using HsServer.Lobby;
using HsServer.Protocol;
using PegasusUtil;

namespace HsServer.Tests;

/// <summary>C4 reward track against the live capture live-20261002-progression (track 175, level 39, no Tavern Pass).</summary>
public class RewardTrackTests
{
    private static readonly string Session = Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures", "live-20261002-progression", "session-20261002-133808.jsonl");
    private const long TavernPass = 1183935; // track 175's AccountLicenseRecord 9343

    private static readonly Lazy<RewardTrackRules> Rules = new(() =>
    {
        string dir = HsServer.Engine.CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
        var calendar = new EventCalendar(Path.Combine(dir, "EventMap.json"), EventCalendar.FromRecording(Session))
            { Clock = () => new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc) };
        return new RewardTrackRules(dir, calendar);
    });

    private static PlayerRewardTrackStateUpdate RecordedLogin() =>
        PegasusPackets.Deserialize<PlayerRewardTrackStateUpdate>(Recording.Read(Session).First(p => p.Channel == "util" && p.IsIn && p.Type == 614).Body);

    private static PlayerAccount Player()
    {
        var store = new CollectionStore(null);
        store.Seed(new Collection { Stacks = new List<PegasusShared.CardStack>() }, new GameCurrencyStates());
        store.ShopBalance("XSH", 100);
        store.RewardTrackLogin(RecordedLogin());
        return new PlayerAccount
        {
            Name = "test", AccountId = 1, GameAccountId = 1, BattleTag = "Test#1", SaveData = new SaveDataStore(), Decks = new DeckStore(null), Collection = store,
        };
    }

    [Fact]
    public void The_current_global_track_is_live_s()
    {
        RewardTrackRules.Track track = Rules.Value.Current(RewardTrackRules.Global)!;
        Assert.Equal(175, track.Id);                                                          // Escape from Violet Hold, season 19
        Assert.Equal(TavernPass, track.PaidLicense);
        Assert.Equal(400, track.MaxLevel);
        Assert.Equal(1400, track.Levels[39].XpNeeded);
    }

    [Fact]
    public void The_login_adopts_live_s_tracks_once_then_sends_the_player_s()
    {
        var store = new CollectionStore(null);
        PlayerRewardTrackStateUpdate login = store.RewardTrackLogin(RecordedLogin());
        Assert.Equal(183, login.RewardTrackId);                                               // live's own (the Battlegrounds track)
        PlayerRewardTrackState track = login.State.Single(s => s.RewardTrackId == 175);
        Assert.Equal((39, 263), (track.Level, track.Xp));
        Assert.Equal(38, track.TrackLevel.Count);
        Assert.All(track.TrackLevel, l => Assert.Equal(RewardTracks.Acked, l.FreeRewardStatus));

        store.RewardTrack(175, s => { s.Xp = 999; return 0; });
        Assert.Equal(999, store.RewardTrackLogin(RecordedLogin()).State.Single(s => s.RewardTrackId == 175).Xp); // not adopted again
    }

    [Theory]
    [InlineData(7.83, true, 41)]   // live: Tavern Brawl, won, +41
    [InlineData(5.03, false, 18)]  // conceded, +18
    [InlineData(3.82, true, 19)]   // won, +19 (the formula gives 20)
    public void Game_xp_is_close_to_live(double minutes, bool won, int live)
    {
        const long tavernBrawl = 16; // BGT_TAVERNBRAWL_PVP
        int xp = RewardTrackRules.GameXp(tavernBrawl, TimeSpan.FromMinutes(minutes), won);
        Assert.InRange(xp, live - 1, live + 1);
        Assert.Equal(RewardTrackRules.GameXp(tavernBrawl, TimeSpan.FromMinutes(30), true),
                     RewardTrackRules.GameXp(tavernBrawl, TimeSpan.FromMinutes(90), true));            // capped at 30 min
        Assert.True(RewardTrackRules.GameXp(2, TimeSpan.FromMinutes(10), true) > RewardTrackRules.GameXp(4, TimeSpan.FromMinutes(10), true)); // ranked > practice
    }

    [Fact]
    public void Xp_levels_up_and_is_announced_like_live()
    {
        PlayerAccount player = Player();
        var tracks = new RewardTracks(Rules.Value, null, () => 100);
        var changes = tracks.AddXp(player.Collection, 1000, RewardTracks.FromQuest, 61);
        var xp = Assert.IsType<RewardTrackXpNotification>(changes[1]).XpChange.Single();
        Assert.Equal((1, 61, 263, 1263, 39, 39, 1), (xp.RewardSourceType, xp.RewardSourceId, xp.PrevXp, xp.CurrXp, xp.PrevLevel, xp.CurrLevel, xp.RewardTrackType));
        var state = Assert.IsType<PlayerRewardTrackStateUpdate>(changes[0]).State.Single();
        Assert.Equal((175, 39, 1263, true, 0), (state.RewardTrackId, state.Level, state.Xp, state.IsActiveRewardTrack, state.TrackLevel.Count));

        changes = tracks.AddXp(player.Collection, 200, RewardTracks.FromGame);                  // 1463 ≥ 1400: level 40, 63 XP
        xp = Assert.IsType<RewardTrackXpNotification>(changes[1]).XpChange.Single();
        Assert.Equal((39, 40, 63), (xp.PrevLevel, xp.CurrLevel, xp.CurrXp));
    }

    [Fact]
    public void The_tavern_pass_boosts_xp_as_the_client_computes_it()
    {
        RewardTrackRules.Track track = Rules.Value.Current(RewardTrackRules.Global)!;
        Assert.Equal(1.0, Rules.Value.XpMultiplier(track, 39, _ => false));
        double boost = Rules.Value.XpMultiplier(track, 100, l => l == TavernPass);
        Assert.InRange(boost, 1.1, 1.2);                                                       // wiki: 10% rising to 20%
    }

    [Fact]
    public void Claiming_gold_grants_it_and_marks_the_level_like_live()
    {
        PlayerAccount player = Player();
        var tracks = new RewardTracks(Rules.Value, null, () => 100);
        tracks.AddXp(player.Collection, 1200, RewardTracks.FromGame);                          // reach level 40 (rewards up to 40 claimable)
        Assert.Single(Rules.Value.RewardList(Rules.Value.Get(175)!.Levels[39].Lists[0])!.Items);

        var claimed = tracks.Claim(player, 175, 39, RewardTrackRules.Free, 0, out string refused)!;
        Assert.Null(refused);
        // live #533: the level's full state at GRANTED with the item (223: 50 gold), then 735 XSH +50
        PlayerRewardTrackState state = Assert.IsType<PlayerRewardTrackStateUpdate>(claimed.Changes[0]).State.Single();
        Assert.False(state.HasIsActiveRewardTrack || state.HasLevel || state.HasXp);
        PlayerRewardTrackLevelState level = state.TrackLevel.Single();
        Assert.Equal((39, RewardTracks.Granted, 0, 223), (level.Level, level.FreeRewardStatus, level.PaidRewardStatus, level.RewardItemOutput.Single().RewardItemId));
        var gold = claimed.Changes.OfType<VirtualCurrencyTransactionCompleteResponse>().Single();
        Assert.Equal(("XSH", 50L, true), (gold.CurrencyCode, gold.Amount, gold.Success));
        Assert.Equal(150, player.Collection.Balance(CollectionStore.Currency.Gold));

        Assert.Null(tracks.Claim(player, 175, 39, RewardTrackRules.Free, 0, out refused));        // once
        Assert.Contains("already", refused);
        Assert.True(tracks.Ack(player.Collection, 175, 39, RewardTrackRules.Free));
        Assert.Equal(RewardTracks.Acked, player.Collection.RewardTrack(175, s => s.Status(39, 0)));

        Assert.Null(tracks.Claim(player, 175, 60, RewardTrackRules.Free, 0, out refused));        // not reached
        Assert.Null(tracks.Claim(player, 175, 40, RewardTrackRules.Paid, 0, out refused));        // no Tavern Pass
        Assert.Contains("not owned", refused);
        player.Collection.GrantLicense(TavernPass);
        Assert.NotNull(tracks.Claim(player, 175, 40, RewardTrackRules.Paid, 0, out refused));
    }

    [Fact]
    public void Live_s_xp_bars_and_gold_grants_are_never_replayed()
    {
        // after its games live pushed 617 / 735 for its own XP and claims; replayed after ours they'd show live's numbers
        var notifications = RecordedAccount.Load(Session).Util.Values.SelectMany(l => l).SelectMany(e => e.Notifications).ToList();
        static IEnumerable<long> Types(byte[] raw) => raw == null ? Enumerable.Empty<long>()
            : Bgs.Parse<Blizzard.GameService.Protocol.Notification.V2.Client.NotificationReceivedNotification>(raw).Notifications
                .Select(n => n.Attribute.FirstOrDefault(a => a.Name == "message_type")?.Value?.IntValue ?? 0);
        var store = new CollectionStore(null);
        var sent = notifications.Select(n => LobbySession.RewriteLoginState(n, new DeckStore(null), null, out _, store)).SelectMany(Types).ToList();
        Assert.DoesNotContain(617L, sent);
        Assert.DoesNotContain(735L, sent);
    }
}
