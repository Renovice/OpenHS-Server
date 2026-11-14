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
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.SimpleTasks
{
	public class DestroyTask : SimpleTask
	{
		private readonly bool _forcedDeathPhase;
		private readonly EntityType _type;

		public DestroyTask(EntityType entityType, bool forcedDeathPhase = false)
		{
			_type = entityType;
			_forcedDeathPhase = forcedDeathPhase;
		}

		public override TaskState Process(in Game game, in Controller controller, in IEntity source,
			in IPlayable target,
			in TaskStack stack = null)
		{
			// HsServer: a destroy works in every zone (Generic.Destroy: rulebook "Destroy effects in all zones")
			var entities = new System.Collections.Generic.List<IPlayable>(IncludeTask.GetEntities(_type, in controller, source, target, stack?.Playables));
			foreach (IPlayable p in entities)
				Actions.Generic.Destroy(p, source, entities.Count > 1);

			if (_forcedDeathPhase) game.ForcedDeathPhase(); // HsServer: rules A9

			return TaskState.COMPLETE;
		}
	}
}
