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
using System.Collections.Generic;
using SabberStoneCore.Actions;
using SabberStoneCore.Auras;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Model.Zones;

namespace SabberStoneCore.Tasks.SimpleTasks
{
	public class TransformCopyTask : SimpleTask
	{
		private readonly bool _addToStack;

		public TransformCopyTask(bool addToStack = false)
		{
			_addToStack = addToStack;
		}

		public override TaskState Process(in Game game, in Controller controller, in IEntity source,
			in IPlayable target,
			in TaskStack stack = null)
		{
			var minionTarget = (Minion) target;
			if (minionTarget == null)
				return TaskState.STOP;

			var minionSource = (Minion) source;
			if (minionSource.Zone?.Type != Zone.PLAY)
				return TaskState.STOP;


			//if (game.History)
			//	tags.Add(GameTag.PREMIUM, minionTarget[GameTag.PREMIUM]);

			//var copy = (Minion) Entity.FromCard(in controller, minionTarget.Card, tags);

			minionSource = (Minion) Generic.ChangeEntityBlock(controller, minionSource, minionTarget.Card, true);

			// HsServer: the transform leaves a fresh minion (rules E9); the copy then takes the target's state (E10) —
			// Frozen, gained keywords, damage — each told to the client. Its own place, owner, history and "just
			// played" state stay its own.
			foreach (KeyValuePair<GameTag, int> kv in minionTarget._data)
				switch (kv.Key)
				{
					case GameTag.ENTITY_ID:
					case GameTag.CONTROLLER:
					case GameTag.ZONE:
					case GameTag.ZONE_POSITION:
					case GameTag.CREATOR:
					case GameTag.CREATOR_DBID:
					case GameTag.PREMIUM:
					case GameTag.EXHAUSTED:
					case GameTag.NUM_TURNS_IN_PLAY:
					case (GameTag)273:  // NUM_TURNS_IN_HAND
					case (GameTag)1556: // SPAWN_TIME_COUNT
					case (GameTag)2321: // TIMES_BEEN_TRANSFORMED
					// HsServer (TITANS): a Titan's own ability entities aren't copied (the copy has its own: ChangeEntityBlock)
					case HsTag.TITAN_ABILITY_ENTITY_1:
					case HsTag.TITAN_ABILITY_ENTITY_2:
					case HsTag.TITAN_ABILITY_ENTITY_3:
						continue;
					default:
						// Written straight into the data (no setters: copied damage mustn't kill the copy before its
						// copied buffs land); CopyInternalAttributes below copies the cached stats.
						if (game.History && Entity.InHistory(kv.Key) && minionSource[kv.Key] != kv.Value)
							game.PowerHistory.Add(Kettle.PowerHistoryBuilder.TagChange(minionSource.Id, kv.Key, kv.Value));
						minionSource._data[kv.Key] = kv.Value;
						break;
				}

			minionTarget.CopyInternalAttributes(minionSource);
			Generic.CopyTitanState(minionTarget, minionSource); // HsServer (TITANS): the copied ability state (used, their numbers) shows on its abilities

			//Trigger trigger = minionTarget.ActivatedTrigger;
			IAura aura = minionTarget.OngoingEffect;

			if (game.CurrentEventData != null && minionSource == game.CurrentEventData.EventSource) // HsServer: none at a turn's end (Replicat-o-tron)
				game.CurrentEventData.EventSource = minionSource;

			// Copy Enchantments
			if (minionTarget.AppliedEnchantments != null)
				foreach (Enchantment e in minionTarget.AppliedEnchantments)
				{
					Enchantment instance = Enchantment.GetInstance(in controller, minionSource, minionSource, e.Card);
					if (e[GameTag.TAG_SCRIPT_DATA_NUM_1] > 0)
					{
						instance[GameTag.TAG_SCRIPT_DATA_NUM_1] = e[GameTag.TAG_SCRIPT_DATA_NUM_1];
						if (e[GameTag.TAG_SCRIPT_DATA_NUM_2] > 0)
							instance[GameTag.TAG_SCRIPT_DATA_NUM_2] = e[GameTag.TAG_SCRIPT_DATA_NUM_2];
					}
					instance.CapturedCard = e.CapturedCard;
					if (e.IsOneTurnActive)
						game.OneTurnEffectEnchantments.Add(instance);
				}

			//foreach (KeyValuePair<GameTag, int> kvp in minionTarget._data)
			//	switch (kvp.Key)
			//	{
			//		case GameTag.ENTITY_ID:
			//		case GameTag.CONTROLLER:
			//		case GameTag.ZONE:
			//		case GameTag.ZONE_POSITION:
			//		case GameTag.CREATOR:
			//		case GameTag.PREMIUM:
			//		case GameTag.EXHAUSTED:
			//		case GameTag.DEATHRATTLE:
			//			continue;
			//		default:
			//			copy._data.Add(kvp.Key, kvp.Value);
			//			break;
			//	}

			if (aura != null && minionSource.OngoingEffect == null)
				aura.Clone(minionSource);

			//List<(int entityId, IEffect effect)> oneTurnEffects = controller.Game.OneTurnEffects;
			//for (int i = oneTurnEffects.Count - 1; i >= 0; i--)
			//{
			//	(int id, IEffect effect) = oneTurnEffects[i];

			//	if (id == target.Id)
			//		oneTurnEffects.Add((.Id, effect));
			//}

			if (minionTarget.HasCharge)
				minionSource.IsExhausted = false;
			// HsServer (TITANS): a Titan copy has hidden Charge (Advanced rulebook § Copy effects: summoning sickness "if not
			// overridden by keywords like Charge, Rush, or Titan"; wiki V-07-TR-0N Prime patch 27.2.2)
			else if (Generic.IsTitan(minionSource) && minionSource.Controller == game.CurrentPlayer)
				minionSource.IsExhausted = minionSource.NumAttacksThisTurn >= Generic.AttacksPerTurn(minionSource);
			else if (minionTarget.IsRush)
			{
				minionSource.AttackableByRush = true;
				game.RushMinions.Add(minionSource.Id);
			}

			if (_addToStack)
				stack.Playables = new IPlayable[] {minionSource};

			return TaskState.COMPLETE;
		}
	}
}
