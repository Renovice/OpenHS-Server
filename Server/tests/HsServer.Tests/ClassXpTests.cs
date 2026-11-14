using HsServer.Game;
using HsServer.Lobby;
using PegasusShared;
using PegasusUtil;

namespace HsServer.Tests;

/// <summary>Class levels (HeroXP 283): live's levels adopted once, XP per game (ClassXpRules, UNCLEAR: fitted to the two
/// recorded gains), level-ups and the cap.</summary>
public class ClassXpTests
{
    private static CollectionStore Store()
    {
        var store = new CollectionStore(null);
        store.Seed(new Collection { Stacks = new List<CardStack>() }, new GameCurrencyStates());
        return store;
    }

    private static HeroXP Live() => new()
    {
        XpInfos =
        {
            new HeroXPInfo { ClassId = 1, Level = 45, CurrXp = 1614, MaxXp = 1730 },  // Death Knight (live-20260930)
            new HeroXPInfo { ClassId = 14, Level = 56, CurrXp = 1305, MaxXp = 1730 }, // Demon Hunter (live-20261001)
            new HeroXPInfo { ClassId = 2, Level = 60, CurrXp = 9999, MaxXp = 9999 },
        },
    };

    private static GameTally Game(int plays, int attacks, int kills, bool won = false, bool conceded = false, int health = 10, int turns = 8)
    {
        var tally = new GameTally { HeroClass = 1, Won = won, Conceded = conceded, HealthAtEnd = health, OwnTurns = turns };
        for (int i = 0; i < plays; i++) tally.Events.Add(new TallyEvent(GameTally.CardPlayed, "CS2_182", 1, 4));
        for (int i = 0; i < attacks; i++) tally.Events.Add(new TallyEvent(GameTally.Attacked, "CS2_182", 1));
        tally.Events.Add(new TallyEvent(GameTally.EnemyMinionDied, null, kills));
        tally.Events.Add(new TallyEvent(GameTally.MinionSummoned, "CS2_182", 1)); // not counted
        return tally;
    }

    [Fact]
    public void Live_levels_are_adopted_once_then_the_players_own()
    {
        CollectionStore store = Store();
        HeroXP first = store.ClassXpLogin(Live());
        Assert.Equal((45L, 1614L, 1730L), (first.XpInfos[0].Level, first.XpInfos[0].CurrXp, first.XpInfos[0].MaxXp));
        store.AddClassXp(1, 7);
        HeroXP again = store.ClassXpLogin(new HeroXP()); // a later recording is not taken over again
        Assert.Equal(1621L, again.XpInfos.Single(x => x.ClassId == 1).CurrXp);
        Assert.Equal((60L, 9999L, 9999L), (again.XpInfos[2].Level, again.XpInfos[2].CurrXp, again.XpInfos[2].MaxXp));
    }

    [Fact]
    public void The_recorded_gains_are_reproduced()
    {
        // live: Practice losses (Demon Hunter, Death Knight), Casual against players (Death Knight, live-20261003-dk-classxp)
        Assert.Equal(41, ClassXpRules.GameXp(Game(25, 14, 2, conceded: true, turns: 8, health: 12), solo: true));
        Assert.InRange(ClassXpRules.GameXp(Game(5, 2, 1, conceded: true, turns: 6, health: 9), solo: true), 7, 8);
        Assert.Equal(54, ClassXpRules.GameXp(Game(13, 5, 6, conceded: true, turns: 8, health: 14), solo: false));
        Assert.InRange(ClassXpRules.GameXp(Game(16, 22, 2, won: true), solo: false), 94, 98);
        // live: conceded at 30 Health after 5 and 1 turns: nothing (the game doesn't count)
        Assert.Equal(0, ClassXpRules.GameXp(Game(2, 0, 1, conceded: true, health: 30, turns: 3), solo: false));
    }

    [Fact]
    public void Levels_go_up_and_stop_at_60()
    {
        CollectionStore store = Store();
        store.ClassXpLogin(Live());
        Assert.Equal((45, 1614, 46, 34), store.AddClassXp(1, 150));  // 1764 - 1730
        Assert.Equal((60, 9999, 60, 9999), store.AddClassXp(2, 50)); // capped
        store.AddClassXp(14, 1730 * 10);
        HeroXP now = store.ClassXpLogin(null);
        Assert.Equal((60L, 9999L), (now.XpInfos.Single(x => x.ClassId == 14).Level, now.XpInfos.Single(x => x.ClassId == 14).CurrXp));
        Assert.Equal((1, 0, 1, 20), store.AddClassXp(5, 20)); // a class not in the recording starts at level 1
    }
}
