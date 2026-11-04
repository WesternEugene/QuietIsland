using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using QuietIsland.Controls;
using QuietIsland.Services;

namespace QuietIsland.Views;
public sealed class ShelfView : IslandView
{
    private readonly WrapPanel files = new();
    private readonly Border empty = Ui.Empty("▱", "Полка пуста", "Перетащите файлы в чёлку. Двойной щелчок открывает файл; правая кнопка — действия.");
    private readonly ScrollViewer fileScroll;
    private readonly TextBlock count = Ui.Text("", 12, "Muted");
    private readonly Button dragAll;
    public ShelfView(IslandContext context) : base(context)
    {
        var root = new DockPanel { AllowDrop = true };
        dragAll = Ui.Button("Перетащить все", CopyAll, tip: "Перетащите в другое приложение или нажмите, чтобы скопировать файлы");
        FileDrag.Attach(dragAll, () => new DataObject(DataFormats.FileDrop, Existing()));
        var top = new Grid { Margin = new Thickness(0, 0, 0, 8) }; top.ColumnDefinitions.Add(new()); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var left = Ui.Row(dragAll, Ui.Button("+ Добавить", AddFiles), count); count.VerticalAlignment = VerticalAlignment.Center; top.Children.Add(left);
        var clear = Ui.Button("Очистить", () => { C.State.Data.Shelf.Clear(); C.State.Touch(); Build(); }); clear.ToolTip = "Убрать ссылки с полки"; Grid.SetColumn(clear, 1); top.Children.Add(clear);
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var stage = new Grid(); fileScroll = Ui.Scroll(files); stage.Children.Add(fileScroll); empty.HorizontalAlignment = HorizontalAlignment.Center; empty.VerticalAlignment = VerticalAlignment.Center; stage.Children.Add(empty); root.Children.Add(stage);
        root.DragOver += (_, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Copy; e.Handled = true; } };
        root.Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) { Add(paths); e.Handled = true; } };
        Content = root; Build();
    }
    private string[] Existing() => C.State.Data.Shelf.Where(x => File.Exists(x) || Directory.Exists(x)).ToArray();
    private void Add(IEnumerable<string> paths)
    {
        foreach (var path in paths) if ((File.Exists(path) || Directory.Exists(path)) && !C.State.Data.Shelf.Contains(path, StringComparer.OrdinalIgnoreCase)) C.State.Data.Shelf.Add(path);
        C.State.Touch(); Build();
    }
    private void AddFiles() { var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Добавить на полку" }; if (dialog.ShowDialog() == true) Add(dialog.FileNames); }
    private void CopyAll() => CopyFiles(Existing());
    private void CopyFiles(string[] paths)
    {
        if (paths.Length == 0) return;
        var list = new StringCollection(); list.AddRange(paths); System.Windows.Clipboard.SetFileDropList(list); C.Notify("Файлы скопированы");
    }
    private void Open(string path)
    {
        try { if (!File.Exists(path) && !Directory.Exists(path)) throw new InvalidOperationException("Файл перемещён или удалён. Уберите его с полки и добавьте снова."); SystemService.Open(path); C.Collapse(); }
        catch (Exception e) { C.Error(e); }
    }
    private void Build()
    {
        files.Children.Clear(); count.Text = $"{C.State.Data.Shelf.Count} файлов"; dragAll.Content = $"Перетащить все ({C.State.Data.Shelf.Count})"; dragAll.IsEnabled = Existing().Length > 0;
        empty.Visibility = C.State.Data.Shelf.Count == 0 ? Visibility.Visible : Visibility.Collapsed; fileScroll.Visibility = C.State.Data.Shelf.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (C.State.Data.Shelf.Count == 0) return;
        foreach (var path in C.State.Data.Shelf.ToArray())
        {
            var exists = File.Exists(path) || Directory.Exists(path);
            var preview = new Grid { Height = 52, Margin = new Thickness(0, 0, 0, 7) };
            preview.Children.Add(ShellIcons.File(path, 42));
            var name = Ui.Text(Path.GetFileName(path), 11); name.TextAlignment = TextAlignment.Center; name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
            string detail;
            try { detail = !exists ? "Файл недоступен" : Directory.Exists(path) ? "Папка" : Ui.Bytes(new FileInfo(path).Length); } catch { detail = ""; }
            var size = Ui.Text(detail, 10, "Muted"); size.TextAlignment = TextAlignment.Center; size.Margin = new Thickness(0, 4, 0, 0);
            var card = Ui.Card(Ui.Stack(preview, name, size), 12); card.Width = 124; card.Height = 116; card.Margin = new Thickness(0, 0, 9, 9); card.Cursor = Cursors.Hand; card.ToolTip = path;
            card.Focusable = true; card.Tag = "QuietIsland.FileTile";
            card.MouseLeftButtonDown += (_, e) => { card.Focus(); if (e.ClickCount == 2) { Open(path); e.Handled = true; } };
            card.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Open(path); e.Handled = true; } else if (e.Key == Key.Delete) { C.State.Data.Shelf.Remove(path); C.State.Touch(); Build(); e.Handled = true; } else if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) CopyFiles([path]); };
            FileDrag.Attach(card, () => new DataObject(DataFormats.FileDrop, new[] { path }));
            var menu = new ContextMenu();
            foreach (var (label, action) in new (string, Action)[]
            {
                ("Открыть", () => Open(path)), ("Показать в папке", () => SystemService.Reveal(path)),
                ("Скопировать файл", () => CopyFiles([path])), ("Скопировать путь", () => System.Windows.Clipboard.SetText(path)),
                ("Убрать с полки", () => { C.State.Data.Shelf.Remove(path); C.State.Touch(); Build(); })
            })
            { var item = new MenuItem { Header = label, Icon = Icons.Colored(label switch { "Открыть" => "file", "Показать в папке" => "folder", "Скопировать файл" => "copy", "Скопировать путь" => "code", _ => "trash" }, 16) }; item.Click += (_, _) => { try { action(); } catch (Exception e) { C.Error(e); } }; menu.Items.Add(item); }
            card.ContextMenu = menu; files.Children.Add(card);
        }
    }
}
