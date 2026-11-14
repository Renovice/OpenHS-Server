using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>SUPPRESS_MILL_ANIMATION (3481): a mass deck destruction shows no per-card animation.</summary>
		private const GameTag SuppressMillAnimation = (GameTag)3481;

		/// <summary>
		/// HsServer: a DECK card an effect destroys or removes (Gnomeferatu, Void Contract, mill): not a draw and not a
		/// discard (discard is from the hand only; engine-rules.md), so no discard trigger. Live (live-20261002-visuals) reveals
		/// it to both players straight into the graveyard: SHOW_ENTITY with the card and ZONE=GRAVEYARD inside,
		/// LAST_AFFECTED_BY = the effect's card, and SUPPRESS_MILL_ANIMATION=1 when many go at once (Void Contract); no
		/// separate ZONE change. Without the reveal the client got an empty card id (a card back). The same reveal takes a
		/// picked "Discover from your deck" option (a set-aside copy) to the graveyard (Generic.DiscoverFromDeck, live Tracking).
		/// </summary>
		public static void DeckCardToGraveyard(IPlayable card, int sourceId, bool suppressAnimation)
		{
			Game g = card.Game;
			card.Zone.Remove(card);
			var data = ((Entity)card)._data;
			if (sourceId != 0) data[GameTag.LAST_AFFECTED_BY] = sourceId;
			if (suppressAnimation) data[SuppressMillAnimation] = 1;
			data[GameTag.ZONE] = (int)Zone.GRAVEYARD; // inside the reveal; the zone add below then changes nothing
			if (g.History)
				g.PowerHistory.Add(PowerHistoryBuilder.ShowEntity(card));
			card.Controller.GraveyardZone.Add(card);
		}
	}
}
