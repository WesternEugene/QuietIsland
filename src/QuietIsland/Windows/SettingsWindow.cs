using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using QuietIsland.Controls;
using QuietIsland.Services;
using QuietIsland.Views;

namespace QuietIsland.Windows;

public sealed class SettingsWindow : Window
{
    private readonly SettingsView settings;
    private readonly ScrollRouting scrolling;
    private readonly Action<string> selectCategory;
    public void SelectCategory(string category) => selectCategory(category);
    public SettingsWindow(IslandContext context, MainWindow main)
    {
        Title = "Quiet Island — настройки"; Width = 860; Height = 710; MinWidth = 720; MinHeight = 500;
        SetResourceReference(BackgroundProperty, "SettingsSurface"); SetResourceReference(ForegroundProperty, "Text"); WindowStartupLocation = WindowStartupLocation.CenterScreen;
        settings = new(context, main); scrolling = new(this);
        var root = new Grid(); root.SetResourceReference(Panel.BackgroundProperty, "SettingsSurface"); root.ColumnDefinitions.Add(new() { Width = new GridLength(195) }); root.ColumnDefinitions.Add(new());
        var sidebar = new StackPanel { Margin = new Thickness(14, 22, 12, 12) };
        var logo = Ui.Text("Quiet Island", 18, bold: true); logo.Margin = new Thickness(11, 0, 0, 22); sidebar.Children.Add(logo);
        var buttons = new Dictionary<string, Button>();
        void Select(string category)
        {
            settings.SelectCategory(category);
            foreach (var (name, b) in buttons) { if (name == category) b.SetResourceReference(BackgroundProperty, "Hover"); else b.Background = Brushes.Transparent; }
        }
        selectCategory = Select;
        foreach (var category in settings.Categories)
        {
            var button = Ui.Button(category, () => Select(category)); button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Padding = new Thickness(12, 11, 10, 11); button.Margin = new Thickness(0, 0, 0, 4); button.FontSize = 12;
            buttons[category] = button; sidebar.Children.Add(button);
        }
        var left = new Border { Child = Ui.Scroll(sidebar) }; left.SetResourceReference(Border.BackgroundProperty, "SettingsSidebar"); root.Children.Add(left);
        var body = new Grid { Margin = new Thickness(28, 26, 20, 15) }; body.RowDefinitions.Add(new()); body.RowDefinitions.Add(new() { Height = GridLength.Auto }); body.Children.Add(settings);
        var message = Ui.Text("", 11, "Muted"); message.Margin = new Thickness(0, 8, 4, 0); Grid.SetRow(message, 1); body.Children.Add(message);
        void Toast(string text) => Dispatcher.InvokeAsync(() => message.Text = text);
        context.Toast += Toast;
        Grid.SetColumn(body, 1); root.Children.Add(body); Content = root; Select(settings.Categories[0]);
        SourceInitialized += (_, _) => ApplyTheme();
        Closed += (_, _) => { context.Toast -= Toast; settings.Dispose(); scrolling.Dispose(); };
    }
    public void ApplyTheme()
    {
        var handle = new WindowInteropHelper(this).Handle; if (handle == IntPtr.Zero) return;
        var light = Ui.Brush("SettingsSurface") is SolidColorBrush b && b.Color.R > 128; var dark = light ? 0 : 1;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
