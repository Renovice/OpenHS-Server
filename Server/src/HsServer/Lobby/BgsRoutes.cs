namespace HsServer.Lobby;

/// <summary>
/// Battle.net service hashes (Header.ServiceHash) used by the 36.6 client, observed in recordings.
/// Client→server services are called by the client; listener services are called by us (notifications).
/// </summary>
internal static class BgsRoutes
{
    // client → server
    public const uint Connection = 1698982289;      // 1 Connect, 5 KeepAlive (no response)
    public const uint Authentication = 3224338966;  // 1 Logon, 3 GenerateAuthToken
    public const uint Account = 584852580;          // 101 GetAccountInfo
    public const uint Session = 2067257200;         // 1 CreateSession, 6 ...
    public const uint Presence = 328015884;         // 3 BatchSubscribe, 6 Update
    public const uint Whisper = 4276202004;         // 1 Subscribe
    public const uint ChannelMembership = 2119327385; // 1 Subscribe
    public const uint Friends = 1483325068;         // 1 Subscribe, 3 SentInvitations, 4 ReceivedInvitations, 5 Friends
    public const uint GameUtilities = 1572557250;   // 1 ProcessTask (Pegasus util packets)
    public const uint Matchmaking = 2858242545;     // 1 QueueMatchmaking

    // server → client listeners
    public const uint AuthenticationListener = 2645037419; // 1 LogonComplete, 2 LogonQueueUpdate, 3 LogonQueueEnd
    public const uint SessionListener = 3165069621;        // 1 SessionCreated
    public const uint NotificationListener = 593673933;    // 1 NotificationReceived (util notifications)
    public const uint PresenceListener = 4101150473;       // 1 PresenceStateUpdated
    public const uint MatchmakingListener = 3914139686;    // 5 MatchmakingResult

    public const uint TitleId = 1465140039; // "WTCG"
}
