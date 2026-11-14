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
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.SimpleTasks
{
	public class DamageTask : SimpleTask
	{
		public DamageTask(int amount, int randAmount, EntityType entityType, bool spellDmg = false)
		{
			Amount = amount;
			RandAmount = randAmount;
			Type = entityType;
			SpellDmg = spellDmg;
		}

		public DamageTask(int amount, EntityType entityType, bool spellDmg = false)
		{
			Amount = amount;
			RandAmount = 0;
			Type = entityType;
			SpellDmg = spellDmg;
		}

		public int Amount { get; set; }

		public int RandAmount { get; set; }

		public EntityType Type { get; set; }

		public bool SpellDmg { get; set; }

		/// <summary>HsServer: rules DH3 — each hit resolves (its triggers act) before the next one, instead of all hits first
		/// (Dark Iron Skulker). The targets are still fixed before any damage. Random amounts always do this.</summary>
		public bool EachAtOnce { get; set; }

		public override TaskState Process(in Game game, in Controller controller, in IEntity source,
			in IPlayable target,
			in TaskStack stack = null)
		{
			if (Amount < 1 && RandAmount < 1)
				return TaskState.STOP;

			IList<IPlayable> entities = IncludeTask.GetEntities(Type, in controller, source, target, stack?.Playables);

			// HsServer: rules DH1 — several targets are all hit first, then their damage triggers resolve. Random amounts
			// (Lightning Storm, Elemental Destruction) resolve each hit at once (DH3).
			bool batch = entities.Count > 1 && RandAmount == 0 && !EachAtOnce && game.TaskQueue.StartBatch();
			try
			{
				DamageEach(game, source, entities);
			}
			catch
			{
				if (batch)
					game.TaskQueue.EndBatch();
				throw;
			}
			if (batch)
			{
				// the triggers of every hit, in their own event level (the running task stays at the front of its queue)
				game.TaskQueue.StartEvent();
				game.TaskQueue.EndBatch();
				game.ProcessTasks();
				game.TaskQueue.EndEvent();
			}
			return TaskState.COMPLETE;
		}

		private void DamageEach(Game game, IEntity source, IList<IPlayable> entities)
		{
			// HsServer: one step that damages a group ("1 damage to all other minions") is an area hit: no missile per
			// character (Generic.MissilePerHit)
			bool outer = game.AreaDamage;
			game.AreaDamage = outer || entities.Count > 1;
			try { DamageEachInner(game, source, entities); }
			finally { game.AreaDamage = outer; }
		}

		private void DamageEachInner(Game game, IEntity source, IList<IPlayable> entities)
		{
			for (int i = 0; i < entities.Count; i++)
			{
				// HsServer: only characters take damage — a Location on the board is in "all enemies" lists but isn't one (Explosive
				// Trap / Wrathspike Brute with a Location in play threw: soak 2026-10-08)
				if (!(entities[i] is ICharacter)) continue;
				int randAmount = 0;
				if (RandAmount > 0)
				{
					randAmount = game.Random.Next(0, RandAmount + 1);
					game.OnRandomHappened(true);
				}

				int amount = Amount + randAmount;

				game.Log(LogLevel.WARNING, BlockType.ACTION, "DamageTask",
					!game.Logging ? "" : $"Amount is {amount} damage of {source}.");

				Generic.DamageCharFunc.Invoke(source as IPlayable, entities[i] as ICharacter, amount, SpellDmg);
			}
		}
	}
}
