using HsServer.Engine;
using HsServer.Lobby;
using PegasusUtil;

namespace HsServer.Tests;

/// <summary>Card packs (roadmap C2), against RESEARCH/hearthstone-rules.md §F.</summary>
public class PackTests
{
    private static readonly string DbfDir = CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
    private static readonly DateTime Today = new(2026, 10, 1, 15, 0, 0, DateTimeKind.Utc);

    private static readonly Lazy<(CardDb Cards, CraftingRules Crafting, BoosterRules Boosters)> Data = new(() =>
    {
        var cards = CardDb.Load(DbfDir);
        var calendar = new EventCalendar(Path.Combine(DbfDir, "EventMap.json"), EventCalendar.FromRecording(RecordedAccount.FindReferenceSession(
            Path.Combine(TestEnvironment.ProjectRoot, "Hearthstone main game", "BepInEx", "HsRecorder"),
            Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures")))) { Clock = () => Today };
        var crafting = new CraftingRules(DbfDir, cards, calendar);
        return (cards, crafting, new BoosterRules(DbfDir, cards, crafting, calendar));
    });

    private const int Standard = 713, GoldenStandard = 716;

    private static int Rarity(int dbf) => Data.Value.Cards.ByDbf(dbf).Tag(GAME_TAG.RARITY) is 2 ? 1 : Data.Value.Cards.ByDbf(dbf).Tag(GAME_TAG.RARITY);

    [Fact]
    public void The_flat_chance_with_its_pity_timer_averages_blizzards_rate()
    {
        foreach (var rate in BoosterRules.NormalRates)
        {
            // mean wait for one, pity included = 1 / average
            double wait = (1 - Math.Pow(1 - rate.Base, rate.Pity)) / rate.Base;
            Assert.InRange(wait, 1 / rate.Average * 0.999, 1 / rate.Average * 1.001);
        }
    }

    [Fact]
    public void Standard_packs_follow_the_published_rules()
    {
        var (_, _, boosters) = Data.Value;
        var pack = boosters.Get(Standard);
        Assert.True(boosters.CanOpen(pack));
        var pool = boosters.Pool(pack);
        Assert.True(pool.Count > 300);
        var pity = new BoosterRules.PityState();
        var rng = new Random(7);
        var collected = new Dictionary<int, int>();
        int packs = 20000, legendaryPacks = 0, epicPacks = 0, sinceLegendary = 0, sinceEpic = 0, firstLegendary = -1;
        for (int i = 0; i < packs; i++)
        {
            var cards = boosters.Roll(pack, pool, pity, d => collected.GetValueOrDefault(d), rng);
            foreach (var (dbf, _) in cards) collected[dbf] = collected.GetValueOrDefault(dbf) + 1;
            Assert.Equal(5, cards.Count);
            Assert.Contains(cards, c => Rarity(c.Dbf) >= (int)TAG_RARITY.RARE);                 // one Rare or better
            Assert.All(cards.GroupBy(c => c.Dbf), g => Assert.True(g.Count() <= (Rarity(g.Key) == 5 ? 1 : 2))); // per pack
            bool leg = cards.Any(c => Rarity(c.Dbf) == 5 && c.Premium == 0), epic = cards.Any(c => Rarity(c.Dbf) == 4 && c.Premium == 0);
            if (leg) { legendaryPacks++; sinceLegendary = 0; if (firstLegendary < 0) firstLegendary = i; } else Assert.True(++sinceLegendary < 40);
            if (epic) { epicPacks++; sinceEpic = 0; } else Assert.True(++sinceEpic < 10);
        }
        Assert.InRange(firstLegendary, 0, 9);                                                     // one within the first 10
        Assert.InRange(legendaryPacks / (double)packs, 0.045, 0.055);                             // 5.0%
        Assert.InRange(epicPacks / (double)packs, 0.195, 0.225);                                  // 21.0%
    }

    [Fact]
    public void Duplicate_protection_gives_new_legendaries_until_the_set_is_complete()
    {
        var (_, _, boosters) = Data.Value;
        var pack = boosters.Get(boosters.Find("The Lost City")!.Value);
        var pool = boosters.Pool(pack);
        int legendaries = pool.Count(c => c.Tag(GAME_TAG.RARITY) == 5);
        Assert.True(legendaries > 5);
        var collected = new Dictionary<int, int>();
        var seen = new List<int>();
        var pity = new BoosterRules.PityState();
        var rng = new Random(3);
        while (seen.Count < legendaries)
            foreach (var (dbf, _) in boosters.Roll(pack, pool, pity, d => collected.GetValueOrDefault(d), rng))
            {
                if (Rarity(dbf) == 5) seen.Add(dbf);
                collected[dbf] = collected.GetValueOrDefault(dbf) + 1;
            }
        Assert.Equal(legendaries, seen.Distinct().Count());                                       // no repeat before all of them
    }

    [Fact]
    public void Golden_packs_are_all_golden()
    {
        var (_, _, boosters) = Data.Value;
        var pack = boosters.Get(GoldenStandard);
        Assert.True(pack.Golden && boosters.CanOpen(pack));
        var pool = boosters.Pool(pack);
        var rng = new Random(1);
        var pity = new BoosterRules.PityState();
        for (int i = 0; i < 500; i++)
            Assert.All(boosters.Roll(pack, pool, pity, _ => 0, rng), c => Assert.NotEqual(0, c.Premium));
    }

    [Fact]
    public void Opening_packs_answers_like_live_and_spends_them()
    {
        var (_, _, boosters) = Data.Value;
        var store = new CollectionStore(null);
        store.Seed(new Collection { Stacks = new List<PegasusShared.CardStack>(), CollectionVersion = 100 }, new GameCurrencyStates());
        // the recorded login's packs are adopted once
        var login = new Boosters { List = { new PegasusShared.BoosterInfo { Type = Standard, Count = 3, EverGrantedCount = 159 } } };
        store.ApplyTo(login);
        Assert.Equal(3, store.PackCount(Standard));
        var later = new Boosters { List = { new PegasusShared.BoosterInfo { Type = Standard, Count = 50, EverGrantedCount = 999 } } };
        store.ApplyTo(later);
        Assert.Equal(3, Assert.Single(later.List).Count);                                         // the player's own afterwards

        var (reply, change, error) = store.OpenPacks(Standard, 2, boosters, new Random(5));
        Assert.Null(error);
        Assert.Equal(10, reply.List.Count);
        Assert.Equal(2, reply.PacksOpened);
        Assert.True(reply.IsMpo);
        Assert.Equal(store.CollectionVersion, reply.CollectionVersion);
        Assert.Equal(store.CollectionVersion, change.CollectionModifications.CollectionVersion);
        Assert.Equal(10, change.CollectionModifications.CardModifications.Sum(m => m.Quantity));
        var mod = Assert.Single(change.BoosterModifications.Modifications);
        Assert.Equal((Standard, -2), (mod.Type, mod.Count));
        Assert.Equal(1, store.PackCount(Standard));
        foreach (var c in reply.List) Assert.True(store.Count(c.CardDef.Asset, c.CardDef.Premium) >= 1);

        Assert.NotNull(store.OpenPacks(Standard, 2, boosters, new Random(5)).Error);             // only 1 left
        Assert.NotNull(store.OpenPacks(Standard, 0, boosters, new Random(5)).Error);
        Assert.Equal(1, store.PackCount(Standard));
    }

    [Fact]
    public void Class_packs_hold_that_class_from_standard_and_year_packs_that_years_expansions()
    {
        var (cards, crafting, boosters) = Data.Value;
        var hunter = boosters.Get(boosters.Find("Hunter Pack")!.Value);
        Assert.True(boosters.CanOpen(hunter));
        var hunterPool = boosters.Pool(hunter);
        var standardPool = boosters.Pool(boosters.Get(Standard)).Select(c => c.DbfId).ToHashSet();
        Assert.True(hunterPool.Count > 40);
        Assert.All(hunterPool, c => Assert.Equal((int)TAG_CLASS.HUNTER, c.Tag(GAME_TAG.CLASS)));     // class cards only
        Assert.All(hunterPool, c => Assert.Contains(c.DbfId, standardPool));                           // from Standard
        var dk = boosters.Pool(boosters.Get(boosters.Find("Death Knight Pack")!.Value));
        Assert.All(dk, c => Assert.Equal((int)TAG_CLASS.DEATHKNIGHT, c.Tag(GAME_TAG.CLASS)));

        int Set(string pack) => crafting.SetOf(boosters.Pool(boosters.Get(boosters.Find(pack)!.Value)).First().DbfId);
        var phoenix = boosters.Pool(boosters.Get(boosters.Find("Year of the Phoenix")!.Value)).Select(c => crafting.SetOf(c.DbfId)).ToHashSet();
        Assert.Equal(new[] { Set("Ashes of Outland"), Set("Scholomance Academy"), Set("Madness at the Darkmoon Faire") }.OrderBy(x => x), phoenix.OrderBy(x => x));
        var dragon = boosters.Pool(boosters.Get(boosters.Find("Year of the Dragon")!.Value)).Select(c => crafting.SetOf(c.DbfId)).ToHashSet();
        Assert.Equal(3, dragon.Count);                                                                // RoS, SoU, DoD — not Classic
        Assert.DoesNotContain(Set("Classic"), dragon);
        var goldenPegasus = boosters.Get(boosters.Find("Golden Year of the Pegasus")!.Value);
        Assert.True(goldenPegasus.Golden && boosters.CanOpen(goldenPegasus));
        Assert.Equal(3, boosters.Pool(goldenPegasus).Select(c => crafting.SetOf(c.DbfId)).Distinct().Count());
    }

    private static int SetOfPack(string name)
    {
        var (_, crafting, boosters) = Data.Value;
        return crafting.SetOf(boosters.Pool(boosters.Get(boosters.Find(name)!.Value)).First().DbfId);
    }

    [Fact]
    public void Catch_up_packs_hold_the_standard_expansions_before_their_launch()
    {
        var (_, _, boosters) = Data.Value;
        int[] Sets(int type) => boosters.Get(type).CatchupSets.ToArray();
        // the wiki's examples
        Assert.Equal(new[] { "Voyage to the Sunken City", "Murder at Castle Nathria", "March of the Lich King", "Festival of Legends", "TITANS" }
            .Select(SetOfPack), Sets(944));                                                          // Badlands Catch-Up
        Assert.Equal(new[] { "Festival of Legends", "TITANS", "Showdown in the Badlands" }.Select(SetOfPack), Sets(971)); // Whizbang
        Assert.Equal(new[] { "Festival of Legends", "TITANS", "Showdown in the Badlands", "Whizbang's Workshop" }.Select(SetOfPack),
            Sets(978));                                                                               // Perils in Paradise's
        Assert.Equal(new[] { "Whizbang's Workshop", "Perils in Paradise", "The Great Dark Beyond" }.Select(SetOfPack), Sets(1033)); // Emerald Dream
        Assert.True(boosters.CanOpen(boosters.Get(944)));
    }

    [Fact]
    public void Catch_up_packs_give_more_cards_from_sets_you_lack()
    {
        var (_, crafting, boosters) = Data.Value;
        var pack = boosters.Get(971); // 3 slots
        var pool = boosters.Pool(pack);
        var rng = new Random(11);
        var fresh = boosters.RollCatchup(pack, pool, new BoosterRules.PityState(), _ => 0, _ => 0.0, rng);
        Assert.Equal(50, fresh.Count);                                                                // 17 x 3 = 51 → 50
        Assert.All(fresh, c => Assert.Equal(0, c.Premium));                                          // regular only
        Assert.True(fresh.Count(c => Rarity(c.Dbf) >= (int)TAG_RARITY.RARE) >= 10);                   // 20% Rare or better
        Assert.All(pack.CatchupSets, set => Assert.InRange(fresh.Count(c => crafting.SetOf(c.Dbf) == set), 16, 17));
        var nearlyDone = boosters.RollCatchup(pack, pool, new BoosterRules.PityState(), _ => 0, _ => 0.9, rng);
        Assert.Equal(5, nearlyDone.Count);                                                            // 1 x 3 = 3 → 5
        Assert.All(pack.CatchupSets, set => Assert.Contains(nearlyDone, c => crafting.SetOf(c.Dbf) == set));

        // per-card pity: a Legendary within 50 cards the first time, then never more than 163 cards apart
        var pity = new BoosterRules.PityState();
        int since = 0, first = -1, n = 0;
        for (int i = 0; i < 300; i++)
            foreach (var c in boosters.RollCatchup(pack, pool, pity, _ => 0, _ => 0.5, rng))
            {
                if (Rarity(c.Dbf) == 5) { if (first < 0) first = n; since = 0; } else Assert.True(++since < 163);
                n++;
            }
        Assert.InRange(first, 0, 49);
    }

    [Fact]
    public void Opening_a_catch_up_pack_sends_the_collection_stats()
    {
        var (_, _, boosters) = Data.Value;
        var store = new CollectionStore(null);
        store.Seed(new Collection { Stacks = new List<PegasusShared.CardStack>(), CollectionVersion = 1 }, new GameCurrencyStates());
        store.SetPacks(944, 1);
        var (reply, change, error) = store.OpenPacks(944, 1, boosters, new Random(2));
        Assert.Null(error);
        Assert.True(reply.IsCatchupPack);
        Assert.Equal(50, reply.List.Count);
        Assert.Equal(5, reply.ExpansionCollectionStats.Count);
        Assert.Equal(50, reply.ExpansionCollectionStats.Sum(s => (int)s.CardsInCollection));         // all new copies
        Assert.All(reply.ExpansionCollectionStats, s => Assert.True(s.CardsInExpansion > s.CardsInCollection));
        Assert.Equal((944, -1), (change.BoosterModifications.Modifications[0].Type, change.BoosterModifications.Modifications[0].Count));
    }

    [Fact]
    public void Playable_hero_cards_can_be_crafted_hero_skins_cannot()
    {
        var (cards, crafting, _) = Data.Value;
        var heroes = cards.All.Where(c => c.Type == TAG_CARDTYPE.HERO && c.Tag(GAME_TAG.COLLECTIBLE) == 1).ToList();
        Assert.Contains(heroes, c => c.CardId == "AV_200" && crafting.Value(c.DbfId, 0) != null);    // Magister Dawngrasp
        Assert.True(heroes.Count(c => crafting.Value(c.DbfId, 0) == null) > 700);                    // the skins
    }
}
