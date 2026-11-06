using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuietIsland.Services;

internal static class Native
{
    public const int WM_HOTKEY = 0x0312, WM_CLIPBOARDUPDATE = 0x031D;
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint id);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
    [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint threadId);
    [DllImport("user32.dll")] public static extern short GetKeyState(int key);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out RECT value, int size);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern bool LockWorkStation();
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public INPUTUNION data; }
    [StructLayout(LayoutKind.Explicit)] private struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT keyboard; [FieldOffset(0)] public MOUSEINPUT mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int x, y; public uint data, flags, time; public IntPtr extra; }

    public static void ConfigureOverlay(Window window, bool acceptsInput, bool clickThrough, bool hideCapture)
    {
        var h = new WindowInteropHelper(window).Handle;
        var style = GetWindowLongPtrW(h, -20).ToInt64() | 0x80; // TOOLWINDOW
        style = acceptsInput ? style & ~0x08000000 : style | 0x08000000;
        style = clickThrough ? style | 0x20 : style & ~0x20;
        SetWindowLongPtrW(h, -20, new IntPtr(style));
        SetWindowDisplayAffinity(h, hideCapture ? 0x11u : 0u);
    }
    public static (double X, double Y) MonitorDpi(System.Windows.Forms.Screen screen)
    {
        var monitor = MonitorFromPoint(new POINT { X = screen.Bounds.Left + screen.Bounds.Width / 2, Y = screen.Bounds.Top + screen.Bounds.Height / 2 }, 2);
        return GetDpiForMonitor(monitor, 0, out var x, out var y) == 0 ? (x / 96d, y / 96d) : (1, 1);
    }
    public static void ClipOverlay(IntPtr window, bool hidden)
    {
        if (!hidden) { SetWindowRgn(window, IntPtr.Zero, true); return; }
        var region = CreateRectRgn(0, 0, 0, 0);
        if (SetWindowRgn(window, region, true) == 0) DeleteObject(region);
    }
    public static string ForegroundProcess()
    {
        try { GetWindowThreadProcessId(GetForegroundWindow(), out var pid); using var process = Process.GetProcessById((int)pid); return process.ProcessName; }
        catch { return ""; }
    }
    public static bool IsFullscreen(System.Windows.Forms.Screen screen)
    {
        var h = GetForegroundWindow();
        GetWindowThreadProcessId(h, out var pid);
        if (pid == Environment.ProcessId || !GetWindowRect(h, out var r)) return false;
        if (DwmGetWindowAttribute(h, 9, out var frame, Marshal.SizeOf<RECT>()) == 0) r = frame;
        var name = ForegroundProcess();
        if (name.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return false;
        var b = screen.Bounds;
        return r.Left <= b.Left + 2 && r.Top <= b.Top + 2 && r.Right >= b.Right - 2 && r.Bottom >= b.Bottom - 2;
    }
    public static void Paste(IntPtr target)
    {
        if (!IsWindow(target)) return;
        SetForegroundWindow(target);
        var inputs = new[]
        {
            new INPUT { type=1, data=new() { keyboard=new() { vk=0x11 } } },
            new INPUT { type=1, data=new() { keyboard=new() { vk=0x56 } } },
            new INPUT { type=1, data=new() { keyboard=new() { vk=0x56, flags=2 } } },
            new INPUT { type=1, data=new() { keyboard=new() { vk=0x11, flags=2 } } }
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }
}
