using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using QuietIsland.Controls;

namespace QuietIsland;

public partial class MainWindow
{
    private string themeKey = "", hotkeyKey = "";
    internal bool IsClosing { get; private set; }
    private void ApplyVisualOpacity()
    {
        void Set(UIElement element, double target)
        {
            if (motion.Active && IsLoaded) element.BeginAnimation(OpacityProperty, new DoubleAnimation(element.Opacity, target, TimeSpan.FromMilliseconds(100)));
            else { element.BeginAnimation(OpacityProperty, null); element.Opacity = target; }
        }
        Set(Compact, expanded ? 0 : c.Settings.PillOpacity);
        Set(Panel, expanded ? c.Settings.PanelOpacity : 0);
        Set(Fluid, expanded ? c.Settings.PanelOpacity : c.Settings.PillOpacity);
    }
    private void ConfigureHeader()
    {
        var desired = c.Settings.HeaderTabs.Where(navigation.ContainsKey).Select(x => navigation[x]).ToArray();
        if (!Nav.Children.Cast<UIElement>().SequenceEqual(desired)) { Nav.Children.Clear(); foreach (var button in desired) Nav.Children.Add(button); }
        PanelClock.Visibility = c.Settings.HeaderClock ? Visibility.Visible : Visibility.Collapsed;
        DictationButton.Visibility = c.Settings.HeaderDictation ? Visibility.Visible : Visibility.Collapsed;
        PinButton.Visibility = c.Settings.HeaderPin ? Visibility.Visible : Visibility.Collapsed;
        CloseButton.Visibility = c.Settings.HeaderClose ? Visibility.Visible : Visibility.Collapsed;
        UpdateNavigation();
    }
    private void UpdateNavigation()
    {
        var active = selected is "search" or "media" or "activity" ? "home" : selected;
        foreach (var (key, button) in navigation)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal }; content.Children.Add(key == active ? Icons.Colored(key, 18) : Icons.Make(key, 18, "Muted"));
            if (key == active && c.Settings.HeaderLabels) { var label = Ui.Text(navTitles[key], 12); label.Margin = new Thickness(7, 0, 1, 0); label.VerticalAlignment = VerticalAlignment.Center; content.Children.Add(label); }
            button.Content = content; button.Background = key == active ? Ui.Brush("Hover") : Brushes.Transparent;
        }
    }
    private string ClockLabel(DateTime now) => now.ToString(c.Settings.Clock12Hour ? c.Settings.ClockSeconds ? "h:mm:ss tt" : "h:mm tt" : c.Settings.ClockSeconds ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture);
}
