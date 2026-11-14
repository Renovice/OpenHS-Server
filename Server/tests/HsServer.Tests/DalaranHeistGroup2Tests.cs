using HsServer.Engine;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;
using PegasusGame;
using Entity = SabberStoneCore.Model.Entities.Entity;

namespace HsServer.Tests;

/// <summary>The Dalaran Heist, bosses DALA_BOSS_26..50 (CardSets/HsServer/DalaranHeistCardsGenHs2.cs, Actions/DalaranHeistGroup2.cs):
/// passive boss Hero Powers working from the game's start, the Underbelly Rats, the Void, the counters.</summary>
public class DalaranHeistGroup2Tests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly Lazy<CardDb> Db = new(() =>
    {
        string dir = CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
        SabberEngineGame.EnsureCards(dir);
        return CardDb.Load(dir);
    });

    /// <summary>A game where player 2 (the boss) starts with <paramref name="bossPower"/>; player 1 goes first, both at 10 Mana.</summary>
    private static SabberStoneCore.Model.Game BossGame(string bossPower, int startPlayer = 1)
    {
        _ = Db.Value;
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = startPlayer, Player1HeroClass = CardClass.DRUID, Player2HeroClass = CardClass.MAGE,
            Player2HeroPowerCard = Cards.FromId(bossPower),
            FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false,
        });
        game.StartGame();
        game.Player1.BaseMana = 10;
        game.Player2.BaseMana = 10;
        return game;
    }

    private static IPlayable Give(Controller c, string id) => Generic.DrawCard(c, Cards.FromId(id));

    private static Minion OnBoard(SabberStoneCore.Model.Game game, Controller c, string id)
    {
        var m = (Minion)Entity.FromCard(c, Cards.FromId(id));
        Generic.SummonBlock.Invoke(game, m, -1, null);
        m[GameTag.NUM_TURNS_IN_PLAY] = 1;
        m.IsExhausted = false;
        return m;
    }

    private static void EndTurn(SabberStoneCore.Model.Game game)
    {
        game.CurrentPlayer.UsedMana = 0;
        Assert.True(game.Process(EndTurnTask.Any(game.CurrentPlayer)));
    }

    [Fact]
    public void Every_group_2_card_is_scripted()
    {
        _ = Db.Value;
        string[] ids =
        {
            "DALA_BOSS_26p", "DALA_BOSS_26px", "DALA_BOSS_27p", "DALA_BOSS_28p", "DALA_BOSS_28e", "DALA_BOSS_29p", "DALA_BOSS_30p", "DALA_BOSS_30t",
            "DALA_BOSS_31p", "DALA_BOSS_31px", "DALA_BOSS_31e", "DALA_BOSS_31ex", "DALA_BOSS_32p", "DALA_BOSS_32e1", "DALA_BOSS_32e2", "DALA_BOSS_32e3",
            "DALA_BOSS_32e4", "DALA_BOSS_33p", "DALA_BOSS_34p", "DALA_BOSS_35p", "DALA_BOSS_35px", "DALA_BOSS_36p", "DALA_BOSS_36px", "DALA_BOSS_37p",
            "DALA_BOSS_37px", "DALA_BOSS_38p", "DALA_BOSS_38px", "DALA_BOSS_38t", "DALA_BOSS_39p", "DALA_BOSS_39e", "DALA_BOSS_40p", "DALA_BOSS_40px",
            "DALA_BOSS_41p", "DALA_BOSS_41px", "DALA_BOSS_42p", "DALA_BOSS_42px", "DALA_BOSS_43p", "DALA_BOSS_44p", "DALA_BOSS_44px", "DALA_BOSS_44e",
            "DALA_BOSS_44ex", "DALA_BOSS_45p", "DALA_BOSS_45px", "DALA_BOSS_46p", "DALA_BOSS_46px", "DALA_BOSS_47p", "DALA_BOSS_47px", "DALA_BOSS_47t",
            "DALA_BOSS_48p", "DALA_BOSS_48t", "DALA_BOSS_49p", "DALA_BOSS_49px", "DALA_BOSS_50p", "DALA_BOSS_50px", "GILA_500p2t",
        };
        foreach (string id in ids)
        {
            Card card = Cards.FromId(id);
            Assert.True(card.Power != null && (card.Power.PowerTask != null || card.Power.Trigger != null || card.Power.Aura != null || card.Power.Enchant != null), id);
        }
    }

    [Fact]
    public void Cold_water_freezes_only_the_first_minion_that_attacks_the_boss_each_turn()
    {
        var game = BossGame("DALA_BOSS_26p");
        var me = game.Player1;
        var a = OnBoard(game, me, "CS2_182"); // Chillwind Yeti 4/5
        var b = OnBoard(game, me, "CS2_182");
        Assert.True(game.Process(MinionAttackTask.Any(me, a, me.Opponent.Hero)));
        Assert.True(a.IsFrozen);
        Assert.True(game.Process(MinionAttackTask.Any(me, b, me.Opponent.Hero)));
        Assert.False(b.IsFrozen);
        Assert.Equal(1, me.Opponent.Hero.HeroPower[GameTag.TAG_SCRIPT_DATA_NUM_1]);
        EndTurn(game); // the boss's turn starts: the count goes back to 0
        Assert.Equal(0, me.Opponent.Hero.HeroPower[GameTag.TAG_SCRIPT_DATA_NUM_1]);
    }

    [Fact]
    public void Heroic_cold_water_and_immolation_aura_hit_every_attacker_of_the_hero_but_not_attacks_on_minions()
    {
        var game = BossGame("DALA_BOSS_26px");
        var me = game.Player1;
        var a = OnBoard(game, me, "CS2_182");
        var b = OnBoard(game, me, "CS2_182");
        var wall = OnBoard(game, me.Opponent, "CS2_182");
        Assert.True(game.Process(MinionAttackTask.Any(me, a, me.Opponent.Hero)));
        Assert.True(game.Process(MinionAttackTask.Any(me, b, wall)));
        Assert.True(a.IsFrozen);
        Assert.False(b.IsFrozen);

        game = BossGame("DALA_BOSS_40px");
        me = game.Player1;
        a = OnBoard(game, me, "CS2_182");
        Assert.True(game.Process(MinionAttackTask.Any(me, a, me.Opponent.Hero)));
        Assert.Equal(2, a.Health); // 5 - 3
    }

    [Fact]
    public void Made_of_coins_adds_a_coin_per_damage_taken()
    {
        var game = BossGame("DALA_BOSS_33p");
        var me = game.Player1;
        var boss = me.Opponent;
        int hand = boss.HandZone.Count;
        var a = OnBoard(game, me, "CS2_182");
        Assert.True(game.Process(MinionAttackTask.Any(me, a, boss.Hero)));
        Assert.Equal(hand + 1, boss.HandZone.Count);
        Assert.Equal("GAME_005", boss.HandZone[boss.HandZone.Count - 1].Card.Id);
    }

    [Fact]
    public void Merry_go_round_turns_the_minions_like_a_carousel_at_the_end_of_the_boss_turn()
    {
        var game = BossGame("DALA_BOSS_34p", startPlayer: 2);
        var boss = game.Player2;
        var me = game.Player1;
        var b1 = OnBoard(game, boss, "CS2_182");
        var b2 = OnBoard(game, boss, "CS2_120");
        var p1 = OnBoard(game, me, "CS2_172");
        var p2 = OnBoard(game, me, "CS2_168");
        EndTurn(game);
        // his left-most goes to the player's left end, the player's right-most comes to his right end (live 183021 16749)
        Assert.Equal(new[] { b2.Id, p2.Id }, boss.BoardZone.Select(m => m.Id).ToArray());
        Assert.Equal(new[] { b1.Id, p1.Id }, me.BoardZone.Select(m => m.Id).ToArray());
    }

    [Fact]
    public void Healing_hands_heals_the_boss_minions_at_the_start_of_its_turn()
    {
        var game = BossGame("DALA_BOSS_35px");
        var boss = game.Player2;
        var yeti = OnBoard(game, boss, "CS2_182");
        yeti.Damage = 4;
        EndTurn(game);
        Assert.Equal(5, yeti.Health);
    }

    [Fact]
    public void The_eldest_underbelly_rat_replaces_the_dead_rat_king()
    {
        var game = BossGame("DALA_BOSS_30p");
        var me = game.Player1;
        var boss = game.Player2;
        var eldest = OnBoard(game, boss, "DALA_BOSS_30t");
        var younger = OnBoard(game, boss, "DALA_BOSS_30t");
        younger.Damage = 0;
        boss.Hero.Damage = boss.Hero.BaseHealth - 2;
        var a = OnBoard(game, me, "CS2_182");
        Assert.True(game.Process(MinionAttackTask.Any(me, a, boss.Hero)));
        Assert.Equal(State.RUNNING, game.State);
        Assert.Equal(PlayState.PLAYING, boss.PlayState);
        Assert.Equal(1, boss.Hero.Health); // the rat's Health
        Assert.DoesNotContain(eldest, boss.BoardZone);
        Assert.Contains(younger, boss.BoardZone);

        // the second death takes the next rat; with none left the boss loses
        Assert.True(game.Process(MinionAttackTask.Any(me, OnBoard(game, me, "CS2_182"), boss.Hero)));
        Assert.Equal(PlayState.PLAYING, boss.PlayState);
        Assert.Empty(boss.BoardZone);
        Assert.True(game.Process(MinionAttackTask.Any(me, OnBoard(game, me, "CS2_182"), boss.Hero)));
        Assert.NotEqual(PlayState.PLAYING, boss.PlayState);
    }

    [Fact]
    public void To_the_void_banishes_without_deathrattle_and_void_shift_brings_them_back_on_their_side()
    {
        var game = BossGame("DALA_BOSS_48p", startPlayer: 2);
        var boss = game.Player2;
        var me = game.Player1;
        var loot = OnBoard(game, me, "LOOT_998k"); // any minion
        var harvest = OnBoard(game, me, "FP1_001"); // Zombie Chow: Deathrattle heals the enemy hero
        boss.Hero.Damage = 10;
        Assert.True(game.Process(HeroPowerTask.Any(boss, harvest)));
        Assert.DoesNotContain(harvest, me.BoardZone);
        Assert.Equal(20, boss.Hero.Health); // no Deathrattle
        Assert.Single(Generic.InTheVoid(game));

        var shift = Give(me, "DALA_BOSS_48t");
        EndTurn(game);
        Assert.True(game.Process(PlayCardTask.Spell(me, shift)));
        Assert.DoesNotContain(loot, me.BoardZone);
        Assert.Single(me.BoardZone);
        Assert.Equal("FP1_001", me.BoardZone[0].Card.Id);
        Assert.Empty(Generic.InTheVoid(game));
    }

    [Fact]
    public void Kaironomaly_limits_only_the_opponents_turn_time()
    {
        var game = BossGame("DALA_BOSS_43p");
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(30, game.Player1.TurnLimitSeconds);
        Assert.Null(game.Player2.TurnLimitSeconds);
    }

    [Fact]
    public void Ray_of_suffering_grows_with_each_use()
    {
        var game = BossGame("DALA_BOSS_45p", startPlayer: 2);
        var boss = game.Player2;
        int health = game.Player1.Hero.Health;
        Assert.True(game.Process(HeroPowerTask.Any(boss)));
        Assert.Equal(health - 1, game.Player1.Hero.Health);
        EndTurn(game);
        EndTurn(game);
        Assert.True(game.Process(HeroPowerTask.Any(boss)));
        Assert.Equal(health - 3, game.Player1.Hero.Health);
    }

    [Fact]
    public void Dark_tidings_buffs_a_deck_minion_and_puts_it_on_top()
    {
        var game = BossGame("DALA_BOSS_44p", startPlayer: 2);
        var boss = game.Player2;
        Assert.True(game.Process(HeroPowerTask.Any(boss)));
        IPlayable top = boss.DeckZone.TopCard;
        Assert.IsType<Minion>(top);
        Assert.Contains(top.AppliedEnchantments, e => e.Card.Id == "DALA_BOSS_44e");
    }

    [Fact]
    public void Bling_it_on_makes_a_minion_golden_then_buffs_it()
    {
        var game = BossGame("DALA_BOSS_31p", startPlayer: 2);
        var boss = game.Player2;
        var yeti = OnBoard(game, boss, "CS2_182");
        Assert.True(game.Process(HeroPowerTask.Any(boss, yeti)));
        Assert.Equal(1, yeti[GameTag.PREMIUM]);
        Assert.Equal(4, yeti.AttackDamage);
        EndTurn(game);
        EndTurn(game);
        Assert.True(game.Process(HeroPowerTask.Any(boss, yeti)));
        Assert.Equal(8, yeti.AttackDamage);
        Assert.Equal(9, yeti.Health);
    }

    [Fact]
    public void Scroll_savvy_makes_drawn_spells_free_that_turn()
    {
        var game = BossGame("DALA_BOSS_28p", startPlayer: 2);
        var boss = game.Player2;
        // a spell put on top of the deck, then drawn
        var spell = Entity.FromCard(boss, Cards.FromId("CS2_029"));
        boss.DeckZone.Add(spell);
        Generic.Draw(boss, spell);
        Assert.Equal(0, spell.Cost);
        EndTurn(game);
        Assert.Equal(4, spell.Cost);
    }

    [Fact]
    public void Excavate_masterpiece_and_the_card_givers_run_on_many_seeds()
    {
        foreach (string power in new[] { "DALA_BOSS_29p", "DALA_BOSS_27p", "DALA_BOSS_37px", "DALA_BOSS_38p", "DALA_BOSS_32p", "DALA_BOSS_42px", "DALA_BOSS_41px", "DALA_BOSS_50px", "DALA_BOSS_46px", "DALA_BOSS_39p", "DALA_BOSS_47p" })
            for (int i = 0; i < 12; i++)
            {
                var game = BossGame(power, startPlayer: 2);
                var boss = game.Player2;
                var elemental = OnBoard(game, boss, "UNG_928"); // Tar Creeper (Elemental)
                OnBoard(game, game.Player1, "CS2_182");
                for (int k = 0; k < i % 4; k++) Generic.Draw(boss);
                bool target = power is "DALA_BOSS_29p" or "DALA_BOSS_32p" or "DALA_BOSS_42px";
                int mana = boss.RemainingMana;
                Assert.True(game.Process(HeroPowerTask.Any(boss, target ? elemental : null)), power);
                if (power == "DALA_BOSS_27p")
                {
                    Assert.Equal(0, boss.RemainingMana);
                    Assert.Contains(boss.BoardZone, m => m != elemental && m.Card.Cost == mana);
                }
            }
    }
    /// <summary>The TRIGGER blocks a Hero Power sends (what player 1 receives), from <paramref name="from"/> on.</summary>
    private List<PowerHistoryData> Blocks(SabberStoneCore.Model.Game game, int source, int from = 0)
    {
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full).ToList();
        var starts = new List<PowerHistoryData>();
        for (int i = from; i < sent.Count; i++)
            if (sent[i].PowerStart is { } b && b.Source == source)
            {
                starts.Add(sent[i]);
                int depth = 0, j = i;
                var block = new List<PowerHistoryData>();
                for (; j < sent.Count; j++)
                {
                    block.Add(sent[j]);
                    if (sent[j].PowerStart != null) depth++;
                    if (sent[j].PowerEnd != null && --depth == 0) break;
                }
                output.WriteLine(HistoryText.Format(block, id => id.ToString(), withTags: false));
            }
        return starts;
    }

    [Fact]
    public void Passive_boss_powers_send_their_live_block_shapes()
    {
        // Cold Water: the freeze in a TRIGGER block at index 0 shown in the history, the turn-start reset at index 1 (live 222041 40608 / 41139)
        var game = BossGame("DALA_BOSS_26p");
        var me = game.Player1;
        int hp = me.Opponent.Hero.HeroPower.Id;
        Assert.True(game.Process(MinionAttackTask.Any(me, OnBoard(game, me, "CS2_182"), me.Opponent.Hero)));
        EndTurn(game);
        var cold = Blocks(game, hp);
        Assert.Contains(cold, d => d.PowerStart.EffectIndex == 0 && d.PowerStart.ShowInHistory);
        Assert.Contains(cold, d => d.PowerStart.EffectIndex == 1 && !d.PowerStart.ShowInHistory);

        // Merry Go Round: TRIGGER_VISUAL, not shown in the history (live 183021 15875)
        game = BossGame("DALA_BOSS_34p", startPlayer: 2);
        OnBoard(game, game.Player2, "CS2_182");
        OnBoard(game, game.Player1, "CS2_182");
        EndTurn(game);
        var merry = Blocks(game, game.Player2.Hero.HeroPower.Id);
        Assert.Contains(merry, d => d.PowerStart.TriggerKeyword == (int)GameTag.TRIGGER_VISUAL && !d.PowerStart.ShowInHistory);

        // Made of Coins: shown in the history, index 0 (live 183021 40403)
        game = BossGame("DALA_BOSS_33p");
        me = game.Player1;
        Assert.True(game.Process(MinionAttackTask.Any(me, OnBoard(game, me, "CS2_182"), me.Opponent.Hero)));
        Assert.Contains(Blocks(game, me.Opponent.Hero.HeroPower.Id), d => d.PowerStart.EffectIndex == 0 && d.PowerStart.ShowInHistory);

        // Healing Hands: TRIGGER_VISUAL, shown in the history (live 224614 78852)
        game = BossGame("DALA_BOSS_35p");
        OnBoard(game, game.Player2, "CS2_182").Damage = 3;
        EndTurn(game);
        Assert.Contains(Blocks(game, game.Player2.Hero.HeroPower.Id), d => d.PowerStart.TriggerKeyword == (int)GameTag.TRIGGER_VISUAL && d.PowerStart.ShowInHistory);
    }
    [Fact]
    public void Every_group_2_boss_plays_whole_games_with_its_hero_powers()
    {
        // a soak per boss Hero Power (Normal and Heroic): the boss (the AI) with its hero and power, the scripted client as player 1
        var cards = Db.Value;
        var problems = new List<string>();
        for (int boss = 26; boss <= 50; boss++)
            foreach (string power in new[] { $"DALA_BOSS_{boss}p", $"DALA_BOSS_{boss}px" })
            {
                if (SabberStoneCore.Model.Cards.TryFromId(power) == null) continue;
                for (int seed = 1; seed <= 2; seed++)
                {
                    var rng = new Random(boss * 100 + seed);
                    var engine = new SabberEngineGame(new HsServer.Game.EngineSetup
                    {
                        Cards = cards, Scenario = 3332, Seed = boss * 100 + seed, Rules = HsServer.Game.RulesEngine.Sabber,
                        GameSetupBody = HsServer.Protocol.PegasusPackets.Serialize(new GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
                        Human = new HsServer.Engine.PlayerSetup { HeroCardId = "HERO_08", Deck = RandomDecks.Build(CardClass.MAGE, rng, CardSet.CORE, CardSet.EXPERT1), AccountHi = 1, AccountLo = 2 },
                        Opponent = new HsServer.Engine.PlayerSetup
                        {
                            HeroCardId = $"DALA_BOSS_{boss}h", HeroPowerCardId = power, IsAi = true,
                            Deck = RandomDecks.Build(CardClass.SHAMAN, rng, CardSet.CORE, CardSet.EXPERT1),
                        },
                    });
                    var result = new SoakRunner(rng) { Trace = (_, _) => { } }.Play(engine);
                    problems.AddRange(result.Problems.Select(x => $"{power} seed {seed}: {x}"));
                    Assert.Equal(power, engine.Rules.Player2.Hero.HeroPower.Card.Id);
                    output.WriteLine($"{power} seed {seed}: {result.Turns} turns, used {engine.Rules.Player2.NumTimesHeroPowerUsedThisGame}x, " +
                                     $"boss {engine.Rules.Player2.PlayState}");
                }
            }
        foreach (string x in problems.Take(20)) output.WriteLine(x);
        Assert.Empty(problems);
    }
}
