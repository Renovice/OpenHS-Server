using System.Collections.Generic;
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
using SabberStoneCore.Model;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Triggers;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
	{
		public static bool HeroPower(Controller c, ICharacter target = null, int chooseOne = 0, bool skipPrePhase = false, HeroPower power = null)
		{
			// HsServer (VIOLET_HOLD): a second Hero Power (Blood Doctor Thal'ena) is used the same way, its own entity
			return power != null && power != c.Hero.HeroPower ? HeroPowerUse(c, power, target, chooseOne, skipPrePhase)
				: HeroPowerBlock.Invoke(c, target, chooseOne, skipPrePhase);
		}

		/// <summary>Client tags: a second Hero Power's index (ADDITIONAL_HERO_POWER_INDEX 3919) and the player's link to it
		/// (ADDITIONAL_HERO_POWER_ENTITY_1 4029; client Player.cs 589, ZoneHeroPower.cs 58).</summary>
		public const GameTag ADDITIONAL_HERO_POWER_INDEX = (GameTag)3919, ADDITIONAL_HERO_POWER_ENTITY_1 = (GameTag)4029;

		/// <summary>The player's second Hero Power (Blood Doctor Thal'ena's Vampyr's Kiss), or null.</summary>
		public static HeroPower SecondHeroPower(Controller c) =>
			c[ADDITIONAL_HERO_POWER_ENTITY_1] > 0 && c.Game.IdEntityDic.TryGetValue(c[ADDITIONAL_HERO_POWER_ENTITY_1], out IPlayable p) && p is HeroPower hp ? hp : null;

		/// <summary>
		/// "Get a second Hero Power" (Blood Doctor Thal'ena; wiki: "can be activated independently, and each can be used once per turn";
		/// "Cards that replace Hero Powers ... only replace the original power, not the second one"): a new Hero Power entity in play with
		/// ADDITIONAL_HERO_POWER_INDEX 1, the player's ADDITIONAL_HERO_POWER_ENTITY_1 naming it. A second one replaces the first (doubt).
		/// </summary>
		public static HeroPower AddSecondHeroPower(Controller c, Card card, IPlayable source)
		{
			if (card == null) return null;
			if (SecondHeroPower(c) is HeroPower old)
			{
				RetireFromPlay(old);
				c.SetasideZone.Add(old);
			}
			var power = (HeroPower)Entity.FromCard(c, card, new EntityData
			{
				{ GameTag.CREATOR, source?.Id ?? c.Hero.Id }, { GameTag.CREATOR_DBID, source?.Card.AssetId ?? 0 }, { ADDITIONAL_HERO_POWER_INDEX, 1 },
				{ GameTag.ZONE, (int)Zone.PLAY },
			});
			c[ADDITIONAL_HERO_POWER_ENTITY_1] = power.Id;
			if (power.ActivatedTrigger == null) power.Power?.Trigger?.Activate(power);
			return power;
		}

		/// <summary>Client player tags: the Hero Power triggers twice (3428, Chrono-Lord Deios); a doubled run is going on (4169).</summary>
		public const GameTag HERO_POWER_TWICE = (GameTag)3428, IS_EXTRA_TRIGGERED_POWER = (GameTag)4169;

		public static Func<Controller, ICharacter, int, bool, bool> HeroPowerBlock
			=> delegate (Controller c, ICharacter target, int chooseOne, bool skipPrePhase)
			{
				return HeroPowerUse(c, c.Hero.HeroPower, target, chooseOne, skipPrePhase);
			};

		/// <summary>HsServer: using <paramref name="heroPower"/> — the player's Hero Power, or a second one (VIOLET_HOLD: Thal'ena).</summary>
		public static bool HeroPowerUse(Controller c, HeroPower heroPower, ICharacter target, int chooseOne, bool skipPrePhase)
			{
				Game game = c.Game;

				if (!skipPrePhase)
					if (!heroPower.IsPlayable || !heroPower.IsValidPlayTarget(target))
						return false;

				// HsServer: live order (official Power.log, HERO_08bp / CS2_034_H2): the PLAY block opens first and holds the
				// mana spend, CARD_TARGET and SLUSH_TIME 2000 before the effect; activation counters and EXHAUSTED follow the
				// effect inside the same block.
				if (game.History)
					game.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.PLAY, heroPower.Id, "", 0, target?.Id ?? 0));

				PayPhase.Invoke(c, heroPower);
				if (game.History)
				{
					if (target != null)
						heroPower[GameTag.CARD_TARGET] = target.Id;
					game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 2000, Info = new List<int> { heroPower.Id } });
				}

				if (target != null)
				{
					game.CurrentEventData = new EventMetaData(heroPower, target);
					Trigger.ValidateTriggers(game, heroPower, SequenceType.Target);
					game.TaskQueue.StartEvent();
					game.TriggerManager.OnTargetTrigger(heroPower);
					game.ProcessTasks();
					game.TaskQueue.EndEvent();
				}

				game.Log(LogLevel.INFO, BlockType.ACTION, "HeroPowerBlock", !game.Logging? "":$"Play HeroPower {heroPower}[{heroPower.Card.Id}]{(target != null ? $" targeting {target}" : "")}.");

				c.ResolvingHeroPower = true; // HsServer: "cards you draw from your Hero Power" (Wilfred Fizzlebang)
				int armorBefore = c[HsTag.ARMOR_GAINED_THIS_GAME], heroEnchantsBefore = c.Hero.AppliedEnchantments?.Count ?? 0;
				// HsServer (DALA heist): a Hero Power with Combo (client COMBO tag + a ComboTask: Linzi Redgrin's Lil' Eviscerate "Deal 1
				// damage. Combo: 2 instead") runs its Combo when the player played a card this turn — the keyword's own rule (wiki
				// Combo.txt:1 "A bonus if you already played a card this turn"); a Hero Power isn't a card, using it starts no Combo
				PowerActivation activation = heroPower.Combo && c.IsComboActive && heroPower.Card.Power?.ComboTask != null
					? PowerActivation.COMBO : PowerActivation.POWER;
				game.TaskQueue.StartEvent();
				heroPower.ActivateTask(activation, target, chooseOne);
				game.ProcessTasks();
				game.TaskQueue.EndEvent();
				// HsServer (TIME_TRAVEL): "Your … Hero Power … trigger(s) twice" (Chrono-Lord Deios; live player tag 3428): the effect
				// again, the same target if it still can be (live wraps it in IS_EXTRA_TRIGGERED_POWER 4169)
				if (c[HERO_POWER_TWICE] > 0 && !c.Hero.IsDead && (target == null || !target.IsDead && target.Zone?.Type == Zone.PLAY || target is Hero))
				{
					c[IS_EXTRA_TRIGGERED_POWER] = 1;
					game.TaskQueue.StartEvent();
					heroPower.ActivateTask(activation, target, chooseOne);
					game.ProcessTasks();
					game.TaskQueue.EndEvent();
					c[IS_EXTRA_TRIGGERED_POWER] = 0;
				}
				// HsServer (Dalaran Heist): a Hero Power's second effect in its own POWER block, as a card's (live DALA_BOSS_64px
				// Invest!: POWER 0, SLUSH_TIME 3000, POWER 1 "then … copy it", then the activation counters; Power.SecondPowerTask)
				SecondPowerBlock(c, heroPower, target);
				HeroPowerBonuses(c, heroPower, armorBefore, heroEnchantsBefore);
				// HsServer (2026-10-09, Dalaran Heist): a Hero Power with Overload overloads like a card (Refresh DALA_Shaman_HP2 "Draw a
				// card. Overload: (1)"; also BOM_01_Brukan_01p, PVPDR_Finley_HP3): its own POWER block after the effect, as a played card's
				// (unrecorded for Hero Powers — live-checks)
				OverloadBlock(c, heroPower, game.History);
				c.ResolvingHeroPower = false;

				// HsServer (live, 8 of 8 captures): the use is counted and the Hero Power exhausted before the Death Phase,
				// so a death trigger already sees it used. The Hero Power counts its own uses too (a replacement starts at 0).
				c.HeroPowerActivationsThisTurn++;
				c.NumTimesHeroPowerUsedThisGame++;
				heroPower[GameTag.HEROPOWER_ACTIVATIONS_THIS_TURN] += 1;
				// exhausted unless the player may use it again this turn (HEROPOWER_ADDITIONAL_ACTIVATIONS, Game.AuraUpdate)
				heroPower.IsExhausted = heroPower[GameTag.HEROPOWER_ACTIVATIONS_THIS_TURN] > c[GameTag.HEROPOWER_ADDITIONAL_ACTIVATIONS];

				game.DeathProcessingAndAuraUpdate();

				if (game.History)
					game.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
				EndPlayerPlay(game, heroPower);

				game.TaskQueue.StartEvent();
				game.TriggerManager.OnInspireTrigger(heroPower);
				game.ProcessTasks();
				game.TaskQueue.EndEvent();

				game.DeathProcessingAndAuraUpdate();
				game.CurrentEventData = null;
				return true;
			}

		/// <summary>
		/// HsServer: "Your Hero Power gains 1 more Armor this game" (Free Spirit) / "gives your hero 1 more Attack this game"
		/// (Groovy Cat): a Hero Power that gained Armor gains the extra too; one that gave the hero Attack (its one-turn
		/// enchantment on the hero) gives it again for each extra point. A Hero Power without Armor / Attack gets nothing.
		/// </summary>
		private static void HeroPowerBonuses(Controller c, HeroPower heroPower, int armorBefore, int heroEnchantsBefore)
		{
			if (c[HsTag.HERO_POWER_EXTRA_ARMOR] > 0 && c[HsTag.ARMOR_GAINED_THIS_GAME] > armorBefore)
				c.Hero.GainArmor(heroPower, c[HsTag.HERO_POWER_EXTRA_ARMOR]);
			if (c[HsTag.HERO_POWER_EXTRA_ATTACK] > 0 && c.Hero.AppliedEnchantments is { } list && list.Count > heroEnchantsBefore)
			{
				Enchantment attack = list.Skip(heroEnchantsBefore).FirstOrDefault(e => e.Creator == heroPower && e.Card.Power?.Enchant?.Effects?
					.Any(x => x is Enchants.Effect eff && eff.Tag == GameTag.ATK) == true);
				for (int i = 0; attack != null && i < c[HsTag.HERO_POWER_EXTRA_ATTACK]; i++)
					AddEnchantmentBlock(c.Game, attack.Card, heroPower, c.Hero, 0, 0, 0);
			}
		}
	}
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
