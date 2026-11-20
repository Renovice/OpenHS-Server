using HsServer.Engine;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace HsServer.Tests;

/// <summary>"Cast When Drawn" given by an enchantment (Amitut, BE_143t2e): drawn, the spell casts itself (random targets) and the player
/// draws a replacement — Draw.OnDrawn, like a printed Casts When Drawn card.</summary>
public class GrantedCastWhenDrawnTests
{
    [Fact]
    public void A_spell_given_cast_when_drawn_casts_itself_and_is_replaced()
    {
        SabberEngineGame.EnsureCards(CardDb.FindDefaultDir(TestEnvironment.ProjectRoot));
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig { StartPlayer = 1, FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false, SkipMulligan = true });
        game.StartGame();
        var me = game.CurrentPlayer;
        var bolt = SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("CS2_029")); // Fireball: 6 damage
        me.DeckZone.Add(bolt); // the top
        Generic.AddEnchantmentBlock(game, Cards.FromId("BE_143t2e"), me.Hero, bolt);
        int hand = me.HandZone.Count, enemy = me.Opponent.Hero.Health + me.Opponent.BoardZone.GetAll().Sum(m => m.Health);
        Generic.Draw(me);
        Assert.NotEqual(Zone.HAND, bolt.Zone?.Type);
        Assert.Equal(hand + 1, me.HandZone.Count); // the replacement draw
        Assert.True(me.Opponent.Hero.Health + me.Opponent.BoardZone.GetAll().Sum(m => m.Health) < enemy || me.Hero.Health < 30);
    }
}
