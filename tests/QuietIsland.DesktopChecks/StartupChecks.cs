using System.Runtime.InteropServices;
using Microsoft.Win32;
using QuietIsland.Services;

internal static class StartupChecks
{
    public static Task RunAsync(Func<string, Func<Task>, Task> check) => check("startup_toggle_preserves_other_entries_and_quotes_path", () =>
    {
        // Redirect HKCU for this process only, before any background services start.
        // The real Windows startup list is never changed by this check.
        var fixturePath = @"Software\QuietIsland\StartupChecks\" + Guid.NewGuid().ToString("N");
        using var fixture = Registry.CurrentUser.CreateSubKey(fixturePath);
        var currentUser = new IntPtr(unchecked((int)0x80000001));
        var result = RegOverridePredefKey(currentUser, fixture.Handle.DangerousGetHandle());
        Require(result == 0, "Could not isolate the registry: " + result);
        try
        {
            const string run = @"Software\Microsoft\Windows\CurrentVersion\Run";
            Require(!SystemService.AutoStartEnabled(), "An empty startup list is reported as enabled.");
            SystemService.AutoStart(false);
            using var entries = Registry.CurrentUser.CreateSubKey(run);
            entries.SetValue("OtherApplication", "keep.exe");
            SystemService.AutoStart(true);
            Require(SystemService.AutoStartEnabled(), "Enabling startup was not reflected in the registry.");
            Require(entries.GetValue("QuietIsland") as string == "\"" + Environment.ProcessPath + "\"", "Startup command does not quote the executable path.");
            Require(entries.GetValueKind("QuietIsland") == RegistryValueKind.String, "Startup command is not REG_SZ.");
            SystemService.AutoStart(false); SystemService.AutoStart(false);
            Require(!SystemService.AutoStartEnabled() && entries.GetValue("QuietIsland") == null, "Disabling startup did not remove its entry.");
            Require(entries.GetValue("OtherApplication") as string == "keep.exe", "Disabling startup removed another application.");
            entries.SetValue("QuietIsland", "   ");
            Require(!SystemService.AutoStartEnabled(), "An empty command is reported as enabled.");
        }
        finally
        {
            var restored = RegOverridePredefKey(currentUser, IntPtr.Zero);
            Require(restored == 0, "Could not restore registry mapping: " + restored);
            Registry.CurrentUser.DeleteSubKeyTree(fixturePath, throwOnMissingSubKey: false);
        }
        return Task.CompletedTask;
    });

    private static void Require(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); }
    [DllImport("advapi32.dll")] private static extern int RegOverridePredefKey(IntPtr key, IntPtr replacement);
}
