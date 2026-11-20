using HsServer.Engine;
using SabberStoneCore.Model;

namespace HsServer.Lobby;

/// <summary>
/// (2026-10-09) Bob's Tavern, the Dalaran Heist's "Friendly Encounter" between bosses 3-4 and 5-6 — the server's part (spec and evidence:
/// RESEARCH/heist-tavern.md, 27 live visits):
///   offer — with the 3rd and 5th boss win (besides its loot and treasure offers) SCENARIO_OVERRIDE (266) = the mode's tavern scenario
///           (3236 Normal / 3359 Heroic), NEXT_BOSS_FIGHT = the tavern's host (Scenario.Player2HeroCardId, Bartender Bob 52913) ×5,
///           NEXT_BOSS_HEALTH = his Health (40); the client queues the override as an ordinary adventure game;
///   game  — the run's hero and Hero Power (the hero at its card's own Health), the run deck with each card's slot (tag 1286) and the run's
///           deck enchantments, against Bob (rarely Bartendotron); the scenario's tags (PUZZLE_MODE, squelched triggers, Hero Powers off,
///           the player's hero Immune); Bob's script sets the table (rules engine: CardSets/HsServer/DalaranHeistCardsGenHs5.cs);
///   end   — the player always wins (Bob gives up on his turn); the client disconnects at once ("Reason: DALA_Tavern" — a normal close for
///           the game server: the game is over and reported). The spells' run-deck edits (GameOutcome.RunDeckEdits) are written in play
///           order: a removed card's DECK_CARD_LIST slot = 0, a new card appended, a deck enchantment appended to DECK_ENCHANTMENT_INDICES
///           (368) / DECK_ENCHANTMENTS (369), Good Food's HERO_HEALTH +5 and subkey 370 = 1; BOSSES_FOUGHT += the host (not DEFEATED),
///           SCENARIO_OVERRIDE 0, the next level's boss and Health, no loot or treasure offered (the next boss win adds a 0 to
///           LOOT_HISTORY), HAS_SEEN_EVENT_1 (227) / _2 (228) = 1 after the first / second visit. No rewards: the client shows none.
/// </summary>
internal sealed partial class HeistRuns
{
    // client GameSaveKeySubkeyId (227 / 228 DUNGEON_CRAWL_HAS_SEEN_EVENT_1 / _2; 370: no client name — live, Good Food)
    private const long DeckEnchantmentIndices = 368, DeckEnchantments = 369, GoodFoodPlayed = 370, HasSeenEvent1 = 227, HasSeenEvent2 = 228;

    /// <summary>The bosses after which the tavern comes (live: 27 of 27 visits after the 3rd and the 5th).</summary>
    private static readonly int[] TavernAfter = { 3, 5 };

    /// <summary>Bartendotron's odds of hosting instead of Bob — wiki Bartender Bob: "On rare occasions, players can find themselves
    /// encountering Bartendotron instead" (no rate known; 0 of 27 live visits); only with 4+ Mechs in the deck (wiki Bartendotron).</summary>
    private const double BartendotronChance = 0.05;
    private const string BobId = "DALA_BOSS_99h", BartendotronId = "DALA_BOSS_98h";

    private readonly Dictionary<int, bool> tavernHeroic = new(); // tavern scenario → Heroic

    /// <summary>The scenario is Bob's Tavern (3236 / 3359).</summary>
    public bool IsTavern(long scenario) => taverns.Contains((int)scenario);

    private int? TavernFor(bool heroic) => tavernHeroic.Where(t => t.Value == heroic).Select(t => (int?)t.Key).FirstOrDefault();

    /// <summary>The tavern's host as the save data names it (Scenario.Player2HeroCardId: Bartender Bob).</summary>
    private int TavernHost(int tavern) => previewBoss.GetValueOrDefault(tavern);

    private bool IsTavernHost(long dbf) => dbf > 0 && taverns.Any(t => TavernHost(t) == dbf);

    /// <summary>A new run: no deck enchantments, no Good Food, no tavern seen.</summary>
    private static void ResetTavernState(SaveDataStore sd, long key)
    {
        sd.SetInts(key, DeckEnchantmentIndices, Array.Empty<long>());
        sd.SetInts(key, DeckEnchantments, Array.Empty<long>());
        foreach (long sub in new[] { GoodFoodPlayed, HasSeenEvent1, HasSeenEvent2 }) sd.SetInts(key, sub, new long[] { 0 });
    }

    /// <summary>
    /// The player's run deck for a game: DECK_CARD_LIST's non-zero slots in slot order (each card with its slot), the list's length, and
    /// the run's deck enchantments (368 index i → DECK_CARD_LIST[i - 1], with 369's enchantment; the client's SetUpDeckList reads them so).
    /// </summary>
    private void SetRunDeck(SaveDataStore sd, long key, PlayerSetup human)
    {
        var list = Many(sd, key, DeckCardList);
        var run = new RunDeckSetup { SlotCount = list.Count };
        human.Deck = new List<string>();
        for (int i = 0; i < list.Count; i++)
            if (list[i] > 0 && cards.ByDbf((int)list[i]) is { } card)
            {
                human.Deck.Add(card.CardId);
                run.Slots.Add(i + 1);
            }
        var indices = Many(sd, key, DeckEnchantmentIndices);
        var enchantments = Many(sd, key, DeckEnchantments);
        for (int i = 0; i < Math.Min(indices.Count, enchantments.Count); i++)
            if (indices[i] > 0 && cards.ByDbf((int)enchantments[i]) is { } enchantment)
                run.Enchantments.Add(((int)indices[i], enchantment.CardId));
        human.RunDeck = run;
    }

    /// <summary>After boss <paramref name="level"/>'s win: the tavern comes next when it is one of <see cref="TavernAfter"/>.</summary>
    private void OfferTavern(SaveDataStore sd, long key, int level, bool heroic)
    {
        if (!TavernAfter.Contains(level) || TavernFor(heroic) is not int tavern) return;
        int host = TavernHost(tavern);
        sd.SetInts(key, ScenarioOverride, new long[] { tavern });
        sd.SetInts(key, NextBossFight, Enumerable.Repeat((long)host, 5));
        sd.SetInts(key, NextBossHealth, new long[] { cards.ByDbf(host)?.Tag(GAME_TAG.HEALTH) ?? 40 });
        Log.Info("heist", $"Bob's Tavern next (scenario {tavern})");
    }

    /// <summary>A boss win right after a tavern: that stage had no loot — live LOOT_HISTORY gets a 0 for it (183021 #31496).</summary>
    private void LootAfterTavern(SaveDataStore sd, long key, List<long> fought)
    {
        if (fought.Count >= 2 && IsTavernHost(fought[^2])) Append(sd, key, LootHistory, 0);
    }

    /// <summary>The tavern's two players and the scenario's tags (null without a run).</summary>
    private (PlayerSetup Human, PlayerSetup Boss)? PrepareTavern(SaveDataStore sd, long scenario, long heroCardDbId, Random rng)
    {
        long key = KeyOf(scenario);
        if (One(sd, key, IsRunActive) != 1)
        {
            Log.Warn("heist", $"tavern {scenario} queued without a run in progress");
            return null;
        }
        var human = new PlayerSetup
        {
            HeroCardId = cards.ByDbf((int)One(sd, key, RunHeroCard))?.CardId ?? cards.ByDbf((int)heroCardDbId)?.CardId ?? "HERO_08",
            HeroPowerCardId = cards.ByDbf((int)One(sd, key, HeroPower))?.CardId,
            StartHealth = null, // (live: the hero card's own Health in the tavern — 15 — not the run's)
        };
        SetRunDeck(sd, key, human);
        // (Bartendotron only for a deck with at least 4 Mechs — Hearthstone Wiki Bartendotron: "a small chance of appearing if your deck
        // has at least four Mechs"; race 17 Mech, 26 All)
        int mechs = human.Deck.Count(id => cards.TryGet(id, out CardDef? d) && d.Tag(GAME_TAG.CARDRACE) is 17 or 26);
        string hostId = mechs >= 4 && rng.NextDouble() < BartendotronChance && cards.TryGet(BartendotronId, out _) ? BartendotronId
            : cards.ByDbf(TavernHost((int)scenario))?.CardId ?? BobId;
        // the host's deck: none known (live: 30 hidden cards; he never draws, Brood puts his minions back into it) — left empty, the caller
        // gives him a plain deck of his class (Program: Core + Classic; a random collectible card could be a deck-start effect such as
        // Maestra of the Masquerade's disguise, which would replace the host's hero)
        var host = new PlayerSetup { HeroCardId = hostId, IsAi = true };
        Log.Info("heist", $"{(tavernHeroic.GetValueOrDefault((int)scenario) ? "Heroic" : "Normal")} tavern: {human.HeroCardId} " +
                          $"({human.Deck.Count} cards, {human.RunDeck!.Enchantments.Count} deck enchantments) with {hostId}");
        return (human, host);
    }

    /// <summary>
    /// The tavern's own tags (live CREATE_GAME and its PUZZLE_MODE start, RESEARCH/heist-tavern.md §2.1-2.2; null for another scenario):
    /// the game PUZZLE_MODE and the SQUELCH tags; both players HERO_POWER_DISABLED (player 1 also 1276 = 50); the player's hero IMMUNE,
    /// CANT_BE_ATTACKED, CANT_BE_TARGETED, HIDE_STATS, DONT_SHOW_IMMUNE; the host's HIDE_STATS as the game starts; the start block's
    /// concede text, no restart button, no out-of-cards warning, no "job's done" line, the end-turn button and turn indicator looks.
    /// </summary>
    public ScenarioTags? ScenarioTagsFor(long scenario)
    {
        if (!IsTavern(scenario)) return null;
        var t = new ScenarioTags();
        t.Game[(int)GAME_TAG.SQUELCH_NON_GAME_TRIGGERS_AND_MODIFIERS] = 1;
        t.Game[(int)GAME_TAG.PUZZLE_MODE] = 1;
        t.Game[(int)GAME_TAG.SQUELCH_LIFETIME_EFFECTS] = 1;
        t.Players[0][(int)GAME_TAG.HERO_POWER_DISABLED] = 1;
        t.Players[0][1276] = 50; // (unnamed; live, player 1 only)
        t.Players[1][(int)GAME_TAG.HERO_POWER_DISABLED] = 1;
        foreach (GAME_TAG tag in new[] { GAME_TAG.IMMUNE, GAME_TAG.CANT_BE_ATTACKED, GAME_TAG.CANT_BE_TARGETED, GAME_TAG.HIDE_STATS, GAME_TAG.DONT_SHOW_IMMUNE })
            t.Heroes[0][(int)tag] = 1;
        t.HeroesAtStart[1][(int)GAME_TAG.HIDE_STATS] = 1;
        foreach (GAME_TAG tag in new[] { GAME_TAG.CONCEDE_BUTTON_ALTERNATIVE_TEXT, GAME_TAG.HIDE_RESTART_BUTTON, GAME_TAG.HIDE_OUT_OF_CARDS_WARNING, GAME_TAG.SUPPRESS_JOBS_DONE_VO })
            t.Start[(int)tag] = 1;
        t.Start[(int)GAME_TAG.END_TURN_BUTTON_ALTERNATIVE_APPEARANCE] = 2;
        t.Start[(int)GAME_TAG.TURN_INDICATOR_ALTERNATIVE_APPEARANCE] = 5;
        return t;
    }

    /// <summary>The tavern is over: its deck edits written in play order and the run goes on to the next boss (see the class notes).</summary>
    private void TavernEnded(SaveDataStore sd, long scenario, bool won, Random rng, IReadOnlyList<RunDeckEdit>? edits)
    {
        long key = KeyOf(scenario);
        if (One(sd, key, IsRunActive) != 1) return;
        long host = One(sd, key, NextBossFight);
        var fought = Many(sd, key, BossesFought);
        fought.Add(host > 0 ? host : TavernHost((int)scenario));
        sd.SetInts(key, BossesFought, fought);
        if (!won)
        {
            // (user 2026-11-20: conceding the tavern ends the run, like losing to a boss — no concede was recorded)
            Log.Info("heist", "tavern conceded: the run ends");
            EndRun(sd, key, host > 0 ? host : TavernHost((int)scenario), retired: false);
            return;
        }

        var deck = Many(sd, key, DeckCardList);
        var indices = Many(sd, key, DeckEnchantmentIndices);
        var enchantments = Many(sd, key, DeckEnchantments);
        int health = 0;
        foreach (RunDeckEdit e in edits ?? Array.Empty<RunDeckEdit>())
            switch (e.Kind)
            {
                case RunDeckEditKind.RemoveCard when e.Slot >= 1 && e.Slot <= deck.Count:
                    deck[e.Slot - 1] = 0;
                    break;
                case RunDeckEditKind.AddCard:
                    if (e.Slot != deck.Count + 1) Log.Warn("heist", $"tavern: {e.CardId} added at slot {e.Slot}, the deck list has {deck.Count}");
                    deck.Add(cards.TryGet(e.CardId, out CardDef? added) ? added.DbfId : e.Dbf);
                    break;
                case RunDeckEditKind.AddEnchantment when e.Slot >= 1:
                    indices.Add(e.Slot);
                    enchantments.Add(cards.TryGet(e.CardId, out CardDef? enchantment) ? enchantment.DbfId : e.Dbf);
                    break;
                case RunDeckEditKind.AddStartingHealth:
                    health += e.Amount;
                    break;
            }
        sd.SetInts(key, DeckCardList, deck);
        sd.SetInts(key, DeckEnchantmentIndices, indices);
        sd.SetInts(key, DeckEnchantments, enchantments);
        if (health != 0)
        {
            sd.SetInts(key, HeroHealth, new[] { One(sd, key, HeroHealth) + health });
            sd.SetInts(key, GoodFoodPlayed, new long[] { 1 });
        }

        int level = Many(sd, key, BossesDefeated).Count;
        sd.SetInts(key, level <= TavernAfter[0] ? HasSeenEvent1 : HasSeenEvent2, new long[] { 1 });
        sd.SetInts(key, ScenarioOverride, new long[] { 0 });
        // the next level's boss of the run's chapter (its scenario: ScenarioId), at that level's Health
        long chapterScenario = One(sd, key, ScenarioId);
        int wing = scenarios.TryGetValue((int)chapterScenario, out var s) ? s.Wing : 0;
        int nextBoss = PickBoss(wing, level + 1, fought, rng, chapterScenario);
        sd.SetInts(key, NextBossFight, Enumerable.Repeat((long)nextBoss, 5));
        sd.SetInts(key, NextBossHealth, new long[] { BossHealth[Math.Min(level, BossHealth.Length - 1)] });
        // no loot and no treasure for the tavern's stage
        foreach (long sub in new[] { LootA, LootB, LootC, TreasureOption, ChosenLoot, ChosenTreasure }) sd.SetInts(key, sub, Array.Empty<long>());
        Log.Info("heist", $"tavern done: {edits?.Count ?? 0} deck edits ({deck.Count} slots, {indices.Count} deck enchantments" +
                          (health != 0 ? $", +{health} starting Health" : "") + $"); next {cards.ByDbf(nextBoss)?.CardId} at " +
                          $"{BossHealth[Math.Min(level, BossHealth.Length - 1)]} Health");
    }
}
