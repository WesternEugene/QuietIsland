using System.Windows.Interop;
using QuietIsland.Services;

namespace QuietIsland;

public partial class MainWindow
{
    private int captureSuppressions;
    private bool HiddenForCapture => c.Settings.HideInCapture && ((mirrorOwner ?? this).captureSuppressions > 0
        || c.Recording.IsStarting || c.Recording.IsRecording || c.Recording.IsFinalizing);

    internal void SetCapturePreference(bool hidden)
    {
        c.Settings.HideInCapture = hidden; c.State.Touch();
        var owner = mirrorOwner ?? this;
        foreach (var window in owner.mirrors.Values.Prepend(owner))
        {
            if (window.IsClosing || new WindowInteropHelper(window).Handle == IntPtr.Zero) continue;
            Native.ConfigureOverlay(window, window.expanded && !window.HiddenForCapture,
                !window.expanded && !window.HasAlarm && c.Settings.ClickThrough, hidden);
        }
        owner.RefreshCaptureVisibility();
    }

    private IDisposable SuppressForCapture()
    {
        var owner = mirrorOwner ?? this;
        owner.captureSuppressions++;
        owner.RefreshCaptureVisibility();
        return new CaptureSuppression(owner);
    }

    private void RefreshCaptureVisibility()
    {
        var owner = mirrorOwner ?? this;
        foreach (var window in owner.mirrors.Values.Prepend(owner))
        {
            if (window.IsClosing) continue;
            if (window.snapshot && window.mirrorOwner == null)
            {
                if (window.NativeMotionProbe) window.ResizeAndPosition(window.hiddenByUser);
            }
            else window.UpdateRuntimeVisibility(DateTime.Now);
        }
    }

    private sealed class CaptureSuppression(MainWindow owner) : IDisposable
    {
        private MainWindow? window = owner;
        public void Dispose()
        {
            if (window is not { } target) return;
            window = null;
            target.captureSuppressions--;
            target.RefreshCaptureVisibility();
        }
    }
}
