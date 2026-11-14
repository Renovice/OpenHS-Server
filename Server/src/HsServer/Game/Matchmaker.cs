using Blizzard.GameService.Protocol;
using Blizzard.GameService.Protocol.Account.V2;
using Blizzard.GameService.Protocol.Matchmaking.V2;
using Blizzard.GameService.Protocol.Matchmaking.V2.Client;
using HsServer.Lobby;
using HsServer.Protocol;

namespace HsServer.Game;

/// <summary>What the client queued for: game-level "scenario"/"type" plus its player attributes "deck"/"hero_card_id".</summary>
internal sealed record QueueRequest(long Scenario, long GameType, long DeckId, long HeroCardDbId, PlayerAccount Player = null);

/// <summary>
/// QueueMatchmaking → allocate a game → QueueEntry + QueueMatchmakingResponse + MatchmakingResult (connect info),
/// mirroring the live server's sequence. The game is served by <see cref="GameServer"/> on loopback.
/// </summary>
internal sealed class Matchmaker(
    GameAllocations allocations, IReadOnlyList<RecordedMatch> matches, int gamePort,
    Func<QueueRequest, EngineSetup> engineFactory = null, Func<QueueRequest, GameOutcome, Task> gameOver = null) : IMatchmaker
{
    private long nextRequestId = DateTime.UtcNow.Ticks;

    public async Task HandleAsync(LobbySession session, Header header, BgsFrame frame, CancellationToken ct)
    {
        if (header.MethodId != 1)
        {
            Log.Warn("matchmaking", $"method {header.MethodId} not implemented");
            await session.ReplyAsync(header, new NoData());
            return;
        }
        var request = frame.Decode<QueueMatchmakingRequest>();
        long scenario = request.Options?.CreationProperties?.Attributes.FirstOrDefault(a => a.Name == "scenario")?.Value?.IntValue ?? 0;
        long gameType = request.Options?.CreationProperties?.Attributes.FirstOrDefault(a => a.Name == "type")?.Value?.IntValue ?? 0;

        var playerAttributes = request.Options?.Players.FirstOrDefault()?.Attributes;
        long PlayerInt(string name) => playerAttributes?.FirstOrDefault(a => a.Name == name)?.Value?.IntValue ?? 0;
        var queued = new QueueRequest(scenario, gameType, PlayerInt("deck"), PlayerInt("hero_card_id"), session.Player);

        EngineSetup engine = engineFactory?.Invoke(queued);
        RecordedMatch match = engine != null ? null : matches.LastOrDefault(m => m.Scenario == scenario) ?? matches.LastOrDefault();
        if (engine == null && match == null)
        {
            Log.Warn("matchmaking", $"no recorded match to replay (scenario {scenario}) and engine disabled");
            await session.ReplyAsync(header, new NoData(), status: 3);
            return;
        }
        GameAllocation game = allocations.Allocate(scenario, match, engine);
        game.Player = session.Player?.Name;
        game.GameType = gameType;
        if (engine != null && gameOver != null) game.Finished = outcome => gameOver(queued, outcome);
        var requestId = new RequestId { Id = (ulong)Interlocked.Increment(ref nextRequestId) };
        GameAccountHandle player = request.Options?.Players.FirstOrDefault()?.GameAccount;

        var entry = new QueueEntryNotification { RequestId = requestId, WaitTimes = new QueueWaitTimes { MinWaitS = 0, MaxWaitS = 1 } };
        if (player != null) { entry.Members.Add(player); entry.RequestInitiator = player; }
        await session.NotifyAsync(Lobby.BgsRoutes.MatchmakingListener, 1, entry);
        await session.ReplyAsync(header, new QueueMatchmakingResponse { RequestId = requestId });

        var connect = new ConnectInfo { Address = new Blizzard.GameService.Protocol.Address { Address_ = "127.0.0.1", Port = (uint)gamePort } };
        connect.Attributes.Add(Attr("token", game.Password));
        connect.Attributes.Add(Attr("version", "hsserver"));
        connect.Attributes.Add(Attr("game", game.GameHandle));
        connect.Attributes.Add(Attr("player", 0));
        connect.Attributes.Add(Attr("id", game.ClientHandle));
        await session.NotifyAsync(Lobby.BgsRoutes.MatchmakingListener, 5, new MatchmakingResultNotification
        {
            RequestId = requestId, Result = 0, ConnectInfo = connect,
            GameHandle = new GameHandle { GameInstanceId = (ulong)game.GameHandle, MatchmakerGuid = 1, GameServerGuid = 1 },
        });
        Log.Info("matchmaking", $"type {gameType} scenario {scenario} deck {queued.DeckId} -> game {game.GameHandle} " +
                                (engine != null ? $"(engine: {engine.Human.HeroCardId} [{engine.Human.Deck.Count} cards] vs {engine.Opponent.HeroCardId})" : $"(replay {match})"));
    }

    /// <summary>
    /// A4: the login's DisconnectedGame for the player's unfinished engine game. The client builds its game-server info
    /// from it (NetCache.OnDisconnectedGame: address, port, game handle, client handle, password, scenario, game type,
    /// format) and rejoins it from login (ReconnectMgr.ReconnectToGameFromLogin).
    /// </summary>
    public PegasusShared.GameConnectionInfo Unfinished(PlayerAccount player)
    {
        GameAllocation game = allocations.Unfinished(player?.Name);
        if (game == null) return null;
        return new PegasusShared.GameConnectionInfo
        {
            Address = "127.0.0.1", Port = gamePort, GameHandle = game.GameHandle, ClientHandle = game.ClientHandle,
            AuroraPassword = game.Password, Scenario = (int)game.Scenario, GameType = (PegasusShared.GameType)game.GameType,
            FormatType = PegasusShared.FormatType.FT_WILD, Version = "hsserver",
        };
    }

    private static Blizzard.GameService.Protocol.V2.Attribute Attr(string name, string value) =>
        new() { Name = name, Value = new Blizzard.GameService.Protocol.V2.Variant { StringValue = value } };

    private static Blizzard.GameService.Protocol.V2.Attribute Attr(string name, long value) =>
        new() { Name = name, Value = new Blizzard.GameService.Protocol.V2.Variant { IntValue = value } };
}
