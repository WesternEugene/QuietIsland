using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuietIsland.Controls;
using QuietIsland.Core;
using QuietIsland.Models;
using QuietIsland.Services;

namespace QuietIsland.Views;

public sealed class SearchView : IslandView
{
    private readonly TextBox query = Ui.Input("Файлы, программы и прочее");
    private readonly ListBox list = new();
    private readonly TextBlock hint = Ui.Text("Введите имя файла или используйте синтаксис Everything", 11, "Muted");
    private readonly WrapPanel pins = new();
    private readonly WrapPanel group = new();
    private readonly Button aiShortcut;
    private CancellationTokenSource? search;
    private IReadOnlyList<SearchResult>? catalog;
    private readonly Dictionary<ListBoxItem, Action> actions = new();
    public SearchView(IslandContext context) : base(context)
    {
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var title = Ui.Stack(query); title.Margin = new Thickness(0, 0, 0, 5); root.Children.Add(title);
        aiShortcut = Ui.Button("✧ Спросить ИИ", () => { if (C.Ai.IsConfigured) { query.Text = "? "; query.CaretIndex = 2; query.Focus(); } }); aiShortcut.IsEnabled = C.Ai.IsConfigured;
        var shortcuts = Ui.Row(Ui.Button("◷ Таймер", () => { query.Text = "таймер 10 мин"; query.Focus(); }), aiShortcut, Ui.Button("+ Ярлык", AddPin)); Grid.SetRow(shortcuts, 1); root.Children.Add(shortcuts);
        Grid.SetRow(list, 2); root.Children.Add(list);
        var footer = Ui.Stack(group, pins, hint); Grid.SetRow(footer, 3); root.Children.Add(footer); Content = root;
        query.FontSize = 17; query.Height = 50; query.Padding = new Thickness(14, 8, 14, 8);
        query.TextChanged += async (_, _) => await SearchAsync();
        query.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Down or Key.Up && list.Items.Count > 0) { var direction = e.Key == Key.Down ? 1 : -1; var index = list.SelectedIndex; do { index += direction; } while (index >= 0 && index < list.Items.Count && list.Items[index] is ListBoxItem row && !actions.ContainsKey(row)); if (index >= 0 && index < list.Items.Count) { list.SelectedIndex = index; list.ScrollIntoView(list.SelectedItem); } e.Handled = true; }
            if (e.Key == Key.Enter) { Execute(); e.Handled = true; }
        };
        list.MouseDoubleClick += (_, _) => Execute(); list.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { Execute(); e.Handled = true; } };
        C.State.Changed += ConfigurationChanged;
        BuildPins(); _ = SearchAsync();
    }
    private void ConfigurationChanged()
    {
        var configured = C.Ai.IsConfigured;
        if (aiShortcut.IsEnabled == configured) return;
        aiShortcut.IsEnabled = configured;
        if (CommandParser.Parse(query.Text.Trim()).Kind == CommandKind.Ai) _ = SearchAsync();
    }
    public override void FocusInput() { query.Focus(); query.CaretIndex = query.Text.Length; }
    public override void Refresh() { if (!Disposed && !ReferenceEquals(catalog, C.Applications)) _ = SearchAsync(); }
    private void Execute() { if (list.SelectedItem is ListBoxItem row && actions.TryGetValue(row, out var action)) { try { action(); } catch (Exception e) { C.Error(e); } } }
    private void Add(string name, string description, string glyph, Action action, SearchResult? result = null)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new GridLength(35) }); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var iconKind = glyph switch { "⌕" => "search", "◷" => "clock", "◉" => "activity", "=" => "calculator", ">_" => "terminal", "✧" => "ai", "☷" => "control", "↗" => "browser", _ => "file" };
        var symbol = result == null ? Icons.Colored(iconKind, 24) : ShellIcons.File(result.Path, 26);
        symbol.VerticalAlignment = VerticalAlignment.Center; symbol.HorizontalAlignment = HorizontalAlignment.Left; grid.Children.Add(symbol);
        var lines = Ui.Stack(Ui.Text(name, 13, bold: true), Ui.Text(description, 10, "Muted")); Grid.SetColumn(lines, 1); grid.Children.Add(lines);
        if (result != null) { var kind = Ui.Text(result.Kind, 10, "Muted"); Grid.SetColumn(kind, 2); grid.Children.Add(kind); }
        var item = new ListBoxItem { Content = grid }; actions[item] = action;
        if (result != null)
        {
            var menu = new ContextMenu();
            foreach (var (label, callback) in new (string, Action)[] { ("Открыть", () => SystemService.Open(result.Path)), ("Показать в папке", () => SystemService.Reveal(result.Path)), ("Скопировать путь", () => System.Windows.Clipboard.SetText(result.Path)), ("Добавить на полку", () => { if (!C.State.Data.Shelf.Contains(result.Path)) C.State.Data.Shelf.Add(result.Path); C.State.Touch(); C.Notify("Файл на полке"); }) })
            { var mi = new MenuItem { Header = label, Icon = Icons.Colored(label switch { "Открыть" => "file", "Показать в папке" => "folder", "Скопировать путь" => "code", _ => "shelf" }, 16) }; mi.Click += (_, _) => { try { callback(); } catch (Exception e) { C.Error(e); } }; menu.Items.Add(mi); }
            item.ContextMenu = menu;
            FileDrag.Attach(item, () => new DataObject(DataFormats.FileDrop, new[] { result.Path }));
        }
        list.Items.Add(item);
    }
    private void GroupHeading(string title)
    {
        list.Items.Add(new ListBoxItem { Content = Ui.Text(title, 10, "Muted", true), IsEnabled = false, Focusable = false, Margin = new Thickness(0, 7, 0, 0), Tag = "QuietIsland.SearchGroup" });
    }
    private void SelectFirst() { if (list.SelectedItem == null) list.SelectedItem = list.Items.OfType<ListBoxItem>().FirstOrDefault(actions.ContainsKey); }
    private static bool IsApplication(SearchResult result) => result.Kind == "Приложение" || Path.GetExtension(result.Path).ToLowerInvariant() is ".exe" or ".lnk" or ".appref-ms";
    private SearchResult[] FindApplications(string input) => C.Applications
        .Select(app => (App: app, Score: ApplicationMatcher.Score(app.Name, input)))
        .Where(match => match.Score > 0).OrderByDescending(match => match.Score)
        .ThenBy(match => match.App.Name, StringComparer.CurrentCultureIgnoreCase)
        .Take(10).Select(match => match.App).ToArray();
    private async Task SearchAsync()
    {
        search?.Cancel(); search = new(); var token = search.Token;
        try
        {
            await Task.Delay(130, token); if (Disposed) return;
            list.Items.Clear(); actions.Clear();
            var input = query.Text.Trim();
            catalog = C.Applications;
            if (input.Length == 0)
            {
                Add("Поиск по всему компьютеру", "Everything · имена, пути, ext:pdf, size:>10mb", "⌕", () => query.Focus());
                Add("таймер 10 мин", "Запустить таймер прямо в острове", "◷", () => { C.StartTimer(TimeSpan.FromMinutes(10)); C.Collapse(); });
                Add("напомни позвонить маме завтра в 9", "Напоминания одной фразой · ежедневные и по дням недели", "◉", () => { query.Text = "напомни "; query.CaretIndex = query.Text.Length; query.Focus(); });
                Add("2 + 3 * 4  /  1 inch to cm", "Калькулятор и перевод единиц без запуска команд", "=", () => { query.Text = "2 + 3 * 4"; query.Focus(); });
                list.SelectedIndex = 0; return;
            }
            var command = CommandParser.Parse(input);
            if (CurrencyService.IsQuery(input))
            {
                hint.Text = "Запрашиваю курсы ЦБ РФ…";
                var converted = await CurrencyService.ConvertAsync(input, token); token.ThrowIfCancellationRequested(); if (Disposed) return;
                if (converted != null) { Add(converted, "Enter — скопировать", "=", () => System.Windows.Clipboard.SetText(converted)); list.SelectedIndex = 0; hint.Text = "Официальные справочные курсы"; return; }
            }
            switch (command.Kind)
            {
                case CommandKind.Calculate: Add(command.Result!, "Enter — скопировать результат", "=", () => { System.Windows.Clipboard.SetText(command.Result!); C.Notify("Результат скопирован"); }); break;
                case CommandKind.Timer: Add("Таймер · " + Ui.Time(command.Duration.TotalSeconds), "Enter — запустить", "◷", () => { C.StartTimer(command.Duration); C.Collapse(); }); break;
                case CommandKind.Reminder:
                    var reminder = ReminderParser.Parse(command.Text, DateTimeOffset.Now);
                    Add(reminder?.Title ?? "Не удалось разобрать время", reminder != null ? reminder.Due.ToString("dd.MM · HH:mm") + (reminder.Daily ? " · ежедневно" : "") : "Пример: напомни таблетки каждый день в 23:00", "◉", () => { if (reminder != null) { C.State.Data.Reminders.Add(reminder); C.State.Touch(); C.Notify("Напоминание сохранено"); C.Collapse(); } }); break;
                case CommandKind.Shell: Add("> " + command.Text, "Enter — выполнить в отдельном окне cmd", ">_", () => { SystemService.RunShell(command.Text); C.Collapse(); }); break;
                case CommandKind.Ai:
                    if (C.Ai.IsConfigured) Add(command.Text, "Enter — открыть вопрос в ИИ-чате", "✧", () => { if (!C.Ai.IsConfigured) return; C.AiDraft = command.Text; C.Go("ai"); });
                    else Add("ИИ отключён · добавьте API ключ", "Enter — открыть настройки ИИ", "✧", () => C.Go("settings:ai"));
                    break;
                case CommandKind.System: Add(command.Text, command.Text is "wifi" or "wi-fi" or "bluetooth" ? "Enter — переключить радиомодуль" : "Системная команда", "☷", () => { _ = C.RunSystemCommandAsync(command.Text); C.Collapse(); }); break;
                case CommandKind.Web: Add("Искать в интернете: " + command.Text, "Открыть браузер", "↗", () => SystemService.Open("https://www.google.com/search?q=" + Uri.EscapeDataString(command.Text))); break;
                default:
                    var apps = FindApplications(input);
                    if (apps.Length > 0) { GroupHeading("Приложения"); foreach (var app in apps) AddResult(app); SelectFirst(); }
                    hint.Text = apps.Length > 0 ? "Приложения найдены · ищу файлы…" : "Ищу файлы и папки…";
                    var results = await C.Everything.SearchAsync(input, token); token.ThrowIfCancellationRequested(); if (Disposed) return;
                    // Discovery may finish while Everything is answering the first startup query.
                    var lateApps = FindApplications(input).Where(x => !apps.Any(a => a.Path.Equals(x.Path, StringComparison.OrdinalIgnoreCase))).ToArray();
                    if (lateApps.Length > 0) { if (apps.Length == 0) GroupHeading("Приложения"); foreach (var app in lateApps) AddResult(app); apps = apps.Concat(lateApps).ToArray(); SelectFirst(); }
                    catalog = C.Applications;
                    var known = apps.Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var moreApps = results.Where(IsApplication).Where(x => known.Add(x.Path)).Take(10).ToArray();
                    if (moreApps.Length > 0) { if (apps.Length == 0) GroupHeading("Приложения"); foreach (var app in moreApps) AddResult(app with { Kind = "Приложение" }); SelectFirst(); }
                    var files = results.Where(x => !IsApplication(x) && known.Add(x.Path)).ToArray();
                    if (files.Length > 0) { GroupHeading("Файлы и папки"); foreach (var result in files) AddResult(result); }
                    hint.Text = C.Everything.Status;
                    Add("Искать «" + input + "» в интернете", "Google · Enter", "↗", () => SystemService.Open("https://www.google.com/search?q=" + Uri.EscapeDataString(input)));
                    break;
            }
            SelectFirst();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!Disposed) hint.Text = "Поиск недоступен · " + e.GetType().Name; }
    }
    private void AddResult(SearchResult result) => Add(result.Name, result.Path + (result.Size >= 0 && result.Kind != "Папка" ? " · " + Ui.Bytes(result.Size) : ""), result.Kind == "Папка" ? "▱" : "◇", () =>
    {
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift)) Process.Start(new ProcessStartInfo(result.Path) { UseShellExecute = true, Verb = "runas" });
        else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) SystemService.Reveal(result.Path);
        else SystemService.Open(result.Path);
        C.Collapse();
    }, result);
    private void AddPin() => LauncherActions.Add(C, BuildPins);
    private void BuildPins()
    {
        pins.Children.Clear(); group.Children.Clear();
        foreach (var items in C.State.Data.Launcher.GroupBy(x => string.IsNullOrEmpty(x.Group) ? x.Name + "\0" : x.Group))
        {
            var entries = items.ToArray(); var first = entries[0]; var isGroup = entries.Length > 1 || first.Group.Length > 0;
            var button = Ui.Button(isGroup ? "▦ " + first.Group : first.Name, () =>
            { if (!isGroup) { try { SystemService.Open(first.Target); C.Collapse(); } catch (Exception e) { C.Error(e); } } else { group.Children.Clear(); foreach (var item in entries) group.Children.Add(PinButton(item)); } });
            button.AllowDrop = true; button.Drop += (_, e) => { if (e.Data.GetData("QuietIsland.Launcher") is LaunchItem dragged && dragged != first) { var name = first.Group.Length > 0 ? first.Group : first.Name; first.Group = dragged.Group = name; C.State.Touch(); BuildPins(); e.Handled = true; } };
            FileDrag.Attach(button, () => new DataObject("QuietIsland.Launcher", first));
            button.ContextMenu = PinMenu(first); pins.Children.Add(button);
        }
    }
    private Button PinButton(LaunchItem pin) { var button = Ui.Button(pin.Name, () => { SystemService.Open(pin.Target); C.Collapse(); }); button.ContextMenu = PinMenu(pin); return button; }
    private ContextMenu PinMenu(LaunchItem pin) => LauncherActions.Menu(C, pin, BuildPins);
    public override void Dispose() { C.State.Changed -= ConfigurationChanged; search?.Cancel(); search?.Dispose(); base.Dispose(); }
}
