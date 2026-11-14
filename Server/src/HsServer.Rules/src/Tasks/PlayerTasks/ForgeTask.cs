// HsServer addition (not upstream SabberStone): forge a Forge card (Generic.Forge).
using SabberStoneCore.Actions;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.PlayerTasks
{
	public class ForgeTask : PlayerTask
	{
		public static ForgeTask Any(Controller controller, IPlayable card) => new ForgeTask(controller, card);

		private ForgeTask(Controller controller, IPlayable card)
		{
			PlayerTaskType = PlayerTaskType.FORGE;
			Game = controller.Game;
			Controller = controller;
			Source = card;
		}

		public override bool Process() => Generic.Forge(Controller, Source);

		public override string FullPrint() => $"ForgeTask => [{Controller.Name}] {Source}";
	}
}
