namespace HsServer.Engine;

/// <summary>
/// (2026-10-09, Dalaran Heist) A player's adventure-run deck as the save data has it (Lobby/HeistRuns): the 1-based DECK_CARD_LIST slot of
/// each card of <see cref="PlayerSetup.Deck"/> (same order; zeroed slots left out), how many slots the list has, and the run's deck
/// enchantments (DECK_ENCHANTMENT_INDICES 368 / DECK_ENCHANTMENTS 369: slot, enchantment card id). The rules engine puts each card's slot
/// on it (tag 1286) and the enchantments on their cards as the game starts; what the game changes comes back in GameOutcome.RunDeckEdits.
/// </summary>
internal sealed class RunDeckSetup
{
    public List<int> Slots = new();
    public int SlotCount;
    public List<(int Slot, string EnchantmentId)> Enchantments = new();
}

/// <summary>
/// (2026-10-09) A scenario's own tags, set by the server's scenario setup (live Bob's Tavern: RESEARCH/heist-tavern.md §2.1-2.2). Keys
/// are client GAME_TAG numbers. <see cref="Game"/>, <see cref="Players"/> and <see cref="Heroes"/> are in CREATE_GAME / the heroes'
/// FULL_ENTITY; <see cref="HeroesAtStart"/> change as the game starts; <see cref="Start"/> are the PUZZLE_MODE start block's game tags.
/// Index 0 = player 1 (the human).
/// </summary>
internal sealed class ScenarioTags
{
    public Dictionary<int, int> Game = new();
    public Dictionary<int, int> Start = new();
    public Dictionary<int, int>[] Players = { new(), new() };
    public Dictionary<int, int>[] Heroes = { new(), new() };
    public Dictionary<int, int>[] HeroesAtStart = { new(), new() };
}
