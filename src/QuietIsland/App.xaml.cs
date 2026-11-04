using System.Windows;
using System.Security.Principal;
using QuietIsland.Services;

namespace QuietIsland;
public partial class App : System.Windows.Application
{
    private Mutex? mutex;
    private EventWaitHandle? show;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length >= 2 && e.Args[0] == "--media-fixture")
        {
            try { await MediaFixture.RunPlayerAsync(e.Args[1]); Shutdown(0); }
            catch (Exception error) { Log.Write("Media fixture", error); Shutdown(1); }
            return;
        }
        DispatcherUnhandledException += (_, args) => { Log.Write("Unhandled UI", args.Exception); args.Handled = true; MessageBox.Show("Quiet Island: " + args.Exception.GetType().Name + ". Подробности: %LOCALAPPDATA%\\QuietIsland\\errors.log", "Quiet Island"); };
        if (e.Args.Contains("--render") || e.Args.Contains("--diagnostics") || e.Args.Contains("--ui-checks") || e.Args.Contains("--motion-checks"))
        {
            try { await Diagnostics.RunAsync(e.Args); Shutdown(0); }
            catch (Exception error)
            {
                Log.Write("Diagnostics", error);
                var report = e.Args.FirstOrDefault(x => x.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
                if (report != null) File.WriteAllText(Path.ChangeExtension(report, ".error.json"), System.Text.Json.JsonSerializer.Serialize(new { type = error.GetType().Name, error.StackTrace, innerType = error.InnerException?.GetType().Name, innerStack = error.InnerException?.StackTrace }));
                Shutdown(1);
            }
            return;
        }
        var suffix = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        mutex = new Mutex(true, "Local\\QuietIsland." + suffix, out var created);
        show = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\QuietIsland.Show." + suffix);
        if (!created) { show.Set(); Shutdown(); return; }
        var context = new IslandContext(); var window = new MainWindow(context); MainWindow = window; window.Show();
        _ = Task.Run(() => { while (show.WaitOne()) Dispatcher.InvokeAsync(() => { if (!window.IsVisible) window.Show(); window.ShowPanel("search", true); }); });
    }
    protected override void OnExit(ExitEventArgs e) { show?.Dispose(); if (mutex != null) { try { mutex.ReleaseMutex(); } catch (ApplicationException) { } mutex.Dispose(); } base.OnExit(e); }
}
