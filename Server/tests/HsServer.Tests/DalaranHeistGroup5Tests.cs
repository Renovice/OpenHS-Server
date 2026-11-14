using HsServer.Engine;
using HsServer.Game;
using HsServer.Lobby;
using HsServer.Protocol;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;
using SGame = SabberStoneCore.Model.Game;
using Xunit;

namespace HsServer.Tests;

/// <summary>Bob's Tavern (Dalaran Heist group 5): the tavern game, each spell's save-data effect, the run after it and the run deck's
/// enchantments in later games (spec RESEARCH/heist-tavern.md).</summary>
public class DalaranHeistGroup5Tests
{
    private static readonly string DbfDir = CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
    private static readonly Lazy<CardDb> ClientCards = new(() => CardDb.Load(DbfDir));
    private static readonly Lazy<HeistRuns> Heist = new(() =>
        new HeistRuns(DbfDir, Path.Combine(TestEnvironment.ProjectRoot, "Server", "data", "heist"), ClientCards.Value));

    private const long Normal = 55, ChapterOne = 3005, Warrior = 10, MrChu = 54546, ArmorUp = 725, Tavern = 3236, Bob = 52913;
    private const long DeckList = 14, Defeated = 13, Fought = 18, NextBoss = 19, NextHealth = 123, HeroHealth = 130, LootHistory = 80,
        Override = 266, EnchantIndices = 368, Enchantments = 369, GoodFood = 370, SeenEvent1 = 227, SeenEvent2 = 228;

    private static long One(SaveDataStore sd, long sub) => sd.Values(Normal, sub).FirstOrDefault();
    private static List<long> Many(SaveDataStore sd, long sub) => sd.Values(Normal, sub).ToList();

    /// <summary>A chapter I run (Tough Crowd: 5 minions, 4 spells, a weapon) after <paramref name="wins"/> boss wins.</summary>
    private static SaveDataStore Run(int wins)
    {
        var sd = new SaveDataStore();
        foreach (var (sub, v) in new[] { (238L, ChapterOne), (239L, Warrior), (236L, ArmorUp), (237L, 1955L), (246L, 0L) }) sd.SetInts(Normal, sub, new[] { v });
        Heist.Value.Prepare(sd, ChapterOne, MrChu, new Random(1)); // (starts the run)
        for (int i = 0; i < wins; i++) Heist.Value.GameEnded(sd, ChapterOne, won: true, new Random(10 + i));
        return sd;
    }

    private static SabberEngineGame Engine(SaveDataStore sd, long scenario, int seed)
    {
        SabberEngineGame.EnsureCards(DbfDir); // (before RandomDecks touches the engine's card list)
        var (human, boss) = Heist.Value.Prepare(sd, scenario, MrChu, new Random(seed))!.Value;
        human.AccountHi = 1; human.AccountLo = 2;
        if (boss.Deck.Count == 0) boss.Deck = RandomDecks.Build(CardClass.MAGE, new Random(seed), CardSet.CORE, CardSet.EXPERT1);
        return new SabberEngineGame(new EngineSetup
        {
            Cards = ClientCards.Value, Scenario = (int)scenario, Seed = seed, Rules = RulesEngine.Sabber,
            GameSetupBody = PegasusPackets.Serialize(new PegasusGame.GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
            Human = human, Opponent = boss, ScenarioTags = Heist.Value.ScenarioTagsFor(scenario),
        });
    }

    private static SabberEngineGame TavernGame(SaveDataStore sd, int seed)
    {
        SabberEngineGame engine = Engine(sd, Tavern, seed);
        engine.Start();
        return engine;
    }

    private static IPlayable Give(Controller c, string id) => Generic.DrawCard(c, Cards.FromId(id));

    [Fact]
    public void Bobs_tavern_comes_after_the_third_boss_and_plays_to_a_win()
    {
        var sd = Run(3);
        Assert.Equal(Tavern, One(sd, Override));
        Assert.Equal(Enumerable.Repeat(Bob, 5), Many(sd, NextBoss));
        Assert.Equal(40, One(sd, NextHealth));
        Assert.Equal(3, Many(sd, 26).Count); // (boss 3's treasure offer is still made)
        Assert.True(Heist.Value.IsTavern(Tavern));

        SabberEngineGame engine = TavernGame(sd, 21);
        SGame game = engine.Rules;
        Controller me = game.Player1, bob = game.Player2;
        // the PUZZLE_MODE start: no mulligan, no opening hand, the player's turn 1 at once
        Assert.Equal(Step.MAIN_ACTION, game.Step);
        Assert.Same(me, game.CurrentPlayer);
        Assert.Null(me.Choice);
        Assert.Equal(1, game[GameTag.PUZZLE_MODE]);
        Assert.Equal(1, game[GameTag.SQUELCH_NON_GAME_TRIGGERS_AND_MODIFIERS]);
        Assert.Equal("DALA_BOSS_99h", bob.Hero.Card.Id);
        Assert.Equal(40, bob.Hero.Health);
        Assert.True(me.Hero.IsImmune);
        Assert.Equal(PlayReq.REQ_NOT_DISABLED_HERO_POWER, PlayErrors.OptionError(me, me.Hero.HeroPower));
        // Bob's setup: 4 of the run's 5 minions (with their slots, can't attack), 4 minions of his, 2 gold, the 5 spells, MISSION_EVENT 101
        Assert.Equal(4, me.BoardZone.Count);
        Assert.All(me.BoardZone, m => { Assert.True(Generic.RunSlot(m) > 0); Assert.Equal(1, m[GameTag.CANT_ATTACK]); });
        Assert.Equal(4, bob.BoardZone.Count);
        Assert.Equal(2, me.BaseMana);
        Assert.Equal(101, game[GameTag.MISSION_EVENT]);
        var hand = me.HandZone.Select(p => p.Card.Id).ToList();
        Assert.Equal(5, hand.Count);
        Assert.Equal(5, hand.Distinct().Count());
        Assert.Contains(hand[0], new[] { "DALA_901", "DALA_906" });
        Assert.Contains(hand[1], new[] { "DALA_902", "DALA_909" });
        Assert.All(me.HandZone, p => Assert.Equal(1, p[GameTag.COIN_MANA_GEM]));
        Assert.Equal(6, me.DeckZone.Count); // (10 run cards, 4 on the board; the turn draw was cancelled)
        // every run card carries its slot (1286), the slots 1..10
        Assert.Equal(Enumerable.Range(1, 10), me.DeckZone.Concat<IPlayable>(me.BoardZone).Select(Generic.RunSlot).OrderBy(x => x));

        // the player ends the turn: Bob says goodbye and leaves — the player wins
        game.Process(EndTurnTask.Any(me));
        Assert.True(engine.Over);
        Assert.True(engine.HumanWon);
        Assert.Equal(102, game[GameTag.MISSION_EVENT]);
        Assert.Empty(engine.RunDeckEdits!);

        // the whole game through the client stream (random plays, the AI side never acts)
        var rng = new Random(22);
        var result = new SoakRunner(rng) { Trace = (_, _) => { } }.Play(Engine(Run(3), Tavern, 22));
        Assert.Empty(result.Problems);
        Assert.True(result.Over);
    }

    /// <summary>A tavern with <paramref name="spell"/> in hand (10 gold), played on what <paramref name="target"/> picks; then the turn
    /// ends and the server writes the result. Returns the save data before (a copy of its lists) and after, and the game.</summary>
    private static (SaveDataStore Sd, List<long> DeckBefore, SGame Game, IReadOnlyList<RunDeckEdit> Edits) Play(string spell,
        Func<SGame, ICharacter> target = null, int seed = 31)
    {
        var sd = Run(3);
        var before = Many(sd, DeckList);
        SabberEngineGame engine = TavernGame(sd, seed);
        SGame game = engine.Rules;
        Controller me = game.Player1;
        me.BaseMana = 10;
        IPlayable card = Give(me, spell);
        game.Process(PlayCardTask.Any(me, card, target?.Invoke(game)));
        while (me.Choice != null) game.Process(ChooseTask.Pick(me, me.Choice.Choices[0]));
        game.Process(EndTurnTask.Any(me));
        Assert.True(engine.HumanWon);
        IReadOnlyList<RunDeckEdit> edits = engine.RunDeckEdits!;
        Heist.Value.GameEnded(sd, Tavern, won: true, new Random(seed), edits);
        return (sd, before, game, edits);
    }

    private static ICharacter FirstFriendly(SGame g) => g.Player1.BoardZone[0];
    private static ICharacter FirstEnemy(SGame g) => g.Player2.BoardZone[0];

    [Fact]
    public void Each_tavern_spell_changes_the_run_deck_as_live()
    {
        // Recruit: the enemy minion joins the deck at the next slot
        var (sd, before, game, edits) = Play("DALA_901", FirstEnemy);
        var deck = Many(sd, DeckList);
        Assert.Equal(before.Count + 1, deck.Count);
        Assert.Equal(ClientCards.Value[edits.Single().CardId].DbfId, deck[^1]);
        Assert.Equal(before.Count + 1, edits.Single().Slot);
        Assert.Contains(game.Player1.DeckZone, p => Generic.RunSlot(p) == before.Count + 1); // (in this game: the same entity, in the deck)

        // Dismiss: the friendly minion's slot is zeroed; it is set aside, not dead
        (sd, before, game, edits) = Play("DALA_902", FirstFriendly);
        int slot = edits.Single().Slot;
        Assert.Equal(0, Many(sd, DeckList)[slot - 1]);
        Assert.NotEqual(0, before[slot - 1]);
        Assert.Contains(game.Player1.SetasideZone, p => Generic.RunSlot(p) == slot);

        // Take a Chance: a random run card costs (0) for the run (DALA_903e)
        (sd, _, _, edits) = Play("DALA_903");
        Assert.Single(Many(sd, EnchantIndices));
        Assert.Equal(new long[] { 55138 }, Many(sd, Enchantments));

        // Good Food: +5 starting Health, subkey 370
        long health = One(Run(3), HeroHealth);
        (sd, _, _, _) = Play("DALA_904");
        Assert.Equal(health + 5, One(sd, HeroHealth));
        Assert.Equal(1, One(sd, GoodFood));

        // Right Hand Man: (slot, DALA_905e)
        (sd, _, game, edits) = Play("DALA_905", FirstFriendly);
        Assert.Equal(new long[] { edits.Single().Slot }, Many(sd, EnchantIndices));
        Assert.Equal(new long[] { 56283 }, Many(sd, Enchantments));

        // Round of Drinks: the 4 enemy minions join (left to right), each with Merriment (+1/+1)
        (sd, before, game, edits) = Play("DALA_906");
        deck = Many(sd, DeckList);
        Assert.Equal(before.Count + 4, deck.Count);
        Assert.Equal(Enumerable.Range(before.Count + 1, 4).Select(i => (long)i), Many(sd, EnchantIndices));
        Assert.Equal(Enumerable.Repeat(56287L, 4), Many(sd, Enchantments));
        Assert.Empty(game.Player2.BoardZone);

        // Recruit a Veteran: a Legendary minion joins the deck
        (sd, before, _, edits) = Play("DALA_907");
        deck = Many(sd, DeckList);
        Assert.Equal(before.Count + 1, deck.Count);
        Assert.Equal(Rarity.LEGENDARY, Cards.FromId(edits.Single().CardId).Rarity);

        // Tell a Story: +2/+2 now and for the run
        (sd, _, game, edits) = Play("DALA_908", FirstFriendly);
        Assert.Equal(new long[] { 55137 }, Many(sd, Enchantments));
        Minion told = game.Player1.BoardZone.First(m => Generic.RunSlot(m) == edits.Single().Slot);
        Assert.Equal(told.Card[GameTag.ATK] + 2, told.AttackDamage);

        // You're All Fired!: every friendly minion's slot is zeroed
        (sd, before, game, edits) = Play("DALA_909");
        Assert.Equal(4, edits.Count);
        Assert.All(edits, e => Assert.Equal(0, Many(sd, DeckList)[e.Slot - 1]));
        Assert.Empty(game.Player1.BoardZone);

        // The Gang's All Here!: three new copies
        (sd, before, game, _) = Play("DALA_910", FirstFriendly);
        deck = Many(sd, DeckList);
        Assert.Equal(before.Count + 3, deck.Count);
        Assert.Single(deck.Skip(before.Count).Distinct());

        // Kindle: one of the deck's spells leaves the run deck
        (sd, before, _, edits) = Play("DALA_911");
        Assert.Equal(CardType.SPELL, Cards.FromId(edits.Single().CardId).Type);
        Assert.Equal(0, Many(sd, DeckList)[edits.Single().Slot - 1]);

        // Brood: a new tavern, nothing for the run
        (sd, before, game, edits) = Play("DALA_912");
        Assert.Empty(edits);
        Assert.Equal(before, Many(sd, DeckList));
        Assert.Equal(4, game.Player2.BoardZone.Count);
        Assert.Equal(4, game.Player1.BoardZone.Count);

        // Tall Tales: +4/+4, costs (2) more (DALA_913e)
        (sd, _, game, edits) = Play("DALA_913", FirstFriendly);
        Assert.Equal(new long[] { 55139 }, Many(sd, Enchantments));
        Minion tall = game.Player1.BoardZone.First(m => Generic.RunSlot(m) == edits.Single().Slot);
        Assert.Equal(tall.Card.Cost + 2, tall.Cost);

        // The Upper Hand: a deck spell costs (3) less for the run (DALA_914e)
        (sd, _, _, edits) = Play("DALA_914");
        Assert.Equal(new long[] { 55603 }, Many(sd, Enchantments));
        Assert.Equal(CardType.SPELL, Cards.FromAssetId((int)Many(sd, DeckList)[edits.Single().Slot - 1]).Type);
    }

    [Fact]
    public void After_the_tavern_the_run_goes_on_to_the_next_boss()
    {
        var (sd, _, _, _) = Play("DALA_904");
        Assert.Equal(0, One(sd, Override));
        Assert.Equal(3, Many(sd, Defeated).Count); // (the tavern isn't a defeated boss)
        Assert.Equal(Bob, Many(sd, Fought)[^1]);
        Assert.Equal(1, One(sd, SeenEvent1));
        Assert.Equal(30, One(sd, NextHealth));
        long next = One(sd, NextBoss);
        Assert.NotEqual(Bob, next);
        Assert.StartsWith("DALA_BOSS_", ClientCards.Value.ByDbf((int)next).CardId);
        Assert.Empty(Many(sd, 23)); // no loot
        Assert.Empty(Many(sd, 26)); // no treasure

        // the next boss's win: LOOT_HISTORY gets a 0 for the tavern's stage; boss 5's win: the tavern again
        int loot = Many(sd, LootHistory).Count;
        Heist.Value.GameEnded(sd, ChapterOne, won: true, new Random(41));
        Assert.Equal(loot + 1, Many(sd, LootHistory).Count);
        Assert.Equal(0, Many(sd, LootHistory)[^1]);
        Assert.Equal(0, One(sd, Override));
        Heist.Value.GameEnded(sd, ChapterOne, won: true, new Random(42));
        Assert.Equal(Tavern, One(sd, Override));
        Heist.Value.GameEnded(sd, Tavern, won: true, new Random(43), Array.Empty<RunDeckEdit>());
        Assert.Equal(1, One(sd, SeenEvent2));
        Assert.Equal(50, One(sd, NextHealth));
    }

    [Fact]
    public void The_runs_deck_enchantments_apply_in_the_next_games()
    {
        // a tavern: Tell a Story on one minion, Right Hand Man on another
        var sd = Run(3);
        SabberEngineGame engine = TavernGame(sd, 51);
        SGame game = engine.Rules;
        Controller me = game.Player1;
        me.BaseMana = 10;
        Minion story = me.BoardZone[0], right = me.BoardZone[1];
        int storySlot = Generic.RunSlot(story), rightSlot = Generic.RunSlot(right);
        game.Process(PlayCardTask.Any(me, Give(me, "DALA_908"), story));
        game.Process(PlayCardTask.Any(me, Give(me, "DALA_905"), right));
        game.Process(EndTurnTask.Any(me));
        Heist.Value.GameEnded(sd, Tavern, won: true, new Random(52), engine.RunDeckEdits);
        Assert.Equal(new long[] { storySlot, rightSlot }, Many(sd, EnchantIndices));

        // the next boss game: the deck cards carry them (the buffed one +2/+2; the other starts in the hand)
        for (int seed = 53; seed < 58; seed++)
        {
            SabberEngineGame boss = Engine(sd, ChapterOne, seed);
            boss.Start();
            Controller p = boss.Rules.Player1;
            var all = p.DeckZone.Concat<IPlayable>(p.HandZone).ToList();
            var buffed = (Minion)all.Single(c => Generic.RunSlot(c) == storySlot);
            Assert.Equal(buffed.Card[GameTag.ATK] + 2, buffed.AttackDamage);
            Assert.Contains(p.HandZone, c => Generic.RunSlot(c) == rightSlot);
        }
    }
}
