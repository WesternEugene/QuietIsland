using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using QuietIsland.Models;

namespace QuietIsland.Services;

public enum LyricsStatus { Empty, Loading, Synced, Plain, Instrumental, NotFound, Unavailable }
public sealed record LyricsLine(double Time, string Text);
public sealed record LyricsDocument(LyricsStatus Status, IReadOnlyList<LyricsLine> Lines, string PlainText)
{
    public static LyricsDocument Empty { get; } = new(LyricsStatus.Empty, Array.Empty<LyricsLine>(), "");
    public int ActiveLineAt(double position)
    {
        if (!double.IsFinite(position)) return -1;
        var low = 0; var high = Lines.Count - 1; var result = -1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (Lines[middle].Time <= position) { result = middle; low = middle + 1; }
            else high = middle - 1;
        }
        return result;
    }
}

/// <summary>One context owns lyrics and the display preference, independently of disposable views.</summary>
public sealed class LyricsService : IDisposable
{
    private const int CacheLimit = 16, TextLimit = 80000, LineLimit = 1200;
    private readonly object gate = new();
    private readonly HttpClient client;
    private readonly SemaphoreSlim requestGate = new(1);
    private readonly Dictionary<TrackKey, LyricsDocument> cache = [];
    private readonly LinkedList<TrackKey> recent = [];
    private Track? track;
    private Request? pending;
    private LyricsDocument current = LyricsDocument.Empty;
    private bool enabled, disposed;
    private DateTimeOffset nextRequest;
    public bool Enabled { get { lock (gate) return enabled; } }
    public LyricsDocument Current { get { lock (gate) return current; } }
    public event Action? Changed;

    public LyricsService(HttpMessageHandler? handler = null)
    {
        client = handler == null ? new HttpClient() : new HttpClient(handler);
        client.Timeout = TimeSpan.FromSeconds(12); client.MaxResponseContentBufferSize = 512 * 1024;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("QuietIsland/1.4.0 (Windows desktop lyrics)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public void UpdateTrack(MediaSnapshot media)
    {
        var next = media.HasMedia && media.IsMusic ? Track.From(media) : null;
        Request? request = null; var changed = false;
        lock (gate)
        {
            if (disposed) return;
            if (!Equals(track?.Key, next?.Key))
            {
                CancelRequest(); track = next;
                current = next != null && cache.TryGetValue(next.Key, out var cached) ? cached : LyricsDocument.Empty;
                if (next != null && cache.ContainsKey(next.Key)) Touch(next.Key);
                changed = true;
            }
            if (enabled && track != null && current.Status == LyricsStatus.Empty && pending == null) { request = PrepareRequest(); changed = true; }
        }
        if (changed) Changed?.Invoke();
        if (request != null) _ = FetchAsync(request);
    }

    public void SetEnabled(bool value)
    {
        Request? request = null;
        lock (gate)
        {
            if (disposed || enabled == value) return;
            enabled = value;
            if (enabled && track != null && current.Status == LyricsStatus.Empty && pending == null) request = PrepareRequest();
        }
        Changed?.Invoke();
        if (request != null) _ = FetchAsync(request);
    }

    public void Retry()
    {
        Request? request;
        lock (gate)
        {
            if (disposed || !enabled || track == null || pending != null) return;
            cache.Remove(track.Key); recent.Remove(track.Key); request = PrepareRequest();
        }
        Changed?.Invoke();
        if (request != null) _ = FetchAsync(request);
    }

    private Request? PrepareRequest()
    {
        if (track!.Artist.Length == 0) { current = new(LyricsStatus.NotFound, Array.Empty<LyricsLine>(), ""); Put(track.Key, current); return null; }
        current = new(LyricsStatus.Loading, Array.Empty<LyricsLine>(), "");
        return pending = new(track, new CancellationTokenSource());
    }

    private async Task FetchAsync(Request request)
    {
        var token = request.Cancellation.Token;
        LyricsDocument? result = null;
        try
        {
            await requestGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                DateTimeOffset allowed; lock (gate) allowed = nextRequest;
                var wait = allowed - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, token).ConfigureAwait(false);
                using var response = await client.GetAsync(request.Track.Uri, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                {
                    var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(30);
                    lock (gate) nextRequest = DateTimeOffset.UtcNow + (delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1)) + TimeSpan.FromMilliseconds(Random.Shared.Next(50, 250));
                    result = new(LyricsStatus.Unavailable, Array.Empty<LyricsLine>(), "");
                }
                else if (response.StatusCode == HttpStatusCode.NotFound) result = new(LyricsStatus.NotFound, Array.Empty<LyricsLine>(), "");
                else if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true) result = new(LyricsStatus.Unavailable, Array.Empty<LyricsLine>(), "");
                else
                {
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
                    var root = json.RootElement;
                    result = ParseLyrics(String(root, "syncedLyrics"), String(root, "plainLyrics"), root.TryGetProperty("instrumental", out var instrumental) && instrumental.ValueKind == JsonValueKind.True);
                }
            }
            finally
            {
                lock (gate) { var interval = DateTimeOffset.UtcNow.AddMilliseconds(250); if (nextRequest < interval) nextRequest = interval; }
                requestGate.Release();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException or IOException or InvalidOperationException)
        { result = new(LyricsStatus.Unavailable, Array.Empty<LyricsLine>(), ""); }
        var changed = false;
        lock (gate)
        {
            if (!disposed && !token.IsCancellationRequested && ReferenceEquals(pending, request) && track?.Key == request.Track.Key)
            {
                current = result ?? new(LyricsStatus.Unavailable, Array.Empty<LyricsLine>(), "");
                Put(request.Track.Key, current); pending = null; changed = true;
            }
        }
        request.Cancellation.Dispose();
        if (changed) Changed?.Invoke();
    }

    internal static LyricsDocument ParseLyrics(string synced, string plain, bool instrumental = false)
    {
        if (instrumental) return new(LyricsStatus.Instrumental, Array.Empty<LyricsLine>(), "");
        synced = Limit(synced); plain = Limit(plain).Trim();
        var lines = new List<LyricsLine>();
        foreach (var raw in synced.Split('\n'))
        {
            var stamps = Regex.Matches(raw, @"\[(\d{1,3}):([0-5]?\d(?:\.\d{1,3})?)\]", RegexOptions.CultureInvariant);
            if (stamps.Count == 0) continue;
            var text = Regex.Replace(raw[(stamps[^1].Index + stamps[^1].Length)..], @"<\d{1,3}:[0-5]?\d(?:\.\d{1,3})?>", "").Trim();
            foreach (Match stamp in stamps)
            {
                var time = int.Parse(stamp.Groups[1].Value, CultureInfo.InvariantCulture) * 60 + double.Parse(stamp.Groups[2].Value, CultureInfo.InvariantCulture);
                lines.Add(new(time, text)); if (lines.Count >= LineLimit) break;
            }
            if (lines.Count >= LineLimit) break;
        }
        if (lines.Any(x => x.Text.Length > 0))
        {
            var ordered = Array.AsReadOnly(lines.OrderBy(x => x.Time).ToArray());
            return new(LyricsStatus.Synced, ordered, plain.Length > 0 ? plain : JoinLines(ordered));
        }
        return new(plain.Length > 0 ? LyricsStatus.Plain : LyricsStatus.NotFound, Array.Empty<LyricsLine>(), plain);
    }

    private static string String(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static string Limit(string value) => value.Length > TextLimit ? value[..TextLimit] : value;
    private static string JoinLines(IEnumerable<LyricsLine> lines)
    {
        var text = new StringBuilder();
        foreach (var line in lines)
        {
            if (text.Length > 0) text.Append('\n');
            var remaining = TextLimit - text.Length;
            text.Append(line.Text.AsSpan(0, Math.Min(line.Text.Length, remaining)));
            if (text.Length >= TextLimit) break;
        }
        return text.ToString();
    }
    private void Touch(TrackKey key) { recent.Remove(key); recent.AddFirst(key); }
    private void Put(TrackKey key, LyricsDocument value)
    {
        cache[key] = value; Touch(key);
        while (cache.Count > CacheLimit) { var oldest = recent.Last!.Value; recent.RemoveLast(); cache.Remove(oldest); }
    }
    private void CancelRequest() { var previous = pending; pending = null; previous?.Cancellation.Cancel(); }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; CancelRequest(); cache.Clear(); recent.Clear(); }
        client.Dispose();
    }
    private readonly record struct TrackKey(string Title, string Artist, string Album, int Duration);
    private sealed record Track(string Title, string Artist, string Album, int Duration)
    {
        public TrackKey Key => new(Title.ToUpperInvariant(), Artist.ToUpperInvariant(), Album.ToUpperInvariant(), Duration);
        public string Uri => "https://lrclib.net/api/get?track_name=" + System.Uri.EscapeDataString(Title) + "&artist_name=" + System.Uri.EscapeDataString(Artist) + (Album.Length > 0 ? "&album_name=" + System.Uri.EscapeDataString(Album) : "") + (Duration > 0 ? "&duration=" + Duration.ToString(CultureInfo.InvariantCulture) : "");
        public static Track From(MediaSnapshot media) => new(media.Title.Trim(), media.Artist.Trim(), media.Album.Trim(), double.IsFinite(media.Duration) && media.Duration is >= 1 and <= 3600 ? (int)Math.Round(media.Duration) : 0);
    }
    private sealed record Request(Track Track, CancellationTokenSource Cancellation);
}
