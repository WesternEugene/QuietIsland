namespace QuietIsland.Core;

public enum BluetoothDeviceKind { Unknown, Mouse, Keyboard, Headphones, Speaker, Gamepad, Phone, Watch, Pen, Computer, Microphone }
public enum BatteryChargeBand { Low, Medium, High }

public sealed record BluetoothBatteryEndpoint(string Id, string Name, string? ContainerId, bool Connected, int? Percent, BluetoothDeviceKind Kind);
public sealed record BluetoothBatteryNode(string ContainerId, int? Percent, BluetoothDeviceKind Kind);
public sealed record BluetoothBatteryDevice(string Id, string Name, int Percent, BluetoothDeviceKind Kind)
{
    public bool IsApproximate { get; init; }
    public string ChargeLabel => $"{(IsApproximate ? "≈" : "")}{Percent}%";
    public double Progress => Percent / 100d;
    public BatteryChargeBand Band => Percent > 50 ? BatteryChargeBand.High : Percent > 30 ? BatteryChargeBand.Medium : BatteryChargeBand.Low;
}

public static class BluetoothBatteryPolicy
{
    public static IReadOnlyList<BluetoothBatteryDevice> Select(IEnumerable<BluetoothBatteryEndpoint> endpoints, IEnumerable<BluetoothBatteryNode> nodes)
    {
        var batteries = nodes.Where(n => !string.IsNullOrWhiteSpace(n.ContainerId))
            .ToLookup(n => n.ContainerId, StringComparer.OrdinalIgnoreCase);
        var result = new List<BluetoothBatteryDevice>();
        foreach (var group in endpoints.GroupBy(e => string.IsNullOrWhiteSpace(e.ContainerId) ? e.Id : e.ContainerId, StringComparer.OrdinalIgnoreCase))
        {
            // Paired/present is not connected. Never borrow a cached percentage from an offline endpoint.
            var connected = group.Where(e => e.Connected).ToArray();
            if (connected.Length == 0) continue;
            var associated = batteries[group.Key].ToArray();
            var percent = connected.Select(e => e.Percent).Concat(associated.Select(n => n.Percent)).FirstOrDefault(Valid);
            if (!Valid(percent)) continue;
            // A headset may also expose HID media keys. Its Bluetooth identity describes
            // the physical device more accurately than an auxiliary input interface.
            var kind = group.Select(e => e.Kind).Concat(associated.Select(n => n.Kind)).FirstOrDefault(k => k != BluetoothDeviceKind.Unknown);
            var name = connected.Select(e => e.Name.Trim()).FirstOrDefault(n => n.Length > 0) ?? "Bluetooth";
            result.Add(new(group.Key, name, percent!.Value, kind));
        }
        return result.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool Valid(int? value) => value is >= 0 and <= 100;

    public static BluetoothDeviceKind Classify(IEnumerable<string> categories, int? major = null, int? minor = null, int? appearance = null, int? subcategory = null, Guid? deviceClass = null)
    {
        foreach (var category in categories)
        {
            var kind = category.ToLowerInvariant() switch
            {
                var c when c.Contains("mouse") => BluetoothDeviceKind.Mouse,
                var c when c.Contains("keyboard") => BluetoothDeviceKind.Keyboard,
                var c when c.Contains("headphone") || c.Contains("headset") => BluetoothDeviceKind.Headphones,
                var c when c.Contains("speaker") => BluetoothDeviceKind.Speaker,
                var c when c.Contains("gamepad") || c.Contains("gamecontroller") || c.Contains("input.gaming") => BluetoothDeviceKind.Gamepad,
                var c when c.Contains("microphone") => BluetoothDeviceKind.Microphone,
                var c when c.Contains("phone") => BluetoothDeviceKind.Phone,
                var c when c.Contains("watch") => BluetoothDeviceKind.Watch,
                var c when c.Contains("tabletpen") || c.EndsWith(".pen") => BluetoothDeviceKind.Pen,
                var c when c.Contains("computer") => BluetoothDeviceKind.Computer,
                _ => BluetoothDeviceKind.Unknown
            };
            if (kind != BluetoothDeviceKind.Unknown) return kind;
        }
        if (deviceClass == new Guid("4D36E96B-E325-11CE-BFC1-08002BE10318")) return BluetoothDeviceKind.Keyboard;
        if (deviceClass == new Guid("4D36E96F-E325-11CE-BFC1-08002BE10318")) return BluetoothDeviceKind.Mouse;
        if (appearance == 15) return subcategory switch
        {
            1 => BluetoothDeviceKind.Keyboard, 2 => BluetoothDeviceKind.Mouse,
            3 or 4 => BluetoothDeviceKind.Gamepad, 7 => BluetoothDeviceKind.Pen, _ => BluetoothDeviceKind.Unknown
        };
        if (appearance is 1 or 2 or 3) return appearance switch { 1 => BluetoothDeviceKind.Phone, 2 => BluetoothDeviceKind.Computer, _ => BluetoothDeviceKind.Watch };
        if (major == 5 && minor is { } peripheral)
        {
            if ((peripheral & 32) != 0) return BluetoothDeviceKind.Mouse;
            if ((peripheral & 16) != 0) return BluetoothDeviceKind.Keyboard;
            if ((peripheral & 15) is 1 or 2) return BluetoothDeviceKind.Gamepad;
        }
        return (major, minor) switch
        {
            (4, 1 or 2 or 6) => BluetoothDeviceKind.Headphones,
            (4, 5 or 7 or 10) => BluetoothDeviceKind.Speaker,
            (4, 4) => BluetoothDeviceKind.Microphone,
            (1, _) => BluetoothDeviceKind.Computer,
            (2, _) => BluetoothDeviceKind.Phone,
            (7, 1) => BluetoothDeviceKind.Watch,
            _ => BluetoothDeviceKind.Unknown
        };
    }
}
