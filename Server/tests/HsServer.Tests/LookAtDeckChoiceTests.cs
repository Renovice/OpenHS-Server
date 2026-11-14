using HsServer.Engine;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;
using SGame = SabberStoneCore.Model.Game;

namespace HsServer.Tests;

/// <summary>"Look at a card in your deck" choices (Generic.DeckCardCopies / OptionsToGraveyard): the options are copies, used up by
/// the pick — nothing reaches the hand (user 2026-10-09: Sphere of Sapience put the top card's copy or "A New Fate" in hand).</summary>
public class LookAtDeckChoiceTests
{
    private static readonly Lazy<CardDb> Db = new(() =>
    {
        string dir = CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
        SabberEngineGame.EnsureCards(dir);
        return CardDb.Load(dir);
    });

    private static readonly string[] Deck = { "CS2_118", "CS2_124", "CS2_201", "CS2_186", "CS2_168", "CS2_172", "CS2_120", "CS2_182", "CS2_127", "CS2_119" };

    /// <summary>Sphere of Sapience in play, the player's next turn started: the choice is open.</summary>
    private static (SGame Game, Controller Me, Weapon Sphere, IPlayable Top, int Hand) SphereTurn()
    {
        _ = Db.Value;
        var game = new SGame(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.MAGE, Player2HeroClass = CardClass.WARRIOR,
            Player1Deck = Deck.Select(Cards.FromId).ToList(), Player2Deck = Deck.Select(Cards.FromId).ToList(),
            FillDecks = false, History = true, Shuffle = false, SkipMulligan = true,
        });
        game.StartGame();
        var me = game.Player1;
        me.BaseMana = 10;
        me.UsedMana = 0;
        var sphere = Generic.DrawCard(me, Cards.FromId("SCH_259"));
        Assert.True(game.Process(PlayCardTask.Any(me, sphere)));
        Assert.True(game.Process(EndTurnTask.Any(me)));
        IPlayable top = me.DeckZone.TopCard;
        int hand = me.HandZone.Count;
        Assert.True(game.Process(EndTurnTask.Any(game.Player2)));
        Assert.NotNull(me.Choice);
        return (game, me, (Weapon)sphere, top, hand);
    }

    [Fact]
    public void Sphere_offers_a_copy_of_the_top_card_and_a_new_fate()
    {
        var (game, me, sphere, top, _) = SphereTurn();
        var options = me.Choice.Choices.Select(id => game.IdEntityDic[id]).ToList();
        Assert.Equal(2, options.Count);
        Assert.Equal(top.Card.Id, options[0].Card.Id);
        Assert.NotEqual(top.Id, options[0].Id); // a copy, linked to the deck card
        Assert.Equal(top.Id, options[0][GameTag.LINKED_ENTITY]);
        Assert.Equal("SCH_259t", options[1].Card.Id);
        Assert.Equal(sphere.Id, options[1][GameTag.CREATOR]);
    }

    [Fact]
    public void Keeping_the_top_card_adds_nothing_to_the_hand()
    {
        var (game, me, sphere, top, hand) = SphereTurn();
        var options = me.Choice.Choices.ToList();
        int durability = sphere.Durability;
        Assert.True(game.Process(ChooseTask.Pick(me, options[0])));
        Assert.All(options, id => Assert.Equal(Zone.GRAVEYARD, game.IdEntityDic[id].Zone.Type));
        Assert.DoesNotContain(me.HandZone, p => p.Card.Id == "SCH_259t");
        // (every deck card is unique) the top card exists once — drawn by the turn's draw, no second copy
        Assert.Equal(1, me.HandZone.Count(p => p.Card.Id == top.Card.Id) + me.DeckZone.Count(p => p.Card.Id == top.Card.Id));
        Assert.Contains(me.HandZone, p => p.Id == top.Id);
        Assert.Equal(hand + 1, me.HandZone.Count);
        Assert.Equal(durability, sphere.Durability);
    }

    [Fact]
    public void A_new_fate_puts_the_top_card_on_the_bottom_and_costs_a_durability()
    {
        var (game, me, sphere, top, _) = SphereTurn();
        var options = me.Choice.Choices.ToList();
        bool drawnAlready = me.HandZone.Any(p => p.Id == top.Id);
        int durability = sphere.Durability;
        Assert.True(game.Process(ChooseTask.Pick(me, options[1])));
        Assert.All(options, id => Assert.Equal(Zone.GRAVEYARD, game.IdEntityDic[id].Zone.Type));
        Assert.DoesNotContain(me.HandZone, p => p.Card.Id == "SCH_259t");
        Assert.False(drawnAlready, "the turn's draw came before the pick");
        Assert.Equal(top.Id, me.DeckZone[0].Id); // the bottom
        Assert.Equal(durability - 1, sphere.Durability);
    }
}
