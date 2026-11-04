using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using QuietIsland.Services;

namespace QuietIsland;

internal static class DesktopWorkflowChecks
{
    public static async Task<object> RunAsync(string directory)
    {
        using var c = new IslandContext(directory);
        c.State.Data.Settings = new Models.AppSettings(); c.State.Data.TimerEnd = null; c.State.Data.TimerPaused = false;
        var main = new MainWindow(c, true); using var settings = new Views.SettingsView(c, main);
        Diagnostics.Render(settings, 650, 650);
        var minimum = Descendants(settings).OfType<Slider>().Single(x => AutomationProperties.GetName(x) == "Минимальная ширина");
        minimum.Value = 220;
        var settingsApplyWithoutButton = c.Settings.CompactMinWidth == 220 && main.NotchWidth >= 220 && !Descendants(settings).OfType<Button>().Any(x => x.Content is string label && label.Contains("Сохранить и применить"));
        var opacity = Descendants(settings).OfType<TextBox>().Single(x => Equals(x.Tag, "Прозрачность пилюли · 45–100%")); opacity.Text = "45";
        await Task.Delay(380);
        var surface = (Controls.LiquidSurface)((Grid)main.RenderRoot).Children[0];
        var opacityAppliesToContentAndSurface = c.Settings.PillOpacity == .45 && Math.Abs(((FrameworkElement)main.FindName("Compact")).Opacity - .45) < .001 && Math.Abs(surface.Opacity - .45) < .001;
        var scale = Descendants(settings).OfType<TextBox>().Single(x => Equals(x.Tag, "Масштаб · 80–200%")); scale.Text = "110"; await Task.Delay(360); scale.Text = "-"; await Task.Delay(360);
        var invalidPartialInputKeepsLastValue = Math.Abs(c.Settings.Scale - 1.1) < .001;
        var clockToggle = Descendants(settings).OfType<CheckBox>().Single(x => Equals(x.Content, "Часы")); clockToggle.IsChecked = false; clockToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var down = Descendants(settings).OfType<Button>().First(x => Equals(x.ToolTip, "Ниже") && x.IsEnabled); down.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var headerAppliesVisibilityAndOrder = !c.Settings.HeaderClock && ((TextBlock)main.FindName("PanelClock")).Visibility == Visibility.Collapsed && c.Settings.HeaderTabs[0] == "clipboard" && c.Settings.HeaderTabs[1] == "home";
        var format = Descendants(settings).OfType<ComboBox>().Single(x => x.Items.Contains("12 часов · AM/PM")); format.SelectedItem = "12 часов · AM/PM";
        var time = ((TextBlock)main.FindName("ClockText")).Text; var twelveHourFormatSelectable = c.Settings.Clock12Hour && (time.EndsWith(" AM") || time.EndsWith(" PM"));
        foreach (var field in new[] { "lastActivity", "lastWeather", "lastPoll" }) typeof(MainWindow).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, DateTime.Now);
        var beforeDialogs = System.Windows.Application.Current.Windows.OfType<Windows.AlertWindow>().Count(); c.State.Data.TimerEnd = DateTimeOffset.Now.AddSeconds(-1); await main.TickAsync();
        var timerNoticeStaysInIsland = c.State.Data.TimerEnd == null && ((FrameworkElement)main.FindName("TimerNotification")).Visibility == Visibility.Visible && System.Windows.Application.Current.Windows.OfType<Windows.AlertWindow>().Count() == beforeDialogs;
        main.HandleDragEnter(new DataObject(DataFormats.FileDrop, new[] { Environment.ProcessPath! })); main.PointerLeaveForChecks(3000); var kept = main.IsExpanded && main.SelectedTab == "shelf"; main.EndIncomingDrag(); main.PointerLeaveForChecks(3000);
        var fileDragSessionPreventsHoverFlapping = kept && !main.IsExpanded;
        var shell = (ContentControl)Controls.ShellIcons.File(Environment.ProcessPath!, 26);
        for (var i = 0; i < 30 && shell.Content is not Image; i++) await Task.Delay(40);
        var shellIconUsesNativeBitmap = shell.Content is Image { Source: System.Windows.Media.Imaging.BitmapSource { IsFrozen: true } };
        main.Close();
        return new { settingsApplyWithoutButton, opacityAppliesToContentAndSurface, invalidPartialInputKeepsLastValue, headerAppliesVisibilityAndOrder, twelveHourFormatSelectable, timerNoticeStaysInIsland, fileDragSessionPreventsHoverFlapping, shellIconUsesNativeBitmap };
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var item in Descendants(child)) yield return item; }
    }
}
