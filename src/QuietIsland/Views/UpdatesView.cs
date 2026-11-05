using System.Windows;
using System.Windows.Controls;
using QuietIsland.Controls;
using QuietIsland.Services;

namespace QuietIsland.Views;

public sealed class UpdatesView : IslandView
{
    private readonly TextBlock status = Ui.Text("", 12, "Muted"), notes = Ui.Text("", 12, "Muted");
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100, Height = 5, Margin = new Thickness(0, 10, 0, 10) };
    private readonly Button check, install, cancel, release;
    public UpdatesView(IslandContext context, MainWindow window) : base(context)
    {
        status.TextWrapping = notes.TextWrapping = TextWrapping.Wrap;
        check = Ui.AsyncButton("Проверить обновления", C.Updates.CheckAsync, C.Error);
        install = Ui.AsyncButton("Скачать и установить", window.InstallUpdateAsync, C.Error, true);
        cancel = Ui.Button("Отменить загрузку", C.Updates.CancelDownload);
        release = Ui.Button("Открыть релиз на GitHub", () => { if (C.Updates.Available is { } update) SystemService.Open(update.Page.AbsoluteUri); });
        Content = Ui.Stack(Ui.Text("Quiet Island " + C.Updates.CurrentVersionText, 18, bold: true), status, progress,
            Ui.Row(check, install, cancel), release, notes);
        C.Updates.Changed += Updated; Refresh();
    }
    private void Updated() => Dispatcher.InvokeAsync(() => { if (!Disposed) Refresh(); });
    public override void Refresh()
    {
        var service = C.Updates; var update = service.Available;
        status.Text = service.Status;
        progress.Visibility = cancel.Visibility = service.IsDownloading ? Visibility.Visible : Visibility.Collapsed;
        progress.Value = Math.Clamp(service.Progress, 0, 100);
        check.IsEnabled = !service.IsChecking && !service.IsDownloading;
        install.Visibility = update?.CanInstall == true ? Visibility.Visible : Visibility.Collapsed;
        install.IsEnabled = !service.IsDownloading && !service.IsChecking;
        release.Visibility = update != null ? Visibility.Visible : Visibility.Collapsed;
        notes.Text = update?.Notes is { Length: > 0 } body ? (body.Length > 2000 ? body[..2000] + "…" : body) : "";
    }
    public override void Dispose() { C.Updates.Changed -= Updated; base.Dispose(); }
}
