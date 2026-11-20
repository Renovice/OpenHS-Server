// HsServer addition (not upstream SabberStone): Excavate treasures, Excavate cards and Forge cards (Showdown in the
// Badlands, TITANS, Fall of Ulduar, Into the Emerald Dream / Deepholm). They use the shared rules: Generic.Excavate
// (tiers, the treasure visual), Generic.Forge (the Forged version, FORGES_INTO), the EXCAVATE / FORGE triggers,
// SelfCondition.ExcavatedTwice / ForgedThisGame / IsQuickdraw. Every number goes through CardValues (V).
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
		private static readonly Dictionary<PlayReq, int> TargetFriendlyMinion =
			new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } };

		/// <summary>The minions next to a minion on its board.</summary>
		private static IEnumerable<Minion> Neighbors(Minion m)
		{
			var board = m.Controller.BoardZone;
			int pos = m.ZonePosition;
			if (pos > 0) yield return board[pos - 1];
			if (pos < board.Count - 1) yield return board[pos + 1];
		}

		/// <summary>"Discover a N-Cost card. It costs (0)." (your class or Neutral)</summary>
		private static ISimpleTask DiscoverCostCardFree(string card, int cost, string enchantment) =>
			DiscoverFromTask.ClassOrNeutral((c, x) => x.Cost == V(card, "cost", cost), enchantment);

		/// <summary>The minion that resolves this card's damage: "random spells" etc. come from the player's class.</summary>
		private static IEnumerable<Card> ClassCards(Controller c, CardType type) =>
			Cards.GenerationPool(c.Game, c).Where(x => x.Type == type && x.HasClass(c.HeroClass));

		private static void BadlandsTitans(IDictionary<string, CardDef> cards)
		{
			// [TTN_920] Mimiron, the Mastermind (3) — After you play a Mech, get a random one of Mimiron's Gadgets. (The six
			// Gadgets: the client's TTN_920t5..t10 spells; wiki: "all 6 of Mimiron's Gadgets".)
			string[] gadgets = { "TTN_920t5", "TTN_920t6", "TTN_920t7", "TTN_920t8", "TTN_920t9", "TTN_920t10" };
			cards.Add("TTN_920", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => IsMech(p)),
					SingleTask = new CustomTask((g, c, s, t, stack) => AddToHand(c, Cards.FromId(gadgets[g.Random.Next(gadgets.Length)])))
				}
			}));
			// [TTN_920t5] Mimiron's Coolant — The next card you play this turn costs (2) less.
			cards.Add("TTN_920t5", new CardDef(new Power { PowerTask = new AddEnchantmentTask("TTN_920e2", EntityType.CONTROLLER) }));
			cards.Add("TTN_920e2", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("TTN_920t5", "discount", 2))) { ShowAs = "TTN_920e3" },
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.PLAY_CARD) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = RemoveEnchantmentTask.Task },
					new Trigger(TriggerType.TURN_END) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = RemoveEnchantmentTask.Task })
			}));
			// [TTN_920t6] Mimiron's Cloakfield (1) — Give a friendly minion +3 Attack. It gains Stealth until your next turn.
			cards.Add("TTN_920t6", new CardDef(TargetFriendlyMinion, new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("TTN_920e1", EntityType.TARGET), new AddEnchantmentTask("TTN_920t6e1", EntityType.TARGET))
			}));
			cards.Add("TTN_920e1", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TTN_920t6", "attack", 3))) }));
			cards.Add("TTN_920t6e1", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.StealthEff),
				Trigger = new Trigger(TriggerType.TURN_START) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = RemoveEnchantmentTask.Task }
			}));
			// [TTN_920t7] Mimiron's Switch (1) — Choose two minions. They swap stats. (Two picks: Generic.PickTargets.)
			cards.Add("TTN_920t7", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int me = c.PlayerId; // (values only: the pick may resolve on an AI's copy — Generic.PickTargetsInOrder)
					Generic.PickTargetsInOrder(c, s, 2, ch => ch is Minion m && !m.Untouchable && !m.CantBeTargetedBySpells && (m.Controller.PlayerId == me || !m.HasStealth),
						(g2, c2, s2, pick, before) =>
						{
							if (before.Count < 1 || !(pick is Minion second) || !(g2.IdEntityDic.TryGetValue(before[0], out IPlayable p) && p is Minion first)) return;
							var chosen = new List<Minion> { first, second };
							if (chosen.Any(x => x.IsDead || x.Zone?.Type != Zone.PLAY)) return;
							int a1 = chosen[0].AttackDamage, h1 = chosen[0].Health, a2 = chosen[1].AttackDamage, h2 = chosen[1].Health;
							Generic.AddEnchantmentBlock(g2, Cards.FromId("TTN_920t7e"), (IPlayable)s2, chosen[0], a2, h2, 0);
							Generic.AddEnchantmentBlock(g2, Cards.FromId("TTN_920t7e"), (IPlayable)s2, chosen[1], a1, h1, 0);
						});
				})
			}));
			cards.Add("TTN_920t7e", new CardDef(new Power { Enchant = Enchants.Enchants.SetAttackHealthScriptTag }));
			// [TTN_920t8] Mimiron's Horn (1) — Give a minion Taunt and Divine Shield.
			cards.Add("TTN_920t8", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m) { m.HasTaunt = true; m.HasDivineShield = true; } })
			}));
			// [TTN_920t9] Mimiron's Rewinder (1) — Return a minion to its owner's hand.
			cards.Add("TTN_920t9", new CardDef(TargetMinion, new Power { PowerTask = new ReturnHandTask(EntityType.TARGET) }));
			// [TTN_920t10] Mimiron's Blades (1) — Deal 3 damage.
			cards.Add("TTN_920t10", new CardDef(TargetAny, new Power { PowerTask = new DamageTask(V("TTN_920t10", "damage", 3), EntityType.TARGET, true) }));

			// [YOG_522] Conductivity (2) — The next spell you cast this turn also targets adjacent minions. (Zentimo's way: the spell is
			// cast again on the target's neighbours — EnqueuePendingTask skips ones it couldn't target; YOG_522e goes after it.)
			cards.Add("YOG_522", new CardDef(new Power { PowerTask = new AddEnchantmentTask("YOG_522e", EntityType.CONTROLLER) }));
			cards.Add("YOG_522e", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.TARGET)
					{
						TriggerSource = TriggerSource.FRIENDLY,
						Condition = SelfCondition.IsSpellTargetingMinion,
						SingleTask = ComplexTask.Create(new IncludeAdjacentTask(EntityType.EVENT_TARGET), new EnqueuePendingTask(EntityType.STACK))
					},
					new Trigger(TriggerType.AFTER_CAST) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = RemoveEnchantmentTask.Task },
					new Trigger(TriggerType.TURN_END) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = RemoveEnchantmentTask.Task })
			}));

			// [CATA_153] Al'Akir, Lord of Storms (8) — Colossal +2, Rush, Windfury. Battlecry: Get 2 minions with Cost equal to
			// this minion's Attack. They cost (1). (User 2026-10-07: like Evolve, a generated Cost stops at 10.)
			cards.Add("CATA_153", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int cost = System.Math.Min(10, System.Math.Max(0, ((Minion)s).AttackDamage));
					for (int i = 0; i < V("CATA_153", "minions", 2); i++)
					{
						if (c.HandZone.IsFull || !(RandomCard(g, x => x.Type == CardType.MINION && x.Cost == cost) is Card minion)) break;
						IPlayable p = Entity.FromCard(c, minion);
						if (Generic.AddHandPhase.Invoke(c, p)) Give(g, s, p, "GBL_001e");
					}
				})
			}));
			Treasures(cards);
			ExcavateCards(cards);
			ForgeCards(cards);
		}

		private const int NATURE = 4; // client TAG_SPELL_SCHOOL

		// ------------------------------------------------------------------ Forge cards (client tag 2785; FORGES_INTO 3074)
		// The base card and its Forged version are separate client cards: each gets its own script.
		private static void ForgeCards(IDictionary<string, CardDef> cards)
		{
			// [DEEP_024] Glowstone Gyreworm (4) — Lifesteal. Quickdraw: Deal 5 damage. Forge: Change Quickdraw to Battlecry.
			// (Its target only while Quickdraw is on: client PlayReq 95, WildWestQuickdrawTarget — WILD_WEST 2026-10-07.)
			cards.Add("DEEP_024", new CardDef(WildWestQuickdrawTarget, new Power
			{
				PowerTask = ComplexTask.Create(new ConditionTask(EntityType.SOURCE, SelfCondition.IsQuickdraw),
					new FlagTask(true, new DamageTask(V("DEEP_024", "damage", 5), EntityType.TARGET)))
			}));
			cards.Add("DEEP_024t", new CardDef(BattlecryAny, new Power { PowerTask = new DamageTask(V("DEEP_024", "damage", 5), EntityType.TARGET) }));
			// [DEEP_027] Gloomstone Guardian (4) — Taunt. Choose One - Discard 2 cards; or Destroy one of your Mana Crystals.
			// Forge: Do NEITHER. (Discard: random — "Discard 2 cards" names no card to pick.)
			cards.Add("DEEP_027", new CardDef(new Power()));
			cards.Add("DEEP_027a", new CardDef(new Power { PowerTask = ComplexTask.DiscardRandomCard(V("DEEP_027", "discard", 2)) }));
			cards.Add("DEEP_027b", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (c.BaseMana > 0) c.BaseMana -= 1; })
			}));
			cards.Add("DEEP_027t", new CardDef(new Power { PowerTask = Nothing }));
			// [TTN_039] Watcher of the Sun (2) — Battlecry: Get a random Holy spell. Forge: Also restore 6 Health to your hero.
			ISimpleTask HolySpell() => new CustomTask((g, c, s, t, stack) =>
			{
				var pool = Cards.GenerationPool(g, c).Where(x => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == HOLY).ToList();
				if (pool.Count > 0) AddToHand(c, pool[g.Random.Next(pool.Count)]);
			});
			cards.Add("TTN_039", new CardDef(new Power { PowerTask = HolySpell() }));
			cards.Add("TTN_039t", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(HolySpell(), new HealTask(V("TTN_039t", "heal", 6), EntityType.HERO))
			}));
			// [TTN_042] Cyclopian Crusher (3) — Rush. Forge: Gain +3/+2 (the Forged card is a 6/5).
			cards.Add("TTN_042", new CardDef(new Power { PowerTask = Nothing }));
			cards.Add("TTN_042t", new CardDef(new Power { PowerTask = Nothing }));
			// [TTN_302] Titanforged Traps (2) — Discover and cast a Secret. Forge: Do it twice.
			ISimpleTask DiscoverCastSecret() => new DiscoverFromTask((g, c, s) => Cards.GenerationPool(g, c)
				.Where(x => x.IsSecret && x.HasClass(c.HeroClass) && !c.SecretZone.Any(p => p.Card.Id == x.Id)), action: ChoiceAction.CAST);
			cards.Add("TTN_302", new CardDef(new Power { PowerTask = DiscoverCastSecret() }));
			cards.Add("TTN_302t", new CardDef(new Power { PowerTask = ComplexTask.Create(DiscoverCastSecret(), DiscoverCastSecret()) }));
			// [TTN_430] Creation Protocol (2) — Discover a copy of a minion in your deck. Forge: Get another copy.
			IEnumerable<Card> DeckMinions(Game g, Controller c, IEntity s) => c.DeckZone.Where(p => Generic.IsMinionCard(p)).Select(p => p.Card);
			cards.Add("TTN_430", new CardDef(new Power { PowerTask = new DiscoverFromTask(DeckMinions) }));
			cards.Add("TTN_430t", new CardDef(new Power
			{
				PowerTask = new DiscoverFromTask(DeckMinions, afterPick: new CustomTask((g, c, s, t, stack) => { if (t != null) AddToHand(c, t.Card); }))
			}));
			// [TTN_457] Eulogizer (3) — Battlecry: Spend 3 Corpses to deal 3 damage. Forge: Gain them instead.
			cards.Add("TTN_457", new CardDef(BattlecryAny, new Power
			{
				CorpseCost = V("TTN_457", "corpses", 3), // spent on play (glows with enough Corpses)
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => stack.Flag = Generic.SpendCorpses(c, s)),
					new FlagTask(true, new DamageTask(V("TTN_457", "damage", 3), EntityType.TARGET)))
			}));
			cards.Add("TTN_457t", new CardDef(BattlecryAny, new Power
			{
				PowerTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) => Generic.GainCorpses(c, s, V("TTN_457", "corpses", 3))),
					new DamageTask(V("TTN_457", "damage", 3), EntityType.TARGET))
			}));
			// [TTN_458] XB-488 Disposalbot (5) — Battlecry: Deal 5 damage randomly split among all enemy minions. Forge: Gain Lifesteal.
			ISimpleTask Split(string card, int n) => new EnqueueTask(V(card, "damage", n), ComplexTask.DamageRandomTargets(1, EntityType.OP_MINIONS, 1));
			cards.Add("TTN_458", new CardDef(new Power { PowerTask = Split("TTN_458", 5) }));
			cards.Add("TTN_458t", new CardDef(new Power { PowerTask = Split("TTN_458", 5) }));
			// [TTN_477] Molten Rune (3) — Deal 3 damage. Get a random spell. Forge: This casts twice.
			ISimpleTask RandomSpell() => new CustomTask((g, c, s, t, stack) =>
			{
				var pool = ClassCards(c, CardType.SPELL).ToList();
				if (pool.Count > 0) AddToHand(c, pool[g.Random.Next(pool.Count)]);
			});
			cards.Add("TTN_477", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("TTN_477", "damage", 3), EntityType.TARGET, true), RandomSpell())
			}));
			cards.Add("TTN_477t1", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("TTN_477", "damage", 3), EntityType.TARGET, true), RandomSpell(),
					new DamageTask(V("TTN_477", "damage", 3), EntityType.TARGET, true), RandomSpell())
			}));
			// [TTN_490] Disciple of Sargeras (4) — Battlecry: Discard a spell to summon two 3/2 Imps. Forge: Give them +2 Health and
			// Taunt. (Discard: the player picks the spell — hand target; the Imps: Felblaze Imp, its related card.)
			ISimpleTask DiscardForImps(bool forged) => new CustomTask((g, c, s, t, stack) =>
			{
				if (!(t is Spell spell) || spell.Zone?.Type != Zone.HAND || spell.Controller != c) return;
				new DiscardTask(EntityType.TARGET).Process(g, c, s, spell);
				Generic.BeginSummonGroup(g, s, V("TTN_490", "imps", 2));
				for (int i = 0; i < V("TTN_490", "imps", 2) && !c.BoardZone.IsFull; i++)
				{
					var imp = (Minion)Entity.FromCard(c, Cards.FromId("TTN_960t6"));
					Generic.SummonBlock.Invoke(g, imp, -1, s);
					if (forged && imp.Zone?.Type == Zone.PLAY)
					{
						imp.BaseHealth += V("TTN_490t", "health", 2);
						imp.HasTaunt = true;
					}
				}
			});
			cards.Add("TTN_490", new CardDef(new Power { HandTarget = SelfCondition.IsSpell, PowerTask = DiscardForImps(false) }));
			cards.Add("TTN_490t", new CardDef(new Power { HandTarget = SelfCondition.IsSpell, PowerTask = DiscardForImps(true) }));
			// [TTN_724] Storm Giant (8) — Taunt. Forge: Costs (2) less. Can be Forged endlessly. (TTN_724t forges into itself:
			// each further Forge adds Storm's Fury, -2 Cost.)
			cards.Add("TTN_724", new CardDef(new Power { PowerTask = Nothing }));
			cards.Add("TTN_724t", new CardDef(new Power
			{
				PowerTask = Nothing,
				Trigger = new Trigger(TriggerType.FORGE)
				{
					TriggerActivation = TriggerActivation.HAND,
					TriggerSource = TriggerSource.SELF,
					Condition = new SelfCondition(p => p[GameTag.TRANSFORMED_FROM_CARD] == p.Card.AssetId),
					SingleTask = new AddEnchantmentTask("TTN_724e", EntityType.SOURCE)
				}
			}));
			cards.Add("TTN_724e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("TTN_724", "discount", 2))) }));
			// [TTN_729] Melted Maker (3) — After you Forge a card, get a copy of it.
			cards.Add("TTN_729", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.FORGE)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (t != null) AddToHand(c, t.Card); })
				}
			}));
			// [TTN_730] Lab Constructor (4) — At the end of your turn, summon a copy of this. Forge: Gain Magnetic.
			Trigger CopyAtEndOfTurn() => new Trigger(TriggerType.TURN_END) { SingleTask = new SummonCopyTask(EntityType.SOURCE) };
			cards.Add("TTN_730", new CardDef(new Power { Trigger = CopyAtEndOfTurn() }));
			cards.Add("TTN_730t", new CardDef(new Power { PowerTask = new MagneticTask(), Trigger = CopyAtEndOfTurn() }));
			cards.Add("TTN_730e2", new CardDef(new Power { Trigger = CopyAtEndOfTurn() }));
			// [TTN_753] Bellowing Flames (3) — Deal 5 damage to a minion. Forge: Then deal 5 damage split among all enemy minions.
			cards.Add("TTN_753", new CardDef(TargetMinion, new Power { PowerTask = new DamageTask(V("TTN_753", "damage", 5), EntityType.TARGET, true) }));
			cards.Add("TTN_753t", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("TTN_753", "damage", 5), EntityType.TARGET, true),
					new EnqueueTask(V("TTN_753t", "split", 5), ComplexTask.DamageRandomTargets(1, EntityType.OP_MINIONS, 1, true)))
			}));
			// [TTN_801] Champion of Storms (4) — After you cast a Nature spell, summon a 4/2 Elemental. Forge: With Rush.
			Trigger AfterNature(string elemental) => new Trigger(TriggerType.AFTER_CAST)
			{
				TriggerSource = TriggerSource.FRIENDLY,
				Condition = IsSchool(NATURE),
				SingleTask = new SummonTask(elemental, SummonSide.RIGHT)
			};
			cards.Add("TTN_801", new CardDef(new Power { Trigger = AfterNature("TTN_801t1") }));
			cards.Add("TTN_801t", new CardDef(new Power { Trigger = AfterNature("TTN_801t2") }));
			cards.Add("TTN_801t1", new CardDef(new Power { PowerTask = Nothing }));
			cards.Add("TTN_801t2", new CardDef(new Power { PowerTask = Nothing }));
			// [TTN_855t] Tyr's Tears (5) — Resurrect 3 different minions from your class. Set their stats to 2/2. Forge: Resurrect
			// another. (The Forged card is TTN_855.)
			ISimpleTask TyrsTears(int n) => new CustomTask((g, c, s, t, stack) =>
			{
				var pool = c.DiedThisGameCards.Where(p => p is Minion && p.Card.HasClass(c.HeroClass))
					.Select(p => p.Card).GroupBy(x => x.Id).Select(x => x.First()).ToList();
				foreach (Card card in DiscoverFromTask.PickDistinct(g, pool, n))
				{
					if (c.BoardZone.IsFull) return;
					var risen = (Minion)Entity.FromCard(c, card);
					Generic.SummonBlock.Invoke(g, risen, -1, s);
					if (risen.Zone?.Type == Zone.PLAY) Give(g, s, risen, "TTN_855e1");
				}
			});
			cards.Add("TTN_855t", new CardDef(new Power { PowerTask = TyrsTears(V("TTN_855t", "minions", 3)) }));
			cards.Add("TTN_855", new CardDef(new Power { PowerTask = TyrsTears(V("TTN_855", "minions", 4)) }));
			cards.Add("TTN_855e1", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("TTN_855t", "stats", 2))) }));
			// [TTN_865] Weight of the World (3) — Draw 2 cards. Forge: Draw cards until you have as many in hand as your opponent first.
			cards.Add("TTN_865", new CardDef(new Power { PowerTask = ComplexTask.Repeat(new DrawTask(), V("TTN_865", "draw", 2)) }));
			cards.Add("TTN_865t", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) =>
				{
					while (c.HandZone.Count < c.Opponent.HandZone.Count && !c.DeckZone.IsEmpty && !c.HandZone.IsFull)
						if (Generic.Draw(c) == null) break;
				}), ComplexTask.Repeat(new DrawTask(), V("TTN_865", "draw", 2)))
			}));
			// [TTN_951] Embrace of Nature (2) — Draw a Choose One card. Forge: It has both effects combined.
			cards.Add("TTN_951", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p.ChooseOne)) }));
			cards.Add("TTN_951t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var choose = c.DeckZone.Where(p => p.ChooseOne).ToList();
					if (choose.Count == 0) return;
					IPlayable drawn = Generic.Draw(c, choose[g.Random.Next(choose.Count)]);
					if (drawn != null) drawn[GameTag.CHOOSE_BOTH] = 1; // both effects (Playable.ActivateTask)
				})
			}));
			// [YOG_502] Sanitize (4) — Deal damage equal to your Armor to all minions. Forge: Gain 3 Armor first.
			ISimpleTask ArmorDamage() => new CustomTask((g, c, s, t, stack) =>
				new DamageTask(c.Hero.Armor, EntityType.ALLMINIONS, true).Process(g, c, s, null));
			cards.Add("YOG_502", new CardDef(new Power { PowerTask = ArmorDamage() }));
			cards.Add("YOG_502t", new CardDef(new Power { PowerTask = ComplexTask.Create(new ArmorTask(V("YOG_502t", "armor", 3)), ArmorDamage()) }));
			// [YOG_525] Muscle-o-Tron (3) — Battlecry: Give all minions in your hand +1/+1. Forge: +2/+2 instead.
			ISimpleTask BuffHandMinions(string enchantment) => new CustomTask((g, c, s, t, stack) =>
			{
				foreach (IPlayable p in c.HandZone.Where(p => Generic.IsMinionCard(p)).ToList()) Give(g, s, p, enchantment);
			});
			cards.Add("YOG_525", new CardDef(new Power { PowerTask = BuffHandMinions("YOG_525e") }));
			cards.Add("YOG_525t", new CardDef(new Power { PowerTask = BuffHandMinions("YOG_525e1") }));
			cards.Add("YOG_525e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("YOG_525", "buff", 1))) }));
			cards.Add("YOG_525e1", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("YOG_525t", "buff", 2))) }));
		}

		/// <summary>"If you've Excavated twice, …": the check on the card itself and the branch (the hand glow finds it).</summary>
		private static ISimpleTask IfExcavatedTwice(ISimpleTask then) => ComplexTask.If((g, c, s) => SelfCondition.ExcavatedTwice.Eval(s), then);

		// The 8 Bonus Effects (wiki Bonus effect: "Taunt, Windfury, Divine Shield, Poisonous, Elusive, Rush, Lifesteal, or Reborn").
		// (the Bonus Effects: Generic.BonusEffectKeywords, Actions/WildWest.cs)

		// ------------------------------------------------------------------ Excavate cards (client tag 3114)
		private static void ExcavateCards(IDictionary<string, CardDef> cards)
		{
			static ISimpleTask Dig() => new ExcavateTask();

			// [DEEP_009] Digging Straight Down (4) — Deal 8 damage to a minion. Excavate a treasure.
			cards.Add("DEEP_009", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("DEEP_009", "damage", 8), EntityType.TARGET, true), Dig())
			}));
			// [DEEP_018] Shroomscavate (2) — Give a minion Divine Shield. Excavate a treasure.
			cards.Add("DEEP_018", new CardDef(TargetMinion, new Power { PowerTask = ComplexTask.Create(ComplexTask.DivineShield(EntityType.TARGET), Dig()) }));
			// [DEEP_033] Fossilized Kaleidosaur (4) — Battlecry: Gain two random Bonus Effects. Excavate a treasure.
			cards.Add("DEEP_033", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) => GainBonusEffects(g, s as Minion, V("DEEP_033", "effects", 2))), Dig())
			}));
			// [DEEP_007] Sir Finley, the Intrepid (3) — Battlecry: If you've Excavated twice, transform all enemy minions into
			// 1/1 Murlocs (Murloc Tinyfin: the card's related card, wiki).
			cards.Add("DEEP_007", new CardDef(new Power
			{
				PowerTask = IfExcavatedTwice(new CustomTask((g, c, s, t, stack) =>
				{
					Card murloc = Cards.FromId("CORE_LOEA10_3");
					foreach (Minion m in c.Opponent.BoardZone.GetAll().Where(m => !(m is Location)).ToList())
						Generic.TransformBlock.Invoke(c.Opponent, murloc, m);
				}))
			}));
			// [WW_001] Kobold Miner (1) — Battlecry: Excavate a treasure.
			cards.Add("WW_001", new CardDef(new Power { PowerTask = Dig() }));
			// [WW_002] Burrow Buster (3) — Rush. Battlecry: Excavate a treasure.
			cards.Add("WW_002", new CardDef(new Power { PowerTask = Dig() }));
			// [WW_003] Bounty Board (3) — Your Excavate, Quickdraw, Tradeable, and Legendary cards cost (1) less.
			cards.Add("WW_003", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("WW_003", "discount", 1)))
				{
					Condition = new SelfCondition(p => p.Card[Generic.EXCAVATE] > 0 || p.Card[HsTag.QUICKDRAW] > 0 || p.Card[Generic.TRADEABLE] > 0 ||
					                                   p.Card.Rarity == Rarity.LEGENDARY),
					ShowAs = "WW_003e" // HsServer: shown like Sorcerer's Apprentice (user 2026-10-04)
				}
			}));
			// [WW_009] Cryopreservation (1) — Freeze an enemy. Excavate a treasure.
			cards.Add("WW_009", new CardDef(TargetEnemy, new Power { PowerTask = ComplexTask.Create(ComplexTask.Freeze(EntityType.TARGET), Dig()) }));
			// [WW_322] Skeleton Crew (4) — Battlecry: Excavate a treasure. It costs (0).
			cards.Add("WW_322", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (Generic.Excavate(c, (IPlayable)s) is { } treasure) treasure[GameTag.COST] = 0;
				})
			}));
			// [WW_324] Pile of Bones (2) — Deathrattle: The next time you Excavate, resummon this.
			cards.Add("WW_324", new CardDef(new Power { DeathrattleTask = new AddEnchantmentTask("WW_324e", EntityType.CONTROLLER) }));
			cards.Add("WW_324e", new CardDef(new Power
			{
				// "After you excavate, resummon one Pile of Bones."
				Trigger = new Trigger(TriggerType.EXCAVATE)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = ComplexTask.Create(new SummonTask("WW_324", SummonSide.DEFAULT), RemoveEnchantmentTask.Task)
				}
			}));
			// [WW_349] Badlands Brawler (7) — Battlecry: Start a Brawl! If you've Excavated twice, this always wins.
			cards.Add("WW_349", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var all = c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => !(m is Location) && !m.IsDead).ToList();
					if (all.Count < 2) return;
					// (and a minion that "always wins Brawls": Dark Iron Bouncer, Generic.BrawlSurvivor)
					Minion survivor = Generic.BrawlSurvivor(g, all, c[Generic.EXCAVATES_THIS_GAME] >= 2 && s is Minion me && all.Contains(me) ? me : null);
					foreach (Minion m in all)
						if (m != survivor) m.Destroy();
				})
			}));
			// [WW_352] Reap What You Sow (3) — Deal 3 damage. Excavate a treasure.
			cards.Add("WW_352", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("WW_352", "damage", 3), EntityType.TARGET, true), Dig())
			}));
			// [WW_356] Harrowing Ox (7) — Taunt. Battlecry: If you've Excavated twice, your next card this turn costs (7) less.
			cards.Add("WW_356", new CardDef(new Power { PowerTask = IfExcavatedTwice(new AddEnchantmentTask("WW_356e", EntityType.CONTROLLER)) }));
			cards.Add("WW_356e", NextCardEffect(Effects.ReduceCost(V("WW_356", "discount", 7)), new SelfCondition(p => true), thisTurnOnly: true));
			// [WW_375] Slagmaw the Slumbering (4) — Rush, Taunt. Dormant for 6 turns. (Excavate to awaken 2 turns sooner!)
			// (both from its text: Card.DormantTurns, Card.ExcavateAwakensSooner in Generic.Excavate)
			cards.Add("WW_375", new CardDef(new Power { PowerTask = Nothing }));
			// [WW_378] Smokestack (1) — Deal 1 damage to a minion. If it dies, Excavate a treasure.
			cards.Add("WW_378", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("WW_378", "damage", 1), EntityType.TARGET, true),
					new CustomTask((g, c, s, t, stack) => stack.Flag = t is Minion m && (m.IsDead || m.ToBeDestroyed)),
					new FlagTask(true, Dig()))
			}));
			// [WW_380] Blast Charge (2) — Destroy a damaged enemy minion. Excavate a treasure.
			cards.Add("WW_380", new CardDef(new Dictionary<PlayReq, int>
				{
					{ PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_DAMAGED_TARGET, 0 }
				},
				new Power { PowerTask = ComplexTask.Create(new DestroyTask(EntityType.TARGET), Dig()) }));
			// [WW_412] Bloodrock Co. Shovel (weapon) — Deathrattle: Excavate a treasure.
			cards.Add("WW_412", new CardDef(new Power { DeathrattleTask = Dig() }));
			// [WW_413] Antique Flinger (3) — Battlecry: If you've Excavated twice, destroy an enemy minion.
			cards.Add("WW_413", new CardDef(BattlecryEnemyMinion, new Power { PowerTask = IfExcavatedTwice(new DestroyTask(EntityType.TARGET)) }));
			// [WW_417] Drilly the Kid (4) — Battlecry, Quickdraw, and Deathrattle: Excavate a treasure.
			cards.Add("WW_417", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(Dig(), new ConditionTask(EntityType.SOURCE, SelfCondition.IsQuickdraw), new FlagTask(true, Dig())),
				DeathrattleTask = Dig()
			}));
			// [WW_426] Blastmage Miner (6) — Battlecry: Excavate a treasure. For each card in your hand, deal 1 damage to a random enemy.
			cards.Add("WW_426", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(Dig(), new CustomTask((g, c, s, t, stack) =>
				{
					int n = c.HandZone.Count;
					for (int i = 0; i < n; i++)
						ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, V("WW_426", "damage", 1)).Process(g, c, s, null);
				}))
			}));
			// [WW_432] Reliquary Researcher (4) — Battlecry: If you've Excavated twice, cast two random Mage Secrets.
			cards.Add("WW_432", new CardDef(new Power
			{
				PowerTask = IfExcavatedTwice(new CustomTask((g, c, s, t, stack) =>
					Generic.CastRandomSecrets(c, s, x => x.HasClass(CardClass.MAGE), V("WW_432", "secrets", 2))))
			}));
			// [WW_437] Tram Conductor Gerry (7) — Battlecry: If you've Excavated twice, summon six 3/3 Tram Cars with Rush.
			cards.Add("WW_437", new CardDef(new Power
			{
				PowerTask = IfExcavatedTwice(ComplexTask.Repeat(new SummonTask("WW_437t", SummonSide.RIGHT), V("WW_437", "cars", 6)))
			}));
			cards.Add("WW_437t", new CardDef(new Power { PowerTask = Nothing }));
			// [WW_442] Mo'arg Drillfist (4) — Taunt. Deathrattle: Excavate a treasure.
			cards.Add("WW_442", new CardDef(new Power { DeathrattleTask = Dig() }));
		}

		// ------------------------------------------------------------------ Excavate treasures (client tag 2823)
		private static void Treasures(IDictionary<string, CardDef> cards)
		{
			// --- common
			// [WW_001t] Rock (1) — Deal 3 damage.
			cards.Add("WW_001t", new CardDef(TargetAny, new Power { PowerTask = new DamageTask(V("WW_001t", "damage", 3), EntityType.TARGET, true) }));
			// [WW_001t2] Water Source (1) — Restore 3 Health. Draw a card.
			cards.Add("WW_001t2", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new HealTask(V("WW_001t2", "heal", 3), EntityType.TARGET), new DrawTask())
			}));
			// [WW_001t3] Fool's Azerite (1) — Discover a 2-Cost card. It costs (0).
			cards.Add("WW_001t3", new CardDef(new Power { PowerTask = DiscoverCostCardFree("WW_001t3", 2, "WW_001t14e") }));
			// [WW_001t4] Escaping Trogg (1) — Rush.
			cards.Add("WW_001t4", new CardDef(new Power { PowerTask = Nothing }));
			// [WW_001t18] Pouch of Coins (1) — Get two Coins.
			cards.Add("WW_001t18", new CardDef(new Power { PowerTask = new AddCardTo("GAME_005", EntityType.HAND, V("WW_001t18", "coins", 2)) }));
			// [DEEP_999t1] Heartblossom (1) — Give a friendly minion +2/+2. Deal 2 damage to a random enemy minion.
			cards.Add("DEEP_999t1", new CardDef(TargetFriendlyMinion, new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("DEEP_999t1e", EntityType.TARGET),
					ComplexTask.DamageRandomTargets(1, EntityType.OP_MINIONS, V("DEEP_999t1", "damage", 2), true))
			}));
			cards.Add("DEEP_999t1e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DEEP_999t1", "buff", 2))) }));

			// --- rare
			// [WW_001t5] Falling Stalactite (2) — Deal 3 damage to a minion and the enemy hero.
			cards.Add("WW_001t5", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("WW_001t5", "damage", 3), EntityType.TARGET, true),
					new DamageTask(V("WW_001t5", "damage", 3), EntityType.OP_HERO, true))
			}));
			// [WW_001t7] Canary (2) — Battlecry: Return an enemy minion to its owner's hand.
			cards.Add("WW_001t7", new CardDef(BattlecryEnemyMinion, new Power { PowerTask = new ReturnHandTask(EntityType.TARGET) }));
			// [WW_001t8] Glowing Glyph (2) — Give a minion and its neighbors +1/+2.
			cards.Add("WW_001t8", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					var hit = new List<Minion> { m };
					hit.AddRange(Neighbors(m));
					foreach (Minion x in hit) Give(g, s, x, "WW_001t8e");
				})
			}));
			cards.Add("WW_001t8e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("WW_001t8", "attack", 1)), Effects.Health_N(V("WW_001t8", "health", 2)))
			}));
			// [WW_001t9] Azerite Chunk (2) — Discover a 3-Cost card. It costs (0).
			cards.Add("WW_001t9", new CardDef(new Power { PowerTask = DiscoverCostCardFree("WW_001t9", 3, "WW_001t14e") }));
			// [WW_001t16] Living Stone (2) — Taunt. Deathrattle: Summon a random 2-Cost minion.
			cards.Add("WW_001t16", new CardDef(new Power { DeathrattleTask = ComplexTask.SummonRandomMinion(GameTag.COST, V("WW_001t16", "cost", 2)) }));
			// [DEEP_999t2] Deepholm Geode (2) — At the end of your turn, deal 2 damage to all enemies.
			cards.Add("DEEP_999t2", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new DamageTask(V("DEEP_999t2", "damage", 2), EntityType.ENEMIES) }
			}));

			// --- epic
			// [WW_001t11] Ogrefist Boulder (3, Location) — Set a minion's stats to 6/7.
			cards.Add("WW_001t11", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("WW_001t11e", EntityType.TARGET) }));
			cards.Add("WW_001t11e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.SetAttack(V("WW_001t11", "attack", 6)), Effects.SetMaxHealth(V("WW_001t11", "health", 7)))
			}));
			// [WW_001t12] Collapse! (3) — Deal 3 damage to all enemies.
			cards.Add("WW_001t12", new CardDef(new Power { PowerTask = new DamageTask(V("WW_001t12", "damage", 3), EntityType.ENEMIES, true) }));
			// [WW_001t13] Steelhide Mole (3) — Taunt, Reborn, Elusive.
			cards.Add("WW_001t13", new CardDef(new Power { PowerTask = Nothing }));
			// [WW_001t14] Azerite Gem (3) — Discover a 5-Cost card. It costs (0).
			cards.Add("WW_001t14", new CardDef(new Power { PowerTask = DiscoverCostCardFree("WW_001t14", 5, "WW_001t14e") }));
			cards.Add("WW_001t14e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) }));
			// [WW_001t17] Motherlode Drake (3) — Rush, Divine Shield, Lifesteal.
			cards.Add("WW_001t17", new CardDef(new Power { PowerTask = Nothing }));
			// [DEEP_999t3] World Pillar Fragment (3) — Discover an Elemental to summon. Add the others to your hand.
			// (the shared WildWestDiscoverToSummonOthersToHand: Maruut Stonebinder does the same — wiki Maruut trivia)
			cards.Add("DEEP_999t3", new CardDef(new Power
			{
				PowerTask = WildWestDiscoverToSummonOthersToHand(x => x.Type == CardType.MINION && x.IsRace(Race.ELEMENTAL))
			}));

			// --- legendary (the class's fourth Excavate)
			// [WW_001t23] The Azerite Scorpion (4) — Battlecry: Get 4 random spells. They cost (1) less for each time you've
			// Excavated this game (but not less than 1).
			cards.Add("WW_001t23", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var pool = ClassCards(c, CardType.SPELL).ToList();
					for (int i = 0; i < V("WW_001t23", "spells", 4) && pool.Count > 0 && !c.HandZone.IsFull; i++)
					{
						IPlayable spell = Entity.FromCard(c, pool[g.Random.Next(pool.Count)], new EntityData { { GameTag.CREATOR, s.Id } });
						if (Generic.AddHandPhase.Invoke(c, spell))
							Generic.AddEnchantmentBlock(g, Cards.FromId("WW_001t23e"), (IPlayable)s, spell, c[Generic.EXCAVATES_THIS_GAME], 0, 0);
					}
				})
			}));
			cards.Add("WW_001t23e", new CardDef(new Power
			{
				// (1) less per Excavate counted when the spell was made (num1), never below 1
				Enchant = new Enchant(GameTag.COST, EffectOperator.SUB_NOT_BELOW_1),
			}));
			// [WW_001t24] The Azerite Hawk (4) — Battlecry: Get a random Titan. It costs (1).
			cards.Add("WW_001t24", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					// the card names Titans: its own pool, past the "never generated" rule the random pools follow
					var titans = Cards.FormatTypeCollectible(g.FormatType).Where(x => x[(GameTag)2772] > 0).ToList(); // TITAN
					if (titans.Count == 0 || c.HandZone.IsFull) return;
					IPlayable titan = Entity.FromCard(c, titans[g.Random.Next(titans.Count)], new EntityData { { GameTag.CREATOR, s.Id } });
					if (Generic.AddHandPhase.Invoke(c, titan))
						titan[GameTag.COST] = V("WW_001t24", "cost", 1);
				})
			}));
			// [WW_001t25] The Azerite Snake (4) — Battlecry: Your hero steals 10 Health from the enemy hero.
			cards.Add("WW_001t25", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("WW_001t25e", EntityType.OP_HERO), new AddEnchantmentTask("WW_001t25e2", EntityType.HERO))
			}));
			cards.Add("WW_001t25e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(-V("WW_001t25", "health", 10))) }));
			cards.Add("WW_001t25e2", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("WW_001t25", "health", 10))) }));
			// [WW_001t26] The Azerite Rat (4) — Battlecry: Resurrect your highest Cost minion. Give it +2/+2, Reborn, and Lifesteal.
			cards.Add("WW_001t26", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var dead = c.DiedThisGameCards.Where(p => p is Minion && p.Id != s.Id).ToList();
					if (dead.Count == 0 || c.BoardZone.IsFull) return;
					int max = dead.Max(p => p.Card.Cost);
					var highest = dead.Where(p => p.Card.Cost == max).ToList();
					var risen = (Minion)Entity.FromCard(c, highest[g.Random.Next(highest.Count)].Card);
					Generic.SummonBlock.Invoke(g, risen, -1, s);
					if (risen.Zone?.Type == Zone.PLAY)
						Give(g, s, risen, "WW_001t26e");
				})
			}));
			cards.Add("WW_001t26e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("WW_001t26", "buff", 2)), Effects.Health_N(V("WW_001t26", "buff", 2)),
					new Effect(GameTag.REBORN, EffectOperator.SET, 1), Effects.Lifesteal)
			}));
			// [WW_001t27] The Azerite Ox (4) — Battlecry: Discover two 8-Cost minions. Summon them.
			cards.Add("WW_001t27", new CardDef(new Power
			{
				PowerTask = ComplexTask.Repeat(new DiscoverTask(CardType.MINION, CardClass.INVALID,
					(GameTag.COST, RelaSign.EQ, V("WW_001t27", "cost", 8)), ChoiceAction.SUMMON), V("WW_001t27", "minions", 2))
			}));
			// [DEEP_999t4] The Azerite Dragon (4) — Battlecry: Give all other minions in your hand, deck, and battlefield +3/+3.
			cards.Add("DEEP_999t4", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in c.HandZone.Concat(c.DeckZone).Concat(c.BoardZone).Where(p => Generic.IsMinionCard(p) && p.Id != s.Id).ToList())
						Give(g, s, p, "DEEP_999t4e");
				})
			}));
			cards.Add("DEEP_999t4e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DEEP_999t4", "buff", 3))) }));
			// [DEEP_999t5] The Azerite Murloc (4) — Battlecry: Transform ALL your other minions into ones that cost (3) more
			// (keeping their original Costs).
			cards.Add("DEEP_999t5", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int more = V("DEEP_999t5", "cost", 3);
					foreach (Minion m in c.BoardZone.GetAll().Where(m => m.Id != s.Id).ToList())
					{
						int cost = m.Card.Cost;
						if (!(Cards.EvolvePick(g.FormatType, m, more, g.Random) is Card into)) continue; // the shared evolve rules
						if (Generic.TransformBlock.Invoke(c, into, m) && c.Game.IdEntityDic.Values.LastOrDefault(e => e is Minion x && x.Card == into && x.Zone == c.BoardZone) is Minion now)
							Generic.AddEnchantmentBlock(g, Cards.FromId("DEEP_999t5e"), (IPlayable)s, now, cost, 0, 0);
					}
				})
			}));
			cards.Add("DEEP_999t5e", new CardDef(new Power
			{
				// keeps the original Cost (its ScriptTag num1)
				Enchant = new Enchant(GameTag.COST, EffectOperator.SET)
			}));
		}
	}
}
