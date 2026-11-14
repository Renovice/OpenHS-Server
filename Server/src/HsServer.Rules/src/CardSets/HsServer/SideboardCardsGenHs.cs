// HsServer addition (not upstream SabberStone): the cards with a deck sideboard ("While building your deck, …") whose sets
// aren't scripted yet — Zilliax Deluxe 3000 and its modules (Whizbang's Workshop), Commander Beatrix and King of the
// Underbelly (The Violet Hold) — plus their tokens. E.T.C., Band Manager is in FestivalCardsGenHs.cs. The sideboard rules
// (where the cards come from, Discover from it, cards that join the deck, a deck card built from modules) are the engine's:
// Actions/Sideboards.cs. Every number goes through CardValues (V).
using SabberStoneCore.Actions;
using SabberStoneCore.Auras;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Loader;
using SabberStoneCore.src.Loader;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks;
using SabberStoneCore.Tasks.SimpleTasks;
using SabberStoneCore.Triggers;
using System.Collections.Generic;

namespace SabberStoneCore.CardSets.HsServer
{
	public static partial class CurrentCoreCardsGen
	{
		/// <summary>The client's generic "Costs (N) less." enchantment for an amount (GBL_*e "Cost - N": GenericCostEnchantments),
		/// else null.</summary>
		private static string GenericCostLess(int n) => n switch
		{
			1 => "GBL_003e", 2 => "GBL_002e", 3 => "GBL_004e", 4 => "GBL_008e", 5 => "GBL_011e", 6 => "GBL_012e", 7 => "GBL_013e", _ => null,
		};

		/// <summary>"Discover one [of your sideboard]. It costs (N) less." — the pick gets the generic "Cost - N" enchantment
		/// (N times "Cost - 1" for an amount the client has none for).</summary>
		private static ISimpleTask DiscoverFromSideboardCostingLess(int n) =>
			GenericCostLess(n) is string less ? Generic.DiscoverFromSideboard(less) : Generic.DiscoverFromSideboard("GBL_003e", n);

		private static void Sideboards(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Zilliax Deluxe 3000 (Whizbang's Workshop)
			// [TOY_330] Zilliax Deluxe 3000 — While building your deck, customize your very own Zilliax Deluxe 3000! The deck
			// card is built from its sideboard when the game starts (Generic.AsBuilt: the cosmetic module's card with both
			// functional modules combined); the card itself has no effect of its own. The cosmetic modules (the eight
			// "Zilliax Deluxe 3000" looks, client ZILLIAX_CUSTOMIZABLE_COSMETICMODULE) are pictures: their effects are the
			// modules' (wiki: "This is purely a cosmetic pick, and no gameplay changes occur from picking the different
			// artworks").
			foreach (string id in new[] { "TOY_330", "TOY_330t5", "TOY_330t6", "TOY_330t7", "TOY_330t8", "TOY_330t9", "TOY_330t10", "TOY_330t11", "TOY_330t12" })
				cards.Add(id, new CardDef(new Power { NoScript = true }));
			// [TOY_330t92] Recursive Module (1) — Deathrattle: Shuffle this into your deck. (A new copy: a card going back into
			// the deck keeps nothing; on a built Zilliax "this" is the whole Zilliax.)
			cards.Add("TOY_330t92", new CardDef(new Power { DeathrattleTask = new CopyTask(EntityType.SOURCE, Zone.DECK) }));
			// [TOY_330t93] Haywire Module (2) — At the end of your turn, deal 3 damage to your hero.
			cards.Add("TOY_330t93", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new DamageTask(V("TOY_330t93", "damage", 3), EntityType.HERO) }
			}));
			// [TOY_330t94] Power Module (2) — At the start of your turn, double this minion's Attack. (TOY_330t94e "Doubled
			// Attack.")
			// (Doubling = gaining its current Attack, as Gahz'rilla's GVG_049e: an ADD the enchantment carries in its script data.)
			cards.Add("TOY_330t94", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me && me.AttackDamage > 0)
							Give(g, s, me, "TOY_330t94e", me.AttackDamage * (V("TOY_330t94", "multiplier", 2) - 1));
					})
				}
			}));
			cards.Add("TOY_330t94e", new CardDef(new Power { Enchant = new Enchant(GameTag.ATK, EffectOperator.ADD) }));
			// [TOY_330t95] Pylon Module (3) — Your other minions have +1 Attack. (TOY_330t95e1 "Pylon's Unity".)
			cards.Add("TOY_330t95", new CardDef(new Power { Aura = new Aura(AuraType.BOARD_EXCEPT_SOURCE, "TOY_330t95e1") }));
			cards.Add("TOY_330t95e1", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TOY_330t95", "attack", 1))) }));
			// [TOY_330t96] Virus Module (4) — Elusive, Poisonous. (Keywords only: card data.)
			cards.Add("TOY_330t96", new CardDef(new Power { NoScript = true }));
			// [TOY_330t97] Twin Module (4) — Battlecry: Summon a copy of this.
			cards.Add("TOY_330t97", new CardDef(new Power { PowerTask = new SummonCopyTask(EntityType.SOURCE) }));
			// [TOY_330t98] Ticking Module (4) — Costs (1) less for each friendly minion.
			cards.Add("TOY_330t98", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.BoardZone.CountExceptUntouchables * V("TOY_330t98", "discount", 1))
			}));
			// [TOY_330t99] Perfect Module (5) — Divine Shield, Taunt, Lifesteal, Rush. (Keywords only: card data.)
			cards.Add("TOY_330t99", new CardDef(new Power { NoScript = true }));

			// ---------------------------------------------------------------- The Violet Hold
			// [JAIL_397] Commander Beatrix (5) — Taunt. While building your deck, pick a 2-Cost minion. Ten copies join your
			// deck! (Her deck-building effect: Power.SideboardTask, before the opening hands — live-20261003-dk-classxp; wiki:
			// "This card will add 10 additional copies of any Paladin or Neutral 2-cost minion to the player's deck at the
			// start of the game", the copies "are considered to be generated cards, not ones that started in the player's
			// deck".)
			cards.Add("JAIL_397", new CardDef(new Power
			{
				SideboardTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Card pick in Generic.SideboardCards(c, Cards.SelfOf(s)))
						for (int i = 0; i < V("JAIL_397", "copies", 10); i++)
							Generic.JoinDeck(c, s, pick);
				})
			}));
			// [JAIL_831] King of the Underbelly (6) — While building your deck, pick 3 contraband Beasts. Battlecry: Discover
			// one. It costs (3) less. (Wiki: "Other functionality related to sideboard cards functions the same as with E.T.C.,
			// Band Manager": the pick leaves the sideboard; none picked or left: nothing.)
			cards.Add("JAIL_831", new CardDef(new Power { PowerTask = DiscoverFromSideboardCostingLess(V("JAIL_831", "discount", 3)) }));
		}
	}
}
