// HsServer addition (not upstream SabberStone): March of the Lich King's shared rules (Manathirst, "a friendly Undead died
// after your last turn").
using System.Linq;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>The client's MANATHIRST tag: the card's Manathirst number (its text's "Manathirst (X)").</summary>
		public const GameTag MANATHIRST = (GameTag)2498;

		/// <summary>
		/// HsServer: Manathirst (wiki Manathirst: "if the player controls 6 mana crystals, the card's bonus effect will activate";
		/// "Temporary mana crystals do not count for Manathirst effects; only permanent mana crystals do" — empty ones count,
		/// Nourish does, The Coin doesn't). <paramref name="threshold"/>: a second Manathirst on the card (Crystal Broker's 10),
		/// else its MANATHIRST tag. When it applies (minions and weapons played from hand; spells cast by other cards too) is the
		/// card's effect: its play / cast task asks.
		/// </summary>
		public static bool Manathirst(IPlayable card, int threshold = 0)
		{
			if (card?.Controller == null) return false;
			int need = threshold > 0 ? threshold : card[MANATHIRST] > 0 ? card[MANATHIRST] : card.Card[MANATHIRST];
			return need > 0 && card.Controller.BaseMana >= need;
		}

		/// <summary>
		/// HsServer: "If a friendly Undead died after your last turn" — one of the player's Undead died since their last turn
		/// ended (during the opponent's turn or this one). Controller.DiedThisGame past the mark set when their turn ended
		/// (HsTag.DIED_AT_LAST_TURN_END); before their first turn ends, every death counts.
		/// </summary>
		public static bool UndeadDiedAfterYourLastTurn(Controller c) =>
			c.DiedThisGameCards.Skip(c[HsTag.DIED_AT_LAST_TURN_END]).Any(p => p is Minion && p.Card.IsRace(Race.UNDEAD));

		/// <summary>The Undead of the player that died after their last turn ended, in death order (High Cultist Basaleph).</summary>
		public static System.Collections.Generic.List<IPlayable> UndeadDiedAfterYourLastTurnCards(Controller c) =>
			c.DiedThisGameCards.Skip(c[HsTag.DIED_AT_LAST_TURN_END]).Where(p => p is Minion && p.Card.IsRace(Race.UNDEAD)).ToList();
	}
}
