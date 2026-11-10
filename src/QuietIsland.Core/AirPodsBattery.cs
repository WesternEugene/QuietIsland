using System.Globalization;

namespace QuietIsland.Core;

public sealed record AirPodsBatteryPacket(ushort ModelId, int Percent);
public sealed record AirPodsBatteryPeer(string Id, string Name, ushort ModelId, ulong Address, bool Connected);

public static class AirPodsBatteryProtocol
{
    private static bool Supported(ushort model) => model is
        0x2002 or 0x200F or 0x2013 or 0x2019 or 0x201B or 0x2030 or 0x2036 or
        0x200E or 0x2014 or 0x2024 or 0x2027 or 0x200A or 0x201F or 0x202D;

    public static bool TryRead(ReadOnlySpan<byte> manufacturerData, out AirPodsBatteryPacket? packet)
    {
        packet = null;
        // Apple manufacturer data is a sequence of type/length/value messages.
        // Proximity pairing has a 25-byte payload. Only its earbud battery byte
        // is read; the following case/charging byte is deliberately unused.
        for (var offset = 0; offset + 2 <= manufacturerData.Length;)
        {
            var tag = manufacturerData[offset];
            var length = manufacturerData[offset + 1];
            if (length > manufacturerData.Length - offset - 2) return false;
            var payload = manufacturerData.Slice(offset + 2, length);
            if (tag == 7 && length == 25 && payload[0] == 1)
            {
                var model = (ushort)(payload[1] | payload[2] << 8);
                if (!Supported(model)) return false;
                var first = payload[4] >> 4;
                var second = payload[4] & 15;
                if (first is > 10 and < 15 || second is > 10 and < 15 || first == 15 && second == 15) return false;
                // Max has one battery. For earbuds, unknown (F) is omitted from
                // the mean, so an available active earbud can still report charge.
                var percent = model is 0x200A or 0x201F or 0x202D
                    ? (first == 15 ? second : first) * 10
                    : first == 15 ? second * 10 : second == 15 ? first * 10 : (first + second) * 5;
                packet = new(model, percent);
                return true;
            }
            offset += length + 2;
        }
        return false;
    }

    public static ushort? ModelFromWindowsId(string id)
    {
        if (!id.Contains("VID&0001004C", StringComparison.OrdinalIgnoreCase) &&
            !id.Contains("VID&000205AC", StringComparison.OrdinalIgnoreCase)) return null;
        var index = id.IndexOf("_PID&", StringComparison.OrdinalIgnoreCase);
        return index >= 0 && index + 9 <= id.Length &&
            ushort.TryParse(id.AsSpan(index + 5, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var model) && Supported(model)
            ? model : null;
    }
}

public sealed class AirPodsBatteryCache
{
    private sealed record Sample(ushort Model, int Percent, int Rssi, DateTimeOffset Seen);
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2), DiscoveryWindow = TimeSpan.FromSeconds(10);
    private readonly Dictionary<ulong, Sample> samples = [];
    private readonly Dictionary<string, ulong> bindings = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> discoveryBlockedPeers = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset? NextExpiration => samples.Count == 0 ? null : samples.Min(pair => pair.Value.Seen) + Lifetime;

    public bool Observe(ulong address, int rssi, ReadOnlySpan<byte> manufacturerData, DateTimeOffset now)
    {
        if (address == 0 || !AirPodsBatteryProtocol.TryRead(manufacturerData, out var packet)) return false;
        Prune(now);
        samples[address] = new(packet!.ModelId, packet.Percent, rssi, now);
        if (samples.Count > 128) samples.Remove(samples.MinBy(pair => pair.Value.Seen).Key);
        return true;
    }

    public IReadOnlyList<BluetoothBatteryDevice> Resolve(IReadOnlyList<AirPodsBatteryPeer> peers, DateTimeOffset now)
    {
        Prune(now);
        var connected = peers.Where(peer => peer.Connected).ToArray();
        var ids = connected.Select(peer => peer.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in bindings.Keys.Where(id => !ids.Contains(id)).ToArray()) bindings.Remove(id);
        discoveryBlockedPeers.RemoveWhere(id => !ids.Contains(id));
        var result = new List<BluetoothBatteryDevice>();
        foreach (var peer in connected)
        {
            Sample? selected = null;
            if (samples.TryGetValue(peer.Address, out var exact) && exact.Model == peer.ModelId)
            {
                selected = exact;
                // Once its exact identity is known, never replace it with a
                // heuristic match after that sample expires during this connection.
                discoveryBlockedPeers.Add(peer.Id);
            }
            else if (!discoveryBlockedPeers.Contains(peer.Id) && connected.Count(other => other.ModelId == peer.ModelId) == 1)
            {
                var known = samples.Where(pair => pair.Value.Model == peer.ModelId).ToArray();
                var nearby = known.Where(pair => pair.Value.Rssi >= -70 && now - pair.Value.Seen <= DiscoveryWindow).ToArray();
                if (known.Length > 1) { bindings.Remove(peer.Id); discoveryBlockedPeers.Add(peer.Id); continue; }
                if (nearby.Length == 1)
                {
                    bindings[peer.Id] = nearby[0].Key;
                    selected = nearby[0].Value;
                }
                else if (nearby.Length == 0 && bindings.TryGetValue(peer.Id, out var bound) &&
                    samples.TryGetValue(bound, out var previous) && previous.Model == peer.ModelId) selected = previous;
                else bindings.Remove(peer.Id); // Never choose the strongest of two indistinguishable pairs.
            }
            else discoveryBlockedPeers.Add(peer.Id);
            if (selected != null)
                result.Add(new(peer.Id, peer.Name, selected.Percent, BluetoothDeviceKind.Headphones) { IsApproximate = true });
        }
        return result;
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var key in samples.Where(pair => now - pair.Value.Seen > Lifetime).Select(pair => pair.Key).ToArray()) samples.Remove(key);
        foreach (var id in bindings.Keys.Where(id => !samples.ContainsKey(bindings[id])).ToArray()) bindings.Remove(id);
    }
    public void Clear() { samples.Clear(); bindings.Clear(); discoveryBlockedPeers.Clear(); }
}
