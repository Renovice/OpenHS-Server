using HsServer.Engine;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;
using SGame = SabberStoneCore.Model.Game;

namespace HsServer.Tests;

/// <summary>Rule differences `HsServer livecheck` found against live recordings (2026-10-10).</summary>
public class LiveCheckFixTests
{
    private static readonly Lazy<CardDb> Db = new(() =>
    {
        string dir = CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
        SabberEngineGame.EnsureCards(dir);
        return CardDb.Load(dir);
    });

    private static readonly string[] Filler = { "CS2_118", "CS2_118", "CS2_124", "CS2_124", "CS2_201", "CS2_201", "CS2_186", "CS2_186", "CS2_168", "CS2_168" };

    private static SGame Game(CardClass p1 = CardClass.MAGE, CardClass p2 = CardClass.WARRIOR)
    {
        _ = Db.Value;
        var game = new SGame(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = p1, Player2HeroClass = p2,
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
        m[GameTag.NUM_TURNS_IN_PLAY] = 1;
        m.IsExhausted = false;
        return m;
    }

    [Fact]
    public void Duplicate_triggers_once_when_two_friendly_minions_die_together()
    {
        // Advanced rulebook Rule 7: Death Event triggers queue per Death; once revealed, the Secret is gone for the next Death
        var game = Game(CardClass.MAGE, CardClass.MAGE);
        var me = game.Player1;
        var foe = me.Opponent;
        Assert.True(game.Process(EndTurnTask.Any(me)));
        foe.BaseMana = 10;
        foe.UsedMana = 0;
        IPlayable duplicate = Generic.DrawCard(foe, Cards.FromId("FP1_018"));
        Assert.True(game.Process(PlayCardTask.Spell(foe, duplicate)));
        Assert.Single(foe.SecretZone);
        Assert.True(game.Process(EndTurnTask.Any(foe)));
        me.UsedMana = 0;
        OnBoard(game, foe, "CS2_168"); // Murloc Raider 2/1
        OnBoard(game, foe, "CS2_168");
        int hand = foe.HandZone.Count;
        IPlayable arcaneExplosion = Generic.DrawCard(me, Cards.FromId("CS2_025")); // 1 damage to all enemy minions
        Assert.True(game.Process(PlayCardTask.Spell(me, arcaneExplosion)));
        Assert.Equal(hand + 2, foe.HandZone.Count); // one Duplicate: 2 copies, not 4
        Assert.Empty(foe.SecretZone);
    }

    [Fact]
    public void Gallywix_does_not_copy_his_own_coin()
    {
        var game = Game();
        var me = game.Player1;
        OnBoard(game, me.Opponent, "GVG_028"); // the opponent's Gallywix
        IPlayable coin = Generic.DrawCard(me, Cards.FromId("GVG_028t"));
        int theirHand = me.Opponent.HandZone.Count;
        Assert.True(game.Process(PlayCardTask.Spell(me, coin)));
        Assert.Equal(theirHand, me.Opponent.HandZone.Count); // "(Won't trigger Gallywix.)"
        IPlayable arcane = Generic.DrawCard(me, Cards.FromId("CS2_025"));
        int myHand = me.HandZone.Count;
        Assert.True(game.Process(PlayCardTask.Spell(me, arcane)));
        Assert.Equal(theirHand + 1, me.Opponent.HandZone.Count); // a real spell is copied
        Assert.Equal(myHand, me.HandZone.Count); // ...and I get a Coin (the Arcane Explosion left my hand)
    }

    [Fact]
    public void Soldier_of_Fortune_gives_one_coin_per_attack()
    {
        var game = Game();
        var me = game.Player1;
        var soldier = OnBoard(game, me, "DAL_771");
        int theirHand = me.Opponent.HandZone.Count;
        Assert.True(game.Process(MinionAttackTask.Any(me, soldier, me.Opponent.Hero)));
        Assert.Equal(theirHand + 1, me.Opponent.HandZone.Count);
        Assert.Equal("GAME_005", me.Opponent.HandZone[^1].Card.Id);
    }
    [Fact]
    public void Mugs_Magic_discount_leaves_the_other_minions_after_the_first()
    {
        // "Your first minion each turn costs (2) less": once one is played, the rest of the hand is back to full cost
        var game = Game();
        var me = game.Player1;
        new SabberStoneCore.Tasks.SimpleTasks.AddEnchantmentTask("JAIL_800hp1e", SabberStoneCore.Tasks.SimpleTasks.EntityType.CONTROLLER).Process(game, me, me.Hero, null);
        IPlayable first = Generic.DrawCard(me, Cards.FromId("CS2_186")); // War Golem (7)
        IPlayable second = Generic.DrawCard(me, Cards.FromId("JAIL_805t")); // Stormfury Elemental (7)
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(5, first.Cost);
        Assert.Equal(5, second.Cost);
        Assert.True(game.Process(PlayCardTask.Minion(me, first)));
        Assert.Equal(7, second.Cost);
    }
}
