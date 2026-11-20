// HsServer addition (not upstream SabberStone): scripts for current Core cards the 2021 engine never had.
// Every number goes through CardValues (editable without code; see Loader/CardValues.cs).
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
		private static int V(string card, string name, int value) => CardValues.Get(card, name, value);

		private static readonly Dictionary<PlayReq, int> TargetMinion =
			new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } };
		private static readonly Dictionary<PlayReq, int> TargetAny =
			new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 } };
		private static readonly Dictionary<PlayReq, int> TargetEnemy =
			new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } };
		private static readonly Dictionary<PlayReq, int> BattlecryFriendlyMinion =
			new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } };
		private static readonly Dictionary<PlayReq, int> BattlecryMinion =
			new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } };
		private static readonly Dictionary<PlayReq, int> BattlecryAny =
			new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 } };

		/// <summary>Minions in the controller's hand.</summary>
		private static ISimpleTask HandMinions(params SelfCondition[] also) => ComplexTask.Create(
			new IncludeTask(EntityType.HAND),
			new FilterStackTask(new[] { SelfCondition.IsMinion }.Concat(also).ToArray()));

		/// <summary>A task that does nothing (a card whose effect can't happen in this engine, e.g. "destroy an enemy location").</summary>
		private static ISimpleTask Nothing => new CustomTask((g, c, s, t, stack) => { });

		private static void Neutral(IDictionary<string, CardDef> cards)
		{
			// [ULD_191] Beaming Sidekick (1) — Battlecry: Give a friendly minion +2 Health.
			cards.Add("CORE_ULD_191", new CardDef(BattlecryFriendlyMinion, new Power
			{
				PowerTask = new AddEnchantmentTask("ULD_191e", EntityType.TARGET)
			}));
			cards.Add("ULD_191e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("ULD_191e", "health", 2))) }));

			// [CS3_022] Fogsail Freebooter (2) — Battlecry: If you have a weapon equipped, deal 2 damage.
			cards.Add("CS3_022", new CardDef(BattlecryAny, new Power
			{
				PowerTask = ComplexTask.Create(
					new ConditionTask(EntityType.SOURCE, SelfCondition.IsWeaponEquiped),
					new FlagTask(true, new DamageTask(V("CS3_022", "damage", 2), EntityType.TARGET)))
			}));

			// [DMF_067] Prize Vendor (2) — Battlecry and Deathrattle: Each player draws a card.
			cards.Add("CORE_DMF_067", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new DrawTask(), new DrawOpTask()),
				DeathrattleTask = ComplexTask.Create(new DrawTask(), new DrawOpTask())
			}));

			// [ULD_271] Injured Tol'vir (2) — Taunt. Battlecry: Deal 3 damage to this minion.
			cards.Add("CORE_ULD_271", new CardDef(new Power
			{
				PowerTask = new DamageTask(V("CORE_ULD_271", "damage", 3), EntityType.SOURCE)
			}));

			// [EX1_323] Lord Jaraxxus (9) — a Hero card now: playing it makes him your hero (with INFERNO!).
			// Battlecry: Equip a 3/8 Blood Fury. (The 2021 minion replaced the hero from the board.)
			cards.Add("CORE_EX1_323", new CardDef(new Power { PowerTask = new WeaponTask("EX1_323w") }));

			// [SW_072] Rustrot Viper (3) — Tradeable. Battlecry: Destroy your opponent's weapon.
			cards.Add("CORE_SW_072", new CardDef(new Power
			{
				PowerTask = new DestroyTask(EntityType.OP_WEAPON)
			}));

			// [REV_023] Demolition Renovator (3) — Tradeable. Battlecry: Destroy an enemy location. (No locations here.)
			cards.Add("CORE_REV_023", new CardDef(new Power { PowerTask = Nothing }));

			// [REV_308] Maze Guide (2) — Battlecry: Summon a random 2-Cost minion.
			cards.Add("CORE_REV_308", new CardDef(new Power
			{
				PowerTask = ComplexTask.SummonRandomMinion(GameTag.COST, V("CORE_REV_308", "cost", 2))
			}));

			// [SW_066] Royal Librarian (4) — Tradeable. Battlecry: Silence a minion.
			cards.Add("CORE_SW_066", new CardDef(BattlecryMinion, new Power
			{
				PowerTask = new SilenceTask(EntityType.TARGET)
			}));

			// [ICC_466] Saronite Chain Gang (4) — Taunt. Battlecry: Summon a copy of this minion.
			cards.Add("CORE_ICC_466", new CardDef(new Power
			{
				PowerTask = new CopyTask(EntityType.SOURCE, Zone.PLAY)
			}));

			// [CS3_024] Taelan Fordring (5) — Taunt, Divine Shield. Deathrattle: Draw your highest Cost minion.
			cards.Add("CS3_024", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.DeckZone.Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.Count == 0) return;
					int max = minions.Max(p => p.Cost);
					var highest = minions.Where(p => p.Cost == max).ToList();
					Generic.Draw(c, highest[g.Random.Next(highest.Count)]);
				})
			}));

			// [CS3_025] Overlord Runthak (5) — Rush. Whenever this attacks, give +1/+1 to all minions in your hand.
			cards.Add("CS3_025", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = ComplexTask.Create(HandMinions(), new AddEnchantmentTask("CS3_025e", EntityType.STACK))
				}
			}));
			cards.Add("CS3_025e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("CS3_025e", "stats", 1))) }));

			// [SW_068] Mo'arg Forgefiend (8) — Taunt. Deathrattle: Gain 8 Armor.
			cards.Add("CORE_SW_068", new CardDef(new Power
			{
				DeathrattleTask = new ArmorTask(V("CORE_SW_068", "armor", 8))
			}));

			// [YOP_034] Runaway Blackwing (10) — At the end of your turn, deal 10 damage to a random enemy minion.
			cards.Add("CORE_YOP_034", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = ComplexTask.DamageRandomTargets(1, EntityType.OP_MINIONS, V("CORE_YOP_034", "damage", 10))
				}
			}));
		}

		private static void ClassCards(IDictionary<string, CardDef> cards)
		{
			// ---- Druid
			// [SW_439] Vibrant Squirrel (1) — Deathrattle: Shuffle 4 Acorns into your deck. When drawn, summon a 2/1 Squirrel.
			cards.Add("CORE_SW_439", new CardDef(new Power
			{
				DeathrattleTask = new AddCardTo("SW_439t", EntityType.DECK, V("CORE_SW_439", "acorns", 4))
			}));
			cards.Add("SW_439t", new CardDef(new Power
			{
				PowerTask = new SummonTask("SW_439t2"),
				TopdeckTask = new SummonTask("SW_439t2")
			}));

			// [ULD_133] Crystal Merchant (2) — If you have any unspent Mana at the end of your turn, draw a card.
			cards.Add("CORE_ULD_133", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (c.RemainingMana > 0) Generic.Draw(c);
					})
				}
			}));

			// [SW_429] Best in Shell (6) — Tradeable. Summon two 2/7 Turtles with Taunt.
			cards.Add("CORE_SW_429", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new SummonTask("SW_429t", V("CORE_SW_429", "turtles", 2))
			}));

			// [RLK_657] Underking (7) — Rush. Battlecry and Deathrattle: Gain 6 Armor.
			cards.Add("CORE_RLK_657", new CardDef(new Power
			{
				PowerTask = new ArmorTask(V("CORE_RLK_657", "armor", 6)),
				DeathrattleTask = new ArmorTask(V("CORE_RLK_657", "armor", 6))
			}));

			// ---- Hunter
			// [BAR_801] Wound Prey (1) — Deal 1 damage. Summon a 1/1 Hyena with Rush.
			cards.Add("CORE_BAR_801", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("CORE_BAR_801", "damage", 1), EntityType.TARGET, true),
					new SummonTask("BAR_035t"))
			}));

			// [ULD_152] Pressure Plate (2) — Secret: After your opponent casts a spell, destroy a random enemy minion.
			cards.Add("CORE_ULD_152", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = ComplexTask.Secret(ComplexTask.DestroyRandomTargets(1, EntityType.OP_MINIONS))
				}
			}));

			// [BT_201] Augmented Porcupine (3) — Deathrattle: Deal this minion's Attack damage randomly split among all enemies.
			cards.Add("CORE_BT_201", new CardDef(new Power
			{
				DeathrattleTask = ComplexTask.Create(
					new GetGameTagTask(GameTag.ATK, EntityType.SOURCE),
					new EnqueueNumberTask(ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, 1)))
			}));

			// [DRG_256] Dragonbane (4) — After you use your Hero Power, deal 5 damage to a random enemy.
			cards.Add("CORE_DRG_256", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.INSPIRE)
				{
					SingleTask = ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, V("CORE_DRG_256", "damage", 5))
				}
			}));

			// [AV_337] Mountain Bear (7) — Taunt. Deathrattle: Summon two 2/4 Cubs with Taunt.
			cards.Add("CORE_AV_337", new CardDef(new Power
			{
				DeathrattleTask = new SummonTask("AV_337t", V("CORE_AV_337", "cubs", 2))
			}));

			// ---- Mage
			// [DRG_107] Violet Spellwing (1) — Deathrattle: Add an 'Arcane Missiles' spell to your hand.
			cards.Add("CORE_DRG_107", new CardDef(new Power
			{
				DeathrattleTask = new AddCardTo("EX1_277", EntityType.HAND)
			}));

			// [SW_108] First Flame (1) — Deal 2 damage to a minion. Add a Second Flame to your hand.
			cards.Add("CORE_SW_108", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("CORE_SW_108", "damage", 2), EntityType.TARGET, true),
					new AddCardTo("SW_108t", EntityType.HAND))
			}));
			cards.Add("SW_108t", new CardDef(TargetMinion, new Power
			{
				PowerTask = new DamageTask(V("SW_108t", "damage", 2), EntityType.TARGET, true)
			}));

			// [BAR_541] Runed Orb (2) — Deal 2 damage. Discover a spell.
			cards.Add("CORE_BAR_541", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("CORE_BAR_541", "damage", 2), EntityType.TARGET, true),
					new DiscoverTask(DiscoverType.SPELL))
			}));

			// [EDR_001] Babbling Bookcase (3) — Battlecry: Add 2 random Mage spells to your hand.
			cards.Add("CORE_EDR_001", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new RandomCardTask(CardType.SPELL, CardClass.MAGE), new AddStackTo(EntityType.HAND),
					new RandomCardTask(CardType.SPELL, CardClass.MAGE), new AddStackTo(EntityType.HAND))
			}));

			// [BT_072] Deep Freeze (7) — Freeze an enemy. Summon two 3/6 Water Elementals.
			cards.Add("CORE_BT_072", new CardDef(TargetEnemy, new Power
			{
				PowerTask = ComplexTask.Create(
					ComplexTask.Freeze(EntityType.TARGET),
					new SummonTask("CS2_033", V("CORE_BT_072", "elementals", 2)))
			}));

			// ---- Paladin
			// [BT_292] Hand of A'dal (2) — Give a minion +2/+1. Draw a card.
			cards.Add("CORE_BT_292", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("BT_292e", EntityType.TARGET), new DrawTask())
			}));
			cards.Add("BT_292e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("BT_292e", "attack", 2)), Effects.Health_N(V("BT_292e", "health", 1)))
			}));

			// [SW_047] Highlord Fordragon (6) — Divine Shield. After a friendly minion loses Divine Shield, give a minion in
			// your hand +5/+5.
			cards.Add("CORE_SW_047", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.LOSE_DIVINE_SHIELD)
				{
					TriggerSource = TriggerSource.MINIONS,
					SingleTask = ComplexTask.Create(HandMinions(), new RandomTask(1, EntityType.STACK),
						new AddEnchantmentTask("SW_047e", EntityType.STACK))
				}
			}));
			cards.Add("SW_047e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("SW_047e", "stats", 5))) }));

			// [TSC_076] Immortalized in Stone (7) — Summon a 4/8, 2/4, and 1/2 Elemental with Taunt.
			cards.Add("CORE_TSC_076", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = ComplexTask.Create(new SummonTask("TSC_076t"), new SummonTask("TSC_076t2"), new SummonTask("TSC_076t3"))
			}));

			// ---- Priest
			// [EX1_193] Psychic Conjurer (1) — Battlecry: Copy a card in your opponent's deck and add it to your hand.
			cards.Add("CORE_EX1_193", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new IncludeTask(EntityType.OP_DECK), new RandomTask(1, EntityType.STACK), new CopyTask(EntityType.STACK, Zone.HAND))
			}));

			// [BAR_311] Devouring Plague (3) — Lifesteal. Deal 4 damage randomly split among all enemy minions.
			cards.Add("CORE_BAR_311", new CardDef(new Power
			{
				PowerTask = new EnqueueTask(V("CORE_BAR_311", "damage", 4), ComplexTask.DamageRandomTargets(1, EntityType.OP_MINIONS, 1), true)
			}));

			// [EX1_197] Shadow Word: Ruin (4) — Destroy all minions with 5 or more Attack.
			cards.Add("CORE_EX1_197", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new IncludeTask(EntityType.ALLMINIONS),
					new FilterStackTask(SelfCondition.IsTagValue(GameTag.ATK, V("CORE_EX1_197", "attack", 5), RelaSign.GEQ)),
					new DestroyTask(EntityType.STACK))
			}));

			// [SW_442] Void Shard (4) — Lifesteal. Deal 4 damage.
			cards.Add("CORE_SW_442", new CardDef(TargetAny, new Power
			{
				PowerTask = new DamageTask(V("CORE_SW_442", "damage", 4), EntityType.TARGET, true)
			}));

			// [SCH_512] Initiation (6) — Deal 4 damage to a minion. If it dies, summon a new copy.
			cards.Add("CORE_SCH_512", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("CORE_SCH_512", "damage", 4), EntityType.TARGET, true),
					new CustomTask((g, c, s, t, stack) =>
					{
						if (t is Minion m && m.IsDead && !c.BoardZone.IsFull)
							Generic.SummonBlock.Invoke(g, (Minion)Entity.FromCard(c, m.Card), -1, s);
					}))
			}));

			// [BAR_310] Lightshower Elemental (6) — Taunt. Deathrattle: Restore 8 Health to all friendly characters.
			cards.Add("CORE_BAR_310", new CardDef(new Power
			{
				DeathrattleTask = new HealTask(V("CORE_BAR_310", "heal", 8), EntityType.FRIENDS)
			}));

			// [CATA_002] Calia Menethil (6) — Battlecry: Resurrect your highest-Cost minion that died this game.
			cards.Add("CORE_CATA_002", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					// "died this game": destroyed on the board only (not discarded or destroyed in the deck)
					var dead = c.DiedThisGameCards.Where(p => p is Minion)
						.Where(p => p.Id != s.Id).ToList();
					if (dead.Count == 0 || c.BoardZone.IsFull) return;
					int max = dead.Max(p => p.Card.Cost);
					var highest = dead.Where(p => p.Card.Cost == max).ToList();
					Generic.SummonBlock.Invoke(g, (Minion)Entity.FromCard(c, highest[g.Random.Next(highest.Count)].Card), -1, s);
				})
			}));

			// ---- Rogue
			// [ULD_280] Sahket Sapper (4) — Deathrattle: Return a random enemy minion to your opponent's hand.
			cards.Add("CORE_ULD_280", new CardDef(new Power
			{
				DeathrattleTask = ComplexTask.Create(new RandomTask(1, EntityType.OP_MINIONS, harmful: true), new ReturnHandTask(EntityType.STACK))
			}));

			// ---- Shaman
			// [WC_042] Wailing Vapor (1) — After you play an Elemental, gain +1 Attack.
			cards.Add("CORE_WC_042", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = SelfCondition.IsRace(Race.ELEMENTAL),
					SingleTask = new AddEnchantmentTask("WC_042e", EntityType.SOURCE)
				}
			}));
			cards.Add("WC_042e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("WC_042e", "attack", 1))) }));

			// ---- Warlock
			// [YOD_026] Fiendish Servant (1) — Deathrattle: Give this minion's Attack to a random friendly minion.
			cards.Add("CORE_YOD_026", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var friends = c.BoardZone.Where(m => m.Id != s.Id).ToList();
					if (friends.Count == 0) return;
					Minion to = friends[g.Random.Next(friends.Count)];
					to.AttackDamage += ((ICharacter)s).AttackDamage;
				})
			}));

			// [SW_088] Demonic Assault (4) — Deal 3 damage. Summon two 1/3 Voidwalkers with Taunt.
			cards.Add("CORE_SW_088", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("CORE_SW_088", "damage", 3), EntityType.TARGET, true),
					new SummonTask("CS2_065", V("CORE_SW_088", "voidwalkers", 2)))
			}));

			// [ULD_165] Riftcleaver (6) — Battlecry: Destroy a minion. Your hero takes damage equal to its Health.
			cards.Add("CORE_ULD_165", new CardDef(BattlecryMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					int health = m.Health;
					m.Destroy();
					c.Hero.TakeDamage((IPlayable)s, health);
				})
			}));

			// ---- Warrior
			// [DRG_024] Sky Raider (1) — Battlecry: Add a random Pirate to your hand.
			cards.Add("CORE_DRG_024", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new RandomCardTask(CardType.MINION, CardClass.INVALID, Race.PIRATE), new AddStackTo(EntityType.HAND))
			}));

			// [NX2_028] Hookfist-3000 (3) — After your hero attacks, gain 4 Armor and draw a card.
			cards.Add("CORE_NX2_028", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = ComplexTask.Create(new ArmorTask(V("CORE_NX2_028", "armor", 4)), new DrawTask())
				}
			}));

			// [WW_329] Detonation Juggernaut (4) — Taunt. Battlecry: Give Taunt minions in your hand +2/+2.
			cards.Add("CORE_WW_329", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(HandMinions(SelfCondition.IsTagValue(GameTag.TAUNT, 1)), new AddEnchantmentTask("WW_329e", EntityType.STACK))
			}));
			cards.Add("WW_329e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("WW_329e", "stats", 2))) }));
		}

		// Client spell schools (TAG_SPELL_SCHOOL, tag 1635).
		private const GameTag SPELL_SCHOOL = (GameTag)1635;
		private const int HOLY = 5, SHADOW = 6;
		private static SelfCondition IsSchool(int school) => new SelfCondition(p => p[SPELL_SCHOOL] == school);

		private static void Batch2(IDictionary<string, CardDef> cards)
		{
			// [ONY_018] Boomkin (5) — Choose One - Restore 8 Health to your hero; or Deal 4 damage.
			cards.Add("CORE_ONY_018", new CardDef(new Power()));
			cards.Add("ONY_018t", new CardDef(new Power { PowerTask = new HealTask(V("ONY_018t", "heal", 8), EntityType.HERO) }));
			cards.Add("ONY_018t2", new CardDef(TargetAny, new Power { PowerTask = new DamageTask(V("ONY_018t2", "damage", 4), EntityType.TARGET) }));

			// [TSC_650] Flipper Friends (5) — Choose One - Summon a 6/6 Orca with Taunt; or six 1/1 Otters with Rush.
			cards.Add("CORE_TSC_650", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power()));
			cards.Add("TSC_650a", new CardDef(new Power { PowerTask = new SummonTask("TSC_650t") }));
			cards.Add("TSC_650d", new CardDef(new Power { PowerTask = new SummonTask("TSC_650t4", V("TSC_650d", "otters", 6)) }));

			// [BAR_878] Veteran Warmedic (4) — After you cast a Holy spell, summon a 2/2 Medic with Lifesteal.
			cards.Add("CORE_BAR_878", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = IsSchool(HOLY),
					SingleTask = new SummonTask("BAR_878t")
				}
			}));

			// [ONY_022] Battle Vicar (2) — Battlecry: Discover a Holy spell.
			cards.Add("CORE_ONY_022", new CardDef(new Power
			{
				PowerTask = new DiscoverTask(CardType.SPELL, CardClass.INVALID, (SPELL_SCHOOL, RelaSign.EQ, HOLY))
			}));

			// [RLK_814] Crystalsmith Cultist (1) — Battlecry: If you're holding a Shadow spell, gain +1/+1.
			cards.Add("CORE_RLK_814", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.HandZone.Any(p => p is Spell && p[SPELL_SCHOOL] == SHADOW),
					new CustomTask((g, c, s, t, stack) => Generic.AddEnchantmentBlock(g, Cards.FromId("RLK_814e"), (IPlayable)s, (IPlayable)s, 0, 0, 0)))
			}));
			cards.Add("RLK_814e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("RLK_814e", "stats", 1))) }));

			// [SCH_713] Cult Neophyte (2) — Battlecry: Your opponent's spells cost (1) more next turn. (Loatheb's pattern.)
			cards.Add("CORE_SCH_713", new CardDef(new Power
			{
				PowerTask = new AddEnchantmentTask("SCH_713e", EntityType.OP_CONTROLLER)
			}));
			cards.Add("SCH_713e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.OP_HAND, Effects.AddCost(V("SCH_713e", "cost", 1)))
				{
					Condition = SelfCondition.IsSpell,
					RemoveTrigger = (TriggerType.TURN_END, SelfCondition.IsOpTurn)
				}
			}));

			// [DMF_511] Foxy Fraud (2) — Battlecry: Your next Combo card this turn costs (2) less. (Preparation's pattern.)
			cards.Add("CORE_DMF_511", new CardDef(new Power
			{
				PowerTask = new AddEnchantmentTask("DMF_511e", EntityType.CONTROLLER)
			}));
			cards.Add("DMF_511e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("DMF_511e", "discount", 2)))
				{
					Condition = SelfCondition.IsComboCard,
					RemoveTrigger = (TriggerType.PLAY_CARD, SelfCondition.IsComboCard)
				}
			}));

			// [EX1_198] Natalie Seline (7) — Battlecry: Destroy a minion and gain its Health.
			cards.Add("CORE_EX1_198", new CardDef(BattlecryMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m) || !(s is Minion me)) return;
					int health = m.Health;
					Generic.DestroyFx(me, m, () => m.Destroy());
					// live: the Health comes as her EX1_198e enchantment ("Increased Health."), the amount in its
					// TAG_SCRIPT_DATA_NUM_1 (so Silence removes it and the client shows it)
					Generic.AbsorbFx(me, m, () => Generic.AddEnchantmentBlock(g, Cards.FromId("EX1_198e"), me, me, health, 0));
				})
			}));
			cards.Add("EX1_198e", new CardDef(new Power { Enchant = Enchants.Enchants.AddHealthScriptTag }));

			// [TID_931] Jackpot! (2) — Add two random spells from other classes that cost (5) or more to your hand.
			cards.Add("CORE_TID_931", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int min = V("CORE_TID_931", "min cost", 5);
					var pool = Cards.GenerationPool(g, c)
						.Where(card => card.Type == CardType.SPELL && card.Cost >= min && !card.HasClass(c.HeroClass) && !card.HasClass(CardClass.NEUTRAL))
						.ToList();
					for (int i = 0; i < V("CORE_TID_931", "spells", 2) && pool.Count > 0 && !c.HandZone.IsFull; i++)
						Generic.AddHandPhase.Invoke(c, Entity.FromCard(c, pool[g.Random.Next(pool.Count)]));
				})
			}));

			// [WON_096] Dark Peddler (2) — Battlecry: Discover a 1-Cost card.
			cards.Add("CORE_WON_096", new CardDef(new Power
			{
				PowerTask = new DiscoverTask(CardType.INVALID, CardClass.INVALID, (GameTag.COST, RelaSign.EQ, V("CORE_WON_096", "cost", 1)))
			}));

			// [AV_107] Glaciate (6) — Discover an 8-Cost minion. Summon and Freeze it.
			cards.Add("CORE_AV_107", new CardDef(new Power
			{
				PowerTask = new DiscoverTask(CardType.MINION, CardClass.INVALID, (GameTag.COST, RelaSign.EQ, V("CORE_AV_107", "cost", 8)),
					ChoiceAction.SUMMON, ComplexTask.Freeze(EntityType.TARGET))
			}));

			// [SCH_181] Archwitch Willow (8) — Battlecry: Summon a random Demon from your hand and deck.
			cards.Add("CORE_SCH_181", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (var zone in new IEnumerable<IPlayable>[] { c.HandZone.ToArray(), c.DeckZone.ToArray() })
					{
						var demons = zone.Where(p => p is Minion m && m.IsRace(Race.DEMON)).ToList();
						if (demons.Count == 0 || c.BoardZone.IsFull) continue;
						var demon = (Minion)demons[g.Random.Next(demons.Count)];
						demon.Zone.Remove(demon);
						Generic.SummonBlock.Invoke(g, demon, -1, s);
					}
				})
			}));

			// [ETC_111] Merch Seller (4) — At the end of your turn, put a random spell on the top of your opponent's deck.
			cards.Add("CORE_ETC_111", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var spells = Cards.GenerationPool(g, c).Where(card => card.Type == CardType.SPELL).ToList();
						if (spells.Count == 0 || c.Opponent.DeckZone.IsFull) return;
						c.Opponent.DeckZone.Add(Entity.FromCard(c.Opponent, spells[g.Random.Next(spells.Count)]));
					})
				}
			}));

			// [SCH_717] Keymaster Alabaster (7) — Whenever your opponent draws a card, add a copy to your hand that costs (1).
			cards.Add("CORE_SCH_717", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DRAW)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable drawn = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (drawn == null || c.HandZone.IsFull) return;
						IPlayable copy = Entity.FromCard(c, drawn.Card);
						copy.Cost = V("CORE_SCH_717", "cost", 1);
						Generic.AddHandPhase.Invoke(c, copy);
					})
				}
			}));

			// [WON_141] Menagerie Mug (3) — Battlecry: Give 3 random friendly minions of different minion types +1/+1.
			cards.Add("CORE_WON_141", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var typed = c.BoardZone.Where(m => m.Id != s.Id && ((Race)m[GameTag.CARDRACE]) != Race.INVALID && ((Race)m[GameTag.CARDRACE]) != Race.BLANK).ToList();
					var used = new HashSet<Race>();
					for (int i = 0; i < V("CORE_WON_141", "minions", 3); i++)
					{
						var options = typed.Where(m => !used.Contains(((Race)m[GameTag.CARDRACE])) || ((Race)m[GameTag.CARDRACE]) == Race.ALL).ToList();
						if (options.Count == 0) break;
						Minion pick = options[g.Random.Next(options.Count)];
						used.Add(((Race)pick[GameTag.CARDRACE]));
						typed.Remove(pick);
						Generic.AddEnchantmentBlock(g, Cards.FromId("WON_141e"), (IPlayable)s, pick, 0, 0, 0);
					}
				})
			}));
			cards.Add("WON_141e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("WON_141e", "stats", 1))) }));

			// [SCH_605] Lake Thresher (5) — Also damages the minions next to whomever this attacks. (The shared Cleave.)
			cards.Add("CORE_SCH_605", new CardDef(new Power { Trigger = Cleave() }));

			// [CS3_035] Nozdormu the Eternal (7) — Start of Game: if in both decks, turns are 15 seconds. (The turn timer is the
			// server's, not the rules engine's: no rules effect.)
			cards.Add("CS3_035", new CardDef(new Power { PowerTask = Nothing }));
		}

		private static readonly Dictionary<PlayReq, int> BattlecryEnemyMinion =
			new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } };

		/// <summary>A random collectible minion with exactly this Cost for the battlefield (every caller summons it or transforms
		/// a minion in play into it: the board pool, no Colossals — Card.NeverRandomlyOnBoard), or null.</summary>
		private static Card RandomMinionOfCost(Game g, int cost) => RandomMinionOfCost(g, cost, null);

		/// <summary>The same, among the minions matching <paramref name="filter"/> ("a random 1-Cost Deathrattle minion", "a 2-Cost Taunt
		/// minion", "a random 5-Cost Beast"); none of that Cost: null (nothing happens).</summary>
		private static Card RandomMinionOfCost(Game g, int cost, System.Func<Card, bool> filter)
		{
			if (!Cards.BoardCostMinionCards(g.FormatType).TryGetValue(cost, out List<Card> all)) return null;
			Card self = Cards.GeneratingCard(g); // never the card generating it; deck-gated families only when its deck opens them
			Controller generator = Cards.GeneratingPlayer(g);
			var pool = all.Where(card => card.Id != self?.Id && Cards.DeckAllows(generator, card) && (filter == null || filter(card))).ToList();
			return pool.Count > 0 ? pool[g.Random.Next(pool.Count)] : null;
		}

		/// <summary>Shadow of Demise: the card in hand becomes a copy of the spell just cast, and keeps doing so.</summary>
		private static ISimpleTask ShadowOfDemise => new CustomTask((g, c, s, t, stack) =>
		{
			IPlayable me = s is Enchantment e ? e.Target as IPlayable : s as IPlayable;
			if (me == null || t == null || me.Id == t.Id || me.Zone?.Type != SabberStoneCore.Enums.Zone.HAND) return;
			IPlayable copy = Generic.ChangeEntityBlock(c, me, t.Card, true);
			Generic.AddEnchantmentBlock(g, Cards.FromId("RLK_567e2"), copy, copy, 0, 0, 0);
		});

		private static void Batch3(IDictionary<string, CardDef> cards)
		{
			// [ULD_178] Siamat (7) — Battlecry: Gain 2 of Rush, Taunt, Divine Shield, or Windfury (your choice).
			// Two adapt-style picks from the four client option cards; the second offers the three left.
			string[] siamat = { "ULD_178a2", "ULD_178a", "ULD_178a3", "ULD_178a4" };
			cards.Add("CORE_ULD_178", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var self = new List<IPlayable> { (IPlayable)s };
					var second = new CustomTask((g2, c2, s2, chosen, stack2) =>
						Generic.CreateChoiceCards.Invoke(c2, s2, stack2?.Playables ?? self, ChoiceType.GENERAL, ChoiceAction.ADAPT,
							siamat.Where(id => id != chosen.Card.Id).Select(Cards.FromId).ToArray(), null));
					Generic.CreateChoiceCards.Invoke(c, s, self, ChoiceType.GENERAL, ChoiceAction.ADAPT,
						siamat.Select(Cards.FromId).ToArray(), second);
				})
			}));
			cards.Add("ULD_178a", new CardDef(new Power { PowerTask = new SetGameTagTask(GameTag.WINDFURY, 1, EntityType.TARGET) }));
			cards.Add("ULD_178a2", new CardDef(new Power { PowerTask = new SetGameTagTask(GameTag.DIVINE_SHIELD, 1, EntityType.TARGET) }));
			cards.Add("ULD_178a3", new CardDef(new Power { PowerTask = new SetGameTagTask(GameTag.TAUNT, 1, EntityType.TARGET) }));
			cards.Add("ULD_178a4", new CardDef(new Power { PowerTask = new SetGameTagTask(GameTag.RUSH, 1, EntityType.TARGET) }));

			// [TRL_345] Krag'wa, the Frog (6) — Battlecry: Return all spells you played last turn to your hand.
			cards.Add("CORE_TRL_345", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (PlayHistoryEntry played in c.PlayHistory.Where(e => e.Turn == g.Turn - 2 && e.SourceCard.Type == CardType.SPELL).ToList())
					{
						if (c.HandZone.IsFull) break;
						Generic.AddHandPhase.Invoke(c, Entity.FromCard(c, played.SourceCard));
					}
				})
			}));

			// [BT_120] Warmaul Challenger (3) — Battlecry: Choose an enemy minion. Battle it to the death!
			cards.Add("CORE_BT_120", new CardDef(BattlecryEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me) || !(t is Minion foe)) return;
					for (int round = 0; round < 50; round++)
					{
						if (me.ToBeDestroyed || foe.ToBeDestroyed || me.Health <= 0 || foe.Health <= 0) break;
						if (me.AttackDamage <= 0 && foe.AttackDamage <= 0) break;
						int mine = me.AttackDamage, theirs = foe.AttackDamage;
						foe.TakeDamage(me, mine);
						me.TakeDamage(foe, theirs);
					}
				})
			}));

			// [BT_781] Bulwark of Azzinoth (3, 1/4 weapon) — Whenever your hero would take damage, this loses 1 Durability instead.
			cards.Add("CORE_BT_781", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PREDAMAGE)
				{
					TriggerSource = TriggerSource.HERO,
					FastExecution = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Weapon w) || w.ToBeDestroyed || g.CurrentEventData == null || g.CurrentEventData.EventNumber <= 0) return;
						g.CurrentEventData.EventNumber = 0;
						w.Damage += V("CORE_BT_781", "durability lost", 1);
					})
				}
			}));

			// [BAR_313] Priest of An'she (5) — Taunt. Battlecry: If you've restored Health this turn, gain +3/+3.
			cards.Add("CORE_BAR_313", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.AmountHealedThisTurn > 0,
					new CustomTask((g, c, s, t, stack) => Generic.AddEnchantmentBlock(g, Cards.FromId("BAR_313e"), (IPlayable)s, (IPlayable)s, 0, 0, 0)))
			}));
			cards.Add("BAR_313e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BAR_313e", "stats", 3))) }));

			// [BAR_812] Oasis Ally (3) — Secret: When a friendly minion is attacked, summon a 3/6 Water Elemental.
			cards.Add("CORE_BAR_812", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.ENEMY,
					Condition = new SelfCondition(p => p.Game.CurrentEventData?.EventTarget is Minion m && m.Controller != p.Controller),
					SingleTask = ComplexTask.Secret(new SummonTask("CS2_033", SummonSide.SPELL))
				}
			}));

			// [CATA_006] Ulfar (6) — Battlecry: Give your other minions "Deathrattle: Summon a minion with this minion's Cost."
			cards.Add("CORE_CATA_006", new CardDef(new Power
			{
				PowerTask = new AddEnchantmentTask("CORE_CATA_006e", EntityType.MINIONS_NOSOURCE)
			}));
			cards.Add("CORE_CATA_006e", new CardDef(new Power
			{
				DeathrattleTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) =>
					{
						Card card = s is IPlayable dying ? RandomMinionOfCost(g, dying.Cost) : null;
						stack.Playables = card == null ? new List<IPlayable>() : new List<IPlayable> { Entity.FromCard(c, card) };
					}),
					new SummonTask(SummonSide.DEATHRATTLE))
			}));

			// [CATA_004] Rehgar Earthfury (5) — After this or an adjacent minion attacks, get a Lightning Bolt.
			cards.Add("CORE_CATA_004", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.MINIONS,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me) || !(t is Minion attacker) || me.Zone?.Type != SabberStoneCore.Enums.Zone.PLAY) return;
						if (attacker.Zone != me.Zone || System.Math.Abs(attacker.ZonePosition - me.ZonePosition) > 1) return;
						if (!c.HandZone.IsFull)
							Generic.AddHandPhase.Invoke(c, Entity.FromCard(c, Cards.FromId("CORE_EX1_238")));
					})
				}
			}));

			// [RLK_567] Shadow of Demise (0) — Each time you cast a spell, transform this into a copy of it.
			cards.Add("CORE_RLK_567", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerActivation = TriggerActivation.HAND,
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = ShadowOfDemise
				}
			}));
			cards.Add("RLK_567e2", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = ShadowOfDemise
				}
			}));

			// [REV_018] Prince Renathal (3) — Your deck size and starting Health are 40. (Deck size is a deck-building
			// rule for the server's collection; the engine applies the Health at the start of the game.)
			cards.Add("CORE_REV_018", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.GAME_START)
				{
					TriggerActivation = TriggerActivation.DECK,
					SingleTask = new CustomTask((g, c, s, t, stack) => c.Hero.BaseHealth = V("CORE_REV_018", "health", 40)),
					RemoveAfterTriggered = true
				}
			}));

			// [REV_946] Steamcleaner (5) — Battlecry: Destroy ALL cards in both players' decks that didn't start there.
			cards.Add("CORE_REV_946", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Controller p in new[] { c, c.Opponent })
						foreach (IPlayable card in p.DeckZone.ToArray().Where(x => x.Id >= g.FirstGeneratedEntityId))
							Generic.Destroy(card, s, many: true);
				})
			}));

			// [TTN_851] Resistance Aura (2) — Your opponent's spells cost (1) more. Lasts 2 enemy turns.
			// (a Paladin Aura: PaladinAura, counting enemy turns; the cost change shows as the client's TTN_851e)
			cards.Add("TTN_851", new CardDef(PaladinAura(new Power
			{
				Aura = new Aura(AuraType.OP_HAND, Effects.AddCost(V("TTN_851", "cost", 1))) { Condition = SelfCondition.IsSpell, ShowAs = "TTN_851e" }
			}, enemyTurns: true, countdownIndex: 0, countdownKeyword: GameTag.TRIGGER_VISUAL))); // (live issues 3471: index 0, TRIGGER_VISUAL, not shown)

			// [WON_145] Avatar of Hearthstone (9) — Battlecry: Open a Standard Pack. Play all cards from it.
			cards.Add("CORE_WON_145", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Card card in OpenPack(g))
						PlayFromPack(g, c, s, card);
				})
			}));
		}

		/// <summary>Five random collectible Standard cards with pack odds (at least one Rare or better).</summary>
		private static List<Card> OpenPack(Game g)
		{
			var pool = Cards.GenerationPool(g, Cards.GeneratingPlayer(g), FormatType.FT_STANDARD).Where(card => card.Collectible && card.Type != CardType.HERO_POWER).ToList();
			var pack = new List<Card>();
			for (int i = 0; i < 5; i++)
			{
				int roll = g.Random.Next(1000);
				Rarity rarity = roll < 11 ? Rarity.LEGENDARY : roll < 55 ? Rarity.EPIC : roll < 283 || i == 4 && pack.All(p => p.Rarity == Rarity.COMMON || p.Rarity == Rarity.FREE) ? Rarity.RARE : Rarity.COMMON;
				var ofRarity = pool.Where(card => card.Rarity == rarity || rarity == Rarity.COMMON && card.Rarity == Rarity.FREE).ToList();
				if (ofRarity.Count == 0) ofRarity = pool;
				pack.Add(ofRarity[g.Random.Next(ofRarity.Count)]);
			}
			return pack;
		}

		/// <summary>Avatar of Hearthstone, Murozond: cards played by an effect — the shared engine action.</summary>
		private static void PlayFromPack(Game g, Controller c, IEntity source, Card card) => Generic.PlayByEffect(g, c, source, card);

		// ---------------------------------------------------------------- Demon Hunter

		/// <summary>The client's OUTCAST keyword tag.</summary>
		private const GameTag OUTCAST = (GameTag)1333;

		/// <summary>Outcast: this card was the left- or right-most card in hand when played (Controller.OutcastPlayId).</summary>
		private static readonly SelfCondition PlayedAsOutcast = new SelfCondition(p => p.Controller.OutcastPlayId == p.Id);

		/// <summary>Is in the left- or right-most hand slot right now (Outcast cost effects).</summary>
		private static bool InOutcastSlot(IPlayable p) =>
			p.Zone is SabberStoneCore.Model.Zones.HandZone hand && (p.ZonePosition == 0 || p.ZonePosition == hand.Count - 1);

		private static readonly SelfCondition IsOutcastCard = new SelfCondition(p => p.Card[OUTCAST] > 0);

		/// <summary>
		/// A controller enchantment: matching cards in hand get <paramref name="effect"/> until one of them is played
		/// (and, for "this turn" effects, until the turn ends).
		/// </summary>
		private static CardDef NextCardEffect(IEffect effect, SelfCondition which, bool thisTurnOnly) => new CardDef(new Power
		{
			Aura = new Aura(AuraType.HAND, effect)
			{
				Condition = which,
				RemoveTrigger = thisTurnOnly ? (TriggerType.TURN_END, null) : (TriggerType.NONE, null)
			},
			Trigger = new Trigger(TriggerType.PLAY_CARD)
			{
				TriggerSource = TriggerSource.FRIENDLY,
				Condition = which,
				SingleTask = RemoveEnchantmentTask.Task
			}
		});

		private static void DemonHunter(IDictionary<string, CardDef> cards)
		{
			// [HERO_10bp] Demon Claws (1) — Hero Power: +1 Attack this turn. (Skins borrow it: MapHeroPowerVariants.)
			cards.Add("HERO_10bp", new CardDef(new Power { PowerTask = new AddEnchantmentTask("HERO_10bpe", EntityType.HERO) }));
			cards.Add("HERO_10bpe", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("HERO_10bp", "attack", 1))) { IsOneTurnEffect = true } }));
			// [HERO_10bp2] Demon's Bite (1) — Hero Power: +2 Attack this turn (upgraded).
			cards.Add("HERO_10bp2", new CardDef(new Power { PowerTask = new AddEnchantmentTask("HERO_10pe2", EntityType.HERO) }));
			cards.Add("HERO_10pe2", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("HERO_10bp2", "attack", 2))) { IsOneTurnEffect = true } }));

			// [BT_351] Battlefiend (1) — After your hero attacks, gain +1 Attack.
			cards.Add("CORE_BT_351", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new AddEnchantmentTask("BT_351e", EntityType.SOURCE)
				}
			}));
			cards.Add("BT_351e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("BT_351e", "attack", 1))) }));

			// [BT_480] Crimson Sigil Runner (1) — Outcast: Draw a card.
			cards.Add("CORE_BT_480", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new ConditionTask(EntityType.SOURCE, PlayedAsOutcast),
					new FlagTask(true, new DrawTask()))
			}));

			// [YOP_001] Illidari Studies (1) — Discover an Outcast card. Your next one costs (1) less.
			cards.Add("CORE_YOP_001", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new DiscoverTask(CardType.INVALID, CardClass.DEMONHUNTER, (OUTCAST, RelaSign.EQ, 1)),
					new AddEnchantmentTask("YOP_001e", EntityType.CONTROLLER))
			}));
			cards.Add("YOP_001e", NextCardEffect(Effects.ReduceCost(V("YOP_001e", "discount", 1)), IsOutcastCard, thisTurnOnly: false));

			// [BT_035] Chaos Strike (2) — Give your hero +2 Attack this turn. Draw a card.
			cards.Add("CORE_BT_035", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("BT_035e", EntityType.HERO), new DrawTask())
			}));
			cards.Add("BT_035e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("BT_035e", "attack", 2))) { IsOneTurnEffect = true } }));

			// Legacy (Demon Hunter Initiate) cards that had no script — shared pieces only.
			// [BT_352] Satyr Overseer (3) — After your hero attacks, summon a 2/2 Satyr. (Piranha Launcher's trigger)
			cards.Add("BT_352", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new SummonTask("BT_352t")
				}
			}));

			// [BT_495] Glaivebound Adept (5) — Battlecry: If your hero attacked this turn, deal 4 damage.
			cards.Add("BT_495", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE_AND_HERO_ATTACKED_THIS_TURN, 0 } }, new Power
			{
				PowerTask = new DamageTask(V("BT_495", "damage", 4), EntityType.TARGET)
			}));

			// [BT_512] Inner Demon (8) — Give your hero +8 Attack this turn. (Chaos Strike's one-turn hero enchantment)
			cards.Add("BT_512", new CardDef(new Power { PowerTask = new AddEnchantmentTask("BT_512e", EntityType.HERO) }));
			cards.Add("BT_512e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("BT_512e", "attack", 8))) { IsOneTurnEffect = true } }));

			// [BT_740] Soul Cleave (3) — Lifesteal. Deal $2 damage to two random enemy minions. (Cleave; Lifesteal from the tag)
			cards.Add("BT_740", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_MINIMUM_ENEMY_MINIONS, 1 } }, new Power
			{
				PowerTask = ComplexTask.DamageRandomTargets(V("BT_740", "targets", 2), EntityType.OP_MINIONS, V("BT_740", "damage", 2), true)
			}));

			// [EX1_196] Scarlet Subjugator (1) — Battlecry: Give an enemy minion -2 Attack until your next turn.
			cards.Add("EX1_196", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				PowerTask = new AddEnchantmentTask("EX1_196e", EntityType.TARGET)
			}));
			// "until your next turn": removed at the start of the caster's next turn (the enchantment is the caster's; Conceal's pattern)
			cards.Add("EX1_196e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(-V("EX1_196e", "attack", 2))),
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = RemoveEnchantmentTask.Task, RemoveAfterTriggered = true }
			}));

			// [BT_321] Netherwalker (2) — Battlecry: Discover a Demon.
			cards.Add("CORE_BT_321", new CardDef(new Power { PowerTask = new DiscoverTask(DiscoverType.DEMON) }));

			// [BT_491] Spectral Sight (2) — Draw a card. Outcast: Draw another.
			cards.Add("CORE_BT_491", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new DrawTask(),
					new ConditionTask(EntityType.SOURCE, PlayedAsOutcast),
					new FlagTask(true, new DrawTask()))
			}));

			// [BT_801] Eye Beam (3) — Lifesteal. Deal 3 damage to a minion. Outcast: This costs (1).
			cards.Add("CORE_BT_801", new CardDef(TargetMinion, new Power
			{
				PowerTask = new DamageTask(V("CORE_BT_801", "damage", 3), EntityType.TARGET, true),
				Aura = new AdaptiveCostEffect(p => InOutcastSlot(p) ? p.Card.Cost - V("CORE_BT_801", "outcast cost", 1) : 0)
			}));

			// [WC_701] Felrattler (3) — Rush. Deathrattle: Deal 1 damage to all enemy minions.
			cards.Add("CORE_WC_701", new CardDef(new Power
			{
				DeathrattleTask = new DamageTask(V("CORE_WC_701", "damage", 1), EntityType.OP_MINIONS)
			}));

			// [BT_187] Kayn Sunfury (4) — Charge. All friendly attacks ignore Taunt.
			cards.Add("CORE_BT_187", new CardDef(new Power
			{
				Aura = new MultiAura(
					new Aura(AuraType.BOARD, new Effect(GameTag.IGNORE_TAUNT, EffectOperator.SET, 1)),
					new Aura(AuraType.HERO, new Effect(GameTag.IGNORE_TAUNT, EffectOperator.SET, 1)))
			}));

			// [TTN_843] Eredar Deceptor (4) — Whenever you draw a card, summon a 1/1 Demon with Rush.
			cards.Add("CORE_TTN_843", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DRAW)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new SummonTask("TTN_843t1", SummonSide.RIGHT)
				}
			}));

			// [BT_416] Raging Felscreamer (4) — Battlecry: The next Demon you play costs (2) less.
			cards.Add("CORE_BT_416", new CardDef(new Power { PowerTask = new AddEnchantmentTask("BT_416e", EntityType.CONTROLLER) }));
			cards.Add("BT_416e", NextCardEffect(Effects.ReduceCost(V("BT_416e", "discount", 2)), SelfCondition.IsRace(Race.DEMON), thisTurnOnly: false));

			// [BT_510] Wrathspike Brute (5) — Taunt. After this is attacked, deal 1 damage to all enemies. (Any attacker: the shared
			// "this is attacked" source, TriggerSource.ATTACKED_SELF — the text has no "enemy".)
			cards.Add("CORE_BT_510", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.ATTACKED_SELF,
					SingleTask = new DamageTask(V("CORE_BT_510", "damage", 1), EntityType.ENEMIES)
				}
			}));

			// [TTN_866] Mythical Terror (7) — Lifesteal. At the end of your turn, force all enemy minions to attack this.
			cards.Add("CORE_TTN_866", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion terror)) return;
						EventMetaData temp = g.CurrentEventData;
						foreach (Minion enemy in c.Opponent.BoardZone.ToArray())
						{
							if (terror.IsDead || terror.ToBeDestroyed || terror.Zone?.Type != SabberStoneCore.Enums.Zone.PLAY) break;
							if (enemy.IsDead || enemy.ToBeDestroyed || enemy.AttackDamage <= 0) continue;
							Generic.AttackBlock.Invoke(c.Opponent, enemy, terror, true, true);
						}
						g.DeathProcessingAndAuraUpdate();
						g.CurrentEventData = temp;
					})
				}
			}));

			// [BT_493] Priestess of Fury (7) — At the end of your turn, deal 6 damage randomly split among all enemies.
			cards.Add("CORE_BT_493", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new EnqueueTask(V("CORE_BT_493", "damage", 6), ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, 1))
				}
			}));

			// [CS3_020] Illidari Inquisitor (8) — Rush. After your hero attacks an enemy, this attacks it too.
			cards.Add("CS3_020", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me) || !(g.CurrentEventData?.EventTarget is ICharacter enemy)) return;
						if (me.IsDead || me.ToBeDestroyed || me.Zone?.Type != SabberStoneCore.Enums.Zone.PLAY) return;
						if (enemy.IsDead || enemy.ToBeDestroyed || enemy.Zone?.Type != SabberStoneCore.Enums.Zone.PLAY) return;
						EventMetaData temp = g.CurrentEventData;
						Generic.AttackBlock.Invoke(c, me, enemy, true, true);
						g.CurrentEventData = temp;
					})
				}
			}));

			// [CATA_001] Tichondrius (9) — Your hero is Immune. Battlecry: Your next Demon this turn costs (0).
			cards.Add("CORE_CATA_001", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HERO, new Effect(GameTag.IMMUNE, EffectOperator.SET, 1)),
				PowerTask = new AddEnchantmentTask("CORE_CATA_001e", EntityType.CONTROLLER)
			}));
			cards.Add("CORE_CATA_001e", NextCardEffect(Effects.SetCost(0), SelfCondition.IsRace(Race.DEMON), thisTurnOnly: true));
		}

		// ---------------------------------------------------------------- Death Knight

		private const GameTag COST_BLOOD = (GameTag)2196, COST_FROST = (GameTag)2197, COST_UNHOLY = (GameTag)2198;
		private const GameTag CORPSE_SPENDER = (GameTag)4058;
		private const int FROST = 3;

		/// <summary>Spend exactly <paramref name="n"/> Corpses if the player has them ("Spend N Corpses to ...").</summary>
		/// <summary>"Raise up to N Corpses as X": one token per Corpse, as many as fit on the board.</summary>
		private static void RaiseCorpses(Game g, Controller c, IEntity source, int max, string tokenId)
		{
			int n = Generic.SpendCorpsesUpTo(c, source, System.Math.Min(max, Game.MAX_MINIONS_ON_BOARD - c.BoardZone.Count));
			Generic.BeginSummonGroup(g, source, n);
			for (int i = 0; i < n && !c.BoardZone.IsFull; i++)
				Generic.SummonBlock.Invoke(g, (Minion)Entity.FromCard(c, Cards.FromId(tokenId)), -1, source);
		}

		/// <summary>Discover a Death Knight card of a rune (COST_BLOOD / COST_FROST / COST_UNHOLY).</summary>
		private static ISimpleTask DiscoverRune(GameTag rune) =>
			new DiscoverTask(CardType.INVALID, CardClass.DEATHKNIGHT, (rune, RelaSign.GEQ, 1));

		private static void GiveAll(Game g, IEntity source, IEnumerable<IPlayable> targets, string enchantment)
		{
			foreach (IPlayable p in targets.ToList())
				Generic.AddEnchantmentBlock(g, Cards.FromId(enchantment), (IPlayable)source, p, 0, 0, 0);
		}

		private static readonly Dictionary<PlayReq, int> BattlecryFriendlyUndead = new Dictionary<PlayReq, int>
		{
			{ PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 },
			{ PlayReq.REQ_TARGET_WITH_RACE, (int)Race.UNDEAD }
		};

		private static void DeathKnight(IDictionary<string, CardDef> cards)
		{
			// [HERO_11bp] Ghoul Charge (2) — Hero Power: Summon a 1/1 Ghoul with Charge. It dies at end of turn.
			cards.Add("HERO_11bp", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } },
				new Power { PowerTask = new SummonTask("HERO_11bpt", SummonSide.DEFAULT) }));
			cards.Add("HERO_11bp2", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } },
				new Power { PowerTask = new SummonTask("HERO_11bp2t", SummonSide.DEFAULT) }));
			foreach (string ghoul in new[] { "HERO_11bpt", "HERO_11bp2t" }) // Frail Ghoul: dies at the end of your turn
				cards.Add(ghoul, new CardDef(new Power
				{
					Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new DestroyTask(EntityType.SOURCE) }
				}));

			// [RLK_503] Body Bagger (1) — Battlecry: Gain a Corpse.
			cards.Add("RLK_503", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.GainCorpses(c, s, V("RLK_503", "corpses", 1)))
			}));

			// [RLK_958] Skeletal Sidekick (1) — Battlecry: Give a friendly Undead +2 Attack.
			cards.Add("RLK_958", new CardDef(BattlecryFriendlyUndead, new Power { PowerTask = new AddEnchantmentTask("RLK_958e", EntityType.TARGET) }));
			cards.Add("RLK_958e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("RLK_958e", "attack", 2))) }));

			// [CATA_009] Death's Advance (2) — Freeze a character. Discover a spell.
			cards.Add("CORE_CATA_009", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(ComplexTask.Freeze(EntityType.TARGET), new DiscoverTask(CardType.SPELL))
			}));

			// [RLK_025] Frost Strike (2) — Deal 3 damage to a minion. If it dies, Discover a Frost Rune card.
			cards.Add("RLK_025", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("RLK_025", "damage", 3), EntityType.TARGET, true),
					new CustomTask((g, c, s, t, stack) => stack.Flag = t is Minion m && (m.IsDead || m.ToBeDestroyed)),
					new FlagTask(true, DiscoverRune(COST_FROST)))
			}));

			// [RLK_061] Battlefield Necromancer (2) — At the end of your turn, raise a Corpse as a 1/3 Risen Footman with Taunt.
			cards.Add("RLK_061", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) => RaiseCorpses(g, c, s, V("RLK_061", "corpses", 1), "RLK_061t"))
				}
			}));

			// [RLK_066] Hematurge (2) — Battlecry: Spend a Corpse to Discover a Blood Rune card.
			cards.Add("CORE_RLK_066", new CardDef(new Power
			{
				CorpseCost = V("CORE_RLK_066", "corpses", 1), // spent on play (glows with enough Corpses)
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => stack.Flag = Generic.SpendCorpses(c, s)),
					new FlagTask(true, DiscoverRune(COST_BLOOD)))
			}));

			// [RLK_712] Blood Tap (2) — Give all minions in your hand +1/+1. Spend 2 Corpses to give them +1/+1 more.
			cards.Add("CORE_RLK_712", new CardDef(new Power
			{
				CorpseCost = V("CORE_RLK_712", "corpses", 2), // spent on play (glows with enough Corpses)
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					// in the order of the text (live: HandBuff, ConsumeCorpse, HandBuff)
					var minions = c.HandZone.Where(p => Generic.IsMinionCard(p)).ToList();
					GiveAll(g, s, minions, "RLK_712e");
					if (Generic.SpendCorpses(c, s))
						GiveAll(g, s, minions, "RLK_712e");
				})
			}));
			cards.Add("RLK_712e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("RLK_712e", "stats", 1))) }));

			// [RLK_116] Necrotic Mortician (2) — Battlecry: If a friendly Undead died after your last turn, Discover an Unholy Rune card.
			cards.Add("CORE_RLK_116", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => stack.Flag = c.LastUndeadDeathTurn > 0 && c.LastUndeadDeathTurn >= g.Turn - 1),
					new FlagTask(true, DiscoverRune(COST_UNHOLY)))
			}));

			// [RLK_083] Deathchiller (2) — After you cast a spell, deal 1 damage to two random enemies.
			cards.Add("CORE_RLK_083", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = ComplexTask.DamageRandomTargets(V("CORE_RLK_083", "targets", 2), EntityType.ENEMIES, V("CORE_RLK_083", "damage", 1))
				}
			}));

			// [EDR_002] Poison Breath (2) — Give a friendly Undead Poisonous.
			cards.Add("CORE_EDR_002", new CardDef(new Dictionary<PlayReq, int>
				{
					{ PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 },
					{ PlayReq.REQ_TARGET_WITH_RACE, (int)Race.UNDEAD }
				},
				new Power { PowerTask = new AddEnchantmentTask("CORE_EDR_002e", EntityType.TARGET) }));
			cards.Add("CORE_EDR_002e", new CardDef(new Power { Enchant = new Enchant(GameTag.POISONOUS, EffectOperator.SET, 1) }));

			// [RLK_511] Harbinger of Winter (2) — Deathrattle: Draw a Frost spell.
			cards.Add("RLK_511", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var frost = c.DeckZone.Where(p => p is Spell && p.Card[SPELL_SCHOOL] == FROST).ToList();
					if (frost.Count > 0) Generic.Draw(c, frost[g.Random.Next(frost.Count)]);
				})
			}));

			// [WW_374] Corpse Farm (3) — Spend up to 8 Corpses to summon a random minion of that Cost.
			cards.Add("CORE_WW_374", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card card = RandomMinionOfCost(g, Generic.SpendCorpsesUpTo(c, s, V("CORE_WW_374", "corpses", 8)));
					if (card != null && !c.BoardZone.IsFull)
						Generic.SummonBlock.Invoke(g, (Minion)Entity.FromCard(c, card), -1, s);
				})
			}));

			// [RLK_048] Anti-Magic Shell (3) — Give your minions +1/+1 and Elusive.
			cards.Add("RLK_048", new CardDef(new Power { PowerTask = new AddEnchantmentTask("RLK_048e", EntityType.MINIONS) }));
			cards.Add("RLK_048e", new CardDef(new Power
			{
				Enchant = new Enchant(
					Effects.Attack_N(V("RLK_048e", "stats", 1)), Effects.Health_N(V("RLK_048e", "stats", 1)),
					new Effect(GameTag.CANT_BE_TARGETED_BY_SPELLS, EffectOperator.SET, 1),
					new Effect(GameTag.CANT_BE_TARGETED_BY_HERO_POWERS, EffectOperator.SET, 1))
			}));

			// [RLK_708] Chillfallen Baron (3) — Battlecry and Deathrattle: Draw a card.
			cards.Add("RLK_708", new CardDef(new Power { PowerTask = new DrawTask(), DeathrattleTask = new DrawTask() }));

			// [RLK_087] Asphyxiate (3) — Destroy the highest Attack enemy minion.
			cards.Add("CORE_RLK_087", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_MINIMUM_ENEMY_MINIONS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = c.Opponent.BoardZone.GetAll();
					if (enemies.Length == 0) return;
					int top = enemies.Max(m => m.AttackDamage);
					var highest = enemies.Where(m => m.AttackDamage == top).ToList();
					highest[g.Random.Next(highest.Count)].Destroy();
				})
			}));

			// [RLK_121] Acolyte of Death (3) — After a friendly Undead dies, draw a card.
			cards.Add("CORE_RLK_121", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					Condition = SelfCondition.IsRace(Race.UNDEAD),
					SingleTask = new DrawTask()
				}
			}));

			// [ETC_523] Death Metal Knight (3) — Taunt. Costs Health instead of Mana if your hero was healed this turn.
			cards.Add("CORE_ETC_523", new CardDef(new Power { PowerTask = Nothing, CostsHealthWhile = p => p.Controller.AmountHeroHealedThisTurn > 0 }));

			// [EDR_003] Falric (3) — You gain twice as many Corpses as normal. Battlecry: Draw a card that spends Corpses.
			cards.Add("CORE_EDR_003", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER, new Effect(HsTag.CORPSE_GAINS_DOUBLED, EffectOperator.ADD, 1)),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var spenders = c.DeckZone.Where(p => p.Card[CORPSE_SPENDER] > 0).ToList();
					if (spenders.Count > 0) Generic.Draw(c, spenders[g.Random.Next(spenders.Count)]);
				})
			}));

			// [CATA_007] Consumption (4) — Deal 3 damage to two random enemy minions. Draw a card for each that dies.
			cards.Add("CORE_CATA_007", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed).ToList();
					var hit = new List<Minion>();
					for (int i = 0; i < V("CORE_CATA_007", "targets", 2) && enemies.Count > 0; i++)
					{
						Minion m = enemies[g.Random.Next(enemies.Count)];
						enemies.Remove(m);
						hit.Add(m);
						Generic.DamageCharFunc(s as IPlayable, m, V("CORE_CATA_007", "damage", 3), true);
					}
					int died = hit.Count(m => m.IsDead || m.ToBeDestroyed);
					for (int i = 0; i < died; i++) Generic.Draw(c);
				})
			}));

			// [RLK_024] Death Strike (4) — Lifesteal. Deal 6 damage to a minion.
			cards.Add("RLK_024", new CardDef(TargetMinion, new Power { PowerTask = new DamageTask(V("RLK_024", "damage", 6), EntityType.TARGET, true) }));

			// [RLK_707] Grave Strength (4) — Give your minions +1 Attack. Spend 5 Corpses to give them +3 instead.
			cards.Add("RLK_707", new CardDef(new Power
			{
				CorpseCost = V("RLK_707", "corpses", 5), // spent on play (glows with enough Corpses)
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.BoardZone.GetAll(), Generic.SpendCorpses(c, s) ? "RLK_707e2" : "RLK_707e"))
			}));
			cards.Add("RLK_707e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("RLK_707e", "attack", 1))) }));
			cards.Add("RLK_707e2", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("RLK_707e2", "attack", 3))) }));

			// [RLK_709] Remorseless Winter (4) — Deal 2 damage to all enemies. Draw a card.
			cards.Add("RLK_709", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("RLK_709", "damage", 2), EntityType.ENEMIES, true), new DrawTask())
			}));

			// [RLK_062] Nerubian Swarmguard (4) — Taunt. Battlecry: Summon two copies of this minion.
			cards.Add("CORE_RLK_062", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new SummonCopyTask(EntityType.SOURCE, side: SummonSide.RIGHT),
					new SummonCopyTask(EntityType.SOURCE, side: SummonSide.RIGHT))
			}));

			// [RLK_118] Tomb Guardians (4) — Summon two 2/2 Zombies with Taunt. Spend 4 Corpses to give them Reborn.
			cards.Add("CORE_RLK_118", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				CorpseCost = V("CORE_RLK_118", "corpses", 4), // spent on play (glows with enough Corpses)
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					// in the order of the text: summon, then the spend gives them Reborn (no client enchantment: the tag)
					var zombies = new List<Minion>();
					for (int i = 0; i < V("CORE_RLK_118", "zombies", 2) && !c.BoardZone.IsFull; i++)
					{
						var zombie = (Minion)Entity.FromCard(c, Cards.FromId("RLK_118t3"));
						Generic.SummonBlock.Invoke(g, zombie, -1, s);
						zombies.Add(zombie);
					}
					if (Generic.SpendCorpses(c, s))
						foreach (Minion zombie in zombies.Where(z => z.Zone?.Type == Zone.PLAY))
							zombie[GameTag.REBORN] = 1;
				})
			}));

			// [RLK_745] Malignant Horror (4) — Reborn. At the end of your turn, spend 4 Corpses to summon a copy of this minion.
			cards.Add("CORE_RLK_745", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = ComplexTask.Create(
						new CustomTask((g, c, s, t, stack) => stack.Flag = !c.BoardZone.IsFull && Generic.SpendCorpses(c, s, V("CORE_RLK_745", "corpses", 4))),
						new FlagTask(true, new SummonCopyTask(EntityType.SOURCE, side: SummonSide.RIGHT)))
				}
			}));

			// [RLK_223] Thassarian (4) — Reborn. Battlecry and Deathrattle: Deal 2 damage to a random enemy.
			cards.Add("RLK_223", new CardDef(new Power
			{
				PowerTask = ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, V("RLK_223", "damage", 2)),
				DeathrattleTask = ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, V("RLK_223", "damage", 2))
			}));

			// [RLK_060] Army of the Dead (5) — Raise up to 5 Corpses as 2/2 Risen Ghouls with Rush.
			cards.Add("RLK_060", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => RaiseCorpses(g, c, s, V("RLK_060", "corpses", 5), "RLK_008t"))
			}));

			// [RLK_720] Gnome Muncher (6) — Taunt, Lifesteal. At the end of your turn, attack the lowest Health enemy.
			cards.Add("RLK_720", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me) || me.IsDead || me.ToBeDestroyed || me.AttackDamage <= 0 || me.Untouchable) return;
						var enemies = c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed).Cast<ICharacter>().ToList();
						if (!c.Opponent.Hero.IsDead) enemies.Add(c.Opponent.Hero);
						if (enemies.Count == 0) return;
						int low = enemies.Min(e => e.Health);
						var lowest = enemies.Where(e => e.Health == low).ToList();
						EventMetaData temp = g.CurrentEventData;
						Generic.AttackBlock.Invoke(c, me, lowest[g.Random.Next(lowest.Count)], true, true);
						g.DeathProcessingAndAuraUpdate();
						g.CurrentEventData = temp;
					})
				}
			}));

			// [RLK_505] Marrow Manipulator (6) — Battlecry: Spend up to 5 Corpses. Deal 2 damage to a random enemy for each.
			cards.Add("CORE_RLK_505", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = Generic.SpendCorpsesUpTo(c, s, V("CORE_RLK_505", "corpses", 5));
					if (n > 0)
						new EnqueueTask(n, ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, V("CORE_RLK_505", "damage", 2))).Process(g, c, s, t);
				})
			}));

			// [RLK_706] Alexandros Mograine (7) — Battlecry: For the rest of the game, deal 3 damage to your opponent at the end of your turns.
			cards.Add("CORE_RLK_706", new CardDef(new Power { PowerTask = new AddEnchantmentTask("RLK_706e3", EntityType.CONTROLLER) }));
			cards.Add("RLK_706e3", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new DamageTask(V("CORE_RLK_706", "damage", 3), EntityType.OP_HERO)
				}
			}));

			// [RLK_063] Frostwyrm's Fury (7) — Deal 5 damage. Freeze all enemy minions. Summon a 5/5 Frostwyrm.
			cards.Add("CORE_RLK_063", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("CORE_RLK_063", "damage", 5), EntityType.TARGET, true),
					ComplexTask.Freeze(EntityType.OP_MINIONS),
					new SummonTask("RLK_063t", SummonSide.SPELL))
			}));

			// [RLK_506] Boneguard Commander (8) — Taunt. Battlecry: Raise up to 6 Corpses as 1/3 Risen Footmen with Taunt.
			cards.Add("CORE_RLK_506", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => RaiseCorpses(g, c, s, V("CORE_RLK_506", "corpses", 6), "RLK_061t"))
			}));
		}

		/// <summary>HsServer: the client's generic Cost enchantments (GBL_001e "Cost = 1" … GBL_013e "Cost - 7"), used by any card
		/// whose own data has no enchantment for its cost change. One definition each, from the client's names.</summary>
		private static void GenericCostEnchantments(IDictionary<string, CardDef> cards)
		{
			var costs = new (string Id, char Op, int N)[]
			{
				("GBL_001e", '=', 1), ("GBL_002e", '-', 2), ("GBL_003e", '-', 1), ("GBL_004e", '-', 3), ("GBL_005e", '+', 2),
				("GBL_006e", '=', 2), ("GBL_007e", '=', 10), ("GBL_008e", '-', 4), ("GBL_009e", '=', 0), ("GBL_010e", '=', 3),
				("GBL_011e", '-', 5), ("GBL_012e", '-', 6), ("GBL_013e", '-', 7), ("GBL_016e", '+', 1),
			};
			foreach ((string id, char op, int n) in costs)
				cards.Add(id, new CardDef(new Power
				{
					Enchant = new Enchant(op == '=' ? Effects.SetCost(n) : op == '+' ? Effects.AddCost(n) : Effects.ReduceCost(n))
				}));
		}

		public static void AddAll(Dictionary<string, CardDef> cards)
		{
			GenericCostEnchantments(cards);
			Neutral(cards);
			ClassCards(cards);
			Batch2(cards);
			Batch3(cards);
			DemonHunter(cards);
			DeathKnight(cards);
			CorpseCards(cards); // HsServer: the Corpse spenders that were placeholders
			WildPool(cards);
			Questlines(cards);
			Uldum(cards);
			Dragons(cards);
			Outland(cards); // HsServer: Ashes of Outland (OutlandCardsGenHs.cs)
			Scholomance(cards); // HsServer: Scholomance Academy (ScholomanceCardsGenHs.cs)
			Darkmoon(cards); // HsServer: Madness at the Darkmoon Faire + Year of the Phoenix minisets (DarkmoonCardsGenHs.cs)
			Barrens(cards); // HsServer: Forged in the Barrens + Wailing Caverns (BarrensCardsGenHs.cs)
			Stormwind(cards); // HsServer: United in Stormwind + Deadmines (StormwindCardsGenHs.cs)
			Alterac(cards); // HsServer: Fractured in Alterac Valley + Onyxia's Lair (AlteracCardsGenHs.cs)
			SunkenCity(cards); // HsServer: Voyage to the Sunken City (SunkenCityCardsGenHs.cs)
			LichKing(cards); // HsServer: March of the Lich King + Return to Naxxramas (LichKingCardsGenHs*.cs)
			Festival(cards); // HsServer: Festival of Legends + Audiopocalypse (FestivalCardsGenHs*.cs)
			Sideboards(cards); // HsServer: the deck-sideboard cards of sets not done yet: Zilliax, Beatrix, King (SideboardCardsGenHs.cs)
			Titans(cards); // HsServer: TITANS
			WildWest(cards); // HsServer: Showdown in the Badlands + Delve into Deepholm (WildWestCardsGenHs*.cs)
			Wonders(cards); // HsServer: Caverns of Time (WondersCardsGenHs*.cs)
			Island(cards); // HsServer: Perils in Paradise + Traveling Travel Agency (IslandCardsGenHs*.cs)
			Space(cards); // HsServer: The Great Dark Beyond + Heroes of StarCraft (SpaceCardsGenHs*.cs)
			PlayableHeroCards(cards); // HsServer: Hero cards from finished sets the pools used to leave out (HeroCardsGenHs.cs)
			CoreRotations(cards);
			Nathria(cards);
			NathriaClasses(cards);
			EmeraldDream(cards);
			LostCity(cards); // HsServer: The Lost City of Un'Goro (LostCityCardsGenHs*.cs)
			TimeTravel(cards); Cataclysm(cards); VioletHold(cards); Whizbang(cards); Vanilla(cards); PathOfArthas(cards); Events(cards); Be(cards); // HsServer: Across the Timeways (TimeTravelCardsGenHs*.cs)
			Locations(cards);
			BadlandsTitans(cards);
			Legacy(cards); // HsServer: the Legacy set's cards that had no script (LEG_ reprints share them)
			DalaranHeist(cards); // HsServer: The Dalaran Heist's own cards (DalaranHeistCardsGenHs*.cs)
			TokenPass(cards); // HsServer: tokens scripted cards create that had no script (TokenPassCardsGenHs.cs; `HsServer cards --tokens`)
			IcecrownAdventure(cards); // HsServer: The Frozen Throne adventure's cards seen live (IcecrownAdventureCardsGenHs.cs)
		}
	}
}
