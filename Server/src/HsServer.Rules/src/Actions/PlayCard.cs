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
using SabberStoneCore.Enchants;
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
		/// <summary>HsServer: Quickdraw is on for a card in hand: it entered the hand this turn (wiki Quickdraw: "played the
		/// same turn it was added to the player's hand"; "Cards in the starting hand do not trigger Quickdraw effects").</summary>
		public static bool IsQuickdrawNow(IPlayable p) =>
			p.Zone?.Type == Zone.HAND && p.Game.Turn > 0 && p[HsTag.ADDED_TO_HAND_TURN] == p.Game.Turn;

		/// <summary>HsServer: the cards <paramref name="c"/> played this turn as an effect counts them ("for each card you've
		/// played this turn"). The card still resolving its own play isn't one of them (user ruling 2026-10-05: "the card
		/// isn't 'played' until it actually finishes its effect"; wiki Shadow Sculptor: "does not count itself"). The wire
		/// counter NUM_CARDS_PLAYED_THIS_TURN does count it at once (live: set at the PLAY block's start, before POWER).</summary>
		public static int CardsPlayedBefore(Controller c, IEntity source) =>
			c.NumCardsPlayedThisTurn - (source != null && c[HsTag.RESOLVING_PLAY] == source.Id ? 1 : 0);

		public static bool PlayCard(Controller c, IPlayable source, IPlayable target = null, int zonePosition = -1, int chooseOne = 0, bool skipPrePhase = false)
		{
			return PlayCardBlock.Invoke(c, source, target, zonePosition, chooseOne, skipPrePhase);
		}

		public static Func<Controller, IPlayable, IPlayable, int, int, bool, bool> PlayCardBlock
			=> delegate (Controller c, IPlayable source, IPlayable target, int zonePosition, int chooseOne, bool skipPrePhase)
			{
				// Preplay Phase : check the given source is playable
				if (!skipPrePhase)
					if (!PrePlayPhase.Invoke(c, source, target, zonePosition, chooseOne))
						return false;

				Game game = c.Game;
				bool history = game.History;

				// Start play block (HsServer: with the Choose One option chosen, live's SubOption on its PLAY / POWER blocks)
				if (history)
				{
					game.PowerHistory.ChosenOption = chooseOne > 0 ? (source.Id, chooseOne - 1) : (0, -1);
					game.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.PLAY, source.Id, "", 0, target?.Id ?? 0));
				}

				game.CurrentEventData = new EventMetaData(source, target);

				// Pay Phase
				if (!PayPhase.Invoke(c, source))
				{
					if (history) game.PowerHistory.ChosenOption = (0, -1);
					return false;
				}

				bool echo = source.IsEcho || source.Card.Repeatable; // HsServer: "Repeatable this turn" works like Echo (wiki Echo)

				// HsServer: Outcast - the card was the left- or right-most card in hand when played.
				c.OutcastPlayId = source.Zone is Model.Zones.HandZone hand && (source.ZonePosition == 0 || source.ZonePosition == hand.Count - 1) ? source.Id : 0;
				c.PlayedFromSlot = source.Zone is Model.Zones.HandZone ? source.ZonePosition : -1;
				c[HsTag.PLAYED_HAND_COUNT] = source.Zone is Model.Zones.HandZone hz ? hz.Count : 0; // HsServer (TIME_TRAVEL): Precise Shot
				// HsServer (SPACE): its hand neighbours saw an adjacent card played (Orbital Moon, Red Giant)
				if (source.Zone is Model.Zones.HandZone fromHand)
					foreach (int slot in new[] { source.ZonePosition - 1, source.ZonePosition + 1 })
						if (slot >= 0 && slot < fromHand.Count && fromHand[slot] is IPlayable next)
						{
							next[HsTag.ADJACENT_PLAYED_TURN] = game.Turn;
							next[HsTag.ADJACENT_PLAYED_COUNT] = next[HsTag.ADJACENT_PLAYED_COUNT] + 1;
						}
				// HsServer: Quickdraw (wiki: "played the same turn it was added to the player's hand"; "only activates if
				// the card is played from hand"; never for the starting hand)
				((Entity)source)._data[HsTag.QUICKDRAW_PLAY] = IsQuickdrawNow(source) ? 1 : 0;

				// remove from hand zone
				if (!RemoveFromZone.Invoke(c, source))
				{
					if (history) game.PowerHistory.ChosenOption = (0, -1);
					return false;
				}

				c.NumCardsPlayedThisTurn++;
				c.LastCardPlayed = source.Id;
				c[HsTag.RESOLVING_PLAY] = source.Id;
				MarkCorrupting(c, source); // HsServer: the cards this play corrupts are fixed now (live)

				// record played cards for effect of cards like Obsidian Shard and Lynessa Sunsorrow
				// or use graveyard instead with 'played' tag(or bool)?
				c.CardsPlayedThisTurn.Add(source.Card);
				c.PlayHistory.Add(new PlayHistoryEntry(in source, in target, in chooseOne));
				// HsServer (LOST_CITY): a Temporary card stays "Temporary" for its play's triggers (its GBL_999e leaves when played)
				if (source[HsTag.TEMPORARY] > 0) source[HsTag.PLAYED_AS_TEMPORARY] = 1;

				//// show entity
				//if (c.Game.History)
				//	c.Game.PowerHistory.Add(PowerHistoryBuilder.ShowEntity(source));

				// target is beeing set onto this gametag
				if (target != null)
				{
					source.CardTarget = target.Id;
					Trigger.ValidateTriggers(game, source, SequenceType.Target);
				}


				Trigger.ValidateTriggers(game, source, SequenceType.PlayCard);
				switch (source)
				{
					case Hero hero:
						PlayHero.Invoke(c, game, hero, target, chooseOne);
						break;
					case Location location: // HsServer: before Minion (a Location is one internally)
						PlayLocation(c, game, location, zonePosition);
						break;
					case Minion minion:
						PlayMinion.Invoke(c, game, minion, target, zonePosition, chooseOne);
						break;
					case Weapon weapon:
						PlayWeapon.Invoke(c, game, weapon, target, chooseOne);
						break;
					case Spell spell:
						PlaySpell.Invoke(c, game, spell, target, chooseOne);
						break;
				}
				SpendPlayThisTurnMark(source); // HsServer (TITANS): a played card's "if you play it this turn" mark is spent

				if (echo && !(source is Spell s && s.IsCountered))
				{
					// HsServer (live, live-20261006-echo: Unstable Evolution): a new card enters the hand like any card an effect
					// gives (one FULL_ENTITY: CREATOR = the player, DISPLAYED_CREATOR = the played card, GHOSTLY 1)
					var echoTags = new EntityData
					{
						{GameTag.GHOSTLY, 1},
						{GameTag.CREATOR, c.Id},
						{GameTag.DISPLAYED_CREATOR, source.Id}
					};
					IPlayable echoPlayable = Entity.FromCard(c, source.Card, echoTags);
					if (AddHandPhase.Invoke(c, echoPlayable))
					{
						c.Game.AuraUpdate();
						c.Game.GhostlyCards.Add(echoPlayable.Id);
					}
				}

				c[HsTag.RESOLVING_PLAY] = 0;
				CorruptHand(c, source); // HsServer: Corrupt — "upgrades in hand after you play a card that costs more" (live: before Combo)
				if (!c.IsComboActive)
					c.IsComboActive = true;

				c.NumOptionsPlayedThisTurn++;

				if (history)
				{
					if (source[GameTag.GHOSTLY] == 1)
						source[GameTag.GHOSTLY] = 0;
					game.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
					game.PowerHistory.ChosenOption = (0, -1);
					EndPlayerPlay(game, source);
				}

				game.CurrentEventData = null;

				return true;
			};

		public static Func<Controller, IPlayable, IPlayable, int, int, bool> PrePlayPhase
			=> delegate (Controller c, IPlayable source, IPlayable target, int zonePosition, int chooseOne)
			{
				// can't play because we got already board full (HsServer VIOLET_HOLD: a Disguised play needs room on the opponent's board)
				// (HsServer 2026-10-08: a Magnetic minion may go onto a Mech of a full board — wiki Magnetic.txt:7)
				if (source is Minion && (source[HsTag.PLAYED_DISGUISED] > 0 ? c.Opponent.BoardZone.IsFull :
				    c.BoardZone.IsFull && MagnetizeHostAt(c, source.Card, zonePosition) == null))
				{
					c.Game.Log(LogLevel.WARNING, BlockType.ACTION, "PrePlayPhase", !c.Game.Logging? "":$"Board has already {c.BoardZone.MaxSize} minions.");
					return false;
				}

				// set choose one option (HsServer: a choice the card doesn't have — it changed in hand — is refused, not a crash)
				if (chooseOne > 0 && (source.ChooseOnePlayables == null || chooseOne > source.ChooseOnePlayables.Length ||
				                      source.ChooseOnePlayables[chooseOne - 1] == null))
					return false;
				IPlayable subSource = chooseOne > 0 ? source.ChooseOnePlayables[chooseOne - 1] : source;

				// check if we can play this card and the target is valid
				if (!source.IsPlayableByPlayer || !subSource.IsPlayableByCardReq || !subSource.IsValidTarget(target)) // HsServer: a character, or a card in hand (Power.HandTarget)
				{
					return false;
				}

				return true;
			};

		// HsServer: a Health cost is paid, not damage (Character.LoseHealthForCost: no Armor, Immune or Divine Shield, no damage
		// triggers, no Lifesteal weapon heal — wiki Change_Cost_to_Health, live 75 of 75 payments with Armor up)
		private static void PayHealth(Controller c, int cost)
		{
			c.Hero.LoseHealthForCost(cost);
			// HsServer (2026-10-08): live marks the payment — game tag 2949 on, META SPEND_HEALTH (the cost, the hero), 2949 off (92 of 92)
			if (c.Game.History)
			{
				c.Game.PowerHistory.Add(PowerHistoryBuilder.TagChange(c.Game.Id, (GameTag)2949, 1));
				c.Game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SPEND_HEALTH, Data = cost, Info = new System.Collections.Generic.List<int> { c.Hero.Id } });
				c.Game.PowerHistory.Add(PowerHistoryBuilder.TagChange(c.Game.Id, (GameTag)2949, 0));
			}
		}

		public static Func<Controller, IPlayable, bool> PayPhase
			=> delegate (Controller c, IPlayable source)
			{
				int cost = source.Cost;
				source[HsTag.PLAYED_COST_PLUS1] = cost + 1; // HsServer (SPACE): the cost it was played for, 0 included
				if (source[HsTag.PAID_MANA_PLUS1] != 0) source[HsTag.PAID_MANA_PLUS1] = 0; // (VIOLET_HOLD: set below when Mana pays)
				c.Game.PayingFor = source.Id; // HsServer (CATACLYSM): the card being paid for isn't "held" while its own Mana is spent
				if (cost > 0)
				{
					source[GameTag.TAG_LAST_KNOWN_COST_IN_HAND] = cost;

					if (source is Spell && c.ControllerAuraEffects[GameTag.SPELLS_COST_HEALTH] == 1)
					{
						PayHealth(c, cost);
						return true;
					}

					if ((source.AuraEffects?.CardCostHealth ?? false) || Playable.CostsHealth(source)) // HsServer: Death Metal Knight
					{
						if (Playable.NextPaladinMinionCostsHealth(source)) c[HsTag.NEXT_PALADIN_MINION_COSTS_HEALTH] = 0; // Blood Crusader: used
						PayHealth(c, cost);
						return true;
					}

					// HsServer (EMERALD_DREAM): costs the opponent's Health (Agamaggan). Wiki: "armor is ignored, Immune and Divine Shield
					// cannot prevent the lost health, and lost health does not count as taking damage" — Health lost, not damage
					if (Playable.CostsOpponentHealth(source))
					{
						if (cost > 0) c.Opponent.Hero.Damage += cost;
						c.Opponent.Hero[AGAMAGGAN_CURSE] = 0;
						c[HsTag.NEXT_CARD_COSTS_OPPONENT_HEALTH] = 0;
						return true;
					}

					if (Playable.CostsArmor(source)) // HsServer: costs Armor instead of Mana (Anub'Rekhan)
					{
						c.Hero.Armor -= cost;
						return true;
					}

					if (Playable.CostsCorpses(source)) // HsServer: costs Corpses instead of Mana
					{
						SpendCorpses(c, source, cost);
						c[HsTag.NEXT_CARD_COSTS_CORPSES] = 0; // Exarch Maladaar's: used
						return true;
					}

					int tempUsed = Math.Min(c.TemporaryMana, cost);
					if (tempUsed > 0) c.TemporaryMana -= tempUsed;
					c.UsedMana += cost - tempUsed;
					c.TotalManaSpentThisGame += cost;
					// HsServer (VIOLET_HOLD): "played for 2 Mana" (the Lotus cards; wiki Lotus Troublemaker: not cards paid with Health)
					source[HsTag.PAID_MANA_PLUS1] = cost + 1;
					if (cost == 2) c[HsTag.PLAYED_FOR_2_MANA] = c[HsTag.PLAYED_FOR_2_MANA] + 1;
				}
				else
					source[HsTag.PAID_MANA_PLUS1] = 1;
				c.Game.PayingFor = 0;
				c.Game.Log(LogLevel.INFO, BlockType.ACTION, "PayPhase", !c.Game.Logging? "":$"Paying {source} for {source.Cost} Mana, remaining mana is {c.RemainingMana}.");
				return true;
			};

		/// <summary>Client AGAMAGGAN_CURSE (4056): on the opponent's hero while Agamaggan's next card is pending (live).</summary>
		public const GameTag AGAMAGGAN_CURSE = (GameTag)4056;

		/// <summary>HsServer (2026-10-08): a Battlecry's extra run (Brann, Shudderblock, "triggers twice") — live brackets its block with the
		/// card's IS_EXTRA_TRIGGERED_POWER (4169): 1 right before it, 0 right after (976 live; dungar-student 20768). The run before it resolves
		/// first.</summary>
		private static void ExtraRun(Game game, IPlayable card, Action activate)
		{
			game.ProcessTasks();
			card[(GameTag)4169] = 1;
			activate();
			game.ProcessTasks();
			card[(GameTag)4169] = 0;
		}

		public static Func<Controller, Game, Hero, IPlayable, int, bool> PlayHero
			=> delegate (Controller c, Game game, Hero hero, IPlayable target, int chooseOne)
			{ 
				game.Log(LogLevel.INFO, BlockType.ACTION, "PlayHero", !game.Logging? "":$"{c.Name} plays Hero {hero} {(target != null ? "with target " + target : "to board")}.");


				Hero oldHero = c.Hero;
				hero[GameTag.ZONE] = (int)Zone.PLAY;
				//hero[GameTag.LINKED_ENTITY] = c.Hero.Id;
				//hero[GameTag.HEALTH] = oldHero[GameTag.HEALTH];
				hero.BaseHealth = oldHero.BaseHealth;
				//hero[GameTag.DAMAGE] = oldHero[GameTag.DAMAGE];
				hero.Damage = oldHero.Damage;
				hero[GameTag.ARMOR] = oldHero[GameTag.ARMOR] + hero.Card[GameTag.ARMOR];
				hero.IsExhausted = oldHero.IsExhausted;

				c.SetasideZone.Add(oldHero);
				Generic.RetireFromPlay(oldHero);
				//oldHero[GameTag.REVEALED] = 1;
				//c[GameTag.HERO_ENTITY] = hero.Id;
				hero.Weapon = oldHero.Weapon;
				c.SetasideZone.Add(oldHero.HeroPower);
				Generic.RetireFromPlay(oldHero.HeroPower);
				// HsServer (2026-10-08): the new Hero Power comes after the Battlecry — rulebook :403 "First your hero is replaced … Then the
				// Battlecry/Choose One is resolved. Finally, replace your Hero Power with the hero's Hero Power."; live (startofgame 164832
				// 7243: the old one set aside with the hero, the Battlecry's POWER block, then a POWER block at effect index 1 with the new
				// Hero Power's FULL_ENTITY). Till then the hero holds the retired one (set aside, no trigger / aura).
				hero.HeroPower = oldHero.HeroPower;

				c.Hero = hero;
				hero.Power?.Trigger?.Activate(hero);

				// HsServer: rules (user, 2026-10-01) — the new hero keeps the old one's enchantments, "this turn" ones too
				// (Heroic Strike's +4 Attack, Ice Block's Immune), and its one-turn effects; plain state (Divine Shield,
				// Freeze, Stealth) stays behind with the old hero. Aura enchantments come back from their aura.
				if (oldHero.AppliedEnchantments != null)
					foreach (Enchantment e in oldHero.AppliedEnchantments.ToList())
					{
						if (e.Creator?.Power?.Aura != null)
							continue;
						Enchantment moved = Enchantment.GetInstance(in c, e.Creator, hero, e.Card);
						if (e[GameTag.TAG_SCRIPT_DATA_NUM_1] > 0)
							moved[GameTag.TAG_SCRIPT_DATA_NUM_1] = e[GameTag.TAG_SCRIPT_DATA_NUM_1];
						if (e[GameTag.TAG_SCRIPT_DATA_NUM_2] > 0)
							moved[GameTag.TAG_SCRIPT_DATA_NUM_2] = e[GameTag.TAG_SCRIPT_DATA_NUM_2];
						moved.CapturedCard = e.CapturedCard;
						if (e.IsOneTurnActive)
							game.OneTurnEffectEnchantments.Add(moved);
					}
				foreach ((int id, IEffect effect) in game.OneTurnEffects.Where(x => x.entityId == oldHero.Id).ToList())
					effect.ApplyTo(hero, isOneTurnEffect: true);

				// - OnPlay Phase --> OnPlay Trigger (Illidan)
				//   (death processing, aura updates)
				game.TaskQueue.StartEvent();
				OnPlayTrigger.Invoke(game, hero);

				// - BattleCry Phase --> Battle Cry Resolves
				//   (death processing, aura updates)
				game.TaskQueue.StartEvent();
				hero.ActivateTask(PowerActivation.POWER, target, chooseOne);
				// Brann / Shudderblock: as a minion's Battlecry (Controller.BattlecryRuns: twice, or 3 times)
				for (int run = 1; run < c.NonMinionBattlecryRuns; run++)
					ExtraRun(game, hero, () => hero.ActivateTask(PowerActivation.POWER, target, chooseOne));
				game.ProcessTasks();

				if (game.History)
					game.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.POWER, hero.Id, "", 1, 0));
				hero.HeroPower = (HeroPower) Entity.FromCard(c, Cards.GetHeroPower(hero.Card[GameTag.HERO_POWER]));
				hero.HeroPower.Power?.Trigger?.Activate(hero.HeroPower);
				hero.Card.Power?.AfterHeroPowerTask?.Process(game, c, hero, null);
				if (game.History)
					game.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
				if (hero.HeroPower.IsPassiveHeroPower)  // Valeera, ad hoc for now; Maybe revisit here for Bosses
					hero.HeroPower.ActivateTask();
				game.ProcessTasks();
				game.TaskQueue.EndEvent();

				game.DeathProcessingAndAuraUpdate();

				// - After Play Phase --> After play Trigger / Secrets (Mirror Entity)
				//   (death processing, aura updates)
				//hero.JustPlayed = false;
				game.TaskQueue.StartEvent();
				game.TriggerManager.OnAfterPlayCardTrigger(hero);
				game.ProcessTasks();
				game.TaskQueue.EndEvent();

				game.DeathProcessingAndAuraUpdate();

				return true;
			};

		public static Func<Controller, Game, Minion, IPlayable, int, int, bool> PlayMinion
			=> delegate (Controller c, Game game, Minion minion, IPlayable target, int zonePosition, int chooseOne)
			{
				Trigger.ValidateTriggers(game, minion, SequenceType.PlayMinion);

				game.Log(LogLevel.INFO, BlockType.ACTION, "PlayMinion", !game.Logging? "":$"{c.Name} plays Minion {minion} {(target != null ? "with target " + target : "to board")} " +
						 $"{(zonePosition > -1 ? "position " + zonePosition : "")}.");

				c.NumMinionsPlayedThisTurn++;
				// HsServer (VIOLET_HOLD): Free placement — played by the player onto the opponent's board, theirs from then on (wiki Free
				// placement: "Cards that are played on the opponent's board are still considered to be played by the player")
				if (minion[HsTag.PLAYED_DISGUISED] > 0 && !c.Opponent.BoardZone.IsFull)
				{
					minion[IS_USING_DISGUISED_OPTION] = 1;
					GiveTo(minion, c.Opponent);
					c.Opponent.BoardZone.Add(minion, Math.Min(zonePosition < 0 ? c.Opponent.BoardZone.Count : zonePosition, c.Opponent.BoardZone.Count));
				}
				else
				{
					// HsServer (2026-10-08): onto a Mech of a full board — an 8th minion for this moment; it magnetizes away (MagneticTask)
					c.BoardZone.AllowOverflow = c.BoardZone.IsFull && MagnetizeHostAt(c, minion.Card, zonePosition) != null;
					try { c.BoardZone.Add(minion, zonePosition); }
					finally { c.BoardZone.AllowOverflow = false; }
				}
				SummonAppendages(game, minion); // HsServer: Colossal — before its other effects (wiki Colossal: Battlecry, if it has one)
				PowerHistoryMetaData enterPause = null;
				if (game.History) // HsServer: live SLUSH_TIME for a minion play, once it is on the board
					game.PowerHistory.Add(enterPause = new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 3000, Info = new List<int> { minion.Id } });
				// HsServer (2026-10-08): a "spent N Mana while holding this" card played (live 012951 95599 Felwood Treant): its glow off and
				// its played text (USE_ALTERNATE_CARD_TEXT 2) after the pause; its count is reset after its Battlecry (below)
				bool manaHeld = minion.Card.Power?.ManaSpentWhileHeld == true;
				if (manaHeld)
				{
					if (minion[GameTag.POWERED_UP] != 0) minion[GameTag.POWERED_UP] = 0;
					minion[GameTag.USE_ALTERNATE_CARD_TEXT] = 2;
				}
				// HsServer (TITANS): a card with its own Magnetic names the minion it will join in its TRIGGER block (effect index -1,
				// empty when it joins none: live) and joins it in its POWER block — from the client data, no script needed
				// (Generic.MagneticPlayBlock / Generic.Magnetize)
				bool magnetic = minion.Card.Modular && MagneticPlayBlock(game, minion) != null;

				// - PreSummon Phase --> PreSummon Phase Trigger (Tidecaller)
				//   (death processing, aura updates)
				// not Implemented

				// - OnPlay Phase --> OnPlay Trigger (Illidan)
				//   (death processing, aura updates)
				// HsServer: a minion's on-play replacement (Miniaturize, Gigantify) into the slot it was played from, before
				// the play triggers, as for spells (no minion recorded yet: UNCLEAR timing)
				OnPlayReplacement(c, minion, c.PlayedFromSlot);

				game.TaskQueue.StartEvent();
				// HsServer (2026-10-08): one On Play queue in order of play — rule book (Advanced_rulebook.txt:432) Hobgoblin "triggers alongside
				// Questing Adventurer and Illidan … (order determined by order of play)" (OnPlayTrigger ends the gathering)
				game.TaskQueue.StartGathering();
				game.TriggerManager.OnPlayMinionTrigger(minion);
				OnPlayTrigger.Invoke(game, minion);

				// HsServer: Countered (Objection!) - a minion whose play is countered leaves play without its Battlecry.
				if (minion[GameTag.CANT_PLAY] == 1)
				{
					// live (live-20261006-secrets-generators 5346-5371): the countered card's pause comes after the Secret's, not
					// when it entered play too (audit 2026-10-08: two 3000s)
					if (enterPause != null) game.PowerHistory.Retract(enterPause);
					CounteredPause(game, minion);
					if (minion.Zone?.Type == Zone.PLAY)
					{
						c.BoardZone.Remove(minion);
						c.SetasideZone.Add(minion); // live (Objection!, 4 of 4): set aside, not the Graveyard
					}
					game.DeathProcessingAndAuraUpdate();
					return true;
				}

				// - Summon Resolution Step (Work in Process)
				// (HsServer (TITANS): wiki Magnetic — "Magnetizing counts as playing a minion but not summoning a minion")
				game.TaskQueue.StartEvent();
				if (!magnetic) game.TriggerManager.OnSummonTrigger(minion);
				game.ProcessTasks();
				game.TaskQueue.EndEvent();

				// Noggenfogger here
				if (target != null)
				{
					game.TaskQueue.StartEvent();
					game.TriggerManager.OnTargetTrigger(minion);
					game.ProcessTasks();
					game.TaskQueue.EndEvent();
					if (minion.CardTarget != target.Id)
						target = game.IdEntityDic[minion.CardTarget];
				}

				// - BattleCry Phase --> Battle Cry Resolves
				//   (death processing, aura updates)
				int powerMark = game.History ? game.PowerHistory.Full.Count : 0;
				game.TaskQueue.StartEvent();
				if (magnetic) // HsServer (TITANS): Magnetic first; a script's own MagneticTask is the same step
					game.TaskQueue.Enqueue(new Tasks.SimpleTasks.MagneticTask(), c, minion, null);
				// HsServer (SPACE): Lucky Comet — the next Combo minion played spends a charge (wiki Lucky Comet: "any Combo minion
				// played after casting Lucky Comet"); its Combo then runs twice when Combo is active
				bool comboTwice = minion.Card[GameTag.COMBO] > 0 && c[HsTag.NEXT_COMBO_TWICE] > 0;
				if (comboTwice) c[HsTag.NEXT_COMBO_TWICE] = c[HsTag.NEXT_COMBO_TWICE] - 1;
				if (minion.Combo && c.IsComboActive)
				{
					int times = (comboTwice ? 2 : 1) * (c.ControllerAuraEffects[GameTag.EXTRA_MINION_BATTLECRIES_BASE] == 1 ? 2 : 1);
					for (int i = 0; i < times; i++)
						minion.ActivateTask(PowerActivation.COMBO, target);
				}
				else if (!(minion.Card.Power?.PowerTask is Tasks.SimpleTasks.MagneticTask))
					minion.ActivateTask(PowerActivation.POWER, target, chooseOne);

				// check if [LOE_077] Brann Bronzebeard aura is active
				// HsServer: a minion's own "Battlecries trigger twice" (Dark Gift Rude Awakening) doubles again with Brann.
				int battlecries = (minion.HasBattleCry ? c.BattlecryRuns : 1) * (minion.HasBattleCry && minion[Game.DOUBLE_BATTLECRY] > 0 ? 2 : 1); // (WHIZBANG: Shudderblock 3)
				for (int i = 1; i < battlecries; i++)
					ExtraRun(game, minion, () => minion.ActivateTask(PowerActivation.POWER, target, chooseOne));

				game.ProcessTasks();
				if (manaHeld && minion.Card[GameTag.TAG_SCRIPT_DATA_NUM_1] > 0) // (live: back to its threshold at its POWER block's end)
					minion[GameTag.TAG_SCRIPT_DATA_NUM_1] = minion.Card[GameTag.TAG_SCRIPT_DATA_NUM_1];
				PlayedPowerBlock(c, minion, powerMark); // its POWER block even with no effect, then its Overload (live)
				SecondPowerBlock(c, minion, target); // HsServer (LOST_CITY): a second effect's own POWER block (Kindred's index 1)
				game.TaskQueue.EndEvent();

				game.DeathProcessingAndAuraUpdate();

				// HsServer: a Battlecry that runs attacks (Festival Security's Finale) ends their events: keep the played minion
				minion = game.CurrentEventData?.EventSource as Minion ?? minion;

				// HsServer: "When you play this minion, summon a 2/2 copy of it." (Dark Gift Living Nightmare)
				if (minion[Game.LIVING_NIGHTMARE] > 0 && !c.BoardZone.IsFull && minion.Zone == c.BoardZone)
				{
					var copy = (Minion)Entity.FromCard(c, minion.Card);
					copy.AttackDamage = 2;
					copy.BaseHealth = 2;
					SummonBlock.Invoke(game, copy, minion.ZonePosition + 1, minion);
					game.DeathProcessingAndAuraUpdate();
				}

				// HsServer: rules A7 Rule 6 / B2 — the Sequence ends early once the played minion has left play or changed
				// sides: the later Phases' triggers (Mirror Entity, Snipe, Sword of Justice, Knife Juggler) don't run for it.
				bool StillHere() => minion.Zone == c.BoardZone && minion.Controller == c;
				// HsServer (TITANS): a magnetized minion was played (its after-play triggers run: live Mimiron after a Sparkbot) but
				// not summoned (no after-summon; wiki Magnetic)
				bool magnetized = magnetic && minion.Zone?.Type == Zone.SETASIDE && minion.Controller == c;

				// - After Play Phase --> After play Trigger / Secrets (Mirror Entity)
				//   (death processing, aura updates)
				//minion.JustPlayed = false;
				if (StillHere() || magnetized)
				{
					game.TaskQueue.StartEvent();
					game.TriggerManager.OnAfterPlayMinionTrigger(minion);
					game.ProcessTasks();
					game.TaskQueue.EndEvent();
					game.DeathProcessingAndAuraUpdate(); // inter-Phase steps
				}

				// - After Summon Phase --> After Summon Trigger
				//   (death processing, aura updates)
				if (StillHere() || magnetized)
				{
					game.TaskQueue.StartEvent();
					game.TriggerManager.OnAfterPlayCardTrigger(minion);
					if (!magnetized) AfterSummonTrigger.Invoke(game, minion, null);
					game.ProcessTasks();
					game.TaskQueue.EndEvent();
				}

				game.DeathProcessingAndAuraUpdate();

				if (minion.IsRace(Race.ELEMENTAL))
					c.NumElementalsPlayedThisTurn++;
				if (minion.IsRace(Race.MURLOC))
					c.NumMurlocsPlayedThisGame++;

				return true;
			};

		public static Func<Controller, Game, Spell, IPlayable, int, bool> PlaySpell
			=> delegate (Controller c, Game game, Spell spell, IPlayable target, int chooseOne)
			{
				Trigger.ValidateTriggers(game, spell, SequenceType.PlaySpell);

				PowerHistoryMetaData spellEnterPause = null;
				if (game.History)
				{
					if (spell.IsSecret || spell.IsQuest || spell.Card.IsDurationSpell)
						spell[GameTag.ZONE] = (int)Zone.SECRET;
					else
						spell[GameTag.ZONE] = (int)Zone.PLAY;
					// HsServer: live SLUSH_TIME for a spell play, right after it enters play
					game.PowerHistory.Add(spellEnterPause = new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 3000, Info = new List<int> { spell.Id } });
				}

				// - OnPlay Phase --> OnPlay Trigger (Illidan)
				//   (death processing, aura updates)
				// HsServer: rules (Twinspell, Drink spells) — the on-play replacement is added at once, before any "when you
				// cast" trigger, into the slot the spell was played from (a countered Twinspell still leaves it).
				OnPlayReplacement(c, spell, c.PlayedFromSlot);

				game.TaskQueue.StartEvent();
				game.TaskQueue.StartGathering(); // (HsServer 2026-10-08: one On Play queue in order of play — rule book :350, :432)
				game.TriggerManager.OnCastSpellTrigger(spell);
				OnPlayTrigger.Invoke(game, spell);

				game.Log(LogLevel.INFO, BlockType.ACTION, "PlaySpell", !game.Logging? "":$"{c.Name} plays Spell {spell} {(target != null ? "with target " + target.Card : "to board")}.");

				// check the spell is countered
				if (spell.IsCountered)
				{
					game.Log(LogLevel.INFO, BlockType.ACTION, "PlaySpell", !game.Logging ? "" : $"Spell {spell} has been countred.");
					// live (live-20261006-dredge 3680-3720): only the counter's pause, after the Secret's 5000 (audit 2026-10-08)
					if (spellEnterPause != null) game.PowerHistory.Retract(spellEnterPause);
					CounteredPause(game, spell);
					// live (Counterspell, 5 of 5): a countered card is set aside, not put in the Graveyard, and never counts as a
					// spell played (live counts it right before its effect, which it never reaches). User 2026-10-05: match live
					// (the rule book's "sends the spell to the Graveyard" predates it).
					c.SetasideZone.Add(spell);
				}
				else
				{
					// check Spellbender and Mayor Noggenfogger
					if (target != null)
					{
						game.TaskQueue.StartEvent();
						int temp = game.CurrentEventData.EventNumber;
						game.CurrentEventData.EventNumber = chooseOne;
						game.TriggerManager.OnTargetTrigger(spell);
						game.ProcessTasks();
						game.CurrentEventData.EventNumber = temp;
						game.TaskQueue.EndEvent();
						if (target.Id != spell.CardTarget)
						{
							target = spell.Game.IdEntityDic[spell.CardTarget];
							game.Log(LogLevel.DEBUG, BlockType.ACTION, "PlaySpell", !game.Logging ? "" : $"trigger Spellbender Phase. Target of {spell} is changed to {target}.");
						}
					}

					// HsServer (2026-10-08): counted right before its effect, as live (live-20261006-secrets-generators 224302 2311:
					// NUM_SPELLS_PLAYED_THIS_GAME right before the POWER block) — a spell reading the count sees itself
					c.NumSpellsPlayedThisGame++;
					if (spell.IsSecret)
						c.NumSecretsPlayedThisGame++;
					CastSpell.Invoke(c, game, spell, target, chooseOne);
					game.DeathProcessingAndAuraUpdate();
				}
				
				// trigger After Play Phase
				game.Log(LogLevel.DEBUG, BlockType.ACTION, "PlaySpell", !game.Logging? "":"trigger After Play Phase");
				game.TaskQueue.StartEvent();
				// HsServer (2026-10-08): one After Play queue in order of play (rule book :352)
				game.TaskQueue.StartGathering();
				game.TriggerManager.OnAfterCastTrigger(spell);
				game.TriggerManager.OnAfterPlayCardTrigger(spell);
				game.TaskQueue.EndGathering();
				game.ProcessTasks();
				game.TaskQueue.EndEvent();


				game.DeathProcessingAndAuraUpdate();

				return true;
			};

		public static Func<Controller, Game, Weapon, IPlayable, int, bool> PlayWeapon
			=> delegate (Controller c, Game game, Weapon weapon, IPlayable target, int chooseOne)
			{
				game.Log(LogLevel.INFO, BlockType.ACTION, "PlayWeapon", !game.Logging ? "" : $"{c.Hero} gets Weapon {c.Hero.Weapon}.");

				//c.Hero.AddWeapon(weapon);

				if (game.History)
				{
					weapon[GameTag.ZONE] = (int) Zone.PLAY;
					// HsServer: live SLUSH_TIME for a weapon play, once it is shown in play, before its POWER block
					// (Fiery War Axe, Light's Justice, Doomhammer: 3000)
					game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 3000, Info = new List<int> { weapon.Id } });
				}

				// HsServer (WHIZBANG): a weapon's Mini too (Boom Wrench; live vfx 236790: after SLUSH_TIME 3000, before its POWER block)
				OnPlayReplacement(c, weapon, c.PlayedFromSlot);

				// - OnPlay Phase --> OnPlay Trigger (Illidan)
				//   (death processing, aura updates)
				game.TaskQueue.StartEvent();
				OnPlayTrigger.Invoke(game, weapon);

				// not sure... need some test
				weapon.Card.Power?.Aura?.Activate(weapon);
				weapon.Card.Power?.Trigger?.Activate(weapon);


				if (target != null)
				{
					game.TaskQueue.StartEvent();
					game.TriggerManager.OnTargetTrigger(weapon);
					game.ProcessTasks();
					game.TaskQueue.EndEvent();
					if (target.Id != weapon.CardTarget)
						target = weapon.Game.IdEntityDic[weapon.CardTarget];
				}

				// - Equipping Phase --> Resolve Battlecry, OnDeathTrigger
				// activate battlecry
				// HsServer: its POWER block, then its Overload (live: the effect first; with no effect the Overload in its only
				// POWER block - PlayedPowerBlock)
				bool effect = weapon.Power?.PowerTask != null || weapon.Card.ChooseOne; // (a Choose One weapon: Barbed Thorn)
				if (game.History && effect)
					game.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.POWER, weapon.Id, "", 0, 0)); // HsServer: live effect index 0

				game.TaskQueue.StartEvent();
				weapon.ActivateTask(PowerActivation.POWER, target, chooseOne);
				if (weapon.Card[GameTag.BATTLECRY] == 1)
					for (int run = 1; run < c.NonMinionBattlecryRuns; run++) // (WHIZBANG: Shudderblock's 3; not Spirit of the Shark's minion-only)
						ExtraRun(game, weapon, () => weapon.ActivateTask(PowerActivation.POWER, target, chooseOne));
				game.ProcessTasks();
				game.TaskQueue.EndEvent();

				if (game.History && effect)
					game.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
				OverloadBlock(c, weapon, game.History, ownPowerBlock: !effect);

				// equip new weapon here
				game.TaskQueue.StartEvent();
				// destroy old weapon
				// equip new weapon
				// weapon's deathrattle task queues up here
				c.Hero.AddWeapon(weapon);
				game.ProcessTasks();
				game.TaskQueue.EndEvent();

				// deathprocessing;
				game.DeathProcessingAndAuraUpdate();

				// trigger After Play Phase
				game.Log(LogLevel.DEBUG, BlockType.ACTION, "PlayWeapon", !game.Logging? "":"trigger After Play Phase");
				game.TaskQueue.StartEvent();
				game.TriggerManager.OnAfterPlayCardTrigger(weapon);
				game.ProcessTasks();
				game.TaskQueue.EndEvent();

				game.DeathProcessingAndAuraUpdate();

				c.NumWeaponsPlayedThisGame++;

				return true;
			};

		/// <summary>
		/// HsServer: play a Location from hand into a board slot. Not a minion play: no minion counters, summon or Battlecry;
		/// "when you play a card" triggers fire. Live sends the slush time and an empty POWER block (RESEARCH/locations-protocol.md).
		/// </summary>
		public static bool PlayLocation(Controller c, Game game, Location location, int zonePosition)
		{
			game.Log(LogLevel.INFO, BlockType.ACTION, "PlayLocation", !game.Logging ? "" : $"{c.Name} plays Location {location} position {zonePosition}.");
			c.BoardZone.Add(location, zonePosition);
			if (game.History)
				game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 3000, Info = new List<int> { location.Id } });

			game.TaskQueue.StartEvent();
			OnPlayTrigger.Invoke(game, location);

			if (game.History)
			{
				game.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.POWER, location.Id, "", 0, 0));
				game.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			}

			game.TaskQueue.StartEvent();
			game.TriggerManager.OnAfterPlayCardTrigger(location);
			game.ProcessTasks();
			game.TaskQueue.EndEvent();
			game.DeathProcessingAndAuraUpdate();
			return true;
		}

		private static Action<Game, IPlayable> OnPlayTrigger
			=> delegate (Game game, IPlayable playable)
			{
				//playable.JustPlayed = true;
				game.TriggerManager.OnPlayCardTrigger(playable);
				if (game.TaskQueue.Gathering) game.TaskQueue.EndGathering(); // (the phase's triggers sorted by order of play)
				game.ProcessTasks();
				game.TaskQueue.EndEvent();

				game.DeathProcessingAndAuraUpdate();
			};
	}
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
