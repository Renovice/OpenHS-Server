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
	public class DamageNumberTask : SimpleTask
	{
		public DamageNumberTask(EntityType type, bool spellDmg = false)
		{
			Type = type;
			SpellDmg = spellDmg;
		}

		public EntityType Type { get; set; }

		public bool SpellDmg { get; set; }

		/// <summary>HsServer: the effect's target deals the damage, not the card (rule book x_Damage: "Betrayal makes target
		/// minion create two Damages Events ... the source of the Damage Event is the minion, not the spell: thus bonuses
		/// from Spell Damage and Prophet Velen do not apply, while minion abilities like poison ... do apply").</summary>
		public bool TargetDeals { get; set; }

		public override TaskState Process(in Game game, in Controller controller, in IEntity source,
			in IPlayable target,
			in TaskStack stack = null)
		{
			if (stack.Number < 1) return TaskState.STOP;


			foreach (IPlayable p in IncludeTask.GetEntities(Type, in controller, source, target, stack?.Playables))
				Generic.DamageCharFunc.Invoke(TargetDeals && target != null ? target : source as IPlayable, p as ICharacter, stack.Number, SpellDmg);

			return TaskState.COMPLETE;
		}
	}
}
