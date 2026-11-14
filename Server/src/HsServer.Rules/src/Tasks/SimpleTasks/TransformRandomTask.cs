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
using System.Collections.Generic;
using SabberStoneCore.Actions;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.SimpleTasks
{
	public class TransformMinionTask : SimpleTask
	{
		public TransformMinionTask(EntityType type, int costChange)
		{
			Type = type;
			CostChange = costChange;
		}

		public EntityType Type { get; set; }
		public int CostChange { get; set; }

		public override TaskState Process(in Game game, in Controller controller, in IEntity source,
			in IPlayable target,
			in TaskStack stack = null)
		{
			//System.Collections.Generic.IEnumerable<Card> cards = game.FormatType == FormatType.FT_STANDARD ? Cards.AllStandard : Cards.AllWild;
			// HsServer: the wiki's cost-based transform rules (10 cap, 11+ stays, 0 floor, the board pool): Cards.EvolvePick
			foreach (IPlayable p in IncludeTask.GetEntities(Type, in controller, source, target, stack?.Playables))
				if (Cards.EvolvePick(game.FormatType, p, CostChange, game.Random, controller) is Card pick)
					Generic.ChangeEntityBlock(controller, p, pick, true);

			game.OnRandomHappened(true);

			return TaskState.COMPLETE;
		}
	}
}
