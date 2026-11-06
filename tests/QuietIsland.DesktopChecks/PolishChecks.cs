using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using QuietIsland.Models;
using QuietIsland.Services;
using QuietIsland.Views;

internal static class PolishChecks
{
    internal static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        await check("polish_cross_app_focus_defaults_and_legacy_migration", () =>
        {
            var fresh = new AppSettings(); fresh.NormalizeAppearance();
            Require(fresh.PauseMediaForOtherAudio && fresh.AudioInterruptApps.Length == 0, "New settings still disable cross-app focus or restrict it to Telegram.");
            var legacy = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("""{"PauseMediaForOtherAudio":false,"AudioInterruptApps":"Telegram"}""")!;
            legacy.NormalizeAppearance();
            Require(legacy.PauseMediaForOtherAudio && legacy.AudioInterruptApps.Length == 0, "The first 1.3.0 default survives as a disabled Telegram-only rule.");
            legacy.PauseMediaForOtherAudio = false; legacy.NormalizeAppearance();
            Require(!legacy.PauseMediaForOtherAudio, "A subsequent user opt-out is not respected.");
            return Task.CompletedTask;
        });
        await check("polish_player_has_no_note_action_and_timeline_below_controls", () =>
        {
            using var context = new IslandContext(Path.Combine(Path.GetTempPath(), "island-player-layout-" + Guid.NewGuid().ToString("N")));
            var main = new QuietIsland.MainWindow(context, true);
            try
            {
                using var home = new HomeView(context, main, () => []) { Resources = VisualPolishChecks.Styles() };
                home.Measure(new(680, home.PreferredBodyHeight)); home.Arrange(new(0, 0, 680, home.PreferredBodyHeight)); home.UpdateLayout();
                var seek = (Slider)typeof(HomeView).GetField("seek", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(home)!;
                var play = Descendants(home).OfType<Button>().Single(x => Equals(x.ToolTip, "Воспроизвести"));
                Require(!Descendants(home).OfType<Button>().Any(x => x.ToolTip is string text && text.StartsWith("Выбрать медиаплеер")), "The note action is still visible in the player card.");
                Require(seek.TranslatePoint(new(), home).Y >= play.TranslatePoint(new(0, play.ActualHeight), home).Y, "The timeline still sits above playback controls.");
            }
            finally { main.Close(); }
            return Task.CompletedTask;
        });
        await check("polish_control_tab_builds_power_confirmation_without_reparent_error", () =>
        {
            using var context = new IslandContext(Path.Combine(Path.GetTempPath(), "island-control-regression-" + Guid.NewGuid().ToString("N")));
            using var control = new ControlView(context) { Resources = VisualPolishChecks.Styles() };
            control.Measure(new(680, 360)); control.Arrange(new(0, 0, 680, 360)); control.UpdateLayout();
            Require(Descendants(control).OfType<Button>().Count(x => x.ToolTip is string text && text.Contains("два нажатия")) == 3, "The Control tab cannot build all three power-confirmation buttons.");
            // Supply an inert action: never invoke real shutdown, restart, sleep, network or VPN.
            var calls = 0;
            var button = (Button)typeof(ControlView).GetMethod("PowerButton", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, ["power", "Test power", (Action)(() => calls++)])!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(calls == 0, "First power click immediately performs the action.");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(calls == 1, "Second power click does not confirm exactly once.");
            return Task.CompletedTask;
        });
        await check("polish_synced_lyric_click_requests_exact_track_position", () =>
        {
            using var lyrics = new QuietIsland.Controls.LyricsPanel(true);
            double requested = -1;
            var seek = lyrics.GetType().GetEvent("SeekRequested");
            Require(seek != null, "Clicking a synchronized lyric cannot seek the track.");
            seek!.AddEventHandler(lyrics, (Action<double>)(seconds => requested = seconds));
            lyrics.Update(new LyricsDocument(LyricsStatus.Synced, [new(12.5, "Первая строка"), new(37.25, "Вторая строка")], ""), 0);
            lyrics.Measure(new(280, 180)); lyrics.Arrange(new(0, 0, 280, 180)); lyrics.UpdateLayout();
            var row = Descendants(lyrics).OfType<Button>().Single(x => x.Content is TextBlock text && text.Text == "Вторая строка");
            row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(requested == 37.25, "Lyric click lost its fractional-second timestamp.");
            lyrics.Update(new LyricsDocument(LyricsStatus.Plain, [], "Без синхронизации"), 0);
            Require(!Descendants(lyrics).OfType<Button>().Any(x => x.Content is TextBlock), "Unsynchronized lyrics offer misleading seek actions.");
            return Task.CompletedTask;
        });
        await check("polish_focus_manual_override_does_not_disable_later_alarms", () =>
        {
            using var context = new IslandContext(Path.Combine(Path.GetTempPath(), "island-focus-episode-" + Guid.NewGuid().ToString("N")));
            var policy = (QuietIsland.Core.MediaFocusPolicy)typeof(MediaFocusService).GetField("policy", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context.Focus)!;
            var now = DateTimeOffset.UtcNow;
            policy.Update([new("music", true)], true, true, TimeSpan.Zero, now); policy.ConfirmPause("music", true); policy.Cancel();
            context.Focus.SetAlarmState(false, false);
            Require(policy.Update([new("music", true)], true, true, TimeSpan.Zero, now).Count == 1, "A manual override in the first alarm prevents later alarms from pausing music.");
            return Task.CompletedTask;
        });
        await check("polish_pinned_clipboard_survives_restart_and_protects_image", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "island-pins-" + Guid.NewGuid().ToString("N"));
            var state = new DesktopState(directory);
            var image = Path.Combine(directory, "clipboard", Guid.NewGuid().ToString("N") + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(image)!); File.WriteAllBytes(image, [1, 2, 3]);
            var pinned = new ClipEntry { Text = "pinned", Pinned = true };
            state.Data.Clipboard.AddRange([pinned, new() { Kind = "image", ImagePath = image, Pinned = true }, new() { Text = "temporary" }]);
            new ClipboardService(state).Remove(pinned);
            Require(state.Data.Clipboard.Contains(pinned), "A pinned entry can be deleted before unpinning.");
            state.Save();
            var restored = new DesktopState(directory);
            Require(restored.Data.Clipboard.Count == 2 && restored.Data.Clipboard.All(x => x.Pinned), "Pinned clipboard entries did not survive restart, or temporary entries were persisted.");
            Require(File.Exists(image), "Startup deleted a pinned image.");
            var restoredText = restored.Data.Clipboard.Single(x => x.Kind == "text"); restoredText.Pinned = false;
            new ClipboardService(restored).Remove(restoredText);
            Require(restored.Data.Clipboard.Count == 1, "Unpinned entries cannot be removed.");
            return Task.CompletedTask;
        });
        await check("polish_default_launcher_has_no_powershell", () =>
        {
            var state = new DesktopState(Path.Combine(Path.GetTempPath(), "island-launcher-" + Guid.NewGuid().ToString("N")));
            Require(!state.Data.Launcher.Any(x => x.Target.Contains("powershell", StringComparison.OrdinalIgnoreCase)), "PowerShell is still a default shortcut.");
            return Task.CompletedTask;
        });
        await check("polish_clipboard_actions_are_not_nested", () =>
        {
            using var context = new IslandContext(Path.Combine(Path.GetTempPath(), "island-clip-ui-" + Guid.NewGuid().ToString("N")));
            context.State.Data.Clipboard.Add(new() { Text = "protected pin", Pinned = true });
            using var view = new ClipboardView(context); view.Measure(new(680, 340)); view.Arrange(new(0, 0, 680, 340)); view.UpdateLayout();
            var buttons = Descendants(view).OfType<Button>().ToArray();
            Require(!buttons.Any(x => Equals(x.ToolTip, "Удалить запись")), "Pinned entry still offers Delete.");
            Require(buttons.Any(x => Equals(x.ToolTip, "Открепить")), "Pinned entry has no explicit Unpin action.");
            Require(!buttons.Any(x => Equals(x.ToolTip, "")), "Clipboard has an empty tooltip.");
            Require(!buttons.Any(x => Descendants(x).OfType<Button>().Any()), "Clipboard actions are nested in the paste button.");
            return Task.CompletedTask;
        });
        await check("polish_music_routes_to_home", () =>
        {
            using var context = new IslandContext(Path.Combine(Path.GetTempPath(), "island-home-only-" + Guid.NewGuid().ToString("N")));
            var main = new QuietIsland.MainWindow(context, true);
            try
            {
                main.ShowPanel("media", false);
                Require(((ContentControl)main.FindName("Body")).Content is HomeView, "Media navigation still opens a separate player.");
            }
            finally { main.Close(); }
            return Task.CompletedTask;
        });
        await check("polish_neutral_cover_keeps_neutral_palette", () =>
        {
            var palette = typeof(QuietIsland.Controls.Ui).Assembly.GetType("QuietIsland.Controls.AlbumPalette")!;
            foreach (var tone in new byte[] { 0, 128, 255 })
            {
                var image = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { tone, tone, tone, 255 }, 4);
                var color = (Color)palette.GetMethod("Accent", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, [image])!;
                Require(Math.Abs(color.R - color.G) < 5 && Math.Abs(color.R - color.B) < 5, "A neutral cover uses the unrelated coral accent.");
            }
            return Task.CompletedTask;
        });
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
