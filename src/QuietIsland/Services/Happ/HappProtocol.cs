using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace QuietIsland.Services.Happ;

internal static class HappWire
{
    internal const int MaxMessageLength = 1_048_576;
    internal static async Task WriteAsync(Stream stream, object message, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        if (body.Length is < 2 or > MaxMessageLength) throw new InvalidDataException("Недопустимый размер сообщения Happ.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, body.Length);
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(body, token);
        await stream.FlushAsync(token);
    }

    internal static async Task<JsonDocument> ReadAsync(Stream stream, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is < 2 or > MaxMessageLength) throw new InvalidDataException("Недопустимый размер ответа Happ.");
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, token);
        var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 32 });
        if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
        document.Dispose();
        throw new InvalidDataException("Неизвестный формат ответа Happ.");
    }
}

internal static class HappEvidence
{
    internal static IReadOnlySet<int> LoopbackProxyPorts(string? proxy)
    {
        var ports = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(proxy)) return ports;
        foreach (var item in proxy.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var endpoint = item;
            var equals = item.IndexOf('=');
            if (equals >= 0)
            {
                if (item[..equals].ToLowerInvariant() is not ("http" or "https" or "ftp" or "socks")) continue;
                endpoint = item[(equals + 1)..].Trim();
            }
            if (!endpoint.Contains("://", StringComparison.Ordinal)) endpoint = "http://" + endpoint;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Port is < 1 or > 65535
                || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0) continue;
            var host = uri.Host.Trim('[', ']');
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address))
                ports.Add(uri.Port);
        }
        return ports;
    }

    // These are the three explicit names recognized by the installed Happ diagnostic tool.
    // A generic Wintun adapter can belong to any other VPN and is never sufficient evidence.
    internal static bool IsTunnelName(string name) => name.Equals("happ-tun", StringComparison.OrdinalIgnoreCase)
        || name.Equals("happ-xray", StringComparison.OrdinalIgnoreCase)
        || name.Equals("happ-default-tun", StringComparison.OrdinalIgnoreCase);

    internal static ProcessStartInfo StartInfo(string executable, bool enabled)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? ""
        };
        info.ArgumentList.Add(enabled ? "happ://connect" : "happ://disconnect");
        return info;
    }
}
