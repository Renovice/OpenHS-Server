// HsServer addition (not upstream SabberStoneCore): the BE expansion's cards, built from shared pieces.
using System;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Conditions;
using SabberStoneCore.Enums;
using SabberStoneCore.Tasks;
using SabberStoneCore.Triggers;
using SabberStoneCore.Actions;
using SabberStoneCore.Auras;
using SabberStoneCore.Enchants;
using SabberStoneCore.Loader;
using SabberStoneCore.src.Loader;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.SimpleTasks;

namespace SabberStoneCore.CardSets.HsServer
{
	public static partial class CurrentCoreCardsGen
	{
		/// <summary>"If you've played two different spell schools while holding this": a hand trigger keeps the schools seen
		/// (HsTag.SCHOOLS_SEEN bits; TAG_SCRIPT_DATA_NUM_1 = their count, up to <paramref name="needed"/>), counted again each time the
		/// card enters the hand (Carress's way, VAC_449); the Battlecry checks the count.</summary>
		private static Power SchoolsWhileHolding(int needed, ISimpleTask effect, Power with = null)
		{
			Power p = with ?? new Power();
			p.Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
			{
				TriggerActivation = TriggerActivation.HAND,
				TriggerSource = TriggerSource.FRIENDLY,
				CountsWhileHeld = true,
				Condition = new SelfCondition(x => x is Spell && x[SPELL_SCHOOL] > 0),
				SingleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is IPlayable me) || me.Zone?.Type != Zone.HAND || !(g.CurrentEventData?.EventSource is Spell spell)) return;
					int bit = 1 << spell[SPELL_SCHOOL];
					if ((me[HsTag.SCHOOLS_SEEN] & bit) != 0) return;
					me[HsTag.SCHOOLS_SEEN] = me[HsTag.SCHOOLS_SEEN] | bit;
					int seen = Enumerable.Range(1, 15).Count(x => (me[HsTag.SCHOOLS_SEEN] & (1 << x)) != 0);
					me[GameTag.TAG_SCRIPT_DATA_NUM_1] = Math.Min(seen, needed);
				})
			};
			p.PowerTask = new CustomTask((g, c, s, t, stack) =>
			{
				if (s is IPlayable me && me[GameTag.TAG_SCRIPT_DATA_NUM_1] >= needed) effect.Process(g, c, s, t, stack);
			});
			return p;
		}

		private static void Be(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Assemble (Generic.Assemble: the client's pool, tag 4533;
			// each card's base filter from its own text, sides the base's Cost to 2 more, class or neutral unless it says otherwise)
			bool ClassOrNeutral(Controller c, Card x) => x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL);
			ISimpleTask Assemble(Func<Controller, Card, bool> pool, Action<Game, Controller, IPlayable> after = null,
				Func<Controller, Card, bool> sides = null, Func<Card, Card, bool> sideCost = null) =>
				new CustomTask((g, c, s, t, stack) => Generic.Assemble(c, (IPlayable)s, pool, after, sides, "BE_044e", sideCost));
			// [BE_044] F3TCH3R (3) — Battlecry: Assemble a 4-Cost minion.
			cards.Add("BE_044", new CardDef(new Power { PowerTask = Assemble((c, x) => ClassOrNeutral(c, x) && x.Cost == V("BE_044", "cost", 4)) }));
			// [BE_262] Attentive Tender (1) — Battlecry: Assemble a 1-Cost minion.
			cards.Add("BE_262", new CardDef(new Power { PowerTask = Assemble((c, x) => ClassOrNeutral(c, x) && x.Cost == V("BE_262", "cost", 1)) }));
			// [BE_045] Mismatcher (3) — Taunt. Battlecry: Assemble a minion that costs (3) or less with cards that cost (5) or more.
			cards.Add("BE_045", new CardDef(new Power
			{
				PowerTask = Assemble((c, x) => ClassOrNeutral(c, x) && x.Cost <= V("BE_045", "base", 3), null, null, (b, x) => x.Cost >= V("BE_045", "sides", 5))
			}));
			// [BE_851] Steward of the Vale — Battlecry: Assemble a Beast.
			cards.Add("BE_851", new CardDef(new Power { PowerTask = Assemble((c, x) => ClassOrNeutral(c, x) && x.IsRace(Race.BEAST)) }));
			// [BE_229] Sandcrafter — Battlecry: Assemble a minion with an end of your turn effect.
			cards.Add("BE_229", new CardDef(new Power { PowerTask = Assemble((c, x) => ClassOrNeutral(c, x) && x.Power?.Trigger?.Type == TriggerType.TURN_END && !x.Power.Trigger.EitherTurn) }));
			// [BE_740] Chisel — Assemble a Taunt minion. Give it +2/+3 (BE_740e).
			cards.Add("BE_740", new CardDef(new Power
			{
				PowerTask = Assemble((c, x) => ClassOrNeutral(c, x) && x[GameTag.TAUNT] > 0, (g, c, made) => Give(g, made, made, "BE_740e"))
			}));
			cards.Add("BE_740e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("BE_740", "attack", 2)), Effects.Health_N(V("BE_740", "health", 3))) }));
			// [BE_114] Shadow Sculpt — Assemble a minion. It costs (2) less. (live 28457: GBL_002e, the client's generic Cost - 2)
			cards.Add("BE_114", new CardDef(new Power
			{
				PowerTask = Assemble(ClassOrNeutral, (g, c, made) => Give(g, made, made, GenericCostLess(V("BE_114", "less", 2))))
			}));
			// [BE_571] Fleshshape — Assemble a Deathrattle minion. Give all minions in your hand +2/+2 (BE_571e; the assembled one too).
			cards.Add("BE_571", new CardDef(new Power
			{
				PowerTask = Assemble((c, x) => ClassOrNeutral(c, x) && x[GameTag.DEATHRATTLE] > 0, (g, c, made) =>
				{
					foreach (IPlayable m in c.HandZone.GetAll().Where(x => x is Minion).ToList()) Give(g, made, m, "BE_571e");
				})
			}));
			cards.Add("BE_571e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BE_571", "stats", 2))) }));
			// [BE_087] Darkest Depths — Summon two 0/3 Guards with Taunt (BE_087t). Assemble a minion.
			cards.Add("BE_087", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("BE_087", "guards", 2); i++) Summon(g, c, Cards.FromId("BE_087t"), s);
				}), Assemble(ClassOrNeutral))
			}));
			// [BE_702] Shadowcrafter — Combo: Assemble a minion from another class (its sides too).
			cards.Add("BE_702", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.IsComboActive,
					Assemble((c, x) => Generic.IsFromAnotherClass(c, x), null, (c, x) => Generic.IsFromAnotherClass(c, x)))
			}));
			// [BE_553] Tinker — Assemble a Mech. Get an Overdrive (BE_553t: Give a friendly Mech +2 Attack and Rush. It dies at end of turn).
			cards.Add("BE_553", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(Assemble((c, x) => ClassOrNeutral(c, x) && x.IsRace(Race.MECHANICAL)),
					new CustomTask((g, c, s, t, stack) => GetCard(c, Cards.FromId("BE_553t"), s)))
			}));
			cards.Add("BE_553t", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 },
				{ PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_RACE, (int)Race.MECHANICAL } }, new Power
			{
				PowerTask = new AddEnchantmentTask("BE_553e", EntityType.TARGET)
			}));
			cards.Add("BE_553e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("BE_553", "attack", 2)), Effects.Rush),
				Trigger = new Trigger(TriggerType.TURN_END) { EitherTurn = true, SingleTask = new DestroyTask(EntityType.TARGET) }
			}));
			// [BE_257] Thaw From Ice — Assemble a Mage minion. At the end of your turns, reduce its Cost by (1). (BE_257e on it: a turn-end
			// trigger in the hand giving BE_257e2, Cost -1, each time)
			cards.Add("BE_257", new CardDef(new Power
			{
				PowerTask = Assemble((c, x) => x.HasClass(CardClass.MAGE), (g, c, made) => Give(g, made, made, "BE_257e"))
			}));
			cards.Add("BE_257e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Enchantment e && e.Target is IPlayable card && card.Zone?.Type == Zone.HAND) Give(g, e, card, "BE_257e2");
					})
				}
			}));
			cards.Add("BE_257e2", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("BE_257", "less", 1))) }));
			cards.Add("BE_044e", new CardDef(new Power()));
			// [BE_004] Rajh — Battlecry: Assemble a minion from any class. Transform it into a Passive Hero Power (BE_004p "{0}").
			// Its base pool is curated and not in the client data (no tag marks it): HSReplay's Rajh Assemble Pool page (2026-11-16, "Middle
			// Card Pool: 13 cards") — "After you / Whenever you / At the end of your turn" minions of any class. The sides: any class.
			// Live (session-20261021-013733 128078): the assembled minion stays set aside (Rajh's 4694 ASSEMBLE_TARGET = it); BE_004p
			// is created in play (LINKED_ENTITY = the old Hero Power, set aside), CARDTEXT_ENTITY_0 = the assembled minion; its Power is
			// the assembled one (a trigger, so a Passive).
			var rajhPool = new HashSet<string> { "TTN_843", "CORE_TTN_843", "RLK_083", "CORE_RLK_083", "BE_701", "EX1_559", "CORE_EX1_559",
				"BAR_878", "CORE_BAR_878", "EDR_462", "FIR_955", "BE_997", "TLC_623", "TLC_254", "BE_008", "TIME_100", "CATA_133" };
			cards.Add("BE_004", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.Assemble(c, (IPlayable)s, (cc, x) => rajhPool.Contains(x.Id), null,
					// (its sides from the same list, any Cost: every half stays a trigger, so a Passive — live: Antonidas + Iridescent Flitterwing)
					(cc, x) => rajhPool.Contains(x.Id), null, (b, x) => true, (g2, c2, made, src) =>
					{
						if (src != null) src[(GameTag)4694] = made.Id;
						if (!(Cards.TryFromId("BE_004p") is Card hp)) return;
						Card passive = hp.Clone();
						passive.Power = made.Card.Power;
						new ReplaceHeroPower(passive).Process(g2, c2, src, null);
						c2.Hero.HeroPower[(GameTag)2655] = made.Id; // CARDTEXT_ENTITY_0: the client shows the assembled text
					}))
			}));

			// ---------------------------------------------------------------- Neutral (one first half and one second half each: they are
			// Assemble pool cards too — Generic.AssembleHalves)
			// [BE_008] Mogu Warmaster (3) — After you play a neutral card, your next spell costs (1) less this turn (BE_008e, next-card).
			cards.Add("BE_008", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.Card.HasClass(CardClass.NEUTRAL)),
					SingleTask = new AddEnchantmentTask("BE_008e", EntityType.CONTROLLER)
				}
			}));
			cards.Add("BE_008e", NextCardEffect(Effects.ReduceCost(V("BE_008", "less", 1)), SelfCondition.IsSpell, thisTurnOnly: true));
			// [BE_151] Vaultkeeper (5) — Battlecry and Deathrattle: Give adjacent minions +2/+2 (BE_151e). (dead: the minions beside the slot it
			// left — Minion.LastBoardPosition)
			ISimpleTask vault = new CustomTask((g, c, s, t, stack) =>
			{
				if (!(s is Minion me)) return;
				IEnumerable<Minion> side;
				if (me.Zone?.Type == Zone.PLAY) side = me.GetAdjacentMinions();
				else
				{
					int pos = me.LastBoardPosition;
					var board = c.BoardZone.GetSpan().ToArray();
					side = new[] { pos - 1, pos }.Where(i => i >= 0 && i < board.Length).Select(i => board[i]).Where(m => !m.Untouchable && !(m is Location));
				}
				foreach (Minion m in side.Where(m => !m.IsDead).ToList()) Give(g, s, m, "BE_151e");
			});
			cards.Add("BE_151", new CardDef(new Power { PowerTask = vault, DeathrattleTask = vault }));
			cards.Add("BE_151e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BE_151", "stats", 2))) }));
			// [BE_169] Mantid Precursor (2) — Battlecry: If you have a weapon equipped, gain +1/+1 and Rush (BE_169e).
			cards.Add("BE_169", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.Hero.Weapon == null || !(s is Minion me)) return;
					Give(g, s, me, "BE_169e");
					me.IsRush = true;
				})
			}));
			cards.Add("BE_169e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BE_169", "stats", 1))) }));
			// [BE_207] Unstable Prototype (1) — Reborn. Deathrattle: Give all minions in your hand +1 Attack (BE_207e; +1/+1 before the 2026-11-20 patch).
			cards.Add("BE_207", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable m in c.HandZone.GetAll().Where(p => p is Minion).ToList()) Give(g, s, m, "BE_207e");
				})
			}));
			cards.Add("BE_207e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("BE_207", "attack", 1))) }));
			// [BE_255] Primordial Wyrm (1) — After you play a minion, gain +1 Health (BE_255e).
			cards.Add("BE_255", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me) Give(g, s, me, "BE_255e"); })
				}
			}));
			cards.Add("BE_255e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("BE_255", "health", 1))) }));
			// [BE_701] OC91 Chariot (5) — After you play a minion, draw a card.
			cards.Add("BE_701", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = new CustomTask((g, c, s, t, stack) => Generic.Draw(c)) }
			}));
			// [BE_733] Titanic Thrasher (8) — Rush, Windfury. Costs (1) less for each enemy minion.
			cards.Add("BE_733", new CardDef(new Power { Aura = new AdaptiveCostEffect(p => p.Controller.Opponent.BoardZone.Count(m => !m.Untouchable)) }));
			// [BE_734] Assimilating Ooze (4) — Battlecry:& Summon a copy of this at the end of this turn. (Generic.AtEndOfThisTurn: one effect,
			// so it also works once the Ooze left play and as an Assemble half; the copy keeps its buffs — Generic.Copy)
			ISimpleTask oozeCopy = new CustomTask((g, c, s, t, stack) =>
			{
				if (s is IPlayable me && !c.BoardZone.IsFull) Generic.Copy(c, me, me, Zone.PLAY);
			});
			cards.Add("BE_734", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.AtEndOfThisTurn(c, s, oozeCopy))
			}));
			// [BE_861] Crystalline Golem (4) — Taunt, Reborn. Deathrattle: Restore #3 Health to your hero.
			cards.Add("BE_861", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => c.Hero.TakeHeal((IPlayable)s, V("BE_861", "heal", 3)))
			}));
			// [BE_993] Shadewing (2) — Deathrattle: Reduce the Cost of a random Shadow spell in your hand by (2).
			cards.Add("BE_993", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var shadow = c.HandZone.GetAll().Where(p => p is Spell && p[SPELL_SCHOOL] == SHADOW).ToList();
					if (shadow.Count > 0) GiveCostLess(g, s, shadow[g.Random.Next(shadow.Count)], V("BE_993", "less", 2));
				})
			}));
			// [BE_994] Stone Trogg (8) — Battlecry: Deal 6 damage to a minion. If it survives, gain +6 Health (BE_994e) and Taunt.
			// (Assemble splits it after "If it survives,": the first half aims and deals the damage, the second is the gain — live
			// session-20261022-020357: an Assembled Stone Trogg with another card's end-of-turn first half, played without a target)
			ISimpleTask troggGain = new CustomTask((g, c, s, t, stack) => { if (s is Minion me) { Give(g, s, me, "BE_994e"); me.HasTaunt = true; } });
			Func<ISimpleTask, Power> troggWhen = null;
			troggWhen = then => new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Generic.DamageCharFunc((IPlayable)s, m, V("BE_994", "damage", 6), false);
					if (m.Health > 0 && !m.ToBeDestroyed) then.Process(g, c, s, t, stack);
				}),
				AssembleWhen = then2 => troggWhen(then2), AssembleWhat = troggGain, AssembleWhenAims = true
			};
			cards.Add("BE_994", new CardDef(BattlecryMinion, troggWhen(troggGain)));
			cards.Add("BE_994e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("BE_994", "health", 6))) }));
			// [BE_997] Proto-Pandaren (2) — After a card enters your hand except from this, give both players a random spell.
			cards.Add("BE_997", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ADD_TO_HAND)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t != null && t[GameTag.CREATOR] == s.Id) return; // (except from this)
						foreach (Controller p in new[] { c, c.Opponent })
							if (RandomCard(g, x => x.Type == CardType.SPELL) is Card sp) GetCard(p, sp, s);
					})
				}
			}));
			// [BE_998] Seeker of Prey (7) — Elusive. After this takes damage, destroy a random enemy minion.
			cards.Add("BE_998", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE) { TriggerSource = TriggerSource.SELF, SingleTask = ComplexTask.DestroyRandomTargets(1, EntityType.OP_MINIONS) }
			}));
			// [BE_999] Power Tiger (6) — At the start of EVERY player's turn, attack a random enemy.
			cards.Add("BE_999", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					EitherTurn = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me) || me.IsDead || me.Zone?.Type != Zone.PLAY) return;
						var enemies = Enemies(c).Where(x => !x.IsDead && !(x is Minion { ToBeDestroyed: true })).ToList();
						if (enemies.Count > 0) EffectAttack(g, me, enemies[g.Random.Next(enemies.Count)]);
					})
				}
			}));

			// ---------------------------------------------------------------- C'Thun (Old Gods: Titans with four abilities —
			// Actions/Titans.cs; another card's "Choose a C'Thun ability to trigger": Generic.ChooseOldGodAbility, Actions/OldGods.cs)
			// [BE_013] C'Thun, Eye of the Empire (10) — Old God. If you've triggered all 4 C'Thun abilities this game, this minion's
			// abilities are quadrupled (Generic.CthunTimes; reworded from "tripled" in the 2026-11-03 balance patch). Live (14
			// recordings): the extra action at index 6, the after-ability blocks at 3, 2, 1, 0 for Wrath, Domain, Glare, Servants, the
			// end-of-turn mark reset at 7.
			cards.Add("BE_013", new CardDef(new Power
			{
				OldGodExtraActionIndex = 6,
				TitanAbilityEffectIndexes = new[] { 3, 2, 1, 0 },
				Trigger = Generic.OldGodTurnEnd(7)
			}));
			// [BE_013t1] Wrath of C'Thun — Deal 6 damage randomly split among enemies (24 quadrupled). (ImmuneToSpellpower: no Spell Damage)
			cards.Add("BE_013t1", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = V("BE_013t1", "damage", 6) * Generic.CthunTimes(s); // (before this one counts: live 20261109-160826, the 4th ability isn't tripled)
					Generic.NoteCthunAbility(c, 1);
					for (int i = 0; i < n; i++)
					{
						ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, 1).Process(g, c, s, t, stack);
						g.ProcessTasks();
					}
					Generic.CthunAltText(c);
				})
			}));
			// [BE_013t2] Domain of C'Thun — Give two other random friendly minions +2/+2 (+8/+8 quadrupled: BE_013t2e2).
			cards.Add("BE_013t2", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					string buff = Generic.CthunTimes(s) == 4 ? "BE_013t2e2" : "BE_013t2e1";
					Generic.NoteCthunAbility(c, 2);
					var pool = c.BoardZone.GetAll().Where(m => m != s && !m.IsDead && !m.ToBeDestroyed).ToList();
					for (int i = 0; i < V("BE_013t2", "minions", 2) && pool.Count > 0; i++)
					{
						Minion pick = pool[g.Random.Next(pool.Count)];
						pool.Remove(pick);
						Give(g, s, pick, buff);
					}
					Generic.CthunAltText(c);
				})
			}));
			cards.Add("BE_013t2e1", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("BE_013t2", "atk", 2)), Effects.Health_N(V("BE_013t2", "health", 2))) }));
			cards.Add("BE_013t2e2", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(4 * V("BE_013t2", "atk", 2)), Effects.Health_N(4 * V("BE_013t2", "health", 2))) }));
			// [BE_013t3] Glare of C'Thun — Lifesteal. Deal 5 damage to an enemy minion (20 quadrupled). Live (session-20261106-001808 160678,
			// 20261021-013733 96533): its effect is a second card, BE_013t6 (the Lifesteal one), shown big and triggered at the same target.
			cards.Add("BE_013t3", new CardDef(TargetEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter target) Generic.TriggerAbility(c, s, Cards.FromId("BE_013t6"), target, bigCard: true);
				})
			}));
			cards.Add("BE_013t6", new CardDef(TargetEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int damage = V("BE_013t3", "damage", 5) * Generic.CthunTimes(s);
					Generic.NoteCthunAbility(c, 3);
					if (t is ICharacter target) Generic.DamageCharFunc((IPlayable)s, target, damage, false);
					Generic.CthunAltText(c);
				})
			}));
			// [BE_013t4] Servants of C'Thun — Summon two 2/2 Fanatics with Taunt (8/8 quadrupled).
			cards.Add("BE_013t4", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int times = Generic.CthunTimes(s);
					Generic.NoteCthunAbility(c, 4);
					for (int i = 0; i < V("BE_013t4", "summons", 2) && !c.BoardZone.IsFull; i++)
					{
						var fanatic = (Minion)Entity.FromCard(c, Cards.FromId("BE_013t5"), new EntityData { [GameTag.CREATOR] = s.Id });
						if (times > 1) { fanatic[GameTag.ATK] *= times; fanatic[GameTag.HEALTH] *= times; }
						Generic.SummonBlock.Invoke(g, fanatic, -1, s);
					}
					Generic.CthunAltText(c);
				})
			}));
			// [BE_204] Gargantuan Slitherer (6) — Taunt. Battlecry:& Choose a C'Thun ability to trigger.
			cards.Add("BE_204", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.ChooseOldGodAbility(c, s, Generic.CthunAbilities))
			}));
			// [BE_726] Ritual of C'Thun (1) — In 2 turns, choose a C'Thun ability to trigger. (live: its countdown at index 0)
			cards.Add("BE_726", new CardDef(new Power
			{
				Trigger = Generic.RitualTrigger(0, new CustomTask((g, c, s, t, stack) => Generic.ChooseOldGodAbility(c, s, Generic.CthunAbilities)))
			}));
			// [BE_778] Watch Over (6) — Choose two different C'Thun abilities to trigger. (live 20261021-013733 7295 / 7425: the second
			// choice offers the three left)
			cards.Add("BE_778", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.ChooseOldGodAbility(c, s, Generic.CthunAbilities, V("BE_778", "abilities", 2)))
			}));
			// [BE_014] Spawn of C'Thun (4) — Battlecry: If C'Thun started in your deck, choose one of its abilities to trigger.
			cards.Add("BE_014", new CardDef(new Power
			{
				GlowWhile = p => p.Controller.DeckCards.Any(x => x.Id == "BE_013"),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.DeckCards.Any(x => x.Id == "BE_013")) Generic.ChooseOldGodAbility(c, s, Generic.CthunAbilities);
				})
			}));
			// [BE_012] Dark Deacon (3) — Battlecry: If you've cast a Shadow spell while holding this, choose a C'Thun ability to trigger.
			// (counted in hand: WhileHolding; "({0} left!)" reads the count)
			cards.Add("BE_012", new CardDef(new Power
			{
				Trigger = WhileHolding(TriggerType.CAST_SPELL, p => p[SPELL_SCHOOL] == SHADOW),
				ThresholdValue = V("BE_012", "spells", 1), // (2026-11-20 patch: "a Shadow spell", was 2)
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (Generic.ThresholdMet((IPlayable)s)) Generic.ChooseOldGodAbility(c, s, Generic.CthunAbilities);
				})
			}));
			// [BE_771] Tentacle Lash (5) — After your hero attacks, reduce the Cost of C'Thun by (1). Choose a C'Thun ability to trigger.
			// (user, 2026-11-20: a player-wide discount, not a one-time enchant to whatever C'Thun is held when it's cast — a C'Thun
			// that enters hand or deck later still carries the full accumulated discount; one already on the board keeps its printed
			// Cost. HsTag.TENTACLE_LASH_CTHUN_DISCOUNT counts the attacks; every C'Thun gets caught up to it on arrival.)
			void CatchUpCthunDiscount(Game g, Controller c, IEntity s, IPlayable cthun)
			{
				for (int i = 0; i < c[HsTag.TENTACLE_LASH_CTHUN_DISCOUNT]; i++) Give(g, s, cthun, "GBL_003e");
			}
			cards.Add("BE_771", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.AFTER_ATTACK)
					{
						TriggerSource = TriggerSource.HERO,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							c[HsTag.TENTACLE_LASH_CTHUN_DISCOUNT]++;
							foreach (IPlayable cthun in c.HandZone.GetAll().Concat(c.DeckZone.GetAll()).Where(p => p.Card.Id == "BE_013").ToList())
								Give(g, s, cthun, "GBL_003e");
							Generic.ChooseOldGodAbility(c, s, Generic.CthunAbilities);
						})
					},
					new Trigger(TriggerType.ADD_TO_HAND)
					{
						TriggerSource = TriggerSource.FRIENDLY,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (t is IPlayable cthun && cthun.Card.Id == "BE_013") CatchUpCthunDiscount(g, c, s, cthun);
						})
					},
					new Trigger(TriggerType.SHUFFLE_INTO_DECK)
					{
						TriggerSource = TriggerSource.FRIENDLY,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (t is IPlayable cthun && cthun.Card.Id == "BE_013") CatchUpCthunDiscount(g, c, s, cthun);
						})
					}
				)
			}));

			// ---------------------------------------------------------------- Yogg-Saron (an Old God)
			// [BE_009] Mastermind Yogg-Saron (9) — Old God. Battlecry: End your turn. Yogg-saron takes an extra turn for you. (Once per game)
			// (Generic.YoggTakesTurn: the server's AI plays that turn — it plays for lethal like it does for the Innkeeper. Live: the extra
			// action at 11, the after-ability blocks at 0-3, the mark reset at 10.)
			cards.Add("BE_009", new CardDef(new Power
			{
				OldGodExtraActionIndex = 11,
				Trigger = Generic.OldGodTurnEnd(10),
				GlowWhile = p => p.Controller[HsTag.YOGG_TURN] == 0,
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.YoggTakesTurn(c, (IPlayable)s))
			}));
			cards.Add("BE_009e2", new CardDef(new Power { Trigger = Generic.YoggTurnEnds }));
			// [BE_009t1] Brain of Yogg-Saron — Your next spell this turn casts twice. (BE_009t1e on the player: a one-turn charge)
			cards.Add("BE_009t1", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.AddEnchantmentBlock(g, Cards.FromId("BE_009t1e"), (IPlayable)s, c))
			}));
			cards.Add("BE_009t1e", new CardDef(new Power { NextSpellCastsTwice = true, Enchant = new Enchant() { IsOneTurnEffect = true } }));
			// [BE_009t2] Madness of Yogg-Saron — Deal 10 damage. (live 20261110-153646: aimed, a Fireball missile from Yogg-Saron)
			cards.Add("BE_009t2", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter target) Generic.DamageCharFunc((IPlayable)s, target, V("BE_009t2", "damage", 10), false);
				})
			}));
			// [BE_009t3] Ascent of Yogg-Saron — Transform spells in your hand into ones that cost (3) more. (They keep their original Cost.)
			// (Deck of Lunacy's way, in hand: BE_009t3e keeps the old Cost)
			cards.Add("BE_009t3", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable spell in c.HandZone.Where(p => p is Spell).ToList())
					{
						int cost = spell.Cost;
						if (!(RandomCard(g, x => x.Type == CardType.SPELL && x.Cost == Math.Min(10, cost + V("BE_009t3", "more", 3))) is Card card)) continue;
						IPlayable changed = Generic.ChangeEntityBlock(c, spell, card, true);
						Generic.AddEnchantmentBlock(g, Cards.FromId("BE_009t3e"), (IPlayable)s, changed, cost, 0, 0);
					}
				})
			}));
			cards.Add("BE_009t3e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) { UseScriptTag = true } }));
			// [BE_009t4] Domain of Yogg-Saron — Draw 5 spells.
			cards.Add("BE_009t4", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p is Spell, V("BE_009t4", "draws", 5)))
			}));

			// ---------------------------------------------------------------- N'Zoth (an Old God)
			// [BE_017] N'Zoth, Dream's End (15) — Old God. Costs (1) less for each Deathrattle minion you've summoned this game.
			// (Controller.SummonedThisGame. Live: the extra action at 5, the after-ability blocks at 1, 3, 0, 2 for its abilities in their
			// order t5, t4, t3, t6, the mark reset at 6.)
			cards.Add("BE_017", new CardDef(new Power
			{
				OldGodExtraActionIndex = 5,
				TitanAbilityEffectIndexes = new[] { 1, 3, 0, 2 },
				Trigger = Generic.OldGodTurnEnd(6),
				Aura = new AdaptiveCostEffect(p => p.Controller.SummonedThisGame.Count(x => x.Type == CardType.MINION && x[GameTag.DEATHRATTLE] > 0))
			}));
			// [BE_017t5] Army of N'Zoth — Resurrect 4 different Deathrattle minions. (friendly ones that died: Tyr's Tears' way)
			cards.Add("BE_017t5", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var pool = c.DiedThisGameCards.Where(p => p is Minion && p.Card[GameTag.DEATHRATTLE] > 0).Select(p => p.Card).GroupBy(x => x.Id).Select(x => x.First()).ToList();
					foreach (Card card in DiscoverFromTask.PickDistinct(g, pool, V("BE_017t5", "minions", 4)))
					{
						if (c.BoardZone.IsFull) return;
						Generic.SummonBlock.Invoke(g, (Minion)Entity.FromCard(c, card), -1, s);
					}
				})
			}));
			// [BE_017t4] Mastery of N'Zoth — Trigger another friendly minion's Deathrattle twice. (its own Deathrattle: TriggerDeathrattleOf)
			cards.Add("BE_017t4", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_NONSELF_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_DEATHRATTLE, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					for (int i = 0; i < V("BE_017t4", "times", 2); i++) Generic.TriggerDeathrattleOf(s, m);
				})
			}));
			// [BE_017t3] Call of N'Zoth — Discover a Deathrattle minion that costs (5) or more to summon. (Story of Umbra's pool and summon)
			cards.Add("BE_017t3", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x[GameTag.DEATHRATTLE] > 0 && x.Cost >= V("BE_017t3", "cost", 5),
					afterPick: new CustomTask((g, c, s, t, stack) =>
					{
						if (t == null || t.Zone?.Type != Zone.HAND || c.BoardZone.IsFull) return;
						var m = (Minion)t;
						c.HandZone.Remove(m);
						Generic.SummonBlock.Invoke(g, m, -1, s);
					}))
			}));
			// [BE_017t6] Domain of N'Zoth — Give another friendly minion "Deathrattle: Deal 2 damage to all enemy minions." (BE_017t6e)
			cards.Add("BE_017t6", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_NONSELF_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m) Give(g, s, m, "BE_017t6e"); })
			}));
			cards.Add("BE_017t6e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.DEATHRATTLE, EffectOperator.SET, 1)),
				DeathrattleTask = new DamageTask(V("BE_017t6", "damage", 2), EntityType.OP_MINIONS)
			}));

			// ---------------------------------------------------------------- Y'Shaarj (an Old God)
			// [BE_784] Y'Shaarj, Titan Bane (10) — Old God. Battlecry: If you've spent 30 Mana on minions this game, gain Immune for 1 turn.
			// (the Mana each minion was played for — PlayHistory, before this one; its "({0} left!)" number: TAG_SCRIPT_DATA_NUM_1 30. Live:
			// the mark in NUM_4, the extra action at 11, the after-ability blocks at 0-3, the mark reset at 10.)
			// (live 20261110-200615 77608: NUM_1 is the Mana still needed — 0 when ready; kept up to date in hand and deck after each
			// friendly minion is played. Ready: that counter at 0, or the play history says so.)
			int YshaarjSpent(Controller c, IEntity self) => c.PlayHistory.Where(h => h.SourceCard?.Type == CardType.MINION && h.SourceId != self.Id).Sum(h => h.Cost);
			bool YshaarjReady(IPlayable p) => p[GameTag.TAG_SCRIPT_DATA_NUM_1] == 0 || YshaarjSpent(p.Controller, p) >= V("BE_784", "mana", 30);
			cards.Add("BE_784", new CardDef(new Power
			{
				OldGodExtraActionIndex = 11,
				OldGodMarkTag = (GameTag)2919, // (TAG_SCRIPT_DATA_NUM_4)
				Trigger = new MultiTrigger(Generic.OldGodTurnEnd(10), new Trigger(TriggerType.PLAY_MINION)
				{
					TriggerActivation = TriggerActivation.HAND_OR_DECK,
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is IPlayable me)) return;
						int left = Math.Max(0, V("BE_784", "mana", 30) - YshaarjSpent(c, me));
						if (me[GameTag.TAG_SCRIPT_DATA_NUM_1] != left) me[GameTag.TAG_SCRIPT_DATA_NUM_1] = left;
					})
				}),
				GlowWhile = YshaarjReady,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (s is Minion me && YshaarjReady(me)) Give(g, s, me, "BE_784e");
				})
			}));
			// (Immune until its owner's next turn: Time Out!'s way)
			cards.Add("BE_784e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Immune),
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = RemoveEnchantmentTask.Task }
			}));
			// [BE_784t1] Gaze of Y'Shaarj — Force all enemy minions to attack this. (Mythical Terror's way: each in turn, while it stands)
			cards.Add("BE_784t1", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion yshaarj)) return;
					EventMetaData temp = g.CurrentEventData;
					foreach (Minion enemy in c.Opponent.BoardZone.ToArray())
					{
						if (yshaarj.IsDead || yshaarj.ToBeDestroyed || yshaarj.Zone?.Type != Zone.PLAY) break;
						if (enemy.IsDead || enemy.ToBeDestroyed || enemy.AttackDamage <= 0) continue;
						Generic.AttackBlock.Invoke(c.Opponent, enemy, yshaarj, true, true);
					}
					g.DeathProcessingAndAuraUpdate();
					g.CurrentEventData = temp;
				})
			}));
			// [BE_784t3] Might of Y'Shaarj — Gain the stats of adjacent minions. (BE_784t2e "Mighty" carries the numbers)
			cards.Add("BE_784t3", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					var near = me.GetAdjacentMinions().Where(m => !m.IsDead).ToList();
					int atk = near.Sum(m => m.AttackDamage), health = near.Sum(m => m.Health);
					if (atk > 0 || health > 0) Generic.AddEnchantmentBlock(g, Cards.FromId("BE_784t2e"), me, me, atk, health);
				})
			}));
			cards.Add("BE_784t2e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0), Effects.Health_N(0)) { UseScriptTag = true } }));
			// [BE_784t2] Spawn of Y'Shaarj — Summon a random minion that costs (8) or more. Get a copy of it.
			cards.Add("BE_784t2", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(RandomCard(g, x => x.Type == CardType.MINION && x.Cost >= V("BE_784t2", "cost", 8)) is Card big)) return;
					Summon(g, c, big, s);
					IPlayable copy = Entity.FromCard(c, big, new EntityData { [GameTag.CREATOR] = s.Id });
					if (!c.HandZone.IsFull) Generic.AddHandPhase.Invoke(c, copy);
				})
			}));
			// [BE_784t4] Domain of Y'Shaarj — Your next card costs (5) less. (BE_784t4e on the player; the cards show the generic Cost - 5)
			cards.Add("BE_784t4", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.AddEnchantmentBlock(g, Cards.FromId("BE_784t4e"), (IPlayable)s, c))
			}));
			cards.Add("BE_784t4e", NextCardCost(Effects.ReduceCost(V("BE_784t4", "less", 5)), new SelfCondition(p => true), "GBL_011e"));

			// ---------------------------------------------------------------- Fetid Devourer (Death Knight)
			// [BE_781] Fetid Devourer (7) — While in your hand, this transforms after you play a card with a rune. Wiki (Fetid Devourer):
			// "gains the Runes of the most recently-played card. It does not stack Runes from multiple cards" — its form (BE_781t1-t19) is
			// the one whose runes (COST_BLOOD / FROST / UNHOLY) equal that card's; every form keeps transforming. A form's Battlecry, in its
			// text's order: Blood N — destroy N enemy minions (live: the first is the play target, the rest TARGET choices: 20261110-200615
			// "EntityChoices type=3" after the play); Frost N — draw N cards, they cost (3) less; Unholy N — summon two random 3N-Cost minions.
			var fetidForms = new List<string> { "BE_781" }; // (+ its forms: the minions among its RelatedCards)
			fetidForms.AddRange((SabberStoneCore.Loader.ReprintCards.RelatedCardIds("BE_781") ?? new List<string>()).Where(id => Cards.TryFromId(id)?.Type == CardType.MINION && id != "BE_781"));
			Trigger FetidTransforms() => new Trigger(TriggerType.AFTER_PLAY_CARD)
			{
				TriggerActivation = TriggerActivation.HAND,
				TriggerSource = TriggerSource.FRIENDLY,
				SingleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is IPlayable me) || me.Zone?.Type != Zone.HAND || !(t is IPlayable played) || played == me) return;
					int b = played.Card[COST_BLOOD], f = played.Card[COST_FROST], u = played.Card[COST_UNHOLY];
					if (b + f + u == 0) return;
					Card form = fetidForms.Skip(1).Select(Cards.TryFromId).FirstOrDefault(x => x != null &&
						x[COST_BLOOD] == b && x[COST_FROST] == f && x[COST_UNHOLY] == u);
					if (form != null && form.Id != me.Card.Id) Generic.ChangeEntityBlock(c, me, form, true);
				})
			};
			ISimpleTask FetidBattlecry(Card form) => new CustomTask((g, c, s, t, stack) =>
			{
				int b = form[COST_BLOOD], f = form[COST_FROST], u = form[COST_UNHOLY];
				if (b > 0)
				{
					if (t is Minion first && first.Controller != c) Generic.Destroy(first, s);
					int more = t is Minion ? b - 1 : b;
					if (more > 0)
						Generic.PickTargets(c, s, more, ch => ch is Minion m && m.Controller == c.Opponent && m != t && !m.ToBeDestroyed && !m.Untouchable,
							(g2, c2, s2, target) => Generic.Destroy((Minion)target, s2));
				}
				for (int i = 0; i < f; i++)
					if (Generic.Draw(c) is IPlayable drawn && drawn.Zone?.Type == Zone.HAND) GiveCostLess(g, s, drawn, V("BE_781", "less", 3));
				for (int i = 0; i < (u > 0 ? V("BE_781", "summons", 2) : 0); i++)
					if (RandomCard(g, x => x.Type == CardType.MINION && x.Cost == V("BE_781", "cost", 3) * u) is Card m) Summon(g, c, m, s);
			});
			foreach (string id in fetidForms)
			{
				Card form = Cards.TryFromId(id);
				bool aims = form != null && form[COST_BLOOD] > 0;
				var reqs = aims ? new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } } : new Dictionary<PlayReq, int>();
				cards.Add(id, new CardDef(reqs, new Power
				{
					Trigger = FetidTransforms(),
					PowerTask = form == null || id == "BE_781" ? null : FetidBattlecry(form)
				}));
			}

			// [BE_780] Uldir Quarantine (3, Location) — Locked until you play a card from each Rune. Summon a random 5-Cost minion. (Location
			// .RuneLocked: the pips 4948-4950 set by the runes of each friendly card played while this is held or on the board (user,
			// 2026-11-20, from play: tracks while it's still in hand; one card can cover several Runes at once) — wiki: "playing another
			// Uldir Quarantine will immediately unlock the first Uldir Quarantine on the board"; not itself.)
			cards.Add("BE_780", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					TriggerActivation = TriggerActivation.HAND_OR_PLAY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Location gate) || !(t is IPlayable played) || played == gate) return;
						foreach (var (rune, pip) in new[] { (COST_BLOOD, (GameTag)4948), (COST_FROST, (GameTag)4949), (COST_UNHOLY, (GameTag)4950) })
							if (played.Card[rune] > 0 && gate[pip] == 0) gate[pip] = 1;
					})
				},
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (RandomCard(g, x => x.Type == CardType.MINION && x.Cost == V("BE_780", "cost", 5)) is Card m) Summon(g, c, m, s);
				})
			}));

			// [BE_250] Discs of Norgannon (Mage) — Move all spells in your deck to the top. You can pick to draw from the top or bottom each
			// turn. Live (session-20261021-235213 12374-12397, 14785): a deck effect, then "Shift the Discs" (BE_250e) on the hero for the
			// rest of the game; at the start of each of its owner's turns its TRIGGER block offers Leave the Discs (top) / Shift the Discs
			// (bottom), both to the graveyard, the pick named (HISTORY_TARGET); then the turn's draw comes from that end (HsTag.DRAW_BOTTOM_TURN).
			cards.Add("BE_250", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					// (the spells keep their order among themselves, the rest under them)
					foreach (IPlayable spell in c.DeckZone.GetAll().Where(p => p is Spell).ToList())
					{
						c.DeckZone.Remove(spell);
						c.DeckZone.Add(spell);
					}
					if (c.Hero.AppliedEnchantments?.Any(e => e.Card.Id == "BE_250e") != true) Give(g, s, c.Hero, "BE_250e");
				})
			}));
			cards.Add("BE_250e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					InHistory = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (c.DeckZone.Count == 0) return;
						Generic.CreateChoiceEntities(c, s, OfferCards(c, s, new[] { "BE_250t", "BE_250t2" }), ChoiceAction.STACK,
							new CustomTask((g2, c2, s2, picked, st2) =>
							{
								if (picked?.Card.Id == "BE_250t2") c2[HsTag.DRAW_BOTTOM_TURN] = g2.Turn;
							}));
					})
				}
			}));
			cards.Add("BE_250t", new CardDef(new Power { NoScript = true }));  // (an option: Discs of Norgannon's pick)
			cards.Add("BE_250t2", new CardDef(new Power { NoScript = true }));

			// [BE_588] Mimiron (Hunter, 1) — Battlecry: If you control a Mech, Discover the perfect tech card. The pool: 34 tech cards
			// (discoverstone.dev/cards/BE_588, Reddit via x.com/SidisiTV; every one live offered is in it — 20261021-194927, 20261110-153646,
			// 20261113-174307: EX1_085, AV_126, DRG_401, LOOT_111, ICC_701, EX1_002, REV_023, TRL_514; live offers the original ids). "Perfect":
			// DOUBT — not documented (Reddit: "if the opponent has a high-value deathrattle minion, Mimiron will offer a silence option"); ours
			// offers three whose situation applies (each card's own text), then others at random. RESEARCH/live-checks.md.
			bool Enemy(Controller c, Func<Minion, bool> m) => c.Opponent.BoardZone.GetAll().Any(x => !(x is Location) && m(x));
			bool HasText(Minion m) => m.Card.Power?.DeathrattleTask != null || m.Card.Power?.Trigger != null || m.Card.Power?.Aura != null || m.HasTaunt || m.HasDivineShield || m.HasDeathrattle;
			int EnemyMinions(Controller c) => c.Opponent.BoardZone.GetAll().Count(x => !(x is Location));
			var techs = new (string Id, Func<Controller, bool> Fits)[]
			{
				("EX1_544", c => c.Opponent.SecretZone.Any(x => x.IsSecret) || Enemy(c, m => m.HasStealth)), // Flare
				("NEW1_017", c => Enemy(c, m => m.IsRace(Race.MURLOC))), // Hungry Crab
				("DRG_401", c => false), // Grizzled Wizard
				("EX1_066", c => c.Opponent.Hero.Weapon != null), // Acidic Swamp Ooze
				("FP1_017", c => c.Opponent.HandZone.Count(p => p[GameTag.BATTLECRY] > 0) >= 2), // Nerub'ar Weblord
				("LOOT_111", c => Enemy(c, m => m.AttackDamage <= 1)), // Scorp-o-matic
				("SCH_713", c => c.Opponent.HandZone.Count(p => p is Spell) >= 2), // Cult Neophyte
				("TRL_514", c => EnemyMinions(c) >= 2), // Belligerent Gnome
				("UNG_807", c => Enemy(c, m => m.IsRace(Race.PIRATE))), // Golakka Crawler
				("AV_126", c => EnemyMinions(c) >= 2 && Enemy(c, m => m.Health == 1)), // Bunker Sergeant
				("AV_138", c => Enemy(c, m => m.Card.Rarity == Rarity.LEGENDARY)), // Grimtotem Bounty Hunter
				("CS2_203", c => Enemy(c, HasText)), // Ironbeak Owl
				("DED_523", c => Enemy(c, m => m.IsRace(Race.BEAST) || m.IsRace(Race.PIRATE))), // Golakka Glutton
				("DRG_068", c => false), // Living Dragonbreath
				("DRG_071", c => false), // Bad Luck Albatross
				("EX1_590", c => c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Any(m => m.HasDivineShield)), // Blood Knight
				("LOOT_132", c => Enemy(c, m => m.IsRace(Race.DRAGON))), // Dragonslayer
				("REV_023", c => c.Opponent.BoardZone.GetAll().Any(x => x is Location)), // Demolition Renovator
				("REV_378", c => c.Opponent.HandZone.Count(p => p is Minion) >= 2), // Forensic Duster
				("TSC_926", c => c.Opponent.BoardZone.GetAll().Count(m => !(m is Location) && HasText(m)) >= 2), // Smothering Starfish
				("AV_135", c => false), // Stormpike Marshal
				("EX1_002", c => Enemy(c, m => m.HasTaunt)), // The Black Knight
				("EX1_005", c => Enemy(c, m => m.AttackDamage >= 7)), // Big Game Hunter
				("OG_254", c => c.Opponent.SecretZone.Any(x => x.IsSecret)), // Eater of Secrets
				("BOT_540", c => Enemy(c, m => m.IsRace(Race.MECHANICAL))), // E.M.P. Operative
				("BT_729", c => c.Opponent.BoardZone.GetAll().Where(m => !(m is Location) && m[GameTag.CARDRACE] > 0)
					.GroupBy(m => m[GameTag.CARDRACE]).Any(r => r.Count() >= 2)), // Waste Warden
				("CFM_652", c => EnemyMinions(c) >= 3), // Second-Rate Bruiser
				("DRG_069", c => c.Opponent.Hero.Armor >= 5), // Platebreaker
				("EX1_085", c => EnemyMinions(c) >= 4), // Mind Control Tech
				("GIL_623", c => c.Opponent.HandZone.Count <= 3), // Witchwood Grizzly
				("REV_946", c => c.Opponent.DeckZone.GetAll().Any(Generic.DidntStartInDeck)), // Steamcleaner
				("GIL_124", c => c.Opponent.BoardZone.GetAll().Count(m => !(m is Location) && m.AttackDamage <= 2) >= 2), // Mossy Horror
				("ICC_701", c => c.Opponent.HandZone.GetAll().Concat(c.Opponent.DeckZone.GetAll()).Count(p => p is Spell && p.Card.Cost == 1) >= 2), // Skulking Geist
				("GVG_121", c => c.Opponent.HandZone.Count >= 6), // Clockwork Giant
			};
			cards.Add("BE_588", new CardDef(new Power
			{
				GlowWhile = p => p.Controller.BoardZone.GetAll().Any(m => m != p && m.IsRace(Race.MECHANICAL)),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!c.BoardZone.GetAll().Any(m => m != s && m.IsRace(Race.MECHANICAL))) return;
					new DiscoverFromTask((g2, c2, s2) =>
					{
						var fits = techs.Where(x => x.Fits(c2)).Select(x => Cards.TryFromId(x.Id)).Where(x => x != null).ToList();
						var rest = techs.Select(x => Cards.TryFromId(x.Id)).Where(x => x != null && !fits.Contains(x)).ToList();
						var offer = DiscoverFromTask.PickDistinct(g2, fits, 3);
						offer.AddRange(DiscoverFromTask.PickDistinct(g2, rest, 3 - offer.Count));
						return offer;
					}).Process(g, c, s, t, stack);
				})
			}));

			// ---------------------------------------------------------------- Decipher, Bonus Effect
			// [BE_327] Tome Translator (4) — Battlecry: ??? (Deciphers in 2 turns.) (Generic.DecipherTrigger: in hand it counts down at the
			// start of its owner's turns, then becomes one of its four versions; played before that: a random version's Battlecry, then it becomes it)
			cards.Add("BE_327", new CardDef(new Power { Trigger = Generic.DecipherTrigger(), PowerTask = Generic.UndecipheredBattlecry }));
			// [BE_327t] (N'Zoth's) — Battlecry: Summon two random 2-Cost Deathrattle minions.
			cards.Add("BE_327t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("BE_327t", "summons", 2); i++)
						if (RandomCard(g, x => x.Type == CardType.MINION && x[GameTag.DEATHRATTLE] > 0 && x.Cost == V("BE_327t", "cost", 2)) is Card dr)
							Summon(g, c, dr, s);
				})
			}));
			// [BE_327t1] (Yogg-Saron's) — Battlecry: The next spell you cast this turn costs (4) less. (BE_327te "Yogg Scripture" on the
			// player, a one-turn enchantment; the spells in hand show the generic Cost - 4)
			cards.Add("BE_327t1", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.AddEnchantmentBlock(g, Cards.FromId("BE_327te"), (IPlayable)s, c))
			}));
			cards.Add("BE_327te", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("BE_327t1", "less", 4))) { Condition = SelfCondition.IsSpell, ShowAs = "GBL_008e" },
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.PLAY_CARD) { TriggerSource = TriggerSource.FRIENDLY, Condition = SelfCondition.IsSpell, SingleTask = RemoveEnchantmentTask.Task },
					new Trigger(TriggerType.TURN_END) { SingleTask = RemoveEnchantmentTask.Task })
			}));
			// [BE_327t2] (C'Thun's) — Battlecry: Deal 6 damage randomly split among all other minions.
			cards.Add("BE_327t2", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("BE_327t2", "damage", 6); i++)
					{
						var pool = c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => m != s && !m.IsDead && !m.ToBeDestroyed && !(m is Location)).ToList();
						if (pool.Count == 0) return;
						Generic.DamageCharFunc((IPlayable)s, pool[g.Random.Next(pool.Count)], 1, false);
						g.ProcessTasks();
					}
				})
			}));
			// [BE_327t3] (Y'Shaarj's) — Battlecry: Gain the stats of a random adjacent minion. (BE_327te2 "Y'Shaarj Scripture" carries them)
			cards.Add("BE_327t3", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					var near = me.GetAdjacentMinions().Where(m => !m.IsDead).ToList();
					if (near.Count == 0) return;
					Minion pick = near[g.Random.Next(near.Count)];
					Generic.AddEnchantmentBlock(g, Cards.FromId("BE_327te2"), me, me, pick.AttackDamage, pick.Health);
				})
			}));
			cards.Add("BE_327te2", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0), Effects.Health_N(0)) { UseScriptTag = true } }));
			// [BE_996] Frontline Ironborn (5) — After an enemy minion dies on your turn,& gain +2/+2 and a random Bonus Effect. (Generic's
			// Bonus Effects: one it doesn't have, the wiki's Rush rule)
			cards.Add("BE_996", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerSource = TriggerSource.OP_MINIONS,
					Condition = new SelfCondition(p => p.Game.CurrentPlayer == p.Controller.Opponent),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me) || me.IsDead) return;
						Give(g, s, me, "BE_996e");
						Generic.GainBonusEffects(g, me, 1);
					})
				}
			}));
			cards.Add("BE_996e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("BE_996", "stats", 2)), Effects.Health_N(V("BE_996", "stats", 2))) }));

			// ---------------------------------------------------------------- Warlock (Rituals: Generic.RitualTrigger — an Objective
			// in the Secret zone that ticks at the start of its owner's turns; its effect index is live's per card)
			// [BE_005] Ritual of Excess (3) — In 2 turns, draw 2 cards. Reduce their Costs by (2).
			cards.Add("BE_005", new CardDef(new Power
			{
				Trigger = Generic.RitualTrigger(3, new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("BE_005", "draws", 2); i++)
						if (Generic.Draw(c) is IPlayable drawn && drawn.Zone?.Type == Zone.HAND) GiveCostLess(g, s, drawn, V("BE_005", "less", 2));
				}))
			}));
			// [BE_085] Ritual of Summoning (2) — In 2 turns, summon two 3/5 C'thraxxi with Taunt.
			cards.Add("BE_085", new CardDef(new Power
			{
				Trigger = Generic.RitualTrigger(2, new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("BE_085", "summons", 2); i++) Summon(g, c, Cards.FromId("BE_085t"), s);
				}))
			}));
			// [BE_090] Kith'ix (6) — Battlecry: If you've completed a Ritual this game, choose a Sinister Ritual to begin. (live
			// 20261021-194927 144241: a choice of the three, the pick begins in the Secret zone as a new entity)
			cards.Add("BE_090", new CardDef(new Power
			{
				GlowWhile = p => p.Controller[Generic.RITUALS_COMPLETED] > 0,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c[Generic.RITUALS_COMPLETED] == 0) return;
					Generic.CreateChoiceEntities(c, s, OfferCards(c, s, new[] { "BE_090t1", "BE_090t2", "BE_090t3" }), ChoiceAction.STACK,
						new CustomTask((g2, c2, s2, picked, st2) => { if (picked != null) Generic.BeginRitual(c2, s2, picked.Card); }));
				})
			}));
			// [BE_090t1] Ritual of Famine (3) — In 2 turns, draw 5 cards from your opponent's deck. (a full hand burns it: Doommaiden's;
			// the Sinister Rituals' effect index isn't recorded: 0)
			cards.Add("BE_090t1", new CardDef(new Power
			{
				Trigger = Generic.RitualTrigger(0, new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("BE_090t1", "draws", 5); i++)
					{
						if (c.Opponent.DeckZone.Count == 0) return;
						IPlayable top = c.Opponent.DeckZone.TopCard;
						if (c.HandZone.IsFull) { Generic.Destroy(top, s); continue; }
						if (Generic.GiveTo(top, c, Zone.HAND) is IPlayable mine) Generic.AddHandPhase.Invoke(c, mine);
					}
				}))
			}));
			// [BE_090t2] Ritual of Subjugation (3) — In 2 turns, summon three random Legendary minions. Get a 1-Cost copy of each.
			cards.Add("BE_090t2", new CardDef(new Power
			{
				Trigger = Generic.RitualTrigger(0, new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("BE_090t2", "summons", 3); i++)
					{
						if (!(RandomCard(g, x => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY) is Card legend)) return;
						Summon(g, c, legend, s);
						IPlayable copy = Entity.FromCard(c, legend, new EntityData { [GameTag.CREATOR] = s.Id });
						if (!c.HandZone.IsFull && Generic.AddHandPhase.Invoke(c, copy)) Give(g, s, copy, "BE_090e2");
					}
				}))
			}));
			cards.Add("BE_090e2", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(V("BE_090t2", "cost", 1))) }));
			// [BE_090t3] Ritual of Conquest (3) — In 2 turns, give your minions +6/+6 (wherever they are): board, hand and deck.
			cards.Add("BE_090t3", new CardDef(new Power
			{
				Trigger = Generic.RitualTrigger(0, new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable m in c.BoardZone.GetAll().Cast<IPlayable>().Concat(c.HandZone.GetAll()).Concat(c.DeckZone.GetAll()).Where(p => p is Minion).ToList())
						Give(g, s, m, "BE_090e1");
				}))
			}));
			cards.Add("BE_090e1", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("BE_090t3", "stats", 6)), Effects.Health_N(V("BE_090t3", "stats", 6))) }));
			// [BE_086] Blasphemy (3) — Deal 2 damage to all enemies. If this is the highest Cost card in your hand, do it again. (as it
			// is cast: no card left in hand costs more — a tie still counts; RESEARCH/live-checks.md)
			// (user 2026-11-20: a tie is not "the highest" — the card doesn't glow; strictly more than every other card in hand)
			cards.Add("BE_086", new CardDef(new Power
			{
				GlowWhile = p => p.Zone?.Type == Zone.HAND && p.Controller.HandZone.GetAll().All(x => x == p || x.Cost < p.Cost),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					bool highest = c.HandZone.GetAll().All(x => x.Cost < PlayedCost((IPlayable)s));
					new DamageTask(V("BE_086", "damage", 2), EntityType.ENEMIES, true).Process(g, c, s, t, stack);
					g.DeathProcessingAndAuraUpdate();
					if (highest) new DamageTask(V("BE_086", "damage", 2), EntityType.ENEMIES, true).Process(g, c, s, t, stack);
				})
			}));
			// [BE_093] Mindrot Subjugator (5) — Battlecry: Transform adjacent minions into a copy of this. (board to board: the copy
			// keeps this one's enchantments — Replicat-o-tron's TransformCopyTask, user 2026-10-05)
			cards.Add("BE_093", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					foreach (Minion n in me.GetAdjacentMinions().Where(m => !m.IsDead && !m.ToBeDestroyed).ToList())
						new TransformCopyTask().Process(g, n.Controller, n, me);
				})
			}));
			// [BE_094] Floating Lessons (2) — Give a minion and a random one in your hand stats equal to double this card's Cost
			// (its Cost as it is cast; BE_094e carries the number).
			cards.Add("BE_094", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = V("BE_094", "times", 2) * Math.Max(0, PlayedCost((IPlayable)s)); // (the Cost it was played for: live 194927 147292, 2 in hand with Mawman Cultist, +4)
					var inHand = c.HandZone.GetAll().Where(p => p is Minion).ToList();
					if (t is Minion m) Generic.AddEnchantmentBlock(g, Cards.FromId("BE_094e"), (IPlayable)s, m, n, n);
					if (inHand.Count > 0) Generic.AddEnchantmentBlock(g, Cards.FromId("BE_094e"), (IPlayable)s, inHand[g.Random.Next(inHand.Count)], n, n);
				})
			}));
			cards.Add("BE_094e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0), Effects.Health_N(0)) { UseScriptTag = true } }));
			// [BE_728] Zakajz the Corruptor (5) — Battlecry: Reduce the Cost of minions in your hand and deck by (1) and increase the
			// Cost of spells by (1) (the client's generic Cost - 1 / Cost + 1).
			cards.Add("BE_728", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = V("BE_728", "cost", 1);
					foreach (IPlayable p in c.HandZone.GetAll().Concat(c.DeckZone.GetAll()).Where(p => p != s).ToList())
					{
						if (p is Minion) for (int i = 0; i < n; i++) Give(g, s, p, "GBL_003e");
						else if (p is Spell) for (int i = 0; i < n; i++) Give(g, s, p, "GBL_016e");
					}
				})
			}));
			// [BE_729] Mawman Cultist (2) — ALL spells cost (1) more. (both hands, while it is in play)
			cards.Add("BE_729", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HANDS, Effects.AddCost(V("BE_729", "more", 1))) { Condition = SelfCondition.IsSpell, ShowAs = "BE_729e" }
			}));

			// ---------------------------------------------------------------- Demon Hunter
			int FriendlyDiedThisTurn(Controller c) => DiedThisTurn(c).Count(p => p is Minion);
			// [BE_230] (4) — Your left-most minion has Rush. Your right-most minion is Immune while attacking. (two board auras by slot,
			// Restless: they move as the board changes)
			cards.Add("BE_230", new CardDef(new Power
			{
				Aura = new MultiAura(
					new Aura(AuraType.BOARD, Effects.Rush) { Condition = new SelfCondition(p => p is Minion m && m.ZonePosition == 0), Restless = true },
					new Aura(AuraType.BOARD, new Effect(GameTag.IMMUNE_WHILE_ATTACKING, EffectOperator.SET, 1))
					{
						Condition = new SelfCondition(p => p is Minion m && m.ZonePosition == m.Controller.BoardZone.Count - 1), Restless = true
					})
			}));
			// [BE_264] Blind Harrower (3) — Outcast and Deathrattle: Get a random Outcast card.
			ISimpleTask outcastCard = new CustomTask((g, c, s, t, stack) =>
			{
				if (RandomCard(g, x => x[HsTag.OUTCAST] > 0 && x.HasClass(CardClass.DEMONHUNTER)) is Card o) GetCard(c, o, s);
			});
			cards.Add("BE_264", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new ConditionTask(EntityType.SOURCE, PlayedAsOutcast), new FlagTask(true, outcastCard)),
				DeathrattleTask = outcastCard
			}));
			// [BE_265] Encircle (5) — Summon five 3/1 Shredclaws with Rush (BE_873t). Summon a random 1-Cost minion for your opponent.
			cards.Add("BE_265", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("BE_265", "shredclaws", 5); i++) Summon(g, c, Cards.FromId("BE_873t"), s);
					if (RandomCard(g, x => x.Type == CardType.MINION && x.Cost == V("BE_265", "cost", 1)) is Card one) Summon(g, c.Opponent, one, s);
				})
			}));
			// [BE_564] (4) — Battlecry: If a friendly minion died this turn, attack a random enemy.
			cards.Add("BE_564", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (FriendlyDiedThisTurn(c) == 0 || !(s is Minion me) || me.IsDead) return;
					var enemies = Enemies(c).Where(x => !x.IsDead && !(x is Minion { ToBeDestroyed: true })).ToList();
					if (enemies.Count > 0) EffectAttack(g, me, enemies[g.Random.Next(enemies.Count)]);
				})
			}));
			// [BE_565] (4) — Battlecry: If 5 friendly minions died this turn, your Rush minions have Charge this game (BE_565e on the player,
			// BE_565e2 Charge on each Rush minion).
			cards.Add("BE_565", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => FriendlyDiedThisTurn(c) >= V("BE_565", "died", 5), new AddEnchantmentTask("BE_565e", EntityType.CONTROLLER))
			}));
			cards.Add("BE_565e", new CardDef(new Power { Aura = new Aura(AuraType.BOARD, "BE_565e2") { Condition = new SelfCondition(p => p is Minion m && m[GameTag.RUSH] > 0), Restless = true } }));
			cards.Add("BE_565e2", new CardDef(new Power { Enchant = new Enchant(Effects.Charge) }));
			// [BE_566] (1) — Battlecry: Choose a card in your hand to shuffle into your deck. It costs (1) less.
			cards.Add("BE_566", new CardDef(new Power
			{
				HandTarget = SelfCondition.AnyCard,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t?.Zone?.Type != Zone.HAND || t.Controller != c) return;
					Generic.MoveToDeck(c, s, t);
					GiveCostLess(g, s, t, V("BE_566", "less", 1));
				})
			}));
			// [BE_873] Sigil of Hatching (2) — At the start of your next turn, summon three 3/1 Shredclaws with Rush (BE_873t).
			cards.Add("BE_873", Sigil("BE_873", new CustomTask((g, c, s, t, stack) =>
			{
				for (int i = 0; i < V("BE_873", "shredclaws", 3); i++) Summon(g, c, Cards.FromId("BE_873t"), s);
			})));
			// [BE_877] (2) — Lifesteal. Deal $2 damage. If a friendly minion died this turn, this costs (0).
			cards.Add("BE_877", new CardDef(TargetAny, new Power
			{
				Aura = new AdaptiveCostEffect(p => FriendlyDiedThisTurn(p.Controller) > 0 ? 99 : 0),
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is ICharacter ch) Generic.DamageCharFunc((IPlayable)s, ch, V("BE_877", "damage", 2), true); })
			}));

			// ---------------------------------------------------------------- Rogue
			// Larvae (BE_720t1-t11): 1-Cost, "Combo: <a basic Hero Power>" — the class's own Hero Power runs (its PowerTask). Mage's Fireblast and
			// Priest's Lesser Heal aim on Combo (REQ_TARGET_FOR_COMBO). "From another class": not the player's own class's Larva.
			var larvae = new (string Id, CardClass Class, string HeroPower, bool Aims)[]
			{
				("BE_720t1", CardClass.PRIEST, "HERO_09bp", true), ("BE_720t2", CardClass.SHAMAN, "HERO_02bp", false),
				("BE_720t3", CardClass.ROGUE, "HERO_03bp", false), ("BE_720t4", CardClass.PALADIN, "HERO_04bp", false),
				("BE_720t5", CardClass.HUNTER, "HERO_05bp", false), ("BE_720t6", CardClass.DRUID, "HERO_06bp", false),
				("BE_720t7", CardClass.WARLOCK, "HERO_07bp", false), ("BE_720t8", CardClass.MAGE, "HERO_08bp", true),
				("BE_720t9", CardClass.WARRIOR, "HERO_01bp", false), ("BE_720t10", CardClass.DEMONHUNTER, "HERO_10bp", false),
				("BE_720t11", CardClass.DEATHKNIGHT, "HERO_11bp", false),
			};
			foreach (var l in larvae)
			{
				string hp = l.HeroPower;
				var reqs = l.Aims ? new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_FOR_COMBO, 0 } } : new Dictionary<PlayReq, int>();
				cards.Add(l.Id, new CardDef(reqs, new Power
				{
					ComboTask = new CustomTask((g, c, s, t, stack) => Cards.FromId(hp).Power?.PowerTask?.Process(g, c, s, t, stack))
				}));
			}
			void GetLarvae(Game g, Controller c, IEntity s, int n, bool free = false)
			{
				var others = larvae.Where(l => l.Class != c.HeroClass).ToList();
				for (int i = 0; i < n && others.Count > 0; i++)
					if (GetCard(c, Cards.FromId(others[g.Random.Next(others.Count)].Id), s) is IPlayable larva && free)
						Give(g, s, larva, "GBL_009e"); // ("They cost (0)": the client's generic Cost = 0)
			}
			// [BE_720] Broodchamber Tunneler (1) — Battlecry: Get a 1-Cost Larva with a Combo that triggers a Hero Power from another class.
			cards.Add("BE_720", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => GetLarvae(g, c, s, 1)) }));
			// [BE_722] Aqir Swarmer (3) — Deathrattle: Get two Larvae. They cost (0). (the 2026-11-20 patch added "They cost (0)")
			cards.Add("BE_722", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => GetLarvae(g, c, s, V("BE_722", "larvae", 2), free: true)) }));
			// [BE_724] Incubate (2) — Get three Larvae.
			cards.Add("BE_724", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => GetLarvae(g, c, s, V("BE_724", "larvae", 3))) }));
			// [BE_071] Shadow Dance (1) — Deal $1 damage to all undamaged enemies.
			cards.Add("BE_071", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					DamageAll(s, Enemies(c).Where(x => !x.IsDead && x.Damage == 0).ToList(), V("BE_071", "damage", 1), true))
			}));
			// [BE_541] Agent of N'Zoth (2) — Deathrattle: Get a copy of a random different Deathrattle minion in your hand.
			cards.Add("BE_541", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var pool = c.HandZone.GetAll().Where(p => p is Minion && p.Card[GameTag.DEATHRATTLE] > 0 && p.Card.Id != s.Card.Id).ToList();
					if (pool.Count > 0 && !c.HandZone.IsFull) Generic.Copy(c, s, pool[g.Random.Next(pool.Count)], Zone.HAND);
				})
			}));
			// [BE_721] (8) — Costs (2) less for each Combo card played this turn. Battlecry: Draw a card.
			cards.Add("BE_721", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => V("BE_721", "less", 2) * p.Controller.CardsPlayedThisTurn.Count(x => x[GameTag.COMBO] > 0)),
				PowerTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("BE_721", "draws", 2); i++) Generic.Draw(c); }) // (2026-11-20 patch: "Draw 2 cards", was 1)
			}));
			// [BE_723] (4) — After you play a Combo card, deal 3 damage to a random enemy.
			cards.Add("BE_723", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.Card[GameTag.COMBO] > 0),
					SingleTask = ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, V("BE_723", "damage", 3))
				}
			}));
			// [BE_725] (5) — Battlecry: Set the Attack and Health of all Combo minions in your hand to 6 (BE_725e2).
			cards.Add("BE_725", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable m in c.HandZone.GetAll().Where(p => p is Minion && p.Card[GameTag.COMBO] > 0).ToList()) Give(g, s, m, "BE_725e2");
				})
			}));
			cards.Add("BE_725e2", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("BE_725", "stats", 6))) }));

			// ---------------------------------------------------------------- Priest
			// The Elder Words — "<effect>. (The next Shadow spell you play repeats this!)": its player enchantment (BE_22Xe); live (session
			// with BE_221e 2931): after the next Shadow spell played, a NEW copy of the Elder Word is cast (CREATOR and 1715 = the enchantment,
			// random targets, the history tile — Generic.CastPlayedCopy, Wild Magic's), the enchantment leaves; the copy leaves none.
			CardDef ElderWord(string id, ISimpleTask effect) => new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(effect, new CustomTask((g, c, s, t, stack) =>
				{
					if (s[(GameTag)1715] == 0) Generic.AddEnchantmentBlock(g, Cards.FromId(id + "e"), (IPlayable)s, c);
				}))
			});
			CardDef ElderWordMark() => new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Spell && p[SPELL_SCHOOL] == SHADOW),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || t == null || t.Id == e.Creator?.Id || t[(GameTag)1715] > 0) return;
						e.Remove();
						Generic.CastPlayedCopy(c, e, e.Creator);
					})
				}
			});
			// [BE_221] Elder Word: Covet (3) — Draw a card and reduce its Cost by (2) (BE_221e2).
			cards.Add("BE_221", ElderWord("BE_221", new CustomTask((g, c, s, t, stack) =>
			{
				if (Generic.Draw(c) is IPlayable drawn && drawn.Zone?.Type == Zone.HAND) Give(g, s, drawn, "BE_221e2");
			})));
			cards.Add("BE_221e", ElderWordMark());
			cards.Add("BE_221e2", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("BE_221", "less", 2))) }));
			// [BE_222] Elder Word: Manifest (5) — Summon two 3/4 Voidwings with Lifesteal (BE_222t; reworded from "a 5/5" in the
			// 2026-11-03 balance patch, Server/data/dbf-37.0.0.0-20261120/changes.txt).
			cards.Add("BE_222", ElderWord("BE_222", new CustomTask((g, c, s, t, stack) =>
			{
				for (int i = 0; i < V("BE_222", "voidwings", 2); i++) Summon(g, c, Cards.FromId("BE_222t"), s);
			})));
			cards.Add("BE_222e", ElderWordMark());
			// [BE_223] Elder Word: Flay (1) — Deal $3 damage to a random enemy.
			cards.Add("BE_223", ElderWord("BE_223", ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, V("BE_223", "damage", 3), true)));
			cards.Add("BE_223e", ElderWordMark());
			// [BE_196] Befuddled Summoner (4) — At the end of your turn, summon a copy of a random enemy minion. It attacks the original.
			cards.Add("BE_196", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var enemies = c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed).ToList();
						if (enemies.Count == 0 || c.BoardZone.IsFull) return;
						Minion original = enemies[g.Random.Next(enemies.Count)];
						if (Generic.Copy(c, s, original, Zone.PLAY) is Minion copy && !copy.IsDead && !original.IsDead) EffectAttack(g, copy, original);
					})
				}
			}));
			// [BE_220] (2) — Get two random Shadow Spells.
			cards.Add("BE_220", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("BE_220", "spells", 2); i++)
						if (RandomCard(g, x => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == SHADOW) is Card sp) GetCard(c, sp, s);
				})
			}));
			// [BE_227] (7) — At the end of your turn, if you have cast a Shadow spell, the enemy takes fatigue. Otherwise you do. ("this turn":
			// the Shadow spells this player played this turn)
			cards.Add("BE_227", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						bool shadow = c.CardsPlayedThisTurn.Any(x => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == SHADOW);
						Generic.TakeFatigue(shadow ? c.Opponent : c);
					})
				}
			}));
			// [BE_228] Servant of Zuramat (1) — Battlecry: Silence a minion.
			cards.Add("BE_228", new CardDef(BattlecryMinion, new Power { PowerTask = new SilenceTask(EntityType.TARGET) }));
			// [BE_790] (8) — Taunt. Battlecry: Summon a 0, 1, and 2-Attack minion from your deck. Gain their stats (BE_790e).
			cards.Add("BE_790", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int atk = 0, health = 0;
					foreach (int a in new[] { 0, 1, 2 })
					{
						var pool = c.DeckZone.GetAll().Where(p => p is Minion m && m.AttackDamage == a).ToList();
						if (pool.Count == 0 || c.BoardZone.IsFull) continue;
						var pick = (Minion)pool[g.Random.Next(pool.Count)];
						pick.Zone.Remove(pick);
						Generic.SummonBlock.Invoke(g, pick, -1, s);
						if (pick.Zone?.Type == Zone.PLAY) { atk += pick.AttackDamage; health += pick.Health; }
					}
					if ((atk > 0 || health > 0) && s is Minion me)
						Generic.AddEnchantmentBlock(g, Cards.FromId("BE_790e"), me, me, atk, health);
				})
			}));
			cards.Add("BE_790e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0), Effects.Health_N(0)) { UseScriptTag = true } }));

			// ---------------------------------------------------------------- Paladin
			// [BE_000] Lightcaller (3, weapon) — Lifesteals for both players. (live session BE_000 23698: the hero's attack heals both — an AURA
			// trigger heals the enemy hero by the damage, then its own Lifesteal heals the owner)
			cards.Add("BE_000", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						int dealt = c.Hero.AttackDamage;
						if (dealt > 0) c.Opponent.Hero.TakeHeal((IPlayable)s, dealt);
					})
				}
			}));
			// [BE_142] (5) — All numbers on this card equal your remaining Mana. Summon a @/@ Soldier with Divine Shield (BE_142t).
			cards.Add("BE_142", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = c.RemainingMana;
					if (n > 0 && IslandSummon(g, c, Cards.FromId("BE_142t"), s) is Minion soldier) { soldier.AttackDamage = n; soldier.BaseHealth = n; }
				})
			}));
			// [BE_143] Amitut (4) — Taunt. Battlecry: For each Holy spell in your deck, shuffle in a copy with Cast when Drawn (BE_143t2e:
			// CASTS_WHEN_DRAWN on it — Draw.OnDrawn casts it, random targets, and replaces it).
			cards.Add("BE_143", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable holy in c.DeckZone.GetAll().Where(p => p is Spell && p[SPELL_SCHOOL] == HOLY).ToList())
					{
						IPlayable copy = Entity.FromCard(c, holy.Card, new EntityData { [GameTag.CREATOR] = s.Id });
						Generic.ShuffleIntoDeck.Invoke(c, s, copy);
						Give(g, s, copy, "BE_143t2e");
					}
				})
			}));
			cards.Add("BE_143t2e", new CardDef(new Power { Enchant = new Enchant(new Effect(Generic.CASTS_WHEN_DRAWN, EffectOperator.SET, 1)) }));
			// [BE_146] Oathbinder's Aura (3) — After you cast a Holy spell, summon a Soldier with stats equal to its Cost and Divine Shield.
			// Lasts 3 turns. (a Paladin Aura; the Soldier: BE_142t, live)
			cards.Add("BE_146", new CardDef(PaladinAura(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Spell && p[SPELL_SCHOOL] == HOLY),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						int cost = t is IPlayable spell ? spell[GameTag.TAG_LAST_KNOWN_COST_IN_HAND] : 0;
						if (cost > 0 && IslandSummon(g, c, Cards.FromId("BE_142t"), s) is Minion soldier) { soldier.AttackDamage = cost; soldier.BaseHealth = cost; }
					})
				}
			})));
			// [BE_152] (2) — At the end of your turn, get a 2-Cost Blessing that gives +2/+3 (BE_152t, BE_152e).
			cards.Add("BE_152", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new CustomTask((g, c, s, t, stack) => GetCard(c, Cards.FromId("BE_152t"), s)) }
			}));
			cards.Add("BE_152t", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("BE_152e", EntityType.TARGET) }));
			cards.Add("BE_152e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("BE_152", "attack", 2)), Effects.Health_N(V("BE_152", "health", 3))) }));
			// [BE_153] Sun's Reckoning (2) — Deal $1 damage to all enemy minions. Shuffle 'Final Reckoning' (BE_153t) into your deck.
			cards.Add("BE_153", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					DamageAll(s, c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead).Cast<ICharacter>().ToList(), V("BE_153", "damage", 1), true);
					Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("BE_153t"), new EntityData { [GameTag.CREATOR] = s.Id }));
				})
			}));
			// [BE_153t] Final Reckoning — Lifesteal. Deal $3 damage to all enemy minions. (Lifesteal from the card's tag)
			cards.Add("BE_153t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					DamageAll(s, c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead).Cast<ICharacter>().ToList(), V("BE_153t", "damage", 3), true))
			}));
			// [BE_154] (3) — Draw 3 cards. Set the Attack and Health of any minions drawn to 2 (BE_154e).
			cards.Add("BE_154", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("BE_154", "draws", 3); i++)
						if (Generic.Draw(c) is Minion m && m.Zone?.Type == Zone.HAND) Give(g, s, m, "BE_154e");
				})
			}));
			cards.Add("BE_154e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("BE_154", "stats", 2))) }));
			// [BE_183] Halls of Origination (8) — Lifesteal. Deal $8 damage randomly split among all enemies. Summon an 8-Cost minion.
			cards.Add("BE_183", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new EnqueueTask(V("BE_183", "damage", 8), ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, 1), true),
					new CustomTask((g, c, s, t, stack) =>
					{
						if (RandomCard(g, x => x.Type == CardType.MINION && x.Cost == V("BE_183", "cost", 8)) is Card big) Summon(g, c, big, s);
					}))
			}));

			// ---------------------------------------------------------------- Druid
			// [BE_208] (2) — Draw a minion. Give Beasts in your hand +2/+2 (BE_208e).
			cards.Add("BE_208", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					DrawWhere(g, c, p => p is Minion);
					foreach (IPlayable b in c.HandZone.GetAll().Where(p => p is Minion m && m.IsRace(Race.BEAST)).ToList()) Give(g, s, b, "BE_208e");
				})
			}));
			cards.Add("BE_208e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BE_208", "stats", 2))) }));
			// [BE_801] (2) — Draw a different spell. If it's a Nature spell, it casts twice (BE_801e: HsTag.SPELL_CASTS_TWICE, CastSpell's).
			// ("different": not a copy of this card)
			cards.Add("BE_801", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var spells = c.DeckZone.GetAll().Where(p => p is Spell && p.Card.Id != s.Card.Id).ToList();
					if (spells.Count == 0) return;
					IPlayable drawn = Generic.Draw(c, spells[g.Random.Next(spells.Count)]);
					if (drawn is Spell sp && sp[SPELL_SCHOOL] == NATURE && drawn.Zone?.Type == Zone.HAND) Give(g, s, drawn, "BE_801e");
				})
			}));
			cards.Add("BE_801e", new CardDef(new Power { Enchant = new Enchant(new Effect(HsTag.SPELL_CASTS_TWICE, EffectOperator.SET, 1)) }));
			// [BE_802] Deathblossom (2) — Get two 2/1 Plants (BE_802t) with "Deathrattle: Summon a 5/5 Bug with Taunt." They cost (2) (GBL_006e,
			// the client's Cost = 2). The Bug: BE_002 Waterstrider (live session-20261022-020357).
			cards.Add("BE_802", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("BE_802", "plants", 2); i++)
						if (GetCard(c, Cards.FromId("BE_802t"), s) is IPlayable plant) Give(g, s, plant, "GBL_006e");
				})
			}));
			cards.Add("BE_802t", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("BE_002"), s)) }));
			// [BE_850t] Curious Grimlynx (4) — Taunt. Battlecry: Summon a {0}/{1} Beast with Rush (BE_850 Twisted Grimlynx). (It has the
			// opposite stats of this minion: its Attack = this Health, its Health = this Attack)
			cards.Add("BE_850t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					int atk = me.Health, health = me.AttackDamage;
					Minion beast = IslandSummon(g, c, Cards.FromId("BE_850"), s);
					if (beast != null) { beast.AttackDamage = atk; beast.BaseHealth = Math.Max(1, health); }
				})
			}));
			// [BE_852] Ammunae (8) — Battlecry: For the rest of the game, after you play a Deathrattle minion, summon a copy of it (BE_852e on
			// the player; Generic.Copy keeps its buffs).
			cards.Add("BE_852", new CardDef(new Power { PowerTask = new AddEnchantmentTask("BE_852e", EntityType.CONTROLLER) }));
			cards.Add("BE_852e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Minion m && m.Card[GameTag.DEATHRATTLE] > 0),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t is Minion m && m.Zone?.Type == Zone.PLAY && !c.BoardZone.IsFull) Generic.Copy(c, s, m, Zone.PLAY);
					})
				}
			}));
			// [BE_854] (3) — Deal $3 damage. Summon a random 3-Cost minion.
			cards.Add("BE_854", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter ch) Generic.DamageCharFunc((IPlayable)s, ch, V("BE_854", "damage", 3), true);
					if (RandomCard(g, x => x.Type == CardType.MINION && x.Cost == V("BE_854", "cost", 3)) is Card three) Summon(g, c, three, s);
				})
			}));
			// [BE_855] (3) — Battlecry: Give your hero this minion's Attack this turn (BE_855e, its number).
			cards.Add("BE_855", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (s is Minion me && me.AttackDamage > 0) Generic.AddEnchantmentBlock(g, Cards.FromId("BE_855e"), me, c.Hero, me.AttackDamage, -1);
				})
			}));
			cards.Add("BE_855e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0)) { UseScriptTag = true, IsOneTurnEffect = true } }));
			// [BE_856] (6) — Battlecry: Double the stats of all Beasts in your hand and deck (BE_856e: + its own Attack and Health).
			cards.Add("BE_856", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable b in c.HandZone.GetAll().Concat(c.DeckZone.GetAll()).Where(p => p is Minion m && m.IsRace(Race.BEAST)).ToList())
					{
						var m = (Minion)b;
						Generic.AddEnchantmentBlock(g, Cards.FromId("BE_856e"), (IPlayable)s, m, m.AttackDamage, m.BaseHealth);
					}
				})
			}));
			cards.Add("BE_856e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0), Effects.Health_N(0)) { UseScriptTag = true } }));
			// [BE_859] (4) — Taunt. Deathrattle: Draw cards equal to this minion's Attack. (its Attack as it died)
			cards.Add("BE_859", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = s is Minion me ? me.AttackDamage : s[GameTag.ATK];
					for (int i = 0; i < n; i++) Generic.Draw(c);
				})
			}));

			// ---------------------------------------------------------------- Hunter
			// [BE_550] (6) — Battlecry: Summon two copies of this. Deathrattle: Deal 3 damage to a random enemy. (the copies don't Battlecry)
			cards.Add("BE_550", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					// (a copy keeps its buffs — live session-20261021-194927 203131: a 4/4 one made 4/4 copies; Generic.Copy)
					if (s is Minion me) for (int i = 0; i < V("BE_550", "copies", 2) && !c.BoardZone.IsFull; i++) Generic.Copy(c, s, me, Zone.PLAY);
				}),
				DeathrattleTask = ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, V("BE_550", "damage", 3))
			}));
			// [BE_552] Flamespitter (3, weapon) — After your hero attacks, get an Overdrive (BE_553t, Tinker's).
			cards.Add("BE_552", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = new CustomTask((g, c, s, t, stack) => GetCard(c, Cards.FromId("BE_553t"), s)) }
			}));
			// [BE_554] (5) — Destroy a random enemy minion and deal $3 damage to the enemy hero. If you control a Mech, do it again.
			ISimpleTask strike = ComplexTask.Create(ComplexTask.DestroyRandomTargets(1, EntityType.OP_MINIONS),
				new CustomTask((g, c, s, t, stack) => Generic.DamageCharFunc((IPlayable)s, c.Opponent.Hero, V("BE_554", "damage", 3), true)));
			cards.Add("BE_554", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(strike, ComplexTask.If((g, c, s) => c.BoardZone.Any(m => m.IsRace(Race.MECHANICAL) && !m.IsDead), strike))
			}));
			// [BE_555] Reload (8) — Discover a spell and reduce its Cost by (8). Repeat this with excess Cost reduction. (the excess: 8 minus the
			// picked spell's Cost, while above 0)
			ISimpleTask reload = null;
			ISimpleTask ReloadStep(int left) => DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL, null, new CustomTask((g, c, s, t, stack) =>
			{
				if (!(t is IPlayable pick)) return;
				int cost = pick.Card.Cost;
				GiveCostLess(g, s, pick, left);
				int excess = left - cost;
				if (excess > 0) ReloadStep(excess).Process(g, c, s, null, stack);
			}));
			reload = new CustomTask((g, c, s, t, stack) => ReloadStep(V("BE_555", "less", 8)).Process(g, c, s, t, stack));
			cards.Add("BE_555", new CardDef(new Power { PowerTask = reload }));
			// [BE_730] (2) — Summon two 1/1 Mech Beasts with Charge (BE_730t).
			cards.Add("BE_730", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("BE_730", "beasts", 2); i++) Summon(g, c, Cards.FromId("BE_730t"), s); })
			}));
			// [BE_731] Primal Rockfang (3) — Battlecry: Gain the Deathrattles of adjacent minions. (BE_731e "Copied Deathrattle from {0}", one
			// per neighbour with a Deathrattle — Archdruid of Thorns' capture)
			cards.Add("BE_731", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me) || me.Zone?.Type != Zone.PLAY) return;
					foreach (Minion n in me.GetAdjacentMinions().Where(n => n.Card.Power?.DeathrattleTask != null && n.Card[GameTag.DEATHRATTLE] > 0))
						Generic.AddEnchantmentBlock(g, Cards.FromId("BE_731e"), me, me, n.Id, 0, n.Id);
				})
			}));
			cards.Add("BE_731e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.DEATHRATTLE, EffectOperator.SET, 1)),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Enchantment e && e.CapturedCard?.Power?.DeathrattleTask is ISimpleTask copied) copied.Process(g, c, s, null);
				})
			}));
			// [BE_732] (10) — Battlecry: All your minions deal damage equal to their Attack to a random enemy. (one at a time, left to right,
			// a new random enemy each)
			cards.Add("BE_732", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().Where(m => !m.IsDead && m.AttackDamage > 0).ToList())
					{
						var enemies = Enemies(c).Where(x => !x.IsDead && !(x is Minion { ToBeDestroyed: true })).ToList();
						if (enemies.Count == 0) break;
						Generic.DamageCharFunc(m, enemies[g.Random.Next(enemies.Count)], m.AttackDamage, false);
					}
				})
			}));
			// [BE_900] Mechacycle (4) — Taunt. Deathrattle: Summon 4 Mana worth of random Mechs.
			cards.Add("BE_900", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					int left = V("BE_900", "mana", 4);
					for (int guard = 0; left > 0 && guard < 20 && !c.BoardZone.IsFull; guard++)
					{
						int fits = left;
						Card mech = RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.MECHANICAL) && !x.NeverRandomlyOnBoard && x.Cost >= 1 && x.Cost <= fits);
						if (mech == null) break;
						left -= mech.Cost;
						Summon(g, c, mech, s);
					}
				})
			}));

			// ---------------------------------------------------------------- Mage
			// [BE_150] (2) — Battlecry: Discover a spell to put on top of your deck. It costs (2) less.
			cards.Add("BE_150", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL, null, new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is IPlayable pick) || pick.Zone?.Type != Zone.HAND) return;
					pick.Zone.Remove(pick);
					c.DeckZone.Add(pick); // (the top: the deck's last card)
					GiveCostLess(g, s, pick, V("BE_150", "less", 2));
				}))
			}));
			// [BE_201] (3) — Freeze an enemy. Summon a random 5-Cost minion and Freeze it.
			cards.Add("BE_201", new CardDef(TargetEnemy, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter ch && !ch.IsDead) ch.IsFrozen = true;
					if (RandomCard(g, x => x.Type == CardType.MINION && x.Cost == V("BE_201", "cost", 5)) is Card five &&
					    IslandSummon(g, c, five, s) is Minion m) m.IsFrozen = true;
				})
			}));
			// [BE_251] (3, Location) — Draw a card. If it's not a minion, refresh two Mana Crystals.
			cards.Add("BE_251", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (Generic.Draw(c) is IPlayable drawn && !(drawn is Minion)) RefreshMana(c, V("BE_251", "mana", 2));
				})
			}));
			// [BE_252] (2) — Choose a friendly minion. Summon a random minion of the same Cost.
			cards.Add("BE_252", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m && RandomCard(g, x => x.Type == CardType.MINION && x.Cost == m.Card.Cost) is Card same) Summon(g, c, same, s);
				})
			}));
			// [BE_256] (5) — Battlecry and Deathrattle: Cast a random 3-Cost spell.
			ISimpleTask cast3 = new CastRandomSpellTask(x => x.Cost == V("BE_256", "cost", 3));
			cards.Add("BE_256", new CardDef(new Power { PowerTask = cast3, DeathrattleTask = cast3 }));
			// [BE_258] (4) — Deal $2 damage to a random enemy minion, five times. (a new random pick each time)
			cards.Add("BE_258", new CardDef(new Power
			{
				PowerTask = new EnqueueTask(V("BE_258", "times", 5), ComplexTask.DamageRandomTargets(1, EntityType.OP_MINIONS, V("BE_258", "damage", 2), true))
			}));
			// [BE_259] (6) — Battlecry: Summon two 10/10 Constructs (BE_254 Colossus of the Cold). They are Frozen until you spend 15 Mana on
			// spells.
			cards.Add("BE_259", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("BE_259", "constructs", 2); i++) Summon(g, c, Cards.FromId("BE_254"), s);
				})
			}));
			// [BE_254] Colossus of the Cold (10) — Frozen until you spend 15 Mana on spells. (@ left!) (live session-20261021-194927 211785-214048:
			// FROZEN 1 as it lands; TAG_SCRIPT_DATA_NUM_1 counts down by each spell's Mana; at 0, FROZEN 0. The end-of-turn thaw skips it:
			// HsTag.STAYS_FROZEN, Frozen Mammoth's way)
			cards.Add("BE_254", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.ZONE)
					{
						TriggerSource = TriggerSource.SELF,
						Condition = new SelfCondition(p => p.Zone?.Type == Zone.PLAY),
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (!(s is Minion me)) return;
							if (me[GameTag.TAG_SCRIPT_DATA_NUM_1] <= 0) me[GameTag.TAG_SCRIPT_DATA_NUM_1] = me.Card[GameTag.TAG_SCRIPT_DATA_NUM_1];
							me[HsTag.STAYS_FROZEN] = 1;
							me.IsFrozen = true;
						})
					},
					new Trigger(TriggerType.CAST_SPELL)
					{
						TriggerSource = TriggerSource.FRIENDLY,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (!(s is Minion me) || me[HsTag.STAYS_FROZEN] == 0 || !(g.CurrentEventData?.EventSource is IPlayable spell)) return;
							int left = Math.Max(0, me[GameTag.TAG_SCRIPT_DATA_NUM_1] - spell[GameTag.TAG_LAST_KNOWN_COST_IN_HAND]);
							me[GameTag.TAG_SCRIPT_DATA_NUM_1] = left;
							if (left > 0) return;
							me[HsTag.STAYS_FROZEN] = 0;
							me.IsFrozen = false;
						})
					})
			}));
			// [BE_270] (5) — Battlecry: Draw two cards. For each that isn't a minion, summon a 3/6 Water Elemental (CS2_033).
			cards.Add("BE_270", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("BE_270", "draws", 2); i++)
						if (Generic.Draw(c) is IPlayable drawn && !(drawn is Minion)) Summon(g, c, Cards.FromId("CS2_033"), s);
				})
			}));

			// ---------------------------------------------------------------- Death Knight
			// [BE_283] (4) — Battlecry: Spend up to 4 Corpses. Deal 1 damage to all other minions for each. (one damage step per Corpse)
			cards.Add("BE_283", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = Generic.SpendCorpsesUpTo(c, s, V("BE_283", "corpses", 4));
					for (int i = 0; i < n; i++)
					{
						var others = c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => m.Id != s.Id && !m.IsDead && !m.ToBeDestroyed).Cast<ICharacter>().ToList();
						DamageAll(s, others, 1, false);
						// (each pulse its own death step — live session-20261106-001808 201335: a Deathrattle token summoned between pulses
						// takes the later ones)
						g.DeathProcessingAndAuraUpdate();
					}
				})
			}));
			// [BE_284] (3) — Deal $4 damage to a minion and $2 damage to its neighbors. (neighbours: the slots beside it, minions only)
			cards.Add("BE_284", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					var side = m.GetAdjacentMinions().Cast<ICharacter>().ToList();
					Generic.DamageCharFunc((IPlayable)s, m, V("BE_284", "damage", 4), true);
					DamageAll(s, side, V("BE_284", "splash", 2), true);
				})
			}));
			// [BE_325] (2) — Destroy a minion. Restore the minion's Health to its owner. (its current Health, before it dies)
			cards.Add("BE_325", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					int health = Math.Max(0, m.Health);
					Controller owner = m.Controller;
					m.Destroy();
					if (health > 0) owner.Hero.TakeHeal((IPlayable)s, health);
				})
			}));
			// [BE_749] (5) — Deathrattle: Summon two random 1-Cost minions. Give your minions +3 Attack (BE_749e).
			cards.Add("BE_749", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("BE_749", "minions", 2); i++)
						if (RandomCard(g, x => x.Type == CardType.MINION && x.Cost == V("BE_749", "cost", 1)) is Card one) Summon(g, c, one, s);
					foreach (Minion m in c.BoardZone.GetAll().Where(m => !m.IsDead).ToList()) Give(g, s, m, "BE_749e");
				})
			}));
			cards.Add("BE_749e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("BE_749", "attack", 3))) }));
			// [BE_287] G'huun (8) — Battlecry: Steal @ Health split among the lowest Health enemies. Improved by Blood Runes you've played.
			// (live session-20261109-174017 22580: TAG_SCRIPT_DATA_NUM_1 from 6 to 20 as it resolves; each point: BE_287e2 -1 Health on the
			// lowest-Health enemy, BE_287e on G'huun. The improvement: the Blood Runes (COST_BLOOD 2196) of the cards played this game —
			// DOUBT, the session's sum doesn't settle it exactly)
			cards.Add("BE_287", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int blood = c.PlayHistory.Where(h => h.SourceCard != null && h.SourceId != s.Id).Sum(h => h.SourceCard[(GameTag)2196]);
					int n = s.Card[GameTag.TAG_SCRIPT_DATA_NUM_1] + blood;
					s[GameTag.TAG_SCRIPT_DATA_NUM_1] = n;
					for (int i = 0; i < n; i++)
					{
						var enemies = Enemies(c).Where(x => !x.IsDead && x.Health > 0 && !(x is Minion { ToBeDestroyed: true })).ToList();
						if (enemies.Count == 0) break;
						int low = enemies.Min(x => x.Health);
						var lowest = enemies.Where(x => x.Health == low).ToList();
						ICharacter victim = lowest[g.Random.Next(lowest.Count)];
						StealHealth(g, (IPlayable)s, victim, "BE_287e2", "BE_287e");
					}
				})
			}));
			cards.Add("BE_287e2", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(-1)) }));
			cards.Add("BE_287e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(1)) }));
			// [BE_298] (4) — Battlecry: Discover a Blood, Frost or Unholy card. Gain Lifesteal, Reborn or Rush based on your choice.
			// (one card per rune, offered in that order; the keyword by the text's order: Blood -> Lifesteal, Frost -> Reborn, Unholy ->
			// Rush — DOUBT: live hides the opponent's picks)
			cards.Add("BE_298", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var offer = new List<Card>();
					foreach (int rune in new[] { 2196, 2197, 2198 })
					{
						var pool = Cards.FormatTypeCards(g.FormatType).Where(x => x.HasClass(CardClass.DEATHKNIGHT) && x[(GameTag)rune] > 0 && x.Collectible).ToList();
						if (pool.Count > 0) offer.Add(pool[g.Random.Next(pool.Count)]);
					}
					if (offer.Count == 0) return;
					int me = s.Id;
					Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.HAND, offer.ToArray(), new CustomTask((g2, c2, s2, pick, st) =>
					{
						if (pick == null || !g2.IdEntityDic.TryGetValue(me, out IPlayable self) || !(self is Minion m)) return;
						// (live session-20261106-001808 86988, 87285: a card with two runes gives both — Lifesteal and Rush, Reborn and Rush)
						if (pick.Card[(GameTag)2196] > 0) m.HasLifeSteal = true;
						if (pick.Card[(GameTag)2197] > 0) m[GameTag.REBORN] = 1;
						if (pick.Card[(GameTag)2198] > 0) m.IsRush = true;
					}));
				})
			}));

			// ---------------------------------------------------------------- Warrior
			bool GainedArmorThisTurn(Controller c) => c.Game.Turn > 0 && c[HsTag.ARMOR_GAINED_TURN] == c.Game.Turn && c[HsTag.ARMOR_GAINED_THIS_TURN] > 0;
			// [BE_042] Anvil of Reshaping (2) — Give your hero +4 Attack this turn. Costs Armor instead of Mana. (the client's
			// TAG_CARD_ALTERNATE_COST 2 = Armor: Playable.CostsArmor pays it)
			cards.Add("BE_042", new CardDef(new Power { PowerTask = new AddEnchantmentTask("BE_042e", EntityType.HERO) }));
			cards.Add("BE_042e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("BE_042", "attack", 4))) { IsOneTurnEffect = true } }));
			// [BE_559] Show of Force (2) — Deal 3 damage to a minion. If you've gained Armor this turn, destroy it instead.
			cards.Add("BE_559", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					if (GainedArmorThisTurn(c)) m.Destroy();
					else Generic.DamageCharFunc((IPlayable)s, m, V("BE_559", "damage", 3), true);
				})
			}));
			// [BE_560] Brassknuckle Brawler (8) — Charge. Battlecry: If you have 10 or more Armor, gain +4/+4 (BE_560e).
			cards.Add("BE_560", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.Hero.Armor >= V("BE_560", "armor", 10) && s is Minion me) Give(g, s, me, "BE_560e");
				})
			}));
			cards.Add("BE_560e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BE_560", "stats", 4))) }));
			// [BE_572] Tol'vir Vanguard (2) — Taunt. Battlecry and Deathrattle: Gain 3 Armor.
			ISimpleTask vanguard = new CustomTask((g, c, s, t, stack) => c.Hero.GainArmor((IPlayable)s, V("BE_572", "armor", 3)));
			cards.Add("BE_572", new CardDef(new Power { PowerTask = vanguard, DeathrattleTask = vanguard }));
			// [BE_600] Animator of Iron (3) — After you gain Armor, draw a card.
			cards.Add("BE_600", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ARMOR) { TriggerSource = TriggerSource.HERO, SingleTask = new CustomTask((g, c, s, t, stack) => Generic.Draw(c)) }
			}));
			// [BE_558] Stone Gauntlet (1, weapon) — After your hero attacks, convert 5 of your hero's Health to Armor. (a loss of Health,
			// not damage: Character.LoseHealthForCost; no more than leaves the hero at 1 — DOUBT, unrecorded)
			cards.Add("BE_558", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						int n = Math.Min(V("BE_558", "convert", 5), c.Hero.Health - 1);
						if (n <= 0) return;
						c.Hero.LoseHealthForCost(n);
						c.Hero.GainArmor((IPlayable)s, n);
					})
				}
			}));
			// [BE_561] Forge of Origination (3, Location) — Summon a 3/3 Golem with Taunt (BE_561t). Spend 3 Armor to give it +3/+3
			// (BE_561e). (Generic.PayArmor: with less Armor, just the Golem)
			cards.Add("BE_561", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Minion golem = IslandSummon(g, c, Cards.FromId("BE_561t"), s);
					if (golem != null && Generic.PayArmor(c, s, V("BE_561", "armor", 3))) Give(g, s, golem, "BE_561e");
				})
			}));
			cards.Add("BE_561e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("BE_561", "stats", 3))) }));
			// [BE_557] Setesh (7) — Battlecry: Gain 20 Armor. In 2 turns, convert your Armor into a Golem with that many stats and Taunt.
			// (Live session-20261106-001808 215061, 248160: BE_557e2 on the player counts the owner's turn starts in TAG_SCRIPT_DATA_NUM_1;
			// at 2: ARMOR 0 and a BE_561t Golem with those stats.)
			cards.Add("BE_557", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) => c.Hero.GainArmor((IPlayable)s, V("BE_557", "armor", 20))),
					new AddEnchantmentTask("BE_557e2", EntityType.CONTROLLER))
			}));
			cards.Add("BE_557e2", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e)) return;
						e[GameTag.TAG_SCRIPT_DATA_NUM_1] = e[GameTag.TAG_SCRIPT_DATA_NUM_1] + 1;
						if (e[GameTag.TAG_SCRIPT_DATA_NUM_1] < V("BE_557", "turns", 2)) return;
						int armor = c.Hero.Armor;
						e.Remove();
						// (live 215061: the Armor goes inside the card's recorded effect from the hero, BE_557_Setesh_Deathrattle_Super)
						string fx = SabberStoneCore.Loader.SubSpells.FindNamed("BE_557e2", "Deathrattle");
						bool open = fx != null && g.OpenSubSpell(c.Hero, fx, null);
						c.Hero.Armor = 0; // (live: a second Setesh with no Armor left still summons its Golem)
						if (open) g.CloseSubSpells(c.Hero.Id);
						Minion golem = IslandSummon(g, c, Cards.FromId("BE_561t"), e);
						if (golem != null) { golem.AttackDamage = armor; golem.BaseHealth = armor; }
					})
				}
			}));
			// [BE_055] Archaedas (6) — Battlecry: Discover a spell. Transform it into a 3/3 weapon of the same cost (BE_055t) that casts it
			// each attack. (BE_055t: "After your hero attacks, cast '{0}' randomly." — the spell kept set aside; live session-20261021-013733
			// 122615: TAG_SCRIPT_DATA_NUM_1 = that entity's id, nothing else)
			cards.Add("BE_055", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL, null, new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is IPlayable spell) || spell.Zone?.Type != Zone.HAND || !(Cards.TryFromId("BE_055t") is Card weaponCard)) return;
					int cost = spell.Card.Cost;
					IPlayable kept = Entity.FromCard(c, spell.Card, null, c.SetasideZone);
					IPlayable weapon = Generic.ChangeInHand(c, spell, weaponCard);
					weapon[GameTag.COST] = cost;
					weapon[GameTag.TAG_SCRIPT_DATA_NUM_1] = kept.Id;
				}))
			}));
			cards.Add("BE_055t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(g.IdEntityDic.TryGetValue(s[GameTag.TAG_SCRIPT_DATA_NUM_1], out IPlayable kept) && kept.Card.Type == CardType.SPELL)) return;
						Card spell = kept.Card;
						new CastRandomSpellTask().Process(g, c, s, Entity.FromCard(c, spell, new EntityData { [GameTag.CREATOR] = s.Id }));
					})
				}
			}));

			// ---------------------------------------------------------------- Shaman
			// [BE_302] Flame Shock (1) — Deal $2 damage to two different enemies. Overload: (1). (Live session-20261021-013733 35878: the
			// first is the spell's target; then a second pick among the other enemies — EntityChoices type 3, the targeting arrow:
			// Generic.PickTargets. Overload from the card's tag.)
			cards.Add("BE_302", new CardDef(TargetEnemy, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int dmg = V("BE_302", "damage", 2);
					if (t is ICharacter first) Generic.DamageCharFunc((IPlayable)s, first, dmg, true);
					int firstId = t?.Id ?? 0;
					Generic.PickTargets(c, s, 1, ch => ch.Controller != c && ch.Id != firstId && Generic.Pickable(c, ch, true),
						(g2, c2, s2, other) => Generic.DamageCharFunc((IPlayable)s2, other, dmg, true));
				})
			}));
			// [BE_349] Eyemaul (4, weapon) — After your hero attacks, summon two 2/2 Elementals with Rush (BE_349t).
			cards.Add("BE_349", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						for (int i = 0; i < V("BE_349", "elementals", 2); i++) Summon(g, c, Cards.FromId("BE_349t"), s);
					})
				}
			}));
			// [BE_842] Unnatural Cold (2) — Freeze an enemy. Gain 3 Armor. Draw a card.
			cards.Add("BE_842", new CardDef(TargetEnemy, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter ch && !ch.IsDead) ch.IsFrozen = true;
					c.Hero.GainArmor((IPlayable)s, V("BE_842", "armor", 3));
					Generic.Draw(c);
				})
			}));
			// [BE_870] Elemental of Madness (3) — Battlecry: Discover any spell with Cost equal to this minion's Attack. It costs (0) this
			// turn (BE_870e). ("any": every class — DiscoverFromTask.Where.)
			cards.Add("BE_870", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int atk = s is Minion me ? me.AttackDamage : s[GameTag.ATK];
					DiscoverFromTask.Where((cc, x) => x.Type == CardType.SPELL && x.Cost == atk, "BE_870e").Process(g, c, s, t, stack);
				})
			}));
			cards.Add("BE_870e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) { IsOneTurnEffect = true } }));
			// [BE_209] Neptulon, Lord of Tides (7) — Battlecry: For the rest of the game, Elementals you summon have their stats set to 5/5.
			// (Client: BE_209e2 on the player, BE_209e1 "Stats set to 5/5" on each Elemental as it is summoned.)
			cards.Add("BE_209", new CardDef(new Power { PowerTask = new AddEnchantmentTask("BE_209e2", EntityType.CONTROLLER) }));
			cards.Add("BE_209e2", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.SUMMON)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(x => x is Minion m && m.IsRace(Race.ELEMENTAL)),
					SingleTask = new AddEnchantmentTask("BE_209e1", EntityType.TARGET)
				}
			}));
			cards.Add("BE_209e1", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("BE_209", "stats", 5))) }));
			// [BE_171] Primus of Power (4) — Taunt. Battlecry: If you've played two different spell schools while holding this, summon a
			// copy of this. (Unrecorded: never played in a capture. BE_171e "+3/+3 and Taunt" in the client data belongs to an older text.)
			cards.Add("BE_171", new CardDef(SchoolsWhileHolding(V("BE_171", "schools", 2), new CustomTask((g, c, s, t, stack) =>
			{
				if (s is Minion me && !c.BoardZone.IsFull) Generic.Copy(c, s, me, Zone.PLAY); // (a copy keeps its buffs, as BE_550 live)
			}))));
			// [BE_303] Scorchfist Scholar (5) — Battlecry: If you've played two different spell schools while holding this, deal 4 damage
			// to all enemies.
			cards.Add("BE_303", new CardDef(SchoolsWhileHolding(V("BE_303", "schools", 2), new CustomTask((g, c, s, t, stack) =>
				DamageAll(s, Enemies(c), V("BE_303", "damage", 4), false)))));

			// [BE_337] Therazane the Earthmother — Battlecry: Your next spell of each school casts twice. (Generic.GiveSchoolsCastTwice;
			// live session-20261023-152211: BE_337e2 per school on the player, BE_337e on the matching spells in hand)
			cards.Add("BE_337", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.GiveSchoolsCastTwice(c, (IPlayable)s, Cards.FromId("BE_337e2"), Cards.TryFromId("BE_337e")))
			}));
			cards.Add("BE_337e2", new CardDef(new Power { SchoolCastsTwice = true }));
			cards.Add("BE_337e", new CardDef(new Power()));
		}
	}
}
