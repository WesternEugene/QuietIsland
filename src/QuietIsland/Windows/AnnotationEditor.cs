using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using QuietIsland.Controls;
using VectorPath = System.Windows.Shapes.Path;

namespace QuietIsland.Windows;

internal sealed class AnnotationEditor : ContentControl
{
    private readonly BitmapSource source;
    private readonly Grid drawing;
    private readonly Canvas shapes = new() { IsHitTestVisible = false };
    private readonly InkCanvas ink = new();
    private readonly Stack<Action> undo = new();
    private readonly Dictionary<string, Button> tools = new();
    private readonly Dictionary<Color, Button> colors = new();
    private readonly WrapPanel palette = new();
    private readonly ComboBox widths = new() { Width = 82, Margin = new Thickness(6, 0, 16, 0), ToolTip = "Толщина линии в пикселях снимка" };
    private readonly Button undoButton = new() { ToolTip = "Отменить последнее действие (Ctrl+Z)" };
    private readonly TextBlock hint = new() { FontSize = 12, Margin = new Thickness(0, 2, 0, 10), TextWrapping = TextWrapping.Wrap };
    private Color color = Colors.OrangeRed;
    private double thickness = 4;
    private string mode = "select";
    private Point? start;
    private Shape? temporary;

    internal UIElement Toolbar { get; }
    internal bool HasActiveGesture => temporary != null || ink.IsMouseCaptureWithin || ink.IsStylusCaptureWithin;
    internal bool IsOptionsOpen => widths.IsDropDownOpen;
    internal bool IsSelecting => mode == "select";
    internal event Action? ToolChanged;
    internal event Action? CancelRequested;

    internal AnnotationEditor(BitmapSource image)
    {
        source = image;
        Width = image.PixelWidth;
        Height = image.PixelHeight;
        ClipToBounds = true;

        drawing = new Grid { Width = image.PixelWidth, Height = image.PixelHeight, ClipToBounds = true, Background = Brushes.Transparent, Focusable = true };
        drawing.Children.Add(new Image { Source = image, Stretch = Stretch.Fill, IsHitTestVisible = false });
        drawing.Children.Add(shapes);
        drawing.Children.Add(ink);
        ink.Background = Brushes.Transparent;
        ink.DefaultDrawingAttributes = new DrawingAttributes { Color = color, Width = thickness, Height = thickness, FitToCurve = true };
        ink.StrokeCollected += (_, e) => Remember(() => ink.Strokes.Remove(e.Stroke));

        Toolbar = BuildToolbar();
        Content = drawing;

        CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, (_, e) => { Undo(); e.Handled = true; }, (_, e) => { e.CanExecute = undo.Count > 0 || temporary != null; e.Handled = true; }));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Undo, new KeyGesture(Key.Z, ModifierKeys.Control)));
        PreviewKeyDown += (_, e) =>
        {
            // Preview also reaches us when focus is inside InkCanvas or a toolbar control.
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Undo();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && temporary != null)
            {
                CancelGesture();
                e.Handled = true;
            }
        };
        drawing.PreviewMouseLeftButtonDown += Down;
        drawing.PreviewMouseMove += Move;
        drawing.PreviewMouseLeftButtonUp += Up;
        drawing.LostMouseCapture += (_, _) => { if (temporary != null && !drawing.IsMouseCaptured) CancelGesture(); };
        Unloaded += (_, _) => CancelGesture();
        SetTool("select");
    }

    private UIElement BuildToolbar()
    {
        var row = Ui.Row();
        row.Margin = new Thickness(0, 0, 0, 2);
        var capture = Ui.Button("Снимок", () => SetTool("select"), tip: "Режим захвата: клик по окну или выделение области (Пробел)");
        AutomationProperties.SetName(capture, "Режим: Снимок");
        tools.Add("select", capture);
        row.Children.Add(capture);
        foreach (var (key, label) in new[] { ("pen", "Перо"), ("arrow", "Стрелка"), ("box", "Рамка"), ("text", "Текст"), ("blur", "Скрыть область") })
        {
            var button = Ui.Button(label, () => SetTool(key));
            button.Padding = new Thickness(10, 8, 10, 8);
            AutomationProperties.SetName(button, "Инструмент: " + label);
            tools.Add(key, button);
            row.Children.Add(button);
        }
        undoButton.Content = ActionLabel("undo", "Отменить");
        undoButton.Click += (_, _) => Undo();
        AutomationProperties.SetName(undoButton, "Отменить последнее действие (Ctrl+Z)");
        row.Children.Add(undoButton);
        row.Children.Add(Ui.Button("Отмена", () => CancelRequested?.Invoke(), tip: "Отменить снимок (Esc)"));

        var options = Ui.Row();
        options.Margin = new Thickness(0, 0, 0, 6);
        var colorLabel = Ui.Text("Цвет", 12, "Muted");
        colorLabel.VerticalAlignment = VerticalAlignment.Center;
        colorLabel.Margin = new Thickness(0, 0, 8, 0);
        options.Children.Add(colorLabel);
        foreach (var (value, label) in new[] { (Colors.OrangeRed, "Оранжевый"), (Colors.Gold, "Жёлтый"), (Colors.DeepSkyBlue, "Голубой"), (Colors.White, "Белый"), (Colors.Black, "Чёрный") })
        {
            var swatch = Ui.Button("", () => { color = value; UpdateOptions(); }, tip: label);
            swatch.Width = swatch.Height = 30;
            swatch.Padding = new Thickness(6);
            swatch.Margin = new Thickness(0, 0, 5, 0);
            swatch.Content = new Ellipse { Width = 15, Height = 15, Fill = new SolidColorBrush(value), Stroke = Brushes.Gray, StrokeThickness = .7 };
            AutomationProperties.SetName(swatch, "Цвет: " + label);
            colors.Add(value, swatch);
            palette.Children.Add(swatch);
        }
        palette.Margin = new Thickness(0, 0, 16, 0);
        options.Children.Add(palette);
        var widthLabel = Ui.Text("Толщина", 12, "Muted");
        widthLabel.VerticalAlignment = VerticalAlignment.Center;
        options.Children.Add(widthLabel);
        foreach (var value in new[] { 2d, 4d, 8d }) widths.Items.Add(new ComboBoxItem { Content = $"{value:0} px", Tag = value });
        widths.SelectedIndex = 1;
        widths.SelectionChanged += (_, _) =>
        {
            if (widths.SelectedItem is ComboBoxItem { Tag: double value }) { thickness = value; UpdateOptions(); }
        };
        AutomationProperties.SetName(widths, "Толщина линии");
        options.Children.Add(widths);
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return Ui.Stack(row, options, hint);
    }

    private static UIElement ActionLabel(string icon, string text, string foreground = "Text")
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(Icons.Make(icon, 16, foreground));
        var label = new TextBlock { Text = text, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(label);
        return row;
    }

    internal void SetTool(string tool)
    {
        CancelGesture();
        mode = tool;
        IsHitTestVisible = tool != "select";
        ink.EditingMode = tool == "pen" ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.None;
        ink.IsHitTestVisible = tool == "pen";
        drawing.Cursor = tool == "text" ? Cursors.IBeam : Cursors.Cross;
        foreach (var (key, button) in tools)
        {
            var selected = key == tool;
            button.Background = selected ? key == "select" ? Ui.Brush("Accent") : Icons.Soft("shot") : Ui.Brush("Card");
            button.Foreground = selected && key == "select" ? Ui.Brush("AccentText") : Ui.Brush("Text");
            button.BorderBrush = selected ? Ui.Brush("Accent") : Brushes.Transparent;
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            AutomationProperties.SetItemStatus(button, selected ? "Выбрано" : "");
            if (key == "select") button.Content = ActionLabel("shot", "Снимок", selected ? "AccentText" : "Text");
        }
        hint.Text = tool switch
        {
            "select" => "Клик по окну — снимок окна. Выделите область и отпустите кнопку — снимок области. Для дорисовок сначала выберите инструмент.",
            "arrow" => "Стрелка: зажмите кнопку мыши и ведите от начала к острию.",
            "box" => "Рамка: зажмите кнопку мыши и выделите нужную область.",
            "text" => "Текст: щёлкните по снимку, затем введите подпись.",
            "blur" => "Скрыть область: протяните рамку. Область будет непрозрачно закрашена чёрным.",
            _ => "Перо: рисуйте по снимку с зажатой кнопкой мыши. Цвет и толщину можно выбрать в панели инструментов."
        };
        UpdateOptions();
        ToolChanged?.Invoke();
    }

    private void UpdateOptions()
    {
        ink.DefaultDrawingAttributes.Color = color;
        ink.DefaultDrawingAttributes.Width = ink.DefaultDrawingAttributes.Height = thickness;
        palette.IsEnabled = mode != "blur";
        palette.Opacity = palette.IsEnabled ? 1 : .4;
        widths.IsEnabled = mode is "pen" or "arrow" or "box";
        foreach (var (value, button) in colors)
        {
            var selected = value == color;
            button.BorderBrush = selected ? Ui.Brush("Accent") : Ui.Brush("Line");
            button.Background = selected ? Ui.Brush("Hover") : Brushes.Transparent;
            AutomationProperties.SetItemStatus(button, selected ? "Выбрано" : "");
        }
    }

    private void Down(object sender, MouseButtonEventArgs e)
    {
        if (mode is "pen" or "select") return;
        e.Handled = true;
        drawing.Focus();
        var point = AnnotationGeometry.Clamp(e.GetPosition(drawing), drawing.RenderSize);
        if (mode == "text") { AddText(point); return; }
        BeginShape(point);
        if (!drawing.CaptureMouse()) CancelGesture();
    }

    private void BeginShape(Point point)
    {
        CancelGesture();
        start = AnnotationGeometry.Clamp(point, new Size(source.PixelWidth, source.PixelHeight));
        temporary = mode == "arrow"
            ? new VectorPath { Fill = new SolidColorBrush(color), Stretch = Stretch.None }
            : new Rectangle
            {
                Stroke = mode == "blur" ? Brushes.White : new SolidColorBrush(color),
                StrokeThickness = mode == "blur" ? 1.5 : thickness,
                Fill = mode == "blur" ? new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)) : Brushes.Transparent,
                StrokeDashArray = mode == "blur" ? new DoubleCollection { 4, 3 } : null
            };
        shapes.Children.Add(temporary);
        UpdateShape(start.Value);
        UpdateUndo();
    }

    private void Move(object sender, MouseEventArgs e)
    {
        if (temporary == null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { CancelGesture(); return; }
        UpdateShape(e.GetPosition(drawing));
        e.Handled = true;
    }

    private void UpdateShape(Point point)
    {
        if (start is not { } a || temporary == null) return;
        var b = AnnotationGeometry.Clamp(point, new Size(source.PixelWidth, source.PixelHeight));
        if (temporary is VectorPath arrow) arrow.Data = AnnotationGeometry.Arrow(a, b, thickness);
        else
        {
            var bounds = new Rect(a, b);
            Canvas.SetLeft(temporary, bounds.Left);
            Canvas.SetTop(temporary, bounds.Top);
            temporary.Width = bounds.Width;
            temporary.Height = bounds.Height;
        }
    }

    private void Up(object sender, MouseButtonEventArgs e)
    {
        if (temporary == null) return;
        e.Handled = true;
        FinishShape(e.GetPosition(drawing));
    }

    private void FinishShape(Point point)
    {
        if (start is not { } a || temporary == null) return;
        var b = AnnotationGeometry.Clamp(point, new Size(source.PixelWidth, source.PixelHeight));
        // Mouse-up can carry a newer position than the last move, including a very quick drag.
        UpdateShape(b);
        var element = temporary;
        temporary = null;
        start = null;
        if (drawing.IsMouseCaptured) drawing.ReleaseMouseCapture();
        var bounds = new Rect(a, b);
        if (mode == "arrow" ? (b - a).Length < 3 : bounds.Width < 2 || bounds.Height < 2)
        {
            shapes.Children.Remove(element);
            UpdateUndo();
            return;
        }
        if (mode == "blur")
        {
            // Cover every intersected pixel; the exported rectangle is fully opaque.
            var pixels = AnnotationGeometry.RedactionBounds(a, b, source.PixelWidth, source.PixelHeight);
            shapes.Children.Remove(element);
            element = new Rectangle { Width = pixels.Width, Height = pixels.Height, Fill = Brushes.Black };
            Canvas.SetLeft(element, pixels.X);
            Canvas.SetTop(element, pixels.Y);
            shapes.Children.Add(element);
        }
        var committed = element;
        Remember(() => shapes.Children.Remove(committed));
    }

    private void AddText(Point point)
    {
        var text = Dialogs.Ask("Подпись на снимке", "Введите текст");
        if (string.IsNullOrWhiteSpace(text)) return;
        var label = new TextBlock
        {
            Text = text, FontSize = 28, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(color), Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
            Padding = new Thickness(4), MaxWidth = source.PixelWidth, TextWrapping = TextWrapping.Wrap
        };
        label.Measure(new Size(source.PixelWidth, double.PositiveInfinity));
        Canvas.SetLeft(label, Math.Clamp(point.X, 0, Math.Max(0, source.PixelWidth - label.DesiredSize.Width)));
        Canvas.SetTop(label, Math.Clamp(point.Y, 0, Math.Max(0, source.PixelHeight - label.DesiredSize.Height)));
        shapes.Children.Add(label);
        Remember(() => shapes.Children.Remove(label));
    }

    private void Remember(Action action) { undo.Push(action); UpdateUndo(); }

    internal void Undo()
    {
        if (temporary != null) CancelGesture();
        else if (undo.TryPop(out var action)) { action(); UpdateUndo(); }
    }

    internal bool CancelGesture()
    {
        var element = temporary;
        temporary = null;
        start = null;
        if (element != null) shapes.Children.Remove(element);
        if (drawing.IsMouseCaptured) drawing.ReleaseMouseCapture();
        UpdateUndo();
        return element != null;
    }

    private void UpdateUndo()
    {
        undoButton.IsEnabled = undo.Count > 0 || temporary != null;
        CommandManager.InvalidateRequerySuggested();
    }

    internal BitmapSource Export() => ExportRegion(new Int32Rect(0, 0, source.PixelWidth, source.PixelHeight));

    internal BitmapSource ExportRegion(Int32Rect region)
    {
        drawing.Measure(new Size(source.PixelWidth, source.PixelHeight));
        drawing.Arrange(new Rect(0, 0, source.PixelWidth, source.PixelHeight));
        drawing.UpdateLayout();
        var visual = new DrawingVisual();
        var output = new Rect(0, 0, region.Width, region.Height);
        var brush = new VisualBrush(drawing)
        {
            Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(region.X, region.Y, region.Width, region.Height)
        };
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(brush, null, output);
        var bitmap = new RenderTargetBitmap(region.Width, region.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
