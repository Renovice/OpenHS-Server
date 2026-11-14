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
using SabberStoneCore.Model;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Tasks.SimpleTasks;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
	{
		public static Func<Controller, Game, int, bool> ChoicePick
			=> delegate (Controller c, Game g, int choice)
			{
				if (c.Choice.ChoiceType != ChoiceType.GENERAL && c.Choice.ChoiceType != ChoiceType.TARGET)
				{
					g.Log(LogLevel.WARNING, BlockType.ACTION, "ChoicePick", !g.Logging? "":$"Choice failed, trying to pick in a non-pick choice!");
					return false;
				}

				if (!c.Choice.Choices.Contains(choice))
				{
					g.Log(LogLevel.WARNING, BlockType.ACTION, "ChoicePick", !g.Logging? "":$"Choice failed, trying to pick a card that doesn't exist in this choice!");
					return false;
				}

				IPlayable playable = g.IdEntityDic[choice];
				bool discovered = IsDiscoverChoice(g, c.Choice); // HsServer (SPACE): every Discover pick (NoteDiscovered)

				g.Log(LogLevel.INFO, BlockType.ACTION, "ChoicePick", !g.Logging? "":$"{c.Name} Picks {playable.Card.Name} as choice!");

				// HsServer: live (Watfin live-20261008-120539, TTN_317 questline 2016): the picked card's 4741 and LAST_AFFECTED_BY = the choice's
				// source, before it moves
				if (g.IdEntityDic.TryGetValue(c.Choice.SourceId, out IPlayable chooser)) NoteAffected(playable, chooser);

				switch (c.Choice.ChoiceAction)
				{
					case ChoiceAction.HAND:
						if (RemoveFromZone(c, playable))
						{
							// HsServer (2026-10-08): the opponent's pick flies into its hand inside the chooser's to-hand effect (live: Source =
							// the Discover card, Targets = the pick — CATA_484 and the other recorded opponent Discovers); the client's own
							// pick gets none (live 618 of 618 picks: the client animates its own choice)
							bool pickFx = c != g.Player1 && chooser != null && g.History &&
							              g.OpenSubSpell(chooser, Loader.SubSpells.Find(chooser.Card.Id, Loader.SubSpells.Role.SpawnToHand), new[] { playable.Id });
							AddHandPhase.Invoke(c, playable);
							if (pickFx) g.CloseSubSpells(chooser.Id);
							// (HsServer: "after you Discover a card" — NoteDiscovered, below, for every Discover)
						}
						break;

					// HsServer: "Discover a card from your deck" is Generic.DiscoverFromDeck (copies offered, the pick drawn).

					case ChoiceAction.CAST:
						RemoveFromZone(c, playable);
						// a spell that stays in play (Secret, Quest, Sidequest, Paladin Aura: Arcane Keysmith "put it into the
						// battlefield") goes there as a cast; the rest resolves inside the chooser's block (live)
						if (playable is Spell stays && (stays.IsSecret || stays.IsQuest || stays[GameTag.SIDEQUEST] > 0 || stays.Card.IsDurationSpell))
							CastSpell.Invoke(c, g, stays, null, 0);
						else
							CastChosen(c, g, playable);
						break;

					case ChoiceAction.SPELL_RANDOM:
						if (RemoveFromZone(c, playable))
						{
							ICharacter randTarget = ((Playable) playable).GetRandomValidTarget();

							g.TaskQueue.StartEvent();
							CastSpell.Invoke(c, g, (Spell)playable, randTarget, 0);
							g.TaskQueue.EndEvent();
						}
						break;

					case ChoiceAction.SUMMON:
						if (!c.BoardZone.IsFull && RemoveFromZone(c, playable))
						{
							SummonBlock.Invoke(g, (Minion)playable, -1, g.IdEntityDic[c.Choice.SourceId]);
						}
						//if (RemoveFromZone(c, playable))
						//{
						//	g.TaskStack.Playables.Add(playable);
						//	g.TaskQueue.Enqueue(new SummonTask()
						//	{
						//		Game = g,
						//		Controller = c,
						//		Source = playable,
						//		Target = playable
						//	});
						//}
						break;

					case ChoiceAction.ADAPT:
						// HsServer (LOST_CITY): the player's Adaptations, in order (Ashalon: "give minions you play those Adaptations")
						if (c[HsTag.ADAPT_PICKS] < 20)
						{
							c[HsTag.ADAPT_PICK_BASE + c[HsTag.ADAPT_PICKS]] = playable.Card.AssetId;
							c[HsTag.ADAPT_PICKS] = c[HsTag.ADAPT_PICKS] + 1;
						}
						g.TaskQueue.StartEvent();
						foreach (IPlayable p in c.Choice.EntityStack.Select(id => g.IdEntityDic[id]))
							playable.ActivateTask(PowerActivation.POWER, (ICharacter)p);
						// Need to move the chosen adaptation to the Graveyard
						g.TaskQueue.Enqueue(new MoveToGraveYard(EntityType.SOURCE), in c, playable, null);
						g.ProcessTasks();
						g.TaskQueue.EndEvent();
						if (g.History)
						{
							// Send metadata to the client to hide the card
							g.PowerHistory.Add(new PowerHistoryMetaData
							{
								Type = MetaDataType.SHOW_BIG_CARD,
								Data = 2,
								Info = new List<int> { choice }
							});
						}
						break;

					//case ChoiceAction.TRACKING:
					//	if (RemoveFromZone(c, playable))
					//	{
					//		g.TaskQueue.Enqueue(new AddCardTo(playable, EntityType.HAND)
					//		{
					//			Game = g,
					//			Controller = c,
					//			Source = playable,
					//			Target = playable
					//		});
					//	}
					//	break;

					case ChoiceAction.HEROPOWER:
						if (RemoveFromZone(c, playable))
						{
							playable[GameTag.CREATOR] = c.Hero.Id;
							g.Log(LogLevel.INFO, BlockType.PLAY, "ReplaceHeroPower",
								!g.Logging ? "" : $"{c.Hero} power replaced by {playable}");

							c.SetasideZone.Add(c.Hero.HeroPower);
							c.Hero.HeroPower = (HeroPower) playable;
						}
						break;

					case ChoiceAction.STACK:
						c.Choice.AddToStack(choice);
						break;
					case ChoiceAction.KAZAKUS:
						//c.Choice.Choices.Where(p => p != choice).ToList().ForEach(p =>
						//{
						//	g.IdEntityDic[p][GameTag.TAG_SCRIPT_DATA_NUM_1] = 0;
						//});
						////c.Setaside.Add(playable);
						//var kazakusPotions =
						//	c.SetasideZone.Where(p => p.Card.Id.StartsWith("CFM_621"))
						//		.Where(p => p[GameTag.TAG_SCRIPT_DATA_NUM_1] > 0)
						//		.Select(p => p[GameTag.TAG_SCRIPT_DATA_NUM_1])
						//		.ToList();
						//if (kazakusPotions.Any())
						//{
						//	g.TaskQueue.Enqueue(new PotionGenerating(kazakusPotions), in c, playable, null);
						//}
						if (playable.Power == null)
							c.Choice.EntityStack = new List<int> {playable.Id};
						else
							c.Choice.AddToStack(playable.Id);

						KazakusPower.Action(in g, in c, c.Choice.EntityStack);
						break;
					case ChoiceAction.GLIMMERROOT:
						if (c.Opponent.DeckCards.Select(p => p.Id).Contains(playable.Card.Id))
						{
							if (RemoveFromZone(c, playable))
								AddHandPhase.Invoke(c, playable);
						}
						break;

					case ChoiceAction.BUILDABEAST:
						//if (c.Choice.ChoiceQueue.Count == 0)
						if (c.Choice.NextChoice == null)
						{
							Card firstCard = g.IdEntityDic[c.Choice.LastChoice].Card.Clone();
							Card secondCard = playable.Card;
							Card zombeastCard = Card.CreateZombeastCard(in firstCard, in secondCard, g.History);

							IPlayable zombeast = Entity.FromCard(in c, in zombeastCard);
							zombeast[GameTag.DISPLAYED_CREATOR] = playable[GameTag.DISPLAYED_CREATOR];
							// HsServer (events): "Craft a custom Zombeast. It costs (3) less." (Storm the Gates: HsTag.CRAFTED_ZOMBEAST_DISCOUNT)
							if (c[HsTag.CRAFTED_ZOMBEAST_DISCOUNT] > 0)
							{
								zombeast[GameTag.COST] = Math.Max(0, zombeast.Cost - c[HsTag.CRAFTED_ZOMBEAST_DISCOUNT]);
								c[HsTag.CRAFTED_ZOMBEAST_DISCOUNT] = 0;
							}

							AddHandPhase.Invoke(c, zombeast);
							break;
						}
						else
							break;


					default:
						throw new NotImplementedException();
				}

				//if (c.Choice.EnchantmentCard != null)
				//	AddEnchantmentBlock.Invoke(c, c.Choice.EnchantmentCard, g.IdEntityDic[c.Choice.SourceId], playable, 0, 0, 0);

				// aftertask here
				if (c.Choice.AfterChooseTask != null)
				{
					// choice creator as Source
					// selected card as Target
					//g.TaskQueue.Enqueue(c.Choice.AfterChooseTask, in c, g.IdEntityDic[playable[GameTag.CREATOR]], playable);
					c.Choice.AfterChooseTask.Process(in g, in c, g.IdEntityDic[c.Choice.SourceId], playable,
						new TaskStack()
						{
							Playables = c.Choice.EntityStack?.Select(id => g.IdEntityDic[id]).ToArray()
						});
				}

				// HsServer (SPACE): a Discover pick counts and fires "after you Discover a card" once it is complete (after the
				// card's own after-pick step), whatever became of it (hand, cast, summoned, drawn from the deck)
				if (discovered)
					NoteDiscovered(c, playable);

				// set displayed creator at least for discover
				//playable[GameTag.DISPLAYED_CREATOR] = c.LastCardPlayed;

				//	Start next Choice if any choice is queueing up
				//if (c.Choice.ChoiceQueue.Any())
				//{
				//	Choice nextChoice = c.Choice.ChoiceQueue.Dequeue();
				//	nextChoice.ChoiceQueue = c.Choice.ChoiceQueue;
				//	c.Choice = nextChoice;
				//	c.Choice.LastChoice = choice;
				//}
				// HsServer: the offering effect's continuation (Choice.ThenTask) waits for its own next pick, else runs now
				ISimpleTask then = c.Choice.ThenTask;
				int thenSource = c.Choice.SourceId;
				if (c.Choice.TryPopNextChoice(choice, out Choice nextChoice))
				{
					c.Choice = nextChoice;
					if (then != null && nextChoice.SourceId == thenSource && nextChoice.ThenTask == null)
					{
						nextChoice.ThenTask = then;
						then = null;
					}
				}
				else
				{
					// reset choice it's done
					c.Choice = null;
				}
				if (then != null && g.IdEntityDic.TryGetValue(thenSource, out IPlayable thenFrom))
				{
					then.Process(in g, in c, thenFrom, null);
					g.ProcessTasks();
				}

				return true;
			};

		/// <summary>HsServer (SPACE): is this choice a Discover — its source card (or, for an enchantment, its creator's card) has the
		/// client DISCOVER tag (rules: "Only cards whose text says 'Discover' count as Discover")?</summary>
		public static bool IsDiscoverChoice(Game g, Choice choice)
		{
			if (choice == null || choice.ChoiceType != ChoiceType.GENERAL || !g.IdEntityDic.TryGetValue(choice.SourceId, out IPlayable source)) return false;
			if (source is Enchantment e && e.Creator != null) source = e.Creator;
			// (HsServer LOST_CITY: or its text says Discover — Shokk, Jungle Tyrant has no DISCOVER tag; wiki Discover: "Cards are
			// only considered Discover effects when they say Discover on their card text")
			return source.Card[GameTag.DISCOVER] > 0 || source.Card.Text?.Contains("<b>Discover") == true;
		}

		/// <summary>HsServer (SPACE): a card was Discovered — the player's counters (this game / this turn) and "after you Discover a
		/// card" triggers (Rangari Scout, Dwarven Archaeologist).</summary>
		public static void NoteDiscovered(Controller c, IPlayable picked)
		{
			// (HsServer LOST_CITY: the unpicked options — The Origin Stone "plays the other options")
			var others = c.Choice?.Choices?.Where(id => id != picked?.Id).ToList();
			c[HsTag.LAST_DISCOVER_OTHER_A] = others?.Count > 0 ? others[0] : 0;
			c[HsTag.LAST_DISCOVER_OTHER_B] = others?.Count > 1 ? others[1] : 0;
			c[HsTag.DISCOVERED_THIS_GAME] = c[HsTag.DISCOVERED_THIS_GAME] + 1;
			if (c[HsTag.DISCOVERED_TURN] != c.Game.Turn) { c[HsTag.DISCOVERED_TURN] = c.Game.Turn; c[HsTag.DISCOVERED_THIS_TURN] = 0; }
			c[HsTag.DISCOVERED_THIS_TURN] = c[HsTag.DISCOVERED_THIS_TURN] + 1;
			AfterDiscover(c, picked);
		}

		/// <summary>The player's Discovers this turn.</summary>
		public static int DiscoveredThisTurn(Controller c) => c[HsTag.DISCOVERED_TURN] == c.Game.Turn ? c[HsTag.DISCOVERED_THIS_TURN] : 0;

		/// <summary>HsServer: fire "after you Discover a card" triggers for a picked card.</summary>
		public static void AfterDiscover(Controller c, IPlayable picked)
		{
			c.Game.TaskQueue.StartEvent();
			c.Game.TriggerManager.OnAfterDiscoverTrigger(picked);
			c.Game.ProcessTasks();
			c.Game.TaskQueue.EndEvent();
		}

		public static Func<Controller, List<int>, bool> ChoiceMulligan
			=> delegate (Controller c, List<int> choices)
			{
				if (c.Choice.ChoiceType != ChoiceType.MULLIGAN)
				{
					c.Game.Log(LogLevel.WARNING, BlockType.ACTION, "ChoiceMulligan", !c.Game.Logging? "":$"Choice failed, trying to mulligan in a non-mulligan choice!");
					return false;
				}

				if (!choices.TrueForAll(p => c.Choice.Choices.Contains(p)))
				{
					c.Game.Log(LogLevel.WARNING, BlockType.ACTION, "ChoiceMulligan", !c.Game.Logging? "":$"Choice failed, trying to mulligan a card that doesn't exist in this choice!");
					return false;
				}

				switch (c.Choice.ChoiceAction)
				{
					case ChoiceAction.HAND:
						c.MulliganState = Mulligan.DEALING;

						// starting mulligan draw block
						if (c.Game.History)
							c.Game.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, c.Id, "", -1, 0)); // HsServer: live -1

						// HsServer: the cards offered and not kept (was: every hand card but the Coin, by its id)
						var mulliganList = c.HandZone.Where(p => c.Choice.Choices.Contains(p.Id) && !choices.Contains(p.Id)).ToList();
						// HsServer: the replacements are taken before any returned card goes back, so a card you mulligan
						// is never drawn again (it could be shuffled onto the top before the next draw).
						var replacements = new Queue<IPlayable>(mulliganList.Select(_ => c.DeckZone.Remove(c.DeckZone.TopCard)).ToList());
						mulliganList.ForEach(p =>
						{
							// drawing a new one
							IPlayable playable = replacements.Dequeue();

							// HsServer: it takes the returned card's slot, no other card moves (live: new card ZONE=HAND + ZONE_POSITION =
							// the slot, then the old one ZONE_POSITION 0 → DECK; was: added right-most, then swapped)
							AddHandPhaseAt(c, playable, -1, replacing: p);

							// removing old one (HsServer: live sets its ZONE_POSITION to 0 as it leaves the hand)
							p[GameTag.ZONE_POSITION] = 0;
							if (p.Zone != null) RemoveFromZone(c, p);
							ShuffleIntoDeck.Invoke(c, null, p);
						});

						c.MulliganState = Mulligan.WAITING; // HsServer: live MULLIGAN_STATE 3 at the end of the swap block
						if (c.Game.History)
							c.Game.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());

						c.OpeningHand = c.HandZone.Select(p => p.Card).ToList(); // HsServer: Hex Lord Malacrass

						// reset choice it's done
						c.Choice = null;

						break;

					default:
						throw new NotImplementedException();
				}



				return true;
			};

		public static Func<Controller, IEntity, ChoiceType, ChoiceAction, List<int>, bool> CreateChoice
			=> delegate (Controller c, IEntity source, ChoiceType type, ChoiceAction action, List<int> choices)
			{
				if (c.Choice != null)
				{
					c.Game.Log(LogLevel.WARNING, BlockType.ACTION, "CreateChoice", !c.Game.Logging? "":$"there is an unresolved choice, can't add a new one!");
					return false;
				}

				c.Choice = new Choice(c)
				{
					ChoiceType = type,
					ChoiceAction = action,
					Choices = choices,
					SourceId = source.Id
				};
				return true;
			};

		public static Func<Controller, IEntity, IList<IPlayable>, ChoiceType, ChoiceAction, Card[], ISimpleTask, bool> CreateChoiceCards
			=> delegate (Controller c, IEntity source, IList<IPlayable> targets, ChoiceType type, ChoiceAction action, Card[] choices, ISimpleTask taskToDo)
			{
				//if (c.Choice != null)
				//{
				//	c.Game.Log(LogLevel.WARNING, BlockType.ACTION, "CreateChoice", $"there is an unresolved choice, can't add a new one!");
				//	return false;
				//}

				// HsServer (TITANS review 2026-10-07): the card's held-back buff effect (Game.OpenSubSpellAtFirstBuff) opens before the
				// options: a Discover makes no buff (live live-20261007-vfx-decks 581-585, Discovery of Magic: its Runes SUB_SPELL,
				// empty, then the options' FULL_ENTITYs)
				if (type == ChoiceType.GENERAL) c.Game.FlushPendingFx(source.Id);
				var choicesIds = new List<int>();
				foreach (Card p in choices)
				{
					IPlayable choiceEntity = Entity.FromCard(c, p,
						new EntityData
						{
							{GameTag.CREATOR, source.Id},
							{GameTag.DISPLAYED_CREATOR, source.Id }
						}, c.SetasideZone);
					if (type == ChoiceType.GENERAL)
						choiceEntity[HsTag.WAS_DISCOVER_OPTION] = 1; // HsServer: live, right after each option's FULL_ENTITY
					choicesIds.Add(choiceEntity.Id);
				}

				var choice = new Choice(c)
				{
					ChoiceType = type,
					ChoiceAction = action,
					Choices = choicesIds,
					SourceId = source.Id,
					//TargetIds = targets != null ? targets.Select(p => p.Id).ToList() : new List<int>(),
					//EnchantmentCard = enchantmentCard,
					AfterChooseTask = taskToDo,

					EntityStack = targets?.Select(p => p.Id).ToList()
				};


				if (c.Choice != null)
				{
					//c.Choice.ChoiceQueue.Enqueue(choice);

					Choice next = c.Choice;
					while (next.NextChoice != null)
						next = next.NextChoice;

					next.NextChoice = choice;
				}
				else
					c.Choice = choice;

				return true;
			};

		/// <summary>
		/// HsServer: a chosen card its chooser casts (Bob the Bartender's actions, Champions of Azeroth's sides…). Live
		/// (live-20261005, Bob's four actions 8/8; Tiny Pal JAIL_458): it resolves INSIDE the chooser's block — the card goes to
		/// the graveyard, is shown big (Bob: SHOW_BIG_CARD 1 and 2, HISTORY_TARGET), then its effect runs; no PLAY / POWER block
		/// of its own (no recording has one). Behind the scenes a cast, but not "you cast a spell" (user 2026-10-05: it doesn't
		/// trigger Mana Wyrm): no cast triggers, no spells-played count.
		/// </summary>
		private static void CastChosen(Controller c, Game g, IPlayable card)
		{
			c.GraveyardZone.Add(card);
			if (g.History)
			{
				var shown = new List<int> { card.Id };
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SHOW_BIG_CARD, Data = 1, Info = shown });
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SHOW_BIG_CARD, Data = 2, Info = shown });
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.HISTORY_TARGET, Data = 0, Info = shown });
			}
			if (card.Card.Power?.PowerTask is Tasks.ISimpleTask task)
			{
				task.Process(g, c, card, null);
				g.ProcessTasks();
			}
		}

		/// <summary>
		/// HsServer: "(You pick the targets!)" — aim, shoot, then aim again (user 2026-10-04: Night Elf Huntress "goes aim
		/// shoot, next aim shoot, third aim shoot"). Each shot is a TARGET choice (client CHOICE_TYPE.TARGET: the targeting
		/// arrow from <paramref name="source"/>) between the valid characters in play; the pick runs
		/// <paramref name="onPick"/>, then the next shot is offered. "Different" targets: one already picked isn't offered
		/// again. Fewer valid targets than shots: the shots end when none is left.
		/// Live (Night Elf Huntress, live-20261005 165219): each shot plays inside the card's still-open POWER block (its
		/// recorded missile per hit: ReuseFX_Generic_Missile_Arrow_Super from the Huntress, TARGET + DAMAGE in it), then
		/// <paramref name="counter"/> on the card counts the shots left (3 from the card data: 2, 1, 0), then the next pick.
		/// The counter is what the client's arrow text reads ("Deal 3 damage. (@ left!)": Card.TargetArrowText with its
		/// CardTextBuilder: TAG_SCRIPT_DATA_NUM_1 for Huntress / Goliath, TAG_SCRIPT_DATA_NUM_2 for Grunty).
		/// </summary>
		/// <summary>
		/// HsServer (TITANS review 2026-10-07): can <paramref name="chooser"/> pick <paramref name="ch"/> with a "choose …" made
		/// while the effect resolves (PickTargets)? The rule of a target (Card.TargetingRequirements): never an untouchable (Dormant,
		/// a permanent, a Location); an enemy in Stealth or Immune can't be chosen (RESEARCH/sources/wiki/Stealth.txt: "Characters
		/// in Stealth cannot be directly targeted by the opponent in any way"); <paramref name="bySpell"/>: not an Elusive one
		/// either (CANT_BE_TARGETED_BY_SPELLS). Alive, not about to die.
		/// </summary>
		public static bool Pickable(Controller chooser, ICharacter ch, bool bySpell)
		{
			if (ch == null || ch.IsDead || ch is Minion { ToBeDestroyed: true } || ch.Zone?.Type != Zone.PLAY && !(ch is Hero)) return false;
			if (ch.Card.Untouchable || ch is Minion { Untouchable: true }) return false;
			if (ch.Controller != chooser && (ch.HasStealth || ch.IsImmune)) return false;
			return !(bySpell && ch.CantBeTargetedBySpells);
		}

		/// <summary>Does <paramref name="chooser"/>'s opponent have a minion <paramref name="chooser"/> could pick (Pickable)? The
		/// play check of a "choose an enemy minion" picked while resolving (Power.PlayableIf: Strike from History, Chaotic
		/// Consumption).</summary>
		public static bool HasPickableEnemyMinion(Controller chooser, bool bySpell) =>
			chooser.Opponent.BoardZone.Any(m => Pickable(chooser, m, bySpell));

		/// <param name="betweenShots">HsServer (TITANS review): what the card does after a shot when it has shots left, before the
		/// next aim; its bool: whether a target is left for it (live Strike from History, live-20261007-vfx-decks 42738 / 48028:
		/// EFFECT_TIMING, then the next pick — or, with none left, its effect played empty).</param>
		public static void PickTargets(Controller c, IEntity source, int shots, Func<ICharacter, bool> valid,
			Action<Game, Controller, IEntity, ICharacter> onPick, bool different = true, HashSet<int> picked = null,
			GameTag? counter = null, Action<Game, Controller, IEntity, bool> betweenShots = null)
			=> PickTargets(c, source, shots, valid, (g, c2, s, t, before) => onPick(g, c2, s, t), different, picked, counter, false, betweenShots,
				Array.Empty<int>());

		/// <summary>
		/// <see cref="PickTargets(Controller, IEntity, int, Func{ICharacter, bool}, Action{Game, Controller, IEntity, ICharacter}, bool, HashSet{int}, GameTag?, Action{Game, Controller, IEntity, bool})"/>
		/// whose pick also gets the ids picked before it in this game, in order (its shot number = their count). A pick may
		/// resolve on an AI's copy of the game (the choice's task is shared): a card's pick never captures entities or counters
		/// of its own — it reads what it needs from these ids and the game it gets (soak 842017 / 843074: Grunty shot the
		/// real Murlocs from a copy, the real Tidecaller got an enchantment the client never saw).
		/// </summary>
		public static void PickTargetsInOrder(Controller c, IEntity source, int shots, Func<ICharacter, bool> valid,
			Action<Game, Controller, IEntity, ICharacter, IReadOnlyList<int>> onPick, bool different = true, GameTag? counter = null,
			Action<Game, Controller, IEntity, bool> betweenShots = null)
			=> PickTargets(c, source, shots, valid, onPick, different, null, counter, false, betweenShots, Array.Empty<int>());

		private static List<int> PickableTargets(Controller c, Func<ICharacter, bool> valid, bool different, HashSet<int> picked) =>
			new ICharacter[] { c.Hero, c.Opponent.Hero }
				.Concat(c.BoardZone).Concat(c.Opponent.BoardZone)
				.Where(ch => ch != null && !ch.IsDead && !(ch is Minion m && m.ToBeDestroyed) && valid(ch) && !(different && picked.Contains(ch.Id)))
				.Select(ch => ch.Id).ToList();

		private static void PickTargets(Controller c, IEntity source, int shots, Func<ICharacter, bool> valid,
			Action<Game, Controller, IEntity, ICharacter, IReadOnlyList<int>> onPick, bool different, HashSet<int> picked, GameTag? counter,
			bool nextShot, Action<Game, Controller, IEntity, bool> betweenShots, IReadOnlyList<int> order)
		{
			picked ??= new HashSet<int>();
			List<int> targets = PickableTargets(c, valid, different, picked);
			if (shots <= 0 || targets.Count == 0) return;
			var choice = new Choice(c)
			{
				ChoiceType = ChoiceType.TARGET,
				ChoiceAction = ChoiceAction.STACK,
				Choices = targets,
				SourceId = source.Id,
				AfterChooseTask = new CustomTask((g, c2, s, t, stack) =>
				{
					if (!(t is ICharacter target)) return;
					// a new set, never the captured one: a cloned game shares this task (the AI tries each pick on a clone;
					// adding to the shared set marked every enemy it tried as shot, so its Night Elf Huntress stopped after one)
					var shot = new HashSet<int>(picked) { target.Id };
					onPick(g, c2, s, target, order);
					if (g.History) g.CloseSubSpells(s.Id); // this shot's effect ends with it (live: one per shot)
					if (counter is GameTag left && s is IPlayable card)
						card[left] = shots - 1;
					// each shot resolves before the next aim (user 2026-10-04: "each subsequent shot resolves the event, so a
					// deathrattle for example will trigger"): deaths and Deathrattles first, then the next shot's targets
					g.ProcessTasks();
					g.DeathProcessingAndAuraUpdate();
					if (shots > 1)
						betweenShots?.Invoke(g, c2, s, PickableTargets(c2, valid, different, shot).Count > 0);
					PickTargets(c2, s, shots - 1, valid, onPick, different, shot, counter, true, betweenShots, order.Append(target.Id).ToList());
				})
			};
			if (c.Choice != null && nextShot)
			{
				// HsServer (TITANS review 2026-10-07): the next shot follows the one just picked (still c.Choice while its
				// AfterChooseTask runs), before whatever the card queued after it — a card's shots stay together (live
				// live-20261007-vfx-decks 42717-42760: Strike from History's two picks, then Aman'Thul's Discover)
				choice.NextChoice = c.Choice.NextChoice;
				c.Choice.NextChoice = choice;
			}
			else if (c.Choice != null)
			{
				Choice next = c.Choice;
				while (next.NextChoice != null)
					next = next.NextChoice;
				next.NextChoice = choice;
			}
			else
				c.Choice = choice;
		}

		/// <summary>
		/// HsServer: a choice between entities already built in the set-aside zone (so they can be enchanted first and the
		/// client shows them that way: "Discover a Beast with a Dark Gift"). Queues behind an open choice like CreateChoiceCards.
		/// </summary>
		public static bool CreateChoiceEntities(Controller c, IEntity source, IList<IPlayable> offered, ChoiceAction action, ISimpleTask taskToDo)
		{
			var choice = new Choice(c)
			{
				ChoiceType = ChoiceType.GENERAL,
				ChoiceAction = action,
				Choices = offered.Select(p => p.Id).ToList(),
				SourceId = source.Id,
				AfterChooseTask = taskToDo
			};
			if (c.Choice != null)
			{
				Choice next = c.Choice;
				while (next.NextChoice != null)
					next = next.NextChoice;
				next.NextChoice = choice;
			}
			else
				c.Choice = choice;
			return true;
		}
	}
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
