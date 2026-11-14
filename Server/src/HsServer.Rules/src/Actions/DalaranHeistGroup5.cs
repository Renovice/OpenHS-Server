// HsServer addition (not upstream SabberStone): The Dalaran Heist (2019 solo adventure) — group 5: the adventure run's deck and
// Bob's Tavern ("Friendly Encounter"). Spec: RESEARCH/heist-tavern.md (27 live tavern games). Shared pieces:
//   - the run deck (GameConfig.Player1RunDeck): each deck card carries its 1-based DECK_CARD_LIST slot (client tag 1286), the run's
//     deck enchantments (save data DECK_ENCHANTMENT_INDICES 368 / DECK_ENCHANTMENTS 369) are put on those cards at game start, and
//     effects that change the run deck record what they did (Game.RunDeckEdits) for the server, which writes the save data;
//   - a scenario's own tags (GameConfig.Scenario: the server's scenario setup) and the PUZZLE_MODE start (no mulligan, no opening
//     hand: the scenario sets the table itself), squelched card triggers (SQUELCH_NON_GAME_TRIGGERS_AND_MODIFIERS);
//   - "Always starts in your hand" (Right Hand Man's DALA_905e), a cancelled turn-start draw, the starting heroes' own triggers.
// Card scripts: CardSets/HsServer/DalaranHeistCardsGenHs5.cs (Bartender Bob / Bartendotron and the tavern spells DALA_901..914).
using System;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Config;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Config
{
	/// <summary>
	/// HsServer (2026-10-09, Dalaran Heist): the player's adventure-run deck. <see cref="Slots"/> = the 1-based DECK_CARD_LIST slot of
	/// each card of the player's deck list (same order, zeroed slots left out); <see cref="SlotCount"/> = how many slots the list has
	/// (a card added to the run deck takes the next one: live never reuses or compacts a zeroed slot); <see cref="Enchantments"/> = the
	/// run's deck enchantments (slot, enchantment card), put on that slot's card when the game starts.
	/// </summary>
	public sealed class RunDeck
	{
		public List<int> Slots { get; set; } = new List<int>();
		public int SlotCount { get; set; }
		public List<(int Slot, Card Enchantment)> Enchantments { get; set; } = new List<(int Slot, Card Enchantment)>();
	}

	/// <summary>
	/// HsServer (2026-10-09): a scenario's own tags, as the server's scenario setup gives them (live Bob's Tavern: PUZZLE_MODE and the
	/// SQUELCH tags on the game, HERO_POWER_DISABLED on both players, the player's hero IMMUNE and untargetable). Index 0 = player 1.
	/// <see cref="GameTags"/>, <see cref="PlayerTags"/> and <see cref="HeroTags"/> are in CREATE_GAME / the heroes' FULL_ENTITY;
	/// <see cref="HeroStartTags"/> change as the game starts (live: Bob's HIDE_STATS right after the players' PLAYSTATE); <see
	/// cref="StartTags"/> are the game tags of the PUZZLE_MODE start block (button texts, turn indicator).
	/// </summary>
	public sealed class ScenarioSetup
	{
		public Dictionary<GameTag, int> GameTags { get; set; } = new Dictionary<GameTag, int>();
		public Dictionary<GameTag, int> StartTags { get; set; } = new Dictionary<GameTag, int>();
		public Dictionary<GameTag, int>[] PlayerTags { get; } = { new Dictionary<GameTag, int>(), new Dictionary<GameTag, int>() };
		public Dictionary<GameTag, int>[] HeroTags { get; } = { new Dictionary<GameTag, int>(), new Dictionary<GameTag, int>() };
		public Dictionary<GameTag, int>[] HeroStartTags { get; } = { new Dictionary<GameTag, int>(), new Dictionary<GameTag, int>() };
	}

	public partial class GameConfig
	{
		/// <summary>HsServer (2026-10-09): player 1's adventure-run deck (Dalaran Heist), or null.</summary>
		public RunDeck Player1RunDeck { get; set; }

		/// <summary>HsServer (2026-10-09): the scenario's own tags (Bob's Tavern), or null.</summary>
		public ScenarioSetup Scenario { get; set; }
	}
}

namespace SabberStoneCore.Model
{
	/// <summary>What a run-deck edit does (Bob's Tavern spells; HsServer: Lobby/HeistTavern.cs writes them to the save data).</summary>
	public enum RunDeckEditKind
	{
		/// <summary>The slot's card leaves the run deck (DECK_CARD_LIST slot = 0).</summary>
		RemoveCard,
		/// <summary>A card joins the run deck at the next slot (DECK_CARD_LIST += its dbf).</summary>
		AddCard,
		/// <summary>The slot's card gets a run enchantment (368 += slot, 369 += the enchantment's dbf).</summary>
		AddEnchantment,
		/// <summary>The run's starting Health goes up (HERO_HEALTH += Amount).</summary>
		AddStartingHealth,
	}

	/// <summary>One change to the adventure-run deck, in play order: the slot (1-based), the card's (or the enchantment's) dbf id and
	/// card id, an amount.</summary>
	public readonly record struct RunDeckEdit(RunDeckEditKind Kind, int Slot, int Dbf, string CardId, int Amount = 0);

	public partial class Game
	{
		/// <summary>HsServer (2026-10-09): the run-deck edits made this game, in order (only with a run deck: GameConfig.Player1RunDeck).</summary>
		internal List<RunDeckEdit> RunDeckEditList = new List<RunDeckEdit>();

		/// <summary>HsServer (2026-10-09): what this game did to the player's adventure-run deck, in order (the server applies them).</summary>
		public IReadOnlyList<RunDeckEdit> RunDeckEdits => RunDeckEditList;

		/// <summary>HsServer (2026-10-09): the configuration the game was made with (the run deck, the scenario).</summary>
		internal GameConfig RunSetup => _gameConfig;
	}
}

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		// ======================================================================================================= the run deck

		/// <summary>Client tag 1286 (unnamed): the card's 1-based slot in the adventure-run deck (DECK_CARD_LIST). Live: on every run deck
		/// card (also the treasure passive), kept through every move; a card recruited in the tavern gets the next slot.</summary>
		public const GameTag RUN_DECK_SLOT = (GameTag)1286;
		/// <summary>Client tag 1254 (unnamed): who put a minion onto the tavern's board (Bob, Brood; live: cleared when it leaves).</summary>
		public const GameTag PLACED_BY = (GameTag)1254;
		/// <summary>HsServer (internal): player — the run-deck slots used so far (the next added card takes this + 1).</summary>
		public const GameTag RUN_DECK_SLOTS_USED = (GameTag)995501;
		/// <summary>HsServer (internal): card — "Always starts in your hand" (DALA_905e Right Hand Man): it is in the opening hand.</summary>
		public const GameTag STARTS_IN_HAND = (GameTag)995502;
		/// <summary>HsServer (internal): player — its next turn-start draw is cancelled by this entity's trigger (live Bob, index 1).</summary>
		public const GameTag TURN_DRAW_CANCELLED_BY = (GameTag)995503;
		/// <summary>HsServer (internal): player — the effect index of that trigger's block, + 1.</summary>
		public const GameTag TURN_DRAW_CANCEL_INDEX = (GameTag)995504;
		/// <summary>HsServer (internal): a tavern host's hero — the race its tavern minions are of (Bartendotron: Mechs), 0 for any.</summary>
		public const GameTag TAVERN_MINION_RACE = (GameTag)995505;

		/// <summary>The card's run-deck slot (1-based), 0 when it isn't a run-deck card.</summary>
		public static int RunSlot(IPlayable card) => card?[RUN_DECK_SLOT] ?? 0;

		/// <summary>The game has an adventure-run deck for <paramref name="c"/> (its edits are recorded).</summary>
		public static bool HasRunDeck(Controller c) => c != null && c == c.Game.Player1 && c.Game.RunSetup?.Player1RunDeck != null;

		private static void RecordRunEdit(Controller c, RunDeckEdit edit)
		{
			if (HasRunDeck(c)) c.Game.RunDeckEditList.Add(edit);
		}

		/// <summary>
		/// <paramref name="card"/> (any card of <paramref name="c"/>'s, wherever it is) joins the run deck: the next slot (tag 1286, set
		/// first as live does) and an AddCard edit. Returns the slot (0 without a run deck).
		/// </summary>
		public static int AddToRunDeck(Controller c, IPlayable card)
		{
			if (card == null || !HasRunDeck(c)) return 0;
			int slot = c[RUN_DECK_SLOTS_USED] + 1;
			c[RUN_DECK_SLOTS_USED] = slot;
			card[RUN_DECK_SLOT] = slot;
			RecordRunEdit(c, new RunDeckEdit(RunDeckEditKind.AddCard, slot, card.Card.AssetId, card.Card.Id));
			return slot;
		}

		/// <summary>The card leaves the run deck (its slot is zeroed in the save data). False when it has no slot.</summary>
		public static bool RemoveFromRunDeck(Controller c, IPlayable card)
		{
			int slot = RunSlot(card);
			if (slot <= 0) return false;
			RecordRunEdit(c, new RunDeckEdit(RunDeckEditKind.RemoveCard, slot, card.Card.AssetId, card.Card.Id));
			return true;
		}

		/// <summary>The card gets <paramref name="enchantmentId"/> for the rest of the run (the save data's deck enchantments; later games
		/// put it on the card as they start: <see cref="ScenarioGameStart"/>). Only the record: the caller gives it in this game if it
		/// shows here.</summary>
		public static bool AddRunEnchantment(Controller c, IPlayable card, string enchantmentId)
		{
			int slot = RunSlot(card);
			if (slot <= 0 || Cards.TryFromId(enchantmentId) is not Card enchantment) return false;
			RecordRunEdit(c, new RunDeckEdit(RunDeckEditKind.AddEnchantment, slot, enchantment.AssetId, enchantment.Id));
			return true;
		}

		/// <summary>The run's starting Health goes up by <paramref name="amount"/> (Good Food).</summary>
		public static void AddRunStartingHealth(Controller c, int amount)
		{
			if (amount != 0) RecordRunEdit(c, new RunDeckEdit(RunDeckEditKind.AddStartingHealth, 0, 0, null, amount));
		}

		/// <summary>
		/// "Choose an enemy minion. Add it to your Adventure Deck" (Recruit, Round of Drinks): the same entity (a steal is a move) goes
		/// into <paramref name="c"/>'s deck with the next run-deck slot. Live (play_DALA_901: 183021 L44859): 1286 = the new slot, the
		/// board closes up, CONTROLLER_AND_ZONE_CHANGE, CONTROLLER, HIDE_ENTITY into the DECK, SHUFFLE_DECK.
		/// </summary>
		public static bool RecruitToRunDeck(Controller c, IEntity source, Minion m)
		{
			if (m == null || m.Zone?.Type != Zone.PLAY || m.Controller == c || c.DeckZone.IsFull) return false;
			AddToRunDeck(c, m);
			ExitCombat(m);
			if (m[PLACED_BY] != 0) m[PLACED_BY] = 0;
			m.Zone.Remove(m);
			ResetForBackwardMove(m); // (the slot is kept: KeptOnBackwardMove)
			GiveTo(m, c, Zone.DECK);
			return ShuffleIntoDeck.Invoke(c, source, m);
		}

		/// <summary>
		/// "Choose a friendly minion. Remove it from your Adventure Deck" (Dismiss, You're All Fired!): the minion leaves play for the
		/// set-aside zone, not the graveyard (live play_DALA_902: 1068 = 6 pulse, EXHAUSTED 0, 1254 0, ZONE SETASIDE — Generic.RemoveFromGame),
		/// and its slot leaves the run deck.
		/// </summary>
		public static bool DismissFromRunDeck(Controller c, IEntity source, Minion m)
		{
			if (m == null || m.Zone?.Type != Zone.PLAY || m.Controller != c) return false;
			RemoveFromRunDeck(c, m);
			if (m[PLACED_BY] != 0) m[PLACED_BY] = 0;
			return RemoveFromGame(m, source);
		}

		/// <summary>The cards of <paramref name="c"/>'s run deck in this game: in the deck or on the board, with a slot.</summary>
		public static List<IPlayable> RunDeckCards(Controller c) =>
			c.DeckZone.Concat<IPlayable>(c.BoardZone.GetAll()).Where(p => RunSlot(p) > 0).ToList();

		/// <summary>
		/// Shows the run-deck card <paramref name="card"/> as it now is (Take a Chance, The Upper Hand). Live (play_DALA_903 / 914): a
		/// NEW hidden entity in the deck, then a REVEAL_CARD block (Source = the spell, Target = it): SHOW_ENTITY (the card, its new cost,
		/// REVEALED 1), HIDE_ENTITY, REVEALED 0, SHOW_ENTITY into SETASIDE, SLUSH_TIME 7000. <paramref name="cost"/> = the cost it shows.
		/// </summary>
		public static IPlayable RevealRunDeckCard(Controller c, IPlayable source, IPlayable card, int cost)
		{
			Game g = c.Game;
			if (card == null || !g.History) return null;
			IPlayable shown = Entity.FromCard(c, card.Card, new EntityData { { GameTag.ZONE, (int)Zone.DECK } });
			if (cost >= 0) shown.NativeTags[GameTag.COST] = cost;
			g.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.REVEAL_CARD, source.Id, "", 0, shown.Id));
			shown.NativeTags[GameTag.REVEALED] = 1;
			g.PowerHistory.Add(PowerHistoryBuilder.ShowEntity(shown));
			g.PowerHistory.Add(new PowerHistoryHideEntity { EntityID = shown.Id, Zone = Zone.DECK });
			shown[GameTag.REVEALED] = 0;
			shown.NativeTags[GameTag.ZONE] = (int)Zone.SETASIDE;
			c.SetasideZone.Add(shown);
			g.PowerHistory.Add(PowerHistoryBuilder.ShowEntity(shown));
			g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 7000, Info = new List<int> { source.Id } });
			g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			return shown;
		}

		/// <summary>A pause the client waits out (live: META ARTIFICIAL_PAUSE + SLUSH_TIME, Info = the source).</summary>
		public static void ArtificialPause(Game g, IEntity source, int ms)
		{
			if (!g.History || ms <= 0) return;
			g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.ARTIFICIAL_PAUSE, Data = ms, Info = new List<int> { source.Id } });
			g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = ms, Info = new List<int> { source.Id } });
		}

		// ======================================================================================================= game setup

		/// <summary>Game constructor, before CREATE_GAME: the scenario's game and player tags (in CREATE_GAME, no tag changes).</summary>
		internal static void ScenarioTagsAtCreation(Game g)
		{
			ScenarioSetup s = g.RunSetup?.Scenario;
			if (s == null) return;
			foreach (KeyValuePair<GameTag, int> t in s.GameTags) g.NativeTags[t.Key] = t.Value;
			for (int i = 0; i < 2; i++)
				foreach (KeyValuePair<GameTag, int> t in s.PlayerTags[i])
					(i == 0 ? g.Player1 : g.Player2).NativeTags[t.Key] = t.Value;
		}

		/// <summary>The scenario's tags for player <paramref name="index"/>'s (0 / 1) hero as it is created, or null.</summary>
		internal static IDictionary<GameTag, int> ScenarioHeroTags(Game g, int index) =>
			g.RunSetup?.Scenario is ScenarioSetup s && s.HeroTags[index].Count > 0 ? s.HeroTags[index] : null;

		/// <summary>Game constructor, after the decks: each run-deck card's slot (tag 1286; live: the hidden deck entities in slot order,
		/// the tag shown with the card), and the player's slots used so far.</summary>
		internal static void AssignRunDeckSlots(Game g)
		{
			GameConfig cfg = g.RunSetup;
			RunDeck run = cfg?.Player1RunDeck;
			if (run == null) return;
			List<IPlayable> deck = g.Player1.DeckZone.OrderBy(p => p.Id).ToList();
			bool reversed = !cfg.Shuffle && !cfg.DrawWithRandom; // (the constructor reversed the deck list)
			int n = Math.Min(deck.Count, run.Slots.Count);
			for (int k = 0; k < n; k++)
				deck[k].NativeTags[RUN_DECK_SLOT] = run.Slots[reversed ? run.Slots.Count - 1 - k : k];
			g.Player1.NativeTags[RUN_DECK_SLOTS_USED] = Math.Max(run.SlotCount, run.Slots.Count == 0 ? 0 : run.Slots.Max());
		}

		/// <summary>
		/// Game.StartGame, after the heroes' Health: the starting heroes' own triggers (a scenario host's: Bob), the scenario's hero start
		/// tags, the run deck's enchantments and a PUZZLE_MODE scenario's start. True when the game went on its own way (PUZZLE_MODE: no
		/// mulligan, no opening hand — the caller stops).
		/// </summary>
		internal static bool ScenarioGameStart(Game g)
		{
			foreach (Controller p in new[] { g.Player1, g.Player2 })
				if (p.Hero?.Card.Power?.Trigger is { } trigger && p.Hero.ActivatedTrigger == null)
					trigger.Activate(p.Hero);
			ScenarioSetup s = g.RunSetup?.Scenario;
			if (s != null)
				for (int i = 0; i < 2; i++)
				{
					Hero hero = (i == 0 ? g.Player1 : g.Player2).Hero;
					// (a tag the engine keeps in a field — IMMUNE — isn't read from the creation tags: set it through the entity)
					foreach (KeyValuePair<GameTag, int> t in s.HeroTags[i])
						if (hero[t.Key] != t.Value) hero[t.Key] = t.Value;
					foreach (KeyValuePair<GameTag, int> t in s.HeroStartTags[i])
						hero[t.Key] = t.Value;
				}
			bool puzzle = g[GameTag.PUZZLE_MODE] > 0;
			if (puzzle) PuzzleModeStart(g);
			RunDeckEnchantments(g);
			if (!puzzle) return false;
			g.NativeTags[GameTag.STEP] = (int)Step.BEGIN_FIRST; // (no step was sent before: live's first STEP is MAIN_READY)
			g.NextStep = Step.MAIN_READY;
			return true;
		}

		/// <summary>
		/// The PUZZLE_MODE start (live Bob's Tavern, 183021 L20885): instead of the mulligan, a TRIGGER block of the game (effect index 1,
		/// TriggerKeyword PUZZLE_MODE): player 1 CURRENT_PLAYER and FIRST_PLAYER, TURN 1, the scenario's start tags, NUM_TURNS_LEFT 1 for
		/// both, NEXT_STEP MAIN_READY. No opening hand and no Coin: the scenario deals what it deals.
		/// </summary>
		private static void PuzzleModeStart(Game g)
		{
			if (g.History)
			{
				PowerHistoryBlockStart block = PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, g.Id, "", 1, 0);
				block.TriggerKeyword = (int)GameTag.PUZZLE_MODE;
				g.PowerHistory.Add(block);
			}
			Controller first = g.RunSetup.StartPlayer == 2 ? g.Player2 : g.Player1;
			g.CurrentPlayer = first;
			g.FirstPlayer = first;
			g.Turn = 1;
			if (g.RunSetup.Scenario is ScenarioSetup s)
				foreach (KeyValuePair<GameTag, int> t in s.StartTags.Where(t => !IsTurnLook(t.Key)))
					g[t.Key] = t.Value;
			g.Player1.NumTurnsLeft = 1;
			g.Player2.NumTurnsLeft = 1;
			if (g.RunSetup.Scenario is ScenarioSetup s2)
				foreach (KeyValuePair<GameTag, int> t in s2.StartTags.Where(t => IsTurnLook(t.Key)))
					g[t.Key] = t.Value;
			g[GameTag.NEXT_STEP] = (int)Step.MAIN_READY; // (the step itself runs once the run deck's enchantments are on: ScenarioGameStart)
			if (g.History)
				g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			if (g.RunSetup.Shuffle)
			{
				g.Player1.DeckZone.Shuffle();
				g.Player2.DeckZone.Shuffle();
			}
		}

		// (live order inside the PUZZLE_MODE block: the button tags, NUM_TURNS_LEFT, then the end-turn button and turn indicator looks)
		private static bool IsTurnLook(GameTag t) => t == GameTag.END_TURN_BUTTON_ALTERNATIVE_APPEARANCE || t == GameTag.TURN_INDICATOR_ALTERNATIVE_APPEARANCE;

		/// <summary>
		/// The run's deck enchantments (save data 368 / 369) on their cards. Live (224614 L32761 boss game, L31384 tavern): a TRIGGER block
		/// of the game (index -1), one hidden enchantment per pair (FULL_ENTITY SETASIDE, ZONE PLAY) — the card's own (CREATOR = it);
		/// an empty block when there are none. A pair whose slot has no card in this game (zeroed, or the card unknown) is skipped.
		/// </summary>
		private static void RunDeckEnchantments(Game g)
		{
			RunDeck run = g.RunSetup?.Player1RunDeck;
			if (run == null) return;
			if (g.History)
				g.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, g.Id, "", -1, 0));
			foreach ((int slot, Card enchantment) in run.Enchantments)
			{
				if (enchantment == null || slot <= 0) continue;
				IPlayable card = g.Player1.DeckZone.FirstOrDefault(p => RunSlot(p) == slot);
				if (card != null) AddEnchantmentBlock(g, enchantment, card, card, 0, 0, 0);
			}
			if (g.History)
				g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
		}

		/// <summary>The deck cards that always start in <paramref name="c"/>'s hand (DALA_905e "Always starts in your hand"), not counting
		/// <paramref name="already"/> (the Quests): the opening hand takes them first (Game.BeginDraw).</summary>
		public static List<IPlayable> AlwaysStartsInHand(Controller c, IList<IPlayable> already) =>
			c.DeckZone.Where(p => p[STARTS_IN_HAND] > 0 && !already.Contains(p)).ToList();

		/// <summary>
		/// <paramref name="source"/>'s trigger cancels <paramref name="c"/>'s next turn-start draw (live Bob's Tavern, 183021 L21240: at
		/// MAIN_START the draw starts, then TRIGGER Bob index 1 sets it back — no card is drawn). Game.MainDraw asks <see
		/// cref="TurnDrawCancelled"/>.
		/// </summary>
		public static void CancelTurnDraw(Controller c, IEntity source, int effectIndex)
		{
			c[TURN_DRAW_CANCELLED_BY] = source.Id;
			c[TURN_DRAW_CANCEL_INDEX] = effectIndex + 1;
		}

		/// <summary>The current turn's draw was cancelled (<see cref="CancelTurnDraw"/>): its trigger's block, and no draw.</summary>
		public static bool TurnDrawCancelled(Controller c)
		{
			int by = c[TURN_DRAW_CANCELLED_BY];
			if (by <= 0) return false;
			int index = c[TURN_DRAW_CANCEL_INDEX] - 1;
			c[TURN_DRAW_CANCELLED_BY] = 0;
			c[TURN_DRAW_CANCEL_INDEX] = 0;
			Game g = c.Game;
			if (g.History)
			{
				g.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, by, "", index, 0));
				g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			}
			return true;
		}

		/// <summary>
		/// A scenario that squelches the cards' own triggers (client SQUELCH_NON_GAME_TRIGGERS_AND_MODIFIERS on the game: Bob's Tavern,
		/// where the run's minions are shown on the board and the spells are the tavern's): only the heroes' own triggers (the scenario
		/// host's script) work. Trigger.Process asks this.
		/// </summary>
		public static bool Squelched(IPlayable owner) =>
			owner != null && owner.Game[GameTag.SQUELCH_NON_GAME_TRIGGERS_AND_MODIFIERS] > 0 && !(owner is Hero);
	}
}
