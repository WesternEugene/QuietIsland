using QuietIsland.Core;
using Xunit;

namespace QuietIsland.Tests;

public sealed class FrequencyContourTests
{
    [Fact]
    public void LocalFrequencyEnergyControlsTheContourWithoutAClockOrPeakBaseline()
    {
        Assert.Equal(0, MusicContour.Sample(.4, 1, 0, 1, 1, 7));
        Assert.Equal(MusicContour.Sample(.4, 0, .6, .1, 1, 7),
            MusicContour.Sample(.4, 20, .6, 1, 1, 7));
    }

    [Fact]
    public void EqualPeakLowAndHighBandsDisplaceDifferentContourRegions()
    {
        var low = new float[32]; low[4] = .8f;
        var high = new float[32]; high[25] = .8f;
        var lowContour = Contour(low);
        var highContour = Contour(high);

        var lowPeak = Array.IndexOf(lowContour, lowContour.Max()) / 100d;
        var highPeak = Array.IndexOf(highContour, highContour.Max()) / 100d;
        Assert.InRange(lowPeak, .7, .95);
        Assert.InRange(highPeak, .05, .35);
        Assert.True(lowPeak - highPeak > .4);
    }

    [Fact]
    public void MissingAudioSettlesToSilenceWithStationaryEnds()
    {
        var envelope = new SpectrumEnvelope();
        var signal = Enumerable.Repeat(.8f, 32).ToArray();
        for (var i = 0; i < 12; i++) envelope.Advance(signal, .1);
        Assert.True(MusicContour.Sample(.02, 0, envelope.Sample(.98), 0, 1, 7) > 0);
        Assert.True(MusicContour.Sample(.98, 0, envelope.Sample(.02), 0, 1, 7) > 0);
        Assert.Equal(0, MusicContour.Sample(0, 0, 1, 1, 1, 7));
        Assert.Equal(0, MusicContour.Sample(1, 0, 1, 1, 1, 7));

        for (var i = 0; i < 40; i++) envelope.Advance(new float[32], .1);
        Assert.All(Enumerable.Range(0, 101), i =>
            Assert.Equal(0, MusicContour.Sample(i / 100d, 20, envelope.Sample(1 - i / 100d), 0, 1, 7)));
    }

    private static double[] Contour(float[] spectrum)
    {
        var envelope = new SpectrumEnvelope();
        for (var i = 0; i < 12; i++) envelope.Advance(spectrum, .1);
        return Enumerable.Range(0, 101).Select(i =>
            Math.Abs(MusicContour.Sample(i / 100d, 1, envelope.Sample(1 - i / 100d), .8, 1, 7))).ToArray();
    }
}
