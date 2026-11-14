using System.Text.Json;
using HsServer.Engine;
using HsServer.Lobby;

namespace HsServer.Shop;

/// <summary>
/// "Random card" shop items (PRODUCT_TYPE_RANDOM_CARD): Data is a BoosterCardSet id → its Subset → SubsetRules
/// (client RewardUtils.CreateShopRewardItemDataModel / GetRarityForRandomCardReward). Rules with a tag are evaluated as
/// the data describes them: MinValue ≤ card tag ≤ MaxValue (CARD_SET from CardSetTiming), RuleIsNot excludes, rules on
/// the same tag are OR'd and different tags AND'd. Rules without a tag (their type, e.g. card lists or "not rotated", is
/// missing from the client export) are skipped: our approximation (FINDINGS 2026-10-02). The pool is always collectible
/// cards that are craftable or will be once their set launches (an upcoming set grows with the client data); a
/// BoosterCardSet with a CardSetId also limits the set.
/// </summary>
internal sealed class RandomCards
{
    private sealed record Rule(int Tag, int Min, int Max, bool Not);

    private readonly Dictionary<int, (int Subset, int CardSet)> sets = new();
    private readonly Dictionary<int, List<Rule>> rules = new();
    private readonly Dictionary<int, int> untyped = new(); // subset → rules without a tag (skipped)
    private readonly HashSet<int> notRotated = new(); // subsets with IS_NOT_ROTATED (Standard cards only)
    private readonly Func<int, bool> isStandardSet;
    private readonly CardDb cards;
    private readonly CraftingRules crafting;
    private readonly Dictionary<int, List<CardDef>> cache = new();

    /// <param name="isStandardSet">is a card set in Standard now (CardSet.StandardEvent active); null: the
    /// IS_NOT_ROTATED rule is skipped</param>
    public RandomCards(string dbfDir, CardDb cards, CraftingRules crafting, Func<int, bool> isStandardSet = null)
    {
        this.cards = cards;
        this.crafting = crafting;
        this.isStandardSet = isStandardSet;
        foreach (JsonElement r in Records(dbfDir, "BoosterCardSet.json"))
            sets[Int(r, "ID")] = (Int(r, "SubsetId"), Int(r, "CardSetId"));
        foreach (JsonElement r in Records(dbfDir, "SubsetRule.json"))
        {
            int subset = Int(r, "SubsetId"), tag = Int(r, "Tag");
            if (tag == 0 && isStandardSet != null && r.TryGetProperty("RuleType", out var type) && type.GetString() == "IS_NOT_ROTATED")
            {
                notRotated.Add(subset);
                continue;
            }
            if (tag == 0) { untyped[subset] = untyped.GetValueOrDefault(subset) + 1; continue; }
            if (!rules.TryGetValue(subset, out var list)) rules[subset] = list = new List<Rule>();
            list.Add(new Rule(tag, Int(r, "MinValue"), Int(r, "MaxValue"), r.TryGetProperty("RuleIsNot", out var not) && not.ValueKind == JsonValueKind.True));
        }
    }

    private static List<JsonElement> Records(string dbfDir, string file)
    {
        string path = Path.Combine(dbfDir, file);
        if (!File.Exists(path)) return new List<JsonElement>();
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        return doc.RootElement.GetProperty("records").EnumerateArray().Select(r => r.Clone()).ToList();
    }

    private static int Int(JsonElement r, string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    public bool Knows(int boosterCardSet) => sets.ContainsKey(boosterCardSet);

    /// <summary>Was any rule of this item skipped (its pool is our approximation)?</summary>
    public bool Approximate(int boosterCardSet) => sets.TryGetValue(boosterCardSet, out var s) && untyped.ContainsKey(s.Subset);

    private int TagOf(CardDef card, int tag) => tag == (int)GAME_TAG.CARD_SET ? crafting.SetOf(card.DbfId) : card.Tag((GAME_TAG)tag);

    /// <summary>The cards one random card of this item can be.</summary>
    public List<CardDef> Pool(int boosterCardSet)
    {
        lock (cache)
        {
            if (cache.TryGetValue(boosterCardSet, out var known)) return known;
            var pool = new List<CardDef>();
            if (sets.TryGetValue(boosterCardSet, out var set))
            {
                var subsetRules = rules.GetValueOrDefault(set.Subset) ?? new List<Rule>();
                var required = subsetRules.Where(r => !r.Not).GroupBy(r => r.Tag).ToList();
                var excluded = subsetRules.Where(r => r.Not).ToList();
                foreach (CardDef card in cards.All)
                {
                    // collectible cards that can be crafted, or will be once their set launches (an upcoming set's cards
                    // join as the client data gets them); never-craftable ones (free Core cards) stay out
                    if (card.Tag(GAME_TAG.COLLECTIBLE) != 1) continue;
                    if (crafting.Value(card.DbfId, CollectionStore.Normal, out var why) == null && why != PegasusUtil.BoughtSoldCard.Result.EVENT_NOT_ACTIVE) continue;
                    if (set.CardSet > 0 && crafting.SetOf(card.DbfId) != set.CardSet) continue;
                    if (!required.All(g => g.Any(r => TagOf(card, r.Tag) >= r.Min && TagOf(card, r.Tag) <= r.Max))) continue;
                    if (excluded.Any(r => TagOf(card, r.Tag) >= r.Min && TagOf(card, r.Tag) <= r.Max)) continue;
                    if (notRotated.Contains(set.Subset) && !isStandardSet(crafting.SetOf(card.DbfId))) continue; // Standard only
                    pool.Add(card);
                }
            }
            cache[boosterCardSet] = pool;
            return pool;
        }
    }

    /// <summary><paramref name="count"/> cards from the pool, cards the player has fewer than a deck's worth of first
    /// (duplicate protection, as single-card rewards on live), never the same one twice while others remain.</summary>
    public List<int> Pick(int boosterCardSet, int count, Func<int, int> owned, Random rng)
    {
        var pool = Pool(boosterCardSet);
        var picked = new List<int>();
        for (int i = 0; i < count && pool.Count > 0; i++)
        {
            int Limit(CardDef c) => c.Tag(GAME_TAG.RARITY) == (int)TAG_RARITY.LEGENDARY ? 1 : 2;
            var fresh = pool.Where(c => owned(c.DbfId) + picked.Count(p => p == c.DbfId) < Limit(c)).ToList();
            if (fresh.Count == 0) fresh = pool.Where(c => !picked.Contains(c.DbfId)).ToList();
            if (fresh.Count == 0) fresh = pool;
            picked.Add(fresh[rng.Next(fresh.Count)].DbfId);
        }
        return picked;
    }
}
