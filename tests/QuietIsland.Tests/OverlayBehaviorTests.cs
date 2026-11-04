using QuietIsland.Core;
using Xunit;

namespace QuietIsland.Tests;
public sealed class OverlayBehaviorTests
{
    [Theory]
    [InlineData("happ")]
    [InlineData("vpn")]
    public void HappCanBeControlledFromSearch(string input) => Assert.Equal(CommandKind.System, CommandParser.Parse(input).Kind);
    [Fact]
    public void PopupAndKeyboardSessionHoldTheIslandOpen()
    {
        Assert.False(OverlayBehavior.ShouldDismiss(true, false, false, true, false, 3000, 220));
        Assert.False(OverlayBehavior.ShouldDismiss(true, false, false, false, true, 3000, 220));
        Assert.False(OverlayBehavior.ShouldDismiss(true, true, false, false, false, 3000, 220));
        Assert.False(OverlayBehavior.ShouldDismiss(true, false, false, false, false, 200, 220));
        Assert.True(OverlayBehavior.ShouldDismiss(true, false, false, false, false, 240, 220));
    }
    [Fact]
    public void SilentMusicProducesNoAutonomousWave()
    {
        foreach (var phase in new[] { 0d, 1, 2, 5, 20 })
            Assert.Equal(0, MusicContour.Sample(.4, phase, 0, 0, 1, 7));
    }
    [Fact]
    public void AudioControlsAmplitudeAcrossTheLowerContour()
    {
        var quiet = Math.Abs(MusicContour.Sample(.4, 1, .02, .02, 1, 7));
        var loud = Math.Abs(MusicContour.Sample(.4, 1, .8, .8, 1, 7));
        Assert.True(loud > quiet * 3);
        Assert.NotEqual(0, MusicContour.Sample(.02, 1, .8, .8, 1, 7));
        Assert.NotEqual(0, MusicContour.Sample(.98, 1, .8, .8, 1, 7));
        Assert.Equal(0, MusicContour.Sample(0, 1, 1, 1, 1, 7));
        Assert.Equal(0, MusicContour.Sample(1, 1, 1, 1, 1, 7));
    }
}
