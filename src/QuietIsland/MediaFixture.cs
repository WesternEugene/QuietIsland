using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using NAudio.Wave;
using QuietIsland.Services;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuietIsland.Controls;

namespace QuietIsland;
internal static class MediaFixture
{
    private const string Title = "Quiet Island diagnostic playback";
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    public static async Task RunPlayerAsync(string directory)
    {
        Directory.CreateDirectory(directory); SetCurrentProcessExplicitAppUserModelID("QuietIsland.DiagnosticPlayer");
        var path = Path.Combine(directory, "silent-fixture.wav");
        using (var writer = new WaveFileWriter(path, new WaveFormat(22050, 16, 1)))
        { var silence = new byte[22050 * 2]; for (var i = 0; i < 12; i++) writer.Write(silence, 0, silence.Length); }
        using var source = MediaSource.CreateFromStorageFile(await StorageFile.GetFileFromPathAsync(path));
        var item = new MediaPlaybackItem(source); var properties = item.GetDisplayProperties(); properties.Type = MediaPlaybackType.Music; properties.MusicProperties.Title = Title; properties.MusicProperties.Artist = "Quiet Island test"; item.ApplyDisplayProperties(properties);
        var coverPath = Path.Combine(directory, "cover-fixture.png");
        var drawing = new System.Windows.Controls.Border { Width = 80, Height = 80, Background = Brushes.Coral, Child = Icons.Make("music", 40) };
        CaptureService.SavePng(Diagnostics.Render(drawing, 80, 80), coverPath);
        properties.Thumbnail = global::Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(await StorageFile.GetFileFromPathAsync(coverPath)); item.ApplyDisplayProperties(properties);
        using var player = new global::Windows.Media.Playback.MediaPlayer { Source = item, Volume = 0 };
        player.CommandManager.IsEnabled = true; player.Play();
        await Task.Delay(5000); player.Pause(); File.WriteAllText(Path.Combine(directory, "paused"), "");
        await Task.Delay(3000);
    }
    public static async Task<object> CheckAsync(IslandContext c, string directory)
    {
        Directory.CreateDirectory(directory); var pausedFile = Path.Combine(directory, "paused"); if (File.Exists(pausedFile)) File.Delete(pausedFile);
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        info.ArgumentList.Add("--media-fixture"); info.ArgumentList.Add(directory);
        using var child = Process.Start(info)!;
        bool playing = false, paused = false, waveMoves = false, stillOnPause = false;
        bool silenceStaysStill = false, staleAudioExpires = false, amplitudeFollowsAudio = false, spectrumFollowsAudio = false, coverDetected = false, lowerCornersReact = false;
        using var audio = new MediaService();
        void CopySession() => typeof(MediaService).GetProperty(nameof(MediaService.Current))!.SetValue(audio, c.Media.Current);
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        byte[] Signal(float amplitude, double frequency)
        {
            var bytes = new byte[2048 * 8];
            for (var i = 0; i < 2048; i++)
            {
                // Right-channel-only music also has to drive the visualizer.
                BitConverter.TryWriteBytes(bytes.AsSpan(i * 8 + 4, 4), amplitude * (float)Math.Sin(i * 2 * Math.PI * frequency / 48000));
            }
            return bytes;
        }
        async Task Feed(float amplitude, double frequency, int duration)
        {
            var signal = Signal(amplitude, frequency); var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < duration) { audio.ProcessAudio(signal, format); await Task.Delay(20); }
        }
        var surface = new LiquidSurface { Width = 264, Height = 54, VisualWidth = 264, VisualHeight = 54 };
        using var visualSource = new HwndSource(new HwndSourceParameters("QuietIsland.LiquidCheck") { ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 264, Height = 54 });
        visualSource.RootVisual = surface;
        try
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < 4.5 && !child.HasExited)
            {
                await c.Media.PollAsync();
                if (c.Media.Current.Title == Title && c.Media.Current.IsPlaying) { playing = true; break; }
                await Task.Delay(200);
            }
            CopySession(); surface.Update(audio); coverDetected = audio.Current.Cover != null;
            var silentFrame = Pixels(surface); await Task.Delay(180); silenceStaysStill = silentFrame.SequenceEqual(Pixels(surface));
            await Feed(.015f, 140, 300); var quietLevel = surface.AudioLevelForChecks;
            await Feed(.75f, 140, 320); var loudLevel = surface.AudioLevelForChecks; var lowSpectrum = audio.Spectrum;
            amplitudeFollowsAudio = loudLevel > quietLevel * 5 && loudLevel > .4;
            var playingFrame = Pixels(surface); await Feed(.75f, 2400, 200); waveMoves = !playingFrame.SequenceEqual(Pixels(surface));
            spectrumFollowsAudio = lowSpectrum.Zip(audio.Spectrum).Sum(x => Math.Abs(x.First - x.Second)) > 1;
            bool CornerChanged(byte[] active, int left, int right)
            {
                for (var y = 24; y < 54; y++) for (var x = left; x < right; x++)
                    for (var channel = 0; channel < 4; channel++) if (silentFrame[(y * 264 + x) * 4 + channel] != active[(y * 264 + x) * 4 + channel]) return true;
                return false;
            }
            await Feed(.75f, 15000, 200);
            lowerCornersReact = CornerChanged(playingFrame, 17, 38) && CornerChanged(Pixels(surface), 226, 247);
            for (var i = 0; i < 8; i++) { audio.ProcessAudio(ReadOnlySpan<byte>.Empty, format); await Task.Delay(40); }
            staleAudioExpires = audio.Peak == 0 && audio.Spectrum.All(x => x == 0);
            watch.Restart();
            while (!File.Exists(pausedFile) && !child.HasExited && watch.Elapsed.TotalSeconds < 6) await Task.Delay(150);
            for (var i = 0; i < 8; i++)
            {
                await c.Media.PollAsync();
                if (c.Media.Current.Title == Title && !c.Media.Current.IsPlaying) { paused = true; break; }
                await Task.Delay(150);
            }
            CopySession(); surface.Update(audio); var pausedFrame = Pixels(surface); await Task.Delay(160); stillOnPause = pausedFrame.SequenceEqual(Pixels(surface));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await child.WaitForExitAsync(timeout.Token);
            return new { playingDetected = playing, pauseDetected = paused, waveMovesWhilePlaying = playing && waveMoves, silenceStaysStill, amplitudeFollowsAudio, spectrumFollowsAudio, staleAudioExpires, coverDetected, lowerCornersReact, animationStops = paused && !surface.Playing && stillOnPause, helperExit = child.ExitCode, audioFixture = "Synthetic PCM through the production FFT; the native SMTC player remains silent" };
        }
        finally { if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); } await c.Media.PollAsync(); }
    }
    private static byte[] Pixels(LiquidSurface surface)
    {
        surface.Measure(new Size(264,54)); surface.Arrange(new Rect(0,0,264,54)); surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap(264, 54, 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface); var pixels = new byte[264 * 54 * 4]; bitmap.CopyPixels(pixels, 264 * 4, 0); return pixels;
    }
}
