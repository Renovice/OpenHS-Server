using HsServer.Lobby;
using HsServer.Protocol;

namespace HsServer;

internal static class Program
{
    private static int Main(string[] args)
    {
        GameAssemblies.Initialize();
        string command = args.Length > 0 ? args[0] : "serve";
        string[] rest = args.Skip(1).ToArray();
        return command switch
        {
            "serve" => Serve(rest),
            "analyze" => Tools.Analyze.Run(rest),
            "dump" => Tools.Analyze.Dump(rest),
            "timeline" => Tools.TimelineReport.Run(rest),
            "local" => Tools.LocalRun.Run(rest),
            "cards" => Tools.CardCoverage.Run(rest),
            "soak" => Tools.Soak.Run(rest),
            "trace" => Tools.TraceGame.Run(rest),
            "keywords" => Tools.KeywordLibrary.Run(rest),
            "events" => Tools.EventList.Run(rest),
            "card" => Tools.CardInfo.Run(rest),
            "power" => Tools.PowerDump.Run(rest),
            "hsreplay" => Tools.HsReplayImport.Run(rest),
            "update" => Tools.UpdateClient.Run(rest),
            "deck" => Tools.DeckTool.Run(rest),
            "tagdiff" => Tools.TagDiff.Run(rest),
            "hsjson" => Tools.HsJsonCheck.Run(rest),
            "wirecheck" => Tools.WireCheck.Run(rest),
            "viewcheck" => Tools.ViewCheck.Run(rest),
            "vfxcheck" => Tools.VfxCheck.Run(rest),
            "vfxlib" => Tools.VfxLibrary.Run(rest),
            "golden" => Tools.Golden.Run(rest),
            "livecheck" => Tools.LiveCheck.Run(rest),
            "arena" => Tools.Arena.Run(rest),
            "admin" => Admin.AdminTool.Run(rest),
            _ => Usage(),
        };
    }

    internal static int Serve(string[] args)
    {
        Log.Verbose = args.Contains("--verbose");
        string root = ProjectRoot();
        bool noRecording = args.Contains("--no-recording");
        ServerData data = ServerData.Load(root, Option(args, "--account"), noRecording);
        if (data == null)
        {
            Log.Error("server", "no recorded login session found (need an HsRecorder session containing util 711)");
            return 1;
        }
        string reference = data.Reference, dbfDir = data.DbfDir;
        RecordedAccount account = data.Account;
        var cards = data.Cards;
        var grantedCards = data.GrantedCards;
        var calendar = data.Calendar;
        var crafting = data.Crafting;
        var accounts = data.Accounts;
        Log.Info("server", $"event calendar: {calendar.Count} events from {Path.GetFileName(reference)}");
        PlayerAccount defaultPlayer = accounts.Get(AccountStore.DefaultName);
        Log.Info("server", $"account state from {Path.GetFileName(reference)}: {account.Util.Count} util request types, " +
                           $"{account.Util.Values.Sum(l => l.Count)} exchanges, {defaultPlayer.SaveData.KeyCount} save keys (default player)");

        var sessions = noRecording ? Array.Empty<string>() : Recording.FindSessions(
            Path.Combine(root, "Hearthstone main game", "BepInEx", "HsRecorder"),
            Path.Combine(root, "RESEARCH", "captures"));
        var matches = Game.RecordedMatch.LoadAll(sessions);
        Log.Info("server", $"{matches.Count} recorded matches available for replay " +
                           $"(scenarios: {string.Join(",", matches.Select(m => m.Scenario).Distinct().OrderBy(s => s))})");

        int lobbyPort = int.Parse(Option(args, "--lobby-port") ?? "1119");
        int gamePort = int.Parse(Option(args, "--game-port") ?? "3724");
        var gameOptions = new Game.GameOptions
        {
            AutoplaySeconds = Option(args, "--autoplay") is string a ? double.Parse(a, System.Globalization.CultureInfo.InvariantCulture) : null,
            UseEngine = args.Contains("--engine"),
        };
        DeckStore decks = defaultPlayer.Decks;
        Log.Info("server", $"deck store (default player): {decks.Count} decks");

        // the scenario's adventure (client Scenario.AdventureId): 2 = Practice, other > 0 = an adventure (solo)
        var scenarioAdventure = LoadScenarioField(Path.Combine(dbfDir, "Scenario.json"), "AdventureId");
        Func<Game.QueueRequest, Game.EngineSetup> engineFactory = null;
        if (gameOptions.UseEngine)
        {
            var scenarioHeroes = LoadScenarioHeroes(Path.Combine(dbfDir, "Scenario.json"));
            var scenarioModes = LoadScenarioField(Path.Combine(dbfDir, "Scenario.json"), "ModeId"); // 1 Normal, 2 Expert
            byte[] gameSetup = matches.SelectMany(m => m.Events).FirstOrDefault(e => e.FromServer && e.Type == 16)?.Body
                ?? PegasusPackets.Serialize(new PegasusGame.GameSetup { Board = 8, MaxSecretZoneSizePerPlayer = 5, MaxSecretsPerPlayer = 5,
                    MaxQuestsPerPlayer = 3, MaxFriendlyMinionsPerPlayer = 7, KeepAliveFrequencySeconds = 30, DisconnectWhenStuckSeconds = 120 });
            ulong accountHi = (2UL << 56) | ((ulong)account.Region << 32) | BgsRoutes.TitleId;
            var rules = string.Equals(Option(args, "--rules"), "sabber", StringComparison.OrdinalIgnoreCase) ? Game.RulesEngine.Sabber : Game.RulesEngine.Basic;
            if (rules == Game.RulesEngine.Sabber) Sabber.SabberEngineGame.EnsureCards(dbfDir, calendar.IsActive);
            Log.Info("server", $"engine enabled: {cards.Count} cards from {Path.GetFileName(dbfDir)}, rules: {rules}");
            Log.Info("server", $"collection: {grantedCards.Count} Core cards owned by every player");
            engineFactory = queued =>
            {
                // The player's chosen deck from their deck store; the basic deck when it is unknown or empty.
                DeckStore playerDecks = queued.Player?.Decks ?? decks;
                List<string> deck = playerDecks.CardIds(queued.DeckId).Select(cards.ByDbf).Where(c => c != null).Select(c => c.CardId).ToList();
                // the deck's sideboards (E.T.C.'s band, Zilliax's modules, Beatrix's minion, King's Beasts), checked again
                List<(string Owner, string Card)> SideboardOf(DeckStore store, long deckId) => (data.Sideboards?.Valid(store.CardIds(deckId),
                        store.Sideboard(deckId), why => Log.Warn("deck", $"deck {deckId}: {why}")) ?? store.Sideboard(deckId).ToList())
                    .Select(s => (Owner: cards.ByDbf(s.Owner)?.CardId, Card: cards.ByDbf(s.Card)?.CardId))
                    .Where(s => s.Owner != null && s.Card != null).ToList();
                var sideboard = deck.Count > 0 ? SideboardOf(playerDecks, queued.DeckId) : new List<(string Owner, string Card)>();
                if (deck.Count == 0) deck = Engine.Decks.BasicDeck(cards);
                // B2: the client only queues decks the player owns; a deck that isn't is played anyway, but logged
                // (live's refusal can't be recorded: the client never sends one).
                if (queued.Player?.Collection is { } owned && owned.Missing(playerDecks.CardIds(queued.DeckId)) is { Count: > 0 } missing)
                    Log.Warn("collection", $"player {queued.Player.Name} deck {queued.DeckId}: not owned " +
                                           string.Join(", ", missing.Select(m => $"{cards.ByDbf(m.Dbf)?.CardId ?? m.Dbf.ToString()} x{m.Missing}")));
                string heroId = new[] { (int)queued.HeroCardDbId, playerDecks.Get(queued.DeckId)?.Hero ?? 0 }
                    .Select(cards.ByDbf).FirstOrDefault(c => c != null)?.CardId ?? "HERO_01";
                string opponentHero = scenarioHeroes.TryGetValue(queued.Scenario, out int dbf) && cards.ByDbf(dbf) is { } hero ? hero.CardId : "HERO_08";
                // Rules engine: the opponent plays a Basic + Classic deck of its hero's class; the basic engine keeps its small deck.
                // Practice: one of the player's own decks when `admin practice decks <name start>` is set (Game.PracticeDecks).
                string ClassOf(string cardId) => cardId != null && SabberStoneCore.Model.Cards.TryFromId(cardId) is { } c ? c.Class.ToString() : null;
                long? practiceDeck = rules == Game.RulesEngine.Sabber && scenarioAdventure.GetValueOrDefault(queued.Scenario) == 2
                    ? Game.PracticeDecks.PickDeck(Game.PracticeDecks.Prefix, playerDecks, queued.DeckId, dbf => ClassOf(cards.ByDbf(dbf)?.CardId),
                        ClassOf(opponentHero), new Random())
                    : null;
                List<string> opponentDeck = practiceDeck is long chosen
                    ? playerDecks.CardIds(chosen).Select(cards.ByDbf).Where(c => c != null).Select(c => c.CardId).ToList()
                    : null;
                var opponentSideboard = practiceDeck is long withSideboard ? SideboardOf(playerDecks, withSideboard) : new List<(string Owner, string Card)>();
                opponentDeck ??= rules == Game.RulesEngine.Sabber && SabberStoneCore.Model.Cards.TryFromId(opponentHero) is { } oh
                    ? Sabber.RandomDecks.Build(oh.Class, new Random(), SabberStoneCore.Enums.CardSet.CORE, SabberStoneCore.Enums.CardSet.EXPERT1)
                    : Engine.Decks.BasicDeck(cards);
                // (2026-10-09) the Dalaran Heist: the run's deck, hero, Hero Power and Health against its next boss (Lobby/HeistRuns)
                if (rules == Game.RulesEngine.Sabber && data.Heist?.IsHeist(queued.Scenario) == true && queued.Player?.SaveData is { } heistSave &&
                    data.Heist.Prepare(heistSave, queued.Scenario, queued.HeroCardDbId, new Random()) is var (runHuman, runBoss))
                {
                    runHuman.AccountHi = accountHi;
                    runHuman.AccountLo = queued.Player?.GameAccountId ?? account.GameAccountId;
                    // the boss's deck: from boss-decks*.json (HeistRuns); a boss with none known gets a deck of its class
                    var bossClass = SabberStoneCore.Model.Cards.TryFromId(runBoss.HeroCardId)?.Class ?? SabberStoneCore.Enums.CardClass.MAGE;
                    if (bossClass is SabberStoneCore.Enums.CardClass.NEUTRAL or SabberStoneCore.Enums.CardClass.INVALID) bossClass = SabberStoneCore.Enums.CardClass.MAGE;
                    if (runBoss.Deck.Count == 0) runBoss.Deck = Sabber.RandomDecks.Build(bossClass, new Random(), SabberStoneCore.Enums.CardSet.CORE, SabberStoneCore.Enums.CardSet.EXPERT1);
                    return new Game.EngineSetup
                    {
                        Cards = cards, Scenario = (int)queued.Scenario, Rules = rules,
                        GameSetupBody = Game.Boards.WithRandomBoard(gameSetup, new Random()),
                        Mode = scenarioModes.TryGetValue(queued.Scenario, out int heistMode) ? heistMode : 0,
                        TurnTimer = false, // (solo: no rope)
                        Human = runHuman, Opponent = runBoss,
                        ScenarioTags = data.Heist.ScenarioTagsFor(queued.Scenario), // (Bob's Tavern's own tags: Lobby/HeistTavern.cs)
                    };
                }
                return new Game.EngineSetup
                {
                    Cards = cards, Scenario = (int)queued.Scenario, Rules = rules,
                    GameSetupBody = Game.Boards.WithRandomBoard(gameSetup, new Random()), // a random expansion board per game, as live
                    Mode = scenarioModes.TryGetValue(queued.Scenario, out int mode) ? mode : 0,
                    // the rope in Ranked / Casual-like games; solo modes (Practice, adventures) have none (wiki Turn)
                    TurnTimer = scenarioAdventure.GetValueOrDefault(queued.Scenario) == 0,
                    Human = new Engine.PlayerSetup { HeroCardId = heroId, Deck = deck, Sideboard = sideboard, AccountHi = accountHi, AccountLo = queued.Player?.GameAccountId ?? account.GameAccountId,
                        CoinCardId = Lobby.CoinSkins.CardOf(dbfDir, cards, playerDecks.Get(queued.DeckId)?.CosmeticCoin ?? 0, queued.Player?.Collection?.FavoriteCoinIds(), new Random()) },
                    Opponent = new Engine.PlayerSetup { HeroCardId = opponentHero, Deck = opponentDeck, Sideboard = opponentSideboard, IsAi = true },
                };
            };
        }
        var allocations = new Game.GameAllocations();
        var context = new LobbyContext
        {
            Account = account, SaveData = defaultPlayer.SaveData, Decks = decks, Matchmaker = new Game.Matchmaker(allocations, matches, gamePort, engineFactory, GameOver),
            GrantedCards = grantedCards, Accounts = accounts, Crafting = crafting, Calendar = calendar, Boosters = data.Boosters,
            Orders = data.Orders, RewardTracks = data.RewardTracks, Zilliax = data.Zilliax, Quests = data.Quests, LuckyDraw = data.LuckyDraw, BlackMarket = data.BlackMarket, Ranked = data.Ranked, Heist = data.Heist,
            // (2026-10-09, user: "report it as owned so it shows up") Naxxramas's wings (client Wing.json, adventure 3)
            GrantedWings = Lobby.AdventureWings.All(dbfDir),
        };

        // PlayerRecords keep PegasusShared.GameType: the matchmaking BnetGameType mapped (practice = GT_VS_AI)
        static int RecordGameType(long bnetGameType) => bnetGameType switch
        {
            2 or 30 => (int)PegasusShared.GameType.GT_RANKED,
            10 or 31 => (int)PegasusShared.GameType.GT_CASUAL,
            16 => (int)PegasusShared.GameType.GT_TAVERNBRAWL,
            17 => (int)PegasusShared.GameType.GT_TB_1P_VS_AI,
            18 => (int)PegasusShared.GameType.GT_TB_2P_COOP,
            _ => (int)PegasusShared.GameType.GT_VS_AI,
        };

        // C4: after an engine game, its reward-track XP and quest progress (live: 614 + 617 with the game's and the completed
        // quests' XP, then 601 with the quests' progress)
        async Task GameOver(Game.QueueRequest queued, Game.GameOutcome outcome)
        {
            // (2026-10-09) the Dalaran Heist: the run goes on, is won or ends (Lobby/HeistRuns)
            if (data.Heist?.IsHeist(queued.Scenario) == true && queued.Player?.SaveData is { } heistSave)
                data.Heist.GameEnded(heistSave, queued.Scenario, outcome.Won, new Random(), outcome.RunDeckEdits);
            // Bob's Tavern: the client leaves at once (no end screen, no rewards: live DALA_Tavern.NotifyOfGameOver)
            if (data.Heist?.IsTavern(queued.Scenario) == true) return;
            if (queued.Player?.Collection is not { } collection || data.RewardTracks == null) return;
            long type = queued.GameType; // BnetGameType
            int adventure = scenarioAdventure.GetValueOrDefault(queued.Scenario);
            bool counts = outcome.Tally?.CountsAsPlayed ?? true; // live: an early concede gives no XP at all
            int xp = counts ? RewardTrackRules.GameXp(type, outcome.Duration, outcome.Won, practice: adventure == 2) : 0;
            if (outcome.Tally != null) outcome.Tally.GameType = type;
            var (questUpdate, completed) = data.Quests?.Progress(collection, outcome.Tally) ?? (null, new List<QuestRules.Quest>());
            var grants = new List<(int, int, int)>();
            if (xp > 0) grants.Add((xp, RewardTracks.FromGame, 0)); // live: no 617 for a game without XP
            grants.AddRange(Quests.XpGrants(completed));
            List<IProtoBuf> changes = data.RewardTracks.AddXp(collection, grants);
            if (questUpdate != null) changes.Add(questUpdate);
            if (data.Quests != null) changes.AddRange(data.Quests.Rewards(queued.Player, completed, data.RewardTracks)); // event chains' reward lists
            // C4: the win / loss records (live: the hero's and the type's total, then the whole list, 270)
            if (outcome.Tally?.HeroCardId is { } hero && data.Cards.TryGet(hero, out Engine.CardDef heroCard))
                changes.Add(collection.AddResult(RecordGameType(type), heroCard.DbfId, outcome.Won));
            // C4: class XP for the hero's class (granted now; the end screen asks for HERO_XP next and animates the change)
            if (outcome.Tally is { HeroClass: > 0 } played)
            {
                int classXp = ClassXpRules.GameXp(played, solo: adventure > 0);
                var (level, had, newLevel, newXp) = collection.AddClassXp(played.HeroClass, classXp);
                Log.Info("progress", $"{queued.Player.Name}: class {played.HeroClass} +{classXp} XP (level {level} {had} -> level {newLevel} {newXp})");
            }
            // the Ranked ladder: stars, streak, season wins / games (the end screen asks for the medal next), the season card back
            if (data.Ranked?.GameEnded(queued.Player, type, outcome.Won, RankedLadder.Recorded(data.Account)) is { Log: not null } ranked)
            {
                changes.AddRange(ranked.Changes);
                Log.Info("progress", $"{queued.Player.Name}: Ranked {ranked.Log}");
            }
            // D1: Bloodstones for a Ranked game (BGT_RANKED_STANDARD 2, WILD 30, CLASSIC 58, TWIST 63), the 728 the
            // end-of-game screen shows
            if (type is 2 or 30 or 58 or 63 && data.BlackMarket?.RankedGameEnded(queued.Player, outcome.Won) is { } bloodstones)
            {
                changes.AddRange(bloodstones.Changes);
                changes.Add(bloodstones.Response);
                Log.Info("progress", $"{queued.Player.Name}: Black Market {bloodstones.Log}");
            }
            foreach (IProtoBuf change in changes) await queued.Player.NotifyAsync(change);
            foreach (QuestRules.Quest q in completed)
                Log.Info("progress", $"{queued.Player.Name}: quest {q.Id} done ({q.Description?.Replace("$q", q.Quota.ToString())}) -> " +
                                     (q.Xp > 0 ? $"{q.Xp} XP" : $"reward list {q.RewardList} (not granted yet)"));
            if (changes.OfType<PegasusUtil.RewardTrackXpNotification>().FirstOrDefault()?.XpChange.FirstOrDefault() is { } x)
                Log.Info("progress", $"{queued.Player.Name}: game type {type} {(outcome.Won ? "won" : "lost")} in {outcome.Duration.TotalMinutes:0.0} min -> {xp} XP before the pass boost " +
                                     $"(level {x.PrevLevel} {x.PrevXp} -> level {x.CurrLevel} {x.CurrXp})");
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        int adminPort = int.Parse(Option(args, "--admin-port") ?? Admin.AdminServer.DefaultPort.ToString());
        Admin.AdminConsole.Start(new Admin.AdminCommands(data), cts); // commands typed into this window
        Task.WaitAll(
            new LobbyServer(context, lobbyPort).RunAsync(cts.Token),
            new Game.GameServer(allocations, gameOptions, gamePort).RunAsync(cts.Token),
            new Admin.AdminServer(new Admin.AdminCommands(data), adminPort).RunAsync(cts.Token),
            new Shop.CommerceServer(data, data.Shop, int.Parse(Option(args, "--shop-port") ?? Shop.CommerceServer.DefaultPort.ToString())).RunAsync(cts.Token));
        return 0;
    }

    // Scenario → opponent hero (Player2HeroCardId, a card dbf id) from the DBF export.
    // Scenario id → an integer field of its DBF record (ModeId...).
    private static Dictionary<long, int> LoadScenarioField(string scenarioJson, string field)
    {
        var map = new Dictionary<long, int>();
        if (!File.Exists(scenarioJson)) return map;
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(scenarioJson));
        foreach (var r in doc.RootElement.GetProperty("records").EnumerateArray())
            if (r.TryGetProperty(field, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number)
                map[r.GetProperty("ID").GetInt64()] = v.GetInt32();
        return map;
    }

    private static Dictionary<long, int> LoadScenarioHeroes(string scenarioJson)
    {
        var map = new Dictionary<long, int>();
        if (!File.Exists(scenarioJson)) return map;
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(scenarioJson));
        foreach (var r in doc.RootElement.GetProperty("records").EnumerateArray())
            if (r.TryGetProperty("Player2HeroCardId", out var h))
                map[r.GetProperty("ID").GetInt64()] = h.GetInt32();
        return map;
    }

    internal static string ProjectRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "Hearthstone main game"))) return dir.FullName;
        return Directory.GetCurrentDirectory();
    }

    private static string Option(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage: HsServer serve [--account <session.jsonl>] [--lobby-port 1119] [--game-port 3724] [--autoplay <seconds>] [--engine [--rules basic|sabber]] [--verbose]");
        Console.Error.WriteLine("       HsServer local [--replay] [--no-client] [--autoplay <seconds>] | local --stop   (used by run-local.bat)");
        Console.Error.WriteLine("       HsServer analyze <session.jsonl> [maxLines]");
        Console.Error.WriteLine("       HsServer timeline [--detail] <session.jsonl>...");
        Console.Error.WriteLine("       HsServer cards [--missing] [SET...]   (rules engine card coverage; default CORE EXPERT1)");
        Console.Error.WriteLine("       HsServer cards --ids <file>   (each card id in the file: known / implemented, type, name, text)");
        Console.Error.WriteLine("       HsServer soak [games] [seed]           (random Basic+Classic games through the client adapter)");
        Console.Error.WriteLine("       HsServer keywords [--cards]           (keyword library: current cards' keywords vs engine support)");
        Console.Error.WriteLine("       HsServer wirecheck [games] [seed]     (tags live sends that our server never does; report in RESEARCH/engine-audit)");
        Console.Error.WriteLine("       HsServer vfxcheck [file] [--out f] [--card ID] (plays each recorded card: does our server send the effects live sent?)");
        Console.Error.WriteLine("       HsServer vfxlib [--out dir] (every collectible card's visual-effect status -> RESEARCH/vfx/library.md + library.tsv)");
        Console.Error.WriteLine("       HsServer golden [--update] [--card ID] [--set NAME] [--jobs N] (every card's behaviour vs the accepted baseline -> RESEARCH/golden/last-run.md)");
        Console.Error.WriteLine("       HsServer livecheck [--card ID] [--set NAME] [--kind play|attack|end-turn] [--limit N] [--session FILE] [--line N] [--captures DIR] [--verbose] [--detail] (every live action redone in our engine from the live position: same outcome? -> RESEARCH/livecheck/last-run.md)");
        Console.Error.WriteLine("       HsServer arena [games] [seed] [aiA] [aiB]  (AI vs AI: win rate and time per decision/turn)");
        Console.Error.WriteLine("       HsServer power <session.jsonl> [from=HH:mm:ss] [to=HH:mm:ss]  (recorded game packets as Power.log text)");
        Console.Error.WriteLine("       HsServer deck list | deck add <name> <heroCardId> [cardId[:copies]]... [seed=N]  (local server decks)");
        Console.Error.WriteLine("       HsServer dump <session.jsonl> <seq>...");
        Console.Error.WriteLine("       HsServer update [--yes] [--live <folder>] [--no-client-sync]  (update.bat: after a patch, private client + new card database)");
        Console.Error.WriteLine("       HsServer hsjson [--build N] [--dbf DIR] [--xml FILE] [--offline]  (our card database vs HearthSim's HearthstoneJSON for the same build; run by update)");
        return 2;
    }
}
