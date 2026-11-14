// HsServer addition (not upstream SabberStone): cards that carry a spell and cast it when played — the client's DRUSTVAR_HORROR text
// builder ("Battlecry: Cast {0}." / "Cast {0} and {1}."): Drustvar Horror DAL_431t, Nagaling TSC_052t, Slime TOY_504t, Pupil
// TIME_704t, Bashana's Treant MEND_046t (livecheck pass 2026-10-10).
using System.Collections.Generic;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.SimpleTasks;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		private static readonly (GameTag shown, GameTag requirements)[] CarriedSlots =
		{
			(GameTag.TAG_SCRIPT_DATA_ENT_1, GameTag.ADDITIONAL_PLAY_REQS_1), (GameTag.TAG_SCRIPT_DATA_ENT_2, GameTag.ADDITIONAL_PLAY_REQS_2),
		};

		/// <summary>
		/// <paramref name="carrier"/> carries <paramref name="spell"/> (a set-aside spell entity) in slot <paramref name="slot"/> (0 / 1):
		/// its text names it (TAG_SCRIPT_DATA_ENT_1 / _2: the client's "Cast {0}") and its play takes the spell's requirements
		/// (ADDITIONAL_PLAY_REQS_1 / _2 = the spell's dbf id: live session-20261005-140614 14437, MEND_046t; Playable.TargetingCard).
		/// </summary>
		public static void CarrySpell(IPlayable carrier, IPlayable spell, int slot = 0)
		{
			if (carrier == null || spell == null || slot < 0 || slot >= CarriedSlots.Length) return;
			carrier[CarriedSlots[slot].shown] = spell.Id;
			carrier[CarriedSlots[slot].requirements] = spell.Card.AssetId;
		}

		/// <summary>The spells <paramref name="carrier"/> carries (CarrySpell), in slot order.</summary>
		public static List<Card> CarriedSpells(IPlayable carrier)
		{
			var list = new List<Card>();
			foreach ((GameTag shown, GameTag requirements) in CarriedSlots)
			{
				Card spell = carrier.Game.IdEntityDic.TryGetValue(carrier[shown], out IPlayable p) && p is Spell ? p.Card
					: carrier[requirements] > 0 ? Cards.FromAssetId(carrier[requirements]) : null;
				if (spell != null) list.Add(spell);
			}
			return list;
		}

		/// <summary>
		/// "Battlecry: Cast {0} (and {1})": each carried spell is cast (a new copy, by the carrier) in slot order; the one whose
		/// requirements the play took (Playable.TargetingCard) at the play's <paramref name="target"/> — "you choose the target when
		/// you play the minion, just like any other targeted battlecry" (wiki Swampqueen Hagatha; live MEND_046t 35272: Totemic
		/// Surge… the spell's POWER block at the chosen target) — the others at random targets.
		/// </summary>
		public static void CastCarriedSpells(Controller c, IPlayable carrier, IPlayable target)
		{
			Card aimed = carrier is Playable p ? p.TargetingCard : null;
			bool used = false;
			foreach (Card spell in CarriedSpells(carrier))
			{
				bool takesTarget = !used && target != null && spell == aimed;
				if (takesTarget) used = true;
				new CastRandomSpellTask(preferTarget: takesTarget) { Spell = spell }.Process(c.Game, c, carrier, takesTarget ? target : null);
			}
		}
	}
}
