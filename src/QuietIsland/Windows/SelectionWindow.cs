using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using QuietIsland.Controls;
using QuietIsland.Services;
using ScreenRectangle = System.Drawing.Rectangle;

namespace QuietIsland.Windows;

public sealed class SelectionWindow : Window
{
    private readonly BitmapSource image;
    private readonly ScreenRectangle screen;
    private readonly IReadOnlyList<ScreenRectangle> windows;
    private readonly IReadOnlyList<ScreenRectangle> snapAreas;
    private readonly bool color, annotate;
    private readonly Canvas overlay = new() { Background = Brushes.Transparent, ClipToBounds = true };
    private readonly System.Windows.Shapes.Path dim = new() { Fill = new SolidColorBrush(Color.FromArgb(95, 0, 0, 0)), IsHitTestVisible = false };
    private readonly Rectangle selection = new() { Stroke = new SolidColorBrush(Color.FromRgb(168, 230, 208)), StrokeThickness = 2, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Border loupe = new() { Background = Brushes.Black, Padding = new Thickness(8), CornerRadius = new CornerRadius(8), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    private readonly Image zoom = new() { Width = 108, Height = 108 };
    private readonly TextBlock colorText = Ui.Text("", 12);
    private readonly TextBlock hint;
    private readonly Border instruction, toolbar;
    private readonly AnnotationEditor? editor;
    private readonly Viewbox? editorHost;
    private Point? start;
    private Int32Rect region, pressedWindow, lastWindow;
    private bool dragged;
    private CaptureEscape? escape;
    private bool IsSelecting => editor?.IsSelecting != false;
    public CaptureResult? Result { get; private set; }

    public SelectionWindow(BitmapSource source, ScreenRectangle bounds, bool pickColor, bool annotate = false, IReadOnlyList<ScreenRectangle>? windows = null, IReadOnlyList<ScreenRectangle>? snapAreas = null)
    {
        image = source; screen = bounds; color = pickColor; this.annotate = annotate && !pickColor;
        if (windows == null && !pickColor)
        {
            var targets = CaptureWindows.SnapshotTargets();
            this.windows = targets.Select(x => x.Bounds).ToArray();
            this.snapAreas = targets.Select(x => x.ActivationBounds).ToArray();
        }
        else { this.windows = windows ?? []; this.snapAreas = snapAreas ?? this.windows; }
        if (this.snapAreas.Count != this.windows.Count) throw new ArgumentException("Each window needs a corresponding activation area.", nameof(snapAreas));
        Title = pickColor ? "Выбрать цвет" : "Quiet Island · Снимок экрана";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false; Cursor = Cursors.Cross;
        Left = SystemParameters.VirtualScreenLeft; Top = SystemParameters.VirtualScreenTop; Width = SystemParameters.VirtualScreenWidth; Height = SystemParameters.VirtualScreenHeight;
        var root = new Grid();
        root.Children.Add(new Image { Source = image, Stretch = Stretch.Fill, IsHitTestVisible = false });
        root.Children.Add(overlay); overlay.Children.Add(dim); overlay.Children.Add(selection);
        Canvas.SetZIndex(dim, 1);
        Canvas.SetZIndex(selection, 3);
        hint = Ui.Text(pickColor ? "Нажмите на цвет · Esc — отмена"
            : this.annotate ? "Клик по окну или выделение области — снимок · Инструменты — дорисовки до захвата · Esc — отмена"
            : "Клик — выбрать окно · Потяните — область · Esc — отмена", 13);
        hint.TextWrapping = TextWrapping.Wrap;
        instruction = Ui.Card(hint, 12); instruction.IsHitTestVisible = false;
        overlay.Children.Add(instruction); Canvas.SetZIndex(instruction, 10);
        toolbar = Ui.Card(Ui.Row(Ui.Button("Отмена", Cancel)), 10);
        if (this.annotate)
        {
            editor = new AnnotationEditor(image);
            editor.ToolChanged += OnToolChanged; editor.CancelRequested += Cancel;
            editorHost = new Viewbox { Child = editor, Stretch = Stretch.Fill };
            overlay.Children.Add(editorHost); Canvas.SetZIndex(editorHost, 0);
            toolbar.Child = editor.Toolbar;
        }
        toolbar.Visibility = this.annotate ? Visibility.Visible : Visibility.Collapsed;
        overlay.Children.Add(toolbar); Canvas.SetZIndex(toolbar, 10);
        toolbar.MouseEnter += (_, _) =>
        {
            if (!IsSelecting || start != null || lastWindow.Width == 0) return;
            region = lastWindow; UpdateFrame();
        };
        toolbar.MouseDown += (_, e) => e.Handled = true;
        toolbar.MouseUp += (_, e) => e.Handled = true;
        loupe.Child = Ui.Stack(zoom, colorText); overlay.Children.Add(loupe); Canvas.SetZIndex(loupe, 11);
        RenderOptions.SetBitmapScalingMode(zoom, BitmapScalingMode.NearestNeighbor); Content = root;
        SourceInitialized += (_, _) => Native.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(this).Handle, new IntPtr(-1), bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0040);
        Loaded += (_, _) => { InputManager.Current.PreProcessInput += SelectionInput; try { escape = new CaptureEscape(() => Dispatcher.BeginInvoke(Cancel, System.Windows.Threading.DispatcherPriority.Input)); } catch (System.ComponentModel.Win32Exception e) { Log.Write("Capture Escape", e); } Activate(); Focus(); Keyboard.Focus(this); PlaceChrome(); Move(Mouse.GetPosition(overlay)); };
        SizeChanged += (_, _) => { UpdateFrame(); PlaceChrome(); };
        PreviewKeyDown += OnKeyDown;
        MouseRightButtonDown += (_, e) => { Cancel(); e.Handled = true; };
        overlay.MouseLeftButtonDown += (_, e) =>
        {
            if (!IsSelecting) return;
            Down(e.GetPosition(overlay));
            if (start != null) overlay.CaptureMouse();
        };
        overlay.MouseMove += (_, e) => { if (!toolbar.IsMouseOver && IsSelecting) Move(e.GetPosition(overlay)); };
        overlay.MouseLeftButtonUp += (_, e) => { if (IsSelecting) Up(e.GetPosition(overlay)); };
        overlay.LostMouseCapture += (_, _) => { if (!overlay.IsMouseCaptured) start = null; };
        Deactivated += (_, _) => editor?.CancelGesture();
        Closed += (_, _) => { escape?.Dispose(); InputManager.Current.PreProcessInput -= SelectionInput; editor?.CancelGesture(); if (overlay.IsMouseCaptured) overlay.ReleaseMouseCapture(); };
        UpdateFrame();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { editor?.CancelGesture(); Cancel(); e.Handled = true; return; }
        if (editor?.IsOptionsOpen == true) return;
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && editor != null)
        { editor.Undo(); e.Handled = true; }
        else if (e.Key == Key.Space && annotate && start == null)
        { editor?.SetTool("select"); e.Handled = true; }
    }

    private void SelectionInput(object sender, PreProcessInputEventArgs e)
    {
        // Tool popups have their own HWND and can consume Escape before it
        // reaches the selection window's routed PreviewKeyDown handler.
        if (IsVisible && e.StagingItem.Input is KeyEventArgs { Key: Key.Escape } key && key.RoutedEvent == Keyboard.PreviewKeyDownEvent)
        { key.Handled = true; e.Cancel(); Dispatcher.BeginInvoke(Cancel, System.Windows.Threading.DispatcherPriority.Input); }
    }

    private (int X, int Y) Pixel(Point point, bool edge = false)
    {
        int Coordinate(double value, int length)
        {
            var pixel = Math.Clamp((int)Math.Floor(value), 0, length - 1);
            // The physical pointer stops at Right-1 / Bottom-1. Snap that outer
            // pixel to the image boundary when it is used as a selection edge.
            return edge && pixel == length - 1 ? length : pixel;
        }
        return (Coordinate(point.X / Math.Max(1, overlay.ActualWidth) * image.PixelWidth, image.PixelWidth),
            Coordinate(point.Y / Math.Max(1, overlay.ActualHeight) * image.PixelHeight, image.PixelHeight));
    }

    private Int32Rect DragBounds(Point a, Point b)
    {
        var first = Pixel(a, true); var last = Pixel(b, true);
        return new Int32Rect(Math.Min(first.X, last.X), Math.Min(first.Y, last.Y), Math.Abs(last.X - first.X), Math.Abs(last.Y - first.Y));
    }

    private Int32Rect WindowAt(Point point)
    {
        var pixel = Pixel(point);
        var desktop = new System.Drawing.Point(screen.Left + (int)(pixel.X * screen.Width / (double)image.PixelWidth), screen.Top + (int)(pixel.Y * screen.Height / (double)image.PixelHeight));
        for (var index = 0; index < windows.Count; index++)
        {
            var window = windows[index];
            if (!window.Contains(desktop)) continue;
            // A maximized client area blocks obscured windows without snapping itself.
            if (!snapAreas[index].Contains(desktop)) return default;
            var clipped = ScreenRectangle.Intersect(window, screen);
            if (clipped.Width < 2 || clipped.Height < 2) return default;
            var left = (int)Math.Floor((clipped.Left - screen.Left) * image.PixelWidth / (double)screen.Width);
            var top = (int)Math.Floor((clipped.Top - screen.Top) * image.PixelHeight / (double)screen.Height);
            var right = (int)Math.Ceiling((clipped.Right - screen.Left) * image.PixelWidth / (double)screen.Width);
            var bottom = (int)Math.Ceiling((clipped.Bottom - screen.Top) * image.PixelHeight / (double)screen.Height);
            return new Int32Rect(left, top, right - left, bottom - top);
        }
        return default;
    }

    private void Down(Point point)
    {
        if (!IsSelecting) return;
        if (color) { var pixel = Pixel(point); region = new Int32Rect(pixel.X, pixel.Y, 1, 1); Complete(); return; }
        start = point; pressedWindow = WindowAt(point); dragged = false;
    }

    private static bool IsDrag(Point a, Point b) => Math.Abs(a.X - b.X) >= SystemParameters.MinimumHorizontalDragDistance
        || Math.Abs(a.Y - b.Y) >= SystemParameters.MinimumVerticalDragDistance;

    private void Move(Point point)
    {
        if (color) { MoveLoupe(point); return; }
        if (!IsSelecting) return;
        if (start is { } a)
        {
            dragged |= IsDrag(a, point);
            if (dragged) region = DragBounds(a, point);
        }
        else
        {
            region = WindowAt(point);
            if (region.Width > 0) lastWindow = region;
        }
        UpdateFrame();
    }

    private void Up(Point point)
    {
        if (start is not { } a || color || !IsSelecting) return;
        dragged |= IsDrag(a, point);
        start = null;
        if (overlay.IsMouseCaptured) overlay.ReleaseMouseCapture();
        region = dragged ? DragBounds(a, point) : pressedWindow;
        UpdateFrame();
        if (region.Width < 2 || region.Height < 2) return;
        Complete();
    }

    private void OnToolChanged()
    {
        start = null;
        if (overlay.IsMouseCaptured) overlay.ReleaseMouseCapture();
        if (IsSelecting)
        {
            if (toolbar.IsMouseOver) region = lastWindow;
            else Move(Mouse.GetPosition(overlay));
        }
        else region = default;
        hint.Text = IsSelecting
            ? "Клик по окну или выделение области — снимок · Инструменты — дорисовки до захвата · Esc — отмена"
            : "Дорисуйте нужное · «Снимок» или Пробел — захват окна / области · Ctrl+Z — отменить действие · Esc — отмена";
        UpdateFrame(); PlaceChrome();
    }

    private void UpdateFrame()
    {
        var width = overlay.ActualWidth; var height = overlay.ActualHeight;
        if (width <= 0 || height <= 0) return;
        var selected = region.Width > 0 && region.Height > 0;
        var rect = new Rect(region.X * width / image.PixelWidth, region.Y * height / image.PixelHeight, region.Width * width / image.PixelWidth, region.Height * height / image.PixelHeight);
        var mask = new GeometryGroup { FillRule = FillRule.EvenOdd };
        mask.Children.Add(new RectangleGeometry(new Rect(0, 0, width, height)));
        if (selected && !color) mask.Children.Add(new RectangleGeometry(rect));
        dim.Data = mask;
        dim.Visibility = IsSelecting ? Visibility.Visible : Visibility.Collapsed;
        selection.Visibility = selected && !color ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(selection, rect.X); Canvas.SetTop(selection, rect.Y); selection.Width = rect.Width; selection.Height = rect.Height;
        if (editorHost != null)
        {
            Canvas.SetLeft(editorHost, 0); Canvas.SetTop(editorHost, 0); editorHost.Width = width; editorHost.Height = height;
        }
    }

    private void PlaceChrome()
    {
        if (overlay.ActualWidth <= 0 || overlay.ActualHeight <= 0) return;
        var desktopPoint = Native.GetCursorPos(out var pointer) ? new System.Drawing.Point(pointer.X, pointer.Y) : screen.Location;
        var monitor = ScreenRectangle.Intersect(System.Windows.Forms.Screen.FromPoint(desktopPoint).Bounds, screen);
        if (monitor.IsEmpty) monitor = screen;
        var sx = overlay.ActualWidth / screen.Width; var sy = overlay.ActualHeight / screen.Height;
        var left = (monitor.Left - screen.Left) * sx; var top = (monitor.Top - screen.Top) * sy;
        var width = monitor.Width * sx; var height = monitor.Height * sy;
        toolbar.MaxWidth = Math.Max(1, Math.Min(760, width - 32)); instruction.MaxWidth = Math.Max(1, width - 32);
        toolbar.Measure(new Size(toolbar.MaxWidth, double.PositiveInfinity)); instruction.Measure(new Size(instruction.MaxWidth, double.PositiveInfinity));
        Canvas.SetLeft(instruction, left + Math.Max(0, (width - instruction.DesiredSize.Width) / 2)); Canvas.SetTop(instruction, top + 20);
        Canvas.SetLeft(toolbar, left + Math.Max(0, (width - toolbar.DesiredSize.Width) / 2)); Canvas.SetTop(toolbar, top + Math.Max(0, height - toolbar.DesiredSize.Height - 24));
    }

    private void MoveLoupe(Point point)
    {
        var (x, y) = Pixel(point);
        zoom.Source = new CroppedBitmap(image, new Int32Rect(Math.Clamp(x - 4, 0, Math.Max(0, image.PixelWidth - 9)), Math.Clamp(y - 4, 0, Math.Max(0, image.PixelHeight - 9)), Math.Min(9, image.PixelWidth), Math.Min(9, image.PixelHeight)));
        var bgra = new FormatConvertedBitmap(new CroppedBitmap(image, new Int32Rect(x, y, 1, 1)), PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4]; bgra.CopyPixels(pixel, 4, 0); colorText.Text = $"#{pixel[2]:X2}{pixel[1]:X2}{pixel[0]:X2}";
        loupe.Visibility = Visibility.Visible;
        Canvas.SetLeft(loupe, Math.Clamp(point.X + 24, 0, Math.Max(0, overlay.ActualWidth - 140))); Canvas.SetTop(loupe, Math.Clamp(point.Y + 20, 0, Math.Max(0, overlay.ActualHeight - 160)));
    }

    private CaptureResult CreateResult()
    {
        BitmapSource cropped = editor?.ExportRegion(region) ?? new CroppedBitmap(image, region); cropped.Freeze();
        var x = screen.Left + (int)Math.Round(region.X * screen.Width / (double)image.PixelWidth);
        var y = screen.Top + (int)Math.Round(region.Y * screen.Height / (double)image.PixelHeight);
        return new(cropped, new ScreenRectangle(x, y, cropped.PixelWidth, cropped.PixelHeight));
    }

    private void Complete()
    {
        if (region.Width < (color ? 1 : 2) || region.Height < (color ? 1 : 2) || start != null || !IsSelecting || editor?.HasActiveGesture == true) return;
        Result = CreateResult(); DialogResult = true;
    }
    private bool cancelQueued;
    internal void CancelSelection() => Cancel();
    private void Cancel()
    {
        if (cancelQueued || !IsVisible) return;
        editor?.CancelGesture(); Result = null;
        var dialogs = OwnedWindows.Cast<Window>().Where(x => x.IsVisible).ToArray();
        if (dialogs.Length > 0)
        {
            // Unwind the inner ShowDialog before closing its modal owner.
            cancelQueued = true;
            foreach (var dialog in dialogs) dialog.Close();
            Dispatcher.BeginInvoke(() => { cancelQueued = false; Cancel(); }, System.Windows.Threading.DispatcherPriority.Background);
            return;
        }
        DialogResult = false;
    }
}
