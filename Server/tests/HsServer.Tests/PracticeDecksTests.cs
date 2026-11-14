using HsServer.Game;
using HsServer.Lobby;
using PegasusShared;
using PegasusUtil;

namespace HsServer.Tests;

/// <summary>`admin practice decks &lt;name start&gt;`: the Practice Innkeeper plays the player's own decks (Game.PracticeDecks).</summary>
public class PracticeDecksTests
{
    [Fact]
    public void Innkeeper_takes_a_named_deck_of_its_class_and_not_yours()
    {
        var store = new DeckStore(null);
        long Add(string name, int hero, int card)
        {
            long id = store.Create(new CreateDeck { Name = name, Hero = hero, DeckType = DeckType.NORMAL_DECK, FormatType = FormatType.FT_WILD }).Info.Id;
            store.SetData(new DeckSetData { Deck = id, Cards = new List<DeckCardData> { new() { Def = new CardDef { Asset = card }, Qty = 2 } },
                SideboardCards = new List<SideBoardCardData>() });
            return id;
        }
        // hero dbf 637 = a Mage, 7 = a Warrior (classes given by the lookup below); card dbf = its id here
        long mageA = Add("Legacy Mage A", 637, 1), mageB = Add("Legacy Mage B", 637, 2);
        Add("Legacy Warrior A", 7, 3);
        Add("Custom Mage", 637, 4);
        string Class(int hero) => hero == 637 ? "MAGE" : "WARRIOR";
        string Card(int dbf) => "card" + dbf;

        for (int i = 0; i < 20; i++) // you play Mage A: the Innkeeper always takes Mage B
            Assert.Equal(new[] { "card2", "card2" }, PracticeDecks.Pick("Legacy", store, mageA, Class, "MAGE", Card, new Random(i)));
        Assert.Equal(new[] { "card3", "card3" }, PracticeDecks.Pick("Legacy", store, mageA, Class, "WARRIOR", Card, new Random(1)));
        Assert.Null(PracticeDecks.Pick("Legacy", store, mageA, Class, "PRIEST", Card, new Random(1))); // none: the random deck
        Assert.Null(PracticeDecks.Pick(null, store, mageA, Class, "MAGE", Card, new Random(1)));       // off
        Assert.Equal(new[] { "card4", "card4" }, PracticeDecks.Pick("Custom", store, mageB, Class, "MAGE", Card, new Random(1)));
    }
}
