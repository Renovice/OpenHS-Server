using HsServer.Engine;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;
using SabberStoneCore.Tasks.SimpleTasks;

namespace HsServer.Tests;

/// <summary>The Dalaran Heist, bosses 51-75 (CardSets/HsServer/DalaranHeistCardsGenHs3.cs, Actions/DalaranHeistGroup3.cs): the
/// trickier Hero Powers and boss cards.</summary>
public class DalaranHeistGroup3Tests
{
    private static readonly Lazy<CardDb> Db = new(() =>
    {
        string dir = CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
        SabberEngineGame.EnsureCards(dir);
        return CardDb.Load(dir);
    });

    private static SabberStoneCore.Model.Game TenManaGame()
    {
        _ = Db.Value;
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.DRUID, Player2HeroClass = CardClass.MAGE,
            FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false,
        });
        game.StartGame();
        game.Player1.BaseMana = 10;
        game.Player2.BaseMana = 10;
        return game;
    }

    private static IPlayable Give(Controller c, string id) => Generic.DrawCard(c, Cards.FromId(id));

    private static Minion OnBoard(SabberStoneCore.Model.Game game, Controller c, string id)
    {
        var m = (Minion)SabberStoneCore.Model.Entities.Entity.FromCard(c, Cards.FromId(id));
        Generic.SummonBlock.Invoke(game, m, -1, null);
        m[GameTag.NUM_TURNS_IN_PLAY] = 1;
        m.IsExhausted = false;
        return m;
    }

    private static void SetPower(SabberStoneCore.Model.Game game, Controller c, string id) =>
        new ReplaceHeroPower(Cards.FromId(id)).Process(game, c, c.Hero, null);

    private static void UsePower(SabberStoneCore.Model.Game game, Controller c, ICharacter? target = null)
    {
        c.UsedMana = 0;
        Assert.True(game.Process(target == null ? HeroPowerTask.Any(c) : HeroPowerTask.Any(c, target)));
    }

    private static void EndTurn(SabberStoneCore.Model.Game game) => Assert.True(game.Process(EndTurnTask.Any(game.CurrentPlayer)));

    [Fact]
    public void Every_group3_card_has_its_script()
    {
        _ = Db.Value;
        foreach (string id in new[] { "DALA_BOSS_51p", "DALA_BOSS_52px", "DALA_BOSS_53p", "DALA_BOSS_54p", "DALA_BOSS_55p", "DALA_BOSS_56p",
                     "DALA_BOSS_57p", "DALA_BOSS_58p", "DALA_BOSS_59p", "DALA_BOSS_60p", "DALA_BOSS_61p", "DALA_BOSS_62p", "DALA_BOSS_62px",
                     "DALA_BOSS_63p", "DALA_BOSS_64px", "DALA_BOSS_65p", "DALA_BOSS_66p", "DALA_BOSS_67p", "DALA_BOSS_68px", "DALA_BOSS_69p",
                     "DALA_BOSS_70px", "DALA_BOSS_71px", "DALA_BOSS_72p", "DALA_BOSS_73px", "DALA_BOSS_74p", "DALA_504", "DALA_503",
                     "BRMA01_3", "DALA_502", "LOOTA_BOSS_26p3", "LOOTA_BOSS_26p5", "LOOTA_109", "KARA_00_08", "ICCA01_007", "ICCA01_011" })
        {
            Card card = Cards.FromId(id);
            Assert.True(card.Power != null && (card.Power.PowerTask != null || card.Power.Trigger != null || card.Power.Aura != null), id);
        }
    }

    [Fact]
    public void Squirrel_bomb_blows_up_at_its_owners_turn_start()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        SetPower(game, me, "DALA_BOSS_51p");
        UsePower(game, me);
        Minion bomb = Assert.Single(me.BoardZone.GetAll());
        Assert.Equal("DALA_BOSS_51t", bomb.Card.Id);
        var enemy = OnBoard(game, me.Opponent, "CS2_200"); // Boulderfist Ogre 6/7
        EndTurn(game);
        EndTurn(game);
        Assert.Empty(me.BoardZone.GetAll());
        Assert.Equal(2, enemy.Damage);
        Assert.Equal(28, me.Opponent.Hero.Health);
    }

    [Fact]
    public void Invest_heroic_returns_buffs_and_copies_in_two_power_blocks()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        SetPower(game, me, "DALA_BOSS_64px");
        var yeti = OnBoard(game, me, "CS2_182"); // Chillwind Yeti 4/5
        int mark = game.PowerHistory.Full.Count;
        UsePower(game, me, yeti);
        var inHand = me.HandZone.GetAll().Where(p => p.Card.Id == "CS2_182").ToList();
        Assert.Equal(2, inHand.Count);
        Assert.All(inHand, p => Assert.Equal(8, ((Minion)p).AttackDamage));
        var powerBlocks = game.PowerHistory.Full.Skip(mark).OfType<PowerHistoryBlockStart>()
            .Where(b => b.BlockType == BlockType.POWER && b.Source == me.Hero.HeroPower.Id).ToList();
        Assert.Equal(2, powerBlocks.Count);
        Assert.Equal(1, powerBlocks[1].Index);
    }

    [Fact]
    public void Kirin_tor_elementals_each_add_a_cast()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        OnBoard(game, me, "DALA_504");
        OnBoard(game, me, "DALA_504");
        me.UsedMana = 0;
        var bolt = Give(me, "CS2_029"); // Fireball 6
        Assert.True(game.Process(PlayCardTask.SpellTarget(me, bolt, me.Opponent.Hero)));
        Assert.Equal(30 - 18, me.Opponent.Hero.Health);
    }

    [Fact]
    public void Impervious_caps_a_hit_on_the_hero_at_three()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        SetPower(game, me.Opponent, "DALA_BOSS_65p");
        me.UsedMana = 0;
        var bolt = Give(me, "CS2_029");
        Assert.True(game.Process(PlayCardTask.SpellTarget(me, bolt, me.Opponent.Hero)));
        Assert.Equal(27, me.Opponent.Hero.Health);
    }

    [Fact]
    public void Steelskin_prevents_own_damage_only()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        SetPower(game, me, "DALA_BOSS_72p");
        me.UsedMana = 0;
        var bolt = Give(me, "CS2_029");
        Assert.True(game.Process(PlayCardTask.SpellTarget(me, bolt, me.Hero)));
        Assert.Equal(30, me.Hero.Health);
        me.UsedMana = 0;
        var bolt2 = Give(me.Opponent, "CS2_029");
        EndTurn(game);
        me.Opponent.UsedMana = 0;
        Assert.True(game.Process(PlayCardTask.SpellTarget(me.Opponent, bolt2, me.Hero)));
        Assert.Equal(24, me.Hero.Health);
    }

    [Fact]
    public void Bombardment_reloads_for_two_turns()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        SetPower(game, me, "DALA_BOSS_70px");
        var enemy = OnBoard(game, me.Opponent, "CS2_200");
        UsePower(game, me);
        Assert.Equal(3, enemy.Damage);
        Assert.True(me.HeroPowerDisabled);
        for (int turn = 0; turn < 2; turn++)
        {
            EndTurn(game);
            EndTurn(game);
            Assert.True(me.HeroPowerDisabled, $"reloading, turn {turn}");
        }
        EndTurn(game);
        EndTurn(game);
        Assert.False(me.HeroPowerDisabled);
    }

    [Fact]
    public void Dragonwrath_counts_up_spell_damage()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        SetPower(game, me, "DALA_BOSS_69p");
        UsePower(game, me);
        EndTurn(game);
        EndTurn(game);
        UsePower(game, me);
        Assert.Equal(2, me.CurrentSpellPower);
        Assert.Equal(2, me.Hero.HeroPower[GameTag.TAG_SCRIPT_DATA_NUM_1]);
    }

    [Fact]
    public void Blackmail_extortion_gives_back_the_same_card()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        foreach (var p in op.HandZone.GetAll().ToList()) Generic.RemoveFromHand(op, p);
        var yeti = Give(op, "CS2_182");
        SetPower(game, me, "DALA_BOSS_59p");
        UsePower(game, me);
        var extortion = Assert.Single(op.HandZone.GetAll());
        Assert.Equal("DALA_BOSS_59t3", extortion.Card.Id);
        Assert.Equal(yeti.Id, extortion[GameTag.TAG_SCRIPT_DATA_NUM_1]);
        EndTurn(game);
        op.UsedMana = 0;
        Assert.True(game.Process(PlayCardTask.Spell(op, extortion)));
        Assert.Contains(yeti, op.HandZone.GetAll());
        Assert.DoesNotContain(op.HandZone.GetAll(), p => p.Card.Id == "DALA_BOSS_59t3");
    }

    [Fact]
    public void Greed_is_good_buys_the_first_card_each_turn()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        SetPower(game, me.Opponent, "DALA_BOSS_57p");
        int bossHand = me.Opponent.HandZone.Count;
        Give(me, "CS2_182");
        Give(me, "CS2_182");
        var cards = me.HandZone.GetAll().Where(p => p.Card.Id == "CS2_182").ToList();
        me.UsedMana = 0;
        Assert.True(game.Process(PlayCardTask.Minion(me, cards[0])));
        me.UsedMana = 0;
        Assert.True(game.Process(PlayCardTask.Minion(me, cards[1])));
        Assert.Equal(bossHand + 1, me.Opponent.HandZone.Count);
        Assert.Equal(1, me.HandZone.GetAll().Count(p => p.Card.Id == "GVG_028t"));
    }

    [Fact]
    public void Dark_iron_bouncer_always_wins_brawls()
    {
        for (int seed = 0; seed < 6; seed++)
        {
            var game = TenManaGame();
            var me = game.CurrentPlayer;
            for (int i = 0; i < 3; i++) OnBoard(game, me, "CS2_182");
            var bouncer = OnBoard(game, me.Opponent, "BRMA01_3");
            OnBoard(game, me.Opponent, "CS2_200");
            me.UsedMana = 0;
            var brawl = Give(me, "EX1_407");
            Assert.True(game.Process(PlayCardTask.Spell(me, brawl)));
            Assert.Same(bouncer, Assert.Single(me.BoardZone.GetAll().Concat(me.Opponent.BoardZone.GetAll())));
        }
    }

    [Fact]
    public void Next_swaps_the_leftmost_card_with_a_draw()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        SetPower(game, me, "DALA_BOSS_66p");
        IPlayable left = me.HandZone[0];
        int hand = me.HandZone.Count, deck = me.DeckZone.Count, drawn = me.NumCardsDrawnThisTurn;
        UsePower(game, me);
        Assert.Equal(Zone.DECK, left.Zone?.Type);
        Assert.Equal(hand, me.HandZone.Count);
        Assert.Equal(deck, me.DeckZone.Count);
        Assert.Equal(drawn + 1, me.NumCardsDrawnThisTurn);
    }

    [Fact]
    public void Bribed_minion_goes_back_to_the_opponents_hand()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        SetPower(game, me, "DALA_BOSS_56p");
        int deck = me.Opponent.DeckZone.Count;
        UsePower(game, me);
        Minion stolen = Assert.Single(me.BoardZone.GetAll());
        Assert.Equal(deck - 1, me.Opponent.DeckZone.Count);
        int hand = me.Opponent.HandZone.Count;
        Generic.Destroy(stolen, null);
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(hand + 1, me.Opponent.HandZone.Count);
        Assert.Equal(stolen.Card.Id, me.Opponent.HandZone[me.Opponent.HandZone.Count - 1].Card.Id);
    }

    [Fact]
    public void Darken_wears_off_at_the_users_next_turn()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        SetPower(game, me, "DALA_BOSS_62p");
        var mine = OnBoard(game, me, "CS2_182");
        var theirs = OnBoard(game, me.Opponent, "CS2_182");
        UsePower(game, me);
        Assert.Equal(2, mine.AttackDamage);
        Assert.Equal(2, theirs.AttackDamage);
        EndTurn(game);
        Assert.Equal(2, theirs.AttackDamage);
        EndTurn(game);
        Assert.Equal(4, mine.AttackDamage);
        Assert.Equal(4, theirs.AttackDamage);
    }

    [Fact]
    public void Kirin_tor_guards_count_each_other()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        SetPower(game, me, "DALA_BOSS_55p");
        UsePower(game, me);
        EndTurn(game);
        EndTurn(game);
        UsePower(game, me);
        Assert.All(me.BoardZone.GetAll(), m => Assert.Equal(2, m.AttackDamage));
    }

    [Fact]
    public void Eager_rogue_deals_no_damage_attacking()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var rogue = OnBoard(game, me, "ICCA01_007");
        var target = OnBoard(game, me.Opponent, "CS2_200");
        Assert.True(game.Process(MinionAttackTask.Any(me, rogue, target)));
        Assert.Equal(0, target.Damage);
        Assert.NotEqual(Zone.PLAY, rogue.Zone?.Type); // it still takes the hit back (6 damage kills it)
    }
    [Fact]
    public void Spell_lock_overloads_both_players_for_the_cost()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        SetPower(game, me.Opponent, "DALA_BOSS_54p");
        me.UsedMana = 0;
        var bolt = Give(me, "CS2_029");
        Assert.True(game.Process(PlayCardTask.SpellTarget(me, bolt, me.Opponent.Hero)));
        Assert.Equal(4, me.OverloadOwed);
    }

    [Fact]
    public void Mega_portal_summons_a_minion_of_the_spells_cost()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        SetPower(game, me, "DALA_BOSS_68px");
        me.UsedMana = 0;
        var bolt = Give(me, "CS2_029");
        Assert.True(game.Process(PlayCardTask.SpellTarget(me, bolt, me.Opponent.Hero)));
        Minion m = Assert.Single(me.BoardZone.GetAll());
        Assert.Equal(4, m.Card.Cost);
    }

    [Fact]
    public void Boogie_woogie_resummons_and_hold_the_gates_taunts()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var yeti = OnBoard(game, me, "CS2_182");
        SetPower(game, me, "DALA_BOSS_52px");
        UsePower(game, me, yeti);
        Assert.Equal(9, yeti.Health);
        Assert.True(yeti.HasTaunt);
        SetPower(game, me, "DALA_BOSS_53p");
        UsePower(game, me, yeti);
        Generic.Destroy(yeti, null);
        game.DeathProcessingAndAuraUpdate();
        Minion back = Assert.Single(me.BoardZone.GetAll());
        Assert.Equal("CS2_182", back.Card.Id);
        Assert.NotSame(yeti, back);
        Assert.Equal(5, back.Health);
    }

    [Fact]
    public void Soul_weave_discards_and_summons()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        foreach (var p in me.HandZone.GetAll().ToList()) Generic.RemoveFromHand(me, p);
        Give(me, "CS2_182");
        SetPower(game, me, "DALA_BOSS_74p");
        UsePower(game, me);
        Assert.Empty(me.HandZone.GetAll());
        Assert.Equal(4, Assert.Single(me.BoardZone.GetAll()).Card.Cost);
    }
}
