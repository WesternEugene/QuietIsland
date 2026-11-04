using System.Windows;
using System.Windows.Media;
using QuietIsland.Core;
using QuietIsland.Models;
using QuietIsland.Services;

namespace QuietIsland.Controls;

/// <summary>A stable attached silhouette with audio-driven displacement along its whole lower contour.</summary>
public sealed class LiquidSurface : FrameworkElement
{
    public static readonly DependencyProperty VisualWidthProperty = DependencyProperty.Register(nameof(VisualWidth), typeof(double), typeof(LiquidSurface), new FrameworkPropertyMetadata(190d, FrameworkPropertyMetadataOptions.AffectsRender, ChangedDimensions));
    public static readonly DependencyProperty VisualHeightProperty = DependencyProperty.Register(nameof(VisualHeight), typeof(double), typeof(LiquidSurface), new FrameworkPropertyMetadata(52d, FrameworkPropertyMetadataOptions.AffectsRender, ChangedDimensions));
    public double VisualWidth { get => (double)GetValue(VisualWidthProperty); set => SetValue(VisualWidthProperty, value); }
    public double VisualHeight { get => (double)GetValue(VisualHeightProperty); set => SetValue(VisualHeightProperty, value); }
    public event Action? DimensionsChanged;
    private static void ChangedDimensions(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((LiquidSurface)d).DimensionsChanged?.Invoke();
    private readonly SpectrumEnvelope envelope = new();
    private MediaService? media;
    private AppSettings appearance = new();
    private TimeSpan lastFrame, nextFrame;
    private double level;
    private bool subscribed, suspended, economy;
    private Geometry? shape;
    private System.Windows.Media.Imaging.BitmapSource? lastCover;
    private readonly SolidColorBrush musicInk = new(Icons.Accent("coral"));
    public bool Playing { get; private set; }
    internal bool ContainsSurface(Point point) => shape?.FillContains(point) == true;
    internal double AudioLevelForChecks => level;
    public LiquidSurface()
    {
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
        IsVisibleChanged += (_, _) => { if (IsVisible) Subscribe(); else Unsubscribe(); };
        SizeChanged += (_, _) => InvalidateVisual();
    }
    public void ApplyAppearance(AppSettings settings)
    {
        appearance = settings;
        lastCover = null;
        if (media != null) Update(media); else InvalidateVisual();
    }
    public void Update(MediaService service)
    {
        media = service;
        if (!ReferenceEquals(lastCover, service.Current.Cover) || lastCover == null)
        {
            lastCover = service.Current.Cover;
            musicInk.Color = appearance.UseAlbumAccent && lastCover != null ? AlbumPalette.Accent(lastCover) : (Color)ColorConverter.ConvertFromString(appearance.AccentColor);
        }
        Playing = appearance.MusicAnimationEnabled && AnimationPolicy.ShouldAnimate(service.Current.HasMedia && service.Current.IsMusic, service.Current.IsPlaying);
        if (Playing) Subscribe();
        else { Unsubscribe(); level = 0; envelope.Reset(); }
        if (!suspended) InvalidateVisual();
    }
    public void SetRuntime(bool suspended, bool economy)
    {
        if (this.suspended == suspended && this.economy == economy) return;
        this.suspended = suspended; this.economy = economy; nextFrame = TimeSpan.Zero;
        if (suspended) { Unsubscribe(); level = 0; envelope.Reset(); }
        else Subscribe();
        if (IsVisible) InvalidateVisual();
    }
    private void Subscribe()
    {
        if (!Playing || suspended || subscribed || !IsLoaded || !IsVisible) return;
        subscribed = true; lastFrame = nextFrame = TimeSpan.Zero; CompositionTarget.Rendering += RenderFrame;
    }
    private void Unsubscribe() { if (subscribed) CompositionTarget.Rendering -= RenderFrame; subscribed = false; lastFrame = nextFrame = TimeSpan.Zero; }
    private void RenderFrame(object? sender, EventArgs args)
    {
        if (suspended || args is not RenderingEventArgs frame || frame.RenderingTime == lastFrame || frame.RenderingTime < nextFrame) return;
        var interval = TimeSpan.FromSeconds(1d / (economy ? 12 : 60));
        var elapsed = lastFrame == TimeSpan.Zero ? 1d / 60 : Math.Clamp((frame.RenderingTime - lastFrame).TotalSeconds, 0, 2);
        lastFrame = frame.RenderingTime;
        nextFrame = nextFrame == TimeSpan.Zero || frame.RenderingTime - nextFrame > interval
            ? frame.RenderingTime + interval : nextFrame + interval;
        var audio = media?.ReadAudio() ?? new AudioFrame(new float[32], 0);
        // SpectrumEnvelope bounds an integration step to 100 ms; retain the full elapsed time
        // across economy frames and delayed dispatcher work instead of slowing attack/release.
        for (var remaining = elapsed; remaining > 0; remaining -= Math.Min(remaining, .1))
            envelope.Advance(audio.Spectrum, Math.Min(remaining, .1));
        level += (audio.Peak - level) * (1 - Math.Exp(-elapsed / (audio.Peak > level ? .045 : .19)));
        if (level < .001 && envelope.Values.All(x => x < .001)) { level = 0; envelope.Reset(); }
        InvalidateVisual();
    }
    protected override HitTestResult? HitTestCore(PointHitTestParameters parameters)
        => shape?.FillContains(parameters.HitPoint) == true ? new PointHitTestResult(this, parameters.HitPoint) : null;
    protected override void OnRender(DrawingContext dc)
    {
        var w = Math.Min(ActualWidth, VisualWidth); var h = VisualHeight - 8;
        if (w <= 0 || h <= 0) return;
        var shoulder = Math.Min(appearance.ShoulderRadius, w / 10);
        var openness = Math.Clamp((VisualHeight - appearance.CompactHeight) / (376 - appearance.CompactHeight), 0, 1);
        var radius = Math.Min(appearance.CompactCornerRadius + (appearance.ExpandedCornerRadius - appearance.CompactCornerRadius) * openness, Math.Min(w / 4, Math.Max(0, (h - shoulder) * .7)));
        var left = shoulder + radius; var right = w - shoulder - radius;
        var points = new List<Point>();
        const int corners = 8, middle = 40;
        for (var i = 0; i <= corners; i++)
        {
            var t = i / (double)corners; var u = 1 - t;
            points.Add(new Point(u * u * (w - shoulder) + 2 * u * t * (w - shoulder) + t * t * right, u * u * (h - radius) + (2 * u * t + t * t) * h));
        }
        for (var i = 1; i <= middle; i++) points.Add(new Point(right + (left - right) * i / middle, h));
        for (var i = 1; i <= corners; i++)
        {
            var t = i / (double)corners; var u = 1 - t;
            points.Add(new Point(u * u * left + (2 * u * t + t * t) * shoulder, (u * u + 2 * u * t) * h + t * t * (h - radius)));
        }
        var contentHeight = Math.Max(28, (appearance.CompactFontSize + 9) * 1.4);
        var contentBottom = (appearance.CompactHeight - 8 + contentHeight) / 2;
        var inwardRoom = Math.Max(0, h - contentBottom - 2);
        var outwardRoom = Math.Max(0, ActualHeight - h - 2);
        if (Playing && !suspended)
            for (var i = 1; i < points.Count - 1; i++)
            {
                var t = i / (double)(points.Count - 1);
                var displacement = MusicContour.Sample(t, 0, envelope.Sample(1 - t), 0, appearance.MusicSensitivity, appearance.MusicAmplitude);
                // Keep the inner edge below the compact text/cover even at the smallest height.
                // Compression is smooth at zero and asymptotically approaches the available room.
                var room = displacement < 0 ? inwardRoom : outwardRoom;
                if (room <= 0) displacement = 0;
                else displacement = Math.Sign(displacement) * room * (1 - Math.Exp(-Math.Abs(displacement) / room));
                var p = points[i]; p.Y += displacement; points[i] = p;
            }
        void LowerContour(StreamGeometryContext g)
        {
            for (var i = 0; i < points.Count - 1; i++)
            {
                var a = points[i]; var b = points[i + 1];
                var previous = i > 0 ? points[i - 1] : new Point(a.X, a.Y - Math.Max(1, radius / corners));
                var next = i + 2 < points.Count ? points[i + 2] : new Point(b.X, b.Y - Math.Max(1, radius / corners));
                g.BezierTo(a + (b - previous) / 6, b - (next - a) / 6, b, true, true);
            }
        }
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(0, 0), true, true); g.LineTo(new Point(w, 0), true, false);
            g.BezierTo(new Point(w - shoulder, 0), new Point(w - shoulder, shoulder), new Point(w - shoulder, shoulder), true, true);
            g.LineTo(points[0], true, true); LowerContour(g);
            g.LineTo(new Point(shoulder, shoulder), true, true);
            g.BezierTo(new Point(shoulder, shoulder), new Point(shoulder, 0), new Point(0, 0), true, true);
        }
        geometry.Transform = new TranslateTransform((ActualWidth - w) / 2, 0); geometry.Freeze(); shape = geometry;
        dc.DrawGeometry(Ui.Brush("Surface"), null, geometry);
        if (Playing)
        {
            var edge = new StreamGeometry();
            using (var g = edge.Open()) { g.BeginFigure(points[0], false, false); LowerContour(g); }
            edge.Transform = geometry.Transform; edge.Freeze(); dc.DrawGeometry(null, new Pen(musicInk, 1.7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, edge);
        }
    }
}
