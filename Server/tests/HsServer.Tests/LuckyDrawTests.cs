using HsServer.Lobby;
using HsServer.Protocol;
using HsServer.Shop;
using PegasusLuckyDraw;
using PegasusShared;
using PegasusUtil;

namespace HsServer.Tests;

/// <summary>D2 Lucky Draw (FINDINGS 2026-10-02) against the client data and live-20261002-shop-purchase (boxes 39, 81,
/// 82 on; box 81 = Kel'Thuzad: 10 rewards, weights 1000).</summary>
public class LuckyDrawTests
{
    private static readonly string Session = Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures", "live-20261002-shop-purchase", "session-20261002-111909.jsonl");
    private static readonly string Dir = HsServer.Engine.CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);

    private static readonly Lazy<EventCalendar> Calendar = new(() =>
        new EventCalendar(Path.Combine(Dir, "EventMap.json"), EventCalendar.FromRecording(Session))
            { Clock = () => new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc) });
    private static readonly Lazy<LuckyDrawRules> Rules = new(() => new LuckyDrawRules(Dir));
    private static readonly Lazy<ShopOrders> Orders = new(() => ShopOrders.FromRecording(Session, Dir, null, ShopCatalog.FromRecording(Session).FreeProducts()));

    private static LuckyDraws Draws(Random random = null) =>
        new(Rules.Value, Calendar.Value, new RewardTracks(new RewardTrackRules(Dir, Calendar.Value), Orders.Value, () => 100), random);

    private static PlayerAccount Player()
    {
        var store = new CollectionStore(null);
        store.Seed(new Collection { Stacks = new List<CardStack>() }, new GameCurrencyStates());
        store.SetBalance(CollectionStore.Currency.Runestones, 100000);
        return new PlayerAccount { Name = "test", AccountId = 1, GameAccountId = 1, BattleTag = "Test#1", SaveData = new SaveDataStore(), Decks = new DeckStore(null), Collection = store };
    }

    private static LuckyDrawState Box(LuckyDraws draws, PlayerAccount player, int box, bool reconcile = false) =>
        draws.State(player, new LuckyDrawBoxStateRequest { ReconcileRewards = reconcile }, null).Response.ActiveLuckyDraws.Single(s => s.LuckyDrawBoxId == box);

    private static void Own(PlayerAccount player, int box, int pulls)
    {
        foreach (int l in Rules.Value.Get(box).PullLicenses.Take(pulls)) player.Collection.GrantLicense(Rules.Value.LicenseOf(l)!.Value);
    }

    [Fact]
    public void Boxes_come_from_the_client_data_and_open_with_their_event()
    {
        LuckyDrawRules.Box kt = Rules.Value.Get(81);
        Assert.Equal(10, kt.Rewards.Count);
        Assert.Equal(10, kt.PullLicenses.Count);
        Assert.Equal(1000, kt.Rewards.Sum(r => r.Weight));
        Assert.Equal(1, Assert.Single(kt.Rewards, r => r.Style == "CHASE").Weight);
        // live on 2026-10-02: boxes 39, 81, 82
        var open = Draws().State(Player(), new LuckyDrawBoxStateRequest(), null).Response;
        Assert.Equal(ErrorCode.ERROR_OK, open.ErrorCode);
        Assert.Equal(new[] { 39, 81, 82 }, open.ActiveLuckyDraws.Select(s => s.LuckyDrawBoxId));
        LuckyDrawState fresh = open.ActiveLuckyDraws.Single(s => s.LuckyDrawBoxId == 81);
        Assert.Equal(kt.Rewards.Select(r => r.Id), fresh.Rewards.Select(r => r.LuckyDrawRewardId)); // all, in DBF order
        Assert.Empty(fresh.Pulls);
        Assert.Equal(kt.PullLicenses[0], fresh.NextPullLicenseAsset);
    }

    [Fact]
    public void A_bought_pull_wins_one_reward_which_is_granted_and_then_seen()
    {
        LuckyDraws draws = Draws();
        PlayerAccount player = Player();
        // the shop sells the box's next pull license (the first one is free)
        int next = Box(draws, player, 81).NextPullLicenseAsset;
        var bundles = PegasusPackets.Deserialize<BattlePayConfigResponse>(Recording.Read(Session).Last(p => p.Channel == "util" && p.IsIn && p.Type == 238).Body).Bundles;
        Bundle product = bundles.First(b => b.Items.Any(i => i.ProductType == ProductType.PRODUCT_TYPE_LUCKY_DRAW && i.Data == next));
        Assert.Contains(product.PmtProductId, Orders.Value.Free); // the first pull is free (wiki: "The first roll ... is free")
        string currency = "XSH";
        var bought = Orders.Value.Place(player, "{\"productId\":" + product.PmtProductId + ",\"quantity\":1,\"currencyCode\":\"" + currency + "\"}");
        Assert.Contains("SUCCESS", bought.Json);
        Assert.Contains(bought.Changes, c => c is ClientStateNotification { NoticeNotifications: not null }); // ACCOUNT_LICENSE

        var pull = draws.UsePull(player, 81);
        Assert.Equal(ErrorCode.ERROR_OK, pull.Response.ErrorCode);
        Assert.Contains(pull.Response.LuckyDrawRewardId, Rules.Value.Get(81).Rewards.Select(r => r.Id));
        Assert.NotEmpty(pull.Changes); // the reward list's items
        LuckyDrawState after = Box(draws, player, 81);
        Assert.True(after.Rewards.Single(r => r.LuckyDrawRewardId == pull.Response.LuckyDrawRewardId).IsAwarded);
        Assert.Equal(new[] { true }, after.Pulls.Select(p => p.IsUsed));
        Assert.Equal(Rules.Value.Get(81).PullLicenses[1], after.NextPullLicenseAsset);

        Assert.Equal(ErrorCode.ERROR_OK, draws.Acknowledge(player, 81, pull.Response.LuckyDrawRewardId).ErrorCode);
        Assert.True(Box(draws, player, 81).Rewards.Single(r => r.LuckyDrawRewardId == pull.Response.LuckyDrawRewardId).IsAcknowledged);
        Assert.NotEqual(ErrorCode.ERROR_OK, draws.UsePull(player, 81).Response.ErrorCode); // no pull left
    }

    [Fact]
    public void Ten_pulls_win_all_ten_rewards_without_repeats()
    {
        // wiki Darkmoon Faire Treasure: "Duplicates of the rewards cannot be received" — a pulled item's weight becomes 0
        LuckyDraws draws = Draws(new Random(5));
        PlayerAccount player = Player();
        Own(player, 81, 10);
        var won = Enumerable.Range(0, 10).Select(_ => draws.UsePull(player, 81).Response.LuckyDrawRewardId).ToList();
        Assert.Equal(Rules.Value.Get(81).Rewards.Select(r => r.Id).OrderBy(i => i), won.OrderBy(i => i));
        Assert.Null(Box(draws, player, 81).Pulls.FirstOrDefault(p => !p.IsUsed));
        Assert.Equal(0, Box(draws, player, 81).NextPullLicenseAsset); // nothing more to buy
    }

    [Fact]
    public void The_first_pull_follows_the_weights()
    {
        var counts = new Dictionary<int, int>();
        var random = new Random(11);
        var draws = new LuckyDraws(Rules.Value, Calendar.Value, null, random);
        for (int i = 0; i < 4000; i++)
        {
            PlayerAccount player = Player();
            Own(player, 81, 1);
            int reward = draws.UsePull(player, 81).Response.LuckyDrawRewardId;
            counts[reward] = counts.GetValueOrDefault(reward) + 1;
        }
        foreach (LuckyDrawRules.Reward r in Rules.Value.Get(81).Rewards.Where(r => r.Weight >= 100))
            Assert.InRange(counts.GetValueOrDefault(r.Id) / 4000.0, r.Weight / 1000.0 - 0.03, r.Weight / 1000.0 + 0.03);
    }

    [Fact]
    public void An_unused_pull_is_pulled_at_login_and_counts_as_seen()
    {
        // live (location-20260930 seq 334): the reply to the login's ReconcileRewards lists the reward in reconciled_rewards,
        // awarded and acknowledged
        LuckyDraws draws = Draws();
        PlayerAccount player = Player();
        Own(player, 82, 2);
        LuckyDrawState state = Box(draws, player, 82, reconcile: true);
        Assert.Equal(2, state.ReconciledRewards.Count);
        Assert.All(state.ReconciledRewards, id => Assert.True(state.Rewards.Single(r => r.LuckyDrawRewardId == id) is { IsAwarded: true, IsAcknowledged: true }));
        Assert.Empty(Box(draws, player, 82, reconcile: true).ReconciledRewards);
    }

    [Fact]
    public void The_recorded_login_box_state_is_found_where_the_server_looks()
    {
        // the 10201 comes as its own util message after the request's reply: RecordedAccount.LastUtil, not the exchange's
        // reply body (the server first looked there, found nothing and took over an empty state: local-test-20261002-luckydraw-shop)
        string reference = Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures", "live-20261002-excavate-tiers", "session-20261002-195327.jsonl");
        RecordedAccount account = RecordedAccount.Load(reference);
        Assert.True(account.LastUtil.TryGetValue(10201, out byte[] body));
        var recorded = PegasusPackets.Deserialize<LuckyDrawBoxStateResponse>(body);
        Assert.Contains(recorded.ActiveLuckyDraws, s => s.Rewards.Any(r => r.IsAwarded));
    }

    [Fact]
    public void Live_box_state_is_taken_over_once()
    {
        // location-20260930: box 39 with reward 222 won and seen, one pull
        string live = Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures", "location-20260930", "session-20260930-071519.jsonl");
        var recorded = Recording.Read(live).Where(p => p.Channel == "util" && p.IsIn && p.Type == 10201)
            .Select(p => PegasusPackets.Deserialize<LuckyDrawBoxStateResponse>(p.Body))
            .First(r => r.ActiveLuckyDraws.Any(s => s.LuckyDrawBoxId == 39 && s.Pulls.Count > 0));
        LuckyDraws draws = Draws();
        PlayerAccount player = Player();
        var first = draws.State(player, new LuckyDrawBoxStateRequest(), recorded).Response.ActiveLuckyDraws.Single(s => s.LuckyDrawBoxId == 39);
        Assert.True(first.Rewards.Single(r => r.LuckyDrawRewardId == 222) is { IsAwarded: true, IsAcknowledged: true });
        Assert.Equal(new[] { true }, first.Pulls.Select(p => p.IsUsed));
        Assert.Equal(Rules.Value.Get(39).PullLicenses[1], first.NextPullLicenseAsset);
        // a second login doesn't take it over again (nor undo what the player did since)
        Own(player, 39, 2);
        draws.UsePull(player, 39);
        var again = draws.State(player, new LuckyDrawBoxStateRequest(), recorded).Response.ActiveLuckyDraws.Single(s => s.LuckyDrawBoxId == 39);
        Assert.Equal(2, again.Rewards.Count(r => r.IsAwarded));
    }
}
