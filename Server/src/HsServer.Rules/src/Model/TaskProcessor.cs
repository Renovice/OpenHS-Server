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
//#define LOGEVENT
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks;
using SabberStoneCore.Exceptions;

//using TaskInstance = System.ValueTuple<SabberStoneCore.Tasks.ISimpleTask, SabberStoneCore.Model.Entities.Controller, SabberStoneCore.Model.Entities.IEntity, SabberStoneCore.Model.Entities.IEntity>;

namespace SabberStoneCore.Model
{
	public class TaskQueue
	{
		private class TaskInstance
		{
			public readonly ISimpleTask Task;
			public readonly Controller Controller;
			public readonly IEntity Source;
			public readonly IPlayable Target;
			/// <summary>HsServer: the keyword the task's TRIGGER block fires for (TriggerKeyword), or 0.</summary>
			public readonly int Keyword;
			/// <summary>HsServer: the event a deferred task belongs to (area-effect batch), restored while it runs.</summary>
			public EventMetaData Event;
			/// <summary>HsServer: for a triggered task, the trigger, what its condition was checked on and the event it
			/// was queued in: the condition is checked again when it resolves (rules A11 Rule 10b).</summary>
			public Triggers.Trigger Trigger;
			public IPlayable ConditionSubject;
			public EventMetaData QueuedEvent;
			public Zones.IZone QueuedZone;
			/// <summary>HsServer (2026-10-08): the minion the trigger is attached to (its own, or an enchantment's target) was in play when it
			/// was queued (MPR1: it doesn't resolve once that minion left play).</summary>
			public Minion HolderInPlay;

			public TaskInstance(in ISimpleTask task, in Controller controller, in IEntity source, in IPlayable target, int keyword = 0)
			{
				Task = task;
				Controller = controller;
				Source = source;
				Target = target;
				Keyword = keyword;
			}

			public static implicit operator (ISimpleTask, Controller, IEntity, IPlayable) (TaskInstance t)
			{
				return (t.Task, t.Controller, t.Source, t.Target);
			}

			public void Deconstruct(out ISimpleTask simpleTask, out Controller controller, out IEntity entity, out IPlayable target)
			{
				simpleTask = Task;
				controller = Controller;
				entity = Source;
				target = Target;
			}
		}

		//private readonly struct Event
		//{
		//	public readonly Queue<TaskInstance> Queue;

		//	public Event(Queue<TaskInstance> newQueue)
		//	{
		//		Queue = newQueue;
		//	}
		//}

		private readonly Game _game;
		private readonly Stack<Queue<TaskInstance>> _eventStack;
		//private readonly Stack<Event> _eventStack;
		private readonly Queue<TaskInstance> _baseQueue;
		private readonly Queue<TaskInstance> _pendingTasks;
#if LOGEVENT
		private int _stackHeight;
#endif

		public TaskQueue(Game game)
		{
			_game = game;
			_eventStack = new Stack<Queue<TaskInstance>>();
			//_eventStack = new Stack<Event>();
			_baseQueue = new Queue<TaskInstance>();
			CurrentQueue = _baseQueue;
			_pendingTasks = new Queue<TaskInstance>();
		}

		//private Queue<TaskInstance> CurrentQueue => _eventStack.Count == 0 ? _baseQueue : _eventStack.Peek();
		private Queue<TaskInstance> CurrentQueue { get; set; }

		// nothing left in current event
		//public bool IsEmpty => _eventFlag || CurrentQueue.Count == 0;
		public bool IsEmpty => CurrentQueue == null || CurrentQueue.Count == 0;

		public ISimpleTask CurrentTask { get; private set; }

		/// <summary>HsServer: the source of the task running now (restored when it ends, like CurrentTask): the card whose
		/// effect is generating, which a random pick never makes (Cards.GeneratingCard).</summary>
		public IEntity CurrentSource { get; private set; }
		/// <summary>HsServer (2026-10-08): the target of the queued task now running (an enchantment-granted Deathrattle's: its enchantment).</summary>
		public IPlayable CurrentTarget { get; private set; }

		public void StartEvent()
		{
			//_eventFlag = true;
			//_numEventsStarted++;
			//_eventStack.Push(null);

			_eventStack.Push(CurrentQueue);
			CurrentQueue = null;

#if LOGEVENT
			if (_game.Logging)
			{
				//var sb = new System.Text.StringBuilder("Event Starts");
				//for (int i = 0; i < 10 - _stackHeight; i++)
				//	sb.Append("----");
				_game.Log(LogLevel.DEBUG, BlockType.ACTION, "TaskQueue",
					//sb.ToString()
					$"Event ({_stackHeight}) Starts"
				);
				_stackHeight++;
			}
#endif
		}

		public void EndEvent()
		{
#if LOGEVENT
			if (_game.Logging)
			{
				//var sb = new System.Text.StringBuilder("Event Ends--");
				//for (int i = 0; i < 10 - _stackHeight; i++)
				//	sb.Append("----");
				//_stackHeight--;
				_stackHeight--;
				_game.Log(LogLevel.DEBUG, BlockType.ACTION, "TaskQueue",
					//sb.ToString()
					$"Event ({_stackHeight}) Ends"
				);
			}
#endif



			//if (!_eventFlag)
			//{
			//	if (_eventStack.Count > 0)
			//		_eventStack.Pop();
			//}
			//else
			//{
			//	_eventFlag = false;
			//	_numEventsStarted--;
			//}

			//if (_numEventsStarted > 0)
			//	_eventFlag = true;

			CurrentQueue = _eventStack.Pop();
		}

		public void Enqueue(in ISimpleTask task, in Controller controller, in IEntity source, in IPlayable target) =>
			Enqueue(in task, in controller, in source, in target, 0);

		/// <summary>HsServer (2026-10-08): queue <paramref name="task"/> as a card's own TRIGGER block (no keyword) at the client effect index
		/// <paramref name="effectIndex"/> — a step live sends as its own block after the current one (a hand card's upgrade after its
		/// countdown, an Objective's expiry, a "spend Mana" countdown in the play's trigger phase).</summary>
		public void EnqueueTriggerBlock(ISimpleTask task, Controller controller, IEntity source, int effectIndex)
		{
			task.IsTrigger = true;
			Enqueue(task, controller, source, null, 0, 0, -1, new Triggers.Trigger(TriggerType.NONE) { EffectIndex = effectIndex });
		}

		/// <summary>HsServer: queue a task whose block is a TRIGGER for <paramref name="keyword"/> (live TriggerKeyword:
		/// DEATHRATTLE, SECRET, TRIGGER_VISUAL...), which the client animates by.</summary>
		public void Enqueue(in ISimpleTask task, in Controller controller, in IEntity source, in IPlayable target, int keyword) =>
			Enqueue(in task, in controller, in source, in target, keyword, 0);

		// HsServer: rules A8 Rule 7 — a death's responders (on-death triggers and Secrets, its own Deathrattle, granted
		// Deathrattles) are gathered, then queued in order of play; a "last within a death" Priority (Redemption) goes
		// after the rest. Only tasks queued at the gathering's own event level are gathered.
		// HsServer: rules A2 Rule 1b / DH1 — an area effect creates and applies all its Damage Events first; the triggers
		// of each event are then queued, event by event, and resolved (Flamestrike: every target is hit before an Imp
		// Gang Boss's Imp is summoned, so Knife Juggler's knife can't pick a minion that's already at 0 Health).
		private List<TaskInstance> _batch;
		private int _batchLevel;

		/// <summary>HsServer: drop an area-effect batch or a death gathering left open by an effect that threw.</summary>
		public void ResetBatches()
		{
			_batch = null;
			_gathered = null;
		}

		public bool StartBatch()
		{
			if (_batch != null)
				return false;
			_batch = new List<TaskInstance>();
			_batchLevel = _eventStack.Count;
			return true;
		}

		/// <summary>Inside a batch, at a hit's own event level: set that event's queued triggers aside (with the event).</summary>
		internal bool DeferCurrentEvent(EventMetaData ev)
		{
			if (_batch == null || _eventStack.Count != _batchLevel + 1)
				return false;
			if (CurrentQueue != null)
			{
				foreach (TaskInstance t in CurrentQueue)
				{
					t.Event ??= ev;
					_batch.Add(t);
				}
				CurrentQueue.Clear();
			}
			return true;
		}

		public void EndBatch()
		{
			List<TaskInstance> batch = _batch;
			_batch = null;
			if (batch.Count == 0)
				return;
			Queue<TaskInstance> queue = CurrentQueue;
			if (queue == null)
			{
				queue = new Queue<TaskInstance>();
				CurrentQueue = queue;
			}
			foreach (TaskInstance t in batch)
				queue.Enqueue(t);
		}

		private List<(TaskInstance task, int priority, int order, int seq)> _gathered;
		private int _gatherLevel;

		/// <summary>HsServer (2026-10-08): triggers are being gathered (StartGathering) for one order-of-play queue.</summary>
		public bool Gathering => _gathered != null;

		public void StartGathering()
		{
			_gathered = new List<(TaskInstance, int, int, int)>();
			_gatherLevel = _eventStack.Count;
		}

		/// <param name="stage">optional first sort key by the task's source (e.g. combat: Secrets' Proposed Attack Event
		/// before the Attack Event).</param>
		/// <param name="keep">HsServer: only the tasks whose source and trigger pass (combat: the Proposed Attack Event's, then the Attack Event's).</param>
		public void EndGathering(Func<IEntity, int> stage = null, Func<IEntity, Triggers.Trigger, bool> keep = null)
		{
			List<(TaskInstance task, int priority, int order, int seq)> gathered = _gathered;
			_gathered = null;
			if (keep != null)
				gathered.RemoveAll(x => !keep(x.task.Source, x.task.Trigger));
			if (stage != null)
				for (int i = 0; i < gathered.Count; i++)
					gathered[i] = (gathered[i].task, gathered[i].priority + 1000 * stage(gathered[i].task.Source), gathered[i].order, gathered[i].seq);
			gathered.Sort((a, b) => a.priority != b.priority ? a.priority.CompareTo(b.priority)
				: a.order != b.order ? a.order.CompareTo(b.order) : a.seq.CompareTo(b.seq));
			foreach ((TaskInstance t, _, _, _) in gathered)
				EnqueueInstance(t);
		}

		/// <param name="priority">0 normal, 1 last within a death (see <see cref="StartGathering"/>).</param>
		/// <param name="orderOfPlay">the timestamp the task is ordered by while gathering; -1 = its source's.</param>
		public void Enqueue(in ISimpleTask task, in Controller controller, in IEntity source, in IPlayable target, int keyword,
			int priority, int orderOfPlay = -1, Triggers.Trigger trigger = null, IPlayable conditionSubject = null)
		{
			var instance = new TaskInstance(task, controller, source, target, keyword)
			{
				Trigger = trigger, ConditionSubject = conditionSubject, QueuedEvent = trigger != null ? _game.CurrentEventData : null,
				QueuedZone = conditionSubject?.Zone,
				HolderInPlay = trigger != null && keyword != (int)GameTag.DEATHRATTLE &&
				               (source is Minion own ? own : (source as Enchantment)?.Target as Minion) is Minion h && h.Zone?.Type == Enums.Zone.PLAY ? h : null,
			};
			if (_gathered != null && _eventStack.Count == _gatherLevel)
			{
				int order = orderOfPlay >= 0 ? orderOfPlay : (source as IPlayable)?.OrderOfPlay ?? int.MaxValue;
				_gathered.Add((instance, priority, order, _gathered.Count));
				return;
			}
			EnqueueInstance(instance);
		}

		private void EnqueueInstance(TaskInstance instance)
		{

			//if (_eventFlag)	// flag = true means Event starts and no tasks queue yet
			//{
			//	if (CurrentQueue.Count != 0) // Check if an ongoing event exists
			//		_eventStack.Push(new Queue<TaskInstance>());

			//	_eventFlag = false;
			//	_numEventsStarted--;
			//}

			//Stack<Queue<TaskInstance>> stack = _eventStack;
			Queue<TaskInstance> queue = CurrentQueue;
			if (queue == null)
			{
				queue = new Queue<TaskInstance>();
				CurrentQueue = queue;
			}

			queue.Enqueue(instance);

#if LOGEVENT
			_game.Log(LogLevel.DEBUG, BlockType.TRIGGER, "TaskQueue",
				!_game.Logging ? "" : $"{instance.Task.GetType().Name} is Enqueued in {_eventStack.Count}th stack");
#endif
		}

		public void EnqueueBase(in ISimpleTask task, in Controller controller, in IEntity source, in IPlayable target)
		{
			//_baseQueue.Enqueue((task, controller, source, target));
			//if (!_eventFlag && _eventStack.Count == 0)
			//{
			//	_eventStack.Push(new Queue<TaskInstance>(_baseQueue));
			//	_baseQueue.Clear();
			//}

			_baseQueue.Enqueue(new TaskInstance(in task, in controller, in source, in target));

			_game.Log(LogLevel.DEBUG, BlockType.TRIGGER, "TaskQueue",
				!_game.Logging ? "" : $"{task.GetType().Name} is Enqueued in 0th stack");
		}

		/// <summary>
		/// Queue a task that will be processed after a task is queued and processed.
		/// </summary>
		/// <param name="task"></param>
		public void EnqueuePendingTask(in ISimpleTask task, in Controller controller, in IEntity source, in IPlayable target)
		{
			_pendingTasks.Enqueue(new TaskInstance(in task, in controller, in source, in target));
			_hasPendingTask = true;
		}

		private bool _hasPendingTask;

		public void ResumePendingTasks()
		{
			if (!_hasPendingTask) return;
			_hasPendingTask = false;

			do CurrentQueue.Enqueue(_pendingTasks.Dequeue());
			while (_pendingTasks.Count != 0);
		}

		public TaskState Process()
		{
			TaskInstance instance = CurrentQueue.Peek();
			(ISimpleTask task, Controller controller, IEntity source, IPlayable target) = instance;

			// HsServer (TITANS review 2026-10-07): an Attack Event trigger of a minion — its own ("whenever this attacks"), or an
			// enchantment's on it — needs that minion still in play when it resolves. Rule book (Advanced_rulebook.txt:552-555,
			// Attack Event): "Cutpurse has a condition of 'attacker is this minion and defender is a hero'. It implicitly
			// requires the Cutpurse to still be in play."; "Blessing of Wisdom has a condition of 'attacker is this minion'. It
			// implicitly requires the attacker to still be in play, as it gets detached when the attacker leaves play." The
			// Proposed Attack Event's Secrets resolve first (Attack.cs): Kologarn sent back to hand by Freezing Trap steals nothing.
			// HsServer (2026-10-08): the general rule — MPR1 (Advanced_rulebook.txt:1573): "If a minion is removed from play in the middle of
			// a Phase (… returned to the hand, shuffled into the deck or due to Full Zone Instant Removal), all additional triggers attached
			// to it are immediately removed from play and ineligible to trigger" (:1586 Cult Master bounced by Anub'ar Ambusher draws nothing;
			// :1587 Nightmare on a bounced Alarm-o-Bot does nothing). MPR2 (:1574): not a Deathrattle. A trigger of a minion that works in
			// play — its own, or an enchantment's on it — doesn't resolve once that minion left play (the attack triggers were the first case).
			if (instance.HolderInPlay is Minion holder && holder.Zone?.Type != Enums.Zone.PLAY)
			{
				_game.Log(LogLevel.VERBOSE, BlockType.TRIGGER, "TaskQueue", !_game.Logging ? "" : $"{source}'s trigger: its minion left play.");
				CurrentQueue.Dequeue();
				return TaskState.COMPLETE;
			}

			// HsServer: rules A11 Rule 10b — a trigger checks its condition when it resolves too: an earlier responder may
			// have changed it (a buff before Hobgoblin's "1-Attack minion"). Checked in the event it was queued in, and only
			// while the subject is still where it was: a condition about the moment of play ("the right-most card in your
			// hand", Stargazer Luna) can't be asked again once the card has left the hand.
			if (instance.Trigger?.Condition != null && instance.ConditionSubject != null &&
			    instance.ConditionSubject.Zone == instance.QueuedZone)
			{
				EventMetaData now = _game.CurrentEventData;
				_game.CurrentEventData = instance.QueuedEvent ?? now;
				bool holds = instance.Trigger.Condition.Eval(instance.ConditionSubject);
				_game.CurrentEventData = now;
				if (!holds)
				{
					_game.Log(LogLevel.VERBOSE, BlockType.TRIGGER, "TaskQueue", !_game.Logging ? "" : $"{source}'s trigger no longer applies.");
					CurrentQueue.Dequeue();
					return TaskState.COMPLETE;
				}
			}
			// HsServer (2026-10-10): a Secret that has left play (revealed by an earlier event of the same phase) does nothing more —
			// Advanced rulebook Rule 7: "For each Death, all Death Event triggers ([[Deathrattle]]s, on-Death [[Secret]]s ...) are
			// Queued and resolved"; live Duplicate (session-20261002-204752 107628) triggered once for two friendly deaths, ours twice
			if (instance.Keyword == (int)GameTag.SECRET && source is Spell spent && spent.Zone?.Type != Enums.Zone.SECRET)
			{
				_game.Log(LogLevel.VERBOSE, BlockType.TRIGGER, "TaskQueue", !_game.Logging ? "" : $"{source} has left play: its Secret no longer triggers.");
				CurrentQueue.Dequeue();
				return TaskState.COMPLETE;
			}
			// HsServer: off the queue before it runs. A nested ProcessTasks inside it (an effect that resolves another card's
			// effect at once: a chosen card cast, a Dormant awakening) then only runs what was queued after it; with the
			// task still at the queue's head it ran the same task again and the queue ran dry ("Queue empty", Unseen
			// Saboteur casting a Discover spell, soak 2026-10-05).
			CurrentQueue.Dequeue();
			ISimpleTask temp = CurrentTask;
			CurrentTask = task;
			IEntity tempSource = CurrentSource;
			CurrentSource = source;
			IPlayable tempTarget = CurrentTarget;
			CurrentTarget = target;
			bool revealedFromDeck = false;
			PowerHistoryBlockStart keywordBlock = null;

			//if (currentTask is StateTaskList tasks)
			//	tasks.Stack = new TaskStack(_game);

			_game.Log(LogLevel.VERBOSE, BlockType.TRIGGER, "TaskQueue", !_game.Logging ? "" : $"LazyTask[{source}]: '{CurrentTask.GetType().Name}' is processed!" +
			                                                                                $"'{source.Card.Text?.Replace("\n", " ")}'");
			if (_game.History)
			{
				// HsServer: a keyworded trigger (Deathrattle, Secret...) is a TRIGGER block with effect index 0 and its keyword, as live.
				// HsServer: live (captures/live-20261001-startofgame, Genn) — a Start of Game card in the deck is first revealed in
				// its own TRIGGER block (effect index 2, REVEALED 1: SHOW_ENTITY to both players), then its keyworded block runs,
				// and it's hidden again at that block's end.
				// (2026-10-08: from the opening hand too — live 174206 14109, Mug'Zee in hand: reveal block effect index 7, then its block)
				revealedFromDeck = instance.Keyword == (int)GameTag.START_OF_GAME && source is IPlayable p0 && p0.Zone?.Type is Enums.Zone.DECK or Enums.Zone.HAND;
				if (revealedFromDeck)
				{
					_game.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, source.Id, "", ((IPlayable)source).Zone.Type == Enums.Zone.HAND ? 7 : 2, 0));
					((IPlayable)source)[GameTag.REVEALED] = 1;
					_game.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
				}
				if (instance.Keyword != 0)
				{
					// HsServer: live TRIGGER blocks never name a target (3,790 of 3,790 in the captures): the event's entity is
					// not something the effect lands on, and naming it made the client hold it back (a played Murloc stayed
					// invisible until Underbelly Angler's card arrived).
					// (its effect index: which of the card's client trigger effects plays; Trigger.EffectIndex)
					int effectIndex = instance.Keyword == (int)GameTag.DEATHRATTLE
						? (source as IPlayable)?.Card.Power?.DeathrattleEffectIndex ?? 0
						: instance.Trigger?.EffectIndex ?? 0;
					PowerHistoryBlockStart start = PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, source.Id, "", effectIndex, 0);
					start.TriggerKeyword = instance.Keyword;
					start.ShowInHistory = (HsTag.ShowsInHistory(instance.Keyword) || instance.Trigger?.InHistory == true) && instance.Trigger?.NotInHistory != true;
					start.IsDeferrable = HsTag.IsDeferrable(instance.Keyword);
					_game.PowerHistory.Add(start);
					keywordBlock = start;
				}
				else
				{
					PowerHistoryBlockStart plain = PowerHistoryBuilder.BlockStart(task.IsTrigger ? BlockType.TRIGGER : BlockType.POWER, source.Id, "",
						// HsServer: live — POWER blocks and a card's (or enchantment's) own trigger use effect index 0; the client
						// plays the card's trigger effect only for an index >= 0. Player / game triggers stay -1.
						task.IsTrigger && (source is Controller || source is Game) ? -1 : task.IsTrigger ? instance.Trigger?.EffectIndex ?? 0 : 0,
						task.IsTrigger ? 0 : target?.Id ?? 0); // live TRIGGER blocks: Target=0
					// (2026-10-08: a keyword-less trigger shown in the history — live Sandfury Aura's countdown, Rhythm and Roots)
					plain.ShowInHistory = task.IsTrigger && instance.Trigger?.InHistory == true;
					_game.PowerHistory.Add(plain);
				}
			}

			Game.LastTask = task; // HsServer: breadcrumb for a hang watchdog
			Game.LastTaskSource = source;
			if (Game.AbortRequested) // HsServer: a watchdog asked a stuck engine to report where it is
				throw new TaskException($"Aborted by watchdog. Task:{task}, Source:{source}, Target:{target}, tasks this move {_game.TasksThisMove}");
			if (++_game.TasksThisMove > Game.MaxTasksPerMove) // HsServer: runaway guard
				throw new TaskException($"Runaway effect: more than {Game.MaxTasksPerMove} tasks in one move. Task:{task}, Source:{source}, Target:{target}");

			// HsServer: the card's own effect prefab around what the task does (SUB_SPELL, Loader.SubSpells):
			// its Deathrattle, or its effect on a target.
			// (HsServer (TITANS): not a Titan's: its prefabs belong to its abilities, played by Generic.UseTitanAbility / the
			// ability scripts — the Titan's card lists several, and the wrong one would wrap everything it does)
			if (_game.History && source is IPlayable effectSource && !effectSource.Card.IsTitan)
			{
				// (HsServer 2026-10-08: the effects live recorded for this block wait in it from its start: Game.RecordedFxAt)
				string blockKind = instance.Keyword == (int)GameTag.DEATHRATTLE ? "DEATHRATTLE" : instance.Trigger != null || task.IsTrigger ? "TRIGGER" : "POWER";
				string prefab = instance.Keyword == (int)GameTag.DEATHRATTLE
					? Loader.SubSpells.Find(effectSource.Card.Id, Loader.SubSpells.Role.Deathrattle)
					: target != null ? Loader.SubSpells.Find(effectSource.Card.Id, Loader.SubSpells.Role.Missile, blockKind != "POWER") : null;
				// a Deathrattle without its own effect plays its missile at what it hits (live Shadowed Spirit CS3_013:
				// Deathrattle block, SUB_SPELL Missile_Shadowbolt, Targets = the hero it damages; session-20260930-125547)
				bool deathrattleMissile = false;
				// (not on a card with a Battlecry: its missile may be the Battlecry's: Iso'rath, Shadestone Skulker)
				if (prefab == null && instance.Keyword == (int)GameTag.DEATHRATTLE && effectSource.Card[GameTag.BATTLECRY] == 0 &&
				    Loader.SubSpells.Find(effectSource.Card.Id, Loader.SubSpells.Role.Missile) is { } missile)
				{
					prefab = missile;
					deathrattleMissile = true;
				}
				// a spell's missile flies from its caster's hero, a minion's from itself (live-20261002-visuals: Demonic
				// Assault's Source = the hero; from the card it started wherever the card was held)
				if (prefab != null)
					_game.OpenSubSpell(effectSource, prefab, target != null ? new[] { target.Id } : null,
						shownSource: effectSource is Spell && instance.Keyword != (int)GameTag.DEATHRATTLE ? controller.Hero.Id : 0,
						collectTargets: deathrattleMissile && target == null);
				// its buff / area effect around the whole action (live: one per action, the effect inside; a spell's
				// plays from its caster's hero, a minion's from itself)
				// (a card with its own Corpse effect closes it around its Corpse step and opens it again after it:
				// Generic.SpendCorpses; effects go in the order of the card's text, engine-rules.md)
				else if (effectSource.Card.Power?.OwnFx != true && Loader.SubSpells.WholeEffect(effectSource.Card.Id, instance.Trigger != null, includeRecorded: false) is { } whole)
				{
					int[] wholeTargets = target != null ? new[] { target.Id } : null;
					int shown = effectSource is Spell ? controller.Hero.Id : 0;
					// a buff effect waits for the buff (a step before it plays first: Focused Will; Game.OpenSubSpellAtFirstBuff)
					if (Loader.SubSpells.RoleOf(effectSource.Card.Id, whole) == Loader.SubSpells.Role.Buff)
						_game.OpenSubSpellAtFirstBuff(effectSource, whole, wholeTargets, shown, target == null);
					else
						_game.OpenSubSpell(effectSource, whole, wholeTargets, shownSource: shown, collectTargets: target == null);
				}
			}

			TaskState success;
			EventMetaData outerEvent = _game.CurrentEventData;
			if (instance.Event != null)
				_game.CurrentEventData = instance.Event;
			Triggers.Trigger outerTrigger = _game.RunningTrigger;
			_game.RunningTrigger = instance.Trigger;
			int outerEffect = _game.EffectSerial; // HsServer: this task is its own effect (Game.EffectSerial)
			_game.EffectSerial = ++_game.EffectCounter;
			// HsServer: always name the task and its source card (release builds too; the soak and server log need it).
			try
			{
				success = task.Process(in _game, in controller, in source, in target);
			}
			catch (Exception e)
			{
				throw new TaskException(
					$"Exception occurs during processing a task.\nTask:{task}, Source:{source}, Target:{target}", e);
			}
			finally
			{
				_game.EffectSerial = outerEffect;
				_game.RunningTrigger = outerTrigger;
				if (instance.Event != null)
					_game.CurrentEventData = outerEvent;
				if (_game.History)
				{
					_game.FlushPendingFx(source.Id);
					_game.CloseSubSpells(source.Id, keepSpawnGroup: true); // (one effect's cards keep their shared to-hand effect)
				}
			}

			// HsServer: a Secret whose effect stopped before its end (Secret: the SECRET_TRIGGERED step) either did nothing — it
			// aborted (Noble Sacrifice / Mirror Entity on a full board, Freezing Trap on a dying attacker): its block is taken
			// back, no reveal, it stays up (Secret.txt: it "will simply remain in place without taking effect, revealing itself,
			// or giving any indication") — or did part of it (Snake Trap with room for one Snake): it is spent.
			bool secretAborted = false;
			if (instance.Keyword == (int)GameTag.SECRET && source is Spell stopped && stopped[HsTag.SECRET_TRIGGERED] != 1 &&
			    stopped.Zone?.Type == Enums.Zone.SECRET)
			{
				secretAborted = _game.History && keywordBlock != null && _game.PowerHistory.DropEmptyBlock(keywordBlock);
				if (!secretAborted && _game.History) stopped[HsTag.SECRET_TRIGGERED] = 1;
			}
			if (_game.History && !secretAborted)
			{
				if (revealedFromDeck)
					((IPlayable)source)[GameTag.REVEALED] = 0; // back into the deck, hidden (HIDE_ENTITY)
				_game.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			}

			RevealTriggeredSecret(source);

			// reset between task execution
			//_game.TaskStack.Reset();

			CurrentTask = temp;
			CurrentSource = tempSource;
			CurrentTarget = tempTarget;

			return success;
		}

		/// <param name="keyword">HsServer: for a trigger resolved at once (end of turn…), the keyword its TRIGGER block fires
		/// for (live: TriggerKeyword=TRIGGER_VISUAL lights the card's lightning icon); -1 = not a trigger (a POWER block).</param>
		/// <param name="effectCardId">HsServer (2026-10-08): the card whose effect this is when it isn't the source's own (live EffectCardId:
		/// Brilliant Macaw / Shudderwock repeating a Battlecry — Generic.RunCardEffect); its POWER block names it.</param>
		public void Execute(in ISimpleTask task, in Controller controller, in IPlayable source, in IPlayable target, int number = 0, int keyword = -1, int effectIndex = 0,
			bool inHistory = false, string effectCardId = null, bool notInHistory = false)
		{
			Game.LastTask = task; // HsServer: breadcrumb for a hang watchdog
			Game.LastTaskSource = source;

			_game.Log(LogLevel.VERBOSE, BlockType.TRIGGER, "TaskQueue", !_game.Logging ? "" : $"PriorityTask[{source}]: '{task.GetType().Name}' is processed!" +
			                                                                                $"'{source.Card.Text?.Replace("\n", " ")}'");

			// power block
			if (controller.Game.History)
			{
				if (keyword >= 0)
				{
					// HsServer: the same TRIGGER block as a queued trigger (Process): card triggers index 0 + keyword.
					PowerHistoryBlockStart start = PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, source.Id, "",
						source is Controller ? -1 : effectIndex, 0); // live TRIGGER blocks: Target=0; the trigger's own index
					start.TriggerKeyword = keyword;
					// (Trigger.NotInHistory: never shown, whatever its keyword — as a queued trigger's block in Process)
					start.ShowInHistory = !notInHistory && (inHistory || HsTag.ShowsInHistory(keyword));
					controller.Game.PowerHistory.Add(start);
				}
				else
				{
					PowerHistoryBlockStart power = PowerHistoryBuilder.BlockStart(BlockType.POWER, source.Id, effectCardId ?? "", 0, target?.Id ?? 0); // HsServer: live effect index 0
					power.ShowInHistory = inHistory;
					controller.Game.PowerHistory.Add(power);
				}
			}

			IEntity outerSource = CurrentSource;
			CurrentSource = source;
			int outerEffect = _game.EffectSerial; // HsServer: this task is its own effect (Game.EffectSerial)
			_game.EffectSerial = ++_game.EffectCounter;
			try { task.Process(in _game, in controller, source, target); }
			finally { _game.EffectSerial = outerEffect; }
			CurrentSource = outerSource;

			if (controller.Game.History)
			{
				_game.CloseSubSpells(source.Id); // its effects end with its block (a group's spawn effect: Generic.SummonBlock)
				controller.Game.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			}
			RevealTriggeredSecret(source);

			//_game.TaskStack.Reset();
		}

		// HsServer: a triggered Secret is revealed after its TRIGGER block, as live (74 of 74): a 5 s pause (the reveal
		// banner), then it goes to the graveyard (the opponent's is shown by that move) and "Secret revealed" triggers run.
		private void RevealTriggeredSecret(IEntity source)
		{
			if (!(source is Spell secret) || secret[HsTag.SECRET_TRIGGERED] != 1) return;
			secret[HsTag.SECRET_TRIGGERED] = 0;
			if (secret.Zone?.Type != Enums.Zone.SECRET) return;
			if (_game.History)
				_game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 5000, Info = new List<int> { secret.Id } });
			secret.Controller.GraveyardZone.Add(secret.Zone.Remove(secret));
			if (secret.Card.IsSigil) return; // HsServer (TITANS): a Sigil's effect is over (Generic.SigilPower): no Secret revealed
			secret.Controller[HsTag.SECRETS_REVEALED] = secret.Controller[HsTag.SECRETS_REVEALED] + 1; // Sayge
			secret[HsTag.SECRET_TRIGGERED_ORDER] = secret.Controller[HsTag.SECRETS_REVEALED]; // HsServer (WHIZBANG): Product 9's order
			_game.RaiseEvent(() => _game.TriggerManager.OnSecretRevealedTrigger(secret));
		}

		public void ClearCurrentEvent()
		{
			CurrentQueue.Clear();
		}

		public void Clone()
		{
			//return new TaskQueue();
		}
	}


	internal class EventMetaData
	{
		public IPlayable EventSource { get; set; }
		public IPlayable EventTarget { get; set; }
		public int EventNumber { get; set; }

		public EventMetaData(IPlayable source, IPlayable target, int number = 0)
		{
			EventSource = source;
			EventTarget = target;
			EventNumber = number;
		}
	}
}
