using HsServer.Lobby;
using PegasusShared;
using PegasusUtil;

namespace HsServer.Tests;

/// <summary>The Ranked ladder against the client data (League 5), the wiki (Ranked) and the user's live medal
/// (live-20261001-1417: a win at Silver 8 2★ x8, streak 1 → Silver 5 1★ x7, streak 2).</summary>
public class RankedTests
{
    private static readonly string Dir = HsServer.Engine.CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
    private static readonly Lazy<RankedRules> Rules = new(() => new RankedRules(Dir));
    private static readonly DateTime Oct3 = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    private static RankedLadder Ladder(DateTime? now = null) => new(Rules.Value, () => now ?? Oct3);

    private static PlayerAccount Player(Action<RankedBook> setUp = null)
    {
        var store = new CollectionStore(null);
        store.Seed(new Collection { Stacks = new List<CardStack>() }, new GameCurrencyStates());
        store.RankedBook(b => { b.Adopted = true; setUp?.Invoke(b); return 0; });
        return new PlayerAccount { Name = "test", AccountId = 1, GameAccountId = 1, BattleTag = "Test#1", SaveData = new SaveDataStore(), Decks = new DeckStore(null), Collection = store };
    }

    private static MedalInfoData Standard(PlayerAccount p, DateTime? now = null) =>
        Ladder(now).Medal(p, null).MedalData.Single(d => d.FormatType == FormatType.FT_STANDARD);

    private static void Set(RankedBook b, int level, int stars, int streak, int bonus, int best = 0) =>
        b.Formats[(int)FormatType.FT_STANDARD] = new FormatRank
            { Season = 156, StarLevel = level, Stars = stars, Streak = streak, StarsPerWin = bonus, BestStarLevel = best == 0 ? level : best };

    [Fact]
    public void The_ladder_comes_from_the_client_data()
    {
        Assert.Equal(5, Rules.Value.LeagueId);
        Assert.Equal(51, Rules.Value.LegendLevel);
        Assert.Equal(new[] { 6, 11, 16, 21, 26, 31, 36, 41, 46 }, Enumerable.Range(1, 50).Where(Rules.Value.IsFloor));
        Assert.False(Rules.Value.Get(5).CanLoseStars);   // Bronze
        Assert.True(Rules.Value.Get(12).CanLoseStars);   // Silver 9
        Assert.Equal(567, Rules.Value.SeasonCardBack(156)); // "Kazakus", October 2026
    }

    [Fact]
    public void Seasons_are_months_ending_at_central_european_midnight()
    {
        // live: season 155 ended 2026-09-30 22:00 UTC, 156 ends 2026-10-31 23:00 UTC
        Assert.Equal((156, new DateTime(2026, 10, 31, 23, 0, 0, DateTimeKind.Utc)), RankedRules.SeasonAt(Oct3));
        Assert.Equal(155, RankedRules.SeasonAt(new DateTime(2026, 9, 30, 21, 59, 0, DateTimeKind.Utc)).Season);
        Assert.Equal(156, RankedRules.SeasonAt(new DateTime(2026, 9, 30, 22, 0, 0, DateTimeKind.Utc)).Season);
        Assert.Equal(157, RankedRules.SeasonAt(new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc)).Season);
    }

    [Fact]
    public void A_win_replays_the_live_game()
    {
        PlayerAccount p = Player(b => Set(b, 13, 2, 1, 8));
        Ladder().GameEnded(p, 2, won: true, null);
        MedalInfoData d = Standard(p);
        Assert.Equal((16, 1, 2, 7), (d.StarLevel, d.Stars, d.Streak, d.StarsPerWin)); // Silver 5, 1 star, streak 2, x7
        Assert.Equal(1, d.SeasonGames);
        Assert.Equal(16, d.BestStarLevel);
    }

    [Fact]
    public void A_third_win_in_a_row_doubles_the_stars_below_diamond_5()
    {
        PlayerAccount p = Player(b => Set(b, 1, 0, 2, 1));
        Ladder().GameEnded(p, 2, won: true, null);
        Assert.Equal((1, 2), (Standard(p).StarLevel, Standard(p).Stars));
        PlayerAccount high = Player(b => Set(b, 46, 0, 2, 1)); // Diamond 5: no streak bonus
        Ladder().GameEnded(high, 2, won: true, null);
        Assert.Equal(1, Standard(high).Stars);
    }

    [Fact]
    public void Losses_take_stars_above_bronze_and_floors_hold()
    {
        PlayerAccount bronze = Player(b => Set(b, 3, 2, 3, 1));
        Ladder().GameEnded(bronze, 2, won: false, null);
        Assert.Equal((3, 2, 0), (Standard(bronze).StarLevel, Standard(bronze).Stars, Standard(bronze).Streak)); // no star lost in Bronze
        PlayerAccount silver9 = Player(b => Set(b, 12, 0, 0, 1));
        Ladder().GameEnded(silver9, 2, won: false, null);
        Assert.Equal((11, 2), (Standard(silver9).StarLevel, Standard(silver9).Stars)); // down to Silver 10
        PlayerAccount floor = Player(b => Set(b, 11, 0, 0, 1));
        Ladder().GameEnded(floor, 2, won: false, null);
        Assert.Equal((11, 0), (Standard(floor).StarLevel, Standard(floor).Stars)); // a floor holds
    }

    [Fact]
    public void A_new_season_starts_at_bronze_10_with_the_bonus_for_last_seasons_best()
    {
        PlayerAccount p = Player(b => b.Formats[(int)FormatType.FT_STANDARD] = new FormatRank { Season = 155, StarLevel = 42, Stars = 1, BestStarLevel = 42, StarsPerWin = 2 });
        MedalInfoData d = Standard(p);
        Assert.Equal((156, 1, 0, 8, 0), (d.SeasonId, d.StarLevel, d.Stars, d.StarsPerWin, d.SeasonGames)); // Diamond 9 → 8x
        Assert.Equal(5, Rules.Value.LeagueId == d.LeagueId ? 5 : 0);
    }

    [Fact]
    public void The_fifth_win_of_the_season_gives_its_card_back_once()
    {
        PlayerAccount p = Player(b => { Set(b, 5, 0, 0, 1); b.Formats[(int)FormatType.FT_STANDARD].SeasonWins = 4; });
        var fifth = Ladder().GameEnded(p, 2, won: true, null);
        Assert.Single(fifth.Changes);
        Assert.True(p.Collection.Owns(CollectionStore.Cosmetic.CardBack, 567));
        Assert.Empty(Ladder().GameEnded(p, 2, won: true, null).Changes);
    }

    private static PlayerAccount Named(string name, Action<RankedBook> setUp)
    {
        PlayerAccount p = Player(setUp);
        return new PlayerAccount { Name = name, AccountId = 1, GameAccountId = 1, BattleTag = name + "#1", SaveData = p.SaveData, Decks = p.Decks, Collection = p.Collection };
    }

    [Fact]
    public void The_hidden_rating_moves_with_results_and_is_never_sent()
    {
        PlayerAccount p = Player(b => Set(b, 13, 2, 0, 1));
        Ladder().GameEnded(p, 2, won: true, null);
        var (afterWin, deviation) = Ladder().Rating(p, FormatType.FT_STANDARD);
        Assert.True(afterWin > 1500 && deviation < 350);
        Ladder().GameEnded(p, 2, won: false, null);
        Assert.True(Ladder().Rating(p, FormatType.FT_STANDARD).Rating < afterWin);
        Assert.False(Standard(p).HasRating); // live's MedalInfo never carries it
    }

    [Fact]
    public void Legend_numbers_are_places_by_rating_among_the_servers_legends()
    {
        var board = new RankedLeaderboard();
        var ladder = new RankedLadder(Rules.Value, () => Oct3, board);
        void Legend(RankedBook b, double rating)
        {
            Set(b, 51, 0, 0, 1);
            b.Formats[(int)FormatType.FT_STANDARD].Rating = rating;
        }
        PlayerAccount a = Named("a", b => Legend(b, 1600)), top = Named("top", b => Legend(b, 1900)), c = Named("c", b => Set(b, 30, 1, 0, 1));
        foreach (PlayerAccount p in new[] { a, top, c }) ladder.Medal(p, null);
        MedalInfoData Of(PlayerAccount p) => ladder.Medal(p, null).MedalData.Single(d => d.FormatType == FormatType.FT_STANDARD);
        Assert.Equal(1, Of(top).LegendRank);
        Assert.Equal(2, Of(a).LegendRank);
        Assert.False(Of(c).HasLegendRank); // not in Legend: no number
    }

    [Fact]
    public void Next_season_bonus_counts_high_legend_and_rating()
    {
        // season 156: one top Legend player (11x: top 10 % of Legend), and a Gold player whose rating is the highest of the
        // server (the MMR part: players with the top rating reached Legend → 10x)
        var board = new RankedLeaderboard();
        var october = new RankedLadder(Rules.Value, () => Oct3, board);
        PlayerAccount legend = Named("legend", b => { Set(b, 51, 0, 0, 1); b.Formats[(int)FormatType.FT_STANDARD].Rating = 2000; });
        PlayerAccount strong = Named("strong", b => { Set(b, 21, 1, 0, 1); b.Formats[(int)FormatType.FT_STANDARD].Rating = 2100; });
        PlayerAccount weak = Named("weak", b => { Set(b, 3, 1, 0, 1); b.Formats[(int)FormatType.FT_STANDARD].Rating = 1300; });
        foreach (PlayerAccount p in new[] { legend, strong, weak }) october.Medal(p, null);
        var november = new RankedLadder(Rules.Value, () => new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc), board);
        int Bonus(PlayerAccount p) => november.Medal(p, null).MedalData.Single(d => d.FormatType == FormatType.FT_STANDARD).StarsPerWin;
        Assert.Equal(11, Bonus(legend));
        Assert.Equal(4, RankedRules.SeasonStartBonus(21));
        Assert.Equal(10, Bonus(strong)); // Gold 10 alone would be 4x
        Assert.Equal(2, Bonus(weak));
        Assert.Equal(1, november.Medal(weak, null).MedalData.Single(d => d.FormatType == FormatType.FT_STANDARD).StarLevel); // Bronze 10
    }

    [Fact]
    public void Matchmaking_pairs_the_closest_rating_and_widens_with_waiting()
    {
        var waiting = new[] { ("far", 2000.0, TimeSpan.Zero), ("near", 1580.0, TimeSpan.Zero) };
        Assert.Equal("near", RankedLadder.ClosestByRating(1500, TimeSpan.Zero, waiting));
        Assert.Null(RankedLadder.ClosestByRating(1500, TimeSpan.Zero, new[] { ("far", 2000.0, TimeSpan.Zero) }));
        Assert.Equal("far", RankedLadder.ClosestByRating(1500, TimeSpan.FromSeconds(80), new[] { ("far", 2000.0, TimeSpan.Zero) }));
    }

    [Fact]
    public void The_live_medal_is_taken_over_once()
    {
        // the reference login (live-20261002-excavate-tiers): Standard Gold 6, 3 stars, streak 2, x6
        string session = Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures", "live-20261002-excavate-tiers", "session-20261002-195327.jsonl");
        MedalInfo live = RankedLadder.Recorded(RecordedAccount.Load(session));
        Assert.NotNull(live);
        var store = new CollectionStore(null);
        store.Seed(new Collection { Stacks = new List<CardStack>() }, new GameCurrencyStates());
        var p = new PlayerAccount { Name = "t", AccountId = 1, GameAccountId = 1, BattleTag = "T#1", SaveData = new SaveDataStore(), Decks = new DeckStore(null), Collection = store };
        MedalInfoData d = Ladder().Medal(p, live).MedalData.Single(x => x.FormatType == FormatType.FT_STANDARD);
        Assert.Equal((25, 3, 2, 6), (d.StarLevel, d.Stars, d.Streak, d.StarsPerWin));
        Assert.Equal(4, Ladder().Medal(p, live).MedalData.Count);
    }

    // ---------- the end-of-season chest ----------

    private static readonly Lazy<RankedSeasonRewards> SeasonRewards = new(() => new RankedSeasonRewards(Dir, Rules.Value.LeagueId));
    private static readonly Lazy<HsServer.Shop.RandomCards> Random = new(() =>
    {
        var cards = HsServer.Engine.CardDb.Load(Dir);
        var calendar = new EventCalendar(Path.Combine(Dir, "EventMap.json"), null) { Clock = () => Oct3 };
        return new HsServer.Shop.RandomCards(Dir, cards, new CraftingRules(Dir, cards, calendar));
    });

    /// <summary>The season's reward pack in the current card data (Booster.RankedRewardInitialSeason: the newest at or before the season;
    /// 1047 in 36.6, a newer one since 37.0) — read, not hard-coded, so a card-data update doesn't break the test.</summary>
    private static int SeasonPack(int season)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Dir, "Booster.json")));
        return doc.RootElement.GetProperty("records").EnumerateArray()
            .Where(r => r.TryGetProperty("RankedRewardInitialSeason", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number &&
                        v.GetInt32() > 0 && v.GetInt32() <= season)
            .OrderBy(r => r.GetProperty("RankedRewardInitialSeason").GetInt32()).Last().GetProperty("ID").GetInt32();
    }

    private static RankedLadder ChestLadder() => new(Rules.Value, () => Oct3, null, SeasonRewards.Value);

    [Fact]
    public void The_chest_adds_up_every_milestone_reached()
    {
        Assert.Empty(SeasonRewards.Value.Chest(5, 156));                                  // nothing below Bronze 5
        Assert.Equal(new[] { ("CARD", 22, 1) }, SeasonRewards.Value.Chest(6, 156));       // Bronze 5: a Standard rare
        var legend = SeasonRewards.Value.Chest(51, 156);
        Assert.Equal(5, legend.Where(i => i.Kind == "PACK").Sum(i => i.Count));           // a pack every 10 ranks
        Assert.Equal(1, legend.Where(i => i.Kind == "CARD" && i.Data == 23).Sum(i => i.Count)); // Diamond 5: an epic
        Assert.Equal(7, legend.Where(i => i.Kind == "CARD" && i.Data == 22).Sum(i => i.Count)); // 1 + 2 + 2 + 2 rares
        Assert.All(legend.Where(i => i.Kind == "PACK"), i => Assert.Equal(SeasonPack(156), i.Data)); // the season's pack
    }

    [Fact]
    public void A_finished_season_grants_its_chest_once_at_login()
    {
        PlayerAccount p = Player(b => b.Formats[(int)FormatType.FT_STANDARD] = new FormatRank
            { Season = 155, StarLevel = 28, BestStarLevel = 30, SeasonGames = 12, SeasonWins = 7, StarsPerWin = 3 });
        int packs = p.Collection.PackCount(SeasonPack(155));
        var changes = ChestLadder().SeasonEnd(p, null, Random.Value, new Random(1));
        Assert.NotEmpty(changes);
        Assert.Equal(packs + 2, p.Collection.PackCount(SeasonPack(155)));                        // Silver 5 and Gold 5 packs
        var notices = new ProfileNotices { List = new List<ProfileNotice>() };
        p.Collection.ApplyTo(notices);
        ProfileNotice n = Assert.Single(notices.List, x => x.Medal != null);
        Assert.Equal((1, 155L), (n.Origin, n.OriginData));                              // SEASON, the season that ended
        Assert.Equal((28, 30, 5), (n.Medal.StarLevel, n.Medal.BestStarLevel, n.Medal.LeagueId));
        Assert.Equal(ProfileNoticeMedal.MedalType.STANDARD_MEDAL, n.Medal.MedalType_);
        Assert.Equal(2, n.Medal.Chest.Bag.Sum(b => b.RewardBooster?.BoosterCount ?? 0));
        Assert.Equal(5, n.Medal.Chest.Bag.Count(b => b.RewardCard != null));            // 1 + 2 + 2 rares
        Assert.Empty(ChestLadder().SeasonEnd(p, null, Random.Value));                   // once
    }

    [Fact]
    public void No_chest_without_ranked_games_or_before_the_season_ends()
    {
        PlayerAccount idle = Player(b => b.Formats[(int)FormatType.FT_STANDARD] = new FormatRank { Season = 155, StarLevel = 1, BestStarLevel = 1 });
        Assert.Empty(ChestLadder().SeasonEnd(idle, null, Random.Value));
        PlayerAccount playing = Player(b => Set(b, 20, 1, 0, 3));
        playing.Collection.RankedBook(b => b.Formats[(int)FormatType.FT_STANDARD].SeasonGames = 4);
        Assert.Empty(ChestLadder().SeasonEnd(playing, null, Random.Value));             // season 156 is still on
    }

    [Fact]
    public void The_admin_season_end_gives_this_seasons_chest_and_starts_over()
    {
        PlayerAccount p = Player(b => Set(b, 20, 1, 0, 3));
        p.Collection.RankedBook(b => b.Formats[(int)FormatType.FT_STANDARD].SeasonGames = 4);
        Assert.NotEmpty(ChestLadder().EndSeasonNow(p, null, Random.Value, new Random(2)));
        var notices = new ProfileNotices { List = new List<ProfileNotice>() };
        p.Collection.ApplyTo(notices);
        Assert.Equal(156L, Assert.Single(notices.List, x => x.Medal != null).OriginData);
        MedalInfoData d = Standard(p);
        Assert.Equal((1, 0, 0, 3), (d.StarLevel, d.SeasonGames, d.Stars, d.StarsPerWin)); // Bronze 10, Silver 1 best → x3
        Assert.Empty(ChestLadder().SeasonEnd(p, null, Random.Value));
    }
}
