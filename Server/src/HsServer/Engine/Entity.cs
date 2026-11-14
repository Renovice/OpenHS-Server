namespace HsServer.Engine;

/// <summary>
/// A game entity (game, player, hero, card...). Tags are read freely; writes go through <see cref="GameState.Set"/>
/// so every change is reported to the client in the power history.
/// </summary>
internal sealed class Entity(int id, CardDef card)
{
    public readonly int Id = id;
    public CardDef Card { get; set; } = card;
    public readonly Dictionary<int, int> Tags = new();
    /// <summary>Set once the card's identity has been shown to the client (FULL_ENTITY with CardID or SHOW_ENTITY).</summary>
    public bool RevealedToClient;

    public int this[GAME_TAG tag] => Tags.TryGetValue((int)tag, out int v) ? v : 0;

    public TAG_ZONE Zone => (TAG_ZONE)this[GAME_TAG.ZONE];
    public int Controller => this[GAME_TAG.CONTROLLER];
    public TAG_CARDTYPE Type => (TAG_CARDTYPE)this[GAME_TAG.CARDTYPE];
    public int Atk => this[GAME_TAG.ATK];
    public int Health => this[GAME_TAG.HEALTH] - this[GAME_TAG.DAMAGE];
    public int Cost => this[GAME_TAG.COST];
    public bool Has(GAME_TAG tag) => this[tag] != 0;
    public string CardId => Card?.CardId ?? "";

    public override string ToString() => $"#{Id} {Card?.Name ?? Type.ToString()}";
}
