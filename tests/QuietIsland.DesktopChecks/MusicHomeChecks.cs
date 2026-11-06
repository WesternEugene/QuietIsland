using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using QuietIsland.Controls;
using QuietIsland.Models;
using QuietIsland.Services;
using QuietIsland.Views;

internal static class MusicHomeChecks
{
    public static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        using var context = new IslandContext(Path.Combine(Path.GetTempPath(), "quiet-island-music-" + Guid.NewGuid().ToString("N")));
        context.State.Data.Launcher.Clear();
        context.State.Data.Launcher.Add(new() { Name = "Fixture launcher", Target = "fixture.exe" });
        var main = new QuietIsland.MainWindow(context, true);
        try
        {
            await check("saved_recording_stays_below_visible_launcher_and_fits_home", () =>
            {
                using var view = new HomeView(context, main, () => Array.Empty<(string, string)>());
                using var source = Attach(view);
                var originalHeight = PreferredHeight(view);
                typeof(RecordingService).GetProperty(nameof(RecordingService.LastOutputPath))!.SetValue(context.Recording, Path.Combine(context.State.DirectoryPath, "Fixture recording.mp4"));
                view.Refresh();
                var expandedHeight = PreferredHeight(view);
                Layout(view, expandedHeight);
                var launcher = Descendants(view).OfType<Button>().Single(x => Equals(x.ToolTip, "Fixture launcher"));
                var saved = Descendants(view).OfType<TextBlock>().Single(x => x.Text == "Запись сохранена");
                Require(launcher.IsVisible, "Saving a recording hides the launcher.");
                Require(saved.IsVisible && saved.TranslatePoint(new Point(), view).Y >= launcher.TranslatePoint(new Point(0, launcher.ActualHeight), view).Y, "Saved recording is not below the launcher.");
                Require(expandedHeight > originalHeight && saved.TranslatePoint(new Point(0, saved.ActualHeight), view).Y <= view.ActualHeight, "Saved recording does not request enough visible home height.");
                var dismiss = Descendants(view).OfType<Button>().Single(x => Equals(x.ToolTip, "Скрыть сохранённую запись"));
                dismiss.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Require(PreferredHeight(view) == originalHeight && launcher.IsVisible, "Dismissing the notice does not restore home height while keeping the launcher.");
                typeof(RecordingService).GetProperty(nameof(RecordingService.LastOutputPath))!.SetValue(context.Recording, null);
                return Task.CompletedTask;
            });
            await check("home_volume_and_side_tools_share_bottom_edge", () =>
            {
                using var view = new HomeView(context, main, () => Array.Empty<(string, string)>());
                using var source = Attach(view);
                Layout(view, PreferredHeight(view));
                var output = Descendants(view).OfType<ComboBox>().Single();
                var lastTool = Descendants(view).OfType<Button>().Single(x => x.Content is StackPanel panel && panel.Children.OfType<TextBlock>().Any(text => text.Text == "Команда"));
                var volumeBottom = output.TranslatePoint(new Point(0, output.ActualHeight), view).Y;
                var toolBottom = lastTool.TranslatePoint(new Point(0, lastTool.ActualHeight), view).Y;
                Require(Math.Abs(volumeBottom - toolBottom) <= 12, $"Volume and tool bottoms diverge by {Math.Abs(volumeBottom - toolBottom):0.0}px.");
                return Task.CompletedTask;
            });
            foreach (var fullPlayer in new[] { false, true })
                await check(fullPlayer ? "media_lyrics_put_metadata_under_cover_and_center_transport" : "home_lyrics_put_metadata_under_cover_and_center_transport", async () =>
                {
                    typeof(MediaService).GetProperty(nameof(MediaService.Current))!.SetValue(context.Media, new MediaSnapshot("Fixture song", "Fixture artist", "", false, "fixture", 10, 180, IsMusic: false));
                    context.Lyrics.SetEnabled(false);
                    using IslandView view = fullPlayer ? new MediaView(context) : new HomeView(context, main, () => Array.Empty<(string, string)>());
                    using var source = Attach(view);
                    Layout(view, fullPlayer ? 340 : PreferredHeight((HomeView)view));
                    context.Lyrics.SetEnabled(true);
                    await Task.Delay(450);
                    Layout(view, fullPlayer ? 340 : PreferredHeight((HomeView)view));
                    var cover = Descendants(view).OfType<Image>().Single();
                    var title = Descendants(view).OfType<TextBlock>().Single(x => x.Text == "Fixture song");
                    var lyrics = Descendants(view).OfType<LyricsPanel>().Single();
                    Require(title.TranslatePoint(new Point(), view).Y >= cover.TranslatePoint(new Point(0, cover.ActualHeight), view).Y, "Lyrics mode leaves title beside the cover.");
                    Require(lyrics.TranslatePoint(new Point(), view).X >= cover.TranslatePoint(new Point(cover.ActualWidth, 0), view).X, "Lyrics do not fill the area to the right of the cover.");
                    var play = Descendants(view).OfType<Button>().Single(x => Equals(x.ToolTip, "Воспроизвести"));
                    var transport = VisualTreeHelper.GetParent(play);
                    while (transport is FrameworkElement element && AutomationProperties.GetName(element) != "Управление музыкой") transport = VisualTreeHelper.GetParent(transport);
                    Require(transport is FrameworkElement, "Music controls have no accessible transport group.");
                    var group = (FrameworkElement)transport!;
                    var playCenter = play.TranslatePoint(new Point(play.ActualWidth / 2, 0), group).X;
                    Require(Math.Abs(playCenter - group.ActualWidth / 2) < 1, "Additional mode buttons shift the central play button.");
                    var shuffle = Descendants(view).OfType<Button>().Single(x => AutomationProperties.GetName(x).StartsWith("Перемешивание"));
                    var repeat = Descendants(view).OfType<Button>().Single(x => AutomationProperties.GetName(x).StartsWith("Повтор"));
                    var previous = Descendants(view).OfType<Button>().Single(x => Equals(x.ToolTip, "Предыдущий трек"));
                    Require(shuffle.TranslatePoint(new Point(shuffle.ActualWidth, 0), view).X <= repeat.TranslatePoint(new Point(), view).X && repeat.TranslatePoint(new Point(repeat.ActualWidth, 0), view).X <= previous.TranslatePoint(new Point(), view).X, "Shuffle and repeat do not precede Previous.");
                    Require(!shuffle.IsEnabled && !repeat.IsEnabled, "Unsupported player modes remain actionable without a media session.");
                    context.Lyrics.SetEnabled(false);
                });
            await check("repeat_cycles_off_all_one_off", () =>
            {
                var next = typeof(MediaService).GetMethod("NextRepeatMode", BindingFlags.NonPublic | BindingFlags.Static);
                Require(next != null, "Repeat mode cycle is unavailable.");
                var type = next!.ReturnType;
                object mode = Enum.Parse(type, "None");
                foreach (var expected in new[] { "List", "Track", "None", "List" })
                {
                    mode = next.Invoke(null, [mode])!;
                    Require(mode.ToString() == expected, $"Repeat cycle expected {expected}, got {mode}.");
                }
                return Task.CompletedTask;
            });
        }
        finally { main.Close(); }
    }

    private static double PreferredHeight(HomeView view) => (double?)typeof(HomeView).GetProperty("PreferredBodyHeight")?.GetValue(view) ?? 286;
    private static HwndSource Attach(FrameworkElement view)
    {
        var source = new HwndSource(new HwndSourceParameters("QuietIsland.MusicHomeCheck") { ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 680, Height = 500 });
        source.RootVisual = view; return source;
    }
    private static void Layout(FrameworkElement view, double height) { view.Measure(new Size(680, height)); view.Arrange(new Rect(0, 0, 680, height)); view.UpdateLayout(); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
