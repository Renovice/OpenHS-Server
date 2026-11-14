using System.Text.Json.Nodes;
using HsServer.Lobby;
using HsServer.Shop;
using HsServer.Protocol;
using PegasusUtil;

namespace HsServer.Tests;

/// <summary>The in-game shop answers (roadmap C3): recorded pages and catalog, the player's own gold / runestones.</summary>
public class ShopTests
{
    // a fixed live login (2026-10-01 16:48: gold 795), not "the newest", so the numbers below stay true
    private static readonly Lazy<ShopCatalog> Catalog = new(() => ShopCatalog.FromRecording(
        Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures", "live-20261001-startofgame", "session-20261001-164832.jsonl")));

    [Fact]
    public void The_recorded_shop_is_served_as_of_now()
    {
        ShopCatalog catalog = Catalog.Value;
        Assert.True(catalog.HasPages);
        Assert.True(catalog.ProductCount > 1000);
        DateTime now = new(2026, 12, 24, 12, 0, 0, DateTimeKind.Utc);
        JsonNode pages = JsonNode.Parse(catalog.Pages(now))!;
        Assert.Equal(new DateTimeOffset(now).ToUnixTimeMilliseconds(), pages["metaData"]!["serverTimeMs"]!.GetValue<long>());
        Assert.True(pages["placements"]!.AsArray().Count > 0);
        // schedules move with the clock: nothing is "next" before the recording's own time
        foreach (JsonNode p in pages["placements"]!.AsArray())
            if (p!["nextScheduledPageDisplayTimeMs"]?.GetValue<long>() is > 0 and var next)
                Assert.True(next > new DateTimeOffset(now).ToUnixTimeMilliseconds() - TimeSpan.FromDays(30).TotalMilliseconds);

        JsonNode all = JsonNode.Parse(catalog.Products("{\"productIds\":[]}", now))!;
        long first = all["products"]!.AsArray()[0]!["productId"]!.GetValue<long>();
        JsonNode one = JsonNode.Parse(catalog.Products("{\"productIds\":[" + first + "]}", now))!;
        Assert.Single(one["products"]!.AsArray());
        Assert.Equal("", one["paginationToken"]!.GetValue<string>());                               // one page: no more to ask
    }

    [Fact]
    public void Gold_and_runestones_start_as_live_then_are_the_players()
    {
        ShopCatalog catalog = Catalog.Value;
        Assert.Equal(795, catalog.RecordedBalances["XSH"]);                                          // live gold, 2026-10-01
        var store = new CollectionStore(null);
        store.Seed(new Collection { Stacks = new List<PegasusShared.CardStack>() }, new GameCurrencyStates());
        Assert.Equal(795, store.ShopBalance("XSH", catalog.RecordedBalances["XSH"]));
        Assert.Equal(0, store.ShopBalance("XSR", catalog.RecordedBalances.GetValueOrDefault("XSR")));
        store.SetBalance(CollectionStore.Currency.Gold, 5000);
        store.SetBalance(CollectionStore.Currency.Runestones, 300);
        Assert.Equal(5000, store.ShopBalance("XSH", 795));                                          // not adopted again
        Assert.Equal(300, store.ShopBalance("XSR", 0));

        JsonNode balance = JsonNode.Parse(ShopCatalog.Balance(
            "{\"ecosystem\":0,\"ledgerKey\":{\"gameServiceRegionId\":0,\"titleSegment\":\"T5_HS_EU\"},\"currencyCode\":\"XSH\"}", 5000, 42))!;
        Assert.Equal("5000", balance["balance"]!["grantedBalance"]!.GetValue<string>());
        Assert.Equal("XSH", balance["balance"]!["ledgerId"]!["currencyCode"]!.GetValue<string>());
        Assert.Equal("T5_HS_EU", balance["balance"]!["ledgerId"]!["ledgerKey"]!["titleSegment"]!.GetValue<string>());
        Assert.Equal(42UL, balance["balance"]!["ledgerId"]!["accountId"]!.GetValue<ulong>());
    }

    private static readonly Lazy<ShopOrders> Orders = new(() => ShopOrders.FromRecording(
        Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures", "live-20261002-shop-purchase", "session-20261002-111909.jsonl")));

    private static PlayerAccount Player(long gold, long runestones)
    {
        var store = new CollectionStore(null);
        store.Seed(new Collection { Stacks = new List<PegasusShared.CardStack>() }, new GameCurrencyStates());
        store.SetBalance(CollectionStore.Currency.Gold, gold);
        store.SetBalance(CollectionStore.Currency.Runestones, runestones);
        return new PlayerAccount
        {
            Name = "test", AccountId = 1, GameAccountId = 1, BattleTag = "Test#1", SaveData = new SaveDataStore(), Decks = new DeckStore(null), Collection = store,
        };
    }

    [Fact]
    public void Buying_a_pack_with_gold_charges_and_grants_like_live()
    {
        ShopOrders orders = Orders.Value;
        Assert.True(orders.Count > 3000);
        Bundle pack = orders.Get(2088499)!;                                                          // "1 Standard Pack"
        Assert.Equal(100L, ShopOrders.Price(pack, "XSH"));                                          // live: 795 → 695
        PlayerAccount player = Player(795, 0);
        var result = orders.Place(player, "{\"productId\":2088499,\"quantity\":1,\"currencyCode\":\"XSH\",\"externalTransactionId\":\"T1\"}");
        Assert.Equal("SUCCESS", JsonNode.Parse(result.Json)!["status"]!.GetValue<string>());
        Assert.Equal("T1", JsonNode.Parse(result.Json)!["externalTransactionId"]!.GetValue<string>());
        Assert.Equal(695, player.Collection.Balance(CollectionStore.Currency.Gold));
        Assert.Equal(1, player.Collection.PackCount(713));
        var mod = Assert.Single(result.Changes.OfType<ClientStateNotification>().Where(c => c.BoosterModifications != null)).BoosterModifications.Modifications.Single();
        Assert.Equal((713, 1, 1), (mod.Type, mod.Count, mod.EverGrantedCount));                    // as live's #567

        var poor = Player(50, 0);
        Assert.Equal("FAILED", JsonNode.Parse(orders.Place(poor, "{\"productId\":2088499,\"quantity\":1,\"currencyCode\":\"XSH\"}").Json)!["status"]!.GetValue<string>());
        Assert.Equal(50, poor.Collection.Balance(CollectionStore.Currency.Gold));                   // nothing charged
    }

    [Fact]
    public void Products_the_server_cant_grant_are_refused_without_charging_and_runestones_pay_their_price()
    {
        ShopOrders orders = Orders.Value;
        var data = PegasusPackets.Deserialize<BattlePayConfigResponse>(HsServer.Protocol.Recording.Read(
            Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures", "live-20261002-shop-purchase", "session-20261002-111909.jsonl"))
            .Last(p => p.Channel == "util" && p.IsIn && p.Type == 238).Body).Bundles;
        Bundle skin = data.First(b => b.Items.Any(i => i.ProductType == ProductType.PRODUCT_TYPE_LUCKY_DRAW) && ShopOrders.Price(b, "XSR") != null);
        PlayerAccount player = Player(0, 100000);
        var refused = orders.Place(player, "{\"productId\":" + skin.PmtProductId + ",\"quantity\":1,\"currencyCode\":\"XSR\"}");
        Assert.Equal("NOT_SUPPORTED", JsonNode.Parse(refused.Json)!["error"]!["code"]!.GetValue<string>());
        Assert.Equal(100000, player.Collection.Balance(CollectionStore.Currency.Runestones));

        Bundle packs = data.First(b => b.Items.Count > 0 && b.Items.All(ShopOrders.CanGrant) && b.Items.Any(i => i.ProductType == ProductType.PRODUCT_TYPE_BOOSTER)
                                     && ShopOrders.Price(b, "XSR") is > 0);
        long price = ShopOrders.Price(packs, "XSR")!.Value;
        var bought = orders.Place(player, "{\"productId\":" + packs.PmtProductId + ",\"quantity\":1,\"currencyCode\":\"XSR\"}");
        Assert.Equal("SUCCESS", JsonNode.Parse(bought.Json)!["status"]!.GetValue<string>());
        Assert.Equal(100000 - price, player.Collection.Balance(CollectionStore.Currency.Runestones));
        foreach (var item in packs.Items.Where(i => i.ProductType == ProductType.PRODUCT_TYPE_BOOSTER))
            Assert.True(player.Collection.PackCount(item.Data) >= item.Quantity);
    }

    [Fact]
    public void Mini_sets_and_class_sets_give_exactly_their_cards()
    {
        string session = Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures", "live-20261002-shop-purchase", "session-20261002-111909.jsonl");
        var orders = ShopOrders.FromRecording(session, HsServer.Engine.CardDb.FindDefaultDir(TestEnvironment.ProjectRoot));
        var bundles = PegasusPackets.Deserialize<BattlePayConfigResponse>(Recording.Read(session)
            .Last(p => p.Channel == "util" && p.IsIn && p.Type == 238).Body).Bundles;
        Bundle set = bundles.First(b => b.Items.Any(i => i.ProductType == ProductType.PRODUCT_TYPE_MINI_SET) && b.Items.All(orders.Grants)
                                        && (ShopOrders.Price(b, "XSH") ?? ShopOrders.Price(b, "XSR")) != null);
        BundleItem setItem = set.Items.First(i => i.ProductType == ProductType.PRODUCT_TYPE_MINI_SET);
        string currency = ShopOrders.Price(set, "XSH") != null ? "XSH" : "XSR";
        PlayerAccount player = Player(1_000_000, 1_000_000);
        var result = orders.Place(player, "{\"productId\":" + set.PmtProductId + ",\"quantity\":1,\"currencyCode\":\"" + currency + "\"}");
        Assert.Equal("SUCCESS", JsonNode.Parse(result.Json)!["status"]!.GetValue<string>());
        int premium = int.TryParse(setItem.Attributes.FirstOrDefault(a => a.Name == "premium")?.Value, out int p) ? p : 0;
        var cards = orders.MiniSet(setItem.Data);
        Assert.True(cards.Count > 3);
        Assert.All(cards, c => Assert.Equal(c.Copies, player.Collection.Count(c.Dbf, premium)));
    }

    [Fact]
    public void A_prebuilt_deck_gives_its_cards_and_the_deck()
    {
        string session = Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures", "live-20261002-shop-purchase", "session-20261002-111909.jsonl");
        var orders = ShopOrders.FromRecording(session, HsServer.Engine.CardDb.FindDefaultDir(TestEnvironment.ProjectRoot));
        var bundles = PegasusPackets.Deserialize<BattlePayConfigResponse>(Recording.Read(session)
            .Last(p => p.Channel == "util" && p.IsIn && p.Type == 238).Body).Bundles;
        Bundle product = bundles.First(b => b.Items.Any(i => i.ProductType == ProductType.PRODUCT_TYPE_SELLABLE_DECK) && b.Items.All(orders.Grants)
                                            && (ShopOrders.Price(b, "XSH") ?? ShopOrders.Price(b, "XSR")) != null);
        string currency = ShopOrders.Price(product, "XSH") != null ? "XSH" : "XSR";
        PlayerAccount player = Player(1_000_000, 1_000_000);
        int decksBefore = player.Decks.Count;
        var result = orders.Place(player, "{\"productId\":" + product.PmtProductId + ",\"quantity\":1,\"currencyCode\":\"" + currency + "\"}");
        Assert.Equal("SUCCESS", JsonNode.Parse(result.Json)!["status"]!.GetValue<string>());
        var deck = orders.Sellable(product.Items.First(i => i.ProductType == ProductType.PRODUCT_TYPE_SELLABLE_DECK).Data);
        Assert.Equal(30, deck.Cards.Sum(c => c.Copies));
        Assert.All(deck.Cards, c => Assert.True(player.Collection.Count(c.Dbf, 0) >= c.Copies));
        Assert.Equal(decksBefore + 1, player.Decks.Count);
        var info = player.Decks.Get(player.Decks.All().Max(d => d.Id));
        Assert.Equal(deck.Name, info.Name);
        Assert.True(info.Hero > 0);                                                                   // the class's basic hero
        Assert.Equal(30, player.Decks.CardIds(info.Id).Count);
    }

    private static (ShopOrders Orders, List<Bundle> Bundles) Live()
    {
        string session = Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures", "live-20261002-shop-purchase", "session-20261002-111909.jsonl");
        var orders = ShopOrders.FromRecording(session, HsServer.Engine.CardDb.FindDefaultDir(TestEnvironment.ProjectRoot));
        return (orders, PegasusPackets.Deserialize<BattlePayConfigResponse>(Recording.Read(session)
            .Last(p => p.Channel == "util" && p.IsIn && p.Type == 238).Body).Bundles);
    }

    private static ShopOrders.Result Buy(ShopOrders orders, PlayerAccount player, Bundle b)
    {
        string currency = ShopOrders.Price(b, "XSH") != null ? "XSH" : "XSR";
        return orders.Place(player, "{\"productId\":" + b.PmtProductId + ",\"quantity\":1,\"currencyCode\":\"" + currency + "\"}");
    }

    private static bool Priced(Bundle b) => (ShopOrders.Price(b, "XSH") ?? ShopOrders.Price(b, "XSR")) != null;

    [Fact]
    public void Hero_skins_card_backs_and_licenses_are_granted_like_the_client_expects()
    {
        var (orders, bundles) = Live();
        PlayerAccount player = Player(10_000_000, 10_000_000);

        // a constructed hero skin: the hero card in the collection (CollectionModifications)
        Bundle hero = bundles.First(b => Priced(b) && b.Items.All(orders.Grants) && b.Items.Any(i => i.ProductType == ProductType.PRODUCT_TYPE_HERO)
                                         && b.Items.Where(i => i.ProductType == ProductType.PRODUCT_TYPE_HERO).All(i => orders.BgHeroBase(i.Data) == 0));
        var heroResult = Buy(orders, player, hero);
        Assert.Equal("SUCCESS", JsonNode.Parse(heroResult.Json)!["status"]!.GetValue<string>());
        int heroCard = hero.Items.First(i => i.ProductType == ProductType.PRODUCT_TYPE_HERO).Data;
        Assert.True(player.Collection.Count(heroCard, 0) == 1
                    || player.Collection.Owned(CollectionStore.Cosmetic.BgHeroSkin).Count + player.Collection.Owned(CollectionStore.Cosmetic.BgGuideSkin).Count > 0);

        // a card back: owned, and the 333 CardBackModifications
        Bundle back = bundles.First(b => Priced(b) && b.Items.All(orders.Grants) && b.Items.Any(i => i.ProductType == ProductType.PRODUCT_TYPE_CARD_BACK)
                                         && b.Items.Where(i => i.ProductType == ProductType.PRODUCT_TYPE_CARD_BACK).All(i => !player.Collection.Owns(CollectionStore.Cosmetic.CardBack, i.Data)));
        var backResult = Buy(orders, player, back);
        int backId = back.Items.First(i => i.ProductType == ProductType.PRODUCT_TYPE_CARD_BACK).Data;
        Assert.True(player.Collection.Owns(CollectionStore.Cosmetic.CardBack, backId));
        Assert.Contains(backResult.Changes.OfType<ClientStateNotification>(), c => c.CardBackModifications?.CardBackModifications_.Any(m => m.AssetCardBackId == backId) == true);

        // a hidden license: owned, announced by an ACCOUNT_LICENSE notice, listed at the next login
        Bundle licensed = bundles.First(b => Priced(b) && b.Items.All(orders.Grants)
                                             && b.Items.Any(i => i.ProductType == ProductType.PRODUCT_TYPE_HIDDEN_LICENSE && orders.LicenseOf(i) != null)
                                             && !b.Items.Any(i => i.ProductType == ProductType.PRODUCT_TYPE_HIDDEN_LICENSE && orders.LicenseOf(i) is { } l && player.Collection.OwnsLicense(l)));
        var licenseResult = Buy(orders, player, licensed);
        long license = orders.LicenseOf(licensed.Items.First(i => i.ProductType == ProductType.PRODUCT_TYPE_HIDDEN_LICENSE && orders.LicenseOf(i) != null))!.Value;
        Assert.True(player.Collection.OwnsLicense(license));
        var notice = licenseResult.Changes.OfType<ClientStateNotification>().SelectMany(c => c.NoticeNotifications?.NoticeNotifications_ ?? new List<NoticeNotification>())
            .Single(n => n.Notice.AccountLicense?.License == license).Notice;
        Assert.Equal((21, 1L), (notice.Origin, notice.OriginData));
        Assert.Contains(player.Collection.LicenseInfos(), l => l.License == license && l.Flags_ == 1 && l.CasId == notice.AccountLicense.CasId);
    }

    [Fact]
    public void Free_products_cost_zero_gold_and_owned_bundles_cant_be_bought_again()
    {
        // the Black Empire sequence bundle's free ranks (live 2026-10-02): no prices, tagged "free"; the client buys them
        // as a 0-gold order. Owning a bundle's hidden license makes it ALREADY_OWNED (the next rank unlocks).
        string session = Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures", "live-20261002-shop-purchase", "session-20261002-111909.jsonl");
        var free = ShopCatalog.FromRecording(session).FreeProducts();
        Assert.Contains(2122560L, free);                                                            // random golden legendary
        Assert.Contains(2120946L, free);                                                            // Way of the Monk Coin
        Assert.DoesNotContain(2122561L, free);                                                      // the paid rank
        var orders = ShopOrders.FromRecording(session, HsServer.Engine.CardDb.FindDefaultDir(TestEnvironment.ProjectRoot), null, free);
        PlayerAccount player = Player(0, 0);

        Assert.Null(ShopOrders.Price(orders.Get(2120946)!, "XSH"));
        string Order(string currency) => "{\"productId\":2120946,\"quantity\":1,\"currencyCode\":\"" + currency + "\"}";
        Assert.Contains("NOT_FOR_SALE", orders.Place(player, Order("XSR")).Log);
        var coin = orders.Place(player, Order("XSH"));
        Assert.Equal("SUCCESS", JsonNode.Parse(coin.Json)!["status"]!.GetValue<string>());
        Assert.Equal(0, player.Collection.Balance(CollectionStore.Currency.Gold));
        Assert.True(player.Collection.Owns(CollectionStore.Cosmetic.Coin, 95));
        Assert.Contains(coin.Changes.OfType<CoinUpdate>(), c => c.AddCoinId.Contains(95));

        var again = orders.Place(player, Order("XSH"));
        Assert.Equal("FAILED", JsonNode.Parse(again.Json)!["status"]!.GetValue<string>());
        Assert.Contains("ALREADY_OWNED", again.Log);
        Assert.Empty(again.Changes);
    }

    [Fact]
    public void Coins_and_emotes_use_the_messages_the_client_handles()
    {
        var store = new CollectionStore(null);
        store.Seed(new Collection { Stacks = new List<PegasusShared.CardStack>() }, new GameCurrencyStates());
        var coin = Assert.IsType<CoinUpdate>(store.GrantCosmetic(CollectionStore.Cosmetic.Coin, 7));
        Assert.Equal(7, Assert.Single(coin.AddCoinId));
        Assert.Equal(611, PegasusPackets.IdOf(typeof(CoinUpdate)));
        Assert.Null(store.GrantCosmetic(CollectionStore.Cosmetic.Coin, 7));                         // already owned
        var emote = Assert.IsType<ClientStateNotification>(store.GrantCosmetic(CollectionStore.Cosmetic.BgEmote, 40))
            .BattlegroundsEmoteModifications.BattlegroundsEmoteModifications_.Single();
        Assert.True(emote.AddBattlegroundsEmote);
        Assert.True(emote.HasRemoveBattlegroundsEmote);                                             // the client's add branch tests it
        Assert.False(emote.RemoveBattlegroundsEmote);
    }

    private static readonly Lazy<(HsServer.Engine.CardDb Cards, CraftingRules Crafting, RandomCards Random)> RandomData = new(() =>
    {
        string dir = HsServer.Engine.CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
        var cards = HsServer.Engine.CardDb.Load(dir);
        var calendar = new EventCalendar(Path.Combine(dir, "EventMap.json"), EventCalendar.FromRecording(
            Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures", "live-20261002-shop-purchase", "session-20261002-111909.jsonl")))
            { Clock = () => new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc) };
        var crafting = new CraftingRules(dir, cards, calendar);
        return (cards, crafting, new RandomCards(dir, cards, crafting));
    });

    [Fact]
    public void Random_cards_come_from_their_subset_with_duplicate_protection()
    {
        var (cards, crafting, random) = RandomData.Value;
        int Rarity(int dbf) => cards.ByDbf(dbf).Tag(HsServer.Engine.GAME_TAG.RARITY);
        // 3459: Escape from Violet Hold (set 1988) by rarity; 2060: a class's Rares (CLASS 10, not sets 17/1403)
        var evh = random.Pool(3459);
        Assert.NotEmpty(evh);
        Assert.All(evh, c => Assert.Equal(1988, crafting.SetOf(c.DbfId)));
        Assert.Single(evh.Select(c => c.Tag(HsServer.Engine.GAME_TAG.RARITY)).Distinct());
        var classRares = random.Pool(2060);
        Assert.NotEmpty(classRares);
        Assert.All(classRares, c => Assert.Equal((10, 3), (c.Tag(HsServer.Engine.GAME_TAG.CLASS), c.Tag(HsServer.Engine.GAME_TAG.RARITY))));
        // 3499: "random Legendary" whose other rules aren't in the client export: Legendaries from any set (approximate)
        Assert.True(random.Approximate(3499));
        var wild = random.Pool(3499);
        Assert.All(wild, c => Assert.Equal(5, c.Tag(HsServer.Engine.GAME_TAG.RARITY)));
        Assert.True(wild.Select(c => crafting.SetOf(c.DbfId)).Distinct().Count() > 10);

        // 3476: Black Empire Legendaries (set 1994, not launched): the ones already in the client data
        var blackEmpire = random.Pool(3476);
        Assert.True(blackEmpire.Count >= 3); // BE_013, BE_259, BE_703 (BE_036 is never craftable: a bundle exclusive)
        Assert.All(blackEmpire, c => Assert.StartsWith("BE_", c.CardId));

        var owned = new Dictionary<int, int>();
        var picked = random.Pick(3499, 30, d => owned.GetValueOrDefault(d), new Random(4));
        Assert.Equal(30, picked.Distinct().Count());                                                   // no repeats while new ones remain
    }

    [Fact]
    public void Buying_random_cards_grants_them_with_a_reward_notice()
    {
        var (cards, crafting, random) = RandomData.Value;
        string session = Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures", "live-20261002-shop-purchase", "session-20261002-111909.jsonl");
        var orders = ShopOrders.FromRecording(session, HsServer.Engine.CardDb.FindDefaultDir(TestEnvironment.ProjectRoot), random);
        var bundles = PegasusPackets.Deserialize<BattlePayConfigResponse>(Recording.Read(session)
            .Last(p => p.Channel == "util" && p.IsIn && p.Type == 238).Body).Bundles;
        Bundle product = bundles.First(b => Priced(b) && b.Items.All(orders.Grants) && b.Items.Any(i => i.ProductType == ProductType.PRODUCT_TYPE_RANDOM_CARD));
        PlayerAccount player = Player(10_000_000, 10_000_000);
        var result = Buy(orders, player, product);
        Assert.Equal("SUCCESS", JsonNode.Parse(result.Json)!["status"]!.GetValue<string>());
        int expected = product.Items.Where(i => i.ProductType == ProductType.PRODUCT_TYPE_RANDOM_CARD).Sum(i => i.Quantity);
        var notices = result.Changes.OfType<ClientStateNotification>().SelectMany(c => c.NoticeNotifications?.NoticeNotifications_ ?? new List<NoticeNotification>())
            .Where(n => n.Notice.RewardCard != null).ToList();
        Assert.Equal(expected, notices.Count);
        Assert.All(notices, n => Assert.Equal(22, n.Notice.Origin));                                   // FROM_PURCHASE
        Assert.All(notices, n => Assert.True(player.Collection.Count(n.Notice.RewardCard.Card.Asset, n.Notice.RewardCard.Card.Premium) >= 1));
    }
}
