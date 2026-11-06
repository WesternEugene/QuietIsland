using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using QuietIsland.Controls;
using QuietIsland.Models;
using QuietIsland.Services;
using QuietIsland.Views;

internal static class VisualPolishChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    internal static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        await check("polish_music_overflow_cover_and_rapid_lyrics_layout", async () =>
        {
            using var context = new IslandContext(Path.Combine(Path.GetTempPath(), "island-polish-visual-" + Guid.NewGuid().ToString("N")));
            var image = BitmapSource.Create(82, 82, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat(new byte[] { 80, 60, 110, 255 }, 82 * 82).SelectMany(x => x).ToArray(), 82 * 4); image.Freeze();
            var media = new MediaSnapshot("Очень длинное название композиции, которое должно плавно прокручиваться туда и обратно", "Исполнитель с очень длинным именем, второй исполнитель и третий исполнитель", "", false, "fixture", 37, 186, image, false);
            typeof(MediaService).GetProperty("Current")!.SetValue(context.Media, media);
            var main = new QuietIsland.MainWindow(context, true);
            try
            {
                using var view = new HomeView(context, main, () => []); view.Resources = Styles();
                using var source = new HwndSource(new HwndSourceParameters("QuietIsland.PolishVisuals") { ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 680, Height = 500 }); source.RootVisual = view;
                Layout(view); await Dispatcher.Yield(DispatcherPriority.Loaded); Layout(view);
                var cover = Descendants(view).OfType<Image>().Single(x => ReferenceEquals(x.Source, image));
                var title = Descendants(view).OfType<TextBlock>().Single(x => x.Text == media.Title);
                var artist = Descendants(view).OfType<TextBlock>().Single(x => x.Text == media.Artist);
                Require(title.FontSize >= 18 && artist.FontSize >= 14, "Music labels are still too small.");
                Require(title.TextTrimming == TextTrimming.None && ((TranslateTransform)title.RenderTransform).HasAnimatedProperties, "Overflow title has no animation.");
                Require(((TranslateTransform)artist.RenderTransform).HasAnimatedProperties, "Overflow artist has no animation.");
                var seek = Descendants(view).OfType<Slider>().Single(x => x.Maximum == 186);
                Require(cover.TranslatePoint(new(0, cover.ActualHeight), view).Y <= seek.TranslatePoint(new(), view).Y, "Cover extends below the progress track with lyrics disabled.");
                Save(view, "home-no-lyrics.png");
                foreach (var enabled in new[] { true, false, true, false, true }) { context.Lyrics.SetEnabled(enabled); Layout(view); await Task.Delay(45); }
                await Task.Delay(420); Layout(view); Save(view, "home-lyrics.png");
                Require(title.TranslatePoint(new(), view).Y >= cover.TranslatePoint(new(0, cover.ActualHeight), view).Y, "Rapid toggles leave metadata overlapping artwork.");
                context.Lyrics.SetEnabled(false); await Task.Delay(420); Layout(view);
                view.Visibility = Visibility.Collapsed; await Dispatcher.Yield();
                Require(!((TranslateTransform)title.RenderTransform).HasAnimatedProperties, "Hidden music keeps marquee animation active.");
                context.State.Data.Clipboard.Add(new() { Text = "Закреплённая запись", Pinned = true });
                using var clipboard = new ClipboardView(context) { Resources = Styles() };
                source.RootVisual = clipboard; clipboard.Measure(new(680, 340)); clipboard.Arrange(new(0, 0, 680, 340)); clipboard.UpdateLayout(); Save(clipboard, "clipboard-pin.png");
            }
            finally { main.Close(); }
        });
        await check("polish_closed_windows_release_shared_event_subscriptions", async () =>
        {
            using var context = new IslandContext(Path.Combine(Path.GetTempPath(), "island-polish-events-" + Guid.NewGuid().ToString("N")));
            int Subscribers(object obj, string field) => obj.GetType().GetField(field, Private)?.GetValue(obj) is Delegate handlers ? handlers.GetInvocationList().Length : 0;
            var before = Subscribers(context.Media, "Changed");
            for (var i = 0; i < 12; i++) { var main = new QuietIsland.MainWindow(context, true); main.ShowPanel("home", false); main.Close(); }
            await Dispatcher.Yield(DispatcherPriority.Background);
            Require(Subscribers(context.Media, "Changed") == before, "Closed windows remain subscribed to MediaService.");
            Require(Subscribers(context, "Navigate") == 0 && Subscribers(context, "Toast") == 0, "Closed windows remain retained by IslandContext.");
            Console.WriteLine("METRIC closed windows: 12; retained media, navigation and toast handlers: 0");
        });
        await check("polish_monitor_and_hover_preferences_survive_restart", () =>
        {
            var state = new DesktopState(Path.Combine(Path.GetTempPath(), "island-displays-" + Guid.NewGuid().ToString("N")));
            state.Data.Settings.IslandDisplays = ["DISPLAY1", "DISPLAY2"]; state.Data.Settings.HoverOpenDelayMs = 750;
            state.Save(); var next = new DesktopState(state.DirectoryPath);
            Require(next.Data.Settings.IslandDisplays.SequenceEqual(["DISPLAY1", "DISPLAY2"]) && next.Data.Settings.HoverOpenDelayMs == 750, "Display selections or hover delay are not durable.");
            next.Data.Settings.HoverOpenDelayMs = double.NaN; next.Data.Settings.NormalizeAppearance();
            Require(double.IsFinite(next.Data.Settings.HoverOpenDelayMs), "Invalid hover delay can reach DispatcherTimer.");
            return Task.CompletedTask;
        });
    }
    private static void Layout(HomeView view) { view.Measure(new(680, view.PreferredBodyHeight)); view.Arrange(new(0, 0, 680, view.PreferredBodyHeight)); view.UpdateLayout(); }
    internal static ResourceDictionary Styles()
    {
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var xml = XDocument.Load("src/QuietIsland/App.xaml");
        var resources = new XElement(ns + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"), xml.Root!.Element(ns + "Application.Resources")!.Elements().Select(x => new XElement(x)));
        foreach (var node in resources.Descendants().Where(x => x.Name.NamespaceName == "clr-namespace:QuietIsland.Controls")) node.Name = XName.Get(node.Name.LocalName, "clr-namespace:QuietIsland.Controls;assembly=QuietIsland");
        return (ResourceDictionary)XamlReader.Parse(resources.ToString());
    }
    private static void Save(FrameworkElement view, string name)
    {
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        CaptureService.SavePng(bitmap, Path.GetFullPath("artifacts/verification-nov06/" + name));
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
