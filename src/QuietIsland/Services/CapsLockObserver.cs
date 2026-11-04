using System.ComponentModel;
using System.Runtime.InteropServices;

namespace QuietIsland.Services;

/// <summary>Observes only Caps Lock transitions. Never captures text, injects or suppresses input.</summary>
internal sealed class CapsLockObserver : IDisposable
{
    private readonly Callback callback;
    private IntPtr hook;
    private bool pressed;
    public bool Enabled { get; private set; }
    public CapsLockObserver()
    {
        Enabled = (Native.GetKeyState(0x14) & 1) != 0;
        pressed = (Native.GetKeyState(0x14) & 0x8000) != 0;
        callback = Observe;
        hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private IntPtr Observe(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && Marshal.ReadInt32(data) == 0x14)
        {
            var kind = message.ToInt32();
            if (kind is 0x100 or 0x104) { if (!pressed) Enabled = !Enabled; pressed = true; }
            else if (kind is 0x101 or 0x105) pressed = false;
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
