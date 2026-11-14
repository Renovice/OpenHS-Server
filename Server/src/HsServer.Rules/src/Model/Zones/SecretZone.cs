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
using SabberStoneCore.Enums;
using SabberStoneCore.Exceptions;
using SabberStoneCore.Model.Entities;
using System.Collections.Generic;

namespace SabberStoneCore.Model.Zones
{
	public class SecretZone : LimitedZone<Spell>
	{
		public const int SecretZoneMaxSize = 5;

		/// <summary>
		/// The active Quest (the first, when a split card put two up); null means none. Setting it replaces every Quest.
		/// HsServer: "one active Quest" asks <c>Quest != null</c>; a script checking it is its own Quest asks <see cref="IsActiveQuest"/>.
		/// </summary>
		public Spell Quest
		{
			get => _quests.Count > 0 ? _quests[0] : null;
			set
			{
				_quests.Clear();
				if (value != null) _quests.Add(value);
			}
		}

		/// <summary>HsServer (livecheck 2026-10-10, user "follow live"): the active Quests — one, or both halves of a card that is two
		/// Quests (Reach Equilibrium TLC_817 → TLC_817t + TLC_817t2, live session-20261001-130515 13259: the card set aside, both
		/// halves FULL_ENTITY into SECRET, each counting its own spells; Generic.SplitQuest).</summary>
		public IReadOnlyList<Spell> Quests => _quests;

		private readonly List<Spell> _quests = new List<Spell>();

		/// <summary>HsServer: the client tag each half of a split Quest carries (BACON_EVOLUTION_CARD_ID 2519): the dbf id of the
		/// combined reward, which is its card's own QUEST_REWARD_DATABASE_ID (TLC_817t / TLC_817t2: 119879 = TLC_817t5).</summary>
		public const GameTag QUEST_HALF_OF = (GameTag)2519;

		/// <summary>HsServer: is <paramref name="quest"/> one of this player's active Quests?</summary>
		public bool IsActiveQuest(Spell quest) => quest != null && _quests.Contains(quest);

		/// <summary>HsServer: <paramref name="quest"/> is no longer active (completed, a Questline stage done, set aside).</summary>
		public void RemoveQuest(Spell quest)
		{
			if (!_quests.Remove(quest)) return;
			if (quest.Zone == this) quest.Zone = null;
		}

		/// <summary>HsServer: two Quests may be up together only as the halves of one split card (engine-rules.md One Quest).</summary>
		public static bool HalvesOfOneQuest(Card a, Card b) => a[QUEST_HALF_OF] > 0 && a[QUEST_HALF_OF] == b[QUEST_HALF_OF] && a.Id != b.Id;

		public SecretZone(Controller controller) : base(Zone.SECRET, SecretZoneMaxSize)
		{
			Game = controller.Game;
			Controller = controller;
		}

		private SecretZone(Controller c, SecretZone zone) : base(c, zone)
		{
			// HsServer: the copied Quests know their zone, as added ones do (a Rewind point's Quest had none: soak 2026-10-08)
			foreach (Spell q in zone._quests)
			{
				var copy = (Spell)q.Clone(c);
				copy.Zone = this;
				_quests.Add(copy);
			}
		}

		// HsServer: rules D4 (user rule) — the hero zone has 5 slots for Secrets, Sidequests, the Quest (and Sigils): an
		// active Quest takes one of them (each half of a split one too).
		public override bool IsFull => _count + _quests.Count >= SecretZoneMaxSize;

		public override int FreeSpace => SecretZoneMaxSize - _count - _quests.Count;

		public override void Add(Spell entity, int zonePosition = -1)
		{
			Actions.Generic.ObjectiveEnters(entity); // HsServer (2026-10-08): an Objective / Aura notes its cast turn and counter slot
			if (entity.Card.IsQuest)
			{
				if (_quests.Count > 0 && !_quests.TrueForAll(q => HalvesOfOneQuest(q.Card, entity.Card)))
					throw new ZoneException($"Another quest is already in play");

				_quests.Add(entity);
				entity[GameTag.ZONE] = (int)Type;
				entity.Zone = this;

				Game.Log(LogLevel.DEBUG, BlockType.PLAY, "Zone", !Game.Logging ? "" : $"Quest {entity} has been added to zone '{Type}'.");

				entity.OrderOfPlay = Game.NextOop;

				return;
			}

			base.Add(entity, zonePosition);
			Game.Log(LogLevel.DEBUG, BlockType.PLAY, "Zone", !Game.Logging ? ""
				: $"Entity '{entity} ({entity.Card.Type})' has been added to zone '{Type}' in position '{entity.ZonePosition}'."); 

			entity.OrderOfPlay = Game.NextOop;
		}

		/// <summary>HsServer: a Quest leaves through its own list (the base would search the Secrets for it).</summary>
		public override Spell Remove(Spell entity)
		{
			if (!_quests.Contains(entity))
				return base.Remove(entity);
			RemoveQuest(entity);
			entity.Zone = null;
			entity.ActivatedTrigger?.Remove();
			return entity;
		}

		public override void ChangeEntity(Spell oldEntity, Spell newEntity)
		{
			throw new NotImplementedException();
		}

		public override IEnumerator<Spell> GetEnumerator()
		{
			var entities = _entities;
			for (int i = 0; i < _count; i++)
				yield return entities[i];
			foreach (Spell quest in _quests.ToArray())
				yield return quest;
		}

		public SecretZone Clone(Controller c)
		{
			return new SecretZone(c, this);
		}
	}
}
