// HsServer addition (not upstream SabberStone): CATACLYSM — chunk 3: Rogue, Priest, Paladin and the MEND_ Paladin mini-set (spec:
// research spec_chunk3). Shared pieces: Herald, Shatter, Colossal (+4 / +3 placement), Power.WhenSummonedTask,
// HsTag.OTHER_CLASS_SPELLS_CAST_TWICE (CastSpell), CURRENT_HEALING_POWER (Character.TakeHeal), Generic.LockInHand (UNPLAYABLE_VISUALS),
// HsTag.MINION_EXTRA_END_TURN_EFFECT (Trigger), Generic.TriggerEndOfTurn / BuffRecruitsThisGame / SetRemainingHealth / DiedThisTurn /
// IsLegendary (Actions/Cataclysm.cs), a Location's hand target by its script. Every number goes through CardValues (V).
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
		/// <summary>"When summoned, get a random spell from another class. It costs ({0}) less." (Sinestra's Wings, Soldier of Sinestra)</summary>
		private static ISimpleTask WingOfSinestraSummoned => new CustomTask((g, c, s, t, stack) =>
		{
			var pool = OtherClassCards(g, c).Where(x => x.Type == CardType.SPELL).ToList();
			if (pool.Count == 0 || !(GetCard(c, pool[g.Random.Next(pool.Count)], s) is IPlayable spell)) return;
			int less = s[GameTag.TAG_SCRIPT_DATA_NUM_1];
			if (GenericCostLess(less) is string ench) Give(g, s, spell, ench);
			else for (int i = 0; i < less; i++) Give(g, s, spell, "GBL_003e");
		});

		/// <summary>Does the player have a Legendary card in play or in its hero zone (Medivh's Triumph; wiki: "any type of card, including
		/// legendaries that are Hero zone effects like Quests"; the frame counts — Generic.IsLegendary)?</summary>
		private static bool ControlsLegendary(Controller c) =>
			c.BoardZone.GetAll().Any(m => Generic.IsLegendary(m.Card)) || Generic.IsLegendary(c.Hero.Card) ||
			c.Hero.Weapon != null && Generic.IsLegendary(c.Hero.Weapon.Card) || c.SecretZone.Any(p => Generic.IsLegendary(p.Card));

		private static void CataclysmChunk3(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Rogue
			// [CATA_154] Sinestra (6) — Colossal +2. Your spells from other classes cast twice.
			cards.Add("CATA_154", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER, new Effect(HsTag.OTHER_CLASS_SPELLS_CAST_TWICE, EffectOperator.ADD, 1))
			}));
			cards.Add("CATA_154e", new CardDef(new Power { Enchant = new Enchant() }));
			// [CATA_154t / t1] Sinestra's Wing, [CATA_158t] Soldier of Sinestra — When summoned, get a random spell from another class. It costs
			// ({0}) less.
			foreach (string id in new[] { "CATA_154t", "CATA_154t1", "CATA_158t" })
				cards.Add(id, new CardDef(new Power { WhenSummonedTask = WingOfSinestraSummoned }));
			// [CATA_158] Maniacal Follower (3) — Stealth. Deathrattle: Herald {0}. (the Soldier in its slot: live 141732 3967)
			cards.Add("CATA_158", new CardDef(new Power { DeathrattleTask = HeraldTask }));
			// [CATA_200] Agent of the Old Ones (1) — Battlecry: Choose a card in your hand to transform into a Coin. (wiki fix 2026-03-26: the
			// Coin keeps nothing of it)
			cards.Add("CATA_200", new CardDef(new Power
			{
				HandTarget = SelfCondition.AnyCard,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t?.Zone?.Type != Zone.HAND || t.Controller != c) return;
					bool fx = g.History && g.OpenSubSpell((IPlayable)s, SubSpells.FindNamed(((IPlayable)s).Card.Id, "Transform"), null);
					Generic.ChangeEntityBlock(c, t, Cards.FromId("GAME_005"), true);
					if (fx) g.CloseSubSpells(s.Id);
				})
			}));
			// [CATA_201] Twilight Mistress (9) — Battlecry: Return all enemy minions to their owner's hand.
			cards.Add("CATA_201", new CardDef(new Power { PowerTask = new ReturnHandTask(EntityType.OP_MINIONS) }));
			// [CATA_202] Stolen Power (3) — Get a random Shatter card from another class. (It's already combined). (SHATTER 0 and its combined
			// text: live dk-classxp 16123)
			cards.Add("CATA_202", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var pool = OtherClassCards(g, c).Where(x => x.Type == CardType.SPELL && x[Generic.SHATTER] > 0).ToList();
					if (pool.Count == 0) return;
					IPlayable p = Entity.FromCard(c, pool[g.Random.Next(pool.Count)], new EntityData
					{
						{ GameTag.CREATOR, s.Id }, { GameTag.DISPLAYED_CREATOR, s.Id }, { Generic.SHATTER, 0 }, { GameTag.USE_ALTERNATE_CARD_TEXT, 1 }
					});
					Generic.AddHandPhase.Invoke(c, p);
				})
			}));
			// [CATA_203] Garona's Last Stand (2) — Tradeable. Destroy a Legendary minion. (the frame counts — doubt for tokens)
			cards.Add("CATA_203", new CardDef(TargetMinion, new Power
			{
				TargetIf = (PlayReq.REQ_LEGENDARY_TARGET, (src, target) => target is Minion m && Generic.IsLegendary(m.Card)),
				PowerTask = new DestroyTask(EntityType.TARGET)
			}));
			// [CATA_215] Daze (3) — Return an enemy minion to its owner's hand. They can't play it next turn. (Generic.LockInHand: live 140614
			// 62238 — CATA_215e shown, LITERALLY_UNPLAYABLE / UNPLAYABLE_VISUALS until the end of its owner's next turn)
			cards.Add("CATA_215", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				PowerTask = ComplexTask.Create(new ReturnHandTask(EntityType.TARGET), new CustomTask((g, c, s, t, stack) =>
				{
					if (t?.Zone?.Type == Zone.HAND) Generic.LockInHand(t, s, g.Turn + 1, "CATA_215e");
				}))
			}));
			cards.Add("CATA_215e", new CardDef(new Power { Enchant = new Enchant() }));
			// [CATA_481] Iso'rath (5) — Battlecry: Devour 2 random cards from the opponent's hand, then go Dormant for 2 turns. Deathrattle:
			// Return them. (set aside, their ids on it; the same cards come back — doubt on their buffs)
			cards.Add("CATA_481", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me) || me.Zone?.Type != Zone.PLAY) return;
					var devoured = c.Opponent.HandZone.GetAll().OrderBy(_ => g.Random.Next()).Take(V("CATA_481", "cards", 2)).ToList();
					GameTag[] slots = { HsTag.DEVOURED_1, HsTag.DEVOURED_2 };
					for (int i = 0; i < devoured.Count && i < slots.Length; i++)
					{
						me[slots[i]] = devoured[i].Id;
						Generic.RemoveFromHand(c.Opponent, devoured[i]);
					}
					Generic.MakeDormant(me, V("CATA_481", "turns", 2), "CATA_481e", me);
				}),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (GameTag slot in new[] { HsTag.DEVOURED_1, HsTag.DEVOURED_2 })
						if (g.IdEntityDic.TryGetValue(s[slot], out IPlayable card) && card.Zone?.Type == Zone.SETASIDE)
						{
							Controller owner = card.Controller;
							owner.SetasideZone.Remove(card);
							Generic.AddHandPhase.Invoke(owner, card);
						}
				})
			}));
			// [CATA_785] Rite of Twilight (2) — Herald {0}. Combo: Deal $3 damage. (live: the Combo damage in POWER 0, then the Herald in POWER
			// 1; without Combo — doubt: block 0 empty)
			cards.Add("CATA_785", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_FOR_COMBO, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { }),
				ComboTask = new CustomTask((g, c, s, t, stack) => { if (t is ICharacter ch) Generic.DamageCharFunc((IPlayable)s, ch, V("CATA_785", "damage", 3), true); }),
				SecondPowerTask = HeraldTask
			}));
			// [CATA_786] Chaos Supplicant (4) — After you cast a spell, cast a random spell of the same Cost from another class. (the Cost it
			// was played for, Enchanted Cauldron's way)
			cards.Add("CATA_786", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						int cost = t?[GameTag.TAG_LAST_KNOWN_COST_IN_HAND] ?? 0;
						CardClass mine = c.HeroClass;
						new CastRandomSpellTask(x => x.Cost == cost && !x.HasClass(mine) && !x.HasClass(CardClass.NEUTRAL) && !x.HasClass(CardClass.INVALID)).Process(g, c, s, null);
					})
				}
			}));

			// ---------------------------------------------------------------- Priest
			// [CATA_216] Cleansing Cleric (4) — Battlecry: Your healing effects restore 2 more Health this game. (client CURRENT_HEALING_POWER)
			cards.Add("CATA_216", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Generic.AddEnchantmentBlock(g, Cards.FromId("CATA_216e"), (IPlayable)s, c, 0, 0, 0);
					c[(GameTag)2333] = c[(GameTag)2333] + V("CATA_216", "more", 2);
				})
			}));
			cards.Add("CATA_216e", new CardDef(new Power { Enchant = new Enchant() }));
			// [CATA_300] The Black Blood (7) — Colossal +3. After you restore Health to a character, attack a random enemy minion.
			cards.Add("CATA_300", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.HEAL)
				{
					TriggerSource = TriggerSource.ALL,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventSource?.Controller != c || !(s is Minion me) || me.Zone?.Type != Zone.PLAY || me.IsDead || me.ToBeDestroyed) return;
						var enemies = c.Opponent.BoardZone.GetAll().Where(m => !(m is Location) && !m.Untouchable && !m.IsDead && !m.ToBeDestroyed && m.Health > 0).ToList();
						if (enemies.Count > 0) EffectAttack(g, me, enemies[g.Random.Next(enemies.Count)]);
					})
				}
			}));
			// [CATA_300t1..t3] Black Blood's Body — At the end of your turn, restore #3 Health to a random damaged friendly character.
			foreach (string id in new[] { "CATA_300t1", "CATA_300t2", "CATA_300t3" })
				cards.Add(id, new CardDef(new Power
				{
					Trigger = new Trigger(TriggerType.TURN_END)
					{
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							var hurt = c.BoardZone.GetAll().Cast<ICharacter>().Append(c.Hero).Where(x => x.Damage > 0 && !x.IsDead && !(x is Location)).ToList();
							if (hurt.Count > 0) hurt[g.Random.Next(hurt.Count)].TakeHeal((IPlayable)s, V("CATA_300t1", "heal", 3));
						})
					}
				}));
			// [CATA_302] Mend (1) — Restore a minion to full Health. Draw a card.
			cards.Add("CATA_302", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) => { if (t is Minion m) m.TakeFullHeal((IPlayable)s); }), new DrawTask())
			}));
			// [CATA_303] Purifying Breath (2) — Deal $5 damage to a minion. If it dies, restore #5 Health to the enemy hero.
			cards.Add("CATA_303", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Generic.DamageCharFunc((IPlayable)s, m, V("CATA_303", "damage", 5), true);
					if (m.IsDead || m.ToBeDestroyed || m.Health <= 0) c.Opponent.Hero.TakeHeal((IPlayable)s, V("CATA_303", "heal", 5));
				})
			}));
			// [CATA_304] Injured Attendant (3) — Lifesteal. Battlecry: Deal 4 damage to this minion.
			cards.Add("CATA_304", new CardDef(new Power { PowerTask = new DamageTask(V("CATA_304", "damage", 4), EntityType.SOURCE) }));
			// [CATA_305] Incensed Matriarch (4) — At the end of your turn, if this is at full Health, gain +3 Health.
			cards.Add("CATA_305", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me && me.Damage == 0) Give(g, s, me, "CATA_305e"); })
				}
			}));
			cards.Add("CATA_305e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("CATA_305", "health", 3))) }));
			// [CATA_306] Schism (4) — Shatter. Give a friendly minion +2/+3 and Elusive. Summon a copy of it. (t1: the buff, t2: the copy;
			// whole: the buff, then the copy — live vfx 47917: the copy has the buff)
			ISimpleTask schismBuff = new CustomTask((g, c, s, t, stack) =>
			{
				if (!(t is Minion m)) return;
				Give(g, s, m, "CATA_306e");
				Generic.GiveBonusEffect(m, HsTag.ELUSIVE);
			});
			ISimpleTask schismCopy = new CustomTask((g, c, s, t, stack) =>
			{
				if (t is Minion m && m.Zone?.Type == Zone.PLAY && !c.BoardZone.IsFull) Generic.Copy(c, m, m, Zone.PLAY, position: m.ZonePosition + 1);
			});
			cards.Add("CATA_306", new CardDef(TargetFriendlyMinion, new Power { PowerTask = ComplexTask.Create(schismBuff, schismCopy) }));
			cards.Add("CATA_306t1", new CardDef(TargetFriendlyMinion, new Power { PowerTask = schismBuff }));
			cards.Add("CATA_306t2", new CardDef(TargetFriendlyMinion, new Power { PowerTask = schismCopy }));
			cards.Add("CATA_306e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("CATA_306", "attack", 2)), Effects.Health_N(V("CATA_306", "health", 3))) }));
			// [CATA_307] Alexstrasza, Guardian of Life (7) — Battlecry: Set your remaining Health to 15. When you reach full Health, deal 15
			// damage to your opponent. (wiki: 15 per Battlecry; CATA_307e on the hero, spent when it fires)
			cards.Add("CATA_307", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Generic.SetRemainingHealth(c.Hero, V("CATA_307", "health", 15));
					Give(g, s, c.Hero, "CATA_307e");
				})
			}));
			cards.Add("CATA_307e", new CardDef(new Power
			{
				Enchant = new Enchant(),
				Trigger = new Trigger(TriggerType.HEAL)
				{
					TriggerSource = TriggerSource.ENCHANTMENT_TARGET,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || !(e.Target is Hero h) || h.Damage > 0) return;
						Generic.DamageCharFunc(e.Creator ?? h, h.Controller.Opponent.Hero, V("CATA_307", "damage", 15), false);
						RemoveEnchantmentTask.Task.Process(g, c, e, null);
					})
				}
			}));
			// [CATA_308] Medivh's Triumph (5) — Deal $4 damage to all minions. Costs (1) if you control a Legendary card.
			cards.Add("CATA_308", new CardDef(new Power
			{
				GlowWhile = p => ControlsLegendary(p.Controller),
				Aura = new AdaptiveCostEffect(p => V("CATA_308", "cost", 1), EffectOperator.SET, new SelfCondition(p => ControlsLegendary(p.Controller))),
				PowerTask = new CustomTask((g, c, s, t, stack) => DamageAll(s, AllOtherMinions(c, null).Cast<ICharacter>(), V("CATA_308", "damage", 4), true))
			}));

			// ---------------------------------------------------------------- Paladin
			// [CATA_432] Chromatus (8) — Colossal +4. Taunt, Lifesteal, Elusive, Divine Shield. (keywords from its data)
			cards.Add("CATA_432", new CardDef(new Power { NoScript = true }));
			// [CATA_432t1..t4] Green / Red / Blue / Bronze Head — <keyword>. Deathrattle: Remove <it> from Chromatus. (its Chromatus: the one
			// that brought it; the keyword: the head's own — live vfx 187017: a plain TAUNT 0)
			foreach (string id in new[] { "CATA_432t1", "CATA_432t2", "CATA_432t3", "CATA_432t4" })
				cards.Add(id, new CardDef(new Power
				{
					DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!g.IdEntityDic.TryGetValue(s[GameTag.CREATOR], out IPlayable p) || !(p is Minion body) || body.Zone?.Type != Zone.PLAY) return;
						Card head = ((IPlayable)s).Card;
						if (head[GameTag.TAUNT] > 0) body[GameTag.TAUNT] = 0;
						if (head[GameTag.LIFESTEAL] > 0) body[GameTag.LIFESTEAL] = 0;
						if (head[HsTag.ELUSIVE] > 0 || head[GameTag.CANT_BE_TARGETED_BY_SPELLS] > 0)
						{
							body[GameTag.CANT_BE_TARGETED_BY_SPELLS] = 0;
							body[GameTag.CANT_BE_TARGETED_BY_HERO_POWERS] = 0;
							body[HsTag.ELUSIVE] = 0;
						}
						if (head[GameTag.DIVINE_SHIELD] > 0 && body.HasDivineShield) body.HasDivineShield = false;
					})
				}));
			// [CATA_472] Inspiring Maul (2, weapon) — Deathrattle: Trigger a random friendly minion's end of turn effect.
			cards.Add("CATA_472", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var withEffect = c.BoardZone.GetAll().Where(m => !(m is Location) && !m.IsDead && Generic.HasEndOfTurnEffect(m)).ToList();
					if (withEffect.Count > 0) Generic.TriggerEndOfTurn(withEffect[g.Random.Next(withEffect.Count)]);
				})
			}));
			// [CATA_473] Nozdormu, Bronze Aspect (5) — At the end of your turn, give your minions Divine Shield. Any that already had one gain
			// +3/+3 instead.
			cards.Add("CATA_473", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						foreach (Minion m in c.BoardZone.GetAll().Where(m => !(m is Location) && !m.Untouchable && !m.IsDead).ToList())
							if (m.HasDivineShield) Give(g, s, m, "CATA_473e");
							else m.HasDivineShield = true;
					})
				}
			}));
			cards.Add("CATA_473e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("CATA_473", "stats", 3))) }));
			// [CATA_474] Spearheart Sentry (4) — At the end of your turn, get a random Holy spell. Reduce its Cost by (3).
			cards.Add("CATA_474", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (GetCard(c, RandomCard(g, x => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == HOLY), s) is IPlayable holy)
							GiveCostLess(g, s, holy, V("CATA_474", "less", 3));
					})
				}
			}));
			// [CATA_475] Scalebreaker Bulwark (6) — At the end of your turn, deal 2 damage to all enemies.
			cards.Add("CATA_475", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new CustomTask((g, c, s, t, stack) => DamageAll(s, Enemies(c), V("CATA_475", "damage", 2), false)) }
			}));
			// [CATA_477] Chamber of Aspects (2, Location) — Choose a minion in your hand. Give it +2/+2.
			cards.Add("CATA_477", new CardDef(new Power
			{
				HandTarget = SelfCondition.IsMinion,
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m && m.Zone?.Type == Zone.HAND && m.Controller == c) Give(g, s, m, "CATA_477e"); })
			}));
			cards.Add("CATA_477e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("CATA_477", "stats", 2))) }));
			// [CATA_478] Bronze Redeemer (5) — At the end of your turn, summon a Dragon with stats equal to this minion's. (its Attack and
			// current Health)
			cards.Add("CATA_478", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me) SummonWithStats(g, c, Cards.FromId("CATA_478t"), me.AttackDamage, Math.Max(1, me.Health), s);
					})
				}
			}));
			// [CATA_479] Flight Maneuvers (4) — Shatter. Summon two 4/2 Drakes. Give your minions +1 Attack and Divine Shield. (t: the Drakes,
			// t2: the buff; live dk-classxp 16114: the Drakes get it too)
			ISimpleTask drakes = new CustomTask((g, c, s, t, stack) =>
			{
				Generic.BeginSummonGroup(g, s, V("CATA_479", "drakes", 2));
				for (int i = 0; i < V("CATA_479", "drakes", 2); i++) Summon(g, c, Cards.FromId("CATA_479t3"), s);
			});
			ISimpleTask maneuvers = new CustomTask((g, c, s, t, stack) =>
			{
				foreach (Minion m in c.BoardZone.GetAll().Where(m => !(m is Location) && !m.Untouchable).ToList())
				{
					Give(g, s, m, "CATA_479e");
					m.HasDivineShield = true;
				}
			});
			cards.Add("CATA_479", new CardDef(new Power { PowerTask = ComplexTask.Create(drakes, maneuvers) }));
			cards.Add("CATA_479t", new CardDef(new Power { PowerTask = drakes }));
			cards.Add("CATA_479t2", new CardDef(new Power { PowerTask = maneuvers }));
			cards.Add("CATA_479e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("CATA_479", "attack", 1))) }));
			// [CATA_480] Sandfury Aura (3) — Your minions' end of turn effects trigger twice. Lasts @ turns.
			cards.Add("CATA_480", new CardDef(PaladinAura(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER, new Effect(HsTag.MINION_EXTRA_END_TURN_EFFECT, EffectOperator.ADD, 1))
			}, countdownIndex: 3, countdownInHistory: true))); // (live issues 3646: its countdown at index 3, shown)
			// [CATA_621] Gelbin's Triumph (1) — Get a random Paladin Aura. It lasts an additional turn.
			cards.Add("CATA_621", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(RandomCard(g, x => x[HsTag.PALADIN_AURA] > 0) is Card aura)) return;
					int more = V("CATA_621", "turns", 1);
					IPlayable p = Entity.FromCard(c, aura, new EntityData
					{
						{ GameTag.CREATOR, s.Id }, { GameTag.DISPLAYED_CREATOR, s.Id },
						{ GameTag.QUEST_PROGRESS_TOTAL, aura[GameTag.QUEST_PROGRESS_TOTAL] + more },
						{ GameTag.TAG_SCRIPT_DATA_NUM_1, aura[GameTag.TAG_SCRIPT_DATA_NUM_1] + more }
					});
					Generic.AddHandPhase.Invoke(c, p);
				})
			}));
			// [MEND_800] Brash Battlemaster (2) — Rush. Deathrattle: Give your Silver Hand Recruits +1 Attack this game.
			cards.Add("MEND_800", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => Generic.BuffRecruitsThisGame(c, s, V("MEND_800", "attack", 1), 0, "MEND_800e"))
			}));
			// [MEND_801] Resilient Savior (3) — Divine Shield. After this loses Divine Shield, give your Silver Hand Recruits +1 Health this game.
			cards.Add("MEND_801", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.LOSE_DIVINE_SHIELD)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) => Generic.BuffRecruitsThisGame(c, s, 0, V("MEND_801", "health", 1), "MEND_801e"))
				}
			}));
			// [MEND_803] Emboldening Blade (5, weapon) — Battlecry: Give your Silver Hand Recruits +1/+1 this game.
			cards.Add("MEND_803", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.BuffRecruitsThisGame(c, s, V("MEND_803", "stats", 1), V("MEND_803", "stats", 1), "MEND_803e"))
			}));
			// the player enchantments: their recruits (hand and board) get the per-recruit one
			foreach ((string player, string recruit, IEffect[] fx) in new[]
			{
				("MEND_800e", "MEND_800e2", new[] { Effects.Attack_N(V("MEND_800", "attack", 1)) }),
				("MEND_801e", "MEND_801e2", new[] { Effects.Health_N(V("MEND_801", "health", 1)) }),
				("MEND_803e", "MEND_803e2", Effects.AttackHealth_N(V("MEND_803", "stats", 1))),
			})
			{
				cards.Add(player, new CardDef(new Power { Aura = new Aura(AuraType.HAND_AND_BOARD, recruit) { Condition = SelfCondition.IsSilverHandRecruit } }));
				cards.Add(recruit, new CardDef(new Power { Enchant = new Enchant(fx) }));
			}
			// [MEND_802] Convalescence (2) — Summon two {0} Silver Hand Recruits with Divine Shield.
			cards.Add("MEND_802", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Generic.BeginSummonGroup(g, s, V("MEND_802", "recruits", 2));
					for (int i = 0; i < V("MEND_802", "recruits", 2); i++)
						if (SummonGet(g, c, Cards.FromId("CS2_101t"), s) is Minion m) m.HasDivineShield = true;
				})
			}));
			// [MEND_804] Arator the Redeemer (5) — Battlecry: Double the stats of all friendly Silver Hand Recruits and give them Taunt.
			cards.Add("MEND_804", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().Where(m => SelfCondition.IsSilverHandRecruit.Eval(m) && !m.IsDead).ToList())
						Generic.AddEnchantmentBlock(g, Cards.FromId("MEND_804e"), (IPlayable)s, m, m.AttackDamage, Math.Max(0, m.Health), 0);
				})
			}));
			cards.Add("MEND_804e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0), Effects.Health_N(0), new Effect(GameTag.TAUNT, EffectOperator.SET, 1)) { UseScriptTag = true } }));
			// [MEND_805] Charity (3) — Get copies of all friendly minions that died this turn. Give them +3/+3.
			cards.Add("MEND_805", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable dead in Generic.DiedThisTurn(c))
					{
						if (c.HandZone.IsFull) break;
						if (GetCard(c, dead.Card, s) is IPlayable copy) Give(g, s, copy, "MEND_805e");
					}
				})
			}));
			cards.Add("MEND_805e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("MEND_805", "stats", 3))) }));
			// [MEND_900] Teamwork (4) — Summon and get four {0} Silver Hand Recruits. (4 + 4 — doubt; wiki fix 35.6.0: playable on a full board)
			cards.Add("MEND_900", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = V("MEND_900", "recruits", 4);
					Generic.BeginSummonGroup(g, s, n);
					for (int i = 0; i < n; i++) Summon(g, c, Cards.FromId("CS2_101t"), s);
					for (int i = 0; i < n; i++) GetCard(c, Cards.FromId("CS2_101t"), s);
				})
			}));
		}
	}
}
