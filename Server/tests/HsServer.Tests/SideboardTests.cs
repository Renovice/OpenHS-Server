using HsServer.Engine;
using HsServer.Game;
using HsServer.Lobby;
using HsServer.Protocol;
using HsServer.Sabber;
using PegasusGame;
using PegasusShared;
using PegasusUtil;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;
using Xunit.Abstractions;
using Card = SabberStoneCore.Model.Card;
using Cards = SabberStoneCore.Model.Cards;
using Generic = SabberStoneCore.Actions.Generic;
using SGame = SabberStoneCore.Model.Game;
using PowerHistoryTagChange = SabberStoneCore.Kettle.PowerHistoryTagChange;
using PowerHistoryFullEntity = SabberStoneCore.Kettle.PowerHistoryFullEntity;
using PowerHistoryBlockStart = SabberStoneCore.Kettle.PowerHistoryBlockStart;
using PowerHistoryBlockEnd = SabberStoneCore.Kettle.PowerHistoryBlockEnd;
using DeckType = PegasusShared.DeckType;

namespace HsServer.Tests;

/// <summary>Deck sideboards (E.T.C.'s band, Zilliax Deluxe 3000's modules, Commander Beatrix's minion, King of the
/// Underbelly's Beasts): the lobby keeps them as the client sends them, the game gets them, the cards use them.</summary>
public class SideboardTests(ITestOutputHelper output)
{
    private static readonly Lazy<CardDb> Db = new(() =>
    {
        string dir = CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
        SabberEngineGame.EnsureCards(dir);
        return CardDb.Load(dir);
    });

    private static readonly Lazy<SideboardRules> Rules = new(() =>
    {
        string login = RecordedAccount.FindReferenceSession(Path.Combine(TestEnvironment.ProjectRoot, "Hearthstone main game", "BepInEx", "HsRecorder"),
            Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures"));
        return new SideboardRules(Db.Value, new[] { login });
    });

    private static int Dbf(string id) => Db.Value[id].DbfId;

    /// <summary>A rules game whose player 1 has <paramref name="deck"/> (in this order, no shuffle) and these sideboards.</summary>
    private static SGame Game(IEnumerable<string> deck, params (string Owner, string Card)[] sideboard)
    {
        _ = Db.Value;
        var game = new SGame(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.PALADIN, Player2HeroClass = CardClass.MAGE,
            Player1Deck = deck.Select(Cards.FromId).ToList(),
            Player2Deck = Enumerable.Repeat("CS2_118", 10).Select(Cards.FromId).ToList(),
            Player1Sideboard = sideboard.Select(s => (Cards.FromId(s.Owner), Cards.FromId(s.Card))).ToList(),
            FillDecks = false, History = true, Shuffle = false,
        });
        game.StartGame();
        game.Player1.BaseMana = 10;
        game.Player2.BaseMana = 10;
        return game;
    }

    private static readonly string[] Filler = { "CS2_118", "CS2_118", "CS2_124", "CS2_124", "CS2_201", "CS2_201", "CS2_186", "CS2_186", "CS2_168" };

    [Fact]
    public void The_deck_s_sideboards_reach_the_players()
    {
        var rng = new Random(5);
        var engine = new SabberEngineGame(new EngineSetup
        {
            Cards = Db.Value, Scenario = 260, Seed = 5, Rules = RulesEngine.Sabber,
            GameSetupBody = PegasusPackets.Serialize(new GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
            Human = new PlayerSetup
            {
                HeroCardId = "HERO_04", Deck = RandomDecks.Build(CardClass.PALADIN, rng, CardSet.CORE, CardSet.EXPERT1).Take(29).Append("ETC_080").ToList(),
                Sideboard = new() { ("ETC_080", "CS2_029"), ("ETC_080", "CS2_182"), ("ETC_080", "UNKNOWN_CARD") }, AccountHi = 1, AccountLo = 2,
            },
            Opponent = new PlayerSetup { HeroCardId = "HERO_08", Deck = RandomDecks.Build(CardClass.MAGE, rng, CardSet.CORE, CardSet.EXPERT1), IsAi = true },
        });
        engine.Start();
        var board = Assert.Single(engine.Rules.Player1.Sideboards);
        Assert.Equal("ETC_080", board.Owner.Id);
        Assert.Equal(new[] { "CS2_029", "CS2_182" }, board.Cards.Select(c => c.Id)); // an unknown card is left out
        Assert.Empty(engine.Rules.Player2.Sideboards);
        // AI clones share it, and a pick in a clone doesn't touch the real game
        var clone = engine.Rules.Clone();
        Assert.True(Generic.TakeFromSideboard(clone.Player1, Cards.FromId("ETC_080"), Cards.FromId("CS2_029")));
        Assert.Single(clone.Player1.Sideboards[0].Cards);
        Assert.Equal(2, engine.Rules.Player1.Sideboards[0].Cards.Count);
    }

    [Fact]
    public void ETC_discovers_from_the_band_and_each_pick_leaves_it()
    {
        var game = Game(Filler, ("ETC_080", "CS2_029"), ("ETC_080", "CS2_182"), ("ETC_080", "CS2_023"));
        var me = game.Player1;
        IPlayable Play()
        {
            var etc = Generic.DrawCard(me, Cards.FromId("ETC_080"));
            Assert.True(game.Process(PlayCardTask.Minion(me, etc)));
            return etc;
        }
        Play();
        Assert.NotNull(me.Choice);
        // live (live-20261007-zilliax-etc-remix): the last band card first
        Assert.Equal(new[] { "CS2_023", "CS2_182", "CS2_029" }, me.Choice.Choices.Select(id => game.IdEntityDic[id].Card.Id));
        int yeti = me.Choice.Choices.Single(id => game.IdEntityDic[id].Card.Id == "CS2_182");
        Assert.True(game.Process(ChooseTask.Pick(me, yeti)));
        Assert.Contains(me.HandZone, p => p.Card.Id == "CS2_182");
        Assert.Equal(new[] { "CS2_029", "CS2_023" }, Generic.SideboardCards(me, Cards.FromId("ETC_080")).Select(c => c.Id));

        // a second E.T.C. (a copy) uses the same band: only what is left
        Play();
        Assert.Equal(new[] { "CS2_023", "CS2_029" }, me.Choice.Choices.Select(id => game.IdEntityDic[id].Card.Id));
        Assert.True(game.Process(ChooseTask.Pick(me, me.Choice.Choices[0])));
        Assert.Single(Generic.SideboardCards(me, Cards.FromId("ETC_080")));

        // the other player has no band: their E.T.C. offers nothing (hearthpwn: "you're offered nothing unless you run ETC")
        Assert.True(game.Process(EndTurnTask.Any(me)));
        var theirs = Generic.DrawCard(me.Opponent, Cards.FromId("ETC_080"));
        Assert.True(game.Process(PlayCardTask.Minion(me.Opponent, theirs)));
        Assert.Null(me.Opponent.Choice);
    }

    [Fact]
    public void King_of_the_Underbelly_discovers_one_of_his_beasts_three_cheaper()
    {
        var game = Game(Filler, ("JAIL_831", "CS2_119"), ("JAIL_831", "CS2_172"), ("JAIL_831", "CS2_120"));
        var me = game.Player1;
        var king = Generic.DrawCard(me, Cards.FromId("JAIL_831"));
        Assert.True(game.Process(PlayCardTask.Minion(me, king)));
        Assert.Equal(new[] { "CS2_120", "CS2_172", "CS2_119" }, me.Choice.Choices.Select(id => game.IdEntityDic[id].Card.Id));
        int snapjaw = me.Choice.Choices.Single(id => game.IdEntityDic[id].Card.Id == "CS2_119");
        Assert.True(game.Process(ChooseTask.Pick(me, snapjaw)));
        var picked = Assert.Single(me.HandZone, p => p.Card.Id == "CS2_119");
        Assert.Equal(1, picked.Cost);                                                     // 4 - 3
        Assert.Contains(picked.AppliedEnchantments, e => e.Card.Id == "GBL_004e");       // the client's "Cost - 3"
        Assert.Equal(2, Generic.SideboardCards(me, Cards.FromId("JAIL_831")).Count);
    }

    [Fact]
    public void Commander_Beatrix_s_ten_copies_join_the_deck_before_the_opening_hand()
    {
        // an all-odd deck: the copies are generated, Baku-style "only odd-Cost cards" checks don't see them (wiki)
        var game = Game(Filler.Append("JAIL_397"), ("JAIL_397", "CS2_120"));
        var me = game.Player1;
        var copies = me.DeckZone.Concat(me.HandZone).Where(p => p.Card.Id == "CS2_120").ToList();
        Assert.Equal(10, copies.Count);
        IPlayable beatrix = me.DeckZone.Concat(me.HandZone).Single(p => p.Card.Id == "JAIL_397");
        Assert.All(copies, c => Assert.True(c.Id >= game.FirstGeneratedEntityId));
        Assert.All(copies, c => Assert.Equal(beatrix.Id, c[GameTag.DISPLAYED_CREATOR]));
        Assert.True(me.DeckZone.NoEvenCostCards);
        Assert.DoesNotContain(me.DeckCards, c => c.Id == "CS2_120");                     // not in the deck list either

        // live shape: one TRIGGER block by Beatrix, effect index 2, the card revealed in it — before the opening draw block
        var history = game.PowerHistory.Full.ToList();
        int block = history.FindIndex(e => e is PowerHistoryBlockStart b && b.Source == beatrix.Id && b.BlockType == BlockType.TRIGGER);
        int draws = history.FindIndex(e => e is PowerHistoryBlockStart b && b.Source == game.Id && b.Index == -1);
        Assert.True(block >= 0 && block < draws, $"{block} < {draws}");
        var start = (PowerHistoryBlockStart)history[block];
        Assert.Equal(2, start.Index);
        Assert.Equal(0, start.TriggerKeyword);
        var inBlock = history.Skip(block).TakeWhile(e => e is not PowerHistoryBlockEnd).OfType<PowerHistoryTagChange>().ToList();
        Assert.Contains(inBlock, t => t.EntityId == beatrix.Id && t.Tag == GameTag.REVEALED && t.Value == 1);
        Assert.Contains(inBlock, t => t.EntityId == beatrix.Id && t.Tag == GameTag.REVEALED && t.Value == 0);
        Assert.Equal(10, history.Skip(block).TakeWhile(e => e is not PowerHistoryBlockEnd).OfType<PowerHistoryFullEntity>().Count());

        // no pick: nothing joins, no block
        var none = Game(Filler.Append("JAIL_397"));
        Assert.DoesNotContain(none.Player1.DeckZone.Concat(none.Player1.HandZone), p => p.Card.Id == "CS2_120");
        Assert.DoesNotContain(none.PowerHistory.Full, e => e is PowerHistoryBlockStart { Index: 2 });
    }

    [Fact]
    public void The_opponent_s_Beatrix_block_is_live_s()
    {
        // live-20261003-dk-classxp (the opponent's Beatrix, before the first player is chosen): BLOCK_START TRIGGER
        // Entity=[JAIL_397] EffectIndex=2; SHOW_ENTITY JAIL_397; ten hidden FULL_ENTITY in DECK, each followed by
        // TAG_CHANGE DISPLAYED_CREATOR=Beatrix; HIDE_ENTITY JAIL_397 (DECK); TAG_CHANGE REVEALED=0; BLOCK_END
        var rng = new Random(3);
        var aiDeck = Enumerable.Repeat("CS2_118", 29).Append("JAIL_397").ToList();
        var engine = new SabberEngineGame(new EngineSetup
        {
            Cards = Db.Value, Scenario = 260, Seed = 3, Rules = RulesEngine.Sabber,
            GameSetupBody = PegasusPackets.Serialize(new GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
            Human = new PlayerSetup { HeroCardId = "HERO_08", Deck = RandomDecks.Build(CardClass.MAGE, rng, CardSet.CORE, CardSet.EXPERT1), AccountHi = 1, AccountLo = 2 },
            Opponent = new PlayerSetup { HeroCardId = "HERO_04", Deck = aiDeck, Sideboard = new() { ("JAIL_397", "CS2_120") }, IsAi = true },
        });
        engine.Start();
        int beatrix = engine.Rules.Player2.DeckZone.Concat(engine.Rules.Player2.HandZone).Single(p => p.Card.Id == "JAIL_397").Id;
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(engine.Rules.PowerHistory.Full).ToList();
        int start = sent.FindIndex(d => d.PowerStart is { } b && b.Source == beatrix);
        Assert.True(start >= 0);
        var block = sent.Skip(start).TakeWhile(d => d.PowerEnd == null).ToList();
        output.WriteLine(HistoryText.Format(block, id => id == beatrix ? "[JAIL_397]" : id.ToString(), withTags: false));
        Assert.Equal(2, block[0].PowerStart.EffectIndex);
        Assert.Equal(0, block[0].PowerStart.TriggerKeyword);
        Assert.Equal(beatrix, block[1].ShowEntity?.Entity);
        Assert.Equal("JAIL_397", block[1].ShowEntity.Name);
        var made = block.Where(d => d.FullEntity != null).ToList();
        Assert.Equal(10, made.Count);
        Assert.All(made, f => Assert.Equal("", f.FullEntity.Name ?? ""));                 // hidden from the other player
        Assert.Equal(10, block.Count(d => d.TagChange is { Tag: (int)GAME_TAG.DISPLAYED_CREATOR } t && t.Value == beatrix));
        Assert.Equal(beatrix, block[^2].HideEntity?.Entity);
        Assert.True(block[^1].TagChange is { Tag: (int)GAME_TAG.REVEALED, Value: 0 });
        // and it comes before the first player is chosen (the opening draw block, by the game, effect index -1)
        int draws = sent.FindIndex(d => d.PowerStart is { } b && b.Source == 1 && b.EffectIndex == -1);
        Assert.True(start < draws);
    }

    [Fact]
    public void Zilliax_is_built_from_its_modules()
    {
        // Ticking (4: 1/3, costs 1 less per friendly minion) + Perfect (5: 3/2 Divine Shield, Taunt, Lifesteal, Rush), look t9
        var game = Game(Filler.Prepend("TOY_330"), ("TOY_330", "TOY_330t98"), ("TOY_330", "TOY_330t9"), ("TOY_330", "TOY_330t99"));
        var me = game.Player1;
        var zilliax = me.DeckZone.Concat(me.HandZone).Single(p => p.Card.Id == "TOY_330t9");
        Assert.Equal((9, 4, 5), (zilliax.Card.Cost, zilliax.Card.ATK, zilliax.Card.Health));
        Assert.Equal(Dbf("TOY_330t98"), zilliax.Card[GameTag.MODULAR_ENTITY_PART_1]);
        Assert.Equal(Dbf("TOY_330t99"), zilliax.Card[GameTag.MODULAR_ENTITY_PART_2]);
        Assert.Equal(0, zilliax.Card[GameTag.HIDE_COST]);
        Assert.False(zilliax.Card.Tags.ContainsKey(GameTag.HIDE_STATS));
        Assert.Equal(1, zilliax.Card[GameTag.TAUNT]);
        Assert.Contains(me.DeckCards, c => c.Id == "TOY_330t9");
        // the FULL_ENTITY the client gets carries the build
        var tags = PowerHistoryBuilder.FullEntity(zilliax).Entity.Tags;
        Assert.Equal(Dbf("TOY_330t98"), tags[GameTag.MODULAR_ENTITY_PART_1]);
        Assert.Equal(9, tags[GameTag.COST]);

        // in hand Ticking's discount works; played it has Perfect's keywords
        if (zilliax.Zone.Type != Zone.HAND) Generic.Draw(me, zilliax);
        var croc = Generic.DrawCard(me, Cards.FromId("CS2_120"));
        Assert.True(game.Process(PlayCardTask.Minion(me, croc)));
        Assert.Equal(8, zilliax.Cost);
        Assert.True(game.Process(PlayCardTask.Minion(me, zilliax)));
        var m = (Minion)zilliax;
        Assert.True(m.HasTaunt && m.HasDivineShield && m.HasLifeSteal && m.IsRush);
        Assert.Equal((4, 5), (m.AttackDamage, m.Health));
    }

    [Fact]
    public void A_built_Zilliax_has_both_modules_effects()
    {
        // Pylon (other minions +1 Attack) + Haywire (end of your turn: 3 damage to your hero)
        var game = Game(Filler, ("TOY_330", "TOY_330t95"), ("TOY_330", "TOY_330t93"), ("TOY_330", "TOY_330t5"));
        var me = game.Player1;
        Card built = Generic.AsBuilt(me, Cards.FromId("TOY_330"));
        Assert.Equal("TOY_330t5", built.Id);
        Assert.Equal((5, 6, 6), (built.Cost, built.ATK, built.Health));
        var croc = Generic.DrawCard(me, Cards.FromId("CS2_120"));
        Assert.True(game.Process(PlayCardTask.Minion(me, croc)));
        var zilliax = Generic.DrawCard(me, built);
        Assert.True(game.Process(PlayCardTask.Minion(me, zilliax)));
        Assert.Equal(3, ((Minion)croc).AttackDamage);                                    // 2 + 1
        Assert.Equal(6, ((Minion)zilliax).AttackDamage);                                 // not itself
        int health = me.Hero.Health;
        Assert.True(game.Process(EndTurnTask.Any(me)));
        Assert.Equal(health - 3, me.Hero.Health);

        // every module and its tokens load with their scripts (a token the loader skipped would fail only when played)
        foreach (string id in new[] { "TOY_330t92", "TOY_330t93", "TOY_330t94", "TOY_330t95", "TOY_330t97", "TOY_330t98", "TOY_330t94e", "TOY_330t95e1" })
            Assert.NotNull(Cards.TryFromId(id)?.Power);
        foreach (string id in new[] { "TOY_330", "TOY_330t5", "TOY_330t12", "TOY_330t96", "TOY_330t99", "JAIL_397", "JAIL_831", "ETC_080" })
            Assert.True(Cards.FromId(id).Implemented, id);

        // Power (start of your turn: double this minion's Attack)
        var game3 = Game(Filler, ("TOY_330", "TOY_330t94"), ("TOY_330", "TOY_330t96"), ("TOY_330", "TOY_330t6"));
        var power = Generic.DrawCard(game3.Player1, Generic.AsBuilt(game3.Player1, Cards.FromId("TOY_330")));
        Assert.True(game3.Process(PlayCardTask.Minion(game3.Player1, power)));
        Assert.Equal(3, ((Minion)power).AttackDamage);                                   // 1 + 2
        Assert.True(((Minion)power).CantBeTargetedBySpells && ((Minion)power).Poisonous);  // Virus: Elusive, Poisonous
        Assert.True(game3.Process(EndTurnTask.Any(game3.Player1)));
        Assert.True(game3.Process(EndTurnTask.Any(game3.Player2)));
        Assert.Equal(6, ((Minion)power).AttackDamage);

        // Twin (Battlecry: summon a copy) + Recursive (Deathrattle: shuffle this into your deck)
        var game2 = Game(Filler, ("TOY_330", "TOY_330t97"), ("TOY_330", "TOY_330t92"), ("TOY_330", "TOY_330t12"));
        var me2 = game2.Player1;
        var twin = Generic.DrawCard(me2, Generic.AsBuilt(me2, Cards.FromId("TOY_330")));
        Assert.True(game2.Process(PlayCardTask.Minion(me2, twin)));
        Assert.Equal(2, me2.BoardZone.Count(p => p.Card.Id == "TOY_330t12"));
        int deck = me2.DeckZone.Count;
        var fireball = Generic.DrawCard(me2, Cards.FromId("CS2_029"));
        Assert.True(game2.Process(PlayCardTask.SpellTarget(me2, fireball, (ICharacter)twin)));
        Assert.Equal(1, me2.BoardZone.Count(p => p.Card.Id == "TOY_330t12"));
        Assert.Equal(deck + 1, me2.DeckZone.Count);
        Assert.Contains(me2.DeckZone, p => p.Card.Id == "TOY_330t12" && p.Card[GameTag.MODULAR_ENTITY_PART_1] == Dbf("TOY_330t97"));
    }

    [Fact]
    public void The_deck_store_applies_sideboard_changes_as_the_client_sends_them()
    {
        var store = new DeckStore(null);
        long id = store.Create(new CreateDeck { Name = "Z", Hero = 7, DeckType = DeckType.NORMAL_DECK, SortOrder = 0 }).Info.Id;
        int zilliax = Dbf("TOY_330");
        // a new sideboard: every card with all four premiums, the unused at 0 (live DeckSetData, trade-lifesteal-20260930 #729)
        IEnumerable<SideBoardCardData> All(string card, int qty) => new[] { 0, 1, 3, 2 }.Select(premium => new SideBoardCardData
            { Def = new PegasusShared.CardDef { Asset = Dbf(card), Premium = premium }, LinkedCardDbId = zilliax, Qty = premium == 0 ? qty : 0 });
        store.SetData(new DeckSetData
        {
            Deck = id, Cards = new() { new DeckCardData { Def = new PegasusShared.CardDef { Asset = zilliax }, Qty = 1 } },
            SideboardCards = All("TOY_330t5", 1).Concat(All("TOY_330t98", 1)).Concat(All("TOY_330t99", 1)).ToList(),
        });
        Assert.Equal(new[] { Dbf("TOY_330t5"), Dbf("TOY_330t98"), Dbf("TOY_330t99") }, store.Sideboard(id).Select(s => s.Card));
        // a change lists only what changed: Ticking out, Twin in — the rest stays
        store.SetData(new DeckSetData { Deck = id, Cards = new(), SideboardCards = All("TOY_330t98", 0).Concat(All("TOY_330t97", 1)).ToList() });
        Assert.Equal(new[] { Dbf("TOY_330t5"), Dbf("TOY_330t99"), Dbf("TOY_330t97") }, store.Sideboard(id).Select(s => s.Card));
        Assert.All(store.Sideboard(id), s => Assert.Equal(zilliax, s.Owner));
        // a change to the main cards only keeps the sideboard
        store.SetData(new DeckSetData { Deck = id, Cards = new() { new DeckCardData { Def = new PegasusShared.CardDef { Asset = Dbf("CS2_120") }, Qty = 2 } }, SideboardCards = new() });
        Assert.Equal(3, store.Sideboard(id).Count);
        Assert.Equal(3, store.GetContents(new GetDeckContents { DeckId = new() { id } }).Decks[0].SideboardCards.Count);
        // the owner taken out of the deck: its cards at 0
        store.SetData(new DeckSetData
        {
            Deck = id, Cards = new() { new DeckCardData { Def = new PegasusShared.CardDef { Asset = zilliax }, Qty = 0 } },
            SideboardCards = All("TOY_330t5", 0).Concat(All("TOY_330t99", 0)).Concat(All("TOY_330t97", 0)).ToList(),
        });
        Assert.Empty(store.Sideboard(id));
    }

    [Fact]
    public void Sideboard_rules_come_from_the_card_data_and_the_recorded_subsets()
    {
        SideboardRules rules = Rules.Value;
        Assert.Contains(74, rules.Subsets.Keys);      // Commander Beatrix: 2-Cost minions
        Assert.Contains(18465, rules.Subsets.Keys);   // Zilliax: the 8 functional + 8 cosmetic modules
        Assert.Contains(19499, rules.Subsets.Keys);   // King of the Underbelly: other classes' Beasts
        Assert.Equal(16, rules.Subsets[18465].Count);

        var dropped = new List<string>();
        int beatrix = Dbf("JAIL_397");
        var kept = rules.Valid(new[] { beatrix, Dbf("CS2_120") },
            new[] { (beatrix, Dbf("CS2_182")), (beatrix, Dbf("CS2_120")), (beatrix, Dbf("CS2_172")), (Dbf("ETC_080"), Dbf("CS2_029")) }, dropped.Add);
        Assert.Equal(new[] { (beatrix, Dbf("CS2_120")) }, kept); // Yeti costs 4; one pick only; no E.T.C. in the deck
        Assert.Equal(3, dropped.Count);
        foreach (string d in dropped) output.WriteLine(d);

        int zilliax = Dbf("TOY_330");
        var fill = rules.RandomFill(new[] { zilliax, Dbf("JAIL_831"), Dbf("ETC_080") }, 3, new Random(1));
        Assert.Equal(2, fill.Count(f => f.Owner == zilliax && Db.Value.ByDbf(f.Card).Tag(GAME_TAG.ZILLIAX_CUSTOMIZABLE_FUNCTIONALMODULE) != 0));
        Assert.Equal(1, fill.Count(f => f.Owner == zilliax && Db.Value.ByDbf(f.Card).Tag(GAME_TAG.ZILLIAX_CUSTOMIZABLE_COSMETICMODULE) != 0));
        Assert.Equal(3, fill.Count(f => f.Owner == Dbf("JAIL_831")));
        Assert.Equal(3, fill.Count(f => f.Owner == Dbf("ETC_080")));
        Assert.Empty(rules.Valid(fill.Select(f => f.Owner).Distinct(), fill).Except(fill)); // a random fill is valid
        Assert.Equal(fill.Count, rules.Valid(fill.Select(f => f.Owner).Distinct(), fill).Count);
    }

    [Fact]
    public void Games_with_every_sideboard_card_keep_the_client_view_exact()
    {
        var failures = new List<string>();
        for (int seed = 1; seed <= 6; seed++)
        {
            var rng = new Random(seed);
            List<string> Deck(CardClass c, params string[] owners) =>
                RandomDecks.Build(c, rng, CardSet.CORE, CardSet.EXPERT1).Take(30 - owners.Length).Concat(owners).ToList();
            // odd seeds: Beatrix on the human's side, King on the AI's; even seeds the other way round (the AI's deck card is
            // revealed to the human at the start)
            bool swap = seed % 2 == 0;
            CardClass humanClass = swap ? CardClass.HUNTER : CardClass.PALADIN, aiClass = swap ? CardClass.PALADIN : CardClass.HUNTER;
            string Own(CardClass c) => c == CardClass.PALADIN ? "JAIL_397" : "JAIL_831";
            var human = new PlayerSetup { HeroCardId = swap ? "HERO_05" : "HERO_04", Deck = Deck(humanClass, "ETC_080", "TOY_330", Own(humanClass)), AccountHi = 1, AccountLo = 2 };
            var ai = new PlayerSetup { HeroCardId = swap ? "HERO_04" : "HERO_05", Deck = Deck(aiClass, "ETC_080", "TOY_330", Own(aiClass)), IsAi = true };
            human.Sideboard = SoakRunner.RandomSideboard(Db.Value, human.Deck, humanClass, rng);
            ai.Sideboard = SoakRunner.RandomSideboard(Db.Value, ai.Deck, aiClass, rng);
            Assert.Equal(swap ? 9 : 7, human.Sideboard.Count);                            // band 3, Zilliax 3, Beatrix 1 / King 3
            var engine = new SabberEngineGame(new EngineSetup
            {
                Cards = Db.Value, Scenario = 260, Seed = seed, Rules = RulesEngine.Sabber,
                GameSetupBody = PegasusPackets.Serialize(new GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
                Human = human, Opponent = ai,
            });
            var result = new SoakRunner(rng).Play(engine);
            failures.AddRange(result.Problems.Take(3).Select(p => $"seed {seed}: {p}"));
        }
        foreach (string f in failures) output.WriteLine(f);
        Assert.Empty(failures);
    }
}
