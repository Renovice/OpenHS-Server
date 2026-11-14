// HsServer addition (not upstream SabberStone): March of the Lich King — Paladin, Priest, Rogue, Shaman, Warlock and Warrior
// (see LichKingCardsGenHs.cs for the set's shared pieces).
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
		/// <summary>A friendly minion of the player that died this game matching <paramref name="where"/>, summoned again
		/// (a new entity, Generic.NewFromGraveyard: CREATOR = the dead one, and a resurrect's summons line up to the summoner's
		/// right — Summon.cs SummonPosition; Tyr's three went to both sides); null when none or the board is full.</summary>
		private static Minion ResurrectOne(Game g, Controller c, IEntity source, System.Func<IPlayable, bool> where)
		{
			var dead = c.DiedThisGameCards.Where(p => p is Minion && where(p)).ToList();
			if (dead.Count == 0 || c.BoardZone.IsFull) return null;
			var m = (Minion)Generic.NewFromGraveyard(c, dead[g.Random.Next(dead.Count)]);
			Generic.SummonBlock.Invoke(g, m, -1, source);
			return m;
		}

		private static bool DamagedFriendly(ICharacter ch) => ch.Damage > 0 && !ch.IsDead;

		private static void LichKingClasses2(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Paladin
			// [NX2_021] Knight of the Dead (3) — Battlecry: Deal 5 damage to your hero. Manathirst (7): Restore 5 Health to your hero instead.
			cards.Add("NX2_021", new CardDef(new Power
			{
				PowerTask = ManathirstOr(new DamageTask(V("NX2_021", "damage", 5), EntityType.HERO), new HealTask(V("NX2_021", "heal", 5), EntityType.HERO))
			}));
			// [NX2_022] Goldwing (4) — Rush. Battlecry: If you're holding a Mech, gain Windfury.
			cards.Add("NX2_022", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.HandZone.Any(p => p != s && IsMech(p)), new CustomTask((g, c, s, t, stack) => { if (s is Minion m) m.HasWindfury = true; }))
			}));
			// [NX2_023] The Purator (5) — Taunt. Battlecry: If your deck has no Neutral cards, draw a minion of each minion type.
			cards.Add("NX2_023", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => !c.DeckZone.Any(p => p.Card.HasClass(CardClass.NEUTRAL)), new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Race race in System.Enum.GetValues(typeof(Race)).Cast<Race>().Where(r => r != Race.INVALID && r != Race.BLANK && r != Race.ALL))
					{
						var typed = c.DeckZone.Where(p => Generic.IsMinionCard(p) && p.Card.IsRace(race) && !p.Card.IsRace(Race.ALL)).ToList();
						if (typed.Count > 0) Generic.Draw(c, typed[g.Random.Next(typed.Count)]);
					}
				}))
			}));
			// [RLK_527] Timewarden (3) — Battlecry: Until the end of your next turn, Dragons you summon gain Taunt and Divine Shield.
			// (A player tag: the summon gives RLK_527e2 — Actions/Summon.cs; the client has no player enchantment for it.)
			cards.Add("RLK_527", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => c[HsTag.DRAGONS_GAIN_TAUNT_SHIELD_UNTIL] = g.Turn + 2)
			}));
			cards.Add("RLK_527e2", new CardDef(new Power { Enchant = new Enchant(Effects.TauntEff, new Effect(GameTag.DIVINE_SHIELD, EffectOperator.SET, 1)) }));
			// [RLK_916] Daring Drake (4) — Rush. Battlecry: If you're holding a Dragon, gain +1/+1.
			cards.Add("RLK_916", new CardDef(new Power { PowerTask = IfHoldingDragon(new AddEnchantmentTask("RLK_916e", EntityType.SOURCE)) }));
			cards.Add("RLK_916e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("RLK_916", "stats", 1))) }));
			// [RLK_917] Flight of the Bronze (1) — Discover a Dragon. Manathirst (7): Summon a 5/5 Drake with Taunt.
			cards.Add("RLK_917", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.IsRace(Race.DRAGON)),
					ManathirstOr(null, new SummonTask("RLK_917t", SummonSide.SPELL)))
			}));
			// [RLK_918] For Quel'Thalas! (2) — Give a friendly minion +3 Attack. Give your hero +2 Attack this turn.
			cards.Add("RLK_918", new CardDef(TargetFriendlyMinion, new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("RLK_918e", EntityType.TARGET), new AddEnchantmentTask("RLK_918e2", EntityType.HERO))
			}));
			cards.Add("RLK_918e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("RLK_918", "attack", 3))) }));
			cards.Add("RLK_918e2", OneTurn(Effects.Attack_N(V("RLK_918", "hero attack", 2))));
			// [RLK_921] Sanguine Soldier (1) — Divine Shield. Battlecry: Deal 2 damage to your hero.
			cards.Add("RLK_921", new CardDef(new Power { PowerTask = new DamageTask(V("RLK_921", "damage", 2), EntityType.HERO) }));
			// [RLK_922] Seal of Blood (3) — Give a minion +3/+3 and Divine Shield. Deal 3 damage to your hero.
			cards.Add("RLK_922", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("RLK_922e", EntityType.TARGET),
					new CustomTask((g, c, s, t, stack) => { if (t is Minion m) m.HasDivineShield = true; }),
					new DamageTask(V("RLK_922", "damage", 3), EntityType.HERO, true))
			}));
			cards.Add("RLK_922e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("RLK_922", "stats", 3))) }));
			// [RLK_923] Feast and Famine (1) — Give your hero +3 Attack this turn. Manathirst (4): And Lifesteal.
			cards.Add("RLK_923", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("RLK_923e1", EntityType.HERO), ManathirstOr(null, new AddEnchantmentTask("RLK_923e3", EntityType.HERO)))
			}));
			cards.Add("RLK_923e1", OneTurn(Effects.Attack_N(V("RLK_923", "attack", 3))));
			cards.Add("RLK_923e3", OneTurn(Effects.Lifesteal));
			// [RLK_924] Blood Matriarch Liadrin (2) — After you summon a minion with less Attack than this, give it Divine Shield and Rush.
			cards.Add("RLK_924", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t is Minion m && s is Minion liadrin && m.AttackDamage < liadrin.AttackDamage) Give(g, s, m, "RLK_924e");
					})
				}
			}));
			cards.Add("RLK_924e", new CardDef(new Power { Enchant = new Enchant(Effects.Rush, new Effect(GameTag.DIVINE_SHIELD, EffectOperator.SET, 1)) }));
			// [RLK_927] Blood Crusader (6) — Battlecry: Your next Paladin minion this turn costs Health instead of Mana.
			// (a player turn stamp: Playable.CostsHealth / its cost gem; the client's RLK_927e is the card-side name)
			cards.Add("RLK_927", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => c[HsTag.NEXT_PALADIN_MINION_COSTS_HEALTH] = g.Turn) }));

			// ---------------------------------------------------------------- Priest
			// [NX2_018] Rotting Necromancer (4) — Battlecry: Dredge. If it's an Undead, deal 5 damage to the enemy hero.
			cards.Add("NX2_018", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.Dredge(c, c, s, (g2, c2, top) =>
				{
					if (top != null && IsUndead(top)) Generic.DamageCharFunc((IPlayable)s, c2.Opponent.Hero, V("NX2_018", "damage", 5), false);
				}))
			}));
			// [NX2_019] Mind Sear (1) — Deal 2 damage to a minion. If it dies, deal 3 damage to the enemy hero.
			cards.Add("NX2_019", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Generic.DamageCharFunc((IPlayable)s, m, V("NX2_019", "damage", 2), true);
					if (m.IsDead || m.ToBeDestroyed) Generic.DamageCharFunc((IPlayable)s, c.Opponent.Hero, V("NX2_019", "hero damage", 3), true);
				})
			}));
			// [NX2_020] Cannibalize (4) — Destroy a minion. Restore its Health to all friendly characters.
			cards.Add("NX2_020", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					int health = System.Math.Max(0, m.Health);
					Generic.Destroy(m, s);
					foreach (ICharacter friend in new ICharacter[] { c.Hero }.Concat(c.BoardZone.Where(x => !x.IsDead && x != m)).ToList())
						friend.TakeHeal((IPlayable)s, health);
				})
			}));
			// [RLK_812] Animate Dead (1) — Resurrect a friendly minion that costs (3) or less.
			cards.Add("RLK_812", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => ResurrectOne(g, c, s, p => p.Card.Cost <= V("RLK_812", "cost", 3))) }));
			// [RLK_813] Bonecaller (4) — Taunt. Deathrattle: Resurrect a friendly Undead that died this game.
			cards.Add("RLK_813", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => ResurrectOne(g, c, s, p => IsUndead(p) && p != s)) }));
			// [RLK_815] Shadow Word: Undeath (5) — Deal 2 damage to all enemies. If a friendly Undead died after your last turn, deal 2 more.
			cards.Add("RLK_815", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int amount = V("RLK_815", "damage", 2) + (Generic.UndeadDiedAfterYourLastTurn(c) ? V("RLK_815", "more", 2) : 0);
					new DamageTask(amount, EntityType.ENEMIES, true).Process(g, c, s, t, stack);
				})
			}));
			// [RLK_816] Sister Svalna (6) — Battlecry: Permanently add a Vision of Darkness to your hand. (Vision of Darkness:
			// "Discover a Shadow spell. (This stays in your hand.)" — the client's ReplacementsWhenPlayed table leaves a new one in
			// the slot it was played from (Generic.OnPlayReplacement; WILD_WEST 2026-10-07: the script's own re-add made two).)
			cards.Add("RLK_816", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => AddToHand(c, Cards.FromId("RLK_816t3"))) }));
			cards.Add("RLK_816t3", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == SHADOW)
			}));
			// [RLK_822] Haunting Nightmare (3) — Deathrattle: Haunt a card in your hand. When you play it, summon a 4/3 Soldier.
			cards.Add("RLK_822", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var hand = c.HandZone.ToList();
					if (hand.Count > 0) Give(g, s, hand[g.Random.Next(hand.Count)], "RLK_822e2");
				})
			}));
			cards.Add("RLK_822e2", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.ENCHANTMENT_TARGET,
					SingleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("RLK_822t"), s))
				}
			}));
			// [RLK_823] Undying Allies (0) — After you play an Undead this turn, give it Reborn.
			cards.Add("RLK_823", new CardDef(new Power { PowerTask = new AddEnchantmentTask("RLK_823e", EntityType.CONTROLLER) }));
			cards.Add("RLK_823e", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.AFTER_PLAY_MINION)
					{
						TriggerSource = TriggerSource.FRIENDLY,
						Condition = new SelfCondition(p => IsUndead(p)),
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if ((g.CurrentEventData?.EventSource ?? t) is Minion m && !m.IsDead) m[GameTag.REBORN] = 1;
						})
					},
					new Trigger(TriggerType.TURN_END) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = RemoveEnchantmentTask.Task })
			}));
			// [RLK_829] Grave Digging (4) — Draw 2 cards. Costs (1) if a friendly Undead died after your last turn.
			cards.Add("RLK_829", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => Generic.UndeadDiedAfterYourLastTurn(p.Controller) ? p.Card.Cost - V("RLK_829", "cost", 1) : 0),
				PowerTask = ComplexTask.Repeat(new DrawTask(), V("RLK_829", "draw", 2))
			}));
			// [RLK_832] High Cultist Basaleph (4) — Battlecry: Resurrect all friendly Undead that died after your last turn.
			cards.Add("RLK_832", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable dead in Generic.UndeadDiedAfterYourLastTurnCards(c)) Summon(g, c, dead.Card, s);
				})
			}));
			// [RLK_845] Mind Eater (2) — Deathrattle: Add a copy of a card in your opponent's deck to your hand.
			cards.Add("RLK_845", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var deck = c.Opponent.DeckZone.ToList();
					if (deck.Count > 0) AddToHand(c, deck[g.Random.Next(deck.Count)].Card);
				})
			}));

			// ---------------------------------------------------------------- Rogue
			// [NX2_004] Ransack (0) — Deal 1 damage to a minion. If you've played a card from another class this turn, deal 4 instead.
			cards.Add("NX2_004", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					bool other = c.PlayHistory.Any(h => h.Turn == g.Turn && h.SourceCard != null && h.SourceId != s.Id &&
					                                   !h.SourceCard.HasClass(c.HeroClass) && !h.SourceCard.HasClass(CardClass.NEUTRAL));
					Generic.DamageCharFunc((IPlayable)s, m, other ? V("NX2_004", "damage 2", 4) : V("NX2_004", "damage", 1), true);
				})
			}));
			// [NX2_005] Stitched Creation (3) — Combo: Gain +2/+2. Infuse (2): Gain +3/+3. Manathirst (8): Gain +4/+4. (Infused: NX2_005t.)
			ISimpleTask stitchedManathirst = ManathirstOr(null, new AddEnchantmentTask("NX2_005e2", EntityType.SOURCE));
			foreach (string id in new[] { "NX2_005", "NX2_005t" })
				cards.Add(id, new CardDef(new Power
				{
					PowerTask = stitchedManathirst,
					ComboTask = ComplexTask.Create(new AddEnchantmentTask("NX2_005e", EntityType.SOURCE), stitchedManathirst)
				}));
			cards.Add("NX2_005e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("NX2_005", "combo", 2))) }));
			cards.Add("NX2_005e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("NX2_005", "manathirst", 4))) }));
			// [NX2_006] Jolly Roger (1, weapon) — After your hero attacks, summon a 1/1 Undead Pirate.
			cards.Add("NX2_006", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = new SummonTask("NX2_006t", SummonSide.DEFAULT) }
			}));
			// [RLK_216] Rotten Rodent (2) — Battlecry: Reduce the Cost of all Deathrattle cards in your deck by (1).
			cards.Add("RLK_216", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.DeckZone.Where(p => p.Card.Deathrattle).ToList(), "RLK_216e"))
			}));
			cards.Add("RLK_216e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("RLK_216", "less", 1))) }));
			// [RLK_217] Scourge Illusionist (4) — Deathrattle: Add a 4/4 copy of another Deathrattle minion in your deck to your hand. It costs (4) less.
			cards.Add("RLK_217", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var others = c.DeckZone.Where(p => Generic.IsMinionCard(p) && p.Card.Deathrattle && p.Card.Id != ((IPlayable)s).Card.Id).ToList();
					if (others.Count == 0 || c.HandZone.IsFull) return;
					IPlayable copy = Entity.FromCard(c, others[g.Random.Next(others.Count)].Card);
					if (!Generic.AddHandPhase.Invoke(c, copy)) return;
					Give(g, s, copy, "RLK_217e");
					Give(g, s, copy, "RLK_217e2");
				})
			}));
			cards.Add("RLK_217e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("RLK_217", "stats", 4))) }));
			cards.Add("RLK_217e2", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("RLK_217", "less", 4))) }));
			// [RLK_529] Noxious Infiltrator (4) — Poisonous. Battlecry: If a friendly Undead died after your last turn, deal 1 damage to a minion.
			cards.Add("RLK_529", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE_AND_UNDEAD_DIED_AFTER_LAST_TURN, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } },
				new Power { PowerTask = IfUndeadDied(new DamageTask(V("RLK_529", "damage", 1), EntityType.TARGET)) }));
			// [RLK_568] Concoctor (1) — Battlecry: Add a random Concoction to your hand.
			cards.Add("RLK_568", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => AddConcoction(g, c, s, RandomConcoction(g))) }));
			// [RLK_569] Potion Belt (2) — Discover 2 Concoctions. (Each pick mixes with an unmixed one in hand.)
			cards.Add("RLK_569", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("RLK_569", "discovers", 2); i++)
					{
						var offer = Pick(g, Concoctions, 3);
						if (offer.Count == 0) return;
						Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.STACK, offer.ToArray(),
							new CustomTask((g2, c2, s2, chosen, stack2) => { if (chosen != null) AddConcoction(g2, c2, s2, chosen.Card); }));
					}
				})
			}));
			// [RLK_570] Ghoulish Alchemist (2) — Battlecry: Your next Concoction costs (0).
			cards.Add("RLK_570", new CardDef(new Power { PowerTask = new AddEnchantmentTask("RLK_570e4", EntityType.CONTROLLER) }));
			cards.Add("RLK_570e4", NextCardCost(Effects.SetCost(0), new SelfCondition(p => p.Card[CONCOCTION] > 0), "GBL_009e"));
			// [RLK_571] Vile Apothecary (3) — At the end of your turn, add a random Concoction to your hand.
			cards.Add("RLK_571", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new CustomTask((g, c, s, t, stack) => AddConcoction(g, c, s, RandomConcoction(g))) }
			}));
			// [RLK_572] Potionmaster Putricide (2) — After a minion dies, add a Concoction to your hand.
			cards.Add("RLK_572", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerSource = TriggerSource.ALL_MINIONS_EXCEPT_SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) => AddConcoction(g, c, s, RandomConcoction(g)))
				}
			}));
			// [RLK_573] Ghostly Strike (1) — Deal 1 damage. Combo: Draw a card.
			cards.Add("RLK_573", new CardDef(TargetAny, new Power
			{
				PowerTask = new DamageTask(V("RLK_573", "damage", 1), EntityType.TARGET, true),
				ComboTask = ComplexTask.Create(new DamageTask(V("RLK_573", "damage", 1), EntityType.TARGET, true), new DrawTask())
			}));

			// ---------------------------------------------------------------- Shaman
			// [NX2_007] Frostfin Chomper (3) — Battlecry: If you played an Elemental last turn, summon three 1/1 Murlocs.
			cards.Add("NX2_007", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new ConditionTask(EntityType.SOURCE, SelfCondition.ElementalPlayedLastTurn),
					new FlagTask(true, ComplexTask.Repeat(new SummonTask("NX2_007t", SummonSide.RIGHT), V("NX2_007", "murlocs", 3))))
			}));
			// [NX2_008] Blazing Transmutation (1) — Choose a minion. Discover one that costs (1) more to transform it into.
			cards.Add("NX2_008", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion victim)) return;
					int cost = victim.Card.Cost + V("NX2_008", "more", 1);
					var offer = Pick(g, Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION && x.Collectible && x.Cost == cost), 3);
					if (offer.Count == 0) return;
					Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.STACK, offer.ToArray(),
						new CustomTask((g2, c2, s2, chosen, stack2) =>
						{
							if (chosen != null && victim.Zone?.Type == Zone.PLAY) Generic.TransformBlock.Invoke(victim.Controller, chosen.Card, victim);
						}));
				})
			}));
			// [NX2_009] Cold Storage (1) — Freeze a minion. Add a copy of it to your hand.
			cards.Add("NX2_009", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					FreezeAll(new ICharacter[] { m });
					AddToHand(c, m.Card);
				})
			}));
			// [RLK_550] Rotgill (5) — Battlecry: Give your other minions "Deathrattle: Give your minions +1/+1."
			cards.Add("RLK_550", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.Where(m => m != s && !m.IsDead).ToList(), "RLK_550e"))
			}));
			cards.Add("RLK_550e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.Where(m => !m.IsDead && m != s).ToList(), "RLK_550e2"))
			}));
			cards.Add("RLK_550e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("RLK_550", "stats", 1))) }));
			// [RLK_551] Blightblood Berserker (7) — Taunt, Lifesteal, Reborn. Deathrattle: Deal 3 damage to a random enemy.
			cards.Add("RLK_551", new CardDef(new Power
			{
				DeathrattleTask = ComplexTask.Create(new RandomTask(1, EntityType.ENEMIES, harmful: true), new DamageTask(V("RLK_551", "damage", 3), EntityType.STACK))
			}));
			// [RLK_552] Unliving Champion (3) — Battlecry: If a friendly Undead died after your last turn, summon two 3/2 Zombies.
			cards.Add("RLK_552", new CardDef(new Power { PowerTask = IfUndeadDied(ComplexTask.Repeat(new SummonTask("RLK_909t", SummonSide.RIGHT), V("RLK_552", "zombies", 2))) }));
			// [RLK_553] Prescience (4) — Draw 2 minions. For each that costs (5) or more, summon a 2/3 Spirit with Taunt.
			cards.Add("RLK_553", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("RLK_553", "minions", 2); i++)
					{
						var minions = c.DeckZone.Where(p => Generic.IsMinionCard(p)).ToList();
						if (minions.Count == 0) break;
						IPlayable drawn = minions[g.Random.Next(minions.Count)];
						Generic.Draw(c, drawn);
						if (drawn.Cost >= V("RLK_553", "cost", 5)) Summon(g, c, Cards.FromId("RLK_553t"), s);
					}
				})
			}));
			// [RLK_554] Harkener of Dread (5) — Taunt, Reborn. Deathrattle: Summon a 4/4 Undead with Taunt.
			cards.Add("RLK_554", new CardDef(new Power { DeathrattleTask = new SummonTask("RLK_554t", SummonSide.DEATHRATTLE) }));
			// [RLK_909] Deathweaver Aura (2) — Give a minion "Deathrattle: Summon two 3/2 Zombies." Overload: (1)
			cards.Add("RLK_909", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("RLK_909e", EntityType.TARGET) }));
			cards.Add("RLK_909e", new CardDef(new Power { DeathrattleTask = new SummonTask("RLK_909t", V("RLK_909", "zombies", 2), SummonSide.DEATHRATTLE) }));
			// [RLK_910] Shadow Suffusion (3) — Give your minions "Deathrattle: Deal 3 damage to a random enemy."
			cards.Add("RLK_910", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.Where(m => !m.IsDead).ToList(), "RLK_910e"))
			}));
			cards.Add("RLK_910e", new CardDef(new Power
			{
				DeathrattleTask = ComplexTask.Create(new RandomTask(1, EntityType.ENEMIES, harmful: true), new DamageTask(V("RLK_910", "damage", 3), EntityType.STACK))
			}));
			// [RLK_911] From De Other Side (9) — Summon a copy of each minion in your hand. They attack random enemy minions, then die.
			cards.Add("RLK_911", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var summoned = new List<Minion>();
					foreach (IPlayable p in c.HandZone.Where(x => Generic.IsMinionCard(x)).ToList())
					{
						if (c.BoardZone.IsFull) break;
						var copy = (Minion)Entity.FromCard(c, p.Card);
						Generic.SummonBlock.Invoke(g, copy, -1, s);
						summoned.Add(copy);
					}
					foreach (Minion copy in summoned)
					{
						var enemies = c.Opponent.BoardZone.Where(m => !m.IsDead && !m.ToBeDestroyed).ToList();
						if (enemies.Count > 0 && !copy.IsDead && copy.Zone?.Type == Zone.PLAY) EffectAttack(g, copy, enemies[g.Random.Next(enemies.Count)]);
					}
					foreach (Minion copy in summoned.Where(m => m.Zone?.Type == Zone.PLAY && !m.IsDead)) copy.Destroy();
				})
			}));
			// [RLK_912] Scourge Troll (1) — Deathrattles given to this minion trigger twice. (Power.GivenDeathrattlesTwice:
			// Enchantment's Deathrattle loop.)
			cards.Add("RLK_912", new CardDef(new Power { GivenDeathrattlesTwice = true }));
			// [RLK_913] Overlord Drakuru (9) — Rush, Windfury. After this attacks and kills a minion, resurrect it on your side.
			cards.Add("RLK_913", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventTarget is Minion killed && (killed.IsDead || killed.ToBeDestroyed)) Summon(g, c, killed.Card, s);
					})
				}
			}));

			// ---------------------------------------------------------------- Warlock
			// [NX2_016] Nofin's Imp-ossible (2) — Give all friendly Demons and Murlocs "Deathrattle: Summon a 2/2 Imploc."
			cards.Add("NX2_016", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.BoardZone.Where(m => !m.IsDead && (m.IsRace(Race.DEMON) || m.IsRace(Race.MURLOC))).ToList(), "NX2_016e"))
			}));
			cards.Add("NX2_016e", new CardDef(new Power { DeathrattleTask = new SummonTask("NX2_016t", SummonSide.DEATHRATTLE) }));
			// [NX2_017] Plague Eruption (3) — Deal 2 damage to all minions. If you've discarded a card this game, deal 1 more.
			cards.Add("NX2_017", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					new DamageTask(V("NX2_017", "damage", 2) + (c.DiscardedEntities.Count > 0 ? V("NX2_017", "more", 1) : 0), EntityType.ALLMINIONS, true).Process(g, c, s, t, stack))
			}));
			// [NX2_044] Suspicious Peddler (2) — Battlecry: Discover a 1-Cost card. If your opponent guesses your choice, they get a copy.
			cards.Add("NX2_044", Suspicious("NX2_044", x => x.Cost == V("NX2_044", "cost", 1) && x.Collectible));
			// [RLK_531] Infantry Reanimator (5) — Battlecry: Resurrect a friendly Undead. Give it Reborn.
			cards.Add("RLK_531", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (ResurrectOne(g, c, s, IsUndead) is Minion m) m[GameTag.REBORN] = 1; })
			}));
			// [RLK_532] Walking Dead (3) — Taunt. If you discard this minion, summon it.
			cards.Add("RLK_532", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DISCARD)
				{
					TriggerActivation = TriggerActivation.HAND,
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, ((IPlayable)s).Card, s))
				}
			}));
			// [RLK_533] Scourge Supplies (3) — Draw 3 cards. Choose one to discard.
			cards.Add("RLK_533", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var drawn = new List<IPlayable>();
					for (int i = 0; i < V("RLK_533", "draw", 3); i++) if (Generic.Draw(c) is IPlayable p && p.Zone?.Type == Zone.HAND) drawn.Add(p);
					if (drawn.Count > 0)
						Generic.DiscoverFromCards(c, s, drawn, (g2, c2, s2, pick) => { if (pick?.Zone?.Type == Zone.HAND) Generic.DiscardBlock.Invoke(c2, pick); });
				})
			}));
			// [RLK_534] Soul Barrage (4) — When you play or discard this, deal 5 damage randomly split among all enemies.
			ISimpleTask barrage = new CustomTask((g, c, s, t, stack) =>
			{
				for (int i = 0; i < V("RLK_534", "damage", 5); i++)
				{
					var enemies = Enemies(c).Where(x => !x.IsDead).ToList();
					if (enemies.Count == 0) break;
					g.PowerHistory?.NewTargetRound();
					Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], 1, false);
				}
			});
			cards.Add("RLK_534", new CardDef(new Power
			{
				PowerTask = barrage,
				Trigger = new Trigger(TriggerType.DISCARD) { TriggerActivation = TriggerActivation.HAND, TriggerSource = TriggerSource.SELF, SingleTask = barrage }
			}));
			// [RLK_535] Savage Ymirjar (5) — Rush. Battlecry: Discard 2 cards.
			cards.Add("RLK_535", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("RLK_535", "discard", 2); i++)
					{
						var hand = c.HandZone.ToList();
						if (hand.Count == 0) break;
						Generic.DiscardBlock.Invoke(c, hand[g.Random.Next(hand.Count)]);
					}
				})
			}));
			// [RLK_536] Shallow Grave (2) — Trigger a friendly minion's Deathrattle, then destroy it.
			cards.Add("RLK_536", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_DEATHRATTLE, 0 } },
				new Power { PowerTask = ComplexTask.Create(new ActivateDeathrattleTask(EntityType.TARGET), new DestroyTask(EntityType.TARGET)) }));
			// [RLK_537] Twisted Tether (4) — Destroy a minion. Give its stats to a random Undead in your hand.
			cards.Add("RLK_537", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					int atk = m.AttackDamage, hp = System.Math.Max(0, m.Health);
					Generic.Destroy(m, s);
					var undead = c.HandZone.Where(p => Generic.IsMinionCard(p) && IsUndead(p)).ToList();
					if (undead.Count > 0) Generic.AddEnchantmentBlock(g, Cards.FromId("RLK_537e"), (IPlayable)s, undead[g.Random.Next(undead.Count)], atk, hp, 0);
				})
			}));
			cards.Add("RLK_537e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));
			// [RLK_538] Devourer of Souls (1) — After a friendly minion dies, gain its Deathrattle. (Each gained Deathrattle: an
			// RLK_538e capturing the card — {0} — its Deathrattle runs from it.)
			cards.Add("RLK_538", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t is Minion dead && dead.Card.Deathrattle && dead.Card.Power?.DeathrattleTask != null && s is Minion me && !me.IsDead)
							Generic.AddEnchantmentBlock(g, Cards.FromId("RLK_538e"), me, me, -1, -1, dead.Id);
					})
				}
			}));
			cards.Add("RLK_538e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Enchantment e && e.CapturedCard?.Power?.DeathrattleTask is ISimpleTask dr) dr.Process(g, c, s, null);
				})
			}));
			// [RLK_539] Dar'Khan Drathir (7) — Lifesteal. At the end of your turn, deal 6 damage to the enemy hero.
			cards.Add("RLK_539", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new DamageTask(V("RLK_539", "damage", 6), EntityType.OP_HERO) }
			}));
			// [RLK_540] Amorphous Slime (5) — Battlecry: Discard a random Undead. Deathrattle: Summon a copy of it. (RLK_540e
			// remembers the card.)
			cards.Add("RLK_540", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var undead = c.HandZone.Where(p => Generic.IsMinionCard(p) && IsUndead(p)).ToList();
					if (undead.Count == 0 || !(s is Minion me)) return;
					IPlayable discarded = undead[g.Random.Next(undead.Count)];
					Generic.DiscardBlock.Invoke(c, discarded);
					Generic.AddEnchantmentBlock(g, Cards.FromId("RLK_540e"), me, me, -1, -1, discarded.Id);
				})
			}));
			cards.Add("RLK_540e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { if (t is Enchantment e && e.CapturedCard != null) Summon(g, c, e.CapturedCard, s); })
			}));

			// ---------------------------------------------------------------- Warrior
			// [NX2_027] Fleshshaper (6) — Taunt. Battlecry: If you have 5 or more Armor, summon a copy of this.
			cards.Add("NX2_027", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.Hero.Armor >= V("NX2_027", "armor", 5), new SummonCopyTask(EntityType.SOURCE))
			}));
			// [NX2_029] Training Session (1) — Discover a Taunt minion. If you play it this turn, repeat this.
			ISimpleTask training = null;
			training = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x[GameTag.TAUNT] > 0, "NX2_029e");
			cards.Add("NX2_029", new CardDef(new Power { PowerTask = training }));
			cards.Add("NX2_029e", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.AFTER_PLAY_CARD)
					{
						TriggerSource = TriggerSource.ENCHANTMENT_TARGET,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
							DiscoverFromTask.ClassOrNeutral((c2, x) => x.Type == CardType.MINION && x[GameTag.TAUNT] > 0, "NX2_029e").Process(g, c, s, null, stack))
					},
					// "this turn": the mark goes at the end of the turn it was Discovered
					new Trigger(TriggerType.TURN_END) { TriggerSource = TriggerSource.ALL, SingleTask = RemoveEnchantmentTask.Task })
			}));
			// [RLK_600] Sunfire Smithing (4) — Equip a 4/2 Sword. Give a random minion in your hand +4/+2.
			cards.Add("RLK_600", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new WeaponTask("RLK_600t"), BuffRandomInHand(p => p is Minion, "RLK_600e"))
			}));
			cards.Add("RLK_600e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("RLK_600", "attack", 4)), Effects.Health_N(V("RLK_600", "health", 2))) }));
			// [RLK_601] Last Stand (1) — Draw a Taunt minion. Manathirst (7): Double its stats.
			cards.Add("RLK_601", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (DrawOne(g, c, p => p is Minion && p[GameTag.TAUNT] > 0) is Minion drawn && Generic.Manathirst((IPlayable)s))
						Generic.AddEnchantmentBlock(g, Cards.FromId("RLK_601e"), (IPlayable)s, drawn, drawn.AttackDamage, drawn.BaseHealth, 0);
				})
			}));
			cards.Add("RLK_601e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));
			// [RLK_603] Light of the Phoenix (4) — Draw 2 cards. Costs (1) less for each damaged friendly character.
			cards.Add("RLK_603", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => (DamagedFriendly(p.Controller.Hero) ? 1 : 0) + p.Controller.BoardZone.Count(DamagedFriendly)),
				PowerTask = ComplexTask.Repeat(new DrawTask(), V("RLK_603", "draw", 2))
			}));
			// [RLK_604] Thori'belore (4) — Rush. Deathrattle: Go Dormant. Cast a Fire spell to revive Thori'belore! (The Phoenix
			// Egg RLK_604t; revived as RLK_604a, which does it once more: egg RLK_604t2, revived as RLK_604b.)
			cards.Add("RLK_604", new CardDef(new Power { DeathrattleTask = new SummonTask("RLK_604t", SummonSide.DEATHRATTLE) }));
			cards.Add("RLK_604a", new CardDef(new Power { DeathrattleTask = new SummonTask("RLK_604t2", SummonSide.DEATHRATTLE) }));
			foreach (var (egg, revived) in new[] { ("RLK_604t", "RLK_604a"), ("RLK_604t2", "RLK_604b") })
			{
				string into = revived;
				cards.Add(egg, new CardDef(new Power
				{
					Trigger = new Trigger(TriggerType.AFTER_CAST)
					{
						TriggerSource = TriggerSource.FRIENDLY,
						Condition = new SelfCondition(p => p is Spell && p[SPELL_SCHOOL] == FIRE),
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (s is Minion e && e.Zone?.Type == Zone.PLAY) Generic.TransformBlock.Invoke(c, Cards.FromId(into), e);
						})
					}
				}));
			}
			// [RLK_605] Blazing Power (2) — Give a minion +1/+1. Repeat for each damaged friendly character.
			cards.Add("RLK_605", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					int times = 1 + (DamagedFriendly(c.Hero) ? 1 : 0) + c.BoardZone.Count(DamagedFriendly);
					for (int i = 0; i < times; i++) Give(g, s, m, "RLK_605e");
				})
			}));
			cards.Add("RLK_605e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("RLK_605", "stats", 1))) }));
			// [RLK_607] Disruptive Spellbreaker (5) — At the end of your turn, your opponent discards a spell.
			cards.Add("RLK_607", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var spells = c.Opponent.HandZone.Where(p => p is Spell).ToList();
						if (spells.Count > 0) Generic.DiscardBlock.Invoke(c.Opponent, spells[g.Random.Next(spells.Count)]);
					})
				}
			}));
			// [RLK_608] Asvedon, the Grandshield (3) — Taunt. Battlecry: Cast a copy of the last spell your opponent played.
			cards.Add("RLK_608", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card last = c.Opponent.PlayHistory.LastOrDefault(h => h.SourceCard?.Type == CardType.SPELL).SourceCard;
					if (last != null) new CastRandomSpellTask { Spell = last }.Process(g, c, s, null);
				})
			}));
			// [RLK_609] Sunfury Champion (1) — After you cast a Fire spell, deal 1 damage to all minions.
			cards.Add("RLK_609", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Spell && p[SPELL_SCHOOL] == FIRE),
					SingleTask = new DamageTask(V("RLK_609", "damage", 1), EntityType.ALLMINIONS)
				}
			}));
			// [RLK_960] Embers of Strength (2) — Summon two 1/2 Guards with Taunt. Manathirst (6): Give them +1/+2.
			cards.Add("RLK_960", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					bool thirsty = Generic.Manathirst((IPlayable)s);
					for (int i = 0; i < V("RLK_960", "guards", 2) && !c.BoardZone.IsFull; i++)
					{
						var guard = (Minion)Entity.FromCard(c, Cards.FromId("RLK_960t"));
						Generic.SummonBlock.Invoke(g, guard, -1, s);
						if (thirsty && !guard.IsDead) Give(g, s, guard, "RLK_960e");
					}
				})
			}));
			cards.Add("RLK_960e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("RLK_960", "attack", 1)), Effects.Health_N(V("RLK_960", "health", 2))) }));
		}
	}
}
