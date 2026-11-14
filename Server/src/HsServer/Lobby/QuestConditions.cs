using System.Text.Json;
using System.Text.RegularExpressions;
using HsServer.Engine;
using HsServer.Game;

namespace HsServer.Lobby;

/// <summary>
/// C4: what each quest counts. Live keeps quest conditions server-side (the client data has only text and quota), so a
/// quest's Description is matched against the patterns of Server/data/quest-conditions.tsv (first match wins), giving
/// one game event and a filter, evaluated on a game's <see cref="GameTally"/>. A quest no pattern matches never
/// progresses (logged once).
/// </summary>
internal sealed class QuestConditions
{
    public sealed record Term(string Key, string Op, string[] Values);
    public sealed record Condition(string Event, IReadOnlyList<Term> Terms);
    private sealed record Rule(Regex Pattern, string Event, string Filter);

    private static readonly long[] RankedGameTypes = { 2, 30, 58, 63 }; // BGT_RANKED_STANDARD, _WILD, _CLASSIC, _TWIST (LeagueGameType)

    private readonly List<Rule> rules = new();
    private readonly CardDb cards;
    private readonly Dictionary<int, int> raceTags = new(); // race id → its "is this race" tag (CardRace.json)
    private readonly Dictionary<int, Condition> byQuest = new();
    private readonly ClientCardSets sets;
    private readonly Dictionary<string, string> setAliases = new(StringComparer.OrdinalIgnoreCase); // quest-text name → TAG_CARD_SET

    public QuestConditions(string tsvPath, CardDb cards, string dbfDir)
    {
        this.cards = cards;
        if (!File.Exists(tsvPath)) Log.Warn("quests", $"no quest conditions at {tsvPath}: quests won't progress from games");
        if (File.Exists(tsvPath))
            foreach (string line in File.ReadAllLines(tsvPath))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                string[] parts = line.Split('\t');
                if (parts[0] == "@set" && parts.Length >= 3) { setAliases[parts[1].Trim()] = parts[2].Trim(); continue; }
                if (parts.Length < 2) continue;
                rules.Add(new Rule(new Regex(parts[0], RegexOptions.CultureInvariant), parts[1].Trim(), parts.Length > 2 ? parts[2].Trim() : ""));
            }
        sets = dbfDir != null && File.Exists(Path.Combine(dbfDir, "CardSetTiming.json")) ? ClientCardSets.Load(dbfDir) : null;
        string raceFile = Path.Combine(dbfDir ?? "", "CardRace.json");
        if (File.Exists(raceFile))
            using (var doc = JsonDocument.Parse(File.ReadAllBytes(raceFile)))
                foreach (JsonElement r in doc.RootElement.GetProperty("records").EnumerateArray())
                    if (r.TryGetProperty("ID", out var id) && r.TryGetProperty("IsRaceTag", out var tag))
                        raceTags[id.GetInt32()] = tag.GetInt32();
    }

    /// <summary>The quest's condition, or null when no pattern matches its text.</summary>
    public Condition For(QuestRules.Quest quest)
    {
        lock (byQuest)
        {
            if (byQuest.TryGetValue(quest.Id, out Condition known)) return known;
            string text = Normalize(quest.Description);
            Condition found = null;
            foreach (Rule rule in rules)
            {
                Match m = rule.Pattern.Match(text);
                if (!m.Success) continue;
                string filter = Regex.Replace(rule.Filter, @"\$\{(\w+)\}", g => m.Groups[g.Groups[1].Value].Value);
                found = new Condition(rule.Event, ParseFilter(filter, setAliases));
                break;
            }
            return byQuest[quest.Id] = found;
        }
    }

    /// <summary>How much a game adds to a quest with this condition.</summary>
    public int Count(Condition condition, GameTally tally)
    {
        if (condition == null || tally == null) return 0;
        switch (condition.Event)
        {
            case "GAME_PLAYED": return tally.CountsAsPlayed && GameMatches(condition, tally) ? 1 : 0;
            case "GAME_WON": return tally.Won && GameMatches(condition, tally) ? 1 : 0;
            case GameTally.CardPlayed or GameTally.MinionSummoned or GameTally.DeathrattleTriggered:
                return tally.Events.Where(e => e.Kind == condition.Event && CardMatches(condition, e)).Sum(e => e.Amount * Weight(condition, e));
            default:
                return tally.Events.Where(e => e.Kind == condition.Event).Sum(e => e.Amount);
        }
    }

    private bool GameMatches(Condition condition, GameTally tally) => condition.Terms.All(t => t.Key switch
    {
        "class" => t.Values.Any(v => Enum.TryParse(v, out TAG_CLASS c) && (int)c == tally.HeroClass),
        "deck" => tally.StartingDeck.Any(id => cards.TryGet(id, out CardDef d) && t.Values.Any(v => string.Equals(d.Name, v, StringComparison.OrdinalIgnoreCase))),
        "mode" => t.Values.Contains("RANKED") && RankedGameTypes.Contains(tally.GameType),
        _ => true,
    });

    // "(Beasts, Elementals, and Murlocs count 2x!)": bonus=N with bonusrace= / bonustag=
    private int Weight(Condition condition, TallyEvent e)
    {
        Term bonus = condition.Terms.FirstOrDefault(t => t.Key == "bonus");
        if (bonus == null || !int.TryParse(bonus.Values.FirstOrDefault(), out int n) || e.CardId == null || !cards.TryGet(e.CardId, out CardDef card)) return 1;
        bool extra = condition.Terms.Any(t => t.Key == "bonusrace" && t.Values.Any(v => HasRace(card, v))
                                              || t.Key == "bonustag" && t.Values.Any(v => HasTag(card, v))
                                              || t.Key == "bonusany" && t.Values.Any(v => HasRace(card, v) || HasTag(card, v) || HasRarity(card, v)));
        return extra ? n : 1;
    }

    private bool CardMatches(Condition condition, TallyEvent e)
    {
        if (e.CardId == null || !cards.TryGet(e.CardId, out CardDef card)) return condition.Terms.Count == 0;
        // (2026-10-08) the card's base Cost, not what was paid — wiki Quest.txt:84 "Only counts the base cost of the card, not how much mana
        // you played it for"
        int cost = card.Cost;
        return condition.Terms.All(t => t.Key switch
        {
            "type" => t.Values.Any(v => Enum.TryParse(v, out TAG_CARDTYPE type) && card.Type == type),
            "race" => t.Values.Any(v => HasRace(card, v)),
            "school" => t.Values.Any(v => Enum.TryParse(v, out TAG_SPELL_SCHOOL s) && card.Tag(GAME_TAG.SPELL_SCHOOL) == (int)s),
            "tag" => t.Values.Any(v => HasTag(card, v)),
            "rarity" => t.Values.Any(v => HasRarity(card, v)),
            "trait" => t.Values.Any(v => e.Traits?.Split(',').Contains(v) == true),
            "set" => t.Values.Any(v => Enum.TryParse(v, out TAG_CARD_SET s) && sets != null && sets.IsIn(card.DbfId, (int)s)),
            "cost" => t.Values.Any(v => v switch
            {
                "odd" => cost % 2 == 1,
                "even" => cost % 2 == 0,
                _ => int.TryParse(v, out int n) && t.Op switch { "<=" => cost <= n, ">=" => cost >= n, _ => cost == n },
            }),
            _ => true,
        });
    }

    private static bool HasTag(CardDef card, string name) => Enum.TryParse(name, out GAME_TAG tag) && card.Tag(tag) > 0;
    private static bool HasRarity(CardDef card, string name) => Enum.TryParse(name, out TAG_RARITY r) && card.Tag(GAME_TAG.RARITY) == (int)r;

    // The client's races (EntityBase.GetRacesForNonTrinket): CARDRACE (ALL counts as every race) + each CardRace tag
    private bool HasRace(CardDef card, string name)
    {
        if (!Enum.TryParse(name, out TAG_RACE race)) return false;
        int main = card.Tag(GAME_TAG.CARDRACE);
        return main == (int)race || main == (int)TAG_RACE.ALL || raceTags.TryGetValue((int)race, out int tag) && card.Tags.GetValueOrDefault(tag) > 0;
    }

    private static List<Term> ParseFilter(string filter, Dictionary<string, string> setAliases)
    {
        var terms = new List<Term>();
        foreach (Match m in Regex.Matches(filter, @"(\w+)(<=|>=|=)((?:[^ ]| (?!\w+(?:<=|>=|=)))+)"))
        {
            string key = m.Groups[1].Value;
            string[] values = Regex.Split(m.Groups[3].Value, @",? and/or |,? and |,? or |, |\|")
                .Select(v => v.Trim()).Where(v => v.Length > 0)
                .Select(v => key == "set" && setAliases.TryGetValue(v, out string set) ? set : Canonical(key, v)).ToArray();
            terms.Add(new Term(key, m.Groups[2].Value, values));
        }
        return terms;
    }

    // "Death Knight" → DEATHKNIGHT, "Murlocs" → MURLOC, "Mechs" → MECHANICAL; card names (deck=) stay as written
    private static string Canonical(string key, string value)
    {
        if (key is "deck" or "cost" or "bonus") return value;
        string upper = value.Replace(" ", "").Replace("-", "").ToUpperInvariant();
        if (key is "race" or "bonusrace" or "bonusany")
        {
            if (upper.StartsWith("MECH")) return "MECHANICAL";
            if (!Enum.TryParse(upper, out TAG_RACE _) && upper.EndsWith("S") && Enum.TryParse(upper[..^1], out TAG_RACE _)) return upper[..^1];
        }
        return upper;
    }

    private static string Normalize(string description) =>
        Regex.Replace(Regex.Replace(description ?? "", @"<[^>]+>|\[x\]", ""), @"\s+", " ").Trim();
}
