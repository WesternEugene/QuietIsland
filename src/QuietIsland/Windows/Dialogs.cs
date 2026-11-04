using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using QuietIsland.Controls;
using QuietIsland.Models;

namespace QuietIsland.Windows;
public static class Dialogs
{
    private static int openDialogs;
    internal static bool IsOpen => openDialogs > 0;
    private static void ShowModal(Window window)
    {
        openDialogs++;
        try { window.ShowDialog(); }
        finally { openDialogs--; }
    }
    public static LaunchItem? Launcher(string title, LaunchItem? initial = null)
    {
        var name = Ui.Input("Название", initial?.Name ?? "");
        var target = Ui.Input("Путь к программе, https://сайт или steam://run/…", initial?.Target ?? "");
        var hint = Ui.Text("", 11, "Accent");
        name.TextChanged += (_, _) => hint.Text = ""; target.TextChanged += (_, _) => hint.Text = "";
        var window = Base(title); LaunchItem? result = null;
        void Save()
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { hint.Text = "Укажите название ярлыка"; name.Focus(); return; }
            if (string.IsNullOrWhiteSpace(target.Text)) { hint.Text = "Укажите путь или ссылку"; target.Focus(); return; }
            result = new LaunchItem { Name = name.Text.Trim(), Target = target.Text.Trim(), Group = initial?.Group ?? "", Hotkey = initial?.Hotkey ?? "" };
            window.DialogResult = true;
        }
        var save = Ui.Button(initial == null ? "Добавить" : "Сохранить", Save, true); save.IsDefault = true;
        window.Content = Ui.Card(Ui.Stack(Ui.Heading(title), Ui.Field("Название", name), Ui.Field("Путь или ссылка", target), hint, Ui.Row(save, Ui.Button("Отмена", () => window.DialogResult = false))), 22);
        window.Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
        ShowModal(window); return result;
    }
    public static string? Ask(string title, string label, string initial = "")
    {
        var input = Ui.Input(label, initial, true); input.Height = 80; string? result = null;
        var window = Base(title); window.Content = Ui.Card(Ui.Stack(Ui.Heading(title), Ui.Text(label, 12, "Muted"), input, Ui.Row(Ui.Button("Сохранить", () => { result = input.Text; window.DialogResult = true; }, true), Ui.Button("Отмена", () => window.DialogResult = false))), 22);
        window.Loaded += (_, _) => { input.Focus(); input.SelectAll(); }; ShowModal(window); return result;
    }
    public static string? Choose(string title, string[] values)
    {
        var list = new ComboBox { ItemsSource = values, SelectedIndex = 0, Margin = new Thickness(0, 12, 0, 12) }; string? result = null; var window = Base(title);
        window.Content = Ui.Card(Ui.Stack(Ui.Heading(title), list, Ui.Row(Ui.Button("Выбрать", () => { result = list.SelectedItem as string; window.DialogResult = true; }, true), Ui.Button("Отмена", () => window.DialogResult = false))), 22); ShowModal(window); return result;
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
