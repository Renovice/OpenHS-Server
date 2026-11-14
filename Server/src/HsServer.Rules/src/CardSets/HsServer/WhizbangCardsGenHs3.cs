// HsServer addition (not upstream SabberStone): Whizbang's Workshop (WHIZBANG, set 1897) — chunk 3: Priest, Rogue, Paladin (spec: research
// spec_chunk3). Shared pieces: the extra turn (Generic.GrantExtraTurn), Paladin Auras' end-of-turn effect in the countdown block
// (PaladinAura countdownEffect) and their duration (Generic.ExtendAuras), copies keeping their script numbers (Generic.Copy), Miniaturize /
// Gigantify / trade-to-upgrade (chunk 1). Every number goes through CardValues (V).
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
		private static bool HolySpellEntry(PlayHistoryEntry h) => h.SourceCard?.Type == CardType.SPELL && h.SourceCard[SPELL_SCHOOL] == HOLY;

		private static void WhizbangChunk3(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Priest
			// [MIS_305] Delayed Product (4) — Discover and summon a minion that costs (8) or more. It goes Dormant for 2 turns.
			cards.Add("MIS_305", new CardDef(new Power
			{
				PowerTask = new DiscoverFromTask((g, c, s) => Cards.FormatTypeCards(g.FormatType).Where(x => x.Type == CardType.MINION && x.Cost >= V("MIS_305", "cost", 8) &&
						!x.NeverRandomlyOnBoard && (x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL))), null,
					new CustomTask((g, c, s, t, stack) => { if (t is Minion m && m.Zone?.Type == Zone.PLAY) Generic.MakeDormant(m, V("MIS_305", "turns", 2), "MIS_305e2", (IPlayable)s); }),
					ChoiceAction.SUMMON)
			}));
			// [MIS_714] Funhouse Mirror (3) — Summon a copy of an enemy minion. It attacks the original.
			cards.Add("MIS_714", new CardDef(TargetEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion original) || c.BoardZone.IsFull) return;
					if (Generic.Copy(c, s, original, Zone.PLAY) is Minion copy && copy.Zone?.Type == Zone.PLAY && original.Zone?.Type == Zone.PLAY && !original.IsDead)
						EffectAttack(g, copy, original);
				})
			}));
			// [MIS_919] Puppet Theatre (4, Location) — Choose an enemy minion. Get a 1/1 copy of it that costs (1). (live 1005 7407: a base copy)
			cards.Add("MIS_919", new CardDef(TargetEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m && Generic.Copy(c, s, m, Zone.HAND) is IPlayable copy && copy.Zone?.Type == Zone.HAND) Give(g, s, copy, "MIS_919e");
				})
			}));
			cards.Add("MIS_919e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.SetAttackHealth(V("MIS_919", "stats", 1)).Append(Effects.SetCost(V("MIS_919", "cost", 1))).ToArray())
			}));
			// [TOY_380] Clay Matriarch (6) — Miniaturize. Taunt. Deathrattle: Summon a 4/4 Whelp with Elusive.
			cards.Add("TOY_380", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("TOY_380t2"), s)) }));
			// [TOY_381] Papercraft Angel (2) — Your Hero Power costs (0).
			cards.Add("TOY_381", new CardDef(new Power { Aura = new Aura(AuraType.HEROPOWER, Effects.SetCost(0)) }));
			// [TOY_382] Careless Crafter (3) — Deathrattle: Get two 0-Cost Bandages that restore #3 Health. / [TOY_382t] Bandage (your target)
			cards.Add("TOY_382", new CardDef(new Power { DeathrattleTask = new AddCardTo("TOY_382t", EntityType.HAND, V("TOY_382", "bandages", 2)) }));
			cards.Add("TOY_382t", new CardDef(TargetAny, new Power { PowerTask = new HealTask(V("TOY_382t", "heal", 3), EntityType.TARGET) }));
			// [TOY_383] Raza the Resealed (5) — Battlecry: Shuffle copies of 5 random friendly minions that died this game into your deck. They
			// cost (0).
			cards.Add("TOY_383", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var dead = c.DiedThisGame.Select(id => g.IdEntityDic.TryGetValue(id, out IPlayable p) ? p : null).Where(p => p is Minion).ToList();
					foreach (IPlayable d in RandomOf(g, dead, V("TOY_383", "minions", 5)))
					{
						IPlayable copy = Generic.NewFromGraveyard(c, d);
						if (copy == null) continue;
						Generic.AddEnchantmentBlock(g, Cards.FromId("TOY_383e2"), (IPlayable)s, copy, 0, 0, 0);
						Generic.ShuffleIntoDeck.Invoke(c, s, copy);
					}
				})
			}));
			cards.Add("TOY_383e2", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(V("TOY_383", "cost", 0))) }));
			// [TOY_384] Purifying Power (2) — Silence all friendly minions, then give them +1/+2.
			cards.Add("TOY_384", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var mine = c.BoardZone.GetAll().Where(m => !(m is Location) && !m.Untouchable).ToList();
					foreach (Minion m in mine) m.Silence();
					GiveAll(g, s, mine.Where(m => m.Zone?.Type == Zone.PLAY).Cast<IPlayable>().ToList(), "TOY_384e");
				})
			}));
			cards.Add("TOY_384e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TOY_384", "attack", 1)), Effects.Health_N(V("TOY_384", "health", 2))) }));
			// [TOY_385] Timewinder Zarimi (5) — Battlecry: Once per game, if you've played 8 other Dragons, take an extra turn. ({0} left!)
			cards.Add("TOY_385", new CardDef(new Power
			{
				ThresholdCount = p => p.Controller.PlayHistory.Count(h => h.SourceId != p.Id && h.SourceCard?.Type == CardType.MINION && h.SourceCard.IsRace(Race.DRAGON)),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!Generic.ThresholdMet((IPlayable)s) || c[HsTag.ZARIMI_USED] > 0) return;
					c[HsTag.ZARIMI_USED] = 1;
					Generic.GrantExtraTurn(c, s);
				})
			}));
			// [TOY_387] Scale Replica (2) — Draw your lowest and highest Cost Dragon.
			cards.Add("TOY_387", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (bool lowest in new[] { true, false })
					{
						var dragons = c.DeckZone.GetAll().Where(p => Generic.IsMinionCard(p) && p.Card.IsRace(Race.DRAGON)).ToList();
						if (dragons.Count == 0) break;
						int cost = lowest ? dragons.Min(p => p.Cost) : dragons.Max(p => p.Cost);
						var pick = dragons.Where(p => p.Cost == cost).ToList();
						Generic.Draw(c, pick[g.Random.Next(pick.Count)]);
					}
				})
			}));
			// [TOY_388] Chalk Artist (4) — Battlecry: Draw a minion. Transform it into a random Legendary one (keeping its original stats and Cost).
			cards.Add("TOY_388", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(DrawOne(g, c, p => p is Minion) is Minion drawn) || !(RandomCard(g, x => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY) is Card legend)) return;
					int atk = drawn.AttackDamage, health = drawn.Health, cost = drawn.Cost;
					IPlayable changed = Generic.ChangeEntityBlock(c, drawn, legend, true);
					if (changed == null) return;
					Generic.AddEnchantmentBlock(g, Cards.FromId("TOY_388e2"), (IPlayable)s, changed, atk, health, 0);
					Generic.AddEnchantmentBlock(g, Cards.FromId("TOY_388e3"), (IPlayable)s, changed, cost, 0, 0);
				})
			}));
			cards.Add("TOY_388e2", new CardDef(new Power { Enchant = Enchants.Enchants.SetAttackHealthScriptTag }));
			cards.Add("TOY_388e3", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) { UseScriptTag = true } }));
			// [TOY_714] Fly Off the Shelves (3) — Deal $1 damage to all enemy minions. Repeat for each Dragon you're holding.
			cards.Add("TOY_714", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int waves = 1 + c.HandZone.GetAll().Count(p => Generic.IsMinionCard(p) && p.Card.IsRace(Race.DRAGON));
					for (int i = 0; i < waves; i++)
					{
						DamageAll(s, c.Opponent.BoardZone.GetAll().Where(m => !(m is Location)).Cast<ICharacter>(), V("TOY_714", "damage", 1), true);
						g.DeathProcessingAndAuraUpdate();
					}
				})
			}));
			// [TOY_879] Repackage (7) — Stuff all minions into a 2-Cost Box, then shuffle it into the opponent's deck. (wiki: removed from the game)
			cards.Add("TOY_879", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => !(m is Location) && !m.Untouchable).ToList();
					var data = new EntityData { { GameTag.CREATOR, s.Id }, { GameTag.TAG_SCRIPT_DATA_NUM_1, Math.Min(14, minions.Count) }, { GameTag.USE_ALTERNATE_CARD_TEXT, 1 } };
					for (int i = 0; i < Math.Min(14, minions.Count); i++) data[HsTag.REPACKAGED_BASE + i] = minions[i].Card.AssetId;
					foreach (Minion m in minions) Generic.RemoveFromGame(m, s);
					Generic.ShuffleIntoDeck.Invoke(c.Opponent, s, Entity.FromCard(c.Opponent, Cards.FromId("TOY_879t"), data));
				})
			}));
			cards.Add("TOY_879t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < Math.Min(14, s[GameTag.TAG_SCRIPT_DATA_NUM_1]); i++)
						if (Cards.FromAssetId(s[HsTag.REPACKAGED_BASE + i]) is Card card) GetCard(c, card, s);
				})
			}));

			// ---------------------------------------------------------------- Rogue
			// [MIS_706] Dust Bunny (3) — Battlecry and Deathrattle: Add a random piece of junk to your hand (a Coin, Rock, Banana, or Knife).
			ISimpleTask junk = new CustomTask((g, c, s, t, stack) =>
			{
				var pool = ReprintCards.RelatedCardIds("MIS_706")?.Select(Cards.TryFromId).Where(x => x != null).ToList();
				if (pool != null && pool.Count > 0) GetCard(c, pool[g.Random.Next(pool.Count)], s);
			});
			cards.Add("MIS_706", new CardDef(new Power { PowerTask = junk, DeathrattleTask = junk }));
			// [MIS_708] Twisted Pack (1) — Add 5 random cards from other classes to your hand. They are Temporary.
			cards.Add("MIS_708", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var pool = OtherClassCards(g, c).Where(x => !x.NeverGenerated).ToList();
					for (int i = 0; i < V("MIS_708", "cards", 5) && pool.Count > 0; i++)
						if (GetCard(c, pool[g.Random.Next(pool.Count)], s) is IPlayable got) Generic.MakeTemporary(got, s);
				})
			}));
			// [MIS_903] Dubious Purchase (4) — Draw 3 cards. Combo: Destroy a random enemy minion.
			cards.Add("MIS_903", new CardDef(new Power
			{
				PowerTask = ComplexTask.Repeat(new DrawTask(), V("MIS_903", "cards", 3)),
				ComboTask = ComplexTask.Create(ComplexTask.Repeat(new DrawTask(), V("MIS_903", "cards", 3)), new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = c.Opponent.BoardZone.GetAll().Where(m => !(m is Location) && !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
					if (enemies.Count > 0) Generic.Destroy(enemies[g.Random.Next(enemies.Count)], s);
				}))
			}));
			// [TOY_505] Toy Boat (2) — After you summon a Pirate, draw a card.
			cards.Add("TOY_505", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON) { TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF, Condition = SelfCondition.IsRace(Race.PIRATE), SingleTask = new DrawTask() }
			}));
			// [TOY_510] Dig for Treasure (1) — Draw a minion. If it's a Pirate, get a Coin.
			cards.Add("TOY_510", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (DrawOne(g, c, p => p is Minion) is IPlayable drawn && IsPirate(drawn)) GetCard(c, Cards.FromId("GAME_005"), s);
				})
			}));
			// [TOY_511] Shoplifter Goldbeard (5) — After you summon a Pirate, summon a copy of it that attacks a random enemy, then dies. (forum
			// 136134: two Goldbeards chain; one doesn't copy its own copy)
			cards.Add("TOY_511", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					Condition = SelfCondition.IsRace(Race.PIRATE),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(t is Minion pirate) || pirate[HsTag.GOLDBEARD_COPY] == s.Id || c.BoardZone.IsFull) return;
						if (!(Generic.Copy(c, s, pirate, Zone.PLAY) is Minion copy) || copy.Zone?.Type != Zone.PLAY) return;
						copy[HsTag.GOLDBEARD_COPY] = s.Id;
						var enemies = AttackableEnemies(c);
						if (enemies.Count > 0) EffectAttack(g, copy, enemies[g.Random.Next(enemies.Count)]);
						if (copy.Zone?.Type == Zone.PLAY && !copy.ToBeDestroyed) Generic.Destroy(copy, s);
					})
				}
			}));
			// [TOY_512] The Crystal Cove (3, Location) — The next minion you summon this turn has its stats set to 4/4.
			cards.Add("TOY_512", new CardDef(new Power { PowerTask = new AddEnchantmentTask("TOY_512e1", EntityType.CONTROLLER) }));
			cards.Add("TOY_512e1", new CardDef(new Power
			{
				Enchant = new Enchant { IsOneTurnEffect = true },
				Trigger = new Trigger(TriggerType.SUMMON)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(t is Minion m) || !(s is Enchantment e)) return;
						Give(g, s, m, "TOY_512e");
						RemoveEnchantmentTask.Task.Process(g, c, e, null);
					})
				}
			}));
			cards.Add("TOY_512e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("TOY_512", "stats", 4))) }));
			// [TOY_514] Thistle Tea Set (2) — Discover a spell from another class. Get a copy of it.
			cards.Add("TOY_514", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.Where((c, x) => x.Type == CardType.SPELL && !x.HasClass(c.HeroClass) && !x.HasClass(CardClass.NEUTRAL), null,
					new CustomTask((g, c, s, t, stack) => { if (t != null) GetCard(c, t.Card, s); }))
			}));
			// [TOY_515] Sonya Waterdancer (4) — After you play a 1-Cost minion, get a copy of it that costs (0). (the Cost it was played for)
			cards.Add("TOY_515", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(t is Minion m) || m.Id == s.Id || PlayedCost(m) != V("TOY_515", "cost", 1)) return;
						if (GetCard(c, m.Card, s) is IPlayable copy) Give(g, s, copy, "TOY_515e3");
					})
				}
			}));
			cards.Add("TOY_515e3", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) }));
			// [TOY_516] Bargain Bin Buccaneer (3) — Rush. Combo: Summon a copy of this.
			cards.Add("TOY_516", new CardDef(new Power
			{
				ComboTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me && !c.BoardZone.IsFull) Generic.Copy(c, s, me, Zone.PLAY); })
			}));
			// [TOY_519] Everything Must Go! (8) — Summon two random 4-Cost minions. Costs (1) less for each card you've drawn this turn.
			cards.Add("TOY_519", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.NumCardsDrawnThisTurn),
				PowerTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("TOY_519", "minions", 2); i++) Summon(g, c, RandomMinionOfCost(g, V("TOY_519", "cost", 4)), s); })
			}));
			// [TOY_521] Sandbox Scoundrel (5) — Miniaturize. Battlecry: Your next card this turn costs (2) less.
			cards.Add("TOY_521", new CardDef(new Power { PowerTask = new AddEnchantmentTask("TOY_521e", EntityType.CONTROLLER) }));
			cards.Add("TOY_521e", NextCardEffect(Effects.ReduceCost(V("TOY_521", "less", 2)), new SelfCondition(p => true), thisTurnOnly: true));
			// [TOY_522] Watercannon (4, weapon) — After your hero attacks, summon a 1/1 Pirate that attacks a random enemy.
			cards.Add("TOY_522", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(SummonNew(g, c, Cards.FromId("TOY_522t"), s) is Minion slider)) return;
						var enemies = AttackableEnemies(c);
						if (enemies.Count > 0) EffectAttack(g, slider, enemies[g.Random.Next(enemies.Count)]);
					})
				}
			}));

			// ---------------------------------------------------------------- Paladin
			// [MIS_700] Whack-A-Gnoll (1) — Discover a Paladin weapon from the past. Give it +1/+1.
			cards.Add("MIS_700", new CardDef(new Power
			{
				PowerTask = new DiscoverFromTask((g, c, s) => Cards.All.Where(x => x.Collectible && x.Type == CardType.WEAPON && x.HasClass(CardClass.PALADIN) &&
					!x.NeverGenerated && Cards.FromThePast(x)).GroupBy(x => x.Name).Select(x => x.First()), "MIS_700e")
			}));
			cards.Add("MIS_700e", new CardDef(new Power { Enchant = new Enchant(WeaponStats(V("MIS_700", "stats", 1))) }));
			// [MIS_709] Holy Glowsticks (4) — Lifesteal. Deal $4 damage to a minion. Costs (1) if you've cast a Holy spell this turn.
			cards.Add("MIS_709", new CardDef(TargetMinion, new Power
			{
				PowerTask = new DamageTask(V("MIS_709", "damage", 4), EntityType.TARGET, true),
				Aura = new AdaptiveCostEffect(p => V("MIS_709", "cost", 1), EffectOperator.SET,
					new SelfCondition(p => p.Controller.PlayHistory.Any(h => h.Turn == p.Game.Turn && HolySpellEntry(h))))
			}));
			// [MIS_918] Flickering Lightbot (5) — Gigantify. Costs (1) less for each Holy spell you've cast this game.
			cards.Add("MIS_918", new CardDef(new Power { Aura = new AdaptiveCostEffect(p => p.Controller.PlayHistory.Count(HolySpellEntry)) }));
			// [TOY_716] Flash Sale (4) — Summon a 1/2 Mech with Divine Shield and Taunt. Give your minions +1/+2. (its COLLECTION_RELATED card)
			cards.Add("TOY_716", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Summon(g, c, Cards.FromAssetId(s.Card[GameTag.COLLECTION_RELATED_CARD_DATABASE_ID]) ?? Cards.FromId("CORE_GVG_085"), s);
					GiveAll(g, s, c.BoardZone.GetAll().Where(m => !(m is Location)).Cast<IPlayable>().ToList(), "TOY_716e");
				})
			}));
			cards.Add("TOY_716e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TOY_716", "attack", 1)), Effects.Health_N(V("TOY_716", "health", 2))) }));
			// [TOY_808] Crafter's Aura (7) — At the end of your turn, summon a random 6-Cost minion. Lasts @ turns. (one OBJECTIVE block)
			cards.Add("TOY_808", new CardDef(PaladinAura(new Power(), countdownEffect: new CustomTask((g, c, s, t, stack) =>
				Summon(g, c, RandomMinionOfCost(g, V("TOY_808", "cost", 6)), s)))));
			// [TOY_809] Cardboard Golem (3) — Battlecry: Increase the duration of Auras in your hand, deck, and battlefield by 1.
			cards.Add("TOY_809", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Generic.ExtendAuras(c, V("TOY_809", "turns", 1))) }));
			// [TOY_810] Painter's Virtue (4, weapon) — Lifesteal. After your hero attacks, give minions in your hand +1/+1.
			cards.Add("TOY_810", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = ComplexTask.Create(HandMinions(), new AddEnchantmentTask("TOY_810e", EntityType.STACK)) }
			}));
			cards.Add("TOY_810e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TOY_810", "stats", 1))) }));
			// [TOY_811] Tigress Plushy (3) — Miniaturize. Rush, Lifesteal, Divine Shield. (keywords)
			cards.Add("TOY_811", new CardDef(new Power { NoScript = true }));
			// [TOY_812] Pipsi Painthoof (7) — Deathrattle: Summon a random Divine Shield, Rush, and Taunt minion from your deck.
			cards.Add("TOY_812", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (GameTag keyword in new[] { GameTag.DIVINE_SHIELD, GameTag.RUSH, GameTag.TAUNT })
						SummonFromDeck(g, c, s, p => p[keyword] > 0);
				})
			}));
			// [TOY_813] Toy Captain Tarim (5) — Miniaturize. Taunt. Battlecry: Set a minion's Attack and Health to this minion's.
			cards.Add("TOY_813", new CardDef(BattlecryMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (s is Minion me && t is Minion m) Generic.AddEnchantmentBlock(g, Cards.FromId("TOY_813e3"), me, m, me.AttackDamage, me.Health, 0);
				})
			}));
			cards.Add("TOY_813e3", new CardDef(new Power { Enchant = Enchants.Enchants.SetAttackHealthScriptTag }));
			// [TOY_880] Wind-Up Enforcer (6) — Tradeable. Battlecry: Summon @ copies of this minion. (Trade to upgrade!)
			cards.Add("TOY_880", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					for (int i = 0; i < Math.Max(1, me[GameTag.TAG_SCRIPT_DATA_NUM_1]) && !c.BoardZone.IsFull; i++) Generic.Copy(c, s, me, Zone.PLAY);
				})
			}));
			// [TOY_881] Fancy Packaging (1) — Give a minion with Divine Shield +2/+3. (any side: the text says "a minion")
			cards.Add("TOY_881", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_DIVINE_SHIELD, 0 } },
				new Power { PowerTask = new AddEnchantmentTask("TOY_881e", EntityType.TARGET) }));
			cards.Add("TOY_881e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TOY_881", "attack", 2)), Effects.Health_N(V("TOY_881", "health", 3))) }));
			// [TOY_882] Trinket Artist (3) — Battlecry: Draw a Divine Shield minion and an Aura.
			cards.Add("TOY_882", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					DrawOne(g, c, p => p is Minion && p[GameTag.DIVINE_SHIELD] > 0);
					DrawOne(g, c, p => p.Card[HsTag.PALADIN_AURA] > 0);
				})
			}));
		}
	}
}
