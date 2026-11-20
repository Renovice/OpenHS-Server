using HsServer.Engine;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsServer.Tests;

/// <summary>Commander Bolan (DALA_BOSS_75h): Immune, no turn draw, "A Quest!" gives one of five quests (enchantments on his hero, a quest
/// may come again), each grows at the start of his turns, a completed quest ends the battle — live session-20261007-113244 and
/// 20260929-122856, wiki Commander_Bolan.</summary>
public class BolanTests
{
    private static SabberStoneCore.Model.Game BolanGame()
    {
        SabberEngineGame.EnsureCards(CardDb.FindDefaultDir(TestEnvironment.ProjectRoot));
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 2, Player1HeroClass = CardClass.DRUID, Player2HeroClass = CardClass.PALADIN,
            Player2HeroCard = Cards.FromId("DALA_BOSS_75h"), Player2HeroPowerCard = Cards.FromId("DALA_BOSS_75p"),
            FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false, SkipMulligan = true,
        });
        game.StartGame();
        return game;
    }

    private static int Quests(Controller bolan) => bolan.Hero.AppliedEnchantments?.Count(e => e.Card.Id.StartsWith("DALA_BOSS_75e")) ?? 0;

    [Fact]
    public void Bolan_is_immune_and_killing_hogger_ends_the_battle()
    {
        var game = BolanGame();
        var bolan = game.Player2;
        Assert.True(bolan.Hero.IsImmune);
        // the Hero Power until the Hogger quest is up (a quest may come again); his board emptied without deaths in between
        for (int i = 0; i < 60 && bolan.Hero.AppliedEnchantments?.Any(e => e.Card.Id == "DALA_BOSS_75e5") != true; i++)
        {
            bolan.Hero.HeroPower.IsExhausted = false;
            bolan.BaseMana = 10; bolan.UsedMana = 0;
            Assert.True(game.Process(HeroPowerTask.Any(bolan)));
            foreach (Minion m in bolan.BoardZone.ToList()) bolan.BoardZone.Remove(m);
        }
        Assert.InRange(Quests(bolan), 1, 5);
        int hand = bolan.HandZone.Count;
        game.Process(EndTurnTask.Any(bolan));
        game.Process(EndTurnTask.Any(game.Player1)); // his next turn: Hogger comes
        Assert.Equal(hand, bolan.HandZone.Count); // (no turn draw: he has no deck — live)
        Minion hogger = bolan.BoardZone.FirstOrDefault(m => m.Card.Id == "NEW1_040");
        Assert.NotNull(hogger);
        Generic.Destroy(hogger, null);
        game.DeathProcessingAndAuraUpdate();
        Assert.True(bolan.Hero.IsDead || bolan.PlayState is PlayState.LOSING or PlayState.LOST, "Bolan " + bolan.PlayState);
    }
}
