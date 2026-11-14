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
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.SimpleTasks
{
	public class GetGameTagTask : SimpleTask
	{
		public GetGameTagTask(GameTag tag, EntityType entityType, int entityIndex = 0, int numberIndex = 0)
		{
			Tag = tag;
			Type = entityType;
			EntityIndex = entityIndex;
			NumberIndex = numberIndex;
		}

		public GameTag Tag { get; set; }
		public EntityType Type { get; set; }
		public int EntityIndex { get; set; }
		public int NumberIndex { get; set; }

		public override TaskState Process(in Game game, in Controller controller, in IEntity source,
			in IPlayable target,
			in TaskStack stack = null)
		{
			IList<IPlayable> entities = IncludeTask.GetEntities(Type, in controller, source, target, stack?.Playables);
			if (entities == null || entities.Count == 0) return TaskState.STOP;

			int value;
			// HsServer (livecheck 2026-10-10): a later entity the list doesn't have reads 0 — "gain their Attack and Health" over the
			// minions next to it (Void Terror: entity 0 and 1 of its adjacent minions) still sums the one that is there (live
			// session-20261006-145846 5344: one neighbour, its stats gained). Ours stopped the whole Battlecry. An empty list still stops.
			if (entities.Count <= EntityIndex)
				value = 0;
			else if (Tag == GameTag.ENTITY_ID)
				value = entities[EntityIndex].Id;
			else if (entities[EntityIndex] is Character c)
				switch (Tag)
				{
					case GameTag.ATK:
						value = c.AttackDamage;
						break;
					case GameTag.HEALTH:
						value = c.BaseHealth; // (the maximum: scripts wanting the current Health take DAMAGE off, as Divine Spirit)
						break;
					case GameTag.DAMAGE:
						value = c.Damage;
						break;
					case GameTag.EXTRA_ATTACKS_THIS_TURN:
						if (c is Hero h)
							value = h.ExtraAttacksThisTurn;
						else
							value = 0;
						break;
					default:
						value = c[Tag];
						break;
				}
			else
				value = entities[EntityIndex][Tag];


			if (NumberIndex == 0)
				stack.Number = value;
			else
				switch (NumberIndex)
				{
					case 1:
						stack.Number1 = value;
						break;
					case 2:
						stack.Number2 = value;
						break;
					case 3:
						stack.Number3 = value;
						break;
					case 4:
						stack.Number4 = value;
						break;
				}

			return TaskState.COMPLETE;
		}
	}

	/// <summary>
	///     Gets number of the current event and stores it to the stack.
	///     (e.g. the amount damage dealt or heal taken)
	/// </summary>
	public class GetEventNumberTask : SimpleTask
	{
		private readonly int _numberIndex;

		public GetEventNumberTask(int numberIndex = 0)
		{
			_numberIndex = numberIndex;
		}

		public override TaskState Process(in Game game, in Controller controller, in IEntity source,
			in IPlayable target,
			in TaskStack stack = null)
		{
			switch (_numberIndex)
			{
				case 0:
					stack.Number = game.CurrentEventData?.EventNumber ?? 0;
					break;
				case 1:
					stack.Number1 = game.CurrentEventData?.EventNumber ?? 0;
					break;
				case 2:
					stack.Number2 = game.CurrentEventData?.EventNumber ?? 0;
					break;
				case 3:
					stack.Number3 = game.CurrentEventData?.EventNumber ?? 0;
					break;
				case 4:
					stack.Number4 = game.CurrentEventData?.EventNumber ?? 0;
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}

			return TaskState.COMPLETE;
		}
	}

	public class SetEventNumberTask : SimpleTask
	{
		private readonly int _num;
		public SetEventNumberTask()
		{
			_num = -1;
		}

		public SetEventNumberTask(int num)
		{
			_num = num;
		}

		public override TaskState Process(in Game game, in Controller controller, in IEntity source,
			in IPlayable target,
			in TaskStack stack = null)
		{
			game.CurrentEventData.EventNumber = _num > 0 ? _num : stack.Number;

			return TaskState.COMPLETE;
		}
	}
}
