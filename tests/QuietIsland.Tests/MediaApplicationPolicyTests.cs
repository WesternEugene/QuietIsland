using QuietIsland.Core;
using Xunit;

namespace QuietIsland.Tests;

public sealed class MediaApplicationPolicyTests
{
    [Theory]
    [InlineData("chrome.exe")]
    [InlineData("Chrome.Default")]
    [InlineData("MSEdge")]
    [InlineData("Microsoft.MicrosoftEdge_8wekyb3d8bbwe!MicrosoftEdge")]
    [InlineData("firefox")]
    [InlineData("308046B0AF4A39CB")]
    [InlineData("Mozilla.Firefox_n80bbvh6b1yt2!App")]
    [InlineData("opera.exe")]
    [InlineData("Brave")]
    [InlineData("Vivaldi")]
    [InlineData("browser.exe")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify")]
    [InlineData("SpotifyAB.SpotifyMusic!Spotify")]
    [InlineData("Spotify.exe|SpotifyAB.SpotifyMusic!Spotify")]
    [InlineData("VLC.exe")]
    [InlineData("wmplayer")]
    [InlineData("Music.UI")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic")]
    [InlineData("Microsoft.ZuneVideo_8wekyb3d8bbwe!Microsoft.ZuneVideo")]
    [InlineData("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App")]
    [InlineData("foobar2000.exe")]
    [InlineData("mpc-hc64.exe")]
    [InlineData("mpv.exe")]
    [InlineData(@"C:\Program Files\VideoLAN\VLC\vlc.exe")]
    [InlineData("Telegram.exe")]
    [InlineData("TelegramMessengerLLP.TelegramDesktop_t4vj0pshhgkwm!Telegram.TelegramDesktop")]
    public void RecognizesPlayerAndBrowserIdentities(string app) => Assert.True(MediaApplicationPolicy.IsMediaApplication(app));

    [Theory]
    [InlineData("")]
    [InlineData("Discord")]
    [InlineData("Discord.exe")]
    [InlineData("DiscordCanary")]
    [InlineData("DiscordPTB")]
    [InlineData("Discord.exe|Chrome")]
    [InlineData("Game.exe|Spotify")]
    [InlineData("obs-browser-page")]
    [InlineData("obs64.exe")]
    [InlineData("Teams.exe")]
    [InlineData("cs2.exe")]
    [InlineData("Minecraft.Windows_8wekyb3d8bbwe!App")]
    [InlineData("javaw.exe")]
    [InlineData("Steam.exe")]
    [InlineData("UnknownApplication.exe")]
    [InlineData("SpotifyGame.exe")]
    [InlineData("not-chrome.exe")]
    [InlineData("MusicGame.exe")]
    [InlineData("ChromeGame")]
    public void DoesNotTreatChatGamesOrNameSubstringsAsMediaApplications(string app) => Assert.False(MediaApplicationPolicy.IsMediaApplication(app));

    [Theory]
    [InlineData("Telegram")]
    [InlineData("Telegram.exe")]
    [InlineData("Telegram.TelegramDesktop")]
    [InlineData("Telegram.TelegramDesktop.Store")]
    [InlineData("Telegram.TelegramDesktop.1234ABCD")]
    [InlineData("TelegramMessengerLLP.TelegramDesktop_t4vj0pshhgkwm!Telegram.TelegramDesktop")]
    public void TelegramRequiresTransportPlaybackInsteadOfProcessAudio(string app)
    {
        Assert.True(MediaApplicationPolicy.IsTelegram(app));
        Assert.False(MediaApplicationPolicy.AllowsNativeAudio(app));
    }
}
