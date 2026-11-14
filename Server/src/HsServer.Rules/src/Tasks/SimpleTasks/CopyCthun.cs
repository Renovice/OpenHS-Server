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
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.SimpleTasks
{
	public class CopyCthun : SimpleTask
	{
		public override TaskState Process(in Game game, in Controller controller, in IEntity source,
			in IPlayable target,
			in TaskStack stack = null)
		{
			// HsServer (WONDERS, 2026-10-07): live since patch 27.2 has no proxy C'Thun and no ritual enchantments — the player's
			// ritual totals (CTHUN_ATTACK_BUFF / CTHUN_HEALTH_BUFF) are permanent base stats; a C'Thun entering a hand or play that
			// never followed them gets them (Generic.SyncCthun). Was: the 2016 proxy's enchantments copied.
			if (!(source is IPlayable playableSource))
				return TaskState.STOP;
			Actions.Generic.SyncCthun(playableSource);
			return TaskState.COMPLETE;
		}
	}
}
