using System.ComponentModel;
using System.Runtime.InteropServices;

namespace QuietIsland.Services.Devices;

internal sealed record WlanPhy(Guid InterfaceId, uint Index, int SoftwareState, int HardwareState)
{
    public bool On => SoftwareState == 1 && HardwareState == 1;
}

internal sealed record WlanReading(int InterfaceCount, int ConnectedCount, IReadOnlyList<WlanPhy> Phys)
{
    public bool Available => InterfaceCount > 0 && Phys.Count > 0;
    public bool On => Phys.Any(p => p.On);
    public string Detail => !Available ? "Адаптер Wi-Fi не найден"
        : Phys.All(p => p.HardwareState == 2) ? "Wi-Fi отключён аппаратно"
        : On ? ConnectedCount > 0 ? "Wi-Fi включён · есть подключение" : "Wi-Fi включён · нет подключения"
        : Phys.Any(p => p.SoftwareState == 0 || p.HardwareState == 0) ? "Windows не сообщила состояние радио Wi-Fi"
        : "Wi-Fi выключен";
}

/// <summary>Desktop Native WLAN API. No network scan, SSID query, settings URI or shell commands.</summary>
internal static class WlanRadio
{
    private const uint RadioStateOpcode = 4;

    public static WlanReading Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Check(WlanOpenHandle(2, IntPtr.Zero, out _, out var handle), "Открытие WLAN");
        try { return Read(handle, cancellationToken); }
        finally { WlanCloseHandle(handle, IntPtr.Zero); }
    }

    public static void Set(bool enabled, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Check(WlanOpenHandle(2, IntPtr.Zero, out _, out var handle), "Открытие WLAN");
        try
        {
            var before = Read(handle, cancellationToken);
            if (!before.Available) throw new NotSupportedException("На компьютере не найден доступный адаптер Wi-Fi.");
            if (enabled && before.Phys.All(p => p.HardwareState == 2))
                throw new InvalidOperationException("Wi-Fi отключён аппаратным переключателем или режимом полёта. Программное включение недоступно.");
            foreach (var phy in before.Phys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (phy.SoftwareState == (enabled ? 1 : 2)) continue;
                var id = phy.InterfaceId;
                var state = new PhyRadioState { PhyIndex = phy.Index, SoftwareState = enabled ? 1 : 2 };
                Check(WlanSetInterface(handle, ref id, RadioStateOpcode, (uint)Marshal.SizeOf<PhyRadioState>(), ref state, IntPtr.Zero), "Переключение Wi-Fi");
            }
        }
        finally { WlanCloseHandle(handle, IntPtr.Zero); }
    }

    private static WlanReading Read(IntPtr handle, CancellationToken cancellationToken)
    {
        Check(WlanEnumInterfaces(handle, IntPtr.Zero, out var interfaces), "Чтение адаптеров Wi-Fi");
        try
        {
            var count = Marshal.ReadInt32(interfaces);
            if (count is < 0 or > 1024) throw new InvalidOperationException("WLAN вернула некорректный список адаптеров.");
            var phys = new List<WlanPhy>();
            var connected = 0;
            var itemSize = Marshal.SizeOf<InterfaceInfo>();
            for (var i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = Marshal.PtrToStructure<InterfaceInfo>(IntPtr.Add(interfaces, 8 + i * itemSize));
                if (info.State == 1) connected++;
                var id = info.Id;
                Check(WlanQueryInterface(handle, ref id, RadioStateOpcode, IntPtr.Zero, out var size, out var data, out _), "Чтение радио Wi-Fi");
                try
                {
                    if (size < 4) throw new InvalidOperationException("WLAN не вернула состояние радио.");
                    var phyCount = Marshal.ReadInt32(data);
                    if (phyCount is < 0 or > 64 || size < 4 + 12 * phyCount)
                        throw new InvalidOperationException("WLAN вернула некорректное состояние радио.");
                    for (var p = 0; p < phyCount; p++)
                    {
                        var native = Marshal.PtrToStructure<PhyRadioState>(IntPtr.Add(data, 4 + p * 12));
                        phys.Add(new WlanPhy(id, native.PhyIndex, native.SoftwareState, native.HardwareState));
                    }
                }
                finally { WlanFreeMemory(data); }
            }
            return new WlanReading(count, connected, phys);
        }
        finally { WlanFreeMemory(interfaces); }
    }

    private static void Check(uint result, string operation)
    {
        if (result == 0) return;
        var detail = result switch
        {
            5 => "Windows запретила доступ к управлению Wi-Fi",
            1062 => "Служба WLAN AutoConfig остановлена",
            50 => "Драйвер не поддерживает эту операцию",
            _ => new Win32Exception(unchecked((int)result)).Message
        };
        throw new InvalidOperationException($"{operation}: {detail} (код {result}).");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct InterfaceInfo
    {
        public Guid Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
        public int State;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PhyRadioState
    {
        public uint PhyIndex;
        public int SoftwareState;
        public int HardwareState;
    }

    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiatedVersion, out IntPtr handle);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr interfaces);
    [DllImport("wlanapi.dll")] private static extern uint WlanQueryInterface(IntPtr handle, ref Guid interfaceId, uint opcode, IntPtr reserved, out uint size, out IntPtr data, out uint valueType);
    [DllImport("wlanapi.dll")] private static extern uint WlanSetInterface(IntPtr handle, ref Guid interfaceId, uint opcode, uint size, ref PhyRadioState data, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr memory);
}
