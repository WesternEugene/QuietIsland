using System.Globalization;
using QuietIsland.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace QuietIsland.Services;

public sealed partial class BluetoothBatteryService
{
    private readonly AirPodsBatteryCache airPods = new();
    private BluetoothLEAdvertisementWatcher? airPodsWatcher;
    private Timer? airPodsExpiry;
    private bool airPodsEnabled = true;

    public void SetAirPodsEnabled(bool enabled)
    {
        bool changed;
        lock (sync)
        {
            if (disposed || airPodsEnabled == enabled) return;
            airPodsEnabled = enabled;
            if (!enabled) StopAirPodsLocked();
            changed = PublishLocked();
        }
        if (changed) Changed?.Invoke();
    }

    private IReadOnlyList<BluetoothBatteryDevice> MergeAirPodsLocked(IReadOnlyList<BluetoothBatteryDevice> native)
    {
        var peers = airPodsEnabled ? AirPodsPeersLocked() : [];
        EnsureAirPodsWatcherLocked(peers.Count > 0);
        if (!airPodsEnabled) return native;
        var nativeIds = native.Select(device => device.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var readings = airPods.Resolve(peers, DateTimeOffset.UtcNow);
        ScheduleAirPodsExpiryLocked();
        return native.Concat(readings.Where(device => !nativeIds.Contains(device.Id)))
            .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(device => device.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private IReadOnlyList<AirPodsBatteryPeer> AirPodsPeersLocked()
    {
        var result = new List<AirPodsBatteryPeer>();
        var associated = nodes.Values.ToLookup(device => Container(device, false) ?? "", StringComparer.OrdinalIgnoreCase);
        foreach (var group in endpoints.Values.GroupBy(device => Container(device, true) ?? device.Id, StringComparer.OrdinalIgnoreCase))
        {
            var model = associated[group.Key].Concat(group).Select(device =>
                AirPodsBatteryProtocol.ModelFromWindowsId(device.Id) ??
                (device.Properties.TryGetValue("System.Devices.DeviceInstanceId", out var id) ? AirPodsBatteryProtocol.ModelFromWindowsId(id?.ToString() ?? "") : null))
                .FirstOrDefault(model => model.HasValue);
            if (model == null) continue;
            var primary = group.FirstOrDefault(device => device.Id.StartsWith("Bluetooth#", StringComparison.OrdinalIgnoreCase)) ?? group.First();
            var connected = group.Any(device => device.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var value) && value is true);
            result.Add(new(group.Key, string.IsNullOrWhiteSpace(primary.Name) ? "AirPods" : primary.Name.Trim(), model.Value, AirPodsAddress(primary), connected));
        }
        return result;
    }

    private static ulong AirPodsAddress(DeviceInformation device)
    {
        var value = device.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var address) ? address?.ToString() : null;
        if (string.IsNullOrWhiteSpace(value) && device.Id.StartsWith("Bluetooth#", StringComparison.OrdinalIgnoreCase))
            value = device.Id[(device.Id.LastIndexOf('-') + 1)..];
        var hex = (value ?? "").Replace(":", "").Replace("-", "");
        return hex.Length == 12 && ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private void EnsureAirPodsWatcherLocked(bool paired)
    {
        if (!started || !airPodsEnabled || !paired || disposed)
        {
            if (airPodsWatcher != null) StopAirPodsLocked();
            return;
        }
        if (airPodsWatcher?.Status is BluetoothLEAdvertisementWatcherStatus.Started or BluetoothLEAdvertisementWatcherStatus.Created) return;
        StopAirPodsLocked();
        try
        {
            // AirPods proximity messages fit legacy advertisements. No connection,
            // audio control, pairing change or extra driver is needed to receive them.
            airPodsWatcher = new() { ScanningMode = BluetoothLEScanningMode.Active };
            airPodsWatcher.Received += AirPodsReceived;
            airPodsWatcher.Stopped += AirPodsStopped;
            airPodsWatcher.Start();
        }
        catch (Exception error) { StopAirPodsLocked(); Log.Write("AirPods battery scan", error); }
    }

    private void AirPodsReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        try
        {
            foreach (var manufacturer in args.Advertisement.ManufacturerData)
            {
                if (manufacturer.CompanyId != 0x004C || manufacturer.Data.Length is < 27 or > 512) continue;
                using var reader = DataReader.FromBuffer(manufacturer.Data);
                var bytes = new byte[manufacturer.Data.Length]; reader.ReadBytes(bytes);
                bool changed;
                lock (sync)
                {
                    if (disposed || !airPodsEnabled || !ReferenceEquals(sender, airPodsWatcher)) return;
                    if (!airPods.Observe(args.BluetoothAddress, args.RawSignalStrengthInDBm, bytes, DateTimeOffset.UtcNow)) continue;
                    changed = PublishLocked();
                }
                if (changed) Changed?.Invoke();
            }
        }
        catch (Exception error) { Log.Write("AirPods battery advertisement", error); }
    }

    private void AirPodsStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        if (args.Error != BluetoothError.Success) Log.Write("AirPods battery scan stopped", new InvalidOperationException(args.Error.ToString()));
        // The regular refresh retries after a radio/driver interruption.
    }

    private void ScheduleAirPodsExpiryLocked()
    {
        var deadline = airPods.NextExpiration;
        if (deadline == null) { airPodsExpiry?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan); return; }
        airPodsExpiry ??= new Timer(_ => ExpireAirPods(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        var due = deadline.Value - DateTimeOffset.UtcNow;
        airPodsExpiry.Change(due > TimeSpan.Zero ? due : TimeSpan.FromMilliseconds(1), Timeout.InfiniteTimeSpan);
    }

    private void ExpireAirPods()
    {
        try
        {
            bool changed;
            lock (sync)
            {
                if (disposed || !airPodsEnabled) return;
                // Expiry must not wait for the next asynchronous Windows enumeration.
                changed = PublishLocked();
            }
            if (changed) Changed?.Invoke();
        }
        catch (Exception error) { Log.Write("AirPods battery expiry", error); }
    }

    private void StopAirPodsLocked()
    {
        airPodsExpiry?.Dispose(); airPodsExpiry = null;
        var watcher = airPodsWatcher; airPodsWatcher = null;
        if (watcher != null)
        {
            watcher.Received -= AirPodsReceived; watcher.Stopped -= AirPodsStopped;
            try { if (watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started) watcher.Stop(); }
            catch (Exception error) { Log.Write("AirPods battery stop", error); }
        }
        airPods.Clear();
    }
}
