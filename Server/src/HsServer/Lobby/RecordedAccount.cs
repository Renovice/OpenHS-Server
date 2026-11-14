using Blizzard.GameService.Protocol;
using Blizzard.GameService.Protocol.GameUtilities.V2.Client;
using HsServer.Protocol;

namespace HsServer.Lobby;

/// <summary>A recorded util request and the server's answer (original Pegasus bytes) plus notifications that followed.</summary>
internal sealed class UtilExchange
{
    public int RequestType;
    public byte[] RequestBody;
    public string System;
    public int ResponseType;
    public byte[] ResponseBody;
    /// <summary>The whole recorded ProcessTaskResponse (BGS message) — replayed verbatim.</summary>
    public byte[] RawBgsResponse;
    public long ResponseSeq;
    /// <summary>Raw NotificationReceivedNotification bodies that arrived after this response (replayed once).</summary>
    public readonly List<byte[]> Notifications = new();
}

/// <summary>A recorded non-util BGS request/response pair (friends, presence, session...).</summary>
internal sealed class BgsExchange
{
    public uint ServiceHash;
    public uint MethodId;
    public byte[] RequestBody;
    public string ResponseType;
    public byte[] ResponseBody;
    public uint Status;
}

/// <summary>
/// Account state reconstructed from one HsRecorder session of a real login. Serves as the replay knowledge base
/// for the lobby: stateless answers are replayed as the original bytes (see GoldenRoundTripTests for why).
/// Personal data (GetAccountInfoResponse) is never loaded; only ids/region needed for protocol consistency.
/// </summary>
internal sealed class RecordedAccount
{
    public ulong AccountId = 1;
    public ulong GameAccountId = 1;
    public uint Region = 2;
    public string BattleTag = "LocalPlayer#0001";
    public string SourceSession;

    public readonly Dictionary<int, List<UtilExchange>> Util = new();
    public readonly List<BgsExchange> Bgs = new();
    public readonly List<byte[]> SaveDataResponses = new(); // raw 358 bodies in order, for SaveDataStore seeding
    /// <summary>The newest body of every util packet type the server sent (also unsolicited ones, e.g. MedalInfo after a
    /// game), for answering requests the recording never made (GetAccountInfo 201).</summary>
    public readonly Dictionary<int, byte[]> LastUtil = new();

    public static RecordedAccount Load(string sessionFile)
    {
        var account = new RecordedAccount { SourceSession = sessionFile };
        var pending = new Dictionary<uint, (Header Header, byte[] Body, string Message)>();
        UtilExchange lastUtil = null;

        foreach (RecordedPacket p in Recording.Read(sessionFile))
        {
            if (p.Channel == "util" && p.IsIn && p.Type == 358 && p.Body != null)
                account.SaveDataResponses.Add(p.Body);
            if (p.Channel == "util" && p.IsIn && p.Body != null)
                account.LastUtil[p.Type] = p.Body;
            if (p.Channel != "bgs" || p.Header == null) continue;
            Header header = Protocol.Bgs.Parse<Header>(p.Header);

            if (!p.IsIn && p.Kind == "request")
            {
                pending[header.Token] = (header, p.Body, p.Message);
                continue;
            }
            if (p.IsIn && p.Kind == "response")
            {
                if (!pending.Remove(header.Token, out var req)) continue;
                if (req.Message?.EndsWith("GameUtilities.V2.Client.ProcessTaskRequest") == true && p.Body != null && req.Body != null)
                {
                    UtilExchange ex = ToUtilExchange(req.Body, p.Body, p.Seq);
                    if (ex == null) continue;
                    if (!account.Util.TryGetValue(ex.RequestType, out var list)) account.Util[ex.RequestType] = list = new();
                    list.Add(ex);
                    lastUtil = ex;
                }
                else if (!p.Redacted)
                {
                    account.Bgs.Add(new BgsExchange
                    {
                        ServiceHash = header.ServiceHash, MethodId = header.MethodId, RequestBody = req.Body,
                        ResponseType = p.Message, ResponseBody = p.Body ?? Array.Empty<byte>(),
                    });
                    account.CaptureIdentity(p.Message, p.Body);
                }
                continue;
            }
            if (p.IsIn && p.Kind == "request" && !p.Redacted && p.Body != null)
            {
                // Server-initiated. Util notifications are replayed after the exchange they followed.
                if (header.ServiceHash == BgsRoutes.NotificationListener && lastUtil != null)
                    lastUtil.Notifications.Add(p.Body);
                account.CaptureIdentity(p.Message, p.Body);
            }
        }
        return account;
    }

    private static UtilExchange ToUtilExchange(byte[] requestBody, byte[] responseBody, long seq)
    {
        var request = Protocol.Bgs.Parse<ProcessTaskRequest>(requestBody);
        var blob = request.Attribute.FirstOrDefault(a => a.Name == "p")?.Value?.BlobValue;
        if (blob == null) return null;
        var rpc = PegasusPackets.Deserialize<PegasusUtil.RpcMessage>(blob.ToByteArray());
        var response = Protocol.Bgs.Parse<ProcessTaskResponse>(responseBody);
        return new UtilExchange
        {
            RequestType = (int)rpc.RpcHeader.Type,
            RequestBody = rpc.MessageBody ?? Array.Empty<byte>(),
            System = request.Attribute.FirstOrDefault(a => a.Name == "system")?.Value?.StringValue,
            ResponseType = (int)(response.Result.FirstOrDefault(a => a.Name == "id")?.Value?.IntValue ?? -1),
            ResponseBody = response.Result.FirstOrDefault(a => a.Name == "proto")?.Value?.BlobValue?.ToByteArray() ?? Array.Empty<byte>(),
            RawBgsResponse = responseBody,
            ResponseSeq = seq,
        };
    }

    private void CaptureIdentity(string messageType, byte[] body)
    {
        if (messageType?.EndsWith("Session.V2.Client.CreateSessionResponse") == true)
        {
            var sid = Protocol.Bgs.Parse<Blizzard.GameService.Protocol.Session.V2.Client.CreateSessionResponse>(body).SessionId;
            if (sid != null)
            {
                AccountId = sid.AccountId;
                if (sid.GameAccount != null) { GameAccountId = sid.GameAccount.Id; Region = sid.GameAccount.Region; }
            }
        }
    }

    public BgsExchange FindBgs(uint serviceHash, uint methodId) =>
        Bgs.FirstOrDefault(e => e.ServiceHash == serviceHash && e.MethodId == methodId);

    /// <summary>
    /// Newest session under the given roots that contains a full lobby login (util request 711 = initial state).
    /// </summary>
    public static string FindReferenceSession(params string[] roots)
    {
        foreach (string file in Recording.FindSessions(roots).Reverse())
        {
            foreach (RecordedPacket p in Recording.Read(file))
                if (p.Channel == "util" && !p.IsIn && p.Body != null)
                {
                    var rpc = PegasusPackets.Deserialize<PegasusUtil.RpcMessage>(p.Body);
                    if (rpc.RpcHeader.Type == 711) return file;
                }
        }
        return null;
    }
}
