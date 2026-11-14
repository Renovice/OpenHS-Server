using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace HsServer.Net;

/// <summary>
/// Minimal RFC 6455 server handshake over a raw TCP stream, then hands the stream to the framework WebSocket.
/// Avoids HttpListener (URL ACLs) and ASP.NET (dependency weight) for a loopback-only server.
/// </summary>
internal static class WebSocketAcceptor
{
    private const string Magic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    public static async Task<WebSocket> AcceptAsync(NetworkStream stream, string requiredSubProtocol, CancellationToken ct)
    {
        string request = await ReadHeadersAsync(stream, ct);
        var headers = request.Split("\r\n").Skip(1)
            .Select(l => l.Split(':', 2)).Where(p => p.Length == 2)
            .GroupBy(p => p[0].Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First()[1].Trim(), StringComparer.OrdinalIgnoreCase);

        if (!headers.TryGetValue("Sec-WebSocket-Key", out string key) ||
            !headers.TryGetValue("Upgrade", out string upgrade) || !upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase))
        {
            await WriteAsync(stream, "HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n", ct);
            throw new InvalidDataException("not a WebSocket upgrade");
        }
        headers.TryGetValue("Sec-WebSocket-Protocol", out string offered);
        bool protocolOk = offered?.Split(',').Any(p => p.Trim() == requiredSubProtocol) == true;
        if (!protocolOk)
        {
            await WriteAsync(stream, "HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n", ct);
            throw new InvalidDataException($"subprotocol {requiredSubProtocol} not offered (got '{offered}')");
        }

        string accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + Magic)));
        await WriteAsync(stream,
            "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {accept}\r\nSec-WebSocket-Protocol: {requiredSubProtocol}\r\n\r\n", ct);

        return WebSocket.CreateFromStream(stream, new WebSocketCreationOptions
        {
            IsServer = true,
            SubProtocol = requiredSubProtocol,
            KeepAliveInterval = TimeSpan.FromSeconds(30),
        });
    }

    // Byte-wise read up to the blank line so no WebSocket frame bytes are consumed.
    private static async Task<string> ReadHeadersAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new List<byte>(1024);
        var one = new byte[1];
        while (buffer.Count < 16 * 1024)
        {
            if (await stream.ReadAsync(one, ct) == 0) throw new EndOfStreamException("connection closed during handshake");
            buffer.Add(one[0]);
            int n = buffer.Count;
            if (n >= 4 && buffer[n - 4] == '\r' && buffer[n - 3] == '\n' && buffer[n - 2] == '\r' && buffer[n - 1] == '\n')
                return Encoding.ASCII.GetString(buffer.ToArray());
        }
        throw new InvalidDataException("handshake headers too large");
    }

    private static Task WriteAsync(NetworkStream stream, string text, CancellationToken ct) =>
        stream.WriteAsync(Encoding.ASCII.GetBytes(text), ct).AsTask();
}
