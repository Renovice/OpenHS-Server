using HsServer.Lobby;
using HsServer.Protocol;
using HsServer.Sabber;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using Xunit;

namespace HsServer.Tests;

/// <summary>The Dalaran Heist run loop on save data (Lobby/HeistRuns; live shape: FINDINGS 2026-10-09).</summary>
public class HeistRunTests
{
    private static readonly string DbfDir = HsServer.Engine.CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
    private static readonly Lazy<HsServer.Engine.CardDb> Cards = new(() => HsServer.Engine.CardDb.Load(DbfDir));
    private static readonly Lazy<HeistRuns> Heist = new(() =>
        new HeistRuns(DbfDir, Path.Combine(TestEnvironment.ProjectRoot, "Server", "data", "heist"), Cards.Value));

    private const long Normal = 55, ChapterOne = 3005, Warrior = 10, MrChu = 54546, ArmorUp = 725;

    private static SaveDataStore Picked(long deck)
    {
        var sd = new SaveDataStore();
        // what the client writes when the player picks (live: 238, 239, 236, 237, 246)
        sd.SetInts(Normal, 238, new[] { ChapterOne });
        sd.SetInts(Normal, 239, new[] { Warrior });
        sd.SetInts(Normal, 236, new[] { ArmorUp });
        sd.SetInts(Normal, 237, new[] { deck });
        sd.SetInts(Normal, 246, new long[] { 0 });
        return sd;
    }

    private static long One(SaveDataStore sd, long sub) => sd.Values(Normal, sub).FirstOrDefault();

    [Fact]
    public void A_run_starts_from_the_picks_and_goes_on_after_a_win()
    {
        HeistRuns heist = Heist.Value;
        Assert.True(heist.IsHeist(ChapterOne));
        var sd = Picked(1955); // Tough Crowd (Warrior premade)
        var (human, boss) = heist.Prepare(sd, ChapterOne, MrChu, new Random(1))!.Value;
        Assert.Equal(1, One(sd, 81)); // IS_RUN_ACTIVE
        Assert.Equal(10, human.Deck.Count); // the premade deck's DeckCard rows
        Assert.Equal(15, human.StartHealth);
        Assert.Equal("HERO_01bp", human.HeroPowerCardId); // the picked Hero Power
        Assert.Equal(10, boss.StartHealth);
        Assert.StartsWith("DALA_BOSS_", boss.HeroCardId);
        Assert.Equal(5, sd.Values(Normal, 19).Count); // NEXT_BOSS_FIGHT: the boss 5 times
        Assert.Single(sd.Values(Normal, 19).Distinct());

        heist.GameEnded(sd, ChapterOne, won: true, new Random(2));
        Assert.Single(sd.Values(Normal, 13)); // BOSSES_DEFEATED
        Assert.Equal(20, One(sd, 130)); // +5 hero Health
        Assert.Equal(15, One(sd, 123)); // the 2nd boss at 15
        foreach (long loot in new long[] { 23, 24, 25 }) Assert.Equal(4, sd.Values(Normal, loot).Count); // [bundle, 3 cards]
        Assert.Equal(3, sd.Values(Normal, 26).Count); // a treasure offer after boss 1

        // the picks (the client writes PLAYER_CHOSEN_TREASURE, then _LOOT): the treasure, then the bundle's cards join the deck
        long treasure = sd.Values(Normal, 26)[0];
        var bundle = sd.Values(Normal, 24).ToList();
        sd.SetInts(Normal, 102, new long[] { 1 });
        heist.ClientWrote(sd, new[] { (Normal, 102L) });
        sd.SetInts(Normal, 101, new long[] { 2 });
        heist.ClientWrote(sd, new[] { (Normal, 101L) });
        var deck = sd.Values(Normal, 14).ToList();
        Assert.Equal(14, deck.Count);
        Assert.Equal(treasure, deck[10]);
        Assert.Equal(bundle.Skip(1), deck.Skip(11));
        Assert.Equal(new[] { bundle[0] }, sd.Values(Normal, 80)); // LOOT_HISTORY
        Assert.Empty(sd.Values(Normal, 23));

        // the next game is the same run
        var (human2, boss2) = heist.Prepare(sd, ChapterOne, MrChu, new Random(3))!.Value;
        Assert.Equal(14, human2.Deck.Count);
        Assert.Equal(20, human2.StartHealth);
        Assert.Equal(15, boss2.StartHealth);

        // a loss ends it: the boss it lost to, the picks cleared, the deck kept for the results screen
        long lostTo = One(sd, 19);
        heist.GameEnded(sd, ChapterOne, won: false, new Random(4));
        Assert.Equal(0, One(sd, 81));
        Assert.Equal(lostTo, One(sd, 100));
        Assert.Equal(0, One(sd, 237));
        Assert.Equal(14, sd.Values(Normal, 14).Count);
        Assert.Equal(0, One(sd, 168)); // the results screen not seen yet
    }

    [Fact]
    public void A_heist_game_plays_with_the_runs_health_hero_power_and_a_heroic_boss()
    {
        // Heroic Chapter V: the run's deck, Hero Power and Health; the boss with its HEROIC_HERO_POWER (an unscripted placeholder)
        const long Heroic = 344, HeroicFive = 3332;
        var sd = new SaveDataStore();
        foreach (var (sub, v) in new[] { (238L, HeroicFive), (239L, Warrior), (236L, ArmorUp), (237L, 1955L), (246L, 0L) }) sd.SetInts(Heroic, sub, new[] { v });
        var (human, boss) = Heist.Value.Prepare(sd, HeroicFive, MrChu, new Random(9))!.Value;
        // (30 of 77 bosses have no HEROIC_HERO_POWER — Kizi Copperclip: they keep their HERO_POWER)
        int heroicPower = Cards.Value[boss.HeroCardId].Tag(HsServer.Engine.GAME_TAG.HEROIC_HERO_POWER);
        string expectedPower = Cards.Value.ByDbf(heroicPower > 0 ? heroicPower : Cards.Value[boss.HeroCardId].Tag(HsServer.Engine.GAME_TAG.HERO_POWER)).CardId;
        Assert.Equal(heroicPower > 0 ? expectedPower : null, boss.HeroPowerCardId);
        human.AccountHi = 1; human.AccountLo = 2;
        var rng = new Random(9);
        boss.Deck = RandomDecks.Build(CardClass.MAGE, rng, CardSet.CORE, CardSet.EXPERT1);
        var engine = new SabberEngineGame(new HsServer.Game.EngineSetup
        {
            Cards = Cards.Value, Scenario = (int)HeroicFive, Seed = 9, Rules = HsServer.Game.RulesEngine.Sabber,
            GameSetupBody = PegasusPackets.Serialize(new PegasusGame.GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
            Human = human, Opponent = boss,
        });
        var result = new SoakRunner(rng) { Trace = (_, _) => { } }.Play(engine);
        Assert.Empty(result.Problems);
        var game = engine.Rules;
        Assert.Equal("DALA_Chu", game.Player1.Hero.Card.Id);
        Assert.Equal((int)ArmorUp, game.Player1.Hero.HeroPower.Card.AssetId); // (the engine knows dbf 725 as CS2_102; the client data as HERO_01bp)
        Assert.Equal(expectedPower, game.Player2.Hero.HeroPower.Card.Id);
        Assert.Equal(15, game.Player1.Hero[GameTag.HEALTH]); // (the first game: the cards' own Health, as live's first game)
        Assert.Equal(10, game.Player2.Hero[GameTag.HEALTH]);

        // the next game after a win: live (224614, 2nd game) — right after the players start, a TRIGGER block of the game sets both
        // heroes' HEALTH (20 and 15)
        Heist.Value.GameEnded(sd, HeroicFive, won: true, new Random(10));
        var (human2, boss2) = Heist.Value.Prepare(sd, HeroicFive, MrChu, new Random(11))!.Value;
        human2.AccountHi = 1; human2.AccountLo = 2;
        boss2.Deck = RandomDecks.Build(CardClass.MAGE, rng, CardSet.CORE, CardSet.EXPERT1);
        var engine2 = new SabberEngineGame(new HsServer.Game.EngineSetup
        {
            Cards = Cards.Value, Scenario = (int)HeroicFive, Seed = 10, Rules = HsServer.Game.RulesEngine.Sabber,
            GameSetupBody = PegasusPackets.Serialize(new PegasusGame.GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
            Human = human2, Opponent = boss2,
        });
        engine2.Start();
        var game2 = engine2.Rules;
        var history = game2.PowerHistory.Full;
        int block = history.FindIndex(e => e is PowerHistoryBlockStart { BlockType: BlockType.TRIGGER } b && b.Source == game2.Id);
        Assert.True(block >= 0);
        Assert.Contains(history.Skip(block + 1).TakeWhile(e => e is not PowerHistoryBlockEnd),
            e => e is PowerHistoryTagChange { Tag: GameTag.HEALTH, Value: 20 } t && t.EntityId == game2.Player1.Hero.Id);
        Assert.Contains(history.Skip(block + 1).TakeWhile(e => e is not PowerHistoryBlockEnd),
            e => e is PowerHistoryTagChange { Tag: GameTag.HEALTH, Value: 15 } t && t.EntityId == game2.Player2.Hero.Id);
        // live: the first HEALTH change is a "DEF CHANGE" — the run's Health becomes the base: the client shows it white, not green
        Assert.Contains(history.Skip(block + 1).TakeWhile(e => e is not PowerHistoryBlockEnd),
            e => e is PowerHistoryTagChange { Tag: GameTag.HEALTH, Value: 20, ChangeDef: true } t && t.EntityId == game2.Player1.Hero.Id);
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(history);
        Assert.Contains(sent, d => d.TagChange is { ChangeDef: true } t && t.Entity == game2.Player1.Hero.Id && t.Value == 20);
        // (2026-11-01, user: boss Health on screen ≠ the server's) live's full sequence reaches the client for BOTH heroes: the DEF CHANGE,
        // the plain value (the client shows that), then 3085 — the boss card's printed Health (30 for Mr. Chu? its own) isn't what's shown
        foreach (var (hero, health) in new[] { (game2.Player1.Hero.Id, 20), (game2.Player2.Hero.Id, 15) })
        {
            Assert.Contains(sent, d => d.TagChange is { ChangeDef: true } t && t.Entity == hero && t.Tag == (int)GameTag.HEALTH && t.Value == health);
            Assert.Contains(sent, d => d.TagChange is { ChangeDef: false } t && t.Entity == hero && t.Tag == (int)GameTag.HEALTH && t.Value == health);
            Assert.Contains(sent, d => d.TagChange is { } t && t.Entity == hero && t.Tag == 3085 && t.Value == health);
        }
        // ...and it stays: no later change back to the card's printed Health (2026-11-01: a silent set was undone — the boss showed 30, green)
        Assert.Equal(15, game2.Player2.Hero.Health);
        Assert.DoesNotContain(sent, d => d.TagChange is { ChangeDef: false } t && t.Entity == game2.Player2.Hero.Id && t.Tag == (int)GameTag.HEALTH && t.Value != 15);
    }

    /// <summary>A run in chapter I facing <paramref name="boss"/> after <paramref name="defeated"/> wins.</summary>
    private static SaveDataStore Facing(string boss, int defeated)
    {
        var sd = Picked(1955);
        Heist.Value.Prepare(sd, ChapterOne, MrChu, new Random(12)); // (starts the run)
        sd.SetInts(Normal, 19, Enumerable.Repeat((long)Cards.Value[boss].DbfId, 5));
        sd.SetInts(Normal, 13, Enumerable.Range(0, defeated).Select(_ => 53568L));
        return sd;
    }

    [Fact]
    public void Bosses_play_their_decks_from_the_wiki()
    {
        // a fixed list (Trade Prince Gallywix, chapter I's last boss): its 30 cards
        var (_, gallywix) = Heist.Value.Prepare(Facing("DALA_BOSS_57h", 7), ChapterOne, MrChu, new Random(13))!.Value;
        Assert.Equal(30, gallywix.Deck.Count);
        // a pool ("Possible cards", Alchemist Wendy, sizes 10 / 15): 10 at her lower level (I-1), 15 at her higher (I-2)
        var (_, wendyLow) = Heist.Value.Prepare(Facing("DALA_BOSS_16h", 0), ChapterOne, MrChu, new Random(14))!.Value;
        Assert.Equal(10, wendyLow.Deck.Count);
        var (_, wendyHigh) = Heist.Value.Prepare(Facing("DALA_BOSS_16h", 1), ChapterOne, MrChu, new Random(15))!.Value;
        Assert.Equal(15, wendyHigh.Deck.Count);
        Assert.All(wendyHigh.Deck.GroupBy(c => c), g => Assert.True(g.Count() <= 2));
        // a fixed list "missing 5 random cards" at the boss's lower level (Banker Biggs: I-5 and I-6)
        var (_, biggsLow) = Heist.Value.Prepare(Facing("DALA_BOSS_64h", 4), ChapterOne, MrChu, new Random(16))!.Value;
        Assert.Equal(25, biggsLow.Deck.Count);
        var (_, biggsHigh) = Heist.Value.Prepare(Facing("DALA_BOSS_64h", 5), ChapterOne, MrChu, new Random(17))!.Value;
        Assert.Equal(30, biggsHigh.Deck.Count);
    }

    [Fact]
    public void Create_game_names_each_players_hero_like_live()
    {
        // live (live-20261001-1624 Power.log): each player's HERO_ENTITY is a CREATE_GAME tag; the Heist's client code reads the boss
        // from it (GenericDungeonMissionEntity.GetOpposingHeroCardID) — without it the client threw and never loaded the game
        var sd = Picked(1955);
        var (human, boss) = Heist.Value.Prepare(sd, ChapterOne, MrChu, new Random(20))!.Value;
        human.AccountHi = 1; human.AccountLo = 2;
        boss.Deck = RandomDecks.Build(CardClass.MAGE, new Random(20), CardSet.CORE, CardSet.EXPERT1);
        var engine = new SabberEngineGame(new HsServer.Game.EngineSetup
        {
            Cards = Cards.Value, Scenario = (int)ChapterOne, Seed = 20, Rules = HsServer.Game.RulesEngine.Sabber,
            GameSetupBody = PegasusPackets.Serialize(new PegasusGame.GameSetup { Board = 8, MaxFriendlyMinionsPerPlayer = 7 }),
            Human = human, Opponent = boss,
        });
        engine.Start();
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(engine.Rules.PowerHistory.Full);
        var create = sent.First(d => d.CreateGame != null).CreateGame;
        foreach (var (player, hero) in new[] { (1, engine.Rules.Player1.Hero.Id), (2, engine.Rules.Player2.Hero.Id) })
            Assert.Contains(create.Players.Single(p => p.Id == player).Entity.Tags, t => t.Name == (int)HsServer.Engine.GAME_TAG.HERO_ENTITY && t.Value == hero);
        Assert.DoesNotContain(sent, d => d.TagChange is { } t && t.Tag == (int)HsServer.Engine.GAME_TAG.HERO_ENTITY);
    }

    [Fact]
    public void Loot_never_offers_a_hero_skin()
    {
        // (2026-10-09, user: "why is a hero portrait that's essentially a skin showing up in the buckets?" — Khadgar HERO_08b in Mage's
        // Summoning bundle; the pool's name lookup had picked the skin over DAL_575) every class, many wins: no 0-cost hero card offered
        foreach (long cls in new long[] { 2, 3, 4, 5, 6, 7, 8, 9, 10 })
            for (int seed = 0; seed < 25; seed++)
            {
                var sd = Picked(1955);
                sd.SetInts(Normal, 239, new[] { cls });
                Heist.Value.Prepare(sd, ChapterOne, MrChu, new Random(seed));
                Heist.Value.GameEnded(sd, ChapterOne, won: true, new Random(seed));
                foreach (long loot in new long[] { 23, 24, 25 })
                    foreach (long dbf in sd.Values(Normal, loot).Skip(1))
                    {
                        var card = Cards.Value.ByDbf((int)dbf);
                        Assert.False(card.Type == HsServer.Engine.TAG_CARDTYPE.HERO && card.Cost == 0, $"class {cls}: skin {card.CardId} offered");
                    }
            }
    }

    [Fact]
    public void A_replaced_hero_power_is_its_owners()
    {
        // (2026-10-09, user: A Prince's Ring "removed my hero power") ReplaceHeroPower made the new Hero Power from a plain dictionary,
        // which the Entity constructor copied — the CONTROLLER Playable adds after was lost: the client got "player=0" and drew no Hero
        // Power (Shadowform, Metamorphosis, Reno, every replacer too)
        var g = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.MAGE, Player2HeroClass = CardClass.WARRIOR, FillDecks = true, History = true, Shuffle = false,
        });
        g.StartGame();
        int mark = g.PowerHistory.Full.Count;
        new SabberStoneCore.Tasks.SimpleTasks.ReplaceHeroPower(SabberStoneCore.Model.Cards.FromId("ICC_829p")).Process(g, g.Player1, g.Player1.Hero, null);
        var made = g.PowerHistory.Full.Skip(mark).OfType<PowerHistoryFullEntity>().Single(f => f.Entity.Id == g.Player1.Hero.HeroPower.Id);
        Assert.Equal(1, made.Entity.Tags[GameTag.CONTROLLER]);
        Assert.Equal(g.Player1.Hero.HeroPower.Id, made.Entity.Tags[GameTag.ENTITY_ID]);
    }

    [Fact]
    public void The_ai_auto_casts_a_bosses_auto_cast_hero_power()
    {
        // (2026-10-09, user: Makeover only "sometimes") wiki Kizi Copperclip: Makeover "Auto-cast" (client tag AI_MUST_PLAY); live: the
        // first action of each of her turns with a friendly minion (heist-session-20260929-122856, 7 of 7)
        var g = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 2, Player1HeroClass = CardClass.MAGE, Player2HeroCard = SabberStoneCore.Model.Cards.FromId("DALA_BOSS_32h"),
            Player2HeroClass = CardClass.ROGUE, FillDecks = true, History = false, Shuffle = false, SkipMulligan = true,
        });
        g.StartGame();
        g.Player2.BaseMana = 10;
        SabberStoneCore.Model.Entities.Entity.FromCard(g.Player2, SabberStoneCore.Model.Cards.FromId("CS2_182"), zone: g.Player2.BoardZone);
        var move = new HsServer.Sabber.Ai.PlannerAi().Step(g, g.Player2);
        Assert.Equal(SabberStoneCore.Tasks.PlayerTasks.PlayerTaskType.HERO_POWER, move.PlayerTaskType);
    }

    private static SabberStoneCore.Model.Game BossGame(string bossHero)
    {
        HsServer.Sabber.SabberEngineGame.EnsureCards(DbfDir);
        var g = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 2, Player1HeroClass = CardClass.MAGE, Player2HeroCard = SabberStoneCore.Model.Cards.FromId(bossHero),
            Player2HeroClass = CardClass.HUNTER, FillDecks = true, History = true, Shuffle = false, SkipMulligan = true,
        });
        g.StartGame();
        return g;
    }

    [Fact]
    public void Merry_go_round_needs_a_minion_to_turn()
    {
        // (2026-10-09, user) live: 4 of 4 Merry Go Round triggers moved a minion; on an empty board it doesn't go off
        var g = BossGame("DALA_BOSS_34h");
        int mark = g.PowerHistory.Full.Count;
        g.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(g.Player2));
        Assert.DoesNotContain(g.PowerHistory.Full.Skip(mark), e => e is PowerHistoryBlockStart { BlockType: BlockType.TRIGGER } b && b.Source == g.Player2.Hero.HeroPower.Id);
        g.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(g.Player1));
        var mine = SabberStoneCore.Model.Entities.Entity.FromCard(g.Player2, SabberStoneCore.Model.Cards.FromId("CS2_182"), zone: g.Player2.BoardZone);
        g.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(g.Player2));
        Assert.Equal(g.Player1, mine.Controller); // his left-most went round to the player's side
    }

    [Fact]
    public void The_lackeys_do_what_they_say()
    {
        // (2026-10-09, user: "Titanic Lackey has no effect") Titanic Lackey and Draconic Lackey were upstream TODOs
        var g = BossGame("DALA_BOSS_34h");
        var friend = SabberStoneCore.Model.Entities.Entity.FromCard(g.Player2, SabberStoneCore.Model.Cards.FromId("CS2_182"), zone: g.Player2.BoardZone);
        g.Player2.BaseMana = 10;
        var titanic = SabberStoneCore.Actions.Generic.DrawCard(g.Player2, SabberStoneCore.Model.Cards.FromId("ULD_616"));
        Assert.True(g.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(g.Player2, titanic, (SabberStoneCore.Model.Entities.ICharacter)friend)));
        Assert.True(((SabberStoneCore.Model.Entities.Minion)friend).HasTaunt);
        Assert.Equal(7, ((SabberStoneCore.Model.Entities.Minion)friend).Health);
        var draconic = SabberStoneCore.Actions.Generic.DrawCard(g.Player2, SabberStoneCore.Model.Cards.FromId("DRG_052"));
        Assert.True(g.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Any(g.Player2, draconic)));
        Assert.NotNull(g.Player2.Choice);
        Assert.All(g.Player2.Choice.Choices, id => Assert.True(g.IdEntityDic[id].Card.IsRace(Race.DRAGON)));
    }

    [Fact]
    public void A_random_deck_is_ten_cards_of_the_class_and_neutral()
    {
        var sd = Picked(1988); // Random Warrior Deck
        var (human, _) = Heist.Value.Prepare(sd, ChapterOne, MrChu, new Random(5))!.Value;
        Assert.Equal(10, human.Deck.Count);
        foreach (string id in human.Deck)
        {
            var card = Cards.Value[id];
            Assert.Contains(card.Tag(HsServer.Engine.GAME_TAG.CLASS), new[] { (int)Warrior, (int)HsServer.Engine.TAG_CLASS.NEUTRAL });
        }
    }

    [Fact]
    public void Retiring_ends_the_run_and_a_reset_leaves_none()
    {
        var sd = Picked(1955);
        Heist.Value.Prepare(sd, ChapterOne, MrChu, new Random(6));
        sd.SetInts(Normal, 662, new long[] { 1 }); // the client's retire (IS_RUN_RETIRED)
        Heist.Value.ClientWrote(sd, new[] { (Normal, 662L) });
        Assert.Equal(0, One(sd, 81));
        Assert.Equal(1, One(sd, 662));

        var sd2 = Picked(1955);
        Heist.Value.Prepare(sd2, ChapterOne, MrChu, new Random(7));
        sd2.SetInts(Normal, 84, new long[] { 9 }); // WARRIOR_BOSS_WINS (unlock progress)
        Heist.Value.Clean(sd2);
        Assert.Equal(0, One(sd2, 81));
        Assert.Equal(9, One(sd2, 84)); // kept
        Assert.Equal(5, sd2.Values(Normal, 19).Count); // previews between runs

        // the next run's first boss is the chapter's pre-rolled one (live: 22 of 22 starts)
        long rolledForChapterOne = sd2.Values(Normal, 19)[0];
        foreach (var (sub, v) in new[] { (238L, ChapterOne), (239L, Warrior), (236L, ArmorUp), (237L, 1955L) }) sd2.SetInts(Normal, sub, new[] { v });
        var (_, boss) = Heist.Value.Prepare(sd2, ChapterOne, MrChu, new Random(8))!.Value;
        Assert.Equal(Cards.Value.ByDbf((int)rolledForChapterOne).CardId, boss.HeroCardId);
    }
}
