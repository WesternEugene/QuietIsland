using QuietIsland.Core;
using Xunit;

namespace QuietIsland.Tests;
public sealed class MotionTests
{
    [Fact]
    public void CompactMeasuresBothLinesAndKeepsMonitorLimit()
    {
        Assert.True(CompactLayout.Width(350, 120, 25, 700) >= 475);
        Assert.True(CompactLayout.Width(30, 400, 25, 700) >= 525);
        Assert.Equal(700, CompactLayout.Width(10000, 0, 80, 700));
        Assert.InRange(CompactLayout.Width(30, 0, 20, 700), 180, 230);
    }
    [Fact]
    public void SpringPreservesVelocityWhenInterrupted()
    {
        var motion = new MotionValue(190); motion.Target = 744; motion.Advance(.07);
        var velocity = motion.Velocity; var value = motion.Value; motion.Target = 190;
        Assert.Equal(velocity, motion.Velocity); Assert.Equal(value, motion.Value);
        motion.Advance(.0001); Assert.InRange(Math.Abs(motion.Value - value), 0, Math.Abs(velocity) * .0002);
        for (var i = 0; i < 120; i++) motion.Advance(1d / 120);
        Assert.InRange(Math.Abs(motion.Value - 190), 0, .1);
    }
    [Fact]
    public void SpringProducesTheSamePositionAtDifferentRefreshRates()
    {
        var a = new MotionValue(190) { Target = 744 }; var b = new MotionValue(190) { Target = 744 };
        for (var i = 0; i < 60; i++) a.Advance(1d / 60);
        for (var i = 0; i < 144; i++) b.Advance(1d / 144);
        Assert.Equal(a.Value, b.Value, 5); Assert.InRange(Math.Abs(a.Value - 744), 0, .01);
    }
}
