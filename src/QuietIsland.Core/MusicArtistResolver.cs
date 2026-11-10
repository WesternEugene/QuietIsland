using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace QuietIsland.Core;

public sealed class MusicArtistResolver : IDisposable
{
    private static readonly Regex StructuredData = new("""<script\b[^>]*\btype\s*=\s*["']application/ld\+json["'][^>]*>(.*?)</script>""", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex SongLinks = new("""\bhref\s*=\s*["'](https://music\.apple\.com/[a-z]{2}/(?:album|song)/[^"']+)["']""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Featured = new(@"\s*[\(\[]\s*(?:feat(?:uring)?\.?|ft\.?)\s+(?<artists>.+?)[\)\]]\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly SemaphoreSlim requests = new(1, 1);
    private readonly Dictionary<TrackKey, string> cache = [];
    private readonly Queue<TrackKey> recent = [];
    private DateTimeOffset nextSearch;
    private volatile bool disposed;
    public MusicArtistResolver(HttpClient? client = null)
    {
        ownsClient = client == null;
        this.client = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = 2_000_000 };
    }
    public static string FormatProvided(string artist) => string.Join(", ", artist.Split([';', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    public async Task<string> ResolveAsync(string title, string artist, string album, double duration, CancellationToken cancellationToken = default)
    {
        var fallback = FormatProvided(artist);
        if (disposed || string.IsNullOrWhiteSpace(title) || fallback.Length == 0 || !double.IsFinite(duration) || duration <= 0) return fallback;
        var key = new TrackKey(title.Trim(), artist.Trim(), album.Trim(), Math.Round(duration, 1));
        lock (cache) if (cache.TryGetValue(key, out var found)) return found;
        var entered = false;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(12)); cancellationToken = budget.Token;
        try
        {
            await requests.WaitAsync(cancellationToken).ConfigureAwait(false); entered = true;
            lock (cache) if (cache.TryGetValue(key, out var found)) return found;
            var delay = nextSearch - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            nextSearch = DateTimeOffset.UtcNow.AddSeconds(3);
            var uri = "https://itunes.apple.com/search?media=music&entity=song&country=US&limit=20&term=" + Uri.EscapeDataString(title + " " + artist);
            long? id = null;
            try
            {
                using var search = JsonDocument.Parse(await client.GetStringAsync(uri, cancellationToken).ConfigureAwait(false));
                id = FindRecording(search.RootElement, key);
            }
            catch (Exception error) when (error is HttpRequestException or JsonException) { }
            var names = id is { } knownId ? await ReadCatalogArtistsAsync(knownId, key, cancellationToken).ConfigureAwait(false) : [];
            if (names.Length == 0)
            {
                // The iTunes Store index omits streaming-only releases. Search the full music catalog too.
                var html = await client.GetStringAsync("https://music.apple.com/us/search?term=" + Uri.EscapeDataString(title + " " + artist), cancellationToken).ConfigureAwait(false);
                foreach (var candidate in FindCatalogRecordings(html, key).Where(candidate => candidate != id))
                {
                    names = await ReadCatalogArtistsAsync(candidate, key, cancellationToken).ConfigureAwait(false);
                    if (names.Length > 0) break;
                }
            }
            if (names.Length == 0) return fallback;
            cancellationToken.ThrowIfCancellationRequested();
            var result = string.Join(", ", names);
            lock (cache)
            {
                if (!cache.ContainsKey(key)) recent.Enqueue(key);
                cache[key] = result;
                while (recent.Count > 32) cache.Remove(recent.Dequeue());
            }
            return result;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException or IOException or RegexMatchTimeoutException || error is ObjectDisposedException && disposed)
        { return fallback; }
        finally { if (entered) requests.Release(); }
    }
    private async Task<string[]> ReadCatalogArtistsAsync(long id, TrackKey track, CancellationToken cancellationToken)
    {
        var locales = track.Artist.Any(c => c is >= '\u0400' and <= '\u04ff') ? new[] { "ru", "us" } : ["us", "ru"];
        foreach (var locale in locales)
        {
            try
            {
                var page = await client.GetStringAsync("https://music.apple.com/" + locale + "/song/" + id, cancellationToken).ConfigureAwait(false);
                var names = ReadArtists(page, track);
                if (names.Length > 0) return names;
            }
            catch (HttpRequestException) { }
        }
        return [];
    }
    private static IEnumerable<long> FindCatalogRecordings(string html, TrackKey track)
    {
        var candidates = new List<(long Id, bool SameTitle)>();
        foreach (Match link in SongLinks.Matches(html))
        {
            if (!Uri.TryCreate(WebUtility.HtmlDecode(link.Groups[1].Value), UriKind.Absolute, out var uri)) continue;
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 4) continue;
            var parameter = Regex.Match(uri.Query, @"(?:^\?|&)i=(\d+)").Groups[1].Value;
            if (!long.TryParse(parameter.Length > 0 ? parameter : segments[^1], out var id) || id <= 0) continue;
            var slug = Uri.UnescapeDataString(segments[^2]).Replace('-', ' ');
            slug = Regex.Split(slug, @"\s*[\(\[]?\s+feat(?:uring)?\.?\s+", RegexOptions.IgnoreCase)[0];
            candidates.Add((id, SameTitle(slug, track.Title)));
        }
        return candidates.OrderByDescending(candidate => candidate.SameTitle).Select(candidate => candidate.Id).Distinct().Take(6);
    }
    private static long? FindRecording(JsonElement json, TrackKey track)
    {
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return null;
        var candidates = results.EnumerateArray().Where(item => Text(item, "kind") == "song"
            && SameTitle(Text(item, "trackName"), track.Title)
            && Text(item, "artistName").Contains(track.Artist, StringComparison.OrdinalIgnoreCase)
            && item.TryGetProperty("trackTimeMillis", out var time) && time.ValueKind == JsonValueKind.Number && time.TryGetDouble(out var milliseconds) && Math.Abs(milliseconds / 1000 - track.Duration) <= 3
            && item.TryGetProperty("trackId", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var number) && number > 0).ToArray();
        var album = Normalize(track.Album);
        var sameAlbum = candidates.Where(item => Normalize(Text(item, "collectionName")) is var value && (value == album || value == album + " SINGLE" || value == album + " EP")).ToArray();
        if (sameAlbum.Length > 0) candidates = sameAlbum;
        if (candidates.Length == 0 || candidates.Select(item => Normalize(Text(item, "artistName"))).Distinct().Count() != 1) return null;
        return candidates[0].GetProperty("trackId").GetInt64();
    }
    private static string[] ReadArtists(string html, TrackKey track)
    {
        foreach (Match script in StructuredData.Matches(html))
        {
            try
            {
                using var document = JsonDocument.Parse(script.Groups[1].Value);
                foreach (var recording in Recordings(document.RootElement))
                {
                    var title = Text(recording, "name");
                    if (!SameTitle(title, track.Title)) continue;
                    var duration = Text(recording, "duration");
                    if (duration.Length == 0 || Math.Abs(XmlConvert.ToTimeSpan(duration).TotalSeconds - track.Duration) > 3) continue;
                    if (!recording.TryGetProperty("byArtist", out var credits)) continue;
                    var artists = credits.ValueKind == JsonValueKind.Array ? credits.EnumerateArray().ToArray() : [credits];
                    var names = artists.Select(item => WebUtility.HtmlDecode(Text(item, "name")).Trim()).Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    if (names.Any(name => Normalize(name) == Normalize(track.Artist)))
                    {
                        return names.Concat(FeaturedGuests(title, names)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    }
                }
            }
            catch (Exception error) when (error is JsonException or FormatException) { }
        }
        return [];
    }
    private static IEnumerable<string> FeaturedGuests(string title, string[] declaredArtists)
    {
        var featured = Featured.Match(title);
        if (!featured.Success) return [];
        var remaining = WebUtility.HtmlDecode(featured.Groups["artists"].Value);
        const string declared = "\uE000credited-artist\uE001";
        // Structured artist names are atomic, including commas and ampersands inside band names.
        foreach (var name in declaredArtists.OrderByDescending(name => name.Length))
            remaining = Regex.Replace(remaining, $@"(^|,\s*|\s+&\s+){Regex.Escape(name)}(?=\s*(?:,|&|$))", "$1" + declared, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return Regex.Split(remaining, @",\s*|\s+&\s+").Select(name => name.Trim()).Where(name => name.Length > 0 && name != declared);
    }
    private static IEnumerable<JsonElement> Recordings(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray()) foreach (var recording in Recordings(item)) yield return recording;
        }
        else if (node.ValueKind == JsonValueKind.Object)
        {
            if (Text(node, "@type") == "MusicRecording") yield return node;
            foreach (var child in new[] { "audio", "@graph" })
                if (node.TryGetProperty(child, out var value)) foreach (var recording in Recordings(value)) yield return recording;
        }
    }
    private static string Normalize(string value) => Regex.Replace(value.Normalize(NormalizationForm.FormKC).ToUpperInvariant(), @"[\s\p{P}]+", " ").Trim();
    private static bool SameTitle(string left, string right) => TitleKey(left) == TitleKey(right);
    private static string TitleKey(string value)
    {
        value = Normalize(Featured.Replace(value, ""));
        const string cyrillic = "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ";
        string[] latin = ["A", "B", "V", "G", "D", "E", "E", "ZH", "Z", "I", "Y", "K", "L", "M", "N", "O", "P", "R", "S", "T", "U", "F", "KH", "TS", "CH", "SH", "SHCH", "", "Y", "", "E", "YU", "YA"];
        var result = new StringBuilder(value.Length);
        foreach (var letter in value) { var index = cyrillic.IndexOf(letter); if (index >= 0) result.Append(latin[index]); else result.Append(letter); }
        return result.ToString();
    }
    private static string Text(JsonElement node, string property) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private readonly record struct TrackKey(string Title, string Artist, string Album, double Duration);
    public void Dispose() { disposed = true; if (ownsClient) client.Dispose(); }
}
