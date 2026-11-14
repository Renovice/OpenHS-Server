using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace HsServer.Admin;

/// <summary>
/// The running server's admin port (127.0.0.1 only): one command per connection, sent by `HsServer admin` as a JSON
/// array of words on one line; the answer is "ok" or "error" on the first line, then the text.
/// </summary>
internal sealed class AdminServer(AdminCommands commands, int port)
{
    public const int DefaultPort = 1121;

    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        try { listener.Start(); }
        catch (SocketException e)
        {
            Log.Warn("admin", $"admin port {port} unavailable ({e.Message}): `HsServer admin` won't reach this server");
            return;
        }
        Log.Info("admin", $"admin commands on 127.0.0.1:{port} (HsServer admin / admin.bat)");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => ServeAsync(client, ct), ct);
            }
        }
        catch (OperationCanceledException) { }
        finally { listener.Stop(); }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, new UTF8Encoding(false), leaveOpen: true);
                string line = await reader.ReadLineAsync(ct);
                string[] args = line == null ? Array.Empty<string>() : JsonSerializer.Deserialize<string[]>(line) ?? Array.Empty<string>();
                AdminCommands.Result result = await commands.RunAsync(args);
                Log.Info("admin", $"{string.Join(' ', args)} -> {(result.Ok ? "ok" : "error")}: {result.Text.Split('\n')[0]}");
                byte[] reply = new UTF8Encoding(false).GetBytes((result.Ok ? "ok" : "error") + "\n" + result.Text);
                await stream.WriteAsync(reply, ct);
            }
            catch (Exception e) when (e is IOException or JsonException or OperationCanceledException)
            {
                Log.Warn("admin", "bad admin connection: " + e.Message);
            }
        }
    }
}
