// HsServer addition (not upstream SabberStoneCore): launch a Starship (Actions/Starship.cs).
using SabberStoneCore.Actions;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.PlayerTasks
{
	/// <summary>Launch the player's Starship (the Starship's POWER option, its sub-option the launch card GDB_905).</summary>
	public class StarshipLaunchTask : PlayerTask
	{
		public static StarshipLaunchTask Any(Controller controller, Minion starship) => new StarshipLaunchTask(controller, starship);

		private StarshipLaunchTask(Controller controller, Minion starship)
		{
			PlayerTaskType = PlayerTaskType.STARSHIP_LAUNCH;
			Game = controller.Game;
			Controller = controller;
			Source = starship;
		}

		public override bool Process() => Generic.LaunchStarship(Controller, (Minion)Source);

		public override string FullPrint() => $"StarshipLaunchTask => [{Controller.Name}] launch {Source}";
	}
}
