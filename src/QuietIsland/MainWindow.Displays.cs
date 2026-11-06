using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using QuietIsland.Services;
using QuietIsland.Views;

namespace QuietIsland;

public partial class MainWindow
{
    private MainWindow? mirrorOwner;
    private string? mirrorDevice;
    private readonly Dictionary<string, MainWindow> mirrors = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Action> subscriptions = [];
    private void SubscribeContext()
    {
        c.Navigate += ShowPanel; subscriptions.Add(() => c.Navigate -= ShowPanel);
        c.CollapseRequested += Collapse; subscriptions.Add(() => c.CollapseRequested -= Collapse);
        c.Updates.Changed += UpdatesChanged; subscriptions.Add(() => c.Updates.Changed -= UpdatesChanged);
        void OnToast(string message) => Dispatcher.InvokeAsync(() => { if (IsClosing) return; toast = message; toastUntil = DateTime.Now.AddSeconds(5); Status.Text = message; UpdateCompact(); });
        c.Toast += OnToast; subscriptions.Add(() => c.Toast -= OnToast);
        void OnMedia() => Dispatcher.InvokeAsync(() =>
        {
            if (IsClosing) return;
            if (mirrorOwner == null) c.Lyrics.UpdateTrack(c.Media.Current);
            Fluid.Update(c.Media); RefreshRuntime(runtimeHidden); UpdateCompact();
            if (view is HomeView) view.Refresh();
        });
        c.Media.Changed += OnMedia; subscriptions.Add(() => c.Media.Changed -= OnMedia);
        void OnAgents() => Dispatcher.InvokeAsync(() => { if (IsClosing) return; if (c.Agents.Broker.Pending.Count > 0) ShowPanel("activity", false); if (view is ActivityView) view.Refresh(); });
        c.Agents.Broker.Changed += OnAgents; subscriptions.Add(() => c.Agents.Broker.Changed -= OnAgents);
        void OnSpeech(string text) { if (mirrorOwner == null) Dispatcher.InvokeAsync(() => c.Notify(text)); }
        c.Speech.Partial += OnSpeech; subscriptions.Add(() => c.Speech.Partial -= OnSpeech);
        void OnRecording() => Dispatcher.InvokeAsync(() =>
        {
            if (IsClosing) return;
            if (mirrorOwner == null && c.Recording.LastError is { } error && error != lastRecordingError) { lastRecordingError = error; c.Notify(error); }
            if (c.Recording.LastError == null) lastRecordingError = null;
            UpdateCompact(); view?.Refresh();
        });
        c.Recording.Changed += OnRecording; subscriptions.Add(() => c.Recording.Changed -= OnRecording);
        if (!snapshot)
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplaysChanged;
            subscriptions.Add(() => Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplaysChanged);
        }
    }
    private void UnsubscribeContext() { foreach (var unsubscribe in subscriptions) unsubscribe(); subscriptions.Clear(); }
    private void DisplaysChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { if (!IsClosing) ApplySettings(); });
    private System.Windows.Forms.Screen ResolveScreen()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        var named = mirrorDevice ?? c.Settings.IslandDisplays.FirstOrDefault(id => screens.Any(x => x.DeviceName.Equals(id, StringComparison.OrdinalIgnoreCase)));
        return screens.FirstOrDefault(x => x.DeviceName.Equals(named, StringComparison.OrdinalIgnoreCase)) ?? screens[Math.Clamp(c.Settings.Monitor, 0, screens.Length - 1)];
    }
    private void ReconcileMirrors()
    {
        if (IsClosing || snapshot) return;
        var screens = System.Windows.Forms.Screen.AllScreens;
        var selected = c.Settings.IslandDisplays.Where(id => screens.Any(x => x.DeviceName.Equals(id, StringComparison.OrdinalIgnoreCase))).ToArray();
        var primary = ResolveScreen().DeviceName;
        var desired = selected.Where(id => !id.Equals(primary, StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in mirrors.Keys.Where(id => !desired.Contains(id)).ToArray()) { mirrors[id].Close(); mirrors.Remove(id); }
        foreach (var id in desired)
        {
            if (!mirrors.TryGetValue(id, out var mirror))
            {
                mirror = new MainWindow(c, true) { mirrorOwner = this, mirrorDevice = id, Owner = this };
                mirrors[id] = mirror; mirror.Show();
            }
            mirror.ApplySettings();
        }
    }
    private void CloseMirrors() { foreach (var mirror in mirrors.Values.ToArray()) mirror.Close(); mirrors.Clear(); }
    private void MirrorTick()
    {
        if (mirrorOwner == null || IsClosing) return;
        var changed = timerAlarm != mirrorOwner.timerAlarm || !reminderAlarms.SequenceEqual(mirrorOwner.reminderAlarms);
        timerAlarm = mirrorOwner.timerAlarm; reminderAlarms.Clear(); foreach (var reminder in mirrorOwner.reminderAlarms) reminderAlarms.Enqueue(reminder);
        if (changed) PresentAlarm();
        UpdateRuntimeVisibility(DateTime.Now); UpdateCompact();
    }
}
