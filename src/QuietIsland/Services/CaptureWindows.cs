using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace QuietIsland.Services;

internal static class CaptureWindows
{
    private delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder value, int length);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Native.RECT value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

    // Snapshot before showing the selector: it must not become its own hit target.
    internal sealed record Target(Rectangle Bounds, Rectangle ActivationBounds);
    internal static IReadOnlyList<Rectangle> Snapshot() => SnapshotTargets().Select(x => x.Bounds).ToArray();
    internal static IReadOnlyList<Target> SnapshotTargets()
    {
        var result = new List<Target>();
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || IsIconic(window)) return true;
            Native.GetWindowThreadProcessId(window, out var process);
            if (process == Environment.ProcessId && (Native.GetWindowLongPtrW(window, -20).ToInt64() & 0x80 /* WS_EX_TOOLWINDOW */) != 0) return true;
            if (DwmGetWindowAttribute(window, 14 /* DWMWA_CLOAKED */, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            var name = new StringBuilder(256); GetClassName(window, name, name.Capacity);
            if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "#32768") return true;
            if (DwmGetWindowAttribute(window, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out Native.RECT frame, Marshal.SizeOf<Native.RECT>()) != 0
                && !Native.GetWindowRect(window, out frame)) return true;
            if (frame.Right - frame.Left >= 2 && frame.Bottom - frame.Top >= 2)
            {
                var bounds = Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, frame.Bottom);
                var monitor = System.Windows.Forms.Screen.FromHandle(window).Bounds;
                var full = IsZoomed(window) || bounds.Left <= monitor.Left && bounds.Top <= monitor.Top && bounds.Right >= monitor.Right && bounds.Bottom >= monitor.Bottom;
                var activation = bounds;
                if (full)
                {
                    var dpi = GetDpiForWindow(window); if (dpi == 0) dpi = 96;
                    var captionHeight = Math.Max((int)Math.Ceiling(32 * dpi / 96d), GetSystemMetricsForDpi(4, dpi) + GetSystemMetricsForDpi(33, dpi) + GetSystemMetricsForDpi(92, dpi));
                    activation = new Rectangle(bounds.Left, Math.Max(bounds.Top, monitor.Top), bounds.Width, Math.Min(bounds.Height, captionHeight));
                }
                result.Add(new(bounds, activation));
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
