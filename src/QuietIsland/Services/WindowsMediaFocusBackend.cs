using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using QuietIsland.Core;
using Windows.Media.Control;

namespace QuietIsland.Services;

/// <summary>Reads transport sessions and sustained player/browser audio across active outputs.</summary>
public sealed class WindowsMediaFocusBackend(MediaService media) : IMediaFocusBackend
{
    private readonly Dictionary<string, GlobalSystemMediaTransportControlsSession> controls = [];
    private readonly Dictionary<string, MMDevice> outputs = [];
    private readonly Dictionary<uint, (string Name, string AppId, DateTime Until)> apps = [];
    private readonly Dictionary<string, FocusAudio> audible = [];
    private readonly AudioActivityFilter audioActivity = new();
    private DateTime nextOutputs;
    public FocusSnapshot ReadSnapshot(bool includeAudio)
    {
        var targets = media.FocusTargets(); controls.Clear();
        var playback = new List<FocusPlayback>();
        foreach (var target in targets)
        {
            try
            {
                var info = target.Session.GetPlaybackInfo(); controls[target.Id] = target.Session;
                playback.Add(new(target.Id, target.Session.SourceAppUserModelId, info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing, info.Controls.IsPauseEnabled, info.Controls.IsPlayEnabled));
            }
            catch (Exception e) when (e is COMException or InvalidOperationException) { }
        }
        var current = media.CurrentSystemFocusTargetId;
        if (!includeAudio) { ReleaseAudio(); return new(playback, [], current); }
        ReadAudio();
        return MergeAudio(playback, audible.Values, current);
    }
    private static FocusSnapshot MergeAudio(List<FocusPlayback> playback, IEnumerable<FocusAudio> audible, string? current)
    {
        var audioOnly = new List<FocusAudio>();
        foreach (var source in audible)
        {
            var matches = Enumerable.Range(0, playback.Count).Where(i => Matches(playback[i].App, source.App)).ToArray();
            // A process meter cannot identify a paused browser tab. Never turn
            // ambiguous app-wide audibility into a playback start for that tab.
            var selected = matches.Length == 1 ? matches : matches.Where(i => playback[i].IsPlaying).ToArray();
            foreach (var index in selected) playback[index] = playback[index] with { IsAudible = true };
            if (selected.Length == 0) audioOnly.Add(source);
        }
        return new(playback, audioOnly, current);
    }
    private static string Canonical(string app) => new(app.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static bool Matches(string source, string audioApp)
    {
        var sourceApp = MediaApplicationPolicy.CanonicalId(source);
        var nativeApp = MediaApplicationPolicy.CanonicalId(audioApp);
        if (sourceApp != null && nativeApp != null) return sourceApp == nativeApp;
        var sourceKey = Canonical(source);
        return audioApp.Split('|', StringSplitOptions.RemoveEmptyEntries).Any(part =>
        {
            var key = Canonical(part);
            return key.Length > 0 && sourceKey.Contains(key, StringComparison.Ordinal);
        });
    }
    private void ReadAudio()
    {
        var now = DateTime.UtcNow;
        var heard = new HashSet<string>();
        if (now >= nextOutputs)
        {
            nextOutputs = now.AddSeconds(3);
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var active = new HashSet<string>();
                foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    var id = device.ID; active.Add(id);
                    if (!outputs.TryAdd(id, device)) device.Dispose();
                }
                foreach (var id in outputs.Keys.Where(id => !active.Contains(id)).ToArray()) { outputs[id].Dispose(); outputs.Remove(id); }
            }
            catch (Exception e) when (e is COMException or InvalidOperationException) { }
        }
        foreach (var output in outputs.Values.ToArray())
        {
            try
            {
                if (output.AudioMeterInformation.MasterPeakValue < .001f) continue;
                var manager = output.AudioSessionManager; manager.RefreshSessions();
                var sessions = manager.Sessions;
                for (var i = 0; i < sessions.Count; i++)
                {
                    using var session = sessions[i];
                    try
                    {
                        var pid = session.GetProcessID;
                        if (pid == 0 || pid == Environment.ProcessId || session.IsSystemSoundsSession || session.State != AudioSessionState.AudioSessionStateActive || session.SimpleAudioVolume.Mute || session.AudioMeterInformation.MasterPeakValue < .003f) continue;
                        var app = ReadApp(pid, now);
                        if (app.Length == 0 || !MediaApplicationPolicy.AllowsNativeAudio(app)) continue;
                        // Group a process's streams and outputs into one source.
                        var id = "audio:" + pid;
                        heard.Add(id); audible[id] = new FocusAudio(id, app, true);
                    }
                    catch (Exception e) when (e is COMException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
                }
            }
            catch (Exception e) when (e is COMException or InvalidOperationException) { outputs.Remove(output.ID); output.Dispose(); }
        }
        // Transport Playing above remains immediate. Only the native-meter
        // fallback waits for sustained sound, so short notifications cannot steal focus.
        var activeSources = audioActivity.Update(heard, now);
        foreach (var id in audible.Keys.Where(id => !activeSources.Contains(id)).ToArray()) audible.Remove(id);
        foreach (var pid in apps.Where(x => x.Value.Until <= now).Select(x => x.Key).ToArray()) apps.Remove(pid);
    }
    private string ReadApp(uint pid, DateTime now)
    {
        if (!apps.TryGetValue(pid, out var app))
        {
            using var process = Process.GetProcessById((int)pid);
            var id = new StringBuilder(512); var length = (uint)id.Capacity;
            var handle = OpenProcess(0x1000, false, pid); var appId = "";
            if (handle != IntPtr.Zero)
            {
                try { if (GetApplicationUserModelId(handle, ref length, id) == 0) appId = id.ToString(); }
                finally { CloseHandle(handle); }
            }
            app = (process.ProcessName, appId, now.AddSeconds(4)); apps[pid] = app;
        }
        return app.Name + (app.AppId.Length > 0 ? "|" + app.AppId : "");
    }
    public async Task<bool> SetPausedAsync(string id, bool paused)
    {
        if (!controls.TryGetValue(id, out var target)) return false;
        var info = target.GetPlaybackInfo();
        return paused ? info.Controls.IsPauseEnabled && await target.TryPauseAsync() : info.Controls.IsPlayEnabled && await target.TryPlayAsync();
    }
    private void ReleaseAudio() { foreach (var output in outputs.Values) output.Dispose(); outputs.Clear(); audible.Clear(); audioActivity.Reset(); apps.Clear(); nextOutputs = default; }
    public void Dispose() { ReleaseAudio(); controls.Clear(); }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetApplicationUserModelId(IntPtr process, ref uint length, StringBuilder id);
}
