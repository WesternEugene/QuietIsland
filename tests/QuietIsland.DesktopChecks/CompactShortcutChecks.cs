using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using QuietIsland;
using QuietIsland.Models;
using QuietIsland.Services;
using QuietIsland.Views;

internal static class CompactShortcutChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        await check("compact_music_character_viewports_scroll_and_reset", async () =>
        {
            using var context = Context();
            const string titlePrefix = "🎵e\u0301abcdefgh"; // 10 text elements, including a surrogate pair and a combining accent.
            const string artistPrefix = "ABCDEFGHIJKL";
            var media = new MediaSnapshot(titlePrefix + " continues across the island", artistPrefix + " and another artist", "", true, "fixture", 10, 180);
            SetMedia(context, media);
            var main = new MainWindow(context, true) { Left = -10000, Top = -10000 };
            try
            {
                main.Show(); await Dispatcher.Yield(DispatcherPriority.Loaded); main.UpdateLayout();
                var title = (TextBlock)main.FindName("CompactTitle");
                var artist = (TextBlock)main.FindName("CompactSubtitle");
                Require(title.Text == media.Title && artist.Text == media.Artist, "Scrolling discarded part of the media metadata.");
                Require(Math.Abs(Viewport(title).ActualWidth - PrefixWidth(title, titlePrefix)) <= 1, "Compact title does not have a 10-character viewport.");
                Require(Math.Abs(Viewport(artist).ActualWidth - PrefixWidth(artist, artistPrefix)) <= 1, "Compact artist does not have a 12-character viewport.");
                Require(title.RenderTransform is TranslateTransform { HasAnimatedProperties: true } && artist.RenderTransform is TranslateTransform { HasAnimatedProperties: true }, "Overflowing compact metadata is not scrolling.");
                await Task.Delay(1550);
                Require(((TranslateTransform)title.RenderTransform).X < 0, "Compact title did not move after the initial rest.");
                var preview = new System.Windows.Media.Imaging.RenderTargetBitmap(744, 72, 96, 96, PixelFormats.Pbgra32);
                preview.Render(main.RenderRoot);
                CaptureService.SavePng(preview, Path.GetFullPath("artifacts/verification-1.3.2/compact-marquee-fade.png"));
                main.Hide(); await Dispatcher.Yield();
                Require(!((TranslateTransform)title.RenderTransform).HasAnimatedProperties, "Hidden compact metadata keeps scrolling.");
                SetMedia(context, media with { Title = "Коротко", Artist = "Имя" });
                Call(main, "UpdateCompact"); main.Show(); await Dispatcher.Yield(DispatcherPriority.Loaded); main.UpdateLayout();
                Require(!((TranslateTransform)title.RenderTransform).HasAnimatedProperties && !((TranslateTransform)artist.RenderTransform).HasAnimatedProperties, "Short metadata still scrolls after a track change.");
                Require(((TranslateTransform)title.RenderTransform).X == 0, "A new short track retained the old scrolling offset.");
                context.Settings.CompactMaxWidth = 360; context.Settings.CompactFontSize = 16;
                context.Settings.ClockSeconds = context.Settings.Clock12Hour = true;
                context.State.Data.TimerEnd = DateTimeOffset.Now.AddMinutes(5);
                SetMedia(context, media); main.ApplySettings(); main.UpdateLayout();
                var visibleWidth = ((RectangleGeometry)((FrameworkElement)main.FindName("CompactLines")).Clip).Rect.Width;
                Require(Viewport(title).ActualWidth > 0 && Viewport(title).ActualWidth <= visibleWidth + .5,
                    "A narrow island clips the title before the scrolling viewport ends.");
                Require(Viewport(artist).ActualWidth <= visibleWidth + .5,
                    "A narrow island clips the artist before the scrolling viewport ends.");
            }
            finally { main.Close(); }
        });
        await check("capture_shortcuts_backfill_and_remain_editable", () =>
        {
            using var context = Context();
            context.Settings.GlobalHotkeys = GlobalShortcuts.Definitions.Where(x => x.Id <= 8).ToDictionary(x => x.Key, x => x.Default);
            context.Settings.GlobalHotkeys["search"] = "Ctrl+Shift+F9";
            context.Settings.GlobalHotkeys["notes"] = "";
            var bindings = GlobalShortcuts.Bindings(context.Settings);
            foreach (var (key, chord) in new[] { ("color", "Ctrl+Alt+C"), ("ocr", "Ctrl+Alt+T"), ("recording-fullscreen", "Ctrl+Alt+Shift+R") })
                Require(bindings.GetValueOrDefault(key) == chord, "Saved settings did not acquire the new shortcut: " + key);
            Require(bindings["search"] == "Ctrl+Shift+F9" && bindings["notes"] == "", "Adding shortcuts replaced an existing preference.");
            var main = new MainWindow(context, true);
            try
            {
                using var settings = new SettingsView(context, main);
                settings.SelectCategory(settings.Categories.Single(x => x.Contains("клав", StringComparison.OrdinalIgnoreCase)));
                settings.Measure(new(650, 1000)); settings.Arrange(new(0, 0, 650, 1000)); settings.UpdateLayout();
                var index = 0;
                foreach (var key in new[] { "color", "ocr", "recording-fullscreen" })
                {
                    var definition = GlobalShortcuts.Definitions.Single(x => x.Key == key);
                    var editor = Descendants(settings).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == definition.Title);
                    var assign = ((Panel)editor.Parent).Children.OfType<Button>().Single();
                    editor.Text = "Ctrl+Shift+F9"; assign.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Require(bindings[key] == definition.Default, "The settings editor accepted a conflicting shortcut.");
                    editor.Text = "Ctrl+Shift+F" + (6 + index++); assign.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Require(bindings[key] == editor.Text, "The new shortcut cannot be edited in settings: " + key);
                }
                context.State.Save();
                var restored = new DesktopState(context.State.DirectoryPath);
                Require(GlobalShortcuts.Bindings(restored.Data.Settings)["recording-fullscreen"] == "Ctrl+Shift+F8", "Edited shortcuts did not survive saving.");
                bindings["color"] = "";
                Require(GlobalShortcuts.Bindings(context.Settings)["color"] == "", "An explicitly disabled new shortcut was re-enabled.");
            }
            finally { main.Close(); }
            var upgrade = new DesktopState(Path.Combine(context.State.DirectoryPath, "upgrade"));
            upgrade.Data.Settings.GlobalHotkeys = GlobalShortcuts.Definitions.Where(x => x.Id <= 8).ToDictionary(x => x.Key, x => x.Default);
            upgrade.Data.Settings.GlobalHotkeys["search"] = "Ctrl+Alt+T";
            upgrade.Data.Launcher.Add(new() { Name = "Saved color chord", Target = "fixture", Hotkey = "Ctrl+Alt+C" });
            upgrade.Data.Launcher.Add(new() { Name = "Saved recording chord", Target = "fixture", Hotkey = "Ctrl+Alt+Shift+R" });
            upgrade.Save();
            var migrated = new DesktopState(upgrade.DirectoryPath);
            var migratedBindings = GlobalShortcuts.Bindings(migrated.Data.Settings);
            Require(migratedBindings["color"] == "" && migratedBindings["ocr"] == "" && migratedBindings["recording-fullscreen"] == "",
                "New default shortcuts take over saved global or launcher assignments during upgrade.");
            Require(migratedBindings["search"] == "Ctrl+Alt+T" && migrated.Data.Launcher.Any(x => x.Hotkey == "Ctrl+Alt+C"),
                "Shortcut migration changed an existing assignment.");
            return Task.CompletedTask;
        });
        await check("capture_shortcuts_dispatch_color_text_and_full_screen", async () =>
        {
            using var context = Context();
            var main = new MainWindow(context, true);
            var selections = new List<bool>();
            typeof(MainWindow).GetProperty("SelectionForChecks", Private)!.SetValue(main,
                (Func<bool, Task<CaptureResult?>>)(color => { selections.Add(color); return Task.FromResult<CaptureResult?>(null); }));
            try
            {
                foreach (var key in new[] { "color", "ocr" })
                    Call(main, "InvokeShortcut", GlobalShortcuts.Definitions.Single(x => x.Key == key).Id);
                Require(selections.SequenceEqual([true, false]), "Color and text shortcuts did not request their respective selection modes.");
                // Fail before starting an encoder or capturing the real desktop.
                context.Settings.FfmpegPath = Path.Combine(context.State.DirectoryPath, "missing-shortcut-encoder.exe");
                var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                context.Toast += message => { if (message.Contains("missing-shortcut-encoder.exe")) completed.TrySetResult(); };
                Call(main, "InvokeShortcut", GlobalShortcuts.Definitions.Single(x => x.Key == "recording-fullscreen").Id);
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(3));
                Require(selections.Count == 2 && context.Recording.LastError?.Contains("missing-shortcut-encoder.exe") == true,
                    "Whole-screen recording opened a region selector instead of directly preparing recording.");
                Require(!context.Recording.IsStarting && !context.Recording.IsRecording, "Failed recording did not release its state.");
            }
            finally { main.Close(); }
        });
    }
    private static IslandContext Context() => new(Path.Combine(Path.GetTempPath(), "quiet-compact-shortcuts-" + Guid.NewGuid().ToString("N")));
    private static void SetMedia(IslandContext context, MediaSnapshot media) => typeof(MediaService).GetProperty(nameof(MediaService.Current))!.SetValue(context.Media, media);
    private static void Call(object value, string method, params object?[] args) => value.GetType().GetMethod(method, Private)!.Invoke(value, args);
    private static FrameworkElement Viewport(DependencyObject label)
    {
        for (var parent = VisualTreeHelper.GetParent(label); parent != null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is FrameworkElement { ClipToBounds: true } viewport) return viewport;
        throw new InvalidOperationException("Compact text has no clipping viewport.");
    }
    private static double PrefixWidth(TextBlock label, string prefix)
    {
        var probe = new TextBlock { Text = prefix, FontFamily = label.FontFamily, FontStyle = label.FontStyle, FontWeight = label.FontWeight, FontStretch = label.FontStretch, FontSize = label.FontSize };
        probe.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Ceiling(probe.DesiredSize.Width);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Require(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); }
}
