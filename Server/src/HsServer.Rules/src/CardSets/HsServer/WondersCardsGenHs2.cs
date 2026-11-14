// HsServer addition (not upstream SabberStone): Caverns of Time (WONDERS, client set 1898, 2023) — chunk 1: the Neutral
// C'Thun cards, Dragons and the time travellers (shared pieces: WondersCardsGenHs.cs; engine rules: Actions/Wonders.cs).
// The "counts as a copy" reprints of this chunk (client tag 858: WON_124 = OG_284, WON_125 = OG_283, WON_131 = OG_321,
// WON_329 = BRM_034) are NOT registered under their WON ids: their originals are rewritten here to the current text and the
// loader gives the copies the original's script (Loader/ReprintCards.cs CountsAsCopyOf). Every number goes through
// CardValues (V).
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Actions;
using SabberStoneCore.Auras;
using SabberStoneCore.Conditions;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Loader;
using SabberStoneCore.src.Loader;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks;
using SabberStoneCore.Tasks.SimpleTasks;
using SabberStoneCore.Triggers;

namespace SabberStoneCore.CardSets.HsServer
{
	public static partial class CurrentCoreCardsGen
	{
		/// <summary>"It costs (N) less" / "Reduce the Cost … by (N)" on a card in hand: the client's generic "Cost - N" enchantment
		/// (GBL_*e, ENCHANTMENT_INVISIBLE — what live attaches when a card has no cost enchantment of its own: Reserved Spot
		/// WORK_021, live-20261004-night 222041 15557), N times "Cost - 1" for an amount the client has none for.</summary>
		private static void WondersC1CostLess(Game g, IEntity source, IPlayable target, int n)
		{
			if (target == null || n <= 0) return;
			if (GenericCostLess(n) is string less)
				Give(g, source, target, less);
			else
				for (int i = 0; i < n; i++) Give(g, source, target, "GBL_003e");
		}

		/// <summary>The Dragon cards in the player's hand (minions of the Dragon type, Amalgams included).</summary>
		private static List<IPlayable> WondersC1DragonsInHand(Controller c) => c.HandZone.Where(p => IsRaceCard(p, Race.DRAGON)).ToList();

		private static void WondersChunk1(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- C'Thun (the 27.2 ritual: Actions/Wonders.cs CthunRitual)
			// Wiki C'Thun, patch 27.2: "Cards that buff C'Thun now permanently increment its attack and health, instead of granting
			// an enchantment" — RitualTask is the shared ritual (player CTHUN_ATTACK_BUFF / CTHUN_HEALTH_BUFF totals, permanent base
			// stats on every C'Thun and every card that follows its gains, the display copy, the ritual effect).

			// [OG_284] Twilight Geomancer (2) — Taunt. Battlecry: Give your C'Thun +1/+1 and Taunt (wherever it is).
			// (Caverns of Time copy WON_124 plays this: tag 858. Wiki Twilight Geomancer, patch 27.2: "Changed to ... Give your
			// C'Thun +1/+1 and Taunt". Was RitualTask(Taunt) only. Taunt is the ritual's plain TAUNT + player CTHUN_TAUNT_BUFF.)
			cards.Add("OG_284", new CardDef(new Power
			{
				PowerTask = new RitualTask(V("OG_284", "stats", 1), V("OG_284", "stats", 1), taunt: true)
			}));
			// [OG_283] C'Thun's Chosen (4) — Divine Shield. Battlecry: Give your C'Thun +3/+3 (wherever it is).
			// (Copy WON_125. Wiki C'Thun's Chosen, patch 27.2: "+2/+2" → "+3/+3". Divine Shield and the Undead type are tags.)
			cards.Add("OG_283", new CardDef(new Power
			{
				PowerTask = new RitualTask(V("OG_283", "stats", 3))
			}));
			// [WON_131] Crazed Worshipper (4) — Taunt. Whenever this minion takes damage, give your C'Thun +1/+1 (wherever it is).
			// = OG_321 (tag 858): its upstream script (TAKE_DAMAGE, SELF, RitualTask(1)) already matches the current text
			// (ReprintCards.Reviewed "OG_321"); nothing to register.

			// [WON_144] Eyestalk of C'Thun (4) — Taunt, Lifesteal. Whenever your C'Thun gains Attack or Health, this does too
			// (wherever it is). (Power.FollowsCthunGains: the shared ritual gives it the same permanent gain in deck / hand / play,
			// not silenced; only the ritual's "(wherever it is)" gains — Blizzard forums: "If C'thun is drawn and buffed by Stolen
			// Goods. It will not buff Eyestalk of Ct'hun along with it." Taunt and Lifesteal are tags.)
			cards.Add("WON_144", new CardDef(WondersFollowsCthun));

			// ---------------------------------------------------------------- Dragons

			// [BRM_034] Blackwing Corruptor (5) — Battlecry: If you're holding a Dragon, deal 5 damage.
			// (Copy WON_329. Wiki Blackwing Corruptor, patch 27.2.0.183876: "now deals 5 damage instead of the previous 3". The
			// player picks the target only while holding a Dragon: REQ_TARGET_IF_AVAILABLE_AND_DRAGON_IN_HAND; RLK_915's shape.)
			cards.Add("BRM_034", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE_AND_DRAGON_IN_HAND, 0 } }, new Power
			{
				PowerTask = IfHoldingDragon(new DamageTask(V("BRM_034", "damage", 5), EntityType.TARGET))
			}));

			// [WON_140] Future Emissary (4) — Battlecry: Reduce the Cost of all Dragons in your hand by (1). Give them +1/+1.
			// (Per Dragon: the generic "Cost - 1" GBL_003e and its own WON_140e "Future Favor", as live's Reserved Spot gives its
			// own enchantment and the invisible generic cost one. Inside the card's hand-buff effect when one is recorded —
			// none is for WON_140 yet, so NamedFx opens nothing.)
			cards.Add("WON_140", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var dragons = WondersC1DragonsInHand(c);
					if (dragons.Count == 0) return;
					Generic.NamedFx((IPlayable)s, "HandBuff", dragons.Select(p => p.Id), () =>
					{
						foreach (IPlayable dragon in dragons)
						{
							WondersC1CostLess(g, s, dragon, V("WON_140", "discount", 1));
							Give(g, s, dragon, "WON_140e");
						}
					});
				})
			}));
			cards.Add("WON_140e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("WON_140", "stats", 1))) }));

			// [WON_146] Soridormi (5) — Dormant for 2 turns. When this awakens, reduce the Cost of all Dragons in your hand by (4).
			// (The Dormant is data: client 1652 + the counter WON_146e "Sands of Time" in Loader.DormantCards, awaken effect index 0
			// from live-20261005 140614 42116. Wiki Soridormi: "The discount applies exclusively to Dragon minions currently in your
			// hand at the moment of awakening"; "Reduced costs cannot go below zero" (the cost rule). Live played its HandBuff Gold
			// effect even with no Dragon in hand (Targets=[]), so it opens whatever the hand holds.)
			cards.Add("WON_146", new CardDef(new Power
			{
				AwakenTask = new CustomTask((g, c, s, t, stack) =>
				{
					var dragons = WondersC1DragonsInHand(c);
					Generic.NamedFx((IPlayable)s, "HandBuff", dragons.Select(p => p.Id), () =>
					{
						foreach (IPlayable dragon in dragons)
							WondersC1CostLess(g, s, dragon, V("WON_146", "discount", 4));
					});
				})
			}));

			// ---------------------------------------------------------------- the others

			// [WON_138] Shark Puncher (2) — Deathrattle: Give a random friendly Pirate +2/+2. (A Pirate on the board, alive; none:
			// nothing. Wailing Banshee's shape.)
			cards.Add("WON_138", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (RandomFriendly(g, c, m => m.IsRace(Race.PIRATE), s) is Minion pirate) Give(g, s, pirate, "WON_138e");
				})
			}));
			cards.Add("WON_138e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("WON_138", "stats", 2))) }));

			// [WON_139] Timeline Accelerator (3) — Battlecry: Draw a Mech. It costs (2) less. (A random Mech of the deck — a hidden
			// zone; burnt or none: no discount. The client has no WON_139e: the generic "Cost - 2" GBL_002e.)
			cards.Add("WON_139", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (DrawOne(g, c, IsMech) is IPlayable drawn) WondersC1CostLess(g, s, drawn, V("WON_139", "discount", 2));
				})
			}));

			// [WON_143] Infinite Amalgam (4) — Inspire, Frenzy, Spellburst, Honorable Kill, and Overkill: Summon a random 1-Cost
			// minion. (Each keyword its own way — rule book keyword pages: Inspire "activates each time the controlling hero uses
			// their Hero Power"; Frenzy "A one-time effect after this survives damage"; Spellburst "the first time you cast a spell
			// with it in play, and only the first time"; Honorable Kill / Overkill on your turn, even if it dies (Character.cs runs
			// them with their keywords). Each trigger shows its own keyword (Trigger.Keyword). The minion is random: the board pool.)
			ISimpleTask amalgamSummon = new CustomTask((g, c, s, t, stack) =>
				Summon(g, c, RandomMinionOfCost(g, V("WON_143", "cost", 1)), s));
			cards.Add("WON_143", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.INSPIRE) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = amalgamSummon },
					Frenzy(amalgamSummon),
					Spellburst(amalgamSummon)),
				HonorableKillTask = amalgamSummon,
				OverkillTask = amalgamSummon
			}));

			// [WON_345] Valstann Staghelm (6) — Deathrattle: Summon a Taunt minion from your deck. (Random: a hidden zone, a
			// trigger. Full board: nothing. From the deck by a Deathrattle: the far right, Advanced rulebook "Where do Minions
			// summoned by Deathrattles spawn?" (Meat Wagon) — SummonBlock's Force Play. Valstann's client TAUNT tag is only a
			// reference tag: it has no Taunt itself.)
			cards.Add("WON_345", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => SummonFromDeck(g, c, s, p => p is Minion m && m.HasTaunt))
			}));
		}
	}
}
