namespace QuietIsland.Core;

public static class InteractionPolicy
{
    public static bool ShouldStartDrag(double dx, double dy, double minimumX, double minimumY, bool pressed, bool fromAction)
        => pressed && !fromAction && (Math.Abs(dx) >= minimumX || Math.Abs(dy) >= minimumY);

    public static bool CanScroll(double offset, double maximum, double delta)
        => delta < 0 ? offset > .1 : delta > 0 && offset < maximum - .1;
}

/// <summary>A time-based envelope: independent of the audio callback or rendering rate.</summary>
public sealed class SpectrumEnvelope(int count = 32)
{
    private readonly double[] values = new double[count];
    public IReadOnlyList<double> Values => values;
    public void Reset() => Array.Clear(values);
    public void Advance(IReadOnlyList<float> target, double seconds)
    {
        seconds = Math.Clamp(seconds, 0, .1);
        for (var i = 0; i < values.Length; i++)
        {
            var sum = 0d; var weight = 0d;
            for (var j = Math.Max(0, i - 2); j <= Math.Min(target.Count - 1, i + 2); j++)
            { var w = 3 - Math.Abs(j - i); sum += Math.Clamp(target[j], 0, 1) * w; weight += w; }
            var next = weight > 0 ? sum / weight : 0;
            var tau = next > values[i] ? .075 : .28;
            values[i] += (next - values[i]) * (1 - Math.Exp(-seconds / tau));
        }
    }
    public double Sample(double t)
    {
        var p = Math.Clamp(t, 0, 1) * (values.Length - 1); var i = (int)p;
        var f = p - i; f = f * f * (3 - 2 * f);
        return values[i] * (1 - f) + values[Math.Min(i + 1, values.Length - 1)] * f;
    }
}
