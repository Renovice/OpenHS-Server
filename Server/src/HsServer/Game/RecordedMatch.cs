using Blizzard.GameService.Protocol.Matchmaking.V2.Client;
using HsServer.Protocol;

namespace HsServer.Game;

/// <summary>One game-server packet of a recorded match, in either direction.</summary>
internal sealed record MatchEvent(bool FromServer, int Type, byte[] Body, DateTime Time, long Seq);

/// <summary>A complete recorded match (handshake to disconnect) and the scenario it was queued for.</summary>
internal sealed class RecordedMatch
{
    public long Scenario;
    public string Session;
    public long StartSeq;
    public readonly List<MatchEvent> Events = new();

    public override string ToString() =>
        $"{System.IO.Path.GetFileName(Session)}#{StartSeq} scenario={Scenario} packets={Events.Count}";

    /// <summary>Every match in the given sessions; the scenario comes from the preceding QueueMatchmakingRequest.</summary>
    public static List<RecordedMatch> LoadAll(IEnumerable<string> sessions)
    {
        var matches = new List<RecordedMatch>();
        foreach (string session in sessions)
        {
            long scenario = 0;
            RecordedMatch current = null;
            foreach (RecordedPacket p in Recording.Read(session))
            {
                if (p.Channel == "bgs" && !p.IsIn && p.Message?.EndsWith("Matchmaking.V2.Client.QueueMatchmakingRequest") == true && p.Body != null)
                {
                    var request = Bgs.Parse<QueueMatchmakingRequest>(p.Body);
                    var attr = request.Options?.CreationProperties?.Attributes.FirstOrDefault(a => a.Name == "scenario");
                    scenario = attr?.Value?.IntValue ?? 0;
                    continue;
                }
                if (p.Channel != "game" || p.Body == null) continue;
                if (!p.IsIn && p.Type == 168) // Handshake starts a new match
                {
                    current = new RecordedMatch { Scenario = scenario, Session = session, StartSeq = p.Seq };
                    matches.Add(current);
                }
                current?.Events.Add(new MatchEvent(p.IsIn, p.Type, p.Body, p.Time, p.Seq));
            }
        }
        // Matches that never got past the handshake are useless for replay.
        return matches.Where(m => m.Events.Count(e => e.FromServer && e.Type == 19) > 0).ToList();
    }
}
