using QuietIsland.Core;
using Xunit;

namespace QuietIsland.Tests;

public class ExtendedUnitTests
{
    [Theory]
    [InlineData("1 ярд в сантиметры", "91.44 см")]
    [InlineData("2 миллиграмма в граммы", "0.002 г")]
    [InlineData("8 мегабит в мегабайты", "1 МБ")]
    [InlineData("1 MiB/s to Mbit/s", "8.388608 Mbit/s")]
    [InlineData("1,5 литра в см³", "1500 см³")]
    [InlineData("1 gallon to liters", "3.785411784 L")]
    [InlineData("1 UK gallon to l", "4.54609 L")]
    [InlineData("10000 см2 в квадратные метры", "1 м²")]
    [InlineData("2 гектара в кв. м", "20000 м²")]
    [InlineData("1 square foot to in^2", "144 in²")]
    [InlineData("72 километра в час в м/с", "20 м/с")]
    [InlineData("60 mph to km/h", "96.56064 km/h")]
    [InlineData("90 минут в часы", "1.5 ч")]
    [InlineData("2 ч в с", "7200 с")]
    [InlineData("1 с в мс", "1000 мс")]
    [InlineData("1 атм в кПа", "101.325 кПа")]
    [InlineData("760 torr to atm", "1 atm")]
    [InlineData("1 mPa to MPa", "1E-09 MPa")]
    [InlineData("1 кВт·ч в кДж", "3600 кДж")]
    [InlineData("1 килокалория в джоули", "4184 Дж")]
    [InlineData("1 л. с. в ватт", "735.49875 Вт")]
    [InlineData("1 mW to MW", "1E-09 MW")]
    [InlineData("180 градусов в радианы", "3.141592654 рад")]
    [InlineData("1 kHz to Hz", "1000 Hz")]
    [InlineData("1 кгс в ньютоны", "9.80665 Н")]
    [InlineData("32 degrees Fahrenheit to degrees Celsius", "0 C")]
    public void CommonDimensionsAndCompoundUnitsProduceCalculations(string input, string expected)
    {
        var command = CommandParser.Parse(input);

        Assert.Equal(CommandKind.Calculate, command.Kind);
        Assert.Equal(expected, command.Result);
    }

    [Theory]
    [InlineData("1 м в минуты")]
    [InlineData("1 м² в м³")]
    [InlineData("1 fl oz to oz")]
    [InlineData("1 ватт в джоули")]
    [InlineData("1 kN to km/h")]
    [InlineData("10 smoot to m")]
    [InlineData("1 км/ч в м/с и выход")]
    public void UnknownOrIncompatibleUnitsRemainSearchQueries(string input)
    {
        var command = CommandParser.Parse(input);

        Assert.Equal(CommandKind.Search, command.Kind);
        Assert.Null(command.Result);
    }
}
