using System.Windows.Threading;
using QuietIsland.Core;

namespace QuietIsland.Services;

public sealed class MediaFocusService : IDisposable
{
    private readonly IslandContext c;
    private readonly IMediaFocusBackend backend;
    private readonly TimeProvider clock;
    private readonly MediaFocusPolicy policy = new();
    private readonly MediaSourceArbiter sources = new();
    private readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
    private bool timerAlarm, reminderAlarm, alarmEpisode, timerEpisode, reminderEpisode, restartAlarm, busy, disposed, started, monitoringAudio;
    private int revision;
    private readonly Dictionary<string, int> manualRevisions = [];
    private readonly Dictionary<string, (int Attempts, DateTimeOffset Until)> resumeRetries = [];
    private string? winner, pendingResume;
    private DateTimeOffset pendingResumeUntil;
    public MediaFocusService(IslandContext context, IMediaFocusBackend? backend = null, TimeProvider? clock = null)
    {
        c = context; this.backend = backend ?? new WindowsMediaFocusBackend(c.Media); this.clock = clock ?? TimeProvider.System;
        timer.Tick += Tick; c.Media.ManualControl += ManualControl;
    }
    public void Start()
    {
        if (started || disposed) return;
        started = true; timer.Start();
    }
    public void SetAlarmState(bool timerActive, bool reminderActive, bool newAlarm = false)
    {
        timerAlarm = timerActive; reminderAlarm = reminderActive; restartAlarm |= newAlarm; _ = RefreshAsync();
    }
    private void Tick(object? sender, EventArgs e) => _ = RefreshAsync();
    private void ManualControl()
    {
        var id = c.Media.ManualFocusTargetId;
        if (id == null) { revision++; resumeRetries.Clear(); }
        else { manualRevisions[id] = manualRevisions.GetValueOrDefault(id) + 1; resumeRetries.Remove(id); }
        policy.Cancel(id);
        if (id == null || pendingResume == id) pendingResume = null;
    }
    public async Task RefreshAsync()
    {
        if (disposed || busy) return;
        var s = c.Settings;
        var timerInterruption = timerAlarm && s.PauseMediaForTimer;
        var reminderInterruption = reminderAlarm && s.PauseMediaForReminder;
        var alarm = timerInterruption || reminderInterruption;
        if (!alarm && !s.PauseMediaForOtherAudio && !policy.HasOwnership)
        {
            policy.Update([], false, s.ResumeInterruptedMedia, TimeSpan.Zero, DateTimeOffset.UtcNow);
            sources.Reset(); winner = pendingResume = null; alarmEpisode = timerEpisode = reminderEpisode = restartAlarm = false;
            if (monitoringAudio) { backend.ReadSnapshot(false); monitoringAudio = false; }
            return;
        }
        busy = true; var generation = revision;
        try
        {
            var now = clock.GetUtcNow();
            var snapshot = backend.ReadSnapshot(s.PauseMediaForOtherAudio); monitoringAudio = s.PauseMediaForOtherAudio;
            var filters = s.AudioInterruptApps.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            bool Allowed(string app)
            {
                var canonical = MediaApplicationPolicy.CanonicalId(app);
                return filters.Length == 0 || filters.Any(x => app.Contains(x.Replace(".exe", "", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase)
                    || canonical != null && (canonical.Equals(MediaApplicationPolicy.CanonicalId(x), StringComparison.OrdinalIgnoreCase)
                        || canonical.Contains(x.Replace(".exe", "", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase)));
            }
            var activity = snapshot.Playback.Where(x => Allowed(x.App) && MediaApplicationPolicy.IsMediaApplication(x.App))
                .Select(x => new MediaSourceState(x.Id, x.IsPlaying || !MediaApplicationPolicy.IsTelegram(x.App) && x.IsAudible, x.IsPlaying))
                .Concat(snapshot.Audio.Where(x => Allowed(x.App) && MediaApplicationPolicy.AllowsNativeAudio(x.App)).Select(x => new MediaSourceState(x.Id, x.IsActive))).ToArray();
            var active = s.PauseMediaForOtherAudio ? sources.Update(activity, snapshot.CurrentId, now) : null;
            if (pendingResume != null && (now >= pendingResumeUntil || snapshot.Playback.All(x => x.Id != pendingResume) || snapshot.Playback.Any(x => x.Id == pendingResume && x.IsPlaying))) pendingResume = null;
            active ??= pendingResume;
            // An alarm is its own interruption episode, even when the current
            // player has not changed since the user's last transport command.
            // Meter/transport changes during that alarm must not erase a manual override.
            var newAlarm = alarm && (restartAlarm || timerInterruption && !timerEpisode || reminderInterruption && !reminderEpisode);
            if (newAlarm || !alarm && (alarmEpisode || active != winner))
            {
                policy.BeginEpisode(newAlarm ? snapshot.Playback.Where(x => x.IsPlaying).Select(x => x.Id) : null);
                resumeRetries.Clear();
            }
            winner = active; alarmEpisode = alarm; timerEpisode = timerInterruption; reminderEpisode = reminderInterruption; restartAlarm = false;
            var resume = s.PauseMediaForOtherAudio ? sources.NextToResume(policy.OwnedIds) : null;
            var actions = policy.Update(snapshot.Playback.Select(x => new MediaFocusSession(x.Id, x.IsPlaying,
                x.CanPause && (alarm || MediaApplicationPolicy.IsMediaApplication(x.App) && active != null && active != x.Id),
                x.CanPlay && (!s.PauseMediaForOtherAudio || x.Id == resume) && (!resumeRetries.TryGetValue(x.Id, out var retry) || retry.Until <= now))), alarm || active != null, s.ResumeInterruptedMedia, TimeSpan.FromMilliseconds(s.MediaResumeDelayMs), now);
            var versions = actions.ToDictionary(x => x.Id, x => manualRevisions.GetValueOrDefault(x.Id));
            foreach (var action in actions)
            {
                if (disposed || generation != revision) return;
                if (manualRevisions.GetValueOrDefault(action.Id) != versions[action.Id]) { policy.Cancel(action.Id); continue; }
                var accepted = false;
                try { accepted = await backend.SetPausedAsync(action.Id, action.Pause); }
                catch (Exception error) { Log.Write("Media focus command", error); }
                if (disposed || generation != revision) return;
                if (manualRevisions.GetValueOrDefault(action.Id) != versions[action.Id]) { policy.Cancel(action.Id); continue; }
                if (action.Pause) policy.ConfirmPause(action.Id, accepted);
                else
                {
                    policy.ConfirmResume(action.Id, accepted);
                    if (accepted) { resumeRetries.Remove(action.Id); sources.ExpectResume(action.Id, now); pendingResume = action.Id; pendingResumeUntil = now.AddSeconds(3); }
                    else
                    {
                        var attempts = resumeRetries.GetValueOrDefault(action.Id).Attempts + 1;
                        resumeRetries[action.Id] = (attempts, attempts >= 3 ? DateTimeOffset.MaxValue : now.AddMilliseconds(500 * attempts));
                        if (attempts == 3) c.Notify("Плеер не принял продолжение воспроизведения. Нажмите Play в плеере.");
                    }
                }
            }
        }
        catch (Exception error) { Log.Write("Media interruption", error); }
        finally { busy = false; }
    }
    public void Dispose()
    {
        disposed = true; revision++; timer.Stop(); timer.Tick -= Tick; c.Media.ManualControl -= ManualControl;
        policy.Cancel(); sources.Reset(); backend.Dispose();
    }
}
