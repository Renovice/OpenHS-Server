// HsServer addition (not upstream SabberStoneCore): use an interactable object (LOST_CITY: the Underfel Rift — Actions/LostCity.cs).
using SabberStoneCore.Actions;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.PlayerTasks
{
	/// <summary>Use an interactable object on the board with a hand card (client INTERACTABLE_OBJECT: the object's POWER option,
	/// keyword 4089, the hand card its target — OPTION_REVERSE_TARGET).</summary>
	public class InteractTask : PlayerTask
	{
		public static InteractTask Any(Controller controller, Minion obj, IPlayable card) => new InteractTask(controller, obj, card);

		private readonly IPlayable _card;

		private InteractTask(Controller controller, Minion obj, IPlayable card)
		{
			PlayerTaskType = PlayerTaskType.INTERACT;
			Game = controller.Game;
			Controller = controller;
			Source = obj;
			_card = card;
		}

		/// <summary>The hand card thrown in.</summary>
		public override IPlayable TargetEntity => _card;

		public override bool Process() => Generic.UseInteractable(Controller, (Minion)Source, _card);

		public override string FullPrint() => $"InteractTask => [{Controller.Name}] {Source} with {_card}";
	}
}
