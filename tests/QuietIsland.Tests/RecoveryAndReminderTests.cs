using System.Text.Json;
using QuietIsland.Core;
using Xunit;

namespace QuietIsland.Tests;
public sealed class RecoveryAndReminderTests
{
    [Theory]
    [InlineData("встреча каждую пятницу в 9", DayOfWeek.Friday, 6)]
    [InlineData("встреча по средам в 9", DayOfWeek.Wednesday, 4)]
    [InlineData("встреча каждую субботу в 9", DayOfWeek.Saturday, 7)]
    public void RussianWeekdayFormsCreateCorrectRecurrence(string input, DayOfWeek weekday, int day)
    {
        var now = DateTimeOffset.Parse("2026-11-03T20:00:00+05:00");
        var reminder = ReminderParser.Parse(input, now)!;
        Assert.Equal("встреча", reminder.Title);
        Assert.Equal(day, reminder.Due.Day);
        Assert.Contains(weekday, reminder.Weekdays);
    }
    [Fact] public void UnknownRepeatRuleIsRejected() => Assert.Null(ReminderParser.Parse("встреча каждую неизвестность в 9", DateTimeOffset.Parse("2026-11-03T20:00:00+05:00")));
    [Fact]
    public void CorruptOriginalSurvivesRecoveryAndSave()
    {
        InTemporaryDirectory(directory =>
        {
            var store = new StateStore<CoreTests.TestState>(directory);
            store.Save(new("first")); store.Save(new("second"));
            File.WriteAllText(Path.Combine(directory, "state.json"), "{broken, user data");
            var state = store.Load(); store.Save(state);
            Assert.Contains(Directory.GetFiles(directory, "state.corrupt.*.json"), file => File.ReadAllText(file) == "{broken, user data");
        });
    }
    [Fact]
    public void WrongTypedJsonCannotReplaceGoodBackup()
    {
        InTemporaryDirectory(directory =>
        {
            var store = new StateStore<CoreTests.TestState>(directory);
            store.Save(new("first")); store.Save(new("second"));
            File.WriteAllText(Path.Combine(directory, "state.json"), "{\"Value\":{\"wrong\":true}}");
            Assert.Equal("first", store.Load().Value);
            store.Save(new("third"));
            var backup = JsonSerializer.Deserialize<CoreTests.TestState>(File.ReadAllText(Path.Combine(directory, "state.backup.json")));
            Assert.Equal("first", backup!.Value);
        });
    }
    private static void InTemporaryDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "island-recovery-" + Guid.NewGuid());
        try { action(directory); } finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
