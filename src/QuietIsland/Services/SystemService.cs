using System.Diagnostics;
using System.Net.NetworkInformation;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using QuietIsland.Models;

namespace QuietIsland.Services;

public sealed class SystemService(DesktopState state)
{
    public static void Open(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        if (ShellApplications.IsTarget(target))
        {
            var launch = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            launch.ArgumentList.Add(target); Process.Start(launch); return;
        }
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }
    public static void Reveal(string path)
    {
        if (Directory.Exists(path)) { Open(path); return; }
        Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = "/select,\"" + path + "\"", UseShellExecute = true });
    }
    public static void RunShell(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        var info = new ProcessStartInfo("cmd.exe") { UseShellExecute = true };
        info.ArgumentList.Add("/d"); info.ArgumentList.Add("/k"); info.ArgumentList.Add(command); Process.Start(info);
    }
    public static void Command(string name)
    {
        switch (name.ToLowerInvariant())
        {
            case "lock": case "блокировка": Native.LockWorkStation(); break;
            case "theme": case "тема": ToggleSystemTheme(); break;
            case "wifi": case "wi-fi": Open("ms-settings:network-wifi"); break;
            case "bluetooth": Open("ms-settings:bluetooth"); break;
            case "vpn": Open("ms-settings:network-vpn"); break;
            case "sound": case "звук": Open("ms-settings:sound"); break;
            default: Open("ms-settings:"); break;
        }
    }
    public static bool IsSystemLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return (key?.GetValue("AppsUseLightTheme") as int? ?? 0) == 1;
    }
    public static void ToggleSystemTheme()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var next = IsSystemLight() ? 0 : 1; key.SetValue("AppsUseLightTheme", next); key.SetValue("SystemUsesLightTheme", next);
    }
    public IReadOnlyList<(string Id, string Name)> AudioDevices(DataFlow flow)
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
        var result = new List<(string, string)>();
        foreach (var device in devices) { using (device) result.Add((device.ID, device.FriendlyName)); }
        return result;
    }
    private MMDevice Device(DataFlow flow)
    {
        using var enumerator = new MMDeviceEnumerator();
        var id = flow == DataFlow.Render ? state.Data.Settings.AudioOutputId : state.Data.Settings.MicrophoneId;
        return string.IsNullOrWhiteSpace(id) ? enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia) : enumerator.GetDevice(id);
    }
    public float Volume
    {
        get { try { using var device = Device(DataFlow.Render); return device.AudioEndpointVolume.MasterVolumeLevelScalar; } catch { return 0; } }
        set { using var device = Device(DataFlow.Render); device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(value, 0, 1); }
    }
    public bool MicrophoneMuted { get { try { using var device = Device(DataFlow.Capture); return device.AudioEndpointVolume.Mute; } catch { return false; } } }
    public void ToggleMicrophone() { using var device = Device(DataFlow.Capture); device.AudioEndpointVolume.Mute = !device.AudioEndpointVolume.Mute; }
    public static string NetworkSummary()
    {
        var adapters = NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType != NetworkInterfaceType.Loopback).ToList();
        if (adapters.Count == 0) return "Нет подключения";
        return string.Join(" · ", adapters.Take(3).Select(x => x.Name));
    }
    public static IReadOnlyList<SearchResult> DiscoverApplications()
    {
        var paths = new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) };
        var shortcuts = new Dictionary<string, SearchResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in paths)
        {
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var path in Directory.EnumerateFiles(folder, "*", options).Where(x => Path.GetExtension(x).ToLowerInvariant() is ".lnk" or ".url" or ".appref-ms"))
                {
                    var name = Path.GetFileNameWithoutExtension(path);
                    shortcuts.TryAdd(name, new SearchResult(name, path, "Приложение"));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Write("Apps", e); }
        }
        var applications = ShellApplications.Discover();
        var packagedNames = applications.Where(app => app.IsPackaged).Select(app => app.Result.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var desktopNames = applications.Where(app => !app.IsPackaged).Select(app => app.Result.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var results = new List<SearchResult>();
        foreach (var app in applications)
        {
            // A desktop shortcut preserves its arguments, run-as-admin and reveal
            // actions. Packaged apps retain their shell activation identity.
            results.Add(!app.IsPackaged && shortcuts.TryGetValue(app.Result.Name, out var shortcut) ? shortcut : app.Result);
        }
        results.AddRange(shortcuts.Values.Where(shortcut => !packagedNames.Contains(shortcut.Name) || desktopNames.Contains(shortcut.Name)));
        return results.DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
    public static void AutoStart(bool enabled)
    {
        const string run = @"Software\Microsoft\Windows\CurrentVersion\Run";
        if (!enabled) { using var existing = Registry.CurrentUser.OpenSubKey(run, writable: true); existing?.DeleteValue("QuietIsland", false); return; }
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Не удалось определить путь к Quiet Island.");
        using var key = Registry.CurrentUser.CreateSubKey(run);
        key.SetValue("QuietIsland", "\"" + executable + "\"", RegistryValueKind.String);
    }
    public static bool AutoStartEnabled() { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); return key?.GetValue("QuietIsland") is string value && !string.IsNullOrWhiteSpace(value); }
}
