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
using SabberStoneCore.Enums;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Enchants
{
	public enum EffectOperator
	{
		ADD, SUB, MUL, SET,
		/// <summary>HsServer: subtract, but not below 1 ("costs (1) less, but not less than 1"). Cost auras only.</summary>
		SUB_NOT_BELOW_1,
		/// <summary>HsServer (TITANS): add, but not above 10 ("Your next card costs (1) more (up to 10)": Frost Plague); a card
		/// already costing 10 or more is unchanged. Cost auras only.</summary>
		ADD_NOT_ABOVE_10
	}

	/// <summary>
	/// Defines methods for tags value variation.
	/// </summary>
	public interface IEffect
	{
		GameTag Tag { get; }
		EffectOperator Operator { get; }
		int Value { get; }


		void ApplyTo(IEntity entity, bool isOneTurnEffect = false);
		void ApplyAuraTo(IPlayable playable);
		//void ApplyTo(AuraEffects auraEffects);
		//void ApplyTo(ControllerAuraEffects controllerAuraEffects);

		void RemoveFrom(IEntity entity);
		void RemoveAuraFrom(IPlayable playable);
		//void RemoveFrom(AuraEffects auraEffects);
		//void RemoveFrom(ControllerAuraEffects controllerAuraEffects);

		IEffect ChangeValue(int newValue);
	}

	/// <summary>
	///	A structure for tag value variation.
	/// </summary>
	public readonly struct Effect : IEffect, IEquatable<Effect>
	{
		public readonly GameTag Tag;
		public readonly EffectOperator Operator;
		public readonly int Value;

		/// <summary>
		/// Create a new Effect. An Effect consists of <see cref="GameTag"/>, <see cref="EffectOperator"/>, and <see cref="int"/> value.
		/// </summary>
		/// <param name="tag">The <see cref="GameTag"/> to be affected.</param>
		/// <param name="operator">The operation this effect performs.</param>
		/// <param name="value">The right operand of the operator.</param>
		public Effect(GameTag tag, EffectOperator @operator, int value)
		{
			Tag = tag;
			Operator = @operator;
			Value = value;
		}

		/// <summary>
		/// Apply this effect to the target entity.
		/// </summary>
		/// <summary>HsServer: an Attack / Health gain on a character that doubles its gains (client tag 2028, Saidan the Scarlet:
		/// "Whenever this minion gains Attack or Health, double that amount (wherever this is)") counts twice.</summary>
		private int Gain(IEntity entity) => Value > 0 && entity[HsTag.DOUBLES_STAT_GAINS] > 0 ? Value * 2 : Value;

		public void ApplyTo(IEntity entity, bool oneTurnEffect = false)
		{
			// HsServer: a card's Cost lives in its cost manager (Playable.Cost), not in the COST tag: a plain COST effect goes
			// through the Cost attribute like Effects.AddCost / SetCost / ReduceCost (it wrote a tag nothing reads: Tenwu).
			if (Tag == GameTag.COST && entity is Playable)
			{
				((IEffect)Cost.Effect(Operator, Value)).ApplyTo(entity, oneTurnEffect);
				return;
			}
			// HsServer: a character's Attack / Health live in its own fields (Character AttackDamage / BaseHealth), not in
			// NativeTags: a plain ATK / HEALTH effect wrote a tag nothing reads (Volcoross's +N/+N, Vampiric Blood's Health),
			// while RemoveFrom took the stat off through the indexer. Both go through the indexer now.
			if (entity is Character && (Tag == GameTag.ATK || Tag == GameTag.HEALTH) && (Operator == EffectOperator.ADD || Operator == EffectOperator.SUB))
			{
				if (oneTurnEffect)
					entity.Game.OneTurnEffects.Add((entity.Id, this));
				entity[Tag] = Operator == EffectOperator.ADD ? entity[Tag] + Gain(entity) : entity[Tag] - Value;
				if (Operator == EffectOperator.ADD && Value > 0) Actions.Generic.NoteStatGain(entity); // HsServer (VIOLET_HOLD): Dalaran Champion
				if (Tag == GameTag.ATK && Operator == EffectOperator.ADD && Value > 0 && entity is Character attacker && (attacker is Hero || attacker.Zone?.Type == Zone.PLAY))
					attacker.RaiseAttackGained(Value);
				return;
			}
			if (!entity.NativeTags.ContainsKey(Tag))
				entity.NativeTags.Add(Tag, entity.Card[Tag]);

			if (oneTurnEffect)
				entity.Game.OneTurnEffects.Add((entity.Id, this));

			switch (Operator)
			{
				case EffectOperator.ADD:
					entity.NativeTags[Tag] += Value;
					// HsServer: "gain Attack with your hero" (Druid Questline) — an effect giving a hero Attack
					if (Tag == GameTag.ATK && Value > 0 && entity is Character gainer && (gainer is Hero || gainer.Zone?.Type == Zone.PLAY))
						gainer.RaiseAttackGained(Value);
					break;
				case EffectOperator.SUB:
					entity.NativeTags[Tag] -= Value;
					break;
				case EffectOperator.MUL:
					entity.NativeTags[Tag] *= Value;
					break;
				case EffectOperator.SET:
					// experimental implmentation for simulating tricky situations
					switch (Tag)
					{
						// (HsServer: a hero can get these too — Al'Akir's SWAT, INSECTS!: "your hero … Windfury this turn"; it threw)
						case GameTag.CHARGE:
						case Actions.Generic.NON_KEYWORD_CHARGE: // HsServer: the same, without the keyword (a temporary steal)
							{
								if (!(entity is Minion m)) break;
								if (m.IsExhausted && m.NumAttacksThisTurn == 0)
									m.IsExhausted = false;
								if (m.AttackableByRush)
									m.AttackableByRush = false;
								break;
							}
						case GameTag.WINDFURY:
							{
								if (entity is Character ch && ch.NumAttacksThisTurn > 0 && ch.NumAttacksThisTurn < 2 && ch.IsExhausted)
									ch.IsExhausted = false;
								break;
							}
						case GameTag.MEGA_WINDFURY: // HsServer (2026-10-08): gained after attacking (Generic.RefreshAttackReady)
							{
								if (entity is Character mega) Actions.Generic.RefreshAttackReady(mega);
								break;
							}
						case GameTag.TAUNT:
							((Character) entity).HasTaunt = Value > 0;
							return;
						case GameTag.IMMUNE:
							if (entity is Character c)
								c.IsImmune = Value > 0;
							else
								break;
							return;
						case GameTag.RUSH:
						{
							var m = (Minion)entity;
							if (m.IsExhausted)
							{
								if (m.HasWindfury)
								{
									if (m.NumAttacksThisTurn < 2)
									{
										m.IsExhausted = false;
										m.AttackableByRush = true;
									}
								}
								else
								{
									if (m.NumAttacksThisTurn == 0)
									{
										m.IsExhausted = false;
										m.AttackableByRush = true;
									}
								}
							}
							break;
						}
					}

					if (oneTurnEffect && entity.NativeTags[Tag] == Value)
						entity.Game.OneTurnEffects.Remove((entity.Id, this));

					entity.NativeTags[Tag] = Value;

					break;
	
				default:
					throw new ArgumentException();
			}
		}

		/// <summary>
		/// Apply this effect to the target as an aura effect.
		/// </summary>
		public void ApplyAuraTo(IPlayable playable)
		{
			AuraEffects auraEffects = playable.AuraEffects;
			if (auraEffects == null)
			{
				auraEffects = new AuraEffects(playable.Card.Type);
				playable.AuraEffects = auraEffects;
			}

			switch (Operator)
			{
				case EffectOperator.ADD:
					auraEffects[Tag] += Value;
					return;
				case EffectOperator.SUB:
					auraEffects[Tag] -= Value;
					return;
				// TODO: SET Aura
				case EffectOperator.SET:
					//playable[Tag] = 0;
					auraEffects[Tag] = Value;

					if (playable is Minion m)
					{
						switch (Tag)
						{
							case GameTag.CHARGE:
								if (m.IsExhausted && m._numAttackThisTurn < 1)
									m.IsExhausted = false;
								if (m.AttackableByRush)
									m.AttackableByRush = false;
								break;
							case GameTag.RUSH:
								if (m.IsExhausted && m._numAttackThisTurn == 0)
								{
									m.IsExhausted = false;
									m.AttackableByRush = true;
									playable.Game.RushMinions.Add(playable.Id);
								}
								break;
							case GameTag.HEALTH_MINIMUM:
								m[GameTag.HEALTH_MINIMUM] = Value;
								break;
						}
					}
					return;
				default:
					throw new NotImplementedException();
			}
		}

		/// <summary>
		/// Apply this effect to the target controller as an aura effect.
		/// </summary>
		public void ApplyTo(ControllerAuraEffects auraEffects)
		{
			switch (Operator)
			{
				case EffectOperator.ADD:
				case EffectOperator.SET:
					auraEffects[Tag] += Value;
					return;
				case EffectOperator.SUB:
					auraEffects[Tag] += Value;
					return;
				default:
					throw new NotImplementedException();
			}
		}

		/// <summary>
		/// Remove this effect from the target entity.
		/// </summary>
		public void RemoveFrom(IEntity entity)
		{
			if (Tag == GameTag.COST && entity is Playable)
			{
				((IEffect)Cost.Effect(Operator, Value)).RemoveFrom(entity);
				return;
			}
			switch (Operator)
			{
				case EffectOperator.ADD:
					entity[Tag] -= entity is Character && (Tag == GameTag.ATK || Tag == GameTag.HEALTH) ? Gain(entity) : Value;
					return;
				case EffectOperator.SUB:
					entity[Tag] = entity.NativeTags[Tag] + Value;
					return;
				case EffectOperator.SET:
					entity[Tag] = 0;
					entity.NativeTags.Remove(Tag);		// unstable
					if (entity.Game.History)
						entity.Game.PowerHistory.Add(
							Kettle.PowerHistoryBuilder
								.TagChange(entity.Id, Tag, entity.Card[Tag]));
					// HsServer (2026-10-08): losing Charge / Windfury / Mega-Windfury (Generic.RefreshAttackReady)
					if (Tag is GameTag.CHARGE or GameTag.WINDFURY or GameTag.MEGA_WINDFURY or Actions.Generic.NON_KEYWORD_CHARGE && entity is Character lost)
						Actions.Generic.RefreshAttackReady(lost);
					return;
			}
		}

		/// <summary>
		/// Remove ths aura effect from the target entity.
		/// </summary>
		public void RemoveAuraFrom(IPlayable playable)
		{
			switch (Operator)
			{
				case EffectOperator.ADD:
					playable.AuraEffects[Tag] -= Value;
					return;
				case EffectOperator.SUB:
					playable.AuraEffects[Tag] += Value;
					return;
				case EffectOperator.SET:
					playable.AuraEffects[Tag] -= Value;
					// HsServer (2026-10-08): an aura's Charge / Windfury / Mega-Windfury gone (Tundra Rhino died: Generic.RefreshAttackReady)
					if (Tag is GameTag.CHARGE or GameTag.WINDFURY or GameTag.MEGA_WINDFURY && playable is Character auraLost)
						Actions.Generic.RefreshAttackReady(auraLost);
					if (Tag == GameTag.RUSH)
					{
						var m = (Minion)playable;
						if (m.AttackableByRush && !m.IsExhausted)
						{
							if (m.IsRush || m.Card.Rush)
								return;

							m.AttackableByRush = false;
							m.IsExhausted = true;
							m.Game.RushMinions.Remove(m.Id);
						}
					}
					else if
						(Tag == GameTag.HEALTH_MINIMUM)
					{
						playable.NativeTags.Remove(GameTag.HEALTH_MINIMUM);
					}
					return;
			}
		}

		public void RemoveFrom(ControllerAuraEffects auraEffects)
		{
			switch (Operator)
			{
				case EffectOperator.ADD:
				case EffectOperator.SET:
					auraEffects[Tag] -= Value;
					return;
				case EffectOperator.SUB:
					auraEffects[Tag] += Value;
					return;
				default:
					throw new NotImplementedException();
			}
		}

		/// <summary>
		/// Creates a new Effect having changed amount of <see cref="Value"/>.
		/// </summary>
		public IEffect ChangeValue(int newValue)
		{
			return new Effect(Tag, Operator, newValue);
		}

		GameTag IEffect.Tag => Tag;
		EffectOperator IEffect.Operator => Operator;
		int IEffect.Value => Value;

		public bool Equals(Effect other)
		{
			return Tag == other.Tag && Operator == other.Operator && Value == other.Value;
		}

		public override bool Equals(object obj)
		{
			if (obj is null) return false;
			return obj is Effect effect && Equals(effect);
		}

		public static bool operator ==(Effect e1, Effect e2) => e1.Equals(e2);

		public static bool operator !=(Effect e1, Effect e2) => !(e1.Equals(e2));

		public override int GetHashCode()
		{
			unchecked
			{
				var hashCode = (int) Tag;
				hashCode = (hashCode * 397) ^ (int) Operator;
				hashCode = (hashCode * 397) ^ Value;
				return hashCode;
			}
		}
		public override string ToString()
		{
			return $"[{Operator} {Tag} {Value}]";
		}
	}
}
