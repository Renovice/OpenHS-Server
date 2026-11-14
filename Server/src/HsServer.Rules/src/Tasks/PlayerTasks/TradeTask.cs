// HsServer addition (not upstream SabberStone): trade a Tradeable card from hand.
using SabberStoneCore.Actions;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.PlayerTasks
{
	public class TradeTask : PlayerTask
	{
		public static TradeTask Any(Controller controller, IPlayable card) => new TradeTask(controller, card);

		private TradeTask(Controller controller, IPlayable card)
		{
			PlayerTaskType = PlayerTaskType.TRADE;
			Game = controller.Game;
			Controller = controller;
			Source = card;
		}

		public override bool Process() => Generic.Trade(Controller, Source);

		public override string FullPrint() => $"TradeTask => [{Controller.Name}] {Source}";
	}
}
