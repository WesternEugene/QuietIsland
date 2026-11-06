using System.Windows;
using System.Windows.Automation;
using QuietIsland.Controls;
using QuietIsland.Core;
using QuietIsland.Services;

namespace QuietIsland;

public partial class MainWindow
{
    private bool timerAlarm;
    private readonly Queue<Reminder> reminderAlarms = new();
    private bool HasAlarm => timerAlarm || reminderAlarms.Count > 0;
    private readonly TimerAlarmSound timerSound = new();
    private void InitializeTimerNotice()
    {
        TimerAgain.Click += (_, _) => RepeatActiveAlarm();
        CompactTimerAgain.Click += (_, _) => RepeatActiveAlarm();
        TimerDismiss.Click += (_, _) => DismissActiveAlarm();
        CompactTimerDismiss.Click += (_, _) => DismissActiveAlarm();
        c.State.Changed += AppStateChanged;
        PresentAlarm();
    }
    private void RepeatActiveAlarm()
    {
        if (mirrorOwner != null) { mirrorOwner.RepeatActiveAlarm(); MirrorTick(); return; }
        if (reminderAlarms.Count == 0) { RepeatTimer(); return; }
        var reminder = reminderAlarms.Dequeue();
        if (reminder.Daily || reminder.Weekdays.Count > 0)
        {
            // Snoozing must not move the original daily/weekly recurrence.
            c.State.Data.Reminders.Add(new Reminder { Title = reminder.Title, Due = DateTimeOffset.Now.AddMinutes(10) });
        }
        else { reminder.Completed = false; reminder.Due = DateTimeOffset.Now.AddMinutes(10); }
        c.State.Touch(); PresentAlarm(); UpdateCompact();
    }
    private void DismissActiveAlarm()
    {
        if (mirrorOwner != null) { mirrorOwner.DismissActiveAlarm(); MirrorTick(); return; }
        if (reminderAlarms.Count > 0) { reminderAlarms.Dequeue(); PresentAlarm(); UpdateCompact(); }
        else ClearTimerNotice();
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
        timerAlarm = true; RevealAlarm();
    }
    private void ReminderFinished(Reminder reminder)
    {
        if (!reminderAlarms.Any(x => x.Id == reminder.Id)) reminderAlarms.Enqueue(reminder);
        RevealAlarm();
    }
    private void RevealAlarm()
    {
        PresentAlarm();
        if (!snapshot)
        {
            hiddenByUser = false; Show(); UpdateRuntimeVisibility(DateTime.Now);
            Native.ConfigureOverlay(this, expanded, false, c.Settings.HideInCapture);
            try { timerSound.Start(); }
            catch (Exception error) when (error is InvalidOperationException or TimeoutException) { Log.Write("Timer sound", error); }
        }
        UpdateCompact();
    }
    private void PresentAlarm()
    {
        if (!snapshot) c.Focus.SetAlarmState(timerAlarm, reminderAlarms.Count > 0);
        var isReminder = reminderAlarms.TryPeek(out var reminder);
        CompactAlarmTitle.Text = TimerNoticeTitle.Text = isReminder ? reminder!.Title : "Таймер";
        CompactAlarmIcon.Content = Icons.Colored(isReminder ? "bell" : "clock", 24);
        TimerNotification.Visibility = HasAlarm ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in new[] { TimerAgain, CompactTimerAgain })
        {
            button.Content = isReminder ? "+10 минут" : Icons.Make("repeat");
            button.Width = isReminder ? 94 : button == TimerAgain ? 32 : 36;
            button.Padding = new Thickness(isReminder ? 10 : 7);
            button.ToolTip = isReminder ? "Напомнить через 10 минут" : "Повторить таймер";
            AutomationProperties.SetName(button, (string)button.ToolTip);
        }
        foreach (var button in new[] { TimerDismiss, CompactTimerDismiss })
        {
            button.Content = Icons.Make("close"); button.Width = isReminder ? 52 : button == TimerDismiss ? 32 : 36;
            button.ToolTip = isReminder ? "Закрыть напоминание" : "Закрыть таймер";
            AutomationProperties.SetName(button, (string)button.ToolTip);
        }
        if (!HasAlarm) timerSound.Stop();
        ResizeExpandedContent();
        if (!snapshot && !IsClosing && hwnd != null)
        {
            UpdateRuntimeVisibility(DateTime.Now);
            Native.ConfigureOverlay(this, expanded, !expanded && !HasAlarm && c.Settings.ClickThrough, c.Settings.HideInCapture);
        }
    }
    private void ClearTimerNotice()
    {
        timerAlarm = false;
        if (toast == "Таймер завершён") { toast = ""; toastUntil = DateTime.MinValue; }
        PresentAlarm(); UpdateCompact();
    }
}
