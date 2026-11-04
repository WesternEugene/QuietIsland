using System.ComponentModel;
using System.Runtime.InteropServices;

namespace QuietIsland.Services;

/// <summary>Observes global presses without capturing or suppressing any input.</summary>
internal sealed class OutsidePointer : IDisposable
{
    private readonly Callback callback;
    private IntPtr hook;
    public event Action<System.Drawing.Point>? Pressed;
    public OutsidePointer()
    {
        callback = Observe;
        hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private IntPtr Observe(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && message.ToInt32() is 0x201 or 0x204 or 0x207 or 0x20B)
        {
            var info = Marshal.PtrToStructure<MouseInfo>(data);
            Pressed?.Invoke(new(info.X, info.Y));
        }
        return CallNextHookEx(hook, code, message, data);
    }
    internal static bool IsOwnWindowAt(System.Drawing.Point point)
    {
        var window = WindowFromPoint(new Native.POINT { X = point.X, Y = point.Y });
        Native.GetWindowThreadProcessId(window, out var pid);
        return pid == Environment.ProcessId;
    }
    public void Dispose() { if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
    private delegate IntPtr Callback(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct MouseInfo { public int X, Y; public uint MouseData, Flags, Time; public IntPtr Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, Callback callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Native.POINT point);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? module);
}
