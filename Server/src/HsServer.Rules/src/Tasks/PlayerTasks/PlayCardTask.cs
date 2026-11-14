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
using System.Linq;

namespace SabberStoneCore.Tasks.PlayerTasks
{
	public class PlayCardTask : PlayerTask
	{
		public static PlayCardTask Any(Controller controller, IPlayable source, ICharacter target = null,
			int zonePosition = -1, int chooseOne = 0, bool skipPrePhase = false)
		{
			return new PlayCardTask(controller, source, target, zonePosition, chooseOne, skipPrePhase);
		}
		/// <summary>HsServer: a play on any target entity: a card in hand (Power.HandTarget) or a character.</summary>
		public static PlayCardTask Any(Controller controller, IPlayable source, IPlayable target,
			int zonePosition = -1, int chooseOne = 0, bool skipPrePhase = false)
		{
			if (target == null || target is ICharacter && target.Zone?.Type != Enums.Zone.HAND)
				return new PlayCardTask(controller, source, (ICharacter)target, zonePosition, chooseOne, skipPrePhase);
			return new PlayCardTask(controller, source, null, zonePosition, chooseOne, skipPrePhase) { HandTarget = target };
		}
		public static PlayCardTask Any(Controller controller, string cardName, ICharacter target = null,
			int zonePosition = -1, int chooseOne = 0, bool skipPrePhase = false)
		{
			return new PlayCardTask(controller, controller.HandZone.First(p => p.Card.Name == cardName), target, zonePosition, chooseOne, skipPrePhase);
		}
		public static PlayCardTask Minion(Controller controller, IPlayable source, int chooseOne = 0,
			bool skipPrePhase = false)
		{
			return new PlayCardTask(controller, source, null, -1, chooseOne, skipPrePhase);
		}
		public static PlayCardTask Minion(Controller controller, string cardName, int chooseOne = 0,
			bool skipPrePhase = false)
		{
			return new PlayCardTask(controller, controller.HandZone.First(p => p.Card.Name == cardName), null, -1, chooseOne, skipPrePhase);
		}
		public static PlayCardTask MinionTarget(Controller controller, IPlayable source, ICharacter target, int chooseOne = 0,
			bool skipPrePhase = false)
		{
			return new PlayCardTask(controller, source, target, -1, chooseOne, skipPrePhase);
		}
		public static PlayCardTask MinionTarget(Controller controller, string cardName, ICharacter target, int chooseOne = 0,
			bool skipPrePhase = false)
		{
			return new PlayCardTask(controller, controller.HandZone.First(p => p.Card.Name == cardName), target, -1, chooseOne, skipPrePhase);
		}
		public static PlayCardTask Spell(Controller controller, IPlayable source, int chooseOne = 0,
			bool skipPrePhase = false)
		{
			return new PlayCardTask(controller, source, null, -1, chooseOne, skipPrePhase);
		}
		public static PlayCardTask Spell(Controller controller, string cardName, int chooseOne = 0, bool skipPrePhase = false)
		{
			return new PlayCardTask(controller, controller.HandZone.First(p => p.Card.Name == cardName), null, -1, chooseOne, skipPrePhase);
		}
		public static PlayCardTask SpellTarget(Controller controller, IPlayable source, ICharacter target, int chooseOne = 0,
			bool skipPrePhase = false)
		{
			return new PlayCardTask(controller, source, target, -1, chooseOne, skipPrePhase);
		}
		public static PlayCardTask SpellTarget(Controller controller, string cardName, ICharacter target, int chooseOne = 0,
			bool skipPrePhase = false)
		{
			return new PlayCardTask(controller, controller.HandZone.First(p => p.Card.Name == cardName), target, -1, chooseOne, skipPrePhase);
		}
		public PlayCardTask(Controller controller, IPlayable source, ICharacter target = null, int zonePosition = -1,
			int chooseOne = 0, bool skipPrePhase = false)
		{
			PlayerTaskType = PlayerTaskType.PLAY_CARD;
			Game = controller.Game;
			Controller = controller;
			Source = source;
			Target = target;
			ZonePosition = zonePosition;
			ChooseOne = chooseOne;
			SkipPrePhase = skipPrePhase;
		}

		public int ZonePosition { get; set; }

		/// <summary>HsServer (VIOLET_HOLD): Free placement — the minion is played on the opponent's side (client option keyword DISGUISED
		/// 4690); ZonePosition is then a slot of the opponent's board.</summary>
		public bool Disguised { get; set; }

		/// <summary>HsServer: the card in hand this play targets (Power.HandTarget), else null.</summary>
		public IPlayable HandTarget { get; private set; }

		public override IPlayable TargetEntity => HandTarget ?? Target;

		public PlayCardTask Copy()
		{
			return new PlayCardTask(Controller, Source, Target, ZonePosition, ChooseOne) { HandTarget = HandTarget };
		}

		public override bool Process()
		{
			if (Disguised) Source[Enums.HsTag.PLAYED_DISGUISED] = 1;
			bool success = Generic.PlayCard(Controller, Source, TargetEntity, ZonePosition, ChooseOne, SkipPrePhase);
			if (Disguised && !success && Source.Zone?.Type == Enums.Zone.HAND) Source[Enums.HsTag.PLAYED_DISGUISED] = 0;

			return success;
		}

		public override string FullPrint()
		{
			return $"PlayCardTask => [{Controller.Name}] play {Source}({Source.Card.Type})" +
				   $"{(TargetEntity != null ? $" {TargetEntity}" : "")}{(ZonePosition > -1 ? $" to Pos[{ZonePosition}]" : "")}" +
				   $"{(ChooseOne != 0 ? $" Option {ChooseOne}" : "")}";
		}
	}
}
