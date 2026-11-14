// HsServer addition (not upstream SabberStone): the Questline mechanic.
using System.Collections.Generic;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.SimpleTasks
{
	/// <summary>
	/// A Questline stage is complete (live, captures/live-20261001-questline): its reward (the stage card's own PowerTask)
	/// resolves, the stage leaves the Quest slot (SETASIDE), and the next stage — the card named by the stage's
	/// QUEST_REWARD_DATABASE_ID — is created as a NEW entity straight into the Quest slot (CREATOR = the old stage). The last
	/// stage's QUEST_REWARD_DATABASE_ID is the final reward card instead, which goes to the hand. Then SLUSH_TIME 5000 and
	/// the old stage goes to the Graveyard. Each stage card's script says only what counts (its trigger, with a
	/// QuestProgressTask()) and its reward.
	/// </summary>
	public class QuestlineStageTask : SimpleTask
	{
		public override TaskState Process(in Game game, in Controller controller, in IEntity source, in IPlayable target,
			in TaskStack stack = null)
		{
			if (!(source is Spell stage))
				return TaskState.STOP;

			if (stage.Card.Power?.PowerTask is ISimpleTask reward)
				game.TaskQueue.Execute(reward, controller, stage, null);

			stage.ActivatedTrigger?.Remove();
			controller.SecretZone.RemoveQuest(stage);
			controller.SetasideZone.Add(stage);

			Card next = Cards.FromAssetId(stage.Card[GameTag.QUEST_REWARD_DATABASE_ID]);
			if (next != null && next.IsQuest)
			{
				var nextStage = (Spell)Entity.FromCard(controller, next, new EntityData { [GameTag.CREATOR] = stage.Id });
				controller.SecretZone.Add(nextStage);
				nextStage.IsExhausted = true;
				nextStage.Power?.Trigger?.Activate(nextStage);
			}
			else if (next != null)
				// HsServer (2026-10-08): the final reward waits for hand space like a Quest's (wiki Questline.txt:12: "If the condition … is
				// met when the owner's hand is full, the reward will not enter the player's hand until they have free hand space"; the
				// Quest Reward Step gives it: Game.QuestRewardStep)
				controller.PendingQuestRewards.Add((next, stage.Id));

			if (game.History)
				game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 5000, Info = new List<int> { stage.Id } });
			controller.SetasideZone.Remove(stage);
			controller.GraveyardZone.Add(stage);
			return TaskState.COMPLETE;
		}
	}
}
