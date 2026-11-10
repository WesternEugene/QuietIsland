using QuietIsland.Core;

namespace QuietIsland.Models;

public class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public double Scale { get; set; } = 1;
    public double PillOpacity { get; set; } = 1;
    public double PanelOpacity { get; set; } = 1;
    public double HoverCloseDelayMs { get; set; } = 220;
    public double HoverOpenDelayMs { get; set; } = 320;
    public double CompactMinWidth { get; set; } = 190;
    public double CompactMaxWidth { get; set; } = 700;
    public double CompactHeight { get; set; } = 52;
    public double CompactFontSize { get; set; } = 13;
    public double CompactCornerRadius { get; set; } = 18;
    public double ExpandedCornerRadius { get; set; } = 24;
    public double ShoulderRadius { get; set; } = 17;
    public double AnimationSpeed { get; set; } = 1;
    public bool MusicAnimationEnabled { get; set; } = true;
    public double MusicSensitivity { get; set; } = 1;
    public double MusicAmplitude { get; set; } = 7;
    public bool ShowMusicCover { get; set; } = true;
    public bool ShowSongTitle { get; set; } = true;
    public bool ShowInputStatus { get; set; } = true;
    public bool ShowWeather { get; set; } = true;
    public bool ClockSeconds { get; set; }
    public bool Clock12Hour { get; set; }
    public List<string> HeaderTabs { get; set; } = ["home", "clipboard", "shelf", "tools", "ai", "notes", "weather", "control"];
    public bool HeaderLabels { get; set; } = true;
    public bool HeaderClock { get; set; } = true;
    public bool HeaderDictation { get; set; } = true;
    public bool HeaderPin { get; set; } = true;
    public bool HeaderClose { get; set; } = true;
    public bool AutoReduceResources { get; set; } = true;
    public string ResourceHeavyApps { get; set; } = "";
    public bool UseAlbumAccent { get; set; } = true;
    public string AccentColor { get; set; } = "#FF8B81";
    public string SurfaceColor { get; set; } = "#000000";
    public int Monitor { get; set; }
    public List<string> IslandDisplays { get; set; } = [];
    public bool PauseMediaForTimer { get; set; } = true;
    public bool PauseMediaForReminder { get; set; } = true;
    public bool PauseMediaForOtherAudio { get; set; } = true;
    public int MediaFocusRevision { get; set; }
    public bool ResumeInterruptedMedia { get; set; } = true;
    public double MediaResumeDelayMs { get; set; } = 1000;
    public string AudioInterruptApps { get; set; } = "";
    public double TopOffset { get; set; }
    public int AppearanceRevision { get; set; }
    public bool AutoHide { get; set; }
    public bool HideFullscreen { get; set; } = true;
    public bool ClickThrough { get; set; }
    public bool ClipboardEnabled { get; set; } = true;
    public int ClipboardLimit { get; set; } = 150;
    public bool ClipboardAutoPaste { get; set; } = true;
    public bool HideInCapture { get; set; } = true;
    public bool HotkeyAltSpace { get; set; } = true;
    public Dictionary<string, string>? GlobalHotkeys { get; set; }
    public bool AutomaticUpdateChecks { get; set; } = true;
    public string ExcludedApps { get; set; } = "";
    public string AudioOutputId { get; set; } = "";
    public string MicrophoneId { get; set; } = "";
    public string CaptureFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Quiet Island");
    public string FfmpegPath { get; set; } = "ffmpeg";
    public int RecordingFps { get; set; } = 30;
    public bool RecordingHighQuality { get; set; } = true;
    public bool RecordSystemAudio { get; set; } = true;
    public bool RecordCursor { get; set; } = true;
    public string AiEndpoint { get; set; } = "http://localhost:11434/v1";
    public string AiModel { get; set; } = "";
    public string AiKeyEncrypted { get; set; } = "";
    public string AiSystemPrompt { get; set; } = "Ты полезный помощник. Отвечай на русском языке, если пользователь не просит другой язык.";
    public string SpeechEndpoint { get; set; } = "";
    public string SpeechModel { get; set; } = "whisper-1";
    public string SpeechKeyEncrypted { get; set; } = "";
    public bool SpeechPreferLocal { get; set; }
    public string WeatherCity { get; set; } = "";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool BridgeEnabled { get; set; } = true;
    public int BridgePort { get; set; } = 47832;
    public bool ObsEnabled { get; set; }
    public int ObsPort { get; set; } = 4455;
    public string ObsPasswordEncrypted { get; set; } = "";
    public void NormalizeAppearance()
    {
        string[] tabs = ["home", "clipboard", "shelf", "tools", "ai", "notes", "weather", "control"];
        HeaderTabs = (HeaderTabs ?? []).Where(tabs.Contains).Distinct().ToList();
        static double Limit(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
        Scale = Limit(Scale, .8, 2, 1); PillOpacity = Limit(PillOpacity, .45, 1, 1); PanelOpacity = Limit(PanelOpacity, .45, 1, 1);
        HoverCloseDelayMs = Limit(HoverCloseDelayMs, 80, 2000, 220);
        HoverOpenDelayMs = Limit(HoverOpenDelayMs, 0, 5000, 320);
        MediaResumeDelayMs = Limit(MediaResumeDelayMs, 0, 10000, 1000);
        AudioInterruptApps ??= "";
        if (MediaFocusRevision < 1)
        {
            // The first 1.3.0 build shipped this disabled, Telegram-only default.
            if (!PauseMediaForOtherAudio && AudioInterruptApps.Equals("Telegram", StringComparison.OrdinalIgnoreCase))
            { PauseMediaForOtherAudio = true; AudioInterruptApps = ""; }
            MediaFocusRevision = 1;
        }
        IslandDisplays = (IslandDisplays ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        CompactMinWidth = Limit(CompactMinWidth, 160, 360, 190); CompactMaxWidth = Limit(CompactMaxWidth, CompactMinWidth, 700, 700);
        CompactHeight = Limit(CompactHeight, 44, 72, 52); CompactFontSize = Limit(CompactFontSize, 10, 16, 13);
        CompactCornerRadius = Limit(CompactCornerRadius, 0, 24, 18); ExpandedCornerRadius = Limit(ExpandedCornerRadius, 0, 36, 24); ShoulderRadius = Limit(ShoulderRadius, 8, 28, 17);
        AnimationSpeed = Limit(AnimationSpeed, .6, 1.8, 1); MusicSensitivity = Limit(MusicSensitivity, .25, 3, 1); MusicAmplitude = Limit(MusicAmplitude, 0, 12, 7);
        static string Hex(string? value, string fallback) => value is not null && System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$") ? value.ToUpperInvariant() : fallback;
        AccentColor = Hex(AccentColor, "#FF8B81"); SurfaceColor = Hex(SurfaceColor, "#000000");
    }
}

public class AppState
{
    public AppSettings Settings { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public List<ClipEntry> Clipboard { get; set; } = [];
    // Ordinary history is session-only; pins have an explicit durable representation.
    public List<ClipEntry> PinnedClipboard
    {
        get => Clipboard.Where(x => x.Pinned).ToList();
        set { Clipboard.RemoveAll(x => x.Pinned); Clipboard.AddRange((value ?? []).Where(x => x.Pinned)); }
    }
    public List<string> Shelf { get; set; } = [];
    public List<Note> Notes { get; set; } = [new() { Title = "Мои заметки", Text = "" }];
    public List<Reminder> Reminders { get; set; } = [];
    public List<ChatMessage> Chat { get; set; } = [];
    public List<LaunchItem> Launcher { get; set; } = [];
    public bool LauncherDefaultsInitialized { get; set; }
    public List<string> Colors { get; set; } = [];
    public DateTimeOffset? TimerEnd { get; set; }
    public bool TimerPaused { get; set; }
    public double TimerRemaining { get; set; }
    public double TimerDuration { get; set; }
}
public class ClipEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "text";
    public string Text { get; set; } = "";
    public string? ImagePath { get; set; }
    public List<string> Files { get; set; } = [];
    public bool Pinned { get; set; }
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public string Preview => Kind switch { "image" => "Изображение", "files" => string.Join(", ", Files.Select(Path.GetFileName)), _ => Text };
}
public class Note
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Новая заметка";
    public string Folder { get; set; } = "Личное";
    public string Text { get; set; } = "";
    public string Rtf { get; set; } = "";
    public DateTimeOffset Modified { get; set; } = DateTimeOffset.Now;
}
public class ChatMessage
{
    public string Role { get; set; } = "user";
    public string Text { get; set; } = "";
    public string DisplayText { get; set; } = "";
    public List<string> Images { get; set; } = [];
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
}
public class LaunchItem
{
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
    public string Group { get; set; } = "";
    public string Hotkey { get; set; } = "";
}
public record SearchResult(string Name, string Path, string Kind, long Size = -1, string? Detail = null);
public record MediaSnapshot(string Title, string Artist, string Album, bool IsPlaying, string Source, double Position, double Duration, System.Windows.Media.Imaging.BitmapSource? Cover = null, bool IsMusic = true)
{
    public string? NativeArtist { get; init; }
    public bool HasMedia => !string.IsNullOrWhiteSpace(Title);
    public static MediaSnapshot Empty { get; } = new("", "", "", false, "", 0, 0);
}
