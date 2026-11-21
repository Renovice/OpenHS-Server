using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HsServer.Lobby;

namespace HsServer.Admin;

/// <summary>
/// `HsServer admin &lt;command&gt;` (Server/admin.bat): sends the command to the running server's admin port; when no
/// server runs, does it on the state files itself. A server that runs without an admin port (an older build) keeps its
/// own copy of the state, so then nothing is changed.
/// </summary>
internal static class AdminTool
{
    public static int Run(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        int port = AdminServer.DefaultPort;
        if (Array.IndexOf(args, "--port") is >= 0 and int i && i + 1 < args.Length)
        {
            port = int.Parse(args[i + 1]);
            args = args.Where((_, k) => k != i && k != i + 1).ToArray();
        }

        if (Connect(port) is { } client)
            using (client)
            {
                var stream = client.GetStream();
                stream.Write(new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(args) + "\n"));
                client.Client.Shutdown(SocketShutdown.Send);
                string reply = new StreamReader(stream, new UTF8Encoding(false)).ReadToEnd();
                int nl = reply.IndexOf('\n');
                bool ok = nl >= 0 ? reply[..nl] == "ok" : reply == "ok";
                Console.WriteLine(nl >= 0 ? reply[(nl + 1)..] : "");
                return ok ? 0 : 1;
            }

        if (args.Length == 0 || args[0] is "help" or "-h" or "--help" or "?")
        {
            Console.WriteLine(AdminCommands.FullHelp);
            return 0;
        }
        if (Connect(1119) is { } lobby)
        {
            lobby.Dispose();
            Console.Error.WriteLine("error: a server is running without the admin port (an older build?). Stop it (stop-local.bat), " +
                                    "or rebuild and restart it, then try again.");
            return 1;
        }
        Console.Error.WriteLine("(server not running: changing the state files directly)");
        ServerData data = ServerData.Load(Program.ProjectRoot());
        if (data == null)
        {
            Console.Error.WriteLine("error: no recorded login session found");
            return 1;
        }
        AdminCommands.Result result = new AdminCommands(data).RunAsync(args).GetAwaiter().GetResult();
        Console.WriteLine(result.Text);
        return result.Ok ? 0 : 1;
    }

    private static TcpClient Connect(int port)
    {
        var client = new TcpClient();
        try
        {
            if (client.ConnectAsync("127.0.0.1", port).Wait(TimeSpan.FromMilliseconds(500)) && client.Connected) return client;
        }
        catch (AggregateException) { }
        client.Dispose();
        return null;
    }
}
