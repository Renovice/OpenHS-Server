using HsServer.Engine;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsServer.Tests;

/// <summary>The Dalaran Heist bosses DALA_BOSS_01..25 (CardSets/HsServer/DalaranHeistCardsGenHs1.cs): their Hero Powers and cards,
/// a boss Hero Power given at the game's start (GameConfig.PlayerNHeroPowerCard) as a Heist game does.</summary>
public class DalaranHeistGroup1Tests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly Lazy<CardDb> Db = new(() =>
    {
        string dir = CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
        SabberEngineGame.EnsureCards(dir);
        return CardDb.Load(dir);
    });

    /// <summary>A started game at 10 Mana; <paramref name="p1Power"/> / <paramref name="p2Power"/>: the players' starting Hero Powers.</summary>
    private static SabberStoneCore.Model.Game Game(string? p1Power = null, string? p2Power = null)
    {
        _ = Db.Value;
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.DRUID, Player2HeroClass = CardClass.MAGE,
            Player1HeroPowerCard = p1Power == null ? null : SabberStoneCore.Model.Cards.FromId(p1Power),
            Player2HeroPowerCard = p2Power == null ? null : SabberStoneCore.Model.Cards.FromId(p2Power),
            FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false,
        });
        game.StartGame();
        game.Player1.BaseMana = 10;
        game.Player2.BaseMana = 10;
        return game;
    }

    private static IPlayable Give(Controller c, string id) => Generic.DrawCard(c, SabberStoneCore.Model.Cards.FromId(id));

    private static void Play(SabberStoneCore.Model.Game game, Controller me, string id, ICharacter? target = null)
    {
        me.UsedMana = 0;
        var card = Give(me, id);
        var task = card is Minion
            ? (target == null ? PlayCardTask.Minion(me, card) : PlayCardTask.MinionTarget(me, card, target))
            : (target == null ? PlayCardTask.Spell(me, card) : PlayCardTask.SpellTarget(me, card, target));
        Assert.True(game.Process(task), id);
    }

    private static void UsePower(SabberStoneCore.Model.Game game, Controller me, ICharacter? target = null)
    {
        me.UsedMana = 0;
        Assert.True(game.Process(HeroPowerTask.Any(me, target)), me.Hero.HeroPower.Card.Id);
    }

    private static Minion OnBoard(SabberStoneCore.Model.Game game, Controller c, string id)
    {
        var m = (Minion)SabberStoneCore.Model.Entities.Entity.FromCard(c, SabberStoneCore.Model.Cards.FromId(id));
        Generic.SummonBlock.Invoke(game, m, -1, null);
        m[GameTag.NUM_TURNS_IN_PLAY] = 1;
        m.IsExhausted = false;
        return m;
    }

    private static void PassTurns(SabberStoneCore.Model.Game game, int n)
    {
        for (int i = 0; i < n; i++)
        {
            game.CurrentPlayer.UsedMana = 0;
            Assert.True(game.Process(EndTurnTask.Any(game.CurrentPlayer)));
        }
    }

    [Theory]
    [InlineData("DALA_BOSS_20p", 2)]
    [InlineData("DALA_BOSS_20px", 3)]
    public void Stormswell_casts_overload_spells_again_from_the_games_start(string power, int casts)
    {
        // Lightning Bolt: Deal 3 damage. Overload: (1) — each cast overloads (live Stormswell thrice: OVERLOAD_OWED 1, 2, 3)
        var game = Game(power);
        var me = game.CurrentPlayer;
        Play(game, me, "EX1_238", me.Opponent.Hero);
        Assert.Equal(30 - 3 * casts, me.Opponent.Hero.Health);
        Assert.Equal(casts, me.OverloadOwed);
        // a spell without Overload casts once
        Play(game, me, "CS2_029", me.Opponent.Hero); // Fireball
        Assert.Equal(30 - 3 * casts - 6, me.Opponent.Hero.Health);
    }

    [Fact]
    public void The_feast_heals_the_boss_after_each_spell()
    {
        var game = Game("DALA_BOSS_02p");
        var me = game.CurrentPlayer;
        me.Hero.Damage = 10;
        Play(game, me, "CS2_029", me.Opponent.Hero);
        Assert.Equal(23, me.Hero.Health);
    }

    [Fact]
    public void Blessing_of_elune_doubles_both_players_healing()
    {
        var game = Game("DALA_BOSS_15p");
        var me = game.CurrentPlayer;
        Assert.Equal(1, me.ControllerAuraEffects[GameTag.ALL_HEALING_DOUBLE]);
        Assert.Equal(1, me.Opponent.ControllerAuraEffects[GameTag.ALL_HEALING_DOUBLE]);
        me.Opponent.Hero.Damage = 10;
        me.Opponent.Hero.TakeHeal(me.Opponent.Hero, 3);
        Assert.Equal(26, me.Opponent.Hero.Health);
    }

    [Fact]
    public void Lil_eviscerate_deals_two_with_combo()
    {
        var game = Game("DALA_BOSS_19p");
        var me = game.CurrentPlayer;
        UsePower(game, me, me.Opponent.Hero);
        Assert.Equal(29, me.Opponent.Hero.Health);

        game = Game("DALA_BOSS_19px");
        me = game.CurrentPlayer;
        Play(game, me, "GAME_005"); // a card first: Combo
        UsePower(game, me, me.Opponent.Hero);
        Assert.Equal(28, me.Opponent.Hero.Health);
    }

    [Fact]
    public void Belnaara_takes_flight_after_15_damage_and_lands_two_turns_later()
    {
        var game = Game(p2Power: "DALA_BOSS_07p");
        var me = game.CurrentPlayer;
        var boss = me.Opponent;
        Play(game, me, "CS2_029", boss.Hero);
        Play(game, me, "CS2_029", boss.Hero);
        Assert.Equal(3, boss.Hero.HeroPower[GameTag.TAG_SCRIPT_DATA_NUM_1]); // (@ damage remaining)
        Play(game, me, "CS2_029", boss.Hero);
        Assert.Equal("DALA_BOSS_07p2", boss.Hero.HeroPower.Card.Id);
        Assert.True(boss.Hero.IsImmune);
        int health = boss.Hero.Health;
        Play(game, me, "DS1_233"); // Mind Blast: 5 damage to the enemy hero — Immune
        Assert.Equal(health, boss.Hero.Health);

        PassTurns(game, 1); // the boss's first turn: 1 left
        Assert.Equal(1, boss.Hero.HeroPower[GameTag.TAG_SCRIPT_DATA_NUM_1]);
        Assert.True(boss.Hero.IsImmune);
        PassTurns(game, 2); // the boss's second turn: lands
        Assert.Equal("DALA_BOSS_07p", boss.Hero.HeroPower.Card.Id);
        Assert.False(boss.Hero.IsImmune);
        Assert.Equal(15, boss.Hero.HeroPower[GameTag.TAG_SCRIPT_DATA_NUM_1]);
    }

    [Fact]
    public void Street_smarts_draws_for_the_boss_when_a_coin_is_played()
    {
        // Heroic: any player's Coin, a Counterfeit Coin too (live: Mo drew on the player's CFM_630 and DMF_COIN1)
        var game = Game(p2Power: "DALA_BOSS_10px");
        var me = game.CurrentPlayer;
        int hand = me.Opponent.HandZone.Count;
        Play(game, me, "GAME_005");
        Assert.Equal(hand + 1, me.Opponent.HandZone.Count);
        Play(game, me, "CFM_630");
        Assert.Equal(hand + 2, me.Opponent.HandZone.Count);
        Play(game, me, "CS2_029", me.Opponent.Hero); // not a Coin
        Assert.Equal(hand + 2, me.Opponent.HandZone.Count);

        // Normal: only the boss's own Coins
        game = Game(p2Power: "DALA_BOSS_10p");
        me = game.CurrentPlayer;
        hand = me.Opponent.HandZone.Count;
        Play(game, me, "GAME_005");
        Assert.Equal(hand, me.Opponent.HandZone.Count);
    }

    [Fact]
    public void Repeat_history_summons_a_new_copy_of_a_minion_that_died_this_turn()
    {
        var game = Game("DALA_BOSS_09p");
        var me = game.CurrentPlayer;
        Assert.False(me.Hero.HeroPower.IsPlayable); // nothing died this turn
        var yeti = OnBoard(game, me, "CS2_182");
        Generic.Destroy(yeti, null);
        game.DeathProcessingAndAuraUpdate();
        Assert.Empty(me.BoardZone);
        Assert.True(me.Hero.HeroPower.IsPlayable);
        UsePower(game, me);
        var back = Assert.Single(me.BoardZone.GetAll());
        Assert.Equal("CS2_182", back.Card.Id);
        Assert.NotEqual(yeti.Id, back.Id);
    }

    [Fact]
    public void Equivalent_exchange_swaps_one_card_each_way()
    {
        var game = Game("DALA_BOSS_16p");
        var me = game.CurrentPlayer;
        var mine = me.HandZone.GetAll().Select(p => p.Id).ToHashSet();
        var theirs = me.Opponent.HandZone.GetAll().Select(p => p.Id).ToHashSet();
        int myCount = mine.Count, theirCount = theirs.Count;
        UsePower(game, me);
        Assert.Equal(myCount, me.HandZone.Count);
        Assert.Equal(theirCount, me.Opponent.HandZone.Count);
        var got = Assert.Single(me.HandZone.GetAll(), p => theirs.Contains(p.Id));
        var gave = Assert.Single(me.Opponent.HandZone.GetAll(), p => mine.Contains(p.Id));
        Assert.Equal(me, got.Controller);
        Assert.Equal(me.Opponent, gave.Controller);
        Assert.Contains(got.AppliedEnchantments, e => e.Card.Id == "DALA_BOSS_16e");
    }

    [Fact]
    public void Apple_toss_repeats_while_the_lowest_health_enemy_dies()
    {
        var game = Game("DALA_BOSS_08p");
        var me = game.CurrentPlayer;
        OnBoard(game, me.Opponent, "CS2_231"); // Wisp 1/1
        OnBoard(game, me.Opponent, "CS2_231");
        var yeti = OnBoard(game, me.Opponent, "CS2_182"); // 4/5
        UsePower(game, me);
        Assert.Single(me.Opponent.BoardZone.GetAll());
        Assert.Equal(4, yeti.Health); // two Wisps died, then the Yeti (lowest left) took 1 and lived
        Assert.Equal(30, me.Opponent.Hero.Health);
    }

    [Fact]
    public void Open_wormhole_summons_from_both_decks_and_they_fight()
    {
        var game = Game("DALA_BOSS_04p");
        var me = game.CurrentPlayer;
        int myDeck = me.DeckZone.Count, theirDeck = me.Opponent.DeckZone.Count;
        UsePower(game, me);
        Assert.Equal(myDeck - 1, me.DeckZone.Count);
        Assert.Equal(theirDeck - 1, me.Opponent.DeckZone.Count);
        var fought = me.BoardZone.GetAll().Concat(me.Opponent.BoardZone.GetAll()).ToList();
        // the boss's minion attacked: both took damage, or one / both died
        Assert.True(fought.Count < 2 || fought.All(m => m.Damage > 0) || fought.Any(m => m.Card.ATK == 0));
    }

    [Fact]
    public void Arhas_call_doubles_the_next_battlecry_only()
    {
        var game = Game("DALA_BOSS_24p");
        var me = game.CurrentPlayer;
        UsePower(game, me);
        Play(game, me, "CS2_189", me.Opponent.Hero); // Elven Archer: Battlecry: Deal 1 damage
        Assert.Equal(28, me.Opponent.Hero.Health);
        Assert.DoesNotContain(me.AppliedEnchantments ?? new List<Enchantment>(), e => e.Card.Id == "DALA_BOSS_24e");
        Play(game, me, "CS2_189", me.Opponent.Hero);
        Assert.Equal(27, me.Opponent.Hero.Health);
    }

    [Fact]
    public void Assembly_summons_a_toy_copy_of_a_drawn_minion_for_the_drawer()
    {
        // the player's deck: Chillwind Yetis only; the boss's Assembly (Normal: any player) copies the one drawn at their turn's start
        _ = Db.Value;
        var yeti = SabberStoneCore.Model.Cards.FromId("CS2_182");
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.DRUID, Player2HeroClass = CardClass.MAGE,
            Player2HeroPowerCard = SabberStoneCore.Model.Cards.FromId("DALA_BOSS_25p"),
            Player1Deck = Enumerable.Repeat(yeti, 30).ToList(), Player2Deck = Enumerable.Repeat(yeti, 30).ToList(),
            FillDecks = false, History = true, Shuffle = false,
        });
        game.StartGame();
        var me = game.CurrentPlayer.Opponent;
        Assert.Empty(me.BoardZone.GetAll());
        PassTurns(game, 1); // the player draws a Yeti
        var toy = Assert.Single(me.BoardZone.GetAll());
        Assert.Equal("CS2_182", toy.Card.Id);
        Assert.Equal(1, toy.AttackDamage);
        Assert.Equal(1, toy.Health);
    }

    [Fact]
    public void Mana_echoes_casts_a_spell_cast_this_game()
    {
        var game = Game("DALA_BOSS_05p");
        var me = game.CurrentPlayer;
        Play(game, me, "CS2_029", me.Opponent.Hero);
        UsePower(game, me);
        Assert.Equal(2, me.GraveyardZone.GetAll().Count(p => p.Card.Id == "CS2_029"));
    }

    [Fact]
    public void Backstabber_buffs_stealthed_minions_when_summoned()
    {
        var game = Game(p2Power: "DALA_BOSS_11px");
        var me = game.CurrentPlayer;
        Play(game, me, "EX1_010"); // Worgen Infiltrator 2/1 Stealth
        Assert.Equal(4, me.BoardZone[0].AttackDamage);
        Play(game, me, "CS2_182"); // no Stealth
        Assert.Equal(4, me.BoardZone[1].AttackDamage);
    }

    [Fact]
    public void Kind_waitress_sometimes_sends_the_attack_elsewhere()
    {
        int redirected = 0, straight = 0;
        for (int i = 0; i < 40 && (redirected == 0 || straight == 0); i++)
        {
            var game = Game();
            var me = game.CurrentPlayer;
            var waitress = OnBoard(game, me.Opponent, "DALA_BOSS_14t");
            var raptor = OnBoard(game, me, "CS2_172"); // 3/2
            int heroHealth = me.Opponent.Hero.Health;
            Assert.True(game.Process(MinionAttackTask.Any(me, raptor, waitress)));
            if (waitress.Damage > 0 || waitress.IsDead) straight++;
            else if (me.Opponent.Hero.Health < heroHealth) redirected++;
        }
        Assert.True(redirected > 0 && straight > 0, $"redirected {redirected}, straight {straight}");
    }

    /// <summary>Every boss 01..25 (Normal and Heroic Hero Power) plays whole AI games without a problem the client would see.</summary>
    [Fact]
    public void Every_group_one_boss_plays_whole_games_cleanly()
    {
        var cards = Db.Value;
        var failures = new List<string>();
        for (int n = 1; n <= 25; n++)
        {
            string hero = $"DALA_BOSS_{n:00}h";
            foreach (string power in new[] { $"DALA_BOSS_{n:00}p", $"DALA_BOSS_{n:00}px" })
            {
                if (!cards.TryGet(power, out _)) continue;
                var rng = new Random(n * 100 + power.Length);
                var engine = new SabberEngineGame(new HsServer.Game.EngineSetup
                {
                    Cards = cards, Scenario = 260, Seed = rng.Next(), Rules = HsServer.Game.RulesEngine.Sabber,
                    GameSetupBody = HsServer.Protocol.PegasusPackets.Serialize(new PegasusGame.GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
                    Human = new PlayerSetup { HeroCardId = RandomDecks.AnyHero(CardClass.MAGE, rng), Deck = RandomDecks.BuildWild(CardClass.MAGE, rng), AccountHi = 1, AccountLo = 2 },
                    Opponent = new PlayerSetup { HeroCardId = hero, HeroPowerCardId = power, Deck = RandomDecks.BuildWild(CardClass.SHAMAN, rng), IsAi = true },
                });
                var result = new SoakRunner(rng) { Trace = (_, _) => { } }.Play(engine);
                // (a hero card from the deck may replace the Hero Power later: it only has to have been in the game)
                if (!engine.Rules.IdEntityDic.Values.Any(e => e.Card.Id == power)) failures.Add($"{power}: the boss never had it");
                else if (result.Problems.Count > 0) failures.Add($"{power}: {result.Problems[0]}");
                output.WriteLine($"{power}: used {engine.Rules.Player2[GameTag.NUM_TIMES_HERO_POWER_USED_THIS_GAME]} times, {engine.Rules.Turn} turns");
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    /// <summary>What the client gets for a play, from the play's first history entry on.</summary>
    private static string Sent(SabberStoneCore.Model.Game game, int from) =>
        HistoryText.Format(new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full.Skip(from).ToList()).ToList(),
            id => game.IdEntityDic.TryGetValue(id, out var e) ? $"[{e.Card.Id} {id}]" : id.ToString(), withTags: false);

    [Fact]
    public void Passive_boss_powers_trigger_in_the_live_block_shape()
    {
        // Stormswell thrice (live-20261007-212735 59942): POWER 0, POWER 1 (Overload), then twice SLUSH_TIME 3000, POWER 0, POWER 1
        var game = Game("DALA_BOSS_20px");
        var me = game.CurrentPlayer;
        int from = game.PowerHistory.Full.Count;
        Play(game, me, "EX1_238", me.Opponent.Hero);
        string bolt = Sent(game, from);
        output.WriteLine(bolt);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(bolt, @"BlockType=POWER Entity=\[EX1_238 \d+\][^\n]*EffectIndex=1").Count);

        // The Feast (live-session-20261001-162435 17691): TRIGGER on the Hero Power, TRIGGER_VISUAL, inside the spell's PLAY block
        game = Game("DALA_BOSS_02p");
        me = game.CurrentPlayer;
        me.Hero.Damage = 10;
        from = game.PowerHistory.Full.Count;
        Play(game, me, "CS2_029", me.Opponent.Hero);
        string feast = Sent(game, from);
        output.WriteLine(feast);
        Assert.Matches(@"BlockType=TRIGGER Entity=\[DALA_BOSS_02p \d+\][^\n]*TriggerKeyword=TRIGGER_VISUAL", feast);

        // Street Smarts (live-session-20261005-140614 38402): the boss's TRIGGER before the Coin's POWER block
        game = Game(p2Power: "DALA_BOSS_10px");
        me = game.CurrentPlayer;
        from = game.PowerHistory.Full.Count;
        Play(game, me, "GAME_005");
        string coin = Sent(game, from);
        output.WriteLine(coin);
        Assert.True(coin.IndexOf("Entity=[DALA_BOSS_10px", StringComparison.Ordinal) < coin.IndexOf("BlockType=POWER Entity=[GAME_005", StringComparison.Ordinal));
    }

    [Fact]
    public void Prestidigitation_prediction_and_shhh_do_what_they_say()
    {
        var game = Game("DALA_BOSS_03p");
        var me = game.CurrentPlayer;
        int hand = me.HandZone.Count;
        UsePower(game, me);
        Assert.Equal(hand + 1, me.HandZone.Count);
        Assert.StartsWith("DALA_BOSS_03t", me.HandZone.GetAll().Last().Card.Id);

        game = Game("DALA_BOSS_21p");
        me = game.CurrentPlayer;
        UsePower(game, me);
        var secret = Assert.Single(me.SecretZone.GetAll());
        Assert.True(secret.Card.IsSecret);

        game = Game("DALA_BOSS_22p");
        me = game.CurrentPlayer;
        var taunt = OnBoard(game, me.Opponent, "CS2_179"); // Sen'jin Shieldmasta: Taunt
        UsePower(game, me);
        Assert.False(taunt.HasTaunt);
    }

    [Fact]
    public void Akazamzarak_feats_change_both_players()
    {
        var game = Game();
        var me = game.CurrentPlayer;
        var bolt = Give(me.Opponent, "CS2_029");
        Play(game, me, "DALA_BOSS_03t"); // Reductomara
        Assert.Equal(3, bolt.Cost);
        int mine = me.DeckZone.Count, theirs = me.Opponent.DeckZone.Count;
        Play(game, me, "DALA_BOSS_03t4"); // Yoggers Poggers
        Assert.Equal(mine + 3, me.DeckZone.Count);
        Assert.Equal(theirs + 3, me.Opponent.DeckZone.Count);
        int myHand = me.HandZone.Count, theirHand = me.Opponent.HandZone.Count;
        Play(game, me, "DALA_BOSS_03t3"); // Presto Legendaro
        Assert.Equal(myHand + 1, me.HandZone.Count);
        Assert.Equal(theirHand + 1, me.Opponent.HandZone.Count);
        Assert.Equal(1, me.HandZone.GetAll().Last()[GameTag.PREMIUM]);
    }
}
