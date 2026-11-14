// HsServer addition (not upstream SabberStone): activate a Location on the board.
using SabberStoneCore.Actions;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.PlayerTasks
{
	public class LocationTask : PlayerTask
	{
		/// <summary>The activation's target: a character (also in <see cref="PlayerTask.Target"/>) or a card in hand.</summary>
		public IPlayable ActivationTarget { get; }

		public static LocationTask Any(Controller controller, Location location, IPlayable target = null) =>
			new LocationTask(controller, location, target);

		private LocationTask(Controller controller, Location location, IPlayable target)
		{
			PlayerTaskType = PlayerTaskType.LOCATION;
			Game = controller.Game;
			Controller = controller;
			Source = location;
			ActivationTarget = target;
			Target = target as ICharacter;
		}

		public override IPlayable TargetEntity => ActivationTarget;

		public override bool Process() => Generic.ActivateLocation(Controller, (Location)Source, ActivationTarget);

		public override string FullPrint() => $"LocationTask => [{Controller.Name}] {Source}{(ActivationTarget != null ? " => " + ActivationTarget : "")}";
	}
}
