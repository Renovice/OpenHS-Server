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
using SabberStoneCore.Enums;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.SimpleTasks
{
	public class EnqueueTask : SimpleTask
	{
		private readonly int _amount;
		private readonly bool _spellDmg;
		private readonly ISimpleTask _task;

		public EnqueueTask(int amount, ISimpleTask task, bool spellDmg = false)
		{
			_amount = amount;
			_task = task;
			_spellDmg = spellDmg;
		}

		public override TaskState Process(in Game game, in Controller controller, in IEntity source,
			in IPlayable target,
			in TaskStack stack = null)
		{
			// HsServer (2026-10-08): a split spell's hits = base + Spell Damage, then doubled by Prophet Velen (x_Damage.txt:32 "take its base
			// damage, add its controller's Spell Damage bonus … then, if its controller also controlled an unsilenced Prophet Velen …
			// double the total damage … each one still having a proposed damage of 1"; Generic.SpellDamageAmount: school and own Spell
			// Damage too). Each hit then gets neither (SPELL_DAMAGE_COUNTED 2: Generic.DamageCharFunc).
			int times = _spellDmg ? (source is Spell ? Actions.Generic.SpellDamageAmount((Spell)source, _amount) : _amount + controller.CurrentSpellPower) : _amount;
			if (_spellDmg && source is Spell spell) spell[HsTag.SPELL_DAMAGE_COUNTED] = 2;

			// HsServer: the repeats run here, inside the card's own block, in the text's order (live: Arcane Missiles'
			// hits, Prison of Yogg-Saron's four spells are all in its one POWER block). Queued as separate blocks after
			// it, the card's block ended empty and its play effect (missiles, Yogg's screen FX) stopped at once.
			// Each repeat's triggers resolve before the next (they open their own events).
			for (int i = 0; i < times; i++)
			{
				if (game.State == SabberStoneCore.Enums.State.COMPLETE) break;
				if (times > 1) TargetRoundTask.Mark(game); // each repeat names its own targets (live: one TARGET per missile)
				_task.Process(in game, in controller, in source, in target);
			}
			//Controller.game.TaskQueue.Enqueue(
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

	public class EnqueuePendingTask : SimpleTask
	{
		private readonly ISimpleTask _task;
		private readonly EntityType _targetType;

		public EnqueuePendingTask(ISimpleTask task, EntityType targetType)
		{
			_task = task;
			_targetType = targetType;
		}

		public EnqueuePendingTask(EntityType targetType)
		{
			_targetType = targetType;
		}

		public override TaskState Process(in Game game, in Controller controller, in IEntity source, in IPlayable target,
			in TaskStack stack = null)
		{
			IList<IPlayable> targets = IncludeTask.GetEntities(in _targetType, in controller, source, target, stack?.Playables);

			if (_task == null)
			{
				ISimpleTask task;
				if (target.Card.ChooseOne && target.ChooseOnePlayables != null) // (options, not only the keyword: Playable.ActivateTask)
				{
					int chooseOne = game.CurrentEventData.EventNumber;

					// HsServer: the engine rule of Playable.ActivateTask instead of a card-id list — a Choose One card with a task of
					// its own scripts its "choose both" version (Druid of the Claw's combined form...); others run both options.
					if (controller.ChooseBothCards && target.Card.Power?.PowerTask == null)
					{
						foreach (IPlayable p in targets)
						{
							game.TaskQueue.EnqueuePendingTask(target.ChooseOnePlayables[0].Card.Power.PowerTask,
								in controller, target, in p);
							game.TaskQueue.EnqueuePendingTask(target.ChooseOnePlayables[1].Card.Power.PowerTask,
								in controller, target, in p);
						}

						return TaskState.COMPLETE;
					}

					if (controller.ChooseBothCards)
						task = target.Card.Power.PowerTask; // its own "choose both" version
					else if (chooseOne > 0)
						task = target.ChooseOnePlayables[chooseOne - 1]?.Card.Power?.PowerTask;
					else
						return TaskState.STOP;
				}
				else
					task = target.Card.Power?.PowerTask;

				if (task == null) return TaskState.STOP; // HsServer: an unscripted (placeholder) card has nothing to cast
				if (target.Card.HasOverload)
					task = ComplexTask.Create(task, OverloadTask.Task);
				foreach (IPlayable p in targets)
				{
					// HsServer (user 2026-10-07): "also targets" (Zentimo, Conductivity) is targeting — a character the spell
					// couldn't target itself (enemy Stealth, Elusive, Immune) isn't one; a spell's own splash (Meteor) isn't
					// targeting and still hits it
					if (p is ICharacter extra && !target.IsValidPlayTarget(extra)) continue;
					game.TaskQueue.EnqueuePendingTask(in task, in controller, target, in p);
				}
			}
			else
				foreach (IPlayable p in targets)
					game.TaskQueue.EnqueuePendingTask(in _task, in controller, target, in p);

			return TaskState.COMPLETE;
		}
	}

	public class OverloadTask : SimpleTask
	{
		private OverloadTask() { }

		public static readonly OverloadTask Task = new OverloadTask();

		public override TaskState Process(in Game game, in Controller controller, in IEntity source,
			in IPlayable target,
			in TaskStack stack = null)
		{
			Generic.OverloadBlock(controller, (IPlayable) source, game.History);
			return TaskState.COMPLETE;
		}
	}
}
