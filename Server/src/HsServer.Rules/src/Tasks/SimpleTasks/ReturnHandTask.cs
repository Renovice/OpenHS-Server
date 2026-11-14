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
	public class ReturnHandTask : SimpleTask
	{
		public ReturnHandTask(EntityType type, bool addToStack = false)
		{
			Type = type;
			_addToStack = addToStack;
		}

		/// <summary>HsServer: put the cards now in hand on the stack (a dead minion comes back as a new card, so later
		/// tasks must act on that card, not on the dead one).</summary>
		private readonly bool _addToStack;

		public EntityType Type { get; set; }

		public override TaskState Process(in Game game, in Controller controller, in IEntity source,
			in IPlayable target,
			in TaskStack stack = null)
		{
			var returned = new System.Collections.Generic.List<IPlayable>();
			foreach (IPlayable p in IncludeTask.GetEntities(Type, in controller, source, target, stack?.Playables))
			{
				if (!(p is Minion minion))
					break;
				Generic.NoteAffected(minion, source); // HsServer: live, before the move (4741, LAST_AFFECTED_BY)
				IPlayable inHand = Generic.ReturnToHand(minion.Controller, minion);
				if (inHand != null) returned.Add(inHand);
			}
			if (_addToStack && stack != null)
				stack.Playables = returned;

			return TaskState.COMPLETE;
		}
	}
}
