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

using SabberStoneCore.Auras;
using SabberStoneCore.Enums;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Model.Zones
{
	/// <summary>
	/// Zone for all entities which are held 'in hand'.
	/// </summary>
	public class HandZone : PositioningZone<IPlayable>
	{
		public HandZone(Controller controller) : base(Zone.HAND, Controller.LargestHandSize) // HsServer: room for a raised limit (Valdris Felgorge)
		{
			Game = controller.Game;
			Controller = controller;
		}

		private HandZone(Controller c, HandZone zone) : base(c, zone)
		{
		}

		public override bool IsFull => _count >= Controller[GameTag.MAXHANDSIZE]; // HsServer: the player's limit (MAXHANDSIZE, 10 unless raised)

		public override int FreeSpace => System.Math.Max(0, Controller[GameTag.MAXHANDSIZE] - _count); // HsServer: same limit

		public override void Add(IPlayable entity, int zonePosition = -1)
		{
			base.Add(entity, zonePosition);

			 if (MultiAura.CostEffect(entity.Power?.Aura) is AdaptiveCostEffect e) // HsServer: also inside a MultiAura
				e.Activate((Playable)entity);
			entity.Power?.Trigger?.Activate(entity, TriggerActivation.HAND);

			Game.TriggerManager.OnZoneTrigger(entity);
		}

		public override IPlayable Remove(IPlayable entity)
		{
			((Playable)entity).ResetCost();
			entity.AppliedEnchantments?.ForEach(p => p.ActivatedTrigger?.Remove());
			return base.Remove(entity);
		}

		public HandZone Clone(Controller c)
		{
			return new HandZone(c, this);
		}
	}
}
