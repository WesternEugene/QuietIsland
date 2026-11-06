namespace QuietIsland.Core;

public sealed record MediaSourceState(string Id, bool IsActive, bool? TransportPlaying = null);

/// <summary>Latest explicit playback start wins; own resumes do not become new user starts.</summary>
public sealed class MediaSourceArbiter
{
    private readonly Dictionary<string, bool> previous = [];
    private readonly Dictionary<string, bool> previousTransport = [];
    private readonly List<string> recent = [];
    private readonly Dictionary<string, DateTimeOffset> automatic = [];
    public void Reset() { previous.Clear(); previousTransport.Clear(); recent.Clear(); automatic.Clear(); }
    public void ExpectResume(string id, DateTimeOffset now) => automatic[id] = now.AddSeconds(3);
    public string? Update(IEnumerable<MediaSourceState> sources, string? currentSourceId, DateTimeOffset now)
    {
        var current = sources.ToDictionary(x => x.Id);
        foreach (var id in previous.Keys.Where(x => !current.ContainsKey(x)).ToArray()) { previous.Remove(id); previousTransport.Remove(id); recent.Remove(id); automatic.Remove(id); }
        foreach (var id in automatic.Where(x => x.Value <= now).Select(x => x.Key).ToArray()) automatic.Remove(id);
        var started = current.Values.Where(x => x.IsActive && (!previous.GetValueOrDefault(x.Id) || x.TransportPlaying == true && !previousTransport.GetValueOrDefault(x.Id))).OrderBy(x => x.Id == currentSourceId ? 1 : 0).ToArray();
        foreach (var source in started)
        {
            if (automatic.Remove(source.Id) && recent.Contains(source.Id)) continue;
            recent.Remove(source.Id); recent.Add(source.Id);
        }
        foreach (var source in current.Values) { previous[source.Id] = source.IsActive; if (source.TransportPlaying.HasValue) previousTransport[source.Id] = source.TransportPlaying.Value; }
        return recent.LastOrDefault(id => current.TryGetValue(id, out var source) && source.IsActive);
    }
    public string? NextToResume(IEnumerable<string> owned)
    {
        var ids = owned.ToHashSet();
        return recent.LastOrDefault(ids.Contains) ?? ids.FirstOrDefault();
    }
}
