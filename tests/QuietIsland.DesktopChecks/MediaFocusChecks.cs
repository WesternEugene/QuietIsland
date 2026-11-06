using QuietIsland.Services;

internal static class MediaFocusChecks
{
    internal static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        foreach (var app in new[] { "Discord", "Discord.exe", "DiscordCanary", "DiscordPTB", "cs2.exe", "VALORANT-Win64-Shipping.exe", "javaw", "Game.exe", "Steam", "obs64" })
            await check("focus_media_only_ignores_non_media_" + app, async () =>
            {
                using var context = Context(); using var backend = new Fixture(); using var service = new MediaFocusService(context, backend);
                backend.Players["music"] = new("music", "Spotify", true);
                backend.Players["other"] = new("other", app, false); await service.RefreshAsync();
                for (var i = 0; i < 6; i++)
                {
                    backend.Players["other"] = new("other", app, i % 2 == 0);
                    backend.Audio["other-audio"] = new("other-audio", app, true);
                    await service.RefreshAsync();
                }
                Require(backend.Players["music"].IsPlaying && backend.Actions.Count == 0, "A chat/game/background application interrupted music or received a media command.");
            });
        foreach (var app in new[] { "Chrome", "MSEdge", "firefox.exe", "VLC.exe", "Music.UI", "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "308046B0AF4A39CB" })
            await check("focus_media_only_other_player_or_browser_" + app, async () =>
            {
                using var context = Context(); using var backend = new Fixture(); using var service = new MediaFocusService(context, backend);
                backend.Players["music"] = new("music", "Spotify", true);
                backend.Players["media"] = new("media", app, false); await service.RefreshAsync();
                Require(backend.Actions.Count == 0, "Merely opening a paused browser/player interrupted music.");
                backend.Play("media"); await service.RefreshAsync(); await service.RefreshAsync();
                Require(backend.Actions.SequenceEqual(["pause music"]), "Media playback in a browser/player did not pause the previous source exactly once.");
                backend.Stop("media"); await service.RefreshAsync();
                Require(backend.Actions.SequenceEqual(["pause music", "play music"]), "The previous media source was not resumed after the other player stopped.");
            });
        await check("focus_media_only_ignores_games_while_browser_stops_and_alarm_fires", async () =>
        {
            using var context = Context(); using var backend = new Fixture(); using var service = new MediaFocusService(context, backend);
            backend.Players["music"] = new("music", "Spotify", true);
            backend.Players["browser"] = new("browser", "chrome.exe", false); await service.RefreshAsync();
            backend.Play("browser"); await service.RefreshAsync(); await service.RefreshAsync();
            backend.Audio["game"] = new("game", "Game.exe", true); backend.Audio["chat"] = new("chat", "Discord", true);
            backend.Stop("browser"); await service.RefreshAsync();
            Require(backend.Players["music"].IsPlaying, "Game/chat audio held music paused after the browser stopped.");
            service.SetAlarmState(true, false); await service.RefreshAsync();
            Require(!backend.Players["music"].IsPlaying, "Restricting cross-app focus disabled the timer's explicit pause.");
            service.SetAlarmState(false, false); await service.RefreshAsync();
            Require(backend.Players["music"].IsPlaying, "Game/chat audio blocked resuming music after the timer.");
        });
        await check("focus_media_only_filter_cannot_promote_chat_or_game", async () =>
        {
            using var context = Context(); context.Settings.AudioInterruptApps = "Discord;Game";
            using var backend = new Fixture(); using var service = new MediaFocusService(context, backend);
            backend.Players["music"] = new("music", "Spotify", true); await service.RefreshAsync();
            backend.Audio["chat"] = new("chat", "Discord", true); backend.Audio["game"] = new("game", "Game.exe", true);
            await service.RefreshAsync();
            Require(backend.Actions.Count == 0 && backend.Players["music"].IsPlaying, "The optional program filter bypassed media-only focus.");
        });
        foreach (var app in new[] { "Telegram", "Telegram.exe", "Telegram.TelegramDesktop", "Telegram.TelegramDesktop.Store", "Telegram.TelegramDesktop.1234ABCD", "TelegramMessengerLLP.TelegramDesktop_t4vj0pshhgkwm!Telegram.TelegramDesktop" })
            await check("focus_role_telegram_media_without_notification_interruptions_" + app, async () =>
            {
                using var context = Context(); using var backend = new Fixture(); using var service = new MediaFocusService(context, backend);
                backend.Players["music"] = new("music", "Spotify", true);
                backend.Players["telegram"] = new("telegram", app, false, IsAudible: true);
                backend.Audio["notification"] = new("notification", app, true);
                for (var i = 0; i < 12; i++) await service.RefreshAsync();
                Require(backend.Actions.Count == 0, "Telegram notification audio promoted an idle/paused media session.");
                backend.Play("telegram"); await service.RefreshAsync(); await service.RefreshAsync();
                Require(backend.Actions.SequenceEqual(["pause music"]), "Telegram voice/media playback did not interrupt music immediately.");
                backend.Stop("telegram");
                for (var i = 0; i < 12; i++) await service.RefreshAsync();
                Require(backend.Actions.SequenceEqual(["pause music", "play music"]), "Notification audio held or reacquired focus after Telegram media stopped.");
            });
        await check("focus_role_telegram_native_audio_alone_is_never_a_media_signal", async () =>
        {
            using var context = Context(); using var backend = new Fixture(); using var service = new MediaFocusService(context, backend);
            backend.Players["music"] = new("music", "Spotify", true);
            backend.Audio["telegram-notification"] = new("telegram-notification", "Telegram.exe", true);
            for (var i = 0; i < 16; i++) await service.RefreshAsync();
            Require(backend.Actions.Count == 0 && backend.Players["music"].IsPlaying, "Telegram's raw process audio triggered a pause without media playback.");
        });
        foreach (var (app, executable) in new[] { ("MSEdge", "msedge"), ("308046B0AF4A39CB", "firefox"), ("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "Music.UI") })
            await check("focus_role_transport_and_native_audio_share_one_source_" + executable, async () =>
            {
                using var context = Context(); using var backend = new Fixture { AttributeNativeAudio = true };
                using var service = new MediaFocusService(context, backend);
                backend.Players["music"] = new("music", "Spotify", true); backend.Players["media"] = new("media", app, false);
                await service.RefreshAsync();
                for (var i = 0; i < 3; i++)
                {
                    backend.Play("media"); await service.RefreshAsync(); await service.RefreshAsync();
                    backend.Audio["media-audio"] = new("media-audio", executable, true); await service.RefreshAsync(); await service.RefreshAsync();
                    Require(backend.Players["media"].IsPlaying && !backend.Players["music"].IsPlaying, "The media application competed with its own native audio and paused itself.");
                    backend.Stop("media"); backend.Audio.Clear(); await service.RefreshAsync(); await service.RefreshAsync();
                    Require(backend.Players["music"].IsPlaying, "The original player did not resume after the media source stopped.");
                }
                Require(!backend.Actions.Contains("pause media"), "A single media source was paused because its audio identity differed from its transport identity.");
            });
        await check("focus_role_firefox_filter_matches_opaque_transport_identity", async () =>
        {
            using var context = Context(); context.Settings.AudioInterruptApps = "firefox";
            using var backend = new Fixture { AttributeNativeAudio = true }; using var service = new MediaFocusService(context, backend);
            backend.Players["music"] = new("music", "Spotify", true); backend.Players["browser"] = new("browser", "308046B0AF4A39CB", false);
            await service.RefreshAsync(); backend.Play("browser"); await service.RefreshAsync();
            Require(!backend.Players["music"].IsPlaying, "The Firefox restriction excludes Firefox's Windows transport identity.");
            backend.Audio["browser-audio"] = new("browser-audio", "firefox", true); await service.RefreshAsync();
            Require(backend.Players["browser"].IsPlaying, "Firefox paused itself after its native audio was attributed.");
        });
        foreach (var timerFirst in new[] { true, false })
            await check("review_focus_new_overlapping_alarm_" + timerFirst, async () =>
            {
                using var context = Context(); using var backend = new Fixture(); using var service = new MediaFocusService(context, backend);
                backend.Players["spotify"] = new("spotify", "Spotify", true);
                await service.RefreshAsync(); service.SetAlarmState(timerFirst, !timerFirst); await service.RefreshAsync();
                ManualControl(context, "spotify"); backend.Play("spotify"); await service.RefreshAsync();
                Require(backend.Players["spotify"].IsPlaying, "The first alarm did not respect manual playback.");
                service.SetAlarmState(true, true); await service.RefreshAsync();
                Require(!backend.Players["spotify"].IsPlaying, "A new overlapping alarm inherited the first alarm's manual override.");
            });
        await check("review_focus_new_queued_reminder_starts_another_episode", async () =>
        {
            using var context = Context(); using var backend = new Fixture(); using var service = new MediaFocusService(context, backend);
            backend.Players["spotify"] = new("spotify", "Spotify", true);
            service.SetAlarmState(false, true); await service.RefreshAsync();
            ManualControl(context, "spotify"); backend.Play("spotify"); await service.RefreshAsync();
            service.SetAlarmState(false, true, newAlarm: true); await service.RefreshAsync();
            Require(!backend.Players["spotify"].IsPlaying, "A second queued reminder did not interrupt manually resumed music.");
        });
        await check("review_focus_new_alarm_pauses_native_resume_from_previous_interruption", async () =>
        {
            using var context = Context(); using var backend = new Fixture(); using var service = new MediaFocusService(context, backend);
            backend.Players["spotify"] = new("spotify", "Spotify", true); await service.RefreshAsync();
            backend.Audio["browser"] = new("browser", "Chrome", true); await service.RefreshAsync(); await service.RefreshAsync();
            Require(!backend.Players["spotify"].IsPlaying, "The browser fixture did not establish an observed owned pause.");
            // Resume from the native player, without the island's ManualControl event.
            backend.Play("spotify"); service.SetAlarmState(true, false); await service.RefreshAsync();
            Require(!backend.Players["spotify"].IsPlaying, "An external resume from the previous interruption disabled the fresh alarm.");
        });
        foreach (var reminder in new[] { false, true })
            await check(reminder ? "focus_reminder_pauses_after_manual_play" : "focus_timer_pauses_after_manual_play", async () =>
            {
                using var context = Context(); using var backend = new Fixture(); using var service = new MediaFocusService(context, backend);
                backend.Players["spotify"] = new("spotify", "Spotify", true);
                await service.RefreshAsync(); ManualControl(context, "spotify");
                service.SetAlarmState(!reminder, reminder); await service.RefreshAsync();
                Require(!backend.Players["spotify"].IsPlaying, "A previous manual player command prevents the new alarm from pausing music.");
                service.SetAlarmState(false, false); await service.RefreshAsync();
                Require(backend.Actions.SequenceEqual(["pause spotify", "play spotify"]), "The alarm did not resume exactly the music it paused.");
            });
        await check("focus_later_alarm_can_pause_after_manual_resume_during_previous_alarm", async () =>
        {
            using var context = Context(); using var backend = new Fixture(); using var service = new MediaFocusService(context, backend);
            backend.Players["spotify"] = new("spotify", "Spotify", true);
            await service.RefreshAsync(); service.SetAlarmState(true, false); await service.RefreshAsync();
            ManualControl(context, "spotify"); backend.Play("spotify"); await service.RefreshAsync();
            Require(backend.Players["spotify"].IsPlaying, "The active alarm overrides the user's manual resume.");
            service.SetAlarmState(false, false); await service.RefreshAsync();
            service.SetAlarmState(false, true); await service.RefreshAsync();
            Require(!backend.Players["spotify"].IsPlaying && backend.Actions.Count(x => x == "pause spotify") == 2,
                "A manual override of the previous alarm disables the next reminder.");
        });
        foreach (var app in new[] { "obs64", "OBS32.exe", "obs", "obs-browser-page", "obs64|com.obsproject.Studio", @"C:\Program Files\obs-studio\bin\64bit\obs64.exe" })
            await check("focus_obs_monitoring_does_not_cycle_music_" + app, async () =>
            {
                using var context = Context(); using var backend = new Fixture(); var clock = new Clock();
                using var service = new MediaFocusService(context, backend, clock);
                backend.Players["spotify"] = new("spotify", "Spotify", true);
                await service.RefreshAsync();
                for (var i = 0; i < 8; i++)
                {
                    backend.Audio["monitor"] = new("monitor", app, i % 2 == 0);
                    clock.Advance(TimeSpan.FromSeconds(3)); await service.RefreshAsync(); await service.RefreshAsync();
                }
                Require(backend.Actions.Count == 0 && backend.Players["spotify"].IsPlaying, "OBS monitoring repeatedly steals and releases music focus.");
                backend.Audio["browser"] = new("browser", "Chrome", true); await service.RefreshAsync();
                Require(!backend.Players["spotify"].IsPlaying, "Ignoring OBS also disabled genuine browser interruption.");
                backend.Audio.Remove("browser"); await service.RefreshAsync();
                Require(backend.Players["spotify"].IsPlaying, "Music did not resume after the genuine browser ended.");
            });
        await check("focus_timer_episode_ends_with_cross_app_audio_disabled", async () =>
        {
            using var context = Context(); context.Settings.PauseMediaForOtherAudio = false;
            using var backend = new Fixture(); using var service = new MediaFocusService(context, backend);
            backend.Players["spotify"] = new("spotify", "Spotify", true);
            service.SetAlarmState(true, false); await service.RefreshAsync();
            ManualControl(context, "spotify"); backend.Play("spotify");
            service.SetAlarmState(false, false); await service.RefreshAsync();
            ManualControl(context, "spotify"); service.SetAlarmState(false, true); await service.RefreshAsync();
            Require(!backend.Players["spotify"].IsPlaying, "Disabling other-audio focus leaves the previous alarm episode active.");
        });
        await check("focus_resume_after_browser_end_and_windows_session_rewrap", async () =>
        {
            using var context = Context(); context.Settings.MediaResumeDelayMs = 1000;
            using var backend = new Fixture { RecreateWindowsWrappers = true };
            var clock = new Clock(); using var service = new MediaFocusService(context, backend, clock);
            backend.Players["spotify"] = new("spotify", "Spotify", true);
            await service.RefreshAsync();
            backend.Players["browser"] = new("browser", "Chrome", true);
            await service.RefreshAsync(); await service.RefreshAsync();
            Require(!backend.Players["spotify"].IsPlaying, "The browser did not pause Spotify.");
            backend.Players.Remove("browser"); await service.RefreshAsync();
            Require(!backend.Players["spotify"].IsPlaying, "The configured resume delay was ignored.");
            clock.Advance(TimeSpan.FromMilliseconds(1200)); await service.RefreshAsync();
            Require(backend.Players["spotify"].IsPlaying && backend.Actions.SequenceEqual(["pause spotify", "play spotify"]), "Closing the browser session lost the paused player's identity and blocked resume.");
        });
        await check("focus_native_audio_attribution_does_not_promote_paused_sibling", () =>
        {
            var merge = typeof(WindowsMediaFocusBackend).GetMethod("MergeAudio", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            var playback = new List<FocusPlayback> { new("paused-tab", "chrome.exe", false), new("playing-tab", "chrome.exe", true) };
            var result = (FocusSnapshot)merge.Invoke(null, [playback, new[] { new FocusAudio("audio:1", "chrome", true) }, "playing-tab"])!;
            Require(!result.Playback[0].IsAudible && result.Playback[1].IsAudible, "App-wide audio promotes an arbitrary paused sibling over the playing target.");
            playback = [new("paused-one", "chrome.exe", false), new("paused-two", "chrome.exe", false)];
            result = (FocusSnapshot)merge.Invoke(null, [playback, new[] { new FocusAudio("audio:1", "chrome", true) }, null])!;
            Require(result.Playback.All(x => !x.IsAudible) && result.Audio.Count == 1, "An ambiguous browser stream is assigned to a random paused tab.");
            return Task.CompletedTask;
        });
        await check("focus_unrelated_manual_action_preserves_inflight_pause_ownership", async () =>
        {
            using var context = Context(); using var backend = new Fixture(); using var service = new MediaFocusService(context, backend);
            backend.Players["spotify"] = new("spotify", "Spotify", true); backend.Players["browser"] = new("browser", "Chrome", false);
            await service.RefreshAsync();
            backend.Play("browser"); backend.PauseRequested = new(); backend.ReleasePause = new();
            var pausing = service.RefreshAsync(); await backend.PauseRequested.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(MediaService).GetProperty("ManualFocusTargetId", flags)!.SetValue(context.Media, "browser");
            ((Action)typeof(MediaService).GetField("ManualControl", flags)!.GetValue(context.Media)!).Invoke();
            backend.ReleasePause.SetResult(); await pausing;
            backend.Stop("browser"); await service.RefreshAsync();
            Require(backend.Players["spotify"].IsPlaying, "A manual Chrome command discards Spotify's in-flight automatic pause ownership.");
        });
        await check("focus_transport_restart_wins_before_audio_meter_tail_expires", async () =>
        {
            using var context = Context(); using var backend = new Fixture(); using var service = new MediaFocusService(context, backend);
            backend.Players["spotify"] = new("spotify", "Spotify", true, IsAudible: true); backend.Players["browser"] = new("browser", "Chrome", false);
            await service.RefreshAsync(); backend.Play("browser"); await service.RefreshAsync(); await service.RefreshAsync();
            Require(!backend.Players["spotify"].IsPlaying && backend.Players["spotify"].IsAudible, "Fixture did not retain the native audio-meter tail after pausing.");
            backend.Play("spotify"); await service.RefreshAsync();
            Require(!backend.Players["browser"].IsPlaying, "The old audio tail hides the new Spotify transport start.");
        });
        await check("focus_rejected_resume_is_retried_without_losing_ownership", async () =>
        {
            using var context = Context(); using var backend = new Fixture();
            var clock = new Clock(); using var service = new MediaFocusService(context, backend, clock);
            backend.Players["spotify"] = new("spotify", "Spotify", true); backend.Audio["browser"] = new("browser", "Chrome", false);
            await service.RefreshAsync(); backend.Audio["browser"] = new("browser", "Chrome", true); await service.RefreshAsync(); await service.RefreshAsync();
            backend.FailNextPlay = true; backend.Audio["browser"] = new("browser", "Chrome", false); await service.RefreshAsync();
            Require(!backend.Players["spotify"].IsPlaying, "Fixture did not reject the first resume.");
            clock.Advance(TimeSpan.FromSeconds(1)); await service.RefreshAsync();
            Require(backend.Players["spotify"].IsPlaying && backend.Actions.Count(x => x == "play spotify") == 2, "A rejected resume permanently discarded the owned pause.");
        });
        await check("focus_spotify_browser_last_started_wins_and_resumes_both_directions", async () =>
        {
            using var context = Context(); using var backend = new Fixture(); using var service = new MediaFocusService(context, backend);
            backend.Players["spotify"] = new("spotify", "SpotifyAB.SpotifyMusic!Spotify", true);
            backend.Players["browser"] = new("browser", "Chrome.exe", false);
            await service.RefreshAsync(); Require(backend.Actions.Count == 0, "A single player pauses itself.");
            backend.Play("browser"); await service.RefreshAsync(); await service.RefreshAsync();
            Require(backend.Actions.SequenceEqual(["pause spotify"]), "Starting Chrome did not pause Spotify exactly once.");
            backend.Stop("browser"); await service.RefreshAsync(); await service.RefreshAsync();
            Require(backend.Actions.SequenceEqual(["pause spotify", "play spotify"]), "Ending Chrome did not resume Spotify.");
            backend.Play("browser"); await service.RefreshAsync(); await service.RefreshAsync();
            backend.Play("spotify"); await service.RefreshAsync(); await service.RefreshAsync();
            Require(!backend.Players["browser"].IsPlaying && backend.Players["spotify"].IsPlaying, "Starting Spotify while Chrome plays does not transfer priority back.");
            backend.Stop("spotify"); await service.RefreshAsync();
            Require(backend.Players["browser"].IsPlaying, "Ending Spotify did not resume the Chrome session paused by the service.");
        });
        await check("focus_browser_without_transport_controls_still_pauses_music", async () =>
        {
            using var context = Context(); using var backend = new Fixture(); using var service = new MediaFocusService(context, backend);
            backend.Players["spotify"] = new("spotify", "Spotify", true);
            backend.Audio["browser"] = new("browser", "Chrome", false);
            await service.RefreshAsync();
            backend.Audio["browser"] = new("browser", "Chrome", true); await service.RefreshAsync(); await service.RefreshAsync();
            Require(backend.Actions.SequenceEqual(["pause spotify"]), "WASAPI-only browser activity was ignored.");
            backend.Audio["browser"] = new("browser", "Chrome", false); await service.RefreshAsync();
            Require(backend.Actions.SequenceEqual(["pause spotify", "play spotify"]), "Music did not resume after WASAPI-only browser ended.");
        });
        await check("focus_timer_does_not_resume_music_over_an_active_browser", async () =>
        {
            using var context = Context(); using var backend = new Fixture(); using var service = new MediaFocusService(context, backend);
            backend.Players["spotify"] = new("spotify", "Spotify", true);
            await service.RefreshAsync(); service.SetAlarmState(true, false); await service.RefreshAsync();
            backend.Audio["browser"] = new("browser", "Chrome", true); await service.RefreshAsync();
            service.SetAlarmState(false, false); await service.RefreshAsync();
            Require(!backend.Players["spotify"].IsPlaying, "Dismissing the timer resumes music over the active browser.");
            backend.Audio["browser"] = new("browser", "Chrome", false); await service.RefreshAsync();
            Require(backend.Players["spotify"].IsPlaying, "Music remains paused after both interruptions end.");
        });
        await check("focus_disabled_cross_app_option_never_touches_players", async () =>
        {
            using var context = Context(); context.Settings.PauseMediaForOtherAudio = false;
            using var backend = new Fixture(); using var service = new MediaFocusService(context, backend);
            backend.Players["spotify"] = new("spotify", "Spotify", true); backend.Players["browser"] = new("browser", "Chrome", true);
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
    private static void ManualControl(IslandContext context, string id)
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(MediaService).GetProperty("ManualFocusTargetId", flags)!.SetValue(context.Media, id);
        ((Action)typeof(MediaService).GetField("ManualControl", flags)!.GetValue(context.Media)!).Invoke();
    }
    private sealed class Fixture : IMediaFocusBackend
    {
        public bool RecreateWindowsWrappers, AttributeNativeAudio;
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
            if (!RecreateWindowsWrappers)
            {
                if (AttributeNativeAudio && includeAudio)
                {
                    var merge = typeof(WindowsMediaFocusBackend).GetMethod("MergeAudio", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
                    return (FocusSnapshot)merge.Invoke(null, [Players.Values.ToList(), Audio.Values.Where(x => x.IsActive).ToArray(), null])!;
                }
                return new(Players.Values.ToArray(), includeAudio ? Audio.Values.ToArray() : []);
            }
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
