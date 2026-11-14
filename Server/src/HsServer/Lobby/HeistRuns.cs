using System.Text.Json;
using HsServer.Engine;

namespace HsServer.Lobby;

/// <summary>
/// The Dalaran Heist run loop (2026-10-09, slice 1). Live has no Heist-specific message: the run lives in the client's save data
/// (server key 55 Normal / 344 Heroic, AdventureData) and boss games are ordinary VS_AI queues of a chapter's scenario. Recorded
/// shape (RESEARCH/captures live-20261004-night, heist-run-20260929, the live install's sessions; FINDINGS 2026-10-09):
///   pick  — the client writes PLAYER_SELECTED_* (scenario 238, class 239, hero power 236, deck 237, anomaly 246), then queues;
///   start — the queue starts the run: IS_RUN_ACTIVE, the deck (premade DeckCard rows, or 10 random cards for a "Random" deck), hero
///           Health 15, the first boss (NEXT_BOSS_FIGHT ×5) at Health 10;
///   win   — BOSSES_FOUGHT / DEFEATED, class BOSS_WINS, +5 hero Health, the next boss and its Health (10, 15, 20, 30 … 70; Chapter V
///           75, 80, 90, 100), three loot bundles [bundle, 3 cards], a treasure offer after bosses 1, 3, 5 and 7; the last boss ends
///           the run as won (RUN_WINS, the chapter's *_WING_n_WINS lists);
///   pick  — PLAYER_CHOSEN_TREASURE / _LOOT (1–3): the treasure, then the bundle's cards, join DECK_CARD_LIST; the bundle LOOT_HISTORY;
///   loss  — the run ends: BOSS_LOST_TO, the picks cleared, the deck and defeated bosses kept for the results screen;
///   retire — the client writes IS_RUN_RETIRED = 1 (its own code: AdventureDungeonCrawlDeckTray.OnRetirePopupResponse): ended as a loss.
///   tavern — after bosses 3 and 5 Bob's Tavern comes first (SCENARIO_OVERRIDE; HeistTavern.cs, RESEARCH/heist-tavern.md).
///   anomaly — between runs ANOMALY_MODE_CARD_PREVIEW (392) holds a pre-rolled anomaly; a run's start makes it CURRENT_ANOMALY_MODE_CARD
///           (243), IS_ANOMALY_MODE (377) = the pick (246); in anomaly mode every boss game plays it (PlayerSetup.AnomalyCardId); the run's
///           end rolls the next preview (Server/data/heist/anomalies.json).
/// Not yet: treasures' and bosses' scripts, boss decks (the client data names them — HERO_DECK_ID — without cards: placeholders of the
/// boss's class).
/// </summary>
internal sealed partial class HeistRuns
{
    // client GameSaveKeySubkeyId
    private const long BossesDefeated = 13, DeckCardList = 14, DeckClass = 15, BossesFought = 18, NextBossFight = 19,
        LootA = 23, LootB = 24, LootC = 25, TreasureOption = 26, LootHistory = 80, IsRunActive = 81, BossLostTo = 100,
        ChosenLoot = 101, ChosenTreasure = 102, NextBossHealth = 123, HeroHealth = 130, HasSeenRunComplete = 168,
        SelHeroPower = 236, SelDeck = 237, SelScenario = 238, SelHeroClass = 239, SelAnomaly = 246, CurrentAnomalyCard = 243,
        TotalBossWins = 248, TotalRunWins = 249, ScenarioOverride = 266, HeroPowerWingWins = 299, DeckWingWins = 304,
        HeroCardWingWins = 309, ScenarioId = 375, HeroPower = 376, IsAnomalyMode = 377, IsRunRetired = 662,
        RunHeroCard = 155, RunHeroPower = 156, RunCounter = 409, // (155 / 156 / 409: no client name — live values)
        AnomalyPreview = 392;

    // class → (BOSS_WINS, RUN_WINS) subkeys
    private static readonly Dictionary<int, (long Boss, long Run)> ClassWins = new()
    {
        [5] = (82, 83), [10] = (84, 85), [2] = (86, 87), [7] = (88, 89), [8] = (90, 91), [6] = (92, 93), [9] = (94, 95), [3] = (96, 97),
        [4] = (98, 99), [14] = (668, 669),
    };

    private const int StartHeroHealth = 15, HealthPerWin = 5;
    // the boss's Health by its level (1-based; live: 10, 15, 20, 30, 40, 50, 60, 70; Chapter V's 9-12: 75, 80, 90, 100)
    private static readonly int[] BossHealth = { 10, 15, 20, 30, 40, 50, 60, 70, 75, 80, 90, 100 };
    private static readonly int[] TreasureAfter = { 1, 3, 5, 7 };

    private sealed record Chapter(int Number, int Wing, List<List<int>> Levels);
    private sealed record Bundle(int Dbf, List<int> Cards);

    private readonly CardDb cards;
    private readonly Dictionary<int, (int Wing, bool Heroic)> scenarios = new(); // chapter scenarios
    private readonly HashSet<int> taverns = new();
    private readonly Dictionary<bool, long> serverKey = new();
    private readonly Dictionary<int, int> chapterOfWing = new();
    private readonly Dictionary<int, Chapter> chapters = new(); // by wing
    private readonly Dictionary<int, List<int>> deckCards = new();
    private readonly Dictionary<int, string> deckNotes = new();
    private readonly Dictionary<int, List<Bundle>> bundles = new(); // by class
    private readonly Dictionary<int, List<int>> treasureOffers = new(); // by boss level the offer follows: every option seen
    private readonly Dictionary<int, int> previewBoss = new(); // scenario → its preview boss (Scenario.Player2HeroCardId)
    private readonly Dictionary<int, int> twistOfWing = new(); // wing → its chapter twist (WingDbf.PlotTwistCardId: DALA_829 The Coffers…)
    private readonly List<int> anomalies = new(); // Anomaly Mode's pool (Server/data/heist/anomalies.json: the wiki's 18)
    // boss hero card id → its decks: Server/data/heist/boss-decks.json (the wiki boss pages' Decks sections, cited per boss; the client
    // data names each boss's deck — HERO_DECK_ID — without its cards)
    private sealed record DeckList(bool Pool, List<(string Id, int Count)> Cards);
    private sealed record BossDeck(DeckList Normal, DeckList? Heroic, List<int> Sizes, int LowerLevelMissing);
    private readonly Dictionary<string, BossDeck> bossDecks = new(StringComparer.Ordinal);

    public HeistRuns(string dbfDir, string heistDir, CardDb cards)
    {
        this.cards = cards;
        static JsonElement.ArrayEnumerator Records(string file) => JsonDocument.Parse(File.ReadAllBytes(file)).RootElement.GetProperty("records").EnumerateArray();
        static int Int(JsonElement r, string name) => r.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
        const int Adventure = 438; // The Dalaran Heist
        foreach (JsonElement r in Records(Path.Combine(dbfDir, "AdventureData.json")))
            if (Int(r, "AdventureId") == Adventure) serverKey[Int(r, "ModeId") == 7] = Int(r, "GameSaveDataServerKey");
        foreach (JsonElement r in Records(Path.Combine(dbfDir, "Scenario.json")))
        {
            if (Int(r, "AdventureId") != Adventure) continue;
            int id = Int(r, "ID"), wing = Int(r, "WingId");
            if (wing == 0) { taverns.Add(id); tavernHeroic[id] = Int(r, "ModeId") == 7; } // (Bob's Tavern: HeistTavern.cs)
            else scenarios[id] = (wing, Int(r, "ModeId") == 7);
            previewBoss[id] = Int(r, "Player2HeroCardId");
        }
        foreach (JsonElement r in Records(Path.Combine(dbfDir, "Wing.json")))
            if (Int(r, "AdventureId") == Adventure && Int(r, "PlotTwistCardId") > 0) twistOfWing[Int(r, "ID")] = Int(r, "PlotTwistCardId");
        foreach (JsonElement r in Records(Path.Combine(dbfDir, "DeckCard.json")))
        {
            int deck = Int(r, "DeckId");
            if (!deckCards.TryGetValue(deck, out var list)) deckCards[deck] = list = new List<int>();
            list.Add(Int(r, "CardId"));
        }
        foreach (JsonElement r in Records(Path.Combine(dbfDir, "Deck.json")))
            deckNotes[Int(r, "ID")] = r.TryGetProperty("NoteName", out JsonElement n) ? n.GetString() ?? "" : "";

        // the boss pools by chapter and level (Server/data/heist/boss-pools.json: web sources + the recordings)
        string pools = Path.Combine(heistDir, "boss-pools.json");
        if (File.Exists(pools))
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(pools));
            foreach (JsonElement ch in doc.RootElement.GetProperty("chapters").EnumerateArray())
            {
                int number = Int(ch, "chapter");
                int wing = scenarios.TryGetValue(Int(ch, "normalScenario"), out var s) ? s.Wing : 0;
                if (wing == 0) continue;
                var levels = ch.GetProperty("levels").EnumerateArray().OrderBy(l => Int(l, "level"))
                    .Select(l => l.GetProperty("bosses").EnumerateArray().Select(b => Int(b, "dbf")).Where(d => d > 0).ToList()).ToList();
                chapters[wing] = new Chapter(number, wing, levels);
                chapterOfWing[wing] = number;
            }
        }
        // (without the pools: the chapters in wing order, each boss the scenario's preview boss)
        int next = chapterOfWing.Count + 1;
        foreach (int wing in scenarios.Values.Select(v => v.Wing).Distinct().OrderBy(w => w))
            if (!chapterOfWing.ContainsKey(wing)) chapterOfWing[wing] = next++;

        string deckFile = Path.Combine(heistDir, "boss-decks.json");
        if (File.Exists(deckFile))
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(deckFile));
            foreach (JsonElement b in doc.RootElement.GetProperty("bosses").EnumerateArray())
            {
                DeckList? List(string mode) => b.TryGetProperty(mode, out JsonElement d) && d.ValueKind == JsonValueKind.Object
                    ? new DeckList(d.GetProperty("kind").GetString() == "pool",
                        d.GetProperty("cards").EnumerateArray().Select(c => (c.GetProperty("cardId").GetString() ?? "", Math.Max(1, Int(c, "count"))))
                            .Where(c => c.Item1.Length > 0).ToList())
                    : null;
                string? id = b.TryGetProperty("cardId", out JsonElement cid) ? cid.GetString() : null;
                if (id == null || List("normal") is not { Cards.Count: > 0 } normal) continue;
                var sizes = b.TryGetProperty("sizes", out JsonElement sz) && sz.ValueKind == JsonValueKind.Array ? sz.EnumerateArray().Select(x => x.GetInt32()).ToList() : new List<int>();
                bossDecks[id] = new BossDeck(normal, List("heroic"), sizes, Int(b, "lowerLevelMissing"));
            }
        }

        // loot bundles and the treasure offers seen live (Server/data/heist/loot-pools.json)
        string loot = Path.Combine(heistDir, "loot-pools.json");
        if (File.Exists(loot))
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(loot));
            foreach (JsonProperty cls in doc.RootElement.GetProperty("classes").EnumerateObject())
            {
                if (!Enum.TryParse(cls.Name, true, out TAG_CLASS tagClass)) continue;
                bundles[(int)tagClass] = cls.Value.EnumerateArray()
                    .Select(b => new Bundle(Int(b, "bucketDbfId"), b.GetProperty("cards").EnumerateArray().Select(c => Int(c, "dbfId"))
                        .Where(d => d > 0 && !IsSkin(cards.ByDbf(d))).ToList())) // (a hero skin is never a deck card)
                    .Where(b => b.Dbf > 0 && b.Cards.Count >= 3).ToList();
            }
            foreach (JsonElement offer in doc.RootElement.GetProperty("treasureOffers").EnumerateArray())
            {
                int after = Int(offer, "bossesFought");
                if (!treasureOffers.TryGetValue(after, out var list)) treasureOffers[after] = list = new List<int>();
                foreach (JsonElement o in offer.GetProperty("options").EnumerateArray())
                    if (Int(o, "dbfId") is > 0 and int d && !list.Contains(d)) list.Add(d);
            }
        }
        // (2026-10-09) the whole treasure / passive pool (Server/data/heist/treasures.json: the wiki's lists, each with the bosses it is
        // offered after — passives 1 and 5, treasures 3 and 7) replaces the offers seen live, which only show part of it
        // Anomaly Mode's anomalies (Server/data/heist/anomalies.json: the wiki's 18 by dbf id; the live save-data rules in its note)
        string anomalyFile = Path.Combine(heistDir, "anomalies.json");
        if (File.Exists(anomalyFile))
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(anomalyFile));
            foreach (JsonElement a in doc.RootElement.GetProperty("anomalies").EnumerateArray())
                if (Int(a, "dbfId") is > 0 and int dbf && cards.ByDbf(dbf) != null) anomalies.Add(dbf);
        }
        string treasureFile = Path.Combine(heistDir, "treasures.json");
        if (File.Exists(treasureFile))
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(treasureFile));
            treasureOffers.Clear();
            foreach (JsonElement t in doc.RootElement.GetProperty("cards").EnumerateArray())
                if (Int(t, "dbfId") is > 0 and int dbf && t.TryGetProperty("offeredAfterBosses", out JsonElement after))
                    foreach (JsonElement a in after.EnumerateArray())
                    {
                        if (!treasureOffers.TryGetValue(a.GetInt32(), out var list)) treasureOffers[a.GetInt32()] = list = new List<int>();
                        if (!list.Contains(dbf)) list.Add(dbf);
                    }
        }
    }

    /// <summary>A hero skin: a 0-cost hero card (Cards.IsHeroPortrait's rule) — never a card in a deck.</summary>
    private static bool IsSkin(CardDef? c) => c != null && c.Type == TAG_CARDTYPE.HERO && c.Cost == 0;

    public bool IsHeist(long scenario) => scenarios.ContainsKey((int)scenario) || taverns.Contains((int)scenario);

    private long KeyOf(long scenario) =>
        scenarios.TryGetValue((int)scenario, out var s) ? serverKey.GetValueOrDefault(s.Heroic)
        : serverKey.GetValueOrDefault(tavernHeroic.GetValueOrDefault((int)scenario));

    private static long One(SaveDataStore sd, long key, long sub) => sd.Values(key, sub).FirstOrDefault();
    private static List<long> Many(SaveDataStore sd, long key, long sub) => sd.Values(key, sub).ToList();

    /// <summary>The players' setups for a Heist queue: starts the run when none is active (from the client's picks).</summary>
    public (PlayerSetup Human, PlayerSetup Boss)? Prepare(SaveDataStore sd, long scenario, long heroCardDbId, Random rng)
    {
        if (taverns.Contains((int)scenario)) return PrepareTavern(sd, scenario, heroCardDbId, rng); // (Bob's Tavern: HeistTavern.cs)
        if (!scenarios.TryGetValue((int)scenario, out var chapterScenario)) return null;
        long key = KeyOf(scenario);
        if (One(sd, key, IsRunActive) != 1) StartRun(sd, key, scenario, heroCardDbId, rng);

        var human = new PlayerSetup
        {
            HeroCardId = cards.ByDbf((int)One(sd, key, RunHeroCard))?.CardId ?? cards.ByDbf((int)heroCardDbId)?.CardId ?? "HERO_08",
            HeroPowerCardId = cards.ByDbf((int)One(sd, key, HeroPower))?.CardId,
            Deck = Many(sd, key, DeckCardList).Where(d => d > 0).Select(d => cards.ByDbf((int)d)?.CardId).Where(c => c != null).ToList()!,
            StartHealth = (int)One(sd, key, HeroHealth),
            // the chapter's twist, played for the run's player as the game starts (live: The Coffers, live-session-20260929-183021 383)
            PlotTwistCardId = twistOfWing.TryGetValue(chapterScenario.Wing, out int twist) ? cards.ByDbf(twist)?.CardId : null,
            // the run's anomaly in anomaly mode, played for the run's player after the mulligan (live: every boss game of the run)
            AnomalyCardId = One(sd, key, IsAnomalyMode) == 1 && One(sd, key, CurrentAnomalyCard) is > 0 and long anomaly
                ? cards.ByDbf((int)anomaly)?.CardId : null,
        };
        SetRunDeck(sd, key, human); // (the deck's slots and the run's deck enchantments: HeistTavern.cs)
        CardDef? boss = cards.ByDbf((int)One(sd, key, NextBossFight));
        string bossId = boss?.CardId ?? "HERO_08";
        var opponent = new PlayerSetup
        {
            HeroCardId = bossId,
            // (Heroic: the boss's HEROIC_HERO_POWER)
            HeroPowerCardId = chapterScenario.Heroic && boss?.Tag(GAME_TAG.HEROIC_HERO_POWER) is > 0 and int heroic ? cards.ByDbf(heroic)?.CardId : null,
            StartHealth = (int)One(sd, key, NextBossHealth),
            IsAi = true,
            // its deck (Heroic: the Heroic list when the sources have one); none known: empty (the caller gives a stand-in)
            Deck = BossDeckFor(bossId, chapterScenario.Heroic, chapterScenario.Wing, Many(sd, key, BossesDefeated).Count + 1, rng),
        };
        Log.Info("heist", $"{(chapterScenario.Heroic ? "Heroic" : "Normal")} chapter {chapterOfWing.GetValueOrDefault(chapterScenario.Wing)}: " +
                          $"{human.HeroCardId} ({human.StartHealth} Health, {human.Deck.Count} cards) vs {bossId} ({opponent.StartHealth} Health)");
        return (human, opponent);
    }

    /// <summary>
    /// The boss's deck for this game (boss-decks.json): a fixed list as listed — at the boss's lower level of the chapter less
    /// LowerLevelMissing random cards (wiki: "missing 5 random cards"); a pool ("Possible cards") drawn to the level's size (wiki
    /// "Total cards": low, high), each card at most twice unless the pool is too small.
    /// </summary>
    private List<string> BossDeckFor(string bossId, bool heroic, int wing, int level, Random rng)
    {
        if (!bossDecks.TryGetValue(bossId, out BossDeck? d)) return new List<string>();
        DeckList list = heroic ? d.Heroic ?? d.Normal : d.Normal;
        int bossDbf = cards.TryGet(bossId, out CardDef? def) ? def.DbfId : 0;
        var levels = chapters.TryGetValue(wing, out Chapter? ch)
            ? ch.Levels.Select((pool, i) => (pool, i)).Where(x => x.pool.Contains(bossDbf)).Select(x => x.i + 1).ToList() : new List<int>();
        bool lower = levels.Count > 1 && level <= levels.Min();
        if (!list.Pool)
        {
            var deck = list.Cards.SelectMany(c => Enumerable.Repeat(c.Id, c.Count)).ToList();
            if (lower && d.LowerLevelMissing > 0)
                for (int i = 0; i < d.LowerLevelMissing && deck.Count > 0; i++) deck.RemoveAt(rng.Next(deck.Count));
            return deck;
        }
        int size = d.Sizes.Count == 0 ? list.Cards.Count : lower ? d.Sizes[0] : d.Sizes[^1];
        var ids = list.Cards.Select(c => c.Id).ToList();
        var drawn = ids.OrderBy(_ => rng.Next()).Take(size).ToList();
        for (int copies = 2; drawn.Count < size && ids.Count > 0; copies++)
            foreach (string id in ids.OrderBy(_ => rng.Next()))
                if (drawn.Count < size && drawn.Count(x => x == id) < copies) drawn.Add(id);
        return drawn;
    }

    private void StartRun(SaveDataStore sd, long key, long scenario, long heroCardDbId, Random rng)
    {
        int cls = (int)One(sd, key, SelHeroClass);
        int deckId = (int)One(sd, key, SelDeck);
        List<long> deck = deckCards.TryGetValue(deckId, out var premade) && premade.Count > 1 && !deckNotes.GetValueOrDefault(deckId, "").EndsWith("_Random", StringComparison.Ordinal)
            ? premade.Select(d => (long)d).ToList()
            : RandomDeck(cls, rng);
        int wing = scenarios[(int)scenario].Wing;
        long anomaly = One(sd, key, AnomalyPreview) is > 0 and long preview && anomalies.Contains((int)preview) ? preview : RollAnomaly(rng, 0);
        // the first boss: the chapter's pre-rolled one (between runs NEXT_BOSS_FIGHT holds a level-1 boss per chapter, in chapter order;
        // live: it was the run's first boss in 22 of 22 recorded starts — boss-pools.json notes)
        var previews = Many(sd, key, NextBossFight);
        int chapter = chapterOfWing.GetValueOrDefault(wing);
        long rolled = chapter >= 1 && chapter <= previews.Count ? previews[chapter - 1] : 0;
        int first = rolled > 0 && (!chapters.TryGetValue(wing, out Chapter? ch) || ch.Levels.Count == 0 || ch.Levels[0].Contains((int)rolled))
            ? (int)rolled : PickBoss(wing, 1, new List<long>(), rng, scenario);
        var set = new List<(long, IEnumerable<long>)>
        {
            (IsRunActive, new long[] { 1 }), (RunCounter, new[] { One(sd, key, RunCounter) + 1 }), (ScenarioId, new[] { scenario }),
            (HeroPower, new[] { One(sd, key, SelHeroPower) }), (IsAnomalyMode, new[] { One(sd, key, SelAnomaly) }), (DeckClass, new long[] { cls }),
            (RunHeroCard, new[] { heroCardDbId }), (RunHeroPower, new[] { One(sd, key, SelHeroPower) }), (DeckCardList, deck),
            (HeroHealth, new long[] { StartHeroHealth }), (NextBossHealth, new long[] { BossHealth[0] }),
            (NextBossFight, Enumerable.Repeat((long)first, 5)), (BossesFought, Array.Empty<long>()), (BossesDefeated, Array.Empty<long>()),
            (LootA, Array.Empty<long>()), (LootB, Array.Empty<long>()), (LootC, Array.Empty<long>()), (TreasureOption, Array.Empty<long>()),
            (LootHistory, Array.Empty<long>()), (ChosenLoot, Array.Empty<long>()), (ChosenTreasure, Array.Empty<long>()),
            (ScenarioOverride, new long[] { 0 }), (BossLostTo, new long[] { 0 }), (HasSeenRunComplete, new long[] { 0 }),
            (IsRunRetired, new long[] { 0 }),
            // the pre-rolled anomaly becomes the run's, in anomaly mode or not (live: 243 = 392 at every recorded start; the preview
            // itself stays until the run ends)
            (CurrentAnomalyCard, new[] { anomaly }), (AnomalyPreview, new[] { anomaly }),
        };
        foreach (var (sub, values) in set) sd.SetInts(key, sub, values);
        ResetTavernState(sd, key); // (no deck enchantments, no Good Food, no tavern seen yet: HeistTavern.cs)
        Log.Info("heist", $"run started: scenario {scenario}, class {cls}, deck {deckId} ({deck.Count} cards), first boss {cards.ByDbf(first)?.CardId}");
    }

    /// <summary>A "Random" deck (live: 10 cards of any set and rarity — about 6 of the class, 4 neutral; never a premade deck).</summary>
    private List<long> RandomDeck(int cls, Random rng)
    {
        // (never a Classic-format copy, CARD_SET VANILLA: user 2026-11-14 got the old Blizzard; the real version is in the pool)
        bool Playable(CardDef c) => c.Tag(GAME_TAG.COLLECTIBLE) == 1 && c.Type is TAG_CARDTYPE.MINION or TAG_CARDTYPE.SPELL or TAG_CARDTYPE.WEAPON &&
                                    c.Tag(GAME_TAG.CARD_SET) != (int)TAG_CARD_SET.VANILLA;
        var ofClass = cards.All.Where(c => Playable(c) && c.Tag(GAME_TAG.CLASS) == cls).Select(c => (long)c.DbfId).ToList();
        var neutral = cards.All.Where(c => Playable(c) && c.Tag(GAME_TAG.CLASS) == (int)TAG_CLASS.NEUTRAL).Select(c => (long)c.DbfId).ToList();
        return ofClass.OrderBy(_ => rng.Next()).Take(6).Concat(neutral.OrderBy(_ => rng.Next()).Take(4)).OrderBy(_ => rng.Next()).ToList();
    }

    /// <summary>The boss for a level of the chapter: its pool minus the bosses already fought; the scenario's preview boss without one.</summary>
    private int PickBoss(int wing, int level, List<long> fought, Random rng, long scenario)
    {
        if (chapters.TryGetValue(wing, out Chapter? ch) && level - 1 < ch.Levels.Count)
        {
            var pool = ch.Levels[level - 1].Where(b => !fought.Contains(b)).ToList();
            if (pool.Count == 0) pool = ch.Levels[level - 1];
            if (pool.Count > 0) return pool[rng.Next(pool.Count)];
        }
        return previewBoss.GetValueOrDefault((int)scenario);
    }

    private int LevelsOf(int wing) => chapters.TryGetValue(wing, out Chapter? ch) ? ch.Levels.Count : chapterOfWing.GetValueOrDefault(wing) == 5 ? 12 : 8;

    /// <summary>After a Heist boss game: the run goes on (a win), ends as won (the last boss) or ends (a loss).</summary>
    public void GameEnded(SaveDataStore sd, long scenario, bool won, Random rng, IReadOnlyList<SabberStoneCore.Model.RunDeckEdit>? edits = null)
    {
        if (taverns.Contains((int)scenario)) { TavernEnded(sd, scenario, won, rng, edits); return; } // (HeistTavern.cs)
        if (!scenarios.TryGetValue((int)scenario, out var chapterScenario)) return;
        long key = KeyOf(scenario);
        if (One(sd, key, IsRunActive) != 1) return;
        long boss = One(sd, key, NextBossFight);
        var fought = Many(sd, key, BossesFought);
        fought.Add(boss);
        sd.SetInts(key, BossesFought, fought);
        if (!won)
        {
            EndRun(sd, key, boss, retired: false);
            return;
        }
        var defeated = Many(sd, key, BossesDefeated);
        defeated.Add(boss);
        sd.SetInts(key, BossesDefeated, defeated);
        LootAfterTavern(sd, key, fought); // (the stage after a tavern had no loot: a 0 in LOOT_HISTORY — HeistTavern.cs)
        int cls = (int)One(sd, key, DeckClass);
        if (ClassWins.TryGetValue(cls, out var wins)) sd.SetInts(key, wins.Boss, new[] { One(sd, key, wins.Boss) + 1 });
        sd.SetInts(key, TotalBossWins, new[] { One(sd, key, TotalBossWins) + 1 });
        int wing = chapterScenario.Wing, level = defeated.Count;
        if (level >= LevelsOf(wing))
        {
            // the chapter is cleared: the run is won
            if (ClassWins.TryGetValue(cls, out wins)) sd.SetInts(key, wins.Run, new[] { One(sd, key, wins.Run) + 1 });
            sd.SetInts(key, TotalRunWins, new[] { One(sd, key, TotalRunWins) + 1 });
            int chapter = chapterOfWing.GetValueOrDefault(wing);
            if (chapter is >= 1 and <= 5)
            {
                Append(sd, key, HeroPowerWingWins + chapter - 1, One(sd, key, HeroPower));
                Append(sd, key, DeckWingWins + chapter - 1, One(sd, key, SelDeck));
                Append(sd, key, HeroCardWingWins + chapter - 1, One(sd, key, RunHeroCard));
            }
            EndRun(sd, key, 0, retired: false);
            Log.Info("heist", $"run won: chapter {chapter}, {level} bosses");
            return;
        }
        sd.SetInts(key, HeroHealth, new[] { One(sd, key, HeroHealth) + HealthPerWin });
        int nextBoss = PickBoss(wing, level + 1, fought, rng, scenario);
        sd.SetInts(key, NextBossFight, Enumerable.Repeat((long)nextBoss, 5));
        sd.SetInts(key, NextBossHealth, new long[] { BossHealth[Math.Min(level, BossHealth.Length - 1)] });
        // three loot bundles of the class: [bundle, three of its cards]
        var offers = bundles.GetValueOrDefault(cls, new List<Bundle>()).OrderBy(_ => rng.Next()).Take(3).ToList();
        long[] lootKeys = { LootA, LootB, LootC };
        for (int i = 0; i < 3; i++)
            sd.SetInts(key, lootKeys[i], i < offers.Count
                ? new[] { (long)offers[i].Dbf }.Concat(offers[i].Cards.OrderBy(_ => rng.Next()).Take(3).Select(c => (long)c))
                : Array.Empty<long>());
        // a treasure offer after bosses 1, 3, 5 and 7: three of the pool for that boss the run doesn't already hold
        var held = Many(sd, key, DeckCardList).ToHashSet();
        var treasures = TreasureAfter.Contains(level) && treasureOffers.TryGetValue(level, out var pool)
            ? pool.Where(t => !held.Contains(t)).OrderBy(_ => rng.Next()).Take(3).Select(t => (long)t).ToList() : new List<long>();
        sd.SetInts(key, TreasureOption, treasures);
        sd.SetInts(key, ChosenLoot, Array.Empty<long>());
        sd.SetInts(key, ChosenTreasure, Array.Empty<long>());
        OfferTavern(sd, key, level, chapterScenario.Heroic); // (after bosses 3 and 5: Bob's Tavern first — HeistTavern.cs)
        Log.Info("heist", $"boss {level} defeated; next {cards.ByDbf(nextBoss)?.CardId} at {BossHealth[Math.Min(level, BossHealth.Length - 1)]} Health, " +
                          $"{offers.Count} loot bundles, {treasures.Count} treasures");
    }

    /// <summary>The client wrote save data: apply a loot / treasure pick, or a retire.</summary>
    public void ClientWrote(SaveDataStore sd, IEnumerable<(long Key, long Subkey)> written)
    {
        foreach (var (key, sub) in written.Distinct())
        {
            if (!serverKey.ContainsValue(key) || One(sd, key, IsRunActive) != 1) continue;
            if (sub == ChosenTreasure && One(sd, key, ChosenTreasure) is >= 1 and <= 3 and long t)
            {
                var options = Many(sd, key, TreasureOption);
                if (t <= options.Count) Append(sd, key, DeckCardList, options[(int)t - 1]);
                sd.SetInts(key, TreasureOption, Array.Empty<long>());
            }
            else if (sub == ChosenLoot && One(sd, key, ChosenLoot) is >= 1 and <= 3 and long l)
            {
                var bundle = Many(sd, key, new[] { LootA, LootB, LootC }[l - 1]);
                if (bundle.Count > 1)
                {
                    var deck = Many(sd, key, DeckCardList);
                    deck.AddRange(bundle.Skip(1));
                    sd.SetInts(key, DeckCardList, deck);
                    Append(sd, key, LootHistory, bundle[0]);
                }
                foreach (long k in new[] { LootA, LootB, LootC }) sd.SetInts(key, k, Array.Empty<long>());
            }
            else if (sub == IsRunRetired && One(sd, key, IsRunRetired) == 1)
            {
                EndRun(sd, key, 0, retired: true);
                Log.Info("heist", "run retired");
            }
        }
    }

    /// <summary>The run is over: active off, the picks cleared; the deck and the defeated bosses stay for the results screen.</summary>
    private void EndRun(SaveDataStore sd, long key, long lostTo, bool retired)
    {
        sd.SetInts(key, IsRunActive, new long[] { 0 });
        sd.SetInts(key, BossLostTo, new[] { lostTo });
        foreach (long sub in new[] { SelHeroPower, SelDeck, SelScenario, SelHeroClass, SelAnomaly })
            sd.SetInts(key, sub, new long[] { 0 });
        foreach (long sub in new[] { BossesFought, LootA, LootB, LootC, TreasureOption, LootHistory, ChosenLoot, ChosenTreasure })
            sd.SetInts(key, sub, Array.Empty<long>());
        sd.SetInts(key, ScenarioOverride, new long[] { 0 });
        sd.SetInts(key, NextBossFight, Previews());
        // the next run's anomaly, shown from now on (live: a new one at every recorded run end, never the one just used — 22 of 22);
        // CURRENT_ANOMALY_MODE_CARD and IS_ANOMALY_MODE keep the last run's
        sd.SetInts(key, AnomalyPreview, new[] { RollAnomaly(new Random(), One(sd, key, CurrentAnomalyCard)) });
        sd.SetInts(key, HasSeenRunComplete, new long[] { 0 });
        if (!retired) sd.SetInts(key, IsRunRetired, new long[] { 0 });
    }

    /// <summary>A random anomaly of the pool other than <paramref name="last"/> (0 without a pool).</summary>
    private long RollAnomaly(Random rng, long last)
    {
        var pool = anomalies.Where(a => a != last).ToList();
        if (pool.Count == 0) pool = anomalies;
        return pool.Count == 0 ? 0 : pool[rng.Next(pool.Count)];
    }

    /// <summary>Between runs live shows 5 bosses: a first boss of each chapter.</summary>
    private List<long> Previews()
    {
        var rng = new Random();
        return chapterOfWing.OrderBy(c => c.Value).Take(5)
            .Select(c => (long)(chapters.TryGetValue(c.Key, out Chapter? ch) && ch.Levels.Count > 0 && ch.Levels[0].Count > 0
                ? ch.Levels[0][rng.Next(ch.Levels[0].Count)]
                : previewBoss.FirstOrDefault(p => scenarios.TryGetValue(p.Key, out var s) && s.Wing == c.Key).Value)).ToList();
    }

    /// <summary>No run in progress (a new player — the recordings' last run is not theirs — or an admin reset): progress and unlocks
    /// kept.</summary>
    public void Clean(SaveDataStore sd, bool? heroic = null)
    {
        foreach (var (isHeroic, key) in serverKey)
        {
            if (heroic != null && heroic != isHeroic) continue;
            EndRun(sd, key, 0, retired: false);
            sd.SetInts(key, DeckCardList, Array.Empty<long>());
            sd.SetInts(key, BossesDefeated, Array.Empty<long>());
            sd.SetInts(key, HasSeenRunComplete, new long[] { 1 }); // (nothing to show)
        }
    }

    private static void Append(SaveDataStore sd, long key, long sub, long value)
    {
        var list = Many(sd, key, sub);
        list.Add(value);
        sd.SetInts(key, sub, list);
    }
}
