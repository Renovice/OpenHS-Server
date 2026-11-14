// HsServer addition (not upstream SabberStone): Fractured in Alterac Valley (2021) and its Onyxia's Lair miniset (ONY_).
// Every number goes through CardValues (V).
// Shared pieces this set uses: Honorable Kill (Power.HonorableKillTask, Character.TakeDamage: exact lethal damage on its
// owner's turn; the task's target is the killed character), Objectives "Lasts 3 turns" (PaladinAura: they count down at the
// end of your turns), "Frozen until …" (HsTag.STAYS_FROZEN), "Repeatable this turn" (Card.Repeatable).
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
		private static bool IsFrostSpell(IPlayable p) => p is Spell && p[SPELL_SCHOOL] == FROST;
		private static bool IsFrostCard(Card card) => card != null && card.Type == CardType.SPELL && card[SPELL_SCHOOL] == FROST;
		private static bool IsDemon(IPlayable p) => IsRaceCard(p, Race.DEMON);

		/// <summary>Freezes the characters (a Frozen character stays Frozen).</summary>
		private static void FreezeAll(IEnumerable<ICharacter> chars)
		{
			foreach (ICharacter ch in chars.ToList())
				if (ch != null && !ch.IsDead) ch.IsFrozen = true;
		}

		/// <summary>Summons a random minion of the deck matching <paramref name="where"/> (a Recruit: it leaves the deck); the
		/// summoned minion, or null.</summary>
		private static Minion SummonFromDeck(Game g, Controller c, IEntity source, System.Func<IPlayable, bool> where)
		{
			if (c.BoardZone.IsFull) return null;
			var pool = c.DeckZone.Where(p => Generic.IsMinionCard(p) && where(p)).ToList();
			if (pool.Count == 0) return null;
			var m = (Minion)pool[g.Random.Next(pool.Count)];
			Generic.RemoveFromZone.Invoke(c, m);
			Generic.SummonBlock.Invoke(g, m, -1, source);
			return m;
		}

		/// <summary>The cards' costs as the card was played ("If this costs more / less than every minion in your deck").</summary>
		private static int CostWhenPlayed(IEntity s) => s is IPlayable p ? PlayedCost(p) : 0;

		/// <summary>"Your next Hero Power costs (0)" on the player (Sneaky Scout's AV_123e, Amplified Snowflurry's AV_115e5).</summary>
		private static Power NextHeroPowerFree(Trigger also = null)
		{
			var used = new Trigger(TriggerType.INSPIRE) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = RemoveEnchantmentTask.Task };
			return new Power
			{
				Aura = new Aura(AuraType.HEROPOWER, Effects.SetCost(0)),
				Trigger = also == null ? used : new MultiTrigger(also, used)
			};
		}

		/// <summary>"Your next … costs (N) less / more" on a player: the cost shows on each matching card as
		/// <paramref name="shownOnCards"/>; the enchantment leaves when such a card is played.</summary>
		private static CardDef NextCardCost(IEffect cost, SelfCondition which, string shownOnCards, bool opponent = false) => new CardDef(new Power
		{
			Aura = new Aura(opponent ? AuraType.OP_HAND : AuraType.HAND, cost) { Condition = which, ShowAs = shownOnCards },
			Trigger = new Trigger(TriggerType.PLAY_CARD)
			{
				TriggerSource = opponent ? TriggerSource.ENEMY : TriggerSource.FRIENDLY,
				Condition = which,
				SingleTask = RemoveEnchantmentTask.Task
			}
		});

		private static void Alterac(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Neutral
			// [AV_100] Drek'Thar (4) — Battlecry: If this costs more than every minion in your deck, summon one of those minions.
			cards.Add("AV_100", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int cost = CostWhenPlayed(s);
					if (c.DeckZone.Where(p => Generic.IsMinionCard(p)).All(p => p.Cost < cost))
						SummonFromDeck(g, c, s, p => true);
				})
			}));
			// [AV_101] Herald of Lokholar (4) — Battlecry: Draw a Frost spell.
			cards.Add("AV_101", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, IsFrostSpell)) }));
			// [AV_102] Popsicooler (3) — Deathrattle: Freeze two random enemy minions.
			cards.Add("AV_102", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
					FreezeAll(PickMinions(g, c.Opponent.BoardZone.Where(m => !m.IsDead && !m.ToBeDestroyed), V("AV_102", "minions", 2))))
			}));
			// [AV_112] Snowblind Harpy (3) — Battlecry: If you're holding a Frost spell, gain 5 Armor.
			cards.Add("AV_112", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.HandZone.Any(IsFrostSpell)) c.Hero.GainArmor((IPlayable)s, V("AV_112", "armor", 5));
				})
			}));
			// [AV_121] Gnome Private (1) — Honorable Kill: Gain +2 Attack.
			cards.Add("AV_121", new CardDef(new Power { HonorableKillTask = new AddEnchantmentTask("AV_121e", EntityType.SOURCE) }));
			cards.Add("AV_121e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("AV_121", "attack", 2))) }));
			// [AV_122] Corporal (2) — Honorable Kill: Give your other minions Divine Shield.
			cards.Add("AV_122", new CardDef(new Power
			{
				HonorableKillTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.Where(m => m.Id != s.Id && !m.IsDead).ToList()) m.HasDivineShield = true;
				})
			}));
			// [AV_123] Sneaky Scout (2) — Stealth. Honorable Kill: Your next Hero Power costs (0).
			cards.Add("AV_123", new CardDef(new Power { HonorableKillTask = new AddEnchantmentTask("AV_123e", EntityType.CONTROLLER) }));
			cards.Add("AV_123e", new CardDef(NextHeroPowerFree()));
			// [AV_124] Direwolf Commander (3) — Honorable Kill: Summon a 2/2 Wolf with Stealth.
			cards.Add("AV_124", new CardDef(new Power { HonorableKillTask = new SummonTask("AV_211t", SummonSide.RIGHT) }));
			// [AV_125] Tower Sergeant (4) — Battlecry: If you control at least 2 other minions, gain +2/+2.
			cards.Add("AV_125", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.BoardZone.Count(m => m.Id != s.Id) >= V("AV_125", "minions", 2)) Give(g, s, (IPlayable)s, "AV_125e");
				})
			}));
			cards.Add("AV_125e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("AV_125", "stats", 2))) }));
			// [AV_126] Bunker Sergeant (3) — Battlecry: If your opponent has 2 or more minions, deal 1 damage to all enemy minions.
			cards.Add("AV_126", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.Opponent.BoardZone.Count < V("AV_126", "minions", 2)) return;
					foreach (Minion m in c.Opponent.BoardZone.ToList()) Generic.DamageCharFunc((IPlayable)s, m, V("AV_126", "damage", 1), false);
				})
			}));
			// [AV_127] Ice Revenant (4) — Whenever you cast a Frost spell, gain +2/+2.
			cards.Add("AV_127", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.CAST_SPELL)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(IsFrostSpell),
					SingleTask = new AddEnchantmentTask("AV_127e", EntityType.SOURCE)
				}
			}));
			cards.Add("AV_127e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("AV_127", "stats", 2))) }));
			// [AV_128] Frozen Mammoth (4) — This is Frozen until you cast a Fire spell. (It enters play Frozen under its invisible
			// AV_128e; the end-of-turn thaw skips it (HsTag.STAYS_FROZEN); your next Fire spell removes it and unfreezes it.)
			cards.Add("AV_128", new CardDef(new Power
			{
				// when it lands on the board, however it got there (played, summoned): the zone trigger of itself
				Trigger = new Trigger(TriggerType.ZONE)
				{
					TriggerSource = TriggerSource.SELF,
					Condition = new SelfCondition(p => p.Zone?.Type == Zone.PLAY && p.AppliedEnchantments?.Any(e => e.Card.Id == "AV_128e") != true),
					SingleTask = new AddEnchantmentTask("AV_128e", EntityType.SOURCE)
				}
			}));
			cards.Add("AV_128e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.FROZEN, EffectOperator.SET, 1), new Effect(HsTag.STAYS_FROZEN, EffectOperator.SET, 1)),
				Trigger = new Trigger(TriggerType.CAST_SPELL)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Spell && p[SPELL_SCHOOL] == FIRE),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || !(e.Target is Minion m)) return;
						e.Remove();
						m[HsTag.STAYS_FROZEN] = 0;
						m.IsFrozen = false;
					})
				}
			}));
			// [AV_129] Blood Guard (5) — Whenever this minion takes damage, give your minions +1 Attack.
			cards.Add("AV_129", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.Where(m => !m.IsDead), "AV_129e"))
				}
			}));
			cards.Add("AV_129e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("AV_129", "attack", 1))) }));
			// [AV_130] Legionnaire (6) — Deathrattle: Give all minions in your hand +2/+2.
			cards.Add("AV_130", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.Where(p => Generic.IsMinionCard(p)), "AV_130e"))
			}));
			cards.Add("AV_130e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("AV_130", "stats", 2))) }));
			// [AV_131] Knight-Captain (5) — Battlecry: Deal 3 damage. Honorable Kill: Gain +3/+3.
			cards.Add("AV_131", new CardDef(BattlecryAny, new Power
			{
				PowerTask = new DamageTask(V("AV_131", "damage", 3), EntityType.TARGET),
				HonorableKillTask = new AddEnchantmentTask("AV_131e", EntityType.SOURCE)
			}));
			cards.Add("AV_131e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("AV_131", "stats", 3))) }));
			// [AV_132] Troll Centurion (8) — Rush. Honorable Kill: Deal 8 damage to the enemy hero.
			cards.Add("AV_132", new CardDef(new Power { HonorableKillTask = new DamageTask(V("AV_132", "damage", 8), EntityType.OP_HERO) }));
			// [AV_133] Icehoof Protector (6) — Taunt. Freeze any character damaged by this minion. (The client card has no FREEZE
			// keyword, a trigger: it freezes what it damaged.)
			cards.Add("AV_133", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEAL_DAMAGE)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventTarget is ICharacter hit && !hit.IsDead) hit.IsFrozen = true;
					})
				}
			}));
			// [AV_134] Frostwolf Warmaster (4) — Costs (1) less for each card you've played this turn.
			cards.Add("AV_134", new CardDef(new Power { Aura = new AdaptiveCostEffect(p => p.Controller.NumCardsPlayedThisTurn) }));
			// [AV_135] Stormpike Marshal (4) — Taunt. If you took 5 or more damage on your opponent's turn, this costs (1).
			// (Your hero's damage during the turn that just ended: live DAMAGE_DEALT_TO_HERO_LAST_TURN.)
			cards.Add("AV_135", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => V("AV_135", "cost", 1), EffectOperator.SET, new SelfCondition(p =>
					p.Game.CurrentPlayer == p.Controller && p.Controller[HsTag.DAMAGE_DEALT_TO_HERO_LAST_TURN] >= V("AV_135", "damage", 5)))
			}));
			// [AV_136] Kobold Taskmaster (3) — Battlecry: Add 2 Armor Scraps to your hand that give +2 Health to a minion.
			cards.Add("AV_136", new CardDef(new Power { PowerTask = new AddCardTo("AV_136t", EntityType.HAND, V("AV_136", "scraps", 2)) }));
			cards.Add("AV_136t", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("AV_136e", EntityType.TARGET) }));
			cards.Add("AV_136e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("AV_136t", "health", 2))) }));
			// [AV_137] Irondeep Trogg (1) — After your opponent casts a spell, summon another Irondeep Trogg.
			cards.Add("AV_137", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST) { TriggerSource = TriggerSource.ENEMY, SingleTask = new SummonTask("AV_137", SummonSide.RIGHT) }
			}));
			// [AV_138] Grimtotem Bounty Hunter (3) — Battlecry: Destroy an enemy Legendary minion.
			cards.Add("AV_138", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 }, { PlayReq.REQ_LEGENDARY_TARGET, 0 } },
				new Power { PowerTask = new DestroyTask(EntityType.TARGET) }));
			// [AV_139] Abominable Lieutenant (8) — At the end of your turn, eat a random enemy minion and gain its stats.
			cards.Add("AV_139", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var prey = c.Opponent.BoardZone.Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
						if (prey.Count == 0 || !(s is Minion me)) return;
						Minion eaten = prey[g.Random.Next(prey.Count)];
						int atk = eaten.AttackDamage, hp = eaten.Health;
						Generic.Destroy(eaten, s);
						Generic.AddEnchantmentBlock(g, Cards.FromId("AV_139e"), me, me, atk, hp, 0);
					})
				}
			}));
			cards.Add("AV_139e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0), Effects.Health_N(0)) { UseScriptTag = true } }));
			// [AV_141t] Lokholar the Ice Lord (10) — Rush, Windfury. Costs (5) less if you have 15 Health or less.
			cards.Add("AV_141t", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.Hero.Health <= V("AV_141t", "health", 15) ? V("AV_141t", "discount", 5) : 0)
			}));
			// [AV_142t] Ivus, the Forest Lord (1) — Battlecry: Spend the rest of your Mana and gain +2/+2, Rush, Divine Shield, or
			// Taunt at random for each.
			cards.Add("AV_142t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int mana = c.RemainingMana;
					if (mana <= 0 || !(s is Minion me)) return;
					c.UsedMana += mana;
					string[] gifts = { "AV_142e", "AV_142e2", "AV_142e3", "AV_142e4" };
					for (int i = 0; i < mana; i++) Give(g, s, me, gifts[g.Random.Next(gifts.Length)]);
				})
			}));
			cards.Add("AV_142e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("AV_142t", "stats", 2))) }));
			cards.Add("AV_142e2", new CardDef(new Power { Enchant = new Enchant(Effects.Rush) }));
			cards.Add("AV_142e3", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.DIVINE_SHIELD, EffectOperator.SET, 1)) }));
			cards.Add("AV_142e4", new CardDef(new Power { Enchant = new Enchant(Effects.TauntEff) }));
			// [AV_143] Korrak the Bloodrager (4) — Deathrattle: If this wasn't Honorably Killed, resummon Korrak.
			cards.Add("AV_143", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (s[HsTag.HONORABLY_KILLED] == 0 && s is Minion me) Summon(g, c, me.Card, s, me.LastBoardPosition);
				})
			}));
			// [AV_215] Frantic Hippogryph (5) — Rush. Honorable Kill: Gain Windfury.
			cards.Add("AV_215", new CardDef(new Power { HonorableKillTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion m) m.HasWindfury = true; }) }));
			// [AV_219] Ram Commander (2) — Battlecry: Add two 1/1 Rams with Rush to your hand.
			cards.Add("AV_219", new CardDef(new Power { PowerTask = new AddCardTo("AV_219t", EntityType.HAND, V("AV_219", "rams", 2)) }));
			// [AV_222] Spammy Arcanist (5) — Battlecry: Deal 1 damage to all other minions. If any die, repeat this.
			cards.Add("AV_222", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int round = 0; round < 30; round++)
					{
						var hit = c.BoardZone.Concat(c.Opponent.BoardZone).Where(m => m.Id != s.Id && !m.IsDead && !m.ToBeDestroyed).ToList();
						if (hit.Count == 0) return;
						foreach (Minion m in hit) Generic.DamageCharFunc((IPlayable)s, m, V("AV_222", "damage", 1), false);
						bool died = hit.Any(m => m.IsDead || m.ToBeDestroyed);
						g.DeathProcessingAndAuraUpdate();
						if (!died) return;
					}
				})
			}));
			// [AV_223] Vanndar Stormpike (4) — Battlecry: If this costs less than every minion in your deck, reduce their Cost by (3).
			cards.Add("AV_223", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int cost = CostWhenPlayed(s);
					var minions = c.DeckZone.Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.All(p => p.Cost > cost)) GiveAll(g, s, minions, "AV_223e");
				})
			}));
			cards.Add("AV_223e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("AV_223", "discount", 3))) }));
			// [AV_238] Gankster (2) — Stealth. After your opponent plays a minion, attack it.
			cards.Add("AV_238", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventSource is Minion played && played.Zone?.Type == Zone.PLAY && !played.IsDead && s is Minion me && !me.IsDead)
							EffectAttack(g, me, played);
					})
				}
			}));
			// [AV_256] Reflecto Engineer (3) — Battlecry: Swap the Attack and Health of all minions in both players' hands.
			cards.Add("AV_256", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in c.HandZone.Concat(c.Opponent.HandZone).Where(p => Generic.IsMinionCard(p)).ToList())
					{
						var m = (Minion)p;
						Generic.AddEnchantmentBlock(g, Cards.FromId("AV_256e"), (IPlayable)s, m, m.Health, m.AttackDamage, 0);
					}
				})
			}));
			cards.Add("AV_256e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttack(0), Effects.SetMaxHealth(0)) { UseScriptTag = true } }));
			// [AV_309] Piggyback Imp (3) — Deathrattle: Summon a 4/1 Imp.
			cards.Add("AV_309", new CardDef(new Power { DeathrattleTask = new SummonTask("AV_309t", SummonSide.DEATHRATTLE) }));
			// [AV_401] Stormpike Quartermaster (2) — After you cast a spell, give a random minion in your hand +1/+1.
			cards.Add("AV_401", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = BuffRandomInHand(p => p is Minion, "AV_401e") }
			}));
			cards.Add("AV_401e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("AV_401", "stats", 1))) }));
			// [AV_704] Humongous Owl (7) — Deathrattle: Deal 8 damage to a random enemy.
			cards.Add("AV_704", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = new List<ICharacter> { c.Opponent.Hero };
					enemies.AddRange(c.Opponent.BoardZone.Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable));
					enemies.RemoveAll(e => e == null || e.IsDead);
					if (enemies.Count > 0) Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], V("AV_704", "damage", 8), false);
				})
			}));

			// ---------------------------------------------------------------- Onyxia's Lair (neutral)
			// [ONY_001] Onyxian Warder (5) — Battlecry: If you're holding a Dragon, summon two 2/1 Whelps with Rush.
			cards.Add("ONY_001", new CardDef(new Power { PowerTask = IfHoldingDragon(ComplexTask.Repeat(new SummonTask("ONY_001t", SummonSide.SPELL), V("ONY_001", "whelps", 2))) }));
			// [ONY_002] Gear Grubber (4) — Taunt. If you end your turn with any unspent Mana, reduce this card's Cost by (1).
			cards.Add("ONY_002", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					TriggerActivation = TriggerActivation.HAND,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (c.RemainingMana > 0) Give(g, s, (IPlayable)s, "ONY_002e"); })
				}
			}));
			cards.Add("ONY_002e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("ONY_002", "discount", 1))) }));
			// [ONY_003] Whelp Bonker (3) — Frenzy and Honorable Kill: Draw a card.
			cards.Add("ONY_003", new CardDef(new Power { Trigger = Frenzy(new DrawTask()), HonorableKillTask = new DrawTask() }));
			// [ONY_004] Raid Boss Onyxia (10) — Rush. Immune while you control a Whelp. Battlecry: Summon six 2/1 Whelps with Rush.
			cards.Add("ONY_004", new CardDef(new Power
			{
				PowerTask = ComplexTask.Repeat(new SummonTask("ONY_001t", SummonSide.ALTERNATE), V("ONY_004", "whelps", 6)),
				Aura = new Aura(AuraType.SELF, Effects.Immune)
				{
					Condition = new SelfCondition(p => p.Controller.BoardZone.Any(m => m.Card[Generic.WHELP] > 0)), // (the client's WHELP tag, not the name)
					Restless = true
				}
			}));

			// ---------------------------------------------------------------- Demon Hunter
			// [AV_118] Battleworn Vanguard (2) — After your hero attacks, summon two 1/1 Felwings.
			cards.Add("AV_118", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = ComplexTask.Repeat(new SummonTask("BT_922t", SummonSide.RIGHT), V("AV_118", "felwings", 2))
				}
			}));
			// [AV_209] Dreadprison Glaive (1) — Honorable Kill: Deal damage equal to your hero's Attack to the enemy hero.
			cards.Add("AV_209", new CardDef(new Power
			{
				HonorableKillTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.Hero.AttackDamage > 0) Generic.DamageCharFunc((IPlayable)s, c.Opponent.Hero, c.Hero.AttackDamage, false);
				})
			}));
			// [AV_261] Flag Runner (3) — Whenever a friendly minion dies, gain +1 Attack.
			cards.Add("AV_261", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEATH) { TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF, SingleTask = new AddEnchantmentTask("AV_261e", EntityType.SOURCE) }
			}));
			cards.Add("AV_261e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("AV_261", "attack", 1))) }));
			// [AV_262] Warden of Chains (4) — Taunt. Battlecry: If you're holding a Demon that costs (5) or more, gain +1/+2.
			cards.Add("AV_262", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.HandZone.Any(p => IsDemon(p) && p.Cost >= V("AV_262", "cost", 5))) Give(g, s, (IPlayable)s, "AV_262e2");
				})
			}));
			cards.Add("AV_262e2", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("AV_262", "attack", 1)), Effects.Health_N(V("AV_262", "health", 2))) }));
			// [AV_264] Sigil of Reckoning (5) — At the start of your next turn, summon a random Demon from your hand.
			cards.Add("AV_264", Sigil("AV_264", new CustomTask((g, c, s, t, stack) =>
			{
				var demons = c.HandZone.Where(p => Generic.IsMinionCard(p) && IsDemon(p)).Cast<Minion>().ToList();
				if (demons.Count > 0) Generic.SummonFromHand(c, demons[g.Random.Next(demons.Count)], s);
			})));
			// [AV_265] Ur'zul Giant (13) — Costs (1) less for each friendly minion that died this game.
			cards.Add("AV_265", new CardDef(new Power { Aura = new AdaptiveCostEffect(p => p.Controller.DiedThisGameCards.Count(x => x is Minion)) }));
			// [AV_267] Caria Felsoul (6) — Battlecry: Transform into a 6/6 copy of a Demon in your deck.
			cards.Add("AV_267", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var demons = c.DeckZone.Where(p => Generic.IsMinionCard(p) && IsDemon(p)).ToList();
					if (demons.Count == 0 || !(s is Minion me) || me.Zone?.Type != Zone.PLAY) return;
					IPlayable now = Generic.ChangeEntityBlock(c, me, demons[g.Random.Next(demons.Count)].Card, true);
					if (now != null) Give(g, s, now, "AV_267e2");
				})
			}));
			cards.Add("AV_267e2", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("AV_267", "stats", 6))) }));
			// [AV_269] Flanking Maneuver (4) — Summon a 4/2 Demon with Rush. If it dies this turn, summon another.
			cards.Add("AV_269", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.BoardZone.IsFull) return;
					var satyr = (Minion)Entity.FromCard(c, Cards.FromId("AV_269t"));
					Generic.SummonBlock.Invoke(g, satyr, -1, s);
					if (satyr.Zone?.Type == Zone.PLAY) Give(g, s, satyr, "AV_269e");
				})
			}));
			cards.Add("AV_269e", new CardDef(new Power
			{
				Enchant = new Enchant { IsOneTurnEffect = true },
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					// an enchantment's Deathrattle: source = the dead minion, target = the enchantment
					if (t is Enchantment && s is Minion dead) Summon(g, c, Cards.FromId("AV_269t"), s, dead.LastBoardPosition);
				})
			}));
			// [AV_661] Field of Strife (2) — Your minions have +1 Attack. Lasts 3 turns.
			cards.Add("AV_661", new CardDef(PaladinAura(new Power
			{
				Aura = new Aura(AuraType.BOARD, Effects.Attack_N(V("AV_661", "attack", 1))) { ShowAs = "AV_661e2" }
			})));
			// [ONY_014] Keen Reflex (2) — Deal $1 damage to all minions. Honorable Kill: Gain +1 Attack this turn.
			cards.Add("ONY_014", new CardDef(new Power
			{
				PowerTask = new DamageTask(V("ONY_014", "damage", 1), EntityType.ALLMINIONS, true),
				HonorableKillTask = new AddEnchantmentTask("ONY_014e", EntityType.HERO)
			}));
			cards.Add("ONY_014e", OneTurn(Effects.Attack_N(V("ONY_014", "attack", 1))));
			// [ONY_016] Wings of Hate (1) — Summon two / three / four 1/1 Felwings (ranks: Generic.RankUp).
			foreach ((string id, int n) in new[] { ("ONY_016", 2), ("ONY_016t", 3), ("ONY_016t2", 4) })
				cards.Add(id, new CardDef(new Power { PowerTask = ComplexTask.Repeat(new SummonTask("BT_922t", SummonSide.SPELL), V(id, "felwings", n)) }));
			// [ONY_036] Razorglaive Sentinel (4) — After you play the left or right-most card in your hand, draw a card.
			cards.Add("ONY_036", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.Controller.OutcastPlayId == p.Id),
					SingleTask = new DrawTask()
				}
			}));

			// ---------------------------------------------------------------- Druid
			// [AV_211] Dire Frostwolf (4) — Stealth. Deathrattle: Summon a 2/2 Wolf with Stealth.
			cards.Add("AV_211", new CardDef(new Power { DeathrattleTask = new SummonTask("AV_211t", SummonSide.DEATHRATTLE) }));
			// [AV_291] Frostsaber Matriarch (7) — Taunt. Costs (1) less for each Beast you've summoned this game.
			cards.Add("AV_291", new CardDef(new Power { Aura = new AdaptiveCostEffect(p => p.Controller.SummonedThisGame.Count(x => x.IsRace(Race.BEAST))) }));
			// [AV_292] Heart of the Wild (3) — Give a minion +2/+2, then give your Beasts +1/+1.
			cards.Add("AV_292", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m) Give(g, s, m, "AV_292e");
					GiveAll(g, s, c.BoardZone.Where(x => x.IsRace(Race.BEAST) && !x.IsDead), "AV_292e2");
				})
			}));
			cards.Add("AV_292e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("AV_292", "stats", 2))) }));
			cards.Add("AV_292e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("AV_292", "beasts", 1))) }));
			// [AV_293] Wing Commander Mulverick (4) — Rush. Your minions have "Honorable Kill: Summon a 2/2 Wyvern with Rush."
			cards.Add("AV_293", new CardDef(new Power { Aura = new Aura(AuraType.BOARD, "AV_293e") }));
			cards.Add("AV_293e", new CardDef(new Power { Enchant = new Enchant(), HonorableKillTask = new SummonTask("AV_293t", SummonSide.RIGHT) }));
			// [AV_294] Clawfury Adept (2) — Battlecry: Give all other friendly characters +1 Attack this turn.
			cards.Add("AV_294", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var all = new List<IPlayable> { c.Hero };
					all.AddRange(c.BoardZone.Where(m => m.Id != s.Id && !m.IsDead));
					GiveAll(g, s, all, "AV_294e");
				})
			}));
			cards.Add("AV_294e", OneTurn(Effects.Attack_N(V("AV_294", "attack", 1))));
			// [AV_295] Capture Coldtooth Mine (2) — Choose One - Draw your lowest Cost card; or Draw your highest Cost card.
			cards.Add("AV_295a", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => DrawByCost(g, c, lowest: true)) }));
			cards.Add("AV_295b", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => DrawByCost(g, c, lowest: false)) }));
			// [AV_296] Pride Seeker (3) — Battlecry: Your next Choose One card costs (2) less.
			cards.Add("AV_296", new CardDef(new Power { PowerTask = new AddEnchantmentTask("AV_296e", EntityType.CONTROLLER) }));
			cards.Add("AV_296e", NextCardCost(Effects.ReduceCost(V("AV_296", "discount", 2)), new SelfCondition(p => p.Card.ChooseOne), "AV_296e2"));
			// [AV_360] Frostwolf Kennels (3) — At the end of your turn, summon a 2/2 Wolf with Stealth. Lasts 3 turns.
			cards.Add("AV_360", new CardDef(PaladinAura(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new SummonTask("AV_211t", SummonSide.SPELL) }
			})));
			// [ONY_021] Scale of Onyxia (7) — Fill your board with 2/1 Whelps with Rush.
			cards.Add("ONY_021", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					while (!c.BoardZone.IsFull)
					{
						int before = c.BoardZone.Count;
						Summon(g, c, Cards.FromId("ONY_001t"), s);
						if (c.BoardZone.Count == before) break;
					}
				})
			}));

			// ---------------------------------------------------------------- Hunter
			// [AV_147] Dun Baldar Bunker (2) — At the end of your turn, draw a Secret and set its Cost to (1). Lasts 3 turns.
			cards.Add("AV_147", new CardDef(PaladinAura(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (DrawOne(g, c, p => p is Spell sp && sp.IsSecret) is IPlayable secret) Give(g, s, secret, "AV_147e");
					})
				}
			})));
			cards.Add("AV_147e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(V("AV_147", "cost", 1))) }));
			// [AV_224] Spring the Trap (4) — Deal $3 damage to a minion and cast a Secret from your deck. Honorable Kill: Cast 2.
			cards.Add("AV_224", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("AV_224", "damage", 3), EntityType.TARGET, true), CastSecretFromDeck),
				HonorableKillTask = CastSecretFromDeck
			}));
			// [AV_244] Bloodseeker (2) — Honorable Kill: Gain +1/+1.
			cards.Add("AV_244", new CardDef(new Power { HonorableKillTask = new AddEnchantmentTask("AV_244e", EntityType.SOURCE) }));
			cards.Add("AV_244e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("AV_244", "attack", 1)), new Effect(GameTag.DURABILITY, EffectOperator.ADD, V("AV_244", "durability", 1))) }));
			// [AV_333] Revive Pet (3) — Discover a friendly Beast that died this game. Summon it.
			cards.Add("AV_333", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var beasts = c.DiedThisGameCards.Where(p => p is Minion m && m.IsRace(Race.BEAST)).Select(p => p.Card).ToList();
					new DiscoverFromTask((g2, c2, s2) => beasts, null, null, ChoiceAction.SUMMON).Process(g, c, s, null);
				})
			}));
			// [AV_334] Stormpike Battle Ram (4) — Rush. Deathrattle: Your next Beast costs (2) less.
			cards.Add("AV_334", new CardDef(new Power { DeathrattleTask = new AddEnchantmentTask("AV_334e", EntityType.CONTROLLER) }));
			cards.Add("AV_334e", NextCardCost(Effects.ReduceCost(V("AV_334", "discount", 2)), new SelfCondition(p => IsRaceCard(p, Race.BEAST)), "AV_334e2"));
			// [AV_335] Ram Tamer (3) — Battlecry: If you control a Secret, gain +1/+1 and Stealth.
			cards.Add("AV_335", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (c.SecretZone.Any(x => x.IsSecret)) Give(g, s, (IPlayable)s, "AV_335e"); })
			}));
			cards.Add("AV_335e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("AV_335", "stats", 1)), Effects.Health_N(V("AV_335", "stats", 1)), Effects.StealthEff) }));
			// [ONY_008] Furious Howl (2) — Draw a card. Repeat until you have at least 3 cards.
			cards.Add("ONY_008", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < 12; i++)
					{
						if (c.DeckZone.IsEmpty && i > 0) return;
						Generic.Draw(c);
						if (c.HandZone.Count >= V("ONY_008", "cards", 3) || c.Hero.IsDead) return;
					}
				})
			}));
			// [ONY_009] Pet Collector (5) — Battlecry: Summon a Beast from your deck that costs (5) or less.
			cards.Add("ONY_009", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => SummonFromDeck(g, c, s, p => IsRaceCard(p, Race.BEAST) && p.Cost <= V("ONY_009", "cost", 5)))
			}));
			// [ONY_010] Dragonbane Shot (2) — Deal $2 damage. Honorable Kill: Add a Dragonbane Shot to your hand.
			cards.Add("ONY_010", new CardDef(TargetAny, new Power
			{
				PowerTask = new DamageTask(V("ONY_010", "damage", 2), EntityType.TARGET, true),
				HonorableKillTask = new AddCardTo("ONY_010", EntityType.HAND)
			}));

			// ---------------------------------------------------------------- Mage
			// [AV_114] Shivering Sorceress (1) — Battlecry: Reduce the Cost of the highest Cost spell in your hand by (1).
			cards.Add("AV_114", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var spells = c.HandZone.Where(p => p is Spell).ToList();
					if (spells.Count == 0) return;
					int top = spells.Max(p => p.Cost);
					var highest = spells.Where(p => p.Cost == top).ToList();
					Give(g, s, highest[g.Random.Next(highest.Count)], "AV_114e");
				})
			}));
			cards.Add("AV_114e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("AV_114", "discount", 1))) }));
			// [AV_115] Amplified Snowflurry (2) — Battlecry: Your next Hero Power costs (0) and Freezes the target.
			cards.Add("AV_115", new CardDef(new Power { PowerTask = new AddEnchantmentTask("AV_115e5", EntityType.CONTROLLER) }));
			cards.Add("AV_115e5", new CardDef(NextHeroPowerFree(new Trigger(TriggerType.TARGET)
			{
				TriggerSource = TriggerSource.HERO_POWER,
				SingleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (g.CurrentEventData?.EventTarget is ICharacter target && !target.IsDead) target.IsFrozen = true;
				})
			})));
			// [AV_116] Arcane Brilliance (4) — Add a copy of a 7, 8, 9, and 10-Cost spell in your deck to your hand.
			cards.Add("AV_116", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int cost = V("AV_116", "from", 7); cost <= V("AV_116", "to", 10); cost++)
					{
						int n = cost;
						var spells = c.DeckZone.Where(p => p is Spell && p.Cost == n).ToList();
						if (spells.Count > 0 && !c.HandZone.IsFull) Generic.Copy(c, s, spells[g.Random.Next(spells.Count)], Zone.HAND);
					}
				})
			}));
			// [AV_212] Siphon Mana (2) — Deal $2 damage. Honorable Kill: Reduce the Cost of spells in your hand by (1).
			cards.Add("AV_212", new CardDef(TargetAny, new Power
			{
				PowerTask = new DamageTask(V("AV_212", "damage", 2), EntityType.TARGET, true),
				HonorableKillTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.Where(p => p is Spell), "AV_212e"))
			}));
			cards.Add("AV_212e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("AV_212", "discount", 1))) }));
			// [AV_218] Mass Polymorph (7) — Transform all minions into 1/1 Sheep.
			cards.Add("AV_218", new CardDef(new Power { PowerTask = new TransformTask("AV_218t", EntityType.ALLMINIONS) }));
			// [AV_282] Build a Snowman (3) — Summon a 3/3 Snowman that Freezes. Add "Build a Snowbrute" to your hand. (Then a
			// 6/6 Snowbrute and "Build a Snowgre", then a 9/9 Snowgre.)
			foreach ((string build, string man, string next) in new[] { ("AV_282", "AV_282t", "AV_282t2"), ("AV_282t2", "AV_282t3", "AV_282t4"), ("AV_282t4", "AV_282t5", null) })
				cards.Add(build, new CardDef(new Power
				{
					PowerTask = next == null ? (ISimpleTask)new SummonTask(man, SummonSide.SPELL)
						: ComplexTask.Create(new SummonTask(man, SummonSide.SPELL), new AddCardTo(next, EntityType.HAND))
				}));
			// [AV_290] Iceblood Tower (10) — At the end of your turn, cast another spell from your deck. Lasts 3 turns.
			cards.Add("AV_290", new CardDef(PaladinAura(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var spells = c.DeckZone.Where(p => p is Spell).Cast<Spell>().ToList();
						if (spells.Count > 0) Generic.CastFromDeck(c, s, spells[g.Random.Next(spells.Count)]);
					})
				}
			})));
			// [ONY_006] Deep Breath (5) — Deal $@ damage to a minion and its neighbors. (Improved by number of other spells in
			// your hand.) (@ = the card's TAG_SCRIPT_DATA_NUM_1 + the other spells, like Charged Call.)
			cards.Add("ONY_006", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					int damage = s.Card[GameTag.TAG_SCRIPT_DATA_NUM_1] + c.HandZone.Count(p => p is Spell && p.Id != s.Id);
					var hit = new List<Minion> { m };
					if (m.Zone?.Type == Zone.PLAY) hit.AddRange(Neighbors(m));
					foreach (Minion x in hit) Generic.DamageCharFunc((IPlayable)s, x, damage, true);
				})
			}));
			// [ONY_007] Haleh, Matron Protectorate (8) — After you cast a spell, deal 4 damage randomly split among all enemies.
			cards.Add("ONY_007", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new EnqueueTask(V("ONY_007", "damage", 4), ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, 1))
				}
			}));
			// [ONY_029] Drakefire Amulet (10) — Tradeable. Discover 2 Dragons. Summon them.
			cards.Add("ONY_029", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.IsRace(Race.DRAGON), action: ChoiceAction.SUMMON),
					DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.IsRace(Race.DRAGON), action: ChoiceAction.SUMMON))
			}));

			// ---------------------------------------------------------------- Paladin
			// [AV_213] Vitality Surge (2) — Draw a minion. Restore Health to your hero equal to its Cost.
			cards.Add("AV_213", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.DeckZone.Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.Count == 0) return;
					IPlayable drawn = minions[g.Random.Next(minions.Count)];
					int cost = drawn.Cost;
					Generic.Draw(c, drawn);
					if (cost > 0) c.Hero.TakeHeal((IPlayable)s, cost);
				})
			}));
			// [AV_338] Hold the Bridge (3) — Give a minion +2/+1 and Divine Shield. It gains Lifesteal until end of turn.
			cards.Add("AV_338", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("AV_338e", EntityType.TARGET), new AddEnchantmentTask("AV_338e2", EntityType.TARGET))
			}));
			cards.Add("AV_338e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("AV_338", "attack", 2)), Effects.Health_N(V("AV_338", "health", 1)), new Effect(GameTag.DIVINE_SHIELD, EffectOperator.SET, 1))
			}));
			cards.Add("AV_338e2", OneTurn(Effects.Lifesteal));
			// [AV_339] Templar Captain (8) — Rush. After this attacks a minion, summon a 5/5 Defender with Taunt.
			cards.Add("AV_339", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					Condition = new SelfCondition(p => p.Game.CurrentEventData?.EventTarget is Minion),
					SingleTask = new SummonTask("AV_342t", SummonSide.RIGHT)
				}
			}));
			// [AV_340] Brasswing (8) — At the end of your turn, deal 2 damage to all enemies. Honorable Kill: Restore 4 Health to
			// your hero.
			cards.Add("AV_340", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var enemies = new List<ICharacter> { c.Opponent.Hero };
						enemies.AddRange(c.Opponent.BoardZone.Where(m => !m.IsDead));
						foreach (ICharacter e in enemies) Generic.DamageCharFunc((IPlayable)s, e, V("AV_340", "damage", 2), false);
					})
				},
				HonorableKillTask = new CustomTask((g, c, s, t, stack) => c.Hero.TakeHeal((IPlayable)s, V("AV_340", "heal", 4)))
			}));
			// [AV_341] Cavalry Horn (5) — Deathrattle: Summon the lowest Cost minion in your hand.
			cards.Add("AV_341", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.HandZone.Where(p => Generic.IsMinionCard(p)).Cast<Minion>().ToList();
					if (minions.Count == 0) return;
					int low = minions.Min(m => m.Cost);
					var lowest = minions.Where(m => m.Cost == low).ToList();
					Generic.SummonFromHand(c, lowest[g.Random.Next(lowest.Count)], s);
				})
			}));
			// [AV_342] Protect the Innocent (5) — Summon a 5/5 Defender with Taunt. If your hero was healed this turn, summon another.
			cards.Add("AV_342", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Summon(g, c, Cards.FromId("AV_342t"), s);
					if (c.AmountHeroHealedThisTurn > 0) Summon(g, c, Cards.FromId("AV_342t"), s);
				})
			}));
			// [AV_343] Stonehearth Vindicator (3) — Battlecry: Draw a spell that costs (3) or less. It costs (0) this turn.
			cards.Add("AV_343", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (DrawOne(g, c, p => p is Spell && p.Cost <= V("AV_343", "cost", 3)) is IPlayable spell) Give(g, s, spell, "AV_343e");
				})
			}));
			cards.Add("AV_343e", OneTurnCost0);
			// [AV_344] Dun Baldar Bridge (4) — After you summon a minion, give it +2/+2. Lasts 3 turns.
			cards.Add("AV_344", new CardDef(PaladinAura(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventSource is Minion m && m.Zone?.Type == Zone.PLAY) Give(g, s, m, "AV_344e");
					})
				}
			})));
			cards.Add("AV_344e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("AV_344", "stats", 2))) }));
			// [ONY_020] Stormwind Avenger (3) — After you cast a spell on this minion, it gains +2 Attack.
			cards.Add("ONY_020", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY_SPELL_CASTED_ON_THE_OWNER,
					SingleTask = new AddEnchantmentTask("ONY_020e", EntityType.SOURCE)
				}
			}));
			cards.Add("ONY_020e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("ONY_020", "attack", 2))) }));
			// [ONY_027] Ring of Courage (2) — Tradeable. Give a minion +1/+1. Repeat for each enemy minion.
			cards.Add("ONY_027", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					int times = 1 + c.Opponent.BoardZone.Count;
					for (int i = 0; i < times; i++) Give(g, s, m, "ONY_027e");
				})
			}));
			cards.Add("ONY_027e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ONY_027", "stats", 1))) }));

			// ---------------------------------------------------------------- Priest
			// [AV_315] Deliverance (3) — Deal $3 damage to a minion. Honorable Kill: Summon a new 3/3 copy of it.
			cards.Add("AV_315", new CardDef(TargetMinion, new Power
			{
				PowerTask = new DamageTask(V("AV_315", "damage", 3), EntityType.TARGET, true),
				HonorableKillTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion killed) || c.BoardZone.IsFull) return;
					var copy = (Minion)Entity.FromCard(c, killed.Card);
					Generic.SummonBlock.Invoke(g, copy, -1, s);
					if (copy.Zone?.Type == Zone.PLAY) Give(g, s, copy, "AV_315e2");
				})
			}));
			cards.Add("AV_315e2", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("AV_315", "stats", 3))) }));
			// [AV_324] Shadow Word: Devour (1) — Choose a minion. It steals 1 Health from ALL other minions.
			cards.Add("AV_324", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion devourer)) return;
					int stolen = 0;
					foreach (Minion m in c.BoardZone.Concat(c.Opponent.BoardZone).Where(x => x.Id != devourer.Id && !x.IsDead).ToList())
					{
						Give(g, s, m, m.Controller == devourer.Controller ? "AV_324eb" : "AV_324e3");
						stolen++;
					}
					for (int i = 0; i < stolen; i++) Give(g, s, devourer, devourer.Controller == c ? "AV_324e2" : "AV_324e2b");
				})
			}));
			foreach (string id in new[] { "AV_324e3", "AV_324eb" })
				cards.Add(id, new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(-V("AV_324", "health", 1))) }));
			foreach (string id in new[] { "AV_324e2", "AV_324e2b" })
				cards.Add(id, new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("AV_324", "health", 1))) }));
			// [AV_325] Undying Disciple (6) — Taunt. Deathrattle: Deal damage equal to this minion's Attack to all enemy minions.
			cards.Add("AV_325", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					int atk = s is Minion me ? me.AttackDamage : 0;
					if (atk <= 0) return;
					foreach (Minion m in c.Opponent.BoardZone.Where(x => !x.IsDead).ToList()) Generic.DamageCharFunc((IPlayable)s, m, atk, false);
				})
			}));
			// [AV_326] Luminous Geode (2) — After a friendly minion is healed, give it +2 Attack.
			cards.Add("AV_326", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.HEAL)
				{
					TriggerSource = TriggerSource.MINIONS,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventTarget is Minion healed && healed.Zone?.Type == Zone.PLAY) Give(g, s, healed, "AV_326e");
					})
				}
			}));
			cards.Add("AV_326e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("AV_326", "attack", 2))) }));
			// [AV_329] Bless (2) — Give a minion +2 Health, then set its Attack to be equal to its Health.
			cards.Add("AV_329", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Give(g, s, m, "AV_329e2");
					Generic.AddEnchantmentBlock(g, Cards.FromId("AV_329e"), (IPlayable)s, m, m.Health, 0, 0);
				})
			}));
			cards.Add("AV_329e2", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("AV_329", "health", 2))) }));
			cards.Add("AV_329e", new CardDef(new Power { Enchant = Enchants.Enchants.SetAttackScriptTag }));
			// [AV_330] Gift of the Naaru (1) — Restore 3 Health to all characters. If any are still damaged, draw a card.
			cards.Add("AV_330", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var all = new List<ICharacter> { c.Hero, c.Opponent.Hero };
					all.AddRange(c.BoardZone.Concat(c.Opponent.BoardZone).Where(m => !m.IsDead));
					foreach (ICharacter ch in all) ch.TakeHeal((IPlayable)s, V("AV_330", "heal", 3));
					if (all.Any(ch => !ch.IsDead && ch.Damage > 0)) Generic.Draw(c);
				})
			}));
			// [AV_331] Najak Hexxen (4) — Battlecry: Take control of an enemy minion. Deathrattle: Give the minion back.
			cards.Add("AV_331", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion stolen) || c.BoardZone.IsFull) return;
					new ControlTask(EntityType.TARGET).Process(g, c, s, stolen);
					if (stolen.Controller == c) Generic.AddEnchantmentBlock(g, Cards.FromId("AV_331e2"), (IPlayable)s, stolen, 0, 0, 0);
				}),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.Where(m => m.AppliedEnchantments?.Any(e => e.Card.Id == "AV_331e2" && e.Creator?.Id == s.Id) == true).ToList())
					{
						if (m.Controller.Opponent.BoardZone.IsFull) continue;
						new ControlTask(EntityType.TARGET, opposite: true).Process(g, c, s, m);
					}
				})
			}));
			cards.Add("AV_331e2", new CardDef(new Power()));
			// [ONY_017] Horn of Wrathion (3) — Draw a minion. If it's a Dragon, summon two 2/1 Whelps with Rush.
			cards.Add("ONY_017", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.DeckZone.Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.Count == 0) return;
					IPlayable drawn = minions[g.Random.Next(minions.Count)];
					bool dragon = IsRaceCard(drawn, Race.DRAGON);
					Generic.Draw(c, drawn);
					if (dragon)
						for (int i = 0; i < V("ONY_017", "whelps", 2); i++) Summon(g, c, Cards.FromId("ONY_001t"), s);
				})
			}));
			// [ONY_026] Lightmaw Netherdrake (4) — Battlecry: If you're holding a Holy and a Shadow spell, deal 3 damage to all other
			// minions.
			cards.Add("ONY_026", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!c.HandZone.Any(p => p is Spell && p[SPELL_SCHOOL] == HOLY) || !c.HandZone.Any(p => p is Spell && p[SPELL_SCHOOL] == SHADOW)) return;
					foreach (Minion m in c.BoardZone.Concat(c.Opponent.BoardZone).Where(m => m.Id != s.Id && !m.IsDead).ToList())
						Generic.DamageCharFunc((IPlayable)s, m, V("ONY_026", "damage", 3), false);
				})
			}));
			// [AV_664] Stormpike Aid Station (3) — At the end of your turn, give your minions +2 Health. Lasts 3 turns.
			cards.Add("AV_664", new CardDef(PaladinAura(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.Where(m => !m.IsDead), "AV_664e2")) }
			})));
			cards.Add("AV_664e2", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("AV_664", "health", 2))) }));

			// ---------------------------------------------------------------- Rogue
			// [AV_201] Coldtooth Yeti (3) — Combo: Gain +3 Attack.
			cards.Add("AV_201", new CardDef(new Power { ComboTask = new AddEnchantmentTask("AV_201e", EntityType.SOURCE) }));
			cards.Add("AV_201e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("AV_201", "attack", 3))) }));
			// [AV_298] Wildpaw Gnoll (6) — Rush. Costs (1) less for each non-Rogue Class card added to your hand this game.
			cards.Add("AV_298", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.AddedToHandThisGame.Count(x => !x.HasClass(CardClass.ROGUE) && !x.HasClass(CardClass.NEUTRAL)))
			}));
			// [AV_400] Snowfall Graveyard (5) — Your Deathrattles trigger twice. Lasts 3 turns.
			cards.Add("AV_400", new CardDef(PaladinAura(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER, new Effect(GameTag.EXTRA_DEATHRATTLES_BASE, EffectOperator.SET, 1)) { ShowAs = "AV_400e" }
			})));
			// [AV_402] The Lobotomizer (2) — Honorable Kill: Get a copy of the top card of your opponent's deck.
			cards.Add("AV_402", new CardDef(new Power
			{
				HonorableKillTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.Opponent.DeckZone.Count > 0) AddToHand(c, c.Opponent.DeckZone.TopCard.Card);
				})
			}));
			// [AV_710] Reconnaissance (2) — Discover a Deathrattle minion from another class. It costs (2) less.
			cards.Add("AV_710", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.Where((c, x) => x.Type == CardType.MINION && x[GameTag.DEATHRATTLE] > 0 && !x.HasClass(c.HeroClass) && !x.HasClass(CardClass.NEUTRAL), "AV_710e")
			}));
			cards.Add("AV_710e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("AV_710", "discount", 2))) }));
			// [AV_711] Double Agent (3) — Battlecry: If you're holding a card from another class, summon a copy of this.
			cards.Add("AV_711", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.HandZone.Any(p => !p.Card.HasClass(c.HeroClass) && !p.Card.HasClass(CardClass.NEUTRAL)) && s is Minion me)
						Summon(g, c, me.Card, s);
				})
			}));
			// [ONY_030] SI:7 Smuggler (3) — Battlecry: Summon a random @-Cost minion. (Upgraded for each other SI:7 card you have
			// played this game.) (@ = the card's TAG_SCRIPT_DATA_NUM_1 + the other SI:7 cards played.)
			cards.Add("ONY_030", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int cost = s.Card[GameTag.TAG_SCRIPT_DATA_NUM_1] + c.PlayHistory.Count(h => h.SourceCard != null && IsSI7(h.SourceCard) && h.SourceId != s.Id);
					Summon(g, c, RandomBoardMinion(g, x => x.Cost == cost), s);
				})
			}));
			// [ONY_032] Tooth of Nefarian (2) — Deal $3 damage. Honorable Kill: Discover a spell from another class.
			cards.Add("ONY_032", new CardDef(TargetAny, new Power
			{
				PowerTask = new DamageTask(V("ONY_032", "damage", 3), EntityType.TARGET, true),
				HonorableKillTask = DiscoverFromTask.Where((c, x) => x.Type == CardType.SPELL && !x.HasClass(c.HeroClass) && !x.HasClass(CardClass.NEUTRAL))
			}));

			// ---------------------------------------------------------------- Shaman
			// [AV_250] Snowball Fight! (3) — Deal $1 damage to a minion and Freeze it. If it survives, repeat this on another minion!
			cards.Add("AV_250", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var hit = new HashSet<int>();
					Minion m = t as Minion;
					while (m != null && hit.Count < 30)
					{
						hit.Add(m.Id);
						Generic.DamageCharFunc((IPlayable)s, m, V("AV_250", "damage", 1), true);
						if (!m.IsDead) m.IsFrozen = true;
						if (m.IsDead || m.ToBeDestroyed) return;
						var next = c.BoardZone.Concat(c.Opponent.BoardZone).Where(x => !hit.Contains(x.Id) && !x.IsDead && !x.ToBeDestroyed && !x.Untouchable).ToList();
						m = next.Count > 0 ? next[g.Random.Next(next.Count)] : null;
					}
				})
			}));
			// [AV_251] Cheaty Snobold (3) — After an enemy is Frozen, deal 3 damage to it.
			cards.Add("AV_251", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.FROZEN)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventSource is ICharacter frozen && !frozen.IsDead) Generic.DamageCharFunc((IPlayable)s, frozen, V("AV_251", "damage", 3), false);
					})
				}
			}));
			// [AV_255] Snowfall Guardian (6) — Battlecry: Freeze all other minions.
			cards.Add("AV_255", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => FreezeAll(c.BoardZone.Concat(c.Opponent.BoardZone).Where(m => m.Id != s.Id)))
			}));
			// [AV_257] Bearon Gla'shear (7) — Battlecry: For each Frost spell you've cast this game, summon a 3/4 Elemental that
			// Freezes.
			cards.Add("AV_257", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = SpellsCast(c).Count(h => IsFrostCard(h.SourceCard));
					for (int i = 0; i < n && !c.BoardZone.IsFull; i++) Summon(g, c, Cards.FromId("AV_257t"), s);
				})
			}));
			// [AV_259] Frostbite (2) — Deal $3 damage. Honorable Kill: Your opponent's next spell costs (2) more.
			cards.Add("AV_259", new CardDef(TargetAny, new Power
			{
				PowerTask = new DamageTask(V("AV_259", "damage", 3), EntityType.TARGET, true),
				HonorableKillTask = new AddEnchantmentTask("AV_259e", EntityType.OP_CONTROLLER)
			}));
			cards.Add("AV_259e", NextCardCost(Effects.AddCost(V("AV_259", "cost", 2)), SelfCondition.IsSpell, "AV_259e2", opponent: true));
			// [AV_260] Sleetbreaker (2) — Battlecry: Add a Windchill to your hand.
			cards.Add("AV_260", new CardDef(new Power { PowerTask = new AddCardTo("AV_266", EntityType.HAND) }));
			// [AV_266] Windchill (1) — Freeze a minion. Draw a card.
			cards.Add("AV_266", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) => { if (t is Minion m && !m.IsDead) m.IsFrozen = true; }), new DrawTask())
			}));
			// [AV_268] Wildpaw Cavern (4) — At the end of your turn, summon a 3/4 Elemental that Freezes. Lasts 3 turns.
			cards.Add("AV_268", new CardDef(PaladinAura(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new SummonTask("AV_257t", SummonSide.SPELL) }
			})));
			// [ONY_011] Don't Stand in the Fire! (5) — Deal $10 damage randomly split among all enemy minions. Overload: (1)
			cards.Add("ONY_011", new CardDef(new Power { PowerTask = new EnqueueTask(V("ONY_011", "damage", 10), ComplexTask.DamageRandomTargets(1, EntityType.OP_MINIONS, 1), true) }));
			// [ONY_012] Spirit Mount (2) — Give a minion +1/+2 and Spell Damage +1. When it dies, summon a Spirit Raptor.
			cards.Add("ONY_012", MountSpell("ONY_012e"));
			cards.Add("ONY_012e", MountEnchant("ONY_012t", Effects.Attack_N(V("ONY_012", "attack", 1)), Effects.Health_N(V("ONY_012", "health", 2)),
				new Effect(GameTag.SPELLPOWER, EffectOperator.ADD, V("ONY_012", "spell damage", 1))));
			// [ONY_013] Bracing Cold (2) — Restore 5 Health to your hero. Reduce the Cost of a random spell in your hand by (2).
			cards.Add("ONY_013", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new HealTask(V("ONY_013", "heal", 5), EntityType.HERO), BuffRandomInHand(p => p is Spell, "ONY_013e"))
			}));
			cards.Add("ONY_013e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("ONY_013", "discount", 2))) }));

			// ---------------------------------------------------------------- Warlock
			// [AV_277] Seeds of Destruction (2) — Shuffle four Rifts into your deck. They summon a 3/3 Dread Imp when drawn.
			cards.Add("AV_277", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("AV_277", "rifts", 4); i++) Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("AV_316t4")));
				})
			}));
			if (!cards.ContainsKey("AV_316t4"))
				cards.Add("AV_316t4", new CardDef(new Power { PowerTask = new SummonTask("AV_316t", SummonSide.SPELL) }));
			// [AV_281] Felfire in the Hole! (5) — Draw a spell and deal $2 damage to all enemies. If it's a Fel spell, deal $1 more.
			cards.Add("AV_281", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var spells = c.DeckZone.Where(p => p is Spell).ToList();
					bool fel = false;
					if (spells.Count > 0)
					{
						IPlayable drawn = spells[g.Random.Next(spells.Count)];
						fel = drawn[SPELL_SCHOOL] == FEL;
						Generic.Draw(c, drawn);
					}
					int damage = V("AV_281", "damage", 2) + (fel ? V("AV_281", "fel", 1) : 0);
					var enemies = new List<ICharacter> { c.Opponent.Hero };
					enemies.AddRange(c.Opponent.BoardZone.Where(m => !m.IsDead));
					foreach (ICharacter e in enemies) Generic.DamageCharFunc((IPlayable)s, e, damage, true);
				})
			}));
			// [AV_285] Full-Blown Evil (3) — Deal $5 damage randomly split among all enemy minions. Repeatable this turn.
			cards.Add("AV_285", new CardDef(new Power { PowerTask = new EnqueueTask(V("AV_285", "damage", 5), ComplexTask.DamageRandomTargets(1, EntityType.OP_MINIONS, 1), true) }));
			// [AV_286] Felwalker (6) — Taunt. Battlecry: Cast the highest Cost Fel spell from your hand.
			cards.Add("AV_286", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var fel = c.HandZone.Where(p => p is Spell && p[SPELL_SCHOOL] == FEL).Cast<Spell>().ToList();
					if (fel.Count == 0) return;
					int top = fel.Max(p => p.Cost);
					var highest = fel.Where(p => p.Cost == top).ToList();
					Generic.CastFromHand(c, s, highest[g.Random.Next(highest.Count)]);
				})
			}));
			// [AV_308] Grave Defiler (1) — Battlecry: Choose a Fel spell in your hand. Get a copy of it. (A pick from the hand:
			// user rule "not random unless it says so".)
			cards.Add("AV_308", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var fel = c.HandZone.Where(p => p is Spell && p[SPELL_SCHOOL] == FEL).ToList();
					Generic.DiscoverFromCards(c, s, Generic.PickFrom(fel, g, null), (g2, c2, s2, picked) =>
					{
						if (!c2.HandZone.IsFull) Generic.Copy(c2, s2, picked, Zone.HAND);
					});
				})
			}));
			// [AV_312] Sacrificial Summoner (3) — Battlecry: Destroy a friendly minion. Summon a minion from your deck that costs (1)
			// more.
			cards.Add("AV_312", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion victim)) return;
					int cost = victim.Cost + V("AV_312", "more", 1);
					Generic.Destroy(victim, s);
					g.DeathProcessingAndAuraUpdate();
					SummonFromDeck(g, c, s, p => p.Cost == cost);
				})
			}));
			// [AV_313] Hollow Abomination (5) — Battlecry: Deal 1 damage to all enemy minions. Honorable Kill: Gain the minion's
			// Attack.
			cards.Add("AV_313", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.Opponent.BoardZone.Where(x => !x.IsDead).ToList()) Generic.DamageCharFunc((IPlayable)s, m, V("AV_313", "damage", 1), false);
				}),
				HonorableKillTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion killed && killed.AttackDamage > 0 && s is Minion me)
						Generic.AddEnchantmentBlock(g, Cards.FromId("AV_313e"), me, me, killed.AttackDamage, 0, 0);
				})
			}));
			cards.Add("AV_313e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackScriptTag }));
			// [AV_657] Desecrated Graveyard (3) — At the end of your turn, destroy your lowest Attack minion to summon a 4/4 Shade.
			// Lasts 3 turns.
			cards.Add("AV_657", new CardDef(PaladinAura(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					EffectIndex = 1, // live: its effect 1, the countdown 0 (audit 2026-10-08)
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var mine = c.BoardZone.Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
						if (mine.Count == 0) return;
						int low = mine.Min(m => m.AttackDamage);
						var lowest = mine.Where(m => m.AttackDamage == low).ToList();
						Minion victim = lowest[g.Random.Next(lowest.Count)];
						int pos = victim.ZonePosition;
						Generic.Destroy(victim, s);
						g.DeathProcessingAndAuraUpdate();
						Summon(g, c, Cards.FromId("AV_657t"), s, System.Math.Min(pos, c.BoardZone.Count));
					})
				}
			}, expiryInOwnBlock: true)));
			// [ONY_033] Impfestation (6) — Summon a 3/3 Dread Imp to attack each enemy minion.
			cards.Add("ONY_033", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion enemy in c.Opponent.BoardZone.Where(m => !m.IsDead && !m.Untouchable).ToList())
					{
						if (c.BoardZone.IsFull) return;
						var imp = (Minion)Entity.FromCard(c, Cards.FromId("AV_316t"));
						Generic.SummonBlock.Invoke(g, imp, -1, s);
						if (imp.Zone?.Type == Zone.PLAY && !enemy.IsDead && enemy.Zone?.Type == Zone.PLAY) EffectAttack(g, imp, enemy);
					}
				})
			}));
			// [ONY_034] Curse of Agony (1) — Shuffle three Agonies into the opponent's deck. They deal Fatigue damage when drawn.
			cards.Add("ONY_034", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("ONY_034", "agonies", 3); i++) Generic.ShuffleIntoDeck.Invoke(c.Opponent, s, Entity.FromCard(c.Opponent, Cards.FromId("ONY_034t")));
				})
			}));
			// [ONY_034t] Agony (1) — Casts When Drawn. Take Fatigue damage (the next Fatigue: it counts up like an empty-deck draw).
			cards.Add("ONY_034t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int fatigue = c.Hero.Fatigue + 1;
					c.Hero.Fatigue = fatigue;
					Generic.DamageCharFunc((IPlayable)s, c.Hero, fatigue, false);
				})
			}));
			// [ONY_035] Spawn of Deathwing (5) — Battlecry: Destroy a random enemy minion. Discard a random card.
			cards.Add("ONY_035", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = c.Opponent.BoardZone.Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
					if (enemies.Count > 0) Generic.Destroy(enemies[g.Random.Next(enemies.Count)], s);
					if (c.HandZone.Count > 0) Generic.DiscardBlock.Invoke(c, c.HandZone[g.Random.Next(c.HandZone.Count)]);
				})
			}));

			// ---------------------------------------------------------------- Warrior
			// [AV_108] Shield Shatter (10) — Deal $5 damage to all minions. Costs (1) less for each Armor you have.
			cards.Add("AV_108", new CardDef(new Power
			{
				PowerTask = new DamageTask(V("AV_108", "damage", 5), EntityType.ALLMINIONS, true),
				Aura = new AdaptiveCostEffect(p => p.Controller.Hero.Armor)
			}));
			// [AV_109] Frozen Buckler (2) — Gain 10 Armor. At the start of your next turn, lose 5 Armor.
			cards.Add("AV_109", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new ArmorTask(V("AV_109", "armor", 10)), new AddEnchantmentTask("AV_109e", EntityType.CONTROLLER))
			}));
			cards.Add("AV_109e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) =>
					{
						c.Hero.Armor = System.Math.Max(0, c.Hero.Armor - V("AV_109", "lose", 5));
					}), RemoveEnchantmentTask.Task)
				}
			}));
			// [AV_119] To the Front! (2) — Your minions cost (2) less this turn (but not less than 1).
			cards.Add("AV_119", new CardDef(new Power { PowerTask = new AddEnchantmentTask("AV_119e", EntityType.CONTROLLER) }));
			cards.Add("AV_119e", new CardDef(new Power
			{
				Enchant = new Enchant { IsOneTurnEffect = true },
				Aura = new Aura(AuraType.HAND, Effects.ReduceCostNotBelow1(V("AV_119", "discount", 2))) { Condition = SelfCondition.IsMinion }
			}));
			// [AV_145] Captain Galvangar (6) — Battlecry: If you have gained 15 or more Armor this game, gain +3/+3 and Charge.
			cards.Add("AV_145", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c[HsTag.ARMOR_GAINED_THIS_GAME] >= V("AV_145", "armor", 15)) Give(g, s, (IPlayable)s, "AV_145e");
				})
			}));
			cards.Add("AV_145e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("AV_145", "stats", 3)), Effects.Health_N(V("AV_145", "stats", 3)), Effects.Charge) }));
			// [AV_321] Glory Chaser (3) — After you play a Taunt minion, draw a card.
			cards.Add("AV_321", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Minion m && m.HasTaunt),
					SingleTask = new DrawTask()
				}
			}));
			// [AV_322] Snowed In (3) — Destroy a damaged minion. Freeze all other minions.
			cards.Add("AV_322", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_DAMAGED_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m) Generic.Destroy(m, s);
					FreezeAll(c.BoardZone.Concat(c.Opponent.BoardZone).Where(x => x.Id != t?.Id));
				})
			}));
			// [AV_323] Scrapsmith (3) — Taunt. Battlecry: Add two 2/4 Grunts with Taunt to your hand.
			cards.Add("AV_323", new CardDef(new Power { PowerTask = new AddCardTo("AV_323t", EntityType.HAND, V("AV_323", "grunts", 2)) }));
			// [AV_565] Axe Berserker (4) — Rush. Honorable Kill: Draw a weapon.
			cards.Add("AV_565", new CardDef(new Power { HonorableKillTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p is Weapon)) }));
			// [AV_660] Iceblood Garrison (2) — At the end of your turn, deal $1 damage to all minions. Lasts 3 turns.
			cards.Add("AV_660", new CardDef(PaladinAura(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					EffectIndex = 1, // live: its effect 1, the countdown 0 (audit 2026-10-08)
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						foreach (Minion m in c.BoardZone.Concat(c.Opponent.BoardZone).Where(m => !m.IsDead).ToList())
							Generic.DamageCharFunc((IPlayable)s, m, V("AV_660", "damage", 1), true);
					})
				}
			}, expiryInOwnBlock: true)));
			// [ONY_023] Hit It Very Hard (1) — Gain +10 Attack and "Can't attack heroes" this turn.
			cards.Add("ONY_023", new CardDef(new Power { PowerTask = new AddEnchantmentTask("ONY_023e", EntityType.HERO) }));
			cards.Add("ONY_023e", OneTurn(Effects.Attack_N(V("ONY_023", "attack", 10)), new Effect(GameTag.CANNOT_ATTACK_HEROES, EffectOperator.SET, 1)));
			// [ONY_025] Shoulder Check (1) — Tradeable. Give a minion +2/+1 and Rush.
			cards.Add("ONY_025", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("ONY_025e", EntityType.TARGET) }));
			cards.Add("ONY_025e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("ONY_025", "attack", 2)), Effects.Health_N(V("ONY_025", "health", 1)), Effects.Rush) }));

			AlteracBatch2(cards);
		}

		/// <summary>"Cast a Secret from your deck" (Spring the Trap; Secrets you can't hold stay in the deck).</summary>
		private static readonly ISimpleTask CastSecretFromDeck = new CustomTask((g, c, s, t, stack) =>
		{
			var secrets = c.DeckZone.Where(p => p is Spell sp && sp.IsSecret && Generic.CanHoldSecret(c, p.Card)).Cast<Spell>().ToList();
			if (secrets.Count > 0 && !c.SecretZone.IsFull) Generic.CastFromDeck(c, s, secrets[g.Random.Next(secrets.Count)]);
		});

		/// <summary>"Draw your lowest / highest Cost card" (ties: random).</summary>
		private static void DrawByCost(Game g, Controller c, bool lowest)
		{
			if (c.DeckZone.Count == 0) return;
			int cost = lowest ? c.DeckZone.Min(p => p.Cost) : c.DeckZone.Max(p => p.Cost);
			DrawWhere(g, c, p => p.Cost == cost);
		}

		/// <summary>
		/// "Remove your deck. Discover a card and add N copies of it into your deck, repeating R times" (wiki Kazakusan notes:
		/// the same effect as Archivist Elysiana's). Each round offers up to three cards of <paramref name="pool"/> not picked
		/// yet; the picks' copies are shuffled in at the end, then <paramref name="done"/> runs with the picks.
		/// </summary>
		private static void CraftDeck(Game g, Controller c, IEntity source, IReadOnlyList<Card> pool, int rounds, int copies,
			System.Action<Game, Controller, IEntity, List<Card>> done = null)
		{
			var picked = new List<Card>();
			void Finish(Game g2, Controller c2, IEntity s2)
			{
				foreach (IPlayable old in c2.DeckZone.ToList())
					c2.SetasideZone.Add(c2.DeckZone.Remove(old));
				foreach (Card card in picked)
					for (int i = 0; i < copies; i++)
						Generic.ShuffleIntoDeck.Invoke(c2, s2, Entity.FromCard(c2, card));
				done?.Invoke(g2, c2, s2, picked);
			}
			void Round(Game g2, Controller c2, IEntity s2, int left)
			{
				List<Card> offer = left > 0 ? Pick(g2, pool.Where(x => !picked.Contains(x)), 3) : new List<Card>();
				if (offer.Count == 0) { Finish(g2, c2, s2); return; }
				Generic.CreateChoiceEntities(c2, s2, OfferCards(c2, s2, offer.Select(x => x.Id)), ChoiceAction.STACK, new CustomTask((g3, c3, s3, chosen, st) =>
				{
					if (chosen != null)
					{
						picked.Add(chosen.Card);
						Generic.NoteAffected(chosen, s3.Id);
						if (chosen.Zone?.Type == Zone.SETASIDE) c3.GraveyardZone.Add(chosen.Zone.Remove(chosen));
					}
					Round(g3, c3, s3, left - 1);
				}));
			}
			Round(g, c, source, rounds);
		}

		/// <summary>Kazakusan's Treasures: the cards carrying client tag 2054 (HsTag.KAZAKUSAN_TREASURE; the same 28 as its
		/// RelatedCards — LOCUUUUSTS!!! left the pool in 22.4.3 and has no tag).</summary>
		private static List<Card> KazakusanTreasures() => Cards.All.Where(x => x[HsTag.KAZAKUSAN_TREASURE] > 0).ToList();

		/// <summary>
		/// Kazakusan's Quel'Delar (wiki: "2 Quel'Delars can be assembled instead if you Discover at least a Blade of Quel'Delar
		/// and a Hilt of Quel'Delar. This occurs regardless of where the pieces are (i.e.: in hand or deck)"; user 2026-10-06:
		/// one piece in each of two Discovers gives Quel'Delar): the pieces leave your hand and deck, 2 Quel'Delars go into the
		/// deck.
		/// </summary>
		private static List<IPlayable> QuelDelarPieces(Controller c, string blade = "ONY_005tc5", string hilt = "ONY_005tc6") =>
			c.HandZone.Concat(c.DeckZone).Where(p => p.Card.Id == blade || p.Card.Id == hilt).ToList();

		/// <summary>When both pieces are there, "Forging Quel'Delar" (ONY_005tc7t) is cast by the effect (CastQuietly); its
		/// own block does the forge.</summary>
		private static void ForgeQuelDelar(Game g, Controller c, IEntity source, string blade = "ONY_005tc5", string hilt = "ONY_005tc6",
			string forging = "ONY_005tc7t")
		{
			var pieces = QuelDelarPieces(c, blade, hilt);
			if (pieces.Any(p => p.Card.Id == blade) && pieces.Any(p => p.Card.Id == hilt))
				CastQuietly(g, c, source, Cards.FromId(forging));
		}

		private static void Kazakusan(IDictionary<string, CardDef> cards)
		{
			// [ONY_005] Kazakusan (8) — Battlecry: If you played 4 other Dragons this game, craft a custom deck of Treasures.
			// (Remove your deck; Discover a Treasure and add 2 copies, 5 times; Blade + Hilt become 2 Quel'Delars.) Its hand text
			// "(4 left!) … (Ready!)" is the client's: threshold 4 (card data) minus its count (Power.ThresholdCount).
			cards.Add("ONY_005", new CardDef(new Power
			{
				ThresholdCount = p => p.Controller.PlayHistory.Count(h => h.SourceId != p.Id && h.SourceCard?.Type == CardType.MINION && h.SourceCard.IsRace(Race.DRAGON)),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!Generic.ThresholdMet((IPlayable)s)) return;
					var pool = KazakusanTreasures();
					CraftDeck(g, c, s, pool, V("ONY_005", "treasures", 5), V("ONY_005", "copies", 2), (g2, c2, s2, picked) => ForgeQuelDelar(g2, c2, s2));
				})
			}));

			// ---------------------------------------------------------------- the Treasures
			// [ONY_005ta1] Necrotic Poison (2) — Destroy a minion.
			cards.Add("ONY_005ta1", new CardDef(TargetMinion, new Power { PowerTask = new DestroyTask(EntityType.TARGET) }));
			// [ONY_005ta2] Mutating Injection (2) — Give a minion +4/+4 and Taunt.
			cards.Add("ONY_005ta2", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("ONY_005ta2e", EntityType.TARGET) }));
			cards.Add("ONY_005ta2e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ONY_005ta2", "stats", 4)).Append(Effects.TauntEff).ToArray()) }));
			// [ONY_005ta3] The Exorcisor (1, weapon 3/3) — Silence any minion attacked by this weapon.
			cards.Add("ONY_005ta3", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventTarget is Minion attacked) new SilenceTask(EntityType.TARGET).Process(g, c, s, attacked);
					})
				}
			}));
			// [ONY_005ta4] Pure Cold (5) — Deal $8 damage to the enemy hero, and Freeze it.
			cards.Add("ONY_005ta4", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Generic.DamageCharFunc((IPlayable)s, c.Opponent.Hero, V("ONY_005ta4", "damage", 8), true);
					if (!c.Opponent.Hero.IsDead) c.Opponent.Hero.IsFrozen = true;
				})
			}));
			// [ONY_005ta5] Bubba (5) — Battlecry: Summon six 1/1 Bloodhounds with Rush to attack an enemy minion.
			cards.Add("ONY_005ta5", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion prey)) return;
					for (int i = 0; i < V("ONY_005ta5", "hounds", 6) && !c.BoardZone.IsFull; i++)
					{
						var hound = (Minion)Entity.FromCard(c, Cards.FromId("ONY_005ta5t"));
						Generic.SummonBlock.Invoke(g, hound, -1, s);
						if (hound.Zone?.Type == Zone.PLAY && !prey.IsDead && !prey.ToBeDestroyed && prey.Zone?.Type == Zone.PLAY) EffectAttack(g, hound, prey);
					}
				})
			}));
			// [ONY_005ta6] Holy Book (8) — Silence and destroy a minion. Summon a 10/10 copy of it.
			cards.Add("ONY_005ta6", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Card card = m.Card;
					new SilenceTask(EntityType.TARGET).Process(g, c, s, m);
					Generic.Destroy(m, s);
					g.DeathProcessingAndAuraUpdate();
					if (c.BoardZone.IsFull) return;
					var copy = (Minion)Entity.FromCard(c, card);
					Generic.SummonBlock.Invoke(g, copy, -1, s);
					if (copy.Zone?.Type == Zone.PLAY) Give(g, s, copy, "GILA_804e");
				})
			}));
			if (!cards.ContainsKey("GILA_804e"))
				cards.Add("GILA_804e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("ONY_005ta6", "stats", 10))) }));
			// [ONY_005ta7] Crusty the Crustacean (3) — Battlecry: Destroy a minion. Gain its Attack and Health.
			cards.Add("ONY_005ta7", new CardDef(BattlecryMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion prey) || !(s is Minion me)) return;
					int atk = prey.AttackDamage, hp = prey.Health;
					Generic.Destroy(prey, s);
					Generic.AddEnchantmentBlock(g, Cards.FromId("ONY_005ta7e"), me, me, atk, hp, 0);
				})
			}));
			cards.Add("ONY_005ta7e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0), Effects.Health_N(0)) { UseScriptTag = true } }));
			// [ONY_005ta8] Looming Presence (3) — Draw 2 cards. Gain 4 Armor.
			cards.Add("ONY_005ta8", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new DrawTask(V("ONY_005ta8", "cards", 2)), new ArmorTask(V("ONY_005ta8", "armor", 4)))
			}));
			// [ONY_005ta9] Beastly Beauty (3) — Rush. After this attacks a minion and survives, transform this into an 8/8.
			cards.Add("ONY_005ta9", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					Condition = new SelfCondition(p => p.Game.CurrentEventData?.EventTarget is Minion && p is Minion me && !me.IsDead && !me.ToBeDestroyed && me.Health > 0),
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me) Generic.ChangeEntityBlock(c, me, Cards.FromId(me.Card.Id + "t"), true); }) // its own 8/8 (Eudora's loot VAC_464t27 too)
				}
			}));
			// [ONY_005ta10] Spyglass (1) — Put a copy of a random card in your opponent's hand into yours. It costs (3) less.
			cards.Add("ONY_005ta10", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.Opponent.HandZone.Count == 0 || c.HandZone.IsFull) return;
					IPlayable copy = Generic.Copy(c, s, c.Opponent.HandZone[g.Random.Next(c.Opponent.HandZone.Count)], Zone.HAND);
					if (copy != null) Give(g, s, copy, "GBL_004e");
				})
			}));
			// [ONY_005ta11] Clockwork Assistant (3) — Has +1/+1 for each spell you've cast this game.
			cards.Add("ONY_005ta11", new CardDef(new Power
			{
				Aura = new MultiAura(
					new AdaptiveEffect(GameTag.ATK, EffectOperator.ADD, p => SpellsCast(p.Controller).Count()),
					new AdaptiveEffect(GameTag.HEALTH, EffectOperator.ADD, p => SpellsCast(p.Controller).Count()))
			}));
			// [ONY_005ta12] Grimmer Patron (3) — At the end of your turn, summon a copy of this minion.
			cards.Add("ONY_005ta12", new CardDef(new Power { Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new SummonCopyTask(EntityType.SOURCE) } }));
			// [ONY_005ta13] Puzzle Box (3) — Transform all minions into random ones that cost (3) more.
			cards.Add("ONY_005ta13", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					TransformCostShifted(g, c.BoardZone.Concat(c.Opponent.BoardZone).Where(m => !m.IsDead), V("ONY_005ta13", "more", 3)))
			}));
			// [ONY_005tb1] Hyperblaster (3, weapon 1/4) — Poisonous. Your hero is Immune while attacking.
			cards.Add("ONY_005tb1", new CardDef(new Power { Aura = new Aura(AuraType.HERO, "ONY_005tb1e") }));
			cards.Add("ONY_005tb1e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.IMMUNE_WHILE_ATTACKING, EffectOperator.SET, 1)) }));
			// [ONY_005tb2] Gnomish Army Knife (5) — Give a minion Rush, Windfury, Divine Shield, Lifesteal, Poisonous, Taunt, and
			// Stealth.
			cards.Add("ONY_005tb2", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("ONY_005tb2e", EntityType.TARGET) }));
			cards.Add("ONY_005tb2e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Rush, Effects.Windfury, new Effect(GameTag.DIVINE_SHIELD, EffectOperator.SET, 1), Effects.Lifesteal,
					new Effect(GameTag.POISONOUS, EffectOperator.SET, 1), Effects.TauntEff, Effects.StealthEff)
			}));
			// [ONY_005tb4] Wand of Disintegration (5) — Silence and destroy all enemy minions.
			cards.Add("ONY_005tb4", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.Opponent.BoardZone.Where(x => !x.IsDead).ToList())
					{
						new SilenceTask(EntityType.TARGET).Process(g, c, s, m);
						Generic.Destroy(m, s);
					}
				})
			}));
			// [ONY_005tb5] Staff of Scales (5) — Summon three 1/1 Snakes with Rush, Poisonous and Reborn.
			cards.Add("ONY_005tb5", new CardDef(new Power { PowerTask = ComplexTask.Repeat(new SummonTask("ONY_005tb5t", SummonSide.SPELL), V("ONY_005tb5", "snakes", 3)) }));
			// [ONY_005tb7] Canopic Jars (3) — Give your minions "Deathrattle: Summon a random Legendary minion."
			cards.Add("ONY_005tb7", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.Where(m => !m.IsDead), "ONY_005tb7e")) }));
			cards.Add("ONY_005tb7e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.DEATHRATTLE, EffectOperator.SET, 1)),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					// an enchantment's Deathrattle: source = the dead minion, target = the enchantment
					if (t is Enchantment && s is Minion dead)
						Summon(g, c, RandomBoardMinion(g, x => x.Rarity == Rarity.LEGENDARY), s, dead.LastBoardPosition);
				})
			}));
			// [ONY_005tb8] Ancient Reflections (4) — Choose a minion. Fill your board with 1/1 copies of it.
			cards.Add("ONY_005tb8", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					for (int guard = 0; guard < 7 && !c.BoardZone.IsFull; guard++)
					{
						var copy = (Minion)Entity.FromCard(c, m.Card);
						Generic.SummonBlock.Invoke(g, copy, -1, s);
						if (copy.Zone?.Type != Zone.PLAY) break;
						Give(g, s, copy, "ONY_005tb8e");
					}
				})
			}));
			cards.Add("ONY_005tb8e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("ONY_005tb8", "stats", 1))) }));
			// [ONY_005tb9] Banana Split (5) — Give a friendly minion +2/+2. Summon two copies of it.
			cards.Add("ONY_005tb9", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 } }, new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("ONY_005tb9e", EntityType.TARGET),
					ComplexTask.Repeat(new SummonCopyTask(EntityType.TARGET), V("ONY_005tb9", "copies", 2)))
			}));
			cards.Add("ONY_005tb9e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ONY_005tb9", "stats", 2))) }));
			// [ONY_005tb12] Dr. Boom's Boombox (4) — Summon 7 'Boom Bots'.
			cards.Add("ONY_005tb12", new CardDef(new Power { PowerTask = ComplexTask.Repeat(new SummonTask("GVG_110t", SummonSide.SPELL), V("ONY_005tb12", "bots", 7)) }));
			// [ONY_005tb13] Wax Rager (3) — Deathrattle: Resummon this minion.
			cards.Add("ONY_005tb13", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me) Summon(g, c, me.Card, s, me.LastBoardPosition); })
			}));
			// [ONY_005tb14] Vampiric Fangs (3) — Destroy a minion. Restore its Health to your hero.
			cards.Add("ONY_005tb14", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					int health = m.Health;
					Generic.Destroy(m, s);
					if (health > 0) c.Hero.TakeHeal((IPlayable)s, health);
				})
			}));
			// [ONY_005tc1] Embers of Ragnaros (10) — Shoot three fireballs at random enemies that deal $8 damage each.
			cards.Add("ONY_005tc1", new CardDef(new Power
			{
				PowerTask = ComplexTask.Repeat(new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = new List<ICharacter> { c.Opponent.Hero };
					enemies.AddRange(c.Opponent.BoardZone.Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable));
					enemies.RemoveAll(e => e == null || e.IsDead);
					if (enemies.Count > 0) Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], V("ONY_005tc1", "damage", 8), true);
				}), V("ONY_005tc1", "fireballs", 3))
			}));
			// [ONY_005tc2] Book of the Dead (14) — Deal $7 damage to all enemies. Costs (1) less for each minion that's died this
			// game (both players').
			cards.Add("ONY_005tc2", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.DiedThisGameCards.Concat(p.Controller.Opponent.DiedThisGameCards).Count(x => x is Minion)),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = new List<ICharacter> { c.Opponent.Hero };
					enemies.AddRange(c.Opponent.BoardZone.Where(m => !m.IsDead));
					foreach (ICharacter e in enemies) Generic.DamageCharFunc((IPlayable)s, e, V("ONY_005tc2", "damage", 7), true);
				})
			}));
			// [ONY_005tc3] Annoy-o Horn (8) — Fill your board with annoying minions (wiki: Annoying Fan, Annoy-o-Tron,
			// Annoy-o-Module, Psych-o-Tron, Annoy-o-Troupe).
			cards.Add("ONY_005tc3", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					string[] annoying = { "ETC_109", "GVG_085", "BOT_911", "OG_145", "ETC_321" };
					for (int guard = 0; guard < 7 && !c.BoardZone.IsFull; guard++)
						if (Cards.TryFromId(annoying[g.Random.Next(annoying.Length)]) is Card card) Summon(g, c, card, s);
				})
			}));
			// [ONY_005tc5] Blade of Quel'Delar (1, weapon 2/2) — no text. [ONY_005tc6] Hilt of Quel'Delar (1) — Give a minion +3/+3.
			cards.Add("ONY_005tc5", EngineOnly);
			cards.Add("ONY_005tc6", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("ONY_005tc6e", EntityType.TARGET) }));
			cards.Add("ONY_005tc6e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ONY_005tc6", "stats", 3))) }));
			// [ONY_005tc7t] Forging Quel'Delar — the pieces leave your hand and deck, 2 Quel'Delars go into the deck.
			cards.Add("ONY_005tc7t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in QuelDelarPieces(c))
						c.SetasideZone.Add(p.Zone.Remove(p));
					for (int i = 0; i < V("ONY_005", "quel'delars", 2); i++)
						Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("ONY_005tc7")));
				})
			}));
			// [ONY_005tc7] Quel'Delar (6, weapon 4/4) — After your hero attacks, deal 4 damage to all enemies.
			cards.Add("ONY_005tc7", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var enemies = new List<ICharacter> { c.Opponent.Hero };
						enemies.AddRange(c.Opponent.BoardZone.Where(m => !m.IsDead));
						foreach (ICharacter e in enemies) Generic.DamageCharFunc((IPlayable)s, e, V("ONY_005tc7", "damage", 4), false);
					})
				}
			}));
		}

		/// <summary>"Trigger its Deathrattle" for a card that isn't dying (Call of the Grave, Smokescreen): its Deathrattle runs
		/// with the card as the source, wherever it is.</summary>
		private static void TriggerDeathrattleOf(Game g, Controller c, IPlayable card) =>
			Generic.TriggerDeathrattleOf(card, card); // (the shared rule and its live wire: Actions/LostCity.cs)

		/// <summary>Pathmaker: the other choice of the last Choose One spell you cast (null when none).</summary>
		private static Card OtherChoiceOfLastChooseOne(Controller c, int except)
		{
			for (int i = c.PlayHistory.Count - 1; i >= 0; i--)
			{
				PlayHistoryEntry h = c.PlayHistory[i];
				if (h.SourceId == except || h.SourceCard?.Type != CardType.SPELL || !h.SourceCard.ChooseOne) continue;
				Card[] options = Cards.ChooseOneOptions(h.SourceCard);
				if (options == null || options.Length < 2 || h.SubOption < 1) return null;
				return options[h.SubOption == 1 ? 1 : 0];
			}
			return null;
		}

		/// <summary>Wing Commander Ichman: a Beast from your deck with Rush and AV_336e (if it kills a minion this turn, repeat).</summary>
		private static void IchmanBeast(Game g, Controller c, IEntity source)
		{
			if (SummonFromDeck(g, c, source, p => IsRaceCard(p, Race.BEAST)) is Minion beast && beast.Zone?.Type == Zone.PLAY)
				Generic.AddEnchantmentBlock(g, Cards.FromId("AV_336e"), source as IPlayable ?? beast, beast, 0, 0, 0);
		}

		private static void AlteracBatch2(IDictionary<string, CardDef> cards)
		{
			// [AV_210] Pathmaker (3) — Battlecry: Cast the other choice from the last Choose One spell you've cast. (Its hand
			// card shows that choice on hover.)
			cards.Add("AV_210", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (OtherChoiceOfLastChooseOne(c, s.Id) is Card other)
						new CastRandomSpellTask().Process(g, c, s, Entity.FromCard(c, other));
				}),
				MouseOverCard = p => OtherChoiceOfLastChooseOne(p.Controller, p.Id)
			}));
			// [AV_283] Rune of the Archmage (9) — Cast 20 Mana worth of Mage spells at enemies. (Random Mage spells that still fit
			// the Mana left; 0-Cost spells don't count towards it and aren't picked.)
			cards.Add("AV_283", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int left = V("AV_283", "mana", 20);
					for (int guard = 0; left > 0 && guard < 40; guard++)
					{
						int fits = left;
						Card spell = RandomCard(g, x => x.Type == CardType.SPELL && x.HasClass(CardClass.MAGE) && x.Cost >= 1 && x.Cost <= fits);
						if (spell == null || c.Hero.IsDead || c.Opponent.Hero.IsDead) return;
						left -= spell.Cost;
						new CastRandomSpellTask(enemiesIfPossible: true).Process(g, c, s, Entity.FromCard(c, spell));
					}
				})
			}));
			// [AV_284] Balinda Stonehearth (6) — Battlecry: Draw 2 spells. Swap their Costs with this minion's stats. (The first
			// spell's Cost swaps with its Attack, the second's with its Health: AV_284e on the spells, AV_284e2 / e3 on it.)
			cards.Add("AV_284", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					var spells = new List<IPlayable>();
					for (int i = 0; i < V("AV_284", "spells", 2); i++)
						if (DrawOne(g, c, p => p is Spell) is IPlayable drawn) spells.Add(drawn);
					if (spells.Count >= 1)
					{
						int cost = spells[0].Cost, atk = me.AttackDamage;
						Generic.AddEnchantmentBlock(g, Cards.FromId("AV_284e"), me, spells[0], atk, 0, 0);
						Generic.AddEnchantmentBlock(g, Cards.FromId("AV_284e2"), me, me, cost, 0, 0);
					}
					if (spells.Count >= 2)
					{
						int cost = spells[1].Cost, hp = me.Health;
						Generic.AddEnchantmentBlock(g, Cards.FromId("AV_284e"), me, spells[1], hp, 0, 0);
						Generic.AddEnchantmentBlock(g, Cards.FromId("AV_284e3"), me, me, cost, 0, 0);
					}
				})
			}));
			cards.Add("AV_284e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) { UseScriptTag = true } }));
			cards.Add("AV_284e2", new CardDef(new Power { Enchant = Enchants.Enchants.SetAttackScriptTag }));
			cards.Add("AV_284e3", new CardDef(new Power { Enchant = Enchants.Enchants.SetHealthScriptTag }));
			// [AV_317] Tamsin's Phylactery (4) — Discover a friendly Deathrattle minion that died this game. Give your minions its
			// Deathrattle. (AV_317e "Copied Deathrattle from {0}" captures the dead minion's card.)
			cards.Add("AV_317", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var dead = c.DiedThisGameCards.Where(p => p is Minion && p.Card[GameTag.DEATHRATTLE] > 0 && p.Card.Power?.DeathrattleTask != null).ToList();
					var options = Generic.PickFrom(dead, g, null);
					if (options.Count == 0) return;
					Generic.CreateChoiceEntities(c, s, OfferCards(c, s, options.Select(p => p.Card.Id)), ChoiceAction.STACK, new CustomTask((g2, c2, s2, picked, st2) =>
					{
						if (picked == null) return;
						IPlayable source = dead.FirstOrDefault(p => p.Card.Id == picked.Card.Id);
						foreach (Minion m in c2.BoardZone.Where(m => !m.IsDead).ToList())
							Generic.AddEnchantmentBlock(g2, Cards.FromId("AV_317e"), (IPlayable)s2, m, 0, 0, source?.Id ?? picked.Id);
						Generic.NoteAffected(picked, s2.Id);
						if (picked.Zone?.Type == Zone.SETASIDE) c2.GraveyardZone.Add(picked.Zone.Remove(picked));
					}));
				})
			}));
			cards.Add("AV_317e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.DEATHRATTLE, EffectOperator.SET, 1)),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					// an enchantment's Deathrattle: source = the dead minion, target = the enchantment
					if (t is Enchantment e && e.CapturedCard?.Power?.DeathrattleTask is ISimpleTask copied) copied.Process(g, c, s, null);
				})
			}));
			// [AV_336] Wing Commander Ichman (9) — Battlecry: Summon a Beast from your deck and give it Rush. If it kills a minion
			// this turn, repeat.
			cards.Add("AV_336", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => IchmanBeast(g, c, s)) }));
			cards.Add("AV_336e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Rush),
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.AFTER_ATTACK)
					{
						TriggerSource = TriggerSource.ENCHANTMENT_TARGET,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (!(s is Enchantment e) || e[GameTag.TAG_SCRIPT_DATA_NUM_2] > 0) return;
							if (g.CurrentEventData?.EventTarget is Minion defender && (defender.Health <= 0 || defender.ToBeDestroyed || defender.IsDead))
							{
								e[GameTag.TAG_SCRIPT_DATA_NUM_2] = 1; // once per Beast
								IchmanBeast(g, c, e.Target as IPlayable);
							}
						})
					},
					new Trigger(TriggerType.TURN_END)
					{
						EitherTurn = true,
						SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Enchantment e) e[GameTag.TAG_SCRIPT_DATA_NUM_2] = 1; })
					})
			}));
			// [AV_345] Saidan the Scarlet (3) — Rush. Whenever this minion gains Attack or Health, double that amount (wherever this
			// is). (Engine rule: its client tag 2028, HsTag.DOUBLES_STAT_GAINS, doubles every Attack / Health gain: Effect.Gain.)
			cards.Add("AV_345", EngineOnly);
			// [AV_403] Cera'thine Fleetrunner (5) — Battlecry: Replace your minions in hand and deck with ones from other classes.
			// They cost (2) less. (Random other-class minions; in the deck silently, like Lady Prestor.)
			cards.Add("AV_403", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var pool = OtherClassCards(g, c).Where(x => x.Type == CardType.MINION).ToList();
					if (pool.Count == 0) return;
					foreach (IPlayable m in c.HandZone.Where(p => Generic.IsMinionCard(p)).ToList())
					{
						IPlayable now = Generic.ChangeEntityBlock(c, m, pool[g.Random.Next(pool.Count)], true);
						if (now != null) Give(g, s, now, "AV_403e2");
					}
					foreach (IPlayable m in c.DeckZone.Where(p => Generic.IsMinionCard(p)).ToList())
					{
						g.SilentChanges = true;
						IPlayable now;
						try { now = Generic.ChangeEntityBlock(c, m, pool[g.Random.Next(pool.Count)], true); }
						finally { g.SilentChanges = false; }
						if (now != null) Give(g, s, now, "AV_403e2");
					}
				})
			}));
			cards.Add("AV_403e2", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("AV_403", "discount", 2))) }));
			// [AV_405] Contraband Stash (5) — Replay 5 cards from other classes you've played this game. (Random ones if you
			// played more; each is played again as a new card, like Zuramat's: Generic.PlayByEffect.)
			cards.Add("AV_405", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var played = c.PlayHistory.Where(h => h.SourceId != s.Id && h.SourceCard != null && h.SourceCard.Type != CardType.HERO_POWER
						&& !h.SourceCard.HasClass(c.HeroClass) && !h.SourceCard.HasClass(CardClass.NEUTRAL)).Select(h => h.SourceCard).ToList();
					for (int i = 0; i < V("AV_405", "cards", 5) && played.Count > 0; i++)
					{
						if (c.Hero.IsDead || c.Opponent.Hero.IsDead) return;
						int k = g.Random.Next(played.Count);
						Card card = played[k];
						played.RemoveAt(k);
						PlayForFree(g, c, s, card);
					}
				})
			}));
			// [AV_601] Forsaken Lieutenant (2) — Stealth. After you play a Deathrattle minion, become a 2/2 copy of it with Rush.
			cards.Add("AV_601", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.Card[GameTag.DEATHRATTLE] > 0),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(g.CurrentEventData?.EventSource is Minion played) || !(s is Minion me) || me.Zone?.Type != Zone.PLAY || me.Id == played.Id) return;
						IPlayable now = Generic.ChangeEntityBlock(c, me, played.Card, true);
						if (now != null) Give(g, s, now, "AV_601e");
					})
				}
			}));
			cards.Add("AV_601e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("AV_601", "stats", 2)).Append(Effects.Rush).ToArray()) }));
			// [ONY_019] Raid Negotiator (4) — Battlecry: Discover a Choose One card. It has both effects combined. (ONY_019e gives
			// the card CHOOSE_BOTH: Playable.ActivateTask runs both options.)
			cards.Add("ONY_019", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.ChooseOne && x[GameTag.CHOOSE_BOTH] == 0, "ONY_019e")
			}));
			cards.Add("ONY_019e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.CHOOSE_BOTH, EffectOperator.SET, 1)) }));
			// [ONY_028] Mi'da, Pure Light (6) — Divine Shield, Lifesteal. Deathrattle: Shuffle a Fragment into your deck that
			// resummons Mi'da when drawn.
			cards.Add("ONY_028", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("ONY_028t"))))
			}));
			cards.Add("ONY_028t", new CardDef(new Power { PowerTask = new SummonTask("ONY_028", SummonSide.SPELL) }));
			// [ONY_031] Smokescreen (8) — Draw 5 cards. Trigger any Deathrattles drawn.
			cards.Add("ONY_031", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var drawn = new List<IPlayable>();
					for (int i = 0; i < V("ONY_031", "cards", 5); i++)
						if (Generic.Draw(c) is IPlayable card && card.Zone?.Type == Zone.HAND) drawn.Add(card);
					foreach (IPlayable card in drawn)
						if (card.Card[GameTag.DEATHRATTLE] > 0) TriggerDeathrattleOf(g, c, card);
				})
			}));
			Kazakusan(cards);
		}
	}
}
