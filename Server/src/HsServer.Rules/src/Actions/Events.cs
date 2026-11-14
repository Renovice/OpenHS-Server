// HsServer: shared rules first needed by the event cards (BE_EVENT_, CATA_EVENT_, TIME_EVENT_, BE_036...).

using System;
using System.Collections.Generic;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>
		/// "You can only play minions / spells for the rest of the turn" (HsTag.ONLY_PLAY_CARDTYPE: Playable.IsPlayRestricted) shown on the hand
		/// as live does: every hand card of another type gets LITERALLY_UNPLAYABLE 1 and UNPLAYABLE_VISUALS 1 (live-20261007-vfx-decks 15338,
		/// Arrival of the Titans), cleared when the restriction ends (its one-turn enchantment goes at the end of the turn). Cards drawn later
		/// in the turn get them too (unrecorded: live-checks).
		/// </summary>
		public static void SyncTurnPlayRestriction(Game g)
		{
			if (g.Player1 == null || g.Player2 == null) return;
			foreach (Controller c in new[] { g.Player1, g.Player2 })
			{
				int only = c[HsTag.ONLY_PLAY_CARDTYPE];
				foreach (IPlayable p in c.HandZone)
				{
					bool lockIt = only > 0 && (int)p.Card.Type != only;
					bool locked = p[HsTag.LOCKED_BY_TURN_RESTRICTION] > 0;
					if (lockIt == locked) continue;
					p[HsTag.LOCKED_BY_TURN_RESTRICTION] = lockIt ? 1 : 0;
					p[HsTag.LITERALLY_UNPLAYABLE] = lockIt ? 1 : 0;
					p[HsTag.UNPLAYABLE_VISUALS] = lockIt ? 1 : 0;
				}
			}
		}

		/// <summary>Client tag 4741: how many times an effect touched this entity (live sends it, +1, right before LAST_AFFECTED_BY: all 9,628
		/// LAST_AFFECTED_BY changes in the live captures follow it).</summary>
		public const GameTag TIMES_AFFECTED = (GameTag)4741;

		/// <summary>
		/// "An effect of <paramref name="source"/> touched <paramref name="target"/>": TIMES_AFFECTED +1, then LAST_AFFECTED_BY = the source — as
		/// live (user 2026-10-08: match live). Where live sends it: damage (after the DAMAGE metadata, before DAMAGE: Character.TakeDamage), a
		/// destroy (after TO_BE_DESTROYED: Generic.Destroy), a buff (after the stat changes: AddEnchantmentBlock; Crusader Aura, Watfin), a
		/// return to hand (ReturnHandTask), a picked choice (before it moves: Choose), a card moved by an effect (Discover copies, mills).
		/// The client reads LAST_AFFECTED_BY for combat death sounds and for what a source killed (Anomalus, Karazhan portals).
		/// </summary>
		public static void NoteAffected(IPlayable target, IEntity source)
		{
			// a client message only: written where the game records history (not in AI search copies: their entity ids differ)
			if (target == null || source == null || !target.Game.History) return;
			target[TIMES_AFFECTED] = target[TIMES_AFFECTED] + 1;
			target[GameTag.LAST_AFFECTED_BY] = source.Id;
		}

		/// <summary>The same with the source's entity id.</summary>
		public static void NoteAffected(IPlayable target, int sourceId)
		{
			if (target == null || sourceId == 0 || !target.Game.History) return;
			target[TIMES_AFFECTED] = target[TIMES_AFFECTED] + 1;
			target[GameTag.LAST_AFFECTED_BY] = sourceId;
		}

		/// <summary>Client tags of a card that names another card in its text (TAG_SCRIPT_DATA_NUM_6: a card dbf id) and hides that text
		/// from the opponent (SUPPRESS_ALT_CARD_TEXT_FOR_OPPONENT).</summary>
		public const GameTag TAG_SCRIPT_DATA_NUM_6 = (GameTag)2921, SUPPRESS_ALT_CARD_TEXT_FOR_OPPONENT = (GameTag)4300;

		/// <summary>
		/// "(It's {5}!)": a hand card with Power.ShowsTopOfDeck names the top card of its owner's deck (Sharp-Eyed Lookout; user 2026-10-08: "it
		/// just shows u the top card of your deck"): TAG_SCRIPT_DATA_NUM_6 = its dbf id and USE_ALTERNATE_CARD_TEXT 1 while the deck has one.
		/// </summary>
		public static void SyncTopOfDeckText(Game g)
		{
			if (g.Player1 == null || g.Player2 == null) return;
			foreach (Controller c in new[] { g.Player1, g.Player2 })
			{
				int top = c.DeckZone.TopCard?.Card.AssetId ?? 0;
				foreach (IPlayable p in c.HandZone)
				{
					if (p.Card.Power?.ShowsTopOfDeck != true) continue;
					if (p[SUPPRESS_ALT_CARD_TEXT_FOR_OPPONENT] != 1) p[SUPPRESS_ALT_CARD_TEXT_FOR_OPPONENT] = 1;
					if (p[TAG_SCRIPT_DATA_NUM_6] != top) p[TAG_SCRIPT_DATA_NUM_6] = top;
					int alt = top > 0 ? 1 : 0;
					if (p[GameTag.USE_ALTERNATE_CARD_TEXT] != alt) p[GameTag.USE_ALTERNATE_CARD_TEXT] = alt;
				}
			}
		}

		/// <summary>
		/// The run of hand cards matching <paramref name="match"/> connected to the slot the played card left (wiki Dirdra: "playing Crewmate 5
		/// summons Crewmates 3, 4, and 6"; Dragon Soul, Shattered: "The other Essences in your hand have to be adjacent to the one you played",
		/// Blizzard forum t/160964), in live's order (live-20261008-120539 19719: Red played from slot 4 cast 5, 6, then 3, 2, 1): the right run
		/// (nearest first), then the left run. Taken once, at the play.
		/// </summary>
		public static List<IPlayable> AdjoiningRunInHand(Controller c, Func<IPlayable, bool> match)
		{
			var run = new List<IPlayable>();
			int slot = c.PlayedFromSlot;
			if (slot < 0) return run;
			var hand = new List<IPlayable>(c.HandZone.GetAll());
			for (int i = slot; i < hand.Count && match(hand[i]); i++) run.Add(hand[i]);
			for (int i = slot - 1; i >= 0 && i < hand.Count && match(hand[i]); i--) run.Add(hand[i]);
			return run;
		}

		/// <summary>
		/// "… This continues to the left and right with 1 less" (M.O.T.H.E.R. in the hand, Dark Terraforming on the board): the card at
		/// <paramref name="index"/> gets <paramref name="amount"/>, then each step outward (left, then right) 1 less, while above 0. Live
		/// (live-20261001-startofgame 14437, M.O.T.H.E.R.): the target first, then outward; positions after the played card left.
		/// </summary>
		public static void Ripple<T>(IList<T> row, int index, int amount, Action<T, int> apply)
		{
			if (index < 0 || index >= row.Count || amount <= 0) return;
			var cards = new List<T>(row);
			apply(cards[index], amount);
			for (int d = 1; amount - d > 0 && (index - d >= 0 || index + d < cards.Count); d++)
			{
				if (index - d >= 0) apply(cards[index - d], amount - d);
				if (index + d < cards.Count) apply(cards[index + d], amount - d);
			}
		}
	}
}
