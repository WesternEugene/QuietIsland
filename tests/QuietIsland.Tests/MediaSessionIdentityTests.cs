using QuietIsland.Core;
using Xunit;

namespace QuietIsland.Tests;

public class MediaSessionIdentityTests
{
    private sealed record Session(string App, Guid Wrapper);
    [Fact]
    public void MusicKeepsIdentityWhenVoiceAppearsAndDisappears()
    {
        var ids = new SessionIdentityRegistry<Session>(x => x.App);
        var original = ids.Refresh([new("Spotify", Guid.NewGuid())]).Single().Id;
        var voice = new Session("Telegram", Guid.NewGuid());
        var withVoice = ids.Refresh([new("Spotify", Guid.NewGuid()), voice]);
        Assert.Equal(original, withVoice.Single(x => x.Session.App == "Spotify").Id);
        var afterVoice = ids.Refresh([new("Spotify", Guid.NewGuid())]);
        Assert.Equal(original, afterVoice.Single().Id);
    }
    [Fact]
    public void IndependentSessionsOfTheSameApplicationAreNotMerged()
    {
        var ids = new SessionIdentityRegistry<Session>(x => x.App);
        var first = new Session("Browser", Guid.NewGuid()); var second = new Session("Browser", Guid.NewGuid());
        var targets = ids.Refresh([first, second]);
        Assert.NotEqual(targets[0].Id, targets[1].Id);
        Assert.Equal(targets[1].Id, ids.Refresh([second]).Single().Id);
    }
}
