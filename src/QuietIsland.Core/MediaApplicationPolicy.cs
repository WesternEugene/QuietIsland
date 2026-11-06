namespace QuietIsland.Core;

/// <summary>Only playback in recognized media applications participates in automatic audio focus.</summary>
public static class MediaApplicationPolicy
{
    private static readonly HashSet<string> Executables = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "chromium", "msedge", "firefox", "opera", "opera_gx", "brave", "vivaldi", "browser", "yandex",
        "spotify", "vlc", "wmplayer", "music.ui", "video.ui", "windowsmediaplayer", "mediaplayer",
        "aimp", "aimp3", "aimp4", "aimp5", "foobar2000", "musicbee", "mpv", "mpc-hc", "mpc-hc64", "mpc-be", "mpc-be64",
        "potplayer", "potplayermini", "potplayermini64", "itunes", "applemusic", "deezer", "tidal", "yandexmusic",
        "telegram", "telegramdesktop"
    };
    private static readonly (string Id, string Process)[] ApplicationIds =
    [
        ("Chrome", "chrome"), ("Chromium", "chromium"), ("MSEdge", "msedge"), ("Microsoft.MicrosoftEdge", "msedge"), ("MicrosoftEdge", "msedge"),
        ("Mozilla.Firefox", "firefox"), ("Firefox", "firefox"), ("Opera", "opera"), ("OperaStable", "opera"), ("OperaGXStable", "opera"),
        ("Brave", "brave"), ("BraveSoftware.Brave-Browser", "brave"), ("Vivaldi", "vivaldi"), ("Yandex.Browser", "browser"),
        ("SpotifyAB.SpotifyMusic", "spotify"), ("Microsoft.ZuneMusic", "music.ui"), ("Microsoft.ZuneVideo", "video.ui"), ("Microsoft.MediaPlayer", "music.ui"),
        ("AppleInc.AppleMusicWin", "applemusic"), ("AppleInc.iTunes", "itunes"), ("VideoLAN.VLC", "vlc"), ("com.squirrel.TIDAL.TIDAL", "tidal"), ("Yandex.Music", "yandexmusic"),
        ("Telegram.TelegramDesktop", "telegram"), ("TelegramMessengerLLP.TelegramDesktop", "telegram"), ("Telegram.FZ-LLC.TelegramDesktop", "telegram"), ("org.telegram.desktop", "telegram")
    ];

    public static bool IsMediaApplication(string app) => CanonicalId(app) != null;
    public static bool IsTelegram(string app) => CanonicalId(app) == "telegram";
    // Telegram publishes voice/song/video playback through SMTC. Its process
    // audio also carries notifications/calls, so it must never serve as fallback.
    public static bool AllowsNativeAudio(string app) => CanonicalId(app) is { } id && id != "telegram";

    public static string? CanonicalId(string app)
    {
        // WASAPI app values start with the real executable, followed by an optional
        // package identity. Never let a secondary label promote a chat or game.
        var name = app.Split('|', 2)[0].Trim().Trim('"').Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var executable = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        if (executable) name = name[..^4];
        if (Executables.Contains(name)) return name.ToLowerInvariant() switch
        {
            "opera_gx" => "opera", "yandex" => "browser", "windowsmediaplayer" or "mediaplayer" => "music.ui",
            "aimp3" or "aimp4" or "aimp5" => "aimp", "mpc-hc64" => "mpc-hc", "mpc-be64" => "mpc-be",
            "potplayermini" or "potplayermini64" => "potplayer", "telegramdesktop" => "telegram", var id => id
        };
        if (executable) return null;
        // Firefox's standard Windows installation advertises this opaque SMTC ID.
        if (name.Equals("308046B0AF4A39CB", StringComparison.OrdinalIgnoreCase)) return "firefox";
        foreach (var (id, process) in ApplicationIds)
            if (name.Equals(id, StringComparison.OrdinalIgnoreCase)
                || name.Length > id.Length && name.StartsWith(id, StringComparison.OrdinalIgnoreCase) && name[id.Length] is '.' or '_' or '!') return process;
        return null;
    }
}
