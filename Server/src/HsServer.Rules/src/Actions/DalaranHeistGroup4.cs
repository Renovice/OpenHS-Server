// HsServer addition (not upstream SabberStone): The Dalaran Heist's shared run rules (2026-10-09, group 4) — passives (client tag
// DUNGEON_PASSIVE_BUFF) that start the game set aside and act from there, the chapter twists played at game start (client
// WingDbf.PlotTwistCardId → GameConfig.Player1PlotTwist), "dummy effect" cards (DALA_728d / DALA_724d: "Holds an effect for …"),
// the Stench's swap, a player's cards costing half, immunity to Fatigue, and a small "remembered cards" list on an entity.
// Card scripts: CardSets/HsServer/DalaranHeistCardsGenHs4.cs. Evidence: the decoded live Heist games (scratchpad boss-games,
// live install sessions 2026-09-29 … 2026-10-08), cited per rule.
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Model.Zones;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		// =========================================================================================================== rule tags

		// Internal rule tags of the Heist (≥ 990000: never sent to the client). Range 994400-994599 (group 4).
		/// <summary>On a set-aside passive: its start-of-game cast is done (once per game).</summary>
		public const GameTag HEIST_PASSIVE_PLAYED = (GameTag)994400;
		/// <summary>On a minion: the Stench (DALA_831e) has swapped its Attack and Health (once per stay in a zone).</summary>
		public const GameTag HEIST_STENCH_SWAPPED = (GameTag)994401;
		/// <summary>On a player (aura): "You are Immune to Fatigue" (Elixir of Vim DALA_747): drawing from an empty deck does nothing.</summary>
		public const GameTag FATIGUE_IMMUNE = (GameTag)994402;
		/// <summary>On a player (aura): "Your cards cost half" (Robes of Gaudiness DALA_735) — rounded down (wiki Robes of Gaudiness:
		/// "All cards with an odd cost have it rounded down").</summary>
		public const GameTag CARDS_COST_HALF = (GameTag)994403;
		/// <summary>On a spell an effect casts: no SHOW_BIG_CARD / HISTORY_TARGET lead-in (live chapter twists: only the 3 s pause).</summary>
		public const GameTag CAST_WITHOUT_BIG_CARD = (GameTag)994404;
		/// <summary>"Remembered cards" on an entity: the count, then one dbf id per slot (REMEMBERED_FIRST + i), at most RememberedMax.</summary>
		public const GameTag REMEMBERED_COUNT = (GameTag)994410, REMEMBERED_FIRST = (GameTag)994411;
		public const int RememberedMax = 60;

		/// <summary>A value of ONLY_PLAY_CARDTYPE no card has: "Can't play cards this turn" (Robes of Gaudiness' Too Gaudy DALA_735e3).</summary>
		public const int NoCardType = 9999;

		// =========================================================================================================== passives

		/// <summary>A Dungeon passive (client DUNGEON_PASSIVE_BUFF 783: Crystal Gem, Captured Flag, Elixir of Vile…) — never a deck card.</summary>
		public static bool IsDungeonPassive(Card card) => card != null && card[GameTag.DUNGEON_PASSIVE_BUFF] > 0 && card.Type == CardType.SPELL;

		/// <summary>
		/// The zone a card of the player's deck list starts the game in (Game's deck setup). A Dungeon passive starts set aside, not in
		/// the deck — live (heist-session-20260929-122856 CREATE_GAME: DALA_748 id 38, live-session-20261001-162435: LOOTA_828 id 38):
		/// its FULL_ENTITY sits among the player's deck entities with ZONE SETASIDE (CREATOR = the game, DUNGEON_PASSIVE_BUFF 1).
		/// </summary>
		public static IZone StartingZoneOf(Controller c, Card card) => IsDungeonPassive(card) ? (IZone)c.SetasideZone : c.DeckZone;

		/// <summary>
		/// The passives act at the start of the game (Game.MainBegin, once the mulligan is done). Live (heist-session-20260929-122856
		/// 16700-16760, 23800-23960; live-session-20261001-162435 6227): after the mulligan, ONE game TRIGGER block (effect index −1)
		/// per player holding all of their passives in the deck list's order; for each: a NEW copy (FULL_ENTITY SETASIDE, CREATOR =
		/// the game) is cast like a spell an effect casts (ZONE PLAY, REVEALED, SHOW_BIG_CARD, HISTORY_TARGET, SLUSH_TIME 3000, its
		/// POWER block — usually its player enchantment — GRAVEYARD, SLUSH_TIME 1000), then BEGIN_ARTIFICIAL_HISTORY_TILE and
		/// OVERRIDE_HISTORY name the set-aside original and END_ARTIFICIAL_HISTORY_TILE the game. The original stays set aside.
		/// </summary>
		public static void PlayDungeonPassives(Game g)
		{
			foreach (Controller c in new[] { g.Player1, g.Player2 })
			{
				if (c == null) continue;
				List<IPlayable> passives = c.SetasideZone.Where(p => IsDungeonPassive(p.Card) && p[HEIST_PASSIVE_PLAYED] == 0)
					.OrderBy(p => p.Id).ToList();
				if (passives.Count == 0) continue;
				if (g.History) g.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, g.Id, "", -1, 0));
				foreach (IPlayable original in passives)
				{
					original[HEIST_PASSIVE_PLAYED] = 1;
					CastAtGameStart(g, c, original.Card, bigCard: true);
					if (g.History)
					{
						var shown = new List<int> { original.Id };
						g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.BEGIN_ARTIFICIAL_HISTORY_TILE, Data = 0, Info = shown });
						g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.OVERRIDE_HISTORY, Data = 0, Info = shown });
						g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.END_ARTIFICIAL_HISTORY_TILE, Data = 0, Info = new List<int> { g.Id } });
					}
				}
				if (g.History) g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
				g.DeathProcessingAndAuraUpdate();
			}
		}

		// =========================================================================================================== chapter twists

		/// <summary>
		/// A chapter's twist (client WingDbf.PlotTwistCardId, e.g. DALA_829 "Twist - The Coffers") is played for its player as the game
		/// starts (Game.StartGame, right after the run's Health block). Live (live-session-20260929-183021 383-445: The Coffers;
		/// heist-session-20260929-114419 9628-9757: The Stench): a game TRIGGER block (effect index −1) creates the twist twice
		/// (CONTROLLER = the run's player, CREATOR = the game, ZONE SETASIDE): the first stays set aside (the deck list's copy), the
		/// second goes to PLAY with only a SLUSH_TIME 3000 (no big card), runs its POWER block, goes to the GRAVEYARD, SLUSH_TIME 1000.
		/// Then the boss's lower-level missing cards and the first-player block follow.
		/// </summary>
		public static void PlayPlotTwist(Game g, Controller c, Card twist)
		{
			if (twist == null || c == null) return;
			// (before the first step: the game has no STEP yet — the death and aura checks read it; set without a history entry and
			// taken back after, so the first step still goes out as live sends it)
			bool noStep = !g.NativeTags.ContainsKey(GameTag.STEP);
			if (noStep) g.SetStepQuietly(Step.INVALID);
			if (g.History) g.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, g.Id, "", -1, 0));
			Entity.FromCard(c, twist, new EntityData { [GameTag.CREATOR] = g.Id }, c.SetasideZone); // the deck list's copy
			CastAtGameStart(g, c, twist, bigCard: false);
			g.DeathProcessingAndAuraUpdate();
			if (g.History) g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			if (noStep) g.NativeTags.Remove(GameTag.STEP);
		}

		/// <summary>A new copy of <paramref name="card"/> (FULL_ENTITY SETASIDE, CREATOR = the game) cast for <paramref name="c"/> by the
		/// game: the shared effect cast (Generic.CastSpell), with or without the big card.</summary>
		private static void CastAtGameStart(Game g, Controller c, Card card, bool bigCard)
		{
			var copy = (Spell)Entity.FromCard(c, card, new EntityData { [GameTag.CREATOR] = g.Id, [GameTag.ZONE] = (int)Zone.SETASIDE });
			if (!bigCard) copy[CAST_WITHOUT_BIG_CARD] = 1;
			if (g.History && bigCard) copy[GameTag.REVEALED] = 1;
			g.TaskQueue.StartEvent();
			CastSpell.Invoke(c, g, copy, null, 0);
			g.TaskQueue.EndEvent();
		}

		// =========================================================================================================== dummy effects

		/// <summary>
		/// A "dummy effect" card ("Recycling Dummy Effects: Holds an effect for Recycling Player Enchantment", DALA_728d / DALA_724d /
		/// DALA_744d / DALA_746d): the visual of a player enchantment's trigger is that card's own client effect. Live
		/// (live-session-20261003-184102 5529: DALA_728e gaining Armor; live-session-20261008-012951 112052: DALA_724e): inside the
		/// trigger's block a new dummy (FULL_ENTITY SETASIDE, CREATOR = the enchantment, tag 1200 = 1) gets CARD_TARGET = the target,
		/// goes to PLAY, SLUSH_TIME 3000, an empty POWER block on the target, GRAVEYARD, SLUSH_TIME 1000.
		/// </summary>
		/// <param name="inside">The trigger's own step that happens inside the dummy's POWER block (Wisdomball's transform), or null.</param>
		public static void PlayDummyEffect(Controller c, IEntity creator, string dummyId, IEntity target, System.Action inside = null)
		{
			Game g = c.Game;
			Card card = Cards.TryFromId(dummyId);
			if (card == null || !g.History) // a visual only
			{
				inside?.Invoke();
				return;
			}
			var dummy = Entity.FromCard(c, card, new EntityData
			{
				[GameTag.CREATOR] = creator?.Id ?? g.Id, [GameTag.ZONE] = (int)Zone.SETASIDE, [(GameTag)1200] = 1,
			});
			int targetId = target?.Id ?? 0;
			if (targetId > 0) dummy[GameTag.CARD_TARGET] = targetId;
			dummy[GameTag.ZONE] = (int)Zone.PLAY;
			g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 3000, Info = new List<int> { dummy.Id } });
			g.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.POWER, dummy.Id, "", 0, targetId));
			inside?.Invoke();
			g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			c.GraveyardZone.Add(dummy);
			g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 1000, Info = new List<int> { dummy.Id } });
		}

		// =========================================================================================================== the Stench

		/// <summary>
		/// "All minions' Attack and Health are swapped" (Twist - The Stench DALA_831 / its player enchantment DALA_831e). Live
		/// (heist-session-20260929-114419 9667-9750, 10420-10456): every minion card of both players (decks, hands, boards) gets
		/// DALA_831e2 "Stinky" with its Attack and Health swapped; a minion made later gets it as it is made (CREATOR = DALA_831e).
		/// Ours: at once for the cards there, then (DALA_831e's ZONE trigger) for each minion entering a hand or the board that
		/// wasn't swapped yet. Once per minion (HEIST_STENCH_SWAPPED): a card returned to the hand is a new card and swaps again.
		/// </summary>
		public static void StenchSwap(Game g, Controller c, IEntity source, IPlayable p)
		{
			if (!(p is Minion m) || m[HEIST_STENCH_SWAPPED] > 0 || m.Zone == null) return;
			m[HEIST_STENCH_SWAPPED] = 1;
			new Tasks.SimpleTasks.SwapAttackHealthTask(Tasks.SimpleTasks.EntityType.TARGET, "DALA_831e2").Process(g, c, source, m);
		}

		/// <summary>Every minion card of both players now (decks, hands, boards): the Stench's first pass.</summary>
		public static IEnumerable<IPlayable> AllMinionCards(Game g) =>
			new[] { g.Player1, g.Player2 }.Where(x => x != null)
				.SelectMany(x => x.DeckZone.GetAll().Concat(x.HandZone.GetAll()).Concat(x.BoardZone.GetAll()))
				.Where(p => p is Minion && !(p is Location)).ToList();

		// =========================================================================================================== costs, Fatigue

		/// <summary>
		/// A player's rule over all their cards' costs, applied after every other cost change (Playable.Cost): "Your cards cost half"
		/// (CARDS_COST_HALF; rounded down — wiki Robes of Gaudiness: "All cards with 1, 3, 5, 7, 9,... cost will cost 0, 1, 2, 3, 4").
		/// Hero Powers aren't cards.
		/// </summary>
		public static int PlayerCostRule(IPlayable p, int cost)
		{
			Controller c = p.Controller;
			if (c == null || p is HeroPower || p is Enchantment || c[CARDS_COST_HALF] <= 0) return cost;
			return cost / 2;
		}

		/// <summary>"You are Immune to Fatigue" (Elixir of Vim): drawing from an empty deck and "take Fatigue damage" do nothing.</summary>
		public static bool FatigueImmune(Controller c) => c != null && c[FATIGUE_IMMUNE] > 0;

		// =========================================================================================================== remembered cards

		/// <summary>Remembers <paramref name="card"/> on <paramref name="holder"/> (internal tags, cloned with the game; at most
		/// <see cref="RememberedMax"/>): "the spells you've cast on this minion" (Elistra), "the minions killed by this weapon"
		/// (Soulreaper's Scythe).</summary>
		public static void Remember(IEntity holder, Card card, int max = RememberedMax)
		{
			if (holder == null || card == null) return;
			int n = holder[REMEMBERED_COUNT];
			if (n >= System.Math.Min(max, RememberedMax)) return;
			holder[REMEMBERED_FIRST + n] = card.AssetId;
			holder[REMEMBERED_COUNT] = n + 1;
		}

		/// <summary>The cards remembered on <paramref name="holder"/>, in order.</summary>
		public static List<Card> Remembered(IEntity holder)
		{
			var list = new List<Card>();
			if (holder == null) return list;
			for (int i = 0; i < holder[REMEMBERED_COUNT] && i < RememberedMax; i++)
				if (Cards.FromAssetId(holder[REMEMBERED_FIRST + i]) is Card card) list.Add(card);
			return list;
		}
	}
}
