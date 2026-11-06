using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QuietIsland;
using QuietIsland.Core;
using QuietIsland.Models;
using QuietIsland.Services;
using QuietIsland.Windows;

internal static class ShellBehaviorChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr handle, int id);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
    internal static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        using var context = new IslandContext(Path.Combine(Path.GetTempPath(), "quiet-shell-checks-" + Guid.NewGuid().ToString("N")));
        context.Settings.CaptureFolder = Path.Combine(context.State.DirectoryPath, "captures");
        await check("inline_alarm_keeps_media_transport_visible_with_lyrics", () =>
        {
            context.Lyrics.SetEnabled(true);
            var main = new MainWindow(context, true);
            try
            {
                main.ShowPanel("media", false); Call(main, "TimerFinished", "Таймер завершён");
                var root = (FrameworkElement)main.Content; root.Measure(new(744, 560)); root.Arrange(new(0, 0, 744, 560)); root.UpdateLayout();
                var media = Field<QuietIsland.Views.IslandView>(main, "view"); var transport = Field<object>(media, "transport");
                var play = (Button)transport.GetType().GetProperty("PlayButton")!.GetValue(transport)!;
                var shellHeight = (double)typeof(MainWindow).GetProperty("NotchHeight", Private)!.GetValue(main)!;
                Require(play.TranslatePoint(new Point(0, play.ActualHeight), root).Y <= shellHeight - 14, "Inline alarm clips the music controls under the expanded surface.");
                var art = Field<FrameworkElement>(media, "artwork"); var seek = Field<Slider>(media, "seek");
                Require(art.TranslatePoint(new Point(0, art.ActualHeight), root).Y <= seek.TranslatePoint(new Point(), root).Y + .5, "The inline alarm forces album/lyrics content to overlap the seek controls.");
            }
            finally { main.Close(); context.Lyrics.SetEnabled(false); }
            return Task.CompletedTask;
        });
        await check("home_expands_surface_for_lyrics_saved_recording_and_alarm", () =>
        {
            context.Lyrics.SetEnabled(true);
            typeof(RecordingService).GetProperty(nameof(RecordingService.LastOutputPath))!.SetValue(context.Recording, Path.Combine(context.Settings.CaptureFolder, "fixture.mp4"));
            var main = new MainWindow(context, true);
            try
            {
                main.ShowPanel("home", false);
                var shellHeight = (double)typeof(MainWindow).GetProperty("NotchHeight", Private)!.GetValue(main)!;
                Require(shellHeight >= 500, "Animated shell height clips lyrics and the saved recording notice at " + shellHeight + "px.");
                Call(main, "TimerFinished", "Таймер завершён");
                shellHeight = (double)typeof(MainWindow).GetProperty("NotchHeight", Private)!.GetValue(main)!;
                Require(shellHeight >= 550, "Inline alarm leaves the recording notice outside the shell.");
                var root = (FrameworkElement)main.Content; root.Measure(new(744, 560)); root.Arrange(new(0, 0, 744, 560)); root.UpdateLayout();
                var home = Field<QuietIsland.Views.HomeView>(main, "homeCache"); var saved = Field<Border>(home, "saved");
                Require(saved.Visibility == Visibility.Visible && saved.TranslatePoint(new Point(0, saved.ActualHeight), root).Y < shellHeight - 12, $"Saved recording bottom={saved.TranslatePoint(new Point(0, saved.ActualHeight), root).Y}, shell={shellHeight}, root={root.ActualHeight}, panel={((FrameworkElement)main.FindName("Panel")).ActualHeight}.");
            }
            finally
            {
                main.Close(); context.Lyrics.SetEnabled(false);
                typeof(RecordingService).GetProperty(nameof(RecordingService.LastOutputPath))!.SetValue(context.Recording, null);
            }
            return Task.CompletedTask;
        });
        await check("shortcut_assignments_validate_persist_and_never_replace_conflicts", () =>
        {
            Require(ShortcutChord.TryParse("Ctrl+Shift+S", out var shortcut) && shortcut == new ShortcutChord(6, 0x53), "Shortcut modifiers or key were parsed incorrectly.");
            Require(ShortcutChord.TryParse("Win+F8", out _) && ShortcutChord.TryParse("PrintScreen", out _), "Windows or unmodified Print Screen assignments are unavailable.");
            foreach (var invalid in new[] { "Ctrl", "Ctrl+A+B", "Ctrl++S", "S", "F12", "Ctrl+UnknownKey" })
                Require(!ShortcutChord.TryParse(invalid, out _), "Invalid shortcut was accepted: " + invalid);
            var settings = new AppSettings(); var bindings = GlobalShortcuts.Bindings(settings);
            Require(GlobalShortcuts.ValidationError(settings, "search", bindings["recording"], []) != null, "Duplicate global shortcut was accepted.");
            Require(GlobalShortcuts.ValidationError(settings, "search", "Ctrl+Shift+S", [new() { Hotkey = "Shift+Ctrl+S" }]) != null, "A launcher shortcut conflict was accepted.");
            bindings["search"] = "Ctrl+Alt+Shift+F10";
            var restored = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
            Require(GlobalShortcuts.Bindings(restored)["search"] == bindings["search"], "Saving preferences changed the explicit assignment.");
            using var blocker = new HwndSource(new HwndSourceParameters("QuietIsland.ShortcutBlocker") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
            using var receiver = new HwndSource(new HwndSourceParameters("QuietIsland.ShortcutReceiver") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
            Require(RegisterHotKey(blocker.Handle, 900, 7 | 0x4000, 0x79), "Cannot reserve the test shortcut.");
            var main = new MainWindow(context, true);
            try
            {
                context.Settings.GlobalHotkeys = GlobalShortcuts.Definitions.ToDictionary(x => x.Key, x => x.Key == "search" ? "Ctrl+Alt+Shift+F10" : "");
                typeof(MainWindow).GetField("hwnd", Private)!.SetValue(main, receiver);
                Call(main, "RegisterHotkeys");
                Require(!Field<List<int>>(main, "registered").Contains(1), "An occupied search shortcut was automatically replaced by another chord.");
                Require(main.ShortcutErrors.ContainsKey("search") && context.Settings.GlobalHotkeys["search"] == "Ctrl+Alt+Shift+F10", "An unavailable chord changed preferences or hid the conflict.");
                UnregisterHotKey(blocker.Handle, 900);
                using var settingsView = new QuietIsland.Views.SettingsView(context, main);
                settingsView.Measure(new(650, 650)); settingsView.Arrange(new(0, 0, 650, 650)); settingsView.UpdateLayout();
                var editor = Descendants(settingsView).OfType<TextBox>().Single(x => System.Windows.Automation.AutomationProperties.GetName(x) == "Открыть поиск");
                var assign = ((Panel)editor.Parent).Children.OfType<Button>().Single();
                assign.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(Field<List<int>>(main, "registered").Contains(1) && !main.ShortcutErrors.ContainsKey("search"), "Explicitly assigning the same chord after freeing it did not retry registration.");
                editor.Text = ""; assign.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(!Field<List<int>>(main, "registered").Contains(1) && context.Settings.GlobalHotkeys["search"] == "", "Clearing an assignment did not disable the shortcut.");
            }
            finally { main.Close(); UnregisterHotKey(blocker.Handle, 900); }
            return Task.CompletedTask;
        });
        await check("screenshot_repeated_clipboard_notifications_create_one_history_entry", async () =>
        {
            var image = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
                new byte[] { 1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 255, 10, 11, 12, 255 }, 8);
            image.Freeze();
            await context.Capture.SaveAndCopyAsync(image);
            context.Clipboard.Capture(); context.Clipboard.Capture();
            Require(context.State.Data.Clipboard.Count(x => x.Kind == "image") == 1, "One screenshot was duplicated by repeated Windows clipboard notifications.");
            Clipboard.SetImage(image); context.Clipboard.Capture();
            Require(context.State.Data.Clipboard.Count(x => x.Kind == "image") == 1, "Equivalent delayed image formats duplicated the screenshot.");
        });
        await check("escape_aborts_capture_during_annotation_gesture", () =>
        {
            var image = BitmapSource.Create(20, 20, 96, 96, PixelFormats.Bgra32, null, new byte[1600], 80); image.Freeze();
            var selector = new SelectionWindow(image, new System.Drawing.Rectangle(0, 0, 20, 20), false, true, []);
            Exception? failure = null;
            selector.Loaded += (_, _) => selector.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    var editor = Field<object>(selector, "editor"); Call(editor, "SetTool", "arrow");
                    Call(editor, "BeginShape", new Point(2, 2));
                    var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(selector)!, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    selector.RaiseEvent(key);
                    Require(!selector.IsVisible && selector.Result == null, "Escape cancelled only the stroke and left screenshot selection open.");
                }
                catch (Exception error) { failure = error; }
                finally { selector.Close(); }
            }, DispatcherPriority.ApplicationIdle);
            try { selector.ShowDialog(); if (failure != null) throw failure; }
            finally { selector.Close(); }
            return Task.CompletedTask;
        });
        await check("escape_closes_annotation_text_dialog_and_entire_capture", () =>
        {
            var image = BitmapSource.Create(20, 20, 96, 96, PixelFormats.Bgra32, null, new byte[1600], 80); image.Freeze();
            var previousMain = Application.Current.MainWindow;
            var main = new MainWindow(context, true); Application.Current.MainWindow = main;
            var selector = new SelectionWindow(image, new System.Drawing.Rectangle(0, 0, 20, 20), false, true, []);
            Exception? failure = null; var textDialogClosed = false;
            var deadline = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            void Cleanup()
            {
                foreach (var dialog in Application.Current.Windows.Cast<Window>().Where(x => x.IsVisible && x.Title == "Подпись на снимке").ToArray()) dialog.Close();
                selector.Close();
            }
            deadline.Tick += (_, _) => { failure ??= new InvalidOperationException("Escape did not unwind the nested capture dialog."); deadline.Stop(); Cleanup(); };
            selector.Loaded += (_, _) => selector.Dispatcher.BeginInvoke(() =>
            {
                var editor = Field<object>(selector, "editor");
                selector.Dispatcher.BeginInvoke(() =>
                {
                    var dialog = Application.Current.Windows.Cast<Window>().Single(x => x.IsVisible && x.Title == "Подпись на снимке");
                    try
                    {
                        Require(ReferenceEquals(dialog.Owner, selector), "The annotation text dialog is not owned by the capture selector.");
                        var handle = new WindowInteropHelper(dialog).Handle;
                        Require(PostMessage(handle, 0x100, new IntPtr(0x1B), new IntPtr(1)), "Cannot deliver Escape to the annotation dialog.");
                        PostMessage(handle, 0x101, new IntPtr(0x1B), new IntPtr(unchecked((int)0xC0000001)));
                    }
                    catch (Exception error) { failure = error; Cleanup(); }
                }, DispatcherPriority.ApplicationIdle);
                try
                {
                    editor.GetType().GetMethod("AddText", Private, null, [typeof(Point)], null)!.Invoke(editor, [new Point(2, 2)]);
                    textDialogClosed = true;
                }
                catch (Exception error) { failure ??= error; selector.Close(); }
            }, DispatcherPriority.ApplicationIdle);
            try
            {
                deadline.Start(); selector.ShowDialog();
                if (failure != null) throw failure;
                Require(textDialogClosed && selector.Result == null && !Application.Current.Windows.Cast<Window>().Any(x => x.IsVisible && x.Title == "Подпись на снимке"), "One Escape left part of the capture session open.");
            }
            finally { deadline.Stop(); Cleanup(); Application.Current.MainWindow = previousMain; main.Close(); }
            return Task.CompletedTask;
        });
        await check("maximized_snap_activates_from_title_bar_only", () =>
        {
            var image = BitmapSource.Create(100, 100, 96, 96, PixelFormats.Bgra32, null, new byte[40000], 400); image.Freeze();
            var selector = new SelectionWindow(image, new System.Drawing.Rectangle(0, 0, 100, 100), false, false, [new(0, 0, 100, 100), new(20, 30, 40, 40)]);
            try
            {
                var hotZones = typeof(SelectionWindow).GetField("snapAreas", Private);
                Require(hotZones != null, "Window selection has no maximized title bar hit region.");
                hotZones!.SetValue(selector, new System.Drawing.Rectangle[] { new(0, 0, 100, 10), new(20, 30, 40, 40) });
                var root = (FrameworkElement)selector.Content; root.Width = root.Height = 100; root.Measure(new(100, 100)); root.Arrange(new(0, 0, 100, 100)); root.UpdateLayout();
                Require(((Int32Rect)Call(selector, "WindowAt", new Point(50, 5))!).Width == 100, "Title bar failed to select the maximized window.");
                Require(((Int32Rect)Call(selector, "WindowAt", new Point(50, 50))!).Width == 0, "Maximized client area snapped itself or an obscured window.");
            }
            finally { selector.Close(); }
            return Task.CompletedTask;
        });
        await check("passive_reopen_resets_tab_after_five_seconds", () =>
        {
            var main = new MainWindow(context, true);
            try
            {
                main.ShowPanel("notes", false); main.Collapse();
                var closed = typeof(MainWindow).GetField("lastCollapsed", Private);
                Require(closed != null, "Collapse does not remember when the tab was last used.");
                closed!.SetValue(main, DateTime.UtcNow.AddSeconds(-6)); Call(main, "ReopenPanel");
                Require(Field<string>(main, "selected") == "home", "Passive reopening after five seconds did not open home.");
                main.ShowPanel("notes", false); main.Collapse(); closed.SetValue(main, DateTime.UtcNow); Call(main, "ReopenPanel");
                Require(Field<string>(main, "selected") == "notes", "A quick reopening lost the last tab.");
                main.Collapse(); closed.SetValue(main, DateTime.UtcNow.AddSeconds(-6)); main.ShowPanel("clipboard", false);
                Require(Field<string>(main, "selected") == "clipboard", "Explicit navigation was overridden by the passive tab timeout.");
            }
            finally { main.Close(); }
            return Task.CompletedTask;
        });
        await check("reminder_uses_inline_alarm_with_title_and_ten_minute_snooze", () =>
        {
            var main = new MainWindow(context, true);
            try
            {
                var reminder = new Reminder { Title = "Проверить духовку", Due = DateTimeOffset.Now.AddMinutes(-1), Completed = true };
                context.State.Data.Reminders.Add(reminder); Call(main, "Alarm", reminder.Title, reminder);
                var title = main.FindName("CompactAlarmTitle") as TextBlock;
                Require(title?.Text == reminder.Title, "Reminder did not use the inline alarm with its title.");
                var snooze = (Button)main.FindName("CompactTimerAgain"); var dismiss = (Button)main.FindName("CompactTimerDismiss");
                Require(snooze.Content as string == "+10 минут" && snooze.Width > snooze.Height && dismiss.Width > dismiss.Height, "Reminder actions are not rectangular snooze/dismiss buttons.");
                snooze.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(!reminder.Completed && Math.Abs((reminder.Due - DateTimeOffset.Now).TotalMinutes - 10) < .05, "Reminder did not reschedule for ten minutes.");
            }
            finally { main.Close(); }
            return Task.CompletedTask;
        });
        await check("reminder_queue_and_snooze_preserve_recurring_schedule_and_pending_timer", () =>
        {
            var main = new MainWindow(context, true);
            try
            {
                var nextDaily = DateTimeOffset.Now.AddDays(1);
                var recurring = new Reminder { Title = "Ежедневное лекарство", Daily = true, Due = nextDaily };
                var second = new Reminder { Title = "Второе напоминание", Completed = true };
                context.State.Data.Reminders.Add(recurring); context.State.Data.Reminders.Add(second);
                Call(main, "TimerFinished", "Таймер завершён"); Call(main, "Alarm", recurring.Title, recurring); Call(main, "Alarm", second.Title, second);
                ((Button)main.FindName("CompactTimerAgain")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(recurring.Due == nextDaily && context.State.Data.Reminders.Any(x => x.Id != recurring.Id && x.Title == recurring.Title && !x.Daily && Math.Abs((x.Due - DateTimeOffset.Now).TotalMinutes - 10) < .05), "Snooze moved the daily schedule or failed to create a one-time reminder.");
                Require(((TextBlock)main.FindName("CompactAlarmTitle")).Text == second.Title, "The second simultaneous reminder was lost.");
                ((Button)main.FindName("CompactTimerDismiss")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(((TextBlock)main.FindName("CompactAlarmTitle")).Text == "Таймер" && ((FrameworkElement)main.FindName("CompactAlarm")).Visibility == Visibility.Visible, "Dismissing reminders lost the pending timer alarm.");
                ((Button)main.FindName("CompactTimerDismiss")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(((FrameworkElement)main.FindName("CompactAlarm")).Visibility == Visibility.Collapsed, "Final dismissal left an alarm visible.");
            }
            finally { main.Close(); }
            return Task.CompletedTask;
        });
    }
    private static object? Call(object value, string method, params object?[] args) => value.GetType().GetMethod(method, Private)!.Invoke(value, args);
    private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name, Private)!.GetValue(value)!;
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}
