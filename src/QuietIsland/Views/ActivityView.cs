using System.Windows.Controls;
using QuietIsland.Controls;
using QuietIsland.Services;

namespace QuietIsland.Views;
public sealed class ActivityView : IslandView
{
    private readonly StackPanel rows = new();
    private string signature = "";
    public ActivityView(IslandContext c) : base(c) { Content = Ui.Scroll(Ui.Stack(Ui.Heading("События"), rows)); C.Agents.Changed += Changed; Refresh(); }
    private void Changed() => Dispatcher.InvokeAsync(Refresh);
    public override void Refresh()
    {
        if (Disposed) return;
        var requests = C.Agents.Broker.Pending;
        var key = string.Join(";", requests.Select(x => x.Id)) + string.Join(";", C.Agents.Events.Select(x => x.Time.ToString("O"))) + string.Join(";", C.Activity.Items.Select(x => x.Title + x.Detail)) + string.Join(";", C.Notifications.Items.Select(x => x.Detail)) + C.Obs.Status + C.Obs.Recording + C.Obs.Streaming;
        if (signature == key) return; signature = key; rows.Children.Clear();
        rows.Children.Add(Ui.Text(C.Agents.Status, 11, "Muted"));
        foreach (var request in requests)
        {
            var buttons = Ui.Row(); foreach (var option in request.Options) { var label = option switch { "allow" => "Разрешить", "deny" => "Запретить", _ => option }; buttons.Children.Add(Ui.Button(label, () => { C.Agents.Broker.Answer(request.Id, option); Refresh(); }, option == "allow")); }
            if (request.AllowText) buttons.Children.Add(Ui.Button("Свой ответ…", () => { var text = Windows.Dialogs.Ask(request.Title, request.Message); if (!string.IsNullOrWhiteSpace(text)) C.Agents.Broker.Answer(request.Id, text); Refresh(); }));
            rows.Children.Add(Ui.Card(Ui.Stack(Ui.Text(request.Source + " · ЖДЁТ ОТВЕТА", 10, "Accent"), Ui.Text(request.Title, 17, bold: true), Ui.Text(request.Message, 12), buttons)));
        }
        foreach (var item in C.Activity.Items)
        {
            var row = new DockPanel(); var icon = Icons.Colored(item.Title.Contains("Микрофон", StringComparison.OrdinalIgnoreCase) ? "mic" : "activity", 24); icon.Margin = new System.Windows.Thickness(0, 0, 12, 0); DockPanel.SetDock(icon, Dock.Left); row.Children.Add(icon); row.Children.Add(Ui.Stack(Ui.Text(item.Title, 14, bold: true), Ui.Text(item.Detail, 12, "Muted"))); rows.Children.Add(Ui.Card(row));
        }
        foreach (var item in C.Notifications.Items) rows.Children.Add(Ui.Card(Ui.Stack(Ui.Text(item.Title, 14, bold: true), Ui.Text(item.Detail, 12), Ui.Text(item.Time.ToString("HH:mm"), 10, "Muted"))));
        if (C.Settings.ObsEnabled) rows.Children.Add(Ui.Card(Ui.Stack(Ui.Text(C.Obs.Recording ? "● OBS · запись" : C.Obs.Streaming ? "● OBS · трансляция" : "OBS", 15), Ui.Text(C.Obs.Status, 11, "Muted"))));
        foreach (var item in C.Agents.Events.Take(12)) rows.Children.Add(Ui.Card(Ui.Stack(Ui.Text(item.Source + " · " + item.Time.ToString("HH:mm"), 10, "Muted"), Ui.Text(item.Title, 14, bold: true), Ui.Text(item.Message, 12))));
        if (requests.Count == 0 && C.Activity.Items.Count == 0 && C.Agents.Events.Count == 0) rows.Children.Add(Ui.Empty("◉", "Всё спокойно", "Здесь появятся загрузки, микрофон и камера, события OBS и запросы агентов. Инструкции подключения — в папке integrations проекта."));
    }
    public override void Dispose() { C.Agents.Changed -= Changed; base.Dispose(); }
}

