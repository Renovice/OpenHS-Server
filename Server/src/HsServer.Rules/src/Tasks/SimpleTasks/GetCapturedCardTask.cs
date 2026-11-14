using System;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.SimpleTasks
{
	/// <summary>
	/// Creates new entity instance from the captured Card of an Enchantment.
	/// </summary>
	public class GetCapturedCardTask : SimpleTask
	{
		private GetCapturedCardTask() { }

		/// <summary>
		/// Gets a singleton of this task.
		/// </summary>
		public static GetCapturedCardTask Task { get; } = new GetCapturedCardTask();

		public override TaskState Process(in Game game, in Controller controller, in IEntity source,
			in IPlayable target,
			in TaskStack stack = null)
		{
			if (stack == null)
				throw new Exception($"{this} should contained in StateTaskList");

			if (!(target is Enchantment e))
				throw new Exception($"Target of {this} is not Enchantment.");

			Card c = e.CapturedCard;

			// HsServer (livecheck 2026-10-10): nothing captured (an enchantment made or copied without its card) does nothing, as an
			// unscripted card (a shared task survives it) instead of stopping the game
			if (c == null)
			{
				game.Log(LogLevel.WARNING, Enums.BlockType.TRIGGER, ToString(), $"{e} does not have any captured Card.");
				if (stack != null) stack.Playables = new IPlayable[0];
				return TaskState.STOP;
			}

			IPlayable entity = Entity.FromCard(in controller, in c);

			stack.Playables = new[] {entity};

			return TaskState.COMPLETE;
		}
	}

	/// <summary>
	/// Activates the captured card's Deathrattle Task.
	/// </summary>
	public class ActivateCapturedDeathrattleTask : SimpleTask
	{
		private ActivateCapturedDeathrattleTask() { }

		/// <summary>
		/// Gets a singleton of this task.
		/// </summary>
		public static ActivateCapturedDeathrattleTask Task { get; } = new ActivateCapturedDeathrattleTask();

		public override TaskState Process(in Game game, in Controller controller, in IEntity source,
			in IPlayable target,
			in TaskStack stack = null)
		{
			if (!(target is Enchantment e))
				throw new Exception($"Target of {this} is not Enchantment.");

			Card c = e.CapturedCard;

			// HsServer (livecheck 2026-10-10): nothing captured (an enchantment made or copied without its card) does nothing, as an
			// unscripted card (a shared task survives it) instead of stopping the game
			if (c == null)
			{
				game.Log(LogLevel.WARNING, Enums.BlockType.TRIGGER, ToString(), $"{e} does not have any captured Card.");
				if (stack != null) stack.Playables = new IPlayable[0];
				return TaskState.STOP;
			}

			ISimpleTask task = c.Power?.DeathrattleTask; // HsServer: an unscripted (placeholder) card has no Power
			// HsServer (2026-10-08): its Deathrattle block names the card it came from (live EffectCardId: Unearthed Raptor's copy of
			// Deathcharger, LOE_019 TRIGGER DEATHRATTLE EffectCardId=FP1_006 — 19 live blocks)
			if (game.History && game.PowerHistory.InnermostBlock is Kettle.PowerHistoryBlockStart open && open.TriggerKeyword == (int)Enums.GameTag.DEATHRATTLE &&
			    string.IsNullOrEmpty(open.EffectCardId))
				open.EffectCardId = c.Id;

			if (task == null)
			{ 
				game.Log(LogLevel.WARNING, Enums.BlockType.TRIGGER, ToString(),
					$"Captured card {c} doesn't have Deathrattle Task.");
				return TaskState.STOP;
			}

			// HsServer (2026-10-07): an effect that picks from its card's Entourage reads the captured card's (Game.CapturedEffectCard;
			// soak 820313: a Vectus whelp with Arfus' Deathrattle threw in RandomEntourageTask)
			if (c.Entourage?.Length > 0)
				task = ComplexTask.Create(
					new CustomTask((g, ctl, s, t, st) => g.CapturedEffectCard = c),
					task,
					new CustomTask((g, ctl, s, t, st) => g.CapturedEffectCard = null));

			game.TaskQueue.Enqueue(in task, in controller, in source, null);

			return TaskState.COMPLETE;
		}
	}
}
