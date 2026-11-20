using HsServer.Engine;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsServer.Tests;

/// <summary>Anomaly - Infused (DALA_867e on the player): "After a minion is summoned, it gains Taunt, Divine Shield, Rush, or Windfury at
/// random" — a played minion too (live session-20261013-233341 87032: the TRIGGER block right after the play gives the keyword).</summary>
public class InfusedAnomalyTests
{
    [Fact]
    public void A_played_minion_gains_one_of_the_keywords()
    {
        SabberEngineGame.EnsureCards(CardDb.FindDefaultDir(TestEnvironment.ProjectRoot));
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig { StartPlayer = 1, FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false, SkipMulligan = true });
        game.StartGame();
        var me = game.CurrentPlayer;
        Generic.AddEnchantmentBlock(game, Cards.FromId("DALA_867e"), me.Hero, me);
        var wisp = SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("CS2_231")); // Wisp, no keywords
        me.HandZone.Add(wisp);
        game.Process(PlayCardTask.Minion(me, wisp));
        var m = (Minion)wisp;
        Assert.True(m.HasTaunt || m.HasDivineShield || m.IsRush || m.HasWindfury, "no keyword");
    }
}
