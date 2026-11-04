namespace QuietIsland.Core;

public static class OverlayBehavior
{
    public static bool ShouldDismiss(bool expanded, bool pinned, bool pointerInside, bool popupOpen,
        bool keyboardHold, double elapsedMs, double delayMs)
        => expanded && !pinned && !pointerInside && !popupOpen && !keyboardHold && elapsedMs >= delayMs;
}

public static class MusicContour
{
    /// <summary>Local frequency energy supplies each displacement; elapsed time and the global peak never supply motion.</summary>
    public static double Sample(double position, double phase, double band, double level, double sensitivity, double amplitude)
    {
        // phase and level remain in the signature for existing callers, but cannot create a waveform.
        if (!double.IsFinite(position) || !double.IsFinite(band) || !double.IsFinite(sensitivity) || !double.IsFinite(amplitude)
            || position <= 0 || position >= 1 || amplitude <= 0) return 0;
        var energy = Math.Clamp(band * sensitivity, 0, 1);
        if (energy < .003) return 0;
        var endFade = Math.Pow(4 * position * (1 - position), .65);
        return amplitude * Math.Pow(energy, .7) * endFade;
    }
}
