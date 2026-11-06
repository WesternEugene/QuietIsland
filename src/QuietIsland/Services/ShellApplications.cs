using System.Runtime.InteropServices;
using QuietIsland.Models;

namespace QuietIsland.Services;

internal sealed record ShellApplication(SearchResult Result, bool IsPackaged);

/// <summary>Registered desktop and packaged launch targets from the Windows AppsFolder.</summary>
internal static class ShellApplications
{
    internal const string Prefix = "shell:AppsFolder\\";
    private const string ParsingPrefix = "::{4234d49b-0245-4df3-b780-3893943456e1}\\";

    internal static bool IsTarget(string target) => target.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ||
        target.StartsWith(ParsingPrefix, StringComparison.OrdinalIgnoreCase) ||
        target.StartsWith("shell:" + ParsingPrefix, StringComparison.OrdinalIgnoreCase);

    internal static string ParsingName(string target) => target.StartsWith(ParsingPrefix, StringComparison.OrdinalIgnoreCase)
        ? "shell:" + target : target;

    internal static IReadOnlyList<ShellApplication> Discover()
    {
        IReadOnlyList<ShellApplication> results = [];
        // The application catalog is loaded on a pool thread at startup, whereas
        // shell namespace handlers require their own initialized STA apartment.
        var worker = new Thread(() =>
        {
            var initialized = CoInitializeEx(IntPtr.Zero, 2) >= 0;
            try { if (initialized) results = ReadApplications(); }
            catch (Exception error) { Log.Write("AppsFolder", error); }
            finally { if (initialized) CoUninitialize(); }
        }) { IsBackground = true, Name = "Quiet Island application catalog" };
        worker.SetApartmentState(ApartmentState.STA); worker.Start(); worker.Join();
        return results;
    }

    private static IReadOnlyList<ShellApplication> ReadApplications()
    {
        IShellItem? folder = null;
        IEnumShellItems? items = null;
        var results = new List<ShellApplication>();
        try
        {
            var itemId = typeof(IShellItem).GUID;
            // Use the shell moniker: SHCreateItemFromParsingName rejects a bare
            // AppsFolder CLSID on some Windows versions with E_INVALIDARG.
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName("shell:AppsFolder", IntPtr.Zero, ref itemId, out folder));
            var enumHandler = new Guid("94f60519-2850-4924-aa5a-d15e84868039");
            var enumId = typeof(IEnumShellItems).GUID;
            folder.BindToHandler(IntPtr.Zero, ref enumHandler, ref enumId, out items);
            var familyPropertyAvailable = PSGetPropertyKeyFromName("System.AppUserModel.PackageFamilyName", out var familyKey) >= 0;
            while (items.Next(1, out var item, out var fetched) == 0 && fetched == 1)
            {
                if (item == null) continue;
                try
                {
                    var name = DisplayName(item, 0); // SIGDN_NORMALDISPLAY is localized by Windows.
                    var identity = DisplayName(item, 0x80018001); // SIGDN_PARENTRELATIVEPARSING: AUMID or desktop app ID.
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(identity))
                    {
                        var packaged = IsPackaged(item, identity, familyPropertyAvailable, ref familyKey);
                        results.Add(new ShellApplication(new SearchResult(name, Prefix + identity, "Приложение", Detail: "Установленное приложение"), packaged));
                    }
                }
                catch (Exception error) when (error is COMException or ArgumentException) { Log.Write("AppsFolder.Item", error); }
                finally { Marshal.FinalReleaseComObject(item); }
            }
        }
        finally
        {
            if (items != null) Marshal.FinalReleaseComObject(items);
            if (folder != null) Marshal.FinalReleaseComObject(folder);
        }
        return results;
    }

    private static bool IsPackaged(IShellItem item, string identity, bool familyPropertyAvailable, ref PropertyKey familyKey)
    {
        if (familyPropertyAvailable)
        {
            IntPtr family = IntPtr.Zero;
            try
            {
                if (((IShellItem2)item).GetString(ref familyKey, out family) >= 0)
                    return !string.IsNullOrWhiteSpace(Marshal.PtrToStringUni(family));
            }
            catch (InvalidCastException) { }
            finally { if (family != IntPtr.Zero) Marshal.FreeCoTaskMem(family); }
        }
        // Fallback for providers without package metadata. Normal packaged AUMIDs
        // consist of a package family (name_publisherId) and relative app ID.
        var separator = identity.IndexOf('!');
        return separator > 0 && separator < identity.Length - 1 && identity[..separator].Contains('_');
    }

    private static string DisplayName(IShellItem item, uint kind)
    {
        item.GetDisplayName(kind, out var text);
        try { return Marshal.PtrToStringUni(text) ?? ""; }
        finally { Marshal.FreeCoTaskMem(text); }
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IEnumShellItems items);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint kind, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid FormatId; public uint Id; }

    [ComImport, Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem2
    {
        void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr result);
        void GetParent(out IntPtr parent);
        void GetDisplayName(uint kind, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
        void GetPropertyStore(uint flags, ref Guid iid, out IntPtr store);
        void GetPropertyStoreWithCreateObject(uint flags, IntPtr creator, ref Guid iid, out IntPtr store);
        void GetPropertyStoreForKeys(IntPtr keys, uint count, uint flags, ref Guid iid, out IntPtr store);
        void GetPropertyDescriptionList(ref PropertyKey key, ref Guid iid, out IntPtr descriptions);
        void Update(IntPtr context);
        void GetProperty(ref PropertyKey key, IntPtr value);
        void GetCLSID(ref PropertyKey key, out Guid value);
        void GetFileTime(ref PropertyKey key, out long value);
        void GetInt32(ref PropertyKey key, out int value);
        [PreserveSig] int GetString(ref PropertyKey key, out IntPtr value);
    }

    [ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumShellItems
    {
        [PreserveSig] int Next(uint count, [MarshalAs(UnmanagedType.Interface)] out IShellItem? item, out uint fetched);
        void Skip(uint count);
        void Reset();
        void Clone(out IEnumShellItems copy);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid, out IShellItem item);
    [DllImport("propsys.dll", CharSet = CharSet.Unicode)] private static extern int PSGetPropertyKeyFromName(string name, out PropertyKey key);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
