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
using System.Text.RegularExpressions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;

namespace SabberStoneCore.Enchants
{
    internal static class Enchants
    {
	    private static Regex AttackHealth = new Regex(@"[+](\d)[/][+](\d)");
	    private static Regex SetAttackHealth = new Regex(@"(\d)[/](\d)");
	    private static Regex Attack = new Regex(@"[+](\d) Attack");
	    private static Regex Health = new Regex(@"[+](\d) Health");

	    public static readonly Enchant AddAttackScriptTag =
		    new Enchant(Effects.Attack_N(0))
		    {
			    UseScriptTag = true
		    };
	    public static readonly Enchant AddHealthScriptTag =
		    new Enchant(Effects.Health_N(0))
		    {
			    UseScriptTag = true
		    };
	    public static readonly Enchant SetAttackScriptTag =
		    new Enchant(Effects.SetAttack(0))
		    {
			    UseScriptTag = true
		    };
	    public static readonly Enchant SetHealthScriptTag =
		    new Enchant(Effects.SetMaxHealth(0))
		    {
			    UseScriptTag = true
		    };
		public static readonly Enchant AddAttackHealthScriptTag =
		    new Enchant(Effects.AttackHealth_N(0))
		    {
			    UseScriptTag = true
		    };
	    public static readonly Enchant SetAttackHealthScriptTag =
		    new Enchant(Effects.SetAttackHealth(0))
		    {
			    UseScriptTag = true
		    };

		/// <summary>
		/// Generate proper <see cref="Enchant"/> from the text of the given card.
		/// </summary>
		/// <param name="cardId"></param>
		/// <returns></returns>
		public static Enchant GetAutoEnchantFromText(string cardId) => GetAutoEnchantFromText(Cards.FromId(cardId));

		/// <summary>HsServer: the same from a card object (also usable while the cards are being loaded: CardContainer's magnetize
		/// enchantments).</summary>
		public static Enchant GetAutoEnchantFromText(Card card)
		{
			// HsServer (WONDERS, 2026-10-07): the client's current text, not the script's 2021 one (Get Big CFM_816e is "+3/+3" now,
			// the 2021 text said "+2/+2"; 15 of 910 auto enchantments differ). A client text that names no numbers (Shapeshift's
			// CS2_017o "Your hero has extra Attack this turn") keeps the 2021 text's.
			string text = card.ClientText ?? card.Text ?? "";
			if (!card.Modular && !AttackHealth.IsMatch(text) && !Attack.IsMatch(text) && !Health.IsMatch(text) && !SetAttackHealth.IsMatch(text)
			    && card.Text is string scriptText && scriptText != text
			    && (AttackHealth.IsMatch(scriptText) || Attack.IsMatch(scriptText) || Health.IsMatch(scriptText) || SetAttackHealth.IsMatch(scriptText)))
				text = scriptText;
		    var effects = new List<IEffect>();
			bool oneTurn = false;
			bool mod = false;

			if (card.Modular)
			{	// generate magnetic enchants
				effects.AddRange(Effects.AttackHealth_N(0));
				mod = true;
			}
			else
			{
				Match attackHealth = AttackHealth.Match(text);
				Match attack = Attack.Match(text);
				Match health = Health.Match(text);
				Match set = SetAttackHealth.Match(text);

				if (attackHealth.Success)
				{
					effects.Add(Effects.Attack_N(Int32.Parse(attackHealth.Groups[1].Value)));
					effects.Add(Effects.Health_N(Int32.Parse(attackHealth.Groups[2].Value)));
				}
				else if (attack.Success)
				{
					effects.Add(Effects.Attack_N(Int32.Parse(attack.Groups[1].Value)));
				}
				else if (health.Success)
				{
					effects.Add(Effects.Health_N(Int32.Parse(health.Groups[1].Value)));
				}
				else if
					(set.Success)
				{
					effects.Add(Effects.SetAttack(Int32.Parse(set.Groups[1].Value)));
					effects.Add(Effects.SetMaxHealth(Int32.Parse(set.Groups[2].Value)));
				}
			}


			if (text.Contains(@"<b>Taunt</b>"))
		    {
			    effects.Add(Effects.TauntEff);
		    }

		    if (text.Contains(@"<b>Windfury</b>"))
		    {
			    effects.Add(Effects.Windfury);
		    }

		    if (text.Contains(@"<b>Charge</b>"))
		    {
			    effects.Add(Effects.Charge);
		    }

		    if (text.Contains(@"<b>Immune</b>"))
		    {
			    effects.Add(Effects.Immune);
		    }

		    if (text.Contains(@"<b>Divine Shield</b>"))
		    {
			    effects.Add(new Effect(GameTag.DIVINE_SHIELD, EffectOperator.SET, 1));
		    }

		    if (text.Contains(@"<b>Poisonous</b>"))
		    {
			    effects.Add(new Effect(GameTag.POISONOUS, EffectOperator.SET, 1));
		    }

		    if (text.Contains(@"<b>Lifesteal</b>"))
		    {
			    effects.Add(Effects.Lifesteal);
		    }

		    if (text.Contains(@"<b>Rush</b>"))
		    {
			    effects.Add(Effects.Rush);
		    }

			// HsServer (TITANS): the other keywords a magnetize enchantment carries (wiki Magnetic: the Mech gets "its stats and
			// text"): Sparkbots' Elusive / Reborn, SP-3Y3-D3R's Stealth (for 1 turn: its enchantment leaves at the owner's next
			// turn start, CardContainer), Chill-o-matic's "Freezes any character damaged by this minion" — magnetize enchantments
			// only (the other auto enchants keep their 2021 behaviour)
			if (mod)
			{
				if (text.Contains(@"<b>Elusive</b>"))
					effects.Add(Effects.CantBeTargetedBySpellsAndHeroPowers);
				if (text.Contains(@"<b>Reborn</b>"))
					effects.Add(new Effect(GameTag.REBORN, EffectOperator.SET, 1));
				if (text.Contains(@"<b>Stealth</b>"))
					effects.Add(Effects.StealthEff);
				if (text.Contains(@"<b>Freeze") || text.Contains("Freezes any character damaged"))
					effects.Add(new Effect(GameTag.FREEZE, EffectOperator.SET, 1));
			}

			if (text.Contains("this turn"))
		    {
				oneTurn = true;
		    }

			var output = new Enchant(effects.ToArray())
			{
				IsOneTurnEffect = oneTurn
			};
			if (mod)
				output.UseScriptTag = true;

			return output;
	    }


    }

	internal static class Effects
	{
		internal static IEffect Attack_N(int n)
		{
			return ATK.Effect(EffectOperator.ADD, n);
		}

		internal static IEffect Health_N(int n)
		{
			return Health.Effect(EffectOperator.ADD, n);
		}

		internal static IEffect[] AttackHealth_N(int n)
		{
			return new[] {Attack_N(n), Health_N(n)};
		}

		internal static IEffect SetAttack(int n)
		{
			return ATK.Effect(EffectOperator.SET, n);
		}

		internal static IEffect SetMaxHealth(int n)
		{
			return Health.Effect(EffectOperator.SET, n);
		}

		internal static IEffect[] SetAttackHealth(int n)
		{
			return new[] {SetAttack(n), SetMaxHealth(n)};
		}

		internal static IEffect ReduceCost(int n)
		{
			return Cost.Effect(EffectOperator.SUB, n);
		}

		// HsServer: "costs (n) less, but not less than 1" (current Sorcerer's Apprentice, Radiant Elemental)
		internal static IEffect ReduceCostNotBelow1(int n)
		{
			return Cost.Effect(EffectOperator.SUB_NOT_BELOW_1, n);
		}

		internal static IEffect SetCost(int n)
		{
			return Cost.Effect(EffectOperator.SET, n);
		}

		// HsServer (TITANS): "costs (n) more (up to 10)" (Frost Plague's Frost Fever)
		internal static IEffect AddCostNotAbove10(int n)
		{
			return Cost.Effect(EffectOperator.ADD_NOT_ABOVE_10, n);
		}

		internal static IEffect AddCost(int n)
		{
			return Cost.Effect(EffectOperator.ADD, n);
		}

		internal static IEffect TauntEff => Taunt.Effect();

		internal static IEffect StealthEff => Stealth.Effect();

		internal static IEffect CantBeTargetedBySpellsAndHeroPowers => CantBeTargetedBySpells.Effect();

		internal static Effect Windfury => new Effect(GameTag.WINDFURY, EffectOperator.SET, 1);

		internal static Effect Charge => new Effect(GameTag.CHARGE, EffectOperator.SET, 1);

		internal static Effect Immune => new Effect(GameTag.IMMUNE, EffectOperator.SET, 1);

		internal static Effect Lifesteal => new Effect(GameTag.LIFESTEAL, EffectOperator.SET, 1);

		internal static Effect Rush => new Effect(GameTag.RUSH, EffectOperator.SET, 1);

		internal static Effect Echo => new Effect(GameTag.ECHO, EffectOperator.SET, 1);
	}
}
