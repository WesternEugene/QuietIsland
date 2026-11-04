using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace QuietIsland.Controls;

/// <summary>Tasks stay ordinary Unicode text so RTF, plain text, undo and clipboard retain them.</summary>
public sealed class NoteEditor : RichTextBox
{
    private TaskPress? taskPress;

    public NoteEditor()
    {
        AutomationProperties.SetName(this, "Текст заметки");
        AutomationProperties.SetHelpText(this, "Нажмите на флажок ☐ или ☑, чтобы изменить его состояние. С клавиатуры: Ctrl+Пробел рядом с флажком или Пробел при выделенном флажке.");
        // Run after the editor has finished its normal mouse selection and released capture.
        AddHandler(Mouse.MouseUpEvent, new MouseButtonEventHandler(TaskMouseUp), true);
    }

    public void InsertTask()
    {
        if (IsReadOnly) return;
        Focus();
        BeginChange();
        try
        {
            var insertion = CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
            var after = insertion.GetPositionAtOffset(0, LogicalDirection.Forward);
            insertion.InsertTextInRun("☐ ");
            CaretPosition = after;
        }
        finally { EndChange(); }
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.None) BeginTaskClick(e.GetPosition(this), e.ClickCount);
        else taskPress = null;
        base.OnPreviewMouseLeftButtonDown(e);
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        UpdateTaskClick(e.GetPosition(this), e.LeftButton == MouseButtonState.Pressed);
        base.OnPreviewMouseMove(e);
    }

    private void TaskMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (Keyboard.Modifiers == ModifierKeys.None) e.Handled |= CompleteTaskClick(e.GetPosition(this));
        else taskPress = null;
    }

    internal void BeginTaskClick(Point point, int clickCount)
    {
        taskPress = !IsReadOnly && clickCount == 1 && FindTask(point) is { } task
            ? new TaskPress(Document, task.Start, point, SelectionBookmark.Capture(this)) : null;
    }

    internal void UpdateTaskClick(Point point, bool leftButtonDown)
    {
        if (taskPress is { } press && (!leftButtonDown || Dragged(press.Origin, point))) taskPress = null;
    }

    internal bool CompleteTaskClick(Point point)
    {
        var press = taskPress; taskPress = null;
        if (press == null || IsReadOnly || !ReferenceEquals(press.Document, Document) || Dragged(press.Origin, point)) return false;
        var task = FindTask(point);
        if (task == null || task.Start.CompareTo(press.Start) != 0) return false;
        press.Selection.Restore(this);
        return ToggleTaskAt(point);
    }

    protected override void OnQueryCursor(QueryCursorEventArgs e)
    {
        base.OnQueryCursor(e);
        if (!IsReadOnly && FindTask(Mouse.GetPosition(this)) != null) { e.Cursor = Cursors.Hand; e.Handled = true; }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!e.Handled && e.Key == Key.Space && (Keyboard.Modifiers == ModifierKeys.Control || Keyboard.Modifiers == ModifierKeys.None && IsTask(Selection.Text)) && ToggleTaskAtCaret())
        { e.Handled = true; return; }
        base.OnPreviewKeyDown(e);
    }

    internal bool ToggleTaskAt(Point point)
    {
        if (IsReadOnly || FindTask(point) is not { } task) return false;
        return ToggleTask(task);
    }

    internal bool ToggleTaskAtCaret()
    {
        if (IsReadOnly || !Selection.IsEmpty && !IsTask(Selection.Text)) return false;
        var position = (Selection.IsEmpty ? CaretPosition : Selection.Start).GetInsertionPosition(LogicalDirection.Forward);
        var task = TaskStartingAt(position) ?? (Selection.IsEmpty ? TaskStartingAt(position.GetNextInsertionPosition(LogicalDirection.Backward)) : null);
        if (task == null) return false;
        return ToggleTask(task);
    }

    private TextRange? FindTask(Point point)
    {
        var position = GetPositionFromPoint(point, false)?.GetInsertionPosition(LogicalDirection.Forward);
        if (position == null) return null;
        foreach (var candidate in new[] { position, position.GetNextInsertionPosition(LogicalDirection.Backward) })
        {
            var task = TaskStartingAt(candidate);
            if (task == null) continue;
            var bounds = Rect.Union(task.Start.GetCharacterRect(LogicalDirection.Forward), task.End.GetCharacterRect(LogicalDirection.Backward));
            if (!bounds.IsEmpty && bounds.Contains(point)) return task;
        }
        return null;
    }

    private static TextRange? TaskStartingAt(TextPointer? position)
    {
        // A caret may be between adjacent runs; find the next text without crossing a line break.
        for (var pointer = position; pointer != null; pointer = pointer.GetNextContextPosition(LogicalDirection.Forward))
        {
            if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
            {
                var text = pointer.GetTextInRun(LogicalDirection.Forward);
                return text.Length > 0 && text[0] is '☐' or '☑' ? new TextRange(pointer, pointer.GetPositionAtOffset(1)!) : null;
            }
            if (pointer.GetAdjacentElement(LogicalDirection.Forward) is not Inline or LineBreak or InlineUIContainer) return null;
        }
        return null;
    }

    private bool ToggleTask(TextRange task)
    {
        if (task.Start.Parent is not Run run) return false;
        var selection = SelectionBookmark.Capture(this);
        var offset = run.ContentStart.GetOffsetToPosition(task.Start);
        var text = run.Text;
        BeginChange();
        // Replacing TextRange.Text can inherit an adjacent run's formatting. Keep the run intact.
        try { run.Text = text[..offset] + (text[offset] == '☐' ? "☑" : "☐") + text[(offset + 1)..]; selection.Restore(this); }
        finally { EndChange(); }
        return true;
    }

    private static bool IsTask(string text) => text is "☐" or "☑";
    private static bool Dragged(Point origin, Point point) => Math.Abs(point.X - origin.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(point.Y - origin.Y) >= SystemParameters.MinimumVerticalDragDistance;
    private sealed record TaskPress(FlowDocument Document, TextPointer Start, Point Origin, SelectionBookmark Selection);
    private readonly record struct SelectionBookmark(int Start, int End, bool Backward)
    {
        public static SelectionBookmark Capture(NoteEditor editor) => new(editor.Document.ContentStart.GetOffsetToPosition(editor.Selection.Start), editor.Document.ContentStart.GetOffsetToPosition(editor.Selection.End), !editor.Selection.IsEmpty && editor.CaretPosition.CompareTo(editor.Selection.Start) == 0);
        public void Restore(NoteEditor editor)
        {
            var start = editor.Document.ContentStart.GetPositionAtOffset(Start)!;
            var end = editor.Document.ContentStart.GetPositionAtOffset(End)!;
            editor.Selection.Select(Backward ? end : start, Backward ? start : end);
        }
    }
}
