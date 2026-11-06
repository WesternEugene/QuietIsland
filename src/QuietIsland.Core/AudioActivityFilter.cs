namespace QuietIsland.Core;

/// <summary>Ignores brief native sounds, then retains sustained media across quiet word gaps.</summary>
public sealed class AudioActivityFilter
{
    private static readonly TimeSpan MinimumDuration = TimeSpan.FromMilliseconds(1800);
    private static readonly TimeSpan MaximumSampleGap = TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan ReleaseDelay = TimeSpan.FromMilliseconds(900);
    private sealed class Activity(DateTimeOffset now)
    {
        public DateTimeOffset Started { get; } = now;
        public DateTimeOffset LastHeard { get; set; } = now;
        public bool Qualified { get; set; }
    }
    private readonly Dictionary<string, Activity> activity = [];

    public IReadOnlySet<string> Update(IEnumerable<string> audibleIds, DateTimeOffset now)
    {
        var heard = audibleIds.ToHashSet();
        foreach (var (id, sound) in activity.ToArray())
        {
            var gap = now - sound.LastHeard;
            // A notification's release tail must never count toward qualification.
            // Missing samples also cannot establish continuous playback.
            if (gap < TimeSpan.Zero || gap >= ReleaseDelay ||
                !sound.Qualified && (!heard.Contains(id) || gap > MaximumSampleGap)) activity.Remove(id);
        }
        foreach (var id in heard)
        {
            if (!activity.TryGetValue(id, out var sound)) activity[id] = sound = new Activity(now);
            sound.LastHeard = now;
            sound.Qualified |= now - sound.Started >= MinimumDuration;
        }
        return activity.Where(x => x.Value.Qualified).Select(x => x.Key).ToHashSet();
    }

    public void Reset() => activity.Clear();
}
