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
	/// <summary>
	/// HsServer: the Corpse spenders that were placeholders (effect audit 2026-10-04, RESEARCH/vfx/audit_subspells.md).
	/// Every spend goes through Generic.SpendCorpses (the card's recorded Corpse effect plays around it); a fixed spend on
	/// play is the card's Power.CorpseCost (its hand glow). "Costs Corpses instead of Mana" is the client tag
	/// CARD_ALTERNATE_COST = 3 (Playable.CostsCorpses). Not here: Blood Doctor Thal'ena (a second Hero Power: the engine
	/// has none yet).
	/// </summary>
	public static partial class CurrentCoreCardsGen
	{
		private static void CorpseCards(IDictionary<string, CardDef> cards)
		{
			// [CATA_465] Chow Down (8) — Summon five 5/4 Undead Drakes. Spend 8 Corpses to give them Rush.
			cards.Add("CATA_465", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				CorpseCost = V("CATA_465", "corpses", 8),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					// in the order of the text: summon, then the spend gives them Rush (no client enchantment: the tag)
					var drakes = new List<Minion>();
					for (int i = 0; i < V("CATA_465", "drakes", 5) && !c.BoardZone.IsFull; i++)
					{
						var drake = (Minion)Entity.FromCard(c, Cards.FromId("CATA_465t"));
						Generic.SummonBlock.Invoke(g, drake, -1, s);
						drakes.Add(drake);
					}
					if (Generic.SpendCorpses(c, s))
						foreach (Minion drake in drakes.Where(d => d.Zone?.Type == Zone.PLAY))
							drake[GameTag.RUSH] = 1;
				})
			}));

			// [DINO_416] Hollow Direhorn (5) — Rush. After a friendly minion dies, spend 3 Corpses to gain Reborn.
			cards.Add("DINO_416", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = SelfCondition.IsMinion,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						// a minion that already has Reborn keeps its Corpses (UNCLEAR live)
						if (s is Minion me && me[GameTag.REBORN] == 0 && Generic.SpendCorpses(c, s, V("DINO_416", "corpses", 3)))
							me[GameTag.REBORN] = 1;
					})
				}
			}));

			// [EDR_813] Morbid Swarm (1) — Choose One - Summon two 1/1 Ants; or Spend 2 Corpses to deal 4 damage to a minion.
			cards.Add("EDR_813", new CardDef(new Power()));
			cards.Add("EDR_813a", new CardDef(new Power { PowerTask = new SummonTask("EDR_813at", V("EDR_813a", "ants", 2)) }));
			cards.Add("EDR_813b", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => stack.Flag = Generic.SpendCorpses(c, s, V("EDR_813b", "corpses", 2))),
					new FlagTask(true, new DamageTask(V("EDR_813b", "damage", 4), EntityType.TARGET, true)))
			}));

			// [EDR_815] Corpse Flower (3) — After your opponent summons a minion, spend 2 Corpses to deal 3 damage to it.
			cards.Add("EDR_815", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t is Minion m && !m.IsDead && !m.ToBeDestroyed && Generic.SpendCorpses(c, s, V("EDR_815", "corpses", 2)))
							Generic.DamageCharFunc((IPlayable)s, m, V("EDR_815", "damage", 3), false);
					})
				}
			}));

			// [END_005] Bygone Echoes (5) — Summon a random 4-Cost minion. Spend 4 Corpses to summon another. Outcast: And another.
			// (Outcast's "another" belongs to the Corpse clause: with the Corpses spent, two more; UNCLEAR live)
			cards.Add("END_005", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				CorpseCost = V("END_005", "corpses", 4),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					// in the order of the text: one, then the spend, then another (and another with Outcast)
					int cost = V("END_005", "cost", 4);
					void SummonOne()
					{
						if (!c.BoardZone.IsFull && RandomMinionOfCost(g, cost) is Card card)
							Generic.SummonBlock.Invoke(g, (Minion)Entity.FromCard(c, card), -1, s);
					}
					SummonOne();
					if (Generic.SpendCorpses(c, s))
						for (int i = s is IPlayable p && c.OutcastPlayId == p.Id ? 2 : 1; i > 0; i--)
							SummonOne();
				})
			}));

			// [ETC_428] Boneshredder (5) — Battlecry: Spend 5 Corpses to trigger and gain the Deathrattle of a random friendly
			// minion that died this game.
			cards.Add("ETC_428", new CardDef(new Power
			{
				CorpseCost = V("ETC_428", "corpses", 5),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var died = c.DiedThisGameCards.Where(p => p is Minion && p.Card.Power?.DeathrattleTask != null).ToList();
					if (died.Count == 0 || !Generic.SpendCorpses(c, s)) return;
					Card copied = died[g.Random.Next(died.Count)].Card;
					copied.Power.DeathrattleTask.Process(g, c, s, null);
					s[GameTag.TAG_SCRIPT_DATA_NUM_2] = copied.AssetId;
					Generic.AddEnchantmentBlock(g, Cards.FromId("ETC_428e"), (IPlayable)s, (IPlayable)s, 0, 0, 0);
				})
			}));
			cards.Add("ETC_428e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					int assetId = s[GameTag.TAG_SCRIPT_DATA_NUM_2];
					if (assetId > 0) Cards.FromAssetId(assetId).Power?.DeathrattleTask?.Process(g, c, s, null);
				})
			}));

			// [ETC_533] Mosh Pit (2, Location) — Spend 3 Corpses to give a friendly minion Reborn.
			cards.Add("ETC_533", new CardDef(LocationFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m && Generic.SpendCorpses(c, s, V("ETC_533", "corpses", 3))) m[GameTag.REBORN] = 1;
				})
			}));

			// [FIR_951] Volcoross (8) — Rush, Taunt. Battlecry: Choose to spend 10, 20, or 30 Corpses to gain that many stats.
			// (the choices are its three spells; only the ones the Corpses pay for are offered)
			cards.Add("FIR_951", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var options = new[] { "FIR_951t2", "FIR_951t3", "FIR_951t4" }
						.Where(id => c.Corpses >= VolcorossCorpses(id)).Select(Cards.FromId).ToArray();
					if (options.Length == 0) return;
					Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.STACK, options,
						new CustomTask((g2, c2, s2, chosen, st) =>
						{
							if (chosen == null) return;
							int n = VolcorossCorpses(chosen.Card.Id);
							if (Generic.SpendCorpses(c2, s, n)) // s: Volcoross itself (the choice's source is the choice)
								Generic.AddEnchantmentBlock(g2, Cards.FromId("FIR_951e"), (IPlayable)s, (IPlayable)s, n, n, 0);
						}));
				})
			}));
			foreach (string option in new[] { "FIR_951t2", "FIR_951t3", "FIR_951t4" })
				cards.Add(option, EngineOnly); // choices Volcoross resolves (never cast)
			cards.Add("FIR_951e", new CardDef(new Power
			{
				Enchant = Enchants.Enchants.AddAttackHealthScriptTag // +N/+N (N = the Corpses spent)
			}));

			// [GDB_113] Airlock Breach (6) — Summon a 5/5 Undead with Taunt and give your hero +5 Health. Spend 5 Corpses to do
			// it again.
			cards.Add("GDB_113", new CardDef(new Power
			{
				CorpseCost = V("GDB_113", "corpses", 5),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					// in the order of the text: do it, the spend, then do it again
					void DoIt()
					{
						if (!c.BoardZone.IsFull) Generic.SummonBlock.Invoke(g, (Minion)Entity.FromCard(c, Cards.FromId("GDB_113t")), -1, s);
						Generic.AddEnchantmentBlock(g, Cards.FromId("GDB_113e"), (IPlayable)s, c.Hero, 0, 0, 0);
					}
					DoIt();
					if (Generic.SpendCorpses(c, s)) DoIt();
				})
			}));
			cards.Add("GDB_113e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("GDB_113e", "health", 5))) }));

			// [GDB_470] Exarch Maladaar (6) — Battlecry: The next card you play this turn costs Corpses instead of Mana.
			cards.Add("GDB_470", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => c[HsTag.NEXT_CARD_COSTS_CORPSES] = g.Turn)
			}));

			// [JAIL_451] Blood Clone (3) — Discover a 5-Cost minion. Spend 5 Corpses to summon a copy of it.
			cards.Add("JAIL_451", new CardDef(new Power
			{
				CorpseCost = V("JAIL_451", "corpses", 5),
				PowerTask = new DiscoverTask(CardType.MINION, CardClass.INVALID, (GameTag.COST, RelaSign.EQ, V("JAIL_451", "cost", 5)),
					ChoiceAction.HAND, new CustomTask((g, c, s, t, stack) =>
					{
						if (t != null && !c.BoardZone.IsFull && Generic.SpendCorpses(c, s))
							Generic.SummonBlock.Invoke(g, (Minion)Entity.FromCard(c, t.Card), -1, s);
					}))
			}));

			// [RLK_085] Lord Marrowgar (8) — Battlecry: Raise ALL of your Corpses as 1/1 Risen Golems with Rush. For each that
			// can't fit, give one +2/+2.
			cards.Add("RLK_085", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int all = Generic.SpendCorpsesUpTo(c, s, c.Corpses);
					var golems = new List<Minion>();
					for (int i = 0; i < all && !c.BoardZone.IsFull; i++)
					{
						var golem = (Minion)Entity.FromCard(c, Cards.FromId("RLK_085t"));
						Generic.SummonBlock.Invoke(g, golem, -1, s);
						if (golem.Zone == c.BoardZone) golems.Add(golem);
					}
					if (golems.Count == 0) golems = c.BoardZone.GetAll().Where(m => m.Card.Id == "RLK_085t").ToList();
					for (int extra = all - golems.Count(x => x.Zone == c.BoardZone); extra > 0 && golems.Count > 0; extra--)
						Generic.AddEnchantmentBlock(g, Cards.FromId("RLK_085e"), (IPlayable)s, golems[g.Random.Next(golems.Count)], 0, 0, 0);
				})
			}));
			cards.Add("RLK_085e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("RLK_085e", "stats", 2))) }));

			// [RLK_101] Defrost (2) — Draw a card. Spend 2 Corpses to draw another.
			cards.Add("RLK_101", new CardDef(new Power
			{
				CorpseCost = V("RLK_101", "corpses", 2),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Generic.Draw(c); // in the order of the text: draw, the spend, then draw another
					if (Generic.SpendCorpses(c, s)) Generic.Draw(c);
				})
			}));

			// [RLK_226] Ymirjar Deathbringer (3) — Taunt. Deathrattle: Spend 3 Corpses to summon a 3/3 Risen Ymirjar with Taunt.
			cards.Add("RLK_226", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!c.BoardZone.IsFull && Generic.SpendCorpses(c, s, V("RLK_226", "corpses", 3)))
						Generic.SummonBlock.Invoke(g, (Minion)Entity.FromCard(c, Cards.FromId("RLK_226t")), -1, s);
				})
			}));

			// [RLK_715] Runeforging (1) — Draw a weapon. Spend a Corpse to reduce its Cost by (1).
			cards.Add("RLK_715", new CardDef(new Power
			{
				CorpseCost = V("RLK_715", "corpses", 1),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var weapons = c.DeckZone.Where(p => p is Weapon).ToList();
					if (weapons.Count == 0) return;
					IPlayable drawn = Generic.Draw(c, weapons[g.Random.Next(weapons.Count)]);
					if (drawn?.Zone?.Type == Zone.HAND && Generic.SpendCorpses(c, s))
						Generic.AddEnchantmentBlock(g, Cards.FromId("RLK_715e"), (IPlayable)s, drawn, 0, 0, 0);
				})
			}));
			cards.Add("RLK_715e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("RLK_715e", "discount", 1))) }));

			// [RLK_753] Bonedigger Geist (2) — Battlecry: Spend a Corpse to gain +1/+2.
			cards.Add("RLK_753", new CardDef(new Power
			{
				CorpseCost = V("RLK_753", "corpses", 1),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (Generic.SpendCorpses(c, s))
						Generic.AddEnchantmentBlock(g, Cards.FromId("RLK_753e"), (IPlayable)s, (IPlayable)s, 0, 0, 0);
				})
			}));
			cards.Add("RLK_753e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("RLK_753e", "attack", 1)), Effects.Health_N(V("RLK_753e", "health", 2)))
			}));

			// [MIS_101] Foamrender (5, weapon) — Whenever your hero attacks, spend 3 Corpses to gain +1 Durability.
			cards.Add("MIS_101", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (Generic.SpendCorpses(c, s, V("MIS_101", "corpses", 3)))
							Generic.AddEnchantmentBlock(g, Cards.FromId("MIS_101e"), (IPlayable)s, (IPlayable)s, 0, 0, 0);
					})
				}
			}));
			cards.Add("MIS_101e", new CardDef(new Power { Enchant = new Enchant(GameTag.DURABILITY, EffectOperator.ADD, V("MIS_101e", "durability", 1)) }));

			// [RLK_731] Darkfallen Neophyte (3) — Battlecry: Spend 2 Corpses to give all minions in your hand +2 Attack.
			cards.Add("RLK_731", new CardDef(new Power
			{
				CorpseCost = V("RLK_731", "corpses", 2),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (Generic.SpendCorpses(c, s)) GiveAll(g, s, c.HandZone.Where(p => Generic.IsMinionCard(p)), "RLK_731e");
				})
			}));
			cards.Add("RLK_731e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("RLK_731e", "attack", 2))) }));

			// [TLC_434] Paleomancy (3) — Discover an Undead. Spend 5 Corpses to keep all 3 instead.
			cards.Add("TLC_434", new CardDef(new Power
			{
				CorpseCost = V("TLC_434", "corpses", 5),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var undead = Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION && x.IsRace(Race.UNDEAD)
						&& (x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL))).ToList();
					List<Card> offer = Pick(g, undead, 3);
					if (offer.Count == 0) return;
					if (Generic.SpendCorpses(c, s))
						foreach (Card card in offer) AddToHand(c, card);
					else if (!c.HandZone.IsFull)
						Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.HAND, offer.ToArray(), null);
				})
			}));

			// [TLC_436] Reanimated Pterrordax (5) — Rush, Lifesteal. Costs Corpses instead of Mana (client CARD_ALTERNATE_COST).
			cards.Add("TLC_436", new CardDef(new Power { NoScript = true })); // (its Corpse cost is client data: Playable.CostsCorpses)

			// [TOY_827] Shambling Zombietank (2) — Taunt. Battlecry: Spend 5 Corpses to summon a copy of this.
			cards.Add("TOY_827", new CardDef(new Power
			{
				CorpseCost = V("TOY_827", "corpses", 5),
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => stack.Flag = !c.BoardZone.IsFull && Generic.SpendCorpses(c, s)),
					new FlagTask(true, new SummonCopyTask(EntityType.SOURCE, side: SummonSide.RIGHT)))
			}));

			// [VAC_427] Corpsicle (2) — Deal 3 damage. Spend 3 Corpses to return this to your hand at the end of your turn.
			// (a new Corpsicle comes back: effect-generated cards are new entities)
			cards.Add("VAC_427", new CardDef(TargetAny, new Power
			{
				CorpseCost = V("VAC_427", "corpses", 3),
				PowerTask = ComplexTask.Create(
					new DamageTask(V("VAC_427", "damage", 3), EntityType.TARGET, true),
					new CustomTask((g, c, s, t, stack) =>
					{
						if (Generic.SpendCorpses(c, s))
							Generic.AddEnchantmentBlock(g, Cards.FromId("VAC_427e"), (IPlayable)s, c.Hero, 0, 0, 0);
					}))
			}));
			cards.Add("VAC_427e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = ComplexTask.Create(
						new CustomTask((g, c, s, t, stack) => AddToHand(c, Cards.FromId("VAC_427"))),
						RemoveEnchantmentTask.Task)
				}
			}));

			// [WORK_070] Soul Searching (2) — Discover a card from your deck. Spend 5 Corpses to copy it.
			cards.Add("WORK_070", new CardDef(new Power
			{
				CorpseCost = V("WORK_070", "corpses", 5),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Generic.DiscoverFromDeck(c, s, Generic.PickFromDeck(c, p => true), (g2, c2, s2, deck) =>
					{
						Generic.Draw(c2, deck);
						if (Generic.SpendCorpses(c2, s2)) AddToHand(c2, deck.Card);
					});
				})
			}));

			// [WW_357] Maw and Paw (4) — At the end of your turn, gain 5 Corpses. At the start of your turn, spend 5 Corpses to
			// give your hero +5 Health.
			cards.Add("WW_357", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.TURN_END)
					{
						SingleTask = new CustomTask((g, c, s, t, stack) => Generic.GainCorpses(c, s, V("WW_357", "gain", 5)))
					},
					new Trigger(TriggerType.TURN_START)
					{
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (Generic.SpendCorpses(c, s, V("WW_357", "corpses", 5)))
								Generic.AddEnchantmentBlock(g, Cards.FromId("WW_357e"), (IPlayable)s, c.Hero, V("WW_357", "health", 5), 0, 0);
						})
					})
			}));
			cards.Add("WW_357e", new CardDef(new Power { Enchant = Enchants.Enchants.AddHealthScriptTag }));

			// [YOG_511] Runes of Darkness (1) — Discover a weapon. Spend 3 Corpses to give it +1/+1.
			cards.Add("YOG_511", new CardDef(new Power
			{
				CorpseCost = V("YOG_511", "corpses", 3),
				PowerTask = new DiscoverTask(CardType.WEAPON, CardClass.INVALID, default, ChoiceAction.HAND,
					new CustomTask((g, c, s, t, stack) =>
					{
						if (t != null && Generic.SpendCorpses(c, s))
							Generic.AddEnchantmentBlock(g, Cards.FromId("YOG_511e"), (IPlayable)s, t, 0, 0, 0);
					}))
			}));
			cards.Add("YOG_511e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("YOG_511e", "attack", 1)),
					new Effect(GameTag.DURABILITY, EffectOperator.ADD, V("YOG_511e", "durability", 1)))
			}));

			// [YOG_513] Sinister Soulcage (4) — Give a friendly Undead +2/+2. Spend 5 Corpses to summon a copy of it.
			cards.Add("YOG_513", new CardDef(new Dictionary<PlayReq, int>
			{
				{ PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 },
				{ PlayReq.REQ_TARGET_WITH_RACE, (int)Race.UNDEAD }
			}, new Power
			{
				CorpseCost = V("YOG_513", "corpses", 5),
				PowerTask = ComplexTask.Create(
					new AddEnchantmentTask("YOG_513e", EntityType.TARGET),
					new CustomTask((g, c, s, t, stack) => stack.Flag = !c.BoardZone.IsFull && Generic.SpendCorpses(c, s)),
					new FlagTask(true, new SummonCopyTask(EntityType.TARGET, side: SummonSide.RIGHT)))
			}));
			cards.Add("YOG_513e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("YOG_513e", "stats", 2))) }));
		}

		private static int VolcorossCorpses(string option) => option switch
		{
			"FIR_951t2" => V("FIR_951", "corpses small", 10),
			"FIR_951t3" => V("FIR_951", "corpses medium", 20),
			_ => V("FIR_951", "corpses large", 30),
		};
	}
}
