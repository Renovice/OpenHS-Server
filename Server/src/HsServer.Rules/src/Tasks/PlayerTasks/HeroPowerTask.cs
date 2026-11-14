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
using SabberStoneCore.Actions;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.PlayerTasks
{
	public class HeroPowerTask : PlayerTask
	{
		public static HeroPowerTask Any(Controller controller, ICharacter target = null, int chooseOne = 0,
			bool skipPrePhase = false, HeroPower power = null)
		{
			// HsServer (VIOLET_HOLD): a second Hero Power's use names it (its Source)
			return new HeroPowerTask(controller, target, chooseOne, skipPrePhase) { Power = power, Source = power };
		}

		/// <summary>HsServer (VIOLET_HOLD): the second Hero Power used (Thal'ena's), else null = the player's Hero Power.</summary>
		public HeroPower Power { get; private set; }

		private HeroPowerTask(Controller controller, ICharacter target, int chooseOne, bool skipPrePhase)
		{
			PlayerTaskType = PlayerTaskType.HERO_POWER;
			Game = controller.Game;
			Controller = controller;
			Target = target;
			ChooseOne = chooseOne;
			SkipPrePhase = skipPrePhase;
		}

		public override bool Process()
		{
			bool success = Generic.HeroPower(Controller, Target, ChooseOne, SkipPrePhase, Power);
			return success;
		}

		public override string FullPrint()
		{
			return $"HeroPowerTask => [{Controller.Name}] using {Controller.Hero.HeroPower}" +
				   $"{(Target != null ? $" attack {Target}" : "")}";
		}
	}
}
