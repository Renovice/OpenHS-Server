// HsServer addition (not upstream SabberStoneCore): Warlock Rituals (the BE set: "In @ turns, ..."), one wire for
// every Ritual. A Ritual is an Objective (card data: OBJECTIVE, QUEST_PROGRESS_TOTAL 2, OBJECTIVE_COUNTDOWN 4960): played, it waits in the
// Secret zone; at the start of each of its owner's turns it ticks (Generic.ObjectiveTick), and on its last tick it does its effect.
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks;
using SabberStoneCore.Tasks.SimpleTasks;
using SabberStoneCore.Triggers;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>Player tag: the Rituals this player completed this game (live: 4976 = 1 on the player right after the first one's
		/// effect — session-20261021-013733 5794, 20261021-194927 BE_085). Kith'ix reads it.</summary>
		public const GameTag RITUALS_COMPLETED = (GameTag)4976;

		/// <summary>
		/// A Ritual's countdown (live session-20261021-013733 BE_005 4410 / 5727, BE_085 and BE_726 in 20261021-194927): a TRIGGER block
		/// at MAIN_START_TRIGGERS, TriggerKeyword OBJECTIVE, shown in the history, at the card's own <paramref name="effectIndex"/>
		/// (live: BE_005 3, BE_085 2, BE_726 0). A tick that isn't the last: QUEST_PROGRESS, the turns counter, FX_DATANUM_1 = the
		/// progress. The last: QUEST_PROGRESS, the counter, a revealed copy shown big ([copy, 2000], ARTIFICIAL_PAUSE 1000 and
		/// SLUSH_TIME 1000 on the Ritual), <paramref name="effect"/>, RITUALS_COMPLETED +1, then it expires (Generic.ObjectiveExpires).
		/// </summary>
		public static Trigger RitualTrigger(int effectIndex, ISimpleTask effect) => new Trigger(TriggerType.TURN_START)
		{
			EffectIndex = effectIndex,
			InHistory = true,
			SingleTask = new CustomTask((g, c, s, t, stack) =>
			{
				if (!(s is Spell ritual) || ritual.Zone?.Type != Zone.SECRET) return;
				if (!ObjectiveTick(ritual, ritual.QuestTotalProgress)) { ritual[GameTag.FX_DATANUM_1] = ritual.QuestProgress; return; }
				if (g.History)
				{
					IPlayable shown = Entity.FromCard(c, ritual.Card, new EntityData { [GameTag.CREATOR] = ritual.Id }, c.SetasideZone);
					shown[GameTag.REVEALED] = 1;
					g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SHOW_BIG_CARD, Data = 0, Info = new System.Collections.Generic.List<int> { shown.Id, 2000 } });
					g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.ARTIFICIAL_PAUSE, Data = 1000, Info = new System.Collections.Generic.List<int> { ritual.Id } });
					g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 1000, Info = new System.Collections.Generic.List<int> { ritual.Id } });
				}
				effect.Process(g, c, s, t, stack);
				c[RITUALS_COMPLETED]++;
				ObjectiveExpires(c, ritual);
			})
		};

		/// <summary>Begin a Ritual an effect makes (Kith'ix's Sinister Rituals: a new entity of it into the Secret zone, as if played).</summary>
		public static void BeginRitual(Controller c, IEntity source, Card ritual)
		{
			var made = (Spell)Entity.FromCard(c, ritual, new EntityData { [GameTag.CREATOR] = source.Id });
			PutSecretIntoPlay(c, made);
		}
	}
}
