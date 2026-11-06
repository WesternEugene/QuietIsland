namespace QuietIsland.Core;

public sealed record MediaFocusSession(string Id, bool IsPlaying, bool CanPause = true, bool CanResume = true);
public sealed record MediaFocusAction(string Id, bool Pause);

/// <summary>Tracks ownership of pauses, independent of Windows transport APIs.</summary>
public sealed class MediaFocusPolicy
{
    private readonly HashSet<string> owned = [], pending = [], overridden = [];
    private readonly HashSet<string> observedPaused = [];
    private DateTimeOffset? quietSince;
    public bool HasOwnership => owned.Count > 0;
    public IEnumerable<string> OwnedIds => owned;
    public void BeginEpisode() { overridden.Clear(); quietSince = null; }
    public void Cancel(string? id = null)
    {
        if (id == null) { overridden.UnionWith(owned); overridden.UnionWith(pending); owned.Clear(); observedPaused.Clear(); pending.Clear(); }
        else { owned.Remove(id); pending.Remove(id); observedPaused.Remove(id); overridden.Add(id); }
        quietSince = null;
    }
    public void ConfirmPause(string id, bool accepted) { pending.Remove(id); if (accepted) owned.Add(id); else overridden.Add(id); }
    public void ConfirmResume(string id, bool accepted) { if (accepted) { owned.Remove(id); observedPaused.Remove(id); } }
    public IReadOnlyList<MediaFocusAction> Update(IEnumerable<MediaFocusSession> sessions, bool interrupted, bool resume, TimeSpan delay, DateTimeOffset now)
    {
        var current = sessions.ToDictionary(x => x.Id);
        foreach (var id in owned.ToArray())
        {
            if (!current.TryGetValue(id, out var session)) { owned.Remove(id); observedPaused.Remove(id); }
            else if (!session.IsPlaying) observedPaused.Add(id);
            else if (observedPaused.Contains(id)) { owned.Remove(id); observedPaused.Remove(id); overridden.Add(id); }
        }
        if (interrupted)
        {
            quietSince = null;
            var actions = current.Values.Where(x => x.IsPlaying && x.CanPause && !owned.Contains(x.Id) && !pending.Contains(x.Id) && !overridden.Contains(x.Id)).Select(x => new MediaFocusAction(x.Id, true)).ToArray();
            foreach (var action in actions) pending.Add(action.Id);
            return actions;
        }
        pending.Clear(); overridden.Clear();
        if (!resume) { owned.Clear(); observedPaused.Clear(); quietSince = null; return []; }
        quietSince ??= now;
        if (now - quietSince < delay) return [];
        var resumes = owned.Where(id => current.TryGetValue(id, out var session) && !session.IsPlaying && session.CanResume).Select(id => new MediaFocusAction(id, false)).ToArray();
        return resumes;
    }
}
