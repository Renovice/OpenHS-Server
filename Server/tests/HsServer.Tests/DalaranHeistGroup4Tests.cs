using HsServer.Engine;
using HsServer.Game;
using HsServer.Lobby;
using HsServer.Protocol;
using GameSetup = PegasusGame.GameSetup;
using EntityChoices = PegasusGame.EntityChoices;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;
using SGame = SabberStoneCore.Model.Game;

namespace HsServer.Tests;

/// <summary>The Dalaran Heist, group 4 (CardSets/HsServer/DalaranHeistCardsGenHs4.cs, Actions/DalaranHeistGroup4.cs): passives set aside
/// and played at the start of the game, chapter twists, the player heroes' Hero Powers and the treasures.</summary>
public class DalaranHeistGroup4Tests
{
    private static readonly Lazy<CardDb> Db = new(() =>
    {
        string dir = CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
        SabberEngineGame.EnsureCards(dir);
        return CardDb.Load(dir);
    });

    private static readonly string[] Filler = { "CS2_118", "CS2_118", "CS2_124", "CS2_124", "CS2_201", "CS2_201", "CS2_186", "CS2_186", "CS2_168", "CS2_168" };

    /// <summary>A rules game: player 1's deck in this order (no shuffle), no mulligan, an optional chapter twist for player 1.</summary>
    private static SGame Game(IEnumerable<string>? deck1 = null, string? twist = null, IEnumerable<string>? deck2 = null, int mana = 10)
    {
        _ = Db.Value;
        var game = new SGame(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.MAGE, Player2HeroClass = CardClass.WARRIOR,
            Player1Deck = (deck1 ?? Filler).Select(Cards.FromId).ToList(),
            Player2Deck = (deck2 ?? Filler).Select(Cards.FromId).ToList(),
            Player1PlotTwist = twist == null ? null : Cards.FromId(twist),
            FillDecks = false, History = true, Shuffle = false, SkipMulligan = true,
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

    private static void Play(SGame game, Controller me, string id, ICharacter? target = null)
    {
        me.UsedMana = 0;
        var card = Give(me, id);
        var task = card is Minion
            ? (target == null ? PlayCardTask.Minion(me, card) : PlayCardTask.MinionTarget(me, card, target))
            : (target == null ? PlayCardTask.Spell(me, card) : PlayCardTask.SpellTarget(me, card, target));
        Assert.True(game.Process(task), id);
    }

    private static void EndTurns(SGame game, int n)
    {
        for (int i = 0; i < n; i++)
        {
            game.CurrentPlayer.UsedMana = 0;
            Assert.True(game.Process(EndTurnTask.Any(game.CurrentPlayer)));
        }
    }

    // ------------------------------------------------------------------------------------------------ every card of the group

    [Fact]
    public void Every_heist_treasure_passive_twist_and_hero_power_has_a_script()
    {
        _ = Db.Value;
        string[] ids =
        {
            "DALA_Druid_HP1", "DALA_Druid_HP2", "DALA_Hunter_HP1", "DALA_Hunter_HP2", "DALA_Mage_HP1", "DALA_Mage_HP2", "DALA_Paladin_HP1",
            "DALA_Paladin_HP2", "DALA_Priest_HP1", "DALA_Priest_HP2", "DALA_Rogue_HP1", "DALA_Rogue_HP2", "DALA_Shaman_HP1", "DALA_Shaman_HP2",
            "DALA_Warlock_HP1", "DALA_Warlock_HP2", "DALA_Warrior_HP1", "DALA_Warrior_HP2",
            "LOOTA_800", "LOOTA_801", "LOOTA_803", "LOOTA_804", "LOOTA_825", "LOOTA_828", "LOOTA_831", "LOOTA_832", "LOOTA_833", "LOOTA_845",
            "LOOTA_846", "GILA_506", "GILA_813", "DALA_728", "DALA_731", "DALA_733", "DALA_735", "DALA_736", "DALA_737", "DALA_738", "DALA_739",
            "DALA_740", "DALA_741", "DALA_743", "DALA_744", "DALA_745", "DALA_746", "DALA_747", "DALA_748",
            "DALA_700", "DALA_701", "DALA_702", "DALA_703", "DALA_704", "DALA_705", "DALA_706", "DALA_707", "DALA_708", "DALA_709", "DALA_711",
            "DALA_712", "DALA_713", "DALA_714", "DALA_715", "DALA_716", "DALA_717", "DALA_718", "DALA_719", "DALA_720", "DALA_721", "DALA_722",
            "DALA_723", "DALA_724", "DALA_725", "DALA_726", "DALA_727", "DALA_729", "DALA_730",
            "DALA_829", "DALA_831", "DALA_832", "DALA_833",
        };
        var missing = ids.Where(id => Cards.TryFromId(id) is not Card c || c.Power == null ||
            c.Power.PowerTask == null && c.Power.Trigger == null && c.Power.Aura == null && c.Power.DeathrattleTask == null).ToList();
        Assert.Empty(missing);
    }

    // ------------------------------------------------------------------------------------------------ passives

    [Fact]
    public void A_passive_starts_set_aside_and_acts_after_the_mulligan()
    {
        var game = Game(new[] { "LOOTA_801", "LOOTA_828" }.Concat(Filler), mana: 0);
        var me = game.Player1;
        Assert.DoesNotContain(me.DeckZone, p => p.Card.Id is "LOOTA_801" or "LOOTA_828");
        Assert.Contains(me.SetasideZone, p => p.Card.Id == "LOOTA_801" && p[Generic.HEIST_PASSIVE_PLAYED] == 1);
        Assert.Contains(me.SetasideZone, p => p.Card.Id == "LOOTA_828" && p[Generic.HEIST_PASSIVE_PLAYED] == 1);
        Assert.Equal(2, me.BaseMana); // Crystal Gem: an extra crystal, then the turn's
        // Captured Flag: the player's minions in hand have +1/+1 (Magma Rager 5/1 → 6/2)
        var rager = me.HandZone.First(p => p.Card.Id == "CS2_118");
        Assert.Equal(6, ((Minion)rager).AttackDamage);
        Assert.Equal(2, ((Minion)rager).Health);
        Assert.Contains(me.AppliedEnchantments!, e => e.Card.Id == "LOOTA_828e2");
        // the live shape: the copy cast in a game block, then the original's artificial history tile
        var h = game.PowerHistory.Full;
        Assert.Contains(h, e => e is PowerHistoryMetaData { Type: MetaDataType.BEGIN_ARTIFICIAL_HISTORY_TILE });
    }

    [Fact]
    public void Robes_of_gaudiness_halve_costs_and_stop_the_third_card()
    {
        var game = Game(new[] { "DALA_735" }.Concat(Filler));
        var me = game.Player1;
        var golem = Give(me, "CS2_186"); // War Golem (7) → 3
        Assert.Equal(3, golem.Cost);
        Play(game, me, "CS2_168");
        Play(game, me, "CS2_168");
        var third = Give(me, "CS2_124");
        me.UsedMana = 0;
        Assert.False(third.IsPlayable);
        EndTurns(game, 2);
        Assert.True(third.IsPlayable);
    }

    [Fact]
    public void Elixir_of_vim_draws_two_more_and_ignores_fatigue()
    {
        var game = Game(new[] { "DALA_747", "CS2_168", "CS2_168", "CS2_168" }, mana: 0);
        var me = game.Player1;
        int hp = me.Hero.Health;
        EndTurns(game, 2); // my next turn: 1 + 2 draws from an empty deck
        Assert.Equal(hp, me.Hero.Health);
        Assert.Equal(0, me.Hero.Fatigue);
    }

    [Fact]
    public void Emerald_goggles_discount_only_the_left_most_card()
    {
        var game = Game(new[] { "DALA_733" }.Concat(Filler), mana: 0);
        var me = game.Player1;
        var left = me.HandZone[0];
        Assert.Equal(System.Math.Max(0, left.Card.Cost - 2), left.Cost);
        Assert.Equal(me.HandZone[1].Card.Cost, me.HandZone[1].Cost);
    }

    [Fact]
    public void Book_of_wonders_shuffles_ten_scrolls_in_two_blocks()
    {
        var game = Game(new[] { "DALA_739" }.Concat(Filler), mana: 0);
        // (a Scroll drawn at once is cast: count every Scroll made)
        Assert.Equal(10, game.IdEntityDic.Values.Count(p => p.Card.Id == "LOOT_106t" && p.Controller == game.Player1 && p[GameTag.CREATOR] > 0
            && game.IdEntityDic.TryGetValue(p[GameTag.CREATOR], out var cr) && cr.Card.Id == "DALA_739"));
    }

    [Fact]
    public void Battle_totem_and_totem_of_the_dead_double_the_players_effects()
    {
        var game = Game(new[] { "LOOTA_846", "LOOTA_845" }.Concat(Filler), mana: 0);
        Assert.Equal(2, game.Player1.BattlecryRuns);
        Assert.Equal(2, game.Player1.DeathrattleTimes(game.Player1.HandZone[0]));
    }

    // ------------------------------------------------------------------------------------------------ twists

    [Fact]
    public void The_coffers_give_the_boss_a_cache_of_cash_that_pays_both_players()
    {
        var game = Game(twist: "DALA_829");
        var me = game.Player1;
        var cache = Assert.Single(me.Opponent.BoardZone, m => m.Card.Id == "DALA_829t");
        // the deck list's copy stays set aside, the played one is in the graveyard
        Assert.Contains(me.SetasideZone, p => p.Card.Id == "DALA_829");
        Assert.Contains(me.GraveyardZone, p => p.Card.Id == "DALA_829");
        int mine = me.HandZone.Count(p => p.Card.Id.Contains("COIN") || p.Card.Id == "GAME_005");
        int theirs = me.Opponent.HandZone.Count(p => p.Card.Id.Contains("COIN") || p.Card.Id == "GAME_005");
        Play(game, me, "CS2_029", cache); // Fireball
        Assert.Equal(mine + 2, me.HandZone.Count(p => p.Card.Id.Contains("COIN") || p.Card.Id == "GAME_005"));
        Assert.Equal(theirs + 2, me.Opponent.HandZone.Count(p => p.Card.Id.Contains("COIN") || p.Card.Id == "GAME_005"));
        // no big card for a twist (live: only the 3 s pause)
        var h = game.PowerHistory.Full;
        var twistIds = game.IdEntityDic.Values.Where(e => e.Card.Id == "DALA_829").Select(e => e.Id).ToHashSet();
        Assert.DoesNotContain(h, e => e is PowerHistoryMetaData { Type: MetaDataType.SHOW_BIG_CARD } m && m.Info.Any(twistIds.Contains));
    }

    [Fact]
    public void The_stench_swaps_every_minion_now_and_later()
    {
        var game = Game(twist: "DALA_831", mana: 10);
        var me = game.Player1;
        var rager = (Minion)me.HandZone.First(p => p.Card.Id == "CS2_118"); // 5/1 → 1/5
        Assert.Equal(1, rager.AttackDamage);
        Assert.Equal(5, rager.Health);
        Assert.All(me.DeckZone.Concat(me.Opponent.DeckZone), p => Assert.Equal(1, p[Generic.HEIST_STENCH_SWAPPED]));
        var golem = (Minion)Give(me, "CS2_186"); // 7/7 stays 7/7
        Assert.Equal(1, golem[Generic.HEIST_STENCH_SWAPPED]);
        var raider = OnBoard(game, me, "CS2_124"); // Wolfrider 3/1 → 1/3, once
        Assert.Equal(1, raider.AttackDamage);
        Assert.Equal(3, raider.Health);
    }

    [Fact]
    public void The_carts_leave_four_slots_and_the_prisons_open_after_the_minions_cost()
    {
        var carts = Game(twist: "DALA_833");
        Assert.Equal(3, carts.Player1.BoardZone.Count);
        Assert.Equal(3, carts.Player2.BoardZone.Count);
        Assert.All(carts.Player1.BoardZone.GetSpan().ToArray(), m => Assert.True(m.Untouchable));

        var prisons = Game(twist: "DALA_832");
        var me = prisons.Player1;
        var prison = Assert.Single(me.BoardZone.GetSpan().ToArray()); // (a permanent: enumerating the board skips it)
        Assert.Equal("DALA_832t", prison.Card.Id);
        int turns = prison[GameTag.TAG_SCRIPT_DATA_NUM_2];
        Card inside = Cards.FromAssetId(prison[GameTag.TAG_SCRIPT_DATA_NUM_1]);
        for (int i = 0; i < turns && me.BoardZone.GetSpan().ToArray().Any(m => m.Card.Id == "DALA_832t"); i++) EndTurns(prisons, 2);
        Assert.DoesNotContain(me.BoardZone.GetSpan().ToArray(), m => m.Card.Id == "DALA_832t");
        Assert.Contains(me.BoardZone, m => m.Card.Id == inside.Id);
    }

    // ------------------------------------------------------------------------------------------------ Hero Powers

    [Fact]
    public void Heist_hero_powers_do_what_they_say()
    {
        var game = Game();
        var me = game.Player1;
        // Frostburn: freeze, then 2 damage to a Frozen one
        new SabberStoneCore.Tasks.SimpleTasks.ReplaceHeroPower(Cards.FromId("DALA_Mage_HP2")).Process(game, me, me.Hero, null);
        var target = OnBoard(game, me.Opponent, "CS2_186");
        Assert.True(game.Process(HeroPowerTask.Any(me, target)));
        Assert.True(target.IsFrozen);
        Assert.Equal(7, target.Health);
        EndTurns(game, 2);
        target.IsFrozen = true;
        Assert.True(game.Process(HeroPowerTask.Any(me, target)));
        Assert.Equal(5, target.Health);

        // Undermine: two Explosives in the opponent's deck; drawing one costs 3 Health
        new SabberStoneCore.Tasks.SimpleTasks.ReplaceHeroPower(Cards.FromId("DALA_Warrior_HP2")).Process(game, me, me.Hero, null);
        me.Hero.HeroPower.IsExhausted = false;
        me.UsedMana = 0;
        Assert.True(game.Process(HeroPowerTask.Any(me)));
        Assert.Equal(2, me.Opponent.DeckZone.Count(p => p.Card.Id == "DALA_Warrior_HP2t"));
        int hp = me.Opponent.Hero.Health;
        Generic.Draw(me.Opponent, me.Opponent.DeckZone.First(p => p.Card.Id == "DALA_Warrior_HP2t"));
        // (the replacement draw may be the other Explosive)
        Assert.Contains(hp - me.Opponent.Hero.Health, new[] { 3, 6 });
    }

    // ------------------------------------------------------------------------------------------------ treasures

    [Fact]
    public void Big_boomba_destroys_everything_now_and_at_the_next_turn()
    {
        var game = Game();
        var me = game.Player1;
        OnBoard(game, me, "CS2_186");
        OnBoard(game, me.Opponent, "CS2_186");
        Play(game, me, "DALA_724");
        Assert.Empty(me.BoardZone);
        Assert.Empty(me.Opponent.BoardZone);
        EndTurns(game, 1);
        OnBoard(game, me.Opponent, "CS2_186");
        EndTurns(game, 1);
        Assert.Empty(me.Opponent.BoardZone);
        Assert.DoesNotContain(me.AppliedEnchantments ?? new List<Enchantment>(), e => e.Card.Id == "DALA_724e");
    }

    [Fact]
    public void Continuum_collider_gives_me_an_extra_turn_then_the_opponent_two()
    {
        var game = Game();
        var me = game.Player1;
        Play(game, me, "DALA_700");
        EndTurns(game, 1);
        Assert.Equal(me, game.CurrentPlayer);
        EndTurns(game, 1);
        Assert.Equal(me.Opponent, game.CurrentPlayer);
        EndTurns(game, 1);
        Assert.Equal(me.Opponent, game.CurrentPlayer);
        EndTurns(game, 1);
        Assert.Equal(me, game.CurrentPlayer);
    }

    [Fact]
    public void Overpowered_replays_the_cards_played_this_turn()
    {
        var game = Game();
        var me = game.Player1;
        Play(game, me, "DALA_715");
        Play(game, me, "CS2_186");
        Assert.Equal(2, me.BoardZone.Count(m => m.Card.Id == "CS2_186"));
        EndTurns(game, 2);
        Play(game, me, "CS2_168");
        Assert.Single(me.BoardZone, m => m.Card.Id == "CS2_168");
    }

    [Fact]
    public void Golden_candle_turns_every_other_card_into_golden_legendaries()
    {
        var game = Game();
        var me = game.Player1;
        OnBoard(game, me.Opponent, "CS2_186");
        Play(game, me, "DALA_709");
        Assert.All(me.HandZone.Concat(me.DeckZone).Concat(me.Opponent.HandZone).Concat(me.Opponent.DeckZone).Concat(me.Opponent.BoardZone),
            p => Assert.Equal(Rarity.LEGENDARY, p.Card.Rarity));
        Assert.Contains(me.BoardZone, m => m.Card.Id == "DALA_709");
    }

    [Fact]
    public void Master_scheme_grows_each_turn_in_hand()
    {
        var game = Game();
        var me = game.Player1;
        var scheme = Give(me, "DALA_726");
        EndTurns(game, 2);
        Assert.Equal(2, scheme[GameTag.TAG_SCRIPT_DATA_NUM_1]);
        int armor = me.Hero.Armor, hp = me.Opponent.Hero.Health;
        me.UsedMana = 0;
        Assert.True(game.Process(PlayCardTask.SpellTarget(me, scheme, me.Opponent.Hero)));
        Assert.Equal(hp - 2, me.Opponent.Hero.Health);
        Assert.Equal(armor + 2, me.Hero.Armor);
        Assert.Equal(2, me.BoardZone.Count(m => m.Card.Id == "GVG_110t"));
    }

    [Fact]
    public void Untold_splendor_and_the_box_use_the_treasure_pool()
    {
        var game = Game();
        var me = game.Player1;
        int before = me.DeckZone.Count;
        Play(game, me, "DALA_708");
        var added = me.DeckZone.Skip(0).Where(p => p.Card.Id.StartsWith("DALA_7")).ToList();
        Assert.Equal(5, added.Count);
        Assert.Equal(before + 5, me.DeckZone.Count);
        var box = Give(me, "DALA_701");
        Assert.False(box.IsPlayable);
        EndTurns(game, 2);
        Assert.NotEqual("DALA_701", me.HandZone.First(p => p.Id == box.Id || p[GameTag.TRANSFORMED_FROM_CARD] > 0).Card.Id);
    }

    // ------------------------------------------------------------------------------------------------ through the run and the adapter

    [Fact]
    public void A_heist_game_plays_the_chapter_twist_and_the_runs_passives_through_the_adapter()
    {
        _ = Db.Value;
        string dbf = CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
        var heist = new HeistRuns(dbf, Path.Combine(TestEnvironment.ProjectRoot, "Server", "data", "heist"), Db.Value);
        var sd = new SaveDataStore();
        const long normal = 55, chapterOne = 3005;
        sd.SetInts(normal, 238, new[] { chapterOne });
        sd.SetInts(normal, 239, new long[] { 10 });
        sd.SetInts(normal, 236, new long[] { 725 });
        sd.SetInts(normal, 237, new long[] { 1955 });
        sd.SetInts(normal, 246, new long[] { 0 });
        var (human, boss) = heist.Prepare(sd, chapterOne, 54546, new Random(1))!.Value;
        Assert.Equal("DALA_829", human.PlotTwistCardId); // Chapter 1: The Coffers (WingDbf.PlotTwistCardId 55455)
        human.Deck.Insert(0, "LOOTA_828");
        human.AccountHi = 1; human.AccountLo = 2;
        if (boss.Deck.Count == 0) boss.Deck = Filler.ToList();
        var engine = new SabberEngineGame(new EngineSetup
        {
            Cards = Db.Value, Scenario = (int)chapterOne, Seed = 3, Rules = RulesEngine.Sabber,
            GameSetupBody = PegasusPackets.Serialize(new GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
            Human = human, Opponent = boss,
        });
        engine.Start();
        Assert.Contains(engine.Rules.Player2.BoardZone, m => m.Card.Id == "DALA_829t");
        Assert.Contains(engine.Rules.Player1.SetasideZone, p => p.Card.Id == "LOOTA_828");
        var choices = engine.Drain().OfType<EntityChoices>().Single(c => c.PlayerId == 1);
        Assert.True(engine.ChooseEntities(choices.Id, choices.Entities));
        var flag = engine.Rules.Player1.SetasideZone.First(p => p.Card.Id == "LOOTA_828");
        Assert.True(flag[Generic.HEIST_PASSIVE_PLAYED] == 1, $"step {engine.Rules.Step} mull {engine.Rules.Player1.MulliganState}/{engine.Rules.Player2.MulliganState}");
        Assert.Contains(engine.Rules.Player1.AppliedEnchantments!, e => e.Card.Id == "LOOTA_828e2");
        while (engine.AiToAct) engine.AiStep();
    }

    // ------------------------------------------------------------------------------------------------ robustness

    private static readonly string[] Passives =
    {
        "LOOTA_800", "LOOTA_801", "LOOTA_803", "LOOTA_804", "LOOTA_825", "LOOTA_828", "LOOTA_831", "LOOTA_832", "LOOTA_833", "LOOTA_845",
        "LOOTA_846", "GILA_506", "GILA_813", "DALA_728", "DALA_731", "DALA_733", "DALA_735", "DALA_736", "DALA_737", "DALA_738", "DALA_739",
        "DALA_740", "DALA_741", "DALA_743", "DALA_744", "DALA_745", "DALA_746", "DALA_747", "DALA_748",
    };

    private static readonly string[] Treasures =
    {
        "DALA_700", "DALA_701", "DALA_702", "DALA_703", "DALA_704", "DALA_705", "DALA_706", "DALA_707", "DALA_708", "DALA_709", "DALA_711",
        "DALA_712", "DALA_713", "DALA_714", "DALA_715", "DALA_716", "DALA_717", "DALA_718", "DALA_719", "DALA_720", "DALA_721", "DALA_722",
        "DALA_723", "DALA_724", "DALA_725", "DALA_726", "DALA_727", "DALA_729", "DALA_730",
    };

    /// <summary>Plays turns: each turn the current player plays what it can (first option of each card) and attacks, then ends.</summary>
    private static void PlayTurns(SGame game, int turns, Random rng)
    {
        for (int t = 0; t < turns && game.State != State.COMPLETE; t++)
        {
            for (int k = 0; k < 12 && game.State != State.COMPLETE; k++)
            {
                var c = game.CurrentPlayer;
                if (c.Choice != null) { Assert.True(game.Process(ChooseTask.Pick(c, c.Choice.Choices[rng.Next(c.Choice.Choices.Count)]))); continue; }
                var options = c.Options().Where(o => o is not EndTurnTask).ToList();
                if (options.Count == 0) break;
                game.Process(options[rng.Next(options.Count)]);
            }
            if (game.State == State.COMPLETE) break;
            if (game.CurrentPlayer.Choice != null) continue;
            game.Process(EndTurnTask.Any(game.CurrentPlayer));
        }
    }

    [Theory]
    [InlineData(44)]
    [InlineData(45)]
    [InlineData(46)]
    public void Every_passive_twist_and_treasure_survives_a_few_random_turns(int seed)
    {
        var rng = new Random(seed);
        foreach (string passive in Passives)
            PlayTurns(Game(new[] { passive }.Concat(Filler).Concat(Filler)), 8, rng);
        PlayTurns(Game(Passives.Concat(Filler)), 8, rng); // all at once
        foreach (string twist in new[] { "DALA_829", "DALA_830", "DALA_831", "DALA_832", "DALA_833" })
            PlayTurns(Game(Filler.Concat(Filler), twist), 10, rng);
        foreach (string treasure in Treasures)
        {
            var game = Game(Filler.Concat(Filler));
            Give(game.Player1, treasure);
            Give(game.Player2, treasure);
            OnBoard(game, game.Player2, "CS2_186");
            OnBoard(game, game.Player1, "CS2_124");
            PlayTurns(game, 8, rng);
        }
        string[] powers = { "Druid", "Hunter", "Mage", "Paladin", "Priest", "Rogue", "Shaman", "Warlock", "Warrior" };
        foreach (string cls in powers)
            foreach (int n in new[] { 1, 2 })
            {
                var game = Game(Filler.Concat(Filler));
                foreach (var c in new[] { game.Player1, game.Player2 })
                    new SabberStoneCore.Tasks.SimpleTasks.ReplaceHeroPower(Cards.FromId($"DALA_{cls}_HP{n}")).Process(game, c, c.Hero, null);
                OnBoard(game, game.Player2, "CS2_186");
                PlayTurns(game, 8, rng);
            }
    }

    [Fact]
    public void More_passives_do_what_they_say()
    {
        var game = Game(new[] { "DALA_731", "LOOTA_831", "GILA_506", "DALA_748", "DALA_736", "DALA_745", "DALA_743", "DALA_728", "DALA_707" }.Concat(Filler));
        var me = game.Player1;
        // Dr. Boom's Remote: three Boom Bots; Loyal Henchman: drawn at the start of the game; The Hand of Rafaam: two Cursed!
        Assert.Equal(3, me.BoardZone.Count(m => m.Card.Id == "GVG_110t"));
        Assert.Contains(me.HandZone, p => p.Card.Id == "DALA_707");
        Assert.Equal(2, me.Opponent.HandZone.Count(p => p.Card.Id == "LOE_007t"));
        // Glyph of Warding: enemy minions cost (1) more
        var enemyMinion = me.Opponent.HandZone.First(p => p is Minion);
        Assert.Equal(enemyMinion.Card.Cost + 1, enemyMinion.Cost);
        // Rocket Backpacks: a minion played can attack minions at once
        Play(game, me, "CS2_186");
        var golem = me.BoardZone.Last(m => m.Card.Id == "CS2_186");
        Assert.True(golem.IsRush);
        // Elixir of Vile: spells cost Health
        var bolt = Give(me, "CS2_029");
        int hp = me.Hero.Health;
        me.UsedMana = me.BaseMana; // no Mana left
        Assert.True(game.Process(PlayCardTask.SpellTarget(me, bolt, me.Opponent.Hero)));
        Assert.Equal(hp - 4, me.Hero.Health);
        // Stargazing: the Hero Power twice, (1) less
        me.UsedMana = 0;
        Assert.Equal(me.Hero.HeroPower.Card.Cost - 1, me.Hero.HeroPower.Cost);
        Assert.True(game.Process(HeroPowerTask.Any(me, me.Opponent.Hero)));
        Assert.True(game.Process(HeroPowerTask.Any(me, me.Opponent.Hero)));
        Assert.False(game.Process(HeroPowerTask.Any(me, me.Opponent.Hero)));
        // Recycling: a friendly minion dies → 2 Armor
        int armor = me.Hero.Armor;
        Play(game, me, "CS2_029", me.BoardZone.First(m => m.Card.Id == "GVG_110t")); // (the Boom Bot's Deathrattle hits too)
        Assert.Equal(armor + 2, me.Hero.Armor);
        // First Aid Kit: 2 Health to all friendly characters at the end of the turn
        int before = me.Hero.Health;
        EndTurns(game, 1);
        Assert.Equal(System.Math.Min(before + 2, me.Hero.BaseHealth), me.Hero.Health);
        // Loyal Henchman: +1/+1 each turn in hand (drawn before turn 1: turns 1 and 3)
        EndTurns(game, 1);
        var henchman = (Minion)me.HandZone.First(p => p.Card.Id == "DALA_707");
        Assert.Equal(3, henchman.AttackDamage);
    }

    [Fact]
    public void Togwaggles_dice_moves_costs_but_never_below_zero()
    {
        var game = Game(new[] { "DALA_741" }.Concat(Filler));
        var me = game.Player1;
        for (int i = 0; i < 4; i++) Give(me, "CS2_168");
        EndTurns(game, 1);
        Assert.All(me.HandZone, p => Assert.InRange(p.Cost, 0, p.Card.Cost + 2));
        Assert.All(me.HandZone, p => Assert.True(p.Cost >= p.Card.Cost - 2));
    }

    [Fact]
    public void Some_treasures_do_what_they_say()
    {
        var game = Game();
        var me = game.Player1;
        var foe = OnBoard(game, me.Opponent, "CS2_186");
        // Gnomish Army Knife: seven keywords
        var wolf = OnBoard(game, me, "CS2_124");
        Play(game, me, "DALA_702", wolf);
        Assert.True(wolf.HasTaunt && wolf.HasDivineShield && wolf.HasWindfury && wolf.HasLifeSteal && wolf.Poisonous && wolf.HasStealth && wolf.HasCharge);
        // Banana Split: +2/+2 and two copies
        Play(game, me, "DALA_725", wolf);
        Assert.Equal(3, me.BoardZone.Count(m => m.Card.Id == "CS2_124"));
        Assert.All(me.BoardZone.Where(m => m.Card.Id == "CS2_124"), m => Assert.Equal(5, m.AttackDamage));
        // Sow the Seeds: deck minions +3/+3
        Play(game, me, "DALA_727");
        Assert.All(me.DeckZone.Where(p => p is Minion), p => Assert.Equal(p.Card[GameTag.ATK] + 3, ((Minion)p).AttackDamage));
        // EVIL Propaganda: the enemy minion is mine
        Play(game, me, "DALA_706");
        Assert.Equal(me, foe.Controller);
        // Fly-By: a Kadoom Bot in the opponent's deck
        Play(game, me, "DALA_716");
        Assert.Contains(me.Opponent.DeckZone, p => p.Card.Id == "DALA_716t");
        // Orb of the Untold: 20 damage split between enemies (only the hero left)
        int hp = me.Opponent.Hero.Health;
        Play(game, me, "DALA_712");
        Assert.Equal(System.Math.Max(0, hp - 20), System.Math.Max(0, me.Opponent.Hero.Health));
    }

    [Fact]
    public void The_muscle_and_elixir_of_vigor_make_cheap_cards()
    {
        var game = Game(new[] { "DALA_746" }.Concat(Filler));
        var me = game.Player1;
        int hand = me.HandZone.Count;
        Play(game, me, "DALA_703");
        Assert.Equal(hand + 3, me.HandZone.Count);
        Assert.All(me.HandZone.Skip(hand), p => Assert.Equal(0, p.Cost));
        Assert.Equal(2, me.DeckZone.Count(p => p.Card.Id == "DALA_703" && p.Cost == 1)); // Elixir of Vigor's copies, Costs (1)
    }

    private static void UsePower(SGame game, Controller me, string id, ICharacter? target = null)
    {
        new SabberStoneCore.Tasks.SimpleTasks.ReplaceHeroPower(Cards.FromId(id)).Process(game, me, me.Hero, null);
        me.Hero.HeroPower.IsExhausted = false;
        me.UsedMana = 0;
        Assert.True(game.Process(HeroPowerTask.Any(me, target)), id);
    }

    [Fact]
    public void More_hero_powers_do_what_they_say()
    {
        var game = Game();
        var me = game.Player1;
        var golem = OnBoard(game, me.Opponent, "CS2_186");
        int hp = me.Hero.Health;
        UsePower(game, me, "DALA_Warlock_HP1", golem); // Pain Split
        Assert.Equal(hp - 2, me.Hero.Health);
        Assert.Equal(5, golem.Health);
        UsePower(game, me, "DALA_Warlock_HP2"); // The Pact
        Assert.Contains(me.BoardZone, m => m.Card.Id == "DALA_Warlock_HP2t");
        var rager = OnBoard(game, me, "CS2_118");
        UsePower(game, me, "DALA_Priest_HP1", rager); // Distort: 5/1 → 1/5
        Assert.Equal(1, rager.AttackDamage);
        UsePower(game, me, "DALA_Priest_HP2", golem); // Soothe: -2 Attack until my next turn
        Assert.Equal(5, golem.AttackDamage);
        UsePower(game, me, "DALA_Shaman_HP2"); // Refresh: Overload (1)
        Assert.Equal(1, me.OverloadOwed);
        UsePower(game, me, "DALA_Paladin_HP1"); // Backup
        Assert.Equal(3, me.HandZone.Count(p => p.Card.Id == "CS2_101t"));
        UsePower(game, me, "DALA_Warrior_HP1", golem); // Invigorating Brew: 1 damage, +2 Attack
        Assert.Equal(4, golem.Health);
        Assert.Equal(7, golem.AttackDamage);
        // Cut-less: only an undamaged minion
        new SabberStoneCore.Tasks.SimpleTasks.ReplaceHeroPower(Cards.FromId("DALA_Rogue_HP2")).Process(game, me, me.Hero, null);
        me.Hero.HeroPower.IsExhausted = false;
        me.UsedMana = 0;
        Assert.False(game.Process(HeroPowerTask.Any(me, golem)));
        EndTurns(game, 2);
        Assert.Equal(9, golem.AttackDamage); // Soothe ended at my turn's start (7 + Invigorated 2)
        UsePower(game, me, "DALA_Hunter_HP2"); // Pet Training: the Chameleon shifts each turn
        var chameleon = me.HandZone.Last();
        Assert.Equal("DALA_Hunter_HP2t", chameleon.Card.Id);
        EndTurns(game, 2);
        Assert.NotEqual("DALA_Hunter_HP2t", me.HandZone.First(p => p.Id == chameleon.Id).Card.Id);
        Assert.Equal(1, me.HandZone.First(p => p.Id == chameleon.Id).Card.Cost);
    }

    /// <summary>Soak-style games with the run's pieces: random decks with two passives and three treasures, a Heist Hero Power and a
    /// chapter twist, played to the end by the soak runner — the client view must stay exact (SoakRunner's checks).</summary>
    [Theory]
    [InlineData(7140101)]
    [InlineData(7140202)]
    [InlineData(7140303)]
    [InlineData(7140404)]
    public void Heist_games_keep_the_client_view_exact(int seed)
    {
        _ = Db.Value;
        var rng = new Random(seed);
        string[] twists = { "DALA_829", "DALA_830", "DALA_831", "DALA_832", "DALA_833" };
        string[] classes = { "Druid", "Hunter", "Mage", "Paladin", "Priest", "Rogue", "Shaman", "Warlock", "Warrior" };
        var c1 = RandomDecks.Classes[rng.Next(RandomDecks.Classes.Length)];
        var deck = RandomDecks.BuildCurrentCore(c1, rng).Take(20).ToList();
        deck.InsertRange(0, Passives.OrderBy(_ => rng.Next()).Take(2));
        deck.AddRange(Treasures.OrderBy(_ => rng.Next()).Take(3));
        var c2 = RandomDecks.Classes[rng.Next(RandomDecks.Classes.Length)];
        var engine = new SabberEngineGame(new EngineSetup
        {
            Cards = Db.Value, Scenario = 260, Seed = rng.Next(), Rules = RulesEngine.Sabber,
            GameSetupBody = PegasusPackets.Serialize(new GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
            Human = new PlayerSetup
            {
                HeroCardId = RandomDecks.AnyHero(c1, rng), Deck = deck, AccountHi = 1, AccountLo = 2, StartHealth = 30,
                HeroPowerCardId = $"DALA_{classes[rng.Next(classes.Length)]}_HP{1 + rng.Next(2)}", PlotTwistCardId = twists[rng.Next(twists.Length)],
            },
            Opponent = new PlayerSetup { HeroCardId = RandomDecks.AnyHero(c2, rng), Deck = RandomDecks.BuildCurrentCore(c2, rng), IsAi = true, StartHealth = 40 },
        });
        var result = new SoakRunner(rng).Play(engine);
        Assert.True(result.Problems.Count == 0, string.Join(" | ", result.Problems.Take(5)));
    }
}
