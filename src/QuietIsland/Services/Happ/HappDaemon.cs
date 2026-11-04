using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

namespace QuietIsland.Services.Happ;

/// <summary>
/// The read-only observer protocol also used by Happ's own happ-diag.exe (4.6.1).
/// All connection changes go through Happ.exe's URI handler, never through its privileged daemon.
/// </summary>
internal static class HappDaemon
{
    internal static async Task<bool> ReadTunnelAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(1400));
        using var process = Process.GetCurrentProcess();
        await using var pipe = new NamedPipeClientStream(".", $"happd-{process.SessionId}", PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(250, timeout.Token);
        using var hello = await RequestAsync(pipe, new Dictionary<string, object?>
        {
            ["action"] = "client-hello", ["observer"] = true,
            // Never advertise a newer client version: older daemons can restart on version
            // mismatch. Zero also keeps discovery read-only during a partially applied update.
            ["version"] = "0.0.0", ["process-id"] = Environment.ProcessId
        }, timeout.Token);
        if (!hello.RootElement.TryGetProperty("status", out var status) || status.GetString() != "success")
            throw new InvalidDataException("Наблюдение за службой Happ недоступно.");

        using var tunnel = await RequestAsync(pipe, new Dictionary<string, object?> { ["action"] = "tun-status" }, timeout.Token);
        if (!tunnel.RootElement.TryGetProperty("running", out var running) || running.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Служба Happ не сообщила состояние TUN.");
        return running.GetBoolean();
    }

    private static async Task<JsonDocument> RequestAsync(Stream stream, Dictionary<string, object?> message, CancellationToken token)
    {
        var id = "quiet-island-observer-" + Guid.NewGuid().ToString("N");
        message["request-id"] = id;
        await HappWire.WriteAsync(stream, message, token);
        for (var i = 0; i < 16; i++)
        {
            var reply = await HappWire.ReadAsync(stream, token);
            if (reply.RootElement.TryGetProperty("request-id", out var request) && request.ValueKind == JsonValueKind.String && request.GetString() == id) return reply;
            reply.Dispose();
        }
        throw new InvalidDataException("Не получен ответ наблюдателю Happ.");
    }
}
