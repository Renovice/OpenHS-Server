using System.Collections.Generic;
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
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Actions;

namespace SabberStoneCore.Tasks.SimpleTasks
{
	public class ReplaceHeroPower : SimpleTask
	{
		private readonly Card _heroPowerCard;

		public ReplaceHeroPower()
		{
		}

		public ReplaceHeroPower(Card heroPowerCard)
		{
			_heroPowerCard = heroPowerCard;
		}

		public override TaskState Process(in Game game, in Controller controller, in IEntity source,
			in IPlayable target,
			in TaskStack stack = null)
		{
			if (controller == null) return TaskState.STOP;

			HeroPower power;

			if (_heroPowerCard == null)
			{
				if (stack?.Playables.Count != 1 || !(stack.Playables[0] is HeroPower)) return TaskState.STOP;

				power = (HeroPower) stack.Playables[0];
			}
			else
			{
				// HsServer (live, Shadowform: live-20261004-shadowform-leeroy): the new Hero Power is created by the card whose
				// effect makes it — CREATOR, CREATOR_DBID and SPAWN_TIME_COUNT in its FULL_ENTITY, as a card created into the hand
				IPlayable creator = source as IPlayable ?? controller.Hero;
				power = (HeroPower) Entity.FromCard(controller, _heroPowerCard, new Dictionary<GameTag, int>
				{
					[GameTag.CREATOR] = creator.Id, [GameTag.CREATOR_DBID] = creator.Card.AssetId, [(GameTag)1556] = 1, // SPAWN_TIME_COUNT
				});
			}

			if (power[GameTag.CREATOR] == 0) power[GameTag.CREATOR] = controller.Hero.Id;

			game.Log(LogLevel.INFO, BlockType.PLAY, "ReplaceHeroPower",
				!game.Logging ? "" : $"{controller.Hero} power replaced by {power}");

			// HsServer: live links the new Hero Power to the one it replaces before that one is set aside (LINKED_ENTITY;
			// the client plays the replacement from it)
			if (controller.Hero.HeroPower != null)
				power[GameTag.LINKED_ENTITY] = controller.Hero.HeroPower.Id;
			controller.SetasideZone.Add(controller.Hero.HeroPower);
			Generic.RetireFromPlay(controller.Hero.HeroPower);
			controller.Hero.HeroPower = power;
			// HsServer: a passive Hero Power works from the moment it's yours.
			if (power.ActivatedTrigger == null)
				power.Power?.Trigger?.Activate(power);

			return TaskState.COMPLETE;
		}
	}
}
