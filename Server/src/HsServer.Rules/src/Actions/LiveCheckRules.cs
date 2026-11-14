// HsServer addition (not upstream SabberStone): small shared rules the livecheck engine pass (2026-10-10) settled from live recordings.
using SabberStoneCore.Enums;
using SabberStoneCore.Model.Entities;

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>Client card tag 4392 (unnamed): the card is a Ysera — Ysera EX1_572 / VAN_ / CORE_VAN_, Ysera the Dreamer CS3_033 /
		/// LEG_, Ysera, Unleashed DRG_320, Ysera, Emerald Aspect EDR_000, Story_07_Ysera. What "except Ysera" (Ysera Awakens DREAM_02)
		/// leaves out: live session-20260930-125547 13769, CS3_033 undamaged (the name check missed "Ysera the Dreamer").</summary>
		public const GameTag YSERA = (GameTag)4392;

		/// <summary>Client card tag WHELP (2355): the card is a Whelp (Onyxia's ONY_001t, BRM_004t, Black Whelp BRM_022t…) — a
		/// "Whelp" family check reads it, not the name.</summary>
		public const GameTag WHELP = (GameTag)2355;

		/// <summary>Client tag NON_KEYWORD_CHARGE (3968): it may attack this turn like Charge, without the keyword (no Charge on the card,
		/// no "minion with Charge"). Live: the minion a "gain control … until end of turn" effect takes (Potion of Madness CFM_603e:
		/// session-20261003-184102 2538, 20261007-113244; Cursed Chains CATA_496 too, with its CANT_ATTACK) — ours gave it Charge.</summary>
		public const GameTag NON_KEYWORD_CHARGE = (GameTag)3968;

		/// <summary>The effect "can attack this turn" of a temporary steal (NON_KEYWORD_CHARGE).</summary>
		public static Enchants.Effect NonKeywordCharge => new Enchants.Effect(NON_KEYWORD_CHARGE, Enchants.EffectOperator.SET, 1);

		/// <summary>A Gallywix effect sees <paramref name="played"/> unless its card "(Won't trigger Gallywix.)" (Gallywix's Coin:
		/// Power.WontTriggerGallywix) — Trade Prince Gallywix GVG_028 and the Heist boss's Greed is Good DALA_BOSS_57p alike (live
		/// session-20261005-140614 74794-75011).</summary>
		public static bool TriggersGallywix(IPlayable played) => !(played?.Card.Power?.WontTriggerGallywix ?? false);
	}
}
