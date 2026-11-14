using HsServer.Engine;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsServer.Tests;

/// <summary>TITANS' shared engine rules (Actions/Titans.cs): Titans, Sigils, Magnetic from the client data, Blossom, excess
/// attack damage. The cards' own scripts aren't needed: these are data-driven rules.</summary>
public class TitansMechanicsTests
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

    private static IPlayable Give(Controller c, string id) => Generic.DrawCard(c, SabberStoneCore.Model.Cards.FromId(id));

    private static void PassTurns(SabberStoneCore.Model.Game game, int n)
    {
        for (int i = 0; i < n; i++)
        {
            game.CurrentPlayer.UsedMana = 0;
            Assert.True(game.Process(EndTurnTask.Any(game.CurrentPlayer)));
        }
    }

    [Fact]
    public void A_titan_uses_its_abilities_instead_of_attacking_then_attacks()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        // Eonar, the Life-Binder: three untargeted abilities (Aman'Thul's Strike from History now needs an enemy minion, live 48008)
        var titan = (Minion)Give(me, "TTN_903");
        Assert.True(titan.Card.IsTitan);
        Assert.Equal(3, Generic.TitanAbilityCount(titan));
        var abilities = Enumerable.Range(0, 3).Select(i => Generic.TitanAbility(titan, i)).ToList();
        Assert.All(abilities, a => Assert.NotNull(a));
        Assert.Equal(new[] { "TTN_903t", "TTN_903t2", "TTN_903t3" }, abilities.Select(a => a.Card.Id));
        Assert.All(abilities, a => Assert.Equal(titan.Id, a[GameTag.PARENT_CARD]));

        Assert.True(game.Process(PlayCardTask.Minion(me, titan)));
        Assert.False(titan.IsExhausted); // hidden Charge
        var options = me.Options();
        Assert.Contains(options, o => o is TitanAbilityTask);
        Assert.DoesNotContain(options, o => o is MinionAttackTask a && a.Source == titan);

        // the client's view: one POWER option on the Titan, keyword 4786, its abilities as sub-options
        var all = PowerOptionsBuilder.AllOptions(game, options);
        var titanOption = all.PowerOptionList.Single(o => o.MainOption?.EntityId == titan.Id && o.MainOption.PowerKeyword == 4786);
        Assert.Equal(3, titanOption.SubOptions.Count);

        var second = options.OfType<TitanAbilityTask>().First(t => t.AbilityIndex == 1 && (t.AbilityTarget == null || true));
        int historyMark = game.PowerHistory.Full.Count;
        Assert.True(game.Process(second));
        Assert.Equal(1, titan[Generic.TITAN_ABILITY_USED_2]);
        Assert.Equal(0, titan[Generic.TITAN_ABILITY_USED_1]);
        Assert.Equal(1, abilities[1][HsTag.LITERALLY_UNPLAYABLE]);
        Assert.True(titan.IsExhausted);
        Assert.DoesNotContain(me.Options(), o => o is TitanAbilityTask);
        // live wire: PLAY (SubOption 1) > POWER (SubOption 1) ... TRIGGER effect index 1
        var blocks = game.PowerHistory.Full.Skip(historyMark).OfType<PowerHistoryBlockStart>().Where(b => b.Source == titan.Id).ToList();
        Assert.Contains(blocks, b => b.BlockType == BlockType.PLAY && b.SubOption == 1);
        Assert.Contains(blocks, b => b.BlockType == BlockType.POWER && b.SubOption == 1);
        Assert.Contains(blocks, b => b.BlockType == BlockType.TRIGGER && b.Index == 1);

        PassTurns(game, 2);
        Assert.True(game.Process(me.Options().OfType<TitanAbilityTask>().First(t => t.AbilityIndex == 0)));
        PassTurns(game, 2);
        Assert.Single(me.Options().OfType<TitanAbilityTask>().Select(t => t.AbilityIndex).Distinct());
        Assert.True(game.Process(me.Options().OfType<TitanAbilityTask>().First(t => t.AbilityIndex == 2)));
        Assert.False(Generic.TitanHasAbilitiesLeft(titan));
        PassTurns(game, 2);
        Assert.Contains(me.Options(), o => o is MinionAttackTask a && a.Source == titan);
    }

    [Fact]
    public void The_ai_plans_with_titan_abilities()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var titan = (Minion)Give(me, "TTN_429");
        Assert.True(game.Process(PlayCardTask.Minion(me, titan)));
        var ai = new HsServer.Sabber.Ai.PlannerAi { Replies = 0, BudgetMs = 500 };
        var moves = new List<string>();
        for (int i = 0; i < 10 && game.State != State.COMPLETE && game.CurrentPlayer == me; i++)
        {
            var task = ai.Step(game, me);
            moves.Add(task.FullPrint());
            Assert.True(game.Process(task), task.FullPrint());
        }
        Assert.NotEmpty(moves);
    }

    [Fact]
    public void A_bounced_titan_gets_fresh_abilities_and_a_copy_keeps_their_state()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var titan = (Minion)Give(me, "TTN_429");
        Assert.True(game.Process(PlayCardTask.Minion(me, titan)));
        Assert.True(game.Process(me.Options().OfType<TitanAbilityTask>().First(t => t.AbilityIndex == 2)));
        var copy = (Minion)Generic.Copy(me, titan, titan, Zone.PLAY);
        Assert.Equal(1, copy[Generic.TITAN_ABILITY_USED_3]);
        Assert.NotEqual(Generic.TitanAbility(titan, 2).Id, Generic.TitanAbility(copy, 2).Id);
        Assert.Equal(1, Generic.TitanAbility(copy, 2)[HsTag.LITERALLY_UNPLAYABLE]);

        var oldAbility = Generic.TitanAbility(titan, 2);
        Assert.NotNull(Generic.ReturnToHand(me, titan));
        Assert.Equal(0, titan[Generic.TITAN_ABILITY_USED_3]);
        Assert.Equal((int)Zone.REMOVEDFROMGAME, oldAbility[GameTag.ZONE]);
        Assert.NotEqual(oldAbility.Id, Generic.TitanAbility(titan, 2).Id);
    }

    [Fact]
    public void A_silenced_titan_attacks_like_a_minion()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var titan = (Minion)Give(me, "TTN_429");
        Assert.True(game.Process(PlayCardTask.Minion(me, titan)));
        titan.Silence();
        Assert.False(Generic.IsTitan(titan));
        Assert.DoesNotContain(me.Options(), o => o is TitanAbilityTask);
        PassTurns(game, 2);
        Assert.Contains(me.Options(), o => o is MinionAttackTask a && a.Source == titan);
    }

    [Fact]
    public void A_sigil_waits_in_the_secret_zone_and_acts_next_turn()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var sigil = Give(me, "BAR_705"); // Sigil of Silence
        Assert.True(sigil.Card.IsSigil);
        Assert.True(game.Process(PlayCardTask.Spell(me, sigil)));
        Assert.Equal(Zone.SECRET, sigil.Zone.Type);
        PassTurns(game, 1);
        Assert.Equal(Zone.SECRET, sigil.Zone.Type);
        PassTurns(game, 1);
        Assert.Equal(Zone.GRAVEYARD, sigil.Zone.Type);
    }

    [Fact]
    public void A_sparkbot_magnetizes_from_the_client_data()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var mech = (Minion)Give(me, "GVG_085"); // Annoy-o-Tron
        Assert.True(game.Process(PlayCardTask.Minion(me, mech)));
        int atk = mech.AttackDamage, health = mech.BaseHealth;
        var sparkbot = (Minion)Give(me, "TTN_719t4"); // Sparkbot: Magnetic, Poisonous
        Assert.True(game.Process(PlayCardTask.Any(me, sparkbot, null, 0)));
        Assert.Equal(Zone.SETASIDE, sparkbot.Zone.Type);
        Assert.Equal(atk + 1, mech.AttackDamage);
        Assert.Equal(health + 1, mech.BaseHealth);
        Assert.True(mech.Poisonous);
        Assert.Contains(mech.AppliedEnchantments, e => e.Card.Id == "TTN_719e4");
    }

    [Fact]
    public void A_blossom_card_changes_after_its_turns_in_hand()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var seedling = Give(me, "TTN_930"); // Frost Lotus Seedling: Blossoms in 3 turns
        Assert.True(Generic.Blossoms(seedling.Card));
        Assert.Equal("TTN_930t", Generic.BlossomVersion(seedling.Card).Id);
        Assert.Equal(3, seedling[GameTag.TAG_SCRIPT_DATA_NUM_1]);
        PassTurns(game, 2);
        Assert.Equal(2, seedling[GameTag.TAG_SCRIPT_DATA_NUM_1]);
        PassTurns(game, 4);
        var now = game.IdEntityDic[seedling.Id];
        Assert.Equal("TTN_930t", now.Card.Id);
    }

    [Fact]
    public void A_cost_floor_comes_after_every_other_cost_change()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var cheap = Give(me, "CS2_168"); // Murloc Raider (1)
        Assert.Equal(1, cheap.Cost);
        me.ControllerAuraEffects[HsTag.COST_FLOOR_SOURCES] = 1;
        me.ControllerAuraEffects[HsTag.COST_FLOOR_SUM] = 2;
        Assert.Equal(2, Generic.CostFloor(me));
        Assert.Equal(2, cheap.Cost);
        Assert.Equal(2, me.Hero.HeroPower.Cost); // a Hero Power isn't a card: unchanged (its own 2)
    }

    [Fact]
    public void A_crafted_weapon_keeps_its_base_stats_and_takes_its_parts()
    {
        var look = SabberStoneCore.Model.Cards.FromId("TTN_060t");
        var crafted = Generic.CraftedCard(look, SabberStoneCore.Model.Cards.FromId("TTN_060t3"), SabberStoneCore.Model.Cards.FromId("TTN_060t12"));
        Assert.Equal(look.Cost, crafted.Cost);
        Assert.Equal(look.ATK, crafted.ATK);
        Assert.Equal(look[GameTag.DURABILITY], crafted[GameTag.DURABILITY]);
        Assert.Equal(1, crafted[GameTag.POISONOUS]);
        Assert.Equal(1, crafted[GameTag.BATTLECRY]);
        Assert.Equal(SabberStoneCore.Model.Cards.FromId("TTN_060t3").AssetId, crafted[GameTag.MODULAR_ENTITY_PART_1]);
        Assert.Equal(CardType.WEAPON, crafted.Type);
    }

    [Fact]
    public void A_drawn_spell_is_cast_again_as_a_new_copy()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var granter = Give(me, "CS2_168");
        var fireball = Give(me, "CS2_029");
        int heroHealth = me.Opponent.Hero.Health;
        var copy = Generic.CastDrawnCopy(me, granter, fireball, keepInHand: true, enemiesIfPossible: true);
        Assert.NotNull(copy);
        Assert.NotEqual(fireball.Id, copy.Id);
        Assert.Equal(Zone.HAND, fireball.Zone.Type);
        Assert.Equal(Zone.GRAVEYARD, copy.Zone.Type);
        Assert.True(me.Opponent.Hero.Health < heroHealth || me.Opponent.BoardZone.Count > 0);
    }

    [Fact]
    public void Overload_unlocks_and_armor_is_paid()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.OverloadOwed = 2;
        Assert.Equal(2, Generic.UnlockOverload(me));
        Assert.Equal(0, me.OverloadOwed);
        me.Hero.GainArmor(me.Hero, 5);
        Assert.False(Generic.PayArmor(me, me.Hero, 6));
        Assert.True(Generic.PayArmor(me, me.Hero, 4));
        Assert.Equal(1, me.Hero.Armor);
        Assert.True(Generic.ArmorChangedThisTurn(me));
    }

    [Fact]
    public void Excess_attack_damage_hits_the_enemy_hero()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var big = (Minion)Give(me, "CS2_186"); // War Golem 7/7
        Assert.True(game.Process(PlayCardTask.Minion(me, big)));
        PassTurns(game, 1);
        var small = (Minion)Give(me.Opponent, "CS2_168"); // Murloc Raider 2/1
        Assert.True(game.Process(PlayCardTask.Minion(me.Opponent, small)));
        PassTurns(game, 1);
        big[HsTag.EXCESS_ATTACK_DAMAGE_TO_HERO] = 1;
        int heroHealth = me.Opponent.Hero.Health;
        Assert.True(game.Process(MinionAttackTask.Any(me, big, small)));
        Assert.Equal(heroHealth - (big.AttackDamage - 1), me.Opponent.Hero.Health);
    }
}
