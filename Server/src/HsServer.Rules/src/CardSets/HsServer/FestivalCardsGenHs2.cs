// HsServer addition (not upstream SabberStone): Festival of Legends — Death Knight, Demon Hunter, Druid, Hunter, Mage and
// Paladin cards (shared pieces: FestivalCardsGenHs.cs). Every number goes through CardValues (V).
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
		/// <summary>Client TAG_SCRIPT_DATA_NUM_3 / _4 (the engine enum stops at _2).</summary>
		private const GameTag SCRIPT_DATA_NUM_3 = (GameTag)2889, SCRIPT_DATA_NUM_4 = (GameTag)2919;

		/// <summary>A minion of <paramref name="card"/> with its Attack and Health set (a token whose stats the effect names: Souls,
		/// Blighthead, Cosmic Creation).</summary>
		private static void SummonWithStats(Game g, Controller c, Card card, int attack, int health, IEntity source)
		{
			if (card == null || c.BoardZone.IsFull) return;
			var m = (Minion)Entity.FromCard(c, card, new EntityData { { GameTag.ATK, attack }, { GameTag.HEALTH, health } });
			Generic.SummonBlock.Invoke(g, m, -1, source);
		}

		/// <summary>Up to <paramref name="n"/> random cards of <paramref name="from"/> (no repeats).</summary>
		private static List<T> RandomOf<T>(Game g, IEnumerable<T> from, int n)
		{
			var list = from.ToList();
			var picks = new List<T>();
			while (picks.Count < n && list.Count > 0)
			{
				T x = list[g.Random.Next(list.Count)];
				list.Remove(x);
				picks.Add(x);
			}
			return picks;
		}

		/// <summary>"Discover a copy of …": up to three different cards of <paramref name="pool"/> offered; a copy of the pick
		/// goes to the hand.</summary>
		private static void DiscoverCopyOf(Controller c, IEntity source, IEnumerable<Card> pool)
		{
			Card[] offer = DiscoverFromTask.PickDistinct(c.Game, pool, 3).ToArray();
			if (offer.Length > 0 && !c.HandZone.IsFull)
				Generic.CreateChoiceCards.Invoke(c, source, null, ChoiceType.GENERAL, ChoiceAction.HAND, offer, null);
		}

		private static void FestivalClasses1(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Death Knight
			// [ETC_209] Hardcore Cultist (3) — Battlecry: Deal 2 damage. Finale: To all enemies.
			cards.Add("ETC_209", new CardDef(BattlecryAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int damage = V("ETC_209", "damage", 2);
					if (Generic.Finale(s)) DamageAll(s, Enemies(c), damage, false);
					else if (t is ICharacter target) Generic.DamageCharFunc((IPlayable)s, target, damage, false);
				})
			}));
			// [ETC_210] Climactic Necrotic Explosion (10) — Lifesteal. Deal {0} damage. Summon {1} {2}/{3} Souls. (Randomly
			// improved by Corpses you've spent.) Wiki: "When a Corpse is spent, any one of these numbers on the card can
			// individually upgrade by 1" — the damage (NUM_1, 6), the Souls (NUM_2, 3, capped at 7), their Attack (NUM_3, 2) or
			// Health (NUM_4, 2). User 2026-10-07: the numbers update while it is in hand. Each Corpse its owner spends upgrades
			// every copy in hand and deck (Power.CorpseSpentTask, Generic.CorpsesSpent): one number at random +1 on the card, so
			// the client text ({0}..{3} from the entity's NUM_1..4: MultiAltTextScriptDataNumsCardTextBuilder) follows. The cast
			// uses the card's current numbers. The Souls: ETC_522t.
			cards.Add("ETC_210", new CardDef(TargetAny, new Power
			{
				CorpseSpentTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is IPlayable card)) return;
					bool soulsFull = card[GameTag.TAG_SCRIPT_DATA_NUM_2] >= V("ETC_210", "max souls", 7);
					GameTag number = g.Random.Next(soulsFull ? 3 : 4) switch
					{
						0 => GameTag.TAG_SCRIPT_DATA_NUM_1,
						1 => SCRIPT_DATA_NUM_3,
						2 => SCRIPT_DATA_NUM_4,
						_ => GameTag.TAG_SCRIPT_DATA_NUM_2,
					};
					card[number] = card[number] + 1;
				}),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int damage = s[GameTag.TAG_SCRIPT_DATA_NUM_1], souls = s[GameTag.TAG_SCRIPT_DATA_NUM_2];
					int atk = s[SCRIPT_DATA_NUM_3], hp = s[SCRIPT_DATA_NUM_4];
					if (t is ICharacter target) Generic.DamageCharFunc((IPlayable)s, target, damage, true);
					for (int i = 0; i < souls; i++) SummonWithStats(g, c, Cards.FromId("ETC_522t"), atk, hp, s);
				})
			}));
			// [ETC_423] Arcanite Ripper (3) — Deathrattle: Summon a @/@ Lifesteal Undead. (Change your Health on your turn while
			// equipped to improve!) +1 per change: each damage that reaches your hero's Health and each heal of it, on your turn
			// (wiki patch notes: 26.0.4 "was not improving when damage and healing occurred at the same time", 29.6.0 "would only
			// trigger once when dealt damage and healed at the same time" — each counts; 26.2.0 "would count Armor change as
			// Health change" and 27.6.0 "would upgrade if the attacker took damage equal to the Armor they had" — Armor alone
			// doesn't: HsTag.HERO_HEALTH_LOST_LAST_DAMAGE). Not yet: 35.6.0 "did not upgrade after gaining Health" (a max-Health
			// gain: no engine event for it; live-checks).
			var ripperImproves = new SelfCondition(p => p.Game.CurrentPlayer == p.Controller);
			cards.Add("ETC_423", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.TAKE_DAMAGE)
					{
						TriggerSource = TriggerSource.HERO,
						Condition = new SelfCondition(p => p.Game.CurrentPlayer == p.Controller && p.Controller.Hero[HsTag.HERO_HEALTH_LOST_LAST_DAMAGE] > 0),
						SingleTask = new CustomTask((g, c, s, t, stack) => Improve(s))
					},
					new Trigger(TriggerType.HEAL) { TriggerSource = TriggerSource.HERO, Condition = ripperImproves, SingleTask = new CustomTask((g, c, s, t, stack) => Improve(s)) }),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => SummonWithStats(g, c, Cards.FromId("ETC_423t"), Num1(s), Num1(s), s))
			}));
			// [ETC_424] Death Growl (1) — Choose a minion. Spread its Deathrattle to adjacent minions. (ETC_424e: "Copied
			// Deathrattle from {0}" — the chosen minion's card Deathrattle, as Ancient Curse / AV_317e.)
			cards.Add("ETC_424", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m) || m.Card.Power?.DeathrattleTask == null) return;
					foreach (Minion next in Neighbors(m).ToList())
						Generic.AddEnchantmentBlock(g, Cards.FromId("ETC_424e"), (IPlayable)s, next, 0, 0, m.Id);
				})
			}));
			cards.Add("ETC_424e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.DEATHRATTLE, EffectOperator.SET, 1)),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Enchantment e && e.CapturedCard?.Power?.DeathrattleTask is ISimpleTask copied) copied.Process(g, c, s, null);
				})
			}));
			// [ETC_427] Harmonic Metal (3) — Give 4 random minions in your hand +2/+2. / [ETC_427t] Dissonant Metal — 2 random +4/+4.
			cards.Add("ETC_427", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, RandomOf(g, c.HandZone.Where(p => Generic.IsMinionCard(p)), V("ETC_427", "minions", 4)), "ETC_427e"))
			}));
			cards.Add("ETC_427t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, RandomOf(g, c.HandZone.Where(p => Generic.IsMinionCard(p)), V("ETC_427t", "minions", 2)), "ETC_427e2"))
			}));
			cards.Add("ETC_427e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ETC_427", "stats", 2))) }));
			cards.Add("ETC_427e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ETC_427t", "stats", 4))) }));
			// [ETC_522] Screaming Banshee (5) — Lifesteal. After your hero gains Health, summon a Soul with that much Attack and Health.
			cards.Add("ETC_522", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.HEAL)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						int healed = g.CurrentEventData?.EventNumber ?? 0;
						if (healed > 0) SummonWithStats(g, c, Cards.FromId("ETC_522t"), healed, healed, s);
					})
				}
			}));
			// [ETC_526] Cage Head (8) — Deathrattle: Summon a 9/9 Blight Boar with Charge and Taunt.
			cards.Add("ETC_526", new CardDef(new Power { DeathrattleTask = new SummonTask("ETC_526t", SummonSide.DEATHRATTLE) }));
			// [JAM_005] Yelling Yodeler (4) — Battlecry: Trigger a friendly minion's Deathrattle twice.
			cards.Add("JAM_005", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_DEATHRATTLE, 0 } },
				new Power { PowerTask = ComplexTask.Create(new ActivateDeathrattleTask(EntityType.TARGET), new ActivateDeathrattleTask(EntityType.TARGET)) }));
			// [JAM_006] Cold Feet (2) — Enemy minions cost (5) more next turn.
			cards.Add("JAM_006", new CardDef(new Power { PowerTask = new AddEnchantmentTask("JAM_006e", EntityType.OP_CONTROLLER) })); // (on the opponent: OpponentNextTurnCost)
			cards.Add("JAM_006e", OpponentNextTurnCost(V("JAM_006", "more", 5), SelfCondition.IsMinion, "JAM_006e2"));
			// [JAM_008] Dead Air (2) — Destroy your Undead. Resummon them. (They die — Deathrattles first — then new copies come.)
			cards.Add("JAM_008", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var undead = c.BoardZone.Where(m => !m.IsDead && !m.ToBeDestroyed && m.IsRace(Race.UNDEAD)).ToList();
					foreach (Minion m in undead) Generic.Destroy(m, s, true);
					g.DeathProcessingAndAuraUpdate();
					foreach (Minion m in undead) Summon(g, c, m.Card, s);
				})
			}));

			// ---------------------------------------------------------------- Demon Hunter
			// [ETC_026] Guitar Soloist (5) — Battlecry: If you control no other minions, draw a spell, a minion, and a weapon.
			cards.Add("ETC_026", new CardDef(new Power
			{
				PowerTask = IfSoloist(new CustomTask((g, c, s, t, stack) =>
				{
					DrawWhere(g, c, p => p is Spell);
					DrawWhere(g, c, p => p is Minion);
					DrawWhere(g, c, p => p is Weapon);
				}))
			}));
			// [ETC_200] Rush the Stage (3) — Draw two Rush minions. They cost (1) less.
			cards.Add("ETC_200", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("ETC_200", "draw", 2); i++)
						if (DrawOne(g, c, p => p is Minion && p.Card[GameTag.RUSH] > 0) is IPlayable drawn) Give(g, s, drawn, "ETC_200e");
				})
			}));
			cards.Add("ETC_200e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("ETC_200", "discount", 1))) }));
			// [ETC_394] Taste of Chaos (1) — Deal $2 damage to a minion. Finale: Discover a Fel spell.
			cards.Add("ETC_394", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("ETC_394", "damage", 2), EntityType.TARGET, true),
					Finale(DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == FEL)))
			}));
			// [ETC_398] Eye of Shadow (2) — Your hero has Lifesteal.
			cards.Add("ETC_398", new CardDef(new Power { Aura = new Aura(AuraType.HERO, Effects.Lifesteal) }));
			// [ETC_399] Halveria Darkraven (4) — Rush. After a friendly Rush minion attacks, give your minions +1 Attack.
			cards.Add("ETC_399", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.MINIONS,
					Condition = new SelfCondition(p => p is Minion m && m.IsRush),
					SingleTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.Where(m => !m.IsDead), "ETC_399e"))
				}
			}));
			cards.Add("ETC_399e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("ETC_399", "attack", 1))) }));
			// [ETC_400] Instrument Smasher (4) — Whenever your weapon is destroyed, equip a random Demon Hunter weapon.
			// "Destroyed" = naturally (durability, an Ooze effect), never by being replaced with a newly equipped weapon (user
			// ruling 2026-10-07; the shared rule lives in Trigger.Validate, TriggerSource.WEAPON + HsTag.REPLACED_BY_WEAPON), so
			// the weapon it equips can't start a chain.
			cards.Add("ETC_400", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerSource = TriggerSource.WEAPON,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(RandomCard(g, x => x.Type == CardType.WEAPON && x.HasClass(CardClass.DEMONHUNTER)) is Card w)) return;
						var weapon = (Weapon)Entity.FromCard(c, w, new EntityData { [GameTag.CREATOR] = s.Id });
						weapon.Card.Power?.Aura?.Activate(weapon);
						weapon.Card.Power?.Trigger?.Activate(weapon);
						c.Hero.AddWeapon(weapon);
					})
				}
			}));
			// [ETC_405] Glaivetar (4) — Deathrattle: Draw @ cards. (Play Outcast cards while equipped to improve!)
			cards.Add("ETC_405", new CardDef(new Power
			{
				Trigger = Improves(TriggerType.PLAY_CARD, TriggerSource.FRIENDLY, IsOutcastCard),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < Num1(s); i++) Generic.Draw(c); })
			}));
			// [ETC_410] Snakebite (2) — Rush. Battlecry: Gain +1/+1 for each minion that died this turn.
			cards.Add("ETC_410", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int died = c.NumFriendlyMinionsThatDiedThisTurn + c.Opponent.NumFriendlyMinionsThatDiedThisTurn;
					for (int i = 0; i < died; i++) Give(g, s, (IPlayable)s, "ETC_410e");
				})
			}));
			cards.Add("ETC_410e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ETC_410", "stats", 1))) }));
			// [ETC_411] SECURITY!! (2) — Summon two 1/1 Illidari with Rush. Outcast: Summon one more. (BT_036t)
			cards.Add("ETC_411", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					ComplexTask.Repeat(new SummonTask("BT_036t", SummonSide.SPELL), V("ETC_411", "illidari", 2)),
					new ConditionTask(EntityType.SOURCE, PlayedAsOutcast),
					new FlagTask(true, new SummonTask("BT_036t", SummonSide.SPELL)))
			}));
			// [ETC_413] Going Down Swinging (4) — Give your hero +2 Attack and Immune this turn, then attack each enemy minion.
			cards.Add("ETC_413", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Give(g, s, c.Hero, "ETC_413e");
					Give(g, s, c.Hero, "ETC_413e2");
					foreach (Minion enemy in c.Opponent.BoardZone.ToList())
					{
						if (c.Hero.IsDead || c.Hero.ToBeDestroyed) break;
						if (enemy.IsDead || enemy.ToBeDestroyed || enemy.Zone?.Type != Zone.PLAY) continue;
						Generic.AttackBlock.Invoke(c, c.Hero, enemy, true, true);
					}
				})
			}));
			cards.Add("ETC_413e", OneTurn(Effects.Attack_N(V("ETC_413", "attack", 2))));
			cards.Add("ETC_413e2", OneTurn(Effects.Immune));
			// [JAM_016] Abyssal Bassist (7) — Taunt, Lifesteal. Costs (2) less for each weapon you've equipped this game.
			cards.Add("JAM_016", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => V("JAM_016", "discount", 2) * p.Controller.NumWeaponsPlayedThisGame)
			}));
			// [JAM_018] Remixed Rhapsody (5) — Deal $3 damage to all minions. Gains an extra effect in your hand that changes
			// each turn. Its versions (Generic.RemixOnEnteringHand): t draw 3, t2 twice, t3 hero +5 Attack, t4 a 5/5 Demon.
			ISimpleTask rhapsody = new DamageTask(V("JAM_018", "damage", 3), EntityType.ALLMINIONS, true);
			cards.Add("JAM_018", new CardDef(new Power { PowerTask = rhapsody }));
			cards.Add("JAM_018t", new CardDef(new Power { PowerTask = ComplexTask.Create(rhapsody, ComplexTask.Repeat(new DrawTask(), V("JAM_018t", "draw", 3))) }));
			cards.Add("JAM_018t2", new CardDef(new Power { PowerTask = ComplexTask.Create(rhapsody, rhapsody) }));
			cards.Add("JAM_018t3", new CardDef(new Power { PowerTask = ComplexTask.Create(rhapsody, new AddEnchantmentTask("JAM_018t3e", EntityType.HERO)) }));
			cards.Add("JAM_018t3e", OneTurn(Effects.Attack_N(V("JAM_018t3", "attack", 5))));
			cards.Add("JAM_018t4", new CardDef(new Power { PowerTask = ComplexTask.Create(rhapsody, new SummonTask("JAM_018t5", SummonSide.SPELL)) }));

			// ---------------------------------------------------------------- Druid
			// [ETC_373] Drum Circle (7) — Choose One - Summon five 2/2 Treants; or Give your minions +2/+4 and Taunt.
			cards.Add("ETC_373a", new CardDef(new Power { PowerTask = ComplexTask.Repeat(new SummonTask("ETC_373t", SummonSide.SPELL), V("ETC_373a", "treants", 5)) }));
			cards.Add("ETC_373b", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.Where(m => !m.IsDead).ToList())
					{
						Give(g, s, m, "ETC_373be");
						m.HasTaunt = true;
					}
				})
			}));
			cards.Add("ETC_373be", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("ETC_373b", "attack", 2)), Effects.Health_N(V("ETC_373b", "health", 4))) }));
			// [ETC_375] Peaceful Piper (1) — Choose One - Draw a Beast; or Discover one.
			cards.Add("ETC_375a", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p is Minion && IsRaceCard(p, Race.BEAST))) }));
			cards.Add("ETC_375b", new CardDef(new Power { PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.IsRace(Race.BEAST)) }));
			// [ETC_376] Summer Flowerchild (5) — Battlecry: Draw two cards that cost (6) or more. Finale: They cost (1) less.
			cards.Add("ETC_376", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					bool finale = Generic.Finale(s);
					for (int i = 0; i < V("ETC_376", "draw", 2); i++)
						if (DrawOne(g, c, p => p.Cost >= V("ETC_376", "cost", 6)) is IPlayable drawn && finale) Give(g, s, drawn, "ETC_376e");
				})
			}));
			cards.Add("ETC_376e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("ETC_376", "discount", 1))) }));
			// [ETC_379] Harmonic Mood (2) — Give your hero +2 Attack this turn. Gain 4 Armor. / [ETC_379t] Dissonant Mood: +4 / 2.
			cards.Add("ETC_379", new CardDef(new Power { PowerTask = ComplexTask.Create(new AddEnchantmentTask("ETC_379e", EntityType.HERO), new ArmorTask(V("ETC_379", "armor", 4))) }));
			cards.Add("ETC_379t", new CardDef(new Power { PowerTask = ComplexTask.Create(new AddEnchantmentTask("ETC_379e2", EntityType.HERO), new ArmorTask(V("ETC_379t", "armor", 2))) }));
			cards.Add("ETC_379e", OneTurn(Effects.Attack_N(V("ETC_379", "attack", 2))));
			cards.Add("ETC_379e2", OneTurn(Effects.Attack_N(V("ETC_379t", "attack", 4))));
			// [ETC_382] Free Spirit (1) — Battlecry and Deathrattle: Your Hero Power gains 1 more Armor this game. (Actions/
			// HeroPower.cs: HERO_POWER_EXTRA_ARMOR.)
			ISimpleTask freeSpirit = new CustomTask((g, c, s, t, stack) => c[HsTag.HERO_POWER_EXTRA_ARMOR] += V("ETC_382", "armor", 1));
			cards.Add("ETC_382", new CardDef(new Power { PowerTask = freeSpirit, DeathrattleTask = freeSpirit }));
			// [ETC_384] Spread the Word (4) — Draw 2 cards. Costs (1) less for each Attack your hero has.
			cards.Add("ETC_384", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.Hero.AttackDamage),
				PowerTask = ComplexTask.Repeat(new DrawTask(), V("ETC_384", "draw", 2))
			}));
			// [ETC_385] Groovy Cat (2) — Battlecry and Deathrattle: Your Hero Power gives your hero 1 more Attack this game.
			ISimpleTask groovy = new CustomTask((g, c, s, t, stack) => c[HsTag.HERO_POWER_EXTRA_ATTACK] += V("ETC_385", "attack", 1));
			cards.Add("ETC_385", new CardDef(new Power { PowerTask = groovy, DeathrattleTask = groovy }));
			// [ETC_386] Zok Fogsnout (6) — Battlecry: Summon two {0}/{1} Quilboar with Taunt. (Improved by your hero Attack &
			// Armor gained this turn!) The base 1/1 (its NUM_1 / NUM_2) plus the hero's Attack / the Armor gained this turn.
			cards.Add("ETC_386", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int atk = s[GameTag.TAG_SCRIPT_DATA_NUM_1] + c.Hero.AttackDamage;
					int hp = s[GameTag.TAG_SCRIPT_DATA_NUM_2] + (c[HsTag.ARMOR_GAINED_TURN] == g.Turn ? c[HsTag.ARMOR_GAINED_THIS_TURN] : 0);
					for (int i = 0; i < V("ETC_386", "quilboar", 2); i++) SummonWithStats(g, c, Cards.FromId("ETC_386t"), atk, hp, s);
				})
			}));
			// [ETC_387] Rhythm and Roots (4) — Choose One (Secretly) - Summon three 5/5 Ancients in 2 turns; or 8/8 Giants in 4
			// turns. The pick puts ETC_387t into the Secret zone, where BOTH players see it like an Objective (user 2026-10-07:
			// "by secret slot I don't mean invisible, I mean literally just the same slots as secrets and/or auras ... like
			// Wildpaw Cavern"; its card data: OBJECTIVE, CardTextBuilderType HIDDEN_CHOICE). Only the option is secret: its
			// HIDDEN_CHOICE (1 Ancients, 2 Giants) reaches the owner only (ClientStream.OwnerOnlyTags), so the owner's client shows
			// "In 2 turns, summon three 5/5 Ancients (Secretly)." and its Objective banner counts 2 / 4 turns down
			// (Entity.GetCustomObjectiveBannerText: 2 or 4 from HIDDEN_CHOICE, minus QUEST_PROGRESS), while the opponent's shows
			// "Choose One (Secretly) - ..." and an unknown count (no QUEST_PROGRESS_TOTAL is sent; its length is the server's
			// HsTag.HIDDEN_OBJECTIVE_TURNS), and gets no SubOption on the play (card tag 818, HsTag.SECRET_CHOICE). At the start of
			// each of its owner's turns QUEST_PROGRESS +1 (its TRIGGER block: TriggerKeyword OBJECTIVE, the card's tag); at its
			// length it summons three Ancients (ETC_387bt) or Giants (ETC_387ct) and leaves.
			cards.Add("ETC_387", new CardDef(new Power { NoScript = true })); // its two options below (ChooseOneOptionId: b / c)
			cards.Add("ETC_387b", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => StartRhythm(c, s, V("ETC_387b", "turns", 2), 1)) }));
			cards.Add("ETC_387c", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => StartRhythm(c, s, V("ETC_387c", "turns", 4), 2)) }));
			cards.Add("ETC_387t", new CardDef(new Power
			{
				// (2026-10-08, live 012951 133777 / 136022: its countdown is index 2, no keyword, shown in the history)
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					EffectIndex = 2,
					KeywordOverride = (GameTag)0,
					InHistory = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Spell rhythm) || rhythm.Zone?.Type != Zone.SECRET) return;
						if (!Generic.ObjectiveTick(rhythm, rhythm[HsTag.HIDDEN_OBJECTIVE_TURNS])) return;
						string token = rhythm[GameTag.HIDDEN_CHOICE] == 2 ? "ETC_387ct" : "ETC_387bt";
						for (int i = 0; i < V("ETC_387", "summons", 3); i++) Summon(g, c, Cards.FromId(token), s);
						Generic.ObjectiveExpires(c, rhythm);
					})
				}
			}));
			// [ETC_388] Timber Tambourine (4) — Deathrattle: Summon @ 5/5 Ancients. (Play cards that cost (5) or more while
			// equipped to improve!)
			cards.Add("ETC_388", new CardDef(new Power
			{
				Trigger = Improves(TriggerType.PLAY_CARD, TriggerSource.FRIENDLY, new SelfCondition(p => PlayedCost(p) >= V("ETC_388", "cost", 5))),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < Num1(s); i++) Summon(g, c, Cards.FromId("ETC_387bt"), s); })
			}));
			// [JAM_026] Popular Pixie (2) — Choose One - Refresh your Hero Power; or Your next one costs (0).
			cards.Add("JAM_026a", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.Hero.HeroPower is HeroPower hp)
					{
						hp[GameTag.HEROPOWER_ACTIVATIONS_THIS_TURN] = 0;
						hp.IsExhausted = false;
					}
				})
			}));
			cards.Add("JAM_026b", new CardDef(new Power { PowerTask = new AddEnchantmentTask("JAM_026e", EntityType.CONTROLLER) }));
			cards.Add("JAM_026e", new CardDef(NextHeroPowerFree()));
			// [JAM_028] Blood Treant (5) — Costs Health instead of Mana.
			cards.Add("JAM_028", new CardDef(new Power { CostsHealthWhile = p => true }));
			// [JAM_029] Doomkin (6) — Battlecry: Take one of your opponent's empty Mana Crystals. (They lose an empty Mana
			// Crystal — one they spent, if any — and the player gains an empty one; none if they have no spent crystal.)
			cards.Add("JAM_029", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Controller op = c.Opponent;
					if (op.BaseMana <= 0 || op.UsedMana <= 0 && op.OverloadLocked <= 0) return;
					op.BaseMana -= 1;
					if (op.UsedMana > 0) op.UsedMana -= 1;
					Generic.ChangeManaCrystal.Invoke(c, 1, false);
				})
			}));

			// ---------------------------------------------------------------- Hunter
			// [ETC_028] Harmonica Soloist (3) — Battlecry: If you control no other minions, Discover and cast a Secret.
			cards.Add("ETC_028", new CardDef(new Power
			{
				PowerTask = IfSoloist(DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL && x[GameTag.SECRET] > 0, null, null, ChoiceAction.CAST))
			}));
			// [ETC_201] Bunch of Bananas (1) — Give a minion +1/+1. (3 Bananas left!) → ETC_201t (2 left) → ETC_201t2 (last).
			foreach (var (id, next) in new[] { ("ETC_201", "ETC_201t"), ("ETC_201t", "ETC_201t2"), ("ETC_201t2", null) })
			{
				string after = next;
				cards.Add(id, new CardDef(TargetMinion, new Power
				{
					PowerTask = ComplexTask.Create(new AddEnchantmentTask("ETC_201e", EntityType.TARGET),
						new CustomTask((g, c, s, t, stack) => { if (after != null) AddToHand(c, Cards.FromId(after)); }))
				}));
			}
			cards.Add("ETC_201e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ETC_201", "stats", 1))) }));
			// [ETC_207] Barrel of Monkeys (2) — Summon a 1/4 Monkey with Taunt. (3 Monkeys left!) (The Monkey: the client's
			// related CS2_127; ETC_207t / t2 the next barrels.)
			foreach (var (id, next) in new[] { ("ETC_207", "ETC_207t"), ("ETC_207t", "ETC_207t2"), ("ETC_207t2", null) })
			{
				string after = next;
				cards.Add(id, new CardDef(new Power
				{
					PowerTask = ComplexTask.Create(new SummonTask("CS2_127", SummonSide.SPELL),
						new CustomTask((g, c, s, t, stack) => { if (after != null) AddToHand(c, Cards.FromId(after)); }))
				}));
			}
			// [ETC_208] Stranglethorn Heart (8) — Tradeable. Resurrect all friendly Beasts that cost (5) or more.
			cards.Add("ETC_208", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable dead in c.DiedThisGameCards.Where(p => p is Minion && p.Card.IsRace(Race.BEAST) && p.Card.Cost >= V("ETC_208", "cost", 5)).ToList())
					{
						if (c.BoardZone.IsFull) break;
						Summon(g, c, dead.Card, s);
					}
				})
			}));
			// [ETC_831] Thornmantle Musician (1) — Finale: The next Beast you summon gets +1/+1.
			cards.Add("ETC_831", new CardDef(new Power { PowerTask = Finale(new AddEnchantmentTask("ETC_831e", EntityType.CONTROLLER)) }));
			cards.Add("ETC_831e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Minion m && m.IsRace(Race.BEAST)),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || !(t is Minion beast)) return;
						Give(g, e.Creator ?? (IPlayable)c.Hero, beast, "ETC_831e1");
						e.Remove();
					})
				}
			}));
			cards.Add("ETC_831e1", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ETC_831", "stats", 1))) }));
			// [ETC_832] Jungle Jammer (4) — Deathrattle: Summon a random @-Cost Beast. (Cast spells while equipped to improve!)
			cards.Add("ETC_832", new CardDef(new Power
			{
				Trigger = Improves(TriggerType.AFTER_CAST, TriggerSource.FRIENDLY),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					int cost = Num1(s);
					if (Cards.BoardCostMinionCards(g.FormatType).TryGetValue(cost, out List<Card> pool) &&
					    RandomOf(g, pool.Where(x => x.IsRace(Race.BEAST)), 1).FirstOrDefault() is Card beast)
						Summon(g, c, beast, s);
				})
			}));
			// [ETC_833] Arrow Smith (2) — After you cast a spell, deal 1 damage to the lowest Health enemy.
			cards.Add("ETC_833", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (LowestHealthEnemy(g, c) is ICharacter low) Generic.DamageCharFunc((IPlayable)s, low, V("ETC_833", "damage", 1), false);
					})
				}
			}));
			// [ETC_836] Mister Mukla (6) — Rush. Battlecry: Fill your opponent's hand with Bananas. (EX1_014t, its related card.)
			cards.Add("ETC_836", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					while (!c.Opponent.HandZone.IsFull) AddToHand(c.Opponent, Cards.FromId("EX1_014t"));
				})
			}));
			// [ETC_838] Big Dreams (5) — Summon the highest Cost Beast from your hand. It goes Dormant for 2 turns.
			cards.Add("ETC_838", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var beasts = c.HandZone.OfType<Minion>().Where(m => Generic.IsMinionCard(m)).Where(m => m.IsRace(Race.BEAST)).ToList();
					if (beasts.Count == 0 || c.BoardZone.IsFull) return;
					int max = beasts.Max(m => m.Cost);
					var top = beasts.Where(m => m.Cost == max).ToList();
					Minion beast = top[g.Random.Next(top.Count)];
					c.HandZone.Remove(beast);
					Generic.SummonBlock.Invoke(g, beast, -1, s);
					if (beast.Zone?.Type == Zone.PLAY) Generic.MakeDormant(beast, V("ETC_838", "turns", 2), "ETC_838e2", (IPlayable)s);
				})
			}));
			// [ETC_840] Banjosaur (10) — Rush. Whenever this attacks, draw a Beast and gain its stats.
			cards.Add("ETC_840", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (DrawOne(g, c, p => p is Minion && IsRaceCard(p, Race.BEAST)) is Minion beast && s is Minion me)
							Generic.AddEnchantmentBlock(g, Cards.FromId("ETC_840e"), me, me, beast.AttackDamage, beast.Health, 0);
					})
				}
			}));
			cards.Add("ETC_840e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));
			// [JAM_003] Hidden Meaning (2) — Secret: When your opponent ends their turn with no Mana, summon a random 3-Cost minion.
			cards.Add("JAM_003", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					EitherTurn = true,
					Condition = new SelfCondition(p => p.Game.CurrentPlayer == p.Controller.Opponent && p.Controller.Opponent.RemainingMana == 0),
					SingleTask = ComplexTask.Secret(new CustomTask((g, c, s, t, stack) => Summon(g, c, RandomMinionOfCost(g, V("JAM_003", "cost", 3)), s)))
				}
			}));
			// [JAM_004] Hollow Hound (6) — Lifesteal, Rush. Also damages minions next to whomever this attacks.
			cards.Add("JAM_004", new CardDef(new Power { Trigger = Cleave() }));

			// ---------------------------------------------------------------- Mage
			// [ETC_029] Keyboard Soloist (4) — Battlecry: If you control no other minions, summon two 1/2 Amps with Spell Damage +1.
			cards.Add("ETC_029", new CardDef(new Power
			{
				PowerTask = IfSoloist(ComplexTask.Repeat(new SummonTask("ETC_029t", SummonSide.RIGHT), V("ETC_029", "amps", 2)))
			}));
			// [ETC_205] Volume Up (4) — Draw 3 spells. Finale: Discover a copy of one.
			cards.Add("ETC_205", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					bool finale = Generic.Finale(s);
					var drawn = new List<Card>();
					for (int i = 0; i < V("ETC_205", "draw", 3); i++)
						if (DrawOne(g, c, p => p is Spell) is IPlayable spell) drawn.Add(spell.Card);
					if (finale) DiscoverCopyOf(c, s, drawn);
				})
			}));
			// [ETC_206] Infinitize the Maxitude (2) — Discover a spell. Reduce its Cost by (1). Finale: Return this to your hand at
			// end of turn. (ETC_206e on the player gives a new copy at the end of the turn: a card back from the graveyard is a
			// new card.)
			cards.Add("ETC_206", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL, "ETC_206e2"),
					Finale(new AddEnchantmentTask("ETC_206e", EntityType.CONTROLLER)))
			}));
			cards.Add("ETC_206e2", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("ETC_206", "discount", 1))) }));
			cards.Add("ETC_206e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						AddToHand(c, Cards.FromId("ETC_206"));
						if (s is Enchantment e) e.Remove();
					})
				}
			}));
			// [ETC_395] DJ Manastorm (9) — Battlecry: Set the Cost of spells in your hand to (0). After you cast one, the others
			// cost (1) more. (Each gets ETC_395e: Cost 0, and HsTag.MANASTORM_DISCOUNT = this Manastorm; casting one of them gives
			// the rest in hand ETC_395e2, +1 Cost.)
			cards.Add("ETC_395", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable spell in c.HandZone.Where(p => p is Spell).ToList())
					{
						spell[HsTag.MANASTORM_DISCOUNT] = s.Id;
						Give(g, s, spell, "ETC_395e");
					}
				})
			}));
			cards.Add("ETC_395e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.SetCost(0)),
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || !(e.Target is IPlayable mine) || mine.Zone?.Type != Zone.HAND) return;
						if (!(g.CurrentEventData?.EventSource is IPlayable cast) || cast.Id == mine.Id) return;
						if (cast[HsTag.MANASTORM_DISCOUNT] == 0 || cast[HsTag.MANASTORM_DISCOUNT] != mine[HsTag.MANASTORM_DISCOUNT]) return;
						Give(g, e.Creator ?? (IPlayable)c.Hero, mine, "ETC_395e2");
					})
				}
			}));
			cards.Add("ETC_395e2", new CardDef(new Power { Enchant = new Enchant(Effects.AddCost(V("ETC_395", "more", 1))) }));
			// [ETC_521] Cosmic Keyboard (2) — After you cast a spell, summon an Elemental with stats equal to its Cost. Lose 1 Durability.
			cards.Add("ETC_521", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(g.CurrentEventData?.EventSource is IPlayable cast)) return;
						int cost = PlayedCost(cast);
						SummonWithStats(g, c, Cards.FromId("ETC_521t"), cost, cost, s);
						LoseDurability(s);
					})
				}
			}));
			// [ETC_528] Lightshow (3) — Shoot @ beams at enemies that each deal $2 damage. Your future Lightshows shoot one more
			// beam. (@ = its NUM_1, 2, plus the Lightshows the player cast: HsTag.LIGHTSHOWS_CAST; the ones in hand and deck show
			// the new number.)
			cards.Add("ETC_528", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int beams = ((IPlayable)s).Card[GameTag.TAG_SCRIPT_DATA_NUM_1] + c[HsTag.LIGHTSHOWS_CAST];
					for (int i = 0; i < beams; i++)
					{
						var enemies = Enemies(c).Where(x => !x.IsDead).ToList();
						if (enemies.Count == 0) break;
						g.PowerHistory?.NewTargetRound();
						Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], V("ETC_528", "damage", 2), true);
					}
					c[HsTag.LIGHTSHOWS_CAST] += 1;
					foreach (IPlayable other in c.HandZone.Concat(c.DeckZone).Where(p => p.Card.Id == "ETC_528").ToList())
						other[GameTag.TAG_SCRIPT_DATA_NUM_1] = other.Card[GameTag.TAG_SCRIPT_DATA_NUM_1] + c[HsTag.LIGHTSHOWS_CAST];
				})
			}));
			// [ETC_532] Rewind (2) — Discover a copy of another spell you've cast this game.
			cards.Add("ETC_532", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					DiscoverCopyOf(c, s, SpellsCast(c).Where(h => h.SourceId != s.Id && h.SourceCard.Id != ((IPlayable)s).Card.Id).Select(h => h.SourceCard)))
			}));
			// [ETC_534] Holotechnician (3) — After ANY minion takes exactly 1 damage, destroy it.
			cards.Add("ETC_534", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.ALL_MINIONS,
					Condition = new SelfCondition(p => p.Game.CurrentEventData?.EventNumber == 1),
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m && !m.IsDead && !m.ToBeDestroyed) Generic.Destroy(m, s); })
				}
			}));
			// [ETC_535] Synthesize (1) — Add a random 1, 2, and 3-Cost Elemental to your hand.
			cards.Add("ETC_535", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (int cost in new[] { 1, 2, 3 })
						if (RandomCard(g, x => x.Type == CardType.MINION && x.Cost == cost && x.IsRace(Race.ELEMENTAL)) is Card e) AddToHand(c, e);
				})
			}));
			// [ETC_536] Audio Splitter (2) — Deathrattle: Copy the highest Cost spell in your hand.
			cards.Add("ETC_536", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var spells = c.HandZone.Where(p => p is Spell).ToList();
					if (spells.Count == 0) return;
					int max = spells.Max(p => p.Cost);
					var top = spells.Where(p => p.Cost == max).ToList();
					AddToHand(c, top[g.Random.Next(top.Count)].Card);
				})
			}));
			// [JAM_000] Remixed Dispense-o-bot (3) — Gains an extra effect in your hand that changes each turn. Versions: t two
			// random Frost spells, t2 two random Mechs, t3 two Coins, t4 two random Mage Secrets.
			cards.Add("JAM_000", new CardDef(new Power { NoScript = true }));
			void Dispense(string id, System.Func<Card, bool> what)
			{
				cards.Add(id, new CardDef(new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) =>
					{
						for (int i = 0; i < V("JAM_000", "cards", 2); i++)
							if (RandomCard(g, what) is Card got) AddToHand(c, got);
					})
				}));
			}
			Dispense("JAM_000t", x => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == FROST);
			Dispense("JAM_000t2", x => x.Type == CardType.MINION && x.IsRace(Race.MECHANICAL));
			cards.Add("JAM_000t3", new CardDef(new Power { PowerTask = new AddCardTo("GAME_005", EntityType.HAND, V("JAM_000", "cards", 2)) }));
			Dispense("JAM_000t4", x => x.Type == CardType.SPELL && x[GameTag.SECRET] > 0 && x.HasClass(CardClass.MAGE));
			// [JAM_001] Costumed Singer (1) — At the end of your turn, draw a Secret.
			cards.Add("JAM_001", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p is Spell sp && sp.IsSecret)) }
			}));
			// [JAM_002] Star Power (5) — Deal $5 damage to a random enemy minion. Repeat this with 1 less damage.
			cards.Add("JAM_002", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int damage = V("JAM_002", "damage", 5); damage > 0; damage--)
					{
						var enemies = c.Opponent.BoardZone.Where(m => !m.IsDead && !m.ToBeDestroyed).ToList();
						if (enemies.Count == 0) break;
						g.PowerHistory?.NewTargetRound();
						Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], damage, true);
						g.DeathProcessingAndAuraUpdate();
					}
				})
			}));

			// ---------------------------------------------------------------- Paladin
			// [ETC_317] Disco Maul (3) — Deathrattle: Give a random friendly minion +@/+@. (Play minions while equipped to improve!)
			cards.Add("ETC_317", new CardDef(new Power
			{
				Trigger = Improves(TriggerType.PLAY_MINION, TriggerSource.FRIENDLY),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (RandomFriendly(g, c) is Minion m) Generic.AddEnchantmentBlock(g, Cards.FromId("ETC_317e"), (IPlayable)s, m, Num1(s), Num1(s), 0);
				})
			}));
			cards.Add("ETC_317e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));
			// [ETC_318] Boogie Down (3) — Summon two 1-Cost minions from your deck. Finale: Summon another.
			cards.Add("ETC_318", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = V("ETC_318", "minions", 2) + (Generic.Finale(s) ? 1 : 0);
					for (int i = 0; i < n; i++) SummonFromDeck(g, c, s, p => p.Cost == V("ETC_318", "cost", 1));
				})
			}));
			// [ETC_320] Spotlight (2) — Tradeable. Convert a friendly Divine Shield into a 5/5 Elemental.
			cards.Add("ETC_320", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_DIVINE_SHIELD, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is ICharacter ch) || ch[GameTag.DIVINE_SHIELD] == 0) return;
					ch[GameTag.DIVINE_SHIELD] = 0;
					Summon(g, c, Cards.FromId("ETC_320t"), s);
				})
			}));
			// [ETC_321] Annoy-o-Troupe (9) — Taunt, Divine Shield. Deathrattle: Summon three 1/2 Mechs with Taunt and Divine
			// Shield. (The client's related CORE_GVG_085 Annoy-o-Tron.)
			cards.Add("ETC_321", new CardDef(new Power
			{
				DeathrattleTask = ComplexTask.Repeat(new SummonTask("CORE_GVG_085", SummonSide.DEATHRATTLE), V("ETC_321", "mechs", 3))
			}));
			// [ETC_324] Jitterbug (4) — Divine Shield. After a friendly character loses Divine Shield, draw a card.
			cards.Add("ETC_324", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.LOSE_DIVINE_SHIELD) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = new DrawTask() }
			}));
			// [ETC_328] Lead Dancer (6) — Deathrattle: Summon a minion from your deck with less Attack than this minion.
			cards.Add("ETC_328", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					int attack = s is Minion me ? me.AttackDamage : 0;
					SummonFromDeck(g, c, s, p => p is Minion m && m.AttackDamage < attack);
				})
			}));
			// [ETC_329] Kangor, Dancing King (5) — Lifesteal. Deathrattle: Swap this with a minion from your hand and give it
			// Lifesteal. User 2026-10-07: it swaps with a random minion from your hand — Kangor goes back to the hand (a new card:
			// it died; Generic.ReturnToHand), the minion comes onto the board in Kangor's place (its last board position: a swap,
			// not the rulebook's "Deathrattle summons from the hand go far right") and gets Lifesteal. A full board: no swap.
			cards.Add("ETC_329", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var hand = c.HandZone.OfType<Minion>().Where(m => Generic.IsMinionCard(m)).ToList();
					if (hand.Count == 0 || c.BoardZone.IsFull || !(s is Minion kangor)) return;
					Minion swap = hand[g.Random.Next(hand.Count)];
					int pos = kangor.Zone?.Type == Zone.PLAY ? kangor.ZonePosition : kangor.LastBoardPosition;
					c.HandZone.Remove(swap);
					Generic.ReturnToHand(c, kangor);
					Generic.SummonBlock.Invoke(g, swap, System.Math.Max(0, pos), s);
					if (swap.Zone?.Type == Zone.PLAY) swap[GameTag.LIFESTEAL] = 1;
				})
			}));
			// [ETC_330] Starlight Groove (3) — Give your hero Divine Shield. For the rest of the game, playing a Holy spell
			// refreshes it. (ETC_330e1 on the player.)
			cards.Add("ETC_330", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					c.Hero[GameTag.DIVINE_SHIELD] = 1;
					if (c.AppliedEnchantments?.Any(e => e.Card.Id == "ETC_330e1") != true)
						Generic.AddEnchantmentBlock(g, Cards.FromId("ETC_330e1"), (IPlayable)s, c, 0, 0, 0);
				})
			}));
			cards.Add("ETC_330e1", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Spell && p[SPELL_SCHOOL] == HOLY),
					SingleTask = new CustomTask((g, c, s, t, stack) => c.Hero[GameTag.DIVINE_SHIELD] = 1)
				}
			}));
			// [ETC_337] Funkfin (3) — Divine Shield. Your minions with Divine Shield have +2 Attack.
			cards.Add("ETC_337", new CardDef(new Power
			{
				Aura = new Aura(AuraType.BOARD, "ETC_337e") { Condition = new SelfCondition(p => p is Minion m && m.HasDivineShield), Restless = true }
			}));
			cards.Add("ETC_337e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("ETC_337", "attack", 2))) }));
			// [ETC_506] Harmonic Disco (5) — Discover a 5-Cost minion. Summon it with +1/+1. / [ETC_506t] Dissonant Disco — a
			// 1-Cost minion with +5/+5.
			cards.Add("ETC_506", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.Cost == V("ETC_506", "cost", 5), "ETC_506e", null, ChoiceAction.SUMMON)
			}));
			cards.Add("ETC_506t", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.Cost == V("ETC_506t", "cost", 1), "ETC_506te", null, ChoiceAction.SUMMON)
			}));
			cards.Add("ETC_506e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ETC_506", "stats", 1))) }));
			cards.Add("ETC_506te", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ETC_506t", "stats", 5))) }));
			// [JAM_009] Dance Floor (2, Location) — Give your minions Rush.
			cards.Add("JAM_009", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.Where(m => !m.IsDead), "JAM_009e"))
			}));
			cards.Add("JAM_009e", new CardDef(new Power { Enchant = new Enchant(Effects.Rush) }));
			// [JAM_010] Jukebox Totem (2) — At the end of your turn, summon a 1/1 Silver Hand Recruit.
			cards.Add("JAM_010", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new SummonTask("CS2_101t", SummonSide.RIGHT) }
			}));
		}

		/// <summary>Rhythm and Roots' pick: ETC_387t into the Secret zone (CREATOR the played card), its option
		/// <paramref name="which"/> as HIDDEN_CHOICE (the owner's only), lasting <paramref name="turns"/> of the player's turns
		/// (kept on the server). A full Secret zone: nothing.</summary>
		private static void StartRhythm(Controller c, IEntity played, int turns, int which)
		{
			var rhythm = (Spell)Entity.FromCard(c, Cards.FromId("ETC_387t"), new EntityData
			{
				[GameTag.HIDDEN_CHOICE] = which, [HsTag.HIDDEN_OBJECTIVE_TURNS] = turns, [GameTag.CREATOR] = played?.Id ?? 0,
			});
			Generic.PutSecretIntoPlay(c, rhythm);
		}
	}
}
