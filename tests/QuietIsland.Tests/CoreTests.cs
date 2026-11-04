using QuietIsland.Core;
using Xunit;

namespace QuietIsland.Tests;

public class CoreTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void FluidRequiresPlayingMusic(bool music, bool playing, bool expected) =>
        Assert.Equal(expected, AnimationPolicy.ShouldAnimate(music, playing));

    [Theory]
    [InlineData("2 + 3 * 4", "14")]
    [InlineData("(2 + 3) * 4", "20")]
    [InlineData("2^3^2", "512")]
    [InlineData("-2^2", "-4")]
    [InlineData("sqrt(81) + 10%", "9.1")]
    [InlineData("1,5 + 2,5", "4")]
    public void CalculatorUsesArithmeticNotCode(string expression, string expected) =>
        Assert.Equal(expected, Calculator.Evaluate(expression));

    [Theory]
    [InlineData("1/0")]
    [InlineData("Process.Start('cmd')")]
    [InlineData("2 +")]
    [InlineData("sqrt(-1)")]
    public void CalculatorRejectsInvalidOrNonFiniteValues(string expression) =>
        Assert.Null(Calculator.Evaluate(expression));

    [Fact]
    public void TimerRecognizesRussianMinutes()
    {
        var command = CommandParser.Parse("таймер 10 мин");
        Assert.Equal(CommandKind.Timer, command.Kind);
        Assert.Equal(TimeSpan.FromMinutes(10), command.Duration);
    }

    [Fact]
    public void ReminderUnderstandsTomorrowAndRetainsText()
    {
        var now = new DateTimeOffset(2026, 11, 3, 20, 0, 0, TimeSpan.FromHours(5));
        var reminder = ReminderParser.Parse("позвонить маме завтра в 9", now);
        Assert.NotNull(reminder);
        Assert.Equal("позвонить маме", reminder.Title);
        Assert.Equal(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.FromHours(5)), reminder.Due);
    }

    [Fact]
    public void DailyReminderRollsToNextDayAndRecurs()
    {
        var now = new DateTimeOffset(2026, 11, 3, 23, 30, 0, TimeSpan.FromHours(5));
        var reminder = ReminderParser.Parse("таблетки каждый день в 23:00", now)!;
        Assert.True(reminder.Daily);
        Assert.Equal(4, reminder.Due.Day);
        Assert.Equal(5, reminder.NextAfter(reminder.Due).Day);
    }

    [Fact]
    public void RelativeReminderKeepsActionAfterTime()
    {
        var now = DateTimeOffset.Parse("2026-11-03T12:00:00+05:00");
        var reminder = ReminderParser.Parse("через 20 минут выключить плиту", now)!;
        Assert.Equal("выключить плиту", reminder.Title);
        Assert.Equal(now.AddMinutes(20), reminder.Due);
    }

    [Fact]
    public void UnitConversionHasRealValues() => Assert.Equal("2.54 cm", UnitConverter.Convert("1 inch to cm"));

    [Fact]
    public void EverythingSyntaxPassesThrough() => Assert.Equal("ext:pdf path:D:\\Projects", CommandParser.Parse("ext:pdf path:D:\\Projects").Text);

    [Fact]
    public void StateRecoversPreviousSaveWhenMainIsCorrupt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "quiet-island-test-" + Guid.NewGuid());
        try
        {
            var store = new StateStore<TestState>(directory);
            store.Save(new TestState("first"));
            store.Save(new TestState("second"));
            File.WriteAllText(Path.Combine(directory, "state.json"), "{broken");
            Assert.Equal("first", store.Load().Value);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    public record TestState(string Value = "default");
}
