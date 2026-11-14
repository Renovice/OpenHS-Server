using HsServer.Lobby;
using HsServer.Protocol;
using HsServer.Shop;
using PegasusShared;
using PegasusUtil;

namespace HsServer.Tests;

/// <summary>D1 Black Market against the client data and the live item list of location-20260930 (event 19 "Wanderer's",
/// 2026-09-15 17:00 → 10-06 17:00 UTC, daily price refreshes at 07:00 UTC).</summary>
public class BlackMarketTests
{
    private static readonly string Session = Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures", "live-20261002-shop-purchase", "session-20261002-111909.jsonl");
    private static readonly string Live732 = Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures", "location-20260930", "session-20260930-071519.jsonl");
    private static readonly string Dir = HsServer.Engine.CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);

    private static EventCalendar Calendar(DateTime now) =>
        new(Path.Combine(Dir, "EventMap.json"), EventCalendar.FromRecording(Session)) { Clock = () => now };

    private static readonly Lazy<BlackMarketItemListResponse> LiveItems = new(() => BlackMarkets.LatestRecordedItems(new[] { Live732 }));
    private static readonly DateTime Oct2 = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FinalWeek = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Lazy<(BlackMarketRules Rules, RewardTracks Rewards)> Data = new(() =>
        (new BlackMarketRules(Dir, Path.Combine(TestEnvironment.ProjectRoot, "Server", "data", "black-market-start-prices.tsv")), new RewardTracks(new RewardTrackRules(Dir, Calendar(Oct2)), ShopOrders.FromRecording(Session, Dir), () => 100)));

    private static BlackMarkets Market(DateTime now, Random random = null, bool recorded = true) =>
        new(Data.Value.Rules, Calendar(now), Data.Value.Rewards, random ?? new Random(1), recorded ? LiveItems.Value : null);

    [Fact]
    public void Ranked_games_earn_bloodstones_up_to_the_daily_cap()
    {
        // wiki: win 200, loss or draw 100, 900 a day; live 728: base 100 + win bonus 100, at the cap 0 and DailyEarnCapReached
        PlayerAccount player = Player(0);
        BlackMarkets market = Market(Oct2);
        var win = market.RankedGameEnded(player, won: true)!.Response;
        Assert.Equal((100, 100, 200, true, false), (win.BaseAmount, win.WinBonusAmount, win.TotalEarned, win.IsWinner, win.DailyEarnCapReached));
        Assert.Equal(100, market.RankedGameEnded(player, won: false)!.Response.TotalEarned);
        for (int i = 0; i < 3; i++) market.RankedGameEnded(player, won: true);  // 900
        var capped = market.RankedGameEnded(player, won: true)!.Response;
        Assert.Equal((0, true), (capped.TotalEarned, capped.DailyEarnCapReached));
        Assert.Equal(900, player.Collection.Balance(CollectionStore.Currency.Bmec));
        Assert.Equal(900, market.PlayerState(player, null, null).DailyEarn);
        // a new earning day starts at 17:00 UTC
        Assert.Equal(200, Market(new DateTime(2026, 10, 2, 17, 30, 0, DateTimeKind.Utc)).RankedGameEnded(player, won: true)!.Response.TotalEarned);
        // patch notes: no Bloodstones in the final week
        Assert.Null(Market(FinalWeek).RankedGameEnded(player, won: true));
    }

    [Fact]
    public void Without_a_recording_prices_start_from_the_first_day_ones()
    {
        // the published first-day prices (Server/data/black-market-start-prices.tsv), then every daily refresh since
        PlayerAccount player = Player(0);
        var first = Market(new DateTime(2026, 9, 15, 18, 0, 0, DateTimeKind.Utc), recorded: false).ItemList(player, null);
        Assert.Equal(new[] { 750, 4200, 1050, 2100, 1050, 900 }, first.Items.Select(i => i.Price));
        var later = Market(new DateTime(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc), new Random(4), recorded: false).ItemList(Player(0), null);
        Assert.NotEqual(first.Items.Select(i => i.Price), later.Items.Select(i => i.Price));
        Assert.All(later.Items, i => Assert.InRange(i.Price, 1, 4200 * 1.15 * 1.15 + 1));
    }

    private static PlayerAccount Player(long bloodstones)
    {
        var store = new CollectionStore(null);
        store.Seed(new Collection { Stacks = new List<CardStack>() }, new GameCurrencyStates());
        store.SetBalance(CollectionStore.Currency.Bmec, bloodstones);
        return new PlayerAccount { Name = "test", AccountId = 1, GameAccountId = 1, BattleTag = "Test#1", SaveData = new SaveDataStore(), Decks = new DeckStore(null), Collection = store };
    }

    [Fact]
    public void The_market_is_open_with_live_numbers_during_its_event()
    {
        Assert.NotNull(LiveItems.Value);
        var state = Market(Oct2).PlayerState(Player(0), null, null);
        Assert.Equal(BlackMarketPlayerStateResponse.Status.BMPS_SUCCESS, state.Status_);
        Assert.Equal(19, state.EventId);
        Assert.Equal(16, state.CurrentEventDay);                    // live on 10-02: day 16/17 (from 09-15 17:00)
        Assert.InRange(state.SecondsUntilNextPriceRefresh, 19 * 3600 - 60, 19 * 3600 + 60); // the next refresh: 10-03 07:00
        Assert.False(state.InGracePeriod);
        // after the grace period: closed
        var closed = Market(new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc)).PlayerState(Player(0), null, null);
        Assert.Equal(BlackMarketPlayerStateResponse.Status.BMPS_NO_ACTIVE_EVENT, closed.Status_);
    }

    [Fact]
    public void Prices_start_from_live_and_change_by_whole_percents_each_day()
    {
        PlayerAccount player = Player(0);
        var today = Market(Oct2).ItemList(player, null);
        Assert.Equal(6, today.Items.Count);
        Assert.Equal(LiveItems.Value.Items.OrderBy(i => i.ItemAssetId).Select(i => i.Price), today.Items.Select(i => i.Price));
        // two refreshes later (10-03 and 10-04 07:00), each price moved at most 15 % a day
        var later = Market(new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc), new Random(3)).ItemList(player, null);
        foreach (BlackMarketItemEntry item in later.Items)
        {
            int before = today.Items.Single(i => i.ItemAssetId == item.ItemAssetId).Price;
            Assert.InRange(item.Price, (int)Math.Floor(before * 0.85 * 0.85) - 1, (int)Math.Ceiling(before * 1.15 * 1.15) + 1);
            Assert.InRange(item.PriceAdjustment, -15100, 15100); // the last change, thousandths of a percent
        }
        Assert.NotEqual(today.Items.Select(i => i.Price), later.Items.Select(i => i.Price));
    }

    [Fact]
    public void Buying_charges_bloodstones_counts_stock_and_sends_the_reward_notice()
    {
        BlackMarkets market = Market(Oct2);
        PlayerAccount player = Player(100000);
        BlackMarketItemEntry skin = market.ItemList(player, null).Items.Single(i => i.ItemAssetId == 75); // Champion Tyrande, stock 1
        Assert.Equal(BlackMarketBuyItemResponse.Status.BMBI_PRICE_MISMATCH,
            market.Buy(player, new BlackMarketBuyItemRequest { ItemAssetId = 75, Quantity = 1, ExpectedPrice = skin.Price + 1 }).Response.Status_);
        var bought = market.Buy(player, new BlackMarketBuyItemRequest { ItemAssetId = 75, Quantity = 1, ExpectedPrice = skin.Price });
        Assert.Equal(BlackMarketBuyItemResponse.Status.BMBI_SUCCESS, bought.Response.Status_);
        Assert.Equal(100000 - skin.Price, bought.Response.BmecBalanceAfter);
        Assert.Equal(0, bought.Response.RemainingStock);
        // the client's reward popup waits for a REWARD_LIST notice from the Black Market with this id
        Assert.Contains(bought.Changes, c => c is ClientStateNotification n && n.NoticeNotifications?.NoticeNotifications_
            .Any(x => x.Notice.Entry == bought.Response.RewardNoticeId && x.Notice.Origin == 35 && x.Notice.RewardList != null) == true);
        Assert.Equal(BlackMarketBuyItemResponse.Status.BMBI_OUT_OF_STOCK,
            market.Buy(player, new BlackMarketBuyItemRequest { ItemAssetId = 75, Quantity = 1, ExpectedPrice = skin.Price }).Response.Status_);
        BlackMarketItemEntry pack = market.ItemList(player, null).Items.Single(i => i.ItemAssetId == 78);
        Assert.Equal(BlackMarketBuyItemResponse.Status.BMBI_INSUFFICIENT_FUNDS,
            market.Buy(Player(10), new BlackMarketBuyItemRequest { ItemAssetId = 78, Quantity = 1, ExpectedPrice = pack.Price }).Response.Status_);
    }

    [Fact]
    public void Haggling_in_the_final_week_offers_all_bloodstones_once_per_item()
    {
        // patch 36.6: haggling "during the final week of the event" (October 6 - 13)
        PlayerAccount early = Player(10);
        BlackMarketItemEntry offered = Market(Oct2).ItemList(early, null).Items.Single(i => i.ItemAssetId == 75);
        Assert.Equal(BlackMarketHaggleItemResponse.Status.BMHI_INVALID_REQUEST,
            Market(Oct2).Haggle(early, new BlackMarketHaggleItemRequest { ItemAssetId = 75, Quantity = 1, ExpectedPrice = offered.Price }).Response.Status_);
        PlayerAccount rich = Player(100000);
        BlackMarkets market = Market(FinalWeek);
        BlackMarketItemEntry skin = market.ItemList(rich, null).Items.Single(i => i.ItemAssetId == 75);
        Assert.Equal(BlackMarketHaggleItemResponse.Status.BMHI_HAS_SUFFICIENT_FUNDS,
            market.Haggle(rich, new BlackMarketHaggleItemRequest { ItemAssetId = 75, Quantity = 1, ExpectedPrice = skin.Price }).Response.Status_);

        int accepted = 0, declined = 0;
        for (int seed = 0; seed < 40; seed++)
        {
            PlayerAccount player = Player(skin.Price / 2);
            BlackMarkets m = Market(FinalWeek, new Random(seed));
            skin = m.ItemList(player, null).Items.Single(i => i.ItemAssetId == 75);
            var haggle = m.Haggle(player, new BlackMarketHaggleItemRequest { ItemAssetId = 75, Quantity = 1, ExpectedPrice = skin.Price }).Response;
            Assert.InRange(haggle.SuccessChanceMilli, 490, 510);
            if (haggle.Status_ == BlackMarketHaggleItemResponse.Status.BMHI_ACCEPTED)
            {
                accepted++;
                Assert.Equal(0, haggle.BmecBalanceAfter); // all Bloodstones offered
                Assert.True(haggle.ShopClosed);
                Assert.True(haggle.RewardNoticeId > 0);
            }
            else
            {
                declined++;
                Assert.Equal(BlackMarketHaggleItemResponse.Status.BMHI_DECLINED, haggle.Status_);
                Assert.Equal(BlackMarketHaggleItemResponse.Status.BMHI_ALREADY_DECLINED,
                    m.Haggle(player, new BlackMarketHaggleItemRequest { ItemAssetId = 75, Quantity = 1, ExpectedPrice = skin.Price }).Response.Status_);
                Assert.Equal(BlackMarketItemEntry.HaggleStatus.HS_DECLINED, m.ItemList(player, null).Items.Single(i => i.ItemAssetId == 75).HaggleStatus_);
                // no price refresh in the final week (the last is 10-06 07:00): one haggle per item
                var tomorrow = Market(new DateTime(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc), new Random(seed));
                Assert.Equal(BlackMarketItemEntry.HaggleStatus.HS_DECLINED, tomorrow.ItemList(player, null).Items.Single(i => i.ItemAssetId == 75).HaggleStatus_);
            }
        }
        Assert.True(accepted > 5 && declined > 5, $"accepted {accepted}, declined {declined}");
    }
}
