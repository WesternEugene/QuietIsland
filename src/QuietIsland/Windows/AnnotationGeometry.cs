using System.Windows;
using System.Windows.Media;

namespace QuietIsland.Windows;

internal static class AnnotationGeometry
{
    internal static Point Clamp(Point point, Size image) => new(
        Math.Clamp(point.X, 0, image.Width),
        Math.Clamp(point.Y, 0, image.Height));

    internal static StreamGeometry Arrow(Point start, Point tip, double thickness)
    {
        var geometry = new StreamGeometry();
        var direction = tip - start;
        var length = direction.Length;
        if (length > .001)
        {
            direction /= length;
            var normal = new Vector(-direction.Y, direction.X);
            var headLength = Math.Min(length * .7, Math.Max(12, thickness * 3.5));
            var headHalfWidth = headLength * .5;
            var shaftHalfWidth = Math.Min(thickness / 2, headLength * .3);
            var neck = tip - direction * headLength;
            using var path = geometry.Open();
            path.BeginFigure(start + normal * shaftHalfWidth, true, true);
            path.LineTo(neck + normal * shaftHalfWidth, true, false);
            path.LineTo(neck + normal * headHalfWidth, true, false);
            path.LineTo(tip, true, false);
            path.LineTo(neck - normal * headHalfWidth, true, false);
            path.LineTo(neck - normal * shaftHalfWidth, true, false);
            path.LineTo(start - normal * shaftHalfWidth, true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    internal static Int32Rect RedactionBounds(Point start, Point end, int width, int height)
    {
        var size = new Size(width, height);
        var bounds = new Rect(Clamp(start, size), Clamp(end, size));
        var left = (int)Math.Floor(bounds.Left);
        var top = (int)Math.Floor(bounds.Top);
        return new Int32Rect(left, top, (int)Math.Ceiling(bounds.Right) - left, (int)Math.Ceiling(bounds.Bottom) - top);
    }
}
