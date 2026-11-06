using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QuietIsland;
using QuietIsland.Services;
using QuietIsland.Views;

internal static class CaptureVisibilityChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr region);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window, IntPtr region);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);

    internal static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        await check("review_capture_preference_from_mirror_updates_all_native_affinities", () =>
        {
            using var context = Context(); var main = Window(context); var mirror = Window(context); var sibling = Window(context);
            typeof(MainWindow).GetField("mirrorOwner", Private)!.SetValue(mirror, main);
            typeof(MainWindow).GetField("mirrorOwner", Private)!.SetValue(sibling, main);
            var mirrors = (Dictionary<string, MainWindow>)typeof(MainWindow).GetField("mirrors", Private)!.GetValue(main)!;
            mirrors["fixture-one"] = mirror; mirrors["fixture-two"] = sibling;
            try
            {
                using var home = new HomeView(context, mirror, () => []);
                home.Measure(new(680, home.PreferredBodyHeight)); home.Arrange(new(0, 0, 680, home.PreferredBodyHeight)); home.UpdateLayout();
                var button = Descendants(home).OfType<Button>().Single(x => x.Content is StackPanel panel && panel.Children.OfType<TextBlock>().Any(t => t.Text == "Область"));
                var option = button.ContextMenu!.Items.OfType<MenuItem>().Single(x => x.IsCheckable);
                foreach (var enabled in new[] { false, true })
                {
                    option.IsChecked = enabled; option.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    foreach (var window in new[] { main, mirror, sibling })
                    {
                        Require(GetWindowDisplayAffinity(new WindowInteropHelper(window).Handle, out var affinity), "Windows did not return the island's capture affinity.");
                        Require(affinity == (enabled ? 0x11u : 0u), "Changing capture visibility from one monitor left another island's native affinity stale.");
                    }
                }
            }
            finally { main.Close(); }
            return Task.CompletedTask;
        });
        await check("review_capture_hidden_recording_does_not_open_invisible_input", async () =>
        {
            using var context = Context(); var main = Window(context);
            try
            {
                SetRecordingState(context, "IsRecording", true); await Dispatcher.Yield(DispatcherPriority.Background);
                main.ShowPanel("search", true); await Dispatcher.Yield(DispatcherPriority.Input);
                Require((string)typeof(MainWindow).GetProperty("SelectedTab", Private)!.GetValue(main)! == "home"
                    && !(bool)typeof(MainWindow).GetProperty("IsExpanded", Private)!.GetValue(main)!,
                    "The search shortcut opened an invisible input panel during a hidden recording.");
            }
            finally { SetRecordingState(context, "IsRecording", false); main.Close(); }
        });
        foreach (var enabled in new[] { true, false })
            await check("capture_visibility_screenshot_option_" + enabled, async () =>
            {
                using var context = Context(); context.Settings.HideInCapture = enabled;
                var main = Window(context);
                try
                {
                    var called = false; var hiddenBeforeCapture = false;
                    Select(main, _ => { called = true; hiddenBeforeCapture = Clipped(main); return Task.FromResult<CaptureResult?>(null); });
                    await main.ScreenshotAsync("shot");
                    Require(called && hiddenBeforeCapture == enabled, "The screenshot preference was not applied before selection captured the desktop.");
                    Require(!Clipped(main), "Cancelling a screenshot left the island hidden.");
                }
                finally { main.Close(); }
            });
        await check("capture_visibility_selection_error_restores_all_monitors", async () =>
        {
            using var context = Context(); var main = Window(context); var mirror = Window(context);
            typeof(MainWindow).GetField("mirrorOwner", Private)!.SetValue(mirror, main);
            var mirrors = (Dictionary<string, MainWindow>)typeof(MainWindow).GetField("mirrors", Private)!.GetValue(main)!;
            mirrors["fixture-monitor"] = mirror;
            try
            {
                var bothHidden = false;
                Select(main, _ =>
                {
                    bothHidden = Clipped(main) && Clipped(mirror);
                    return Task.FromException<CaptureResult?>(new InvalidOperationException("Selection fixture failure"));
                });
                await main.ScreenshotAsync("shot");
                Require(bothHidden, "Capture did not suppress the island on every selected monitor.");
                Require(!Clipped(main) && !Clipped(mirror), "Capture failure did not restore every monitor.");
            }
            finally { main.Close(); }
        });
        await check("capture_visibility_recording_hides_before_selection_and_restores_on_cancel", async () =>
        {
            using var context = Context(); var main = Window(context);
            try
            {
                var hiddenBeforeCapture = false;
                Select(main, _ => { hiddenBeforeCapture = Clipped(main); return Task.FromResult<CaptureResult?>(null); });
                await main.ToggleRecordingAsync();
                Require(hiddenBeforeCapture, "Recording selection included the island before its first captured frame.");
                Require(!Clipped(main) && !context.Recording.IsRecording, "Cancelling recording selection left the island hidden.");
            }
            finally { main.Close(); }
        });
        await check("capture_visibility_encoder_failure_restores_island", async () =>
        {
            using var context = Context(); context.Settings.FfmpegPath = Path.Combine(context.State.DirectoryPath, "missing-encoder.exe");
            var main = Window(context);
            try
            {
                var hiddenBeforeCapture = false;
                var image = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8); image.Freeze();
                Select(main, _ => { hiddenBeforeCapture = Clipped(main); return Task.FromResult<CaptureResult?>(new(image, new(0, 0, 32, 32))); });
                await main.ToggleRecordingAsync();
                Require(hiddenBeforeCapture && context.Recording.LastError != null, "The encoder failure fixture did not begin with a hidden island.");
                Require(!Clipped(main) && !context.Recording.IsStarting && !context.Recording.IsRecording, "An encoder launch error left the island hidden.");
            }
            finally { main.Close(); }
        });
        await check("capture_visibility_recording_lifecycle_and_settings_apply", async () =>
        {
            using var context = Context(); var main = Window(context);
            try
            {
                foreach (var state in new[] { "IsStarting", "IsRecording", "IsFinalizing" })
                {
                    SetRecordingState(context, state, true); await Dispatcher.Yield(DispatcherPriority.Background);
                    main.ShowPanel("home", false);
                    Require(Clipped(main), "Opening the panel revealed the island during recording state " + state);
                    context.Settings.HideInCapture = false; main.ApplySettings();
                    Require(!Clipped(main), "The capture option cannot reveal the island when disabled.");
                    context.Settings.HideInCapture = true; main.ApplySettings();
                    Require(Clipped(main), "The capture option cannot hide an ongoing recording.");
                    SetRecordingState(context, state, false); await Dispatcher.Yield(DispatcherPriority.Background);
                    Require(!Clipped(main), "Finishing/failing a recording did not restore the island after " + state);
                }
            }
            finally { main.Close(); }
        });
        await check("capture_visibility_capture_preserves_user_hidden_state", async () =>
        {
            using var context = Context(); var main = Window(context);
            try
            {
                main.SetIslandHidden(true);
                Select(main, _ => Task.FromResult<CaptureResult?>(null));
                await main.ScreenshotAsync("shot");
                Require(Clipped(main), "Capture restoration revealed an island the user had hidden.");
            }
            finally { main.Close(); }
        });
        await check("capture_visibility_option_is_available_before_screenshots_and_recording", () =>
        {
            using var context = Context(); var main = Window(context);
            try
            {
                using var home = new HomeView(context, main, () => []);
                home.Measure(new(680, home.PreferredBodyHeight)); home.Arrange(new(0, 0, 680, home.PreferredBodyHeight)); home.UpdateLayout();
                var actions = Descendants(home).OfType<Button>().Where(button => button.Content is StackPanel panel
                    && panel.Children.OfType<TextBlock>().Any(label => label.Text is "Область" or "Весь экран" or "Запись экрана")).ToArray();
                Require(actions.Length == 3, "Capture actions were not found in the home view.");
                foreach (var button in actions)
                {
                    var menu = button.ContextMenu;
                    Require(menu != null, "A capture action has no pre-capture options menu.");
                    menu!.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
                    var option = menu.Items.OfType<MenuItem>().SingleOrDefault(x => x.IsCheckable && x.Header is string title && title.Contains("Скрывать чёлку"));
                    Require(option is { IsChecked: true }, "A capture action does not reflect the shared hide preference.");
                    option!.IsChecked = false; option.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Require(!context.Settings.HideInCapture, "The menu option did not update capture behavior.");
                    context.State.Save();
                    Require(!new DesktopState(context.State.DirectoryPath).Data.Settings.HideInCapture, "The menu option was not persisted.");
                    context.Settings.HideInCapture = true;
                }
                using var settings = new SettingsView(context, main); settings.SelectCategory("Запись и звук");
                var recordingSection = ((Dictionary<string, UIElement>)typeof(SettingsView).GetField("sections", Private)!.GetValue(settings)!)["Запись и звук"];
                Require(recordingSection.Visibility == Visibility.Visible && Descendants(recordingSection).OfType<CheckBox>().Any(x => x.Content is string title && title.Contains("Скрывать чёлку")),
                    "Recording settings do not expose the hide preference.");
                settings.Resources = VisualPolishChecks.Styles();
                settings.Measure(new(630, 650)); settings.Arrange(new(0, 0, 630, 650)); settings.UpdateLayout();
                var preview = new RenderTargetBitmap(630, 650, 96, 96, PixelFormats.Pbgra32); preview.Render(settings);
                CaptureService.SavePng(preview, Path.GetFullPath("artifacts/verification-1.3.1/capture-settings.png"));
            }
            finally { main.Close(); }
            return Task.CompletedTask;
        });
    }
    private static IslandContext Context() => new(Path.Combine(Path.GetTempPath(), "island-capture-visibility-" + Guid.NewGuid().ToString("N")));
    private static MainWindow Window(IslandContext context)
    {
        var main = new MainWindow(context, true);
        typeof(MainWindow).GetProperty("NativeMotionProbe", Private)!.SetValue(main, true);
        new WindowInteropHelper(main).EnsureHandle(); main.ApplySettings();
        return main;
    }
    private static void Select(MainWindow main, Func<bool, Task<CaptureResult?>> select) => typeof(MainWindow).GetProperty("SelectionForChecks", Private)!.SetValue(main, select);
    private static bool Clipped(MainWindow window)
    {
        var region = CreateRectRgn(0, 0, 0, 0);
        try { return GetWindowRgn(new WindowInteropHelper(window).Handle, region) == 1; /* NULLREGION */ }
        finally { DeleteObject(region); }
    }
    private static void SetRecordingState(IslandContext context, string name, bool active)
    {
        typeof(RecordingService).GetProperty(name)!.SetValue(context.Recording, active);
        ((Action?)typeof(RecordingService).GetField("Changed", Private)!.GetValue(context.Recording))?.Invoke();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
