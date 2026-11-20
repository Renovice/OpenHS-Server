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
using System.Text;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
    public static partial class Generic
    {
		/// <summary>HsServer: a card's on-play replacement (Cards.OnPlayReplacement: Twinspell's copy, a Drink spell's next
		/// drink, a Miniaturize mini...), a new card, into the hand at <paramref name="slot"/> (-1: right-most), at once when
		/// the card is played, before any play / cast trigger. Live (live-20261001-twinspell, live-20261002-forge-drink): one
		/// FULL_ENTITY with CREATOR = the played card and CREATED_AS_ON_PLAY_REPLACEMENT, so the client shows it taking the
		/// played card's slot. A spell cast by an effect leaves it too (live: Yogg-Saron's Twinspell, right-most).</summary>
		public static void OnPlayReplacement(Controller c, IPlayable played, int slot)
		{
			// HsServer (WILD_WEST): or the card a "(This stays in your hand.)" enchantment on it stands for (Mirage: Generic.StaysInHandVia)
			List<Card> copies = Cards.OnPlayReplacementsOf(played.Card);
			if (copies.Count == 0 && StaysInHandVia(played) is Card stays) copies.Add(stays);
			bool sized = false;
			foreach (Card copy in copies)
			{
				if (c.HandZone.IsFull) break;
				var tags = new EntityData { [GameTag.CREATOR] = played.Id, [HsTag.CREATED_AS_ON_PLAY_REPLACEMENT] = 1 };
				// HsServer (WHIZBANG): a Mini / Gigantic (live vfx 3541, 141165): DISPLAYED_CREATOR and CREATED_BY_MINIATURIZE (3433) /
				// CREATED_BY_GIGANTIFY (3434) = the played card — the client's on-play replacement animation needs them (Card.cs 6637)
				if (copy[MINI] > 0 || copy[GIGANTIC] > 0)
				{
					tags[GameTag.DISPLAYED_CREATOR] = played.Id;
					tags[copy[MINI] > 0 ? CREATED_BY_MINIATURIZE : CREATED_BY_GIGANTIFY] = played.Id;
					sized = true;
				}
				Entity.FromCard(in c, copy, tags, c.HandZone, zonePos: slot >= 0 && slot <= c.HandZone.Count ? slot : -1);
				c.OnPlayReplaced = played.Id;
			}
			if (sized && c.Game.History) // live: SLUSH_TIME 1000 on the played card after its Mini / Gigantic
				c.Game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 1000, Info = new List<int> { played.Id } });
		}

		/// <summary>HsServer: "Cast a spell from your deck" (High Abbess Alura, Sunlight Blossom): the card leaves the deck and
		/// is cast as an effect-cast spell (CastRandomSpellTask with a given spell); <paramref name="targetsSourceIfPossible"/>:
		/// "(targets this if possible)". A deck card cast without leaving the deck ended in the graveyard while the deck still
		/// listed it (soak 10000209).</summary>
		public static void CastFromDeck(Controller c, IEntity source, Spell spell, bool targetsSourceIfPossible = false)
		{
			if (spell?.Zone?.Type != Zone.DECK) return;
			c.DeckZone.Remove(spell);
			new Tasks.SimpleTasks.CastRandomSpellTask { TargetsSourceIfPossible = targetsSourceIfPossible }.Process(c.Game, c, source, spell);
		}

		/// <summary>HsServer: "If you're holding a Secret, cast it" (Sparkjoy Cheat): the card leaves the hand (not a discard),
		/// then is cast as an effect cast.</summary>
		public static void CastFromHand(Controller c, IEntity source, Spell spell)
		{
			if (spell?.Zone?.Type != Zone.HAND) return;
			c.HandZone.Remove(spell);
			new Tasks.SimpleTasks.CastRandomSpellTask().Process(c.Game, c, source, spell);
		}

	    public static Action<Controller, Game, Spell, IPlayable, int> CastSpell
		    => delegate(Controller c, Game game, Spell spell, IPlayable target, int chooseOne)
		    {
				if (game.Logging)
				{
					var sb = new StringBuilder();
					sb.Append("Spell ");
					sb.Append(spell);
					sb.Append(" is cast");
					if (target != null)
					{
						sb.Append(" to ");
						sb.Append(target);
					}
					sb.Append(".");
					if (chooseOne > 0)
						sb.Append($"(ChooseOne: {{{chooseOne}}}.)");

					game.Log(LogLevel.INFO, BlockType.PLAY, "CastSpell", sb.ToString());
				}

				// HsServer: a spell an effect casts (not played from hand) is shown big first, as live: SHOW_BIG_CARD,
				// HISTORY_TARGET and a SLUSH_TIME 3000 pause on it before its effect (Yogg-Saron, Prison of Yogg-Saron...).
				bool castByEffect = spell[GameTag.ZONE] != (int)Zone.PLAY && spell[GameTag.ZONE] != (int)Zone.SECRET;
				if (game.History && castByEffect)
				{
					if (!spell.IsSecret && !spell.IsQuest && spell[GameTag.SIDEQUEST] == 0 && !spell.Card.IsDurationSpell)
						spell[GameTag.ZONE] = (int)Zone.PLAY; // live: the cast spell is in PLAY while it resolves
					var shown = new List<int> { spell.Id };
					// (HsServer, Dalaran Heist: a chapter twist has only the pause — CAST_WITHOUT_BIG_CARD, Generic.PlayPlotTwist)
					if (spell[CAST_WITHOUT_BIG_CARD] == 0)
					{
						game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SHOW_BIG_CARD, Data = 0, Info = shown });
						game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.HISTORY_TARGET, Data = 0, Info = shown });
					}
					game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 3000, Info = shown });
				}

				game.TaskQueue.StartEvent();
			    // HsServer: a Quest cast (by an effect) while one is active, or a Secret/Quest with all 5 hero-zone slots
			    // taken, has no slot: it goes to the graveyard.
			    // (and the hero zone holds one copy of any given Secret or Sidequest: a duplicate cast by an effect is lost too)
			    if (spell.IsQuest && c.SecretZone.Quest != null ||
			        (spell.IsSecret || spell.IsQuest || spell[GameTag.SIDEQUEST] > 0 || spell.Card.IsDurationSpell) && c.SecretZone.IsFull ||
			        (spell.IsSecret || spell[GameTag.SIDEQUEST] > 0) && c.SecretZone.Any(x => x.Card.Id == spell.Card.Id))
			    {
				    c.GraveyardZone.Add(spell);
				    game.TaskQueue.EndEvent();
				    return;
			    }
			    // HsServer: Sidequests and Paladin Auras (PALADIN_AURA: "Lasts @ turns", live TIME_700) sit in the Secret zone too
			    if (spell.IsSecret || spell.IsQuest || spell[GameTag.SIDEQUEST] > 0 || spell.Card.IsDurationSpell)
			    {
				    spell.Power?.Trigger?.Activate(spell); // HsServer: a Secret without a script has no Power
				    if (spell.Card.IsDurationSpell) spell.Power?.Aura?.Activate(spell); // a Paladin Aura's ongoing effect
				    c.SecretZone.Add(spell);
				    // HsServer (2026-10-08): a Secret is inactive only on its owner's turn (wiki Secret.txt:6: "Secrets can only be activated on the
				    // opponent's turn") — one an effect casts on the opponent's turn is up at once, as Generic.PutSecretIntoPlay
				    spell.IsExhausted = !spell.IsSecret || c == game.CurrentPlayer;
				    // HsServer (livecheck 2026-10-10): a card that is two Quests puts both halves up (Generic.SplitQuest)
				    if (spell.IsQuest) SplitQuest(c, spell);
			    }
			    else
			    {
				    //spell.Power?.Trigger?.Activate(spell);
				    if (spell.Power?.Trigger != null && spell.ActivatedTrigger == null)
					    spell.Power.Trigger.Activate(spell);
				    spell.Power?.Aura?.Activate(spell);

				    // HsServer: a spell an effect casts that needs a target and has none is still cast (it shows as played)
				    // but does nothing (a random spell of Yogg-Saron / Prison of Yogg-Saron with no valid target left).
				    int powerMark = game.History ? game.PowerHistory.Full.Count : 0;
				    bool fizzles = castByEffect && target == null && (spell.Card.RequiresTarget || spell.Card.MustHaveTargetToPlay);
				    // HsServer: who had died before this spell's effect (Headmaster Kel'Thuzad: "if the spell destroys any minions")
				    spell[HsTag.CHOSEN_OPTION] = chooseOne; // its Overload follows the picked option (Generic.OverloadOf)
				    spell[HsTag.DIED_BEFORE_SPELL_MINE] = c.DiedThisGame.Count;
				    spell[HsTag.DIED_BEFORE_SPELL_THEIRS] = c.Opponent.DiedThisGame.Count;
				    // HsServer (Dalaran Heist): "Your spells cast an additional time" (Kirin Tor Elemental) — the live pause first
				    int extraCasts = ExtraCastsPause(game, spell, castByEffect);
				    if (fizzles)
				    { }
				    else if (spell.Combo && c.IsComboActive)
					    spell.ActivateTask(PowerActivation.COMBO, target);
				    else
					    spell.ActivateTask(PowerActivation.POWER, target, chooseOne);

				    // HsServer: a spell cast by an effect leaves its on-play replacement right-most (played from hand: PlaySpell did it)
				    if (c.OnPlayReplaced != spell.Id)
					    OnPlayReplacement(c, spell, -1);
				    c.OnPlayReplaced = 0;

				    // process power tasks
				    game.ProcessTasks();

				    if (fizzles)
					    OverloadBlock(c, spell, game.History);
				    else
				    {
					    PlayedPowerBlock(c, spell, powerMark); // its POWER block even with no effect, then its Overload (live)
					    SecondPowerBlock(c, spell, target); // HsServer: a second effect's own POWER block (the Riffs)
				    }

				    // HsServer: SPELLS_CAST_TWICE (Stormcaller Bru'kan, live 2026-10-01): the whole effect again, Overload
				    // included, after a 3 s pause (live: POWER 0, POWER 1, SLUSH_TIME 3000, POWER 0, POWER 1 in one PLAY block).
				    // A target that died or left gives way to a random valid one (rules: random casts).
				    // (and Vexallus's Arcane spells: HsTag.ARCANE_SPELLS_CAST_TWICE)
				    // (and HsServer (WILD_WEST): a card marked "if you play it this turn, it casts twice" — Stargazing: Generic.CastsTwiceNow)
				    // HsServer: only a spell the player plays (wiki Yogg-Saron, Hope's End: spells cast by an effect "do NOT appear in
				    // spell-doubling effects"; user 2026-10-07: "only your spells that you cast"). The repeat is no new cast: the
				    // cast triggers, history and counters run once, in PlaySpell (wiki Cast spell: a spell cast by an effect "is not
				    // considered to have been cast by the player for the purposes of triggers or game history").
				    // (HsServer EMERALD_DREAM: Tyrande's "The next 3 spells you play cast twice" — a charge per played spell)
				    // (2026-11-16 Therazane: the next spell of each school — its charge spent whatever else doubles it)
				    bool schoolTwice = !castByEffect && SchoolSpellCastsTwice(c, spell);
				    bool nextTwice = !castByEffect && NextSpellCastsTwice(c); // (2026-11-19 Brain of Yogg-Saron)
				    bool playedTwice = !castByEffect && c[HsTag.PLAYED_SPELLS_CAST_TWICE] > 0;
				    if (playedTwice) c[HsTag.PLAYED_SPELLS_CAST_TWICE] = c[HsTag.PLAYED_SPELLS_CAST_TWICE] - 1;
				    bool twiceByOthers = playedTwice || schoolTwice || nextTwice || c[HsTag.SPELLS_CAST_TWICE] > 0 || spell[(GameTag)1635] == 1 && c.ControllerAuraEffects[HsTag.ARCANE_SPELLS_CAST_TWICE] > 0 ||
				        CastsTwiceNow(spell) || CheapSpellCastsTwice(c, spell) || ExactCostSpellCastsTwice(c, spell) || spell[HsTag.SPELL_CASTS_TWICE] > 0;
				    // (HsServer CATACLYSM: Sinestra — "Your spells from other classes cast twice"; live vfx 98999-99078: no pause between)
				    bool twiceOtherClass = c.ControllerAuraEffects[HsTag.OTHER_CLASS_SPELLS_CAST_TWICE] > 0 && !spell.Card.HasClass(c.HeroClass) &&
				        !spell.Card.HasClass(CardClass.NEUTRAL) && !spell.Card.HasClass(CardClass.INVALID);
				    // (HsServer DALA heist: Stormswell — "Your spells with Overload cast twice / thrice": OVERLOAD_SPELLS_EXTRA_CASTS extra
				    // casts, each after the 3 s pause, each overloading again — live-20261007-212735 59942-60238)
				    int repeats = twiceByOthers || twiceOtherClass ? 1 : 0;
				    bool stormswell = spell.Card.HasOverload && c.ControllerAuraEffects[OVERLOAD_SPELLS_EXTRA_CASTS] > repeats;
				    if (stormswell) repeats = c.ControllerAuraEffects[OVERLOAD_SPELLS_EXTRA_CASTS];
				    for (int cast = 0; cast < repeats; cast++)
				    // (2026-11-16: not stopped by a hero at 0 Health — rulebook: "death is not checked during resolution", and win/loss only
				    // "at the end of the Sequence"; live Bru'kan Lightning Bolt cast again on a boss the first cast left at 0: Overload 2)
				    if (!castByEffect && (twiceByOthers || twiceOtherClass || stormswell) && !(castByEffect && target == null && (spell.Card.RequiresTarget || spell.Card.MustHaveTargetToPlay))
				        && game.State != State.COMPLETE)
				    {
					    if (game.History && (twiceByOthers || stormswell))
						    game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 3000, Info = new List<int> { spell.Id } });
					    IPlayable again = target;
					    List<IPlayable> handTargets = spell.HandTargets();
					    if (handTargets != null) // a card in hand: the same one if still valid, else a random valid one
					    {
						    if (target != null && !handTargets.Contains(target))
							    again = handTargets.Count > 0 ? handTargets[game.Random.Next(handTargets.Count)] : null;
					    }
					    else if (target != null && (target[GameTag.ZONE] != (int)Zone.PLAY || target.ToBeDestroyed || !(target is ICharacter ch && spell.IsValidPlayTarget(ch)))) // a hero has no Zone object
						    again = spell.GetRandomValidTarget();
					    if (target == null || again != null)
					    {
						    if (spell.Combo && c.IsComboActive)
							    spell.ActivateTask(PowerActivation.COMBO, again);
						    else
							    spell.ActivateTask(PowerActivation.POWER, again, chooseOne);
						    game.ProcessTasks();
						    OverloadBlock(c, spell, game.History);
						    SecondPowerBlock(c, spell, again);
					    }
				    }

				    if (extraCasts > 0 && !fizzles)
					    ExtraCasts(c, game, spell, target, chooseOne, extraCasts); // (Generic, Actions/DalaranHeistGroup3.cs)

				    // HsServer (2026-10-08): into its owner's graveyard — its effect may have changed who that is (soak 2400330: a spell Druid of
				    // Regrowth cast for the opponent threw "Can't add an opponent's entity to own Zones")
				    spell.Controller.GraveyardZone.Add(spell);
				    SpendPlayThisTurnMark(spell); // HsServer (TITANS): a marked card played spends its mark (live)
				    if (game.History && castByEffect) // live: a short pause once an effect's cast is done
					    game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 1000, Info = new List<int> { spell.Id } });
			    }
			    game.TaskQueue.EndEvent();
		    };
    }
}
