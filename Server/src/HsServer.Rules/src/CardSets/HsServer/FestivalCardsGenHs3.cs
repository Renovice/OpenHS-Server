// HsServer addition (not upstream SabberStone): Festival of Legends — Priest, Rogue, Shaman, Warlock and Warrior cards (shared
// pieces: FestivalCardsGenHs.cs). Every number goes through CardValues (V).
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
		/// <summary>"For each one Overhealed": the Overheals <paramref name="heal"/> caused (Character.Overheal counts them on
		/// the healer's player: HsTag.OVERHEALS_DONE).</summary>
		private static int OverhealsDuring(Controller c, System.Action heal)
		{
			int before = c[HsTag.OVERHEALS_DONE];
			heal();
			return c[HsTag.OVERHEALS_DONE] - before;
		}

		/// <summary>The Riff the player played last before <paramref name="self"/> (a card with the client's Riff tag), or null.</summary>
		private static Card LastRiff(Controller c, IEntity self) =>
			c.PlayHistory.Where(h => h.SourceId != self.Id && h.SourceCard != null && h.SourceCard[Generic.RIFF] > 0).Select(h => h.SourceCard).LastOrDefault();

		/// <summary>
		/// "Finale: Play your last Riff" — the Riffs' second effect (Power.SecondPowerTask). Live (live-20261007-vfx-decks): the
		/// Riff Stopper ETC_363e2 goes on the player, then a new copy of the last Riff is cast (shown big) with both its blocks
		/// inside this one; in that copy's second block the Stopper is used up (LAST_AFFECTED_BY the copy, to the graveyard)
		/// instead of a Finale — no loop. With no earlier Riff the block stays empty.
		/// </summary>
		private static ISimpleTask PlayLastRiff => new CustomTask((g, c, s, t, stack) =>
		{
			if (c[HsTag.RIFF_STOPPERS] > 0)
			{
				c[HsTag.RIFF_STOPPERS] -= 1;
				if (c.AppliedEnchantments?.FirstOrDefault(e => e.Card.Id == "ETC_363e2") is Enchantment stopper)
				{
					Generic.NoteAffected(stopper, s.Id);
					stopper.Remove();
				}
				return;
			}
			if (!Generic.Finale(s) || !(LastRiff(c, s) is Card riff)) return;
			c[HsTag.RIFF_STOPPERS] += 1;
			Generic.AddEnchantmentBlock(g, Cards.FromId("ETC_363e2"), (IPlayable)s, c, 0, 0, 0);
			PlayForFree(g, c, s, riff);
		});

		/// <summary>The seven Movements of Symphony of Sins (the client's ETC_085t.. related spells).</summary>
		private static readonly string[] Movements = { "ETC_085t", "ETC_085t2", "ETC_085t3", "ETC_085t4", "ETC_085t6", "ETC_085t7", "ETC_085t8" };

		private static void FestivalClasses2(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Priest
			// [ETC_305] Shadow Chord: Distort (3) — Give a minion -5/-5. If it has 0 Attack, destroy it.
			cards.Add("ETC_305", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Give(g, s, m, "ETC_305e");
					if (m.AttackDamage <= 0 && !m.IsDead) Generic.Destroy(m, s);
				})
			}));
			cards.Add("ETC_305e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(-V("ETC_305", "stats", 5))) }));
			// [ETC_312] Idol's Adoration (1, weapon) — Your Hero Power costs (0). After you use it, lose 1 Durability.
			cards.Add("ETC_312", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HEROPOWER, Effects.SetCost(0)),
				Trigger = new Trigger(TriggerType.INSPIRE) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = new CustomTask((g, c, s, t, stack) => LoseDurability(s)) }
			}));
			// [ETC_314] Harmonic Pop (6) — Deal $3 damage to all minions. Summon a 6/6 Popstar. / [ETC_314t] Dissonant Pop — $6
			// and a 3/3 Popstar. (The Popstar ETC_314t2, its stats set.)
			cards.Add("ETC_314", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("ETC_314", "damage", 3), EntityType.ALLMINIONS, true),
					new CustomTask((g, c, s, t, stack) => SummonWithStats(g, c, Cards.FromId("ETC_314t2"), V("ETC_314", "popstar", 6), V("ETC_314", "popstar", 6), s)))
			}));
			cards.Add("ETC_314t", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("ETC_314t", "damage", 6), EntityType.ALLMINIONS, true),
					new CustomTask((g, c, s, t, stack) => SummonWithStats(g, c, Cards.FromId("ETC_314t2"), V("ETC_314t", "popstar", 3), V("ETC_314t", "popstar", 3), s)))
			}));
			// [ETC_316] Fight Over Me (4) — Choose two enemy minions. They fight! Add copies of any that die to your hand. User
			// 2026-10-07: two separate targets, the first then the second. The first is the spell's target; the second a board
			// target pick (Generic.PickTargets: the targeting arrow again, ChoiceType.TARGET) among the other enemy minions the
			// spell could target. "They fight!" as the set's other fights (Proving Grounds, user 2026-10-06: "they attack each
			// other"): the first attacks the second (EffectAttack: each deals its Attack to the other at once, as combat). Then
			// a copy of each that died goes to the hand (Generic.Copy: a new card shown as created by the spell).
			cards.Add("ETC_316", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_MINIMUM_ENEMY_MINIONS, 2 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion firstPick) || !(s is Playable spell)) return;
					int firstId = firstPick.Id, me = c.PlayerId; // (values only: the pick may resolve on an AI's copy of the game)
					Generic.PickTargets(c, s, 1, ch => ch is Minion m && m.Id != firstId && m.Controller.PlayerId != me && spell.IsValidPlayTarget(m),
						(g2, c2, s2, picked) =>
						{
							if (!(g2.IdEntityDic.TryGetValue(firstId, out IPlayable p) && p is Minion first)) return;
							if (!(picked is Minion second) || second.Zone?.Type != Zone.PLAY || first.Zone?.Type != Zone.PLAY) return;
							EffectAttack(g2, first, second);
							g2.DeathProcessingAndAuraUpdate();
							foreach (Minion m in new[] { first, second })
								if (m.IsDead || m.Zone?.Type == Zone.GRAVEYARD) Generic.Copy(c2, s2, m, Zone.HAND);
						});
				})
			}));
			// [ETC_332] Dreamboat (2) — Battlecry: Restore 3 Health to all other friendly minions. Gain +1/+1 for each one Overhealed.
			cards.Add("ETC_332", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int over = OverhealsDuring(c, () => new HealTask(V("ETC_332", "heal", 3), EntityType.MINIONS_NOSOURCE).Process(g, c, s, null));
					for (int i = 0; i < over; i++) Give(g, s, (IPlayable)s, "ETC_332e");
				})
			}));
			cards.Add("ETC_332e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ETC_332", "stats", 1))) }));
			// [ETC_334] Heartbreaker Hedanis (4) — Battlecry: Deal 4 damage to this minion. Overheal: Deal 5 damage to a random enemy.
			cards.Add("ETC_334", new CardDef(new Power
			{
				PowerTask = new DamageTask(V("ETC_334", "self damage", 4), EntityType.SOURCE),
				Trigger = new Trigger(TriggerType.OVERHEAL)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = ComplexTask.Create(new RandomTask(1, EntityType.ENEMIES, harmful: true), new DamageTask(V("ETC_334", "damage", 5), EntityType.STACK))
				}
			}));
			// [ETC_335] Love Everlasting (3) — Your first spell each turn costs (2) less. Lasts until you don't play a spell on your
			// turn. (ETC_335e on the player: the discount while no spell was played this turn; it leaves at the end of a turn of
			// theirs without one.)
			cards.Add("ETC_335", new CardDef(new Power { PowerTask = new AddEnchantmentTask("ETC_335e", EntityType.CONTROLLER) }));
			cards.Add("ETC_335e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("ETC_335", "discount", 2)))
				{
					Condition = new SelfCondition(p => p is Spell && !p.Controller.CardsPlayedThisTurn.Any(x => x.Type == CardType.SPELL)),
					Restless = true
				},
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Enchantment e && !c.CardsPlayedThisTurn.Any(x => x.Type == CardType.SPELL)) e.Remove();
					})
				}
			}));
			// [ETC_338] Power Chord: Synchronize (2) — Choose a minion. Add a copy of it to your hand. Finale: Give both +1/+2.
			cards.Add("ETC_338", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					bool finale = Generic.Finale(s);
					IPlayable copy = c.HandZone.IsFull ? null : Entity.FromCard(c, m.Card);
					if (copy != null && !Generic.AddHandPhase.Invoke(c, copy)) copy = null;
					if (!finale) return;
					Give(g, s, m, "ETC_338e");
					if (copy != null) Give(g, s, copy, "ETC_338e");
				})
			}));
			cards.Add("ETC_338e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("ETC_338", "attack", 1)), Effects.Health_N(V("ETC_338", "health", 2))) }));
			// [ETC_339] Heartthrob (3) — Overheal: Summon a random minion with Cost equal to the amount Overhealed.
			cards.Add("ETC_339", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.OVERHEAL)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, RandomMinionOfCost(g, g.CurrentEventData?.EventNumber ?? 0), s))
				}
			}));
			// [JAM_022] Deafen (1) — Silence a minion. Combo: Also deal $2 damage to it.
			cards.Add("JAM_022", new CardDef(TargetMinion, new Power
			{
				PowerTask = new SilenceTask(EntityType.TARGET),
				ComboTask = ComplexTask.Create(new SilenceTask(EntityType.TARGET), new DamageTask(V("JAM_022", "damage", 2), EntityType.TARGET, true))
			}));
			// [JAM_023] Plagiarizarrr (3) — Battlecry: Get a copy of the top card of your opponent's deck.
			cards.Add("JAM_023", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (c.Opponent.DeckZone.TopCard is IPlayable top) AddToHand(c, top.Card); })
			}));
			// [JAM_024] Ambient Lightspawn (4) — Finale and Overheal: Give another random friendly minion +2/+2.
			ISimpleTask ambience = new CustomTask((g, c, s, t, stack) => { if (RandomFriendly(g, c, null, s) is Minion m) Give(g, s, m, "JAM_024e"); });
			cards.Add("JAM_024", new CardDef(new Power
			{
				PowerTask = Finale(ambience),
				Trigger = new Trigger(TriggerType.OVERHEAL) { TriggerSource = TriggerSource.SELF, SingleTask = ambience }
			}));
			cards.Add("JAM_024e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("JAM_024", "stats", 2))) }));
			// [JAM_025] Funnel Cake (2) — Restore 3 Health to a minion and its neighbors. For each one Overhealed, refresh a Mana Crystal.
			cards.Add("JAM_025", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					var hit = new List<Minion> { m };
					hit.AddRange(Neighbors(m));
					int over = OverhealsDuring(c, () =>
					{
						foreach (Minion x in hit) new HealTask(V("JAM_025", "heal", 3), EntityType.TARGET).Process(g, c, s, x);
					});
					RefreshMana(c, over);
				})
			}));
			// [JAM_027] Fanboy (2) — Choose One - Give a friendly minion +2 Attack and Rush; or +2 Health and Lifesteal.
			cards.Add("JAM_027a", new CardDef(BattlecryFriendlyMinion, new Power { PowerTask = new AddEnchantmentTask("JAM_027ae", EntityType.TARGET) }));
			cards.Add("JAM_027b", new CardDef(BattlecryFriendlyMinion, new Power { PowerTask = new AddEnchantmentTask("JAM_027be", EntityType.TARGET) }));
			cards.Add("JAM_027ae", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("JAM_027a", "attack", 2)), Effects.Rush) }));
			cards.Add("JAM_027be", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("JAM_027b", "health", 2)), Effects.Lifesteal) }));

			// ---------------------------------------------------------------- Rogue
			// [ETC_072] Beatboxer (3) — Combo: Deal 4 damage randomly split among all enemies.
			cards.Add("ETC_072", new CardDef(new Power
			{
				ComboTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("ETC_072", "damage", 4); i++)
					{
						var enemies = Enemies(c).Where(x => !x.IsDead).ToList();
						if (enemies.Count == 0) break;
						g.PowerHistory?.NewTargetRound();
						Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], 1, false);
					}
				})
			}));
			// [ETC_073] Rhyme Spinner (3) — Rush. Combo: Gain +1/+1 for each other Combo card you've played this game.
			cards.Add("ETC_073", new CardDef(new Power
			{
				ComboTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = PlayedBefore(c, s).Count(h => h.SourceCard?[GameTag.COMBO] > 0);
					for (int i = 0; i < n; i++) Give(g, s, (IPlayable)s, "ETC_073e");
				})
			}));
			cards.Add("ETC_073e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ETC_073", "stats", 1))) }));
			// [ETC_074] Mixtape (1) — Discover a copy of a card your opponent played this game.
			cards.Add("ETC_074", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => DiscoverCopyOf(c, s, c.Opponent.PlayHistory.Where(h => h.SourceCard != null).Select(h => h.SourceCard)))
			}));
			// [ETC_075] Mic Drop (3) — Draw 2 cards. Finale: Give your weapon +2 Attack.
			cards.Add("ETC_075", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					bool finale = Generic.Finale(s);
					for (int i = 0; i < V("ETC_075", "draw", 2); i++) Generic.Draw(c);
					if (finale && c.Hero.Weapon is Weapon w) Give(g, s, w, "ETC_075e");
				})
			}));
			cards.Add("ETC_075e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("ETC_075", "attack", 2))) }));
			// [ETC_076] Breakdance (1) — Return a friendly minion to your hand. Summon a Dancer with its stats and Rush.
			cards.Add("ETC_076", new CardDef(TargetFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m) || m.Zone?.Type != Zone.PLAY) return;
					int atk = m.AttackDamage, hp = m.Health;
					Generic.ReturnToHand(c, m);
					SummonWithStats(g, c, Cards.FromId("ETC_076t"), atk, hp, s);
				})
			}));
			// [ETC_077] Disc Jockey (2) — Combo: Add a random Combo card to your hand.
			cards.Add("ETC_077", new CardDef(new Power
			{
				ComboTask = new CustomTask((g, c, s, t, stack) => { if (RandomCard(g, x => x[GameTag.COMBO] > 0) is Card combo) AddToHand(c, combo); })
			}));
			// [ETC_078] MC Blingtron (5) — Battlecry: Both players equip 1/2 Microphones. Your opponent's increases all damage
			// they take by 1! (Golden Microphone ETC_078t2 for the player; Broken Microphone ETC_078t for the opponent.)
			cards.Add("ETC_078", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new WeaponTask("ETC_078t2"), new WeaponTask("ETC_078t", true))
			}));
			// [ETC_078t] Broken Microphone — Your hero takes 1 additional damage from all sources.
			cards.Add("ETC_078t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PREDAMAGE)
				{
					TriggerSource = TriggerSource.HERO,
					FastExecution = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData != null && g.CurrentEventData.EventNumber > 0) g.CurrentEventData.EventNumber += V("ETC_078t", "extra", 1);
					})
				}
			}));
			// [ETC_079] Bounce Around (ft. Garona) (3) — Return all friendly minions to your hand. They cost (1) this turn.
			cards.Add("ETC_079", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.Where(m => !m.IsDead && !m.ToBeDestroyed).ToList())
						if (Generic.ReturnToHand(c, m) is IPlayable back)
							Generic.AddEnchantmentBlock(g, Cards.FromId("ETC_079e"), (IPlayable)s, back, V("ETC_079", "cost", 1) - back.Cost, 0, 0);
				})
			}));
			// "Costs (1) this turn" as a one-turn ADD of (1 - Cost), like Darkmoon's DMF_071e (a one-turn SET can't be undone)
			cards.Add("ETC_079e", new CardDef(new Power { Enchant = new Enchant(Effects.AddCost(0)) { UseScriptTag = true, IsOneTurnEffect = true } }));
			// [ETC_518] Record Scratcher (3) — Deathrattle: Refresh @ Mana Crystals. (Play Combo cards while equipped to improve!)
			cards.Add("ETC_518", new CardDef(new Power
			{
				Trigger = Improves(TriggerType.PLAY_CARD, TriggerSource.FRIENDLY, new SelfCondition(p => p.Card[GameTag.COMBO] > 0)),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => RefreshMana(c, Num1(s)))
			}));
			// [ETC_717] Harmonic Hip Hop (2) — Deal $1 damage. Give your weapon +3 Attack. / [ETC_717t] Dissonant — $3, +1 Attack.
			foreach (var (id, damage, attack, ench) in new[] { ("ETC_717", 1, 3, "ETC_717e"), ("ETC_717t", 3, 1, "ETC_717te") })
			{
				string e = ench;
				cards.Add(id, new CardDef(TargetAny, new Power
				{
					PowerTask = ComplexTask.Create(new DamageTask(V(id, "damage", damage), EntityType.TARGET, true),
						new CustomTask((g, c, s, t, stack) => { if (c.Hero.Weapon is Weapon w) Give(g, s, w, e); }))
				}));
				cards.Add(ench, new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V(id, "attack", attack))) }));
			}
			// [JAM_019] Rhythmdancer Risa (4) — Rush. After your hero attacks, return this to your hand and set its Cost to (1).
			cards.Add("JAM_019", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me && me.Zone?.Type == Zone.PLAY && Generic.ReturnToHand(c, me) is IPlayable back) Give(g, back, back, "JAM_019e");
					})
				}
			}));
			cards.Add("JAM_019e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(V("JAM_019", "cost", 1))) }));
			// [JAM_020] Tough Crowd (3) — Outcast: Return a minion to its owner's hand.
			cards.Add("JAM_020", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE_AND_OUTCAST, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } }, new Power
			{
				PowerTask = ComplexTask.Create(new ConditionTask(EntityType.SOURCE, PlayedAsOutcast),
					new FlagTask(true, new CustomTask((g, c, s, t, stack) => { if (t is Minion m && m.Zone?.Type == Zone.PLAY) Generic.ReturnToHand(m.Controller, m); })))
			}));
			// [JAM_021] One Hit Wonder (2) — Rush. Combo: Gain Poisonous.
			cards.Add("JAM_021", new CardDef(new Power
			{
				ComboTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion m) m[GameTag.POISONOUS] = 1; })
			}));

			// ---------------------------------------------------------------- Shaman
			// [ETC_356] Altered Chord (5) — Lifesteal. Deal $6 damage to a minion. Costs (3) less if you're Overloaded.
			cards.Add("ETC_356", new CardDef(TargetMinion, new Power
			{
				Aura = new AdaptiveCostEffect(p => Overloaded(p.Controller) ? V("ETC_356", "discount", 3) : 0),
				PowerTask = new DamageTask(V("ETC_356", "damage", 6), EntityType.TARGET, true)
			}));
			// [ETC_358] Saxophone Soloist (1) — Battlecry: If you control no other minions, add a Saxophone Soloist to your hand.
			cards.Add("ETC_358", new CardDef(new Power
			{
				PowerTask = IfSoloist(new CustomTask((g, c, s, t, stack) => AddToHand(c, ((IPlayable)s).Card)))
			}));
			// [ETC_359] Flowrider (1) — Battlecry: If you're Overloaded, Discover a spell from your deck.
			cards.Add("ETC_359", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => Overloaded(c),
					new CustomTask((g, c, s, t, stack) => Generic.DiscoverFromDeck(c, s, Generic.PickFromDeck(c, p => p is Spell))))
			}));
			// [ETC_362] JIVE, INSECT! (5) — Transform a minion into Ragnaros the Firelord. (CORE_EX1_298, its related card.)
			cards.Add("ETC_362", new CardDef(TargetMinion, new Power { PowerTask = new TransformTask("CORE_EX1_298", EntityType.TARGET) }));
			// [ETC_367] Melomania (0) — Each time you play a minion this turn, add a random Shaman spell to your hand.
			cards.Add("ETC_367", new CardDef(new Power { PowerTask = new AddEnchantmentTask("ETC_367e", EntityType.CONTROLLER) }));
			cards.Add("ETC_367e", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.PLAY_MINION)
					{
						TriggerSource = TriggerSource.FRIENDLY,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (RandomCard(g, x => x.Type == CardType.SPELL && x.HasClass(CardClass.SHAMAN)) is Card spell) AddToHand(c, spell);
						})
					},
					new Trigger(TriggerType.TURN_END) { SingleTask = RemoveEnchantmentTask.Task })
			}));
			// [ETC_369] Chill Vibes (3) — Restore 8 Health. Finale: Summon a 3/3 Elemental with Taunt.
			cards.Add("ETC_369", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new HealTask(V("ETC_369", "heal", 8), EntityType.TARGET), Finale(new SummonTask("ETC_369t", SummonSide.SPELL)))
			}));
			// [ETC_370] Pack the House (7) — Summon a random 6, 5, 4, and 3-Cost minion. Overload: (2)
			cards.Add("ETC_370", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (int cost in new[] { 6, 5, 4, 3 }) Summon(g, c, RandomMinionOfCost(g, cost), s);
				})
			}));
			// [ETC_371] Inzah (5) — Battlecry: For the rest of the game, your Overload cards cost (1) less.
			cards.Add("ETC_371", new CardDef(new Power { PowerTask = new AddEnchantmentTask("ETC_371e1", EntityType.CONTROLLER) }));
			cards.Add("ETC_371e1", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, "ETC_371e2") { Condition = new SelfCondition(p => p.Card[GameTag.OVERLOAD] > 0) }
			}));
			cards.Add("ETC_371e2", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("ETC_371", "discount", 1))) }));
			// [ETC_813] Jazz Bass (3) — Deathrattle: Your next spell costs (@) less. (Overload while equipped to improve!) (@ times
			// ETC_813e on the player: each "next spell costs (1) less", all used by the next spell.) +1 per card you Overload with,
			// whatever its Overload amount, like the set's other instruments (live Glaivetar: +1 per Outcast card). Vicious
			// Syndicate's preview: "On turn 4, you play a couple of overload (1) cards. Your next spell is now discounted by 3
			// mana" — consistent, but only Overload (1) cards; per Overloaded Mana Crystal is UNCONFIRMED (live-checks).
			cards.Add("ETC_813", new CardDef(new Power
			{
				Trigger = Improves(TriggerType.OVERLOAD, TriggerSource.FRIENDLY),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < Num1(s); i++) Generic.AddEnchantmentBlock(g, Cards.FromId("ETC_813e"), (IPlayable)s, c, 0, 0, 0);
				})
			}));
			cards.Add("ETC_813e", NextCardCost(Effects.ReduceCost(1), SelfCondition.IsSpell, "ETC_813e2"));
			// [JAM_011] Horn of the Windlord (6) — Windfury. Whenever your hero attacks a minion, set its stats to 3/3.
			cards.Add("JAM_011", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (g.CurrentEventData?.EventTarget is Minion m) Give(g, s, m, "JAM_011e"); })
				}
			}));
			cards.Add("JAM_011e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("JAM_011", "stats", 3))) }));
			// [JAM_012] Remixed Totemcarver (3) — Gains an extra effect in your hand that changes each turn. Versions summon a Stereo
			// Totem / Mana Tide Totem / Flametongue Totem / Jukebox Totem.
			cards.Add("JAM_012", new CardDef(new Power { NoScript = true }));
			foreach (var (id, totem) in new[] { ("JAM_012t", "ETC_105"), ("JAM_012t2", "CORE_EX1_575"), ("JAM_012t3", "CORE_EX1_565"), ("JAM_012t4", "JAM_010") })
				cards.Add(id, new CardDef(new Power { PowerTask = new SummonTask(totem, SummonSide.RIGHT) }));
			// [JAM_013] Jam Session (2) — Give a friendly minion +3/+3. Deal $1 damage to all other minions. Overload: (1)
			cards.Add("JAM_013", new CardDef(TargetFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Give(g, s, m, "JAM_013e");
					DamageAll(s, g.Player1.BoardZone.Concat(g.Player2.BoardZone).Where(x => x != m && !x.IsDead).Cast<ICharacter>().ToList(), V("JAM_013", "damage", 1), true);
				})
			}));
			cards.Add("JAM_013e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("JAM_013", "stats", 3))) }));

			// ---------------------------------------------------------------- Warlock
			// [ETC_034] Opera Soloist (5) — Battlecry: If you control no other minions, deal 3 damage to all enemy minions.
			cards.Add("ETC_034", new CardDef(new Power { PowerTask = IfSoloist(new DamageTask(V("ETC_034", "damage", 3), EntityType.OP_MINIONS)) }));
			// [ETC_068] Baritone Imp (2) — Battlecry: Take Fatigue damage. Gain that much Attack and Health. (Generic.TakeFatigue)
			cards.Add("ETC_068", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = Generic.TakeFatigue(c);
					if (s is Minion me) Generic.AddEnchantmentBlock(g, Cards.FromId("ETC_068e"), me, me, n, n, 0);
				})
			}));
			cards.Add("ETC_068e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));
			// [ETC_069] Crescendo (2) — Take Fatigue damage. Deal that much damage to all enemies.
			cards.Add("ETC_069", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => DamageAll(s, Enemies(c), Generic.TakeFatigue(c), false))
			}));
			// [ETC_070] Crazed Conductor (4) — Battlecry: Take Fatigue damage. Summon that many 3/3 Imps.
			cards.Add("ETC_070", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = Generic.TakeFatigue(c);
					for (int i = 0; i < n; i++) Summon(g, c, Cards.FromId("ETC_070t"), s);
				})
			}));
			// [ETC_071] Rin, Orchestrator of Doom (5) — Taunt. Deathrattle: Both players draw 2 cards, discard 2 cards, and destroy
			// the top 2 cards of their deck. (The discards: random.)
			cards.Add("ETC_071", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Controller p in new[] { c, c.Opponent })
					{
						for (int i = 0; i < V("ETC_071", "cards", 2); i++) Generic.Draw(p);
						for (int i = 0; i < V("ETC_071", "cards", 2) && p.HandZone.Count > 0; i++)
							Generic.DiscardBlock.Invoke(p, p.HandZone[g.Random.Next(p.HandZone.Count)]);
						for (int i = 0; i < V("ETC_071", "cards", 2) && p.DeckZone.Count > 0; i++)
							p.GraveyardZone.Add(p.DeckZone.Remove(p.DeckZone.TopCard));
					}
				})
			}));
			// [ETC_081] Void Virtuoso (1) — During your turn, your hero is Immune.
			cards.Add("ETC_081", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HERO, "ETC_081e") { Condition = new SelfCondition(p => p.Game.CurrentPlayer == p.Controller), Restless = true }
			}));
			cards.Add("ETC_081e", new CardDef(new Power { Enchant = new Enchant(Effects.Immune) }));
			// [ETC_082] Dirge of Despair (6) — Deal $3 damage to a character. If it dies, summon a Demon from your deck.
			cards.Add("ETC_082", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is ICharacter target)) return;
					Generic.DamageCharFunc((IPlayable)s, target, V("ETC_082", "damage", 3), true);
					if (target.IsDead || target.ToBeDestroyed) SummonFromDeck(g, c, s, p => IsDemon(p));
				})
			}));
			// [ETC_083] Demonic Dynamics (2) — Discover 2 Demons. Finale: Give them +1/+2.
			cards.Add("ETC_083", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					string bonus = Generic.Finale(s) ? "ETC_083e" : null;
					for (int i = 0; i < V("ETC_083", "demons", 2); i++)
						DiscoverFromTask.ClassOrNeutral((c2, x) => x.Type == CardType.MINION && x.IsRace(Race.DEMON), bonus).Process(g, c, s, null, stack);
				})
			}));
			cards.Add("ETC_083e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("ETC_083", "attack", 1)), Effects.Health_N(V("ETC_083", "health", 2))) }));
			// [ETC_084] Felstring Harp (1) — Whenever your hero would take damage on your turn, restore 2 Health instead. Lose 1
			// Durability.
			cards.Add("ETC_084", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PREDAMAGE)
				{
					TriggerSource = TriggerSource.HERO,
					FastExecution = true,
					Condition = new SelfCondition(p => p.Game.CurrentPlayer == p.Controller),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Weapon w) || w.ToBeDestroyed || g.CurrentEventData == null || g.CurrentEventData.EventNumber <= 0) return;
						g.CurrentEventData.EventNumber = 0;
						c.Hero.TakeHeal(w, V("ETC_084", "heal", 2));
						LoseDurability(w);
					})
				}
			}));
			// [ETC_085] Symphony of Sins (5) — Discover and play a Movement. Shuffle the other 6 into your deck.
			cards.Add("ETC_085", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card[] offer = DiscoverFromTask.PickDistinct(g, Movements.Select(Cards.FromId), 3).ToArray();
					Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.CAST, offer, new CustomTask((g2, c2, s2, pick, st) =>
					{
						foreach (string id in Movements.Where(id => id != pick?.Card.Id))
							Generic.ShuffleIntoDeck.Invoke(c2, s2, Entity.FromCard(c2, Cards.FromId(id)));
					}));
				})
			}));
			// [ETC_085t] Movement of Envy — Remove the top 6 cards from your opponent's deck.
			cards.Add("ETC_085t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("ETC_085t", "cards", 6) && c.Opponent.DeckZone.Count > 0; i++)
						c.Opponent.SetasideZone.Add(c.Opponent.DeckZone.Remove(c.Opponent.DeckZone.TopCard));
				})
			}));
			// [ETC_085t2] Movement of Pride — Draw your highest Cost minion. Reduce its Cost by (6).
			cards.Add("ETC_085t2", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.DeckZone.Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.Count == 0) return;
					int max = minions.Max(p => p.Cost);
					if (DrawOne(g, c, p => p is Minion && p.Cost == max) is IPlayable drawn) Give(g, s, drawn, "ETC_085e");
				})
			}));
			cards.Add("ETC_085e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("ETC_085t2", "discount", 6))) }));
			// [ETC_085t3] Movement of Wrath — Deal $6 damage to all characters.
			cards.Add("ETC_085t3", new CardDef(new Power { PowerTask = new DamageTask(V("ETC_085t3", "damage", 6), EntityType.ALL, true) }));
			// [ETC_085t4] Movement of Gluttony — Give a random minion in your hand, deck, and battlefield +6/+6.
			cards.Add("ETC_085t4", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IEnumerable<IPlayable> zone in new IEnumerable<IPlayable>[] { c.HandZone, c.DeckZone, c.BoardZone.Where(m => !m.IsDead) })
					{
						var minions = zone.Where(p => p is Minion).ToList();
						if (minions.Count > 0) Give(g, s, minions[g.Random.Next(minions.Count)], "ETC_085e2");
					}
				})
			}));
			cards.Add("ETC_085e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ETC_085t4", "stats", 6))) }));
			// [ETC_085t6] Movement of Desire — Lifesteal. Deal $6 damage to the enemy hero.
			cards.Add("ETC_085t6", new CardDef(new Power { PowerTask = new DamageTask(V("ETC_085t6", "damage", 6), EntityType.OP_HERO, true) }));
			// [ETC_085t7] Movement of Greed — Draw 6 cards.
			cards.Add("ETC_085t7", new CardDef(new Power { PowerTask = ComplexTask.Repeat(new DrawTask(), V("ETC_085t7", "draw", 6)) }));
			// [ETC_085t8] Movement of Sloth — Summon a 6/6 Demon with Taunt and Reborn.
			cards.Add("ETC_085t8", new CardDef(new Power { PowerTask = new SummonTask("ETC_t8t", SummonSide.SPELL) }));
			// [JAM_030] Fanottem, Lord of the Opera (30) — Taunt, Lifesteal. This card's Cost is equal to the amount of cards in your deck.
			cards.Add("JAM_030", new CardDef(new Power { Aura = new AdaptiveCostEffect(p => p.Controller.DeckZone.Count, EffectOperator.SET) }));
			// [JAM_031] Reverberations (3) — Summon a copy of a minion. Each one dies after taking damage. (JAM_031e on both.)
			cards.Add("JAM_031", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Give(g, s, m, "JAM_031e");
					if (c.BoardZone.IsFull) return;
					var copy = (Minion)Generic.Copy(c, s, m, Zone.PLAY); // (Copy summons it)
					if (copy.Zone?.Type == Zone.PLAY && copy.AppliedEnchantments?.Any(e => e.Card.Id == "JAM_031e") != true) Give(g, s, copy, "JAM_031e");
				})
			}));
			cards.Add("JAM_031e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.ENCHANTMENT_TARGET,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Enchantment e && e.Target is Minion m && !m.IsDead && !m.ToBeDestroyed) Generic.Destroy(m, s);
					})
				}
			}));
			// [JAM_032] Fiddlefire Imp (3) — Battlecry: Add a random Fire Mage and Fire Warlock spell to your hand.
			cards.Add("JAM_032", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (CardClass cls in new[] { CardClass.MAGE, CardClass.WARLOCK })
						if (RandomCard(g, x => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == FIRE && x.HasClass(cls)) is Card spell) AddToHand(c, spell);
				})
			}));

			// ---------------------------------------------------------------- Warrior
			// [ETC_035] Drum Soloist (5) — Taunt. Battlecry: If you control no other minions, gain +2/+2 and Rush.
			cards.Add("ETC_035", new CardDef(new Power
			{
				PowerTask = IfSoloist(new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					Give(g, s, me, "ETC_035e");
					me.IsRush = true;
				}))
			}));
			cards.Add("ETC_035e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ETC_035", "stats", 2))) }));
			// [ETC_121] Rock Master Voone (3) — Battlecry: Copy each minion of a different type in your hand.
			cards.Add("ETC_121", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable m in OfDifferentTypes(g, c.HandZone.Where(p => Generic.IsMinionCard(p)), p => p.Card))
						if (!c.HandZone.IsFull) Generic.Copy(c, s, m, Zone.HAND); // (Copy adds it to the hand)
				})
			}));
			// [ETC_355] Razorfen Rockstar (1) — After you gain Armor, gain 2 more. (The bonus Armor doesn't trigger a Rockstar
			// again: HsTag.BONUS_ARMOR_GAINING.)
			cards.Add("ETC_355", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ARMOR)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (c[HsTag.BONUS_ARMOR_GAINING] > 0) return;
						c[HsTag.BONUS_ARMOR_GAINING] = 1;
						try { c.Hero.GainArmor((IPlayable)s, V("ETC_355", "armor", 2)); }
						finally { c[HsTag.BONUS_ARMOR_GAINING] = 0; }
					})
				}
			}));
			// [ETC_363] Verse Riff (1) — Give your hero +2 Attack this turn. Gain 2 Armor. Finale: Play your last Riff.
			cards.Add("ETC_363", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("ETC_363e", EntityType.HERO), new ArmorTask(V("ETC_363", "armor", 2))),
				SecondPowerTask = PlayLastRiff
			}));
			cards.Add("ETC_363e2", new CardDef(new Power { Enchant = new Enchant(new IEffect[0]) })); // Riff Stopper
			cards.Add("ETC_363e", OneTurn(Effects.Attack_N(V("ETC_363", "attack", 2))));
			// [ETC_364] Chorus Riff (3) — Draw a minion. Give it +3/+3. Finale: Play your last Riff.
			cards.Add("ETC_364", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (DrawOne(g, c, p => p is Minion) is IPlayable drawn) Give(g, s, drawn, "ETC_364e");
				}),
				SecondPowerTask = PlayLastRiff
			}));
			cards.Add("ETC_364e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ETC_364", "stats", 3))) }));
			// [ETC_365] Bridge Riff (5) — Summon a 3/4 Rocker with Taunt and a 4/3 with Rush. Finale: Play your last Riff.
			cards.Add("ETC_365", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new SummonTask("ETC_365t", SummonSide.SPELL), new SummonTask("ETC_365t2", SummonSide.SPELL)),
				SecondPowerTask = PlayLastRiff
			}));
			// [ETC_372] Roaring Applause (2) — Draw a card. Repeat for each minion of a different type you control.
			cards.Add("ETC_372", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = 1 + OfDifferentTypes(g, c.BoardZone.Where(m => !m.IsDead), m => m.Card).Count;
					for (int i = 0; i < n; i++) Generic.Draw(c);
				})
			}));
			// [ETC_408] Power Slider (3) — Rush. Battlecry: Gain +1/+1 for each minion of a different type you've played this game.
			cards.Add("ETC_408", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = DifferentTypesPlayed(g, c, s);
					for (int i = 0; i < n; i++) Give(g, s, (IPlayable)s, "ETC_408e");
				})
			}));
			cards.Add("ETC_408e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ETC_408", "stats", 1))) }));
			// [ETC_417] Blackrock 'n' Roll (5) — Give all minions in your deck Attack and Health equal to their Cost.
			cards.Add("ETC_417", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in c.DeckZone.Where(x => Generic.IsMinionCard(x)).ToList())
						Generic.AddEnchantmentBlock(g, Cards.FromId("ETC_417e"), (IPlayable)s, p, p.Cost, p.Cost, 0);
				})
			}));
			cards.Add("ETC_417e", new CardDef(new Power { Enchant = Enchants.Enchants.SetAttackHealthScriptTag }));
			// [ETC_520] Kodohide Drumkit (4) — Deathrattle: Deal @ damage to all minions. (Gain Armor while equipped to improve!)
			// +1 per Armor gain, whatever its amount (as Arcanite Ripper's per-change patch notes and Glaivetar's live +1 per card;
			// no source states the Drumkit's: HSTopDecks comments only guess — UNCONFIRMED, live-checks).
			cards.Add("ETC_520", new CardDef(new Power
			{
				Trigger = Improves(TriggerType.ARMOR, TriggerSource.HERO),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => DamageAll(s, g.Player1.BoardZone.Concat(g.Player2.BoardZone).Where(m => !m.IsDead).Cast<ICharacter>().ToList(), Num1(s), false))
			}));
			// [JAM_014] Backstage Bouncer (4) — Taunt. Battlecry: Transform a friendly minion into a copy of this.
			cards.Add("JAM_014", new CardDef(BattlecryFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m && s is Minion me && m != me && m.Zone?.Type == Zone.PLAY) new TransformCopyTask().Process(g, c, m, me);
				})
			}));
			// [JAM_015] Remixed Tuning Fork (2) — Gains an extra effect in your hand that changes each turn. Versions: t gain +2
			// Attack, t2 gain 5 Armor, t3 the hero's attacks also hit the minions next to its target, t4 Discover a Taunt minion.
			cards.Add("JAM_015", new CardDef(new Power { NoScript = true }));
			cards.Add("JAM_015t", new CardDef(new Power { PowerTask = new AddEnchantmentTask("JAM_015e", EntityType.SOURCE) }));
			cards.Add("JAM_015e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("JAM_015t", "attack", 2))) }));
			cards.Add("JAM_015t2", new CardDef(new Power { PowerTask = new ArmorTask(V("JAM_015t2", "armor", 5)) }));
			cards.Add("JAM_015t3", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(g.CurrentEventData?.EventTarget is Minion defender)) return;
						var adjacent = defender.GetAdjacentMinions().ToList();
						foreach (Minion next in adjacent) next.TakeDamage(c.Hero, c.Hero.AttackDamage);
					})
				}
			}));
			cards.Add("JAM_015t4", new CardDef(new Power { PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x[GameTag.TAUNT] > 0) }));
			// [JAM_017] Through Fel and Flames (0) — Give a minion Rush. Finale: And +1/+1.
			cards.Add("JAM_017", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) => { if (t is Minion m) m.IsRush = true; }),
					Finale(new AddEnchantmentTask("JAM_017e", EntityType.TARGET)))
			}));
			cards.Add("JAM_017e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("JAM_017", "stats", 1))) }));
		}
	}
}
