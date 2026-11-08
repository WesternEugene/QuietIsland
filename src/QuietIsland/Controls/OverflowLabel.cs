using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace QuietIsland.Controls;

/// <summary>One line, endpoint rests and eased travel in both directions; no frame timer.</summary>
internal sealed class OverflowLabel : Grid
{
    private readonly TextBlock label;
    private readonly TranslateTransform shift = new();
    private double distance = -1;
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
        var next = Math.Max(0, label.DesiredSize.Width - ActualWidth);
        if (!IsLoaded || !IsVisible || ActualWidth <= 0) { Stop(); return; }
        if (Math.Abs(next - distance) < .5) return;
        Stop(); distance = next;
        if (distance < 1) return;
        var edge = Math.Min(10, ActualWidth / 4) / ActualWidth;
        var fade = new LinearGradientBrush
        {
            StartPoint = new Point(0, .5), EndPoint = new Point(1, .5),
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
    private void Stop() { shift.BeginAnimation(TranslateTransform.XProperty, null); shift.X = 0; OpacityMask = null; distance = -1; }
}
