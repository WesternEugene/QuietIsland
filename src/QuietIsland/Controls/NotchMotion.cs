using System.Windows.Media;
using QuietIsland.Core;

namespace QuietIsland.Controls;

internal sealed class NotchMotion(LiquidSurface surface) : IDisposable
{
    private readonly MotionValue width = new(surface.VisualWidth), height = new(surface.VisualHeight);
    private bool active;
    private TimeSpan last;
    private Action? complete;
    public bool Active => active;
    public double Speed { get; set; } = 1;
    public void To(double w, double h, bool animate, Action? completed = null, bool keepCompletion = false)
    {
        if (!keepCompletion || !active) complete = completed;
        if (!animate) { Stop(); width.Snap(w); height.Snap(h); Present(); completed?.Invoke(); return; }
        width.Target = w; height.Target = h;
        if (active) return;
        active = true; last = TimeSpan.Zero; CompositionTarget.Rendering += Frame;
    }
    private void Frame(object? sender, EventArgs args)
    {
        if (args is not RenderingEventArgs e || e.RenderingTime == last) return;
        var dt = last == TimeSpan.Zero ? 1d / 60 : (e.RenderingTime - last).TotalSeconds; last = e.RenderingTime;
        width.Advance(dt * Speed); height.Advance(dt * Speed); Present();
        if (!width.Settled || !height.Settled) return;
        width.Snap(width.Target); height.Snap(height.Target); Present();
        var callback = complete; Stop(); callback?.Invoke();
    }
    private void Present() { surface.VisualWidth = Math.Clamp(width.Value, 150, 744); surface.VisualHeight = Math.Clamp(height.Value, 44, 430); }
    private void Stop() { if (active) CompositionTarget.Rendering -= Frame; active = false; last = TimeSpan.Zero; complete = null; }
    public void Dispose() => Stop();
}
