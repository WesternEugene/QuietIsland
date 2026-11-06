using QuietIsland.Core;
using Xunit;

namespace QuietIsland.Tests;

public class MediaSourceArbiterTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-06T12:00:00Z");
    [Fact]
    public void LatestStartedSourceWinsInBothDirections()
    {
        var arbiter = new MediaSourceArbiter();
        Assert.Equal("spotify", arbiter.Update([new("spotify", true), new("telegram", false)], null, Now));
        Assert.Equal("telegram", arbiter.Update([new("spotify", true), new("telegram", true)], null, Now.AddSeconds(1)));
        Assert.Equal("telegram", arbiter.Update([new("spotify", false), new("telegram", true)], null, Now.AddSeconds(2)));
        Assert.Equal("spotify", arbiter.Update([new("spotify", true), new("telegram", true)], null, Now.AddSeconds(3)));
        Assert.Equal("spotify", arbiter.Update([new("spotify", true), new("telegram", false)], null, Now.AddSeconds(4)));
    }
    [Fact]
    public void VoiceWithoutMediaControlsCanInterruptAndPreviousSourceResumes()
    {
        var arbiter = new MediaSourceArbiter();
        arbiter.Update([new("spotify", true)], null, Now);
        Assert.Equal("voice", arbiter.Update([new("spotify", true), new("voice", true)], null, Now.AddSeconds(1)));
        Assert.Null(arbiter.Update([new("spotify", false), new("voice", false)], null, Now.AddSeconds(2)));
        Assert.Equal("spotify", arbiter.NextToResume(["spotify"]));
    }
    [Fact]
    public void NestedInterruptionsResumeTheMostRecentOwnedSourceFirst()
    {
        var arbiter = new MediaSourceArbiter();
        arbiter.Update([new("spotify", true)], null, Now);
        arbiter.Update([new("spotify", false), new("telegram", true)], null, Now.AddSeconds(1));
        arbiter.Update([new("spotify", false), new("telegram", false), new("browser", true)], null, Now.AddSeconds(2));
        Assert.Null(arbiter.Update([new("spotify", false), new("telegram", false), new("browser", false)], null, Now.AddSeconds(3)));
        Assert.Equal("telegram", arbiter.NextToResume(["spotify", "telegram"]));
        Assert.Equal("spotify", arbiter.NextToResume(["spotify"]));
    }
    [Fact]
    public void AutomaticResumeCannotStealPriorityFromAJustStartedVoice()
    {
        var arbiter = new MediaSourceArbiter();
        arbiter.Update([new("spotify", true)], null, Now);
        arbiter.Update([new("spotify", false)], null, Now.AddSeconds(1));
        arbiter.ExpectResume("spotify", Now.AddSeconds(2));
        Assert.Equal("voice", arbiter.Update([new("spotify", true), new("voice", true)], "spotify", Now.AddSeconds(3)));
    }
    [Fact]
    public void WindowsCurrentSessionBreaksSimultaneousStartupTies()
    {
        var arbiter = new MediaSourceArbiter();
        Assert.Equal("telegram", arbiter.Update([new("telegram", true), new("spotify", true)], "telegram", Now));
    }
}
