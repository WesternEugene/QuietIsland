namespace QuietIsland.Core;

/// <summary>Windows may recreate managed wrappers when another media session closes.</summary>
public sealed class SessionIdentityRegistry<T>(Func<T, string> application) where T : class
{
    private readonly Dictionary<T, string> references = [];
    private Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
    private long sequence;
    public IReadOnlyList<T> Sessions { get; private set; } = [];
    public IReadOnlyList<(string Id, T Session)> Refresh(IReadOnlyList<T> sessions)
    {
        counts = sessions.GroupBy(application, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
        var present = sessions.ToHashSet();
        foreach (var old in references.Keys.Where(x => !present.Contains(x)).ToArray()) references.Remove(old);
        Sessions = sessions;
        return sessions.Select(x => (GetId(x), x)).ToArray();
    }
    public string GetId(T session)
    {
        if (references.TryGetValue(session, out var id)) return id;
        var app = application(session);
        // A single session can be rebound safely by its Windows application ID.
        // Multiple sessions of one app retain separate identities instead.
        id = counts.GetValueOrDefault(app) <= 1 ? "app:" + app.ToUpperInvariant() : "session:" + ++sequence;
        references[session] = id; return id;
    }
}
