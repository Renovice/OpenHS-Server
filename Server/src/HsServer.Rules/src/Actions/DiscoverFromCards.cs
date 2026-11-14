using System;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks;
using SabberStoneCore.Tasks.SimpleTasks;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>
		/// HsServer: up to <paramref name="count"/> random cards of <paramref name="cards"/> with different card ids (the
		/// options of a "Discover / pick a card from your deck or hand"; fewer when there are fewer: wiki Discover).
		/// </summary>
		public static List<IPlayable> PickFrom(IEnumerable<IPlayable> cards, Game game, Func<IPlayable, bool> filter, int count = 3)
		{
			var candidates = new List<IPlayable>();
			foreach (IPlayable p in cards)
				if ((filter == null || filter(p)) && !candidates.Exists(x => x.Card.Id == p.Card.Id))
					candidates.Add(p);
			int n = Math.Min(count, candidates.Count);
			for (int i = 0; i < n; i++)
			{
				int j = game.Random.Next(i, candidates.Count);
				(candidates[i], candidates[j]) = (candidates[j], candidates[i]);
			}
			return candidates.GetRange(0, n);
		}

		/// <summary>HsServer: <see cref="PickFrom"/> over the deck.</summary>
		public static List<IPlayable> PickFromDeck(Controller c, Func<IPlayable, bool> filter, int count = 3) =>
			PickFrom(c.DeckZone, c.Game, filter, count);

		/// <summary>
		/// HsServer: "Discover a card from your deck" (wiki Discover: such cards draw the card): <see cref="DiscoverFromCards"/>
		/// whose pick is drawn, unless <paramref name="onPick"/> says otherwise ("... to summon / to cast").
		/// </summary>
		public static bool DiscoverFromDeck(Controller c, IEntity source, IList<IPlayable> deckCards,
			Action<Game, Controller, IEntity, IPlayable> onPick = null) =>
			DiscoverFromCards(c, source, deckCards, onPick ?? ((g, c2, s, card) => Draw(c2, card)));

		/// <summary>
		/// HsServer: Discover / pick one of some existing cards (in a deck or a hand). Live (Tracking:
		/// live-20261001-auchenai, practice-innkeeper-20260929-1x-spells; Dispose of Evidence: live-20261001-startofgame): the
		/// options are new copies set aside (CREATOR = the source, LINKED_ENTITY / COPIED_FROM_ENTITY_ID = their card) and the
		/// cards don't move; after the pick the chosen copy goes to the graveyard (LAST_AFFECTED_BY = the source; revealed
		/// when it stood for a hidden deck card), then <paramref name="onPick"/> acts on the real card if it is still where it
		/// was. The unpicked copies stay set aside.
		/// </summary>
		public static bool DiscoverFromCards(Controller c, IEntity source, IList<IPlayable> cards,
			Action<Game, Controller, IEntity, IPlayable> onPick)
		{
			if (cards.Count == 0) return false;
			c.Game.FlushPendingFx(source.Id); // (a held-back buff effect opens before the options: Generic.CreateChoiceCards)
			var offered = new List<IPlayable>();
			var zones = new Dictionary<int, Model.Zones.IZone>();
			foreach (IPlayable card in cards)
			{
				IPlayable copy = Entity.FromCard(c, card.Card, new EntityData
				{
					{ GameTag.CREATOR, source.Id },
					{ GameTag.LINKED_ENTITY, card.Id },
					{ HsTag.COPIED_FROM_ENTITY_ID, card.Id },
				}, c.SetasideZone);
				copy[HsTag.WAS_DISCOVER_OPTION] = 1;
				offered.Add(copy);
				zones[card.Id] = card.Zone;
			}
			return CreateChoiceEntities(c, source, offered, ChoiceAction.STACK, new CustomTask((g, c2, s, chosen, stack) =>
			{
				if (chosen == null || chosen.Zone?.Type != Zone.SETASIDE) return;
				g.IdEntityDic.TryGetValue(chosen[GameTag.LINKED_ENTITY], out IPlayable card);
				bool stillThere = card != null && zones.TryGetValue(card.Id, out Model.Zones.IZone zone) && card.Zone == zone;
				if (card?.Zone?.Type == Zone.DECK)
					DeckCardToGraveyard(chosen, source.Id, suppressAnimation: false);
				else
				{
					NoteAffected(chosen, source.Id);
					c2.GraveyardZone.Add(chosen.Zone.Remove(chosen));
				}
				if (stillThere) onPick(g, c2, s, card);
				// live (Sightless Watcher): a pick whose real card stays hidden in the deck names it for the history tile
				// (HISTORY_TARGET = the deck card); a drawn one shows itself (Tracking: none)
				if (g.History && card?.Zone?.Type == Zone.DECK)
					g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.HISTORY_TARGET, Data = 0, Info = new List<int> { card.Id } });
			}));
		}
	}
}
