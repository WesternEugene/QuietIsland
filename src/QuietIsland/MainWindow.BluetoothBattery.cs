using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using QuietIsland.Core;

namespace QuietIsland;

public partial class MainWindow
{
    private static readonly IReadOnlySet<string> NoBluetoothDetails = new HashSet<string>();
    private readonly BluetoothBatteryPresentation bluetoothPresentation = new();
    private readonly RectangleGeometry bluetoothNoticeClip = new();
    private IReadOnlyList<BluetoothBatteryDevice>? lastBluetoothDevices;
    private string bluetoothSettingsKey = "";
    private int bluetoothPresentationRevision;
    private bool bluetoothSizeChanged, bluetoothNoticeShowing;
    private bool BluetoothConnectionNoticeActive => !HasAlarm && c.Settings.ShowBluetoothBattery && bluetoothPresentation.Details.Count > 0
        && (expanded ? c.Settings.BluetoothBatteryExpanded : c.Settings.BluetoothBatteryCompact);

    private void UpdateBluetoothBattery()
    {
        var settings = c.Settings;
        var devices = c.BluetoothBattery.Current;
        var noticeChanged = bluetoothPresentation.Update(devices, DateTimeOffset.Now, settings.ShowBluetoothBattery && settings.BluetoothConnectionNotifications);
        var key = $"{settings.ShowBluetoothBattery}|{settings.BluetoothBatteryCompact}|{settings.BluetoothBatteryExpanded}|{settings.BluetoothBatteryShowNames}|{settings.BluetoothConnectionNotifications}|{settings.AnimationSpeed}";
        if (!noticeChanged && ReferenceEquals(lastBluetoothDevices, devices) && bluetoothSettingsKey == key) return;
        lastBluetoothDevices = devices; bluetoothSettingsKey = key; bluetoothPresentationRevision++;
        var animate = (!snapshot || mirrorOwner != null || NativeMotionProbe) && IsLoaded;
        // The ordinary view measures only permanent indicators; notification text has
        // its own content and cannot leave its previous width in the compact layout.
        bluetoothSizeChanged |= CompactBluetoothBattery.Update(settings.ShowBluetoothBattery && settings.BluetoothBatteryCompact ? devices : [], NoBluetoothDetails, false, settings.AnimationSpeed, animate);
        PanelBluetoothBattery.Update(settings.ShowBluetoothBattery && settings.BluetoothBatteryExpanded ? devices : [], NoBluetoothDetails, false, settings.AnimationSpeed, animate);
        PanelBluetoothBattery.SetBudget(240);
        BluetoothConnectionDevices.Update(devices.Where(d => bluetoothPresentation.Details.Contains(d.Id)).ToArray(), bluetoothPresentation.Details, settings.BluetoothBatteryShowNames, settings.AnimationSpeed, animate);
    }

    /// <summary>Temporarily replaces the shell while preserving its logical view and resting size.</summary>
    private bool PresentBluetoothConnectionShell(bool forceAnimation = false)
    {
        var animate = forceAnimation || (!snapshot || mirrorOwner != null || NativeMotionProbe) && IsLoaded && !overlayBlocked;
        if (BluetoothConnectionNoticeActive)
        {
            var entering = !bluetoothNoticeShowing;
            if (entering)
            {
                bluetoothNoticeShowing = true; ++transition; hover.Stop();
                Compact.BeginAnimation(OpacityProperty, null); Panel.BeginAnimation(OpacityProperty, null);
                panelShift.BeginAnimation(TranslateTransform.YProperty, null); panelShift.Y = 0;
                BluetoothNotice.Clip = bluetoothNoticeClip; BluetoothNotice.Visibility = Visibility.Visible;
                if (animate) BluetoothNotice.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280 / c.Settings.AnimationSpeed))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
            }
            Compact.Visibility = Panel.Visibility = Visibility.Hidden;
            ToastBanner.Visibility = Visibility.Collapsed;
            var width = expanded ? 744 : Math.Min(c.Settings.CompactMaxWidth, Math.Max(300, Math.Max(compactWidth + 28, BluetoothConnectionDevices.NaturalWidth + 64)));
            var height = expanded ? Math.Min(Root.Height - 12, ExpandedShellHeight + 20) : Math.Max(84, compactHeight + 32);
            BluetoothNotice.Width = width - 48; BluetoothNotice.Height = height - 8;
            BluetoothConnectionDevices.SetBudget(width - 64);
            UpdatePanelClip();
            if (entering || motion.Active || Math.Abs(Fluid.VisualWidth - width) > .1 || Math.Abs(Fluid.VisualHeight - height) > .1)
                motion.To(width, height, animate, easing: true);
            return true;
        }
        if (!bluetoothNoticeShowing) return false;
        bluetoothNoticeShowing = false; ++transition; lastInside = DateTime.Now;
        BluetoothNotice.BeginAnimation(OpacityProperty, null); BluetoothNotice.Visibility = Visibility.Collapsed;
        Compact.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        Panel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        Compact.BeginAnimation(OpacityProperty, null); Panel.BeginAnimation(OpacityProperty, null);
        Compact.Opacity = c.Settings.PillOpacity; Panel.Opacity = c.Settings.PanelOpacity;
        var content = expanded ? (UIElement)Panel : Compact;
        if (animate) content.BeginAnimation(OpacityProperty, new DoubleAnimation(0, content.Opacity, TimeSpan.FromMilliseconds(220 / c.Settings.AnimationSpeed))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        motion.To(expanded ? 744 : compactWidth, expanded ? ExpandedShellHeight : compactHeight, animate, () =>
        {
            if (!expanded && !bluetoothNoticeShowing) { ReleaseView(); Body.Content = null; }
        }, easing: true);
        if (!expanded && Root.IsMouseOver && !HasAlarm) hover.Start();
        return true;
    }
}
