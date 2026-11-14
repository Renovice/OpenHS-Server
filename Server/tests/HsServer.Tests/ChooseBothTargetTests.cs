using HsServer.Engine;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;
using SGame = SabberStoneCore.Model.Game;

namespace HsServer.Tests;

/// <summary>Choose One cards with both effects combined take the target their targeted options need (wiki Fandral Staghelm:
/// "Choose One cards with targeted effects will use the same target for both effects"; user 2026-10-09: a combined Boomkin
/// healed but its "Deal 4 damage" did nothing — the play had no target).</summary>
public class ChooseBothTargetTests
{
    private static readonly Lazy<CardDb> Db = new(() =>
    {
        string dir = CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
        SabberEngineGame.EnsureCards(dir);
        return CardDb.Load(dir);
    });

    private static readonly string[] Filler = { "CS2_118", "CS2_118", "CS2_124", "CS2_124", "CS2_201", "CS2_201", "CS2_186", "CS2_186", "CS2_168", "CS2_168" };

    private static SGame Game()
    {
        _ = Db.Value;
        var game = new SGame(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.DRUID, Player2HeroClass = CardClass.WARRIOR,
            Player1Deck = Filler.Select(Cards.FromId).ToList(), Player2Deck = Filler.Select(Cards.FromId).ToList(),
            FillDecks = false, History = true, Shuffle = false, SkipMulligan = true,
        });
        game.StartGame();
        game.Player1.BaseMana = 10;
        game.Player1.UsedMana = 0;
        return game;
    }

    private static Minion OnBoard(SGame game, Controller c, string id)
    {
        var m = (Minion)SabberStoneCore.Model.Entities.Entity.FromCard(c, Cards.FromId(id));
        Generic.SummonBlock.Invoke(game, m, -1, null);
        return m;
    }

    [Fact]
    public void A_combined_Boomkin_targets_and_does_both()
    {
        var game = Game();
        var me = game.Player1;
        var golem = OnBoard(game, me.Opponent, "CS2_186"); // War Golem 7/7
        me.Hero.Damage = 10;
        IPlayable boomkin = Generic.DrawCard(me, Cards.FromId("CORE_ONY_018"));
        boomkin[GameTag.CHOOSE_BOTH] = 1; // Raid Negotiator's "It has both effects combined"
        var plays = me.Options().OfType<PlayCardTask>().Where(o => o.Source == boomkin).ToList();
        Assert.Contains(plays, o => o.Target == golem); // the combined card offers its option's targets
        Assert.True(game.Process(PlayCardTask.MinionTarget(me, boomkin, golem)));
        Assert.Equal(2, me.Hero.Damage);   // Restore 8 Health to your hero
        Assert.Equal(4, golem.Damage);     // Deal 4 damage — to the chosen target
    }

    [Fact]
    public void Fandral_makes_Wrath_hit_one_minion_with_both()
    {
        var game = Game();
        var me = game.Player1;
        OnBoard(game, me, "OG_044"); // Fandral Staghelm: your Choose One cards have both effects combined
        var golem = OnBoard(game, me.Opponent, "CS2_186");
        int hand = me.HandZone.Count;
        IPlayable wrath = Generic.DrawCard(me, Cards.FromId("EX1_154")); // Deal 3 damage to a minion; or 1 damage and draw a card
        Assert.True(game.Process(PlayCardTask.SpellTarget(me, wrath, golem)));
        Assert.Equal(4, golem.Damage);          // 3 + 1, the same target
        Assert.Equal(hand + 1, me.HandZone.Count); // Wrath gone, a card drawn
    }
}
