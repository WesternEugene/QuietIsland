using QuietIsland.Models;

namespace QuietIsland.Services;

public sealed class IslandContext : IDisposable
{
    public DesktopState State { get; }
    public AppSettings Settings => State.Data.Settings;
    public EverythingService Everything { get; } = new();
    public MediaService Media { get; } = new();
    public LyricsService Lyrics { get; }
    public ClipboardService Clipboard { get; }
    public SystemService System { get; }
    public DeviceControlService Devices { get; } = new();
    public HappService Happ { get; } = new();
    public ActivityService Activity { get; } = new();
    public NotificationService Notifications { get; } = new();
    public AiService Ai { get; }
    public WeatherService Weather { get; }
    public AgentBridge Agents { get; }
    public CaptureService Capture { get; }
    public RecordingService Recording { get; }
    public SpeechService Speech { get; }
    public ObsService Obs { get; }
    public IReadOnlyList<SearchResult> Applications { get; private set; } = [];
    public IntPtr PasteTarget { get; set; }
    public string AiDraft { get; set; } = "";
    public event Action<string>? Toast;
    public event Action<string, bool>? Navigate;
    public event Action? CollapseRequested;
    public IslandContext(string? dataDirectory = null, LyricsService? lyrics = null)
    {
        State = new(dataDirectory); Lyrics = lyrics ?? new(); Clipboard = new(State); System = new(State); Ai = new(State); Weather = new(State); Agents = new(State);
        Capture = new(State); Recording = new(State); Speech = new(State); Obs = new(State);
    }
    public async Task StartAsync()
    {
        Activity.StartInputObserver();
        Applications = await Task.Run(SystemService.DiscoverApplications);
        await Media.StartAsync();
        await Task.WhenAll(Devices.RefreshAsync(), Happ.RefreshAsync());
        if (Settings.AudioOutputId.Length > 0) Media.StartAudio(Settings.AudioOutputId);
        Agents.Start(); Obs.Start(); _ = Weather.RefreshAsync();
    }
    public void Notify(string message) => Toast?.Invoke(message);
    public void Error(Exception error) { Log.Write("Action", error); Notify(error is InvalidOperationException or NotSupportedException ? error.Message : "Не удалось выполнить действие: " + error.GetType().Name); }
    public void Go(string tab, bool focus = true) => Navigate?.Invoke(tab, focus);
    public void Collapse() => CollapseRequested?.Invoke();
    public async Task RunSystemCommandAsync(string command)
    {
        try
        {
            if (command is "wifi" or "wi-fi") { await Devices.SetWifiAsync(!Devices.Current.WifiOn); Notify(Devices.Current.WifiOn ? "Wi-Fi включён" : "Wi-Fi выключен"); }
            else if (command == "bluetooth") { await Devices.SetBluetoothAsync(!Devices.Current.BluetoothOn); Notify(Devices.Current.BluetoothOn ? "Bluetooth включён" : "Bluetooth выключен"); }
            else if (command is "happ" or "vpn") { await Happ.ToggleAsync(); Notify(Happ.Current.Detail); }
            else SystemService.Command(command);
        }
        catch (Exception error) { Error(error); }
    }
    public void StartTimer(TimeSpan duration)
    {
        State.Data.TimerDuration = State.Data.TimerRemaining = duration.TotalSeconds;
        State.Data.TimerEnd = DateTimeOffset.Now.Add(duration); State.Data.TimerPaused = false; State.Touch(); Notify("Таймер запущен · " + Controls.Ui.Time(duration.TotalSeconds));
    }
    public void Dispose() { State.Save(); Everything.Dispose(); Media.Dispose(); Lyrics.Dispose(); Devices.Dispose(); Happ.Dispose(); Activity.Dispose(); Agents.Dispose(); Recording.Dispose(); Speech.Dispose(); Obs.Dispose(); }
}
