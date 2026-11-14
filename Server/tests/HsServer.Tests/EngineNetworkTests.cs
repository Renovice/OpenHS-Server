using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using HsServer.Engine;
using HsServer.Game;
using HsServer.Protocol;
using PegasusGame;
using Xunit.Abstractions;

namespace HsServer.Tests;

/// <summary>An engine game over TCP, driven like the real client: handshake, GetGameState, mulligan, options.</summary>
public class EngineNetworkTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Client_can_play_an_engine_game_to_the_end()
    {
        CardDb cards = CardDb.Load(CardDb.FindDefaultDir(TestEnvironment.ProjectRoot));
        var setup = new EngineSetup
        {
            Cards = cards, Scenario = 260, Seed = 11,
            GameSetupBody = PegasusPackets.Serialize(new GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
            Human = new PlayerSetup { HeroCardId = "HERO_01", Deck = Decks.BasicDeck(cards), AccountHi = 1, AccountLo = 2 },
            Opponent = new PlayerSetup { HeroCardId = "HERO_08", Deck = Decks.BasicDeck(cards), IsAi = true },
        };
        var allocations = new GameAllocations();
        GameAllocation game = allocations.Allocate(260, null, setup);
        int port = FreePort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        Task server = new GameServer(allocations, new GameOptions { EndTurnResponseSeconds = 0, AiFirstActionSeconds = 0, AiActionSpacingSeconds = 0 }, port).RunAsync(cts.Token);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        var s = tcp.GetStream();
        // A real client handshake (it carries the required Platform block) with our allocation swapped in.
        var recorded = RecordedMatch.LoadAll(TestEnvironment.Sessions).First().Events.First(e => !e.FromServer && e.Type == PegasusPackets.IdOf<Handshake>());
        var handshake = PegasusPackets.Deserialize<Handshake>(recorded.Body);
        handshake.GameHandle = game.GameHandle;
        handshake.Password = game.Password;
        await Write(s, PegasusPackets.IdOf<Handshake>(), handshake);
        Assert.IsType<GameSetup>(await Read(s, cts.Token));
        await Write(s, PegasusPackets.IdOf<GetGameState>(), new GetGameState());

        EntityChoices mulligan = null;
        while (mulligan == null)
            if (await Read(s, cts.Token) is EntityChoices ec && ec.PlayerId == 1) mulligan = ec;
        await Write(s, PegasusPackets.IdOf<ChooseEntities>(), new ChooseEntities { Id = mulligan.Id, Entities = mulligan.Entities });

        int turns = 0, packets = 0;
        bool over = false;
        while (!over && turns < 60)
        {
            IProtoBuf packet = await Read(s, cts.Token);
            packets++;
            if (packet is PowerHistory history)
                over = history.List.Any(d => d.TagChange is { } t && t.Tag == (int)GAME_TAG.STATE && t.Value == (int)TAG_STATE.COMPLETE);
            if (packet is AllOptions options && !over)
            {
                turns++;
                await Write(s, PegasusPackets.IdOf<ChooseOption>(), new ChooseOption { Id = options.Id, Index = 0, Target = 0, SubOption = -1, Position = 0 });
            }
        }
        output.WriteLine($"turns ended by client: {turns}, packets received: {packets}, game over: {over}");
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
        Assert.True(over, "the AI should win against a player who only ends turns");
    }

    // Concede (and the in-game Restart button, which concedes first) must get the live game-over sequence back;
    // before this was handled the client froze waiting for it.
    [Fact]
    public async Task Concede_ends_the_game_like_live()
    {
        CardDb cards = CardDb.Load(CardDb.FindDefaultDir(TestEnvironment.ProjectRoot));
        var setup = new EngineSetup
        {
            Cards = cards, Scenario = 260, Seed = 12,
            GameSetupBody = PegasusPackets.Serialize(new GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
            Human = new PlayerSetup { HeroCardId = "HERO_01", Deck = Decks.BasicDeck(cards), AccountHi = 1, AccountLo = 2 },
            Opponent = new PlayerSetup { HeroCardId = "HERO_08", Deck = Decks.BasicDeck(cards), IsAi = true },
        };
        var allocations = new GameAllocations();
        GameAllocation game = allocations.Allocate(260, null, setup);
        int port = FreePort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task server = new GameServer(allocations, new GameOptions { EndTurnResponseSeconds = 0, AiFirstActionSeconds = 0, AiActionSpacingSeconds = 0 }, port).RunAsync(cts.Token);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        var s = tcp.GetStream();
        var recorded = RecordedMatch.LoadAll(TestEnvironment.Sessions).First().Events.First(e => !e.FromServer && e.Type == PegasusPackets.IdOf<Handshake>());
        var handshake = PegasusPackets.Deserialize<Handshake>(recorded.Body);
        handshake.GameHandle = game.GameHandle;
        handshake.Password = game.Password;
        await Write(s, PegasusPackets.IdOf<Handshake>(), handshake);
        Assert.IsType<GameSetup>(await Read(s, cts.Token));
        await Write(s, PegasusPackets.IdOf<GetGameState>(), new GetGameState());
        EntityChoices mulligan = null;
        while (mulligan == null)
            if (await Read(s, cts.Token) is EntityChoices ec && ec.PlayerId == 1) mulligan = ec;
        await Write(s, PegasusPackets.IdOf<ChooseEntities>(), new ChooseEntities { Id = mulligan.Id, Entities = mulligan.Entities });

        await Write(s, PegasusPackets.IdOf<PegasusGame.Concede>(), new PegasusGame.Concede());
        var changes = new List<PowerHistoryTagChange>();
        while (!changes.Any(t => t.Tag == (int)GAME_TAG.STATE && t.Value == (int)TAG_STATE.COMPLETE))
            if (await Read(s, cts.Token) is PowerHistory history)
                changes.AddRange(history.List.Where(d => d.TagChange != null).Select(d => d.TagChange));
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }

        var playStates = changes.Where(t => t.Tag == (int)GAME_TAG.PLAYSTATE).Select(t => (TAG_PLAYSTATE)t.Value).ToList();
        Assert.Equal(new[] { TAG_PLAYSTATE.CONCEDED, TAG_PLAYSTATE.LOST, TAG_PLAYSTATE.WON }, playStates.TakeLast(3));
        var steps = changes.Where(t => t.Tag == (int)GAME_TAG.STEP).Select(t => (TAG_STEP)t.Value).ToList();
        Assert.Equal(new[] { TAG_STEP.FINAL_WRAPUP, TAG_STEP.FINAL_GAMEOVER }, steps.TakeLast(2));
    }

    private static readonly Dictionary<int, Type> Types = new();

    private static async Task Write(NetworkStream s, int type, IProtoBuf message)
    {
        byte[] body = PegasusPackets.Serialize(message);
        var frame = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, type);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), body.Length);
        body.CopyTo(frame, 8);
        await s.WriteAsync(frame);
    }

    private static async Task<IProtoBuf> Read(NetworkStream s, CancellationToken ct)
    {
        var head = new byte[8];
        await s.ReadExactlyAsync(head, ct);
        int type = BinaryPrimitives.ReadInt32LittleEndian(head);
        var body = new byte[BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(4))];
        await s.ReadExactlyAsync(body, ct);
        return PegasusPackets.Deserialize(PegasusPackets.TypeFor(type, "PegasusGame"), body);
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
