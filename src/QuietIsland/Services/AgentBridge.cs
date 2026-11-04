using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using QuietIsland.Core;

namespace QuietIsland.Services;

public record AgentEvent(string Source, string Title, string Message, DateTimeOffset Time);
public sealed class AgentBridge(DesktopState state) : IDisposable
{
    private HttpListener? listener;
    private CancellationTokenSource? stop;
    private readonly SemaphoreSlim slots = new(8);
    private readonly List<AgentEvent> events = [];
    private readonly object eventGate = new();
    public AgentBroker Broker { get; } = new();
    public string Status { get; private set; } = "Мост выключен";
    public string TokenFile => Path.Combine(state.DirectoryPath, "bridge-token");
    public IReadOnlyList<AgentEvent> Events { get { lock (eventGate) return events.ToArray(); } }
    public event Action? Changed;
    public void Start()
    {
        Dispose();
        if (!state.Data.Settings.BridgeEnabled) return;
        try
        {
            Directory.CreateDirectory(state.DirectoryPath);
            if (!File.Exists(TokenFile)) File.WriteAllText(TokenFile, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            var security = new FileSecurity(); security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(TokenFile).SetAccessControl(security);
            listener = new HttpListener(); listener.Prefixes.Add($"http://localhost:{state.Data.Settings.BridgePort}/"); listener.Start();
            stop = new(); _ = AcceptAsync(listener, stop.Token);
            Status = $"Агенты · localhost:{state.Data.Settings.BridgePort}";
        }
        catch (Exception e) { Status = "Мост не запущен: порт занят или доступ запрещён."; Log.Write("Agent bridge", e); }
        Changed?.Invoke();
    }
    private async Task AcceptAsync(HttpListener server, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var context = await server.GetContextAsync().WaitAsync(token);
                if (!await slots.WaitAsync(0, token)) { context.Response.StatusCode = 429; context.Response.Close(); continue; }
                _ = HandleAsync(context, token);
            }
            catch (Exception e) when (e is HttpListenerException or OperationCanceledException or ObjectDisposedException) { break; }
        }
    }
    private async Task HandleAsync(HttpListenerContext context, CancellationToken token)
    {
        try
        {
            var request = context.Request;
            var expected = File.ReadAllText(TokenFile).Trim(); var supplied = request.Headers["X-Island-Token"] ?? "";
            var a = Encoding.UTF8.GetBytes(expected); var b = Encoding.UTF8.GetBytes(supplied);
            if (request.RemoteEndPoint == null || !IPAddress.IsLoopback(request.RemoteEndPoint.Address) || a.Length != b.Length || !CryptographicOperations.FixedTimeEquals(a, b)) { await Reply(context, 401, new { error = "unauthorized" }); return; }
            if (!string.IsNullOrEmpty(request.Headers["Origin"])) { await Reply(context, 403, new { error = "browser requests forbidden" }); return; }
            if (request.Url?.AbsolutePath == "/health" && request.HttpMethod == "GET") { await Reply(context, 200, new { ok = true, pending = Broker.Pending.Count }); return; }
            if (request.HttpMethod != "POST" || request.ContentLength64 is < 1 or > 65536) { await Reply(context, 400, new { error = "POST with 1..65536 bytes required" }); return; }
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(3));
            var buffer = new byte[(int)request.ContentLength64]; await request.InputStream.ReadExactlyAsync(buffer, deadline.Token);
            var data = JsonSerializer.Deserialize<BridgeMessage>(buffer, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (data == null) throw new JsonException();
            var source = Limit(data.Source ?? "Агент", 120); var title = Limit(data.Title ?? "Событие", 200); var message = Limit(data.Message ?? "", 20000);
            if (request.Url?.AbsolutePath == "/events")
            {
                lock (eventGate) { events.Insert(0, new(source, title, message, DateTimeOffset.Now)); if (events.Count > 50) events.RemoveAt(events.Count - 1); }
                Changed?.Invoke(); await Reply(context, 200, new { ok = true });
            }
            else if (request.Url?.AbsolutePath == "/request")
            {
                var options = data.Options ?? ["allow", "deny"];
                if (options.Length == 0 || options.Length > 12 || options.Any(x => x.Length is 0 or > 200)) throw new JsonException();
                var id = Guid.NewGuid().ToString("N");
                var answer = await Broker.AskAsync(new(id, source, title, message, options, data.AllowText), TimeSpan.FromSeconds(Math.Clamp(data.TimeoutSeconds, 5, 170)), deadline.Token);
                await Reply(context, 200, new { decision = answer, id });
            }
            else await Reply(context, 404, new { error = "not found" });
        }
        catch (Exception e) when (e is JsonException or ArgumentException) { try { await Reply(context, 400, new { error = "invalid request" }); } catch { } }
        catch (Exception e) { Log.Write("Bridge request", e); try { await Reply(context, 503, new { decision = "deny", error = "request cancelled" }); } catch { } }
        finally { slots.Release(); }
    }
    private static string Limit(string text, int length) => text.Length <= length ? text : text[..length];
    private static async Task Reply(HttpListenerContext context, int status, object value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value); context.Response.StatusCode = status; context.Response.ContentType = "application/json; charset=utf-8"; context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes); context.Response.Close();
    }
    public void Dispose() { stop?.Cancel(); listener?.Close(); stop?.Dispose(); stop = null; listener = null; Status = "Мост выключен"; }
    private record BridgeMessage(string? Source, string? Title, string? Message, string[]? Options, int TimeoutSeconds = 120, bool AllowText = false);
}
