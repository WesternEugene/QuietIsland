using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace QuietIsland.Services;

public sealed class ObsService(DesktopState state) : IDisposable
{
    private CancellationTokenSource? stop;
    public bool Connected { get; private set; }
    public bool Recording { get; private set; }
    public bool Streaming { get; private set; }
    public string Status { get; private set; } = "OBS не подключён";
    public void Start() { Dispose(); if (!state.Data.Settings.ObsEnabled) return; stop = new(); _ = RunAsync(stop.Token); }
    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            using var socket = new ClientWebSocket();
            try
            {
                using var connect = CancellationTokenSource.CreateLinkedTokenSource(token); connect.CancelAfter(5000);
                await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{state.Data.Settings.ObsPort}"), connect.Token);
                using var hello = JsonDocument.Parse(await Receive(socket, token));
                var auth = "";
                if (hello.RootElement.GetProperty("d").TryGetProperty("authentication", out var authentication))
                {
                    var password = DesktopState.Unprotect(state.Data.Settings.ObsPasswordEncrypted);
                    var secret = Hash(password + authentication.GetProperty("salt").GetString()); auth = Hash(secret + authentication.GetProperty("challenge").GetString());
                }
                await Send(socket, new { op = 1, d = new { rpcVersion = 1, authentication = auth, eventSubscriptions = 64 } }, token);
                using var identified = JsonDocument.Parse(await Receive(socket, token));
                if (identified.RootElement.GetProperty("op").GetInt32() != 2) throw new InvalidOperationException();
                Connected = true; Status = "OBS подключён";
                foreach (var type in new[] { "GetRecordStatus", "GetStreamStatus" }) await Send(socket, new { op = 6, d = new { requestType = type, requestId = type } }, token);
                while (!token.IsCancellationRequested)
                {
                    using var message = JsonDocument.Parse(await Receive(socket, token)); var root = message.RootElement; var op = root.GetProperty("op").GetInt32(); var d = root.GetProperty("d");
                    if (op == 7 && d.TryGetProperty("responseData", out var data) && data.TryGetProperty("outputActive", out var active))
                    { if (d.GetProperty("requestType").GetString() == "GetRecordStatus") Recording = active.GetBoolean(); else Streaming = active.GetBoolean(); }
                    if (op == 5)
                    {
                        var type = d.GetProperty("eventType").GetString(); var data2 = d.GetProperty("eventData");
                        if (type == "RecordStateChanged") Recording = data2.GetProperty("outputActive").GetBoolean();
                        if (type == "StreamStateChanged") Streaming = data2.GetProperty("outputActive").GetBoolean();
                    }
                }
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException or JsonException or InvalidOperationException) { Connected = Recording = Streaming = false; Status = "OBS: нет соединения / проверьте пароль"; }
            try { await Task.Delay(5000, token); } catch (OperationCanceledException) { break; }
        }
    }
    private static string Hash(string text) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static async Task Send(ClientWebSocket socket, object data, CancellationToken token) => await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(data), WebSocketMessageType.Text, true, token);
    private static async Task<string> Receive(ClientWebSocket socket, CancellationToken token)
    {
        using var stream = new MemoryStream(); var buffer = new byte[8192]; WebSocketReceiveResult result;
        do { result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token); if (result.MessageType == WebSocketMessageType.Close || stream.Length > 1_000_000) throw new WebSocketException(); stream.Write(buffer, 0, result.Count); } while (!result.EndOfMessage);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    public void Dispose() { stop?.Cancel(); stop?.Dispose(); stop = null; Connected = Recording = Streaming = false; }
}
