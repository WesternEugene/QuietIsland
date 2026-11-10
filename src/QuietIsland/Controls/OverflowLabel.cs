using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace QuietIsland.Controls;

/// <summary>One line, endpoint rests and eased travel in both directions; no frame timer.</summary>
internal sealed class OverflowLabel : Grid
{
    public const double EdgeWidth = 10;
    public double EdgePadding { get; set; }
    private readonly TextBlock label;
    private readonly TranslateTransform shift = new();
    private double distance = -1, restInset;
    private string measuredText = "";
    public OverflowLabel(TextBlock text)
    {
        label = text; ClipToBounds = true;
        text.TextTrimming = TextTrimming.None; text.TextWrapping = TextWrapping.NoWrap;
        text.RenderTransform = shift; text.VerticalAlignment = VerticalAlignment.Top;
        var canvas = new Canvas { IsHitTestVisible = false }; canvas.Children.Add(text); Children.Add(canvas);
        SizeChanged += (_, _) => Refresh(); Loaded += (_, _) => Refresh();
        IsVisibleChanged += (_, _) => Refresh(); Unloaded += (_, _) => Stop();
    }
    public void Refresh()
    {
        if (measuredText != label.Text) { measuredText = label.Text; Stop(); }
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Height = Math.Ceiling(label.DesiredSize.Height);
        var edgeWidth = Math.Min(EdgeWidth, ActualWidth / 4);
        var reservedInset = Math.Min(EdgePadding, edgeWidth);
        var overflow = label.DesiredSize.Width - ActualWidth + 2 * reservedInset;
        var nextInset = overflow >= 1 ? edgeWidth : reservedInset;
        var next = Math.Max(0, label.DesiredSize.Width - ActualWidth + 2 * nextInset);
        if (!IsLoaded || !IsVisible || ActualWidth <= 0) { Stop(); return; }
        if (Math.Abs(next - distance) < .5 && Math.Abs(nextInset - restInset) < .1) return;
        Stop(); distance = next;
        restInset = nextInset; Canvas.SetLeft(label, restInset);
        if (distance < 1) return;
        var edge = edgeWidth / ActualWidth;
        var fade = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0), EndPoint = new Point(ActualWidth, 0),
            GradientStops = [new(Colors.Transparent, 0), new(Colors.Black, edge), new(Colors.Black, 1 - edge), new(Colors.Transparent, 1)]
        };
        fade.Freeze(); OpacityMask = fade;
        var travel = Math.Clamp(distance / 32, 1.8, 18);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var animation = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.3))));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(-distance, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.3 + travel)), ease));
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(-distance, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.6 + travel))));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.6 + 2 * travel)), ease));
        shift.BeginAnimation(TranslateTransform.XProperty, animation);
    }
    private void Stop() { shift.BeginAnimation(TranslateTransform.XProperty, null); shift.X = 0; Canvas.SetLeft(label, 0); restInset = 0; OpacityMask = null; distance = -1; }
}
