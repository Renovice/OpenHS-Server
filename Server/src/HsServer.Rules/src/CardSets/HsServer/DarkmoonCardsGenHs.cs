// HsServer addition (not upstream SabberStone): Madness at the Darkmoon Faire (2020) and the Year of the Phoenix minisets
// (YOP_, Darkmoon Races). Upstream had no script for the set. Every number goes through CardValues (V).
// Shared rules this set added: Corrupt (Generic.CorruptHand / CorruptedVersion, Power.CorruptTask), Power.NoScript (a
// card whose text is all engine rules), Generic.CastFromHand, HsTag.SECRETS_REVEALED; batch 2: Generic.SwitchSides
// (Silas), Dormant minions' triggers wait (Trigger.WhileDormant), the Il'gynoth / Deathwarden / Moonfang / Lightsteed rules.
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
		/// <summary>A card whose text is all engine rules: keywords and a Corrupt into its Corrupted version.</summary>
		private static CardDef EngineOnly => new CardDef(new Power { NoScript = true });

		/// <summary>A random card in hand matching <paramref name="where"/> gets <paramref name="enchantment"/>.</summary>
		private static ISimpleTask BuffRandomInHand(System.Func<IPlayable, bool> where, string enchantment) => new CustomTask((g, c, s, t, stack) =>
		{
			var pool = c.HandZone.Where(where).ToList();
			if (pool.Count > 0) Give(g, s, pool[g.Random.Next(pool.Count)], enchantment);
		});

		/// <summary>"Summon a N/N copy of a minion in your deck" (Blood of G'huun, Idol of Y'Shaarj): a random deck minion's card,
		/// its stats set by <paramref name="enchantment"/>.</summary>
		private static void SummonSetCopyFromDeck(Game g, Controller c, IEntity source, string enchantment)
		{
			var minions = c.DeckZone.Where(p => Generic.IsMinionCard(p)).ToList();
			if (minions.Count == 0 || c.BoardZone.IsFull) return;
			var copy = (Minion)Entity.FromCard(c, minions[g.Random.Next(minions.Count)].Card);
			Generic.SummonBlock.Invoke(g, copy, -1, source);
			Give(g, source, copy, enchantment);
		}

		private static void Darkmoon(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- engine-only (keywords + Corrupt)
			foreach (string id in new[] { "DMF_064", "DMF_073", "DMF_078", "DMF_080", "DMF_083", "DMF_184", "DMF_247", "DMF_248",
				         "DMF_517", "DMF_532", "YOP_011", "YOP_025",
				         "SCH_199t26" }) // Transfer Student's Darkmoon Faire version: "Corrupt: Gain +2/+2"
				cards.Add(id, EngineOnly);

			// ---------------------------------------------------------------- Neutral

			// [DMF_002] N'Zoth, God of the Deep (9) — Battlecry: Resurrect a friendly minion of each minion type.
			cards.Add("DMF_002", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var dead = c.DiedThisGameCards.Where(p => p is Minion).ToList();
					var picked = new List<IPlayable>();
					foreach (Race race in System.Enum.GetValues(typeof(Race)).Cast<Race>().Where(r => r != Race.INVALID && r != Race.BLANK && r != Race.ALL))
					{
						var ofType = dead.Where(p => !picked.Contains(p) && ((Minion)p).IsRace(race)).ToList();
						if (ofType.Count > 0) picked.Add(ofType[g.Random.Next(ofType.Count)]);
					}
					foreach (IPlayable p in picked) Summon(g, c, p.Card, s);
				})
			}));
			// [DMF_062] Gyreworm (3) — Battlecry: If you played an Elemental last turn, deal 3 damage.
			cards.Add("DMF_062", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABE_AND_ELEMENTAL_PLAYED_LAST_TURN, 0 } }, new Power
			{
				PowerTask = ComplexTask.Conditional(SelfCondition.ElementalPlayedLastTurn, new DamageTask(V("DMF_062", "damage", 3), EntityType.TARGET))
			}));
			// [DMF_065] Banana Vendor (3) — Battlecry: Add 2 Bananas to each player's hand.
			cards.Add("DMF_065", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Controller p in new[] { c, c.Opponent })
						for (int i = 0; i < V("DMF_065", "bananas", 2); i++) AddToHand(p, Cards.FromId("EX1_014t"));
				})
			}));
			// [DMF_066] Knife Vendor (4) — Battlecry: Deal 4 damage to each hero.
			cards.Add("DMF_066", new CardDef(new Power { PowerTask = new DamageTask(V("DMF_066", "damage", 4), EntityType.HEROES) }));
			// [DMF_068] Optimistic Ogre (5) — 50% chance to attack the correct enemy.
			cards.Add("DMF_068", new CardDef(new Power { Trigger = Forgetful("DMF_068") }));
			// [DMF_069] Claw Machine (6) — Rush. Deathrattle: Draw a minion and give it +3/+3.
			cards.Add("DMF_069", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { if (DrawOne(g, c, p => p is Minion) is IPlayable m) Give(g, s, m, "DMF_069e"); })
			}));
			cards.Add("DMF_069e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DMF_069", "stats", 3))) }));
			// [DMF_070] Darkmoon Rabbit (10) — Rush, Poisonous. Also damages the minions next to whomever this attacks.
			cards.Add("DMF_070", new CardDef(new Power { Trigger = Cleave() }));
			// [DMF_079] Inconspicuous Rider (3) — Battlecry: Cast a Secret from your deck.
			cards.Add("DMF_079", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var secrets = c.DeckZone.Where(p => p is Spell sp && sp.IsSecret).ToList();
					if (secrets.Count > 0) Generic.CastFromDeck(c, s, (Spell)secrets[g.Random.Next(secrets.Count)]);
				})
			}));
			// [DMF_081] K'thir Ritualist (3) — Taunt. Battlecry: Add a random 4-Cost minion to your opponent's hand.
			cards.Add("DMF_081", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (RandomCard(g, x => x.Type == CardType.MINION && x.Cost == V("DMF_081", "cost", 4)) is Card card) AddToHand(c.Opponent, card);
				})
			}));
			// [DMF_082] Darkmoon Statue (3) — Your other minions have +1 Attack. Corrupt: This gains +4 Attack (its Corrupted
			// version's stats; it keeps the aura).
			var statue = new CardDef(new Power { Aura = new Aura(AuraType.BOARD_EXCEPT_SOURCE, "DMF_082e") });
			cards.Add("DMF_082", statue);
			cards.Add("DMF_082t", statue);
			cards.Add("DMF_082e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("DMF_082", "attack", 1))) }));
			// [DMF_091] Wriggling Horror (2) — Battlecry: Give adjacent minions +1/+1.
			cards.Add("DMF_091", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me) GiveAll(g, s, me.GetAdjacentMinions().ToList(), "DMF_091e2"); })
			}));
			cards.Add("DMF_091e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DMF_091", "stats", 1))) }));
			// [DMF_124] Horrendous Growth (2) — Corrupt: Gain +1/+1. Can be Corrupted endlessly. (its Corrupted version is 3/3; it
			// Corrupts again into itself, +1/+1 each time: Power.CorruptTask)
			cards.Add("DMF_124", EngineOnly);
			cards.Add("DMF_124t", new CardDef(new Power
			{
				CorruptTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					me.AttackDamage += V("DMF_124t", "stats", 1);
					me.BaseHealth += V("DMF_124t", "stats", 1);
				})
			}));
			// [DMF_125] Safety Inspector (1) — Battlecry: Shuffle the lowest-Cost card from your hand into your deck. Draw a card.
			cards.Add("DMF_125", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.HandZone.Count > 0)
					{
						int low = c.HandZone.Min(p => p.Cost);
						var lowest = c.HandZone.Where(p => p.Cost == low).ToList();
						Generic.MoveToDeck(c, s, lowest[g.Random.Next(lowest.Count)]);
					}
					Generic.Draw(c);
				})
			}));
			// [DMF_163] Carnival Clown (9) — Taunt. Battlecry: Summon 2 copies of this. Corrupt: Fill your board with copies.
			cards.Add("DMF_163", new CardDef(new Power { PowerTask = ComplexTask.Repeat(new SummonCopyTask(EntityType.SOURCE), V("DMF_163", "copies", 2)) }));
			cards.Add("DMF_163t", new CardDef(new Power { PowerTask = ComplexTask.Repeat(new SummonCopyTask(EntityType.SOURCE), 6) }));
			// [DMF_174] Circus Medic (4) — Battlecry: Restore 4 Health. Corrupt: Deal 4 damage instead.
			cards.Add("DMF_174", new CardDef(BattlecryAny, new Power { PowerTask = new HealTask(V("DMF_174", "heal", 4), EntityType.TARGET) }));
			cards.Add("DMF_174t", new CardDef(BattlecryAny, new Power { PowerTask = new DamageTask(V("DMF_174t", "damage", 4), EntityType.TARGET) }));
			// [DMF_189] Costumed Entertainer (2) — Battlecry: Give a random minion in your hand +2/+2.
			cards.Add("DMF_189", new CardDef(new Power { PowerTask = BuffRandomInHand(p => p is Minion, "DMF_189e") }));
			cards.Add("DMF_189e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DMF_189", "stats", 2))) }));
			// [DMF_191] Showstopper (2) — Deathrattle: Silence all minions.
			cards.Add("DMF_191", new CardDef(new Power { DeathrattleTask = new SilenceTask(EntityType.ALLMINIONS) }));
			// [DMF_202] Derailed Coaster (5) — Battlecry: Summon a 1/1 Rider with Rush for each minion in your hand.
			cards.Add("DMF_202", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = c.HandZone.Count(p => Generic.IsMinionCard(p));
					for (int i = 0; i < n && !c.BoardZone.IsFull; i++) Summon(g, c, Cards.FromId("DMF_523t"), s);
				})
			}));
			// [DMF_520] Parade Leader (2) — After you summon a Rush minion, give it +2 Attack.
			cards.Add("DMF_520", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					Condition = new SelfCondition(p => p is Minion m && m.IsRush),
					SingleTask = new AddEnchantmentTask("DMF_520e", EntityType.TARGET)
				}
			}));
			cards.Add("DMF_520e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("DMF_520", "attack", 2))) }));
			// [YOP_005] Barricade (4) — Summon a 2/4 Guard with Taunt. If it's your only minion, summon another.
			cards.Add("YOP_005", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Summon(g, c, Cards.FromId("YOP_005t"), s);
					if (c.BoardZone.Count == 1) Summon(g, c, Cards.FromId("YOP_005t"), s);
				})
			}));
			// [YOP_006] Hysteria (4) — Choose an enemy minion. It attacks random minions until it dies.
			cards.Add("YOP_006", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } },
				new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(t is Minion m)) return;
						for (int i = 0; i < 30 && !m.IsDead && !m.ToBeDestroyed && m.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY; i++)
						{
							var others = c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll())
								.Where(o => o.Id != m.Id && !o.IsDead && !o.ToBeDestroyed && !o.Untouchable).ToList();
							if (others.Count == 0) break;
							EffectAttack(g, m, others[g.Random.Next(others.Count)]);
							g.DeathProcessingAndAuraUpdate();
						}
					})
				}));
			// [YOP_009] Rally! (4) — Resurrect a friendly 1-Cost, 2-Cost, and 3-Cost minion.
			cards.Add("YOP_009", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var dead = c.DiedThisGameCards.Where(p => p is Minion).Select(p => p.Card).ToList();
					foreach (int cost in new[] { 1, 2, 3 })
					{
						var ofCost = dead.Where(x => x.Cost == cost).ToList();
						if (ofCost.Count > 0) Summon(g, c, ofCost[g.Random.Next(ofCost.Count)], s);
					}
				})
			}));
			// [YOP_015] Nitroboost Poison (2) — Give a minion +2 Attack. Corrupt: And your weapon.
			cards.Add("YOP_015", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("YOP_015e", EntityType.TARGET) }));
			cards.Add("YOP_015t", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("YOP_015e", EntityType.TARGET), new AddEnchantmentTask("YOP_015e", EntityType.WEAPON))
			}));
			cards.Add("YOP_015e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.ATK, EffectOperator.ADD, V("YOP_015", "attack", 2))) }));
			// [YOP_029] Resizing Pouch (1) — Discover a card with Cost equal to your remaining Mana Crystals.
			cards.Add("YOP_029", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int mana = c.RemainingMana;
					DiscoverFromTask.ClassOrNeutral((c2, x) => x.Cost == mana).Process(g, c, s, t);
				})
			}));
			// [YOP_030] Felfire Deadeye (2) — Your Hero Power costs (1) less.
			cards.Add("YOP_030", new CardDef(new Power { Aura = new Aura(AuraType.HEROPOWER, "YOP_030e") }));
			cards.Add("YOP_030e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("YOP_030", "discount", 1))) }));

			// ---------------------------------------------------------------- Demon Hunter

			// [DMF_217] Line Hopper (3) — Your Outcast cards cost (1) less.
			cards.Add("DMF_217", new CardDef(new Power { Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("DMF_217", "discount", 1))) { Condition = IsOutcastCard } }));
			// [DMF_219] Relentless Pursuit (3) — Give your hero +4 Attack and Immune this turn.
			cards.Add("DMF_219", new CardDef(new Power { PowerTask = new AddEnchantmentTask("DMF_219e", EntityType.HERO) }));
			cards.Add("DMF_219e", OneTurn(Effects.Attack_N(V("DMF_219", "attack", 4)), Effects.Immune));
			// [DMF_221] Felscream Blast (1) — Lifesteal. Deal 1 damage to a minion and its neighbors.
			cards.Add("DMF_221", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					var hit = new List<Minion> { m };
					hit.AddRange(m.GetAdjacentMinions());
					foreach (Minion x in hit) Generic.DamageCharFunc((IPlayable)s, x, V("DMF_221", "damage", 1), false);
				})
			}));
			// [DMF_222] Redeemed Pariah (2) — After you play an Outcast card, gain +1/+1.
			cards.Add("DMF_222", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = IsOutcastCard,
					SingleTask = new AddEnchantmentTask("DMF_222e", EntityType.SOURCE)
				}
			}));
			cards.Add("DMF_222e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DMF_222", "stats", 1))) }));
			// [DMF_223] Renowned Performer (4) — Rush. Deathrattle: Summon two 1/1 Assistants with Taunt.
			cards.Add("DMF_223", new CardDef(new Power { DeathrattleTask = new SummonTask("DMF_223t", V("DMF_223", "assistants", 2)) }));
			// [DMF_225] Throw Glaive (1) — Deal 2 damage to a minion. If it dies, add a Temporary copy of this to your hand.
			cards.Add("DMF_225", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Generic.DamageCharFunc((IPlayable)s, m, V("DMF_225", "damage", 2), false);
					if ((m.IsDead || m.ToBeDestroyed || m.Health <= 0) && !c.HandZone.IsFull)
					{
						IPlayable copy = Entity.FromCard(c, Cards.FromId("DMF_225"));
						if (Generic.AddHandPhase.Invoke(c, copy)) Generic.MakeTemporary(copy, s);
					}
				})
			}));
			// [DMF_226] Bladed Lady (6) — Rush. Costs (1) if your hero has 6 or more Attack.
			cards.Add("DMF_226", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.Hero.AttackDamage >= V("DMF_226", "attack", 6) ? System.Math.Max(0, p.Card.Cost - 1) : 0)
			}));
			// [DMF_227] Dreadlord's Bite (3) — Outcast: Deal 1 damage to all enemies.
			cards.Add("DMF_227", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new ConditionTask(EntityType.SOURCE, PlayedAsOutcast),
					new FlagTask(true, new DamageTask(V("DMF_227", "damage", 1), EntityType.ENEMIES)))
			}));
			// [DMF_229] Stiltstepper (3) — Battlecry: Draw a card. If you play it this turn, give your hero +4 Attack this turn.
			cards.Add("DMF_229", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (Generic.Draw(c) is IPlayable drawn && drawn.Zone?.Type == SabberStoneCore.Enums.Zone.HAND) Give(g, s, drawn, "DMF_229e2"); })
			}));
			cards.Add("DMF_229e2", new CardDef(new Power
			{
				Enchant = new Enchant { IsOneTurnEffect = true },
				Trigger = new Trigger(TriggerType.PLAY_CARD)
				{
					TriggerActivation = TriggerActivation.HAND_OR_PLAY,
					TriggerSource = TriggerSource.ENCHANTMENT_TARGET,
					SingleTask = new AddEnchantmentTask("DMF_229e", EntityType.HERO)
				}
			}));
			cards.Add("DMF_229e", OneTurn(Effects.Attack_N(V("DMF_229", "attack", 4))));
			// [YOP_002] Felsaber (4) — Can only attack if your hero attacked this turn.
			cards.Add("YOP_002", new CardDef(new Power
			{
				Aura = new Aura(AuraType.SELF, new Effect(GameTag.CANT_ATTACK, EffectOperator.SET, 1))
				{
					Condition = new SelfCondition(p => p.Controller.Hero.NumAttacksThisTurn == 0), Restless = true
				}
			}));

			// ---------------------------------------------------------------- Druid

			// [DMF_061] Faire Arborist (3) — Choose One - Draw a card; or Summon a 2/2 Treant. Corrupt: Do both.
			cards.Add("DMF_061", new CardDef(new Power { PowerTask = ComplexTask.Create(new SummonTask("DMF_061t2", SummonSide.RIGHT), new DrawTask()) }));
			cards.Add("DMF_061a", new CardDef(new Power { PowerTask = new DrawTask() }));
			cards.Add("DMF_061b", new CardDef(new Power { PowerTask = new SummonTask("DMF_061t2", SummonSide.RIGHT) }));
			cards.Add("DMF_061t", new CardDef(new Power { PowerTask = ComplexTask.Create(new SummonTask("DMF_061t2", SummonSide.RIGHT), new DrawTask()) }));
			// [DMF_730] Moontouched Amulet (3) — Give your hero +4 Attack this turn. Corrupt: And gain 6 Armor.
			cards.Add("DMF_730", new CardDef(new Power { PowerTask = new AddEnchantmentTask("DMF_730e", EntityType.HERO) }));
			cards.Add("DMF_730t", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("DMF_730e", EntityType.HERO), new ArmorTask(V("DMF_730t", "armor", 6)))
			}));
			cards.Add("DMF_730e", OneTurn(Effects.Attack_N(V("DMF_730", "attack", 4))));
			// [DMF_732] Cenarion Ward (8) — Gain 8 Armor. Summon a random 8-Cost minion.
			cards.Add("DMF_732", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new ArmorTask(V("DMF_732", "armor", 8)),
					new CustomTask((g, c, s, t, stack) => Summon(g, c, RandomMinionOfCost(g, V("DMF_732", "cost", 8)), s)))
			}));
			// [YOP_026] Arbor Up (5) — Summon two 2/2 Treants. Give your minions +2/+1.
			cards.Add("YOP_026", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new SummonTask("DMF_061t2", V("YOP_026", "treants", 2), SummonSide.SPELL),
					new AddEnchantmentTask("YOP_026e", EntityType.MINIONS))
			}));
			cards.Add("YOP_026e", FromText("YOP_026e"));

			// ---------------------------------------------------------------- Hunter

			// [DMF_084] Jewel of N'Zoth (8) — Summon three friendly Deathrattle minions that died this game.
			cards.Add("DMF_084", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var dead = c.DiedThisGameCards.Where(p => p is Minion && p.Card[GameTag.DEATHRATTLE] > 0).Select(p => p.Card).ToList();
					for (int i = 0; i < V("DMF_084", "minions", 3) && dead.Count > 0; i++)
					{
						Card card = dead[g.Random.Next(dead.Count)];
						dead.Remove(card);
						Summon(g, c, card, s);
					}
				})
			}));
			// [DMF_085] Darkmoon Tonk (7) — Deathrattle: Fire four missiles at random enemies that deal 2 damage each.
			cards.Add("DMF_085", new CardDef(new Power
			{
				DeathrattleTask = new EnqueueTask(V("DMF_085", "missiles", 4), ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, V("DMF_085", "damage", 2)))
			}));
			// [DMF_086] Petting Zoo (3) — Summon a 3/3 Strider. Repeat for each Secret you control.
			cards.Add("DMF_086", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = 1 + c.SecretZone.Count(p => p.IsSecret);
					for (int i = 0; i < n && !c.BoardZone.IsFull; i++) Summon(g, c, Cards.FromId("DMF_086e"), s);
				})
			}));
			// [DMF_088] Rinling's Rifle (4) — After your hero attacks, Discover a Secret and cast it.
			cards.Add("DMF_088", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.IsSecret, action: ChoiceAction.CAST)
				}
			}));
			// [DMF_089] Maxima Blastenheimer (6) — Battlecry: Summon a minion from your deck. It attacks the enemy hero, then dies.
			cards.Add("DMF_089", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var summoned = new TaskStack();
					new RecruitTask(1, true).Process(g, c, s, t, summoned);
					foreach (Minion m in (summoned.Playables ?? new List<IPlayable>()).OfType<Minion>().ToList())
					{
						EffectAttack(g, m, c.Opponent.Hero);
						if (m.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY) m.Destroy();
					}
				})
			}));
			// [DMF_090] Don't Feed the Animals (2) — Give all Beasts in your hand +1/+1. Corrupt: Give them +2/+2 instead.
			cards.Add("DMF_090", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.Where(p => p is Minion m && m.IsRace(Race.BEAST)).ToList(), "DMF_090e2"))
			}));
			cards.Add("DMF_090t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.Where(p => p is Minion m && m.IsRace(Race.BEAST)).ToList(), "DMF_090e"))
			}));
			cards.Add("DMF_090e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DMF_090", "stats", 1))) }));
			cards.Add("DMF_090e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DMF_090t", "stats", 2))) }));
			// [DMF_122] Mystery Winner (1) — Battlecry: Discover a Secret.
			cards.Add("DMF_122", new CardDef(new Power { PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.IsSecret) }));
			// [DMF_123] Open the Cages (2) — Secret: When your turn starts, if you control two minions, summon an Animal Companion.
			cards.Add("DMF_123", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					Condition = new SelfCondition(p => p.Controller.BoardZone.Count >= V("DMF_123", "minions", 2) && !p.Controller.BoardZone.IsFull),
					SingleTask = ComplexTask.Secret(new CustomTask((g, c, s, t, stack) => Generic.SummonCompanions(c, s, 1)))
				}
			}));
			// [YOP_027] Bola Shot (2) — Deal 1 damage to a minion and 2 damage to its neighbors.
			cards.Add("YOP_027", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					var neighbors = m.GetAdjacentMinions().ToList();
					Generic.DamageCharFunc((IPlayable)s, m, V("YOP_027", "damage", 1), false);
					foreach (Minion n in neighbors) Generic.DamageCharFunc((IPlayable)s, n, V("YOP_027", "neighbor damage", 2), false);
				})
			}));
			// [YOP_028] Saddlemaster (3) — After you play a Beast, add a random Beast to your hand.
			cards.Add("YOP_028", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = SelfCondition.IsRace(Race.BEAST),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.BEAST)) is Card card) AddToHand(c, card);
					})
				}
			}));

			// ---------------------------------------------------------------- Mage

			// [DMF_100] Confection Cyclone (2) — Battlecry: Add two 1/2 Sugar Elementals to your hand.
			cards.Add("DMF_100", new CardDef(new Power { PowerTask = new AddCardTo("DMF_100t", EntityType.HAND, V("DMF_100", "elementals", 2)) }));
			// [DMF_101] Firework Elemental (5) — Battlecry: Deal 3 damage to a minion. Corrupt: Deal 12 instead.
			cards.Add("DMF_101", new CardDef(BattlecryMinion, new Power { PowerTask = new DamageTask(V("DMF_101", "damage", 3), EntityType.TARGET) }));
			cards.Add("DMF_101t", new CardDef(BattlecryMinion, new Power { PowerTask = new DamageTask(V("DMF_101t", "damage", 12), EntityType.TARGET) }));
			// [DMF_102] Game Master (2) — The first Secret you play each turn costs (1).
			cards.Add("DMF_102", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, "DMF_102e")
				{
					Condition = new SelfCondition(p => p is Spell sp && sp.IsSecret && !p.Controller.CardsPlayedThisTurn.Any(x => x.IsSecret)),
					Restless = true
				}
			}));
			cards.Add("DMF_102e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(V("DMF_102", "cost", 1))) }));
			// [DMF_103] Mask of C'Thun (7) — Deal 10 damage randomly split among all enemies.
			cards.Add("DMF_103", new CardDef(new Power
			{
				PowerTask = new EnqueueTask(V("DMF_103", "damage", 10), ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, 1), true)
			}));
			// [DMF_104] Grand Finale (8) — Summon an 8/8 Elemental. Repeat for each Elemental you played last turn.
			cards.Add("DMF_104", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = 1 + c.NumElementalsPlayedLastTurn;
					for (int i = 0; i < n && !c.BoardZone.IsFull; i++) Summon(g, c, Cards.FromId("DMF_104t"), s);
				})
			}));
			// [DMF_105] Ring Toss (4) — Discover a Secret and cast it. Corrupt: Discover 2 instead.
			ISimpleTask ringToss = DiscoverFromTask.ClassOrNeutral((c, x) => x.IsSecret, action: ChoiceAction.CAST);
			cards.Add("DMF_105", new CardDef(new Power { PowerTask = ringToss }));
			cards.Add("DMF_105t", new CardDef(new Power { PowerTask = ComplexTask.Repeat(ringToss, V("DMF_105t", "secrets", 2)) }));
			// [DMF_106] Occult Conjurer (4) — Battlecry: If you control a Secret, summon a copy of this.
			cards.Add("DMF_106", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.SecretZone.Any(p => p.IsSecret), new SummonCopyTask(EntityType.SOURCE))
			}));
			// [DMF_107] Rigged Faire Game (3) — Secret: If you didn't take any damage during your opponent's turn, draw 3 cards.
			cards.Add("DMF_107", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					EitherTurn = true,
					TriggerSource = TriggerSource.ENEMY,
					Condition = new SelfCondition(p => p.Controller.Hero.DamageTakenThisTurn == 0),
					SingleTask = ComplexTask.Secret(new EnqueueTask(V("DMF_107", "cards", 3), new DrawTask()))
				}
			}));
			// [DMF_108] Deck of Lunacy (2) — Transform spells in your deck into ones that cost (3) more. (They keep their
			// original Cost.)
			cards.Add("DMF_108", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable spell in c.DeckZone.Where(p => p is Spell).ToList())
					{
						int cost = spell.Cost;
						if (!(RandomCard(g, x => x.Type == CardType.SPELL && x.Cost == System.Math.Min(10, cost + V("DMF_108", "more", 3))) is Card card)) continue;
						IPlayable changed = Generic.ChangeEntityBlock(c, spell, card, true);
						Generic.AddEnchantmentBlock(g, Cards.FromId("DMF_108e"), (IPlayable)s, changed, cost, 0, 0);
					}
				})
			}));
			cards.Add("DMF_108e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) { UseScriptTag = true } }));
			// [DMF_109] Sayge, Seer of Darkmoon (6) — Battlecry: Draw 1 card (upgraded for each friendly Secret that has
			// triggered this game).
			cards.Add("DMF_109", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < 1 + c[HsTag.SECRETS_REVEALED]; i++) Generic.Draw(c);
				})
			}));
			// [YOP_019] Conjure Mana Biscuit (2) — Add a Biscuit to your hand that refreshes 2 Mana Crystals.
			cards.Add("YOP_019", new CardDef(new Power { PowerTask = new AddCardTo("YOP_019t", EntityType.HAND) }));
			cards.Add("YOP_019t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => c.UsedMana = System.Math.Max(0, c.UsedMana - V("YOP_019t", "mana", 2)))
			}));
			// [YOP_020] Glacier Racer (1) — Spellburst: Deal 3 damage to all Frozen enemies.
			cards.Add("YOP_020", new CardDef(new Power
			{
				Trigger = Spellburst(new CustomTask((g, c, s, t, stack) =>
				{
					var frozen = c.Opponent.BoardZone.GetAll().Where(m => m.IsFrozen).Cast<ICharacter>().ToList();
					if (c.Opponent.Hero.IsFrozen) frozen.Add(c.Opponent.Hero);
					foreach (ICharacter x in frozen) Generic.DamageCharFunc((IPlayable)s, x, V("YOP_020", "damage", 3), false);
				}))
			}));

			// ---------------------------------------------------------------- Paladin

			// [DMF_235] Balloon Merchant (4) — Battlecry: Give your Silver Hand Recruits +1 Attack and Divine Shield.
			cards.Add("DMF_235", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().Where(m => m.Card.Name == Cards.FromId("CS2_101t").Name).ToList())
					{
						Give(g, s, m, "DMF_235e");
						m[GameTag.DIVINE_SHIELD] = 1;
					}
				})
			}));
			cards.Add("DMF_235e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("DMF_235", "attack", 1))) }));
			// [DMF_237] Carnival Barker (3) — Whenever you summon a 1-Health minion, give it +1/+2.
			cards.Add("DMF_237", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					Condition = new SelfCondition(p => p is Minion m && m.Health == 1),
					SingleTask = new AddEnchantmentTask("DMF_237e", EntityType.TARGET)
				}
			}));
			cards.Add("DMF_237e", FromText("DMF_237e"));
			// [DMF_241] High Exarch Yrel (8) — Battlecry: If your deck has no Neutral cards, gain Rush, Lifesteal, Taunt, and Divine Shield.
			cards.Add("DMF_241", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.DeckZone.All(p => !p.Card.HasClass(CardClass.NEUTRAL)), new CustomTask((g, c, s, t, stack) =>
				{
					foreach (GameTag k in new[] { GameTag.RUSH, GameTag.LIFESTEAL, GameTag.TAUNT, GameTag.DIVINE_SHIELD }) ((IPlayable)s)[k] = 1;
				}))
			}));
			// [DMF_244] Day at the Faire (3) — Summon 3 Silver Hand Recruits. Corrupt: Summon 5.
			cards.Add("DMF_244", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power { PowerTask = new SummonTask("CS2_101t", V("DMF_244", "recruits", 3), SummonSide.SPELL) }));
			cards.Add("DMF_244t", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power { PowerTask = new SummonTask("CS2_101t", V("DMF_244t", "recruits", 5), SummonSide.SPELL) }));

			// ---------------------------------------------------------------- Priest

			// [DMF_053] Blood of G'huun (9) — Taunt. At the end of your turn, summon a 5/5 copy of a minion in your deck.
			cards.Add("DMF_053", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new CustomTask((g, c, s, t, stack) => SummonSetCopyFromDeck(g, c, s, "DMF_053e")) }
			}));
			cards.Add("DMF_053e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("DMF_053", "stats", 5))) }));
			// [DMF_054] Insight (2) — Draw a minion. Corrupt: Reduce its Cost by (2).
			cards.Add("DMF_054", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => DrawOne(g, c, p => p is Minion)) }));
			cards.Add("DMF_054t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (DrawOne(g, c, p => p is Minion) is IPlayable m) Give(g, s, m, "DMF_054e"); })
			}));
			cards.Add("DMF_054e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("DMF_054t", "discount", 2))) }));
			// [DMF_055] Idol of Y'Shaarj (8) — Summon a 10/10 copy of a minion in your deck.
			cards.Add("DMF_055", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => SummonSetCopyFromDeck(g, c, s, "DMF_055e"))
			}));
			cards.Add("DMF_055e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("DMF_055", "stats", 10))) }));
			// [DMF_120] Nazmani Bloodweaver (3) — After you cast a spell, reduce the Cost of a random card in your hand by (1).
			cards.Add("DMF_120", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = BuffRandomInHand(p => true, "DMF_120e2") }
			}));
			cards.Add("DMF_120e2", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("DMF_120", "discount", 1))) }));
			// [DMF_121] Fortune Teller (5) — Taunt. Battlecry: Gain +1/+1 for each spell in your hand.
			cards.Add("DMF_121", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = c.HandZone.Count(p => p is Spell);
					if (n > 0) Generic.AddEnchantmentBlock(g, Cards.FromId("DMF_121e2"), (IPlayable)s, (IPlayable)s, n, n, 0);
				})
			}));
			cards.Add("DMF_121e2", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));
			// [DMF_186] Auspicious Spirits (4) — Summon a random 4-Cost minion. Corrupt: Summon a 7-Cost minion instead.
			cards.Add("DMF_186", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, RandomMinionOfCost(g, V("DMF_186", "cost", 4)), s))
			}));
			cards.Add("DMF_186a", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, RandomMinionOfCost(g, V("DMF_186a", "cost", 7)), s))
			}));
			// [DMF_187] Palm Reading (3) — Discover a spell. Reduce the Cost of spells in your hand by (1).
			cards.Add("DMF_187", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL),
					new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.Where(p => p is Spell).ToList(), "DMF_187e")))
			}));
			cards.Add("DMF_187e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("DMF_187", "discount", 1))) }));

			// ---------------------------------------------------------------- Rogue

			// [DMF_071] Tenwu of the Red Smoke (2) — Battlecry: Return a friendly minion to your hand. It costs (1) this turn.
			cards.Add("DMF_071", new CardDef(BattlecryFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m && m.Id != s.Id && Generic.ReturnToHand(c, m) is IPlayable back)
						Generic.AddEnchantmentBlock(g, Cards.FromId("DMF_071e"), (IPlayable)s, back, V("DMF_071", "cost", 1) - back.Cost, 0, 0);
				})
			}));
			// "Costs (1) this turn": a one-turn ADD of (1 - Cost), which reverts (a SET can't be undone)
			cards.Add("DMF_071e", new CardDef(new Power { Enchant = new Enchant(Effects.AddCost(0)) { UseScriptTag = true, IsOneTurnEffect = true } }));
			// [DMF_512] Cloak of Shadows (4) — Give your hero Stealth until your next turn.
			cards.Add("DMF_512", new CardDef(new Power { PowerTask = new AddEnchantmentTask("DMF_512e", EntityType.HERO) }));
			cards.Add("DMF_512e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.StealthEff),
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = RemoveEnchantmentTask.Task }
			}));
			// [DMF_513] Shadow Clone (2) — Secret: After a minion attacks your hero, summon a copy of it with Stealth.
			cards.Add("DMF_513", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.ENEMY,
					Condition = new SelfCondition(p => p is Minion && p.Game.CurrentEventData?.EventTarget is Hero),
					SingleTask = ComplexTask.Secret(new CustomTask((g, c, s, t, stack) =>
					{
						if (!(g.CurrentEventData?.EventSource is Minion attacker) || c.BoardZone.IsFull) return;
						var copies = new TaskStack();
						new SummonCopyTask(EntityType.TARGET, addToStack: true).Process(g, c, s, attacker, copies);
						foreach (Minion copy in (copies.Playables ?? new List<IPlayable>()).OfType<Minion>()) copy[GameTag.STEALTH] = 1;
					}))
				}
			}));
			// [DMF_514] Ticket Master (3) — Deathrattle: Shuffle 3 Tickets into your deck. When drawn, summon a 3/3 Plush Bear.
			cards.Add("DMF_514", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("DMF_514", "tickets", 3); i++) Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("DMF_514t")));
				})
			}));
			var bear = new SummonTask("DMF_514t2", SummonSide.SPELL);
			cards.Add("DMF_514t", new CardDef(new Power { PowerTask = bear, TopdeckTask = bear }));
			// [DMF_515] Swindle (2) — Draw a spell. Combo: And a minion.
			cards.Add("DMF_515", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => DrawOne(g, c, p => p is Spell)),
				ComboTask = new CustomTask((g, c, s, t, stack) => { DrawOne(g, c, p => p is Spell); DrawOne(g, c, p => p is Minion); })
			}));
			// [DMF_516] Grand Empress Shek'zara (6) — Battlecry: Discover a card in your deck and draw all copies of it.
			cards.Add("DMF_516", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var distinct = c.DeckZone.GroupBy(p => p.Card.Id).Select(x => x.First()).ToList();
					Generic.DiscoverFromDeck(c, s, Generic.PickFrom(distinct, g, _ => true), (g2, c2, s2, picked) =>
					{
						foreach (IPlayable copy in c2.DeckZone.Where(p => p.Card.Id == picked.Card.Id).ToList()) Generic.Draw(c2, copy);
					});
				})
			}));
			// [DMF_518] Malevolent Strike (5) — Destroy a minion. Costs (1) less for each card in your deck that didn't start there.
			cards.Add("DMF_518", new CardDef(TargetMinion, new Power
			{
				PowerTask = new DestroyTask(EntityType.TARGET),
				Aura = new AdaptiveCostEffect(p => p.Controller.DeckZone.Count(x => x.Id >= p.Game.FirstGeneratedEntityId))
			}));
			// [DMF_519] Prize Plunderer (1) — Combo: Deal 1 damage to a minion for each other card you've played this turn.
			var plunderer = new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_FOR_COMBO, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } }, new Power
			{
				ComboTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					for (int i = 0; i < Generic.CardsPlayedBefore(c, s) && !m.IsDead && !m.ToBeDestroyed; i++) Generic.DamageCharFunc((IPlayable)s, m, 1, false);
				})
			});
			cards.Add("DMF_519", plunderer);
			cards.Add("DMF_519_COPY", plunderer);
			// [YOP_016] Sparkjoy Cheat (3) — Battlecry: If you're holding a Secret, cast it and draw a card.
			cards.Add("YOP_016", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.HandZone.Any(p => p is Spell sp && sp.IsSecret), new CustomTask((g, c, s, t, stack) =>
				{
					var secrets = c.HandZone.Where(p => p is Spell sp && sp.IsSecret).ToList();
					Generic.CastFromHand(c, s, (Spell)secrets[g.Random.Next(secrets.Count)]);
					Generic.Draw(c);
				}))
			}));

			// ---------------------------------------------------------------- Shaman

			// [DMF_700] Revolve (1) — Transform all minions into random ones with the same Cost.
			cards.Add("DMF_700", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => TransformCostShifted(g, c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()), 0))
			}));
			// [DMF_701] Dunk Tank (4) — Deal 4 damage. Corrupt: Then deal 2 damage to all enemy minions.
			cards.Add("DMF_701", new CardDef(TargetAny, new Power { PowerTask = new DamageTask(V("DMF_701", "damage", 4), EntityType.TARGET, true) }));
			cards.Add("DMF_701t", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("DMF_701t", "damage", 4), EntityType.TARGET, true),
					new DamageTask(V("DMF_701t", "all damage", 2), EntityType.OP_MINIONS, true))
			}));
			// [DMF_702] Stormstrike (3) — Deal 3 damage to a minion. Give your hero +3 Attack this turn.
			cards.Add("DMF_702", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("DMF_702", "damage", 3), EntityType.TARGET, true), new AddEnchantmentTask("DMF_702e", EntityType.HERO))
			}));
			cards.Add("DMF_702e", OneTurn(Effects.Attack_N(V("DMF_702", "attack", 3))));
			// [DMF_703] Pit Master (3) — Battlecry: Summon a 3/2 Duelist. Corrupt: Summon two. (The Duelist is Pavilion Duelist DMF_706t:
			// live session-20260930-212533 1265 and 20261006-131949 76831; the client's DMF_703t2 "Duelist" isn't what live summons.)
			cards.Add("DMF_703", new CardDef(new Power { PowerTask = new SummonTask("DMF_706t", SummonSide.RIGHT) }));
			cards.Add("DMF_703t", new CardDef(new Power { PowerTask = new SummonTask("DMF_706t", V("DMF_703t", "duelists", 2)) }));
			// [DMF_704] Cagematch Custodian (2) — Battlecry: Draw a weapon.
			cards.Add("DMF_704", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => DrawOne(g, c, p => p is Weapon)) }));
			// [DMF_705] Whack-A-Gnoll Hammer (3) — After your hero attacks, give a random friendly minion +1/+1.
			cards.Add("DMF_705", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var minions = c.BoardZone.GetAll().Where(m => !m.IsDead).ToList();
						if (minions.Count > 0) Give(g, s, minions[g.Random.Next(minions.Count)], "DMF_705e");
					})
				}
			}));
			cards.Add("DMF_705e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DMF_705", "stats", 1))) }));
			// [DMF_706] Deathmatch Pavilion (2) — Summon a 3/2 Duelist. If your hero attacked this turn, summon another.
			cards.Add("DMF_706", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Summon(g, c, Cards.FromId("DMF_706t"), s);
					if (c.Hero.NumAttacksThisTurn > 0) Summon(g, c, Cards.FromId("DMF_706t"), s);
				})
			}));
			// [DMF_707] Magicfin (3) — After a friendly Murloc dies, add a random Legendary minion to your hand.
			cards.Add("DMF_707", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					Condition = SelfCondition.IsRace(Race.MURLOC),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (RandomCard(g, x => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY) is Card card) AddToHand(c, card);
					})
				}
			}));
			// [DMF_708] Inara Stormcrash (5) — On your turn, your hero has +2 Attack and Windfury.
			cards.Add("DMF_708", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HERO, "DMF_708e") { Condition = new SelfCondition(p => p.Game.CurrentPlayer == p.Controller), Restless = true }
			}));
			cards.Add("DMF_708e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("DMF_708", "attack", 2)), Effects.Windfury) }));
			// [YOP_022] Mistrunner (5) — Battlecry: Give a friendly minion +3/+3. Overload: (1)
			cards.Add("YOP_022", new CardDef(BattlecryFriendlyMinion, new Power { PowerTask = new AddEnchantmentTask("YOP_022e", EntityType.TARGET) }));
			cards.Add("YOP_022e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("YOP_022", "stats", 3))) }));
			// [YOP_023] Landslide (2) — Deal 1 damage to all enemy minions. If you're Overloaded, deal 1 damage again.
			cards.Add("YOP_023", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("YOP_023", "damage", 1), EntityType.OP_MINIONS, true),
					ComplexTask.If((g, c, s) => Overloaded(c), new DamageTask(V("YOP_023", "damage", 1), EntityType.OP_MINIONS, true)))
			}));

			// ---------------------------------------------------------------- Warlock

			// [DMF_110] Fire Breather (4) — Battlecry: Deal 2 damage to all minions except Demons.
			cards.Add("DMF_110", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => m.Id != s.Id && !m.IsRace(Race.DEMON)).ToList())
						Generic.DamageCharFunc((IPlayable)s, m, V("DMF_110", "damage", 2), false);
				})
			}));
			// [DMF_111] Man'ari Mosher (3) — Battlecry: Give a friendly Demon +3 Attack and Lifesteal this turn.
			cards.Add("DMF_111", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_RACE, (int)Race.DEMON } },
				new Power { PowerTask = new AddEnchantmentTask("DMF_111e", EntityType.TARGET) }));
			cards.Add("DMF_111e", OneTurn(Effects.Attack_N(V("DMF_111", "attack", 3)), Effects.Lifesteal));
			// [DMF_113] Free Admission (3) — Draw 2 minions. If they're both Demons, reduce their Costs by (2).
			cards.Add("DMF_113", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var drawn = new List<IPlayable>();
					for (int i = 0; i < V("DMF_113", "minions", 2); i++)
						if (DrawOne(g, c, p => p is Minion) is IPlayable m) drawn.Add(m);
					if (drawn.Count == 2 && drawn.All(p => p is Minion m && m.IsRace(Race.DEMON))) GiveAll(g, s, drawn, "DMF_113e");
				})
			}));
			cards.Add("DMF_113e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("DMF_113", "discount", 2))) }));
			// [DMF_115] Revenant Rascal (3) — Battlecry: Destroy a Mana Crystal for each player.
			cards.Add("DMF_115", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Controller p in new[] { c, c.Opponent }) if (p.BaseMana > 0) p.BaseMana -= 1;
				})
			}));
			// [DMF_117] Cascading Disaster (4) — Destroy a random enemy minion. Corrupt: Destroy 2. Corrupt Again: Destroy 3.
			ISimpleTask DestroyRandomEnemies(string id, int n) => new CustomTask((g, c, s, t, stack) =>
			{
				var enemies = c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
				for (int i = 0; i < V(id, "minions", n) && enemies.Count > 0; i++)
				{
					Minion m = enemies[g.Random.Next(enemies.Count)];
					enemies.Remove(m);
					m.Destroy();
				}
			});
			cards.Add("DMF_117", new CardDef(new Power { PowerTask = DestroyRandomEnemies("DMF_117", 1) }));
			cards.Add("DMF_117t", new CardDef(new Power { PowerTask = DestroyRandomEnemies("DMF_117t", 2) }));
			cards.Add("DMF_117t2", new CardDef(new Power { PowerTask = DestroyRandomEnemies("DMF_117t2", 3) }));
			// [DMF_118] Tickatus (6) — Battlecry: Remove the top 5 cards from your deck. Corrupt: Your opponent's deck instead.
			ISimpleTask RemoveTop(string id, bool opponent) => new CustomTask((g, c, s, t, stack) =>
			{
				Controller who = opponent ? c.Opponent : c;
				int n = V(id, "cards", 5);
				for (int i = 0; i < n && who.DeckZone.Count > 0; i++) Generic.DeckCardToGraveyard(who.DeckZone.TopCard, s?.Id ?? 0, n > 1);
			});
			cards.Add("DMF_118", new CardDef(new Power { PowerTask = RemoveTop("DMF_118", false) }));
			cards.Add("DMF_118t", new CardDef(new Power { PowerTask = RemoveTop("DMF_118t", true) }));
			// [DMF_119] Wicked Whispers (1) — Discard your lowest Cost card. Give your minions +1/+1.
			cards.Add("DMF_119", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) =>
				{
					if (c.HandZone.Count == 0) return;
					int low = c.HandZone.Min(p => p.Cost);
					var lowest = c.HandZone.Where(p => p.Cost == low).ToList();
					Generic.DiscardBlock.Invoke(c, lowest[g.Random.Next(lowest.Count)]);
				}), new AddEnchantmentTask("DMF_119e", EntityType.MINIONS))
			}));
			cards.Add("DMF_119e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DMF_119", "stats", 1))) }));
			// [DMF_533] Ring Matron (6) — Taunt. Deathrattle: Summon two 3/2 Imps.
			cards.Add("DMF_533", new CardDef(new Power { DeathrattleTask = new SummonTask("DMF_533t", V("DMF_533", "imps", 2)) }));
			// [DMF_534] Deck of Chaos (5) — Swap the Cost and Attack of all minions in your deck.
			cards.Add("DMF_534", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.DeckZone.OfType<Minion>().Where(m => Generic.IsMinionCard(m)).ToList())
					{
						int cost = m.Cost, atk = m.AttackDamage;
						Generic.AddEnchantmentBlock(g, Cards.FromId("DMF_534e"), (IPlayable)s, m, cost, 0, 0);
						Generic.AddEnchantmentBlock(g, Cards.FromId("DMF_534e2"), (IPlayable)s, m, atk, 0, 0);
					}
				})
			}));
			cards.Add("DMF_534e", new CardDef(new Power { Enchant = Enchants.Enchants.SetAttackScriptTag }));
			cards.Add("DMF_534e2", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) { UseScriptTag = true } }));
			// [YOP_003] Luckysoul Hoarder (3) — Battlecry: Shuffle 2 Soul Fragments into your deck. Corrupt: Draw a card.
			cards.Add("YOP_003", new CardDef(new Power { PowerTask = ShuffleSoulFragments(V("YOP_003", "fragments", 2)) }));
			cards.Add("YOP_003t", new CardDef(new Power { PowerTask = ComplexTask.Create(ShuffleSoulFragments(V("YOP_003t", "fragments", 2)), new DrawTask()) }));
			// [YOP_004] Envoy Rustwix (5) — Deathrattle: Shuffle 3 random Prime Legendary minions into your deck.
			cards.Add("YOP_004", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var primes = new List<string> { "BT_019t", "BT_028t", "BT_109t", "BT_123t", "BT_136t", "BT_197t", "BT_210t", "BT_309t", "BT_713t" };
					for (int i = 0; i < V("YOP_004", "primes", 3) && primes.Count > 0; i++)
					{
						string id = primes[g.Random.Next(primes.Count)];
						primes.Remove(id);
						Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId(id)));
					}
				})
			}));
			// [YOP_033] Backfire (3) — Draw 3 cards. Deal 3 damage to your hero.
			cards.Add("YOP_033", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new EnqueueTask(V("YOP_033", "cards", 3), new DrawTask()), new DamageTask(V("YOP_033", "damage", 3), EntityType.HERO, true))
			}));

			// ---------------------------------------------------------------- Warrior

			// [DMF_523] Bumper Car (2) — Rush. Deathrattle: Add two 1/1 Riders with Rush to your hand.
			cards.Add("DMF_523", new CardDef(new Power { DeathrattleTask = new AddCardTo("DMF_523t", EntityType.HAND, V("DMF_523", "riders", 2)) }));
			// [DMF_524] Ringmaster's Baton (2) — After your hero attacks, give a Mech, Dragon, and Pirate in your hand +1/+1.
			cards.Add("DMF_524", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						foreach (Race race in new[] { Race.MECHANICAL, Race.DRAGON, Race.PIRATE })
						{
							var pool = c.HandZone.Where(p => p is Minion m && m.IsRace(race)).ToList();
							if (pool.Count > 0) Give(g, s, pool[g.Random.Next(pool.Count)], "DMF_524e");
						}
					})
				}
			}));
			cards.Add("DMF_524e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DMF_524", "stats", 1))) }));
			// [DMF_525] Ringmaster Whatley (5) — Battlecry: Draw a Mech, Dragon, and Pirate.
			cards.Add("DMF_525", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Race race in new[] { Race.MECHANICAL, Race.DRAGON, Race.PIRATE }) DrawOne(g, c, p => p is Minion m && m.IsRace(race));
				})
			}));
			// [DMF_526] Stage Dive (1) — Draw a Rush minion. Corrupt: Give it +2/+1.
			cards.Add("DMF_526", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => DrawOne(g, c, p => p is Minion && p.Card[GameTag.RUSH] > 0)) }));
			cards.Add("DMF_526a", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (DrawOne(g, c, p => p is Minion && p.Card[GameTag.RUSH] > 0) is IPlayable m) Give(g, s, m, "DMF_526e"); })
			}));
			cards.Add("DMF_526e", FromText("DMF_526e"));
			// [DMF_528] Tent Trasher (5) — Rush. Costs (1) less for each friendly minion with a unique minion type.
			cards.Add("DMF_528", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p =>
				{
					var types = System.Enum.GetValues(typeof(Race)).Cast<Race>().Where(r => r != Race.INVALID && r != Race.BLANK && r != Race.ALL);
					return types.Count(r => p.Controller.BoardZone.GetAll().Any(m => m.IsRace(r)));
				})
			}));
			// [DMF_529] E.T.C., God of Metal (2) — After a friendly Rush minion attacks, deal 2 damage to the enemy hero.
			cards.Add("DMF_529", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.MINIONS,
					Condition = new SelfCondition(p => p is Minion m && m.IsRush),
					SingleTask = new DamageTask(V("DMF_529", "damage", 2), EntityType.OP_HERO)
				}
			}));
			// [DMF_530] Feat of Strength (3) — Give a random Taunt minion in your hand +5/+5.
			cards.Add("DMF_530", new CardDef(new Power { PowerTask = BuffRandomInHand(p => p is Minion && p[GameTag.TAUNT] > 0, "DMF_530e") }));
			cards.Add("DMF_530e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DMF_530", "stats", 5))) }));
			// [DMF_531] Stage Hand (2) — Battlecry: Give a random minion in your hand +1/+1.
			cards.Add("DMF_531", new CardDef(new Power { PowerTask = BuffRandomInHand(p => p is Minion, "DMF_531e") }));
			cards.Add("DMF_531e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DMF_531", "stats", 1))) }));
			// [YOP_013] Spiked Wheel (1) — Has +3 Attack while your hero has Armor.
			cards.Add("YOP_013", new CardDef(new Power
			{
				Aura = new Aura(AuraType.SELF, new Effect(GameTag.ATK, EffectOperator.ADD, V("YOP_013", "attack", 3)))
				{
					Condition = new SelfCondition(p => p.Controller.Hero.Armor > 0), Restless = true
				}
			}));
			// [YOP_014] Ironclad (3) — Battlecry: If your hero has Armor, gain +2/+2.
			cards.Add("YOP_014", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.Hero.Armor > 0, new AddEnchantmentTask("YOP_014e", EntityType.SOURCE))
			}));
			cards.Add("YOP_014e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("YOP_014", "stats", 2))) }));

			DarkmoonBatch2(cards);
		}

		/// <summary>Silas Darkmoon's rotation: the minions turn like a ring, so the outermost one of each side switches sides
		/// (Dormant minions stay out of it). The two options are the two directions by where they show (user 2026-10-06: "if
		/// it shows up on the right u click right and it rotates that way"): the left one, DMF_074a (offered first), turns the
		/// chooser's row left — their left-most minion goes to the enemy's left end, the enemy's right-most comes to their
		/// right end; the right one, DMF_074b, turns it right.</summary>
		private static void RotateMinions(Controller c, bool thisWay)
		{
			var mine = c.BoardZone.Where(m => m[GameTag.DORMANT] == 0).ToList();
			var theirs = c.Opponent.BoardZone.Where(m => m[GameTag.DORMANT] == 0).ToList();
			var moves = new List<(Minion, int)>();
			if (thisWay)
			{
				if (mine.Count > 0) moves.Add((mine[0], 0));
				if (theirs.Count > 0) moves.Add((theirs[theirs.Count - 1], int.MaxValue));
			}
			else
			{
				if (mine.Count > 0) moves.Add((mine[mine.Count - 1], int.MaxValue));
				if (theirs.Count > 0) moves.Add((theirs[0], 0));
			}
			Generic.SwitchSides(moves);
		}

		/// <summary>"They cost (0) this turn": a one-turn ADD of (0 - Cost) (see DMF_071e).</summary>
		private static void FreeThisTurn(Game g, IEntity source, IPlayable card, string enchantment)
		{
			if (card?.Zone?.Type == SabberStoneCore.Enums.Zone.HAND)
				Generic.AddEnchantmentBlock(g, Cards.FromId(enchantment), (IPlayable)source, card, 0 - card.Cost, 0, 0);
		}

		private static readonly GameTag[] CThunPieces = { HsTag.PLAYED_CTHUN_EYE, HsTag.PLAYED_CTHUN_BODY, HsTag.PLAYED_CTHUN_MAW, HsTag.PLAYED_CTHUN_HEART };

		/// <summary>A Piece of C'Thun, as live plays it (live-20261006-yogg-cthun): its player tag PLAYED_CTHUN_&lt;piece&gt; = 1 first,
		/// then <paramref name="effect"/>, then FX_DATANUM_1 2 and the ritual (ReuseFX_CThun_DMF_Ritual, no targets). With all
		/// four played C'Thun reforms: the reformation from the hero (DMFFX_CthunTheShattered_Reformation_SpawnToDeck) around
		/// a new C'Thun shuffled into the deck, then the four tags back to 0. (Live puts the reform in the piece's second POWER
		/// block, effect index 1; here it ends the first.)</summary>
		private static ISimpleTask PieceOfCThun(GameTag piece, ISimpleTask effect) => ComplexTask.Create(
			new CustomTask((g, c, s, t, stack) => c[piece] = 1),
			effect,
			new CustomTask((g, c, s, t, stack) =>
			{
				s[GameTag.FX_DATANUM_1] = 2;
				if (g.OpenSubSpell((IPlayable)s, SabberStoneCore.Loader.SubSpells.Named("DMF_254t5", "CThun_DMF_Ritual"), null)) g.CloseSubSpells(s.Id);
				if (CThunPieces.Any(tag => c[tag] == 0)) return;
				bool fx = g.OpenSubSpell((IPlayable)s, SabberStoneCore.Loader.SubSpells.Named("DMF_254t3", "Reformation"), null, c.Hero.Id);
				Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("DMF_254")));
				if (fx) g.CloseSubSpells(s.Id);
				foreach (GameTag tag in CThunPieces) c[tag] = 0;
			}));

		private static CardDef OneTurnCost0 => new CardDef(new Power
		{
			Enchant = new Enchant(Effects.AddCost(0)) { UseScriptTag = true, IsOneTurnEffect = true }
		});

		/// <summary>
		/// "Start of Game: Break into pieces" (C'Thun, the Shattered; Dragon Soul, Shattered). Live (live-20261006-yogg-cthun; live-20261008-120539
		/// 664, Dragon Soul): TAG_SCRIPT_DATA_NUM_1 1, TARGET itself, the card's start-of-game effect showing the pieces (shown copies, then set
		/// aside; OVERRIDE_HISTORY the card, HISTORY_TARGET the pieces); then the real pieces enter the deck unseen (DISPLAYED_CREATOR the card)
		/// and the card is set aside.
		/// </summary>
		private static void BreakIntoPieces(Game g, Controller c, IPlayable card, string[] pieces, string fxPart)
		{
			if (card == null || card.Zone?.Type != SabberStoneCore.Enums.Zone.DECK) return;
			card[GameTag.TAG_SCRIPT_DATA_NUM_1] = 1;
			if (g.History)
				g.PowerHistory.Add(new SabberStoneCore.Kettle.PowerHistoryMetaData { Type = MetaDataType.TARGET, Data = 0, Info = new List<int> { card.Id } });
			bool fx = g.OpenSubSpell(card, SabberStoneCore.Loader.SubSpells.Named(card.Card.Id, fxPart), null);
			var shown = new List<int>();
			foreach (string piece in pieces)
			{
				IPlayable copy = Entity.FromCard(c, Cards.FromId(piece), new EntityData { { GameTag.CREATOR, card.Id }, { GameTag.REVEALED, 1 } }, c.DeckZone);
				c.DeckZone.Remove(copy);
				c.SetasideZone.Add(copy);
				shown.Add(copy.Id);
			}
			if (g.History)
			{
				g.PowerHistory.Add(new SabberStoneCore.Kettle.PowerHistoryMetaData { Type = MetaDataType.OVERRIDE_HISTORY, Data = 0, Info = new List<int> { card.Id } });
				g.PowerHistory.Add(new SabberStoneCore.Kettle.PowerHistoryMetaData { Type = MetaDataType.HISTORY_TARGET, Data = 0, Info = shown });
			}
			if (fx) g.CloseSubSpells(card.Id);
			foreach (string piece in pieces)
				Generic.ShuffleIntoDeck.Invoke(c, card, Entity.FromCard(c, Cards.FromId(piece), new EntityData { { GameTag.DISPLAYED_CREATOR, card.Id } }));
			c.DeckZone.Remove(card);
			c.SetasideZone.Add(card);
		}

		private static void DarkmoonBatch2(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Neutral

			// [DMF_004] Yogg-Saron, Master of Fate (10) — Battlecry: If you've cast 10 spells this game, spin the Wheel of
			// Yogg-Saron. (The wheel's six outcomes are spells cast by the wheel; Rod of Roasting is the rare one.)
			cards.Add("DMF_004", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.NumSpellsPlayedThisGame < V("DMF_004", "spells", 10)) return;
					int rod = V("DMF_004", "rodChance", 5);
					int roll = g.Random.Next(100);
					// Rod of Roasting (the 6th) is the rare outcome; the other five share the rest evenly
					string[] outcomes = { "DMF_004t1", "DMF_004t2", "DMF_004t3", "DMF_004t4", "DMF_004t5", "DMF_004t6" };
					int index = roll < rod ? 6 : 1 + (roll - rod) * 5 / (100 - rod);
					// live (live-20261006-yogg-cthun, Devouring Hunger): FX_DATANUM_1 = the outcome (1-6) and the wheel
					// (DMFFX_YoggSaronGodOfFate_Battlecry_YoggWheel, no targets); the outcome card set aside, revealed, shown big
					// (4000) and targeted; its effect then runs inside Yogg-Saron's block (not cast: no play of its own).
					s[GameTag.FX_DATANUM_1] = index;
					if (g.OpenSubSpell((IPlayable)s, SabberStoneCore.Loader.SubSpells.Named("DMF_004", "YoggWheel"), null)) g.CloseSubSpells(s.Id);
					IPlayable outcome = Entity.FromCard(c, Cards.FromId(outcomes[index - 1]), new EntityData { { GameTag.CREATOR, s.Id } }, c.SetasideZone);
					outcome[GameTag.REVEALED] = 1;
					if (g.History)
					{
						g.PowerHistory.Add(new SabberStoneCore.Kettle.PowerHistoryMetaData { Type = MetaDataType.SHOW_BIG_CARD, Data = 0, Info = new List<int> { outcome.Id, 4000 } });
						g.PowerHistory.Add(new SabberStoneCore.Kettle.PowerHistoryMetaData { Type = MetaDataType.TARGET, Data = 0, Info = new List<int> { outcome.Id } });
					}
					outcome.Card.Power?.PowerTask?.Process(g, c, s, null);
				})
			}));
			// [DMF_004t1] Mysterybox — Cast a random spell for every spell you've cast this game (targets chosen randomly).
			cards.Add("DMF_004t1", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = c.NumSpellsPlayedThisGame;
					for (int i = 0; i < n && g.State != State.COMPLETE; i++)
						new CastRandomSpellTask().Process(g, c, s, null);
				})
			}));
			cards.Add("DMF_004t1e", OneTurnCost0);
			// [DMF_004t2] Hand of Fate — Fill your hand with random spells. They cost (0) this turn.
			cards.Add("DMF_004t2", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int guard = 0; !c.HandZone.IsFull && guard < 12; guard++)
					{
						Card spell = RandomCard(g, x => x.Type == CardType.SPELL);
						if (spell == null) break;
						IPlayable p = Entity.FromCard(c, spell);
						Generic.AddHandPhase.Invoke(c, p);
						FreeThisTurn(g, s, p, "DMF_004t1e");
					}
				})
			}));
			// [DMF_004t3] Curse of Flesh — Fill the board with random minions, then give yours Rush.
			cards.Add("DMF_004t3", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) =>
					{
						foreach (Controller p in new[] { c, c.Opponent })
							for (int guard = 0; !p.BoardZone.IsFull && guard < 7; guard++)
								Summon(g, p, RandomBoardMinion(g, x => true), s);
					}),
					new SetGameTagTask(GameTag.RUSH, 1, EntityType.MINIONS))
			}));
			// [DMF_004t4] Mindflayer Goggles — Take control of three random enemy minions.
			cards.Add("DMF_004t4", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = c.Opponent.BoardZone.Where(m => !m.Untouchable && !m.ToBeDestroyed).ToList();
					for (int i = 0; i < V("DMF_004t4", "minions", 3) && enemies.Count > 0; i++)
					{
						Minion m = enemies[g.Random.Next(enemies.Count)];
						enemies.Remove(m);
						new ControlTask(EntityType.TARGET).Process(g, c, s, m);
					}
				})
			}));
			// [DMF_004t5] Devouring Hunger — Destroy all other minions. Gain their Attack and Health. (Yogg-Saron gains them,
			// as DMF_004t5e "Consume"; the spell's CREATOR is the Yogg-Saron that spun the wheel.)
			cards.Add("DMF_004t5", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					// the wheel runs it with Yogg-Saron as its source (live: Consume's CREATOR is Yogg-Saron)
					Minion yogg = s as Minion ?? (g.IdEntityDic.TryGetValue(s[GameTag.CREATOR], out IPlayable creator) ? creator as Minion : null);
					var others = c.BoardZone.Concat(c.Opponent.BoardZone)
						.Where(m => m != yogg && !m.Untouchable && !m.ToBeDestroyed).ToList();
					int atk = others.Sum(m => m.AttackDamage), hp = others.Sum(m => System.Math.Max(0, m.Health));
					// live: the chomp from the hero on every minion it destroys (then the recorded shadow buff on Yogg-Saron)
					bool chomp = others.Count > 0 && g.OpenSubSpell((IPlayable)s, "ReuseFX_Impact_Beast_Chomp_Small_Purple_Super:8ac3e549e66769b4e9f47e4d12729e11",
						others.Select(m => m.Id), c.Hero.Id);
					foreach (Minion m in others) m.Destroy();
					if (chomp) g.CloseSubSpells(s.Id);
					if (yogg?.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY && (atk > 0 || hp > 0))
						Generic.AddEnchantmentBlock(g, Cards.FromId("DMF_004t5e"), yogg, yogg, atk, hp, 0); // its buff effect: RecordedSubSpells
				})
			}));
			cards.Add("DMF_004t5e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.ATK, EffectOperator.ADD, 0), new Effect(GameTag.HEALTH, EffectOperator.ADD, 0)) { UseScriptTag = true }
			}));
			// [DMF_004t6] Rod of Roasting — Cast 'Pyroblast' randomly until a hero dies.
			cards.Add("DMF_004t6", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card pyroblast = Cards.TryFromId("EX1_279");
					for (int guard = 0; pyroblast != null && guard < 60 && g.State != State.COMPLETE; guard++)
					{
						if (c.Hero.Health <= 0 || c.Hero.ToBeDestroyed || c.Opponent.Hero.Health <= 0 || c.Opponent.Hero.ToBeDestroyed) break;
						new CastRandomSpellTask().Process(g, c, s, Entity.FromCard(c, pyroblast));
					}
				})
			}));

			// [DMF_056] G'huun the Blood God (8) — Battlecry: Draw 2 cards. They cost Health instead of Mana.
			cards.Add("DMF_056", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("DMF_056", "draw", 2); i++)
						if (Generic.Draw(c) is IPlayable drawn && drawn.Zone?.Type == SabberStoneCore.Enums.Zone.HAND)
							Give(g, s, drawn, "DMF_056e");
				})
			}));
			cards.Add("DMF_056e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.CARD_COSTS_HEALTH, EffectOperator.SET, 1)) }));

			// [DMF_074] Silas Darkmoon (7) — Battlecry: Choose a direction to rotate all minions.
			cards.Add("DMF_074", new CardDef(new Power { PowerTask = ChooseAndCast("DMF_074a", "DMF_074b") }));
			cards.Add("DMF_074a", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => RotateMinions(c, true)) }));
			cards.Add("DMF_074b", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => RotateMinions(c, false)) }));

			// [DMF_116] The Nameless One (6) — Battlecry: Choose a minion. Become a 4/4 copy of it, then Silence it.
			cards.Add("DMF_116", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion target) || !(s is Minion me) || target.Id == me.Id) return;
					new TransformCopyTask().Process(g, c, s, target);
					if (g.IdEntityDic.TryGetValue(s.Id, out IPlayable copy) && copy.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY)
						Generic.AddEnchantmentBlock(g, Cards.FromId("DMF_116e"), copy, copy, 0, 0, 0);
					new SilenceTask(EntityType.TARGET).Process(g, c, s, target);
				})
			}));
			cards.Add("DMF_116e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("DMF_116", "stats", 4))) }));

			// [DMF_188] Y'Shaarj, the Defiler (10) — Battlecry: Add a copy of each Corrupted card you've played this game to
			// your hand. They cost (0) this turn.
			cards.Add("DMF_188", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Card played in c.PlayHistory.Select(h => h.SourceCard).Where(x => x != null && x[HsTag.CORRUPTED_CARD] > 0).ToList())
					{
						if (c.HandZone.IsFull) break;
						IPlayable copy = Entity.FromCard(c, played);
						Generic.AddHandPhase.Invoke(c, copy);
						FreeThisTurn(g, s, copy, "DMF_188e2");
					}
				})
			}));
			cards.Add("DMF_188e2", OneTurnCost0);

			// [DMF_230] Il'gynoth (4) — Lifesteal. Your Lifesteal damages the enemy hero instead of healing you.
			cards.Add("DMF_230", new CardDef(new Power { Aura = new Aura(AuraType.CONTROLLER, new Effect(HsTag.LIFESTEAL_DAMAGES_ENEMY, EffectOperator.ADD, 1)) }));

			// [DMF_254] C'Thun, the Shattered (10) — Start of Game: Break into pieces. Battlecry: Deal 30 damage randomly split
			// among all enemies. (Its four pieces replace it in the deck; with all four played it reforms into the deck.)
			cards.Add("DMF_254", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.GAME_START)
				{
					TriggerActivation = TriggerActivation.DECK,
					// live (live-20261006-yogg-cthun): after the engine's reveal block, TAG_SCRIPT_DATA_NUM_1 1, TARGET itself, the
					// spawn-to-deck effect (DMFFX_SpawnToDeck_CthunTheShattered) showing the four pieces flying into the deck
					// (shown copies, then set aside; OVERRIDE_HISTORY C'Thun, HISTORY_TARGET the pieces); then the real pieces enter
					// the deck unseen (DISPLAYED_CREATOR C'Thun) and C'Thun is set aside.
					SingleTask = new CustomTask((g, c, s, t, stack) =>
						BreakIntoPieces(g, c, s as IPlayable, new[] { "DMF_254t3", "DMF_254t5", "DMF_254t4", "DMF_254t7" }, "SpawnToDeck")),
					RemoveAfterTriggered = true
				},
				PowerTask = new EnqueueTask(V("DMF_254", "damage", 30), ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, 1))
			}));
			// [DMF_254t3] Eye of C'Thun — Piece of C'Thun. Deal $7 damage randomly split among all enemies.
			cards.Add("DMF_254t3", new CardDef(new Power
			{
				PowerTask = PieceOfCThun(HsTag.PLAYED_CTHUN_EYE, new EnqueueTask(V("DMF_254t3", "damage", 7), ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, 1), true))
			}));
			// [DMF_254t4] Heart of C'Thun — Piece of C'Thun. Deal $3 damage to all minions.
			cards.Add("DMF_254t4", new CardDef(new Power
			{
				PowerTask = PieceOfCThun(HsTag.PLAYED_CTHUN_HEART, new DamageTask(V("DMF_254t4", "damage", 3), EntityType.ALLMINIONS, true))
			}));
			// [DMF_254t5] Body of C'Thun — Piece of C'Thun. Summon a 6/6 C'Thun's Body with Taunt.
			cards.Add("DMF_254t5", new CardDef(new Power
			{
				PowerTask = PieceOfCThun(HsTag.PLAYED_CTHUN_BODY, new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("DMF_254t5t"), s)))
			}));
			// [DMF_254t7] Maw of C'Thun — Piece of C'Thun. Destroy a minion.
			cards.Add("DMF_254t7", new CardDef(TargetMinion, new Power
			{
				PowerTask = PieceOfCThun(HsTag.PLAYED_CTHUN_MAW, new DestroyTask(EntityType.TARGET))
			}));

			// [YOP_012] Deathwarden (4) — Deathrattles can't trigger. (Both players: HsTag.DEATHRATTLES_CANT_TRIGGER.)
			cards.Add("YOP_012", new CardDef(new Power { Aura = new Aura(AuraType.CONTROLLERS, new Effect(HsTag.DEATHRATTLES_CANT_TRIGGER, EffectOperator.ADD, 1)) }));

			// [YOP_018] Keywarden Ivory (5) — Battlecry: Discover a Dual Class spell from any class. Spellburst: Get another copy.
			// (The picked card is captured in YOP_018e "Key to Victory", which holds the Spellburst.)
			cards.Add("YOP_018", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card self = Cards.GeneratingCard(g);
					var pool = Cards.GenerationPool(g, c).Where(x => x.Type == CardType.SPELL && x.Id != self?.Id &&
						System.Numerics.BitOperations.PopCount((uint)x[GameTag.MULTIPLE_CLASSES]) == 2);
					var offered = Pick(g, pool, 3).Select(card => Entity.FromCard(c, card, new EntityData
					{
						{ GameTag.CREATOR, s.Id }, { GameTag.DISPLAYED_CREATOR, s.Id }
					}, c.SetasideZone)).ToList();
					if (offered.Count == 0) return;
					int ivory = s.Id;
					Generic.CreateChoiceEntities(c, s, offered, ChoiceAction.STACK, new CustomTask((g2, c2, s2, chosen, stack2) =>
					{
						if (chosen == null) return;
						chosen.Zone?.Remove(chosen);
						if (c2.HandZone.IsFull) c2.GraveyardZone.Add(chosen);
						else Generic.AddHandPhase.Invoke(c2, chosen); // ("after you Discover": Generic.NoteDiscovered at the pick)
						if (g2.IdEntityDic.TryGetValue(ivory, out IPlayable me) && me.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY)
							Generic.AddEnchantmentBlock(g2, Cards.FromId("YOP_018e"), me, me, 0, 0, chosen.Id);
					}));
				})
			}));
			cards.Add("YOP_018e", new CardDef(new Power
			{
				Trigger = Spellburst(new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Enchantment e) || e.CapturedCard == null) return;
					if (e.Target is IPlayable owner) owner[HsTag.SPELLBURST] = 0;
					AddToHand(c, e.CapturedCard);
					RemoveEnchantmentTask.Task.Process(g, c, s, null);
				}))
			}));

			// [YOP_035] Moonfang (5) — Can only take 1 damage at a time.
			cards.Add("YOP_035", new CardDef(new Power { Aura = new Aura(AuraType.SELF, new Effect(HsTag.TAKES_ONE_DAMAGE, EffectOperator.SET, 1)) }));

			// ---------------------------------------------------------------- Demon Hunter

			// [YOP_007] Dark Inquisitor Xanesh (5) — Battlecry: Reduce the Cost of all Corrupt and Corrupted cards in your hand and
			// deck by (2).
			cards.Add("YOP_007", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in c.HandZone.Concat(c.DeckZone).Where(p => p.Card[HsTag.CORRUPT] > 0 || p.Card[HsTag.CORRUPTED_CARD] > 0).ToList())
						Give(g, s, p, "YOP_007e");
				})
			}));
			cards.Add("YOP_007e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("YOP_007", "cost", 2))) }));

			// ---------------------------------------------------------------- Hunter

			// [DMF_087] Trampling Rhino (5) — Rush. After this attacks and kills a minion, excess damage hits the enemy hero.
			// (The defender's Health below 0 after the hit is the excess.)
			cards.Add("DMF_087", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					Condition = new SelfCondition(p => KilledItsDefender(p.Game)),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventTarget is Minion defender && defender.Health < 0)
							Generic.DamageCharFunc((IPlayable)s, c.Opponent.Hero, -defender.Health, false);
					})
				}
			}));

			// ---------------------------------------------------------------- Mage

			// [DMF_236] Oh My Yogg! (1) — Secret: When your opponent casts a spell, they instead cast a random one of the same Cost.
			cards.Add("DMF_236", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.CAST_SPELL)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = ComplexTask.Secret(
						new CounterTask(EntityType.TARGET),
						new FuncNumberTask((IPlayable p) =>
						{
							Trigger.InvalidateAll(p.Game); // as Counterspell: no cast triggers of a countered spell (wiki, patch 11.2.0)
							return 0;
						}),
						new CustomTask((g, c, s, t, stack) =>
						{
							if (t == null) return;
							int cost = PlayedCost(t);
							new CastRandomSpellTask(card => card.Cost == cost && card.Id != t.Card.Id).Process(g, t.Controller, s, null);
						})),
					FastExecution = true
				}
			}));

			// ---------------------------------------------------------------- Paladin

			// [YOP_008] Lightsteed (4) — Your healing effects also give affected minions +2 Health.
			cards.Add("YOP_008", new CardDef(new Power { Aura = new Aura(AuraType.CONTROLLER, new Effect(HsTag.HEALING_GIVES_HEALTH, EffectOperator.ADD, 1)) }));
			cards.Add("YOP_008e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("YOP_008", "health", 2))) }));
			// [YOP_010] Imprisoned Celestial (3) — Dormant for 2 turns. Spellburst: Give your minions Divine Shield.
			cards.Add("YOP_010", new CardDef(new Power { Trigger = Spellburst(new SetGameTagTask(GameTag.DIVINE_SHIELD, 1, EntityType.MINIONS)) }));

			// ---------------------------------------------------------------- Rogue

			// [DMF_075] Guess the Weight (2) — Draw a card. Guess if your next card costs more or less to draw it. (The drawn
			// card's Cost is kept on the spell; the guess compares the top card of the deck with it, a tie is a wrong guess.)
			cards.Add("DMF_075", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(Generic.Draw(c) is IPlayable drawn)) return;
					s[GameTag.TAG_SCRIPT_DATA_NUM_1] = drawn.Cost;
					ChooseAndCast("DMF_075a", "DMF_075a2").Process(g, c, s, null);
				})
			}));
			cards.Add("DMF_075a", new CardDef(new Power { PowerTask = GuessTheWeight(more: true) }));
			cards.Add("DMF_075a2", new CardDef(new Power { PowerTask = GuessTheWeight(more: false) }));
			// [YOP_017] Shenanigans (2) — Secret: When your opponent draws their second card in a turn, transform it into a Banana.
			cards.Add("YOP_017", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DRAW)
				{
					TriggerSource = TriggerSource.ENEMY,
					Condition = new SelfCondition(p => p.Controller.NumCardsDrawnThisTurn == V("YOP_017", "draw", 2)),
					SingleTask = ComplexTask.Secret(new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable drawn = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (drawn?.Zone?.Type == SabberStoneCore.Enums.Zone.HAND)
							new TransformTask("EX1_014t", EntityType.TARGET).Process(g, c, s, drawn);
					}))
				}
			}));

			// ---------------------------------------------------------------- Shaman

			// [YOP_024] Guidance (1) — Look at two spells. Add one to your hand or Overload: (1) to get both. (Offered: the two
			// spells and Spirit Path, YOP_024t; picking it adds both and Overloads.)
			cards.Add("YOP_024", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card self = Cards.GeneratingCard(g);
					var spells = Pick(g, ClassCards(c, CardType.SPELL).Where(x => x.Id != self?.Id), V("YOP_024", "spells", 2));
					if (spells.Count == 0) return;
					var offered = spells.Append(Cards.FromId("YOP_024t")).Select(card => Entity.FromCard(c, card, new EntityData
					{
						{ GameTag.CREATOR, s.Id }, { GameTag.DISPLAYED_CREATOR, s.Id }
					}, c.SetasideZone)).ToList();
					var spellIds = offered.Take(spells.Count).Select(p => p.Id).ToArray();
					Generic.CreateChoiceEntities(c, s, offered, ChoiceAction.STACK, new CustomTask((g2, c2, s2, chosen, stack2) =>
					{
						if (chosen == null) return;
						bool both = chosen.Card.Id == "YOP_024t";
						foreach (int id in both ? spellIds : new[] { chosen.Id })
						{
							if (!g2.IdEntityDic.TryGetValue(id, out IPlayable spell)) continue;
							spell.Zone?.Remove(spell);
							if (c2.HandZone.IsFull) c2.GraveyardZone.Add(spell);
							else Generic.AddHandPhase.Invoke(c2, spell);
						}
						if (both) Generic.OverloadBlock(c2, chosen, g2.History);
					}));
				})
			}));

			// ---------------------------------------------------------------- Warrior / other

			// [DMF_195] Snack Run (2) — Discover a spell. Restore Health to your hero equal to its Cost.
			cards.Add("DMF_195", new CardDef(new Power
			{
				PowerTask = new DiscoverTask(CardType.SPELL, CardClass.INVALID, default, ChoiceAction.HAND, new CustomTask((g, c, s, t, stack) =>
				{
					if (t != null && t.Cost > 0) c.Hero.TakeHeal((IPlayable)s, t.Cost);
				}))
			}));
			// [DMF_249] Acrobatics (3) — Draw 2 cards. If you play both this turn, draw 2 more. Live (live-20261006-yogg-cthun):
			// one DMF_249e on the player (one-turn) holding the two drawn cards (TAG_SCRIPT_DATA_NUM_1 / _2 = their entity
			// ids, set as each is drawn), after the draw effect from the spell to the hero.
			cards.Add("DMF_249", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (g.OpenSubSpell((IPlayable)s, "ReuseFX_DrawBoosh_DemonHunter_Small_Super:425f23a1c2069a9419bc4bff0c753427", new[] { c.Hero.Id }))
						g.CloseSubSpells(s.Id);
					Generic.AddEnchantmentBlock(g, Cards.FromId("DMF_249e"), (IPlayable)s, c, 0, 0, 0);
					var mark = c.AppliedEnchantments?.LastOrDefault(e => e.Card.Id == "DMF_249e");
					foreach (GameTag slot in new[] { GameTag.TAG_SCRIPT_DATA_NUM_1, GameTag.TAG_SCRIPT_DATA_NUM_2 })
						if (Generic.Draw(c) is IPlayable drawn && drawn.Zone?.Type == SabberStoneCore.Enums.Zone.HAND && mark != null)
							mark[slot] = drawn.Id;
				})
			}));
			cards.Add("DMF_249e", new CardDef(new Power
			{
				Enchant = new Enchant { IsOneTurnEffect = true },
				Trigger = new Trigger(TriggerType.PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable played = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (!(s is Enchantment e) || played == null) return;
						if (played.Id == e[GameTag.TAG_SCRIPT_DATA_NUM_1]) e[HsTag.ACROBATICS_PLAYED] |= 1;
						else if (played.Id == e[GameTag.TAG_SCRIPT_DATA_NUM_2]) e[HsTag.ACROBATICS_PLAYED] |= 2;
						else return;
						if (e[HsTag.ACROBATICS_PLAYED] != 3) return;
						for (int i = 0; i < V("DMF_249", "more", 2); i++) Generic.Draw(c);
						RemoveEnchantmentTask.Task.Process(g, c, s, null);
					})
				}
			}));
		}

		/// <summary>Guess the Weight's guess: the top card of the deck against the drawn card's Cost (kept on the spell that
		/// offered the guess); right (strictly more / less) draws it.</summary>
		private static ISimpleTask GuessTheWeight(bool more) => new CustomTask((g, c, s, t, stack) =>
		{
			IPlayable guess = Bob(g, s);
			if (guess == s || c.DeckZone.Count == 0) return;
			int drawnCost = guess[GameTag.TAG_SCRIPT_DATA_NUM_1];
			int next = c.DeckZone.TopCard.Cost;
			if (more ? next > drawnCost : next < drawnCost)
				Generic.Draw(c);
		});
	}
}
