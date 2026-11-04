using System.Globalization;
using QuietIsland.Core;
using Xunit;

namespace QuietIsland.Tests;

public class RussianUnitTests
{
    [Theory]
    [InlineData("10 см в дюймы", "3.937007874 дюйма")]
    [InlineData("5 километров в мили", "3.106855961 мили")]
    [InlineData("2 кг в фунты", "4.409245244 фунта")]
    [InlineData("1 дюйм в сантиметры", "2.54 см")]
    [InlineData("1 дюйма в сантиметрах", "2.54 см")]
    [InlineData("12 дюймов в футы", "1 фут")]
    [InlineData("36 дюймов в футах", "3 фута")]
    [InlineData("60 дюймов в фут", "5 футов")]
    [InlineData("1 фут в дюймы", "12 дюймов")]
    [InlineData("1 миля в километры", "1.609344 км")]
    [InlineData("1 мили в метрах", "1609.344 м")]
    [InlineData("1,5 километра в метры", "1500 м")]
    [InlineData("1 миллиметр в сантиметры", "0.1 см")]
    [InlineData("1 сантиметр в миллиметрах", "10 мм")]
    [InlineData("1 метр в сантиметры", "100 см")]
    [InlineData("1000 граммов в килограммы", "1 кг")]
    [InlineData("1000 грамм в килограммах", "1 кг")]
    [InlineData("1 фунт в граммы", "453.59237 г")]
    [InlineData("1 фунта в унции", "16 унций")]
    [InlineData("16 унций в фунты", "1 фунт")]
    [InlineData("1 унция в граммах", "28.34952313 г")]
    [InlineData("  2,54СМ.   В   ДЮЙМЫ  ", "1 дюйм")]
    [InlineData("+1,5 км в м", "1500 м")]
    [InlineData("0,5 дюйма в см", "1.27 см")]
    [InlineData("1 cm в inches", "0.3937007874 дюйма")]
    public void RussianLengthAndMassQueriesProduceCalculationResults(string input, string expected)
    {
        var command = CommandParser.Parse(input);

        Assert.Equal(CommandKind.Calculate, command.Kind);
        Assert.Equal(input.Trim(), command.Text);
        Assert.Equal(expected, command.Result);
    }

    [Theory]
    [InlineData("100 градусов Фаренгейта в Цельсия", "37.77777778 °C")]
    [InlineData("100 градусов по Цельсию в градусы по Фаренгейту", "212 °F")]
    [InlineData("-40 градусов Цельсия в градусы Фаренгейта", "-40 °F")]
    [InlineData("−40 °Ф в °С", "-40 °C")]
    [InlineData("273,15 кельвина в °C", "0 °C")]
    [InlineData("0 °С в Кельвины", "273.15 К")]
    [InlineData("32F в C", "0 °C")]
    [InlineData("32 гр. Фаренгейта в гр. Цельсия", "0 °C")]
    [InlineData("0 градусов цельсия в градусах кельвина", "273.15 К")]
    public void RussianTemperatureQueriesUnderstandScalesAndNegativeValues(string input, string expected)
    {
        var command = CommandParser.Parse(input);

        Assert.Equal(CommandKind.Calculate, command.Kind);
        Assert.Equal(expected, command.Result);
    }

    [Theory]
    [InlineData("1,5МБ в КБ", "1500 КБ")]
    [InlineData("1 гигабайт в мегабайты", "1000 МБ")]
    [InlineData("1 мебибайт в кибибайтах", "1024 КиБ")]
    [InlineData("1024 байта в кибибайты", "1 КиБ")]
    [InlineData("1 гибибайт в миб", "1024 МиБ")]
    public void RussianDataQueriesKeepDecimalAndBinaryUnitsDistinct(string input, string expected) =>
        Assert.Equal(expected, UnitConverter.Convert(input));

    [Theory]
    [InlineData("1 inch to cm", "2.54 cm")]
    [InlineData("1 inch in cm", "2.54 cm")]
    [InlineData("1 lb to g", "453.59237 g")]
    [InlineData("1 MB to KB", "1000 KB")]
    [InlineData("32 f to c", "0 C")]
    [InlineData("0 c to f", "32 F")]
    [InlineData("1 foot to inches", "12 inch")]
    public void EnglishQueriesRetainEnglishResultSymbols(string input, string expected) =>
        Assert.Equal(expected, UnitConverter.Convert(input));

    [Theory]
    [InlineData("ru-RU")]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    public void ParsingAndResultsDoNotDependOnTheCurrentCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

            Assert.Equal("1 дюйм", UnitConverter.Convert("2,54 СМ В ДЮЙМЫ"));
            Assert.Equal("2.54 cm", UnitConverter.Convert("1 INCH TO CM"));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("10 см в кг")]
    [InlineData("10 неизвестных в метры")]
    [InlineData("100 градусов в Цельсия")]
    [InlineData("10 сантиметров в метры и выход")]
    [InlineData("1.2.3 м в см")]
    [InlineData("10 кг в cmd")]
    [InlineData("10 см в дюймы > calc")]
    public void InvalidConversionsRemainSearchQueries(string input)
    {
        var command = CommandParser.Parse(input);

        Assert.Equal(CommandKind.Search, command.Kind);
        Assert.Null(command.Result);
        Assert.Equal(input, command.Text);
    }

    [Theory]
    [InlineData("> 10 см в дюймы", CommandKind.Shell, "10 см в дюймы", null)]
    [InlineData("?10 см в дюймы", CommandKind.Ai, "10 см в дюймы", null)]
    [InlineData("web 10 см в дюймы", CommandKind.Web, "10 см в дюймы", null)]
    [InlineData("1,5 + 2,5", CommandKind.Calculate, "1,5 + 2,5", "4")]
    public void ExistingCommandPrecedenceIsPreserved(string input, CommandKind kind, string text, string? result)
    {
        var command = CommandParser.Parse(input);

        Assert.Equal(kind, command.Kind);
        Assert.Equal(text, command.Text);
        Assert.Equal(result, command.Result);
    }

    [Fact]
    public void NonFiniteNumbersRemainSearchQueriesWithoutThrowing()
    {
        var command = CommandParser.Parse(new string('9', 400) + " м в см");

        Assert.Equal(CommandKind.Search, command.Kind);
        Assert.Null(command.Result);
    }
}
