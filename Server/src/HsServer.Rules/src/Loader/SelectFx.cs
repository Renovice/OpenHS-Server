using System.Collections.Generic;
using System.IO;
using System.Reflection;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;

namespace SabberStoneCore.Loader
{
	/// <summary>
	/// HsServer: cards whose own client effect for a block is a SelectSpell (ClientSelectFx.tsv, from the client CardDefs by
	/// RESEARCH/vfx/tools/select_spells.py). The client plays one spell of its table, the one the block's META_DATA
	/// EFFECT_SELECTION names; with no such metadata it can't attach the effect and plays nothing (client
	/// SelectSpell.AttachPowerTaskList / SetSelectedSpell; our Arcanosaur's Arcane Explosion never showed: user 2026-10-09,
	/// client Power.log "FAILED to attach task list to spell TRLFX_ArcaneExplosion_SelectSpell"). Live: The Countess 1 with
	/// her Invitations / 0 and an empty block, Mecha'thun's Deathrattle 1 and nothing destroyed (its table: 0 = the effect),
	/// Augmented Elekk 1 into the opponent's deck (its table: 1 = "…_Opponent").
	/// </summary>
	public static class SelectFx
	{
		private sealed class Entry
		{
			public int Selection;
			public string Spell; // null: none
		}

		private static Dictionary<(string Card, int Block, int Index), List<Entry>> _map;

		/// <summary>The selection <paramref name="block"/> of <paramref name="cardId"/> sends, or null (not a SelectSpell, or
		/// a variant effect its script didn't choose).
		/// A script's choice (Generic.SelectEffect) wins. A conditional effect — its table plays one spell or nothing — follows
		/// the card's "If …" (Generic.ConditionHeld; with no condition checked, whether anything happened in the block): held,
		/// the entry with the spell; not, the empty one. A table of two spells without a choice: the second when held, the
		/// first when not (Master's Call: roar / audio only).</summary>
		public static int? SelectionOf(string cardId, PowerHistoryBlockStart block)
		{
			List<Entry> table = Find(cardId, block.BlockType, block.Index);
			if (table == null) return null;
			if (block.EffectSelection is int chosen) return chosen;
			var distinct = Distinct(table);
			if (distinct.Count != 2) return Empty(distinct); // a variant effect: nothing to show unless the script chose
			bool held = block.ConditionHeld ?? block.HasContent;
			Entry none = distinct.Find(e => e.Spell == null);
			if (none != null)
				return held ? distinct.Find(e => e.Spell != null)?.Selection : none.Selection;
			// two spells: only a conditional card's script tells which is which
			return block.ConditionHeld is bool h ? distinct[h ? 1 : 0].Selection : (int?)null;
		}

		/// <summary>The selection whose spell is named with <paramref name="word"/> (…_AttackRight_FX); with
		/// <paramref name="otherwiseSpell"/>, else the first spell named with neither "Right" nor "Left"; -1: none.</summary>
		public static int IndexNamed(string cardId, BlockType type, int index, string word, bool otherwiseSpell = false)
		{
			List<Entry> table = Find(cardId, type, index);
			if (table == null) return -1;
			var distinct = Distinct(table);
			Entry e = distinct.Find(x => x.Spell != null && x.Spell.Contains(word));
			if (e == null && otherwiseSpell)
				e = distinct.Find(x => x.Spell != null && !x.Spell.Contains("Right") && !x.Spell.Contains("Left"));
			return e?.Selection ?? -1;
		}

		/// <summary>The card's client effect for this block is a SelectSpell.</summary>
		public static bool Has(string cardId, BlockType type, int index) => Find(cardId, type, index) != null;

		/// <summary>The selection that plays nothing, or null.</summary>
		private static int? Empty(List<Entry> distinct) => distinct.Find(e => e.Spell == null)?.Selection;

		// the client takes the first entry of a selection (SelectSpell.LoadSelectedSpell): later duplicates never play
		private static List<Entry> Distinct(List<Entry> table)
		{
			var seen = new HashSet<int>();
			var list = new List<Entry>();
			foreach (Entry e in table)
				if (seen.Add(e.Selection)) list.Add(e);
			return list;
		}

		private static List<Entry> Find(string cardId, BlockType type, int index)
		{
			if (string.IsNullOrEmpty(cardId)) return null;
			Dictionary<(string, int, int), List<Entry>> map = Map;
			foreach (string id in Variants(cardId))
				if (map.TryGetValue((id, (int)type, index), out List<Entry> table))
					return table;
			return null;
		}

		// the card, its Core / Legacy original and its reprint's original (as Loader.BlockMeta)
		private static IEnumerable<string> Variants(string cardId)
		{
			yield return cardId;
			if (cardId.StartsWith("CORE_", System.StringComparison.Ordinal)) yield return cardId.Substring(5);
			else if (cardId.StartsWith("LEG_", System.StringComparison.Ordinal)) yield return cardId.Substring(4);
			else yield return "CORE_" + cardId;
			if (ReprintCards.CanonicalOf.TryGetValue(cardId, out string original)) yield return original;
		}

		private static Dictionary<(string, int, int), List<Entry>> Map
		{
			get
			{
				if (_map != null) return _map;
				var map = new Dictionary<(string, int, int), List<Entry>>();
				Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SabberStoneCore.Resources.ClientSelectFx.tsv");
				if (stream != null)
					using (var reader = new StreamReader(stream))
					{
						string line;
						while ((line = reader.ReadLine()) != null)
						{
							if (line.Length == 0 || line[0] == '#') continue;
							string[] parts = line.Split('\t');
							if (parts.Length < 5 || !System.Enum.TryParse(parts[1], out BlockType block) || !int.TryParse(parts[2], out int index))
								continue;
							var table = new List<Entry>();
							foreach (string cell in parts[4].Split(';'))
							{
								string[] kv = cell.Split(new[] { '=' }, 2);
								if (kv.Length == 2 && int.TryParse(kv[0], out int selection))
									table.Add(new Entry { Selection = selection, Spell = kv[1] == "-" ? null : kv[1] });
							}
							if (table.Count > 0) map[(parts[0], (int)block, index)] = table;
						}
					}
				return _map = map;
			}
		}
	}
}
