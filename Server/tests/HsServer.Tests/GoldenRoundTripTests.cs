using Blizzard.GameService.Protocol;
using Google.Protobuf;
using HsServer.Protocol;
using Xunit.Abstractions;

namespace HsServer.Tests;

/// <summary>
/// Golden tests against packets recorded from the live servers.
/// Pegasus note: the client's generated Deserialize assigns defaults through the properties, which sets the Has*
/// flags, so a decode→encode round trip adds explicit default fields (same values, larger bytes). Therefore:
/// replay must forward original bytes, and freshly built messages (only needed fields set) must match Blizzard's
/// minimal encoding — both are asserted here.
/// </summary>
public class GoldenRoundTripTests(ITestOutputHelper output)
{
    [Fact]
    public void Recordings_are_available()
    {
        Assert.NotEmpty(TestEnvironment.Sessions);
    }

    [Fact]
    public void Fresh_pegasus_messages_encode_like_blizzard()
    {
        // Recorded client bytes for RpcHeader { Type = 314 } inside RpcMessage: 0A 03 08 BA 02 ...
        var header = new PegasusUtil.RpcHeader { Type = 314 };
        Assert.Equal("08BA02", Convert.ToHexString(PegasusPackets.Serialize(header)));
    }

    [Fact]
    public void Pegasus_game_and_util_packets_decode_and_encode_stably()
    {
        int checkedCount = 0;
        var failures = new List<string>();
        var unknownTypes = new SortedSet<string>();
        foreach (string session in TestEnvironment.Sessions)
        {
            foreach (RecordedPacket p in Recording.Read(session))
            {
                if (p.Body == null || (p.Channel != "game" && p.Channel != "util")) continue;
                byte[] body = p.Body;
                int type = p.Type;
                if (p.Channel == "util" && !p.IsIn)
                {
                    // Outbound util = RpcMessage wrapper; the inner packet type sits in its header.
                    var rpc = PegasusPackets.Deserialize<PegasusUtil.RpcMessage>(p.Body);
                    Check(p, typeof(PegasusUtil.RpcMessage), p.Body, failures);
                    type = (int)rpc.RpcHeader.Type;
                    body = rpc.MessageBody ?? Array.Empty<byte>();
                }
                Type messageType = PegasusPackets.TypeFor(type, p.Channel == "game" ? "PegasusGame" : "PegasusUtil");
                if (messageType == null) { unknownTypes.Add($"{p.Channel}-{p.Direction}:{type}"); continue; }
                Check(p, messageType, body, failures);
                checkedCount++;
            }
        }
        output.WriteLine($"pegasus packets checked: {checkedCount}, failures: {failures.Count}, unknown types: {string.Join(" ", unknownTypes)}");
        foreach (string f in failures.Take(30)) output.WriteLine(f);
        Assert.True(checkedCount > 0, "no Pegasus packets found in recordings");
        Assert.Empty(failures);
    }

    [Fact]
    public void Bgs_rpc_messages_round_trip_byte_identical()
    {
        var bgsAssembly = typeof(Header).Assembly;
        int checkedCount = 0;
        var failures = new List<string>();
        foreach (string session in TestEnvironment.Sessions)
        {
            foreach (RecordedPacket p in Recording.Read(session))
            {
                if (p.Channel != "bgs" || p.Header == null) continue;
                Header header = Bgs.Parse<Header>(p.Header);
                if (!Bgs.Serialize(header).AsSpan().SequenceEqual(p.Header))
                    failures.Add($"{Path.GetFileName(session)}#{p.Seq} header re-encode differs");
                if (p.Redacted || string.IsNullOrEmpty(p.Message) || p.Body == null) continue;
                Type messageType = bgsAssembly.GetType(p.Message);
                if (messageType == null) { failures.Add($"#{p.Seq} unknown BGS type {p.Message}"); continue; }
                IMessage message = Bgs.Parse(messageType, p.Body);
                if (!Bgs.Serialize(message).AsSpan().SequenceEqual(p.Body))
                    failures.Add($"{Path.GetFileName(session)}#{p.Seq} {p.Message} re-encode differs ({p.Body.Length} bytes)");
                checkedCount++;
            }
        }
        output.WriteLine($"bgs messages checked: {checkedCount}, failures: {failures.Count}");
        foreach (string f in failures.Take(30)) output.WriteLine(f);
        Assert.True(checkedCount > 0, "no BGS messages found in recordings");
        Assert.Empty(failures);
    }

    [Fact]
    public void Bgs_frame_encode_then_parse()
    {
        var header = new Header { ServiceId = 0, ServiceHash = 1698982289, MethodId = 1, Token = 37 };
        var body = new Blizzard.GameService.Protocol.Connection.V1.ConnectResponse { UseBindlessRpc = true, Ciid = "t" };
        byte[] frame = BgsFrame.Encode(header, body);
        BgsFrame parsed = BgsFrame.Parse(frame);
        Assert.Equal(37u, parsed.Header.Token);
        Assert.True(parsed.Decode<Blizzard.GameService.Protocol.Connection.V1.ConnectResponse>().UseBindlessRpc);
    }

    // Decode must succeed and consume the whole body; after one normalization pass the encoding must be a fixed
    // point (decode(encode(x)) re-encodes to the same bytes), i.e. no information is lost or invented.
    private static void Check(RecordedPacket p, Type messageType, byte[] body, List<string> failures)
    {
        try
        {
            byte[] normalized = PegasusPackets.Serialize(PegasusPackets.Deserialize(messageType, body));
            byte[] again = PegasusPackets.Serialize(PegasusPackets.Deserialize(messageType, normalized));
            if (!again.AsSpan().SequenceEqual(normalized))
                failures.Add($"#{p.Seq} {p.Channel}-{p.Direction} {messageType.Name}: encoding not stable ({normalized.Length} -> {again.Length} bytes)");
            if (normalized.Length < body.Length)
                failures.Add($"#{p.Seq} {p.Channel}-{p.Direction} {messageType.Name}: data lost ({body.Length} -> {normalized.Length} bytes)");
        }
        catch (Exception e)
        {
            failures.Add($"#{p.Seq} {p.Channel}-{p.Direction} {messageType.Name}: {e.GetType().Name} {e.Message}");
        }
    }
}
