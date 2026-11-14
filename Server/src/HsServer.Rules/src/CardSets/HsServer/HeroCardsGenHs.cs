// HsServer addition (not upstream SabberStone): playable Hero cards the pools and the coverage tool used to leave out (upstream
// dropped every HERO-type card; Cards.IsHeroPortrait keeps only the portraits out now — 2026-10-07). Alterac Valley's Hero cards,
// The Amazing Reno, Al'Akir and Reno, Lone Ranger, with their hero powers and tokens. Every number goes through CardValues (V).
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

		/// <summary>"Flip / Swap each turn" for a hero power (Xyrella's Holy Touch ↔ Void Spike, Reno's bullets): at the end of its
		/// owner's turn it becomes <paramref name="next"/> (the user's Harmonic ruling: the swap at the end of the owner's turn).</summary>
		private static Trigger HeroPowerSwapsEachTurn(Func<Game, IEntity, Card> next) => new Trigger(TriggerType.TURN_END)
		{
			SingleTask = new CustomTask((g, c, s, t, stack) =>
			{
				if (!(s is HeroPower hp) || c.Hero?.HeroPower != hp || !(next(g, s) is Card other)) return;
				new ReplaceHeroPower(other).Process(g, c, s, null);
			})
		};

		/// <summary>Reno, Lone Ranger's bullets (WW_0700p1-p7: the hero power cards "<id>p" + a number).</summary>
		private static List<Card> RenoBullets() =>
			Enumerable.Range(1, 9).Select(i => Cards.TryFromId("WW_0700p" + i)).Where(x => x != null && x.Type == CardType.HERO_POWER).ToList();

		private static Card OtherRenoBullet(Game g, IEntity current)
		{
			var bullets = RenoBullets().Where(x => x.Id != (current as IPlayable)?.Card.Id).ToList();
			return bullets.Count == 0 ? null : bullets[g.Random.Next(bullets.Count)];
		}

		/// <summary>A Reno bullet: $2 to the target, then its own extra.</summary>
		private static CardDef RenoBullet(Action<Game, Controller, IEntity, IPlayable> extra) => new CardDef(TargetAny, new Power
		{
			PowerTask = new CustomTask((g, c, s, t, stack) =>
			{
				if (t is ICharacter target) Generic.DamageCharFunc((IPlayable)s, target, V("WW_0700p", "damage", 2), false);
				extra?.Invoke(g, c, s, t);
			}),
			Trigger = HeroPowerSwapsEachTurn(OtherRenoBullet)
		});

		/// <summary>Beaststalker Tavish's Discover-and-cast: an Improved Secret (its related Secret cards) not already active.</summary>
		private static void TavishDiscover(Game g, Controller c, IEntity source, int left)
		{
			if (left <= 0) return;
			var active = new HashSet<string>(c.SecretZone.Select(x => x.Card.Id));
			var pool = (ReprintCards.RelatedCardIds("AV_113") ?? new List<string>()).Select(Cards.TryFromId)
				.Where(x => x != null && x.IsSecret && !active.Contains(x.Id));
			Card[] offer = DiscoverFromTask.PickDistinct(g, pool, 3).ToArray();
			if (offer.Length == 0) return;
			Generic.CreateChoiceCards.Invoke(c, source, null, ChoiceType.GENERAL, ChoiceAction.STACK, offer, new CustomTask((g2, c2, s2, pick, st) =>
			{
				if (pick != null) new CastRandomSpellTask { Spell = pick.Card }.Process(g2, c2, s2, null);
				TavishDiscover(g2, c2, s2, left - 1);
			}));
		}

		private static void PlayableHeroCards(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Fractured in Alterac Valley (1626)
			// [AV_200] Magister Dawngrasp (7) — Battlecry: Recast a spell from each spell school you've cast this game.
			// (one random spell per school, in the order the schools were first cast; random targets — doubt)
			cards.Add("AV_200", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var bySchool = c.PlayHistory.Where(h => h.SourceCard?.Type == CardType.SPELL && h.SourceCard[SPELL_SCHOOL] > 0)
						.GroupBy(h => h.SourceCard[SPELL_SCHOOL]).ToList();
					foreach (var school in bySchool)
					{
						var spells = school.Select(h => h.SourceCard).ToList();
						new CastRandomSpellTask { Spell = spells[g.Random.Next(spells.Count)] }.Process(g, c, s, null);
					}
				})
			}));
			// [AV_200p2] Arcane Burst (2) — Deal $@ damage. Honorable Kill: Gain +2 damage. (its number on itself)
			cards.Add("AV_200p2", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter target && s is IPlayable hp) Generic.DamageCharFunc(hp, target, hp[GameTag.TAG_SCRIPT_DATA_NUM_1], false);
				}),
				HonorableKillTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (s is IPlayable hp) hp[GameTag.TAG_SCRIPT_DATA_NUM_1] = hp[GameTag.TAG_SCRIPT_DATA_NUM_1] + V("AV_200p2", "gain", 2);
				})
			}));
			// [AV_202] Rokara, the Valorous (7) — Battlecry: Equip a 5/2 Unstoppable Force.
			cards.Add("AV_202", new CardDef(new Power { PowerTask = new WeaponTask("AV_202t2") }));
			// [AV_202p] Grand Slam (2) — Deal $2 damage. Honorable Kill: Gain 4 Armor.
			cards.Add("AV_202p", new CardDef(TargetAny, new Power
			{
				PowerTask = new DamageTask(V("AV_202p", "damage", 2), EntityType.TARGET),
				HonorableKillTask = new ArmorTask(V("AV_202p", "armor", 4))
			}));
			// [AV_202t2] The Unstoppable Force (5/2 weapon) — After you attack a minion, smash it into the enemy hero!
			// (the attacked minion deals its Attack to its own hero — doubt: unrecorded)
			cards.Add("AV_202t2", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(g.CurrentEventData?.EventTarget is Minion victim)) return;
						int attack = Math.Max(0, victim.AttackDamage);
						if (attack > 0) Generic.DamageCharFunc(victim, c.Opponent.Hero, attack, false);
					})
				}
			}));
			// [AV_203] Shadowcrafter Scabbs (8) — Battlecry: Return all minions to their owner's hands. Summon two 4/2 Shadows with Stealth.
			cards.Add("AV_203", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => !m.IsDead).ToList())
						Generic.ReturnToHand(m.Controller, m);
					for (int i = 0; i < V("AV_203", "shadows", 2); i++) Summon(g, c, Cards.FromId("AV_203t"), s);
				})
			}));
			// [AV_203p] Sleight of Hand (2) — The next card you play this turn costs (2) less.
			cards.Add("AV_203p", new CardDef(new Power { PowerTask = new AddEnchantmentTask("AV_203pe", EntityType.CONTROLLER) }));
			cards.Add("AV_203pe", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("AV_203p", "discount", 2))) { ShowAs = "AV_203po" },
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.PLAY_CARD) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = RemoveEnchantmentTask.Task },
					new Trigger(TriggerType.TURN_END) { SingleTask = RemoveEnchantmentTask.Task })
			}));
			// [AV_204] Kurtrus, Demon-Render (6) — Battlecry: Summon two @/4 Demons with Rush. (Improved by your hero attacks this game.)
			int felbatAttack(Controller c) => (Cards.TryFromId("AV_204t2")?.ATK ?? 1) + (c?[HsTag.HERO_ATTACKS_THIS_GAME] ?? 0);
			cards.Add("AV_204", new CardDef(new Power
			{
				HandCounter = p => felbatAttack(p.Controller),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("AV_204", "demons", 2) && !c.BoardZone.IsFull; i++)
					{
						var bat = (Minion)Entity.FromCard(c, Cards.FromId("AV_204t2"), new EntityData { [GameTag.ATK] = felbatAttack(c), [GameTag.CREATOR] = s.Id });
						Generic.SummonBlock.Invoke(g, bat, -1, s);
					}
				})
			}));
			// [AV_204p] Ashfallen's Fury (1) — +2 Attack this turn. After a friendly minion attacks, refresh this.
			cards.Add("AV_204p", new CardDef(new Power
			{
				PowerTask = new AddEnchantmentTask("AV_204e", EntityType.HERO),
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.MINIONS,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is HeroPower hp && hp.IsExhausted) hp.IsExhausted = false; })
				}
			}));
			cards.Add("AV_204e", OneTurn(Effects.Attack_N(V("AV_204p", "attack", 2))));
			// [AV_205] Wildheart Guff (5) — Battlecry: Set your maximum Mana to 20. Gain an empty Mana Crystal. Draw a card.
			cards.Add("AV_205", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => c[GameTag.MAXRESOURCES] = V("AV_205", "max mana", 20)),
					new ManaCrystalEmptyTask(1),
					new DrawTask())
			}));
			// [AV_205p] Nurture (2) — Choose One - Draw a card; or Gain an empty Mana Crystal. (its options: AV_205a / AV_205pb)
			cards.Add("AV_205p", new CardDef(new Power()));
			cards.Add("AV_205a", new CardDef(new Power { PowerTask = new ManaCrystalEmptyTask(1) }));
			cards.Add("AV_205pb", new CardDef(new Power { PowerTask = new DrawTask() }));
			// [AV_206] Lightforged Cariel (8) — Battlecry: Deal 2 damage to all enemies. Equip a 2/5 Immovable Object.
			cards.Add("AV_206", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("AV_206", "damage", 2), EntityType.ENEMIES), new WeaponTask("AV_146"))
			}));
			// [AV_146] The Immovable Object (2/5 weapon) — This doesn't lose Durability. Your hero takes half damage, rounded up.
			cards.Add("AV_146", new CardDef(new Power
			{
				Aura = new AdaptiveEffect(new SelfCondition(w => true), HsTag.KEEPS_DURABILITY),
				Trigger = new Trigger(TriggerType.PREDAMAGE) { TriggerSource = TriggerSource.HERO, FastExecution = true, SingleTask = Generic.PreDamageHalve() }
			}));
			// [AV_206p] Blessing of Queens (2) — Give a random minion in your hand +4/+4.
			cards.Add("AV_206p", new CardDef(new Power { PowerTask = BuffRandomInHand(p => p is Minion, "AV_206pe") }));
			cards.Add("AV_206pe", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("AV_206p", "stats", 4))) }));
			// [AV_207] Xyrella, the Devout (8) — Battlecry: Trigger the Deathrattle of every friendly minion that died this game.
			cards.Add("AV_207", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable dead in c.DiedThisGameCards.Where(p => p is Minion && p.Card.Power?.DeathrattleTask != null).ToList())
						TriggerDeathrattleOf(g, c, dead);
				})
			}));
			// [AV_207p] Holy Touch (2) — Restore 5 Health. Flip each turn. / [AV_207p2] Void Spike (2) — Deal $5 damage. Flip each turn.
			cards.Add("AV_207p", new CardDef(TargetAny, new Power
			{
				PowerTask = new HealTask(V("AV_207p", "heal", 5), EntityType.TARGET),
				Trigger = HeroPowerSwapsEachTurn((g, s) => Cards.FromId("AV_207p2"))
			}));
			cards.Add("AV_207p2", new CardDef(TargetAny, new Power
			{
				PowerTask = new DamageTask(V("AV_207p2", "damage", 5), EntityType.TARGET),
				Trigger = HeroPowerSwapsEachTurn((g, s) => Cards.FromId("AV_207p"))
			}));
			// [AV_316] Dreadlich Tamsin (6) — Battlecry: Deal 3 damage to all minions. Shuffle 3 Rifts into your deck. Draw 3 cards.
			ISimpleTask shuffleRift = new CustomTask((g, c, s, t, stack) =>
				Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("AV_316t4"), new EntityData { [GameTag.CREATOR] = s.Id })));
			cards.Add("AV_316", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					new DamageTask(V("AV_316", "damage", 3), EntityType.ALLMINIONS).Process(g, c, s, null);
					for (int i = 0; i < V("AV_316", "rifts", 3); i++) shuffleRift.Process(g, c, s, null);
					for (int i = 0; i < V("AV_316", "draw", 3); i++) Generic.Draw(c);
				})
			}));
			// [AV_316hp] Chains of Dread (2) — Shuffle a Rift into your deck. Draw a card.
			cards.Add("AV_316hp", new CardDef(new Power { PowerTask = ComplexTask.Create(shuffleRift, new DrawTask()) }));
			// [AV_113] Beaststalker Tavish (6) — Battlecry: Discover and cast 2 Improved Secrets.
			cards.Add("AV_113", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => TavishDiscover(g, c, s, V("AV_113", "secrets", 2))) }));
			// [AV_113p] Summon Pet (2) — Summon an Animal Companion.
			cards.Add("AV_113p", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.SummonCompanions(c, s, 1))
			}));
			// The Improved Secrets (the base Secrets' rules with the bigger numbers)
			cards.Add("AV_113t1", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					Condition = SelfCondition.IsProposedDefender(CardType.HERO),
					SingleTask = ComplexTask.Secret(new DamageTask(V("AV_113t1", "damage", 3), EntityType.ENEMIES, true))
				}
			}));
			cards.Add("AV_113t2", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.OP_MINIONS,
					SingleTask = ComplexTask.Create(
						new ConditionTask(EntityType.TARGET, SelfCondition.IsNotDead),
						new FlagTask(true, ComplexTask.Secret(new ReturnHandTask(EntityType.TARGET), new AddEnchantmentTask("AV_113t2e", EntityType.TARGET))))
				}
			}));
			cards.Add("AV_113t2e", new CardDef(new Power { Enchant = new Enchant(Effects.AddCost(V("AV_113t2", "cost", 4))) }));
			cards.Add("AV_113t3", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					Condition = SelfCondition.IsProposedDefender(CardType.MINION),
					SingleTask = ComplexTask.Secret(new SummonTask("AV_113t3t2", V("AV_113t3", "snakes", 3)))
				}
			}));
			cards.Add("AV_113t7", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					Condition = SelfCondition.IsEventTargetIs(CardType.MINION),
					SingleTask = ComplexTask.Secret(
						new SummonCopyTask(EntityType.EVENT_TARGET, addToStack: true), new SummonCopyTask(EntityType.EVENT_TARGET, addToStack: true),
						new AddEnchantmentTask("BT_203e", EntityType.STACK))
				}
			}));
			cards.Add("AV_113t8", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					Condition = new SelfCondition(p => p.Controller.BoardZone.Count >= V("AV_113t8", "minions", 2) && !p.Controller.BoardZone.IsFull),
					SingleTask = ComplexTask.Secret(new CustomTask((g, c, s, t, stack) =>
					{
						Generic.SummonCompanions(c, s, V("AV_113t8", "companions", 2));
					}))
				}
			}));
			cards.Add("AV_113t9", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.CAST_SPELL)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = ComplexTask.Secret(
						new CounterTask(EntityType.TARGET),
						new CustomTask((g, c, s, t, stack) =>
						{
							if (t == null || c.Opponent.HandZone.IsFull) return;
							IPlayable back = Entity.FromCard(c.Opponent, t.Card);
							Generic.AddHandPhase.Invoke(c.Opponent, back);
							Give(g, s, back, "AV_113t9e");
						}))
				}
			}));
			cards.Add("AV_113t9e", new CardDef(new Power { Enchant = new Enchant(Effects.AddCost(V("AV_113t9", "cost", 2))) }));
			cards.Add("MAW_010t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.ENEMY,
					Condition = new SelfCondition(p => p.Controller.NumCardsPlayedThisTurn == 3),
					SingleTask = ComplexTask.Secret(new DamageTask(V("MAW_010t", "damage", 9), EntityType.OP_HERO, true))
				}
			}));

			// ---------------------------------------------------------------- Rise of Shadows (1403)
			// [YOD_009] The Amazing Reno (10) — Battlecry: Make all minions disappear. *Poof!* (removed from the game, no deaths)
			cards.Add("YOD_009", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetSpan().ToArray().Concat(c.Opponent.BoardZone.GetSpan().ToArray()).Where(m => !(m is Location)).ToList())
						Generic.RemoveFromGame(m, s);
				})
			}));
			// [YOD_009h] What Does This Do? — Passive. At the start of your turn, cast a random spell.
			cards.Add("YOD_009h", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = new CastRandomSpellTask() }
			}));

			// ---------------------------------------------------------------- Caverns of Time (1898)
			// [WON_092h] Al'Akir, the Winds of Time (7) — Battlecry: Draw a Charge, Divine Shield, Taunt, and Windfury minion.
			cards.Add("WON_092h", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (GameTag keyword in new[] { GameTag.CHARGE, GameTag.DIVINE_SHIELD, GameTag.TAUNT, GameTag.WINDFURY })
						DrawOne(g, c, p => p is Minion && p.Card[keyword] > 0);
				})
			}));
			// [WON_092p] SWAT, INSECTS! (2) — Give your hero Divine Shield. They gain +2 Attack and Windfury this turn.
			cards.Add("WON_092p", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					c.Hero[GameTag.DIVINE_SHIELD] = 1;
					Give(g, s, c.Hero, "WON_092he");
				})
			}));
			cards.Add("WON_092he", OneTurn(Effects.Attack_N(V("WON_092p", "attack", 2)), Effects.Windfury));

			// ---------------------------------------------------------------- Showdown in the Badlands (1892)
			// [WW_0700] Reno, Lone Ranger (8) — Battlecry: If your deck started with no duplicates, remove all enemy minions from the game.
			// (then the hero power shows this turn's bullet; the bullets swap at the end of each of its owner's turns — doubt)
			cards.Add("WW_0700", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (Generic.DeckStartedWithNoDuplicates(c))
						foreach (Minion m in c.Opponent.BoardZone.GetSpan().ToArray().Where(m => !(m is Location)).ToList())
							Generic.RemoveFromGame(m, s);
				}),
				// (2026-10-08: his Hero Power arrives after the Battlecry — rulebook :403 — so this turn's bullet is set then)
				AfterHeroPowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.Hero?.HeroPower?.Card.Id == "WW_0700p" && OtherRenoBullet(g, c.Hero.HeroPower) is Card bullet)
						new ReplaceHeroPower(bullet).Process(g, c, s, null);
				})
			}));
			// [WW_0700p] Reno's Handcannon (2) — Shoot this turn's magic bullet!
			cards.Add("WW_0700p", RenoBullet(null));
			cards.Add("WW_0700p1", RenoBullet((g, c, s, t) => RefreshMana(c, V("WW_0700p1", "mana", 2))));
			cards.Add("WW_0700p2", RenoBullet((g, c, s, t) => c.Hero.Armor += V("WW_0700p2", "armor", 4)));
			cards.Add("WW_0700p3", RenoBullet((g, c, s, t) => new DamageTask(V("WW_0700p3", "splash", 1), EntityType.OP_MINIONS).Process(g, c, s, null)));
			cards.Add("WW_0700p4", RenoBullet((g, c, s, t) =>
			{
				var mine = c.BoardZone.GetAll().Where(m => !m.IsDead).ToList();
				if (mine.Count > 0) Give(g, s, mine[g.Random.Next(mine.Count)], "WW_0700p4e1");
			}));
			cards.Add("WW_0700p4e1", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("WW_0700p4", "stats", 2))) }));
			cards.Add("WW_0700p5", RenoBullet((g, c, s, t) => DiscoverFromTask.ClassOrNeutral((c2, x) => x.Type == CardType.SPELL).Process(g, c, s, null)));
			cards.Add("WW_0700p6", RenoBullet((g, c, s, t) => Summon(g, c, RandomMinionOfCost(g, V("WW_0700p6", "cost", 3)), s)));
			cards.Add("WW_0700p7", RenoBullet((g, c, s, t) => Generic.Draw(c)));
		}
	}
}
