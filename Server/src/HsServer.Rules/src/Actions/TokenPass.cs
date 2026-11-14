// HsServer addition (not upstream SabberStone): shared rules the token pass (2026-10-09) needed — the non-collectible cards scripted
// cards create, which the coverage tool never checked (`HsServer cards --tokens` lists them now). Card scripts:
// CardSets/HsServer/TokenPassCardsGenHs.cs.
using System;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>
		/// A crafted potion (Kazakus CFM_621, Mixologist VAC_523: "works exactly like Kazakus", wiki Mixologist): the potion card
		/// <paramref name="look"/> (its own Cost: Kazakus Potion CFM_621t / t14 / t15, Mixologist's Special VAC_523t) with the two
		/// ingredients' effects — in the client's ingredient order, each ingredient's TAG_SCRIPT_DATA_NUM_1 (Felbloom / Mystic Wool 1,
		/// Ichor of Undeath 3, Heart of Fire 4, Icecap 7 …; SabberStone's PotionGenerating sorted on it), lower first. Make the entity
		/// with Entity.FromCard.
		/// </summary>
		public static Card CraftPotion(Card look, Card ingredient1, Card ingredient2)
		{
			if (ingredient2[GameTag.TAG_SCRIPT_DATA_NUM_1] < ingredient1[GameTag.TAG_SCRIPT_DATA_NUM_1])
				(ingredient1, ingredient2) = (ingredient2, ingredient1);
			return CraftedCard(look, ingredient1, ingredient2);
		}

		/// <summary>
		/// "Shuffle both decks together and give half to each player" (Aleatoric Cube LOOTA_820): every card of both decks, shuffled,
		/// dealt one by one starting with <paramref name="c"/> (an odd card out goes to <paramref name="c"/>: DOUBT, the wiki is
		/// silent). The same entities change owner (GiveTo); returns the cards <paramref name="c"/> got.
		/// </summary>
		public static List<IPlayable> ShuffleDecksTogether(Controller c)
		{
			Game g = c.Game;
			var all = c.DeckZone.GetAll().Concat(c.Opponent.DeckZone.GetAll()).ToList();
			foreach (IPlayable card in all) card.Zone?.Remove(card);
			for (int i = all.Count - 1; i > 0; i--)
			{
				int j = g.Random.Next(i + 1);
				(all[i], all[j]) = (all[j], all[i]);
			}
			var mine = new List<IPlayable>();
			for (int i = 0; i < all.Count; i++)
			{
				Controller to = i % 2 == 0 ? c : c.Opponent;
				IPlayable card = GiveTo(all[i], to, Zone.DECK);
				if (to.DeckZone.IsFull) { to.GraveyardZone.Add(card); continue; }
				to.DeckZone.Add(card);
				if (to == c) mine.Add(card);
			}
			return mine;
		}

		/// <summary>
		/// "Each minion randomly chooses a side of the battlefield" (Scroll of Confusion LOOTA_839): every minion in play (not an
		/// untouchable one) picks either player with even chances; those that picked the other side switch together at the end of
		/// that side's board (SwitchSides: a minion with no room there dies, as a control change).
		/// </summary>
		public static void MinionsChooseSides(Game g)
		{
			var moves = new List<(Minion minion, int position)>();
			foreach (Controller p in new[] { g.Player1, g.Player2 })
				foreach (Minion m in p.BoardZone.GetAll().Where(m => !m.Untouchable && !m.IsDead).ToList())
					if (g.Random.Next(2) == 1)
						moves.Add((m, int.MaxValue));
			SwitchSides(moves);
		}
	}
}
