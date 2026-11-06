using QuietIsland.Core;
using Xunit;

namespace QuietIsland.Tests;

public class MediaFocusTests
{
    [Fact]
    public void ChangedPauseEligibilityDoesNotDiscardAnOwnedPause()
    {
        var policy = new MediaFocusPolicy(); var now = DateTimeOffset.UtcNow;
        Assert.Single(policy.Update([new("telegram", true, true)], true, true, TimeSpan.Zero, now));
        policy.ConfirmPause("telegram", true);
        Assert.Equal([new MediaFocusAction("telegram", false)], policy.Update([new("telegram", false, false)], false, true, TimeSpan.Zero, now.AddSeconds(1)));
        Assert.Empty(policy.Update([new("telegram", true, false)], true, true, TimeSpan.Zero, now.AddSeconds(2)));
    }
    [Fact]
    public void ResumeOnlyOwnedPausesAfterDelay()
    {
        var policy = new MediaFocusPolicy(); var now = DateTimeOffset.UtcNow;
        Assert.Equal([new MediaFocusAction("music", true)], policy.Update([new("music", true), new("already-paused", false)], true, true, TimeSpan.FromSeconds(1), now));
        policy.ConfirmPause("music", true);
        Assert.Empty(policy.Update([new("music", false), new("already-paused", false)], false, true, TimeSpan.FromSeconds(1), now));
        Assert.Equal([new MediaFocusAction("music", false)], policy.Update([new("music", false)], false, true, TimeSpan.FromSeconds(1), now.AddSeconds(2)));
    }
    [Fact]
    public void ManualControlAndFailedPauseNeverResume()
    {
        var policy = new MediaFocusPolicy(); var now = DateTimeOffset.UtcNow;
        policy.Update([new("music", true)], true, true, TimeSpan.Zero, now); policy.ConfirmPause("music", false);
        Assert.Empty(policy.Update([new("music", false)], false, true, TimeSpan.Zero, now));
        policy.Update([new("music", true)], true, true, TimeSpan.Zero, now); policy.ConfirmPause("music", true); policy.Cancel();
        Assert.Empty(policy.Update([new("music", false)], false, true, TimeSpan.Zero, now));
    }
    [Fact]
    public void OverlappingInterruptionsHoldPauseAndDisabledResumeClearsOwnership()
    {
        var policy = new MediaFocusPolicy(); var now = DateTimeOffset.UtcNow;
        policy.Update([new("music", true)], true, true, TimeSpan.Zero, now); policy.ConfirmPause("music", true);
        Assert.Empty(policy.Update([new("music", false)], true, true, TimeSpan.Zero, now.AddSeconds(10)));
        Assert.Empty(policy.Update([new("music", false)], false, false, TimeSpan.Zero, now.AddSeconds(20)));
        Assert.Empty(policy.Update([new("music", false)], false, true, TimeSpan.Zero, now.AddSeconds(30)));
    }
    [Fact]
    public void RemovedSessionOrUserResumeIsNeverTakenOverAgain()
    {
        var policy = new MediaFocusPolicy(); var now = DateTimeOffset.UtcNow;
        policy.Update([new("music", true)], true, true, TimeSpan.Zero, now); policy.ConfirmPause("music", true);
        Assert.Empty(policy.Update([new("music", true)], true, true, TimeSpan.Zero, now.AddSeconds(1)));
        Assert.Empty(policy.Update([], false, true, TimeSpan.Zero, now.AddSeconds(2)));
    }
}
