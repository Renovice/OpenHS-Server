// HsServer addition (not upstream SabberStone): the "condition met" glow of a card in hand (POWERED_UP).
using System.Linq;
using SabberStoneCore.Conditions;
using System;
using System.Collections.Concurrent;
using SabberStoneCore.Enums;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks;
using SabberStoneCore.Tasks.SimpleTasks;

namespace SabberStoneCore.Model
{
	/// <summary>
	/// Live lights a card in hand (POWERED_UP = 1, the client's yellow glow) while its "If ..." condition would hold if it
	/// were played now (Ravasaur Runt with 2 other minions, Cumulo-Maximus while Overloaded...), and clears it when it is
	/// played. The condition is the card's own script: its play effect starts with a check on itself (a ConditionTask on
	/// the card, or a script that sets the flag) that the next task acts on, and its text says "If". It is evaluated
	/// where it will resolve: on a copy of the game with the card moved out of the hand (a minion onto the board), so
	/// "2 other minions" / "holding a Dragon" don't count the card in hand. No card list.
	/// </summary>
	internal static class PoweredUp
	{
		/// <summary>The card's "If ..." check (and whether "met" means the flag is set), or null.</summary>
		public static (ISimpleTask Task, bool MetWhen)? Check(Card card) => Checks.GetOrAdd(card, Find);

		private static readonly ConcurrentDictionary<Card, (ISimpleTask, bool)?> Checks = new ConcurrentDictionary<Card, (ISimpleTask, bool)?>();

		// The first check on the card itself in its play effect (a ConditionTask on SOURCE, or a script setting the flag),
		// with the FlagTasks right after it acting on it ("If X, A" / "If X, A; otherwise B" / "Deal 3. If X, ...").
		private static (ISimpleTask, bool)? Find(Card card)
		{
			string text = card.Text ?? "";
			if (!text.Contains("If ", StringComparison.Ordinal) && !text.Contains(" if ", StringComparison.Ordinal))
				return null;
			if (!(card.Power?.PowerTask is StateTaskList list))
				return null;
			ISimpleTask[] tasks = list.Tasks;
			for (int i = 0; i + 1 < tasks.Length; i++)
			{
				bool check = tasks[i] is ConditionTask c && c.Type == EntityType.SOURCE || tasks[i] is CustomTask;
				if (!check || !(tasks[i + 1] is FlagTask))
					continue;
				bool metWhen = false;
				for (int j = i + 1; j < tasks.Length && tasks[j] is FlagTask f; j++)
					metWhen |= f.CheckFlag; // a "met" branch: lit when the flag is set; only an "unmet" branch: lit when not
				return (tasks[i], metWhen);
			}
			return null;
		}

		/// <summary>HsServer: does the card's own effect (play, combo or trigger) check <paramref name="condition"/>? Used for
		/// glows tied to one shared condition (ELEMENTAL_POWERED_UP: SelfCondition.ElementalPlayedLastTurn) instead of the
		/// card text.</summary>
		public static bool Uses(Card card, SelfCondition condition) =>
			UsesCache.GetOrAdd((card, condition), k => Uses(k.Item1.Power?.PowerTask, k.Item2) || Uses(k.Item1.Power?.ComboTask, k.Item2) ||
			                                           Uses(k.Item1.Power?.Trigger?.SingleTask, k.Item2) ||
			                                           k.Item1.Power?.Aura is Auras.AdaptiveCostEffect cost && cost.Condition == k.Item2); // Scorch

		private static readonly ConcurrentDictionary<(Card, SelfCondition), bool> UsesCache = new ConcurrentDictionary<(Card, SelfCondition), bool>();

		private static bool Uses(ISimpleTask task, SelfCondition condition) => task switch
		{
			ConditionTask c => c.SelfConditions?.Contains(condition) == true,
			StateTaskList list => list.Tasks.Any(t => Uses(t, condition)),
			_ => false,
		};

		/// <summary>Would <paramref name="card"/>'s condition hold if it were played now?</summary>
		public static bool Holds(Game game, IPlayable card, (ISimpleTask Task, bool MetWhen) check)
		{
			try
			{
				Game copy = game.Clone();
				IPlayable there = copy.IdEntityDic[card.Id];
				Controller owner = there.Controller;
				owner.HandZone.Remove(there);
				if (there is Minion minion && !owner.BoardZone.IsFull)
					owner.BoardZone.Add(minion);
				var stack = new TaskStack();
				check.Task.Process(copy, owner, there, null, stack);
				return stack.Flag == check.MetWhen;
			}
			catch (Exception)
			{
				return false; // a check that can't run outside its play doesn't light the card
			}
		}
	}
}
