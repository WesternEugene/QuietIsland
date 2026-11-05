using QuietIsland.Core;
using Xunit;

namespace QuietIsland.Tests;

public sealed class ReminderTimeOfDayTests
{
    [Theory]
    [InlineData("позвонить в 9 вечера", 21, 0)]
    [InlineData("позвонить в 9:30 вечера", 21, 30)]
    [InlineData("позвонить в 9 ВЕЧЕРА", 21, 0)]
    [InlineData("позвонить 9 вечера", 21, 0)]
    [InlineData("позвонить в 3 дня", 15, 0)]
    [InlineData("позвонить в 12 дня", 12, 0)]
    [InlineData("позвонить в 9 утра", 9, 0)]
    [InlineData("позвонить в 3 ночи", 3, 0)]
    [InlineData("позвонить в 12 ночи", 0, 0)]
    [InlineData("позвонить в 6 ночи", 6, 0)]
    [InlineData("позвонить в 9 ночи", 21, 0)]
    [InlineData("позвонить в 11 ночи", 23, 0)]
    [InlineData("позвонить в 10:30 ночи", 22, 30)]
    [InlineData("позвонить в 12 вечера", 0, 0)]
    [InlineData("позвонить в 21:15 вечера", 21, 15)]
    public void TimeOfDaySetsHourAndIsRemovedFromTitle(string input, int hour, int minute)
    {
        var now = new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.FromHours(5));

        var reminder = ReminderParser.Parse(input, now);

        Assert.NotNull(reminder);
        Assert.Equal("позвонить", reminder.Title);
        var day = hour == 0 && minute == 0 ? 6 : 5;
        Assert.Equal(new DateTimeOffset(2026, 11, day, hour, minute, 0, now.Offset), reminder.Due);
    }

    [Fact]
    public void EveningIsStillTodayWhenMorningHasAlreadyPassed()
    {
        var now = new DateTimeOffset(2026, 11, 5, 18, 0, 0, TimeSpan.FromHours(5));

        var reminder = ReminderParser.Parse("позвонить в 9 вечера", now);

        Assert.NotNull(reminder);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 21, 0, 0, now.Offset), reminder.Due);
    }

    [Fact]
    public void TomorrowAndDailyRecurrenceUseTheConvertedHour()
    {
        var now = new DateTimeOffset(2026, 11, 5, 22, 0, 0, TimeSpan.FromHours(5));

        var reminder = ReminderParser.Parse("таблетки завтра каждый день в 9 вечера", now);

        Assert.NotNull(reminder);
        Assert.True(reminder.Daily);
        Assert.Equal("таблетки", reminder.Title);
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 21, 0, 0, now.Offset), reminder.Due);
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 21, 0, 0, now.Offset), reminder.NextAfter(reminder.Due));
    }
}
