// HsServer addition (not upstream SabberStone): Showdown in the Badlands (WILD_WEST, client set 1892) and its Delve into Deepholm
// mini-set (DEEP_) — the set's shared card pieces (Quickdraw, "If your deck started with no duplicates", "If you're holding another
// N-Cost card", "When this is played, discarded, or destroyed", the Elemental streak counter, Discover-to-summon) and the
// registration method. The engine rules are in Actions/WildWest.cs (Generic.*); the cards are in the chunk files
// (WildWestCardsGenHs*.cs). Every number goes through CardValues (V).
using System;
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
		// ---------------------------------------------------------------- shared pieces of the set

		/// <summary>"Quickdraw: Deal N damage" — a target only while Quickdraw is on (client PlayReq 95
		/// REQ_TARGET_IF_AVAILABLE_AND_DRAWN_THIS_TURN; live-20261005 140614 81935-81940: Sunspot Dragon's option has targets only
		/// then). Any character.</summary>
		private static readonly Dictionary<PlayReq, int> WildWestQuickdrawTarget =
			new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE_AND_DRAWN_THIS_TURN, 0 } };

		/// <summary>The same, minions only (Misfire "Quickdraw: Choose the targets": live 140614 81477 options list minions only).</summary>
		private static readonly Dictionary<PlayReq, int> WildWestQuickdrawMinionTarget =
			new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE_AND_DRAWN_THIS_TURN, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } };

		/// <summary>"If you're holding another N-Cost card, deal …" — a target only while the player holds another card of Cost
		/// <paramref name="cost"/> (internal PlayReq REQ_TARGET_IF_AVAILABLE_AND_HOLDING_COST; Generic.HoldingOtherOfCostMet). The
		/// glow comes with it (Playable.HasConditionalTarget).</summary>
		private static Dictionary<PlayReq, int> WildWestHoldingCostTarget(int cost) =>
			new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE_AND_HOLDING_COST, cost } };

		/// <summary>"Quickdraw: X" as its own effect in the card's POWER block (the Drilly the Kid / Glowstone Gyreworm composition:
		/// a check on the card + FlagTask; the glow comes from the card's QUICKDRAW tag). SelfCondition.IsQuickdraw reads
		/// HsTag.QUICKDRAW_PLAY (set at the play: played from hand the turn it entered it — never an effect's cast).</summary>
		private static ISimpleTask WildWestIfQuickdraw(ISimpleTask then) =>
			ComplexTask.Create(new ConditionTask(EntityType.SOURCE, SelfCondition.IsQuickdraw), new FlagTask(true, then));

		/// <summary>"If your deck started with no duplicates, X" (Generic.DeckStartedWithNoDuplicates: the deck as the game began; the
		/// check first so PoweredUp lights the card in hand).</summary>
		private static ISimpleTask WildWestIfNoDuplicates(ISimpleTask then) =>
			ComplexTask.Create(new ConditionTask(EntityType.SOURCE, Generic.DeckStartedWithNoDuplicatesCondition), new FlagTask(true, then));

		/// <summary>"If you're holding another N-Cost card, X" (Greedy Partner, Linedance Partner; Generic.HoldingOtherOfCost — the
		/// check first so PoweredUp lights the card in hand).</summary>
		private static ISimpleTask WildWestIfHoldingOtherOfCost(int cost, ISimpleTask then) =>
			ComplexTask.Create(new CustomTask((g, c, s, t, stack) => stack.Flag = Generic.HoldingOtherOfCost(c, s, cost)), new FlagTask(true, then));

		/// <summary>
		/// "When this is played, discarded, or destroyed, X" (Barrel of Sludge, Furnace Fuel; client tags 335 + 3482): played =
		/// PowerTask; discarded (a destroy in hand is a discard, rule book C3) = a DISCARD trigger in hand on itself (Hand of
		/// Gul'dan BT_300; its block's TriggerKeyword 335: Trigger.Keyword); destroyed in the deck = Power.DestroyedTask
		/// (Generic.RunDestroyedTask). <paramref name="effect"/> makes a new task for each slot.
		/// </summary>
		private static Power WildWestPlayedDiscardedOrDestroyed(Func<ISimpleTask> effect) => new Power
		{
			PowerTask = effect(),
			Trigger = new Trigger(TriggerType.DISCARD)
			{
				TriggerActivation = TriggerActivation.HAND,
				TriggerSource = TriggerSource.SELF,
				SingleTask = effect()
			},
			DestroyedTask = effect(),
		};

		/// <summary>"Quickdraw: Costs (N)." (Dehydrate): the card's Cost is N while Quickdraw is on (re-read whenever its Cost is;
		/// live-20261007-vfx-decks 68357-68482: COST 1 + POWERED_UP after the draw, back to 3 in the PLAY block).</summary>
		private static AdaptiveCostEffect WildWestQuickdrawCost(int cost) =>
			new AdaptiveCostEffect(p => cost, EffectOperator.SET, new SelfCondition(p => Generic.IsQuickdrawNow(p)));

		/// <summary>Power.HandCounter of an "@ (improved by each turn in a row you've played an Elemental)" card (Skarr, Overflow
		/// Surger, Lamplighter): its card's TAG_SCRIPT_DATA_NUM_1 + Generic.ElementalStreak.</summary>
		private static int WildWestStreakCounter(IPlayable p) => Generic.ElementalStreakCounter(p);

		/// <summary>
		/// "Discover a &lt;which&gt; to summon. Add the others to your hand." (World Pillar Fragment; Maruut Stonebinder — wiki Maruut
		/// trivia: "World Pillar Fragment generates the same outcome"): <paramref name="options"/> different cards of the whole
		/// generation pool (never ones that can't be on the board at random), the pick summoned (ChoiceAction.SUMMON), the others
		/// to hand in their order.
		/// </summary>
		private static ISimpleTask WildWestDiscoverToSummonOthersToHand(Func<Card, bool> which, int options = 3) =>
			new CustomTask((g, c, s, t, stack) =>
			{
				List<Card> offer = DiscoverFromTask.PickDistinct(g,
					Cards.GenerationPool(g, c).Where(x => which(x) && !x.NeverRandomlyOnBoard), options);
				if (offer.Count == 0) return;
				Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.SUMMON, offer.ToArray(),
					new CustomTask((g2, c2, s2, picked, st) =>
					{
						bool skipped = false;
						foreach (Card other in offer)
						{
							if (!skipped && picked != null && other == picked.Card) { skipped = true; continue; }
							AddToHand(c2, other);
						}
					}));
			});

		// ---------------------------------------------------------------- the set

		/// <summary>Showdown in the Badlands and Delve into Deepholm: the chunk files' registrations (added by the integrator).</summary>
		private static void WildWest(IDictionary<string, CardDef> cards)
		{
			WildWestChunk1(cards);
			WildWestChunk2(cards);
			WildWestChunk3(cards);
			WildWestChunk4(cards);
		}
	}
}
