using System.Text.Json;

namespace QuietIsland.Core;

public sealed record AppRelease(Version Version, string Tag, Uri Page, string Notes,
    string? InstallerName = null, Uri? InstallerUrl = null, long Size = 0, string? Sha256 = null)
{
    public const string Repository = "WesternEugene/QuietIsland";
    public string DisplayVersion => Version.Revision > 0 ? Version.ToString(4) : Version.ToString(3);
    public bool CanInstall => InstallerName != null && InstallerUrl != null && Size > 0;

    public static AppRelease? Parse(string json, Version current, bool currentIsPrerelease = false)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || Flag(root, "draft") || Flag(root, "prerelease")) return null;
        var tag = Text(root, "tag_name");
        if (!System.Version.TryParse(tag.TrimStart('v', 'V'), out var parsed)) return null;
        var version = Normalize(parsed);
        if (version < Normalize(current) || version == Normalize(current) && !currentIsPrerelease) return null;
        var page = new Uri($"https://github.com/{Repository}/releases/tag/{Uri.EscapeDataString(tag)}");
        var release = new AppRelease(version, tag, page, Text(root, "body"));
        var expected = $"QuietIsland-{release.DisplayVersion}-Setup-win-x64.exe";
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return release;
        foreach (var asset in assets.EnumerateArray())
        {
            if (Text(asset, "name") != expected || !asset.TryGetProperty("size", out var size) || !size.TryGetInt64(out var bytes) || bytes is <= 0 or > 2_147_483_648) continue;
            if (!Uri.TryCreate(Text(asset, "browser_download_url"), UriKind.Absolute, out var url) || !IsInstallerUrl(url, tag, expected)) continue;
            var digest = Text(asset, "digest");
            string? sha256 = null;
            if (digest.Length > 0)
            {
                if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || digest.Length != 71 || !digest.AsSpan(7).ToString().All(Uri.IsHexDigit)) continue;
                sha256 = digest[7..];
            }
            return release with { InstallerName = expected, InstallerUrl = url, Size = bytes, Sha256 = sha256 };
        }
        return release;
    }

    public static bool IsInstallerUrl(Uri url, string tag, string name) => url.Scheme == Uri.UriSchemeHttps
        && url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && url.IsDefaultPort && url.UserInfo.Length == 0
        && url.Query.Length == 0 && url.Fragment.Length == 0
        && Uri.UnescapeDataString(url.AbsolutePath).Equals($"/{Repository}/releases/download/{tag}/{name}", StringComparison.Ordinal);
    private static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
    private static string Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static bool Flag(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
