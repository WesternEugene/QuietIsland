namespace QuietIsland.Core;
public record OverlayPlacement(int X, int Y, int Width, int Height, double Scale);
public static class OverlayLayout
{
    public static OverlayPlacement Calculate(int left, int top, int width, int height, double dpiX, double dpiY, double scale, double topOffset, bool expanded)
        => CalculateSize(left, top, width, height, dpiX, dpiY, scale, topOffset, expanded ? 744 : 280, expanded ? 430 : 52);

    public static OverlayPlacement CalculateSize(int left, int top, int width, int height, double dpiX, double dpiY, double scale, double topOffset, double baseWidth, double baseHeight)
    {
        dpiX = double.IsFinite(dpiX) && dpiX > 0 ? dpiX : 1;
        dpiY = double.IsFinite(dpiY) && dpiY > 0 ? dpiY : 1;
        var offset = Math.Clamp(topOffset, 0, Math.Max(0, height / dpiY - baseHeight * .1));
        var fits = Math.Min(width / dpiX / baseWidth, (height / dpiY - offset) / baseHeight);
        var effective = Math.Clamp(Math.Min(scale, fits), .1, 2);
        var y = top + (int)Math.Round(offset * dpiY);
        var w = Math.Clamp((int)Math.Ceiling(baseWidth * effective * dpiX), 1, width);
        var h = Math.Clamp((int)Math.Ceiling(baseHeight * effective * dpiY), 1, Math.Max(1, top + height - y));
        return new(left + (width - w) / 2, y, w, h, effective);
    }
}
