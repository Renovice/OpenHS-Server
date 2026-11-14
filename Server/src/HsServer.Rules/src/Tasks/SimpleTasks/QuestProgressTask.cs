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
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.SimpleTasks
{
	public class QuestProgressTask : SimpleTask
	{
		//private readonly string _questRewardId;
		private readonly Card _card;

		public QuestProgressTask(string questRewardId)
		{
			_card = Cards.FromId(questRewardId);
		}

		/// <summary>HsServer: a Questline stage's progress — completing it runs QuestlineStageTask (live: a TRIGGER block with
		/// TriggerKeyword QUESTLINE, right after the progress, in the same play).</summary>
		public QuestProgressTask()
		{
		}

		/// <summary>HsServer: a Questline stage advances by <paramref name="amount"/> (damage taken, Attack gained, ...);
		/// reaching its total completes it (QuestlineStageTask).</summary>
		public static void Advance(Game game, Controller controller, Spell stage, int amount)
		{
			if (amount <= 0 || stage.QuestProgress >= stage.QuestTotalProgress || !controller.SecretZone.IsActiveQuest(stage))
				return;
			stage.QuestProgress = System.Math.Min(stage.QuestTotalProgress, stage.QuestProgress + amount);
			if (stage.QuestProgress == stage.QuestTotalProgress)
				game.TaskQueue.Enqueue(new QuestlineStageTask(), controller, stage, null, (int)HsTag.QUESTLINE);
		}

		/// <summary>HsServer: count <paramref name="key"/> once per stage ("different 1-Cost cards", "a Fire, Frost and Arcane
		/// spell"); true the first time.</summary>
		public static bool CountOnce(Spell stage, int key)
		{
			GameTag mark = (GameTag)((int)HsTag.QUEST_COUNTED + key);
			if (stage[mark] > 0) return false;
			stage[mark] = 1;
			return true;
		}

		public override TaskState Process(in Game game, in Controller controller, in IEntity source,
			in IPlayable target,
			in TaskStack stack = null)
		{
			var spell = source as Spell;
			if (spell == null)
				return TaskState.STOP;

			spell.QuestProgress++;
			game.Log(LogLevel.INFO, BlockType.PLAY, "QuestProgressTask",
				!game.Logging
					? ""
					: $"{controller} {spell}'s Quest {spell.QuestProgress} / {spell.QuestTotalProgress} progress!");

			if (spell.QuestProgress == spell.QuestTotalProgress && _card == null && spell.Card[HsTag.QUESTLINE] > 0)
			{
				game.TaskQueue.Enqueue(new QuestlineStageTask(), controller, spell, null, (int)HsTag.QUESTLINE);
				return TaskState.COMPLETE;
			}

			if (spell.QuestProgress == spell.QuestTotalProgress)
			{
				game.Log(LogLevel.INFO, BlockType.PLAY, "QuestProgressTask",
					!game.Logging ? "" : $"{controller} Quest finished, reward {_card}!");

				// HsServer: the reward comes at the Quest Reward Step (Generic.CompleteQuest)
				Generic.CompleteQuest(controller, spell, _card);

				return TaskState.COMPLETE;
			}

			return TaskState.COMPLETE;
		}
	}
}
