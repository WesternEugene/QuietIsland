using QuietIsland.Core;
using Xunit;

namespace QuietIsland.Tests;

public class ToolsTests
{
    [Fact] public void KeyboardLayoutIsReversible() => Assert.Equal("Привет, мир!", TextTransforms.FixLayout("Ghbdtn? vbh!"));
    [Fact] public void Base64RoundTripsCyrillic() => Assert.Equal("Привет", TextTransforms.Apply("base64-decode", TextTransforms.Apply("base64", "Привет")));
    [Fact] public void JsonFormattingRejectsBrokenInput() => Assert.ThrowsAny<Exception>(() => TextTransforms.Apply("json", "{broken"));
    [Fact] public async Task AgentCannotApproveItself()
    {
        var broker = new AgentBroker();
        var request = new AgentRequest("test", "Claude Code", "Команда", "git status", ["allow", "deny"]);
        var task = broker.AskAsync(request, TimeSpan.FromSeconds(2));
        Assert.False(task.IsCompleted);
        Assert.False(broker.Answer("test", "unknown"));
        Assert.True(broker.Answer("test", "deny"));
        Assert.Equal("deny", await task);
    }
    [Fact] public async Task UnansweredAgentRequestTimesOutDenied()
    {
        var broker = new AgentBroker();
        Assert.Equal("deny", await broker.AskAsync(new("timeout", "Codex", "Approval", "test", ["allow", "deny"]), TimeSpan.FromMilliseconds(30)));
        Assert.Empty(broker.Pending);
    }
    [Fact] public async Task FreeTextRequiresExplicitQuestionFlag()
    {
        var broker = new AgentBroker();
        var task = broker.AskAsync(new("question", "Codex", "Branch", "Which branch?", ["main", "deny"], AllowText: true), TimeSpan.FromSeconds(2));
        Assert.True(broker.Answer("question", "feature/new-branch"));
        Assert.Equal("feature/new-branch", await task);
    }
}
