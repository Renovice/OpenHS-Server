// HsServer addition (not upstream SabberStoneCore): starting the game disguised as another class (Maestra of the Masquerade) — live
// (live-20261007-vfx-decks 85210 / 85750).
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>The hero a deck with a "start as a different class" card (Power.StartsDisguisedWith) starts as: a random other class's
		/// hero (user 2026-10-06: its hero and Hero Power), else null.</summary>
		public static Card DisguiseHero(Game g, IEnumerable<Card> deck, Card real)
		{
			if (deck == null || real == null || !deck.Any(x => x?.Power?.StartsDisguisedWith != null)) return null;
			var classes = Cards.HeroClasses.Where(cls => cls != real.Class).ToList();
			for (int tries = 0; tries < 10 && classes.Count > 0; tries++)
			{
				Card hero;
				try { hero = Cards.HeroCard(classes[g.Random.Next(classes.Count)]); } catch { hero = null; }
				if (hero != null && hero[GameTag.HERO_POWER] > 0) return hero;
			}
			return null;
		}

		/// <summary>Each disguised player gets its revealer: the card's enchantment on the player, created by that card (in the deck).</summary>
		public static void DisguiseHolders(Game g)
		{
			foreach (Controller c in new[] { g.Player1, g.Player2 })
			{
				if (c == null || c[HsTag.DISGUISED_REAL_HERO] == 0) continue;
				IPlayable card = c.DeckZone.GetAll().FirstOrDefault(p => p.Card.Power?.StartsDisguisedWith != null);
				if (card == null || !(Cards.TryFromId(card.Card.Power.StartsDisguisedWith) is Card holder)) continue;
				AddEnchantmentBlock(g, holder, card, c, 0, 0, 0);
			}
		}

		/// <summary>
		/// The disguise drops (Maestra: a Rogue card played). Live (vfx-decks 85750): in the revealer's TRIGGER block, its Ritual effect at the
		/// disguise hero, a NEW real hero (COPIED_FROM_ENTITY_ID / LINKED_ENTITY = the disguise) and its Hero Power, the disguise and its Hero
		/// Power set aside; an artificial tile (a copy of the card, HISTORY_TARGET = both heroes); the revealer leaves.
		/// </summary>
		public static void RevealDisguise(Game g, Controller c, Enchantment holder, string cardId)
		{
			if (c[HsTag.DISGUISED_REAL_HERO] == 0 || !(Cards.FromAssetId(c[HsTag.DISGUISED_REAL_HERO]) is Card real)) return;
			Hero disguise = c.Hero;
			int damage = disguise.Damage, armor = disguise.Armor;
			string ritual = g.History ? Loader.SubSpells.Named(cardId, "Ritual") : null;
			bool open = ritual != null && g.OpenSubSpell(holder, ritual, new[] { disguise.Id });
			c.AddHeroAndPower(real);
			Hero hero = c.Hero;
			hero[HsTag.COPIED_FROM_ENTITY_ID] = disguise.Id;
			hero[GameTag.LINKED_ENTITY] = disguise.Id;
			if (damage > 0) hero.Damage = damage;
			if (armor > 0) hero.Armor = armor;
			c[HsTag.DISGUISED_REAL_HERO] = 0;
			if (open) g.CloseSubSpells(holder.Id);
			ArtificialTriggerTile(c, holder, Cards.TryFromId(cardId), new[] { disguise.Id, hero.Id });
			holder.Remove();
		}
	}
}
