using System.Windows;
using QuietIsland.Controls;
using QuietIsland.Services;

namespace QuietIsland;

public partial class MainWindow
{
    private bool timerAlarm;
    private readonly TimerAlarmSound timerSound = new();
    private void InitializeTimerNotice()
    {
        TimerAgain.Content = Icons.Make("repeat"); CompactTimerAgain.Content = Icons.Make("repeat");
        TimerDismiss.Content = Icons.Make("close"); CompactTimerDismiss.Content = Icons.Make("close");
        CompactAlarmIcon.Content = Icons.Colored("clock", 24);
        TimerAgain.Click += (_, _) => RepeatTimer();
        CompactTimerAgain.Click += (_, _) => RepeatTimer();
        TimerDismiss.Click += (_, _) => ClearTimerNotice();
        CompactTimerDismiss.Click += (_, _) => ClearTimerNotice();
        c.State.Changed += AppStateChanged;
    }
    private void RepeatTimer()
    {
        var seconds = c.State.Data.TimerDuration;
        ClearTimerNotice(); c.StartTimer(TimeSpan.FromSeconds(double.IsFinite(seconds) && seconds > 0 ? seconds : 300)); view?.Refresh();
    }
    private void AppStateChanged() => Dispatcher.BeginInvoke(() =>
    {
        if (IsClosing) return;
        if (timerAlarm && c.State.Data.TimerEnd != null) ClearTimerNotice();
        RegisterHotkeys(); UpdateCompact(); if (view is Views.NotesView or Views.HomeView) view.Refresh();
    });
    private void TimerFinished(string title)
    {
        timerAlarm = true; TimerNotification.Visibility = Visibility.Visible;
        if (!snapshot)
        {
            hiddenByUser = false; Show(); UpdateRuntimeVisibility(DateTime.Now);
            Native.ConfigureOverlay(this, expanded, false, c.Settings.HideInCapture);
            try { timerSound.Start(); }
            catch (Exception error) when (error is InvalidOperationException or TimeoutException) { Log.Write("Timer sound", error); }
        }
        c.Notify(title); UpdateCompact();
    }
    private void ClearTimerNotice()
    {
        timerSound.Stop(); timerAlarm = false; TimerNotification.Visibility = Visibility.Collapsed;
        if (toast == "Таймер завершён") { toast = ""; toastUntil = DateTime.MinValue; }
        UpdateCompact();
        if (!snapshot && !IsClosing)
        {
            UpdateRuntimeVisibility(DateTime.Now);
            Native.ConfigureOverlay(this, expanded, !expanded && c.Settings.ClickThrough, c.Settings.HideInCapture);
        }
    }
}
