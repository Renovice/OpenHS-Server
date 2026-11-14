// HsServer addition (not upstream SabberStone): Prepare a Prepare card from hand (Actions/Prepare.cs).
using SabberStoneCore.Actions;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.PlayerTasks
{
	public class PrepareTask : PlayerTask
	{
		public static PrepareTask Any(Controller controller, IPlayable card) => new PrepareTask(controller, card);

		private PrepareTask(Controller controller, IPlayable card)
		{
			PlayerTaskType = PlayerTaskType.PREPARE;
			Game = controller.Game;
			Controller = controller;
			Source = card;
		}

		public override bool Process() => Generic.Prepare(Controller, Source);

		public override string FullPrint() => $"PrepareTask => [{Controller.Name}] {Source}";
	}
}
