// HsServer addition (not upstream SabberStone): Dredge (Voyage to the Sunken City) and "put on the bottom of your deck".
using System;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.SimpleTasks;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>Live's "to the top of the deck" effect after a Dredge pick (Source = the deck owner's hero, no targets).</summary>
		private const string DredgeFx = "ReuseFX_Generic_DeckAE_SpawnToDeckQuick_Blue_Super:c3cd0c226de505b40bb1cee891c7733c";

		/// <summary>
		/// HsServer: Dredge (wiki: "Look at the bottom 3 cards of your deck. Put one on top."; "Dredge does not shuffle your
		/// deck"; "the card at the left is the bottom-most card in your deck"). <paramref name="deckOwner"/>'s deck (Disarming
		/// Elemental: the opponent's), <paramref name="chooser"/> picks. Live (live-20261006-dredge, Tuskarrrr Trawler): in the
		/// card's POWER block SLUSH_TIME 7000 and OVERRIDE_HISTORY on it, then copies of the bottom three set aside (CREATOR =
		/// the card, COPIED_FROM_ENTITY_ID / LINKED_ENTITY = the deck card); the pick's copy goes to the graveyard
		/// (LAST_AFFECTED_BY = the card), the real card is shown, HISTORY_TARGET, the hero's to-deck effect, hidden again — now
		/// on top. <paramref name="then"/> gets the card now on top (the "If it's a …" part), or nothing with an empty deck.
		/// </summary>
		/// <param name="cards">HsServer (LOST_CITY): the cards to look at instead of the bottom three (Eyes in the Sky: "Look at 3 cards
		/// in your enemy's deck. Pick one to put on top.").</param>
		public static void Dredge(Controller chooser, Controller deckOwner, IEntity source, Action<Game, Controller, IPlayable> then = null,
			IList<IPlayable> cards = null)
		{
			Game g = chooser.Game;
			var bottom = cards?.ToList() ?? deckOwner.DeckZone.Take(3).ToList(); // slot 0 is the bottom
			if (bottom.Count == 0) return;
			if (g.History)
			{
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 7000, Info = new List<int> { source.Id } });
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.OVERRIDE_HISTORY, Data = 0, Info = new List<int> { source.Id } });
			}
			List<IPlayable> offered = DeckCardCopies(chooser, source, bottom);
			CreateChoiceEntities(chooser, source, offered, ChoiceAction.STACK, new CustomTask((g2, c2, s2, chosen, stack) =>
			{
				if (chosen == null) return;
				g2.IdEntityDic.TryGetValue(chosen[GameTag.LINKED_ENTITY], out IPlayable real);
				NoteAffected(chosen, s2.Id);
				if (chosen.Zone?.Type == Zone.SETASIDE) c2.GraveyardZone.Add(chosen.Zone.Remove(chosen));
				if (real?.Zone?.Type != Zone.DECK || real.Controller != deckOwner) return;
				deckOwner.DeckZone.Remove(real);
				deckOwner.DeckZone.Add(real); // the top
				if (g2.History)
				{
					g2.PowerHistory.Add(PowerHistoryBuilder.ShowEntity(real));
					g2.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.HISTORY_TARGET, Data = 0, Info = new List<int> { real.Id } });
					if (g2.OpenSubSpell(deckOwner.Hero, DredgeFx, Array.Empty<int>()))
						g2.CloseSubSpells(deckOwner.Hero.Id);
					g2.PowerHistory.Add(PowerHistoryBuilder.HideEntity(real));
				}
				then?.Invoke(g2, c2, real);
			}));
		}

		/// <summary>HsServer: the options of a "look at a card in your deck" choice (Dredge, Sphere of Sapience): copies set aside,
		/// CREATOR = the effect's source, COPIED_FROM_ENTITY_ID / LINKED_ENTITY = the deck card (live) — the deck card itself stays
		/// where it is until the pick moves it.</summary>
		public static List<IPlayable> DeckCardCopies(Controller chooser, IEntity source, IEnumerable<IPlayable> cards) =>
			cards.Select(card => Entity.FromCard(chooser, card.Card, new EntityData
			{
				{ GameTag.CREATOR, source.Id },
				{ GameTag.LINKED_ENTITY, card.Id },
				{ HsTag.COPIED_FROM_ENTITY_ID, card.Id },
			}, chooser.SetasideZone)).ToList();

		/// <summary>HsServer: a decision's options are used up after the pick: each goes to the graveyard, touched by the choice's
		/// source — the pick first (its touch came with the pick: Choose), then the others (live Sphere of Sapience,
		/// live-20261002 session-20261002-204752: the top card's copy, then A New Fate). Nothing goes to the hand.</summary>
		public static void OptionsToGraveyard(Controller c, IEntity source, IEnumerable<int> options, IPlayable picked)
		{
			Game g = c.Game;
			IEnumerable<int> order = picked == null ? options : new[] { picked.Id }.Concat(options.Where(id => id != picked.Id));
			foreach (int id in order)
			{
				if (!g.IdEntityDic.TryGetValue(id, out IPlayable option) || option.Zone?.Type != Zone.SETASIDE) continue;
				if (option != picked) NoteAffected(option, source);
				option.Controller.GraveyardZone.Add(option.Zone.Remove(option));
			}
		}

		/// <summary>HsServer: "Put a … on the bottom of your deck" (the Azsharan cards' Sunken versions, Bottomfeeder): a new card at
		/// the deck's bottom (slot 0), not shuffled; CREATOR = the effect's source.</summary>
		public static IPlayable PutOnBottom(Controller c, IEntity source, Card card)
		{
			if (card == null || c.DeckZone.IsFull) return null;
			IPlayable p = Entity.FromCard(c, card, new EntityData { { GameTag.CREATOR, source.Id } });
			c.DeckZone.Add(p, 0);
			if (c.Game.History) c.Game.PowerHistory.Add(PowerHistoryBuilder.HideEntity(p)); // like a shuffle, without the SHUFFLE_DECK
			return p;
		}
	}
}
