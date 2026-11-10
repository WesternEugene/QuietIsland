using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace QuietIsland.Controls;

/// <summary>A hollow battery arc with a rounded, separated tip at the near-full join.</summary>
public sealed class BatteryRing : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(double), typeof(BatteryRing), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GapBrushProperty = DependencyProperty.Register(nameof(GapBrush), typeof(Brush), typeof(BatteryRing), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly Brush Green = Ink("#64D98B"), Yellow = Ink("#F5CC55"), Red = Ink("#FF6268");
    private int percent;
    private int animationRevision;
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public Brush GapBrush { get => (Brush)GetValue(GapBrushProperty); set => SetValue(GapBrushProperty, value); }
    public BatteryRing()
    {
        SetResourceReference(GapBrushProperty, "Surface");
        IsVisibleChanged += (_, _) => { if (!IsVisible) SetPercent(percent, false); };
        Unloaded += (_, _) => SetPercent(percent, false);
    }
    public void SetPercent(int value, bool animate, double speed = 1)
    {
        percent = Math.Clamp(value, 0, 100);
        var revision = ++animationRevision;
        var target = percent / 100d; var from = Progress;
        BeginAnimation(ProgressProperty, null); Progress = target; InvalidateVisual();
        if (!animate || Math.Abs(from - target) < .0001) return;
        var animation = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(420 / Math.Clamp(speed, .6, 1.8)))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
        animation.Completed += (_, _) => { if (revision == animationRevision) BeginAnimation(ProgressProperty, null); };
        BeginAnimation(ProgressProperty, animation);
    }
    private static Brush Ink(string hex) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); brush.Freeze(); return brush; }
    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight); if (size < 6) return;
        var thickness = Math.Max(2.4, size * .12); var gap = Math.Max(.9, size * .035);
        var radius = (size - thickness - 2 * gap) / 2;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var progress = Math.Clamp(double.IsFinite(Progress) ? Progress : 0, 0, 1);
        dc.DrawEllipse(null, new Pen(Ui.Brush("Line"), thickness), center, radius, radius);
        if (progress <= 0) return;
        var ink = percent > 50 ? Green : percent > 30 ? Yellow : Red;
        var pen = new Pen(ink, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var angle = progress * 2 * Math.PI;
        var end = new Point(center.X + Math.Sin(angle) * radius, center.Y - Math.Cos(angle) * radius);
        if (progress >= .99999) dc.DrawEllipse(null, pen, center, radius, radius);
        else
        {
            var geometry = new StreamGeometry();
            using (var path = geometry.Open())
            {
                path.BeginFigure(new(center.X, center.Y - radius), false, false);
                path.ArcTo(end, new(radius, radius), 0, progress > .5, SweepDirection.Clockwise, true, false);
            }
            geometry.Freeze(); dc.DrawGeometry(null, pen, geometry);
        }
        // Separate the approaching rounded tip from the start using the island's actual color.
        if (progress > .5 && 2 * radius * Math.Sin((1 - progress) * Math.PI) < thickness + 2 * gap)
        {
            dc.DrawEllipse(GapBrush, null, end, thickness / 2 + gap, thickness / 2 + gap);
            dc.DrawEllipse(ink, null, end, thickness / 2, thickness / 2);
        }
    }
}
