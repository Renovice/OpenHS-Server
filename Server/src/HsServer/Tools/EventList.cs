using HsServer.Protocol;

namespace HsServer.Tools;

/// <summary>
/// `HsServer events [session.jsonl] [name...]`: the live server's special-event timings (which events were active when
/// the session was recorded) from its SecondaryClientStateResponse (util 714). Card-set membership over time (e.g. which
/// Core cards are current) is keyed on these events in the client DB's CardSetTiming table.
/// </summary>
internal static class EventList
{
    public static int Run(string[] args)
    {
        string session = args.FirstOrDefault(a => a.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)) ?? LatestLiveSession();
        var names = args.Where(a => !a.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)).ToHashSet();
        var events = Read(session);
        if (events == null) { Console.Error.WriteLine($"no 714 in {session}"); return 1; }
        Console.WriteLine($"{events.Count} events in {Path.GetFileName(session)}");
        foreach (var e in events.OrderBy(e => e.Name, StringComparer.Ordinal))
            if (names.Count == 0 || names.Contains(e.Name))
                Console.WriteLine($"  {e.Name,-40} {(e.Active ? "ACTIVE" : "inactive"),-8} start {e.Start?.ToString("yyyy-MM-dd") ?? "-",-10} end {e.End?.ToString("yyyy-MM-dd") ?? "-"}");
        return 0;
    }

    public sealed record Event(string Name, bool Active, DateTime? Start, DateTime? End);

    /// <summary>The events in a recorded session (null if it has no 714), with their dates relative to the recording.</summary>
    public static List<Event> Read(string session)
    {
        foreach (RecordedPacket p in Recording.Read(session))
        {
            if (p.Channel != "util" || !p.IsIn || p.Type != 714 || p.Body == null) continue;
            var state = PegasusPackets.Deserialize<PegasusUtil.SecondaryClientStateResponse>(p.Body);
            var list = state.SecondaryState?.SpecialEventTiming;
            if (list == null) continue;
            return list.Select(t =>
            {
                DateTime? start = t.HasSecondsToStart ? p.Time.AddSeconds(t.SecondsToStart) : null;
                DateTime? end = t.HasSecondsToEnd ? p.Time.AddSeconds(t.SecondsToEnd) : null;
                bool active = (start == null || start <= p.Time) && (end == null || end > p.Time);
                return new Event(t.EventName, active, start, end);
            }).ToList();
        }
        return null;
    }

    /// <summary>The newest recorder session that holds a 714 (a live login).</summary>
    /// <summary>The server's own reference login (live only: sessions recorded against our server are skipped).</summary>
    public static string LatestLiveSession()
    {
        string root = Program.ProjectRoot();
        return Lobby.RecordedAccount.FindReferenceSession(
            Path.Combine(root, "Hearthstone main game", "BepInEx", "HsRecorder"), Path.Combine(root, "RESEARCH", "captures"));
    }
}
