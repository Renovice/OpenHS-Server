using System.Text.Json;

namespace HsServer.Lobby;

/// <summary>
/// What each calendar event is, from the client's own tables (2026-11-14, user: "event list" shows what every event does):
/// SpecialEvent (quests + an event reward track: "Epic Duels of Rock Legends"), LuckyDrawBox (the paid prize machines and bundles:
/// "Eye of the Empire Bundle"), BlackMarket ("Wanderer's"), CardSet / Booster (an expansion's launch, its Standard years, a pack's
/// opening). An event the tables never name stays "other" under its raw name. A table names an event by its EventMap ID (as text) or
/// its Enum.
/// </summary>
internal sealed class EventNames
{
    public enum Kind { Event, PrizeMachine, BlackMarket, Expansion, Other }

    public static readonly IReadOnlyDictionary<Kind, string> Titles = new Dictionary<Kind, string>
    {
        [Kind.Event] = "Events (quests and an event reward track)",
        [Kind.PrizeMachine] = "Prize machines and bundles (paid pulls)",
        [Kind.BlackMarket] = "Black Market",
        [Kind.Expansion] = "Expansions and packs (launch, Standard, pack opening)",
        [Kind.Other] = "Other (not named by the client's data)",
    };

    private readonly Dictionary<string, (Kind Kind, string Label)> byEvent = new(StringComparer.OrdinalIgnoreCase);

    public EventNames(string dbfDir)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // EventMap ID or Enum -> Name
        foreach (JsonElement e in Records(dbfDir, "EventMap"))
        {
            string name = Text(e, "Name");
            if (name == null) continue;
            if (Text(e, "ID") is { } id) names[id] = name;
            if (Text(e, "Enum") is { } en) names[en] = name;
        }
        void Add(string reference, Kind kind, string label)
        {
            if (reference == null || label == null || !names.TryGetValue(reference, out string name) || name is "always" or "never" or "none") return;
            if (!byEvent.TryGetValue(name, out var known) || kind < known.Kind) byEvent[name] = (kind, label);
            else if (known.Kind == kind && !known.Label.Contains(label, StringComparison.Ordinal)) byEvent[name] = (kind, known.Label + " / " + label);
        }
        foreach (JsonElement e in Records(dbfDir, "SpecialEvent")) Add(Text(e, "EventTiming"), Kind.Event, Text(e, "DisplayName"));
        foreach (JsonElement e in Records(dbfDir, "LuckyDrawBox")) Add(Text(e, "Event"), Kind.PrizeMachine, Text(e, "Name"));
        foreach (JsonElement e in Records(dbfDir, "BlackMarket"))
        {
            string label = (Text(e, "DisplayName") ?? "") + " Black Market";
            Add(Text(e, "EventTiming"), Kind.BlackMarket, label);
            Add(Text(e, "GracePeriodTiming"), Kind.BlackMarket, label + " (grace period)");
        }
        foreach (JsonElement e in Records(dbfDir, "CardSet"))
        {
            string set = int.TryParse(Text(e, "ID"), out int id) && Enum.IsDefined(typeof(Engine.TAG_CARD_SET), id)
                ? ((Engine.TAG_CARD_SET)id).ToString() : "card set " + Text(e, "ID");
            Add(Text(e, "ContentLaunchEvent"), Kind.Expansion, set + " launch");
            Add(Text(e, "StandardEvent"), Kind.Expansion, set + " in Standard");
        }
        foreach (JsonElement e in Records(dbfDir, "Booster"))
        {
            string pack = Text(e, "Name") ?? "pack";
            Add(Text(e, "OpenPackEvent"), Kind.Expansion, pack + " packs open");
            Add(Text(e, "PrereleaseOpenPackEvent"), Kind.Expansion, pack + " pre-release packs");
        }
    }

    public (Kind Kind, string Label) Of(string eventName) =>
        eventName != null && byEvent.TryGetValue(eventName, out var known) ? known : (Kind.Other, "");

    private static IEnumerable<JsonElement> Records(string dbfDir, string table)
    {
        string file = Path.Combine(dbfDir, table + ".json");
        if (!File.Exists(file)) return Array.Empty<JsonElement>();
        using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
        return doc.RootElement.GetProperty("records").EnumerateArray().Select(r => r.Clone()).ToList();
    }

    private static string Text(JsonElement e, string field) =>
        !e.TryGetProperty(field, out JsonElement v) ? null
        : v.ValueKind == JsonValueKind.String ? v.GetString()
        : v.ValueKind == JsonValueKind.Number ? v.GetRawText() : null;
}
