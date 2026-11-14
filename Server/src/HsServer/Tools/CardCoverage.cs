using SabberStoneCore.Enums;
using SabberStoneCore.Model;

namespace HsServer.Tools;

/// <summary>
/// `HsServer cards [--missing] [SET...]`: how much of each card set the rules engine implements (collectible cards,
/// per the engine's own Implemented flag). Default sets: CORE (Basic) and EXPERT1 (Classic). --missing lists the
/// collectible cards without an implementation. `cards --ids &lt;file&gt;` (2026-10-09): for each card id in the file (one per line),
/// "id 	 known|unknown 	 implemented|missing 	 type 	 name 	 text" (any card, collectible or not).
/// `cards --tokens [--all]` (2026-10-09): the non-collectible cards other cards use and whether they work (see Tokens).
/// </summary>
internal static class CardCoverage
{
    public static int Run(string[] args)
    {
        bool missing = args.Contains("--missing");
        Sabber.SabberEngineGame.EnsureCards(Engine.CardDb.FindDefaultDir(Program.ProjectRoot()));
        int idsAt = Array.IndexOf(args, "--ids");
        if (idsAt >= 0 && idsAt + 1 < args.Length)
        {
            foreach (string line in File.ReadLines(args[idsAt + 1]))
            {
                string id = line.Trim();
                if (id.Length == 0 || id[0] == '#') continue;
                Card? c = Cards.TryFromId(id);
                Console.WriteLine(c == null ? $"{id}	unknown" : $"{id}	known	{(IsImplemented(c) ? "implemented" : "missing")}	{c.Type}	{c.Name}	{Plain(c.Text)}");
            }
            return 0;
        }
        if (args.Contains("--tokens"))
            return Tokens(args.Contains("--all"));
        var reprints = SabberStoneCore.Loader.ReprintCards.CanonicalOf; // forces card loading
        _ = Cards.All.Count();
        foreach (var group in reprints.Keys.GroupBy(id => id[..(id.IndexOf('_') + 1)]))
        {
            var playable = group.Select(Cards.FromId).Where(c => c != null && c.Collectible).ToList();
            Console.WriteLine($"reprints {group.Key,-6} {group.Count(),4} added from the client DBF; collectible {playable.Count(IsImplemented)} / {playable.Count} implemented");
            if (missing)
                foreach (Card c in playable.Where(c => !IsImplemented(c)).OrderBy(c => c.Id))
                    Console.WriteLine($"    missing: {c.Id,-16} {c.Name} ({c.Class}, {c.Cost} mana): {Plain(c.Text)}");
        }
        Console.WriteLine($"reprints with changed text (need their own script): {SabberStoneCore.Loader.ReprintCards.Reworded.Count}");

        // The current Core set (client CardSetTiming, see ClientCardSets) and the cards of it the engine doesn't know at
        // all (no 2021 original to map to).
        string dbfDir = Engine.CardDb.FindDefaultDir(Program.ProjectRoot());
        var clientCards = Engine.CardDb.Load(dbfDir);
        var cardSets = Engine.ClientCardSets.Load(dbfDir);
        var currentCore = clientCards.All
            .Where(c => c.Tag(Engine.GAME_TAG.COLLECTIBLE) == 1 && c.Type != Engine.TAG_CARDTYPE.HERO && cardSets.IsCurrentCore(c.DbfId))
            .ToList();
        var unknownCore = currentCore.Where(c => Cards.TryFromId(c.CardId) == null).OrderBy(c => c.CardId).ToList();
        int implemented = currentCore.Count(c => Cards.TryFromId(c.CardId) is { } k && IsImplemented(k));
        Console.WriteLine($"current Core cards of a type the engine can't create (Locations): {SabberStoneCore.Loader.ReprintCards.Unsupported.Count}");
        Console.WriteLine($"current Core set: {currentCore.Count} collectible cards; engine knows {currentCore.Count - unknownCore.Count}, implemented {implemented}; unknown {unknownCore.Count}");
        if (missing)
            foreach (var c in currentCore.Where(c => Cards.TryFromId(c.CardId) is { } k && !IsImplemented(k)).OrderBy(c => (Engine.TAG_CLASS)c.Tag(Engine.GAME_TAG.CLASS)).ThenBy(c => c.Cost))
                Console.WriteLine($"    to script: {c.CardId,-18} {c.Name} ({(Engine.TAG_CLASS)c.Tag(Engine.GAME_TAG.CLASS)}, {c.Cost}): {Plain(Cards.FromId(c.CardId).Text)}");
        if (missing)
            foreach (var c in unknownCore)
                Console.WriteLine($"    unknown: {c.CardId,-18} {c.Name} ({(Engine.TAG_CLASS)c.Tag(Engine.GAME_TAG.CLASS)}, {c.Cost} mana)");
        if (missing)
            foreach (string id in SabberStoneCore.Loader.ReprintCards.Reworded.OrderBy(i => i))
                Console.WriteLine($"    reworded: {id}");
        // The pool random and Discover effects draw from (games run as Wild): every collectible card the engine knows. An
        // unscripted card here can be generated and then does nothing — the rule is to script it, never to filter it out.
        var pool = Cards.FormatTypeCollectible(FormatType.FT_WILD).ToList(); // every card, also the never-generated ones
        var unscripted = pool.Where(c => !IsImplemented(c)).OrderBy(c => c.Set).ThenBy(c => c.Id).ToList();
        Console.WriteLine($"random/Discover pool (Wild): {pool.Count} collectible cards, {unscripted.Count} without a script (--pool lists them)");
        if (args.Contains("--pool"))
            foreach (Card c in unscripted)
                Console.WriteLine($"    unscripted: {c.Id,-18} {c.Name} ({c.Set}, {c.Class}, {c.Cost}): {Plain(c.Text)}");

        var sets = args.Where(a => !a.StartsWith("--")).Select(a => Enum.Parse<CardSet>(a, ignoreCase: true)).ToList();
        if (sets.Count == 0) sets = new List<CardSet> { CardSet.CORE, CardSet.EXPERT1 };

        // Scripts older than the card: Blizzard changed the card after SabberStone's data (texts differ).
        var stale = SabberStoneCore.Loader.ReprintCards.Stale
            .Select(Cards.TryFromId)
            .Where(c => c != null && c.Collectible && (sets.Contains(c.Set) || SabberStoneCore.Loader.ReprintCards.CanonicalOf.ContainsKey(c.Id)))
            .OrderBy(c => c.Id).ToList();
        Console.WriteLine($"stale scripts (client text differs from the script's): {stale.Count}");
        if (args.Contains("--stale"))
            foreach (Card c in stale)
            {
                string original = SabberStoneCore.Loader.ReprintCards.Canonical(c.Id);
                Console.WriteLine($"    {c.Id,-14} {c.Name}: now \"{Plain(SabberStoneCore.Loader.ReprintCards.ClientText(c.Id))}\" | script: \"{Plain(Cards.TryFromId(original)?.Text)}\"");
            }

        foreach (CardSet set in sets)
        {
            var cards = Cards.All.Where(c => c.Set == set && c.Collectible && c.Type != CardType.HERO).OrderBy(c => c.Class).ThenBy(c => c.Cost).ToList();
            int done = cards.Count(IsImplemented);
            Console.WriteLine($"{set,-12} {done,4} / {cards.Count,-4} collectible cards implemented ({(cards.Count == 0 ? 0 : 100.0 * done / cards.Count):0.0}%)");
            foreach (var group in cards.GroupBy(c => c.Class))
                Console.WriteLine($"    {group.Key,-12} {group.Count(IsImplemented),3} / {group.Count()}");
            if (missing)
                foreach (Card c in cards.Where(c => !IsImplemented(c)))
                    Console.WriteLine($"    missing: {c.Id,-12} {c.Name} ({c.Class}, {c.Cost} mana)");
        }
        return 0;
    }

    /// <summary>
    /// `cards --tokens [--all]` (token pass 2026-10-09): the non-collectible playable cards (minions, spells, weapons, hero powers,
    /// Locations, heroes) other cards use, and whether each works: the counts, then the ones without a script (--all: every one).
    /// A card is used when a card script names it (a string literal outside comments in Server/src/HsServer.Rules/src/CardSets,
    /// Adventure and TavernBrawl left out, its own `cards.Add("…"` not counted), the client's RelatedCards table relates it to a
    /// collectible card, or a collectible card names it as its Entourage or its COLLECTION_RELATED_CARD_DATABASE_ID (1452).
    /// "Works" is the engine's Implemented: a script, keyword-only text (ReprintCards.IsKeywordOnly), a shared script of the same
    /// card, a Starship, or a card marked as shown only / built by a mechanic (Power.NoScript).
    /// </summary>
    private static int Tokens(bool all)
    {
        _ = Cards.All.Count();
        var usedBy = new Dictionary<string, string>(StringComparer.Ordinal);
        void Use(string id, string by) { if (!usedBy.ContainsKey(id)) usedBy[id] = by; }

        string cardSets = Path.Combine(Program.ProjectRoot(), "Server", "src", "HsServer.Rules", "src", "CardSets");
        int scanned = 0;
        if (Directory.Exists(cardSets))
            foreach (string file in Directory.EnumerateFiles(cardSets, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                string rel = Path.GetRelativePath(cardSets, file).Replace('\\', '/');
                if (rel.StartsWith("Adventure/", StringComparison.Ordinal) || rel.StartsWith("TavernBrawl/", StringComparison.Ordinal)) continue;
                scanned++;
                int n = 0;
                foreach (string line in File.ReadLines(file))
                {
                    n++;
                    int comment = line.IndexOf("//", StringComparison.Ordinal);
                    string code = comment >= 0 ? line[..comment] : line;
                    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(code, "\"([A-Za-z0-9_]+)\""))
                        if (!code[..m.Index].TrimEnd().EndsWith("cards.Add(", StringComparison.Ordinal)) // its own definition
                            Use(m.Groups[1].Value, $"{Path.GetFileName(file)}:{n}");
                }
            }
        foreach (Card c in Cards.All.Where(c => c.Collectible).OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            foreach (string related in SabberStoneCore.Loader.ReprintCards.RelatedCardIds(c.Id) ?? Array.Empty<string>())
                Use(related, "related to " + c.Id);
            foreach (string e in c.Entourage ?? Array.Empty<string>())
                Use(e, "entourage of " + c.Id);
            if (c[(GameTag)1452] > 0 && Cards.FromAssetId(c[(GameTag)1452]) is Card linked)
                Use(linked.Id, "collection-related to " + c.Id);
        }

        var tokens = usedBy.Keys.Select(Cards.TryFromId)
            .Where(k => k != null && !k.Collectible && k.Type is CardType.MINION or CardType.SPELL or CardType.WEAPON or CardType.HERO_POWER
                or CardType.LOCATION or CardType.HERO)
            .GroupBy(k => k.Id).Select(g => g.First())
            .OrderBy(k => k.Id, StringComparer.Ordinal).ToList();
        var missing = tokens.Where(k => !IsImplemented(k)).ToList();
        Console.WriteLine($"tokens (non-collectible cards other cards use): {tokens.Count} — from {scanned} script files, RelatedCards, Entourage, 1452");
        Console.WriteLine($"    work: {tokens.Count - missing.Count}; without a script: {missing.Count}");
        foreach (var group in tokens.GroupBy(k => k.Type).OrderBy(g => g.Key))
            Console.WriteLine($"    {group.Key,-11} {group.Count(IsImplemented),4} / {group.Count()}");
        foreach (Card k in all ? tokens : missing)
            Console.WriteLine($"{k.Id}\t{(IsImplemented(k) ? "implemented" : "missing")}\t{k.Type}\t{k.Name}\t{usedBy[k.Id]}\t{Plain(k.Text)}");
        return 0;
    }

    private static string Plain(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text ?? "", @"<[^>]+>|\[x\]|\n|\\n", " ").Trim();

    // Choose One cards carry no effect themselves; the engine plays option card "<id>a" or "<id>b" instead.
    internal static bool IsImplemented(Card c)
    {
        if (c.Implemented) return true;
        if (!c.ChooseOne) return false;
        Card a = Option(c, 'a'), b = Option(c, 'b');
        return a != null && b != null && a.Implemented && b.Implemented;
    }

    private static Card Option(Card c, char option)
    {
        try { return Cards.ChooseOneOption(c, option); } catch { return null; }
    }
}
