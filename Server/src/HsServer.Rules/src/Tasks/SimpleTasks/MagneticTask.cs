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
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Triggers;

namespace SabberStoneCore.Tasks.SimpleTasks
{
	/// <summary>
	/// HsServer (TITANS): a Magnetic minion just played magnetizes to the minion on its right when it can (Generic.MagnetizeHost:
	/// a Mech, or its MAGNETIC_TO_RACE type) — the engine runs this for every card with its own Magnetic (PlayCard's
	/// PlayMinion), so a script needs no MagneticTask; one that still has it does nothing more (the minion has already left
	/// play). The enchantment is the client's (tag 2787) and the "Whenever you Magnetize" event follows (Generic.Magnetize).
	/// </summary>
	public class MagneticTask : SimpleTask
	{
		public override TaskState Process(in Game game, in Controller controller, in IEntity source,
			in IPlayable target,
			in TaskStack stack = null)
		{
			if (!(source is Minion s) || !s.Card.Modular)
			{
				game.Log(LogLevel.ERROR, BlockType.POWER, "Magnetic", $"{source}'s not a Magnetic Minion");
				return TaskState.STOP;
			}

			Minion host = Generic.MagnetizeHost(s);
			if (host == null)
				return TaskState.STOP;

			Generic.Magnetize(game, s, host);
			return TaskState.COMPLETE;
		}
	}
}
