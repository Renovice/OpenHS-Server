// HsServer addition (not upstream SabberStone): Colossal (Voyage to the Sunken City).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>Client tags: COLOSSAL (the body), COLOSSAL_LIMB (an appendage), COLOSSAL_LIMB_ON_LEFT.</summary>
		public const GameTag Colossal = (GameTag)2247, ColossalLimb = (GameTag)2248, ColossalLimbOnLeft = (GameTag)2469;

		private static readonly ConcurrentDictionary<string, List<Card>> _limbs = new ConcurrentDictionary<string, List<Card>>();

		/// <summary>HsServer: a Colossal's appendages: the client's limb cards named after it (TID_712t, TID_712t2…: the
		/// COLOSSAL_LIMB tag), in id order.</summary>
		public static List<Card> ColossalLimbs(Card body) => body == null ? new List<Card>() : _limbs.GetOrAdd(body.Id, id =>
		{
			var limbs = new List<Card>();
			// (CATACLYSM: the limbs are t and t1 — Ragnaros, Al'Akir, Azshara, Onyxia, Sinestra, Cho'gall)
			foreach (string suffix in new[] { "t", "t1", "t2", "t3", "t4", "t5", "t6", "t7", "t8", "t9" })
				if (Model.Cards.TryFromId(id + suffix) is Card limb && limb[ColossalLimb] > 0)
					limbs.Add(limb);
			return limbs;
		});

		/// <summary>HsServer (CATACLYSM): "When summoned, ..." — the minion's Power.WhenSummonedTask in its own TRIGGER block (effect index 1)
		/// right as it lands, nested in the summoning block (live vfx 260347 Soldier of Azshara, 277581 Azshara's Tentacle).</summary>
		public static void WhenSummoned(Game g, Minion m)
		{
			if (!(m?.Card.Power?.WhenSummonedTask is Tasks.ISimpleTask task) || m.Zone?.Type != Zone.PLAY) return;
			if (g.History) g.PowerHistory.Add(Kettle.PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, m.Id, "", 1, 0));
			g.TaskQueue.StartEvent();
			task.Process(g, m.Controller, m, null);
			g.ProcessTasks();
			g.TaskQueue.EndEvent();
			if (g.History) g.PowerHistory.Add(Kettle.PowerHistoryBuilder.BlockEnd());
		}

		/// <summary>HsServer: "Colossal +N" (its text): how many appendages it brings.</summary>
		public static int ColossalCount(Card body)
		{
			Match m = body?.Text == null ? Match.Empty : Regex.Match(body.Text, @"Colossal \+(\d+)");
			return m.Success ? int.Parse(m.Groups[1].Value) : 0;
		}

		/// <summary>
		/// HsServer: Colossal (wiki: "Whenever a Colossal minion is summoned or transformed into, uncollectible minions called
		/// appendages are summoned alongside it"; "Colossal minions summon their appendages first before triggering any of
		/// their other effects"; "If a Colossal minion is removed from battlefield ... its appendages will remain").
		/// Live (live-20261006-glugg, Glugg the Gulper in the leftmost slot): right after it lands, a TRIGGER block on it
		/// (EffectIndex 0, ShowInHistory) puts the appendages in — the last first, each directly to its right, so they end in
		/// id order on its right (t, t2, t3) — before the play's SLUSH_TIME and the card's POWER block (empty for Glugg, which has
		/// no Battlecry; a Battlecry would run in it). Exactly two appendages: one
		/// right, one left (user 2026-10-06: Neptulon's two Hands, Crabatoa's Claws). A limb the card puts on its left
		/// (COLOSSAL_LIMB_ON_LEFT: Blackwater Behemoth's Lure) goes left. Called by every summon, play and transform.
		/// </summary>
		public static void SummonAppendages(Game g, Minion body)
		{
			if (body == null || body.Card[Colossal] == 0 || body.Zone?.Type != Zone.PLAY) return;
			List<Card> limbs = ColossalLimbs(body.Card);
			int n = ColossalCount(body.Card);
			if (limbs.Count == 0 || n <= 0) return;
			if (LeftoverAppendages(body.Card)) { SummonLeftovers(g, body); return; } // (CATACLYSM: Magmaw's 99)
			var order = new List<(Card Limb, bool Left)>();
			if (n == 2)
			{
				// HsServer (CATACLYSM review): the first limb ("t") goes LEFT, the other RIGHT in every recording (Neptulon vfx 81087,
				// Crabatoa 95297: t left first; Ragnaros 241744, Al'Akir 112456 28745, Sinestra 98618, Azshara 277453: t1 right first,
				// then t left) — the positions are the same, only the order of the history differs
				Card first = limbs[0], other = limbs[1 % limbs.Count];
				if (other.Id.EndsWith("t1", StringComparison.Ordinal))
				{
					order.Add((other, false));
					order.Add((first, true));
				}
				else
				{
					order.Add((first, true));
					order.Add((other, false));
				}
			}
			else if (n == 4 && limbs.Count >= 4)
			{
				// live Chromatus (vfx-decks, CATA_432): t2 right of the body, t4 left, t3 right of t2, t1 left of t4 → t1 t4 BODY t2 t3
				order.Add((limbs[1], false));
				order.Add((limbs[3], true));
				order.Add((limbs[2], false));
				order.Add((limbs[0], true));
			}
			else
				for (int i = n - 1; i >= 0; i--) // the last first, each right next to the body
					order.Add((limbs[i % limbs.Count], limbs[i % limbs.Count][ColossalLimbOnLeft] > 0));
			bool outward = n == 4 && limbs.Count >= 4;
			if (g.History)
			{
				// (the block's effect index is the card's own: recorded with its OVERRIDE_HISTORY — Ragnaros / Al'Akir 2, Sinestra 1)
				var block = Kettle.PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, body.Id, "", Loader.BlockMeta.IndexWith(body.Card.Id,
					BlockType.TRIGGER, MetaDataType.OVERRIDE_HISTORY) is int index && index >= 0 ? index : 0, 0);
				block.ShowInHistory = true;
				g.PowerHistory.Add(block);
			}
			foreach ((Card limb, bool left) in order)
			{
				if (body.Controller.BoardZone.IsFull || body.Zone?.Type != Zone.PLAY) break;
				Minion m = HeraldScaled(body.Controller, limb); // (CATACLYSM: Herald-upgraded limbs come upgraded)
				int slot = left ? body.ZonePosition : body.ZonePosition + 1;
				// (to its right: past the limbs already put there — Chromatus' outer ones)
				if (!outward) { }
				else if (!left) while (slot < body.Controller.BoardZone.Count && body.Controller.BoardZone[slot][ColossalLimb] > 0 &&
				                  body.Controller.BoardZone[slot][GameTag.CREATOR] == body.Id) slot++;
				else while (slot > 0 && body.Controller.BoardZone[slot - 1][ColossalLimb] > 0 && body.Controller.BoardZone[slot - 1][GameTag.CREATOR] == body.Id) slot--;
				SummonBlock.Invoke(g, m, slot, body);
				if (HeraldUpgraded(limb) && m.Zone?.Type == Zone.PLAY) m[GameTag.USE_ALTERNATE_CARD_TEXT] = 2;
			}
			if (g.History) g.PowerHistory.Add(Kettle.PowerHistoryBuilder.BlockEnd());
		}
	}
}
