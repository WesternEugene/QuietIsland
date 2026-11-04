using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;

namespace QuietIsland.Controls;

public static class Ui
{
    public static Brush Brush(string resource) => (Brush)System.Windows.Application.Current.FindResource(resource);
    public static TextBlock Text(string text, double size = 13, string color = "Text", bool bold = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
        block.SetResourceReference(TextBlock.ForegroundProperty, color); return block;
    }
    public static TextBlock Heading(string text, string? subtitle = null) => Text(text, 18, bold: true);
    public static Button Button(string text, Action action, bool primary = false, string? tip = null)
    {
        var b = new Button { Content = text, ToolTip = tip ?? text };
        if (primary) { b.SetResourceReference(Control.BackgroundProperty, "Accent"); b.SetResourceReference(Control.ForegroundProperty, "AccentText"); }
        ActionContent(b, text); b.Click += (_, _) => action(); return b;
    }
    public static Button AsyncButton(string text, Func<Task> action, Action<Exception>? error = null, bool primary = false)
    {
        var b = new Button { Content = text, ToolTip = text };
        if (primary) { b.SetResourceReference(Control.BackgroundProperty, "Accent"); b.SetResourceReference(Control.ForegroundProperty, "AccentText"); }
        ActionContent(b, text); b.Click += async (_, _) => { b.IsEnabled = false; try { await action(); } catch (Exception e) { if (error != null) error(e); else Services.Log.Write("UI action", e); } finally { b.IsEnabled = true; } };
        return b;
    }
    private static void ActionContent(Button button, string text)
    {
        var kind = text switch { "×" => "close", "−" => "trash", "+" => "plus", "◆" or "◇" => "pin", "■" => "stop", "Ⅱ / ▶" => "pause", "⏮" => "previous", "⏭" => "next", _ => "" };
        var label = kind.Length > 0 ? "" : text;
        foreach (var (prefix, icon) in new[] { ("◷ ", "clock"), ("✧ ", "ai"), ("⌗ ", "shot"), ("● ", "record"), ("■ ", "stop"), ("▦ ", "app") })
            if (text.StartsWith(prefix, StringComparison.Ordinal)) { kind = icon; label = text[prefix.Length..]; break; }
        if (kind.Length == 0) return;
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; row.Children.Add(Icons.Colored(kind, 16));
        if (label.Length > 0) { var name = Text(label, 12); name.Margin = new Thickness(6, 0, 0, 0); name.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(name); }
        button.Content = row;
        System.Windows.Automation.AutomationProperties.SetName(button, label.Length > 0 ? label : kind);
    }
    public static StackPanel Stack(params UIElement[] children) { var p = new StackPanel(); foreach (var c in children) p.Children.Add(c); return p; }
    public static WrapPanel Row(params UIElement[] children) { var p = new WrapPanel { Margin = new Thickness(0, 6, 0, 6) }; foreach (var c in children) p.Children.Add(c); return p; }
    public static Border Card(UIElement content, double padding = 12)
    {
        var card = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(padding), Margin = new Thickness(0, 0, 0, 8), Child = content };
        card.SetResourceReference(Border.BackgroundProperty, "Card"); return card;
    }
    public static TextBox Input(string hint, string value = "", bool multiline = false)
    {
        var box = new TextBox { Text = value, Tag = hint, ToolTip = hint, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, Margin = new Thickness(0, 5, 0, 8), Height = multiline ? double.NaN : 42, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden };
        AttachEditMenu(box); System.Windows.Automation.AutomationProperties.SetName(box, hint); return box;
    }
    public static void AttachEditMenu(System.Windows.Controls.Primitives.TextBoxBase editor)
    {
        var menu = new ContextMenu();
        foreach (var (label, kind, command) in new (string, string, System.Windows.Input.RoutedUICommand)[]
        {
            ("Отменить", "undo", System.Windows.Input.ApplicationCommands.Undo),
            ("Повторить", "redo", System.Windows.Input.ApplicationCommands.Redo),
            ("Вырезать", "cut", System.Windows.Input.ApplicationCommands.Cut),
            ("Копировать", "copy", System.Windows.Input.ApplicationCommands.Copy),
            ("Вставить", "paste", System.Windows.Input.ApplicationCommands.Paste),
            ("Выделить всё", "check", System.Windows.Input.ApplicationCommands.SelectAll)
        }) menu.Items.Add(new MenuItem { Header = label, Icon = Icons.Colored(kind, 16), Command = command, CommandTarget = editor });
        editor.ContextMenu = menu;
    }
    public static UIElement Field(string label, UIElement input) => Stack(Text(label, 12, "Muted"), input);
    public static DataTemplate OptionLabel(string property)
    {
        var text = new FrameworkElementFactory(typeof(TextBlock)); text.SetBinding(TextBlock.TextProperty, new Binding(property));
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap); text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        return new DataTemplate { VisualTree = text };
    }
    public static ScrollViewer Scroll(UIElement content) => new() { Content = content, Padding = new Thickness(0, 0, 4, 0), CanContentScroll = false, PanningMode = PanningMode.Both };
    public static Border Empty(string glyph, string title, string detail)
    {
        var symbol = Icons.Colored(glyph switch { "▱" => "shelf", "▤" => "clipboard", "✧" => "ai", "☀" => "weather", "◉" => "activity", _ => "app" }, 32); symbol.HorizontalAlignment = HorizontalAlignment.Center;
        var name = Text(title, 18, bold: true); name.TextAlignment = TextAlignment.Center; name.Margin = new Thickness(0, 12, 0, 8);
        var sub = Text(detail, 13, "Muted"); sub.TextAlignment = TextAlignment.Center; sub.MaxWidth = 420;
        return new Border { Padding = new Thickness(24, 28, 24, 24), Child = Stack(symbol, name, sub) };
    }
    public static string Bytes(long value) => value < 0 ? "" : value < 1024 ? $"{value} Б" : value < 1024 * 1024 ? $"{value / 1024d:0.#} КБ" : value < 1024L * 1024 * 1024 ? $"{value / 1048576d:0.#} МБ" : $"{value / 1073741824d:0.#} ГБ";
    public static string Time(double seconds) => TimeSpan.FromSeconds(Math.Max(0, double.IsFinite(seconds) ? seconds : 0)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
}
