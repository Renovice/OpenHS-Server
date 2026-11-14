// HsServer addition (not upstream SabberStone): Shatter (CATACLYSM, set 1980). Data-driven by the client's card tags: SHATTER (the whole
// card: TAG_SCRIPT_DATA_NUM_1 / _2 = its halves' dbf ids), SHATTERED (a half: TAG_SCRIPT_DATA_NUM_3 = the whole card's dbf id).
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
		/// <summary>Client tags: SHATTER (4239, the whole card), SHATTERED (4240, a half), TAG_SCRIPT_DATA_NUM_3 (2889).</summary>
		public const GameTag SHATTER = (GameTag)4239, SHATTERED = (GameTag)4240, TAG_SCRIPT_DATA_NUM_3 = (GameTag)2889;

		/// <summary>The whole card and its two halves of a Shatter card or half, or null.</summary>
		public static (Card Whole, Card A, Card B)? ShatterParts(Card card)
		{
			if (card == null) return null;
			Card whole = card[SHATTER] > 0 ? card : card[SHATTERED] > 0 ? Cards.FromAssetId(card[TAG_SCRIPT_DATA_NUM_3]) : null;
			if (whole == null || whole[SHATTER] == 0) return null;
			Card a = Cards.FromAssetId(whole[GameTag.TAG_SCRIPT_DATA_NUM_1]), b = Cards.FromAssetId(whole[GameTag.TAG_SCRIPT_DATA_NUM_2]);
			return a == null || b == null ? ((Card, Card, Card)?)null : (whole, a, b);
		}

		private static readonly Dictionary<string, string> _shatterFx = new Dictionary<string, string>();

		/// <summary>The Shatter keyword's effect whose name holds <paramref name="part"/> (recorded on the Shatter cards and halves).</summary>
		private static string ShatterFx(Card whole, string part)
		{
			foreach (Card c in ShatterParts(whole) is { } p ? new[] { p.Whole, p.A, p.B } : new[] { whole })
				if (Loader.SubSpells.FindNamed(c.Id, part) is string own) return own;
			lock (_shatterFx)
			{
				if (_shatterFx.TryGetValue(part, out string known)) return known;
				string found = Cards.All.Where(x => x[SHATTER] > 0 || x[SHATTERED] > 0).OrderBy(x => x.Id)
					.Select(x => Loader.SubSpells.FindNamed(x.Id, part)).FirstOrDefault(x => x != null);
				_shatterFx[part] = found;
				return found;
			}
		}

		/// <summary>
		/// Shatter (wiki Shatter: "When a Shatter spell enters a player's hand, it splits into two spells that each have half of the
		/// card's effect but the full mana cost, with one going to the left-most position"; "If the player's hand is full when they get a
		/// Shatter card, no Shattering occurs"; "If the player only has one space left in hand, they will only get the Shattered card that
		/// goes to the left side"; "Once the two Shattered cards are adjacent in hand, they will be recombined and appear in the middle of
		/// where the two cards met"; "Recombining ... prevents it from being Shattered again"; "they will also combine their
		/// enchantments"). Run after each sequence (Game.DeathProcessingAndAuraUpdate), not during the mulligan. Live (vfx-decks
		/// 7074-7203 split; 9575-9660 merge): the split in a TRIGGER block on the card (effect index 2) with
		/// CATAFX_Shatter_Conceal_Separate_Super then CATAFX_Shattered_Separated_OverrideSpawn_Super; the merge in a TRIGGER block on half A
		/// (effect index 0) with CATAFX_Shatter_Conceal_Impact_Super then CATAFX_Shattered_Combined_OverrideSpawn_Super; the merged card
		/// shows its text without "Shatter" (USE_ALTERNATE_CARD_TEXT 1).
		/// </summary>
		internal static void ShatterHands(Game g)
		{
			if (g.Step == Step.BEGIN_MULLIGAN || g.State != State.RUNNING) return;
			foreach (Controller c in new[] { g.Player1, g.Player2 })
			{
				if (c?.HandZone == null) continue;
				for (int guard = 0; guard < 20; guard++)
					if (!SplitOne(g, c) && !MergeOne(g, c)) break;
			}
		}

		private static bool SplitOne(Game g, Controller c)
		{
			IPlayable whole = c.HandZone.GetAll().FirstOrDefault(p => p is Spell && p[SHATTER] > 0 && ShatterParts(p.Card) != null);
			if (whole == null) return false;
			var parts = ShatterParts(whole.Card).Value;
			// (a full hand burnt the card already — "If the player's hand is full when they get a Shatter card, no Shattering occurs";
			// it took the last space: only the left half)
			int room = c.HandZone.FreeSpace + 1; // (the whole card leaves)
			whole[SHATTER] = 0; // (once: a full hand keeps it whole)
			if (g.History) g.PowerHistory.Add(Kettle.PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, whole.Id, "", 2, 0));
			bool fx = g.History && g.OpenSubSpell(whole, ShatterFx(parts.Whole, "Shatter_Conceal_Separate"), new[] { whole.Id });
			RemoveFromHand(c, whole);
			if (fx) g.CloseSubSpells(whole.Id);
			fx = g.History && g.OpenSubSpell(whole, ShatterFx(parts.Whole, "Shattered_Separated_OverrideSpawn"), null, shownSource: Game.NoSubSpellSource);
			AddHandPhaseAt(c, Entity.FromCard(c, parts.A, new EntityData { { GameTag.CREATOR, whole.Id }, { GameTag.CREATOR_DBID, parts.A.AssetId } }), 0);
			if (room >= 2)
				AddHandPhase.Invoke(c, Entity.FromCard(c, parts.B, new EntityData { { GameTag.CREATOR, whole.Id }, { GameTag.DISPLAYED_CREATOR, whole.Id } }));
			if (fx) g.CloseSubSpells(whole.Id);
			if (g.History) g.PowerHistory.Add(Kettle.PowerHistoryBuilder.BlockEnd());
			// "Whenever you Shatter a card" (Misplaced Pyromancer)
			foreach (Minion m in c.BoardZone.GetAll().Where(m => !m.IsSilenced && !m.IsDead && m.Card.Power?.AfterShatterTask != null).ToList())
			{
				g.TaskQueue.StartEvent();
				g.TaskQueue.Execute(m.Card.Power.AfterShatterTask, c, m, null, 0, (int)GameTag.TRIGGER_VISUAL, 0);
				g.ProcessTasks();
				g.TaskQueue.EndEvent();
			}
			return true;
		}

		private static bool MergeOne(Game g, Controller c)
		{
			var hand = c.HandZone.GetAll().ToList();
			for (int i = 0; i + 1 < hand.Count; i++)
			{
				IPlayable x = hand[i], y = hand[i + 1];
				if (x.Card[SHATTERED] == 0 || y.Card[SHATTERED] == 0 || !(ShatterParts(x.Card) is { } parts)) continue;
				if (!(x.Card == parts.A && y.Card == parts.B || x.Card == parts.B && y.Card == parts.A)) continue;
				IPlayable a = x.Card == parts.A ? x : y, b = a == x ? y : x;
				int slot = i;
				if (g.History) g.PowerHistory.Add(Kettle.PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, a.Id, "", 0, 0));
				bool fx = g.History && g.OpenSubSpell(a, ShatterFx(parts.Whole, "Shatter_Conceal_Impact"), new[] { a.Id, b.Id }, shownSource: Game.NoSubSpellSource);
				var enchantments = (a.AppliedEnchantments ?? new List<Enchantment>()).Concat(b.AppliedEnchantments ?? new List<Enchantment>())
					.Select(e => (e.Card, e.Creator, e.ScriptTag1, e.ScriptTag2)).ToList();
				RemoveFromHand(c, a);
				c.GraveyardZone.Add(c.HandZone.Remove(b));
				if (fx) g.CloseSubSpells(a.Id);
				IPlayable merged = Entity.FromCard(c, parts.Whole, new EntityData { { GameTag.CREATOR, a.Id }, { SHATTER, 0 } });
				fx = g.History && g.OpenSubSpell(a, ShatterFx(parts.Whole, "Shattered_Combined_OverrideSpawn"), new[] { merged.Id });
				if (AddHandPhaseAt(c, merged, slot))
				{
					merged[GameTag.USE_ALTERNATE_CARD_TEXT] = 1;
					foreach (var (card, creator, n1, n2) in enchantments)
						AddEnchantmentBlock(g, card, creator ?? merged, merged, n1, n2, 0);
				}
				if (fx) g.CloseSubSpells(a.Id);
				if (g.History) g.PowerHistory.Add(Kettle.PowerHistoryBuilder.BlockEnd());
				return true;
			}
			return false;
		}
	}
}
