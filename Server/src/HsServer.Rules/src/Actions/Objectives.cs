// HsServer addition (not upstream SabberStoneCore): the shared tick and expiry of Objectives and Paladin Auras (client OBJECTIVE 2311,
// PALADIN_AURA 3429) — one wire for every card (live audit 2026-10-08: 14 cards, 53 OBJECTIVE blocks).
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.SimpleTasks;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		private static readonly GameTag[] ObjectiveCounterSlots =
			{ GameTag.TAG_SCRIPT_DATA_NUM_1, GameTag.TAG_SCRIPT_DATA_NUM_2, (GameTag)2889 }; // (TAG_SCRIPT_DATA_NUM_3)

		/// <summary>An Objective / Aura entering the Secret zone: its cast turn and the first free script-data slot, which will carry its
		/// turns-elapsed counter (live: NUM_1 for Alterac Objectives, NUM_2 for Auras whose NUM_1 is the duration, NUM_3 for Ursol's).</summary>
		public static void ObjectiveEnters(Spell s)
		{
			if (s?.Game == null || s.Card[HsTag.OBJECTIVE] == 0 || s[HsTag.OBJECTIVE_CAST_TURN] != 0) return;
			s[HsTag.OBJECTIVE_CAST_TURN] = s.Game.Turn;
			for (int i = 0; i < ObjectiveCounterSlots.Length; i++)
				if (s[ObjectiveCounterSlots[i]] == 0) { s[HsTag.OBJECTIVE_COUNTER_SLOT] = i + 1; break; }
		}

		/// <summary>One tick: QUEST_PROGRESS +1, then the turns-elapsed counter (game turns since it was cast; written once it isn't 0) —
		/// live dk-classxp / issues 6562: TIME_700 QUEST_PROGRESS 3, NUM_2 4. Returns whether it reached its total.</summary>
		public static bool ObjectiveTick(Spell s, int total)
		{
			s.QuestProgress++;
			int slot = s[HsTag.OBJECTIVE_COUNTER_SLOT];
			int elapsed = s.Game.Turn - s[HsTag.OBJECTIVE_CAST_TURN];
			if (slot > 0 && s[HsTag.OBJECTIVE_CAST_TURN] > 0 && elapsed > 0 && s[ObjectiveCounterSlots[slot - 1]] != elapsed)
				s[ObjectiveCounterSlots[slot - 1]] = elapsed;
			return s.QuestProgress >= System.Math.Max(1, total);
		}

		/// <summary>
		/// An Objective / Aura runs out: its effect stops (its aura, its triggers, the stats it set), then live's expiry — 2747 = itself,
		/// TIMES_AFFECTED + LAST_AFFECTED_BY = itself (NoteAffected), to the graveyard (issues 6562-6601). <paramref name="ownBlock"/>: in a
		/// TRIGGER block of its own at index 0 queued after the current one (live Alterac Objectives: vfx-decks 107776), else right here.
		/// </summary>
		public static void ObjectiveExpires(Controller c, Spell s, bool ownBlock = false)
		{
			if (ownBlock)
			{
				var later = new CustomTask((g, ctl, src, t, stack) => ObjectiveExpires(ctl, (Spell)src)) { IsTrigger = true };
				c.Game.TaskQueue.Enqueue(later, c, s, null);
				return;
			}
			if (s.Zone?.Type != Zone.SECRET) return;
			s.OngoingEffect?.Remove();
			s.ActivatedTrigger?.Remove();
			// (its stats on characters go too: Celestial Aura's 10/10)
			foreach (Controller side in new[] { c, c.Opponent })
				foreach (ICharacter ch in side.BoardZone.GetAll().Cast<ICharacter>().Append(side.Hero))
					foreach (Enchantment made in ch.AppliedEnchantments?.Where(x => x.Creator == s).ToList() ?? new List<Enchantment>())
						EndStatSet(made);
			s[(GameTag)2747] = s.Id;
			NoteAffected(s, s);
			c.SecretZone.Remove(s);
			c.GraveyardZone.Add(s);
		}
	}
}
