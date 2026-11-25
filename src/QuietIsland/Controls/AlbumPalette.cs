using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuietIsland.Controls;

internal static class AlbumPalette
{
    public static Color Accent(BitmapSource? source)
    {
        var fallback = Icons.Accent("coral");
        if (source == null) return fallback;
        try
        {
            var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var stride = checked(bitmap.PixelWidth * 4);
            var pixels = new byte[checked(stride * bitmap.PixelHeight)];
            bitmap.CopyPixels(pixels, stride, 0);

            var accent = fallback;
            var bestScore = -1;
            var bestLuma = -1;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                if (pixels[i + 3] < 128) continue;
                var red = pixels[i + 2];
                var green = pixels[i + 1];
                var blue = pixels[i];
                var value = Math.Max(red, Math.Max(green, blue));
                var chroma = value - Math.Min(red, Math.Min(green, blue));
                var score = value * chroma;
                if (score < bestScore) continue;

                // Use luma to break ties, including on grayscale covers.
                var luma = 299 * red + 587 * green + 114 * blue;
                if (score == bestScore && luma <= bestLuma) continue;

                accent = Color.FromRgb(red, green, blue);
                bestScore = score;
                bestLuma = luma;
            }
            return accent;
        }
        catch (Exception) { return fallback; }
    }
    public static Color Control(Color color)
    {
        var white = Math.Min(color.R, Math.Min(color.G, color.B)) > 225;
        byte Adjust(byte value) => (byte)Math.Clamp(white ? value * .78 : value + (255 - value) * .24, 0, 255);
        return Color.FromRgb(Adjust(color.R), Adjust(color.G), Adjust(color.B));
    }
    public static Brush Surface(Color color)
    {
        var light = Ui.Brush("Text") is SolidColorBrush text && text.Color.R < 128;
        var baseline = light ? 245 : 12; var strength = light ? .12 : .12;
        var brush = new SolidColorBrush(Color.FromRgb((byte)(baseline * (1 - strength) + color.R * strength), (byte)(baseline * (1 - strength) + color.G * strength), (byte)(baseline * (1 - strength) + color.B * strength)));
        brush.Freeze(); return brush;
    }
}
