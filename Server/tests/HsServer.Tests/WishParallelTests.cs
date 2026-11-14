using Xunit;

namespace HsServer.Tests;

/// <summary>Zephrys the Great's wish judged in parallel gives the one-by-one result (2026-10-09: 6 s → about 1 s cold, 50 ms warm).</summary>
public class WishParallelTests
{
    [Fact]
    public void A_parallel_wish_offers_what_a_one_by_one_wish_offers()
    {
        HsServer.Sabber.SabberEngineGame.EnsureCards(HsServer.Engine.CardDb.FindDefaultDir(TestEnvironment.ProjectRoot));
        var g = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = SabberStoneCore.Enums.CardClass.MAGE, Player2HeroClass = SabberStoneCore.Enums.CardClass.HUNTER,
            FillDecks = true, History = true, Shuffle = false, SkipMulligan = true,
        });
        g.StartGame();
        foreach (string id in new[] { "CS2_182", "CS2_172", "EX1_556", "CORE_REV_845", "CORE_REV_845", "CORE_REV_845" })
            SabberStoneCore.Model.Entities.Entity.FromCard(g.Player1, SabberStoneCore.Model.Cards.FromId(id), zone: g.Player1.BoardZone);
        foreach (string id in new[] { "CS2_182", "CS2_172", "CS2_168", "EX1_284", "CORE_REV_845" })
            SabberStoneCore.Model.Entities.Entity.FromCard(g.Player2, SabberStoneCore.Model.Cards.FromId(id), zone: g.Player2.BoardZone);
        foreach (var m in g.Player1.BoardZone) { m[SabberStoneCore.Enums.GameTag.NUM_TURNS_IN_PLAY] = 1; m.IsExhausted = false; }
        try
        {
            foreach (int mana in new[] { 7, 3, 10 })
            {
                g.Player1.BaseMana = mana;
                SabberStoneCore.Actions.Generic.WishParallel = false;
                var oneByOne = SabberStoneCore.Actions.Generic.WishOffer(g, g.Player1).Select(c => c.Id).ToList();
                SabberStoneCore.Actions.Generic.WishParallel = true;
                var parallel = SabberStoneCore.Actions.Generic.WishOffer(g, g.Player1).Select(c => c.Id).ToList();
                Assert.Equal(oneByOne, parallel);
                Assert.Equal(3, parallel.Count);
            }
        }
        finally { SabberStoneCore.Actions.Generic.WishParallel = true; }
    }
}
