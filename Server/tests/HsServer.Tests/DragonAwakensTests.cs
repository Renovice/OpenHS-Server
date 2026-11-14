using HsServer.Engine;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;
using SGame = SabberStoneCore.Model.Game;

namespace HsServer.Tests;

/// <summary>"(While in hand, play a Dragon to become a N/N Dragon!)" (Stonetalon Striker, Ebonscale Scout, Ebyssian): the card in
/// hand becomes its Dragon version with that version's stats (user 2026-11-14: the Striker became a Dragon that kept 3/3).</summary>
public class DragonAwakensTests
{
    private static readonly Lazy<CardDb> Db = new(() =>
    {
        string dir = CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
        SabberEngineGame.EnsureCards(dir);
        return CardDb.Load(dir);
    });

    private static readonly string[] Filler = { "CS2_118", "CS2_118", "CS2_124", "CS2_124", "CS2_201", "CS2_201", "CS2_186", "CS2_186", "CS2_168", "CS2_168" };

    [Theory]
    [InlineData("CATA_551", "CATA_551t")]
    [InlineData("CATA_552", "CATA_552t")]
    [InlineData("CATA_553", "CATA_553t")]
    public void Playing_a_dragon_turns_the_card_in_hand_into_its_dragon_with_the_dragons_stats(string id, string dragonId) =>
        Awaken(id, dragonId, null, 0);

    // (user's Heist game 2026-11-14: a run buff +1/+1 on the Striker — it became the Dragon, then went back to 4/4)
    [Fact]
    public void A_buffed_card_keeps_its_buff_on_top_of_the_dragons_stats() => Awaken("CATA_551", "CATA_551t", "DALA_906e", 1);

    private static void Awaken(string id, string dragonId, string buff, int bonus)
    {
        _ = Db.Value;
        var game = new SGame(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.WARRIOR, Player2HeroClass = CardClass.WARRIOR,
            Player1Deck = Filler.Select(Cards.FromId).ToList(), Player2Deck = Filler.Select(Cards.FromId).ToList(),
            FillDecks = false, History = true, Shuffle = false, SkipMulligan = true,
        });
        game.StartGame();
        game.Player1.BaseMana = 10;
        game.Player1.UsedMana = 0;
        var me = game.CurrentPlayer;
        IPlayable card = Generic.DrawCard(me, Cards.FromId(id));
        if (buff != null) Generic.AddEnchantmentBlock(game, Cards.FromId(buff), me.Hero, card, 0, 0, 0);
        Card dragonCard = Cards.FromId("EX1_116t"); // Whelp (1/1 Dragon)
        IPlayable dragon = Generic.DrawCard(me, dragonCard);
        Assert.True(game.Process(PlayCardTask.Minion(me, dragon)));
        // live (Ebonscale Scout): one TRIGGER block on the card, TARGET it, the change inside the gold level-up effect
        string wire = HistoryText.Format(new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full.ToList()).ToList(), x => x.ToString());
        string[] lines = wire.Split((char)10).Select(l => l.Trim()).ToArray();
        int change = Array.FindIndex(lines, l => l.StartsWith($"CHANGE_ENTITY - Updating Entity={card.Id} "));
        Assert.True(change > 0, wire);
        int spell = Array.FindLastIndex(lines, change, l => l.StartsWith("SUB_SPELL_START"));
        Assert.Contains("LevelUp_Gold", lines[spell]);
        Assert.Single(lines, l => l.StartsWith($"BLOCK_START BlockType=TRIGGER Entity={card.Id} "));

        IPlayable now = me.HandZone.Single(p => p.Card.Id == dragonId);
        Card v = Cards.FromId(dragonId);
        Assert.Equal(v.ATK + bonus, ((Minion)now).AttackDamage);
        Assert.Equal(v.Health + bonus, ((Minion)now).Health);
        Assert.Equal(v.ATK + bonus, now[GameTag.ATK]);
        Assert.Equal(v.Health + bonus, now[GameTag.HEALTH]);
    }
}
