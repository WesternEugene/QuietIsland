using System.Runtime.InteropServices;

namespace QuietIsland.Services.Devices;

internal sealed record BluetoothAudioEndpoint(string Id, Guid ContainerId, bool Connected, string? AdapterId, bool CanConnect, bool CanDisconnect);

/// <summary>
/// Documented Core Audio / KS Bluetooth audio commands, when exposed by the installed driver.
/// BASICSUPPORT is read-only. GET on ONESHOT_RECONNECT/DISCONNECT is a command, not a read.
/// </summary>
internal static class BluetoothAudio
{
    private static readonly Guid TopologyId = new("2A07407E-6497-4A18-9787-32F79BD0D98F");
    private static readonly Guid KsControlId = new("28F54685-06FD-11D2-B27A-00A0C9223196");
    private static readonly Guid BtAudioSet = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    private static readonly PropertyKey ContainerKey = new(new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);
    private const uint PropertyGet = 1;
    private const uint PropertyBasicSupport = 0x200;

    // A null filter is useful for aggregate, read-only diagnostic probing. IDs are never logged.
    public static IReadOnlyList<BluetoothAudioEndpoint> Read(IReadOnlySet<Guid>? containers, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (containers?.Count == 0) return [];
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            Check(enumerator.EnumAudioEndpoints(2, 15, out collection));
            Check(collection.GetCount(out var count));
            var result = new List<BluetoothAudioEndpoint>();
            for (uint i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IMMDevice? endpoint = null;
                try
                {
                    Check(collection.Item(i, out endpoint));
                    var container = ReadContainer(endpoint);
                    if (containers != null && !containers.Contains(container)) continue;
                    Check(endpoint.GetId(out var id));
                    Check(endpoint.GetState(out var state));
                    string? adapterId = null;
                    var connect = false;
                    var disconnect = false;
                    object? ksObject = null;
                    try
                    {
                        ksObject = OpenKsControl(enumerator, endpoint, out adapterId);
                        var control = (IKsControl)ksObject;
                        connect = Supports(control, 0);
                        disconnect = Supports(control, 1);
                    }
                    catch (Exception e) when (e is COMException or InvalidCastException)
                    {
                        // Unsupported/unavailable control is represented by false capabilities.
                        // The endpoint state is still useful and remains an actual OS observation.
                    }
                    finally { Release(ksObject); }
                    result.Add(new BluetoothAudioEndpoint(id, container, (state & 1) != 0, adapterId, connect, disconnect));
                }
                catch (COMException)
                {
                    // Endpoints can disappear during enumeration. Do not invent their state.
                }
                finally { Release(endpoint); }
            }
            return result;
        }
        finally { Release(collection); Release(enumerator); }
    }

    public static void Request(Guid container, bool connect, CancellationToken cancellationToken)
    {
        var endpoints = Read(new HashSet<Guid> { container }, cancellationToken);
        var candidates = endpoints.Where(e => connect ? e.CanConnect : e.CanDisconnect)
            .Where(e => e.AdapterId != null).DistinctBy(e => e.AdapterId, StringComparer.OrdinalIgnoreCase).ToList();
        if (candidates.Count == 0)
            throw new NotSupportedException("Драйвер этих наушников не предоставляет Windows-команду " + (connect ? "подключения." : "отключения."));

        IMMDeviceEnumerator? enumerator = null;
        var accepted = 0;
        var lastError = 0;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            foreach (var endpoint in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IMMDevice? adapter = null;
                object? controlObject = null;
                try
                {
                    Check(enumerator.GetDevice(endpoint.AdapterId!, out adapter));
                    var iid = KsControlId;
                    Check(adapter.Activate(ref iid, 23, IntPtr.Zero, out controlObject));
                    var property = new KsPropertyRequest(BtAudioSet, connect ? 0u : 1u, PropertyGet);
                    cancellationToken.ThrowIfCancellationRequested();
                    var hr = ((IKsControl)controlObject).KsProperty(ref property, 24, IntPtr.Zero, 0, out _);
                    if (hr >= 0) accepted++; else lastError = hr;
                }
                catch (COMException e) { lastError = e.HResult; }
                finally { Release(controlObject); Release(adapter); }
            }
        }
        finally { Release(enumerator); }
        if (accepted == 0)
            throw new InvalidOperationException($"Драйвер отклонил команду Bluetooth-аудио (0x{lastError:X8}).");
        // Acceptance alone is never treated as a successful connection; the caller polls state.
    }

    private static Guid ReadContainer(IMMDevice endpoint)
    {
        IPropertyStore? store = null;
        try
        {
            Check(endpoint.OpenPropertyStore(0, out store));
            var key = ContainerKey;
            Check(store.GetValue(ref key, out var value));
            try
            {
                if (value.Type == 72 && value.Pointer != IntPtr.Zero) return Marshal.PtrToStructure<Guid>(value.Pointer);
                if (value.Type == 31 && Guid.TryParse(Marshal.PtrToStringUni(value.Pointer), out var id)) return id;
                return Guid.Empty;
            }
            finally { PropVariantClear(ref value); }
        }
        finally { Release(store); }
    }

    private static object OpenKsControl(IMMDeviceEnumerator enumerator, IMMDevice endpoint, out string adapterId)
    {
        object? topologyObject = null;
        IConnector? connector = null;
        IMMDevice? adapter = null;
        try
        {
            var iid = TopologyId;
            Check(endpoint.Activate(ref iid, 23, IntPtr.Zero, out topologyObject));
            var topology = (IDeviceTopology)topologyObject;
            Check(topology.GetConnectorCount(out var count));
            if (count == 0) throw new COMException("У аудиоустройства нет аппаратного коннектора.", unchecked((int)0x80004002));
            Check(topology.GetConnector(0, out connector));
            Check(connector.GetDeviceIdConnectedTo(out adapterId));
            Check(enumerator.GetDevice(adapterId, out adapter));
            iid = KsControlId;
            Check(adapter.Activate(ref iid, 23, IntPtr.Zero, out var control));
            return control;
        }
        finally { Release(adapter); Release(connector); Release(topologyObject); }
    }

    private static bool Supports(IKsControl control, uint id)
    {
        var property = new KsPropertyRequest(BtAudioSet, id, PropertyBasicSupport);
        var buffer = Marshal.AllocCoTaskMem(4);
        try
        {
            Marshal.WriteInt32(buffer, 0);
            var hr = control.KsProperty(ref property, 24, buffer, 4, out var returned);
            return hr >= 0 && returned >= 4 && (Marshal.ReadInt32(buffer) & PropertyGet) != 0;
        }
        finally { Marshal.FreeCoTaskMem(buffer); }
    }

    private static void Check(int hr) { if (hr < 0) throw new COMException("Аудиодрайвер отклонил запрос.", hr); }
    private static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

    [StructLayout(LayoutKind.Sequential)]
    private struct KsPropertyRequest(Guid set, uint id, uint flags)
    {
        public Guid Set = set;
        public uint Id = id;
        public uint Flags = flags;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey(Guid format, uint id)
    {
        public Guid Format = format;
        public uint Id = id;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint stateMask, out IMMDeviceCollection collection);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceTopology
    {
        [PreserveSig] int GetConnectorCount(out uint count);
        [PreserveSig] int GetConnector(uint index, out IConnector connector);
    }

    [ComImport, Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IConnector
    {
        [PreserveSig] int GetType(out int type);
        [PreserveSig] int GetDataFlow(out int flow);
        [PreserveSig] int ConnectTo(IConnector connector);
        [PreserveSig] int Disconnect();
        [PreserveSig] int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);
        [PreserveSig] int GetConnectedTo(out IConnector connector);
        [PreserveSig] int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IKsControl
    {
        [PreserveSig] int KsProperty(ref KsPropertyRequest property, uint propertyLength, IntPtr data, uint dataLength, out uint bytesReturned);
    }
}
