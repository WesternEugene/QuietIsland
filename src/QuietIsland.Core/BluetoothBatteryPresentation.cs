namespace QuietIsland.Core;

/// <summary>Device details belong to a connection notice, never to an ordinary battery refresh.</summary>
public sealed class BluetoothBatteryPresentation
{
    private readonly Dictionary<string, DateTimeOffset> deadlines = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> connected = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> Details { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool Update(IReadOnlyList<BluetoothBatteryDevice> devices, DateTimeOffset now, bool noticesEnabled)
    {
        var present = devices.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (noticesEnabled)
            foreach (var id in present.Where(id => !connected.Contains(id))) deadlines[id] = now.AddSeconds(2);
        foreach (var id in deadlines.Keys.Where(id => !noticesEnabled || !present.Contains(id) || deadlines[id] <= now).ToArray()) deadlines.Remove(id);
        connected = present;
        var next = deadlines.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (Details.SetEquals(next)) return false;
        Details = next; return true;
    }
}
