using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using QuietIsland.Controls;
using QuietIsland.Services;
using VectorPath = System.Windows.Shapes.Path;

namespace QuietIsland.Windows;

public sealed class AnnotationWindow : Window
{
    private readonly BitmapSource source;
    private readonly IslandContext c;
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
    private string mode = "pen";
    private Point? start;
    private Shape? temporary;

    public AnnotationWindow(BitmapSource image, IslandContext context)
    {
        source = image;
        c = context;
        Title = "Quiet Island · Разметка снимка";
        Width = Math.Min(SystemParameters.WorkArea.Width - 80, 1100);
        Height = Math.Min(SystemParameters.WorkArea.Height - 80, 780);
        MinWidth = Math.Min(620, Width);
        MinHeight = Math.Min(420, Height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Ui.Brush("Surface");
        Topmost = true;

        drawing = new Grid { Width = image.PixelWidth, Height = image.PixelHeight, ClipToBounds = true, Background = Brushes.Transparent, Focusable = true };
        drawing.Children.Add(new Image { Source = image, Stretch = Stretch.Fill, IsHitTestVisible = false });
        drawing.Children.Add(shapes);
        drawing.Children.Add(ink);
        ink.Background = Brushes.Transparent;
        ink.DefaultDrawingAttributes = new DrawingAttributes { Color = color, Width = thickness, Height = thickness, FitToCurve = true };
        ink.StrokeCollected += (_, e) => Remember(() => ink.Strokes.Remove(e.Stroke));

        var root = new DockPanel { Margin = new Thickness(14) };
        var toolbar = BuildToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        var footer = Ui.Text($"{image.PixelWidth} × {image.PixelHeight} px    ·    Ctrl+Z — отменить    ·    Esc — отменить текущую фигуру", 11, "Muted");
        footer.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(new Border
        {
            Background = Ui.Brush("Card"),
            BorderBrush = Ui.Brush("Line"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
            Child = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = drawing }
        });
        Content = root;

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
        Deactivated += (_, _) => CancelGesture();
        Closed += (_, _) => CancelGesture();
        SetTool("pen");
    }

    private UIElement BuildToolbar()
    {
        var row = Ui.Row();
        row.Margin = new Thickness(0, 0, 0, 2);
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
        var copy = Ui.Button("Копировать", () =>
        {
            System.Windows.Clipboard.SetImage(Export());
            c.Notify("Снимок скопирован");
            Close();
        }, true, "Копировать размеченный снимок в буфер обмена");
        copy.Content = ActionLabel("copy", "Копировать", "AccentText");
        row.Children.Add(copy);
        row.Children.Add(Ui.Button("Сохранить", () =>
        {
            var image = Export();
            var path = c.Capture.NewPath(".png");
            CaptureService.SavePng(image, path);
            System.Windows.Clipboard.SetImage(image);
            c.Notify("Снимок сохранён: " + System.IO.Path.GetFileName(path));
            Close();
        }, tip: "Сохранить PNG и скопировать снимок в буфер обмена"));

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

    private void SetTool(string tool)
    {
        CancelGesture();
        mode = tool;
        ink.EditingMode = tool == "pen" ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.None;
        ink.IsHitTestVisible = tool == "pen";
        drawing.Cursor = tool == "text" ? Cursors.IBeam : Cursors.Cross;
        foreach (var (key, button) in tools)
        {
            var selected = key == tool;
            button.Background = selected ? Icons.Soft("shot") : Ui.Brush("Card");
            button.BorderBrush = selected ? Ui.Brush("Accent") : Brushes.Transparent;
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            AutomationProperties.SetItemStatus(button, selected ? "Выбрано" : "");
        }
        hint.Text = tool switch
        {
            "arrow" => "Стрелка: зажмите кнопку мыши и ведите от начала к острию.",
            "box" => "Рамка: зажмите кнопку мыши и выделите нужную область.",
            "text" => "Текст: щёлкните по снимку, затем введите подпись.",
            "blur" => "Скрыть область: протяните рамку. Область будет непрозрачно закрашена чёрным.",
            _ => "Перо: рисуйте по снимку с зажатой кнопкой мыши. Цвет и толщину можно выбрать в панели инструментов."
        };
        UpdateOptions();
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
        if (mode == "pen") return;
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

    private void Undo()
    {
        if (temporary != null) CancelGesture();
        else if (undo.TryPop(out var action)) { action(); UpdateUndo(); }
    }

    private void CancelGesture()
    {
        var element = temporary;
        temporary = null;
        start = null;
        if (element != null) shapes.Children.Remove(element);
        if (drawing.IsMouseCaptured) drawing.ReleaseMouseCapture();
        UpdateUndo();
    }

    private void UpdateUndo()
    {
        undoButton.IsEnabled = undo.Count > 0 || temporary != null;
        CommandManager.InvalidateRequerySuggested();
    }

    private BitmapSource Export()
    {
        drawing.UpdateLayout();
        var bitmap = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        bitmap.Freeze();
        return bitmap;
    }
}
