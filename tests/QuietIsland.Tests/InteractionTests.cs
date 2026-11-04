using QuietIsland.Core;
using Xunit;

namespace QuietIsland.Tests;
public sealed class InteractionTests
{
    [Fact]
    public void SmallPointerMovementAndActionClicksDoNotBecomeFileDrags()
    {
        Assert.False(InteractionPolicy.ShouldStartDrag(2, 1, 4, 4, true, false));
        Assert.False(InteractionPolicy.ShouldStartDrag(40, 30, 4, 4, true, true));
        Assert.False(InteractionPolicy.ShouldStartDrag(40, 30, 4, 4, false, false));
        Assert.True(InteractionPolicy.ShouldStartDrag(5, 0, 4, 4, true, false));
    }
    [Fact]
    public void NestedScrollerPassesWheelToParentAtBothEnds()
    {
        Assert.False(InteractionPolicy.CanScroll(0, 100, -30));
        Assert.True(InteractionPolicy.CanScroll(0, 100, 30));
        Assert.False(InteractionPolicy.CanScroll(100, 100, 30));
        Assert.True(InteractionPolicy.CanScroll(100, 100, -30));
    }
    [Fact]
    public void SpectrumHasSmoothAttackReleaseAndResetsAtPause()
    {
        var e = new SpectrumEnvelope(); var target = Enumerable.Repeat(1f, 32).ToArray();
        e.Advance(target, 1d / 60); var first = e.Sample(.5);
        Assert.InRange(first, .01, .4);
        for (var i = 0; i < 60; i++) e.Advance(target, 1d / 60);
        Assert.InRange(e.Sample(.5), .95, 1);
        e.Advance(new float[32], 1d / 60); Assert.InRange(e.Sample(.5), .8, 1);
        e.Reset(); Assert.All(e.Values, value => Assert.Equal(0, value));
    }
    [Fact]
    public void SpectrumEnvelopeIsIndependentOfFrameRate()
    {
        var a = new SpectrumEnvelope(); var b = new SpectrumEnvelope(); var target = Enumerable.Repeat(.7f, 32).ToArray();
        for (var i = 0; i < 60; i++) a.Advance(target, 1d / 60);
        for (var i = 0; i < 30; i++) b.Advance(target, 1d / 30);
        Assert.Equal(a.Sample(.4), b.Sample(.4), 6);
    }
}
