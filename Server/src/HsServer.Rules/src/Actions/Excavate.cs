// HsServer addition (not upstream SabberStone): the Excavate keyword.
using System;
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
		/// <summary>Client tags for Excavate (the engine's 2021 GameTag enum doesn't name them): the player's tier and its
		/// maximum, the card keyword, a treasure card (2823) and the player's Excavates this game (2822, live).</summary>
		public const GameTag CURRENT_EXCAVATE_TIER = (GameTag)3249, MAX_EXCAVATE_TIER = (GameTag)3326, EXCAVATE = (GameTag)3114,
			EXCAVATE_TREASURE = (GameTag)2823, EXCAVATES_THIS_GAME = (GameTag)2822;

		private static readonly Rarity[] ExcavateTiers = { Rarity.COMMON, Rarity.RARE, Rarity.EPIC, Rarity.LEGENDARY };

		/// <summary>The treasure visual: one for every tier (live-20261002-excavate-tiers: all 7 Excavates up to the
		/// Legendary used this prefab; the rarity shows on the treasure card itself).</summary>
		private const string ExcavateSubSpell = "WWFX_Excavate_SpawnToHand_Common_Super:0d6fc79bd84427643a35abccb0937ec4";

		private static List<Card> _treasures;
		/// <summary>The Excavate treasures: client cards with tag 2823 and a tier rarity.</summary>
		private static List<Card> Treasures
		{
			get
			{
				if (_treasures != null) return _treasures;
				var found = Cards.All.Where(c => c[EXCAVATE_TREASURE] > 0 && ExcavateTiers.Contains(c.Rarity)).ToList();
				return found.Count > 0 ? _treasures = found : found; // not cached before the client's cards are loaded
			}
		}

		/// <summary>A player's highest Excavate tier (0-based), set at game start: 3 (a Legendary treasure) for the classes
		/// that have one, 2 otherwise (wiki Excavate: "Death Knight, Mage, Paladin, Rogue, Shaman, Warlock, and Warrior
		/// generate a unique legendary minion on their fourth Excavate"; live MAX_EXCAVATE_TIER 3 for Warrior, 2 for Hunter).</summary>
		public static int MaxExcavateTier(CardClass heroClass) =>
			Treasures.Any(t => t.Rarity == Rarity.LEGENDARY && t.HasClass(heroClass)) ? 3 : 2;

		/// <summary>
		/// Excavate (wiki Excavate: "add a random treasure to the player's hand with its rarity corresponding to the number of
		/// times that player has Excavated this game, resetting once the maximum rarity has been reached"): common, rare, epic,
		/// then the class's Legendary, then common again. Live (live-20261002-forge-drink, Reinforced Plating): inside the
		/// card's effect a SUB_SPELL (the treasure visual; Source = the player for a spell, the card itself for a minion:
		/// live-20261002-excavate-tiers) holds EFFECT_TIMING, the player's Excavate count (2822) and CURRENT_EXCAVATE_TIER
		/// (already the next tier; 0 after the Legendary, live), then the treasure created in hand (CREATOR = the card,
		/// DISPLAYED_CREATOR right after). Returns the treasure (null: none, or the hand was full).
		/// </summary>
		public static IPlayable Excavate(Controller c, IPlayable source)
		{
			Game game = c.Game;
			int tier = Math.Max(0, Math.Min(c[CURRENT_EXCAVATE_TIER], ExcavateTiers.Length - 1));
			Rarity rarity = ExcavateTiers[tier];
			List<Card> pool = Treasures.Where(t => t.Rarity == rarity && (rarity != Rarity.LEGENDARY || t.HasClass(c.HeroClass))).ToList();
			if (pool.Count == 0) return null;
			Card pick = pool[game.Random.Next(pool.Count)];
			bool visual = game.OpenSubSpell(source, ExcavateSubSpell, null, shownSource: source is Spell ? c.Id : 0);
			if (game.History)
				game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.EFFECT_TIMING, Data = 0, Info = new List<int> { source.Id } });
			c[EXCAVATES_THIS_GAME] = c[EXCAVATES_THIS_GAME] + 1;
			c[CURRENT_EXCAVATE_TIER] = tier + 1 > c[MAX_EXCAVATE_TIER] ? 0 : tier + 1;
			IPlayable treasure = Entity.FromCard(c, pick, new EntityData { { GameTag.CREATOR, source.Id } });
			bool added = AddHandPhase.Invoke(c, treasure);
			if (added) treasure[GameTag.DISPLAYED_CREATOR] = source.Id;
			if (visual) game.CloseSubSpells(source.Id);
			// "Excavate to awaken N turns sooner" (a Dormant minion's own text: Slagmaw the Slumbering)
			foreach (Minion m in c.BoardZone.GetDormant())
				if (m.Card.ExcavateAwakensSooner > 0)
					AwakenSooner(m, m.Card.ExcavateAwakensSooner);
			// "after you Excavate" / "the next time you Excavate" (Pile of Bones)
			game.TaskQueue.StartEvent();
			game.TriggerManager.OnExcavateTrigger(added ? treasure : source);
			game.ProcessTasks();
			game.TaskQueue.EndEvent();
			return added ? treasure : null;
		}
	}
}
