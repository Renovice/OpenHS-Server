using System.Text.Json;
using HsServer.Engine;

namespace HsServer.Lobby;

/// <summary>
/// Card packs (roadmap C2): which packs exist (client Booster table), what each one can hold, and what a pack opening
/// gives — the rules of RESEARCH/hearthstone-rules.md §F (Blizzard's rates and pity timers since 2023-03-14, duplicate
/// protection; §F4 lists the unpublished parts and our choices).
/// </summary>
internal sealed class BoosterRules
{
    /// <summary>A pack's special cards (§F2). The other slots are Common or Rare.</summary>
    public enum Slot { SignatureLegendary, GoldenLegendary, GoldenEpic, GoldenRare, GoldenCommon, Legendary, Epic }

    /// <param name="Average">Blizzard's "average probability" per pack</param>
    /// <param name="Pity">"max packs": certain on that pack at the latest</param>
    /// <param name="FirstPity">the first one is certain within this many packs (0 = no such rule)</param>
    public sealed record Rate(Slot Slot, double Average, int Pity, int FirstPity = 0)
    {
        /// <summary>The flat chance per pack that, with the pity timer, averages <see cref="Average"/> (§F4).</summary>
        public double Base { get; } = SolveBase(Average, Pity);
    }

    // §F2, rarest first (Blizzard Support "Hearthstone Shop", 2023-03-17)
    public static readonly Rate[] NormalRates =
    {
        new(Slot.SignatureLegendary, 0.006, 361), new(Slot.GoldenLegendary, 0.006, 361), new(Slot.GoldenEpic, 0.014, 142),
        new(Slot.Legendary, 0.05, 40, 10), new(Slot.GoldenRare, 0.071, 29), new(Slot.GoldenCommon, 0.081, 25), new(Slot.Epic, 0.21, 10),
    };
    public static readonly Rate[] ExpansionGoldenRates =
        { new(Slot.SignatureLegendary, 0.05, 40), new(Slot.GoldenLegendary, 0.05, 40, 10), new(Slot.GoldenEpic, 0.21, 10) };
    public static readonly Rate[] OtherGoldenRates =
        { new(Slot.SignatureLegendary, 0.006, 361), new(Slot.GoldenLegendary, 0.05, 40, 10), new(Slot.GoldenEpic, 0.21, 10) };
    public static readonly Rate[] SignatureRates =
        { new(Slot.SignatureLegendary, 0.201, 14), new(Slot.GoldenLegendary, 0.05, 40, 10), new(Slot.GoldenEpic, 0.21, 10) };

    /// <summary>Chance of a Rare for each remaining slot (§F4: about 1.14 Rares per pack with the Rare-or-better rule).</summary>
    public const double RareChance = 0.19;
    public const int CardsPerPack = 5;

    /// <summary>A pack type. <see cref="Member"/> null = not openable here (Epic, Draft, Mercenaries packs...).</summary>
    /// <param name="CatchupSets">a Catch-Up pack's expansions, one slot each (§F2); null for other packs</param>
    public sealed record Pack(int Type, string Name, bool Golden, Rate[] Rates, string OpenEvent, Func<int, bool> Member,
        IReadOnlyList<int> CatchupSets = null)
    {
        public bool Catchup => CatchupSets != null;
    }

    // Catch-Up packs (§F2, wiki Card pack "Catch-Up"): per card Legendary 1.2309% (pity 163 cards, the first within 50),
    // Epic 4.48% (pity 45 cards); at least 20% of the cards Rare or better, the rest Common; regular cards only.
    public static readonly Rate CatchupLegendary = new(Slot.Legendary, 0.012309, 163, 50);
    public static readonly Rate CatchupEpic = new(Slot.Epic, 0.0448, 45);
    public const double CatchupRareShare = 0.20;
    public const int CatchupMinCards = 5, CatchupMaxCards = 50;

    private const int ClassicSet = 3; // Classic packs: the original Classic set (EXPERT1)
    private readonly Dictionary<int, (string Name, int Set, int Premium, string OpenEvent)> boosters = new();
    private readonly Dictionary<int, string> boosterStandardEvent = new(); // pack type → its Standard period (year packs)
    private readonly Dictionary<int, string> standardEvent = new(); // card set → in Standard while this event is on
    private readonly Dictionary<int, string> launchEvent = new();   // card set → ContentLaunchEvent
    private readonly HashSet<int> catchupTypes = new();
    private readonly CardDb cards;
    private readonly CraftingRules crafting;
    private readonly EventCalendar calendar;

    public BoosterRules(string dbfDir, CardDb cards, CraftingRules crafting, EventCalendar calendar)
    {
        this.cards = cards;
        this.crafting = crafting;
        this.calendar = calendar;
        foreach (JsonElement r in Records(dbfDir, "Booster.json"))
        {
            boosters[r.GetProperty("ID").GetInt32()] = (Str(r, "Name"), Int(r, "CardSetId"),
                r.TryGetProperty("Premium", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : -1, Str(r, "OpenPackEvent"));
            boosterStandardEvent[r.GetProperty("ID").GetInt32()] = Str(r, "StandardEvent");
            if (r.TryGetProperty("IsCatchupPack", out var c) && c.ValueKind == JsonValueKind.True) catchupTypes.Add(r.GetProperty("ID").GetInt32());
        }
        foreach (JsonElement r in Records(dbfDir, "CardSet.json"))
        {
            standardEvent[r.GetProperty("ID").GetInt32()] = Str(r, "StandardEvent");
            launchEvent[r.GetProperty("ID").GetInt32()] = Str(r, "ContentLaunchEvent");
        }
    }

    private static List<JsonElement> Records(string dbfDir, string file)
    {
        string path = Path.Combine(dbfDir, file);
        if (!File.Exists(path)) return new List<JsonElement>();
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        return doc.RootElement.GetProperty("records").EnumerateArray().Select(r => r.Clone()).ToList();
    }

    private static int Int(JsonElement r, string name) => r.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
    private static string Str(JsonElement r, string name) => r.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public IEnumerable<int> Types => boosters.Keys;

    public int SetOf(int dbf) => crafting.SetOf(dbf);

    public string NameOf(int type) => boosters.TryGetValue(type, out var b) ? b.Name : null;

    /// <summary>A pack type by id or name (case-insensitive); null when unknown.</summary>
    public int? Find(string text)
    {
        if (int.TryParse(text, out int id)) return boosters.ContainsKey(id) ? id : null;
        var named = boosters.Where(b => string.Equals(b.Value.Name, text, StringComparison.OrdinalIgnoreCase)).Select(b => b.Key).ToList();
        return named.Count >= 1 ? named.Min() : null;
    }

    /// <summary>The pack type, with its pool when we can open it.</summary>
    public Pack Get(int type)
    {
        if (!boosters.TryGetValue(type, out var b)) return null;
        if (catchupTypes.Contains(type))
        {
            List<int> sets = CatchupSetsOf(b.OpenEvent);
            return new Pack(type, b.Name, false, Array.Empty<Rate>(), b.OpenEvent,
                sets.Count == 0 ? null : dbf => sets.Contains(crafting.SetOf(dbf)), sets);
        }
        bool golden = b.Premium == 1;
        bool signature = b.Name != null && b.Name.StartsWith("Signature ", StringComparison.Ordinal);
        Func<int, bool> member = Member(type, b, golden);
        Rate[] rates = !golden ? NormalRates
            : signature ? SignatureRates
            : BaseSet(type, b) > 0 ? ExpansionGoldenRates : OtherGoldenRates;
        return new Pack(type, b.Name, golden, rates, b.OpenEvent, member);
    }

    // The expansion a pack opens: its own CardSetId; a golden pack without one, the normal pack of the same name.
    private int BaseSet(int type, (string Name, int Set, int Premium, string OpenEvent) b)
    {
        if (b.Set > 0) return b.Set;
        if (b.Premium == 1 && b.Name != null && b.Name.StartsWith("Golden ", StringComparison.Ordinal))
        {
            string normal = b.Name["Golden ".Length..];
            var match = boosters.FirstOrDefault(x => x.Key != type && x.Value.Premium != 1 && x.Value.Name == normal);
            if (match.Value.Name != null) return match.Value.Set > 0 ? match.Value.Set : 0;
        }
        return 0;
    }

    /// <summary>Is a card set in Standard now (CardSet.StandardEvent on the calendar).</summary>
    public bool IsStandardSet(int set) => standardEvent.TryGetValue(set, out string e) && calendar.IsActive(e);

    private Func<int, bool> Member(int type, (string Name, int Set, int Premium, string OpenEvent) b, bool golden)
    {
        int set = BaseSet(type, b);
        string kind = b.Name == null ? null : golden && b.Name.StartsWith("Golden ", StringComparison.Ordinal) ? b.Name["Golden ".Length..] : b.Name;
        if (set > 0) return dbf => crafting.SetOf(dbf) == set;
        bool InStandard(int dbf) => standardEvent.TryGetValue(crafting.SetOf(dbf), out string e) && calendar.IsActive(e);
        switch (kind)
        {
            case "Standard": return InStandard;
            case "Wild": return _ => true;
            case "Classic": return dbf => crafting.SetOf(dbf) == ClassicSet;
        }
        // Class packs ("Hunter Pack"): that class's cards from the Standard sets when opened (§F1)
        if (kind != null && kind.EndsWith(" Pack", StringComparison.Ordinal) &&
            Enum.TryParse(kind[..^" Pack".Length].Replace(" ", ""), ignoreCase: true, out TAG_CLASS cls) && cls != TAG_CLASS.NEUTRAL)
            return dbf => InStandard(dbf) && cards.ByDbf(dbf)?.Tag(GAME_TAG.CLASS) == (int)cls;
        // Year packs ("Year of the Phoenix"): the expansions of that Standard year — the sets with an expansion pack and a
        // dated launch that share the pack's StandardEvent (Classic shares Year of the Dragon's but launched "always")
        if (kind != null && kind.StartsWith("Year of ", StringComparison.Ordinal) &&
            (boosterStandardEvent.GetValueOrDefault(type) ?? StandardOfNormal(kind)) is { } year && year != "SPECIAL_EVENT_ALWAYS")
        {
            var sets = boosters.Values.Where(x => x.Set > 0 && x.Premium != 1).Select(x => x.Set).Distinct()
                .Where(s => standardEvent.GetValueOrDefault(s) == year && launchEvent.GetValueOrDefault(s) is { } l && l != "SPECIAL_EVENT_ALWAYS")
                .ToHashSet();
            if (sets.Count > 0) return dbf => sets.Contains(crafting.SetOf(dbf));
        }
        return null; // Epic, Catch-Up, Draft, Mercenaries packs: not defined yet (§F4)
    }

    /// <summary>
    /// A Catch-Up pack's expansions (the client data doesn't list them): the pack unlocks shortly before an expansion
    /// launches; it holds the expansions launched before that one and still in Standard at its launch. Live's examples
    /// (wiki): Badlands → Sunken City, Nathria, Lich King, Festival, TITANS; Whizbang → Festival, TITANS, Badlands;
    /// Perils in Paradise → those + Whizbang.
    /// </summary>
    private List<int> CatchupSetsOf(string openEvent)
    {
        if (calendar.StartOf(openEvent) is not { } unlock) return new List<int>();
        var expansions = boosters.Values.Where(x => x.Set > 0 && x.Premium != 1).Select(x => x.Set).Distinct()
            .Select(s => (Set: s, Launch: calendar.StartOf(launchEvent.GetValueOrDefault(s))))
            .Where(x => x.Launch != null).ToList();
        DateTime? target = expansions.Where(x => x.Launch!.Value.Date >= unlock.Date).Select(x => x.Launch).Min();
        if (target is not { } launch) return new List<int>();
        return expansions.Where(x => x.Launch < launch && calendar.WasActive(standardEvent.GetValueOrDefault(x.Set), launch))
            .OrderBy(x => x.Launch).Select(x => x.Set).ToList();
    }

    // A golden pack's year: the normal pack of that name.
    private string StandardOfNormal(string name) =>
        boosters.Where(x => x.Value.Name == name && x.Value.Premium != 1).Select(x => boosterStandardEvent.GetValueOrDefault(x.Key)).FirstOrDefault();

    /// <summary>Can it be opened now (its OpenPackEvent is on, and we know its pool)?</summary>
    public bool CanOpen(Pack pack) => pack?.Member != null && calendar.IsActive(pack.OpenEvent);

    /// <summary>The cards a pack can hold: collectible, craftable now (§F1), in the pack's pool.</summary>
    public List<CardDef> Pool(Pack pack) =>
        pack?.Member == null ? new List<CardDef>()
            : cards.All.Where(c => c.Tag(GAME_TAG.COLLECTIBLE) == 1 && crafting.Value(c.DbfId, 0) != null && pack.Member(c.DbfId)).ToList();

    /// <summary>The pity state of one pack type for one player (persisted with the collection).</summary>
    public sealed class PityState
    {
        public int Opened { get; set; }
        /// <summary>Packs since the last card of a slot (by slot name).</summary>
        public Dictionary<string, int> Since { get; set; } = new();
        /// <summary>Slots ever opened from this pack type (the "first N packs" rules apply until then).</summary>
        public HashSet<string> Got { get; set; } = new();
    }

    /// <summary>One pack's 5 cards (dbf, premium), updating the pity state (§F2/§F3).</summary>
    /// <param name="collected">copies of a card ever received, all premiums (duplicate protection)</param>
    public List<(int Dbf, int Premium)> Roll(Pack pack, List<CardDef> pool, PityState pity, Func<int, int> collected, Random rng)
    {
        var slots = new List<(int Rarity, int Premium)>();
        foreach (Rate rate in pack.Rates)
        {
            string key = rate.Slot.ToString();
            int since = pity.Since.GetValueOrDefault(key);
            int cap = rate.FirstPity > 0 && !pity.Got.Contains(key) ? Math.Min(rate.FirstPity, rate.Pity) : rate.Pity;
            bool has = slots.Count < CardsPerPack && (since + 1 >= cap || rng.NextDouble() < rate.Base);
            if (has)
            {
                slots.Add(Shape(rate.Slot));
                pity.Since[key] = 0;
                pity.Got.Add(key);
            }
            else pity.Since[key] = since + 1;
        }
        int premium = pack.Golden ? CollectionStore.Golden : CollectionStore.Normal;
        while (slots.Count < CardsPerPack)
            slots.Add((rng.NextDouble() < RareChance ? (int)TAG_RARITY.RARE : (int)TAG_RARITY.COMMON, premium));
        // at least one Rare or better: a Common becomes a Rare (the regular one first)
        if (slots.All(s => s.Rarity == (int)TAG_RARITY.COMMON))
        {
            int i = slots.FindIndex(s => s.Premium == premium);
            slots[i < 0 ? 0 : i] = ((int)TAG_RARITY.RARE, slots[i < 0 ? 0 : i].Premium);
        }
        pity.Opened++;

        var inPack = new Dictionary<int, int>();
        var result = new List<(int, int)>();
        foreach (var (rarity, slotPremium) in slots.OrderBy(_ => rng.Next()))
        {
            int p = slotPremium;
            List<CardDef> candidates = pool.Where(c => Rarity(c) == rarity).ToList();
            if (p == CollectionStore.Signature)
            {
                var signed = candidates.Where(c => c.Tag(GAME_TAG.HAS_SIGNATURE_QUALITY) == 1 && crafting.Value(c.DbfId, CollectionStore.Signature) != null).ToList();
                if (signed.Count > 0) candidates = signed;
                else p = CollectionStore.Golden; // no signature version in this pool: a golden one
            }
            if (Pick(candidates, pool, rarity, inPack, collected, rng) is { } card) result.Add((card.DbfId, p));
        }
        return result;
    }

    // §F3: a card of that rarity you have fewer than the limit of, while there is one; never over the limit in one pack.
    private static CardDef Pick(List<CardDef> candidates, List<CardDef> pool, int rarity, Dictionary<int, int> inPack, Func<int, int> collected, Random rng)
    {
        if (candidates.Count == 0) candidates = pool.Where(c => Rarity(c) <= rarity).ToList(); // a pool without that rarity
        if (candidates.Count == 0) return null;
        int limit = rarity == (int)TAG_RARITY.LEGENDARY ? 1 : 2;
        var fresh = candidates.Where(c => collected(c.DbfId) + inPack.GetValueOrDefault(c.DbfId) < limit).ToList();
        if (fresh.Count == 0) fresh = candidates.Where(c => inPack.GetValueOrDefault(c.DbfId) < limit).ToList();
        if (fresh.Count == 0) fresh = candidates;
        CardDef card = fresh[rng.Next(fresh.Count)];
        inPack[card.DbfId] = inPack.GetValueOrDefault(card.DbfId) + 1;
        return card;
    }

    /// <summary>
    /// A Catch-Up pack (§F2): each expansion's slot gives X cards by how much of the set is collected (Y, copies, 2 per
    /// card and 1 per Legendary, disenchanted ones included): Y ≤ 25% → the most (3 slots 17, 4 slots 12.5, 5 slots 10;
    /// else 50 / slots), Y ≥ 75% → 1, linear in between, the fraction a chance of one more; the total kept within 5..50.
    /// Rarity per card with its own pity counters; at least 20% Rare or better; regular cards only.
    /// </summary>
    /// <param name="collectedShare">set → share of its copies collected (0..1)</param>
    public List<(int Dbf, int Premium)> RollCatchup(Pack pack, List<CardDef> pool, PityState pity, Func<int, int> collected,
        Func<int, double> collectedShare, Random rng)
    {
        var sets = pack.CatchupSets;
        double most = sets.Count == 3 ? 17 : CatchupMaxCards / (double)Math.Max(1, sets.Count);
        var counts = sets.Select(set =>
        {
            double y = collectedShare(set);
            double x = y <= 0.25 ? most : y >= 0.75 ? 1 : most - (most - 1) * (y - 0.25) / 0.5;
            int n = (int)Math.Floor(x);
            return n + (rng.NextDouble() < x - n ? 1 : 0);
        }).ToArray();
        while (counts.Sum() < CatchupMinCards) counts[rng.Next(counts.Length)]++;
        while (counts.Sum() > CatchupMaxCards)
        {
            int i = rng.Next(counts.Length);
            if (counts[i] > 1) counts[i]--;
        }

        int total = counts.Sum();
        var rarities = new int[total];
        for (int i = 0; i < total; i++)
            rarities[i] = CatchupCard(CatchupLegendary, pity, rng) ? (int)TAG_RARITY.LEGENDARY
                : CatchupCard(CatchupEpic, pity, rng) ? (int)TAG_RARITY.EPIC : (int)TAG_RARITY.COMMON;
        int rareOrBetter = rarities.Count(r => r != (int)TAG_RARITY.COMMON);
        foreach (int i in Enumerable.Range(0, total).Where(i => rarities[i] == (int)TAG_RARITY.COMMON).OrderBy(_ => rng.Next())
                     .Take(Math.Max(0, (int)Math.Ceiling(total * CatchupRareShare) - rareOrBetter)))
            rarities[i] = (int)TAG_RARITY.RARE;
        pity.Opened++;

        var inPack = new Dictionary<int, int>();
        var result = new List<(int, int)>();
        int at = 0;
        for (int s = 0; s < sets.Count; s++)
        {
            var setPool = pool.Where(c => crafting.SetOf(c.DbfId) == sets[s]).ToList();
            for (int k = 0; k < counts[s]; k++, at++)
            {
                int rarity = rarities[at];
                if (Pick(setPool.Where(c => Rarity(c) == rarity).ToList(), setPool, rarity, inPack, collected, rng) is { } card)
                    result.Add((card.DbfId, CollectionStore.Normal));
            }
        }
        return result;
    }

    // One Catch-Up card of this rarity? Counted per card ("max cards"), with the first-time rule.
    private static bool CatchupCard(Rate rate, PityState pity, Random rng)
    {
        string key = "Catchup" + rate.Slot;
        int since = pity.Since.GetValueOrDefault(key);
        int cap = rate.FirstPity > 0 && !pity.Got.Contains(key) ? Math.Min(rate.FirstPity, rate.Pity) : rate.Pity;
        if (since + 1 >= cap || rng.NextDouble() < rate.Base)
        {
            pity.Since[key] = 0;
            pity.Got.Add(key);
            return true;
        }
        pity.Since[key] = since + 1;
        return false;
    }

    private static int Rarity(CardDef c) => c.Tag(GAME_TAG.RARITY) is (int)TAG_RARITY.FREE ? (int)TAG_RARITY.COMMON : c.Tag(GAME_TAG.RARITY);

    private static (int Rarity, int Premium) Shape(Slot slot) => slot switch
    {
        Slot.SignatureLegendary => ((int)TAG_RARITY.LEGENDARY, CollectionStore.Signature),
        Slot.GoldenLegendary => ((int)TAG_RARITY.LEGENDARY, CollectionStore.Golden),
        Slot.GoldenEpic => ((int)TAG_RARITY.EPIC, CollectionStore.Golden),
        Slot.GoldenRare => ((int)TAG_RARITY.RARE, CollectionStore.Golden),
        Slot.GoldenCommon => ((int)TAG_RARITY.COMMON, CollectionStore.Golden),
        Slot.Legendary => ((int)TAG_RARITY.LEGENDARY, CollectionStore.Normal),
        _ => ((int)TAG_RARITY.EPIC, CollectionStore.Normal),
    };

    // b with (1 - (1-b)^cap) / b = 1 / average: the mean wait for one, pity included, is 1 / average.
    internal static double SolveBase(double average, int cap)
    {
        double target = 1 / average;
        if (target >= cap) return 0; // the pity timer alone already gives that average
        double lo = 1e-9, hi = 1;
        for (int i = 0; i < 100; i++)
        {
            double mid = (lo + hi) / 2;
            double wait = (1 - Math.Pow(1 - mid, cap)) / mid;
            if (wait > target) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2;
    }
}
