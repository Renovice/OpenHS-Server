using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using HsServer.Engine;
using HsServer.Game;
using HsServer.Protocol;
using HsServer.Sabber;
using PegasusGame;
using SabberStoneCore.Enums;
using Xunit.Abstractions;

namespace HsServer.Tests;

/// <summary>The SabberStone-based rules engine behind the client adapter (history conversion, visibility, options).</summary>
public class SabberEngineTests(ITestOutputHelper output)
{
    private static readonly Lazy<CardDb> Db = new(() =>
    {
        string dir = CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
        SabberEngineGame.EnsureCards(dir);
        return CardDb.Load(dir);
    });

    [Fact]
    public void Random_basic_and_classic_games_keep_the_client_view_exact()
    {
        int games = 30, clean = 0;
        var failures = new List<string>();
        for (int seed = 1; seed <= games; seed++)
        {
            var rng = new Random(seed);
            var result = new SoakRunner(rng).Play(SoakRunner.RandomGame(rng, Db.Value));
            if (result.Problems.Count == 0) clean++;
            else failures.Add($"seed {seed}: {result.Problems[0]}");
        }
        output.WriteLine($"{clean}/{games} clean");
        foreach (string f in failures.Take(10)) output.WriteLine(f);
        Assert.Empty(failures);
    }

    [Fact]
    public void Core_reprints_are_playable_with_the_original_scripts()
    {
        _ = Db.Value;
        foreach (string id in new[] { "CORE_CS2_024", "CORE_CS2_029", "CORE_CS2_023", "CORE_UNG_205", "CORE_EX1_160" })
        {
            var card = SabberStoneCore.Model.Cards.TryFromId(id);
            Assert.NotNull(card);
            Assert.True(card.Implemented || card.ChooseOne, $"{id} has no script");
        }
        // Choose One reprints reuse the original option cards (the client has no CORE_EX1_160a/b).
        var powerOfTheWild = SabberStoneCore.Model.Cards.FromId("CORE_EX1_160");
        Assert.Equal("EX1_160a", SabberStoneCore.Model.Cards.ChooseOneOption(powerOfTheWild, 'a').Id);
    }

    [Fact]
    public void Cards_have_their_current_keywords_not_the_ones_they_only_mention()
    {
        _ = Db.Value;
        SabberStoneCore.Model.Card C(string id) => SabberStoneCore.Model.Cards.FromId(id);
        // Glacial Shard only mentions Freeze (a reference tag in the client DB); Water Elemental freezes what it damages.
        Assert.False(C("CORE_UNG_205").Freeze);
        Assert.True(C("CS2_033").Freeze);
        // Current versions (2021 data had no Taunt / a 4/5 Baine / Overload 2): stats and keywords come from the client.
        Assert.True(C("EX1_110").Taunt);
        Assert.True(C("CORE_EX1_110").Taunt);
        Assert.Equal(1, C("EX1_248").Overload);
        // Hero-skin hero powers play like the basic one (Khadgar's Fireblast needs a target).
        Assert.True(C("CS2_034_H2").MustHaveTargetToPlay);
    }

    [Fact]
    public void Tradeable_cards_trade_for_1_mana_into_the_deck_and_draw()
    {
        _ = Db.Value;
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.MAGE, Player2HeroClass = CardClass.MAGE,
            FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false,
        });
        game.StartGame();
        game.Player1.BaseMana = 5;
        var me = game.CurrentPlayer;
        var knight = SabberStoneCore.Actions.Generic.DrawCard(me, SabberStoneCore.Model.Cards.FromId("CORE_EX1_002"));
        int hand = me.HandZone.Count, deck = me.DeckZone.Count;

        // Offered as its own option, marked with the TRADEABLE keyword for the client.
        var options = me.Options();
        var trade = options.Single(t => t.PlayerTaskType == SabberStoneCore.Tasks.PlayerTasks.PlayerTaskType.TRADE && t.Source == knight);
        var all = SabberStoneCore.Kettle.PowerOptionsBuilder.AllOptions(game, options);
        Assert.Contains(all.PowerOptionList, o => o.MainOption?.EntityId == knight.Id && o.MainOption.PowerKeyword == 1720);

        Assert.True(game.Process(trade));
        Assert.Equal(1, me.UsedMana);
        Assert.Equal(SabberStoneCore.Enums.Zone.DECK, knight.Zone.Type);
        Assert.Equal(hand, me.HandZone.Count);  // the Knight left, a card came
        Assert.Equal(deck, me.DeckZone.Count);  // a card left, the Knight came
        Assert.NotEqual(knight, me.HandZone[me.HandZone.Count - 1]); // never draws itself back
        Assert.Equal(0, knight[SabberStoneCore.Enums.GameTag.REVEALED]);
        Assert.Equal(0, knight[SabberStoneCore.Actions.Generic.IS_USING_TRADE_OPTION]);

        // Live shape: DECK_ACTION (effect 0) > reveal + into the deck > SLUSH_TIME 3000 > POWER (effect 0) holding the draw.
        var history = game.PowerHistory.Last;
        int deckAction = history.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart { BlockType: (SabberStoneCore.Enums.BlockType)13 });
        Assert.True(deckAction >= 0);
        Assert.Equal(0, ((SabberStoneCore.Kettle.PowerHistoryBlockStart)history[deckAction]).Index);
        int zone = history.FindIndex(deckAction, e => e is SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: SabberStoneCore.Enums.GameTag.ZONE } t && t.EntityId == knight.Id);
        int slush = history.FindIndex(deckAction, e => e is SabberStoneCore.Kettle.PowerHistoryMetaData { Type: SabberStoneCore.Enums.MetaDataType.SLUSH_TIME, Data: 3000 });
        int power = history.FindIndex(deckAction, e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart { BlockType: SabberStoneCore.Enums.BlockType.POWER, Index: 0 } b && b.Source == knight.Id);
        Assert.True(zone > deckAction && slush > zone && power > slush);

        // What each client sees: the trader keeps seeing it until it's hidden in the deck; the opponent is shown it.
        var mine = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        int hide = mine.FindIndex(d => d.HideEntity?.Entity == knight.Id);
        Assert.True(hide >= 0);
        Assert.Equal((int)HsServer.Engine.TAG_ZONE.DECK, mine[hide].HideEntity.Zone);
        Assert.Contains(mine, d => d.TagChange is { Tag: (int)HsServer.Engine.GAME_TAG.ZONE, Value: (int)HsServer.Engine.TAG_ZONE.DECK } t && t.Entity == knight.Id);
        // Client tags >= 1000 reach the client too (IS_USING_TRADE_OPTION was dropped by the old "< 1000" history rule).
        Assert.Contains(mine, d => d.TagChange is { Tag: 2045, Value: 1 } t && t.Entity == knight.Id);
        var theirs = new ClientStream(2, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        Assert.Contains(theirs, d => d.ShowEntity?.Entity == knight.Id);

        // A Tradeable card drawn from the deck is shown with TRADEABLE (the client's trade icon and drag-to-deck need it).
        var viper = SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("CORE_SW_072"), zone: me.DeckZone);
        SabberStoneCore.Actions.Generic.Draw(me, viper);
        var drawn = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full).Last(d => d.ShowEntity?.Entity == viper.Id);
        Assert.Contains(drawn.ShowEntity.Tags, t => t.Name == 1720 && t.Value == 1);
    }

    [Fact]
    public void Reborn_minions_return_once_with_1_health()
    {
        _ = Db.Value;
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.MAGE, Player2HeroClass = CardClass.MAGE,
            FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false,
        });
        game.StartGame();
        game.Player1.BaseMana = 10;
        var me = game.CurrentPlayer;
        var murmy = SabberStoneCore.Actions.Generic.DrawCard(me, SabberStoneCore.Model.Cards.FromId("CORE_ULD_723"));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, murmy)));

        void Zap(SabberStoneCore.Model.Entities.IPlayable target)
        {
            var bolt = SabberStoneCore.Actions.Generic.DrawCard(me, SabberStoneCore.Model.Cards.FromName("Frostbolt"));
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, bolt, (SabberStoneCore.Model.Entities.ICharacter)target)));
        }

        Zap(murmy);
        var back = Assert.Single(me.BoardZone);
        Assert.NotEqual(murmy.Id, back.Id);
        Assert.Equal("CORE_ULD_723", back.Card.Id);
        Assert.Equal(1, back.Health);
        Assert.Equal(0, back[GameTag.REBORN]);

        Zap(back);
        Assert.Empty(me.BoardZone); // only once
    }

    private static SabberStoneCore.Model.Game TenManaGame()
    {
        _ = Db.Value;
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.DRUID, Player2HeroClass = CardClass.MAGE,
            FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false,
        });
        game.StartGame();
        game.Player1.BaseMana = 10;
        game.Player2.BaseMana = 10;
        return game;
    }

    [Fact]
    public void Current_core_choose_one_minion_uses_the_client_option_cards()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var boomkin = SabberStoneCore.Actions.Generic.DrawCard(me, SabberStoneCore.Model.Cards.FromId("CORE_ONY_018"));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.MinionTarget(me, boomkin, me.Opponent.Hero, chooseOne: 2)));
        Assert.Equal(26, me.Opponent.Hero.Health);
        Assert.Single(me.BoardZone);
    }

    [Fact]
    public void Cult_neophyte_taxes_opponent_spells_for_one_turn()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var bolt = SabberStoneCore.Actions.Generic.DrawCard(me.Opponent, SabberStoneCore.Model.Cards.FromName("Frostbolt"));
        var neophyte = SabberStoneCore.Actions.Generic.DrawCard(me, SabberStoneCore.Model.Cards.FromId("CORE_SCH_713"));
        Assert.Equal(2, bolt.Cost);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, neophyte)));
        Assert.Equal(3, bolt.Cost);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.Equal(3, bolt.Cost);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)));
        Assert.Equal(2, bolt.Cost);
    }

    [Fact]
    public void A_dual_type_minion_counts_as_both_types()
    {
        // wiki Minion: dual-type minions "benefit from the effects of both types". Murmy (Murloc, and Undead by its
        // "is Undead" tag 2534) dying makes Acolyte of Death ("After a friendly Undead dies, draw a card") draw.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var acolyte = SabberStoneCore.Actions.Generic.DrawCard(me, SabberStoneCore.Model.Cards.FromId("CORE_RLK_121"));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, acolyte)));
        var murmy = SabberStoneCore.Actions.Generic.DrawCard(me, SabberStoneCore.Model.Cards.FromId("CORE_ULD_723"));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, murmy)));
        Assert.True(((SabberStoneCore.Model.Entities.Minion)murmy).IsRace(Race.MURLOC));
        Assert.True(((SabberStoneCore.Model.Entities.Minion)murmy).IsRace(Race.UNDEAD));
        int hand = me.HandZone.Count;
        var moonfire = SabberStoneCore.Actions.Generic.DrawCard(me, SabberStoneCore.Model.Cards.FromName("Moonfire"));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, moonfire, (SabberStoneCore.Model.Entities.ICharacter)murmy)));
        Assert.Equal(hand + 1, me.HandZone.Count); // Moonfire left, Acolyte drew one (Reborn doesn't stop it)
    }

    [Fact]
    public void Corpses_count_up_and_spends_count_separately_like_the_client()
    {
        // client Player.GetNumAvailableCorpses = CORPSES − CORPSES_SPENT_THIS_GAME: CORPSES never goes down
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.GainCorpses(5);
        me.Corpses -= 3;
        Assert.Equal(2, me.Corpses);
        Assert.Equal(5, me[GameTag.CORPSES]);
        Assert.Equal(3, me[SabberStoneCore.Enums.HsTag.CORPSES_SPENT_THIS_GAME]);
        me.GainCorpses(1);
        Assert.Equal((3, 6), (me.Corpses, me[GameTag.CORPSES]));
    }

    [Fact]
    public void A_hidden_deck_card_never_gets_hand_counters_before_its_draw_is_shown()
    {
        // live: a draw is SHOW_ENTITY first, NUM_TURNS_IN_HAND after; ours sent the counter on the hidden deck card first,
        // and Chillfallen Baron's draw effect then read a card without a type (client fatal → reconnect)
        var rng = new Random(8);
        var engine = new SabberEngineGame(new EngineSetup
        {
            Cards = Db.Value, Scenario = 260, Seed = 8, Rules = RulesEngine.Sabber,
            GameSetupBody = PegasusPackets.Serialize(new GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
            Human = new PlayerSetup { HeroCardId = "HERO_08", Deck = RandomDecks.Build(CardClass.MAGE, rng, CardSet.CORE, CardSet.EXPERT1), AccountHi = 1, AccountLo = 2 },
            Opponent = new PlayerSetup { HeroCardId = "HERO_01", Deck = RandomDecks.Build(CardClass.WARRIOR, rng, CardSet.CORE, CardSet.EXPERT1), IsAi = true },
        });
        var known = new Dictionary<int, (bool Known, int Zone)>();
        var problems = new List<string>();
        var result = new SoakRunner(rng) { Trace = (_, _) => { } }.Play(engine);
        Assert.Empty(result.Problems);
        // the whole game's history as the client is sent it
        var stream = new ClientStream(1, _ => (0, 0, 0));
        foreach (var d in stream.Convert(engine.Rules.PowerHistory.Full))
        {
            if (d.FullEntity is { } f) known[f.Entity] = (!string.IsNullOrEmpty(f.Name), f.Tags.FirstOrDefault(t => t.Name == (int)GAME_TAG.ZONE)?.Value ?? 0);
            if (d.ShowEntity is { } s) known[s.Entity] = (true, s.Tags.FirstOrDefault(t => t.Name == (int)GAME_TAG.ZONE)?.Value ?? 0);
            if (d.HideEntity is { } h) known[h.Entity] = (false, h.Zone);
            if (d.TagChange is { } t && known.TryGetValue(t.Entity, out var k))
            {
                if (t.Tag == (int)GAME_TAG.NUM_TURNS_IN_HAND && !k.Known && k.Zone == (int)TAG_ZONE.DECK)
                    problems.Add($"NUM_TURNS_IN_HAND on hidden deck card {t.Entity}");
                if (t.Tag == (int)GAME_TAG.ZONE) known[t.Entity] = (k.Known, t.Value);
            }
        }
        Assert.Empty(problems);
    }

    [Fact]
    public void Natalie_destroys_a_minion_and_gains_its_health()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var victim = SabberStoneCore.Actions.Generic.DrawCard(me, SabberStoneCore.Model.Cards.FromName("River Crocolisk"));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, victim)));
        var natalie = SabberStoneCore.Actions.Generic.DrawCard(me, SabberStoneCore.Model.Cards.FromId("CORE_EX1_198"));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.MinionTarget(me, natalie, (SabberStoneCore.Model.Entities.ICharacter)victim)));
        var n = Assert.Single(me.BoardZone);
        Assert.Equal(natalie.Card.Health + 3, n.Health);
    }

    // A turn where the only win needs the right board slot: the AI must find it (and doesn't when it may not pick slots).
    private static bool AiWinsThisTurn(bool slots, Action<SabberStoneCore.Model.Game, SabberStoneCore.Model.Entities.Controller> setUp, List<string> log = null)
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        foreach (var card in me.HandZone.ToList()) me.HandZone.Remove(card);
        setUp(game, me);
        var ai = new HsServer.Sabber.Ai.PlannerAi { Slots = slots, Replies = 0, BudgetMs = 2000 };
        for (int i = 0; i < 30 && game.State != State.COMPLETE && game.CurrentPlayer == me; i++)
        {
            var task = ai.Step(game, me);
            bool ok = game.Process(task);
            log?.Add($"{task.FullPrint()} -> {ok}; enemy hero {me.Opponent.Hero.Health}, board [{string.Join(", ", me.BoardZone.Select(m => $"{m.Card.Name} {m.AttackDamage}/{m.Health}{(m.IsExhausted ? " exhausted" : "")}"))}]");
        }
        return me.PlayState == PlayState.WON;
    }

    // a minion already on the board that can attack this turn
    private static SabberStoneCore.Model.Entities.Minion ReadyMinion(SabberStoneCore.Model.Game game, SabberStoneCore.Model.Entities.Controller me, string name)
    {
        var card = SabberStoneCore.Actions.Generic.DrawCard(me, SabberStoneCore.Model.Cards.FromName(name));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, card)));
        var minion = (SabberStoneCore.Model.Entities.Minion)card;
        minion[GameTag.NUM_TURNS_IN_PLAY] = 1; // in play since last turn: no summoning sickness
        minion.IsExhausted = false;
        return minion;
    }

    [Fact]
    public void The_ai_magnetizes_onto_a_mech()
    {
        // Magnetic attaches to the Mech on the minion's right. Golem 2 + Druid's hero power 1 = 3; with Annoy-o-Module on
        // the Golem 4 + 1 = 5 = lethal.
        void SetUp(SabberStoneCore.Model.Game game, SabberStoneCore.Model.Entities.Controller me)
        {
            ReadyMinion(game, me, "Harvest Golem");
            SabberStoneCore.Actions.Generic.DrawCard(me, SabberStoneCore.Model.Cards.FromName("Annoy-o-Module"));
            me.Opponent.Hero.Damage = me.Opponent.Hero.BaseHealth - 5;
        }
        var log = new List<string>();
        bool won = AiWinsThisTurn(slots: true, SetUp, log);
        foreach (string line in log) output.WriteLine(line);
        Assert.True(won);
        Assert.False(AiWinsThisTurn(slots: false, SetUp));
    }

    [Fact]
    public void Live_shapes_2026_10_08_talgath_gigafin_auras_sancazel()
    {
        // Talgath: GDB_472e on its owner's player; an undamaged enemy minion takes double with META CRITICAL_HIT; a damaged one doesn't
        var g = TenManaGame();
        var me = g.CurrentPlayer;
        var talgath = OnBoard(g, me, "GDB_472");
        Assert.Contains(me.AppliedEnchantments ?? new List<SabberStoneCore.Model.Entities.Enchantment>(), e => e.Card.Id == "GDB_472e");
        var yeti = OnBoard(g, me.Opponent, "CS2_182");
        int mark = g.PowerHistory.Full.Count;
        SabberStoneCore.Actions.Generic.DamageCharFunc(me.Hero, yeti, 1, false);
        Assert.Equal(2, yeti.Damage);
        Assert.Contains(g.PowerHistory.Full.Skip(mark), e => e is SabberStoneCore.Kettle.PowerHistoryMetaData { Type: MetaDataType.CRITICAL_HIT });
        SabberStoneCore.Actions.Generic.DamageCharFunc(me.Hero, yeti, 1, false);
        Assert.Equal(3, yeti.Damage);

        // Gigafin: the enemy minions are destroyed (they die), one TSC_962e2 holds them; its Deathrattle summons new copies for the opponent
        var g2 = TenManaGame();
        var p2 = g2.CurrentPlayer;
        var a = OnBoard(g2, p2.Opponent, "CS2_182");
        var b = OnBoard(g2, p2.Opponent, "CS2_120");
        var giga = (SabberStoneCore.Model.Entities.Minion)Give(p2, "TSC_962");
        p2.UsedMana = 0;
        Assert.True(g2.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(p2, giga)));
        Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, a.Zone.Type);
        Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, b.Zone.Type);
        Assert.Single(giga.AppliedEnchantments.Where(e => e.Card.Id == "TSC_962e2"));
        giga.Destroy();
        g2.DeathProcessingAndAuraUpdate();
        Assert.Contains(p2.Opponent.BoardZone, m => m.Card.Id == "CS2_182" && m.Id != a.Id);
        Assert.Contains(p2.Opponent.BoardZone, m => m.Card.Id == "CS2_120" && m.Id != b.Id);

        // Chronological Aura: one OBJECTIVE block a turn (its summon and the count together), the turn it's cast counts
        var g3 = TenManaGame();
        var p3 = g3.CurrentPlayer;
        var aura = Give(p3, "TIME_700");
        p3.UsedMana = 0;
        Assert.True(g3.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(p3, aura)));
        int mark3 = g3.PowerHistory.Full.Count;
        Assert.True(g3.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(p3)));
        var blocks = g3.PowerHistory.Full.Skip(mark3).OfType<SabberStoneCore.Kettle.PowerHistoryBlockStart>().Where(x => x.Source == aura.Id).ToList();
        Assert.Single(blocks);
        Assert.Equal(1, ((SabberStoneCore.Model.Entities.Spell)aura).QuestProgress);
        Assert.Contains(p3.BoardZone, m => m.Card.Id == "TIME_700t");

        // Sanc'Azel: a NEW location after it attacks, and a NEW minion when the location turns back
        var g4 = TenManaGame();
        var p4 = g4.CurrentPlayer;
        var sanc = OnBoard(g4, p4, "VAC_923");
        var loc = SabberStoneCore.Actions.Generic.TurnIntoLocation(sanc, SabberStoneCore.Model.Cards.FromId("VAC_923t"));
        Assert.NotNull(loc);
        Assert.Equal(SabberStoneCore.Enums.Zone.SETASIDE, sanc.Zone.Type);
        var back = SabberStoneCore.Actions.Generic.TurnBackIntoMinion(loc, 0);
        Assert.NotNull(back);
        Assert.NotEqual(sanc.Id, back.Id);
        Assert.Equal(sanc.Id, back[SabberStoneCore.Enums.HsTag.COPIED_FROM_ENTITY_ID]);
        var loc2 = SabberStoneCore.Actions.Generic.TurnIntoLocation(back, SabberStoneCore.Model.Cards.FromId("VAC_923t"));
        Assert.NotEqual(loc.Id, loc2.Id);
    }

    [Fact]
    public void Yoink_uses_the_picked_hero_power_and_swaps_back_to_a_copy_of_yours()
    {
        // live zilliax 113244 123490 / 129381: the picked option entity itself becomes the Hero Power; after two uses a NEW copy of yours
        // (COPIED_FROM_ENTITY_ID = yours) in a second block at index 1
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        int yours = me.Hero.HeroPower.Id;
        string yourCard = me.Hero.HeroPower.Card.Id;
        var yoink = Give(me, "BAR_323");
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, yoink)));
        Assert.NotNull(me.Choice);
        int picked = me.Choice.Choices[0];
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, picked)));
        Assert.Equal(picked, me.Hero.HeroPower.Id);
        for (int use = 0; use < 2; use++)
        {
            me.UsedMana = 0;
            me.Hero.HeroPower.IsExhausted = false;
            var power = me.Options().First(t => t.PlayerTaskType == SabberStoneCore.Tasks.PlayerTasks.PlayerTaskType.HERO_POWER);
            Assert.True(game.Process(power));
            while (me.Choice != null) game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, me.Choice.Choices[0]));
        }
        Assert.Equal(yourCard, me.Hero.HeroPower.Card.Id);
        Assert.NotEqual(yours, me.Hero.HeroPower.Id);
        Assert.Equal(yours, me.Hero.HeroPower[SabberStoneCore.Enums.HsTag.COPIED_FROM_ENTITY_ID]);
    }

    [Fact]
    public void A_magnetic_minion_goes_onto_a_mech_of_a_full_board()
    {
        // wiki Magnetic.txt:7 "can be played while the player's board is full, but only to combine it with another mech"; it enters as
        // an 8th minion for that moment and magnetizes away (user 2026-10-08: it looks like magnetizing onto a board of 7)
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        for (int i = 0; i < 6; i++) OnBoard(game, me, "CS2_186");
        var golem = OnBoard(game, me, "EX1_556"); // Harvest Golem, a Mech, in slot 6
        Assert.True(me.BoardZone.IsFull);
        var module = Give(me, "BOT_911"); // Annoy-o-Module
        var options = me.Options();
        Assert.Contains(options, o => o is SabberStoneCore.Tasks.PlayerTasks.PlayCardTask pc && pc.Source == module && pc.ZonePosition == 6);
        Assert.DoesNotContain(options, o => o is SabberStoneCore.Tasks.PlayerTasks.PlayCardTask pc && pc.Source == module && pc.ZonePosition != 6);
        Assert.False(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, module, null, 2))); // not next to a Mech
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, module, null, 6)));
        Assert.Equal(7, me.BoardZone.Count);
        Assert.Equal(SabberStoneCore.Enums.Zone.SETASIDE, module.Zone.Type);
        Assert.True(golem.HasTaunt && golem.HasDivineShield);
        // a non-Magnetic minion still can't be played on the full board
        var yeti = Give(me, "CS2_182");
        Assert.DoesNotContain(me.Options(), o => o is SabberStoneCore.Tasks.PlayerTasks.PlayCardTask pc && pc.Source == yeti);
    }

    [Fact]
    public void The_ai_places_an_adjacency_buff_between_its_attackers()
    {
        // Flametongue Totem gives the minions next to it +2 Attack: between two ready Crocolisks 4 + 4 + 1 = 9, at the far
        // right only one gets it (2 + 4 + 1 = 7).
        void SetUp(SabberStoneCore.Model.Game game, SabberStoneCore.Model.Entities.Controller me)
        {
            ReadyMinion(game, me, "River Crocolisk");
            ReadyMinion(game, me, "River Crocolisk");
            SabberStoneCore.Actions.Generic.DrawCard(me, SabberStoneCore.Model.Cards.FromName("Flametongue Totem"));
            me.Opponent.Hero.Damage = me.Opponent.Hero.BaseHealth - 9;
        }
        Assert.True(AiWinsThisTurn(slots: true, SetUp));
        Assert.False(AiWinsThisTurn(slots: false, SetUp));
    }

    private static SabberStoneCore.Model.Entities.IPlayable Give(SabberStoneCore.Model.Entities.Controller c, string id) =>
        SabberStoneCore.Actions.Generic.DrawCard(c, SabberStoneCore.Model.Cards.FromId(id));

    [Fact]
    public void Elite_tauren_chieftain_gives_both_players_a_power_chord_that_works()
    {
        // User 2026-10-08: "elite tauren chieftain does literally nothing" (the Power Chords were unscripted placeholders)
        var chords = new[] { "PRO_001a", "PRO_001b", "PRO_001c" };
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        int mine = me.HandZone.Count, theirs = op.HandZone.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "PRO_001"))));
        Assert.Equal(mine + 1, me.HandZone.Count);
        Assert.Equal(theirs + 1, op.HandZone.Count);
        Assert.Contains(me.HandZone[me.HandZone.Count - 1].Card.Id, chords);
        Assert.Contains(op.HandZone[op.HandZone.Count - 1].Card.Id, chords);

        me.UsedMana = 0;
        int board = me.BoardZone.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "PRO_001a")))); // I Am Murloc
        int murlocs = me.BoardZone.Count(m => m.Card.Id == "PRO_001at");
        Assert.InRange(murlocs, 3, Math.Min(5, 7 - board));

        me.UsedMana = 0;
        int hand = me.HandZone.Count;
        int health = op.Hero.Health;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "PRO_001b"), op.Hero))); // Rogues Do It...
        Assert.Equal(health - 4, op.Hero.Health);
        Assert.Equal(hand + 1, me.HandZone.Count); // the chord was given (+1), played (-1), then a card drawn (+1)

        foreach (var m in me.BoardZone.ToList()) m.ToBeDestroyed = true;
        game.DeathProcessingAndAuraUpdate();
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "PRO_001c")))); // Power of the Horde
        var warrior = Assert.Single(me.BoardZone);
        Assert.Contains(warrior.Card.Id, new[] { "CS2_121", "EX1_021", "EX1_023", "EX1_110", "EX1_390", "CS2_179" });
    }

    [Fact]
    public void Anub_rekhan_lets_minions_be_paid_with_armor()
    {
        // User 2026-10-08: "it literally shows the pay armor for card thingy but then it just shows the green button": the
        // options sent had no Armor branch (local-test-20261008-013810 2998: Feugen 5 with 5 Armor, REQ_ENOUGH_MANA)
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var feugen = Give(me, "FP1_015"); // 5 Armor for 5 Armor, as in the user's game
        var before = Give(me, "DREAM_03"); // a 4-cost minion already in hand
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "RLK_659"))));
        Assert.Equal(5, me.Hero.Armor);
        Assert.Equal(2, me.RemainingMana);
        Assert.True(feugen.IsPlayable, "a 5-cost minion with 5 Armor");
        Assert.Equal((SabberStoneCore.Enums.PlayReq)0, SabberStoneCore.Model.PlayErrors.OptionError(me, feugen)); // the option the client gets
        Assert.True(before.IsPlayable, "a 4-cost minion with 5 Armor");
        Assert.Equal((SabberStoneCore.Enums.PlayReq)0, SabberStoneCore.Model.PlayErrors.OptionError(me, before));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, before)));
        Assert.Equal(1, me.Hero.Armor);
        Assert.Equal(2, me.RemainingMana);
        me.Hero.Armor = 5;
        var after = Give(me, "DREAM_03"); // one that comes to hand after him
        game.DeathProcessingAndAuraUpdate();
        Assert.True(after.IsPlayable, "a minion that came to hand later");
        Assert.Equal((SabberStoneCore.Enums.PlayReq)0, SabberStoneCore.Model.PlayErrors.OptionError(me, after));
        me.Hero.Armor = 3; // too little Armor: not playable, whatever Mana is left
        me.UsedMana = 0;
        Assert.False(after.IsPlayable);
        Assert.NotEqual((SabberStoneCore.Enums.PlayReq)0, SabberStoneCore.Model.PlayErrors.OptionError(me, after));
    }

    [Fact]
    public void Vanilla_and_path_of_arthas_cards_follow_their_text()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        SabberStoneCore.Model.Entities.Minion Play(string id)
        {
            me.UsedMana = 0;
            var card = Give(me, id);
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, card)), id);
            return (SabberStoneCore.Model.Entities.Minion)card;
        }
        // Vanilla Murloc Warleader: ALL other Murlocs (both boards) +2/+1, not itself
        var myMurloc = Play("CS2_168"); // Murloc Raider 2/1
        var theirMurloc = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Actions.Generic.DrawCard(op, SabberStoneCore.Model.Cards.FromId("CS2_168"));
        SabberStoneCore.Actions.Generic.SummonBlock.Invoke(game, theirMurloc, -1, null);
        var warleader = Play("VAN_EX1_507");
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(4, myMurloc.AttackDamage);
        Assert.Equal(2, myMurloc.Health);
        Assert.Equal(4, theirMurloc.AttackDamage);
        Assert.Equal(3, warleader.AttackDamage);

        // Vanilla Emperor Cobra: destroys what it damages, without a Poisonous tag
        var cobra = Play("VAN_EX1_170");
        Assert.Equal(0, cobra[GameTag.POISONOUS]);
        var big = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Actions.Generic.DrawCard(op, SabberStoneCore.Model.Cards.FromId("CS2_186")); // War Golem 7/7
        SabberStoneCore.Actions.Generic.SummonBlock.Invoke(game, big, -1, null);
        SabberStoneCore.Actions.Generic.DamageCharFunc(cobra, big, 1, false);
        game.DeathProcessingAndAuraUpdate();
        Assert.True(big.IsDead || big.Zone?.Type == SabberStoneCore.Enums.Zone.GRAVEYARD);

        // Vanilla Shadowform: 2 damage, then 3 when already in Shadowform
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "VAN_EX1_625"))));
        Assert.Equal("EX1_625t", me.Hero.HeroPower.Card.Id);
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "VAN_EX1_625"))));
        Assert.Equal("EX1_625t2", me.Hero.HeroPower.Card.Id);

        // Path of Arthas: Vicious Bloodworm gives the picked hand minion its Attack
        var target = (SabberStoneCore.Model.Entities.Minion)Give(me, "CS2_168");
        me.UsedMana = 0;
        var worm = Give(me, "RLK_711");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, worm, target)));
        Assert.Equal(2 + 3, target.AttackDamage);

        // Blood Boil: at the end of my turn the infected enemy minions take 2, and its Lifesteal heals me
        foreach (var m in op.BoardZone.ToList()) m.ToBeDestroyed = true;
        game.DeathProcessingAndAuraUpdate();
        var golem = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Actions.Generic.DrawCard(op, SabberStoneCore.Model.Cards.FromId("CS2_186"));
        SabberStoneCore.Actions.Generic.SummonBlock.Invoke(game, golem, -1, null);
        me.Hero.Damage = 10;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "RLK_730"))));
        int hp = me.Hero.Health;
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        Assert.Equal(2, golem.Damage);
        Assert.Equal(hp + 2, me.Hero.Health);
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(op));
        Assert.Equal(2, golem.Damage); // not at the end of its own turn
    }

    [Fact]
    public void An_aborted_secret_stays_hidden_and_in_play()
    {
        // Secret.txt: an aborted Secret "will simply remain in place without taking effect, revealing itself, or giving any
        // indication" (rule book: Noble Sacrifice "aborts if there is no space"); live: no Secret block is ever empty
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        me.UsedMana = 0;
        var noble = Give(me, "EX1_130");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, noble)));
        for (int i = 0; i < 7; i++)
        {
            var m = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Actions.Generic.DrawCard(me, SabberStoneCore.Model.Cards.FromId("CS2_186"));
            SabberStoneCore.Actions.Generic.SummonBlock.Invoke(game, m, -1, null);
        }
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        op.UsedMana = 0;
        var charger = Give(op, "CS2_124"); // Wolfrider 3/1 Charge
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(op, charger)));
        int before = game.PowerHistory.Full.Count;
        game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(op, (SabberStoneCore.Model.Entities.ICharacter)charger, me.Hero));
        Assert.Equal(27, me.Hero.Health); // the attack went through
        Assert.Contains(me.SecretZone.GetAll(), x => x.Id == noble.Id); // still up
        Assert.DoesNotContain(game.PowerHistory.Full.Skip(before),
            e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart b && b.Source == noble.Id); // no block: not revealed
    }

    [Fact]
    public void Crusader_aura_plays_its_holy_buff_in_its_trigger_like_live()
    {
        // live-20261008-issues 13901-13931: the aura's TRIGGER block holds SUB_SPELL ReuseFX_Holy_BuffImpact_Heal_Small_Super
        // around the +2/+1 (user: "a weird just out of nowhere buff"); a recorded trigger-only effect (RecordedSubSpellBlocks.tsv)
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.UsedMana = 0;
        var aura = Give(me, "LEG_TTN_908");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, aura)));
        me.UsedMana = 0;
        var wolf = Give(me, "CS2_124"); // Wolfrider: Charge
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, wolf)));
        int from = game.PowerHistory.Full.Count;
        game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(me, (SabberStoneCore.Model.Entities.ICharacter)wolf, me.Opponent.Hero, skipPrePhase: false));
        var h = game.PowerHistory.Full.Skip(from).ToList();
        int trigger = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart { BlockType: BlockType.TRIGGER } b && b.Source == aura.Id);
        Assert.True(trigger >= 0, "the aura's trigger");
        int fx = h.FindIndex(trigger, e => e is SabberStoneCore.Kettle.PowerHistorySubSpellStart sub && sub.Prefab.StartsWith("ReuseFX_Holy_BuffImpact_Heal_Small_Super:"));
        Assert.True(fx > trigger, "the holy buff inside the trigger");
        Assert.Equal(5, ((SabberStoneCore.Model.Entities.Minion)wolf).AttackDamage);
        // its countdown at the end of the turn: its own block, no keyword, effect index 1 (live 14185: the client plays nothing)
        from = game.PowerHistory.Full.Count;
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        var countdown = game.PowerHistory.Full.Skip(from).OfType<SabberStoneCore.Kettle.PowerHistoryBlockStart>()
            .First(b => b.BlockType == BlockType.TRIGGER && b.Source == aura.Id);
        Assert.Equal(0, countdown.TriggerKeyword);
        Assert.Equal(1, countdown.Index);
        Assert.False(countdown.ShowInHistory);
    }

    [Theory]
    [InlineData("EX1_610", false)] // Explosive Trap: queued against the hero, still goes off after Noble Sacrifice (rule book l.578)
    [InlineData("EX1_554", true)]  // Snake Trap: the new defender is a minion: a new Proposed Attack Event (rule book l.510)
    public void A_redirect_keeps_queued_secrets_and_starts_a_new_proposed_attack(string second, bool needsNewEvent)
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        me.UsedMana = 0;
        var noble = Give(me, "EX1_130");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, noble)));
        me.UsedMana = 0;
        var other = Give(me, second);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, other)));
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        op.UsedMana = 0;
        var golem = Give(op, "CS2_186"); // War Golem 7/7
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(op, golem)));
        ((SabberStoneCore.Model.Entities.Minion)golem).IsExhausted = false;
        game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(op, (SabberStoneCore.Model.Entities.ICharacter)golem, me.Hero, skipPrePhase: false));
        Assert.Equal(30, me.Hero.Health); // the Defender took the hit
        Assert.DoesNotContain(me.SecretZone.GetAll(), x => x.Id == noble.Id);
        Assert.DoesNotContain(me.SecretZone.GetAll(), x => x.Id == other.Id); // the second Secret went off too
        if (needsNewEvent) Assert.Equal(3, me.BoardZone.Count(m => m.Card.Id == "EX1_554t"));
    }

    [Fact]
    public void Counterspell_cancels_the_casters_cast_triggers_with_one_pause()
    {
        // wiki Counterspell: "Since patch 11.2.0, any effects which activate from the spell being cast or completed will not be triggered"
        // (the rule book's l.357 is 9.2's older rule); live (live-20261006-dredge 3680-3720): the countered card's SLUSH_TIME 3000 only
        // after the Secret's 5000
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        me.UsedMana = 0;
        var counter = Give(me, "EX1_287");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, counter)));
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        op.UsedMana = 0;
        var wyrm = (SabberStoneCore.Model.Entities.Minion)Give(op, "NEW1_012"); // Mana Wyrm 1/3
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(op, wyrm)));
        op.UsedMana = 0;
        var bolt = Give(op, "CS2_029"); // Fireball
        int health = me.Hero.Health, from = game.PowerHistory.Full.Count;
        game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(op, bolt, me.Hero));
        Assert.Equal(health, me.Hero.Health); // countered
        Assert.Equal(1, wyrm.AttackDamage); // its cast trigger doesn't go off
        var pauses = game.PowerHistory.Full.Skip(from).OfType<SabberStoneCore.Kettle.PowerHistoryMetaData>()
            .Where(m => m.Type == MetaDataType.SLUSH_TIME && m.Data == 3000 && m.Info.Contains(bolt.Id)).ToList();
        Assert.Single(pauses);
    }

    [Fact]
    public void Event_cards_use_their_shared_rules()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        SabberStoneCore.Model.Entities.Minion OnBoard(SabberStoneCore.Model.Entities.Controller side, string id)
        {
            var m = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Actions.Generic.DrawCard(side, SabberStoneCore.Model.Cards.FromId(id));
            SabberStoneCore.Actions.Generic.SummonBlock.Invoke(game, m, -1, null);
            return m;
        }
        // Dark Terraforming: 5 on the target, 4 on each neighbour, 3 on the next
        var row = new[] { OnBoard(op, "CS2_186"), OnBoard(op, "CS2_186"), OnBoard(op, "CS2_186"), OnBoard(op, "CS2_186") };
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "BE_EVENT_102"), row[1])));
        Assert.Equal(new[] { 4, 5, 4, 3 }, row.Select(m => m.Damage).ToArray());

        // Arrival of the Titans: only spells for the rest of the turn (the minions in hand locked, like live), back next turn
        var minionInHand = Give(me, "CS2_186");
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "BE_EVENT_101"))));
        Assert.False(minionInHand.IsPlayable);
        Assert.Equal(1, minionInHand[(GameTag)1020]); // LITERALLY_UNPLAYABLE
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(op));
        Assert.Equal(0, minionInHand[(GameTag)1020]);

        // Runi: the hand's minions leave and come back next turn with +5/+5
        foreach (var m in me.BoardZone.ToList()) m.ToBeDestroyed = true;
        game.DeathProcessingAndAuraUpdate();
        var traveller = (SabberStoneCore.Model.Entities.Minion)Give(me, "CS2_168"); // Murloc Raider 2/1
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "TIME_EVENT_998"))));
        Assert.DoesNotContain(me.HandZone.GetAll(), x => x.Id == traveller.Id);
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(op));
        Assert.Contains(me.HandZone.GetAll(), x => x.Id == traveller.Id);
        Assert.Equal(7, traveller.AttackDamage);

        // Dragon Soul's Essences: playing one casts the adjoining ones in hand (not a separated one)
        me.UsedMana = 0;
        foreach (var x in me.HandZone.ToList()) { me.HandZone.Remove(x); me.GraveyardZone.Add(x); }
        var black = Give(me, "CATA_EVENT_110t5");   // 12 Armor
        var green = Give(me, "CATA_EVENT_110t6");   // 8/8 Dragon
        Give(me, "CS2_168");                         // a gap
        var lone = Give(me, "CATA_EVENT_110t5");    // not adjoining
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, black)));
        Assert.Equal(12, me.Hero.Armor);
        Assert.Contains(me.BoardZone.GetAll(), m => m.Card.Id == "CATA_EVENT_110t6t");
        Assert.Contains(me.HandZone.GetAll(), x => x.Id == lone.Id);

        // live order and nesting (live-20261008-120539 19719): the right neighbour first, then the left; each cast inside the previous block
        foreach (var x in me.HandZone.ToList()) { me.HandZone.Remove(x); me.GraveyardZone.Add(x); }
        var left = Give(me, "CATA_EVENT_110t5");
        var played = Give(me, "CATA_EVENT_110t5");
        var right = Give(me, "CATA_EVENT_110t5");
        me.UsedMana = 0;
        int from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, played)));
        var h = game.PowerHistory.Full.Skip(from).ToList();
        int Power(int id) => h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart { BlockType: BlockType.POWER } b && b.Source == id);
        Assert.True(Power(played.Id) < Power(right.Id) && Power(right.Id) < Power(left.Id), $"{Power(played.Id)} {Power(right.Id)} {Power(left.Id)}");
        int depth = 0, leftDepth = -1;
        for (int i = 0; i < h.Count; i++)
        {
            if (h[i] is SabberStoneCore.Kettle.PowerHistoryBlockStart) { depth++; if (i == Power(left.Id)) leftDepth = depth; }
            else if (h[i] is SabberStoneCore.Kettle.PowerHistoryBlockEnd) depth--;
        }
        Assert.True(leftDepth >= 4, $"nested: depth {leftDepth}"); // PLAY > POWER(played) > POWER(right) > POWER(left)
    }

    [Fact]
    public void Gorehowl_sees_the_final_defender_after_noble_sacrifice()
    {
        // rule book, Attack Event: Gorehowl "has a condition of 'attacker is your hero and defender is a minion'", queued only once the
        // final defender is known; Noble Sacrifice turns a hero attack into a minion attack: Gorehowl loses Attack, not Durability
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "EX1_130"))));
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        op.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(op, Give(op, "EX1_411"))));
        var axe = op.Hero.Weapon;
        game.Process(SabberStoneCore.Tasks.PlayerTasks.HeroAttackTask.Any(op, me.Hero, skipPrePhase: false));
        Assert.Equal(30, me.Hero.Health);
        Assert.Equal(1, axe.Durability);
        Assert.Equal(6, axe.AttackDamage);
    }

    [Fact]
    public void Event_and_vanilla_fixes_follow_the_text()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        // Sharp-Eyed Lookout names the top card of your deck while in hand
        var lookout = Give(me, "EDR_950");
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(me.DeckZone.TopCard.Card.AssetId, lookout[(GameTag)2921]);
        Assert.Equal(1, lookout[GameTag.USE_ALTERNATE_CARD_TEXT]);

        // Watfin: one option shows a wrong number; picking it gives that card and Watfin +1/+1
        me.UsedMana = 0;
        var watfin = (SabberStoneCore.Model.Entities.Minion)Give(me, "JAIL_EVENT_100");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, watfin)));
        var options = me.Choice.Choices.Select(id => game.IdEntityDic[id]).ToList();
        var odd = Assert.Single(options, o => o.Cost != o.Card.Cost || o[GameTag.ATK] != o.Card.ATK || o[GameTag.HEALTH] != o.Card.Health);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, odd.Id)));
        Assert.Equal(4, watfin.AttackDamage); // a 3/2 with +1/+1
        Assert.Contains(me.HandZone.GetAll(), x => x.Id == odd.Id);

        // Vanilla Eaglehorn Bow: "Whenever a Secret is revealed" — the opponent's too
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, Give(me, "VAN_EX1_536"))));
        var bow = me.Hero.Weapon;
        int durability = bow.Durability;
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        op.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(op, Give(op, "EX1_130")))); // Noble Sacrifice
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(op));
        game.Process(SabberStoneCore.Tasks.PlayerTasks.HeroAttackTask.Any(me, op.Hero, skipPrePhase: false));
        Assert.Equal(durability, bow.Durability); // +1 for the revealed Secret, -1 for the attack

        // Destructive Phoenix: burns at the start of your turns (wiki); the third start discards it and summons a Phoenix
        foreach (var m in me.BoardZone.ToList()) m.ToBeDestroyed = true;
        game.DeathProcessingAndAuraUpdate();
        var fuel = Give(me, "CS2_168");
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, Give(me, "CATA_EVENT_001"), fuel)));
        for (int i = 0; i < 2; i++)
        {
            game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
            game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(op));
            Assert.Contains(me.HandZone.GetAll(), x => x.Id == fuel.Id);
        }
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(op));
        Assert.DoesNotContain(me.HandZone.GetAll(), x => x.Id == fuel.Id);
        Assert.Equal(2, me.BoardZone.Count(m => m.Card.Id == "CATA_EVENT_001"));
    }

    [Fact]
    public void Overheal_triggers_inside_the_heal_and_is_deferrable_like_live()
    {
        // live (all 23 OVERHEAL blocks, live-20261006-yogg-cthun 33099): TRIGGER TriggerKeyword=OVERHEAL ShowInHistory IsDeferrable between
        // PREHEALING n and PREHEALING 0
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var clergy = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Actions.Generic.DrawCard(me, SabberStoneCore.Model.Cards.FromId("CORE_CS3_014"));
        SabberStoneCore.Actions.Generic.SummonBlock.Invoke(game, clergy, -1, null);
        int from = game.PowerHistory.Full.Count;
        clergy.TakeHeal(me.Hero, 3);
        game.DeathProcessingAndAuraUpdate();
        var h = game.PowerHistory.Full.Skip(from).ToList();
        int pre = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: GameTag.PREHEALING } t && t.EntityId == clergy.Id && t.Value > 0);
        int block = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart { TriggerKeyword: 2821 });
        int post = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: GameTag.PREHEALING, Value: 0 } t && t.EntityId == clergy.Id);
        Assert.True(pre >= 0 && pre < block && block < post, $"PREHEALING {pre}, OVERHEAL {block}, PREHEALING 0 {post}");
        Assert.True(((SabberStoneCore.Kettle.PowerHistoryBlockStart)h[block]).IsDeferrable);
    }

    [Fact]
    public void Ice_block_prevents_the_damage_first_inside_its_block()
    {
        // live (live-20261006-yogg-cthun 31464-31490): the Secret's block opens with PREDAMAGE 0, then its Immune enchantment
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        me.UsedMana = 0;
        var block = Give(me, "EX1_295");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, block)));
        me.Hero.Damage = 27;
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        op.UsedMana = 0;
        int from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(op, Give(op, "CS2_029"), me.Hero)));
        Assert.Equal(3, me.Hero.Health);
        var h = game.PowerHistory.Full.Skip(from).ToList();
        int secret = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart b && b.Source == block.Id);
        Assert.True(secret >= 0);
        var first = h.Skip(secret + 1).First(e => e is SabberStoneCore.Kettle.PowerHistoryTagChange || e is SabberStoneCore.Kettle.PowerHistoryFullEntity);
        Assert.True(first is SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: GameTag.PREDAMAGE, Value: 0 } t && t.EntityId == me.Hero.Id, "PREDAMAGE 0 first");
    }

    [Fact]
    public void Every_effect_marks_what_it_touched_like_live()
    {
        // live (9,628 LAST_AFFECTED_BY in the captures, each right after 4741 +1): damage, destroy, buff and bounce all mark their target
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        SabberStoneCore.Model.Entities.Minion Enemy()
        {
            var m = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Actions.Generic.DrawCard(op, SabberStoneCore.Model.Cards.FromId("CS2_186"));
            SabberStoneCore.Actions.Generic.SummonBlock.Invoke(game, m, -1, null);
            return m;
        }
        void Cast(string id, SabberStoneCore.Model.Entities.ICharacter target, out SabberStoneCore.Model.Entities.IPlayable spell)
        {
            me.UsedMana = 0;
            spell = Give(me, id);
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, spell, target)));
        }
        var golem = Enemy();
        Cast("CS2_029", golem, out var fireball);                     // damage
        Assert.Equal(fireball.Id, golem[GameTag.LAST_AFFECTED_BY]);
        Assert.Equal(1, golem[(GameTag)4741]);
        Cast("CS2_092", golem, out var kings);                         // buff (Blessing of Kings)
        Assert.Equal(kings.Id, golem[GameTag.LAST_AFFECTED_BY]);
        Assert.Equal(2, golem[(GameTag)4741]);
        int from = game.PowerHistory.Full.Count;
        Cast("EX1_581", golem, out var sap);                           // bounce (Sap): sent before the move (the card's tags reset in hand)
        Assert.Contains(game.PowerHistory.Full.Skip(from), e => e is SabberStoneCore.Kettle.PowerHistoryTagChange
            { Tag: GameTag.LAST_AFFECTED_BY } t && t.EntityId == golem.Id && t.Value == sap.Id);
        var other = Enemy();
        Cast("CS2_076", other, out var assassinate);                   // destroy
        Assert.Equal(assassinate.Id, other[GameTag.LAST_AFFECTED_BY]);
        Assert.Equal(1, other[(GameTag)4741]);
    }

    [Fact]
    public void Siamat_picks_two_different_keywords()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var siamat = (SabberStoneCore.Model.Entities.Minion)Give(me, "CORE_ULD_178");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, siamat)));
        Assert.Equal(4, me.Choice.Choices.Count);
        int first = me.Choice.Choices[0];
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, first)));
        Assert.Equal(3, me.Choice.Choices.Count);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, me.Choice.Choices[0])));
        Assert.Null(me.Choice);
        int gained = new[] { GameTag.WINDFURY, GameTag.DIVINE_SHIELD, GameTag.TAUNT, GameTag.RUSH }.Count(tag => siamat[tag] > 0);
        Assert.Equal(2, gained);
    }

    [Fact]
    public void Warmaul_challenger_battles_to_the_death()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var foe = Give(me.Opponent, "CORE_CS2_120"); // River Crocolisk 2/3
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me.Opponent, foe)));
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent));
        var challenger = Give(me, "CORE_BT_120");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.MinionTarget(me, challenger, (SabberStoneCore.Model.Entities.ICharacter)foe)));
        Assert.Empty(me.Opponent.BoardZone);
        var c = Assert.Single(me.BoardZone);
        Assert.Equal(10 - 2 * 3, c.Health); // three rounds of 1 damage kill the 3-health crocolisk
    }

    [Fact]
    public void Bulwark_of_azzinoth_takes_hero_damage_as_durability()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var bulwark = Give(me, "CORE_BT_781");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, bulwark)));
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        var bolt = Give(me.Opponent, "CORE_CS2_024");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me.Opponent, bolt, me.Hero)));
        Assert.Equal(30, me.Hero.Health);
        Assert.Equal(3, me.Hero.Weapon.Durability);
    }

    [Fact]
    public void Steamcleaner_destroys_only_cards_added_to_decks()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        int before = me.DeckZone.Count;
        SabberStoneCore.Actions.Generic.ShuffleIntoDeck.Invoke(me, null, SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromName("Frostbolt")));
        Assert.Equal(before + 1, me.DeckZone.Count);
        var cleaner = Give(me, "CORE_REV_946");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, cleaner)));
        Assert.Equal(before, me.DeckZone.Count);
    }

    [Fact]
    public void Resistance_aura_lasts_two_enemy_turns()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var bolt = Give(me.Opponent, "CORE_CS2_024");
        var aura = Give(me, "TTN_851");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, aura)));
        Assert.Equal(3, bolt.Cost);
        for (int enemyTurn = 1; enemyTurn <= 2; enemyTurn++)
        {
            game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
            Assert.Equal(3, bolt.Cost);
            game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent));
        }
        Assert.Equal(2, bolt.Cost);
    }

    private static SabberStoneCore.Model.Game DemonHunterGame()
    {
        _ = Db.Value;
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.DEMONHUNTER, Player2HeroClass = CardClass.MAGE,
            FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false,
        });
        game.StartGame();
        game.Player1.BaseMana = 10;
        game.Player2.BaseMana = 10;
        return game;
    }

    [Fact]
    public void Demon_hunter_plays_as_illidan_with_demon_claws()
    {
        var game = DemonHunterGame();
        var me = game.CurrentPlayer;
        Assert.Equal("HERO_10", me.Hero.Card.Id);
        Assert.Equal("HERO_10bp", me.Hero.HeroPower.Card.Id);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.HeroPowerTask.Any(me)));
        Assert.Equal(1, me.Hero.AttackDamage);
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        Assert.Equal(0, me.Hero.AttackDamage);
    }

    [Fact]
    public void Outcast_triggers_only_from_the_edge_of_the_hand()
    {
        var game = DemonHunterGame();
        var me = game.CurrentPlayer;
        while (me.HandZone.Count < 3) Give(me, "CORE_CS2_120");
        int deck = me.DeckZone.Count;
        var middle = Give(me, "CORE_BT_491"); // Spectral Sight: now the right-most card
        Give(me, "CORE_CS2_120");               // ... and now in the middle
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, middle)));
        Assert.Equal(deck - 1, me.DeckZone.Count);

        var edge = Give(me, "CORE_BT_491"); // right-most
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, edge)));
        Assert.Equal(deck - 3, me.DeckZone.Count);
    }

    [Fact]
    public void Eye_beam_costs_1_on_the_edge_of_the_hand()
    {
        var game = DemonHunterGame();
        var me = game.CurrentPlayer;
        while (me.HandZone.Count < 3) Give(me, "CORE_CS2_120");
        var beam = Give(me, "CORE_BT_801");
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)); // any processed task updates costs
        Assert.Equal(1, beam.Cost);
        Give(me, "CORE_CS2_120");
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)); // my turn: the draw lands right-most too
        Assert.Equal(3, beam.Cost);
    }

    [Fact]
    public void Kayn_sunfury_lets_friendly_attacks_ignore_taunt()
    {
        var game = DemonHunterGame();
        var me = game.CurrentPlayer;
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        var wall = Give(me.Opponent, "CORE_CS2_179"); // Sen'jin Shieldmasta (Taunt)
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me.Opponent, wall)));
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent));
        var kayn = Give(me, "CORE_BT_187");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, kayn)));
        Assert.Contains(me.Options(), o => o is SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask a && a.Target == me.Opponent.Hero);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(me, kayn, me.Opponent.Hero)));
        Assert.Equal(30 - kayn.Card.ATK, me.Opponent.Hero.Health);
    }

    [Fact]
    public void Dormant_minions_sleep_untouchable_then_awaken_like_just_summoned()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var fiend = (SabberStoneCore.Model.Entities.Minion)Give(me, "CORE_BT_156"); // Dormant for 2 turns, Rush
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, fiend)));
        Assert.True(fiend.Untouchable);
        Assert.Equal(0, me.BoardZone.CountExceptUntouchables);
        Assert.DoesNotContain(me.Options(), o => o is SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask);

        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        var ping = Give(me.Opponent, "CS2_025"); // Arcane Explosion: 1 to all enemy minions
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me.Opponent, ping)));
        Assert.Equal(0, fiend.Damage);
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)); // my turn 1 after: still asleep
        Assert.True(fiend.Untouchable);
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)); // my turn 2 after: awake
        Assert.False(fiend.Untouchable);
        Assert.Equal(1, me.BoardZone.CountExceptUntouchables);
        Assert.True(fiend.AttackableByRush); // Rush: may attack minions only, as if just summoned
    }

    private static SabberStoneCore.Model.Entities.IPlayable OnDeckTop(SabberStoneCore.Model.Entities.Controller c, string id)
    {
        var card = SabberStoneCore.Model.Entities.Entity.FromCard(c, SabberStoneCore.Model.Cards.FromId(id));
        c.DeckZone.Add(card);
        return card;
    }

    [Fact]
    public void The_darkness_starts_dormant_and_only_candles_wake_it()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        var darkness = (SabberStoneCore.Model.Entities.Minion)Give(me, "LOOT_526");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, darkness)));
        Assert.True(darkness.Untouchable); // Starts Dormant (Card.StartsDormant)
        var candles = op.DeckZone.GetAll().Where(x => x.Card.Id == "LOOT_526t").ToList();
        Assert.Equal(3, candles.Count); // Battlecry: 3 Candles into the enemy deck
        var awaits = darkness.AppliedEnchantments.Single(e => e.Card.Id == "LOOT_526e");
        Assert.Equal(3, awaits[GameTag.SCORE_VALUE_1]); // card data: "Awaken when your opponent draws @ Candles"

        // turns never count for it (wiki Dormant: only turn-counted ones count down at the start of a turn)
        for (int i = 0; i < 4; i++) game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(game.CurrentPlayer));
        Assert.True(darkness.Untouchable);

        // each Candle drawn counts up (client shows 3 − n); the replacement draw may itself be a Candle
        int Left() => op.DeckZone.GetAll().Count(x => x.Card.Id == "LOOT_526t");
        while (Left() > 0)
        {
            Assert.True(darkness.Untouchable);
            Assert.Equal(3 - Left(), awaits[GameTag.SCORE_VALUE_2]);
            var candle = op.DeckZone.GetAll().First(x => x.Card.Id == "LOOT_526t");
            SabberStoneCore.Actions.Generic.Draw(op, candle);
            Assert.NotEqual(SabberStoneCore.Enums.Zone.HAND, candle.Zone?.Type);  // Casts When Drawn: cast, not kept
        }
        Assert.DoesNotContain(op.HandZone.GetAll(), x => x.Card.Id == "LOOT_526t"); // each was replaced by a draw
        Assert.False(darkness.Untouchable);
        Assert.Equal(0, darkness[GameTag.DORMANT]);
        Assert.DoesNotContain(darkness.AppliedEnchantments ?? new List<SabberStoneCore.Model.Entities.Enchantment>(), e => e.Card.Id == "LOOT_526e");
    }

    [Fact]
    public void A_starts_dormant_minion_pulled_into_play_stays_dormant()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        foreach (var p in op.HandZone.GetAll().ToArray()) op.HandZone.Remove(p);
        var darkness = (SabberStoneCore.Model.Entities.Minion)Give(op, "LOOT_526");
        Play(game, me, "CFM_790"); // Dirty Rat: the opponent summons a random minion from their hand (no Battlecry)
        Assert.Equal(SabberStoneCore.Enums.Zone.PLAY, darkness.Zone?.Type);
        Assert.True(darkness.Untouchable);
        Assert.DoesNotContain(me.DeckZone.GetAll(), x => x.Card.Id == "LOOT_526t");
        for (int i = 0; i < 4; i++) game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(game.CurrentPlayer));
        Assert.True(darkness.Untouchable);
        // wiki Dormant: "Dormant minions with no way to awaken themselves cannot be randomly summoned or transformed into"
        Assert.True(SabberStoneCore.Model.Cards.FromId("LOOT_526").NeverRandomlyOnBoard);
        Assert.True(SabberStoneCore.Model.Cards.FromId("BT_850").NeverRandomlyOnBoard); // Magtheridon
        Assert.False(SabberStoneCore.Model.Cards.FromId("SW_077").NeverRandomlyOnBoard); // Stockades Prisoner awakens by itself
    }

    [Fact]
    public void Dormant_api_turns_condition_and_copies_of_awakened_minions()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var yeti = (SabberStoneCore.Model.Entities.Minion)Give(me, "CS2_182");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, yeti)));
        SabberStoneCore.Actions.Generic.MakeDormant(yeti); // a condition Dormant: turns don't wake it
        Assert.True(yeti.Untouchable);
        for (int i = 0; i < 4; i++) game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(game.CurrentPlayer));
        Assert.True(yeti.Untouchable);
        SabberStoneCore.Actions.Generic.Awaken(yeti);
        Assert.False(yeti.Untouchable);
        Assert.Equal(1, me.BoardZone.CountExceptUntouchables);

        // a copy made straight into play of an awakened Dormant minion doesn't sleep (wiki Dormant's exception)
        me = game.CurrentPlayer;
        var fiend = (SabberStoneCore.Model.Entities.Minion)Give(me, "CORE_BT_156");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, fiend)));
        SabberStoneCore.Actions.Generic.Awaken(fiend);
        var copy = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Actions.Generic.Copy(me, fiend, fiend, SabberStoneCore.Enums.Zone.PLAY);
        Assert.Equal(SabberStoneCore.Enums.Zone.PLAY, copy.Zone?.Type);
        Assert.False(copy.Untouchable);
        // a fresh one (not a copy) still starts Dormant
        var fresh = (SabberStoneCore.Model.Entities.Minion)Give(me, "CORE_BT_156");
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, fresh)));
        Assert.True(fresh.Untouchable);

        // Slagmaw: "Excavate to awaken N turns sooner" finds its Dormant self (the board's GetAll leaves Dormant ones out)
        var slagmaw = (SabberStoneCore.Model.Entities.Minion)Give(me, "WW_375");
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, slagmaw)));
        int left = SabberStoneCore.Actions.Generic.DormantTurnsLeft(slagmaw);
        Assert.True(left > 0);
        SabberStoneCore.Actions.Generic.Excavate(me, slagmaw);
        Assert.Equal(System.Math.Max(0, left - slagmaw.Card.ExcavateAwakensSooner), SabberStoneCore.Actions.Generic.DormantTurnsLeft(slagmaw));
    }

    [Fact]
    public void Dormant_goes_to_sleep_counts_and_awakens_like_live()
    {
        // Live (live-20261004-night, Slagmaw WW_375): its own TRIGGER block (index 1) attaches its counter WW_375e (SCORE_VALUE_1
        // 6, DORMANT_AWAKEN_CONDITION_ENCHANT), turns Taunt / Rush off, DORMANT 1 + UNTOUCHABLE 1. Each of its owner's turns:
        // the counter's TRIGGER block (index 0, TRIGGER_VISUAL) with the progress-counter effect raises SCORE_VALUE_2; at the
        // total it awakens inside it: UNTOUCHABLE 0, DORMANT 0, DORMANT_AWAKENED_THIS_TURN 1, the counter gone, keywords back.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var full = game.PowerHistory.Full;
        var slagmaw = (SabberStoneCore.Model.Entities.Minion)Give(me, "WW_375");
        int from = full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, slagmaw)));
        var enter = full.Skip(from).OfType<SabberStoneCore.Kettle.PowerHistoryBlockStart>()
            .Single(b => b.BlockType == BlockType.TRIGGER && b.Source == slagmaw.Id);
        Assert.Equal(1, enter.Index);
        var counter = Assert.Single(slagmaw.AppliedEnchantments, e => e.Card.Id == "WW_375e");
        Assert.Equal(6, counter[GameTag.SCORE_VALUE_1]);
        Assert.Equal(1, counter[SabberStoneCore.Actions.Generic.DORMANT_AWAKEN_CONDITION_ENCHANT]);
        Assert.Equal(1, slagmaw[GameTag.UNTOUCHABLE]);
        Assert.Equal(0, slagmaw[GameTag.TAUNT]);
        Assert.Equal(0, slagmaw[GameTag.RUSH]);
        Assert.False(slagmaw.HasTaunt);

        for (int turn = 1; turn <= 6; turn++)
        {
            game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(game.CurrentPlayer));
            from = full.Count;
            game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(game.CurrentPlayer)); // my turn starts
            var count = full.Skip(from).OfType<SabberStoneCore.Kettle.PowerHistoryBlockStart>()
                .FirstOrDefault(b => b.Source == counter.Id && b.BlockType == BlockType.TRIGGER);
            Assert.NotNull(count);
            Assert.Equal((int)GameTag.TRIGGER_VISUAL, count.TriggerKeyword);
            Assert.Contains(full.Skip(from).OfType<SabberStoneCore.Kettle.PowerHistorySubSpellStart>(), x => x.Prefab.Contains("ProgressCounter"));
            if (turn < 6)
            {
                Assert.Equal(turn, counter[GameTag.SCORE_VALUE_2]);
                Assert.True(slagmaw.Untouchable);
            }
        }
        Assert.False(slagmaw.Untouchable);
        Assert.Equal(0, slagmaw[GameTag.UNTOUCHABLE]);
        Assert.Equal(1, slagmaw[SabberStoneCore.Actions.Generic.DORMANT_AWAKENED_THIS_TURN]);
        Assert.Equal(1, slagmaw[GameTag.TAUNT]);
        Assert.Equal(1, slagmaw[GameTag.RUSH]);
        Assert.DoesNotContain(slagmaw.AppliedEnchantments ?? new List<SabberStoneCore.Model.Entities.Enchantment>(), e => e.Card.Id == "WW_375e");
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(game.CurrentPlayer));
        Assert.Equal(0, slagmaw[SabberStoneCore.Actions.Generic.DORMANT_AWAKENED_THIS_TURN]); // cleared at the turn's end
    }

    [Fact]
    public void A_darkness_candle_is_counted_then_its_detector_draws_the_replacement()
    {
        // Live (live-20261004-night): The Darkness goes Dormant in its own block (keyword 1652) and attaches "Darkness Candle
        // Detect" (LOOT_526et) to its owner. A Candle drawn: its own block only sets it aside; then "Darkness Awaits" counts
        // (TRIGGER_VISUAL); then the detector's block (START_DRAW ...) draws the replacement.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        var full = game.PowerHistory.Full;
        var darkness = (SabberStoneCore.Model.Entities.Minion)Give(me, "LOOT_526");
        int from = full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, darkness)));
        var enter = full.Skip(from).OfType<SabberStoneCore.Kettle.PowerHistoryBlockStart>()
            .First(b => b.BlockType == BlockType.TRIGGER && b.Source == darkness.Id);
        Assert.Equal(1652, enter.TriggerKeyword);
        var detect = Assert.Single(me.AppliedEnchantments, e => e.Card.Id == "LOOT_526et");
        var candle = op.DeckZone.GetAll().First(x => x.Card.Id == "LOOT_526t");
        from = full.Count;
        SabberStoneCore.Actions.Generic.Draw(op, candle);
        var blocks = full.Skip(from).OfType<SabberStoneCore.Kettle.PowerHistoryBlockStart>().Where(b => b.BlockType == BlockType.TRIGGER).ToList();
        int candleBlock = blocks.FindIndex(b => b.Source == candle.Id);
        int countBlock = blocks.FindIndex(b => b.Source != candle.Id && b.TriggerKeyword == (int)GameTag.TRIGGER_VISUAL);
        int drawBlock = blocks.FindIndex(b => b.Source == detect.Id);
        Assert.True(candleBlock >= 0 && countBlock > candleBlock && drawBlock > countBlock);
        Assert.Contains(full.Skip(from).OfType<SabberStoneCore.Kettle.PowerHistoryMetaData>(), m => m.Type == MetaDataType.START_DRAW && m.Info.Contains(candle.Id));
    }

    [Fact]
    public void When_you_draw_this_cards_stay_in_hand_and_casts_when_drawn_ones_are_replaced()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        // Flame Leviathan: "When you draw this, deal 2 damage to all characters except Mechs" — it stays in hand
        int hp = me.Hero.Health, opHp = me.Opponent.Hero.Health;
        var leviathan = OnDeckTop(me, "GVG_007");
        SabberStoneCore.Actions.Generic.Draw(me, leviathan);
        Assert.Equal(SabberStoneCore.Enums.Zone.HAND, leviathan.Zone?.Type);
        Assert.Equal(hp - 2, me.Hero.Health);
        Assert.Equal(opHp - 2, me.Opponent.Hero.Health);

        // Corrupted Blood: take 3, the engine draws its replacement, then "After you draw, shuffle two copies"
        hp = me.Hero.Health;
        int hand = me.HandZone.Count;
        var blood = OnDeckTop(me, "TRL_541t");
        int bloods = me.DeckZone.GetAll().Count(x => x.Card.Id == "TRL_541t");
        SabberStoneCore.Actions.Generic.Draw(me, blood);
        Assert.Equal(hp - 3, me.Hero.Health);
        Assert.Equal(hand + 1, me.HandZone.Count);
        Assert.DoesNotContain(me.HandZone.GetAll(), x => x.Card.Id == "TRL_541t" && x.Id == blood.Id);
        Assert.Equal(bloods - 1 + 2, me.DeckZone.GetAll().Count(x => x.Card.Id == "TRL_541t"));
    }

    private static SabberStoneCore.Model.Game DeathKnightGame()
    {
        _ = Db.Value;
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.DEATHKNIGHT, Player2HeroClass = CardClass.MAGE,
            FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false,
        });
        game.StartGame();
        game.Player1.BaseMana = 10;
        game.Player2.BaseMana = 10;
        return game;
    }

    [Fact]
    public void Death_knight_ghoul_charge_ghoul_dies_at_end_of_turn_and_leaves_a_corpse()
    {
        var game = DeathKnightGame();
        var me = game.CurrentPlayer;
        Assert.Equal("HERO_11", me.Hero.Card.Id);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.HeroPowerTask.Any(me)));
        var ghoul = Assert.Single(me.BoardZone);
        Assert.True(ghoul.HasCharge);
        Assert.Equal(0, me.Corpses);
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        Assert.Empty(me.BoardZone);
        Assert.Equal(1, me.Corpses);
    }

    [Fact]
    public void Corpses_raise_tokens_that_leave_no_corpse_and_falric_doubles_gains()
    {
        var game = DeathKnightGame();
        var me = game.CurrentPlayer;
        me.Corpses = 3;
        var army = Give(me, "RLK_060"); // raise up to 5 as 2/2 Rush ghouls
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, army)));
        Assert.Equal(3, me.BoardZone.Count);
        Assert.Equal(0, me.Corpses);
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        var consecration = Give(me.Opponent, "CS2_093"); // Consecration: 2 to all enemies
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me.Opponent, consecration)));
        Assert.Empty(me.BoardZone);
        Assert.Equal(0, me.Corpses); // raised ghouls leave no corpse

        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent));
        var falric = Give(me, "CORE_EDR_003");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, falric)));
        var yeti = Give(me, "CORE_CS2_120");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, yeti)));
        var bolt = Give(me, "CORE_CS2_029"); // Fireball
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, bolt, (SabberStoneCore.Model.Entities.ICharacter)yeti)));
        Assert.Equal(2, me.Corpses); // doubled
    }

    [Fact]
    public void Death_metal_knight_costs_health_after_the_hero_was_healed()
    {
        var game = DeathKnightGame();
        var me = game.CurrentPlayer;
        me.Hero.Damage = 10;
        var knight = Give(me, "CORE_ETC_523");
        me.BaseMana = 0;
        Assert.False(knight.IsPlayable);
        me.Hero.TakeHeal(me.Hero, 2);
        Assert.True(knight.IsPlayable);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, knight)));
        Assert.Equal(30 - 8 - 3, me.Hero.Health);
    }

    [Fact]
    public void Steamwheedle_sniper_lets_steady_shot_target_minions()
    {
        _ = Db.Value;
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.HUNTER, Player2HeroClass = CardClass.MAGE,
            FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false,
        });
        game.StartGame();
        game.Player1.BaseMana = 10;
        game.Player2.BaseMana = 10;
        var me = game.CurrentPlayer;
        bool Targeted() => me.Options().Any(o => o is SabberStoneCore.Tasks.PlayerTasks.HeroPowerTask h && h.Target != null);
        Assert.False(Targeted());
        var sniper = Give(me, "GVG_087");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, sniper)));
        Assert.True(Targeted());
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.HeroPowerTask.Any(me, (SabberStoneCore.Model.Entities.ICharacter)sniper)));
        Assert.Equal(sniper.Card.Health - 2, ((SabberStoneCore.Model.Entities.Minion)sniper).Health);
        Assert.Equal(30, me.Opponent.Hero.Health);
    }

    [Fact]
    public void Invoke_triggers_galakrond_power_and_upgrades_it_every_two_invokes()
    {
        _ = Db.Value;
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.WARRIOR, Player2HeroClass = CardClass.MAGE,
            FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false,
        });
        game.StartGame();
        game.Player1.BaseMana = 10;
        var me = game.CurrentPlayer;
        var gala = Give(me, "DRG_650"); // Galakrond, the Unbreakable (Might: +3 Attack this turn)
        var chopper = Give(me, "DRG_021"); // Ritual Chopper: Battlecry: Invoke Galakrond
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, chopper)));
        Assert.Equal(1, me[GameTag.INVOKE_COUNTER]);
        Assert.Equal(1 + 3, me.Hero.AttackDamage); // Chopper's 1 + Might's 3
        Assert.Equal("DRG_650", gala.Card.Id);

        var maniac = Give(me, "DRG_050"); // Devoted Maniac: Invoke
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, maniac)));
        Assert.Equal(2, me[GameTag.INVOKE_COUNTER]);
        Assert.Contains(me.HandZone, p => p.Card.Id == "DRG_650t2");
    }

    [Fact]
    public void Sidequest_sits_in_the_secret_zone_and_rewards_on_completion()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var cause = Give(me, "DRG_008"); // Righteous Cause: summon 5 minions -> your minions +1/+1
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, cause)));
        Assert.Contains(me.SecretZone, p => p.Card.Id == "DRG_008");
        for (int i = 0; i < 5; i++)
        {
            var wisp = Give(me, "CS2_231"); // Wisp 1/1
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, wisp)));
        }
        Assert.DoesNotContain(me.SecretZone, p => p.Card.Id == "DRG_008");
        Assert.All(me.BoardZone, m => Assert.Equal(2, m.AttackDamage));
    }

    /// <summary>Kills <paramref name="n"/> friendly Wisps (Infuse counts friendly deaths).</summary>
    private static void KillFriendlyMinions(SabberStoneCore.Model.Game game, SabberStoneCore.Model.Entities.Controller me, int n)
    {
        for (int i = 0; i < n; i++)
        {
            var wisp = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("CS2_231"));
            SabberStoneCore.Actions.Generic.SummonBlock.Invoke(game, wisp, -1, wisp);
            wisp.Destroy();
            game.DeathProcessingAndAuraUpdate();
        }
    }

    [Fact]
    public void Huntsman_altimor_infuses_in_place_twice()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var altimor = Give(me, "CORE_REV_353"); // Infuse (4): another Gargon; Infuse (8): all three
        KillFriendlyMinions(game, me, 4);
        Assert.Equal(1, altimor[SabberStoneCore.Model.Game.INFUSED]);
        Assert.Equal(4, altimor[GameTag.TAG_SCRIPT_DATA_NUM_1]); // 4 more to the second threshold
        KillFriendlyMinions(game, me, 4);
        Assert.Equal(2, altimor[SabberStoneCore.Model.Game.INFUSED]);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, altimor)));
        Assert.Equal(3, me.BoardZone.Count(m => m.Card.Id.StartsWith("REV_353t")));
    }

    [Fact]
    public void Sire_denathrius_endlessly_infuses()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        Give(me, "CORE_REV_906"); // Battlecry: 5 damage amongst enemies. Endlessly Infuse (2): 1 more.
        KillFriendlyMinions(game, me, 2);
        var sire = me.HandZone.Single(p => p.Card.Name == "Sire Denathrius");
        Assert.Equal("REV_906t", sire.Card.Id);
        Assert.Equal(6, sire[GameTag.TAG_SCRIPT_DATA_NUM_2]);
        KillFriendlyMinions(game, me, 2);
        Assert.Equal(7, sire[GameTag.TAG_SCRIPT_DATA_NUM_2]);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, sire)));
        Assert.Equal(30 - 7, me.Opponent.Hero.Health); // the enemy board is empty: all 7 hit the hero
    }

    /// <summary>Ids scripts name without needing the card: labels (Suspicious cards) and Wild originals matched by id.</summary>
    private static readonly HashSet<string> NamedButNotLoaded = new() { "REV_000", "REV_002", "REV_006", "REV_353" };

    [Fact]
    public void Every_card_id_an_hsserver_script_names_is_loaded_by_the_engine()
    {
        _ = Db.Value;
        string dir = Path.Combine(TestEnvironment.ProjectRoot, "Server", "src", "HsServer.Rules", "src", "CardSets", "HsServer");
        if (!Directory.Exists(dir)) dir = Path.Combine(TestEnvironment.ProjectRoot, "src", "HsServer.Rules", "src", "CardSets", "HsServer");
        Assert.True(Directory.Exists(dir), dir);
        // Quoted card ids, except CardValues keys (V("REV_507", ...)) which name the card a value belongs to.
        var id = new System.Text.RegularExpressions.Regex(@"(?<!V\()""((?:CORE_)?[A-Z][A-Za-z0-9]*_[A-Za-z0-9_]*\d[A-Za-z0-9_]*)""");
        var missing = Directory.GetFiles(dir, "*.cs")
            .SelectMany(f => id.Matches(File.ReadAllText(f)).Select(m => (file: Path.GetFileName(f), id: m.Groups[1].Value)))
            .Where(x => SabberStoneCore.Model.Cards.TryFromId(x.id) == null && !NamedButNotLoaded.Contains(x.id))
            .Distinct().ToList();
        foreach (var (file, card) in missing) output.WriteLine($"{file}: {card}");
        Assert.Empty(missing);
    }

    [Fact]
    public void Options_carry_error_params_rush_and_disabled_hero_power_like_live()
    {
        // Live (captures, 2026-10-01 survey): Shadow Word: Death's REQ_TARGET_MIN_ATTACK carries errorParam 5; a Rush minion
        // that can't hit the hero gets REQ_MINION_TARGET (Soldier of the Infinite; REQ_CHARGE_TARGET never occurs); a
        // disabled Hero Power gets REQ_NOT_DISABLED_HERO_POWER.
        var game = TenManaGame();
        var me = game.Player1;
        var op = game.Player2;
        var death = Give(me, "EX1_622"); // Shadow Word: Death
        SabberStoneCore.Model.Entities.Entity.FromCard(op, SabberStoneCore.Model.Cards.FromId("CS2_182"), zone: op.BoardZone); // 4 Attack
        SabberStoneCore.Model.Entities.Entity.FromCard(op, SabberStoneCore.Model.Cards.FromId("CS2_231"), zone: op.BoardZone); // Wisp
        var big = SabberStoneCore.Model.Entities.Entity.FromCard(op, SabberStoneCore.Model.Cards.FromId("EX1_543"), zone: op.BoardZone); // 7 Attack
        me.UsedMana = 0;
        var spark = Give(me, "BOT_102t"); // Spark, Rush
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, spark)));

        var all = SabberStoneCore.Kettle.PowerOptionsBuilder.AllOptions(game, me.Options());
        SabberStoneCore.Kettle.PowerSubOption Main(int entity) => all.PowerOptionList.Single(o => o.MainOption?.EntityId == entity && o.MainOption.PowerKeyword != 1720).MainOption;
        var minAttack = Main(death.Id).TargetErrors.Where(t => t.Error == (int)PlayReq.REQ_TARGET_MIN_ATTACK).ToList();
        Assert.NotEmpty(minAttack);
        Assert.All(minAttack, t => Assert.Equal(5, t.Param));
        Assert.DoesNotContain(minAttack, t => t.Id == big.Id);
        Assert.Contains(Main(spark.Id).TargetErrors, t => t.Id == op.Hero.Id && t.Error == (int)PlayReq.REQ_MINION_TARGET);

        me.HeroPowerDisabled = true; // as an aura sets it (Mind Spike...)
        all = SabberStoneCore.Kettle.PowerOptionsBuilder.AllOptions(game, me.Options());
        Assert.Equal((int)PlayReq.REQ_NOT_DISABLED_HERO_POWER, Main(me.Hero.HeroPower.Id).PlayError);
    }

    [Fact]
    public void Options_list_what_cant_be_used_and_why_like_live()
    {
        // Live AllOptions (RESEARCH/captures/trade-lifesteal-20260930): unusable entities come with their first failing
        // requirement, and a targeted option lists the characters it can't target with the reason.
        var game = TenManaGame();
        var me = game.Player1;
        var op = game.Player2;
        var backstab = Give(me, "CS2_072");      // 0: an undamaged minion
        var fireball = Give(me, "CS2_029");      // 4 mana
        var archer = Give(me, "CS2_189");        // Battlecry
        var mine = SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("CS2_182"), zone: me.BoardZone);
        var theirs = SabberStoneCore.Model.Entities.Entity.FromCard(op, SabberStoneCore.Model.Cards.FromId("CS2_182"), zone: op.BoardZone);
        me.UsedMana = 9;

        var all = SabberStoneCore.Kettle.PowerOptionsBuilder.AllOptions(game, me.Options());
        SabberStoneCore.Kettle.PowerSubOption Main(int entity) => all.PowerOptionList.Single(o => o.MainOption?.EntityId == entity && o.MainOption.PowerKeyword != 1720).MainOption;
        int Req(PlayReq r) => (int)r;

        Assert.Equal(Req(PlayReq.REQ_ENOUGH_MANA), Main(fireball.Id).PlayError);
        Assert.Equal(0, Main(archer.Id).PlayError);
        Assert.Equal((int)GameTag.BATTLECRY, Main(archer.Id).PowerKeyword);
        Assert.Equal(Req(PlayReq.REQ_ATTACK_GREATER_THAN_0), Main(me.Hero.Id).PlayError);
        Assert.Equal(Req(PlayReq.REQ_ENOUGH_MANA), Main(me.Hero.HeroPower.Id).PlayError);
        Assert.Equal(Req(PlayReq.REQ_NOT_MINION_JUST_PLAYED), Main(mine.Id).PlayError);
        foreach (int id in new[] { op.Hero.Id, op.Hero.HeroPower.Id, theirs.Id })
            Assert.Equal(Req(PlayReq.REQ_YOUR_TURN), Main(id).PlayError);

        var stab = Main(backstab.Id);
        Assert.Equal(0, stab.PlayError);
        Assert.Equal(new[] { mine.Id, theirs.Id }.OrderBy(i => i), stab.Targets.OrderBy(i => i));
        Assert.Contains((me.Hero.Id, Req(PlayReq.REQ_MINION_TARGET)), stab.TargetErrors.Select(t => (t.Id, t.Error)));
        Assert.Contains((op.Hero.Id, Req(PlayReq.REQ_MINION_TARGET)), stab.TargetErrors.Select(t => (t.Id, t.Error)));

        // A target requirement past the first one counts too (the multicast predicate only checked its last).
        ((SabberStoneCore.Model.Entities.Minion)theirs).Damage = 1;
        all = SabberStoneCore.Kettle.PowerOptionsBuilder.AllOptions(game, me.Options());
        Assert.Contains((theirs.Id, Req(PlayReq.REQ_UNDAMAGED_TARGET)), Main(backstab.Id).TargetErrors.Select(t => (t.Id, t.Error)));
    }

    [Fact]
    public void Summons_land_where_blizzard_puts_them()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        SabberStoneCore.Model.Entities.Minion OnBoard(string id) =>
            (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId(id), zone: me.BoardZone);
        string Board() => string.Join(",", me.BoardZone.GetAll().Select(m => m.Card.Id));

        // A minion's Battlecry summons right next to it (played on the left of a Yeti).
        var yeti = OnBoard("CS2_182");
        var hunter = Give(me, "CS2_196"); // Razorfen Hunter: summon a 1/1 Boar
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, hunter, null, 0)));
        Assert.Equal("CS2_196,CS2_boar,CS2_182", Board());
        // Live order: the Yeti moves over first, then the Boar is created already in PLAY at its slot (no snap from the right).
        var last = game.PowerHistory.Last;
        int boarCreated = last.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryFullEntity f && f.Entity.Name == "CS2_boar");
        int yetiMoved = last.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: GameTag.ZONE_POSITION, Value: 3 } t && t.EntityId == yeti.Id);
        Assert.True(boarCreated > yetiMoved && yetiMoved >= 0, $"boar {boarCreated}, yeti {yetiMoved}");
        var boarTags = ((SabberStoneCore.Kettle.PowerHistoryFullEntity)last[boarCreated]).Entity.Tags;
        Assert.Equal((int)SabberStoneCore.Enums.Zone.PLAY, boarTags[GameTag.ZONE]);
        Assert.Equal(2, boarTags[GameTag.ZONE_POSITION]);

        // A spell summons on the far right.
        var image = Give(me, "CS2_027"); // Mirror Image: two 0/2s
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, image)));
        Assert.Equal("CS2_196,CS2_boar,CS2_182,CS2_mirror,CS2_mirror", Board());

        // A Deathrattle summons in the slot the minion had (Haunted Creeper, second from the left).
        foreach (var m in me.BoardZone.GetAll().Skip(1).ToList()) m.Damage = 100;
        game.DeathProcessingAndAuraUpdate();
        var creeper = OnBoard("FP1_002");
        OnBoard("CS2_182");
        creeper.Damage = 100;
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal("CS2_196,FP1_002t,FP1_002t,CS2_182", Board());

        // A Location breaking frees Zuramat in its slot (on the left here).
        foreach (var m in me.BoardZone.GetAll().ToList()) m.Damage = 100;
        game.DeathProcessingAndAuraUpdate();
        var prison = (SabberStoneCore.Model.Entities.Location)Give(me, "JAIL_887");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, prison, null, 0)));
        OnBoard("CS2_182");
        prison.Damage = prison.BaseHealth;
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal("JAIL_887t2,CS2_182", Board());

        // A card Zuramat plays is brought into play by another card: it lands on the far right and its Battlecry does
        // NOT trigger (wiki Battlecry / Tess Greymane notes; Zuramat's effect is filed under Summon / Cast / Equip).
        var zuramat = me.BoardZone[0];
        zuramat[HsTag.PRISON_DISCARD_1] = SabberStoneCore.Model.Cards.FromId("CS2_196").AssetId;
        int beforePlay = game.PowerHistory.Full.Count;
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        Assert.Equal("JAIL_887t2,CS2_182,CS2_196", Board());
        // ...and it enters like a summon: FULL_ENTITY already in PLAY at its slot with CREATOR = Zuramat, no SHOW_ENTITY
        // after (the client drew a blue placeholder next to Zuramat and slid it over).
        var played = me.BoardZone[2];
        var playedFull = game.PowerHistory.Full.Skip(beforePlay).OfType<SabberStoneCore.Kettle.PowerHistoryFullEntity>().Single(f => f.Entity.Id == played.Id);
        Assert.Equal((int)SabberStoneCore.Enums.Zone.PLAY, playedFull.Entity.Tags[SabberStoneCore.Enums.GameTag.ZONE]);
        Assert.Equal(3, playedFull.Entity.Tags[SabberStoneCore.Enums.GameTag.ZONE_POSITION]);
        Assert.Equal(zuramat.Id, playedFull.Entity.Tags[SabberStoneCore.Enums.GameTag.CREATOR]);
        Assert.DoesNotContain(game.PowerHistory.Full.Skip(beforePlay), e => e is SabberStoneCore.Kettle.PowerHistoryShowEntity s && s.Entity.Id == played.Id);
        // shown big at the side / in the history tile, but only once it is on the board (user, 2026-10-01)
        var playHistory = game.PowerHistory.Full.Skip(beforePlay).ToList();
        int bigCard = playHistory.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryMetaData { Type: SabberStoneCore.Enums.MetaDataType.SHOW_BIG_CARD } m && m.Info.Contains(played.Id));
        Assert.True(bigCard > playHistory.IndexOf(playedFull), "SHOW_BIG_CARD after the minion's FULL_ENTITY");

        // The same engine action for every "play" effect: Murozond replays the opponent's Hunter on the far right.
        var op = game.CurrentPlayer;
        var theirHunter = Give(op, "CS2_196");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(op, theirHunter, null, 0)));
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(op));
        foreach (var m in me.BoardZone.GetAll().Skip(1).ToList()) m.Damage = 100;
        game.DeathProcessingAndAuraUpdate();
        var murozond = Give(me, "DRG_090");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, murozond, null, 0)));
        Assert.Equal("DRG_090,JAIL_887t2,CS2_196", Board()); // no Boar: a replayed Battlecry doesn't trigger
    }

    [Fact]
    public void Silence_removes_every_keyword_and_charge_minions_go_back_to_sleep()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        SabberStoneCore.Model.Entities.Minion Play(string id)
        {
            var card = Give(me, id);
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, card)));
            return (SabberStoneCore.Model.Entities.Minion)card;
        }
        void Silence(SabberStoneCore.Model.Entities.Minion m)
        {
            var silence = Give(me, "EX1_332"); // Silence
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, silence, m)));
        }

        var cobra = Play("EX1_170");        // Emperor Cobra: Poisonous
        Silence(cobra);
        Assert.False(cobra.Poisonous);

        var watcher = Play("EX1_045");      // Ancient Watcher: Can't attack
        Silence(watcher);
        Assert.False(watcher.CantAttack);

        var rider = Play("CS2_124");        // Wolf Rider: Charge — it could attack now...
        Assert.False(rider.IsExhausted);
        Silence(rider);                     // ...but without Charge it's summoning sick again
        Assert.True(rider.IsExhausted);
        Assert.DoesNotContain(me.Options(), o => o.PlayerTaskType == SabberStoneCore.Tasks.PlayerTasks.PlayerTaskType.MINION_ATTACK && o.Source == rider);
    }

    [Fact]
    public void A_transformed_minion_is_a_fresh_minion()
    {
        // Rules E9: nothing the old minion had survives; it is newest in order of play and counts as just played.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var yeti = (SabberStoneCore.Model.Entities.Minion)Give(me, "CS2_182"); // Chillwind Yeti
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, yeti)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)));
        Assert.False(yeti.IsExhausted);
        yeti.IsSilenced = true;
        yeti.HasDivineShield = true;
        yeti.HasWindfury = true;
        yeti.IsFrozen = true;
        yeti.IsImmune = true;
        yeti.Damage = 2;
        int oldOrder = yeti.OrderOfPlay;

        var polymorph = Give(me, "CS2_022"); // Polymorph: transform into a 1/1 Sheep
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, polymorph, yeti)));
        var sheep = (SabberStoneCore.Model.Entities.Minion)me.BoardZone[0];
        Assert.Equal(yeti.Id, sheep.Id);
        Assert.Equal("CS2_tk1", sheep.Card.Id);
        Assert.False(sheep.IsSilenced);
        Assert.False(sheep.HasDivineShield);
        Assert.False(sheep.HasWindfury);
        Assert.False(sheep.IsFrozen);
        Assert.False(sheep.IsImmune);
        Assert.Equal(1, sheep.Health);
        Assert.Equal(1, sheep.AttackDamage);
        Assert.True(sheep.OrderOfPlay > oldOrder);
        Assert.Equal(0, sheep[SabberStoneCore.Enums.GameTag.NUM_TURNS_IN_PLAY]);
        Assert.DoesNotContain(me.Options(), o => o.PlayerTaskType == SabberStoneCore.Tasks.PlayerTasks.PlayerTaskType.MINION_ATTACK && o.Source == sheep);

        // Live's CHANGE_ENTITY lists the old minion's tags with their new values (FROZEN 0, DIVINE_SHIELD 0...).
        var change = game.PowerHistory.Last.OfType<SabberStoneCore.Kettle.PowerHistoryChangeEntity>().Single(c => c.Entity.Id == sheep.Id);
        Assert.Equal(0, change.Entity.Tags[SabberStoneCore.Enums.GameTag.FROZEN]);
        Assert.Equal(0, change.Entity.Tags[SabberStoneCore.Enums.GameTag.DIVINE_SHIELD]);
        Assert.Equal(0, change.Entity.Tags[SabberStoneCore.Enums.GameTag.DAMAGE]);
        Assert.Equal(1, change.Entity.Tags[(SabberStoneCore.Enums.GameTag)2321]); // TIMES_BEEN_TRANSFORMED
    }

    [Fact]
    public void Damage_armor_absorbs_still_counts_as_damage_dealt()
    {
        // Rules E1: Armor absorbs damage, but the damage still counts (Lifesteal heals it all, Freeze still freezes).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var water = (SabberStoneCore.Model.Entities.Minion)Give(me, "CS2_033"); // Water Elemental 3/6: Freeze
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, water)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)));
        water.HasLifeSteal = true;
        me.Hero.Damage = 10;
        var enemy = me.Opponent.Hero;
        enemy.Armor = 5;

        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(me, water, enemy)));
        Assert.Equal(2, enemy.Armor);
        Assert.Equal(enemy.BaseHealth, enemy.Health);
        Assert.True(enemy.IsFrozen);
        Assert.Equal(7, me.Hero.Damage); // Lifesteal healed the full 3

        // Live: META_DATA DAMAGE carries the whole 3, then ARMOR; no DAMAGE tag change (Health didn't drop).
        var history = game.PowerHistory.Last;
        int meta = history.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryMetaData { Type: SabberStoneCore.Enums.MetaDataType.DAMAGE } m && m.Info.Contains(enemy.Id));
        Assert.True(meta >= 0);
        Assert.Equal(3, ((SabberStoneCore.Kettle.PowerHistoryMetaData)history[meta]).Data);
        int armor = history.FindIndex(meta, e => e is SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: SabberStoneCore.Enums.GameTag.ARMOR } t && t.EntityId == enemy.Id);
        Assert.True(armor > meta);
        Assert.DoesNotContain(history, e => e is SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: SabberStoneCore.Enums.GameTag.DAMAGE } t && t.EntityId == enemy.Id);
    }

    [Fact]
    public void Deathrattle_summons_resolve_their_summon_triggers_right_after_the_death_phase()
    {
        // Rules A5: a Death Phase is an outermost Phase, so its summons get a Summon Resolution Step right away.
        // (Savannah Highmane's Hyenas are Beasts; Haunted Creeper's Spiders have no minion type.)
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var buzzard = Give(me, "CS2_237");  // Starving Buzzard: whenever you summon a Beast, draw a card
        var highmane = Give(me, "EX1_534");  // Savannah Highmane: Deathrattle: summon two 2/2 Hyenas
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, buzzard)));
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, highmane)));
        me.UsedMana = 0;
        var fireball = Give(me, "CS2_029"); // Fireball: 6 damage
        int hand = me.HandZone.Count - 1;   // after the Fireball leaves
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, fireball, (SabberStoneCore.Model.Entities.ICharacter)highmane)));
        Assert.Equal(3, me.BoardZone.Count);          // Buzzard + 2 Hyenas
        Assert.Equal(hand + 2, me.HandZone.Count);    // both draws already happened
    }

    [Fact]
    public void A_minion_that_deals_damage_itself_keeps_stealth()
    {
        // Patch 11.0 (wiki Stealth; user 2026-10-09): only attacking breaks Stealth — a minion's own damaging effects don't.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var infiltrator = (SabberStoneCore.Model.Entities.Minion)Give(me, "EX1_010"); // Worgen Infiltrator: Stealth
        var juggler = (SabberStoneCore.Model.Entities.Minion)Give(me, "NEW1_019");   // Knife Juggler
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, infiltrator)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, juggler)));
        juggler.HasStealth = true;

        var moonfire = Give(me, "CS2_008"); // Moonfire: my spell deals the damage, not the minion
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, moonfire, me.Opponent.Hero)));
        Assert.True(infiltrator.HasStealth);
        Assert.True(juggler.HasStealth);

        var wisp = Give(me, "CS2_231");     // summoning makes the Juggler throw a knife: its own damage, Stealth stays
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, wisp)));
        Assert.True(juggler.HasStealth);
        Assert.True(infiltrator.HasStealth);

        // A Titan's ability is its action instead of an attack: one that deals damage breaks its Stealth (wiki Titan.txt:42).
        var g2 = TenManaGame();
        var p2 = g2.CurrentPlayer;
        var golganneth = (SabberStoneCore.Model.Entities.Minion)Give(p2, "TTN_800");
        Assert.True(g2.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(p2, golganneth)));
        OnBoard(g2, p2.Opponent, "CS2_182");
        golganneth.HasStealth = true;
        int oceans = SabberStoneCore.Actions.Generic.TitanAbilityIndex(golganneth, SabberStoneCore.Model.Cards.FromId("TTN_800t"));
        Assert.True(g2.Process(p2.Options().OfType<SabberStoneCore.Tasks.PlayerTasks.TitanAbilityTask>().First(o => o.AbilityIndex == oceans)));
        Assert.False(golganneth.HasStealth);
    }

    [Fact]
    public void Sudden_betrayal_waits_for_a_minion_not_a_hero()
    {
        // "When a minion attacks your hero" (as Vaporize): an enemy hero's attack passes it by (soak 4200334: it fired and threw)
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        me.UsedMana = 0;
        var betrayal = Give(me, "LOOT_210");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, betrayal)));
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        op.UsedMana = 0;
        var axe = Give(op, "CS2_106"); // Fiery War Axe 3/2
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(op, axe)));
        int health = me.Hero.Health;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.HeroAttackTask.Any(op, me.Hero)));
        Assert.Equal(health - 3, me.Hero.Health);
        Assert.Contains(me.SecretZone.GetAll(), x => x.Id == betrayal.Id); // still up
    }

    [Fact]
    public void A_card_set_by_rotation_comes_from_the_calendar()
    {
        // CardSetTiming lists only a pre / post row around a rotation for ~1,200 cards; the set is the row whose event is on now
        // (2026-10-09: they had no set — out of Standard pools, "from the past" for Timeways).
        TenManaGame();
        Assert.Equal(1635, (int)SabberStoneCore.Model.Cards.FromId("BT_035").Set);        // Chaos Strike: Basic → Legacy (2021)
        Assert.Equal(1637, (int)SabberStoneCore.Model.Cards.FromId("CORE_TRL_240").Set);  // into Core at the 2026 rotation
        Assert.Equal(1810, (int)SabberStoneCore.Model.Cards.FromId("CS3_028").Set);       // out of Core (hidden set) in 2026
        Assert.False(SabberStoneCore.Model.Cards.FromThePast(SabberStoneCore.Model.Cards.FromId("CORE_TRL_240")));
        Assert.True(SabberStoneCore.Model.Cards.FromThePast(SabberStoneCore.Model.Cards.FromId("CS3_028")));
    }

    [Fact]
    public void A_quest_takes_one_of_the_five_hero_zone_slots()
    {
        // User rule / wiki D4: Secrets, Sidequests, the Quest (and Sigils) share 5 slots.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        void Cast(string id)
        {
            me.UsedMana = 0;
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, id))));
        }
        Cast("UNG_028");                                 // Open the Waygate (Quest)
        foreach (string secret in new[] { "EX1_287", "EX1_289", "EX1_294", "EX1_594" }) // Counterspell, Ice Barrier, Mirror Entity, Vaporize
            Cast(secret);
        Assert.True(me.SecretZone.IsFull);

        me.UsedMana = 0;
        var fifth = Give(me, "EX1_295");                // Ice Block: no slot left
        Assert.False(fifth.IsPlayableByPlayer);
        Assert.Equal(SabberStoneCore.Enums.PlayReq.REQ_SECRET_ZONE_CAP, SabberStoneCore.Model.PlayErrors.OptionError(me, fifth));
        Assert.DoesNotContain(me.Options(), o => o is SabberStoneCore.Tasks.PlayerTasks.PlayCardTask t && t.Source == fifth);
    }

    [Fact]
    public void A_neutral_hero_card_keeps_your_class()
    {
        // Rules B6: the class becomes the hero card's, except Neutral and multi-class hero cards (Reno, Lone Ranger).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        Assert.Equal(CardClass.DRUID, me.HeroClass);
        var neutralHero = SabberStoneCore.Model.Cards.All.First(c => c.Type == SabberStoneCore.Enums.CardType.HERO && c.Collectible &&
                                                                   c.Class == CardClass.NEUTRAL && c.MultiClassGroup == 0);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, SabberStoneCore.Actions.Generic.DrawCard(me, neutralHero))));
        Assert.Equal(neutralHero.Id, me.Hero.Card.Id);
        Assert.Equal(CardClass.DRUID, me.HeroClass);

        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, Give(me, "ICC_833")))); // Frost Lich Jaina
        Assert.Equal(CardClass.MAGE, me.HeroClass);
    }

    [Fact]
    public void A_replaced_hero_power_stops_acting()
    {
        // Rules C1 / A5: a replaced Hero Power is removed from play and can't trigger (Dalaran Heist passives rely on it).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        int PlayWisp()
        {
            me.UsedMana = 0;
            int before = me.HandZone.Count;
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "CS2_231"))));
            return me.HandZone.Count - before; // -1 for the Wisp itself, +1 per card added
        }
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, Give(me, "GIL_504")))); // Hagatha: Bewitch (passive)
        Assert.Equal(1, PlayWisp()); // Bewitch added a Shaman spell

        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, Give(me, "ICC_833")))); // Frost Lich Jaina replaces it
        Assert.Equal(0, PlayWisp()); // Bewitch is gone
    }

    [Fact]
    public void Reborn_returns_with_1_health_and_its_full_maximum()
    {
        // Rules D2: resummoned with 1 Health; its maximum Health is the original maximum (it can be healed back up).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var card = SabberStoneCore.Model.Cards.All.First(c => c.Type == SabberStoneCore.Enums.CardType.MINION && c.Collectible &&
            c[SabberStoneCore.Enums.GameTag.REBORN] == 1 && c.Health >= 3 && c.Health <= 5 && c.Power?.DeathrattleTask == null &&
            SabberStoneCore.Model.Cards.FromId(c.Id) == c);
        var reborn = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Actions.Generic.DrawCard(me, card);
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, reborn)));
        me.UsedMana = 0;
        var fireball = Give(me, "CS2_029"); // Fireball: 6 damage
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, fireball, reborn)));

        var back = me.BoardZone.Single(m => m.Card.Id == card.Id);
        Assert.NotEqual(reborn.Id, back.Id);
        Assert.Equal(1, back.Health);
        Assert.Equal(card.Health, back.BaseHealth);
        Assert.Equal(0, back[SabberStoneCore.Enums.GameTag.REBORN]);
        Assert.Equal(1, back[SabberStoneCore.Enums.GameTag.HAS_BEEN_REBORN]);

        me.UsedMana = 0;
        var heal = Give(me, "CS2_007"); // Healing Touch: restore 8 Health
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, heal, back)));
        Assert.Equal(card.Health, back.Health);
    }

    [Fact]
    public void Immune_and_zero_damage_leave_divine_shield_alone()
    {
        // Rules DH2a: Immune or 0 proposed damage prevents the event before Divine Shield is checked.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var squire = (SabberStoneCore.Model.Entities.Minion)Give(me, "EX1_008"); // Argent Squire: Divine Shield
        var other = (SabberStoneCore.Model.Entities.Minion)Give(me, "EX1_008");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, squire)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, other)));
        squire.IsImmune = true;

        var moonfire = Give(me, "CS2_008"); // Moonfire: 1 damage
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, moonfire, squire)));
        Assert.True(squire.HasDivineShield);

        Assert.Equal(0, SabberStoneCore.Actions.Generic.DamageCharFunc(me.Hero, other, 0, false));
        Assert.True(other.HasDivineShield);
    }

    [Fact]
    public void Poisonous_works_for_any_damage_the_minion_deals()
    {
        // Rules D2: Poisonous applies to all damage from the source (Battlecries, triggers...), not only combat.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        var ogre = (SabberStoneCore.Model.Entities.Minion)Give(me.Opponent, "CS2_200"); // Boulderfist Ogre 6/7
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me.Opponent, ogre)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)));

        var archer = (SabberStoneCore.Model.Entities.Minion)Give(me, "CS2_189"); // Elven Archer: Battlecry: deal 1 damage
        archer.Poisonous = true;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.MinionTarget(me, archer, ogre)));
        Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, ogre.Zone.Type);
    }

    [Fact]
    public void Echo_cards_cost_at_least_1()
    {
        // Rules: an Echo card's cost can't go below 1; other cards can reach 0.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var echoCard = SabberStoneCore.Model.Cards.All.First(c => c.Collectible && c.Echo && c.Cost >= 2 && SabberStoneCore.Model.Cards.FromId(c.Id) == c);
        var echo = SabberStoneCore.Actions.Generic.DrawCard(me, echoCard);
        echo.Cost = 0;
        Assert.Equal(1, echo.Cost);
        var wolf = Give(me, "CS2_124"); // Wolf Rider (not Echo)
        wolf.Cost = 0;
        Assert.Equal(0, wolf.Cost);
    }

    [Fact]
    public void A_bounced_minion_loses_its_buffs()
    {
        // User rule: moving backward (board → hand) removes enchantments; ones applied after the move stay.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var yeti = (SabberStoneCore.Model.Entities.Minion)Give(me, "CS2_182"); // Chillwind Yeti 4/5
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, yeti)));
        var kings = Give(me, "CS2_092");   // Blessing of Kings: +4/+4
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, kings, yeti)));
        Assert.Equal(8, yeti.AttackDamage);

        me.UsedMana = 0;
        var shadowstep = Give(me, "EX1_144"); // Shadowstep: return a friendly minion to hand, it costs (2) less
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, shadowstep, yeti)));
        Assert.Equal(SabberStoneCore.Enums.Zone.HAND, yeti.Zone.Type);
        Assert.Equal(4, yeti.AttackDamage);
        Assert.Equal(5, yeti.Health);
        Assert.Equal(2, yeti.Cost);
        Assert.DoesNotContain(yeti.AppliedEnchantments ?? new(), e => e.Card.Id.StartsWith("CS2_092"));
    }

    [Fact]
    public void A_targeted_draw_fires_whenever_you_draw_triggers()
    {
        // Rules B12: "whenever you draw" triggers fire for a drawn card however it was picked.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "BRM_031")))); // Chromaggus
        var pick = me.DeckZone[me.DeckZone.Count / 2];
        int hand = me.HandZone.Count;
        SabberStoneCore.Actions.Generic.Draw(me, pick);
        Assert.Equal(SabberStoneCore.Enums.Zone.HAND, pick.Zone.Type);
        Assert.Equal(hand + 2, me.HandZone.Count); // the card + Chromaggus's copy
    }

    [Fact]
    public void Baron_rivendare_makes_deathrattles_trigger_twice()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "FP1_031")))); // Baron Rivendare
        var golem = (SabberStoneCore.Model.Entities.Minion)Give(me, "EX1_556"); // Harvest Golem: Deathrattle: summon a 2/1
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, golem)));
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_029"), golem))); // Fireball
        Assert.Equal(2, me.BoardZone.Count(m => m.Card.Id.EndsWith("skele21")));
    }

    [Fact]
    public void Fandral_turns_transform_choose_ones_into_their_combined_form()
    {
        // Engine rule (no card-id list): a Choose One card with its own task uses it as its "choose both" version.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "OG_044")))); // Fandral Staghelm
        var scythe = Give(me, "GIL_188"); // Druid of the Scythe
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, scythe)));
        Assert.Equal(2, me.BoardZone.Count);
        Assert.Equal("GIL_188t3", me.BoardZone[1].Card.Id);
    }

    [Fact]
    public void Wardruid_loti_options_come_from_the_client_related_cards()
    {
        // No card-id case in the engine: a Choose One card without a/b options uses the client's RelatedCards (Loti's 4 forms).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var loti = Give(me, "TRL_343");
        Assert.Equal(4, loti.ChooseOnePlayables.Length);
        Assert.All(loti.ChooseOnePlayables, p => Assert.StartsWith("TRL_343", p.Card.Id));
        Assert.Equal(new[] { "TRL_343at1", "TRL_343ct1", "TRL_343dt1", "TRL_343bt1" }, loti.ChooseOnePlayables.Select(p => p.Card.Id));
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, loti, chooseOne: 3)));
        Assert.Equal("TRL_343dt2", Assert.Single(me.BoardZone).Card.Id);

        // with Fandral: its own task, the combined form
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "OG_044"))));
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "TRL_343"))));
        Assert.Equal("TRL_343et1", me.BoardZone[2].Card.Id);
    }

    [Fact]
    public void Recruited_minions_land_on_the_far_right()
    {
        // Engine rule (corrected 2026-10-02, user + Advanced rulebook): a living minion's Recruit ("Force Play" from the
        // deck) lands right of the recruiter, like any summon by a minion on the board; only a Deathrattle's Force Play
        // goes far right (Meat Wagon, Giant Anaconda, Deathwing Dragonlord).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "CS2_231")))); // Wisp
        var recruiter = Give(me, "LOOT_375"); // Guild Recruiter: Battlecry: Recruit a minion that costs (4) or less
        Assert.Contains(me.DeckZone, p => p is SabberStoneCore.Model.Entities.Minion && p.Cost <= 4);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, recruiter, null, 0))); // left of the Wisp
        Assert.Equal(3, me.BoardZone.Count);
        Assert.Equal(recruiter, me.BoardZone[0]);
        Assert.Equal("CS2_231", me.BoardZone[2].Card.Id);                                // the recruit sits between
    }

    [Fact]
    public void Deathrattles_resolve_before_reborn()
    {
        // User rule / rules D2: Sylvanas dying together with an (older) Reborn minion can't steal the reborn copy.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var rebornCard = SabberStoneCore.Model.Cards.All.First(c => c.Type == SabberStoneCore.Enums.CardType.MINION && c.Collectible &&
            c[SabberStoneCore.Enums.GameTag.REBORN] == 1 && c.Power?.DeathrattleTask == null && c.Power?.Trigger == null &&
            c.Power?.Aura == null && SabberStoneCore.Model.Cards.FromId(c.Id) == c);
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, SabberStoneCore.Actions.Generic.DrawCard(me, rebornCard))));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me.Opponent, Give(me.Opponent, "EX1_016")))); // Sylvanas
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)));

        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "EX1_312")))); // Twisting Nether
        Assert.Single(me.BoardZone);
        Assert.Equal(rebornCard.Id, me.BoardZone[0].Card.Id);
        Assert.Empty(me.Opponent.BoardZone);
    }

    [Fact]
    public void Redemption_resolves_after_the_deathrattle()
    {
        // Rules A8: Redemption has Priority "last within a death": Harvest Golem's Deathrattle comes first.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        var them = me.Opponent;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(them, Give(them, "EX1_136")))); // Redemption
        var golem = (SabberStoneCore.Model.Entities.Minion)Give(them, "EX1_556"); // Harvest Golem
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(them, golem)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(them)));

        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_029"), golem))); // Fireball
        var damaged = them.BoardZone.Single(m => m.Card.Id.EndsWith("skele21"));
        var redeemed = them.BoardZone.Single(m => m.Card.Id == golem.Card.Id);
        Assert.True(damaged.Id < redeemed.Id); // the Deathrattle's summon was created first
        Assert.Equal(1, redeemed.Health);
    }

    [Fact]
    public void A_dead_minions_non_stat_aura_is_gone_before_its_death_phase()
    {
        // Rules A5 Rule 4 (rulebook example): Auchenai Soulpriest and Zombie Chow die together; the Chow's Deathrattle heals.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "EX1_591")))); // Auchenai Soulpriest
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "FP1_001")))); // Zombie Chow
        me.Opponent.Hero.Damage = 10;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "EX1_312")))); // Twisting Nether
        Assert.Empty(me.BoardZone);
        Assert.Equal(5, me.Opponent.Hero.Damage); // healed 5, not dealt 5
    }

    [Fact]
    public void Baron_rivendare_dying_with_the_board_doesnt_double()
    {
        // Rules A5: Rivendare's aura is an "Other" aura, gone before the Death Phase it dies in.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "FP1_031")))); // Baron Rivendare
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "EX1_556")))); // Harvest Golem
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "EX1_312")))); // Twisting Nether
        Assert.Single(me.BoardZone);
        Assert.EndsWith("skele21", me.BoardZone[0].Card.Id);
    }

    [Fact]
    public void A_mortally_wounded_minion_healed_in_the_same_phase_lives()
    {
        // Rules A6 example: Baron Geddon brings Healing Totem to 0 at end of turn; the Totem heals itself for 1 and lives.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "EX1_249")))); // Baron Geddon (older)
        var totem = (SabberStoneCore.Model.Entities.Minion)Give(me, "NEW1_009");  // Healing Totem 0/2
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, totem)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.Equal(SabberStoneCore.Enums.Zone.PLAY, totem.Zone.Type);
        Assert.Equal(1, totem.Health);

        // A destroy effect is final, whatever the Health.
        var geddon = (SabberStoneCore.Model.Entities.Minion)me.BoardZone.First(m => m.Card.Id.EndsWith("EX1_249"));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me.Opponent, Give(me.Opponent, "CS2_076"), geddon))); // Assassinate
        Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, geddon.Zone.Type);
    }

    [Fact]
    public void Area_damage_hits_every_target_before_triggers_resolve()
    {
        // Rules DH1 / A2 (audit example): Hellfire mortally wounds the enemy Harvest Golem before Imp Gang Boss's Imp is
        // summoned, so Knife Juggler's knife can only hit the enemy hero.
        for (int run = 0; run < 20; run++)
        {
            var game = TenManaGame();
            var me = game.CurrentPlayer;
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me.Opponent, Give(me.Opponent, "EX1_556")))); // Harvest Golem 2/3
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)));

            me.UsedMana = 0;
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "BRM_006")))); // Imp Gang Boss 2/4
            var juggler = (SabberStoneCore.Model.Entities.Minion)Give(me, "NEW1_019");
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, juggler)));
            me.UsedMana = 0;
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_092"), juggler))); // Kings: survives Hellfire
            me.UsedMana = 0;
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "CS2_062")))); // Hellfire: 3 to all
            Assert.Equal(4, me.Opponent.Hero.Damage); // 3 + the knife
        }
    }

    [Theory]
    [InlineData("BRM_008")] // Dark Iron Skulker: 2 to undamaged enemy minions
    [InlineData("GVG_008")] // Lightbomb: each minion takes damage equal to its Attack
    public void Dh3_area_effects_resolve_each_hit_before_the_next(string cardId)
    {
        // Rules DH3: unlike other area effects (DH1), these resolve each Damage Event at once: Acolyte of Pain (played
        // first) draws before the Yeti is hit.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        var acolyte = (SabberStoneCore.Model.Entities.Minion)Give(me.Opponent, "EX1_007");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me.Opponent, acolyte)));
        var yeti = (SabberStoneCore.Model.Entities.Minion)Give(me.Opponent, "CS2_182");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me.Opponent, yeti)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)));

        me.UsedMana = 0;
        var card = Give(me, cardId);
        Assert.True(game.Process(card is SabberStoneCore.Model.Entities.Minion
            ? SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, card)
            : SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, card)));
        var history = game.PowerHistory.Last;
        int draw = history.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart { BlockType: SabberStoneCore.Enums.BlockType.TRIGGER } b && b.Source == acolyte.Id);
        int yetiHit = history.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: GameTag.DAMAGE } t && t.EntityId == yeti.Id && t.Value > 0);
        Assert.True(draw >= 0 && yetiHit >= 0, $"draw {draw}, yeti hit {yetiHit}");
        Assert.True(draw < yetiHit, $"draw {draw}, yeti hit {yetiHit}");
    }

    [Fact]
    public void Freeze_thaws_this_turn_only_if_an_attack_was_still_possible()
    {
        // Rules (Freeze thaw timing): a Windfury minion frozen after one attack thaws at the end of this turn; a Rush
        // minion frozen with no enemy minion to hit couldn't attack, so it stays Frozen through its next turn.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var farseer = (SabberStoneCore.Model.Entities.Minion)Give(me, "EX1_021"); // Thrallmar Farseer, Windfury
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, farseer)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)));

        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(me, farseer, me.Opponent.Hero)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_031"), farseer))); // Ice Lance
        var spark = (SabberStoneCore.Model.Entities.Minion)Give(me, "BOT_102t"); // Spark 1/1 Rush
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, spark)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_031"), spark)));
        Assert.True(farseer.IsFrozen && spark.IsFrozen);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.False(farseer.IsFrozen); // had its second attack left
        Assert.True(spark.IsFrozen);    // no enemy minion: it could not attack
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.False(spark.IsFrozen);
    }

    [Fact]
    public void Twinspell_copy_takes_the_played_slot_before_cast_triggers()
    {
        // Rules (Twinspell): the copy is added at once, before "when you cast" triggers, into the slot the spell was
        // played from: Antonidas's Fireball comes after it, right-most.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "EX1_559")))); // Archmage Antonidas
        while (me.HandZone.Count > 2) me.HandZone.Remove(me.HandZone[0]);
        var rapidFire = Give(me, "DAL_373"); // Rapid Fire, Twinspell
        Give(me, "CS2_182");
        int slot = rapidFire.ZonePosition;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, rapidFire, me.Opponent.Hero)));
        Assert.Equal("DAL_373ts", me.HandZone[slot].Card.Id);
        Assert.Equal("CS2_029", me.HandZone[me.HandZone.Count - 1].Card.Id); // Fireball
    }

    [Fact]
    public void Silence_keeps_damage_an_auras_health_covers()
    {
        // Rules E5 (audit M8): silencing a damaged minion under Stormwind Champion doesn't remove the aura's Health, so it
        // stays damaged; a removed enchantment's Health only lowers current Health above the new maximum.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "CS2_222")))); // Stormwind Champion
        var raptor = (SabberStoneCore.Model.Entities.Minion)Give(me, "CS2_172"); // Bloodfen Raptor 3/2
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, raptor)));
        me.UsedMana = 0;
        var kings = (SabberStoneCore.Model.Entities.Minion)Give(me, "CS2_182"); // Yeti 4/5, gets Kings
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, kings)));
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_092"), kings))); // +4/+4
        raptor.Damage = 1;  // 4/3 -> 2 left
        kings.Damage = 7;   // 9/10 -> 3 left
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "EX1_332"), raptor))); // Silence
        Assert.Equal(3, raptor.BaseHealth);
        Assert.Equal(2, raptor.Health);
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "EX1_332"), kings)));
        Assert.Equal(6, kings.BaseHealth); // 5 + the aura
        Assert.Equal(3, kings.Health);
    }

    [Fact]
    public void Twinspell_sends_the_pink_replacement_like_live()
    {
        // Live (captures/live-20261001-twinspell, Rising Winds): the card in hand carries the tag list
        // CLIENT_LIST_REPLACEMENTS_WHEN_PLAYED = [YOD_001ts]; when played, YOD_001ts (a new card, no Twinspell) is created in
        // HAND at the played slot with CREATOR = the spell and CREATED_AS_ON_PLAY_REPLACEMENT = 1, before the spell's POWER
        // block. The client then shows it as the pink card (TWINSPELL_SUMMON_IN).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var winds = Give(me, "YOD_001"); // Rising Winds: Twinspell, Choose One
        int slot = winds.ZonePosition;
        int copyDbf = SabberStoneCore.Model.Cards.FromId("YOD_001ts").AssetId;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, winds, chooseOne: 1)));

        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        Assert.Contains(sent, d => (d.ShowEntity ?? d.FullEntity) is { } e && e.Entity == winds.Id && e.TagLists.Any(l =>
            l.Name == (int)GAME_TAG.CLIENT_LIST_REPLACEMENTS_WHEN_PLAYED && l.Values.SequenceEqual(new[] { copyDbf })));
        int copyAt = sent.FindIndex(d => d.FullEntity?.Name == "YOD_001ts");
        Assert.True(copyAt >= 0);
        var tags = sent[copyAt].FullEntity.Tags.ToDictionary(t => t.Name, t => t.Value);
        Assert.Equal((int)TAG_ZONE.HAND, tags[(int)GAME_TAG.ZONE]);
        Assert.Equal(slot + 1, tags[(int)GAME_TAG.ZONE_POSITION]);
        Assert.Equal(winds.Id, tags[(int)GAME_TAG.CREATOR]);
        Assert.Equal(1, tags[(int)GAME_TAG.CREATED_AS_ON_PLAY_REPLACEMENT]);
        int power = sent.FindIndex(d => d.PowerStart is { Type: HistoryBlock.Type.POWER } b && b.Source == winds.Id);
        var copy = Assert.Single(me.HandZone.GetAll(p => p.Card.Id == "YOD_001ts"));
        Assert.Equal(new[] { "YOD_001b", "YOD_001c" }, copy.ChooseOnePlayables.Select(p => p.Card.Id)); // live: its original's options
        Assert.True(power > copyAt, $"copy {copyAt}, power block {power}");
    }

    [Fact]
    public void A_forced_death_phase_leaves_the_deaths_it_causes_for_the_outer_phase()
    {
        // Rules A9 example: Poison Seeds on Haunted Creeper + Explosive Sheep. Its Forced Death Phase summons the Spiders and
        // the Sheep mortally wounds them; they wait, the Treants are summoned, and the Spiders die when the spell ends.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "FP1_002")))); // Haunted Creeper
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "GVG_076")))); // Explosive Sheep
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "FP1_019")))); // Poison Seeds
        var history = game.PowerHistory.Last;
        int treant = history.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryFullEntity f && game.IdEntityDic[f.Entity.Id].Card.Id == "FP1_019t");
        int spiderDies = history.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: GameTag.ZONE, Value: (int)SabberStoneCore.Enums.Zone.GRAVEYARD } t
                                               && game.IdEntityDic[t.EntityId].Card.Id == "FP1_002t");
        Assert.True(treant >= 0 && spiderDies >= 0, $"treant {treant}, spider dies {spiderDies}");
        Assert.True(treant < spiderDies, $"treant {treant}, spider dies {spiderDies}");
        Assert.All(me.BoardZone, m => Assert.Equal("FP1_019t", m.Card.Id));
        Assert.Equal(2, me.BoardZone.Count);
    }

    [Fact]
    public void Quest_contributor_marks_hand_cards_that_would_advance_the_quest()
    {
        // Live (heist-run-20260929, Open the Waygate): generated spells in hand get QUEST_CONTRIBUTOR 1; it goes back to 0 once
        // the card is played. Minions and spells that started in the deck don't count.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "UNG_028")))); // Open the Waygate
        var bolt = Give(me, "CS2_024");   // Frostbolt, created now: didn't start in the deck
        var yeti = Give(me, "CS2_182");
        // a spell that started in the deck (the filled deck may have none: then any deck card, which can't count either)
        var fromDeck = me.DeckZone.GetAll().FirstOrDefault(p => p is SabberStoneCore.Model.Entities.Spell) ?? me.DeckZone.GetAll().First();
        SabberStoneCore.Actions.Generic.Draw(me, fromDeck);
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, yeti)));

        int Last(List<PegasusGame.PowerHistoryData> sent, int id) =>
            sent.LastOrDefault(d => d.TagChange is { } t && t.Entity == id && t.Tag == (int)GAME_TAG.QUEST_CONTRIBUTOR)?.TagChange.Value ?? 0;
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        Assert.Equal(1, Last(sent, bolt.Id));
        Assert.Equal(0, Last(sent, yeti.Id));
        Assert.Equal(0, Last(sent, fromDeck.Id));

        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, bolt, me.Opponent.Hero)));
        sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        Assert.Equal(0, Last(sent, bolt.Id)); // cleared once played
        Assert.Equal(1, ((SabberStoneCore.Model.Entities.Spell)me.SecretZone.Quest).QuestProgress);
    }

    [Fact]
    public void Brukan_of_the_elements_casts_two_different_invocations_and_swaps_his_hero_power()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, Give(me, "AV_258"))));
        string[] powers = { "AV_258pt", "AV_258p2", "AV_258pt3", "AV_258pt4" };
        Assert.Contains(me.Hero.HeroPower.Card.Id, powers); // an Invocation from the moment he's played

        Assert.Equal(4, me.Choice.Choices.Count);
        int fire = me.Choice.Choices.First(id => game.IdEntityDic[id].Card.Id == "AV_258t3");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, fire)));
        Assert.Equal(6, me.Opponent.Hero.Damage);
        Assert.Equal(3, me.Choice.Choices.Count); // the second pick: the three left
        Assert.DoesNotContain(me.Choice.Choices, id => game.IdEntityDic[id].Card.Id == "AV_258t3");
        int earth = me.Choice.Choices.First(id => game.IdEntityDic[id].Card.Id == "AV_258t");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, earth)));
        Assert.Null(me.Choice);
        Assert.Equal(2, me.BoardZone.Count(m => m.Card.Id == "AV_258t6"));

        string before = me.Hero.HeroPower.Card.Id;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)));
        Assert.Contains(me.Hero.HeroPower.Card.Id, powers);
        Assert.NotEqual(before, me.Hero.HeroPower.Card.Id); // a different Element every turn
    }

    [Fact]
    public void Stormcaller_brukan_makes_your_spells_cast_twice()
    {
        // Live (captures/live-20261001-questline): SW_031t7e on the player; each spell's effect (Overload included) runs twice.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "SW_031t7"))));
        Assert.Equal(1, me[HsTag.SPELLS_CAST_TWICE]);
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_024"), me.Opponent.Hero))); // Frostbolt 3
        Assert.Equal(6, me.Opponent.Hero.Damage);
        me.UsedMana = 0;
        var storm = Give(me, "EX1_259"); // Lightning Storm (Overload from the card data)
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, storm)));
        Assert.Equal(2 * storm.Card.Overload, me.OverloadOwed); // Overload owed for both casts (live: 1 -> 2)
    }

    [Fact]
    public void Area_heal_heals_everything_before_triggers_resolve()
    {
        // Rules DH1 example: Circle of Healing heals everything before any Shadowboxer fires.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.UsedMana = 0;
        var boxer = (SabberStoneCore.Model.Entities.Minion)Give(me, "GVG_072"); // Shadowboxer: whenever a character is healed, 1 damage to a random enemy
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, boxer)));
        var yeti = (SabberStoneCore.Model.Entities.Minion)Give(me, "CS2_182");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, yeti)));
        boxer.Damage = 1;
        yeti.Damage = 2;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "EX1_621")))); // Circle of Healing
        var history = game.PowerHistory.Last;
        int lastHeal = history.FindLastIndex(e => e is SabberStoneCore.Kettle.PowerHistoryMetaData { Type: SabberStoneCore.Enums.MetaDataType.HEALING });
        int firstBox = history.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart { BlockType: SabberStoneCore.Enums.BlockType.TRIGGER } b && b.Source == boxer.Id);
        Assert.True(lastHeal >= 0 && firstBox >= 0);
        Assert.True(lastHeal < firstBox);
        Assert.Equal(2, me.Opponent.Hero.Damage); // two heals, two bolts (no enemy minions)
    }

    [Fact]
    public void After_summon_triggers_skip_a_played_minion_that_already_died()
    {
        // Rules A7 Rule 6 / B2: Snipe kills the Wisp in the After Play Phase, so Sword of Justice (After Summon) doesn't act.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me.Opponent, Give(me.Opponent, "EX1_609")))); // Snipe
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)));

        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, Give(me, "EX1_366")))); // Sword of Justice (5 Durability)
        var wisp = Give(me, "CS2_231");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, wisp)));
        Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, wisp.Zone.Type);
        Assert.Empty(me.Opponent.SecretZone);
        Assert.Equal(5, me.Hero.Weapon.Durability);
    }

    [Fact]
    public void Losing_divine_shield_resolves_its_triggers_at_once()
    {
        // Rules A2 Rule 1a: an Event raised mid-effect resolves its triggers first (depth-first): Bolvar's buff happens
        // inside the spell, not after it.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.UsedMana = 0;
        var bolvar = Give(me, "ICC_858"); // Bolvar, Fireblood: after a friendly minion loses Divine Shield, +2 Attack
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, bolvar)));
        var squire = (SabberStoneCore.Model.Entities.Minion)Give(me, "EX1_008"); // Argent Squire
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, squire)));
        var smite = Give(me, "CS1_130"); // Holy Smite: 2 damage... (Divine Shield takes it)
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, smite, squire)));
        Assert.False(squire.HasDivineShield);

        var history = game.PowerHistory.Last;
        int power = history.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart { BlockType: SabberStoneCore.Enums.BlockType.POWER } b && b.Source == smite.Id);
        int end = power, depth = 0;
        for (; end < history.Count; end++)
        {
            if (history[end] is SabberStoneCore.Kettle.PowerHistoryBlockStart) depth++;
            else if (history[end] is SabberStoneCore.Kettle.PowerHistoryBlockEnd && --depth == 0) break;
        }
        int bolvarTrigger = history.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart { BlockType: SabberStoneCore.Enums.BlockType.TRIGGER } b && b.Source == bolvar.Id);
        Assert.True(power >= 0 && bolvarTrigger > power && bolvarTrigger < end);
    }

    [Fact]
    public void Triggers_recheck_their_condition_when_they_resolve()
    {
        // Rules A11 Rule 10b: the first Hobgoblin's +2/+2 makes the Wisp a 3-Attack minion before the second resolves.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "GVG_104")))); // Hobgoblin
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "GVG_104"))));
        var wisp = (SabberStoneCore.Model.Entities.Minion)Give(me, "CS2_231");
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, wisp)));
        Assert.Equal(3, wisp.AttackDamage);
        Assert.Equal(3, wisp.Health);
    }

    [Fact]
    public void Attack_secrets_resolve_before_attack_event_triggers()
    {
        // Rules B8: the Proposed Attack Event (Explosive Trap) resolves before the Attack Event (Truesilver Champion),
        // even though Truesilver was equipped first.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, Give(me, "CS2_097")))); // Truesilver Champion
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me.Opponent, Give(me.Opponent, "EX1_610")))); // Explosive Trap
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)));

        Assert.Equal(0, me.Hero.Damage);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.HeroAttackTask.Any(me, me.Opponent.Hero)));
        Assert.Empty(me.Opponent.SecretZone);   // the Trap went off (2 to me)...
        Assert.Equal(0, me.Hero.Damage);         // ...then Truesilver healed it back
    }

    [Fact]
    public void Combat_damage_is_simultaneous()
    {
        // Rules B8.4: attack and counterattack both land before the damage triggers resolve.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var yeti = (SabberStoneCore.Model.Entities.Minion)Give(me, "CS2_182"); // Chillwind Yeti 4/5
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, yeti)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        var axe = (SabberStoneCore.Model.Entities.Minion)Give(me.Opponent, "BRM_016"); // Axe Flinger: whenever this takes damage, 2 to the enemy hero
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me.Opponent, axe)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)));

        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(me, yeti, axe)));
        Assert.Equal(2, me.Hero.Damage);
        var history = game.PowerHistory.Last;
        int counter = history.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: SabberStoneCore.Enums.GameTag.DAMAGE } t && t.EntityId == yeti.Id);
        int trigger = history.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart { BlockType: SabberStoneCore.Enums.BlockType.TRIGGER } b && b.Source == axe.Id);
        Assert.True(counter >= 0 && trigger >= 0);
        Assert.True(counter < trigger);
    }

    [Fact]
    public void Bouncing_into_a_full_hand_is_a_death()
    {
        // Rules C2 Z1: Sap on a Harvest Golem whose owner holds 10 cards kills it, and its Deathrattle still fires.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        var them = me.Opponent;
        var golem = (SabberStoneCore.Model.Entities.Minion)Give(them, "EX1_556"); // Harvest Golem
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(them, golem)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(them)));
        while (them.HandZone.Count < 10)
            Give(them, "CS2_231");

        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "EX1_581"), golem))); // Sap
        Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, golem.Zone.Type);
        Assert.Single(them.BoardZone);
        Assert.EndsWith("skele21", them.BoardZone[0].Card.Id); // Deathrattle: Damaged Golem
        Assert.Equal(10, them.HandZone.Count);
    }

    [Fact]
    public void An_auchenai_inverted_heal_gets_spell_damage()
    {
        // Rules DH2a / D4: the heal becomes damage first, then Spell Damage applies (Flash Heal 5 + Kobold 1 = 6).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "EX1_591")))); // Auchenai Soulpriest
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "CS2_142")))); // Kobold Geomancer
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "AT_055"), me.Opponent.Hero))); // Flash Heal
        Assert.Equal(6, me.Opponent.Hero.Damage);
    }

    [Fact]
    public void Cost_auras_and_cost_enchantments_apply_in_order_of_play()
    {
        // Rules M6: Naga Sea Witch (cards cost 5) then Thaurissan's -1 → 4; Thaurissan's -1 then Naga → 5.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var yeti = Give(me, "CS2_182");   // 4 mana
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "LOE_038")))); // Naga Sea Witch
        Assert.Equal(5, yeti.Cost);
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "BRM_028")))); // Emperor Thaurissan
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me))); // -1 to cards in hand
        Assert.Equal(4, yeti.Cost);

        var game2 = TenManaGame();
        var p = game2.CurrentPlayer;
        var yeti2 = SabberStoneCore.Actions.Generic.DrawCard(p, SabberStoneCore.Model.Cards.FromId("CS2_182"));
        p.UsedMana = 0;
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(p, SabberStoneCore.Actions.Generic.DrawCard(p, SabberStoneCore.Model.Cards.FromId("BRM_028")))));
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(p)));
        Assert.Equal(3, yeti2.Cost);
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(p.Opponent)));
        p.UsedMana = 0;
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(p, SabberStoneCore.Actions.Generic.DrawCard(p, SabberStoneCore.Model.Cards.FromId("LOE_038")))));
        Assert.Equal(5, yeti2.Cost);
    }

    [Fact]
    public void A_quest_reward_waits_for_room_in_hand()
    {
        // Rules D4: the reward comes at the Quest Reward Step; with a full hand it waits until there is room.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "UNG_920")))); // The Marsh Queen
        var moonfire = Give(me, "CS2_008");
        for (int i = 0; i < 6; i++)
        {
            me.UsedMana = 0;
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "EX1_029")))); // Leper Gnome (1)
        }
        while (me.HandZone.Count < 9)
            Give(me, "CS2_231");
        var firefly = Give(me, "UNG_809"); // Firefly (1): Battlecry: add a Flame Elemental → the hand is full again
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, firefly))); // 7th: completes
        Assert.Equal(10, me.HandZone.Count);
        Assert.Null(me.SecretZone.Quest);                                   // completed
        Assert.DoesNotContain(me.HandZone, p => p.Card.Id == "UNG_920t1");  // no room: the reward waits...
        Assert.DoesNotContain(me.GraveyardZone, p => p.Card.Id == "UNG_920t1"); // ...instead of burning

        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, moonfire, me.Opponent.Hero)));
        Assert.Contains(me.HandZone, p => p.Card.Id == "UNG_920t1");        // room now: Queen Carnassa arrives
    }

    [Fact]
    public void A_card_in_hand_glows_while_its_condition_would_hold_like_live()
    {
        // Live: Ravasaur Runt (If you control at least 2 other minions, Adapt) gets POWERED_UP=1 when the second minion
        // lands, 0 when it is played. The check is evaluated as if it were played: the Runt itself doesn't count.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var runt = Give(me, "UNG_009");
        int Glow()
        {
            game.PublishDerived();
            return game.PowerHistory.Reported.TryGetValue(runt.Id, out var tags) && tags.TryGetValue(SabberStoneCore.Enums.GameTag.POWERED_UP, out int v) ? v : 0;
        }
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "CS2_182"))));
        Assert.Equal(0, Glow()); // 1 other minion
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "CS2_182"))));
        Assert.Equal(1, Glow()); // 2 other minions: lit
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, runt)));
        Assert.Equal(0, Glow()); // played: cleared
    }

    [Fact]
    public void A_minion_type_target_battlecry_glows_while_such_a_target_is_there()
    {
        // Live: Skeletal Sidekick (Give a friendly Undead +2 Attack) carries POWERED_UP in hand (cleared at the play); the
        // user saw no glow with an Undead on board. Rule: an optional target of a minion type lights while one is there.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var sidekick = Give(me, "RLK_958");
        int Glow()
        {
            game.PublishDerived();
            return game.PowerHistory.Reported.TryGetValue(sidekick.Id, out var tags) && tags.TryGetValue(GameTag.POWERED_UP, out int v) ? v : 0;
        }
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "CS2_182"))));
        Assert.Equal(0, Glow()); // a Beast-less, Undead-less board
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "RLK_958")))); // an Undead
        Assert.Equal(1, Glow());
    }

    [Fact]
    public void A_conditional_target_glows_while_the_condition_is_met_and_a_plain_one_never_does()
    {
        // User: The Black Knight (destroy an enemy Taunt) and other tech cards glow on live when a target is there; live
        // POWERED_UP on Skeletal Sidekick / Amalgam of the Deep; a plain optional target never glows (live: Darkshire
        // Alchemist, Stormpike Commando). One rule (Playable.HasConditionalTarget): target filters and "if" conditions.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var knight = Give(me, "CORE_EX1_002");   // Taunt filter
        var hunter = Give(me, "EX1_005");        // Attack 7+ filter
        var corruptor = Give(me, "BRM_034");     // "If you're holding a Dragon"
        var alchemist = Give(me, "OG_234");      // plain optional target
        int Glow(SabberStoneCore.Model.Entities.IPlayable card)
        {
            game.PublishDerived();
            return game.PowerHistory.Reported.TryGetValue(card.Id, out var tags) && tags.TryGetValue(GameTag.POWERED_UP, out int v) ? v : 0;
        }
        // (the dealt hand may already hold a Dragon: Blackwing Corruptor's glow follows that)
        bool Dragon() => me.HandZone.Any(x => x.Id != corruptor.Id && x.Card.IsRace(SabberStoneCore.Enums.Race.DRAGON));
        Assert.Equal(0, Glow(knight));
        Assert.Equal(0, Glow(hunter));
        Assert.Equal(Dragon() ? 1 : 0, Glow(corruptor));
        // the opponent puts down a Taunt (Goldshire Footman) and a 7-Attack minion (Core Hound 9/5)
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        var op = game.CurrentPlayer;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(op, Give(op, "CS1_042"))));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(op, Give(op, "CS2_201"))));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(op)));
        Assert.Equal(1, Glow(knight));
        Assert.Equal(1, Glow(hunter));
        Assert.Equal(Dragon() ? 1 : 0, Glow(corruptor));
        Give(me, "EX1_563"); // a Dragon in hand (Malygos)
        Assert.Equal(1, Glow(corruptor));
        Assert.Equal(0, Glow(alchemist));
    }

    [Fact]
    public void Corpse_cards_glow_with_enough_corpses_and_play_their_corpse_effect_like_live()
    {
        // Live (live-20261003-dk-classxp): Grave Strength carries POWERED_UP while 5+ Corpses are available (Rite of Atrocity
        // at 2); its POWER block holds SUB_SPELL ConsumeCorpse (from the hero, at the hero) around CORPSES_SPENT_THIS_GAME,
        // then SUB_SPELL BuffImpact (from the hero) with the buffs. User: no glow, no animation.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "CS2_182"))));
        var strength = Give(me, "RLK_707");
        int Glow()
        {
            game.PublishDerived();
            return game.PowerHistory.Reported.TryGetValue(strength.Id, out var tags) && tags.TryGetValue(GameTag.POWERED_UP, out int v) ? v : 0;
        }
        me.GainCorpses(4);
        Assert.Equal(0, Glow());
        me.GainCorpses(1);
        Assert.Equal(1, Glow());
        int from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, strength)));
        Assert.Equal(0, me.Corpses);
        var h = game.PowerHistory.Full.Skip(from).ToList();
        int consume = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistorySubSpellStart sub && sub.Prefab.StartsWith("ReuseFX_Necrotic_DeathKnight_AE_ConsumeCorpse_Super:"));
        int buff = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistorySubSpellStart sub && sub.Prefab.StartsWith("ReuseFX_Necrotic_DeathKnight_BuffImpact_Small_Super:"));
        Assert.True(consume >= 0 && buff > consume, "ConsumeCorpse, then BuffImpact");
        var c = (SabberStoneCore.Kettle.PowerHistorySubSpellStart)h[consume];
        Assert.Equal(me.Hero.Id, c.Source);
        Assert.Equal(new[] { me.Hero.Id }, c.Targets.ToArray());
        int spent = h.FindIndex(consume, e => e is SabberStoneCore.Kettle.PowerHistoryTagChange t && t.Tag == SabberStoneCore.Enums.HsTag.CORPSES_SPENT_THIS_GAME);
        int end = h.FindIndex(consume, e => e is SabberStoneCore.Kettle.PowerHistorySubSpellEnd);
        Assert.True(spent > consume && spent < end, "the spend inside ConsumeCorpse");
        Assert.Equal(me.Hero.Id, ((SabberStoneCore.Kettle.PowerHistorySubSpellStart)h[buff]).Source);
        Assert.Contains(h.Skip(buff), e => e is SabberStoneCore.Kettle.PowerHistoryFullEntity fe && game.IdEntityDic.TryGetValue(fe.Entity.Id, out var en)
                                                && en.Card.Id == "RLK_707e2"); // the +3 buff, inside BuffImpact
    }

    [Fact]
    public void A_repeated_round_of_an_effect_plays_its_animation_again_like_live()
    {
        // Audit (RESEARCH/vfx/audit_subspells.md): live plays Blood Tap's hand buff effect twice in one block (+1/+1, then
        // +1/+1 more for 2 Corpses), Relic of Extinction's missile per hit. Ours played one for both rounds.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        Give(me, "CS2_182"); Give(me, "CS2_182");
        var tap = Give(me, "CORE_RLK_712");
        me.GainCorpses(2);
        int from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, tap)));
        var subs = game.PowerHistory.Full.Skip(from).OfType<SabberStoneCore.Kettle.PowerHistorySubSpellStart>()
            .Where(x => x.Prefab.StartsWith("ReuseFX_Blood_DeathKnight_HandBuff_ScaleUp_OpponentAE_Super:")).ToList();
        Assert.Equal(2, subs.Count);
        var h = game.PowerHistory.Full.Skip(from).ToList();
        Assert.Equal(h.OfType<SabberStoneCore.Kettle.PowerHistorySubSpellStart>().Count(), h.OfType<SabberStoneCore.Kettle.PowerHistorySubSpellEnd>().Count());
    }

    [Fact]
    public void Effects_follow_the_order_of_the_text_around_a_corpse_spend()
    {
        // engine-rules.md "Effects happen in the order of the text" (user 2026-10-04). Live Blood Tap ("+1/+1. Spend 2 Corpses
        // to give them +1/+1 more"): HandBuff, ConsumeCorpse, HandBuff; live Grave Strength ("…+3 instead"): ConsumeCorpse
        // first (live-20261004-corpses, live-20261003-dk-classxp). Ours spent first on every Corpse card.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        List<string> Fx(int from) => game.PowerHistory.Full.Skip(from).OfType<SabberStoneCore.Kettle.PowerHistorySubSpellStart>()
            .Select(x => x.Prefab.Split(':')[0]).ToList();
        int SpentAt(int from) => game.PowerHistory.Full.FindIndex(from, e => e is SabberStoneCore.Kettle.PowerHistoryTagChange t &&
                                                                               t.Tag == SabberStoneCore.Enums.HsTag.CORPSES_SPENT_THIS_GAME);
        const string handBuff = "ReuseFX_Blood_DeathKnight_HandBuff_ScaleUp_OpponentAE_Super";
        const string corpse = "ReuseFX_Necrotic_DeathKnight_AE_ConsumeCorpse_Super";

        // Blood Tap: the first +1/+1 in its own effect, the spend, then +1/+1 more
        Give(me, "CS2_182"); Give(me, "CS2_182");
        me.GainCorpses(2);
        int from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "CORE_RLK_712"))));
        Assert.Equal(new[] { handBuff, corpse, handBuff }, Fx(from));
        var h = game.PowerHistory.Full;
        int spent = SpentAt(from);
        bool TapBuff(SabberStoneCore.Kettle.IPowerHistoryEntry e) => e is SabberStoneCore.Kettle.PowerHistoryShowEntity s && s.Entity.Name == "RLK_712e";
        int minions = me.HandZone.Count(p => p is SabberStoneCore.Model.Entities.Minion);
        Assert.Equal(minions, h.Skip(from).Take(spent - from).Count(TapBuff));
        Assert.Equal(minions, h.Skip(spent).Count(TapBuff));
        // without the Corpses: the one buff only
        from = h.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "CORE_RLK_712"))));
        Assert.Equal(new[] { handBuff }, Fx(from));

        // Grave Strength ("instead"): the spend first, no empty buff effect before it
        me.UsedMana = 0;
        me.GainCorpses(5);
        from = h.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "RLK_707"))));
        Assert.Equal(corpse, Fx(from)[0]);

        // Defrost: draw, the spend, draw another
        me.UsedMana = 0;
        me.GainCorpses(2);
        int hand = me.HandZone.Count;
        from = h.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "RLK_101"))));
        Assert.Equal(hand + 2, me.HandZone.Count);
        spent = SpentAt(from);
        bool Drew(SabberStoneCore.Kettle.IPowerHistoryEntry e) => e is SabberStoneCore.Kettle.PowerHistoryTagChange t && t.Tag == GameTag.ZONE &&
                                                                  t.Value == (int)Zone.HAND && game.IdEntityDic[t.EntityId].Card.Id != "RLK_101";
        Assert.Equal(1, h.Skip(from).Take(spent - from).Count(Drew));
        Assert.Equal(1, h.Skip(spent).Count(Drew));

        // Tomb Guardians: summon, the spend, then Reborn
        me.UsedMana = 0;
        me.GainCorpses(4);
        from = h.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "CORE_RLK_118"))));
        spent = SpentAt(from);
        Assert.Equal(2, h.Skip(from).Take(spent - from).Count(e => e is SabberStoneCore.Kettle.PowerHistoryFullEntity fe && fe.Entity.Name == "RLK_118t3"));
        Assert.Equal(2, h.Skip(spent).Count(e => e is SabberStoneCore.Kettle.PowerHistoryTagChange t && t.Tag == GameTag.REBORN && t.Value == 1));
        Assert.Equal(2, me.BoardZone.Count(m => m.Card.Id == "RLK_118t3" && m[GameTag.REBORN] == 1));

        // Vampiric Blood: +5, the spend, 5 more and a draw (two enchantments)
        me.UsedMana = 0;
        me.GainCorpses(3);
        int max = me.Hero.BaseHealth;
        hand = me.HandZone.Count;
        from = h.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "CORE_RLK_051"))));
        Assert.Equal(max + 10, me.Hero.BaseHealth);
        Assert.Equal(hand + 1, me.HandZone.Count);
        spent = SpentAt(from);
        Assert.Equal(1, h.Skip(from).Take(spent - from).Count(e => e is SabberStoneCore.Kettle.PowerHistoryShowEntity s && s.Entity.Name == "RLK_051e"));
        Assert.Equal(1, h.Skip(spent).Count(e => e is SabberStoneCore.Kettle.PowerHistoryShowEntity s && s.Entity.Name == "RLK_051e"));
        Assert.Equal(h.Skip(from).OfType<SabberStoneCore.Kettle.PowerHistorySubSpellStart>().Count(),
                     h.Skip(from).OfType<SabberStoneCore.Kettle.PowerHistorySubSpellEnd>().Count());
    }

    private static SabberStoneCore.Model.Entities.Minion OnBoard(SabberStoneCore.Model.Game game, SabberStoneCore.Model.Entities.Controller c, string id)
    {
        var m = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(c, SabberStoneCore.Model.Cards.FromId(id));
        SabberStoneCore.Actions.Generic.SummonBlock.Invoke(game, m, -1, null);
        m[GameTag.NUM_TURNS_IN_PLAY] = 1;
        m.IsExhausted = false;
        return m;
    }

    private static void Kill(SabberStoneCore.Model.Game game, SabberStoneCore.Model.Entities.IPlayable p)
    {
        SabberStoneCore.Actions.Generic.Destroy(p, null);
        game.DeathProcessingAndAuraUpdate();
    }

    [Fact]
    public void Legacy_death_knight_cards_follow_their_text()
    {
        // Legacy part 2 (2026-10-04): the Legacy set is set 1635 + Expert1 (475); its Death Knight cards (LEG_RLK_) were
        // placeholders. Each plays its LEG_ id: the reprint shares the original's script (Loader/CardDefs.cs, same text).
        var game = DeathKnightGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;

        // Heart Strike: 3 to a minion; it dies → a Corpse
        int corpses = me.Corpses;
        var croc = OnBoard(game, op, "CS2_120");
        Play(game, me, "LEG_RLK_034", croc);
        Assert.True(croc.IsDead || croc.Zone?.Type == Zone.GRAVEYARD);
        Assert.Equal(corpses + 1, me.Corpses);

        // Plagued Grain: 4 Corpses, four Crates; a drawn Crate summons a 2/2 Undead Peasant
        corpses = me.Corpses;
        Play(game, me, "LEG_RLK_039");
        Assert.Equal(corpses + 4, me.Corpses);
        var crates = me.DeckZone.Where(p => p.Card.Id == "RLK_039t").ToList();
        Assert.Equal(4, crates.Count);
        SabberStoneCore.Actions.Generic.Draw(me, crates[0]);
        game.DeathProcessingAndAuraUpdate();
        Assert.Contains(me.BoardZone, m => m.Card.Id == "RLK_070t");

        // Possessifier: two 1/1 Fighters; Graveyard Shift: two Zombies
        Play(game, me, "LEG_RLK_077");
        Assert.Equal(2, me.BoardZone.Count(m => m.Card.Id == "RLK_077t"));
        Play(game, me, "LEG_RLK_705");
        Assert.Equal(2, me.BoardZone.Count(m => m.Card.Id == "RLK_705t"));
        foreach (var m in me.BoardZone.ToList()) Kill(game, m);

        // Noxious Cadaver: 2 to an enemy and 2 to your hero
        int mine = me.Hero.Health, theirs = op.Hero.Health;
        Play(game, me, "LEG_RLK_079", op.Hero);
        Assert.Equal(mine - 2, me.Hero.Health);
        Assert.Equal(theirs - 2, op.Hero.Health);

        // Obliterate: destroy a minion, 3 to your hero
        var yeti = OnBoard(game, op, "CS2_182");
        mine = me.Hero.Health;
        Play(game, me, "LEG_RLK_125", yeti);
        Assert.NotEqual(Zone.PLAY, yeti.Zone?.Type);
        Assert.Equal(mine - 3, me.Hero.Health);
        foreach (var m in me.BoardZone.ToList()) Kill(game, m);

        // Patchwerk: a random minion in the opponent's hand, deck and battlefield
        var target = OnBoard(game, op, "CS2_182");
        SabberStoneCore.Actions.Generic.DrawCard(op, SabberStoneCore.Model.Cards.FromId("CS2_182"));
        int hand = op.HandZone.Count(p => p is SabberStoneCore.Model.Entities.Minion), deck = op.DeckZone.Count(p => p is SabberStoneCore.Model.Entities.Minion);
        Play(game, me, "LEG_RLK_071");
        game.DeathProcessingAndAuraUpdate();
        Assert.NotEqual(Zone.PLAY, target.Zone?.Type);
        Assert.Equal(hand - 1, op.HandZone.Count(p => p is SabberStoneCore.Model.Entities.Minion));
        Assert.Equal(deck - 1, op.DeckZone.Count(p => p is SabberStoneCore.Model.Entities.Minion));
        foreach (var m in me.BoardZone.ToList()) Kill(game, m);

        // Deathbringer Saurfang: back to hand as a fresh card that costs Health
        Play(game, me, "LEG_RLK_082");
        var saurfang = me.BoardZone.Single(m => m.Card.Id == "LEG_RLK_082");
        Kill(game, saurfang);
        var back = me.HandZone.Last();
        Assert.Equal("LEG_RLK_082", back.Card.Id);
        Assert.NotEqual(saurfang.Id, back.Id);
        me.UsedMana = me.BaseMana; // no Mana left: it costs Health
        mine = me.Hero.Health;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, back)));
        Assert.Equal(mine - back.Card.Cost, me.Hero.Health);
        foreach (var m in me.BoardZone.ToList()) Kill(game, m);

        // Repulsive Gargantuan: the opponent's characters can't be healed; ours can
        Play(game, me, "LEG_RLK_115");
        op.Hero.Damage = 5;
        me.Hero.Damage = 5;
        op.Hero.TakeHeal(op.Hero, 3);
        me.Hero.TakeHeal(me.Hero, 3);
        Assert.Equal(5, op.Hero.Damage);
        Assert.Equal(2, me.Hero.Damage);
        foreach (var m in me.BoardZone.ToList()) Kill(game, m);
        op.Hero.TakeHeal(op.Hero, 3);
        Assert.Equal(2, op.Hero.Damage); // the rule leaves with it

        // Stitched Giant: (1) less per Corpse spent this game
        me.GainCorpses(5);
        int spent = me[SabberStoneCore.Enums.HsTag.CORPSES_SPENT_THIS_GAME];
        SabberStoneCore.Actions.Generic.SpendCorpses(me, me.Hero, 5);
        var giant = Give(me, "LEG_RLK_744");
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(Math.Max(0, giant.Card.Cost - (spent + 5)), giant.Cost);

        // Rime Sculptor: two 2/1 Rime Elementals; each deals 2 to a random enemy when it dies
        Play(game, me, "LEG_RLK_752");
        var elementals = me.BoardZone.Where(m => m.Card.Id == "RLK_907t").ToList();
        Assert.Equal(2, elementals.Count);
        theirs = op.Hero.Health;
        Kill(game, elementals[0]);
        Assert.Equal(theirs - 2, op.Hero.Health);
        foreach (var m in me.BoardZone.ToList()) Kill(game, m);

        // Rimefang Sword: after your hero attacks, a spell in hand costs (1) less
        Play(game, me, "LEG_RLK_710");
        var spell = Give(me, "CS2_024"); // Frostbolt
        int cost = spell.Cost;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.HeroAttackTask.Any(me, op.Hero)));
        Assert.Equal(cost - 1, spell.Cost);

        // Overseer Frigidara: draw 2 spells; both Frost → 2 to all enemies
        while (me.DeckZone.Count > 0) me.DeckZone.Remove(me.DeckZone[0]);
        foreach (string frost in new[] { "CS2_024", "CS2_026" }) // Frostbolt, Frost Nova
            me.DeckZone.Add(SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId(frost)));
        theirs = op.Hero.Health;
        Play(game, me, "LEG_RLK_224");
        Assert.Equal(theirs - 2, op.Hero.Health);
    }

    [Fact]
    public void Legacy_rotated_core_and_event_cards_follow_their_text()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;

        // Shadowed Spirit: 3 to the enemy hero when it dies
        Play(game, me, "LEG_CS3_013");
        int theirs = op.Hero.Health;
        Kill(game, me.BoardZone.Single(m => m.Card.Id == "LEG_CS3_013"));
        Assert.Equal(theirs - 3, op.Hero.Health);

        // Alexstrasza the Life-Binder: 8 to an enemy, or 8 Health to a friendly character
        theirs = op.Hero.Health;
        Play(game, me, "LEG_CS3_031", op.Hero);
        Assert.Equal(theirs - 8, op.Hero.Health);
        me.Hero.Damage = 10;
        Play(game, me, "LEG_CS3_031", me.Hero);
        Assert.Equal(2, me.Hero.Damage);
        foreach (var m in me.BoardZone.ToList()) Kill(game, m);

        // Ysera the Dreamer: one of each Dream card
        while (me.HandZone.Count > 0) me.HandZone.Remove(me.HandZone[0]);
        Play(game, me, "LEG_CS3_033");
        Assert.Equal(new[] { "DREAM_01", "DREAM_02", "DREAM_03", "DREAM_04", "DREAM_05" }, me.HandZone.Select(p => p.Card.Id).OrderBy(x => x));
        foreach (var m in me.BoardZone.ToList()) Kill(game, m);

        // Malygos the Spellweaver: spells until the hand is full (or none are left)
        while (me.HandZone.Count > 0) me.HandZone.Remove(me.HandZone[0]);
        for (int i = 0; i < 12; i++) me.DeckZone.Add(SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("CS2_024")));
        Play(game, me, "LEG_CS3_034");
        Assert.True(me.HandZone.IsFull);
        Assert.All(me.HandZone, p => Assert.IsType<SabberStoneCore.Model.Entities.Spell>(p));
        foreach (var m in me.BoardZone.ToList()) Kill(game, m);

        // Deathwing the Destroyer: all other minions die, a discard for each
        while (me.HandZone.Count > 0) me.HandZone.Remove(me.HandZone[0]);
        OnBoard(game, op, "CS2_182"); OnBoard(game, op, "CS2_120"); OnBoard(game, me, "CS2_182");
        for (int i = 0; i < 5; i++) Give(me, "CS2_024");
        Play(game, me, "LEG_CS3_036");
        game.DeathProcessingAndAuraUpdate();
        Assert.Empty(op.BoardZone);
        Assert.Single(me.BoardZone);
        Assert.Equal(2, me.HandZone.Count); // 5 - 3 destroyed
        foreach (var m in me.BoardZone.ToList()) Kill(game, m);

        // Old Murk-Eye: +1 Attack for each OTHER Murloc on the battlefield (both sides)
        OnBoard(game, op, "CS2_168"); // Murloc Raider
        OnBoard(game, me, "CS2_168");
        Play(game, me, "EX1_062");
        var murkEye = me.BoardZone.Single(m => m.Card.Id == "EX1_062");
        Assert.Equal(murkEye.Card.ATK + 2, murkEye.AttackDamage);
        foreach (var m in me.BoardZone.Concat(op.BoardZone).ToList()) Kill(game, m);

        // Harth Stonebrew: the hand becomes one of the 13 iconic hands, always 8 cards; once per game
        while (me.HandZone.Count > 0) me.HandZone.Remove(me.HandZone[0]);
        var oldHand = new[] { Give(me, "CS2_024"), Give(me, "CS2_024") };
        oldHand[0][GameTag.PREMIUM] = 1; // a golden card in hand must not make the new cards golden
        int harthFrom = game.PowerHistory.Full.Count;
        Play(game, me, "GIFT_01");
        Assert.Equal(8, me.HandZone.Count);
        // the old cards leave inside the server-named RemoveHand effect; the new hand is NEW cards (no transform)
        var removeHand = Assert.Single(game.PowerHistory.Full.Skip(harthFrom).OfType<SabberStoneCore.Kettle.PowerHistorySubSpellStart>(),
            x => x.Prefab.Contains("RemoveHand"));
        Assert.Equal(oldHand.Select(p => p.Id).OrderBy(x => x), removeHand.Targets.OrderBy(x => x));
        Assert.All(me.HandZone, p => Assert.DoesNotContain(p.Id, oldHand.Select(o => o.Id)));
        Assert.All(me.HandZone, p => Assert.Equal(0, p[GameTag.PREMIUM]));
        Assert.DoesNotContain(game.PowerHistory.Full.Skip(harthFrom), e => e is SabberStoneCore.Kettle.PowerHistoryChangeEntity);
        var iconic = me.HandZone.Select(p => p.Card.Id).ToList();
        foreach (var m in me.BoardZone.ToList()) Kill(game, m);
        Play(game, me, "GIFT_01");
        Assert.Equal(iconic, me.HandZone.Select(p => p.Card.Id).ToList());
        foreach (var m in me.BoardZone.ToList()) Kill(game, m);
        while (me.HandZone.Count > 0) me.HandZone.Remove(me.HandZone[0]);

        // Onyxia the Broodmother: at the end of each turn the board fills with Whelps
        Play(game, me, "LEG_CS3_032");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.Equal(7, me.BoardZone.Count);
        Assert.Equal(6, me.BoardZone.Count(m => m.Card.Id == "ds1_whelptoken"));
    }

    [Fact]
    public void Legacy_aegwynn_outcast_and_deck_discovers_follow_their_text()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;

        // Aegwynn: Spell Damage +2; when she dies the next minion you draw inherits it (and passes it on)
        Play(game, me, "LEG_CS3_001");
        Assert.Equal(2, me.CurrentSpellPower);
        Kill(game, me.BoardZone.Single(m => m.Card.Id == "LEG_CS3_001"));
        Assert.Equal(0, me.CurrentSpellPower);
        var heir = SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("CS2_182")); // a known minion
        me.DeckZone.Add(heir);
        SabberStoneCore.Actions.Generic.Draw(me, heir);
        Assert.Contains(heir.AppliedEnchantments, e => e.Card.Id == "CS3_001e");
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, heir)));
        Assert.Equal(2, me.CurrentSpellPower);
        foreach (var m in me.BoardZone.ToList()) Kill(game, m);

        // Spell Damage +N comes from the text (the client's SPELLPOWER tag is only the keyword, 1): Malygos is +5
        Play(game, me, "EX1_563");
        Assert.Equal(5, me.CurrentSpellPower);
        foreach (var m in me.BoardZone.ToList()) Kill(game, m);

        // Gan'arg Glaivesmith: Outcast (the right-most card) gives the hero +3 Attack this turn
        Play(game, me, "LEG_CS3_017");
        Assert.Equal(3, me.Hero.AttackDamage);
        foreach (var m in me.BoardZone.ToList()) Kill(game, m);

        // Selective Breeder: Discover a copy of a Beast in your deck (the deck keeps it)
        me.DeckZone.Add(SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("CS2_172")));
        int deck = me.DeckZone.Count, hand = me.HandZone.Count;
        Play(game, me, "LEG_CS3_015");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, me.Choice.Choices[0])));
        Assert.Equal(deck, me.DeckZone.Count);
        Assert.True(me.HandZone.Last().Card.IsRace(Race.BEAST));
        foreach (var m in me.BoardZone.ToList()) Kill(game, m);

        // Thrive in the Shadows: Discover a spell from your deck (it is drawn)
        me.DeckZone.Add(SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("CS2_024")));
        deck = me.DeckZone.Count;
        Play(game, me, "LEG_CS3_028");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, me.Choice.Choices[0])));
        Assert.Equal(deck - 1, me.DeckZone.Count);
        Assert.IsType<SabberStoneCore.Model.Entities.Spell>(me.HandZone.Last());
    }

    [Fact]
    public void Legacy_cards_play_their_own_effects()
    {
        // The Legacy set's server-named effects (client CardDef m_SubSpellEffectDefs + recordings): where each plays.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        List<SabberStoneCore.Kettle.PowerHistorySubSpellStart> Fx(int from, string part) => game.PowerHistory.Full.Skip(from)
            .OfType<SabberStoneCore.Kettle.PowerHistorySubSpellStart>().Where(x => x.Prefab.Contains(part)).ToList();

        // Shadowed Spirit (live session-20260930-125547): its Deathrattle plays its missile, no source, at the enemy hero
        Play(game, me, "LEG_CS3_013");
        int from = game.PowerHistory.Full.Count;
        Kill(game, me.BoardZone.Single(m => m.Card.Id == "LEG_CS3_013"));
        var bolt = Assert.Single(Fx(from, "Missile_Shadowbolt"));
        Assert.Equal(0, bolt.Source);
        Assert.Equal(new[] { op.Hero.Id }, bolt.Targets);

        // Rime Elemental (Rime Sculptor's token; live-20261004-night 215955): its Deathrattle's frost lance from itself at the
        // random enemy it hits (a token's own server-picked effect, recorded)
        var rime = OnBoard(game, me, "RLK_907t");
        from = game.PowerHistory.Full.Count;
        Kill(game, rime);
        var lance = Assert.Single(Fx(from, "Frost_Missile_Lance"));
        Assert.Equal(rime.Id, lance.Source);
        Assert.Single(lance.Targets);

        // Patchwerk: its cleaver at each card it destroys (hand, deck, board)
        OnBoard(game, op, "CS2_182");
        SabberStoneCore.Actions.Generic.DrawCard(op, SabberStoneCore.Model.Cards.FromId("CS2_182"));
        from = game.PowerHistory.Full.Count;
        Play(game, me, "LEG_RLK_071");
        Assert.Equal(3, Fx(from, "Patchwerk_Missile_Cleaver").Count);
        foreach (var m in me.BoardZone.Concat(op.BoardZone).ToList()) Kill(game, m);

        // Overseer Frigidara: its frost blast only when both spells are Frost (and it deals the damage)
        while (me.DeckZone.Count > 0) me.DeckZone.Remove(me.DeckZone[0]);
        foreach (string id in new[] { "CS2_029", "CS2_024" }) // Fireball, Frostbolt
            me.DeckZone.Add(SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId(id)));
        from = game.PowerHistory.Full.Count;
        Play(game, me, "LEG_RLK_224");
        Assert.Empty(Fx(from, "Xyrella_AE"));
        foreach (string id in new[] { "CS2_026", "CS2_024" }) // Frost Nova, Frostbolt
            me.DeckZone.Add(SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId(id)));
        from = game.PowerHistory.Full.Count;
        Play(game, me, "LEG_RLK_224");
        Assert.Contains(op.Hero.Id, Assert.Single(Fx(from, "Xyrella_AE")).Targets);
        foreach (var m in me.BoardZone.ToList()) Kill(game, m);

        // Gnomelia: its burst at the minions next to whomever it attacks
        var left = OnBoard(game, op, "CS2_182"); var mid = OnBoard(game, op, "CS2_182"); var right = OnBoard(game, op, "CS2_182");
        Play(game, me, "TOY_100");
        var gnomelia = me.BoardZone.Single(m => m.Card.Id == "TOY_100");
        from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(me, gnomelia, mid)));
        var burst = Assert.Single(Fx(from, "AdjacentBurst"));
        Assert.Equal(new[] { left.Id, right.Id }.OrderBy(x => x), burst.Targets.OrderBy(x => x));
    }

    [Fact]
    public void Naxxramas_cards_follow_their_text_and_the_rule_book()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        // (repeated: a random deck minion's Deathrattle may summon a replacement — Deathlord pulls a random one)
        void Clear() { for (int r = 0; r < 6 && me.BoardZone.Count + op.BoardZone.Count > 0; r++) foreach (var m in me.BoardZone.Concat(op.BoardZone).ToList()) Kill(game, m); }

        // Loatheb: enemy spells cost (5) more on their next turn (shown on each spell: FP1_030e2), not ours
        var theirBolt = Give(op, "CS2_024");
        var ourBolt = Give(me, "CS2_024");
        Play(game, me, "FP1_030");
        Assert.Equal(theirBolt.Card.Cost + 5, theirBolt.Cost);
        Assert.Equal(ourBolt.Card.Cost, ourBolt.Cost);
        Assert.Contains(theirBolt.AppliedEnchantments, e => e.Card.Id == "FP1_030e2");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.Equal(theirBolt.Card.Cost + 5, theirBolt.Cost); // their turn
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(op)));
        Assert.Equal(theirBolt.Card.Cost, theirBolt.Cost);
        Clear();

        // Baron Rivendare: Deathrattles twice, shown on the player (FP1_031e)
        Play(game, me, "FP1_031");
        Assert.Contains(me.AppliedEnchantments, e => e.Card.Id == "FP1_031e");
        Play(game, me, "FP1_002"); // Haunted Creeper
        Kill(game, me.BoardZone.Single(m => m.Card.Id == "FP1_002"));
        Assert.Equal(4, me.BoardZone.Count(m => m.Card.Id == "FP1_002t"));
        Clear();

        // Avenge and Duplicate (Secrets: they trigger on the opponent's turn). Avenge buffs another minion, not the dead one;
        // Duplicate gives 2 fresh copies (no buffs) of the friendly minion that died.
        Play(game, me, "FP1_020");
        Play(game, me, "FP1_018");
        var raptor = OnBoard(game, me, "CS2_172");
        var croc = OnBoard(game, me, "CS2_120");
        SabberStoneCore.Actions.Generic.AddEnchantmentBlock(game, SabberStoneCore.Model.Cards.FromId("FP1_020e"), croc, croc, 0, 0, 0);
        int hand = me.HandZone.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Kill(game, croc);
        Assert.Equal(raptor.Card.ATK + 3, raptor.AttackDamage);
        var copies = me.HandZone.Skip(hand).ToList();
        Assert.Equal(2, copies.Count);
        Assert.All(copies, p => Assert.Equal("CS2_120", p.Card.Id));
        Assert.All(copies, p => Assert.True(p.AppliedEnchantments == null || p.AppliedEnchantments.Count == 0));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(op)));
        Clear();

        // Voidcaller: its Deathrattle puts a Demon from hand far right (rule book: a Deathrattle Force Play)
        OnBoard(game, me, "CS2_182");
        Play(game, me, "FP1_022");
        OnBoard(game, me, "CS2_120");
        var demon = Give(me, "CS2_065"); // Voidwalker
        Kill(game, me.BoardZone.Single(m => m.Card.Id == "FP1_022"));
        Assert.Equal(demon.Id, me.BoardZone.Last().Id);
        Clear();

        // Deathlord: the opponent puts a minion from their deck into play, far right of their board
        OnBoard(game, op, "CS2_182");
        Play(game, me, "FP1_009");
        int theirs = op.BoardZone.Count, deck = op.DeckZone.Count(p => p is SabberStoneCore.Model.Entities.Minion);
        Kill(game, me.BoardZone.Single(m => m.Card.Id == "FP1_009"));
        Assert.Equal(theirs + 1, op.BoardZone.Count);
        Assert.Equal(deck - 1, op.DeckZone.Count(p => p is SabberStoneCore.Model.Entities.Minion));
        Clear();

        // Poison Seeds: every minion becomes a 2/2 Treant on its own side
        OnBoard(game, me, "CS2_182"); OnBoard(game, op, "CS2_182"); OnBoard(game, op, "CS2_120");
        Play(game, me, "FP1_019");
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(new[] { "FP1_019t" }, me.BoardZone.Select(m => m.Card.Id));
        Assert.Equal(new[] { "FP1_019t", "FP1_019t" }, op.BoardZone.Select(m => m.Card.Id));
        Clear();

        // Kel'Thuzad: at the end of the turn, the friendly minions that died this turn come back (a fresh turn)
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(op)));
        while (me.HandZone.Count > 0) me.HandZone.Remove(me.HandZone[0]);
        Play(game, me, "FP1_013");
        var a = OnBoard(game, me, "CS2_172"); var b = OnBoard(game, me, "CS2_120");
        Kill(game, a); Kill(game, b);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.Equal(new[] { "FP1_013", "CS2_172", "CS2_120" }, me.BoardZone.Select(m => m.Card.Id));
    }

    [Fact]
    public void Naxxramas_ooze_thaddius_and_undertaker()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;

        // Undertaker: +1 Attack whenever you summon a Deathrattle minion
        Play(game, me, "FP1_028");
        var undertaker = me.BoardZone.Single(m => m.Card.Id == "FP1_028");
        Play(game, me, "FP1_002");
        Assert.Equal(undertaker.Card.ATK + 1, undertaker.AttackDamage);
        Play(game, me, "CS2_182");
        Assert.Equal(undertaker.Card.ATK + 1, undertaker.AttackDamage);
        foreach (var m in me.BoardZone.ToList()) Kill(game, m);

        // Echoing Ooze: an exact copy (its buffs too) at the end of the turn
        Play(game, me, "FP1_003");
        var ooze = me.BoardZone.Single(m => m.Card.Id == "FP1_003");
        SabberStoneCore.Actions.Generic.AddEnchantmentBlock(game, SabberStoneCore.Model.Cards.FromId("FP1_020e"), ooze, ooze, 0, 0, 0);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.Equal(2, me.BoardZone.Count(m => m.Card.Id == "FP1_003"));
        Assert.All(me.BoardZone.Where(m => m.Card.Id == "FP1_003"), m => Assert.Equal(ooze.AttackDamage, m.AttackDamage));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)));
        foreach (var m in me.BoardZone.ToList()) Kill(game, m);

        // Stalagg + Feugen: the second one to die summons Thaddius
        Play(game, me, "FP1_014");
        Play(game, me, "FP1_015");
        Kill(game, me.BoardZone.Single(m => m.Card.Id == "FP1_014"));
        Assert.DoesNotContain(me.BoardZone, m => m.Card.Id == "FP1_014t");
        Kill(game, me.BoardZone.Single(m => m.Card.Id == "FP1_015"));
        Assert.Contains(me.BoardZone, m => m.Card.Id == "FP1_014t");
    }

    [Fact]
    public void Night_elf_huntress_aims_and_shoots_three_times_at_different_enemies()
    {
        // User (2026-10-04): "aim shoot, next aim shoot, third aim shoot". Each shot is a TARGET choice (client
        // CHOICE_TYPE.TARGET: the targeting arrow from the card) between enemies not yet shot; Stealth can't be aimed at.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        var yeti = OnBoard(game, op, "CS2_182");
        var ogre = OnBoard(game, op, "CS2_200");
        var hidden = OnBoard(game, op, "CS2_182");
        hidden.HasStealth = true;
        Play(game, me, "TOY_101");
        var shot = new List<int>();
        for (int i = 0; i < 3; i++)
        {
            Assert.NotNull(me.Choice);
            Assert.Equal(ChoiceType.TARGET, me.Choice.ChoiceType);
            Assert.DoesNotContain(hidden.Id, me.Choice.Choices);
            Assert.All(me.Choice.Choices, id => Assert.DoesNotContain(id, shot));
            Assert.All(me.Choice.Choices, id => Assert.Equal(op, game.IdEntityDic[id].Controller));
            int pick = me.Choice.Choices.Contains(op.Hero.Id) && i == 2 ? op.Hero.Id : me.Choice.Choices.First(id => id != op.Hero.Id);
            shot.Add(pick);
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, pick)));
        }
        Assert.Null(me.Choice);
        Assert.Equal(3, yeti.Damage);
        Assert.Equal(3, ogre.Damage);
        Assert.Equal(3, op.Hero.Damage);
        Assert.Equal(0, hidden.Damage);
    }

    [Fact]
    public void Night_elf_huntress_keeps_its_three_shots_when_picks_are_tried_on_clones_first()
    {
        // Local test 2026-10-05 (local-test-20261005-secrets): the AI's Huntress shot one minion and stopped. The AI tries each
        // pick on a clone; the shot-already set was shared with the clones, so every enemy it tried counted as shot.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        var yeti = OnBoard(game, op, "CS2_182");
        var ogre = OnBoard(game, op, "CS2_200");
        Play(game, me, "TOY_101");
        foreach (int option in me.Choice.Choices.ToList())
        {
            var clone = game.Clone();
            var cm = clone.ControllerById(me.Id);
            clone.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(cm, option));
        }
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, yeti.Id)));
        Assert.NotNull(me.Choice);
        Assert.Contains(ogre.Id, me.Choice.Choices);
        Assert.Contains(op.Hero.Id, me.Choice.Choices);
    }

    [Fact]
    public void Night_elf_huntress_fires_an_arrow_per_pick_and_counts_its_shots_down_like_live()
    {
        // Live (live-20261005 165219): inside the Huntress's POWER block, per pick: the missile SUB_SPELL
        // ReuseFX_Generic_Missile_Arrow_Super from the Huntress holding TARGET + DAMAGE, then TAG_SCRIPT_DATA_NUM_1 counts
        // down (3 from the card data: 2, 1, 0; the client's arrow text "Deal 3 damage. (@ left!)"), then the next pick; the
        // POWER block ends after the last shot.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        var yeti = OnBoard(game, op, "CS2_182");
        var ogre = OnBoard(game, op, "CS2_200");
        var huntress = Give(me, "TOY_101");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, huntress)));
        Assert.Equal(3, huntress[GameTag.TAG_SCRIPT_DATA_NUM_1]);
        foreach (int pick in new[] { yeti.Id, ogre.Id, op.Hero.Id })
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, pick)));
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        int power = sent.FindIndex(d => d.PowerStart is { } p && p.Source == huntress.Id && p.Type == HistoryBlock.Type.POWER);
        var shots = new List<(int Source, List<int> Named, bool Damage)>();
        var counts = new List<int>();
        int end = -1;
        for (int i = power + 1, depth = 1; i < sent.Count; i++)
        {
            if (sent[i].PowerStart != null) depth++;
            if (sent[i].PowerEnd != null && --depth == 0) { end = i; break; }
            if (sent[i].SubSpellStart is { } sub && sub.SpellPrefabGuid.Contains("Missile_Arrow"))
            {
                var inside = sent.Skip(i + 1).TakeWhile(d => d.SubSpellEnd == null).ToList();
                shots.Add((sub.SourceEntityId, inside.FirstOrDefault(d => d.MetaData is { Type: HistoryMeta.Type.TARGET })?.MetaData.Info.ToList() ?? new List<int>(),
                    inside.Any(d => d.MetaData is { Type: HistoryMeta.Type.DAMAGE })));
            }
            if (sent[i].TagChange is { } t && t.Entity == huntress.Id && t.Tag == (int)GameTag.TAG_SCRIPT_DATA_NUM_1) counts.Add(t.Value);
        }
        Assert.True(end > 0, "the POWER block ends");
        Assert.Equal(3, shots.Count);
        Assert.All(shots, s => Assert.Equal(huntress.Id, s.Source));
        Assert.Equal(new[] { yeti.Id, ogre.Id, op.Hero.Id }, shots.Select(s => s.Named.Single()));
        Assert.All(shots, s => Assert.True(s.Damage));
        Assert.Equal(new[] { 2, 1, 0 }, counts);
    }


    // (Actor: the card played / the attacker — the random decks may hold other copies of it)
    private static (SabberStoneCore.Model.Game Game, SabberStoneCore.Model.Entities.IPlayable Secret, SabberStoneCore.Model.Entities.IPlayable Actor, List<PowerHistoryData> Sent)
        SecretFires(string secretId, string action)
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        op.UsedMana = 0;
        var secret = Give(op, secretId);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(op, secret)));
        var guard = OnBoard(game, op, "CS2_182");
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(op));
        me.UsedMana = 0;
        int from;
        SabberStoneCore.Model.Entities.IPlayable actor;
        switch (action)
        {
            case "spell":
                actor = Give(me, "CS2_008");
                from = game.PowerHistory.Full.Count;
                Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, actor, guard)));
                break;
            case "minion":
                actor = Give(me, "CS2_200");
                from = game.PowerHistory.Full.Count;
                Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, actor)));
                break;
            default:
                actor = Give(me, "NEW1_011");
                Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, actor)));
                from = game.PowerHistory.Full.Count;
                game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(me, (SabberStoneCore.Model.Entities.ICharacter)actor, action == "attack-hero" ? op.Hero : guard));
                break;
        }
        var stream = new ClientStream(1, _ => (0, 0, 0));
        stream.Convert(game.PowerHistory.Full.Take(from).ToList());
        return (game, secret, actor, stream.Convert(game.PowerHistory.Full.Skip(from).ToList()));
    }

    private static List<PowerHistoryData> BlockOf(List<PowerHistoryData> sent, int source)
    {
        int start = sent.FindIndex(d => d.PowerStart is { } p && p.Source == source && p.Type == HistoryBlock.Type.TRIGGER);
        var inside = new List<PowerHistoryData>();
        for (int i = start + 1, depth = 1; start >= 0 && i < sent.Count; i++)
        {
            if (sent[i].PowerStart != null) depth++;
            if (sent[i].PowerEnd != null && --depth == 0) break;
            inside.Add(sent[i]);
        }
        return inside;
    }

    [Theory]
    [InlineData("EX1_611")] // Freezing Trap: back to hand
    [InlineData("EX1_594")] // Vaporize: destroyed
    public void A_secret_that_takes_the_attacker_out_names_it_and_tells_the_client_to_drop_the_swing(string id)
    {
        // Live (Freezing Trap, Vaporize): the Secret's block names the attacker (TARGET) and sets SHOULDEXITCOMBAT 1 on it
        // before its effect; the attack's end sets it back to 0 (user: Freezing Trap's attack still landed visually).
        var (game, secret, actor, sent) = SecretFires(id, "attack-hero");
        int attacker = actor.Id;
        var block = BlockOf(sent, secret.Id);
        Assert.Equal(new List<int> { attacker }, block.First(d => d.MetaData is { Type: HistoryMeta.Type.TARGET }).MetaData.Info);
        Assert.Contains(block, d => d.TagChange is { Tag: (int)GAME_TAG.SHOULDEXITCOMBAT, Value: 1 } t && t.Entity == attacker);
        Assert.Contains(sent, d => d.TagChange is { Tag: (int)GAME_TAG.SHOULDEXITCOMBAT, Value: 0 } t && t.Entity == attacker);
        Assert.DoesNotContain(sent, d => d.TagChange is { Tag: (int)GAME_TAG.ATTACKING, Value: 1 });
    }

    [Theory]
    [InlineData("EX1_287", "spell")]  // Counterspell
    [InlineData("MAW_006", "minion")] // Objection!
    public void A_countered_card_is_revealed_named_and_shown_before_it_leaves(string id, string action)
    {
        // Live (EX1_287 5/5, MAW_006 4/4): in the Secret's block REVEALED 1, TARGET and CANT_PLAY on the card (Objection!'s
        // counter effect around it); after the Secret's block a 3 s pause on the countered card.
        var (game, secret, actor, sent) = SecretFires(id, action);
        int countered = actor.Id;
        var block = BlockOf(sent, secret.Id);
        Assert.Contains(block, d => d.TagChange is { Tag: (int)GAME_TAG.REVEALED, Value: 1 } t && t.Entity == countered);
        Assert.Contains(block, d => d.TagChange is { Tag: (int)GAME_TAG.CANT_PLAY, Value: 1 } t && t.Entity == countered);
        Assert.Contains(block, d => d.MetaData is { Type: HistoryMeta.Type.TARGET } m && m.Info.Contains(countered));
        if (id == "MAW_006") Assert.Contains(block, d => d.SubSpellStart?.SpellPrefabGuid.Contains("_Counter_Impact") == true);
        Assert.Contains(sent, d => d.MetaData is { Type: HistoryMeta.Type.SLUSH_TIME, Data: 3000 } m && m.Info.Contains(countered));
        // live: set aside, not the Graveyard; a countered spell isn't a spell played (user 2026-10-05: match live)
        Assert.Equal(Zone.SETASIDE, actor.Zone.Type);
        if (action == "spell") Assert.Equal(0, actor.Controller.NumSpellsPlayedThisGame);
    }

    [Theory]
    [InlineData("ICC_200", "attack-minion")] // Venomstrike Trap: its Cobra
    [InlineData("EX1_294", "minion")]        // Mirror Entity: the minion it copied
    public void A_secret_names_the_minion_it_summons_or_copies(string id, string action)
    {
        // Live: Venomstrike Trap / Bear Trap / Cat Trick name the minion they summon right after it; Mirror Entity's block
        // opens naming the minion it copies (its copy: COPIED_FROM_ENTITY_ID).
        var (game, secret, actor, sent) = SecretFires(id, action);
        var block = BlockOf(sent, secret.Id);
        int target = block.FindIndex(d => d.MetaData is { Type: HistoryMeta.Type.TARGET });
        int made = block.FindIndex(d => d.FullEntity != null);
        Assert.True(target >= 0 && made >= 0);
        if (id == "EX1_294")
        {
            Assert.Equal(new List<int> { actor.Id }, block[target].MetaData.Info);
            Assert.True(target < made);
        }
        else
        {
            Assert.Equal(new List<int> { block[made].FullEntity.Entity }, block[target].MetaData.Info);
            Assert.Equal(made + 1, target);
        }
    }

    [Fact]
    public void A_played_card_ends_with_a_pause_and_a_group_summon_plays_one_spawn_effect()
    {
        // Live: every play ends with SLUSH_TIME 1000 on the card after its PLAY block (6095 of 6100); a group summon is one
        // spawn effect (Feral Spirit's two Wolves, Rime Sculptor's two Elementals: one OverrideSpawn SUB_SPELL). User: the
        // "summons around" minions appeared one by one.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var spirit = Give(me, "EX1_248");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, spirit)));
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        var subs = sent.Where(d => d.SubSpellStart?.SpellPrefabGuid.Contains("OverrideSpawn") == true).ToList();
        Assert.Single(subs);
        int sub = sent.IndexOf(subs[0]);
        Assert.Equal(2, sent.Skip(sub).TakeWhile(d => d.SubSpellEnd == null).Count(d => d.FullEntity != null));
        int playEnd = -1;
        int play = sent.FindIndex(d => d.PowerStart is { } p && p.Source == spirit.Id && p.Type == HistoryBlock.Type.PLAY);
        for (int i = play + 1, depth = 1; i < sent.Count; i++)
        {
            if (sent[i].PowerStart != null) depth++;
            if (sent[i].PowerEnd != null && --depth == 0) { playEnd = i; break; }
        }
        Assert.True(sent[playEnd + 1].MetaData is { Type: HistoryMeta.Type.SLUSH_TIME, Data: 1000 } m && m.Info.Contains(spirit.Id));
    }

    [Fact]
    public void Sightless_watcher_offers_linked_copies_and_clears_the_picked_one()
    {
        // Live (live-20261005 112456): the options are set-aside copies linked to their deck cards (LINKED_ENTITY); the picked
        // copy goes to the graveyard (the client clears it), the deck card goes on top, HISTORY_TARGET names it. Ours offered
        // unlinked new cards and the picked one stayed on screen (user 2026-10-05).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var watcher = Give(me, "BT_323");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, watcher)));
        Assert.NotNull(me.Choice);
        var options = me.Choice.Choices.Select(id => game.IdEntityDic[id]).ToList();
        Assert.All(options, o => Assert.Equal(Zone.DECK, game.IdEntityDic[o[GameTag.LINKED_ENTITY]].Zone.Type));
        var pick = options[1];
        var deckCard = game.IdEntityDic[pick[GameTag.LINKED_ENTITY]];
        int from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, pick.Id)));
        Assert.Equal(Zone.GRAVEYARD, pick.Zone.Type);
        Assert.Same(deckCard, me.DeckZone.TopCard);
        Assert.Contains(game.PowerHistory.Full.Skip(from), e => e is SabberStoneCore.Kettle.PowerHistoryMetaData { Type: MetaDataType.HISTORY_TARGET } m && m.Info.Contains(deckCard.Id));
    }

    [Fact]
    public void Brawl_names_every_minion_survivor_first_like_live()
    {
        // Live (live-20261005 184804): Brawl's POWER block opens with one TARGET per minion, the survivor first; the client's
        // BrawlSpell takes the TARGET that isn't TO_BE_DESTROYED as the survivor. Ours named only the destroyed ones.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        var minions = new[] { OnBoard(game, me, "CS2_182"), OnBoard(game, me, "CS2_200"), OnBoard(game, op, "CS2_182"), OnBoard(game, op, "CS2_231") };
        var brawl = Give(me, "EX1_407");
        int from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, brawl)));
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        int power = sent.FindIndex(d => d.PowerStart is { } p && p.Source == brawl.Id && p.Type == HistoryBlock.Type.POWER);
        var named = sent.Skip(power + 1).TakeWhile(d => d.MetaData is { Type: HistoryMeta.Type.TARGET }).Select(d => d.MetaData.Info.Single()).ToList();
        Assert.Equal(minions.Select(m => m.Id).OrderBy(x => x), named.OrderBy(x => x));
        var alive = minions.Where(m => m.Zone?.Type == Zone.PLAY).ToList();
        Assert.Single(alive);
        Assert.Equal(alive[0].Id, named[0]);
    }

    [Fact]
    public void Focused_will_silences_then_buffs_inside_its_holy_light_effect_like_live()
    {
        // Live (live-20261005 190813): no TARGET on the block; SILENCED, then the server-picked
        // ReuseFX_BuffImpact_HolyLight_Heal_Small_Super (Source none, Targets = the minion) around the +3 Health enchantment.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var yeti = OnBoard(game, me, "CS2_182");
        var will = Give(me, "CS3_027");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, will, yeti)));
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        int power = sent.FindIndex(d => d.PowerStart is { } p && p.Source == will.Id && p.Type == HistoryBlock.Type.POWER);
        var block = new List<PowerHistoryData>();
        for (int i = power + 1, depth = 1; i < sent.Count; i++)
        {
            if (sent[i].PowerStart != null) depth++;
            if (sent[i].PowerEnd != null && --depth == 0) break;
            block.Add(sent[i]);
        }
        Assert.DoesNotContain(block, d => d.MetaData is { Type: HistoryMeta.Type.TARGET });
        int silenced = block.FindIndex(d => d.TagChange is { Tag: (int)GAME_TAG.SILENCED, Value: 1 });
        int sub = block.FindIndex(d => d.SubSpellStart?.SpellPrefabGuid.StartsWith("ReuseFX_BuffImpact_HolyLight_Heal_Small_Super") == true);
        Assert.True(silenced >= 0 && sub > silenced, $"silenced {silenced}, sub {sub}");
        Assert.Equal(0, block[sub].SubSpellStart.SourceEntityId);
        Assert.Equal(new List<int> { yeti.Id }, block[sub].SubSpellStart.TargetEntityIds.ToList());
        var inside = block.Skip(sub + 1).TakeWhile(d => d.SubSpellEnd == null).ToList();
        Assert.Contains(inside, d => d.TagChange is { Tag: (int)GAME_TAG.HEALTH } t && t.Entity == yeti.Id);
    }

    [Fact]
    public void Reincarnate_returns_the_minion_to_life_for_its_owner()
    {
        // "Destroy a minion, then return it to life with full Health": the minion comes back for its owner (user 2026-10-05:
        // Reincarnate on an enemy minion brought it back on the caster's side — a 1-mana steal).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        var theirs = OnBoard(game, op, "CS2_200");
        theirs.Damage = 3;
        int before = op.BoardZone.Count, mine = me.BoardZone.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "FP1_025"), theirs)));
        Assert.Equal(before, op.BoardZone.Count);
        Assert.Equal(mine, me.BoardZone.Count);
        var back = op.BoardZone.Last();
        Assert.Equal("CS2_200", back.Card.Id);
        Assert.Equal(back.BaseHealth, back.Health);

        var own = OnBoard(game, me, "CS2_182");
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "FP1_025"), own)));
        Assert.Equal("CS2_182", me.BoardZone.Last().Card.Id);
        Assert.Equal(before, op.BoardZone.Count);
    }

    [Fact]
    public void Shadow_sculptor_does_not_count_itself()
    {
        // User ruling 2026-10-05: a card isn't "played" until its effect is done (wiki: "does not count itself").
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, Give(me, "GAME_005"))));
        var sculptor = Give(me, "YOD_017");
        int hand = me.HandZone.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, sculptor)));
        Assert.Equal(hand - 1 + 1, me.HandZone.Count); // the Sculptor left, one card drawn (the Coin before it)
        Assert.Equal(2, me.NumCardsPlayedThisTurn);       // the wire counter does count it
    }

    [Fact]
    public void Fist_of_raden_summons_a_legendary_and_does_not_trigger_without_one()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var fist = Give(me, "YOD_042");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, fist)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_029"), me.Opponent.Hero)));
        Assert.Single(me.BoardZone);
        Assert.Equal(Rarity.LEGENDARY, me.BoardZone[0].Card.Rarity);
        Assert.Equal(4, me.BoardZone[0].Card.Cost);
        Assert.Equal(3, me.Hero.Weapon.Durability);

        var pool = SabberStoneCore.Model.Cards.BoardCostMinionCards(game.FormatType);
        int? none = Enumerable.Range(0, 11).Cast<int?>().FirstOrDefault(k =>
            !pool.TryGetValue(k!.Value, out var list) || !list.Any(x => x.Rarity == Rarity.LEGENDARY));
        if (none == null) return; // every cost has a Legendary in this pool
        me.UsedMana = 0;
        var spell = Give(me, "CS2_029");
        spell.Cost = none.Value;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, spell, me.Opponent.Hero)));
        Assert.Single(me.BoardZone);
        Assert.Equal(3, me.Hero.Weapon.Durability);
    }

    [Fact]
    public void Chaos_gazer_curses_a_playable_card_discarded_at_its_owners_turn_end()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        Give(op, "EX1_620"); // Molten Giant: too costly next turn
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "YOD_027"))));
        var cursed = op.HandZone.Where(p => p.AppliedEnchantments?.Any(e => e.Card.Id == "YOD_027e") == true).ToList();
        Assert.Single(cursed);
        Assert.True(cursed[0].Cost <= 10);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.Equal(Zone.HAND, cursed[0].Zone.Type);   // still theirs during their turn
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(op)));
        Assert.Equal(Zone.GRAVEYARD, cursed[0].Zone.Type);
    }

    [Fact]
    public void Deck_gated_families_are_generated_only_with_one_in_the_starting_deck()
    {
        // User rule 2026-10-05: Galakrond / Invoke, Herald and Imbue cards can't be Discovered or randomly generated unless
        // the starting deck has one of that family (a payoff counts); Excavate isn't gated.
        _ = Db.Value; // the client's cards (Heralds, Imbue)
        var invoke = SabberStoneCore.Model.Cards.FromId("DRG_303");      // Disciple of Galakrond
        var herald = SabberStoneCore.Model.Cards.FromId("CATA_722");     // Envoy of the End
        var imbue = SabberStoneCore.Model.Cards.FromId("EDR_800");       // Flutterwing Guardian
        var excavate = SabberStoneCore.Model.Cards.FromId("WW_334");     // Reinforced Plating
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.DeckCards.RemoveAll(x => x.DeckGatedFamilies != 0); // the filled test deck may hold one (a Druid Imbue card)
        Assert.False(SabberStoneCore.Model.Cards.DeckAllows(me, invoke));
        Assert.False(SabberStoneCore.Model.Cards.DeckAllows(me, herald));
        Assert.False(SabberStoneCore.Model.Cards.DeckAllows(me, imbue));
        Assert.True(SabberStoneCore.Model.Cards.DeckAllows(me, excavate));
        var pool = SabberStoneCore.Model.Cards.GenerationPool(game, me).ToList();
        Assert.DoesNotContain(invoke, pool);
        Assert.DoesNotContain(herald, pool);
        Assert.Equal(SabberStoneCore.Model.Cards.FormatTypeCards(game.FormatType).Count(x => x.DeckGatedFamilies == 0), pool.Count); // only those
        // a random Priest minion (the engine's random tasks) never gives a Galakrond card to this deck
        for (int i = 0; i < 40; i++)
        {
            var list = SabberStoneCore.Tasks.SimpleTasks.RandomCardTask.GetCardList(me.Hero, CardType.MINION, CardClass.PRIEST);
            Assert.DoesNotContain(list, x => x.DeckGatedFamilies != 0);
        }

        var opened = TenManaGame();
        var you = opened.CurrentPlayer;
        you.DeckCards.RemoveAll(x => x.DeckGatedFamilies != 0);
        you.DeckCards.Add(SabberStoneCore.Model.Cards.FromId("DRG_600"));  // a Galakrond in the starting deck opens Invoke cards
        you.DeckCards.Add(SabberStoneCore.Model.Cards.FromId("EDR_940"));  // Merry Moonkin, an Imbue payoff
        Assert.True(SabberStoneCore.Model.Cards.DeckAllows(you, invoke));
        Assert.True(SabberStoneCore.Model.Cards.DeckAllows(you, imbue));
        Assert.False(SabberStoneCore.Model.Cards.DeckAllows(you, herald));
    }

    [Fact]
    public void A_card_never_generates_itself()
    {
        // User rule 2026-10-05: "cards cannot actually generate themselves". The running effect's card is known to every
        // random pick (Cards.GeneratingCard), restored after nested effects.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var copter = OnBoard(game, me, "YOD_004");
        SabberStoneCore.Model.Card seen = null, after = null;
        game.TaskQueue.Execute(new SabberStoneCore.Tasks.SimpleTasks.CustomTask((g, c, s, t, st) =>
        {
            g.TaskQueue.Execute(new SabberStoneCore.Tasks.SimpleTasks.CustomTask((g2, c2, s2, t2, st2) => { }), c, c.Hero, null);
            seen = SabberStoneCore.Model.Cards.GeneratingCard(g);
        }), me, copter, null);
        after = SabberStoneCore.Model.Cards.GeneratingCard(game);
        Assert.Equal("YOD_004", seen?.Id);
        Assert.Null(after);
    }

    [Fact]
    public void Moarg_artificer_doubles_spell_damage_to_minions_only()
    {
        // Rule book DH2a: damage-doubling after Spell Damage; Mo'arg: "All minions take double damage from spells".
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        OnBoard(game, me, "BT_733");
        var ogre = OnBoard(game, op, "CS2_200"); // 6/7
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_029"), ogre))); // Fireball 6
        Assert.True(ogre.IsDead || ogre.Zone.Type == Zone.GRAVEYARD);
        int hp = op.Hero.Health;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_029"), op.Hero)));
        Assert.Equal(hp - 6, op.Hero.Health);
    }

    [Fact]
    public void Teron_gorefiend_resummons_what_he_destroyed_with_plus_one()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        OnBoard(game, me, "CS2_182");
        OnBoard(game, me, "CS2_200");
        var teron = Give(me, "BT_126");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, teron)));
        Assert.Single(me.BoardZone);
        ((SabberStoneCore.Model.Entities.Minion)teron).Destroy();
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(new[] { "CS2_182", "CS2_200" }, me.BoardZone.Select(m => m.Card.Id).OrderBy(x => x));
        Assert.Contains(me.BoardZone, m => m.Card.Id == "CS2_182" && m.AttackDamage == 5 && m.Health == 6);
    }

    [Fact]
    public void Kaelthas_makes_every_third_spell_cost_one()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        OnBoard(game, me, "BT_255");
        var fireball = Give(me, "CS2_029");
        Assert.Equal(4, fireball.Cost);
        for (int i = 0; i < 2; i++)
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "GAME_005"))));
        game.AuraUpdate();
        Assert.Equal(1, fireball.Cost);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, fireball, me.Opponent.Hero)));
        game.AuraUpdate();
        var next = Give(me, "CS2_029");
        game.AuraUpdate();
        Assert.Equal(4, next.Cost);
    }

    [Fact]
    public void Outland_dormant_and_cost_cards_work()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        // Aldor Attendant: Librams cost (1) less this game, also ones that come to hand later
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "BT_020"))));
        var libram = Give(me, "BT_024");
        game.AuraUpdate();
        Assert.Equal(8, libram.Cost);
        // Imprisoned Observer: Dormant 2 turns, then 2 damage to enemy minions
        var observer = OnBoard(game, me, "BT_004");
        var ogre = OnBoard(game, op, "CS2_200");
        Assert.True(observer[GameTag.DORMANT] > 0);
        for (int i = 0; i < 4; i++) Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(game.CurrentPlayer)));
        Assert.Equal(0, observer[GameTag.DORMANT]);
        Assert.Equal(5, ogre.Health);
        // Mana Burn: the opponent has 2 fewer crystals on their next turn
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "BT_753"))));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.Equal(op.BaseMana - 2, op.RemainingMana);
    }

    [Fact]
    public void Magtheridon_awakens_after_three_warders_die_and_destroys_all_minions()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        var mine = OnBoard(game, me, "CS2_182");
        var mag = (SabberStoneCore.Model.Entities.Minion)Give(me, "BT_850");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, mag)));
        Assert.True(mag[GameTag.DORMANT] > 0);
        var warders = op.BoardZone.Where(m => m.Card.Id == "BT_850t").ToList();
        Assert.Equal(3, warders.Count);
        foreach (var w in warders) { w.Destroy(); game.DeathProcessingAndAuraUpdate(); }
        Assert.Equal(0, mag[GameTag.DORMANT]);
        Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, mine.Zone.Type);
        Assert.Single(me.BoardZone);
    }

    [Fact]
    public void Lurker_below_continues_in_one_direction_while_it_kills()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        var a = OnBoard(game, op, "CS2_168"); // 2/1
        var b = OnBoard(game, op, "CS2_168");
        var c = OnBoard(game, op, "CS2_168");
        var d = OnBoard(game, op, "CS2_182"); // 4/5 stops it
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.MinionTarget(me, Give(me, "BT_230"), b)));
        // from b: either a (left, then nothing) or c then d (right)
        bool left = a.Zone.Type == Zone.GRAVEYARD;
        Assert.Equal(Zone.GRAVEYARD, b.Zone.Type);
        if (left) Assert.Equal(Zone.PLAY, c.Zone.Type);
        else { Assert.Equal(Zone.GRAVEYARD, c.Zone.Type); Assert.Equal(2, d.Health); Assert.Equal(Zone.PLAY, a.Zone.Type); }
    }

    [Fact]
    public void Soul_mirror_copies_attack_their_originals()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        var yeti = OnBoard(game, op, "CS2_182"); // 4/5
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "BT_198"))));
        var copy = me.BoardZone.Single();
        Assert.Equal("CS2_182", copy.Card.Id);
        Assert.Equal(1, yeti.Health);
        Assert.Equal(1, copy.Health);
    }

    [Fact]
    public void Outland_rulings_from_the_user_hold()
    {
        // user 2026-10-05: Akama Prime's Stealth never breaks; Nagrand Slam summons all four, then they attack; a
        // Replicat-o-tron copy keeps its enchantments (board to board); "targets enemies if possible" picks an enemy.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        var akama = OnBoard(game, me, "BT_713t");
        game.AuraUpdate();
        Assert.True(akama.HasStealth);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(me, akama, op.Hero)));
        Assert.True(akama.HasStealth);

        var replicat = OnBoard(game, me, "BT_190");
        var neighbor = OnBoard(game, me, "CS2_168");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_092"), replicat))); // Blessing of Kings +4/+4
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        var replicats = me.BoardZone.Where(m => m.Card.Id == "BT_190").ToList(); // a random neighbor became one
        Assert.Equal(2, replicats.Count);
        Assert.All(replicats, m => Assert.Equal(replicat.AttackDamage, m.AttackDamage)); // with the +4/+4

        var fireball = (SabberStoneCore.Model.Entities.Spell)Give(op, "CS2_029");
        for (int i = 0; i < 20; i++)
            Assert.NotEqual(op.Id, fireball.GetRandomValidTarget(enemiesIfPossible: true).Controller.Id);
    }

    [Fact]
    public void Nagrand_slam_summons_all_four_before_any_attacks()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        OnBoard(game, me.Opponent, "CS2_182");
        int from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "BT_163"))));
        var entries = game.PowerHistory.Full.Skip(from).ToList();
        int lastSummon = entries.FindLastIndex(e => e is SabberStoneCore.Kettle.PowerHistoryFullEntity f && f.Entity.Name == "BT_163t");
        int firstAttack = entries.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart b && b.BlockType == BlockType.ATTACK);
        Assert.True(firstAttack > lastSummon, $"summon {lastSummon}, attack {firstAttack}");
    }

    [Theory]
    [InlineData(26, "SCH_199t13")] // Journey to Un'Goro: Adapt
    [InlineData(10, "SCH_199t9")]  // Hall of Explorers (The Museum)
    [InlineData(120, "SCH_199t30")] // Voyage to the Sunken City: Discover a Sunken City card
    [InlineData(0, "SCH_199t")]    // no / a special board: the Stormwind version
    public void Transfer_student_starts_the_game_as_its_boards_version(int board, string version)
    {
        // wiki Transfer Student: "At the start of game, Transfer Student's effect is automatically replaced by one of the
        // following ones, depending on the board"; special boards use the Stormwind version
        _ = Db.Value;
        var student = SabberStoneCore.Model.Cards.FromId("SCH_199");
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.DRUID, Player2HeroClass = CardClass.MAGE, Board = board,
            Player1Deck = Enumerable.Repeat(student, 2).ToList(),
            FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false,
        });
        game.StartGame();
        var p1 = game.Player1;
        var all = p1.DeckZone.Concat(p1.HandZone).ToList();
        Assert.Equal(2, all.Count(p => p.Card.Id == version));
        Assert.DoesNotContain(all, p => p.Card.Id == "SCH_199");
    }

    [Fact]
    public void In_hand_transform_takes_the_new_cards_stats_keeping_its_own_changes()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var g = Give(me, "DMF_124");
        int before = g[GameTag.ATK];
        bool had = g.NativeTags.TryGetValue(GameTag.ATK, out int raw);
        SabberStoneCore.Actions.Generic.ChangeEntityBlock(me, g, SabberStoneCore.Model.Cards.FromId("DMF_124t"), false);
        Assert.True(g[GameTag.ATK] == 3, $"before {before} had {had} raw {raw} after {g[GameTag.ATK]} old card atk {SabberStoneCore.Model.Cards.FromId("DMF_124")[GameTag.ATK]} tags has {SabberStoneCore.Model.Cards.FromId("DMF_124").Tags.ContainsKey(GameTag.ATK)}");
    }

    [Fact]
    public void Corrupt_upgrades_cards_in_hand_after_a_costlier_card_is_played()
    {
        // wiki Corrupt: "activates when the user plays a card with a higher mana cost than the Corrupt card's cost while in
        // hand"; enchantments kept; Cascading Disaster corrupts again; Horrendous Growth endlessly (+1/+1)
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var insight = Give(me, "DMF_054");         // 2
        var disaster = Give(me, "DMF_117");        // 4
        var growth = Give(me, "DMF_124");          // 2
        var cheap = Give(me, "CS2_168");           // 1: a cheaper card corrupts nothing
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, cheap)));
        Assert.Equal("DMF_054", insight.Card.Id);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "CS2_200")))); // 6
        Assert.Equal("DMF_054t", insight.Card.Id);
        Assert.Equal("DMF_117t", disaster.Card.Id);
        Assert.Equal("DMF_124t", growth.Card.Id);
        var g1 = (SabberStoneCore.Model.Entities.Minion)growth;
        Assert.True(g1.AttackDamage == 3, $"atk {g1.AttackDamage} tag {g1[GameTag.ATK]} card {g1.Card[GameTag.ATK]} type {growth.GetType().Name} same {ReferenceEquals(growth, me.HandZone.FirstOrDefault(p => p.Id == growth.Id))}");
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "CS2_200"))));
        Assert.Equal("DMF_117t2", disaster.Card.Id);
        Assert.Equal("DMF_124t", growth.Card.Id);
        Assert.Equal(4, ((SabberStoneCore.Model.Entities.Minion)growth).AttackDamage);
    }

    [Fact]
    public void Spellburst_fires_once_like_live()
    {
        // wiki Spellburst: the first spell only; live (YOP_018): TRIGGER block, keyword SPELLBURST, SPELLBURST -> 0 first
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var initiate = OnBoard(game, me, "SCH_231"); // Spellburst: Gain +2 Attack
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "GAME_005"))));
        Assert.Equal(3, initiate.AttackDamage);
        Assert.Equal(0, initiate[HsTag.SPELLBURST]);
        int from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "GAME_005"))));
        Assert.Equal(3, initiate.AttackDamage);
        Assert.DoesNotContain(game.PowerHistory.Full.Skip(from), e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart b && b.Source == initiate.Id);
    }

    [Fact]
    public void Scholomance_shared_mechanics_work()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        // Soul Fragments: shuffled in, cast when drawn (2 Health)
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "SCH_700"))));
        Assert.Equal(2, me.DeckZone.Count(p => p.Card.Id == "SCH_307t"));
        // Coerce: only damaged minions without Combo
        var healthy = OnBoard(game, op, "CS2_182");
        var hurt = OnBoard(game, op, "CS2_200");
        hurt.Damage = 1;
        me.IsComboActive = false;
        var coerce = Give(me, "SCH_521");
        Assert.False(coerce.IsValidPlayTarget(healthy));
        Assert.True(coerce.IsValidPlayTarget(hurt));
        me.IsComboActive = true;
        Assert.True(coerce.IsValidPlayTarget(healthy));
        // Professor Slate: spells are Poisonous
        OnBoard(game, me, "SCH_539");
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_008"), healthy))); // Moonfire 1
        Assert.True(healthy.IsDead || healthy.Zone.Type == Zone.GRAVEYARD);
        // Flesh Giant: each change of your hero's Health during your turns
        var giant = Give(me, "SCH_140");
        int cost = giant.Cost;
        me.Hero.Armor = 0;
        me.Hero.TakeDamage(op.Hero, 1);
        me.Hero.TakeHeal(me.Hero, 1);
        game.AuraUpdate();
        Assert.Equal(cost - 2, giant.Cost);
    }

    [Fact]
    public void Secret_passage_sets_the_hand_aside_and_swaps_back_at_turn_end()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var hand = me.HandZone.ToList();
        int deck = me.DeckZone.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "SCH_305"))));
        Assert.Equal(4, me.HandZone.Count);
        Assert.Equal(deck - 4, me.DeckZone.Count);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.Equal(hand.Select(p => p.Id).OrderBy(x => x), me.HandZone.Select(p => p.Id).OrderBy(x => x));
        Assert.Equal(deck, me.DeckZone.Count);
    }

    [Fact]
    public void Headmaster_kelthuzad_summons_what_the_spell_destroyed()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        OnBoard(game, me, "SCH_224");
        var yeti = OnBoard(game, op, "CS2_182");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_029"), yeti))); // Fireball 6
        Assert.Contains(me.BoardZone, m => m.Card.Id == "CS2_182");
    }

    [Fact]
    public void Overload_amounts_come_from_the_card_text()
    {
        // the client export marks Overload with OVERLOAD = 1; the amount is the text's "(N)" (live: Lightning Bloom and
        // Earth Elemental owe 2)
        _ = Db.Value;
        Assert.Equal(2, SabberStoneCore.Model.Cards.FromId("CORE_EX1_250").Overload);
        Assert.Equal(2, SabberStoneCore.Model.Cards.FromId("SCH_427").Overload);
        var unparsed = SabberStoneCore.Model.Cards.All.Where(c => c.Collectible && c.HasOverload &&
                System.Text.RegularExpressions.Regex.IsMatch(c.Text ?? "", @"Overload") &&
                !System.Text.RegularExpressions.Regex.IsMatch(c.Text ?? "", @"Overload(</b>)?[^()]{0,30}\(\d+\)"))
            .Select(c => c.Id).ToList();
        Assert.True(unparsed.Count == 0, "no amount in the text: " + string.Join(", ", unparsed.Take(20)));
    }

    [Fact]
    public void Runic_carvings_overloads_only_with_its_rush_option()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "SCH_612"), chooseOne: 1)));
        Assert.Equal(4, me.BoardZone.Count(m => m.Card.Id == "SCH_612t" && !m.IsRush));
        Assert.Equal(0, me.OverloadOwed);
        me.UsedMana = 0;
        foreach (var m in me.BoardZone.ToList()) m.Destroy();
        game.DeathProcessingAndAuraUpdate();
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "SCH_612"), chooseOne: 2)));
        Assert.Equal(4, me.BoardZone.Count(m => m.Card.Id == "SCH_612t" && m.IsRush));
        Assert.Equal(2, me.OverloadOwed);
    }

    [Fact]
    public void Nethrandamus_upgrades_in_hand_when_friendly_minions_die()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var neth = Give(me, "BT_481");
        for (int i = 0; i < 3; i++) { OnBoard(game, me, "CS2_168").Destroy(); game.DeathProcessingAndAuraUpdate(); }
        Assert.Equal(3, neth[GameTag.TAG_SCRIPT_DATA_NUM_1]);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, neth)));
        Assert.Equal(3, me.BoardZone.Count);
        Assert.All(me.BoardZone.Where(m => m.Id != neth.Id), m => Assert.Equal(3, m.Card.Cost));
    }

    [Fact]
    public void Boompistol_bully_taxes_enemy_battlecry_cards_for_their_next_turn()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var archer = Give(me.Opponent, "CS2_189");
        int cost = archer.Cost;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "YOD_033"))));
        Assert.Equal(cost + 5, archer.Cost);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.Equal(cost + 5, archer.Cost);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)));
        Assert.Equal(cost, archer.Cost);
    }

    [Fact]
    public void Galakrond_awakening_cards_do_what_they_say()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        // Air Raid: two Taunt Recruits, and its Twinspell copy back in hand
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "YOD_012"))));
        Assert.Equal(2, me.BoardZone.Count(m => m.Card.Id == "CS2_101t" && m.HasTaunt));
        Assert.Contains(me.HandZone, p => p.Card.Id == "YOD_012ts");
        // Explosive Evolution: a 4-Cost minion becomes a 7-Cost one
        var yeti = OnBoard(game, op, "CS2_182");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "YOD_020"), yeti)));
        Assert.Equal(7, op.BoardZone.Last().Card.Cost);
        // Bomb Wrangler: damaged -> a Boom Bot
        var wrangler = OnBoard(game, me, "YOD_024");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_008"), wrangler)));
        Assert.Contains(me.BoardZone, m => m.Card.Id == "GVG_110t");
        // Aeon Reaver: damage equal to the target's Attack
        var ogre = OnBoard(game, op, "CS2_200");
        int health = ogre.Health;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.MinionTarget(me, Give(me, "YOD_014"), ogre)));
        Assert.Equal(health - ogre.AttackDamage, ogre.Health);
    }

    [Theory]
    [InlineData("")]
    [InlineData("EX1_611")] // Freezing Trap
    [InlineData("EX1_610")] // Explosive Trap
    public void The_ai_attacks_into_secrets_it_cannot_see(string secretId)
    {
        // The AI planned on the real game, so it read the human's hidden Secrets: it never attacked into Freezing Trap (the
        // game stalled) and went exactly around Explosive Trap (user 2026-10-05). It plans on a copy without the opponent's
        // Secrets now: it attacks as with none up, and plans again when one fires (Freezing Trap: it replays the minion).
        var game = TenManaGame();
        var ai = game.CurrentPlayer;
        var human = ai.Opponent;
        foreach (var card in ai.HandZone.ToList()) ai.HandZone.Remove(card);
        OnBoard(game, ai, "CS2_200"); OnBoard(game, ai, "CS2_182"); OnBoard(game, ai, "CS2_120");
        OnBoard(game, human, "CS2_182"); OnBoard(game, human, "CS2_172");
        if (secretId != "")
        {
            var secret = (SabberStoneCore.Model.Entities.Spell)SabberStoneCore.Model.Entities.Entity.FromCard(human, SabberStoneCore.Model.Cards.FromId(secretId));
            human.SecretZone.Add(secret);
            secret.Power?.Trigger?.Activate(secret);
        }
        var planner = new HsServer.Sabber.Ai.PlannerAi { BudgetMs = 500 };
        int attacks = 0;
        string firstAttacker = null;
        for (int i = 0; i < 20 && game.CurrentPlayer == ai; i++)
        {
            var t = planner.Step(game, ai);
            if (t.PlayerTaskType == SabberStoneCore.Tasks.PlayerTasks.PlayerTaskType.MINION_ATTACK)
            {
                attacks++;
                firstAttacker ??= t.Source.Card.Id;
            }
            game.Process(t);
        }
        Assert.True(attacks >= 3, $"{attacks} minion attacks");
        // with a Secret up it plays around it like a player: its least valuable minion (River Crocolisk 2/3) attacks first
        if (secretId != "") Assert.Equal("CS2_120", firstAttacker);
        Assert.True(human.Hero.Damage > 0, "it also went face");
    }

    [Fact]
    public void Night_elf_huntress_shot_resolves_before_the_next_aim()
    {
        // User (2026-10-04): "each subsequent shot resolves the event, so a deathrattle for example will trigger": a Haunted
        // Creeper shot dead leaves its spiders before the next aim, and they can be aimed at.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        var creeper = OnBoard(game, op, "FP1_002");
        Play(game, me, "TOY_101");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, creeper.Id)));
        var spiders = op.BoardZone.Where(m => m.Card.Id == "FP1_002t").ToList();
        Assert.Equal(2, spiders.Count);
        Assert.All(spiders, sp => Assert.Contains(sp.Id, me.Choice.Choices));
    }

    [Fact]
    public void Gnomelia_deathrattle_plays_her_area_blast_trigger_effect()
    {
        // Client CardDef TOY_100 m_TriggerEffectDefs: 0 = Trigger_AdjacentBurst (her cleave), 1 = AE (her Deathrattle; user:
        // "then does a deathrattle aoe vfx"). The TRIGGER block's effect index picks it.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        Play(game, me, "TOY_100");
        int from = game.PowerHistory.Full.Count;
        Kill(game, me.BoardZone.Single(m => m.Card.Id == "TOY_100"));
        var block = game.PowerHistory.Full.Skip(from).OfType<SabberStoneCore.Kettle.PowerHistoryBlockStart>()
            .Single(b => b.BlockType == BlockType.TRIGGER && b.TriggerKeyword == (int)GameTag.DEATHRATTLE);
        Assert.Equal(1, block.Index);
    }

    [Fact]
    public void Temporary_cards_show_their_outline_and_are_discarded_at_the_end_of_turn()
    {
        // Live (live-20261003-dk-classxp): a Temporary card carries TEMPORARY 1 + VALEERASHADOW 1 under the invisible GBL_999e;
        // played, both go back to 0. Wiki Temporary: "Discarded at the end of your turn" (a discard).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        Play(game, me, "GIFT_02"); // Jaina's Gift
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, me.Choice.Choices[0])));
        var gift = me.HandZone.Last();
        Assert.Equal(1, gift[SabberStoneCore.Enums.HsTag.TEMPORARY]);
        Assert.Equal(1, gift[GameTag.VALEERASHADOW]);
        Assert.Contains(gift.AppliedEnchantments, e => e.Card.Id == "GBL_999e");

        Play(game, me, "BOT_568"); // The Soularium: three drawn cards, Temporary too
        var drawn = me.HandZone.Skip(me.HandZone.Count - 3).ToList();
        Assert.All(drawn, p => Assert.Equal(1, p[SabberStoneCore.Enums.HsTag.TEMPORARY]));

        // played: an ordinary card again
        var bolt = drawn.FirstOrDefault(p => p is SabberStoneCore.Model.Entities.Minion) ?? drawn[0];
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, bolt,
            bolt is SabberStoneCore.Model.Entities.Spell sp && (sp.Card.RequiresTarget || sp.Card.MustHaveTargetToPlay) ? me.Opponent.Hero : null)));
        Assert.Equal(0, bolt[SabberStoneCore.Enums.HsTag.TEMPORARY]);
        Assert.Equal(0, bolt[GameTag.VALEERASHADOW]);

        // the rest: discarded at the end of the turn
        var rest = new[] { gift }.Concat(drawn.Where(p => p != bolt)).ToList();
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.All(rest, p => Assert.Equal(Zone.GRAVEYARD, p.Zone.Type));
    }

    [Fact]
    public void Gaining_corpses_plays_the_gain_corpse_effect()
    {
        // Live: Plagued Grain RLK_039 plays ReuseFX_..._BuffImpact_GainCorpse from its hero, Meat Grinder RLK_120 from itself
        // (RecordedSubSpellSources.tsv); the same for every card effect that gains Corpses (Generic.GainCorpses).
        var game = DeathKnightGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        List<SabberStoneCore.Kettle.PowerHistorySubSpellStart> Gain(int from) => game.PowerHistory.Full.Skip(from)
            .OfType<SabberStoneCore.Kettle.PowerHistorySubSpellStart>().Where(x => x.Prefab.Contains("GainCorpse")).ToList();

        // Heart Strike: only when its minion dies; a spell: from the hero
        var yeti = OnBoard(game, op, "CS2_182");
        int from = game.PowerHistory.Full.Count;
        Play(game, me, "LEG_RLK_034", yeti);
        Assert.Empty(Gain(from));
        var croc = OnBoard(game, op, "CS2_120");
        from = game.PowerHistory.Full.Count;
        Play(game, me, "LEG_RLK_034", croc);
        Assert.Equal(me.Hero.Id, Assert.Single(Gain(from)).Source);

        // Body Bagger: a minion's battlecry, from itself
        from = game.PowerHistory.Full.Count;
        Play(game, me, "RLK_503");
        var bagger = me.BoardZone.Last();
        Assert.Equal(bagger.Id, Assert.Single(Gain(from)).Source);
    }

    [Fact]
    public void A_group_summon_spreads_from_the_right_a_single_one_goes_right()
    {
        // A group goes both sides of its summoner, the first directly right, the second directly left (2026-10-10 survey of every
        // live recording: Dr. Boom, Cenarius, Captain Crowley, Nerubian Swarmguard ... 2S1; wiki Dr. Boom: "Dr. Boom's right Boom Bot
        // spawns before the left Boom Bot"). Live Rime Sculptor (live-20261004-evening) made its left one first, as a few other
        // cards with two identical tokens do: the board looks the same. A single summon goes immediately right (wiki Summon:
        // Violet Teacher, Razorfen Hunter).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var a = OnBoard(game, me, "CS2_182"); var b = OnBoard(game, me, "CS2_182");
        int from = game.PowerHistory.Full.Count;
        Play(game, me, "LEG_RLK_752"); // lands at the right end: a, b, Sculptor
        var made = game.PowerHistory.Full.Skip(from).OfType<SabberStoneCore.Kettle.PowerHistoryFullEntity>()
            .Where(fe => fe.Entity.Name == "RLK_907t").Select(fe => fe.Entity.Id).ToList();
        Assert.Equal(2, made.Count);
        var ids = me.BoardZone.Select(m => m.Id).ToList();
        int sculptor = ids.IndexOf(me.BoardZone.Single(m => m.Card.Id == "LEG_RLK_752").Id);
        Assert.Equal(made[0], ids[sculptor + 1]); // first: right
        Assert.Equal(made[1], ids[sculptor - 1]); // second: left
        foreach (var m in me.BoardZone.ToList()) Kill(game, m);

        OnBoard(game, me, "CS2_182");
        Play(game, me, "CS2_196"); // Razorfen Hunter: one Boar, to its right
        var board = me.BoardZone.Select(m => m.Card.Id).ToList();
        Assert.Equal(new[] { "CS2_182", "CS2_196", "CS2_boar" }, board);
    }

    [Fact]
    public void Ritual_of_doom_plays_lives_sacrifice_then_summon_effects()
    {
        // Live (live-20261004-evening/session-20261004-204915.jsonl): POWER → SUB_SPELL ReuseFX_CustomDeath_Void_DarkBargain_Super
        // (Source 0, Targets = the sacrificed minion) around its TO_BE_DESTROYED, the DEATHS block, then SUB_SPELL
        // DALFX_Fel_Purple_RuneCircle_OverrideSpawn_Random (Source 0) around the 5/5 Demon.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        for (int i = 0; i < 4; i++) OnBoard(game, me, "CS2_182");
        var victim = OnBoard(game, me, "CS2_120");
        int from = game.PowerHistory.Full.Count;
        Play(game, me, "CS3_002", victim);
        var h = game.PowerHistory.Full.Skip(from).ToList();
        var subs = h.OfType<SabberStoneCore.Kettle.PowerHistorySubSpellStart>().ToList();
        Assert.Equal(new[] { "ReuseFX_CustomDeath_Void_DarkBargain_Super", "DALFX_Fel_Purple_RuneCircle_OverrideSpawn_Random" },
            subs.Select(x => x.Prefab.Split(':')[0]));
        Assert.Equal(0, subs[0].Source);
        Assert.Equal(new[] { victim.Id }, subs[0].Targets);
        Assert.Equal(0, subs[1].Source);
        int deaths = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart b && b.BlockType == BlockType.DEATHS);
        Assert.True(h.IndexOf(subs[0]) < deaths && deaths < h.IndexOf(subs[1]));
        Assert.Contains(me.BoardZone, m => m.Card.Id == "CS3_002t");
    }

    [Fact]
    public void Legacy_paladin_aura_lasts_its_turns_in_the_secret_zone()
    {
        // Paladin Aura (client PALADIN_AURA, duration QUEST_PROGRESS_TOTAL): live TIME_700 sits in the Secret zone, each end
        // of its owner's turn counts one, and it leaves at the total (live-20261003-dk-classxp).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        var raptor = OnBoard(game, me, "CS2_172"); // Bloodfen Raptor 3/2
        Play(game, me, "LEG_TTN_908");
        var aura = me.SecretZone.Single(p => p.Card.Id == "LEG_TTN_908");
        Assert.Equal(3, ((SabberStoneCore.Model.Entities.Spell)aura).QuestTotalProgress);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(me, raptor, op.Hero)));
        Assert.Equal(5, raptor.AttackDamage);
        Assert.Equal(3, raptor.BaseHealth);
        for (int turn = 1; turn <= 3; turn++)
        {
            Assert.Contains(aura, me.SecretZone);
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(game.CurrentPlayer)));
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(game.CurrentPlayer)));
        }
        Assert.DoesNotContain(aura, me.SecretZone);
        Assert.Equal(Zone.GRAVEYARD, aura.Zone.Type);
    }

    [Fact]
    public void Legacy_choice_cards_cast_the_option_picked()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;

        // Champions of Azeroth: Alliance or Horde; 2 of that side's 5 champions, each (2) cheaper
        int hand = me.HandZone.Count;
        Play(game, me, "WON_113");
        Assert.Equal(new[] { "WON_113a", "WON_113b" }, me.Choice.Choices.Select(id => game.IdEntityDic[id].Card.Id));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, me.Choice.Choices[1])));
        var champions = me.HandZone.Skip(hand).ToList();
        Assert.Equal(2, champions.Count);
        var horde = new[] { "EX1_414", "EX1_016", "EX1_110", "RLK_593", "BAR_080" };
        Assert.All(champions, p => Assert.Contains(p.Card.Id, horde));
        Assert.All(champions, p => Assert.Equal(p.Card.Cost - 2, p.Cost));

        // Bob the Bartender: four Battlegrounds actions; Find a Triple draws a minion and gets 2 copies
        me.UsedMana = 0;
        Play(game, me, "BG31_BOB");
        Assert.Equal(4, me.Choice.Choices.Count);
        hand = me.HandZone.Count;
        var triple = me.Choice.Choices.Single(id => game.IdEntityDic[id].Card.Id == "BG31_BOBt4");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, triple)));
        Assert.Equal(hand + 3, me.HandZone.Count);
        var got = me.HandZone.Skip(hand).Select(p => p.Card.Id).Distinct().ToList();
        Assert.Single(got);
    }

    [Fact]
    public void A_missile_card_fires_one_missile_at_each_character_it_hits()
    {
        // Live: Fel Barrage (live-20261005) = two missile SUB_SPELLs from the hero, each with its own TARGET; user (2026-10-05):
        // Noxious Cadaver's missile flies at the enemy AND at its own hero. Ours: one missile at the chosen target / none.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        var yeti = OnBoard(game, op, "CS2_182");
        List<(string Prefab, int Source, List<int> Targets, List<int> Named)> Missiles(SabberStoneCore.Model.Entities.IPlayable card)
        {
            var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
            var list = new List<(string, int, List<int>, List<int>)>();
            int from = sent.FindIndex(d => d.PowerStart is { } p && p.Source == card.Id && p.Type == HistoryBlock.Type.POWER);
            for (int i = from, depth = 0; i < sent.Count; i++)
            {
                if (sent[i].PowerStart != null) depth++;
                if (sent[i].PowerEnd != null && --depth == 0) break;
                if (sent[i].SubSpellStart is { } sub && sub.SpellPrefabGuid.Contains("Missile"))
                    list.Add((sub.SpellPrefabGuid, sub.SourceEntityId, sub.TargetEntityIds.ToList(),
                        sent.Skip(i + 1).Take(3).FirstOrDefault(d => d.MetaData is { Type: HistoryMeta.Type.TARGET })?.MetaData.Info.ToList() ?? new List<int>()));
            }
            return list;
        }
        var cadaver = Give(me, "LEG_RLK_079");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.MinionTarget(me, cadaver, yeti)));
        var shots = Missiles(cadaver);
        Assert.Equal(2, shots.Count);
        Assert.Contains(shots, m => m.Targets.Contains(yeti.Id));
        Assert.Contains(shots, m => m.Targets.Contains(me.Hero.Id));

        me.UsedMana = 0;
        var barrage = Give(me, "SW_040");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, barrage)));
        shots = Missiles(barrage);
        Assert.Equal(2, shots.Count);
        Assert.All(shots, m => Assert.Equal(me.Hero.Id, m.Source));
        Assert.All(shots, m => Assert.Single(m.Named));
    }

    [Fact]
    public void An_impact_only_effect_names_what_it_acted_on()
    {
        // User (2026-10-05): Lone Champion's gain of Taunt + Divine Shield showed no animation. Its client effect is
        // Holy_Targeted_Healing_ImpactOnly_FX (an impact on a target); live TRL_071, same effect, a minion buffing itself, opens
        // with TARGET = itself. A BuffImpact Hero Power buffing the hero gets none (live Demon Hunter 8/8).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        foreach (var m in me.BoardZone.ToList()) Kill(game, m);
        var champion = Give(me, "LOOT_124");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, champion)));
        Assert.True(((SabberStoneCore.Model.Entities.Minion)champion).HasTaunt);
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        int power = sent.FindIndex(d => d.PowerStart is { } p && p.Source == champion.Id && p.Type == HistoryBlock.Type.POWER);
        Assert.Equal(new List<int> { champion.Id }, sent[power + 1].MetaData?.Info);
    }

    [Fact]
    public void An_equal_targeted_effect_names_every_character_it_hits()
    {
        // User (2026-10-05): Cone of Cold is one cone over the three minions (1 damage + Freeze each): not a split effect, so
        // its TARGET names all of them (only a split one — the target treated differently — names the target alone).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        var left = OnBoard(game, op, "CS2_182"); var mid = OnBoard(game, op, "CS2_200"); var right = OnBoard(game, op, "CS2_182");
        var cone = Give(me, "EX1_275");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, cone, mid)));
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        int power = sent.FindIndex(d => d.PowerStart is { } p && p.Source == cone.Id && p.Type == HistoryBlock.Type.POWER);
        var named = sent.Skip(power).First(d => d.MetaData is { Type: HistoryMeta.Type.TARGET }).MetaData.Info;
        Assert.Equal(new[] { left.Id, mid.Id, right.Id }.OrderBy(x => x), named.OrderBy(x => x));
    }

    [Fact]
    public void An_attack_secret_resolves_before_the_swing_and_a_cancelled_attack_never_swings()
    {
        // Live (live-20261005: Novice Engineer into Explosive Trap; Oasis Ally): PROPOSED_ATTACKER / DEFENDER, then the Secret's
        // TRIGGER block, and only then ATTACKING / SLUSH_TIME 1200 / DEFENDING (the swing). A cancelled attack (the attacker
        // died) sends no ATTACKING at all, but the attack is used (NUM_ATTACKS_THIS_TURN 1). Rule book: the Combat
        // Preparation Phase (Proposed Attack Event, Attack Event) before the Combat Phase. User: "Whenever" before, "After" after.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        op.UsedMana = 0;
        var trap = Give(op, "EX1_610");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(op, trap)));
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(op));
        var boar = (SabberStoneCore.Model.Entities.Minion)Give(me, "CS2_171"); // 1/1 Charge: dies to the Trap
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, boar)));
        int from = game.PowerHistory.Full.Count;
        game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(me, boar, op.Hero));
        var after = game.PowerHistory.Full.Skip(from).ToList();
        int secret = after.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart b && b.Source == trap.Id);
        Assert.True(secret > 0);
        Assert.DoesNotContain(after, e => e is SabberStoneCore.Kettle.PowerHistoryTagChange t && t.EntityId == boar.Id && t.Tag == GameTag.ATTACKING && t.Value == 1);
        Assert.Equal(1, boar.NumAttacksThisTurn);

        // an attack that goes on swings after the Secret: Kor'kron Elite survives the second Trap
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        op.UsedMana = 0;
        var trap2 = Give(op, "EX1_610");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(op, trap2)));
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(op));
        me.UsedMana = 0;
        var elite = (SabberStoneCore.Model.Entities.Minion)Give(me, "NEW1_011");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, elite)));
        from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(me, elite, op.Hero)));
        after = game.PowerHistory.Full.Skip(from).ToList();
        int secret2 = after.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart b && b.Source == trap2.Id);
        int swing = after.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryTagChange t && t.EntityId == elite.Id && t.Tag == GameTag.ATTACKING && t.Value == 1);
        Assert.True(secret2 > 0 && swing > secret2);
    }

    [Theory]
    // "When" a card is played / a spell is cast: before its effect (live: inside the PLAY block, before its POWER block)
    [InlineData("EX1_287", "spell", "before")]  // Counterspell
    [InlineData("tt_010", "spell", "before")]   // Spellbender
    [InlineData("UNG_024", "spell", "before")]  // Mana Bind
    [InlineData("MAW_006", "minion", "before")] // Objection!
    // "After": once it resolved (live: after its POWER block, still in the PLAY block)
    [InlineData("KAR_004", "spell", "after")]   // Cat Trick
    [InlineData("EX1_294", "minion", "after")]  // Mirror Entity
    [InlineData("EX1_609", "minion", "after")]  // Snipe
    [InlineData("CFM_620", "minion", "after")]  // Potion of Polymorph
    [InlineData("LOOT_101", "minion", "after")] // Explosive Runes
    // "When your hero takes damage": at the damage, inside the effect
    [InlineData("EX1_132", "face", "inside")]   // Eye for an Eye
    // "When … attacks / is attacked": before the swing (live: before ATTACKING)
    [InlineData("EX1_610", "attack-hero", "before")]   // Explosive Trap
    [InlineData("EX1_611", "attack-hero", "before")]   // Freezing Trap
    [InlineData("EX1_533", "attack-hero", "before")]   // Misdirection
    [InlineData("EX1_594", "attack-hero", "before")]   // Vaporize
    [InlineData("EX1_289", "attack-hero", "before")]   // Ice Barrier
    [InlineData("EX1_554", "attack-minion", "before")] // Snake Trap
    [InlineData("BAR_812", "attack-minion", "before")] // Oasis Ally
    [InlineData("ICC_200", "attack-minion", "before")] // Venomstrike Trap
    [InlineData("TRL_400", "attack-minion", "before")] // Splitting Image
    public void A_secret_fires_where_live_puts_it_when_before_after_after(string secretId, string action, string where)
    {
        // Live survey (2026-10-05, 62 sessions, every SECRET trigger): "When" secrets resolve before the action ("When your
        // opponent casts a spell": before the spell's POWER block; "When … is attacked": before ATTACKING), "After" secrets once
        // it resolved (after the POWER block, inside the PLAY block). User: "the wording WHENEVER happens before the action
        // resolves ie spell or attack, AFTER mean the effect trigger after". Rule book: Combat Preparation before the Combat Phase.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        op.UsedMana = 0;
        var secret = Give(op, secretId);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(op, secret)));
        var guard = OnBoard(game, op, "CS2_182");
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(op));
        me.UsedMana = 0;
        int from = game.PowerHistory.Full.Count;
        SabberStoneCore.Model.Entities.IPlayable actor;
        switch (action)
        {
            case "spell":
                actor = Give(me, "CS2_008"); // Moonfire at the enemy minion
                Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, actor, guard)));
                break;
            case "face":
                actor = Give(me, "CS2_008"); // Moonfire at the enemy hero
                Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, actor, op.Hero)));
                break;
            case "minion":
                actor = Give(me, "CS2_200"); // Boulderfist Ogre
                Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, actor)));
                break;
            default:
                actor = Give(me, "NEW1_011"); // Kor'kron Elite (Charge)
                Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, actor)));
                from = game.PowerHistory.Full.Count;
                game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(me, (SabberStoneCore.Model.Entities.ICharacter)actor,
                    action == "attack-hero" ? op.Hero : guard));
                break;
        }
        var h = game.PowerHistory.Full.Skip(from).ToList();
        int fired = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart b && b.Source == secret.Id && b.BlockType == BlockType.TRIGGER);
        Assert.True(fired >= 0, $"{secretId} never fired");
        string got;
        if (action.StartsWith("attack"))
        {
            int swing = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryTagChange t && t.Tag == GameTag.ATTACKING && t.Value == 1);
            got = swing < 0 || fired < swing ? "before" : "after";
        }
        else
        {
            int power = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart b && b.Source == actor.Id && b.BlockType == BlockType.POWER);
            int end = -1;
            if (power >= 0)
                for (int i = power + 1, depth = 1; i < h.Count; i++)
                {
                    if (h[i] is SabberStoneCore.Kettle.PowerHistoryBlockStart) depth++;
                    else if (h[i] is SabberStoneCore.Kettle.PowerHistoryBlockEnd && --depth == 0) { end = i; break; }
                }
            got = power < 0 || fired < power ? "before" : end < 0 || fired < end ? "inside" : "after";
        }
        Assert.Equal(where, got);
    }

    [Theory]
    [InlineData("CS2_182", "P0")]      // Chillwind Yeti: no effect, an empty POWER block
    [InlineData("EX1_250", "P0*")]     // Earth Elemental: no effect, its Overload inside its only POWER block
    [InlineData("EX1_238", "P0 P1*")]  // Lightning Bolt: the effect, then the Overload in a block of its own
    [InlineData("EX1_247", "P0*")]     // Stormforged Axe: a weapon with no effect, as Earth Elemental
    public void A_played_card_always_opens_its_power_block_and_overloads_after_its_effect(string id, string shape)
    {
        // Live (2026-10-05, every capture): 1787 of 1789 minion plays open a POWER block (the 2 without were countered), empty
        // with no Battlecry; Overload after the effect (P0 P1: 144 of 144), or inside P0 when there is no effect (27 of 27).
        // Ours sent no block for a plain minion, so "After your opponent plays a minion" Secrets had no effect to follow.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var card = Give(me, id);
        int from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(card is SabberStoneCore.Model.Entities.Spell
            ? SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, card, me.Opponent.Hero)
            : SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, card)));
        var h = game.PowerHistory.Full.Skip(from).ToList();
        var got = new List<string>();
        for (int i = 0; i < h.Count; i++)
        {
            if (h[i] is not SabberStoneCore.Kettle.PowerHistoryBlockStart b || b.Source != card.Id || b.BlockType != BlockType.POWER) continue;
            bool overload = false;
            for (int j = i + 1, depth = 1; j < h.Count && depth > 0; j++)
            {
                if (h[j] is SabberStoneCore.Kettle.PowerHistoryBlockStart) depth++;
                else if (h[j] is SabberStoneCore.Kettle.PowerHistoryBlockEnd) depth--;
                else if (depth == 1 && h[j] is SabberStoneCore.Kettle.PowerHistoryTagChange t && t.Tag == GameTag.OVERLOAD_OWED) overload = true;
            }
            got.Add("P" + b.Index + (overload ? "*" : ""));
        }
        Assert.Equal(shape, string.Join(" ", got));
    }

    [Fact]
    public void A_player_attack_targets_first_and_ends_with_its_option_and_a_pause()
    {
        // Live (2026-10-05, 3104 of 3104 attacks; live-20261005 164158 Explosive Trap): CARD_TARGET on the attacker before the
        // ATTACK block; after the block and its deaths NUM_OPTIONS_PLAYED_THIS_TURN, then SLUSH_TIME 1000 on the attacker (the
        // client lets the swing finish). Ours had neither, and counted the option inside the block.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        var grizzly = OnBoard(game, me, "CS2_125");
        var wisp = OnBoard(game, op, "CS2_231");
        int from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(me, grizzly, wisp)));
        var h = game.PowerHistory.Full.Skip(from).ToList();
        int target = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryTagChange t && t.EntityId == grizzly.Id && t.Tag == GameTag.CARD_TARGET && t.Value == wisp.Id);
        int attack = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart b && b.BlockType == BlockType.ATTACK);
        int deaths = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart b && b.BlockType == BlockType.DEATHS);
        int options = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryTagChange t && t.Tag == GameTag.NUM_OPTIONS_PLAYED_THIS_TURN);
        int pause = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryMetaData m && m.Type == MetaDataType.SLUSH_TIME && m.Data == 1000 && m.Info.Contains(grizzly.Id));
        Assert.True(target >= 0 && target < attack, "CARD_TARGET before the ATTACK block");
        Assert.True(deaths > attack && options > deaths && pause > options, $"deaths {deaths}, options {options}, pause {pause}");
        Assert.Equal(1, me.NumOptionsPlayedThisTurn);
    }

    [Fact]
    public void Echoing_ooze_shows_its_slime_tag_and_copies_in_a_keyworded_trigger()
    {
        // Live (live-20261005 140614): the Battlecry sets ECHOING_OOZE_SPELL (963) on it (the client's slime); at the end of the
        // turn its TRIGGER block (TriggerKeyword ECHOING_OOZE_SPELL, effect index -1) summons the copy, then the tag goes to 0.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var ooze = Give(me, "FP1_003");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, ooze)));
        Assert.Equal(1, ooze[GameTag.ECHOING_OOZE_SPELL]);
        int from = game.PowerHistory.Full.Count;
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        var block = game.PowerHistory.Full.Skip(from).OfType<SabberStoneCore.Kettle.PowerHistoryBlockStart>()
            .Single(b => b.Source == ooze.Id && b.BlockType == BlockType.TRIGGER);
        Assert.Equal((int)GameTag.ECHOING_OOZE_SPELL, block.TriggerKeyword);
        Assert.Equal(-1, block.Index);
        Assert.Equal(0, ooze[GameTag.ECHOING_OOZE_SPELL]);
        Assert.Equal(2, me.BoardZone.Count(m => m.Card.Id == "FP1_003"));
        Assert.All(me.BoardZone.Where(m => m.Card.Id == "FP1_003"), m => Assert.Equal(0, m[GameTag.ECHOING_OOZE_SPELL]));
    }

    [Fact]
    public void An_enemy_secret_is_revealed_before_its_trigger_block()
    {
        // Live (enemy Snipe, live-20261004-night): SHOW_ENTITY of the hidden Secret right before its TRIGGER block. Ours revealed
        // it only afterwards (to the graveyard): Explosive Trap played no effect, Freezing Trap's attack went through visually.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        op.UsedMana = 0;
        var trap = Give(op, "EX1_610"); // Explosive Trap
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(op, trap)));
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(op));
        var boar = (SabberStoneCore.Model.Entities.Minion)Give(me, "NEW1_011"); // Kor'kron Elite: Charge, survives the Trap
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, boar)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(me, boar, op.Hero)));
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        int trigger = sent.FindIndex(d => d.PowerStart is { } p && p.Source == trap.Id && p.Type == HistoryBlock.Type.TRIGGER);
        Assert.True(trigger > 0);
        Assert.Equal(trap.Id, sent[trigger - 1].ShowEntity?.Entity);
        Assert.Equal("EX1_610", sent[trigger - 1].ShowEntity.Name);
    }

    [Theory]
    [InlineData("RLK_015", true)]  // Howling Blast: 3 + Freeze, 1 to all other enemies in its frost shockwave
    [InlineData("EX1_537", false)] // Explosive Shot: 5, 2 to the neighbours (its client effect hits them itself)
    public void A_targeted_spell_names_only_its_target_and_its_splash_plays_in_its_area_effect(string id, bool splashFx)
    {
        // Live (live-20261005: Explosive Shot, Howling Blast; Obliterate): the block's TARGET is only the chosen target, also
        // when the spell hits others; Howling Blast's splash plays inside ReuseFX_Frost_DeathKnight_AE_Shockwave_Small from
        // the hero, with its own TARGET for what it hits. Ours named every character it damaged (the big effect on all).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        var left = OnBoard(game, op, "CS2_182"); var mid = OnBoard(game, op, "CS2_200"); var right = OnBoard(game, op, "CS2_182");
        var card = Give(me, id);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, card, mid)));
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        int power = sent.FindIndex(d => d.PowerStart is { } p && p.Source == card.Id && p.Type == HistoryBlock.Type.POWER);
        Assert.Equal(new List<int> { mid.Id }, sent[power + 1].MetaData?.Info);
        int sub = sent.FindIndex(power, d => d.SubSpellStart != null && d.SubSpellStart.SpellPrefabGuid.Contains("Shockwave"));
        if (!splashFx) { Assert.Equal(-1, sub); return; }
        Assert.Equal(me.Hero.Id, sent[sub].SubSpellStart.SourceEntityId);
        Assert.Contains(left.Id, sent[sub + 1].MetaData?.Info ?? new List<int>());
        Assert.DoesNotContain(mid.Id, sent[sub + 1].MetaData.Info);
    }

    [Fact]
    public void Bob_the_bartenders_actions_play_their_live_effects_and_recruit_is_aimed()
    {
        // Live (live-20261005, all four actions): every effect plays from Bob, each its own: Freeze the Shop = a Frost Nova
        // shockwave; Recruit a Minion = the player aims at an enemy minion (TARGET choice), a Sap missile at it, the copy added,
        // the opponent's Coins with the gold-coins effect; Find a Triple = the copies with the goldenize effect.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        var full = game.PowerHistory.Full;
        List<SabberStoneCore.Kettle.PowerHistorySubSpellStart> Fx(int from) => full.Skip(from).OfType<SabberStoneCore.Kettle.PowerHistorySubSpellStart>().ToList();
        OnBoard(game, op, "CS2_182");
        var target = OnBoard(game, op, "CS2_200");
        foreach (var p in me.HandZone.ToArray()) me.HandZone.Remove(p);
        foreach (var p in op.HandZone.ToArray()) op.HandZone.Remove(p);

        SabberStoneCore.Model.Entities.IPlayable Choose(string action)
        {
            me.UsedMana = 0;
            var bob = Give(me, "BG31_BOB");
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, bob)));
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, me.Choice.Choices.Single(id => game.IdEntityDic[id].Card.Id == action))));
            return bob;
        }

        int from = full.Count;
        var bob1 = Choose("BG31_BOBt");
        var nova = Assert.Single(Fx(from), x => x.Prefab.Contains("FrostNova"));
        Assert.Equal(bob1.Id, nova.Source);
        Assert.True(op.BoardZone.All(m => m.IsFrozen));

        from = full.Count;
        var bob2 = Choose("BG31_BOBt2");
        Assert.NotNull(me.Choice);
        Assert.Equal(ChoiceType.TARGET, me.Choice.ChoiceType);
        Assert.All(me.Choice.Choices, id => Assert.Equal(op, game.IdEntityDic[id].Controller));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, target.Id)));
        Assert.Contains(me.HandZone, p => p.Card.Id == "CS2_200");
        Assert.Equal(3, op.HandZone.Count(p => p.Card.Id == "GAME_005"));
        var sap = Assert.Single(Fx(from), x => x.Prefab.Contains("Sap"));
        Assert.Equal(new[] { target.Id }, sap.Targets);
        Assert.Contains(Fx(from), x => x.Prefab.Contains("GoldCoins") && x.Source == bob2.Id);

        from = full.Count;
        OnDeckTop(me, "CS2_182");
        Choose("BG31_BOBt4");
        Assert.Contains(Fx(from), x => x.Prefab.Contains("Goldenize"));
    }

    [Theory]
    [InlineData("CORE_EX1_082", true)] // Mad Bomber: 3 damage randomly split (a minion's battlecry)
    [InlineData("EX1_277", false)]     // Arcane Missiles: 3 random 1-damage missiles (a spell)
    public void A_repeated_random_hit_names_its_own_target_each_time_like_live(string id, bool minion)
    {
        // Live (Arcane Missiles, live-20261002-progression Power.log 4610+): one META_DATA TARGET per missile, right before
        // its damage, even for the same target again; the client flies one projectile per TARGET. Ours sent one TARGET for
        // all hits, so the first impact landed every hit at once (user: Mad Bomber's damage didn't spread).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "CS2_182"))));
        int from = game.PowerHistory.Full.Count;
        var card = Give(me, id);
        Assert.True(game.Process(minion ? SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, card)
            : SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, card)));
        var h = game.PowerHistory.Full.Skip(from).ToList();
        var rounds = h.OfType<SabberStoneCore.Kettle.PowerHistoryTargetRound>().Where(r => r.Info.Count > 0).ToList();
        var hits = h.OfType<SabberStoneCore.Kettle.PowerHistoryMetaData>()
            .Where(m => m.Type == SabberStoneCore.Enums.MetaDataType.DAMAGE && m is not SabberStoneCore.Kettle.PowerHistoryTargetRound).ToList();
        Assert.Equal(3, hits.Count);
        Assert.Equal(3, rounds.Count);
        // each hit comes after its own TARGET (and before the next), naming what it hits
        for (int i = 0; i < 3; i++)
        {
            int round = h.IndexOf(rounds[i]), hit = h.IndexOf(hits[i]);
            Assert.True(round < hit && (i == 2 || hit < h.IndexOf(rounds[i + 1])), $"hit {i} isn't inside its round");
            Assert.Equal(hits[i].Info, rounds[i].Info);
        }
        // the block itself no longer opens with a TARGET for all of them
        var block = h.OfType<SabberStoneCore.Kettle.PowerHistoryBlockStart>().First(b => b.BlockType == SabberStoneCore.Enums.BlockType.POWER);
        Assert.True(block.EffectTargets == null || block.EffectTargets.Count == 0);
    }

    [Fact]
    public void An_area_effect_still_names_all_its_targets_once()
    {
        // Flamestrike hits everything at once: one TARGET for the block (no rounds).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        var op = game.CurrentPlayer;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(op, Give(op, "CS2_182"))));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(op, Give(op, "CS2_182"))));
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(op));
        me.UsedMana = 0;
        int from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "CS2_032"))));
        var h = game.PowerHistory.Full.Skip(from).ToList();
        Assert.Empty(h.OfType<SabberStoneCore.Kettle.PowerHistoryTargetRound>());
        var block = h.OfType<SabberStoneCore.Kettle.PowerHistoryBlockStart>().First(b => b.BlockType == SabberStoneCore.Enums.BlockType.POWER);
        Assert.Equal(2, block.EffectTargets?.Count ?? 0);
    }

    [Fact]
    public void Every_corpse_spender_plays_lives_corpse_effect_from_the_right_place()
    {
        // User (2026-10-03): Corpse cards without a recording looked generic (no effect at the spend); follow Grave Strength's
        // live pattern for all of them. Live: a spell from its hero at the hero, a minion from itself (at nothing in an
        // end-of-turn trigger: Battlefield Necromancer); Rite of Atrocity / Lord Marrowgar spend with none.
        const string Generic = "ReuseFX_Necrotic_DeathKnight_AE_ConsumeCorpse_Super:";
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.GainCorpses(30);
        List<SabberStoneCore.Kettle.PowerHistorySubSpellStart> Corpse(int from) => game.PowerHistory.Full.Skip(from)
            .OfType<SabberStoneCore.Kettle.PowerHistorySubSpellStart>().Where(x => x.Prefab.StartsWith(Generic)).ToList();

        // Soul Searching (no CardDef visuals, no recording): from the hero, at the hero, around the spend
        int from = game.PowerHistory.Full.Count;
        var soul = Give(me, "WORK_070");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, soul)));
        if (game.CurrentPlayer.Choice != null)
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, me.Choice.Choices[0])));
        var fx = Assert.Single(Corpse(from));
        Assert.Equal(me.Hero.Id, fx.Source);
        Assert.Equal(new[] { me.Hero.Id }, fx.Targets);

        // Malignant Horror's end-of-turn spend: from itself, at nothing
        me.UsedMana = 0;
        var horror = Give(me, "CORE_RLK_745");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, horror)));
        from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        fx = Assert.Single(Corpse(from));
        Assert.Equal(horror.Id, fx.Source);
        Assert.Empty(fx.Targets);

        // Rite of Atrocity: live sends none
        Assert.Null(SabberStoneCore.Loader.SubSpells.CorpseEffect(SabberStoneCore.Model.Cards.FromId("EDR_811")));
        // a Legacy reprint finds its original's recording (Ymirjar Deathbringer)
        Assert.NotNull(SabberStoneCore.Loader.SubSpells.CorpseEffect(SabberStoneCore.Model.Cards.FromId("LEG_RLK_226")));
        // a card that doesn't spend Corpses gets none
        Assert.Null(SabberStoneCore.Loader.SubSpells.CorpseEffect(SabberStoneCore.Model.Cards.FromId("CS2_182")));
    }

    [Theory]
    [InlineData("EX1_323")]      // Legacy
    [InlineData("CORE_EX1_323")] // Core
    public void Lord_jaraxxus_is_a_hero_card_that_keeps_your_health_and_equips_blood_fury(string id)
    {
        // Rule book "Playing a Hero card": "your hero is replaced by a Hero with the same current Health and maximum Health and
        // gain Armor displayed on the card"; then the Battlecry (equip a 3/8 Blood Fury); the Hero Power becomes INFERNO!.
        // User (live-like on our Core one): 17 Health stayed, +5 Armor. The Legacy script still swapped in the old 15-Health hero.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.Hero.Damage = 13;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, Give(me, id))));
        Assert.Equal(id, me.Hero.Card.Id);
        Assert.Equal(17, me.Hero.Health);
        Assert.Equal(5, me.Hero.Armor);
        Assert.Equal("EX1_323w", me.Hero.Weapon?.Card.Id);
        Assert.Equal("EX1_tk33", me.Hero.HeroPower.Card.Id);
    }

    [Fact]
    public void Natalie_and_leeroy_play_their_own_effects()
    {
        // Legacy set's only own effects we never sent (audit): Natalie Seline's Destroy (from her to the minion) and Absorb (from
        // it back into her), live: Leeroy Jenkins sends none. Roles Destroy / Absorb, shared.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        var op = game.CurrentPlayer;
        var victim = (SabberStoneCore.Model.Entities.Minion)Give(op, "CS2_182");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(op, victim)));
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(op));
        me.UsedMana = 0;

        int from = game.PowerHistory.Full.Count;
        var natalie = (SabberStoneCore.Model.Entities.Minion)Give(me, "CORE_EX1_198");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, natalie, victim)));
        Assert.True(victim.IsDead || victim.Zone?.Type != SabberStoneCore.Enums.Zone.PLAY);
        var subs = game.PowerHistory.Full.Skip(from).OfType<SabberStoneCore.Kettle.PowerHistorySubSpellStart>().ToList();
        var destroy = Assert.Single(subs, x => x.Prefab.Contains("NatalieSeline_Destroy"));
        var absorb = Assert.Single(subs, x => x.Prefab.Contains("NatalieSeline_Absorb"));
        Assert.Equal(natalie.Id, destroy.Source);
        Assert.Equal(new[] { victim.Id }, destroy.Targets);
        Assert.Equal(victim.Id, absorb.Source);
        Assert.Equal(new[] { natalie.Id }, absorb.Targets);
        Assert.True(subs.IndexOf(destroy) < subs.IndexOf(absorb));

        me.UsedMana = 0;
        from = game.PowerHistory.Full.Count;
        var leeroy = Give(me, "EX1_116");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, leeroy)));
        // live (session-20261004-122914): Leeroy's battlecry sends no SUB_SPELL (his CardDef's Shout prefab isn't used)
        Assert.DoesNotContain(game.PowerHistory.Full.Skip(from).OfType<SabberStoneCore.Kettle.PowerHistorySubSpellStart>(),
            x => x.Prefab.Contains("Physical_Shout"));
        Assert.Equal(2, op.BoardZone.Count(m => m.Card.Id == "EX1_116t"));
    }

    [Fact]
    public void Legacy_scripts_follow_their_current_text_and_the_rule_book()
    {
        // The Legacy pass (RESEARCH/engine-audit/legacy-pass-2026-10-04.md): shared rules and scripts fixed against the
        // current card text and the rule book.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        bool Play(SabberStoneCore.Model.Entities.Controller c, string id, SabberStoneCore.Model.Entities.ICharacter target = null)
        {
            c.UsedMana = 0;
            var card = Give(c, id);
            return game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(c, card, target, 0));
        }
        SabberStoneCore.Model.Entities.Minion Put(SabberStoneCore.Model.Entities.Controller c, string id)
        {
            var m = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(c, SabberStoneCore.Model.Cards.FromId(id));
            c.BoardZone.Add(m);
            return m;
        }
        void Clear() { foreach (var m in me.BoardZone.GetAll().Concat(op.BoardZone.GetAll()).ToList()) m.Zone.Remove(m); }

        // Spell Damage (rule book Spell_Damage: "increases the damage dealt by any spell"): the client's "$" decides it,
        // not whether a script asked — Eviscerate 2 + 1
        Put(me, "CS2_142"); // Kobold Geomancer, Spell Damage +1
        var yeti = Put(op, "CS2_182");
        Assert.True(Play(me, "EX1_124", yeti));
        Assert.Equal(3, yeti.Damage);
        // ... and a missile spell gets it as one more missile, not +1 on each (Arcane Missiles: 3 + 1 hits of 1)
        Clear();
        Put(me, "CS2_142");
        int heroHealth = op.Hero.Health;
        Assert.True(Play(me, "EX1_277"));
        Assert.Equal(heroHealth - 4, op.Hero.Health);

        // Betrayal: the minion deals the damage (rule book x_Damage) — no Spell Damage on it
        Clear();
        Put(me, "CS2_142");
        var left = Put(op, "CS2_200");
        var betrayed = Put(op, "CS2_182");
        var right = Put(op, "CS2_200");
        Assert.True(Play(me, "EX1_126", betrayed));
        Assert.Equal(4, left.Damage);
        Assert.Equal(4, right.Damage);

        // "other": Deathwing stays; Coldlight Seer doesn't buff itself
        Clear();
        Put(me, "CS2_182");
        Put(op, "CS2_182");
        Assert.True(Play(me, "NEW1_030"));
        Assert.Equal("NEW1_030", Assert.Single(me.BoardZone.GetAll()).Card.Id);
        Assert.Empty(op.BoardZone.GetAll());
        Clear();
        var raider = Put(me, "CS2_168");
        Assert.True(Play(me, "EX1_103"));
        Assert.Equal(3, raider.Health);
        Assert.Equal(3, me.BoardZone.GetAll().Single(m => m.Card.Id == "EX1_103").Health);

        // Preparation: (2) less; Sorcerer's Apprentice shows its EX1_608e on the spell in hand (live)
        Clear();
        var fireball = Give(me, "CS2_029");
        Assert.True(Play(me, "EX1_145"));
        Assert.Equal(2, fireball.Cost);
        Assert.Contains(fireball.AppliedEnchantments, e => e.Card.Id == "EX1_145e");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, fireball, op.Hero, 0)));
        var frostbolt = Give(me, "CS2_024");
        Assert.True(Play(me, "EX1_608"));
        Assert.Contains(frostbolt.AppliedEnchantments, e => e.Card.Id == "EX1_608e");

        // Twisting Nether: minions and locations
        Clear();
        Put(op, "CS2_182");
        me.UsedMana = 0;
        var location = Give(me, "CORE_REV_290");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, location, null, 0)));
        Assert.True(Play(me, "EX1_312"));
        Assert.Empty(me.BoardZone.GetAll());
        Assert.Equal(0, me.BoardZone.Count);
        Assert.Empty(op.BoardZone.GetAll());

        // Pilfer: a card from another class (not the opponent's)
        int hand = me.HandZone.Count;
        Assert.True(Play(me, "EX1_182"));
        var stolen = me.HandZone[me.HandZone.Count - 1];
        Assert.Equal(hand + 1, me.HandZone.Count);
        Assert.NotEqual(me.HeroClass, stolen.Card.Class);
        Assert.NotEqual(CardClass.NEUTRAL, stolen.Card.Class);

        // Unbound Elemental: only after its owner's Overload card
        Clear();
        var unbound = Put(me, "EX1_258");
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        Assert.True(Play(op, "EX1_238", me.Hero));
        Assert.Equal(unbound.Card.ATK, unbound.AttackDamage); // not grown by the opponent's Overload card
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(op));

        // Natalie Seline: the Health comes as her EX1_198e enchantment (live): Silence takes it away
        Clear();
        var meal = Put(op, "CS2_182");
        Assert.True(Play(me, "CORE_EX1_198", meal));
        var natalie = me.BoardZone.GetAll().Single(m => m.Card.Id == "CORE_EX1_198");
        Assert.Equal(6, natalie.Health);
        Assert.Contains(natalie.AppliedEnchantments, e => e.Card.Id == "EX1_198e");
        Assert.True(Play(me, "EX1_332", natalie));
        Assert.Equal(1, natalie.Health);

        // Scarlet Subjugator: -2 Attack until the caster's next turn
        Clear();
        var subjugated = Put(op, "CS2_182");
        Assert.True(Play(me, "EX1_196", subjugated));
        Assert.Equal(2, subjugated.AttackDamage);
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        Assert.Equal(2, subjugated.AttackDamage); // still on the opponent's turn
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(op));
        Assert.Equal(4, subjugated.AttackDamage);

        // Demon Hunter Initiate: Inner Demon, Satyr Overseer, Glaivebound Adept (target only after the hero attacked), Soul Cleave
        Clear();
        Put(me, "BT_352");
        var adept = Give(me, "BT_495");
        Assert.False(((SabberStoneCore.Model.Entities.Playable)adept).HasAnyValidPlayTargets && SabberStoneCore.Model.Entities.Playable.ConditionalTargetReady((SabberStoneCore.Model.Entities.Playable)adept));
        Assert.True(Play(me, "BT_512"));
        Assert.Equal(8, me.Hero.AttackDamage);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.HeroAttackTask.Any(me, op.Hero)));
        Assert.Contains(me.BoardZone.GetAll(), m => m.Card.Id == "BT_352t");
        var target = Put(op, "CS2_200");
        Assert.True(SabberStoneCore.Model.Entities.Playable.ConditionalTargetReady((SabberStoneCore.Model.Entities.Playable)adept));
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, adept, target, 0)));
        Assert.Equal(4, target.Damage);
        var a = Put(op, "CS2_200");
        Assert.True(Play(me, "BT_740"));
        Assert.Equal(2, a.Damage);
    }

    [Fact]
    public void User_rulings_mind_control_tech_summon_sides_and_fallbacks()
    {
        // User rulings 2026-10-04: no "random" = the player picks; a minion's several summons spread to both sides of it,
        // resurrects line up to its right; Sense Demons / Mindgames have their real fallbacks.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        SabberStoneCore.Model.Entities.Minion Put(SabberStoneCore.Model.Entities.Controller c, string id)
        {
            var m = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(c, SabberStoneCore.Model.Cards.FromId(id));
            c.BoardZone.Add(m);
            return m;
        }
        string Board(SabberStoneCore.Model.Entities.Controller c) => string.Join(",", c.BoardZone.GetAll().Select(m => m.Card.Id));

        // Mind Control Tech: a target only with 4+ enemy minions; the chosen one is taken
        var tech = (SabberStoneCore.Model.Entities.Playable)Give(me, "EX1_085");
        for (int i = 0; i < 3; i++) Put(op, "CS2_168");
        Assert.False(SabberStoneCore.Model.Entities.Playable.ConditionalTargetReady(tech));
        var wanted = Put(op, "CS2_182");
        Assert.True(SabberStoneCore.Model.Entities.Playable.ConditionalTargetReady(tech));
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, tech, wanted, 0)));
        Assert.Equal(me, wanted.Controller);

        // Cenarius' two Treants: one on each side of him
        foreach (var m in me.BoardZone.GetAll().Concat(op.BoardZone.GetAll()).ToList()) m.Zone.Remove(m);
        Put(me, "CS2_168");
        me.UsedMana = 0;
        var cenarius = Give(me, "EX1_573");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, cenarius, null, 1, 2)));
        Assert.Equal("CS2_168,EX1_573t,EX1_573,EX1_573t", Board(me));
        // Dr. Boom, Sindragosa (user: one on each side) and Onyxia (three on each side)
        foreach (var (card, token, expected) in new[] { ("GVG_110", "GVG_110t", "GVG_110t,GVG_110,GVG_110t"),
                     ("ICC_838", "ICC_838t", "ICC_838t,ICC_838,ICC_838t"),
                     ("EX1_562", "EX1_116t", "EX1_116t,EX1_116t,EX1_116t,EX1_562,EX1_116t,EX1_116t,EX1_116t") })
        {
            foreach (var m in me.BoardZone.GetAll().ToList()) m.Zone.Remove(m);
            foreach (var h in me.HandZone.GetAll().ToList()) me.HandZone.Remove(h);
            me.UsedMana = 0;
            var played = Give(me, card);
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, played, null, 0)), card);
            Assert.Equal(expected, Board(me));
        }
        foreach (var m in me.BoardZone.GetAll().ToList()) m.Zone.Remove(m);
        // Princess Talanji puts all the minions from the hand into play: both sides of her like any "all" (user rule 2026-10-10,
        // superseding 2026-10-04's "to her right"; live: Archwitch Willow's and Scarlet Recruiter's from the hand / deck go both sides)
        foreach (var h in me.HandZone.GetAll().ToList()) me.HandZone.Remove(h);
        Give(me, "CS2_168"); Give(me, "CS2_182");
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, Give(me, "TRL_259"), null, 0)));
        Assert.Equal(3, me.BoardZone.Count);
        Assert.Equal("TRL_259", me.BoardZone[1].Card.Id);
        foreach (var m in me.BoardZone.GetAll().ToList()) m.Zone.Remove(m);

        // Basic cost auras show like Sorcerer's Apprentice: their client card on each affected card in hand (user 2026-10-04)
        foreach (var h in me.HandZone.GetAll().ToList()) me.HandZone.Remove(h);
        var yetiInHand = Give(me, "CS2_182");
        foreach (var (aura, shown) in new[] { ("CS2_227", "CS2_227e"), ("EX1_315", "EX1_315e") })
        {
            me.UsedMana = 0;
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, Give(me, aura), null, 0)));
            game.DeathProcessingAndAuraUpdate();
            Assert.Contains(yetiInHand.AppliedEnchantments, e => e.Card.Id == shown);
        }
        foreach (var m in me.BoardZone.GetAll().ToList()) m.Zone.Remove(m);
        game.DeathProcessingAndAuraUpdate();

        // Choose One options in the text's order (client related cards): Ancient of War's "+5 Attack" first
        var ancient = (SabberStoneCore.Model.Entities.Playable)Give(me, "EX1_178");
        Assert.Equal("EX1_178b", ancient.ChooseOnePlayables[0].Card.Id);
        me.HandZone.Remove(ancient);

        // Redemption: the minion comes back in the slot it died in (user 2026-10-04)
        foreach (var m in me.BoardZone.GetAll().ToList()) m.Zone.Remove(m);
        var left = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("CS2_168"));
        var middle = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("CS2_182"));
        var right = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("CS2_120"));
        me.BoardZone.Add(left); me.BoardZone.Add(middle); me.BoardZone.Add(right);
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, Give(me, "EX1_136"), null, 0)));
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        op.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(op, Give(op, "CS2_076"), middle, 0))); // Assassinate
        Assert.Equal("CS2_168,CS2_182,CS2_120", Board(me));
        Assert.Equal(1, me.BoardZone[1].Health);
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(op));
        foreach (var m in me.BoardZone.GetAll().ToList()) m.Zone.Remove(m);

        // Shadowfiend: a trigger on its owner's draws only ("whenever you draw"), not an aura
        var fiend = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("AT_014"));
        me.BoardZone.Add(fiend);
        var mine = SabberStoneCore.Actions.Generic.Draw(me);
        var theirs = SabberStoneCore.Actions.Generic.Draw(op);
        Assert.Contains(mine.AppliedEnchantments, e => e.Card.Id == "AT_014e");
        Assert.True(theirs.AppliedEnchantments == null || theirs.AppliedEnchantments.All(e => e.Card.Id != "AT_014e"));
        fiend.Zone.Remove(fiend);

        // Sense Demons with no Demon in the deck: two Worthless Imps
        int hand = me.HandZone.Count;
        foreach (var d in me.DeckZone.GetAll().Where(p => p.Card.IsRace(Race.DEMON)).ToList()) me.DeckZone.Remove(d);
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, Give(me, "EX1_317"), null, 0)));
        Assert.Equal(2, me.HandZone.GetAll().Count(p => p.Card.Id == "EX1_317t"));

        // Mindgames with no minion in the opponent's deck: Shadow of Nothing
        foreach (var d in op.DeckZone.GetAll().Where(p => p.Card.Type == CardType.MINION).ToList()) op.DeckZone.Remove(d);
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, Give(me, "EX1_345"), null, 0)));
        Assert.Contains(me.BoardZone.GetAll(), m => m.Card.Id == "EX1_345t");
    }

    [Fact]
    public void Secrets_enchantments_and_velen_send_what_live_sends()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        T Get<T>(int from) where T : class => game.PowerHistory.Full.Skip(from).OfType<T>().FirstOrDefault();

        // An enchantment's reveal (live CS2_226e): its class, creator card (CREATOR_DBID), SPAWN_TIME_COUNT, no zero tags; an
        // amount in TAG_SCRIPT_DATA_NUM_1 (Natalie Seline's EX1_198e)
        var meal = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(op, SabberStoneCore.Model.Cards.FromId("CS2_182"));
        op.BoardZone.Add(meal);
        int from = game.PowerHistory.Full.Count;
        var natalie = Give(me, "CORE_EX1_198");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, natalie, meal, 0)));
        var shown = game.PowerHistory.Full.Skip(from).OfType<SabberStoneCore.Kettle.PowerHistoryShowEntity>().First(x => x.Entity.Name == "EX1_198e").Entity.Tags;
        Assert.Equal(natalie.Card.AssetId, shown[GameTag.CREATOR_DBID]);
        Assert.Equal(5, shown[GameTag.TAG_SCRIPT_DATA_NUM_1]);
        Assert.Equal(1, shown[(GameTag)1556]); // SPAWN_TIME_COUNT
        Assert.DoesNotContain(shown, t => t.Value == 0);

        // A triggered Secret (live, 74 of 74): its TRIGGER block holds only the effect, then SLUSH_TIME 5000 on it, then it goes
        // to the graveyard; never REVEALED; the block isn't flagged ShowInHistory
        me.UsedMana = 0;
        var barrier = Give(me, "EX1_289");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, barrier, null, 0)));
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        var attacker = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(op, SabberStoneCore.Model.Cards.FromId("CS2_171"));
        op.BoardZone.Add(attacker);
        attacker.IsExhausted = false;
        from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(op, attacker, me.Hero)));
        var history = game.PowerHistory.Full.Skip(from).ToList();
        int start = history.FindIndex(x => x is SabberStoneCore.Kettle.PowerHistoryBlockStart b && b.Source == barrier.Id && b.BlockType == BlockType.TRIGGER);
        Assert.True(start >= 0);
        Assert.False(((SabberStoneCore.Kettle.PowerHistoryBlockStart)history[start]).ShowInHistory);
        int depth = 0, end = -1;
        for (int i = start + 1; i < history.Count && end < 0; i++)
            if (history[i] is SabberStoneCore.Kettle.PowerHistoryBlockStart) depth++;
            else if (history[i] is SabberStoneCore.Kettle.PowerHistoryBlockEnd && depth-- == 0) end = i;
        Assert.DoesNotContain(history.Skip(start).Take(end - start), x => x is SabberStoneCore.Kettle.PowerHistoryTagChange t && t.EntityId == barrier.Id);
        var slush = Assert.IsType<SabberStoneCore.Kettle.PowerHistoryMetaData>(history[end + 1]);
        Assert.Equal(MetaDataType.SLUSH_TIME, slush.Type);
        Assert.Equal(5000, slush.Data);
        Assert.Contains(history.Skip(end + 1), x => x is SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: GameTag.ZONE, Value: (int)Zone.GRAVEYARD } t && t.EntityId == barrier.Id);
        Assert.DoesNotContain(history, x => x is SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: GameTag.REVEALED } t && t.EntityId == barrier.Id);
        Assert.Equal(7, me.Hero.Armor); // 8 Armor before the Boar's 1 damage (the Secret resolves before the attack)

        // Prophet Velen doubles the Hero Power's damage too (rule book DH2a): the Mage's Fireblast deals 2
        var velen = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(op, SabberStoneCore.Model.Cards.FromId("EX1_350"));
        op.BoardZone.Add(velen);
        game.DeathProcessingAndAuraUpdate();
        int health = me.Hero.Health, armor = me.Hero.Armor;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.HeroPowerTask.Any(op, me.Hero)));
        Assert.Equal(2, health + armor - me.Hero.Health - me.Hero.Armor);
    }

    [Fact]
    public void Turns_last_45_then_55_then_75_seconds_and_nozdormu_limits_them()
    {
        // Live (live-20261003-dk-classxp): TIMEOUT 45 at the start, 55 after that player's first turn, 75 after the second;
        // wiki Turn: "the turn timer for the first two turns of a match are now shorter". Nozdormu: "Players only have
        // 15 seconds to take their turns": both players' TIMEOUT while he is in play (live-20261008-issues: 20; Advanced rulebook:
        // "implemented by changing the TIMEOUT tag"), the turn's own TIMEOUT back when he leaves.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        Assert.Equal(45, me.TimeOut);
        Assert.Equal(45, me.Opponent.TimeOut);
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        Assert.Equal(55, me.TimeOut);
        Assert.Equal(45, me.Opponent.TimeOut);
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent));
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        Assert.Equal(75, me.TimeOut);
        Assert.Equal(55, me.Opponent.TimeOut);

        Assert.Null(me.TurnLimitSeconds);
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent));
        me.UsedMana = 0;
        var nozdormu = (SabberStoneCore.Model.Entities.Minion)Give(me, "EX1_560");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, nozdormu)));
        Assert.Equal(20, me.TurnLimitSeconds);
        Assert.Equal(20, me.Opponent.TurnLimitSeconds);
        Assert.Equal(20, me.TimeOut);
        Assert.Equal(20, me.Opponent.TimeOut);
        nozdormu.Destroy();
        game.DeathProcessingAndAuraUpdate();
        Assert.Null(me.TurnLimitSeconds);
        Assert.Equal(75, me.TimeOut);
        Assert.Equal(75, me.Opponent.TimeOut); // its second turn ended before Nozdormu came
    }

    [Fact]
    public void An_opponents_hidden_secret_shows_its_kind_class_and_cost_before_it_enters_the_secret_zone()
    {
        // Live (heist-run-20260929 #2088): the boss's Secret: SECRET 1, CLASS, COST, TAG_LAST_KNOWN_COST_IN_HAND, then ZONE
        // SECRET (card still hidden). Ours sent only the ZONE change and the client stopped processing (2026-10-05).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        var op = game.CurrentPlayer;
        var secret = Give(op, "EX1_289"); // Ice Barrier (Mage)
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(op, secret)));
        var seen = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        int zone = seen.FindIndex(d => d.TagChange is { Tag: (int)GAME_TAG.ZONE, Value: (int)TAG_ZONE.SECRET } t && t.Entity == secret.Id);
        Assert.True(zone > 0);
        int Before(GAME_TAG tag) => seen.FindLastIndex(zone, d => d.TagChange is { } t && t.Entity == secret.Id && t.Tag == (int)tag);
        Assert.True(Before(GAME_TAG.SECRET) >= 0, "SECRET");
        Assert.True(Before(GAME_TAG.CLASS) >= 0, "CLASS");
        Assert.True(Before(GAME_TAG.COST) >= 0, "COST");
        Assert.True(Before(GAME_TAG.SECRET) < Before(GAME_TAG.CLASS) && Before(GAME_TAG.CLASS) < Before(GAME_TAG.COST));
        // still hidden: no SHOW_ENTITY for the viewer
        Assert.DoesNotContain(seen, d => d.ShowEntity?.Entity == secret.Id);
    }

    [Fact]
    public void Shadowform_gives_the_hero_its_shadow_look_and_mind_spike()
    {
        // User: Shadowform puts a shadow tint on the hero; the client draws it from the hero's SHADOWFORM tag (Actor.cs).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        int from = game.PowerHistory.Full.Count;
        int oldPower = me.Hero.HeroPower.Id;
        var shadowform = Give(me, "EX1_625");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, shadowform)));
        Assert.Equal(1, me.Hero[GameTag.SHADOWFORM]);
        // live (live-20261004-shadowform-leeroy): the new Hero Power's FULL_ENTITY names the card that made it, then it links
        // the old one (the client's swap)
        var created = game.PowerHistory.Full.Skip(from).OfType<SabberStoneCore.Kettle.PowerHistoryFullEntity>()
            .Single(f => f.Entity.Id == me.Hero.HeroPower.Id);
        Assert.Equal(shadowform.Id, created.Entity.Tags[GameTag.CREATOR]);
        Assert.Equal(shadowform.Card.AssetId, created.Entity.Tags[GameTag.CREATOR_DBID]);
        Assert.Equal(oldPower, me.Hero.HeroPower[GameTag.LINKED_ENTITY]);
        Assert.Equal("EX1_625t", me.Hero.HeroPower.Card.Id);
        Assert.Contains(game.PowerHistory.Full.Skip(from).OfType<SabberStoneCore.Kettle.PowerHistoryTagChange>(),
            t => t.EntityId == me.Hero.Id && t.Tag == GameTag.SHADOWFORM && t.Value == 1);
    }

    [Fact]
    public void Corpse_spenders_spend_their_corpses_and_do_their_thing()
    {
        // The 26 Corpse spenders that were placeholders (audit 2026-10-04): each played with plenty of Corpses spends
        // some and leaves no engine error; a few checked in detail.
        _ = Db.Value; // the client cards first
        foreach (string id in new[] { "CATA_465", "DINO_416", "EDR_815", "END_005", "ETC_428", "GDB_113", "GDB_470", "JAIL_451",
                     "RLK_085", "LEG_RLK_085", "RLK_101", "LEG_RLK_101", "RLK_226", "RLK_715", "RLK_753", "MIS_101", "RLK_731", "TLC_434",
                     "TLC_436", "TOY_827", "WORK_070", "WW_357", "YOG_511" })
            Assert.NotNull(SabberStoneCore.Model.Cards.FromId(id).Power);

        var game = TenManaGame();
        var me = game.CurrentPlayer;
        // Chow Down: five Drakes with Rush for 8 Corpses
        me.GainCorpses(8);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "CATA_465"))));
        Assert.Equal(0, me.Corpses);
        Assert.Equal(5, me.BoardZone.Count(m => m.Card.Id == "CATA_465t" && m[GameTag.RUSH] == 1));
    }

    [Theory]
    [InlineData("CATA_465")] [InlineData("DINO_416")] [InlineData("EDR_813")] [InlineData("EDR_815")] [InlineData("END_005")]
    [InlineData("ETC_428")] [InlineData("ETC_533")] [InlineData("FIR_951")] [InlineData("GDB_113")] [InlineData("GDB_470")]
    [InlineData("JAIL_451")] [InlineData("RLK_085")] [InlineData("RLK_101")] [InlineData("RLK_226")] [InlineData("RLK_715")]
    [InlineData("RLK_753")] [InlineData("MIS_101")] [InlineData("RLK_731")] [InlineData("TLC_434")] [InlineData("TLC_436")]
    [InlineData("TOY_827")] [InlineData("VAC_427")] [InlineData("WORK_070")] [InlineData("WW_357")] [InlineData("YOG_511")]
    [InlineData("YOG_513")] [InlineData("LEG_RLK_085")] [InlineData("LEG_RLK_753")] [InlineData("EDR_811")]
    public void A_corpse_card_plays_and_spends_without_an_engine_error(string id)
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        // a friendly Undead on board (Sinister Soulcage's target) and one that died (Boneshredder)
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "RLK_958"))));
        me.GainCorpses(40);
        me.UsedMana = 0;
        var card = Give(me, id);
        bool location = card is SabberStoneCore.Model.Entities.Location;
        var play = card.ChooseOne ? SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, card, me.BoardZone.First(), -1, 2)
            : location ? SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, card)
            : card.Card.MustHaveTargetToPlay || card.Card.TargetingType != SabberStoneCore.Enums.TargetingType.None && card.ValidPlayTargets.Any()
                ? SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, card, card.ValidPlayTargets.First())
                : SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, card);
        Assert.True(game.Process(play), $"{id} not played");
        if (location) // then use it on the Undead
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.LocationTask.Any(me, (SabberStoneCore.Model.Entities.Location)card, me.BoardZone.First(m => !(m is SabberStoneCore.Model.Entities.Location)))), $"{id} not used");
        while (me.Choice != null)
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, me.Choice.Choices[0])));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)));
    }

    [Fact]
    public void Corpse_costs_replace_mana_like_the_client_tag_says()
    {
        // Reanimated Pterrordax: CARD_ALTERNATE_COST = 3 (Corpses) in the client data; Exarch Maladaar: the next card this turn.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var ptero = Give(me, "TLC_436");
        Assert.False(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, ptero))); // no Corpses: not playable
        me.GainCorpses(5);
        int mana = me.RemainingMana;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, ptero)));
        Assert.Equal((0, mana), (me.Corpses, me.RemainingMana));      // paid in Corpses, not Mana

        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "GDB_470"))));
        mana = me.RemainingMana;
        me.GainCorpses(4);
        var yeti = Give(me, "CS2_182");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, yeti)));
        Assert.Equal((0, mana), (me.Corpses, me.RemainingMana));      // the next card: Corpses
        var next = Give(me, "CS2_231");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, next)));
        Assert.Equal(mana - 0, me.RemainingMana + next.Cost);          // the one after: Mana again
    }

    [Fact]
    public void Volcoross_offers_what_the_corpses_pay_for_and_marrowgar_buffs_what_cant_fit()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.GainCorpses(25);
        var volcoross = Give(me, "FIR_951");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, volcoross)));
        Assert.Equal(2, me.Choice.Choices.Count); // 10 and 20, not 30
        int twenty = me.Choice.Choices.First(id => game.IdEntityDic[id].Card.Id == "FIR_951t3");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, twenty)));
        Assert.Equal((5, 25, 25), (me.Corpses, ((SabberStoneCore.Model.Entities.Minion)volcoross).AttackDamage, ((SabberStoneCore.Model.Entities.Minion)volcoross).Health));

        // Lord Marrowgar: 5 slots left after it, 9 Corpses -> 5 Golems, 4 x +2/+2 among them
        me.GainCorpses(4);
        var lord = Give(me, "RLK_085");
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, lord)));
        var golems = me.BoardZone.Where(m => m.Card.Id == "RLK_085t").ToList();
        Assert.Equal(5, golems.Count);
        Assert.Equal(0, me.Corpses);
        Assert.Equal(5 + 4 * 2, golems.Sum(m => m.AttackDamage));
    }

    [Fact]
    public void A_plain_attack_or_health_effect_changes_the_stat_and_comes_off_again()
    {
        // A character's Attack / Health live in their own fields: a plain ATK / HEALTH enchant effect wrote an unread tag
        // (Volcoross, Vampiric Blood never gave Health), while removing it took the stat off. Vampiric Blood: +5 Health.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        int max = me.Hero.BaseHealth;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "CORE_RLK_051"))));
        Assert.Equal(max + 5, me.Hero.BaseHealth);
        var yeti = (SabberStoneCore.Model.Entities.Minion)Give(me, "CS2_182");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, yeti)));
        var raw = new SabberStoneCore.Enchants.Effect(GameTag.ATK, SabberStoneCore.Enchants.EffectOperator.ADD, 3);
        raw.ApplyTo(yeti);
        Assert.Equal(7, yeti.AttackDamage);
        raw.RemoveFrom(yeti);
        Assert.Equal(4, yeti.AttackDamage);
    }

    [Fact]
    public void Corpsicle_comes_back_at_the_end_of_the_turn_for_three_corpses()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.GainCorpses(3);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "VAC_427"), me.Opponent.Hero)));
        Assert.Equal(27, me.Opponent.Hero.Health);
        int inHand = me.HandZone.Count(p => p.Card.Id == "VAC_427");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.Equal(inHand + 1, me.HandZone.Count(p => p.Card.Id == "VAC_427"));
    }

    [Fact]
    public void Rite_of_atrocity_discovers_an_undead_with_a_dark_gift_for_two_corpses()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var rite = Give(me, "EDR_811");
        me.GainCorpses(2);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, rite)));
        Assert.Equal(0, me.Corpses);
        Assert.NotNull(me.Choice);
        foreach (int id in me.Choice.Choices)
        {
            var option = game.IdEntityDic[id];
            Assert.True(option.Card.IsRace(Race.UNDEAD));
            Assert.Contains(option.AppliedEnchantments ?? new List<SabberStoneCore.Model.Entities.Enchantment>(), e => e.Card.Id.StartsWith("EDR_100t"));
        }
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, me.Choice.Choices[0])));
        var plain = Give(me, "EDR_811");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, plain)));
        Assert.NotNull(me.Choice); // no Corpses: a plain Discover
        Assert.All(me.Choice.Choices, id => Assert.True(game.IdEntityDic[id].Card.IsRace(Race.UNDEAD)));
    }

    [Fact]
    public void Random_pools_follow_the_generation_rules()
    {
        // Wiki: Quests / Questlines / Titans / triple-rune cards are never randomly generated or Discovered; Colossals only
        // never go onto the battlefield at random (Generate "Summoning only"); Sidequests can be generated.
        var cards = SabberStoneCore.Model.Cards.All;
        foreach (var format in new[] { FormatType.FT_WILD, FormatType.FT_STANDARD })
        {
            var pool = SabberStoneCore.Model.Cards.FormatTypeCards(format).ToList();
            Assert.DoesNotContain(pool, c => c.IsQuest || c[(GameTag)2772] > 0 || c[(GameTag)2196] + c[(GameTag)2197] + c[(GameTag)2198] >= 3);
            Assert.All(SabberStoneCore.Model.Cards.BoardCostMinionCards(format).Values.SelectMany(x => x), c => Assert.Equal(0, c[(GameTag)2247]));
        }
        Assert.Contains(SabberStoneCore.Model.Cards.AllWild, c => c[(GameTag)2247] > 0);                    // Colossals: hand / deck
        Assert.Contains(SabberStoneCore.Model.Cards.AllWild, c => c[GameTag.SIDEQUEST] > 0);               // Sidequests: yes
        Assert.Contains(SabberStoneCore.Model.Cards.FormatTypeCollectible(FormatType.FT_WILD), c => c.IsQuest); // named pools keep them
        Assert.DoesNotContain(SabberStoneCore.Model.Cards.AllWild, c => c.Name == "Frostwyrm's Fury" || c.Name == "Grave Strength");
    }

    [Fact]
    public void Evolve_stays_within_the_wikis_cost_rules()
    {
        // Wiki Transform: 10 or less never evolves to 11+, 11+ only transforms to its own cost, 0 is the floor.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var costs = SabberStoneCore.Model.Cards.CostMinionCards(FormatType.FT_WILD);
        var ten = Give(me, costs[10][0].Id);
        var zero = Give(me, costs[0][0].Id);
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(10, SabberStoneCore.Model.Cards.EvolvePick(FormatType.FT_WILD, ten, 1, new SabberStoneCore.Model.Util.DeepCloneableRandom(i + 1)).Cost);
            Assert.Equal(9, SabberStoneCore.Model.Cards.EvolvePick(FormatType.FT_WILD, ten, -1, new SabberStoneCore.Model.Util.DeepCloneableRandom(i + 1)).Cost);
            Assert.Equal(0, SabberStoneCore.Model.Cards.EvolvePick(FormatType.FT_WILD, zero, -1, new SabberStoneCore.Model.Util.DeepCloneableRandom(i + 1)).Cost);
        }
        int big = costs.Keys.Where(k => k > 10).DefaultIfEmpty(0).Max();
        if (big > 0)
            Assert.Equal(big, SabberStoneCore.Model.Cards.EvolvePick(FormatType.FT_WILD, Give(me, costs[big][0].Id), 1, new SabberStoneCore.Model.Util.DeepCloneableRandom(1)).Cost);
    }

    [Fact]
    public void Heals_that_deal_damage_are_shown_to_the_client()
    {
        // User test (2026-10-01): no purple "heals deal damage" haze. The client needs HEALING_DOES_DAMAGE (326) on the
        // player and HEALING_DOES_DAMAGE_HINT (1117) on the source (hover / enter play burst the heal cards in hand).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        int Tag(int id, SabberStoneCore.Enums.GameTag tag)
        {
            game.PublishDerived();
            return game.PowerHistory.Reported.TryGetValue(id, out var tags) && tags.TryGetValue(tag, out int v) ? v : 0;
        }
        var auchenai = (SabberStoneCore.Model.Entities.Minion)Give(me, "EX1_591");
        Assert.Equal(0, Tag(me.Id, SabberStoneCore.Enums.GameTag.HEALING_DOES_DAMAGE));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, auchenai)));
        Assert.Equal(1, Tag(me.Id, SabberStoneCore.Enums.GameTag.HEALING_DOES_DAMAGE));
        Assert.Equal(1, Tag(auchenai.Id, SabberStoneCore.Enums.GameTag.HEALING_DOES_DAMAGE_HINT));
        auchenai.Damage = auchenai.BaseHealth;
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(0, Tag(me.Id, SabberStoneCore.Enums.GameTag.HEALING_DOES_DAMAGE));
        Assert.Equal(0, Tag(auchenai.Id, SabberStoneCore.Enums.GameTag.HEALING_DOES_DAMAGE_HINT));
    }

    [Fact]
    public void This_turn_heals_deal_damage_shows_the_purple_border()
    {
        // Live (live-20261001-auchenai): Auchenai Phantasm's Battlecry sets EMBRACE_THE_SHADOW (442) on the player (the
        // client's purple screen border on the hero) and HEALING_DOES_DAMAGE_HINT on the HERO; they end with the turn.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        int Tag(int id, SabberStoneCore.Enums.GameTag tag)
        {
            game.PublishDerived();
            return game.PowerHistory.Reported.TryGetValue(id, out var tags) && tags.TryGetValue(tag, out int v) ? v : 0;
        }
        var phantasm = (SabberStoneCore.Model.Entities.Minion)Give(me, "TRL_501");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, phantasm)));
        Assert.Equal(1, Tag(me.Id, SabberStoneCore.Enums.GameTag.EMBRACE_THE_SHADOW));
        Assert.Equal(1, Tag(me.Hero.Id, SabberStoneCore.Enums.GameTag.HEALING_DOES_DAMAGE_HINT));
        Assert.Equal(0, Tag(phantasm.Id, SabberStoneCore.Enums.GameTag.HEALING_DOES_DAMAGE_HINT)); // its effect is on the player
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.Equal(0, Tag(me.Id, SabberStoneCore.Enums.GameTag.EMBRACE_THE_SHADOW));
        Assert.Equal(0, Tag(me.Hero.Id, SabberStoneCore.Enums.GameTag.HEALING_DOES_DAMAGE_HINT));
    }

    [Fact]
    public void Spell_damage_totals_reach_the_client_and_schools_count()
    {
        // Live keeps the numbers the client writes on card text as running totals on the player: CURRENT_SPELLPOWER_BASE
        // (291) and CURRENT_SPELLPOWER_<school> (1936-1943, e.g. a Fire Spell Damage minion → _FIRE = 2). A spell gets the
        // base plus its school's Spell Damage.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var them = me.Opponent;
        int Tag(int id, SabberStoneCore.Enums.GameTag tag)
        {
            game.PublishDerived();
            return game.PowerHistory.Reported.TryGetValue(id, out var tags) && tags.TryGetValue(tag, out int v) ? v : 0;
        }
        var kobold = (SabberStoneCore.Model.Entities.Minion)Give(me, "CS2_142"); // Kobold Geomancer: Spell Damage +1
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, kobold)));
        Assert.Equal(1, Tag(me.Id, SabberStoneCore.Enums.GameTag.CURRENT_SPELLPOWER));

        kobold[SabberStoneCore.Model.Entities.Controller.SchoolSpellPowerTag(2)] = 2; // Fire Spell Damage +2 (school 2)
        Assert.Equal(2, Tag(me.Id, SabberStoneCore.Model.Entities.Controller.CurrentSchoolSpellPowerTag(2)));
        Assert.Equal(0, Tag(me.Id, SabberStoneCore.Model.Entities.Controller.CurrentSchoolSpellPowerTag(3)));

        int health = them.Hero.Health;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_029"), them.Hero))); // Fireball 6 (Fire)
        Assert.Equal(health - (6 + 1 + 2), them.Hero.Health);
        health = them.Hero.Health;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_024"), them.Hero))); // Frostbolt 3 (Frost)
        Assert.Equal(health - (3 + 1), them.Hero.Health);
    }

    [Fact]
    public void Game_counters_reach_the_client_like_live()
    {
        // Live keeps these on the player (wire-tag-gaps-20261001): spells played this game, friendly minions died this game,
        // health restored this game, armor gained this game, the player's own TURN, and at the start of a player's turn
        // the damage their hero took during the opponent's turn.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var them = me.Opponent;
        int Tag(int id, SabberStoneCore.Enums.GameTag tag)
        {
            game.PublishDerived();
            return game.PowerHistory.Reported.TryGetValue(id, out var tags) && tags.TryGetValue(tag, out int v) ? v : 0;
        }
        int myTurn = Tag(me.Id, SabberStoneCore.Enums.GameTag.TURN);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_029"), them.Hero))); // Fireball: 6 to their hero
        Assert.Equal(1, Tag(me.Id, SabberStoneCore.Enums.GameTag.NUM_SPELLS_PLAYED_THIS_GAME));
        var wisp = Give(me, "CS2_231");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, wisp)));
        ((SabberStoneCore.Model.Entities.Minion)wisp).Damage = 1;
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(1, Tag(me.Id, SabberStoneCore.Enums.GameTag.NUM_FRIENDLY_MINIONS_THAT_DIED_THIS_GAME));
        me.Hero.GainArmor(me.Hero, 5);
        Assert.Equal(5, Tag(me.Id, HsTag.ARMOR_GAINED_THIS_GAME));
        me.Hero.Damage = 4;
        me.Hero.TakeHeal(me.Hero, 3);
        Assert.Equal(3, Tag(me.Id, SabberStoneCore.Enums.GameTag.AMOUNT_HEALED_THIS_GAME));

        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.Equal(6, Tag(them.Id, HsTag.DAMAGE_DEALT_TO_HERO_LAST_TURN)); // their hero took the Fireball
        int theirTurn = Tag(them.Id, SabberStoneCore.Enums.GameTag.TURN);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(them)));
        Assert.Equal(myTurn + 1, Tag(me.Id, SabberStoneCore.Enums.GameTag.TURN));
        Assert.Equal(theirTurn, Tag(them.Id, SabberStoneCore.Enums.GameTag.TURN));
        Assert.Equal(0, Tag(me.Id, HsTag.DAMAGE_DEALT_TO_HERO_LAST_TURN)); // nothing hit my hero on their turn
    }

    [Fact]
    public void Outcast_glows_at_the_edges_and_health_costs_show_on_the_gem()
    {
        // Live: an Outcast card (Spectral Sight) gets POWERED_UP in the left- or right-most hand slot. A card that costs
        // Health shows it on its cost gem: CARD_ALTERNATE_COST (2837) = 1 (HEALTH).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        int Tag(int id, SabberStoneCore.Enums.GameTag tag)
        {
            game.PublishDerived();
            return game.PowerHistory.Reported.TryGetValue(id, out var tags) && tags.TryGetValue(tag, out int v) ? v : 0;
        }
        var sight = Give(me, "CORE_BT_491"); // drawn last: right-most
        Assert.Equal(1, Tag(sight.Id, SabberStoneCore.Enums.GameTag.POWERED_UP));
        Give(me, "CS2_231");                 // now in the middle? only if it isn't left-most
        bool outer = SabberStoneCore.Model.Entities.Playable.InOuterHandSlot(sight);
        Assert.Equal(outer ? 1 : 0, Tag(sight.Id, SabberStoneCore.Enums.GameTag.POWERED_UP));
        Assert.False(outer); // a full-ish hand: Spectral Sight sits between other cards

        var gorgon = Give(me, "CORE_ETC_523"); // costs Health instead of Mana if your hero was healed this turn
        Assert.Equal(0, Tag(gorgon.Id, HsTag.CARD_ALTERNATE_COST));
        me.Hero.Damage = 5;
        me.Hero.TakeHeal(me.Hero, 2);
        Assert.Equal(1, Tag(gorgon.Id, HsTag.CARD_ALTERNATE_COST));
    }

    [Fact]
    public void Turn_bookkeeping_and_keyword_states_follow_live()
    {
        // Live (trade-lifesteal, heist): NUM_TURNS_IN_HAND +1 on the ending player's hand cards; JUST_PLAYED back to 0 in
        // MAIN_CLEANUP; DIVINE_SHIELD_DAMAGE 1 before DIVINE_SHIELD 0, cleared after the action; a dead minion's DAMAGE
        // goes to 0 in its DEATHS block after ZONE=GRAVEYARD; gained Elusive sets ELUSIVE.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var them = me.Opponent;
        int Tag(int id, SabberStoneCore.Enums.GameTag tag)
        {
            game.PublishDerived();
            return game.PowerHistory.Reported.TryGetValue(id, out var tags) && tags.TryGetValue(tag, out int v) ? v : 0;
        }
        var held = Give(me, "CS2_182");
        int turnsBefore = held[HsTag.NUM_TURNS_IN_HAND];
        var yeti = (SabberStoneCore.Model.Entities.Minion)Give(me, "CS2_182");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, yeti)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.Equal(turnsBefore + 1, Tag(held.Id, HsTag.NUM_TURNS_IN_HAND));
        Assert.Equal(0, Tag(yeti.Id, SabberStoneCore.Enums.GameTag.JUST_PLAYED));

        // Divine Shield absorbs a hit: flagged in the action, cleared after it
        var shielded = (SabberStoneCore.Model.Entities.Minion)Give(them, "EX1_008"); // Argent Squire
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(them, shielded)));
        int before = game.PowerHistory.Full.Count;
        shielded.TakeDamage(yeti, 3);
        var hit = game.PowerHistory.Full.Skip(before).OfType<SabberStoneCore.Kettle.PowerHistoryTagChange>().Where(t => t.EntityId == shielded.Id).ToList();
        int flag = hit.FindIndex(t => t.Tag == HsTag.DIVINE_SHIELD_DAMAGE && t.Value == 1);
        int broken = hit.FindIndex(t => t.Tag == SabberStoneCore.Enums.GameTag.DIVINE_SHIELD && t.Value == 0);
        Assert.True(flag >= 0 && broken > flag);
        Assert.Equal(0, Tag(shielded.Id, HsTag.DIVINE_SHIELD_DAMAGE));

        // a dead minion's damage is cleared after it reaches the graveyard
        before = game.PowerHistory.Full.Count;
        yeti.Damage = yeti.BaseHealth;
        game.DeathProcessingAndAuraUpdate();
        var death = game.PowerHistory.Full.Skip(before).OfType<SabberStoneCore.Kettle.PowerHistoryTagChange>().Where(t => t.EntityId == yeti.Id).ToList();
        int toGrave = death.FindIndex(t => t.Tag == SabberStoneCore.Enums.GameTag.ZONE && t.Value == (int)SabberStoneCore.Enums.Zone.GRAVEYARD);
        int cleared = death.FindIndex(t => t.Tag == SabberStoneCore.Enums.GameTag.DAMAGE && t.Value == 0);
        Assert.True(toGrave >= 0 && cleared > toGrave);

        // Elusive gained by an effect
        shielded[SabberStoneCore.Enums.GameTag.CANT_BE_TARGETED_BY_SPELLS] = 1;
        shielded[SabberStoneCore.Enums.GameTag.CANT_BE_TARGETED_BY_HERO_POWERS] = 1;
        Assert.Equal(1, Tag(shielded.Id, HsTag.ELUSIVE));
    }

    [Fact]
    public void Discover_copy_and_hero_power_markers_follow_live()
    {
        // Live: each Discover option gets WAS_DISCOVER_OPTION = 1 after its FULL_ENTITY; a copy names its original
        // (COPIED_FROM_ENTITY_ID); a replacement Hero Power links to the one it replaces (LINKED_ENTITY).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "CORE_WON_096")))); // Dark Peddler: Discover
        Assert.NotNull(me.Choice);
        Assert.All(me.Choice.Choices, id => Assert.Equal(1, game.IdEntityDic[id][HsTag.WAS_DISCOVER_OPTION]));

        var original = Give(me, "CS2_182");
        var copy = SabberStoneCore.Actions.Generic.Copy(me, me.Hero, original, SabberStoneCore.Enums.Zone.HAND);
        Assert.Equal(original.Id, copy[HsTag.COPIED_FROM_ENTITY_ID]);

        int oldPower = me.Hero.HeroPower.Id;
        new SabberStoneCore.Tasks.SimpleTasks.ReplaceHeroPower(SabberStoneCore.Model.Cards.FromId("CS2_034")).Process(game, me, me.Hero, null);
        Assert.Equal(oldPower, me.Hero.HeroPower[SabberStoneCore.Enums.GameTag.LINKED_ENTITY]);
    }

    [Fact]
    public void A_transform_mutes_the_summon_voice_around_the_change()
    {
        // Live (Primordial Wave / Evolve / Hex): SUPPRESS_ALL_SUMMON_VO 1 before the CHANGE_ENTITY, 0 after it.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var yeti = (SabberStoneCore.Model.Entities.Minion)Give(me, "CS2_182");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, yeti)));
        int before = game.PowerHistory.Full.Count;
        Assert.True(SabberStoneCore.Actions.Generic.TransformBlock(me, SabberStoneCore.Model.Cards.FromId("CS2_231"), yeti));
        var h = game.PowerHistory.Full.Skip(before).ToList();
        int on = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: HsTag.SUPPRESS_ALL_SUMMON_VO, Value: 1 } t && t.EntityId == yeti.Id);
        int change = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryChangeEntity c && c.Entity.Id == yeti.Id);
        int off = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: HsTag.SUPPRESS_ALL_SUMMON_VO, Value: 0 } t && t.EntityId == yeti.Id);
        Assert.True(on >= 0 && change > on && off > change, $"on {on}, change {change}, off {off}");
    }

    [Fact]
    public void Turn_89_draw_99_card_deck_and_one_copy_of_a_secret()
    {
        // Rules: a draw when turn 89 has just ended; the deck holds 99 cards; the hero zone holds one copy of a Secret, so a
        // duplicate an effect casts is lost.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        while (me.DeckZone.Count < 99) SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("CS2_182"), zone: me.DeckZone);
        Assert.Equal(99, me.DeckZone.Count);
        Assert.True(me.DeckZone.IsFull);

        var first = Give(me, "EX1_610"); // Explosive Trap
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, first)));
        var second = (SabberStoneCore.Model.Entities.Spell)SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("EX1_610"));
        SabberStoneCore.Actions.Generic.CastSpell.Invoke(me, game, second, null, 0); // cast by an effect
        Assert.Equal(1, me.SecretZone.Count(s => s.Card.Id == "EX1_610"));
        Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, second.Zone.Type);

        game.Turn = SabberStoneCore.Model.Game.MaxTurns;
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(game.CurrentPlayer));
        Assert.Equal(SabberStoneCore.Enums.State.COMPLETE, game.State);
        Assert.Equal(SabberStoneCore.Enums.PlayState.TIED, game.Player1.PlayState);
        Assert.Equal(SabberStoneCore.Enums.PlayState.TIED, game.Player2.PlayState);
    }

    [Fact]
    public void A_hero_power_is_exhausted_and_counted_before_the_death_phase()
    {
        // Live (8 of 8 hero power plays with a death): the activation counters and EXHAUSTED come before the DEATHS block.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        new SabberStoneCore.Tasks.SimpleTasks.ReplaceHeroPower(SabberStoneCore.Model.Cards.FromId("CS2_034")).Process(game, me, me.Hero, null); // Fireblast
        var wisp = (SabberStoneCore.Model.Entities.Minion)Give(me.Opponent, "CS2_231");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me.Opponent, wisp)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)));
        int before = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.HeroPowerTask.Any(me, wisp)));
        var h = game.PowerHistory.Full.Skip(before).ToList();
        int exhausted = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: SabberStoneCore.Enums.GameTag.EXHAUSTED, Value: 1 } t && t.EntityId == me.Hero.HeroPower.Id);
        int deaths = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart { BlockType: SabberStoneCore.Enums.BlockType.DEATHS });
        Assert.True(exhausted >= 0 && deaths > exhausted, $"exhausted {exhausted}, deaths {deaths}");
        Assert.Equal(1, me.Hero.HeroPower[SabberStoneCore.Enums.GameTag.HEROPOWER_ACTIVATIONS_THIS_TURN]);
    }

    [Fact]
    public void Extra_hero_power_uses_come_from_an_aura()
    {
        // Garrison Commander: a Hero Power usable twice a turn; Coldarra Drake: any number of times, also when it arrives
        // after the Hero Power was used (HEROPOWER_ADDITIONAL_ACTIVATIONS on the player, not an Inspire refresh).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        new SabberStoneCore.Tasks.SimpleTasks.ReplaceHeroPower(SabberStoneCore.Model.Cards.FromId("CS2_102")).Process(game, me, me.Hero, null); // Armor Up!
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "AT_080")))); // Garrison Commander
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.HeroPowerTask.Any(me)));
        Assert.False(me.Hero.HeroPower.IsExhausted);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.HeroPowerTask.Any(me)));
        Assert.True(me.Hero.HeroPower.IsExhausted);

        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "AT_008")))); // Coldarra Drake
        Assert.False(me.Hero.HeroPower.IsExhausted); // usable again
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.HeroPowerTask.Any(me)));
        Assert.False(me.Hero.HeroPower.IsExhausted);
    }

    [Fact]
    public void Fatigue_is_not_damage_the_hero_deals()
    {
        // Rules: Fatigue (and a Health cost) is not dealt by the hero: a Lifesteal weapon doesn't heal it back.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var weapon = (SabberStoneCore.Model.Entities.Weapon)SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("CS2_106"));
        me.Hero.AddWeapon(weapon);
        weapon.HasLifeSteal = true;
        Assert.True(me.Hero.HasLifeSteal);
        me.Hero.Damage = 10;
        foreach (var card in me.DeckZone.ToList()) me.DeckZone.Remove(card);
        int health = me.Hero.Health;
        SabberStoneCore.Actions.Generic.Draw(me);
        Assert.Equal(health - 1, me.Hero.Health); // 1 fatigue, no heal back
    }

    [Fact]
    public void A_summon_onto_a_full_board_does_not_happen()
    {
        // Rules: a full board means the summon doesn't happen; a script that doesn't check must not crash the game.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        while (!me.BoardZone.IsFull)
        {
            var m = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("CS2_231"));
            SabberStoneCore.Actions.Generic.SummonBlock.Invoke(game, m, -1, me.Hero);
        }
        var extra = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("CS2_182"));
        SabberStoneCore.Actions.Generic.SummonBlock.Invoke(game, extra, -1, me.Hero);
        Assert.Equal(7, me.BoardZone.Count);
        Assert.Null(extra.Zone);
    }

    [Fact]
    public void A_scripted_condition_glows_too()
    {
        // User test (2026-10-01): Crystalsmith Cultist (If you're holding a Shadow spell, gain +1/+1) didn't glow: its script
        // hid the check inside its effect. Written with ComplexTask.If, the check is readable and the glow follows it.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var cultist = Give(me, "CORE_RLK_814");
        int Glow()
        {
            game.PublishDerived();
            return game.PowerHistory.Reported.TryGetValue(cultist.Id, out var tags) && tags.TryGetValue(SabberStoneCore.Enums.GameTag.POWERED_UP, out int v) ? v : 0;
        }
        Assert.Equal(0, Glow());
        var pain = Give(me, "CORE_CS2_234"); // Shadow Word: Pain (a Shadow spell)
        Assert.Equal(1, Glow());
        int health = ((SabberStoneCore.Model.Entities.Minion)cultist).Health;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, cultist)));
        Assert.Equal(health + 1, ((SabberStoneCore.Model.Entities.Minion)cultist).Health); // the effect still works
        Assert.Equal(0, Glow());
    }

    [Fact]
    public void An_area_spell_plays_its_effect_around_the_whole_action_like_live()
    {
        // Live (JAIL_805 LightningStorm, GDB_305 Flood, AV_258pt4 HalfBoard: RESEARCH/vfx): an area effect is ONE
        // SUB_SPELL, first in the POWER block, from the caster's hero, Targets = the characters hit, the damage inside.
        // Immolation Aura's prefab comes from the client CardDef (ReuseFX_AE_DemonHunter_Shockwave...).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var a = Give(me.Opponent, "CS2_182");
        var b = Give(me.Opponent, "CS2_182");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me.Opponent, a)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me.Opponent, b)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)));
        var aura = Give(me, "CORE_BT_514");
        int before = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, aura)));

        var h = game.PowerHistory.Full.Skip(before).ToList();
        var subs = h.OfType<SabberStoneCore.Kettle.PowerHistorySubSpellStart>().Where(s => s.Prefab.StartsWith("ReuseFX_AE_DemonHunter_Shockwave")).ToList();
        // one per round of the action ("deal 1 damage to all minions twice": 2), not per target; live plays a repeated
        // round's effect again (Blood Tap, audit 2026-10-04; Immolation Aura itself: UNCLEAR, no live recording)
        Assert.Equal(2, subs.Count);
        foreach (var sub in subs)
        {
            Assert.Equal(me.Hero.Id, sub.Source);              // a spell's plays from its caster's hero
            Assert.Contains(a.Id, sub.Targets);
            Assert.Contains(b.Id, sub.Targets);
            int start = h.IndexOf(sub);
            int end = h.FindIndex(start, e => e is SabberStoneCore.Kettle.PowerHistorySubSpellEnd);
            Assert.Contains(h.Skip(start).Take(end - start), e => e is SabberStoneCore.Kettle.PowerHistoryMetaData { Type: SabberStoneCore.Enums.MetaDataType.DAMAGE });
        }
    }

    [Fact]
    public void A_spells_missile_flies_from_its_casters_hero_like_live()
    {
        // live-20261002-visuals (Demonic Assault x4): the missile SUB_SPELL's Source = the caster's hero, the spawn's = 0.
        // From the card it started wherever the card was held.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var assault = Give(me, "CORE_SW_088");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, assault, me.Opponent.Hero)));
        var subs = game.PowerHistory.Last.OfType<SabberStoneCore.Kettle.PowerHistorySubSpellStart>().ToList();
        Assert.NotEmpty(subs);
        Assert.All(subs.Where(s => s.Targets.Contains(me.Opponent.Hero.Id)), s => Assert.Equal(me.Hero.Id, s.Source));
        Assert.DoesNotContain(subs, s => s.Source == assault.Id);
    }

    [Fact]
    public void A_removed_deck_card_is_revealed_into_the_graveyard_like_live()
    {
        // live-20261002-visuals (Gnomeferatu): SHOW_ENTITY of the opponent's top card with its CardID, ZONE=GRAVEYARD inside,
        // LAST_AFFECTED_BY = Gnomeferatu; no separate ZONE change. It isn't a discard.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var top = me.Opponent.DeckZone.TopCard;
        var gnome = Give(me, "CORE_ICC_407");
        int before = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, gnome)));
        Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, top.Zone.Type);
        Assert.Empty(me.Opponent.DiscardedEntities);
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full.Skip(0).ToList());
        var show = sent.Last(d => d.ShowEntity?.Entity == top.Id).ShowEntity;
        Assert.Equal(top.Card.Id, show.Name);
        Assert.Contains(show.Tags, t => t.Name == (int)GameTag.ZONE && t.Value == (int)SabberStoneCore.Enums.Zone.GRAVEYARD);
        Assert.Contains(show.Tags, t => t.Name == (int)GameTag.LAST_AFFECTED_BY && t.Value == gnome.Id);
        int at = sent.FindLastIndex(d => d.ShowEntity?.Entity == top.Id);
        Assert.DoesNotContain(sent.Skip(at + 1), d => d.TagChange is { } t && t.Entity == top.Id && t.Tag == (int)GameTag.ZONE);
    }

    [Fact]
    public void Void_contract_destroys_half_of_each_deck_revealed_without_the_mill_animation()
    {
        // live-20261002-visuals: every destroyed card SHOW_ENTITY into the graveyard with SUPPRESS_MILL_ANIMATION=1, the
        // caster's first; half rounded up (wiki); not a discard.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var contract = Give(me, "TRL_246");
        int mine = me.DeckZone.Count, theirs = me.Opponent.DeckZone.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, contract)));
        Assert.Equal(mine - (mine + 1) / 2, me.DeckZone.Count);
        Assert.Equal(theirs - (theirs + 1) / 2, me.Opponent.DeckZone.Count);
        Assert.Empty(me.DiscardedEntities);
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        var shown = sent.Where(d => d.ShowEntity is { } e && e.Tags.Any(t => t.Name == 3481 && t.Value == 1)).ToList();
        Assert.Equal((mine + 1) / 2 + (theirs + 1) / 2, shown.Count);
        Assert.All(shown, d => Assert.False(string.IsNullOrEmpty(d.ShowEntity.Name)));
        int first = sent.IndexOf(shown[0]);                                                // live: both markers open the block
        Assert.Equal(HistoryMeta.Type.OVERRIDE_HISTORY, sent[first - 1].MetaData?.Type);
        Assert.Equal(HistoryMeta.Type.EFFECT_TIMING, sent[first - 2].MetaData?.Type);
    }

    [Fact]
    public void A_minion_pulled_from_the_hand_moves_like_live()
    {
        // live-20261001-questline (Ancestor's Call): ZONE_POSITION=0, then ZONE=PLAY, then its board slot; no SHOW_ENTITY
        // for its owner. Our board slot first (still in hand) plus a SHOW_ENTITY made Archwitch Willow's pull snap.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        foreach (var other in me.HandZone.ToList()) me.HandZone.Remove(other);   // the random deck may have dealt a Demon
        var demon = Give(me, "CORE_EX1_319");   // Flame Imp
        Give(me, "CS2_231");
        var willow = Give(me, "CORE_SCH_181");
        int before = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, willow)));
        Assert.Equal(SabberStoneCore.Enums.Zone.PLAY, demon.Zone.Type);
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full).Skip(0).ToList();
        var mineChanges = sent.Select((d, i) => (d, i)).Where(x => x.d.TagChange is { } t && t.Entity == demon.Id &&
            (t.Tag == (int)GameTag.ZONE || t.Tag == (int)GameTag.ZONE_POSITION)).ToList();
        int zone = mineChanges.FindLastIndex(x => x.d.TagChange.Tag == (int)GameTag.ZONE && x.d.TagChange.Value == (int)SabberStoneCore.Enums.Zone.PLAY);
        Assert.True(zone > 0, "a ZONE=PLAY change");
        Assert.Equal((int)GameTag.ZONE_POSITION, mineChanges[zone - 1].d.TagChange.Tag);
        Assert.Equal(0, mineChanges[zone - 1].d.TagChange.Value);
        Assert.True(zone + 1 < mineChanges.Count && mineChanges[zone + 1].d.TagChange.Tag == (int)GameTag.ZONE_POSITION && mineChanges[zone + 1].d.TagChange.Value > 0);
        Assert.DoesNotContain(sent.Skip(mineChanges[zone].i), d => d.ShowEntity?.Entity == demon.Id);
    }

    [Fact]
    public void A_card_in_hand_carries_its_cost_for_the_history_tile_like_live()
    {
        // live-20261002-visuals: TAG_LAST_KNOWN_COST_IN_HAND is in the card's draw SHOW_ENTITY and follows its cost in hand;
        // the client's history tile shows it (HistoryInfo.DuplicateEntity). Sent only at the play, ours showed 0.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var gnome = Give(me, "CORE_ICC_407");
        game.DeathProcessingAndAuraUpdate();
        game.PublishDerived();
        int play = game.PowerHistory.Full.Count;
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        Assert.Contains(sent, d => d.TagChange is { } t && t.Entity == gnome.Id && t.Tag == (int)GameTag.TAG_LAST_KNOWN_COST_IN_HAND && t.Value == 2
                                   || d.ShowEntity is { } e && e.Entity == gnome.Id && e.Tags.Any(x => x.Name == (int)GameTag.TAG_LAST_KNOWN_COST_IN_HAND && x.Value == 2));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, gnome)));
        Assert.DoesNotContain(game.PowerHistory.Full.Skip(play), e => e is SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: GameTag.TAG_LAST_KNOWN_COST_IN_HAND } t && t.EntityId == gnome.Id);
    }

    [Fact]
    public void Died_this_game_means_died_on_the_board()
    {
        // user rule: cards destroyed in the deck or discarded reach the graveyard without dying; resurrects skip them
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var deckMinion = me.DeckZone.GetAll().First(p => p is SabberStoneCore.Model.Entities.Minion);
        SabberStoneCore.Actions.Generic.DeckCardToGraveyard(deckMinion, 0, false);
        var discarded = Give(me, "CS2_231");
        SabberStoneCore.Actions.Generic.DiscardBlock.Invoke(me, discarded);
        var calia = Give(me, "CORE_CATA_002");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, calia)));
        Assert.Single(me.BoardZone);                                                      // only Calia: nothing died
        Assert.Empty(me.DiedThisGame);

        // a destroyed weapon died (Rummaging Kobold, Hoard Pillager); a minion that dies on the board too
        var axe = Give(me, "CS2_106");                                                    // Fiery War Axe
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, axe)));
        me.Hero.RemoveWeapon();
        Assert.Contains(axe.Id, me.DiedThisGame);
        ((SabberStoneCore.Model.Entities.Minion)calia).Destroy();
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(new[] { axe.Id, calia.Id }, me.DiedThisGame);
        Assert.DoesNotContain(deckMinion.Id, me.DiedThisGame);
    }

    [Fact]
    public void No_card_script_reads_the_graveyard_for_died_this_game()
    {
        // engine rule: "died this game" comes from Controller.DiedThisGame / EntityType.DIED_THIS_GAME only. The graveyard
        // zone also holds cards that never died (discarded, destroyed in the deck, overdrawn, cast spells).
        string dir = Path.Combine(TestEnvironment.ProjectRoot, "Server", "src", "HsServer.Rules", "src", "CardSets");
        var bad = new List<string>();
        foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimStart();
                if (line.StartsWith("//")) continue;
                bool readsGraveyard = line.Contains("GraveyardZone.Where(") || line.Contains("GraveyardZone.Count(") ||
                                      line.Contains("GraveyardZone.Any(") || line.Contains("EntityType.GRAVEYARD");
                string near = string.Join(" ", lines.Skip(i).Take(3));
                if (readsGraveyard && (near.Contains("ToBeDestroyed") || near.Contains("IsDead") || near.Contains("TO_BE_DESTROYED") ||
                                       near.Contains("is Weapon") || line.Contains("EntityType.GRAVEYARD")))
                    bad.Add($"{Path.GetFileName(file)}:{i + 1}: {line}");
            }
        }
        Assert.True(bad.Count == 0, "use DiedThisGameCards / EntityType.DIED_THIS_GAME: " + string.Join(" | ", bad));
    }

    [Fact]
    public void Shared_identities_come_from_client_tags_not_ids_or_names()
    {
        // engine audit 2026-10-02 (A1-A6): every reprint / skin / token version carries the client's tag
        _ = Db.Value;
        SabberStoneCore.Model.Card C(string id) => SabberStoneCore.Model.Cards.FromId(id);
        bool Is(SabberStoneCore.Conditions.SelfCondition cond, string id)
        {
            var game = TenManaGame();
            return cond.Eval(SabberStoneCore.Model.Entities.Entity.FromCard(game.CurrentPlayer, C(id)));
        }
        Assert.True(Is(SabberStoneCore.Conditions.SelfCondition.IsSilverHandRecruit, "CS2_101t"));   // by its client tag 3444
        Assert.True(Is(SabberStoneCore.Conditions.SelfCondition.IsTreant, "UNG_111t1"));            // Mana Treant
        Assert.True(Is(SabberStoneCore.Conditions.SelfCondition.IsCthun, "WON_135"));               // the reprint
        Assert.False(Is(SabberStoneCore.Conditions.SelfCondition.IsTreant, "CS2_231"));

        // Dormant for N turns: the card's own line; inside another clause it's about something else
        Assert.Equal(2, C("BT_934").DormantTurns);
        Assert.Equal(2, C("CORE_BT_156").DormantTurns);
        // Dormant inside another clause (Battlecry: OTHER minions go Dormant; Deathrattle: Go Dormant; a spell's target):
        // never the card itself. The engine's card list doesn't carry every client card: check those it has.
        var clauses = new[] { "CRED_163", "BTA_BOSS_16te2", "GDB_851b", "JAIL_850", "Story_04_ArthasMinion" }
            .Select(id => { try { return C(id); } catch (KeyNotFoundException) { return null; } }).Where(c => c != null).ToList();
        Assert.NotEmpty(clauses);
        Assert.All(clauses, c => Assert.Equal(0, c.DormantTurns));

        // Justicar: the current hero powers name their upgrade (UPGRADED_HERO_POWER); the engine's 2021 basic power gets the
        // upgrade of its hero's current power in the client (HERO_02 → HERO_02bp → HERO_02bp2)
        Assert.Equal("HERO_02bhp2", SabberStoneCore.Model.Cards.FromAssetId(C("HERO_02bhp")[SabberStoneCore.Enums.HsTag.UPGRADED_HERO_POWER]).Id);
        Assert.Equal("HERO_02bp2", SabberStoneCore.Loader.ReprintCards.Current(SabberStoneCore.Model.Cards.FromAssetId(SabberStoneCore.Loader.ReprintCards.UpgradedHeroPower(C("CS2_049"), C("HERO_02")))).Id);

        // the "Elemental last turn" glow comes from the script's condition (TRL_313's text has a line break)
        var g = TenManaGame();
        var me = g.CurrentPlayer;
        var shard = Give(me, "TRL_313");
        var fireball = Give(me, "CS2_029");
        me[GameTag.NUM_ELEMENTAL_PLAYED_LAST_TURN] = 1;
        me.NumElementalsPlayedLastTurn = 1;
        g.PublishDerived();
        bool Lit(SabberStoneCore.Model.Entities.IPlayable p) => g.PowerHistory.Full.OfType<SabberStoneCore.Kettle.PowerHistoryTagChange>()
            .Any(t => t.EntityId == p.Id && t.Tag == GameTag.ELEMENTAL_POWERED_UP && t.Value == 1);
        Assert.True(Lit(shard));
        Assert.False(Lit(fireball));
    }

    [Fact]
    public void Every_elemental_last_turn_card_glows_from_its_script()
    {
        // audit A6: the ELEMENTAL_POWERED_UP glow comes from the script; every card whose text says it must use the shared
        // condition (or say why not)
        _ = Db.Value;
        var g = TenManaGame();
        var me = g.CurrentPlayer;
        me.NumElementalsPlayedLastTurn = 1;
        var missing = new List<string>();
        foreach (var card in SabberStoneCore.Model.Cards.All.Where(c => System.Text.RegularExpressions.Regex.Replace(c.Text ?? "", @"\s+", " ")
                     .Contains("Elemental last turn", StringComparison.Ordinal) && c.Collectible &&
                     (c.Power?.PowerTask != null || c.Power?.Aura != null || c.Power?.Trigger != null))) // unscripted placeholders: no effect, no glow
        {
            var p = Give(me, card.Id);
            g.PublishDerived();
            if (!g.PowerHistory.Full.OfType<SabberStoneCore.Kettle.PowerHistoryTagChange>()
                    .Any(t => t.EntityId == p.Id && t.Tag == GameTag.ELEMENTAL_POWERED_UP && t.Value == 1))
                missing.Add(card.Id);
            me.HandZone.Remove(p);
        }
        Assert.True(missing.Count == 0, "no glow: " + string.Join(", ", missing));
    }

    [Fact]
    public void Effect_timing_is_the_cards_own_like_live()
    {
        // audit D1/D2: EFFECT_TIMING / OVERRIDE_HISTORY are per-card on live (RecordedBlockMeta.tsv): Fan of Knives opens
        // its effect with EFFECT_TIMING (live-20261001-1624), Fireball has none
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var fan = Give(me, "EX1_129");
        var fireball = Give(me, "CS2_029");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, fan)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, fireball, me.Opponent.Hero)));
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        int Power(int source) => sent.FindIndex(d => d.PowerStart is { } p && p.Source == source && p.Type == HistoryBlock.Type.POWER);
        int fanPower = Power(fan.Id), firePower = Power(fireball.Id);
        Assert.Equal(HistoryMeta.Type.EFFECT_TIMING, sent[fanPower + 1].MetaData?.Type);
        Assert.Equal(new List<int> { fan.Id }, sent[fanPower + 1].MetaData.Info);
        int end = sent.FindIndex(firePower, d => d.PowerEnd != null);
        Assert.DoesNotContain(sent.Skip(firePower).Take(end - firePower), d => d.MetaData?.Type == HistoryMeta.Type.EFFECT_TIMING);
    }

    [Fact]
    public void A_draw_effect_that_targets_itself_names_itself_before_the_hidden_card_moves()
    {
        // User (2026-10-03): the game froze after every Chillfallen Baron. Client dev fatal "SpellCardTypeAction.OnEnter() -
        // unknown type INVALID on [id=16 zone=DECK]": without a META_DATA TARGET, the card's FX target the first entity whose
        // tags change in the block, the hidden drawn card. Live opens the Baron's blocks with TARGET = the Baron
        // (location-20260930; RecordedBlockMeta.tsv TARGET@start).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var baron = Give(me, "RLK_708");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, baron)));
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        int power = sent.FindIndex(d => d.PowerStart is { } p && p.Source == baron.Id && p.Type == HistoryBlock.Type.POWER);
        Assert.True(power >= 0);
        Assert.Equal(HistoryMeta.Type.TARGET, sent[power + 1].MetaData?.Type);
        Assert.Equal(new List<int> { baron.Id }, sent[power + 1].MetaData.Info);
        // Arcane Intellect too; a plain draw spell without it (Sprint? no recording) gets none
        var intellect = Give(me, "CS2_023");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, intellect)));
        sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        power = sent.FindIndex(d => d.PowerStart is { } p && p.Source == intellect.Id && p.Type == HistoryBlock.Type.POWER);
        Assert.Equal(new List<int> { intellect.Id }, sent[power + 1].MetaData?.Info);
    }

    [Fact]
    public void Recruit_and_varian_put_a_drawn_minion_straight_from_deck_into_play()
    {
        // Live Guild Recruiter (live-20261004-night): the deck minion goes straight to PLAY in the Battlecry block, never to
        // the hand. Varian Wrynn the same for each minion he draws (user 2026-10-05); a spell, or no room, is a normal draw
        // (rule book: "the minion is moved to your hand instead"), and DC5: what a drawn card draws itself isn't his.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        bool WentToHand(SabberStoneCore.Model.Entities.IPlayable p, int from) => game.PowerHistory.Full.Skip(from)
            .OfType<SabberStoneCore.Kettle.PowerHistoryTagChange>().Any(t => t.EntityId == p.Id && t.Tag == GameTag.ZONE && t.Value == (int)SabberStoneCore.Enums.Zone.HAND);

        while (me.DeckZone.Count > 0) me.DeckZone.Remove(me.DeckZone[0]); // Recruit picks at random in the deck
        var yeti = OnDeckTop(me, "CS2_182");
        int from = game.PowerHistory.Full.Count;
        Play(game, me, "LOOT_375"); // Guild Recruiter: recruit a minion that costs (4) or less
        Assert.Equal(SabberStoneCore.Enums.Zone.PLAY, yeti.Zone?.Type);
        Assert.False(WentToHand(yeti, from));

        var ogre = OnDeckTop(me, "CS2_200");     // drawn third: a minion
        var bolt = OnDeckTop(me, "CS2_024");     // drawn second: a spell → hand
        var raptor = OnDeckTop(me, "CS2_172");   // drawn first: a minion
        int drawn = me.NumCardsDrawnThisTurn;
        from = game.PowerHistory.Full.Count;
        Play(game, me, "AT_072");
        Assert.Equal(SabberStoneCore.Enums.Zone.PLAY, raptor.Zone?.Type);
        Assert.Equal(SabberStoneCore.Enums.Zone.PLAY, ogre.Zone?.Type);
        Assert.False(WentToHand(raptor, from));
        Assert.Equal(SabberStoneCore.Enums.Zone.HAND, bolt.Zone?.Type);
        Assert.Equal(drawn + 3, me.NumCardsDrawnThisTurn); // all three are draws

        // no room: drawn to the hand instead
        while (!me.BoardZone.IsFull) OnBoard(game, me, "CS2_182");
        var late = OnDeckTop(me, "CS2_172");
        SabberStoneCore.Actions.Generic.DrawIntoPlay(me, me.Hero);
        Assert.Equal(SabberStoneCore.Enums.Zone.HAND, late.Zone?.Type);
    }

    [Fact]
    public void Choose_one_sends_the_chosen_option_on_its_play_and_power_blocks()
    {
        // Live (live-20261004-night): Cenarius's Shan'do's Lesson (2nd option) = SubOption 1 on its PLAY and POWER blocks,
        // Living Roots' damage (1st) = 0; the client plays that option's effect from m_SubOptionEffectDefs. Ours sent -1.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var cenarius = Give(me, "EX1_573");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, cenarius, chooseOne: 2)));
        me.UsedMana = 0;
        var yeti = Give(me, "CS2_182");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, yeti)));
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        var cenariusBlocks = sent.Where(d => d.PowerStart is { } p && p.Source == cenarius.Id).Select(d => d.PowerStart).ToList();
        Assert.Contains(cenariusBlocks, p => p.Type == HistoryBlock.Type.PLAY && p.SubOption == 1);
        Assert.Contains(cenariusBlocks, p => p.Type == HistoryBlock.Type.POWER && p.SubOption == 1);
        Assert.All(cenariusBlocks.Where(p => p.Type == HistoryBlock.Type.TRIGGER), p => Assert.Equal(-1, p.SubOption));
        Assert.All(sent.Where(d => d.PowerStart is { } p && p.Source == yeti.Id).Select(d => d.PowerStart), p => Assert.Equal(-1, p.SubOption));
        Assert.Equal(2, me.BoardZone.Count(m => m.Card.Id == "EX1_573t")); // the Treants (the option itself)
    }

    [Fact]
    public void A_drawing_block_with_a_buff_impact_effect_names_its_recorded_target()
    {
        // User (2026-10-04): Defrost froze the client 3 times (ReconnectIfStuck in its PowerSpellController): its client effect
        // is a BuffImpact (ReuseFX_Frost_DeathKnight_BuffImpact_CardDraw_Super) that needs a TARGET. Unrecorded then, the rule
        // (Loader.BlockMeta.HasImpactFx) named the card itself; live has since recorded it naming the hero.
        var game = DeathKnightGame();
        var me = game.CurrentPlayer;
        me.GainCorpses(4);
        var defrost = Give(me, "LEG_RLK_101");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, defrost)));
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        int power = sent.FindIndex(d => d.PowerStart is { } p && p.Source == defrost.Id && p.Type == HistoryBlock.Type.POWER);
        Assert.True(power >= 0);
        // live (live-20261007-zilliax-etc-remix: Defrost cast) opens it with TARGET = the caster's hero (RecordedBlockMeta
        // TARGETHERO), not the card: its BuffImpact lands there
        Assert.Equal(HistoryMeta.Type.TARGET, sent[power + 1].MetaData?.Type);
        Assert.Equal(new List<int> { me.Hero.Id }, sent[power + 1].MetaData.Info);
        // a draw whose client effect isn't a BuffImpact gets none (Sprint)
        me.UsedMana = 0;
        var sprint = Give(me, "CS2_077");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, sprint)));
        sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        power = sent.FindIndex(d => d.PowerStart is { } p && p.Source == sprint.Id && p.Type == HistoryBlock.Type.POWER);
        Assert.NotEqual(HistoryMeta.Type.TARGET, sent[power + 1].MetaData?.Type);
    }

    [Fact]
    public void Deck_cards_stay_hidden_but_show_their_id_when_they_leave_the_deck()
    {
        // audit B2: the engine keeps deck cards' ids (the stream hides them); a card that leaves the deck to a visible zone
        // then shows its real id. Overdraw burn: the burned card is shown to both players.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        while (me.HandZone.Count < 10) Give(me, "CS2_231");
        var top = me.DeckZone.TopCard;
        SabberStoneCore.Actions.Generic.Draw(me);
        Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, top.Zone.Type);
        foreach (int viewer in new[] { 1, 2 })
        {
            var sent = new ClientStream(viewer, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
            // a card created in a deck reaches nobody with its id (a mulliganed card the owner saw is another matter)
            Assert.DoesNotContain(sent, d => d.FullEntity is { } e && e.Name != "" &&
                                             e.Tags.Any(t => t.Name == (int)GameTag.ZONE && t.Value == (int)SabberStoneCore.Enums.Zone.DECK));
            // the burned card is shown with its id
            Assert.Contains(sent, d => (d.ShowEntity ?? d.FullEntity) is { } e && e.Entity == top.Id && e.Name == top.Card.Id);
        }
    }

    [Fact]
    public void Force_play_lands_right_of_a_living_summoner_and_far_right_from_a_deathrattle()
    {
        // Advanced rulebook "Where do Minions summoned by Deathrattles spawn?": Meat Wagon (and Giant Anaconda, Deathwing
        // Dragonlord) "also summon at the right"; a living summoner's Force Play lands right of it (live Archwitch Willow)
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var left = (SabberStoneCore.Model.Entities.Minion)Give(me, "CS2_231");
        var right = (SabberStoneCore.Model.Entities.Minion)Give(me, "CS2_231");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, left)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, right)));
        var demon = Give(me, "CORE_EX1_319");                                            // Flame Imp, in hand
        var willow = (SabberStoneCore.Model.Entities.Minion)Give(me, "CORE_SCH_181");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, willow, null, 1)));   // between the two
        Assert.Equal(willow.ZonePosition + 1, demon.ZonePosition);                        // right of Willow, not far right
        Assert.NotEqual(me.BoardZone.Count - 1, demon.ZonePosition);

        // a Deathrattle pulling a minion from the hand: far right (Giant Anaconda: "a minion from your hand with 5+ Attack")
        var game2 = TenManaGame();
        var p = game2.CurrentPlayer;
        var anaconda = (SabberStoneCore.Model.Entities.Minion)Give(p, "UNG_086");
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(p, anaconda, null, 0)));
        var wisps = new[] { Give(p, "CS2_231"), Give(p, "CS2_231") };
        foreach (var w in wisps) Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(p, w)));
        Give(p, "CS2_213");                                                               // Reckless Rocketeer 5/2, in hand
        var before = p.BoardZone.GetAll().ToList();
        anaconda.Destroy();
        game2.DeathProcessingAndAuraUpdate();
        // (the Anaconda picks any 5+ Attack minion in hand at random)
        var pulled = Assert.Single(p.BoardZone.GetAll(), m => !before.Contains(m));
        Assert.Equal(p.BoardZone.Count - 1, pulled.ZonePosition);                         // far right, not the Anaconda's slot 0
    }

    [Fact]
    public void Swaps_move_the_same_cards_and_leave_no_ghosts()
    {
        // audit B1 / user rule: a swap is a move (same entities, new owner: Generic.GiveTo), not copies; the old owner no
        // longer sees a hand card it gave away
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var theotar = Give(me, "CORE_REV_238");
        var mineBefore = me.HandZone.GetAll().Where(p => p != theotar).ToList();
        var theirsBefore = me.Opponent.HandZone.GetAll().ToList();
        int myCount = mineBefore.Count, theirCount = theirsBefore.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, theotar, null, 0)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, me.Choice.Choices[0])));   // a Discover in each hand
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, me.Choice.Choices[0])));
        Assert.Equal(myCount, me.HandZone.Count);
        Assert.Equal(theirCount, me.Opponent.HandZone.Count);
        var given = Assert.Single(mineBefore, p => p.Controller == me.Opponent);              // one of mine is theirs now
        var taken = Assert.Single(theirsBefore, p => p.Controller == me);
        Assert.Contains(given, me.Opponent.HandZone.GetAll());
        Assert.Contains(taken, me.HandZone.GetAll());
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        Assert.Contains(sent, d => d.HideEntity?.Entity == given.Id);                          // gone from my view
        Assert.Contains(sent, d => d.ShowEntity is { } e && e.Entity == taken.Id && e.Name == taken.Card.Id);

        // Mischief Maker: the two top cards trade decks, sizes kept
        var g2 = TenManaGame();
        var p2 = g2.CurrentPlayer;
        SabberStoneCore.Model.Entities.IPlayable top = p2.DeckZone.TopCard, theirTop = p2.Opponent.DeckZone.TopCard;
        int deck = p2.DeckZone.Count, theirDeck = p2.Opponent.DeckZone.Count;
        Assert.True(g2.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(p2, Give(p2, "ULD_229"), null, 0)));
        Assert.Equal((deck, theirDeck), (p2.DeckZone.Count, p2.Opponent.DeckZone.Count));
        Assert.Same(theirTop, p2.DeckZone.TopCard);
        Assert.Same(top, p2.Opponent.DeckZone.TopCard);
    }

    [Fact]
    public void A_destroy_follows_the_rule_of_each_zone()
    {
        // Advanced rulebook "Destroy effects in all zones" (audit B3): in play pending destroy; in hand a discard; in the
        // deck to the graveyard (revealed), nothing else. Flik Skyshiv destroys the copies everywhere.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        foreach (var other in me.HandZone.ToList()) me.HandZone.Remove(other);
        Play(game, me, "CS2_231");
        var onBoard = me.BoardZone.GetAll().Single(m => m.Card.Id == "CS2_231");
        var inHand = Give(me, "CS2_231");
        var inDeck = SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("CS2_231"));
        SabberStoneCore.Actions.Generic.ShuffleIntoDeck.Invoke(me, null, inDeck);
        Play(game, me, "DRG_037", onBoard);
        Assert.All(new[] { (SabberStoneCore.Model.Entities.IPlayable)onBoard, inHand, inDeck },
            p => Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, p.Zone.Type));
        Assert.Contains(inHand.Id, me.DiscardedEntities);                                     // hand: counts as a discard
        Assert.DoesNotContain(inDeck.Id, me.DiscardedEntities);                               // deck: not a discard
        Assert.DoesNotContain(inHand.Id, me.DiedThisGame);                                    // neither died
        Assert.DoesNotContain(inDeck.Id, me.DiedThisGame);
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        Assert.Contains(sent, d => d.ShowEntity is { } e && e.Entity == inDeck.Id && e.Name == "CS2_231");

        // Hemet: the destroyed deck cards go to the graveyard (were set aside)
        var g2 = TenManaGame();
        var p2 = g2.CurrentPlayer;
        var cheap = p2.DeckZone.Where(p => p.Cost <= 3).ToList();
        Play(g2, p2, "UNG_840");
        Assert.All(cheap, p => Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, p.Zone.Type));
        Assert.DoesNotContain(p2.DeckZone, p => p.Cost <= 3);

        // Myra's Unstable Element draws the whole deck; past a full hand a draw burns the card (rulebook DC4), none set aside
        var g3 = TenManaGame();
        var p3 = g3.CurrentPlayer;
        var deck = p3.DeckZone.ToList();
        Play(g3, p3, "BOT_242");
        Assert.Empty(p3.DeckZone);
        Assert.All(deck, p => Assert.NotEqual(SabberStoneCore.Enums.Zone.SETASIDE, p.Zone.Type));
        Assert.Contains(deck, p => p.Zone.Type == SabberStoneCore.Enums.Zone.GRAVEYARD);
    }

    [Fact]
    public void Discover_from_the_deck_offers_copies_and_draws_the_pick_like_live()
    {
        // live Tracking (live-20261001-auchenai, practice-innkeeper-20260929-1x-spells; audit B5): the options are new
        // set-aside copies linked to their deck cards, the deck is untouched; the pick is a real draw of the deck card and
        // the picked copy is revealed into the graveyard
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var deckBefore = me.DeckZone.ToList();
        int drawn = me.NumCardsDrawnThisTurn;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "CORE_DS1_184"))));
        Assert.Equal(deckBefore, me.DeckZone.ToList());                                       // nothing left the deck yet
        var options = me.Choice.Choices.Select(id => game.IdEntityDic[id]).ToList();
        Assert.Equal(3, options.Count);
        Assert.All(options, o => Assert.DoesNotContain(o, deckBefore));                      // copies, not the deck cards
        Assert.All(options, o => Assert.Equal(o.Card, game.IdEntityDic[o[SabberStoneCore.Enums.GameTag.LINKED_ENTITY]].Card));
        var pick = options[1];
        var linked = game.IdEntityDic[pick[SabberStoneCore.Enums.GameTag.LINKED_ENTITY]];
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, pick.Id)));
        Assert.Equal(SabberStoneCore.Enums.Zone.HAND, linked.Zone.Type);                      // the deck card, drawn
        Assert.Equal(drawn + 1, me.NumCardsDrawnThisTurn);                                    // counted as a draw
        Assert.Equal(deckBefore.Where(p => p != linked).ToList(), me.DeckZone.ToList());      // order kept
        Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, pick.Zone.Type);
        var sent = new ClientStream(2, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);       // the opponent sees the pick
        Assert.Contains(sent, d => d.ShowEntity is { } e && e.Entity == pick.Id && e.Name == pick.Card.Id);
    }

    [Fact]
    public void A_card_that_targets_the_hand_lets_the_player_pick_the_card()
    {
        // Client Entity.HasHandTarget: CAN_TARGET_CARDS_IN_HAND + hand cards in the option's targets = the client aims at the
        // hand. Bibliomite (Power.HandTarget): every other hand card is a target, a play without one is refused while there
        // is one, the picked card is shuffled into the deck (shared Generic.MoveToDeck, rule Z5a resets it).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        foreach (var other in me.HandZone.ToList()) me.HandZone.Remove(other);
        var mite = Give(me, "CORE_REV_511");
        var keep = Give(me, "CS2_231");
        var shuffle = Give(me, "CS2_182");
        var all = SabberStoneCore.Kettle.PowerOptionsBuilder.AllOptions(game, me.Options());
        var main = all.PowerOptionList.Single(o => o.MainOption?.EntityId == mite.Id).MainOption;
        Assert.Equal(new[] { keep.Id, shuffle.Id }.OrderBy(i => i), main.Targets.OrderBy(i => i));
        Assert.False(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, mite)));     // must pick
        int deck = me.DeckZone.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, mite, shuffle, 0)));
        Assert.Equal(SabberStoneCore.Enums.Zone.DECK, shuffle.Zone.Type);
        Assert.Equal(SabberStoneCore.Enums.Zone.HAND, keep.Zone.Type);
        Assert.Equal(deck + 1, me.DeckZone.Count);
        Assert.Equal(shuffle.Id, mite.CardTarget);
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        Assert.Contains(sent, d => d.PowerStart is { } b && b.Type == HistoryBlock.Type.PLAY && b.Source == mite.Id && b.Target == shuffle.Id);

        // nothing else in hand: it plays without a target
        var g2 = TenManaGame();
        var p2 = g2.CurrentPlayer;
        foreach (var other in p2.HandZone.ToList()) p2.HandZone.Remove(other);
        Assert.True(g2.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(p2, Give(p2, "CORE_REV_511"))));
    }

    [Fact]
    public void Picking_from_cards_in_hand_offers_linked_copies_like_live()
    {
        // live-20261001-startofgame (Dispose of Evidence): options = copies linked to the hand cards; after the pick the copy
        // goes to the graveyard and the hand card is shuffled into the deck; the others stay in hand.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        foreach (var other in me.HandZone.ToList()) me.HandZone.Remove(other);
        var a = Give(me, "CS2_231");
        var b = Give(me, "CS2_182");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "CORE_REV_507"))));
        var options = me.Choice.Choices.Select(id => game.IdEntityDic[id]).ToList();
        Assert.Equal(2, options.Count);
        Assert.All(options, o => Assert.Contains(o[SabberStoneCore.Enums.GameTag.LINKED_ENTITY], new[] { a.Id, b.Id }));
        var pick = options.Single(o => o[SabberStoneCore.Enums.GameTag.LINKED_ENTITY] == b.Id);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, pick.Id)));
        Assert.Equal(SabberStoneCore.Enums.Zone.DECK, b.Zone.Type);
        Assert.Equal(SabberStoneCore.Enums.Zone.HAND, a.Zone.Type);
        Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, pick.Zone.Type);

        // Theotar: a pick in each hand, then the two cards swap owners
        var g2 = TenManaGame();
        var p2 = g2.CurrentPlayer;
        foreach (var other in p2.HandZone.ToList()) p2.HandZone.Remove(other);
        var mine = Give(p2, "CS2_231");
        Assert.True(g2.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(p2, Give(p2, "CORE_REV_238"), null, 0)));
        Assert.True(g2.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(p2, p2.Choice.Choices[0])));
        Assert.NotNull(p2.Choice);                                                            // the second Discover
        var theirOption = g2.IdEntityDic[p2.Choice.Choices[0]];
        var theirs = g2.IdEntityDic[theirOption[SabberStoneCore.Enums.GameTag.LINKED_ENTITY]];
        Assert.Equal(p2.Opponent, theirs.Controller);
        Assert.True(g2.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(p2, theirOption.Id)));
        Assert.Equal(p2.Opponent, mine.Controller);
        Assert.Equal(p2, theirs.Controller);
        Assert.Equal(SabberStoneCore.Enums.Zone.HAND, theirs.Zone.Type);

        // Ghastly Gravedigger (with a Secret): 3 random cards of their hand offered, the pick shuffled into their deck
        var g3 = TenManaGame();
        var p3 = g3.CurrentPlayer;
        SabberStoneCore.Actions.Generic.PutSecretIntoPlay(p3, (SabberStoneCore.Model.Entities.Spell)SabberStoneCore.Model.Entities.Entity.FromCard(p3, SabberStoneCore.Model.Cards.FromId("EX1_610")));
        int theirHand = p3.Opponent.HandZone.Count, theirDeck = p3.Opponent.DeckZone.Count;
        Play(g3, p3, "CORE_REV_959");
        Assert.Equal(System.Math.Min(3, p3.Opponent.HandZone.Select(c => c.Card.Id).Distinct().Count()), p3.Choice.Choices.Count);
        var dug = g3.IdEntityDic[g3.IdEntityDic[p3.Choice.Choices[0]][SabberStoneCore.Enums.GameTag.LINKED_ENTITY]];
        Assert.Equal(p3.Opponent, dug.Controller);
        Assert.True(g3.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(p3, p3.Choice.Choices[0])));
        Assert.Equal(SabberStoneCore.Enums.Zone.DECK, dug.Zone.Type);
        Assert.Equal((theirHand - 1, theirDeck + 1), (p3.Opponent.HandZone.Count, p3.Opponent.DeckZone.Count));
    }

    [Theory]
    [InlineData("TTN_076")]   // Mechagnome Guide (minion)
    [InlineData("TTN_753")]   // a spell (the one forged in the local test game)
    public void Forge_transforms_the_card_in_hand_like_live(string forgeCard)
    {
        // wiki Forge + live-20261002-forge-drink (Mechagnome Guide): its own option with the FORGE keyword (unaffordable:
        // REQ_ENOUGH_MANA), 2 mana, a DECK_ACTION block, the card becomes its FORGES_INTO card in the same hand slot with
        // FORGED=1, keeps its enchantments, and isn't a play
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var guide = Give(me, forgeCard);
        string forgedId = SabberStoneCore.Actions.Generic.ForgedCard(guide).Id;
        int slot = guide.ZonePosition, played = me.PlayHistory.Count, id = guide.Id;
        SabberStoneCore.Actions.Generic.AddEnchantmentBlock(game, SabberStoneCore.Model.Cards.FromId("TTN_076e"), me.Hero, guide);
        var all = SabberStoneCore.Kettle.PowerOptionsBuilder.AllOptions(game, me.Options());
        Assert.Contains(all.PowerOptionList, o => o.MainOption?.EntityId == guide.Id && o.MainOption.PowerKeyword == 2785 && o.MainOption.PlayError == 0);
        Assert.True(game.Process(me.Options().OfType<SabberStoneCore.Tasks.PlayerTasks.ForgeTask>().Single(t => t.Source == guide)));
        var forged = game.IdEntityDic[id];
        Assert.Equal(forgedId, forged.Card.Id);
        Assert.Equal(1, forged[SabberStoneCore.Actions.Generic.FORGED]);
        Assert.Equal(slot, forged.ZonePosition);
        Assert.Equal(2, me.UsedMana);
        Assert.Equal(played, me.PlayHistory.Count);                                           // not a play
        Assert.Equal(forged.Card.Cost - 3, forged.Cost);                                      // the in-hand enchantment kept
        Assert.Empty(me.Options().OfType<SabberStoneCore.Tasks.PlayerTasks.ForgeTask>());     // forged once
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        Assert.Contains(sent, d => d.PowerStart is { } b && (int)b.Type == 13 && b.Source == id);   // DECK_ACTION
        var change = sent.Single(d => d.ChangeEntity is { } c && c.Entity == id && c.Name == forgedId).ChangeEntity;
        Assert.DoesNotContain(change.Tags, t => t.Name is (int)HsServer.Engine.GAME_TAG.ZONE or (int)HsServer.Engine.GAME_TAG.ZONE_POSITION); // live
        // live: after the transform the flags go back to 0 (the client then flies the card from the deck back to the hand)
        bool engineReset = game.PowerHistory.Full.Any(e => e is SabberStoneCore.Kettle.PowerHistoryTagChange t && t.EntityId == id
                                                           && t.Tag == SabberStoneCore.Actions.Generic.IS_USING_FORGE_OPTION && t.Value == 0);
        Assert.True(engineReset, "engine history: IS_USING_FORGE_OPTION=0");
        foreach (int tag in new[] { 2869, 3070, (int)SabberStoneCore.Enums.GameTag.JUST_PLAYED })
            Assert.Contains(sent, d => d.TagChange is { } t && t.Entity == id && t.Tag == tag && t.Value == 0);

        // not enough mana: an unusable forge option
        var g2 = TenManaGame();
        var p2 = g2.CurrentPlayer;
        var other = Give(p2, "TTN_076");
        p2.UsedMana = 9;
        var opts = SabberStoneCore.Kettle.PowerOptionsBuilder.AllOptions(g2, p2.Options());
        Assert.Contains(opts.PowerOptionList, o => o.MainOption?.EntityId == other.Id && o.MainOption.PowerKeyword == 2785
                                                   && o.MainOption.PlayError == (int)SabberStoneCore.Enums.PlayReq.REQ_ENOUGH_MANA);
    }

    [Fact]
    public void A_player_attack_through_the_adapter_runs_its_whole_attack_like_live()
    {
        // Live (3104 of 3104 attacks): CARD_TARGET on the attacker before the ATTACK block, then after it
        // NUM_OPTIONS_PLAYED_THIS_TURN and SLUSH_TIME 1000 on the attacker. The adapter's options skipped both (audit 2026-10-08).
        _ = Db.Value;
        var rng = new Random(7);
        var engine = new SabberEngineGame(new EngineSetup
        {
            Cards = Db.Value, Scenario = 260, Seed = 7, Rules = RulesEngine.Sabber,
            GameSetupBody = PegasusPackets.Serialize(new GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
            Human = new PlayerSetup { HeroCardId = "HERO_01", Deck = Enumerable.Repeat("CS2_124", 30).ToList(), AccountHi = 1, AccountLo = 2 },
            Opponent = new PlayerSetup { HeroCardId = RandomDecks.AnyHero(CardClass.MAGE, rng), Deck = RandomDecks.BuildCurrentCore(CardClass.MAGE, rng), IsAi = true },
        });
        engine.Start();
        var choices = engine.Drain().OfType<EntityChoices>().Single(c => c.PlayerId == 1);
        Assert.True(engine.ChooseEntities(choices.Id, choices.Entities));
        while (engine.AiToAct) engine.AiStep();
        engine.Rules.Player1.BaseMana = 10;
        var wolf = engine.Rules.Player1.HandZone.First(c => c.Card.Id == "CS2_124"); // Wolfrider: Charge
        Assert.True(engine.Rules.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(engine.Rules.Player1, wolf)));
        engine.Drain();
        engine.ResendOptions();
        var options = engine.Drain().OfType<AllOptions>().Last();
        int index = options.Options.FindIndex(o => o.MainOption?.Id == wolf.Id && o.MainOption.PlayError == 0);
        Assert.True(index >= 0, "the Wolfrider can attack");
        int hero = engine.Rules.Player2.Hero.Id;
        int played = engine.Rules.Player1.NumOptionsPlayedThisTurn;
        Assert.True(engine.ChooseOption(options.Id, index, hero, 0, -1));
        var sent = engine.Drain().OfType<PegasusGame.PowerHistory>().SelectMany(h => h.List).ToList();
        int attack = sent.FindIndex(d => d.PowerStart is { Type: HistoryBlock.Type.ATTACK });
        int cardTarget = sent.FindIndex(d => d.TagChange is { } t && t.Entity == wolf.Id && t.Tag == (int)HsServer.Engine.GAME_TAG.CARD_TARGET && t.Value == hero);
        Assert.True(attack >= 0 && cardTarget >= 0 && cardTarget < attack, $"CARD_TARGET {cardTarget}, ATTACK {attack}");
        Assert.Contains(sent.Skip(attack), d => d.MetaData is { Type: HistoryMeta.Type.SLUSH_TIME, Data: 1000 });
        Assert.Equal(played + 1, engine.Rules.Player1.NumOptionsPlayedThisTurn);
    }

    [Fact]
    public void A_secret_is_only_a_card_with_the_secret_keyword_not_a_sidequest_in_the_zone()
    {
        // Soak 1100840: Horde Operative ("Copy your opponent's Secrets") copied what wasn't a Secret from the Secret zone and threw. The zone
        // also holds Sidequests, Paladin Auras, Sigils, Objectives: "a Secret" is only a card with the keyword (Generic.SecretsOf).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        var side = (SabberStoneCore.Model.Entities.Spell)SabberStoneCore.Model.Entities.Entity.FromCard(op, SabberStoneCore.Model.Cards.FromId("DRG_008"));
        Assert.True(SabberStoneCore.Actions.Generic.PutSecretIntoPlay(op, side));
        Assert.False(SabberStoneCore.Actions.Generic.ControlsSecret(op));
        var trap = (SabberStoneCore.Model.Entities.Spell)SabberStoneCore.Model.Entities.Entity.FromCard(op, SabberStoneCore.Model.Cards.FromId("EX1_610"));
        Assert.True(SabberStoneCore.Actions.Generic.PutSecretIntoPlay(op, trap));
        Assert.True(SabberStoneCore.Actions.Generic.ControlsSecret(op));

        var operative = Give(me, "BAR_430");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, operative)));
        Assert.Equal(new[] { "EX1_610" }, me.SecretZone.Select(x => x.Card.Id).ToArray()); // the Secret copied, not the Sidequest
    }

    [Fact]
    public void A_history_copy_of_the_game_keeps_telling_the_client_and_hides_a_revealed_opponent_hand_card()
    {
        // The Rewind snapshot is Game.Clone(history: true): its entities must keep recording (soak 400249: after a Rewind the
        // hero's damage never reached the client).
        var game = TenManaGame();
        var copy = game.Clone(logging: false, resetRandomSeed: false, history: true);
        int start = copy.PowerHistory.Full.Count;
        copy.Player2.Hero.TakeDamage(copy.Player1.Hero, 3);
        Assert.Contains(copy.PowerHistory.Full.Skip(start), e => e is SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: GameTag.DAMAGE, Value: 3 } t
            && t.EntityId == copy.Player2.Hero.Id);

        // A SHOW_BIG_CARD of a card that stays in the opponent's hand (Call of the Grave triggering its Deathrattle) doesn't show it
        // to the other player (soak 300999).
        var theirs = game.Player2;
        var bookie = SabberStoneCore.Actions.Generic.DrawCard(theirs, SabberStoneCore.Model.Cards.FromId("JAIL_720"));
        game.PowerHistory.Add(new SabberStoneCore.Kettle.PowerHistoryMetaData { Type = MetaDataType.SHOW_BIG_CARD, Data = 0, Info = new List<int> { bookie.Id, 1000 } });
        var seen = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        Assert.DoesNotContain(seen, d => d.ShowEntity?.Entity == bookie.Id || d.FullEntity is { Name: "JAIL_720" } f && f.Entity == bookie.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Rewind_resets_the_game_and_replays_the_card_like_live(bool rewind)
    {
        // live (live-20261006-darkmoon 35312-38031): after the effect, EntityChoices [Keep Timeline, Rewind Timeline]; Rewind: one GAME_RESET
        // block (RESET_GAME SourceIsRewind, FULL_ENTITYs), AllOptions, then the card played again (USED_REWIND 1, no second prompt)
        _ = Db.Value;
        var rng = new Random(9);
        var engine = new SabberEngineGame(new EngineSetup
        {
            Cards = Db.Value, Scenario = 260, Seed = 9, Rules = RulesEngine.Sabber,
            GameSetupBody = PegasusPackets.Serialize(new GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
            Human = new PlayerSetup { HeroCardId = "HERO_06", Deck = Enumerable.Repeat("TIME_441", 30).ToList(), AccountHi = 1, AccountLo = 2 },
            Opponent = new PlayerSetup { HeroCardId = RandomDecks.AnyHero(CardClass.MAGE, rng), Deck = RandomDecks.BuildCurrentCore(CardClass.MAGE, rng), IsAi = true },
        });
        engine.Start();
        var choices = engine.Drain().OfType<EntityChoices>().Single(c => c.PlayerId == 1);
        Assert.True(engine.ChooseEntities(choices.Id, choices.Entities));
        while (engine.AiToAct) engine.AiStep();
        engine.Rules.Player1.BaseMana = 10;
        engine.Drain();
        engine.ResendOptions();
        var options = engine.Drain().OfType<AllOptions>().Last();
        var rend = engine.Rules.Player1.HandZone.First(c => c.Card.Id == "TIME_441");
        int index = options.Options.FindIndex(o => o.MainOption?.Id == rend.Id && o.MainOption.PlayError == 0);
        Assert.True(index >= 0);
        Assert.True(engine.ChooseOption(options.Id, index, 0, 0, -1));
        var prompt = engine.Drain().OfType<EntityChoices>().Single(c => c.PlayerId == 1);
        var ids = prompt.Entities.Select(id => engine.Rules.IdEntityDic[id].Card.Id).ToList();
        Assert.Equal(new[] { "TIME_000ta", "TIME_000tb" }, ids);
        Assert.True(engine.ChooseEntities(prompt.Id, new List<int> { prompt.Entities[rewind ? 1 : 0] }));
        var after = engine.Drain();
        var history = after.OfType<PegasusGame.PowerHistory>().SelectMany(h => h.List).ToList();
        int reset = history.FindIndex(d => d.PowerStart is { Type: HistoryBlock.Type.GAME_RESET });
        if (!rewind)
        {
            Assert.Equal(-1, reset);
            return;
        }
        Assert.True(reset >= 0, "a GAME_RESET block");
        Assert.True(history[reset + 1].ResetGame is { SourceIsRewind: true });
        // (2026-10-09) the reset re-sends every entity's tags: never an engine-internal one (>= 900000; the players' were all sent)
        Assert.DoesNotContain(history.Skip(reset), d => (d.FullEntity?.Tags ?? new List<PegasusGame.Tag>())
            .Concat(d.ResetGame?.CreateGame?.Players.SelectMany(p => p.Entity.Tags) ?? Enumerable.Empty<PegasusGame.Tag>()).Any(t => t.Name >= 900000));
        Assert.Contains(after, p => p is AllOptions);
        Assert.DoesNotContain(after.OfType<EntityChoices>(), c => c.PlayerId == 1); // no second prompt
        var replayed = engine.Rules.IdEntityDic[rend.Id];
        Assert.Equal(1, replayed[SabberStoneCore.Actions.Generic.USED_REWIND]);
        Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, replayed.Zone.Type);
        Assert.Equal(4, engine.Rules.Player1.UsedMana); // spent once (the rewound play's mana came back)
    }

    [Fact]
    public void Forge_through_the_adapter_sends_the_flag_resets()
    {
        // local-test-20261002-forge-drink: the client got the CHANGE_ENTITY but no IS_USING_FORGE_OPTION / JUST_PLAYED /
        // FORGE_REVEALED = 0 afterwards (live sends them: the card then flies from the deck back to the hand)
        _ = Db.Value;
        var rng = new Random(5);
        var engine = new SabberEngineGame(new EngineSetup
        {
            Cards = Db.Value, Scenario = 260, Seed = 5, Rules = RulesEngine.Sabber,
            GameSetupBody = PegasusPackets.Serialize(new GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
            Human = new PlayerSetup { HeroCardId = "HERO_01", Deck = Enumerable.Repeat("TTN_753", 30).ToList(), AccountHi = 1, AccountLo = 2 },
            Opponent = new PlayerSetup { HeroCardId = RandomDecks.AnyHero(CardClass.MAGE, rng), Deck = RandomDecks.BuildCurrentCore(CardClass.MAGE, rng), IsAi = true },
        });
        engine.Start();
        var choices = engine.Drain().OfType<EntityChoices>().Single(c => c.PlayerId == 1);
        Assert.True(engine.ChooseEntities(choices.Id, choices.Entities));
        while (engine.AiToAct) engine.AiStep();
        engine.Rules.Player1.BaseMana = 10;
        engine.Drain();
        engine.ResendOptions();
        var options = engine.Drain().OfType<AllOptions>().Last();
        int index = options.Options.FindIndex(o => o.MainOption?.PowerKeyword == 2785 && o.MainOption.PlayError == 0);
        Assert.True(index >= 0, "a usable forge option");
        int card = options.Options[index].MainOption.Id;
        Assert.True(engine.ChooseOption(options.Id, index, 0, 0, -1));
        var sent = engine.Drain().OfType<PegasusGame.PowerHistory>().SelectMany(h => h.List).ToList();
        Assert.Contains(sent, d => d.ChangeEntity is { } c && c.Entity == card);
        foreach (int tag in new[] { 2869, 3070, (int)HsServer.Engine.GAME_TAG.JUST_PLAYED })
            Assert.Contains(sent, d => d.TagChange is { } t && t.Entity == card && t.Tag == tag && t.Value == 0);
    }

    [Fact]
    public void Excavate_climbs_the_rarities_and_resets_like_live()
    {
        // wiki Excavate: common, rare, epic, then (7 classes) the class Legendary, then common again; live
        // (live-20261002-forge-drink, Reinforced Plating): MAX_EXCAVATE_TIER 3 for Warrior (2 for Hunter), the treasure in
        // hand from inside a SUB_SPELL whose Source is the player
        _ = Db.Value;
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.WARRIOR, Player2HeroClass = CardClass.HUNTER,
            FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false,
        });
        game.StartGame();
        var me = game.CurrentPlayer;
        Assert.Equal(3, me[SabberStoneCore.Actions.Generic.MAX_EXCAVATE_TIER]);
        Assert.Equal(2, me.Opponent[SabberStoneCore.Actions.Generic.MAX_EXCAVATE_TIER]);
        var rarities = new List<SabberStoneCore.Enums.Rarity>();
        int firstPlate = 0;
        for (int i = 0; i < 5; i++)
        {
            foreach (var other in me.HandZone.ToList()) me.HandZone.Remove(other);
            me.BaseMana = 10; me.UsedMana = 0;
            var plate = Give(me, "WW_334");
            if (i == 0) firstPlate = plate.Id;
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, plate)));
            var treasure = Assert.Single(me.HandZone.GetAll());
            Assert.True(treasure.Card[SabberStoneCore.Actions.Generic.EXCAVATE_TREASURE] > 0);
            Assert.Equal(plate.Id, treasure[SabberStoneCore.Enums.GameTag.CREATOR]);
            rarities.Add(treasure.Card.Rarity);
            if (treasure.Card.Rarity == SabberStoneCore.Enums.Rarity.LEGENDARY) Assert.Equal(CardClass.WARRIOR, treasure.Card.Class);
        }
        Assert.Equal(new[] { SabberStoneCore.Enums.Rarity.COMMON, SabberStoneCore.Enums.Rarity.RARE, SabberStoneCore.Enums.Rarity.EPIC,
            SabberStoneCore.Enums.Rarity.LEGENDARY, SabberStoneCore.Enums.Rarity.COMMON }, rarities);
        Assert.Equal(5, me[SabberStoneCore.Actions.Generic.EXCAVATES_THIS_GAME]);
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        Assert.Contains(sent, d => d.SubSpellStart is { } s && s.SpellPrefabGuid.StartsWith("WWFX_Excavate_SpawnToHand_Common_Super:") && s.SourceEntityId == me.Id);
        Assert.Equal(30, me.Hero.Armor);                                                      // 5 x 6 Armor
    }

    [Fact]
    public void A_drink_spell_leaves_its_next_drink_in_the_same_slot_like_live()
    {
        // live-20261002-forge-drink (Cup o' Muscle): the client's ReplacementsWhenPlayed chain VAC_338 → VAC_338t → VAC_338t2;
        // the next drink is a new card in the played card's slot (CREATOR, CREATED_AS_ON_PLAY_REPLACEMENT) before the effect
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        foreach (var other in me.HandZone.ToList()) me.HandZone.Remove(other);
        Give(me, "CS2_231");
        var minion = Give(me, "CS2_182");
        var cup = Give(me, "VAC_338");
        int slot = cup.ZonePosition;
        foreach (string next in new[] { "VAC_338t", "VAC_338t2", null })
        {
            me.UsedMana = 0;
            int atk = me.HandZone.GetAll().Where(p => p is SabberStoneCore.Model.Entities.Minion).Sum(p => ((SabberStoneCore.Model.Entities.Minion)p).AttackDamage);
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, cup)));
            Assert.Equal(atk + 2, me.HandZone.GetAll().Where(p => p is SabberStoneCore.Model.Entities.Minion).Sum(p => ((SabberStoneCore.Model.Entities.Minion)p).AttackDamage));
            var refill = me.HandZone.GetAll().FirstOrDefault(p => p.Card.Name == "Cup o' Muscle");
            if (next == null) { Assert.Null(refill); break; }                                 // the last drink
            Assert.Equal(next, refill.Card.Id);
            Assert.Equal(slot, refill.ZonePosition);
            Assert.Equal(cup.Id, refill[SabberStoneCore.Enums.GameTag.CREATOR]);
            Assert.Equal(1, refill[SabberStoneCore.Enums.HsTag.CREATED_AS_ON_PLAY_REPLACEMENT]);
            cup = refill;
        }
    }

    [Fact]
    public void Renounce_darkness_keeps_the_non_warlock_deck_cards()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        int deck = me.DeckZone.Count;
        var renounce = Give(me, "OG_118");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, renounce)));
        Assert.Equal(deck, me.DeckZone.Count);                                                 // replaced, never lost
    }

    [Fact]
    public void Kezan_mystic_follows_rule_z3()
    {
        // Advanced rulebook Z3: "If Kezan Mystic tries to steal a Secret but none can be stolen (because your Secret Zone is
        // full or you already have a copy of all enemy Secrets), a random Secret is destroyed and revealed." Example: both
        // have Mirror Entity, the enemy also Duplicate → Duplicate is stolen with 100%.
        SabberStoneCore.Model.Entities.Spell Up(SabberStoneCore.Model.Entities.Controller c, string id)
        {
            var secret = (SabberStoneCore.Model.Entities.Spell)SabberStoneCore.Model.Entities.Entity.FromCard(c, SabberStoneCore.Model.Cards.FromId(id));
            Assert.True(SabberStoneCore.Actions.Generic.PutSecretIntoPlay(c, secret));
            return secret;
        }
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        // (Secrets that ignore a minion being played: Kezan is one)
        Up(me, "EX1_295");                                                                     // my Ice Block
        var theirMirror = Up(me.Opponent, "EX1_295");                                         // their Ice Block: a copy
        var duplicate = Up(me.Opponent, "EX1_289");                                           // their Ice Barrier: stealable
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, Give(me, "GVG_074"), null, 0)));
        Assert.Same(me, duplicate.Controller);                                                 // the same Secret, stolen
        Assert.Contains(duplicate, me.SecretZone.GetAll());
        Assert.Contains(theirMirror, me.Opponent.SecretZone.GetAll());

        // only a copy left: destroyed and revealed
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, Give(me, "GVG_074"), null, 0)));
        Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, theirMirror.Zone.Type);
        Assert.Equal(1, theirMirror[GameTag.REVEALED]);
    }

    [Fact]
    public void Low_security_wing_flies_its_card_in_like_live()
    {
        // Live (location-20260930): the Wing's POWER block opens SUB_SPELL ReuseFX_Generic_SpawnToHand_NoFX_Super
        // (Source = the Wing, no EFFECT_TIMING before it) and the new card's FULL_ENTITY in HAND is inside it: the client
        // flies the card from the Location into the hand. The prefab is server-picked (RecordedSubSpells.tsv).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var wing = Give(me, "JAIL_987");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, wing, null, 0)));
        int handBefore = me.HandZone.Count;
        Assert.True(game.Process(me.Options().OfType<SabberStoneCore.Tasks.PlayerTasks.LocationTask>().First()));
        Assert.Equal(handBefore + 1, me.HandZone.Count);
        var card = me.HandZone[me.HandZone.Count - 1];

        var h = game.PowerHistory.Last;
        int sub = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistorySubSpellStart s && s.Prefab.StartsWith("ReuseFX_Generic_SpawnToHand_NoFX_Super:"));
        Assert.True(sub >= 0);
        Assert.Equal(wing.Id, ((SabberStoneCore.Kettle.PowerHistorySubSpellStart)h[sub]).Source);
        int full = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryFullEntity f && f.Entity.Id == card.Id);
        int end = h.FindIndex(sub, e => e is SabberStoneCore.Kettle.PowerHistorySubSpellEnd);
        Assert.True(sub < full && full < end, $"SUB_SPELL {sub}, FULL_ENTITY {full}, END {end}");

        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        int sentSub = sent.FindLastIndex(d => d.SubSpellStart is { } s && s.SpellPrefabGuid.StartsWith("ReuseFX_Generic_SpawnToHand_NoFX_Super:"));
        Assert.Null(sent[sentSub - 1].MetaData); // live: no EFFECT_TIMING before it
    }

    [Fact]
    public void Zuramats_prison_plays_its_own_effects_like_live()
    {
        // Live (location-20260930): the Prison's POWER block holds EFFECT_TIMING, then SUB_SPELL CustomDiscard_Missile
        // (target: the discarded card) around the discard, then SUB_SPELL OverrideSpawn around the 3/3's summon. Its
        // Deathrattle's TRIGGER block holds SUB_SPELL Deathrattle around Zuramat's summon. Prefabs from the client CardDef.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var prison = (SabberStoneCore.Model.Entities.Location)Give(me, "JAIL_887");
        var fodder = Give(me, "CS2_231");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, prison, null, 0)));
        Assert.True(game.Process(me.Options().OfType<SabberStoneCore.Tasks.PlayerTasks.LocationTask>().First(u => u.ActivationTarget?.Id == fodder.Id)));

        var h = game.PowerHistory.Last;
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        int timing = sent.FindLastIndex(d => d.MetaData is { Type: HistoryMeta.Type.EFFECT_TIMING } md && md.Info.Contains(prison.Id));
        int sentMissile = sent.FindLastIndex(d => d.SubSpellStart is { } s && s.SpellPrefabGuid.StartsWith("JAILFX_ZuramatsPrison_CustomDiscard_Missile_Super:"));
        Assert.True(timing >= 0 && timing == sentMissile - 1, $"EFFECT_TIMING {timing} before the missile {sentMissile}");
        int missile = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistorySubSpellStart s && s.Prefab.StartsWith("JAILFX_ZuramatsPrison_CustomDiscard_Missile_Super:"));
        int discard = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: SabberStoneCore.Enums.GameTag.ZONE } t && t.EntityId == fodder.Id);
        int missileEnd = h.FindIndex(missile, e => e is SabberStoneCore.Kettle.PowerHistorySubSpellEnd);
        int spawn = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistorySubSpellStart s && s.Prefab.StartsWith("JAILFX_ZuramatsPrison_OverrideSpawn_Super:"));
        var whisper = me.BoardZone.First(m => m.Card.Id == "JAIL_887t3");
        int created = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryFullEntity f && f.Entity.Id == whisper.Id);
        int spawnEnd = h.FindIndex(spawn, e => e is SabberStoneCore.Kettle.PowerHistorySubSpellEnd);
        var start = (SabberStoneCore.Kettle.PowerHistorySubSpellStart)h[missile];
        Assert.Equal(prison.Id, start.Source);
        Assert.Equal(new[] { fodder.Id }, start.Targets);
        Assert.True(missile < discard && discard < missileEnd, $"missile {missile} discard {discard} end {missileEnd}");
        Assert.True(missileEnd < spawn && spawn < created && created < spawnEnd, $"end {missileEnd} spawn {spawn} created {created} spawnEnd {spawnEnd}");

        prison.Damage = prison.BaseHealth; // breaks: Deathrattle frees Zuramat
        game.DeathProcessingAndAuraUpdate();
        var zuramat = me.BoardZone.First(m => m.Card.Id == "JAIL_887t2");
        var full = game.PowerHistory.Full;
        int dr = full.FindLastIndex(e => e is SabberStoneCore.Kettle.PowerHistorySubSpellStart s && s.Prefab.StartsWith("JAILFX_ZuramatsPrison_Deathrattle_Super:"));
        int zCreated = full.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryFullEntity f && f.Entity.Id == zuramat.Id);
        int drEnd = full.FindIndex(dr, e => e is SabberStoneCore.Kettle.PowerHistorySubSpellEnd);
        Assert.True(dr >= 0 && dr < zCreated && zCreated < drEnd, $"dr {dr} created {zCreated} end {drEnd}");
        Assert.DoesNotContain(full.Skip(dr), e => e is SabberStoneCore.Kettle.PowerHistorySubSpellStart s && s.Prefab.Contains("OverrideSpawn") && full.IndexOf(e) < drEnd);
    }

    [Fact]
    public void A_card_generated_from_a_dead_minion_is_a_new_entity()
    {
        // Engine rule (user): Elixir of Hope's "Deathrattle: return this minion to your hand" makes a NEW card; nothing that happened
        // to the dead minion (damage, buffs) follows the new one. The dead one is set aside (user decision 2026-10-10, live
        // session-20261004-224614 79240: GRAVEYARD -> SETASIDE in the Deathrattle block; Generic.SetAsideDead).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var tidehunter = (SabberStoneCore.Model.Entities.Minion)Give(me, "EX1_506");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, tidehunter)));
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "LOOT_278t4"), tidehunter))); // Elixir of Hope
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_029"), tidehunter))); // Fireball
        Assert.Equal(SabberStoneCore.Enums.Zone.SETASIDE, tidehunter.Zone.Type);
        var back = me.HandZone.Last();
        Assert.Equal(tidehunter.Card.Id, back.Card.Id);
        Assert.NotEqual(tidehunter.Id, back.Id);
        Assert.Equal(tidehunter.Id, back[SabberStoneCore.Enums.GameTag.CREATOR]);
        Assert.Equal(0, back[SabberStoneCore.Enums.GameTag.DAMAGE]);
        Assert.True(back.AppliedEnchantments == null || back.AppliedEnchantments.Count == 0);
    }

    [Fact]
    public void A_summoned_token_names_its_summoner_as_creator()
    {
        // Live: the Scout's FULL_ENTITY has CREATOR = Murloc Tidehunter. The client lays new minions out next to their
        // creator; without it the summon flashed on the far right and snapped over.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "CS2_231"))));
        var tidehunter = Give(me, "EX1_506");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, tidehunter, null, 0)));
        var scout = me.BoardZone[1];
        Assert.Equal("EX1_506a", scout.Card.Id);
        var full = game.PowerHistory.Last.OfType<SabberStoneCore.Kettle.PowerHistoryFullEntity>().Single(f => f.Entity.Id == scout.Id);
        Assert.Equal(tidehunter.Id, full.Entity.Tags[SabberStoneCore.Enums.GameTag.CREATOR]);
    }

    [Fact]
    public void A_minion_a_script_prepared_still_enters_in_its_full_entity()
    {
        // A script that sets a tag on a new minion before summoning it (Totemic Call, Uldum summons) used to leave its
        // FULL_ENTITY out of play, then ZONE=PLAY + SHOW_ENTITY: the client drew a blue placeholder and slid it into its
        // slot. The tag change folds into the FULL_ENTITY, which enters in PLAY at its slot like any summon.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        int before = game.PowerHistory.Full.Count;
        var totem = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("CS2_050"));
        totem[SabberStoneCore.Enums.GameTag.TAUNT] = 1; // a script's change before the summon
        SabberStoneCore.Actions.Generic.SummonBlock.Invoke(game, totem, -1, me.Hero.HeroPower);
        var fulls = game.PowerHistory.Full.Skip(before).OfType<SabberStoneCore.Kettle.PowerHistoryFullEntity>().Where(f => f.Entity.Id == totem.Id).ToList();
        Assert.Single(fulls);
        Assert.Equal((int)SabberStoneCore.Enums.Zone.PLAY, fulls[0].Entity.Tags[SabberStoneCore.Enums.GameTag.ZONE]);
        Assert.Equal(1, fulls[0].Entity.Tags[SabberStoneCore.Enums.GameTag.TAUNT]);
        Assert.DoesNotContain(game.PowerHistory.Full.Skip(before), e => e is SabberStoneCore.Kettle.PowerHistoryShowEntity s && s.Entity.Id == totem.Id);
        Assert.DoesNotContain(game.PowerHistory.Full.Skip(before), e => e is SabberStoneCore.Kettle.PowerHistoryTagChange t && t.EntityId == totem.Id && t.Tag == SabberStoneCore.Enums.GameTag.TAUNT);
    }

    [Fact]
    public void A_generated_card_enters_the_hand_like_live()
    {
        // Live (Sludge Slurper, Lorewalker Cho, Mukla): a created card's FULL_ENTITY is already in HAND at its slot with
        // CREATOR / CREATOR_DBID / SPAWN_TIME_COUNT, then DISPLAYED_CREATOR follows; no SHOW_ENTITY. The client flies it
        // in from its CREATOR with the blue spawn and shows "Created by".
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var slurper = Give(me, "DAL_433"); // Sludge Slurper: Battlecry: add a Lackey to your hand
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, slurper)));
        var lackey = me.HandZone[me.HandZone.Count - 1];
        Assert.NotEqual(slurper, lackey);
        var full = game.PowerHistory.Last.OfType<SabberStoneCore.Kettle.PowerHistoryFullEntity>().Single(f => f.Entity.Id == lackey.Id);
        Assert.Equal((int)SabberStoneCore.Enums.Zone.HAND, full.Entity.Tags[SabberStoneCore.Enums.GameTag.ZONE]);
        Assert.Equal(lackey.ZonePosition + 1, full.Entity.Tags[SabberStoneCore.Enums.GameTag.ZONE_POSITION]);
        Assert.Equal(slurper.Id, full.Entity.Tags[SabberStoneCore.Enums.GameTag.CREATOR]);
        Assert.Equal(slurper.Card.AssetId, full.Entity.Tags[SabberStoneCore.Enums.GameTag.CREATOR_DBID]);
        Assert.Equal(1, full.Entity.Tags[(SabberStoneCore.Enums.GameTag)1556]);
        Assert.False(full.Entity.Tags.ContainsKey(SabberStoneCore.Enums.GameTag.DISPLAYED_CREATOR));
        var after = game.PowerHistory.Last.SkipWhile(e => e != full).Skip(1).ToList();
        Assert.Equal(slurper.Id, ((SabberStoneCore.Kettle.PowerHistoryTagChange)after[0]) is { Tag: SabberStoneCore.Enums.GameTag.DISPLAYED_CREATOR } t ? t.Value : -1);
        Assert.DoesNotContain(game.PowerHistory.Last, e => e is SabberStoneCore.Kettle.PowerHistoryShowEntity s && s.Entity.Id == lackey.Id);
    }

    [Fact]
    public void Every_transform_keeps_the_entity_like_live()
    {
        // Live transforms in place (CHANGE_ENTITY on the same entity). The upstream TransformBlock replaced the minion
        // with a new entity, which the client showed as a removal and a new summon.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var yeti = (SabberStoneCore.Model.Entities.Minion)Give(me, "CS2_182");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, yeti)));
        int before = game.PowerHistory.Full.Count;
        Assert.True(SabberStoneCore.Actions.Generic.TransformBlock(me, SabberStoneCore.Model.Cards.FromId("CS2_231"), yeti));
        Assert.Same(yeti, me.BoardZone[0]);
        Assert.Equal("CS2_231", yeti.Card.Id);
        var added = game.PowerHistory.Full.Skip(before).ToList();
        Assert.Contains(added, e => e is SabberStoneCore.Kettle.PowerHistoryChangeEntity c && c.Entity.Id == yeti.Id);
        Assert.DoesNotContain(added, e => e is SabberStoneCore.Kettle.PowerHistoryFullEntity);
    }

    [Fact]
    public void End_of_turn_triggers_light_their_icon()
    {
        // Live: an end-of-turn trigger is a TRIGGER block, EffectIndex 0, TriggerKeyword=TRIGGER_VISUAL (the lightning
        // icon lights up). Our "resolve at once" trigger path sent a plain POWER block.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var imp = Give(me, "EX1_597"); // Imp Master: at the end of your turn, deal 1 damage to this and summon a 1/1 Imp
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, imp)));
        Assert.True(imp.Card[SabberStoneCore.Enums.GameTag.TRIGGER_VISUAL] > 0);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        var block = game.PowerHistory.Full.OfType<SabberStoneCore.Kettle.PowerHistoryBlockStart>().Last(b => b.Source == imp.Id);
        Assert.Equal(SabberStoneCore.Enums.BlockType.TRIGGER, block.BlockType);
        Assert.Equal(0, block.Index);
        Assert.Equal((int)SabberStoneCore.Enums.GameTag.TRIGGER_VISUAL, block.TriggerKeyword);
    }

    [Fact]
    public void Overloaded_crystals_count_as_spent_like_live()
    {
        // Live turn start with 1 Overload: RESOURCES_USED=1, OVERLOAD_LOCKED=1, OVERLOAD_OWED=0 (client shows 4/5 + a lock).
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.BaseMana = 5;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "EX1_238"), me.Opponent.Hero))); // Lightning Bolt (Overload 1)
        Assert.Equal(1, me.OverloadOwed);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)));
        Assert.Equal(1, me[SabberStoneCore.Enums.GameTag.RESOURCES_USED]);
        Assert.Equal(1, me.OverloadLocked);
        Assert.Equal(me.BaseMana - 1, me.RemainingMana);

        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        int used = sent.FindLastIndex(d => d.TagChange is { Tag: (int)GAME_TAG.RESOURCES_USED, Value: 1 } t && t.Entity == me.Id);
        int locked = sent.FindLastIndex(d => d.TagChange is { Tag: (int)GAME_TAG.OVERLOAD_LOCKED, Value: 1 } t && t.Entity == me.Id);
        Assert.True(used >= 0 && used < locked, $"RESOURCES_USED {used} before OVERLOAD_LOCKED {locked}");
    }

    [Fact]
    public void Trigger_blocks_never_name_a_target_like_live()
    {
        // Live: 3,790 of 3,790 TRIGGER blocks have Target=0. Naming the event's entity (the Murloc just played) made the
        // client treat it as the effect's target and keep it invisible until Underbelly Angler's card arrived.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var angler = Give(me, "DAL_049"); // Underbelly Angler: after you play a Murloc, add a random Murloc to your hand
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, angler)));
        var murloc = Give(me, "EX1_506"); // Murloc Tidehunter
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, murloc)));
        var block = game.PowerHistory.Last.OfType<SabberStoneCore.Kettle.PowerHistoryBlockStart>().Single(b => b.Source == angler.Id);
        Assert.Equal(SabberStoneCore.Enums.BlockType.TRIGGER, block.BlockType);
        Assert.Equal(0, block.Target);
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        Assert.DoesNotContain(sent, d => d.MetaData is { Type: HistoryMeta.Type.TARGET } m && m.Info.Contains(murloc.Id));
    }

    [Fact]
    public void A_chosen_target_that_left_play_is_no_longer_targeted()
    {
        // Engine rule: Prison of Yogg-Saron's later spells don't aim at a target that already left play; "targeting it if
        // possible" then picks another valid target.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var prison = Give(me, "TTN_090");
        var dead = (SabberStoneCore.Model.Entities.Minion)Give(me.Opponent, "CS2_182");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me.Opponent, dead)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)));
        dead.Damage = dead.BaseHealth;
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, dead.Zone.Type);

        int before = game.PowerHistory.Full.Count;
        var task = new SabberStoneCore.Tasks.SimpleTasks.CastRandomSpellTask(preferTarget: true);
        for (int i = 0; i < 20 && game.State != SabberStoneCore.Enums.State.COMPLETE; i++)
            task.Process(game, me, prison, dead);
        Assert.DoesNotContain(game.PowerHistory.Full.Skip(before),
            e => e is SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: SabberStoneCore.Enums.GameTag.CARD_TARGET } t && t.Value == dead.Id);
    }

    [Fact]
    public void A_spell_cast_by_an_effect_with_no_valid_target_does_nothing()
    {
        // Engine rule (user): a random spell that needs a target and has none is still cast (shown as played), but
        // does nothing. Explosive Shot with no minion on the board.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var prison = Give(me, "TTN_090");
        foreach (var m in game.Player1.BoardZone.Concat(game.Player2.BoardZone).ToList()) m.Zone.Remove(m);
        var shot = SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("EX1_537"));
        int enemyHealth = me.Opponent.Hero.Health;
        new SabberStoneCore.Tasks.SimpleTasks.CastRandomSpellTask().Process(game, me, prison, shot);
        Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, shot.Zone.Type);
        Assert.Equal(enemyHealth, me.Opponent.Hero.Health);
    }

    [Fact]
    public void Random_casting_stops_once_a_hero_is_dead()
    {
        // Engine rule (user): Yogg-Saron / Prison of Yogg-Saron / Fyrakk stop casting when a hero dies; the game ends there.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var prison = Give(me, "TTN_090");
        me.Opponent.Hero.Damage = me.Opponent.Hero.BaseHealth;
        int before = game.PowerHistory.Full.Count;
        Assert.Equal(SabberStoneCore.Tasks.TaskState.STOP,
            new SabberStoneCore.Tasks.SimpleTasks.CastRandomSpellTask().Process(game, me, prison, null));
        Assert.DoesNotContain(game.PowerHistory.Full.Skip(before), e => e is SabberStoneCore.Kettle.PowerHistoryFullEntity);
    }

    [Fact]
    public void Both_heroes_dying_in_one_play_is_a_draw()
    {
        // Advanced rulebook, Rule 9: win/loss is checked when the Sequence ends, not at the first dead hero.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.Hero.Damage = me.Hero.BaseHealth - 3;
        me.Opponent.Hero.Damage = me.Opponent.Hero.BaseHealth - 3;
        var hellfire = Give(me, "CS2_062"); // Hellfire: 3 damage to ALL characters
        game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, hellfire));
        Assert.Equal(PlayState.TIED, me.PlayState);
        Assert.Equal(PlayState.TIED, me.Opponent.PlayState);
        Assert.Equal(State.COMPLETE, game.State);
    }

    [Fact]
    public void Lord_jaraxxus_is_a_hero_card_that_equips_blood_fury()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var jaraxxus = Give(me, "CORE_EX1_323");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, jaraxxus, null, 0)));
        Assert.Equal("CORE_EX1_323", me.Hero.Card.Id);
        Assert.Equal("EX1_323w", me.Hero.Weapon?.Card.Id);
        Assert.Equal(3, me.Hero.Weapon.AttackDamage);
        Assert.Equal(8, me.Hero.Weapon.Durability);
    }

    [Fact]
    public void Questline_advances_through_new_stage_cards_to_its_final_reward()
    {
        // Live (captures/live-20261001-questline, Command the Elements): progress after each Overload card; a finished stage
        // gives its reward in a QUESTLINE trigger block and is replaced by the next stage, a NEW card in the Quest slot;
        // the last stage gives Stormcaller Bru'kan.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.UsedMana = 0;
        var stage1 = Give(me, "SW_031");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, stage1)));
        Assert.Same(stage1, me.SecretZone.Quest);
        void Bolt()
        {
            me.UsedMana = 0;
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CORE_EX1_238"), me.Opponent.Hero))); // Lightning Bolt, Overload 1
        }
        Bolt(); Bolt();
        Assert.Equal(2, ((SabberStoneCore.Model.Entities.Spell)me.SecretZone.Quest).QuestProgress);
        Bolt();
        var stage2 = (SabberStoneCore.Model.Entities.Spell)me.SecretZone.Quest;
        Assert.Equal("SW_031t", stage2.Card.Id);
        Assert.NotEqual(stage1.Id, stage2.Id);                 // a new card
        Assert.Equal(stage1.Id, stage2[GameTag.CREATOR]);
        Assert.Equal(0, stage2.QuestProgress);
        Assert.Contains(me.GraveyardZone.GetAll(), p => p.Id == stage1.Id);
        Assert.Equal(0, me.OverloadOwed);                       // stage 1 reward: Overloaded crystals unlocked
        var questline = game.PowerHistory.Full.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart { TriggerKeyword: 1725 } b && b.Source == stage1.Id);
        Assert.True(questline >= 0);

        Bolt(); Bolt(); Bolt();
        Assert.Single(me.BoardZone, m => m.Card.Id == "SW_031t8"); // stage 2 reward: Living Earth
        Assert.Equal("SW_031t2", me.SecretZone.Quest.Card.Id);
        Bolt(); Bolt();
        Assert.Null(me.SecretZone.Quest);
        Assert.Contains(me.HandZone.GetAll(), p => p.Card.Id == "SW_031t7"); // Stormcaller Bru'kan
    }

    // Each Questline's first stage, driven by real cards: it advances to stage 2 and gives its reward.
    private static (SabberStoneCore.Model.Game game, SabberStoneCore.Model.Entities.Controller me) QuestlineGame(string questline)
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, questline))));
        Assert.Equal(questline, me.SecretZone.Quest.Card.Id);
        return (game, me);
    }

    private static void Play(SabberStoneCore.Model.Game game, SabberStoneCore.Model.Entities.Controller me, string id, SabberStoneCore.Model.Entities.ICharacter? target = null)
    {
        me.UsedMana = 0;
        var card = Give(me, id);
        var task = card is SabberStoneCore.Model.Entities.Minion
            ? (target == null ? SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, card) : SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.MinionTarget(me, card, target))
            : (target == null ? SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, card) : SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, card, target));
        Assert.True(game.Process(task), id);
    }

    [Fact]
    public void Questline_raid_the_docks_counts_pirates()
    {
        var (game, me) = QuestlineGame("SW_028");
        for (int i = 0; i < 3; i++) Play(game, me, "CS2_146"); // Southsea Deckhand
        Assert.Equal("SW_028t", me.SecretZone.Quest.Card.Id);
    }

    [Fact]
    public void Questline_rise_to_the_occasion_counts_different_one_cost_cards()
    {
        var (game, me) = QuestlineGame("SW_313");
        Play(game, me, "CS2_168"); Play(game, me, "CS2_168"); // the same card twice counts once
        Assert.Equal(1, ((SabberStoneCore.Model.Entities.Spell)me.SecretZone.Quest).QuestProgress);
        Play(game, me, "EX1_008"); Play(game, me, "CS2_065");
        Assert.Equal("SW_313t", me.SecretZone.Quest.Card.Id);
        Assert.Equal("CS2_091", me.Hero.Weapon?.Card.Id); // Light's Justice
    }

    [Fact]
    public void Questline_defend_the_dwarven_district_counts_spells_that_deal_damage()
    {
        var (game, me) = QuestlineGame("SW_322");
        for (int i = 0; i < 3; i++) Play(game, me, "DS1_185", me.Opponent.Hero); // Arcane Shot
        Assert.Equal("SW_322t", me.SecretZone.Quest.Card.Id);
        Assert.True(me.SteadyShotCanTarget); // Crackshot: your Hero Power can target minions
    }

    [Fact]
    public void Questline_the_demon_seed_counts_damage_taken_on_your_turn()
    {
        var (game, me) = QuestlineGame("SW_091");
        for (int i = 0; i < 4; i++) Play(game, me, "CS2_062"); // Hellfire: 3 to everything, your hero too
        Assert.Equal("SW_091t", me.SecretZone.Quest.Card.Id);
        Assert.Equal(12 + 3, me.Opponent.Hero.Damage); // the reward's 3 damage
        Assert.Equal(12 - 3, me.Hero.Damage);         // with Lifesteal
    }

    [Fact]
    public void Questline_sorcerers_gambit_counts_fire_frost_and_arcane()
    {
        var (game, me) = QuestlineGame("SW_450");
        Play(game, me, "CS2_029", me.Opponent.Hero); Play(game, me, "CS2_029", me.Opponent.Hero); // two Fire: one counts
        Play(game, me, "CS2_024", me.Opponent.Hero); // Frost
        Assert.Equal(2, ((SabberStoneCore.Model.Entities.Spell)me.SecretZone.Quest).QuestProgress);
        Play(game, me, "CS2_025"); // Arcane Explosion
        Assert.Equal("SW_450t", me.SecretZone.Quest.Card.Id);
    }

    [Fact]
    public void Questline_find_the_imposter_counts_si7_cards()
    {
        var (game, me) = QuestlineGame("SW_052");
        Play(game, me, "EX1_134", me.Opponent.Hero); Play(game, me, "EX1_134", me.Opponent.Hero); // SI:7 Agent
        Assert.Equal("SW_052t", me.SecretZone.Quest.Card.Id);
        Assert.Contains(me.HandZone.GetAll(), p => p.Card.Id.StartsWith("SW_052t") && p.Card.Id != "SW_052t"); // a Spy Gizmo
    }

    [Fact]
    public void Questline_seek_guidance_counts_the_listed_costs()
    {
        var (game, me) = QuestlineGame("SW_433");
        Play(game, me, "CS2_120"); Play(game, me, "CS2_122"); Play(game, me, "CS2_182"); // 2, 3, 4
        Assert.Equal("SW_433t", me.SecretZone.Quest.Card.Id);
        Assert.NotNull(me.Choice); // Discover a card from your deck
    }

    [Fact]
    public void Questline_final_showdown_counts_draws_in_one_turn()
    {
        var (game, me) = QuestlineGame("SW_039");
        // (2026-11-20 patch 37.0.0.256589: "Draw 5 cards in one turn", was 4)
        Play(game, me, "CS2_023"); Play(game, me, "CS2_023"); SabberStoneCore.Actions.Generic.Draw(me); // Arcane Intellect x2 + a draw: 5 draws
        Assert.Equal("SW_039t", me.SecretZone.Quest.Card.Id);
        // the 5 cards it counted get "Fast Moves: Costs (1) less" (a 0-Cost card can't show it in its cost)
        Assert.Equal(5, me.HandZone.GetAll().Count(p => p.AppliedEnchantments?.Any(e => e.Card.Id == "SW_039te") == true));
    }

    [Fact]
    public void Questline_lost_in_the_park_counts_attack_your_hero_gains()
    {
        var (game, me) = QuestlineGame("SW_428");
        Play(game, me, "CS2_105"); // Heroic Strike: +4 Attack
        Assert.Equal("SW_428t", me.SecretZone.Quest.Card.Id);
        Assert.Equal(5, me.Hero.Armor);
    }

    [Fact]
    public void Practice_normal_and_expert_get_their_own_ai()
    {
        // Client Scenario.ModeId: 1 Normal (small search that misplays half the time), 2 Expert (the full planner).
        var normal = Assert.IsType<HsServer.Sabber.Ai.PlannerAi>(HsServer.Sabber.Ai.GameAis.ForMode(1));
        var expert = Assert.IsType<HsServer.Sabber.Ai.PlannerAi>(HsServer.Sabber.Ai.GameAis.ForMode(2));
        Assert.True(normal.BeamWidth < expert.BeamWidth && normal.Mistakes > 0 && expert.Mistakes == 0 && normal.Replies == 0);
        Assert.Equal(expert.BeamWidth, Assert.IsType<HsServer.Sabber.Ai.PlannerAi>(HsServer.Sabber.Ai.GameAis.ForMode(0)).BeamWidth);
    }

    [Fact]
    public void Temporary_mana_can_go_above_ten()
    {
        // User rule (2026-10-01): The Coin at 10/10 mana gives 11 for the turn.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.UsedMana = 0;
        Assert.Equal(10, me.RemainingMana);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "GAME_005"))));
        Assert.Equal(11, me.RemainingMana);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reborn_and_redemption_are_separate_effects(bool rebornAlreadyUsed)
    {
        // User rule (2026-10-01): both fire. Reborn brings it back at 1 Health without Reborn; Redemption returns it with
        // Reborn if it still had Reborn when it died, without if that was already used.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "EX1_136")))); // Redemption
        var murmy = Give(me, "CORE_ULD_723"); // Murmy 1/1 Reborn
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, murmy)));
        if (rebornAlreadyUsed)
        {
            me.UsedMana = 0;
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_024"), (SabberStoneCore.Model.Entities.ICharacter)murmy)));
            murmy = Assert.Single(me.BoardZone);
            Assert.Equal(0, murmy[GameTag.REBORN]);
        }
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        me.Opponent.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me.Opponent, Give(me.Opponent, "CS2_024"), (SabberStoneCore.Model.Entities.ICharacter)murmy)));

        var back = me.BoardZone.GetAll().Where(m => m.Card.Id == "CORE_ULD_723").ToList();
        Assert.All(back, m => Assert.Equal(1, m.Health));
        if (rebornAlreadyUsed)
            Assert.Equal(0, Assert.Single(back)[GameTag.REBORN]); // Redemption only, Reborn already spent
        else
        {
            Assert.Equal(2, back.Count);                          // Reborn's and Redemption's
            Assert.Single(back, m => m[GameTag.REBORN] == 0);     // Reborn's: no Reborn left
            Assert.Single(back, m => m[GameTag.REBORN] == 1);     // Redemption's: it still had Reborn
        }
    }

    [Fact]
    public void A_hero_card_keeps_this_turn_buffs_but_not_shield_or_freeze()
    {
        // User rule (2026-10-01): the new hero keeps "this turn" enchantments (Heroic Strike's +4 Attack, Ice Block's
        // Immune); plain state like Divine Shield and Freeze stays behind.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "CS2_105")))); // Heroic Strike
        new SabberStoneCore.Tasks.SimpleTasks.AddEnchantmentTask("EX1_295o", SabberStoneCore.Tasks.SimpleTasks.EntityType.HERO).Process(game, me, me.Hero, null); // Ice Block's Immune
        me.Hero[GameTag.DIVINE_SHIELD] = 1;
        me.Hero.IsFrozen = true;
        Assert.Equal(4, me.Hero.AttackDamage);
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, Give(me, "ICC_833")))); // Frost Lich Jaina
        Assert.Equal("ICC_833", me.Hero.Card.Id);
        Assert.Equal(4, me.Hero.AttackDamage);
        Assert.True(me.Hero.IsImmune);
        Assert.False(me.Hero.IsFrozen);
        Assert.Equal(0, me.Hero[GameTag.DIVINE_SHIELD]);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.Equal(0, me.Hero.AttackDamage); // "this turn" ends as usual
        Assert.False(me.Hero.IsImmune);
    }

    [Fact]
    public void Start_of_game_resolves_after_the_mulligan_like_live()
    {
        // Live (captures/live-20261001-startofgame, Genn Greymane): the condition is the starting deck (all even), the
        // effect resolves after both mulligans: a reveal block (effect index 2, REVEALED 1), the START_OF_GAME_KEYWORD block
        // (Hero Power costs 1), Genn hidden back in the deck (REVEALED 0), then the game's EffectIndex 4 block.
        _ = Db.Value;
        var deck = new List<SabberStoneCore.Model.Card> { SabberStoneCore.Model.Cards.FromId("GIL_692") };
        deck.AddRange(Enumerable.Repeat(SabberStoneCore.Model.Cards.FromId("CS2_182"), 29)); // Chillwind Yeti (4)
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.SHAMAN, Player2HeroClass = CardClass.MAGE, Player1Deck = deck,
            FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false, SkipMulligan = false,
        });
        game.StartGame();
        var me = game.Player1;
        int from = game.PowerHistory.Full.Count;
        game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Mulligan(me, new List<int>()));
        game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Mulligan(game.Player2, new List<int>()));

        Assert.Equal(1, me.Hero.HeroPower.Cost);
        var gennEntity = me.DeckZone.GetAll().Concat(me.HandZone.GetAll()).First(p => p.Card.Id == "GIL_692");
        var h = game.PowerHistory.Full.Skip(from).ToList();
        int done = h.FindLastIndex(e => e is SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: GameTag.MULLIGAN_STATE, Value: 4 });
        int reveal = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart { BlockType: BlockType.TRIGGER, Index: 2 } b && b.Source == gennEntity.Id);
        int effect = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart { TriggerKeyword: (int)GameTag.START_OF_GAME } b && b.Source == gennEntity.Id);
        int hidden = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: GameTag.REVEALED, Value: 0 } t && t.EntityId == gennEntity.Id);
        int game4 = h.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart { Index: 4 } b && b.Source == game.Id);
        Assert.True(done < reveal && reveal < effect && effect < hidden && hidden < game4, $"done {done} reveal {reveal} effect {effect} hidden {hidden} game4 {game4}");
    }

    [Fact]
    public void Mulligan_and_turn_start_follow_live()
    {
        // Live (RESEARCH/captures/trade-lifesteal-20260930): swap block with MULLIGAN_STATE 2→3, a DONE block that also
        // moves the game to MAIN_READY, the game's empty EffectIndex 4 block, and NUM_TURNS_IN_PLAY counted each turn.
        _ = Db.Value;
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.MAGE, Player2HeroClass = CardClass.MAGE,
            FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false, SkipMulligan = false,
        });
        game.StartGame();
        var me = game.Player1;
        var returned = me.HandZone.GetAll().Select(p => p.Id).ToList();
        int from = game.PowerHistory.Full.Count;
        game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Mulligan(me, new List<int>()));
        // The replacements are never the returned cards (the turn's first draw may be one: it's back in the deck).
        Assert.DoesNotContain(me.HandZone.GetAll(), p => returned.Contains(p.Id));
        game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Mulligan(game.Player2, game.Player2.HandZone.GetAll().Select(p => p.Id).ToList()));

        Assert.Equal(Step.MAIN_ACTION, game.Step);
        var entries = game.PowerHistory.Full.Skip(from).ToList();
        Assert.Contains(entries, e => e is SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: GameTag.MULLIGAN_STATE, Value: 3 } t && t.EntityId == me.Id);
        Assert.Contains(entries, e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart { BlockType: BlockType.TRIGGER, Index: 4 } b && b.Source == game.Id);
        Assert.DoesNotContain(entries, e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart { Index: 6 or 7 });
        Assert.DoesNotContain(entries, e => e is SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: GameTag.STEP, Value: (int)Step.MAIN_BEGIN });
        // live: both decks are shuffled (SHUFFLE_DECK 1, 2) right before the first player's TURN 1
        int shuffle1 = entries.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryShuffleDeck { PlayerId: 1 });
        int shuffle2 = entries.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryShuffleDeck { PlayerId: 2 });
        int turn1 = entries.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: GameTag.TURN, Value: 1 } t && t.EntityId == me.Id);
        Assert.True(shuffle1 >= 0 && shuffle1 < shuffle2 && shuffle2 < turn1, $"shuffle {shuffle1} {shuffle2} turn {turn1}");

        // A minion that stays in play counts its turns (the client shows it asleep while the count is 0).
        var yeti = SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("CS2_182"), zone: me.BoardZone);
        Assert.Equal(0, yeti[GameTag.NUM_TURNS_IN_PLAY]);
        game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
        Assert.Equal(1, yeti[GameTag.NUM_TURNS_IN_PLAY]);
        Assert.Equal(2, me.Hero[GameTag.NUM_TURNS_IN_PLAY]);
    }

    [Fact]
    public void Every_wild_card_can_be_created_in_a_deck_and_in_hand()
    {
        // Random effects (Wyrmrest Purifier, Discover, "random card") can create any card of the pool.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var failed = new List<string>();
        foreach (var card in SabberStoneCore.Model.Cards.AllWild)
        {
            try
            {
                var inDeck = SabberStoneCore.Model.Entities.Entity.FromCard(me, card, zone: me.DeckZone);
                me.DeckZone.Remove(inDeck);
                SabberStoneCore.Actions.Generic.ChangeEntityBlock(me, me.DeckZone[0], card, true);
            }
            catch (Exception e) { failed.Add($"{card.Id} ({card.Type}): {e.GetType().Name} {e.Message} {string.Join(" <- ", (e.StackTrace ?? "").Split((char)10).Take(4).Select(l => l.Trim().Split(" in ")[0]))}"); }
        }
        foreach (string f in failed) output.WriteLine(f);
        Assert.Empty(failed);
    }

    [Fact]
    public void Raptor_herald_discovers_beasts_with_three_different_dark_gifts_and_kindred_discount()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var beast = Give(me, "CS2_172"); // Bloodfen Raptor, a Beast: next turn Kindred is active
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, beast)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(game.CurrentPlayer)));
        Assert.Same(me, game.CurrentPlayer);

        var herald = Give(me, "CORE_EDR_004");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, herald)));
        Assert.NotNull(me.Choice);
        var offered = me.Choice.Choices.Select(id => game.IdEntityDic[id]).ToList();
        Assert.Equal(3, offered.Count);
        Assert.All(offered, p => Assert.True(p.Card.IsRace(Race.BEAST)));
        var gifts = offered.Select(p => p.AppliedEnchantments.Single(e => e.Card.Id.StartsWith("EDR_100t")).Card.Id).ToList();
        Assert.Equal(3, gifts.Distinct().Count());

        var pick = offered[0];
        int handBefore = me.HandZone.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, pick.Id)));
        if (gifts[0].StartsWith("EDR_100t8e")) // Sweet Dreams (live id EDR_100t8e1): on top of the deck
            Assert.Same(pick, me.DeckZone.TopCard);
        else
            Assert.Equal(handBefore + 1, me.HandZone.Count);
        Assert.Contains(pick.AppliedEnchantments, e => e.Card.Id == "GBL_003e"); // Kindred: costs (1) less
    }

    [Fact]
    public void Rude_awakening_doubles_a_battlecry_and_living_nightmare_summons_a_2_2_copy()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var archer = Give(me, "CS2_189"); // Elven Archer: Battlecry: deal 1 damage
        SabberStoneCore.Actions.Generic.AddEnchantmentBlock(game, SabberStoneCore.Model.Cards.FromId("EDR_100t7e"), archer, archer, 0, 0, 0);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.MinionTarget(me, archer, me.Opponent.Hero)));
        Assert.Equal(30 - 2, me.Opponent.Hero.Health);

        var yeti = Give(me, "CS2_182"); // Chillwind Yeti 4/5
        SabberStoneCore.Actions.Generic.AddEnchantmentBlock(game, SabberStoneCore.Model.Cards.FromId("EDR_100t5e"), yeti, yeti, 0, 0, 0);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, yeti)));
        var yetis = me.BoardZone.Where(m => m.Card.Id == "CS2_182").ToList();
        Assert.Equal(2, yetis.Count);
        Assert.Contains(yetis, m => m.AttackDamage == 2 && m.Health == 2);
    }

    /// <summary>Locations, as captured live (RESEARCH/locations-protocol.md), with Sanguine Depths (1 mana, 3 durability).</summary>
    [Fact]
    public void Location_plays_to_a_slot_activates_on_a_target_cools_down_and_breaks_after_its_durability()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var yeti = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me.Opponent, SabberStoneCore.Model.Cards.FromId("CS2_182"));
        SabberStoneCore.Actions.Generic.SummonBlock.Invoke(game, yeti, -1, yeti);
        var wisp = Give(me, "CS2_231");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, wisp)));

        // Play: into the chosen slot, not a minion play, usable at once, not a target or attacker.
        var depths = (SabberStoneCore.Model.Entities.Location)Give(me, "CORE_REV_990");
        int minionsPlayed = me.NumMinionsPlayedThisTurn;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, depths, null, 1)));
        Assert.Equal(1, depths.ZonePosition);
        Assert.Equal(minionsPlayed, me.NumMinionsPlayedThisTurn);
        Assert.False(depths.IsExhausted);
        Assert.DoesNotContain(depths, me.BoardZone.GetAll());
        Assert.DoesNotContain(me.Options(), o => o.Target?.Id == depths.Id);

        // Activate on the enemy Yeti: 1 damage and +2 Attack; cooldown, 1 durability used, exhausted, uses counter.
        var use = me.Options().OfType<SabberStoneCore.Tasks.PlayerTasks.LocationTask>().Single(o => o.ActivationTarget?.Id == yeti.Id);
        Assert.True(game.Process(use));
        Assert.Equal(yeti.Card.Health - 1, yeti.Health);
        Assert.Equal(yeti.Card.ATK + 2, yeti.AttackDamage);
        Assert.True(depths.OnCooldown);
        Assert.Equal(1, depths.Damage);
        Assert.True(depths.IsExhausted);
        Assert.Equal(1, me[SabberStoneCore.Model.Entities.Location.LOCATION_USES_THIS_GAME]);
        Assert.Equal(yeti.Id, depths.CardTarget);
        Assert.DoesNotContain(me.Options(), o => o is SabberStoneCore.Tasks.PlayerTasks.LocationTask);

        // Two more uses on later turns: the third breaks it (graveyard), with no minion-death side effects.
        // (Live: a used Location skips its owner's next turn — COOLDOWN 0 at the next MAIN_READY, EXHAUSTED 0 one owner turn
        // later; wiki Location "it cannot be used in the following turn".)
        for (int use2 = 2; use2 <= 3; use2++)
        {
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(game.CurrentPlayer)));
            Assert.False(depths.OnCooldown);
            Assert.True(depths.IsExhausted);
            Assert.DoesNotContain(me.Options(), o => o is SabberStoneCore.Tasks.PlayerTasks.LocationTask);
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(game.CurrentPlayer)));
            Assert.False(depths.IsExhausted);
            int corpses = me[GameTag.CORPSES], friendlyDied = me.NumFriendlyMinionsThatDiedThisTurn;
            Assert.True(game.Process(me.Options().OfType<SabberStoneCore.Tasks.PlayerTasks.LocationTask>().First()));
            Assert.Equal(use2, me[SabberStoneCore.Model.Entities.Location.LOCATION_USES_THIS_GAME]);
            if (use2 < 3) continue;
            Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, depths.Zone.Type);
            Assert.Equal(corpses, me[GameTag.CORPSES]);
            Assert.Equal(friendlyDied, me.NumFriendlyMinionsThatDiedThisTurn);
        }
    }

    /// <summary>Every Core Location in a deck of its class (10 copies), AI vs AI through the client view: games stay
    /// consistent and the Locations really get played and used.</summary>
    [Fact]
    public void Soak_games_with_location_decks_stay_consistent_and_use_them()
    {
        string[] locations = { "CORE_REV_290", "CORE_REV_333", "CORE_REV_362", "CORE_REV_371", "CORE_REV_602",
                               "CORE_REV_750", "CORE_REV_923", "CORE_REV_942", "CORE_REV_983", "CORE_REV_990" };
        _ = Db.Value;
        int uses = 0;
        var failures = new List<string>();
        for (int i = 0; i < locations.Length; i++)
        {
            var rng = new Random(100 + i);
            var cardClass = SabberStoneCore.Model.Cards.FromId(locations[i]).Class;
            List<string> Deck()
            {
                var deck = RandomDecks.BuildCurrentCore(cardClass, rng);
                for (int k = 0; k < 10; k++) deck[k] = locations[i];
                return deck;
            }
            var engine = new SabberEngineGame(new EngineSetup
            {
                Cards = Db.Value, Scenario = 260, Seed = rng.Next(), Rules = RulesEngine.Sabber,
                GameSetupBody = PegasusPackets.Serialize(new GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
                Human = new PlayerSetup { HeroCardId = RandomDecks.AnyHero(cardClass, rng), Deck = Deck(), AccountHi = 1, AccountLo = 2 },
                Opponent = new PlayerSetup { HeroCardId = RandomDecks.AnyHero(cardClass, rng), Deck = Deck(), IsAi = true },
            });
            var runner = new SoakRunner(rng)
            {
                Trace = (label, text) => uses += text.Split('\n').Count(l => l.Contains("LOCATION_ACTION_COOLDOWN value=1")),
            };
            var result = runner.Play(engine);
            if (result.Problems.Count > 0) failures.Add($"{locations[i]}: {result.Problems[0]}");
        }
        output.WriteLine($"Location uses seen by the client: {uses}");
        foreach (string f in failures) output.WriteLine(f);
        Assert.Empty(failures);
        Assert.True(uses >= 10, $"only {uses} Location uses in 10 games");
    }

    /// <summary>Excavate decks (every Excavate card, so every treasure tier comes up) in whole games through the client
    /// view, for both players: the games stay consistent, treasures are dug and played, nothing throws.</summary>
    [Fact]
    public void Excavate_decks_play_whole_games_and_dig_up_every_tier()
    {
        _ = Db.Value;
        string[] excavate = { "WW_001", "WW_002", "WW_009", "WW_322", "WW_324", "WW_352", "WW_356", "WW_378", "WW_380", "WW_413",
                              "WW_417", "WW_426", "WW_437", "WW_442", "DEEP_009", "DEEP_018", "DEEP_033", "WW_349", "WW_003", "WW_375" };
        var deck = excavate.Concat(excavate).Take(30).ToList();
        var failures = new List<string>();
        int treasuresPlayed = 0, treasuresDug = 0, errors = 0;
        var tiers = new HashSet<Rarity>();
        for (int i = 0; i < 6; i++)
        {
            var rng = new Random(500 + i);
            var engine = new SabberEngineGame(new EngineSetup
            {
                Cards = Db.Value, Scenario = 260, Seed = rng.Next(), Rules = RulesEngine.Sabber,
                GameSetupBody = PegasusPackets.Serialize(new GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
                Human = new PlayerSetup { HeroCardId = "HERO_01", Deck = deck, AccountHi = 1, AccountLo = 2 },
                Opponent = new PlayerSetup { HeroCardId = "HERO_08", Deck = deck, IsAi = true },
            });
            var result = new SoakRunner(rng).Play(engine);
            foreach (string p in result.Problems.Take(3))
                failures.Add(System.Text.RegularExpressions.Regex.Replace(p, @"entity (\d+)",
                    m => engine.Rules.IdEntityDic.TryGetValue(int.Parse(m.Groups[1].Value), out var e) ? $"entity {m.Groups[1].Value} ({e.Card.Id})" : m.Value));
            errors += engine.EngineErrors;
            var treasures = engine.Rules.IdEntityDic.Values.Where(e => e.Card[(GameTag)2823] > 0).ToList();
            treasuresDug += treasures.Count;
            foreach (var t in treasures) tiers.Add(t.Card.Rarity);
            treasuresPlayed += treasures.Count(e => e.Zone?.Type is SabberStoneCore.Enums.Zone.PLAY or SabberStoneCore.Enums.Zone.GRAVEYARD);
        }
        output.WriteLine($"treasures dug: {treasuresDug}, played: {treasuresPlayed}, tiers: {string.Join(", ", tiers)}, engine errors: {errors}");
        foreach (string f in failures) output.WriteLine(f);
        Assert.Empty(failures);
        Assert.True(treasuresDug >= 20, $"only {treasuresDug} treasures dug");
        Assert.True(treasuresPlayed >= 5, $"only {treasuresPlayed} treasures played");
        Assert.Equal(0, errors);
    }

    [Fact]
    public void Forge_decks_play_whole_games_and_forge()
    {
        _ = Db.Value;
        string[] forge = { "DEEP_024", "DEEP_027", "TTN_039", "TTN_042", "TTN_302", "TTN_430", "TTN_457", "TTN_458", "TTN_477", "TTN_490",
                           "TTN_724", "TTN_729", "TTN_730", "TTN_753", "TTN_801", "TTN_855t", "TTN_865", "TTN_951", "YOG_502", "YOG_525" };
        var deck = forge.Concat(forge).Take(30).ToList();
        var failures = new List<string>();
        int forged = 0, errors = 0;
        for (int i = 0; i < 6; i++)
        {
            var rng = new Random(700 + i);
            var engine = new SabberEngineGame(new EngineSetup
            {
                Cards = Db.Value, Scenario = 260, Seed = rng.Next(), Rules = RulesEngine.Sabber,
                GameSetupBody = PegasusPackets.Serialize(new GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
                Human = new PlayerSetup { HeroCardId = "HERO_06", Deck = deck, AccountHi = 1, AccountLo = 2 },
                Opponent = new PlayerSetup { HeroCardId = "HERO_07", Deck = deck, IsAi = true },
            });
            var result = new SoakRunner(rng).Play(engine);
            foreach (string p in result.Problems.Take(3))
                failures.Add(System.Text.RegularExpressions.Regex.Replace(p, @"entity (\d+)",
                    m => engine.Rules.IdEntityDic.TryGetValue(int.Parse(m.Groups[1].Value), out var e) ? $"entity {m.Groups[1].Value} ({e.Card.Id})" : m.Value));
            errors += engine.EngineErrors;
            forged += engine.Rules.IdEntityDic.Values.Count(e => e[(GameTag)3011] > 0); // FORGED
        }
        output.WriteLine($"cards forged: {forged}, engine errors: {errors}");
        foreach (string f in failures) output.WriteLine(f);
        Assert.Empty(failures);
        Assert.Equal(0, errors);
        Assert.True(forged >= 5, $"only {forged} cards forged");
    }

    [Fact]
    public void Storm_giant_gets_cheaper_every_forge_and_melted_maker_copies_it()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        foreach (var card in me.HandZone.ToList()) me.HandZone.Remove(card);
        var maker = Give(me, "TTN_729");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, maker)));
        var giant = Give(me, "TTN_724");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ForgeTask.Any(me, giant)));
        var forged = me.HandZone.First(p => p.Card.Id == "TTN_724t" && p[GameTag.CREATOR] == 0);
        Assert.Equal(6, forged.Cost);                                   // the Forged card: 8 → 6
        Assert.Equal(2, me.HandZone.Count(p => p.Card.Id == "TTN_724t")); // Melted Maker's copy
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ForgeTask.Any(me, forged)));
        Assert.Equal(4, me.HandZone.Single(p => p.Id == forged.Id || p[GameTag.TRANSFORMED_FROM_CARD] > 0 && p.Card.Id == "TTN_724t" && p.Cost == 4).Cost);
    }

    [Fact]
    public void Quickdraw_works_only_the_turn_the_card_entered_the_hand()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var opponent = me.Opponent;
        int health = opponent.Hero.Health;
        var gyreworm = Give(me, "DEEP_024"); // Quickdraw: Deal 5 damage
        Assert.True(SabberStoneCore.Actions.Generic.IsQuickdrawNow(gyreworm));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.MinionTarget(me, gyreworm, opponent.Hero)));
        Assert.Equal(health - 5, opponent.Hero.Health);
        var later = Give(me, "DEEP_024");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(opponent)));
        Assert.False(SabberStoneCore.Actions.Generic.IsQuickdrawNow(later)); // a turn later
        int before = opponent.Hero.Health;
        // its target only while Quickdraw is on (client PlayReq 95, live Sunspot Dragon): no target now, and no damage
        Assert.False(later.IsValidPlayTarget(opponent.Hero));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, later)));
        Assert.Equal(before, opponent.Hero.Health);
    }

    /// <summary>The AI (player 2) with a Mech + Magnetic deck in whole games through the client view: it really attaches
    /// Magnetic minions (each attach leaves the Magnetic card's enchantment on a Mech).</summary>
    [Fact]
    public void The_ai_magnetizes_in_real_games()
    {
        _ = Db.Value;
        string[] magnetic = { "Annoy-o-Module", "Replicating Menace", "Wargear", "Zilliax" };
        string[] mechs = { "Harvest Golem", "Mechwarper", "Spider Tank", "Annoy-o-Tron", "Piloted Shredder", "Clockwork Gnome" };
        var ids = magnetic.Concat(mechs).Select(n => SabberStoneCore.Model.Cards.FromName(n)).ToList();
        Assert.All(ids, Assert.NotNull);
        var aiDeck = ids.SelectMany(c => Enumerable.Repeat(c.Id, 3)).ToList();
        int attaches = 0;
        var failures = new List<string>();
        for (int i = 0; i < 10; i++)
        {
            var rng = new Random(300 + i);
            var engine = new SabberEngineGame(new EngineSetup
            {
                Cards = Db.Value, Scenario = 260, Seed = rng.Next(), Rules = RulesEngine.Sabber,
                GameSetupBody = PegasusPackets.Serialize(new GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
                Human = new PlayerSetup { HeroCardId = "HERO_08", Deck = RandomDecks.Build(CardClass.MAGE, rng, CardSet.CORE, CardSet.EXPERT1), AccountHi = 1, AccountLo = 2 },
                Opponent = new PlayerSetup { HeroCardId = "HERO_01", Deck = aiDeck, IsAi = true },
            });
            var result = new SoakRunner(rng).Play(engine);
            if (result.Problems.Count > 0) failures.Add(result.Problems[0]);
            attaches += engine.Rules.IdEntityDic.Values.Count(e => e is SabberStoneCore.Model.Entities.Enchantment en
                && en.Controller == engine.Rules.Player2 && en.Card.Id.EndsWith('e')
                && SabberStoneCore.Model.Cards.TryFromId(en.Card.Id[..^1]) is { Modular: true });
        }
        output.WriteLine($"Magnetic attaches by the AI in 10 games: {attaches}");
        foreach (string f in failures) output.WriteLine(f);
        Assert.Empty(failures);
        Assert.True(attaches >= 5, $"only {attaches} Magnetic attaches in 10 games");
    }

    [Fact]
    public void The_first_turn_after_the_mulligan_comes_in_its_own_packet_like_live()
    {
        // Live: MULLIGAN_STATE=DONE and NEXT_STEP=MAIN_READY end one packet; STEP=MAIN_READY and the first draw start the
        // next. In one packet the client counted the draw when laying out the kept cards (3 cards fanned like 4).
        _ = Db.Value;
        var rng = new Random(7);
        var cls = CardClass.MAGE;
        var engine = new SabberEngineGame(new EngineSetup
        {
            Cards = Db.Value, Scenario = 260, Seed = 7, Rules = RulesEngine.Sabber,
            GameSetupBody = PegasusPackets.Serialize(new GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
            Human = new PlayerSetup { HeroCardId = RandomDecks.AnyHero(cls, rng), Deck = RandomDecks.BuildCurrentCore(cls, rng), AccountHi = 1, AccountLo = 2 },
            Opponent = new PlayerSetup { HeroCardId = RandomDecks.AnyHero(cls, rng), Deck = RandomDecks.BuildCurrentCore(cls, rng), IsAi = true },
        });
        engine.Start();
        var choices = engine.Drain().OfType<EntityChoices>().Single(c => c.PlayerId == 1);
        engine.Drain();
        Assert.True(engine.ChooseEntities(choices.Id, choices.Entities));
        var histories = engine.Drain().OfType<PegasusGame.PowerHistory>().ToList();
        int done = histories.FindIndex(h => h.List.Any(d => d.TagChange is { Tag: (int)HsServer.Engine.GAME_TAG.MULLIGAN_STATE, Value: (int)HsServer.Engine.TAG_MULLIGAN.DONE }));
        int ready = histories.FindIndex(h => h.List.Any(d => d.TagChange is { Tag: (int)HsServer.Engine.GAME_TAG.STEP, Value: (int)HsServer.Engine.TAG_STEP.MAIN_READY }));
        Assert.True(done >= 0 && ready > done, $"DONE in packet {done}, MAIN_READY in packet {ready}");
        Assert.Equal((int)HsServer.Engine.TAG_STEP.MAIN_READY, histories[ready].List[0].TagChange?.Value);
    }

    [Fact]
    public void The_ai_mulligan_is_dealt_with_the_humans_its_swaps_keep_the_slot_and_it_gets_no_coin_like_live()
    {
        // Live (live-recordings-backup Hearthstone_2026_10_20_21_09_32, heist): the Innkeeper's EntitiesChosen comes at once, but its
        // MULLIGAN_STATE DEALING only with the player's, AI first; a replacement takes the returned card's slot (new card ZONE=HAND +
        // ZONE_POSITION = the slot, the old one ZONE_POSITION 0 then DECK); the AI going second gets no Coin (144 of 144 games).
        // Ours dealt the AI's swap while the client was still laying the hands out: the opponent's cards floated scattered.
        _ = Db.Value;
        var rng = new Random(11);
        var cls = CardClass.MAGE;
        var engine = new SabberEngineGame(new EngineSetup
        {
            Cards = Db.Value, Scenario = 260, Seed = 11, Rules = RulesEngine.Sabber,
            GameSetupBody = PegasusPackets.Serialize(new GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
            Human = new PlayerSetup { HeroCardId = RandomDecks.AnyHero(cls, rng), Deck = RandomDecks.BuildCurrentCore(cls, rng), AccountHi = 1, AccountLo = 2 },
            Opponent = new PlayerSetup { HeroCardId = RandomDecks.AnyHero(cls, rng), Deck = RandomDecks.BuildCurrentCore(cls, rng), IsAi = true },
        });
        engine.Start();
        var start = engine.Drain();
        var mine = start.OfType<EntityChoices>().Single(c => c.PlayerId == 1);
        var theirs = start.OfType<EntityChoices>().Single(c => c.PlayerId == 2);
        Assert.Equal(4, theirs.Entities.Count); // 4 cards, no Coin
        Assert.Contains(start, p => p is EntitiesChosen { PlayerId: 2 });
        bool Dealing(PegasusGame.PowerHistoryData d) => d.TagChange is { Tag: (int)HsServer.Engine.GAME_TAG.MULLIGAN_STATE, Value: (int)HsServer.Engine.TAG_MULLIGAN.DEALING };
        Assert.DoesNotContain(start.OfType<PegasusGame.PowerHistory>().SelectMany(h => h.List), Dealing);
        Assert.Equal(4, engine.Rules.Player2.HandZone.Count);

        Assert.True(engine.ChooseEntities(mine.Id, new List<int>()));
        var list = engine.Drain().OfType<PegasusGame.PowerHistory>().SelectMany(h => h.List).ToList();
        var dealing = list.Where(Dealing).Select(d => d.TagChange.Entity).ToList();
        Assert.Equal(new[] { engine.Rules.Player2.Id, engine.Rules.Player1.Id }, dealing);
        // every swap: the new card enters at the slot, no card is moved right-most first
        int p2Deal = list.FindIndex(Dealing);
        int p1Deal = list.FindLastIndex(Dealing);
        var aiSwap = list.Skip(p2Deal).Take(p1Deal - p2Deal).Where(d => d.TagChange is { Tag: (int)HsServer.Engine.GAME_TAG.ZONE_POSITION }).Select(d => d.TagChange.Value).ToList();
        Assert.DoesNotContain(aiSwap, v => v > 4);
    }

    /// <summary>Rules that used to be engine tables keyed by card id are now tags granted by the card's aura.</summary>
    [Fact]
    public void Card_rules_are_tags_granted_by_auras()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        SabberStoneCore.Model.Entities.Minion Put(string id)
        {
            var m = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId(id));
            SabberStoneCore.Actions.Generic.SummonBlock.Invoke(game, m, -1, m);
            game.DeathProcessingAndAuraUpdate();
            return m;
        }
        // Whirlwind Tempest: your Windfury minions have Mega-Windfury.
        var harpy = Put("CS2_231");
        harpy.HasWindfury = true;
        Put("DAL_742");
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(4, SabberStoneCore.Actions.Generic.AttacksPerTurn(harpy));
        // Living Dragonbreath: your minions can't be Frozen.
        Put("DRG_068");
        harpy.IsFrozen = true;
        Assert.True(SabberStoneCore.Model.Entities.Character.CantBeFrozen(harpy));
        // Steamwheedle Sniper: Steady Shot can target minions (controller rule tag).
        Assert.False(me.SteadyShotCanTarget);
        Put("GVG_087");
        Assert.True(me.SteadyShotCanTarget);
        // Commander Rhyssa / Tight-Lipped Witness: controller rule tags on the right players.
        Put("DAL_573");
        Assert.True(me.ControllerAuraEffects[HsTag.SECRETS_TRIGGER_TWICE] > 0);
        Put("CORE_MAW_032");
        Assert.True(me.ControllerAuraEffects[HsTag.SECRETS_CANT_TRIGGER] > 0 && me.Opponent.ControllerAuraEffects[HsTag.SECRETS_CANT_TRIGGER] > 0);
        // Falric: two copies double twice (fresh board).
        game = TenManaGame();
        me = game.CurrentPlayer;
        Put("CORE_EDR_003"); Put("CORE_EDR_003");
        int before = me.Corpses;
        me.GainCorpses(1);
        Assert.Equal(before + 4, me.Corpses);
    }

    /// <summary>Event rules are card triggers now: AFTER_DISCOVER, ADD_TO_HAND, and a controller tag for Mel'tranix.</summary>
    [Fact]
    public void Event_rules_are_card_triggers()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        void Put(string id)
        {
            var m = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId(id));
            SabberStoneCore.Actions.Generic.SummonBlock.Invoke(game, m, -1, m);
            game.DeathProcessingAndAuraUpdate();
        }
        // Dwarven Archaeologist: a Discovered card costs (1) less.
        Put("ULD_309");
        var discover = Give(me, "CORE_REV_945"); // Sketchy Stranger: Discover a Secret from another class
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, discover)));
        int pick = me.Choice.Choices[0];
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, pick)));
        var picked = game.IdEntityDic[pick];
        Assert.Equal(System.Math.Max(0, picked.Card.Cost - 1), picked.Cost);
        // Invitation Courier: a card from another class added to your hand is copied.
        Put("CORE_REV_377");
        int hand = me.HandZone.Count;
        SabberStoneCore.Actions.Generic.AddHandPhase.Invoke(me, SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("CS2_029"))); // Fireball (Mage)
        Assert.Equal(hand + 2, me.HandZone.Count);
        // Prosecutor Mel'tranix: the opponent may only play their outer cards next turn.
        var mel = Give(me, "CORE_MAW_014");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, mel)));
        Assert.True(me.Opponent[HsTag.ONLY_OUTER_CARDS_PLAYABLE] > 0);
    }

    /// <summary>Keyworded trigger blocks as live: TRIGGER + TriggerKeyword (LIFESTEAL, DEATHRATTLE, TRIGGER_VISUAL) — the
    /// client picks the animation from it (without it a lifesteal heal played the armor animation).</summary>
    [Fact]
    public void Keyworded_triggers_carry_their_trigger_keyword()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        SabberStoneCore.Model.Entities.Minion Put(SabberStoneCore.Model.Entities.Controller c, string id)
        {
            var m = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(c, SabberStoneCore.Model.Cards.FromId(id));
            SabberStoneCore.Actions.Generic.SummonBlock.Invoke(game, m, -1, m);
            game.DeathProcessingAndAuraUpdate();
            return m;
        }
        List<(int Keyword, int Source)> Keywords(int from) => game.PowerHistory.Full.Skip(from)
            .OfType<SabberStoneCore.Kettle.PowerHistoryBlockStart>().Where(b => b.TriggerKeyword != 0)
            .Select(b => (b.TriggerKeyword, b.Source)).ToList();

        // Lifesteal: a lifesteal minion hits, its heal is a LIFESTEAL trigger block on it.
        var stealer = Put(me, "CS2_231");
        stealer[GameTag.LIFESTEAL] = 1;
        stealer.IsExhausted = false;
        me.Hero.Damage = 5;
        var dummy = Put(me.Opponent, "CS2_182");
        int from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(me, stealer, dummy)));
        Assert.Contains(Keywords(from), k => k.Keyword == (int)GameTag.LIFESTEAL && k.Source == stealer.Id);

        // Live: even on a full-Health hero the Lifesteal block holds the heal attempt (TARGET + PREHEALING pulse), which the
        // client's drain effect needs; and it doesn't show in the history (it rides on the attack).
        me.Hero.Damage = 0;
        var stealer2 = Put(me, "CS2_231");
        stealer2[GameTag.LIFESTEAL] = 1;
        stealer2.IsExhausted = false;
        var dummy2 = Put(me.Opponent, "CS2_182");
        from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(me, stealer2, dummy2)));
        var entries = game.PowerHistory.Full.Skip(from).ToList();
        int open = entries.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart { TriggerKeyword: (int)GameTag.LIFESTEAL } b && b.Source == stealer2.Id);
        var drain = (SabberStoneCore.Kettle.PowerHistoryBlockStart)entries[open];
        Assert.False(drain.ShowInHistory);
        Assert.Contains(me.Hero.Id, drain.EffectTargets ?? new List<int>());
        Assert.Contains(entries.Skip(open), e => e is SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: GameTag.PREHEALING } t && t.EntityId == me.Hero.Id && t.Value > 0);

        // Deathrattle: Leper Gnome's Deathrattle is a DEATHRATTLE trigger block on the gnome.
        var gnome = Put(me, "EX1_029");
        from = game.PowerHistory.Full.Count;
        gnome.Destroy();
        game.DeathProcessingAndAuraUpdate();
        Assert.Contains(Keywords(from), k => k.Keyword == (int)GameTag.DEATHRATTLE && k.Source == gnome.Id);
        Assert.Contains(game.PowerHistory.Full.Skip(from), e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart { TriggerKeyword: (int)GameTag.DEATHRATTLE, ShowInHistory: true } b && b.Source == gnome.Id); // live: own history tile

        // TRIGGER_VISUAL: Armorsmith (a triggering card) fires with the trigger visual.
        var armorsmith = Put(me, "EX1_402");
        from = game.PowerHistory.Full.Count;
        SabberStoneCore.Actions.Generic.DamageCharFunc(dummy, Put(me, "CS2_182"), 1, false);
        game.DeathProcessingAndAuraUpdate();
        Assert.Contains(Keywords(from), k => k.Keyword == (int)GameTag.TRIGGER_VISUAL && k.Source == armorsmith.Id);
    }

    /// <summary>Fatigue as live sends it (heist-run capture): a FATIGUE block on the hero with META TARGET, the player's
    /// FATIGUE count, the damage, SLUSH_TIME 3000; 1, 2, 3... damage.</summary>
    [Fact]
    public void Fatigue_counts_on_the_player_in_a_fatigue_block()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        while (!me.DeckZone.IsEmpty) SabberStoneCore.Actions.Generic.Draw(me);
        int health = me.Hero.Health;
        int from = game.PowerHistory.Full.Count;
        for (int i = 0; i < 3; i++) SabberStoneCore.Actions.Generic.Draw(me);
        Assert.Equal(3, me[GameTag.FATIGUE]);
        Assert.Equal(health - (1 + 2 + 3), me.Hero.Health);

        var entries = game.PowerHistory.Full.Skip(from).ToList();
        var starts = entries.OfType<SabberStoneCore.Kettle.PowerHistoryBlockStart>().Where(b => b.BlockType == BlockType.FATIGUE).ToList();
        Assert.Equal(3, starts.Count);
        Assert.All(starts, b => Assert.Equal(me.Hero.Id, b.Source));
        int first = entries.IndexOf(starts[0]);
        Assert.IsType<SabberStoneCore.Kettle.PowerHistoryMetaData>(entries[first + 1]); // META TARGET [hero]
        var count = Assert.IsType<SabberStoneCore.Kettle.PowerHistoryTagChange>(entries[first + 2]);
        Assert.Equal((me.Id, GameTag.FATIGUE, 1), (count.EntityId, count.Tag, count.Value));
    }

    /// <summary>The newer Locations from the live capture: hand target (Zuramat's Prison), a lock (Low Security Wing), a
    /// "next heal" rule (Ruby Sanctum + Fan Club).</summary>
    [Fact]
    public void Newer_locations_hand_target_lock_and_next_heal()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        // Zuramat's Prison: the activation targets a card in hand; it's discarded and a 5/5 Taunt is summoned.
        var prison = (SabberStoneCore.Model.Entities.Location)Give(me, "JAIL_887");
        var fodder = Give(me, "CS2_231");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, prison, null, 0)));
        var uses = me.Options().OfType<SabberStoneCore.Tasks.PlayerTasks.LocationTask>().ToList();
        Assert.Contains(uses, u => u.ActivationTarget?.Id == fodder.Id);
        Assert.DoesNotContain(uses, u => u.ActivationTarget == null); // it needs a target
        Assert.True(game.Process(uses.First(u => u.ActivationTarget?.Id == fodder.Id)));
        Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, fodder.Zone.Type);
        Assert.Contains(me.BoardZone, m => m.Card.Id == "JAIL_887t3");
        Assert.Equal(fodder.Card.AssetId, prison[HsTag.PRISON_DISCARD_1]);

        // Low Security Wing: the minion it gives is locked until another card is played.
        var wing = (SabberStoneCore.Model.Entities.Location)Give(me, "JAIL_987");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, wing, null, 0)));
        Assert.True(game.Process(me.Options().OfType<SabberStoneCore.Tasks.PlayerTasks.LocationTask>().First(u => u.Source.Id == wing.Id)));
        var lockedCard = me.HandZone.Last();
        Assert.True(lockedCard[HsTag.LITERALLY_UNPLAYABLE] > 0);
        Assert.DoesNotContain(me.Options(), o => o.PlayerTaskType == SabberStoneCore.Tasks.PlayerTasks.PlayerTaskType.PLAY_CARD && o.Source?.Id == lockedCard.Id);
        var other = Give(me, "CS2_231");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, other)));
        Assert.Equal(0, lockedCard[HsTag.LITERALLY_UNPLAYABLE]);

        // Ruby Sanctum then Fan Club: the next healing effect (all friendly characters) deals 3 damage instead.
        var sanctum = (SabberStoneCore.Model.Entities.Location)Give(me, "CATA_301");
        var club = (SabberStoneCore.Model.Entities.Location)Give(me, "ETC_449");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, sanctum, null, 0)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, club, null, 0)));
        int hero = me.Hero.Health;
        Assert.True(game.Process(me.Options().OfType<SabberStoneCore.Tasks.PlayerTasks.LocationTask>().First(u => u.Source.Id == sanctum.Id)));
        Assert.True(game.Process(me.Options().OfType<SabberStoneCore.Tasks.PlayerTasks.LocationTask>().First(u => u.Source.Id == club.Id)));
        Assert.Equal(hero - 3, me.Hero.Health);

        // Two turns on (a used Location skips its owner's next turn): a fresh Ruby Sanctum converts the next heal from another
        // source (the old memory used to eat it).
        for (int turn = 0; turn < 2; turn++)
        {
            game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me));
            game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(game.CurrentPlayer));
        }
        me.Hero.Damage = 5;
        hero = me.Hero.Health;
        Assert.True(game.Process(me.Options().OfType<SabberStoneCore.Tasks.PlayerTasks.LocationTask>().First(u => u.Source.Id == sanctum.Id)));

        var heal = Give(me, "CS2_007");     // Healing Touch: restore 8
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, heal, me.Hero)));
        Assert.Equal(hero - 8, me.Hero.Health);

        // Blizzard files some cards' own Deathrattle as a "reference" tag; the text's "<b>Deathrattle:</b>" makes it theirs.
        SabberStoneCore.Model.Card C(string id) => SabberStoneCore.Model.Cards.FromId(id);
        Assert.True(C("JAIL_887").Deathrattle);   // Zuramat's Prison (live sends DEATHRATTLE=1)
        Assert.True(C("YOD_016").Deathrattle);    // Skyvateer, a minion
        Assert.False(C("REV_333").Deathrattle);   // Hedge Maze only mentions a minion's Deathrattle
        Assert.False(C("BOT_438").Deathrattle);   // quoted: it gives minions a Deathrattle

        // Zuramat's Prison breaks: its Deathrattle frees Zuramat.
        prison.Damage = prison.BaseHealth;
        game.DeathProcessingAndAuraUpdate();
        Assert.Contains(me.BoardZone, m => m.Card.Id == "JAIL_887t2");
    }

    /// <summary>The use's history in live order (RESEARCH/locations-protocol.md "Using a Location").</summary>
    [Fact]
    public void Location_use_history_matches_the_live_order()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var yeti = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me.Opponent, SabberStoneCore.Model.Cards.FromId("CS2_182"));
        SabberStoneCore.Actions.Generic.SummonBlock.Invoke(game, yeti, -1, yeti);
        var depths = (SabberStoneCore.Model.Entities.Location)Give(me, "CORE_REV_990");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, depths, null, 0)));
        int from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.LocationTask.Any(me, depths, yeti)));

        var cooldown = SabberStoneCore.Model.Entities.Location.LOCATION_ACTION_COOLDOWN;
        var uses = SabberStoneCore.Model.Entities.Location.LOCATION_USES_THIS_GAME;
        var seen = new List<string>();
        foreach (var e in game.PowerHistory.Full.Skip(from))
            switch (e)
            {
                case SabberStoneCore.Kettle.PowerHistoryBlockStart b when b.Source == depths.Id:
                    seen.Add($"{b.BlockType}>{b.Target}"); break;
                case SabberStoneCore.Kettle.PowerHistoryBlockEnd:
                    seen.Add("END"); break;
                case SabberStoneCore.Kettle.PowerHistoryTagChange t when t.EntityId == depths.Id && (t.Tag is GameTag.CARD_TARGET or GameTag.DAMAGE or GameTag.EXHAUSTED || t.Tag == cooldown):
                    seen.Add($"{(t.Tag == cooldown ? "COOLDOWN" : t.Tag.ToString())}={t.Value}"); break;
                case SabberStoneCore.Kettle.PowerHistoryTagChange t when t.EntityId == me.Id && t.Tag == uses:
                    seen.Add($"USES={t.Value}"); break;
            }
        output.WriteLine(string.Join(" ", seen));
        // Keep only the outer PLAY and the Location's POWER block ends (nested effect blocks may add ENDs in between).
        string flat = string.Join(" ", seen);
        Assert.Matches($@"^PLAY>{yeti.Id} CARD_TARGET={yeti.Id} POWER>{yeti.Id} (END )*COOLDOWN=1 END USES=1 DAMAGE=1 EXHAUSTED=1 END$", flat);
    }

    [Fact]
    public void Riot_keeps_attackers_at_one_health()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var wisp = (SabberStoneCore.Model.Entities.Minion)Give(me, "CS2_231");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, wisp)));
        var yeti = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me.Opponent, SabberStoneCore.Model.Cards.FromId("CS2_182"));
        SabberStoneCore.Actions.Generic.SummonBlock.Invoke(game, yeti, -1, yeti);
        var riot = Give(me, "CORE_REV_337");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, riot)));
        Assert.Equal(1, wisp.Health);
        Assert.Contains(wisp, me.BoardZone);
        Assert.Equal(yeti.Card.Health - 1, yeti.Health);
    }

    [Fact]
    public async Task Rules_engine_game_plays_over_tcp_to_the_end()
    {
        var (s, cts, server, _) = await Connect(seed: 3);
        EntityChoices mulligan = null;
        while (mulligan == null)
            if (await Read(s, cts.Token) is EntityChoices ec && ec.PlayerId == 1) mulligan = ec;
        await Write(s, PegasusPackets.IdOf<ChooseEntities>(), new ChooseEntities { Id = mulligan.Id, Entities = mulligan.Entities });

        int turns = 0;
        bool over = false;
        while (!over && turns < 80)
        {
            IProtoBuf packet = await Read(s, cts.Token);
            if (packet is PowerHistory history)
                over = history.List.Any(d => d.TagChange is { } t && t.Tag == (int)GAME_TAG.STATE && t.Value == (int)TAG_STATE.COMPLETE);
            if (packet is AllOptions options && !over)
            {
                turns++;
                await Write(s, PegasusPackets.IdOf<ChooseOption>(), new ChooseOption { Id = options.Id, Index = 0, Target = 0, SubOption = -1, Position = 0 });
            }
        }
        output.WriteLine($"turns ended by the client: {turns}, game over: {over}");
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
        Assert.True(over, "the AI should win against a player who only ends turns");
    }

    [Fact]
    public async Task Concede_ends_a_rules_engine_game()
    {
        var (s, cts, server, _) = await Connect(seed: 4);
        EntityChoices mulligan = null;
        while (mulligan == null)
            if (await Read(s, cts.Token) is EntityChoices ec && ec.PlayerId == 1) mulligan = ec;
        await Write(s, PegasusPackets.IdOf<ChooseEntities>(), new ChooseEntities { Id = mulligan.Id, Entities = mulligan.Entities });
        await Write(s, PegasusPackets.IdOf<PegasusGame.Concede>(), new PegasusGame.Concede());
        var playStates = new List<TAG_PLAYSTATE>();
        var steps = new List<TAG_STEP>();
        bool complete = false;
        while (!complete)
            if (await Read(s, cts.Token) is PowerHistory h)
                foreach (var t in h.List.Where(d => d.TagChange != null).Select(d => d.TagChange))
                {
                    if (t.Tag == (int)GAME_TAG.PLAYSTATE) playStates.Add((TAG_PLAYSTATE)t.Value);
                    if (t.Tag == (int)GAME_TAG.STEP) steps.Add((TAG_STEP)t.Value);
                    if (t.Tag == (int)GAME_TAG.STATE && t.Value == (int)TAG_STATE.COMPLETE) complete = true;
                }
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
        output.WriteLine($"play states: {string.Join(", ", playStates)}; steps: {string.Join(", ", steps.TakeLast(3))}");
        // Live: CONCEDED, LOST for the conceder, WON for the opponent, then FINAL_WRAPUP → FINAL_GAMEOVER.
        Assert.Equal(new[] { TAG_PLAYSTATE.CONCEDED, TAG_PLAYSTATE.LOST, TAG_PLAYSTATE.WON }, playStates.TakeLast(3));
        Assert.Equal(new[] { TAG_STEP.FINAL_WRAPUP, TAG_STEP.FINAL_GAMEOVER }, steps.TakeLast(2));
    }

    [Fact]
    public async Task A_player_who_reconnects_gets_the_game_as_it_is_and_plays_on()
    {
        // A4, live-20261001-startofgame (a reconnect from login): GameSetup, then one PowerHistory of CREATE_GAME and a
        // FULL_ENTITY of every entity as the player may see it (no blocks, no tag changes), then the options.
        var (allocations, game, port, cts, server) = Serve(seed: 6, new GameOptions { EndTurnResponseSeconds = 0, AiFirstActionSeconds = 0, AiActionSpacingSeconds = 0 });
        var (tcp, s) = await Join(port, game, cts.Token);
        var known = new Dictionary<int, string>();
        void See(PowerHistory h)
        {
            foreach (var d in h.List)
            {
                if (d.FullEntity != null) known[d.FullEntity.Entity] = d.FullEntity.Name ?? "";
                if (d.ShowEntity != null) known[d.ShowEntity.Entity] = d.ShowEntity.Name ?? "";
                if (d.ChangeEntity != null) known[d.ChangeEntity.Entity] = d.ChangeEntity.Name ?? "";
                if (d.HideEntity != null) known[d.HideEntity.Entity] = "";
            }
        }
        AllOptions options = null;
        for (int turns = 0; turns < 2;)
            switch (await Read(s, cts.Token))
            {
                case PowerHistory h: See(h); break;
                case EntityChoices c when c.PlayerId == 1:
                    await Write(s, PegasusPackets.IdOf<ChooseEntities>(), new ChooseEntities { Id = c.Id, Entities = c.Entities });
                    break;
                case AllOptions o:
                    options = o;
                    if (++turns < 2) await Write(s, PegasusPackets.IdOf<ChooseOption>(), new ChooseOption { Id = o.Id, Index = 0, SubOption = -1 });
                    break;
            }
        tcp.Close(); // the player's connection drops on their turn
        Assert.Same(game, allocations.Unfinished(null)); // the login offers it
        Assert.False(game.Game.Ended);

        var (tcp2, s2) = await Join(port, game, cts.Token);
        var snapshot = Assert.IsType<PowerHistory>(await Read(s2, cts.Token));
        Assert.NotNull(snapshot.List[0].CreateGame);
        Assert.All(snapshot.List.Skip(1), d => Assert.NotNull(d.FullEntity));
        var shown = snapshot.List.Skip(1).ToDictionary(d => d.FullEntity.Entity, d => d.FullEntity.Name ?? "");
        var cards = known.Where(k => k.Key > 3).ToDictionary(k => k.Key, k => k.Value); // 1 game, 2-3 players
        Assert.Equal(cards.Keys.OrderBy(i => i), shown.Keys.OrderBy(i => i));
        foreach (var (id, cardId) in cards) Assert.Equal(cardId, shown[id]);
        var resent = Assert.IsType<AllOptions>(await Read(s2, cts.Token));
        Assert.Equal(options.Options.Count, resent.Options.Count);

        // and the game goes on: end the turn, the AI plays, the turn comes back
        await Write(s2, PegasusPackets.IdOf<ChooseOption>(), new ChooseOption { Id = resent.Id, Index = 0, SubOption = -1 });
        bool aiPlayed = false;
        for (IProtoBuf p = await Read(s2, cts.Token); p is not AllOptions; p = await Read(s2, cts.Token))
            aiPlayed |= p is PowerHistory;
        Assert.True(aiPlayed);
        cts.Cancel();
        tcp2.Close();
        try { await server; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task A_player_who_does_not_come_back_concedes_when_the_window_ends()
    {
        var (allocations, game, port, cts, server) = Serve(seed: 7, new GameOptions { ReconnectSeconds = 0.3, EndTurnResponseSeconds = 0, AiFirstActionSeconds = 0, AiActionSpacingSeconds = 0 });
        var outcome = new TaskCompletionSource<GameOutcome>();
        game.Finished = o => { outcome.TrySetResult(o); return Task.CompletedTask; };
        var (tcp, s) = await Join(port, game, cts.Token);
        while (await Read(s, cts.Token) is not EntityChoices { PlayerId: 1 }) { }
        tcp.Close();
        GameOutcome result = await outcome.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.False(result.Won);
        Assert.True(result.Tally.Conceded);
        for (int i = 0; i < 50 && allocations.Claim(game.GameHandle, game.Password) != null; i++) await Task.Delay(20);
        Assert.Null(allocations.Claim(game.GameHandle, game.Password)); // gone: nobody can join it any more
        Assert.Null(allocations.Unfinished(null));
        cts.Cancel();
        try { await server; } catch (OperationCanceledException) { }
    }

    private static (GameAllocations, GameAllocation, int, CancellationTokenSource, Task) Serve(int seed, GameOptions options)
    {
        var rng = new Random(seed);
        var setup = new EngineSetup
        {
            Cards = Db.Value, Scenario = 260, Seed = seed, Rules = RulesEngine.Sabber,
            GameSetupBody = PegasusPackets.Serialize(new GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
            Human = new PlayerSetup { HeroCardId = "HERO_08", Deck = RandomDecks.Build(CardClass.MAGE, rng, CardSet.CORE, CardSet.EXPERT1), AccountHi = 1, AccountLo = 2 },
            Opponent = new PlayerSetup { HeroCardId = "HERO_01", Deck = RandomDecks.Build(CardClass.WARRIOR, rng, CardSet.CORE, CardSet.EXPERT1), IsAi = true },
        };
        var allocations = new GameAllocations();
        GameAllocation game = allocations.Allocate(260, null, setup);
        int port = FreePort();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Task server = new GameServer(allocations, options, port).RunAsync(cts.Token);
        return (allocations, game, port, cts, server);
    }

    // The recorded Handshake with this game's handle and password, then GameSetup and GetGameState.
    private static async Task<(TcpClient, NetworkStream)> Join(int port, GameAllocation game, CancellationToken ct)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port, ct);
        var s = tcp.GetStream();
        var recorded = RecordedMatch.LoadAll(TestEnvironment.Sessions).First().Events.First(e => !e.FromServer && e.Type == PegasusPackets.IdOf<Handshake>());
        var handshake = PegasusPackets.Deserialize<Handshake>(recorded.Body);
        handshake.GameHandle = game.GameHandle;
        handshake.Password = game.Password;
        await Write(s, PegasusPackets.IdOf<Handshake>(), handshake);
        Assert.IsType<GameSetup>(await Read(s, ct));
        await Write(s, PegasusPackets.IdOf<GetGameState>(), new GetGameState());
        return (tcp, s);
    }

    private async Task<(NetworkStream, CancellationTokenSource, Task, TcpClient)> Connect(int seed)
    {
        var rng = new Random(seed);
        var setup = new EngineSetup
        {
            Cards = Db.Value, Scenario = 260, Seed = seed, Rules = RulesEngine.Sabber,
            GameSetupBody = PegasusPackets.Serialize(new GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
            Human = new PlayerSetup { HeroCardId = "HERO_08", Deck = RandomDecks.Build(CardClass.MAGE, rng, CardSet.CORE, CardSet.EXPERT1), AccountHi = 1, AccountLo = 2 },
            Opponent = new PlayerSetup { HeroCardId = "HERO_01", Deck = RandomDecks.Build(CardClass.WARRIOR, rng, CardSet.CORE, CardSet.EXPERT1), IsAi = true },
        };
        var allocations = new GameAllocations();
        GameAllocation game = allocations.Allocate(260, null, setup);
        int port = FreePort();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Task server = new GameServer(allocations, new GameOptions { EndTurnResponseSeconds = 0, AiFirstActionSeconds = 0, AiActionSpacingSeconds = 0 }, port).RunAsync(cts.Token);
        var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        var s = tcp.GetStream();
        var recorded = RecordedMatch.LoadAll(TestEnvironment.Sessions).First().Events.First(e => !e.FromServer && e.Type == PegasusPackets.IdOf<Handshake>());
        var handshake = PegasusPackets.Deserialize<Handshake>(recorded.Body);
        handshake.GameHandle = game.GameHandle;
        handshake.Password = game.Password;
        await Write(s, PegasusPackets.IdOf<Handshake>(), handshake);
        Assert.IsType<GameSetup>(await Read(s, cts.Token));
        await Write(s, PegasusPackets.IdOf<GetGameState>(), new GetGameState());
        return (s, cts, server, tcp);
    }

    private static async Task Write(NetworkStream s, int type, IProtoBuf message)
    {
        byte[] body = PegasusPackets.Serialize(message);
        var frame = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, type);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), body.Length);
        body.CopyTo(frame, 8);
        await s.WriteAsync(frame);
    }

    private static async Task<IProtoBuf> Read(NetworkStream s, CancellationToken ct)
    {
        var head = new byte[8];
        await s.ReadExactlyAsync(head, ct);
        int type = BinaryPrimitives.ReadInt32LittleEndian(head);
        var body = new byte[BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(4))];
        await s.ReadExactlyAsync(body, ct);
        return PegasusPackets.Deserialize(PegasusPackets.TypeFor(type, "PegasusGame"), body);
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static SabberStoneCore.Model.Entities.IPlayable ChoiceOf(SabberStoneCore.Model.Game game, SabberStoneCore.Model.Entities.Controller me, string id) =>
        me.Choice.Choices.Select(c => game.IdEntityDic[c]).First(p => p.Card.Id == id);

    [Fact]
    public void Silas_darkmoon_rotates_the_outermost_minions_even_with_full_boards()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        var a = OnBoard(game, me, "CS2_168");
        var b = OnBoard(game, me, "CS2_168");
        var x = OnBoard(game, op, "CS2_168");
        var y = OnBoard(game, op, "CS2_168");
        var silas = Give(me, "DMF_074");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, silas)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, ChoiceOf(game, me, "DMF_074a").Id)));
        // "This Way": my left-most goes to the enemy's left end, the enemy's right-most comes to my right end
        Assert.Equal(new[] { b.Id, silas.Id, y.Id }, me.BoardZone.Select(m => m.Id).ToArray());
        Assert.Equal(new[] { a.Id, x.Id }, op.BoardZone.Select(m => m.Id).ToArray());
        Assert.Equal(me, y.Controller);

        // two full boards swap one minion each: nobody dies
        var game2 = TenManaGame();
        var me2 = game2.CurrentPlayer;
        for (int i = 0; i < 6; i++) OnBoard(game2, me2, "CS2_168");
        for (int i = 0; i < 7; i++) OnBoard(game2, me2.Opponent, "CS2_168");
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me2, Give(me2, "DMF_074"))));
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me2, ChoiceOf(game2, me2, "DMF_074b").Id)));
        Assert.Equal(7, me2.BoardZone.Count);
        Assert.Equal(7, me2.Opponent.BoardZone.Count);
    }

    [Fact]
    public void Darkmoon_player_rules_moonfang_deathwarden_ilgynoth_lightsteed()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        // Moonfang: "Can only take 1 damage at a time"
        var moonfang = OnBoard(game, op, "YOP_035");
        int hp = moonfang.Health;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_029"), moonfang)));
        Assert.Equal(hp - 1, moonfang.Health);

        // Deathwarden: Deathrattles can't trigger (either side)
        var warden = OnBoard(game, op, "YOP_012");
        var gnome = OnBoard(game, op, "EX1_029"); // Deathrattle: 2 damage to the enemy hero
        gnome.Destroy();
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(0, me.Hero.Damage);
        warden.Destroy();
        game.DeathProcessingAndAuraUpdate();
        var gnome2 = OnBoard(game, op, "EX1_029");
        gnome2.Destroy();
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(2, me.Hero.Damage);

        // Il'gynoth: my Lifesteal damages the enemy hero instead of healing me
        var ilgynoth = OnBoard(game, me, "DMF_230");
        var ogre = OnBoard(game, op, "CS2_200");
        int opHp = op.Hero.Health;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(me, ilgynoth, ogre)));
        Assert.Equal(2, me.Hero.Damage);
        Assert.Equal(opHp - ilgynoth.AttackDamage, op.Hero.Health);

        // Lightsteed: my healing also gives the affected minion +2 Health
        OnBoard(game, me, "YOP_008");
        var hurt = OnBoard(game, me, "CS2_200");
        hurt.Damage = 3;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "AT_055"), hurt)));
        Assert.Equal(0, hurt.Damage);
        Assert.Equal(hurt.Card[GameTag.HEALTH] + 2, hurt.Health);
    }

    [Fact]
    public void A_dormant_minions_triggers_wait_until_it_awakens()
    {
        // Imprisoned Celestial: Dormant for 2 turns, Spellburst only once awake
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var celestial = OnBoard(game, me, "YOP_010");
        var ogre = OnBoard(game, me, "CS2_200");
        Assert.True(celestial[GameTag.DORMANT] > 0);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "GAME_005"))));
        Assert.False(ogre.HasDivineShield);
        for (int i = 0; i < 4; i++) Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(game.CurrentPlayer)));
        Assert.Equal(0, celestial[GameTag.DORMANT]);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "GAME_005"))));
        Assert.True(ogre.HasDivineShield);

        // live-20261006 Imprisoned Phoenix: no Spell Damage while Dormant (SPELLPOWER 0), +2 once awake
        var phoenix = OnBoard(game, me, "YOP_021");
        Assert.Equal(0, me.CurrentSpellPower);
        for (int i = 0; i < 4; i++) Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(game.CurrentPlayer)));
        Assert.Equal(0, phoenix[GameTag.DORMANT]);
        Assert.Equal(2, me.CurrentSpellPower);
    }

    [Fact]
    public void Darkmoon_legendaries_and_spells_work()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;

        // Trampling Rhino: excess damage of a kill hits the enemy hero
        var rhino = OnBoard(game, me, "DMF_087");
        var raider = OnBoard(game, op, "CS2_168"); // 2/1
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(me, rhino, raider)));
        Assert.Equal(rhino.AttackDamage - 1, op.Hero.Damage);

        // The Nameless One: becomes a 4/4 copy of the minion, then Silences it
        var ogre = OnBoard(game, op, "CS2_200");
        var nameless = Give(me, "DMF_116");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.MinionTarget(me, nameless, ogre)));
        var copy = (SabberStoneCore.Model.Entities.Minion)game.IdEntityDic[nameless.Id];
        Assert.Equal("CS2_200", copy.Card.Id);
        Assert.Equal(4, copy.AttackDamage);
        Assert.Equal(4, copy.Health);
        Assert.True(ogre.IsSilenced);

        // C'Thun's four pieces reform it into the deck
        me.UsedMana = 0;
        var target = OnBoard(game, op, "CS2_168");
        Assert.DoesNotContain(me.DeckZone, p => p.Card.Id == "DMF_254");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "DMF_254t3"))));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "DMF_254t5"))));
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "DMF_254t4"))));
        Assert.DoesNotContain(me.DeckZone, p => p.Card.Id == "DMF_254");
        var other = OnBoard(game, op, "CS2_200");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "DMF_254t7"), other)));
        Assert.Contains(me.DeckZone, p => p.Card.Id == "DMF_254");
        foreach (var piece in new[] { HsTag.PLAYED_CTHUN_EYE, HsTag.PLAYED_CTHUN_BODY, HsTag.PLAYED_CTHUN_MAW, HsTag.PLAYED_CTHUN_HEART })
            Assert.Equal(0, me[piece]); // live: all back to 0 when C'Thun reforms

        // Y'Shaarj: copies of the Corrupted cards played this game, costing (0) this turn
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "DMF_054t"))));
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "DMF_188"))));
        var again = me.HandZone.Last(p => p.Card.Id == "DMF_054t");
        Assert.Equal(0, again.Cost);

        // Guidance: picking Spirit Path adds both spells and Overloads (1)
        me.UsedMana = 0;
        int hand = me.HandZone.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "YOP_024"))));
        Assert.Equal(3, me.Choice.Choices.Count);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, ChoiceOf(game, me, "YOP_024t").Id)));
        Assert.Equal(hand + 2, me.HandZone.Count);
        Assert.Equal(1, me.OverloadOwed);
    }

    [Fact]
    public void One_turn_cost_enchantments_change_the_cost_and_revert()
    {
        // Tenwu of the Red Smoke: the returned minion costs (1) this turn (DMF_071e, a one-turn ADD of 1 - Cost)
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var ogre = OnBoard(game, me, "CS2_200");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.MinionTarget(me, Give(me, "DMF_071"), ogre)));
        Assert.Equal(SabberStoneCore.Enums.Zone.HAND, ogre.Zone.Type);
        Assert.Equal(1, ogre.Cost);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.Equal(6, ogre.Cost);
    }

    [Fact]
    public void Corrupt_is_fixed_at_play_and_shown_like_live()
    {
        // live-20261006 (Pit Master): at the end of the PLAY block a TRIGGER block on the Corrupt card, TriggerKeyword
        // CORRUPT: TRANSFORMED_FROM_CARD, TARGET itself, the CHANGE_ENTITY (TIMES_BEEN_TRANSFORMED 1); before Combo
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var pit = Give(me, "DMF_703");
        int from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "CS2_182")))); // 4
        Assert.Equal("DMF_703t", pit.Card.Id);
        var after = game.PowerHistory.Full.Skip(from).ToList();
        int trigger = after.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart b && b.Source == pit.Id &&
            b.BlockType == BlockType.TRIGGER && b.TriggerKeyword == (int)HsTag.CORRUPT);
        Assert.True(trigger >= 0);
        var change = after.Skip(trigger).OfType<SabberStoneCore.Kettle.PowerHistoryChangeEntity>().First();
        Assert.Equal("DMF_703t", change.CardId);
        Assert.Equal(1, change.Entity.Tags[(GameTag)2321]);
        Assert.Equal(61225, pit[GameTag.TRANSFORMED_FROM_CARD]);

        // a Corrupt card the played card draws wasn't in hand when it was played: it stays as it is
        me.UsedMana = 0;
        var drawn = SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("DMF_703"), null, me.DeckZone);
        Assert.Same(drawn, me.DeckZone.TopCard);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "EX1_284")))); // Azure Drake: draw
        Assert.Equal(SabberStoneCore.Enums.Zone.HAND, drawn.Zone.Type);
        Assert.Equal("DMF_703", drawn.Card.Id);
    }

    [Fact]
    public void Dual_class_cards_belong_to_each_of_their_classes()
    {
        // client data: NEUTRAL + MULTIPLE_CLASSES (bit class - 1). User 2026-10-06 (Guidance): "a venn diagram of shared
        // class cards" — Shaman and Shaman dual-class spells, never a raw Druid card
        _ = Db.Value;
        var cutting = SabberStoneCore.Model.Cards.FromId("SCH_623"); // Rogue / Warrior
        Assert.True(cutting.HasClass(CardClass.ROGUE));
        Assert.True(cutting.HasClass(CardClass.WARRIOR));
        Assert.False(cutting.HasClass(CardClass.MAGE));
        Assert.False(cutting.HasClass(CardClass.NEUTRAL));
        var pools = SabberStoneCore.Model.Cards.FormatTypeClassCards(FormatType.FT_WILD);
        Assert.Contains(cutting, pools[CardClass.ROGUE]);
        Assert.DoesNotContain(cutting, pools[CardClass.MAGE]);

        var game = TenManaGame();
        var me = game.CurrentPlayer;
        for (int i = 0; i < 10; i++)
        {
            me.UsedMana = 0;
            me.OverloadOwed = 0;
            foreach (var held in me.HandZone.ToList()) me.HandZone.Remove(held);
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "YOP_024"))));
            var offered = me.Choice.Choices.Select(id => game.IdEntityDic[id].Card).Where(c => c.Id != "YOP_024t").ToList();
            Assert.All(offered, c => Assert.True(c.HasClass(me.HeroClass), c.Id));
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, me.Choice.Choices[0])));
        }
    }

    [Fact]
    public void Frenzy_fires_once_after_surviving_damage_like_live()
    {
        // wiki Frenzy: "A one-time effect after this survives damage"; live (Samuro): TRIGGER block keyword FRENZY right
        // after the damage, FRENZY = 0 at its end
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        var thrasher = OnBoard(game, me, "BAR_024"); // Oasis Thrasher 2/3 — Frenzy: 3 damage to the enemy hero
        int from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_008"), thrasher))); // Moonfire 1
        Assert.Equal(3, op.Hero.Damage);
        Assert.Equal(0, thrasher[HsTag.FRENZY]);
        var after = game.PowerHistory.Full.Skip(from).ToList();
        int block = after.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart b && b.Source == thrasher.Id && b.TriggerKeyword == (int)HsTag.FRENZY);
        Assert.True(block >= 0);
        int frenzyOff = after.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryTagChange tc && tc.EntityId == thrasher.Id && tc.Tag == HsTag.FRENZY && tc.Value == 0);
        int heroHit = after.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryTagChange tc && tc.EntityId == op.Hero.Id && tc.Tag == GameTag.DAMAGE);
        Assert.True(heroHit > block && frenzyOff > heroHit, $"block {block} hit {heroHit} off {frenzyOff}");
        // once only
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_008"), thrasher)));
        Assert.Equal(3, op.Hero.Damage);
        // a minion the damage kills didn't survive: no Frenzy
        var other = OnBoard(game, me, "BAR_024");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_029"), other))); // Fireball 6
        Assert.Equal(3, op.Hero.Damage);
    }

    [Fact]
    public void Ranked_spells_upgrade_with_mana_crystals_in_hand_and_silently_in_the_deck()
    {
        // live: a ranked spell entering the hand at 5+ Mana gets a TRIGGER block (effect index 2) with its CHANGE_ENTITY;
        // in the deck it changes silently (Fury mulliganed as Rank 1, drawn as Rank 3)
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.BaseMana = 4;
        var stab = Give(me, "BAR_319");
        var inDeck = SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("BAR_319"), null, me.DeckZone);
        Assert.Equal("BAR_319", stab.Card.Id);
        int from = game.PowerHistory.Full.Count;
        me.BaseMana = 5;
        Assert.Equal("BAR_319t", stab.Card.Id);
        Assert.Equal("BAR_319t", inDeck.Card.Id);
        var after = game.PowerHistory.Full.Skip(from).ToList();
        // (live 212735 8925: a Mana Crystal gain's rank-up is effect index 0, with the 1069 pulse of the new rank)
        Assert.Contains(after, e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart b && b.Source == stab.Id && b.BlockType == BlockType.TRIGGER && b.Index == 0);
        Assert.Contains(after, e => e is SabberStoneCore.Kettle.PowerHistoryTagChange tc && tc.EntityId == stab.Id && (int)tc.Tag == 1069 && tc.Value > 0);
        Assert.Contains(after, e => e is SabberStoneCore.Kettle.PowerHistoryChangeEntity ce && ce.Entity.Id == stab.Id);
        // (2026-10-09) the deck card's change is recorded — the stream must show the card as it is when it is revealed (a burned rank-up
        // reached the client under its old card) — but reaches no client: no 1069 pulse, no CHANGE_ENTITY to either player
        Assert.DoesNotContain(after, e => e is SabberStoneCore.Kettle.PowerHistoryTagChange tc && tc.EntityId == inDeck.Id && (int)tc.Tag == 1069);
        foreach (int viewer in new[] { 1, 2 })
            Assert.DoesNotContain(new ClientStream(viewer, _ => (0, 0, 0)).Convert(game.PowerHistory.Full), d => d.ChangeEntity?.Entity == inDeck.Id);
        me.BaseMana = 10;
        Assert.Equal("BAR_319t2", stab.Card.Id);
        // one way: fewer crystals don't lower it; a new one arrives at its rank
        me.BaseMana = 3;
        Assert.Equal("BAR_319t2", stab.Card.Id);
        me.BaseMana = 10;
        Assert.Equal("BAR_319t2", Give(me, "BAR_319").Card.Id);
    }

    [Fact]
    public void Sigils_fire_at_the_start_of_your_next_turn()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var yeti = OnBoard(game, me.Opponent, "CS2_182"); // 4/5
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "BAR_306")))); // Sigil of Flame
        Assert.Equal(0, yeti.Damage);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.Equal(0, yeti.Damage);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)));
        Assert.Equal(3, yeti.Damage);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)));
        Assert.Equal(3, yeti.Damage); // once
    }

    [Fact]
    public void Yogg_saron_spins_the_wheel_like_live()
    {
        // live-20261006-yogg-cthun: FX_DATANUM_1 = the outcome, the wheel sub-spell, the outcome set aside, revealed and
        // shown big; its effect runs inside Yogg-Saron's block (no PLAY block of its own)
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.NumSpellsPlayedThisGame = 10;
        var yogg = Give(me, "DMF_004");
        int from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, yogg)));
        int outcome = yogg[GameTag.FX_DATANUM_1];
        Assert.InRange(outcome, 1, 6);
        var after = game.PowerHistory.Full.Skip(from).ToList();
        Assert.Contains(after, e => e is SabberStoneCore.Kettle.PowerHistorySubSpellStart ss && ss.Prefab.Contains("YoggWheel"));
        Assert.Contains(after, e => e is SabberStoneCore.Kettle.PowerHistoryMetaData md && md.Type == MetaDataType.SHOW_BIG_CARD);
        Assert.DoesNotContain(after, e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart b && b.BlockType == BlockType.PLAY && b.Source != yogg.Id);
    }

    [Fact]
    public void Acrobatics_draws_two_more_after_both_drawn_cards_are_played()
    {
        // live-20261006-yogg-cthun: one DMF_249e on the player holding the two drawn cards (NUM_1 / NUM_2)
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        foreach (var held in me.HandZone.ToList()) me.HandZone.Remove(held);
        SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("CS2_168"), null, me.DeckZone);
        SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("CS2_168"), null, me.DeckZone);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "DMF_249"))));
        var mark = me.AppliedEnchantments.Single(e => e.Card.Id == "DMF_249e");
        Assert.Equal(2, me.HandZone.Count);
        Assert.Equal(me.HandZone[0].Id, mark[GameTag.TAG_SCRIPT_DATA_NUM_1]);
        Assert.Equal(me.HandZone[1].Id, mark[GameTag.TAG_SCRIPT_DATA_NUM_2]);
        var first = me.HandZone[0]; var second = me.HandZone[1];
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, first)));
        Assert.Single(me.HandZone);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, second)));
        Assert.Equal(2, me.HandZone.Count);
    }

    [Fact]
    public void Barrens_ruled_cards_follow_the_users_rulings()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        // Serena Bloodfeather (1/1): steals one point at a time until hers is higher; a 1/1 loses its last Health and dies
        var small = OnBoard(game, op, "CS2_168"); // 2/1
        var serena = Give(me, "BAR_315");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.MinionTarget(me, serena, small)));
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, small.Zone.Type);
        var yeti = OnBoard(game, op, "CS2_182"); // 4/5
        var serena2 = (SabberStoneCore.Model.Entities.Minion)Give(me, "BAR_315");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.MinionTarget(me, serena2, yeti)));
        Assert.True(serena2.AttackDamage > yeti.AttackDamage && serena2.Health > yeti.Health, $"{serena2.AttackDamage}/{serena2.Health} vs {yeti.AttackDamage}/{yeti.Health}");
        Assert.Equal(SabberStoneCore.Enums.Zone.PLAY, yeti.Zone.Type);

        // Yoink!: another class's base Hero Power at (0); after 2 uses yours comes back
        me.UsedMana = 0;
        string mine = me.Hero.HeroPower.Card.Id;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "BAR_323"))));
        Assert.Equal(3, me.Choice.Choices.Count);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, me.Choice.Choices[0])));
        Assert.NotEqual(mine, me.Hero.HeroPower.Card.Id);
        Assert.Equal(0, me.Hero.HeroPower.Cost);
        for (int use = 0; use < 2; use++)
        {
            me.Hero.HeroPower.IsExhausted = false;
            var option = me.Options().First(o => o is SabberStoneCore.Tasks.PlayerTasks.HeroPowerTask);
            Assert.True(game.Process(option));
        }
        Assert.Equal(mine, me.Hero.HeroPower.Card.Id);

        // Kazakus, Golem Shaper: golem, keyword herb, battlecry herb of its size
        var game2 = TenManaGame();
        var me2 = game2.CurrentPlayer;
        foreach (var card in me2.DeckZone.ToList()) if (card.Cost == 4) me2.DeckZone.Remove(card);
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me2, Give(me2, "BAR_079"))));
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me2, ChoiceOf(game2, me2, "BAR_079_m3").Id)));
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me2, me2.Choice.Choices[0])));
        Assert.All(me2.Choice.Choices.Select(id => game2.IdEntityDic[id].Card.Id),
            id => Assert.True(id.EndsWith("c") || id == "BAR_079t11", id));
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me2, me2.Choice.Choices[0])));
        var golem = me2.HandZone.Last();
        Assert.Equal("BAR_079_m3", golem.Card.Id);
        Assert.True(golem[GameTag.MODULAR_ENTITY_PART_1] > 0 && golem[GameTag.MODULAR_ENTITY_PART_2] > 0);

        // Kargal Battlescar: a Lookout per Watch Post summoned this game
        var game3 = TenManaGame();
        var me3 = game3.CurrentPlayer;
        Assert.True(game3.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me3, Give(me3, "BAR_074"))));
        OnBoard(game3, me3, "BAR_076");
        me3.UsedMana = 0;
        Assert.True(game3.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me3, Give(me3, "BAR_077"))));
        Assert.Equal(2, me3.BoardZone.Count(m => m.Card.Id == "BAR_077t"));
    }

    [Fact]
    public void Stormwind_mounts_weapons_and_trades()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        // Noble Mount: +1/+1 and Divine Shield; when the minion dies, a Warhorse takes its place
        var raider = OnBoard(game, me, "CS2_168");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "SW_316"), raider)));
        Assert.True(raider.HasDivineShield);
        raider.Destroy();
        game.DeathProcessingAndAuraUpdate();
        Assert.Contains(me.BoardZone, m => m.Card.Id == "SW_316t");

        // Shadowcloth Needle: after a Shadow spell, 1 damage to all enemies, then it loses 1 Durability
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, Give(me, "SW_012"))));
        int durability = me.Hero.Weapon.Durability;
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "DS1_233")))); // Mind Blast (Shadow)
        Assert.True(me.Hero.Weapon == null || me.Hero.Weapon.Durability == durability - 1);

        // Wicked Shipment: Upgrades by 2 when Traded
        var shipment = Give(me, "DED_504");
        int before = shipment[GameTag.TAG_SCRIPT_DATA_NUM_1] > 0 ? shipment[GameTag.TAG_SCRIPT_DATA_NUM_1] : shipment.Card[GameTag.TAG_SCRIPT_DATA_NUM_1];
        me.UsedMana = 0;
        Assert.True(SabberStoneCore.Actions.Generic.Trade(me, shipment));
        Assert.Equal(before + 2, shipment[GameTag.TAG_SCRIPT_DATA_NUM_1]);

        // Suckerhook: an equipped weapon transforms in place into one that costs (1) more
        var game2 = TenManaGame();
        var me2 = game2.CurrentPlayer;
        OnBoard(game2, me2, "DED_511");
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me2, Give(me2, "CS2_106")))); // Fiery War Axe
        int axeCost = me2.Hero.Weapon.Card.Cost;
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me2)));
        Assert.Equal(axeCost + 1, me2.Hero.Weapon.Card.Cost);
    }

    [Fact]
    public void Stormwind_rulings_and_rules()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        // Brilliant Macaw: repeats the last Battlecry at a random valid target; two Macaws don't loop
        var yeti = OnBoard(game, op, "CS2_182");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.MinionTarget(me, Give(me, "CS2_189"), yeti))); // Elven Archer: 1 damage
        var macawInHand = Give(me, "DED_509");
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(SabberStoneCore.Model.Cards.FromId("CS2_189").AssetId, macawInHand[(GameTag)1078]); // shows Elven Archer on hover
        Assert.True(SabberStoneCore.Model.Cards.FromId("AT_125")[GameTag.CANNOT_ATTACK_HEROES] > 0);  // Icehowl: from its text
        Assert.True(SabberStoneCore.Model.Cards.FromId("SW_314")[GameTag.CANNOT_ATTACK_HEROES] > 0);
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "DED_509"))));
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "DED_509"))));

        // Voidtouched Attendant: both heroes take one extra damage from all sources
        var game2 = TenManaGame();
        var me2 = game2.CurrentPlayer;
        OnBoard(game2, me2, "SW_446");
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me2, Give(me2, "DS1_233")))); // Mind Blast 5
        Assert.Equal(6, me2.Opponent.Hero.Damage);

        // Lightbringer's Hammer: the hero can't attack heroes, only minions
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me2, Give(me2, "SW_314"))));
        OnBoard(game2, me2.Opponent, "CS2_182");
        Assert.DoesNotContain(me2.Options(), o => o is SabberStoneCore.Tasks.PlayerTasks.HeroAttackTask h && h.Target == me2.Opponent.Hero);

        // Stockades Prisoner: Dormant; awakens after you play 3 cards
        var game3 = TenManaGame();
        var me3 = game3.CurrentPlayer;
        var prisoner = (SabberStoneCore.Model.Entities.Minion)Give(me3, "SW_077");
        Assert.True(game3.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me3, prisoner)));
        Assert.True(prisoner[GameTag.DORMANT] > 0);
        for (int i = 0; i < 3; i++) Assert.True(game3.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me3, Give(me3, "GAME_005"))));
        Assert.Equal(0, prisoner[GameTag.DORMANT]);

        // Proving Grounds: two minions from the deck fight each other
        var game4 = TenManaGame();
        var me4 = game4.CurrentPlayer;
        foreach (var card in me4.DeckZone.ToList()) me4.DeckZone.Remove(card);
        SabberStoneCore.Model.Entities.Entity.FromCard(me4, SabberStoneCore.Model.Cards.FromId("CS2_182"), null, me4.DeckZone);
        SabberStoneCore.Model.Entities.Entity.FromCard(me4, SabberStoneCore.Model.Cards.FromId("CS2_182"), null, me4.DeckZone);
        Assert.True(game4.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me4, Give(me4, "DED_508"))));
        Assert.Equal(2, me4.BoardZone.Count);
        Assert.All(me4.BoardZone, m => Assert.Equal(4, m.Damage));

        // Maestra of the Masquerade: starts as another class (hero + Hero Power) until a Rogue card is played
        var game5 = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.ROGUE, Player2HeroClass = CardClass.MAGE, History = true, Shuffle = false,
            FillDecks = true, FillDecksPredictably = true,
            Player1Deck = new List<SabberStoneCore.Model.Card> { SabberStoneCore.Model.Cards.FromId("SW_050") }
        });
        game5.StartGame();
        var rogue = game5.Player1;
        Assert.NotEqual(CardClass.ROGUE, rogue.Hero.Card.Class);
        Assert.Equal(rogue.Hero.Card.Class, rogue.HeroClass); // disguised, you are that class (user 2026-10-06)
        string disguisedPower = rogue.Hero.HeroPower.Card.Id;
        rogue.BaseMana = 10;
        Assert.True(game5.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(rogue, Give(rogue, "EX1_129")))); // Fan of Knives (a Rogue card)
        Assert.Equal(CardClass.ROGUE, rogue.Hero.Card.Class);
        Assert.Equal(CardClass.ROGUE, rogue.HeroClass);
        Assert.NotEqual(disguisedPower, rogue.Hero.HeroPower.Card.Id);
        // (live vfx-decks 85210 / 85750: built disguised — no CHANGE_ENTITY of the hero; the reveal makes a NEW hero copied from the disguise)
        Assert.DoesNotContain(game5.PowerHistory.Full, e => e is SabberStoneCore.Kettle.PowerHistoryChangeEntity ce && ce.Entity.Id == rogue.Hero.Id);
        Assert.True(rogue.Hero[SabberStoneCore.Enums.HsTag.COPIED_FROM_ENTITY_ID] > 0);
        Assert.Contains(game5.PowerHistory.Full, e => e is SabberStoneCore.Kettle.PowerHistoryMetaData { Type: MetaDataType.BEGIN_ARTIFICIAL_HISTORY_TRIGGER_TILE });
    }

    [Fact]
    public void Alterac_shared_rules()
    {
        // Honorable Kill (wiki): exact lethal damage on its owner's turn; Overkill doesn't count
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        var gnome = OnBoard(game, me, "AV_121");               // Gnome Private 1/3: +2 Attack on Honorable Kill
        var wisp = OnBoard(game, op, "CS2_231");               // 1/1
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(me, gnome, wisp)));
        Assert.Equal(3, gnome.AttackDamage);
        var gnome2 = OnBoard(game, me, "AV_121");
        var yeti = OnBoard(game, op, "CS2_182");               // 4/5: no kill
        var wisp2 = OnBoard(game, op, "CS2_231");
        gnome2.AttackDamage += 0;
        Give(me, "CS2_092");                                   // (unused card in hand)
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(me, gnome2, yeti)));
        Assert.Equal(1, gnome2.AttackDamage);

        // Korrak: resummoned unless Honorably Killed
        var korrak = OnBoard(game, op, "AV_143");              // 3/5
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_029"), korrak))); // Fireball 6: overkill
        Assert.Contains(op.BoardZone, m => m.Card.Id == "AV_143");

        // Objective "Lasts 3 turns": Frostwolf Kennels summons a Wolf at the end of each of your 3 turns, then is gone
        var game2 = TenManaGame();
        var me2 = game2.CurrentPlayer;
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me2, Give(me2, "AV_360"))));
        Assert.Contains(me2.SecretZone, p => p.Card.Id == "AV_360");
        for (int turn = 0; turn < 3; turn++)
        {
            Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me2)));
            Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me2.Opponent)));
        }
        Assert.Equal(3, me2.BoardZone.Count(m => m.Card.Id == "AV_211t"));
        Assert.DoesNotContain(me2.SecretZone, p => p.Card.Id == "AV_360");

        // Frozen Mammoth: Frozen through turn ends until you cast a Fire spell
        var game3 = TenManaGame();
        var me3 = game3.CurrentPlayer;
        var mammoth = (SabberStoneCore.Model.Entities.Minion)Give(me3, "AV_128");
        Assert.True(game3.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me3, mammoth)));
        Assert.True(mammoth.IsFrozen);
        Assert.True(game3.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me3)));
        Assert.True(game3.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me3.Opponent)));
        Assert.True(game3.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me3)));
        Assert.True(game3.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me3.Opponent)));
        Assert.True(mammoth.IsFrozen);
        Assert.True(game3.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me3, Give(me3, "CS2_029"), me3.Opponent.Hero)));
        Assert.False(mammoth.IsFrozen);

        // "Repeatable this turn" works like Echo (wiki Echo): Full-Blown Evil comes back to the hand
        var game4 = TenManaGame();
        var me4 = game4.CurrentPlayer;
        OnBoard(game4, me4.Opponent, "CS2_182");
        Assert.True(game4.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me4, Give(me4, "AV_285"))));
        Assert.Contains(me4.HandZone, p => p.Card.Id == "AV_285");

        // Raid Boss Onyxia: Immune while you control a Whelp; Ivus' gifts load (collectible token-style id)
        var game5 = TenManaGame();
        var me5 = game5.CurrentPlayer;
        var onyxia = (SabberStoneCore.Model.Entities.Minion)Give(me5, "ONY_004");
        Assert.True(game5.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me5, onyxia)));
        Assert.True(onyxia.IsImmune);
        foreach (var whelp in me5.BoardZone.Where(m => m.Card.Id == "ONY_001t").ToList()) whelp.Destroy();
        game5.DeathProcessingAndAuraUpdate();
        Assert.False(onyxia.IsImmune);
        Assert.NotNull(SabberStoneCore.Model.Cards.TryFromId("AV_142e3"));
    }

    [Fact]
    public void Sunken_City_shared_rules()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;

        // Dredge (wiki): the bottom 3 offered (bottom-most first), the pick goes on top
        var bottom = me.DeckZone.Take(3).ToList();
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "TSC_909"))));
        Assert.Equal(3, me.Choice.Choices.Count);
        Assert.Equal(bottom[0].Id, game.IdEntityDic[me.Choice.Choices[0]][GameTag.LINKED_ENTITY]);
        int picked = game.IdEntityDic[me.Choice.Choices[1]][GameTag.LINKED_ENTITY];
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, me.Choice.Choices[1])));
        Assert.Equal(picked, me.DeckZone.TopCard.Id);

        // Colossal (wiki): appendages with the body, before its Battlecry; Hydralodon's Battlecry gives its Heads Rush
        me.UsedMana = 0;
        var hydra = (SabberStoneCore.Model.Entities.Minion)Give(me, "TSC_950");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, hydra)));
        var heads = me.BoardZone.Where(m => m.Card.Id.StartsWith("TSC_950t")).ToList();
        Assert.Equal(2, heads.Count);
        Assert.Contains(heads, h => h.ZonePosition > hydra.ZonePosition);   // one to its right
        Assert.Contains(heads, h => h.ZonePosition < hydra.ZonePosition);   // one to its left (user 2026-10-06)
        Assert.All(heads, h => Assert.True(h.IsRush));

        // "If you've cast three spells while holding this": the card counts (TAG_SCRIPT_DATA_NUM_1) and glows when Ready
        var maiden = Give(me, "TSC_212");
        for (int i = 0; i < 4; i++) // a 4th spell isn't counted: it stops at Ready (user 2026-10-06)
        {
            me.UsedMana = 0;
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "EX1_277")))); // Arcane Missiles
        }
        Assert.Equal(3, maiden[GameTag.TAG_SCRIPT_DATA_NUM_1]);
        Assert.True(SabberStoneCore.Actions.Generic.ThresholdMet(maiden));

        // Abyssal Curse: 1 damage, each next one 1 more; at the start of the holder's turn; gone after 2 turns
        var game2 = TenManaGame();
        var w = game2.CurrentPlayer;
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(w, Give(w, "TSC_955"))));
        w.UsedMana = 0;
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(w, Give(w, "TSC_955"))));
        var curses = w.Opponent.HandZone.Where(p => p.Card.Id == "TSC_955t").ToList();
        Assert.Equal(new[] { 1, 2 }, curses.Select(p => p[GameTag.TAG_SCRIPT_DATA_NUM_1]).ToArray());
        int hpBefore = w.Opponent.Hero.Health;
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(w)));
        Assert.Equal(hpBefore - 3, w.Opponent.Hero.Health);
        Assert.Equal(1, curses[0][GameTag.TAG_SCRIPT_DATA_NUM_2]);                                    // after the hit: 2 -> 1
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(w.Opponent)));
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(w)));
        Assert.Equal(hpBefore - 6, w.Opponent.Hero.Health);                                            // the second hit, then gone
        Assert.DoesNotContain(w.Opponent.HandZone, p => p.Card.Id == "TSC_955t");

        // card data read from the text: Bru'kan's Nature Spell Damage +3, Helmet Hermit "Can't attack."
        Assert.Equal(3, SabberStoneCore.Model.Cards.FromId("BAR_048")[(GameTag)1948]);
        Assert.Equal(0, SabberStoneCore.Model.Cards.FromId("SW_112")[(GameTag)1946]);
        Assert.True(SabberStoneCore.Model.Cards.FromId("TSC_065")[GameTag.CANT_ATTACK] > 0);

        // Glugg (live-20261006-glugg): three Tails, all to its right in id order
        var game4 = TenManaGame();
        var gl = game4.CurrentPlayer;
        var glugg = (SabberStoneCore.Model.Entities.Minion)Give(gl, "TSC_639");
        Assert.True(game4.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(gl, glugg)));
        Assert.Equal(new[] { "TSC_639", "TSC_639t", "TSC_639t2", "TSC_639t3" }, gl.BoardZone.Select(m => m.Card.Id).ToArray());

        // Neptulon: while he has Hands, they attack instead
        var game3 = TenManaGame();
        var n = game3.CurrentPlayer;
        var neptulon = OnBoard(game3, n, "TID_712");
        var foe = OnBoard(game3, n.Opponent, "CS2_182");
        var hands = n.BoardZone.Where(m => m.Card.Id.StartsWith("TID_712t")).ToList();
        Assert.Equal(2, hands.Count);
        game3.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(n, neptulon, foe)); // his attack is replaced (used up)
        Assert.Equal(0, neptulon.Damage);                       // he didn't fight
        Assert.True(foe.IsDead || foe.Damage >= 8);
    }

    [Fact]
    public void Kazakusan_crafts_a_treasure_deck()
    {
        // wiki Kazakusan: remove your deck, Discover a Treasure and add 2 copies, 5 times; Blade + Hilt make 2 Quel'Delars
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        for (int i = 0; i < 3; i++)
        {
            me.UsedMana = 0;
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "ONY_001t"))));
        }
        me.UsedMana = 0;
        var early = Give(me, "ONY_005");
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(3, early[GameTag.TAG_SCRIPT_DATA_NUM_1]);  // the client shows "(1 left!)": threshold 4 - 3
        Assert.Equal(4, early.Card[(GameTag)2460]);
        int Glow(SabberStoneCore.Model.Entities.IPlayable p) =>
            game.PowerHistory.Reported.TryGetValue(p.Id, out var tags) && tags.TryGetValue(GameTag.POWERED_UP, out int v) ? v : 0;
        Assert.Equal(0, Glow(early));                           // not Ready yet: no yellow glow
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, early)));
        Assert.Null(me.Choice);                                // only 3 other Dragons: nothing
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Give(me, "ONY_001t"))));
        var ready = Give(me, "ONY_005");
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(1, Glow(ready));                           // 4 other Dragons: "(Ready!)" and the yellow glow
        Give(me, "ONY_005tc5");                                // a Blade and a Hilt already in hand: they count too (wiki)
        Give(me, "ONY_005tc6");
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, ready)));
        var picked = new List<string>();
        for (int round = 0; round < 5; round++)
        {
            Assert.NotNull(me.Choice);
            int pick = me.Choice.Choices.First();
            picked.Add(game.IdEntityDic[pick].Card.Id);
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, pick)));
        }
        Assert.Null(me.Choice);
        Assert.Equal(5, picked.Distinct().Count());
        int pieces = picked.Count(id => id == "ONY_005tc5" || id == "ONY_005tc6");
        Assert.Equal(10 - 2 * pieces + 2, me.DeckZone.Count); // the pieces became 2 Quel'Delars ("Forging Quel'Delar" cast)
        Assert.All(me.DeckZone, p => Assert.StartsWith("ONY_005t", p.Card.Id));
        Assert.Equal(2, me.DeckZone.Count(p => p.Card.Id == "ONY_005tc7"));
        Assert.DoesNotContain(me.HandZone, p => p.Card.Id == "ONY_005tc5" || p.Card.Id == "ONY_005tc6");
        Assert.Contains(me.GraveyardZone, p => p.Card.Id == "ONY_005tc7t");
    }

    [Fact]
    public void Flightmaster_Dungar_flies_like_live()
    {
        // live-20261006-dungar-student: the pick sets FX_DATANUM_1, Dungar sleeps under its Flight counter, a cast copy of
        // the path puts its Flight enchantment on the player; after the count the counter wakes Dungar and the bonus plays
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.Hero.Damage = 20;
        var dungar = (SabberStoneCore.Model.Entities.Minion)Give(me, "SW_079");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, dungar)));
        Assert.Equal(1, dungar[GameTag.TAG_SCRIPT_DATA_NUM_1]);
        var ironforge = me.Choice.Choices.First(id => game.IdEntityDic[id].Card.Id == "SW_079t2");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, ironforge)));
        Assert.Equal(2, dungar[GameTag.FX_DATANUM_1]);
        Assert.True(dungar[GameTag.DORMANT] > 0);
        Assert.Contains(me.AppliedEnchantments, e => e.Card.Id == "SW_079t2e");
        Assert.Contains(me.GraveyardZone, p => p.Card.Id == "SW_079t2"); // the cast copy and the picked option
        for (int turn = 0; turn < 3; turn++)
        {
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me.Opponent)));
        }
        Assert.Equal(0, dungar[GameTag.DORMANT]);
        Assert.Equal(10, me.Hero.Damage);
    }

    [Fact]
    public void Shenanigans_turns_the_opponents_second_draw_into_a_banana()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var op = me.Opponent;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Give(me, "YOP_017"))));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.Equal(1, op.NumCardsDrawnThisTurn);
        var second = SabberStoneCore.Actions.Generic.Draw(op);
        Assert.Equal("EX1_014t", game.IdEntityDic[second.Id].Card.Id);
        Assert.Equal(op, game.IdEntityDic[second.Id].Controller); // a transform keeps the card its owner's
        Assert.Empty(me.SecretZone);
    }

    /// <summary>The enemy hero dies now, or the board's ready attackers (no Taunt in the way) and the Hero Power reach it.</summary>
    private static bool LethalNow(SabberStoneCore.Model.Entities.Controller m)
    {
        int Reach() => m.Opponent.BoardZone.Any(x => x.HasTaunt) ? 0 :
            m.BoardZone.Where(x => x.CanAttack && !x.CantAttackHeroes).Sum(x => x.AttackDamage) + (m.Hero.CanAttack ? m.Hero.AttackDamage : 0);
        if (m.Opponent.Hero.IsDead || Reach() >= m.Opponent.Hero.Health + m.Opponent.Hero.Armor) return true;
        var power = m.Options().OfType<SabberStoneCore.Tasks.PlayerTasks.HeroPowerTask>()
            .FirstOrDefault(o => o.Target == null || o.Target.Id == m.Opponent.Hero.Id);
        return power != null && m.Game.Process(power) && (m.Opponent.Hero.IsDead || Reach() >= m.Opponent.Hero.Health + m.Opponent.Hero.Armor);
    }

    /// <summary>Some play of the card (any target / Choose One option) leaves the game in a state <paramref name="ok"/> accepts.</summary>
    private static bool SomePlay(SabberStoneCore.Model.Game game, SabberStoneCore.Model.Entities.Controller me, SabberStoneCore.Model.Card card,
        Func<SabberStoneCore.Model.Entities.Controller, bool> ok)
    {
        var probe = game.Clone();
        var pm = probe.ControllerById(me.Id);
        var p = SabberStoneCore.Model.Entities.Entity.FromCard(pm, card, zone: pm.HandZone);
        foreach (var o in pm.Options().OfType<SabberStoneCore.Tasks.PlayerTasks.PlayCardTask>().Where(o => o.Source == p).ToList())
        {
            var sim = game.Clone();
            var sm = sim.ControllerById(me.Id);
            var sp = SabberStoneCore.Model.Entities.Entity.FromCard(sm, card, zone: sm.HandZone);
            var t = o.Target == null ? null : (SabberStoneCore.Model.Entities.ICharacter)sim.IdEntityDic[o.Target.Id];
            if (sim.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(sm, sp, t, -1, o.ChooseOne)) && ok(sm)) return true;
        }
        return false;
    }

    [Fact]
    public void Zephrys_wishes_for_the_perfect_card()
    {
        // the pool (wiki Zephrys the Great): Basic and Classic, no Hall of Fame, no return-to-hand, no vanilla minions
        _ = Db.Value;
        var pool = SabberStoneCore.Actions.Generic.WishPool.Select(x => x.Id).ToHashSet();
        output.WriteLine($"wish pool: {pool.Count} cards");
        foreach (string id in new[] { "CS2_029", "CS2_032", "CS2_012", "EX1_066", "CS2_093", "EX1_354" }) Assert.Contains(id, pool);
        foreach (string id in new[] { "CS2_031", "EX1_625", "EX1_016", "CS2_172", "EX1_144", "EX1_049", "EX1_057", "LEG_RLK_125",
            "CS3_016", "GIFT_04", "CORE_CS2_029", "ULD_003", "CS1_130_Puzzle", "TU5_CS2_029", "HERO_01" })
            Assert.DoesNotContain(id, pool);

        // lethal now: the enemy hero at 5, an empty board — the first offer finishes it
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.Opponent.Hero.Damage = me.Opponent.Hero.BaseHealth - 5;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var offer = SabberStoneCore.Actions.Generic.WishOffer(game, me);
        output.WriteLine($"lethal offer ({sw.ElapsedMilliseconds} ms): {string.Join(", ", offer.Select(x => x.Name))}");
        Assert.Equal(3, offer.Count);
        Assert.Equal(3, offer.Distinct().Count());
        Assert.True(SomePlay(game, me, offer[0], m => LethalNow(m))); // dead, or the board (and Hero Power) now have the damage

        // a wide enemy board: one offer clears two or more of it
        var game2 = TenManaGame();
        var me2 = game2.CurrentPlayer;
        for (int i = 0; i < 4; i++)
            SabberStoneCore.Model.Entities.Entity.FromCard(me2.Opponent, SabberStoneCore.Model.Cards.FromId("CS2_172"), zone: me2.Opponent.BoardZone);
        sw.Restart();
        var offer2 = SabberStoneCore.Actions.Generic.WishOffer(game2, me2);
        output.WriteLine($"board offer ({sw.ElapsedMilliseconds} ms): {string.Join(", ", offer2.Select(x => x.Name))}");
        Assert.Contains(offer2, card => SomePlay(game2, me2, card, m => m.Opponent.BoardZone.Count(x => !x.IsDead) <= 2));

        // low on health (6) against two 4/5s at 4 Mana: something that keeps the player alive (heal, Taunt, Freeze, removal)
        var game4 = TenManaGame();
        var me4 = game4.CurrentPlayer;
        me4.BaseMana = 4;
        me4.Hero.Damage = me4.Hero.BaseHealth - 6;
        for (int i = 0; i < 2; i++)
            SabberStoneCore.Model.Entities.Entity.FromCard(me4.Opponent, SabberStoneCore.Model.Cards.FromId("CS2_119"), zone: me4.Opponent.BoardZone);
        sw.Restart();
        var offer4 = SabberStoneCore.Actions.Generic.WishOffer(game4, me4);
        output.WriteLine($"low offer ({sw.ElapsedMilliseconds} ms): {string.Join(", ", offer4.Select(x => x.Name))}");
        Assert.Contains(offer4, card => SomePlay(game4, me4, card, m => m.Hero.Health + m.Hero.Armor > 6 || m.BoardZone.Any(x => x.HasTaunt)
            || m.Opponent.BoardZone.Count(x => !x.IsDead && !x.IsFrozen && x.AttackDamage > 0) < 2));

        // turn 1, coined out (no Mana left), nothing on either board: ramp for next turn (user 2026-10-06: often Wild Growth)
        var game5 = TenManaGame();
        var me5 = game5.CurrentPlayer;
        me5.BaseMana = 1;
        me5.TemporaryMana = 1;
        me5.UsedMana = 2;
        var offer5 = SabberStoneCore.Actions.Generic.WishOffer(game5, me5);
        output.WriteLine($"turn-1 offer: {string.Join(", ", offer5.Select(x => x.Name))}");
        Assert.Contains(offer5, x => x.Id == "CS2_013"); // Wild Growth

        // a friendly Ancient Watcher that "can't attack" (ready) and the enemy at 4: the first offer gives lethal — wiki: Zephrys
        // understands "can't attack" (a Silence frees it) and burn; deterministic: the same board, the same three cards
        var game6 = TenManaGame();
        var me6 = game6.CurrentPlayer;
        var watcher = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me6, SabberStoneCore.Model.Cards.FromId("EX1_045"), zone: me6.BoardZone);
        watcher.IsExhausted = false;
        me6.Opponent.Hero.Damage = me6.Opponent.Hero.BaseHealth - 4;
        var offer6 = SabberStoneCore.Actions.Generic.WishOffer(game6, me6);
        output.WriteLine($"watcher offer: {string.Join(", ", offer6.Select(x => x.Name))}");
        Assert.True(SomePlay(game6, me6, offer6[0], m => LethalNow(m)));
        Assert.Equal(offer6.Select(x => x.Id), SabberStoneCore.Actions.Generic.WishOffer(game6, me6).Select(x => x.Id));

        // no Mana left, 10 next turn, the enemy at 10: Pyroblast for next turn's lethal (wiki)
        var game7 = TenManaGame();
        var me7 = game7.CurrentPlayer;
        me7.BaseMana = 9;
        me7.UsedMana = 9;
        me7.Opponent.Hero.Damage = me7.Opponent.Hero.BaseHealth - 10;
        var offer7 = SabberStoneCore.Actions.Generic.WishOffer(game7, me7);
        output.WriteLine($"next-turn offer: {string.Join(", ", offer7.Select(x => x.Name))}");
        Assert.Contains(offer7, x => x.Id == "EX1_279"); // Pyroblast

        // Zephrys himself: a deck without duplicates, the offer goes to the hand
        var game3 = TenManaGame();
        var z = game3.CurrentPlayer;
        while (z.DeckZone.Count > 0) z.DeckZone.Remove(z.DeckZone[0]);
        var zephrys = Give(z, "ULD_003");
        Assert.True(game3.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(z, zephrys)));
        Assert.NotNull(z.Choice);
        Assert.Equal(3, z.Choice.Choices.Count);
        int hand = z.HandZone.Count;
        Assert.True(game3.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(z, z.Choice.Choices[0])));
        Assert.Equal(hand + 1, z.HandZone.Count);
    }

    [Fact]
    public void Secrets_die_and_generated_cards_arrive_like_live()
    {
        // Dark Cultist (FP1_023): "Deathrattle: Give a random friendly minion +3 Health." (it gave nothing: the pick was lost)
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var cultist = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("FP1_023"), zone: me.BoardZone);
        var friend = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("CS2_182"), zone: me.BoardZone);
        var bolt = Give(me, "CS2_029");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, bolt, cultist)));
        Assert.Equal(5 + 3, friend.Health);

        // Thoughtsteal (EX1_339): the two copies fly in inside ONE SpawnToHand effect (live: one effect's cards arrive at once)
        int before = game.PowerHistory.Full.Count;
        var steal = Give(me, "EX1_339");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, steal)));
        var block = game.PowerHistory.Full.Skip(before).ToList();
        var starts = block.OfType<SabberStoneCore.Kettle.PowerHistorySubSpellStart>().Where(x => x.Prefab.Contains("SpawnToHand")).ToList();
        Assert.Single(starts);
        int open = block.IndexOf(starts[0]);
        int close = block.FindIndex(open, x => x is SabberStoneCore.Kettle.PowerHistorySubSpellEnd);
        Assert.Equal(2, block.Skip(open).Take(close - open).OfType<SabberStoneCore.Kettle.PowerHistoryFullEntity>()
            .Count(f => f.Entity.Tags.TryGetValue(GameTag.ZONE, out int z) && z == (int)SabberStoneCore.Enums.Zone.HAND));

        // Eater of Secrets / Chief Inspector (live-20261006-secrets-generators): each Secret named (TARGET), shown into the
        // graveyard with LAST_AFFECTED_BY = the destroyer, not revealed; Eater grows one enchantment, +1/+1 per Secret
        foreach (string destroyer in new[] { "OG_254", "GIL_648" })
        {
            var g2 = TenManaGame();
            var p = g2.CurrentPlayer;
            var secrets = new[] { "EX1_554", "EX1_610", "EX1_611" }.Select(id =>
            {
                var sp = (SabberStoneCore.Model.Entities.Spell)SabberStoneCore.Model.Entities.Entity.FromCard(p.Opponent, SabberStoneCore.Model.Cards.FromId(id));
                Assert.True(SabberStoneCore.Actions.Generic.PutSecretIntoPlay(p.Opponent, sp));
                return sp;
            }).ToList();
            int b2 = g2.PowerHistory.Full.Count;
            var card = (SabberStoneCore.Model.Entities.Minion)Give(p, destroyer);
            Assert.True(g2.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(p, card)));
            Assert.Empty(p.Opponent.SecretZone);
            var h = g2.PowerHistory.Full.Skip(b2).ToList();
            foreach (var sp in secrets)
            {
                Assert.Equal(card.Id, sp[GameTag.LAST_AFFECTED_BY]);
                Assert.Equal(0, sp[GameTag.REVEALED]);
                Assert.Contains(h, x => x is SabberStoneCore.Kettle.PowerHistoryMetaData { Type: MetaDataType.TARGET } m && m.Info.Count == 1 && m.Info[0] == sp.Id);
            }
            if (destroyer == "OG_254")
            {
                Assert.Equal(card.Card.ATK + 3, card.AttackDamage);   // 2/4 → 5/7
                Assert.Equal(card.Card.Health + 3, card.Health);
                Assert.Single(card.AppliedEnchantments, e => e.Card.Id == "OG_254e");
            }
        }
    }

    [Fact]
    public void Lich_King_shared_rules()
    {
        SabberStoneCore.Model.Entities.Minion OnBoard(SabberStoneCore.Model.Entities.Controller c, string id) =>
            (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(c, SabberStoneCore.Model.Cards.FromId(id), zone: c.BoardZone);
        var P = typeof(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask);

        // Manathirst (wiki: permanent Mana Crystals): Arcane Bolt deals 2, at 8 Mana Crystals 3
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.BaseMana = 7;
        var bolt = Give(me, "RLK_843");
        Assert.False(SabberStoneCore.Actions.Generic.Manathirst(bolt));
        int hp = me.Opponent.Hero.Health;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, bolt, me.Opponent.Hero)));
        Assert.Equal(hp - 2, me.Opponent.Hero.Health);
        me.BaseMana = 8;
        var bolt2 = Give(me, "RLK_843");
        Assert.True(SabberStoneCore.Actions.Generic.Manathirst(bolt2));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, bolt2, me.Opponent.Hero)));
        Assert.Equal(hp - 5, me.Opponent.Hero.Health);

        // "If a friendly Undead died after your last turn" (Bone Flinger): no target until one died
        var game2 = TenManaGame();
        var p2 = game2.CurrentPlayer;
        var flinger = Give(p2, "RLK_123");
        Assert.False(SabberStoneCore.Actions.Generic.UndeadDiedAfterYourLastTurn(p2));
        var egg = OnBoard(p2, "RLK_070t"); // Undead Peasant
        var killer = Give(p2, "CS2_029");
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(p2, killer, egg)));
        Assert.True(SabberStoneCore.Actions.Generic.UndeadDiedAfterYourLastTurn(p2));
        int ohp = p2.Opponent.Hero.Health;
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.MinionTarget(p2, flinger, p2.Opponent.Hero)));
        Assert.Equal(ohp - 2, p2.Opponent.Hero.Health);

        // Concoctions: two Concoctors — the second Concoction mixes with the first (one Mixed Concoction in hand, Cost 3)
        var game3 = TenManaGame();
        var p3 = game3.CurrentPlayer;
        foreach (var x in p3.HandZone.ToList()) p3.HandZone.Remove(x);
        for (int i = 0; i < 2; i++) Assert.True(game3.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(p3, Give(p3, "RLK_568"))));
        var concoctions = p3.HandZone.Where(x => x.Card[(GameTag)2500] > 0).ToList();
        Assert.Single(concoctions);
        Assert.Equal("Mixed Concoction", concoctions[0].Card.Name);
        Assert.Equal(3, concoctions[0].Cost);

        // Scourge Troll: a given Deathrattle (Deathweaver Aura: two 3/2 Zombies) twice
        var game4 = TenManaGame();
        var p4 = game4.CurrentPlayer;
        var troll = OnBoard(p4, "RLK_912");
        Assert.True(game4.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(p4, Give(p4, "RLK_909"), troll)));
        Assert.True(game4.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(p4, Give(p4, "CS2_029"), troll)));
        Assert.Equal(4, p4.BoardZone.Count(m => m.Card.Id == "RLK_909t"));

        // Baroness Vashj: Hex summons the Frog instead; she stays
        var game5 = TenManaGame();
        var p5 = game5.CurrentPlayer;
        var vashj = OnBoard(p5, "REV_925");
        Assert.True(game5.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(p5, Give(p5, "EX1_246"), vashj)));
        Assert.Equal("REV_925", vashj.Card.Id);
        Assert.Contains(p5.BoardZone, m => m.Card.Id == "hexfrog");

        // Anub'Rekhan: 5 Armor; the next minion costs Armor instead of Mana
        var game6 = TenManaGame();
        var p6 = game6.CurrentPlayer;
        var yetiInHand = Give(p6, "CS2_182");
        Assert.True(game6.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(p6, Give(p6, "RLK_659"))));
        Assert.Equal(5, p6.Hero.Armor);
        int mana = p6.RemainingMana;
        Assert.True(game6.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(p6, yetiInHand)));
        Assert.Equal(mana, p6.RemainingMana);
        Assert.Equal(1, p6.Hero.Armor);

        // Enchanter: an enemy minion takes double damage on its owner's turn; Silvermoon Arcanist: spells can't target heroes
        var game7 = TenManaGame();
        var p7 = game7.CurrentPlayer;
        OnBoard(p7, "RLK_952");
        var yeti = OnBoard(p7.Opponent, "CS2_182");
        Assert.True(game7.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(p7, Give(p7, "CS2_024"), yeti))); // Frostbolt 3
        Assert.True(yeti.IsDead || yeti.Health <= 5 - 6);
        Assert.True(game7.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(p7, Give(p7, "RLK_218"))));
        var fireball = Give(p7, "CS2_029");
        Assert.False(((SabberStoneCore.Model.Entities.Spell)fireball).TargetingRequirements(p7.Opponent.Hero));

        // Bonelord Frostwhisper: "You die in 3 turns" — the hero dies when the third of its turns ends
        var game8 = TenManaGame();
        var p8 = game8.CurrentPlayer;
        var bonelord = OnBoard(p8, "RLK_591");
        Assert.True(game8.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(p8, Give(p8, "EX1_246"), bonelord)));
        var lord = OnBoard(p8, "RLK_591");
        Assert.True(game8.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(p8, Give(p8, "CS2_029"), lord)));
        Assert.Contains(p8.AppliedEnchantments, e => e.Card.Id == "RLK_591e");
        for (int turn = 0; turn < 2; turn++)
        {
            Assert.True(game8.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(p8)));
            Assert.False(p8.Hero.IsDead);
            Assert.True(game8.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(p8.Opponent)));
        }
        Assert.True(game8.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(p8)));
        Assert.True(p8.Hero.IsDead || p8.Hero.ToBeDestroyed);
    }

    [Fact]
    public void Shuffles_targets_and_generators_follow_the_rules()
    {
        SabberStoneCore.Model.Entities.Minion OnBoard(SabberStoneCore.Model.Entities.Controller c, string id) =>
            (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(c, SabberStoneCore.Model.Cards.FromId(id), zone: c.BoardZone);

        // wiki Shuffle into deck: "the entire deck is shuffled" — a card put on top loses its place (10 tries: never stuck)
        int moved = 0;
        for (int i = 0; i < 10; i++)
        {
            var g = TenManaGame();
            var c = g.CurrentPlayer;
            var top = c.DeckZone[c.DeckZone.Count - 1];
            SabberStoneCore.Actions.Generic.ShuffleIntoDeck.Invoke(c, c.Hero, SabberStoneCore.Model.Entities.Entity.FromCard(c, SabberStoneCore.Model.Cards.FromId("CS2_029")));
            if (c.DeckZone[c.DeckZone.Count - 1] != top) moved++;
        }
        Assert.True(moved >= 5);

        // Zentimo: "also targets" adjacent ones — not an Elusive one (Faerie Dragon); a plain neighbour is hit
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        OnBoard(me, "TRL_085");
        var left = OnBoard(me.Opponent, "NEW1_023");   // Elusive
        var mid = OnBoard(me.Opponent, "CS2_182");
        var right = OnBoard(me.Opponent, "CS2_182");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_024"), mid))); // Frostbolt
        Assert.False(left.IsFrozen);
        Assert.Equal(left.BaseHealth, left.Health);
        Assert.True(right.IsFrozen);

        // Lady Naz'jar (user 2026-10-07): a new form after each spell of another school
        var game2 = TenManaGame();
        var p2 = game2.CurrentPlayer;
        var najar = Give(p2, "TID_709");
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(p2, Give(p2, "CS2_023")))); // Arcane Intellect
        Assert.Equal("TID_709t", game2.IdEntityDic[najar.Id].Card.Id);
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(p2, Give(p2, "CS2_029"), p2.Opponent.Hero))); // Fireball
        Assert.Equal("TID_709t2", game2.IdEntityDic[najar.Id].Card.Id);

        // Mimiron: a Mech played → one of the six Gadgets
        var game3 = TenManaGame();
        var p3 = game3.CurrentPlayer;
        OnBoard(p3, "TTN_920");
        Assert.True(game3.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(p3, Give(p3, "CORE_GVG_085"))));
        Assert.Contains(p3.HandZone, x => x.Card.Id.StartsWith("TTN_920t"));
    }

    [Fact]
    public void Festival_shared_rules()
    {
        SabberStoneCore.Model.Entities.Minion OnBoard(SabberStoneCore.Model.Entities.Controller c, string id) =>
            (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(c, SabberStoneCore.Model.Cards.FromId(id), zone: c.BoardZone);

        // Finale (wiki: "the player's remaining mana must be 0 when it is played"): Audio Medic gains Lifesteal only then
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me.UsedMana = 8; // 2 left
        var medic = Give(me, "ETC_325");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, medic)));
        Assert.Equal(1, medic[SabberStoneCore.Enums.GameTag.LIFESTEAL]);
        var medic2 = Give(me, "ETC_325");
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, medic2)));
        Assert.Equal(0, medic2[SabberStoneCore.Enums.GameTag.LIFESTEAL]);

        // Harmonic (live-20261007-vfx-decks): swaps at the end of its owner's turns only
        var game2 = TenManaGame();
        var p = game2.CurrentPlayer;
        var metal = Give(p, "ETC_427");
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(p)));
        Assert.Equal("ETC_427t", game2.IdEntityDic[metal.Id].Card.Id);
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(p.Opponent)));
        Assert.Equal("ETC_427t", game2.IdEntityDic[metal.Id].Card.Id);
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(p)));
        Assert.Equal("ETC_427", game2.IdEntityDic[metal.Id].Card.Id);

        // Remixed (wiki: "When drawn or added to the player's hand, this transforms into one of the related cards")
        var game3 = TenManaGame();
        var p3 = game3.CurrentPlayer;
        var musician = Give(p3, "JAM_033");
        string version = game3.IdEntityDic[musician.Id].Card.Id;
        Assert.StartsWith("JAM_033t", version);
        // ... and changes at the end of each of its owner's turns (user 2026-10-07, recorded: live-20261007-zilliax-etc-remix),
        // never into itself, not on the opponent's turn
        Assert.True(game3.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(p3)));
        string version2 = game3.IdEntityDic[musician.Id].Card.Id;
        Assert.NotEqual(version, version2);
        Assert.StartsWith("JAM_033t", version2);
        Assert.True(game3.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(p3.Opponent)));
        Assert.Equal(version2, game3.IdEntityDic[musician.Id].Card.Id);
        // the turn-end block (live): TRIGGER on the card, effect index 0; a minion pulses HAS_ACTIVATE_POWER, a spell doesn't
        var rhapsody = Give(p3, "JAM_018");
        Assert.StartsWith("JAM_018t", game3.IdEntityDic[rhapsody.Id].Card.Id);
        int mark = game3.PowerHistory.Full.Count;
        Assert.True(game3.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(p3)));
        var turnEnd = game3.PowerHistory.Full.Skip(mark).ToList();
        Assert.Contains(turnEnd, d => d is SabberStoneCore.Kettle.PowerHistoryBlockStart b && b.Source == musician.Id && b.Index == 0 && b.BlockType == BlockType.TRIGGER);
        Assert.Contains(turnEnd, d => d is SabberStoneCore.Kettle.PowerHistoryBlockStart b && b.Source == rhapsody.Id && b.Index == 0 && b.BlockType == BlockType.TRIGGER);
        Assert.Contains(turnEnd, d => d is SabberStoneCore.Kettle.PowerHistoryTagChange t && t.EntityId == musician.Id && t.Tag == (SabberStoneCore.Enums.GameTag)2840);
        Assert.DoesNotContain(turnEnd, d => d is SabberStoneCore.Kettle.PowerHistoryTagChange t && t.EntityId == rhapsody.Id && t.Tag == (SabberStoneCore.Enums.GameTag)2840);

        // Riffs (live): Verse Riff's Finale plays the last Riff (Bridge Riff) once — the Riff Stopper ends the chain
        var game4 = TenManaGame();
        var p4 = game4.CurrentPlayer;
        Assert.True(game4.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(p4, Give(p4, "ETC_365"))));
        Assert.Equal(2, p4.BoardZone.Count);
        p4.UsedMana = p4.BaseMana - 1; // exactly Verse Riff's 1
        Assert.True(game4.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(p4, Give(p4, "ETC_363"))));
        Assert.Equal(4, p4.BoardZone.Count);
        Assert.Equal(0, p4[SabberStoneCore.Enums.HsTag.RIFF_STOPPERS]);

        // "Take Fatigue damage" (Baritone Imp): the Fatigue counter goes up, the Imp gains it
        var game5 = TenManaGame();
        var p5 = game5.CurrentPlayer;
        var imp = (SabberStoneCore.Model.Entities.Minion)Give(p5, "ETC_068");
        Assert.True(game5.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(p5, imp)));
        Assert.Equal(1, p5.Hero.Fatigue);
        Assert.Equal(3, imp.AttackDamage);

        // Razorfen Rockstar: each adds 2 to a gain, not to the other's bonus (no loop)
        var game6 = TenManaGame();
        var p6 = game6.CurrentPlayer;
        OnBoard(p6, "ETC_355");
        OnBoard(p6, "ETC_355");
        int before = p6.Hero.Armor;
        p6.Hero.GainArmor(p6.Hero, 2);
        Assert.Equal(before + 6, p6.Hero.Armor);

        // Tradeable (wiki: "the card does not lose any enchantments when traded")
        var game7 = TenManaGame();
        var p7 = game7.CurrentPlayer;
        var viper = Give(p7, "CORE_SW_072");
        SabberStoneCore.Actions.Generic.AddEnchantmentBlock(game7, SabberStoneCore.Model.Cards.FromId("ETC_105e"), p7.Hero, viper, 0, 0, 0);
        Assert.True(SabberStoneCore.Actions.Generic.Trade(p7, viper));
        Assert.Equal(SabberStoneCore.Enums.Zone.DECK, viper.Zone.Type);
        Assert.Contains(viper.AppliedEnchantments, e => e.Card.Id == "ETC_105e");

        // Climactic Necrotic Explosion (user 2026-10-07: its numbers update in hand): each Corpse spent raises one of its
        // four numbers by 1 on every copy in hand and deck; the cast uses the card's numbers
        var game8 = TenManaGame();
        var p8 = game8.CurrentPlayer;
        var boom = Give(p8, "ETC_210");
        var deckBoom = SabberStoneCore.Model.Entities.Entity.FromCard(p8, SabberStoneCore.Model.Cards.FromId("ETC_210"), zone: p8.DeckZone);
        int Numbers(SabberStoneCore.Model.Entities.IPlayable card) => card[SabberStoneCore.Enums.GameTag.TAG_SCRIPT_DATA_NUM_1] +
            card[SabberStoneCore.Enums.GameTag.TAG_SCRIPT_DATA_NUM_2] + card[(SabberStoneCore.Enums.GameTag)2889] + card[(SabberStoneCore.Enums.GameTag)2919];
        Assert.Equal(13, Numbers(boom));
        p8.GainCorpses(5);
        Assert.True(SabberStoneCore.Actions.Generic.SpendCorpses(p8, p8.Hero, 5));
        Assert.Equal(18, Numbers(boom));
        Assert.Equal(18, Numbers(deckBoom));
        int damage = boom[SabberStoneCore.Enums.GameTag.TAG_SCRIPT_DATA_NUM_1], souls = boom[SabberStoneCore.Enums.GameTag.TAG_SCRIPT_DATA_NUM_2];
        int soulAtk = boom[(SabberStoneCore.Enums.GameTag)2889], soulHp = boom[(SabberStoneCore.Enums.GameTag)2919];
        int enemyHealth = p8.Opponent.Hero.Health;
        Assert.True(game8.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(p8, boom, p8.Opponent.Hero)));
        Assert.Equal(enemyHealth - damage, p8.Opponent.Hero.Health);
        Assert.Equal(souls, p8.BoardZone.Count);
        Assert.All(p8.BoardZone, m => { Assert.Equal(soulAtk, m.AttackDamage); Assert.Equal(soulHp, m.Health); });

        // Fight Over Me (user 2026-10-07): the first minion is the spell's target, the second a board target pick (TARGET
        // choice) among the other enemy minions; they fight; a copy of each that died goes to the hand
        var game9 = TenManaGame();
        var p9 = game9.CurrentPlayer;
        var yeti = OnBoard(p9.Opponent, "CS2_182"); // 4/5
        var croc = OnBoard(p9.Opponent, "CS2_120"); // 2/3
        var ogre = OnBoard(p9.Opponent, "CS2_200"); // 6/7
        OnBoard(p9, "CS2_120");
        var fight = Give(p9, "ETC_316");
        int handBefore = p9.HandZone.Count;
        Assert.True(game9.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(p9, fight, yeti)));
        Assert.NotNull(p9.Choice);
        Assert.Equal(ChoiceType.TARGET, p9.Choice.ChoiceType);
        Assert.Equal(new[] { croc.Id, ogre.Id }.OrderBy(i => i), p9.Choice.Choices.OrderBy(i => i));
        Assert.True(game9.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(p9, croc.Id)));
        Assert.Null(p9.Choice);
        Assert.Equal(2, yeti.Damage);
        Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, croc.Zone.Type);
        Assert.Equal(handBefore, p9.HandZone.Count); // the spell left, the copy came
        Assert.Contains(p9.HandZone, h => h.Card.Id == "CS2_120" && h.Id != croc.Id);

        // Instrument Smasher / Grave Shambler (user ruling 2026-10-07): "whenever your weapon is destroyed" = destroyed
        // naturally (durability, an Ooze effect); a weapon replaced by equipping another doesn't trigger them
        var game10 = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.DEMONHUNTER, Player2HeroClass = CardClass.MAGE, FormatType = FormatType.FT_WILD,
            FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false,
        });
        game10.StartGame();
        game10.Player1.BaseMana = 10;
        var p10 = game10.CurrentPlayer;
        var smasher = OnBoard(p10, "ETC_400");
        var shambler = (SabberStoneCore.Model.Entities.Minion)OnBoard(p10, "ICC_097");
        new SabberStoneCore.Tasks.SimpleTasks.WeaponTask("CS2_106").Process(game10, p10, p10.Hero, null);
        var firstWeapon = p10.Hero.Weapon;
        Assert.NotNull(firstWeapon);
        var axe = Give(p10, "CS2_106");
        Assert.True(game10.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(p10, axe)));
        Assert.Same(axe, p10.Hero.Weapon); // replaced: no Smasher weapon, no Shambler buff
        Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, firstWeapon.Zone.Type);
        Assert.Equal(4, shambler.AttackDamage);
        // ... destroyed naturally (an Ooze effect): both trigger
        axe.ToBeDestroyed = true;
        game10.DeathProcessingAndAuraUpdate();
        Assert.NotNull(p10.Hero.Weapon);
        Assert.Equal(smasher.Id, p10.Hero.Weapon[SabberStoneCore.Enums.GameTag.CREATOR]);
        Assert.True(p10.Hero.Weapon.Card.HasClass(CardClass.DEMONHUNTER));
        Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, axe.Zone.Type);
        Assert.Equal(5, shambler.AttackDamage);
        // "your weapon": the opponent's breaking doesn't trigger it (TriggerSource.WEAPON compared the weapon with itself)
        var smashed = p10.Hero.Weapon;
        new SabberStoneCore.Tasks.SimpleTasks.WeaponTask("CS2_106", true).Process(game10, p10, p10.Hero, null);
        Assert.NotNull(p10.Opponent.Hero.Weapon);
        p10.Opponent.Hero.Weapon.ToBeDestroyed = true;
        game10.DeathProcessingAndAuraUpdate();
        Assert.Null(p10.Opponent.Hero.Weapon);
        Assert.Same(smashed, p10.Hero.Weapon);

        // Kangor (user 2026-10-07): swaps with a random hand minion: Kangor to the hand, the minion in his place with Lifesteal
        var game11 = TenManaGame();
        var p11 = game11.CurrentPlayer;
        OnBoard(p11, "CS2_120");
        var kangor = OnBoard(p11, "ETC_329");
        OnBoard(p11, "CS2_182");
        var waiting = Give(p11, "CS2_200");
        foreach (var other in p11.HandZone.Where(h => h is SabberStoneCore.Model.Entities.Minion && h != waiting).ToList())
            p11.HandZone.Remove(other);
        kangor.Destroy();
        game11.DeathProcessingAndAuraUpdate();
        Assert.Equal(SabberStoneCore.Enums.Zone.PLAY, waiting.Zone.Type);
        Assert.Equal(1, waiting.ZonePosition);
        Assert.Equal(1, waiting[SabberStoneCore.Enums.GameTag.LIFESTEAL]);
        Assert.Contains(p11.HandZone, h => h.Card.Id == "ETC_329");

        // Rhythm and Roots (user 2026-10-07): ETC_387t sits in the Secret zone shown to both players; only the option is
        // secret: HIDDEN_CHOICE reaches the owner only, the opponent gets no SubOption; three Ancients after 2 of its turns
        var game12 = TenManaGame();
        var p12 = game12.CurrentPlayer;
        Assert.True(game12.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(p12, Give(p12, "ETC_387"), chooseOne: 1)));
        var rhythm = p12.SecretZone.Single(x => x.Card.Id == "ETC_387t");
        Assert.Equal(1, rhythm[SabberStoneCore.Enums.GameTag.HIDDEN_CHOICE]);
        Assert.Equal(0, rhythm[SabberStoneCore.Enums.GameTag.QUEST_PROGRESS_TOTAL]);
        var ownerView = new ClientStream(p12.PlayerId, _ => (0, 0, 0)).Convert(game12.PowerHistory.Full);
        var enemyView = new ClientStream(p12.Opponent.PlayerId, _ => (0, 0, 0)).Convert(game12.PowerHistory.Full);
        bool HasChoice(List<PowerHistoryData> view) => view.Any(d =>
            (d.FullEntity?.Entity == rhythm.Id ? d.FullEntity.Tags : d.ShowEntity?.Entity == rhythm.Id ? d.ShowEntity.Tags : null)?
            .Any(t => t.Name == (int)HsServer.Engine.GAME_TAG.HIDDEN_CHOICE && t.Value == 1) == true ||
            d.TagChange is { Tag: (int)HsServer.Engine.GAME_TAG.HIDDEN_CHOICE } tc && tc.Entity == rhythm.Id);
        Assert.True(HasChoice(ownerView));
        Assert.False(HasChoice(enemyView));
        Assert.Contains(enemyView, d => d.ShowEntity?.Entity == rhythm.Id && d.ShowEntity.Name == "ETC_387t");
        Assert.Contains(ownerView, d => d.PowerStart is { Type: HistoryBlock.Type.PLAY, SubOption: 0 });
        Assert.DoesNotContain(enemyView, d => d.PowerStart is { Type: HistoryBlock.Type.PLAY, SubOption: >= 0 });
        Assert.DoesNotContain(enemyView, d => d.ShowEntity?.Name is "ETC_387b" or "ETC_387c" || d.FullEntity?.Name is "ETC_387b" or "ETC_387c"
            || d.ChangeEntity?.Name is "ETC_387b" or "ETC_387c");
        // ... while a Secret stays a "?" for the opponent, even one put up straight from the deck (its deck FULL_ENTITY has no
        // SECRET tag: ClientStream reads the card)
        var deckSecret = (SabberStoneCore.Model.Entities.Spell)SabberStoneCore.Model.Entities.Entity.FromCard(p12,
            SabberStoneCore.Model.Cards.FromId("EX1_130"), zone: p12.DeckZone);
        Assert.True(SabberStoneCore.Actions.Generic.PutSecretIntoPlay(p12, deckSecret));
        var enemyView2 = new ClientStream(p12.Opponent.PlayerId, _ => (0, 0, 0)).Convert(game12.PowerHistory.Full);
        Assert.DoesNotContain(enemyView2, d => d.ShowEntity?.Entity == deckSecret.Id || d.FullEntity?.Entity == deckSecret.Id && d.FullEntity.Name.Length > 0);
        int boardBefore = p12.BoardZone.Count;
        for (int turn = 0; turn < 2; turn++)
        {
            Assert.Equal(0, p12.BoardZone.Count - boardBefore);
            Assert.True(game12.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(p12)));
            Assert.True(game12.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(p12.Opponent)));
        }
        Assert.Equal(3, p12.BoardZone.Count(m => m.Card.Id == "ETC_387bt"));
        Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, rhythm.Zone.Type);
        // its countdown block (live 012951 133777 / 136022: effect index 2, no keyword, ShowInHistory)
        Assert.Contains(game12.PowerHistory.Full, e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart
            { BlockType: SabberStoneCore.Enums.BlockType.TRIGGER, TriggerKeyword: 0, ShowInHistory: true, Index: 2 } b && b.Source == rhythm.Id);
    }

    /// <summary>TITANS (set 1858): the shared rules its integration added or fixed — the loader, Immune while attacking, "a copy
    /// of this" on an enchantment, Choose One "both" spent only by real Choose One cards, a copied Titan's ability numbers,
    /// copies placed right of their originals when the card says so, the granting card's draw-and-hold effect, and a Sigil in
    /// the Secret slots shown to both players.</summary>
    [Fact]
    public void Titans_shared_rules()
    {
        // ---- loader: a client card a script defines loads though it is named after no card (Cho'gall's Fortifications
        // buff TTN_02t30e1: ReprintCards.ScriptedClientCard)
        _ = Db.Value;
        var fortified = SabberStoneCore.Model.Cards.TryFromId("TTN_02t30e1");
        Assert.NotNull(fortified);
        Assert.Equal(CardType.ENCHANTMENT, fortified.Type);
        Assert.True(fortified.Implemented);

        // ---- Immune while attacking: the minion's own tag (Lady S'theno 1/4 into a War Golem 7/7) — no damage, live's lone
        // PREDAMAGE pulse; the mark ends with the Sequence
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var stheno = OnBoard(game, me, "TSC_218");
        var golem = OnBoard(game, me.Opponent, "CS2_186");
        int mark = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(me, stheno, golem)));
        Assert.Equal(SabberStoneCore.Enums.Zone.PLAY, stheno.Zone.Type);
        Assert.Equal(0, stheno.Damage);
        Assert.Equal(stheno.AttackDamage, golem.Damage);
        Assert.Equal(0, stheno[HsTag.IMMUNE_THIS_ATTACK]);
        Assert.DoesNotContain(game.PowerHistory.Full.Skip(mark), e => e is SabberStoneCore.Kettle.PowerHistoryMetaData
            { Type: SabberStoneCore.Enums.MetaDataType.DAMAGE } d && d.Info?.Contains(stheno.Id) == true);
        Assert.Contains(game.PowerHistory.Full.Skip(mark), e => e is SabberStoneCore.Kettle.PowerHistoryTagChange
            { Tag: GameTag.PREDAMAGE, Value: 7 } t && t.EntityId == stheno.Id);
        // ... and a hero whose weapon says "Your hero is Immune while attacking" (Aggramar's Swift Slash TTN_092e3)
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, Give(me, "CS2_106")))); // Fiery War Axe 3/2
        SabberStoneCore.Actions.Generic.AddEnchantmentBlock(game, SabberStoneCore.Model.Cards.FromId("TTN_092e3"), me.Hero, me.Hero.Weapon);
        Assert.True(SabberStoneCore.Actions.Generic.ImmuneWhileAttacking(me.Hero));
        var yeti = OnBoard(game, me.Opponent, "CS2_186"); // another War Golem 7/7
        int heroHealth = me.Hero.Health;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.HeroAttackTask.Any(me, yeti)));
        Assert.Equal(heroHealth, me.Hero.Health);
        Assert.Equal(5, yeti.Damage); // 3 + Swift Slash's 2
        Assert.Equal(0, me.Hero[HsTag.IMMUNE_THIS_ATTACK]);
        // without it, the counter-damage lands
        var plain = TenManaGame();
        var p0 = plain.CurrentPlayer;
        var raider = OnBoard(plain, p0, "CS2_182");
        var wall = OnBoard(plain, p0.Opponent, "CS2_182");
        Assert.True(plain.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(p0, raider, wall)));
        Assert.Equal(4, raider.Damage);

        // ---- "a copy of this" said by an enchantment is the minion it is on (a magnetized Lab Constructor's TTN_730e2 on its
        // Mech: it threw at the end of the turn)
        var game2 = TenManaGame();
        var me2 = game2.CurrentPlayer;
        var host = OnBoard(game2, me2, "GVG_085"); // Annoy-o-Tron
        SabberStoneCore.Actions.Generic.AddEnchantmentBlock(game2, SabberStoneCore.Model.Cards.FromId("TTN_730e2"), host, host);
        Assert.True(game2.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me2)));
        Assert.Equal(2, me2.BoardZone.Count(m => m.Card.Id == "GVG_085"));

        // ---- Disciple of Eonar names Choose One but has no options: a second one doesn't spend the first one's charge; a real
        // Choose One card does — and spends every Disciple's (the rule is a flag: each one's "next Choose One card" is that card)
        var game3 = TenManaGame();
        var me3 = game3.CurrentPlayer;
        Assert.True(game3.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me3, Give(me3, "TTN_503"))));
        Assert.True(game3.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me3, Give(me3, "TTN_503"))));
        Assert.Equal(1, me3[HsTag.NEXT_CHOOSE_ONE_BOTH]);
        int handBefore = me3.HandZone.Count;
        var gift = Give(me3, "TTN_955"); // Lifebinder's Gift: both — 2 Nature spells and the discount
        Assert.True(game3.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me3, gift, chooseOne: 1)));
        Assert.Equal(0, me3[HsTag.NEXT_CHOOSE_ONE_BOTH]);
        Assert.Equal(handBefore + 2, me3.HandZone.Count);
        Assert.DoesNotContain(me3.AppliedEnchantments ?? new List<SabberStoneCore.Model.Entities.Enchantment>(), e => e.Card.Id == "TTN_503e");

        // ---- a copied Titan keeps its abilities' state: used ones and their numbers (Norgannon's doubled powers)
        var game4 = TenManaGame();
        var me4 = game4.CurrentPlayer;
        var norgannon = (SabberStoneCore.Model.Entities.Minion)Give(me4, "TTN_075");
        Assert.True(game4.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me4, norgannon)));
        int knowledge = SabberStoneCore.Actions.Generic.TitanAbilityIndex(norgannon, SabberStoneCore.Model.Cards.FromId("TTN_075t2"));
        int power = SabberStoneCore.Actions.Generic.TitanAbilityIndex(norgannon, SabberStoneCore.Model.Cards.FromId("TTN_075t"));
        int basePower = SabberStoneCore.Model.Cards.FromId("TTN_075t")[GameTag.TAG_SCRIPT_DATA_NUM_1];
        Assert.True(game4.Process(me4.Options().OfType<SabberStoneCore.Tasks.PlayerTasks.TitanAbilityTask>().First(o => o.AbilityIndex == knowledge)));
        Assert.Equal(2 * basePower, SabberStoneCore.Actions.Generic.TitanAbility(norgannon, power)[GameTag.TAG_SCRIPT_DATA_NUM_1]);
        var copy = SabberStoneCore.Actions.Generic.Copy(me4, norgannon, norgannon, SabberStoneCore.Enums.Zone.PLAY);
        Assert.NotEqual(SabberStoneCore.Actions.Generic.TitanAbility(norgannon, power).Id, SabberStoneCore.Actions.Generic.TitanAbility(copy, power).Id);
        Assert.Equal(2 * basePower, SabberStoneCore.Actions.Generic.TitanAbility(copy, power)[GameTag.TAG_SCRIPT_DATA_NUM_1]);
        Assert.True(SabberStoneCore.Actions.Generic.TitanAbilityUsed(copy, knowledge));
        Assert.False(SabberStoneCore.Actions.Generic.TitanAbilityUsed(copy, power));
        // ... and Ancient Knowledge's "your cards cost more this turn" holder sits on the OPPONENT's player, controlled by the caster
        // (live JAM_034e: ATTACHED = the opponent player)
        Assert.Contains(me4.Opponent.AppliedEnchantments ?? new List<SabberStoneCore.Model.Entities.Enchantment>(), e => e.Card.Id == "TTN_075t2e" && e.Controller == me4);
        Assert.DoesNotContain(me4.AppliedEnchantments ?? new List<SabberStoneCore.Model.Entities.Enchantment>(), e => e.Card.Id == "TTN_075t2e");

        // ---- Freya's Guardians of Life: each copy right of its original (live vfx-decks 179049-179075; Generic.Copy position)
        var game5 = TenManaGame();
        var me5 = game5.CurrentPlayer;
        var freya = OnBoard(game5, me5, "TTN_940");
        var a = OnBoard(game5, me5, "CS2_168");
        var b = OnBoard(game5, me5, "CS2_182");
        SabberStoneCore.Model.Cards.FromId("TTN_940a").Power.PowerTask.Process(game5, me5, freya, null);
        Assert.Equal(new[] { "TTN_940", "CS2_168", "CS2_168", "CS2_182", "CS2_182" }, me5.BoardZone.Select(m => m.Card.Id));
        Assert.Equal(a.Id, me5.BoardZone[2][HsTag.COPIED_FROM_ENTITY_ID]);
        Assert.Equal(b.Id, me5.BoardZone[4][HsTag.COPIED_FROM_ENTITY_ID]);

        // ---- a drawn card cast by Runic Adornment's TTN_845e plays the granting card's draw-and-hold effect (live 265631)
        Assert.Contains("RunicAdornment_DrawAndHold", SabberStoneCore.Loader.SubSpells.FindNamed("TTN_845", "DrawAndHold") ?? "");
        var game6 = TenManaGame();
        var me6 = game6.CurrentPlayer;
        var adornment = Give(me6, "TTN_845");
        var drawnSpell = SabberStoneCore.Model.Entities.Entity.FromCard(me6, SabberStoneCore.Model.Cards.FromId("CS2_029"), zone: me6.DeckZone);
        SabberStoneCore.Actions.Generic.AddEnchantmentBlock(game6, SabberStoneCore.Model.Cards.FromId("TTN_845e"), adornment, drawnSpell);
        mark = game6.PowerHistory.Full.Count;
        SabberStoneCore.Actions.Generic.Draw(me6, drawnSpell);
        var holds = game6.PowerHistory.Full.Skip(mark).OfType<SabberStoneCore.Kettle.PowerHistorySubSpellStart>().Where(s => s.Prefab.Contains("DrawAndHold")).ToList();
        Assert.Single(holds);
        Assert.Contains("RunicAdornment", holds[0].Prefab);
        Assert.NotEqual(SabberStoneCore.Enums.Zone.HAND, drawnSpell.Zone?.Type ?? SabberStoneCore.Enums.Zone.INVALID); // cast when drawn
        // live 265684-265720: ARTIFICIAL_HISTORY_INTERRUPT on the copy, the tile ends naming the game, the spent TTN_845e goes to
        // the graveyard
        var tail6 = game6.PowerHistory.Full.Skip(mark).OfType<SabberStoneCore.Kettle.PowerHistoryMetaData>().ToList();
        Assert.Contains(tail6, m => m.Type == SabberStoneCore.Enums.MetaDataType.ARTIFICIAL_HISTORY_INTERRUPT && m.Info.Count == 1);
        Assert.DoesNotContain(tail6, m => m.Type == SabberStoneCore.Enums.MetaDataType.ARTIFICIAL_PAUSE);
        Assert.Contains(tail6, m => m.Type == SabberStoneCore.Enums.MetaDataType.END_ARTIFICIAL_HISTORY_TILE && m.Info.SequenceEqual(new[] { game6.Id }));
        Assert.DoesNotContain(drawnSpell.AppliedEnchantments ?? new List<SabberStoneCore.Model.Entities.Enchantment>(), e => e.Card.Id == "TTN_845e");

        // ---- recorded HandToDeck effects (SubSpells.Role.HandToDeck): Gear Shift's moves play inside live's effect, then its
        // draws inside the DeckBuff one (live-20261005/115158 1019)
        var game8 = TenManaGame();
        var me8 = game8.CurrentPlayer;
        Give(me8, "CS2_168");
        Give(me8, "CS2_182");
        mark = game8.PowerHistory.Full.Count;
        Assert.True(game8.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me8, Give(me8, "TTN_922"))));
        var gearFx = game8.PowerHistory.Full.Skip(mark).OfType<SabberStoneCore.Kettle.PowerHistorySubSpellStart>().Select(s => s.Prefab).ToList();
        Assert.Contains(gearFx, p => p.Contains("HandToDeck"));
        Assert.Contains(gearFx, p => p.Contains("DeckBuff"));
        // ... and an empty deck's top card is none (Algalon's Vision on an empty enemy deck threw)
        foreach (var card in me8.Opponent.DeckZone.ToList()) me8.Opponent.DeckZone.Remove(card);
        Assert.Null(me8.Opponent.DeckZone.TopCard);

        // ---- a Sigil sits in the Secret slots, shown to both players (user 2026-10-07: "the same slots as secrets and/or
        // auras ... like Wildpaw Cavern"), and acts at its owner's next turn: Sigil of Time draws 3 more
        var game7 = TenManaGame();
        var me7 = game7.CurrentPlayer;
        var sigil = Give(me7, "YOG_401");
        Assert.True(game7.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me7, sigil)));
        Assert.Equal(SabberStoneCore.Enums.Zone.SECRET, sigil.Zone.Type);
        var enemyView = new ClientStream(me7.Opponent.PlayerId, _ => (0, 0, 0)).Convert(game7.PowerHistory.Full);
        Assert.Contains(enemyView, d => d.ShowEntity?.Entity == sigil.Id && d.ShowEntity.Name == "YOG_401" ||
                                        d.FullEntity?.Entity == sigil.Id && d.FullEntity.Name == "YOG_401");
        int hand7 = me7.HandZone.Count;
        Assert.True(game7.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me7)));
        Assert.True(game7.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me7.Opponent)));
        Assert.Equal(System.Math.Min(10, hand7 + 4), me7.HandZone.Count); // 3 from the Sigil + the turn's draw
        Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, sigil.Zone.Type);
    }
    [Fact]
    public void Titans_review_fixes()
    {
        static SabberStoneCore.Model.Entities.Minion M(SabberStoneCore.Model.Entities.IPlayable p) => (SabberStoneCore.Model.Entities.Minion)p;

        // ---- an Attack Event trigger needs its minion in play (rule book: Cutpurse "implicitly requires the Cutpurse to still be in
        // play"): Kologarn sent back by Freezing Trap steals nothing
        var g1 = TenManaGame();
        var p1 = g1.CurrentPlayer;
        var kologarn = OnBoard(g1, p1, "TTN_330");
        var victim = OnBoard(g1, p1.Opponent, "CS2_182");
        Assert.True(SabberStoneCore.Actions.Generic.PutSecretIntoPlay(p1.Opponent, (SabberStoneCore.Model.Entities.Spell)Give(p1.Opponent, "EX1_611")));
        g1.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(p1, kologarn, victim)); // (cancelled in preparation)
        Assert.Equal(SabberStoneCore.Enums.Zone.HAND, kologarn.Zone.Type);
        Assert.Equal(SabberStoneCore.Enums.Zone.PLAY, victim.Zone.Type);
        Assert.Equal(p1.Opponent, victim.Controller);

        // ---- a stolen card going back to a full hand is lost to the graveyard (Full Zone Instant Removal: "If it was in your
        // Hand ... it goes to the Graveyard")
        var g2 = TenManaGame();
        var p2 = g2.CurrentPlayer;
        var stolen = Give(p2, "CS2_182");
        while (!p2.Opponent.HandZone.IsFull) Give(p2.Opponent, "CS2_168");
        Assert.False(SabberStoneCore.Actions.Generic.TakeIntoHand(p2.Opponent, stolen));
        Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, stolen.Zone.Type);

        // ---- "whenever THIS is attacked": any attacker, a friendly one forced to attack too (Saronite Tol'vir)
        var g3 = TenManaGame();
        var p3 = g3.CurrentPlayer;
        var tolvir = OnBoard(g3, p3, "TTN_711");
        var forced = OnBoard(g3, p3, "CS2_182");
        int hand3 = p3.HandZone.Count;
        SabberStoneCore.Actions.Generic.AttackBlock.Invoke(p3, forced, tolvir, true, false);
        Assert.Equal(hand3 + 1, p3.HandZone.Count);

        // ---- Cleave is After Attack: the defender's neighbours take the attacker's Attack once the attack is done
        var g4 = TenManaGame();
        var p4 = g4.CurrentPlayer;
        var thresher = OnBoard(g4, p4, "CORE_SCH_605");
        var left = OnBoard(g4, p4.Opponent, "CS2_186");
        var middle = OnBoard(g4, p4.Opponent, "CS2_186");
        var right = OnBoard(g4, p4.Opponent, "CS2_186");
        int mark4 = g4.PowerHistory.Full.Count;
        Assert.True(g4.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(p4, thresher, middle)));
        Assert.Equal(thresher.AttackDamage, left.Damage);
        Assert.Equal(thresher.AttackDamage, right.Damage);
        var blocks4 = g4.PowerHistory.Full.Skip(mark4).ToList();
        int attackEnd = blocks4.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockEnd);
        int cleave = blocks4.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart { BlockType: BlockType.TRIGGER } b && b.Source == thresher.Id);
        Assert.True(cleave > attackEnd);

        // ---- Razorscale's floor shows its own enchantment on the cards it raises
        var g5 = TenManaGame();
        var p5 = g5.CurrentPlayer;
        var coin = Give(p5, "GAME_005");
        var yeti = Give(p5, "CS2_182");
        OnBoard(g5, p5, "TTN_924");
        g5.AuraUpdate();
        Assert.Equal(2, coin.Cost);
        Assert.Contains(coin.AppliedEnchantments ?? new List<SabberStoneCore.Model.Entities.Enchantment>(), e => e.Card.Id == "TTN_924e");
        Assert.DoesNotContain(yeti.AppliedEnchantments ?? new List<SabberStoneCore.Model.Entities.Enchantment>(), e => e.Card.Id == "TTN_924e");

        // ---- Strike from History: a Stealthed enemy can't be chosen, so with only that one the ability can't be used; with two
        // enemy minions both picks come before Aman'Thul's Discover (live 42717-42760)
        var g6 = TenManaGame();
        var p6 = g6.CurrentPlayer;
        var amanthul = M(Give(p6, "TTN_429"));
        Assert.True(g6.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(p6, amanthul)));
        int strike = SabberStoneCore.Actions.Generic.TitanAbilityIndex(amanthul, SabberStoneCore.Model.Cards.FromId("TTN_429t2"));
        var lurker = OnBoard(g6, p6.Opponent, "EX1_010");
        Assert.True(lurker.HasStealth);
        Assert.NotEqual(0, (int)SabberStoneCore.Actions.Generic.TitanAbilityError(amanthul, strike, out _));
        lurker.HasStealth = false;
        OnBoard(g6, p6.Opponent, "CS2_182");
        Assert.Equal(0, (int)SabberStoneCore.Actions.Generic.TitanAbilityError(amanthul, strike, out _));
        Assert.True(g6.Process(p6.Options().OfType<SabberStoneCore.Tasks.PlayerTasks.TitanAbilityTask>().First(o => o.AbilityIndex == strike)));
        Assert.Equal(ChoiceType.TARGET, p6.Choice.ChoiceType);
        Assert.Equal(0, amanthul[SabberStoneCore.Actions.Generic.TITAN_ABILITY_USED_1 + strike]); // the action is spent after the picks
        Assert.True(g6.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(p6, p6.Choice.Choices[0])));
        Assert.Equal(ChoiceType.TARGET, p6.Choice.ChoiceType);
        Assert.True(g6.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(p6, p6.Choice.Choices[0])));
        Assert.Equal(ChoiceType.GENERAL, p6.Choice.ChoiceType); // the Discover, last
        Assert.Equal(1, amanthul[SabberStoneCore.Actions.Generic.TITAN_ABILITY_USED_1 + strike]);
        Assert.Equal(0, p6.Opponent.BoardZone.Count);

        // ---- an untouchable minion takes no damage, and the enemy list leaves it out (Sargeras' portal, Tempest Hammer)
        var g7 = TenManaGame();
        var p7 = g7.CurrentPlayer;
        var portal = OnBoard(g7, p7.Opponent, "TTN_960t");
        Assert.True(portal.Untouchable);
        SabberStoneCore.Actions.Generic.DamageCharFunc(p7.Hero, portal, 3, false);
        Assert.Equal(0, portal.Damage);
        Assert.Equal(SabberStoneCore.Enums.Zone.PLAY, portal.Zone.Type);

        // ---- Amitus: his cap's TRIGGER block (index 0, ShowInHistory) comes on every hit, a small one too, and lowers only a hit above 2
        // (live titans-vfx 190857: 41 empty blocks for hits of 1-2, 13 lowering)
        var g8 = TenManaGame();
        var p8 = g8.CurrentPlayer;
        var amitus = OnBoard(g8, p8, "TTN_858");
        var guarded = OnBoard(g8, p8, "CS2_186");
        int mark8 = g8.PowerHistory.Full.Count;
        SabberStoneCore.Actions.Generic.DamageCharFunc(p8.Opponent.Hero, guarded, 1, false);
        Assert.Contains(g8.PowerHistory.Full.Skip(mark8), e => e is SabberStoneCore.Kettle.PowerHistoryBlockStart b && b.Source == amitus.Id &&
            b.BlockType == SabberStoneCore.Enums.BlockType.TRIGGER && b.Index == 0 && b.ShowInHistory);
        Assert.Equal(1, guarded.Damage);
        guarded.Damage = 0;
        SabberStoneCore.Actions.Generic.DamageCharFunc(p8.Opponent.Hero, guarded, 5, false);
        Assert.Equal(2, guarded.Damage); // the 5 capped at 2

        // ---- Astral Automaton: one transformed into was never counted, so it counts every summoned one
        var g9 = TenManaGame();
        var p9 = g9.CurrentPlayer;
        OnBoard(g9, p9, "TTN_401");
        OnBoard(g9, p9, "TTN_401");
        var shaped = OnBoard(g9, p9, "CS2_168");
        var automaton = SabberStoneCore.Actions.Generic.ChangeEntityBlock(p9, shaped, SabberStoneCore.Model.Cards.FromId("TTN_401"), true);
        g9.AuraUpdate();
        Assert.Equal(SabberStoneCore.Model.Cards.FromId("TTN_401").ATK + 2, ((SabberStoneCore.Model.Entities.Minion)automaton).AttackDamage);

        // ---- Armor lost by any means counts for "your Armor changed this turn"
        var g10 = TenManaGame();
        var p10 = g10.CurrentPlayer;
        p10.Hero.Armor = 5;
        p10[HsTag.ARMOR_GAINED_TURN] = 0;
        p10.Hero.Armor = 2;
        Assert.True(SabberStoneCore.Actions.Generic.ArmorChangedThisTurn(p10));
        Assert.Equal(3, p10[HsTag.ARMOR_LOST_THIS_TURN]);

        // ---- a marked "if you play it this turn" card spends its mark when played (live: NEW_1308e removed at once)
        var g11 = TenManaGame();
        var p11 = g11.CurrentPlayer;
        var marked = Give(p11, "CS2_182");
        SabberStoneCore.Actions.Generic.MarkPlayThisTurn(marked, p11.Hero);
        Assert.True(g11.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(p11, marked)));
        Assert.Equal(0, marked[HsTag.PLAY_THIS_TURN_MARK]);
        Assert.Equal(0, marked[GameTag.TAG_ONE_TURN_EFFECT]);
        Assert.DoesNotContain(marked.AppliedEnchantments ?? new List<SabberStoneCore.Model.Entities.Enchantment>(), e => e.Card.Id == "NEW_1308e");

        // ---- a spell burned by a full hand isn't "the first spell you drew this turn" (rule book DC4)
        var g12 = TenManaGame();
        var p12 = g12.CurrentPlayer;
        while (!p12.HandZone.IsFull) Give(p12, "CS2_168");
        p12[HsTag.SPELL_DRAWN_TURN] = 0;
        var burned = SabberStoneCore.Model.Entities.Entity.FromCard(p12, SabberStoneCore.Model.Cards.FromId("CS2_029"), zone: p12.DeckZone);
        SabberStoneCore.Actions.Generic.Draw(p12, burned);
        Assert.NotEqual(g12.Turn, p12[HsTag.SPELL_DRAWN_TURN]);

        // ---- a Choose One card whose options are scripted is implemented (random casts find Lifebinder's Gift)
        Assert.True(SabberStoneCore.Model.Cards.FromId("TTN_955").Implemented);

        // ---- "from the past": a set out of Standard now (TITANS has rotated out), not a set of the newest Standard year (Cataclysm)
        Assert.True(SabberStoneCore.Model.Cards.FromThePast(SabberStoneCore.Model.Cards.FromId("TTN_481")));
        Assert.False(SabberStoneCore.Model.Cards.FromThePast(SabberStoneCore.Model.Cards.FromId("CATA_134")));

        // ---- a doubled Celestial Shot leaves one "next spell +2" (wiki patch 28.0.0.189017)
        var g13 = TenManaGame();
        var p13 = g13.CurrentPlayer;
        p13[HsTag.SPELLS_CAST_TWICE] = 1;
        Assert.True(g13.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(p13, Give(p13, "YOG_082"), p13.Opponent.Hero)));
        Assert.Single((p13.AppliedEnchantments ?? new List<SabberStoneCore.Model.Entities.Enchantment>()).Where(e => e.Card.Id == "YOG_082e"));

        // ---- Legion Invasion! empowers its Sargeras' own portal only
        var g14 = TenManaGame();
        var p14 = g14.CurrentPlayer;
        var other = OnBoard(g14, p14, "TTN_960t");
        var sargeras = M(Give(p14, "TTN_960"));
        Assert.True(g14.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(p14, sargeras)));
        int legion = SabberStoneCore.Actions.Generic.TitanAbilityIndex(sargeras, SabberStoneCore.Model.Cards.FromId("TTN_960t4"));
        Assert.True(g14.Process(p14.Options().OfType<SabberStoneCore.Tasks.PlayerTasks.TitanAbilityTask>().First(o => o.AbilityIndex == legion)));
        var own = p14.BoardZone.GetSpan().ToArray().First(m => m.Id == sargeras[HsTag.OPENED_PERMANENT]); // (a permanent: untouchable)
        Assert.Equal(1, own[HsTag.PORTAL_DEMONS_EMPOWERED]);
        Assert.Equal(0, other[HsTag.PORTAL_DEMONS_EMPOWERED]);

        // ---- a Titan's forced attack keeps its action (wiki Titan: "A forced attack does not ... consume its action")
        var g15 = TenManaGame();
        var p15 = g15.CurrentPlayer;
        OnBoard(g15, p15.Opponent, "CS2_186");
        var khaz = M(Give(p15, "TTN_415"));
        Assert.True(g15.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(p15, khaz)));
        Assert.True(g15.Process(p15.Options().OfType<SabberStoneCore.Tasks.PlayerTasks.TitanAbilityTask>().First()));
        Assert.Equal(1, khaz.NumAttacksThisTurn);
    }

    [Fact]
    public void Generation_bans_from_the_past_and_cast_twice_follow_the_rulings()
    {
        _ = Db.Value;
        // sideboard cards only exist through deck building (user 2026-10-07; wiki Generate: King of the Underbelly)
        foreach (string id in new[] { "TOY_330", "JAIL_831", "ETC_080", "JAIL_397" })
            Assert.DoesNotContain(SabberStoneCore.Model.Cards.AllWild, x => x.Id == id);
        // "from the past": a Wild card with a reprint in Standard now isn't (user 2026-10-07)
        var rag = SabberStoneCore.Model.Cards.FromId("EX1_298");
        bool reprintInStandard = SabberStoneCore.Loader.ReprintCards.InStandardNow((int)SabberStoneCore.Model.Cards.FromId("CORE_EX1_298").Set);
        Assert.Equal(!reprintInStandard && !SabberStoneCore.Loader.ReprintCards.InStandardNow((int)rag.Set), SabberStoneCore.Model.Cards.FromThePast(rag));

        // "your spells cast twice" doubles a played spell, not one an effect casts (wiki Yogg-Saron, Hope's End)
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        me[SabberStoneCore.Enums.HsTag.SPELLS_CAST_TWICE] = 1;
        int hand = me.HandZone.Count;
        SabberStoneCore.Actions.Generic.PlayByEffect(game, me, me.Hero, SabberStoneCore.Model.Cards.FromId("CS2_023")); // Arcane Intellect
        Assert.Equal(hand + 2, me.HandZone.Count);
        var intellect = Give(me, "CS2_023");
        hand = me.HandZone.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, intellect)));
        Assert.Equal(hand - 1 + 4, me.HandZone.Count);
        Assert.Equal(1, me.NumSpellsPlayedThisGame); // the repeat and the effect's cast don't count
    }

    [Fact]
    public void Badlands_shared_rules_steal_bottles_mirage_and_quickdraw()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var them = me.Opponent;
        SabberStoneCore.Model.Entities.IPlayable Card(string id) { me.UsedMana = 0; return Give(me, id); }

        // Shadow Word: Steal: the enemy minion comes to YOUR hand, yours now
        var yeti = OnBoard(game, them, "CS2_182");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Card("DEEP_021"), yeti)));
        Assert.Equal(SabberStoneCore.Enums.Zone.HAND, yeti.Zone.Type);
        Assert.Same(me, yeti.Controller);

        // Holy Springwater: 8 into a hero missing 3 → a Bottle holding the 5 overhealed
        me.Hero.Damage = 3;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Card("WW_395"), me.Hero)));
        var bottle = me.HandZone.Last();
        Assert.Equal("WW_395t", bottle.Card.Id);
        Assert.Equal(5, bottle[GameTag.TAG_SCRIPT_DATA_NUM_1]);
        Assert.Equal(1, bottle[GameTag.USE_ALTERNATE_CARD_TEXT]);

        // Invasive Shadeleaf: 10 into a 5-Health minion → 5 bottled
        var target = OnBoard(game, them, "CS2_182");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Card("WW_393"), target)));
        Assert.Equal("WW_393t", me.HandZone.Last().Card.Id);
        Assert.Equal(5, me.HandZone.Last()[GameTag.TAG_SCRIPT_DATA_NUM_1]);

        // Trolley Problem drawn this turn = Quickdraw: no discard, two Tram Cars
        int board = me.BoardZone.Count;
        var fuel = Card("WW_441");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Card("WW_436"))));
        Assert.Equal(SabberStoneCore.Enums.Zone.HAND, fuel.Zone.Type);
        Assert.Equal(board + 2, me.BoardZone.Count);

        // Furnace Fuel discarded: draw 2
        int hand = me.HandZone.Count;
        SabberStoneCore.Actions.Generic.DiscardBlock(me, fuel);
        Assert.Equal(hand - 1 + 2, me.HandZone.Count);

        // Fracking: pick one of the bottom 3, the other two are destroyed
        int deck = me.DeckZone.Count;
        var bottom = me.DeckZone.GetAll().Take(3).ToList();
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Card("WW_092"))));
        Assert.Equal(3, me.Choice.Choices.Count);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, me.Choice.Choices[0])));
        Assert.Equal(deck - 3, me.DeckZone.Count);
        Assert.Equal(2, bottom.Count(p => p.Zone?.Type == SabberStoneCore.Enums.Zone.GRAVEYARD));

        // Deputization Aura: the left-most minion has +2 Attack and Lifesteal; it moves on when that one dies
        foreach (var m in me.BoardZone.GetAll().ToList()) Kill(game, m);
        foreach (var p in me.HandZone.GetAll().ToList()) me.HandZone.Remove(p); // room for the Aura
        var left = OnBoard(game, me, "CS2_182");
        var right = OnBoard(game, me, "CS2_182");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Card("WW_341"))));
        Assert.Equal(6, left.AttackDamage);
        Assert.True(left.HasLifeSteal);
        Assert.Equal(4, right.AttackDamage);
        Kill(game, left);
        Assert.Equal(6, right.AttackDamage);
        Assert.True(right.HasLifeSteal);
    }

    [Fact]
    public void Paradise_shared_rules_reopen_choose_thrice_cheap_double_cast_excess_and_this_turn()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var them = me.Opponent;
        SabberStoneCore.Model.Entities.IPlayable Card(string id) { me.UsedMana = 0; foreach (var p in me.HandZone.GetAll().ToList()) me.HandZone.Remove(p); return Give(me, id); }

        // Horizon's Edge: used, then a friendly minion dies -> reopened (cooldown and exhaustion cleared)
        var edge = (SabberStoneCore.Model.Entities.Location)Card("VAC_425");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, edge, null, 0)));
        Assert.True(game.Process(me.Options().OfType<SabberStoneCore.Tasks.PlayerTasks.LocationTask>().First(u => u.Source.Id == edge.Id)));
        Assert.True(edge.OnCooldown && edge.IsExhausted);
        Kill(game, OnBoard(game, me, "CS2_231"));
        Assert.False(edge.OnCooldown);
        Assert.False(edge.IsExhausted);

        // Sleep Under the Stars: three picks, the same option allowed (wiki Choose multiple)
        int armor = me.Hero.Armor;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Card("VAC_907"))));
        for (int i = 0; i < 3; i++)
        {
            Assert.NotNull(me.Choice);
            var bear = me.Choice.Choices.First(id => game.IdEntityDic[id].Card.Id == "VAC_907t2");
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, bear)));
        }
        Assert.Equal(armor + 15, me.Hero.Armor);

        // Sunsapper Lynessa: a 2-Cost spell casts twice (one play counted), a 4-Cost one once
        OnBoard(game, me, "VAC_507");
        int health = them.Hero.Health, played = me.NumSpellsPlayedThisGame;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Card("CS2_029"), them.Hero))); // Fireball (4)
        Assert.Equal(health - 6, them.Hero.Health);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Card("CS2_024"), them.Hero))); // Frostbolt (2)
        Assert.Equal(health - 6 - 6, them.Hero.Health);
        Assert.Equal(played + 2, me.NumSpellsPlayedThisGame);

        // Piercing Shot into a Divine Shield minion with 3 Health left: the excess (3) still hits the hero (Blizzard forum t/70422)
        var shielded = OnBoard(game, them, "CS2_182");
        shielded.Damage = 2;
        shielded.HasDivineShield = true;
        health = them.Hero.Health;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Card("BAR_032"), shielded)));
        Assert.Equal(health - 3, them.Hero.Health);

        // "If your hero took damage this turn": this turn only (damage on the opponent's turn doesn't carry over)
        me.Hero.TakeDamage(them.Hero, 1);
        Assert.True(SabberStoneCore.Conditions.SelfCondition.IsMyHeroDamagedThisTurn.Eval(me.Hero));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        me.Hero.TakeDamage(them.Hero, 1);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(them)));
        Assert.False(SabberStoneCore.Conditions.SelfCondition.IsMyHeroDamagedThisTurn.Eval(me.Hero));
    }

    [Fact]
    public void Space_chunk3_shared_rules_zero_cost_librams_health_set_launch_upgrade_templar_merge_and_combo_twice()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var them = me.Opponent;
        SabberStoneCore.Model.Entities.IPlayable Card(string id) { me.UsedMana = 0; foreach (var p in me.HandZone.GetAll().ToList()) me.HandZone.Remove(p); return Give(me, id); }

        // Libram of Clarity played for 0 (three Interstellar discounts): the drawn minions get +2/+1; it glowed in hand
        for (int i = 0; i < 3; i++) Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Card("GDB_721"))));
        foreach (var m in me.BoardZone.GetAll().ToList()) { SabberStoneCore.Actions.Generic.Destroy(m, null); game.DeathProcessingAndAuraUpdate(); }
        foreach (var p in me.DeckZone.GetAll().ToList()) me.DeckZone.Remove(p);
        for (int i = 0; i < 2; i++) me.DeckZone.Add(SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("CS2_182")));
        var clarity = Card("GDB_137");
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(0, clarity.Cost); // 3 played Wayfarers -3, their 3 Deathrattles -3 more (floor 0)
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, clarity)));
        Assert.Equal(2, me.HandZone.Count);
        Assert.All(me.HandZone.GetAll(), m => Assert.Equal(4 + 2, ((SabberStoneCore.Model.Entities.Minion)m).AttackDamage));

        // a Health-setting buff sets current Health too (rule book: Hunter's Mark on a damaged minion leaves it alive)
        var yeti = OnBoard(game, them, "CS2_182");
        yeti.Damage = 3;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Card("CS2_084"), yeti)));
        Assert.Equal(1, yeti.Health);
        Assert.False(yeti.IsDead);

        // Gravity Lapse: Sunfury 2/3 -> 2/2 (undamaged), a Yeti 4/5 damaged to 4/3 -> 3/3
        var wall = OnBoard(game, me, "CS2_231"); // Wisp 1/1 (equal: untouched)
        var tall = OnBoard(game, them, "CS2_182");
        tall.Damage = 2;
        var golem = OnBoard(game, me, "EX1_058"); // Sunfury Protector 2/3
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Card("GDB_464"))));
        Assert.Equal(2, golem.AttackDamage); Assert.Equal(2, golem.Health);
        Assert.Equal(3, tall.Health); Assert.Equal(3, tall.AttackDamage);
        Assert.Equal(1, wall.AttackDamage); Assert.Equal(1, wall.Health);

        // a Starship launch upgrades Hellion in hand (and one drawn later)
        foreach (var m in me.BoardZone.GetAll().ToList()) Kill(game, m);
        var turret = OnBoard(game, me, "GDB_105"); // a Starship Piece; its death (in a real play) builds the Starship
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Card("CS2_029"), turret)));
        var ship = SabberStoneCore.Actions.Generic.BuildingStarship(me);
        Assert.True(ship != null, string.Join(",", me.BoardZone.GetAll().Select(m => $"{m.Card.Id} lp{m[(GameTag)3563]} tbd{m.ToBeDestroyed}")) + " | grave " +
            string.Join(",", me.GraveyardZone.GetAll().Select(m => m.Card.Id)) + " | piece " + SabberStoneCore.Actions.Generic.IsStarshipPiece(turret) + " zone " + turret.Zone?.Type +
            " | ship " + SabberStoneCore.Actions.Generic.StarshipCardFor(me.HeroClass)?.Id + " | made " +
            string.Join(",", game.IdEntityDic.Values.Where(e => e.Card.Id.StartsWith("GDB_100t") || e.Card.Id == "GDB_905").Select(e => $"{e.Card.Id} {e.Zone?.Type} h{e[GameTag.HEALTH]} d{e[GameTag.DAMAGE]}")));
        var hellion = Card("SC_412");
        me.UsedMana = 0;
        Assert.True(SabberStoneCore.Actions.Generic.LaunchStarship(me, ship));
        Assert.Equal("SC_412t", me.HandZone.GetAll().Single().Card.Id);
        Give(me, "SC_412");
        game.DeathProcessingAndAuraUpdate();
        Assert.All(me.HandZone.GetAll(), p => Assert.Equal("SC_412t", p.Card.Id));

        // Templars merge into an Archon; neither dies (no graveyard)
        foreach (var m in me.BoardZone.GetAll().ToList()) Kill(game, m);
        int graveyard = me.GraveyardZone.Count;
        OnBoard(game, me, "SC_752");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Card("SC_765"))));
        Assert.Equal("SC_671t1", me.BoardZone.GetAll().Single().Card.Id);
        Assert.Equal(graveyard, me.GraveyardZone.Count);

        // Celestial Aura: the only minion is 10/10 keeping its damage (wiki: 4/2 damaged -> 10/8); a second minion ends it
        foreach (var m in me.BoardZone.GetAll().ToList()) Kill(game, m);
        var only = OnBoard(game, me, "CS2_182");
        only.Damage = 2;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Card("GDB_140"))));
        Assert.Equal(10, only.AttackDamage); Assert.Equal(8, only.Health);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Card("CS2_231"))));
        // (back to max 5; rule book H2 "Lowering maximum Health only lowers current Health if current exceeds the new maximum": 8 -> 5)
        Assert.Equal(4, only.AttackDamage); Assert.Equal(5, only.Health);

        // Lucky Comet: the next Combo minion (SI:7 Agent, Combo active) deals its 3 damage twice; the charge is spent
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Card("GDB_873"))));
        if (me.Choice != null) Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, me.Choice.Choices[0])));
        Assert.Equal(1, me[SabberStoneCore.Enums.HsTag.NEXT_COMBO_TWICE]);
        int health = them.Hero.Health;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.MinionTarget(me, Card("CORE_EX1_134"), them.Hero)));
        Assert.Equal(health - 6, them.Hero.Health);
        Assert.Equal(0, me[SabberStoneCore.Enums.HsTag.NEXT_COMBO_TWICE]);
    }

    [Fact]
    public void Space_aura_enchantments_on_minions_summoned_inside_a_pick_are_revealed()
    {
        // soak 842017: Hellion's SC_412e1 on a Murloc Grunty summoned before its target pick had no FULL_ENTITY
        for (int run = 0; run < 8; run++)
        {
            var game = TenManaGame();
            var me = game.CurrentPlayer;
            var them = me.Opponent;
            OnBoard(game, me, "SC_412");
            OnBoard(game, me, "EX1_509"); // Murloc Tidecaller: +1 Attack per Murloc summoned
            for (int i = 0; i < 3; i++) OnBoard(game, them, "CS2_182");
            me.UsedMana = 0;
            var grunty = Give(me, "SC_013");
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, grunty)));
            for (int i = 0; i < 6 && me.Choice != null; i++)
            {
                // the AI tries the pick on a copy of the game first (the choice's task is shared with the copy)
                var copy = game.Clone();
                var copyMe = copy.ControllerById(me.Id);
                copy.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(copyMe, copyMe.Choice.Choices[^1]));
                Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, me.Choice.Choices[0])));
            }
            var revealed = game.PowerHistory.Full.OfType<SabberStoneCore.Kettle.PowerHistoryFullEntity>().Select(f => f.Entity.Id).ToHashSet();
            foreach (var e in game.IdEntityDic.Values.OfType<SabberStoneCore.Model.Entities.Enchantment>())
                if (e.Target is SabberStoneCore.Model.Entities.Minion m && m.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY)
                    Assert.True(revealed.Contains(e.Id), $"run {run}: {e.Card.Id} #{e.Id} on {m.Card.Id} never sent");
        }
    }

    [Fact]
    public void Space_chunk4_extra_attacks_lens_location_target_steal_spores_swap_and_spellburst_order()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var them = me.Opponent;
        SabberStoneCore.Model.Entities.IPlayable Card(string id) { me.UsedMana = 0; foreach (var p in me.HandZone.GetAll().ToList()) me.HandZone.Remove(p); return Give(me, id); }
        void Clear() { foreach (var m in me.BoardZone.GetSpan().ToArray().Concat(them.BoardZone.GetSpan().ToArray()).ToList()) Kill(game, m); }

        // Exarch Akama: after it attacks, a friendly minion that already attacked may attack again (not another Akama)
        var akama = OnBoard(game, me, "GDB_235");
        var yeti = OnBoard(game, me, "CS2_182");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(me, yeti, them.Hero)));
        Assert.True(yeti.IsExhausted);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(me, akama, them.Hero)));
        Assert.False(yeti.IsExhausted);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(me, yeti, them.Hero)));
        Assert.True(yeti.IsExhausted);
        Assert.True(akama.IsExhausted);
        Clear();

        // Consume: only a friendly Location is a target; it loses 1 Durability and the hero gets 8
        var cavern = (SabberStoneCore.Model.Entities.Location)OnBoard(game, me, "SC_019");
        var consume = Card("SC_020");
        Assert.Equal(new[] { cavern.Id }, ((SabberStoneCore.Model.Entities.Playable)consume).ValidPlayTargets.Select(x => x.Id).ToArray());
        me.Hero.Damage = 10;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, consume, cavern)));
        Assert.Equal(1, cavern.Damage);
        Assert.Equal(28, me.Hero.Health);
        Clear();

        // Galaxy's Lens: unusable until a spell; then it casts a new copy of that spell at the chosen target
        Kill(game, OnBoard(game, me, "GDB_447"));
        var lens = me.BoardZone.GetSpan().ToArray().OfType<SabberStoneCore.Model.Entities.Location>().Single();
        Assert.False(lens.CanActivate);
        var target = OnBoard(game, them, "CS2_182");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Card("CS2_029"), them.Hero))); // Fireball
        Assert.True(lens.CanActivate);
        Assert.Contains(target.Id, lens.ActivationTargets().Select(x => x.Id));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.LocationTask.Any(me, lens, target)));
        Assert.True(target.IsDead || target.Zone?.Type != SabberStoneCore.Enums.Zone.PLAY);
        Clear();

        // K'ara: a Shadow spell keeps the Spellburst, another spell spends it; each steal moves 2 Health
        var kara = (SabberStoneCore.Model.Entities.Minion)OnBoard(game, me, "GDB_127");
        them.Hero.Damage = 0;
        int health = them.Hero.Health;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Card("DS1_233")))); // Mind Blast (Shadow)
        Assert.True(kara[SabberStoneCore.Enums.HsTag.SPELLBURST] > 0);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Card("CS2_029"), them.Hero)));
        Assert.Equal(0, kara[SabberStoneCore.Enums.HsTag.SPELLBURST]);
        Assert.Equal(3 + 2 + 2, kara.Health);
        Assert.Equal(health - 5 - 6 - 2 - 2, them.Hero.Health); // Mind Blast, Fireball, two steals (max Health)
        Clear();

        // Replicating Spore: the second one summons the first one's minion again plus a new one
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Card("GDB_234t"))));
        Assert.Equal(1, me.BoardZone.Count);
        string first = me.BoardZone[0].Card.Id;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Card("GDB_234t"))));
        Assert.Equal(3, me.BoardZone.Count);
        Assert.True(me.BoardZone.GetAll().Count(m => m.Card.Id == first) >= 2);
        Clear();

        // Stalwart Avenger: swaps at the end of each turn; the swap enchantment shows the NEW Attack / Health (live)
        var avenger = OnBoard(game, me, "GDB_230");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.Equal(2, avenger.AttackDamage); Assert.Equal(7, avenger.Health);
        var swap = avenger.AppliedEnchantments.Last(e => e.Card.Id == "GDB_230e");
        Assert.Equal(2, swap[GameTag.TAG_SCRIPT_DATA_NUM_1]); Assert.Equal(7, swap[GameTag.TAG_SCRIPT_DATA_NUM_2]);
    }

    [Fact]
    public void Spellburst_sets_its_tag_to_0_after_its_effect_like_live()
    {
        // live (questline GDB_310 6831, vfx-decks GDB_104 86444): the effect, then SPELLBURST = 0 as the block's last change
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var burst = OnBoard(game, me, "GDB_434"); // Bolide Behemoth: Spellburst: shuffle 3 Asteroids
        me.UsedMana = 0;
        int mark = game.PowerHistory.Full.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Give(me, "CS2_029"), me.Opponent.Hero)));
        var after = game.PowerHistory.Full.Skip(mark).ToList();
        int silenced = after.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryFullEntity f && game.IdEntityDic.TryGetValue(f.Entity.Id, out var made) && made.Card.Id == "GDB_430");
        int spent = after.FindIndex(e => e is SabberStoneCore.Kettle.PowerHistoryTagChange t && t.EntityId == burst.Id && (int)t.Tag == (int)SabberStoneCore.Enums.HsTag.SPELLBURST && t.Value == 0);
        Assert.True(silenced >= 0 && spent > silenced, $"effect {silenced} spent {spent}");
    }

    [Fact]
    public void Playable_hero_cards_are_in_the_pools_and_their_powers_work()
    {
        // Hero cards are cards (never exclude cards): playable ones (Cost > 0) are in the random pools, portraits aren't
        _ = Db.Value;
        var wild = SabberStoneCore.Model.Cards.FormatTypeCollectible(SabberStoneCore.Enums.FormatType.FT_WILD).ToList();
        Assert.Contains(wild, c => c.Id == "SC_400");
        Assert.DoesNotContain(wild, c => c.Id == "HERO_01");

        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var them = me.Opponent;
        SabberStoneCore.Model.Entities.IPlayable Card(string id) { me.UsedMana = 0; foreach (var p in me.HandZone.GetAll().ToList()) me.HandZone.Remove(p); return Give(me, id); }

        // Wildheart Guff's Nurture: a Choose One hero power whose options are named after the hero card
        var options = SabberStoneCore.Model.Cards.ChooseOneOptions(SabberStoneCore.Model.Cards.FromId("AV_205p"));
        Assert.NotNull(options);
        Assert.Equal(new[] { "AV_205a", "AV_205pb" }, options.Select(o => o.Id).OrderBy(x => x).ToArray());

        // Lightforged Cariel: The Immovable Object halves the hero's damage (rounded up) and keeps its Durability
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, Card("AV_206"))));
        Assert.Equal("AV_146", me.Hero.Weapon?.Card.Id);
        int health = me.Hero.Health, armor = me.Hero.Armor;
        SabberStoneCore.Actions.Generic.DamageCharFunc(them.Hero, me.Hero, 5 + armor, false);
        Assert.True(health + armor - me.Hero.Health - me.Hero.Armor == (5 + armor + 1) / 2, $"took {health + armor - me.Hero.Health - me.Hero.Armor}");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.HeroAttackTask.Any(me, them.Hero)));
        Assert.Equal(5, me.Hero.Weapon.Durability);

        // Al'Akir's SWAT, INSECTS!: the hero gets Divine Shield, +2 Attack and Windfury this turn (a hero can have both)
        var fresh = TenManaGame();
        var p1 = fresh.CurrentPlayer;
        p1.UsedMana = 0;
        Assert.True(fresh.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(p1, Give(p1, "WON_092h"))));
        p1.UsedMana = 0;
        Assert.True(fresh.Process(SabberStoneCore.Tasks.PlayerTasks.HeroPowerTask.Any(p1)));
        Assert.Equal(1, p1.Hero[GameTag.DIVINE_SHIELD]);
        Assert.Equal(2, p1.Hero.AttackDamage);
        int before = p1.Hero.Health;
        SabberStoneCore.Actions.Generic.DamageCharFunc(p1.Opponent.Hero, p1.Hero, 3, false);
        Assert.Equal(before, p1.Hero.Health);
        Assert.Equal(0, p1.Hero[GameTag.DIVINE_SHIELD]);
        Assert.True(fresh.Process(SabberStoneCore.Tasks.PlayerTasks.HeroAttackTask.Any(p1, p1.Opponent.Hero)));
        Assert.True(fresh.Process(SabberStoneCore.Tasks.PlayerTasks.HeroAttackTask.Any(p1, p1.Opponent.Hero)));

        // Xyrella's hero power flips at the end of its owner's turn
        var third = TenManaGame();
        var p3 = third.CurrentPlayer;
        p3.UsedMana = 0;
        Assert.True(third.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(p3, Give(p3, "AV_207"))));
        Assert.Equal("AV_207p", p3.Hero.HeroPower.Card.Id);
        Assert.True(third.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(p3)));
        Assert.Equal("AV_207p2", p3.Hero.HeroPower.Card.Id);
    }

    [Fact]
    public void Emerald_chunk3_toreth_cast_three_spells_tyrande_harbinger_choose_one_weapon_and_portals()
    {
        _ = Db.Value;
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.PALADIN, Player2HeroClass = CardClass.MAGE,
            FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false,
        });
        game.StartGame();
        game.Player1.BaseMana = 10;
        game.Player2.BaseMana = 10;
        var me = game.CurrentPlayer;
        var them = me.Opponent;
        SabberStoneCore.Model.Entities.IPlayable Hand(string id) { me.UsedMana = 0; return Give(me, id); }
        void Clear() { foreach (var m in me.BoardZone.GetSpan().ToArray().Concat(them.BoardZone.GetSpan().ToArray()).ToList()) Kill(game, m); }
        bool Moonfire(SabberStoneCore.Model.Entities.ICharacter at) => game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Hand("CS2_008"), at));

        // Toreth: friendly Divine Shields show 3 and take three hits; the third breaks it
        OnBoard(game, me, "EDR_258");
        var squire = OnBoard(game, me, "EX1_008");
        game.DeathProcessingAndAuraUpdate();
        Assert.True(Moonfire(squire));
        Assert.Equal(3, squire[GameTag.DIVINE_SHIELD]);
        Assert.True(squire.HasDivineShield);
        Assert.Equal(1, squire.Health);
        Assert.True(Moonfire(squire));
        Assert.True(squire.HasDivineShield);
        Assert.True(Moonfire(squire));
        Assert.False(squire.HasDivineShield);
        Assert.Equal(1, squire.Health);
        Clear();
        Assert.Equal(1, OnBoard(game, me, "EX1_008")[GameTag.DIVINE_SHIELD]); // no Toreth: a plain shield
        Clear();

        // Wish of the New Moon: three spells cast while it is held count it down, then it becomes Wish of the Full Moon
        var wish = Give(me, "EDR_460");
        Assert.Equal(3, wish[GameTag.TAG_SCRIPT_DATA_NUM_1]);
        for (int i = 0; i < 3; i++) Assert.True(Moonfire(them.Hero));
        Assert.Equal("EDR_460t", wish.Card.Id);
        foreach (var p in me.HandZone.GetAll().ToList()) me.HandZone.Remove(p);

        // Tyrande: the next 3 spells PLAYED cast twice (Moonfire hits for 2), then the player enchantment leaves
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Hand("EDR_464"))));
        Assert.NotNull(me.AppliedEnchantments?.FirstOrDefault(e => e.Card.Id == "EDR_464e2"));
        for (int i = 0; i < 3; i++)
        {
            int before = them.Hero.Health;
            Assert.True(Moonfire(them.Hero));
            Assert.Equal(before - 2, them.Hero.Health);
        }
        Assert.Null(me.AppliedEnchantments?.FirstOrDefault(e => e.Card.Id == "EDR_464e2"));
        int last = them.Hero.Health;
        Assert.True(Moonfire(them.Hero));
        Assert.Equal(last - 1, them.Hero.Health);
        Clear();

        // Web of Deception + Harbinger of the Blighted: the Spider takes its place; Harbinger back in hand summons two 2-Costs
        var harbinger = OnBoard(game, me, "EDR_781");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Hand("EDR_523"), harbinger)));
        Assert.Equal(SabberStoneCore.Enums.Zone.HAND, harbinger.Zone.Type);
        Assert.Equal("EDR_523t", me.BoardZone[0].Card.Id);
        Assert.Equal(3, me.BoardZone.Count);
        Assert.Equal(2, me.BoardZone.GetAll().Count(m => m.Card.Id != "EDR_523t" && m.Card.Cost == 2));
        Clear();

        // Barbed Thorn: a Choose One weapon — option 2 gives it the Deathrattle (2 damage to all enemies when it breaks)
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, Hand("EDR_525"), (SabberStoneCore.Model.Entities.ICharacter)null, -1, 2)));
        Assert.Contains(me.Hero.Weapon.AppliedEnchantments, e => e.Card.Id == "EDR_525e");
        int enemy = them.Hero.Health;
        SabberStoneCore.Actions.Generic.Destroy(me.Hero.Weapon, null);
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(enemy - 2, them.Hero.Health);

        // Goldpetal Drake: Imbue → Blessing of the Dragon; its Portals carry the count and follow later Imbues
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Hand("EDR_451"))));
        Assert.Equal("EDR_445p", me.Hero.HeroPower.Card.Id);
        Assert.Equal(1, me.Hero.HeroPower[GameTag.TAG_SCRIPT_DATA_NUM_1]);
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.HeroPowerTask.Any(me)));
        var portals = me.DeckZone.GetAll().Where(p => p.Card.Id == "EDR_445pt3").ToList();
        Assert.Equal(2, portals.Count);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Hand("EDR_451"))));
        Assert.All(portals, p => Assert.Equal(2, p[GameTag.TAG_SCRIPT_DATA_NUM_1]));
        Clear();
        var portal = portals[0];
        SabberStoneCore.Actions.Generic.Draw(me, portal);
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(4, portal[GameTag.TAG_SCRIPT_DATA_NUM_1]); // (the two Drakes' Deathrattles Imbued twice more)
        Assert.Contains(me.BoardZone.GetAll(), m => m.Card.IsRace(Race.DRAGON) && m.Card.Cost == 4);
    }

    [Fact]
    public void Emerald_chunk4_agamaggan_plucky_podling_ohnahra_keeper_of_flame_and_wallow()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var them = me.Opponent;
        SabberStoneCore.Model.Entities.IPlayable Hand(string id) { me.UsedMana = 0; return Give(me, id); }
        void Clear() { foreach (var m in me.BoardZone.GetSpan().ToArray().Concat(them.BoardZone.GetSpan().ToArray()).ToList()) Kill(game, m); }
        void EmptyHand() { foreach (var p in me.HandZone.GetAll().ToList()) me.HandZone.Remove(p); }

        // Agamaggan: the next card costs the opponent's Health, not Mana (lost Health, not damage); then the rule ends
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Hand("EDR_489"))));
        Assert.Equal(10, me[SabberStoneCore.Enums.HsTag.NEXT_CARD_COSTS_OPPONENT_HEALTH]);
        var fireball = Give(me, "CS2_029");
        int mana = me.RemainingMana, health = them.Hero.Health;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, fireball, them.Hero)));
        Assert.Equal(mana, me.RemainingMana);
        Assert.Equal(health - 4 - 6, them.Hero.Health);
        Assert.Equal(0, me[SabberStoneCore.Enums.HsTag.NEXT_CARD_COSTS_OPPONENT_HEALTH]);
        Assert.Null(me.AppliedEnchantments?.FirstOrDefault(e => e.Card.Id == "EDR_489e1"));
        Clear(); EmptyHand();

        // Plucky Podling: Polymorph (a 1-Cost Sheep) makes it a random 3-Cost minion instead
        var podling = OnBoard(game, me, "EDR_529");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Hand("CS2_022"), podling)));
        Assert.Equal(3, me.BoardZone[0].Card.Cost);
        Assert.NotEqual("CS2_tk1", me.BoardZone[0].Card.Id);
        Clear(); EmptyHand();

        // Ohn'ahra: at the end of the turn the top 3 deck cards are played — minions come straight from the deck (same entities)
        OnBoard(game, me, "EDR_031");
        var tops = new List<SabberStoneCore.Model.Entities.IPlayable>();
        for (int i = 0; i < 3; i++)
        {
            var yeti = SabberStoneCore.Model.Entities.Entity.FromCard(me, SabberStoneCore.Model.Cards.FromId("CS2_182"));
            me.DeckZone.Add(yeti);
            tops.Add(yeti);
        }
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.All(tops, y => Assert.Equal(SabberStoneCore.Enums.Zone.PLAY, y.Zone.Type));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(them)));
        Clear(); EmptyHand();

        // Keeper of Flame: hand minions +3/+3, marked; the count starts at the end of this turn
        var held = Give(me, "CS2_182");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Hand("FIR_928"))));
        Assert.Equal(7, ((SabberStoneCore.Model.Entities.Minion)held).AttackDamage);
        Assert.Contains(held.AppliedEnchantments, e => e.Card.Id == "FIR_928e3");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.Equal(1, me.AppliedEnchantments.First(e => e.Card.Id == "FIR_928e2")[GameTag.TAG_SCRIPT_DATA_NUM_1]);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(them)));
        Clear(); EmptyHand();

        // Wallow: a Dark Gift given to a picked minion is copied onto Wallow in hand
        var wallow = Give(me, "EDR_487");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Hand("EDR_488"))));
        var pick = me.Choice.Choices[0];
        string gift = game.IdEntityDic[pick].AppliedEnchantments.Select(e => e.Card.Id).First(id => id.StartsWith("EDR_100t"));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, pick)));
        Assert.Contains(wallow.AppliedEnchantments, e => e.Card.Id == gift);
        Assert.Equal(1, wallow[(GameTag)3880]);
    }

    [Fact]
    public void Lost_city_kindred_dual_types_maps_platysaur_and_the_corpse_quest()
    {
        _ = Db.Value;
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.DEMONHUNTER, Player2HeroClass = CardClass.MAGE, FormatType = FormatType.FT_WILD,
            FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false,
        });
        game.StartGame();
        game.Player1.BaseMana = 10;
        game.Player2.BaseMana = 10;
        var me = game.CurrentPlayer;
        var them = me.Opponent;
        SabberStoneCore.Model.Entities.IPlayable Hand(string id) { me.UsedMana = 0; return Give(me, id); }

        // Kindred, dual type: a Demon played last turn wakes Diabolus Rex (Beast + Demon); it glows meanwhile
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Hand("CORE_EX1_319"))));
        var rex = Give(me, "DINO_138");
        Assert.False(SabberStoneCore.Actions.Generic.KindredGlow(rex).powered);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.True(SabberStoneCore.Actions.Generic.KindredGlow(rex).powered);
        Assert.False(SabberStoneCore.Actions.Generic.KindredGlow(rex).now);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(them)));
        Assert.True(SabberStoneCore.Actions.Generic.KindredGlow(rex).now);
        var yeti = OnBoard(game, them, "CS2_182");
        me.UsedMana = 0;
        Assert.True(SabberStoneCore.Actions.Generic.Kindred(me, rex), "kindred before play");
        Assert.NotNull(rex.Card.Power?.PowerTask);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, rex)));
        Assert.True(yeti.IsDead || yeti.Zone?.Type != SabberStoneCore.Enums.Zone.PLAY);

        // Hive Map: the pick is marked; played this turn, the two others are offered again
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Hand("TLC_900"))));
        var first = me.Choice.Choices[0];
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, first)));
        var picked = game.IdEntityDic[first];
        Assert.Equal(SabberStoneCore.Enums.Zone.HAND, picked.Zone.Type);
        Assert.Equal(first, me[SabberStoneCore.Enums.HsTag.MAP_PICK]);
        me.UsedMana = 0;
        var task = picked.Card.RequiresTarget || picked.Card.MustHaveTargetToPlay
            ? SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, picked, them.Hero)
            : SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, picked);
        if (game.Process(task))
        {
            Assert.NotNull(me.Choice);
            Assert.Equal(2, me.Choice.Choices.Count);
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, me.Choice.Choices[0])));
            Assert.Null(me.AppliedEnchantments?.FirstOrDefault(e => e.Card.Id == "TLC_900e"));
        }

        // Platysaur: its drawn card is discarded by its Deathrattle while still in hand
        foreach (var p in me.HandZone.GetAll().ToList()) me.HandZone.Remove(p);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Hand("TLC_603"))));
        var drawn = me.HandZone.GetAll().Single();
        Assert.Equal(1, drawn[(GameTag)401]);
        Kill(game, me.BoardZone.GetAll().First(m => m.Card.Id == "TLC_603"));
        Assert.Equal(SabberStoneCore.Enums.Zone.GRAVEYARD, drawn.Zone.Type);

        // Reanimate the Terror: spending Corpses advances the Quest; 15 complete it
        foreach (var p in me.HandZone.GetAll().ToList()) me.HandZone.Remove(p);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Hand("TLC_433"))));
        var quest = me.SecretZone.Quest;
        Assert.NotNull(quest);
        SabberStoneCore.Actions.Generic.CorpsesSpent(me, 5);
        Assert.Equal(5, quest.QuestProgress);
        SabberStoneCore.Actions.Generic.CorpsesSpent(me, 10);
        game.DeathProcessingAndAuraUpdate();
        Assert.Contains(me.HandZone.GetAll(), p => p.Card.Id == "TLC_433t");
    }

    [Fact]
    public void Lost_city_shuffle_instances_rift_wilted_shadow_bralma_and_sulfuras()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var them = me.Opponent;
        SabberStoneCore.Model.Entities.IPlayable Hand(string id) { me.UsedMana = 0; return Give(me, id); }
        void Clear() { foreach (var m in me.BoardZone.GetSpan().ToArray().Concat(them.BoardZone.GetSpan().ToArray()).ToList()) Kill(game, m); }

        // one effect shuffling three cards is ONE instance (client 4307); Knockback grows by it
        int before = me[SabberStoneCore.Actions.Generic.SHUFFLED_INTO_OWN_DECK];
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Hand("TLC_518"))));
        Assert.Equal(before + 1, me[SabberStoneCore.Actions.Generic.SHUFFLED_INTO_OWN_DECK]);
        var knockback = Give(me, "TLC_517");
        var yeti = OnBoard(game, them, "CS2_182");
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, knockback, yeti)));
        Assert.Equal(5 - (1 + before + 1), yeti.Health);
        Clear();

        // the Underfel Rift: a hand card thrown in leaves the game, two Fel Beasts come; one use a turn
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Hand("TLC_446t"))));
        var rift = me.BoardZone.GetSpan().ToArray().Single(m => m.Card.Id == "TLC_446t1");
        var fodder = Give(me, "CS2_182");
        var uses = me.Options().OfType<SabberStoneCore.Tasks.PlayerTasks.InteractTask>().ToList();
        Assert.Contains(uses, u => u.TargetEntity.Id == fodder.Id);
        Assert.True(game.Process(uses.First(u => u.TargetEntity.Id == fodder.Id)));
        Assert.Equal(SabberStoneCore.Enums.Zone.SETASIDE, fodder.Zone.Type);
        Assert.Equal(2, me.BoardZone.GetAll().Count(m => m.Card.Id.StartsWith("TLC_446t") && m.Card.Id != "TLC_446t1"));
        Assert.Empty(me.Options().OfType<SabberStoneCore.Tasks.PlayerTasks.InteractTask>());
        Clear();

        // Wilted Shadow: healing an enemy makes it attack first
        var shadow = OnBoard(game, me, "TLC_821");
        var hurt = OnBoard(game, them, "CS2_182");
        hurt.Damage = 2;
        hurt.TakeHeal(me.Hero, 1);
        game.DeathProcessingAndAuraUpdate();
        Assert.True(hurt.IsDead || hurt.Zone?.Type != SabberStoneCore.Enums.Zone.PLAY);
        Clear();

        // Bralma Searstone: a friendly Elemental's damage +1 (Fire Elemental's 4 → 5)
        OnBoard(game, me, "TLC_228");
        int heroHealth = them.Hero.Health;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.MinionTarget(me, Hand("CS2_042"), them.Hero)));
        Assert.Equal(heroHealth - 5, them.Hero.Health);
        Clear();

        // Story of Sulfuras: two uses (the second the same turn), then the old Hero Power is back
        string original = me.Hero.HeroPower.Card.Id;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Hand("TLC_632"))));
        Assert.Equal("TLC_632t", me.Hero.HeroPower.Card.Id);
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.HeroPowerTask.Any(me)));
        Assert.Equal("TLC_632t2", me.Hero.HeroPower.Card.Id);
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.HeroPowerTask.Any(me)));
        Assert.Equal(original, me.Hero.HeroPower.Card.Id);
    }

    [Fact]
    public void Time_travel_chunk3_copy_discovers_eternus_atiesh_hand_of_infinity_and_hooktail()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var them = me.Opponent;
        SabberStoneCore.Model.Entities.IPlayable Hand(string id) { me.UsedMana = 0; return Give(me, id); }
        void Clear() { foreach (var m in me.BoardZone.GetSpan().ToArray().Concat(them.BoardZone.GetSpan().ToArray()).ToList()) Kill(game, m); }

        // Deja Vu: a copy of a card in the opponent's hand — its enchantments kept (hand to hand), the original stays
        foreach (var p in them.HandZone.GetAll().ToList()) SabberStoneCore.Actions.Generic.RemoveFromHand(them, p);
        var theirs = Give(them, "CS2_182");
        SabberStoneCore.Actions.Generic.AddEnchantmentBlock(game, SabberStoneCore.Model.Cards.FromId("GBL_002e"), theirs, theirs, 0, 0, 0);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Hand("TIME_039"))));
        int option = me.Choice.Choices.First(id => game.IdEntityDic[id].Card.Id == "CS2_182");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, option)));
        var copy = game.IdEntityDic[option];
        Assert.Equal(SabberStoneCore.Enums.Zone.HAND, copy.Zone.Type);
        Assert.Equal(me, copy.Controller);
        Assert.Equal(theirs.Cost, copy.Cost);
        Assert.Equal(SabberStoneCore.Enums.Zone.HAND, theirs.Zone.Type);

        // Eternus: only an enemy minion with its Health or less (client PlayReq 159)
        var small = OnBoard(game, them, "CS2_182");   // 4/5
        var big = OnBoard(game, them, "CS2_186");     // 7/7
        var eternus = Hand("TIME_435");               // 6/2
        Assert.False(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.MinionTarget(me, eternus, small)));
        var tiny = OnBoard(game, them, "CS2_231");    // 1/1 Wisp
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.MinionTarget(me, eternus, tiny)));
        Assert.Equal(me, tiny.Controller);
        Clear();

        // Atiesh doubles your spells' healing, not the Hero Power's; Clockwork Automaton doubles the Hero Power's
        var atiesh = Hand("TIME_890t");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, atiesh)));
        me.Hero.Damage = 20;
        me.Hero.TakeHeal((SabberStoneCore.Model.Entities.IPlayable)Give(me, "CS1_130"), 2);
        Assert.Equal(16, me.Hero.Damage);
        me.Hero.TakeHeal(me.Hero.HeroPower, 2);
        Assert.Equal(14, me.Hero.Damage);
        OnBoard(game, me, "GIL_646");
        me.Hero.TakeHeal(me.Hero.HeroPower, 2);
        Assert.Equal(10, me.Hero.Damage);
        me.Hero.Damage = 0;
        Clear();

        // Hand of Infinity: INFINITY this turn, back to 4 next turn; the hero can't hit the enemy hero with it
        var hand = Hand("END_012");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(me, hand)));
        Assert.True(me.Hero.Weapon.AttackDamage >= 1_000_000);
        Assert.True(me.Hero.AttackDamage >= 1_000_000);
        var victim = OnBoard(game, them, "CS2_186");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.HeroAttackTask.Any(me, victim)));
        Assert.True(victim.IsDead || victim.Zone?.Type != SabberStoneCore.Enums.Zone.PLAY);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.Equal(4, me.Hero.Weapon?.AttackDamage ?? 4);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(them)));

        // Time Adm'ral Hooktail: the opponent gets the Chest; when it dies, Hooktail's owner gets Coins
        Clear();
        foreach (var p in me.HandZone.GetAll().ToList()) SabberStoneCore.Actions.Generic.RemoveFromHand(me, p);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Hand("TIME_713"))));
        var chest = them.BoardZone.GetSpan().ToArray().Single(m => m.Card.Id == "TIME_713t");
        Kill(game, chest);
        Assert.Equal(10, me.HandZone.Count);
        Assert.All(me.HandZone.GetAll(), p => Assert.Equal("GAME_005", p.Card.Id));
    }

    [Fact]
    public void Time_travel_chunk4_nature_instead_divergence_chronogor_muradin_and_acolyte()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var them = me.Opponent;
        SabberStoneCore.Model.Entities.IPlayable Hand(string id) { me.UsedMana = 0; return Give(me, id); }
        void Clear() { foreach (var m in me.BoardZone.GetSpan().ToArray().Concat(them.BoardZone.GetSpan().ToArray()).ToList()) Kill(game, m); }

        // Flux Revenant: your Nature spell's damage becomes +2/+1; Stormrook: a 5-Cost minion instead
        var flux = OnBoard(game, me, "TIME_214");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Hand("TIME_218"), flux)));
        Assert.Equal(0, flux.Damage);
        Assert.Equal(3, flux.AttackDamage);
        Assert.Equal(5, flux.Health);
        var rook = OnBoard(game, me, "TIME_217");
        int board = me.BoardZone.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Hand("TIME_218"), rook)));
        Assert.Equal(0, rook.Damage);
        Assert.Equal(board + 1, me.BoardZone.Count);
        Clear();

        // Divergence: a minion in hand becomes two halves, splitting its Cost, Attack and Health (M.O.T.H.E.R.: 10 Mana since the 2026-11-20 patch)
        foreach (var p in me.HandZone.GetAll().ToList()) SabberStoneCore.Actions.Generic.RemoveFromHand(me, p);
        var big = Give(me, "BE_036");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Hand("TIME_030"))));
        var halves = me.HandZone.GetAll().Where(p => p.Card.Id == "BE_036").Cast<SabberStoneCore.Model.Entities.Minion>().ToList();
        Assert.Equal(2, halves.Count);
        Assert.Equal(SabberStoneCore.Enums.Zone.SETASIDE, big.Zone.Type);
        Assert.Equal(big.Card.Cost, halves.Sum(h => h.Cost));
        Assert.Equal(big.Card.ATK, halves.Sum(h => h.AttackDamage));
        Assert.Equal(big.Card.Health, halves.Sum(h => h.Health));

        // Chronogor: the opponent draws your lowest-Cost cards (theirs now)
        foreach (var p in me.HandZone.GetAll().ToList()) SabberStoneCore.Actions.Generic.RemoveFromHand(me, p);
        foreach (var p in them.HandZone.GetAll().ToList()) SabberStoneCore.Actions.Generic.RemoveFromHand(them, p);
        int lowest = me.DeckZone.Min(p => p.Cost);
        int deck = me.DeckZone.Count;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Hand("TIME_032"))));
        Assert.Equal(deck - 4, me.DeckZone.Count);
        Assert.Equal(2, them.HandZone.Count);
        Assert.All(them.HandZone.GetAll(), p => Assert.Equal(them, p.Controller));
        Assert.Contains(them.HandZone.GetAll(), p => p.Cost == lowest);
        Clear();

        // Muradin takes the High King's Hammer from the hand: its Attack and Durability, Windfury; his Deathrattle gives it back
        var hammer = Give(me, "TIME_209t");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Hand("TIME_209"))));
        var muradin = me.BoardZone.GetSpan().ToArray().Single(m => m.Card.Id == "TIME_209");
        Assert.Equal(SabberStoneCore.Enums.Zone.SETASIDE, hammer.Zone.Type);
        Assert.Equal(3 + 3, muradin.AttackDamage);
        Assert.Equal(2 + 4, muradin.Health);
        Assert.True(muradin.HasWindfury);
        Kill(game, muradin);
        Assert.Equal(SabberStoneCore.Enums.Zone.HAND, hammer.Zone.Type);

        // Acolyte of Infinity: a hand card costs INFINITY until the Acolyte dies
        foreach (var p in me.HandZone.GetAll().ToList()) SabberStoneCore.Actions.Generic.RemoveFromHand(me, p);
        var only = Give(me, "CS2_182");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Hand("END_018"))));
        Assert.True(only.Cost >= 1_000_000);
        Kill(game, me.BoardZone.GetSpan().ToArray().Single(m => m.Card.Id == "END_018"));
        Assert.Equal(4, only.Cost);
    }

    [Fact]
    public void Cataclysm_herald_colossal_limbs_shatter_leylines_and_companions()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var them = me.Opponent;
        SabberStoneCore.Model.Entities.IPlayable Hand(string id) { me.UsedMana = 0; return Give(me, id); }
        void Clear() { foreach (var m in me.BoardZone.GetSpan().ToArray().Concat(them.BoardZone.GetSpan().ToArray()).ToList()) Kill(game, m); }
        void EmptyHand() { foreach (var p in me.HandZone.GetAll().ToList()) SabberStoneCore.Actions.Generic.RemoveFromHand(me, p); }
        var amount = SabberStoneCore.Actions.Generic.HERALD_COLOSSAL_AMOUNT;

        // Herald (a Druid playing a Demon Hunter card: the card's class — ClayByte's rule): Soldier of Azshara, "When summoned" +1 hero
        // Attack, the count +1; the third Soldier comes upgraded (x2)
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Hand("CATA_525"))));
        Assert.Equal(1, me[amount]);
        var soldier = me.BoardZone.GetSpan().ToArray().Single(m => m.Card.Id == "CATA_525t");
        Assert.Equal(1, me.Hero.AttackDamage);
        Assert.Equal(2, soldier.AttackDamage);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Hand("CATA_525"))));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Hand("CATA_525"))));
        Assert.Equal(3, me[amount]);
        var third = me.BoardZone.GetSpan().ToArray().Where(m => m.Card.Id == "CATA_525t").Last();
        Assert.Equal(4, third.AttackDamage);
        Assert.Equal(2, third[GameTag.TAG_SCRIPT_DATA_NUM_1]);
        Clear();

        // Azshara's Tentacles: t1 right, t left, upgraded x2 by the Heralds; the hero has Windfury
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Hand("CATA_151"))));
        var board = me.BoardZone.GetSpan().ToArray();
        int body = Array.FindIndex(board, m => m.Card.Id == "CATA_151");
        Assert.Equal("CATA_151t", board[body - 1].Card.Id);
        Assert.Equal("CATA_151t1", board[body + 1].Card.Id);
        Assert.Equal(4, board[body + 1].AttackDamage);
        Assert.True(me.Hero[GameTag.WINDFURY] > 0);
        Clear();

        // Shatter: Wildwood Circle splits (left half left-most, right half right-most); played between, the halves meet and merge
        EmptyHand();
        var filler = Give(me, "CS2_231");
        Give(me, "CATA_134");
        game.DeathProcessingAndAuraUpdate();
        var hand = me.HandZone.GetAll().ToList();
        Assert.Equal(3, hand.Count);
        Assert.Equal("CATA_134t", hand[0].Card.Id);
        Assert.Equal("CATA_134t2", hand[2].Card.Id);
        me.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, filler)));
        hand = me.HandZone.GetAll().ToList();
        Assert.Single(hand);
        Assert.Equal("CATA_134", hand[0].Card.Id);
        Assert.Equal(0, hand[0][SabberStoneCore.Actions.Generic.SHATTER]);
        Clear();

        // Leylines: Surge Needle — Bursting Leyline fires twice (its numbers on the card in hand)
        EmptyHand();
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Hand("MEND_503"))));
        var leyline = Give(me, "MEND_500");
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(2, leyline[GameTag.TAG_SCRIPT_DATA_NUM_2]);
        Clear();

        // Animal Companions: Tame Pet replaces them with 4-Cost Beasts; Animal Companion summons one of them
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Hand("MEND_300"))));
        var companions = SabberStoneCore.Actions.Generic.AnimalCompanions(me);
        Assert.Equal(3, companions.Count);
        Assert.All(companions, x => Assert.Equal(4, x.Cost));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Hand("NEW1_031"))));
        Assert.Contains(me.BoardZone.GetSpan().ToArray(), m => companions.Contains(m.Card));
    }
    [Fact]
    public void Violet_hold_misdirection_full_reborn_counterfeit_coins_transform_cannoneers_godfrey_warptooth_slice()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var them = me.Opponent;
        SabberStoneCore.Model.Entities.IPlayable Hand(string id) { me.UsedMana = 0; return Give(me, id); }
        void Clear() { foreach (var m in me.BoardZone.GetSpan().ToArray().Concat(them.BoardZone.GetSpan().ToArray()).ToList()) Kill(game, m); }
        void EmptyHand() { foreach (var p in me.HandZone.GetAll().ToList()) SabberStoneCore.Actions.Generic.RemoveFromHand(me, p); }
        SabberStoneCore.Model.Entities.Minion Put(SabberStoneCore.Model.Entities.Controller c, string id)
        {
            var m = (SabberStoneCore.Model.Entities.Minion)SabberStoneCore.Model.Entities.Entity.FromCard(c, SabberStoneCore.Model.Cards.FromId(id));
            SabberStoneCore.Actions.Generic.SummonBlock.Invoke(game, m, -1, null);
            return m;
        }

        // Sinful Steed: Reborn with full Health and enchantments (Haunt's +2/+3 kept; the copy made as it died)
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Hand("CAP_800"))));
        var steed = me.BoardZone.GetSpan().ToArray().Single(m => m.Card.Id == "CAP_800");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.SpellTarget(me, Hand("CAP_801"), steed)));
        steed.Damage = 2;
        Kill(game, steed);
        var back = me.BoardZone.GetSpan().ToArray().Single(m => m.Card.Id == "CAP_800");
        Assert.NotEqual(steed.Id, back.Id);
        Assert.Equal(steed.Card.ATK + 2, back.AttackDamage);
        Assert.Equal(0, back.Damage);
        Assert.Equal(steed.Card.Health + 3, back.Health);
        Assert.Equal(1, back[GameTag.HAS_BEEN_REBORN]);
        Assert.Equal(0, back[GameTag.REBORN]);
        Assert.Single(SabberStoneCore.Actions.Generic.RebornThisGame(me));
        Clear();

        // Molten Gold: three spells cast while held turn it into its minion in hand
        EmptyHand();
        var gold = Give(me, "JAIL_801");
        Assert.Equal(3, gold[GameTag.TAG_SCRIPT_DATA_NUM_1]);
        Assert.NotNull(gold.ActivatedTrigger);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Hand("CS2_025"))));
        Assert.Equal(2, gold[GameTag.TAG_SCRIPT_DATA_NUM_1]);
        for (int i = 0; i < 2; i++)
            Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Hand("CS2_025"))));
        Assert.Equal("JAIL_801t", me.HandZone.GetAll().Single(p => p.Id == gold.Id).Card.Id); // the same entity (live: CHANGE_ENTITY)
        Clear();

        // Captain Crowley: two Cannoneers; at the end of the turn each fires twice (no enemy minions: the hero takes 4)
        EmptyHand();
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Hand("CAP_106"))));
        Assert.Equal(2, SabberStoneCore.Actions.Generic.Cannoneers(me).Count);
        int heroHealth = them.Hero.Health;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(me)));
        Assert.Equal(heroHealth - 4, them.Hero.Health);

        // Mystic Misdirection (theirs now: played on their turn): my attacker becomes a Sheep and the attack ends
        them.UsedMana = 0;
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(them, Give(them, "JAIL_315"))));
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(them)));
        var attacker = me.BoardZone.GetSpan().ToArray().First(m => m.Card.Id == "CAP_107t");
        int theirHealth = them.Hero.Health;
        game.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(me, attacker, them.Hero)); // (a cancelled attack reports false)
        Assert.Equal("CS2_tk1", attacker.Card.Id);
        Assert.Equal(theirHealth, them.Hero.Health);
        Assert.Empty(them.SecretZone.GetAll());
        Clear();

        // Aya, Lotus Kingpin: three Coins, the pick replaces them; a later Coin is the counterfeit too
        EmptyHand();
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Hand("JAIL_504"))));
        int jade = me.Choice.Choices.First(id => game.IdEntityDic[id].Card.Id == "JAIL_504t");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.ChooseTask.Pick(me, jade)));
        Assert.Equal(3, me.HandZone.GetAll().Count(p => p.Card.Id == "JAIL_504t"));
        Assert.DoesNotContain(me.HandZone.GetAll(), p => p.Card.Id == "GAME_005");
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Hand("JAIL_720"))));
        Kill(game, me.BoardZone.GetSpan().ToArray().Single(m => m.Card.Id == "JAIL_720"));
        Assert.Equal(4, me.HandZone.GetAll().Count(p => p.Card.Id == "JAIL_504t"));
        Clear();

        // Godfrey's rule: an overdrawn card is saved and comes back (Cost - 1) once a card is played
        EmptyHand();
        me[HsTag.GODFREY_RULE] = 1;
        game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Hand("CS2_231"))); // (just to reset)
        Clear();
        EmptyHand();
        SabberStoneCore.Actions.Generic.AddEnchantmentBlock(game, SabberStoneCore.Model.Cards.FromId("JAIL_509e"), me.Hero, me, 0, 0, 0);
        for (int i = 0; i < 10; i++) Give(me, "CS2_231");
        var overdrawn = me.DeckZone.TopCard;
        // (burned from a full hand: not drawn — rule book DC3/DC4 — so the draw hands back no card)
        Assert.Null(SabberStoneCore.Actions.Generic.Draw(me));
        Assert.Contains(me.SetasideZone.GetAll(), p => p[HsTag.GODFREY_SAVED] > 0 && p.Card.Id == overdrawn.Card.Id);
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, me.HandZone.GetAll().First())));
        var returned = me.HandZone.GetAll().Last();
        Assert.Equal(overdrawn.Card.Id, returned.Card.Id);
        Assert.Equal(Math.Max(0, overdrawn.Card.Cost - 1), returned.Cost);
        Clear();

        // Warptooth: four different friendly characters damaged on my turn summon it from hand
        EmptyHand();
        var warptooth = Give(me, "JAIL_421");
        var a = Put(me, "CS2_231"); var b = Put(me, "CS2_120"); var c3 = Put(me, "CS2_120");
        foreach (SabberStoneCore.Model.Entities.ICharacter ch in new SabberStoneCore.Model.Entities.ICharacter[] { me.Hero, a, b, c3 })
            SabberStoneCore.Actions.Generic.DamageCharFunc(them.Hero, ch, 1, false);
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(SabberStoneCore.Enums.Zone.PLAY, warptooth.Zone?.Type);
        Clear();

        // Slice and Dice: the other card played this turn again, then the turn ends
        EmptyHand();
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(me, Hand("CS2_231"))));
        int playedThisTurn = me.PlayHistory.Count(h => h.Turn == game.Turn && h.SourceCard?.Id == "CS2_231"); // (the Godfrey part's Wisps count too)
        Assert.True(game.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Spell(me, Hand("JAIL_500"))));
        Assert.Equal(1 + playedThisTurn, me.BoardZone.GetSpan().ToArray().Count(m => m.Card.Id == "CS2_231"));
        Assert.Equal(them, game.CurrentPlayer);
    }
}
