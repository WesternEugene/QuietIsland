using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using QuietIsland.Core;

namespace QuietIsland.Services;

internal static class SettingsLanguage
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Localized = new(Load);
    private static readonly Dictionary<string, string> PageIds = new()
    {
        ["display"] = "PCSystemDisplay", ["sound"] = "Audio", ["bluetooth"] = "Devices", ["network-wifi"] = "NetworkWiFi",
        ["network-status"] = "GroupNetwork", ["network-vpn"] = "NetworkVPN", ["network-proxy"] = "NetworkProxy",
        ["notifications"] = "AppsNotifications", ["powersleep"] = "PowerAndBattery", ["storagesense"] = "StorageSense",
        ["appsfeatures"] = "InstalledApps", ["defaultapps"] = "AppsDefaults", ["startupapps"] = "StartupApps",
        ["personalization-background"] = "Background", ["colors"] = "Colors", ["taskbar"] = "Taskbar", ["yourinfo"] = "AccountsPicture",
        ["signinoptions"] = "SignInOptions", ["dateandtime"] = "TimeDate", ["regionlanguage"] = "RegionLanguage",
        ["privacy-microphone"] = "PrivacyMicrophone", ["privacy-webcam"] = "PrivacyWebcam", ["clipboard"] = "Clipboard",
        ["easeofaccess"] = "EaseOfAccess", ["recovery"] = "Recovery", ["windowsdefender"] = "WindowsDefender",
        ["windowsupdate"] = "WindowsUpdate", ["about"] = "About"
    };
    private static CultureInfo SystemLanguage
    {
        get { try { return CultureInfo.GetCultureInfo(GetUserDefaultUILanguage()); } catch (CultureNotFoundException) { return CultureInfo.CurrentUICulture; } }
    }
    internal static string Name(WindowsSettingsPage page) => Localized.Value.TryGetValue(page.Target, out var native) ? native : SystemLanguage.TwoLetterISOLanguageName == "ru" ? page.Name : page.EnglishName;
    internal static IReadOnlyList<WindowsSettingsPage> Search(string query)
    {
        var known = WindowsSettingsCatalog.Search(query);
        if (string.IsNullOrWhiteSpace(query)) return known;
        return WindowsSettingsCatalog.Search("").Where(x => Name(x).Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase))
            .Concat(known).DistinctBy(x => x.Target).ToArray();
    }
    private static IReadOnlyDictionary<string, string> Load()
    {
        var result = new Dictionary<string, string>();
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "ImmersiveControlPanel", "Settings");
            var path = Directory.EnumerateFiles(folder, "AllSystemSettings_*.xml").FirstOrDefault();
            if (path == null) return result;
            var descriptions = XDocument.Load(path).Descendants("SearchableContent").Where(x => x.Element("Filename") != null)
                .GroupBy(x => (string)x.Element("Filename")!).ToDictionary(x => x.Key, x => (string?)x.First().Element("SettingInformation")?.Element("Description"));
            foreach (var pair in PageIds)
            {
                if (!descriptions.TryGetValue("AAA_SettingsPage" + pair.Value, out var resource) || string.IsNullOrEmpty(resource)) continue;
                var value = new StringBuilder(1024);
                if (SHLoadIndirectString(resource, value, value.Capacity, IntPtr.Zero) >= 0 && value.Length > 0) result["ms-settings:" + pair.Key] = value.ToString();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException) { }
        return result;
    }
    [DllImport("kernel32.dll")] private static extern ushort GetUserDefaultUILanguage();
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int SHLoadIndirectString(string resource, StringBuilder result, int capacity, IntPtr reserved);
}
