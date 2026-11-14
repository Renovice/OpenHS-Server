// HsServer addition (not upstream SabberStone): Saviors of Uldum (2019) — upstream left most of the set as TODO stubs.
// These definitions replace the stubs (HsServer card defs are registered last). Every number goes through CardValues (V).
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
		// ------------------------------------------------------------------ shared rules

		/// <summary>"If your deck has no duplicates" (Highlander).</summary>
		private static bool NoDuplicatesInDeck(Controller c) => c.DeckZone.GroupBy(p => p.Card.Id).All(x => x.Count() == 1);

		private static List<Card> Pick(Game g, IEnumerable<Card> pool, int n)
		{
			var list = pool.ToList();
			var picks = new List<Card>();
			while (picks.Count < n && list.Count > 0)
			{
				Card c = list[g.Random.Next(list.Count)];
				list.Remove(c);
				picks.Add(c);
			}
			return picks;
		}

		/// <summary>A random card of the format matching <paramref name="where"/>; never the card generating it
		/// (Cards.GeneratingCard).</summary>
		private static Card RandomCard(Game g, Func<Card, bool> where)
		{
			Card self = Cards.GeneratingCard(g);
			return Pick(g, Cards.GenerationPool(g, Cards.GeneratingPlayer(g)).Where(card => card.Id != self?.Id && where(card)), 1).FirstOrDefault();
		}

		/// <summary>HsServer (2026-10-08): a random minion summoned straight onto the battlefield — never one that can't be (Colossals, the
		/// generation exclusions, self-awakening Dormants: Card.NeverRandomlyOnBoard; wiki Colossal.txt:11 "cannot be generated in a way
		/// that they would be summoned directly onto the battlefield").</summary>
		private static Card RandomBoardMinion(Game g, Func<Card, bool> where) =>
			RandomCard(g, x => x.Type == CardType.MINION && !x.NeverRandomlyOnBoard && where(x));

		private static IEnumerable<Card> OtherClassCards(Game g, Controller c) =>
			Cards.GenerationPool(g, c).Where(card => !card.HasClass(c.HeroClass) && !card.HasClass(CardClass.NEUTRAL)
			                                                   && !card.HasClass(CardClass.INVALID));

		private static void Summon(Game g, Controller c, Card card, IEntity source, int position = -1)
		{
			if (card != null && !c.BoardZone.IsFull)
				Generic.SummonBlock.Invoke(g, (Minion)Entity.FromCard(c, card), position, source);
		}

		/// <summary>
		/// One step (or <paramref name="amount"/>) of a Quest. When it completes, the reward goes to the hand — or, for an
		/// Uldum-style Hero Power reward, replaces the Hero Power (its triggers and aura switched on).
		/// </summary>
		private static void QuestStep(Game g, Controller c, IEntity source, string rewardId, int amount = 1)
		{
			if (!(source is Spell quest) || !c.SecretZone.IsActiveQuest(quest) || amount <= 0) return;
			quest.QuestProgress += amount;
			if (quest.QuestProgress < quest.QuestTotalProgress) return;
			// the reward (a card, or a Hero Power for Uldum quests) comes at the Quest Reward Step
			Generic.CompleteQuest(c, quest, Cards.FromId(rewardId));
		}

		private static CardDef Quest(TriggerType type, string rewardId, TriggerSource source = TriggerSource.FRIENDLY,
			Func<Game, Controller, IPlayable, int> steps = null) => new CardDef(new Power
		{
			Trigger = new Trigger(type)
			{
				TriggerSource = source,
				SingleTask = new CustomTask((g, c, s, t, stack) => QuestStep(g, c, s, rewardId, steps?.Invoke(g, c, t) ?? 1))
			}
		});

		private static void Uldum(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Quests and their rewards

			// [ULD_131] Untapped Potential — Quest: End 4 turns with any unspent Mana. Reward: Ossirian Tear.
			cards.Add("ULD_131", Quest(TriggerType.TURN_END, "ULD_131p", TriggerSource.ALL,
				(g, c, t) => g.CurrentPlayer == c && c.RemainingMana > 0 ? 1 : 0));
			// [ULD_131p] Ossirian Tear — Passive: Your Choose One cards have both effects combined.
			cards.Add("ULD_131p", new CardDef(new Power { Aura = new Aura(AuraType.CONTROLLER, new Effect(GameTag.CHOOSE_BOTH, EffectOperator.SET, 1)) }));

			// [ULD_140] Supreme Archaeology — Quest: Draw 20 cards. Reward: Tome of Origination.
			cards.Add("ULD_140", Quest(TriggerType.DRAW, "ULD_140p"));
			// [ULD_140p] Tome of Origination — Draw a card. It costs (0).
			cards.Add("ULD_140p", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (Generic.Draw(c) is IPlayable drawn && drawn.Zone?.Type == SabberStoneCore.Enums.Zone.HAND)
						Give(g, s, drawn, "ULD_140e");
				})
			}));
			cards.Add("ULD_140e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) }));

			// [ULD_155] Unseal the Vault — Quest: Summon 20 minions. Reward: Pharaoh's Warmask.
			cards.Add("ULD_155", Quest(TriggerType.AFTER_SUMMON, "ULD_155p", TriggerSource.MINIONS));
			// [ULD_155p] Pharaoh's Warmask — Give your minions +2 Attack.
			cards.Add("ULD_155p", new CardDef(new Power { PowerTask = new AddEnchantmentTask("ULD_155e", EntityType.MINIONS) }));
			cards.Add("ULD_155e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("ULD_155p", "attack", 2))) }));

			// [ULD_291] Corrupt the Waters — Quest: Play 6 Battlecry cards. Reward: Heart of Vir'naal.
			cards.Add("ULD_291", Quest(TriggerType.PLAY_CARD, "ULD_291p", TriggerSource.FRIENDLY,
				(g, c, t) => t != null && t.Card[GameTag.BATTLECRY] > 0 ? 1 : 0));
			// [ULD_291p] Heart of Vir'naal — Your Battlecries trigger twice this turn.
			cards.Add("ULD_291p", new CardDef(new Power { PowerTask = new AddEnchantmentTask("ULD_291pe", EntityType.CONTROLLER) }));
			cards.Add("ULD_291pe", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER, new Effect(GameTag.EXTRA_BATTLECRIES_BASE, EffectOperator.SET, 1))
				{
					RemoveTrigger = (TriggerType.TURN_END, null)
				}
			}));

			// [ULD_326] Bazaar Burglary — Quest: Add 4 cards from other classes to your hand. Reward: Ancient Blades.
			cards.Add("ULD_326", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ADD_TO_HAND)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => !p.Card.HasClass(p.Controller.HeroClass) && !p.Card.HasClass(CardClass.NEUTRAL) && !p.Card.HasClass(CardClass.INVALID)),
					SingleTask = new CustomTask((g, c, s, t, stack) => QuestStep(g, c, s, "ULD_326p"))
				}
			}));
			// [ULD_326p] Ancient Blades — Equip a 3/2 Blade with Immune while attacking.
			cards.Add("ULD_326p", new CardDef(new Power { PowerTask = new WeaponTask("ULD_326t") }));

			// [ULD_431] Making Mummies — Quest: Play 5 Reborn minions. Reward: Emperor Wraps.
			cards.Add("ULD_431", Quest(TriggerType.PLAY_MINION, "ULD_431p", TriggerSource.FRIENDLY,
				(g, c, t) => t != null && t.Card[GameTag.REBORN] > 0 ? 1 : 0));
			// [ULD_431p] Emperor Wraps — Summon a 2/2 copy of a friendly minion.
			cards.Add("ULD_431p", new CardDef(new Dictionary<PlayReq, int>
				{
					{ PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_NUM_MINION_SLOTS, 1 }
				},
				new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(t is Minion m) || c.BoardZone.IsFull) return;
						var copy = (Minion)Entity.FromCard(c, m.Card);
						Generic.SummonBlock.Invoke(g, copy, m.ZonePosition + 1, s);
						Give(g, s, copy, "ULD_431e");
					})
				}));
			cards.Add("ULD_431e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("ULD_431p", "stats", 2))) }));

			// [ULD_433] Raid the Sky Temple — Quest: Cast 10 spells. Reward: Ascendant Scroll.
			cards.Add("ULD_433", Quest(TriggerType.CAST_SPELL, "ULD_433p"));
			// [ULD_433p] Ascendant Scroll — Add a random Mage spell to your hand. It costs (2) less.
			cards.Add("ULD_433p", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card spell = RandomCard(g, card => card.Type == CardType.SPELL && card.HasClass(CardClass.MAGE));
					if (spell == null || c.HandZone.IsFull) return;
					IPlayable added = Entity.FromCard(c, spell);
					Generic.AddHandPhase.Invoke(c, added);
					Give(g, s, added, "ULD_433e");
				})
			}));
			cards.Add("ULD_433e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("ULD_433p", "discount", 2))) }));

			// [ULD_711] Hack the System — Quest: Attack 5 times with your hero. Reward: Anraphet's Core.
			cards.Add("ULD_711", Quest(TriggerType.ATTACK, "ULD_711p3", TriggerSource.HERO));
			// [ULD_711p3] Anraphet's Core — Summon a 4/3 Golem. After your hero attacks, refresh this.
			cards.Add("ULD_711p3", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new SummonTask("ULD_711t", SummonSide.DEFAULT),
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) => c.Hero.HeroPower.IsExhausted = false)
				}
			}));

			// [ULD_724] Activate the Obelisk — Quest: Restore 15 Health. Reward: Obelisk's Eye.
			cards.Add("ULD_724", Quest(TriggerType.HEAL, "ULD_724p", TriggerSource.ALL,
				(g, c, t) => g.CurrentEventData?.EventSource?.Controller == c ? g.CurrentEventData.EventNumber : 0));
			// [ULD_724p] Obelisk's Eye — Restore 3 Health. If you target a minion, also give it +3/+3.
			cards.Add("ULD_724p", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(
					new HealTask(V("ULD_724p", "heal", 3), EntityType.TARGET),
					new CustomTask((g, c, s, t, stack) => { if (t is Minion) Give(g, s, t, "ULD_724e"); }))
			}));
			cards.Add("ULD_724e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ULD_724p", "stats", 3))) }));

			// ---------------------------------------------------------------- Neutral

			// [ULD_003] Zephrys the Great (2) — Battlecry: If your deck has no duplicates, wish for the perfect card.
			// (Generic.WishForThePerfectCard: the wiki's pool and categories, judged on copies of the game.)
			cards.Add("ULD_003", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => NoDuplicatesInDeck(c),
					new CustomTask((g, c, s, t, stack) => Generic.WishForThePerfectCard(g, c, s)))
			}));
			// [ONY_005tb610] Zephrys's Lamp — Wish for the perfect card.
			cards.Add("ONY_005tb610", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.WishForThePerfectCard(g, c, s))
			}));

			// [ULD_157] Questing Explorer (2) — Battlecry: If you control a Quest, draw a card.
			cards.Add("ULD_157", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => stack.Flag = c.SecretZone.Quest != null),
					new FlagTask(true, new DrawTask()))
			}));

			// [ULD_174] Serpent Egg (2) — Deathrattle: Summon a 3/4 Sea Serpent.
			cards.Add("ULD_174", new CardDef(new Power { DeathrattleTask = new SummonTask("ULD_174t", SummonSide.DEATHRATTLE) }));

			// [ULD_177] Octosari (8) — Deathrattle: Draw 8 cards.
			var octosari = new CardDef(new Power { DeathrattleTask = new EnqueueTask(V("ULD_177", "cards", 8), new DrawTask()) });
			cards.Add("ULD_177", octosari);
			cards.Add("CORE_ULD_177", octosari);

			// [ULD_179] Phalanx Commander (5) — Your Taunt minions have +2 Attack.
			cards.Add("ULD_179", new CardDef(new Power
			{
				Aura = new Aura(AuraType.BOARD, "ULD_179e") { Condition = new SelfCondition(p => p is Minion m && m.HasTaunt) }
			}));
			cards.Add("ULD_179e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("ULD_179", "attack", 2))) }));

			// [ULD_180] Sunstruck Henchman (4) — At the start of your turn, this has a 50% chance to fall asleep.
			cards.Add("ULD_180", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me && g.Random.Next(100) < V("ULD_180", "chance %", 50)) me.IsExhausted = true;
					})
				}
			}));

			// [ULD_182] Spitting Camel (2) — At the end of your turn, deal 1 damage to another random friendly minion.
			cards.Add("ULD_182", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = ComplexTask.DamageRandomTargets(1, EntityType.MINIONS_NOSOURCE, V("ULD_182", "damage", 1))
				}
			}));

			// [ULD_183] Anubisath Warbringer (9) — Deathrattle: Give all minions in your hand +3/+3.
			cards.Add("ULD_183", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.HandZone.Where(p => Generic.IsMinionCard(p)), "ULD_183e"))
			}));
			cards.Add("ULD_183e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ULD_183", "stats", 3))) }));

			// [ULD_184] Kobold Sandtrooper (2) — Deathrattle: Deal 3 damage to the enemy hero.
			cards.Add("ULD_184", new CardDef(new Power { DeathrattleTask = new DamageTask(V("ULD_184", "damage", 3), EntityType.OP_HERO) }));

			// [ULD_185] Temple Berserker (2) — Reborn. Has +2 Attack while damaged.
			cards.Add("ULD_185", new CardDef(new Power { Aura = new EnrageEffect(AuraType.SELF, "ULD_185e") }));
			cards.Add("ULD_185e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("ULD_185", "attack", 2))) }));

			// [ULD_188] Golden Scarab (3) — Battlecry: Discover a 4-Cost card.
			cards.Add("ULD_188", new CardDef(new Power { PowerTask = new DiscoverTask(CardType.INVALID, CardClass.INVALID, (GameTag.COST, RelaSign.EQ, V("ULD_188", "cost", 4))) }));

			// [ULD_189] Faceless Lurker (5) — Taunt. Battlecry: Double this minion's Health.
			cards.Add("ULD_189", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me) Give(g, s, me, "ULD_189e", me.Health); })
			}));
			cards.Add("ULD_189e", new CardDef(new Power { Enchant = new Enchant(GameTag.HEALTH, EffectOperator.ADD) }));

			// [ULD_190] Pit Crocolisk (8) — Battlecry: Deal 5 damage.
			cards.Add("ULD_190", new CardDef(BattlecryAny, new Power { PowerTask = new DamageTask(V("ULD_190", "damage", 5), EntityType.TARGET) }));

			// [ULD_196] Neferset Ritualist (2) — Battlecry: Restore adjacent minions to full Health.
			cards.Add("ULD_196", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					foreach (Minion m in me.GetAdjacentMinions())
						if (m.Damage > 0) m.TakeHeal((IPlayable)s, m.Damage);
				})
			}));

			// [ULD_197] Quicksand Elemental (2) — Battlecry: Give all enemy minions -2 Attack this turn.
			cards.Add("ULD_197", new CardDef(new Power { PowerTask = new AddEnchantmentTask("ULD_197e", EntityType.OP_MINIONS) }));
			cards.Add("ULD_197e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(-V("ULD_197", "attack", 2))) { IsOneTurnEffect = true } }));

			// [ULD_198] Conjured Mirage (4) — Taunt. At the start of your turn, shuffle this minion into your deck.
			cards.Add("ULD_198", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					// the minion itself moves to the deck (a backward move: buffs and a pending death are cleared)
					SingleTask = new MoveToDeck(EntityType.SOURCE)
				}
			}));

			// [ULD_208] Khartut Defender (6) — Taunt, Reborn. Deathrattle: Restore 3 Health to your hero.
			cards.Add("ULD_208", new CardDef(new Power { DeathrattleTask = new HealTask(V("ULD_208", "heal", 3), EntityType.HERO) }));

			// [ULD_209] Vulpera Scoundrel (3) — Battlecry: Discover a spell or pick a mystery choice.
			var scoundrel = new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.HandZone.IsFull) return;
					var offer = Pick(g, Cards.GenerationPool(g, c).Where(card => card.Type == CardType.SPELL
						&& (card.HasClass(c.HeroClass) || card.HasClass(CardClass.NEUTRAL))), 3);
					offer.Add(Cards.FromId("ULD_209t"));
					Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.STACK, offer.ToArray(),
						new CustomTask((g2, c2, s2, chosen, stack2) =>
						{
							if (chosen == null) return;
							Card card = chosen.Card.Id == "ULD_209t"
								? RandomCard(g2, x => x.Type == CardType.SPELL && (x.HasClass(c2.HeroClass) || x.HasClass(CardClass.NEUTRAL)))
								: chosen.Card;
							if (card != null) AddToHand(c2, card);
						}));
				})
			});
			cards.Add("ULD_209", scoundrel);
			cards.Add("CORE_ULD_209", scoundrel);

			// [ULD_214] Generous Mummy (3) — Reborn. Your opponent's cards cost (1) less.
			cards.Add("ULD_214", new CardDef(new Power { Aura = new Aura(AuraType.OP_HAND, Effects.ReduceCost(V("ULD_214", "discount", 1))) }));

			// [ULD_215] Wrapped Golem (7) — Reborn. At the end of your turn, summon a 1/1 Scarab with Taunt.
			cards.Add("ULD_215", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new SummonTask("ULD_215t", SummonSide.RIGHT) }
			}));

			// [ULD_229] Mischief Maker (3) — Battlecry: Swap the top card of your deck with your opponent's.
			cards.Add("ULD_229", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.DeckZone.Count == 0 || c.Opponent.DeckZone.Count == 0) return;
					// the two cards themselves swap decks, each on top (moves: Generic.GiveTo; new copies left the originals as
					// ghosts in the decks)
					IPlayable mine = c.DeckZone.TopCard, theirs = c.Opponent.DeckZone.TopCard;
					c.Opponent.DeckZone.Add(Generic.GiveTo(mine, c.Opponent));
					c.DeckZone.Add(Generic.GiveTo(theirs, c));
				})
			}));

			// [ULD_250] Infested Goblin (3) — Taunt. Deathrattle: Add two 1/1 Scarabs with Taunt to your hand.
			cards.Add("ULD_250", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("ULD_250", "scarabs", 2); i++) AddToHand(c, Cards.FromId("ULD_215t"));
				})
			}));

			// [ULD_282] Jar Dealer (1) — Deathrattle: Add a random 1-Cost minion to your hand.
			cards.Add("ULD_282", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card card = RandomCard(g, x => x.Type == CardType.MINION && x.Cost == V("ULD_282", "cost", 1));
					if (card != null) AddToHand(c, card);
				})
			}));

			// [ULD_289] Fishflinger (2) — Battlecry: Add a random Murloc to each player's hand.
			cards.Add("ULD_289", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Controller p in new[] { c, c.Opponent })
						if (RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.MURLOC)) is Card card) AddToHand(p, card);
				})
			}));

			// [ULD_290] History Buff (3) — Whenever you play a minion, give a random minion in your hand +1/+1.
			cards.Add("ULD_290", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var minions = c.HandZone.Where(p => Generic.IsMinionCard(p)).ToList();
						if (minions.Count > 0) Give(g, s, minions[g.Random.Next(minions.Count)], "ULD_290e");
					})
				}
			}));
			cards.Add("ULD_290e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ULD_290", "stats", 1))) }));

			// [ULD_304] King Phaoris (10) — Battlecry: For each spell in your hand, summon a random minion of the same Cost.
			cards.Add("ULD_304", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable spell in c.HandZone.Where(p => p is Spell).ToList())
						Summon(g, c, RandomMinionOfCost(g, spell.Cost), s);
				})
			}));

			// [ULD_309] Dwarven Archaeologist (2) — After you Discover a card, reduce its cost by (1).
			cards.Add("ULD_309", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_DISCOVER)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.Zone?.Type == SabberStoneCore.Enums.Zone.HAND),
					SingleTask = new AddEnchantmentTask("ULD_309e", EntityType.TARGET)
				}
			}));
			cards.Add("ULD_309e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("ULD_309", "discount", 1))) }));

			// [ULD_702] Mortuary Machine (5) — After your opponent plays a minion, give it Reborn.
			cards.Add("ULD_702", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m) m[GameTag.REBORN] = 1; })
				}
			}));

			// [ULD_703] Desert Obelisk (5) — If you control 3 of these at the end of your turn, deal 5 damage to a random enemy.
			cards.Add("ULD_703", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = ComplexTask.Create(
						new CustomTask((g, c, s, t, stack) => stack.Flag = c.BoardZone.GetAll().Count(m => m.Card.Id == "ULD_703") >= V("ULD_703", "obelisks", 3)),
						new FlagTask(true, ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, V("ULD_703", "damage", 5))))
				}
			}));

			// [ULD_705] Mogu Cultist (1) — Battlecry: If your board is full of Mogu Cultists, sacrifice them all and summon
			// Highkeeper Ra.
			cards.Add("ULD_705", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.BoardZone.Count >= Game.MAX_MINIONS_ON_BOARD && c.BoardZone.GetAll().All(m => m.Card.Id == s.Card.Id),
					new CustomTask((g, c, s, t, stack) =>
				{
					var board = c.BoardZone.GetAll();
					foreach (Minion m in board) m.Destroy();
					g.DeathProcessingAndAuraUpdate();
					Summon(g, c, Cards.FromId("ULD_705t"), s);
				}))
			}));
			// [ULD_705t] Highkeeper Ra — At the end of your turn, deal 20 damage to all enemies.
			cards.Add("ULD_705t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new DamageTask(V("ULD_705t", "damage", 20), EntityType.ENEMIES) }
			}));

			// [ULD_706] Blatant Decoy (6) — Deathrattle: Each player summons the lowest Cost minion from their hand.
			cards.Add("ULD_706", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Controller p in new[] { c, c.Opponent })
					{
						var minions = p.HandZone.Where(x => Generic.IsMinionCard(x)).ToList();
						if (minions.Count == 0 || p.BoardZone.IsFull) continue;
						int low = minions.Min(x => x.Cost);
						var lowest = minions.Where(x => x.Cost == low).ToList();
						var m = (Minion)lowest[g.Random.Next(lowest.Count)];
						p.HandZone.Remove(m);
						Generic.SummonBlock.Invoke(g, m, -1, s);
					}
				})
			}));

			// [ULD_712] Bug Collector (2) — Battlecry: Summon a 1/1 Locust with Rush.
			cards.Add("ULD_712", new CardDef(new Power { PowerTask = new SummonTask("ULD_430t", SummonSide.RIGHT) }));

			// [ULD_719] Desert Hare (3) — Battlecry: Summon two 1/1 Desert Hares.
			cards.Add("ULD_719", new CardDef(new Power { PowerTask = new SummonTask("ULD_719", V("ULD_719", "hares", 2), SummonSide.RIGHT) }));

			// [ULD_727] Body Wrapper (4) — Battlecry: Discover a friendly minion that died this game. Shuffle it into your deck.
			cards.Add("ULD_727", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var died = c.DiedThisGameCards.Where(p => p is Minion).Select(p => p.Card)
						.GroupBy(card => card.Id).Select(x => x.First());
					var offer = Pick(g, died, 3);
					if (offer.Count == 0) return;
					Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.STACK, offer.ToArray(),
						new CustomTask((g2, c2, s2, chosen, stack2) =>
						{
							if (chosen != null) Generic.ShuffleIntoDeck.Invoke(c2, s2, Entity.FromCard(c2, chosen.Card));
						}));
				})
			}));

			// ---------------------------------------------------------------- Druid

			// [ULD_134] BEEEES!!! (3) — Choose a minion. Summon four 1/1 Bees that attack it.
			cards.Add("ULD_134", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } }, new Power
			{
				// (VIOLET_HOLD review: the shared "summon N X that attack it", Emergency Surgery's too)
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion target) Generic.SummonThatAttack(c, s, Cards.FromId("ULD_134t"), V("ULD_134", "bees", 4), target);
				})
			}));

			// [ULD_135] Hidden Oasis (6) — Choose One - Summon a 6/6 Ancient with Taunt; or Restore 12 Health.
			cards.Add("ULD_135", new CardDef(new Power()));
			cards.Add("ULD_135a", new CardDef(new Power { PowerTask = new SummonTask("ULD_135at", SummonSide.SPELL) }));
			cards.Add("ULD_135b", new CardDef(TargetAny, new Power { PowerTask = new HealTask(V("ULD_135b", "heal", 12), EntityType.TARGET) }));

			// [ULD_136] Worthy Expedition (1) — Discover a Choose One card.
			cards.Add("ULD_136", new CardDef(new Power { PowerTask = new DiscoverTask(CardType.INVALID, CardClass.DRUID, (GameTag.CHOOSE_ONE, RelaSign.EQ, 1)) }));

			// [ULD_137] Garden Gnome (4) — Battlecry: If you're holding a spell that costs (5) or more, summon two 2/2 Treants.
			cards.Add("ULD_137", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => stack.Flag = c.HandZone.Any(p => p is Spell && p.Cost >= V("ULD_137", "spell cost", 5))),
					new FlagTask(true, new SummonTask("ULD_137t", V("ULD_137", "treants", 2), SummonSide.RIGHT)))
			}));

			// [ULD_138] Anubisath Defender (5) — Taunt. Costs (0) if you've cast a spell that costs (5) or more this turn.
			cards.Add("ULD_138", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.CardsPlayedThisTurn.Any(card => card.Type == CardType.SPELL && card.Cost >= V("ULD_138", "spell cost", 5))
					? p.Card.Cost : 0)
			}));

			// [ULD_139] Elise the Enlightened (5) — Battlecry: If your deck has no duplicates, duplicate your hand.
			cards.Add("ULD_139", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => NoDuplicatesInDeck(c), new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in c.HandZone.ToList()) AddToHand(c, p.Card);
				}))
			}));

			// [ULD_273] Overflow (7) — Restore 5 Health to all characters. Draw 5 cards.
			cards.Add("ULD_273", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new HealTask(V("ULD_273", "heal", 5), EntityType.ALL), new EnqueueTask(V("ULD_273", "cards", 5), new DrawTask()))
			}));

			// [ULD_292] Oasis Surger (5) — Rush. Choose One - Gain +2/+2; or Summon a copy of this minion.
			cards.Add("ULD_292", new CardDef(new Power()));
			cards.Add("ULD_292a", new CardDef(new Power { PowerTask = new AddEnchantmentTask("ULD_292ae", EntityType.SOURCE) }));
			cards.Add("ULD_292ae", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ULD_292a", "stats", 2))) }));
			cards.Add("ULD_292b", new CardDef(new Power { PowerTask = new SummonCopyTask(EntityType.SOURCE, side: SummonSide.RIGHT) }));

			// ---------------------------------------------------------------- Hunter

			// [ULD_151] Ramkahen Wildtamer (3) — Battlecry: Copy a random Beast in your hand.
			cards.Add("ULD_151", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var beasts = c.HandZone.Where(p => p is Minion m && m.IsRace(Race.BEAST)).ToList();
					if (beasts.Count > 0) AddToHand(c, beasts[g.Random.Next(beasts.Count)].Card);
				})
			}));

			// [ULD_154] Hyena Alpha (4) — Battlecry: If you control a Secret, summon two 2/2 Hyenas.
			cards.Add("ULD_154", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new ConditionTask(EntityType.SOURCE, SelfCondition.IsControllingSecret),
					new FlagTask(true, new SummonTask("ULD_154t", V("ULD_154", "hyenas", 2), SummonSide.RIGHT)))
			}));

			// [ULD_156] Dinotamer Brann (7) — Battlecry: If your deck has no duplicates, summon King Krush.
			cards.Add("ULD_156", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => NoDuplicatesInDeck(c), new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("ULD_156t3"), s)))
			}));

			// [ULD_212] Wild Bloodstinger (6) — Battlecry: Summon a minion from your opponent's hand. Attack it.
			cards.Add("ULD_212", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.Opponent.HandZone.Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.Count == 0 || c.Opponent.BoardZone.IsFull || !(s is Minion me)) return;
					var victim = (Minion)minions[g.Random.Next(minions.Count)];
					c.Opponent.HandZone.Remove(victim);
					Generic.SummonBlock.Invoke(g, victim, -1, s);
					if (me.IsDead || victim.IsDead) return;
					EventMetaData temp = g.CurrentEventData;
					Generic.AttackBlock.Invoke(c, me, victim, true, true);
					g.DeathProcessingAndAuraUpdate();
					g.CurrentEventData = temp;
				})
			}));

			// [ULD_410] Scarlet Webweaver (6) — Battlecry: Reduce the Cost of a random Beast in your hand by (5).
			cards.Add("ULD_410", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var beasts = c.HandZone.Where(p => p is Minion m && m.IsRace(Race.BEAST)).ToList();
					if (beasts.Count > 0) Give(g, s, beasts[g.Random.Next(beasts.Count)], "ULD_410e");
				})
			}));
			cards.Add("ULD_410e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("ULD_410", "discount", 5))) }));

			// [ULD_429] Hunter's Pack (3) — Add a random Hunter Beast, Secret, and weapon to your hand.
			cards.Add("ULD_429", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Func<Card, bool> kind in new Func<Card, bool>[]
					{
						x => x.Type == CardType.MINION && x.IsRace(Race.BEAST), x => x.Type == CardType.SPELL && x.IsSecret, x => x.Type == CardType.WEAPON
					})
						if (RandomCard(g, x => x.HasClass(CardClass.HUNTER) && kind(x)) is Card card) AddToHand(c, card);
				})
			}));

			// [ULD_430] Desert Spear (3) — After your hero attacks, summon a 1/1 Locust with Rush.
			cards.Add("ULD_430", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new SummonTask("ULD_430t", SummonSide.DEFAULT)
				}
			}));

			// [ULD_713] Swarm of Locusts (6) — Summon seven 1/1 Locusts with Rush.
			cards.Add("ULD_713", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } },
				new Power { PowerTask = new SummonTask("ULD_430t", V("ULD_713", "locusts", 7), SummonSide.SPELL) }));

			// ---------------------------------------------------------------- Mage

			// [ULD_216] Puzzle Box of Yogg-Saron (10) — Cast 10 random spells (targets chosen randomly).
			cards.Add("ULD_216", new CardDef(new Power { PowerTask = new EnqueueTask(V("ULD_216", "spells", 10), new CastRandomSpellTask()) }));

			// [ULD_236] Tortollan Pilgrim (8) — Battlecry: Discover a copy of a spell in your deck and cast it with random targets.
			cards.Add("ULD_236", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var spells = c.DeckZone.Where(p => p is Spell).Select(p => p.Card).GroupBy(x => x.Id).Select(x => x.First());
					var offer = Pick(g, spells, 3);
					if (offer.Count > 0)
						Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.SPELL_RANDOM, offer.ToArray(), null);
				})
			}));

			// [ULD_238] Reno the Relicologist (6) — Battlecry: If your deck has no duplicates, deal 10 damage randomly split among
			// all enemy minions.
			cards.Add("ULD_238", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => stack.Flag = NoDuplicatesInDeck(c)),
					new FlagTask(true, new EnqueueTask(V("ULD_238", "damage", 10), ComplexTask.DamageRandomTargets(1, EntityType.OP_MINIONS, 1))))
			}));

			// [ULD_239] Flame Ward (3) — Secret: After a minion attacks your hero, deal 3 damage to all enemy minions.
			cards.Add("ULD_239", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.OP_MINIONS,
					Condition = new SelfCondition(p => p.Game.CurrentEventData?.EventTarget is Hero h && h.Controller != p.Controller),
					SingleTask = ComplexTask.Secret(new DamageTask(V("ULD_239", "damage", 3), EntityType.OP_MINIONS, true))
				}
			}));

			// [ULD_240] Arcane Flakmage (2) — After you play a Secret, deal 2 damage to all enemy minions.
			cards.Add("ULD_240", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = SelfCondition.IsSecret,
					SingleTask = new DamageTask(V("ULD_240", "damage", 2), EntityType.OP_MINIONS)
				}
			}));

			// [ULD_293] Cloud Prince (5) — Battlecry: If you control a Secret, deal 6 damage.
			cards.Add("ULD_293", new CardDef(BattlecryAny, new Power
			{
				PowerTask = ComplexTask.Create(
					new ConditionTask(EntityType.SOURCE, SelfCondition.IsControllingSecret),
					new FlagTask(true, new DamageTask(V("ULD_293", "damage", 6), EntityType.TARGET)))
			}));

			// [ULD_329] Dune Sculptor (3) — After you cast a spell, add a random Mage minion to your hand.
			cards.Add("ULD_329", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (RandomCard(g, x => x.Type == CardType.MINION && x.HasClass(CardClass.MAGE)) is Card card) AddToHand(c, card);
					})
				}
			}));

			// [ULD_435] Naga Sand Witch (5) — Battlecry: Change the Cost of spells in your hand to (5).
			cards.Add("ULD_435", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.Where(p => p is Spell), "ULD_435e"))
			}));
			cards.Add("ULD_435e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(V("ULD_435", "cost", 5))) }));

			// [ULD_726] Ancient Mysteries (2) — Draw a Secret from your deck. It costs (0).
			cards.Add("ULD_726", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var secrets = c.DeckZone.Where(p => p is Spell sp && sp.IsSecret).ToList();
					if (secrets.Count == 0) return;
					if (Generic.Draw(c, secrets[g.Random.Next(secrets.Count)]) is IPlayable drawn && drawn.Zone?.Type == SabberStoneCore.Enums.Zone.HAND)
						Give(g, s, drawn, "ULD_726e");
				})
			}));
			cards.Add("ULD_726e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) }));

			// ---------------------------------------------------------------- Paladin

			// [ULD_143] Pharaoh's Blessing (6) — Give a minion +4/+4, Divine Shield, and Taunt.
			cards.Add("ULD_143", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("ULD_143e", EntityType.TARGET) }));
			cards.Add("ULD_143e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("ULD_143", "stats", 4)), Effects.Health_N(V("ULD_143", "stats", 4)),
					new Effect(GameTag.DIVINE_SHIELD, EffectOperator.SET, 1), new Effect(GameTag.TAUNT, EffectOperator.SET, 1))
			}));

			// [ULD_145] Brazen Zealot (1) — Whenever you summon a minion, gain +1 Attack.
			cards.Add("ULD_145", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.SUMMON)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					SingleTask = new AddEnchantmentTask("ULD_145e", EntityType.SOURCE)
				}
			}));
			cards.Add("ULD_145e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("ULD_145", "attack", 1))) }));

			// [ULD_217] Micro Mummy (2) — Reborn. At the end of your turn, give another random friendly minion +1 Attack.
			cards.Add("ULD_217", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = ComplexTask.Create(
						new IncludeTask(EntityType.MINIONS_NOSOURCE),
						new FilterStackTask(SelfCondition.IsNotDead),
						new RandomTask(1, EntityType.STACK),
						new AddEnchantmentTask("ULD_217e", EntityType.STACK))
				}
			}));
			cards.Add("ULD_217e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("ULD_217", "attack", 1))) }));

			// [ULD_438] Salhet's Pride (3) — Deathrattle: Draw two 1-Health minions from your deck.
			cards.Add("ULD_438", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("ULD_438", "minions", 2); i++)
					{
						var ones = c.DeckZone.Where(p => p is Minion m && m.Card.Health == 1).ToList();
						if (ones.Count == 0) break;
						Generic.Draw(c, ones[g.Random.Next(ones.Count)]);
					}
				})
			}));

			// [ULD_439] Sandwasp Queen (2) — Battlecry: Add two 2/1 Sandwasps to your hand.
			cards.Add("ULD_439", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("ULD_439", "sandwasps", 2); i++) AddToHand(c, Cards.FromId("ULD_439t"));
				})
			}));

			// [ULD_500] Sir Finley of the Sands (2) — Battlecry: If your deck has no duplicates, Discover an upgraded Hero Power.
			cards.Add("ULD_500", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => NoDuplicatesInDeck(c), new CustomTask((g, c, s, t, stack) =>
				{
					var upgraded = s.Card.Entourage.Select(Cards.TryFromId).Where(x => x != null && !x.HasClass(c.HeroClass));
					var offer = Pick(g, upgraded, 3);
					if (offer.Count > 0)
						Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.HEROPOWER, offer.ToArray(), null);
				}))
			}));

			// [ULD_716] Tip the Scales (8) — Summon 7 Murlocs from your deck.
			cards.Add("ULD_716", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("ULD_716", "murlocs", 7) && !c.BoardZone.IsFull; i++)
					{
						var murlocs = c.DeckZone.Where(p => p is Minion m && m.IsRace(Race.MURLOC)).ToList();
						if (murlocs.Count == 0) break;
						var m = (Minion)murlocs[g.Random.Next(murlocs.Count)];
						c.DeckZone.Remove(m);
						Generic.SummonBlock.Invoke(g, m, -1, s);
					}
				})
			}));

			// [ULD_728] Subdue (2) — Set a minion's Attack and Health to 1.
			cards.Add("ULD_728", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("ULD_728e", EntityType.TARGET) }));
			cards.Add("ULD_728e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("ULD_728", "stats", 1))) }));

			// ---------------------------------------------------------------- Priest

			// [ULD_262] High Priest Amet (4) — Whenever you summon a minion, set its Health equal to this minion's.
			cards.Add("ULD_262", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.SUMMON)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion amet && t is Minion summoned) Give(g, s, summoned, "ULD_262e", amet.Health);
					})
				}
			}));
			cards.Add("ULD_262e", new CardDef(new Power { Enchant = new Enchant(GameTag.HEALTH, EffectOperator.SET) }));

			// [ULD_265] Embalming Ritual (1) — Give a minion Reborn.
			cards.Add("ULD_265", new CardDef(TargetMinion, new Power { PowerTask = new SetGameTagTask(GameTag.REBORN, 1, EntityType.TARGET) }));

			// [ULD_266] Grandmummy (2) — Reborn. Deathrattle: Give a random friendly minion +1/+1.
			cards.Add("ULD_266", new CardDef(new Power
			{
				DeathrattleTask = ComplexTask.Create(
					new IncludeTask(EntityType.MINIONS),
					new FilterStackTask(SelfCondition.IsNotDead),
					new RandomTask(1, EntityType.STACK),
					new AddEnchantmentTask("ULD_266e", EntityType.STACK))
			}));
			cards.Add("ULD_266e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ULD_266", "stats", 1))) }));

			// [ULD_268] Psychopomp (4) — Battlecry: Summon a random friendly minion that died this game. Give it Reborn.
			cards.Add("ULD_268", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var died = c.DiedThisGameCards.Where(p => p is Minion).ToList();
					if (died.Count == 0 || c.BoardZone.IsFull) return;
					var copy = (Minion)Entity.FromCard(c, died[g.Random.Next(died.Count)].Card);
					copy[GameTag.REBORN] = 1;
					Generic.SummonBlock.Invoke(g, copy, -1, s);
				})
			}));

			// [ULD_269] Wretched Reclaimer (3) — Battlecry: Destroy a friendly minion, then return it to life with full Health.
			cards.Add("ULD_269", new CardDef(BattlecryFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion victim) || victim.Id == s.Id) return;
					int position = victim.ZonePosition;
					victim.Destroy();
					g.DeathProcessingAndAuraUpdate();
					Summon(g, c, victim.Card, s, System.Math.Min(position, c.BoardZone.Count));
				})
			}));

			// [ULD_270] Sandhoof Waterbearer (5) — At the end of your turn, restore 5 Health to a damaged friendly character.
			cards.Add("ULD_270", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var hurt = c.BoardZone.GetAll().Where(m => m.Damage > 0).Cast<ICharacter>().ToList();
						if (c.Hero.Damage > 0) hurt.Add(c.Hero);
						if (hurt.Count > 0) hurt[g.Random.Next(hurt.Count)].TakeHeal((IPlayable)s, V("ULD_270", "heal", 5));
					})
				}
			}));

			// [ULD_272] Holy Ripple (2) — Deal 1 damage to all enemies. Restore 1 Health to all friendly characters.
			cards.Add("ULD_272", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("ULD_272", "damage", 1), EntityType.ENEMIES, true),
					new HealTask(V("ULD_272", "heal", 1), EntityType.FRIENDS))
			}));

			// [ULD_714] Penance (2) — Lifesteal. Deal 3 damage to a minion.
			cards.Add("ULD_714", new CardDef(TargetMinion, new Power { PowerTask = new DamageTask(V("ULD_714", "damage", 3), EntityType.TARGET, true) }));

			// [ULD_718] Plague of Death (9) — Silence and destroy all minions.
			cards.Add("ULD_718", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new SilenceTask(EntityType.ALLMINIONS), new DestroyTask(EntityType.ALLMINIONS))
			}));

			// ---------------------------------------------------------------- Rogue

			// [ULD_186] Pharaoh Cat (1) — Battlecry: Add a random Reborn minion to your hand.
			cards.Add("ULD_186", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (RandomCard(g, x => x.Type == CardType.MINION && x[GameTag.REBORN] > 0) is Card card) AddToHand(c, card);
				})
			}));

			// [ULD_231] Whirlkick Master (2) — Whenever you play a Combo card, add a random Combo card to your hand.
			cards.Add("ULD_231", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = SelfCondition.IsComboCard,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (RandomCard(g, x => x[GameTag.COMBO] > 0) is Card card) AddToHand(c, card);
					})
				}
			}));

			// [ULD_285] Hooked Scimitar (3) — Combo: Gain +2 Attack.
			cards.Add("ULD_285", new CardDef(new Power { ComboTask = new AddEnchantmentTask("ULD_285e", EntityType.SOURCE) }));
			cards.Add("ULD_285e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("ULD_285", "attack", 2))) }));

			// [ULD_286] Shadow of Death (4) — Choose a minion. Shuffle 3 'Shadows' into your deck that summon a copy when drawn.
			cards.Add("ULD_286", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					for (int i = 0; i < V("ULD_286", "shadows", 3); i++)
					{
						IPlayable shadow = Entity.FromCard(c, Cards.FromId("ULD_286t"));
						shadow[GameTag.TAG_SCRIPT_DATA_NUM_1] = m.Card.AssetId;
						Generic.ShuffleIntoDeck.Invoke(c, s, shadow);
					}
				})
			}));
			// [ULD_286t] Shadow — Casts When Drawn: Summon a {0}.
			cards.Add("ULD_286t", new CardDef(new Power
			{
				TopdeckTask = new CustomTask((g, c, s, t, stack) =>
				{
					int assetId = s[GameTag.TAG_SCRIPT_DATA_NUM_1];
					if (assetId > 0) Summon(g, c, Cards.FromAssetId(assetId), s);
				})
			}));

			// [ULD_288] Anka, the Buried (5) — Battlecry: Change each Deathrattle minion in your hand into a 1/1 that costs (1).
			cards.Add("ULD_288", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.HandZone.Where(p => p is Minion m && m.HasDeathrattle), "ULD_288e"))
			}));
			cards.Add("ULD_288e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.SetAttack(1), Effects.SetMaxHealth(1), Effects.SetCost(V("ULD_288", "cost", 1)))
			}));

			// [ULD_327] Bazaar Mugger (5) — Rush. Battlecry: Add a random minion from another class to your hand.
			cards.Add("ULD_327", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (Pick(g, OtherClassCards(g, c).Where(x => x.Type == CardType.MINION), 1).FirstOrDefault() is Card card) AddToHand(c, card);
				})
			}));

			// [ULD_328] Clever Disguise (2) — Add 2 random spells from another class to your hand.
			cards.Add("ULD_328", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("ULD_328", "spells", 2); i++)
						if (Pick(g, OtherClassCards(g, c).Where(x => x.Type == CardType.SPELL), 1).FirstOrDefault() is Card card) AddToHand(c, card);
				})
			}));

			// [ULD_715] Plague of Madness (1) — Each player equips a 2/2 Knife with Poisonous.
			cards.Add("ULD_715", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new WeaponTask("ULD_715t"), new WeaponTask("ULD_715t", true))
			}));

			// ---------------------------------------------------------------- Shaman

			// [ULD_158] Sandstorm Elemental (2) — Battlecry: Deal 1 damage to all enemy minions. Overload: (1)
			cards.Add("ULD_158", new CardDef(new Power { PowerTask = new DamageTask(V("ULD_158", "damage", 1), EntityType.OP_MINIONS) }));

			// [ULD_169] Mogu Fleshshaper (9) — Rush. Costs (1) less for each minion on the battlefield.
			cards.Add("ULD_169", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.BoardZone.CountExceptUntouchables + p.Controller.Opponent.BoardZone.CountExceptUntouchables)
			}));

			// [ULD_170] Weaponized Wasp (3) — Battlecry: If you control a Lackey, deal 3 damage.
			cards.Add("ULD_170", new CardDef(BattlecryAny, new Power
			{
				PowerTask = ComplexTask.Create(
					new ConditionTask(EntityType.SOURCE, SelfCondition.IsControllingLackey),
					new FlagTask(true, new DamageTask(V("ULD_170", "damage", 3), EntityType.TARGET)))
			}));

			// [ULD_171] Totemic Surge (0) — Give your Totems +2 Attack.
			cards.Add("ULD_171", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.BoardZone.GetAll().Where(m => m.IsRace(Race.TOTEM)), "ULD_171e"))
			}));
			cards.Add("ULD_171e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("ULD_171", "attack", 2))) }));

			// [ULD_172] Plague of Murlocs (3) — Transform all minions into random Murlocs.
			cards.Add("ULD_172", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var murlocs = Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION && x.IsRace(Race.MURLOC) && !x.NeverRandomlyOnBoard).ToList();
					if (murlocs.Count == 0) return;
					foreach (Minion m in c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).ToList())
						Generic.TransformBlock.Invoke(m.Controller, murlocs[g.Random.Next(murlocs.Count)], m);
				})
			}));

			// [ULD_173] Vessina (4) — While you're Overloaded, your other minions have +2 Attack.
			cards.Add("ULD_173", new CardDef(new Power
			{
				Aura = new Aura(AuraType.BOARD_EXCEPT_SOURCE, "ULD_173e")
				{
					Condition = new SelfCondition(p => p.Controller.OverloadLocked > 0 || p.Controller.OverloadOwed > 0)
				}
			}));
			cards.Add("ULD_173e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("ULD_173", "attack", 2))) }));

			// [ULD_181] Earthquake (7) — Deal 5 damage to all minions, then deal 2 damage to all minions.
			cards.Add("ULD_181", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("ULD_181", "first", 5), EntityType.ALLMINIONS, true),
					new CustomTask((g, c, s, t, stack) => g.DeathProcessingAndAuraUpdate()),
					new DamageTask(V("ULD_181", "then", 2), EntityType.ALLMINIONS, true))
			}));

			// [ULD_276] EVIL Totem (2) — At the end of your turn, add a Lackey to your hand.
			cards.Add("ULD_276", new CardDef(new Power { Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new AddLackeyTask(1) } }));

			// [ULD_413] Splitting Axe (4) — Battlecry: Summon copies of your Totems.
			cards.Add("ULD_413", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion totem in c.BoardZone.GetAll().Where(m => m.IsRace(Race.TOTEM)).ToList())
						Summon(g, c, totem.Card, s);
				})
			}));

			// ---------------------------------------------------------------- Warlock

			// [ULD_160] Sinister Deal (1) — Discover a Lackey.
			cards.Add("ULD_160", new CardDef(new Power
			{
				PowerTask = new DiscoverFromTask((g, c, s) => Cards.All.Where(x => x[GameTag.MARK_OF_EVIL] == 1 && x.Type == CardType.MINION))
			}));

			// [ULD_161] Neferset Thrasher (3) — Whenever this attacks, deal 3 damage to your hero.
			cards.Add("ULD_161", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK) { TriggerSource = TriggerSource.SELF, SingleTask = new DamageTask(V("ULD_161", "damage", 3), EntityType.HERO) }
			}));

			// [ULD_162] EVIL Recruiter (3) — Battlecry: Destroy a friendly Lackey to summon a 5/5 Demon.
			cards.Add("ULD_162", new CardDef(new Dictionary<PlayReq, int>
				{
					{ PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }
				},
				new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(t is Minion lackey) || lackey.Card[GameTag.MARK_OF_EVIL] != 1) return;
						int position = lackey.ZonePosition;
						lackey.Destroy();
						g.DeathProcessingAndAuraUpdate();
						Summon(g, c, Cards.FromId("ULD_162t"), s, System.Math.Min(position, c.BoardZone.Count));
					})
				}));

			// [ULD_163] Expired Merchant (2) — Battlecry: Discard your highest Cost card. Deathrattle: Add 2 copies of it to your hand.
			cards.Add("ULD_163", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var hand = c.HandZone.ToList();
					if (hand.Count == 0) return;
					int top = hand.Max(p => p.Cost);
					var highest = hand.Where(p => p.Cost == top).ToList();
					IPlayable discarded = highest[g.Random.Next(highest.Count)];
					s[GameTag.TAG_SCRIPT_DATA_NUM_1] = discarded.Card.AssetId;
					Generic.DiscardBlock.Invoke(c, discarded);
				}),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					int assetId = s[GameTag.TAG_SCRIPT_DATA_NUM_1];
					if (assetId <= 0) return;
					for (int i = 0; i < V("ULD_163", "copies", 2); i++) AddToHand(c, Cards.FromAssetId(assetId));
				})
			}));

			// [ULD_167] Diseased Vulture (4) — After your hero takes damage on your turn, summon a random 3-Cost minion.
			cards.Add("ULD_167", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentPlayer == c) Summon(g, c, RandomMinionOfCost(g, V("ULD_167", "cost", 3)), s);
					})
				}
			}));

			// [ULD_168] Dark Pharaoh Tekahn (5) — Battlecry: For the rest of the game, your Lackeys are 4/4.
			cards.Add("ULD_168", new CardDef(new Power { PowerTask = new AddEnchantmentTask("ULD_168e", EntityType.CONTROLLER) }));
			cards.Add("ULD_168e", new CardDef(new Power
			{
				Aura = new MultiAura(
					new Aura(AuraType.HAND, "ULD_168e3") { Condition = new SelfCondition(p => p.Card[GameTag.MARK_OF_EVIL] == 1) },
					new Aura(AuraType.BOARD, "ULD_168e3") { Condition = new SelfCondition(p => p.Card[GameTag.MARK_OF_EVIL] == 1) })
			}));
			cards.Add("ULD_168e3", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("ULD_168", "stats", 4))) }));

			// [ULD_324] Impbalming (4) — Destroy a minion. Shuffle 3 Worthless Imps into your deck.
			cards.Add("ULD_324", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(
					new DestroyTask(EntityType.TARGET),
					new CustomTask((g, c, s, t, stack) =>
					{
						for (int i = 0; i < V("ULD_324", "imps", 3); i++)
							Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("ULD_324t")));
					}))
			}));

			// [ULD_717] Plague of Flames (1) — Destroy all your minions. For each one, destroy a random enemy minion.
			cards.Add("ULD_717", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var mine = c.BoardZone.GetAll().ToList();
					var theirs = c.Opponent.BoardZone.GetAll().ToList();
					foreach (Minion m in mine) m.Destroy();
					for (int i = 0; i < mine.Count && theirs.Count > 0; i++)
					{
						Minion victim = theirs[g.Random.Next(theirs.Count)];
						theirs.Remove(victim);
						victim.Destroy();
					}
				})
			}));

			// ---------------------------------------------------------------- Warrior

			// [ULD_195] Frightened Flunky (2) — Taunt. Battlecry: Discover a Taunt minion.
			var flunky = new CardDef(new Power { PowerTask = new DiscoverTask(DiscoverType.TAUNT) });
			cards.Add("ULD_195", flunky);
			cards.Add("CORE_ULD_195", flunky);

			// [ULD_253] Tomb Warden (8) — Taunt. Battlecry: Summon a copy of this minion.
			cards.Add("ULD_253", new CardDef(new Power { PowerTask = new SummonCopyTask(EntityType.SOURCE, side: SummonSide.RIGHT) }));

			// [ULD_256] Into the Fray (1) — Give all Taunt minions in your hand +2/+2.
			cards.Add("ULD_256", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.HandZone.Where(p => p is Minion m && m.HasTaunt), "ULD_256e"))
			}));
			cards.Add("ULD_256e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ULD_256", "stats", 2))) }));

			// [ULD_258] Armagedillo (6) — Taunt. At the end of your turn, give all Taunt minions in your hand +2/+2.
			var armagedillo = new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
						GiveAll(g, s, c.HandZone.Where(p => p is Minion m && m.HasTaunt), "ULD_258e"))
				}
			});
			cards.Add("ULD_258", armagedillo);
			cards.Add("CORE_ULD_258", armagedillo);
			cards.Add("ULD_258e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("ULD_258", "stats", 2))) }));

			// [ULD_707] Plague of Wrath (5) — Destroy all damaged minions.
			var wrath = new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => m.Damage > 0).ToList())
						m.Destroy();
				})
			});
			cards.Add("ULD_707", wrath);
			cards.Add("CORE_ULD_707", wrath);

			// [ULD_708] Livewire Lance (3) — After your Hero attacks, add a Lackey to your hand.
			cards.Add("ULD_708", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = new AddLackeyTask(1) }
			}));

			// [ULD_709] Armored Goon (6) — Whenever your hero attacks, gain 5 Armor.
			cards.Add("ULD_709", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = new ArmorTask(V("ULD_709", "armor", 5)) }
			}));

			// [ULD_720] Bloodsworn Mercenary (3) — Battlecry: Choose a damaged friendly minion. Summon a copy of it.
			cards.Add("ULD_720", new CardDef(new Dictionary<PlayReq, int>
				{
					{ PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_DAMAGED_TARGET, 0 }
				},
				new Power { PowerTask = new SummonCopyTask(EntityType.TARGET, side: SummonSide.RIGHT) }));
		}
	}
}
