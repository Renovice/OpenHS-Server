using HsServer.Engine;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsServer.Tests;

/// <summary>Warlock Rituals (Generic.RitualTrigger): live session-20261021-013733 / 20261021-194927 — in the Secret zone, a tick at the
/// start of each of its owner's turns, the effect on the second, RITUALS_COMPLETED +1 (player tag 4976), then gone; Kith'ix begins a
/// Sinister Ritual once one was completed.</summary>
public class RitualTests
{
    private static SabberStoneCore.Model.Game NewGame()
    {
        SabberEngineGame.EnsureCards(CardDb.FindDefaultDir(TestEnvironment.ProjectRoot));
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig { StartPlayer = 1, FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false, SkipMulligan = true, FormatType = FormatType.FT_WILD });
        game.StartGame();
        return game;
    }

    [Fact]
    public void Ritual_of_summoning_summons_in_two_turns_and_counts_as_completed()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        me.BaseMana = 10; me.UsedMana = 0;
        var ritual = SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("BE_085"));
        me.HandZone.Add(ritual);
        Assert.True(game.Process(PlayCardTask.Spell(me, ritual)));
        Assert.Equal(Zone.SECRET, ritual.Zone.Type);
        int board = me.BoardZone.Count;
        game.Process(EndTurnTask.Any(game.CurrentPlayer)); game.Process(EndTurnTask.Any(game.CurrentPlayer)); // my next turn: 1
        Assert.Equal(Zone.SECRET, ritual.Zone.Type);
        Assert.Equal(board, me.BoardZone.Count);
        game.Process(EndTurnTask.Any(game.CurrentPlayer)); game.Process(EndTurnTask.Any(game.CurrentPlayer)); // 2: done
        Assert.Equal(Zone.GRAVEYARD, ritual.Zone.Type);
        Assert.Equal(2, me.BoardZone.Count(m => m.Card.Id == "BE_085t"));
        Assert.Equal(1, me[Generic.RITUALS_COMPLETED]);

        me.BaseMana = 10; me.UsedMana = 0;
        var kithix = SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("BE_090"));
        me.HandZone.Add(kithix);
        Assert.True(game.Process(PlayCardTask.Minion(me, kithix)));
        Assert.NotNull(me.Choice);
        Assert.Equal(3, me.Choice.Choices.Count);
        game.Process(ChooseTask.Pick(me, me.Choice.Choices[2]));
        Assert.Contains(me.SecretZone, s => s.Card.Id == "BE_090t3");
    }

    [Fact]
    public void Kithix_without_a_completed_ritual_does_nothing()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        me.BaseMana = 10; me.UsedMana = 0;
        var kithix = SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("BE_090"));
        me.HandZone.Add(kithix);
        Assert.True(game.Process(PlayCardTask.Minion(me, kithix)));
        Assert.Null(me.Choice);
    }
}
