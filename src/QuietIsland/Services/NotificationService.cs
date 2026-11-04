using Windows.UI.Notifications.Management;

namespace QuietIsland.Services;
public sealed class NotificationService
{
    public bool Enabled { get; private set; }
    public string Status { get; private set; } = "Уведомления Windows не подключены";
    public IReadOnlyList<ActivityItem> Items { get; private set; } = [];
    public async Task EnableAsync()
    {
        try
        {
            var access = await UserNotificationListener.Current.RequestAccessAsync(); Enabled = access == UserNotificationListenerAccessStatus.Allowed;
            Status = Enabled ? "Уведомления Windows подключены" : "Windows не предоставила доступ к уведомлениям. Для этой функции нужен запуск с package identity и разрешение Windows.";
        }
        catch (Exception e) { Enabled = false; Status = "Уведомления Windows требуют установки пакета с package identity. См. packaging/README.md."; Log.Write("Notifications access", e); }
    }
    public async Task PollAsync()
    {
        if (!Enabled) return;
        try
        {
            var notifications = await UserNotificationListener.Current.GetNotificationsAsync(global::Windows.UI.Notifications.NotificationKinds.Toast);
            Items = notifications.OrderByDescending(x => x.CreationTime).Take(20).Select(x => new ActivityItem("▣", x.AppInfo.DisplayInfo.DisplayName, string.Join("\n", x.Notification.Visual.Bindings.SelectMany(b => b.GetTextElements()).Select(t => t.Text)), x.CreationTime)).ToArray();
        }
        catch { Enabled = false; Status = "Доступ к уведомлениям отозван"; Items = []; }
    }
}
