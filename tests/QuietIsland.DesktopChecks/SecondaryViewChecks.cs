using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QuietIsland.Controls;
using QuietIsland.Models;
using QuietIsland.Services;
using QuietIsland.Views;

internal static class SecondaryViewChecks
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;

    public static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "quiet-island-secondary-" + Guid.NewGuid().ToString("N"));
        using var context = new IslandContext(directory);
        context.State.Data.Notes.Clear();
        var note = new Note { Title = "Задача", Text = "☐ первая задача" };
        context.State.Data.Notes.Add(note);

        await check("note_task_release_move_preserves_click_save_and_undo", () =>
        {
            using var view = new NotesView(context);
            using var source = Host(view, 680, 350);
            var editor = Descendants(view).OfType<NoteEditor>().Single();
            var task = TaskRange(editor);
            task.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
            editor.UpdateLayout();
            var caret = task.End.GetPositionAtOffset(3)!;
            editor.Selection.Select(caret, caret);
            var offset = editor.Document.ContentStart.GetOffsetToPosition(editor.CaretPosition);
            editor.IsUndoEnabled = false; editor.IsUndoEnabled = true;
            var point = TaskPoint(editor);

            // The captured click may see a final move with the released button
            // before its bubbling MouseUp. It must remain a click at this point.
            Invoke(editor, "BeginTaskClick", point, 1);
            Invoke(editor, "UpdateTaskClick", point, false);
            Require((bool)Invoke(editor, "CompleteTaskClick", point)!, "A release-state move discarded the checkbox click before MouseUp.");
            Require(note.Text.StartsWith("☑ первая задача"), "Toggling the checkbox did not save the note's plain text.");
            Require(editor.Document.ContentStart.GetOffsetToPosition(editor.CaretPosition) == offset, "Clicking a task moved the text selection.");
            Require(Equals(TaskRange(editor).GetPropertyValue(TextElement.FontWeightProperty), FontWeights.Bold), "Toggling a task discarded its formatting.");

            using (var restored = new NotesView(context))
            {
                using var restoredSource = Host(restored, 680, 350);
                var restoredEditor = Descendants(restored).OfType<NoteEditor>().Single();
                Require(TaskRange(restoredEditor).Text == "☑", "Reloading saved RTF discarded the completed checkbox.");
                Require(Equals(TaskRange(restoredEditor).GetPropertyValue(TextElement.FontWeightProperty), FontWeights.Bold), "Reloading the task lost its formatting.");
            }
            editor.Undo();
            Require(note.Text.StartsWith("☐ первая задача"), "Undo did not restore and save the original task state.");
            return Task.CompletedTask;
        });

        await check("note_task_drag_is_not_a_click", () =>
        {
            var editor = new NoteEditor { Document = new FlowDocument(new Paragraph(new Run("☐ задача"))) };
            using var source = Host(editor, 400, 120);
            var point = TaskPoint(editor);
            Invoke(editor, "BeginTaskClick", point, 1);
            Invoke(editor, "UpdateTaskClick", new Point(point.X + SystemParameters.MinimumHorizontalDragDistance + 2, point.Y), true);
            Require(!(bool)Invoke(editor, "CompleteTaskClick", point)!, "A text-selection drag toggled the task.");
            Require(TaskRange(editor).Text == "☐", "Dragging changed the checkbox text.");
            return Task.CompletedTask;
        });

        await check("note_task_native_click_toggles_and_persists", async () =>
        {
            note.Text = "☐ первая задача"; note.Rtf = "";
            using var view = new NotesView(context);
            var area = SystemParameters.WorkArea;
            var window = new Window
            {
                Title = "Quiet Island · проверка флажка", Content = view, Width = 700, Height = 390,
                Left = area.Left + Math.Max(0, (area.Width - 700) / 2), Top = area.Top + Math.Max(0, (area.Height - 390) / 2),
                ShowInTaskbar = false, Topmost = true, ResizeMode = ResizeMode.NoResize,
                Background = Ui.Brush("Surface"), Foreground = Ui.Brush("Text")
            };
            Require(GetCursorPos(out var originalCursor), "Cannot preserve the cursor before the native click check.");
            var originalForeground = GetForegroundWindow();
            var mouseDown = false;
            try
            {
                window.Show(); window.Activate();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(80);
                var editor = Descendants(view).OfType<NoteEditor>().Single();
                editor.Focus(); window.UpdateLayout();
                var trace = new List<string>();
                void Trace(MouseEventArgs e)
                {
                    if (trace.Count >= 20) return;
                    var p = e.GetPosition(editor);
                    var pending = typeof(NoteEditor).GetField("taskPress", Members)!.GetValue(editor) != null;
                    trace.Add($"{e.RoutedEvent.Name}@{p.X:0.0},{p.Y:0.0} left={e.LeftButton} pending={pending}");
                }
                editor.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((_, e) => Trace(e)), true);
                editor.AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler((_, e) => Trace(e)), true);
                editor.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler((_, e) => Trace(e)), true);
                editor.AddHandler(Mouse.MouseUpEvent, new MouseButtonEventHandler((_, e) => Trace(e)), true);
                var screenPoint = editor.PointToScreen(TaskPoint(editor));
                Require(SetCursorPos((int)Math.Round(screenPoint.X), (int)Math.Round(screenPoint.Y)), "Windows rejected positioning the cursor on the checkbox.");
                await Task.Delay(60);
                Require(MouseInput(0x0002) == 1, "Windows rejected the native mouse-down input."); mouseDown = true;
                await Task.Delay(40);
                Require(MouseInput(0x0004) == 1, "Windows rejected the native mouse-up input."); mouseDown = false;
                await Task.Delay(100); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                SavePreview(view, "notes-680x340.png");
                Require(note.Text.StartsWith("☑ первая задача"), "A real Windows mouse click did not complete the checkbox. " + string.Join("; ", trace));
                context.State.Save();
                var reopened = new DesktopState(directory).Data.Notes.Single(x => x.Title == "Задача");
                Require(reopened.Text.StartsWith("☑ первая задача"), "The native checkbox click did not persist to the saved note.");
                var document = new FlowDocument();
                using var rtf = new MemoryStream(Convert.FromBase64String(reopened.Rtf));
                var range = new TextRange(document.ContentStart, document.ContentEnd); range.Load(rtf, DataFormats.Rtf);
                Require(range.Text.StartsWith("☑ первая задача"), "The native checkbox click did not persist in RTF.");
            }
            finally
            {
                if (mouseDown) MouseInput(0x0004);
                window.Close();
                SetCursorPos(originalCursor.X, originalCursor.Y);
                if (originalForeground != IntPtr.Zero) SetForegroundWindow(originalForeground);
            }
        });

        await check("weather_forecast_rows_scroll_independently_and_survive_refresh", () =>
        {
            var forecast = new WeatherForecast(8, 6, 3, 4.5, 65,
                Enumerable.Range(0, 24).Select(i => new WeatherHour(DateTime.Today.AddHours(i).ToString("s"), 8 + i, 10, 3)).ToList(),
                Enumerable.Range(0, 7).Select(i => new WeatherDay(DateTime.Today.AddDays(i).ToString("yyyy-MM-dd"), 2, 9, 3)).ToList(), DateTimeOffset.Now);
            typeof(WeatherService).GetProperty(nameof(WeatherService.Current))!.SetValue(context.Weather, forecast);
            using var view = new WeatherView(context);
            using var source = Host(view, 680, 340);
            var rows = Descendants(view).OfType<ScrollViewer>()
                .Where(x => x.Content is StackPanel { Orientation: Orientation.Horizontal } && x.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto).ToArray();
            Require(rows.Length == 2, "Hourly and daily forecasts do not each have a horizontal scroller.");
            Require(rows.All(x => x.ScrollableWidth > 100), "Forecast rows expand the page instead of remaining horizontally scrollable.");
            rows[0].ScrollToHorizontalOffset(120); rows[1].ScrollToHorizontalOffset(160); view.UpdateLayout();
            var firstOffset = rows[0].HorizontalOffset; var secondOffset = rows[1].HorizontalOffset;
            view.Refresh(); view.UpdateLayout();
            var refreshed = Descendants(view).OfType<ScrollViewer>().Where(x => x.Content is StackPanel { Orientation: Orientation.Horizontal }).ToArray();
            Require(ReferenceEquals(rows[0], refreshed[0]) && ReferenceEquals(rows[1], refreshed[1]), "Polling weather rebuilt the forecast rows.");
            Require(firstOffset >= 119 && secondOffset >= 159 && Math.Abs(refreshed[0].HorizontalOffset - firstOffset) < .1 && Math.Abs(refreshed[1].HorizontalOffset - secondOffset) < .1, "Refreshing weather reset a forecast scroll position.");
            rows[0].ScrollToLeftEnd(); rows[1].ScrollToLeftEnd(); view.UpdateLayout(); SavePreview(view, "weather-680x340.png");
            return Task.CompletedTask;
        });

        await check("weather_controls_and_precipitation_fit_680_by_340", () =>
        {
            context.Settings.WeatherCity = "Екатеринбург";
            typeof(WeatherService).GetProperty(nameof(WeatherService.Current))!.SetValue(context.Weather,
                new WeatherForecast(-18, -24, 71, 4.5, 65,
                    Enumerable.Range(0, 24).Select(i => new WeatherHour(DateTime.Today.AddHours(i).ToString("s"), -18 + i, 75, 71)).ToList(),
                    Enumerable.Range(0, 7).Select(i => new WeatherDay(DateTime.Today.AddDays(i).ToString("yyyy-MM-dd"), -20, -12, 71)).ToList(), DateTimeOffset.Now));
            using var view = new WeatherView(context);
            using var source = Host(view, 680, 340);
            var failures = new List<string>();
            var outer = (ScrollViewer)view.Content;
            if (outer.ScrollableHeight > .5) failures.Add($"Weather requires {outer.ScrollableHeight:0.0}px of vertical scrolling at 680x340.");
            var required = Descendants(view).OfType<FrameworkElement>().Where(x => x is TextBox || x is Button button && (Equals(button.ToolTip, "Найти город") || Equals(button.ToolTip, "Обновить")) || AutomationProperties.GetName(x) == "Текущая погода").ToArray();
            Require(required.Length == 4, "City search, refresh or current-weather card is missing.");
            foreach (var control in required)
            {
                var bounds = control.TransformToAncestor(view).TransformBounds(new Rect(new Point(), control.RenderSize));
                if (bounds.Left < -.1 || bounds.Right > 680.1 || bounds.Top < -.1 || bounds.Bottom > 340.1) failures.Add($"Weather control {control.GetType().Name} extends outside the 340px pane: {bounds}.");
            }
            var rows = Descendants(view).OfType<ScrollViewer>().Where(x => x.Content is StackPanel { Orientation: Orientation.Horizontal }).ToArray();
            Require(rows.Length == 2, "Weather must preserve independent hourly and daily rows.");
            var precipitation = Descendants(rows[0]).OfType<TextBlock>().Where(x => Equals(x.ToolTip, "Вероятность осадков")).ToArray();
            Require(precipitation.Length == 24, "Hourly forecast lost precipitation labels.");
            foreach (var row in rows)
            {
                var viewport = Descendants(row).OfType<ScrollContentPresenter>().First();
                foreach (var label in Descendants((StackPanel)row.Content).OfType<TextBlock>())
                {
                    var bounds = label.TransformToAncestor(viewport).TransformBounds(new Rect(new Point(), label.RenderSize));
                    if (bounds.Top < -.1 || bounds.Bottom > viewport.ActualHeight + .1) failures.Add($"Forecast label {label.Text} is clipped by the horizontal scrollbar (bottom {bounds.Bottom:0.0}, viewport {viewport.ActualHeight:0.0}).");
                }
            }
            SavePreview(view, "weather-bounds-680x340.png");
            Require(failures.Count == 0, string.Join(" ", failures.Distinct()));
            return Task.CompletedTask;
        });
    }

    private static HwndSource Host(FrameworkElement view, int width, int height)
    {
        var source = new HwndSource(new HwndSourceParameters("QuietIsland.SecondaryViewCheck") { ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = width, Height = height });
        source.RootVisual = view;
        view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout();
        return source;
    }

    private static TextRange TaskRange(NoteEditor editor)
    {
        var paragraph = editor.Document.Blocks.OfType<Paragraph>().First();
        var start = paragraph.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
        return new TextRange(start, start.GetPositionAtOffset(1)!);
    }
    private static Point TaskPoint(NoteEditor editor)
    {
        var task = TaskRange(editor);
        var bounds = Rect.Union(task.Start.GetCharacterRect(LogicalDirection.Forward), task.End.GetCharacterRect(LogicalDirection.Backward));
        Require(!bounds.IsEmpty && bounds.Width > 0, "Checkbox fixture has no rendered text bounds.");
        return new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    }
    private static object? Invoke(NoteEditor editor, string method, params object[] arguments) => typeof(NoteEditor).GetMethod(method, Members)!.Invoke(editor, arguments);
    private static void SavePreview(FrameworkElement view, string name)
    {
        var folder = Path.GetFullPath("artifacts/secondary-view-previews"); Directory.CreateDirectory(folder);
        view.Measure(new Size(680, 340)); view.Arrange(new Rect(0, 0, 680, 340)); view.UpdateLayout();
        var image = new RenderTargetBitmap(680, 340, 96, 96, PixelFormats.Pbgra32); image.Render(view);
        CaptureService.SavePng(image, Path.Combine(folder, name));
    }
    private static uint MouseInput(uint flags) => SendInput(1, [new NativeInput { Mouse = new NativeMouseInput { Flags = flags } }], Marshal.SizeOf<NativeInput>());
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeMouseInput { public int X, Y; public uint MouseData, Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeInput { public uint Type; public NativeMouseInput Mouse; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
