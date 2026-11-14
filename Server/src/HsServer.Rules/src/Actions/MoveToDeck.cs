using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>
		/// HsServer: shuffle a card into <paramref name="c"/>'s deck from wherever it is (the MoveToDeck task, and scripts:
		/// Bibliomite, Dispose of Evidence). Backward move (rule Z5a): its enchantments and tags reset. Full deck (rule Z1): a
		/// minion in play dies, anything else goes to the graveyard. Live (live-20261001-startofgame, Dispose of Evidence):
		/// HIDE_ENTITY into DECK, ZONE=DECK, SHUFFLE_DECK.
		/// </summary>
		public static bool MoveToDeck(Controller c, IEntity source, IPlayable p)
		{
			if (p.Zone?.Type == Zone.DECK)
				return false;
			// HsServer: a dead card isn't moved itself: it is set aside and a new one is shuffled in (ReplaceDead, live).
			if (p.Zone?.Type == Zone.GRAVEYARD)
			{
				if (!c.DeckZone.IsFull)
					ShuffleIntoDeck.Invoke(c, source, ReplaceDead(c, p));
				return false;
			}
			// HsServer: rules C2 Z1 — into a full deck: a minion in play dies (its Death Event still resolves); a card
			// from anywhere else just goes to the Graveyard.
			if (c.DeckZone.IsFull)
			{
				if (p is Minion m && p.Zone?.Type == Zone.PLAY)
					FullZoneKill(m);
				else if (p.Zone != null && p.Zone.Type != Zone.GRAVEYARD)
					p.Controller.GraveyardZone.Add(p.Zone.Remove(p));
				return false;
			}
			IPlayable removedEntity = p.Zone?.Remove(p) ?? p;
			ResetForBackwardMove(removedEntity); // HsServer: enchantments and tags go (a Titan gets new abilities)
			if (removedEntity.Controller != c)
			{
				removedEntity.Controller = c;
				removedEntity[GameTag.CONTROLLER] = c.PlayerId;
			}

			c.Game.Log(LogLevel.INFO, BlockType.PLAY, "MoveToDeck",
				!c.Game.Logging ? "" : $"{c.Name} is taking control of {p} and shuffled into his deck.");

			return ShuffleIntoDeck.Invoke(c, source, p);
		}
	}
}
