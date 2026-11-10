using QuietIsland.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace QuietIsland.Services;

/// <summary>Observes Windows battery properties and optional AirPods advertisements without polling GATT.</summary>
public sealed partial class BluetoothBatteryService : IDisposable
{
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    // Windows can publish the physical peripheral's metadata on its Bluetooth,
    // HID or software audio node. Only containers of paired Bluetooth peers are used.
    private const string NodeSelector = "System.Devices.DeviceInstanceId:~<\"BTH\" OR System.Devices.DeviceInstanceId:~<\"HID\" OR System.Devices.DeviceInstanceId:~<\"SWD\"";
    private static readonly string[] Properties = [BatteryKey, "System.Devices.BatteryLife", "System.Devices.Aep.IsConnected",
        "System.Devices.Aep.ContainerId", "System.Devices.Aep.DeviceAddress", "System.Devices.ContainerId", "System.Devices.DeviceInstanceId", "System.Devices.CategoryIds", "System.Devices.ClassGuid",
        "System.Devices.Aep.Bluetooth.Cod.Major", "System.Devices.Aep.Bluetooth.Cod.Minor",
        "System.Devices.Aep.Bluetooth.Le.Appearance.Category", "System.Devices.Aep.Bluetooth.Le.Appearance.Subcategory"];
    private readonly object sync = new();
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken lifetimeToken;
    private readonly Dictionary<string, DeviceInformation> endpoints = new(StringComparer.OrdinalIgnoreCase), nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DeviceWatcher> watchers = [];
    private IReadOnlyList<BluetoothBatteryDevice> current = [];
    private long revision;
    private bool started, disposed;
    public event Action? Changed;
    public IReadOnlyList<BluetoothBatteryDevice> Current => Volatile.Read(ref current);

    public BluetoothBatteryService() { lifetimeToken = lifetime.Token; }

    public void Start()
    {
        lock (sync)
        {
            if (disposed || started) return;
            started = true;
            foreach (var (selector, kind) in Queries())
            {
                try
                {
                    var watcher = DeviceInformation.CreateWatcher(selector, Properties, kind);
                    watcher.Added += Added; watcher.Updated += Updated; watcher.Removed += Removed;
                    watchers.Add(watcher); watcher.Start();
                }
                catch (Exception error) { Log.Write("Bluetooth battery watcher", error); }
            }
        }
        _ = PollAsync();
    }

    private static (string Selector, DeviceInformationKind Kind)[] Queries() => [
        (BluetoothDevice.GetDeviceSelectorFromPairingState(true), DeviceInformationKind.AssociationEndpoint),
        (BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), DeviceInformationKind.AssociationEndpoint),
        (NodeSelector, DeviceInformationKind.Device)];

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken, cancellationToken);
        var token = linked.Token;
        await refreshGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            long observed;
            lock (sync) { if (disposed) return; observed = revision; }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var queries = Queries();
            var reads = queries.Select(q => DeviceInformation.FindAllAsync(q.Selector, Properties, q.Kind).AsTask(timeout.Token)).ToArray();
            var collections = await Task.WhenAll(reads).WaitAsync(TimeSpan.FromSeconds(9), token).ConfigureAwait(false);
            bool changed;
            lock (sync)
            {
                // A delayed enumeration must not resurrect a device removed/disconnected by a watcher.
                if (disposed || revision != observed) return;
                endpoints.Clear(); nodes.Clear();
                for (var i = 0; i < collections.Length; i++)
                    foreach (var item in collections[i]) (queries[i].Kind == DeviceInformationKind.Device ? nodes : endpoints)[item.Id] = item;
                changed = PublishLocked();
            }
            if (changed) Changed?.Invoke();
        }
        catch (Exception error) when (!token.IsCancellationRequested)
        {
            // Failure is not evidence of a connection or a battery. Do not leave stale rows visible.
            bool changed;
            lock (sync)
            {
                changed = !disposed && current.Count > 0;
                endpoints.Clear(); nodes.Clear(); revision++; StopAirPodsLocked();
                Volatile.Write(ref current, []);
            }
            if (changed) Changed?.Invoke();
            Log.Write("Bluetooth battery refresh", error);
        }
        finally { refreshGate.Release(); }
    }

    private async Task PollAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            await RefreshAsync(lifetimeToken).ConfigureAwait(false);
            while (await timer.WaitForNextTickAsync(lifetimeToken).ConfigureAwait(false)) await RefreshAsync(lifetimeToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested) { }
    }

    private void Added(DeviceWatcher sender, DeviceInformation item) => Change(() => (item.Kind == DeviceInformationKind.Device ? nodes : endpoints)[item.Id] = item);
    private void Updated(DeviceWatcher sender, DeviceInformationUpdate update) => Change(() =>
    {
        if (endpoints.TryGetValue(update.Id, out var endpoint)) endpoint.Update(update);
        else if (nodes.TryGetValue(update.Id, out var node)) node.Update(update);
    });
    private void Removed(DeviceWatcher sender, DeviceInformationUpdate update) => Change(() => { endpoints.Remove(update.Id); nodes.Remove(update.Id); });
    private void Change(Action action)
    {
        bool changed;
        lock (sync) { if (disposed) return; action(); revision++; changed = PublishLocked(); }
        if (changed) Changed?.Invoke();
    }

    private bool PublishLocked()
    {
        var peers = endpoints.Values.Select(d => new BluetoothBatteryEndpoint(d.Id, d.Name, Container(d, true),
            d.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var connected) && connected is true, Percent(d), Kind(d)));
        var batteries = nodes.Values.Select(d => new BluetoothBatteryNode(Container(d, false) ?? "", Percent(d), Kind(d)));
        var next = MergeAirPodsLocked(BluetoothBatteryPolicy.Select(peers, batteries));
        if (current.SequenceEqual(next)) return false;
        Volatile.Write(ref current, next); return true;
    }

    private static string? Container(DeviceInformation d, bool endpoint)
    {
        d.Properties.TryGetValue(endpoint ? "System.Devices.Aep.ContainerId" : "System.Devices.ContainerId", out var value);
        if (value == null && endpoint) d.Properties.TryGetValue("System.Devices.ContainerId", out value);
        return Guid.TryParse(value?.ToString(), out var id) && id != Guid.Empty ? id.ToString("D") : null;
    }
    private static int? Integer(object? value) => value switch { byte b => b, ushort s => s, uint u when u <= int.MaxValue => (int)u, int i => i, _ => null };
    private static int? Number(DeviceInformation d, string key) => d.Properties.TryGetValue(key, out var value) ? Integer(value) : null;
    private static int? Percent(DeviceInformation d) => d.Properties.TryGetValue(BatteryKey, out var value) && value != null ? Integer(value) : Number(d, "System.Devices.BatteryLife");
    private static Guid? DeviceClass(DeviceInformation d) => d.Properties.TryGetValue("System.Devices.ClassGuid", out var value) && Guid.TryParse(value?.ToString(), out var id) ? id : null;
    private static BluetoothDeviceKind Kind(DeviceInformation d) => BluetoothBatteryPolicy.Classify(
        d.Properties.TryGetValue("System.Devices.CategoryIds", out var category) && category is IEnumerable<string> categories ? categories : [],
        Number(d, "System.Devices.Aep.Bluetooth.Cod.Major"), Number(d, "System.Devices.Aep.Bluetooth.Cod.Minor"),
        Number(d, "System.Devices.Aep.Bluetooth.Le.Appearance.Category"), Number(d, "System.Devices.Aep.Bluetooth.Le.Appearance.Subcategory"), DeviceClass(d));

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            foreach (var watcher in watchers)
            {
                watcher.Added -= Added; watcher.Updated -= Updated; watcher.Removed -= Removed;
                if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) watcher.Stop();
            }
            StopAirPodsLocked();
            watchers.Clear(); endpoints.Clear(); nodes.Clear(); Volatile.Write(ref current, []); Changed = null;
        }
        lifetime.Cancel(); lifetime.Dispose();
    }
}
