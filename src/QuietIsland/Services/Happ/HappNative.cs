using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace QuietIsland.Services.Happ;

internal static class HappNative
{
    internal static string? ExecutablePath(Process process)
    {
        using var handle = OpenProcess(0x1000, false, process.Id); // PROCESS_QUERY_LIMITED_INFORMATION
        if (handle.IsInvalid) return null;
        var path = new StringBuilder(32768);
        var size = path.Capacity;
        return QueryFullProcessImageName(handle, 0, path, ref size) ? path.ToString() : null;
    }

    internal static HashSet<int> ListeningPorts(IReadOnlySet<int> processIds)
    {
        var result = new HashSet<int>();
        ReadTcpTable(2, 24, 4, 8, 20); // IPv4: local address, local port, owner PID
        ReadTcpTable(23, 56, 0, 20, 52); // IPv6
        return result;

        void ReadTcpTable(uint family, int rowSize, int addressOffset, int portOffset, int pidOffset)
        {
            uint length = 0;
            var error = GetExtendedTcpTable(IntPtr.Zero, ref length, false, family, 3, 0); // OWNER_PID_LISTENER
            if (error == 50 && family == 23) return; // IPv6 is disabled on some systems.
            if (error is not (0 or 122)) throw new Win32Exception((int)error);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (length is < 4 or > 16_777_216) throw new InvalidDataException("Недопустимый размер таблицы TCP.");
                var buffer = Marshal.AllocHGlobal((int)length);
                try
                {
                    error = GetExtendedTcpTable(buffer, ref length, false, family, 3, 0);
                    if (error == 122) continue;
                    if (error != 0) throw new Win32Exception((int)error);
                    var count = Marshal.ReadInt32(buffer);
                    if (count < 0 || 4L + count * (long)rowSize > length) throw new InvalidDataException("Неполная таблица TCP.");
                    for (var i = 0; i < count; i++)
                    {
                        var row = IntPtr.Add(buffer, 4 + i * rowSize);
                        if (!processIds.Contains(Marshal.ReadInt32(row, pidOffset))) continue;
                        var bytes = new byte[family == 2 ? 4 : 16];
                        Marshal.Copy(IntPtr.Add(row, addressOffset), bytes, 0, bytes.Length);
                        var address = new IPAddress(bytes);
                        if (!IPAddress.IsLoopback(address) && !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any)) continue;
                        var port = Marshal.ReadInt32(row, portOffset);
                        result.Add(((port & 0xff) << 8) | ((port >> 8) & 0xff));
                    }
                    return;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            throw new InvalidOperationException("Таблица TCP изменилась во время чтения.");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref int size);
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref uint size, [MarshalAs(UnmanagedType.Bool)] bool order, uint family, uint tableClass, uint reserved);
}
