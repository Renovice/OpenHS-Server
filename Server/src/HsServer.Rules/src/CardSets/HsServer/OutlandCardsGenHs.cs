// HsServer addition (not upstream SabberStone): Ashes of Outland (2020). Upstream had no script for the set; these
// definitions give its 122 cards (and their Primes / tokens) their effects. Every number goes through CardValues (V).
// Shared rules this set added: Mo'arg's "minions take double damage from spells" (Generic.DamageCharFunc, HsTag
// .MINIONS_TAKE_DOUBLE_SPELL_DAMAGE), Libram cost auras (HsTag.LIBRAM), restless hand auras (Kael'thas), "cast a spell
// last turn" (SelfCondition.CastSpellLastTurn).
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
		// ------------------------------------------------------------------ shared pieces of the set

		/// <summary>An effect-made attack (no attack used, no Exhaustion): the shared AttackTask.</summary>
		private static void EffectAttack(Game g, ICharacter attacker, ICharacter defender)
		{
			if (attacker == null || defender == null || attacker.Zone?.Type != SabberStoneCore.Enums.Zone.PLAY) return;
			new AttackTask(EntityType.SOURCE, EntityType.TARGET).Process(g, attacker.Controller, attacker, (IPlayable)defender);
		}

		/// <summary>The enemies an effect can make something attack: enemy minions in play (not Dormant / dying) and the hero.</summary>
		private static List<ICharacter> AttackableEnemies(Controller c)
		{
			var list = c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).Cast<ICharacter>().ToList();
			if (!c.Opponent.Hero.IsDead) list.Add(c.Opponent.Hero);
			return list;
		}

		/// <summary>"Deathrattle: Shuffle '&lt;Name&gt; Prime' into your deck" (the set's Legendaries).</summary>
		private static ISimpleTask ShufflePrime(string prime) => new CustomTask((g, c, s, t, stack) =>
			Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId(prime))));

		/// <summary>"Transform into a random minion that costs (N) more" for each of <paramref name="minions"/>.</summary>
		private static void TransformCostShifted(Game g, IEnumerable<Minion> minions, int delta)
		{
			foreach (Minion m in minions.ToList())
				if (m.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY && CostShifted(g, m, delta) is Card card)
					Generic.TransformBlock.Invoke(m.Controller, card, m);
		}

		private static bool IsLibram(IPlayable p) => p.Card[HsTag.LIBRAM] > 0;

		/// <summary>A player enchantment: "Reduce the Cost of your Librams by (N) this game" (Aldor Attendant / Truthseeker).</summary>
		private static CardDef LibramDiscount(string card, int n) => new CardDef(new Power
		{
			Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V(card, "discount", n))) { Condition = new SelfCondition(IsLibram) }
		});

		/// <summary>A one-turn enchantment from the client's text ("+4 Attack this turn", "Immune this turn").</summary>
		private static CardDef OneTurn(params IEffect[] effects) => new CardDef(new Power { Enchant = new Enchant(effects) { IsOneTurnEffect = true } });

		/// <summary>"+N/+N" on a weapon: Attack and Durability (Upgrade!'s way; a weapon has no Health).</summary>
		private static IEffect[] WeaponStats(int n) =>
			new IEffect[] { new Effect(GameTag.ATK, EffectOperator.ADD, n), new Effect(GameTag.DURABILITY, EffectOperator.ADD, n) };

		private static CardDef FromText(string id) => new CardDef(new Power { Enchant = Enchants.Enchants.GetAutoEnchantFromText(id) });

		private static void Outland(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Mage

			// [BT_002] Incanter's Flow (2) — Reduce the Cost of spells in your deck by (1).
			cards.Add("BT_002", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.DeckZone.Where(p => p is Spell).ToList(), "BT_002e"))
			}));
			cards.Add("BT_002e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("BT_002", "discount", 1))) }));
			// [BT_003] Netherwind Portal (3) — Secret: After your opponent casts a spell, summon a random 4-Cost minion.
			cards.Add("BT_003", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = ComplexTask.Secret(new CustomTask((g, c, s, t, stack) =>
						Summon(g, c, RandomMinionOfCost(g, V("BT_003", "cost", 4)), s)))
				}
			}));
			// [BT_004] Imprisoned Observer (3) — Dormant for 2 turns. When this awakens, deal 2 damage to all enemy minions.
			cards.Add("BT_004", new CardDef(new Power { AwakenTask = new DamageTask(V("BT_004", "damage", 2), EntityType.OP_MINIONS) }));
			// [BT_006] Evocation (1) — Fill your hand with random Mage spells. They are Temporary.
			cards.Add("BT_006", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					while (!c.HandZone.IsFull && RandomCard(g, x => x.Type == CardType.SPELL && x.HasClass(CardClass.MAGE)) is Card card)
					{
						IPlayable spell = Entity.FromCard(c, card);
						if (!Generic.AddHandPhase.Invoke(c, spell)) break;
						Generic.MakeTemporary(spell, s);
					}
				})
			}));
			// [BT_014] Starscryer (2) — Deathrattle: Draw a spell.
			cards.Add("BT_014", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p is Spell)) }));
			// [BT_021] Font of Power (1) — Discover a Mage minion. If your deck has no minions, keep all 3 instead.
			bool MageMinion(Controller c, Card x) => x.Type == CardType.MINION && x.HasClass(CardClass.MAGE);
			cards.Add("BT_021", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.DeckZone.Any(p => Generic.IsMinionCard(p)))
						DiscoverFromTask.Where(MageMinion).Process(g, c, s, t);
					else
						foreach (Card card in DiscoverFromTask.PickDistinct(g, Cards.GenerationPool(g, c).Where(x => MageMinion(c, x)), 3))
							AddToHand(c, card);
				})
			}));
			// [BT_022] Apexis Smuggler (2) — After you play a Secret, Discover a spell.
			cards.Add("BT_022", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = SelfCondition.IsSecret,
					SingleTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL)
				}
			}));
			// [BT_028] Astromancer Solarian (2) — Spell Damage +1. Deathrattle: Shuffle 'Solarian Prime' into your deck.
			cards.Add("BT_028", new CardDef(new Power { DeathrattleTask = ShufflePrime("BT_028t") }));
			// [BT_028t] Solarian Prime (7) — Spell Damage +1. Battlecry: Cast 5 random Mage spells (targets enemies if possible).
			cards.Add("BT_028t", new CardDef(new Power
			{
				PowerTask = ComplexTask.Repeat(new CastRandomSpellTask(x => x.HasClass(CardClass.MAGE), enemiesIfPossible: true), V("BT_028t", "spells", 5))
			}));
			// [BT_291] Apexis Blast (5) — Deal 5 damage. If your deck has no minions, summon a random 5-Cost minion.
			cards.Add("BT_291", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("BT_291", "damage", 5), EntityType.TARGET, true),
					ComplexTask.If((g, c, s) => !c.DeckZone.Any(p => Generic.IsMinionCard(p)), new CustomTask((g, c, s, t, stack) =>
						Summon(g, c, RandomMinionOfCost(g, V("BT_291", "cost", 5)), s))))
			}));

			// ---------------------------------------------------------------- Paladin

			// [BT_009] Imprisoned Sungill (1) — Dormant for 2 turns. When this awakens, summon two 1/1 Murlocs.
			cards.Add("BT_009", new CardDef(new Power { AwakenTask = new SummonTask("BT_009t", V("BT_009", "murlocs", 2)) }));
			// [BT_011] Libram of Justice (5) — Equip a 1/4 weapon. Change the Health of all enemy minions to 1.
			cards.Add("BT_011", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new WeaponTask("BT_011t"), new AddEnchantmentTask("BT_011e", EntityType.OP_MINIONS))
			}));
			cards.Add("BT_011e", new CardDef(new Power { Enchant = new Enchant(Effects.SetMaxHealth(V("BT_011", "health", 1))) }));
			// [BT_019] Murgur Murgurgle (2) — Divine Shield. Deathrattle: Shuffle 'Murgurgle Prime' into your deck.
			cards.Add("BT_019", new CardDef(new Power { DeathrattleTask = ShufflePrime("BT_019t") }));
			// [BT_019t] Murgurgle Prime (8) — Divine Shield. Battlecry: Summon 4 random Murlocs. Give them Divine Shield.
			cards.Add("BT_019t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("BT_019t", "murlocs", 4) && !c.BoardZone.IsFull; i++)
					{
						if (!(RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.MURLOC) && !x.NeverRandomlyOnBoard) is Card card)) break;
						var murloc = (Minion)Entity.FromCard(c, card);
						Generic.SummonBlock.Invoke(g, murloc, -1, s);
						murloc[GameTag.DIVINE_SHIELD] = 1;
					}
				})
			}));
			// [BT_020] Aldor Attendant (1) — Battlecry: Reduce the Cost of your Librams by (1) this game.
			cards.Add("BT_020", new CardDef(new Power { PowerTask = new AddEnchantmentTask("BT_020e", EntityType.CONTROLLER) }));
			cards.Add("BT_020e", LibramDiscount("BT_020", 1));
			// [BT_026] Aldor Truthseeker (5) — Taunt. Battlecry: Reduce the Cost of your Librams by (2) this game.
			cards.Add("BT_026", new CardDef(new Power { PowerTask = new AddEnchantmentTask("BT_026e", EntityType.CONTROLLER) }));
			cards.Add("BT_026e", LibramDiscount("BT_026", 2));
			// [BT_024] Libram of Hope (9) — Restore 8 Health. Summon an 8/8 Guardian with Taunt and Divine Shield.
			cards.Add("BT_024", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new HealTask(V("BT_024", "heal", 8), EntityType.HERO), new SummonTask("BT_024t", SummonSide.SPELL))
			}));
			// [BT_025] Libram of Wisdom (2) — Give a minion +1/+1 and "Deathrattle: Add a 'Libram of Wisdom' spell to your hand."
			cards.Add("BT_025", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("BT_025e", EntityType.TARGET) }));
			cards.Add("BT_025e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.AttackHealth_N(V("BT_025", "stats", 1))),
				DeathrattleTask = new AddCardTo("BT_025", EntityType.HAND)
			}));

			// ---------------------------------------------------------------- Shaman

			// [BT_100] Serpentshrine Portal (3) — Deal 3 damage. Summon a random 3-Cost minion. Overload: (1)
			cards.Add("BT_100", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("BT_100", "damage", 3), EntityType.TARGET, true),
					new CustomTask((g, c, s, t, stack) => Summon(g, c, RandomMinionOfCost(g, V("BT_100", "cost", 3)), s)))
			}));
			// [BT_101] Vivid Spores (4) — Give your minions "Deathrattle: Resummon this minion." (Ancestral Spirit's way)
			cards.Add("BT_101", new CardDef(new Power { PowerTask = new AddEnchantmentTask("BT_101e", EntityType.MINIONS) }));
			cards.Add("BT_101e", new CardDef(new Power { DeathrattleTask = new CopyTask(EntityType.SOURCE, SabberStoneCore.Enums.Zone.PLAY) }));
			// [BT_102] Boggspine Knuckles (5) — After your hero attacks, transform your minions into random ones that cost (1) more.
			cards.Add("BT_102", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) => TransformCostShifted(g, c.BoardZone.GetAll(), V("BT_102", "cost", 1)))
				}
			}));
			// [BT_106] Bogstrok Clacker (3) — Battlecry: Transform adjacent minions into random minions that cost (1) more.
			cards.Add("BT_106", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (s is Minion me) TransformCostShifted(g, me.GetAdjacentMinions(), V("BT_106", "cost", 1));
				})
			}));
			// [BT_109] Lady Vashj (3) — Spell Damage +1. Deathrattle: Shuffle 'Vashj Prime' into your deck.
			cards.Add("BT_109", new CardDef(new Power { DeathrattleTask = ShufflePrime("BT_109t") }));
			// [BT_109t] Vashj Prime (7) — Spell Damage +1. Battlecry: Draw 3 spells. Reduce their Cost by (3).
			cards.Add("BT_109t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("BT_109t", "spells", 3); i++)
					{
						var spells = c.DeckZone.Where(p => p is Spell).ToList();
						if (spells.Count == 0) break;
						if (Generic.Draw(c, spells[g.Random.Next(spells.Count)]) is IPlayable drawn && drawn.Zone?.Type == SabberStoneCore.Enums.Zone.HAND)
							Give(g, s, drawn, "BT_109te");
					}
				})
			}));
			cards.Add("BT_109te", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("BT_109t", "discount", 3))) }));
			// [BT_110] Torrent (4) — Deal 8 damage to a minion. Costs (3) less if you cast a spell last turn.
			cards.Add("BT_110", new CardDef(TargetMinion, new Power
			{
				PowerTask = new DamageTask(V("BT_110", "damage", 8), EntityType.TARGET, true),
				Aura = new AdaptiveCostEffect(p => SelfCondition.CastSpellLastTurn.Eval(p) ? V("BT_110", "discount", 3) : 0)
			}));
			// [BT_113] Totemic Reflection (3) — Give a minion +2/+2. If it's a Totem, summon a copy of it.
			cards.Add("BT_113", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("BT_113e", EntityType.TARGET),
					new CustomTask((g, c, s, t, stack) =>
					{
						if (t is Minion m && m.IsRace(Race.TOTEM)) new SummonCopyTask(EntityType.TARGET).Process(g, c, s, t);
					}))
			}));
			cards.Add("BT_113e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BT_113", "stats", 2))) }));
			// [BT_114] Shattered Rumbler (5) — Battlecry: If you cast a spell last turn, deal 2 damage to all other minions.
			cards.Add("BT_114", new CardDef(new Power
			{
				PowerTask = ComplexTask.Conditional(SelfCondition.CastSpellLastTurn, new DamageTask(V("BT_114", "damage", 2), EntityType.ALLMINIONS_NOSOURCE))
			}));
			// [BT_230] The Lurker Below (6) — Battlecry: Deal 3 damage to an enemy minion. If it dies, repeat on one of its
			// neighbors. (wiki: it keeps one direction — "it can switch directions if a minion that is killed by it summons a
			// token"; the first neighbor is random)
			cards.Add("BT_230", new CardDef(BattlecryEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion target)) return;
					int damage = V("BT_230", "damage", 3);
					int direction = 0; // -1 left, +1 right, 0 not chosen yet
					while (target != null)
					{
						BoardZoneLike board = new BoardZoneLike(target);
						Generic.DamageCharFunc((IPlayable)s, target, damage, false);
						if (!(target.IsDead || target.ToBeDestroyed || target.Health <= 0)) break;
						Minion left = board.Left, right = board.Right;
						if (direction == 0)
							direction = left != null && right != null ? (g.Random.Next(2) == 0 ? -1 : 1) : left != null ? -1 : 1;
						target = direction < 0 ? left : right;
					}
				})
			}));

			// ---------------------------------------------------------------- Warrior

			// [BT_121] Imprisoned Gan'arg (1) — Dormant for 2 turns. When this awakens, equip a 3/2 Axe. (the client's related
			// card: Fiery War Axe)
			cards.Add("BT_121", new CardDef(new Power { AwakenTask = new WeaponTask("CS2_106") }));
			// [BT_123] Kargath Bladefist (4) — Rush. Deathrattle: Shuffle 'Kargath Prime' into your deck.
			cards.Add("BT_123", new CardDef(new Power { DeathrattleTask = ShufflePrime("BT_123t") }));
			// [BT_123t] Kargath Prime (8) — Rush. Whenever this attacks and kills a minion, gain 10 Armor.
			cards.Add("BT_123t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					Condition = new SelfCondition(p => KilledItsDefender(p.Game)),
					SingleTask = new ArmorTask(V("BT_123t", "armor", 10))
				}
			}));
			// [BT_124] Corsair Cache (2) — Draw a weapon. Give it +1/+1.
			cards.Add("BT_124", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var weapons = c.DeckZone.Where(p => p is Weapon).ToList();
					if (weapons.Count > 0 && Generic.Draw(c, weapons[g.Random.Next(weapons.Count)]) is IPlayable drawn && drawn.Zone?.Type == SabberStoneCore.Enums.Zone.HAND)
						Give(g, s, drawn, "BT_124e");
				})
			}));
			cards.Add("BT_124e", new CardDef(new Power { Enchant = new Enchant(WeaponStats(V("BT_124", "stats", 1))) }));
			// [BT_138] Bloodboil Brute (7) — Rush. Costs (1) less for each damaged minion.
			cards.Add("BT_138", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => V("BT_138", "discount per minion", 1) *
					p.Controller.BoardZone.GetAll().Concat(p.Controller.Opponent.BoardZone.GetAll()).Count(m => m.Damage > 0))
			}));
			// [BT_140] Bonechewer Raider (3) — Battlecry: If there is a damaged minion, gain +1/+1 and Rush.
			cards.Add("BT_140", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Any(m => m.Id != s.Id && m.Damage > 0),
					new AddEnchantmentTask("BT_140e", EntityType.SOURCE))
			}));
			cards.Add("BT_140e", FromText("BT_140e"));
			// [BT_233] Sword and Board (1) — Deal 2 damage to a minion. Gain 2 Armor.
			cards.Add("BT_233", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("BT_233", "damage", 2), EntityType.TARGET, true), new ArmorTask(V("BT_233", "armor", 2)))
			}));
			// [BT_249] Scrap Golem (5) — Taunt. Deathrattle: Gain Armor equal to this minion's Attack.
			cards.Add("BT_249", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (s is Minion me && me.AttackDamage > 0) new ArmorTask(me.AttackDamage).Process(g, c, s, t);
				})
			}));

			// ---------------------------------------------------------------- Druid

			// [BT_127] Imprisoned Satyr (3) — Dormant for 2 turns. When this awakens, reduce the Cost of a random minion in your hand by (5).
			cards.Add("BT_127", new CardDef(new Power
			{
				AwakenTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.HandZone.Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.Count > 0) Give(g, s, minions[g.Random.Next(minions.Count)], "BT_127e");
				})
			}));
			cards.Add("BT_127e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("BT_127", "discount", 5))) }));
			// [BT_128] Fungal Fortunes (3) — Draw 3 cards. Discard any minions drawn.
			cards.Add("BT_128", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var drawn = new List<IPlayable>();
					for (int i = 0; i < V("BT_128", "cards", 3); i++)
						if (Generic.Draw(c) is IPlayable p) drawn.Add(p);
					foreach (IPlayable p in drawn.Where(p => p is Minion && p.Zone?.Type == SabberStoneCore.Enums.Zone.HAND))
						Generic.DiscardBlock.Invoke(c, p);
				})
			}));
			// [BT_129] Germination (4) — Summon a copy of a friendly minion. Give the copy Taunt.
			cards.Add("BT_129", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_NUM_MINION_SLOTS, 1 } },
				new Power
				{
					PowerTask = ComplexTask.Create(new SummonCopyTask(EntityType.TARGET, addToStack: true), new AddEnchantmentTask("BT_129e", EntityType.STACK))
				}));
			cards.Add("BT_129e", new CardDef(new Power { Enchant = new Enchant(Effects.TauntEff) }));
			// [BT_130] Overgrowth (4) — Gain two empty Mana Crystals.
			cards.Add("BT_130", new CardDef(new Power { PowerTask = new ManaCrystalEmptyTask(V("BT_130", "crystals", 2)) }));
			// [BT_131] Ysiel Windsinger (9) — Battlecry: Your spells cost (1) this turn.
			cards.Add("BT_131", new CardDef(new Power { PowerTask = new AddEnchantmentTask("BT_131e", EntityType.CONTROLLER) }));
			cards.Add("BT_131e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.SetCost(V("BT_131", "cost", 1)))
				{
					Condition = SelfCondition.IsSpell,
					RemoveTrigger = (TriggerType.TURN_END, null),
				}
			}));
			// [BT_132] Ironbark (2) — Give a minion +1/+3 and Taunt. Costs (0) if you have at least 7 Mana Crystals.
			cards.Add("BT_132", new CardDef(TargetMinion, new Power
			{
				PowerTask = new AddEnchantmentTask("BT_132e", EntityType.TARGET),
				Aura = new AdaptiveCostEffect(p => p.Controller.BaseMana >= V("BT_132", "mana crystals", 7) ? p.Card.Cost : 0)
			}));
			cards.Add("BT_132e", FromText("BT_132e"));
			// [BT_133] Marsh Hydra (7) — Rush. After this attacks, add a random 8-Cost minion to your hand.
			cards.Add("BT_133", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (RandomCard(g, x => x.Type == CardType.MINION && x.Cost == V("BT_133", "cost", 8)) is Card card) AddToHand(c, card);
					})
				}
			}));
			// [BT_135] Glowfly Swarm (5) — Summon a 2/2 Glowfly for each spell in your hand.
			cards.Add("BT_135", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = c.HandZone.Count(p => p is Spell);
					for (int i = 0; i < n && !c.BoardZone.IsFull; i++) Summon(g, c, Cards.FromId("BT_135t"), s);
				})
			}));
			// [BT_136] Archspore Msshi'fn (3) — Taunt. Deathrattle: Shuffle 'Msshi'fn Prime' into your deck.
			cards.Add("BT_136", new CardDef(new Power { DeathrattleTask = ShufflePrime("BT_136t") }));
			// [BT_136t] Msshi'fn Prime (10) — Taunt. Choose One - Summon a 9/9 Fungal Giant with Taunt; or Rush. Its options
			// are the client's related cards (Cards.ChooseOneOptions); its own task is the "choose both" one (Fandral): the
			// Gargantuan with Rush and Taunt.
			cards.Add("BT_136t", new CardDef(new Power { PowerTask = new SummonTask("BT_136tt3") }));
			cards.Add("BT_136ta", new CardDef(new Power { PowerTask = new SummonTask("BT_136tt") }));
			cards.Add("BT_136tb", new CardDef(new Power { PowerTask = new SummonTask("BT_136tt2") }));

			// ---------------------------------------------------------------- Hunter

			// [BT_163] Nagrand Slam (10) — Summon four 3/5 Clefthoofs that attack random enemies. (user 2026-10-05: all four
			// are summoned, then each attacks in turn)
			cards.Add("BT_163", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var hoofs = new List<Minion>();
					for (int i = 0; i < V("BT_163", "clefthoofs", 4) && !c.BoardZone.IsFull; i++)
					{
						var hoof = (Minion)Entity.FromCard(c, Cards.FromId("BT_163t"));
						Generic.SummonBlock.Invoke(g, hoof, -1, s);
						hoofs.Add(hoof);
					}
					foreach (Minion hoof in hoofs)
					{
						var enemies = AttackableEnemies(c);
						if (enemies.Count > 0 && !hoof.IsDead && !hoof.ToBeDestroyed) EffectAttack(g, hoof, enemies[g.Random.Next(enemies.Count)]);
					}
				})
			}));
			// [BT_202] Helboar (1) — Deathrattle: Give a random Beast in your hand +1/+1.
			cards.Add("BT_202", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var beasts = c.HandZone.Where(p => p is Minion m && m.IsRace(Race.BEAST)).ToList();
					if (beasts.Count > 0) Give(g, s, beasts[g.Random.Next(beasts.Count)], "BT_202e");
				})
			}));
			cards.Add("BT_202e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BT_202", "stats", 1))) }));
			// [BT_203] Pack Tactics (2) — Secret: When a friendly minion is attacked, summon a 3/3 copy.
			cards.Add("BT_203", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					Condition = SelfCondition.IsEventTargetIs(CardType.MINION),
					SingleTask = ComplexTask.Secret(new SummonCopyTask(EntityType.EVENT_TARGET, addToStack: true),
						new AddEnchantmentTask("BT_203e", EntityType.STACK))
				}
			}));
			cards.Add("BT_203e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("BT_203", "stats", 3))) }));
			// [BT_205] Scrap Shot (4) — Deal 3 damage. Give a random Beast in your hand +3/+3.
			cards.Add("BT_205", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("BT_205", "damage", 3), EntityType.TARGET, true),
					new CustomTask((g, c, s, t, stack) =>
					{
						var beasts = c.HandZone.Where(p => p is Minion m && m.IsRace(Race.BEAST)).ToList();
						if (beasts.Count > 0) Give(g, s, beasts[g.Random.Next(beasts.Count)], "BT_205e");
					}))
			}));
			cards.Add("BT_205e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BT_205", "stats", 3))) }));
			// [BT_210] Zixor, Apex Predator (3) — Rush. Deathrattle: Shuffle 'Zixor Prime' into your deck.
			cards.Add("BT_210", new CardDef(new Power { DeathrattleTask = ShufflePrime("BT_210t") }));
			// [BT_210t] Zixor Prime (8) — Rush. Battlecry: Summon 3 copies of this minion.
			cards.Add("BT_210t", new CardDef(new Power { PowerTask = ComplexTask.Repeat(new SummonCopyTask(EntityType.SOURCE), V("BT_210t", "copies", 3)) }));
			// [BT_211] Imprisoned Felmaw (2) — Dormant for 2 turns. When this awakens, attack a random enemy.
			cards.Add("BT_211", new CardDef(new Power
			{
				AwakenTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = AttackableEnemies(c);
					if (s is Minion me && enemies.Count > 0) EffectAttack(g, me, enemies[g.Random.Next(enemies.Count)]);
				})
			}));
			// [BT_212] Mok'Nathal Lion (4) — Rush. Battlecry: Choose a friendly minion. Gain a copy of its Deathrattle.
			// (Unearthed Raptor's way: the enchantment captures the minion and runs its Deathrattle)
			cards.Add("BT_212", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_DEATHRATTLE, 0 } },
				new Power
				{
					PowerTask = ComplexTask.Create(new GetGameTagTask(GameTag.ENTITY_ID, EntityType.TARGET),
						new AddEnchantmentTask("BT_212e", EntityType.SOURCE, false, true))
				}));
			cards.Add("BT_212e", new CardDef(new Power { DeathrattleTask = ActivateCapturedDeathrattleTask.Task }));
			// [BT_213] Scavenger's Ingenuity (2) — Draw a Beast. Give it +3/+3.
			cards.Add("BT_213", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var beasts = c.DeckZone.Where(p => p is Minion m && m.IsRace(Race.BEAST)).ToList();
					if (beasts.Count > 0 && Generic.Draw(c, beasts[g.Random.Next(beasts.Count)]) is IPlayable drawn && drawn.Zone?.Type == SabberStoneCore.Enums.Zone.HAND)
						Give(g, s, drawn, "BT_213e");
				})
			}));
			cards.Add("BT_213e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BT_213", "stats", 3))) }));
			// [BT_214] Beastmaster Leoroxx (8) — Battlecry: Summon 3 Beasts from your hand.
			cards.Add("BT_214", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("BT_214", "beasts", 3) && !c.BoardZone.IsFull; i++)
					{
						var beasts = c.HandZone.Where(p => p is Minion m && m.IsRace(Race.BEAST)).ToList();
						if (beasts.Count == 0) break;
						Generic.SummonFromHand(c, (Minion)beasts[g.Random.Next(beasts.Count)], s);
					}
				})
			}));

			// ---------------------------------------------------------------- Priest

			// [BT_197] Reliquary of Souls (1) — Lifesteal. Deathrattle: Shuffle 'Reliquary Prime' into your deck.
			cards.Add("BT_197", new CardDef(new Power { DeathrattleTask = ShufflePrime("BT_197t") }));
			// [BT_197t] Reliquary Prime (7) — Taunt, Lifesteal. Elusive on your opponent's turn.
			cards.Add("BT_197t", new CardDef(new Power
			{
				Aura = new Aura(AuraType.SELF, Effects.CantBeTargetedBySpellsAndHeroPowers) { Condition = SelfCondition.IsOpTurn, Restless = true }
			}));
			// [BT_198] Soul Mirror (7) — Summon copies of enemy minions. They attack their copies.
			cards.Add("BT_198", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var copies = new TaskStack();
					new SummonCopyTask(EntityType.OP_MINIONS, addToStack: true).Process(g, c, s, t, copies);
					foreach (Minion copy in (copies.Playables ?? new List<IPlayable>()).OfType<Minion>().ToList())
						if (g.IdEntityDic.TryGetValue(copy[HsTag.COPIED_FROM_ENTITY_ID], out IPlayable original) && original is Minion o &&
						    o.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY && !o.IsDead)
							EffectAttack(g, copy, o);
				})
			}));
			// [BT_252] Renew (1) — Restore 3 Health. Discover a spell.
			cards.Add("BT_252", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new HealTask(V("BT_252", "heal", 3), EntityType.TARGET),
					DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL))
			}));
			// [BT_253] Psyche Split (5) — Give a minion +1/+2. Summon a copy of it.
			cards.Add("BT_253", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("BT_253e", EntityType.TARGET), new SummonCopyTask(EntityType.TARGET))
			}));
			cards.Add("BT_253e", FromText("BT_253e"));
			// [BT_254] Sethekk Veilweaver (2) — After you cast a spell on a minion, add a Priest spell to your hand.
			cards.Add("BT_254", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.CardTarget > 0 && p.Game.IdEntityDic.TryGetValue(p.CardTarget, out IPlayable on) && on is Minion),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (RandomCard(g, x => x.Type == CardType.SPELL && x.HasClass(CardClass.PRIEST)) is Card card) AddToHand(c, card);
					})
				}
			}));
			// [BT_256] Dragonmaw Overseer (3) — At the end of your turn, give another friendly minion +2/+2.
			cards.Add("BT_256", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var others = c.BoardZone.GetAll().Where(m => m.Id != s.Id && !m.IsDead).ToList();
						if (others.Count > 0) Give(g, s, others[g.Random.Next(others.Count)], "BT_256e");
					})
				}
			}));
			cards.Add("BT_256e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BT_256", "stats", 2))) }));
			// [BT_257] Apotheosis (3) — Give a minion +2/+3 and Lifesteal.
			cards.Add("BT_257", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("BT_257e", EntityType.TARGET) }));
			cards.Add("BT_257e", FromText("BT_257e"));
			// [BT_262] Dragonmaw Sentinel (2) — Battlecry: If you're holding a Dragon, gain +1 Attack and Lifesteal.
			cards.Add("BT_262", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => HoldingDragon(c, s), new AddEnchantmentTask("BT_262e", EntityType.SOURCE))
			}));
			cards.Add("BT_262e", FromText("BT_262e"));
			// [BT_341] Skeletal Dragon (7) — Taunt. At the end of your turn, add a Dragon to your hand.
			cards.Add("BT_341", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.DRAGON)) is Card card) AddToHand(c, card);
					})
				}
			}));

			// ---------------------------------------------------------------- Warlock

			// [BT_196] Keli'dan the Breaker (6) — Battlecry: Destroy a minion. If drawn this turn, instead destroy all minions
			// except this one.
			cards.Add("BT_196", new CardDef(BattlecryMinion, new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => s[HsTag.DRAWN_ON_TURN] == g.Turn,
					new CustomTask((g, c, s, t, stack) =>
					{
						foreach (Minion m in c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => m.Id != s.Id).ToList())
							m.Destroy();
					}),
					new DestroyTask(EntityType.TARGET))
			}));
			// [BT_199] Unstable Felbolt (1) — Deal 3 damage to an enemy minion and a random friendly one.
			cards.Add("BT_199", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } },
				new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) =>
					{
						int damage = V("BT_199", "damage", 3);
						var friends = c.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed).ToList();
						Minion friend = friends.Count > 0 ? friends[g.Random.Next(friends.Count)] : null;
						if (t is Minion enemy) Generic.DamageCharFunc((IPlayable)s, enemy, damage, false);
						if (friend != null) Generic.DamageCharFunc((IPlayable)s, friend, damage, false);
					})
				}));
			// [BT_300] Hand of Gul'dan (6) — When you play or discard this, draw 3 cards.
			cards.Add("BT_300", new CardDef(new Power
			{
				PowerTask = new EnqueueTask(V("BT_300", "cards", 3), new DrawTask()),
				Trigger = new Trigger(TriggerType.DISCARD)
				{
					TriggerActivation = TriggerActivation.HAND,
					TriggerSource = TriggerSource.SELF,
					SingleTask = new EnqueueTask(V("BT_300", "cards", 3), new DrawTask())
				}
			}));
			// [BT_301] Nightshade Matron (4) — Rush. Battlecry: Discard your highest Cost card.
			cards.Add("BT_301", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.HandZone.Count == 0) return;
					int top = c.HandZone.Max(p => p.Cost);
					var highest = c.HandZone.Where(p => p.Cost == top).ToList();
					Generic.DiscardBlock.Invoke(c, highest[g.Random.Next(highest.Count)]);
				})
			}));
			// [BT_302] The Dark Portal (4) — Draw a minion. If you have at least 8 cards in hand, it costs (5) less.
			cards.Add("BT_302", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.DeckZone.Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.Count == 0) return;
					if (Generic.Draw(c, minions[g.Random.Next(minions.Count)]) is IPlayable drawn && drawn.Zone?.Type == SabberStoneCore.Enums.Zone.HAND &&
					    c.HandZone.Count >= V("BT_302", "cards in hand", 8))
						Give(g, s, drawn, "BT_302e");
				})
			}));
			cards.Add("BT_302e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("BT_302", "discount", 5))) }));
			// [BT_305] Imprisoned Scrap Imp (2) — Dormant for 2 turns. When this awakens, give all minions in your hand +2/+2.
			cards.Add("BT_305", new CardDef(new Power
			{
				AwakenTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.Where(p => Generic.IsMinionCard(p)).ToList(), "BT_305e"))
			}));
			cards.Add("BT_305e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BT_305", "stats", 2))) }));
			// [BT_306] Shadow Council (1) — Replace your hand with random Demons. Give them +2/+2. (new cards: "replace with")
			cards.Add("BT_306", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var old = c.HandZone.ToList();
					foreach (IPlayable p in old) Generic.RemoveFromHand(c, p);
					foreach (IPlayable _ in old)
					{
						if (!(RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.DEMON)) is Card card)) break;
						IPlayable demon = Entity.FromCard(c, card);
						if (Generic.AddHandPhase.Invoke(c, demon)) Give(g, s, demon, "BT_306e");
					}
				})
			}));
			cards.Add("BT_306e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BT_306", "stats", 2))) }));
			// [BT_307] Darkglare (3) — Battlecry: If your hero took damage this turn, refresh 3 Mana Crystals.
			cards.Add("BT_307", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c[HsTag.HERO_DAMAGE_THIS_TURN] > 0,
					new CustomTask((g, c, s, t, stack) => c.UsedMana = System.Math.Max(0, c.UsedMana - V("BT_307", "mana", 3))))
			}));
			// [BT_309] Kanrethad Ebonlocke (2) — Your Demons cost (1) less. Deathrattle: Shuffle 'Kanrethad Prime' into your deck.
			cards.Add("BT_309", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, "BT_309e") { Condition = SelfCondition.IsRace(Race.DEMON) },
				DeathrattleTask = ShufflePrime("BT_309t")
			}));
			cards.Add("BT_309e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("BT_309", "discount", 1))) }));
			// [BT_309t] Kanrethad Prime (8) — Battlecry: Summon 3 friendly Demons that died this game.
			cards.Add("BT_309t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var dead = c.DiedThisGameCards.Where(p => p is Minion m && m.IsRace(Race.DEMON)).Select(p => p.Card).ToList();
					for (int i = 0; i < V("BT_309t", "demons", 3) && dead.Count > 0 && !c.BoardZone.IsFull; i++)
					{
						Card card = dead[g.Random.Next(dead.Count)];
						dead.Remove(card);
						Summon(g, c, card, s);
					}
				})
			}));

			// ---------------------------------------------------------------- Demon Hunter

			// [BT_173] Command the Illidari (5) — Summon six 1/1 Illidari with Rush.
			cards.Add("BT_173", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new SummonTask("BT_036t", V("BT_173", "illidari", 6), SummonSide.SPELL)
			}));
			// [BT_175] Twin Slice (1) — Give your hero +2 Attack this turn. Add 'Second Slice' to your hand.
			cards.Add("BT_175", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("BT_175e", EntityType.HERO), new AddCardTo("BT_175t", EntityType.HAND))
			}));
			// [BT_175t] Second Slice (1) — Give your hero +2 Attack this turn.
			cards.Add("BT_175t", new CardDef(new Power { PowerTask = new AddEnchantmentTask("BT_175e", EntityType.HERO) }));
			cards.Add("BT_175e", OneTurn(Effects.Attack_N(V("BT_175", "attack", 2))));
			// [BT_354] Blade Dance (2) — Deal damage equal to your hero's Attack to 3 random enemy minions.
			cards.Add("BT_354", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int damage = c.Hero.AttackDamage;
					var enemies = c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
					for (int i = 0; i < V("BT_354", "minions", 3) && enemies.Count > 0; i++)
					{
						Minion m = enemies[g.Random.Next(enemies.Count)];
						enemies.Remove(m);
						Generic.DamageCharFunc((IPlayable)s, m, damage, false);
					}
				})
			}));
			// [BT_407] Ur'zul Horror (1) — Deathrattle: Add a 2/1 Lost Soul to your hand.
			cards.Add("BT_407", new CardDef(new Power { DeathrattleTask = new AddCardTo("BT_407t", EntityType.HAND) }));
			// [BT_481] Nethrandamus (9) — Battlecry: Summon two random @-Cost minions. (Upgrades each time a friendly minion
			// dies!) The count is the card's TAG_SCRIPT_DATA_NUM_1 (its text's @), counted in hand.
			cards.Add("BT_481", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int cost = System.Math.Min(s[GameTag.TAG_SCRIPT_DATA_NUM_1], V("BT_481", "max cost", 10));
					for (int i = 0; i < V("BT_481", "minions", 2); i++) Summon(g, c, RandomMinionOfCost(g, cost), s);
				}),
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerActivation = TriggerActivation.HAND,
					TriggerSource = TriggerSource.MINIONS,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is IPlayable me) me[GameTag.TAG_SCRIPT_DATA_NUM_1] = System.Math.Min(me[GameTag.TAG_SCRIPT_DATA_NUM_1] + 1, V("BT_481", "max cost", 10));
					})
				}
			}));
			// [BT_486] Pit Commander (9) — Taunt. At the end of your turn, summon a Demon from your deck.
			cards.Add("BT_486", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new RecruitTask(1, SelfCondition.IsRace(Race.DEMON)) }
			}));
			// [BT_487] Hulking Overfiend (8) — Rush. After this attacks and kills a minion, it may attack again.
			cards.Add("BT_487", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					Condition = new SelfCondition(p => KilledItsDefender(p.Game)),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me)) return;
						me.NumAttacksThisTurn = System.Math.Max(0, me.NumAttacksThisTurn - 1);
						me.IsExhausted = false;
					})
				}
			}));
			// [BT_488] Soul Split (4) — Choose a friendly Demon. Summon a copy of it.
			cards.Add("BT_488", new CardDef(new Dictionary<PlayReq, int>
				{
					{ PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 },
					{ PlayReq.REQ_TARGET_WITH_RACE, (int)Race.DEMON }, { PlayReq.REQ_NUM_MINION_SLOTS, 1 }
				},
				new Power { PowerTask = new SummonCopyTask(EntityType.TARGET) }));
			// [BT_490] Consume Magic (1) — Silence an enemy minion. Outcast: Draw a card.
			cards.Add("BT_490", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } },
				new Power
				{
					PowerTask = ComplexTask.Create(new SilenceTask(EntityType.TARGET),
						new ConditionTask(EntityType.SOURCE, PlayedAsOutcast), new FlagTask(true, new DrawTask()))
				}));
			// [BT_496] Furious Felfin (2) — Battlecry: If your hero attacked this turn, gain +1 Attack and Rush.
			cards.Add("BT_496", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.Hero.NumAttacksThisTurn > 0, new AddEnchantmentTask("BT_496e", EntityType.SOURCE))
			}));
			cards.Add("BT_496e", FromText("BT_496e"));
			// [BT_509] Fel Summoner (6) — Deathrattle: Summon a random Demon from your hand.
			cards.Add("BT_509", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var demons = c.HandZone.Where(p => p is Minion m && m.IsRace(Race.DEMON)).ToList();
					if (demons.Count == 0 || c.BoardZone.IsFull) return;
					Generic.SummonFromHand(c, (Minion)demons[g.Random.Next(demons.Count)], s);
				})
			}));
			// [BT_601] Skull of Gul'dan (6) — Draw 3 cards. Outcast: Reduce their Cost by (3).
			cards.Add("BT_601", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					bool outcast = PlayedAsOutcast.Eval((IPlayable)s);
					for (int i = 0; i < V("BT_601", "cards", 3); i++)
						if (Generic.Draw(c) is IPlayable drawn && outcast && drawn.Zone?.Type == SabberStoneCore.Enums.Zone.HAND)
							Give(g, s, drawn, "BT_601e");
				})
			}));
			cards.Add("BT_601e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("BT_601", "discount", 3))) }));
			// [BT_752] Blur (0) — Your hero can't take damage this turn.
			cards.Add("BT_752", new CardDef(new Power { PowerTask = new AddEnchantmentTask("BT_752e", EntityType.HERO) }));
			cards.Add("BT_752e", OneTurn(Effects.Immune));
			// [BT_753] Mana Burn (1) — Your opponent has 2 fewer Mana Crystals next turn. (a player enchantment: at the start of
			// their turn, after their mana fills, 2 of it is used; then it goes)
			cards.Add("BT_753", new CardDef(new Power { PowerTask = new AddEnchantmentTask("BT_753e", EntityType.OP_CONTROLLER) }));
			cards.Add("BT_753e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					EitherTurn = true,
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = ComplexTask.Create(
						new CustomTask((g, c, s, t, stack) =>
							c.Opponent.UsedMana = System.Math.Min(c.Opponent.BaseMana, c.Opponent.UsedMana + V("BT_753", "crystals", 2))),
						RemoveEnchantmentTask.Task)
				}
			}));
			// [BT_761] Coilfang Warlord (8) — Rush. Deathrattle: Summon a 5/9 Warlord with Taunt.
			cards.Add("BT_761", new CardDef(new Power { DeathrattleTask = new SummonTask("BT_761t") }));
			// [BT_814] Illidari Felblade (4) — Rush. Outcast: Gain Immune this turn.
			cards.Add("BT_814", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new ConditionTask(EntityType.SOURCE, PlayedAsOutcast),
					new FlagTask(true, new AddEnchantmentTask("BT_814e", EntityType.SOURCE)))
			}));
			cards.Add("BT_814e", OneTurn(Effects.Immune));
			// [BT_934] Imprisoned Antaen (5) — Dormant for 2 turns. When this awakens, deal 10 damage randomly split among all enemies.
			cards.Add("BT_934", new CardDef(new Power
			{
				AwakenTask = new EnqueueTask(V("BT_934", "damage", 10), ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, 1))
			}));
			// [BT_937] Altruis the Outcast (4) — After you play the left- or right-most card in your hand, deal 1 damage to all enemies.
			cards.Add("BT_937", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = PlayedAsOutcast,
					SingleTask = new DamageTask(V("BT_937", "damage", 1), EntityType.ENEMIES)
				}
			}));

			// ---------------------------------------------------------------- Rogue

			// [BT_042] Bamboozle (2) — Secret: When one of your minions is attacked, transform it into a random one that costs (3) more.
			cards.Add("BT_042", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					Condition = SelfCondition.IsEventTargetIs(CardType.MINION),
					SingleTask = ComplexTask.Secret(new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventTarget is Minion m) TransformCostShifted(g, new[] { m }, V("BT_042", "cost", 3));
					}))
				}
			}));
			// [BT_702] Ashtongue Slayer (2) — Battlecry: Give a Stealthed minion +3 Attack and Immune this turn.
			cards.Add("BT_702", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_STEALTHED_TARGET, 0 } },
				new Power { PowerTask = new AddEnchantmentTask("BT_702e", EntityType.TARGET) }));
			cards.Add("BT_702e", OneTurn(Effects.Attack_N(V("BT_702", "attack", 3)), Effects.Immune));
			// [BT_703] Cursed Vagrant (7) — Deathrattle: Summon a 7/5 Shadow with Stealth.
			cards.Add("BT_703", new CardDef(new Power { DeathrattleTask = new SummonTask("BT_703t") }));
			// [BT_709] Dirty Tricks (2) — Secret: After your opponent casts a spell, draw 2 cards.
			cards.Add("BT_709", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = ComplexTask.Secret(new EnqueueTask(V("BT_709", "cards", 2), new DrawTask()))
				}
			}));
			// [BT_710] Greyheart Sage (3) — Battlecry: If you control a Stealthed minion, draw 2 cards.
			cards.Add("BT_710", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.BoardZone.GetAll().Any(m => m.Id != s.Id && m.HasStealth),
					new EnqueueTask(V("BT_710", "cards", 2), new DrawTask()))
			}));
			// [BT_711] Blackjack Stunner (1) — Battlecry: If you control a Secret, return a minion to its owner's hand. It costs (2) more.
			cards.Add("BT_711", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_IF_AVAILABLE_AND_MINIMUM_FRIENDLY_SECRETS, 1 }, { PlayReq.REQ_MINION_TARGET, 0 } },
				new Power
				{
					PowerTask = ComplexTask.If((g, c, s) => Generic.ControlsSecret(c), new CustomTask((g, c, s, t, stack) =>
					{
						if (t is Minion m && Generic.ReturnToHand(m.Controller, m) is IPlayable back) Give(g, s, back, "BT_711e");
					}))
				}));
			cards.Add("BT_711e", new CardDef(new Power { Enchant = new Enchant(Effects.AddCost(V("BT_711", "cost", 2))) }));
			// [BT_713] Akama (3) — Stealth. Deathrattle: Shuffle 'Akama Prime' into your deck.
			cards.Add("BT_713", new CardDef(new Power { DeathrattleTask = ShufflePrime("BT_713t") }));
			// [BT_713t] Akama Prime (6) — Permanently Stealthed. (its Stealth never breaks: HsTag.PERMANENT_STEALTH)
			cards.Add("BT_713t", new CardDef(new Power
			{
				Aura = new Aura(AuraType.SELF, new Effect(HsTag.PERMANENT_STEALTH, EffectOperator.SET, 1))
			}));

			// ---------------------------------------------------------------- Neutral

			// [BT_008] Rustsworn Initiate (2) — Deathrattle: Summon a 1/1 Impcaster with Spell Damage +1.
			cards.Add("BT_008", new CardDef(new Power { DeathrattleTask = new SummonTask("BT_008t") }));
			// [BT_010] Felfin Navigator (4) — Battlecry: Give your other Murlocs +1/+1.
			cards.Add("BT_010", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.BoardZone.GetAll().Where(m => m.Id != s.Id && m.IsRace(Race.MURLOC)).ToList(), "BT_010e"))
			}));
			cards.Add("BT_010e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BT_010", "stats", 1))) }));
			// [BT_126] Teron Gorefiend (3) — Battlecry: Destroy all other friendly minions. Deathrattle: Resummon them with +1/+1.
			// Each destroyed minion is remembered by a "Destroyed {0}" enchantment on Teron (Carnivorous Cube's way: NUM_1 = its
			// id); each enchantment's Deathrattle resummons its minion (a new copy) with +1/+1.
			cards.Add("BT_126", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().Where(m => m.Id != s.Id).ToList())
					{
						Give(g, s, (IPlayable)s, "BT_126e", m.Id);
						m.Destroy();
					}
				})
			}));
			cards.Add("BT_126e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t == null || !g.IdEntityDic.TryGetValue(t[GameTag.TAG_SCRIPT_DATA_NUM_1], out IPlayable gone) || c.BoardZone.IsFull) return;
					var back = (Minion)Entity.FromCard(c, gone.Card);
					Generic.SummonBlock.Invoke(g, back, -1, s);
					Give(g, s, back, "BT_126e2");
				})
			}));
			cards.Add("BT_126e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BT_126", "stats", 1))) }));
			// [BT_155] Scrapyard Colossus (10) — Taunt. Deathrattle: Summon a 7/7 Felcracked Colossus with Taunt.
			cards.Add("BT_155", new CardDef(new Power { DeathrattleTask = new SummonTask("BT_155t") }));
			// [BT_159] Terrorguard Escapee (3) — Battlecry: Summon three 1/1 Huntresses for your opponent.
			cards.Add("BT_159", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("BT_159", "huntresses", 3); i++) Summon(g, c.Opponent, Cards.FromId("BT_159t"), s);
				})
			}));
			// [BT_160] Rustsworn Cultist (4) — Battlecry: Give your other minions "Deathrattle: Summon a 1/1 Demon."
			cards.Add("BT_160", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.GetAll().Where(m => m.Id != s.Id).ToList(), "BT_160e"))
			}));
			cards.Add("BT_160e", new CardDef(new Power { DeathrattleTask = new SummonTask("BT_160t") }));
			// [BT_190] Replicat-o-tron (4) — At the end of your turn, transform a neighbor into a copy of this. (a random
			// neighbor; a copy board to board keeps its enchantments — Faceless Manipulator's TransformCopyTask, the other
			// way round; user 2026-10-05)
			cards.Add("BT_190", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me)) return;
						var neighbors = me.GetAdjacentMinions().Where(m => !m.IsDead && !m.ToBeDestroyed).ToList();
						if (neighbors.Count == 0) return;
						Minion pick = neighbors[g.Random.Next(neighbors.Count)];
						new TransformCopyTask().Process(g, pick.Controller, pick, me);
					})
				}
			}));
			// [BT_255] Kael'thas Sunstrider (6) — Every third spell you cast each turn costs (1).
			cards.Add("BT_255", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, "BT_255e")
				{
					Condition = new SelfCondition(p => p is Spell &&
						p.Controller.CardsPlayedThisTurn.Count(x => x.Type == CardType.SPELL) % V("BT_255", "every", 3) == V("BT_255", "every", 3) - 1),
					Restless = true
				}
			}));
			cards.Add("BT_255e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(V("BT_255", "cost", 1))) }));
			// [BT_714] Frozen Shadoweaver (3) — Battlecry: Freeze an enemy.
			cards.Add("BT_714", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } },
				new Power { PowerTask = ComplexTask.Freeze(EntityType.TARGET) }));
			// [BT_715] Bonechewer Brawler (2) / [BT_716] Bonechewer Vanguard (7) — Taunt. Whenever this minion takes damage, gain +2 Attack.
			foreach (string id in new[] { "BT_715", "BT_716" })
			{
				string ench = id + "e";
				cards.Add(id, new CardDef(new Power
				{
					Trigger = new Trigger(TriggerType.TAKE_DAMAGE) { TriggerSource = TriggerSource.SELF, SingleTask = new AddEnchantmentTask(ench, EntityType.SOURCE) }
				}));
				cards.Add(ench, new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V(id, "attack", 2))) }));
			}
			// [BT_717] Burrowing Scorpid (4) — Battlecry: Deal 2 damage. If that kills the target, gain Stealth.
			cards.Add("BT_717", new CardDef(BattlecryAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is ICharacter target)) return;
					Generic.DamageCharFunc((IPlayable)s, target, V("BT_717", "damage", 2), false);
					if (target.IsDead || target.ToBeDestroyed || target.Health <= 0) ((IPlayable)s)[GameTag.STEALTH] = 1;
				})
			}));
			// [BT_720] Ruststeed Raider (5) — Taunt, Rush. Battlecry: Gain +4 Attack this turn.
			cards.Add("BT_720", new CardDef(new Power { PowerTask = new AddEnchantmentTask("BT_720e", EntityType.SOURCE) }));
			cards.Add("BT_720e", OneTurn(Effects.Attack_N(V("BT_720", "attack", 4))));
			// [BT_721] Blistering Rot (3) — At the end of your turn, summon a Rot with stats equal to this minion's.
			cards.Add("BT_721", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me) || c.BoardZone.IsFull || me.Health <= 0) return;
						var rot = (Minion)Entity.FromCard(c, Cards.FromId("BT_721t"));
						rot.AttackDamage = me.AttackDamage;
						rot.BaseHealth = me.Health;
						Generic.SummonBlock.Invoke(g, rot, -1, s);
					})
				}
			}));
			// [BT_722] Guardian / [BT_723] Rocket / [BT_724] Ethereal Augmerchant (1) — Battlecry: Deal 1 damage to a minion
			// and give it Divine Shield / Rush / Spell Damage +1.
			ISimpleTask Augment(string id, ISimpleTask give) => ComplexTask.Create(new DamageTask(V(id, "damage", 1), EntityType.TARGET), give);
			cards.Add("BT_722", new CardDef(BattlecryMinion, new Power { PowerTask = Augment("BT_722", new SetGameTagTask(GameTag.DIVINE_SHIELD, 1, EntityType.TARGET)) }));
			cards.Add("BT_723", new CardDef(BattlecryMinion, new Power { PowerTask = Augment("BT_723", new SetGameTagTask(GameTag.RUSH, 1, EntityType.TARGET)) }));
			cards.Add("BT_724", new CardDef(BattlecryMinion, new Power { PowerTask = Augment("BT_724", new AddEnchantmentTask("BT_724e", EntityType.TARGET)) }));
			cards.Add("BT_724e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.SPELLPOWER, EffectOperator.ADD, V("BT_724", "spell damage", 1))) }));
			// [BT_726] Dragonmaw Sky Stalker (6) — Deathrattle: Summon a 3/4 Dragonrider.
			cards.Add("BT_726", new CardDef(new Power { DeathrattleTask = new SummonTask("BT_726t") }));
			// [BT_727] Soulbound Ashtongue (1) — Whenever this minion takes damage, also deal that amount to your hero.
			cards.Add("BT_727", new CardDef(new Power
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
			// [BT_728] Disguised Wanderer (4) — Deathrattle: Summon a 9/1 Inquisitor.
			cards.Add("BT_728", new CardDef(new Power { DeathrattleTask = new SummonTask("BT_728t") }));
			// [BT_729] Waste Warden (5) — Battlecry: Deal 3 damage to a minion and all others of the same minion type.
			cards.Add("BT_729", new CardDef(BattlecryMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion target)) return;
					var hit = new List<Minion> { target };
					// every minion type it has (IsRace: dual types, Amalgams), then everyone else sharing one
					var types = System.Enum.GetValues(typeof(Race)).Cast<Race>()
						.Where(r => r != Race.INVALID && r != Race.BLANK && r != Race.ALL && target.IsRace(r)).ToList();
					if (types.Count > 0)
						hit.AddRange(c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll())
							.Where(m => m.Id != target.Id && m.Id != s.Id && types.Any(m.IsRace)));
					foreach (Minion m in hit) Generic.DamageCharFunc((IPlayable)s, m, V("BT_729", "damage", 3), false);
				})
			}));
			// [BT_730] Overconfident Orc (3) — Taunt. While at full Health, this has +2 Attack.
			cards.Add("BT_730", new CardDef(new Power
			{
				Aura = new Aura(AuraType.SELF, Effects.Attack_N(V("BT_730", "attack", 2))) { Condition = new SelfCondition(p => p is Minion m && m.Damage == 0), Restless = true }
			}));
			// [BT_731] Infectious Sporeling (1) — After this damages a minion, turn it into an Infectious Sporeling.
			cards.Add("BT_731", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEAL_DAMAGE)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventTarget is Minion hit && hit.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY && !hit.IsDead &&
						    !hit.ToBeDestroyed && hit.Health > 0)
							Generic.TransformBlock.Invoke(hit.Controller, Cards.FromId("BT_731"), hit);
					})
				}
			}));
			// [BT_732] Scavenging Shivarra (6) — Battlecry: Deal 6 damage randomly split among all other minions.
			cards.Add("BT_732", new CardDef(new Power
			{
				PowerTask = new EnqueueTask(V("BT_732", "damage", 6), ComplexTask.DamageRandomTargets(1, EntityType.ALLMINIONS_NOSOURCE, 1))
			}));
			// [BT_733] Mo'arg Artificer (2) — All minions take double damage from spells. (Generic.DamageCharFunc)
			cards.Add("BT_733", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLERS, new Effect(HsTag.MINIONS_TAKE_DOUBLE_SPELL_DAMAGE, EffectOperator.ADD, 1))
			}));
			// [BT_734] Supreme Abyssal (8) — Can't attack heroes.
			cards.Add("BT_734", new CardDef(new Power
			{
				Aura = new Aura(AuraType.SELF, new Effect(GameTag.CANNOT_ATTACK_HEROES, EffectOperator.SET, 1))
			}));
			// [BT_735] Al'ar (5) — Deathrattle: Summon a 0/3 Ashes of Al'ar that resurrects this minion on your next turn.
			cards.Add("BT_735", new CardDef(new Power { DeathrattleTask = new SummonTask("BT_735t") }));
			// [BT_735t] Ashes of Al'ar (1) — At the start of your turn, transform this into Al'ar.
			cards.Add("BT_735t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = new TransformTask("BT_735", EntityType.SOURCE) }
			}));
			// [BT_737] Maiev Shadowsong (4) — Battlecry: Choose a minion. It goes Dormant for 2 turns. (its counter: BT_737e)
			cards.Add("BT_737", new CardDef(BattlecryMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m && m.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY) Generic.MakeDormant(m, V("BT_737", "turns", 2), "BT_737e", (IPlayable)s);
				})
			}));
			// [BT_850] Magtheridon (4) — Dormant. Battlecry: Summon three 1/3 enemy Warders. When they die, destroy all minions
			// and awaken. (its counter: BT_850e, 3 Warders — DormantCards.tsv)
			cards.Add("BT_850", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("BT_850", "warders", 3); i++) Summon(g, c.Opponent, Cards.FromId("BT_850t"), s);
				}),
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerSource = TriggerSource.ALL_MINIONS,
					WhileDormant = true, // counts towards its awakening
					Condition = new SelfCondition(p => p.Card.Id == "BT_850t"),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me && me[GameTag.DORMANT] > 0) Generic.AwakenProgress(me, 1);
					})
				},
				AwakenTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => m.Id != s.Id).ToList())
						m.Destroy();
				})
			}));
		}

		/// <summary>The attack in progress killed its defender (a minion): "attacks and kills" triggers (Kargath Prime,
		/// Hulking Overfiend).</summary>
		private static bool KilledItsDefender(Game g) =>
			g.CurrentEventData?.EventTarget is Minion d && (d.IsDead || d.ToBeDestroyed || d.Health <= 0);

		/// <summary>A minion's board neighbors at the time it's hit (The Lurker Below continues from them).</summary>
		private readonly struct BoardZoneLike
		{
			public readonly Minion Left, Right;
			public BoardZoneLike(Minion m)
			{
				var adjacent = m.GetAdjacentMinions().ToList();
				Left = adjacent.FirstOrDefault(a => a.ZonePosition < m.ZonePosition);
				Right = adjacent.FirstOrDefault(a => a.ZonePosition > m.ZonePosition);
			}
		}
	}
}
