using System;
using SabberStoneCore.Enums;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Enchants
{
	/// <summary>HsServer: the stat an effect changes and how (Generic.ChangeEntityCore: an in-hand change keeps an ADDed buff on top).</summary>
	internal interface IStatEffect
	{
		GameTag? StatTag { get; }
		EffectOperator Operator { get; }
	}

	internal readonly struct GenericEffect<TAttr, T> : IEffect, IStatEffect where TAttr : Attr<T> where T : Playable
	{
		public GameTag? StatTag => typeof(TAttr) == typeof(ATK) ? GameTag.ATK : typeof(TAttr) == typeof(Health) ? GameTag.HEALTH : (GameTag?)null;
		public EffectOperator Operator => _operator;

		private readonly TAttr _attr;
		private readonly EffectOperator _operator;
		private readonly int _value;

		internal GenericEffect(TAttr attr, EffectOperator @operator, int value)
		{
			_attr = attr;
			_operator = @operator;
			_value = value;
		}

		public void ApplyTo(T entity)
		{
			_attr.Apply(entity, _operator, _value);
		}

		/// <summary>HsServer: this Health effect as the Durability effect for a weapon, else null. (TIME_TRAVEL: an Attack effect as the
		/// weapon's ATK tag effect too — the character Attack attribute has no weapon slot, so a one-turn "+N Attack this turn" on a
		/// weapon (Hand of Infinity) couldn't be removed.)</summary>
		private IEffect WeaponDurability(IEntity entity) =>
			entity is Model.Entities.Weapon && (_operator == EffectOperator.ADD || _operator == EffectOperator.SUB)
				? typeof(TAttr) == typeof(Health) ? new Effect(Enums.GameTag.DURABILITY, _operator, _value)
				: typeof(TAttr) == typeof(ATK) ? new Effect(Enums.GameTag.ATK, _operator, _value) : null
				: null;

		public void ApplyAuraTo(T playable)
		{
			_attr.ApplyAura(playable, _operator, _value);
		}

		public void RemoveFrom(T entity)
		{
			_attr.Remove(entity, _operator, _value);
		}

		public void RemoveAuraFrom(T playable)
		{
			_attr.RemoveAura(playable, _operator, _value);
		}

		void IEffect.ApplyTo(IEntity entity, bool oneTurnEffect)
		{
			// HsServer: a weapon's "Health" is its Durability ("give it +1/+1": Obsidiansmith's Dredged weapon)
			if (WeaponDurability(entity) is IEffect durability) // (a weapon only)
			{
				durability.ApplyTo(entity, oneTurnEffect);
				return;
			}
			if (!(entity is T playable))
				throw new Exception($"Cannot apply {this} to an entity of type {entity.GetType()}");

			if (oneTurnEffect)
				playable.Game.OneTurnEffects.Add((playable.Id, this));

			ApplyTo(playable);

			// HsServer: "gain Attack with your hero" (Druid Questline) — character Attack goes through this effect
			if (typeof(TAttr) == typeof(ATK) && _operator == EffectOperator.ADD && _value > 0 && playable is Hero hero)
				hero.RaiseAttackGained(_value);
		}

		void IEffect.ApplyAuraTo(IPlayable playable)
		{
			if (!(playable is T p))
				throw new Exception($"Cannot apply {this} to an entity of type {playable.GetType()}");

			ApplyAuraTo(p);
		}

		void IEffect.RemoveFrom(IEntity entity)
		{
			if (WeaponDurability(entity) is IEffect durability) // (a weapon only)
			{
				durability.RemoveFrom(entity);
				return;
			}
			if (!(entity is T playable))
				throw new Exception();

			RemoveFrom(playable);
		}

		void IEffect.RemoveAuraFrom(IPlayable playable)
		{
			if (!(playable is T p))
				throw new Exception();

			RemoveAuraFrom(p);
		}

		public IEffect ChangeValue(int newValue)
		{
			return new GenericEffect<TAttr, T>(_attr, _operator, newValue);
		}

		GameTag IEffect.Tag => _attr.Tag;
		EffectOperator IEffect.Operator => _operator;
		int IEffect.Value => _value;

		public override string ToString()
		{
			return $"{_operator} {_attr} {_value}";
		}
	}

	internal abstract class Attr<T> where T : Playable
	{
		public abstract void Apply(T entity, EffectOperator @operator, int value);
		public abstract void ApplyAura(T entity, EffectOperator @operator, int value);
		public abstract void Remove(T entity, EffectOperator @operator, int value);
		public abstract void RemoveAura(T entity, EffectOperator @operator, int value);
		public abstract GameTag Tag { get; }

		protected abstract ref int GetAuraRef(AuraEffects auraEffects);
	}

	internal abstract class IntAttr<T> : Attr<T> where T : Playable
	{
		protected abstract ref int? GetRef(T entity);

		protected abstract int GetCardValue(T entity);

		public override void Apply(T entity, EffectOperator @operator, int value)
		{
			ref int? target = ref GetRef(entity);

			if (@operator == EffectOperator.SET)
			{
				target = value;
				return;
			}

			int baseValue = target ?? GetCardValue(entity);

			switch (@operator)
			{
				case EffectOperator.ADD:
					target = baseValue + value;
					break;
				case EffectOperator.SUB:
					target = baseValue - value;
					break;
				case EffectOperator.MUL:
					target = baseValue * value;
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(@operator), @operator, null);
			}
		}

		public override void Remove(T entity, EffectOperator @operator, int value)
		{
			ref int? target = ref GetRef(entity);

			if (!target.HasValue)
				return;

			switch (@operator)
			{
				case EffectOperator.ADD:
					target -= value;
					break;
				case EffectOperator.SUB:
					target += value;
					break;
				case EffectOperator.SET:
					// HsServer: a set can't be undone from here (the value before it is gone): an enchantment that can end
					// restores it itself (Generic.EndStatSet); removing one threw (Celestial Aura's 10/10 leaving)
					return;
				default:
					throw new ArgumentOutOfRangeException(nameof(@operator), @operator, null);
			}
		}

		public override void ApplyAura(T entity, EffectOperator @operator, int value)
		{
			AuraEffects auraEffects = entity.AuraEffects;
			if (auraEffects == null)
			{
				auraEffects = new AuraEffects(entity.Card.Type);
				entity.AuraEffects = auraEffects;
			}

			ref int target = ref GetAuraRef(auraEffects);

			switch (@operator)
			{
				case EffectOperator.ADD:
					target += value;
					break;
				case EffectOperator.SUB:
					target -= value;
					break;
				case EffectOperator.MUL:
					target *= value;
					break;
				case EffectOperator.SET:
					GetRef(entity) = 0;
					target = value;
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(@operator), @operator, null);
			}
		}

		public override void RemoveAura(T entity, EffectOperator @operator, int value)
		{
			ref int target = ref GetAuraRef(entity.AuraEffects);

			switch (@operator)
			{
				case EffectOperator.ADD:
					target -= value;
					return;
				case EffectOperator.SUB:
					target += value;
					return;
				case EffectOperator.SET:
					target -= value;
					return;
				default:
					throw new NotImplementedException();
			}
		}
	}

	internal abstract class BoolAttr<T> : Attr<T> where T : Playable
	{
		protected abstract ref bool? GetRef(T entity);

		public override void Apply(T entity, EffectOperator @operator, int value)
		{
			ref bool? target = ref GetRef(entity);

			if (@operator != EffectOperator.SET)
				throw new ArgumentOutOfRangeException(nameof(@operator), @operator, null);

			target = value > 0;
		}

		public override void Remove(T entity, EffectOperator @operator, int value)
		{
			ref bool? target = ref GetRef(entity);

			target = false;
		}

		public override void ApplyAura(T entity, EffectOperator @operator, int value)
		{
			AuraEffects auraEffects = entity.AuraEffects;
			if (auraEffects == null)
			{
				auraEffects = new AuraEffects(entity.Card.Type);
				entity.AuraEffects = auraEffects;
			}

			GetAuraRef(auraEffects)++;
		}

		public override void RemoveAura(T entity, EffectOperator @operator, int value)
		{
			GetAuraRef(entity.AuraEffects)--;
		}
	}

	internal abstract class SelfContainedIntAttr<TSelf, TTarget> : IntAttr<TTarget>
		where TSelf : SelfContainedIntAttr<TSelf, TTarget>, new() where TTarget : Playable
	{
		private static readonly TSelf _singleton = new TSelf();

		public static GenericEffect<TSelf, TTarget> Effect(EffectOperator @operator, int value)
		{
			return new GenericEffect<TSelf, TTarget>(_singleton, @operator, value);
		}
	}

	internal abstract class SelfContainedBoolAttr<TSelf, TTarget> : BoolAttr<TTarget>
		where TSelf : SelfContainedBoolAttr<TSelf, TTarget>, new() where TTarget : Playable
	{
		private static readonly TSelf _singleton = new TSelf();

		public static GenericEffect<TSelf, TTarget> Effect(bool value = true)
		{
			return new GenericEffect<TSelf, TTarget>(_singleton, EffectOperator.SET, value ? 1 : 0);
		}
	}

	internal class Cost : SelfContainedIntAttr<Cost, Playable>
	{
		public override GameTag Tag => GameTag.COST;

		protected override ref int? GetRef(Playable entity)
		{
			return ref entity._modifiedCost;
		}

		protected override int GetCardValue(Playable entity)
		{
			return entity.Card.Cost;
		}

		protected override ref int GetAuraRef(AuraEffects auraEffects)
		{
			throw new NotImplementedException();
		}

		public override void Apply(Playable entity, EffectOperator @operator, int value)
		{
			base.Apply(entity, @operator, value);

			entity._costManager?.AddCostEnchantment(@operator, value, entity.Game.PeekOop);
		}

		public override void ApplyAura(Playable entity, EffectOperator @operator, int value)
		{
			Playable.CostManager costManager = entity._costManager;
			if (costManager == null)
			{
				costManager = new Playable.CostManager();
				entity._costManager = costManager;
			}

			costManager.AddCostAura(@operator, value, entity.Game.AuraOrder);
		}

		public override void RemoveAura(Playable entity, EffectOperator @operator, int value)
		{
			entity._costManager?.RemoveCostAura(@operator, value, entity.Game.AuraOrder);
		}
	}

	internal class ATK : SelfContainedIntAttr<ATK, Playable>
	{
		public override GameTag Tag => GameTag.ATK;

		protected override ref int? GetRef(Playable entity)
		{
			if (entity is Character c)
				return ref c._modifiedATK;
			throw new NotImplementedException();
		}

		protected override int GetCardValue(Playable entity)
		{
			return entity.Card.ATK;
		}

		protected override ref int GetAuraRef(AuraEffects auraEffects)
		{
			return ref auraEffects._data[3];
		}

		public override void Apply(Playable entity, EffectOperator @operator, int value)
		{
			if (@operator == EffectOperator.SET)
			{
				for (int i = entity.Game.OneTurnEffects.Count - 1; i >= 0; i--)
				{
					(int id, IEffect eff) = entity.Game.OneTurnEffects[i];
					if (id != entity.Id || !(eff is GenericEffect<ATK, Playable>)) continue;
					entity.Game.OneTurnEffects.RemoveAt(i);
				}
			}

			if (entity is Character)
				base.Apply(entity, @operator, value);
			else
				new Effect(Enums.GameTag.ATK, @operator, value).ApplyTo(entity);
		}

		//public override void ApplyAura(Playable entity, EffectOperator @operator, int value)
		//{
		//	if (entity is Weapon)
		//		entity = entity.Controller.Hero;

		//	base.ApplyAura(entity, @operator, value);
		//}

		//public override void RemoveAura(Playable entity, EffectOperator @operator, int value)
		//{
		//	if (entity is Weapon)
		//		entity = entity.Controller.Hero;

		//	base.RemoveAura(entity, @operator, value);
		//}
	}

	internal class Health : SelfContainedIntAttr<Health, Character>
	{
		public override GameTag Tag => GameTag.HEALTH;

		protected override ref int? GetRef(Character entity)
		{
			return ref entity._modifiedHealth;
		}

		protected override int GetCardValue(Character entity)
		{
			return entity.Card.Health;
		}

		protected override ref int GetAuraRef(AuraEffects auraEffects)
		{
			return ref auraEffects._data[4];
		}

		public override void Apply(Character entity, EffectOperator @operator, int value)
		{
			if (@operator == EffectOperator.SET)
			{
				if (entity is Hero h)
				{
					int hbh = h.BaseHealth;
					if (hbh > value)
						h.Damage = hbh - value;
					else
						h.Health = value;
					return;
				}

				if (entity is Minion m)
				{
					m.Health = value;
					return;
				}
			}

			base.Apply(entity, @operator, value);
		}

		public override void RemoveAura(Character entity, EffectOperator @operator, int value)
		{
			base.RemoveAura(entity, @operator, value);

			if (@operator == EffectOperator.ADD)
				entity.Damage -= value;
		}
	}

	internal class Stealth : SelfContainedBoolAttr<Stealth, Character>
	{
		public override GameTag Tag => GameTag.STEALTH;

		protected override ref bool? GetRef(Character entity)
		{
			return ref entity._modifiedStealth;
		}

		protected override ref int GetAuraRef(AuraEffects auraEffects)
		{
			throw new NotImplementedException();
		}
	}

	internal class Taunt : SelfContainedBoolAttr<Taunt, Minion>
	{
		public override GameTag Tag => GameTag.TAUNT;

		protected override ref bool? GetRef(Minion entity)
		{
			return ref entity._modifiedTaunt;
		}

		protected override ref int GetAuraRef(AuraEffects auraEffects)
		{
			return ref auraEffects._data[6];
		}
	}

	internal class CantBeTargetedBySpells : SelfContainedBoolAttr<CantBeTargetedBySpells, Character>
	{
		public override GameTag Tag => GameTag.CANT_BE_TARGETED_BY_SPELLS;

		protected override ref int GetAuraRef(AuraEffects auraEffects)
		{
			return ref auraEffects._data[2];
		}

		protected override ref bool? GetRef(Character entity)
		{
			return ref entity._modifiedCantBeTargetedBySpells;
		}
	}
}
