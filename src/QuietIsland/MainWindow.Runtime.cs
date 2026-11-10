using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using QuietIsland.Controls;
using QuietIsland.Services;

namespace QuietIsland;

public partial class MainWindow
{
    private PrintScreenShortcut? printScreen;
    private readonly ResourcePressure pressure = new();
    private bool incomingFileDrag, overlayBlocked, economyMode, fullscreenOverride, runtimeHidden;
    private DateTime lastDragOver, lastCompactSecond;
    private bool DragHeld => incomingFileDrag || FileDrag.IsDragging;
    private void InitializeDragRetention()
    {
        void Over(object? _, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent("QuietIsland.Launcher")) return;
            incomingFileDrag = true; lastDragOver = lastInside = DateTime.Now; HandleDragEnter(e.Data);
        }
        Root.AddHandler(DragDrop.PreviewDragEnterEvent, new DragEventHandler(Over), true);
        Root.AddHandler(DragDrop.PreviewDragOverEvent, new DragEventHandler(Over), true);
        Root.AddHandler(DragDrop.PreviewDropEvent, new DragEventHandler((_, _) => Dispatcher.BeginInvoke(EndIncomingDrag, DispatcherPriority.Background)), true);
        Root.AddHandler(DragDrop.PreviewDragLeaveEvent, new DragEventHandler((_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (!incomingFileDrag || PointerOnSurface()) return;
            EndIncomingDrag();
        }, DispatcherPriority.Background)), true);
    }
    internal void EndIncomingDrag() { incomingFileDrag = false; lastInside = DateTime.Now; }
    private bool PointerOnSurface()
    {
        try { if (!Native.GetCursorPos(out var pointer)) return false; return Fluid.ContainsSurface(Root.PointFromScreen(new Point(pointer.X, pointer.Y))); }
        catch { return false; }
    }
    private void RefreshRuntime(bool hidden)
    {
        runtimeHidden = hidden;
        var active = expanded || Root.IsMouseOver || DragHeld || BluetoothConnectionNoticeActive;
        var slow = c.Settings.AutoReduceResources && economyMode && !active;
        timer.Interval = TimeSpan.FromMilliseconds(hidden || slow ? 1000 : 250);
        interaction.Interval = TimeSpan.FromMilliseconds(hidden ? 1000 : slow ? 400 : 80);
        Fluid.SetRuntime(hidden, slow);
        if (!snapshot)
        {
            c.Media.EconomyMode = slow;
            c.Media.SetAnalysisEnabled(!hidden && c.Settings.MusicAnimationEnabled && c.Media.Current.IsPlaying && c.Media.Current.IsMusic);
        }
    }
    private void UpdateRuntimeVisibility(DateTime now)
    {
        if (snapshot && mirrorOwner == null) return;
        var fullscreen = c.Settings.HideFullscreen && !fullscreenOverride && Native.IsFullscreen(Screen);
        var excluded = c.Settings.ExcludedApps.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Any(x => Native.ForegroundProcess().Equals(x.Replace(".exe", "", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase));
        overlayBlocked = !HasAlarm && (fullscreen || excluded);
        if (overlayBlocked && expanded && !DragHeld) Collapse();
        var autohide = !expanded && !HasAlarm && !BluetoothConnectionNoticeActive && c.Settings.AutoHide && !Root.IsMouseOver && !DragHeld;
        if (!overlayBlocked && Native.GetCursorPos(out var pointer) && pointer.Y >= Screen.Bounds.Top && pointer.Y <= Screen.Bounds.Top + 4 && Math.Abs(pointer.X - (Screen.Bounds.Left + Screen.Bounds.Width / 2)) < 150)
        { autohide = false; lastInside = now; }
        var hidden = hiddenByUser || HiddenForCapture || overlayBlocked || autohide && (now - lastInside).TotalSeconds > 1;
        economyMode = c.Settings.AutoReduceResources && pressure.Sample(fullscreen, c.Settings.ResourceHeavyApps);
        ResizeAndPosition(hidden); RefreshRuntime(hidden);
    }
    private async Task ScreenshotDesktopAsync()
    {
        if (selectionBusy) { c.Notify("Сначала завершите выбор области. Esc отменяет выделение."); return; }
        selectionBusy = true;
        try { using var capture = SuppressForCapture(); Collapse(); await Task.Delay(300); await SaveScreenshotAsync(CaptureService.Screen(System.Windows.Forms.SystemInformation.VirtualScreen)); }
        catch (Exception e) { c.Error(e); }
        finally { selectionBusy = false; }
    }
}
