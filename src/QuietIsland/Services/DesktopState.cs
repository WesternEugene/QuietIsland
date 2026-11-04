using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;
using QuietIsland.Core;
using QuietIsland.Models;

namespace QuietIsland.Services;

public sealed class DesktopState
{
    public static string DataDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuietIsland");
    public AppState Data { get; }
    public string DirectoryPath { get; }
    public event Action? Changed;
    private readonly StateStore<AppState> store;
    private readonly DispatcherTimer saveTimer;
    public DesktopState(string? directory = null)
    {
        DirectoryPath = directory ?? DataDirectory;
        store = new(DirectoryPath);
        Data = store.Load();
        Data.Settings ??= new(); Data.Clipboard ??= []; Data.Shelf ??= []; Data.Notes ??= []; Data.Reminders ??= []; Data.Chat ??= []; Data.Launcher ??= []; Data.Colors ??= [];
        Data.Settings.NormalizeAppearance();
        if (Data.TimerEnd is { } timerEnd && Data.TimerDuration <= 0)
            Data.TimerDuration = Math.Max(1, Data.TimerPaused ? Data.TimerRemaining : (timerEnd - DateTimeOffset.Now).TotalSeconds);
        Data.Settings.ClipboardLimit = Math.Clamp(Data.Settings.ClipboardLimit, 20, 1000);
        Data.Settings.BridgePort = Math.Clamp(Data.Settings.BridgePort, 1024, 65535);
        if (Data.Settings.AppearanceRevision < 2)
        {
            if (Data.Settings.TopOffset == 8) Data.Settings.TopOffset = 0;
            Data.Settings.AppearanceRevision = 2;
        }
        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); Save(); };
    }
    public void Touch() { saveTimer.Stop(); saveTimer.Start(); Changed?.Invoke(); }
    public void Save() { try { store.Save(Data); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log.Write("State save", e); } }
    public static string Protect(string text) => string.IsNullOrEmpty(text) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(text), null, DataProtectionScope.CurrentUser));
    public static string Unprotect(string text)
    {
        try { return string.IsNullOrEmpty(text) ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(text), null, DataProtectionScope.CurrentUser)); }
        catch (Exception e) when (e is CryptographicException or FormatException) { return ""; }
    }
}
public static class Log
{
    private static readonly object Gate = new();
    public static void Write(string area, Exception e)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(DesktopState.DataDirectory);
                var path = Path.Combine(DesktopState.DataDirectory, "errors.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Move(path, path + ".old", true);
                // Do not log clipboard, prompts, URLs or secret-bearing exception messages.
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {area}: {e.GetType().Name} (0x{e.HResult:X8})\n");
            }
            catch (IOException) { }
        }
    }
}
