using QuietIsland.Core;
using Xunit;

namespace QuietIsland.Tests;

public sealed class WindowsSettingsSearchTests
{
    [Theory]
    [InlineData("s", "")]
    [InlineData(" S ", "")]
    [InlineData("s экран", "экран")]
    [InlineData("S   Windows Update", "Windows Update")]
    [InlineData("s\tbluetooth", "bluetooth")]
    public void LatinSPrefixSearchesSettings(string input, string query)
    {
        var command = CommandParser.Parse(input);
        Assert.Equal("SettingsSearch", command.Kind.ToString());
        Assert.Equal(query, command.Text);
    }

    [Theory]
    [InlineData("spotify")]
    [InlineData("settings.json")]
    [InlineData("screenshot")]
    [InlineData("с экран")]
    public void NormalNamesAndCyrillicSStayInFileAndAppSearch(string input)
    {
        Assert.Equal(CommandKind.Search, CommandParser.Parse(input).Kind);
    }
}
