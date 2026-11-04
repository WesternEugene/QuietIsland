using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuietIsland.Controls;
using QuietIsland.Services;

namespace QuietIsland;
public static class Diagnostics
{
    public static async Task RunAsync(string[] args)
    {
        var path = args.FirstOrDefault(x => x.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) ?? Path.Combine(AppContext.BaseDirectory, "diagnostics.json");
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!; Directory.CreateDirectory(directory);
        using var c = new IslandContext(Path.Combine(directory, "diagnostic-state"));
        var results = new Dictionary<string, object>();
        if (args.Contains("--motion-checks")) results["motion"] = await MotionChecks.RunAsync(Path.Combine(directory, "motion-state"));
        if (args.Contains("--ui-checks")) { results["uiRegressions"] = await UiRegressionChecks.RunAsync(c); results["desktopWorkflows"] = await DesktopWorkflowChecks.RunAsync(Path.Combine(directory, "workflow-state")); }
        if (args.Contains("--diagnostics"))
        {
            var probe = Path.Combine(AppContext.BaseDirectory, "quiet-island-probe-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(probe, "Quiet Island integration probe"); var watch = Stopwatch.StartNew(); bool found = false; int count = 0;
                for (var i = 0; i < 8 && !found; i++) { await Task.Delay(400); var files = await c.Everything.SearchAsync(Path.GetFileName(probe)); count = files.Count; found = files.Any(x => x.Path.Equals(probe, StringComparison.OrdinalIgnoreCase)); }
                results["everything"] = new { found, count, milliseconds = watch.ElapsedMilliseconds, status = c.Everything.Status };
            }
            finally { File.Delete(probe); }
            await c.Media.StartAsync(); results["media"] = new { c.Media.Status, c.Media.Current.HasMedia, c.Media.Current.IsPlaying, sources = c.Media.Sources.Count };
            try
            {
                var label = new TextBlock { Text = "Quiet Island 123", FontSize = 38, Foreground = Brushes.Black, Background = Brushes.White, Padding = new Thickness(20), Width = 480, Height = 100 };
                var image = Render(label, 480, 100); var text = await c.Capture.OcrAsync(image); results["ocr"] = new { passed = text.Contains("Quiet") && text.Contains("123"), recognized = text };
            }
            catch (Exception error) { results["ocr"] = new { passed = false, error = error.GetType().Name }; }
            var qr = CaptureService.CreateQr("Quiet Island probe"); results["qr"] = new { passed = CaptureService.DecodeQr(qr) == "Quiet Island probe" };
            results["audio"] = new { outputs = c.System.AudioDevices(NAudio.CoreAudioApi.DataFlow.Render).Count, inputs = c.System.AudioDevices(NAudio.CoreAudioApi.DataFlow.Capture).Count };
            await c.Devices.RefreshAsync(); var radios = c.Devices.Current;
            results["radiosReadOnly"] = new { radios.WifiAvailable, radios.WifiOn, radios.BluetoothAvailable, radios.BluetoothOn, writesAttempted = false };
            await c.Happ.RefreshAsync(); results["happReadOnly"] = new { c.Happ.Current.Available, c.Happ.Current.Connected, c.Happ.Current.CanToggle, c.Happ.Current.Detail, writesAttempted = false };
            await NetworkChecks(c, results);
            try { results["musicLifecycle"] = await MediaFixture.CheckAsync(c, Path.Combine(directory, "media-fixture")); }
            catch (Exception error) { results["musicLifecycle"] = new { passed = false, error = error.GetType().Name }; }
            try { results["recordingMux"] = await CheckMuxAsync(c, directory); }
            catch (Exception error) { results["recordingMux"] = new { passed = false, error = error.GetType().Name }; }
            try { var start = new ProcessStartInfo(FfmpegLocator.Resolve(c.Settings.FfmpegPath)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }; start.ArgumentList.Add("-version"); using var process = Process.Start(start)!; var output = await process.StandardOutput.ReadToEndAsync(); await process.WaitForExitAsync(); results["ffmpeg"] = new { available = process.ExitCode == 0, version = output.Split('\n')[0].Trim() }; } catch { results["ffmpeg"] = new { available = false }; }
        }
        if (args.Contains("--render"))
        {
            if (c.Weather.Current == null)
            {
                c.Settings.WeatherCity = "Пример прогноза";
                typeof(WeatherService).GetProperty(nameof(WeatherService.Current))!.SetValue(c.Weather, new WeatherForecast(1, -1, 3, 6, 96, [], [], DateTimeOffset.Now));
                results["weatherRenderData"] = "sample";
            }
            var window = new MainWindow(c, true); var folder = Path.Combine(directory, "previews"); Directory.CreateDirectory(folder);
            BitmapSource CompactImage()
            {
                var image = Render(window.RenderRoot, 744, 430); var width = (int)Math.Ceiling(window.NotchWidth);
                return new CroppedBitmap(image, new Int32Rect((744 - width) / 2, 0, width, (int)Math.Ceiling(window.NotchHeight + 6)));
            }
            CaptureService.SavePng(CompactImage(), Path.Combine(folder, "compact.png"));
            c.Notify("Готово: длинное название файла расширяет чёлку по ширине текста"); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            CaptureService.SavePng(CompactImage(), Path.Combine(folder, "compact-long-text.png"));
            c.Notify(""); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            foreach (var tab in new[] { "home", "search", "media", "clipboard", "shelf", "notes", "tools", "weather", "ai", "control", "activity" })
            {
                window.ShowPanel(tab, false); await Task.Delay(160); CaptureService.SavePng(Render(window.RenderRoot, 744, 430), Path.Combine(folder, tab + ".png"));
            }
            var settings = new Windows.SettingsWindow(c, window); var settingsRoot = (FrameworkElement)settings.Content; settings.Content = null;
            CaptureService.SavePng(Render(settingsRoot, 860, 710), Path.Combine(folder, "settings.png"));
            settings.SelectCategory("Состав и музыка"); CaptureService.SavePng(Render(settingsRoot, 860, 710), Path.Combine(folder, "settings-music.png")); settings.SelectCategory("Вид острова");
            settings.SelectCategory("Верхняя панель"); CaptureService.SavePng(Render(settingsRoot, 860, 710), Path.Combine(folder, "settings-header.png"));
            settings.SelectCategory("Ресурсы"); CaptureService.SavePng(Render(settingsRoot, 860, 710), Path.Combine(folder, "settings-resources.png")); settings.SelectCategory("Вид острова");
            settings.Content = settingsRoot;
            c.Settings.Theme = "Light"; window.ApplySettings();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            settings.Content = null;
            CaptureService.SavePng(Render(settingsRoot, 860, 710), Path.Combine(folder, "settings-light.png"));
            c.Settings.Theme = "Dark"; window.ApplySettings();
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var fixtureFolder = Path.Combine(directory, "render-fixtures"); Directory.CreateDirectory(fixtureFolder);
            var fixtureFile = Path.Combine(fixtureFolder, "Пример документа.txt"); File.WriteAllText(fixtureFile, "Synthetic shelf preview");
            var fixtureImage = Path.Combine(fixtureFolder, "Пример QR.png"); CaptureService.SavePng(CaptureService.CreateQr("Quiet Island render fixture"), fixtureImage);
            var savedShelf = c.State.Data.Shelf.ToArray(); c.State.Data.Shelf.Clear(); c.State.Data.Shelf.AddRange([fixtureFile, fixtureImage]);
            window.ShowPanel("shelf", false); await Task.Delay(160); CaptureService.SavePng(Render(window.RenderRoot, 744, 430), Path.Combine(folder, "shelf-files.png"));
            c.State.Data.Shelf.Clear(); c.State.Data.Shelf.AddRange(savedShelf);
            typeof(MediaService).GetProperty(nameof(MediaService.Current))!.SetValue(c.Media, new Models.MediaSnapshot("Пример музыки", "Тестовые данные интерфейса", "", true, "fixture", 85, 214, Render(Icons.Tile("music", 180), 180, 180)));
            window.Collapse(); window.RefreshCompactForChecks();
            CaptureService.SavePng(CompactImage(), Path.Combine(folder, "compact-music.png"));
            c.StartTimer(TimeSpan.FromMinutes(5)); c.Notify(""); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); window.RefreshCompactForChecks();
            CaptureService.SavePng(CompactImage(), Path.Combine(folder, "compact-music-timer.png"));
            window.ShowPanel("notes", false); CaptureService.SavePng(Render(window.RenderRoot, 744, 430), Path.Combine(folder, "notes-timer.png")); c.State.Data.TimerEnd = null;
            window.ShowPanel("home", false); ((LiquidSurface)((Grid)window.RenderRoot).Children[0]).Update(c.Media);
            await Task.Delay(160);
            CaptureService.SavePng(Render(window.RenderRoot, 744, 376), Path.Combine(folder, "home-media.png"));
            typeof(MainWindow).GetMethod("TimerFinished", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, ["Таймер завершён"]);
            window.ShowPanel("notes", false); CaptureService.SavePng(Render(window.RenderRoot, 744, 430), Path.Combine(folder, "timer-notice.png"));
            using (var lyricContext = new IslandContext(Path.Combine(directory, "lyric-preview-state"), new LyricsService(new PreviewLyricsHandler())))
            {
                typeof(MediaService).GetProperty(nameof(MediaService.Current))!.SetValue(lyricContext.Media, new Models.MediaSnapshot("Пример музыки", "Демонстрация интерфейса", "", true, "fixture", 85, 214, Render(Icons.Tile("music", 180), 180, 180)));
                lyricContext.Lyrics.UpdateTrack(lyricContext.Media.Current); lyricContext.Lyrics.SetEnabled(true);
                for (var i = 0; i < 20 && lyricContext.Lyrics.Current.Status == LyricsStatus.Loading; i++) await Task.Delay(20);
                var lyricWindow = new MainWindow(lyricContext, true); lyricWindow.ShowPanel("media", false); Render(lyricWindow.RenderRoot, 744, 430);
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                CaptureService.SavePng(Render(lyricWindow.RenderRoot, 744, 430), Path.Combine(folder, "media-lyrics.png")); lyricWindow.Close();
            }
            window.ShowPanel("home", false); var notchRoot = window.RenderRoot; window.Content = null;
            var desktop = new Canvas { Width = 1000, Height = 580, Background = new SolidColorBrush(Color.FromRgb(68, 87, 116)) };
            Canvas.SetLeft(notchRoot, 128); Canvas.SetTop(notchRoot, 0); desktop.Children.Add(notchRoot);
            CaptureService.SavePng(Render(desktop, 1000, 580), Path.Combine(folder, "attached-home.png"));
            results["render"] = new { count = Directory.GetFiles(folder, "*.png").Length, folder, extraFixtures = "Synthetic music metadata, compact clock/timer/cover, appearance controls and owned shelf files" };
        }
        File.WriteAllText(path, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }
    internal static RenderTargetBitmap Render(FrameworkElement element, int width, int height)
    {
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout(); var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element); bitmap.Freeze(); return bitmap;
    }
    private sealed class PreviewLyricsHandler : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.StringContent(JsonSerializer.Serialize(new { syncedLyrics = "[01:16.00]Музыка продолжает звучать\n[01:21.00]Предыдущая строка остаётся рядом\n[01:25.00]Текущая строка выделена\n[01:31.00]Следующая строка появится плавно\n[01:36.00]Это пример для проверки интерфейса", plainLyrics = "Это пример для проверки интерфейса", instrumental = false }), System.Text.Encoding.UTF8, "application/json") });
    }
    private static int FreePort() { var socket = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); socket.Start(); var port = ((System.Net.IPEndPoint)socket.LocalEndpoint).Port; socket.Stop(); return port; }
    private static async Task<object> CheckMuxAsync(IslandContext c, string directory)
    {
        var folder = Path.Combine(directory, "media-fixture"); Directory.CreateDirectory(folder);
        var video = Path.Combine(folder, "video-3s.mp4"); var audio = Path.Combine(folder, "short-audio.wav"); var merged = Path.Combine(folder, "merged-3s.mp4");
        var encoder = FfmpegLocator.Resolve(c.Settings.FfmpegPath);
        await FfmpegAsync(encoder, "-y", "-f", "lavfi", "-i", "color=c=black:s=320x180:r=30:d=3", "-c:v", "libx264", "-pix_fmt", "yuv420p", video);
        using (var writer = new NAudio.Wave.WaveFileWriter(audio, new NAudio.Wave.WaveFormat(8000, 16, 1))) writer.Write(new byte[3200], 0, 3200);
        await c.Recording.MuxAsync(video, audio, merged);
        var output = await FfmpegAsync(encoder, "-i", merged, "-f", "null", "-");
        var match = System.Text.RegularExpressions.Regex.Match(output, @"Duration: (\d+):(\d+):(\d+(?:\.\d+)?)");
        var seconds = match.Success ? int.Parse(match.Groups[1].Value) * 3600 + int.Parse(match.Groups[2].Value) * 60 + double.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
        return new { fullVideoPreserved = seconds is >= 2.9 and <= 3.2, seconds };
    }
    private static async Task<string> FfmpegAsync(string executable, params string[] arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!; var error = process.StandardError.ReadToEndAsync(); var output = process.StandardOutput.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        await output; var text = await error; if (process.ExitCode != 0) throw new InvalidOperationException("FFmpeg fixture failed"); return text;
    }
    private static async Task NetworkChecks(IslandContext c, Dictionary<string, object> results)
    {
        using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        c.Settings.BridgePort = FreePort(); c.Agents.Start();
        try
        {
            var url = $"http://localhost:{c.Settings.BridgePort}";
            using var unauthorized = await client.GetAsync(url + "/health");
            client.DefaultRequestHeaders.Add("X-Island-Token", File.ReadAllText(c.Agents.TokenFile).Trim());
            using var health = await client.GetAsync(url + "/health");
            void DenyFixture() { foreach (var p in c.Agents.Broker.Pending.Where(x => x.Source == "diagnostic")) c.Agents.Broker.Answer(p.Id, "deny"); }
            c.Agents.Broker.Changed += DenyFixture;
            using var content = new System.Net.Http.StringContent("{\"source\":\"diagnostic\",\"title\":\"Test\",\"message\":\"No command runs\",\"options\":[\"allow\",\"deny\"]}", System.Text.Encoding.UTF8, "application/json");
            using var answer = await client.PostAsync(url + "/request", content);
            var response = await answer.Content.ReadAsStringAsync(); c.Agents.Broker.Changed -= DenyFixture;
            results["bridge"] = new { started = health.IsSuccessStatusCode, rejectsUnauthenticated = unauthorized.StatusCode == System.Net.HttpStatusCode.Unauthorized, explicitDenial = response.Contains("deny") };
        }
        catch (Exception error) { results["bridge"] = new { started = false, status = c.Agents.Status, error = error.GetType().Name }; }
        finally { c.Agents.Dispose(); }
        try
        {
            var port = FreePort(); using var server = new System.Net.HttpListener(); server.Prefixes.Add($"http://localhost:{port}/"); server.Start();
            var provider = Task.Run(async () =>
            {
                var request = await server.GetContextAsync(); using var reader = new StreamReader(request.Request.InputStream); var payload = await reader.ReadToEndAsync();
                var valid = request.Request.Url!.AbsolutePath == "/v1/chat/completions" && request.Request.Headers["Authorization"] == "Bearer fixture-key" && payload.Contains("fixture-model") && payload.Contains("fixture-prompt");
                var bytes = System.Text.Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\"fixture-answer\"}}]}\n\ndata: [DONE]\n\n"); request.Response.ContentType = "text/event-stream"; request.Response.ContentLength64 = bytes.Length; await request.Response.OutputStream.WriteAsync(bytes); request.Response.Close(); return valid;
            });
            c.Settings.AiEndpoint = $"http://localhost:{port}/v1"; c.Settings.AiModel = "fixture-model"; c.Settings.AiKeyEncrypted = DesktopState.Protect("fixture-key");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(6)); var answer = await c.Ai.CompleteAsync([new() { Text = "fixture-prompt" }], token: deadline.Token);
            results["aiProtocol"] = new { payloadValid = await provider, streamedAnswer = answer == "fixture-answer", dpapiRoundtrip = DesktopState.Unprotect(c.Settings.AiKeyEncrypted) == "fixture-key" };
            c.Settings.AiEndpoint = "http://localhost:11434/v1"; c.Settings.AiModel = ""; c.Settings.AiKeyEncrypted = "";
        }
        catch (Exception error) { results["aiProtocol"] = new { passed = false, error = error.GetType().Name }; }
        try
        {
            var places = await c.Weather.FindAsync("Moscow");
            if (places.Count > 0)
            {
                c.Settings.WeatherCity = places[0].Name; c.Settings.Latitude = places[0].Latitude; c.Settings.Longitude = places[0].Longitude;
                await c.Weather.RefreshAsync();
            }
            results["weatherApi"] = new { placeLookup = places.Count > 0, forecastLoaded = c.Weather.Current != null, hours = c.Weather.Current?.Hours.Count ?? 0, days = c.Weather.Current?.Days.Count ?? 0 };
        }
        catch (Exception error) { results["weatherApi"] = new { placeLookup = false, error = error.GetType().Name }; }
    }
}
