using System.Net;
using System.Text;
using System.Text.Json;
using QuietIsland.Core;
using Xunit;

namespace QuietIsland.Tests;

public sealed class MusicArtistResolverTests
{
    [Fact]
    public async Task UsesEveryTrackArtistInCreditOrderAndCachesTheResult()
    {
        using var handler = new CatalogHandler(Search(), Page("sakura zxc", ["i wxnt to slxxp", "mad arieri", "MAD ARIERI"]));
        using var client = new HttpClient(handler); using var resolver = new MusicArtistResolver(client);
        Assert.Equal("i wxnt to slxxp, mad arieri", await resolver.ResolveAsync("sakura zxc", "i wxnt to slxxp", "sakura zxc", 132));
        Assert.Equal("i wxnt to slxxp, mad arieri", await resolver.ResolveAsync("sakura zxc", "i wxnt to slxxp", "sakura zxc", 132));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("itunes.apple.com", handler.Requests[0].Host);
        Assert.Equal("https://music.apple.com/us/song/1802269544", handler.Requests[1].AbsoluteUri);
    }

    [Fact]
    public async Task PreservesPunctuationInsideArtistNamesAndIgnoresAlbumCredits()
    {
        using var handler = new CatalogHandler(Search(artist: "Simon & Garfunkel & Earth, Wind & Fire"), Page("sakura zxc", ["Simon & Garfunkel", "Earth, Wind & Fire"]));
        using var client = new HttpClient(handler); using var resolver = new MusicArtistResolver(client);
        Assert.Equal("Simon & Garfunkel, Earth, Wind & Fire", await resolver.ResolveAsync("sakura zxc", "Simon & Garfunkel", "sakura zxc", 132));
    }

    [Theory]
    [InlineData("Simon & Garfunkel")]
    [InlineData("Earth, Wind & Fire")]
    public async Task DoesNotSplitADeclaredBandRepeatedInTheFeaturedTitle(string band)
    {
        using var handler = new CatalogHandler(Search("Song", "Primary & " + band), Page("Song (feat. " + band + ")", ["Primary", band]));
        using var client = new HttpClient(handler); using var resolver = new MusicArtistResolver(client);
        Assert.Equal("Primary, " + band, await resolver.ResolveAsync("Song", "Primary", "Album", 132));
    }

    [Fact]
    public async Task KeepsSeparatorsIntactAroundADeclaredBandAndAdditionalGuests()
    {
        using var handler = new CatalogHandler(Search("Song", "Primary & Simon & Garfunkel"), Page("Song (feat. Guest A, Simon & Garfunkel & Guest B)", ["Primary", "Simon & Garfunkel"]));
        using var client = new HttpClient(handler); using var resolver = new MusicArtistResolver(client);
        Assert.Equal("Primary, Simon & Garfunkel, Guest A, Guest B", await resolver.ResolveAsync("Song", "Primary", "Album", 132));
    }

    [Theory]
    [InlineData("sakura zxc (Slowed)", "i wxnt to slxxp & mad arieri", 132000)]
    [InlineData("sakura zxc", "i wxnt to slxxp & mad arieri", 160000)]
    [InlineData("sakura zxc", "Other artist", 132000)]
    public async Task DoesNotBorrowCreditsFromAnotherRecording(string title, string artist, int duration)
    {
        using var handler = new CatalogHandler(Search(title, artist, duration), Page("sakura zxc", ["i wxnt to slxxp", "mad arieri"]));
        using var client = new HttpClient(handler); using var resolver = new MusicArtistResolver(client);
        Assert.Equal("i wxnt to slxxp", await resolver.ResolveAsync("sakura zxc", "i wxnt to slxxp", "sakura zxc", 132));
        Assert.DoesNotContain(handler.Requests, uri => uri.AbsolutePath.Contains("/song/"));
    }

    public static TheoryData<string, string, string, double, long, string, string[], string> MissingStoreTracks => new()
    {
        { "id", "FORTUNA 812", "ck", 151, 1893072246, "id", ["FORTUNA 812", "Clayt Carter", "LONOWN"], "FORTUNA 812, Clayt Carter, LONOWN" },
        { "Не думаю", "Юпи", "Не думаю", 141, 1749268748, "Не думаю", ["Юпи", "FORTUNA 812"], "Юпи, FORTUNA 812" },
        { "Malo 2.0", "Егор Крид", "Malo 2.0", 326, 6786283077, "Мало 2.0 (feat. OG Buda, Toxi$, Мэйби Бэйби, Baby Cute, Дора, madk1d & тёмный принц)", ["Егор Крид"], "Егор Крид, OG Buda, Toxi$, Мэйби Бэйби, Baby Cute, Дора, madk1d, тёмный принц" }
    };

    [Theory]
    [MemberData(nameof(MissingStoreTracks))]
    public async Task FindsFullCatalogTracksIncludingLocalizedTitlesAndFeaturedArtists(string title, string artist, string album, double duration, long id, string catalogTitle, string[] declaredArtists, string expected)
    {
        var slug = Uri.EscapeDataString(catalogTitle.Replace(' ', '-'));
        var html = $"<a href='https://music.apple.com/us/album/unrelated/10?i=11'></a><a href='https://music.apple.com/us/album/{slug}/123?i={id}'></a>";
        using var handler = new FullCatalogHandler(html, Page(catalogTitle, declaredArtists, duration), id);
        using var client = new HttpClient(handler); using var resolver = new MusicArtistResolver(client);
        Assert.Equal(expected, await resolver.ResolveAsync(title, artist, album, duration));
        Assert.Contains(handler.Requests, uri => uri.AbsolutePath.EndsWith("/search"));
        Assert.DoesNotContain(handler.Requests, uri => uri.AbsolutePath.EndsWith("/song/11"));
    }

    [Fact]
    public async Task FallsBackToNativeCreditsWhenTheCatalogIsUnavailable()
    {
        using var handler = new CatalogHandler("", "", HttpStatusCode.ServiceUnavailable);
        using var client = new HttpClient(handler); using var resolver = new MusicArtistResolver(client);
        Assert.Equal("Primary, Guest", await resolver.ResolveAsync("Song", "Primary; Guest", "Album", 132));
    }

    private static string Search(string title = "sakura zxc", string artist = "i wxnt to slxxp & mad arieri", int duration = 132000)
        => JsonSerializer.Serialize(new { results = new[] { new { kind = "song", trackName = title, artistName = artist, collectionName = "sakura zxc - Single", trackTimeMillis = duration, trackId = 1802269544 } } });
    private static string Page(string title, string[] artists, double duration = 132)
        => "<script type='application/ld+json'>" + JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["@type"] = "MusicComposition", ["name"] = title,
            ["audio"] = new Dictionary<string, object>
            {
                ["@type"] = "MusicRecording", ["name"] = title, ["duration"] = System.Xml.XmlConvert.ToString(TimeSpan.FromSeconds(duration)),
                ["byArtist"] = artists.Select(name => new { name }).ToArray(),
                ["inAlbum"] = new { byArtist = new[] { new { name = "Unrelated album artist" } } }
            }
        }) + "</script>";
    private sealed class FullCatalogHandler(string searchHtml, string page, long id) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!; Requests.Add(uri);
            var body = uri.Host == "itunes.apple.com" ? "{\"results\":[]}" : uri.AbsolutePath.EndsWith("/search") ? searchHtml : uri.AbsolutePath.EndsWith("/song/" + id) ? page : "";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) });
        }
    }
    private sealed class CatalogHandler(string search, string page, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(request.RequestUri!.Host == "itunes.apple.com" ? search : page, Encoding.UTF8, request.RequestUri.Host == "itunes.apple.com" ? "application/json" : "text/html") });
        }
    }
}
