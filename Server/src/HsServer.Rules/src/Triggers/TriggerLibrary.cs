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

using System.Linq;
using SabberStoneCore.Enums;
using SabberStoneCore.Tasks;
using SabberStoneCore.Tasks.SimpleTasks;

namespace SabberStoneCore.Triggers
{
	public static class TriggerLibrary
    {
		//public static Trigger EnrageTrigger(string enchantmentId)
	 //   {
		//    return new Trigger(TriggerType.PREDAMAGE)
		//    {
		//	    TriggerSource = TriggerSource.SELF,
		//	    Condition = SelfCondition.IsUndamaged,
		//	    SingleTask = new AddEnchantmentTask(enchantmentId, EntityType.SOURCE)
		//    };
	 //   }

	    public static readonly Trigger RevealUnidentifiedItem
		    = new Trigger(TriggerType.ZONE)
		    {
			    TriggerActivation = TriggerActivation.HAND,
			    TriggerSource = TriggerSource.SELF,
			    RemoveAfterTriggered = true,
			    SingleTask = new ChangeUnidentifiedTask()
		    };

	    // TODO: maybe need some more work here
		public static Trigger WorgenTransform(string cardId, string enchantmentId)
	    {
		    return new Trigger(TriggerType.WORGEN_TRANSFORM)
		    {
			    TriggerActivation = TriggerActivation.HAND,
			    SingleTask = ComplexTask.Create(
				    new SwapAttackHealthTask(EntityType.SOURCE, enchantmentId),
				    new ChangeEntityTask(cardId)),
			    RemoveAfterTriggered = true
		    };
	    }

		public static Trigger SpiritTrigger(Trigger trigger)
		{
			return new MultiTrigger(OneTurnStealth, trigger);
		}

		/// <summary>"Stealth for 1 turn": it ends at the start of its owner's next turn. Live (live-20261007-vfx-decks
		/// 91975 / 94609 / 96609 SP-3Y3-D3R; live-20261004-night 5782 a Rastakhan Spirit): a TRIGGER block on the minion at
		/// MAIN_START_TRIGGERS, effect index 2, TriggerKeyword STEALTH, STEALTH = 0. HsServer (TITANS): a Stealth the minion
		/// holds for good (HsTag.PERMANENT_STEALTH) stays (wiki SP-3Y3-D3R patch 27.4).</summary>
		public static readonly Trigger OneTurnStealth
			= new Trigger(TriggerType.TURN_START)
			{
				TriggerActivation = TriggerActivation.PLAY,
				KeywordOverride = GameTag.STEALTH,
				EffectIndex = 2,
				SingleTask = new CustomTask((g, c, s, t, stack) =>
				{
					// HsServer (TITANS review 2026-10-07): only its own Stealth ends — not one an outside source keeps granting (an
					// applied enchantment with STEALTH: a magnetized Sparkbot) — wiki SP-3Y3-D3R, patch 27.4.0.185749: "Fixed a bug
					// where SP-3Y3-D3R would still lose Stealth after one turn if it was granted permanent Stealth by an outside source
					// (like a Sparkbot)"
					if (s is Model.Entities.Minion m && m[HsTag.PERMANENT_STEALTH] == 0 && !StealthFromElsewhere(m))
						m.HasStealth = false;
				}),
				RemoveAfterTriggered = true,
			};

		/// <summary>Does an applied enchantment (not a one-turn one) give <paramref name="m"/> Stealth?</summary>
		private static bool StealthFromElsewhere(Model.Entities.Minion m) =>
			m.AppliedEnchantments?.Any(e => e.Power?.Enchant is Enchants.Enchant en && !en.IsOneTurnEffect &&
			                               en.Effects?.Any(x => x.Tag == GameTag.STEALTH && x is Enchants.Effect f && f.Value > 0) == true &&
			                               !(e.Card.ClientText ?? "").Contains("for 1 turn")) == true;

		public static readonly Trigger UpgradeEachTurn =
			new Trigger(TriggerType.TURN_END)
			{
				TriggerActivation = TriggerActivation.HAND,
				SingleTask = ComplexTask.Create(
					new GetGameTagTask(GameTag.TAG_SCRIPT_DATA_NUM_1, EntityType.SOURCE),
					new MathAddTask(1),
					new SetGameTagNumberTask(GameTag.TAG_SCRIPT_DATA_NUM_1, EntityType.SOURCE))
			};
    }
}
