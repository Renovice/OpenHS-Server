using HsServer.Engine;

namespace HsServer.Game;

/// <summary>
/// A rules engine as the network game (<see cref="EngineGame"/>) drives it. Player 1 is the human, player 2 the AI.
/// Everything the client must receive (history, options, choices) is queued and taken with <see cref="Drain"/>.
/// </summary>
internal interface IEngineGame
{
    bool Over { get; }
    /// <summary>Did the human (player 1) win? Meaningful once <see cref="Over"/>.</summary>
    bool HumanWon { get; }
    /// <summary>The AI is the player to act (its turn, no pending human choice).</summary>
    bool AiToAct { get; }
    string Describe { get; }

    /// <summary>Create the game and deal; the AI answers its own mulligan.</summary>
    void Start();
    /// <summary>The human's answer to an entity choice (mulligan, Discover...). False if it matches no open choice.</summary>
    bool ChooseEntities(int choiceId, List<int> entities);
    /// <summary>The human's ChooseOption. False if it matches no current option (the caller resends options).</summary>
    bool ChooseOption(int optionsId, int index, int target, int position, int subOption);
    void ResendOptions();
    /// <summary>One AI action (or its end of turn).</summary>
    void AiStep();
    void ConcedeHuman();
    List<IProtoBuf> Drain();
    /// <summary>What the human did this game (quests); meaningful once <see cref="Over"/>.</summary>
    GameTally Tally() => new() { Won = HumanWon };
    /// <summary>The client reconnected (A4): queue the game as it is now, then the open choice or the options. False: this
    /// engine can't (the client is left waiting; the game is abandoned when the reconnect window ends).</summary>
    bool Resume() => false;
    /// <summary>The human's turn clock (their game turn, its seconds: TIMEOUT, and a card's turn-time limit), or null when
    /// it isn't the human's turn to act (the AI's turn, the mulligan, game over).</summary>
    TurnClock HumanClock => null;
    /// <summary>Time ran out on the human's turn: an open choice takes its first option, then the turn ends.</summary>
    void TimeOut() { }
    /// <summary>(2026-10-09, Dalaran Heist) what the game did to the human's adventure-run deck, in order (null: nothing / not a run).</summary>
    IReadOnlyList<SabberStoneCore.Model.RunDeckEdit> RunDeckEdits => null;
}

/// <summary>The human's turn: the game turn, its TIMEOUT seconds and a card's turn-time limit (Nozdormu), if any.</summary>
internal sealed record TurnClock(int Turn, int Seconds, int? Limit);

/// <summary>The original hand-written engine (<see cref="GameState"/>) behind <see cref="IEngineGame"/>.</summary>
internal sealed class BasicEngineGame(EngineSetup setup) : IEngineGame
{
    private GameState game;

    public bool Over => game.Over;
    public bool HumanWon => game.Winner == game.P1;
    public bool AiToAct => !game.Over && game.Current.IsAi;
    public string Describe => $"{setup.Human.HeroCardId} vs {setup.Opponent.HeroCardId} (basic engine)";

    public void Start()
    {
        game = new GameState(setup.Cards, setup.Seed);
        game.Start(setup.Human, setup.Opponent, setup.Scenario);
        // The AI answers its mulligan at once, like Blizzard's Innkeeper.
        game.Mulligan(game.P2, game.P2.Hand.Where(c => c.Cost <= 3).Select(c => c.Id).ToList());
    }

    public bool ChooseEntities(int choiceId, List<int> entities)
    {
        if (choiceId != game.P1.MulliganChoiceId) return false;
        game.Mulligan(game.P1, entities);
        return true;
    }

    public bool ChooseOption(int optionsId, int index, int target, int position, int subOption) =>
        game.ChooseOption(optionsId, index, target, position);

    public void ResendOptions() => game.SendOptions();

    public void AiStep()
    {
        if (!SimpleAi.Step(game)) game.EndTurn();
    }

    public void ConcedeHuman() => game.Concede(game.P1);

    public List<IProtoBuf> Drain() => game.History.Drain();
}
