using HsServer.Engine;
using HsServer.Protocol;
using PegasusGame;

namespace HsServer.Game;

/// <summary>
/// A live game against the server's own engine: the human is player 1, the AI player 2. Translates client packets
/// (GetGameState, ChooseEntities, ChooseOption, Ping) into engine calls and streams the engine's packets back,
/// pacing the AI turn so its actions are watchable. The game outlives its connection (A4): a client that reconnects
/// (from gameplay, or from login through the 714's DisconnectedGame) is served the game as it is now.
/// </summary>
internal sealed class EngineGame(EngineSetup setup, GameOptions options)
{
    private static readonly int Ping = PegasusPackets.IdOf<PegasusGame.Ping>();
    private static readonly int Pong = PegasusPackets.IdOf<PegasusGame.Pong>();
    private static readonly int GetGameState = PegasusPackets.IdOf<PegasusGame.GetGameState>();
    private static readonly int ChooseEntities = PegasusPackets.IdOf<PegasusGame.ChooseEntities>();
    private static readonly int ChooseOption = PegasusPackets.IdOf<PegasusGame.ChooseOption>();
    private static readonly int Concede = PegasusPackets.IdOf<PegasusGame.Concede>();   // 11: Concede and the Restart button
    private static readonly int UserUI = PegasusPackets.IdOf<PegasusGame.UserUI>();     // 15: cursor/hover sync, nothing to do vs the AI

    private IEngineGame game;
    private bool overLogged;
    private DateTime started;
    // One connection at a time drives the game; a reconnect cancels the one the server still thinks is open.
    private readonly SemaphoreSlim gate = new(1, 1);
    private CancellationTokenSource current;
    private GameConnection connection;
    private string id = "game";

    /// <summary>Called once when the game ends (C4: reward-track XP for the player).</summary>
    public Func<GameOutcome, Task> Finished { get; init; }

    /// <summary>The game is over or abandoned: nobody can join it any more (the allocation goes).</summary>
    public Action Release { get; init; }

    /// <summary>The game has ended and its outcome was reported.</summary>
    public bool Ended => overLogged;
    /// <summary>Started and not over: the player can come back to it.</summary>
    public bool Resumable => game != null && !game.Over;
    /// <summary>How many connections have served the game (the abandon timer checks nobody came back meanwhile).</summary>
    public int Connections { get; private set; }

    public async Task RunAsync(GameConnection connection, string id, CancellationToken ct)
    {
        using var mine = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Interlocked.Exchange(ref current, mine)?.Cancel();
        await gate.WaitAsync(ct);
        int number = 0;
        try
        {
            this.connection = connection;
            this.id = id;
            number = ++Connections;
            await ServeAsync(mine.Token);
        }
        finally
        {
            Interlocked.CompareExchange(ref current, null, mine);
            this.connection = null;
            bool ended = Ended;
            gate.Release();
            Left(ended, number);
        }
    }

    // The connection is gone. An ended game goes; an unfinished one waits for its player (a newer connection than this one
    // keeps it), then is abandoned.
    private void Left(bool ended, int number)
    {
        if (ended) { Release?.Invoke(); return; }
        if (Resumable) Log.Info(id, $"game kept {options.ReconnectSeconds:0} s for the player to come back");
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(options.ReconnectSeconds));
            try { if (await AbandonAsync(number)) Release?.Invoke(); }
            catch (Exception e) { Log.Error(id, "abandoning the game failed: " + e); }
        });
    }

    /// <summary>
    /// Nobody came back within the reconnect window: the human concedes and the game ends as a loss (quests count it by
    /// their usual rules). False when a connection newer than <paramref name="lastConnection"/> took the game up meanwhile.
    /// </summary>
    internal async Task<bool> AbandonAsync(int lastConnection)
    {
        await gate.WaitAsync();
        try
        {
            if (Connections != lastConnection || Ended) return false;
            if (game == null) return true; // never started: nothing to report
            Log.Info(id, "player did not come back: conceding");
            if (!game.Over) game.ConcedeHuman();
            await ReportOverAsync(CancellationToken.None);
            return true;
        }
        finally { gate.Release(); }
    }

    private async Task ServeAsync(CancellationToken ct)
    {
        if (game == null) started = DateTime.UtcNow;
        Task<(int, byte[])> read = null;
        await connection.WriteAsync(PegasusPackets.IdOf<GameSetup>(), setup.GameSetupBody, ct);
        while (!ct.IsCancellationRequested)
        {
            read ??= connection.ReadAsync(ct);
            // the human's turn clock (the rope): wait for the next packet or the clock's next moment
            if (game != null && clock.Next(game.HumanClock, setup.TurnTimer, DateTime.UtcNow) is { } due)
            {
                TimeSpan wait = due - DateTime.UtcNow;
                if (wait > TimeSpan.Zero && await Task.WhenAny(read, Task.Delay(wait, ct)) == read) { }
                else { await ClockAsync(ct); continue; }
            }
            var (type, body) = await read;
            read = null;
            if (type == Ping) { await connection.WriteAsync(Pong, Array.Empty<byte>(), ct); continue; }

            if (type == GetGameState && game == null)
            {
                game = setup.Rules == RulesEngine.Sabber ? new Sabber.SabberEngineGame(setup) : new BasicEngineGame(setup);
                game.Start();
                await FlushAsync(ct);
                Log.Info(id, $"engine game started: {game.Describe}");
            }
            else if (type == GetGameState)
            {
                // A4: the client came back; live answers its GetGameState with a snapshot of the game, then the options.
                if (game.Resume())
                {
                    Log.Info(id, $"player reconnected: {game.Describe}");
                    await FlushAsync(ct);
                    await RunAiTurnsAsync(ct);
                }
                else Log.Warn(id, "this engine can't resume a game");
            }
            else if (type == ChooseEntities && game != null)
            {
                var choice = PegasusPackets.Deserialize<PegasusGame.ChooseEntities>(body);
                if (!game.ChooseEntities(choice.Id, choice.Entities)) Log.Warn(id, $"entity choice {choice.Id} matches no open choice");
                await FlushAsync(ct);
                await RunAiTurnsAsync(ct); // the AI may be first to act after the mulligan
            }
            else if (type == ChooseOption && game != null)
            {
                var choice = PegasusPackets.Deserialize<PegasusGame.ChooseOption>(body);
                TurnClock before = game.HumanClock;
                if (!game.ChooseOption(choice.Id, choice.Index, choice.Target, choice.Position, choice.SubOption))
                {
                    Log.Warn(id, $"option rejected: id {choice.Id} index {choice.Index} target {choice.Target}");
                    game.ResendOptions(); // resync the client with fresh options
                }
                else if (before != null && game.HumanClock is { } after && after.Turn == before.Turn)
                {
                    // an action: a fast fuse (the last turn timed out idle) goes back to the whole turn (wiki Turn)
                    if (clock.Acted() is { } hide) await SendTimerAsync(hide, ct);
                }
                else if (before != null) clock.EndedByPlayer();
                // Live answers END_TURN after ~0.8 s (median 846 ms over 9 turns); other actions immediately.
                if (game.AiToAct && options.EndTurnResponseSeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(options.EndTurnResponseSeconds), ct);
                await FlushAsync(ct);
                await RunAiTurnsAsync(ct);
            }
            else if (type == Concede && game != null)
            {
                // The client waits for the game-over sequence; for Restart it then disconnects and re-queues.
                Log.Info(id, "player conceded");
                game.ConcedeHuman();
                await FlushAsync(ct);
            }
            else if (type == UserUI)
            {
            }
            else
            {
                Log.Debug(id, $"client packet {type} ({body.Length} bytes) not handled by the engine");
            }
            if (game?.Over == true && !overLogged) await ReportOverAsync(ct);
        }
    }

    private readonly RopeClock clock = new();

    // the clock's moment: the rope, or the end of the human's turn
    private async Task ClockAsync(CancellationToken ct)
    {
        if (clock.RopeDue(DateTime.UtcNow) is { } rope)
        {
            await SendTimerAsync(rope, ct);
            return;
        }
        if (clock.EndDue(DateTime.UtcNow))
        {
            Log.Info(id, "human out of time: turn ends");
            game.TimeOut();
            await FlushAsync(ct);
            if (game.Over && !overLogged) { await ReportOverAsync(ct); return; }
            await RunAiTurnsAsync(ct);
        }
    }

    private async Task SendTimerAsync(TurnTimer timer, CancellationToken ct)
    {
        if (connection != null) await connection.WriteAsync(PegasusPackets.IdOf<TurnTimer>(), PegasusPackets.Serialize(timer), ct);
    }

    private async Task ReportOverAsync(CancellationToken ct)
    {
        overLogged = true;
        Log.Info(id, "game over");
        await FlushAsync(ct);
        if (Finished != null)
        {
            try { await Finished(new GameOutcome(game.HumanWon, DateTime.UtcNow - started, game.Tally()) { RunDeckEdits = game.RunDeckEdits }); }
            catch (Exception e) { Log.Error(id, "game-over handling failed: " + e); }
        }
    }

    // Live pacing (HsRecorder timeline, 10 Innkeeper turns): the server delivers the whole AI turn within 2-3 s —
    // first action ~1 s after the turn change, then one action every ~300 ms — and the client plays it back at its
    // own animation speed. Waiting on the server side instead leaves the board idle between AI actions.
    private async Task RunAiTurnsAsync(CancellationToken ct)
    {
        double wait = options.AiFirstActionSeconds;
        while (game.AiToAct)
        {
            await Task.Delay(TimeSpan.FromSeconds(wait), ct);
            game.AiStep();
            await FlushAsync(ct);
            wait = options.AiActionSpacingSeconds;
        }
    }

    // Without a connection (an abandoned game) the packets are dropped; a reconnect gets the snapshot instead.
    private async Task FlushAsync(CancellationToken ct)
    {
        foreach (IProtoBuf packet in game?.Drain() ?? new List<IProtoBuf>())
            if (connection != null)
                await connection.WriteAsync(PegasusPackets.IdOf(packet.GetType()), PegasusPackets.Serialize(packet), ct);
    }
}

/// <summary>How an engine game ended for the human: won or not, how long it lasted (from the game setup), and what they
/// did (quests).</summary>
internal sealed record GameOutcome(bool Won, TimeSpan Duration, GameTally Tally = null)
{
    /// <summary>(2026-10-09, Dalaran Heist) what the game did to the player's adventure-run deck, in order (Bob's Tavern); null: none.
    /// The server writes them to the run's save data (Lobby/HeistTavern.cs).</summary>
    public IReadOnlyList<SabberStoneCore.Model.RunDeckEdit> RunDeckEdits { get; init; }
}

/// <summary>Everything an engine game needs, prepared by the matchmaker.</summary>
internal sealed class EngineSetup
{
    public required CardDb Cards { get; init; }
    public required PlayerSetup Human { get; init; }
    public required PlayerSetup Opponent { get; init; }
    public required byte[] GameSetupBody { get; init; }
    public int Scenario { get; init; }
    /// <summary>The scenario's mode from the client data (Scenario.ModeId: 1 Normal, 2 Expert; 0 unknown) — picks the
    /// opponent AI's personality (Sabber.Ai.GameAis.ForMode).</summary>
    public int Mode { get; init; }
    public int Seed { get; init; } = Environment.TickCount;
    /// <summary>Which rules engine plays the game.</summary>
    public RulesEngine Rules { get; init; } = RulesEngine.Basic;
    /// <summary>The human's turns are timed (the rope): Ranked / Casual / Brawl-like games; solo modes (Practice, adventures)
    /// aren't, unless a card limits the turn (wiki Turn: "Solo Adventures does not normally feature a turn timer").</summary>
    public bool TurnTimer { get; init; }
    /// <summary>(2026-10-09) the scenario's own tags (Bob's Tavern: Lobby/HeistTavern.cs); null: none (rules engine).</summary>
    public ScenarioTags ScenarioTags { get; init; }
}

internal enum RulesEngine
{
    /// <summary>The original hand-written engine (HsServer.Engine.GameState): a small card set, live-verified history.</summary>
    Basic,
    /// <summary>The SabberStone-based rules engine (HsServer.Rules) through <see cref="Sabber.SabberEngineGame"/>.</summary>
    Sabber,
}
