// HsServer addition (not upstream SabberStone): shared rules the Dalaran Heist bosses DALA_BOSS_01..25 needed (group 1). The card
// scripts are in CardSets/HsServer/DalaranHeistCardsGenHs1.cs.
using SabberStoneCore.Enums;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>
		/// Internal rule tag (player, an aura: never sent): how many EXTRA times this player's played spells with Overload are cast
		/// ("Your spells with Overload cast twice / thrice": Vas'no's Stormswell DALA_BOSS_20p = 1, DALA_BOSS_20px = 2). Read by
		/// CastSpell next to SPELLS_CAST_TWICE. Live (live-20261007-212735 59942-60238, Stormswell thrice, BOT_451 with Overload (1)):
		/// in the one PLAY block POWER 0, POWER 1 (the Overload), then twice SLUSH_TIME 3000, POWER 0, POWER 1 — OVERLOAD_OWED 1, 2, 3:
		/// every cast overloads.
		/// </summary>
		public const GameTag OVERLOAD_SPELLS_EXTRA_CASTS = (GameTag)991101;

		/// <summary>Client card tag 3329 (unnamed in the client enum we have): only Counterfeit Coin (CFM_630) and Gallywix's Coin
		/// (GVG_028t) carry it — the "Gain 1 Mana Crystal this turn only" coins that lack COIN_CARD (2088).</summary>
		public const GameTag COIN_LIKE = (GameTag)3329;

		/// <summary>
		/// "A Coin" for Street Smarts (DALA_BOSS_10p / 10px): a COIN_CARD spell (IsCoin: The Coin and its prints) or a coin-like one
		/// (COIN_LIKE: Counterfeit Coin, Gallywix's Coin). Live (live-session-20261005-140614 38402 and 43155): Mo Eniwhiskers' Street
		/// Smarts triggered on the player's Counterfeit Coin (CFM_630) as on their Darkmoon Coin (DMF_COIN1).
		/// (Open question: whether IsCoin itself — Wishing Well's "a Coin" — should take COIN_LIKE too.)
		/// </summary>
		public static bool CountsAsCoin(IEntity p) => IsCoin(p) || p is Spell && p.Card[COIN_LIKE] > 0;

		/// <summary>The opening hand and the mulligan's replacements are being dealt (the game's BEGIN_* steps): those cards aren't
		/// "drawn" for draw triggers — the rule Casts When Drawn follows (Draw.OnDrawn; wiki Casts_When_Drawn.txt:8 "It is not
		/// triggered if the card is chosen during the mulligan"). A passive Hero Power is up from the game's start (Game.StartGame),
		/// so Assembly ("After a player draws a minion …") would otherwise copy opening-hand minions.</summary>
		public static bool DuringOpeningHand(Model.Game g) =>
			(Step)g[GameTag.STEP] is Step.BEGIN_DRAW or Step.BEGIN_MULLIGAN or Step.BEGIN_FIRST or Step.BEGIN_SHUFFLE or Step.INVALID;
	}
}
