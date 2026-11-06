using QuietIsland.Core;
using Xunit;

namespace QuietIsland.Tests;

public class AudioActivityFilterTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-06T12:00:00Z");

    [Fact]
    public void BriefNotificationBurstsNeverAcquireFocus()
    {
        var filter = new AudioActivityFilter();
        for (var ms = 0; ms <= 3000; ms += 300)
        {
            // Even several notifications must not build up the release hold
            // into an apparent voice message.
            var sound = ms is 0 or 300 or 600 or 1500 or 1800;
            Assert.Empty(filter.Update(sound ? ["telegram"] : [], Now.AddMilliseconds(ms)));
        }
    }

    [Fact]
    public void SustainedVoiceKeepsFocusAcrossWordGapsAndReleasesAfterItsEnd()
    {
        var filter = new AudioActivityFilter();
        for (var ms = 0; ms < 1800; ms += 300)
            Assert.Empty(filter.Update(["telegram"], Now.AddMilliseconds(ms)));
        Assert.Contains("telegram", filter.Update(["telegram"], Now.AddMilliseconds(1800)));
        Assert.Contains("telegram", filter.Update([], Now.AddMilliseconds(2100)));
        Assert.Contains("telegram", filter.Update(["telegram"], Now.AddMilliseconds(2400)));
        Assert.Contains("telegram", filter.Update([], Now.AddMilliseconds(2700)));
        Assert.Contains("telegram", filter.Update([], Now.AddMilliseconds(3000)));
        Assert.Empty(filter.Update([], Now.AddMilliseconds(3300)));
        Assert.Empty(filter.Update(["telegram"], Now.AddMilliseconds(3600)));
        Assert.Empty(filter.Update([], Now.AddMilliseconds(3900)));
    }

    [Fact]
    public void PollingGapCannotTurnTwoIsolatedSoundsIntoSustainedMedia()
    {
        var filter = new AudioActivityFilter();
        Assert.Empty(filter.Update(["telegram"], Now));
        Assert.Empty(filter.Update(["telegram"], Now.AddSeconds(3)));
        Assert.Empty(filter.Update([], Now.AddMilliseconds(3300)));
    }
}
