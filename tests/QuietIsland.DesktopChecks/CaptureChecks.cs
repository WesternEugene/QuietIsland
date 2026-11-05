using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using System.Xml.Linq;
using QuietIsland;
using QuietIsland.Services;
using QuietIsland.Windows;

internal static class CaptureChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [StructLayout(LayoutKind.Sequential)] private struct Frame { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Frame value, int size);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Frame value);

    internal static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        async Task Check(string name, Func<Task> assertion)
        {
            Console.WriteLine("CAPTURE " + name);
            await check(name, assertion);
        }
        using var context = new IslandContext(Path.Combine(Path.GetTempPath(), "quiet-island-capture-" + Guid.NewGuid().ToString("N")));
        context.Settings.CaptureFolder = Path.Combine(context.State.DirectoryPath, "captures");
        var image = Fixture();
        await Check("window_snapshot_includes_normal_app_windows_and_ignores_own_overlay", async () =>
        {
            var fixture = new Window { Title = "QuietIsland capture fixture", Left = 120, Top = 120, Width = 300, Height = 200, Content = new TextBlock { Text = "Capture fixture" } };
            try
            {
                fixture.Show(); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var handle = new WindowInteropHelper(fixture).Handle;
                if (DwmGetWindowAttribute(handle, 9, out var frame, Marshal.SizeOf<Frame>()) != 0) GetWindowRect(handle, out frame);
                var expected = System.Drawing.Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom);
                var snapshot = typeof(CaptureService).Assembly.GetType("QuietIsland.Services.CaptureWindows")!.GetMethod("Snapshot", BindingFlags.Static | BindingFlags.NonPublic)!;
                Require(((IReadOnlyList<System.Drawing.Rectangle>)snapshot.Invoke(null, null)!).Contains(expected), "The app's regular settings/dialog window was incorrectly excluded from window snapping.");
                typeof(CaptureService).Assembly.GetType("QuietIsland.Services.Native")!.GetMethod("ConfigureOverlay")!.Invoke(null, [fixture, true, false, false]);
                Require(!((IReadOnlyList<System.Drawing.Rectangle>)snapshot.Invoke(null, null)!).Contains(expected), "The application's own overlay remained a snapping target.");
            }
            finally { fixture.Close(); }
        });
        var desktop = new System.Drawing.Rectangle(-80, -20, 120, 80);
        System.Drawing.Rectangle[] windows = [new(-60, -10, 40, 30), new(-100, -40, 100, 90)];
        await Check("window_hover_respects_stacking_and_negative_screen_origin", () =>
        {
            var selector = new SelectionWindow(image, desktop, false, true, windows);
            try
            {
                Layout(selector, 240, 160);
                Call(selector, "Move", new Point(50, 30));
                var result = (CaptureResult)Call(selector, "CreateResult")!;
                Require(result.Bounds == new System.Drawing.Rectangle(-60, -10, 40, 30), "Hover selected an obscured window or lost negative screen coordinates.");
                Require(EqualPixels(result.Image, new CroppedBitmap(image, new Int32Rect(20, 10, 40, 30))), "Selection UI entered the screenshot or the wrong pixels were cropped.");
                Call(selector, "Move", new Point(10, 100));
                result = (CaptureResult)Call(selector, "CreateResult")!;
                Require(result.Bounds == new System.Drawing.Rectangle(-80, -20, 80, 70), "Off-screen window was not clipped to the virtual desktop.");
            }
            finally { selector.Close(); }
            return Task.CompletedTask;
        });
        await Check("reverse_drag_includes_last_row_and_column_at_scaled_display_size", () =>
        {
            var selector = new SelectionWindow(image, desktop, false, true, []);
            try
            {
                Layout(selector, 180, 120);
                var rect = (Int32Rect)Call(selector, "DragBounds", new Point(180, 120), new Point(-15, -15))!;
                Require(rect == new Int32Rect(0, 0, 120, 80), "Dragging to screen edges lost a row/column or failed to clamp the pointer.");
                rect = (Int32Rect)Call(selector, "DragBounds", new Point(0, 0), new Point(178.5, 118.5))!;
                Require(rect == new Int32Rect(0, 0, 120, 80), "The last reachable physical pixel did not include the last image row and column at scaled display size.");
                Layout(selector, 120, 80);
                rect = (Int32Rect)Call(selector, "DragBounds", new Point(119, 79), new Point(0, 0))!;
                Require(rect == new Int32Rect(0, 0, 120, 80), "A reverse drag from the last physical pixel lost the outside image edge.");
            }
            finally { selector.Close(); }
            return Task.CompletedTask;
        });
        await Check("window_single_click_finishes_without_entering_annotation", () =>
        {
            var selector = new SelectionWindow(image, desktop, false, true, windows);
            selector.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                var overlay = Field<Canvas>(selector, "overlay");
                var point = new Point(25 * overlay.ActualWidth / 120, 15 * overlay.ActualHeight / 80);
                Call(selector, "Move", point); Call(selector, "Down", point); Call(selector, "Up", point);
            }));
            var deadline = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            deadline.Tick += (_, _) => { deadline.Stop(); selector.Close(); };
            deadline.Start();
            try
            {
                Require(selector.ShowDialog() == true && selector.Result?.Bounds == new System.Drawing.Rectangle(-60, -10, 40, 30), "A single click did not finish the highlighted window capture.");
                Require(selector.Result?.Image.PixelWidth == 40 && selector.Result.Image.PixelHeight == 30, "A window click did not crop the highlighted window.");
            }
            finally { deadline.Stop(); selector.Close(); }
            return Task.CompletedTask;
        });
        await Check("area_mouse_up_finishes_capture_without_confirmation", () =>
        {
            var selector = new SelectionWindow(image, desktop, false, true, windows);
            try
            {
                var completed = InDialog(selector, () =>
                {
                    var overlay = Field<Canvas>(selector, "overlay");
                    var a = new Point(20 * overlay.ActualWidth / 120, 10 * overlay.ActualHeight / 80);
                    var b = new Point(60 * overlay.ActualWidth / 120, 40 * overlay.ActualHeight / 80);
                    Call(selector, "Move", a); Call(selector, "Down", a);
                    Require(selector.Result == null, "Mouse-down captured a window before the user could drag an area.");
                    Call(selector, "Move", b);
                    Require(selector.Result == null, "Moving the selection completed capture before mouse-up.");
                    Call(selector, "Up", b);
                    Require(selector.Result != null, "Releasing the selection did not immediately finish capture.");
                });
                Require(completed && selector.Result?.Bounds == new System.Drawing.Rectangle(-60, -10, 40, 30), "Area capture still requires a confirmation step.");
                Require(EqualPixels(new CroppedBitmap(image, new Int32Rect(20, 10, 40, 30)), selector.Result!.Image), "Area mouse-up captured the wrong image pixels.");
            }
            finally { selector.Close(); }
            return Task.CompletedTask;
        });
        await Check("drawing_tools_are_available_before_selecting_any_region", () =>
        {
            var selector = new SelectionWindow(image, desktop, false, true, windows);
            try
            {
                Layout(selector, 240, 160);
                Require(typeof(SelectionWindow).GetField("editor", Private)!.GetValue(selector) != null, "Annotation is unavailable before the final selection gesture.");
                var editor = Field<object>(selector, "editor");
                var labels = Descendants((DependencyObject)Field<Border>(selector, "toolbar")).OfType<Button>().Select(x => AutomationProperties.GetName(x)).ToArray();
                Require(new[] { "Инструмент: Перо", "Инструмент: Стрелка", "Инструмент: Рамка", "Инструмент: Текст", "Инструмент: Скрыть область", "Режим: Снимок" }.All(labels.Contains), "Pre-capture drawing tools or capture mode are missing.");
                Call(editor, "SetTool", "blur"); Call(editor, "BeginShape", new Point(1.2, 2.1)); Call(editor, "FinishShape", new Point(13.4, 14.6));
                var redacted = (BitmapSource)Call(editor, "Export")!;
                Require(Pixel(redacted, 1, 2).SequenceEqual(new byte[] { 0, 0, 0, 255 }) && Pixel(redacted, 13, 14).SequenceEqual(new byte[] { 0, 0, 0, 255 }), "Redaction did not fully cover every intersected pixel.");
                Require(selector.Result == null, "Finishing a redaction gesture triggered capture.");
                Require(Pixel(redacted, 0, 0).SequenceEqual(Pixel(image, 0, 0)), "Redaction damaged pixels outside its rectangle.");
                Call(editor, "Undo"); Require(EqualPixels(image, (BitmapSource)Call(editor, "Export")!), "Undo did not restore the original image.");
                Call(editor, "SetTool", "arrow"); Call(editor, "BeginShape", new Point(2, 5)); Call(editor, "FinishShape", new Point(34, 24));
                Require(!EqualPixels(image, (BitmapSource)Call(editor, "Export")!), "Arrow was not included in the final image.");
                Call(editor, "Undo");
                Call(editor, "SetTool", "pen");
                var ink = Field<InkCanvas>(editor, "ink");
                var stroke = new Stroke(new StylusPointCollection([new StylusPoint(3, 3), new StylusPoint(20, 16)]), ink.DefaultDrawingAttributes.Clone());
                ink.Strokes.Add(stroke); ink.RaiseEvent(new InkCanvasStrokeCollectedEventArgs(stroke) { RoutedEvent = InkCanvas.StrokeCollectedEvent });
                Require(!EqualPixels(image, (BitmapSource)Call(editor, "Export")!), "Pen strokes were omitted from the image.");
                Call(editor, "Undo"); Require(EqualPixels(image, (BitmapSource)Call(editor, "Export")!), "Pen undo failed.");
            }
            finally { selector.Close(); }
            return Task.CompletedTask;
        });
        foreach (var captureWindow in new[] { true, false })
        {
            await Check(captureWindow ? "window_click_crops_existing_desktop_annotations" : "area_release_crops_existing_desktop_annotations", () =>
            {
                var selector = new SelectionWindow(image, desktop, false, true, windows);
                try
                {
                    Layout(selector, 240, 160);
                    var editor = Field<object>(selector, "editor");
                    Call(editor, "SetTool", "blur");
                    Call(editor, "BeginShape", new Point(0, 0)); Call(editor, "FinishShape", new Point(8, 8));
                    Call(editor, "BeginShape", new Point(22, 12)); Call(editor, "FinishShape", new Point(30, 20));
                    Call(editor, "SetTool", "arrow"); Call(editor, "BeginShape", new Point(32, 14)); Call(editor, "FinishShape", new Point(56, 32));
                    Call(selector, "Down", new Point(50, 30)); Call(selector, "Up", new Point(50, 30));
                    Require(selector.Result == null, "Drawing mode routed a drawing gesture into capture.");
                    Require(((UIElement)editor).IsHitTestVisible, "Drawing mode cannot receive pointer gestures.");
                    var captureMode = Descendants(Field<Border>(selector, "toolbar")).OfType<Button>().Single(x => AutomationProperties.GetName(x) == "Режим: Снимок");
                    captureMode.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    Require(selector.Result == null, "Choosing capture mode saved a screenshot before a window/area gesture.");
                    Require(!((UIElement)editor).IsHitTestVisible, "Capture mode still routes pointer gestures to drawing.");
                    var completed = InDialog(selector, () =>
                    {
                        var overlay = Field<Canvas>(selector, "overlay");
                        Point Display(double x, double y) => new(x * overlay.ActualWidth / 120, y * overlay.ActualHeight / 80);
                        var a = captureWindow ? Display(25, 15) : Display(18, 8);
                        var b = captureWindow ? a : Display(62, 42);
                        Call(selector, "Move", a); Call(selector, "Down", a); Call(selector, "Move", b); Call(selector, "Up", b);
                    });
                    Require(completed && selector.Result != null, "Annotated capture did not finish on the mouse gesture.");
                    var captured = selector.Result!.Image;
                    Require(selector.Result.Bounds == (captureWindow ? new System.Drawing.Rectangle(-60, -10, 40, 30) : new System.Drawing.Rectangle(-62, -12, 44, 34)), "Annotated crop lost its physical desktop position.");
                    Require(Pixel(captured, 0, 0).SequenceEqual(captureWindow ? new byte[] { 40, 30, 220, 255 } : new byte[] { 36, 24, 220, 255 }), "The crop shifted source pixels or included the redaction outside selection.");
                    Require(Pixel(captured, captureWindow ? 2 : 4, captureWindow ? 2 : 4).SequenceEqual(new byte[] { 0, 0, 0, 255 }), "Pre-capture redaction was missing or shifted in the selected image.");
                    Require(Pixel(captured, captureWindow ? 20 : 22, captureWindow ? 10 : 12).SequenceEqual(new byte[] { 0, 69, 255, 255 }), "Pre-capture arrow was missing or shifted in the selected image.");
                    Require(Pixel(captured, captured.PixelWidth - 1, captured.PixelHeight - 1).SequenceEqual(captureWindow ? new byte[] { 118, 117, 220, 255 } : new byte[] { 122, 123, 220, 255 }), "The final crop changed edge pixels.");
                }
                finally { selector.Close(); }
                return Task.CompletedTask;
            });
        }
        await Check("non_screenshot_region_and_color_picker_keep_immediate_completion", () =>
        {
            var region = new SelectionWindow(image, desktop, false, false, []);
            try
            {
                Require(Field<Border>(region, "toolbar").Visibility == Visibility.Collapsed, "OCR/QR/recording selection gained screenshot drawing controls.");
                Require(InDialog(region, () =>
                {
                    var overlay = Field<Canvas>(region, "overlay");
                    var a = new Point(20 * overlay.ActualWidth / 120, 10 * overlay.ActualHeight / 80);
                    var b = new Point(60 * overlay.ActualWidth / 120, 40 * overlay.ActualHeight / 80);
                    Call(region, "Down", a); Call(region, "Up", b);
                }) && region.Result?.Bounds == new System.Drawing.Rectangle(-60, -10, 40, 30), "Unannotated selection no longer completes on release.");
            }
            finally { region.Close(); }
            var picker = new SelectionWindow(image, desktop, true, true, []);
            try
            {
                Require(InDialog(picker, () =>
                {
                    var overlay = Field<Canvas>(picker, "overlay");
                    Call(picker, "Down", new Point(3 * overlay.ActualWidth / 120, 4 * overlay.ActualHeight / 80));
                }) && picker.Result?.Bounds == new System.Drawing.Rectangle(-77, -16, 1, 1), "Color picking no longer captures a single physical pixel.");
                Require(Pixel(picker.Result!.Image, 0, 0).SequenceEqual(new byte[] { 6, 12, 220, 255 }), "The color picker captured the wrong pixel.");
            }
            finally { picker.Close(); }
            return Task.CompletedTask;
        });
        await Check("space_returns_to_capture_without_saving_until_mouse_gesture", () =>
        {
            var selector = new SelectionWindow(image, desktop, false, true, windows);
            try
            {
                Require(InDialog(selector, () =>
                {
                    var editor = Field<object>(selector, "editor");
                    Call(editor, "SetTool", "arrow");
                    Require(((UIElement)editor).IsHitTestVisible, "Arrow mode does not receive input.");
                    var source = PresentationSource.FromVisual(selector)!;
                    var key = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Space) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    selector.RaiseEvent(key);
                    Require(key.Handled && !((UIElement)editor).IsHitTestVisible && selector.Result == null, "Space failed to return to capture mode without completing a screenshot.");
                    var overlay = Field<Canvas>(selector, "overlay");
                    var point = new Point(25 * overlay.ActualWidth / 120, 15 * overlay.ActualHeight / 80);
                    Call(selector, "Move", point);
                    selector.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    Require(selector.Result == null, "Enter completed capture before the required mouse gesture.");
                    Call(selector, "Down", point); Call(selector, "Up", point);
                }) && selector.Result?.Bounds == new System.Drawing.Rectangle(-60, -10, 40, 30), "Capture did not finish on the mouse gesture after Space.");
            }
            finally { selector.Close(); }
            return Task.CompletedTask;
        });
        await Check("switching_tools_during_region_drag_releases_mouse_capture", () =>
        {
            var selector = new SelectionWindow(image, desktop, false, true, windows);
            try
            {
                Require(InDialog(selector, () =>
                {
                    var editor = Field<object>(selector, "editor");
                    var overlay = Field<Canvas>(selector, "overlay");
                    var point = new Point(25 * overlay.ActualWidth / 120, 15 * overlay.ActualHeight / 80);
                    Call(selector, "Down", point);
                    Require(overlay.CaptureMouse() && ReferenceEquals(Mouse.Captured, overlay), "Could not begin a captured selection gesture.");
                    Call(editor, "SetTool", "arrow");
                    Require(!ReferenceEquals(Mouse.Captured, overlay), "Switching from a selection gesture to a drawing tool left the pointer captured by the overlay.");
                    Call(selector, "Up", point);
                    Require(selector.Result == null, "Switching tools while selecting accidentally completed a screenshot.");
                    Call(editor, "SetTool", "select");
                    Call(selector, "Move", point); Call(selector, "Down", point); Call(selector, "Up", point);
                }) && selector.Result?.Bounds == new System.Drawing.Rectangle(-60, -10, 40, 30), "Capture was unusable after switching tools during a selection gesture.");
            }
            finally { selector.Close(); }
            return Task.CompletedTask;
        });
        await Check("space_refreshes_window_preview_at_stationary_pointer", () =>
        {
            System.Drawing.Rectangle[] halves = [new(-80, -20, 60, 80), new(-20, -20, 60, 80)];
            var selector = new SelectionWindow(image, desktop, false, true, halves);
            try
            {
                Require(InDialog(selector, () =>
                {
                    Field<Border>(selector, "toolbar").Visibility = Visibility.Collapsed;
                    selector.UpdateLayout();
                    var editor = Field<object>(selector, "editor");
                    var overlay = Field<Canvas>(selector, "overlay");
                    var pointer = Mouse.GetPosition(overlay);
                    var pointerOnLeft = pointer.X < overlay.ActualWidth / 2;
                    var expected = pointerOnLeft ? new System.Drawing.Rectangle(-80, -20, 60, 80) : new System.Drawing.Rectangle(-20, -20, 60, 80);
                    var previous = new Point((pointerOnLeft ? 90 : 30) * overlay.ActualWidth / 120, overlay.ActualHeight / 2);
                    Call(selector, "Move", previous);
                    Call(editor, "SetTool", "arrow");
                    var source = PresentationSource.FromVisual(selector)!;
                    selector.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Space) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    var preview = (CaptureResult)Call(selector, "CreateResult")!;
                    Require(preview.Bounds == expected, "Space restored a stale highlighted window instead of the window at the stationary pointer.");
                    Call(selector, "Down", pointer); Call(selector, "Up", pointer);
                    Require(selector.Result?.Bounds == preview.Bounds, "The captured window differs from the highlighted frame after leaving drawing mode.");
                }), "A stationary window click did not finish after switching out of drawing.");
            }
            finally { selector.Close(); }
            return Task.CompletedTask;
        });
        await Check("screenshot_saves_png_and_copies_without_opening_editor", async () =>
        {
            var originalClipboard = Clipboard.GetDataObject();
            var main = new MainWindow(context, true);
            var before = Application.Current.Windows.Cast<Window>().ToHashSet();
            typeof(MainWindow).GetProperty("SelectionForChecks", Private)!.SetValue(main,
                (Func<bool, Task<CaptureResult?>>)(_ => Task.FromResult<CaptureResult?>(new(image, new System.Drawing.Rectangle(-80, 20, 120, 80)))));
            try
            {
                await main.ScreenshotAsync("shot");
                Require(!Application.Current.Windows.Cast<Window>().Any(x => !before.Contains(x) && x.IsVisible), "Screenshot opened a post-capture editor.");
                var files = Directory.Exists(context.Settings.CaptureFolder) ? Directory.GetFiles(context.Settings.CaptureFolder, "*.png") : [];
                Require(files.Length == 1, "Screenshot did not automatically save exactly one PNG.");
                var saved = new BitmapImage(); saved.BeginInit(); saved.CacheOption = BitmapCacheOption.OnLoad; saved.UriSource = new Uri(files[0]); saved.EndInit();
                Require(EqualPixels(image, saved), "Saved PNG differs from the selected image.");
                Require(Clipboard.ContainsImage() && EqualPixels(image, Clipboard.GetImage()!), "Clipboard image differs from the saved screenshot.");
                var fileCount = files.Length;
                typeof(MainWindow).GetProperty("SelectionForChecks", Private)!.SetValue(main,
                    (Func<bool, Task<CaptureResult?>>)(_ => Task.FromResult<CaptureResult?>(null)));
                await main.ScreenshotAsync("shot");
                Require(Directory.GetFiles(context.Settings.CaptureFolder, "*.png").Length == fileCount && EqualPixels(image, Clipboard.GetImage()!), "Cancelling capture changed files or clipboard.");
            }
            finally
            {
                foreach (var window in Application.Current.Windows.Cast<Window>().Where(x => !before.Contains(x)).ToArray()) window.Close();
                main.Close();
                if (originalClipboard != null) Clipboard.SetDataObject(originalClipboard, true);
            }
        });
        await Check("uncommitted_redaction_cannot_be_captured", () =>
        {
            var selector = new SelectionWindow(image, desktop, false, true, windows);
            try
            {
                Layout(selector, 240, 160);
                Require(typeof(SelectionWindow).GetField("editor", Private)!.GetValue(selector) != null, "Annotation is unavailable before selection.");
                Call(selector, "Move", new Point(50, 30));
                var editor = Field<object>(selector, "editor");
                Call(editor, "SetTool", "blur"); Call(editor, "BeginShape", new Point(1, 2));
                try { Call(selector, "Complete"); }
                catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException && selector.Result != null) { }
                Require(selector.Result == null, "Enter exported a translucent redaction preview while the mouse gesture was unfinished.");
            }
            finally { selector.Close(); }
            return Task.CompletedTask;
        });
        await Check("toolbar_restores_window_preview_after_crossing_desktop_gap", () =>
        {
            var selector = new SelectionWindow(image, desktop, false, true, [windows[0]]);
            try
            {
                Layout(selector, 240, 160); Call(selector, "Move", new Point(50, 30)); Call(selector, "Move", new Point(230, 150));
                var toolbar = Field<Border>(selector, "toolbar");
                toolbar.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
                var result = (CaptureResult)Call(selector, "CreateResult")!;
                Require(result.Bounds == new System.Drawing.Rectangle(-60, -10, 40, 30), "Toolbar changed the intended window selection.");
            }
            finally { selector.Close(); }
            return Task.CompletedTask;
        });
        await Check("release_version_is_visible_in_updates", () =>
        {
            Require(context.Updates.CurrentVersionText == "1.2.0", "The updates UI does not display 1.2.0.");
            return Task.CompletedTask;
        });
        await Check("capture_toolbar_fits_and_renders_with_application_styles", () =>
        {
            var sample = new Canvas { Width = 1280, Height = 800, Background = new SolidColorBrush(Color.FromRgb(47, 63, 80)) };
            var document = new Border { Width = 900, Height = 500, Background = Brushes.White, CornerRadius = new CornerRadius(10) };
            var content = new StackPanel();
            content.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(232, 234, 238)), Padding = new Thickness(18, 14, 18, 14), Child = new TextBlock { Text = "Документ — пример окна", Foreground = Brushes.Black, FontSize = 16 } });
            content.Children.Add(new TextBlock { Text = "Разметка перед снимком", FontSize = 28, Foreground = Brushes.Black, Margin = new Thickness(32, 38, 32, 20) });
            content.Children.Add(new TextBlock { Text = "Сначала дорисуйте нужное инструментами внизу.\nЗатем «Снимок» — клик по окну или выделение области.", Foreground = Brushes.DimGray, FontSize = 18, Margin = new Thickness(32, 0, 32, 0) });
            document.Child = content; Canvas.SetLeft(document, 140); Canvas.SetTop(document, 110); sample.Children.Add(document);
            sample.Measure(new Size(1280, 800)); sample.Arrange(new Rect(0, 0, 1280, 800)); sample.UpdateLayout();
            var source = new RenderTargetBitmap(1280, 800, 96, 96, PixelFormats.Pbgra32); source.Render(sample); source.Freeze();
            var selector = new SelectionWindow(source, new(0, 0, 1280, 800), false, true, [new(140, 110, 900, 500)]);
            try
            {
                var xml = XDocument.Load("src/QuietIsland/App.xaml");
                XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                var resources = new XElement(presentation + "ResourceDictionary",
                    new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"),
                    xml.Root!.Element(presentation + "Application.Resources")!.Elements().Select(x => new XElement(x)));
                foreach (var item in resources.Descendants().Where(x => x.Name.NamespaceName == "clr-namespace:QuietIsland.Controls"))
                    item.Name = XName.Get(item.Name.LocalName, "clr-namespace:QuietIsland.Controls;assembly=QuietIsland");
                selector.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString());
                Layout(selector, 1280, 800); Call(selector, "Move", new Point(180, 125));
                SavePreview(selector, "window-hover.png", 1280, 800);
                Require(typeof(SelectionWindow).GetField("editor", Private)!.GetValue(selector) != null, "Drawing tools were deferred until after selection.");
                var editor = Field<object>(selector, "editor");
                Call(editor, "SetTool", "arrow"); Call(editor, "BeginShape", new Point(310, 360)); Call(editor, "FinishShape", new Point(600, 280));
                SavePreview(selector, "annotation-overlay.png", 1280, 800);
                var toolbar = Field<Border>(selector, "toolbar");
                Require(toolbar.ActualWidth <= 1248 && Canvas.GetTop(toolbar) >= 0 && Canvas.GetTop(toolbar) + toolbar.ActualHeight <= 800, "The annotation toolbar extends outside the display.");
                Call(editor, "SetTool", "select"); Call(selector, "Move", new Point(180, 125));
                SavePreview(selector, "annotated-window-hover.png", 1280, 800);
                var result = (CaptureResult)Call(selector, "CreateResult")!;
                CaptureService.SavePng(result.Image, Path.GetFullPath("artifacts/verification-1.2.0/annotated-result.png"));
                Require(result.Image.PixelWidth == 900 && result.Image.PixelHeight == 500, "Final capture includes overlay controls or has the wrong pixel dimensions.");
            }
            finally { selector.Close(); }
            return Task.CompletedTask;
        });
    }

    private static bool InDialog(SelectionWindow selector, Action gesture)
    {
        Exception? failure = null;
        selector.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try { gesture(); }
            catch (Exception error) { failure = error; selector.Close(); }
        }));
        var deadline = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        deadline.Tick += (_, _) => { deadline.Stop(); selector.Close(); };
        deadline.Start();
        try
        {
            var completed = selector.ShowDialog() == true;
            if (failure != null) throw failure;
            return completed;
        }
        finally { deadline.Stop(); }
    }

    private static void Layout(SelectionWindow selector, double width, double height)
    {
        var root = (FrameworkElement)selector.Content;
        root.Width = width; root.Height = height;
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        Call(selector, "UpdateFrame"); Call(selector, "PlaceChrome");
    }
    private static void SavePreview(SelectionWindow selector, string name, int width, int height)
    {
        Layout(selector, width, height);
        var preview = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); preview.Render((FrameworkElement)selector.Content);
        CaptureService.SavePng(preview, Path.GetFullPath("artifacts/verification-1.2.0/" + name));
    }
    private static object? Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private)!.Invoke(target, args);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static byte[] Pixel(BitmapSource source, int x, int y)
    {
        var bytes = new byte[4]; new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0).CopyPixels(new Int32Rect(x, y, 1, 1), bytes, 4, 0); return bytes;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var item in Descendants(child)) yield return item;
        }
    }

    internal static BitmapSource Fixture()
    {
        var pixels = new byte[120 * 80 * 4];
        for (var y = 0; y < 80; y++) for (var x = 0; x < 120; x++)
        {
            var offset = (y * 120 + x) * 4;
            pixels[offset] = (byte)(x * 2); pixels[offset + 1] = (byte)(y * 3); pixels[offset + 2] = 220; pixels[offset + 3] = 255;
        }
        var result = BitmapSource.Create(120, 80, 96, 96, PixelFormats.Bgra32, null, pixels, 120 * 4);
        result.Freeze(); return result;
    }

    internal static bool EqualPixels(BitmapSource a, BitmapSource b)
    {
        if (a.PixelWidth != b.PixelWidth || a.PixelHeight != b.PixelHeight) return false;
        byte[] Pixels(BitmapSource source)
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var bytes = new byte[source.PixelWidth * source.PixelHeight * 4]; converted.CopyPixels(bytes, source.PixelWidth * 4, 0); return bytes;
        }
        return Pixels(a).SequenceEqual(Pixels(b));
    }

    internal static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
