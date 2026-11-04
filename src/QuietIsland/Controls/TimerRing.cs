using System.Windows;
using System.Windows.Media;

namespace QuietIsland.Controls;

public sealed class TimerRing : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(double), typeof(TimerRing), new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PausedProperty = DependencyProperty.Register(nameof(Paused), typeof(bool), typeof(TimerRing), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public bool Paused { get => (bool)GetValue(PausedProperty); set => SetValue(PausedProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight); if (size < 4) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2); var thickness = size < 30 ? 2.4 : 4;
        var radius = (size - thickness) / 2; var progress = Math.Clamp(Progress, 0, 1);
        dc.DrawEllipse(null, new Pen(Ui.Brush("Line"), thickness), center, radius, radius);
        var ink = Paused ? Ui.Brush("Muted") : Ui.Brush("Accent");
        if (progress >= .9999) dc.DrawEllipse(null, new Pen(ink, thickness), center, radius, radius);
        else if (progress > 0)
        {
            var angle = progress * 2 * Math.PI;
            var arc = new StreamGeometry(); using (var g = arc.Open())
            {
                g.BeginFigure(new Point(center.X, center.Y - radius), false, false);
                g.ArcTo(new Point(center.X + Math.Sin(angle) * radius, center.Y - Math.Cos(angle) * radius), new Size(radius, radius), 0, progress > .5, SweepDirection.Clockwise, true, false);
            }
            dc.DrawGeometry(null, new Pen(ink, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
        }
    }
}
