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
using SabberStoneCore.Actions;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.SimpleTasks
{
	public class AttackTask : SimpleTask
	{
		private readonly EntityType _aType;
		private readonly EntityType _dType;

		public AttackTask(EntityType attacker, EntityType defender)
		{
			_aType = attacker;
			_dType = defender;
		}

		public override TaskState Process(in Game game, in Controller controller, in IEntity source,
			in IPlayable target,
			in TaskStack stack = null)
		{
			// HsServer: no attacker or no defender (nothing to pick, it died meanwhile) means no attack.
			var attackers = IncludeTask.GetEntities(_aType, in controller, source, target, stack?.Playables);
			var defenders = IncludeTask.GetEntities(_dType, in controller, source, target, stack?.Playables);
			if (attackers.Count == 0 || defenders.Count == 0 ||
			    !(attackers[0] is ICharacter attacker) || !(defenders[0] is ICharacter defender))
				return TaskState.STOP;

			if (defender.Card.Untouchable) return TaskState.STOP;

			EventMetaData temp = game.CurrentEventData;
			Generic.AttackBlock.Invoke(attacker.Controller, attacker, defender, true, false);
			game.CurrentEventData = temp;

			return TaskState.COMPLETE;
		}
	}
}
