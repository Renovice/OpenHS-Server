// HsServer addition (not upstream SabberStone): The Lost City of Un'Goro — chunk 2: Druid, Hunter and Mage (spec: research
// spec_chunk2). Shared pieces: Generic.Kindred (Actions/LostCity.cs), the Discover counters and last options (Actions/Choose.cs
// NoteDiscovered), the "changes each turn" versions (Actions/Festival.cs RemixBaseOf: Titanographer Osk), "1-Cost spells cast
// twice" (Actions/Paradise.cs), Generic.PlayByEffect for prepared cards (The Origin Stone). Every number goes through CardValues (V).
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
		/// <summary>An Objective "At the start of your next turn, …" (Ravenous Flock): it waits in the Secret zone (CastSpell: duration
		/// spells), counts QUEST_PROGRESS at the start of its owner's turns and at its total runs <paramref name="effect"/> and leaves
		/// (the Rhythm and Roots pattern).</summary>
		private static Power StartOfTurnObjective(Action<Game, Controller, Spell> effect) => new Power
		{
			Trigger = new Trigger(TriggerType.TURN_START)
			{
				SingleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Spell objective) || objective.Zone?.Type != Zone.SECRET) return;
					if (!Generic.ObjectiveTick(objective, objective.QuestTotalProgress)) return; // (2026-10-08: the shared tick / expiry)
					effect(g, c, objective);
					Generic.ObjectiveExpires(c, objective);
				})
			}
		};

		/// <summary>A Titan ability played as a Battlecry (Titanographer Osk's versions): its effect and its targeting, a target
		/// "if available".</summary>
		private static CardDef AsBattlecry(CardDef ability)
		{
			var reqs = new Dictionary<PlayReq, int>();
			foreach (var kv in ability?.PlayReqs ?? new Dictionary<PlayReq, int>())
				reqs[kv.Key == PlayReq.REQ_TARGET_TO_PLAY ? PlayReq.REQ_TARGET_IF_AVAILABLE : kv.Key] = kv.Value;
			var power = new Power { PowerTask = ability?.Power?.PowerTask, OwnFx = ability?.Power?.OwnFx ?? false };
			return reqs.Count > 0 ? new CardDef(reqs, power) : new CardDef(power);
		}

		private static void LostCityChunk2(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Druid
			// [DINO_130] Longneck Egg (2) — Deathrattle: Summon a 3/3 Beast. Give your minions +1/+1.
			cards.Add("DINO_130", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					SummonGet(g, c, Cards.FromId("DINO_130t"), s);
					GiveAll(g, s, c.BoardZone.GetAll().Where(m => !(m is Location) && !m.IsDead).ToList(), "DINO_130e");
				})
			}));
			cards.Add("DINO_130e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DINO_130", "stats", 1))) }));
			// [DINO_421] Seismopod (8) — Taunt, Elusive. Deathrattle: Give all minions in your hand and deck +3/+3.
			cards.Add("DINO_421", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.HandZone.GetAll().Concat(c.DeckZone.GetAll()).Where(p => Generic.IsMinionCard(p)).ToList(), "DINO_421e"))
			}));
			cards.Add("DINO_421e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DINO_421", "stats", 3))) }));
			// [DINO_432] Panther Mask (4) — Set a minion's stats to 5/4 and give it Stealth. Draw 2 cards.
			cards.Add("DINO_432", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("DINO_432e", EntityType.TARGET),
					new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("DINO_432", "draws", 2); i++) Generic.Draw(c); }))
			}));
			cards.Add("DINO_432e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.SetAttack(V("DINO_432", "attack", 5)), Effects.SetMaxHealth(V("DINO_432", "health", 4)), Effects.StealthEff)
			}));
			// [TLC_230] TREEEES!!! (5) — Choose a minion. Summon four 2/2 Treants that attack it. (BEEEES!!!: all summoned, then each
			// attacks it while it lives)
			cards.Add("TLC_230", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion target)) return;
					var treants = new List<Minion>();
					for (int i = 0; i < V("TLC_230", "treants", 4); i++)
						if (SummonGet(g, c, Cards.FromId("TLC_230t"), s) is Minion m) treants.Add(m);
					foreach (Minion treant in treants)
					{
						if (target.IsDead || target.ToBeDestroyed || target.Zone?.Type != Zone.PLAY) break;
						if (treant.IsDead || treant.ToBeDestroyed || treant.Zone?.Type != Zone.PLAY) continue;
						EffectAttack(g, treant, target);
						g.DeathProcessingAndAuraUpdate();
					}
				})
			}));
			// [TLC_231] Story of Barnabus (2) — Draw a minion. If it has 5 or more Attack, give it +5 Health and gain 5 Armor.
			cards.Add("TLC_231", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(DrawOne(g, c, p => p is Minion) is Minion drawn) || drawn.AttackDamage < V("TLC_231", "attack", 5)) return;
					Give(g, s, drawn, "TLC_231e");
					new ArmorTask(V("TLC_231", "armor", 5)).Process(g, c, s, null);
				})
			}));
			cards.Add("TLC_231e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("TLC_231", "health", 5))) }));
			// [TLC_232] Ravenous Flock (2) — At the start of your next turn, summon three 2/1 Hatchlings. (an Objective in the Secret zone)
			cards.Add("TLC_232", new CardDef(StartOfTurnObjective((g, c, o) =>
			{
				for (int i = 0; i < V("TLC_232", "hatchlings", 3); i++) SummonGet(g, c, Cards.FromId("TLC_237t"), o);
			})));
			// [TLC_233] Hatchery Helper (3) — Taunt. Battlecry: Give your other minions with 2 or less Attack +1/+1 and Taunt.
			cards.Add("TLC_233", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.BoardZone.GetAll().Where(m => m.Id != s.Id && !(m is Location) && m.AttackDamage <= V("TLC_233", "attack", 2)).ToList(), "TLC_233e"))
			}));
			cards.Add("TLC_233e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TLC_233", "stats", 1)), Effects.Health_N(V("TLC_233", "stats", 1)), Effects.TauntEff) }));
			// [TLC_234] Eternal Bloodpetal (4) — Deathrattle: Summon a 0/1 Eternal Seedling. / [TLC_234t] Eternal Seedling — Deathrattle:
			// Summon a 5/1 Eternal Bloodpetal. (each other's DISPLAY_CARD_ON_MOUSEOVER / RelatedCards)
			cards.Add("TLC_234", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => SummonGet(g, c, Cards.FromId("TLC_234t"), s)) }));
			cards.Add("TLC_234t", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => SummonGet(g, c, Cards.FromId("TLC_234"), s)) }));
			// [TLC_235] Life Cycle (1) — Destroy a minion. Summon a random minion of the same Cost to replace it. (for its owner, in its slot;
			// wiki: "resolving the minion's on-death effects")
			cards.Add("TLC_235", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					int cost = m.Card.Cost, slot = m.ZonePosition;
					Controller owner = m.Controller;
					s[GameTag.TAG_SCRIPT_DATA_NUM_1] = cost;
					Generic.Destroy(m, (IPlayable)s);
					g.DeathProcessingAndAuraUpdate();
					SummonGet(g, owner, RandomMinionOfCost(g, cost), s, Math.Min(slot, owner.BoardZone.Count));
				})
			}));
			// [TLC_236] Hybridization (5) — Draw a 1, 2, 3, and 4-Cost minion. Kindred: They cost (1) less.
			cards.Add("TLC_236", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					bool kindred = Generic.KindredPlayed(c, s);
					for (int cost = 1; cost <= 4; cost++)
					{
						int n = cost;
						if (DrawOne(g, c, p => p is Minion && p.Cost == n) is IPlayable drawn && kindred) GiveCostLess(g, s, drawn, V("TLC_236", "less", 1));
					}
				}),
				SecondPowerTask = new CustomTask((g, c, s, t, stack) => { })
			}));
			// [TLC_237] Skyscreamer Eggs (3) — Deathrattle: Summon four 2/1 Hatchlings.
			cards.Add("TLC_237", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("TLC_237", "hatchlings", 4); i++) SummonGet(g, c, Cards.FromId("TLC_237t"), s); })
			}));
			// [TLC_239] Restore the Wild (1) — Quest: Fill your board on 3 of your turns. Reward: The Everbloom. (wiki: "progress
			// immediately after the board is filled at any time on the player's turn"; once a turn)
			cards.Add("TLC_239", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Spell quest) || g.CurrentPlayer != c || !c.BoardZone.IsFull || quest[HsTag.QUEST_COUNTED_TURN] == g.Turn) return;
						quest[HsTag.QUEST_COUNTED_TURN] = g.Turn;
						QuestStep(g, c, quest);
					})
				}
			}));
			// [TLC_239t] The Everbloom — After your hero attacks, give your minions +2/+2.
			cards.Add("TLC_239t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.GetAll().Where(m => !(m is Location)).ToList(), "TLC_239e"))
				}
			}));
			cards.Add("TLC_239e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TLC_239t", "stats", 2))) }));
			// [TLC_257] Loh, the Living Legend (9) — Battlecry: Your minions cost (5) this game.
			cards.Add("TLC_257", new CardDef(new Power { PowerTask = new AddEnchantmentTask("TLC_257e1", EntityType.CONTROLLER) }));
			cards.Add("TLC_257e1", new CardDef(new Power { Aura = new Aura(AuraType.HAND, Effects.SetCost(V("TLC_257", "cost", 5))) { Condition = SelfCondition.IsMinion } }));

			// ---------------------------------------------------------------- Hunter
			// [DINO_403] Devilsaur Mask (8) — Set a minion's stats to 8/8. Give it Charge.
			cards.Add("DINO_403", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("DINO_403e", EntityType.TARGET) }));
			cards.Add("DINO_403e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.SetAttack(V("DINO_403", "stats", 8)), Effects.SetMaxHealth(V("DINO_403", "stats", 8)), Effects.Charge)
			}));
			// [DINO_422] Ankylodon (6) — Taunt. Deathrattle: Summon two random 3-Cost Beasts. They attack random enemies.
			cards.Add("DINO_422", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var beasts = new List<Minion>();
					for (int i = 0; i < V("DINO_422", "beasts", 2); i++)
						if (SummonGet(g, c, RandomBoardMinion(g, V("DINO_422", "cost", 3), x => x.IsRace(Race.BEAST)), s) is Minion m) beasts.Add(m);
					foreach (Minion beast in beasts)
					{
						if (beast.IsDead || beast.ToBeDestroyed || beast.Zone?.Type != Zone.PLAY) continue;
						var enemies = AttackableEnemies(c);
						if (enemies.Count == 0) break;
						EffectAttack(g, beast, enemies[g.Random.Next(enemies.Count)]);
						g.DeathProcessingAndAuraUpdate();
					}
				})
			}));
			// [DINO_434] Raptor-Nest Nurse (1) — Battlecry: Get a random 1-Cost minion. Deathrattle: Get a random 1-Cost spell.
			cards.Add("DINO_434", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (RandomCard(g, x => x.Type == CardType.MINION && x.Cost == V("DINO_434", "cost", 1)) is Card m) AddToHand(c, m); }),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { if (RandomCard(g, x => x.Type == CardType.SPELL && x.Cost == V("DINO_434", "cost", 1)) is Card sp) AddToHand(c, sp); })
			}));
			// [TLC_366] Pterrorwing Ravager (6) — Rush. Kindred: Costs (2) less. (wiki Kindred: "Kindred effects that modify a card's own
			// cost are active even while in hand")
			cards.Add("TLC_366", new CardDef(KindredPower((g, c, s, t) => { }, power: new Power
			{
				Aura = new AdaptiveCostEffect(p => Generic.Kindred(p.Controller, p) ? V("TLC_366", "less", 2) : 0)
			})));
			// [TLC_822] Dinositter (2) — At the end of your turn, reduce the Cost of a random Beast in your hand by (1).
			cards.Add("TLC_822", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = BuffRandomInHand(p => p is Minion m && m.IsRace(Race.BEAST), GenericCostLess(V("TLC_822", "less", 1))) }
			}));
			// [TLC_823] Cower in Fear (2) — Deal $3 damage to a minion. The next Beast you play this turn costs (2) less.
			cards.Add("TLC_823", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("TLC_823", "damage", 3), EntityType.TARGET, true), new AddEnchantmentTask("TLC_823e1", EntityType.CONTROLLER))
			}));
			cards.Add("TLC_823e1", NextCardEffect(Effects.ReduceCost(V("TLC_823", "less", 2)), new SelfCondition(p => p is Minion m && m.IsRace(Race.BEAST)), true));
			// [TLC_824] Odd Map (1) — Discover an odd-Attack Beast. If you play it this turn, also pick one of the others.
			cards.Add("TLC_824", new CardDef(new Power { PowerTask = DiscoverMap((c, x) => x.Type == CardType.MINION && x.IsRace(Race.BEAST) && x.ATK % 2 == 1) }));
			cards.Add("TLC_824e", MapMark());
			// [TLC_825] Ravasaur Matriarch (4) — Kindred: Deal damage to an enemy minion equal to this minion's Attack. (live vfx 214548:
			// a target only while Kindred holds)
			cards.Add("TLC_825", new CardDef(BattlecryEnemyMinion, KindredPower((g, c, s, t) =>
			{
				if (t is Minion m && s is Minion me) Generic.DamageCharFunc(me, m, me.AttackDamage, false);
			}, power: new Power { TargetIf = (PlayReq.REQ_TARGET_IF_AVAILABLE, (src, target) => Generic.Kindred(src.Controller, src)) })));
			// [TLC_826] Story of Carnassa (2) — Shuffle ten 1-Cost 3/2 Raptors into your deck with "Battlecry: Draw a card."
			cards.Add("TLC_826", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TLC_826", "raptors", 10) && !c.DeckZone.IsFull; i++)
						Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("UNG_920t2"), new EntityData { [GameTag.CREATOR] = s.Id }));
				})
			}));
			// [TLC_827] Grazing Stegodon (3) — At the end of your turn, gain +1 Attack (even while in hand or deck).
			Trigger Graze(TriggerActivation where) => new Trigger(TriggerType.TURN_END)
			{
				TriggerActivation = where,
				SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is IPlayable me) Give(g, me, me, "TLC_827e"); })
			};
			cards.Add("TLC_827", new CardDef(new Power { Trigger = new MultiTrigger(Graze(TriggerActivation.HAND_OR_PLAY), Graze(TriggerActivation.DECK)) }));
			cards.Add("TLC_827e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TLC_827", "attack", 1))) }));
			// [TLC_828] Supreme Dinomancy (5) — Give +2/+2 to all Beasts in your hand, deck, and battlefield.
			cards.Add("TLC_828", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.BoardZone.GetAll().Cast<IPlayable>().Concat(c.HandZone.GetAll()).Concat(c.DeckZone.GetAll())
						.Where(p => p is Minion m && !(m is Location) && m.IsRace(Race.BEAST)).ToList(), "TLC_828e"))
			}));
			cards.Add("TLC_828e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TLC_828", "stats", 2))) }));
			// [TLC_830] The Food Chain (1) — Quest: Play a 1, 3, 5, and 7-Attack Beast. Reward: Shokk. (after the Battlecry — patch 34.0.0;
			// its current Attack; each value once)
			cards.Add("TLC_830", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable played = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (!(s is Spell quest) || !(played is Minion m) || !m.IsRace(Race.BEAST)) return;
						int atk = m.AttackDamage;
						if (atk < 1 || atk > 7 || atk % 2 == 0) return;
						int bit = 1 << (atk / 2);
						if ((quest[HsTag.QUEST_COUNTED_MASK] & bit) != 0) return;
						quest[HsTag.QUEST_COUNTED_MASK] = quest[HsTag.QUEST_COUNTED_MASK] | bit;
						QuestStep(g, c, quest);
					})
				}
			}));
			// [TLC_830t] Shokk, Jungle Tyrant (5) — Rush. Battlecry: Discover any 8, 6, and 4-Attack Beast. Set their Costs to (2).
			ISimpleTask DiscoverBeast(int atk) => DiscoverFromTask.Where((c, x) => x.Type == CardType.MINION && x.IsRace(Race.BEAST) && x.ATK == atk, "GBL_006e");
			cards.Add("TLC_830t", new CardDef(new Power { PowerTask = ComplexTask.Create(DiscoverBeast(8), DiscoverBeast(6), DiscoverBeast(4)) }));
			// [TLC_836] Niri of the Crater (3) — Whenever you play a 1-Cost minion, double its stats. Whenever you cast a 1-Cost spell, cast it
			// twice. (the cost it was played for; Generic.ExactCostSpellCastsTwice)
			Generic.ExactCostSpellsCost = V("TLC_836", "cost", 1);
			cards.Add("TLC_836", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER, new Effect(HsTag.EXACT_COST_SPELLS_CAST_TWICE, EffectOperator.ADD, 1)),
				Trigger = new Trigger(TriggerType.PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(t is Minion m) || m.Id == s.Id || PlayedCost(m) != V("TLC_836", "cost", 1)) return;
						Generic.AddEnchantmentBlock(g, Cards.FromId("TLC_836e"), (IPlayable)s, m, m.AttackDamage, m.Health, 0);
					})
				}
			}));
			cards.Add("TLC_836e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0), Effects.Health_N(0)) { UseScriptTag = true } }));

			// ---------------------------------------------------------------- Mage
			// [DINO_409] Techysaurus (7) — Taunt. Costs (1) less for each card you played this game that didn't start in your deck.
			cards.Add("DINO_409", new CardDef(new Power { Aura = new AdaptiveCostEffect(p => Generic.PlayedNotFromDeck(p.Controller)) }));
			// [DINO_414] Tribute Dance (5) — Choose a minion. Choose a different minion to transform it into. (live darkmoon 23987: the
			// second a TARGET pick among the other minions; the first becomes a copy of it)
			cards.Add("DINO_414", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion first)) return;
					int firstId = first.Id;
					Generic.PickTargets(c, s, 1, ch => ch is Minion m && m.Id != firstId && m.Zone?.Type == Zone.PLAY && Generic.Pickable(c, m, true), (g2, c2, s2, pick) =>
					{
						if (!(pick is Minion into) || !g2.IdEntityDic.TryGetValue(firstId, out IPlayable p) || !(p is Minion turned) || turned.Zone?.Type != Zone.PLAY) return;
						Generic.NamedFx(into, "Polymorph", new[] { firstId }, () => new TransformCopyTask().Process(g2, turned.Controller, turned, into));
					});
				})
			}));
			// [DINO_429] Sheep Mask (4) — Set a minion's stats to 1/1 and give it "Deathrattle: Deal 2 damage to all minions."
			cards.Add("DINO_429", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("DINO_429e", EntityType.TARGET) }));
			cards.Add("DINO_429e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.SetAttack(V("DINO_429", "stats", 1)), Effects.SetMaxHealth(V("DINO_429", "stats", 1)), new Effect(GameTag.DEATHRATTLE, EffectOperator.SET, 1)),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
					DamageAll(s, BoardMinions(c).Concat(BoardMinions(c.Opponent)).ToList(), V("DINO_429", "damage", 2), false))
			}));
			// [TLC_220] Windswept Pageturner (4) — After you summon an Elemental, deal 3 damage to a random enemy.
			cards.Add("TLC_220", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Minion m && m.IsRace(Race.ELEMENTAL)),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t != null && t.Id == s.Id) return;
						var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed).ToList();
						if (enemies.Count > 0) Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], V("TLC_220", "damage", 3), false);
					})
				}
			}));
			// [TLC_226] Conjured Bookkeeper (3) — Deathrattle: Draw a spell. Kindred: Summon a copy of this.
			cards.Add("TLC_226", new CardDef(KindredPower((g, c, s, t) =>
			{
				if (s is Minion me && me.Zone?.Type == Zone.PLAY && !c.BoardZone.IsFull) Generic.Copy(c, s, me, Zone.PLAY);
			}, power: new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p is Spell)) })));
			// [TLC_334] Relic of Kings (7) — Discover a spell from any class that costs (8) or more. It costs (1). (any class: class cards
			// only — wiki Discover)
			cards.Add("TLC_334", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.Where((c, x) => x.Type == CardType.SPELL && x.Cost >= V("TLC_334", "cost", 8) && !x.HasClass(CardClass.NEUTRAL), "GBL_001e")
			}));
			// [TLC_364] Story of the Waygate (2) — Reduce the Cost of cards in your hand that didn't start in your deck by (1).
			cards.Add("TLC_364", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.HandZone.GetAll().Where(Generic.DidntStartInDeck).ToList(), GenericCostLess(V("TLC_364", "less", 1))))
			}));
			// [TLC_365] Storage Scuffle (3) — Deal $3 damage to a minion. Costs (0) if you've Discovered this turn.
			cards.Add("TLC_365", new CardDef(TargetMinion, new Power
			{
				Aura = new AdaptiveCostEffect(p => 0, EffectOperator.SET, new SelfCondition(p => Generic.DiscoveredThisTurn(p.Controller) > 0)),
				PowerTask = new DamageTask(V("TLC_365", "damage", 3), EntityType.TARGET, true)
			}));
			// [TLC_452] Titanographer Osk (6) — Gains a random Titan ability in your hand that changes each turn. (its versions: the
			// Remixed rule — Generic.RemixBaseOf; each version's Battlecry is that TITANS ability, run by Osk)
			cards.Add("TLC_452", new CardDef(new Power { NoScript = true }));
			var oskAbilities = new Dictionary<int, string>
			{
				[1] = "TTN_737t", [2] = "TTN_737t3", [3] = "TTN_737t1", [4] = "TTN_862t1", [5] = "TTN_862t2", [6] = "TTN_862t3", [7] = "TTN_903t",
				[8] = "TTN_903t2", [9] = "TTN_903t3", [13] = "TTN_075t", [14] = "TTN_075t3", [15] = "TTN_075t2", [16] = "TTN_858t3",
				[17] = "TTN_858t2", [18] = "TTN_858t1", [19] = "TTN_429t", [20] = "TTN_429t3", [21] = "TTN_429t2", [22] = "TTN_721t",
				[23] = "TTN_721t1", [24] = "TTN_721t2", [26] = "TTN_800t2", [27] = "TTN_800t", [28] = "TTN_960t3", [29] = "TTN_960t2",
				[30] = "TTN_415t3", [31] = "TTN_415t2", [32] = "TTN_415t", [33] = "YOG_516t3", [34] = "YOG_516t2", [35] = "YOG_516t",
			};
			foreach (var kv in oskAbilities)
				if (Cards.TryFromId("TLC_452" + "t" + kv.Key) != null && cards.TryGetValue(kv.Value, out CardDef ability))
					cards.Add("TLC_452" + "t" + kv.Key, AsBattlecry(ability)); // (the versions TLC_452t<n>)
			// [TLC_460] The Forbidden Sequence (1) — Quest: Discover 8 cards. Reward: The Origin Stone. (wiki: "Discovered cards don't need to
			// enter your hand to advance the quest")
			cards.Add("TLC_460", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_DISCOVER) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = new CustomTask((g, c, s, t, stack) => QuestStep(g, c, s as Spell)) }
			}));
			// [TLC_460t] The Origin Stone (3) — After you Discover a card, this plays the other options. Lose 1 Durability. (copies, with
			// their enchantments — wiki The Forbidden Sequence)
			cards.Add("TLC_460t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_DISCOVER)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						int[] others = { c[HsTag.LAST_DISCOVER_OTHER_A], c[HsTag.LAST_DISCOVER_OTHER_B] };
						foreach (int id in others)
						{
							if (id <= 0 || !g.IdEntityDic.TryGetValue(id, out IPlayable option) || c.Hero.IsDead) continue;
							IPlayable copy = option.Zone?.Type == Zone.SETASIDE ? Generic.Copy(c, s, option, Zone.SETASIDE) : Entity.FromCard(c, option.Card, null, c.SetasideZone);
							Generic.PlayByEffect(g, c, s, copy);
							g.DeathProcessingAndAuraUpdate();
						}
						LoseDurability(s);
					})
				}
			}));
			// [TLC_461] Scrappy Scavenger (1) — Battlecry: Discover a card with Cost equal to your remaining Mana Crystals.
			cards.Add("TLC_461", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int mana = c.RemainingMana;
					DiscoverFromTask.ClassOrNeutral((c2, x) => x.Cost == mana).Process(g, c, s, null);
				})
			}));
			// [TLC_462] Unearthed Artifacts (2) — Summon a random 2-Cost minion. If you've Discovered this turn, summon a random 4-Cost minion instead.
			cards.Add("TLC_462", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				GlowWhile = p => Generic.DiscoveredThisTurn(p.Controller) > 0,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					SummonGet(g, c, RandomMinionOfCost(g, Generic.DiscoveredThisTurn(c) > 0 ? V("TLC_462", "more", 4) : V("TLC_462", "cost", 2)), s))
			}));
			// [TLC_483] Vault Breaker (3) — After you Discover a card, reduce its Cost by (1). (Dwarven Archaeologist's rule)
			cards.Add("TLC_483", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_DISCOVER)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.Zone?.Type == Zone.HAND),
					SingleTask = new AddEnchantmentTask(GenericCostLess(V("TLC_483", "less", 1)), EntityType.TARGET)
				}
			}));
		}
	}
}
