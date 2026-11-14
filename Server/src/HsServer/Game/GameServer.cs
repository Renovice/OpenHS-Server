using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using HsServer.Protocol;

namespace HsServer.Game;

/// <summary>A game slot handed out by the matchmaker; the client presents Handle/Password in its Handshake.</summary>
internal sealed class GameAllocation
{
    public int GameHandle;
    public int ClientHandle;
    public string Password;
    public long Scenario;
    /// <summary>Replay source; null when the game is played against the engine.</summary>
    public RecordedMatch Match;
    public EngineSetup Engine;
    /// <summary>What to do when the engine game ends (set by the matchmaker); null = nothing.</summary>
    public Func<GameOutcome, Task> Finished;
    /// <summary>Who queued (the account name; null on the shared recorded account) and for what (GameType), for the
    /// login's DisconnectedGame (A4).</summary>
    public string Player;
    public long GameType;
    /// <summary>The engine game, created on the first handshake; it outlives its connection (A4 reconnect).</summary>
    public EngineGame Game;
}

/// <summary>Registry shared by matchmaker (allocates) and game server (claims on handshake).</summary>
internal sealed class GameAllocations
{
    private readonly ConcurrentDictionary<int, GameAllocation> byHandle = new();
    private int nextHandle = 1000;

    public GameAllocation Allocate(long scenario, RecordedMatch match, EngineSetup engine = null)
    {
        var a = new GameAllocation
        {
            GameHandle = Interlocked.Increment(ref nextHandle), ClientHandle = Random.Shared.Next(100000, 999999),
            Password = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)),
            Scenario = scenario, Match = match, Engine = engine,
        };
        byHandle[a.GameHandle] = a;
        return a;
    }

    public GameAllocation Claim(int handle, string password) =>
        byHandle.TryGetValue(handle, out var a) && a.Password == password ? a : null;

    /// <summary>A game that ended (or was abandoned) can't be joined again.</summary>
    public void Remove(GameAllocation a) => byHandle.TryRemove(new KeyValuePair<int, GameAllocation>(a.GameHandle, a));

    /// <summary>The player's engine game that is still going (A4: the login offers it as the DisconnectedGame); the newest
    /// if there are several.</summary>
    public GameAllocation Unfinished(string player) =>
        byHandle.Values.Where(a => a.Player == player && a.Game is { Resumable: true }).MaxBy(a => a.GameHandle);
}

/// <summary>Game server: TCP, frames [int32 LE type][int32 LE length][Pegasus body].</summary>
internal sealed class GameServer(GameAllocations allocations, GameOptions options, int port)
{
    private int connections;

    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        Log.Info("game", $"listening on 127.0.0.1:{port}");
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
        string id = "g" + Interlocked.Increment(ref connections);
        using (client)
        {
            client.NoDelay = true;
            var connection = new GameConnection(client.GetStream());
            try
            {
                var (type, body) = await connection.ReadAsync(ct);
                if (type != PegasusPackets.IdOf<PegasusGame.Handshake>()) throw new InvalidDataException($"expected Handshake, got {type}");
                var handshake = PegasusPackets.Deserialize<PegasusGame.Handshake>(body);
                GameAllocation allocation = allocations.Claim(handshake.GameHandle, handshake.Password);
                if (allocation == null) throw new InvalidDataException($"unknown game {handshake.GameHandle} or wrong password");
                if (allocation.Engine != null)
                {
                    EngineGame game;
                    lock (allocation)
                        game = allocation.Game ??= new EngineGame(allocation.Engine, options)
                        {
                            Finished = allocation.Finished, Release = () => allocations.Remove(allocation),
                        };
                    Log.Info(id, $"handshake ok: game {allocation.GameHandle}, scenario {allocation.Scenario}, engine game" +
                                 (game.Connections > 0 ? " (reconnect)" : ""));
                    await game.RunAsync(connection, id, ct);
                }
                else
                {
                    Log.Info(id, $"handshake ok: game {allocation.GameHandle}, scenario {allocation.Scenario}, replaying {allocation.Match}");
                    await new ReplayGame(connection, allocation.Match, options, id).RunAsync(ct);
                }
            }
            catch (Exception e) when (e is IOException or EndOfStreamException or OperationCanceledException)
            {
                Log.Info(id, "game connection ended: " + e.Message);
            }
            catch (Exception e)
            {
                Log.Error(id, e.ToString());
            }
        }
    }
}

internal sealed class GameOptions
{
    /// <summary>A4: how long an engine game waits for its player to come back after the connection is lost (the client
    /// itself retries for 60 s from gameplay, RECONNECT_TIMEOUT; from login it can take a client restart). Our choice:
    /// live's window for a game without a turn timer isn't recorded. Then the player concedes.</summary>
    public double ReconnectSeconds = 300;
    /// <summary>Seconds to wait for the player's own action before the recorded one is played; null = wait forever.</summary>
    public double? AutoplaySeconds;
    /// <summary>Cap for re-creating recorded server-side pauses between consecutive packets.</summary>
    public double MaxPauseSeconds = 3;
    // Engine-game pacing, measured from a live Innkeeper game (RESEARCH/captures/practice-innkeeper-20260929-1x-timeline).
    /// <summary>Server delay before answering the human's END_TURN (live median 846 ms).</summary>
    public double EndTurnResponseSeconds = 0.8;
    /// <summary>Delay from the AI's turn start to its first action (live: first action packet ~1.0-1.5 s after the turn change).</summary>
    public double AiFirstActionSeconds = 1.0;
    /// <summary>Delay between AI actions (live: one packet every ~300 ms); the client animates them back to back.</summary>
    public double AiActionSpacingSeconds = 0.3;
    /// <summary>Play every queued game against the engine instead of replaying recordings.</summary>
    public bool UseEngine;
}

/// <summary>Framed reader/writer for one game TCP connection. Reads run on one task, writes are serialized.</summary>
internal sealed class GameConnection(NetworkStream stream)
{
    private readonly SemaphoreSlim writeLock = new(1, 1);

    public async Task<(int Type, byte[] Body)> ReadAsync(CancellationToken ct)
    {
        byte[] head = new byte[8];
        await stream.ReadExactlyAsync(head, ct);
        int type = BinaryPrimitives.ReadInt32LittleEndian(head);
        int size = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(4));
        if (size < 0 || size > 8 * 1024 * 1024) throw new InvalidDataException($"bad game frame size {size}");
        byte[] body = new byte[size];
        await stream.ReadExactlyAsync(body, ct);
        return (type, body);
    }

    public async Task WriteAsync(int type, byte[] body, CancellationToken ct)
    {
        byte[] frame = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, type);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), body.Length);
        body.CopyTo(frame, 8);
        await writeLock.WaitAsync(ct);
        try { await stream.WriteAsync(frame, ct); }
        finally { writeLock.Release(); }
    }
}
