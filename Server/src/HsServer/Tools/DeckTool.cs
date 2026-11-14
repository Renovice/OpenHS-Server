using HsServer.Lobby;
using HsServer.Sabber;
using PegasusShared;
using PegasusUtil;
using SabberStoneCore.Model;

namespace HsServer.Tools;

/// <summary>
/// `HsServer deck list` / `HsServer deck remove &lt;id&gt;` / `HsServer deck add &lt;name&gt; &lt;heroCardId&gt; [cardId[:copies]]... [sb:ownerId=cardId,...]... [seed=N]`
/// / `HsServer deck sideboard &lt;id&gt; &lt;ownerCardId&gt; [cardId...]`: the local server's
/// decks (Server\state\accounts\<player>\decks.bin; `player=<name>`, default "default"). `add` puts the listed cards first, then fills to 30 with random current Core cards of
/// the hero's class + neutral (at most 2 copies, 1 of a legendary). `sb:` gives a card in the deck its sideboard (E.T.C.'s band,
/// Zilliax's two modules + look, Beatrix's minion, King of the Underbelly's Beasts); `sideboard` replaces one card's sideboard
/// in a deck (no cards: empties it). Restart the server so the client gets the new list.
/// </summary>
internal static class DeckTool
{
    public static int Run(string[] args)
    {
        string root = Program.ProjectRoot();
        // B1: a player's decks (Server/state/accounts/<name>/decks.bin); `player=<name>` picks one, default = "default".
        // Edit decks while the server is stopped: a running server keeps its own copy and would overwrite the file.
        string player = args.FirstOrDefault(a => a.StartsWith("player=", StringComparison.Ordinal))?["player=".Length..] ?? Lobby.AccountStore.DefaultName;
        args = args.Where(a => !a.StartsWith("player=", StringComparison.Ordinal)).ToArray();
        string path = Path.Combine(root, "Server", "state", "accounts", player.ToLowerInvariant(), "decks.bin");
        var store = new DeckStore(path);
        if (!store.Load())
        {
            Console.Error.WriteLine($"no deck state yet for player {player} ({Path.GetRelativePath(root, path)}): log in as them on the local server once first");
            return 1;
        }
        string dir = Engine.CardDb.FindDefaultDir(root);
        SabberEngineGame.EnsureCards(dir);
        var db = Engine.CardDb.Load(dir);

        if (args.Length >= 1 && args[0] == "list")
        {
            var state = new SecondaryClientState();
            store.ApplyTo(state);
            foreach (DeckInfo d in state.Decks)
            {
                Console.WriteLine($"{d.Id}  {d.Name}  hero={db.ByDbf(d.Hero)?.CardId}  cards={store.CardIds(d.Id).Count}");
                foreach (var owner in store.Sideboard(d.Id).GroupBy(s => s.Owner))
                    Console.WriteLine($"    sideboard {db.ByDbf(owner.Key)?.CardId ?? owner.Key.ToString()}: {string.Join(", ", owner.Select(s => db.ByDbf(s.Card)?.CardId ?? s.Card.ToString()))}");
            }
            return 0;
        }
        if (args.Length >= 3 && args[0] == "sideboard")
        {
            long deck = long.Parse(args[1]);
            if (store.Get(deck) == null) { Console.Error.WriteLine($"no deck {deck}"); return 1; }
            if (!db.TryGet(args[2], out var owner)) { Console.Error.WriteLine($"unknown card {args[2]}"); return 1; }
            var ids = args.Skip(3).ToList();
            string unknown = ids.FirstOrDefault(id => !db.TryGet(id, out _));
            if (unknown != null) { Console.Error.WriteLine($"unknown card {unknown}"); return 1; }
            // the client's way: the old entries at 0, the new ones with their counts
            var changes = store.SideboardCards(deck).Where(s => s.LinkedCardDbId == owner.DbfId)
                .Select(s => new SideBoardCardData { Def = new PegasusShared.CardDef { Asset = s.Def.Asset, Premium = s.Def.Premium }, LinkedCardDbId = owner.DbfId, Qty = 0 })
                .Concat(SideboardChanges(db, owner.DbfId, ids)).ToList();
            store.SetData(new DeckSetData { Deck = deck, Cards = new List<DeckCardData>(), SideboardCards = changes });
            Console.WriteLine($"deck {deck}: {args[2]}'s sideboard = [{string.Join(", ", ids)}]");
            return 0;
        }
        if (args.Length == 2 && args[0] == "remove")
        {
            DeckDeleted deleted = store.Delete(new DeleteDeck { Deck = long.Parse(args[1]) });
            Console.WriteLine($"removed deck {deleted.Deck}");
            return 0;
        }
        if (args.Length < 3 || args[0] != "add")
        {
            Console.Error.WriteLine("usage: HsServer deck list | deck remove <id> | deck add <name> <heroCardId> [cardId[:copies]]... [sb:ownerId=cardId,...]... [seed=N]"
                                    + " | deck sideboard <id> <ownerCardId> [cardId...]");
            return 1;
        }

        string name = args[1], hero = args[2];
        int seed = args.FirstOrDefault(a => a.StartsWith("seed=")) is { } s ? int.Parse(s[5..]) : Environment.TickCount;
        var cards = new List<string>();
        // sb:<owner>=<card>,<card>...: the owner card's sideboard (the owner is put in the deck too)
        var sideboards = new List<(string Owner, List<string> Cards)>();
        foreach (string a in args.Skip(3).Where(a => a.StartsWith("sb:")))
        {
            string[] parts = a[3..].Split('=', 2);
            sideboards.Add((parts[0], parts.Length > 1 ? parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() : new List<string>()));
            if (!cards.Contains(parts[0])) cards.Add(parts[0]);
        }
        string bad = sideboards.SelectMany(s => s.Cards.Prepend(s.Owner)).FirstOrDefault(id => !db.TryGet(id, out _));
        if (bad != null) { Console.Error.WriteLine($"unknown card {bad}"); return 1; }
        foreach (string a in args.Skip(3).Where(a => !a.StartsWith("seed=") && !a.StartsWith("sb:")))
        {
            string[] parts = a.Split(':');
            int copies = parts.Length > 1 ? int.Parse(parts[1]) : 1;
            for (int i = 0; i < copies; i++) cards.Add(parts[0]);
        }
        var cardClass = Cards.FromId(hero).Class;
        foreach (string id in RandomDecks.BuildCurrentCore(cardClass, new Random(seed)))
        {
            if (cards.Count >= 30) break;
            Card c = Cards.FromId(id);
            if (cards.Count(x => x == id) < (c.Rarity == SabberStoneCore.Enums.Rarity.LEGENDARY ? 1 : 2)) cards.Add(id);
        }

        DeckCreated created = store.Create(new CreateDeck { Name = name, Hero = db[hero].DbfId, DeckType = DeckType.NORMAL_DECK, FormatType = FormatType.FT_WILD });
        long deckId = created.Info.Id;
        store.SetData(new DeckSetData
        {
            Deck = deckId,
            Cards = cards.GroupBy(id => id).Select(g => new DeckCardData
            {
                Def = new PegasusShared.CardDef { Asset = db[g.Key].DbfId, Premium = 0 },
                Qty = g.Count(),
            }).ToList(),
            SideboardCards = sideboards.SelectMany(s => SideboardChanges(db, db[s.Owner].DbfId, s.Cards)).ToList(),
        });
        Console.WriteLine($"added deck {deckId} \"{name}\" ({hero}, {cards.Count} cards):");
        foreach (var g in cards.GroupBy(id => id)) Console.WriteLine($"  {g.Count()}x {Cards.FromId(g.Key).Name} ({g.Key})");
        foreach (var (owner, picks) in sideboards) Console.WriteLine($"  sideboard {owner}: {string.Join(", ", picks)}");
        return 0;
    }

    /// <summary>Sideboard cards as the client sends them in DeckSetData: one entry per card with its count.</summary>
    private static IEnumerable<SideBoardCardData> SideboardChanges(Engine.CardDb db, int owner, IEnumerable<string> cardIds) =>
        cardIds.GroupBy(id => id).Select(g => new SideBoardCardData
        {
            Def = new PegasusShared.CardDef { Asset = db[g.Key].DbfId, Premium = 0 }, LinkedCardDbId = owner, Qty = g.Count(),
        });
}
