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
using System;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.SimpleTasks
{
	public enum SummonSide
	{
		// summoning on the last position on the right side
		DEFAULT,

		// summoning left of the minion
		LEFT,

		// summoning right of the minion
		RIGHT,

		// summoning at the last position of the source
		DEATHRATTLE,

		// summoning at a given position in the stack number
		NUMBER,

		// summoning by spell, currently like default
		SPELL,

		// summoning on the right side and left side alternately.
		ALTERNATE
	}

	public class SummonTask : SimpleTask
	{
		private readonly bool _addToStack;
		private readonly int _amount = 1;

		public SummonTask(SummonSide side = SummonSide.DEFAULT, Card card = null, bool removeFromStack = false,
			bool addToStack = false, int amount = 1)
		{
			Card = card;
			RemoveFromStack = removeFromStack;
			Side = side;
			_addToStack = addToStack;
			_amount = amount;
		}

		public SummonTask(string cardId, SummonSide side = SummonSide.DEFAULT, bool addToStack = false)
		{
			Card = Cards.FromId(cardId);
			RemoveFromStack = false;
			Side = side;
			_addToStack = addToStack;
		}

		public SummonTask(string cardId, int amount, SummonSide side = SummonSide.DEFAULT)
		{
			Card = Cards.FromId(cardId);
			_amount = amount;
			Side = side;
		}

		public Card Card { get; set; }

		public bool RemoveFromStack { get; set; }

		public SummonSide Side { get; set; }

		public override TaskState Process(in Game game, in Controller controller, in IEntity source,
			in IPlayable target,
			in TaskStack stack = null)
		{
			if (Card == null && stack?.Playables.Count == 0)
				return TaskState.COMPLETE;

			int alternateCount = 0;
			// HsServer: several at once are a group (Generic.SummonPosition places them: the card's text count, else this amount)
			if (_amount > 1) Generic.BeginSummonGroup(game, source, _amount);

			for (int i = 0; i < _amount; ++i)
			{
				if (controller.BoardZone.IsFull)
				{
					if (Card == null && stack?.Playables.Count > 0)
					{
						IPlayable m = stack.Playables[0];
						if (m.Zone == null)
							m.Controller.GraveyardZone.Add(stack.Playables[0]);
					}

					return TaskState.STOP;
				}

				Minion summonEntity = null;
				if (Card != null)
				{
					summonEntity = Entity.FromCard(controller, Card,
						new EntityData
						{
							{GameTag.ZONE, (int) Zone.PLAY},
							{GameTag.CREATOR, source.Id} // HsServer: live summon tokens carry CREATOR (the client lays them out by it)
						}) as Minion;
					if (_addToStack)
						stack.AddPlayable(summonEntity);
				}
				bool forcePlayed = false;
				if (Card == null && stack?.Playables.Count > 0)
				{
					summonEntity = stack.Playables[0] as Minion;
					// HsServer: a minion put into play from the hand or deck ("Force Play": Meat Wagon, Giant Anaconda, Voidcaller)
					// lands on the far right (Advanced rulebook, Deathrattle spawn exceptions).
					forcePlayed = summonEntity != null && (summonEntity[GameTag.ZONE] == (int)Zone.HAND || summonEntity[GameTag.ZONE] == (int)Zone.DECK); // the tag: RemoveFromDeck may have taken it out already
					if (RemoveFromStack) stack.Playables.Remove(summonEntity);
					// HsServer: a minion summoned from the hand or deck (Voidcaller) leaves that zone first, or it stays listed there.
					if (summonEntity?.Zone != null && summonEntity.Zone.Type != Zone.PLAY)
						summonEntity.Zone.Remove(summonEntity);
				}

				if (summonEntity == null)
				{
					if (game.Logging)
					{
						object cause = Card != null ? (object)Card : stack?.Playables.Count > 0 ? stack.Playables[0] : null;
						game.Log(LogLevel.WARNING, BlockType.POWER, "SummonTask",
							$"Cannot summon {cause}. Please Check the implementation of {source}.");
					}

					return TaskState.STOP;
				}

				// HsServer: the default, "right of it", Deathrattle and alternating sides follow Blizzard's placement
				// (Generic.SummonPosition: one effect's several summons by the card's text count — both sides, each directly right, a
				// resurrect's line; 2026-10-10: RIGHT and ALTERNATE were scripts' own spreads, and Keyboard Soloist's RIGHT kept both
				// Amps on its right). Only a slot the card itself names wins: Alarm-o-Bot's swap into its own slot (NUMBER), LEFT.
				bool contextSide = Side == SummonSide.DEFAULT || Side == SummonSide.RIGHT || Side == SummonSide.DEATHRATTLE ||
				                   Side == SummonSide.ALTERNATE;
				int summonPosition = contextSide
					// Force Play: like any summon of a living summoner, far right for a Deathrattle
					? Generic.SummonPosition(game, source, controller, forcePlayed)
					: GetPosition(in source, Side, stack?.Number ?? 0, ref alternateCount);

				if (summonPosition > controller.BoardZone.Count)
					summonPosition = controller.BoardZone.Count;

				if (summonEntity.Untouchable)
					controller.BoardZone.Add(summonEntity, summonPosition);
				else
					Generic.SummonBlock.Invoke(game, summonEntity, summonPosition, source);
			}

			return TaskState.COMPLETE;
		}

		public static int GetPosition(in IEntity source, in SummonSide side, in int number, ref int alternateCount)
		{
			int summonPosition;

			// HsServer: a source that isn't a minion (a weapon's Battlecry, a spell) has no board position to summon beside.
			if ((side == SummonSide.LEFT || side == SummonSide.RIGHT || side == SummonSide.ALTERNATE) && !(source is Minion { Zone: { } }))
				return -1;

			switch (side)
			{
				case SummonSide.SPELL:
				case SummonSide.DEFAULT:
					return -1;
				case SummonSide.LEFT:
					if (source.Zone.Type == Zone.PLAY)
						summonPosition = ((Minion) source).ZonePosition;
					else
						summonPosition = ((Minion) source).LastBoardPosition;
					break;
				case SummonSide.RIGHT:
					if (source.Zone.Type == Zone.PLAY)
						summonPosition = ((Minion) source).ZonePosition + 1;
					else
						summonPosition = ((Minion) source).LastBoardPosition;
					break;
				case SummonSide.DEATHRATTLE:
					if (source is Minion m)
						summonPosition = m.LastBoardPosition;
					else if
						(source is Enchantment e)
						summonPosition = ((Minion)e.Target).LastBoardPosition;
					else goto default;
					break;
				case SummonSide.NUMBER:
					summonPosition = number - 1;
					break;
				case SummonSide.ALTERNATE:
					if (alternateCount % 2 == 0)
						summonPosition = ((Minion) source).ZonePosition - alternateCount / 2;
					else
						summonPosition = ((Minion) source).ZonePosition + alternateCount / 2 + 1;
					alternateCount++;
					break;
				default:
					throw new NotImplementedException();
			}

			if (summonPosition > source.Controller.BoardZone.Count)
				summonPosition = source.Controller.BoardZone.Count;

			return summonPosition;
		}
	}

	public class SummonNumberTask : SimpleTask
	{
		private readonly Card _card;
		private readonly bool _op;

		public SummonNumberTask(string cardId, bool opponent)
		{
			_card = Cards.FromId(cardId);
			_op = opponent;
		}


		public override TaskState Process(in Game game, in Controller controller, in IEntity source, in IPlayable target,
			in TaskStack stack = null)
		{
			if (_op)
				return new SummonOpTask(_card, stack.Number)
					.Process(in game, in controller, in source, in target, in stack);

			return new SummonTask(card: _card, amount: stack.Number)
				.Process(in game, in controller, in source, in target, in stack);
		}
	}
}
