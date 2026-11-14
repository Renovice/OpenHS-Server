using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using Blizzard.GameService.Protocol;
using Blizzard.GameService.Protocol.Authentication.V2.Client;
using Blizzard.GameService.Protocol.Connection.V1;
using Blizzard.GameService.Protocol.GameUtilities.V2.Client;
using Google.Protobuf;
using HsServer.Lobby;
using HsServer.Protocol;
using Xunit.Abstractions;

namespace HsServer.Tests;

/// <summary>
/// Integration test without the game: an in-process lobby server is driven by a WebSocket client that replays the
/// util requests of a recorded real login. Every request must be answered with the response type the live server
/// returned (or the server-state equivalent for save data).
/// </summary>
public class LobbyReplayTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Recorded_login_requests_get_the_recorded_answers()
    {
        string reference = RecordedAccount.FindReferenceSession(
            Path.Combine(TestEnvironment.ProjectRoot, "Hearthstone main game", "BepInEx", "HsRecorder"),
            Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures"));
        Assert.NotNull(reference);
        RecordedAccount account = RecordedAccount.Load(reference);
        var saves = new SaveDataStore();
        saves.Seed(account.SaveDataResponses);
        var context = new LobbyContext { Account = account, SaveData = saves, Matchmaker = new NoMatchmaker() };

        int port = FreePort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Task server = new LobbyServer(context, port).RunAsync(cts.Token);

        using var ws = new ClientWebSocket();
        ws.Options.AddSubProtocol(BgsFrame.SubProtocol);
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cts.Token);
        var client = new TestClient(ws);

        var connect = await client.CallAsync(BgsRoutes_Connection, 1, new ConnectRequest { UseBindlessRpc = true }, cts.Token);
        Assert.Equal(0u, connect.Header.Status);

        var logon = new LogonRequest { LogonOptions = new LogonOptions { AuthToken = ByteString.CopyFromUtf8(LobbySession.LocalAuthToken) } };
        var logonReply = await client.CallAsync(BgsRoutes_Authentication, 1, logon, cts.Token);
        Assert.Equal(0u, logonReply.Header.Status);

        int requests = 0, matched = 0;
        var mismatches = new List<string>();
        foreach (var (type, list) in account.Util.OrderBy(kv => kv.Value.Min(e => e.ResponseSeq)))
        {
            foreach (UtilExchange ex in list)
            {
                var rpc = new PegasusUtil.RpcMessage { RpcHeader = new PegasusUtil.RpcHeader { Type = (ulong)type }, MessageBody = ex.RequestBody };
                var task = new ProcessTaskRequest();
                task.Attribute.Add(new Blizzard.GameService.Protocol.V2.Attribute
                {
                    Name = "p", Value = new Blizzard.GameService.Protocol.V2.Variant { BlobValue = ByteString.CopyFrom(PegasusPackets.Serialize(rpc), 0, PegasusPackets.Serialize(rpc).Length) },
                });
                task.Attribute.Add(new Blizzard.GameService.Protocol.V2.Attribute
                {
                    Name = "system", Value = new Blizzard.GameService.Protocol.V2.Variant { StringValue = ex.System ?? "c" },
                });
                BgsFrame reply = await client.CallAsync(BgsRoutes_GameUtilities, 1, task, cts.Token);
                var response = reply.Decode<ProcessTaskResponse>();
                long got = response.Result.FirstOrDefault(a => a.Name == "id")?.Value?.IntValue ?? -1;
                requests++;
                if (ex.Notifications.Count > 0)
                    output.WriteLine($"  util {type} (#{ex.ResponseSeq}) carries {ex.Notifications.Count} notifications; received so far {client.NotificationCount}");
                if (got == ex.ResponseType) matched++;
                else mismatches.Add($"util {type}: expected {ex.ResponseType}, got {got}");
            }
        }
        await client.DrainAsync(TimeSpan.FromMilliseconds(500));
        int notifications = client.NotificationCount;
        int recordedNotifications = account.Util.Values.SelectMany(l => l).Sum(e => e.Notifications.Count);
        output.WriteLine($"util requests: {requests}, matched: {matched}, notifications pushed: {notifications} (recorded {recordedNotifications})");
        foreach (string m in mismatches) output.WriteLine(m);

        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
        Assert.Empty(mismatches);
        Assert.True(requests > 20);
        Assert.True(notifications >= recordedNotifications, "every recorded notification should be replayed at least once");
    }

    [Fact]
    public async Task A_named_login_token_logs_in_as_that_player()
    {
        // B1: "LOCAL:<name>" logs in as player <name> (created on first login); an unknown token is refused.
        string reference = RecordedAccount.FindReferenceSession(
            Path.Combine(TestEnvironment.ProjectRoot, "Hearthstone main game", "BepInEx", "HsRecorder"),
            Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures"));
        RecordedAccount account = RecordedAccount.Load(reference);
        string dir = Path.Combine(Path.GetTempPath(), "hsserver-lobby-accounts-" + Guid.NewGuid().ToString("N"));
        var accounts = new AccountStore(dir, account, null, null);
        var context = new LobbyContext { Account = account, SaveData = new SaveDataStore(), Matchmaker = new NoMatchmaker(), Accounts = accounts };
        int port = FreePort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task server = new LobbyServer(context, port).RunAsync(cts.Token);
        try
        {
            async Task<uint> LogonStatus(string token)
            {
                using var ws = new ClientWebSocket();
                ws.Options.AddSubProtocol(BgsFrame.SubProtocol);
                await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cts.Token);
                var client = new TestClient(ws);
                await client.CallAsync(BgsRoutes_Connection, 1, new ConnectRequest { UseBindlessRpc = true }, cts.Token);
                var reply = await client.CallAsync(BgsRoutes_Authentication, 1,
                    new LogonRequest { LogonOptions = new LogonOptions { AuthToken = ByteString.CopyFromUtf8(token) } }, cts.Token);
                return reply.Header.Status;
            }
            Assert.Equal(0u, await LogonStatus("LOCAL:Bob"));
            Assert.True(File.Exists(Path.Combine(dir, "bob", "account.json")));
            Assert.Equal(3u, await LogonStatus("not-a-local-token"));
        }
        finally
        {
            cts.Cancel();
            try { await server; } catch (OperationCanceledException) { }
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task The_end_screens_account_data_comes_after_every_game()
    {
        // After every game the end screen sends GenericRequestList (util 327: GetAccountInfo MEDAL_INFO + HERO_XP) and waits
        // for both. Live answers GenericResponse, then pushes MedalInfo (232) and HeroXP (283) as util notifications. We
        // only replayed the generic answer: a 5 s wait, then a NetCache fatal timeout after 2 min (2026-10-01).
        string reference = RecordedAccount.FindReferenceSession(
            Path.Combine(TestEnvironment.ProjectRoot, "Hearthstone main game", "BepInEx", "HsRecorder"),
            Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures"));
        RecordedAccount account = RecordedAccount.Load(reference);
        var saves = new SaveDataStore();
        saves.Seed(account.SaveDataResponses);
        var context = new LobbyContext { Account = account, SaveData = saves, Matchmaker = new NoMatchmaker() };
        int port = FreePort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Task server = new LobbyServer(context, port).RunAsync(cts.Token);
        using var ws = new ClientWebSocket();
        ws.Options.AddSubProtocol(BgsFrame.SubProtocol);
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), cts.Token);
        var client = new TestClient(ws);
        await client.CallAsync(BgsRoutes_Connection, 1, new ConnectRequest { UseBindlessRpc = true }, cts.Token);
        await client.CallAsync(BgsRoutes_Authentication, 1, new LogonRequest { LogonOptions = new LogonOptions { AuthToken = ByteString.CopyFromUtf8(LobbySession.LocalAuthToken) } }, cts.Token);

        var batch = new PegasusUtil.GenericRequestList();
        batch.Requests.Add(new PegasusUtil.GenericRequest { RequestId = 201, RequestSubId = (int)PegasusUtil.GetAccountInfo.Request.MEDAL_INFO });
        batch.Requests.Add(new PegasusUtil.GenericRequest { RequestId = 201, RequestSubId = (int)PegasusUtil.GetAccountInfo.Request.HERO_XP });
        for (int game = 0; game < 2; game++)
        {
            int before = client.UtilNotifications.Count;
            var rpc = new PegasusUtil.RpcMessage { RpcHeader = new PegasusUtil.RpcHeader { Type = 327 }, MessageBody = PegasusPackets.Serialize(batch) };
            byte[] blob = PegasusPackets.Serialize(rpc);
            var task = new ProcessTaskRequest();
            task.Attribute.Add(new Blizzard.GameService.Protocol.V2.Attribute { Name = "p", Value = new Blizzard.GameService.Protocol.V2.Variant { BlobValue = ByteString.CopyFrom(blob, 0, blob.Length) } });
            task.Attribute.Add(new Blizzard.GameService.Protocol.V2.Attribute { Name = "system", Value = new Blizzard.GameService.Protocol.V2.Variant { StringValue = "c" } });
            await client.CallAsync(BgsRoutes_GameUtilities, 1, task, cts.Token);
            // (receiving with a short cancel would abort the socket: wait until both arrive; the 60 s test token bounds it)
            await client.ReceiveUntilAsync(() => client.UtilNotifications.Skip(before).Contains(232L) && client.UtilNotifications.Skip(before).Contains(283L), cts.Token);
            var got = client.UtilNotifications.Skip(before).ToList();
            output.WriteLine($"game {game + 1}: util notifications {string.Join(",", got)}");
            Assert.Contains(232L, got); // MedalInfo
            Assert.Contains(283L, got); // HeroXP
        }
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
    }

    // Local copies keep BgsRoutes internal-only usage explicit in this file.
    private const uint BgsRoutes_Connection = 1698982289;
    private const uint BgsRoutes_Authentication = 3224338966;
    private const uint BgsRoutes_GameUtilities = 1572557250;

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>Minimal BGS RPC client: sends requests with tokens, returns the matching response, counts notifications.</summary>
    private sealed class TestClient(ClientWebSocket ws)
    {
        private uint token;
        public int NotificationCount { get; private set; }
        /// <summary>The Pegasus message type of each util notification received (message_type).</summary>
        public List<long> UtilNotifications { get; } = new();

        private void Count(BgsFrame frame)
        {
            if (frame.Header.ServiceId == BgsFrame.ResponseServiceId || frame.Header.ServiceHash != 593673933) return;
            NotificationCount++;
            try
            {
                var received = frame.Decode<Blizzard.GameService.Protocol.Notification.V2.Client.NotificationReceivedNotification>();
                foreach (var n in received.Notifications)
                    if (n.Type == "WTCG.UtilNotificationMessage" && n.Attribute.FirstOrDefault(a => a.Name == "message_type") is { } t)
                        UtilNotifications.Add(t.Value.IntValue);
            }
            catch (InvalidProtocolBufferException) { /* recorded raw payloads of other shapes */ }
        }

        public async Task<BgsFrame> CallAsync(uint serviceHash, uint method, IMessage body, CancellationToken ct)
        {
            uint t = ++token;
            var header = new Header { ServiceId = 0, ServiceHash = serviceHash, MethodId = method, Token = t };
            await ws.SendAsync(BgsFrame.Encode(header, body), WebSocketMessageType.Binary, true, ct);
            while (true)
            {
                BgsFrame frame = await ReceiveAsync(ct);
                if (frame.Header.ServiceId == BgsFrame.ResponseServiceId && frame.Header.Token == t) return frame;
                Count(frame);
            }
        }

        public async Task ReceiveUntilAsync(Func<bool> done, CancellationToken ct)
        {
            while (!done()) Count(await ReceiveAsync(ct));
        }

        /// <summary>Notifications follow their response asynchronously; collect whatever arrives within the window.</summary>
        public async Task DrainAsync(TimeSpan quiet)
        {
            while (true)
            {
                using var cts = new CancellationTokenSource(quiet);
                try
                {
                    BgsFrame frame = await ReceiveAsync(cts.Token);
                    Count(frame);
                }
                catch (OperationCanceledException) { return; }
            }
        }

        private async Task<BgsFrame> ReceiveAsync(CancellationToken ct)
        {
            var buffer = new byte[64 * 1024];
            using var ms = new MemoryStream();
            while (true)
            {
                var r = await ws.ReceiveAsync(buffer, ct);
                ms.Write(buffer, 0, r.Count);
                if (r.EndOfMessage) return BgsFrame.Parse(ms.ToArray());
            }
        }
    }
}
