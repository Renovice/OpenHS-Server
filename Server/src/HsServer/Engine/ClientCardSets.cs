using System.Text.Json;
using HsServer.Lobby;

namespace HsServer.Engine;

/// <summary>
/// Which card set a card belongs to right now, from the client DB's CardSetTiming table (card → set while an event is
/// on). Core cards move in and out of the Core set (1637) at rotations; out of it they sit in hidden set 1810. "On" is the
/// server's <see cref="EventCalendar"/> (the live dates), so the Core set rotates when its events do, like the client.
/// </summary>
internal sealed class ClientCardSets
{
    public const int CoreSet = 1637;

    private readonly Dictionary<int, List<(int Set, string Event)>> timing = new();
    private Func<string, bool> isActive;

    /// <param name="isActive">is a DBF event on now (EventCalendar.IsActive); null = the calendar of the reference
    /// recording (and the admin's changes)</param>
    public static ClientCardSets Load(string dbfDir, Func<string, bool> isActive = null)
    {
        var sets = new ClientCardSets { isActive = isActive ?? DefaultCalendar(dbfDir).IsActive };
        using var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dbfDir, "CardSetTiming.json")));
        foreach (JsonElement r in doc.RootElement.GetProperty("records").EnumerateArray())
        {
            int card = r.GetProperty("CardId").GetInt32();
            if (!sets.timing.TryGetValue(card, out var list)) sets.timing[card] = list = new List<(int, string)>();
            list.Add((r.GetProperty("CardSetId").GetInt32(), r.GetProperty("EventTimingEvent").GetString()));
        }
        return sets;
    }

    /// <summary>The server's calendar when no one hands one over (tools, tests): the reference recording's events.</summary>
    public static EventCalendar DefaultCalendar(string dbfDir)
    {
        string root = Program.ProjectRoot();
        string reference = RecordedAccount.FindReferenceSession(
            Path.Combine(HsServer.Protocol.GameFolder.GameDir(root), "BepInEx", "HsRecorder"), Path.Combine(root, "RESEARCH", "captures"));
        return new EventCalendar(Path.Combine(dbfDir, "EventMap.json"),
            reference == null ? null : EventCalendar.FromRecording(reference), Path.Combine(root, "Server", "state", "events.json"));
    }

    /// <summary>Is the card (by dbf id) in the given set now?</summary>
    public bool IsIn(int dbfId, int set) =>
        timing.TryGetValue(dbfId, out var list) && list.Any(t => t.Set == set && isActive(t.Event));

    public bool IsCurrentCore(int dbfId) => IsIn(dbfId, CoreSet);

    /// <summary>Is the DBF event on now (this calendar)?</summary>
    public bool IsActive(string dbfEvent) => isActive(dbfEvent);
}
