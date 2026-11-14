using System;
using System.Collections.Generic;
using SabberStoneCore.Auras;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;

namespace SabberStoneCore.Model.Entities
{
	public partial class Playable
	{
		internal class CostManager
		{
			// HsServer: rules M6 - cost auras and cost enchantments apply together in order of play (Naga Sea Witch then
			// Emperor Thaurissan: 4; Thaurissan then Naga: 5). Each modifier keeps its timestamp: an aura's is its
			// source's order of play, an enchantment's is when it was applied.
			private readonly List<(EffectOperator Operator, int Value, int Order)> _costEffects =
				new List<(EffectOperator, int, int)>();
			private readonly List<(EffectOperator Operator, int Value, int Order)> _costEnchantments =
				new List<(EffectOperator, int, int)>();

			private int _cachedValue;
			private bool _toBeUpdated;
			private AdaptiveCostEffect _adaptiveCostEffect;

			public CostManager()
			{
				_toBeUpdated = true;
			}

			public CostManager(AdaptiveCostEffect adaptiveEffect)
			{
				_adaptiveCostEffect = adaptiveEffect;
			}

			private CostManager(CostManager original)
			{
				_cachedValue = original._cachedValue;
				_toBeUpdated = original._toBeUpdated;
				_costEffects.AddRange(original._costEffects);
				_costEnchantments.AddRange(original._costEnchantments);
				_adaptiveCostEffect = original._adaptiveCostEffect;
			}

			public int CachedValue
			{
				get => _cachedValue;
				set => _cachedValue = value;
			}

			/// <summary>
			/// Apply a new Cost effect of an <see cref="Aura"/>
			/// </summary>
			public void AddCostAura(EffectOperator @operator, int value, int order = int.MaxValue)
			{
				_costEffects.Add((@operator, value, order));
				_toBeUpdated = true;
			}

			/// <summary>
			/// Remove an applied Cost effect of an <see cref="Aura"/>.
			/// </summary>
			/// <exception cref="KeyNotFoundException"></exception>
			public void RemoveCostAura(EffectOperator @operator, int value, int order = int.MaxValue)
			{
				// TODO: Fix the behaviour when adaptive cost effect + redraw
				int i = _costEffects.FindIndex(e => e.Operator == @operator && e.Value == value && e.Order == order);
				if (i < 0)
					i = _costEffects.FindIndex(e => e.Operator == @operator && e.Value == value);
				if (i < 0)
					return;
				_costEffects.RemoveAt(i);
				_toBeUpdated = true;
			}

			/// <summary>
			/// Activate <see cref="AdaptiveCostEffect"/> to calculate and reflect its result.
			/// </summary>
			public void ActivateAdaptiveEffect(AdaptiveCostEffect adaptiveCostEffect)
			{
				_adaptiveCostEffect = adaptiveCostEffect;
			}

			/// <summary>
			/// Tie in for <see cref="AdaptiveCostEffectObsolete"/> to calculate and reflect its result.
			/// </summary>
			public void UpdateAdaptiveEffect(int setValue = -1)
			{
				if (setValue > 0)
					_cachedValue = setValue;
				else
					_toBeUpdated = true;
			}

			public void DeactivateAdaptiveEffect()
			{
				_adaptiveCostEffect = null;
			}

			/// <summary>
			/// Add a new permanent cost enchantment effect
			/// </summary>
			public void AddCostEnchantment(EffectOperator @operator, int value, int order = int.MaxValue)
			{
				_costEnchantments.Add((@operator, value, order));
				_toBeUpdated = true;
			}

			/// <param name="c">the cost with enchantments (the entity's modified cost)</param>
			/// <param name="cardCost">the card's own cost</param>
			public int GetCost(int c, int cardCost)
			{
				int cost = _toBeUpdated ? GetCostInternal(c, cardCost) : _cachedValue;

				return cost > 0 ? cost : 0;
			}

			internal void QueueUpdate()
			{
				_toBeUpdated = true;
			}

			/// <summary>
			/// Applies older entity's cost enchantments to the new one.
			/// </summary>
			/// <param name="newCardCost"></param>
			/// <returns></returns>
			internal int EntityChanged(int newCardCost)
			{
				int cardCost = newCardCost;
				for (int i = 0; i < _costEnchantments.Count; i++)
				{
					(EffectOperator @operator, int value, _) = _costEnchantments[i];
					switch (@operator)
					{
						case EffectOperator.SUB:
							newCardCost -= value;
							break;
						case EffectOperator.ADD:
							newCardCost += value;
							break;
						case EffectOperator.SET:
							newCardCost = value;
							break;
					}
				}

				newCardCost = GetCostInternal(newCardCost, cardCost);

				return newCardCost > 0 ? newCardCost : 0;
			}

			private static int Fold(int c, IEnumerable<(EffectOperator Operator, int Value, int Order)> effects)
			{
				foreach ((EffectOperator Operator, int Value, int Order) e in effects)
					switch (e.Operator)
					{
						case EffectOperator.ADD:
							c += e.Value;
							break;
						case EffectOperator.SUB:
							c -= e.Value;
							break;
						case EffectOperator.SET:
							c = e.Value;
							break;
						case EffectOperator.SUB_NOT_BELOW_1: // HsServer
							if (c > 1) c = Math.Max(1, c - e.Value);
							break;
						case EffectOperator.ADD_NOT_ABOVE_10: // HsServer (TITANS)
							if (c < 10) c = Math.Min(10, c + e.Value);
							break;
						default:
							throw new ArgumentOutOfRangeException();
					}
				return c;
			}

			private int GetCostInternal(int c, int cardCost)
			{
				// HsServer: rules M6 - when the enchanted cost is exactly the card cost + its cost enchantments (nothing set
				// the cost directly), rebuild it from the enchantments and the aura effects together, in order of play.
				// Otherwise: the enchanted cost first, then the aura effects (e.g. Naga Sea Witch, Sorcerer's Apprentice).
				if (_costEnchantments.Count > 0 && _costEffects.Count > 0 && Fold(cardCost, _costEnchantments) == c)
				{
					var all = new List<(EffectOperator Operator, int Value, int Order)>(_costEnchantments.Count + _costEffects.Count);
					all.AddRange(_costEnchantments);
					all.AddRange(_costEffects);
					c = Fold(cardCost, System.Linq.Enumerable.OrderBy(all, e => e.Order)); // stable: ties keep enchantments first
				}
				else
					c = Fold(c, _costEffects);

				// 3. Lastly apply Adaptive Cost Effect (e.g. Giants)
				c = _adaptiveCostEffect?.Apply(c) ?? c;

				_cachedValue = c;

				_toBeUpdated = false;

				return c;
			}

			public CostManager Clone()
			{
				return new CostManager(this);
			}
		}

		internal CostManager _costManager;
		internal int? _modifiedCost;

		/// <summary>HsServer (TITANS): the cost before a cost floor (Razorscale) raises it — what its "Costs at least (2)"
		/// enchantment is shown on (Generic.CostFloorShownAura).</summary>
		public int CostBeforeFloor
		{
			get
			{
				int cost = _costManager?.GetCost(_modifiedCost ?? (_modifiedCost = Card.Cost).Value, Card.Cost) ??
				           (_modifiedCost.HasValue ? _modifiedCost < 0 ? 0 : _modifiedCost.Value : (_modifiedCost = Card.Cost).Value);
				// HsServer: rules — an Echo card's cost can't go below 1 (no infinite 0-cost loop). "Repeatable this turn"
				// cards aren't Echo and can reach 0.
				return cost < 1 && Card.Cost >= 1 && IsEcho ? 1 : cost;
			}
		}

		public int Cost
		{
			get
			{
				int cost = Actions.Generic.PlayerCostRule(this, CostBeforeFloor); // HsServer (Dalaran Heist): "Your cards cost half"
				// HsServer (TITANS): a cost floor (Razorscale: "Cards can't cost less than (2)") comes last — Advanced rulebook:
				// "applied after all other cost-reducing Enchantments so that no card can cost less than 2" (Generic.CostFloor)
				if (!(this is HeroPower) && Controller != null)
				{
					int floor = Actions.Generic.CostFloor(Controller);
					if (cost < floor) cost = floor;
				}
				return cost;
			}
			set
			{
				_modifiedCost = value;

				if (_history)
					Game.PowerHistory.Add(PowerHistoryBuilder.TagChange(Id, GameTag.COST, Cost));
			}
		}

		internal void ResetCost()
		{
			_costManager = null;
			_modifiedCost = null;
			if (OngoingEffect is AdaptiveCostEffect ace)
				ace.Remove();

			if (_history)
				Game.PowerHistory.Add(PowerHistoryBuilder.TagChange(Id, GameTag.COST, Card.Cost));
		}

		public override int this[GameTag t]
		{
			get => t == GameTag.COST ? Cost : base[t];
			set
			{
				if (t == GameTag.COST)
					Cost = value;
				else
					base[t] = value;
			}
		}
	}
}
