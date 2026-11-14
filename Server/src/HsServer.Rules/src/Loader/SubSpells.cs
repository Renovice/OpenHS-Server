using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SabberStoneCore.Loader
{
	/// <summary>
	/// HsServer: card-specific effect prefabs the server names in SUB_SPELL_START (the client plays them around the
	/// effect's history). Data, not code: resources/Data/SubSpells.tsv is generated from each card's client CardDef
	/// (m_SubSpellEffectDefs, RESEARCH/vfx). The engine places a prefab by the role its name carries
	/// (<see cref="Role"/>), the way live does (RESEARCH/wire-conventions.md, SUB_SPELL).
	/// </summary>
	public static class SubSpells
	{
		public enum Role
		{
			None,
			/// <summary>a card's counter effect around what it Counters ("…_Counter_Impact…": live Objection!; CounterTask).</summary>
			Counter,
			/// <summary>the card's own Deathrattle ("…_Deathrattle_…").</summary>
			Deathrattle,
			/// <summary>a minion the card summons ("…_OverrideSpawn…", "…_CustomSpawn…").</summary>
			Spawn,
			/// <summary>the card's effect on its target ("…_Missile…").</summary>
			Missile,
			/// <summary>a card it creates flying into the hand from it ("…_SpawnToHand…").</summary>
			SpawnToHand,
			/// <summary>a buff on what it affects ("…Buff…": BuffImpact, HandBuff, MinionBuff…).</summary>
			Buff,
			/// <summary>an area effect ("…_AE…", Fullboard, Halfboard).</summary>
			AE,
			/// <summary>a transform (live: the CHANGE_ENTITY inside it).</summary>
			Transform,
			/// <summary>Corpses spent ("…ConsumeCorpse…": live, from the hero around the spend; Generic.SpendCorpses).</summary>
			ConsumeCorpse,
			/// <summary>Corpses gained ("…GainCorpse…": only around the gain, Generic.GainCorpses — never the whole effect: live
			/// Heart Strike plays it only when its minion dies).</summary>
			GainCorpse,
			/// <summary>what the card destroys ("…_Destroy…": a missile from the card landing on it; Generic.DestroyFx).
			/// Reconstructed from the prefab (missile, impact at the target), unrecorded.</summary>
			Destroy,
			/// <summary>what the card takes into itself ("…_Absorb…": from what it took back to the card; Generic.AbsorbFx).
			/// Reconstructed from the prefab (missile, impact at the target), unrecorded.</summary>
			Absorb,
			/// <summary>cards going from the hand into the deck ("…HandToDeck…": live Gear Shift, around the moves from the spell;
			/// placed by the card's script, Generic.NamedFx).</summary>
			HandToDeck,
			/// <summary>HsServer (2026-10-08): cards the effect puts into a deck ("…SpawnToDeck…", "…DeckAE…"; a recording's "todeck"):
			/// opens before the effect's first card into a deck (Game.PendingFx).</summary>
			ToDeck,
			/// <summary>a draw the effect makes (a recording's "draw": DrawAndHold, Wild Growth…): opens before its first draw.</summary>
			Draw,
			/// <summary>the effect's damage (a recording's "damage" on an effect not named Missile: an area / impact effect): opens before
			/// its first damage.</summary>
			Damage,
			/// <summary>the effect's heal (a recording's "heal"): opens before its first heal.</summary>
			Heal,
			/// <summary>an empty effect live plays at the start of the card's block (a recording's "marker": live sent it with nothing
			/// inside — an impact, a flourish, a hand / deck effect the client shows by itself): opened and closed at once, at the
			/// recorded targets (RecordedMarkerTargets).</summary>
			Marker,
		}

		/// <summary>One recorded effect of a card (RecordedSubSpellAnchors.tsv, RESEARCH/vfx/tools/engine_subspells.py): the block
		/// type live played it in, the prefab, what it wrapped there (spawn / tohand / todeck / draw / transform / destroy / damage /
		/// heal / buff / marker) and, for a marker, its targets (none / self / hero / enemyhero / target / other).</summary>
		public sealed class Anchor
		{
			public string Block, Prefab, Kind, Targets;
			public string Name => Prefab.Split(':')[0];
		}

		private static Dictionary<string, List<Anchor>> _anchors;

		/// <summary>The card's recorded effects (live, every capture), or an empty list. Reprints share their original's.</summary>
		public static IReadOnlyList<Anchor> Anchors(string cardId)
		{
			if (_anchors == null)
			{
				var map = new Dictionary<string, List<Anchor>>();
				Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SabberStoneCore.Resources.RecordedSubSpellAnchors.tsv");
				if (stream != null)
					using (var reader = new StreamReader(stream))
					{
						string line;
						while ((line = reader.ReadLine()) != null)
						{
							if (line.Length == 0 || line[0] == '#') continue;
							string[] parts = line.Split('\t');
							if (parts.Length < 4) continue;
							if (!map.TryGetValue(parts[0], out List<Anchor> list)) map[parts[0]] = list = new List<Anchor>();
							list.Add(new Anchor { Block = parts[1], Prefab = parts[2], Kind = parts[3], Targets = parts.Length > 4 ? parts[4] : "" });
						}
					}
				_anchors = map;
			}
			if (cardId == null) return Array.Empty<Anchor>();
			foreach (string id in new[] { cardId, cardId.StartsWith("CORE_", StringComparison.Ordinal) ? cardId.Substring(5) : null,
				         cardId.StartsWith("LEG_", StringComparison.Ordinal) ? cardId.Substring(4) : null, "CORE_" + cardId })
				if (id != null && _anchors.TryGetValue(id, out List<Anchor> found))
					return found;
			foreach (string variant in ReprintCards.Variants(cardId))
				if (variant != cardId && _anchors.TryGetValue(variant, out List<Anchor> found))
					return found;
			return Array.Empty<Anchor>();
		}

		/// <summary>The recorded effects of the card for a block: "POWER" (its play), "DEATHRATTLE" (its Deathrattle's TRIGGER block)
		/// or "TRIGGER" (its other triggers).</summary>
		public static IEnumerable<Anchor> Anchors(string cardId, string block) =>
			Anchors(cardId).Where(a => a.Block == block);

		/// <summary>
		/// HsServer (2026-10-08): the role of the card's effect: what live wrapped in it when it was recorded (its anchor), else what its
		/// name says (RoleOf). A card repeats its own placement in 85-97% of its plays, a name's role fits only about a third of the
		/// cards (RESEARCH/vfx, FINDINGS 2026-10-08): the recording wins. The engine's own placement for a named role stays when the
		/// recording agrees with it (a Missile around its damage or empty before it, a summon effect around the summon…).
		/// </summary>
		public static Role RoleOf(string cardId, string prefab)
		{
			Role byName = RoleOf(prefab);
			string name = prefab.Split(':')[0];
			Anchor[] recorded = Anchors(cardId).Where(a => a.Name == name).ToArray();
			if (recorded.Length == 0) return byName;
			string kind = recorded[0].Kind; // (a prefab recorded in both blocks: the play's first; same kind in practice)
			switch (byName)
			{
				case Role.Deathrattle: case Role.Counter: case Role.ConsumeCorpse: case Role.GainCorpse:
					return byName; // placed by their own steps
				case Role.Missile when kind is "damage" or "marker" or "heal":
				// (recorded empty: plays where its action didn't happen — a full board, the opponent's hidden cards; its step places it
				// when it does. Not an effect on the cards in hand: "CardsInHand_OverrideSpawn" is an impact there, live empty)
				case Role.Spawn or Role.SpawnToHand or Role.HandToDeck when kind == "marker" && !name.Contains("CardsInHand", StringComparison.OrdinalIgnoreCase):
				case Role.Spawn when kind == "spawn":
				case Role.SpawnToHand when kind == "tohand":
				case Role.Destroy when kind is "destroy" or "marker":
				case Role.Absorb when kind is "destroy" or "marker" or "tohand" or "buff": // (Natalie: it wraps the stats she absorbs)
					return byName;
			}
			return RoleOfAnchor(kind);
		}

		/// <summary>The engine role a recorded anchor kind is placed by.</summary>
		public static Role RoleOfAnchor(string kind) => kind switch
		{
			"spawn" => Role.Spawn,
			"tohand" => Role.SpawnToHand,
			"todeck" => Role.ToDeck,
			"draw" => Role.Draw,
			"transform" => Role.Transform,
			"destroy" => Role.Destroy,
			"damage" => Role.Damage,
			"heal" => Role.Heal,
			"buff" => Role.Buff,
			_ => Role.Marker,
		};

		private static Dictionary<string, string[]> _byCard;

		private static Dictionary<string, string[]> ByCard
		{
			get
			{
				if (_byCard != null) return _byCard;
				var map = new Dictionary<string, string[]>();
				// the client's CardDef list, then the server-picked ReuseFX live sent for the card (RecordedSubSpells.tsv)
				foreach (string resource in new[] { "SabberStoneCore.Resources.SubSpells.tsv", "SabberStoneCore.Resources.RecordedSubSpells.tsv" })
				{
					Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
					if (stream == null) continue;
					using (var reader = new StreamReader(stream))
					{
						string line;
						while ((line = reader.ReadLine()) != null)
						{
							if (line.Length == 0 || line[0] == '#') continue;
							string[] parts = line.Split('\t');
							if (parts.Length < 2) continue;
							map[parts[0]] = map.TryGetValue(parts[0], out string[] had) ? had.Concat(parts[1..]).ToArray() : parts[1..];
						}
					}
				}
				// HsServer (2026-10-08): every effect live sent for the card (RecordedSubSpellAnchors.tsv), whatever its name says
				_byCard = map;
				Anchors((string)null);
				foreach (var (card, list) in _anchors)
					foreach (Anchor a in list)
						if (!map.TryGetValue(card, out string[] had))
							map[card] = new[] { a.Prefab };
						else if (!had.Any(h => h.Split(':')[0] == a.Name))
							map[card] = had.Append(a.Prefab).ToArray();
				return _byCard;
			}
		}

		public static Role RoleOf(string prefab)
		{
			// HsServer (2026-10-08): Blizzard's names vary in case and spelling (SpawntoHand, SpawninHand, OverrrideSpawn, Handbuff,
			// Missle: RESEARCH/vfx audit) — matched case-insensitively, the spellings folded into the usual ones
			string name = Fold(prefab.Split(':')[0]);
			if (name.Contains("SpawnToDeck", StringComparison.Ordinal) || name.Contains("DeckAE", StringComparison.Ordinal) ||
			    name.Contains("DeckBuff", StringComparison.Ordinal))
				return Role.ToDeck; // a card into the deck / the deck's cards — not the board (was AE / Buff: Cho'gall, Azalina…)
			if (name.Contains("ConsumeCorpse", StringComparison.Ordinal)) return Role.ConsumeCorpse;
			if (name.Contains("GainCorpse", StringComparison.Ordinal)) return Role.GainCorpse;
			// (a "CustomDeath" effect plays around the minion the card destroys: live Ritual of Doom,
			// ReuseFX_CustomDeath_Void_DarkBargain_Super at its target around TO_BE_DESTROYED; live-20261004-evening)
			if (name.Contains("_Destroy", StringComparison.Ordinal) || name.Contains("CustomDeath", StringComparison.Ordinal)) return Role.Destroy;
			if (name.Contains("_Absorb", StringComparison.Ordinal)) return Role.Absorb;
			// (a "KeywordImpact_Deathrattle" is the impact of a gained keyword — Vectus's Whelps — not the card's own Deathrattle)
			if (name.Contains("Deathrattle", StringComparison.Ordinal) && !name.Contains("KeywordImpact", StringComparison.Ordinal)) return Role.Deathrattle;
			if (name.Contains("OverrideSpawn", StringComparison.Ordinal) || name.Contains("CustomSpawn", StringComparison.Ordinal))
				return Role.Spawn;
			if (name.Contains("Missile", StringComparison.Ordinal)) return Role.Missile;
			if (name.Contains("_Counter_Impact", StringComparison.Ordinal)) return Role.Counter;
			if (name.Contains("SpawnToHand", StringComparison.Ordinal)) return Role.SpawnToHand;
			if (name.Contains("HandToDeck", StringComparison.Ordinal)) return Role.HandToDeck;
			if (name.Contains("Buff", StringComparison.Ordinal)) return Role.Buff;
			if (name.Contains("_AE", StringComparison.Ordinal) || name.Contains("AE_", StringComparison.Ordinal) ||
			    name.Contains("Fullboard", StringComparison.OrdinalIgnoreCase) || name.Contains("Halfboard", StringComparison.OrdinalIgnoreCase))
				return Role.AE;
			if (name.Contains("Transform", StringComparison.Ordinal)) return Role.Transform;
			return Role.None;
		}

		/// <summary>A prefab name with Blizzard's other spellings folded into the ones RoleOf looks for (case too).</summary>
		private static string Fold(string name)
		{
			string[,] spell =
			{
				{ "spawntohand", "SpawnToHand" }, { "spawninhand", "SpawnToHand" }, { "overrridespawn", "OverrideSpawn" },
				{ "overridespawn", "OverrideSpawn" }, { "customspawn", "CustomSpawn" }, { "missle", "Missile" }, { "missile", "Missile" },
				{ "handbuff", "HandBuff" }, { "buff", "Buff" }, { "deathrattle", "Deathrattle" }, { "consumecorpse", "ConsumeCorpse" },
				{ "gaincorpse", "GainCorpse" }, { "customdeath", "CustomDeath" }, { "_destroy", "_Destroy" }, { "_absorb", "_Absorb" },
				{ "_counter_impact", "_Counter_Impact" }, { "handtodeck", "HandToDeck" }, { "spawntodeck", "SpawnToDeck" },
				{ "deckae", "DeckAE" }, { "deckbuff", "DeckBuff" }, { "transform", "Transform" },
			};
			for (int i = 0; i < spell.GetLength(0); i++)
			{
				int at;
				while ((at = name.IndexOf(spell[i, 0], StringComparison.OrdinalIgnoreCase)) >= 0 &&
				       string.CompareOrdinal(name, at, spell[i, 1], 0, spell[i, 0].Length) != 0)
					name = name.Substring(0, at) + spell[i, 1] + name.Substring(at + spell[i, 0].Length);
			}
			return name;
		}

		/// <summary>HsServer: the card's own effect prefab whose name contains <paramref name="part"/> (its client CardDef or a
		/// recording), or null: a card script naming which of its effects plays (Yogg-Saron's wheel, C'Thun's ritual).</summary>
		public static string Named(string cardId, string part)
		{
			if (!TryPrefabs(cardId, out string[] prefabs)) return null;
			foreach (string p in prefabs)
				if (p.Contains(part, StringComparison.Ordinal))
					return p;
			return null;
		}

		/// <summary>The card's effect prefabs ("Name:guid": its client CardDef's server-named ones and the recorded ones), or none.</summary>
		public static IReadOnlyList<string> Prefabs(string cardId) => TryPrefabs(cardId, out string[] prefabs) ? prefabs : Array.Empty<string>();

		/// <summary>The card's prefabs; a Core or Legacy reprint and its original share them (the data lists either id).</summary>
		private static bool TryPrefabs(string cardId, out string[] prefabs)
		{
			prefabs = null;
			if (cardId == null) return false;
			if (ByCard.TryGetValue(cardId, out prefabs)
			    || cardId.StartsWith("CORE_", StringComparison.Ordinal) && ByCard.TryGetValue(cardId.Substring(5), out prefabs)
			    || cardId.StartsWith("LEG_", StringComparison.Ordinal) && ByCard.TryGetValue(cardId.Substring(4), out prefabs)
			    || ByCard.TryGetValue("CORE_" + cardId, out prefabs))
				return true;
			// HsServer (WONDERS): a reprint under another id (ReprintCards.CanonicalOf: the "counts as a copy" WON_ cards, aliases)
			// and its original share them too, either way (a recording of WON_033 serves GVG_123; GVG_056's serves WON_114)
			foreach (string variant in ReprintCards.Variants(cardId))
				if (variant != cardId && ByCard.TryGetValue(variant, out prefabs))
					return true;
			prefabs = null;
			return false;
		}

		/// <summary>Client tag CORPSE_SPENDER: the card spends Corpses.</summary>
		public const SabberStoneCore.Enums.GameTag CorpseSpender = (SabberStoneCore.Enums.GameTag)4058;

		/// <summary>Live's Corpse effect (recorded for Grave Strength, Blood Tap, Unholy Frenzy, Battlefield Necromancer,
		/// Ymirjar Deathbringer, Corpse Explosion…: RecordedSubSpells.tsv), the same server-picked effect for every spender.</summary>
		public const string GenericConsumeCorpse = "ReuseFX_Necrotic_DeathKnight_AE_ConsumeCorpse_Super:1e8034cc08bf6f6439ef92b7c0906c54";

		/// <summary>
		/// The effect a card plays around its Corpse spend (Generic.SpendCorpses), or null: the one live sent for it
		/// (RecordedSubSpells.tsv), "none" where live sent none ("-ConsumeCorpse": Rite of Atrocity; Lord Marrowgar plays
		/// its own per-Corpse effect), else live's generic Corpse effect for any card the client marks CORPSE_SPENDER (user
		/// decision 2026-10-03: the Grave Strength pattern for every spender without its own recording).
		/// </summary>
		public static string CorpseEffect(SabberStoneCore.Model.Card card)
		{
			if (card == null) return null;
			string recorded = Find(card.Id, Role.ConsumeCorpse);
			if (recorded != null) return recorded.StartsWith("-", StringComparison.Ordinal) ? null : recorded;
			return card[CorpseSpender] > 0 ? GenericConsumeCorpse : null;
		}

		/// <summary>
		/// The card's Buff or area (AE) sub-spell around a whole action of the card ("Name:guid"), or null. Live (119
		/// recorded, RESEARCH/vfx): one per action, never one per target, wrapping what it does (the enchantments, the
		/// damage/heals). Only when the card has exactly one such prefab for this kind of action: names saying Trigger /
		/// EndOfTurn / Deathrattle go with its triggers, the others with its play. Ambiguous cards get none.
		/// </summary>
		/// <param name="includeRecorded">False for the engine's generic placement at a block's start (TaskProcessor): a recorded effect
		/// is placed there by what live wrapped in it (Anchors). Engine code placing the card's area effect itself (WholeFx, SplashFx)
		/// takes it too.</param>
		public static string WholeEffect(string cardId, bool trigger, bool includeRecorded = true)
		{
			if (!TryPrefabs(cardId, out string[] prefabs)) return null;
			string found = null;
			foreach (string p in prefabs)
			{
				Role r = RoleOf(cardId, p);
				if (r != Role.Buff && r != Role.AE && r != Role.Transform && r != Role.Damage && r != Role.Heal) continue;
				string name = p.Split(':')[0];
				// HsServer (2026-10-08): a recorded effect is placed by what live wrapped in it (Anchors; TaskProcessor), not as the
				// card's one whole effect
				if (!includeRecorded && Anchors(cardId).Any(a => a.Name == name)) continue;
				// HsServer (2026-10-08): where live played it (RecordedSubSpellBlocks.tsv: TRIGGER vs POWER / PLAY) wins over its name
				// (Crusader Aura's ReuseFX_Holy_BuffImpact only ever plays in its TRIGGER block, live-20261008-issues 13901)
				if (RecordedBlocks(cardId, name) is { } seen)
				{
					bool inTrigger = seen.Contains("TRIGGER", StringComparison.Ordinal);
					bool inPlay = seen.Contains("POWER", StringComparison.Ordinal) || seen.Contains("PLAY", StringComparison.Ordinal);
					if (trigger ? !inTrigger : !inPlay) continue;
				}
				else
				{
					bool forTrigger = name.Contains("Trigger", StringComparison.OrdinalIgnoreCase) || name.Contains("EndOfTurn", StringComparison.OrdinalIgnoreCase);
					if (forTrigger != trigger) continue;
				}
				if (found != null && found != p) return null; // ambiguous
				found = p;
			}
			return found;
		}

		private static Dictionary<string, string> _blocks;

		/// <summary>The block types live played this card's prefab in ("TRIGGER", "POWER,TRIGGER"...: RecordedSubSpellBlocks.tsv,
		/// RESEARCH/vfx/tools/engine_subspells.py), or null when it wasn't recorded. Reprints share their original's.</summary>
		public static string RecordedBlocks(string cardId, string prefabName)
		{
			if (_blocks == null)
			{
				var map = new Dictionary<string, string>();
				Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SabberStoneCore.Resources.RecordedSubSpellBlocks.tsv");
				if (stream != null)
					using (var reader = new StreamReader(stream))
					{
						string line;
						while ((line = reader.ReadLine()) != null)
						{
							if (line.Length == 0 || line[0] == '#') continue;
							string[] parts = line.Split('\t');
							if (parts.Length >= 3) map[parts[0] + "\t" + parts[1]] = parts[2];
						}
					}
				_blocks = map;
			}
			if (cardId == null || prefabName == null) return null;
			foreach (string id in new[] { cardId, cardId.StartsWith("CORE_", StringComparison.Ordinal) ? cardId.Substring(5) : null,
				         cardId.StartsWith("LEG_", StringComparison.Ordinal) ? cardId.Substring(4) : null, "CORE_" + cardId })
				if (id != null && _blocks.TryGetValue(id + "\t" + prefabName, out string found))
					return found;
			foreach (string variant in ReprintCards.Variants(cardId))
				if (variant != cardId && _blocks.TryGetValue(variant + "\t" + prefabName, out string found))
					return found;
			return null;
		}

		/// <summary>Where live plays a card's effect from (SUB_SPELL_START Source).</summary>
		public enum Source { Hero, Self, None, Player }

		private static Dictionary<string, Source> _sources;

		/// <summary>
		/// Where live played this card's effect prefab from (RecordedSubSpellSources.tsv, RESEARCH/vfx/tools/engine_subspells.py),
		/// or null when it wasn't recorded. A fixed property of each card's effect (689 of 695 recorded card+prefab pairs always
		/// the same), not of the effect kind: the engine's rule (Game.OpenSubSpell callers: a spell's effect from its caster's
		/// hero, a spell's summon from nowhere, anything else from the card) matches only 68% of live, so a recording wins.
		/// </summary>
		public static Source? RecordedSource(string cardId, string prefab)
		{
			if (_sources == null)
			{
				var map = new Dictionary<string, Source>();
				Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SabberStoneCore.Resources.RecordedSubSpellSources.tsv");
				if (stream != null)
					using (var reader = new StreamReader(stream))
					{
						string line;
						while ((line = reader.ReadLine()) != null)
						{
							if (line.Length == 0 || line[0] == '#') continue;
							string[] parts = line.Split('\t');
							if (parts.Length < 3 || !Enum.TryParse(parts[2], true, out Source s)) continue;
							map[parts[0] + "\t" + parts[1]] = s;
						}
					}
				_sources = map;
			}
			if (cardId == null || prefab == null) return null;
			string name = prefab.Split(':')[0];
			foreach (string id in new[] { cardId, cardId.StartsWith("CORE_", StringComparison.Ordinal) ? cardId.Substring(5) : null,
				         cardId.StartsWith("LEG_", StringComparison.Ordinal) ? cardId.Substring(4) : null, "CORE_" + cardId })
				if (id != null && _sources.TryGetValue(id + "\t" + name, out Source found))
					return found;
			// HsServer (WONDERS): a reprint under another id and its original (ReprintCards.Variants), as TryPrefabs
			foreach (string variant in ReprintCards.Variants(cardId))
				if (variant != cardId && _sources.TryGetValue(variant + "\t" + name, out Source found))
					return found;
			return null;
		}

		/// <summary>The card's sub-spell whose name contains <paramref name="part"/> ("Name:guid"), or null.</summary>
		public static string FindNamed(string cardId, string part)
		{
			if (!TryPrefabs(cardId, out string[] prefabs)) return null;
			return prefabs.FirstOrDefault(p => p.Split(':')[0].Contains(part, StringComparison.Ordinal));
		}

		/// <summary>The card's sub-spell for <paramref name="role"/> ("Name:guid"), or null. Its recorded role wins over its name
		/// (RoleOf(cardId, prefab)); with <paramref name="trigger"/> given, one live played in that kind of block comes first.</summary>
		public static string Find(string cardId, Role role, bool? trigger = null)
		{
			if (!TryPrefabs(cardId, out string[] prefabs)) return null;
			string any = null;
			foreach (string p in prefabs)
			{
				if (RoleOf(cardId, p) != role) continue;
				if (trigger == null) return p;
				string seen = RecordedBlocks(cardId, p.Split(':')[0]);
				if (seen == null) { any ??= p; continue; }
				if (seen.Contains(trigger.Value ? "TRIGGER" : "POWER", StringComparison.Ordinal)) return p;
			}
			return any;
		}
	}
}
