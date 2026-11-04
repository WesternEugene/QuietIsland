using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using QuietIsland.Controls;

namespace QuietIsland.Windows;
public static class Dialogs
{
    public static string? Ask(string title, string label, string initial = "")
    {
        var input = Ui.Input(label, initial, true); input.Height = 80; string? result = null;
        var window = Base(title); window.Content = Ui.Card(Ui.Stack(Ui.Heading(title), Ui.Text(label, 12, "Muted"), input, Ui.Row(Ui.Button("Сохранить", () => { result = input.Text; window.DialogResult = true; }, true), Ui.Button("Отмена", () => window.DialogResult = false))), 22);
        window.Loaded += (_, _) => { input.Focus(); input.SelectAll(); }; window.ShowDialog(); return result;
    }
    public static string? Choose(string title, string[] values)
    {
        var list = new ComboBox { ItemsSource = values, SelectedIndex = 0, Margin = new Thickness(0, 12, 0, 12) }; string? result = null; var window = Base(title);
        window.Content = Ui.Card(Ui.Stack(Ui.Heading(title), list, Ui.Row(Ui.Button("Выбрать", () => { result = list.SelectedItem as string; window.DialogResult = true; }, true), Ui.Button("Отмена", () => window.DialogResult = false))), 22); window.ShowDialog(); return result;
    }
    private static Window Base(string title)
    {
        var window = new Window { Title = title, Width = 490, SizeToContent = SizeToContent.Height, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Background = Ui.Brush("Surface"), Topmost = true, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        if (System.Windows.Application.Current.MainWindow is { IsVisible: true } owner) { window.Owner = owner; window.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        window.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) window.DialogResult = false; }; return window;
    }
}
public sealed class AlertWindow : Window
{
    public AlertWindow(string title, Action snooze)
    {
        Title = "Quiet Island · " + title; Width = 430; Height = 190; Topmost = true; ShowInTaskbar = false; ShowActivated = false; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Background = Ui.Brush("Surface");
        Left = (SystemParameters.WorkArea.Width - Width) / 2; Top = SystemParameters.WorkArea.Top + 80;
        Content = Ui.Card(Ui.Stack(Ui.Text("QUIET ISLAND · НАПОМИНАНИЕ", 10, "Accent"), Ui.Heading(title), Ui.Row(Ui.Button("Готово", Close, true), Ui.Button("+10 минут", () => { snooze(); Close(); }))), 22);
        SourceInitialized += (_, _) => Services.Native.ConfigureOverlay(this, false, false, true);
    }
}
