using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using QuietIsland.Models;
using QuietIsland.Core;
using QuietIsland.Services;
using QuietIsland.Views;

internal static class Program
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly List<object> Results = [];

    [STAThread]
    private static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var (key, color) in new[] { ("Text", "#F5F5F5"), ("Card", "#181818"), ("Surface", "#000000"), ("Hover", "#2A2A2A"), ("Line", "#303030"), ("Muted", "#929292"), ("Accent", "#FF8B81"), ("AccentText", "#111111") })
            app.Resources[key] = new BrushConverter().ConvertFromString(color);
        app.Startup += async (_, _) =>
        {
            try { app.Shutdown(await RunAsync()); }
            catch (Exception error) { Console.Error.WriteLine(error); app.Shutdown(1); }
        };
        return app.Run();
    }

    private static async Task<int> RunAsync()
    {
        await StartupChecks.RunAsync(CheckAsync);
        await CaptureChecks.RunAsync(CheckAsync);
        await ResourceChecks.RunAsync(CheckAsync);
        using var context = new IslandContext(Path.Combine(Path.GetTempPath(), "quiet-island-desktop-checks-" + Guid.NewGuid().ToString("N")));
        await CheckAsync("ffmpeg_prefers_installer_bundle", () =>
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe");
            Require(!File.Exists(bundled), "Test output already contains an encoder fixture.");
            Directory.CreateDirectory(Path.GetDirectoryName(bundled)!);
            File.WriteAllText(bundled, "Encoder path fixture; never executed.");
            try
            {
                Require(FfmpegLocator.Resolve("ffmpeg") == bundled, "The installer-bundled encoder was skipped in favour of PATH.");
                Require(FfmpegLocator.Resolve("ffmpeg.exe") == bundled, "The default executable name skipped the installer bundle.");
                var explicitPath = Path.Combine(context.State.DirectoryPath, "missing-ffmpeg.exe");
                try { FfmpegLocator.Resolve(explicitPath); throw new Exception("A missing explicit path silently selected the bundle."); }
                catch (InvalidOperationException error) { Require(error.Message.Contains(explicitPath), "Explicit path failure lost the requested path."); }
            }
            finally { File.Delete(bundled); }
            return Task.CompletedTask;
        });
        await CheckAsync("recording_reports_encoder_launch_error", async () =>
        {
            var invalidEncoder = Path.Combine(context.State.DirectoryPath, "invalid-ffmpeg.exe");
            Directory.CreateDirectory(context.State.DirectoryPath);
            File.WriteAllText(invalidEncoder, "Not a Windows executable.");
            context.Settings.FfmpegPath = invalidEncoder;
            context.Settings.CaptureFolder = context.State.DirectoryPath;
            context.Settings.RecordSystemAudio = false;
            using var recording = new RecordingService(context.State);
            try
            {
                await recording.StartAsync(new System.Drawing.Rectangle(0, 0, 32, 32));
                throw new Exception("An invalid executable unexpectedly started recording.");
            }
            catch (InvalidOperationException error) when (error.InnerException is System.ComponentModel.Win32Exception native)
            {
                Require(error.Message.Contains(invalidEncoder) && error.Message.Contains(native.NativeErrorCode.ToString(CultureInfo.InvariantCulture)), "Encoder launch failure hides the executable path or Windows error code.");
                Require(recording.LastError == error.Message && !recording.IsStarting && !recording.IsRecording, "Failed launch did not release recording state.");
            }
            finally { context.Settings.FfmpegPath = "ffmpeg"; File.Delete(invalidEncoder); }
        });
        var shelfFile = Path.Combine(context.State.DirectoryPath, "drop-file.txt");
        Directory.CreateDirectory(context.State.DirectoryPath); File.WriteAllText(shelfFile, "Shelf drop fixture");
        foreach (var populated in new[] { false, true })
            await CheckAsync(populated ? "shelf_free_space_accepts_drop" : "empty_shelf_accepts_drop", () =>
            {
                context.State.Data.Shelf.Clear();
                if (populated) context.State.Data.Shelf.Add(context.State.DirectoryPath);
                using var shelf = new ShelfView(context);
                using var source = new HwndSource(new HwndSourceParameters("QuietIsland.ShelfDropCheck")
                { ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 680, Height = 330 });
                source.RootVisual = shelf;
                shelf.Measure(new Size(680, 330)); shelf.Arrange(new Rect(0, 0, 680, 330)); shelf.UpdateLayout();
                var position = new Point(8, 320);
                Require(shelf.InputHitTest(position) is UIElement, "Free space on the shelf is not a drop target.");
                var target = (UIElement)shelf.InputHitTest(position)!;
                var data = new DataObject(DataFormats.FileDrop, new[] { shelfFile, shelfFile, Path.Combine(context.State.DirectoryPath, "missing.txt") });
                var over = DragEvent(data, target, position, DragDrop.DragOverEvent); target.RaiseEvent(over);
                Require(over.Handled && over.Effects == DragDropEffects.Copy, "Shelf does not accept files dragged over its free space.");
                var drop = DragEvent(data, target, position, DragDrop.DropEvent); target.RaiseEvent(drop);
                Require(drop.Handled && context.State.Data.Shelf.Count(x => x == shelfFile) == 1, "Dropping a file did not add exactly one shelf entry.");
                Require(context.State.Data.Shelf.Count == (populated ? 2 : 1), "Shelf drop added a nonexistent or duplicate file.");
                return Task.CompletedTask;
            });
        context.State.Data.Shelf.Clear();
        await CheckAsync("updates_offer_new_version_and_survive_network_error", async () =>
        {
            using var handler = new ReleaseHandler(); using var http = new HttpClient(handler);
            using var updates = new UpdateService(() => true, new UpdateClient(http), new Version(1, 1, 2, 0));
            using var updateContext = new IslandContext(Path.Combine(context.State.DirectoryPath, "updates-fixture"), updates: updates);
            var main = new QuietIsland.MainWindow(updateContext, true);
            using var view = new UpdatesView(updateContext, main);
            view.Measure(new Size(600, 330)); view.Arrange(new Rect(0, 0, 600, 330)); view.UpdateLayout();
            await updates.CheckAsync(); await Dispatcher.Yield(DispatcherPriority.Background);
            Require(updates.Available?.DisplayVersion == "1.1.3", "A newer stable release was not offered.");
            Require(Descendants(view).OfType<Button>().Any(x => Equals(x.Content, "Скачать и установить") && x.IsEnabled), "Update UI has no enabled download/install action.");
            var requestsBeforeInstall = handler.Requests;
            var notices = new List<string>(); updateContext.Toast += notices.Add;
            typeof(RecordingService).GetProperty(nameof(RecordingService.IsRecording))!.SetValue(updateContext.Recording, true);
            var install = Descendants(view).OfType<Button>().Single(x => Equals(x.Content, "Скачать и установить"));
            install.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.Background);
            Require(handler.Requests == requestsBeforeInstall && notices.Any(x => x.Contains("Сначала остановите запись")), "Update installation was not blocked during an active recording.");
            typeof(RecordingService).GetProperty(nameof(RecordingService.IsRecording))!.SetValue(updateContext.Recording, false);
            var speechGate = Field<SpeechSessionGate>(updateContext.Speech, "gate");
            speechGate.BeginListening(); speechGate.BeginTranscription(); notices.Clear();
            install.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.Background);
            Require(handler.Requests == requestsBeforeInstall && notices.Any(x => x.Contains("диктовку")), "Update installation was not blocked while speech was transcribing.");
            speechGate.Finish();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(600, 330, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
            var previewFolder = Path.GetFullPath("artifacts/verification-1.1.2"); Directory.CreateDirectory(previewFolder);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(previewFolder, "updates-pane.png"))) encoder.Save(output);
            handler.Fail = true;
            await updates.CheckAsync(); await Dispatcher.Yield(DispatcherPriority.Background);
            Require(updates.Available?.DisplayVersion == "1.1.3" && !updates.IsChecking, "A network failure discarded an already discovered update or left the check busy.");
            main.Close();
        });
        await CheckAsync("updates_corrupt_download_sets_retry_status", async () =>
        {
            using var handler = new ReleaseHandler(); using var http = new HttpClient(handler);
            using var updates = new UpdateService(() => false, new UpdateClient(http), new Version(1, 1, 2, 0));
            await updates.CheckAsync();
            Exception? failure = null;
            try { await updates.DownloadAsync(updates.Available!, Path.Combine(context.State.DirectoryPath, "invalid-download")); }
            catch (Exception error) { failure = error; }
            Require(failure is InvalidOperationException { InnerException: InvalidDataException }, "Invalid installer download did not produce a user-facing retry error.");
            Require(!updates.IsDownloading && !updates.Status.StartsWith("Загрузка"), "A failed installer is still shown as downloading.");
        });
        var applications = new[] { "AVS Converter", "Visual Studio 2022", "Visual Studio Code", "Oracle VirtualBox", "OBS Studio", "Windows Media Player", "XML Notepad", "XMLReader" }
            .Select(name => new SearchResult(name, Path.Combine(AppContext.BaseDirectory, name + ".lnk"), "Приложение")).ToArray();
        SetApplications(context, applications);
        using var search = new SearchView(context);
        foreach (var (query, expected) in new[] { ("vs", "Visual Studio 2022"), ("vsc", "Visual Studio Code"), ("vb", "Oracle VirtualBox"), ("VB", "Oracle VirtualBox"), ("wmp", "Windows Media Player"), ("xn", "XML Notepad"), ("xr", "XMLReader"), ("visual studio code", "Visual Studio Code"), ("virtual", "Oracle VirtualBox") })
        {
            await CheckAsync("application_query_" + query, async () =>
            {
                await SearchAsync(search, query);
                var list = Field<ListBox>(search, "list");
                Require(list.SelectedItem is ListBoxItem selected && RowName(selected) == expected,
                    $"Expected {expected} first for '{query}', got {string.Join(", ", list.Items.OfType<ListBoxItem>().Where(x => x.IsEnabled).Take(4).Select(RowName))}.");
                Require(RowName((ListBoxItem)list.Items[0]) == "Приложения", "Applications must precede Everything files.");
            });
        }
        await CheckAsync("applications_discovered_before_debounced_query", async () =>
        {
            SetApplications(context, []);
            var pending = SearchAsync(search, "vb");
            await Dispatcher.Yield(DispatcherPriority.Background);
            SetApplications(context, applications);
            await pending;
            var list = Field<ListBox>(search, "list");
            Require(list.SelectedItem is ListBoxItem selected && RowName(selected) == "Oracle VirtualBox", "Application catalog loaded during the debounce did not match vb.");
        });
        await CheckAsync("unrelated_query_keeps_file_and_web_search", async () =>
        {
            await SearchAsync(search, "zzqnonexistent");
            var list = Field<ListBox>(search, "list");
            Require(!list.Items.OfType<ListBoxItem>().Any(x => RowName(x) == "Приложения"), "Unrelated applications matched.");
            Require(list.Items.OfType<ListBoxItem>().Any(x => RowName(x).StartsWith("Искать «zzqnonexistent»")), "Web fallback disappeared.");
        });

        context.Settings.Latitude = 56.84; context.Settings.Longitude = 60.6;
        using var handler = new ForecastHandler(); using var client = new HttpClient(handler);
        Field<HttpClient>(context.Weather, "client").Dispose();
        typeof(WeatherService).GetField("client", Fields)!.SetValue(context.Weather, client);
        await context.Weather.RefreshAsync();
        await CheckAsync("weather_requests_and_preserves_metres_per_second", () =>
        {
            Require(handler.Request?.Query.Split('&').Contains("wind_speed_unit=ms") == true, "Forecast request does not select m/s.");
            Require(context.Weather.Current?.Wind == 4.5, "m/s response was incorrectly converted or failed to parse.");
            return Task.CompletedTask;
        });
        await CheckAsync("weather_displays_metres_per_second", () =>
        {
            using var weather = new WeatherView(context);
            weather.Measure(new Size(744, 430)); weather.Arrange(new Rect(0, 0, 744, 430)); weather.UpdateLayout();
            var labels = Descendants(weather).OfType<TextBlock>().Select(x => x.Text).ToArray();
            Require(labels.Any(x => x.StartsWith("Ветер ") && x.Contains("м/с") && !x.Contains("км/ч")), "Wind label does not display m/s.");
            return Task.CompletedTask;
        });
        Console.WriteLine(JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
        return Results.Any(x => (bool)x.GetType().GetProperty("passed")!.GetValue(x)! == false) ? 1 : 0;
    }

    private static async Task CheckAsync(string name, Func<Task> check)
    {
        try { await check(); Results.Add(new { name, passed = true, error = "" }); }
        catch (Exception error) { Results.Add(new { name, passed = false, error = error.Message }); }
    }
    private static void Require(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); }
    private static DragEventArgs DragEvent(IDataObject data, UIElement target, Point position, RoutedEvent routedEvent)
    {
        var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
            null, [data, DragDropKeyStates.None, DragDropEffects.Copy | DragDropEffects.Move, target, position], CultureInfo.InvariantCulture)!;
        args.RoutedEvent = routedEvent; return args;
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Fields)!.GetValue(target)!;
    private static void SetApplications(IslandContext context, IReadOnlyList<SearchResult> applications) => typeof(IslandContext).GetProperty(nameof(IslandContext.Applications))!.SetValue(context, applications);
    private static async Task SearchAsync(SearchView view, string input)
    {
        Field<TextBox>(view, "query").Text = input;
        await (Task)typeof(SearchView).GetMethod("SearchAsync", Fields)!.Invoke(view, null)!;
    }
    private static string RowName(ListBoxItem row) => row.Content is TextBlock title ? title.Text : ((Grid)row.Content).Children.OfType<StackPanel>().First().Children.OfType<TextBlock>().First().Text;
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private sealed class ForecastHandler : HttpMessageHandler
    {
        public Uri? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request.RequestUri;
            var wind = Request!.Query.Split('&').Contains("wind_speed_unit=ms") ? 4.5 : 16.2;
            var json = $$$"""
                {"current_units":{"wind_speed_10m":"{{{(wind == 4.5 ? "m/s" : "km/h")}}}"},
                 "current":{"time":"2026-11-04T12:00","temperature_2m":1,"relative_humidity_2m":80,"apparent_temperature":-2,"weather_code":3,"wind_speed_10m":{{{wind.ToString(CultureInfo.InvariantCulture)}}}},
                 "hourly":{"time":["2026-11-04T12:00"],"temperature_2m":[1],"precipitation_probability":[20],"weather_code":[3]},
                 "daily":{"time":["2026-11-04"],"temperature_2m_min":[-3],"temperature_2m_max":[2],"weather_code":[3]}}
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
    private sealed class ReleaseHandler : HttpMessageHandler
    {
        public bool Fail { get; set; }
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (Fail) throw new HttpRequestException("Offline fixture");
            const string json = """
                {"tag_name":"1.1.3","draft":false,"prerelease":false,"body":"Update fixture",
                 "assets":[{"name":"QuietIsland-1.1.3-Setup-win-x64.exe","size":100,
                 "browser_download_url":"https://github.com/WesternEugene/QuietIsland/releases/download/1.1.3/QuietIsland-1.1.3-Setup-win-x64.exe"}]}
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
