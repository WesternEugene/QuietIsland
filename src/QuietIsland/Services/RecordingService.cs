using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using QuietIsland.Core;

namespace QuietIsland.Services;

public sealed class RecordingService(DesktopState state) : IDisposable
{
    private readonly SemaphoreSlim transition = new(1, 1);
    private readonly Stopwatch clock = new();
    private readonly object audioGate = new();
    private Process? process;
    private WasapiLoopbackCapture? audio;
    private MMDevice? audioDevice;
    private WaveFileWriter? writer;
    private PcmTimeline? timeline;
    private string? rawPath, audioPath, outputPath, encoderPath;
    private bool gif, audioRecorded, disposed;
    private Task<string>? errors;
    private Task? progress;
    public bool IsRecording { get; private set; }
    public bool IsStarting { get; private set; }
    public bool IsFinalizing { get; private set; }
    public DateTimeOffset Started { get; private set; }
    public string? LastError { get; private set; }
    public string? Warning { get; private set; }
    public string? LastOutputPath { get; private set; }
    public event Action? Changed;

    // Kept for integrations using the original API. UI callers should await StartAsync.
    public void Start(Rectangle bounds, bool asGif = false) => StartAsync(bounds, asGif).GetAwaiter().GetResult();

    public async Task StartAsync(Rectangle bounds, bool asGif = false, CancellationToken cancellationToken = default)
    {
        await transition.WaitAsync(cancellationToken).ConfigureAwait(false);
        Process? running = null;
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (process != null || IsStarting || IsFinalizing) throw new InvalidOperationException("Запись уже идёт или сохраняется.");
            ValidateBounds(bounds);
            IsStarting = true; LastError = null; Warning = null; LastOutputPath = null;
            Changed?.Invoke();
            encoderPath = FfmpegLocator.Resolve(state.Data.Settings.FfmpegPath);
            Directory.CreateDirectory(state.Data.Settings.CaptureFolder);
            gif = asGif;
            var stem = Path.Combine(state.Data.Settings.CaptureFolder, "Island " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss-fff"));
            rawPath = stem + ".video.mp4"; audioPath = stem + ".audio.wav"; outputPath = stem + (gif ? ".gif" : ".mp4");
            var duplication = DesktopDuplication.Find(bounds);
            var useDuplication = duplication != null && await SupportsDuplicationAsync(encoderPath, cancellationToken).ConfigureAwait(false);
            while (true)
            {
            var info = BaseInfo(encoderPath);
            var filter = "pad=ceil(iw/2)*2:ceil(ih/2)*2";
            Add(info, "-y", "-nostats", "-progress", "pipe:1", "-stats_period", "0.1");
            if (useDuplication)
            {
                var output = duplication!.Value;
                Add(info, "-filter_complex", FormattableString.Invariant($"ddagrab=output_idx={output.Index}:framerate={(state.Data.Settings.RecordingFps == 60 ? 60 : 30)}:draw_mouse={(state.Data.Settings.RecordCursor ? 1 : 0)}:video_size={bounds.Width}x{bounds.Height}:offset_x={bounds.X - output.Screen.X}:offset_y={bounds.Y - output.Screen.Y},hwdownload,format=bgra,{filter}"));
            }
            else Add(info, "-f", "gdigrab", "-framerate", state.Data.Settings.RecordingFps == 60 ? "60" : "30",
                "-offset_x", bounds.X.ToString(CultureInfo.InvariantCulture), "-offset_y", bounds.Y.ToString(CultureInfo.InvariantCulture), "-video_size", $"{bounds.Width}x{bounds.Height}",
                "-draw_mouse", state.Data.Settings.RecordCursor ? "1" : "0", "-i", "desktop", "-vf", filter);
            Add(info, "-an", "-c:v", "libx264", "-preset", "ultrafast", "-crf", state.Data.Settings.RecordingHighQuality ? "20" : "26", "-pix_fmt", "yuv420p", rawPath);
            running = Process.Start(info) ?? throw new InvalidOperationException("Не удалось запустить FFmpeg.");
            process = running;
            try
            {
            errors = DrainAsync(running.StandardError);
            var firstFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            progress = ReadProgressAsync(running.StandardOutput, firstFrame);
            clock.Restart(); Started = DateTimeOffset.Now;
            StartAudio();
            var exited = running.WaitForExitAsync();
            Task ready;
            try
            {
                ready = await Task.WhenAny(firstFrame.Task, exited).WaitAsync(TimeSpan.FromSeconds(12), cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw new InvalidOperationException("FFmpeg не передал ни одного кадра за 12 секунд. Проверьте доступ к рабочему столу и поддержку gdigrab/libx264 в вашей сборке FFmpeg.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (ready == exited || running.HasExited)
                throw Failure("FFmpeg не смог начать запись", running.ExitCode, await errors.ConfigureAwait(false));
            break;
            }
            catch (Exception error) when (useDuplication && error is not OperationCanceledException)
            {
                StopAudio(); clock.Stop(); process = null;
                await EndProcessAsync(running, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                if (errors != null) await errors.ConfigureAwait(false);
                if (progress != null) await progress.ConfigureAwait(false);
                running.Dispose(); running = null; useDuplication = false;
                Warning = "Аппаратный захват недоступен. Используется совместимый режим; при мерцании отключите запись курсора в настройках.";
            }
            }
            IsRecording = true; IsStarting = false;
            _ = WatchUnexpectedExitAsync(running!, errors!, progress!);
            Changed?.Invoke();
        }
        catch (Exception error)
        {
            // Validation/concurrent-start failures must not disturb the existing session.
            if (running == null && !IsStarting) throw;
            process = null; IsStarting = false; IsRecording = false;
            StopAudio(); clock.Stop();
            if (running != null)
            {
                await EndProcessAsync(running, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                if (errors != null) await errors.ConfigureAwait(false);
                if (progress != null) await progress.ConfigureAwait(false);
                running.Dispose();
            }
            if (error is OperationCanceledException) { LastError = null; Changed?.Invoke(); throw; }
            var failure = error is InvalidOperationException ? error : new InvalidOperationException(
                error is Win32Exception native ? $"Не удалось запустить FFmpeg «{encoderPath}» (ошибка Windows {native.NativeErrorCode}): {native.Message}"
                    : $"Не удалось начать запись: {error.Message}", error);
            LastError = failure.Message; Changed?.Invoke(); throw failure;
        }
        finally { transition.Release(); }
    }
    private static readonly Dictionary<string, bool> DuplicationSupport = new(StringComparer.OrdinalIgnoreCase);
    private static async Task<bool> SupportsDuplicationAsync(string executable, CancellationToken token)
    {
        var key = executable + File.GetLastWriteTimeUtc(executable).Ticks;
        lock (DuplicationSupport) if (DuplicationSupport.TryGetValue(key, out var known)) return known;
        try
        {
            var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            Add(info, "-hide_banner", "-h", "filter=ddagrab");
            using var probe = Process.Start(info)!;
            var output = probe.StandardOutput.ReadToEndAsync(token); var error = probe.StandardError.ReadToEndAsync(token);
            try { await probe.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(3), token).ConfigureAwait(false); }
            catch { if (!probe.HasExited) probe.Kill(true); throw; }
            var supported = (await output).Contains("output_idx", StringComparison.Ordinal) || (await error).Contains("output_idx", StringComparison.Ordinal);
            lock (DuplicationSupport) { if (DuplicationSupport.Count > 8) DuplicationSupport.Clear(); DuplicationSupport[key] = supported; }
            return supported;
        }
        catch (Exception e) when (e is Win32Exception or IOException or TimeoutException) { return false; }
    }

    public async Task<string?> StopAsync()
    {
        await transition.WaitAsync().ConfigureAwait(false);
        Process? running = null;
        try
        {
            if (process == null) return null;
            running = process; process = null; IsRecording = false; IsFinalizing = true; Changed?.Invoke();
            StopAudio(); clock.Stop();
            var graceful = await EndProcessAsync(running, TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            var detail = errors == null ? "" : await errors.ConfigureAwait(false);
            if (progress != null) await progress.ConfigureAwait(false);
            if (!graceful) throw new InvalidOperationException("FFmpeg не остановился за 20 секунд и был завершён. Исходный файл сохранён: " + rawPath);
            if (running.ExitCode != 0 || rawPath == null || !File.Exists(rawPath) || new FileInfo(rawPath).Length == 0)
                throw Failure("FFmpeg не сохранил запись", running.ExitCode, detail);
            if (gif)
            {
                var info = BaseInfo(encoderPath);
                Add(info, "-y", "-i", rawPath, "-vf", "fps=15,scale=960:-1:flags=lanczos,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse", outputPath!);
                await RunAsync(info).ConfigureAwait(false); File.Delete(rawPath);
            }
            else if (audioRecorded && audioPath != null && File.Exists(audioPath) && new FileInfo(audioPath).Length > 100)
            {
                await MuxCoreAsync(rawPath, audioPath, outputPath!, encoderPath).ConfigureAwait(false);
                File.Delete(rawPath); File.Delete(audioPath);
            }
            else File.Move(rawPath, outputPath!, true);
            LastOutputPath = outputPath;
            return outputPath;
        }
        catch (Exception error)
        {
            var failure = error is InvalidOperationException ? error : new InvalidOperationException($"Не удалось сохранить запись: {error.Message}. Исходный файл: {rawPath}", error);
            LastError = failure.Message; throw failure;
        }
        finally
        {
            running?.Dispose();
            if (running != null) { IsFinalizing = false; Changed?.Invoke(); }
            transition.Release();
        }
    }

    private async Task WatchUnexpectedExitAsync(Process running, Task<string> errorOutput, Task progressOutput)
    {
        try
        {
            await running.WaitForExitAsync().ConfigureAwait(false);
            var detail = await errorOutput.ConfigureAwait(false); await progressOutput.ConfigureAwait(false);
            await transition.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!ReferenceEquals(process, running)) return; // StopAsync or Dispose owns cleanup.
                process = null; IsRecording = false; StopAudio(); clock.Stop();
                LastError = Failure("FFmpeg неожиданно завершил запись", running.ExitCode, detail).Message;
                running.Dispose(); Changed?.Invoke();
            }
            finally { transition.Release(); }
        }
        catch (Exception error) when (error is ObjectDisposedException or InvalidOperationException) { /* Explicit stop already disposed the process. */ }
    }

    private void StartAudio()
    {
        audioRecorded = false;
        if (!state.Data.Settings.RecordSystemAudio || gif) return;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            audioDevice = string.IsNullOrEmpty(state.Data.Settings.AudioOutputId) ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : enumerator.GetDevice(state.Data.Settings.AudioOutputId);
            audio = new WasapiLoopbackCapture(audioDevice); writer = new WaveFileWriter(audioPath!, audio.WaveFormat);
            timeline = new PcmTimeline(writer, audio.WaveFormat.SampleRate, audio.WaveFormat.BlockAlign);
            audio.DataAvailable += (_, e) => { lock (audioGate) timeline?.Append(e.Buffer.AsSpan(0, e.BytesRecorded), clock.Elapsed); };
            audio.StartRecording(); audioRecorded = true;
        }
        catch (Exception error)
        {
            StopAudio(); audioRecorded = false;
            Warning = "Системный звук недоступен; видео записывается без звука. Проверьте выбранное устройство вывода в настройках.";
            Log.Write("Recording audio", error);
        }
    }

    private void StopAudio()
    {
        var capture = audio; audio = null;
        try { capture?.StopRecording(); }
        catch (Exception error) { audioRecorded = false; Log.Write("Recording audio stop", error); }
        finally
        {
            try { capture?.Dispose(); audioDevice?.Dispose(); audioDevice = null; }
            finally
            {
                lock (audioGate)
                {
                    try { timeline?.FillTo(clock.Elapsed); }
                    catch (Exception error) { audioRecorded = false; Log.Write("Recording audio timeline", error); }
                    finally { timeline = null; writer?.Dispose(); writer = null; }
                }
            }
        }
    }

    private static void ValidateBounds(Rectangle bounds)
    {
        var screen = System.Windows.Forms.SystemInformation.VirtualScreen;
        if (bounds.Width <= 0 || bounds.Height <= 0 || bounds.X < screen.X || bounds.Y < screen.Y
            || (long)bounds.X + bounds.Width > (long)screen.X + screen.Width || (long)bounds.Y + bounds.Height > (long)screen.Y + screen.Height)
            throw new InvalidOperationException("Область записи должна находиться внутри рабочего стола и иметь ненулевой размер. Выберите область ещё раз.");
    }

    private ProcessStartInfo BaseInfo(string? executable = null)
    {
        var info = new ProcessStartInfo(executable ?? FfmpegLocator.Resolve(state.Data.Settings.FfmpegPath))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
            RedirectStandardOutput = true, RedirectStandardInput = true, WindowStyle = ProcessWindowStyle.Hidden
        };
        Add(info, "-hide_banner", "-loglevel", "error");
        return info;
    }

    internal Task MuxAsync(string video, string sound, string destination) => MuxCoreAsync(video, sound, destination);
    private async Task MuxCoreAsync(string video, string sound, string destination, string? executable = null)
    {
        var info = BaseInfo(executable);
        Add(info, "-y", "-i", video, "-i", sound, "-c:v", "copy", "-c:a", "aac", "-af", "apad", "-shortest", "-movflags", "+faststart", destination);
        await RunAsync(info).ConfigureAwait(false);
    }

    private InvalidOperationException Failure(string message, int exitCode, string detail)
    {
        var tail = detail.Trim(); if (tail.Length > 2400) tail = tail[^2400..];
        var recovery = rawPath != null && File.Exists(rawPath) ? "\nИсходный файл сохранён: " + rawPath : "";
        return new InvalidOperationException($"{message} (код {exitCode}). Проверьте поддержку gdigrab/libx264 и папку сохранения в настройках."
            + (tail.Length > 0 ? "\n" + tail : "") + recovery);
    }

    private static void Add(ProcessStartInfo info, params string[] args) { foreach (var arg in args) info.ArgumentList.Add(arg); }
    private static async Task ReadProgressAsync(StreamReader reader, TaskCompletionSource firstFrame)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            if (line.StartsWith("frame=", StringComparison.Ordinal) && long.TryParse(line.AsSpan(6).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var frame) && frame > 0)
                firstFrame.TrySetResult();
    }
    private static async Task<string> DrainAsync(StreamReader reader)
    {
        var tail = "";
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line) { tail += line + "\n"; if (tail.Length > 8000) tail = tail[^4000..]; }
        return tail;
    }
    private static async Task<bool> EndProcessAsync(Process running, TimeSpan timeout)
    {
        if (!running.HasExited)
        {
            try { await running.StandardInput.WriteLineAsync("q").ConfigureAwait(false); await running.StandardInput.FlushAsync().ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or InvalidOperationException) { }
        }
        using var deadline = new CancellationTokenSource(timeout);
        try { await running.WaitForExitAsync(deadline.Token).ConfigureAwait(false); return true; }
        catch (OperationCanceledException)
        {
            if (!running.HasExited) running.Kill(true);
            await running.WaitForExitAsync().ConfigureAwait(false); return false;
        }
    }
    private static async Task RunAsync(ProcessStartInfo info)
    {
        using var running = Process.Start(info) ?? throw new InvalidOperationException("Не удалось запустить FFmpeg для обработки записи.");
        var errorOutput = DrainAsync(running.StandardError); var output = DrainAsync(running.StandardOutput);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try { await running.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            if (!running.HasExited) running.Kill(true);
            await running.WaitForExitAsync().ConfigureAwait(false); await errorOutput.ConfigureAwait(false); await output.ConfigureAwait(false);
            throw new InvalidOperationException("Обработка записи превысила 5 минут. Исходный файл сохранён.");
        }
        var detail = await errorOutput.ConfigureAwait(false); await output.ConfigureAwait(false);
        if (running.ExitCode != 0) throw new InvalidOperationException("Не удалось обработать запись. Исходный файл сохранён рядом с результатом.\n" + detail.Trim());
    }
    public void Dispose()
    {
        transition.Wait();
        try
        {
            if (disposed) return;
            disposed = true;
            var running = process; process = null; IsRecording = false; IsStarting = false;
            StopAudio(); clock.Stop();
            if (running != null)
            {
                EndProcessAsync(running, TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
                running.Dispose();
            }
        }
        finally { transition.Release(); }
    }
}
