using HsServer.Engine;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace HsServer.Tests;

/// <summary>Rule 4a: the Death Creation Step kills every mortally wounded (0 or less Health) minion — one brought there by a Health loss
/// too, not only by damage (livecheck 2026-11-16: Bloated Leech's steal killed damaged minions on live, ours left them at 0).</summary>
public class MortallyWoundedTests
{
    [Fact]
    public void A_minion_at_zero_health_from_a_health_loss_dies()
    {
        SabberEngineGame.EnsureCards(CardDb.FindDefaultDir(TestEnvironment.ProjectRoot));
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig { StartPlayer = 1, FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false, SkipMulligan = true });
        game.StartGame();
        var opp = game.CurrentPlayer.Opponent;
        var m = (Minion)SabberStoneCore.Model.Entities.Entity.FromCard(opp, Cards.FromId("CS2_172")); // 3/2
        opp.BoardZone.Add(m);
        m.Damage = 1;
        Generic.AddEnchantmentBlock(game, Cards.FromId("EDR_810e"), game.CurrentPlayer.Hero, m, -1, 0, 0);
        Assert.Equal(0, m.Health);
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(Zone.GRAVEYARD, m.Zone?.Type);
    }
}
