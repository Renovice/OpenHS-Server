namespace HsServer.Game;

/// <summary>
/// What the human did in one game, for quests (and later achievements): one list of game events, built by the engine
/// adapter when the game ends (engine-agnostic, like the client-facing history it is read from).
/// </summary>
internal sealed class GameTally
{
    public const string CardPlayed = "CARD_PLAYED", MinionSummoned = "MINION_SUMMONED", ManaSpent = "MANA_SPENT",
        HeroPowerUsed = "HERO_POWER", DamageToEnemyHero = "DAMAGE_ENEMY_HERO", DeathrattleTriggered = "DEATHRATTLE",
        LocationActivated = "LOCATION", CardForged = "CARD_FORGED", EnemyMinionDied = "ENEMY_MINION_DIED",
        Attacked = "ATTACK";

    /// <summary>The human's hero class (client TAG_CLASS).</summary>
    public int HeroClass { get; init; }
    /// <summary>The human's hero card at the start (its records: PlayerRecords by hero).</summary>
    public string HeroCardId { get; init; }
    public bool Won { get; init; }
    public bool Conceded { get; init; }
    public int OwnTurns { get; init; }
    public int HealthAtEnd { get; init; }
    /// <summary>BnetGameType of the game (VS_AI, ranked...).</summary>
    public long GameType { get; set; }
    public IReadOnlyList<string> StartingDeck { get; init; } = Array.Empty<string>();
    public List<TallyEvent> Events { get; } = new();

    /// <summary>wiki Quests: quests that only ask to play games don't count a game "where the player conceded early
    /// (requires conceder to have 15 Health or less, or have played for at least 10 turns)" (anti AFK farming).</summary>
    public bool CountsAsPlayed => !Conceded || HealthAtEnd <= 15 || OwnTurns >= 10;
}

/// <summary>One game event: what happened (<see cref="GameTally"/> kinds), the card involved (client card id, if any),
/// how much (mana, damage; 1 for a count), the card's cost when it was played, and traits the engine knows that the client
/// data doesn't tag ("DRINK").</summary>
internal readonly record struct TallyEvent(string Kind, string CardId, int Amount, int Cost = 0, string Traits = null);
