using SabberStoneCore.Enums;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>
		/// HsServer: a Destroy effect on a card wherever it is (Advanced rulebook § "Destroy effects in all zones";
		/// hearthstone-rules.md C3): in play it is set pending destroy (dies in the next Death Creation Step); a Secret dies
		/// at once; a deck card goes to the graveyard, revealed, nothing else (<see cref="DeckCardToGraveyard"/>); a hand
		/// card goes to the graveyard and counts as a discard (no Death Event, no Deathrattle); a set-aside card goes to the
		/// graveyard; a graveyard card is untouched. <paramref name="many"/>: part of a mass destruction (no per-card mill
		/// animation for deck cards).
		/// </summary>
		/// <summary>HsServer (2026-10-08): client tag 2747 — the entity that destroys it, sent right before TO_BE_DESTROYED (live).</summary>
		public const GameTag DESTROYED_BY = (GameTag)2747;

		public static void Destroy(IPlayable card, IEntity source, bool many = false)
		{
			ExitCombat(card);
			switch (card.Zone?.Type)
			{
				case Zone.SECRET when card is Spell secret:
					DestroySecret(secret, reveal: false, source); // (rule book Z3's "and revealed" is Kezan Mystic's only)
					break;
				case Zone.DECK:
					card.Controller[HsTag.CARDS_DESTROYED_THIS_GAME] = card.Controller[HsTag.CARDS_DESTROYED_THIS_GAME] + 1; // (SPACE)
					DeckCardToGraveyard(card, source?.Id ?? 0, many);
					AnywhereDeathrattle(card); // HsServer (VIOLET_HOLD): IMPFERNAL! destroyed in the deck
					RunDestroyedTask(card); // HsServer (WILD_WEST): "When this is … destroyed" (Power.DestroyedTask), before the next
					break;
				case Zone.HAND:
					card.Controller[HsTag.CARDS_DESTROYED_THIS_GAME] = card.Controller[HsTag.CARDS_DESTROYED_THIS_GAME] + 1; // (SPACE)
					DiscardBlock.Invoke(card.Controller, card);
					break;
				case Zone.SETASIDE:
					card.Controller.GraveyardZone.Add(card.Zone.Remove(card));
					break;
				case Zone.GRAVEYARD:
				case Zone.REMOVEDFROMGAME:
					break;   // rulebook: no known effect
				default:   // in play (heroes and weapons have no zone list here)
					// HsServer (CATACLYSM): a minion's destroy effect killed it (Faceless Replicator)
					if (card is Minion victim && source is Minion killer && victim[HsTag.KILLED_BY] == 0) victim[HsTag.KILLED_BY] = killer.Id;
					// HsServer (2026-10-08): live names the destroyer first — 2747 = the source, then TO_BE_DESTROYED (668 of 706; Gigafin
					// vfx-decks 110421, Poisonous, Shadow Word: Death)
					if (source != null && source.Id > 3 && card[DESTROYED_BY] != source.Id) card[DESTROYED_BY] = source.Id;
					// the source card's own Destroy effect at it, when it has one (DestroyFx: Ritual of Doom's Dark Bargain death)
					if (source is IPlayable fxSource) DestroyFx(fxSource, card, card.Destroy);
					else card.Destroy();
					// live (live-20261008-issues 6008: Shadow Word: Death): TO_BE_DESTROYED, then 4741 and LAST_AFFECTED_BY
					if (source != null && card.ToBeDestroyed) NoteAffected(card, source);
					break;
			}
		}
	}
}
