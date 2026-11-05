using System.Reflection;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.Wave;
using QuietIsland.Models;
using QuietIsland.Services;
using QuietIsland.Views;

internal static class ResourceChecks
{
    private delegate void AudioProcessor(ReadOnlySpan<byte> buffer, WaveFormat format);

    public static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "quiet-island-resources-" + Guid.NewGuid().ToString("N"));
        using var context = new IslandContext(directory);
        Directory.CreateDirectory(directory);
        context.Settings.AiEndpoint = "invalid endpoint that must never be used";
        context.Settings.AiModel = "fixture-model";
        foreach (var credential in new[] { "", "invalid-dpapi", DesktopState.Protect("   ") })
        {
            context.Settings.AiKeyEncrypted = credential;
            var name = credential.Length == 0 ? "absent" : credential == "invalid-dpapi" ? "unreadable" : "blank";
            await check("ai_rejects_" + name + "_key_before_request", async () =>
            {
                await RequiresKeyAsync(() => context.Ai.ModelsAsync());
                await RequiresKeyAsync(() => context.Ai.CompleteAsync([new() { Text = "fixture", Images = [Path.Combine(directory, "missing.png")] }]));
            });
        }
        context.Settings.AiKeyEncrypted = "";
        await check("ai_without_key_does_not_build_chat_or_accept_input", () =>
        {
            context.State.Data.Chat.Add(new() { Text = "Historical content must remain saved, without creating chat cards." });
            using var view = new AiView(context);
            view.Measure(new Size(680, 330)); view.Arrange(new Rect(0, 0, 680, 330)); view.UpdateLayout();
            Require(!Descendants(view).OfType<TextBox>().Any(x => x.IsEnabled), "AI input is enabled without an API key.");
            Require(!Descendants(view).OfType<TextBlock>().Any(x => x.Text.StartsWith("Historical content")), "Unavailable chat renders its entire history.");
            Require(Descendants(view).OfType<Button>().Any(x => Equals(x.ToolTip, "Настройки ИИ") && x.IsEnabled), "Unavailable AI has no working settings action.");
            Require(context.State.Data.Chat.Count == 1, "Disabling AI deleted chat data.");
            return Task.CompletedTask;
        });
        await check("text_ai_actions_disabled_without_key", () =>
        {
            var main = new QuietIsland.MainWindow(context, true);
            try
            {
                using var view = new ToolsView(context, main);
                view.Measure(new Size(680, 330)); view.Arrange(new Rect(0, 0, 680, 330)); view.UpdateLayout();
                var buttons = Descendants(view).OfType<Button>().Where(x => x.ToolTip is string label && label.StartsWith("✧ ")).ToArray();
                Require(buttons.Length == 5 && buttons.All(x => !x.IsEnabled), $"Expected five disabled text AI actions; found {buttons.Length}, enabled {buttons.Count(x => x.IsEnabled)}.");
                Require(!((Button)main.FindName("DictationButton")).IsEnabled, "Default dictation is available without local opt-in or remote credentials.");
            }
            finally { main.Close(); }
            return Task.CompletedTask;
        });
        await check("unconfigured_dictation_does_not_enter_listening", () =>
        {
            Require(!context.Settings.SpeechPreferLocal, "Windows speech is enabled by default.");
            context.Settings.SpeechPreferLocal = false;
            try { context.Speech.Start(); throw new Exception("Dictation started without configuration."); }
            catch (InvalidOperationException error) { Require(error.Message.Contains("Диктовка"), "Dictation did not explain where to configure it."); }
            Require(!context.Speech.IsListening && !context.Speech.IsTranscribing, "Failed dictation kept an active session.");
            Require(!Directory.EnumerateFiles(directory, "dictation-*.wav").Any(), "Unconfigured dictation created a recording.");
            return Task.CompletedTask;
        });
        await check("ai_controls_follow_key_changes_in_existing_views", async () =>
        {
            using var search = new SearchView(context);
            using var chat = new AiView(context);
            search.Measure(new Size(680, 330)); search.Arrange(new Rect(0, 0, 680, 330)); search.UpdateLayout();
            chat.Measure(new Size(680, 330)); chat.Arrange(new Rect(0, 0, 680, 330)); chat.UpdateLayout();
            var shortcut = Descendants(search).OfType<Button>().Single(x => Equals(x.ToolTip, "✧ Спросить ИИ"));
            var input = Descendants(chat).OfType<TextBox>().Single();
            var query = (TextBox)typeof(SearchView).GetField("query", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(search)!;
            query.Text = "? fixture";
            await Task.Delay(180);
            Require(!shortcut.IsEnabled && !input.IsEnabled, "AI is enabled before configuration.");
            context.Settings.AiKeyEncrypted = DesktopState.Protect("fixture-key"); context.State.Touch();
            await Task.Delay(180);
            Require(shortcut.IsEnabled && input.IsEnabled, "Existing search/chat did not enable AI after adding a key.");
            Require(!Descendants(search).OfType<TextBlock>().Any(x => x.Text == "ИИ отключён · добавьте API ключ"), "Existing AI search still shows a stale configuration prompt.");
            context.Settings.AiKeyEncrypted = ""; context.State.Touch();
            await Task.Delay(180);
            Require(!shortcut.IsEnabled && !input.IsEnabled, "Existing search/chat did not disable AI after removing the key.");
            Require(Descendants(search).OfType<TextBlock>().Any(x => x.Text == "ИИ отключён · добавьте API ключ"), "Existing AI query did not return to its settings action.");
        });
        await check("chat_send_stays_disabled_during_streaming_response", async () =>
        {
            var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start();
            var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
            using var server = new HttpListener(); server.Prefixes.Add($"http://localhost:{port}/"); server.Start();
            var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = Task.Run(async () =>
            {
                var request = await server.GetContextAsync();
                using var reader = new StreamReader(request.Request.InputStream); var payload = await reader.ReadToEndAsync();
                Require(payload.Contains("fixture-prompt") && request.Request.Headers["Authorization"] == "Bearer fixture-key", "Authorized chat lost its streamed JSON payload or credentials.");
                received.SetResult(); await release.Task;
                var bytes = Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\"fixture-answer\"}}]}\n\ndata: [DONE]\n\n");
                request.Response.ContentType = "text/event-stream"; request.Response.ContentLength64 = bytes.Length;
                await request.Response.OutputStream.WriteAsync(bytes); request.Response.Close();
            });
            context.Settings.AiEndpoint = $"http://localhost:{port}/v1"; context.Settings.AiKeyEncrypted = DesktopState.Protect("fixture-key");
            using var chat = new AiView(context);
            chat.Measure(new Size(680, 330)); chat.Arrange(new Rect(0, 0, 680, 330)); chat.UpdateLayout();
            var input = Descendants(chat).OfType<TextBox>().Single(); input.Text = "fixture-prompt";
            var send = Descendants(chat).OfType<Button>().Single(x => Equals(x.ToolTip, "Отправить ↑"));
            var sending = (Task)typeof(AiView).GetMethod("SendAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(chat, null)!;
            bool disabled;
            try { await received.Task.WaitAsync(TimeSpan.FromSeconds(5)); disabled = !send.IsEnabled; }
            finally { release.TrySetResult(); }
            await sending.WaitAsync(TimeSpan.FromSeconds(5)); await provider.WaitAsync(TimeSpan.FromSeconds(5));
            Require(disabled, "Send became enabled before the response finished.");
            Require(send.IsEnabled && context.State.Data.Chat.Last().Text == "fixture-answer", "Completed response did not restore Send or preserve its final text.");
            context.Settings.AiKeyEncrypted = "";
        });
        foreach (var (width, height) in new[] { (3840, 2160), (2160, 3840) })
            await check($"clipboard_preview_is_bounded_{width}x{height}", () =>
            {
                var path = Path.Combine(directory, $"image-{width}.png");
                var pixels = new byte[width * height * 4];
                var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
                CaptureService.SavePng(source, path);
                var originalSize = new FileInfo(path).Length;
                context.State.Data.Clipboard.Clear(); context.State.Data.Clipboard.Add(new() { Kind = "image", ImagePath = path });
                using var view = new ClipboardView(context);
                view.Measure(new Size(680, 330)); view.Arrange(new Rect(0, 0, 680, 330)); view.UpdateLayout();
                var preview = Descendants(view).OfType<Image>().Select(x => x.Source).OfType<BitmapSource>().Single();
                Console.WriteLine($"METRIC clipboard {width}x{height}: decoded {preview.PixelWidth}x{preview.PixelHeight}");
                Require(preview.PixelWidth <= 320 && preview.PixelHeight <= 180, "Clipboard preview decoded the full image.");
                Require(preview.IsFrozen, "Clipboard thumbnail is not immutable.");
                using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                Require(new FileInfo(path).Length == originalSize, "Creating a preview changed the original image.");
                return Task.CompletedTask;
            });
        await check("fft_analysis_reuses_work_buffers", () =>
        {
            using var media = new MediaService();
            var process = typeof(MediaService).GetMethod("ProcessAudio", BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<AudioProcessor>(media);
            var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
            var pcm = new byte[2048 * 8];
            for (var i = 0; i < 2048; i++) BitConverter.TryWriteBytes(pcm.AsSpan(i * 8, 4), .5f * (float)Math.Sin(i * 2 * Math.PI * 440 / 48000));
            process(pcm, format);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 64; i++) process(pcm, format);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Console.WriteLine($"METRIC FFT: {allocated} bytes allocated for 64 PCM blocks");
            Require(allocated < 16384, "Audio analysis allocates a fresh FFT buffer for every block.");
            Require(media.Peak > .4 && media.Spectrum.Any(x => x > .1), "Buffer reuse lost audio energy.");
            process(new byte[pcm.Length], format);
            Require(media.Peak == 0 && media.Spectrum.All(x => x == 0), "Reused FFT data leaks into silence.");
            return Task.CompletedTask;
        });
    }

    private static async Task RequiresKeyAsync(Func<Task> action)
    {
        try { await action(); throw new Exception("AI operation succeeded without credentials."); }
        catch (InvalidOperationException error) { Require(error.Message.Contains("ключ", StringComparison.OrdinalIgnoreCase), "AI prepared a request before checking credentials: " + error.Message); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var item in Descendants(child)) yield return item;
        }
    }
}
