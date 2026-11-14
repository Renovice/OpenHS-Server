using HsServer.Lobby;
using PegasusUtil;

namespace HsServer.Tests;

/// <summary>Collection, dust and crafting (roadmap B2), against the client's own value table.</summary>
public class CollectionTests
{
    private static readonly string DbfDir = HsServer.Engine.CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
    private static readonly DateTime Today = new(2026, 10, 1, 15, 0, 0, DateTimeKind.Utc); // the reference recording's day

    private static readonly Lazy<List<EventCalendar.Entry>> Recorded = new(() => EventCalendar.FromRecording(RecordedAccount.FindReferenceSession(
        Path.Combine(TestEnvironment.ProjectRoot, "Hearthstone main game", "BepInEx", "HsRecorder"),
        Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures"))));

    /// <summary>The live calendar as recorded; "now" is whatever <paramref name="clock"/> says.</summary>
    private static EventCalendar Calendar(Func<DateTime> clock, string changes = null) =>
        new(Path.Combine(DbfDir, "EventMap.json"), Recorded.Value, changes) { Clock = clock };

    private static readonly Lazy<(CraftingRules Rules, HsServer.Engine.CardDb Cards)> Data = new(() =>
    {
        var cards = HsServer.Engine.CardDb.Load(DbfDir);
        return (new CraftingRules(DbfDir, cards, Calendar(() => Today)), cards);
    });

    // A collectible card of this rarity that can be crafted (a Core card can't: SPECIAL_EVENT_NEVER).
    private static int Craftable(HsServer.Engine.TAG_RARITY rarity) => Data.Value.Cards.All
        .First(c => c.Tag(HsServer.Engine.GAME_TAG.COLLECTIBLE) == 1 && c.Tag(HsServer.Engine.GAME_TAG.RARITY) == (int)rarity &&
                    c.Type == HsServer.Engine.TAG_CARDTYPE.MINION && Data.Value.Rules.Value(c.DbfId, 0) != null).DbfId;

    /// <summary>An event's DBF number in the current client data (the numbers change between client builds).</summary>
    internal static string EventNumber(string name)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(HsServer.Engine.CardDb.FindDefaultDir(TestEnvironment.ProjectRoot), "EventMap.json")));
        return doc.RootElement.GetProperty("records").EnumerateArray().First(r => r.GetProperty("Name").GetString() == name).GetProperty("ID").GetInt32().ToString();
    }

    private static CollectionStore Store(long dust)
    {
        var store = new CollectionStore(null);
        store.Seed(new Collection { Stacks = new List<PegasusShared.CardStack>(), CollectionVersion = 100 },
            new GameCurrencyStates { ArcaneDustBalance = dust, CurrencyVersion = 50 });
        return store;
    }

    private static CraftingTransaction Tx(int dbf, CollectionStore s, int dust, int create = 0, int disenchant = 0, int upgrade = 0) => new()
    {
        Def = new PegasusShared.CardDef { Asset = dbf, Premium = 0 }, ExpectedDustDelta = dust,
        ExpectedNormalOwnedCount = s.Count(dbf, 0), ExpectedGoldenOwnedCount = s.Count(dbf, 1),
        NormalCreateCount = create, NormalDisenchantCount = disenchant, GoldenUpgradeFromNormalCount = upgrade,
    };

    [Fact]
    public void Crafting_a_common_costs_40_dust_and_sends_the_change()
    {
        int common = Craftable(HsServer.Engine.TAG_RARITY.COMMON);
        var store = Store(100);
        var (reply, change) = store.Craft(Tx(common, store, 40, create: 1), Data.Value.Rules);
        Assert.Equal(BoughtSoldCard.Result.BOUGHT, reply.Result_);
        Assert.Equal(1, store.Count(common, 0));
        Assert.Equal(60, store.Dust);
        Assert.NotNull(change);
        Assert.Equal(reply.CollectionVersion, change.CollectionModifications.CollectionVersion);
        Assert.True(change.CollectionModifications.CollectionVersion > 100); // newer than the login collection
        Assert.Equal(1, Assert.Single(change.CollectionModifications.CardModifications).Quantity);
        Assert.Equal(60, change.CurrencyState.ArcaneDustBalance);
        Assert.True(change.CurrencyState.CurrencyVersion > 50);
    }

    [Fact]
    public void A_wrong_price_or_count_or_a_free_core_card_changes_nothing()
    {
        int common = Craftable(HsServer.Engine.TAG_RARITY.COMMON);
        var store = Store(100);
        Assert.Equal(BoughtSoldCard.Result.WRONG_BUY_PRICE, store.Craft(Tx(common, store, 30, create: 1), Data.Value.Rules).Reply.Result_);
        var mismatch = Tx(common, store, 40, create: 1);
        mismatch.ExpectedNormalOwnedCount = 2;
        Assert.Equal(BoughtSoldCard.Result.COUNT_MISMATCH, store.Craft(mismatch, Data.Value.Rules).Reply.Result_);
        int core = Data.Value.Cards.All.First(c => c.CardId.StartsWith("CORE_") && c.Tag(HsServer.Engine.GAME_TAG.COLLECTIBLE) == 1 &&
                                                  c.Type == HsServer.Engine.TAG_CARDTYPE.MINION).DbfId;
        Assert.Equal(BoughtSoldCard.Result.SOULBOUND, store.Craft(Tx(core, store, 40, create: 1), Data.Value.Rules).Reply.Result_);
        Assert.Equal(100, store.Dust);
        Assert.Equal(0, store.Count(common, 0));
        Assert.Equal(BoughtSoldCard.Result.GENERIC_FAILURE, Store(10).Craft(Tx(common, Store(10), 40, create: 1), Data.Value.Rules).Reply.Result_); // not enough dust
    }

    [Fact]
    public void Disenchant_and_golden_upgrade_use_the_value_table()
    {
        int rare = Craftable(HsServer.Engine.TAG_RARITY.RARE);
        var store = Store(1000);
        store.Craft(Tx(rare, store, 100, create: 1), Data.Value.Rules);
        store.Craft(Tx(rare, store, 100, create: 1), Data.Value.Rules);
        Assert.Equal(800, store.Dust);
        Assert.Equal(BoughtSoldCard.Result.SOLD, store.Craft(Tx(rare, store, -20, disenchant: 1), Data.Value.Rules).Reply.Result_);
        Assert.Equal(820, store.Dust);
        var (up, _) = store.Craft(Tx(rare, store, 700, upgrade: 1), Data.Value.Rules); // rare normal → golden: 700
        Assert.Equal(BoughtSoldCard.Result.UPGRADE, up.Result_);
        Assert.Equal(0, store.Count(rare, 0));
        Assert.Equal(1, store.Count(rare, 1));
        Assert.Equal(120, store.Dust);
    }

    [Fact]
    public void The_expected_dust_delta_is_the_cost_as_the_client_sends_it()
    {
        // live client request (2026-10-01): two rares, ExpectedDustDelta 200 (positive = dust spent)
        int rare = Craftable(HsServer.Engine.TAG_RARITY.RARE);
        var store = Store(1000);
        Assert.Equal(BoughtSoldCard.Result.BOUGHT, store.Craft(Tx(rare, store, 200, create: 2), Data.Value.Rules).Reply.Result_);
        Assert.Equal(800, store.Dust);
        // a golden from nothing costs normal Buy + normal Upgrade
        var fromNothing = Tx(rare, store, 800);
        fromNothing.GoldenUpgradeFromNothingCount = 1;
        Assert.Equal(BoughtSoldCard.Result.BOUGHT, store.Craft(fromNothing, Data.Value.Rules).Reply.Result_);
        Assert.Equal(1, store.Count(rare, 1));
        Assert.Equal(0, store.Dust);
    }

    [Fact]
    public void The_calendar_keeps_the_recorded_dates_and_announces_them_relative_to_now()
    {
        DateTime now = Today;
        var calendar = Calendar(() => now);
        Assert.True(calendar.Count > 1000);
        Assert.True(calendar.IsActive("SPECIAL_EVENT_ALWAYS"));          // DBF enum member → "always"
        Assert.False(calendar.IsActive("SPECIAL_EVENT_NEVER"));
        Assert.True(calendar.IsActive(EventNumber("dmf_craftable")));                      // DBF number → dmf_craftable (open-ended)
        Assert.False(calendar.IsActive("UNKNOWN"));
        Assert.False(calendar.IsActive("no_such_event"));                // not announced = off, as the client
        const string refundNotice = "dust_refund_36_4_2 notification";   // live: 2026-08-28 .. 2026-12-03
        Assert.True(calendar.IsActive(refundNotice));
        DateTime end = calendar.Get(refundNotice).End!.Value;
        var timing = calendar.Timings().Single(t => t.EventName == refundNotice);
        Assert.InRange(timing.SecondsToEnd, (long)(end - now).TotalSeconds - 1, (long)(end - now).TotalSeconds);
        Assert.True(timing.SecondsToStart < 0);
        Assert.False(calendar.Timings().Single(t => t.EventName == "always").HasSecondsToEnd);

        now = end.AddMinutes(1);                                          // it really ends (a replayed 714 never would)
        Assert.False(calendar.IsActive(refundNotice));
        Assert.True(calendar.Timings().Single(t => t.EventName == refundNotice).SecondsToEnd < 0);
    }

    [Fact]
    public void Admin_event_changes_survive_a_restart()
    {
        string file = Path.Combine(Path.GetTempPath(), "hsserver-events-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var calendar = Calendar(() => Today, file);
            Assert.False(calendar.IsActive("dust_refund_36_4_2"));
            calendar.Set("dust_refund_36_4_2", Today.AddDays(-1), Today.AddDays(20));
            calendar.Set("my_event", null, null);
            var again = Calendar(() => Today, file);
            Assert.True(again.IsActive("dust_refund_36_4_2"));
            Assert.True(again.IsActive(EventNumber("dust_refund_36_4_2")));                     // the same event by its DBF number
            Assert.True(again.IsActive("my_event"));
            Assert.Equal(Calendar(() => Today).Get("dust_refund_36_4_2").Id, again.Get("dust_refund_36_4_2").Id); // keeps the live id
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void A_nerf_refund_sells_for_the_buy_price_while_its_event_is_on()
    {
        const int nerfed = 118183; // TLC_460: CardValue override on dust_refund_36_4_2 (10000580), RECENTLY_NERFED_USE_BUY_VALUE
        var calendar = Calendar(() => Today);
        var rules = new CraftingRules(DbfDir, Data.Value.Cards, calendar);
        var before = rules.Value(nerfed, 0)!.Value;
        Assert.True(before.Sell < before.Buy);                           // the refund ended 2026-09-18
        calendar.Set("dust_refund_36_4_2", Today.AddDays(-1), Today.AddDays(20));
        var during = rules.Value(nerfed, 0)!.Value;
        Assert.Equal(during.Buy, during.Sell);                           // full refund
        Assert.Equal(before.Buy, during.Buy);

        var store = new CollectionStore(null);
        store.Seed(new Collection
        {
            Stacks = new List<PegasusShared.CardStack> { new() { CardDef = new PegasusShared.CardDef { Asset = nerfed, Premium = 0 }, Count = 1 } },
        }, new GameCurrencyStates());
        Assert.Equal(BoughtSoldCard.Result.SOLD, store.Craft(Tx(nerfed, store, -during.Buy, disenchant: 1), rules).Reply.Result_);
        Assert.Equal(during.Buy, store.Dust);
    }

    [Fact]
    public void A_card_whose_crafting_event_is_off_cannot_be_crafted()
    {
        // a collectible card crafted under its own event (Card.CraftingEvent 10000361 = dmf_craftable)
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DbfDir, "Card.json")));
        int dmf = doc.RootElement.GetProperty("records").EnumerateArray()
            .Where(r => r.TryGetProperty("CraftingEvent", out var e) && e.GetString() == EventNumber("dmf_craftable"))
            .Select(r => r.GetProperty("ID").GetInt32())
            .First(id => Data.Value.Cards.ByDbf(id) is { } c && c.Tag(HsServer.Engine.GAME_TAG.COLLECTIBLE) == 1 && Data.Value.Rules.Value(id, 0) != null);
        var calendar = Calendar(() => Today);
        var rules = new CraftingRules(DbfDir, Data.Value.Cards, calendar);
        int buy = rules.Value(dmf, 0)!.Value.Buy;
        calendar.Set("dmf_craftable", null, Today.AddDays(-1));
        Assert.Null(rules.Value(dmf, 0));
        var store = Store(5000);
        Assert.Equal(BoughtSoldCard.Result.EVENT_NOT_ACTIVE, store.Craft(Tx(dmf, store, buy, create: 1), rules).Reply.Result_);
        Assert.Equal(5000, store.Dust);
    }

    [Fact]
    public void Mass_disenchant_keeps_the_deck_limit_with_the_fanciest_copies()
    {
        int common = Craftable(HsServer.Engine.TAG_RARITY.COMMON);
        var store = new CollectionStore(null);
        store.Seed(new Collection
        {
            CollectionVersion = 5, Stacks = new List<PegasusShared.CardStack>
            {
                new() { CardDef = new PegasusShared.CardDef { Asset = common, Premium = 0 }, Count = 3 },
                new() { CardDef = new PegasusShared.CardDef { Asset = common, Premium = 1 }, Count = 1 },
            },
        }, new GameCurrencyStates { ArcaneDustBalance = 0, CurrencyVersion = 1 });
        var (reply, change) = store.MassDisenchant(Data.Value.Rules);
        Assert.Equal(10, reply.Amount);                  // 2 normal commons × 5
        Assert.Equal(1, store.Count(common, 1));         // the golden copy stays
        Assert.Equal(1, store.Count(common, 0));
        Assert.Equal(10, store.Dust);
        Assert.Equal(-2, Assert.Single(change!.CollectionModifications.CardModifications).Quantity);
        Assert.Equal(0, store.MassDisenchant(Data.Value.Rules).Reply.Amount); // nothing left to disenchant
    }

    [Fact]
    public void Collection_and_dust_survive_a_restart()
    {
        string file = Path.Combine(Path.GetTempPath(), "hsserver-collection-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            int common = Craftable(HsServer.Engine.TAG_RARITY.COMMON);
            var store = new CollectionStore(file);
            store.Seed(new Collection { Stacks = new List<PegasusShared.CardStack>(), CollectionVersion = 9 }, new GameCurrencyStates { ArcaneDustBalance = 100, CurrencyVersion = 3 });
            store.Craft(Tx(common, store, 40, create: 1), Data.Value.Rules);
            var again = new CollectionStore(file);
            Assert.True(again.Load());
            Assert.Equal(1, again.Count(common, 0));
            Assert.Equal(60, again.Dust);
            Assert.Equal(store.CollectionVersion, again.CollectionVersion);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Favorites_start_as_recorded_then_follow_the_player()
    {
        string file = Path.Combine(Path.GetTempPath(), "hsserver-favorites-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new CollectionStore(file);
            store.Seed(new Collection { Stacks = new List<PegasusShared.CardStack>() }, new GameCurrencyStates());
            var backs = new CardBacks { CardBacks_ = new List<int> { 0, 5, 9 }, FavoriteCardBacks = new List<int> { 5 } };
            store.ApplyTo(backs);                                   // first login: the recorded favorite is adopted
            Assert.Equal(new[] { 5 }, backs.FavoriteCardBacks);
            Assert.True(store.SetFavoriteCardBack(new SetFavoriteCardBack { CardBack = 9, IsFavorite = true }).Success);
            store.SetFavoriteCardBack(new SetFavoriteCardBack { CardBack = 5, IsFavorite = false });
            var hero = new PegasusShared.FavoriteHero { ClassId = 4, Hero = new PegasusShared.CardDef { Asset = 2828, Premium = 0 } };
            store.ApplyTo(new FavoriteHeroesResponse { FavoriteHeroes = new List<PegasusShared.FavoriteHero>() });
            store.SetFavoriteHero(new SetFavoriteHero { FavoriteHero = hero, IsFavorite = true });
            store.ApplyTo(new CosmeticCoins { FavoriteCoins = new List<int>(), Coins = new List<int> { 1, 2 } });
            store.SetFavoriteCoin(new SetFavoriteCosmeticCoin { Coin = 2, IsFavorite = true });

            var again = new CollectionStore(file);                  // a restart
            Assert.True(again.Load());
            var later = new CardBacks { CardBacks_ = new List<int> { 0, 5, 9 }, FavoriteCardBacks = new List<int> { 5 } };
            again.ApplyTo(later);                                   // the recording's favorite no longer wins
            Assert.Equal(new[] { 9 }, later.FavoriteCardBacks);
            var heroes = new FavoriteHeroesResponse { FavoriteHeroes = new List<PegasusShared.FavoriteHero>() };
            again.ApplyTo(heroes);
            Assert.Equal((4, 2828), (Assert.Single(heroes.FavoriteHeroes).ClassId, heroes.FavoriteHeroes[0].Hero.Asset));
            var coins = new CosmeticCoins { FavoriteCoins = new List<int> { 1 }, Coins = new List<int> { 1, 2 } };
            again.ApplyTo(coins);
            Assert.Equal(new[] { 2 }, coins.FavoriteCoins);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Login_currencies_are_adopted_once_then_the_players_own()
    {
        var store = Store(100); // seeded without renown / tokens / event currency / tickets
        var login = new GameCurrencyStates { ArcaneDustBalance = 999, RenownBalance = 3, BgTokenBalance = 5, BmecBalance = 1470, CurrencyVersion = 1 };
        store.ApplyTo(login);                                    // the recorded login's balances are adopted
        Assert.Equal((100L, 3L, 5L, 1470L), (login.ArcaneDustBalance, login.RenownBalance, login.BgTokenBalance, login.BmecBalance));
        var tickets = new PlayerDraftTickets { UnusedTicketBalance = 6 };
        store.ApplyTo(tickets);
        Assert.Equal(6, tickets.UnusedTicketBalance);

        var change = store.SetBalance(CollectionStore.Currency.BgTokens, 50);
        Assert.Equal(50, change.CurrencyState.BgTokenBalance);
        Assert.Equal(1470, change.CurrencyState.BmecBalance);    // the whole currency state, newer version
        Assert.True(change.CurrencyState.CurrencyVersion > 50);
        Assert.Equal(9, store.SetBalance(CollectionStore.Currency.ArenaTickets, 9).PlayerDraftTickets.UnusedTicketBalance);
        Assert.NotNull(store.SetBalance(CollectionStore.Currency.Gold, 400).CurrencyState); // gold: a currency update makes the client re-ask the shop

        var again = new GameCurrencyStates { RenownBalance = 77, BgTokenBalance = 77, BmecBalance = 77 };
        store.ApplyTo(again);                                    // later logins: the player's own, not the recording's
        Assert.Equal((3L, 50L, 1470L), (again.RenownBalance, again.BgTokenBalance, again.BmecBalance));
        var laterTickets = new PlayerDraftTickets { UnusedTicketBalance = 6 };
        store.ApplyTo(laterTickets);
        Assert.Equal(9, laterTickets.UnusedTicketBalance);
        Assert.Equal(400, store.Balance(CollectionStore.Currency.Gold));
    }

    [Fact]
    public void Missing_deck_cards_count_every_premium()
    {
        var store = new CollectionStore(null);
        store.Seed(new Collection
        {
            Stacks = new List<PegasusShared.CardStack>
            {
                new() { CardDef = new PegasusShared.CardDef { Asset = 10, Premium = 0 }, Count = 1 },
                new() { CardDef = new PegasusShared.CardDef { Asset = 10, Premium = 1 }, Count = 1 },
                new() { CardDef = new PegasusShared.CardDef { Asset = 20, Premium = 0 }, Count = 1 },
            },
        }, new GameCurrencyStates());
        Assert.Empty(store.Missing(new[] { 10, 10, 20 }));                       // a normal + a golden copy = 2
        Assert.Equal(new[] { (20, 1), (30, 2) }, store.Missing(new[] { 10, 20, 20, 30, 30 }));
    }

    [Fact]
    public void Seen_cards_stop_being_new()
    {
        var store = new CollectionStore(null);
        store.Seed(new Collection
        {
            Stacks = new List<PegasusShared.CardStack> { new() { CardDef = new PegasusShared.CardDef { Asset = 1234, Premium = 0 }, Count = 2, NumSeen = 0 } },
        }, new GameCurrencyStates());
        Assert.Equal(0, store.ToCollection(null).Stacks.Single().NumSeen); // new: glows in the collection
        store.Seen(new AckCardSeen { CardDefs = new List<PegasusShared.CardDef> { new() { Asset = 1234, Premium = 0 } } });
        Assert.Equal(2, store.ToCollection(null).Stacks.Single().NumSeen);
    }
}
