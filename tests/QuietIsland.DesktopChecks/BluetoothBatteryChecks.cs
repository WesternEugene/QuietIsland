using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QuietIsland;
using QuietIsland.Controls;
using QuietIsland.Core;
using QuietIsland.Services;
using QuietIsland.Views;

internal static class BluetoothBatteryChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string Folder = Path.GetFullPath("artifacts/verification-bluetooth");
    internal static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        await check("bluetooth_battery_airpods_expires_without_windows_refresh", async () =>
        {
            using var service = new BluetoothBatteryService();
            var type = typeof(BluetoothBatteryService); var sync = type.GetField("sync", Private)!.GetValue(service)!;
            var cache = (AirPodsBatteryCache)type.GetField("airPods", Private)!.GetValue(service)!;
            byte[] packet = new byte[27]; packet[0] = 7; packet[1] = 25; packet[2] = 1; packet[3] = 0x14; packet[4] = 0x20; packet[6] = 0x58;
            var changes = 0; service.Changed += () => Interlocked.Increment(ref changes);
            lock (sync)
            {
                cache.Observe(1, -40, packet, DateTimeOffset.UtcNow.AddSeconds(-119.5));
                type.GetField("current", Private)!.SetValue(service, new BluetoothBatteryDevice[] { new("fixture", "AirPods", 65, BluetoothDeviceKind.Headphones) { IsApproximate = true } });
                type.GetMethod("ScheduleAirPodsExpiryLocked", Private)!.Invoke(service, null);
            }
            Require(service.Current.Count == 1, "The unexpired fixture disappeared before its deadline.");
            await Task.Delay(850);
            Require(service.Current.Count == 0 && changes > 0, "AirPods expiry waited for Windows enumeration or another advertisement.");
        });
        await check("bluetooth_battery_airpods_one_earbud_charge_and_preference", async () =>
        {
            using var context = Context();
            var main = new MainWindow(context, true) { Left = -10000, Top = -10000, Resources = VisualPolishChecks.Styles() };
            try
            {
                main.Show(); await Dispatcher.Yield(DispatcherPriority.Loaded);
                SetDevices(context, [new("airpods", "Eugene’s AirPods Pro", 65, BluetoothDeviceKind.Headphones) { IsApproximate = true }]); Refresh(main);
                var notice = (BluetoothBatteryStrip)main.FindName("BluetoothConnectionDevices");
                Require(VisibleLabels(notice).Contains("≈65%"), "The AirPods notice lost its one approximate earbud percentage.");
                Require(Descendants(notice).OfType<BatteryRing>().Count() == 1, "AirPods created separate earbud or case rings.");
                Require(!VisibleLabels(notice).Any(text => text.Contains("кейс", StringComparison.OrdinalIgnoreCase)), "Case battery appeared in the AirPods notice.");
                Save(main.RenderRoot, "airpods-one-charge.png");
                context.Settings.BluetoothConnectionNotifications = false; Refresh(main); main.UpdateLayout();
                var compact = (BluetoothBatteryStrip)main.FindName("CompactBluetoothBattery");
                Require(!VisibleLabels(compact).Any(text => text.Contains('%') || text.Contains("AirPods")), "AirPods details remained in the resting island.");
                Require(Descendants(compact).OfType<BatteryRing>().Count() == 1, "The persistent AirPods indicator disappeared.");
                using var settings = new SettingsView(context, main) { Resources = VisualPolishChecks.Styles() };
                settings.SelectCategory(settings.Categories.Single(x => x.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase)));
                settings.Measure(new(650, 550)); settings.Arrange(new(0, 0, 650, 550)); settings.UpdateLayout();
                var option = Descendants(settings).OfType<CheckBox>().Single(box => (string)box.Content == "Получать заряд AirPods по Bluetooth LE");
                option.IsChecked = false; option.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                Require(!context.Settings.AirPodsBatteryEnabled, "The AirPods battery preference was not applied.");
            }
            finally { main.Close(); }
        });
        await check("bluetooth_battery_notice_replaces_content_and_restores_size_and_spacing", async () =>
        {
            using var context = Context();
            typeof(WeatherService).GetProperty(nameof(WeatherService.Current))!.SetValue(context.Weather, new WeatherForecast(2, -1, 3, 2, 70, [], [], DateTimeOffset.Now));
            typeof(MediaService).GetProperty(nameof(MediaService.Current))!.SetValue(context.Media, new QuietIsland.Models.MediaSnapshot("Текущая композиция", "Исполнитель", "", false, "fixture", 12, 200));
            var mouse = new BluetoothBatteryDevice("mouse", "HUAWEI Mouse CD23", 76, BluetoothDeviceKind.Mouse);
            var main = new MainWindow(context, true) { Left = -10000, Top = -10000, Resources = VisualPolishChecks.Styles() };
            var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            poll.Tick += (_, _) => Refresh(main);
            try
            {
                main.Show(); await Dispatcher.Yield(DispatcherPriority.Loaded); main.UpdateLayout();
                typeof(MainWindow).GetProperty("NativeMotionProbe", Private)!.SetValue(main, true);
                var surface = (LiquidSurface)main.FindName("Fluid");
                var beforeWidth = surface.VisualWidth; var beforeHeight = surface.VisualHeight;
                Save(main.RenderRoot, "sequence-00-before.png");
                poll.Start(); SetDevices(context, [mouse]); Refresh(main);
                foreach (var name in new[] { "ClockText", "CompactWeather", "CompactTitle", "InputBadge", "Panel" })
                    Require(!((UIElement)main.FindName(name)).IsVisible, "Connection notice must hide the ordinary content: " + name);
                await Task.Delay(90); main.UpdateLayout();
                Require(surface.VisualWidth > beforeWidth && surface.VisualHeight > beforeHeight, "Connection notice must grow in both dimensions.");
                Save(main.RenderRoot, "sequence-01-growing.png");
                await Task.Delay(450); main.UpdateLayout();
                var noticeWidth = surface.VisualWidth;
                Require(surface.VisualHeight >= beforeHeight + 32, "Connection notice did not become taller.");
                Require(surface.VisualHeight <= beforeHeight + 36 && noticeWidth <= beforeWidth + 90, "Connection notice grows too much.");
                var visible = Descendants(main.RenderRoot).OfType<TextBlock>().Where(t => t.IsVisible && t.Text.Length > 0).Select(t => t.Text).ToArray();
                Require(visible.Length == 2 && visible.Contains(mouse.Name) && visible.Contains("76%"), "The notification must contain only device percentage and name alongside its ring and icon: " + string.Join(" | ", visible));
                Save(main.RenderRoot, "sequence-02-connection-only.png");
                await Task.Delay(1600); main.UpdateLayout(); Save(main.RenderRoot, "sequence-03-shrinking.png");
                await Task.Delay(600); main.UpdateLayout();
                Require(Math.Abs(surface.VisualHeight - beforeHeight) < .2 && surface.VisualWidth < noticeWidth - 16, "The island did not return to its resting width and height after its two-second notice.");
                foreach (var name in new[] { "ClockText", "CompactWeather", "CompactTitle", "InputBadge" })
                    Require(((UIElement)main.FindName(name)).IsVisible, "Ordinary content was not restored: " + name);
                var strip = (BluetoothBatteryStrip)main.FindName("CompactBluetoothBattery");
                Require(strip.Width <= 52, "The permanent device indicator retained the notification's text width: " + strip.Width);
                var row = ((StackPanel)strip.Content).Children.OfType<StackPanel>().Single();
                var icon = row.Children.OfType<ContentControl>().Single();
                var input = (FrameworkElement)main.FindName("InputBadge");
                var gap = input.TranslatePoint(new(), main.RenderRoot).X - icon.TranslatePoint(new(icon.ActualWidth, 0), main.RenderRoot).X;
                Require(gap is >= 6 and <= 14, "A blank gap remains between the device icon and keyboard layout: " + gap);
                Save(main.RenderRoot, "sequence-04-restored.png");
                SaveSequence();
                Console.WriteLine($"BLUETOOTH layout: before={beforeWidth:0.0}x{beforeHeight:0.0}; notice={noticeWidth:0.0}; restored={surface.VisualWidth:0.0}x{surface.VisualHeight:0.0}; icon-layout-gap={gap:0.0}");
            }
            finally { poll.Stop(); main.Close(); }
        });
        await check("bluetooth_battery_notice_preserves_an_expanded_view", async () =>
        {
            using var context = Context();
            var main = new MainWindow(context, true) { Left = -10000, Top = -10000, Resources = VisualPolishChecks.Styles() };
            var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            poll.Tick += (_, _) => Refresh(main);
            try
            {
                main.Show(); await Dispatcher.Yield(DispatcherPriority.Loaded);
                main.ShowPanel("notes", false); main.UpdateLayout();
                var panel = (FrameworkElement)main.FindName("Panel");
                var body = (ContentControl)main.FindName("Body"); var previous = body.Content;
                var surface = (LiquidSurface)main.FindName("Fluid"); var previousHeight = surface.VisualHeight;
                typeof(MainWindow).GetProperty("NativeMotionProbe", Private)!.SetValue(main, true);
                poll.Start(); SetDevices(context, [new("mouse", "HUAWEI Mouse CD23", 76, BluetoothDeviceKind.Mouse)]); Refresh(main);
                Require(!panel.IsVisible && ReferenceEquals(body.Content, previous), "Connection notice did not hide and retain the expanded view.");
                typeof(MainWindow).GetMethod("DismissAfterLeave", Private)!.Invoke(main, [false, 10000d]);
                Require((bool)typeof(MainWindow).GetField("expanded", Private)!.GetValue(main)!, "The hidden expanded view was auto-collapsed during its notification.");
                await Task.Delay(500); main.UpdateLayout(); Save(main.RenderRoot, "expanded-connection-only.png");
                Require(surface.VisualHeight > previousHeight, "Expanded notification did not animate its height.");
                await Task.Delay(2200); main.UpdateLayout();
                Require(panel.IsVisible && ReferenceEquals(body.Content, previous), "The previous expanded content did not return after the notification.");
                Require(Math.Abs(surface.VisualHeight - previousHeight) < .2 && Math.Abs(surface.VisualWidth - 744) < .2, "Expanded notification retained an enlarged size.");
                Save(main.RenderRoot, "expanded-restored.png");
            }
            finally { poll.Stop(); main.Close(); }
        });
        await check("bluetooth_battery_connection_notice_expires_and_reconnects", async () =>
        {
            using var context = Context();
            var mouse = new BluetoothBatteryDevice("mouse", "HUAWEI Mouse CD23", 76, BluetoothDeviceKind.Mouse);
            var main = new MainWindow(context, true) { Left = -10000, Top = -10000, Resources = VisualPolishChecks.Styles() };
            try
            {
                main.Show(); await Dispatcher.Yield(DispatcherPriority.Loaded);
                typeof(MainWindow).GetProperty("NativeMotionProbe", Private)!.SetValue(main, true);
                SetDevices(context, [mouse]); Refresh(main);
                var compact = (BluetoothBatteryStrip)main.FindName("CompactBluetoothBattery");
                var panel = (BluetoothBatteryStrip)main.FindName("PanelBluetoothBattery");
                var notice = (BluetoothBatteryStrip)main.FindName("BluetoothConnectionDevices");
                Require(VisibleLabels(notice).Contains(mouse.Name) && VisibleLabels(notice).Contains("76%"), "New connection did not reveal name and percentage.");
                await Task.Delay(500); Save(main.RenderRoot, "connection-notice.png");
                var noticeWidth = main.NotchWidthForTest();
                await Task.Delay(1650); Refresh(main);
                Require(!VisibleLabels(compact).Any(x => x == mouse.Name || x.Contains('%')), "Device details remain visible after the connection notice expires.");
                Require(!VisibleLabels(panel).Any(x => x == mouse.Name || x.Contains('%')), "Expanded header still shows persistent device details.");
                Require(Descendants(compact).OfType<BatteryRing>().Single().Visibility == Visibility.Visible, "Expiring the notice removed the persistent ring.");
                await Task.Delay(500); main.UpdateLayout(); Save(main.RenderRoot, "connection-settled.png");
                Require(main.NotchWidthForTest() < noticeWidth - 16, "The island did not ease back to its compact width.");
                SetDevices(context, [mouse with { Percent = 75 }]); Refresh(main);
                Require(!VisibleLabels(compact).Any(x => x.Contains('%')), "A battery refresh replayed the connection notice.");
                SetDevices(context, []); Refresh(main); SetDevices(context, [mouse]); Refresh(main);
                Require(VisibleLabels(notice).Contains(mouse.Name), "Reconnecting the same device did not replay the notice.");
                await Task.Delay(100);
                Require(Descendants(notice).OfType<BatteryRing>().Single().Progress is > 0 and < .76, "Reconnected device did not animate its ring.");
                context.Settings.BluetoothConnectionNotifications = false; Refresh(main);
                SetDevices(context, []); Refresh(main); SetDevices(context, [mouse]); Refresh(main);
                Require(!VisibleLabels(compact).Any(x => x == mouse.Name || x.Contains('%')), "Disabling connection notices leaves device details visible.");
            }
            finally { main.Close(); }
        });
        await check("bluetooth_battery_positions_disconnect_and_preferences", async () =>
        {
            using var context = Context();
            context.Settings.ShowWeather = true;
            typeof(WeatherService).GetProperty(nameof(WeatherService.Current))!.SetValue(context.Weather, new WeatherForecast(2, -1, 3, 2, 70, [], [], DateTimeOffset.Now));
            var main = new MainWindow(context, true) { Left = -10000, Top = -10000, Resources = VisualPolishChecks.Styles() };
            try
            {
                main.Show(); await Dispatcher.Yield(DispatcherPriority.Loaded); main.UpdateLayout();
                var before = main.NotchWidthForTest();
                typeof(MainWindow).GetProperty("NativeMotionProbe", Private)!.SetValue(main, true);
                SetDevices(context, [new("mouse", "HUAWEI Mouse CD23", 76, BluetoothDeviceKind.Mouse)]);
                Refresh(main);
                var target = ((FrameworkElement)main.FindName("BluetoothNotice")).Width + 48;
                await Task.Delay(90);
                Require(main.NotchWidthForTest() > before && main.NotchWidthForTest() < target, "Battery-driven resizing jumps directly to its final width.");
                await Task.Delay(500); main.UpdateLayout();
                Require(Math.Abs(main.NotchWidthForTest() - target) < 1, "Battery-driven resizing did not settle.");
                typeof(MainWindow).GetProperty("NativeMotionProbe", Private)!.SetValue(main, false);
                var compact = (BluetoothBatteryStrip)main.FindName("CompactBluetoothBattery");
                var panel = (BluetoothBatteryStrip)main.FindName("PanelBluetoothBattery");
                Require(compact.Visibility == Visibility.Visible && main.NotchWidthForTest() > before + 60, "Connecting a battery peripheral did not expand the compact island.");
                var notice = (BluetoothBatteryStrip)main.FindName("BluetoothConnectionDevices");
                Require(VisibleLabels(notice).Contains("76%") && VisibleLabels(notice).Contains("HUAWEI Mouse CD23"), "Connection notice lost the Windows percentage/name.");
                var meta = (StackPanel)main.FindName("CompactMeta");
                Require(meta.Children.IndexOf(compact) == meta.Children.IndexOf((UIElement)main.FindName("CompactWeatherGroup")) + 1, "Battery strip is not immediately after compact weather.");
                Save(main.RenderRoot, "compact-mouse.png");
                context.Settings.BluetoothConnectionNotifications = false; main.ApplySettings();
                main.ShowPanel("home", false); main.UpdateLayout();
                var clock = (TextBlock)main.FindName("PanelClock");
                var header = (StackPanel)clock.Parent;
                Require(header.Children.IndexOf(panel) + 1 == header.Children.IndexOf(clock), "Expanded battery is not immediately before the clock.");
                Require(panel.TranslatePoint(new(panel.ActualWidth, 0), header).X <= clock.TranslatePoint(new(), header).X, "Expanded battery overlaps the clock.");
                Save(main.RenderRoot, "expanded-mouse.png");
                context.Settings.BluetoothBatteryCompact = false; main.ApplySettings(); main.UpdateLayout();
                Require(compact.Visibility == Visibility.Collapsed && panel.Visibility == Visibility.Visible, "Compact preference affected the expanded preference.");
                context.Settings.BluetoothBatteryExpanded = false; main.ApplySettings();
                Require(panel.Visibility == Visibility.Collapsed, "Expanded preference did not hide the battery.");
                context.Settings.BluetoothBatteryCompact = context.Settings.BluetoothBatteryExpanded = true;
                context.Settings.BluetoothBatteryShowNames = false; main.ApplySettings();
                Require(!Descendants(compact).OfType<TextBlock>().Any(x => x.Text == "HUAWEI Mouse CD23" && x.Visibility == Visibility.Visible), "Device names cannot be disabled.");
                context.Settings.ShowBluetoothBattery = false; main.ApplySettings();
                Require(compact.Visibility == Visibility.Collapsed && panel.Visibility == Visibility.Collapsed, "Master battery switch did not hide both modes.");
                context.State.Save(); var saved = new DesktopState(context.State.DirectoryPath).Data.Settings;
                Require(!saved.ShowBluetoothBattery && !saved.BluetoothBatteryShowNames, "Bluetooth preferences did not survive saving.");
                context.Settings.ShowBluetoothBattery = true; context.Settings.BluetoothBatteryShowNames = true;
                SetDevices(context, []); main.ApplySettings();
                Require(compact.Visibility == Visibility.Collapsed && panel.Visibility == Visibility.Collapsed, "An offline device is still visible.");
                main.Collapse(); main.UpdateLayout();
                Require(main.NotchWidthForTest() <= before + 1, "Removing the last device left an empty space in the island.");
            }
            finally { main.Close(); }
        });
        await check("bluetooth_battery_long_names_multiple_devices_and_settings_controls", async () =>
        {
            using var context = Context();
            SetDevices(context, [new("1", new string('М', 180), 100, BluetoothDeviceKind.Mouse), new("2", "Наушники", 50, BluetoothDeviceKind.Headphones), new("3", "Колонка", 30, BluetoothDeviceKind.Speaker), new("4", "Клавиатура", 1, BluetoothDeviceKind.Keyboard)]);
            context.Settings.CompactMaxWidth = 360;
            var main = new MainWindow(context, true) { Left = -10000, Top = -10000, Resources = VisualPolishChecks.Styles() };
            try
            {
                main.Show(); await Dispatcher.Yield(DispatcherPriority.Loaded); Refresh(main);
                var compact = (BluetoothBatteryStrip)main.FindName("CompactBluetoothBattery");
                var notice = (BluetoothBatteryStrip)main.FindName("BluetoothConnectionDevices");
                Require(main.NotchWidthForTest() <= 360 && notice.ExtentWidth > notice.ViewportWidth, "Connection details do not fit the configured maximum or cannot scroll.");
                Require(Descendants(compact).OfType<BatteryRing>().Count() == 4, "A connected device vanished to fit the available width.");
                var clock = (TextBlock)main.FindName("ClockText");
                Require(clock.TranslatePoint(new(clock.ActualWidth, 0), main.RenderRoot).X <= compact.TranslatePoint(new(), main.RenderRoot).X, "Battery strip overlaps the compact clock.");
                Save(main.RenderRoot, "compact-many.png");
                main.ShowPanel("home", false); main.UpdateLayout(); Save(main.RenderRoot, "expanded-many.png");
                using var settings = new SettingsView(context, main) { Resources = VisualPolishChecks.Styles() };
                settings.SelectCategory(settings.Categories.Single(x => x.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase)));
                settings.Measure(new(650, 550)); settings.Arrange(new(0, 0, 650, 550)); settings.UpdateLayout();
                var controls = Descendants(settings).OfType<CheckBox>().ToArray();
                Require(controls.Count(x => x.IsChecked == true) >= 4, "Bluetooth visibility/name controls are missing from settings.");
                var master = controls.Single(x => (string)x.Content == "Показывать заряд Bluetooth-устройств");
                master.IsChecked = false; master.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                Require(!context.Settings.ShowBluetoothBattery && compact.Visibility == Visibility.Collapsed, "Settings checkbox did not apply Bluetooth visibility immediately.");
                master.IsChecked = true; master.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                settings.UpdateLayout();
                Save(settings, "settings.png");
            }
            finally { main.Close(); }
        });
        await check("bluetooth_battery_hollow_ring_colors_caps_and_easing", async () =>
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Black };
            foreach (var percent in new[] { 0, 1, 30, 31, 50, 51, 76, 98, 99, 100 })
            {
                var ring = new BatteryRing { Width = 64, Height = 64 }; ring.SetPercent(percent, false);
                ring.Measure(new(64, 64)); ring.Arrange(new(0, 0, 64, 64)); ring.UpdateLayout();
                var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32); bitmap.Render(ring);
                var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(32, 32, 1, 1), pixel, 4, 0);
                Require(pixel[3] == 0, "Battery ring has a filled center at " + percent);
                if (percent >= 30)
                {
                    bitmap.CopyPixels(new Int32Rect(58, 32, 1, 1), pixel, 4, 0);
                    var expected = percent > 50 ? new byte[] { 139, 217, 100 } : percent > 30 ? new byte[] { 85, 204, 245 } : new byte[] { 104, 98, 255 };
                    Require(Enumerable.Range(0, 3).All(i => Math.Abs(pixel[i] - expected[i]) <= 2), "Battery ring has the wrong charge color at " + percent + ": " + string.Join(",", pixel));
                }
                if (percent == 50)
                {
                    bitmap.CopyPixels(new Int32Rect(29, 58, 1, 1), pixel, 4, 0);
                    Require(pixel[2] > 200 && pixel[1] > 150, "The battery arc has a flat end instead of a rounded cap.");
                }
                ring.Margin = new(8); row.Children.Add(ring);
            }
            row.Measure(new(double.PositiveInfinity, 80)); row.Arrange(new(0, 0, row.DesiredSize.Width, 80)); row.UpdateLayout(); Save(row, "rings.png");
            var animated = new BatteryRing { Width = 24, Height = 24 };
            animated.SetPercent(100, true); await Task.Delay(100);
            Require(animated.Progress > 0 && animated.Progress < 1, "Battery progress jumps instead of easing.");
            await Task.Delay(500);
            Require(Math.Abs(animated.Progress - 1) < .001 && !animated.HasAnimatedProperties, "Battery animation did not settle/release its clock.");
        });
        if (Environment.GetEnvironmentVariable("QUIET_ISLAND_BLUETOOTH_LIVE") == "1")
        {
            await check("bluetooth_battery_live_windows_mouse", async () =>
            {
                using var service = new BluetoothBatteryService();
                await service.RefreshAsync();
                var mouse = service.Current.SingleOrDefault(d => d.Name == "HUAWEI Mouse CD23");
                Require(mouse is { Kind: BluetoothDeviceKind.Mouse, Percent: >= 0 and <= 100 }, "Connected HUAWEI mouse did not expose its Windows battery.");
                Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder, "live-windows.json"), JsonSerializer.Serialize(service.Current, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("LIVE Bluetooth: " + JsonSerializer.Serialize(service.Current));
                service.Start(); await Task.Delay(1500);
                Require(service.Current.Any(d => d.Name == "HUAWEI Mouse CD23"), "Starting watchers discarded the live mouse reading.");
            });
        }
    }
    private static IslandContext Context() => new(Path.Combine(Path.GetTempPath(), "island-bluetooth-" + Guid.NewGuid().ToString("N")));
    private static void SetDevices(IslandContext c, IReadOnlyList<BluetoothBatteryDevice> devices) => typeof(BluetoothBatteryService).GetField("current", Private)!.SetValue(c.BluetoothBattery, devices);
    private static void Refresh(MainWindow main) { typeof(MainWindow).GetMethod("UpdateCompact", Private)!.Invoke(main, null); main.UpdateLayout(); }
    private static double NotchWidthForTest(this MainWindow main) => ((LiquidSurface)main.FindName("Fluid")).VisualWidth;
    private static string[] Labels(DependencyObject root) => Descendants(root).OfType<TextBlock>().Select(x => x.Text).ToArray();
    private static string[] VisibleLabels(DependencyObject root) => Descendants(root).OfType<TextBlock>().Where(x => x.Visibility == Visibility.Visible).Select(x => x.Text).ToArray();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    private static void Save(FrameworkElement element, string name)
    {
        Directory.CreateDirectory(Folder);
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(element.ActualWidth)), Math.Max(1, (int)Math.Ceiling(element.ActualHeight)), 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        CaptureService.SavePng(bitmap, Path.Combine(Folder, name));
    }
    private static void SaveSequence()
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(48, 51, 61)), null, new Rect(0, 0, 744, 450));
            var frames = new[] { ("sequence-00-before.png", "До подключения"), ("sequence-02-connection-only.png", "Подключение: только устройство"), ("sequence-04-restored.png", "После: обычное содержимое и индикатор устройства") };
            for (var i = 0; i < frames.Length; i++)
            {
                var source = new BitmapImage(new Uri(Path.Combine(Folder, frames[i].Item1)));
                drawing.DrawImage(new CroppedBitmap(source, new Int32Rect(0, 0, 744, 120)), new Rect(0, i * 150, 744, 120));
                drawing.DrawText(new FormattedText(frames[i].Item2, System.Globalization.CultureInfo.GetCultureInfo("ru-RU"), FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.White, 1), new Point(14, i * 150 + 126));
            }
        }
        var bitmap = new RenderTargetBitmap(744, 450, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        CaptureService.SavePng(bitmap, Path.Combine(Folder, "connection-sequence.png"));
    }
}
