using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QuietIsland.Controls;

/// <summary>Windows file/application icons, deliberately used only by SearchView and ShelfView.</summary>
public static class ShellIcons
{
    private static readonly ConcurrentDictionary<string, Task<BitmapSource?>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly BlockingCollection<Action> Queue = new(512);
    static ShellIcons()
    {
        var worker = new Thread(() => { var initialized = CoInitializeEx(IntPtr.Zero, 2) >= 0; try { foreach (var work in Queue.GetConsumingEnumerable()) work(); } finally { if (initialized) CoUninitialize(); } }) { IsBackground = true, Name = "Quiet Island shell icons" };
        worker.SetApartmentState(ApartmentState.STA); worker.Start();
    }
    public static FrameworkElement File(string path, double size)
    {
        var host = new ContentControl { Width = size, Height = size, Content = Icons.Colored("file", size), IsHitTestVisible = false };
        if (string.IsNullOrWhiteSpace(path)) return host;
        if (Cache.Count > 384) Cache.Clear();
        var task = Cache.GetOrAdd(path, Load);
        var weak = new WeakReference<ContentControl>(host);
        _ = PresentAsync(task, weak, size); return host;
    }
    private static async Task PresentAsync(Task<BitmapSource?> task, WeakReference<ContentControl> target, double size)
    {
        var bitmap = await task;
        if (bitmap == null || !target.TryGetTarget(out var host) || host.Dispatcher.HasShutdownStarted) return;
        await host.Dispatcher.InvokeAsync(() => host.Content = new Image { Source = bitmap, Width = size, Height = size, Stretch = Stretch.Uniform });
    }
    private static Task<BitmapSource?> Load(string path)
    {
        var completion = new TaskCompletionSource<BitmapSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!Queue.TryAdd(() =>
        {
            IntPtr icon = IntPtr.Zero;
            try
            {
                var remote = path.StartsWith(@"\\", StringComparison.Ordinal) || Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile;
                uint flags = 0x100; // SHGFI_ICON, large icon; resolve local .lnk application icons as Explorer does.
                if (remote) flags |= 0x10; // Avoid touching remote providers/network locations.
                var result = SHGetFileInfo(path, 0x80, out var info, (uint)Marshal.SizeOf<FileInfo>(), flags);
                icon = info.Icon;
                if (result == IntPtr.Zero || icon == IntPtr.Zero) { completion.TrySetResult(null); return; }
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); bitmap.Freeze(); completion.TrySetResult(bitmap);
            }
            catch { completion.TrySetResult(null); }
            finally { if (icon != IntPtr.Zero) DestroyIcon(icon); }
        })) completion.TrySetResult(null);
        return completion.Task;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct FileInfo
    {
        public IntPtr Icon; public int IconIndex; public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attributes, out FileInfo info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
