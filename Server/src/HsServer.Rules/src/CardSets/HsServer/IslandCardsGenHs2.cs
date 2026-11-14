// HsServer addition (not upstream SabberStone): Perils in Paradise — chunk 1: the Neutral, Death Knight and Demon Hunter cards
// and the Travel Agency's (spec: research spec_chunk1; shared pieces: IslandCardsGenHs.cs, Actions/Paradise.cs). Every number
// goes through CardValues (V).
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
		/// <summary>Mixologist's ingredients (its RelatedCards: the 1-Cost Kazakus herbs).</summary>
		private static readonly string[] MixologistHerbs =
			{ "CFM_621t4", "CFM_621t10", "CFM_621t37", "CFM_621t2", "CFM_621t3", "CFM_621t6", "CFM_621t5", "CFM_621t8", "CFM_621t9" };

		/// <summary>Griftah's real Amulets (each one's TAG_SCRIPT_DATA_NUM_1 = its phony twin's dbf, the twin's = the real one's).</summary>
		private static readonly string[] GriftahAmulets =
			{ "VAC_959t01t", "VAC_959t05t", "VAC_959t06t", "VAC_959t07t", "VAC_959t08t", "VAC_959t09t", "VAC_959t10t" };

		/// <summary>Dreamplanner Zephrys' Travel Tours and their pools (wiki Dreamplanner Zephrys: "Each tour has an unique card pool
		/// composed of different Legacy cards" — the wiki's lists, collectible members; the client has no subset for them).</summary>
		private static readonly Dictionary<string, string[]> TravelTours = new Dictionary<string, string[]>
		{
			["WORK_027t1"] = new[] { "CORE_EX1_259", "CS2_026", "CORE_CS2_032", "CORE_EX1_312", "CORE_CS2_093", "NEW1_007", "NEW1_004", "CORE_EX1_309",
				"EX1_303", "CORE_EX1_407", "EX1_334", "EX1_626", "CORE_EX1_197", "EX1_619", "CORE_CS2_028", "CORE_CS2_012", "CORE_CS1_112",
				"CORE_EX1_246", "CS2_022", "EX1_066", "CORE_EX1_622" },
			["WORK_027t2"] = new[] { "CORE_EX1_323", "CORE_EX1_383", "CS1_113", "CORE_EX1_016", "CORE_EX1_573", "EX1_559", "EX1_354", "EX1_572",
				"CORE_NEW1_008", "CORE_EX1_298", "EX1_295", "CORE_EX1_144", "CORE_EX1_105", "CORE_EX1_250", "CORE_EX1_164", "CS2_077",
				"CORE_EX1_110", "CORE_EX1_411", "EX1_198" },
			["WORK_027t3"] = new[] { "CORE_CS2_029", "CORE_CS2_024", "EX1_384", "CS2_011", "CORE_EX1_124", "CORE_EX1_238", "EX1_241", "CS2_039",
				"EX1_308", "EX1_408", "CORE_EX1_414", "CORE_EX1_116", "CORE_NEW1_010", "VAN_DS1_233", "CS2_045", "CORE_CS2_046",
				"CORE_EX1_567", "CORE_EX1_543", "CORE_EX1_279" },
		};

		/// <summary>Up to <paramref name="n"/> random hits of <paramref name="damage"/> at living enemies (one target round each).</summary>
		private static void IslandRandomHits(Game g, Controller c, IEntity source, int n, int damage, bool spell)
		{
			for (int i = 0; i < n; i++)
			{
				var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed).ToList();
				if (enemies.Count == 0) break;
				g.PowerHistory?.NewTargetRound();
				Generic.DamageCharFunc((IPlayable)source, enemies[g.Random.Next(enemies.Count)], damage, spell);
			}
		}

		private static void IslandFreeze(Game g, Controller c, IEntity source, ICharacter ch) =>
			ComplexTask.Freeze(EntityType.TARGET).Process(g, c, source, (IPlayable)ch);

		/// <summary>Every character on both sides (heroes and minions in play).</summary>
		private static IEnumerable<ICharacter> AllCharacters(Controller c) =>
			new ICharacter[] { c.Hero, c.Opponent.Hero }.Concat(c.BoardZone.GetAll()).Concat(c.Opponent.BoardZone.GetAll()).Where(x => x != null);

		/// <summary>A summoned minion the caller keeps hold of (Generic.SummonBlock; null when the board is full).</summary>
		private static Minion IslandSummon(Game g, Controller c, Card card, IEntity source, int position = -1)
		{
			if (card == null || c.BoardZone.IsFull) return null;
			var m = (Minion)Entity.FromCard(c, card);
			Generic.SummonBlock.Invoke(g, m, position, source);
			return m.Zone?.Type == Zone.PLAY ? m : null;
		}

		/// <summary>Overplanner: three picks from the deck, each excluding the earlier ones, then the pile on top (first pick on top).</summary>
		private static void IslandOverplan(Controller c, IEntity source, List<IPlayable> picked, int left)
		{
			var options = Generic.PickFromDeck(c, p => !picked.Contains(p));
			if (left <= 0 || options.Count == 0)
			{
				for (int i = picked.Count - 1; i >= 0; i--)
					if (picked[i].Zone?.Type == Zone.DECK) { c.DeckZone.Remove(picked[i]); c.DeckZone.Add(picked[i]); }
				return;
			}
			Generic.DiscoverFromDeck(c, source, options, (g, c2, s2, card) =>
			{
				picked.Add(card);
				IslandOverplan(c2, s2, picked, left - 1);
			});
		}

		private static void IslandChunk1(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Neutral
			// [VAC_304] Tidepool Pupil (2) — Battlecry: If you've cast 3 spells while holding this, Discover one of them. (Live
			// yogg-cthun 12240: the offer is the FIRST three spells cast while holding it.)
			cards.Add("VAC_304", new CardDef(new Power
			{
				Trigger = WhileHolding(TriggerType.CAST_SPELL, p => p is Spell),
				ThresholdValue = V("VAC_304", "spells", 3),
				PowerTask = ComplexTask.If((g, c, s) => Held(s),
					new DiscoverFromTask((g, c, s) => SpellsWhileHeld(c, (IPlayable)s).Take(V("VAC_304", "spells", 3)).Select(h => h.SourceCard).Distinct()))
			}));
			// [VAC_321] Incindius (7) — At the end of your turn, upgrade your Eruptions. Battlecry: Shuffle 5 of them into your deck.
			cards.Add("VAC_321", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("VAC_321", "eruptions", 5); i++)
						Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("VAC_321t"), new EntityData { [GameTag.CREATOR] = s.Id }));
				}),
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						Card eruption = Cards.FromId("VAC_321t");
						foreach (IPlayable p in c.HandZone.GetAll().Concat(c.DeckZone.GetAll()).Where(p => p.Card == eruption).ToList())
							p[GameTag.TAG_SCRIPT_DATA_NUM_1] = p[GameTag.TAG_SCRIPT_DATA_NUM_1] + V("VAC_321", "upgrade", 1);
					})
				}
			}));
			// [VAC_321t] Eruption (1) — Casts When Drawn. Deal $@ damage to all enemies.
			ISimpleTask eruption = new CustomTask((g, c, s, t, stack) => DamageAll(s, Enemies(c), s[GameTag.TAG_SCRIPT_DATA_NUM_1], true));
			cards.Add("VAC_321t", new CardDef(new Power { PowerTask = eruption, TopdeckTask = eruption }));
			// [VAC_327] Cryopractor (3) — Battlecry: Give a minion +3/+3 and Freeze it.
			cards.Add("VAC_327", new CardDef(BattlecryMinion, new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("VAC_327e", EntityType.TARGET), ComplexTask.Freeze(EntityType.TARGET))
			}));
			cards.Add("VAC_327e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("VAC_327", "stats", 3))) }));
			// [VAC_406] Sleepy Resident (4) — Taunt. Deathrattle: ALL other minions fall asleep. (Wiki: "Falling asleep refers to
			// summoning sickness" — Generic.FallAsleep; VAC_406e the invisible marker for this turn.)
			cards.Add("VAC_406", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => m != s && !m.IsDead).ToList())
					{
						bool awake = !m.IsExhausted;
						Generic.FallAsleep(m);
						if (awake && (m.IsExhausted || m.AttackableByRush)) Give(g, s, m, "VAC_406e");
					}
				})
			}));
			cards.Add("VAC_406e", OneTurn());
			// [VAC_421] Snoozin' Zookeeper (7) — Battlecry: Summon an 8/8 Beast for your opponent. It attacks all of their minions.
			// (Forum t/155378: it attacks until it dies or none are left; left to right.)
			cards.Add("VAC_421", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Minion beast = IslandSummon(g, c.Opponent, Cards.FromId("VAC_421t"), s);
					if (beast == null) return;
					foreach (Minion m in c.Opponent.BoardZone.GetAll().Where(m => m != beast).ToList())
					{
						if (beast.IsDead || beast.ToBeDestroyed || beast.Zone?.Type != Zone.PLAY) break;
						if (m.IsDead || m.ToBeDestroyed || m.Untouchable || m.Zone?.Type != Zone.PLAY) continue;
						EffectAttack(g, beast, m);
						g.DeathProcessingAndAuraUpdate();
					}
				})
			}));
			// [VAC_430] Bloodsail Recruiter (2) — Battlecry: Discover a Pirate.
			cards.Add("VAC_430", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.IsRace(Race.PIRATE))
			}));
			// [VAC_432] Resort Valet (4) — Battlecry: Discover a card from the newest expansion.
			cards.Add("VAC_432", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => (int)x.Set == Generic.NewestExpansion())
			}));
			// [VAC_438] Travel Agent (2) — Battlecry: Discover a location from any class.
			cards.Add("VAC_438", new CardDef(new Power { PowerTask = DiscoverFromTask.Where((c, x) => x.Type == CardType.LOCATION) }));
			// [VAC_439] Seaside Giant (10) — Costs (2) less for each time you've used a location this game.
			cards.Add("VAC_439", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => V("VAC_439", "discount", 2) * p.Controller[Location.LOCATION_USES_THIS_GAME])
			}));
			// [VAC_440] Customs Enforcer (3) — Enemy cards that didn't start in their deck cost (2) more.
			cards.Add("VAC_440", new CardDef(new Power
			{
				Aura = new Aura(AuraType.OP_HAND, Effects.AddCost(V("VAC_440", "cost", 2))) { Condition = new SelfCondition(p => Generic.DidntStartInDeck(p)), ShowAs = "GBL_005e" }
			}));
			// [VAC_441] Package Dealer (6) — After you draw a card, 50% chance to draw another.
			cards.Add("VAC_441", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DRAW)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (g.Random.Next(100) < V("VAC_441", "chance %", 50)) Generic.Draw(c); })
				}
			}));
			// [VAC_442] Lamplighter (4) — Battlecry: Deal @ damage (Improved by each turn in a row you've played an Elemental).
			cards.Add("VAC_442", new CardDef(BattlecryAny, new Power
			{
				HandCounter = WildWestStreakCounter,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter target) Generic.DamageCharFunc((IPlayable)s, target, Generic.ElementalStreakCounter((IPlayable)s), false);
				})
			}));
			// [VAC_444] Overplanner (3) — Battlecry: Choose three cards from your deck to put on top in that order. (Wiki: three
			// picks of 3 random deck cards, each without the earlier picks; the first pick ends on top.)
			cards.Add("VAC_444", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => IslandOverplan(c, s, new List<IPlayable>(), V("VAC_444", "cards", 3)))
			}));
			// [VAC_446] A. F. Kay (5) — At the end of your turn, give all other friendly minions that didn't attack +2/+2.
			cards.Add("VAC_446", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
						GiveAll(g, s, c.BoardZone.GetAll().Where(m => m != s && m.NumAttacksThisTurn == 0).ToList(), "VAC_446e"))
				}
			}));
			cards.Add("VAC_446e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("VAC_446", "stats", 2))) }));
			// [VAC_447] Dread Deserter (6) — Has Charge if this didn't start in your deck. (Its start is fixed; the self aura shows it,
			// and summoned it wakes up: BoardZone decides summoning sickness before auras.)
			cards.Add("VAC_447", new CardDef(new Power
			{
				Aura = new AdaptiveEffect(GameTag.CHARGE, EffectOperator.SET, p => Generic.DidntStartInDeck(p) ? 1 : 0),
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me && Generic.DidntStartInDeck(me) && me.NumAttacksThisTurn == 0 && me.IsExhausted) me.IsExhausted = false;
					})
				}
			}));
			// [VAC_461] Drink Server (2) — Deathrattle: Get a random Drink spell. (It has 3 uses!)
			cards.Add("VAC_461", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => AddToHand(c, Generic.RandomRelated(g, ((IPlayable)s).Card, CardType.SPELL)))
			}));
			// [VAC_463] Concierge (3) — Your cards from another class cost (1) less.
			cards.Add("VAC_463", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("VAC_463", "discount", 1))) { Condition = new SelfCondition(Generic.OfAnotherClass), ShowAs = "GBL_003e" }
			}));
			// [VAC_521] Bumbling Bellhop (3) — Taunt. Battlecry: If you're holding a spell that costs (5) or more, summon a copy of this.
			cards.Add("VAC_521", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.HandZone.Any(p => p is Spell && p.Cost >= V("VAC_521", "cost", 5)),
					new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me && me.Zone?.Type == Zone.PLAY && !c.BoardZone.IsFull) Generic.Copy(c, s, me, Zone.PLAY, position: me.ZonePosition + 1);
					}))
			}));
			// [VAC_523] Mixologist (3) — Battlecry: Craft a custom 1-Cost Potion. (Wiki: "works exactly like Kazakus" — two of the
			// 1-Cost herbs, the second offer without the first; Generic.CraftPotion into Mixologist's Special, in Kazakus' ingredient order.)
			cards.Add("VAC_523", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var first = OfferCards(c, s, Pick(g, MixologistHerbs.Select(Cards.FromId), 3).Select(x => x.Id));
					Generic.CreateChoiceEntities(c, s, first, ChoiceAction.STACK, new CustomTask((g2, c2, s2, one, st2) =>
					{
						if (one == null) return;
						var second = OfferCards(c2, s2, Pick(g2, MixologistHerbs.Where(h => h != one.Card.Id).Select(Cards.FromId), 3).Select(x => x.Id));
						Generic.CreateChoiceEntities(c2, s2, second, ChoiceAction.STACK, new CustomTask((g3, c3, s3, two, st3) =>
						{
							if (two == null || c3.HandZone.IsFull) return;
							IPlayable potion = Entity.FromCard(c3, Generic.CraftPotion(Cards.FromId("VAC_523t"), one.Card, two.Card), new EntityData
							{
								[GameTag.CREATOR] = s3.Id, [GameTag.DISPLAYED_CREATOR] = s3.Id, [GameTag.LAST_AFFECTED_BY] = s3.Id
							});
							Generic.AddHandPhase.Invoke(c3, potion);
						}));
					}));
				})
			}));
			// [CFM_621t5] Icecap (1) — Freeze a random enemy minion. (Upstream froze a random enemy CHARACTER.)
			cards.Add("CFM_621t5", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.Untouchable).ToList();
					if (minions.Count > 0) IslandFreeze(g, c, s, minions[g.Random.Next(minions.Count)]);
				})
			}));
			// [VAC_529] Scrapbooking Student (5) — Battlecry: Summon a copy of a friendly location. (The player picks; a play-to-play
			// copy keeps its damage and enchantments — rule book Copy — and can be used: patch 30.2.)
			cards.Add("VAC_529", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.PickTargets(c, s, 1, ch => ch is Location l && l.Controller == c, (g2, c2, s2, picked) =>
					{
						if (!(picked is Location l) || c2.BoardZone.IsFull) return;
						if (Generic.Copy(c2, s2, l, Zone.PLAY, position: l.ZonePosition + 1) is Location copy)
						{
							copy.OnCooldown = false;
							copy.IsExhausted = false;
						}
					}))
			}));
			// [VAC_531] Bayfin Bodybuilder (5) — After a minion is summoned for your opponent during your turn, Silence and destroy it.
			// (Wiki patch 30.0.3: capped at 15 activations; 30.4: one at a time.)
			cards.Add("VAC_531", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.ENEMY,
					Condition = new SelfCondition(p => p is Minion && p.Game.CurrentPlayer != p.Controller),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me) || !((t ?? g.CurrentEventData?.EventSource as IPlayable) is Minion m) || m.Zone?.Type != Zone.PLAY) return;
						if (me[HsTag.ACTIVATIONS_TURN] != g.Turn) { me[HsTag.ACTIVATIONS_TURN] = g.Turn; me[HsTag.ACTIVATIONS_THIS_TURN] = 0; }
						if (me[HsTag.ACTIVATIONS_THIS_TURN] >= V("VAC_531", "cap", 15)) return;
						me[HsTag.ACTIVATIONS_THIS_TURN] = me[HsTag.ACTIVATIONS_THIS_TURN] + 1;
						m.Silence();
						Generic.Destroy(m, s);
					})
				}
			}));
			// [VAC_532] Coconut Cannoneer (2) — After an adjacent minion attacks, deal 1 damage to a random enemy.
			cards.Add("VAC_532", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me) || me.Zone?.Type != Zone.PLAY || !(g.CurrentEventData?.EventSource is Minion attacker)) return;
						if (Neighbors(me).Contains(attacker)) IslandRandomHits(g, c, s, 1, V("VAC_532", "damage", 1), false);
					})
				}
			}));
			// [VAC_702] Marin the Manager (7) — Battlecry: Choose a fantastic treasure. Shuffle the other 3 into your deck. (Live
			// 185040 92004-92272: all four offered; the others shuffled as new cards inside the recorded SpawnToDeck effect.)
			cards.Add("VAC_702", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					string[] treasures = { "VAC_702t2", "VAC_702t", "VAC_702t3", "VAC_702t4" };
					Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.HAND, treasures.Select(Cards.FromId).ToArray(),
						new CustomTask((g2, c2, s2, pick, st) =>
							Generic.NamedFx((IPlayable)s2, "SpawnToDeck", null, () =>
							{
								foreach (string id in treasures.Where(id => id != pick?.Card.Id))
									Generic.ShuffleIntoDeck.Invoke(c2, s2, Entity.FromCard(c2, Cards.FromId(id), new EntityData { [GameTag.DISPLAYED_CREATOR] = s2.Id }));
							})));
				})
			}));
			// Marin's treasures — the LOOT_998 originals (VAC_702t* count as copies of them; the client's texts are the same):
			// [LOOT_998h] Tolin's Goblet (3) — Draw a card. Fill your hand with copies of it.
			cards.Add("LOOT_998h", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					IPlayable drawn = Generic.Draw(c);
					if (drawn == null || drawn.Zone?.Type != Zone.HAND) return;
					while (!c.HandZone.IsFull)
						if (Generic.Copy(c, drawn, drawn, Zone.HAND) == null) break;
				})
			}));
			// [LOOT_998j] Zarog's Crown (3) — Discover a Legendary minion. Summon two copies of it. (Live 103301: the pick goes to the
			// hand and two new copies are summoned.)
			cards.Add("LOOT_998j", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY,
					afterPick: new CustomTask((g, c, s, t, stack) =>
					{
						if (t == null) return;
						for (int i = 0; i < V("LOOT_998j", "copies", 2); i++) IslandSummon(g, c, t.Card, s);
					}))
			}));
			// [LOOT_998l] Wondrous Wand (3) — Draw 3 cards. Set their Cost to (1). (Live 92340: GBL_001e on each.)
			cards.Add("LOOT_998l", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("LOOT_998l", "cards", 3); i++)
						if (Generic.Draw(c) is IPlayable drawn && drawn.Zone?.Type == Zone.HAND) Give(g, s, drawn, "GBL_001e");
				})
			}));
			// [LOOT_998k] Golden Kobold (3) — Taunt. Battlecry: Replace your hand with Legendary minions. They cost (1) less.
			cards.Add("LOOT_998k", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in c.HandZone.GetAll().ToList())
						if (RandomCard(g, x => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY) is Card legend &&
						    Generic.ChangeEntityBlock.Invoke(c, p, legend, true) is IPlayable changed)
							Give(g, s, changed, GenericCostLess(V("LOOT_998k", "discount", 1)));
				})
			}));
			// [VAC_924] Weapons Attendant (6) — Battlecry: If you control another Pirate, equip a random weapon from your deck.
			cards.Add("VAC_924", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.BoardZone.GetAll().Any(m => m != s && m.IsRace(Race.PIRATE)),
					new CustomTask((g, c, s, t, stack) => Generic.EquipFromDeck(c)))
			}));
			// [VAC_934] Beached Whale (7) — Taunt. Battlecry: Deal 10 damage to this minion.
			cards.Add("VAC_934", new CardDef(new Power { PowerTask = new DamageTask(V("VAC_934", "damage", 10), EntityType.SOURCE) }));
			// [VAC_935] Carry-On Grub (4) — Battlecry: Get a 1-Cost Suitcase. Pack the top 2 cards of your deck into it. (Wiki: the
			// top 2 leave the deck; the Suitcase gives new cards of them — forum t/138645. CARDTEXT_ENTITY_0/1 name them.)
			cards.Add("VAC_935", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var packed = new List<IPlayable>();
					for (int i = 0; i < V("VAC_935", "cards", 2) && c.DeckZone.Count > 0; i++)
					{
						IPlayable top = c.DeckZone.TopCard;
						c.DeckZone.Remove(top);
						c.SetasideZone.Add(top);
						packed.Add(top);
					}
					if (packed.Count == 0) return;
					IPlayable suitcase = IslandToHand(c, s, Cards.FromId(packed.Count >= 2 ? "VAC_935t" : "VAC_935t2"));
					if (suitcase == null) return;
					suitcase[HsTag.PACKED_1] = packed[0].Id;
					suitcase[(GameTag)2655] = packed[0].Id;
					if (packed.Count > 1) { suitcase[HsTag.PACKED_2] = packed[1].Id; suitcase[(GameTag)2656] = packed[1].Id; }
				})
			}));
			// [VAC_935t] / [VAC_935t2] Carry-On Suitcase (1) — Get {0} and {1}. / Get {0}. (New cards of the packed ones; a deck card's
			// enchantments come along — rule book Copy: deck → hand copies keep them.)
			ISimpleTask unpack = new CustomTask((g, c, s, t, stack) =>
			{
				foreach (GameTag tag in new[] { HsTag.PACKED_1, HsTag.PACKED_2 })
					if (s[tag] > 0 && g.IdEntityDic.TryGetValue(s[tag], out IPlayable p) && !c.HandZone.IsFull)
						Generic.Copy(c, s, p, Zone.HAND);
			});
			cards.Add("VAC_935t", new CardDef(new Power { PowerTask = unpack }));
			cards.Add("VAC_935t2", new CardDef(new Power { PowerTask = unpack }));
			// [VAC_936] Octo-masseuse (4) — Deals octuple damage to minions. (Wiki: the damage assigned to the minion ×8; live 165219
			// 9740: a TRIGGER block holding the new PREDAMAGE. Client tag 2539 marks the octuple dealer.)
			cards.Add("VAC_936", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PREDAMAGE)
				{
					TriggerSource = TriggerSource.ALL_MINIONS,
					FastExecution = true,
					Condition = new SelfCondition(p => p is Minion && p.Game.CurrentEventData?.EventSource is IPlayable src && src.Card[(GameTag)2539] > 0),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventSource?.Id == s.Id) Generic.PreDamageMultiply(V("VAC_936", "factor", 8)).Process(g, c, s, t, stack);
					})
				}
			}));
			// [VAC_937] Sailboat Captain (3) — Battlecry: Give a friendly Pirate Windfury.
			cards.Add("VAC_937", new CardDef(new Dictionary<PlayReq, int>
			{
				{ PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_RACE, (int)Race.PIRATE }
			}, new Power { PowerTask = ComplexTask.WindFury(EntityType.TARGET) }));
			// [VAC_938] Hozen Roughhouser (3) — Whenever another friendly Pirate attacks, give it +1/+1. (Live 222041 25194: inside the
			// attack, before the damage.)
			cards.Add("VAC_938", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					Condition = new SelfCondition(IsPirate),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventSource is Minion attacker) Give(g, s, attacker, "VAC_938e");
					})
				}
			}));
			cards.Add("VAC_938e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("VAC_938", "stats", 1))) }));
			// [VAC_946] Terrible Chef (3) — Battlecry: Summon a 0/2 Nerubian Egg. Deathrattle: Destroy it. (Patch 32.4: not what the
			// Egg became.)
			cards.Add("VAC_946", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					Minion egg = IslandSummon(g, c, Cards.FromId("CORE_FP1_007"), s, me.Zone?.Type == Zone.PLAY ? me.ZonePosition + 1 : -1);
					if (egg != null) me[HsTag.LINKED_EGG] = egg.Id;
				}),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (s[HsTag.LINKED_EGG] > 0 && g.IdEntityDic.TryGetValue(s[HsTag.LINKED_EGG], out IPlayable p) && p is Minion egg &&
					    egg.Zone?.Type == Zone.PLAY && egg.Card == Cards.FromId("CORE_FP1_007") && !egg.ToBeDestroyed)
						Generic.Destroy(egg, s);
				})
			}));
			// [VAC_947] Wave Pool Thrasher (4) — Battlecry: Give all other minions -1/-1. Deathrattle: Give all other minions +1/+1.
			cards.Add("VAC_947", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => m != s).ToList(), "VAC_947e1")),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => m != s).ToList(), "VAC_947e2"))
			}));
			cards.Add("VAC_947e1", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(-V("VAC_947", "stats", 1))) }));
			cards.Add("VAC_947e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("VAC_947", "stats", 1))) }));
			// [VAC_955] Gorgonzormu (3) — Battlecry: Get a 2-Cost Cheese that summons three 1-Cost minions. It upgrades each turn.
			cards.Add("VAC_955", new CardDef(new Power { PowerTask = new AddCardTo("VAC_955t", EntityType.HAND) }));
			// [VAC_955t] Delicious Cheese (2) — Summon three random @-Cost minions. (Upgrades each turn!) (At the start of its owner's
			// turn in hand, up to 10; patch: not with a full board.)
			cards.Add("VAC_955t", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("VAC_955t", "minions", 3); i++) Summon(g, c, RandomMinionOfCost(g, s[GameTag.TAG_SCRIPT_DATA_NUM_1]), s);
				}),
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					TriggerActivation = TriggerActivation.HAND,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s[GameTag.TAG_SCRIPT_DATA_NUM_1] < V("VAC_955t", "max cost", 10)) s[GameTag.TAG_SCRIPT_DATA_NUM_1] = s[GameTag.TAG_SCRIPT_DATA_NUM_1] + 1;
					})
				}
			}));
			// [VAC_956] XB-931 Housekeeper (2) — After you use a location, gain 3 Armor.
			cards.Add("VAC_956", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.LOCATION_USED) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = new ArmorTask(V("VAC_956", "armor", 3)) }
			}));
			// [VAC_958] Adaptive Amalgam (1) — This has all minion types. Deathrattle: Shuffle this into your deck. It keeps any
			// enchantments. (Immortal Prelate's task.)
			cards.Add("VAC_958", new CardDef(new Power { DeathrattleTask = SpecificTask.ImmortalPrelate }));
			// [VAC_959] Griftah, Trusted Vendor (4) — Battlecry: Discover an amazing Amulet to give to both players. (The enemy's is a
			// phony version!) (Wiki: the pick to you, the phony twin with the same name to the opponent.)
			cards.Add("VAC_959", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card[] offer = DiscoverFromTask.PickDistinct(g, GriftahAmulets.Select(Cards.FromId), 3).ToArray();
					Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.HAND, offer, new CustomTask((g2, c2, s2, pick, st) =>
					{
						if (pick != null && Cards.FromAssetId(pick.Card[GameTag.TAG_SCRIPT_DATA_NUM_1]) is Card phony)
							IslandToHand(c2.Opponent, s2, phony);
					}));
				})
			}));
			// The Amulets: real (…t) and phony.
			ISimpleTask passions = ComplexTask.Create(new ControlTask(EntityType.TARGET), new AddEnchantmentTask("VAC_959t01e", EntityType.TARGET));
			var passionsReqs = new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 }, { PlayReq.REQ_NUM_MINION_SLOTS, 1 } };
			cards.Add("VAC_959t01t", new CardDef(passionsReqs, new Power { PowerTask = passions }));
			cards.Add("VAC_959t01", new CardDef(passionsReqs, new Power { PowerTask = ComplexTask.Create(passions, new AddEnchantmentTask("VAC_959t01e2", EntityType.TARGET)) }));
			cards.Add("VAC_959t01e", new CardDef(new Power
			{
				Enchant = new Enchant(SabberStoneCore.Actions.Generic.NonKeywordCharge, new Effect(GameTag.CONTROLLER_CHANGED_THIS_TURN, EffectOperator.SET, 1)), // HsServer: live NON_KEYWORD_CHARGE, not Charge
				Trigger = new Trigger(TriggerType.TURN_END) { EitherTurn = true, SingleTask = ComplexTask.Create(RemoveEnchantmentTask.Task, new ControlTask(EntityType.TARGET, true)) }
			}));
			cards.Add("VAC_959t01e2", OneTurn(Effects.SetAttack(V("VAC_959t01", "attack", 1))));
			ISimpleTask tracking = new CustomTask((g, c, s, t, stack) =>
			{
				for (int i = 0; i < V("VAC_959t05t", "cards", 3); i++) AddToHand(c, RandomCard(g, x => x.Rarity == Rarity.LEGENDARY));
			});
			cards.Add("VAC_959t05t", new CardDef(new Power { PowerTask = tracking }));
			cards.Add("VAC_959t05", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var got = new List<IPlayable>();
					for (int i = 0; i < V("VAC_959t05", "cards", 3); i++)
						if (IslandToHand(c, s, RandomCard(g, x => x.Rarity == Rarity.LEGENDARY)) is IPlayable p) got.Add(p);
					foreach (IPlayable p in got)
						if (p.Zone?.Type == Zone.HAND && RandomCard(g, x => x.Rarity == Rarity.COMMON) is Card common)
							Generic.ChangeEntityBlock.Invoke(c, p, common, true);
				})
			}));
			cards.Add("VAC_959t06t", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (IslandSummon(g, c, RandomMinionOfCost(g, V("VAC_959t06t", "cost", 4)), s) is Minion m) m[GameTag.TAUNT] = 1;
				})
			}));
			cards.Add("VAC_959t06", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(IslandSummon(g, c, RandomMinionOfCost(g, V("VAC_959t06", "cost", 4)), s) is Minion m)) return;
					m[GameTag.TAUNT] = 1;
					Give(g, s, m, "VAC_959t06e");
				})
			}));
			cards.Add("VAC_959t06e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.CANT_ATTACK, EffectOperator.SET, 1)) }));
			cards.Add("VAC_959t07t", new CardDef(TargetAny, new Power { PowerTask = new DamageTask(V("VAC_959t07t", "damage", 6), EntityType.TARGET, true) }));
			cards.Add("VAC_959t07", new CardDef(TargetMinion, new Power { PowerTask = new DamageTask(V("VAC_959t07", "damage", 6), EntityType.TARGET, true) }));
			cards.Add("VAC_959t08t", new CardDef(new Power { PowerTask = new HealTask(V("VAC_959t08t", "heal", 12), EntityType.HERO) }));
			cards.Add("VAC_959t08", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new HealTask(V("VAC_959t08", "heal", 12), EntityType.HERO), new DamageTask(V("VAC_959t08", "damage", 6), EntityType.HERO, true))
			}));
			cards.Add("VAC_959t09t", new CardDef(new Power { PowerTask = ComplexTask.Repeat(new DrawTask(), V("VAC_959t09t", "cards", 3)) }));
			cards.Add("VAC_959t09", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var drawn = new List<IPlayable>();
					for (int i = 0; i < V("VAC_959t09", "cards", 3); i++)
						if (Generic.Draw(c) is IPlayable p && p.Zone?.Type == Zone.HAND) drawn.Add(p);
					foreach (IPlayable p in RandomOf(g, drawn, V("VAC_959t09", "discards", 2)))
						if (p.Zone?.Type == Zone.HAND) Generic.DiscardBlock(c, p);
				})
			}));
			cards.Add("VAC_959t10t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in c.HandZone.GetAll().ToList()) Give(g, s, p, GenericCostLess(V("VAC_959t10t", "discount", 1)));
				})
			}));
			cards.Add("VAC_959t10", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in c.HandZone.GetAll().Where(p => !(p is Spell)).ToList()) Give(g, s, p, GenericCostLess(V("VAC_959t10", "discount", 1)));
				})
			}));
			// [WORK_027] Dreamplanner Zephrys (3) — Battlecry: Choose a Travel Tour to get two potentially perfect cards from it. (Wiki:
			// two random cards of the tour's pool, repeats allowed; "not 'smart'".)
			cards.Add("WORK_027", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Generic.CreateChoiceEntities(c, s, OfferCards(c, s, TravelTours.Keys), ChoiceAction.STACK, new CustomTask((g2, c2, s2, tour, st) =>
					{
						if (tour == null || !TravelTours.TryGetValue(tour.Card.Id, out string[] pool)) return;
						var cardsOf = pool.Select(Cards.TryFromId).Where(x => x != null).ToList();
						for (int i = 0; i < V("WORK_027", "cards", 2) && cardsOf.Count > 0; i++)
							IslandToHand(c2, s2, cardsOf[g2.Random.Next(cardsOf.Count)]);
					}));
				})
			}));
			// [WORK_040] Clumsy Steward (3) — After ANY card is drawn, make it Temporary.
			cards.Add("WORK_040", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DRAW)
				{
					TriggerSource = TriggerSource.ALL,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if ((t ?? g.CurrentEventData?.EventSource as IPlayable) is IPlayable drawn && drawn.Zone?.Type == Zone.HAND) Generic.MakeTemporary(drawn, s);
					})
				}
			}));
			// [WORK_041] Busy Peon (2) — Deathrattle: Your next location costs (2) less.
			cards.Add("WORK_041", new CardDef(new Power { DeathrattleTask = new AddEnchantmentTask("WORK_041e", EntityType.CONTROLLER) }));
			cards.Add("WORK_041e", NextCardCost(Effects.ReduceCost(V("WORK_041", "discount", 2)), new SelfCondition(p => p.Card.Type == CardType.LOCATION), "GBL_002e"));
			// [WORK_042] Carnivorous Cubicle (5) — Battlecry: Destroy a friendly minion. Summon a copy of it at the end of your turns.
			// (Wiki: the end-of-turn part isn't the Battlecry; base copies — patch 2024-09-13; WORK_042e names the destroyed one.)
			cards.Add("WORK_042", new CardDef(BattlecryFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m) || !(s is Minion me) || m == me) return;
					me[HsTag.DIGESTED_ENTITY] = m.Card.AssetId;
					Generic.Destroy(m, s);
					Give(g, s, me, "WORK_042e", m.Id);
				}),
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s[HsTag.DIGESTED_ENTITY] > 0) Summon(g, c, Cards.FromAssetId(s[HsTag.DIGESTED_ENTITY]), s);
					})
				}
			}));
			cards.Add("WORK_042e", new CardDef(new Power())); // a marker: its TAG_SCRIPT_DATA_NUM_1 names the destroyed entity
			// [WORK_043] Travelmaster Dungar (10) — Battlecry: Summon three minions from your deck that are from different expansions.
			cards.Add("WORK_043", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var sets = new HashSet<CardSet>();
					for (int i = 0; i < V("WORK_043", "minions", 3); i++)
						if (SummonFromDeck(g, c, s, p => p is Minion && !sets.Contains(p.Card.Set)) is Minion m) sets.Add(m.Card.Set);
						else break;
				})
			}));

			// ---------------------------------------------------------------- Death Knight
			// [VAC_402] Frostbitten Freebooter (3) — Deathrattle: Freeze 3 random enemies. Any that were already Frozen take 5 damage
			// instead. (Three different ones, their state read before acting.)
			cards.Add("VAC_402", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var picked = RandomOf(g, Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed), V("VAC_402", "enemies", 3))
						.Select(x => (Target: x, Frozen: x.IsFrozen)).ToList();
					foreach (var (target, frozen) in picked)
					{
						g.PowerHistory?.NewTargetRound();
						if (frozen) Generic.DamageCharFunc((IPlayable)s, target, V("VAC_402", "damage", 5), false);
						else IslandFreeze(g, c, s, target);
					}
				})
			}));
			// [VAC_425] Horizon's Edge (4, Location) — Deal 3 damage randomly split among all enemies. After a friendly minion dies,
			// reopen this. (No $: not spell damage.)
			cards.Add("VAC_425", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => IslandRandomHits(g, c, s, V("VAC_425", "damage", 3), 1, false)),
				Trigger = Reopens(TriggerType.DEATH, TriggerSource.MINIONS_EXCEPT_SELF)
			}));
			// [VAC_426] Eliza Goreblade (4) — Deathrattle: For the rest of the game, your minions have +1 Attack. (Board — doubt: hand.)
			cards.Add("VAC_426", new CardDef(new Power { DeathrattleTask = new AddEnchantmentTask("VAC_426e", EntityType.CONTROLLER) }));
			cards.Add("VAC_426e", new CardDef(new Power { Aura = new Aura(AuraType.BOARD, "VAC_426e2") }));
			cards.Add("VAC_426e2", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("VAC_426", "attack", 1))) }));
			// [VAC_429] Snow Shredder (4) — Costs (1) if a character is Frozen.
			cards.Add("VAC_429", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => V("VAC_429", "cost", 1), EffectOperator.SET, new SelfCondition(p => AllCharacters(p.Controller).Any(ch => ch.IsFrozen)))
			}));
			// [VAC_436] Brittlebone Buccaneer (2) — Whenever you play a Deathrattle minion, give it Reborn.
			cards.Add("VAC_436", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Minion m && m.HasDeathrattle),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if ((t ?? g.CurrentEventData?.EventSource as IPlayable) is Minion m && m != s) m[GameTag.REBORN] = 1;
					})
				}
			}));
			// [VAC_437] Buttons (4) — Shaman Tourist. Battlecry: Draw a spell of each spell school.
			cards.Add("VAC_437", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var schools = c.DeckZone.GetAll().Where(p => p is Spell && p[SPELL_SCHOOL] > 0).Select(p => p[SPELL_SCHOOL]).Distinct().ToList();
					foreach (int school in RandomOf(g, schools, schools.Count))
						DrawOne(g, c, p => p is Spell && p[SPELL_SCHOOL] == school);
				})
			}));
			// [VAC_445] Ghouls' Night (4) — Summon five 1/1 Ghouls that attack random enemies. (Live 204752 61993: all summons, then
			// each Ghoul attacks a random enemy in summon order.)
			cards.Add("VAC_445", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var ghouls = new List<Minion>();
					for (int i = 0; i < V("VAC_445", "ghouls", 5); i++)
						if (IslandSummon(g, c, Cards.FromId("VAC_445t"), s) is Minion m) ghouls.Add(m);
					foreach (Minion ghoul in ghouls)
					{
						if (ghoul.IsDead || ghoul.ToBeDestroyed || ghoul.Zone?.Type != Zone.PLAY) continue;
						var enemies = AttackableEnemies(c);
						if (enemies.Count == 0) break;
						EffectAttack(g, ghoul, enemies[g.Random.Next(enemies.Count)]);
					}
				})
			}));
			// [VAC_513] Slippery Slope (2) — Freeze a character. Draw a card for each Frozen character.
			cards.Add("VAC_513", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter target) IslandFreeze(g, c, s, target);
					int frozen = AllCharacters(c).Count(ch => ch.IsFrozen);
					for (int i = 0; i < frozen; i++) Generic.Draw(c);
				})
			}));
			// [VAC_514] Dreadhound Handler (2) — Rush. Deathrattle: Summon a 1/1 Dreadhound with Reborn.
			cards.Add("VAC_514", new CardDef(new Power { DeathrattleTask = new SummonTask("VAC_514t") }));
			// [WORK_010] Travel Security (6) — Taunt. Deathrattle: Summon a random 8-Cost minion.
			cards.Add("WORK_010", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, RandomMinionOfCost(g, V("WORK_010", "cost", 8)), s))
			}));
			// [WORK_028] Eternal Layover (4) — Give ALL minions Reborn, then destroy all minions.
			cards.Add("WORK_028", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var all = c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => !m.Untouchable).ToList();
					foreach (Minion m in all) m[GameTag.REBORN] = 1;
					foreach (Minion m in all) if (!m.ToBeDestroyed) Generic.Destroy(m, s);
				})
			}));

			// ---------------------------------------------------------------- Demon Hunter
			// [VAC_501] Aranna, Thrill Seeker (5) — Priest Tourist. Damage your hero takes on your turn is redirected to a random enemy.
			// (Forum t/134107: fatigue too; Health paid isn't damage.)
			cards.Add("VAC_501", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PREDAMAGE)
				{
					TriggerSource = TriggerSource.HERO,
					FastExecution = true,
					Condition = new SelfCondition(p => p.Game.CurrentPlayer == p.Controller && p.Game.CurrentEventData?.EventNumber > 0),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData == null || g.CurrentEventData.EventNumber <= 0) return;
						int amount = g.CurrentEventData.EventNumber;
						var damager = g.CurrentEventData.EventSource as IPlayable ?? (IPlayable)s;
						g.CurrentEventData.EventNumber = 0;
						if (c.Hero.PreDamage != 0) c.Hero.PreDamage = 0;
						var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed).ToList();
						if (enemies.Count > 0) Generic.DamageCharFunc(damager, enemies[g.Random.Next(enemies.Count)], amount, false);
					})
				}
			}));
			// [VAC_925] Sigil of Skydiving (2) — At the start of your next turn, summon two 1/1 Pirates with Charge.
			cards.Add("VAC_925", Sigil("VAC_925", ComplexTask.Repeat(new SummonTask("VAC_926t"), V("VAC_925", "pirates", 2))));
			// [VAC_926] Cliff Dive (6) — Summon 2 minions from your deck and give them Rush. They go back at the end of your turn.
			cards.Add("VAC_926", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("VAC_926", "minions", 2); i++)
						if (SummonFromDeck(g, c, s, p => p is Minion) is Minion m) Give(g, s, m, "VAC_926e");
				})
			}));
			// [VAC_926e] Bungee Jumping — Rush; returns to its owner's deck at the end of the turn (wiki 2025-05-02: a dead one stays).
			cards.Add("VAC_926e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.RUSH, EffectOperator.SET, 1)),
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Enchantment e && e.Target is Minion m && m.Zone?.Type == Zone.PLAY && !m.ToBeDestroyed) Generic.MoveToDeck(m.Controller, e, m);
					})
				}
			}));
			// [VAC_927] Adrenaline Fiend (2) — After a friendly Pirate attacks, give your hero +1 Attack this turn.
			cards.Add("VAC_927", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.MINIONS,
					Condition = new SelfCondition(IsPirate),
					SingleTask = new CustomTask((g, c, s, t, stack) => Give(g, s, c.Hero, "VAC_927e"))
				}
			}));
			cards.Add("VAC_927e", OneTurn(Effects.Attack_N(V("VAC_927", "attack", 1))));
			// [VAC_928] Paraglide (3) — Both players draw 3 cards. Outcast: Only you do.
			cards.Add("VAC_928", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = V("VAC_928", "cards", 3);
					for (int i = 0; i < n; i++) Generic.Draw(c);
					if (!PlayedAsOutcast.Eval((IPlayable)s))
						for (int i = 0; i < n; i++) Generic.Draw(c.Opponent);
				})
			}));
			// [VAC_929] Dangerous Cliffside (4, Location) — Summon two 1/1 Pirates with Charge. After your hero attacks, reopen this.
			cards.Add("VAC_929", new CardDef(new Power
			{
				PowerTask = ComplexTask.Repeat(new SummonTask("VAC_926t"), V("VAC_929", "pirates", 2)),
				Trigger = Reopens(TriggerType.AFTER_ATTACK, TriggerSource.HERO)
			}));
			// [VAC_930] All Terrain Voidhound (7) — Whenever this attacks, give your hero +5 Attack this turn.
			cards.Add("VAC_930", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) => Give(g, s, c.Hero, "VAC_930e", V("VAC_930", "attack", 5)))
				}
			}));
			cards.Add("VAC_930e", new CardDef(new Power { Enchant = new Enchant(GameTag.ATK, EffectOperator.ADD) { IsOneTurnEffect = true } }));
			// [VAC_931] Skirting Death (3) — Choose a minion. This turn, your hero steals 4 Attack from it.
			cards.Add("VAC_931", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					int stolen = Math.Min(V("VAC_931", "attack", 4), Math.Max(0, m.AttackDamage));
					Give(g, s, m, "VAC_931e");
					if (stolen > 0) Give(g, s, c.Hero, "VAC_931e1", stolen);
				})
			}));
			cards.Add("VAC_931e", OneTurn(Effects.Attack_N(-V("VAC_931", "attack", 4))));
			cards.Add("VAC_931e1", new CardDef(new Power { Enchant = new Enchant(GameTag.ATK, EffectOperator.ADD) { IsOneTurnEffect = true } }));
			// [VAC_932] Climbing Hook (6, weapon) — Doesn't lose Durability while you control a minion with 5 or more Attack.
			cards.Add("VAC_932", new CardDef(new Power
			{
				Aura = new AdaptiveEffect(new SelfCondition(w => w.Controller.BoardZone.GetAll().Any(m => m.AttackDamage >= V("VAC_932", "attack", 5))), HsTag.KEEPS_DURABILITY)
			}));
			// [VAC_933] Patches the Pilot (1) — Battlecry: Shuffle six Parachutes into your deck that summon a 1/1 Pirate with Charge
			// when drawn.
			cards.Add("VAC_933", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("VAC_933", "parachutes", 6); i++)
						Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("VAC_933t"), new EntityData { [GameTag.CREATOR] = s.Id }));
				})
			}));
			cards.Add("VAC_933t", new CardDef(new Power { PowerTask = new SummonTask("VAC_926t"), TopdeckTask = new SummonTask("VAC_926t") }));
			// [WORK_014] Demonic Deal (2) — Lifesteal. Deal $4 damage to a minion. Put a random Demon that costs (5) or more on top of
			// your deck.
			cards.Add("WORK_014", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("WORK_014", "damage", 4), EntityType.TARGET, true),
					new CustomTask((g, c, s, t, stack) =>
						Generic.PutOnTop(c, s, RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.DEMON) && x.Cost >= V("WORK_014", "cost", 5)))))
			}));
			// [WORK_015] Spirit Peddler (6) — Rush. Deathrattle: Reduce the Cost of a random minion in your hand by (6).
			cards.Add("WORK_015", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.HandZone.GetAll().Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.Count > 0) Give(g, s, minions[g.Random.Next(minions.Count)], "WORK_015e");
				})
			}));
			cards.Add("WORK_015e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("WORK_015", "discount", 6))) }));
			// [WORK_016] Infernal Stapler (3, weapon) — After your hero attacks, deal 3 damage to your hero.
			cards.Add("WORK_016", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = new DamageTask(V("WORK_016", "damage", 3), EntityType.HERO) }
			}));
		}
	}
}
