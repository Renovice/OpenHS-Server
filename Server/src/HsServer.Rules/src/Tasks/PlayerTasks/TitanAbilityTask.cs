// HsServer addition (not upstream SabberStone): use one of a Titan's abilities (Actions/Titans.cs).
using SabberStoneCore.Actions;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.PlayerTasks
{
	/// <summary>
	/// A Titan uses one of its three abilities, instead of attacking (wiki Titan: "Each turn, Titans can use 1 of their 3
	/// abilities instead of attacking"). <see cref="PlayerTask.ChooseOne"/> is the ability's number (1..3), <see cref="Ability"/>
	/// its entity (the client's sub-option), <see cref="PlayerTask.TargetEntity"/> its target.
	/// </summary>
	public class TitanAbilityTask : PlayerTask
	{
		/// <summary>The ability's index (0..2, in the Titan's ability order).</summary>
		public int AbilityIndex { get; }

		/// <summary>The ability's entity (SETASIDE, PARENT_CARD = the Titan).</summary>
		public IPlayable Ability { get; }

		/// <summary>The ability's target (also <see cref="PlayerTask.Target"/>), or null.</summary>
		public IPlayable AbilityTarget { get; }

		public static TitanAbilityTask Any(Controller controller, Minion titan, int abilityIndex, IPlayable target = null) =>
			new TitanAbilityTask(controller, titan, abilityIndex, target);

		private TitanAbilityTask(Controller controller, Minion titan, int abilityIndex, IPlayable target)
		{
			PlayerTaskType = PlayerTaskType.TITAN_ABILITY;
			Game = controller.Game;
			Controller = controller;
			Source = titan;
			AbilityIndex = abilityIndex;
			ChooseOne = abilityIndex + 1;
			Ability = Generic.TitanAbility(titan, abilityIndex);
			AbilityTarget = target;
			Target = target as ICharacter;
		}

		public override IPlayable TargetEntity => AbilityTarget;

		public override bool Process() => Generic.UseTitanAbility(Controller, (Minion)Source, AbilityIndex, AbilityTarget);

		public override string FullPrint() =>
			$"TitanAbilityTask => [{Controller.Name}] {Source} ability {AbilityIndex + 1} {Ability}{(AbilityTarget != null ? " => " + AbilityTarget : "")}";
	}
}
