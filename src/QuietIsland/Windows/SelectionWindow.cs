using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using QuietIsland.Controls;
using QuietIsland.Services;

namespace QuietIsland.Windows;
public sealed class SelectionWindow : Window
{
    private readonly BitmapSource image;
    private readonly System.Drawing.Rectangle screen;
    private readonly Canvas overlay = new();
    private readonly Rectangle selection = new() { Stroke = new SolidColorBrush(Color.FromRgb(168, 230, 208)), StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(30, 168, 230, 208)) };
    private readonly Border loupe = new() { Background = Brushes.Black, Padding = new Thickness(8), CornerRadius = new CornerRadius(8), Visibility = Visibility.Collapsed };
    private readonly Image zoom = new() { Width = 108, Height = 108 };
    private readonly TextBlock colorText = Ui.Text("", 12);
    private Point? start;
    private readonly bool color;
    public CaptureResult? Result { get; private set; }
    public SelectionWindow(BitmapSource source, System.Drawing.Rectangle bounds, bool pickColor)
    {
        image = source; screen = bounds; color = pickColor;
        Title = pickColor ? "Выбрать цвет" : "Выделить область"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false; Cursor = Cursors.Cross;
        Left = SystemParameters.VirtualScreenLeft; Top = SystemParameters.VirtualScreenTop; Width = SystemParameters.VirtualScreenWidth; Height = SystemParameters.VirtualScreenHeight;
        var root = new Grid(); root.Children.Add(new Image { Source = image, Stretch = Stretch.Fill }); overlay.Background = new SolidColorBrush(Color.FromArgb(45, 0, 0, 0)); root.Children.Add(overlay); overlay.Children.Add(selection);
        var instruction = Ui.Card(Ui.Text(pickColor ? "Нажмите на цвет · Esc — отмена" : "Выделите область · Esc — отмена", 14), 16); instruction.HorizontalAlignment = HorizontalAlignment.Center; instruction.VerticalAlignment = VerticalAlignment.Top; instruction.Margin = new Thickness(0, 24, 0, 0); instruction.IsHitTestVisible = false; root.Children.Add(instruction);
        loupe.Child = Ui.Stack(zoom, colorText); loupe.IsHitTestVisible = false; overlay.Children.Add(loupe); RenderOptions.SetBitmapScalingMode(zoom, BitmapScalingMode.NearestNeighbor); Content = root;
        SourceInitialized += (_, _) => Native.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(this).Handle, new IntPtr(-1), bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0040);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) DialogResult = false; }; MouseRightButtonDown += (_, _) => DialogResult = false;
        MouseLeftButtonDown += (_, e) => { var point = e.GetPosition(overlay); if (color) { var (x, y) = Pixel(point); Finish(x, y, 1, 1); } else { start = point; Mouse.Capture(overlay); Canvas.SetLeft(selection, point.X); Canvas.SetTop(selection, point.Y); selection.Width = selection.Height = 0; } };
        MouseMove += (_, e) => Move(e.GetPosition(overlay));
        MouseLeftButtonUp += (_, e) =>
        {
            if (start == null || color) return; var a = Pixel(start.Value); var b = Pixel(e.GetPosition(overlay)); Mouse.Capture(null);
            var x = Math.Min(a.X, b.X); var y = Math.Min(a.Y, b.Y); var width = Math.Abs(a.X - b.X); var height = Math.Abs(a.Y - b.Y);
            if (width >= 2 && height >= 2) Finish(x, y, width, height); else start = null;
        };
    }
    private (int X, int Y) Pixel(Point point) => (Math.Clamp((int)(point.X / Math.Max(1, overlay.ActualWidth) * image.PixelWidth), 0, image.PixelWidth - 1), Math.Clamp((int)(point.Y / Math.Max(1, overlay.ActualHeight) * image.PixelHeight), 0, image.PixelHeight - 1));
    private void Move(Point point)
    {
        if (color)
        {
            var (x, y) = Pixel(point); var sample = new CroppedBitmap(image, new Int32Rect(Math.Clamp(x - 4, 0, Math.Max(0, image.PixelWidth - 9)), Math.Clamp(y - 4, 0, Math.Max(0, image.PixelHeight - 9)), Math.Min(9, image.PixelWidth), Math.Min(9, image.PixelHeight)));
            zoom.Source = sample; var bgra = new FormatConvertedBitmap(new CroppedBitmap(image, new Int32Rect(x, y, 1, 1)), PixelFormats.Bgra32, null, 0); var pixel = new byte[4]; bgra.CopyPixels(pixel, 4, 0); colorText.Text = $"#{pixel[2]:X2}{pixel[1]:X2}{pixel[0]:X2}";
            loupe.Visibility = Visibility.Visible; Canvas.SetLeft(loupe, Math.Clamp(point.X + 24, 0, Math.Max(0, overlay.ActualWidth - 140))); Canvas.SetTop(loupe, Math.Clamp(point.Y + 20, 0, Math.Max(0, overlay.ActualHeight - 160))); return;
        }
        if (start is not { } a) return; Canvas.SetLeft(selection, Math.Min(a.X, point.X)); Canvas.SetTop(selection, Math.Min(a.Y, point.Y)); selection.Width = Math.Abs(a.X - point.X); selection.Height = Math.Abs(a.Y - point.Y);
    }
    private void Finish(int x, int y, int width, int height)
    {
        var cropped = new CroppedBitmap(image, new Int32Rect(x, y, Math.Min(width, image.PixelWidth - x), Math.Min(height, image.PixelHeight - y))); cropped.Freeze();
        Result = new(cropped, new System.Drawing.Rectangle(screen.X + x, screen.Y + y, cropped.PixelWidth, cropped.PixelHeight)); DialogResult = true;
    }
}
