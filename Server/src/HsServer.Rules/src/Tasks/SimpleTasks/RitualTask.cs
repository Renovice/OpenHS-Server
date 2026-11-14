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
using SabberStoneCore.Conditions;
using System.Collections.Generic;
using SabberStoneCore.Actions;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.SimpleTasks
{
	/// <summary>
	/// "Give your C'Thun +X/+Y (wherever it is)". HsServer (WONDERS, 2026-10-07): live's ritual since patch 27.2 — the player's
	/// ritual totals and permanent base stats on every C'Thun, no proxy C'Thun and no enchantments (Actions.Generic.CthunRitual;
	/// wiki C'Thun: "Cards that buff C'Thun now permanently increment its attack and health, instead of granting an
	/// enchantment"). Was: SabberStone's 2016 proxy OG_279 with OG_281e / OG_284e / OG_282e enchantments.
	/// </summary>
	public class RitualTask : SimpleTask
	{
		public enum RitualType
		{
			/// <summary>Only a check follows ("If your C'Thun has at least 10 Attack"): the ideal C'Thun is the player's ritual
			/// totals (SelfCondition.IsCthunGameTag), nothing to do.</summary>
			Check,
			Buff,
			/// <summary>"Give your C'Thun Taunt" (with any +X/+Y given).</summary>
			Taunt,
			/// <summary>Blade of C'Thun: the stack's numbers (the destroyed minion's Attack and Health).</summary>
			Blade
		}

		private readonly int _atk, _health;
		private readonly bool _taunt;
		private readonly RitualType _type;

		public RitualTask(RitualType type = RitualType.Check)
		{
			_type = type;
			_taunt = type == RitualType.Taunt;
		}

		public RitualTask(int amount) : this(amount, amount) { }

		/// <summary>HsServer (WONDERS): +<paramref name="atk"/>/+<paramref name="health"/>, and Taunt (Twilight Geomancer: "+1/+1 and
		/// Taunt").</summary>
		public RitualTask(int atk, int health, bool taunt = false)
		{
			_type = taunt ? RitualType.Taunt : RitualType.Buff;
			_atk = atk;
			_health = health;
			_taunt = taunt;
		}

		public override TaskState Process(in Game game, in Controller controller, in IEntity source,
			in IPlayable target,
			in TaskStack stack = null)
		{
			switch (_type)
			{
				case RitualType.Check:
					break;
				case RitualType.Blade:
					Generic.CthunRitual(controller, source, stack?.Number ?? 0, stack?.Number1 ?? 0);
					break;
				default:
					Generic.CthunRitual(controller, source, _atk, _health, _taunt);
					break;
			}
			return TaskState.COMPLETE;
		}
	}
}
