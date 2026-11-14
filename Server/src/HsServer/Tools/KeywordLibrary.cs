using System.Text.Json;
using HsServer.Engine;
using SabberStoneCore.Loader;

namespace HsServer.Tools;

/// <summary>
/// `HsServer keywords [--cards]`: the keyword library. Every keyword the current client's Core and Legacy cards carry
/// (their own keyword tags in the client DB — not the ones their text only mentions), how many cards use it, and the
/// rules engine's support for it (<see cref="Keywords.Status"/>). --cards lists the cards per keyword that isn't fully
/// supported.
/// </summary>
internal static class KeywordLibrary
{
    private static readonly (string Prefix, string Name)[] Pools = { ("CORE_", "Core"), ("VAN_", "Legacy") };

    public static int Run(string[] args)
    {
        bool listCards = args.Contains("--cards");
        string dir = CardDb.FindDefaultDir(Program.ProjectRoot());

        var cards = new Dictionary<int, (string Id, string Name)>();
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir, "Card.json"))))
            foreach (JsonElement r in doc.RootElement.GetProperty("records").EnumerateArray())
                if (r.TryGetProperty("NoteMiniGuid", out JsonElement id))
                    cards[r.GetProperty("ID").GetInt32()] = (id.GetString(), r.TryGetProperty("Name", out JsonElement n) ? n.GetString() : "");

        // The client's keyword list (KeywordText: the tags with a keyword tooltip). Blizzard doesn't flag every keyword's
        // card tags as IsPowerKeywordTag (Dredge, Excavate and Imbue aren't; Imbue's are all "reference"), so a tag counts
        // as a keyword if either says so.
        var keywordTags = new HashSet<int>();
        string keywordText = Path.Combine(dir, "KeywordText.json");
        if (File.Exists(keywordText))
            using (var doc = JsonDocument.Parse(File.ReadAllBytes(keywordText)))
                foreach (JsonElement r in doc.RootElement.GetProperty("records").EnumerateArray())
                    keywordTags.Add(r.GetProperty("Tag").GetInt32());

        // dbf id → its keyword tags; dbf id → collectible
        var keywords = new Dictionary<int, List<int>>();
        var references = new List<(int Card, int Tag)>(); // keyword tags a card's text only mentions
        var collectible = new HashSet<int>();
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir, "CardTag.json"))))
            foreach (JsonElement r in doc.RootElement.GetProperty("records").EnumerateArray())
            {
                int card = r.GetProperty("CardId").GetInt32(), tag = r.GetProperty("TagId").GetInt32();
                int value = r.TryGetProperty("TagValue", out JsonElement v) ? v.GetInt32() : 0;
                if (tag == (int)GAME_TAG.COLLECTIBLE && value == 1) collectible.Add(card);
                bool power = r.TryGetProperty("IsPowerKeywordTag", out JsonElement k) && k.GetBoolean();
                bool reference = r.TryGetProperty("IsReferenceTag", out JsonElement rf) && rf.GetBoolean();
                if (value != 0 && !power && reference && keywordTags.Contains(tag)) references.Add((card, tag));
                else if (value != 0 && (power || keywordTags.Contains(tag)))
                {
                    if (!keywords.TryGetValue(card, out var list)) keywords[card] = list = new List<int>();
                    list.Add(tag);
                }
            }

        // a keyword Blizzard only ever stores as a reference (Imbue) is still the cards' own keyword
        var owned = new HashSet<int>(keywords.Values.SelectMany(t => t));
        foreach (var (card, tag) in references.Where(r => !owned.Contains(r.Tag)))
        {
            if (!keywords.TryGetValue(card, out var list)) keywords[card] = list = new List<int>();
            list.Add(tag);
        }

        // keyword tag → pool → cards
        var usage = new SortedDictionary<int, Dictionary<string, List<string>>>();
        foreach (var (dbfId, tags) in keywords)
        {
            if (!collectible.Contains(dbfId) || !cards.TryGetValue(dbfId, out var card)) continue;
            // Everything else collectible (every set since all client cards load) counts as "Other".
            string pool = Pools.FirstOrDefault(p => card.Id.StartsWith(p.Prefix, StringComparison.Ordinal)).Name ?? "Other";
            foreach (int tag in tags.Distinct())
            {
                if (!usage.TryGetValue(tag, out var byPool)) usage[tag] = byPool = new Dictionary<string, List<string>>();
                if (!byPool.TryGetValue(pool, out var list)) byPool[pool] = list = new List<string>();
                list.Add($"{card.Id} {card.Name}");
            }
        }

        Console.WriteLine($"keywords on collectible {string.Join(" / ", Pools.Select(p => p.Name))} cards (client DB {Path.GetFileName(dir)}):");
        Console.WriteLine($"  {"tag",5}  {"keyword",-34} {"Core",5} {"Legacy",6} {"Other",6}  status");
        var totals = new Dictionary<Keywords.Support, int>();
        foreach (var (tag, byPool) in usage.OrderBy(u => Keywords.Status.TryGetValue(u.Key, out var s) ? (int)s.Support : -1).ThenBy(u => u.Key))
        {
            string name = Enum.IsDefined(typeof(GAME_TAG), tag) ? ((GAME_TAG)tag).ToString() : $"tag {tag}";
            var (support, note) = Keywords.Status.TryGetValue(tag, out var s) ? s : (Keywords.Support.Unreviewed, "");
            totals[support] = totals.GetValueOrDefault(support) + 1;
            int Count(string pool) => byPool.TryGetValue(pool, out var l) ? l.Count : 0;
            Console.WriteLine($"  {tag,5}  {name,-34} {Count("Core"),5} {Count("Legacy"),6} {Count("Other"),6}  {support}{(note == "" ? "" : ": " + note)}");
            if (listCards && support != Keywords.Support.Supported)
                foreach (string c in byPool.Values.SelectMany(l => l).OrderBy(c => c))
                    Console.WriteLine($"         {c}");
        }
        Console.WriteLine($"  {string.Join(", ", totals.OrderBy(t => t.Key).Select(t => $"{t.Key} {t.Value}"))}");
        return 0;
    }
}
