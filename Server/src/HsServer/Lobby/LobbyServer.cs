using System.Net;
using System.Net.Sockets;
using Blizzard.GameService.Protocol;
using HsServer.Net;
using HsServer.Protocol;

namespace HsServer.Lobby;

/// <summary>Handles matchmaking RPCs (queue → game server handoff).</summary>
internal interface IMatchmaker
{
    Task HandleAsync(LobbySession session, Header header, BgsFrame frame, CancellationToken ct);
    /// <summary>A4: the player's game still going on this server, as the login's DisconnectedGame; null = none.</summary>
    PegasusShared.GameConnectionInfo Unfinished(PlayerAccount player) => null;
}

/// <summary>Shared lobby state for all connections.</summary>
internal sealed class LobbyContext
{
    public required RecordedAccount Account { get; init; }
    public required SaveDataStore SaveData { get; init; }
    public required IMatchmaker Matchmaker { get; init; }
    /// <summary>Server-owned decks; null = deck packets and the login deck list are plain replay.</summary>
    public DeckStore Decks { get; init; }
    /// <summary>Cards the account owns at login in addition to its recorded collection (dbf id, normal copies): the
    /// Core set, free for everyone on live. Null = the recorded collection as is.</summary>
    public IReadOnlyList<(int Dbf, int Count)> GrantedCards { get; init; }
    /// <summary>Player accounts (B1): each login token names a player with their own ids, save data and decks. Null =
    /// one shared account (the recorded one, <see cref="SaveData"/>, <see cref="Decks"/>).</summary>
    public AccountStore Accounts { get; init; }
    /// <summary>B2: craft / disenchant values and rules from the client data (null: crafting isn't handled).</summary>
    public CraftingRules Crafting { get; init; }
    /// <summary>B2: the special-event calendar the login 714 announces (null: the recorded list as is).</summary>
    public EventCalendar Calendar { get; init; }
    /// <summary>C2: card packs (pools, drop rules); null: pack opening isn't handled.</summary>
    public BoosterRules Boosters { get; init; }
    /// <summary>C3: what the shop sells (prices, contents, Battlegrounds skin tables).</summary>
    public Shop.ShopOrders Orders { get; init; }
    /// <summary>C4: the reward tracks (XP, claims); null: not handled.</summary>
    public RewardTracks RewardTracks { get; init; }
    /// <summary>Zilliax Deluxe 3000's saved versions (module tags, live's history); null: not handled.</summary>
    public ZilliaxBuilds Zilliax { get; init; }
    /// <summary>C4: daily / weekly quests; null: live's recorded quest lists are replayed.</summary>
    public Quests Quests { get; init; }
    /// <summary>D2: Lucky Draw boxes; null: live's recorded box state is replayed.</summary>
    public LuckyDraws LuckyDraw { get; init; }
    /// <summary>D1: the Black Market; null: live's recorded answers are replayed.</summary>
    public BlackMarkets BlackMarket { get; init; }
    /// <summary>The Ranked ladder (MedalInfo); null: live's recorded medal is replayed.</summary>
    public RankedLadder Ranked { get; init; }
    /// <summary>The Dalaran Heist run loop: the client's loot / treasure picks and retire (save data writes).</summary>
    public HeistRuns Heist { get; init; }
    /// <summary>(2026-10-09, user) adventure wings every player owns beyond the recording's (Naxxramas: the client hides a rotated
    /// adventure with no owned wing — AdventureConfig.ShouldDisplayAdventure — and the recorded account owns none of it).</summary>
    public IReadOnlyList<int> GrantedWings { get; init; } = Array.Empty<int>();
}

/// <summary>Loopback WebSocket listener for the Battle.net RPC lobby (client connects via LocalBootstrap).</summary>
internal sealed class LobbyServer(LobbyContext context, int port)
{
    private int connections;

    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        Log.Info("lobby", $"listening on 127.0.0.1:{port} ({BgsFrame.SubProtocol})");
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
        string id = "c" + Interlocked.Increment(ref connections);
        using (client)
        {
            client.NoDelay = true;
            try
            {
                using var socket = await WebSocketAcceptor.AcceptAsync(client.GetStream(), BgsFrame.SubProtocol, ct);
                Log.Info(id, "connected");
                await new LobbySession(socket, context, id).RunAsync(ct);
                Log.Info(id, "disconnected");
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or System.Net.WebSockets.WebSocketException)
            {
                Log.Info(id, "connection ended: " + e.Message);
            }
            catch (Exception e)
            {
                Log.Error(id, e.ToString());
            }
        }
    }
}

/// <summary>Placeholder until the game server exists: logs queue requests and leaves them pending.</summary>
internal sealed class NoMatchmaker : IMatchmaker
{
    public Task HandleAsync(LobbySession session, Header header, BgsFrame frame, CancellationToken ct)
    {
        Log.Warn("matchmaking", $"queue request m={header.MethodId} ignored (no game server yet)");
        return Task.CompletedTask;
    }
}
