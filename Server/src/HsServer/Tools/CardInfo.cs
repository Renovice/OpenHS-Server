using System.Text.Json;

namespace HsServer.Tools;

/// <summary>
/// `HsServer card &lt;id&gt; [...]`: the client's own definition of a card (text, every tag, and the tokens and
/// enchantments whose id starts with the card's id) from the DBF export, for scripting cards against the client.
/// </summary>
internal static class CardInfo
{
    public static int Run(string[] args)
    {
        if (args.Length == 0) { Console.WriteLine("usage: HsServer card <cardId> [...]"); return 1; }
        string dir = Path.Combine(AppContext.BaseDirectory, "data");
        dir = Directory.Exists(dir) ? dir : Path.Combine(Directory.GetCurrentDirectory(), "data");
        string dbf = Directory.GetDirectories(dir, "dbf-*").OrderBy(d => d).Last();

        var cards = new List<(int Id, string CardId, string Name, string Text)>();
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dbf, "Card.json"))))
            foreach (var r in doc.RootElement.GetProperty("records").EnumerateArray())
                cards.Add((r.GetProperty("ID").GetInt32(), Str(r, "NoteMiniGuid"), Str(r, "Name"), Str(r, "TextInHand")));

        var tags = new Dictionary<int, List<(int Tag, int Value)>>();
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dbf, "CardTag.json"))))
            foreach (var r in doc.RootElement.GetProperty("records").EnumerateArray())
            {
                // a reference alone is a mention; with IsPowerKeywordTag it is the card's own keyword (ReprintCards)
                if (r.TryGetProperty("IsReferenceTag", out var rt) && rt.GetBoolean() &&
                    !(r.TryGetProperty("IsPowerKeywordTag", out var pk) && pk.GetBoolean())) continue;
                int id = Int(r, "CardId");
                if (!tags.TryGetValue(id, out var list)) tags[id] = list = new();
                list.Add((Int(r, "TagId"), Int(r, "TagValue")));
            }

        foreach (string wanted in args)
        {
            var matches = cards.Where(c => c.CardId.Equals(wanted, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0) { Console.WriteLine($"{wanted}: not in the client DB"); continue; }
            string prefix = matches[0].CardId;
            foreach (var c in cards.Where(c => c.CardId.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                                               || c.CardId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && c.CardId.Length > prefix.Length && !char.IsDigit(c.CardId[prefix.Length])))
            {
                Console.WriteLine($"{c.CardId} (dbf {c.Id}) {c.Name}: {c.Text.Replace("\n", " ")}");
                foreach (var (tag, value) in tags.GetValueOrDefault(c.Id) ?? new())
                    Console.WriteLine($"    {(Enum.IsDefined(typeof(Engine.GAME_TAG), tag) ? ((Engine.GAME_TAG)tag).ToString() : tag.ToString()),-32} {value}");
            }
        }
        return 0;
    }

    private static int Int(JsonElement r, string name) =>
        r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static string Str(JsonElement r, string name) =>
        r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";
}
