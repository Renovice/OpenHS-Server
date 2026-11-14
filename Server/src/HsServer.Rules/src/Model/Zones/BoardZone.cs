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
using System.Linq;
using System.Collections.Generic;
using SabberStoneCore.Auras;
using SabberStoneCore.Enums;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Model.Zones
{
	public class BoardZone : PositioningZone<Minion>
	{
		private int _untouchableCount;
		private bool _hasUntouchables;

		public List<AdjacentAura> AdjacentAuras = new List<AdjacentAura>();

		public BoardZone(Controller controller) : base(Zone.PLAY, Game.MAX_MINIONS_ON_BOARD)
		{
			Game = controller.Game;
			Controller = controller;
		}

		// HsServer (2026-10-08): read from the minions themselves, not the cached count — a stale cache sized "all minions" lists by one
		// count and filled them by another (soak 2200174: a null in Doomsayer's destroy list)
		public int CountExceptUntouchables
		{
			get
			{
				int n = 0;
				foreach (Minion m in GetSpan())
					if (!m.Untouchable) n++;
				return n;
			}
		}

		/// <summary>HsServer (2026-10-08): any untouchable on this side now (live, not cached).</summary>
		private bool AnyUntouchable()
		{
			foreach (Minion m in GetSpan())
				if (m.Untouchable) return true;
			return false;
		}

		/// <summary>HsServer (2026-10-08): the minions on the board — not Dormant minions / permanents (rule BE2: "not considered minions") nor
		/// Locations. What "N minions" in a requirement or condition counts.</summary>
		public int MinionCount
		{
			get
			{
				int n = 0;
				foreach (Minion m in GetSpan())
					if (!m.Untouchable && !(m is Entities.Location)) n++;
				return n;
			}
		}
		public bool HasUntouchables => AnyUntouchable();

		public override bool IsFull => _count >= Game.MAX_MINIONS_ON_BOARD; // HsServer: >= so an overfull board never reads as having room

		/// <summary>HsServer (2026-10-08): a Magnetic minion played onto a Mech of a full board enters as an 8th minion for that moment,
		/// then magnetizes away (wiki Magnetic.txt:7 "can be played while the player's board is full, but only to combine it with another
		/// mech"; user 2026-10-08: it looks like magnetizing onto a board of 7). PlayCard.PlayMinion sets it around the minion's entry.</summary>
		internal bool AllowOverflow;
		protected override bool Overflowing => AllowOverflow;

		public override void Add(Minion entity, int zonePosition = -1)
		{
			// HsServer: wiki Dormant: "Minions will always start Dormant until their condition is fulfilled, no matter how they
			// were summoned" ("Dormant for N turns": until the start of its owner's Nth turn from now; "Starts Dormant": until its
			// condition, Generic.Awaken). "The exception is when the player copies a Dormant minion that has already awakened."
			bool sleeps = false;
			if (entity[HsTag.AWAKENED_COPY] > 0)
				entity[HsTag.AWAKENED_COPY] = 0;
			else
				sleeps = entity.Card.DormantTurns > 0 || entity.Card.StartsDormant;

			base.Add(entity, zonePosition);

			// (HsServer (TITANS): a Titan has hidden Charge — wiki Titan: "Titan minions effectively have Charge without the
			// Charge keyword"; live: no EXHAUSTED when it enters play)
			if (entity.Controller == Game.CurrentPlayer && !(entity is Location) && !Actions.Generic.IsTitan(entity)) // HsServer: a Location can be used the turn it's played
			{
				if (!entity.HasCharge)
				{
					if (entity.IsRush)
					{
						entity.AttackableByRush = true;
						Game.RushMinions.Add(entity.Id);
					}
					else
						entity.IsExhausted = true;
				}
			}

			entity.OrderOfPlay = Game.NextOop;

			ActivateAura(entity);

			for (int i = AdjacentAuras.Count - 1; i >= 0; i--)
				AdjacentAuras[i].BoardChanged = true;

			Game.TriggerManager.OnZoneTrigger(entity);

			if (entity.Untouchable)
			{
				++_untouchableCount;
				_hasUntouchables = true;
			}

			// HsServer: it goes Dormant in its own block once it is on the board (live: after it entered play)
			if (sleeps)
				Actions.Generic.EnterDormant(entity);
		}

		internal static void InitialiseMinion(Minion entity)
		{
			if (entity.Game.CurrentPlayer != entity.Controller) return;
			if (entity.HasCharge || Actions.Generic.IsTitan(entity)) return; // HsServer (TITANS): a Titan's hidden Charge
			if (entity.IsRush)
			{
				entity.AttackableByRush = true;
				entity.Game.RushMinions.Add(entity.Id);
			}
			else
				entity.IsExhausted = true;
		}

		public override Minion Remove(Minion entity)
		{
			RemoveAura(entity);
			for (int i = 0; i < AdjacentAuras.Count; i++)
				AdjacentAuras[i].BoardChanged = true;
			bool dormant = entity[GameTag.DORMANT] > 0;
			if (entity.Untouchable && --_untouchableCount == 0)
				_hasUntouchables = false;
			Minion removed = base.Remove(entity);
			if (dormant) // HsServer: leaving the board ends Dormant (played again, it sleeps again)
				Actions.Generic.ClearDormant(entity);
			return removed;
		}

		//internal override void ChangeEntity(Minion oldEntity, Minion newEntity)
		//{
		//	if (oldEntity.Untouchable)
		//		if (!newEntity.Untouchable && --_untouchableCount == 0)
		//			_hasUntouchables = false;

		//	base.ChangeEntity(oldEntity, newEntity);
		//}

		/// <summary>
		/// Replaces an entity in this zone to new entity and returns the old entity.
		/// The position of the new entity is the same position the old entity had.
		/// </summary>
		/// <param name="oldEntity">The entity to be replaced.</param>
		/// <param name="newEntity">The new entity.</param>
		/// <returns></returns>
		public void Replace(Minion oldEntity, Minion newEntity)
		{
			int pos = oldEntity.ZonePosition;

			// Remove old Entity
			RemoveAura(oldEntity);
			for (int i = 0; i < Auras.Count; i++)
				Auras[i].EntityRemoved(oldEntity);
			oldEntity.ActivatedTrigger?.Remove();
			if (oldEntity.Untouchable && --_untouchableCount == 0)
				_hasUntouchables = false;
			oldEntity.ZonePosition = 0;
			oldEntity.Controller.SetasideZone.Add(oldEntity);

			// Add new Entity
			newEntity.OrderOfPlay = Game.NextOop;
			_entities[pos] = newEntity;
			if (newEntity.Game.History)
				newEntity[GameTag.ZONE] = (int)Type;
			newEntity.Zone = this;
			newEntity.ZonePosition = pos;
			ActivateAura(newEntity);
			if (newEntity.Untouchable)
			{
				++_untouchableCount;
				_hasUntouchables = true;
			}
			Auras.ForEach(a => a.EntityAdded(newEntity));
			AdjacentAuras.ForEach(a => a.BoardChanged = true);
			if (!newEntity.HasCharge && !Actions.Generic.IsTitan(newEntity)) // HsServer (TITANS): a Titan's hidden Charge
			{
				if (newEntity.IsRush)
				{
					newEntity.AttackableByRush = true;
					Game.RushMinions.Add(newEntity.Id);
				}
				else
					newEntity.IsExhausted = true;
			}
		}

		/// <summary>
		/// Activates a <see cref="Minion"/>'s <see cref="Triggers.Trigger"/> and <see cref="Aura"/> and
		/// applies it's Spell Power increment.
		/// </summary>
		/// <param name="entity"></param>
		public static void ActivateAura(Minion entity)
		{
			// HsServer (2026-10-09): a scenario that squelches the cards' triggers and modifiers (Bob's Tavern: Generic.Squelched)
			if (Actions.Generic.Squelched(entity)) return;
			entity.Power?.Trigger?.Activate(entity);
			entity.Power?.Aura?.Activate(entity);
		}

		private static void RemoveAura(Minion entity)
		{
			entity.OngoingEffect?.Remove();
		}

		/// <summary>
		/// HsServer (2026-10-08): enumerating the board (foreach, LINQ) skips untouchables like GetAll — rule BE2 (Advanced_rulebook.txt:1019):
		/// "Permanents and Dormant minions are not considered minions and cannot be affected by any effects from either player unless
		/// otherwise specified". Code that means them asks for them: GetSpan() / GetDormant() (Starship launchpad, awakening).
		/// </summary>
		public override IEnumerator<Minion> GetEnumerator()
		{
			if (!AnyUntouchable())
			{
				foreach (Minion m in GetSpan().ToArray()) yield return m;
				yield break;
			}
			foreach (Minion m in GetSpan().ToArray())
				if (!m.Untouchable) yield return m;
		}

		/// <summary>
		/// Gets all board minions except untouchables(dormant).
		/// </summary>
		/// <returns></returns>
		public override Minion[] GetAll()
		{
			return HasUntouchables ? GetAll(null) : base.GetAll();
		}

		/// <summary>
		/// Gets all board minions satisfying the given predicate except untouchables(dormant).
		/// </summary>
		/// <param name="predicate"></param>
		/// <returns></returns>
		public override Minion[] GetAll(Func<Minion, bool> predicate)
		{
			if (AnyUntouchable())
			{
				if (predicate == null)
					predicate = p => !p.Untouchable;
				else
				{
					Func<Minion, bool> predicate1 = predicate;
					predicate = p => predicate1(p) && !p.Untouchable;
				}
			}
			return base.GetAll(predicate);
		}

		internal override void CopyTo(Array destination, int index)
		{
			if (AnyUntouchable())
			{
				Minion[] touchable = GetAll(null);
				Array.Copy(touchable, 0, destination, index, touchable.Length);
				return;
			}

			base.CopyTo(destination, index);
		}

		/// <summary>HsServer: this side's Dormant minions (GetAll leaves untouchables out, so it never sees them).</summary>
		public Minion[] GetDormant() => GetSpan().ToArray().Where(m => m[GameTag.DORMANT] > 0).ToArray();

		/// <summary>HsServer: counts down this player's turn-counted Dormant minions at the start of their turn (wiki Dormant:
		/// "trigger the countdown to their awakening at the start of the controller's turn"); a condition Dormant (The Darkness)
		/// waits for its condition (Generic.Awaken).</summary>
		internal void AwakenDormant()
		{
			foreach (Minion m in GetSpan().ToArray())
				if (m[GameTag.DORMANT] > 0 && m[HsTag.DORMANT_TIMED] > 0)
					Actions.Generic.CountDormantTurn(m); // live: its counter's block, the turn's first
		}

		/// <summary>HsServer: recount untouchables after an effect changed one directly (Incarceration makes a minion Dormant).</summary>
		public void RecountUntouchables()
		{
			_untouchableCount = GetSpan().ToArray().Count(m => m.Untouchable);
			_hasUntouchables = _untouchableCount > 0;
		}

		internal void DecrementUntouchablesCount()
		{
			if (!_hasUntouchables) return;
			if (--_untouchableCount == 0)
				_hasUntouchables = false;
		}

		public void Stamp(BoardZone zone)
		{
			zone._hasUntouchables = _hasUntouchables;
			zone._untouchableCount = _untouchableCount;
			zone._count = _count;

			Minion[] entities = _entities;
			Minion[] src = zone._entities;
			for (int i = 0; i < _count; ++i)
			{
				Minion copy = (Minion)entities[i].Clone(zone.Controller);
				copy.Zone = zone;
				src[i] = copy;
			}
		}
	}
}
