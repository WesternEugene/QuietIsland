using QuietIsland.Services.Devices;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace QuietIsland.Services;

public sealed record DeviceSnapshot(bool WifiAvailable, bool WifiOn, bool BluetoothAvailable, bool BluetoothOn, string WifiDetail, string BluetoothDetail);

/// <param name="Connected">For audio peers, at least one associated audio endpoint is actually active.</param>
/// <param name="CanConnect">The audio driver advertises its native reconnect command; not a promise that the remote device is reachable.</param>
public sealed record BluetoothPeer(string Id, string Name, bool Connected, bool CanConnect);

/// <summary>
/// Real Windows radio controls for an ordinary desktop EXE. Construction and refresh never request
/// access or modify devices. Call write operations from a user interaction on the WPF UI thread.
/// </summary>
public sealed class DeviceControlService : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken lifetimeToken;
    private DeviceSnapshot current = new(false, false, false, false, "Проверка Wi-Fi…", "Проверка Bluetooth…");
    private RadioAccessStatus? radioAccess;
    private int disposed;

    public DeviceControlService() { lifetimeToken = lifetime.Token; }
    public DeviceSnapshot Current => Volatile.Read(ref current);

    public Task RefreshAsync(CancellationToken cancellationToken = default) => RunAsync(RefreshCoreAsync, cancellationToken);

    public Task SetWifiAsync(bool enabled, CancellationToken cancellationToken = default) => RunAsync(async token =>
    {
        try
        {
            await Task.Run(() => WlanRadio.Set(enabled, token), token);
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var reading = await Task.Run(() => WlanRadio.Read(token), token);
                if (reading.Available && reading.Phys.All(p => p.SoftwareState == (enabled ? 1 : 2)) && (!enabled || reading.On)) return;
                await Task.Delay(250, token);
            }
            throw new InvalidOperationException("Windows не подтвердила новое состояние Wi-Fi. Проверьте аппаратный переключатель и режим полёта.");
        }
        finally { if (!token.IsCancellationRequested) await RefreshCoreAsync(token); }
    }, cancellationToken);

    public Task SetBluetoothAsync(bool enabled, CancellationToken cancellationToken = default) => RunAsync(async token =>
    {
        try
        {
            var radios = await BluetoothRadiosAsync(token);
            if (radios.Count == 0) throw new NotSupportedException("Bluetooth-адаптер не найден или недоступен Windows Radio API.");
            if (Matches(radios, enabled)) return;
            if (enabled && radios.Any(r => r.State == RadioState.Disabled))
                throw new InvalidOperationException("Bluetooth отключён аппаратно или политикой Windows. Программное включение недоступно.");

            // This is deliberately absent from all read-only paths. Windows can show its own consent
            // UI here on the user's explicit toggle. Access is cached for this service lifetime.
            radioAccess ??= await Radio.RequestAccessAsync().AsTask(token);
            if (radioAccess != RadioAccessStatus.Allowed) throw RadioAccessError(radioAccess.Value);
            foreach (var radio in radios)
            {
                token.ThrowIfCancellationRequested();
                if (radio.State == (enabled ? RadioState.On : RadioState.Off) || !enabled && radio.State == RadioState.Disabled) continue;
                var result = await radio.SetStateAsync(enabled ? RadioState.On : RadioState.Off).AsTask(token);
                if (result != RadioAccessStatus.Allowed)
                {
                    radioAccess = null;
                    throw RadioAccessError(result);
                }
            }
            for (var attempt = 0; attempt < 20; attempt++)
            {
                radios = await BluetoothRadiosAsync(token);
                if (radios.Count > 0 && Matches(radios, enabled)) return;
                await Task.Delay(250, token);
            }
            throw new InvalidOperationException("Windows не подтвердила новое состояние Bluetooth.");
        }
        finally { if (!token.IsCancellationRequested) await RefreshCoreAsync(token); }
    }, cancellationToken);

    public Task<IReadOnlyList<BluetoothPeer>> GetBluetoothDevicesAsync(CancellationToken cancellationToken = default) => RunAsync(async token =>
    {
        var paired = await ReadPairedAsync(token);
        var containers = paired.Where(p => p.Container != Guid.Empty).Select(p => p.Container).ToHashSet();
        var audio = await Task.Run(() => BluetoothAudio.Read(containers, token), token);
        return (IReadOnlyList<BluetoothPeer>)paired.Select(peer =>
        {
            var endpoints = audio.Where(e => e.ContainerId == peer.Container).ToList();
            return new BluetoothPeer(peer.Id, peer.Name, endpoints.Count > 0 ? endpoints.Any(e => e.Connected) : peer.LinkConnected, endpoints.Any(e => e.CanConnect));
        }).OrderByDescending(p => p.Connected).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }, cancellationToken);

    public Task ConnectBluetoothAsync(string id, CancellationToken cancellationToken = default) => SetBluetoothConnectionAsync(id, true, cancellationToken);
    public Task DisconnectBluetoothAsync(string id, CancellationToken cancellationToken = default) => SetBluetoothConnectionAsync(id, false, cancellationToken);

    private Task SetBluetoothConnectionAsync(string id, bool connect, CancellationToken cancellationToken) => RunAsync(async token =>
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Не выбрано Bluetooth-устройство.", nameof(id));
        var paired = await ReadPairedAsync(token);
        var peer = paired.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
        if (peer == null) throw new InvalidOperationException("Bluetooth-устройство больше не находится в списке сопряжённых. Обновите список.");
        if (peer.Container == Guid.Empty) throw UnsupportedAudio();
        var containers = new HashSet<Guid> { peer.Container };
        var endpoints = await Task.Run(() => BluetoothAudio.Read(containers, token), token);
        if (endpoints.Count == 0) throw UnsupportedAudio();
        if (connect ? endpoints.Any(e => e.Connected) : endpoints.All(e => !e.Connected)) return;
        if (connect && !(await BluetoothRadiosAsync(token)).Any(r => r.State == RadioState.On))
            throw new InvalidOperationException("Сначала включите Bluetooth.");

        await Task.Run(() => BluetoothAudio.Request(peer.Container, connect, token), token);
        for (var attempt = 0; attempt < 24; attempt++)
        {
            await Task.Delay(500, token);
            endpoints = await Task.Run(() => BluetoothAudio.Read(containers, token), token);
            if (connect ? endpoints.Any(e => e.Connected) : endpoints.All(e => !e.Connected)) return;
        }
        throw new InvalidOperationException(connect
            ? "Драйвер принял команду, но наушники не подключились за 12 секунд. Проверьте питание и доступность наушников."
            : "Драйвер принял команду, но аудиоустройство осталось подключённым. Windows не подтвердила отключение.");
    }, cancellationToken);

    private async Task RefreshCoreAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var wifiAvailable = false;
        var wifiOn = false;
        string wifiDetail;
        try
        {
            var wifi = await Task.Run(() => WlanRadio.Read(token), token);
            wifiAvailable = wifi.Available;
            wifiOn = wifi.On;
            wifiDetail = wifi.Detail;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            wifiDetail = e is InvalidOperationException ? e.Message : $"Чтение Wi-Fi недоступно (0x{e.HResult:X8})";
        }

        var bluetoothAvailable = false;
        var bluetoothOn = false;
        string bluetoothDetail;
        try
        {
            var radios = await BluetoothRadiosAsync(token);
            bluetoothAvailable = radios.Count > 0;
            bluetoothOn = radios.Any(r => r.State == RadioState.On);
            bluetoothDetail = !bluetoothAvailable ? "Bluetooth-адаптер не найден"
                : bluetoothOn ? "Bluetooth включён"
                : radios.Any(r => r.State == RadioState.Disabled) ? "Bluetooth отключён аппаратно или политикой Windows"
                : radios.Any(r => r.State == RadioState.Unknown) ? "Windows не сообщила состояние Bluetooth"
                : "Bluetooth выключен";
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            bluetoothDetail = $"Чтение Bluetooth недоступно (0x{e.HResult:X8})";
        }
        token.ThrowIfCancellationRequested();
        Volatile.Write(ref current, new DeviceSnapshot(wifiAvailable, wifiOn, bluetoothAvailable, bluetoothOn, wifiDetail, bluetoothDetail));
    }

    private static async Task<IReadOnlyList<Radio>> BluetoothRadiosAsync(CancellationToken token)
    {
        var radios = await Radio.GetRadiosAsync().AsTask(token);
        return radios.Where(r => r.Kind == RadioKind.Bluetooth).ToList();
    }

    private sealed record PairedDevice(string Id, string Name, Guid Container, bool LinkConnected);
    private static async Task<IReadOnlyList<PairedDevice>> ReadPairedAsync(CancellationToken token)
    {
        string[] properties = ["System.Devices.Aep.ContainerId", "System.Devices.ContainerId", "System.Devices.Aep.IsConnected"];
        // Use the OS-generated protocol selectors. A hand-built cross-protocol OR query can make
        // the Bluetooth provider perform lengthy discovery despite an IsPaired filter.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var classic = DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true), properties, DeviceInformationKind.AssociationEndpoint).AsTask(timeout.Token);
        var lowEnergy = DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), properties, DeviceInformationKind.AssociationEndpoint).AsTask(timeout.Token);
        DeviceInformationCollection[] collections;
        try { collections = await Task.WhenAll(classic, lowEnergy).WaitAsync(TimeSpan.FromSeconds(9), token); }
        catch (Exception e) when (!token.IsCancellationRequested && e is OperationCanceledException or TimeoutException)
        {
            throw new InvalidOperationException("Windows не вернула список сопряжённых Bluetooth-устройств за 8 секунд. Повторите обновление.");
        }
        var paired = new List<PairedDevice>();
        foreach (var device in collections.SelectMany(c => c))
        {
            var container = Guid.Empty;
            foreach (var key in properties.Take(2))
                if (device.Properties.TryGetValue(key, out var value) && Guid.TryParse(value?.ToString(), out container) && container != Guid.Empty) break;
            var connected = device.Properties.TryGetValue(properties[2], out var state) && state is true;
            paired.Add(new PairedDevice(device.Id, string.IsNullOrWhiteSpace(device.Name) ? "Bluetooth-устройство" : device.Name, container, connected));
        }
        // Dual-mode peers can have both Classic and LE association endpoints in the same container.
        return paired.GroupBy(p => p.Container == Guid.Empty ? p.Id : p.Container.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(p => p.Id, StringComparer.Ordinal).First() with { LinkConnected = g.Any(p => p.LinkConnected) }).ToList();
    }

    private static bool Matches(IReadOnlyList<Radio> radios, bool enabled) => enabled
        ? radios.All(r => r.State == RadioState.On)
        : radios.All(r => r.State is RadioState.Off or RadioState.Disabled);

    private static Exception RadioAccessError(RadioAccessStatus status) => new InvalidOperationException(status switch
    {
        RadioAccessStatus.DeniedByUser => "Пользователь запретил приложению управлять Bluetooth.",
        RadioAccessStatus.DeniedBySystem => "Windows запретила приложению управление Bluetooth. Проверьте ограничения системы и разрешения управления радио.",
        _ => "Windows не предоставила доступ к управлению Bluetooth (" + status + ")."
    });

    private static Exception UnsupportedAudio() => new NotSupportedException("Для этого Bluetooth-устройства не найден управляемый аудиопрофиль. Подключение и отключение поддерживаются для наушников, чей драйвер предоставляет эти команды.");

    private Task RunAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken) => RunAsync(async token => { await action(token); return true; }, cancellationToken);
    private async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeToken);
        await gate.WaitAsync(linked.Token);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            linked.Token.ThrowIfCancellationRequested();
            return await action(linked.Token);
        }
        finally { gate.Release(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        lifetime.Dispose();
        // No native handles are retained between calls. The semaphore is intentionally not
        // disposed while an in-flight async operation may still be unwinding its finally block.
    }
}
