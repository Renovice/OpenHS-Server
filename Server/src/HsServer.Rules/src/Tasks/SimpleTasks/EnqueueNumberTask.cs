using SabberStoneCore.Model;
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
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.SimpleTasks
{
	public class EnqueueNumberTask : SimpleTask
	{
		private readonly bool _spellDmg;
		private readonly ISimpleTask _task;

		public EnqueueNumberTask(ISimpleTask task, bool spellDmg = false)
		{
			_task = task;
			_spellDmg = spellDmg;
		}

		public override TaskState Process(in Game game, in Controller controller, in IEntity source,
			in IPlayable target,
			in TaskStack stack = null)
		{
			// HsServer: "do it N times" with N = 0 does nothing and the rest of the effect still runs (STOP ended the whole chain:
			// Poison Seeds summoned no Treants for the opponent when its caster had no minions; Genzo skipped the opponent's draw)
			if (stack.Number < 1) return TaskState.COMPLETE;

			// HsServer (2026-10-08): a split spell's hits = base + Spell Damage, then doubled by Prophet Velen (x_Damage.txt:32 "take its base
			// damage, add its controller's Spell Damage bonus … then, if its controller also controlled an unsilenced Prophet Velen …
			// double the total damage … each one still having a proposed damage of 1"; Generic.SpellDamageAmount: school and own Spell
			// Damage too). Each hit then gets neither (SPELL_DAMAGE_COUNTED 2: Generic.DamageCharFunc).
			int times = _spellDmg ? (source is Spell ? Actions.Generic.SpellDamageAmount((Spell)source, stack.Number) : stack.Number + controller.CurrentSpellPower) : stack.Number;
			if (_spellDmg && source is Spell spell) spell[HsTag.SPELL_DAMAGE_COUNTED] = 2;

			for (int i = 0; i < times; i++)
			{
				// HsServer: each repeat names its own targets (live: one TARGET per missile)
				if (times > 1) game.TaskQueue.Enqueue(TargetRoundTask.Instance, in controller, in source, in target);
				game.TaskQueue.Enqueue(in _task, in controller, in source, in target);
			}
			//Controller.game.TaskQueue.EnqueueBase(
			//	new ClearStackTask
			//	{
			//		Game = controller.Game,
			//		Controller = in controller,
			//		source = source as IPlayable,
			//		target = target as IPlayable
			//	});

			return TaskState.COMPLETE;
		}
	}
}
