using System.Windows;

namespace QuietIsland;

public partial class MainWindow
{
    private bool timerAlarm;
    private void InitializeTimerNotice()
    {
        TimerAgain.Click += (_, _) => { ClearTimerNotice(); c.StartTimer(TimeSpan.FromMinutes(5)); view?.Refresh(); };
        TimerDismiss.Click += (_, _) => ClearTimerNotice();
        c.State.Changed += AppStateChanged;
    }
    private void AppStateChanged() => Dispatcher.BeginInvoke(() =>
    {
        if (IsClosing) return;
        if (timerAlarm && c.State.Data.TimerEnd != null) ClearTimerNotice();
        RegisterHotkeys(); UpdateCompact(); if (view is Views.NotesView) view.Refresh();
    });
    private void TimerFinished(string title)
    {
        timerAlarm = true; TimerNotification.Visibility = Visibility.Visible;
        if (!snapshot) System.Media.SystemSounds.Exclamation.Play();
        c.Notify(title); UpdateCompact();
    }
    private void ClearTimerNotice()
    {
        timerAlarm = false; TimerNotification.Visibility = Visibility.Collapsed;
        if (toast == "Таймер завершён") { toast = ""; toastUntil = DateTime.MinValue; }
        UpdateCompact();
    }
}
