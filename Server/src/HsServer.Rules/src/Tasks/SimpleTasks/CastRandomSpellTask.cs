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
using System.Collections.Concurrent;
using System.Linq;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Tasks.SimpleTasks
{
	public class CastRandomSpellTask : SimpleTask
	{
		// HsServer: keyed by (source card, format) — Standard and Wild games share the process.
		private static readonly ConcurrentDictionary<(int, FormatType), Card[]> CachedCardLists =
			new ConcurrentDictionary<(int, FormatType), Card[]>();

		private readonly Func<Card, bool> _condition;
		private readonly bool _phaseShift;
		private readonly bool _preferTarget;

		/// <param name="preferTarget">HsServer: the task's target is the character to aim at when the spell can target it
		/// ("Cast 4 random spells (targeting it if possible)"), not a spell to cast.</param>
		// HsServer: "(targets enemies if possible)" — the random target is an enemy when the spell can target one
		private readonly bool _enemiesIfPossible;

		/// <summary>HsServer: "(targets this if possible)" (High Abbess Alura): the casting card is the target when the spell
		/// can take it.</summary>
		public bool TargetsSourceIfPossible { get; set; }

		/// <summary>HsServer: cast this card (a new copy) instead of a random spell; with preferTarget, at the task's target
		/// ("cast it again on another friendly minion": Kotori Lightblade).</summary>
		public Card Spell { get; set; }

		public CastRandomSpellTask(Func<Card, bool> condition = null, bool phaseShift = true, bool preferTarget = false,
			bool enemiesIfPossible = false)
		{
			_condition = condition;
			_phaseShift = phaseShift;
			_preferTarget = preferTarget;
			_enemiesIfPossible = enemiesIfPossible;
		}

		public override TaskState Process(in Game game, in Controller controller, in IEntity source,
			in IPlayable target,
			in TaskStack stack = null)
		{
			// HsServer (2026-11-16): a given spell is cast by its own controller (Wild Magic: the copy is the attached player's, the
			// enchantment player 1's — Trigger.Side); otherwise the source's
			Controller c = target is Spell castThis && castThis.Controller != null ? castThis.Controller : source.Controller;
			Util.DeepCloneableRandom rnd = game.Random;

			// HsServer: engine rule — random casting (Yogg-Saron, Prison of Yogg-Saron, Fyrakk...) stops as soon as a hero
			// is dead: the game ends there, the rest of the effect is not finished.
			if (game.Player1.Hero.IsDead || game.Player2.Hero.IsDead)
				return TaskState.STOP;

			// HsServer: a given spell (cast from the deck, a stored Deathrattle...) is cast as is, with no random pick. Upstream
			// replaced a cached conditional pool with the all-spells pool (else-if on a cache hit) and ignored the format.
			Card randCard = null;
			ICharacter wanted = _preferTarget ? target as ICharacter : null;
			IPlayable given = _preferTarget ? null : target;
			if (given == null && Spell != null)
				randCard = Spell;
			else if (given == null)
			{
				Card[] cards;
				if (_condition != null)
				{
					// HsServer (CATACLYSM review): built per call — a condition can depend on the call ("of the same Cost": Enchanted
					// Cauldron, Oh My Yogg, Chaos Supplicant); a cache by card kept the first call's pool
					cards = Cards.FormatTypeCards(game.FormatType)
						.Where(card => card.Type == CardType.SPELL && _condition(card) && card.Implemented && !card.HideStat).ToArray();
				}
				else if (!CachedCardLists.TryGetValue((0, game.FormatType), out cards))
				{
					cards = Cards.FormatTypeCards(game.FormatType)
						.Where(card => card.Type == CardType.SPELL && !card.IsQuest && card.Implemented && !card.HideStat).ToArray();
					CachedCardLists.TryAdd((0, game.FormatType), cards);
				}
				Card self = Cards.SelfOf(source); // HsServer: never a copy of itself
				if (self != null) cards = cards.Where(card => card.Id != self.Id).ToArray();
				Controller caster = c; // deck-gated families only when the caster's deck opens them (Cards.DeckAllows)
				cards = cards.Where(card => Cards.DeckAllows(caster, card)).ToArray();
				if (cards.Length == 0) return TaskState.STOP;
				randCard = cards[rnd.Next(cards.Length)];
			}

			game.OnRandomHappened(true);

			Spell spellToCast = (Spell) given ?? (Spell) Entity.FromCard(c, randCard);

			game.Log(LogLevel.INFO, BlockType.POWER, "CastRandomSpellTask",
				!game.Logging ? "" : $"{source} casted {c.Name}'s {spellToCast}.");

			if (spellToCast.IsSecret && c.SecretZone.IsFull)
			{
				c.GraveyardZone.Add(spellToCast);
				return TaskState.COMPLETE;
			}

			ICharacter randTarget;
			// HsServer: a chosen target that has left play (removed, bounced) is no longer a target; a mortally wounded
			// one still on the board is (it keeps being hit until deaths are processed).
			if (wanted != null && wanted[GameTag.ZONE] == (int)Enums.Zone.PLAY && spellToCast.IsValidPlayTarget(wanted)) // ZONE tag: a hero has no Zone object
			{
				randTarget = wanted;
				spellToCast.CardTarget = wanted.Id;
			}
			else
				randTarget = TargetsSourceIfPossible && source is ICharacter me && me[GameTag.ZONE] == (int)Enums.Zone.PLAY && spellToCast.IsValidPlayTarget(me)
					? me : spellToCast.GetRandomValidTarget(_enemiesIfPossible);
				if (randTarget != null) spellToCast.CardTarget = randTarget.Id;

			int randChooseOne = rnd.Next(1, 3);

			Choice choiceTemp = c.Choice;
			c.Choice = null;

			game.TaskQueue.StartEvent();
			Generic.CastSpell.Invoke(c, game, spellToCast, randTarget, randChooseOne);
			// forced death processing & AA (Yogg)
			if (_phaseShift)
				game.ForcedDeathPhase(); // HsServer: rules A9 (Yogg-Saron, Hope's End)
			game.TaskQueue.EndEvent();


			//game.TaskQueue.Execute(spellToCast.Power.PowerTask, source.Controller, spellToCast, randTarget, randChooseOne);
			//game.DeathProcessingAndAuraUpdate();

			while (c.Choice != null)
			{
				game.TaskQueue.StartEvent();
				Generic.ChoicePick.Invoke(c, game, c.Choice.Choices.Choose(game.Random));
				game.ProcessTasks();
				game.TaskQueue.EndEvent();
				game.DeathProcessingAndAuraUpdate();
			}

			c.Choice = choiceTemp;

			return TaskState.COMPLETE;
		}
	}
}
