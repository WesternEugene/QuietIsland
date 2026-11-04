using System.ComponentModel;
using System.Runtime.InteropServices;

namespace QuietIsland.Services;

/// <summary>Owns only plain/Ctrl+PrintScreen while the island is running. All other input passes through.</summary>
internal sealed class PrintScreenShortcut : IDisposable
{
    private readonly Callback callback;
    private IntPtr hook;
    private bool claimed;
    public event Action<bool>? Requested;
    public PrintScreenShortcut()
    {
        callback = Observe; hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private IntPtr Observe(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && Marshal.ReadInt32(data) == 0x2C)
        {
            var kind = message.ToInt32();
            if (kind is 0x100 or 0x104)
            {
                bool Down(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;
                if (!Down(0x12) && !Down(0x10) && !Down(0x5B) && !Down(0x5C))
                {
                    if (!claimed) { claimed = true; Requested?.Invoke(Down(0x11)); }
                    return new IntPtr(1);
                }
            }
            else if (kind is 0x101 or 0x105 && claimed) { claimed = false; return new IntPtr(1); }
        }
        return CallNextHookEx(hook, code, message, data);
    }
    public void Dispose() { if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
    private delegate IntPtr Callback(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, Callback callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? module);
}
