using HsServer.Engine;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;
using SGame = SabberStoneCore.Model.Game;

namespace HsServer.Tests;

/// <summary>"If your deck started with …" reads the starting deck, not the deck now (user 2026-11-16 livecheck: Elise the Navigator crafted
/// nothing mid-game on live decks of 10 different Costs; Hexmarshal checked the current deck too).</summary>
public class DeckStartedWithTests
{
    private static readonly Lazy<CardDb> Db = new(() =>
    {
        string dir = CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
        SabberEngineGame.EnsureCards(dir);
        return CardDb.Load(dir);
    });

    // 10 different Costs (0..9): a collectible minion of each (they are only drawn or removed here)
    private static List<Card> TenCosts() => Enumerable.Range(0, 10)
        .Select(cost => Cards.All.First(c => c.Collectible && c.Type == CardType.MINION && c.Cost == cost))
        .ToList();

    [Fact]
    public void Elise_the_navigator_crafts_when_the_starting_deck_had_ten_costs_even_after_drawing_it()
    {
        _ = Db.Value;
        var deck = TenCosts().Concat(TenCosts()).Concat(TenCosts()).ToList();
        var game = new SGame(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.HUNTER, Player2HeroClass = CardClass.MAGE,
            Player1Deck = deck, Player2Deck = deck, FillDecks = false, History = true, Shuffle = false, SkipMulligan = true,
        });
        game.StartGame();
        game.Player1.BaseMana = 10;
        game.Player1.UsedMana = 0;
        var me = game.CurrentPlayer;
        // the deck now: only 0-Cost cards left (one Cost)
        foreach (IPlayable p in me.DeckZone.ToList().Where(p => p.Card.Cost > 0)) me.DeckZone.Remove(p);
        Assert.True(me.DeckZone.Select(p => p.Card.Cost).Distinct().Count() < 10);
        IPlayable elise = Generic.DrawCard(me, Cards.FromId("TLC_100"));
        int hand = me.HandZone.Count;
        Assert.True(game.Process(PlayCardTask.Minion(me, elise)));
        Assert.NotNull(me.Choice); // the craft began (it is a series of picks)
        for (int i = 0; i < 6 && me.Choice != null; i++)
            game.Process(ChooseTask.Pick(me, me.Choice.Choices[0]));
        Assert.Contains(me.HandZone, p => p.Card.Id.StartsWith("TLC_100t"));
    }
}
