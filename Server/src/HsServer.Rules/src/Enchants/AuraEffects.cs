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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

// ReSharper disable InconsistentNaming

namespace SabberStoneCore.Enchants
{
	/// <summary>
	/// A simple container for saving tag value perturbations from external Auras. Call indexer to get value for a particular Tag.
	/// </summary>
	public class AuraEffects : IEquatable<AuraEffects>
	{
		private const int PlayableLength = 2;
		private const int WeaponLength = PlayableLength + 2; // HsServer: + ATK (index 3, as a character's): "Your weapon has +2 Attack" auras (Vulpera Toxinblade)
		private const int CharacterLength = PlayableLength + 2;
		private const int HeroLength = CharacterLength + 4; // HsServer: +1 IgnoreTaunt
		private const int MinionLength = CharacterLength + 8; // HsServer: +1 IgnoreTaunt

		// Indices:
		// Playables
		// 0 : CardCostHealth
		// 1 : Echo
		// Weapon
		// 2 : Immune
		// 3 : ATK (HsServer)
		// Characters
		// 2 : CantBeTargetedBySpells
		// 3 : ATK
		// Hero
		// 4 : CannotAttackHeroes
		// 5 : Immune
		// 6 : HEROPOWER_DAMAGE
		// Minion
		// 4 : Health
		// 5 : Charge
		// 6 : Taunt
		// 7 : Lifesteal
		// 8 : Rush
		// 9 : CantAttack
		// 10 : IgnoreTaunt (HsServer; hero: 7)
		// 11 : Immune (HsServer: "Immune while you control a Whelp", Raid Boss Onyxia)
		internal readonly int[] _data;

		internal AuraEffects(CardType type)
		{
			switch (type)
			{
				case CardType.HERO:
					_data = new int[HeroLength];
					break;
				case CardType.LOCATION: // HsServer: a Location is a Minion entity; it uses the minion layout
					type = CardType.MINION;
					_data = new int[MinionLength];
					break;
				case CardType.MINION:
					_data = new int[MinionLength];
					break;
				case CardType.WEAPON:
					_data = new int[WeaponLength];
					break;
				case CardType.SPELL:
					_data = new int[PlayableLength];
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(type), type, null);
			}

			Type = type;
		}

		private AuraEffects(AuraEffects original) : this (original.Type)
		{
			Buffer.BlockCopy(original._data, 0, _data, 0, _data.Length * sizeof(int));
			if (original._other != null) _other = new Dictionary<GameTag, int>(original._other);
		}

		/// <summary>HsServer: any other tag an aura grants (CANT_BE_FROZEN, MEGA_WINDFURY, internal rule tags...).</summary>
		private Dictionary<GameTag, int> _other;

		public readonly CardType Type;

		public bool CardCostHealth
		{
			get => _data[0] > 0;
			set => _data[0] = value ? 1 : 0;
		}

		public bool Echo
		{
			get => _data[1] > 0;
			set => _data[1] = value ? 1 : 0;
		}

		public bool CantBeTargetedBySpells
		{
			get => _data[2] > 0;
			set => _data[2] = value ? 1 : 0;
		}

		public int ATK
		{
			get => _data[3];
			set => _data[3] = value;
		}

		// HsServer (livecheck 2026-10-10): slots 4-9 are a minion's (the layout above); a hero's slots 4-7 hold other things, so
		// the minion properties read nothing on a hero (Fallen Hero's HEROPOWER_DAMAGE, slot 6, made the hero Taunt: live
		// session-20261002-145651 5025; a "can't attack heroes" aura, slot 4, gave a hero +1 Health)
		private bool MinionSlots => Type == CardType.MINION;

		public int Health
		{
			get => MinionSlots ? _data[4] : 0;
			set { if (MinionSlots) _data[4] = value; }
		}

		public int Charge
		{
			get => MinionSlots ? _data[5] : 0;
			set { if (MinionSlots) _data[5] = value; }
		}

		/// <summary>Taunt from an aura: a minion's slot, or the tag (any character: the indexer keeps it with the other tags).</summary>
		public bool Taunt
		{
			get => MinionSlots && _data[6] > 0 || _other != null && _other.TryGetValue(GameTag.TAUNT, out int v) && v > 0;
			set { if (MinionSlots) _data[6] = value ? 1 : 0; }
		}

		public bool Lifesteal
		{
			get => MinionSlots && _data[7] > 0;
			set { if (MinionSlots) _data[7] = value ? 1 : 0; }
		}

		public bool Rush
		{
			get => MinionSlots && _data[8] > 0;
			set { if (MinionSlots) _data[8] = value ? 1 : 0; }
		}

		public bool CantAttack
		{
			get => MinionSlots && _data[9] > 0;
			set { if (MinionSlots) _data[9] = value ? 1 : 0; }
		}

		/// <summary>HsServer: IGNORE_TAUNT (433) from an aura (Kayn Sunfury); heroes and minions only.</summary>
		public bool IgnoreTaunt
		{
			get => Type == CardType.HERO ? _data[7] > 0 : Type == CardType.MINION && _data[10] > 0;
			set
			{
				if (Type == CardType.HERO) _data[7] = value ? 1 : 0;
				else if (Type == CardType.MINION) _data[10] = value ? 1 : 0;
			}
		}

		// A hero's slot; HsServer: a minion's goes in the other tags ("Can't attack heroes" auras: Supreme Abyssal)
		public bool CannotAttackHeroes
		{
			get
			{
				if (Type != CardType.HERO)
					return _other != null && _other.TryGetValue(GameTag.CANNOT_ATTACK_HEROES, out int v) && v > 0;
				return _data[4] > 0;
			}
			set
			{
				if (Type != CardType.HERO)
				{
					(_other ??= new Dictionary<GameTag, int>())[GameTag.CANNOT_ATTACK_HEROES] = value ? 1 : 0;
					return;
				}
				_data[4] = value ? 1 : 0;
			}
		}

		public int Immune
		{
			get
			{
				if (Type == CardType.HERO)
					return _data[5];
				if (Type == CardType.WEAPON)
					return _data[2];
				if (Type == CardType.MINION)
					return _data[11];

				return 0;
			}
			set
			{
				if (Type == CardType.HERO)
					_data[5] = value;
				else if (Type == CardType.WEAPON)
					_data[2] = value;
				else if (Type == CardType.MINION)
					_data[11] = value;
				else
					throw new NotImplementedException();
			}
		}

		public int HeroPowerDamage
		{
			get
			{
				if (Type == CardType.HERO)
					return _data[6];
				return 0;
			}
			set
			{
				if (Type == CardType.HERO)
					_data[6] = value;
				else
					throw new NotImplementedException();
			}
		}

		public int this[in GameTag t]
		{
			get
			{
				switch (t)
				{
					// HsServer (2026-11-20): a weapon has its ATK slot too (index 3, WeaponLength) — "Your weapon has +2 Attack" (Vulpera Toxinblade,
					// Crabatoa's Claw) was written there but read as 0, so no weapon Attack aura ever showed
					case GameTag.ATK when Type != CardType.MINION && Type != CardType.WEAPON:
						return 0;
					case GameTag.ATK:
						return ATK;
					case GameTag.HEALTH when Type != CardType.MINION: // HsServer: minion-only slots (client weapons carry HEALTH)
					case GameTag.CHARGE when Type != CardType.MINION:
					case GameTag.RUSH when Type != CardType.MINION:
						return 0;
					case GameTag.HEALTH:
						return Health;
					//case GameTag.COST:
					//	//return GetCost() - ((Entity)Owner)._data[GameTag.COST];
					//	return GetCost() - Owner.GetNativeGameTag(GameTag.COST);
					case GameTag.CHARGE:
						return Charge > 0 ? 1 : 0;
					case GameTag.IMMUNE:
						return Immune;
					case GameTag.LIFESTEAL when Type == CardType.MINION:
						return Lifesteal ? 1 : 0;
					//case GameTag.CANT_ATTACK:
					//	return CANT_ATTACK;
					case GameTag.CANT_BE_TARGETED_BY_SPELLS:
					case GameTag.CANT_BE_TARGETED_BY_HERO_POWERS:
						return CantBeTargetedBySpells ? 1 : 0;
					case GameTag.CARD_COSTS_HEALTH:
						return CardCostHealth ? 1 : 0;
					case GameTag.RUSH:
						return Rush ? 1 : 0;
					case GameTag.ECHO:
						return Echo ? 1 : 0;
					case GameTag.CANNOT_ATTACK_HEROES:
						return CannotAttackHeroes ? 1 : 0;
					case GameTag.HEROPOWER_DAMAGE:
						return HeroPowerDamage;
					case GameTag.IGNORE_TAUNT:
						return IgnoreTaunt ? 1 : 0;
					default:
						return _other != null && _other.TryGetValue(t, out int v) ? v : 0;
				}
			}
			set
			{
				if ((t == GameTag.HEALTH || t == GameTag.CHARGE || t == GameTag.RUSH) && Type != CardType.MINION)
					return; // HsServer: minion-only slots
				switch (t)
				{
					case GameTag.ATK:
						ATK = value;
						return;
					case GameTag.HEALTH:
						Health = value;
						return;
					case GameTag.CHARGE:
						Charge += value;
						return;
					case GameTag.HEALTH_MINIMUM:
						//Owner[GameTag.HEALTH_MINIMUM] = value;
						return;
					case GameTag.IMMUNE:
						Immune = value;
						return;
					case GameTag.LIFESTEAL:
						Lifesteal = value > 0;
						return;
					//case GameTag.CANT_ATTACK:
					//	CANT_ATTACK = value;
					//	return;
					case GameTag.CANT_BE_TARGETED_BY_SPELLS:
					case GameTag.CANT_BE_TARGETED_BY_HERO_POWERS:
						CantBeTargetedBySpells = value > 0;
						return;
					case GameTag.CARD_COSTS_HEALTH:
						CardCostHealth = value > 0;
						return;
					case GameTag.RUSH:
						Rush = value > 0;
						return;
					case GameTag.ECHO:
						Echo = value > 0;
						return;
					case GameTag.CANNOT_ATTACK_HEROES:
						CannotAttackHeroes = value > 0;
						return;
					case GameTag.HEROPOWER_DAMAGE:
						HeroPowerDamage = value;
						return;
					case GameTag.IGNORE_TAUNT:
						IgnoreTaunt = value > 0;
						return;
					default:
						(_other ??= new Dictionary<GameTag, int>())[t] = value;
						return;
				}
			}
		}

		public AuraEffects Clone()
		{
			return new AuraEffects(this);
		}

		public string Hash()
		{
			var hash = new StringBuilder();
			hash.Append("[AE:");
			int[] data = _data;
			for (int i = 0; i < data.Length; i++)
				hash.Append(data[i]);
			hash.Append("]");
			return hash.ToString();
		}

		#region Equality members

		public bool Equals(AuraEffects other)
		{
			if (ReferenceEquals(null, other)) return false;
			if (ReferenceEquals(this, other)) return true;
			return Type == other.Type &&
			       System.Linq.Enumerable.SequenceEqual(_data, other._data);
		}

		public override bool Equals(object obj)
		{
			if (ReferenceEquals(null, obj)) return false;
			if (ReferenceEquals(this, obj)) return true;
			if (obj.GetType() != this.GetType()) return false;
			return Equals((AuraEffects) obj);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int hashCode = (int) Type;
				int[] data = _data;
				for (int i = 0; i < data.Length; i++)
					hashCode = (hashCode * 397) ^ data[i];
				return hashCode;
			}
		}

		public static bool operator ==(AuraEffects left, AuraEffects right)
		{
			return Equals(left, right);
		}

		public static bool operator !=(AuraEffects left, AuraEffects right)
		{
			return !Equals(left, right);
		}

		#endregion
	}

	/// <summary>
	/// A collecton of controller Tag increments from Auras.
	/// These tags tends to be checked when a player plays any cards.
	/// </summary>
	public class ControllerAuraEffects : IEquatable<ControllerAuraEffects>
	{
		private Action<IPowerHistoryEntry> _sendHistory;
		private int _controllerEntityId;

		private int _timeOut;
		private int _spellPowerDouble;
		private int _heroPowerDouble;
		private int _restoreToDamage;
		private int _extraBattecry;
		private int _chooseBoth;
		private int _spellsCostHealth;
		private int _extraEndTurnEffect;
		private int _heroPowerDisabled;
		private int _allHealingDouble;
		private int _extraBattlecryAndCombo;
		private int _spellPower;
		/// <summary>HsServer: every other controller-level rule a card's aura grants (Secrets trigger twice, Steady Shot can
		/// target...), so rules are tags the engine reads instead of card ids it looks for.</summary>
		private Dictionary<GameTag, int> _other;

		public ControllerAuraEffects() { }

		public ControllerAuraEffects(in Game g, in Controller c)
		{
			if (g.History)
			{
				_sendHistory = g.PowerHistory.Add;
				_controllerEntityId = c.Id;
			}
		}

		public int this[GameTag t]
		{
			get
			{
				switch (t)
				{
					case GameTag.TIMEOUT:
						return _timeOut;
					case GameTag.SPELLPOWER_DOUBLE:
					case GameTag.SPELL_HEALING_DOUBLE:
						return _spellPowerDouble;
					case GameTag.HERO_POWER_DOUBLE:
						return _heroPowerDouble;
					case GameTag.HEALING_DOES_DAMAGE:
						return _restoreToDamage >= 1 ? 1 : 0;
					case GameTag.CHOOSE_BOTH:
						return _chooseBoth >= 1 ? 1 : 0;
					case GameTag.SPELLS_COST_HEALTH:
						return _spellsCostHealth >= 1 ? 1 : 0;
					case GameTag.EXTRA_BATTLECRIES_BASE:
						return _extraBattecry;
					case GameTag.EXTRA_END_TURN_EFFECT:
						return _extraEndTurnEffect;
					case GameTag.HERO_POWER_DISABLED:
						return _heroPowerDisabled >= 1 ? 1 : 0;
					case GameTag.ALL_HEALING_DOUBLE:
						return _allHealingDouble;
					case GameTag.EXTRA_MINION_BATTLECRIES_BASE:
						return _extraBattlecryAndCombo;
					case GameTag.SPELLPOWER:
						return _spellPower;
					default:
						return _other != null && _other.TryGetValue(t, out int v) ? v : 0;
				}
			}
			set
			{
				if (Entity.InHistory(t)) // HsServer: rule tags (HsTag) stay on the server
					_sendHistory?.Invoke(PowerHistoryBuilder.TagChange(_controllerEntityId, t, value));
				switch (t)
				{
					case GameTag.TIMEOUT:
						_timeOut = value;
						return;
					case GameTag.SPELLPOWER_DOUBLE:
					case GameTag.SPELL_HEALING_DOUBLE:
						_spellPowerDouble = value;
						return;
					case GameTag.HERO_POWER_DOUBLE:
						_heroPowerDouble = value;
						return;
					case GameTag.HEALING_DOES_DAMAGE:
						_restoreToDamage = value;
						return;
					case GameTag.CHOOSE_BOTH:
						_chooseBoth = value;
						return;
					case GameTag.SPELLS_COST_HEALTH:
						_spellsCostHealth = value;
						return;
					case GameTag.EXTRA_BATTLECRIES_BASE:
						_extraBattecry = value;
						return;
					case GameTag.EXTRA_END_TURN_EFFECT:
						_extraEndTurnEffect = value;
						return;
					case GameTag.HERO_POWER_DISABLED:
						_heroPowerDisabled = value;
						return;
					case GameTag.ALL_HEALING_DOUBLE:
						_allHealingDouble = value;
						return;
					case GameTag.EXTRA_MINION_BATTLECRIES_BASE:
						_extraBattlecryAndCombo = value;
						return;
					case GameTag.SPELLPOWER:
						_spellPower = value;
						return;
					default:
						(_other ??= new Dictionary<GameTag, int>())[t] = value;
						return;
				}
			}
		}

		public ControllerAuraEffects Clone(Controller c)
		{

			var cae = (ControllerAuraEffects)MemberwiseClone();
			if (_other != null) cae._other = new Dictionary<GameTag, int>(_other);
			cae._sendHistory = c.Game.History
				? (Action<IPowerHistoryEntry>) c.Game.PowerHistory.Add
				: null;
			return cae;
		}

		public string Hash()
		{
			var sb = new StringBuilder("[CAE:");
			sb.Append(_timeOut);
			sb.Append(_spellPowerDouble);
			sb.Append(_heroPowerDouble);
			sb.Append(_restoreToDamage);
			sb.Append(_extraBattecry);
			sb.Append(_chooseBoth);
			sb.Append(_spellsCostHealth);
			sb.Append(_extraEndTurnEffect);
			sb.Append(_heroPowerDisabled);
			sb.Append(_allHealingDouble);
			sb.Append(_extraBattlecryAndCombo);
			sb.Append(_spellPower);
			if (_other != null)
				foreach (KeyValuePair<GameTag, int> kv in _other.Where(kv => kv.Value != 0).OrderBy(kv => kv.Key))
					sb.Append($"{(int)kv.Key}={kv.Value};");
			sb.Append("]");
			return sb.ToString();
		}

		#region Equality members

		public bool Equals(ControllerAuraEffects other)
		{
			if (ReferenceEquals(null, other)) return false;
			if (ReferenceEquals(this, other)) return true;
			return _timeOut == other._timeOut && _spellPowerDouble == other._spellPowerDouble &&
			       _heroPowerDouble == other._heroPowerDouble && _restoreToDamage == other._restoreToDamage &&
			       _extraBattecry == other._extraBattecry && _chooseBoth == other._chooseBoth &&
			       _spellsCostHealth == other._spellsCostHealth && _extraEndTurnEffect == other._extraEndTurnEffect &&
			       _heroPowerDisabled == other._heroPowerDisabled && _allHealingDouble == other._allHealingDouble &&
			       _extraBattlecryAndCombo == other._extraBattlecryAndCombo && _spellPower == other._spellPower &&
			       OtherEquals(other);
		}

		private bool OtherEquals(ControllerAuraEffects other)
		{
			IEnumerable<KeyValuePair<GameTag, int>> Set(ControllerAuraEffects e) =>
				e._other?.Where(kv => kv.Value != 0).OrderBy(kv => kv.Key) ?? Enumerable.Empty<KeyValuePair<GameTag, int>>();
			return Set(this).SequenceEqual(Set(other));
		}

		public override bool Equals(object obj)
		{
			if (ReferenceEquals(null, obj)) return false;
			if (ReferenceEquals(this, obj)) return true;
			if (obj.GetType() != this.GetType()) return false;
			return Equals((ControllerAuraEffects) obj);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				int hashCode = _timeOut;
				hashCode = (hashCode * 397) ^ _spellPowerDouble;
				hashCode = (hashCode * 397) ^ _heroPowerDouble;
				hashCode = (hashCode * 397) ^ _restoreToDamage;
				hashCode = (hashCode * 397) ^ _extraBattecry;
				hashCode = (hashCode * 397) ^ _chooseBoth;
				hashCode = (hashCode * 397) ^ _spellsCostHealth;
				hashCode = (hashCode * 397) ^ _extraEndTurnEffect;
				hashCode = (hashCode * 397) ^ _heroPowerDisabled;
				hashCode = (hashCode * 397) ^ _allHealingDouble;
				hashCode = (hashCode * 397) ^ _extraBattlecryAndCombo;
				hashCode = (hashCode * 397) ^ _spellPower;
				return hashCode;
			}
		}

		public static bool operator ==(ControllerAuraEffects left, ControllerAuraEffects right)
		{
			return Equals(left, right);
		}

		public static bool operator !=(ControllerAuraEffects left, ControllerAuraEffects right)
		{
			return !Equals(left, right);
		}

		#endregion
	}
}
