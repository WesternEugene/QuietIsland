namespace QuietIsland.Core;

public static class CompactLayout
{
    public static double Width(double title, double subtitle, double meta, double maximum)
        => Math.Ceiling(Math.Clamp(Math.Max(title, subtitle) + meta + 100, Math.Min(190, maximum), maximum));
}

/// <summary>Analytic critically damped motion retains its velocity when the target changes.</summary>
public sealed class MotionValue(double initial)
{
    public double Value { get; private set; } = initial;
    public double Velocity { get; private set; }
    public double Target { get; set; } = initial;
    public bool Settled => Math.Abs(Value - Target) < .08 && Math.Abs(Velocity) < .8;
    public void Snap(double value) { Value = Target = value; Velocity = 0; }
    public void Advance(double seconds)
    {
        if (seconds <= 0) return;
        var dt = Math.Min(seconds, .1); const double omega = 24;
        var x = Value - Target; var step = (Velocity + omega * x) * dt; var decay = Math.Exp(-omega * dt);
        Value = Target + (x + step) * decay;
        Velocity = (Velocity - omega * step) * decay;
    }
}
