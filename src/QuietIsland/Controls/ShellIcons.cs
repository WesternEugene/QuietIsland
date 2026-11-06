using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuietIsland.Services;

namespace QuietIsland.Controls;

/// <summary>Windows file and application icons without shortcut-arrow overlays.</summary>
public static class ShellIcons
{
    private static readonly ConcurrentDictionary<string, Task<BitmapSource?>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly BlockingCollection<Action> Queue = new(512);
    static ShellIcons()
    {
        var worker = new Thread(() => { var initialized = CoInitializeEx(IntPtr.Zero, 2) >= 0; try { foreach (var work in Queue.GetConsumingEnumerable()) work(); } finally { if (initialized) CoUninitialize(); } }) { IsBackground = true, Name = "Quiet Island shell icons" };
        worker.SetApartmentState(ApartmentState.STA); worker.Start();
    }
    public static FrameworkElement File(string path, double size, FrameworkElement? fallback = null)
    {
        var host = new ContentControl
        {
            Width = size, Height = size, Content = fallback ?? Icons.Colored("file", size), IsHitTestVisible = false,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center
        };
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
            IntPtr icon = IntPtr.Zero, itemId = IntPtr.Zero;
            IImageList? images = null;
            try
            {
                var target = IconTarget(path);
                if (ShellApplications.IsTarget(target) && ApplicationImage(target) is { } appIcon)
                { completion.TrySetResult(appIcon); return; }
                uint flags = 0x4000; // SHGFI_SYSICONINDEX gives the base icon, without the .lnk overlay.
                IntPtr imageList;
                FileInfo info;
                if (ShellApplications.IsTarget(target))
                {
                    Marshal.ThrowExceptionForHR(SHParseDisplayName(ShellApplications.ParsingName(target), IntPtr.Zero, out itemId, 0, out _));
                    imageList = SHGetFileInfoId(itemId, 0, out info, (uint)Marshal.SizeOf<FileInfo>(), flags | 0x8); // SHGFI_PIDL
                }
                else
                {
                    var remote = target.StartsWith(@"\\", StringComparison.Ordinal) || Uri.TryCreate(target, UriKind.Absolute, out var uri) && !uri.IsFile;
                    if (remote) flags |= 0x10; // Resolve only local associations for remote paths/URLs.
                    imageList = SHGetFileInfo(target, 0x80, out info, (uint)Marshal.SizeOf<FileInfo>(), flags);
                }
                if (imageList == IntPtr.Zero) { completion.TrySetResult(null); return; }
                var imageListId = typeof(IImageList).GUID;
                if (SHGetImageList(2, ref imageListId, out images) >= 0) // SHIL_EXTRALARGE (48px)
                    images.GetIcon(info.IconIndex, 1, out icon); // ILD_TRANSPARENT, no INDEXTOOVERLAYMASK.
                if (icon == IntPtr.Zero) icon = ImageList_GetIcon(imageList, info.IconIndex, 1);
                if (icon == IntPtr.Zero) { completion.TrySetResult(null); return; }
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); bitmap.Freeze(); completion.TrySetResult(bitmap);
            }
            catch { completion.TrySetResult(null); }
            finally
            {
                if (icon != IntPtr.Zero) DestroyIcon(icon);
                if (itemId != IntPtr.Zero) Marshal.FreeCoTaskMem(itemId);
                if (images != null) Marshal.FinalReleaseComObject(images);
            }
        })) completion.TrySetResult(null);
        return completion.Task;
    }
    private static string IconTarget(string path)
    {
        var target = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        var executable = Path.GetFileName(target).ToLowerInvariant();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            if (executable == "notepad.exe") return ShellApplications.Prefix + "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App";
            if (executable == "calc.exe") return ShellApplications.Prefix + "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";
        }
        if (ShellApplications.IsTarget(target)) return target;
        if (target.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
            return ShellApplications.Prefix + "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel";
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            if (uri.IsFile) return uri.LocalPath;
            // Ask Windows for the registered protocol handler, without contacting
            // the URL. This gives pinned websites their real default-browser icon.
            var length = 32768u; var application = new StringBuilder((int)length);
            if (AssocQueryString(0x1000, 2, uri.Scheme, null, application, ref length) == 0 && application.Length > 0)
                return application.ToString(); // ASSOCF_IS_PROTOCOL, ASSOCSTR_EXECUTABLE
            return target;
        }
        if (!target.Contains('\\') && !target.Contains('/'))
        {
            var resolved = new StringBuilder(32768);
            var length = SearchPath(null, target, null, (uint)resolved.Capacity, resolved, IntPtr.Zero);
            if (length > 0 && length < resolved.Capacity) return resolved.ToString();
        }
        return target;
    }

    private static BitmapSource? ApplicationImage(string target)
    {
        IImageFactory? factory = null; IntPtr bitmap = IntPtr.Zero;
        try
        {
            var iid = typeof(IImageFactory).GUID;
            if (SHCreateItemFromParsingName(ShellApplications.ParsingName(target), IntPtr.Zero, ref iid, out factory) < 0) return null;
            if (factory.GetImage(new ShellSize { Width = 64, Height = 64 }, 0x1 | 0x4, out bitmap) < 0 || bitmap == IntPtr.Zero) return null;
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); source.Freeze(); return source;
        }
        catch (COMException) { return null; }
        finally { if (bitmap != IntPtr.Zero) DeleteObject(bitmap); if (factory != null) Marshal.FinalReleaseComObject(factory); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct ShellSize { public int Width, Height; }
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageFactory { [PreserveSig] int GetImage(ShellSize size, uint flags, out IntPtr bitmap); }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid, out IImageFactory factory);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);

    [ComImport, Guid("46eb5926-582e-4017-9fdf-e8998daa0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig] int Add(IntPtr bitmap, IntPtr mask, out int index);
        [PreserveSig] int ReplaceIcon(int index, IntPtr icon, out int replaced);
        [PreserveSig] int SetOverlayImage(int image, int overlay);
        [PreserveSig] int Replace(int index, IntPtr bitmap, IntPtr mask);
        [PreserveSig] int AddMasked(IntPtr bitmap, uint mask, out int index);
        [PreserveSig] int Draw(IntPtr drawParameters);
        [PreserveSig] int Remove(int index);
        [PreserveSig] int GetIcon(int index, uint flags, out IntPtr icon);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct FileInfo
    {
        public IntPtr Icon; public int IconIndex; public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attributes, out FileInfo info, uint size, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfoW")] private static extern IntPtr SHGetFileInfoId(IntPtr itemId, uint attributes, out FileInfo info, uint size, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, IntPtr context, out IntPtr itemId, uint attributes, out uint resultAttributes);
    [DllImport("shell32.dll")] private static extern int SHGetImageList(int size, ref Guid iid, out IImageList images);
    [DllImport("comctl32.dll")] private static extern IntPtr ImageList_GetIcon(IntPtr list, int index, uint flags);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int AssocQueryString(uint flags, uint kind, string association, string? extra, StringBuilder output, ref uint length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint SearchPath(string? path, string file, string? extension, uint length, StringBuilder output, IntPtr filePart);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
