using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using QuietIsland.Controls;

namespace QuietIsland.Services;

public record ActivityItem(string Icon, string Title, string Detail, DateTimeOffset Time);
public sealed class ActivityService : IDisposable
{
    private CapsLockObserver? caps;
    public void StartInputObserver()
    {
        if (caps != null) return;
        try { caps = new(); } catch (Exception e) { Log.Write("Caps Lock observer", e); }
    }
    private readonly Dictionary<string, (long Size, DateTime Time)> downloads = new();
    public List<ActivityItem> Items { get; } = [];
    public string Layout { get; private set; } = "RU";
    public bool CapsLock { get; private set; }
    public string PrivacyUse { get; private set; } = "";
    public string Vpn { get; private set; } = "";
    public void PollInputState()
    {
        var thread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
        var language = (ushort)(Native.GetKeyboardLayout(thread).ToInt64() & 0xFFFF);
        try { Layout = System.Globalization.CultureInfo.GetCultureInfo(language).TwoLetterISOLanguageName.ToUpperInvariant(); } catch { Layout = "--"; }
        CapsLock = caps?.Enabled ?? (Native.GetKeyState(0x14) & 1) == 1;
    }
    public void Poll()
    {
        PollInputState();
        var items = new List<ActivityItem>(); var now = DateTime.Now;
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            foreach (var path in Directory.EnumerateFiles(folder).Where(x => x.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".part", StringComparison.OrdinalIgnoreCase)))
            {
                var length = new FileInfo(path).Length;
                var speed = downloads.TryGetValue(path, out var old) ? Math.Max(0, length - old.Size) / Math.Max(.1, (now - old.Time).TotalSeconds) : 0;
                downloads[path] = (length, now);
                items.Add(new("↓", Path.GetFileNameWithoutExtension(path), Ui.Bytes(length) + " · " + Ui.Bytes((long)speed) + "/с", DateTimeOffset.Now));
            }
            foreach (var old in downloads.Keys.Where(x => !File.Exists(x)).ToArray()) downloads.Remove(old);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var steam = key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrEmpty(steam))
            {
                var libraries = new List<string> { steam };
                var config = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(config)) libraries.AddRange(Regex.Matches(File.ReadAllText(config), "\"path\"\\s*\"([^\"]+)\"").Cast<Match>().Select(x => x.Groups[1].Value.Replace("\\\\", "\\")));
                foreach (var library in libraries.Select(x => Path.TrimEndingDirectorySeparator(Path.GetFullPath(x))).Distinct(StringComparer.OrdinalIgnoreCase))
                    foreach (var manifest in Directory.EnumerateFiles(Path.Combine(library, "steamapps"), "appmanifest_*.acf"))
                    {
                        var text = File.ReadAllText(manifest);
                        var total = Value(text, "BytesToDownload"); var done = Value(text, "BytesDownloaded");
                        if (long.TryParse(total, out var t) && t > 0 && long.TryParse(done, out var d) && d < t)
                            items.Add(new("↓", Value(text, "name"), $"Steam · {d * 100d / t:0}% · {Ui.Bytes(d)} / {Ui.Bytes(t)}", DateTimeOffset.Now));
                    }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var uses = new List<string>();
        foreach (var device in new[] { "microphone", "webcam" })
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\" + device + @"\NonPackaged");
                if (key == null) continue;
                foreach (var name in key.GetSubKeyNames())
                {
                    using var app = key.OpenSubKey(name);
                    if (Convert.ToInt64(app?.GetValue("LastUsedTimeStop") ?? -1L) != 0) continue;
                    var appName = Path.GetFileNameWithoutExtension(name.Replace('#', '\\'));
                    uses.Add((device == "microphone" ? "Микрофон" : "Камера") + " · " + appName);
                }
            }
            catch { }
        }
        PrivacyUse = string.Join("; ", uses);
        if (uses.Count > 0) items.Add(new("●", "Микрофон / камера", PrivacyUse, DateTimeOffset.Now));
        Vpn = string.Join(", ", NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType is NetworkInterfaceType.Ppp or NetworkInterfaceType.Tunnel).Select(x => x.Name));
        if (Vpn.Length > 0) items.Add(new("◇", "VPN / туннель", Vpn, DateTimeOffset.Now));
        Items.Clear(); Items.AddRange(items);
    }
    private static string Value(string text, string key) => Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s*\"([^\"]*)\"").Groups[1].Value;
    public void Dispose() { caps?.Dispose(); caps = null; }
}
