using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using HsServer.Game;
using HsServer.Protocol;
using Xunit.Abstractions;

namespace HsServer.Tests;

/// <summary>
/// Game server integration test: a TCP client performs the recorded client side of a live match against the replay
/// server and must receive exactly the recorded server packets (original bytes, same order).
/// </summary>
public class GameReplayTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Recorded_match_is_served_packet_for_packet()
    {
        var matches = RecordedMatch.LoadAll(TestEnvironment.Sessions);
        Assert.NotEmpty(matches);
        RecordedMatch match = matches.OrderBy(m => m.Events.Count).First(m => m.Events.Count > 50);
        output.WriteLine("match: " + match);

        var allocations = new GameAllocations();
        GameAllocation game = allocations.Allocate(match.Scenario, match);
        int port = FreePort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Task server = new GameServer(allocations, new GameOptions { MaxPauseSeconds = 0 }, port).RunAsync(cts.Token);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        var stream = tcp.GetStream();

        // Our handshake carries the allocation; everything else is the recorded client traffic.
        var handshake = PegasusPackets.Deserialize<PegasusGame.Handshake>(match.Events.First(e => !e.FromServer).Body);
        handshake.GameHandle = game.GameHandle;
        handshake.Password = game.Password;
        await Write(stream, PegasusPackets.IdOf<PegasusGame.Handshake>(), PegasusPackets.Serialize(handshake));

        var expected = match.Events.Where(e => e.FromServer && e.Type != PegasusPackets.IdOf<PegasusGame.Pong>()).ToList();
        int received = 0, mismatches = 0, cursor = 0;
        foreach (MatchEvent e in match.Events.Skip(1))
        {
            if (!e.FromServer)
            {
                if (e.Type == PegasusPackets.IdOf<PegasusGame.Ping>()) continue;
                await Write(stream, e.Type, e.Body); // the recorded player action
                continue;
            }
            if (e.Type == PegasusPackets.IdOf<PegasusGame.Pong>()) continue;
            var (type, body) = await Read(stream, cts.Token);
            received++;
            MatchEvent want = expected[cursor++];
            if (type != want.Type || !body.AsSpan().SequenceEqual(want.Body)) mismatches++;
        }
        output.WriteLine($"server packets expected {expected.Count}, received {received}, mismatches {mismatches}");
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
        Assert.Equal(expected.Count, received);
        Assert.Equal(0, mismatches);
    }

    private static async Task Write(NetworkStream s, int type, byte[] body)
    {
        var frame = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, type);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), body.Length);
        body.CopyTo(frame, 8);
        await s.WriteAsync(frame);
    }

    private static async Task<(int, byte[])> Read(NetworkStream s, CancellationToken ct)
    {
        var head = new byte[8];
        await s.ReadExactlyAsync(head, ct);
        var body = new byte[BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(4))];
        await s.ReadExactlyAsync(body, ct);
        return (BinaryPrimitives.ReadInt32LittleEndian(head), body);
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
