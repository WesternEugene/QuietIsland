using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using QuietIsland.Core;

namespace QuietIsland.Controls;

/// <summary>Keeps every device reachable when the header or compact island has limited space.</summary>
public sealed class BluetoothBatteryStrip : ScrollViewer
{
    private readonly StackPanel rows = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<string, DeviceRow> entries = new(StringComparer.OrdinalIgnoreCase);
    public bool NotificationLayout { get; set; }
    public double NaturalWidth { get; private set; }
    public BluetoothBatteryStrip()
    {
        Content = rows; HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden; VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        PanningMode = PanningMode.HorizontalOnly; CanContentScroll = false; Focusable = false; IsTabStop = false;
        VerticalAlignment = VerticalAlignment.Center; HorizontalContentAlignment = HorizontalAlignment.Left; Visibility = Visibility.Collapsed;
        PreviewMouseWheel += (_, e) =>
        {
            if (ExtentWidth <= ViewportWidth + .5) return;
            ScrollToHorizontalOffset(HorizontalOffset - e.Delta * .6); e.Handled = true;
        };
    }
    public bool Update(IReadOnlyList<BluetoothBatteryDevice> devices, IReadOnlySet<string> details, bool showNames, double speed, bool animate)
    {
        var changed = false;
        var ids = devices.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in entries.Keys.Where(id => !ids.Contains(id)).ToArray()) { rows.Children.Remove(entries[id]); entries.Remove(id); changed = true; }
        for (var i = 0; i < devices.Count; i++)
        {
            var device = devices[i];
            if (!entries.TryGetValue(device.Id, out var row)) { entries[device.Id] = row = new(NotificationLayout); changed = true; }
            if (rows.Children.IndexOf(row) != i) { rows.Children.Remove(row); rows.Children.Insert(i, row); }
            changed |= row.Update(device, details.Contains(device.Id), showNames, speed, animate);
            row.Margin = new(0, 0, i < devices.Count - 1 ? 12 : 0, 0);
        }
        Visibility = devices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // The scroll viewport may still have the previous frame's allocated width.
        // Derive content width directly from the rows instead of retaining that space.
        NaturalWidth = 0;
        foreach (var row in entries.Values)
        {
            row.InvalidateMeasure(); row.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
            NaturalWidth += Math.Ceiling(row.DesiredSize.Width);
        }
        rows.Width = NaturalWidth;
        ToolTip = string.Join("\n", devices.Select(d => $"{d.Name} · {d.ChargeLabel}")) + (devices.Count > 1 ? "\nПрокрутите, чтобы увидеть все устройства" : "");
        AutomationProperties.SetName(this, "Заряд Bluetooth: " + string.Join(", ", devices.Select(d => $"{d.Name} {d.ChargeLabel}")));
        return changed;
    }
    public void SetBudget(double width) { Width = Math.Min(NaturalWidth, Math.Max(0, width)); }

    private sealed class DeviceRow : StackPanel
    {
        private readonly BatteryRing ring = new() { Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock charge = Ui.Text("", 11, bold: true), name = Ui.Text("", 11, "Muted");
        private readonly ContentControl icon = new() { Width = 17, Height = 17, VerticalAlignment = VerticalAlignment.Center, Margin = new(6, 0, 0, 0) };
        private readonly TranslateTransform shift = new();
        private BluetoothBatteryDevice? previous;
        private bool showingDetails, pendingEntrance;
        private double speed = 1;
        public DeviceRow(bool notification)
        {
            Orientation = Orientation.Horizontal; VerticalAlignment = VerticalAlignment.Center; RenderTransform = shift;
            charge.Margin = new(5, 0, 0, 0); charge.VerticalAlignment = name.VerticalAlignment = VerticalAlignment.Center;
            name.Margin = new(6, 0, 0, 0); name.MaxWidth = 108; name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
            if (notification)
            {
                ring.Width = ring.Height = 32; charge.FontSize = 20; charge.Margin = new(10, 0, 0, 0);
                name.FontSize = 15; name.MaxWidth = 200; name.Margin = new(12, 0, 0, 0);
                name.SetResourceReference(TextBlock.ForegroundProperty, "Text");
                icon.Width = icon.Height = 26; icon.Margin = new(12, 0, 0, 0);
            }
            Children.Add(ring); Children.Add(charge); Children.Add(name); Children.Add(icon);
            Loaded += (_, _) => PlayEntrance();
            IsVisibleChanged += (_, _) => { if (IsVisible) PlayEntrance(); };
            Unloaded += (_, _) => { BeginAnimation(OpacityProperty, null); shift.BeginAnimation(TranslateTransform.XProperty, null); };
        }
        private void PlayEntrance()
        {
            if (!pendingEntrance || !IsLoaded || !IsVisible || previous == null) return;
            pendingEntrance = false;
            var duration = TimeSpan.FromMilliseconds(360 / Math.Clamp(speed, .6, 1.8));
            var fade = new DoubleAnimation(0, 1, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
            var slide = new DoubleAnimation(7, 0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
            fade.Completed += (_, _) => BeginAnimation(OpacityProperty, null);
            slide.Completed += (_, _) => shift.BeginAnimation(TranslateTransform.XProperty, null);
            BeginAnimation(OpacityProperty, fade); shift.BeginAnimation(TranslateTransform.XProperty, slide);
            ring.Progress = 0; ring.SetPercent(previous.Percent, true, speed);
        }
        public bool Update(BluetoothBatteryDevice device, bool showDetails, bool showNames, double speed, bool animate)
        {
            var layoutChanged = showingDetails != showDetails || (name.Visibility == Visibility.Visible) != (showDetails && showNames);
            this.speed = speed;
            if (animate && (previous == null || showDetails && !showingDetails)) pendingEntrance = true;
            showingDetails = showDetails;
            if (previous?.Percent != device.Percent) ring.SetPercent(device.Percent, animate && IsLoaded && IsVisible, speed);
            if (previous?.Kind != device.Kind)
                icon.Content = Icons.Make(device.Kind switch
                {
                    BluetoothDeviceKind.Mouse => "mouse", BluetoothDeviceKind.Keyboard => "keyboard", BluetoothDeviceKind.Headphones => "headphones",
                    BluetoothDeviceKind.Speaker => "speaker", BluetoothDeviceKind.Gamepad => "gamepad", BluetoothDeviceKind.Phone => "phone",
                    BluetoothDeviceKind.Watch => "watch", BluetoothDeviceKind.Pen => "pen", BluetoothDeviceKind.Computer => "computer",
                    BluetoothDeviceKind.Microphone => "mic", _ => "bluetooth"
                }, icon.Width, "Muted");
            charge.Text = device.ChargeLabel; charge.Visibility = showDetails ? Visibility.Visible : Visibility.Collapsed;
            name.Text = device.Name; name.Visibility = showDetails && showNames ? Visibility.Visible : Visibility.Collapsed;
            ToolTip = $"{device.Name} · {device.ChargeLabel}";
            AutomationProperties.SetName(this, (string)ToolTip); previous = device;
            PlayEntrance(); return layoutChanged;
        }
    }
}
