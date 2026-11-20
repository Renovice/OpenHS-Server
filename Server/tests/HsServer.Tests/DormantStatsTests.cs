using HsServer.Engine;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace HsServer.Tests;

/// <summary>A Dormant minion shows its printed stats; its buffs come back when it awakens (live session-20261022-020357 96500: Warden
/// Maiev's +3/+3 on Wailing Vapor, 4/6 → Dormant 1/3 → awake 4/6; wiki Dormant: enchantments are not reset).</summary>
public class DormantStatsTests
{
    [Fact]
    public void A_buffed_minion_shows_printed_stats_while_dormant_and_gets_its_buffs_back()
    {
        SabberEngineGame.EnsureCards(CardDb.FindDefaultDir(TestEnvironment.ProjectRoot));
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig { StartPlayer = 1, FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false, SkipMulligan = true });
        game.StartGame();
        var me = game.CurrentPlayer;
        var m = (Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("CS2_172")); // 3/2
        me.BoardZone.Add(m);
        Generic.AddEnchantmentBlock(game, Cards.FromId("JAIL_850e2"), me.Hero, m, 0, 0, 0); // +3/+3
        Assert.Equal((6, 5), (m.AttackDamage, m.Health));
        Generic.MakeDormant(m, 1);
        Assert.Equal((3, 2), (m.AttackDamage, m.Health));
        Generic.Awaken(m);
        Assert.Equal((6, 5), (m.AttackDamage, m.Health));
    }
}
