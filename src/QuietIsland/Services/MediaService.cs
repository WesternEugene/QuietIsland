using System.Windows.Media.Imaging;
using System.Diagnostics;
using System.Buffers.Binary;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using QuietIsland.Models;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace QuietIsland.Services;

public readonly record struct AudioFrame(float[] Spectrum, float Peak);

public sealed class MediaService : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? session;
    private WasapiLoopbackCapture? loopback;
    private MMDevice? captureDevice;
    private string preferredOutput = "", capturedOutput = "";
    private bool audioStarted, analysisRequested, disposed;
    private volatile bool analysisEnabled = true, economyMode;
    private volatile bool captureStopped;
    private DateTime audioRetry;
    private readonly object audioGate = new();
    private readonly float[] samples = new float[1024];
    private int sampleIndex;
    private readonly float[] spectrum = new float[32];
    private static readonly float[] Silence = new float[32];
    private long audioTimestamp, analysisTimestamp;
    private float peak;
    private string lastKey = "";
    private long pollGeneration;
    private string preferredSource = "";
    private BitmapSource? cover;
    public MediaSnapshot Current { get; private set; } = MediaSnapshot.Empty;
    public string PreferredSource { get => preferredSource; set { if (preferredSource == value) return; preferredSource = value; Interlocked.Increment(ref pollGeneration); } }
    public bool EconomyMode
    {
        get => economyMode;
        set { if (economyMode != value) { economyMode = value; Interlocked.Exchange(ref analysisTimestamp, 0); } }
    }
    public string Status { get; private set; } = "Подключение к медиасессиям…";
    public IReadOnlyList<string> Sources => manager?.GetSessions().Select(x => x.SourceAppUserModelId).ToList() ?? [];
    public float Peak { get { lock (audioGate) return IsFresh ? peak : 0; } }
    public float[] Spectrum => ReadAudio().Spectrum;
    private bool IsFresh => audioTimestamp != 0 && Stopwatch.GetElapsedTime(audioTimestamp).TotalMilliseconds < 220;
    public AudioFrame ReadAudio()
    {
        lock (audioGate) return IsFresh ? new((float[])spectrum.Clone(), peak) : new(Silence, 0);
    }
    public event Action? Changed;
    public async Task StartAsync()
    {
        try { manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync(); Status = "Медиасессии Windows подключены"; }
        catch (Exception e) { Status = "Медиасессии Windows недоступны"; Log.Write("Media init", e); }
        StartAudio();
        await PollAsync();
    }
    public void StartAudio(string? deviceId = null)
    {
        if (disposed) return;
        preferredOutput = deviceId ?? ""; audioStarted = true; analysisRequested = false;
        StopAudio();
        ClearAudio();
        ReconcileAnalysis();
    }
    public void SetAnalysisEnabled(bool enabled)
    {
        if (disposed || analysisEnabled == enabled) return;
        analysisEnabled = enabled;
        ReconcileAnalysis();
    }
    private void ReconcileAnalysis()
    {
        if (disposed || !audioStarted || !analysisEnabled || !Current.IsPlaying)
        {
            analysisRequested = false;
            StopAudio(); ClearAudio();
            return;
        }
        if (!analysisRequested) { analysisRequested = true; audioRetry = default; }
        FollowOutput();
    }
    private void ClearAudio()
    {
        lock (audioGate) { sampleIndex = 0; audioTimestamp = 0; peak = 0; Array.Clear(spectrum); }
        Interlocked.Exchange(ref analysisTimestamp, 0);
    }
    private void StartCapture()
    {
        audioRetry = DateTime.Now.AddSeconds(3);
        StopAudio(); ClearAudio();
        if (disposed || !audioStarted || !analysisEnabled || !Current.IsPlaying) return;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            captureDevice = string.IsNullOrEmpty(preferredOutput) ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : enumerator.GetDevice(preferredOutput);
            capturedOutput = captureDevice.ID;
            loopback = new WasapiLoopbackCapture(captureDevice);
            captureStopped = false;
            loopback.DataAvailable += Audio;
            loopback.RecordingStopped += AudioStopped;
            loopback.StartRecording();
        }
        catch (Exception e) { StopAudio(); Log.Write("Loopback", e); }
    }
    private void StopAudio()
    {
        var previous = loopback; loopback = null;
        if (previous != null) { previous.DataAvailable -= Audio; previous.RecordingStopped -= AudioStopped; try { previous.StopRecording(); } catch { } previous.Dispose(); }
        captureDevice?.Dispose(); captureDevice = null; capturedOutput = "";
    }
    private void FollowOutput()
    {
        if (disposed || !audioStarted || !analysisEnabled || !Current.IsPlaying || DateTime.Now < audioRetry) return;
        audioRetry = DateTime.Now.AddSeconds(3);
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var output = preferredOutput.Length == 0 ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : enumerator.GetDevice(preferredOutput);
            if (loopback == null || captureStopped || output.ID != capturedOutput || output.State != DeviceState.Active) StartCapture();
        }
        catch { if (loopback != null) StopAudio(); }
    }
    private void Audio(object? sender, WaveInEventArgs e)
    {
        if (!analysisEnabled || disposed || !Current.IsPlaying || !ReferenceEquals(sender, loopback)) return;
        var format = (sender as WasapiLoopbackCapture)?.WaveFormat;
        if (format == null) return;
        var now = Stopwatch.GetTimestamp();
        var previous = Interlocked.Read(ref analysisTimestamp);
        // Stay below ReadAudio's 220 ms freshness window while avoiding FFT work on every callback.
        if (economyMode && previous != 0 && Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds < 130) return;
        Interlocked.Exchange(ref analysisTimestamp, now);
        ProcessAudioCore(e.Buffer.AsSpan(0, e.BytesRecorded), format, sender);
    }
    private void AudioStopped(object? sender, StoppedEventArgs e)
    {
        if (!ReferenceEquals(sender, loopback)) return;
        captureStopped = true;
        if (e.Exception != null) Log.Write("Loopback stopped", e.Exception);
    }
    internal void ProcessAudio(ReadOnlySpan<byte> buffer, WaveFormat format)
        => ProcessAudioCore(buffer, format, null);
    private void ProcessAudioCore(ReadOnlySpan<byte> buffer, WaveFormat format, object? source)
    {
        var channels = format.Channels;
        var bytes = format.BitsPerSample / 8;
        if (channels <= 0 || bytes is < 1 or > 4 || buffer.Length < channels * bytes) return;
        var floating = format.Encoding == WaveFormatEncoding.IeeeFloat || format is WaveFormatExtensible extended && extended.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71");
        var nextPeak = 0f;
        lock (audioGate)
        {
            // A callback already in flight must not repopulate the spectrum after suspension.
            // The direct synthetic-PCM fixture path intentionally bypasses runtime capture policy.
            if (source != null && (!analysisEnabled || disposed || !Current.IsPlaying || !ReferenceEquals(source, loopback))) return;
            if (!IsFresh) { sampleIndex = 0; Array.Clear(spectrum); }
            for (var offset = 0; offset + bytes * channels <= buffer.Length; offset += bytes * channels)
            {
                var sum = 0f; var dominant = 0f;
                for (var channel = 0; channel < channels; channel++)
                {
                    var sample = buffer.Slice(offset + channel * bytes, bytes);
                    var value = floating && bytes == 4 ? BitConverter.ToSingle(sample) : bytes switch
                    {
                        1 => (sample[0] - 128) / 128f,
                        2 => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768f,
                        3 => ((sample[0] | sample[1] << 8 | sample[2] << 16) << 8 >> 8) / 8388608f,
                        4 => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648f,
                        _ => 0
                    };
                    if (!float.IsFinite(value)) value = 0;
                    value = Math.Clamp(value, -1, 1); sum += value;
                    if (Math.Abs(value) > Math.Abs(dominant)) dominant = value;
                }
                var mixed = sum / channels;
                // Preserve energy when a stereo source has opposite channel polarity.
                var valueMixed = Math.Abs(mixed) >= Math.Abs(dominant) * .2f ? mixed : dominant;
                nextPeak = Math.Max(nextPeak, Math.Abs(dominant));
                samples[sampleIndex++] = valueMixed;
                if (sampleIndex != samples.Length) continue;
                sampleIndex = 0;
                var fft = new Complex[1024];
                for (var i = 0; i < fft.Length; i++) fft[i].X = samples[i] * (float)FastFourierTransform.HammingWindow(i, fft.Length);
                FastFourierTransform.FFT(true, 10, fft);
                for (var i = 0; i < spectrum.Length; i++)
                {
                    var a = Math.Clamp((int)Math.Pow(500, i / 32.0), 1, 500);
                    var b = Math.Clamp((int)Math.Pow(500, (i + 1) / 32.0), a + 1, 512);
                    float energy = 0;
                    for (var k = a; k < b; k++) energy = Math.Max(energy, MathF.Sqrt(fft[k].X * fft[k].X + fft[k].Y * fft[k].Y));
                    spectrum[i] = Math.Clamp(energy * 24, 0, 1);
                }
            }
            peak = Math.Clamp(nextPeak, 0, 1); audioTimestamp = Stopwatch.GetTimestamp();
        }
    }
    public async Task PollAsync()
    {
        if (disposed) return;
        var generation = Interlocked.Increment(ref pollGeneration);
        bool Fresh() => !disposed && generation == Interlocked.Read(ref pollGeneration);
        if (manager == null) { ReconcileAnalysis(); return; }
        try
        {
            var selectedSession = ResolveSession();
            if (selectedSession == null) { if (Fresh()) { session = null; Current = MediaSnapshot.Empty; lastKey = ""; cover = null; ReconcileAnalysis(); Changed?.Invoke(); } return; }
            var properties = await selectedSession.TryGetMediaPropertiesAsync();
            if (!Fresh()) return;
            var playing = selectedSession.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var timeline = selectedSession.GetTimelineProperties();
            var key = string.Join("\n", selectedSession.SourceAppUserModelId, properties.Title, properties.Artist, properties.AlbumTitle);
            var selectedCover = lastKey == key ? cover : null;
            if (lastKey != key && properties.Thumbnail != null)
            {
                using var randomStream = await properties.Thumbnail.OpenReadAsync();
                if (!Fresh()) return;
                using var stream = randomStream.AsStreamForRead();
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.DecodePixelWidth = 240; bitmap.EndInit(); bitmap.Freeze(); selectedCover = bitmap;
            }
            if (!Fresh()) return;
            var position = timeline.Position.TotalSeconds;
            if (playing) position += Math.Max(0, (DateTimeOffset.Now - timeline.LastUpdatedTime).TotalSeconds);
            session = selectedSession; lastKey = key; cover = selectedCover;
            Current = new(properties.Title, properties.Artist, properties.AlbumTitle, playing, selectedSession.SourceAppUserModelId,
                Math.Min(position, timeline.EndTime.TotalSeconds), timeline.EndTime.TotalSeconds, selectedCover, properties.PlaybackType != global::Windows.Media.MediaPlaybackType.Video);
            ReconcileAnalysis();
            Changed?.Invoke();
        }
        catch (Exception e) { if (!Fresh()) return; Current = Current with { IsPlaying = false }; ReconcileAnalysis(); Log.Write("Media poll", e); Changed?.Invoke(); }
    }
    private GlobalSystemMediaTransportControlsSession? ResolveSession()
    {
        if (manager == null) return session;
        var sessions = manager.GetSessions();
        return sessions.FirstOrDefault(x => x.SourceAppUserModelId == PreferredSource)
            ?? sessions.FirstOrDefault(x => x.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            ?? manager.GetCurrentSession();
    }
    public async Task ToggleAsync() { var target = ResolveSession(); if (target != null) await target.TryTogglePlayPauseAsync(); await PollAsync(); }
    public async Task NextAsync() { var target = ResolveSession(); if (target != null) await target.TrySkipNextAsync(); await PollAsync(); }
    public async Task PreviousAsync() { var target = ResolveSession(); if (target != null) await target.TrySkipPreviousAsync(); await PollAsync(); }
    public async Task SeekAsync(double seconds) { var target = ResolveSession(); if (target != null) await target.TryChangePlaybackPositionAsync(TimeSpan.FromSeconds(seconds).Ticks); await PollAsync(); }
    public void Dispose() { disposed = true; Interlocked.Increment(ref pollGeneration); audioStarted = analysisRequested = false; analysisEnabled = false; StopAudio(); ClearAudio(); }
}
