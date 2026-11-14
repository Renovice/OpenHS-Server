using System.Text.Json;

namespace HsServer.Engine;

/// <summary>A card's static definition: identity and every base tag from the client's own database.</summary>
internal sealed class CardDef
{
    public int DbfId;
    public string CardId;
    public string Name;
    public readonly Dictionary<int, int> Tags = new();

    public int Tag(GAME_TAG tag) => Tags.TryGetValue((int)tag, out int v) ? v : 0;
    public TAG_CARDTYPE Type => (TAG_CARDTYPE)Tag(GAME_TAG.CARDTYPE);
    public int Cost => Tag(GAME_TAG.COST);
    public int Atk => Tag(GAME_TAG.ATK);
    public int Health => Tag(GAME_TAG.HEALTH);
    public override string ToString() => $"{Name} ({CardId})";
}

/// <summary>
/// Card database from HsRecorder's DBF export of the client (Card.json + CardTag.json). The server therefore uses
/// exactly the client's base stats, texts and keywords; only effects need server-side scripts.
/// </summary>
internal sealed class CardDb
{
    private readonly Dictionary<string, CardDef> byCardId = new(StringComparer.Ordinal);
    private readonly Dictionary<int, CardDef> byDbf = new();

    public int Count => byDbf.Count;

    public CardDef this[string cardId] => byCardId.TryGetValue(cardId, out var c) ? c : throw new KeyNotFoundException("unknown card " + cardId);
    public CardDef ByDbf(int dbfId) => byDbf.TryGetValue(dbfId, out var c) ? c : null;
    public bool TryGet(string cardId, out CardDef card) => byCardId.TryGetValue(cardId, out card);
    public IEnumerable<CardDef> All => byDbf.Values;

    public static CardDb Load(string dbfDir)
    {
        var db = new CardDb();
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dbfDir, "Card.json"))))
        {
            foreach (JsonElement r in doc.RootElement.GetProperty("records").EnumerateArray())
            {
                var card = new CardDef
                {
                    DbfId = r.GetProperty("ID").GetInt32(),
                    CardId = r.TryGetProperty("NoteMiniGuid", out var g) ? g.GetString() : null,
                    Name = r.TryGetProperty("Name", out var n) ? n.GetString() : null,
                };
                db.byDbf[card.DbfId] = card;
                if (card.CardId != null) db.byCardId.TryAdd(card.CardId, card);
            }
        }
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dbfDir, "CardTag.json"))))
        {
            foreach (JsonElement t in doc.RootElement.GetProperty("records").EnumerateArray())
            {
                // The exporter omits zero values; a missing TagValue means 0.
                int value = t.TryGetProperty("TagValue", out var v) ? v.GetInt32() : 0;
                if (value == 0) continue;
                if (db.byDbf.TryGetValue(t.GetProperty("CardId").GetInt32(), out var card))
                    card.Tags[t.GetProperty("TagId").GetInt32()] = value;
            }
        }
        return db;
    }

    /// <summary>Default location: Server/data/dbf-&lt;version&gt; (newest).</summary>
    public static string FindDefaultDir(string projectRoot) =>
        Directory.EnumerateDirectories(Path.Combine(projectRoot, "Server", "data"), "dbf-*")
            .OrderByDescending(d => d, StringComparer.Ordinal).FirstOrDefault();
}
