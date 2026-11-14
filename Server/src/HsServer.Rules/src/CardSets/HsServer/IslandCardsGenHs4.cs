// HsServer addition (not upstream SabberStone): Perils in Paradise — chunk 3: Rogue, Priest and Paladin cards and the Travel
// Agency's (spec: research spec_chunk3; shared pieces: IslandCardsGenHs.cs, Actions/Paradise.cs). Every number goes through
// CardValues (V).
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
		/// <summary>Eudora's loot (client tag 3431 marks the Treasure Hunt's Discover pool) and the Kazakusan Treasures with the
		/// same name, cost, stats and text: the loot plays their scripts.</summary>
		private static readonly (string Loot, string Treasure)[] EudoraLoot =
		{
			("VAC_464t2", "ONY_005ta1"), ("VAC_464t3", "ONY_005ta2"), ("VAC_464t4", "ONY_005ta3"), ("VAC_464t5", "ONY_005ta4"),
			("VAC_464t6", "ONY_005ta5"), ("VAC_464t7", "ONY_005ta6"), ("VAC_464t8", "ONY_005ta7"), ("VAC_464t9", "ONY_005ta8"),
			("VAC_464t10", "ONY_005ta10"), ("VAC_464t11", "ONY_005ta11"), ("VAC_464t12", "ONY_005ta13"), ("VAC_464t14", "ONY_005tb1"),
			("VAC_464t15", "ONY_005tb2"), ("VAC_464t16", "ONY_005tb4"), ("VAC_464t17", "ONY_005tb5"), ("VAC_464t18", "ONY_005tb7"),
			("VAC_464t19", "ONY_005tb8"), ("VAC_464t20", "ONY_005tb9"), ("VAC_464t21", "ONY_005tb12"), ("VAC_464t22", "ONY_005tb13"),
			("VAC_464t23", "ONY_005tc1"), ("VAC_464t24", "ONY_005tc2"), ("VAC_464t25", "ONY_005tc3"), ("VAC_464t26", "ONY_005ta12"),
			("VAC_464t27", "ONY_005ta9"), ("VAC_464t28", "ONY_005tb14"), ("VAC_464t29", "ONY_005tc5"), ("VAC_464t30", "ONY_005tc6"),
			("VAC_464t31", "ONY_005tc7"),
		};

		/// <summary>Eudora's reward: two Discovers of her loot (the second without the first pick); Blade + Hilt forge Quel'Delar.</summary>
		private static void EudoraReward(Game g, Controller c, IPlayable side)
		{
			var pool = Cards.All.Where(x => x[(GameTag)3431] > 0).ToList();
			Card[] first = DiscoverFromTask.PickDistinct(g, pool, 3).ToArray();
			if (first.Length == 0) return;
			Generic.CreateChoiceCards.Invoke(c, side, null, ChoiceType.GENERAL, ChoiceAction.HAND, first, new CustomTask((g2, c2, s2, one, st) =>
			{
				Card[] second = DiscoverFromTask.PickDistinct(g2, pool.Where(x => x != one?.Card), 3).ToArray();
				Generic.CreateChoiceCards.Invoke(c2, s2, null, ChoiceType.GENERAL, ChoiceAction.HAND, second, new CustomTask((g3, c3, s3, two, st3) =>
					ForgeQuelDelar(g3, c3, s3, "VAC_464t29", "VAC_464t30", "VAC_464t31t")));
			}));
		}

		/// <summary>Brochure flip partner text: the shared "(Flips each turn.)" (Generic.HarmonicPartner).</summary>
		private static void IslandChunk3(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Rogue
			// [VAC_330] Metal Detector (3, weapon) — Deathrattle: Get a Coin. (A replaced weapon's Deathrattle still runs: rule book.)
			cards.Add("VAC_330", new CardDef(new Power { DeathrattleTask = new AddCardTo("GAME_005", EntityType.HAND) }));
			// [VAC_332] Sea Shill (3) — Battlecry: The next card you play from another class costs (2) less.
			cards.Add("VAC_332", new CardDef(new Power { PowerTask = new AddEnchantmentTask("VAC_332e", EntityType.CONTROLLER) }));
			cards.Add("VAC_332e", NextCardCost(Effects.ReduceCost(V("VAC_332", "discount", 2)), new SelfCondition(p => Generic.IsFromAnotherClass(p.Controller, p.Card)), "GBL_002e"));
			// [VAC_333] Conniving Conman (4) — Battlecry: Replay the last card you played from another class. (Forum t/135926: a new
			// copy, no Battlecry.)
			cards.Add("VAC_333", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var last = Generic.PlayedFromAnotherClass(c).LastOrDefault(h => h.SourceId != s.Id);
					if (last.SourceCard != null) Generic.RepeatCard(g, c, s, last.SourceCard);
				})
			}));
			// [VAC_334] Knickknack Shack (3, Location) — Draw a card. If you play it this turn, reopen this.
			cards.Add("VAC_334", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(Generic.Draw(c) is IPlayable drawn) || drawn.Zone?.Type != Zone.HAND) return;
					Generic.MarkPlayThisTurn(drawn, s);
					s[HsTag.DREW_CARD] = drawn.Id;
					s[HsTag.DREW_ON_TURN] = g.Turn;
				}),
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.Controller.BoardZone.GetAll().Any(l => l is Location loc && loc[HsTag.DREW_CARD] == p.Id && loc[HsTag.DREW_ON_TURN] == p.Game.Turn)),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable played = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (played == null || s[HsTag.DREW_CARD] != played.Id || s[HsTag.DREW_ON_TURN] != g.Turn) return;
						s[HsTag.DREW_CARD] = 0;
						Generic.Reopen(s);
					})
				}
			}));
			// [VAC_335] Petty Theft (1) — Get two random 1-Cost spells from other classes.
			cards.Add("VAC_335", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var pool = OtherClassCards(g, c).Where(x => x.Type == CardType.SPELL && x.Cost == V("VAC_335", "cost", 1)).ToList();
					for (int i = 0; i < V("VAC_335", "spells", 2) && pool.Count > 0; i++) AddToHand(c, pool[g.Random.Next(pool.Count)]);
				})
			}));
			// [VAC_336] Maestra, Mask Merchant (2) — Warlock Tourist. Battlecry: Discover a Hero card from the past (from another class).
			cards.Add("VAC_336", new CardDef(new Power
			{
				PowerTask = new DiscoverFromTask((g, c, s) => Cards.AllWild.Where(x => x.Collectible && x.Type == CardType.HERO && x.Cost > 0 &&
					Cards.FromThePast(x) && Generic.IsFromAnotherClass(c, x)).GroupBy(x => x.Name).Select(grp => grp.First()))
			}));
			// [VAC_460] Oh, Manager! (2) — Deal $2 damage. Combo: Get a Coin.
			cards.Add("VAC_460", new CardDef(TargetAny, new Power
			{
				PowerTask = new DamageTask(V("VAC_460", "damage", 2), EntityType.TARGET, true),
				ComboTask = ComplexTask.Create(new DamageTask(V("VAC_460", "damage", 2), EntityType.TARGET, true), new AddCardTo("GAME_005", EntityType.HAND))
			}));
			// [VAC_464] Treasure Hunter Eudora (4) — Battlecry: Go on a Sidequest to Discover amazing loot! Play 3 cards from other
			// classes to complete it.
			cards.Add("VAC_464", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card hunt = Cards.FromId("VAC_464t");
					if (c.SecretZone.Any(x => x.Card == hunt)) return; // one copy of a Sidequest at a time (wiki Sidequest)
					Generic.PutSecretIntoPlay(c, (Spell)Entity.FromCard(c, hunt, new EntityData { [GameTag.CREATOR] = s.Id }));
				})
			}));
			cards.Add("VAC_464t", Sidequest(TriggerType.AFTER_PLAY_CARD, TriggerSource.FRIENDLY,
				(g, c, t) => (t ?? g.CurrentEventData?.EventSource as IPlayable) is IPlayable played && Generic.IsFromAnotherClass(c, played.Card) ? 1 : 0,
				EudoraReward));
			foreach (var (loot, treasure) in EudoraLoot)
				if (cards.TryGetValue(treasure, out CardDef def)) cards.Add(loot, def);
			// [VAC_464t31t] Forging Quel'Delar — the pieces leave your hand and deck; Quel'Delar into your hand (one: doubt).
			cards.Add("VAC_464t31t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in QuelDelarPieces(c, "VAC_464t29", "VAC_464t30")) c.SetasideZone.Add(p.Zone.Remove(p));
					IslandToHand(c, s, Cards.FromId("VAC_464t31"));
				})
			}));
			// [VAC_700] Snatch and Grab (8) — Destroy two random enemy minions. Costs (1) less for each card you've played from another
			// class.
			cards.Add("VAC_700", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => Generic.PlayedFromAnotherClass(p.Controller).Count()),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in RandomOf(g, c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable), V("VAC_700", "minions", 2)))
						Generic.Destroy(m, s);
				})
			}));
			// [VAC_701] Swarthy Swordshiner (3) — Battlecry: Set the Attack and Durability of your weapon to 3.
			cards.Add("VAC_701", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(c.Hero.Weapon is Weapon w)) return;
					Give(g, s, w, "VAC_701e");
					w.Damage = 0;
				})
			}));
			cards.Add("VAC_701e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.SetAttack(V("VAC_701", "stats", 3)), new Effect(GameTag.DURABILITY, EffectOperator.SET, V("VAC_701", "stats", 3)))
			}));
			// [WORK_004] Agency Espionage (4) — Shuffle a card from each other class into your deck. They cost (1). Draw one.
			cards.Add("WORK_004", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var shuffled = new List<IPlayable>();
					foreach (CardClass cls in Cards.HeroClasses.Where(x => x != c.HeroClass))
					{
						if (c.DeckZone.IsFull) break;
						Card card = RandomCard(g, x => x.Class == cls && !x.HasClass(c.HeroClass) && !x.HasClass(CardClass.NEUTRAL));
						if (card == null) continue;
						IPlayable p = Entity.FromCard(c, card, new EntityData { [GameTag.CREATOR] = s.Id });
						Give(g, s, p, "WORK_004e");
						Generic.ShuffleIntoDeck.Invoke(c, s, p);
						shuffled.Add(p);
					}
					var inDeck = shuffled.Where(p => p.Zone?.Type == Zone.DECK).ToList();
					if (inDeck.Count > 0) Generic.Draw(c, inDeck[g.Random.Next(inDeck.Count)]);
				})
			}));
			cards.Add("WORK_004e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(V("WORK_004", "cost", 1))) }));
			// [WORK_005] Sharp Shipment (4) — Give your weapon +2/+2.
			cards.Add("WORK_005", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (c.Hero.Weapon is Weapon w) Give(g, s, w, "WORK_005e"); })
			}));
			cards.Add("WORK_005e", new CardDef(new Power { Enchant = new Enchant(WeaponStats(V("WORK_005", "stats", 2))) }));
			// [WORK_006] Robocaller (3) — Battlecry: Draw an {0}, {1}, and {2}-Cost card. (Numbers dialed randomly each turn!) (Wiki:
			// 8, 8, 8 at first; each of its owner's turn starts in hand, three new numbers 0-9.)
			cards.Add("WORK_006", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (GameTag tag in new[] { GameTag.TAG_SCRIPT_DATA_NUM_1, GameTag.TAG_SCRIPT_DATA_NUM_2, (GameTag)2889 })
					{
						int cost = s[tag];
						DrawOne(g, c, p => p.Cost == cost);
					}
				}),
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					TriggerActivation = TriggerActivation.HAND,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						foreach (GameTag tag in new[] { GameTag.TAG_SCRIPT_DATA_NUM_1, GameTag.TAG_SCRIPT_DATA_NUM_2, (GameTag)2889 })
							s[tag] = g.Random.Next(V("WORK_006", "highest", 9) + 1);
					})
				}
			}));

			// ---------------------------------------------------------------- Priest
			// [VAC_414] Hot Coals (3) — Deal $2 damage to all enemies. If your hero took damage this turn, deal $1 more. (Live
			// yogg-cthun 57721: two waves.)
			cards.Add("VAC_414", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("VAC_414", "damage", 2), EntityType.ENEMIES, true),
					ComplexTask.If((g, c, s) => c[HsTag.HERO_DAMAGE_THIS_TURN] > 0, new DamageTask(V("VAC_414", "more", 1), EntityType.ENEMIES, true)))
			}));
			// [VAC_417] Sensory Deprivation (6) — Summon a copy of an enemy minion. If you have 20 or less Health, destroy the original.
			cards.Add("VAC_417", new CardDef(TargetEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					if (!c.BoardZone.IsFull) Generic.Copy(c, s, m, Zone.PLAY);
					if (c.Hero.Health <= V("VAC_417", "health", 20) && !m.ToBeDestroyed) Generic.Destroy(m, s);
				})
			}));
			// [VAC_418] Sauna Regular (5) — Taunt. Costs (1) less for each time your hero has taken damage on your turn. (Forum
			// t/132645: counted all game, not while held — client tag 3687.)
			cards.Add("VAC_418", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => V("VAC_418", "per hit", 1) * p.Controller[Generic.HERO_DAMAGED_TIMES_ON_OWN_TURNS])
			}));
			// [VAC_419] Acupuncture (1) — Deal $4 damage to both heroes. (Live install-0929 110567: your hero first.)
			cards.Add("VAC_419", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("VAC_419", "damage", 4), EntityType.HERO, true), new DamageTask(V("VAC_419", "damage", 4), EntityType.OP_HERO, true))
			}));
			// [VAC_420] Narain Soothfancy (4) — Battlecry: Get two Fortunes that are copies of the top card of your deck.
			cards.Add("VAC_420", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card marker = Cards.FromId("VAC_420e1");
					for (int i = 0; i < V("VAC_420", "fortunes", 2); i++)
					{
						Card copyOf = c.DeckZone.Count > 0 ? c.DeckZone.TopCard.Card : Cards.FromId("VAC_420t");
						if (!(IslandToHand(c, s, copyOf) is IPlayable fortune)) break;
						fortune[HsTag.FORTUNE] = marker.AssetId;
						Generic.AddEnchantmentBlock(g, marker, (IPlayable)s, fortune, 0, 0, 0);
					}
				})
			}));
			cards.Add("VAC_420e1", new CardDef(new Power { Enchant = new Enchant(new Effect((GameTag)438, EffectOperator.SET, 1)) { RemoveWhenPlayed = true } }));
			cards.Add("VAC_420t", new CardDef(new Power { PlayableIf = (PlayReq.REQ_LITERALLY_UNPLAYABLE, ctl => false) }));
			// [VAC_423] Twilight Medium (5) — Taunt. Battlecry: Set the Cost of the top card of your deck to (0).
			cards.Add("VAC_423", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (c.DeckZone.Count > 0) Give(g, s, c.DeckZone.TopCard, "VAC_423e4"); })
			}));
			cards.Add("VAC_423e4", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(V("VAC_423", "cost", 0))) }));
			// [VAC_457] Rest in Peace (3) — Each player summons their highest Cost minion that died this game. (Forum t/136669: only
			// minions that died.)
			cards.Add("VAC_457", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Controller who in new[] { c, c.Opponent })
					{
						var dead = who.DiedThisGameCards.Where(p => p is Minion).ToList();
						if (dead.Count == 0 || who.BoardZone.IsFull) continue;
						int top = dead.Max(p => p.Card.Cost);
						var best = dead.Where(p => p.Card.Cost == top).ToList();
						Generic.SummonBlock.Invoke(g, (Minion)Generic.NewFromGraveyard(who, best[g.Random.Next(best.Count)]), -1, s);
					}
				})
			}));
			// [VAC_512] Brain Masseuse (1) — Whenever this minion takes damage, also deal that amount to your hero.
			cards.Add("VAC_512", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						int amount = g.CurrentEventData?.EventNumber ?? 0;
						if (amount > 0) Generic.DamageCharFunc((IPlayable)s, c.Hero, amount, false);
					})
				}
			}));
			// [VAC_957] Chillin' Vol'jin (3) — Hunter Tourist. Battlecry: Choose 2 minions. Swap their stats. (Live, 3 plays: two
			// TARGET picks inside the block, Vol'jin excluded; Mimiron's Switch shape.)
			cards.Add("VAC_957", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int self = s.Id; // (values only: the pick may resolve on an AI's copy — Generic.PickTargetsInOrder)
					Generic.PickTargetsInOrder(c, s, 2, ch => ch is Minion m && m.Id != self && !m.Untouchable && Generic.Pickable(m.Game.CurrentPlayer, m, false), (g2, c2, s2, pick, before) =>
					{
						if (before.Count < 1 || !(pick is Minion second) || !(g2.IdEntityDic.TryGetValue(before[0], out IPlayable p) && p is Minion first)) return;
						var chosen = new List<Minion> { first, second };
						if (chosen.Any(x => x.IsDead || x.Zone?.Type != Zone.PLAY)) return;
						int a1 = chosen[0].AttackDamage, h1 = chosen[0].Health, a2 = chosen[1].AttackDamage, h2 = chosen[1].Health;
						Generic.NamedFx((IPlayable)s2, "VoljinSwap", new[] { chosen[1].Id }, () =>
						{
							Generic.AddEnchantmentBlock(g2, Cards.FromId("VAC_957e"), (IPlayable)s2, chosen[0], a2, h2, 0);
							Generic.AddEnchantmentBlock(g2, Cards.FromId("VAC_957e"), (IPlayable)s2, chosen[1], a1, h1, 0);
						});
					});
				})
			}));
			cards.Add("VAC_957e", new CardDef(new Power { Enchant = Enchants.Enchants.SetAttackHealthScriptTag }));
			// [WORK_017] Silvermoon Brochure (2) — Give a minion Immune this turn and +2/+2. (Flips each turn.) / [WORK_017t] Gilneas
			// Brochure — Silence a minion and give it -2/-2. (Flips each turn.)
			cards.Add("WORK_017", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("WORK_017e", EntityType.TARGET), new AddEnchantmentTask("WORK_017e2", EntityType.TARGET))
			}));
			cards.Add("WORK_017e", OneTurn(new Effect(GameTag.IMMUNE, EffectOperator.SET, 1)));
			cards.Add("WORK_017e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("WORK_017", "stats", 2))) }));
			cards.Add("WORK_017t", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new SilenceTask(EntityType.TARGET), new AddEnchantmentTask("WORK_017te", EntityType.TARGET))
			}));
			cards.Add("WORK_017te", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(-V("WORK_017t", "stats", 2))) }));
			// [WORK_031] Envoy of Prosperity (2) — Battlecry: Put the highest Cost card in your hand on top of your deck.
			cards.Add("WORK_031", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var hand = c.HandZone.GetAll().ToList();
					if (hand.Count == 0) return;
					int top = hand.Max(p => p.Cost);
					var highest = hand.Where(p => p.Cost == top).ToList();
					Generic.PutOnTopOfDeck(c, highest[g.Random.Next(highest.Count)]);
				})
			}));
			// [WORK_032] Job Shadower (3) — Battlecry: If your hero took damage this turn, summon a copy of this.
			cards.Add("WORK_032", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c[HsTag.HERO_DAMAGE_THIS_TURN] > 0, new CustomTask((g, c, s, t, stack) =>
				{
					if (s is Minion me && me.Zone?.Type == Zone.PLAY && !c.BoardZone.IsFull) Generic.Copy(c, s, me, Zone.PLAY, position: me.ZonePosition + 1);
				}))
			}));

			// ---------------------------------------------------------------- Paladin
			// [VAC_507] Sunsapper Lynessa (5) — Rogue Tourist. Your spells that cost (2) or less cast twice.
			Generic.CheapSpellsMaxCost = V("VAC_507", "cost", 2);
			cards.Add("VAC_507", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER, new Effect(HsTag.CHEAP_SPELLS_CAST_TWICE, EffectOperator.ADD, 1))
			}));
			// [VAC_558] Sea Shanty (10) — Summon three 5/5 Pirates. Costs (1) less for each spell you've cast on characters this game.
			cards.Add("VAC_558", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.PlayHistory.Count(h => h.SourceCard?.Type == CardType.SPELL &&
					(h.TargetCard?.Type == CardType.MINION || h.TargetCard?.Type == CardType.HERO))),
				PowerTask = ComplexTask.Repeat(new SummonTask("VAC_558t"), V("VAC_558", "pirates", 3))
			}));
			// [VAC_915] Power Spike (6) — Deal $4 damage. Give a random friendly minion +4/+4.
			cards.Add("VAC_915", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("VAC_915", "damage", 4), EntityType.TARGET, true),
					new CustomTask((g, c, s, t, stack) =>
					{
						if (RandomFriendly(g, c, m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable) is Minion m) Give(g, s, m, "VAC_915e");
					}))
			}));
			cards.Add("VAC_915e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("VAC_915", "stats", 4))) }));
			// [VAC_917] Grillmaster (4) — Battlecry: Draw your lowest Cost card. Deathrattle: Draw your highest Cost card.
			cards.Add("VAC_917", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => DrawByCost(g, c, true)),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => DrawByCost(g, c, false))
			}));
			// [VAC_917t] Sunscreen (1) — Give a minion +1/+2.
			cards.Add("VAC_917t", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("VAC_917e", EntityType.TARGET) }));
			cards.Add("VAC_917e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("VAC_917t", "attack", 1)), Effects.Health_N(V("VAC_917t", "health", 2))) }));
			// [VAC_919] Lifeguard (4) — Taunt. Battlecry: The next spell you cast has Lifesteal.
			cards.Add("VAC_919", new CardDef(new Power { PowerTask = new AddEnchantmentTask("VAC_919e", EntityType.CONTROLLER) }));
			cards.Add("VAC_919e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.CAST_SPELL)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || !((t ?? g.CurrentEventData?.EventSource as IPlayable) is Spell spell)) return;
						Generic.AddEnchantmentBlock(g, Cards.FromId("VAC_919e2"), e.Creator ?? spell, spell, 0, 0, 0);
						e.Remove();
					})
				}
			}));
			cards.Add("VAC_919e2", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.LIFESTEAL, EffectOperator.SET, 1)) }));
			// [VAC_920] Service Ace (2) — After this minion gains Attack, reduce the Cost of the highest Cost card in your hand by (1).
			cards.Add("VAC_920", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK_GAINED)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var hand = c.HandZone.GetAll().ToList();
						if (hand.Count == 0) return;
						int top = hand.Max(p => p.Cost);
						var highest = hand.Where(p => p.Cost == top).ToList();
						Give(g, s, highest[g.Random.Next(highest.Count)], "VAC_920e");
					})
				}
			}));
			cards.Add("VAC_920e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("VAC_920", "discount", 1))) }));
			// [VAC_921] Volley Maul (3, weapon) — After your hero attacks, get a 1-Cost Sunscreen that gives +1/+2.
			cards.Add("VAC_921", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = new AddCardTo("VAC_917t", EntityType.HAND) }
			}));
			// [VAC_922] Lifesaving Aura (1, Paladin Aura) — At the end of your turn, get a 1-Cost Sunscreen that gives +1/+2.
			cards.Add("VAC_922", new CardDef(PaladinAura(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new AddCardTo("VAC_917t", EntityType.HAND) }
			})));
			// [VAC_923] Sanc'Azel (5) — Rush. After this attacks, turn into a location. (Live vfx-decks 202701.)
			cards.Add("VAC_923", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) => Generic.TurnIntoLocation(s as Minion, Cards.FromId("VAC_923t")))
				}
			}));
			// [VAC_923t] Sanc'Azel (Location) — Give a friendly minion +@ Attack and Rush. Turn back into a minion.
			cards.Add("VAC_923t", new CardDef(TargetFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m)
					{
						m[GameTag.RUSH] = 1;
						if (m.IsExhausted && m.NumAttacksThisTurn == 0) { m.IsExhausted = false; m.AttackableByRush = true; g.RushMinions.Add(m.Id); }
						Give(g, s, m, "VAC_923e2", s[GameTag.TAG_SCRIPT_DATA_NUM_1]);
					}
					if (s is Location loc && loc.Damage + 1 < loc.Health) Generic.TurnBackIntoMinion(loc, loc.Damage + 1);
				})
			}));
			cards.Add("VAC_923e2", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackScriptTag }));
			// [WORK_001] Trust Fall (4) — Discover two minions that cost (5) or less. They gain each other's Attack and Health. (Wiki:
			// added, not set.)
			Func<Controller, Card, bool> trusting = (c, x) => x.Type == CardType.MINION && x.Cost <= V("WORK_001", "cost", 5);
			cards.Add("WORK_001", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral(trusting, afterPick: new CustomTask((g, c, s, one, st) =>
					DiscoverFromTask.ClassOrNeutral(trusting, afterPick: new CustomTask((g2, c2, s2, two, st2) =>
					{
						if (one == null || two == null || one.Zone?.Type != Zone.HAND || two.Zone?.Type != Zone.HAND) return;
						int a1 = one.Card.ATK, h1 = one.Card.Health, a2 = two.Card.ATK, h2 = two.Card.Health;
						Generic.AddEnchantmentBlock(g2, Cards.FromId("WORK_001e"), (IPlayable)s2, one, a2, h2, 0);
						Generic.AddEnchantmentBlock(g2, Cards.FromId("WORK_001e2"), (IPlayable)s2, two, a1, h1, 0);
					})).Process(g, c, s, null)))
			}));
			cards.Add("WORK_001e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));
			cards.Add("WORK_001e2", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));
			// [WORK_002] Busy-Bot (2) — Battlecry: Give your 1-Attack minions +1/+1.
			cards.Add("WORK_002", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.BoardZone.GetAll().Where(m => !m.IsDead && m.AttackDamage == V("WORK_002", "attack", 1)).ToList(), "WORK_002e"))
			}));
			cards.Add("WORK_002e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("WORK_002", "stats", 1))) }));
			// [WORK_003] Vacation Planning (4) — Restore #4 Health. Summon 3 Silver Hand Recruits. Draw 2 cards.
			cards.Add("WORK_003", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new HealTask(V("WORK_003", "heal", 4), EntityType.TARGET),
					ComplexTask.Repeat(new SummonTask("CS2_101t"), V("WORK_003", "recruits", 3)), ComplexTask.Repeat(new DrawTask(), V("WORK_003", "cards", 2)))
			}));
		}
	}
}
