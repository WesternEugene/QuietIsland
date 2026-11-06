using System.ComponentModel;
using System.Runtime.InteropServices;

namespace QuietIsland.Services;

/// <summary>Scoped to the selector lifetime; works when a child popup owns keyboard focus.</summary>
internal sealed class CaptureEscape : IDisposable
{
    private readonly Callback callback;
    private IntPtr hook;
    internal CaptureEscape(Action cancel)
    {
        callback = (code, message, data) =>
        {
            if (code >= 0 && Marshal.ReadInt32(data) == 0x1B && message.ToInt32() is 0x100 or 0x104) cancel();
            return CallNextHookEx(hook, code, message, data);
        };
        hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public void Dispose() { if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
    private delegate IntPtr Callback(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, Callback callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
