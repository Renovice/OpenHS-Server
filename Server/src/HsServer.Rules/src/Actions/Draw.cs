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
using SabberStoneCore.Model;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
	{
		public static IPlayable DrawCard(Controller c, Card card)
		{
			return DrawCardBlock.Invoke(c, card);
		}

		/// <summary>HsServer: the effect block that is running drew a card (PowerHistoryBlockStart.Draws).</summary>
		private static void MarkDraw(Game game)
		{
			if (game.History && game.PowerHistory.CurrentEffectBlock is PowerHistoryBlockStart block) block.Draws = true;
		}

		/// <summary>
		/// HsServer: a drawn card's own on-draw effect (Power.TopdeckTask). "Casts When Drawn" / "Summoned When Drawn" (its own
		/// keyword, Card.ReplacedWhenDrawn): the card leaves the hand, its effect resolves, then "the player draws another card
		/// to replace the cast spell" (wiki Casts_When_Drawn / Summoned_When_Drawn) — the engine draws it, not each script —
		/// then Power.AfterReplacementDrawTask (Corrupted Blood: "After you draw, shuffle two copies"), all inside the card's
		/// TRIGGER block (live); Power.ReplacementDrawnElsewhere: another entity's trigger draws it (The Darkness's Candle
		/// Detect). "When you draw this,
		/// …" (Flame Leviathan, Sea Reaver, Aranasi Broodmother): the effect resolves and the card stays in hand.
		/// </summary>
		private static void OnDrawn(Controller c, IPlayable playable)
		{
			ISimpleTask task = playable.Power?.TopdeckTask;
			if (task == null) return;
			Game game = c.Game;
			// HsServer (2026-10-08): not in the opening hand or the mulligan's replacements — wiki Casts_When_Drawn.txt:8 "It is not triggered
			// if the card is chosen during the mulligan" (adventure / Brawl decks)
			if ((Step)game[GameTag.STEP] is Step.BEGIN_DRAW or Step.BEGIN_MULLIGAN or Step.BEGIN_FIRST or Step.BEGIN_SHUFFLE) return;
			bool replaced = playable.Card.ReplacedWhenDrawn;
			if (game.History)
				game.PowerHistory.Add(TopdeckBlock(playable)); // live TriggerKeyword CASTS_WHEN_DRAWN / SUMMONED_WHEN_DRAWN
			if (replaced)
				c.SetasideZone.Add(c.HandZone.Remove(playable));
			game.Log(LogLevel.INFO, BlockType.TRIGGER, "TOPDECK", !game.Logging ? "" : $"{playable}'s TOPDECK effect is activated.");
			task.Process(game, c, playable, null);
			// live (Burrowing Mine, Scroll of Wonder, JAIL_881t: live-20261004-night): the replacement draw is inside the card's
			// own block — a Casts When Drawn card drawn as the replacement nests its block in it — then the block ends
			if (replaced && !playable.Power.ReplacementDrawnElsewhere)
			{
				Draw(c);
				if (playable.Power?.AfterReplacementDrawTask is ISimpleTask after)
					after.Process(game, c, playable, null);
			}
			if (game.History)
				game.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			// the entity that draws it acts after the card's block (live: The Darkness's Candle: "Darkness Awaits" counts,
			// then "Darkness Candle Detect" draws the replacement)
			if (replaced && playable.Power.ReplacementDrawnElsewhere && playable.Power.AfterReplacementDrawTask is ISimpleTask elsewhere)
				elsewhere.Process(game, c, playable, null);
		}

		/// <summary>HsServer: the TRIGGER block of a card that acts when drawn, with the keyword the client animates.</summary>
		private static PowerHistoryBlockStart TopdeckBlock(IPlayable playable)
		{
			PowerHistoryBlockStart start = PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, playable.Id, "", 0, 0);
			// HsServer (TITANS): a minion with both keywords is summoned (Frost Tyrant TTN_083t carries both tags; live EDR_260t:
			// SUMMONED_WHEN_DRAWN)
			if (playable is Minion && playable.Card[SUMMONED_WHEN_DRAWN] > 0) start.TriggerKeyword = (int)SUMMONED_WHEN_DRAWN;
			else if (playable.Card[CASTS_WHEN_DRAWN] > 0) start.TriggerKeyword = (int)CASTS_WHEN_DRAWN;
			else if (playable.Card[SUMMONED_WHEN_DRAWN] > 0) start.TriggerKeyword = (int)SUMMONED_WHEN_DRAWN;
			// HsServer (ISLAND_VACATION): every on-draw block shows in the history (live Line Cook VAC_337, 6 of 6, no keyword; Sea
			// Reaver with its keyword too)
			start.ShowInHistory = true;
			return start;
		}

		/// <summary>Client keyword tags for cards that act when drawn.</summary>
		public const GameTag CASTS_WHEN_DRAWN = (GameTag)1077, SUMMONED_WHEN_DRAWN = (GameTag)3128;

		public static IPlayable Draw(Controller c, IPlayable cardToDraw = null)
		{
			return DrawBlock.Invoke(c, cardToDraw);
		}

		/// <summary>
		/// HsServer: "Draw a card. Put it into the battlefield if it's a minion" (Varian Wrynn): a drawn minion with room on
		/// the board goes from the deck straight into play, like a Recruit (user, live 2026-10-05: "it pulls it directly onboard
		/// like the recruit mechanic"; live Guild Recruiter: SHOW_ENTITY in PLAY, no stop in hand). It is still a draw (rule
		/// book DC5: what such a card draws counts; "whenever you draw" fires). A non-minion, or a minion without room, is a
		/// normal draw (rule book: "the minion is moved to your hand instead of force played").
		/// </summary>
		public static IPlayable DrawIntoPlay(Controller c, IEntity source)
		{
			if (c.DeckZone.IsEmpty || !(Generic.IsMinionCard(c.DeckZone.TopCard)) || c.BoardZone.IsFull)
				return Draw(c);
			IPlayable playable = DrawPhase.Invoke(c, c.DeckZone.TopCard);
			c.NumCardsDrawnThisTurn++; // (still a draw: rule book DC5)
			NoteDrawn(c, playable); // (a minion: never "the first spell drawn"; kept for the rule's one place)
			Game game = c.Game;
			SummonBlock.Invoke(game, (Minion)playable, -1, source);
			game.TaskQueue.StartEvent();
			game.TriggerManager.OnDrawTrigger(playable);
			game.ProcessTasks();
			game.TaskQueue.EndEvent();
			MarkDraw(game);
			return playable;
		}

		public static Func<Controller, Card, IPlayable> DrawCardBlock
			=> delegate (Controller c, Card card)
			{
				IPlayable playable = Entity.FromCard(c, card);
				//c.NumCardsDrawnThisTurn++;
				AddHandPhase.Invoke(c, playable);
				return playable;
			};

		public static Func<Controller, IPlayable, IPlayable> DrawBlock
			=> delegate (Controller c, IPlayable cardToDraw)
			{
				if (!PreDrawPhase.Invoke(c))
					return null;

				IPlayable playable = DrawPhase.Invoke(c, cardToDraw);
				//c.NumCardsToDraw--; 

				if (!AddHandPhase.Invoke(c, playable))
					// HsServer (2026-10-08): burned from a full hand — not drawn (rule book DC4, Advanced_rulebook.txt:1091: "If your hand is
					// full, the card is not drawn"; DC3 :1090: Holy Wrath "does require the card to successfully enter your hand"): no draw
					// counted, no card handed on to what the effect does with "the drawn card"
					return null;
				c.NumCardsDrawnThisTurn++;
				{
					// HsServer (TITANS): the first spell drawn this turn (Jotun) — only a card that reached the hand: rule book DC4
					// (Advanced_rulebook.txt:1091) "A card is only considered drawn if it moves from the top of your deck to your
					// hand. If your hand is full, the card is not drawn"
					NoteDrawn(c, playable);
					EnchantDrawn(c, playable);
					// DrawTrigger vs TOPDECK ?? not sure which one is first

					Game game = c.Game;

					// HsServer: rules B12 — a drawn card fires "whenever you draw" triggers however it was picked (a
					// "draw a spell" tutor too), before its own on-draw effect.
					game.TaskQueue.StartEvent();
					game.TriggerManager.OnDrawTrigger(playable);
					game.ProcessTasks();
					game.TaskQueue.EndEvent();

					MarkDraw(c.Game);
					OnDrawn(c, playable);
				}

				return playable;
			};

		private static Func<Controller, bool> PreDrawPhase
			=> delegate (Controller c)
			{
				if (c.DeckZone.IsEmpty)
				{
					if (FatigueImmune(c)) return false; // HsServer (Dalaran Heist): "You are Immune to Fatigue" (Elixir of Vim)
					// HsServer: live order — a FATIGUE block on the hero (META TARGET), the player's FATIGUE count, the damage,
					// then SLUSH_TIME 3000 (captured: RESEARCH/captures/heist-run-20260929 Power.log).
					Game game = c.Game;
					int fatigueDamage = c.Hero.Fatigue + 1;
					if (game.History)
					{
						game.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.FATIGUE, c.Hero.Id, "", 0, 0));
						game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.TARGET, Data = 0, Info = new List<int> { c.Hero.Id } });
					}
					c.Hero.Fatigue = fatigueDamage;
					game.RulesDamage = true; // Fatigue isn't damage the hero deals (no Lifesteal weapon heal)
					try { DamageCharFunc(c.Hero, c.Hero, fatigueDamage, false); }
					finally { game.RulesDamage = false; }
					if (game.History)
					{
						game.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 3000, Info = new List<int> { c.Hero.Id } });
						game.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
					}
					return false;
				}
				return true;
			};

		private static Func<Controller, IPlayable, IPlayable> DrawPhase
			=> delegate (Controller c, IPlayable cardToDraw)
			{
				IPlayable playable = c.DeckZone.Draw(cardToDraw);

				c.Game.Log(LogLevel.INFO, BlockType.ACTION, "DrawPhase", !c.Game.Logging ? "" : $"{c.Name} draws {playable}");

				// (the card coming off the deck in a draw — what an overdraw reads: Godfrey, IMPFERNAL; it counts as drawn only once in the
				// hand: DrawBlock / DrawIntoPlay)
				c.LastCardDrawn = playable.Id;
				playable[HsTag.DRAWN_ON_TURN] = c.Game.Turn; // HsServer: "the cards drawn" this turn
				// (NoteDrawn — the first spell drawn this turn — comes once the card is in the hand: rule book DC4)

				return playable;
			};

		/// <summary>HsServer: a player effect on each card they draw (DRAWN_CARD_ENCHANTMENT: Demonslayer Kurtrus).</summary>
		private static void EnchantDrawn(Controller c, IPlayable drawn)
		{
			if (c[HsTag.DRAWN_CARD_ENCHANTMENT] > 0 && Cards.FromAssetId(c[HsTag.DRAWN_CARD_ENCHANTMENT]) is Card enchantment)
				new Tasks.SimpleTasks.AddEnchantmentTask(enchantment.Id, Tasks.SimpleTasks.EntityType.TARGET).Process(c.Game, c, c.Hero, drawn);
		}

		public static IPlayable Draw(Controller c, int index)
		{
			IPlayable playable = c.DeckZone.Remove(index);
			c.Game.Log(LogLevel.INFO, BlockType.ACTION, "DrawPhase", !c.Game.Logging ? "" : $"{c.Name} draws {playable}");

			c.LastCardDrawn = playable.Id;
			playable[HsTag.DRAWN_ON_TURN] = c.Game.Turn;

			bool inHand = AddHandPhase.Invoke(c, playable);
			if (inHand)
			{
				c.NumCardsDrawnThisTurn++; // (HsServer 2026-10-08: only a card that reached the hand is drawn — rule book DC4)
				NoteDrawn(c, playable); // HsServer (TITANS): in the hand only — rule book DC4
				EnchantDrawn(c, playable);
				// DrawTrigger vs TOPDECK ?? not sure which one is first

				Game game = c.Game;

				game.TaskQueue.StartEvent();
				game.TriggerManager.OnDrawTrigger(playable);
				game.ProcessTasks();
				game.TaskQueue.EndEvent();
				
				MarkDraw(c.Game);
				OnDrawn(c, playable);
			}

			return inHand ? playable : null; // (burned: not drawn — rule book DC3/DC4)
		}
	}
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
