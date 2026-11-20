using HsServer.Engine;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsServer.Tests;

/// <summary>Old Gods (BE): Titans with four abilities and a second action after the first ability of a turn (Actions/Titans.cs); other
/// cards' "Choose a C'Thun ability to trigger" (Generic.ChooseOldGodAbility) — live session-20261106-001808, 20261021-013733,
/// 20261109-160826, 20261023-152211.</summary>
public class OldGodTests
{
    private static SabberStoneCore.Model.Game NewGame()
    {
        SabberEngineGame.EnsureCards(CardDb.FindDefaultDir(TestEnvironment.ProjectRoot));
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig { StartPlayer = 1, FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false, SkipMulligan = true, FormatType = FormatType.FT_WILD });
        game.StartGame();
        return game;
    }

    private static Minion PlayCthun(SabberStoneCore.Model.Game game)
    {
        var me = game.CurrentPlayer;
        me.BaseMana = 10; me.UsedMana = 0;
        var cthun = (Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("BE_013"));
        me.HandZone.Add(cthun);
        Assert.True(game.Process(PlayCardTask.Minion(me, cthun)));
        return cthun;
    }

    [Fact]
    public void Cthun_has_four_abilities_and_acts_twice_then_once_left()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        Minion cthun = PlayCthun(game);
        Assert.True(Generic.IsOldGod(cthun));
        Assert.Equal(4, Generic.TitanAbilityCount(cthun));
        Assert.True(game.Process(TitanAbilityTask.Any(me, cthun, 3))); // Servants
        Assert.Equal(1, cthun[GameTag.EXTRA_ATTACKS_THIS_TURN]);
        Assert.Equal(2, me.BoardZone.Count(m => m.Card.Id == "BE_013t5"));
        Assert.True(Generic.TitanCanAct(cthun));
        Assert.True(game.Process(TitanAbilityTask.Any(me, cthun, 0))); // Wrath: the second action
        Assert.False(Generic.TitanCanAct(cthun));
        Assert.Equal(1, me[Generic.CthunAbilityUsed[0]]);
        Assert.Equal(1, me[Generic.CthunAbilityUsed[3]]);
    }

    [Fact]
    public void The_fourth_ability_is_not_quadrupled_the_next_ones_are()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        Minion cthun = PlayCthun(game);
        foreach (GameTag t in new[] { Generic.CthunAbilityUsed[0], Generic.CthunAbilityUsed[2], Generic.CthunAbilityUsed[3] }) me[t] = 1;
        Assert.Equal(1, Generic.CthunTimes(cthun));
        me[Generic.CthunAbilityUsed[1]] = 1;
        Assert.Equal(4, Generic.CthunTimes(cthun));
    }

    [Fact]
    public void Tentacle_lash_discounts_cthun_even_for_copies_that_arrive_later()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        me.BaseMana = 10; me.UsedMana = 0;
        var axe = (Weapon)SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("CS2_106")); // Fiery War Axe 3/2
        me.HandZone.Add(axe);
        Assert.True(game.Process(PlayCardTask.Any(me, axe)));
        var lash = SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("BE_771"));
        me.HandZone.Add(lash);
        Assert.True(game.Process(PlayCardTask.Any(me, lash)));
        Assert.True(game.Process(HeroAttackTask.Any(me, me.Opponent.Hero)));
        var later = SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("BE_013"));
        Generic.AddHandPhase.Invoke(me, later); // generated to hand after the attack already happened
        Assert.Equal(later.Card.Cost - 1, later.Cost); // already carries the accumulated discount
    }

    [Fact]
    public void Yogg_saron_ends_the_turn_and_takes_an_extra_one_once()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        me.BaseMana = 10; me.UsedMana = 0;
        var yogg = (Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("BE_009"));
        me.HandZone.Add(yogg);
        int turn = game.Turn;
        Assert.True(game.Process(PlayCardTask.Minion(me, yogg)));
        Assert.Same(me, game.CurrentPlayer); // the extra turn
        Assert.Equal(turn + 1, game.Turn);
        Assert.True(Generic.AiControls(me));
        Assert.Equal(1, yogg[Generic.YOGG_FOG]);
        Assert.Contains(me.AppliedEnchantments, e => e.Card.Id == "BE_009e2");
        game.Process(EndTurnTask.Any(me));
        Assert.NotSame(me, game.CurrentPlayer);
        Assert.False(Generic.AiControls(me));
        Assert.Equal(0, me[Generic.REPLACE_NAMEPLATE]);
        Assert.DoesNotContain(me.AppliedEnchantments ?? new List<Enchantment>(), e => e.Card.Id == "BE_009e2");
    }

    [Fact]
    public void Brain_of_yogg_saron_makes_the_next_spell_cast_twice()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        me.BaseMana = 10; me.UsedMana = 0;
        Generic.TriggerAbility(me, me.Hero, Cards.FromId("BE_009t1"), null);
        var bolt = SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("CS2_029")); // Fireball: 6
        me.HandZone.Add(bolt);
        int health = me.Opponent.Hero.Health;
        Assert.True(game.Process(PlayCardTask.SpellTarget(me, bolt, me.Opponent.Hero)));
        Assert.Equal(health - 12, me.Opponent.Hero.Health);
    }

    [Fact]
    public void Tome_translator_deciphers_after_two_of_its_turns_in_hand()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        var tome = SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("BE_327"));
        me.HandZone.Add(tome);
        Assert.Equal(2, tome[GameTag.TAG_SCRIPT_DATA_NUM_1]);
        game.Process(EndTurnTask.Any(me)); game.Process(EndTurnTask.Any(game.CurrentPlayer));
        Assert.Equal("BE_327", tome.Card.Id);
        Assert.Equal(1, tome[GameTag.TAG_SCRIPT_DATA_NUM_1]);
        game.Process(EndTurnTask.Any(me)); game.Process(EndTurnTask.Any(game.CurrentPlayer));
        Assert.Contains(tome.Card.Id, new[] { "BE_327t", "BE_327t1", "BE_327t2", "BE_327t3" });
    }

    [Fact]
    public void An_undeciphered_tome_translator_becomes_a_random_version_when_played()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        me.BaseMana = 10; me.UsedMana = 0;
        var tome = (Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("BE_327"));
        me.HandZone.Add(tome);
        Assert.True(game.Process(PlayCardTask.Minion(me, tome)));
        Assert.Equal(Zone.PLAY, tome.Zone.Type);
        Assert.Contains(tome.Card.Id, new[] { "BE_327t", "BE_327t1", "BE_327t2", "BE_327t3" });
    }

    [Fact]
    public void Frontline_ironborn_grows_when_an_enemy_minion_dies_on_its_turn()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        var iron = (Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("BE_996"));
        me.BoardZone.Add(iron);
        var enemy = (Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me.Opponent, Cards.FromId("CS2_182"));
        me.Opponent.BoardZone.Add(enemy);
        int atk = iron.AttackDamage;
        Generic.Destroy(enemy, null);
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(atk + 2, iron.AttackDamage);
        Assert.Contains(Generic.BonusEffectKeywords, k => Generic.HasBonusEffect(iron, k));
    }

    [Fact]
    public void Uldir_quarantine_opens_once_each_rune_was_played_while_it_stands()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        me.BaseMana = 10; me.UsedMana = 0;
        var gate = (Location)SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("BE_780"));
        me.HandZone.Add(gate);
        Assert.True(game.Process(PlayCardTask.Any(me, gate)));
        Assert.True(gate.RuneLocked);
        Assert.False(gate.CanActivate);
        var second = SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("BE_780")); // Blood, Frost and Unholy at once
        me.HandZone.Add(second);
        Assert.True(game.Process(PlayCardTask.Any(me, second)));
        Assert.False(gate.RuneLocked);
        Assert.True(((Location)second).RuneLocked);
    }

    [Fact]
    public void Uldir_quarantine_opens_while_still_in_hand()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        me.BaseMana = 10; me.UsedMana = 0;
        var gate = (Location)SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("BE_780"));
        me.HandZone.Add(gate); // stays in hand the whole time
        var second = SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("BE_780")); // Blood, Frost and Unholy at once
        me.HandZone.Add(second);
        Assert.True(game.Process(PlayCardTask.Any(me, second)));
        Assert.False(gate.RuneLocked); // ticked off while still held, not on the board (user, 2026-11-20)
        Assert.True(game.Process(PlayCardTask.Any(me, gate)));
        Assert.False(gate.RuneLocked); // the pips survive the hand -> play zone change
    }

    [Fact]
    public void Fetid_devourer_takes_the_last_rune_cards_runes()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        me.BaseMana = 10; me.UsedMana = 0;
        var fetid = SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("BE_781"));
        me.HandZone.Add(fetid);
        var gate = SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("BE_780")); // one of each rune
        me.HandZone.Add(gate);
        Assert.True(game.Process(PlayCardTask.Any(me, gate)));
        Assert.Equal("BE_781t1", fetid.Card.Id); // Blood, Frost, Unholy
    }

    [Fact]
    public void Discs_of_norgannon_stacks_spells_on_top_and_offers_the_bottom_draw()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        me.BaseMana = 10; me.UsedMana = 0;
        var bolt = SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("CS2_029"));
        me.DeckZone.Add(bolt, 0); // a spell at the bottom
        var discs = SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("BE_250"));
        me.HandZone.Add(discs);
        Assert.True(game.Process(PlayCardTask.Spell(me, discs)));
        Assert.True(me.DeckZone.TopCard is Spell);
        IPlayable bottom = me.DeckZone[0];
        game.Process(EndTurnTask.Any(me)); game.Process(EndTurnTask.Any(game.CurrentPlayer));
        Assert.NotNull(me.Choice);
        int shift = me.Choice.Choices.First(id => game.IdEntityDic[id].Card.Id == "BE_250t2");
        game.Process(ChooseTask.Pick(me, shift));
        Assert.Equal(Zone.HAND, bottom.Zone?.Type);
    }

    [Fact]
    public void Assimilating_ooze_makes_no_copy_if_it_died_before_turn_end()
    {
        // user, 2026-11-20: a copy of something dead can't exist, same as Echoing Ooze (FP1_003, ECHOING_OOZE_SPELL dies with it)
        var game = NewGame();
        var me = game.CurrentPlayer;
        me.BaseMana = 10; me.UsedMana = 0;
        var ooze = (Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("BE_734"));
        me.HandZone.Add(ooze);
        Assert.True(game.Process(PlayCardTask.Minion(me, ooze)));
        Generic.Destroy(ooze, null);
        game.DeathProcessingAndAuraUpdate();
        Assert.DoesNotContain(me.BoardZone, m => m.Card.Id == "BE_734");
        game.Process(EndTurnTask.Any(me));
        Assert.DoesNotContain(me.BoardZone, m => m.Card.Id == "BE_734");
        Assert.Empty(game.EndOfThisTurn);
    }

    [Fact]
    public void Assimilating_ooze_copies_itself_at_the_end_of_this_turn_if_it_survives()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        me.BaseMana = 10; me.UsedMana = 0;
        var ooze = (Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("BE_734"));
        me.HandZone.Add(ooze);
        Assert.True(game.Process(PlayCardTask.Minion(me, ooze)));
        game.Process(EndTurnTask.Any(me));
        Assert.Equal(2, me.BoardZone.Count(m => m.Card.Id == "BE_734"));
        Assert.Empty(game.EndOfThisTurn);
    }

    [Fact]
    public void Watch_over_offers_a_second_different_ability()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        me.BaseMana = 10; me.UsedMana = 0;
        var watch = SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("BE_778"));
        me.HandZone.Add(watch);
        Assert.True(game.Process(PlayCardTask.Spell(me, watch)));
        Assert.NotNull(me.Choice);
        int wrath = me.Choice.Choices.First(id => game.IdEntityDic[id].Card.Id == "BE_013t1");
        game.Process(ChooseTask.Pick(me, wrath));
        Assert.NotNull(me.Choice);
        Assert.DoesNotContain(me.Choice.Choices, id => game.IdEntityDic[id].Card.Id == "BE_013t1");
        int servants = me.Choice.Choices.First(id => game.IdEntityDic[id].Card.Id == "BE_013t4");
        game.Process(ChooseTask.Pick(me, servants));
        Assert.Equal(2, me.BoardZone.Count(m => m.Card.Id == "BE_013t5"));
    }
}
