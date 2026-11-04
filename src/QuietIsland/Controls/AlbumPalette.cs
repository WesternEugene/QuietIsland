using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuietIsland.Controls;

internal static class AlbumPalette
{
    public static Color Accent(BitmapSource? source)
    {
        if (source == null) return Icons.Accent("coral");
        try
        {
            var scaled = new TransformedBitmap(source, new ScaleTransform(24d / source.PixelWidth, 24d / source.PixelHeight));
            var bitmap = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0); var pixels = new byte[24 * 24 * 4]; bitmap.CopyPixels(pixels, 24 * 4, 0);
            var buckets = new (double Weight, double R, double G, double B)[12];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i + 3] < 128) continue;
                var r = pixels[i + 2] / 255d; var g = pixels[i + 1] / 255d; var b = pixels[i] / 255d;
                var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var delta = max - min;
                if (delta < .15 || max < .25) continue;
                var hue = max == r ? (g - b) / delta : max == g ? 2 + (b - r) / delta : 4 + (r - g) / delta;
                var index = (int)((hue * 60 + 360) % 360 / 30); var weight = delta * max;
                buckets[index].Weight += weight; buckets[index].R += r * weight; buckets[index].G += g * weight; buckets[index].B += b * weight;
            }
            var best = buckets.MaxBy(x => x.Weight); if (best.Weight <= 0) return Icons.Accent("coral");
            return Color.FromRgb((byte)Math.Clamp(best.R / best.Weight * 200 + 45, 0, 255), (byte)Math.Clamp(best.G / best.Weight * 200 + 45, 0, 255), (byte)Math.Clamp(best.B / best.Weight * 200 + 45, 0, 255));
        }
        catch (Exception) { return Icons.Accent("coral"); }
    }
    public static Brush Surface(Color color)
    {
        var light = Ui.Brush("Text") is SolidColorBrush text && text.Color.R < 128;
        var baseline = light ? 245 : 12; var strength = light ? .12 : .12;
        var brush = new SolidColorBrush(Color.FromRgb((byte)(baseline * (1 - strength) + color.R * strength), (byte)(baseline * (1 - strength) + color.G * strength), (byte)(baseline * (1 - strength) + color.B * strength)));
        brush.Freeze(); return brush;
    }
}
