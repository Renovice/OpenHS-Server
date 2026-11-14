using System.Collections.Generic;
using System.IO;
using System.Reflection;
using SabberStoneCore.Enums;

namespace SabberStoneCore.Loader
{
	/// <summary>
	/// HsServer: the META_DATA EFFECT_TIMING / OVERRIDE_HISTORY live sends inside a card's effect block. They are a fixed
	/// property of each card's effect (the same on every play; the kind of effect doesn't predict them: engine audit
	/// 2026-10-02, D2), so they are recorded data (RecordedBlockMeta.tsv, from RESEARCH/scripts/extract_block_meta.py),
	/// sent by one generic emitter (ClientStream). Cards live never showed with them get none.
	/// </summary>
	public static class BlockMeta
	{
		/// <summary>A META_DATA of the block: first in the block, or right after its first SUB_SPELL_START.</summary>
		public sealed class Meta
		{
			public MetaDataType Type { get; }
			public bool AfterFirstSubSpell { get; }
			/// <summary>A TARGET naming the block source's controller's hero, not the source (live Defrost: "TARGETHERO").</summary>
			public bool OnHero { get; }
			public Meta(MetaDataType type, bool afterFirstSubSpell, bool onHero = false) { Type = type; AfterFirstSubSpell = afterFirstSubSpell; OnHero = onHero; }
		}

		/// <summary>
		/// Whether live opens the card's block with a TARGET naming what it lands on — also a property of each card (live:
		/// Power Word: Shield, Polymorph, Mark of the Wild do; Rockbiter Weapon, Cold Blood, Abusive Sergeant, Ice Lance don't;
		/// Deadly Shot, Counterspell, Defender of Argus name what they act on without damaging it). Default: the engine's
		/// rule (the chosen target, and what the effect damages, heals or freezes: PowerHistoryBlockStart.EffectTargets).
		/// </summary>
		public enum TargetMode { Default, None, Touched }

		private static readonly IReadOnlyList<Meta> None = new Meta[0];
		private static Dictionary<(string Card, int Block, int Index), TargetMode> _modes;

		/// <summary>The recorded TARGET mode of <paramref name="cardId"/>'s effect block ("NOTARGET" / "TARGETS" in
		/// RecordedBlockMeta.tsv), or Default.</summary>
		public static TargetMode TargetModeOf(string cardId, BlockType type, int index)
		{
			if (string.IsNullOrEmpty(cardId)) return TargetMode.Default;
			_ = Map;
			foreach (string id in Variants(cardId))
				if (_modes.TryGetValue((id, (int)type, index), out TargetMode mode))
					return mode;
			return TargetMode.Default;
		}
		private static Dictionary<(string Card, int Block, int Index), List<Meta>> _byBlock;

		/// <summary>The metas of <paramref name="cardId"/>'s effect block (a reprint and its original count as one card).</summary>
		public static IReadOnlyList<Meta> Find(string cardId, BlockType type, int index)
		{
			if (string.IsNullOrEmpty(cardId)) return None;
			Dictionary<(string, int, int), List<Meta>> map = Map;
			foreach (string id in Variants(cardId))
				if (map.TryGetValue((id, (int)type, index), out List<Meta> metas))
					return metas;
			return None;
		}

		/// <summary>HsServer (CATACLYSM): the effect index of <paramref name="cardId"/>'s recorded <paramref name="type"/> block that holds
		/// <paramref name="meta"/> (a Colossal's appendage block opens with OVERRIDE_HISTORY), or -1.</summary>
		public static int IndexWith(string cardId, BlockType type, MetaDataType meta)
		{
			if (string.IsNullOrEmpty(cardId)) return -1;
			Dictionary<(string, int, int), List<Meta>> map = Map;
			foreach (string id in Variants(cardId))
				foreach (var kv in map)
					if (kv.Key.Item1 == id && kv.Key.Item2 == (int)type && kv.Value.Exists(m => m.Type == meta))
						return kv.Key.Item3;
			return -1;
		}

		/// <summary>Live recorded this card block's metadata (RecordedBlockMeta.tsv): its row decides, no rule.</summary>
		public static bool IsRecorded(string cardId, BlockType type, int index)
		{
			if (string.IsNullOrEmpty(cardId)) return false;
			Dictionary<(string, int, int), List<Meta>> map = Map;
			foreach (string id in Variants(cardId))
				if (map.ContainsKey((id, (int)type, index)))
					return true;
			return false;
		}

		private static Dictionary<(string, int, int), string> _impact;

		/// <summary>The card's client effect for this block is an "…ImpactOnly" effect (only the impact, on a target): an
		/// unrecorded block of it that names nothing itself names what it acted on (ClientStream; live TRL_071).</summary>
		public static bool HasImpactOnlyFx(string cardId, BlockType type, int index) =>
			ImpactPrefab(cardId, type, index)?.Contains("ImpactOnly") == true;

		private static string ImpactPrefab(string cardId, BlockType type, int index)
		{
			if (string.IsNullOrEmpty(cardId) || !HasImpactFx(cardId, type, index)) return null;
			foreach (string id in Variants(cardId))
				if (_impact.TryGetValue((id, (int)type, index), out string prefab))
					return prefab;
			return null;
		}

		/// <summary>
		/// The card's own client effect for this block is a BuffImpact effect: it lands on a target (ClientImpactFx.tsv, from
		/// the client CardDefs by RESEARCH/vfx/tools/impact_effects.py). Live: such a block that draws opens with META_DATA
		/// TARGET on the card itself (Chillfallen Baron 3/3, Corpse Taker 5/5; Arcane Intellect's draw too); without it the
		/// client's effect takes the first entity whose tags change — the player or the hidden drawn card — and never ends
		/// (Defrost froze the client 3 times, 2026-10-04). Blocks that don't draw get none (Demon Hunter's Hero Power 8/8).
		/// </summary>
		public static bool HasImpactFx(string cardId, BlockType type, int index)
		{
			if (string.IsNullOrEmpty(cardId)) return false;
			if (_impact == null)
			{
				var set = new Dictionary<(string, int, int), string>();
				Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SabberStoneCore.Resources.ClientImpactFx.tsv");
				if (stream != null)
					using (var reader = new StreamReader(stream))
					{
						string line;
						while ((line = reader.ReadLine()) != null)
						{
							if (line.Length == 0 || line[0] == '#') continue;
							string[] parts = line.Split('\t');
							if (parts.Length >= 3 && System.Enum.TryParse(parts[1], out BlockType block) && int.TryParse(parts[2], out int i))
								set[(Canonical(parts[0]), (int)block, i)] = parts.Length > 3 ? parts[3] : "";
						}
					}
				_impact = set;
			}
			foreach (string id in Variants(cardId))
				if (_impact.ContainsKey((id, (int)type, index)))
					return true;
			return false;
		}

		private static HashSet<(string, int)> _ownFx;

		/// <summary>
		/// HsServer (2026-11-14): the card's own client effect for this block type (POWER = its play effect, TRIGGER = its
		/// trigger effects) animates what the block does — a card it adds to the hand flies in from it (ClientOwnFx.tsv, from
		/// the client CardDefs by RESEARCH/vfx/tools/own_effects.py). Live sends no server to-hand effect for such a card (68 of
		/// 77 creators: Fire Fly, Babbling Book, Zola…) and one for a card with only the generic Play / Trigger (87 of 89).
		/// </summary>
		public static bool HasOwnFx(string cardId, BlockType type)
		{
			if (string.IsNullOrEmpty(cardId)) return false;
			if (_ownFx == null)
			{
				var set = new HashSet<(string, int)>();
				// (HsServer 2026-11-14: plus the blocks live recorded adding cards to the hand with no SUB_SPELL at all —
				// RecordedNoFlight.tsv, RESEARCH/vfx/tools/no_flight.py: Paleomancy's "keep all 3")
				foreach (string resource in new[] { "SabberStoneCore.Resources.ClientOwnFx.tsv", "SabberStoneCore.Resources.RecordedNoFlight.tsv" })
				{
				Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
				if (stream != null)
					using (var reader = new StreamReader(stream))
					{
						string line;
						while ((line = reader.ReadLine()) != null)
						{
							if (line.Length == 0 || line[0] == '#') continue;
							string[] parts = line.Split('\t');
							if (parts.Length >= 2 && System.Enum.TryParse(parts[1], out BlockType block))
								set.Add((parts[0], (int)block));
						}
					}
				}
				_ownFx = set;
			}
			if (_ownFx.Contains((cardId, (int)type))) return true;
			foreach (string id in Variants(cardId))
				if (_ownFx.Contains((id, (int)type)) ||
				    id.StartsWith("CORE_", System.StringComparison.Ordinal) && _ownFx.Contains((id.Substring(5), (int)type)))
					return true;
			return false;
		}

		// a card and its Core / Legacy reprint share their recording, whichever id live played (as Loader.SubSpells)
		private static IEnumerable<string> Variants(string cardId)
		{
			yield return Canonical(cardId);
			if (cardId.StartsWith("CORE_", System.StringComparison.Ordinal)) yield return Canonical(cardId.Substring(5));
			else if (cardId.StartsWith("LEG_", System.StringComparison.Ordinal)) yield return Canonical(cardId.Substring(4));
			else yield return Canonical("CORE_" + cardId);
		}

		private static string Canonical(string cardId) =>
			ReprintCards.CanonicalOf.TryGetValue(cardId, out string original) ? original : cardId;

		private static Dictionary<(string, int, int), List<Meta>> Map
		{
			get
			{
				if (_byBlock != null) return _byBlock;
				var map = new Dictionary<(string, int, int), List<Meta>>();
				var modes = new Dictionary<(string, int, int), TargetMode>();
				Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SabberStoneCore.Resources.RecordedBlockMeta.tsv");
				if (stream != null)
					using (var reader = new StreamReader(stream))
					{
						string line;
						while ((line = reader.ReadLine()) != null)
						{
							if (line.Length == 0 || line[0] == '#') continue;
							string[] parts = line.Split('\t');
							if (parts.Length < 4 || !System.Enum.TryParse(parts[1], out BlockType block) || !int.TryParse(parts[2], out int index))
								continue;
							var metas = new List<Meta>();
							foreach (string m in parts[3].Split(','))
							{
								if (m == "NOTARGET") { modes[(Canonical(parts[0]), (int)block, index)] = TargetMode.None; continue; }
								if (m == "TARGETS") { modes[(Canonical(parts[0]), (int)block, index)] = TargetMode.Touched; continue; }
								string[] kind = m.Split('@');
								if (kind.Length == 2 && kind[0] == "TARGETHERO") { metas.Add(new Meta(MetaDataType.TARGET, kind[1] == "sub", onHero: true)); continue; }
								if (kind.Length == 2 && System.Enum.TryParse(kind[0], out MetaDataType type))
									metas.Add(new Meta(type, kind[1] == "sub"));
							}
							map[(Canonical(parts[0]), (int)block, index)] = metas;
						}
					}
				_modes = modes;
				return _byBlock = map;
			}
		}
	}
}
