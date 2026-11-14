// HsServer addition (not upstream SabberStone): The Lost City of Un'Goro — chunk 3: Paladin, Priest and Rogue (spec: research
// spec_chunk3). Shared pieces: the shuffle-instance counters (Generic.ShuffleIntoDeck: client 4103 / 4307), Kindred, the Maps,
// Generic.Dredge with given cards, Power.AttacksHealedEnemy (Character.TakeHeal). Every number goes through CardValues (V).
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
		private const int HOLY_SCHOOL = 5, SHADOW_SCHOOL = 6;

		/// <summary>An Objective counted at the end of its owner's turns ("At the end of your next turn": the turn it is played counts —
		/// live TIME_700): at its total (QUEST_PROGRESS_TOTAL) <paramref name="effect"/> runs and it leaves.</summary>
		private static Power EndOfTurnObjective(Action<Game, Controller, Spell> effect) => new Power
		{
			Trigger = new Trigger(TriggerType.TURN_END)
			{
				SingleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Spell objective) || objective.Zone?.Type != Zone.SECRET) return;
					if (!Generic.ObjectiveTick(objective, objective.QuestTotalProgress)) return; // (2026-10-08: the shared tick / expiry)
					effect(g, c, objective);
					Generic.ObjectiveExpires(c, objective);
				})
			}
		};

		/// <summary>"If you're holding both halves of …, combine!" (Sol'etos; live darkmoon 50136-50160): the half WAITING in hand reacts to
		/// the other half (same DISPLAY_CARD_ON_MOUSEOVER — the combined card — another id) entering it: its TRIGGER block, both halves
		/// leave, the combined card arrives as a NEW entity (Generic.CombineInHand).</summary>
		private static Trigger CombineHalves() => new Trigger(TriggerType.ADD_TO_HAND)
		{
			TriggerActivation = TriggerActivation.HAND,
			TriggerSource = TriggerSource.FRIENDLY,
			SingleTask = new CustomTask((g, c, s, t, stack) =>
			{
				if (!(s is IPlayable waiting) || waiting.Zone?.Type != Zone.HAND || !(t is IPlayable arriving) || arriving.Id == waiting.Id ||
				    arriving.Zone?.Type != Zone.HAND) return;
				int combined = waiting.Card[GameTag.DISPLAY_CARD_ON_MOUSEOVER];
				if (combined == 0 || arriving.Card.Id == waiting.Card.Id || arriving.Card[GameTag.DISPLAY_CARD_ON_MOUSEOVER] != combined ||
				    !(Cards.FromAssetId(combined) is Card whole)) return;
				Generic.CombineInHand(c, waiting, arriving, whole, displayedCreator: true);
			})
		};

		private static void LostCityChunk3(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Paladin
			// [DINO_404] Firegill (2) — Rush. Kindred: Give your other minions Rush.
			cards.Add("DINO_404", new CardDef(KindredPower((g, c, s, t) =>
			{
				foreach (Minion m in c.BoardZone.GetAll().Where(m => m.Id != s.Id && !(m is Location)).ToList()) m.IsRush = true;
			})));
			// [DINO_405] Hatching Ceremony (3) — At the end of your next turn, give your minions +2/+2. (an Objective)
			cards.Add("DINO_405", new CardDef(EndOfTurnObjective((g, c, o) => GiveAll(g, o, c.BoardZone.GetAll().Where(m => !(m is Location)).ToList(), "DINO_405e"))));
			cards.Add("DINO_405e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DINO_405", "stats", 2))) }));
			// [DINO_424] Hero's Welcome (8) — Discover a Legendary minion to summon. Set its stats to 10/10.
			cards.Add("DINO_424", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY, action: ChoiceAction.STACK,
					afterPick: new CustomTask((g, c, s, t, stack) =>
					{
						if (t == null || c.BoardZone.IsFull || !(t is Minion m)) return;
						m.Zone?.Remove(m);
						Give(g, s, m, "DINO_424e");
						Generic.SummonBlock.Invoke(g, m, -1, s);
					}))
			}));
			cards.Add("DINO_424e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttack(V("DINO_424", "stats", 10)), Effects.SetMaxHealth(V("DINO_424", "stats", 10))) }));
			// [TLC_240] Tyrannogill (6) — Rush. Deathrattle: Summon three 2/1 Murlocs. Give them each a random Bonus Effect. (live L05 30601:
			// one of each Dinoloc, the Bonus Effect on it as it arrives)
			cards.Add("TLC_240", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (string id in new[] { "TLC_240t", "TLC_240t2", "TLC_240t3" })
						if (SummonGet(g, c, Cards.FromId(id), s) is Minion m) Generic.GainBonusEffects(g, m, 1);
				})
			}));
			// [TLC_241] Ido of the Threshfleet (4) — While this is alive, you get a 2-Cost Holy spell that gives a minion +2/+2 and Divine
			// Shield. (its spell, made when it enters play and after each cast while it lives; the spell leaves the hand when Ido dies —
			// doubt: unrecorded)
			cards.Add("TLC_241", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (c.HandZone.Any(p => p.Card.Id == "TLC_241t" && p[GameTag.CREATOR] == s.Id) || c.HandZone.IsFull) return;
						Generic.AddHandPhase.Invoke(c, Entity.FromCard(c, Cards.FromId("TLC_241t"), new EntityData { [GameTag.CREATOR] = s.Id, [GameTag.DISPLAYED_CREATOR] = s.Id }));
					})
				}
			}));
			cards.Add("TLC_241t", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m) { Give(g, s, m, "TLC_241e"); m.HasDivineShield = true; }
					if (g.IdEntityDic.TryGetValue(s[GameTag.CREATOR], out IPlayable ido) && ido.Zone?.Type == Zone.PLAY && ido.Controller == c && !c.HandZone.IsFull)
						Generic.AddHandPhase.Invoke(c, Entity.FromCard(c, Cards.FromId("TLC_241t"), new EntityData { [GameTag.CREATOR] = ido.Id, [GameTag.DISPLAYED_CREATOR] = ido.Id }));
				}),
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerActivation = TriggerActivation.HAND,
					TriggerSource = TriggerSource.ALL_MINIONS,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable dead = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (!(s is IPlayable spell) || dead == null || dead.Id != spell[GameTag.CREATOR] || spell.Zone?.Type != Zone.HAND) return;
						c.HandZone.Remove(spell);
						c.SetasideZone.Add(spell);
					})
				}
			}));
			cards.Add("TLC_241e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TLC_241t", "stats", 2))) }));
			// [TLC_426] Dive the Golakka Depths (1) — Repeatable Quest: Summon 6 Murlocs. Reward: Murlocs you summon gain +1/+1. (wiki: "it
			// will recast itself automatically"; "Each completion grants the buff" — the count on the player, TLC_426e shows it)
			cards.Add("TLC_426", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Minion m && m.IsRace(Race.MURLOC)),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Spell quest) || !c.SecretZone.IsActiveQuest(quest)) return;
						quest.QuestProgress++;
						if (quest.QuestProgress < quest.QuestTotalProgress) return;
						quest.QuestProgress = 0;
						c[HsTag.GOLAKKA_COMPLETIONS] = c[HsTag.GOLAKKA_COMPLETIONS] + 1;
						if (PlayerEnchantment(c, "TLC_426e") is Enchantment shown) shown[GameTag.TAG_SCRIPT_DATA_NUM_1] = c[HsTag.GOLAKKA_COMPLETIONS];
						else Generic.AddEnchantmentBlock(g, Cards.FromId("TLC_426e"), quest, c, c[HsTag.GOLAKKA_COMPLETIONS], 0, 0);
					})
				}
			}));
			cards.Add("TLC_426e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Minion m && m.IsRace(Race.MURLOC)),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						int n = c[HsTag.GOLAKKA_COMPLETIONS] * V("TLC_426", "stats", 1);
						if (t is Minion m && n > 0) Generic.AddEnchantmentBlock(g, Cards.FromId("TLC_426e2"), (IPlayable)s, m, n, n, 0);
					})
				}
			}));
			cards.Add("TLC_426e2", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0), Effects.Health_N(0)) { UseScriptTag = true } }));
			// [TLC_428] Hot Spring Glider (3) — Divine Shield. Battlecry: Your next Murloc costs (1) less. Kindred: And gains Divine Shield.
			// (live vfx 183256: TLC_428e lasts until a Murloc is played; the Kindred part — the next Murloc played gains it: doubt)
			cards.Add("TLC_428", new CardDef(KindredPower((g, c, s, t) =>
			{
				if (PlayerEnchantment(c, "TLC_428e") is Enchantment e) e[GameTag.TAG_SCRIPT_DATA_NUM_2] = 1;
			}, always: (g, c, s, t) => Generic.AddEnchantmentBlock(g, Cards.FromId("TLC_428e"), (IPlayable)s, c, 0, 0, 0))));
			cards.Add("TLC_428e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("TLC_428", "less", 1))) { Condition = new SelfCondition(p => p is Minion m && m.IsRace(Race.MURLOC)) },
				Trigger = new Trigger(TriggerType.PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Minion m && m.IsRace(Race.MURLOC)),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e)) return;
						if (e[GameTag.TAG_SCRIPT_DATA_NUM_2] > 0 && t is Minion murloc) murloc.HasDivineShield = true;
						e.Remove();
					})
				}
			}));
			// [TLC_430] Creature of the Sacred Cave (4) — At the end of your turn, recast a random Holy spell you cast this turn (targets
			// this if possible).
			cards.Add("TLC_430", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var holy = c.PlayHistory.Where(h => h.Turn == g.Turn && h.SourceController == c.PlayerId && h.SourceCard?.Type == CardType.SPELL &&
							h.SourceCard[SPELL_SCHOOL] == HOLY_SCHOOL).Select(h => h.SourceCard).ToList();
						if (holy.Count == 0) return;
						Card spell = holy[g.Random.Next(holy.Count)];
						new CastRandomSpellTask { TargetsSourceIfPossible = true }.Process(g, c, s, Entity.FromCard(c, spell, new EntityData { [GameTag.CREATOR] = s.Id }));
					})
				}
			}));
			// [TLC_438] Violet Treasuregill (2) — Battlecry: Cast a random spell from your deck that costs (2) or less (targets this if possible).
			cards.Add("TLC_438", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var spells = c.DeckZone.GetAll().Where(p => p is Spell && p.Cost <= V("TLC_438", "cost", 2)).ToList();
					if (spells.Count > 0) Generic.CastFromDeck(c, s, (Spell)spells[g.Random.Next(spells.Count)], targetsSourceIfPossible: true);
				})
			}));
			// [TLC_441] Ready the Fleet (3) — Give +1/+2 to a friendly minion and your other minions that share a type with it.
			cards.Add("TLC_441", new CardDef(TargetFriendlyMinionToPlay, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					var targets = new List<IPlayable> { m };
					var types = Generic.MinionTypesOf(m.Card);
					if (types.Count > 0)
						targets.AddRange(c.BoardZone.GetAll().Where(x => x.Id != m.Id && !(x is Location) && Generic.MinionTypesOf(x.Card).Any(types.Contains)));
					GiveAll(g, s, targets, "TLC_441e2");
				})
			}));
			cards.Add("TLC_441e2", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TLC_441", "attack", 1)), Effects.Health_N(V("TLC_441", "health", 2))) }));
			// [TLC_442] Submerged Map (1) — Discover a Murloc. If you play it this turn, also pick one of the others.
			cards.Add("TLC_442", new CardDef(new Power { PowerTask = DiscoverMap((c, x) => x.Type == CardType.MINION && x.IsRace(Race.MURLOC)) }));
			cards.Add("TLC_442e", MapMark());
			// [TLC_444] Story of Galvadon (2) — Give a minion three random Bonus Effects.
			cards.Add("TLC_444", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m) Generic.GainBonusEffects(g, m, V("TLC_444", "effects", 3)); })
			}));
			// [TLC_477] Threshrider's Blessing (5) — Give a friendly minion +4/+4 and "Deathrattle: Summon a random 4-Cost minion."
			cards.Add("TLC_477", new CardDef(TargetFriendlyMinionToPlay, new Power { PowerTask = new AddEnchantmentTask("TLC_477e", EntityType.TARGET) }));
			cards.Add("TLC_477e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("TLC_477", "stats", 4)), Effects.Health_N(V("TLC_477", "stats", 4)), new Effect(GameTag.DEATHRATTLE, EffectOperator.SET, 1)),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => SummonGet(g, c, RandomMinionOfCost(g, V("TLC_477", "cost", 4)), s))
			}));

			// ---------------------------------------------------------------- Priest
			// [DINO_426] Ritual of Life (2) — Discover a 3-Cost minion. Summon a 2/3 copy of it. (live darkmoon 43222: the pick to hand,
			// then a new copy summoned with DINO_426e)
			cards.Add("DINO_426", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.Cost == V("DINO_426", "cost", 3), afterPick: new CustomTask((g, c, s, t, stack) =>
				{
					if (t == null || c.BoardZone.IsFull) return;
					var copy = (Minion)Entity.FromCard(c, t.Card, new EntityData { [GameTag.CREATOR] = s.Id });
					Give(g, s, copy, "DINO_426e");
					Generic.SummonBlock.Invoke(g, copy, -1, s);
				}))
			}));
			cards.Add("DINO_426e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttack(V("DINO_426", "attack", 2)), Effects.SetMaxHealth(V("DINO_426", "health", 3))) }));
			// [DINO_428] Behemoth Mask (7) — Set a minion's stats to 8/10 and give it Lifesteal. Force a random enemy minion to attack it.
			cards.Add("DINO_428", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Give(g, s, m, "DINO_428e");
					var attackers = c.Opponent.BoardZone.GetAll().Where(x => x.Id != m.Id && !(x is Location) && !x.IsDead && !x.ToBeDestroyed).ToList();
					if (attackers.Count > 0 && m.Zone?.Type == Zone.PLAY) EffectAttack(g, attackers[g.Random.Next(attackers.Count)], m);
				})
			}));
			cards.Add("DINO_428e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.SetAttack(V("DINO_428", "attack", 8)), Effects.SetMaxHealth(V("DINO_428", "health", 10)), new Effect(GameTag.LIFESTEAL, EffectOperator.SET, 1))
			}));
			// [DINO_431] Atlasaurus (8) — Taunt. Deathrattle: Summon a random Taunt minion that costs (5) or more.
			cards.Add("DINO_431", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
					SummonGet(g, c, RandomCard(g, x => x.Type == CardType.MINION && x[GameTag.TAUNT] > 0 && x.Cost >= V("DINO_431", "cost", 5) && !x.NeverRandomlyOnBoard), s))
			}));
			// [TLC_811] Archaios (3) — Whenever another friendly minion attacks, set its Health equal to this minion's Health.
			cards.Add("TLC_811", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me) || !(g.CurrentEventData?.EventSource is Minion attacker) || attacker.Id == me.Id) return;
						Give(g, s, attacker, "TLC_811e", me.Health);
					})
				}
			}));
			cards.Add("TLC_811e", new CardDef(new Power { Enchant = Enchants.Enchants.SetHealthScriptTag }));
			// [TLC_814] Twilight Mender (3) — Deathrattle: Get a random Holy and Shadow spell.
			cards.Add("TLC_814", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (int school in new[] { HOLY_SCHOOL, SHADOW_SCHOOL })
						if (RandomCard(g, x => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == school) is Card spell) AddToHand(c, spell);
				})
			}));
			// [TLC_815] Gravedawn Voidbulb (4) — Summon a random 4-Cost minion and give it Taunt. Kindred: Do it again.
			void Voidbulb(Game g, Controller c, IEntity s)
			{
				if (SummonGet(g, c, RandomMinionOfCost(g, V("TLC_815", "cost", 4)), s) is Minion m) Give(g, s, m, "TLC_815e");
			}
			cards.Add("TLC_815", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, KindredPower((g, c, s, t) => Voidbulb(g, c, s), always: (g, c, s, t) => Voidbulb(g, c, s))));
			cards.Add("TLC_815e", new CardDef(new Power { Enchant = new Enchant(Effects.TauntEff) }));
			// [TLC_816] Gravedawn Sunbloom (4) — Draw 2 cards. Kindred: This costs (2) less.
			cards.Add("TLC_816", new CardDef(KindredPower((g, c, s, t) => { }, always: (g, c, s, t) => { for (int i = 0; i < V("TLC_816", "draws", 2); i++) Generic.Draw(c); },
				power: new Power { Aura = new AdaptiveCostEffect(p => Generic.Kindred(p.Controller, p) ? V("TLC_816", "less", 2) : 0) })));
			// [TLC_817] Reach Equilibrium (1) — Quest: Cast 4 Holy spells. Reward: Life's Breath. Quest: Cast 4 Shadow spells. Reward:
			// Death's Touch. (wiki: "The two sub-Quests proceed individually and grant their Rewards as soon as one is finished". Live
			// session-20261001-130515 13259: the card is set aside and its halves TLC_817t / TLC_817t2 go up as two Quests — the engine's
			// Generic.SplitQuest, from the card data; each half counts its own spells)
			cards.Add("TLC_817", new CardDef(new Power { NoScript = true }));
			// [TLC_817t] Cleanse the Shadow — Quest: Cast 4 Holy spells. Reward: Life's Breath. / [TLC_817t2] Corrupt the Light — Quest:
			// Cast 4 Shadow spells. Reward: Death's Touch. (the count is the card's QUEST_PROGRESS_TOTAL, the reward its QUEST_REWARD_DATABASE_ID)
			Trigger CastSchool(int school) => new Trigger(TriggerType.AFTER_CAST)
			{
				TriggerSource = TriggerSource.FRIENDLY,
				Condition = new SelfCondition(p => p.Card[SPELL_SCHOOL] == school),
				SingleTask = new CustomTask((g, c, s, t, stack) => QuestStep(g, c, s as Spell))
			};
			cards.Add("TLC_817t", new CardDef(new Power { Trigger = CastSchool(HOLY_SCHOOL) }));
			cards.Add("TLC_817t2", new CardDef(new Power { Trigger = CastSchool(SHADOW_SCHOOL) }));
			// [TLC_817t3] Sol'etos, Life's Breath (5) — Taunt. Battlecry: Summon a copy of this. (combines with the other half)
			cards.Add("TLC_817t3", new CardDef(new Power
			{
				Trigger = CombineHalves(),
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me && !c.BoardZone.IsFull) Generic.Copy(c, s, me, Zone.PLAY); })
			}));
			// [TLC_817t4] Sol'etos, Death's Touch (5) — Reborn. Deathrattle: Deal 5 damage to a random enemy.
			ISimpleTask soletosDeath = new CustomTask((g, c, s, t, stack) =>
			{
				var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed).ToList();
				if (enemies.Count > 0) Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], V("TLC_817t4", "damage", 5), false);
			});
			cards.Add("TLC_817t4", new CardDef(new Power { Trigger = CombineHalves(), DeathrattleTask = soletosDeath }));
			// [TLC_817t5] Sol'etos, Cycle's Rebirth (5) — Taunt, Reborn. Battlecry: Summon a copy of this. Deathrattle: Deal 5 damage to a random enemy.
			cards.Add("TLC_817t5", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me && !c.BoardZone.IsFull) Generic.Copy(c, s, me, Zone.PLAY); }),
				DeathrattleTask = soletosDeath
			}));
			// [TLC_818] Resuscitate (6) — Resurrect a 1, 2, and 3-Cost minion. Give them Reborn.
			cards.Add("TLC_818", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int cost = 1; cost <= 3; cost++)
					{
						int n = cost;
						if (ResurrectOne(g, c, s, p => p.Card.Cost == n) is Minion m) m[GameTag.REBORN] = 1;
					}
				})
			}));
			// [TLC_819] Gladesong Siren (6) — Lifesteal. Costs (1) if you've cast a Holy and Shadow spell this turn.
			bool HolyAndShadow(Controller c)
			{
				var cast = c.PlayHistory.Where(h => h.Turn == c.Game.Turn && h.SourceController == c.PlayerId && h.SourceCard?.Type == CardType.SPELL).ToList();
				return cast.Any(h => h.SourceCard[SPELL_SCHOOL] == HOLY_SCHOOL) && cast.Any(h => h.SourceCard[SPELL_SCHOOL] == SHADOW_SCHOOL);
			}
			cards.Add("TLC_819", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => V("TLC_819", "cost", 1), EffectOperator.SET, new SelfCondition(p => HolyAndShadow(p.Controller)))
			}));
			// [TLC_820] Glade Ecologist (1) — Deathrattle: Get a 1-Cost Holy spell that gives a minion +2 or -2 Health.
			cards.Add("TLC_820", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => AddToHand(c, Cards.FromId("TLC_813"))) }));
			// [TLC_813] Purifying Vines (1) — Choose a minion. If it's friendly, give it +2 Health. If it's an enemy, give it -2 Health.
			cards.Add("TLC_813", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m) Give(g, s, m, m.Controller == c ? "TLC_813e" : "TLC_813e2"); })
			}));
			cards.Add("TLC_813e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("TLC_813", "health", 2))) }));
			cards.Add("TLC_813e2", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(-V("TLC_813", "health", 2))) }));
			// [TLC_821] Wilted Shadow (7) — Lifesteal. Whenever you heal an enemy, this attacks it. (Power.AttacksHealedEnemy: Character.TakeHeal)
			cards.Add("TLC_821", new CardDef(new Power { AttacksHealedEnemy = true }));
			// [TLC_835] Story of Amara (10) — Set your hero's Health to 40.
			cards.Add("TLC_835", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Give(g, s, c.Hero, "TLC_835e", V("TLC_835", "health", 40)))
			}));
			cards.Add("TLC_835e", new CardDef(new Power { Enchant = Enchants.Enchants.SetHealthScriptTag }));

			// ---------------------------------------------------------------- Rogue
			// [DINO_407] Mirrex, the Crystalline (3) — While in your hand, this is a 3/4 copy of the last minion your opponent played.
			// (DINO_407e "Reflecting" carries the change — the card's own trigger goes when it turns into another card; DINO_407e2 3/4)
			Card LastEnemyMinion(Controller c) => c.Opponent.PlayHistory.LastOrDefault(h => h.SourceController == c.Opponent.PlayerId && h.SourceCard?.Type == CardType.MINION).SourceCard;
			cards.Add("DINO_407", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ADD_TO_HAND)
				{
					TriggerActivation = TriggerActivation.HAND,
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is IPlayable mirrex) || mirrex.AppliedEnchantments?.Any(e => e.Card.Id == "DINO_407e") == true) return;
						Give(g, s, mirrex, "DINO_407e");
						Give(g, s, mirrex, "DINO_407e2");
						if (LastEnemyMinion(c) is Card last) Generic.ChangeInHand(c, mirrex, last);
					})
				}
			}));
			cards.Add("DINO_407e", new CardDef(new Power
			{
				Enchant = new Enchant { RemoveWhenPlayed = true },
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || !(e.Target is IPlayable card) || card.Zone?.Type != Zone.HAND) return;
						if (LastEnemyMinion(c) is Card last) Generic.ChangeInHand(c, card, last);
					})
				}
			}));
			cards.Add("DINO_407e2", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttack(V("DINO_407", "attack", 3)), Effects.SetMaxHealth(V("DINO_407", "health", 4))) { RemoveWhenPlayed = false } }));
			// [DINO_408] Crystal Tusk (2) — Battlecry: Shuffle the left-most card in your hand into your deck. Deathrattle: Draw 2 cards.
			cards.Add("DINO_408", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (c.HandZone.Count > 0) Generic.MoveToDeck(c, s, c.HandZone[0]); }),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("DINO_408", "draws", 2); i++) Generic.Draw(c); })
			}));
			// [DINO_427] Costume Merchant (3) — Battlecry: Get a random Mask from another class. Combo: It costs (2) less. (the Masks: its
			// related cards; not the player's class — wiki 35.4.0)
			Card RandomMask(Game g, Controller c, IEntity s)
			{
				var masks = (ReprintCards.RelatedCardIds(((IPlayable)s).Card.Id) ?? new List<string>()).Select(Cards.TryFromId)
					.Where(x => x != null && x.Type == CardType.SPELL && !x.HasClass(c.HeroClass)).ToList();
				return masks.Count > 0 ? masks[g.Random.Next(masks.Count)] : null;
			}
			cards.Add("DINO_427", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (RandomMask(g, c, s) is Card mask) AddToHand(c, mask); }),
				ComboTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(RandomMask(g, c, s) is Card mask) || c.HandZone.IsFull) return;
					IPlayable got = Entity.FromCard(c, mask, new EntityData { [GameTag.CREATOR] = s.Id });
					if (Generic.AddHandPhase.Invoke(c, got)) Give(g, s, got, GenericCostLess(V("DINO_427", "less", 2)));
				})
			}));
			// [TLC_513] Lie in Wait (1) — Quest: Shuffle cards into your deck, 5 times. Reward: Master Dusk. (an instance each — client 4307)
			cards.Add("TLC_513", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.SHUFFLE_INTO_DECK)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Spell quest) || c[Generic.SHUFFLED_INTO_OWN_DECK] <= quest[HsTag.QUEST_COUNTED_MASK]) return;
						quest[HsTag.QUEST_COUNTED_MASK] = c[Generic.SHUFFLED_INTO_OWN_DECK];
						QuestStep(g, c, quest);
					})
				}
			}));
			// [TLC_513t] Master Dusk (3) — Battlecry: Summon two 3/3 Ninjas with Stealth. Your Ninjas now shuffle back into your deck when they die.
			cards.Add("TLC_513t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TLC_513t", "ninjas", 2); i++) SummonGet(g, c, Cards.FromId("TLC_513t2"), s);
					if (PlayerEnchantment(c, "TLC_513e2") == null) Generic.AddEnchantmentBlock(g, Cards.FromId("TLC_513e2"), (IPlayable)s, c, 0, 0, 0);
				})
			}));
			cards.Add("TLC_513e2", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.Card.Id == "TLC_513t2"),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable dead = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (dead != null) Generic.MoveToDeck(c, s, dead);
					})
				}
			}));
			cards.Add("TLC_513e1", new CardDef(new Power { NoScript = true }));
			// [TLC_513hp] Way of the Shell (1) — Draw 2 cards that didn't start in your deck.
			cards.Add("TLC_513hp", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("TLC_513hp", "draws", 2); i++) DrawOne(g, c, Generic.DidntStartInDeck); })
			}));
			// [TLC_513t2] Tortollan Ninja — Summoned When Drawn. Stealth. (the Frost Tyrant's rule: the drawn card itself, far right)
			cards.Add("TLC_513t2", new CardDef(new Power
			{
				TopdeckTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion ninja) || ninja.Zone?.Type != Zone.SETASIDE || c.BoardZone.IsFull) return;
					c.SetasideZone.Remove(ninja);
					Generic.SummonBlock.Invoke(g, ninja, -1, null);
				})
			}));
			// [TLC_514] Merchant of Legend (1) — Battlecry: Discover a Legendary minion. Shuffle the other two into your deck.
			cards.Add("TLC_514", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY, afterPick: new CustomTask((g, c, s, t, stack) =>
				{
					// (the choice is still open here: its other options)
					foreach (int id in c.Choice?.Choices?.Where(id => id != t?.Id).ToList() ?? new List<int>())
						if (id > 0 && g.IdEntityDic.TryGetValue(id, out IPlayable other) && other.Zone?.Type == Zone.SETASIDE && !c.DeckZone.IsFull)
						{
							other.Zone.Remove(other);
							Generic.ShuffleIntoDeck.Invoke(c, s, other);
						}
				}))
			}));
			// [TLC_515] Cultist Map (2) — Discover a card from your deck. If you play it this turn, also pick one of the others.
			cards.Add("TLC_515", new CardDef(new Power { PowerTask = DiscoverMapFromDeck() }));
			cards.Add("TLC_515e", MapMark());
			// [TLC_516] Neferset Weaponsmith (4) — Battlecry: Get a random weapon from another class. Combo: Give it +2 Attack.
			cards.Add("TLC_516", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (Pick(g, OtherClassCards(g, c).Where(x => x.Type == CardType.WEAPON), 1).FirstOrDefault() is Card w) AddToHand(c, w); }),
				ComboTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(Pick(g, OtherClassCards(g, c).Where(x => x.Type == CardType.WEAPON), 1).FirstOrDefault() is Card w) || c.HandZone.IsFull) return;
					IPlayable got = Entity.FromCard(c, w, new EntityData { [GameTag.CREATOR] = s.Id });
					if (Generic.AddHandPhase.Invoke(c, got)) Give(g, s, got, "TLC_516e");
				})
			}));
			cards.Add("TLC_516e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TLC_516", "attack", 2))) }));
			// [TLC_517] Knockback (1) — Deal $@ damage to a minion (improved for each time you've shuffled cards into your deck).
			Func<IPlayable, int> knockback = p => p.Card[GameTag.TAG_SCRIPT_DATA_NUM_1] + p.Controller[Generic.SHUFFLED_INTO_OWN_DECK];
			cards.Add("TLC_517", new CardDef(TargetMinion, new Power
			{
				HandCounter = knockback,
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m) Generic.DamageCharFunc((IPlayable)s, m, knockback((IPlayable)s), true); })
			}));
			// [TLC_518] Interrogation (2) — Shuffle three 3/3 Ninjas with Stealth into your deck that are Summoned When Drawn.
			cards.Add("TLC_518", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TLC_518", "ninjas", 3) && !c.DeckZone.IsFull; i++)
						Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("TLC_513t2"), new EntityData { [GameTag.CREATOR] = s.Id }));
				})
			}));
			// [TLC_519] Ambush Predators (3) — Summon a 1/1 Spitter with Stealth and Poisonous. Kindred: Do it again.
			cards.Add("TLC_519", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, KindredPower(
				(g, c, s, t) => SummonGet(g, c, Cards.FromId("TLC_519t"), s), always: (g, c, s, t) => SummonGet(g, c, Cards.FromId("TLC_519t"), s))));
			// [TLC_520] Underbrush Tracker (6) — Rush. Costs (1) less for each time you've shuffled cards into your deck.
			cards.Add("TLC_520", new CardDef(new Power { Aura = new AdaptiveCostEffect(p => p.Controller[Generic.SHUFFLED_INTO_OWN_DECK]) }));
			// [TLC_521] Eyes in the Sky (2) — Battlecry: Look at 3 cards in your enemy's deck. Pick one to put on top. (Generic.Dredge
			// with three random cards)
			cards.Add("TLC_521", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var look = c.Opponent.DeckZone.GetAll().OrderBy(_ => g.Random.Next()).Take(V("TLC_521", "cards", 3)).ToList();
					if (look.Count > 0) Generic.Dredge(c, c.Opponent, s, cards: look);
				})
			}));
			// [TLC_522] Opu the Unseen (6) — Stealth. Battlecry, Combo, and Deathrattle: Cast 'Fan of Knives'. (with Combo: the Battlecry's
			// cast, deaths, then the Combo's — wiki 34.0.0)
			void FanOfKnives(Game g, Controller c, IEntity s)
			{
				Card fan = Cards.FromAssetId(((IPlayable)s).Card[GameTag.COLLECTION_RELATED_CARD_DATABASE_ID]) ?? Cards.FromId("EX1_129");
				new CastRandomSpellTask().Process(g, c, s, Entity.FromCard(c, fan, new EntityData { [GameTag.CREATOR] = s.Id }));
			}
			cards.Add("TLC_522", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => FanOfKnives(g, c, s)),
				ComboTask = new CustomTask((g, c, s, t, stack) => { FanOfKnives(g, c, s); g.DeathProcessingAndAuraUpdate(); FanOfKnives(g, c, s); }),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => FanOfKnives(g, c, s))
			}));
		}
	}
}
