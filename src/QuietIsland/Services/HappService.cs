using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;
using Microsoft.Win32;
using QuietIsland.Services.Happ;

namespace QuietIsland.Services;

public sealed record HappSnapshot(bool Available, bool Connected, bool CanToggle, string Detail);

/// <summary>
/// Read-only discovery/refresh; explicit user toggles use Happ's own connect/disconnect URI handler.
/// A running GUI or background Windows service alone never counts as a connection.
/// No subscriptions, server configurations, credentials or traffic destinations are read.
/// </summary>
public sealed class HappService : IDisposable
{
    private readonly string? configuredPath;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken lifetimeToken;
    private HappSnapshot current = new(false, false, false, "Проверка Happ…");
    private Installation? cachedInstallation;
    private bool stateKnown;
    private int disposed;

    public HappService() : this(null) { }
    public HappService(string? executablePath)
    {
        configuredPath = string.IsNullOrWhiteSpace(executablePath) ? null : executablePath;
        lifetimeToken = lifetime.Token;
    }

    public string? ExecutablePath { get; private set; }
    public HappSnapshot Current => Volatile.Read(ref current);
    public Task RefreshAsync(CancellationToken cancellationToken = default) => RunAsync(RefreshCoreAsync, cancellationToken);

    public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default) => RunAsync(token => SetCoreAsync(enabled, token), cancellationToken);
    public Task ToggleAsync(CancellationToken cancellationToken = default) => RunAsync(token => SetCoreAsync(null, token), cancellationToken);

    private async Task SetCoreAsync(bool? requested, CancellationToken token)
    {
        await RefreshCoreAsync(token);
        if (!Current.Available || !Current.CanToggle || ExecutablePath == null) throw new NotSupportedException(Current.Detail);
        if (requested == null && !stateKnown) throw new InvalidOperationException(Current.Detail + ". Сначала проверьте соединение в Happ.");
        var enabled = requested ?? !Current.Connected;
        if (stateKnown && Current.Connected == enabled) return;

        // The only write operation in this service. ArgumentList keeps the fixed URI separate
        // from the executable path. Never kill the core, stop HappService, or alter Windows proxy.
        try
        {
            using var command = Process.Start(HappEvidence.StartInfo(ExecutablePath, enabled));
            if (command == null) throw new InvalidOperationException("Не удалось передать команду Happ.");
        }
        catch (Win32Exception error)
        {
            throw new InvalidOperationException($"Не удалось передать команду Happ (0x{error.HResult:X8}).", error);
        }

        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(12))
        {
            await Task.Delay(250, token);
            await RefreshCoreAsync(token);
            if (stateKnown && Current.Connected == enabled) return;
        }
        var detail = enabled
            ? "Команда передана Happ, но подключение не подтверждено за 12 секунд. Проверьте выбранный сервер в Happ."
            : "Команда передана Happ, но отключение не подтверждено за 12 секунд. Проверьте состояние в Happ.";
        Volatile.Write(ref current, Current with { Detail = detail });
        throw new InvalidOperationException(detail);
    }

    private async Task RefreshCoreAsync(CancellationToken token)
    {
        Installation? installation = null;
        try
        {
            installation = await Task.Run(() => Discover(token), token);
            ExecutablePath = installation?.Path;
            if (installation == null)
            {
                stateKnown = true;
                Volatile.Write(ref current, new HappSnapshot(false, false, false, "Happ.exe не найден. Установите Happ или укажите путь к приложению."));
                return;
            }

            var local = await Task.Run(() => ReadLocalEvidence(installation, token), token);
            bool? daemonTunnel = null;
            if (installation.ObserverSupported)
            {
                try { daemonTunnel = await HappDaemon.ReadTunnelAsync(token); }
                catch (Exception error) when (!token.IsCancellationRequested && error is IOException or TimeoutException or OperationCanceledException or JsonException or InvalidOperationException)
                {
                    // Proxy mode does not require the daemon. For TUN, exact Happ adapter names
                    // remain independent evidence if the observer pipe is unavailable.
                }
            }
            token.ThrowIfCancellationRequested();
            var connected = local.ProxyOwned || local.CoreRunning && (local.TunnelUp || daemonTunnel == true);
            var disconnected = !local.CoreRunning && !local.UnreadableCore && !local.TunnelUp && daemonTunnel != true;
            stateKnown = connected || disconnected;
            var detail = connected
                ? local.ProxyOwned && (local.TunnelUp || daemonTunnel == true) ? "Happ подключён · TUN и системный прокси"
                    : local.ProxyOwned ? "Happ подключён · системный прокси" : "Happ подключён · TUN"
                : disconnected ? "Happ отключён"
                : "Happ найден; состояние подключения не подтверждено";
            if (!installation.CommandsSupported) detail += ". Эта версия Happ не предоставляет подтверждённые команды подключения и отключения.";
            Volatile.Write(ref current, new HappSnapshot(true, connected, installation.CommandsSupported, detail));
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            stateKnown = false;
            Volatile.Write(ref current, new HappSnapshot(installation != null, false, false, $"Состояние Happ недоступно (0x{error.HResult:X8})."));
        }
    }

    private Installation? Discover(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var path = FindExecutable();
        if (path == null) return null;
        var file = new FileInfo(path);
        if (cachedInstallation is { } cached && string.Equals(cached.Path, path, StringComparison.OrdinalIgnoreCase)
            && cached.Length == file.Length && cached.Modified == file.LastWriteTimeUtc) return cached;
        var version = FileVersionInfo.GetVersionInfo(path);
        if (!string.Equals(version.ProductName, "Happ", StringComparison.OrdinalIgnoreCase)) return null;
        var image = ReadImage(path);
        var commands = image.AsSpan().IndexOf("happ://connect"u8) >= 0 && image.AsSpan().IndexOf("happ://disconnect"u8) >= 0;
        var daemonPath = Path.Combine(file.DirectoryName!, "happd.exe");
        var observer = File.Exists(daemonPath) && ReadImage(daemonPath).AsSpan().IndexOf("registered as a read-only observer"u8) >= 0;
        token.ThrowIfCancellationRequested();
        return cachedInstallation = new Installation(path, commands, observer, file.Length, file.LastWriteTimeUtc);
    }

    private string? FindExecutable()
    {
        if (configuredPath != null) return ExistingExecutable(configuredPath);
        foreach (var process in Process.GetProcessesByName("Happ"))
        {
            using (process)
            {
                var runningPath = HappNative.ExecutablePath(process);
                if (runningPath != null && ExistingExecutable(runningPath) is { } found) return found;
            }
        }
        // Read only the executable from the registered protocol command; never execute registry text.
        using var commandKey = Registry.ClassesRoot.OpenSubKey(@"happ\shell\open\command");
        if (commandKey?.GetValue("") is string command)
        {
            var text = command.Trim();
            var end = text.StartsWith('"') ? text.IndexOf('"', 1) : text.IndexOf(' ');
            var executable = text.StartsWith('"') && end > 1 ? text[1..end] : end > 0 ? text[..end] : text;
            if (ExistingExecutable(Environment.ExpandEnvironmentVariables(executable)) is { } registered) return registered;
        }
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
            if (ExistingExecutable(Path.Combine(root, "FlyFrogLLC", "Happ", "Happ.exe")) is { } standard) return standard;
        return null;
    }

    private static string? ExistingExecutable(string path)
    {
        if (!Path.GetFileName(path).Equals("Happ.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
        return Path.GetFullPath(path);
    }

    private static byte[] ReadImage(string path)
    {
        if (new FileInfo(path).Length > 134_217_728) throw new InvalidDataException("Неизвестный формат программы Happ.");
        return File.ReadAllBytes(path);
    }

    private static LocalEvidence ReadLocalEvidence(Installation installation, CancellationToken token)
    {
        var expectedCore = Path.Combine(Path.GetDirectoryName(installation.Path)!, "core", "xray.exe");
        var coreIds = new HashSet<int>();
        var unreadable = false;
        foreach (var process in Process.GetProcessesByName("xray"))
        {
            using (process)
            {
                token.ThrowIfCancellationRequested();
                var path = HappNative.ExecutablePath(process);
                if (path == null) { if (!process.HasExited) unreadable = true; }
                else if (path.Equals(expectedCore, StringComparison.OrdinalIgnoreCase)) coreIds.Add(process.Id);
            }
        }
        var tunnelUp = NetworkInterface.GetAllNetworkInterfaces().Any(adapter => HappEvidence.IsTunnelName(adapter.Name) && adapter.OperationalStatus == OperationalStatus.Up);
        var proxyOwned = false;
        if (coreIds.Count > 0)
        {
            using var proxy = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            if (proxy?.GetValue("ProxyEnable") is int enabled && enabled != 0)
            {
                var ports = HappEvidence.LoopbackProxyPorts(proxy.GetValue("ProxyServer") as string);
                if (ports.Count > 0) proxyOwned = HappNative.ListeningPorts(coreIds).Overlaps(ports);
            }
        }
        token.ThrowIfCancellationRequested();
        return new LocalEvidence(coreIds.Count > 0, unreadable, tunnelUp, proxyOwned);
    }

    private async Task RunAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeToken);
        await gate.WaitAsync(linked.Token);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            linked.Token.ThrowIfCancellationRequested();
            await action(linked.Token);
        }
        finally { gate.Release(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        lifetime.Dispose();
        // The gate stays alive until in-flight callers have unwound their finally blocks.
    }

    private sealed record Installation(string Path, bool CommandsSupported, bool ObserverSupported, long Length, DateTime Modified);
    private sealed record LocalEvidence(bool CoreRunning, bool UnreadableCore, bool TunnelUp, bool ProxyOwned);
}
