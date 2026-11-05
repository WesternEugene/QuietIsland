using System.Diagnostics;
using System.Windows;

namespace QuietIsland;

public partial class MainWindow
{
    private string? announcedUpdate;
    private void UpdatesChanged() => Dispatcher.InvokeAsync(() =>
    {
        if (snapshot || IsClosing || !c.Settings.AutomaticUpdateChecks || c.Updates.Available is not { } update || announcedUpdate == update.DisplayVersion) return;
        announcedUpdate = update.DisplayVersion;
        tray?.ShowBalloonTip(10000, "Доступна Quiet Island " + update.DisplayVersion,
            "Нажмите, чтобы скачать и установить обновление.", System.Windows.Forms.ToolTipIcon.Info);
    });
    private async Task CheckForUpdatesAsync()
    {
        ShowSettingsPage("Обновления");
        await c.Updates.CheckAsync();
    }
    internal async Task InstallUpdateAsync()
    {
        if (c.Updates.Available is not { CanInstall: true } update) return;
        bool Busy() => c.Recording.IsStarting || c.Recording.IsRecording || c.Recording.IsFinalizing || c.Speech.IsListening || c.Speech.IsTranscribing;
        if (Busy()) { c.Notify("Сначала остановите запись экрана или диктовку, затем установите обновление."); return; }
        try
        {
            var path = await c.Updates.DownloadAsync(update, Path.Combine(c.State.DirectoryPath, "updates"));
            if (Busy()) { c.Notify("Установщик загружен. Остановите запись и снова нажмите «Скачать и установить»."); return; }
            c.State.Save();
            var info = new ProcessStartInfo(path) { UseShellExecute = true };
            info.ArgumentList.Add("/SP-"); info.ArgumentList.Add("/NORESTART");
            using var installer = Process.Start(info) ?? throw new InvalidOperationException("Не удалось открыть установщик обновления.");
            Close();
        }
        catch (OperationCanceledException) { }
        catch (System.ComponentModel.Win32Exception error) { c.Error(new InvalidOperationException("Не удалось открыть установщик. Повторите установку или скачайте его со страницы релиза.", error)); }
        catch (Exception error) { c.Error(error); }
    }
}
