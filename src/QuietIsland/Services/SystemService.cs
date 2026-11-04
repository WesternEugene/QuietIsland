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
        return enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active).Select(x => (x.ID, x.FriendlyName)).ToList();
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
        var results = new List<SearchResult>();
        foreach (var folder in paths)
        {
            try { results.AddRange(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(x => Path.GetExtension(x) is ".lnk" or ".url").Select(x => new SearchResult(Path.GetFileNameWithoutExtension(x), x, "Приложение"))); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Write("Apps", e); }
        }
        return results.DistinctBy(x => x.Name, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Name).ToList();
    }
    public static void AutoStart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("QuietIsland", "\"" + Environment.ProcessPath + "\""); else key.DeleteValue("QuietIsland", false);
    }
    public static bool AutoStartEnabled() { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); return key?.GetValue("QuietIsland") != null; }
}
