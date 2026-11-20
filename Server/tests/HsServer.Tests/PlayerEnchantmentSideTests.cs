using HsServer.Engine;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsServer.Tests;

/// <summary>An enchantment on a player is that player's — "you", "friendly", "your turn" (Trigger.Side) — whoever created it (live Wild
/// Magic: both DALA_856e CONTROLLER 1, one on each player, each fires for its own player's spells). Guards the scripts that put one on
/// the opponent.</summary>
public class PlayerEnchantmentSideTests
{
    private static SabberStoneCore.Model.Game NewGame()
    {
        SabberEngineGame.EnsureCards(CardDb.FindDefaultDir(TestEnvironment.ProjectRoot));
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig { StartPlayer = 1, FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false, SkipMulligan = true });
        game.StartGame();
        return game;
    }

    [Fact]
    public void Mana_burn_takes_two_crystals_on_the_opponents_next_turn()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        for (int i = 0; i < 3; i++) { game.Process(EndTurnTask.Any(game.CurrentPlayer)); }
        var caster = game.CurrentPlayer;
        Generic.AddEnchantmentBlock(game, Cards.FromId("BT_753e"), caster.Hero, caster.Opponent);
        int casterMana = caster.RemainingMana;
        game.Process(EndTurnTask.Any(caster));
        var victim = game.CurrentPlayer;
        Assert.Equal(victim.BaseMana - 2, victim.RemainingMana);
        game.Process(EndTurnTask.Any(victim));
        Assert.Equal(caster.BaseMana, caster.RemainingMana); // the caster isn't burned
    }

    [Fact]
    public void Wild_magic_on_each_player_fires_for_that_players_spells_only()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        Generic.GiveEachPlayer(game, me.Hero, Cards.FromId("DALA_856e"));
        int before = me.HandZone.Count;
        var intellect = SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("CS2_023")); // Arcane Intellect: draw 2
        me.HandZone.Add(intellect);
        me.BaseMana = 10; me.UsedMana = 0;
        game.Process(PlayCardTask.Spell(me, intellect));
        Assert.Equal(before + 4, me.HandZone.Count); // cast twice (its own + one copy), not three times
    }
}
