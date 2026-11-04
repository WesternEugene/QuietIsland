using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using QuietIsland.Controls;
using QuietIsland.Core;
using QuietIsland.Models;
using QuietIsland.Services;

namespace QuietIsland.Views;
public sealed class NotesView : IslandView
{
    private readonly ListBox list = new() { Width = 155, Margin = new Thickness(0, 0, 12, 0) };
    private readonly TextBox title = Ui.Input("Название заметки"), folder = Ui.Input("Папка"), reminder = Ui.Input("Напоминание: таблетки каждый день в 23:00");
    private readonly NoteEditor editor = new() { BorderThickness = new Thickness(1), Padding = new Thickness(12), AcceptsTab = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock timerText = Ui.Text("Таймер", 14, bold: true);
    private readonly TextBlock timerHint = Ui.Text("Выберите длительность", 10, "Muted");
    private readonly TimerRing timerRing = new() { Width = 66, Height = 66 };
    private readonly Button pauseTimer;
    private readonly StackPanel reminderList = new();
    private readonly Expander remindersExpander = new() { Header = "Напоминания", Foreground = Ui.Brush("Text"), Margin = new Thickness(0, 9, 0, 0) };
    private Note? note;
    private bool loading;
    public NotesView(IslandContext c) : base(c)
    {
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var dial = new Grid { Width = 66, Height = 66, Margin = new Thickness(0, 0, 14, 0) }; dial.Children.Add(timerRing); timerText.HorizontalAlignment = HorizontalAlignment.Center; timerText.VerticalAlignment = VerticalAlignment.Center; dial.Children.Add(timerText);
        pauseTimer = Ui.Button("", PauseTimer); pauseTimer.Content = Icons.Make("pause", 16); pauseTimer.ToolTip = "Пауза / продолжить";
        var stop = Ui.Button("", () => { C.State.Data.TimerEnd = null; C.State.Data.TimerPaused = false; C.State.Touch(); Refresh(); }); stop.Content = Icons.Make("stop", 16); stop.ToolTip = "Остановить таймер";
        var presets = Ui.Row(Ui.Button("5 мин", () => C.StartTimer(TimeSpan.FromMinutes(5))), Ui.Button("25 мин", () => C.StartTimer(TimeSpan.FromMinutes(25))), Ui.Button("45 мин", () => C.StartTimer(TimeSpan.FromMinutes(45))), Ui.Button("Свой", () => { var input = Windows.Dialogs.Ask("Таймер", "Например 10 мин или 1 час 20 мин", "10 мин"); var time = CommandParser.ParseDuration(input ?? ""); if (time > TimeSpan.Zero) C.StartTimer(time); }), pauseTimer, stop);
        var timer = Ui.Row(dial, Ui.Stack(timerHint, presets)); timer.Margin = new Thickness(0, 0, 0, 12); root.Children.Add(timer);
        var notes = new DockPanel(); Grid.SetRow(notes, 1); root.Children.Add(notes);
        var left = new DockPanel { Width = 170 }; var noteActions = Ui.Row(Ui.Button("+ Заметка", () => { var n = new Note(); C.State.Data.Notes.Add(n); C.State.Touch(); BuildList(n); }), Ui.Button("−", RemoveNote)); DockPanel.SetDock(noteActions, Dock.Top); left.Children.Add(noteActions); left.Children.Add(list); DockPanel.SetDock(left, Dock.Left); notes.Children.Add(left);
        var right = new Grid(); right.RowDefinitions.Add(new() { Height = GridLength.Auto }); right.RowDefinitions.Add(new() { Height = GridLength.Auto }); right.RowDefinitions.Add(new());
        var meta = new Grid(); meta.ColumnDefinitions.Add(new()); meta.ColumnDefinitions.Add(new() { Width = new GridLength(130) }); Grid.SetColumn(folder, 1); title.Margin = new Thickness(0, 0, 8, 4); folder.Margin = new Thickness(0, 0, 0, 4); meta.Children.Add(title); meta.Children.Add(folder); right.Children.Add(meta);
        var format = Ui.Row(Format("B", EditingCommands.ToggleBold), Format("I", EditingCommands.ToggleItalic), Format("U", EditingCommands.ToggleUnderline), Format("•", EditingCommands.ToggleBullets), Format("1.", EditingCommands.ToggleNumbering), Ui.Button("☐", editor.InsertTask, tip: "Вставить флажок · нажмите на него в тексте, чтобы отметить"), Ui.Button("Экспорт", Export)); Grid.SetRow(format, 1); right.Children.Add(format);
        editor.SetResourceReference(ForegroundProperty, "Text"); editor.SetResourceReference(BackgroundProperty, "Card"); editor.SetResourceReference(BorderBrushProperty, "Line"); editor.CaretBrush = Ui.Brush("Accent"); editor.FontSize = 14;
        Ui.AttachEditMenu(editor);
        Grid.SetRow(editor, 2); right.Children.Add(editor); notes.Children.Add(right);
        var reminders = new DockPanel { Height = 140, Margin = new Thickness(0, 5, 0, 0) }; var add = Ui.AsyncButton("+", () => { AddReminder(); return Task.CompletedTask; }, C.Error); DockPanel.SetDock(add, Dock.Right); var inputRow = new DockPanel(); inputRow.Children.Add(add); inputRow.Children.Add(reminder); DockPanel.SetDock(inputRow, Dock.Top); reminders.Children.Add(inputRow); reminders.Children.Add(Ui.Scroll(reminderList)); remindersExpander.Content = reminders; Grid.SetRow(remindersExpander, 2); root.Children.Add(remindersExpander); Content = root;
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is ListBoxItem item && item.Tag is Note n) Load(n); };
        editor.TextChanged += (_, _) => SaveNote(); title.TextChanged += (_, _) => SaveNote(); folder.TextChanged += (_, _) => SaveNote();
        title.LostKeyboardFocus += (_, _) => BuildList(note); folder.LostKeyboardFocus += (_, _) => BuildList(note);
        reminder.KeyDown += (_, e) => { if (e.Key == Key.Enter) { AddReminder(); e.Handled = true; } };
        BuildList(C.State.Data.Notes.FirstOrDefault()); BuildReminders(); Refresh();
    }
    private Button Format(string label, RoutedUICommand command)
    {
        var button = Ui.Button(label, () => { editor.Focus(); command.Execute(null, editor); });
        var text = new TextBlock { Text = label };
        if (command == EditingCommands.ToggleBold) { text.FontWeight = FontWeights.Bold; button.ToolTip = "Полужирный · Ctrl+B"; }
        else if (command == EditingCommands.ToggleItalic) { text.FontStyle = FontStyles.Italic; button.ToolTip = "Курсив · Ctrl+I"; }
        else if (command == EditingCommands.ToggleUnderline) { text.TextDecorations = TextDecorations.Underline; button.ToolTip = "Подчёркнутый · Ctrl+U"; }
        button.Content = text; button.Padding = new Thickness(8, 4, 8, 4); return button;
    }
    private void BuildList(Note? select)
    {
        loading = true; list.Items.Clear();
        foreach (var n in C.State.Data.Notes.OrderBy(x => x.Folder).ThenByDescending(x => x.Modified))
        { var item = new ListBoxItem { Tag = n, Content = Ui.Stack(Ui.Text(n.Title, 12, bold: true), Ui.Text(n.Folder, 10, "Muted")) }; list.Items.Add(item); if (n == select) list.SelectedItem = item; }
        loading = false; if (select != null) Load(select);
    }
    private void Load(Note n)
    {
        loading = true; note = n; title.Text = n.Title; folder.Text = n.Folder; editor.Document = new FlowDocument();
        try { if (n.Rtf.Length > 0) { using var stream = new MemoryStream(Convert.FromBase64String(n.Rtf)); new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Load(stream, DataFormats.Rtf); } else editor.Document.Blocks.Add(new Paragraph(new Run(n.Text))); }
        catch { editor.Document.Blocks.Add(new Paragraph(new Run(n.Text))); }
        loading = false;
    }
    private void SaveNote()
    {
        if (loading || note == null) return;
        var range = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd); using var stream = new MemoryStream(); range.Save(stream, DataFormats.Rtf);
        note.Rtf = Convert.ToBase64String(stream.ToArray()); note.Text = range.Text; note.Title = title.Text; note.Folder = folder.Text; note.Modified = DateTimeOffset.Now; C.State.Touch();
    }
    private void RemoveNote() { if (note == null) return; C.State.Data.Notes.Remove(note); if (C.State.Data.Notes.Count == 0) C.State.Data.Notes.Add(new()); C.State.Touch(); BuildList(C.State.Data.Notes[0]); }
    internal static Microsoft.Win32.SaveFileDialog CreateExportDialog(string title) => new() { FileName = title, Filter = "Текст (*.txt)|*.txt|Rich Text (*.rtf)|*.rtf", FilterIndex = 1, DefaultExt = ".txt", AddExtension = true };
    private void Export() { if (note == null) return; var dialog = CreateExportDialog(note.Title); if (dialog.ShowDialog() == true) { var range = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd); using var file = File.Create(dialog.FileName); range.Save(file, Path.GetExtension(dialog.FileName).Equals(".rtf", StringComparison.OrdinalIgnoreCase) ? DataFormats.Rtf : DataFormats.Text); } }
    private void PauseTimer()
    {
        var data = C.State.Data; if (data.TimerEnd == null) return;
        if (data.TimerPaused) { data.TimerEnd = DateTimeOffset.Now.AddSeconds(data.TimerRemaining); data.TimerPaused = false; }
        else { data.TimerRemaining = Math.Max(0, (data.TimerEnd.Value - DateTimeOffset.Now).TotalSeconds); data.TimerPaused = true; }
        C.State.Touch(); Refresh();
    }
    private void AddReminder()
    {
        var parsed = ReminderParser.Parse(reminder.Text, DateTimeOffset.Now);
        if (parsed == null) { C.Notify("Укажите время: завтра в 9, через 20 минут, каждый день в 23:00"); return; }
        C.State.Data.Reminders.Add(parsed); C.State.Touch(); reminder.Clear(); BuildReminders();
    }
    private void BuildReminders()
    {
        reminderList.Children.Clear();
        remindersExpander.Header = "Напоминания · " + C.State.Data.Reminders.Count(x => !x.Completed);
        foreach (var r in C.State.Data.Reminders.OrderBy(x => x.Completed).ThenBy(x => x.Due))
        {
            var row = Ui.Row(Ui.Button(r.Completed ? "☑" : "☐", () => { r.Completed = !r.Completed; C.State.Touch(); BuildReminders(); }), Ui.Button(r.Title + " · " + r.Due.ToString("dd.MM HH:mm") + (r.Daily ? " ↻" : ""), () => { var text = Windows.Dialogs.Ask("Изменить напоминание", "Название и время", r.Title + (r.Daily ? " каждый день" : " " + r.Due.ToString("yyyy-MM-dd")) + " в " + r.Due.ToString("HH:mm")); var changed = ReminderParser.Parse(text ?? "", DateTimeOffset.Now); if (changed != null) { C.State.Data.Reminders[C.State.Data.Reminders.IndexOf(r)] = changed; C.State.Touch(); BuildReminders(); } }), Ui.Button("×", () => { C.State.Data.Reminders.Remove(r); C.State.Touch(); BuildReminders(); })); reminderList.Children.Add(row);
        }
    }
    public override void Refresh()
    {
        var data = C.State.Data; var active = data.TimerEnd != null;
        var remaining = active ? Math.Max(0, data.TimerPaused ? data.TimerRemaining : (data.TimerEnd!.Value - DateTimeOffset.Now).TotalSeconds) : 0;
        timerText.Text = active ? Ui.Time(remaining) : "Таймер";
        timerHint.Text = !active ? "Выберите длительность" : data.TimerPaused ? "Таймер на паузе" : "Таймер идёт · часы остаются в чёлке";
        timerRing.Progress = active ? remaining / Math.Max(1, data.TimerDuration > 0 ? data.TimerDuration : remaining) : 0;
        timerRing.Paused = data.TimerPaused; pauseTimer.IsEnabled = active; pauseTimer.Content = Icons.Make(data.TimerPaused ? "play" : "pause", 16);
    }
    public override void FocusInput() => editor.Focus();
}
