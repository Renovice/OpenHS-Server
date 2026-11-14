// HsServer addition (not upstream SabberStone): Showdown in the Badlands / Delve into Deepholm — chunk 3: Paladin, Priest and
// Rogue cards (spec: research spec_chunk3; shared pieces: WildWestCardsGenHs.cs, Actions/WildWest.cs). Every number goes through
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
		/// <summary>A new card of <paramref name="card"/> to the hand with extra tags (CREATOR = the source); the card in hand or null.</summary>
		private static IPlayable WildWestC3ToHand(Controller c, IEntity source, Card card, EntityData tags = null)
		{
			if (card == null || c.HandZone.IsFull) return null;
			tags ??= new EntityData();
			tags[GameTag.CREATOR] = source.Id;
			IPlayable p = Entity.FromCard(c, card, tags);
			return Generic.AddHandPhase.Invoke(c, p) ? p : null;
		}

		/// <summary>One random card of the other classes matching <paramref name="where"/> (never Neutral: Generic.FromAnotherClass).</summary>
		private static Card WildWestC3OtherClass(Game g, Controller c, System.Func<Card, bool> where) =>
			Pick(g, OtherClassCards(g, c).Where(where), 1).FirstOrDefault();

		private static void WildWestChunk3(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Delve into Deepholm
			// [DEEP_014] Quick Pick (2, weapon) — After your hero attacks, draw a card. (Live dredge 7044: effect index 1.)
			cards.Add("DEEP_014", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = new DrawTask(), EffectIndex = 1 }
			}));
			// [DEEP_021] Shadow Word: Steal (5) — Return an enemy minion to YOUR hand. (Generic.ReturnToYourHand; live options:
			// enemy minions only.)
			cards.Add("DEEP_021", new CardDef(TargetEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m) Generic.ReturnToYourHand(c, m); })
			}));
			// [DEEP_022] Fool's Gold (1) — Get a random Golden Pirate and Elemental from other classes. (PREMIUM = 1: "Golden".)
			cards.Add("DEEP_022", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Race race in new[] { Race.PIRATE, Race.ELEMENTAL })
						WildWestC3ToHand(c, s, WildWestC3OtherClass(g, c, x => x.Type == CardType.MINION && x.IsRace(race)), new EntityData { [GameTag.PREMIUM] = 1 });
				})
			}));
			// [DEEP_023] Hidden Gem (2) — Stealth. At the end of your turn, restore 2 Health to all friendly characters.
			cards.Add("DEEP_023", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new HealTask(V("DEEP_023", "heal", 2), EntityType.FRIENDS) }
			}));
			// [DEEP_025] Shattered Reflections (5) — Choose a minion. Add a copy to your hand, deck, and battlefield. (Live
			// c3_li_1003_185040 111636: battlefield, then deck, then hand.)
			cards.Add("DEEP_025", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					if (!c.BoardZone.IsFull) Generic.Copy(c, s, m, Zone.PLAY, position: m.Controller == c && m.Zone?.Type == Zone.PLAY ? m.ZonePosition + 1 : -1);
					if (!c.DeckZone.IsFull) Generic.Copy(c, s, m, Zone.DECK);
					if (!c.HandZone.IsFull) Generic.Copy(c, s, m, Zone.HAND);
				})
			}));
			// [DEEP_026] Pendant of Earth (3) — Discover a minion from your deck. Restore Health to your hero equal to its Cost.
			cards.Add("DEEP_026", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.DiscoverFromDeck(c, s, Generic.PickFromDeck(c, p => p is Minion), (g2, c2, s2, card) =>
					{
						int cost = card.Cost;
						Generic.Draw(c2, card);
						new HealTask(cost, EntityType.HERO).Process(g2, c2, s2, null);
					}))
			}));

			// ---------------------------------------------------------------- Paladin
			// [WW_051] Showdown! (2) — Both players summon three 3/3 Outlaws. Give yours Rush.
			cards.Add("WW_051", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = V("WW_051", "outlaws", 3);
					for (int i = 0; i < n && !c.BoardZone.IsFull; i++)
						Generic.SummonBlock.Invoke(g, (Minion)Entity.FromCard(c, Cards.FromId("WW_051t"), new EntityData { [GameTag.RUSH] = 1 }), -1, s);
					for (int i = 0; i < n && !c.Opponent.BoardZone.IsFull; i++)
						Generic.SummonBlock.Invoke(g, (Minion)Entity.FromCard(c.Opponent, Cards.FromId("WW_051t")), -1, s);
				})
			}));
			// [WW_335] Holy Cowboy (3) — Battlecry: Your next Holy spell costs (2) less.
			cards.Add("WW_335", new CardDef(new Power { PowerTask = new AddEnchantmentTask("WW_335e", EntityType.CONTROLLER) }));
			cards.Add("WW_335e", NextCardCost(Effects.ReduceCost(V("WW_335", "discount", 2)), new SelfCondition(p => p is Spell && p[SPELL_SCHOOL] == HOLY), "GBL_002e"));
			// [WW_336] Prismatic Beam (7) — Deal $3 damage to all enemies. Costs (1) less for each enemy minion.
			cards.Add("WW_336", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.Opponent.BoardZone.GetAll().Count(m => !m.IsDead)),
				PowerTask = new DamageTask(V("WW_336", "damage", 3), EntityType.ENEMIES, true)
			}));
			// [WW_337] Spirit of the Badlands (3) — Battlecry: If your deck started with no duplicates, get a permanent Mirage.
			cards.Add("WW_337", new CardDef(new Power
			{
				PowerTask = WildWestIfNoDuplicates(new CustomTask((g, c, s, t, stack) =>
					Generic.NamedFx((IPlayable)s, "SpawninHand", new[] { c.Hero.Id }, () => AddToHand(c, Cards.FromId("WW_337t")))))
			}));
			// [WW_337t] Mirage — At the start of your turn, transform into a copy of a minion in your deck. (This stays in your hand.)
			// (Generic.ShiftInHand with WW_337e; a played shifted card leaves a new Mirage: Generic.StaysInHandVia. Its own deck at
			// shift time — wiki Mirage patch 28.6.0.)
			ISimpleTask mirage = new CustomTask((g, c, s, t, stack) =>
				Generic.ShiftInHand(s, me =>
				{
					var minions = me.Controller.DeckZone.Where(p => Generic.IsMinionCard(p)).ToList();
					return minions.Count == 0 ? null : minions[g.Random.Next(minions.Count)].Card;
				}, Cards.FromId("WW_337e")));
			cards.Add("WW_337t", new CardDef(new Power
			{
				PlayableIf = (PlayReq.REQ_LITERALLY_UNPLAYABLE, ctl => false),
				Trigger = new Trigger(TriggerType.TURN_START) { TriggerActivation = TriggerActivation.HAND, SingleTask = mirage }
			}));
			cards.Add("WW_337e", new CardDef(new Power
			{
				Enchant = new Enchant(GameTag.SHIFTING_MINION, EffectOperator.SET, 1) { RemoveWhenPlayed = true },
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = mirage }
			}));
			// [WW_341] Deputization Aura (3) — Your left-most minion has +2 Attack and Lifesteal. Lasts 3 turns. (Wiki: "If that minion
			// dies or moves position, the effect reassigns to the new leftmost minion".)
			cards.Add("WW_341", new CardDef(PaladinAura(new Power
			{
				Aura = new Aura(AuraType.BOARD, "WW_341e")
				{
					Condition = new SelfCondition(p => p is Minion m && p.Controller.BoardZone.GetAll().FirstOrDefault() == m),
					Restless = true
				}
			})));
			cards.Add("WW_341e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("WW_341", "attack", 2)), Effects.Lifesteal) }));
			// [WW_342] Lawful Longarm (5) — Rush, Lifesteal. Battlecry: Gain +1 Attack for each card in your hand. (Live: one WW_342e.)
			cards.Add("WW_342", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = c.HandZone.Count * V("WW_342", "attack", 1);
					if (n > 0 && s is Minion me) Generic.AddEnchantmentBlock(g, Cards.FromId("WW_342e"), me, me, n, 0, 0);
				})
			}));
			cards.Add("WW_342e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackScriptTag }));
			// [WW_344] Hi Ho Silverwing (2) — Divine Shield. Deathrattle: Draw a Holy spell.
			cards.Add("WW_344", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => DrawOne(g, c, p => p is Spell && p[SPELL_SCHOOL] == HOLY))
			}));
			// [WW_345] The Badlands Bandits (6) — Get eight 3/2 Bandits with Bonus Effects. Any that can't fit in your hand are summoned
			// instead. (Wiki: "generated in a random order".)
			cards.Add("WW_345", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var bandits = Enumerable.Range(1, 8).Select(i => Cards.TryFromId($"WW_345t{i}")).Where(x => x != null).ToList();
					for (int i = bandits.Count - 1; i > 0; i--) { int j = g.Random.Next(i + 1); (bandits[i], bandits[j]) = (bandits[j], bandits[i]); }
					Generic.NamedFx((IPlayable)s, "OnHero", new[] { c.Hero.Id }, () =>
					{
						foreach (Card bandit in bandits)
						{
							if (!c.HandZone.IsFull) AddToHand(c, bandit);
							else if (!c.BoardZone.IsFull) Summon(g, c, bandit, s);
						}
					});
				})
			}));
			// [WW_365] Lay Down the Law (2) — Tradeable. Set a minion's Attack and Health to 1. Quickdraw: Then deal $1 damage to it.
			cards.Add("WW_365", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("WW_365e", EntityType.TARGET),
					WildWestIfQuickdraw(new DamageTask(V("WW_365", "damage", 1), EntityType.TARGET, true)))
			}));
			cards.Add("WW_365e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttack(V("WW_365", "stats", 1)), Effects.SetMaxHealth(V("WW_365", "stats", 1))) }));
			// [WW_366] Living Horizon (10) — Taunt, Divine Shield. Costs (1) less for each other card in your hand.
			cards.Add("WW_366", new CardDef(new Power { Aura = new AdaptiveCostEffect(p => p.Controller.HandZone.Count(x => x != p)) }));

			// ---------------------------------------------------------------- Priest
			// [WW_052] Swarm of Lightbugs (5) — Summon 10 1/1 Lightbugs with Lifesteal. Save any excess in a 1-Cost Bottle.
			cards.Add("WW_052", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int want = V("WW_052", "lightbugs", 10), summoned = 0;
					for (; summoned < want && !c.BoardZone.IsFull; summoned++) Summon(g, c, Cards.FromId("WW_052t"), s);
					Generic.Bottle(c, s, Cards.FromId("WW_052t2"), want - summoned);
				})
			}));
			// [WW_052t2] Bottled Lightbugs (1) — Summon {0} 1/1 Lightbugs with Lifesteal.
			cards.Add("WW_052t2", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < Generic.BottledAmount(s) && !c.BoardZone.IsFull; i++) Summon(g, c, Cards.FromId("WW_052t"), s);
				})
			}));
			// [WW_053] Tram Heist (4) — Get a copy of each card your opponent played last turn. (Their plays only — PlayHistory; live:
			// COPIED_FROM_ENTITY_ID / LINKED_ENTITY = the played card.)
			cards.Add("WW_053", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (PlayHistoryEntry h in c.Opponent.PlayHistory.Where(h => h.Turn == g.Turn - 1 && h.SourceCard != null).ToList())
					{
						if (c.HandZone.IsFull) break;
						WildWestC3ToHand(c, s, h.SourceCard, new EntityData { [HsTag.COPIED_FROM_ENTITY_ID] = h.SourceId, [GameTag.LINKED_ENTITY] = h.SourceId });
					}
				})
			}));
			// [WW_381] Injured Hauler (3) — Battlecry: Deal 4 damage to this minion. Overheal: Deal 2 damage to all enemy minions.
			cards.Add("WW_381", new CardDef(new Power
			{
				PowerTask = new DamageTask(V("WW_381", "self damage", 4), EntityType.SOURCE),
				Trigger = new Trigger(TriggerType.OVERHEAL) { TriggerSource = TriggerSource.SELF, SingleTask = new DamageTask(V("WW_381", "damage", 2), EntityType.OP_MINIONS) }
			}));
			// [WW_384] Benevolent Banker (3) — Battlecry: Discover a spell from your deck. Quickdraw: Enemy deck instead. (The enemy's
			// card is stolen: Generic.GiveTo with HAND — live CONTROLLER_AND_ZONE_CHANGE.)
			cards.Add("WW_384", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					bool quickdraw = SelfCondition.IsQuickdraw.Eval((IPlayable)s);
					var deck = quickdraw ? c.Opponent.DeckZone : c.DeckZone;
					Generic.DiscoverFromDeck(c, s, Generic.PickFrom(deck, g, p => p is Spell), (g2, c2, s2, card) =>
					{
						if (!quickdraw) { Generic.Draw(c2, card); return; }
						if (c2.HandZone.IsFull) return;
						Generic.AddHandPhase.Invoke(c2, Generic.GiveTo(card, c2, Zone.HAND));
					});
				})
			}));
			// [WW_387] Thirsty Drifter (6) — Taunt. Costs (1) less for each 1-Cost card you've played this game. (The cost it was played
			// for: PlayHistoryEntry.Cost.)
			cards.Add("WW_387", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.PlayHistory.Count(h => h.Cost == V("WW_387", "cost", 1)))
			}));
			// [WW_392] Elise, Badlands Savior (8) — Battlecry: If your deck started with no duplicates, summon 5/5 copies of 4 random
			// minions in your deck.
			cards.Add("WW_392", new CardDef(new Power
			{
				PowerTask = WildWestIfNoDuplicates(new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable m in RandomOf(g, c.DeckZone.Where(p => Generic.IsMinionCard(p)), V("WW_392", "minions", 4)))
					{
						if (c.BoardZone.IsFull) break;
						Generic.Copy(c, s, m, Zone.PLAY, prepare: copy => Give(g, s, copy, "WW_392e"));
					}
				}))
			}));
			cards.Add("WW_392e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttack(V("WW_392", "stats", 5)), Effects.SetMaxHealth(V("WW_392", "stats", 5))) }));
			// [WW_393] Invasive Shadeleaf (4) — Deal $10 damage to an enemy minion. Save any excess in a 1-Cost Bottle. (Excess = the
			// damage past its Health.)
			cards.Add("WW_393", new CardDef(TargetEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					int excess = Generic.DamageWithExcess((IPlayable)s, m, V("WW_393", "damage", 10), true); // (the shared capped excess hit)
					Generic.Bottle(c, s, Cards.FromId("WW_393t"), excess);
				})
			}));
			// [WW_393t] Bottled Shadeleaf (1) — Deal ${0} damage to an enemy minion.
			cards.Add("WW_393t", new CardDef(TargetEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m) Generic.DamageCharFunc((IPlayable)s, m, Generic.BottledAmount(s), true); })
			}));
			// [WW_394] Pip the Potent (3) — Battlecry: Copy each 1-Cost card in your hand.
			cards.Add("WW_394", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in c.HandZone.Where(x => x.Cost == V("WW_394", "cost", 1)).ToList())
					{
						if (c.HandZone.IsFull) break;
						Generic.Copy(c, s, p, Zone.HAND);
					}
				})
			}));
			// [WW_395] Holy Springwater (2) — Restore 8 Health to a damaged character. Save any excess in a 1-Cost Bottle. (Live
			// c3_li_1003_185040 97697: the heal past full Health is bottled.)
			cards.Add("WW_395", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_DAMAGED_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int excess = Generic.OverhealedDuring(c, () => new HealTask(V("WW_395", "heal", 8), EntityType.TARGET).Process(g, c, s, t));
					Generic.Bottle(c, s, Cards.FromId("WW_395t"), excess);
				})
			}));
			// [WW_395t] Bottled Springwater (1) — Restore #{0} Health.
			cards.Add("WW_395t", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => new HealTask(Generic.BottledAmount(s), EntityType.TARGET).Process(g, c, s, t))
			}));
			// [WW_600] Posse Possession (4) — Summon a 4/4 copy of a random minion in your opponent's hand.
			cards.Add("WW_600", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.Opponent.HandZone.Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.Count == 0 || c.BoardZone.IsFull) return;
					Generic.Copy(c, s, minions[g.Random.Next(minions.Count)], Zone.PLAY, prepare: copy => Give(g, s, copy, "WW_600e"));
				})
			}));
			cards.Add("WW_600e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttack(V("WW_600", "stats", 4)), Effects.SetMaxHealth(V("WW_600", "stats", 4))) }));

			// ---------------------------------------------------------------- Rogue
			// [WW_006] Dart Throw (2) — Throw two $2 damage darts at random enemy minions. (If both hit the same minion, get a Coin!)
			cards.Add("WW_006", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var hits = new List<int>();
					for (int i = 0; i < V("WW_006", "darts", 2); i++)
					{
						var enemies = c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable && m.Health > 0).ToList();
						if (enemies.Count == 0) break;
						Minion hit = enemies[g.Random.Next(enemies.Count)];
						g.PowerHistory?.NewTargetRound();
						Generic.DamageCharFunc((IPlayable)s, hit, V("WW_006", "damage", 2), true);
						hits.Add(hit.Id);
					}
					if (hits.Count == 2 && hits[0] == hits[1]) AddToHand(c, Cards.FromId("GAME_005"));
				})
			}));
			// [WW_363] Bounty Wrangler (3) — Quickdraw or Combo: Get a Coin. (The Combo play runs ComboTask instead.)
			cards.Add("WW_363", new CardDef(new Power
			{
				PowerTask = WildWestIfQuickdraw(new AddCardTo("GAME_005", EntityType.HAND)),
				ComboTask = new AddCardTo("GAME_005", EntityType.HAND)
			}));
			// [WW_364] Velarok Windblade (3) — While this is in your hand, play a card from another class to reveal Velarok's true form!
			// (Its trigger effect index 0: WWFX_VelarokWindrunner_TransformImpact; hand buffs kept — wiki patch 28.4.0.)
			cards.Add("WW_364", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerActivation = TriggerActivation.HAND,
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => Generic.FromAnotherClass(p.Card, p.Controller)),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is IPlayable me && me.Zone?.Type == Zone.HAND) Generic.ChangeInHand(c, me, Cards.FromId("WW_364t"), 0);
					})
				}
			}));
			// [WW_364t] Velarok, the Deceiver — Charge. After this attacks, Discover a card from another class. It costs (3) less.
			cards.Add("WW_364t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new DiscoverFromTask((g, c, s) => OtherClassCards(g, c), "WW_364te")
				}
			}));
			cards.Add("WW_364te", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("WW_364t", "discount", 3))) }));
			// [WW_410] Triple Sevens (7) — Deal $7 damage to a minion. Draw 7 cards.
			cards.Add("WW_410", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("WW_410", "damage", 7), EntityType.TARGET, true), ComplexTask.Repeat(new DrawTask(), V("WW_410", "draw", 7)))
			}));
			// [WW_411] Stick Up (1) — Discover a Quickdraw card from another class.
			cards.Add("WW_411", new CardDef(new Power
			{
				PowerTask = new DiscoverFromTask((g, c, s) => OtherClassCards(g, c).Where(x => x[HsTag.QUICKDRAW] > 0))
			}));
			// [WW_415] Wishing Well (5) — After you play a Coin, get a random Legendary minion from another class and set its Cost to (1).
			cards.Add("WW_415", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => Generic.IsCoin(p)),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (WildWestC3ToHand(c, s, WildWestC3OtherClass(g, c, x => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY)) is IPlayable got)
							Give(g, s, got, "GBL_001e");
					})
				}
			}));
			// [WW_416] Shell Game (2) — Get a random Epic, Rare, and Common card from other classes. (Live ×2: in that order.)
			cards.Add("WW_416", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Rarity r in new[] { Rarity.EPIC, Rarity.RARE, Rarity.COMMON })
						WildWestC3ToHand(c, s, WildWestC3OtherClass(g, c, x => x.Rarity == r));
				})
			}));
		}
	}
}
