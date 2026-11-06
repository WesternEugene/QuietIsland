using QuietIsland.Services;

internal static class MediaFocusChecks
{
    internal static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        await check("focus_resume_after_voice_end_and_windows_session_rewrap", async () =>
        {
            using var context = Context(); context.Settings.MediaResumeDelayMs = 1000;
            using var backend = new Fixture { RecreateWindowsWrappers = true };
            var clock = new Clock(); using var service = new MediaFocusService(context, backend, clock);
            backend.Players["spotify"] = new("spotify", "Spotify", true);
            await service.RefreshAsync();
            backend.Players["telegram"] = new("telegram", "Telegram", true);
            await service.RefreshAsync(); await service.RefreshAsync();
            Require(!backend.Players["spotify"].IsPlaying, "The voice did not pause Spotify.");
            backend.Players.Remove("telegram"); await service.RefreshAsync();
            Require(!backend.Players["spotify"].IsPlaying, "The configured resume delay was ignored.");
            clock.Advance(TimeSpan.FromMilliseconds(1200)); await service.RefreshAsync();
            Require(backend.Players["spotify"].IsPlaying && backend.Actions.SequenceEqual(["pause spotify", "play spotify"]), "Closing the voice session lost the paused player's identity and blocked resume.");
        });
        await check("focus_native_audio_attribution_does_not_promote_paused_sibling", () =>
        {
            var merge = typeof(WindowsMediaFocusBackend).GetMethod("MergeAudio", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            var playback = new List<FocusPlayback> { new("paused-tab", "chrome.exe", false), new("playing-tab", "chrome.exe", true) };
            var result = (FocusSnapshot)merge.Invoke(null, [playback, new[] { new FocusAudio("audio:1", "chrome", true) }, "playing-tab"])!;
            Require(!result.Playback[0].IsAudible && result.Playback[1].IsAudible, "App-wide audio promotes an arbitrary paused sibling over the playing target.");
            playback = [new("paused-one", "chrome.exe", false), new("paused-two", "chrome.exe", false)];
            result = (FocusSnapshot)merge.Invoke(null, [playback, new[] { new FocusAudio("audio:1", "chrome", true) }, null])!;
            Require(result.Playback.All(x => !x.IsAudible) && result.Audio.Count == 1, "An ambiguous voice stream is assigned to a random paused tab.");
            return Task.CompletedTask;
        });
        await check("focus_unrelated_manual_action_preserves_inflight_pause_ownership", async () =>
        {
            using var context = Context(); using var backend = new Fixture(); using var service = new MediaFocusService(context, backend);
            backend.Players["spotify"] = new("spotify", "Spotify", true); backend.Players["telegram"] = new("telegram", "Telegram", false);
            await service.RefreshAsync();
            backend.Play("telegram"); backend.PauseRequested = new(); backend.ReleasePause = new();
            var pausing = service.RefreshAsync(); await backend.PauseRequested.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(MediaService).GetProperty("ManualFocusTargetId", flags)!.SetValue(context.Media, "telegram");
            ((Action)typeof(MediaService).GetField("ManualControl", flags)!.GetValue(context.Media)!).Invoke();
            backend.ReleasePause.SetResult(); await pausing;
            backend.Stop("telegram"); await service.RefreshAsync();
            Require(backend.Players["spotify"].IsPlaying, "A manual Telegram command discards Spotify's in-flight automatic pause ownership.");
        });
        await check("focus_transport_restart_wins_before_audio_meter_tail_expires", async () =>
        {
            using var context = Context(); using var backend = new Fixture(); using var service = new MediaFocusService(context, backend);
            backend.Players["spotify"] = new("spotify", "Spotify", true, IsAudible: true); backend.Players["telegram"] = new("telegram", "Telegram", false);
            await service.RefreshAsync(); backend.Play("telegram"); await service.RefreshAsync(); await service.RefreshAsync();
            Require(!backend.Players["spotify"].IsPlaying && backend.Players["spotify"].IsAudible, "Fixture did not retain the native audio-meter tail after pausing.");
            backend.Play("spotify"); await service.RefreshAsync();
            Require(!backend.Players["telegram"].IsPlaying, "The old audio tail hides the new Spotify transport start.");
        });
        await check("focus_rejected_resume_is_retried_without_losing_ownership", async () =>
        {
            using var context = Context(); using var backend = new Fixture();
            var clock = new Clock(); using var service = new MediaFocusService(context, backend, clock);
            backend.Players["spotify"] = new("spotify", "Spotify", true); backend.Audio["voice"] = new("voice", "Telegram", false);
            await service.RefreshAsync(); backend.Audio["voice"] = new("voice", "Telegram", true); await service.RefreshAsync(); await service.RefreshAsync();
            backend.FailNextPlay = true; backend.Audio["voice"] = new("voice", "Telegram", false); await service.RefreshAsync();
            Require(!backend.Players["spotify"].IsPlaying, "Fixture did not reject the first resume.");
            clock.Advance(TimeSpan.FromSeconds(1)); await service.RefreshAsync();
            Require(backend.Players["spotify"].IsPlaying && backend.Actions.Count(x => x == "play spotify") == 2, "A rejected resume permanently discarded the owned pause.");
        });
        await check("focus_spotify_telegram_last_started_wins_and_resumes_both_directions", async () =>
        {
            using var context = Context(); using var backend = new Fixture(); using var service = new MediaFocusService(context, backend);
            backend.Players["spotify"] = new("spotify", "SpotifyAB.SpotifyMusic!Spotify", true);
            backend.Players["telegram"] = new("telegram", "Telegram.exe", false);
            await service.RefreshAsync(); Require(backend.Actions.Count == 0, "A single player pauses itself.");
            backend.Play("telegram"); await service.RefreshAsync(); await service.RefreshAsync();
            Require(backend.Actions.SequenceEqual(["pause spotify"]), "Starting Telegram did not pause Spotify exactly once.");
            backend.Stop("telegram"); await service.RefreshAsync(); await service.RefreshAsync();
            Require(backend.Actions.SequenceEqual(["pause spotify", "play spotify"]), "Ending Telegram did not resume Spotify.");
            backend.Play("telegram"); await service.RefreshAsync(); await service.RefreshAsync();
            backend.Play("spotify"); await service.RefreshAsync(); await service.RefreshAsync();
            Require(!backend.Players["telegram"].IsPlaying && backend.Players["spotify"].IsPlaying, "Starting Spotify while Telegram plays does not transfer priority back.");
            backend.Stop("spotify"); await service.RefreshAsync();
            Require(backend.Players["telegram"].IsPlaying, "Ending Spotify did not resume the Telegram session paused by the service.");
        });
        await check("focus_voice_without_transport_controls_still_pauses_music", async () =>
        {
            using var context = Context(); using var backend = new Fixture(); using var service = new MediaFocusService(context, backend);
            backend.Players["spotify"] = new("spotify", "Spotify", true);
            backend.Audio["voice"] = new("voice", "Telegram", false);
            await service.RefreshAsync();
            backend.Audio["voice"] = new("voice", "Telegram", true); await service.RefreshAsync(); await service.RefreshAsync();
            Require(backend.Actions.SequenceEqual(["pause spotify"]), "WASAPI-only voice activity was ignored.");
            backend.Audio["voice"] = new("voice", "Telegram", false); await service.RefreshAsync();
            Require(backend.Actions.SequenceEqual(["pause spotify", "play spotify"]), "Music did not resume after WASAPI-only voice ended.");
        });
        await check("focus_timer_does_not_resume_music_over_an_active_voice", async () =>
        {
            using var context = Context(); using var backend = new Fixture(); using var service = new MediaFocusService(context, backend);
            backend.Players["spotify"] = new("spotify", "Spotify", true);
            await service.RefreshAsync(); service.SetAlarmState(true, false); await service.RefreshAsync();
            backend.Audio["voice"] = new("voice", "Telegram", true); await service.RefreshAsync();
            service.SetAlarmState(false, false); await service.RefreshAsync();
            Require(!backend.Players["spotify"].IsPlaying, "Dismissing the timer resumes music over the active voice.");
            backend.Audio["voice"] = new("voice", "Telegram", false); await service.RefreshAsync();
            Require(backend.Players["spotify"].IsPlaying, "Music remains paused after both interruptions end.");
        });
        await check("focus_disabled_cross_app_option_never_touches_players", async () =>
        {
            using var context = Context(); context.Settings.PauseMediaForOtherAudio = false;
            using var backend = new Fixture(); using var service = new MediaFocusService(context, backend);
            backend.Players["spotify"] = new("spotify", "Spotify", true); backend.Players["telegram"] = new("telegram", "Telegram", true);
            await service.RefreshAsync(); Require(backend.Actions.Count == 0, "The user's opt-out is ignored.");
        });
    }
    private static IslandContext Context()
    {
        var context = new IslandContext(Path.Combine(Path.GetTempPath(), "island-focus-integration-" + Guid.NewGuid().ToString("N")));
        context.Settings.PauseMediaForOtherAudio = true; context.Settings.AudioInterruptApps = ""; context.Settings.MediaResumeDelayMs = 0;
        return context;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Fixture : IMediaFocusBackend
    {
        public bool RecreateWindowsWrappers;
        private readonly QuietIsland.Core.SessionIdentityRegistry<NativeWrapper> nativeIds = new(x => x.App);
        private readonly Dictionary<string, string> logicalIds = [];
        private sealed class NativeWrapper(string logical, string app) { public string Logical => logical; public string App => app; }
        public bool FailNextPlay;
        public TaskCompletionSource? PauseRequested, ReleasePause;
        public Dictionary<string, FocusPlayback> Players = [];
        public Dictionary<string, FocusAudio> Audio = [];
        public List<string> Actions = [];
        public FocusSnapshot ReadSnapshot(bool includeAudio)
        {
            if (!RecreateWindowsWrappers) return new(Players.Values.ToArray(), includeAudio ? Audio.Values.ToArray() : []);
            var targets = nativeIds.Refresh(Players.Select(x => new NativeWrapper(x.Key, x.Value.App)).ToArray());
            logicalIds.Clear();
            var playback = targets.Select(x => { logicalIds[x.Id] = x.Session.Logical; return Players[x.Session.Logical] with { Id = x.Id }; }).ToArray();
            return new(playback, includeAudio ? Audio.Values.ToArray() : []);
        }
        public async Task<bool> SetPausedAsync(string id, bool paused)
        {
            if (RecreateWindowsWrappers) id = logicalIds[id];
            Actions.Add((paused ? "pause " : "play ") + id);
            if (paused && ReleasePause != null) { PauseRequested?.TrySetResult(); await ReleasePause.Task; }
            if (!paused && FailNextPlay) { FailNextPlay = false; return false; }
            Players[id] = Players[id] with { IsPlaying = !paused }; return true;
        }
        public void Play(string id) => Players[id] = Players[id] with { IsPlaying = true };
        public void Stop(string id) => Players[id] = Players[id] with { IsPlaying = false };
        public void Dispose() { }
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.Parse("2026-11-06T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan span) => now += span;
    }
}
