// HsServer: the Rewind keyword (Across the Timeways). Spec from live (session scratchpad audit/rewind.md: live-20261006-darkmoon 35182-38031,
// live-20261006-yogg-cthun 27318-31130, live-20261007-vfx-decks): after a Rewind card's effect its player is offered Keep Timeline
// (TIME_000ta) / Rewind Timeline (TIME_000tb); Rewind puts the whole game back to before the card was played (a GAME_RESET block, sent by
// the adapter: SabberEngineGame) and the card is played again with a new roll, marked USED_REWIND (no second prompt).
// Wiki Rewind: "Rewinding will undo all effects the card had then immediately replay it"; "The entire game state will be reverted to the
// point it was before the card was played"; "If a player dies from a Rewind card being played, there will not be an option to use the
// Rewind effect"; the turn timer "will automatically pick 'keep timeline' when their turn ends" (Keep is the first option).

using System;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>Client tags: the card was played again by a Rewind (USED_REWIND 3945: "after you Rewind" texts read it), the
		/// rewound card's entity (4384) and, for a minion, the board slot it was played to (4385) — live, on the replayed card.</summary>
		public const GameTag USED_REWIND = (GameTag)3945, REWIND_SOURCE = (GameTag)4384, REWIND_POSITION = (GameTag)4385;

		/// <summary>How many Rewinds a card has (its text's "&lt;b&gt;Rewind&lt;/b&gt;" count; at least 1 for a Rewind card).</summary>
		public static int RewindsOf(Card card)
		{
			if (card?.Text == null) return 0;
			int n = (card.Text.Length - card.Text.Replace("<b>Rewind</b>", "").Length) / "<b>Rewind</b>".Length;
			return n;
		}

		/// <summary>
		/// HsServer (2026-10-08): after a rewind, a card with versions for its Rewinds used becomes the next one — the related card of the
		/// same name whose USED_REWIND (client data) is the count used, after the last one the version with no Rewind left (Mister
		/// Clocksworth TIME_038 → t1 → t2 → t3). Live (titans-vfx 174206 72541 / 74185 / 75830): right after the GAME_RESET, a TRIGGER
		/// block (index 1) on the card: hand → SETASIDE, the 1069 pulse and the CHANGE_ENTITY of the same entity, back to its hand slot.
		/// </summary>
		public static void RewindVersion(Game g, IPlayable card)
		{
			if (card?.Controller == null || card.Zone?.Type != Zone.HAND) return;
			int used = card[USED_REWIND];
			var versions = (Loader.ReprintCards.RelatedCardIds(card.Card.Id) ?? new List<string>()).Select(Cards.TryFromId)
				.Where(x => x != null && x.Name == card.Card.Name && x.Type == card.Card.Type).Append(card.Card).Distinct().ToList();
			if (versions.Count < 2) return;
			const GameTag REWIND = (GameTag)3934;
			Card next = versions.FirstOrDefault(x => x[REWIND] > 0 && x[USED_REWIND] == used && used > 0)
			            ?? (versions.Any(x => x[REWIND] > 0 && x[USED_REWIND] == used - 1 && used > 0) ? versions.FirstOrDefault(x => x[REWIND] == 0) : null);
			if (next == null || next == card.Card) return;
			Controller c = card.Controller;
			int slot = card.ZonePosition;
			if (g.History)
				g.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, card.Id, "", 1, 0));
			c.HandZone.Remove(card);
			c.SetasideZone.Add(card);
			IPlayable now = ChangeEntityBlock(c, card, next, false);
			now[USED_REWIND] = used; // (the count stays the entity's)
			c.SetasideZone.Remove(now);
			c.HandZone.Add(now, Math.Min(slot, c.HandZone.Count));
			if (g.History)
				g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
		}

		/// <summary>
		/// After a Rewind card's effect: its player picks Keep Timeline or Rewind Timeline (a GENERAL choice, Keep first). Only where the
		/// adapter can rewind (Game.RewindEnabled: the live game, the human player; never an AI search copy), not on a replay (USED_REWIND),
		/// not when a hero died. Rewind Timeline's cast sets Game.PendingRewind; the adapter does the rest.
		/// </summary>
		public static void OfferRewind(Game g, Controller c, IPlayable card)
		{
			// as many Rewinds as its text shows (Mister Clocksworth: "Rewind, Rewind, Rewind" — wiki: asked again after each rewind)
			// (2026-10-08: Rewinds left = its text's, less those used since this version — a versioned card (Clocksworth t1..t3) carries
			// the used count in its own data: RewindVersion)
			if (!g.RewindEnabled || c != g.Player1 || card == null || RewindsOf(card.Card) - (card[USED_REWIND] - card.Card[USED_REWIND]) <= 0) return;
			// (2026-10-08: once per play — a Battlecry run twice (Brann, Shudderblock) offered it twice; picking "Rewind" on the first left the
			// second offer standing for a game that was rewound away: soak 3900315 "choice 6 rejected")
			for (Choice open = c.Choice; open != null; open = open.NextChoice)
				if (open.SourceId == card.Id && open.ChoiceAction == ChoiceAction.CAST) return;
			g.DeathProcessingAndAuraUpdate();
			if (c.Hero.IsDead || c.Opponent.Hero.IsDead || g.State != State.RUNNING) return;
			CreateChoiceCards.Invoke(c, card, null, ChoiceType.GENERAL, ChoiceAction.CAST,
				new[] { Cards.FromId("TIME_000ta"), Cards.FromId("TIME_000tb") }, null);
		}
	}
}
