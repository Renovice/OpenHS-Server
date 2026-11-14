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
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Triggers;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
	{
		/// <summary>HsServer: attacks a character may make per turn: 1, 2 with Windfury, 4 with Mega-Windfury (MEGA_WINDFURY, its
		/// own or granted by an aura such as Whirlwind Tempest's).</summary>
		/// <summary>
		/// HsServer (2026-10-08): a character's readiness to attack follows what it may do NOW. Wiki Windfury.txt:12 (Raging Worgen loses
		/// Windfury after one attack: "It may no longer attack"), Windfury.txt:14 (attacks this turn count "even those made with a
		/// different weapon"), x_Attack.txt:42 (losing Charge before attacking "would restore the exhausted status"), Titan.txt:33 (a
		/// Titan silenced after using an ability "will not be able to attack"). One that attacked this turn is exhausted exactly when
		/// its attacks reach what it may make now (losing or gaining Windfury / Mega-Windfury, another weapon); one that entered play
		/// this turn and lost Charge goes back to sleep (to "minions only" with Rush). Called where those abilities or the weapon change.
		/// </summary>
		public static void RefreshAttackReady(ICharacter ic)
		{
			if (!(ic is Character c) || c.Zone?.Type != Zone.PLAY || c.Controller != c.Game.CurrentPlayer) return;
			int used = c.NumAttacksThisTurn;
			if (used > 0)
			{
				bool spent = used >= AttacksPerTurn(c);
				if (spent != c.IsExhausted) c.IsExhausted = spent;
				return;
			}
			if (c is Minion m && m[GameTag.NUM_TURNS_IN_PLAY] == 0 && !m.IsExhausted && !m.HasCharge && !m.Card.IsTitan && !m.AttackableByRush)
			{
				if (m.IsRush)
				{
					m.AttackableByRush = true;
					if (!m.Game.RushMinions.Contains(m.Id)) m.Game.RushMinions.Add(m.Id);
				}
				else
					m.IsExhausted = true;
			}
		}

		public static int AttacksPerTurn(ICharacter c)
		{
			int extra = c is Minion m && m[HsTag.EXTRA_ATTACKS_TURN] == m.Game.Turn ? m[HsTag.EXTRA_ATTACKS] : 0; // (SPACE: Exarch Akama)
			if (c[GameTag.MEGA_WINDFURY] > 0) return 4 + extra;
			if (!c.HasWindfury) return 1 + extra;
			return 2 + extra;
		}

		public static Func<Controller, ICharacter, ICharacter, bool, bool, bool> AttackBlock
			=> delegate (Controller c, ICharacter source, ICharacter target, bool skipPrePhase, bool skipDeathPhase)
			{
				Game g = c.Game;
				// HsServer (2026-10-08): ordered by the player (MinionAttackTask / HeroAttackTask), or forced by an effect — read once, so an
				// attack an effect makes inside this one is a forced one
				bool ordered = g.AttackOrdered;

				if (skipPrePhase)
				{
					g.AttackOrdered = false;
					OpenAttack(c, source, target, !ordered);
					g.CurrentEventData = new EventMetaData(source, target);
				}
				else
				{
					bool proposed = PreAttackPhase.Invoke(c, source, target); // (opens the block: reads AttackOrdered)
					g.AttackOrdered = false;
					if (!proposed)
						return false;
				}

				// HsServer (2026-10-07): "Immune while attacking" — rule book (Advanced_rulebook.txt:509): "If you have a weapon
				// that grants Immunity while attacking ... at the start of Combat Sequence, you are Immune in the whole
				// Sequence." Decided here, at the Sequence's start (the attacker's own IMMUNE_WHILE_ATTACKING or its weapon's);
				// Character.TakeDamage prevents the damage while it is marked. Cleared with the Sequence's combat (below).
				bool immuneThisAttack = ImmuneWhileAttacking(source);
				if (immuneThisAttack) source[HsTag.IMMUNE_THIS_ATTACK] = 1;
				void EndImmunity()
				{
					if (immuneThisAttack && source[HsTag.IMMUNE_THIS_ATTACK] > 0) source[HsTag.IMMUNE_THIS_ATTACK] = 0;
				}

				// HsServer (TITANS review 2026-10-07): a Titan's forced attack (an effect makes it attack: Khaz'goroth's "attack a random
				// enemy minion", Trueaim Crescent) keeps its action — wiki Titan (RESEARCH/sources/wiki/Titan.txt:30): "A forced attack
				// does not execute any one of a Titan's abilities or consume its action for the turn" (no NUM_ATTACKS_THIS_TURN, no
				// EXHAUSTED for it; the attack still counts as one, client tag 4163)
				// (forced = not ordered by a player: the pre-phase flag alone also covered the options' default, the AI's normal attacks)
				bool keepsAction = !ordered && IsTitan(source as IPlayable);
				if (!OnAttackTrigger.Invoke(g, source, target, keepsAction))
				{
					EndImmunity();
					// end block
					if (g.History)
						g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
					if (!skipDeathPhase)
						g.DeathProcessingAndAuraUpdate();
					g.CurrentEventData = null;
					EndPlayerAttack(c, source, skipPrePhase);
					return false;
				}
				Trigger.ValidateTriggers(g, source, SequenceType.Target);
				if (!AttackPhase.Invoke(c, source, skipDeathPhase, keepsAction))
				{
					EndImmunity();
					// end block
					if (g.History)
						g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
					if (!skipDeathPhase)
						g.DeathProcessingAndAuraUpdate();
					g.CurrentEventData = null;
					EndPlayerAttack(c, source, skipPrePhase);
					return false;
				}
				// end block
				if (g.History)
					g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
				TitanAfterAttack(g, source, target); // HsServer (TITANS): a Titan's after-attack block (live)

				// (still Immune through its after-attack triggers: the same Sequence)
				g.TaskQueue.StartEvent();
				source.OnAfterAttackTrigger();
				g.ProcessTasks();
				g.TaskQueue.EndEvent();
				EndImmunity();

				if (!skipDeathPhase)
					g.DeathProcessingAndAuraUpdate();
				g.CurrentEventData = null;
				EndPlayerAttack(c, source, skipPrePhase);
				// HsServer: a forced attack at the end of a turn (Mythical Terror) must not step the game back to MAIN_ACTION.
				if (g.Step != Step.MAIN_END)
					g.NextStep = Step.MAIN_ACTION;

				return true;
			};

		/// <summary>HsServer (2026-10-07): does <paramref name="attacker"/> have "Immune while attacking" — its own
		/// IMMUNE_WHILE_ATTACKING (client tag 373: Lady S'theno, Gangplank Diver, Tavish's Ram), or, for a hero, its weapon's
		/// ("Your hero is Immune while attacking": Spark of Ra-den's crafted weapon, Swift Slash, BAR_321e).</summary>
		public static bool ImmuneWhileAttacking(ICharacter attacker) =>
			attacker != null && (attacker[GameTag.IMMUNE_WHILE_ATTACKING] > 0 ||
			                     attacker is Hero hero && hero.Weapon != null && hero.Weapon[GameTag.IMMUNE_WHILE_ATTACKING] > 0);

		/// <summary>HsServer: the end of an attack the player chose (live, 3104 of 3104 attacks, a cancelled one too: Explosive
		/// Trap killing the attacker): after the ATTACK block and its deaths, NUM_OPTIONS_PLAYED_THIS_TURN, then SLUSH_TIME 1000
		/// on the attacker (the client lets the swing and its damage finish). An attack made by an effect (skipPrePhase:
		/// Mythical Terror, Deathwing) is no option played and has no such pause.</summary>
		private static void EndPlayerAttack(Controller c, ICharacter source, bool skipPrePhase)
		{
			if (skipPrePhase) return;
			c.NumOptionsPlayedThisTurn++;
			if (c.Game.History)
				c.Game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 1000, Info = new List<int> { source.Id } });
		}

		/// <summary>HsServer: live (Freezing Trap, Vaporize: SHOULDEXITCOMBAT 1 on the attacker in the Secret's block, before
		/// its effect; 0 at the attack's end): an effect that takes the proposed attacker out of the fight before it swings —
		/// destroys it, returns it to hand, transforms it — tells the client to drop the swing. Damage that kills it doesn't
		/// (Explosive Trap). Called by Destroy, ReturnToHandBlock and TransformBlock.</summary>
		internal static void ExitCombat(IPlayable p)
		{
			Game g = p?.Game;
			if (g == null || g.ProposedAttacker != p.Id && g.AttackingSource != p.Id || p.Id == 0 || p[GameTag.ATTACKING] == 1 ||
			    p[GameTag.SHOULDEXITCOMBAT] == 1)
				return;
			// (VIOLET_HOLD: also without history — the attack is cancelled by it, Mystic Misdirection's Sheep)
			if (g.History) p[GameTag.SHOULDEXITCOMBAT] = 1;
			g.ExitingCombat = p.Id;
		}

		// HsServer: the attack's end clears SHOULDEXITCOMBAT (live: right after PROPOSED_* go to 0) — also on a minion
		// returned to hand, whose tags the move already reset inside the engine (the client still has the 1).
		private static void EndExitCombat(Game g, IPlayable attacker)
		{
			// HsServer (VIOLET_HOLD): the attack is over — its attacker and "was Stealthed" mark go
			if (g.AttackingSource == attacker.Id) g.AttackingSource = 0;
			if (attacker[HsTag.ATTACKED_STEALTHED] != 0) attacker[HsTag.ATTACKED_STEALTHED] = 0;
			if (g.ExitingCombat == 0 || g.ExitingCombat != attacker.Id) return;
			g.ExitingCombat = 0;
			if (attacker[GameTag.SHOULDEXITCOMBAT] == 1)
				attacker[GameTag.SHOULDEXITCOMBAT] = 0;
			else if (g.History)
				g.PowerHistory.AddUnchecked(PowerHistoryBuilder.TagChange(attacker.Id, GameTag.SHOULDEXITCOMBAT, 0));
		}

		/// <summary>
		/// HsServer (2026-10-08): Poisonous destroys a damaged minion (rules D2) in its own TRIGGER block, as live (133 of 133; progression
		/// 28991): the damage source, effect index -1, TriggerKeyword POISONOUS; META TARGET and META POISONOUS on the minion, the destroy
		/// (2747 = the source, TO_BE_DESTROYED), then SLUSH_TIME 200 on the source. In combat it waits until both hits are dealt.
		/// </summary>
		public static void PoisonousKill(Game game, IPlayable source, Minion minion)
		{
			if (minion == null || minion.ToBeDestroyed && minion.DestroyedByEffect) return;
			if (game.DeferredPoison != null)
			{
				game.DeferredPoison.Add((source, minion));
				return;
			}
			if (minion.Zone?.Type != Zone.PLAY) return;
			bool history = game.History && source != null;
			if (history)
			{
				var block = PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, source.Id, "", -1, 0);
				block.TriggerKeyword = (int)GameTag.POISONOUS;
				game.PowerHistory.Add(block);
				game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.TARGET, Data = 0, Info = new List<int> { minion.Id } });
				game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.POISONOUS, Data = 0, Info = new List<int> { minion.Id } });
			}
			if (source != null && minion[DESTROYED_BY] != source.Id) minion[DESTROYED_BY] = source.Id; // (live: 2747, TO_BE_DESTROYED, 4741)
			minion.Destroy();
			if (source != null) NoteAffected(minion, source);
			if (history)
			{
				game.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
				game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 200, Info = new List<int> { source.Id } });
			}
		}

		/// <summary>HsServer: a countered card (CounterTask): live pauses 3 s on it before it leaves (EX1_287, MAW_006).</summary>
		internal static void CounteredPause(Game game, IPlayable card)
		{
			if (game.History)
				game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 3000, Info = new List<int> { card.Id } });
		}

		/// <summary>HsServer: the end of a play the player chose — a card, a Hero Power, a Location (live: 6095 of 6100 PLAY
		/// blocks, the other 5 a game reset): SLUSH_TIME 1000 on it right after its PLAY block (the client lets the play
		/// finish before what comes next; the play's deaths are inside the block). As after an attack (EndPlayerAttack).</summary>
		internal static void EndPlayerPlay(Game game, IEntity played)
		{
			if (game.History)
				game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 1000, Info = new List<int> { played.Id } });
		}

		/// <summary>
		/// HsServer: live opening of an attack block (official Power.log): ATTACK block (effect index 0), tag 1715 linking
		/// attacker and defender when the defender is a minion, the attacking player's minions-attacked counter, then
		/// PROPOSED_ATTACKER / PROPOSED_DEFENDER. Used by both attack paths (with and without the pre-attack checks).
		/// HsServer (2026-10-08): an attack an effect makes (not ordered by a player: Game.AttackOrdered) — live (89 of 102 nested ATTACK
		/// blocks; titans-vfx 1417 29911, progression 28953): tag 1715 = the effect's source on attacker and defender BEFORE the block
		/// (the attacker itself when it is the source), and the block names no target (Target=0); a hero defender gets no 1715 either way.
		/// </summary>
		private static void OpenAttack(Controller c, ICharacter source, ICharacter target, bool byEffect = false)
		{
			Game g = c.Game;
			if (g.History && byEffect)
			{
				int link = g.TaskQueue.CurrentSource is IEntity effect && effect.Id > 3 ? effect.Id : source.Id;
				if (target is Minion)
				{
					g.PowerHistory.Add(PowerHistoryBuilder.TagChange(source.Id, (GameTag)1715, link));
					g.PowerHistory.Add(PowerHistoryBuilder.TagChange(target.Id, (GameTag)1715, link));
				}
				g.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.ATTACK, source.Id, "", 0, 0));
			}
			else if (g.History)
			{
				g.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.ATTACK, source.Id, "", 0, target.Id));
				if (target is Minion)
				{
					g.PowerHistory.Add(PowerHistoryBuilder.TagChange(source.Id, (GameTag)1715, source.Id));
					g.PowerHistory.Add(PowerHistoryBuilder.TagChange(target.Id, (GameTag)1715, source.Id));
				}
			}
			if (source is Minion)
				c.NumFriendlyMinionsThatAttackedThisTurn++;
			if (g.History)
			{
				g.ProposedAttacker = source.Id;
				g.ProposedDefender = target.Id;
			}
			g.AttackingSource = source.Id;
			if (g.ExitingCombat == source.Id) g.ExitingCombat = 0; // (a stale mark from outside an attack)
			// HsServer (VIOLET_HOLD): "Whenever a friendly Stealthed minion attacks" — it was Stealthed as the attack began (rule book Stealth:
			// attackers "lose Stealth after the Combat Preparation Phase"; the engine drops it here, as live, so the mark keeps it)
			if (source is Character stealthed && stealthed.HasStealth)
				stealthed[HsTag.ATTACKED_STEALTHED] = 1;
			// HsServer: live — the attacker loses stealth right after PROPOSED_DEFENDER, before ATTACKING (was after the
			// damage, and only for minions).
			if (source is Character attacker && attacker.HasStealth && attacker[HsTag.PERMANENT_STEALTH] == 0)
				attacker.HasStealth = false;
		}

		private static Func<Controller, ICharacter, ICharacter, bool> PreAttackPhase
			=> delegate (Controller c, ICharacter source, ICharacter target)
			{
				if (c != source.Controller || c == target.Controller)
				{
					if (source[GameTag.AUTOATTACK] == 1)
						return true;
					c.Game.Log(LogLevel.ERROR, BlockType.ATTACK, "PreAttackPhase", !c.Game.Logging? "":"wrong controller in phase.");
					return false;
				}
				if (!source.CanAttack || !source.IsValidAttackTarget(target))
				{
					if (source[GameTag.AUTOATTACK] == 1)
						return true;
					c.Game.Log(LogLevel.ERROR, BlockType.ATTACK, "PreAttackPhase", !c.Game.Logging? "":"can't attack with this card.");
					return false;
				}
				var hero = source as Hero;

				c.Game.Log(LogLevel.INFO, BlockType.ATTACK, "PreAttackPhase", !c.Game.Logging? "":$"[PreAttackPhase]{source}[ATK:{source.AttackDamage}/HP:{source.Health}{(hero != null ? $"/ARM:{hero.Armor}" : "")}] " +
						$"{(hero?.Weapon != null ? $"[{hero.Weapon}[A:{hero.Weapon.AttackDamage}/D:{hero.Weapon.Durability}]] " : "")}is attacking " +
						$"{target}[ATK:{target.AttackDamage}/HP:{target.Health}].");

				// HsServer: live — the attacker's CARD_TARGET = the defender, right before the ATTACK block (sent when it changes)
				source.CardTarget = target.Id;

				// attack block
				OpenAttack(c, source, target, !c.Game.AttackOrdered);

				c.Game.CurrentEventData = new EventMetaData(source, target);

				// TODO: need to be manipulated for 50% chance to attack  someone else 

				return true;
			};

		/// <summary>HsServer: a trigger of the Proposed Attack Event: a Secret's, or one marked Trigger.ProposedAttack (rule book, Combat).</summary>
		private static bool IsProposedAttackTrigger(IEntity owner, Triggers.Trigger trigger) =>
			owner is Spell s && s.Zone?.Type == Zone.SECRET || trigger?.ProposedAttack == true;

		private static Func<Game, ICharacter, ICharacter, bool, bool> OnAttackTrigger
			=> delegate (Game g, ICharacter source, ICharacter target, bool keepsAction)
			{
				//if (g.History)
				//{

				//}
				// HsServer: the Combat Preparation Phase comes first (rule book: "A Proposed Attack Event is resolved … Finally,
				// an Attack Event"; then the Combat Phase). Live (live-20261005: Explosive Trap, Oasis Ally; Gorehowl, DAL_771…):
				// the attack's own triggers — Secrets and "whenever this attacks / is attacked" — resolve BEFORE ATTACKING /
				// SLUSH_TIME 1200 / DEFENDING (the client's swing), which start the Combat Phase below. Ours swung first, so an
				// attack into Freezing Trap visibly hit before the minion went back to hand (user 2026-10-05).

				// Invoke onAttackTrigger
				//Trigger.ValidateTriggers(g, source, SequenceType.Attack);

				// HsServer: rules B8 — the Proposed Attack Event (Secrets: Explosive Trap, Freezing Trap, Misdirection…)
				// resolves before the Attack Event (Truesilver Champion, Gorehowl, Cutpurse), whatever the order of play.
				// HsServer (2026-10-08): the Proposed Attack Event's triggers (Secrets, Forgetful, Mogor, Shaku, Genzo: Trigger.ProposedAttack) share
				// one queue in order of play (rule book: "Gladiator's Longbow vs Explosive Trap is determined by the Dominant Player Bug"); the
				// Attack Event's come after, once, against the final defender (below).
				g.TaskQueue.StartEvent();
				g.TaskQueue.StartGathering();
				source.Game.TriggerManager.OnAttackTrigger(source);
				g.TaskQueue.EndGathering(keep: IsProposedAttackTrigger);
				g.ProcessTasks();
				g.TaskQueue.EndEvent();
				// HsServer (2026-10-08, user: "how it attacked the New target in noble sac was totally off"): a Secret / effect that
				// changed the defender (Noble Sacrifice, Misdirection, Forgetful — ChangeAttackingTargetTask / Redirect: PROPOSED_DEFENDER)
				// — the swing goes to the NEW one: live (live-20261008-issues 2800) DEFENDING on the Defender, not on the first target.
				// Rule book l.510: "If the defender changes, another Proposed Attack Event is created": the Secrets get another go
				// against the new defender (Snake Trap after Noble Sacrifice put a minion in front), until it stops changing.
				for (int round = 0; round < 8 && g.ProposedDefender != 0 && g.ProposedDefender != target.Id &&
				     g.IdEntityDic.TryGetValue(g.ProposedDefender, out IPlayable moved) && moved is ICharacter newDefender; round++)
				{
					target = newDefender;
					if (source.Zone?.Type != Zone.PLAY || source.ToBeDestroyed || newDefender.ToBeDestroyed || g.ExitingCombat == source.Id) break;
					EventMetaData before = g.CurrentEventData;
					g.CurrentEventData = new EventMetaData(source as IPlayable, newDefender as IPlayable);
					g.TaskQueue.StartEvent();
					g.TaskQueue.StartGathering();
					g.TriggerManager.OnAttackTrigger(source);
					g.TaskQueue.EndGathering(keep: (owner, trigger) => owner is Spell s && s.Zone?.Type == Zone.SECRET);
					g.ProcessTasks();
					g.TaskQueue.EndEvent();
					g.CurrentEventData = before;
				}
				// the Attack Event (rule book: "triggers … that need to occur only once the final defender is known": Truesilver Champion,
				// Gorehowl, Cutpurse, Power Word: Glory), against the final defender — and the rest of the attack (its target triggers:
				// Gorehowl's "attacking a minion") sees that defender too
				g.CurrentEventData = new EventMetaData(source as IPlayable, target as IPlayable);
				g.TaskQueue.StartEvent();
				g.TaskQueue.StartGathering();
				g.TriggerManager.OnAttackTrigger(source);
				g.TaskQueue.EndGathering(keep: (owner, trigger) => !IsProposedAttackTrigger(owner, trigger));
				g.ProcessTasks();
				g.TaskQueue.EndEvent();
				// HsServer: an attack something else makes instead (Power.AttackInstead: Neptulon's Hands)
				Action instead = source.Zone?.Type == Zone.PLAY && !source.ToBeDestroyed && !target.ToBeDestroyed
					? source.Card.Power?.AttackInstead?.Invoke(g, source, target) : null;
				// HsServer: the game is also checked between Preparation and Combat: an Explosive Trap that kills the
				// attacker's hero ends the game before any combat damage (Advanced rulebook, Rule 9).
				// HsServer (VIOLET_HOLD): an attacker taken out of the fight in place — transformed (Mystic Misdirection; rule book l.726 "When a
				// minion is transformed, it is removed from play immediately", l.507 "if the attacker or defender leaves play for any reason,
				// the current Phase will finish resolving but the Sequence will end early afterwards"; live L1 73868: the Sheep gets
				// NUM_ATTACKS_THIS_TURN 1, EXHAUSTED 1, no combat)
				if (instead != null || g.ExitingCombat == source.Id || source.ToBeDestroyed || target.ToBeDestroyed || (source.Zone != null && source.Zone.Type != Zone.PLAY) ||
				    (target.Zone != null && target.Zone.Type != Zone.PLAY) || source.Controller.Hero.ToBeDestroyed || source.Controller.Opponent.Hero.ToBeDestroyed)
				{
					// cancelled in preparation: no combat, no swing — but the attack is used (live Novice Engineer into
					// Explosive Trap: NUM_ATTACKS_THIS_TURN 1, EXHAUSTED 1, then PROPOSED_* reset; no ATTACKING / DEFENDING)
					if (source.Zone?.Type == Zone.PLAY && !keepsAction)
					{
						source.NumAttacksThisTurn++;
						if (source.NumAttacksThisTurn >= AttacksPerTurn(source)) source.IsExhausted = true;
					}
					g.ProposedAttacker = 0;
					g.ProposedDefender = 0;
					EndExitCombat(g, source); // live: after PROPOSED_* (Vaporize)
					instead?.Invoke();

					g.Log(LogLevel.INFO, BlockType.ATTACK, "OnAttackTrigger",
						!g.Logging ? "" : "Oh shizzle, something died to the shizzeling of triggering ...");
					return false;
				}

				// the Combat Phase starts: the client's swing (live order: ATTACKING, SLUSH_TIME 1200, DEFENDING)
				source.IsAttacking = true;
				if (g.History)
					g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 1200, Info = new List<int> { source.Id } });
				target.IsDefending = true;
				return true;
			};

		private static Func<Controller, ICharacter, bool, bool, bool> AttackPhase
			=> delegate (Controller c, ICharacter source, bool noExhaustion, bool keepsAction)
			{
				Game game = c.Game;

				game.TaskQueue.StartEvent();
				game.TriggerManager.OnTargetTrigger(source);
				game.ProcessTasks();
				game.TaskQueue.EndEvent();

				var hero = source as Hero;
				var minion = source as Minion;
				//if (!game.IdEntityDic.TryGetValue(game.ProposedDefender, out IPlayable proposedDefender))
				//{
				//	game.Log(LogLevel.INFO, BlockType.ATTACK, "AttackPhase", !game.Logging? "":"target wasn't found by proposed defender call.");
				//	source.IsAttacking = false;
				//	source.IsDefending = false;
				//	return false;
				//}

				var target = (ICharacter) game.CurrentEventData.EventTarget;

				// Force the game into MAIN_COMBAT step! (HsServer: quietly — live never reports this step)
				game.SetStepQuietly(Step.MAIN_COMBAT);

				// HsServer: live order — NUM_ATTACKS_THIS_TURN, attack counter (tag 4163), EXHAUSTED before the damage.
				int numAtk = source.NumAttacksThisTurn + (keepsAction ? 0 : 1);
				if (!keepsAction) source.NumAttacksThisTurn = numAtk;
				if (source is Hero) source.Controller[HsTag.HERO_ATTACKS_THIS_GAME] += 1; // HsServer: Shockspitter
				source.Controller[HsTag.FRIENDLY_ATTACKS_THIS_GAME] += 1; // HsServer (CATACLYSM): Muradin's Last Stand
				if (game.History)
				{
					source.AttacksThisGame++;
					game.PowerHistory.Add(PowerHistoryBuilder.TagChange(source.Id, (GameTag)4163, source.AttacksThisGame));
				}
				if (!noExhaustion && !keepsAction && numAtk >= AttacksPerTurn(source)) // HsServer: Mega-Windfury (4 attacks)
					source.IsExhausted = true;

				// Save defender's attack as it might change after being damaged (e.g. enrage)
				var targetHero = target as Hero;
				int targetAttack = /*targetHero != null ? 0 : */target.AttackDamage;
				int sourceAttack = /*hero?.TotalAttackDamage ?? */source.AttackDamage;

				// HsServer: rules B8.4 — combat damage is simultaneous: the attack and the counterattack both land, then
				// their damage triggers resolve (the defender's first).
				bool combatBatch = game.TaskQueue.StartBatch();

				// HsServer (TITANS): "Excess damage dealt by attacks hits the enemy hero" — split before the damage
				// (Generic.ExcessAttackDamage: Always a Bigger Jormungar)
				int excess = ExcessAttackDamage(source, target, sourceAttack);
				// (the Lifesteal of either hit heals after both: Character.Lifesteal, paid below)
				var outerLifesteal = game.DeferredLifesteal;
				game.DeferredLifesteal = new List<(IPlayable, int)>();
				var outerPoison = game.DeferredPoison;
				game.DeferredPoison = new List<(IPlayable, Minion)>();
				int targetRealDamage = target.TakeDamage(source, sourceAttack - excess);
				if (excess > 0 && source.Controller.Opponent.Hero is Hero excessHero)
					excessHero.TakeDamage(source, excess);
				bool targetDamaged = targetRealDamage > 0;
				int combatPulses = targetDamaged ? 1 : 0;

				// freeze target if attacker is freezer
				if (targetDamaged && minion != null && minion.Freeze)
				{
					game.Log(LogLevel.VERBOSE, BlockType.ATTACK, "AttackPhase", !game.Logging? "":$"freezer attacker has frozen target.");
					target.IsFrozen = true;
				}

				// destroy target if the attacking hero's weapon is poisonous (a Poisonous minion's damage is handled in
				// TakeDamage, like all its damage)
				if (targetDamaged && targetHero == null && hero?.Weapon != null && hero.Weapon.Poisonous && !target.ToBeDestroyed && target is Minion poisoned)
				{
					game.Log(LogLevel.VERBOSE, BlockType.ATTACK, "AttackPhase", !game.Logging? "":$"poisonous attacker has destroyed target.");
					PoisonousKill(game, hero, poisoned);
				}

				// ignore damage from defenders with 0 attack
				if (targetAttack > 0)
				{
					int sourceRealDamage = source.TakeDamage(target, targetAttack);
					bool sourceDamaged = sourceRealDamage > 0;
					if (sourceDamaged) combatPulses++;

					// freeze source if defender is freezer
					var targetMinion = target as Minion;
					if (sourceDamaged && targetMinion != null && targetMinion.Freeze)
					{
						source.IsFrozen = true;
					}

					// (a Poisonous defender destroys the attacker in TakeDamage)
				}

				// HsServer (2026-10-08): the combat Lifesteal heals now, after both hits, in the order dealt (live 208 of 208: DAMAGE, DAMAGE,
				// the LIFESTEAL block; wiki Lifesteal.txt:13-14 — the hero survives on the net result)
				// HsServer (2026-10-08): the Poisonous kills of the two hits, after both (live progression 28991)
				var poisons = game.DeferredPoison;
				game.DeferredPoison = outerPoison;
				foreach ((IPlayable from, Minion victim) in poisons)
					PoisonousKill(game, from, victim);
				var lifesteals = game.DeferredLifesteal;
				game.DeferredLifesteal = outerLifesteal;
				foreach ((IPlayable from, int healed) in lifesteals)
					Character.Lifesteal(game, from, healed);

				if (combatBatch)
				{
					game.TaskQueue.StartEvent();
					game.TaskQueue.EndBatch();
					game.ProcessTasks();
					game.TaskQueue.EndEvent();
				}

				// remove durability from weapon if hero attack
				if (hero?.Weapon != null && !hero.Weapon.IsImmune)
				{
					//hero.Weapon.Durability -= hero.Weapon.Durability > 0 ? 1 : 0;
					if (!Weapon.KeepsDurability(hero.Weapon)) // HsServer: Stormhammer
						hero.Weapon.Damage += 1;
				}

				//if (game.History)
				//{

				//}
				// HsServer: live closing — one 1710 pulse on the game per combat damage, PROPOSED_* reset, then ATTACKING/DEFENDING.
				if (game.History)
				{
					for (int i = 0; i < combatPulses; i++)
					{
						game.PowerHistory.Add(PowerHistoryBuilder.TagChange(game.Id, (GameTag)1710, 1));
						game.PowerHistory.Add(PowerHistoryBuilder.TagChange(game.Id, (GameTag)1710, 0));
					}
					game.ProposedAttacker = 0;
					game.ProposedDefender = 0;
				}
				EndExitCombat(game, source);
				source.IsAttacking = false;
				target.IsDefending = false;

				game.Log(LogLevel.INFO, BlockType.ATTACK, "AttackPhase", !game.Logging? "":$"[AttackPhase]{source}[ATK:{source.AttackDamage}/HP:{source.Health}{(hero != null ? $"/ARM:{hero.Armor}" : "")}] " +
						$"{(hero?.Weapon != null ? $"[{hero.Weapon}[A:{hero.Weapon.AttackDamage}/D:{hero.Weapon.Durability}]] " : "")}attacked " +
						$"{target}[ATK:{target.AttackDamage}/HP:{target.Health}].");
				return true;
			};
	}
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
