using HsServer.Engine;
using HsServer.Lobby;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;
using SGame = SabberStoneCore.Model.Game;

namespace HsServer.Tests;

/// <summary>The Dalaran Heist's Anomaly Mode (CardSets/HsServer/DalaranHeistCardsGenHs6.cs, Actions/DalaranHeistAnomalies.cs, the run side
/// in Lobby/HeistRuns.cs): the game-start shape live sends, each anomaly's rule, and which anomaly a run's games get.</summary>
public class DalaranHeistAnomalyTests
{
    private static readonly Lazy<CardDb> Db = new(() =>
    {
        string dir = CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
        SabberEngineGame.EnsureCards(dir);
        return CardDb.Load(dir);
    });

    private static readonly string[] Filler = { "CS2_118", "CS2_118", "CS2_124", "CS2_124", "CS2_201", "CS2_201", "CS2_186", "CS2_186", "CS2_118", "CS2_118" };

    private static readonly string[] All =
    {
        "DALA_850", "DALA_851", "DALA_852", "DALA_853", "DALA_854", "DALA_855", "DALA_856", "DALA_857", "DALA_858",
        "DALA_859", "DALA_860", "DALA_861", "DALA_862", "DALA_863", "DALA_864", "DALA_865", "DALA_866", "DALA_867",
    };

    /// <summary>A rules game with <paramref name="anomaly"/> for player 1 (the run's player): decks in order, no mulligan.</summary>
    private static SGame Game(string? anomaly, IEnumerable<string>? deck1 = null, int mana = 10, bool history = true)
    {
        _ = Db.Value;
        var game = new SGame(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.MAGE, Player2HeroClass = CardClass.WARRIOR,
            Player1Deck = (deck1 ?? Filler).Select(Cards.FromId).ToList(),
            Player2Deck = Filler.Select(Cards.FromId).ToList(),
            Player1Anomaly = anomaly == null ? null : Cards.FromId(anomaly),
            FillDecks = false, History = history, Shuffle = false, SkipMulligan = true,
        });
        game.StartGame();
        if (mana > 0)
        {
            game.Player1.BaseMana = mana;
            game.Player2.BaseMana = mana;
        }
        return game;
    }

    private static IPlayable Give(Controller c, string id) => Generic.DrawCard(c, Cards.FromId(id));

    private static Minion OnBoard(SGame game, Controller c, string id)
    {
        var m = (Minion)SabberStoneCore.Model.Entities.Entity.FromCard(c, Cards.FromId(id));
        Generic.SummonBlock.Invoke(game, m, -1, null);
        m[GameTag.NUM_TURNS_IN_PLAY] = 1;
        m.IsExhausted = false;
        return m;
    }

    private static IPlayable Play(SGame game, Controller me, string id, ICharacter? target = null)
    {
        me.UsedMana = 0;
        var card = Give(me, id);
        var task = card is Minion
            ? (target == null ? PlayCardTask.Minion(me, card) : PlayCardTask.MinionTarget(me, card, target))
            : (target == null ? PlayCardTask.Spell(me, card) : PlayCardTask.SpellTarget(me, card, target));
        Assert.True(game.Process(task), id);
        return card;
    }

    private static void EndTurns(SGame game, int n)
    {
        for (int i = 0; i < n; i++)
        {
            game.CurrentPlayer.UsedMana = 0;
            Assert.True(game.Process(EndTurnTask.Any(game.CurrentPlayer)));
        }
    }

    private static bool Has(IPlayable p, string enchantment) => p.AppliedEnchantments?.Any(e => e.Card.Id == enchantment) == true;

    // ------------------------------------------------------------------------------------------------ every anomaly; the start

    [Fact]
    public void Every_anomaly_and_its_enchantments_have_a_script()
    {
        _ = Db.Value;
        string[] enchantments =
        {
            "DALA_850e", "DALA_851e", "DALA_852e", "DALA_853e", "DALA_854e", "DALA_854e2", "DALA_854e3", "DALA_855e", "DALA_856e", "DALA_857e",
            "DALA_858e", "DALA_860e", "DALA_861e", "DALA_861e2", "DALA_862e", "DALA_864e", "DALA_864e2", "DALA_865e", "DALA_865e2", "DALA_866e",
            "DALA_867e",
        };
        var missing = All.Concat(enchantments).Where(id => Cards.TryFromId(id) is not Card c || c.Power == null ||
            c.Power.PowerTask == null && c.Power.Trigger == null && c.Power.Aura == null && c.Power.DeathrattleTask == null && c.Power.Enchant == null).ToList();
        Assert.Empty(missing);
        // every anomaly plays in a game without errors (history on and off)
        foreach (string id in All)
            foreach (bool history in new[] { true, false })
            {
                var game = Game(id, history: history);
                EndTurns(game, 4);
                Assert.Equal(State.RUNNING, game.State);
            }
    }

    /// <summary>Every anomaly plays whole AI games (random Wild decks, the client stream checked) without a problem the client would see.</summary>
    [Fact]
    public void Every_anomaly_plays_whole_games_cleanly()
    {
        var failures = new List<string>();
        foreach (string anomaly in All)
            for (int round = 0; round < 2; round++)
            {
                var rng = new Random(Array.IndexOf(All, anomaly) * 101 + round); // (a fixed seed per anomaly and round)
                var engine = new SabberEngineGame(new HsServer.Game.EngineSetup
                {
                    Cards = Db.Value, Scenario = 260, Seed = rng.Next(), Rules = HsServer.Game.RulesEngine.Sabber,
                    GameSetupBody = HsServer.Protocol.PegasusPackets.Serialize(new PegasusGame.GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
                    Human = new PlayerSetup { HeroCardId = RandomDecks.AnyHero(CardClass.MAGE, rng), Deck = RandomDecks.BuildWild(CardClass.MAGE, rng), AccountHi = 1, AccountLo = 2, AnomalyCardId = anomaly },
                    Opponent = new PlayerSetup { HeroCardId = RandomDecks.AnyHero(CardClass.SHAMAN, rng), Deck = RandomDecks.BuildWild(CardClass.SHAMAN, rng), IsAi = true },
                });
                var result = new SoakRunner(rng) { Trace = (_, _) => { } }.Play(engine);
                if (!engine.Rules.IdEntityDic.Values.Any(e => e.Card.Id == anomaly)) failures.Add($"{anomaly}: never played");
                else if (result.Problems.Count > 0) failures.Add($"{anomaly}: {result.Problems[0]}");
            }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void The_anomaly_is_played_after_the_mulligan_and_the_passives_in_the_live_shape()
    {
        var game = Game("DALA_852", new[] { "LOOTA_801" }.Concat(Filler), mana: 0);
        var h = game.PowerHistory.Full;
        var anomalyIds = game.IdEntityDic.Values.Where(e => e.Card.Id == "DALA_852").Select(e => e.Id).OrderBy(i => i).ToList();
        Assert.Equal(2, anomalyIds.Count); // the history's copy (set aside) and the cast one (graveyard)
        Assert.Equal(Zone.SETASIDE, game.IdEntityDic[anomalyIds[0]].Zone.Type);
        Assert.Equal(Zone.GRAVEYARD, game.IdEntityDic[anomalyIds[1]].Zone.Type);
        Assert.Equal(game.Id, game.IdEntityDic[anomalyIds[0]][GameTag.CREATOR]);
        // its game TRIGGER block: both FULL_ENTITYs, the big card, the POWER block, then the tile on the set-aside one — no END tile
        int start = h.FindIndex(e => e is PowerHistoryFullEntity f && f.Entity.Id == anomalyIds[0]) - 1;
        Assert.True(h[start] is PowerHistoryBlockStart { BlockType: BlockType.TRIGGER } b && b.Source == game.Id && b.Index == -1);
        int end = start + 1, depth = 1;
        for (; depth > 0; end++) depth += h[end] is PowerHistoryBlockStart ? 1 : h[end] is PowerHistoryBlockEnd ? -1 : 0;
        var block = h.Skip(start).Take(end - start).ToList();
        Assert.Contains(block, e => e is PowerHistoryMetaData { Type: MetaDataType.SHOW_BIG_CARD } m && m.Info.Contains(anomalyIds[1]));
        Assert.Contains(block, e => e is PowerHistoryBlockStart { BlockType: BlockType.POWER } p && p.Source == anomalyIds[1]);
        Assert.True(block[^3] is PowerHistoryMetaData { Type: MetaDataType.BEGIN_ARTIFICIAL_HISTORY_TILE } t1 && t1.Info.Single() == anomalyIds[0]);
        Assert.True(block[^2] is PowerHistoryMetaData { Type: MetaDataType.OVERRIDE_HISTORY } t2 && t2.Info.Single() == anomalyIds[0]);
        Assert.DoesNotContain(block, e => e is PowerHistoryMetaData { Type: MetaDataType.END_ARTIFICIAL_HISTORY_TILE });
        // after the passive's block (live 185040: the passives 5508, the anomaly 5565), before the first MAIN_READY
        int passive = h.FindIndex(e => e is PowerHistoryFullEntity f && f.Entity.Tags.TryGetValue(GameTag.ENTITY_ID, out _) &&
            game.IdEntityDic.TryGetValue(f.Entity.Id, out var p) && p.Card.Id == "LOOTA_801" && p.Zone?.Type == Zone.GRAVEYARD);
        Assert.InRange(passive, 0, start);
        int mainReady = h.FindIndex(e => e is PowerHistoryTagChange { Tag: GameTag.STEP, Value: (int)Step.MAIN_READY });
        Assert.True(mainReady > end);
        // the player enchantment, on player 1
        Assert.Contains(game.Player1.AppliedEnchantments!, e => e.Card.Id == "DALA_852e");
    }

    // ------------------------------------------------------------------------------------------------ each anomaly

    [Fact]
    public void Arcane_makes_every_spell_in_both_hands_cost_two_less()
    {
        var game = Game("DALA_850");
        var mine = Give(game.Player1, "CS2_029"); // Fireball 4
        var theirs = Give(game.Player2, "CS2_029");
        var minion = Give(game.Player1, "CS2_186");
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(2, mine.Cost);
        Assert.Equal(2, theirs.Cost);
        Assert.Equal(7, minion.Cost);
        Assert.True(Has(mine, "GBL_002e")); // (live: the client's generic "Cost - 2" on each spell)
        Assert.Single(game.Player1.AppliedEnchantments!, e => e.Card.Id == "DALA_850e");
        Assert.DoesNotContain(game.Player2.AppliedEnchantments ?? new List<Enchantment>(), e => e.Card.Id == "DALA_850e");
    }

    [Fact]
    public void Summons_summons_a_minion_of_the_spells_cost_for_its_caster()
    {
        var game = Game("DALA_851");
        var me = game.Player1;
        Play(game, me, "CS2_029", me.Opponent.Hero); // Fireball (4)
        var summoned = Assert.Single(me.BoardZone);
        Assert.Equal(4, summoned.Card.Cost);
        // the opponent's spells summon for the opponent (its own enchantment)
        EndTurns(game, 1);
        Play(game, game.Player2, "CS2_029", me.Hero);
        Assert.Equal(4, Assert.Single(game.Player2.BoardZone).Card.Cost);
    }

    [Fact]
    public void Crying_and_rattling_double_both_players_battlecries_and_deathrattles()
    {
        var game = Game("DALA_852");
        Assert.Equal(2, game.Player1.BattlecryRuns);
        Assert.Equal(2, game.Player2.BattlecryRuns);
        int hp = game.Player2.Hero.Health;
        Play(game, game.Player1, "CS2_189", game.Player2.Hero); // Elven Archer: 1 damage, twice
        Assert.Equal(hp - 2, game.Player2.Hero.Health);

        var game2 = Game("DALA_853");
        Assert.Equal(2, game2.Player2.DeathrattleTimes(game2.Player2.HandZone[0]));
        var gnome = OnBoard(game2, game2.Player1, "EX1_029"); // Leper Gnome: 2 to the enemy hero, twice
        int hp2 = game2.Player2.Hero.Health;
        Play(game2, game2.Player1, "CS2_029", gnome);
        Assert.Equal(hp2 - 4, game2.Player2.Hero.Health);
    }

    [Fact]
    public void Nesting_gives_a_played_minion_a_deathrattle_that_summons_a_one_one_copy()
    {
        var game = Game("DALA_854");
        var me = game.Player1;
        var golem = (Minion)Play(game, me, "CS2_186");
        Assert.True(Has(golem, "DALA_854e2"));
        var summoned = OnBoard(game, me, "CS2_124"); // summoned, not played: no Nest
        Assert.False(Has(summoned, "DALA_854e2"));
        golem.Destroy();
        game.DeathProcessingAndAuraUpdate();
        var copy = Assert.Single(me.BoardZone, m => m.Card.Id == "CS2_186");
        Assert.Equal(1, copy.AttackDamage);
        Assert.Equal(1, copy.Health);
        Assert.True(Has(copy, "DALA_854e3"));
        Assert.False(Has(copy, "DALA_854e2"));
    }

    [Fact]
    public void Refreshing_draws_each_player_up_to_five_at_the_end_of_their_turn()
    {
        var game = Game("DALA_855", mana: 0);
        Assert.Equal(2, game.IdEntityDic.Values.Count(e => e.Card.Id == "DALA_855e")); // one per player (live)
        Assert.Equal(4, game.Player1.HandZone.Count);
        EndTurns(game, 1);
        Assert.Equal(5, game.Player1.HandZone.Count); // 4 → 5
        // the opponent's own enchantment, at the end of its own turn: from 2 cards back to 5
        var them = game.Player2;
        while (them.HandZone.Count > 2) them.HandZone.Remove(them.HandZone[0]);
        EndTurns(game, 1);
        Assert.Equal(5, them.HandZone.Count);
        // live: its block carries TriggerKeyword TRIGGER_VISUAL (the enchantment card's tag)
        Assert.Contains(game.PowerHistory.Full, e => e is PowerHistoryBlockStart { BlockType: BlockType.TRIGGER } b &&
            game.IdEntityDic.TryGetValue(b.Source, out var src) && src.Card.Id == "DALA_855e" && b.TriggerKeyword == (int)GameTag.TRIGGER_VISUAL);
    }

    [Fact]
    public void Wild_magic_casts_a_played_spell_again_from_the_players_enchantment()
    {
        var game = Game("DALA_856");
        var me = game.Player1;
        int hand = me.HandZone.Count, deck = me.DeckZone.Count;
        var played = Play(game, me, "CS2_023"); // Arcane Intellect: draw 2, then the copy draws 2
        Assert.Equal(deck - 4, me.DeckZone.Count);
        // the copy: a new entity made by the player's DALA_856e, then the tile (a DALA_856 in REMOVEDFROMGAME) naming the played spell
        var granter = game.IdEntityDic.Values.OfType<Enchantment>().Single(e => e.Card.Id == "DALA_856e" && e.Target == me);
        var copy = game.IdEntityDic.Values.Single(e => e.Card.Id == "CS2_023" && e.Id != played.Id);
        Assert.Equal(granter.Id, copy[GameTag.CREATOR]);
        var h = game.PowerHistory.Full;
        int tile = h.FindLastIndex(e => e is PowerHistoryMetaData { Type: MetaDataType.BEGIN_ARTIFICIAL_HISTORY_TRIGGER_TILE });
        Assert.True(tile > 0);
        Assert.Equal("DALA_856", game.IdEntityDic[((PowerHistoryMetaData)h[tile]).Info.Single()].Card.Id);
        Assert.True(h[tile + 2] is PowerHistoryMetaData { Type: MetaDataType.HISTORY_TARGET } ht && ht.Info.Single() == played.Id);
        // a spell an effect casts isn't cast again: one copy only
        Assert.Equal(2, game.IdEntityDic.Values.Count(e => e.Card.Id == "CS2_023"));
    }

    [Fact]
    public void Glittering_adds_a_treasure_after_a_card_of_eight_or_more()
    {
        var game = Game("DALA_857");
        var me = game.Player1;
        Play(game, me, "CS2_186"); // 7: nothing
        Assert.DoesNotContain(me.HandZone, p => CurrentCoreCardsGenTreasures.Contains(p.Card.Id));
        Play(game, me, "EX1_279", me.Opponent.Hero); // Pyroblast (10)
        Assert.Single(me.HandZone, p => CurrentCoreCardsGenTreasures.Contains(p.Card.Id));
    }

    private static readonly HashSet<string> CurrentCoreCardsGenTreasures = new()
    {
        "DALA_700", "DALA_701", "DALA_702", "DALA_703", "DALA_704", "DALA_705", "DALA_706", "DALA_707", "DALA_708", "DALA_709",
        "DALA_711", "DALA_712", "DALA_713", "DALA_714", "DALA_715", "DALA_716", "DALA_717", "DALA_718", "DALA_719", "DALA_720",
        "DALA_721", "DALA_722", "DALA_723", "DALA_724", "DALA_725", "DALA_726", "DALA_727", "DALA_729", "DALA_730",
    };

    [Fact]
    public void Fishy_sometimes_summons_a_murloc_at_the_start_of_a_turn()
    {
        var game = Game("DALA_858", mana: 0);
        var murlocs = new HashSet<int>();
        for (int i = 0; i < 40 && game.State == State.RUNNING; i++)
        {
            foreach (var c in new[] { game.Player1, game.Player2 })
                foreach (var m in c.BoardZone.GetAll()) murlocs.Add(m.Id);
            EndTurns(game, 1);
        }
        // (no one plays a card: every minion that reached the board came from the anomaly)
        var summoned = game.IdEntityDic.Values.OfType<Minion>().Where(m => m[GameTag.NUM_TURNS_IN_PLAY] > 0 || m.Zone?.Type == Zone.PLAY).ToList();
        Assert.NotEmpty(summoned);
        Assert.All(summoned, m => Assert.True(m.Card.IsRace(Race.MURLOC), m.Card.Id));
    }

    [Fact]
    public void Gorged_gives_both_players_two_cards_and_two_crystals()
    {
        var plain = Game(null, mana: 0);
        var game = Game("DALA_859", mana: 0);
        Assert.Equal(plain.Player1.HandZone.Count + 2, game.Player1.HandZone.Count);
        Assert.Equal(plain.Player2.HandZone.Count + 2, game.Player2.HandZone.Count);
        Assert.Equal(plain.Player1.BaseMana + 2, game.Player1.BaseMana);
        EndTurns(game, 1);
        Assert.Equal(3, game.Player2.BaseMana); // its first turn: 2 + 1 (live: RESOURCES 3 at its MAIN_READY)
    }

    [Fact]
    public void Dragon_soul_summons_a_dragon_after_three_spells_in_a_turn()
    {
        var game = Game("DALA_860");
        var me = game.Player1;
        Play(game, me, "GAME_005");
        Play(game, me, "GAME_005");
        Assert.Empty(me.BoardZone);
        Play(game, me, "GAME_005");
        Assert.Equal("LOOT_209t", Assert.Single(me.BoardZone).Card.Id);
        // the count starts over each turn
        Play(game, me, "GAME_005");
        Play(game, me, "GAME_005");
        EndTurns(game, 2);
        Play(game, me, "GAME_005");
        Assert.Single(me.BoardZone);
    }

    [Fact]
    public void Ogreish_minions_attack_the_wrong_enemy_about_half_the_time()
    {
        int wrong = 0, tries = 0;
        for (int seed = 0; seed < 30; seed++)
        {
            var game = Game("DALA_861", mana: 0);
            var me = game.Player1;
            var attacker = OnBoard(game, me, "CS2_124"); // Wolfrider 3/1 at a War Golem: 3 damage when it hits
            var target = OnBoard(game, me.Opponent, "CS2_186");
            OnBoard(game, me.Opponent, "CS2_186");
            Assert.True(Has(attacker, "DALA_861e2"));
            Assert.True(Has(target, "DALA_861e2"));
            Assert.True(game.Process(MinionAttackTask.Any(me, attacker, target)));
            tries++;
            if (target.Damage == 0) wrong++;
        }
        Assert.True(wrong >= 3 && wrong <= tries - 3, $"wrong {wrong} of {tries}");
        // (live Ogre-Gang Outlaw: the attack trigger's block carries TriggerKeyword FORGETFUL)
    }

    [Fact]
    public void Reductive_lowers_each_card_in_the_players_hand_by_one_at_the_end_of_their_turn()
    {
        var game = Game("DALA_862", mana: 0);
        var me = game.Player1;
        var golem = Give(me, "CS2_186");
        var theirs = Give(me.Opponent, "CS2_186");
        EndTurns(game, 1);
        Assert.Equal(6, golem.Cost);
        Assert.Equal(7, theirs.Cost);
        EndTurns(game, 1);
        Assert.Equal(6, theirs.Cost);
    }

    [Fact]
    public void Hallucinating_shuffles_ten_hallucinations_into_each_deck()
    {
        var game = Game("DALA_863", mana: 0);
        Assert.Equal(10, game.Player1.DeckZone.Count(p => p.Card.Id == "UNG_856") + game.Player1.HandZone.Count(p => p.Card.Id == "UNG_856"));
        Assert.Equal(10, game.Player2.DeckZone.Count(p => p.Card.Id == "UNG_856") + game.Player2.HandZone.Count(p => p.Card.Id == "UNG_856"));
    }

    [Fact]
    public void Growing_grows_the_players_minions_at_the_end_of_their_turn()
    {
        var game = Game("DALA_864", mana: 0);
        var mine = OnBoard(game, game.Player1, "CS2_124"); // 3/1
        var theirs = OnBoard(game, game.Player2, "CS2_124");
        EndTurns(game, 1);
        Assert.Equal(4, mine.AttackDamage);
        Assert.Equal(2, mine.Health);
        Assert.Equal(3, theirs.AttackDamage);
        Assert.True(Has(mine, "DALA_864e2"));
    }

    [Fact]
    public void Explosive_gives_every_summoned_minion_a_damaging_deathrattle()
    {
        var game = Game("DALA_865");
        var me = game.Player1;
        var golem = OnBoard(game, me, "CS2_186");
        var theirs = OnBoard(game, me.Opponent, "CS2_186");
        Assert.True(Has(golem, "DALA_865e2"));
        Assert.True(Has(theirs, "DALA_865e2"));
        var wolf = (Minion)Play(game, me, "CS2_124"); // played: summoned too
        Assert.True(Has(wolf, "DALA_865e2"));
        Play(game, me, "CS2_029", wolf); // its Deathrattle: 1 to all minions
        Assert.Equal(1, golem.Damage);
        Assert.Equal(1, theirs.Damage);
    }

    [Fact]
    public void Rejuvenating_heals_each_hero_two_at_the_start_of_its_turn()
    {
        var game = Game("DALA_866", mana: 0);
        game.Player2.Hero.Damage = 5;
        game.Player1.Hero.Damage = 5;
        EndTurns(game, 1);
        Assert.Equal(3, game.Player2.Hero.Damage);
        Assert.Equal(5, game.Player1.Hero.Damage);
    }

    [Fact]
    public void Infused_gives_every_summoned_minion_one_of_four_keywords()
    {
        var game = Game("DALA_867");
        var me = game.Player1;
        for (int i = 0; i < 6; i++)
        {
            var m = OnBoard(game, i % 2 == 0 ? me : me.Opponent, "CS2_186");
            int keywords = (m.HasTaunt ? 1 : 0) + (m.HasDivineShield ? 1 : 0) + (m.IsRush ? 1 : 0) + (m.HasWindfury ? 1 : 0);
            Assert.Equal(1, keywords);
        }
    }

    // ------------------------------------------------------------------------------------------------ the run: which anomaly

    private const long Normal = 55, ChapterOne = 3005, Warrior = 10, MrChu = 54546, ArmorUp = 725;
    private static readonly string DbfDir = CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
    private static readonly Lazy<HeistRuns> Heist = new(() =>
        new HeistRuns(DbfDir, Path.Combine(TestEnvironment.ProjectRoot, "Server", "data", "heist"), Db.Value));

    private static SaveDataStore Picked(long anomalyMode, long preview)
    {
        var sd = new SaveDataStore();
        foreach (var (sub, v) in new[] { (238L, ChapterOne), (239L, Warrior), (236L, ArmorUp), (237L, 1955L), (246L, anomalyMode), (392L, preview) })
            sd.SetInts(Normal, sub, new[] { v });
        return sd;
    }

    [Fact]
    public void An_anomaly_mode_run_plays_its_pre_rolled_anomaly_in_every_boss_game()
    {
        const long WildMagic = 55891;
        var sd = Picked(anomalyMode: 1, preview: WildMagic);
        var (human, boss) = Heist.Value.Prepare(sd, ChapterOne, MrChu, new Random(1))!.Value;
        // live: at the run's start CURRENT_ANOMALY_MODE_CARD (243) = the preview (392), IS_ANOMALY_MODE (377) = the pick (246)
        Assert.Equal(WildMagic, sd.Values(Normal, 243).Single());
        Assert.Equal(WildMagic, sd.Values(Normal, 392).Single());
        Assert.Equal(1, sd.Values(Normal, 377).Single());
        Assert.Equal("DALA_856", human.AnomalyCardId);
        Assert.Null(boss.AnomalyCardId);
        // the next game of the run: the same anomaly
        Heist.Value.GameEnded(sd, ChapterOne, won: true, new Random(2));
        Assert.Equal("DALA_856", Heist.Value.Prepare(sd, ChapterOne, MrChu, new Random(3))!.Value.Human.AnomalyCardId);
        // the run's end rolls the next preview (never the one just used); 243 and 377 keep the last run's
        Heist.Value.GameEnded(sd, ChapterOne, won: false, new Random(4));
        long next = sd.Values(Normal, 392).Single();
        Assert.NotEqual(WildMagic, next);
        Assert.InRange(next, 55878, 56477);
        Assert.Equal(WildMagic, sd.Values(Normal, 243).Single());
        Assert.Equal(1, sd.Values(Normal, 377).Single());

        // the engine plays it: the game built from the setup has DALA_856e on both players after the mulligan
        var sd2 = Picked(anomalyMode: 1, preview: WildMagic);
        var (h2, b2) = Heist.Value.Prepare(sd2, ChapterOne, MrChu, new Random(5))!.Value;
        h2.AccountHi = 1; h2.AccountLo = 2;
        b2.Deck = RandomDecks.Build(CardClass.MAGE, new Random(5), CardSet.CORE, CardSet.EXPERT1);
        var engine = new SabberEngineGame(new HsServer.Game.EngineSetup
        {
            Cards = Db.Value, Scenario = (int)ChapterOne, Seed = 5, Rules = HsServer.Game.RulesEngine.Sabber,
            GameSetupBody = HsServer.Protocol.PegasusPackets.Serialize(new PegasusGame.GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
            Human = h2, Opponent = b2,
        });
        var result = new SoakRunner(new Random(5)) { Trace = (_, _) => { } }.Play(engine);
        Assert.Empty(result.Problems);
        Assert.Equal(2, engine.Rules.IdEntityDic.Values.Count(e => e.Card.Id == "DALA_856e"));
    }

    [Fact]
    public void A_run_without_anomaly_mode_plays_none_but_still_uses_up_the_preview()
    {
        const long Nesting = 55912;
        var sd = Picked(anomalyMode: 0, preview: Nesting);
        var (human, _) = Heist.Value.Prepare(sd, ChapterOne, MrChu, new Random(1))!.Value;
        Assert.Null(human.AnomalyCardId);
        // live (live-session-20261007-113244 #96724): 243 = the preview even with the mode off; 377 = 0
        Assert.Equal(Nesting, sd.Values(Normal, 243).Single());
        Assert.Equal(0, sd.Values(Normal, 377).Single());
        // a player with no preview yet gets one rolled at the run's start
        var fresh = Picked(anomalyMode: 1, preview: 0);
        var (h, _) = Heist.Value.Prepare(fresh, ChapterOne, MrChu, new Random(2))!.Value;
        Assert.NotNull(h.AnomalyCardId);
        Assert.StartsWith("DALA_8", h.AnomalyCardId);
    }
}
