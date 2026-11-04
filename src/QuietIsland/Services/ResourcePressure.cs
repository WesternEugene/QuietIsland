using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuietIsland.Services;

/// <summary>Cheap two-second sampling; no performance-counter subscriptions or background process scans.</summary>
internal sealed class ResourcePressure
{
    private DateTime sampled, busyUntil;
    private ulong idle, total;
    private uint foregroundId;
    private TimeSpan foregroundCpu;
    public bool IsBusy { get; private set; }
    public bool Sample(bool fullscreen, string forcedApps)
    {
        var now = DateTime.UtcNow;
        if ((now - sampled).TotalSeconds < 2) return IsBusy || fullscreen;
        var elapsed = Math.Max(.1, (now - sampled).TotalSeconds); sampled = now;
        var load = 0d;
        if (GetSystemTimes(out var nextIdle, out var kernel, out var user))
        {
            var nextTotal = kernel + user;
            if (total > 0 && nextTotal > total) load = 1 - (nextIdle - idle) / (double)(nextTotal - total);
            total = nextTotal; idle = nextIdle;
        }
        var foregroundBusy = false;
        try
        {
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out var pid);
            if (pid != 0 && pid != Environment.ProcessId)
            {
                using var process = Process.GetProcessById((int)pid); var cpu = process.TotalProcessorTime;
                var cores = pid == foregroundId ? Math.Max(0, (cpu - foregroundCpu).TotalSeconds / elapsed) : 0;
                foregroundId = pid; foregroundCpu = cpu;
                var forced = forcedApps.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Any(x => x.Replace(".exe", "", StringComparison.OrdinalIgnoreCase).Equals(process.ProcessName, StringComparison.OrdinalIgnoreCase));
                foregroundBusy = forced || cores >= .5 || process.WorkingSet64 >= 2L * 1024 * 1024 * 1024;
            }
        }
        catch { }
        if (fullscreen || foregroundBusy || load >= .7) busyUntil = now.AddSeconds(6);
        IsBusy = now < busyUntil; return IsBusy;
    }
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
}
