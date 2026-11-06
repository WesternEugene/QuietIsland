using System.Drawing;
using System.Runtime.InteropServices;

namespace QuietIsland.Services;

/// <summary>Matches physical coordinates to outputs on FFmpeg's default D3D11 adapter.</summary>
internal static class DesktopDuplication
{
    internal static (int Index, Rectangle Screen)? Find(Rectangle region)
    {
        IntPtr factory = IntPtr.Zero, adapter = IntPtr.Zero;
        try
        {
            var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
            if (CreateDXGIFactory1(ref iid, out factory) < 0 || Method<Enumerate>(factory, 7)(factory, 0, out adapter) < 0) return null;
            for (uint index = 0; index < 32; index++)
            {
                if (Method<Enumerate>(adapter, 7)(adapter, index, out var output) < 0) break;
                try
                {
                    if (Method<Describe>(output, 7)(output, out var desc) < 0 || !desc.Attached || desc.Rotation is not (0 or 1)) continue;
                    var screen = Rectangle.FromLTRB(desc.Bounds.Left, desc.Bounds.Top, desc.Bounds.Right, desc.Bounds.Bottom);
                    if (screen.Contains(region)) return ((int)index, screen);
                }
                finally { Marshal.Release(output); }
            }
        }
        catch (Exception e) when (e is COMException or DllNotFoundException) { }
        finally { if (adapter != IntPtr.Zero) Marshal.Release(adapter); if (factory != IntPtr.Zero) Marshal.Release(factory); }
        return null;
    }
    private static T Method<T>(IntPtr instance, int slot) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Enumerate(IntPtr instance, uint index, out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Describe(IntPtr instance, out OutputDescription result);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct OutputDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public Native.RECT Bounds;
        [MarshalAs(UnmanagedType.Bool)] public bool Attached;
        public int Rotation; public IntPtr Monitor;
    }
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
}
