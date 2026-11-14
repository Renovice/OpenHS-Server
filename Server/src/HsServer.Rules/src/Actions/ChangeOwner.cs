using SabberStoneCore.Enums;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>
		/// HsServer: a card changes owner — the same entity (a swap or a steal is a zone move, not a new card: user rule),
		/// out of its zone, CONTROLLER set; the caller puts it where it goes (hand, deck, board). The client stream re-checks
		/// who sees it on the CONTROLLER change (a hand card leaves its old owner's view).
		/// </summary>
		/// <param name="into">HsServer (WILD_WEST): the zone the caller puts it into, when known — live (live-20261007-vfx-decks
		/// 2972-2990, Benevolent Banker stealing from the enemy deck) sends META_DATA CONTROLLER_AND_ZONE_CHANGE Info = [card, old
		/// controller, new controller, old zone, new zone] right before the CONTROLLER change.</param>
		/// <summary>
		/// HsServer: "Return an enemy minion to YOUR hand" (Shadow Word: Steal): a bounce (a backward move: its enchantments go,
		/// ResetForBackwardMove) that also changes its owner (GiveTo with HAND: live's CONTROLLER_AND_ZONE_CHANGE). Into a full
		/// hand it is destroyed (wiki Return to hand: "the returning card will be destroyed"; rules C2 Z1). The minion now in
		/// <paramref name="c"/>'s hand, or null. A minion of <paramref name="c"/>'s own: a plain return.
		/// </summary>
		public static IPlayable ReturnToYourHand(Controller c, Minion minion)
		{
			if (minion == null || minion.Zone?.Type != Zone.PLAY) return null;
			if (minion.Controller == c) return ReturnToHand(c, minion);
			ExitCombat(minion);
			if (c.HandZone.IsFull)
			{
				FullZoneKill(minion);
				return null;
			}
			Controller owner = minion.Controller;
			if (!RemoveFromZone.Invoke(owner, minion)) return null;
			ResetForBackwardMove(minion);
			GiveTo(minion, c, Zone.HAND);
			return AddHandPhase.Invoke(c, minion) ? minion : null;
		}

		public static IPlayable GiveTo(IPlayable card, Controller to, Zone into = Zone.INVALID)
		{
			Zone from = card.Zone?.Type ?? (Zone)card[GameTag.ZONE];
			card.Zone?.Remove(card);
			if (card.Controller != to && into != Zone.INVALID && card.Game.History)
				card.Game.PowerHistory.Add(new Kettle.PowerHistoryMetaData
				{
					Type = MetaDataType.CONTROLLER_AND_ZONE_CHANGE, Data = 0,
					Info = new System.Collections.Generic.List<int> { card.Id, card.Controller.PlayerId, to.PlayerId, (int)from, (int)into }
				});
			if (card.Controller != to)
			{
				card.Controller = to;
				card[GameTag.CONTROLLER] = to.PlayerId;
			}
			return card;
		}
	}
}
