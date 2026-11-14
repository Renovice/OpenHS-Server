using HsServer.Engine;
using HsServer.Protocol;

namespace HsServer.Lobby;

/// <summary>
/// Deck sideboards (client SIDEBOARD_TYPE 3427: E.T.C.'s band, Zilliax Deluxe 3000's modules, Commander Beatrix's minion,
/// King of the Underbelly's Beasts): which cards one may hold. The owner card's MAX_SIDEBOARD_CARDS (2931) and its
/// ALLOWED_SIDEBOARD_SUBSET (3476) — a card list the live server sends the client as a SUBSET_CARD asset
/// (GetAssetResponse 322; the client's DeckRule_SideboardSubset reads it), taken here from a recorded login — and
/// Zilliax's split (the client's ZilliaxSideboardDeck: 2 functional modules + 1 cosmetic). The client checks a deck before
/// it can be queued; the server checks again when it builds a game (<see cref="Valid"/>) and leaves out what doesn't fit.
/// </summary>
internal sealed class SideboardRules
{
    private const GAME_TAG Cosmetic = GAME_TAG.ZILLIAX_CUSTOMIZABLE_COSMETICMODULE, Functional = GAME_TAG.ZILLIAX_CUSTOMIZABLE_FUNCTIONALMODULE;
    private const int Neutral = 12; // TAG_CLASS.NEUTRAL

    private readonly CardDb cards;
    private readonly Dictionary<int, HashSet<int>> subsets = new();

    /// <param name="sessions">Recorded sessions, the newest last: the newest whose GetAssetResponse (322) has the owners'
    /// subsets is used.</param>
    public SideboardRules(CardDb cards, IEnumerable<string> sessions)
    {
        this.cards = cards;
        var wanted = cards.All.Select(c => c.Tag(GAME_TAG.ALLOWED_SIDEBOARD_SUBSET)).Where(s => s != 0).ToHashSet();
        foreach (string session in (sessions ?? Enumerable.Empty<string>()).Reverse())
        {
            if (wanted.All(subsets.ContainsKey)) break;
            foreach (RecordedPacket p in Recording.Read(session).Where(p => p.Channel == "util" && p.IsIn && p.Type == 322 && p.Body != null))
                foreach (PegasusUtil.AssetResponse r in PegasusPackets.Deserialize<PegasusUtil.GetAssetResponse>(p.Body).Responses)
                    if (r.SubsetCardListAsset is { } subset && wanted.Contains(subset.SubsetId) && !subsets.ContainsKey(subset.SubsetId))
                        subsets[subset.SubsetId] = subset.CardIds.ToHashSet();
        }
    }

    /// <summary>The recorded subset lists (id → card dbf ids) known.</summary>
    public IReadOnlyDictionary<int, HashSet<int>> Subsets => subsets;

    /// <summary>
    /// The sideboard entries of a deck that fit, in order: the owner is a sideboard card in the deck, at most its
    /// MAX_SIDEBOARD_CARDS, each card in its ALLOWED_SIDEBOARD_SUBSET (when that list is known), at most 2 functional and 1
    /// cosmetic Zilliax module. <paramref name="dropped"/> hears about every entry left out.
    /// </summary>
    public List<(int Owner, int Card)> Valid(IEnumerable<int> deck, IEnumerable<(int Owner, int Card)> sideboard, Action<string> dropped = null)
    {
        var inDeck = deck.ToHashSet();
        var kept = new List<(int Owner, int Card)>();
        foreach ((int owner, int card) in sideboard)
        {
            CardDef o = cards.ByDbf(owner), c = cards.ByDbf(card);
            string why =
                o == null || c == null ? "unknown card" :
                o.Tag(GAME_TAG.SIDEBOARD_TYPE) == 0 ? $"{o.CardId} has no sideboard" :
                !inDeck.Contains(owner) ? $"{o.CardId} isn't in the deck" :
                kept.Count(k => k.Owner == owner) >= o.Tag(GAME_TAG.MAX_SIDEBOARD_CARDS) ? $"{o.CardId}'s sideboard is full ({o.Tag(GAME_TAG.MAX_SIDEBOARD_CARDS)})" :
                Subset(o) is { } allowed && !allowed.Contains(card) ? $"{c.CardId} isn't allowed in {o.CardId}'s sideboard (subset {o.Tag(GAME_TAG.ALLOWED_SIDEBOARD_SUBSET)})" :
                c.Tag(Functional) != 0 && kept.Count(k => k.Owner == owner && cards.ByDbf(k.Card)?.Tag(Functional) != 0) >= 2 ? "a third functional module" :
                c.Tag(Cosmetic) != 0 && kept.Any(k => k.Owner == owner && cards.ByDbf(k.Card)?.Tag(Cosmetic) != 0) ? "a second cosmetic module" :
                null;
            if (why == null) kept.Add((owner, card));
            else dropped?.Invoke($"sideboard card {c?.CardId ?? card.ToString()} of {o?.CardId ?? owner.ToString()} left out: {why}");
        }
        return kept;
    }

    /// <summary>
    /// A random full sideboard for every sideboard card in a deck (soak and test decks): MAX_SIDEBOARD_CARDS different cards
    /// from its subset (else every collectible card), of <paramref name="heroClass"/> or Neutral unless the owner ignores the
    /// class rule (SIDEBOARD_IGNORES_CLASS_RULE); Zilliax: 2 functional modules and 1 cosmetic. No other sideboard card.
    /// </summary>
    public List<(int Owner, int Card)> RandomFill(IEnumerable<int> deck, int heroClass, Random rng)
    {
        var fill = new List<(int Owner, int Card)>();
        foreach (int owner in deck.Distinct())
        {
            CardDef o = cards.ByDbf(owner);
            if (o == null || o.Tag(GAME_TAG.SIDEBOARD_TYPE) == 0) continue;
            bool anyClass = o.Tag(GAME_TAG.SIDEBOARD_IGNORES_CLASS_RULE) != 0;
            HashSet<int> allowed = Subset(o);
            var pool = (allowed != null ? allowed.Select(cards.ByDbf).Where(c => c != null) : cards.All.Where(c => c.Tag(GAME_TAG.COLLECTIBLE) == 1))
                .Where(c => c.Type != TAG_CARDTYPE.HERO && c.Tag(GAME_TAG.SIDEBOARD_TYPE) == 0 && c.DbfId != owner)
                .Where(c => c.Tag(Functional) != 0 || c.Tag(Cosmetic) != 0 || anyClass || c.Tag(GAME_TAG.CLASS) == heroClass || c.Tag(GAME_TAG.CLASS) == Neutral)
                .OrderBy(c => c.DbfId).ToList();
            IEnumerable<CardDef> Pick(IEnumerable<CardDef> from, int n) => from.OrderBy(_ => rng.Next()).Take(n);
            var picks = pool.Any(c => c.Tag(Functional) != 0)
                ? Pick(pool.Where(c => c.Tag(Functional) != 0), 2).Concat(Pick(pool.Where(c => c.Tag(Cosmetic) != 0), 1))
                : Pick(pool, o.Tag(GAME_TAG.MAX_SIDEBOARD_CARDS));
            fill.AddRange(picks.Select(c => (owner, c.DbfId)));
        }
        return fill;
    }

    private HashSet<int> Subset(CardDef owner) =>
        owner.Tag(GAME_TAG.ALLOWED_SIDEBOARD_SUBSET) is int id && id != 0 && subsets.TryGetValue(id, out var list) ? list : null;
}
