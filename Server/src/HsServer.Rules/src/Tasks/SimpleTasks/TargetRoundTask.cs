using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.SimpleTasks
{
	/// <summary>
	/// HsServer: the next repeat of a repeated effect starts (EnqueueTask, EnqueueNumberTask, ComplexTask.Repeat): it gets
	/// its own META_DATA TARGET, as live (Kettle.PowerHistory.NewTargetRound). Changes nothing in the game.
	/// </summary>
	public class TargetRoundTask : SimpleTask
	{
		public static readonly TargetRoundTask Instance = new TargetRoundTask();

		public static void Mark(Game game)
		{
			if (game.History) game.PowerHistory.NewTargetRound();
		}

		public override TaskState Process(in Game game, in Controller controller, in IEntity source, in IPlayable target,
			in TaskStack stack = null)
		{
			Mark(game);
			return TaskState.COMPLETE;
		}
	}
}
