using HsServer.Lobby;
using HsServer.Protocol;
using PegasusUtil;

namespace HsServer.Tests;

/// <summary>C4 quests against the live capture live-20261002-progression (wire-conventions "Lobby: quests").</summary>
public class QuestTests
{
    private static readonly string Session = Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures", "live-20261002-progression", "session-20261002-133808.jsonl");
    private static readonly DateTime RecordedAt = new(2026, 10, 2, 11, 38, 23, DateTimeKind.Utc); // the login's 602

    private static readonly Lazy<QuestRules> Rules = new(() =>
    {
        string dir = HsServer.Engine.CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
        var calendar = new EventCalendar(Path.Combine(dir, "EventMap.json"), EventCalendar.FromRecording(Session))
            { Clock = () => new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc) };
        return new QuestRules(dir, calendar);
    });

    private static byte[] Recorded(int type) => Recording.Read(Session).First(p => p.Channel == "util" && p.IsIn && p.Type == type).Body;

    // a player who logged in with live's quests, and a clock the test moves
    private static (Quests quests, CollectionStore store, Action<TimeSpan> wait) LoggedIn()
    {
        DateTime now = RecordedAt;
        var quests = new Quests(Rules.Value, clock: () => now);
        var store = new CollectionStore(null);
        quests.PoolsLogin(store, PegasusPackets.Deserialize<PlayerQuestPoolStateUpdate>(Recorded(602)), RecordedAt);
        quests.QuestsLogin(store, PegasusPackets.Deserialize<PlayerQuestStateUpdate>(Recorded(601)));
        return (quests, store, t => now += t);
    }

    [Fact]
    public void The_login_adopts_live_s_quests_and_pools_once()
    {
        var (quests, store, _) = LoggedIn();
        PlayerQuestPoolStateUpdate pools = quests.PoolsLogin(store, null, RecordedAt);
        var daily = pools.QuestPool.Single(p => p.QuestPoolId == 1);
        Assert.Equal((37298, 1), (daily.SecondsUntilNextGrant, daily.RerollAvailableCount));
        Assert.Equal(3, pools.QuestPool.Single(p => p.QuestPoolId == 18).BankedQuestCount);
        PlayerQuestStateUpdate login = quests.QuestsLogin(store, null);
        Assert.Equal(8, login.Quest.Count);
        Assert.Equal((Quests.Active, 14), (login.Quest.Single(q => q.QuestId == 1032).Status, login.Quest.Single(q => q.QuestId == 1032).Progress));
    }

    [Fact]
    public void A_pool_grants_its_quests_when_its_time_comes()
    {
        var (quests, store, wait) = LoggedIn();
        Assert.Empty(quests.Refresh(store));                                                  // not yet
        wait(TimeSpan.FromSeconds(37298));                                                    // the daily's time
        var changes = quests.Refresh(store);
        var pool = changes.OfType<PlayerQuestPoolStateUpdate>().Single().QuestPool.Single();
        Assert.Equal((1, 86400, 1), (pool.QuestPoolId, pool.SecondsUntilNextGrant, pool.RerollAvailableCount));
        PlayerQuestState granted = changes.OfType<PlayerQuestStateUpdate>().Single().Quest.Single();
        Assert.Equal(Quests.New, granted.Status);
        Assert.Equal(1, Rules.Value.Get(granted.QuestId).Pool);
        Assert.NotEqual(61, granted.QuestId);                                                 // not one the player holds
        Assert.Empty(quests.Refresh(store));                                                  // once
    }

    [Fact]
    public void A_reroll_replaces_the_quest_once_per_period_like_live()
    {
        var (quests, store, _) = LoggedIn();
        var changes = quests.Reroll(store, 1036);                                             // live: 1036 -> 1035
        var response = (RerollQuestResponse)changes[0];
        Assert.True(response.Success);
        Assert.Equal(2, Rules.Value.Get(response.GrantedQuestId).Pool);
        Assert.Equal(0, changes.OfType<PlayerQuestPoolStateUpdate>().Single().QuestPool.Single(p => p.QuestPoolId == 2).RerollAvailableCount);
        var states = changes.OfType<PlayerQuestStateUpdate>().Single().Quest;
        Assert.Equal(Quests.Rerolled, states.Single(q => q.QuestId == 1036).Status);
        Assert.Equal(Quests.New, states.Single(q => q.QuestId == response.GrantedQuestId).Status);
        Assert.False(((RerollQuestResponse)quests.Reroll(store, 1032)[0]).Success);           // no reroll left this week
    }

    [Fact]
    public void A_grant_into_a_full_pool_is_lost_and_rerolls_come_back_daily()
    {
        // wiki Quests: "If a player already has 3 quests when the reset time is reached, the potential new quest will simply
        // be lost"; "The player can replace one Daily quest and one Weekly quest each day."
        var (quests, store, wait) = LoggedIn();
        Assert.True(((RerollQuestResponse)quests.Reroll(store, 1036)[0]).Success);           // the weekly reroll used
        wait(TimeSpan.FromSeconds(37298));
        var day1 = quests.Refresh(store);                                                     // 2 dailies; rerolls back
        Assert.Equal(1, day1.OfType<PlayerQuestPoolStateUpdate>().Single().QuestPool.Single(p => p.QuestPoolId == 2).RerollAvailableCount);
        wait(TimeSpan.FromDays(1));
        quests.Refresh(store);                                                                // 3: full
        wait(TimeSpan.FromDays(0.5));
        var full = quests.Refresh(store);                                                     // (the weekly's time came too)
        wait(TimeSpan.FromDays(0.5));
        full = quests.Refresh(store);
        Assert.DoesNotContain(full.OfType<PlayerQuestStateUpdate>().SelectMany(u => u.Quest), q => Rules.Value.Get(q.QuestId).Pool == 1);
        Assert.Equal(0, full.OfType<PlayerQuestPoolStateUpdate>().Single().QuestPool.Single(p => p.QuestPoolId == 1).BankedQuestCount);
    }

    [Fact]
    public void An_event_quest_chain_starts_goes_on_and_can_be_abandoned()
    {
        // Break the Chains (1951) → Worship Them (1952), event legendary_quest_36_6 (on at the test clock)
        DateTime now = RecordedAt;
        var quests = new Quests(Rules.Value, clock: () => now);
        var store = new CollectionStore(null);
        quests.PoolsLogin(store, null, RecordedAt);
        quests.QuestsLogin(store, null);
        var started = quests.Refresh(store).OfType<PlayerQuestStateUpdate>().SelectMany(u => u.Quest).ToList();
        Assert.Contains(started, q => q.QuestId == 1951 && q.Status == Quests.New);
        Assert.Empty(quests.Refresh(store).OfType<PlayerQuestStateUpdate>().SelectMany(u => u.Quest).Where(q => q.QuestId == 1951)); // once
        var (_, completed) = quests.SetProgress(store, 1951, 1, add: false);
        Assert.Equal(1951, Assert.Single(completed).Id);
        var next = quests.Ack(store, 1951).OfType<PlayerQuestStateUpdate>().SelectMany(u => u.Quest).ToList();
        Assert.Contains(next, q => q.QuestId == 1952 && q.Status == Quests.New);
        var dropped = quests.Abandon(store, 1952).OfType<PlayerQuestStateUpdate>().Single().Quest.Single();
        Assert.Equal((1952, Quests.Abandoned), (dropped.QuestId, dropped.Status));
        Assert.Empty(quests.Abandon(store, 1035));                                          // not held / can't be dropped
    }

    // ---------- step 3: what quests count ----------

    private static readonly Lazy<HsServer.Engine.CardDb> Cards = new(() => HsServer.Engine.CardDb.Load(HsServer.Engine.CardDb.FindDefaultDir(TestEnvironment.ProjectRoot)));

    private static readonly Lazy<QuestConditions> Conditions = new(() => new QuestConditions(
        Path.Combine(TestEnvironment.ProjectRoot, "Server", "data", "quest-conditions.tsv"), Cards.Value,
        HsServer.Engine.CardDb.FindDefaultDir(TestEnvironment.ProjectRoot)));

    private static int Counts(int questId, HsServer.Game.GameTally tally) =>
        Conditions.Value.Count(Conditions.Value.For(Rules.Value.Get(questId)), tally);

    private static HsServer.Game.GameTally Game(int heroClass = 4, bool conceded = false, int health = 30, int turns = 12, params HsServer.Game.TallyEvent[] events)
    {
        var tally = new HsServer.Game.GameTally { HeroClass = heroClass, Conceded = conceded, HealthAtEnd = health, OwnTurns = turns };
        tally.Events.AddRange(events);
        return tally;
    }

    [Fact]
    public void A_game_counts_unless_conceded_early()
    {
        // quest 61 "Play 3 games as Mage, Warlock, or Warrior"; wiki: a conceded game counts only at 15 Health or less or
        // after 10 turns
        Assert.Equal(1, Counts(61, Game(heroClass: 4)));                                     // Mage
        Assert.Equal(0, Counts(61, Game(heroClass: 6)));                                     // Priest
        Assert.Equal(0, Counts(61, Game(heroClass: 9, conceded: true, health: 30, turns: 3)));
        Assert.Equal(1, Counts(61, Game(heroClass: 9, conceded: true, health: 15, turns: 3)));
        Assert.Equal(1, Counts(61, Game(heroClass: 10, conceded: true, health: 30, turns: 10)));
    }

    [Fact]
    public void Quests_count_the_game_events_their_text_names()
    {
        var t = Game(events: new[]
        {
            new HsServer.Game.TallyEvent(HsServer.Game.GameTally.ManaSpent, null, 27),
            new HsServer.Game.TallyEvent(HsServer.Game.GameTally.HeroPowerUsed, null, 4),
            new HsServer.Game.TallyEvent(HsServer.Game.GameTally.CardPlayed, "CS2_029", 1, 4),   // Fireball (Fire)
            new HsServer.Game.TallyEvent(HsServer.Game.GameTally.CardPlayed, "CS2_024", 1, 2),   // Frostbolt (Frost)
            new HsServer.Game.TallyEvent(HsServer.Game.GameTally.CardPlayed, "CS2_231", 1, 0),   // Wisp
        });
        Assert.Equal(27, Counts(1035, t));                                                   // Spend $q mana
        Assert.Equal(4, Counts(1032, t));                                                    // Use your Hero Power
        int fireOrShadow = AllQuests().First(q => q.Description.StartsWith("Cast $q Fire or Shadow")).Id;
        Assert.Equal(1, Counts(fireOrShadow, t));
        var cheap = AllQuests().First(q => q.Pool is 1 or 2 && System.Text.RegularExpressions.Regex.IsMatch(q.Description, @"^Play \$q cards that cost \d+ or less\.$"));
        int n = int.Parse(System.Text.RegularExpressions.Regex.Match(cheap.Description, @"\d+").Value);
        Assert.Equal(new[] { 4, 2, 0 }.Count(cost => cost <= n), Counts(cheap.Id, t));       // the cards that cost N or less
    }

    private static IEnumerable<QuestRules.Quest> AllQuests() => Enumerable.Range(1, 5000).Select(Rules.Value.Get).Where(q => q != null);

    [Fact]
    public void Every_grantable_daily_and_weekly_quest_counts_something_or_is_another_mode()
    {
        // left without a condition: modes we don't run yet (Battlegrounds, Arena, Duels, Brawl, friends), Forge (not in
        // the engine yet: now done) and Drink spells (waiting for the client data)
        string[] otherModes = { "Battlegrounds", "Arena", "Duels", "Tavern Brawl", "friend", "Spectator" };
        var missing = Rules.Value.StandardPools.SelectMany(p => Rules.Value.Grantable(p.Id))
            .Where(q => Conditions.Value.For(q) == null && !otherModes.Any(m => q.Description.Contains(m)))
            .Select(q => $"{q.Id}: {q.Description}").ToList();
        Assert.True(missing.Count == 0, "no condition for: " + string.Join(" | ", missing));
    }

    [Fact]
    public void An_engine_game_reports_its_tally()
    {
        HsServer.Sabber.SabberEngineGame.EnsureCards(HsServer.Engine.CardDb.FindDefaultDir(TestEnvironment.ProjectRoot));
        var rng = new Random(11);
        var engine = new HsServer.Sabber.SabberEngineGame(new HsServer.Game.EngineSetup
        {
            Cards = Cards.Value, Scenario = 260, Seed = 11, Rules = HsServer.Game.RulesEngine.Sabber,
            GameSetupBody = PegasusPackets.Serialize(new PegasusGame.GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
            Human = new HsServer.Engine.PlayerSetup { HeroCardId = HsServer.Sabber.RandomDecks.AnyHero(SabberStoneCore.Enums.CardClass.PRIEST, rng),
                Deck = HsServer.Sabber.RandomDecks.BuildCurrentCore(SabberStoneCore.Enums.CardClass.PRIEST, rng), AccountHi = 1, AccountLo = 2 },
            Opponent = new HsServer.Engine.PlayerSetup { HeroCardId = HsServer.Sabber.RandomDecks.AnyHero(SabberStoneCore.Enums.CardClass.MAGE, rng),
                Deck = HsServer.Sabber.RandomDecks.BuildCurrentCore(SabberStoneCore.Enums.CardClass.MAGE, rng), IsAi = true },
        });
        engine.Start();
        engine.ConcedeHuman();                                                                // at once: an AFK farm game
        HsServer.Game.GameTally tally = engine.Tally();
        Assert.True(engine.Over);
        Assert.Equal((int)SabberStoneCore.Enums.CardClass.PRIEST, tally.HeroClass);
        Assert.True(tally.Conceded);
        Assert.False(tally.CountsAsPlayed);
        Assert.Equal(30, tally.StartingDeck.Count);
    }
}
