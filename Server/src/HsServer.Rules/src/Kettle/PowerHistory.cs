#region copyright
// SabberStone, Hearthstone Simulator in C# .NET Core
// Copyright (C) 2017-2019 SabberStone Team, darkfriend77 & rnilva
//
// SabberStone is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as
// published by the Free Software Foundation, either version 3 of the
// License.
// SabberStone is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Kettle
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
{
	public class PowerHistoryBuilder
	{
		public static PowerHistoryCreateGame CreateGame(Game game, Controller[] players)
		{
			return new PowerHistoryCreateGame()
			{
				Game = new PowerEntity
				{
					Id = game.Id,
					Tags = new Dictionary<GameTag, int>(game._data)
				},
				Players = new[]
						{
					new PowerPlayer
					{
						PlayerId = players[0].PlayerId,
						AccountId = 12718623,
						CardBack = 0,
						PowerEntity = new PowerEntity
						{
							Id = players[0].Id,
							Tags = new Dictionary<GameTag, int>(players[0]._data)
						}
					},
					new PowerPlayer
					{
						PlayerId = players[1].PlayerId,
						AccountId = 18463223,
						CardBack = 0,
						PowerEntity = new PowerEntity
						{
							Id = players[1].Id,
							Tags = new Dictionary<GameTag, int>(players[1]._data)
						}
					},
						}
			};
		}

		public static PowerHistoryTagChange TagChange(int id, GameTag tag, int value)
		{
			return new PowerHistoryTagChange()
			{
				EntityId = id,
				Tag = tag,
				Value = value
			};
		}

		/// <summary>HsServer: tag ids from here on are engine-internal script data (card scripts' bookkeeping), never sent.</summary>
		public const int InternalTagsFrom = 900000;

		public static PowerHistoryFullEntity FullEntity(IPlayable playable) => new PowerHistoryFullEntity
		{
			Entity = new PowerHistoryEntity { Id = playable.Id, Name = playable.Card.Id, Tags = EntityTags(playable) }
		};

		/// <summary>HsServer: what live FULL_ENTITY and SHOW_ENTITY carry: the card's own tags (keywords such as TRADEABLE,
		/// SPELLPOWER, CLASS...) plus the entity's changes.</summary>
		private static Dictionary<GameTag, int> EntityTags(IPlayable playable)
		{
			var tags = new Dictionary<GameTag, int>(playable.Card.Tags);
			// (HsServer: an enchantment too — the Rewind reset sends every entity: its own tags)
			IEnumerable<KeyValuePair<GameTag, int>> own = playable is Entity entity ? (IEnumerable<KeyValuePair<GameTag, int>>)entity._data : playable.NativeTags;
			foreach (KeyValuePair<GameTag, int> t in own)
				if ((int)t.Key < InternalTagsFrom) // HsServer: engine-internal script tags stay on the server
					tags[t.Key] = t.Value;
			// (HsServer 2026-10-09: an enchantment's indexer reads only its own changes: its CARDTYPE / RARITY / COST... became 0 in
			// its FULL_ENTITY — a Rewind's reset sent TLC_222e with no type. Its own value, else its card's.)
			gameTagsEntities.ForEach(p => tags[p] = playable is Enchantment
				? playable.NativeTags.TryGetValue(p, out int own) ? own : playable.Card[p]
				: playable[p]);
			if (playable is Character c)
			{
				tags[GameTag.ATK] = c.AttackDamage;
				tags[GameTag.HEALTH] = c.BaseHealth; // HsServer: HEALTH is the maximum; DAMAGE is separate (was remaining health)
				tags[GameTag.DAMAGE] = c.Damage;
			}
			if (playable.Zone?.Type == Zone.HAND)
				tags[GameTag.COST] = playable.Cost; // HsServer: with cost auras, as the hand shows it
			return tags;
		}

		private static List<GameTag> gameTagsEntities = new List<GameTag>()
		{
			GameTag.PREMIUM,
			GameTag.ATK,
			GameTag.HEALTH,
			GameTag.DURABILITY,
			GameTag.DAMAGE,
			GameTag.COST,
			GameTag.ZONE,
			GameTag.ZONE_POSITION,
			GameTag.CONTROLLER,
			GameTag.ENTITY_ID,
			GameTag.SILENCED,
			GameTag.WINDFURY,
			GameTag.TAUNT,
			GameTag.STEALTH,
			GameTag.DIVINE_SHIELD,
			GameTag.POISONOUS,
			GameTag.LIFESTEAL,
			GameTag.CHARGE,
			GameTag.FACTION,
			GameTag.CARDTYPE,
			GameTag.RARITY,
			GameTag.BATTLECRY,
			GameTag.DEATHRATTLE,
			GameTag.FROZEN,
			GameTag.NUM_ATTACKS_THIS_TURN,
			GameTag.FORCED_PLAY,
			GameTag.TO_BE_DESTROYED,
			GameTag.START_WITH_1_HEALTH,
			GameTag.CUSTOM_KEYWORD_EFFECT,
			GameTag.EXTRA_ATTACKS_THIS_TURN
		};

		public static PowerHistoryShowEntity ShowEntity(IPlayable playable)
		{
			return new PowerHistoryShowEntity
			{
				Entity = new PowerHistoryEntity { Id = playable.Id, Name = playable.Card.Id, Tags = EntityTags(playable) }
			};
		}

		public static IPowerHistoryEntry ShuffleDeck(int playerId) => new PowerHistoryShuffleDeck { PlayerId = playerId };

		public static IPowerHistoryEntry HideEntity(IPlayable playable)
		{
			return new PowerHistoryHideEntity
			{
				EntityID = playable.Id,
				Zone = playable.Zone.Type
			};
		}

		public static PowerHistoryBlockStart BlockStart(BlockType blockType, int source, string effectCardId, int index, int target)
		{
			return new PowerHistoryBlockStart
			{
				BlockType = blockType,
				Source = source,
				EffectCardId = effectCardId,
				Index = index,
				Target = target
			};
		}

		public static PowerHistoryBlockEnd BlockEnd()
		{
			return new PowerHistoryBlockEnd();
		}
	}

	//message PowerHistory
	//{
	//    enum PacketID
	//    {
	//        ID_acd5 = 19;
	//    }
	//    repeated PowerHistoryData list = 1;
	//}
	public class PowerHistory
	{
		public List<IPowerHistoryEntry> Full { get; } = new List<IPowerHistoryEntry>();
		public List<IPowerHistoryEntry> Last { get; } = new List<IPowerHistoryEntry>();

		/// <summary>HsServer: is the entity in play right now? An effect block's TARGET names only what it affects while in
		/// play (live: never a card in hand/deck, nor a minion that already died; a minion summoned mid-block is named).</summary>
		internal Func<int, bool> InPlay;

		// HsServer: open SUB_SPELLs (innermost last) with the on-board characters affected inside them.
		private readonly List<(PowerHistorySubSpellStart Start, List<int> Hit)> _openSubSpells = new List<(PowerHistorySubSpellStart, List<int>)>();

		/// <summary>HsServer: the innermost open SUB_SPELL of <paramref name="prefab"/>: its start (targets) and how many
		/// characters it has hit; null when none is open (Generic.MissilePerHit).</summary>
		internal (PowerHistorySubSpellStart Start, int Hits)? OpenSubSpellOf(string prefab)
		{
			for (int i = _openSubSpells.Count - 1; i >= 0; i--)
				if (_openSubSpells[i].Start.Prefab == prefab)
					return (_openSubSpells[i].Start, _subSpellSeen[i].Count);
			return null;
		}

		private void SubSpellHit(int id)
		{
			if (_openSubSpells.Count > 0)
			{
				AddAffected(_openSubSpells[^1].Hit, id);
				_subSpellSeen[^1].Add(id);
			}
		}

		// HsServer: everything each open SUB_SPELL affected, wherever it is (a hand buff too): a repeat starts a new round
		private readonly List<HashSet<int>> _subSpellSeen = new List<HashSet<int>>();

		/// <summary>HsServer: what an entry affects for a SUB_SPELL (an attached enchantment's target, a damage / heal).</summary>
		private static IEnumerable<int> AffectedBy(IPowerHistoryEntry entry) => entry switch
		{
			PowerHistoryShowEntity { Entity: { } e } when e.Tags.TryGetValue(GameTag.ATTACHED, out int a) => new[] { a },
			PowerHistoryFullEntity { Entity: { } e } when e.Tags.TryGetValue(GameTag.ATTACHED, out int a) => new[] { a },
			PowerHistoryMetaData { Type: MetaDataType.DAMAGE or MetaDataType.HEALING, Info: { } info } => info,
			_ => Array.Empty<int>(),
		};

		/// <summary>
		/// HsServer: live plays a card's buff / area effect once per round of its effect: Blood Tap's hand buff twice
		/// (+1/+1, then +1/+1 more), Relic of Extinction's missile per hit (audit RESEARCH/vfx/audit_subspells.md). An open
		/// whole-effect SUB_SPELL (CollectTargets) that would affect something it already affected closes, and the same
		/// effect opens again for the next round.
		/// </summary>
		private void NewRoundIfRepeated(IPowerHistoryEntry entry)
		{
			if (_openSubSpells.Count == 0 || !_openSubSpells[^1].Start.CollectTargets) return;
			HashSet<int> seen = _subSpellSeen[^1];
			if (!AffectedBy(entry).Any(seen.Contains)) return;
			PowerHistorySubSpellStart old = _openSubSpells[^1].Start;
			Add(new PowerHistorySubSpellEnd());
			Add(new PowerHistorySubSpellStart { Prefab = old.Prefab, Source = old.Source, CollectTargets = true });
		}

		private void AddAffected(List<int> affected, int id)
		{
			if (InPlay == null || InPlay(id))
				affected.Add(id);
		}

		// HsServer: blocks whose effect repeats (TargetRoundTask): the open round's TARGET, and how many of the block's
		// affected characters came before the first round.
		private readonly Dictionary<PowerHistoryBlockStart, (PowerHistoryTargetRound Round, int Before)> _targetRounds =
			new Dictionary<PowerHistoryBlockStart, (PowerHistoryTargetRound, int)>();

		/// <summary>
		/// HsServer: the next repeat of a repeated effect starts (Mad Bomber's bomb, Arcane Missiles' missile). Live sends
		/// one META_DATA TARGET per repeat, right before it, even for the same target again (Arcane Missiles,
		/// live-20261002-progression; rule book x_Damage: "each missile creates a Damage Event which is immediately
		/// resolved, before choosing at random the target for the next missile"); the client flies one projectile per
		/// TARGET and lands the hits up to the next one on its impact (Spell.CompleteMetaDataTasks). The round names what
		/// it affects (filled as it happens; a round that affects nothing sends none). An open whole-effect SUB_SPELL that
		/// already affected something plays again for the round (live: Relic of Extinction's missile per hit).
		/// </summary>
		internal void NewTargetRound()
		{
			int top = _openBlocks.Count - 1;
			if (top < 0 || _openBlocks[top].Block.BlockType is not (BlockType.POWER or BlockType.TRIGGER)) return;
			PowerHistoryBlockStart block = _openBlocks[top].Block;
			if (_openSubSpells.Count > 0 && _openSubSpells[^1].Start.CollectTargets && _openSubSpells[^1].Start.Block == block &&
			    _subSpellSeen[^1].Count > 0)
			{
				PowerHistorySubSpellStart old = _openSubSpells[^1].Start;
				Add(new PowerHistorySubSpellEnd());
				Add(new PowerHistorySubSpellStart { Prefab = old.Prefab, Source = old.Source, CollectTargets = true });
			}
			int before = _targetRounds.TryGetValue(block, out var had) ? had.Before : _openBlocks[top].Affected.Count;
			var round = new PowerHistoryTargetRound();
			_targetRounds[block] = (round, before);
			Add(round);
		}

		private void RoundHit(int top, int id)
		{
			if (_targetRounds.TryGetValue(_openBlocks[top].Block, out var r) && !r.Round.Info.Contains(id))
				AddAffected(r.Round.Info, id);
		}

		/// <summary>HsServer: the card being played and its Choose One option (0-based), for its PLAY / POWER blocks'
		/// SubOption (PlayCard; (0, −1) outside a Choose One play).</summary>
		internal (int Source, int SubOption) ChosenOption = (0, -1);

		public void Add(IPowerHistoryEntry entry)
		{
			if (entry is PowerHistoryBlockStart chosen && ChosenOption.Source != 0 && chosen.Source == ChosenOption.Source
			    && chosen.BlockType is BlockType.PLAY or BlockType.POWER)
				chosen.SubOption = ChosenOption.SubOption;
			// HsServer: rule tags never reach the client, whichever path sets them (Nozdormu's controller aura sent 990046/990047
			// and the client froze on them: local-test-20261007-233100 power-decoded 52336, 53510)
			if (entry is PowerHistoryTagChange internalTag && (int)internalTag.Tag >= PowerHistoryBuilder.InternalTagsFrom)
				return;
			// HsServer: the engine re-sets unchanged values (e.g. cost recalculations); only real changes are history.
			// Pulses (PREDAMAGE n→0, ZONE_POSITION n→0→n) change the value each step, so they stay.
			if (entry is PowerHistoryTagChange t && Reported.TryGetValue(t.EntityId, out Dictionary<GameTag, int> known)
				&& known.TryGetValue(t.Tag, out int current) && current == t.Value)
				return;
			BeforeAdd?.Invoke(entry);
			NewRoundIfRepeated(entry);
			_alsoTouched = AlsoTouched(entry);
			Full.Add(entry);
			Last.Add(entry);
			Record(entry);
			TrackEffect(entry);
		}

		/// <summary>HsServer: called before each entry is recorded (the state before it is still in Reported):
		/// Game opens a card's held-back buff effect at its first buff (Game.OpenSubSpellAtFirstBuff).</summary>
		internal Action<IPowerHistoryEntry> BeforeAdd;
		/// <summary>HsServer: a block put back open for a pick (Reopen) / ending again (CloseReopened): Game's recorded effects of it.</summary>
		internal Action<PowerHistoryBlockStart> Reopened, ReopenedEnds;

		// HsServer: what an entry touches that only the state before it tells (read before Record): a minion returned to
		// hand from play (live Freezing Trap's TARGETS: the attacker), an attack redirected (live Misdirection's TARGET:
		// the new defender, the attacker, the old defender).
		private int[] _alsoTouched;

		private int[] AlsoTouched(IPowerHistoryEntry e)
		{
			int Known(int id, GameTag tag) => Reported.TryGetValue(id, out var tags) && tags.TryGetValue(tag, out int v) ? v : 0;
			switch (e)
			{
				case PowerHistoryTagChange { Tag: GameTag.ZONE, Value: (int)Zone.HAND } z when Known(z.EntityId, GameTag.ZONE) == (int)Zone.PLAY:
					return new[] { z.EntityId };
				case PowerHistoryTagChange { Tag: GameTag.PROPOSED_DEFENDER } d when d.Value != 0:
					int old = Known(d.EntityId, GameTag.PROPOSED_DEFENDER), attacker = Known(d.EntityId, GameTag.PROPOSED_ATTACKER);
					return old != 0 && old != d.Value && attacker != 0 ? new[] { d.Value, attacker, old } : null;
				default:
					return null;
			}
		}

		/// <summary>HsServer: take back the innermost open SUB_SPELL when it is the last entry (nothing happened in it yet);
		/// false when something did.</summary>
		internal bool DropEmptySubSpell()
		{
			if (Full.Count == 0 || _openSubSpells.Count == 0 || !ReferenceEquals(Full[^1], _openSubSpells[^1].Start))
				return false;
			PowerHistorySubSpellStart start = _openSubSpells[^1].Start;
			Full.RemoveAt(Full.Count - 1);
			Last.Remove(start);
			_openSubSpells.RemoveAt(_openSubSpells.Count - 1);
			_subSpellSeen.RemoveAt(_subSpellSeen.Count - 1);
			if (start.First && start.Block != null) start.Block.HasSubSpell = false;
			return true;
		}

		/// <summary>HsServer: take back an entry not sent yet (a countered card's entering-play pause: live has only the counter's).</summary>
		internal bool Retract(IPowerHistoryEntry entry)
		{
			if (!Last.Remove(entry)) return false;
			Full.Remove(entry);
			return true;
		}

		/// <summary>HsServer: take back <paramref name="start"/> when nothing happened in it (only empty SUB_SPELLs after it): an
		/// aborted Secret "will simply remain in place without taking effect, revealing itself, or giving any indication"
		/// (Secret.txt; live: none of 68 Secret blocks is empty). False when something did.</summary>
		internal bool DropEmptyBlock(PowerHistoryBlockStart start)
		{
			int at = Full.LastIndexOf(start);
			if (at < 0 || _openBlocks.Count == 0 || !ReferenceEquals(_openBlocks[^1].Block, start)) return false;
			for (int i = at + 1; i < Full.Count; i++)
				if (!(Full[i] is PowerHistorySubSpellStart || Full[i] is PowerHistorySubSpellEnd)) return false;
			for (int i = Full.Count - 1; i >= at; i--)
			{
				Last.Remove(Full[i]);
				Full.RemoveAt(i);
			}
			_openBlocks.RemoveAt(_openBlocks.Count - 1);
			return true;
		}

		/// <summary>HsServer: add a change even when the reported state already has that value — for a client value the
		/// engine's own record lost (a tag set before a fresh CHANGE_ENTITY, then cleared after it).</summary>
		internal void AddUnchecked(IPowerHistoryEntry entry)
		{
			NewRoundIfRepeated(entry);
			Full.Add(entry);
			Last.Add(entry);
			Record(entry);
			TrackEffect(entry);
		}

		/// <summary>
		/// HsServer: puts an entity's entry onto the board into live order. The history from <paramref name="from"/> holds
		/// the board's changes for it (others moving over, its ZONE / ZONE_POSITION). The others' changes go first; then, if
		/// nothing referred to the entity since its FULL_ENTITY, that FULL_ENTITY moves here rebuilt as it is now (in PLAY
		/// at its position: returns true); otherwise its ZONE_POSITION then ZONE follow.
		/// </summary>
		/// <param name="zoneFirst">A card coming from the hand or deck: its ZONE change goes before its ZONE_POSITION (live).</param>
		internal bool PlaceEntered(int id, int from, int floor, Func<PowerHistoryFullEntity> fullNow, bool zoneFirst = false)
		{
			if (from > Full.Count) return false;
			List<IPowerHistoryEntry> tail = Full.GetRange(from, Full.Count - from);
			// Blocks opened in the tail stay whole where they are (a block's start refers to its source, its end doesn't:
			// moving only the start would put it after its own end — Glaciate's summoned C'Thun, whose bookkeeping trigger
			// ran right after it entered). Only the entity's own changes outside them move.
			var own = new List<IPowerHistoryEntry>();
			var others = new List<IPowerHistoryEntry>();
			int depth = 0;
			foreach (IPowerHistoryEntry e in tail)
			{
				if (e is PowerHistoryBlockStart) depth++;
				bool inBlock = depth > 0 || e is PowerHistoryBlockEnd;
				if (e is PowerHistoryBlockEnd && depth > 0) depth--;
				(!inBlock && Refers(e, id) ? own : others).Add(e);
			}
			if (own.Count == 0) return false;

			// Only a FULL_ENTITY not yet sent (this move) and after any open choice's mark can move: indexes before it stay.
			// Tag changes a script made on it before it entered fold into the rebuilt FULL_ENTITY (Totemic Call, Uldum
			// summons: otherwise it entered by a ZONE change after an out-of-play FULL_ENTITY — the client's blue placeholder
			// that slid into its slot).
			int created = UnsentCreation(id, floor, out List<int> folded, before: from);

			List<IPowerHistoryEntry> placed;
			if (created >= 0)
			{
				folded.Add(created); // descending indexes, all before `from`
				foreach (int i in folded)
				{
					Last.Remove(Full[i]);
					Full.RemoveAt(i);
				}
				from -= folded.Count;
				PowerHistoryFullEntity rebuilt = fullNow();
				Record(rebuilt); // what the client now knows of it: later changes compare against these values
				placed = others.Concat(new IPowerHistoryEntry[] { rebuilt }).ToList();
				// a block kept whole that already uses the entity (its own trigger as it entered) comes after its creation
				int firstUse = others.FindIndex(e => Refers(e, id));
				if (firstUse >= 0)
				{
					placed.RemoveAt(placed.Count - 1);
					placed.Insert(firstUse, rebuilt);
				}
			}
			else
			{
				placed = others.Concat(own.OrderBy(e => e is PowerHistoryTagChange { Tag: GameTag.ZONE } ? (zoneFirst ? -1 : 1) : 0)).ToList();
				// a block kept whole that already uses the entity (its own trigger as it entered: The Darkness becoming its
				// Dormant form, which sets it aside) comes after its entry — else its ZONE PLAY followed that block and put a
				// set-aside card back in play on the client (soak seed 900765, Dirty Rat pulling The Darkness)
				int firstUse = others.FindIndex(e => Refers(e, id));
				if (firstUse >= 0)
				{
					var ownSorted = placed.Skip(others.Count).ToList();
					placed = others.Take(firstUse).Concat(ownSorted).Concat(others.Skip(firstUse)).ToList();
				}
			}

			int n = tail.Count;
			Full.RemoveRange(from, n);
			Full.AddRange(placed);
			int lastFrom = Math.Max(0, Last.Count - n);
			Last.RemoveRange(lastFrom, Last.Count - lastFrom);
			Last.AddRange(placed);
			return created >= 0;
		}

		/// <summary>
		/// HsServer: a card an effect created goes into the hand the live way: its FULL_ENTITY (not yet sent, after
		/// <paramref name="floor"/>) moves here, rebuilt in HAND at its slot with its creator tags, the tag changes it had
		/// since folded in; then DISPLAYED_CREATOR follows as a TAG_CHANGE ("Created by"). No SHOW_ENTITY: the client then
		/// flies it in from its CREATOR and plays the blue spawn (Card.Process_OldActorIsNull → ActivateHandSpawnSpell).
		/// Returns false (history untouched) when the card isn't a fresh entity (a draw, a Discover pick already shown).
		/// </summary>
		/// <summary>HsServer (VIOLET_HOLD): forget an entity's unsent creation (an effect's new Coin that Aya's counterfeit replaces before it
		/// enters the hand). False (history untouched) when the client already knows it.</summary>
		internal bool DropUnsentCreation(int id, int floor)
		{
			int created = UnsentCreation(id, floor, out List<int> folded, neverInZone: true);
			if (created < 0) return false;
			folded.Add(created);
			foreach (int i in folded) // descending indexes
			{
				Last.Remove(Full[i]);
				Full.RemoveAt(i);
			}
			return true;
		}

		internal bool PlaceCreatedInHand(int id, int floor, Func<PowerHistoryFullEntity> fullNow)
		{
			int created = UnsentCreation(id, floor, out List<int> folded);
			if (created < 0) return false;

			folded.Add(created);
			foreach (int i in folded) // descending indexes
			{
				Last.Remove(Full[i]);
				Full.RemoveAt(i);
			}
			PowerHistoryFullEntity rebuilt = fullNow();
			rebuilt.Entity.Tags.TryGetValue(GameTag.DISPLAYED_CREATOR, out int displayed);
			rebuilt.Entity.Tags.Remove(GameTag.DISPLAYED_CREATOR);
			Full.Add(rebuilt);
			Last.Add(rebuilt);
			Record(rebuilt);
			if (displayed != 0)
				Add(PowerHistoryBuilder.TagChange(id, GameTag.DISPLAYED_CREATOR, displayed));
			return true;
		}

		/// <summary>HsServer: the index of the entity's FULL_ENTITY if it is still unsent (after <paramref name="floor"/>)
		/// and only tag changes referred to it since (<paramref name="folded"/>, descending); else -1. With
		/// <paramref name="neverInZone"/>: -1 too if it was ever in a zone (a card shuffled into the deck and drawn is a
		/// draw, not a creation in hand).</summary>
		/// <summary>HsServer: the innermost open block of any type (null: none) — the one a BLOCK_END ends.</summary>
		internal PowerHistoryBlockStart InnermostBlock => _openBlocks.Count > 0 ? _openBlocks[^1].Block : null;

		/// <summary>HsServer: the innermost open POWER / TRIGGER block (null: none).</summary>
		internal PowerHistoryBlockStart CurrentEffectBlock
		{
			get
			{
				for (int i = _openBlocks.Count - 1; i >= 0; i--)
					if (_openBlocks[i].Block.BlockType is BlockType.POWER or BlockType.TRIGGER)
						return _openBlocks[i].Block;
				return null;
			}
		}

		/// <summary>HsServer: the innermost open POWER / TRIGGER block of <paramref name="source"/> (null: none).</summary>
		internal PowerHistoryBlockStart EffectBlockOf(int source)
		{
			for (int i = _openBlocks.Count - 1; i >= 0; i--)
				if (_openBlocks[i].Block.BlockType is BlockType.POWER or BlockType.TRIGGER && _openBlocks[i].Block.Source == source)
					return _openBlocks[i].Block;
			return null;
		}

		/// <summary>HsServer: the source of the innermost open POWER / TRIGGER block — whose effect is running (0: none).</summary>
		internal int CurrentEffectSource
		{
			get
			{
				for (int i = _openBlocks.Count - 1; i >= 0; i--)
					if (_openBlocks[i].Block.BlockType is BlockType.POWER or BlockType.TRIGGER)
						return _openBlocks[i].Block.Source;
				return 0;
			}
		}

		internal int UnsentCreation(int id, int floor, out List<int> folded, bool neverInZone = false, int before = -1)
		{
			folded = new List<int>();
			for (int i = (before < 0 ? Full.Count : before) - 1; i >= Math.Max(0, floor); i--)
			{
				if (Full[i] is PowerHistoryFullEntity f && f.Entity.Id == id)
				{
					if (neverInZone && f.Entity.Tags.TryGetValue(GameTag.ZONE, out int z) && z != 0 && z != (int)Zone.SETASIDE)
						return -1;
					return i;
				}
				if (!Refers(Full[i], id)) continue;
				if (Full[i] is PowerHistoryTagChange t && !(neverInZone && t.Tag == GameTag.ZONE)) folded.Add(i);
				else return -1; // shown, used or moved since it was created
			}
			return -1;
		}

		// HsServer: the entity an entry acts on, for PowerHistoryBlockStart.Touched (game / player entities are dropped)
		private static int? TouchedBy(IPowerHistoryEntry e) => e switch
		{
			PowerHistoryTagChange { Tag: GameTag.TO_BE_DESTROYED or GameTag.CANT_PLAY or GameTag.FROZEN or GameTag.SILENCED, Value: 1 } t => t.EntityId,
			PowerHistoryTagChange { Tag: GameTag.CONTROLLER } t => t.EntityId,
			// Armor gained (live Ice Barrier, secrets-generators 19062-19072: its block names the hero; only blocks whose recorded
			// metadata names targets write it: RecordedBlockMeta)
			PowerHistoryTagChange { Tag: GameTag.ARMOR } t => t.EntityId,
			PowerHistoryChangeEntity c => c.Entity?.Id,
			PowerHistoryFullEntity { Entity: { } f } when f.Tags.TryGetValue(GameTag.ATTACHED, out int attached) => attached,
			PowerHistoryShowEntity { Entity: { } s } when s.Tags.TryGetValue(GameTag.ATTACHED, out int attached) => attached,
			// a copy acts on its original (live Mirror Entity's TARGET: the minion it copied)
			PowerHistoryFullEntity { Entity: { } copy } when copy.Tags.TryGetValue(HsTag.COPIED_FROM_ENTITY_ID, out int original) && original > 0 => original,
			_ => null,
		};

		internal static bool Refers(IPowerHistoryEntry e, int id) => e switch
		{
			PowerHistoryTagChange t => t.EntityId == id,
			PowerHistoryFullEntity f => f.Entity.Id == id,
			PowerHistoryShowEntity s => s.Entity.Id == id,
			PowerHistoryHideEntity h => h.EntityID == id,
			PowerHistoryBlockStart b => b.Source == id || b.Target == id,
			PowerHistoryMetaData m => m.Info?.Contains(id) == true,
			_ => false,
		};

		// HsServer: open blocks (innermost last) with the characters their own effects affect so far.
		private readonly List<(PowerHistoryBlockStart Block, List<int> Affected, bool HasTargetMeta, bool First)> _openBlocks =
			new List<(PowerHistoryBlockStart, List<int>, bool, bool)>();

		// HsServer: open blocks that have already damaged / healed / frozen something (see SUB_SPELL above).
		private readonly HashSet<PowerHistoryBlockStart> _actedBlocks = new HashSet<PowerHistoryBlockStart>();

		// HsServer: a block's end: what it names (TARGET, split) from what it affected.
		private void EndBlock(int index)
		{
			var (block, list, hasTargetMeta, _) = _openBlocks[index];
			_openBlocks.RemoveAt(index);
			if (block.Target > 0 && list.Any(id => id != block.Target))
			{
				int main = block.Amounts.TryGetValue(block.Target, out int a) ? a : -1;
				block.SplitTarget = list.Where(id => id != block.Target).Any(id => (block.Amounts.TryGetValue(id, out int o) ? o : -1) != main);
			}
			_actedBlocks.Remove(block);
			// a repeated effect names each round's targets in its own TARGET: the block's opening one only what it
			// affected before its first round
			if (_targetRounds.Remove(block, out var rounds))
			{
				if (rounds.Before > 0)
					block.EffectTargets = list.Take(rounds.Before).Distinct().ToList();
			}
			else if (!hasTargetMeta && list.Count > 0 && block.BlockType is BlockType.POWER or BlockType.TRIGGER)
				block.EffectTargets = list.Distinct().ToList();
		}

		// HsServer: blocks put back open while a pick resolves inside them (Reopen), innermost last.
		private readonly List<PowerHistoryBlockStart> _reopened = new List<PowerHistoryBlockStart>();

		/// <summary>
		/// HsServer: a pick resolves inside the blocks that offered it (Game.HoldForChoice holds their ends back until it's
		/// done, as live). The engine had already closed them: put the ones <paramref name="held"/> closes back open, so what
		/// the pick does is theirs (the card's own effect: Night Elf Huntress's arrow per shot, live; what it affects).
		/// </summary>
		internal void Reopen(List<IPowerHistoryEntry> held)
		{
			int depth = 0;
			var closed = new List<PowerHistoryBlockEnd>();
			foreach (IPowerHistoryEntry e in held)
			{
				if (e is PowerHistoryBlockStart) depth++;
				else if (e is PowerHistoryBlockEnd end)
				{
					if (depth > 0) depth--;
					else if (end.Closed != null) closed.Add(end); // innermost first
				}
			}
			for (int i = closed.Count - 1; i >= 0; i--)
			{
				PowerHistoryBlockEnd end = closed[i];
				_openBlocks.Add((end.Closed, end.ClosedAffected ?? new List<int>(), end.ClosedHasTargetMeta, false));
				_reopened.Add(end.Closed);
				Reopened?.Invoke(end.Closed);
			}
		}

		/// <summary>HsServer: the held ends are shown: the reopened blocks end again (with what the pick added).</summary>
		internal void CloseReopened()
		{
			for (int i = _reopened.Count - 1; i >= 0; i--)
			{
				int index = _openBlocks.FindLastIndex(b => ReferenceEquals(b.Block, _reopened[i]));
				ReopenedEnds?.Invoke(_reopened[i]);
				if (index >= 0) EndBlock(index);
			}
			_reopened.Clear();
		}

		private void TrackEffect(IPowerHistoryEntry entry)
		{
			int top = _openBlocks.Count - 1;
			// something happened in the block (its EFFECT_SELECTION without a recorded condition: Loader.SelectFx); markers
			// and pacing (SLUSH_TIME, EFFECT_TIMING, an empty SUB_SPELL) aren't
			if (top >= 0 && !(entry is PowerHistoryBlockEnd || entry is PowerHistorySubSpellStart || entry is PowerHistorySubSpellEnd ||
			                  entry is PowerHistoryMetaData { Type: not (MetaDataType.DAMAGE or MetaDataType.HEALING) }))
				_openBlocks[top].Block.HasContent = true;
			if (top >= 0 && TouchedBy(entry) is int touched && touched > 3 && touched != _openBlocks[top].Block.Source &&
			    !_openBlocks[top].Block.Touched.Contains(touched))
				_openBlocks[top].Block.Touched.Add(touched);
			else if (top >= 0 && TouchedBy(entry) is int self && self == _openBlocks[top].Block.Source)
				_openBlocks[top].Block.TouchesSelf = true;
			// a minion the block's own effect creates on the board (live: a Secret's TARGETS name it right after it: Bear
			// Trap, Venomstrike Trap, Cat Trick; ClientStream)
			if (top >= 0 && entry is PowerHistoryFullEntity { Entity: { } made } &&
			    made.Tags.TryGetValue(GameTag.ZONE, out int madeZone) && madeZone == (int)Zone.PLAY &&
			    made.Tags.TryGetValue(GameTag.CARDTYPE, out int madeType) && madeType == (int)CardType.MINION)
				_openBlocks[top].Block.Created.Add(made.Id);
			if (top >= 0 && _alsoTouched != null)
				foreach (int id in _alsoTouched)
					if (id > 3 && id != _openBlocks[top].Block.Source && !_openBlocks[top].Block.Touched.Contains(id))
						_openBlocks[top].Block.Touched.Add(id);
			_alsoTouched = null;
			// A block that opens with its own TARGET metadata already names its targets. Its own EFFECT_TIMING doesn't: live
			// sends EFFECT_TIMING, then the TARGET (Fan of Knives, Lord Godfrey, SC_004hp, RLK_208); a Dormant's awaken block
			// opens with EFFECT_TIMING and still names what it hits (ClientStream puts the TARGET right after it).
			if (top >= 0 && _openBlocks[top].First)
			{
				var b = _openBlocks[top];
				b.First = false;
				b.HasTargetMeta = entry is PowerHistoryMetaData { Type: MetaDataType.TARGET };
				if (entry is PowerHistoryMetaData { Type: MetaDataType.EFFECT_TIMING })
					b.Block.OpensWithEffectTiming = true;
				// an effect that names its own targets (Generic.NameTargets) keeps them: no engine TARGET is added (ClientStream)
				if (entry is PowerHistoryMetaData { Type: MetaDataType.TARGET })
					b.Block.NamesItsTargets = true;
				_openBlocks[top] = b;
			}
			switch (entry)
			{
				case PowerHistoryBlockStart start:
					var affected = new List<int>();
					if (start.Target > 0)
						AddAffected(affected, start.Target);
					_openBlocks.Add((start, affected, false, true));
					break;
				case PowerHistorySubSpellStart sub when top >= 0:
					sub.Block = _openBlocks[top].Block;
					// HsServer: a SUB_SPELL that opens after the block already damaged / healed / froze something isn't the
					// effect's own visual (an Excavate after "Deal 3 damage"): the TARGET stays at the block's opening
					// (it still collects what it affects: a buff / area effect's next round, Targets at its END)
					if (_actedBlocks.Contains(sub.Block))
						sub.First = false;
					else
					{
						sub.First = !sub.Block.HasSubSpell;
						sub.Block.HasSubSpell = true;
					}
					_openSubSpells.Add((sub, new List<int>()));
					_subSpellSeen.Add(new HashSet<int>());
					break;
				case PowerHistorySubSpellEnd _ when _openSubSpells.Count > 0:
					var (opened, hit) = _openSubSpells[^1];
					_openSubSpells.RemoveAt(_openSubSpells.Count - 1);
					_subSpellSeen.RemoveAt(_subSpellSeen.Count - 1);
					if (opened.CollectTargets && opened.Targets.Count == 0)
						opened.Targets.AddRange(hit.Distinct());
					break;
				case PowerHistoryBlockEnd end when top >= 0:
					(end.Closed, end.ClosedAffected, end.ClosedHasTargetMeta, _) = _openBlocks[top];
					EndBlock(top);
					break;
				case PowerHistoryMetaData { Type: MetaDataType.DAMAGE or MetaDataType.HEALING } m when top >= 0 && m.Info != null:
					_actedBlocks.Add(_openBlocks[top].Block);
					foreach (int id in m.Info)
					{
						var amounts = _openBlocks[top].Block.Amounts;
						amounts[id] = (amounts.TryGetValue(id, out int had) ? had : 0) + m.Data;
						AddAffected(_openBlocks[top].Affected, id);
						SubSpellHit(id);
						RoundHit(top, id);
					}
					break;
				// an enchantment attached inside a Buff sub-spell: its target
				case PowerHistoryShowEntity { Entity: { } shown } when shown.Tags.TryGetValue(GameTag.ATTACHED, out int attachedTo):
					SubSpellHit(attachedTo);
					break;
				case PowerHistoryFullEntity { Entity: { } full } when full.Tags.TryGetValue(GameTag.ATTACHED, out int attachedTo2):
					SubSpellHit(attachedTo2);
					break;
				case PowerHistoryTagChange { Tag: GameTag.FROZEN, Value: 1 } t when top >= 0:
					_actedBlocks.Add(_openBlocks[top].Block);
					AddAffected(_openBlocks[top].Affected, t.EntityId);
					SubSpellHit(t.EntityId);
					RoundHit(top, t.EntityId);
					break;
				// HsServer: a heal attempt on a full-Health character has no HEALING meta, but live still targets it.
				case PowerHistoryTagChange { Tag: GameTag.PREHEALING } t when top >= 0 && t.Value > 0:
					AddAffected(_openBlocks[top].Affected, t.EntityId);
					RoundHit(top, t.EntityId);
					break;
			}
		}

		/// <summary>
		/// HsServer: every entity's tag values as reported so far (omniscient — what a client would know with nothing
		/// hidden). <see cref="Game.PublishDerived"/> compares the engine's computed values with this and reports the
		/// differences, so values that change without their own history entry (auras, hero attack, costs) still reach
		/// the client, at the point they change.
		/// </summary>
		public Dictionary<int, Dictionary<GameTag, int>> Reported { get; } = new Dictionary<int, Dictionary<GameTag, int>>();

		private void Record(IPowerHistoryEntry entry)
		{
			switch (entry)
			{
				case PowerHistoryCreateGame c:
					Merge(c.Game.Id, c.Game.Tags, replace: true);
					foreach (PowerPlayer p in c.Players)
						Merge(p.PowerEntity.Id, p.PowerEntity.Tags, replace: true);
					break;
				case PowerHistoryFullEntity f:
					Merge(f.Entity.Id, f.Entity.Tags, replace: true);
					break;
				case PowerHistoryShowEntity s:
					Merge(s.Entity.Id, s.Entity.Tags, replace: false);
					break;
				case PowerHistoryChangeEntity ch:
					Merge(ch.Entity.Id, ch.Entity.Tags, replace: false);
					break;
				case PowerHistoryHideEntity h:
					Of(h.EntityID)[GameTag.ZONE] = (int)h.Zone;
					break;
				// (2026-11-01) a "DEF CHANGE" sets the card's base, not the value the client shows: the plain change that follows it
				// (live Heist Health block: DEF CHANGE then the value) must not be dropped as unchanged
				case PowerHistoryTagChange { ChangeDef: true }:
					break;
				case PowerHistoryTagChange t:
					Of(t.EntityId)[t.Tag] = t.Value;
					break;
			}
		}

		private Dictionary<GameTag, int> Of(int id)
		{
			if (!Reported.TryGetValue(id, out Dictionary<GameTag, int> tags))
				Reported[id] = tags = new Dictionary<GameTag, int>();
			return tags;
		}

		private void Merge(int id, IDictionary<GameTag, int> tags, bool replace)
		{
			if (replace) Reported.Remove(id);
			Dictionary<GameTag, int> into = Of(id);
			if (tags == null) return;
			foreach (KeyValuePair<GameTag, int> t in tags)
				into[t.Key] = t.Value;
		}

		public string Print(bool fullFlag = true)
		{
			var str = new StringBuilder();
			if (fullFlag)
				Full.ForEach(p => str.Append(p.Print()));
			else
				Last.ForEach(p => str.Append(p.Print()));
			return str.ToString();
		}
	}

	//message PowerHistoryData
	//{
	//    optional PowerHistoryEntity full_entity = 1;
	//    optional PowerHistoryEntity show_entity = 2;
	//    optional PowerHistoryHide hide_entity = 3;
	//    optional PowerHistoryTagChange tag_change = 4;
	//    optional PowerHistoryCreateGame create_game = 5;
	//    optional PowerHistoryStart power_start = 6;
	//    optional PowerHistoryEnd power_end = 7;
	//    optional PowerHistoryMetaData meta_data = 8;
	//    optional PowerHistoryEntity change_entity = 9;
	//}
	public interface IPowerHistoryEntry
	{
		PowerType PowerType { get; }
		string Print();
	}

	//message PowerHistoryCreateGame
	//{
	//    required Entity game_entity = 1;
	//    repeated Player players = 2;
	//}
	public class PowerHistoryCreateGame : IPowerHistoryEntry
	{
		public PowerType PowerType => PowerType.CREATE_GAME;
		public PowerEntity Game { get; set; }
		public PowerPlayer[] Players { get; set; }
		public string Print()
		{
			var str = new StringBuilder();
			str.AppendLine($"{PowerType}");
			str.AppendLine($" - Game Entity = [{Game.Print()}]");
			foreach (PowerPlayer player in Players)
			{
				str.AppendLine($" - Player Entity = [{player.Print()}]");
			}
			return str.ToString();
		}
	}

	//message PowerHistoryStart
	//{
	//    required HistoryBlock.Type type = 1;
	//    required int32 index = 2;
	//    required int32 source = 3;
	//    required int32 target = 4;
	//    optional string effect_card_id = 5;
	//}
	public class PowerHistoryBlockStart : IPowerHistoryEntry
	{
		public PowerType PowerType => PowerType.BLOCK_START;
		public BlockType BlockType { get; set; }
		public int Index { get; set; } = -1;
		public int Source { get; set; }
		public int Target { get; set; }
		public string EffectCardId { get; set; } = "";

		/// <summary>HsServer: the keyword a TRIGGER block fires for (live: TriggerKeyword=LIFESTEAL); 0 = none. The client picks
		/// the keyword's visual from it.</summary>
		public int TriggerKeyword { get; set; }

		/// <summary>HsServer: live flag — the trigger gets its own history tile (and the triggered big card).</summary>
		public bool ShowInHistory { get; set; }
		/// <summary>HsServer: the client holds this block back until its parent block ends (live OVERHEAL triggers: PowerProcessor
		/// CanDeferTaskList), so an Overheal raised inside the heal still plays after it.</summary>
		public bool IsDeferrable { get; set; }

		/// <summary>
		/// HsServer: for effect blocks (POWER / TRIGGER), the characters the effect lands on — its chosen target, then
		/// every character it damages, heals or freezes directly (not in nested blocks). Filled by
		/// <see cref="PowerHistory"/> when the block ends; live opens such a block with one META_DATA TARGET listing
		/// them, which is what the client's effect FX land on. Null when there are none.
		/// </summary>
		public List<int> EffectTargets { get; set; }

		/// <summary>HsServer: the block holds a SUB_SPELL (its TARGET metadata moves inside the first one).</summary>
		public bool HasSubSpell { get; set; }

		/// <summary>HsServer: the Choose One option played, 0-based (−1: none). Live sends it on the card's PLAY and POWER
		/// blocks (Cenarius 1 = Shan'do's Lesson, Living Roots 0 = the damage: live-20261004-night); the client plays that
		/// option's effect from the card's m_SubOptionEffectDefs. Set by PowerHistory.Add from <see cref="PowerHistory.ChosenOption"/>.</summary>
		public int SubOption { get; set; } = -1;

		/// <summary>HsServer: what the block's effect did to each character (damage / healing amounts, summed), and whether its
		/// chosen target was treated differently from the others it hit — a "split" effect (Explosive Shot 5 / 2, Howling
		/// Blast 3 / 1, Obliterate: destroy / damage). Live names only the chosen target of a split effect; an equal one
		/// (Cone of Cold: 1 to each, user 2026-10-05) is one effect over all of them (ClientStream.BlockTargets).</summary>
		public Dictionary<int, int> Amounts { get; } = new Dictionary<int, int>();
		public bool SplitTarget { get; set; }

		/// <summary>HsServer: a card was drawn while this was the innermost effect block (Draw.cs). A drawing block whose
		/// client effect is a BuffImpact opens with TARGET on its source (ClientStream, Loader.BlockMeta.HasImpactFx).</summary>
		public bool Draws { get; set; }

		/// <summary>HsServer: what the block's own effect acts on (not nested blocks), in order: destroyed, countered, frozen,
		/// silenced, taken control of, transformed, or given an enchantment. For cards whose live block TARGET names what
		/// they act on although they don't damage / heal / freeze (Deadly Shot, Counterspell, Equality, Defender of Argus:
		/// Loader.BlockMeta.TargetMode.Touched).</summary>
		public List<int> Touched { get; } = new List<int>();

		/// <summary>HsServer: the block opens with its own TARGETs (Generic.NameTargets): ClientStream adds none.</summary>
		public bool NamesItsTargets { get; set; }

		/// <summary>HsServer: the engine's first entry in the block is an EFFECT_TIMING (a Dormant's awaken, an Excavate):
		/// ClientStream sends the block's TARGET right after it, as live.</summary>
		public bool OpensWithEffectTiming { get; set; }

		/// <summary>HsServer: minions the block's own effect created on the board, in order (PowerHistory.TrackEffect).</summary>
		public List<int> Created { get; } = new List<int>();

		/// <summary>HsServer: the block's effect acted on its own card (enchanted it: a minion buffing itself, Lone Champion).
		/// Not in Touched (a card's own block never names itself there).</summary>
		public bool TouchesSelf { get; set; }

		/// <summary>HsServer: the card's own "If …" held in this block (null: no condition was checked). Set by the conditional
		/// machinery (FlagTask: ComplexTask.If / Conditional, ConditionTask + FlagTask) or Generic.ConditionHeld; read for
		/// the block's META_DATA EFFECT_SELECTION (Loader.SelectFx, ClientStream).</summary>
		public bool? ConditionHeld { get; set; }

		/// <summary>HsServer: the variant of the card's client effect this block plays, chosen by its script
		/// (Generic.SelectEffect: a direction, a deck, a form). Wins over <see cref="ConditionHeld"/>.</summary>
		public int? EffectSelection { get; set; }

		/// <summary>HsServer: something happened inside the block (any entry the client sees: a change, a nested block…).</summary>
		public bool HasContent { get; set; }

		public string Print()
		{
			var str = new StringBuilder();
			str.AppendLine($"{PowerType} {BlockType} Entity=[{Source}] EffectCardId={EffectCardId} EffectIndex={Index} Target={Target}");
			return str.ToString();
		}
	}

	//message PowerHistoryEnd
	//{
	//}
	public class PowerHistoryBlockEnd : IPowerHistoryEntry
	{
		public PowerType PowerType => PowerType.BLOCK_END;

		/// <summary>HsServer: the block this end closed and what it had affected (PowerHistory.Reopen).</summary>
		internal PowerHistoryBlockStart Closed;
		internal List<int> ClosedAffected;
		internal bool ClosedHasTargetMeta;

		public string Print()
		{
			var str = new StringBuilder();
			str.AppendLine($"{PowerType}");
			return str.ToString();
		}
	}

	public class PowerHistoryFullEntity : IPowerHistoryEntry
	{
		public PowerType PowerType => PowerType.FULL_ENTITY;
		public PowerHistoryEntity Entity { get; set; }

		public string Print()
		{
			var str = new StringBuilder();
			str.AppendLine($"{PowerType} - Creating Entity = [{Entity.Print()}]");
			return str.ToString();
		}
	}

	public class PowerHistoryShowEntity : IPowerHistoryEntry
	{
		public PowerType PowerType => PowerType.SHOW_ENTITY;
		public PowerHistoryEntity Entity { get; set; }
		public string Print()
		{
			var str = new StringBuilder();
			str.AppendLine($"{PowerType} - Updating Entity = [{Entity.Print()}]");
			return str.ToString();
		}
	}

	//message PowerHistoryHide
	//{
	//    required int32 entity = 1;
	//    required int32 zone = 2;
	//}
	/// <summary>HsServer: live SHUFFLE_DECK — the player's deck was shuffled (after the mulligan; after a card was shuffled
	/// into it). The client forgets what it knew about the deck's order.</summary>
	public class PowerHistoryShuffleDeck : IPowerHistoryEntry
	{
		public PowerType PowerType => PowerType.SHUFFLE_DECK;
		public int PlayerId { get; set; }
		public string Print() => $"SHUFFLE_DECK PlayerID={PlayerId}";
	}

	public class PowerHistoryHideEntity : IPowerHistoryEntry
	{
		public PowerType PowerType => PowerType.HIDE_ENTITY;
		public int EntityID { get; set; }
		public Zone Zone { get; set; }

		public string Print()
		{
			var str = new StringBuilder();
			str.AppendLine($"{PowerType} Entity = [{EntityID}] tag={Zone}");
			return str.ToString();
		}
	}

	//message PowerHistoryTagChange
	//{
	//    required int32 entity = 1;
	//    required int32 tag = 2;
	//    required int32 value = 3;
	//}
	public class PowerHistoryTagChange : IPowerHistoryEntry
	{
		public PowerType PowerType => PowerType.TAG_CHANGE;
		public int EntityId { get; set; }
		public GameTag Tag { get; set; }
		public int Value { get; set; }
		/// <summary>HsServer (2026-10-09): the client's "DEF CHANGE" — the change also becomes the card's base value (the client then
		/// shows no buff colour: live Heist run Health, live-20261004-night Power.log "tag=HEALTH value=60 DEF CHANGE").</summary>
		public bool ChangeDef { get; set; }

		public string Print()
		{
			var str = new StringBuilder();
			str.AppendLine(Model.Tag.TypedTags.ContainsKey(Tag)
				? $"{PowerType} Entity = [{EntityId}] Tag={Tag} Value={Enum.GetName(Model.Tag.TypedTags[Tag], Value)}"
				: $"{PowerType} Entity = [{EntityId}] Tag={Tag} Value={Value}");
			return str.ToString();
		}

		public override string ToString()
		{
			return $"[{EntityId}][{Tag}][{Value}]";
		}
	}

	//message PowerHistoryMetaData
	//{
	//    repeated int32 info = 2;
	//    HistoryMeta.Type type = 3;
	//    int32 data = 4;
	//}
	/// <summary>HsServer: SUB_SPELL_START — a card-specific effect prefab the client plays around the entries up to the
	/// matching SUB_SPELL_END (live: e.g. Zuramat's Prison's discard missile). Prefab is the wire form "Name:guid".</summary>
	public class PowerHistorySubSpellStart : IPowerHistoryEntry
	{
		public PowerType PowerType => PowerType.SUB_SPELL_START;
		public string Prefab { get; set; }
		public int Source { get; set; }
		public List<int> Targets { get; set; } = new List<int>();
		/// <summary>The effect block it plays in, and whether it is that block's first SUB_SPELL (live: the block's
		/// TARGET metadata goes inside the first one; a block without targets has EFFECT_TIMING before it).</summary>
		public PowerHistoryBlockStart Block { get; set; }
		public bool First { get; set; }
		/// <summary>Targets = what it affects inside it (on the board), filled at its END (Buff / AE roles).</summary>
		public bool CollectTargets { get; set; }
		public string Print() => $"{PowerType} Prefab={Prefab} Source={Source} Targets={string.Join(", ", Targets)}";
	}

	/// <summary>HsServer: one repeat's META_DATA TARGET (<see cref="PowerHistory.NewTargetRound"/>): Info = what that
	/// repeat affected on the board, filled as it happens; not sent when it affected nothing.</summary>
	public class PowerHistoryTargetRound : PowerHistoryMetaData
	{
		public PowerHistoryTargetRound()
		{
			Type = MetaDataType.TARGET;
			Info = new List<int>();
		}
	}

	/// <summary>HsServer: SUB_SPELL_END.</summary>
	public class PowerHistorySubSpellEnd : IPowerHistoryEntry
	{
		public PowerType PowerType => PowerType.SUB_SPELL_END;
		public string Print() => $"{PowerType}";
	}

	public class PowerHistoryMetaData : IPowerHistoryEntry
	{
		public PowerType PowerType => PowerType.META_DATA;
		public MetaDataType Type { get; set; }
		public int Data { get; set; }
		public List<int> Info { get; set; }

		public string Print()
		{
			var str = new StringBuilder();
			str.AppendLine($"{PowerType} Type={Type} Data={Data} Info={string.Join(", ", Info)}");
			return str.ToString();
		}
	}

	public class PowerHistoryChangeEntity : IPowerHistoryEntry
	{
		public PowerType PowerType => PowerType.CHANGE_ENTITY;
		public string CardId { get; set; }
		public PowerHistoryEntity Entity { get; set; }

		public string Print()
		{
			var str = new StringBuilder();
			str.AppendLine($"{PowerType} Type={CardId} - Change Entity = [{Entity.Print()}]");
			return str.ToString();
		}
	}

	//message PowerHistoryEntity
	//{
	//    required int32 entity = 1;
	//    required string name = 2;
	//    repeated Tag tags = 3;
	//}
	//message Tag
	//{
	//    required int32 name = 1;
	//    required int32 value = 2;
	//}
	public class PowerHistoryEntity
	{
		public int Id { get; set; }
		public string Name { get; set; }
		public IDictionary<GameTag, int> Tags { get; set; } = new Dictionary<GameTag, int>();
		public string Print()
		{
			var str = new StringBuilder();
			str.AppendLine($"Id={Id}, Name={Name}, Tags=[");
			foreach (KeyValuePair<GameTag, int> pair in Tags)
			{
				str.AppendLine(Tag.TypedTags.ContainsKey(pair.Key)
					? $"      [{pair.Key},{Enum.GetName(Tag.TypedTags[pair.Key], pair.Value)}]"
					: $"      [{pair.Key},{pair.Value}]");
			}
			str.Append("]");
			return str.ToString();
		}
	}

	//message Player
	//{
	//    required int32 id = 1;
	//    required pegasus.pegasusshared.BnetId game_account_id = 2;
	//    required int32 card_back = 3;
	//    required Entity entity = 4;
	//}
	public class PowerPlayer
	{
		public int PlayerId { get; set; }
		public int AccountId { get; set; }
		public int CardBack { get; set; }
		public PowerEntity PowerEntity { get; set; }
		public string Print()
		{
			var str = new StringBuilder();
			str.Append($"PlayerId={PlayerId}, AccountId={AccountId}, CardBack={CardBack}, Entity=[{PowerEntity.Print()}]");
			return str.ToString();
		}
	}

	//message Entity
	//{
	//    required int32 id = 1;
	//    repeated Tag tags = 2;
	//}
	public class PowerEntity
	{
		public int Id { get; set; }
		public Dictionary<GameTag, int> Tags { get; set; } = new Dictionary<GameTag, int>();
		public string Print()
		{
			var str = new StringBuilder();
			str.AppendLine($"Id={Id} Tags=[");
			foreach (KeyValuePair<GameTag, int> pair in Tags)
			{
				str.AppendLine($"      [{pair.Key},{pair.Value}]");
			}
			str.Append("]");
			return str.ToString();
		}
	}
}
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
