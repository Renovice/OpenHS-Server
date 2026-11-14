// HsServer addition (not upstream SabberStone): the United in Stormwind Questlines. The mechanic is shared
// (Tasks/SimpleTasks/QuestlineStageTask: reward, then the next stage as a new card, from QUEST_REWARD_DATABASE_ID); each
// stage card here only says what counts (its trigger) and its reward (its PowerTask). Numbers go through V().
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
		private const GameTag SpellSchool = (GameTag)1635; // client SPELL_SCHOOL: 1 Arcane, 2 Fire, 3 Frost...

		/// <summary>The stage advances by one when <paramref name="counts"/> holds for the card you played (live: after it
		/// resolved — captures/live-20261001-questline).</summary>
		private static Trigger AfterYouPlay(SelfCondition counts) => new Trigger(TriggerType.AFTER_PLAY_CARD)
		{
			TriggerSource = TriggerSource.FRIENDLY, Condition = counts, SingleTask = new QuestProgressTask()
		};

		/// <summary>The stage advances by what <paramref name="amount"/> says for the card you played (0: doesn't count).</summary>
		private static Trigger AfterYouPlay(System.Func<Spell, IPlayable, int> amount) => new Trigger(TriggerType.AFTER_PLAY_CARD)
		{
			TriggerSource = TriggerSource.FRIENDLY,
			SingleTask = new CustomTask((g, c, s, t, stack) =>
			{
				if (s is Spell stage && t != null) QuestProgressTask.Advance(g, c, stage, amount(stage, t));
			})
		};

		private static ISimpleTask Advance(System.Func<Game, Controller, Spell, int> amount) =>
			new CustomTask((g, c, s, t, stack) => { if (s is Spell stage) QuestProgressTask.Advance(g, c, stage, amount(g, c, stage)); });

		/// <summary>The cost a played card had in hand ("1-Cost cards": a discounted card counts at its cost).
		/// (HsServer SPACE: the cost it was paid for when it was played — 0 included, HsTag.PLAYED_COST_PLUS1.)</summary>
		private static int PlayedCost(IPlayable p) =>
			p[HsTag.PLAYED_COST_PLUS1] > 0 ? p[HsTag.PLAYED_COST_PLUS1] - 1 :
			p[GameTag.TAG_LAST_KNOWN_COST_IN_HAND] > 0 ? p[GameTag.TAG_LAST_KNOWN_COST_IN_HAND] : p.Cost;

		/// <summary>Discover a card from your deck and draw it (Seek Guidance).</summary>
		private static ISimpleTask DiscoverFromYourDeck() =>
			new CustomTask((g, c, s, t, stack) => Generic.DiscoverFromDeck(c, s, Generic.PickFromDeck(c, null)));

		private static void Questlines(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Shaman: Command the Elements
			// Play 3 / 3 / 2 cards with Overload. Rewards: unlock your Overloaded Mana Crystals; summon a 3/3 Taunt Elemental;
			// Stormcaller Bru'kan (WildPoolCardsGen).
			var overload = new SelfCondition(p => p.Card.HasOverload);
			cards.Add("SW_031", new CardDef(new Power
			{
				Trigger = AfterYouPlay(overload),
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.UnlockOverload(c))
			}));
			cards.Add("SW_031t", new CardDef(new Power { Trigger = AfterYouPlay(overload), PowerTask = new SummonTask("SW_031t8", SummonSide.SPELL) }));
			cards.Add("SW_031t2", new CardDef(new Power { Trigger = AfterYouPlay(overload) }));

			// ---------------------------------------------------------------- Warrior: Raid the Docks
			// Play 3 Pirates (each stage). Rewards: draw a weapon; deal 2 damage to a random enemy twice; Cap'n Rokara.
			var pirate = SelfCondition.IsRace(Race.PIRATE);
			cards.Add("SW_028", new CardDef(new Power { Trigger = AfterYouPlay(pirate), PowerTask = ComplexTask.DrawFromDeck(1, SelfCondition.IsWeapon) }));
			cards.Add("SW_028t", new CardDef(new Power
			{
				Trigger = AfterYouPlay(pirate),
				PowerTask = new EnqueueTask(V("SW_028t", "shots", 2), ComplexTask.Create(
					new RandomTask(1, EntityType.ENEMIES, harmful: true), new DamageTask(V("SW_028t", "damage", 2), EntityType.STACK, true)))
			}));
			cards.Add("SW_028t2", new CardDef(new Power { Trigger = AfterYouPlay(pirate) }));
			// [SW_028t5] Cap'n Rokara (5) — Battlecry: Summon The Juggernaut!
			cards.Add("SW_028t5", new CardDef(new Power { PowerTask = new SummonTask("SW_028t6", SummonSide.RIGHT) }));
			// [SW_028t6] The Juggernaut — Start of Your Turn: Summon a Pirate, equip a Warrior weapon, and fire two cannons
			// that deal 2 damage!
			cards.Add("SW_028t6", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var pirates = Pick(g, Cards.All.Where(x => x.Collectible && x.Type == CardType.MINION && x.IsRace(Race.PIRATE)), 1);
						if (pirates.Count == 1 && !c.BoardZone.IsFull) Summon(g, c, pirates[0], s);
						var weapons = Pick(g, Cards.All.Where(x => x.Collectible && x.Type == CardType.WEAPON && x.HasClass(CardClass.WARRIOR)), 1);
						if (weapons.Count == 1) new WeaponTask(weapons[0].Id).Process(g, c, s, null);
						for (int i = 0; i < V("SW_028t6", "cannons", 2); i++)
						{
							var targets = c.Opponent.BoardZone.Where(m => !m.IsDead && !m.ToBeDestroyed).Cast<ICharacter>().Append(c.Opponent.Hero).ToList();
							Generic.DamageCharFunc((IPlayable)s, targets[g.Random.Next(targets.Count)], V("SW_028t6", "damage", 2), false);
						}
					})
				}
			}));

			// ---------------------------------------------------------------- Paladin: Rise to the Occasion
			// Play 3 different 1-Cost cards (each stage). Rewards: equip a 1/4 Light's Justice; upgrade your Hero Power (the
			// card data's upgraded version, tag 1086); Lightborn Cariel.
			int Different1Cost(Spell stage, IPlayable p) =>
				PlayedCost(p) == V("SW_313", "cost", 1) && QuestProgressTask.CountOnce(stage, p.Card.AssetId) ? 1 : 0;
			cards.Add("SW_313", new CardDef(new Power { Trigger = AfterYouPlay(Different1Cost), PowerTask = new WeaponTask("CS2_091") }));
			cards.Add("SW_313t", new CardDef(new Power
			{
				Trigger = AfterYouPlay(Different1Cost),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int upgraded = c.Hero.HeroPower?.Card[HsTag.UPGRADED_HERO_POWER] ?? 0;
					if (upgraded > 0 && Cards.FromAssetId(upgraded) is Card next)
						new ReplaceHeroPower(next).Process(g, c, s, null);
				})
			}));
			cards.Add("SW_313t2", new CardDef(new Power { Trigger = AfterYouPlay(Different1Cost) }));
			// [SW_313t4] Lightborn Cariel (5) — Battlecry: For the rest of the game, your Silver Hand Recruits have +2/+2.
			cards.Add("SW_313t4", new CardDef(new Power { PowerTask = new AddEnchantmentTask("SW_313t4e", EntityType.CONTROLLER) }));
			cards.Add("SW_313t4e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.BOARD, "SW_313t4ee") { Condition = new SelfCondition(p => p.Card.Id == "CS2_101t") }
			}));
			cards.Add("SW_313t4ee", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.ATK, EffectOperator.ADD, V("SW_313t4", "attack", 2)),
					new Effect(GameTag.HEALTH, EffectOperator.ADD, V("SW_313t4", "health", 2)))
			}));

			// ---------------------------------------------------------------- Hunter: Defend the Dwarven District
			// Deal damage with 3 / 2 / 2 spells (a spell counts once). Rewards: your Hero Power can target minions; it costs 0
			// (Practiced: Costs (2) less); Tavish, Master Marksman.
			Trigger SpellDamage() => new Trigger(TriggerType.DEAL_DAMAGE)
			{
				TriggerSource = TriggerSource.FRIENDLY,
				SingleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (s is Spell stage && g.CurrentEventData?.EventSource is Spell cast && cast.Controller == c &&
					    QuestProgressTask.CountOnce(stage, cast.Id))
						QuestProgressTask.Advance(g, c, stage, 1);
				})
			};
			cards.Add("SW_322", new CardDef(new Power { Trigger = SpellDamage(), PowerTask = new AddEnchantmentTask("SW_322e3", EntityType.CONTROLLER) }));
			cards.Add("SW_322e3", new CardDef(new Power { Aura = new Aura(AuraType.CONTROLLER, new Effect(GameTag.STEADY_SHOT_CAN_TARGET, EffectOperator.SET, 1)) }));
			cards.Add("SW_322t", new CardDef(new Power { Trigger = SpellDamage(), PowerTask = new AddEnchantmentTask("SW_322e", EntityType.HERO_POWER) }));
			cards.Add("SW_322t2", new CardDef(new Power { Trigger = SpellDamage() }));
			// [SW_322t4] Tavish, Master Marksman (5) — Battlecry: For the rest of the game, spells you cast refresh your Hero Power.
			cards.Add("SW_322t4", new CardDef(new Power { PowerTask = new AddEnchantmentTask("SW_322e2", EntityType.CONTROLLER) }));
			cards.Add("SW_322e2", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (c.Hero.HeroPower != null) c.Hero.HeroPower.IsExhausted = false; })
				}
			}));

			// ---------------------------------------------------------------- Warlock: The Demon Seed
			// Take 12 damage on your turns (each stage). Rewards: Lifesteal, deal 3 damage to the enemy hero (twice);
			// Blightborn Tamsin.
			Trigger DamageOnYourTurn() => new Trigger(TriggerType.TAKE_DAMAGE)
			{
				TriggerSource = TriggerSource.HERO,
				SingleTask = Advance((g, c, stage) => g.CurrentPlayer == c ? g.CurrentEventData?.EventNumber ?? 0 : 0)
			};
			cards.Add("SW_091", new CardDef(new Power { Trigger = DamageOnYourTurn(), PowerTask = new DamageTask(V("SW_091", "damage", 3), EntityType.OP_HERO, true) }));
			cards.Add("SW_091t", new CardDef(new Power { Trigger = DamageOnYourTurn(), PowerTask = new DamageTask(V("SW_091t", "damage", 3), EntityType.OP_HERO, true) }));
			cards.Add("SW_091t3", new CardDef(new Power { Trigger = DamageOnYourTurn() }));
			// [SW_091t4] Blightborn Tamsin (5) — Battlecry: For the rest of the game, damage you take on your turn damages your
			// opponent instead.
			cards.Add("SW_091t4", new CardDef(new Power { PowerTask = new AddEnchantmentTask("SW_091t5", EntityType.CONTROLLER) }));
			cards.Add("SW_091t5", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PREDAMAGE)
				{
					TriggerSource = TriggerSource.HERO,
					FastExecution = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentPlayer != c || g.CurrentEventData == null || c.Hero[HsTag.TAKING_REDIRECTED_DAMAGE] > 0) return;
						int amount = g.CurrentEventData.EventNumber;
						if (amount <= 0) return;
						g.CurrentEventData.EventNumber = 0;
						Hero enemy = c.Opponent.Hero;
						enemy[HsTag.TAKING_REDIRECTED_DAMAGE] = 1;
						enemy.TakeDamage(g.CurrentEventData.EventSource, amount);
						enemy[HsTag.TAKING_REDIRECTED_DAMAGE] = 0;
					})
				}
			}));

			// ---------------------------------------------------------------- Mage: Sorcerer's Gambit
			// Cast a Fire, Frost and Arcane spell (each stage). Rewards: draw a spell; Discover one; Arcanist Dawngrasp.
			int[] schools = { 2, 3, 1 }; // Fire, Frost, Arcane
			int NewSchool(Spell stage, IPlayable p) =>
				p is Spell && schools.Contains(p.Card[SpellSchool]) && QuestProgressTask.CountOnce(stage, p.Card[SpellSchool]) ? 1 : 0;
			cards.Add("SW_450", new CardDef(new Power { Trigger = AfterYouPlay(NewSchool), PowerTask = ComplexTask.DrawFromDeck(1, SelfCondition.IsSpell) }));
			cards.Add("SW_450t", new CardDef(new Power
			{
				Trigger = AfterYouPlay(NewSchool),
				PowerTask = new DiscoverFromTask((g, c, s) => Cards.All.Where(x => x.Collectible && x.Type == CardType.SPELL &&
					(x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL)) && schools.Contains(x[SpellSchool])))
			}));
			cards.Add("SW_450t2", new CardDef(new Power { Trigger = AfterYouPlay(NewSchool) }));
			// [SW_450t4] Arcanist Dawngrasp (5) — Battlecry: For the rest of the game, you have Spell Damage +3.
			cards.Add("SW_450t4", new CardDef(new Power { PowerTask = new AddEnchantmentTask("SW_450t4e", EntityType.CONTROLLER) }));
			cards.Add("SW_450t4e", new CardDef(new Power { Enchant = new Enchant(GameTag.SPELLPOWER, EffectOperator.ADD, V("SW_450t4", "spell damage", 3)) }));

			// ---------------------------------------------------------------- Rogue: Find the Imposter
			// Play 2 SI:7 cards (each stage; "SI:7" is in the card's name, no tag marks them). Rewards: add a Spy Gizmo (twice);
			// Spymaster Scabbs (one of each).
			var si7 = new SelfCondition(p => p.Card.Name != null && p.Card.Name.Contains("SI:7"));
			string[] gizmos = { "SW_052t4", "SW_052t5", "SW_052t6", "SW_052t7", "SW_052t8_t" };
			ISimpleTask AddGizmo() => new CustomTask((g, c, s, t, stack) =>
				Generic.AddHandPhase.Invoke(c, Entity.FromCard(c, Cards.FromId(gizmos[g.Random.Next(gizmos.Length)]), new EntityData { [GameTag.CREATOR] = s.Id })));
			cards.Add("SW_052", new CardDef(new Power { Trigger = AfterYouPlay(si7), PowerTask = AddGizmo() }));
			cards.Add("SW_052t", new CardDef(new Power { Trigger = AfterYouPlay(si7), PowerTask = AddGizmo() }));
			cards.Add("SW_052t2", new CardDef(new Power { Trigger = AfterYouPlay(si7) }));
			// [SW_052t3] Spymaster Scabbs (5) — Battlecry: Add one of each Spy Gizmo to your hand.
			cards.Add("SW_052t3", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (string id in gizmos)
						Generic.AddHandPhase.Invoke(c, Entity.FromCard(c, Cards.FromId(id), new EntityData { [GameTag.CREATOR] = s.Id }));
				})
			}));
			// [SW_052t4] Fizzflash Distractor (1) — Return an enemy minion to its owner's hand. They can't play it next turn.
			cards.Add("SW_052t4", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				// (CATACLYSM review: the shared hand lock, Generic.LockInHand — Daze's; the enchantment only shows it)
				PowerTask = ComplexTask.Create(new ReturnHandTask(EntityType.TARGET, true), new CustomTask((g, c, s, t, stack) =>
				{
					if (t?.Zone?.Type == Zone.HAND) Generic.LockInHand(t, s, g.Turn + 1, "SW_052t4e");
				}))
			}));
			cards.Add("SW_052t4e", new CardDef(new Power
			{
				Enchant = new Enchant(HsTag.LITERALLY_UNPLAYABLE, EffectOperator.SET, 1),
				// gone at the end of its owner's next turn
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					EitherTurn = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Enchantment e && e.Target is IPlayable card && g.CurrentPlayer == card.Controller) e.Remove();
					})
				}
			}));
			// [SW_052t5] Spy-o-matic (1) — Battlecry: Look at 3 cards in your opponent's deck. Pick one to put on top.
			cards.Add("SW_052t5", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var offer = Pick(g, c.Opponent.DeckZone.Select(p => p.Card).Distinct(), 3);
					if (offer.Count == 0) return;
					Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.STACK, offer.ToArray(),
						new CustomTask((g2, c2, s2, chosen, stack2) =>
						{
							if (chosen == null || !(c2.Opponent.DeckZone.FirstOrDefault(p => p.Card.Id == chosen.Card.Id) is IPlayable card)) return;
							c2.Opponent.DeckZone.Remove(card);
							c2.Opponent.DeckZone.Add(card); // the top
						}));
				})
			}));
			// [SW_052t6] Noggen-Fog Generator (1) — Give a minion +2 Attack and Stealth.
			cards.Add("SW_052t6", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } }, new Power
			{
				PowerTask = new AddEnchantmentTask("SW_052t6e", EntityType.TARGET)
			}));
			cards.Add("SW_052t6e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.ATK, EffectOperator.ADD, V("SW_052t6", "attack", 2)), new Effect(GameTag.STEALTH, EffectOperator.SET, 1))
			}));
			// [SW_052t7] Hidden Gyroblade (1, 3/2 weapon) — Deathrattle: Throw this at a random enemy minion.
			cards.Add("SW_052t7", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = c.Opponent.BoardZone.Where(m => !m.IsDead && !m.ToBeDestroyed).ToList();
					if (enemies.Count > 0 && s is IPlayable blade)
						Generic.DamageCharFunc(blade, enemies[g.Random.Next(enemies.Count)], blade.Card.ATK, false);
				})
			}));
			// [SW_052t8_t] Undercover Mole (1) — Stealth. After this attacks, add a random card to your hand (from your
			// opponent's class).
			cards.Add("SW_052t8_t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var pick = Pick(g, Cards.All.Where(x => x.Collectible && x.HasClass(c.Opponent.HeroClass)), 1);
						if (pick.Count == 1)
							Generic.AddHandPhase.Invoke(c, Entity.FromCard(c, pick[0], new EntityData { [GameTag.CREATOR] = s.Id }));
					})
				}
			}));

			// ---------------------------------------------------------------- Priest: Seek Guidance
			// Play a 2, 3 and 4-Cost card; then a 5 and 6; then a 7 and 8. Rewards: Discover a card from your deck (twice);
			// Xyrella, the Sanctified.
			System.Func<Spell, IPlayable, int> Costs(string id, params int[] costs) => (stage, p) =>
			{
				int cost = PlayedCost(p);
				return costs.Select((x, i) => V(id, "cost " + (i + 1), x)).Contains(cost) && QuestProgressTask.CountOnce(stage, cost) ? 1 : 0;
			};
			cards.Add("SW_433", new CardDef(new Power { Trigger = AfterYouPlay(Costs("SW_433", 2, 3, 4)), PowerTask = DiscoverFromYourDeck() }));
			cards.Add("SW_433t", new CardDef(new Power { Trigger = AfterYouPlay(Costs("SW_433t", 5, 6)), PowerTask = DiscoverFromYourDeck() }));
			cards.Add("SW_433t2", new CardDef(new Power { Trigger = AfterYouPlay(Costs("SW_433t2", 7, 8)) }));
			// [SW_433t3] Xyrella, the Sanctified (5) — Taunt. Battlecry: Shuffle the Purified Shard into your deck.
			cards.Add("SW_433t3", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("SW_433t3a"), new EntityData { [GameTag.CREATOR] = s.Id })))
			}));
			// [SW_433t3a] Purified Shard (10) — Destroy the enemy hero.
			cards.Add("SW_433t3a", new CardDef(new Power { PowerTask = new DestroyTask(EntityType.OP_HERO) }));

			// ---------------------------------------------------------------- Demon Hunter: Final Showdown
			// Draw 4 / 5 / 5 cards in one turn: +1 per draw while the stage is up, back to 0 at the end of each turn. Rewards:
			// the cards drawn (the ones this stage counted) cost (1) less (twice); Demonslayer Kurtrus.
			Trigger DrawsThisTurn() => new MultiTrigger(
				new Trigger(TriggerType.DRAW)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Spell stage) || t == null) return;
						QuestProgressTask.CountOnce(stage, t.Id); // remembered: "the cards drawn"
						QuestProgressTask.Advance(g, c, stage, 1);
					})
				},
				new Trigger(TriggerType.TURN_END)
				{
					EitherTurn = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Spell stage) || stage.QuestProgress == 0) return;
						stage.QuestProgress = 0;
						foreach (IPlayable p in c.HandZone.ToList()) stage[(GameTag)((int)HsTag.QUEST_COUNTED + p.Id)] = 0;
					})
				});
			ISimpleTask DrawnCostLess(string enchantment) => new CustomTask((g, c, s, t, stack) =>
			{
				foreach (IPlayable drawn in c.HandZone.Where(p => s[(GameTag)((int)HsTag.QUEST_COUNTED + p.Id)] > 0).ToList())
					new AddEnchantmentTask(enchantment, EntityType.TARGET).Process(g, c, s, drawn);
			});
			cards.Add("SW_039", new CardDef(new Power { Trigger = DrawsThisTurn(), PowerTask = DrawnCostLess("SW_039te") }));
			cards.Add("SW_039t", new CardDef(new Power { Trigger = DrawsThisTurn(), PowerTask = DrawnCostLess("SW_039te") }));
			cards.Add("SW_039t3", new CardDef(new Power { Trigger = DrawsThisTurn() }));
			cards.Add("SW_039te", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("SW_039", "discount", 1))) }));
			// [SW_039t3_t] Demonslayer Kurtrus (5) — Battlecry: For the rest of the game, cards you draw cost (2) less. (No
			// player enchantment in the client data: the player carries DRAWN_CARD_ENCHANTMENT = Ludicrous Speed.)
			cards.Add("SW_039t3_t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => c[HsTag.DRAWN_CARD_ENCHANTMENT] = Cards.FromId("SW_039t3_te").AssetId)
			}));
			cards.Add("SW_039t3_te", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("SW_039t3_t", "discount", 2))) }));

			// ---------------------------------------------------------------- Druid: Lost in the Park
			// Gain 4 / 5 / 6 Attack with your hero (Trigger HERO_ATTACK). Rewards: gain 5 Armor; gain 5 Armor and draw a card;
			// Guff the Tough.
			Trigger HeroAttackGained() => new Trigger(TriggerType.HERO_ATTACK)
			{
				TriggerSource = TriggerSource.HERO,
				SingleTask = Advance((g, c, stage) => g.CurrentEventData?.EventNumber ?? 0)
			};
			cards.Add("SW_428", new CardDef(new Power { Trigger = HeroAttackGained(), PowerTask = new ArmorTask(V("SW_428", "armor", 5)) }));
			cards.Add("SW_428t", new CardDef(new Power
			{
				Trigger = HeroAttackGained(),
				PowerTask = ComplexTask.Create(new ArmorTask(V("SW_428t", "armor", 5)), new DrawTask())
			}));
			cards.Add("SW_428t2", new CardDef(new Power { Trigger = HeroAttackGained() }));
			// [SW_428t4] Guff the Tough (5) — Taunt. Battlecry: Give your hero +8 Attack this turn. Gain 8 Armor.
			cards.Add("SW_428t4", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("SW_428t4e", EntityType.HERO), new ArmorTask(V("SW_428t4", "armor", 8)))
			}));
			cards.Add("SW_428t4e", new CardDef(new Power
			{
				Enchant = new Enchant(GameTag.ATK, EffectOperator.ADD, V("SW_428t4", "attack", 8)) { IsOneTurnEffect = true }
			}));
		}
	}
}
