using System.Threading.Channels;
using HsServer.Protocol;

namespace HsServer.Game;

/// <summary>
/// Plays a recorded live match back to the real client, packet for packet (original bytes). Where the recording has a
/// player action, it waits for the client's own action of the same kind (or continues after the autoplay timeout).
/// The client's choice itself is not simulated: the recorded outcome is shown. Purpose: validate the whole game pipe
/// and the client's rendering against Blizzard-exact data before our own engine takes over.
/// </summary>
internal sealed class ReplayGame(GameConnection connection, RecordedMatch match, GameOptions options, string id)
{
    private static readonly int Ping = PegasusPackets.IdOf<PegasusGame.Ping>();
    private static readonly int Pong = PegasusPackets.IdOf<PegasusGame.Pong>();
    private static readonly int Handshake = PegasusPackets.IdOf<PegasusGame.Handshake>();

    private readonly Channel<(int Type, byte[] Body)> fromClient = Channel.CreateUnbounded<(int, byte[])>();

    public async Task RunAsync(CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task reader = ReadClientAsync(linked.Token);
        int sent = 0, awaited = 0, autoplayed = 0;
        DateTime? lastServerTime = null;
        try
        {
            foreach (MatchEvent e in match.Events)
            {
                if (e.FromServer)
                {
                    if (e.Type == Pong) continue; // we answer pings live
                    if (lastServerTime is { } previous && options.MaxPauseSeconds > 0)
                    {
                        double pause = Math.Min((e.Time - previous).TotalSeconds, options.MaxPauseSeconds);
                        if (pause > 0.02) await Task.Delay(TimeSpan.FromSeconds(pause), linked.Token);
                    }
                    await connection.WriteAsync(e.Type, e.Body, linked.Token);
                    lastServerTime = e.Time;
                    sent++;
                    continue;
                }
                if (e.Type == Ping || e.Type == Handshake) continue;
                // A recorded client action: wait for the real client's action of the same kind.
                bool acted = await WaitForClientAsync(e.Type, e.Type == 1 ? null : options.AutoplaySeconds, linked.Token);
                if (acted) awaited++; else autoplayed++;
                lastServerTime = null; // don't re-create the recorded player's thinking time
            }
            Log.Info(id, $"replay finished: {sent} packets sent, {awaited} player actions, {autoplayed} autoplayed");
            // Let the client finish its end-of-game flow; it closes the connection itself.
            await Task.WhenAny(reader, Task.Delay(TimeSpan.FromMinutes(2), linked.Token));
        }
        finally
        {
            linked.Cancel();
            try { await reader; } catch { /* connection closing */ }
        }
    }

    private async Task<bool> WaitForClientAsync(int type, double? timeoutSeconds, CancellationToken ct)
    {
        using var timeout = timeoutSeconds is { } s ? new CancellationTokenSource(TimeSpan.FromSeconds(s)) : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            while (true)
            {
                var (got, _) = await fromClient.Reader.ReadAsync(linked.Token);
                if (got == type) return true;
                Log.Debug(id, $"client sent {got} while replay expects {type}; ignored");
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            Log.Debug(id, $"autoplay: recorded action {type} used");
            return false;
        }
    }

    private async Task ReadClientAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var (type, body) = await connection.ReadAsync(ct);
                if (type == Ping) { await connection.WriteAsync(Pong, Array.Empty<byte>(), ct); continue; }
                Log.Debug(id, $"client -> {type} ({body.Length} bytes)");
                await fromClient.Writer.WriteAsync((type, body), ct);
            }
        }
        finally { fromClient.Writer.TryComplete(); }
    }
}
