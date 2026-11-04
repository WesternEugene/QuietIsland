using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QuietIsland.Models;
using QuietIsland.Services;

namespace QuietIsland;

/// <summary>Isolated launcher, timer and clipboard checks; never starts services or controls VPN.</summary>
internal static class InteractionWorkflowChecks
{
    private static readonly string[] DefaultTargets = ["explorer.exe", "notepad.exe", "powershell.exe", "calc.exe", "ms-settings:"];

    public static async Task<object> RunAsync(string directory)
    {
        var results = new Dictionary<string, bool>();
        Directory.CreateDirectory(directory);
        var imageDirectory = Path.Combine(directory, "clipboard"); Directory.CreateDirectory(imageDirectory);
        var staleImage = Path.Combine(imageDirectory, Guid.NewGuid().ToString("N") + ".png"); File.WriteAllText(staleImage, "owned image fixture");
        var otherImage = Path.Combine(imageDirectory, "keep-user-image.png"); File.WriteAllText(otherImage, "keep unowned fixture");
        var shelfFile = Path.Combine(directory, "keep.txt"); File.WriteAllText(shelfFile, "keep original");
        var previews = Path.Combine(directory, "previews"); Directory.CreateDirectory(previews);
        File.WriteAllText(Path.Combine(directory, "state.json"), JsonSerializer.Serialize(new
        {
            Clipboard = new[] { new ClipEntry { Text = "old pinned history", Pinned = true }, new ClipEntry { Kind = "image", ImagePath = staleImage }, new ClipEntry { Kind = "image", ImagePath = shelfFile } },
            Launcher = new[] { new LaunchItem { Name = "Сохранённый ярлык", Target = "https://example.org" }, new LaunchItem { Name = "Проводник", Target = "EXPLORER.EXE" } },
            Notes = new[] { new Note { Title = "Сохранённая заметка", Text = "keep note" } },
            Shelf = new[] { shelfFile }
        }));
        using var c = new IslandContext(directory);
        results["legacyClipboardStartsEmpty"] = c.State.Data.Clipboard.Count == 0;
        results["startupRemovesOwnedClipboardImages"] = !File.Exists(staleImage) && File.Exists(shelfFile) && File.Exists(otherImage);
        results["startupPreservesOtherData"] = c.State.Data.Shelf.Contains(shelfFile) && c.State.Data.Notes.Any(x => x.Text == "keep note") && c.State.Data.Launcher.Any(x => x.Target == "https://example.org");
        results["defaultLaunchersMigrateWithoutDuplicates"] = DefaultTargets.All(target => c.State.Data.Launcher.Count(x => x.Target.Equals(target, StringComparison.OrdinalIgnoreCase)) == 1);
        c.State.Data.Clipboard.Add(new ClipEntry { Text = "session-only fixture", Pinned = true }); c.State.Save();
        using (var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "state.json"))))
            results["clipboardIsNotPersisted"] = !json.RootElement.TryGetProperty("Clipboard", out _);
        var reopened = new DesktopState(directory);
        results["reopenedClipboardStartsEmpty"] = reopened.Data.Clipboard.Count == 0;
        reopened.Data.Launcher.RemoveAll(x => x.Target.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)); reopened.Save();
        var afterRemoval = new DesktopState(directory);
        results["removedDefaultStaysRemovedAfterRestart"] = !afterRemoval.Data.Launcher.Any(x => x.Target.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)) && afterRemoval.Data.Launcher.Any(x => x.Target == "https://example.org");

        var main = new MainWindow(c, true); main.Content = null;
        using var source = new HwndSource(new HwndSourceParameters("QuietIsland.InteractionChecks") { ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 744, Height = 430 }); source.RootVisual = main.RenderRoot;
        main.ShowPanel("home", false); Layout(main);
        var launchers = Descendants(main.RenderRoot).OfType<Button>().Where(x => x.ToolTip is string tip && c.State.Data.Launcher.Any(pin => pin.Name == tip)).ToArray();
        results["homeShowsDefaultAndCustomLaunchers"] = DefaultTargets.All(target => launchers.Any(b => c.State.Data.Launcher.Any(pin => pin.Target.Equals(target, StringComparison.OrdinalIgnoreCase) && Equals(b.ToolTip, pin.Name))));
        results["homeLaunchersHaveContextMenus"] = launchers.Length > 0 && launchers.All(x => x.ContextMenu?.Items.OfType<MenuItem>().Any(item => Equals(item.Header, "Убрать ярлык")) == true);
        var add = Descendants(main.RenderRoot).OfType<Button>().First(x => x.ToolTip is string tip && tip.StartsWith("Добавить ярлык"));
        var beforeAdd = c.State.Data.Launcher.Count; var rejectedEmptyTarget = false;
        var addDialogOpened = await RespondToDialog(() => Click(add), dialog =>
        {
            main.PointerLeaveForChecks(3000); results["shortcutDialogKeepsIslandOpen"] = main.IsExpanded;
            var inputs = Descendants(dialog).OfType<TextBox>().ToArray();
            if (inputs.Length != 2) return;
            inputs.Single(x => Equals(x.Tag, "Название")).Text = "Добавленный ярлык";
            var target = inputs.Single(x => x.Tag is string label && label.StartsWith("Путь")); target.Text = " ";
            var save = Descendants(dialog).OfType<Button>().Single(x => Equals(x.Content, "Добавить")); Click(save);
            rejectedEmptyTarget = dialog.IsVisible && c.State.Data.Launcher.Count == beforeAdd;
            target.Text = "https://example.net/shortcut";
            results["shortcutValidationClearsWhenCorrected"] = !Descendants(dialog).OfType<TextBlock>().Any(x => x.Text == "Укажите путь или ссылку");
            CaptureService.SavePng(Diagnostics.Render((FrameworkElement)dialog.Content, 490, 340), Path.Combine(previews, "shortcut-dialog.png"));
            Click(save);
        });
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Layout(main);
        results["plusOpensDialogWithoutNavigation"] = addDialogOpened && main.SelectedTab == "home";
        results["emptyShortcutTargetIsRejected"] = rejectedEmptyTarget;
        results["addedShortcutAppearsImmediately"] = c.State.Data.Launcher.Count == beforeAdd + 1 && Descendants(main.RenderRoot).OfType<Button>().Any(x => Equals(x.ToolTip, "Добавленный ярлык"));
        CaptureService.SavePng(Diagnostics.Render(main.RenderRoot, 744, 430), Path.Combine(previews, "home-shortcuts.png"));
        var cancelCount = c.State.Data.Launcher.Count;
        main.ShowPanel("home", false); Layout(main);
        add = Descendants(main.RenderRoot).OfType<Button>().First(x => x.ToolTip is string tip && tip.StartsWith("Добавить ярлык"));
        var cancelled = await RespondToDialog(() => Click(add), dialog => dialog.DialogResult = false);
        results["cancelShortcutLeavesHomeUnchanged"] = cancelled && main.SelectedTab == "home" && c.State.Data.Launcher.Count == cancelCount;
        Layout(main);
        var added = Descendants(main.RenderRoot).OfType<Button>().FirstOrDefault(x => Equals(x.ToolTip, "Добавленный ярлык"));
        if (added?.ContextMenu is { } shortcutMenu)
        {
            var menuEvent = (ContextMenuEventArgs)Activator.CreateInstance(typeof(ContextMenuEventArgs), BindingFlags.Instance | BindingFlags.NonPublic, null, [added, true], null)!;
            menuEvent.Handled = true; added.RaiseEvent(menuEvent); shortcutMenu.PlacementTarget = added; shortcutMenu.Opacity = 0; shortcutMenu.IsOpen = true;
            await Task.Delay(80); main.PointerLeaveForChecks(3000);
            results["shortcutContextMenuKeepsIslandOpen"] = main.IsExpanded && shortcutMenu.Items.OfType<MenuItem>().All(x => x.Icon is FrameworkElement);
            shortcutMenu.IsOpen = false;
        }
        else results["shortcutContextMenuKeepsIslandOpen"] = false;
        var remove = added?.ContextMenu?.Items.OfType<MenuItem>().FirstOrDefault(x => Equals(x.Header, "Убрать ярлык"));
        if (remove != null) Click(remove);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Layout(main);
        results["contextMenuRemovalUpdatesHome"] = remove != null && !c.State.Data.Launcher.Any(x => x.Name == "Добавленный ярлык") && !Descendants(main.RenderRoot).OfType<Button>().Any(x => Equals(x.ToolTip, "Добавленный ярлык"));

        main.Collapse(); c.Notify(""); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); main.RefreshCompactForChecks();
        var normalHeight = main.NotchHeight;
        c.StartTimer(TimeSpan.FromSeconds(7)); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        c.State.Data.TimerEnd = DateTimeOffset.Now.AddSeconds(-1);
        foreach (var field in new[] { "lastActivity", "lastWeather", "lastPoll" }) typeof(MainWindow).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, DateTime.Now);
        await main.TickAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Layout(main);
        results["finishedTimerEnlargesCompactIsland"] = !main.IsExpanded && main.NotchHeight > normalHeight && main.FindName("CompactAlarm") is FrameworkElement { Visibility: Visibility.Visible };
        results["finishedTimerSaysTimer"] = main.FindName("CompactAlarm") is DependencyObject alarm && Descendants(alarm).OfType<TextBlock>().Any(x => x.Text == "Таймер");
        results["timerLabelUsesThemeForeground"] = main.FindName("CompactAlarm") is DependencyObject alarmContent && Descendants(alarmContent).OfType<TextBlock>().Any(x => x.Text == "Таймер" && Equals(x.Foreground, Controls.Ui.Brush("Text")));
        var repeat = main.FindName("CompactTimerAgain") as Button;
        var dismiss = main.FindName("CompactTimerDismiss") as Button;
        results["compactTimerActionsAreIcons"] = repeat?.Content is FrameworkElement && dismiss?.Content is FrameworkElement && repeat.Visibility == Visibility.Visible && dismiss.Visibility == Visibility.Visible && repeat.ActualWidth > 0 && dismiss.ActualWidth > 0;
        results["panelTimerActionsAreIcons"] = ((Button)main.FindName("TimerAgain")).Content is FrameworkElement && ((Button)main.FindName("TimerDismiss")).Content is FrameworkElement;
        var alarmImage = Diagnostics.Render(main.RenderRoot, 744, 430);
        CaptureService.SavePng(new CroppedBitmap(alarmImage, new Int32Rect((744 - (int)main.NotchWidth) / 2, 0, (int)main.NotchWidth, (int)main.NotchHeight + 6)), Path.Combine(previews, "timer-alarm.png"));
        c.Settings.CompactMinWidth = c.Settings.CompactMaxWidth = 160; c.Settings.CompactHeight = 44; main.ApplySettings(); Layout(main);
        results["narrowSettingsKeepTimerButtonsInsideIsland"] = new[] { repeat, dismiss }.All(button => button != null && main.ContainsSurfaceForChecks(button.TranslatePoint(new Point(button.ActualWidth / 2, button.ActualHeight / 2), main.RenderRoot)));
        c.Settings.CompactMinWidth = 190; c.Settings.CompactMaxWidth = 700; c.Settings.CompactHeight = normalHeight; main.ApplySettings();
        Click(repeat ?? (Button)main.FindName("TimerAgain"));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        results["repeatUsesOriginalTimerDuration"] = c.State.Data.TimerEnd != null && !c.State.Data.TimerPaused && c.State.Data.TimerDuration == 7 && (c.State.Data.TimerEnd.Value - DateTimeOffset.Now).TotalSeconds is > 5 and <= 7;
        results["repeatClearsAlarmPresentation"] = ((FrameworkElement)main.FindName("TimerNotification")).Visibility == Visibility.Collapsed && Math.Abs(main.NotchHeight - normalHeight) < .1;
        c.State.Data.TimerEnd = DateTimeOffset.Now.AddSeconds(-1); await main.TickAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Click(dismiss ?? (Button)main.FindName("TimerDismiss")); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        results["dismissClearsAlarmWithoutStartingTimer"] = c.State.Data.TimerEnd == null && ((FrameworkElement)main.FindName("TimerNotification")).Visibility == Visibility.Collapsed && Math.Abs(main.NotchHeight - normalHeight) < .1;
        c.State.Data.TimerEnd = DateTimeOffset.Now.AddSeconds(-1); await main.TickAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        c.StartTimer(TimeSpan.FromMinutes(2)); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        results["startingAnotherTimerClearsAlarm"] = ((FrameworkElement)main.FindName("TimerNotification")).Visibility == Visibility.Collapsed && c.State.Data.TimerDuration == 120;
        main.Close();
        return results;
    }

    private static void Click(UIElement control) => control.RaiseEvent(new RoutedEventArgs(control is MenuItem ? MenuItem.ClickEvent : ButtonBase.ClickEvent));
    private static void Layout(MainWindow window) => Diagnostics.Render(window.RenderRoot, 744, 430);
    private static async Task<bool> RespondToDialog(Action click, Action<Window> respond)
    {
        var opened = false;
        _ = System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var dialog = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(x => x.IsVisible && x.Title == "Добавить ярлык");
            if (dialog == null) return;
            opened = true; dialog.Opacity = 0;
            try { respond(dialog); }
            finally { if (dialog.IsVisible) dialog.DialogResult = false; }
        }, DispatcherPriority.ContextIdle);
        click(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); return opened;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
