using HsServer.Engine;
using HsServer.Game;
using PegasusGame;
using SabberStoneCore.Config;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Loader;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;
using SGame = SabberStoneCore.Model.Game;

namespace HsServer.Sabber;

/// <summary>
/// A game on the SabberStone-based rules engine (HsServer.Rules), behind <see cref="IEngineGame"/>. The human is
/// player 1 and sees the game through <see cref="ClientStream"/>; player 2 is <see cref="SabberAi"/>.
/// The client's options map back to engine moves by (entity, target, choose-one option, board position).
/// </summary>
internal sealed class SabberEngineGame : IEngineGame
{
    /// <summary>A stack frame as "Method (File.cs:line N)" for the engine-error log.</summary>
    private static string Frame(string line)
    {
        string[] parts = line.Trim().Split(" in ");
        return parts.Length < 2 ? parts[0] : $"{parts[0]} ({Path.GetFileName(parts[1].Split(":line")[0])}:{parts[1].Split(":line").ElementAtOrDefault(1)?.Trim()})";
    }

    private static readonly object CardsInit = new();
    private static bool cardsReady;

    private readonly EngineSetup setup;
    private SGame game;
    private ClientStream stream;
    private int historyCursor;
    private Func<int, (ulong Hi, ulong Lo, int CardBack)> accounts;

    // ---------- Rewind (Across the Timeways): the game before a Rewind card's play, and the play to make again ----------
    private SGame rewindPoint;
    private (bool HeroPower, int Source, int Target, int Position, int ChooseOne) rewindMove;
    private int rewindChoiceId;
    private readonly List<IProtoBuf> outbox = new();

    private int optionsId;
    private PowerAllOptions sentOptions;
    private int nextChoiceId = 1;
    private (int Id, Choice Choice)? humanChoice;
    private Choice aiChoiceSent;

    public SabberEngineGame(EngineSetup setup)
    {
        this.setup = setup;
        ai = Ai.GameAis.ForMode(setup.Mode);
    }

    /// <summary>Point the rules engine's card loader at the current client's DBF (reprints) — once per process.</summary>
    /// <param name="isActive">the server's event calendar (which Core set is current); null = the reference one</param>
    public static void EnsureCards(string dbfDirectory, Func<string, bool> isActive = null)
    {
        lock (CardsInit)
        {
            if (cardsReady) return;
            ReprintCards.DbfDirectory ??= dbfDirectory;
            // (2026-11-20) the card numbers follow the current texts: the baseline next to the snapshots (CardValues)
            SabberStoneCore.Loader.CardValues.BaselineFile ??= Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ReprintCards.DbfDirectory)) ?? "", "card-values-baseline.tsv");
            // Current Core cards under their own ids (CS3_, RLK_...) are added from the client DB too.
            var clientCards = CardDb.Load(ReprintCards.DbfDirectory);
            var sets = ClientCardSets.Load(ReprintCards.DbfDirectory, isActive);
            ReprintCards.ExtraCardIds = clientCards.All.Where(c => sets.IsCurrentCore(c.DbfId)).Select(c => c.CardId).ToHashSet();
            // (2026-10-09) a card's set now: the calendar picks among its dated CardSetTiming rows (ReprintCards.SetOf)
            ReprintCards.IsEventActiveNow ??= sets.IsActive;
            _ = Cards.All.Count();
            cardsReady = true;
        }
    }

    /// <summary>The board id in the GameSetup the client gets (0 when none).</summary>
    private static int BoardOf(byte[] gameSetup)
    {
        try { return gameSetup == null ? 0 : HsServer.Protocol.PegasusPackets.Deserialize<PegasusGame.GameSetup>(gameSetup).Board; }
        catch { return 0; }
    }

    /// <summary>The engine's own game (tests compare it with what the client was told).</summary>
    internal SGame Rules => game;

    public bool Over => game.State == State.COMPLETE;
    /// <summary>(2026-10-09, Dalaran Heist) what the game did to the human's adventure-run deck (Bob's Tavern), in order.</summary>
    public IReadOnlyList<SabberStoneCore.Model.RunDeckEdit> RunDeckEdits => setup.Human.RunDeck == null ? null : game?.RunDeckEdits;
    public bool HumanWon => game.Player1.PlayState == PlayState.WON;
    public bool AiToAct => !Over && humanChoice == null && (game.Player2.Choice != null || game.CurrentPlayer == game.Player2 || HumanControlledByAi);

    /// <summary>(2026-11-19) Mastermind Yogg-Saron plays the human's extra turn (Generic.AiControls: AI_MAKES_DECISIONS_FOR_PLAYER): the AI
    /// makes their moves and choices, the client gets no options.</summary>
    private bool HumanControlledByAi => SabberStoneCore.Actions.Generic.AiControls(game.Player1) && (game.Player1.Choice != null || game.CurrentPlayer == game.Player1);
    public string Describe => $"{game.Player1.Hero.Card.Id} vs {game.Player2.Hero.Card.Id} (rules engine)";

    public void Start()
    {
        EnsureCards(CardDb.FindDefaultDir(Program.ProjectRoot()));
        Card hero1 = HeroCard(setup.Human.HeroCardId), hero2 = HeroCard(setup.Opponent.HeroCardId);
        var (deck1, run1) = RunDeckOf(setup.Human); // (2026-10-09, Dalaran Heist: the deck's run slots and enchantments)
        game = new SGame(new GameConfig
        {
            StartPlayer = 1,
            Player1Name = "Player", Player2Name = "The Innkeeper",
            Player1HeroCard = hero1, Player1HeroClass = hero1.Class,
            Player2HeroCard = hero2, Player2HeroClass = hero2.Class,
            Player1HeroPowerCard = setup.Human.HeroPowerCardId == null ? null : Cards.TryFromId(setup.Human.HeroPowerCardId),
            Player2HeroPowerCard = setup.Opponent.HeroPowerCardId == null ? null : Cards.TryFromId(setup.Opponent.HeroPowerCardId),
            Player1StartHealth = setup.Human.StartHealth, Player2StartHealth = setup.Opponent.StartHealth,
            Player1PlotTwist = setup.Human.PlotTwistCardId == null ? null : Cards.TryFromId(setup.Human.PlotTwistCardId), // (Dalaran Heist)
            Player2PlotTwist = setup.Opponent.PlotTwistCardId == null ? null : Cards.TryFromId(setup.Opponent.PlotTwistCardId),
            Player1Anomaly = setup.Human.AnomalyCardId == null ? null : Cards.TryFromId(setup.Human.AnomalyCardId), // (Heist Anomaly Mode)
            Player2Anomaly = setup.Opponent.AnomalyCardId == null ? null : Cards.TryFromId(setup.Opponent.AnomalyCardId),
            Player1Deck = deck1, Player2Deck = Deck(setup.Opponent.Deck),
            Player1RunDeck = run1, Scenario = ScenarioOf(setup.ScenarioTags), // (2026-10-09: Bob's Tavern's own tags)
            Player1Sideboard = Sideboard(setup.Human.Sideboard), Player2Sideboard = Sideboard(setup.Opponent.Sideboard),
            Player1CoinCard = setup.Human.CoinCardId, Player2CoinCard = setup.Opponent.CoinCardId, // live: the player's coin skin
            Player2Coin = false, // live: the AI going second gets no Coin (GameConfig.Player2Coin)
            FormatType = FormatType.FT_WILD,
            Board = BoardOf(setup.GameSetupBody), // the board the client is told (Transfer Student's version)
            FillDecks = false, Shuffle = true, SkipMulligan = false,
            History = true, Logging = false,
            RandomSeed = setup.Seed, // the same seed replays the same game (shuffles, random effects)
            TurnTimer = setup.TurnTimer, // live solo games (GT_VS_AI) send no TIMEOUT
        });
        accounts = playerId => playerId == 1
            ? (setup.Human.AccountHi, setup.Human.AccountLo, setup.Human.CardBack)
            : (setup.Opponent.AccountHi, setup.Opponent.AccountLo, setup.Opponent.CardBack);
        stream = new ClientStream(1, accounts);
        // the player's Rewind cards offer Keep / Rewind (Generic.OfferRewind; Rewind below); HS_NO_REWIND=1: off (debugging)
        game.RewindEnabled = Environment.GetEnvironmentVariable("HS_NO_REWIND") != "1";
        game.StartGame();
        Flush();

        // The AI picks its mulligan at once, like Blizzard's Innkeeper (keep cards costing 3 or less): its EntitiesChosen comes right
        // after its EntityChoices, but its cards are only dealt when the human's mulligan is answered, in the same packet, AI first
        // (live heist Hearthstone_2026_10_20_21_09_32: Innkeeper chosen 21:15:20.348, its MULLIGAN_STATE DEALING at 21:15:30.714
        // with the player's). Dealt at once, its swap reached the client while it was still laying both hands out for the
        // mulligan, and the opponent's cards floated scattered at the top (user screenshot 2026-11-01).
        Choice aiMulligan = game.Player2.Choice;
        if (aiMulligan != null && aiMulligan.ChoiceType == ChoiceType.MULLIGAN)
        {
            var keep = aiMulligan.Choices.Where(id => game.IdEntityDic[id].Cost <= 3).ToList();
            Queue(Chosen(aiChoiceId, 2, keep, CHOICE_TYPE.MULLIGAN));
            if (humanChoice?.Choice.ChoiceType == ChoiceType.MULLIGAN) pendingAiMulligan = keep;
            else { game.Process(ChooseTask.Mulligan(game.Player2, keep)); Flush(); }
        }
    }

    /// <summary>The AI's mulligan, picked at the start and dealt with the human's (live).</summary>
    private List<int> pendingAiMulligan;

    private void DealPendingAiMulligan()
    {
        if (pendingAiMulligan == null) return;
        var keep = pendingAiMulligan;
        pendingAiMulligan = null;
        if (game.Player2.Choice?.ChoiceType == ChoiceType.MULLIGAN) Run(ChooseTask.Mulligan(game.Player2, keep), "AI mulligan");
    }

    private int aiChoiceId;
    private int aiMovesThisTurn;
    // The opponent AI (roadmap A3): the turn-planning search, one instance per game (it keeps its turn plan).
    private Ai.IGameAi ai = new Ai.PlannerAi();

    public bool ChooseEntities(int choiceId, List<int> entities)
    {
        LastRejection = null;
        if (humanChoice is not var (id, choice) || id != choiceId)
        {
            LastRejection = $"choice {choiceId}: the engine's open choice is {(humanChoice is var (open, oc) ? open + " " + oc.ChoiceType + " of " + oc.SourceId : "none")}, P1 Choice {(game.Player1.Choice == null ? "none" : game.Player1.Choice.ChoiceType + " of " + game.Player1.Choice.SourceId)}, last move {LastMove}";
            return false;
        }
        humanChoice = null;
        if (choice.ChoiceType == ChoiceType.MULLIGAN)
        {
            var keep = entities.Where(choice.Choices.Contains).ToList();
            DealPendingAiMulligan();
            Run(ChooseTask.Mulligan(game.Player1, keep), "human mulligan");
            Queue(Chosen(choiceId, 1, keep, CHOICE_TYPE.MULLIGAN));
        }
        else
        {
            int pick = entities.FirstOrDefault(choice.Choices.Contains);
            if (pick == 0) pick = choice.Choices[0];
            Run(ChooseTask.Pick(game.Player1, pick), "human pick");
            Queue(Chosen(choiceId, 1, new List<int> { pick }, (CHOICE_TYPE)(int)choice.ChoiceType));
        }
        Flush();
        if (game.PendingRewind != 0) Rewind(); // "Rewind Timeline" picked
        return true;
    }

    /// <summary>Why the last ChooseOption was refused (diagnostics).</summary>
    internal string LastRejection { get; private set; }

    public bool ChooseOption(int id, int index, int target, int position, int subOption)
    {
        LastRejection = null;
        if (Over || id != optionsId || sentOptions == null || index < 0 || index >= sentOptions.PowerOptionList.Count)
        {
            LastRejection = $"options id {id} (current {optionsId}, sent {(sentOptions == null ? "none" : sentOptions.PowerOptionList.Count + " options")}), index {index}";
            return false;
        }
        PowerOption option = sentOptions.PowerOptionList[index];
        // An option or target sent with a PlayError is shown to the client, not offered.
        PowerSubOption chosen = subOption >= 0 && subOption < option.SubOptions.Count ? option.SubOptions[subOption] : option.MainOption;
        // (a sub-option with its own PlayError too: a Titan's used ability)
        if (option.MainOption?.PlayError > 0 || chosen != null && chosen != option.MainOption && chosen.PlayError > 0 ||
            chosen != null && chosen.TargetErrors.Any(t => t.Id == target))
        {
            LastRejection = $"option #{index} entity {option.MainOption?.EntityId} target {target} carries a PlayError";
            return false;
        }
        PlayerTask task = ToTask(game.Player1, sentOptions.PlayerTaskList, option, target, position, subOption);
        if (task == null)
        {
            LastRejection = $"no engine move for entity {option.MainOption?.EntityId} target {target} sub {subOption}; moves for it: " +
                string.Join(", ", sentOptions.PlayerTaskList.Where(x => Actor(x) == option.MainOption?.EntityId)
                    .Select(x => $"{x.PlayerTaskType} target {x.TargetEntity?.Id ?? 0} chooseOne {x.ChooseOne}"));
            return false;
        }
        sentOptions = null;
        TakeRewindPoint(task);
        Run(task, $"human {task.PlayerTaskType} {task.Source?.Card.Id} → {task.TargetEntity?.Card.Id}");
        Flush();
        if (game.PendingRewind != 0) Rewind();
        return true;
    }

    /// <summary>
    /// Rewind (live, session scratchpad audit/rewind.md): before the player plays a Rewind card (or uses a Rewind Hero Power) the game is
    /// kept as it is (wiki Rewind: "The entire game state will be reverted to the point it was before the card was played").
    /// </summary>
    private void TakeRewindPoint(PlayerTask task)
    {
        rewindPoint = null;
        if (task.Source == null || SabberStoneCore.Actions.Generic.RewindsOf(task.Source.Card) <= 0) return;
        if (task is PlayCardTask play)
            rewindMove = (false, play.Source.Id, play.TargetEntity?.Id ?? 0, play.ZonePosition, play.ChooseOne);
        else if (task is HeroPowerTask hp)
            rewindMove = (true, hp.Source.Id, hp.TargetEntity?.Id ?? 0, -1, hp.ChooseOne);
        else return;
        rewindPoint = game.Clone(logging: false, resetRandomSeed: false, history: true);
        rewindChoiceId = nextChoiceId;
    }

    /// <summary>
    /// "Rewind Timeline" was picked: the client's game goes back to the rewind point and the card is played again with a new roll. Live
    /// (live-20261006-darkmoon 35364-38031, yogg-cthun 27491+): on the card USED_REWIND 1, 4384 (and a minion's 4385 = its slot); one
    /// GAME_RESET block — RESET_GAME {CreateGame, SourceIsRewind} (the client's RewindGameSpellController; without the flag it rebuilds the
    /// whole game), FULL_ENTITY of both players and of every entity not removed from the game; then AllOptions (a new id, the options of
    /// before the play); then SLUSH_TIME 10000 and the same play again (ids and the choice id reused, the random stream going on).
    /// </summary>
    private void Rewind()
    {
        int cardId = game.PendingRewind;
        game.PendingRewind = 0;
        SGame back = rewindPoint;
        rewindPoint = null;
        if (back == null || !back.IdEntityDic.TryGetValue(cardId, out IPlayable card) || !game.IdEntityDic.TryGetValue(cardId, out IPlayable played)) return;
        back.ContinueRandomFrom(game);
        back.RewindEnabled = true;
        card[SabberStoneCore.Actions.Generic.USED_REWIND] = played[SabberStoneCore.Actions.Generic.USED_REWIND] + 1;
        card[SabberStoneCore.Actions.Generic.REWIND_SOURCE] = cardId;
        if (!rewindMove.HeroPower && card is Minion && rewindMove.Position >= 0)
            card[SabberStoneCore.Actions.Generic.REWIND_POSITION] = rewindMove.Position + 1;

        // the reset: the game as the client may see it (a fresh stream: its view becomes the rewound game's)
        game = back;
        stream = new ClientStream(1, accounts);
        var players = new[] { game.Player1, game.Player2 };
        int start = game.PowerHistory.Full.Count;
        game.PowerHistory.Add(PowerHistoryBuilder.CreateGame(game, players));
        foreach (IPlayable e in game.IdEntityDic.Values.Where(e => e != null && e.Id > 3 && e.Zone?.Type != Zone.REMOVEDFROMGAME).OrderBy(e => e.Id))
            game.PowerHistory.Add(PowerHistoryBuilder.FullEntity(e));
        List<PowerHistoryData> converted = stream.Convert(game.PowerHistory.Full.Skip(start).ToList());
        historyCursor = game.PowerHistory.Full.Count;
        var create = converted.First(d => d.CreateGame != null).CreateGame;
        var reset = new List<PowerHistoryData>
        {
            new() { PowerStart = new PowerHistoryStart { Type = HistoryBlock.Type.GAME_RESET, Source = cardId, Target = 0, EffectIndex = -1, EffectCardId = "" } },
            new() { ResetGame = new PowerHistoryResetGame { CreateGame = create, SourceIsRewind = true } },
        };
        foreach (PegasusGame.Player p in create.Players)
            reset.Add(new PowerHistoryData { FullEntity = new PegasusGame.PowerHistoryEntity { Entity = p.Entity.Id, Name = "", Tags = p.Entity.Tags, DefTags = new List<PegasusGame.Tag>(), TagLists = new List<TagList>(), DefTagLists = new List<TagList>() } });
        reset.AddRange(converted.Where(d => d.FullEntity != null));
        reset.Add(new PowerHistoryData { PowerEnd = new PowerHistoryEnd() });
        Queue(new PegasusGame.PowerHistory { List = reset });
        // (2026-10-08) a card with a version per Rewind used becomes the next one now (Mister Clocksworth; Generic.RewindVersion)
        SabberStoneCore.Actions.Generic.RewindVersion(game, card); // (sent with the replay's history)

        // the options of before the play, under a new id; the choice ids of the play are reused
        humanChoice = null;
        aiChoiceSent = null;
        nextChoiceId = rewindChoiceId;
        sentOptions = PowerOptionsBuilder.AllOptions(game, game.Player1.Options(skipPrePhase: false));
        Queue(AllOptions(++optionsId, sentOptions));
        sentOptions = null;

        // the play again (another rewind point first: Mister Clocksworth's further Rewinds)
        if (!game.IdEntityDic.TryGetValue(rewindMove.Source, out IPlayable source)) return;
        PlayerTask again = rewindMove.HeroPower
            ? HeroPowerTask.Any(game.Player1, game.IdEntityDic.TryGetValue(rewindMove.Target, out IPlayable ht) ? ht as ICharacter : null, rewindMove.ChooseOne,
                power: source as SabberStoneCore.Model.Entities.HeroPower)
            : PlayCardTask.Any(game.Player1, source, game.IdEntityDic.TryGetValue(rewindMove.Target, out IPlayable pt) ? pt : null, rewindMove.Position, rewindMove.ChooseOne);
        game.PowerHistory.Add(new SabberStoneCore.Kettle.PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 10000, Info = new List<int> { cardId } });
        TakeRewindPoint(again);
        Run(again, $"rewind replay {source.Card.Id}");
        Flush();
        if (game.PendingRewind != 0) Rewind();
    }

    /// <summary>
    /// Runs one engine move. A card script that throws must not kill the game: the error is logged with the move, the
    /// game goes on from the engine's current state, which the engine then reports (PublishDerived) so the client's
    /// view matches it again.
    /// </summary>
    /// <summary>The last engine move (diagnostics: the soak names it in problems).</summary>
    internal string LastMove { get; private set; }

    private void Run(PlayerTask task, string what)
    {
        LastMove = what;
        try { game.Process(task); }
        catch (Exception e)
        {
            // The engine wraps a failing card task in a TaskException naming the task and its source card.
            Exception root = e.InnerException ?? e;
            string context = e.InnerException != null ? $" [{e.Message.Replace('\n', ' ')}]" : "";
            Log.Error("rules", $"engine error during {what}: {root.GetType().Name}: {root.Message}{context} @ {string.Join(" <- ", (root.StackTrace ?? "").Split('\n').Where(l => l.Contains(" in ")).Take(5).Select(Frame))}");
            EngineErrors++;
            // An attack that threw leaves the engine in its internal combat step: go back to the main action step so the
            // game can go on (otherwise nobody has options and the client waits forever).
            if (game.Step == Step.MAIN_COMBAT && game.State == State.RUNNING)
                game.SetStepQuietly(Step.MAIN_ACTION);
            try { game.PublishDerived(); } catch (Exception) { /* the next move reports it */ }
        }
    }

    /// <summary>Card-script errors caught during this game (diagnostics; the soak counts them).</summary>
    internal int EngineErrors { get; private set; }

    public void ResendOptions()
    {
        sentOptions = null;
        Flush(forceOptions: true);
    }

    public void AiStep()
    {
        // (the Innkeeper, or the human while Mastermind Yogg-Saron plays their turn)
        Controller actor = HumanControlledByAi ? game.Player1 : game.Player2;
        if (actor.Choice is { } choice)
        {
            Run(ai.Choose(game, actor, choice), "AI choice");
            Flush();
            return;
        }
        PlayerTask move;
        try { move = ai.Step(game, actor); }
        catch (Exception e)
        {
            Log.Error("rules", $"AI error: {e.GetType().Name}: {e.Message} at {e.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}");
            EngineErrors++;
            move = EndTurnTask.Any(actor);
        }
        Run(move, $"AI {move.PlayerTaskType} {move.Source?.Card.Id} → {move.TargetEntity?.Card.Id}");
        // A move that failed without effect would repeat forever; end the turn instead.
        if (game.CurrentPlayer == actor && move.PlayerTaskType != PlayerTaskType.END_TURN && ++aiMovesThisTurn > 40)
            Run(EndTurnTask.Any(actor), "AI forced end turn");
        if (game.CurrentPlayer != actor) aiMovesThisTurn = 0;
        Flush();
    }

    private bool humanConceded;

    /// <summary>
    /// What the human did this game (quests): cards played (with their cost then), mana spent, Hero Power uses and
    /// Location activations from the engine's counters; minions summoned, damage to the enemy hero (META_DATA DAMAGE: the
    /// whole damage, Armor included) and Deathrattles triggered (TRIGGER blocks with TriggerKeyword DEATHRATTLE) from the
    /// game's history, as the client sees them.
    /// </summary>
    public GameTally Tally()
    {
        var me = game.Player1;
        var tally = new GameTally
        {
            HeroClass = (int)me.HeroClass, HeroCardId = setup.Human.HeroCardId, Won = HumanWon, Conceded = humanConceded,
            OwnTurns = (game.Turn + (game.FirstPlayer == me ? 1 : 0)) / 2, HealthAtEnd = me.Hero.Health,
            StartingDeck = setup.Human.Deck.ToList(),
        };
        foreach (PlayHistoryEntry play in me.PlayHistory)
            if (play.SourceController == me.PlayerId)
                tally.Events.Add(new TallyEvent(GameTally.CardPlayed, play.SourceCard.Id, 1, play.Cost, play.SourceCard.Power?.Drink == true ? "DRINK" : null));
        tally.Events.Add(new TallyEvent(GameTally.ManaSpent, null, me.TotalManaSpentThisGame));
        tally.Events.Add(new TallyEvent(GameTally.HeroPowerUsed, null, me.NumTimesHeroPowerUsedThisGame));
        tally.Events.Add(new TallyEvent(GameTally.LocationActivated, null, me[SabberStoneCore.Model.Entities.Location.LOCATION_USES_THIS_GAME]));
        tally.Events.Add(new TallyEvent(GameTally.EnemyMinionDied, null, me.Opponent.NumFriendlyMinionsThatDiedThisGame)); // class XP
        // (2026-10-08) only this player's own sources: wiki Quest.txt:90 "Does not count damage caused by fatigue or the opponent's sources"
        tally.Events.Add(new TallyEvent(GameTally.DamageToEnemyHero, null, me[SabberStoneCore.Enums.HsTag.DAMAGE_DEALT_TO_ENEMY_HERO]));
        bool Mine(int id) => game.IdEntityDic.TryGetValue(id, out var e) && e.Controller == me;
        foreach (SabberStoneCore.Kettle.IPowerHistoryEntry e in game.PowerHistory.Full)
            switch (e)
            {
                case SabberStoneCore.Kettle.PowerHistoryBlockStart { BlockType: BlockType.ATTACK } a when Mine(a.Source): // class XP
                    tally.Events.Add(new TallyEvent(GameTally.Attacked, game.IdEntityDic[a.Source].Card.Id, 1));
                    break;
                case SabberStoneCore.Kettle.PowerHistoryBlockStart { BlockType: BlockType.TRIGGER } b when b.TriggerKeyword == (int)GameTag.DEATHRATTLE && Mine(b.Source):
                    tally.Events.Add(new TallyEvent(GameTally.DeathrattleTriggered, game.IdEntityDic[b.Source].Card.Id, 1));
                    break;
                case SabberStoneCore.Kettle.PowerHistoryTagChange { Value: 1 } forging when forging.Tag == SabberStoneCore.Actions.Generic.IS_USING_FORGE_OPTION && Mine(forging.EntityId):
                    tally.Events.Add(new TallyEvent(GameTally.CardForged, game.IdEntityDic[forging.EntityId].Card.Id, 1));
                    break;
                case SabberStoneCore.Kettle.PowerHistoryTagChange { Tag: GameTag.ZONE, Value: (int)Zone.PLAY } z when game.IdEntityDic.TryGetValue(z.EntityId, out var m2)
                                                                                         && m2 is SabberStoneCore.Model.Entities.Minion && m2.Controller == me:
                    tally.Events.Add(new TallyEvent(GameTally.MinionSummoned, m2.Card.Id, 1));
                    break;
                case SabberStoneCore.Kettle.PowerHistoryFullEntity f when f.Entity.Tags.TryGetValue(GameTag.ZONE, out int zone) && zone == (int)Zone.PLAY
                                                   && game.IdEntityDic.TryGetValue(f.Entity.Id, out var m3)
                                                   && m3 is SabberStoneCore.Model.Entities.Minion && m3.Controller == me:
                    tally.Events.Add(new TallyEvent(GameTally.MinionSummoned, m3.Card.Id, 1));
                    break;
            }
        return tally;
    }

    public TurnClock HumanClock =>
        Over || game.CurrentPlayer != game.Player1 || game.Step != Step.MAIN_ACTION ? null
            : new TurnClock(game.Turn, game.Player1.TimeOut, game.Player1.TurnLimitSeconds);

    public void TimeOut()
    {
        if (Over) return;
        if (humanChoice is var (id, choice) && choice.ChoiceType != ChoiceType.MULLIGAN)
            ChooseEntities(id, new List<int> { choice.Choices[0] });
        if (!Over && game.CurrentPlayer == game.Player1)
        {
            sentOptions = null;
            Run(EndTurnTask.Any(game.Player1), "human out of time");
            Flush();
        }
    }

    public void ConcedeHuman()
    {
        if (Over) return;
        humanConceded = true;
        game.Process(ConcedeTask.Any(game.Player1));
        Flush();
    }

    /// <summary>
    /// A4 reconnect. What the lost connection didn't deliver is dropped (the client gets the result instead); the stream
    /// catches up with the engine and sends live's snapshot (<see cref="ClientStream.Snapshot"/>), then the human's open
    /// choice again (same id) or fresh options.
    /// </summary>
    public bool Resume()
    {
        outbox.Clear();
        List<SabberStoneCore.Kettle.IPowerHistoryEntry> full = game.PowerHistory.Full;
        stream.Convert(full.Skip(historyCursor).ToList());
        historyCursor = full.Count;
        Queue(new PegasusGame.PowerHistory { List = stream.Snapshot() });
        if (Over) return true;
        if (humanChoice is var (id, choice)) Queue(Choices(id, choice));
        sentOptions = null;
        Flush();
        return true;
    }

    public List<IProtoBuf> Drain()
    {
        var packets = new List<IProtoBuf>(outbox);
        outbox.Clear();
        return packets;
    }

    // ---------- history, options, choices → client ----------

    private void Queue(IProtoBuf packet) => outbox.Add(packet);

    private void Flush(bool forceOptions = false)
    {
        List<SabberStoneCore.Kettle.IPowerHistoryEntry> full = game.PowerHistory.Full;
        var converted = stream.Convert(full.Skip(historyCursor).ToList());
        historyCursor = full.Count;
        // Live: once both mulligans are done, the turn (STEP=MAIN_READY, the first draw) comes in its own packet. The client
        // lays the hand out when the mulligan ends; a draw already received counts as a card to come, and the 3 kept
        // cards fanned like a 4-card hand (ZoneHand.CalcCardCountAndScrunch).
        int turn = AfterMulliganTurnStart(converted);
        if (turn > 0)
        {
            Queue(new PegasusGame.PowerHistory { List = converted.GetRange(0, turn) });
            converted = converted.GetRange(turn, converted.Count - turn);
        }
        if (converted.Count > 0) Queue(new PegasusGame.PowerHistory { List = converted });
        if (Over) return;

        // Open choices: the human's (answered by the client) and the AI's (shown to the client like live).
        if (game.Player1.Choice is { } mine && humanChoice?.Choice != mine && !SabberStoneCore.Actions.Generic.AiControls(game.Player1))
        {
            humanChoice = (nextChoiceId++, mine);
            Queue(Choices(humanChoice.Value.Id, mine));
        }
        if (game.Player2.Choice is { } theirs && aiChoiceSent != theirs)
        {
            aiChoiceSent = theirs;
            aiChoiceId = nextChoiceId++;
            if (theirs.ChoiceType == ChoiceType.MULLIGAN) Queue(Choices(aiChoiceId, theirs));
        }

        bool humanToAct = humanChoice == null && game.CurrentPlayer == game.Player1 && game.Step == Step.MAIN_ACTION && !SabberStoneCore.Actions.Generic.AiControls(game.Player1);
        if (humanToAct && (sentOptions == null || forceOptions))
        {
            // the player's attacks run their whole pre-attack phase (CARD_TARGET, the validity check) and their end (the options
            // count, SLUSH_TIME 1000), as the AI's and as live (audit 2026-10-08: the default skipped both for every player attack)
            sentOptions = PowerOptionsBuilder.AllOptions(game, game.Player1.Options(skipPrePhase: false));
            Queue(AllOptions(++optionsId, sentOptions));
        }
    }

    /// <summary>The index of the top-level STEP=MAIN_READY that follows a MULLIGAN_STATE=DONE in the list (0: none).</summary>
    private static int AfterMulliganTurnStart(List<PowerHistoryData> list)
    {
        bool mulliganDone = false;
        int depth = 0;
        for (int i = 0; i < list.Count; i++)
        {
            PowerHistoryData d = list[i];
            if (d.PowerStart != null) depth++;
            else if (d.PowerEnd != null) depth--;
            else if (d.TagChange is { Tag: (int)GAME_TAG.MULLIGAN_STATE, Value: (int)TAG_MULLIGAN.DONE }) mulliganDone = true;
            else if (mulliganDone && depth == 0 && d.TagChange is { Tag: (int)GAME_TAG.STEP, Value: (int)TAG_STEP.MAIN_READY })
                return i;
        }
        return 0;
    }

    private static EntityChoices Choices(int id, Choice choice) => new()
    {
        Id = id, ChoiceType = (int)choice.ChoiceType,
        CountMin = choice.ChoiceType == ChoiceType.MULLIGAN ? 0 : 1,
        CountMax = choice.ChoiceType == ChoiceType.MULLIGAN ? choice.Choices.Count : 1,
        Entities = choice.Choices.ToList(), Source = choice.SourceId, PlayerId = choice.Controller.PlayerId,
        HideChosen = choice.ChoiceType == ChoiceType.MULLIGAN, UnchoosableEntities = new List<int>(),
    };

    private static EntitiesChosen Chosen(int id, int playerId, List<int> entities, CHOICE_TYPE type) => new()
    {
        ChooseEntities = new ChooseEntities { Id = id, Entities = entities.ToList() },
        PlayerId = playerId, ChoiceType = (int)type,
    };

    private static AllOptions AllOptions(int id, PowerAllOptions options) => new()
    {
        Id = id,
        Options = options.PowerOptionList.Select(o => new Option
        {
            Type_ = o.OptionType == OptionType.END_TURN ? Option.Type.END_TURN : Option.Type.POWER,
            MainOption = o.MainOption == null ? null : SubOption(o.MainOption),
            SubOptions = o.SubOptions.Select(SubOption).ToList(),
        }).ToList(),
    };

    private static SubOption SubOption(PowerSubOption s) => new()
    {
        Id = s.EntityId, PlayError = s.PlayError, PowerKeyword = s.PowerKeyword,
        Targets = (s.Targets ?? new List<int>()).Select(t => new TargetOption { Id = t, PlayError = 0 })
            .Concat(s.TargetErrors.Select(t => new TargetOption { Id = t.Id, PlayError = t.Error })).ToList(),
    };

    // The engine move behind the client's choice: same kind, entity, target and choose-one option.
    private static PlayerTask ToTask(Controller me, List<PlayerTask> tasks, PowerOption option, int target, int position, int subOption)
    {
        if (option.OptionType == OptionType.END_TURN) return EndTurnTask.Any(me);
        int entity = option.MainOption.EntityId;
        // A trade option (dropped on the deck) is the card's trade move, not its play.
        if (option.MainOption.PowerKeyword == (int)SabberStoneCore.Actions.Generic.TRADEABLE)
            return tasks.FirstOrDefault(t => t.PlayerTaskType == PlayerTaskType.TRADE && Actor(t) == entity);
        // A Disguised option (dropped on the enemy board, DISGUISED keyword): the play onto the opponent's board at that slot.
        if (option.MainOption.PowerKeyword == (int)SabberStoneCore.Actions.Generic.DISGUISED)
            return tasks.OfType<PlayCardTask>().FirstOrDefault(t => t.Disguised && Actor(t) == entity) is PlayCardTask d
                ? new PlayCardTask(me, d.Source, d.Target, position > 0 ? Math.Min(position - 1, me.Opponent.BoardZone.Count) : -1, d.ChooseOne) { Disguised = true }
                : null;
        // A Prepare option (dropped on the deck, PREPARE keyword) is the card's prepare move.
        if (option.MainOption.PowerKeyword == (int)SabberStoneCore.Actions.Generic.PREPARE)
            return tasks.FirstOrDefault(t => t.PlayerTaskType == PlayerTaskType.PREPARE && Actor(t) == entity);
        // A forge option (dropped on the deck, FORGE keyword) is the card's forge move.
        if (option.MainOption.PowerKeyword == (int)SabberStoneCore.Actions.Generic.FORGE)
            return tasks.FirstOrDefault(t => t.PlayerTaskType == PlayerTaskType.FORGE && Actor(t) == entity);
        // An interactable object (the Underfel Rift): the hand card thrown in is the option's target.
        if (option.MainOption.PowerKeyword == (int)SabberStoneCore.Actions.Generic.INTERACTABLE_OBJECT)
            return tasks.OfType<InteractTask>().FirstOrDefault(t => Actor(t) == entity && t.TargetEntity?.Id == target);
        // A Starship's launch (Actions/Starship.cs): the Starship's own option.
        if (tasks.OfType<StarshipLaunchTask>().FirstOrDefault(t => Actor(t) == entity) is StarshipLaunchTask launch)
            return launch;
        // A Titan's abilities option (its keyword 4786, Actions/Titans.cs): the sub-option names the ability entity.
        if (option.MainOption.PowerKeyword == (int)SabberStoneCore.Actions.Generic.TITAN_OPTION)
        {
            if (subOption < 0 || subOption >= option.SubOptions.Count) return null;
            int ability = option.SubOptions[subOption].EntityId;
            return tasks.OfType<TitanAbilityTask>().FirstOrDefault(t => Actor(t) == entity && t.Ability?.Id == ability && TargetId(t) == target);
        }
        // Choose One: the client's sub-option is an index into the sub-options we offered, which list only the playable
        // choices (Power of the Wild with a full board offers just the buff) — map it through the offered option card to
        // the engine's 1/2. Without one the engine uses -1 (or 0).
        int wanted = subOption >= 0 ? subOption + 1 : -1;
        if (subOption >= 0 && subOption < option.SubOptions.Count
            && tasks.FirstOrDefault(t => Actor(t) == entity)?.Source?.ChooseOnePlayables is { } choices)
        {
            int k = Array.FindIndex(choices, p => p?.Id == option.SubOptions[subOption].EntityId);
            if (k >= 0) wanted = k + 1;
        }
        bool SameOption(PlayerTask t) => wanted > 0 ? t.ChooseOne == wanted : t.ChooseOne <= 0;
        PlayerTask match = tasks.FirstOrDefault(t => t.PlayerTaskType != PlayerTaskType.END_TURN
            && Actor(t) == entity && TargetId(t) == target && SameOption(t));
        if (match == null) return null;
        return match.PlayerTaskType switch
        {
            // Client positions are 1-based board slots; the engine takes a 0-based index (-1 = rightmost). Only minions
            // have a position, and an out-of-range one (never sent by the real client) is clamped instead of trusted.
            PlayerTaskType.PLAY_CARD => PlayCardTask.Any(me, match.Source, match.TargetEntity,
                match.Source.Card.Type is CardType.MINION or CardType.LOCATION && position > 0 ? Math.Min(position - 1, me.BoardZone.Count) : -1, match.ChooseOne),
            _ => match,
        };
    }

    // The move's target as the client names it: a character, or a card in hand (a Location's activation, a hand-targeting play).
    private static int TargetId(PlayerTask t) => t.TargetEntity?.Id ?? 0;

    // The entity the client names for a move: the card played, the attacker, the hero (hero attack) or the hero power.
    private static int Actor(PlayerTask t) => t.PlayerTaskType switch
    {
        PlayerTaskType.HERO_ATTACK => t.Controller.Hero.Id,
        PlayerTaskType.HERO_POWER => (t as HeroPowerTask)?.Power?.Id ?? t.Controller.Hero.HeroPower.Id,
        _ => t.Source?.Id ?? 0,
    };

    // ---------- setup ----------

    private static Card HeroCard(string cardId) =>
        Cards.TryFromId(cardId) is { Type: CardType.HERO } hero ? hero : Cards.FromId("HERO_08");

    // Cards the rules engine doesn't know (newer Core cards without a 2021 original, see `HsServer cards --missing`)
    // can't be played: they are left out of the deck, and the log says which, so a short deck isn't a mystery.
    /// <summary>A deck's sideboards as rules-engine cards (Actions/Sideboards.cs); unknown ids are left out.</summary>
    private static List<(Card Owner, Card Card)> Sideboard(IEnumerable<(string Owner, string Card)> entries) =>
        (entries ?? Enumerable.Empty<(string Owner, string Card)>())
            .Select(e => (Owner: Cards.TryFromId(e.Owner), Card: Cards.TryFromId(e.Card)))
            .Where(e => e.Owner != null && e.Card != null).ToList();

    /// <summary>
    /// (2026-10-09, Dalaran Heist) the human's deck with its run-deck slots (PlayerSetup.RunDeck) kept beside their cards — a card the
    /// rules engine doesn't know leaves with its slot — and the run's deck enchantments as engine cards.
    /// </summary>
    private static (List<Card> Deck, SabberStoneCore.Config.RunDeck Run) RunDeckOf(PlayerSetup p)
    {
        if (p.RunDeck == null) return (Deck(p.Deck), null);
        var deck = new List<Card>();
        var run = new SabberStoneCore.Config.RunDeck { SlotCount = p.RunDeck.SlotCount };
        var unknown = new List<string>();
        for (int i = 0; i < p.Deck.Count; i++)
        {
            if (Cards.TryFromId(p.Deck[i]) is not Card card) { unknown.Add(p.Deck[i]); continue; }
            deck.Add(card);
            run.Slots.Add(i < p.RunDeck.Slots.Count ? p.RunDeck.Slots[i] : 0);
        }
        if (unknown.Count > 0)
            Log.Warn("rules", $"run deck: {unknown.Count} of {p.Deck.Count} cards unknown to the rules engine, left out: {string.Join(", ", unknown)}");
        foreach (var (slot, id) in p.RunDeck.Enchantments)
            if (Cards.TryFromId(id) is Card enchantment) run.Enchantments.Add((slot, enchantment));
        return (deck, run);
    }

    /// <summary>(2026-10-09) the scenario's tags (EngineSetup.Scenario) as the rules engine takes them.</summary>
    private static SabberStoneCore.Config.ScenarioSetup ScenarioOf(ScenarioTags t)
    {
        if (t == null) return null;
        static void Copy(Dictionary<int, int> from, Dictionary<GameTag, int> to)
        {
            foreach (var (tag, value) in from) to[(GameTag)tag] = value;
        }
        var s = new SabberStoneCore.Config.ScenarioSetup();
        Copy(t.Game, s.GameTags);
        Copy(t.Start, s.StartTags);
        for (int i = 0; i < 2; i++)
        {
            Copy(t.Players[i], s.PlayerTags[i]);
            Copy(t.Heroes[i], s.HeroTags[i]);
            Copy(t.HeroesAtStart[i], s.HeroStartTags[i]);
        }
        return s;
    }

    private static List<Card> Deck(IEnumerable<string> cardIds)
    {
        var ids = cardIds.ToList();
        var unknown = ids.Where(id => Cards.TryFromId(id) == null).ToList();
        if (unknown.Count > 0)
            Log.Warn("rules", $"deck: {unknown.Count} of {ids.Count} cards unknown to the rules engine, left out: {string.Join(", ", unknown)}");
        return ids.Select(Cards.TryFromId).Where(c => c != null).ToList();
    }
}
