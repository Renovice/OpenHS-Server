using System.Text.Json;
using HsServer.Protocol;

namespace HsServer.Lobby;

/// <summary>
/// The server-wide special-event calendar (B2; quests, shop and seasons hang on it too). The client learns it from the
/// login 714 (SecondaryState.SpecialEventTiming: name, id, seconds to start / end from now) and asks it everything that
/// is "on" right now: crafting (Card.CraftingEvent, else the set's ContentLaunchEvent), nerf refunds (CardValue
/// OverrideEvent), set rotation... The server answers from the same calendar so both agree.
/// Dates are absolute: the reference recording's list pinned to its own clock (an event that ended after the recording
/// has ended now), then the admin's changes in Server/state/events.json on top. Each login gets them relative to now.
/// Rules as the client's EventTimingManager: "always" is on, "never" is off, an event not in the list is off, else on
/// from its start (none = always started) until its end (none = never ends).
/// </summary>
internal sealed class EventCalendar
{
    public sealed record Entry(string Name, long Id, DateTime? Start, DateTime? End)
    {
        public bool IsActive(DateTime utcNow) =>
            !(Start is { } s && End is { } e && e < s) && (Start == null || Start <= utcNow) && (End == null || End > utcNow);
    }

    /// <summary>An admin change: dates for one event (null = open).</summary>
    public sealed record Change(DateTime? Start, DateTime? End);

    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> nameById = new();
    private readonly Dictionary<string, string> nameByEnum = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Change> changes = new(StringComparer.Ordinal);
    private readonly string changesPath;
    private readonly object gate = new();

    /// <summary>"Now" for every check (tests pin it).</summary>
    public Func<DateTime> Clock { get; init; } = () => DateTime.UtcNow;

    /// <param name="eventMap">the client's EventMap.json (DBF event number / enum member → name); null = names only</param>
    /// <param name="recorded">the reference list with absolute dates</param>
    /// <param name="changesPath">the admin's changes (Server/state/events.json); null = none, not saved</param>
    public EventCalendar(string eventMap, IEnumerable<Entry> recorded, string changesPath = null)
    {
        if (eventMap != null && File.Exists(eventMap))
            using (var doc = JsonDocument.Parse(File.ReadAllBytes(eventMap)))
                foreach (JsonElement r in doc.RootElement.GetProperty("records").EnumerateArray())
                {
                    string name = r.GetProperty("Name").GetString();
                    nameById[r.GetProperty("ID").GetInt32()] = name;
                    if (r.TryGetProperty("Enum", out JsonElement member)) nameByEnum[member.GetString()!] = name;
                }
        foreach (Entry e in recorded ?? Enumerable.Empty<Entry>()) entries[e.Name] = e;
        this.changesPath = changesPath;
        if (changesPath != null && File.Exists(changesPath))
            foreach (var (name, change) in JsonSerializer.Deserialize<Dictionary<string, Change>>(File.ReadAllText(changesPath)) ?? new())
                Apply(name, change);
    }

    /// <summary>The event list of a recorded login (its last 714), pinned to the recording's clock.</summary>
    public static List<Entry> FromRecording(string session)
    {
        RecordedPacket p = Recording.Read(session).LastOrDefault(p => p.Channel == "util" && p.IsIn && p.Type == 714 && p.Body != null);
        var list = p == null ? null : PegasusPackets.Deserialize<PegasusUtil.SecondaryClientStateResponse>(p.Body).SecondaryState?.SpecialEventTiming;
        if (list == null) return new List<Entry>();
        DateTime at = p.Time.ToUniversalTime();
        return list.Select(t => new Entry(t.EventName, t.HasEventId ? t.EventId : 0,
            t.HasSecondsToStart ? at.AddSeconds(t.SecondsToStart) : null, t.HasSecondsToEnd ? at.AddSeconds(t.SecondsToEnd) : null)).ToList();
    }

    public int Count { get { lock (gate) return entries.Count; } }

    /// <summary>An event's name from a DBF value: a number (EventMap), an EventTimingType member, or already a name;
    /// null for none (UNKNOWN / empty).</summary>
    public string NameOf(string dbfEvent)
    {
        if (string.IsNullOrEmpty(dbfEvent) || dbfEvent == "UNKNOWN") return null;
        if (int.TryParse(dbfEvent, out int id)) return nameById.TryGetValue(id, out string byId) ? byId : null;
        return nameByEnum.TryGetValue(dbfEvent, out string byEnum) ? byEnum : dbfEvent;
    }

    /// <summary>Is the event (a DBF value or a name) on right now — exactly as the client decides it.</summary>
    public bool IsActive(string dbfEvent)
    {
        string name = NameOf(dbfEvent);
        if (name == null || name == "never") return false;
        if (name == "always") return true;
        lock (gate) return entries.TryGetValue(name, out Entry e) && e.IsActive(Clock());
    }

    public Entry Get(string name) { lock (gate) return entries.GetValueOrDefault(name); }

    /// <summary>Was the event (a DBF value or a name) on at <paramref name="utc"/>?</summary>
    public bool WasActive(string dbfEvent, DateTime utc)
    {
        string name = NameOf(dbfEvent);
        if (name == null || name == "never") return false;
        if (name == "always") return true;
        lock (gate) return entries.TryGetValue(name, out Entry e) && e.IsActive(utc);
    }

    /// <summary>When the event (a DBF value or a name) starts; null for none / open.</summary>
    public DateTime? StartOf(string dbfEvent) => NameOf(dbfEvent) is { } name ? Get(name)?.Start : null;

    public IReadOnlyList<Entry> All() { lock (gate) return entries.Values.ToList(); }

    /// <summary>Does the client's data name this event (or does the calendar have it)?</summary>
    public bool Knows(string name)
    {
        lock (gate) return entries.ContainsKey(name) || nameById.ContainsValue(name);
    }

    /// <summary>Admin: set an event's dates (null = open), saved in events.json; an unknown event is added.</summary>
    public void Set(string name, DateTime? start, DateTime? end)
    {
        lock (gate)
        {
            var change = new Change(start?.ToUniversalTime(), end?.ToUniversalTime());
            changes[name] = change;
            Apply(name, change);
            if (changesPath != null)
                StateFiles.WriteAtomic(changesPath, JsonSerializer.SerializeToUtf8Bytes(changes, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private void Apply(string name, Change change)
    {
        changes[name] = change;
        long id = entries.TryGetValue(name, out Entry known) ? known.Id : 0;
        entries[name] = new Entry(name, id, change.Start, change.End);
    }

    /// <summary>The login list (714) as of now: seconds to start / end from now, open ends left out.</summary>
    public List<PegasusUtil.SpecialEventTiming> Timings()
    {
        DateTime now = Clock();
        lock (gate)
            return entries.Values.Select(e =>
            {
                var t = new PegasusUtil.SpecialEventTiming { EventName = e.Name };
                if (e.Id != 0) t.EventId = e.Id;
                if (e.Start is { } s) t.SecondsToStart = (long)Math.Floor((s - now).TotalSeconds);
                if (e.End is { } end) t.SecondsToEnd = (long)Math.Floor((end - now).TotalSeconds);
                return t;
            }).ToList();
    }
}
