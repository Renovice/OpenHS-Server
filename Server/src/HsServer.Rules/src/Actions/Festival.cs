// HsServer addition (not upstream SabberStone): Festival of Legends' shared rules (Finale, Soloist, Harmonic / Remixed cards
// that change in hand, "take Fatigue damage", Overheal counts, Riffs).
using System.Collections.Concurrent;
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
		/// <summary>The client's FINALE keyword tag.</summary>
		public const GameTag FINALE = (GameTag)2820;

		/// <summary>Client card tag (unnamed in the client enum) on the three Riffs (Verse, Chorus, Bridge).</summary>
		public const GameTag RIFF = (GameTag)2819;

		/// <summary>
		/// HsServer: Finale (wiki Finale: "the player's remaining mana must be 0 when it is played"; "Finale is not tied to the
		/// card's original cost, but its current cost"; "If a spell with Finale is cast randomly, the Finale effect will only
		/// trigger if the player is currently at 0 mana crystals"; "Playing a 0-cost Finale card with 0 mana remaining will
		/// trigger the Finale effect"). The card's effect runs right after it is paid for, so the check is the player's mana
		/// left when the effect asks. A Riff played by another Riff's Finale doesn't run its own (Riff Stopper, ETC_363e2).
		/// </summary>
		public static bool Finale(IEntity source)
		{
			Controller c = source?.Controller;
			return c != null && c.ManaToSpend == 0;
		}

		/// <summary>
		/// HsServer: a card's second effect (Power.SecondPowerTask) in its own POWER block after the main one. Live
		/// (live-20261007-vfx-decks, every Riff): SLUSH_TIME 3000, then BLOCK_START POWER effect index 1 ShowInHistory — empty
		/// when the effect does nothing.
		/// </summary>
		public static void SecondPowerBlock(Controller c, IPlayable card, IPlayable target)
		{
			Tasks.ISimpleTask task = card?.Card.Power?.SecondPowerTask;
			if (task == null) return;
			Game g = c.Game;
			if (g.History)
			{
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 3000, Info = new List<int> { card.Id } });
				PowerHistoryBlockStart block = PowerHistoryBuilder.BlockStart(BlockType.POWER, card.Id, "", 1, target?.Id ?? 0);
				// HsServer (WILD_WEST): a Quickdraw card's second block isn't shown in the history (live Pocket Sand, Rehydrate;
				// Power.SecondPowerInHistory)
				block.ShowInHistory = card.Card.Power.SecondPowerInHistory ?? card.Card[HsTag.QUICKDRAW] == 0;
				g.PowerHistory.Add(block);
			}
			task.Process(g, c, card, target);
			g.ProcessTasks();
			if (g.History)
				g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
		}

		/// <summary>The Finale glow (live POWERED_UP on Infinitize the Maxitude while its Cost equals the Mana left).</summary>
		public static bool FinaleReady(IPlayable card) => card?.Controller != null && card.Card[FINALE] > 0 && card.Cost == card.Controller.ManaToSpend;

		/// <summary>Soloist: "If you control no other minions" — no minion on the player's side but <paramref name="self"/>.</summary>
		public static bool Soloist(Controller c, IEntity self) => c.BoardZone.All(m => m.Id == self?.Id || m.IsDead || m.ToBeDestroyed);

		/// <summary>
		/// HsServer: "Take Fatigue damage" (wiki Fatigue: each time a player would draw from an empty deck the damage grows by 1;
		/// Baritone Imp, Crescendo and Crazed Conductor take that damage without drawing). The player's Fatigue counter goes up
		/// by one and their hero takes it (rules damage, like the draw's); returns the damage.
		/// </summary>
		public static int TakeFatigue(Controller c)
		{
			Game game = c.Game;
			if (FatigueImmune(c)) return 0; // HsServer (Dalaran Heist): "You are Immune to Fatigue" (Elixir of Vim)
			int damage = c.Hero.Fatigue + 1;
			c.Hero.Fatigue = damage;
			game.RulesDamage = true;
			try { DamageCharFunc(c.Hero, c.Hero, damage, false); }
			finally { game.RulesDamage = false; }
			return damage;
		}

		// ---------------------------------------------------------------- Harmonic / Remixed: cards that change in hand

		private static readonly ConcurrentDictionary<string, Card> _harmonicPartner = new ConcurrentDictionary<string, Card>();
		private static readonly ConcurrentDictionary<string, Card[]> _remixVersions = new ConcurrentDictionary<string, Card[]>();

		private static bool SwapsEachTurn(Card card) =>
			card?.Text != null && (card.Text.Contains("Swaps each turn") || card.Text.Contains("Flips each turn"));

		/// <summary>A Harmonic card's other half (Harmonic X ↔ Dissonant X: &lt;id&gt; ↔ &lt;id&gt;t, both "(Swaps each turn.)" in the
		/// client text), or null. HsServer (ISLAND_VACATION): the Travel Agency's Brochures "(Flips each turn.)" too — their pair is
		/// the client's DISPLAY_CARD_ON_MOUSEOVER (Un'Goro Brochure WORK_050 ↔ Dalaran Brochure WORK_050t).</summary>
		public static Card HarmonicPartner(Card card) => card == null ? null : _harmonicPartner.GetOrAdd(card.Id, _ =>
		{
			if (!SwapsEachTurn(card)) return null;
			Card other = card[GameTag.DISPLAY_CARD_ON_MOUSEOVER] > 0 ? Cards.FromAssetId(card[GameTag.DISPLAY_CARD_ON_MOUSEOVER]) : null;
			other ??= card.Id.EndsWith("t") ? Cards.TryFromId(card.Id[..^1]) : Cards.TryFromId(card.Id + "t");
			return SwapsEachTurn(other) ? other : null;
		});

		/// <summary>A Remixed card's version: client text "(Changes each turn.)" and TRANSFORMED_FROM_CARD = its base (JAM_033t..t4,
		/// JAM_018t..t4; the unnamed tag 2165 is on most of them but also on unrelated cards, and not on Remixed Rhapsody's).</summary>
		public static bool IsRemixVersion(Card card) => RemixBaseOf(card) > 0;

		/// <summary>The base card (dbf) of a version that "changes each turn": a Remixed version (TRANSFORMED_FROM_CARD, its own
		/// "(Changes each turn.)") or — HsServer (LOST_CITY) — a version shown with its base (DISPLAY_CARD_ON_MOUSEOVER) of the same name
		/// whose base "changes each turn" (Titanographer Osk's 27 versions: "Gains a random Titan ability in your hand that changes
		/// each turn"); 0 for other cards.</summary>
		public static int RemixBaseOf(Card card)
		{
			if (card == null) return 0;
			if (card[GameTag.TRANSFORMED_FROM_CARD] > 0 && card.Text?.Contains("Changes each turn") == true) return card[GameTag.TRANSFORMED_FROM_CARD];
			int shown = card[GameTag.DISPLAY_CARD_ON_MOUSEOVER];
			if (shown <= 0 || !(Cards.FromAssetId(shown) is Card baseCard) || baseCard.Name != card.Name) return 0;
			return baseCard.Text?.IndexOf("changes each turn", System.StringComparison.OrdinalIgnoreCase) >= 0 ? shown : 0;
		}

		/// <summary>A Remixed card's versions, for the base or any version; empty for other cards.</summary>
		public static Card[] RemixVersions(Card card)
		{
			if (card == null) return System.Array.Empty<Card>();
			int baseDbf = IsRemixVersion(card) ? RemixBaseOf(card) : card.AssetId;
			return _remixVersions.GetOrAdd(baseDbf.ToString(), _ =>
				Cards.All.Where(x => RemixBaseOf(x) == baseDbf).OrderBy(x => x.Id).ToArray());
		}

		/// <summary>HsServer: live puts HAS_ACTIVATE_POWER 1 on every minion and Location in hand and never on spells or weapons
		/// (live-20261007-zilliax-etc-remix: all 223 minion / 1 Location hand reveals have it, none of 139 spells / 30 weapons);
		/// an in-hand change pulses it (0 before the CHANGE_ENTITY, 1 after) only on those cards (Remixed Musician does, Remixed
		/// Rhapsody and the Harmonic spells don't). The client reads it for Battlegrounds only (wire-tag-ignore).</summary>
		internal static bool HasActivatePowerInHand(IPlayable card) =>
			card is Minion || card?.Card.Type == CardType.LOCATION;

		/// <summary>
		/// A card in hand changes into another version of itself (Remixed, Harmonic), keeping its enchantments. Live
		/// (live-20261004-night 90319, live-20261007-zilliax-etc-remix): a TRIGGER block on the card, TARGET itself, then - for a
		/// minion / Location (<see cref="HasActivatePowerInHand"/>) - HAS_ACTIVATE_POWER 0, the CHANGE_ENTITY (TRANSFORMED_FROM_CARD /
		/// TIMES_BEEN_TRANSFORMED), HAS_ACTIVATE_POWER 1. Effect index: 0 for a card drawn, at the turn's end (Remixed) and the
		/// Harmonic swap; 1 for a card put into the hand otherwise (a Discover pick, a generated card, the starting hand). The 1069
		/// pulse (the new version's dbf id, then 0) comes with the CHANGE_ENTITY (ChangeEntityCore; live: 690 of 712).
		/// </summary>
		/// <param name="ownBlock">false: the caller's block is already the card's (an upgrade queued as its own block: no TARGET then —
		/// live dk-classxp 3928).</param>
		public static IPlayable ChangeInHand(Controller c, IPlayable card, Card version, int effectIndex = 1, bool ownBlock = true)
		{
			if (card == null || version == null || version == card.Card || card.Zone?.Type != Zone.HAND) return card;
			Game g = c.Game;
			bool activatePower = HasActivatePowerInHand(card);
			if (g.History && ownBlock)
			{
				g.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.TRIGGER, card.Id, "", effectIndex, 0));
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.TARGET, Data = 0, Info = new List<int> { card.Id } });
			}
			if (activatePower) card[HasActivatePower] = 0;
			IPlayable now = ChangeEntityBlock(c, card, version, false);
			// (2026-10-08: from the NEW card — a spell upgraded into a minion gets it: live dk-classxp 3928 JAIL_803)
			if (HasActivatePowerInHand(now)) now[HasActivatePower] = 1;
			if (g.History && ownBlock)
				g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			return now;
		}

		/// <summary>A random version of a Remixed card other than the one it is (wiki Remixed Dispense-o-bot: "The transformed card is
		/// chosen randomly, as all subsequent transforms. However, the forms cannot swap into themselves").</summary>
		private static Card OtherRemixVersion(Game g, Card current)
		{
			var others = RemixVersions(current).Where(x => x.Id != current.Id).ToList();
			return others.Count == 0 ? null : others[g.Random.Next(others.Count)];
		}

		/// <summary>The game hasn't begun yet: the starting hand is being dealt or mulliganed (Start of Game effects included).</summary>
		private static bool BeforeFirstTurn(Game g) => g.Step is Step.INVALID or Step.BEGIN_FIRST or Step.BEGIN_SHUFFLE or
			Step.BEGIN_DRAW or Step.BEGIN_MULLIGAN or Step.MAIN_BEGIN;

		/// <summary>
		/// HsServer: a Remixed card entering the hand takes a random version (wiki Remixed Dispense-o-bot: "When drawn or added to
		/// the player's hand, this transforms into one of the related cards"). Live (live-20261007-zilliax-etc-remix): drawn -
		/// right after the draw, effect index 0 (Remixed Rhapsody, 7257); put into the hand otherwise - effect index 1, with
		/// TAG_SCRIPT_DATA_NUM_1 = 1 set just before the block (live-20261004-night 90319, a Discover pick). The starting hand
		/// stays in its base form through the mulligan and changes when the game begins (<see cref="RemixStartingHands"/>).
		/// </summary>
		public static void RemixOnEnteringHand(Controller c, IPlayable card)
		{
			if (card?.Zone?.Type != Zone.HAND || IsRemixVersion(card.Card) || RemixVersions(card.Card).Length == 0) return;
			if (BeforeFirstTurn(c.Game)) return; // RemixStartingHands
			bool drawn = c.LastCardDrawn == card.Id && card[HsTag.DRAWN_ON_TURN] == c.Game.Turn;
			if (!drawn) card[GameTag.TAG_SCRIPT_DATA_NUM_1] = 1;
			ChangeInHand(c, card, OtherRemixVersion(c.Game, card.Card), drawn ? 0 : 1);
		}

		/// <summary>
		/// HsServer: live (live-20261007-zilliax-etc-remix 523-555, 144053, 146836: three games, the Remixed card in the first
		/// player's starting hand) - the starting hand's Remixed cards keep their base form through the mulligan and take a
		/// version on the first turn, after the MAIN_READY step block, before STEP MAIN_START_TRIGGERS: TAG_SCRIPT_DATA_NUM_1 = 1,
		/// then the entering-the-hand block (effect index 1). Any Remixed card still in its base form in either hand gets one
		/// here (the turn's player first; the second player's starting hand is unrecorded: live-checks). Game.NextStep.
		/// </summary>
		public static void RemixStartingHands(Game g)
		{
			foreach (Controller c in new[] { g.CurrentPlayer, g.CurrentOpponent })
			foreach (IPlayable card in c.HandZone.GetAll().ToList())
				if (!IsRemixVersion(card.Card) && RemixVersions(card.Card).Length > 0)
				{
					card[GameTag.TAG_SCRIPT_DATA_NUM_1] = 1;
					ChangeInHand(c, card, OtherRemixVersion(g, card.Card), 1);
				}
		}

		/// <summary>
		/// HsServer: the cards in hand that change at the end of each of their owner's turns, never on the opponent's turn. Game
		/// .NextStep calls it twice, at the live points:
		/// <list type="bullet">
		/// <item>MAIN_CLEANUP - <b>Remixed</b> cards ("(Changes each turn.)") change into another random version (wiki Remixed
		/// Dispense-o-bot: "the forms cannot swap into themselves"). The user checked and recorded it (2026-10-07: "remixes do
		/// end of turn"); live-20261007-zilliax-etc-remix 732, 1087, 1675, 2569 (Remixed Rhapsody, only on its owner's turns):
		/// after the end step's block (NEXT_STEP MAIN_CLEANUP) and the players' blocks, before STEP MAIN_CLEANUP; a TRIGGER
		/// block on the card, effect index 0, TARGET itself, the CHANGE_ENTITY (HAS_ACTIVATE_POWER around it for a minion).</item>
		/// <item>MAIN_NEXT - <b>Harmonic</b> cards swap halves ("(Swaps each turn.)"). Live (live-20261007-vfx-decks, Harmonic
		/// Disco: turns 24 and 26 of its owner, not the opponent's 25): after the cleanup step's block, a TRIGGER block on the
		/// card (effect index 0), TARGET itself, the CHANGE_ENTITY.</item>
		/// </list>
		/// </summary>
		public static void HandChangesAtTurnEnd(Controller c, Step at)
		{
			foreach (IPlayable card in c.HandZone.GetAll().ToList())
			{
				if (at == Step.MAIN_CLEANUP && IsRemixVersion(card.Card))
					ChangeInHand(c, card, OtherRemixVersion(c.Game, card.Card), 0);
				else if (at == Step.MAIN_NEXT && HarmonicPartner(card.Card) is Card other)
					ChangeInHand(c, card, other, 0);
				else if (at == Step.MAIN_NEXT && card.Card[DYNAMIC_KEYWORD1] > 0 && card is Minion twisted)
					SwapDynamicKeywords(c, twisted); // (CATACLYSM: Twisted Monstrosity, the same point as Harmonic)
			}
		}
	}
}
