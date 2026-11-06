using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NAudio.CoreAudioApi;
using QuietIsland.Controls;
using QuietIsland.Services;

namespace QuietIsland.Views;
public sealed class ControlView : IslandView
{
    private readonly Slider volume = new() { Minimum = 0, Maximum = 100, Margin = new Thickness(0, 9, 0, 9) };
    private readonly TextBlock volumeLabel = Ui.Text("Громкость", 12), battery = Ui.Text("", 12, "Muted");
    private readonly TextBlock wifiTitle = Ui.Text("Wi-Fi", 14, bold: true), wifiDetail = Ui.Text("Проверка…", 11, "Muted"), bluetoothTitle = Ui.Text("Bluetooth", 14, bold: true), bluetoothDetail = Ui.Text("Проверка…", 11, "Muted");
    private readonly Button microphone, wifi, bluetooth, happ;
    private readonly TextBlock microphoneLabel;
    private readonly TextBlock happTitle = Ui.Text("Happ VPN", 14, bold: true), happDetail = Ui.Text("Проверка…", 11, "Muted");
    private bool happBusy;
    private readonly StackPanel peers = new();
    private readonly CancellationTokenSource lifetime = new();
    private bool updating, reading, peersVisible;
    private DateTime lastRead;
    public ControlView(IslandContext c) : base(c)
    {
        var root = new DockPanel();
        var connections = new Grid { Margin = new Thickness(0, 0, 0, 4) }; connections.ColumnDefinitions.Add(new()); connections.ColumnDefinitions.Add(new() { Width = new GridLength(10) }); connections.ColumnDefinitions.Add(new());
        wifi = Ui.AsyncButton("Wi-Fi", async () => { await C.Devices.SetWifiAsync(!C.Devices.Current.WifiOn, lifetime.Token); RefreshLabels(); }, C.Error);
        bluetooth = Ui.AsyncButton("Bluetooth", async () => { await C.Devices.SetBluetoothAsync(!C.Devices.Current.BluetoothOn, lifetime.Token); RefreshLabels(); }, C.Error);
        UIElement Connection(string kind, TextBlock title, TextBlock detail)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal }; var icon = Icons.Colored(kind, 26); icon.Margin = new Thickness(0, 0, 12, 0); icon.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(icon); row.Children.Add(Ui.Stack(title, detail)); return row;
        }
        wifi.Content = Connection("wifi", wifiTitle, wifiDetail); bluetooth.Content = Connection("bluetooth", bluetoothTitle, bluetoothDetail);
        foreach (var b in new[] { wifi, bluetooth }) { b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Padding = new Thickness(15, 14, 15, 14); b.Margin = new Thickness(0, 0, 0, 8); b.MinHeight = 77; }
        wifiDetail.Margin = bluetoothDetail.Margin = new Thickness(0, 6, 0, 0);
        connections.Children.Add(wifi);
        var bt = new Grid(); bt.ColumnDefinitions.Add(new()); bt.ColumnDefinitions.Add(new() { Width = new GridLength(40) }); bt.Children.Add(bluetooth);
        var listButton = Ui.AsyncButton("", TogglePeersAsync, C.Error); listButton.Content = Icons.Make("chevron-down", 17); listButton.ToolTip = "Сопряжённые Bluetooth-устройства"; listButton.Margin = new Thickness(4, 0, 0, 8); listButton.Padding = new Thickness(7); Grid.SetColumn(listButton, 1); bt.Children.Add(listButton);
        Grid.SetColumn(bt, 2); connections.Children.Add(bt);

        microphone = Ui.Button("Микрофон", () => { try { C.System.ToggleMicrophone(); RefreshLabels(); } catch (Exception e) { C.Error(e); } });
        microphoneLabel = IconLabel(microphone, "mic", "Микрофон");
        var outputs = new ComboBox { ItemTemplate = Ui.OptionLabel("Name"), SelectedValuePath = "Id" };
        try { outputs.ItemsSource = C.System.AudioDevices(DataFlow.Render).Select(x => new DeviceOption(x.Id, x.Name)).Prepend(new DeviceOption("", "Звук Windows по умолчанию")).ToArray(); outputs.SelectedValue = C.Settings.AudioOutputId; } catch { }
        outputs.SelectionChanged += (_, _) => { if (outputs.SelectedValue is string id) { C.Settings.AudioOutputId = id; C.State.Touch(); C.Media.StartAudio(id); RefreshLabels(); } };
        volume.ValueChanged += (_, _) => { if (!updating) { try { C.System.Volume = (float)(volume.Value / 100); volumeLabel.Text = $"Громкость · {volume.Value:0}%"; } catch (Exception e) { C.Error(e); } } };
        happ = Ui.AsyncButton("Happ VPN", async () =>
        {
            happBusy = true; happTitle.Text = "Happ · переключаю…";
            try { await C.Happ.ToggleAsync(); C.Notify(C.Happ.Current.Detail); }
            finally { happBusy = false; if (!Disposed) RefreshLabels(); }
        }, C.Error);
        happ.Content = Connection("lock", happTitle, happDetail); happ.HorizontalContentAlignment = HorizontalAlignment.Left; happ.Padding = new Thickness(15, 12, 15, 12); happ.Margin = new Thickness(0, 0, 0, 8);
        var theme = Ui.Button("Тёмная / светлая тема", SystemService.ToggleSystemTheme); IconLabel(theme, "theme", "Тема Windows");
        var sound = Ui.Button("Звук…", () => SystemService.Command("sound")); IconLabel(sound, "volume", "Настройки звука");
        var system = StretchRow(theme, microphone, sound);
        var locked = Ui.Button("Заблокировать", () => Native.LockWorkStation()); IconLabel(locked, "lock", "Заблокировать", true);
        var power = StretchRow(locked, PowerButton("sleep", "Сон", () => SetSuspendState(false, false, false)), PowerButton("restart", "Перезагрузка", () => Shutdown("/r")), PowerButton("power", "Выключение", () => Shutdown("/s")));
        var notifications = Ui.AsyncButton("Уведомления Windows…", async () => { await C.Notifications.EnableAsync(); C.Notify(C.Notifications.Status); }, C.Error);
        IconLabel(notifications, "notifications", "Уведомления Windows…"); notifications.HorizontalContentAlignment = HorizontalAlignment.Left;
        var content = Ui.Stack(connections, peers, happ, Ui.Card(Ui.Stack(volumeLabel, volume, outputs)), power, system, battery, notifications);
        root.Children.Add(Ui.Scroll(content)); Content = root; RefreshLabels(); _ = ReadAsync();
    }
    private async Task ReadAsync()
    {
        if (reading || Disposed) return; reading = true; lastRead = DateTime.Now;
        try { await Task.WhenAll(C.Devices.RefreshAsync(lifetime.Token), C.Happ.RefreshAsync(lifetime.Token)); if (!Disposed) RefreshLabels(); }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!Disposed) C.Error(e); }
        finally { reading = false; }
    }
    private async Task TogglePeersAsync()
    {
        peersVisible = !peersVisible; peers.Children.Clear(); if (!peersVisible) return;
        peers.Children.Add(Ui.Text("Ищу сопряжённые устройства…", 11, "Muted"));
        try
        {
            var devices = await C.Devices.GetBluetoothDevicesAsync(lifetime.Token); if (Disposed || !peersVisible) return;
            peers.Children.Clear();
            if (devices.Count == 0) peers.Children.Add(Ui.Text("Нет сопряжённых устройств", 12, "Muted"));
            foreach (var peer in devices)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
                if (peer.CanConnect)
                {
                    var toggle = Ui.AsyncButton(peer.Connected ? "Отключить" : "Подключить", async () =>
                    {
                        if (peer.Connected) await C.Devices.DisconnectBluetoothAsync(peer.Id, lifetime.Token); else await C.Devices.ConnectBluetoothAsync(peer.Id, lifetime.Token);
                        peersVisible = false; await TogglePeersAsync();
                    }, C.Error);
                    DockPanel.SetDock(toggle, Dock.Right); row.Children.Add(toggle);
                }
                var label = Ui.Stack(Ui.Text(peer.Name, 12), Ui.Text(peer.Connected ? "Подключено" : peer.CanConnect ? "Аудиоустройство · не подключено" : "Сопряжено · подключением управляет Windows", 10, "Muted")); row.Children.Add(label); peers.Children.Add(row);
            }
            var windows = Ui.Button("Добавить устройство в Windows…", () => SystemService.Open("ms-settings:bluetooth")); peers.Children.Add(windows);
        }
        catch (OperationCanceledException) { }
    }
    private void RefreshLabels()
    {
        var vpn = C.Happ.Current;
        if (!happBusy) { happTitle.Text = vpn.Connected ? "Happ VPN · включён" : "Happ VPN · включить"; happ.IsEnabled = vpn.CanToggle; }
        happDetail.Text = vpn.Detail; happ.Background = vpn.Connected ? Icons.Soft("lock") : Ui.Brush("Card");
        var state = C.Devices.Current;
        wifiTitle.Text = "Wi-Fi · " + (state.WifiOn ? "включён" : "выключен"); wifiDetail.Text = state.WifiDetail; wifi.IsEnabled = state.WifiAvailable;
        bluetoothTitle.Text = "Bluetooth · " + (state.BluetoothOn ? "включён" : "выключен"); bluetoothDetail.Text = state.BluetoothDetail; bluetooth.IsEnabled = state.BluetoothAvailable;
        wifi.Background = state.WifiOn ? Icons.Soft("wifi") : Ui.Brush("Card"); bluetooth.Background = state.BluetoothOn ? Icons.Soft("bluetooth") : Ui.Brush("Card");
        updating = true; if (!volume.IsMouseCaptureWithin) volume.Value = C.System.Volume * 100; updating = false;
        volumeLabel.Text = $"Громкость · {volume.Value:0}%"; microphoneLabel.Text = C.System.MicrophoneMuted ? "Включить микрофон" : "Выключить микрофон"; AutomationProperties.SetName(microphone, microphoneLabel.Text);
        var power = System.Windows.Forms.SystemInformation.PowerStatus; battery.Text = power.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) ? "Питание от сети" : $"Батарея {power.BatteryLifePercent * 100:0}% · {power.PowerLineStatus}";
    }
    public override void Refresh() { RefreshLabels(); if ((DateTime.Now - lastRead).TotalSeconds >= 4) _ = ReadAsync(); }
    private static UniformGrid StretchRow(params Button[] buttons)
    {
        var row = new UniformGrid { Columns = buttons.Length, Margin = new Thickness(0, 0, 0, 10) };
        for (var i = 0; i < buttons.Length; i++) { buttons[i].Margin = new Thickness(0, 0, i == buttons.Length - 1 ? 0 : 8, 0); row.Children.Add(buttons[i]); }
        return row;
    }
    private static TextBlock IconLabel(Button button, string kind, string label, bool stacked = false)
    {
        var text = Ui.Text(label, 12); text.TextWrapping = TextWrapping.Wrap; text.TextAlignment = TextAlignment.Center; text.VerticalAlignment = VerticalAlignment.Center;
        button.Resources["SystemActionIcon"] = Ui.Brush("Muted");
        var icon = Icons.Make(kind, stacked ? 21 : 18, "SystemActionIcon"); icon.VerticalAlignment = VerticalAlignment.Center; icon.HorizontalAlignment = HorizontalAlignment.Center; icon.Margin = stacked ? new Thickness(0, 0, 0, 5) : new Thickness(0, 0, 8, 0);
        text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        button.MouseEnter += (_, _) => { button.Resources["SystemActionIcon"] = new SolidColorBrush(kind == "power" ? Color.FromRgb(244, 86, 86) : Icons.Accent(kind)); text.SetResourceReference(TextBlock.ForegroundProperty, "Text"); };
        button.MouseLeave += (_, _) => { button.Resources["SystemActionIcon"] = Ui.Brush("Muted"); text.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); };
        var content = new StackPanel { Orientation = stacked ? Orientation.Vertical : Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; content.Children.Add(icon); content.Children.Add(text);
        button.Content = content; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Center; button.Padding = new Thickness(12, 9, 12, 9); button.Margin = new Thickness(0); button.MinHeight = stacked ? 72 : 48;
        AutomationProperties.SetName(button, label); return text;
    }
    private Button PowerButton(string kind, string label, Action action)
    {
        long armed = 0;
        var shrink = new ScaleTransform(1, 1);
        var bar = new Border { Height = 3, VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(kind == "power" ? Color.FromRgb(244, 86, 86) : Icons.Accent(kind)), Visibility = Visibility.Collapsed, RenderTransform = shrink, RenderTransformOrigin = new Point(0, .5), IsHitTestVisible = false };
        var button = Ui.Button(label, () =>
        {
            if (armed != 0 && System.Diagnostics.Stopwatch.GetElapsedTime(armed).TotalSeconds < 4)
            { armed = 0; shrink.BeginAnimation(ScaleTransform.ScaleXProperty, null); bar.Visibility = Visibility.Collapsed; action(); }
            else
            {
                armed = System.Diagnostics.Stopwatch.GetTimestamp(); bar.Visibility = Visibility.Visible;
                var stamp = armed;
                var animation = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(4)) { FillBehavior = FillBehavior.Stop };
                animation.Completed += (_, _) => { if (armed != stamp) return; armed = 0; bar.Visibility = Visibility.Collapsed; };
                shrink.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            }
        });
        IconLabel(button, kind, label, true);
        var content = (UIElement)button.Content; button.Content = null;
        var host = new Grid(); host.Children.Add(content); host.Children.Add(bar); button.Content = host;
        button.ToolTip = label + " · два нажатия в течение 4 секунд";
        return button;
    }
    private static void Shutdown(string mode) { var info = new System.Diagnostics.ProcessStartInfo("shutdown.exe") { UseShellExecute = false, CreateNoWindow = true }; info.ArgumentList.Add(mode); info.ArgumentList.Add("/t"); info.ArgumentList.Add("0"); System.Diagnostics.Process.Start(info); }
    [DllImport("powrprof.dll")] private static extern bool SetSuspendState(bool hibernate, bool force, bool disableWake);
    private record DeviceOption(string Id, string Name) { public override string ToString() => Name; }
    public override void Dispose() { lifetime.Cancel(); lifetime.Dispose(); base.Dispose(); }
}
