using SabberStoneCore.Enums;
using SabberStoneCore.Model;

namespace HsServer.Sabber;

/// <summary>Random legal decks from given card sets (collectible, class + neutral, max 2 copies / 1 legendary).</summary>
internal static class RandomDecks
{
    public static readonly CardClass[] Classes =
    {
        CardClass.DRUID, CardClass.HUNTER, CardClass.MAGE, CardClass.PALADIN, CardClass.PRIEST,
        CardClass.ROGUE, CardClass.SHAMAN, CardClass.WARLOCK, CardClass.WARRIOR, CardClass.DEMONHUNTER, CardClass.DEATHKNIGHT,
    };

    /// <summary>Adds the card — a Fabled card with its bundle (wiki Fabled: "come bundled with additional non-collectible legendary cards
    /// that take up slots in the deck"; "A Fabled card cannot be added to a deck unless there are enough spots available"; the bundle:
    /// its related cards with client IS_FABLED_BUNDLE_CARD 4445). False when it doesn't fit.</summary>
    private static bool TryAdd(List<string> deck, Card c)
    {
        var bundle = (SabberStoneCore.Loader.ReprintCards.RelatedCardIds(c.Id) ?? new List<string>()).Select(Cards.TryFromId)
            .Where(x => x != null && x[(GameTag)4445] > 0).ToList();
        if (deck.Count + 1 + bundle.Count > 30) return false;
        deck.Add(c.Id);
        deck.AddRange(bundle.Select(x => x.Id));
        return true;
    }

    public static List<string> Build(CardClass cardClass, Random rng, params CardSet[] sets)
    {
        var pool = Cards.All.Where(c => c.Collectible && c.Type != CardType.HERO && sets.Contains(c.Set)
                                        && (c.HasClass(cardClass) || c.HasClass(CardClass.NEUTRAL))).ToList();
        var deck = new List<string>();
        while (deck.Count < 30 && pool.Count > 0)
        {
            Card c = pool[rng.Next(pool.Count)];
            int max = c.Rarity == Rarity.LEGENDARY ? 1 : 2;
            if (deck.Count(id => id == c.Id) < max && TryAdd(deck, c)) { }
            else pool.Remove(c);
        }
        return deck;
    }

    /// <summary>A random deck from the current Core set (only cards the engine can play), class + neutral.</summary>
    public static List<string> BuildCurrentCore(CardClass cardClass, Random rng)
    {
        var pool = SabberStoneCore.Loader.ReprintCards.ExtraCardIds
            .Select(Cards.TryFromId)
            .Where(c => c != null && c.Collectible && HsServer.Tools.CardCoverage.IsImplemented(c) && c.Type != CardType.HERO
                        && (c.HasClass(cardClass) || c.HasClass(CardClass.NEUTRAL)))
            .OrderBy(c => c.Id).ToList();
        var deck = new List<string>();
        while (deck.Count < 30 && pool.Count > 0)
        {
            Card c = pool[rng.Next(pool.Count)];
            int max = c.Rarity == Rarity.LEGENDARY ? 1 : 2;
            if (deck.Count(id => id == c.Id) < max && TryAdd(deck, c)) { }
            else pool.Remove(c);
        }
        return deck;
    }

    /// <summary>
    /// A random deck from everything the engine can play in Wild (every set, current Core and Legacy included), class +
    /// neutral, with playable hero cards (Galakrond, Death Knights) but not hero skins. Soak coverage for the whole pool.
    /// </summary>
    public static List<string> BuildWild(CardClass cardClass, Random rng)
    {
        var pool = Cards.All.Where(c => c.Collectible && c.Set != CardSet.HERO_SKINS && (c.Type != CardType.HERO || !c.Id.StartsWith("HERO_"))
                                        && (c.HasClass(cardClass) || c.HasClass(CardClass.NEUTRAL)))
            .OrderBy(c => c.Id).ToList();
        var deck = new List<string>();
        while (deck.Count < 30 && pool.Count > 0)
        {
            Card c = pool[rng.Next(pool.Count)];
            int max = c.Rarity == Rarity.LEGENDARY ? 1 : 2;
            if (deck.Count(id => id == c.Id) < max && TryAdd(deck, c)) { }
            else pool.Remove(c);
        }
        return deck;
    }

    /// <summary>The default hero or, half the time, a random hero skin of the class (skins have their own hero power cards).</summary>
    public static string AnyHero(CardClass cardClass, Random rng)
    {
        var skins = Cards.All.Where(c => c.Type == CardType.HERO && c.Set == CardSet.HERO_SKINS && c.Collectible && c.HasClass(cardClass)).ToList();
        return skins.Count > 0 && rng.Next(2) == 0 ? skins[rng.Next(skins.Count)].Id : Hero(cardClass);
    }

    /// <summary>The class's default hero card id (HERO_01 warrior … HERO_09 priest, HERO_10 demon hunter).</summary>
    public static string Hero(CardClass cardClass) => cardClass switch
    {
        CardClass.WARRIOR => "HERO_01", CardClass.SHAMAN => "HERO_02", CardClass.ROGUE => "HERO_03",
        CardClass.PALADIN => "HERO_04", CardClass.HUNTER => "HERO_05", CardClass.DRUID => "HERO_06",
        CardClass.WARLOCK => "HERO_07", CardClass.MAGE => "HERO_08", CardClass.DEMONHUNTER => "HERO_10", CardClass.DEATHKNIGHT => "HERO_11", _ => "HERO_09",
    };
}
