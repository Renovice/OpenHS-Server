// HsServer addition (not upstream SabberStone): United in Stormwind (2021) and its Deadmines miniset (DED_). The Questlines
// are in QuestlinesCardsGenHs.cs. Every number goes through CardValues (V).
// Shared pieces this set added: Mount (a spell's enchantment that summons its mount when the minion dies: the enchantment's
// own Deathrattle), LoseDurability (the "Lose 1 Durability" weapons), Tradeable's after-trade effects (Power.TradeTask).
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Actions;
using SabberStoneCore.Auras;
using SabberStoneCore.Conditions;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
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
		private const int FIRE = 2, FEL = 7; // client TAG_SPELL_SCHOOL

		/// <summary>"Give a minion +X/+Y (…). When it dies, summon a …" (the Mounts): the enchantment carries the stats and the
		/// keyword and its own Deathrattle summons the mount (silencing the minion removes both).</summary>
		private static CardDef MountSpell(string enchantment) => new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask(enchantment, EntityType.TARGET) });
		private static CardDef MountEnchant(string mount, params IEffect[] effects) => new CardDef(new Power
		{
			Enchant = new Enchant(effects),
			DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
			{
				// an enchantment's Deathrattle runs with the dead minion as its source and the enchantment as its target
				if (t is Enchantment && s is Minion rider && !c.BoardZone.IsFull)
					Generic.SummonBlock.Invoke(g, (Minion)Entity.FromCard(c, Cards.FromId(mount)), rider.LastBoardPosition, s);
			})
		});

		/// <summary>"Lose 1 Durability" (Stormwind's crafting weapons): the weapon takes a point of damage (it breaks at 0).</summary>
		private static void LoseDurability(IEntity source)
		{
			if (source is Weapon w && w.Controller?.Hero?.Weapon?.Id == w.Id) w.Damage += 1; // equipped (the hero holds it: no zone)
		}

		private static bool IsPirate(IPlayable p) => IsRaceCard(p, Race.PIRATE);
		private static bool HasOverloadCard(Card card) => card[GameTag.OVERLOAD] > 0;

		/// <summary>A spell an effect casts as its own step (live, Flightmaster Dungar's flightpath: a new copy of the card,
		/// CREATOR and tag 1715 = the source, enters PLAY, SLUSH_TIME 3000, its POWER block, then the graveyard, SLUSH_TIME
		/// 1000). The client plays the spell card's own effect. Also Kazakusan's "Forging Quel'Delar".</summary>
		private static void CastQuietly(Game g, Controller c, IEntity source, Card spell)
		{
			IPlayable cast = Entity.FromCard(c, spell, new EntityData { { GameTag.CREATOR, source.Id } }, c.SetasideZone);
			cast[(GameTag)1715] = source.Id;
			cast.Zone.Remove(cast);
			cast[GameTag.ZONE] = (int)SabberStoneCore.Enums.Zone.PLAY;
			if (g.History)
			{
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 3000, Info = new List<int> { cast.Id } });
				g.PowerHistory.Add(PowerHistoryBuilder.BlockStart(BlockType.POWER, cast.Id, "", 0, 0));
			}
			cast.Card.Power?.PowerTask?.Process(g, c, cast, null);
			g.ProcessTasks();
			if (g.History) g.PowerHistory.Add(PowerHistoryBuilder.BlockEnd());
			c.GraveyardZone.Add(cast);
			if (g.History)
				g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.SLUSH_TIME, Data = 1000, Info = new List<int> { cast.Id } });
		}

		/// <summary>A Dungar Flight counter: when the count ends, a new copy of its path (CREATOR = the counter) carries the
		/// bonus, then goes to the graveyard (live).</summary>
		private static CardDef Flight(string path, System.Action<Game, Controller, IPlayable> bonus) => new CardDef(new Power
		{
			AwakenTask = new CustomTask((g, c, s, t, stack) =>
			{
				if (!(s is Minion flier)) return;
				IPlayable copy = Entity.FromCard(c, Cards.FromId(path), new EntityData { { GameTag.CREATOR, t?.Id ?? flier.Id } }, c.SetasideZone);
				bonus(g, c, flier);
				Generic.NoteAffected(copy, t?.Id ?? flier.Id);
				if (copy.Zone?.Type == SabberStoneCore.Enums.Zone.SETASIDE) c.GraveyardZone.Add(copy.Zone.Remove(copy));
			})
		});

		private static void Stormwind(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Mounts
			// [SW_087] Dreaded Mount — batch 2 (its Dreadsteed comes back endlessly). [SW_316] Noble Mount (2) — Give a minion
			// +1/+1 and Divine Shield. When it dies, summon a Warhorse.
			cards.Add("SW_316", MountSpell("SW_316e"));
			cards.Add("SW_316e", MountEnchant("SW_316t", Effects.Attack_N(V("SW_316", "attack", 1)), Effects.Health_N(V("SW_316", "health", 1)), new Effect(GameTag.DIVINE_SHIELD, EffectOperator.SET, 1)));
			// [SW_432] Kodo Mount (4) — Give a minion +4/+2 and Rush. When it dies, summon a Kodo.
			cards.Add("SW_432", MountSpell("SW_432e"));
			cards.Add("SW_432e", MountEnchant("SW_432t", Effects.Attack_N(V("SW_432", "attack", 4)), Effects.Health_N(V("SW_432", "health", 2)), new Effect(GameTag.RUSH, EffectOperator.SET, 1)));
			// [SW_443] Elekk Mount (7) — Give a minion +4/+7 and Taunt. When it dies, summon an Elekk.
			cards.Add("SW_443", MountSpell("SW_443e"));
			cards.Add("SW_443e", MountEnchant("SW_443t", Effects.Attack_N(V("SW_443", "attack", 4)), Effects.Health_N(V("SW_443", "health", 7)), Effects.TauntEff));
			// [SW_458] Ramming Mount (3) — Give a minion +2/+2 and Immune while attacking. When it dies, summon a Ram.
			cards.Add("SW_458", MountSpell("SW_458e"));
			cards.Add("SW_458e", MountEnchant("SW_458t", Effects.Attack_N(V("SW_458", "attack", 2)), Effects.Health_N(V("SW_458", "health", 2)), new Effect(GameTag.IMMUNE_WHILE_ATTACKING, EffectOperator.SET, 1)));

			// ---------------------------------------------------------------- Neutral
			// [DED_006] Mr. Smite (7) — Your Pirates have Charge.
			cards.Add("DED_006", new CardDef(new Power
			{
				Aura = new Aura(AuraType.BOARD_EXCEPT_SOURCE, new Effect(GameTag.CHARGE, EffectOperator.SET, 1)) { Condition = new SelfCondition(p => IsPirate(p)) }
			}));
			// [DED_521] Maddest Bomber (8) — Battlecry: Deal 12 damage randomly split among all other characters.
			cards.Add("DED_521", new CardDef(new Power
			{
				PowerTask = new EnqueueTask(V("DED_521", "damage", 12), ComplexTask.DamageRandomTargets(1, EntityType.ALL_NOSOURCE, 1))
			}));
			// [DED_523] Golakka Glutton (3) — Battlecry: Destroy a Beast and gain +1/+1.
			cards.Add("DED_523", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_RACE, (int)Race.BEAST } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion beast)) return;
					beast.Destroy();
					Give(g, s, (IPlayable)s, "DED_523e");
				})
			}));
			cards.Add("DED_523e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DED_523", "stats", 1))) }));
			// [SW_006] Stubborn Suspect (4) — Deathrattle: Summon a random 3-Cost minion.
			cards.Add("SW_006", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, RandomBoardMinion(g, x => x.Cost == V("SW_006", "cost", 3)), s))
			}));
			// [SW_054] Stormwind Guard (5) — Taunt. Battlecry: Give adjacent minions +1/+1.
			cards.Add("SW_054", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me) GiveAll(g, s, Neighbors(me).ToList(), "SW_054e"); }) }));
			cards.Add("SW_054e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("SW_054", "stats", 1))) }));
			// [SW_056] Spice Bread Baker (4) — Battlecry: Restore Health to your hero equal to your hand size.
			cards.Add("SW_056", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (c.HandZone.Count > 0) c.Hero.TakeHeal((IPlayable)s, c.HandZone.Count); })
			}));
			// [SW_057] Package Runner (3) — Can only attack if you have at least 8 cards in hand.
			cards.Add("SW_057", new CardDef(new Power
			{
				Aura = new Aura(AuraType.SELF, new Effect(GameTag.CANT_ATTACK, EffectOperator.SET, 1))
				{
					Condition = new SelfCondition(p => p.Controller.HandZone.Count < V("SW_057", "cards", 8)), Restless = true
				}
			}));
			// [SW_059] Deeprun Engineer (2) — Battlecry: Discover a Mech. It costs (1) less.
			cards.Add("SW_059", new CardDef(new Power
			{
				PowerTask = new DiscoverTask(CardType.MINION, CardClass.INVALID, default, ChoiceAction.HAND, new CustomTask((g, c, s, t, stack) =>
				{
					if (t != null) Give(g, s, t, "SW_059e");
				}), 1, null)
			}));
			cards.Add("SW_059e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("SW_059", "cost", 1))) }));
			// [SW_060] Florist (2) — At the end of your turn, reduce the Cost of a Nature spell in your hand by (1).
			cards.Add("SW_060", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = BuffRandomInHand(p => IsNatureSpell.Eval(p), "SW_060t") }
			}));
			cards.Add("SW_060t", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("SW_060", "cost", 1))) }));
			// [SW_062] Goldshire Gnoll (10) — Rush. Costs (1) less for each other card in your hand.
			cards.Add("SW_062", new CardDef(new Power { Aura = new AdaptiveCostEffect(p => p.Controller.HandZone.Count(x => x.Id != p.Id)) }));
			// [SW_063] Battleground Battlemaster (6) — Adjacent minions have Windfury.
			cards.Add("SW_063", new CardDef(new Power { Aura = new Aura(AuraType.ADJACENT, Effects.Windfury) { ShowAs = "SW_063e" } }));
			// [SW_064] Northshire Farmer (3) — Battlecry: Choose a friendly Beast. Shuffle three 3/3 copies into your deck.
			cards.Add("SW_064", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_RACE, (int)Race.BEAST } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion beast)) return;
					for (int i = 0; i < V("SW_064", "copies", 3); i++)
					{
						IPlayable copy = Entity.FromCard(c, beast.Card);
						Generic.ShuffleIntoDeck.Invoke(c, s, copy);
						Give(g, s, copy, "SW_064e");
					}
				})
			}));
			cards.Add("SW_064e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("SW_064", "stats", 3))) }));
			// [SW_065] Pandaren Importer (2) — Battlecry: Discover a spell that didn't start in your deck.
			cards.Add("SW_065", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var started = new HashSet<string>(g.IdEntityDic.Values.Where(p => p.Id < g.FirstGeneratedEntityId && p.Controller == c).Select(p => p.Card.Id));
					DiscoverFromTask.Where((c2, x) => x.Type == CardType.SPELL && !started.Contains(x.Id)).Process(g, c, s, t);
				})
			}));
			// [SW_067] Stockades Guard (1) — Battlecry: Give a friendly minion Taunt.
			cards.Add("SW_067", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 } },
				new Power { PowerTask = new SetGameTagTask(GameTag.TAUNT, 1, EntityType.TARGET) }));
			// [SW_070] Mailbox Dancer (2) — Battlecry: Add a Coin to your hand. Deathrattle: Give your opponent one.
			cards.Add("SW_070", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => AddToHand(c, Cards.FromId("GAME_005"))),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => AddToHand(c.Opponent, Cards.FromId("GAME_005")))
			}));
			// [SW_071] Lion's Guard (5) — Battlecry: If you have 15 or less Health, gain +2/+4 and Taunt.
			cards.Add("SW_071", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.Hero.Health <= V("SW_071", "health", 15), new AddEnchantmentTask("SW_071e", EntityType.SOURCE))
			}));
			cards.Add("SW_071e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("SW_071", "attack", 2)), Effects.Health_N(V("SW_071", "health+", 4)), Effects.TauntEff) }));
			// [SW_073] Cheesemonger (4) — Whenever your opponent casts a spell, add a random spell with the same Cost to your hand.
			cards.Add("SW_073", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.CAST_SPELL)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable cast = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (cast == null) return;
						int cost = PlayedCost(cast);
						if (RandomCard(g, x => x.Type == CardType.SPELL && x.Cost == cost) is Card card) AddToHand(c, card);
					})
				}
			}));
			// [SW_074] Nobleman (3) — Battlecry: Create a Golden copy of a random card in your hand.
			cards.Add("SW_074", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var hand = c.HandZone.ToList();
					if (hand.Count == 0 || c.HandZone.IsFull) return;
					IPlayable copy = Entity.FromCard(c, hand[g.Random.Next(hand.Count)].Card, new EntityData { { GameTag.PREMIUM, 1 } });
					Generic.AddHandPhase.Invoke(c, copy);
				})
			}));
			// [SW_075] Elwynn Boar (1) — Deathrattle: If you had 10 Elwynn Boars die this game, equip a 15/3 Sword of a Thousand Truths.
			cards.Add("SW_075", new CardDef(new Power
			{
				DeathrattleTask = ComplexTask.If((g, c, s) => c.DiedThisGameCards.Count(p => p.Card.Id == s.Card.Id) >= V("SW_075", "boars", 10), new WeaponTask("SW_075t"))
			}));
			// [SW_075t] Sword of a Thousand Truths — After your hero attacks, destroy your opponent's Mana Crystals.
			cards.Add("SW_075t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = new CustomTask((g, c, s, t, stack) => c.Opponent.BaseMana = 0) }
			}));
			// [SW_076] City Architect (6) — Battlecry: Summon two 0/5 Castle Walls with Taunt.
			cards.Add("SW_076", new CardDef(new Power { PowerTask = ComplexTask.Repeat(new SummonTask("SW_076t", SummonSide.ALTERNATE), V("SW_076", "walls", 2)) }));
			// [SW_081] Varian, King of Stormwind (8) — Battlecry: Draw a Rush minion to gain Rush. Repeat for Taunt and Divine Shield.
			cards.Add("SW_081", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (GameTag keyword in new[] { GameTag.RUSH, GameTag.TAUNT, GameTag.DIVINE_SHIELD })
						if (DrawOne(g, c, p => p is Minion && p.Card[keyword] > 0) != null && s is Minion me && me.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY)
							me[keyword] = 1;
				})
			}));
			// [SW_306] Encumbered Pack Mule (2) — Taunt. When you draw this, add a copy of it to your hand.
			cards.Add("SW_306", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DRAW)
				{
					TriggerActivation = TriggerActivation.DECK,
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) => AddToHand(c, s.Card))
				}
			}));
			// [SW_307] Traveling Merchant (3) — Tradeable. Battlecry: Gain +1/+1 for each other friendly minion you control.
			cards.Add("SW_307", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = c.BoardZone.Count(m => m.Id != s.Id);
					if (n > 0) Generic.AddEnchantmentBlock(g, Cards.FromId("SW_307e"), (IPlayable)s, (IPlayable)s, n, n, 0);
				})
			}));
			cards.Add("SW_307e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(0)) { UseScriptTag = true } }));
			// [SW_319] Peasant (1) — At the start of your turn, draw a card.
			cards.Add("SW_319", new CardDef(new Power { Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = new DrawTask() } }));
			// [SW_418] SI:7 Skulker (2) — Stealth. Battlecry: The next card you draw costs (1) less.
			cards.Add("SW_418", new CardDef(new Power { PowerTask = new AddEnchantmentTask("SW_418e2", EntityType.CONTROLLER) }));
			cards.Add("SW_418e2", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DRAW)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable drawn = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (drawn?.Zone?.Type == SabberStoneCore.Enums.Zone.HAND) Give(g, s, drawn, "SW_418e");
						RemoveEnchantmentTask.Task.Process(g, c, s, null);
					})
				}
			}));
			cards.Add("SW_418e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("SW_418", "cost", 1))) }));

			// ---------------------------------------------------------------- Demon Hunter
			// [DED_507] Crow's Nest Lookout (3) — Battlecry: Deal 2 damage to the left and right-most enemy minions.
			cards.Add("DED_507", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = c.Opponent.BoardZone.Where(m => !m.Untouchable).ToList();
					if (enemies.Count == 0) return;
					foreach (Minion m in new[] { enemies[0], enemies[enemies.Count - 1] }.Distinct())
						Generic.DamageCharFunc((IPlayable)s, m, V("DED_507", "damage", 2), false);
				})
			}));
			// [SW_037] Irebound Brute (7) — Taunt. Costs (1) less for each card drawn this turn.
			cards.Add("SW_037", new CardDef(new Power { Aura = new AdaptiveCostEffect(p => p.Controller.NumCardsDrawnThisTurn) }));
			// [SW_041] Sigil of Alacrity (1) — At the start of your next turn, draw a card and reduce its Cost by (1).
			cards.Add("SW_041", Sigil("SW_041", new CustomTask((g, c, s, t, stack) =>
			{
				if (Generic.Draw(c) is IPlayable drawn && drawn.Zone?.Type == SabberStoneCore.Enums.Zone.HAND) Give(g, s, drawn, "SW_041e2");
			})));
			cards.Add("SW_041e2", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("SW_041", "cost", 1))) }));
			// [SW_042] Persistent Peddler (4) — Tradeable. Deathrattle: Summon a Persistent Peddler from your deck.
			cards.Add("SW_042", new CardDef(new Power { DeathrattleTask = new RecruitTask(1, new SelfCondition(p => p.Card.Id == "SW_042")) }));
			// [SW_043] Felgorger (4) — Battlecry: Draw a Fel spell. Reduce its Cost by (2).
			cards.Add("SW_043", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (DrawOne(g, c, p => p is Spell && p[SPELL_SCHOOL] == FEL) is IPlayable spell) Give(g, s, spell, "SW_043e"); })
			}));
			cards.Add("SW_043e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("SW_043", "cost", 2))) }));
			// [SW_451] Metamorfin (1) — Taunt. Battlecry: If you've cast a Fel spell this turn, gain +2/+2.
			cards.Add("SW_451", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.PlayHistory.Any(h => h.Turn == g.Turn && h.SourceCard.Type == CardType.SPELL && h.SourceCard[SPELL_SCHOOL] == FEL),
					new AddEnchantmentTask("SW_451e", EntityType.SOURCE))
			}));
			cards.Add("SW_451e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("SW_451", "stats", 2))) }));
			// [SW_452] Chaos Leech (3) — Lifesteal. Deal $3 damage to a minion. Outcast: Deal $5 instead.
			cards.Add("SW_452", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m) Generic.DamageCharFunc((IPlayable)s, m, PlayedAsOutcast.Eval((IPlayable)s) ? V("SW_452", "outcast", 5) : V("SW_452", "damage", 3), true);
				})
			}));

			// ---------------------------------------------------------------- Druid
			// [SW_419] Oracle of Elune (3) — After you play a minion that costs (2) or less, summon a copy of it.
			cards.Add("SW_419", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => PlayedCost(p) <= V("SW_419", "cost", 2)),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable played = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (played is Minion m && !c.BoardZone.IsFull && Generic.Copy(c, s, m, SabberStoneCore.Enums.Zone.PLAY) is Minion copy && copy.Zone?.Type != SabberStoneCore.Enums.Zone.PLAY)
							Generic.SummonBlock.Invoke(g, copy, m.ZonePosition + 1, s);
					})
				}
			}));
			// [SW_422] Sow the Soil (1) — Choose One - Give your minions +1 Attack; or Summon a 2/2 Treant.
			cards.Add("SW_422", new CardDef(new Power { PowerTask = ComplexTask.Create(new AddEnchantmentTask("SW_422e", EntityType.MINIONS), new SummonTask("SW_422t", SummonSide.SPELL)) }));
			cards.Add("SW_422a", new CardDef(new Power { PowerTask = new SummonTask("SW_422t", SummonSide.SPELL) }));
			cards.Add("SW_422b", new CardDef(new Power { PowerTask = new AddEnchantmentTask("SW_422e", EntityType.MINIONS) }));
			cards.Add("SW_422e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("SW_422", "attack", 1))) }));
			// [SW_437] Composting (2) — Give your minions "Deathrattle: Draw a card."
			cards.Add("SW_437", new CardDef(new Power { PowerTask = new AddEnchantmentTask("SW_437e", EntityType.MINIONS) }));
			cards.Add("SW_437e", new CardDef(new Power { DeathrattleTask = new DrawTask() }));

			// ---------------------------------------------------------------- Hunter
			// [DED_007] Defias Blastfisher (5) — Battlecry: Deal 2 damage to a random enemy. Repeat for each of your Beasts.
			cards.Add("DED_007", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int shots = 1 + c.BoardZone.Count(m => m.IsRace(Race.BEAST));
					for (int i = 0; i < shots; i++)
					{
						var enemies = new List<ICharacter> { c.Opponent.Hero };
						enemies.AddRange(c.Opponent.BoardZone.Where(m => !m.Untouchable && m.Health > 0));
						Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], V("DED_007", "damage", 2), false);
					}
				})
			}));
			// [SW_320] Rats of Extraordinary Size (6) — Summon seven 1/1 Rats. Any that can't fit on the battlefield go to your hand with +4/+4.
			cards.Add("SW_320", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("SW_320", "rats", 7); i++)
					{
						if (!c.BoardZone.IsFull) { Summon(g, c, Cards.FromId("SW_455t"), s); continue; }
						if (c.HandZone.IsFull) break;
						IPlayable rat = Entity.FromCard(c, Cards.FromId("SW_455t"));
						Generic.AddHandPhase.Invoke(c, rat);
						Give(g, s, rat, "SW_320e");
					}
				})
			}));
			cards.Add("SW_320e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("SW_320", "stats", 4))) }));
			// [SW_321] Aimed Shot (3) — Deal $3 damage. Your next Hero Power deals 2 more damage.
			cards.Add("SW_321", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("SW_321", "damage", 3), EntityType.TARGET, true), new AddEnchantmentTask("SW_321e", EntityType.HERO))
			}));
			cards.Add("SW_321e", new CardDef(new Power
			{
				Enchant = new Enchant(GameTag.HEROPOWER_DAMAGE, EffectOperator.ADD, V("SW_321", "hero power", 2)),
				Trigger = new Trigger(TriggerType.INSPIRE) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = RemoveEnchantmentTask.Task }
			}));
			// [SW_455] Rodent Nest (4) — Deathrattle: Summon five 1/1 Rats.
			cards.Add("SW_455", new CardDef(new Power { DeathrattleTask = ComplexTask.Repeat(new SummonTask("SW_455t"), V("SW_455", "rats", 5)) }));
			// [SW_459] Stormwind Piper (3) — After this minion attacks, give your Beasts +1/+1.
			cards.Add("SW_459", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.Where(m => m.IsRace(Race.BEAST)).ToList(), "SW_459e"))
				}
			}));
			cards.Add("SW_459e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("SW_459", "stats", 1))) }));
			// [SW_463] Imported Tarantula (5) — Tradeable. Deathrattle: Summon two 1/1 Spiders with Poisonous and Rush.
			cards.Add("SW_463", new CardDef(new Power { DeathrattleTask = ComplexTask.Repeat(new SummonTask("SW_463t"), V("SW_463", "spiders", 2)) }));

			// ---------------------------------------------------------------- Mage
			// [DED_516] Deepwater Evoker (4) — Battlecry: Draw a spell. Gain Armor equal to its Cost.
			cards.Add("DED_516", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (DrawOne(g, c, p => p is Spell) is IPlayable spell && spell.Cost > 0) c.Hero.GainArmor((IPlayable)s, spell.Cost); })
			}));
			// [DED_517] Arcane Overflow (5) — Deal $8 damage to an enemy minion. Summon a Remnant with stats equal to the excess damage.
			cards.Add("DED_517", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Generic.DamageCharFunc((IPlayable)s, m, V("DED_517", "damage", 8), true);
					int excess = -m.Health;
					if (excess <= 0 || c.BoardZone.IsFull) return;
					var remnant = (Minion)Entity.FromCard(c, Cards.FromId("DED_517t"), new EntityData { { GameTag.ATK, excess }, { GameTag.HEALTH, excess } });
					Generic.SummonBlock.Invoke(g, remnant, -1, s);
				})
			}));
			// [SW_111] Sanctum Chandler (5) — After you cast a Fire spell, draw a spell.
			cards.Add("SW_111", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY, Condition = IsSchool(FIRE),
					SingleTask = new CustomTask((g, c, s, t, stack) => DrawOne(g, c, p => p is Spell))
				}
			}));
			// [SW_112] Prestor's Pyromancer (2) — Battlecry: Your next Fire spell has Spell Damage +2. (Fire Spell Damage on the player
			// — SPELLPOWER_FIRE — until a Fire spell is cast.)
			cards.Add("SW_112", new CardDef(new Power { PowerTask = new AddEnchantmentTask("SW_112e", EntityType.CONTROLLER) }));
			cards.Add("SW_112e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect((GameTag)(1944 + FIRE), EffectOperator.ADD, V("SW_112", "spell damage", 2))),
				Trigger = new Trigger(TriggerType.AFTER_CAST) { TriggerSource = TriggerSource.FRIENDLY, Condition = IsSchool(FIRE), SingleTask = RemoveEnchantmentTask.Task }
			}));
			// [SW_462] Hot Streak (0) — Your next Fire spell this turn costs (2) less.
			cards.Add("SW_462", new CardDef(new Power { PowerTask = new AddEnchantmentTask("SW_462e", EntityType.CONTROLLER) }));
			cards.Add("SW_462e", NextCardEffect(Effects.ReduceCost(V("SW_462", "cost", 2)), new SelfCondition(p => p is Spell && p[SPELL_SCHOOL] == FIRE), true));

			// ---------------------------------------------------------------- Paladin
			// [SW_046] City Tax (2) — Tradeable. Lifesteal. Deal $1 damage to all enemy minions.
			cards.Add("SW_046", new CardDef(new Power { PowerTask = new DamageTask(V("SW_046", "damage", 1), EntityType.OP_MINIONS, true) }));
			// [SW_049] Blessed Goods (1) — Discover a Secret, weapon, or Divine Shield minion.
			cards.Add("SW_049", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.Where((c, x) => x.IsSecret || x.Type == CardType.WEAPON || x.Type == CardType.MINION && x[GameTag.DIVINE_SHIELD] > 0)
			}));
			// [SW_305] First Blade of Wrynn (4) — Divine Shield. Battlecry: Gain Rush if this has at least 4 Attack.
			cards.Add("SW_305", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => s is Minion m && m.AttackDamage >= V("SW_305", "attack", 4), new SetGameTagTask(GameTag.RUSH, 1, EntityType.SOURCE))
			}));
			// [SW_315] Alliance Bannerman (3) — Battlecry: Draw a minion. Give minions in your hand +1/+1.
			cards.Add("SW_315", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					DrawOne(g, c, p => p is Minion);
					GiveAll(g, s, c.HandZone.Where(p => Generic.IsMinionCard(p)).ToList(), "SW_315e");
				})
			}));
			cards.Add("SW_315e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("SW_315", "stats", 1))) }));
			// [SW_317] Catacomb Guard (3) — Lifesteal. Battlecry: Deal damage equal to this minion's Attack to an enemy minion.
			cards.Add("SW_317", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m && s is Minion me) Generic.DamageCharFunc(me, m, me.AttackDamage, false); })
			}));
			// [DED_502] Righteous Defense (3) — Set a minion's stats to 1/1. Give the stats it lost to a random minion in your hand.
			cards.Add("DED_502", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					int atk = System.Math.Max(0, m.AttackDamage - 1), hp = System.Math.Max(0, m.Health - 1);
					Give(g, s, m, "DED_502e");
					var hand = c.HandZone.Where(p => Generic.IsMinionCard(p)).ToList();
					if (hand.Count > 0 && (atk > 0 || hp > 0))
						Generic.AddEnchantmentBlock(g, Cards.FromId("DED_502e2"), (IPlayable)s, hand[g.Random.Next(hand.Count)], atk, hp, 0);
				})
			}));
			cards.Add("DED_502e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("DED_502", "stats", 1))) }));
			cards.Add("DED_502e2", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.ATK, EffectOperator.ADD, 0), new Effect(GameTag.HEALTH, EffectOperator.ADD, 0)) { UseScriptTag = true }
			}));
			// [DED_500] Wealth Redistributor (5) — Taunt. Battlecry: Swap the Attack of the highest and lowest Attack minion.
			cards.Add("DED_500", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var all = c.BoardZone.Concat(c.Opponent.BoardZone).Where(m => !m.Untouchable).ToList();
					if (all.Count < 2) return;
					int hi = all.Max(m => m.AttackDamage), lo = all.Min(m => m.AttackDamage);
					var highs = all.Where(m => m.AttackDamage == hi).ToList();
					Minion high = highs[g.Random.Next(highs.Count)];
					var lows = all.Where(m => m.AttackDamage == lo && m != high).ToList();
					if (lows.Count == 0) return;
					Minion low = lows[g.Random.Next(lows.Count)];
					Generic.AddEnchantmentBlock(g, Cards.FromId("DED_500e"), (IPlayable)s, high, lo, 0, 0);
					Generic.AddEnchantmentBlock(g, Cards.FromId("DED_500e"), (IPlayable)s, low, hi, 0, 0);
				})
			}));
			cards.Add("DED_500e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.ATK, EffectOperator.SET, 0)) { UseScriptTag = true } }));

			// ---------------------------------------------------------------- Priest
			// [DED_513] Defias Leper (2) — Battlecry: If you're holding a Shadow spell, deal 2 damage.
			cards.Add("DED_513", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter target && c.HandZone.Any(p => p is Spell && p[SPELL_SCHOOL] == SHADOW))
						Generic.DamageCharFunc((IPlayable)s, target, V("DED_513", "damage", 2), false);
				})
			}));
			// [SW_445] Psyfiend (3) — After you cast a Shadow spell, deal 2 damage to each hero.
			cards.Add("SW_445", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST) { TriggerSource = TriggerSource.FRIENDLY, Condition = IsSchool(SHADOW), SingleTask = new DamageTask(V("SW_445", "damage", 2), EntityType.HEROES) }
			}));

			// ---------------------------------------------------------------- Rogue
			// [DED_004] Blackwater Cutlass (1) — Tradeable. After you Trade this, reduce the Cost of a spell in your hand by (1).
			cards.Add("DED_004", new CardDef(new Power { TradeTask = BuffRandomInHand(p => p is Spell, "DED_004e") }));
			cards.Add("DED_004e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("DED_004", "cost", 1))) }));
			// [SW_412] SI:7 Extortion (1) — Tradeable. Deal $3 damage to an undamaged character.
			cards.Add("SW_412", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_UNDAMAGED_TARGET, 0 } },
				new Power { PowerTask = new DamageTask(V("SW_412", "damage", 3), EntityType.TARGET, true) }));
			// [SW_413] SI:7 Operative (3) — Rush. After this attacks a minion, gain Stealth.
			cards.Add("SW_413", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					Condition = new SelfCondition(p => p.Game.CurrentEventData?.EventTarget is Minion),
					SingleTask = new SetGameTagTask(GameTag.STEALTH, 1, EntityType.SOURCE)
				}
			}));
			// [SW_434] Loan Shark (3) — Battlecry: Give your opponent a Coin. Deathrattle: You get two.
			cards.Add("SW_434", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => AddToHand(c.Opponent, Cards.FromId("GAME_005"))),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("SW_434", "coins", 2); i++) AddToHand(c, Cards.FromId("GAME_005")); })
			}));

			// ---------------------------------------------------------------- Shaman
			// [DED_511] Suckerhook (4) — At the end of your turn, transform your weapon into one that costs (1) more.
			cards.Add("DED_511", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (c.Hero.Weapon is Weapon w && RandomCard(g, x => x.Type == CardType.WEAPON && x.Cost == w.Card.Cost + 1) is Card into)
							Generic.ChangeEntityBlock(c, w, into, true);
					})
				}
			}));
			// [DED_522] Cookie the Cook (3) — Lifesteal. Deathrattle: Equip a 2/3 Stirring Rod with Lifesteal.
			cards.Add("DED_522", new CardDef(new Power { DeathrattleTask = new WeaponTask("DED_522t") }));
			// [SW_025] Auctionhouse Gavel (2) — After your hero attacks, reduce the Cost of a Battlecry minion in your hand by (1).
			cards.Add("SW_025", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = BuffRandomInHand(p => p is Minion && p.Card[GameTag.BATTLECRY] > 0, "SW_025e") }
			}));
			cards.Add("SW_025e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("SW_025", "cost", 1))) }));
			// [SW_026] Spirit Alpha (4) — After you play a card with Overload, summon a 2/3 Spirit Wolf with Taunt.
			cards.Add("SW_026", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY, Condition = new SelfCondition(p => HasOverloadCard(p.Card)),
					SingleTask = new SummonTask("EX1_tk11", SummonSide.RIGHT)
				}
			}));
			// [SW_032] Granite Forgeborn (4) — Battlecry: Reduce the Cost of Elementals in your hand and deck by (1).
			cards.Add("SW_032", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.Concat(c.DeckZone).Where(p => IsRaceCard(p, Race.ELEMENTAL)).ToList(), "SW_032e"))
			}));
			cards.Add("SW_032e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("SW_032", "cost", 1))) }));
			// [SW_034] Tiny Toys (6) — Summon four random 5-Cost minions. Make them 2/2.
			cards.Add("SW_034", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("SW_034", "minions", 4) && !c.BoardZone.IsFull; i++)
					{
						Card card = RandomCard(g, x => x.Type == CardType.MINION && x.Cost == V("SW_034", "cost", 5));
						if (card == null) break;
						var m = (Minion)Entity.FromCard(c, card);
						Generic.SummonBlock.Invoke(g, m, -1, s);
						if (m.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY) Give(g, s, m, "SW_034e");
					}
				})
			}));
			cards.Add("SW_034e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("SW_034", "stats", 2))) }));
			// [SW_095] Investment Opportunity (1) — Draw an Overload card.
			cards.Add("SW_095", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => DrawOne(g, c, p => HasOverloadCard(p.Card))) }));

			// ---------------------------------------------------------------- Warlock
			// [DED_504] Wicked Shipment (1) — Tradeable. Summon @ 1/1 Imps. (Upgrades by 2 when Traded!) (the count: its
			// TAG_SCRIPT_DATA_NUM_1, which the client shows)
			cards.Add("DED_504", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int imps = s[GameTag.TAG_SCRIPT_DATA_NUM_1] > 0 ? s[GameTag.TAG_SCRIPT_DATA_NUM_1] : s.Card[GameTag.TAG_SCRIPT_DATA_NUM_1];
					for (int i = 0; i < imps && !c.BoardZone.IsFull; i++) Summon(g, c, Cards.FromId("CORE_GIL_191t"), s);
				}),
				TradeTask = new CustomTask((g, c, s, t, stack) =>
				{
					int now = s[GameTag.TAG_SCRIPT_DATA_NUM_1] > 0 ? s[GameTag.TAG_SCRIPT_DATA_NUM_1] : s.Card[GameTag.TAG_SCRIPT_DATA_NUM_1];
					s[GameTag.TAG_SCRIPT_DATA_NUM_1] = now + V("DED_504", "upgrade", 2);
				})
			}));
			// [DED_505] Hullbreaker (4) — Battlecry and Deathrattle: Draw a spell. Your hero takes damage equal to its Cost.
			ISimpleTask hullbreak = new CustomTask((g, c, s, t, stack) =>
			{
				if (DrawOne(g, c, p => p is Spell) is IPlayable spell && spell.Cost > 0) Generic.DamageCharFunc((IPlayable)s, c.Hero, spell.Cost, false);
			});
			cards.Add("DED_505", new CardDef(new Power { PowerTask = hullbreak, DeathrattleTask = hullbreak }));
			// [SW_086] Shady Bartender (5) — Tradeable. Battlecry: Give your Demons +2/+2.
			cards.Add("SW_086", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.Where(m => m.IsRace(Race.DEMON) && m.Id != s.Id).ToList(), "SW_086e"))
			}));
			cards.Add("SW_086e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("SW_086", "stats", 2))) }));
			// [SW_089] Entitled Customer (6) — Battlecry: Deal damage equal to your hand size to all other minions.
			cards.Add("SW_089", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = c.HandZone.Count;
					if (n > 0) foreach (Minion m in c.BoardZone.Concat(c.Opponent.BoardZone).Where(m => m.Id != s.Id).ToList()) Generic.DamageCharFunc((IPlayable)s, m, n, false);
				})
			}));
			// [SW_090] Touch of the Nathrezim (1) — Deal $2 damage to a minion. If it dies, restore 3 Health to your hero.
			cards.Add("SW_090", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Generic.DamageCharFunc((IPlayable)s, m, V("SW_090", "damage", 2), true);
					if (m.Health <= 0 || m.ToBeDestroyed) c.Hero.TakeHeal((IPlayable)s, V("SW_090", "heal", 3));
				})
			}));
			// [SW_092] Anetheron (6) — Costs (1) if your hand is full.
			cards.Add("SW_092", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.HandZone.IsFull ? System.Math.Max(0, p.Card.Cost - V("SW_092", "cost", 1)) : 0)
			}));

			// ---------------------------------------------------------------- Warrior
			// [DED_518] Man the Cannons (2) — Deal $3 damage to a minion and $1 damage to all other minions.
			cards.Add("DED_518", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Generic.DamageCharFunc((IPlayable)s, m, V("DED_518", "damage", 3), true);
					foreach (Minion other in c.BoardZone.Concat(c.Opponent.BoardZone).Where(x => x != m).ToList())
						Generic.DamageCharFunc((IPlayable)s, other, V("DED_518", "others", 1), true);
				})
			}));
			// [DED_519] Defias Cannoneer (3) — After your hero attacks, deal 2 damage to a random enemy twice.
			cards.Add("DED_519", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new EnqueueTask(V("DED_519", "shots", 2), ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, V("DED_519", "damage", 2)))
				}
			}));
			// [DED_527] Blacksmithing Hammer (4) — Tradeable. After you Trade this, gain +2 Durability.
			cards.Add("DED_527", new CardDef(new Power { TradeTask = new CustomTask((g, c, s, t, stack) => Give(g, s, (IPlayable)s, "DED_527e")) }));
			cards.Add("DED_527e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.DURABILITY, EffectOperator.ADD, V("DED_527", "durability", 2))) }));
			// [SW_021] Cowardly Grunt (6) — Deathrattle: Summon a minion from your deck.
			cards.Add("SW_021", new CardDef(new Power { DeathrattleTask = new RecruitTask(1) }));
			// [SW_024] Lothar (7) — At the end of your turn, attack a random enemy minion. If it dies, gain +3/+3.
			cards.Add("SW_024", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var enemies = c.Opponent.BoardZone.Where(m => !m.Untouchable && !m.ToBeDestroyed).ToList();
						if (!(s is Minion me) || enemies.Count == 0) return;
						Minion victim = enemies[g.Random.Next(enemies.Count)];
						EffectAttack(g, me, victim);
						if ((victim.Health <= 0 || victim.ToBeDestroyed || victim.Zone?.Type == SabberStoneCore.Enums.Zone.GRAVEYARD) && me.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY && !me.ToBeDestroyed)
							Give(g, me, me, "SW_024e");
					})
				}
			}));
			cards.Add("SW_024e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("SW_024", "stats", 3))) }));
			// [SW_027] Shiver Their Timbers! (1) — Deal $2 damage to a minion. If you control a Pirate, deal $5 instead.
			cards.Add("SW_027", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m) Generic.DamageCharFunc((IPlayable)s, m, c.BoardZone.Any(x => x.IsRace(Race.PIRATE)) ? V("SW_027", "pirate", 5) : V("SW_027", "damage", 2), true);
				})
			}));
			// [SW_029] Harbor Scamp (2) — Battlecry: Draw a Pirate.
			cards.Add("SW_029", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => DrawOne(g, c, p => IsPirate(p))) }));
			// [SW_093] Stormwind Freebooter (3) — Battlecry: Give your hero +2 Attack this turn.
			cards.Add("SW_093", new CardDef(new Power { PowerTask = new AddEnchantmentTask("SW_093e", EntityType.HERO) }));
			cards.Add("SW_093e", OneTurn(Effects.Attack_N(V("SW_093", "attack", 2))));
			// [SW_097] Remote-Controlled Golem (4) — After this takes damage, shuffle two Golem Parts into your deck. When drawn, summon a 2/1 Mech.
			cards.Add("SW_097", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("SW_097", "parts", 2); i++) Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("SW_097t"))); })
				}
			}));
			// [SW_097t] Golem Parts — Casts When Drawn. Summon a 2/1 Damaged Golem.
			cards.Add("SW_097t", new CardDef(new Power { TopdeckTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("skele21"), s)) }));

			// ---------------------------------------------------------------- Crafting weapons ("Lose 1 Durability")
			// [SW_012] Shadowcloth Needle (1) — After you cast a Shadow spell, deal 1 damage to all enemies. Lose 1 Durability.
			cards.Add("SW_012", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY, Condition = IsSchool(SHADOW),
					SingleTask = ComplexTask.Create(new DamageTask(V("SW_012", "damage", 1), EntityType.ENEMIES), new CustomTask((g, c, s, t, stack) => LoseDurability(s)))
				}
			}));
			// [SW_048] Prismatic Jewel Kit (1) — After a friendly minion loses Divine Shield, give minions in your hand +1/+1. Lose 1 Durability.
			cards.Add("SW_048", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.LOSE_DIVINE_SHIELD)
				{
					TriggerSource = TriggerSource.MINIONS,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						GiveAll(g, s, c.HandZone.Where(p => Generic.IsMinionCard(p)).ToList(), "SW_048e");
						LoseDurability(s);
					})
				}
			}));
			cards.Add("SW_048e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("SW_048", "stats", 1))) }));
			StormwindBatch2(cards);
		}

		/// <summary>"Repeat the last Battlecry …" (Brilliant Macaw, Bolner Hammerbeak): <paramref name="card"/>'s effect again,
		/// from <paramref name="source"/>, at a random valid target when it takes one (user 2026-10-06: "repeat the last tend to
		/// be random valid targets unless specified otherwise"); a card that must have a target and has none does nothing.</summary>
		private static void RepeatEffect(Game g, Controller c, IEntity source, Card card)
		{
			ISimpleTask effect = card?.Power?.PowerTask;
			if (effect == null || c[HsTag.REPEAT_DEPTH] >= 3) return;
			IPlayable target = null;
			if (card.PlayRequirements != null && card.PlayRequirements.Count > 0)
			{
				var valid = card.GetValidPlayTargets(c).Where(x => x.Id != source.Id).ToList();
				if (valid.Count > 0) target = (IPlayable)valid[g.Random.Next(valid.Count)];
				else if (card.RequiresTarget) return;
			}
			c[HsTag.REPEAT_DEPTH] += 1;
			// (2026-10-08: as live — the repeated card shown, its effect in a block naming it: Generic.RunCardEffect)
			try { Generic.RunCardEffect(g, c, (IPlayable)source, card, target, inHistory: true); }
			finally { c[HsTag.REPEAT_DEPTH] -= 1; }
		}

		/// <summary>Spells you cast this game, in order (the cast card's record).</summary>
		private static IEnumerable<PlayHistoryEntry> SpellsCast(Controller c) => c.PlayHistory.Where(h => h.SourceCard?.Type == CardType.SPELL);
		private static bool IsSI7(Card card) => card.Name.Contains("SI:7");

		/// <summary>Brilliant Macaw's "the last Battlecry you played" — another one: never a Macaw (user 2026-10-06: it can't
		/// repeat itself; a Macaw copying a Macaw looped).</summary>
		private static Card LastBattlecry(Controller c, IPlayable macaw) => c.PlayHistory
			.Where(h => h.SourceId != macaw.Id && h.SourceCard?.Type == CardType.MINION && h.SourceCard[GameTag.BATTLECRY] > 0 && h.SourceCard.Id != macaw.Card.Id)
			.LastOrDefault().SourceCard;

		private static void StormwindBatch2(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Repeat
			// [DED_509] Brilliant Macaw (3) — Battlecry: Repeat the last Battlecry you played.
			cards.Add("DED_509", new CardDef(new Power
			{
				MouseOverCard = p => LastBattlecry(p.Controller, p),
				// (2026-10-08, live: in hand, an empty TRIGGER block at index 1 after each Battlecry minion you play — 52 of 52, no keyword;
				// the client refreshes the card from it)
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerActivation = TriggerActivation.HAND,
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.Card[GameTag.BATTLECRY] > 0),
					EffectIndex = 1,
					KeywordOverride = (GameTag)0,
					SingleTask = new CustomTask((g, c, s, t, stack) => { })
				},
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (LastBattlecry(c, (IPlayable)s) is Card last) RepeatEffect(g, c, s, last);
				})
			}));
			// [SW_115] Bolner Hammerbeak (2) — After you play a Battlecry minion, repeat the first Battlecry played this turn.
			cards.Add("SW_115", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.Card[GameTag.BATTLECRY] > 0),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var first = c.PlayHistory.FirstOrDefault(h => h.Turn == g.Turn && h.SourceCard?.Type == CardType.MINION && h.SourceCard[GameTag.BATTLECRY] > 0);
						if (first.SourceCard != null) RepeatEffect(g, c, s, first.SourceCard);
					})
				}
			}));
			// [DED_515] Grey Sage Parrot (6) — Battlecry: Repeat the last spell you've cast that costs (6) or more.
			cards.Add("DED_515", new CardDef(new Power
			{
				MouseOverCard = p => SpellsCast(p.Controller).LastOrDefault(h => h.Cost >= V("DED_515", "cost", 6)).SourceCard,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var last = SpellsCast(c).LastOrDefault(h => h.Cost >= V("DED_515", "cost", 6));
					if (last.SourceCard != null) new CastRandomSpellTask().Process(g, c, s, Entity.FromCard(c, last.SourceCard));
				})
			}));
			// [DED_501] Sunwing Squawker (4) — Battlecry: Repeat the last spell you've cast on a friendly minion on this.
			cards.Add("DED_501", new CardDef(new Power
			{
				MouseOverCard = p => SpellsCast(p.Controller).LastOrDefault(h => h.TargetCard?.Type == CardType.MINION && h.TargetController == p.Controller.PlayerId).SourceCard,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var last = SpellsCast(c).LastOrDefault(h => h.TargetCard?.Type == CardType.MINION && h.TargetController == c.PlayerId);
					if (last.SourceCard != null)
						new CastRandomSpellTask { TargetsSourceIfPossible = true }.Process(g, c, s, Entity.FromCard(c, last.SourceCard));
				})
			}));

			// ---------------------------------------------------------------- Neutral
			// [DED_008] Monstrous Parrot (4) — Battlecry: Get a copy of the last friendly Deathrattle minion that died.
			cards.Add("DED_008", new CardDef(new Power
			{
				MouseOverCard = p => p.Controller.DiedThisGameCards.LastOrDefault(x => x is Minion && x.Card[GameTag.DEATHRATTLE] > 0)?.Card,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.DiedThisGameCards.LastOrDefault(p => p is Minion && p.Card[GameTag.DEATHRATTLE] > 0) is IPlayable dead) AddToHand(c, dead.Card);
				})
			}));
			// [DED_524] Multicaster (4) — Battlecry: Draw a card for each different spell school you've cast this game.
			cards.Add("DED_524", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int schools = Generic.SpellSchoolsCast(c).Count;
					for (int i = 0; i < schools; i++) Generic.Draw(c);
				})
			}));
			// [DED_525] Goliath, Sneed's Masterpiece (8) — Battlecry: Fire five rockets at enemy minions that deal 2 damage each. (You pick the targets!)
			cards.Add("DED_525", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.PickTargets(c, s, V("DED_525", "rockets", 5), ch => ch is Minion m && m.Controller != c && !m.Untouchable && m.Health > 0,
						(g2, c2, s2, target) => Generic.DamageCharFunc((IPlayable)s2, target, V("DED_525", "damage", 2), false), different: false))
			}));
			// [SW_036] Two-Faced Investor (3) — At the end of your turn, reduce the Cost of a card in your hand by (1). (50% chance to increase.)
			cards.Add("SW_036", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var hand = c.HandZone.ToList();
						if (hand.Count > 0) Give(g, s, hand[g.Random.Next(hand.Count)], g.Random.Next(2) == 0 ? "SW_036e" : "SW_036e2");
					})
				}
			}));
			cards.Add("SW_036e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("SW_036", "cost", 1))) }));
			cards.Add("SW_036e2", new CardDef(new Power { Enchant = new Enchant(Effects.AddCost(V("SW_036", "cost", 1))) }));
			// [SW_045] Auctioneer Jaxon (2) — Whenever you Trade, Discover a card from your deck to draw instead.
			cards.Add("SW_045", new CardDef(new Power { Aura = new Aura(AuraType.CONTROLLER, new Effect(HsTag.TRADE_DISCOVERS, EffectOperator.ADD, 1)) }));
			// [SW_069] Enthusiastic Banker (3) — At the end of your turn, store a card from your deck. Deathrattle: Add the stored cards
			// to your hand. (Each stored card is a SW_069e "Safety Deposit" on it; its Deathrattle gives the card back.)
			cards.Add("SW_069", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (c.DeckZone.Count == 0 || !(s is Minion me)) return;
						IPlayable stored = c.DeckZone[g.Random.Next(c.DeckZone.Count)];
						c.DeckZone.Remove(stored);
						c.SetasideZone.Add(stored);
						Generic.AddEnchantmentBlock(g, Cards.FromId("SW_069e"), me, me, 0, 0, stored.Id);
					})
				}
			}));
			cards.Add("SW_069e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { if (t is Enchantment e && e.CapturedCard != null) AddToHand(c, e.CapturedCard); })
			}));
			// [SW_077] Stockades Prisoner (2) — Starts Dormant. After you play 3 cards, this awakens.
			cards.Add("SW_077", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					WhileDormant = true,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me && me[GameTag.DORMANT] > 0) Generic.AwakenProgress(me, 1); })
				}
			}));
			// [SW_078] Lady Prestor (6) — Battlecry: Transform minions in your deck into random Dragons. (They keep their original stats
			// and Cost.) (In the deck: unseen; SW_078e / SW_078e2 keep the stats and Cost.)
			cards.Add("SW_078", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.DeckZone.OfType<Minion>().Where(m => Generic.IsMinionCard(m)).ToList())
					{
						Card dragon = RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.DRAGON));
						if (dragon == null) break;
						int atk = m.AttackDamage, hp = m.Health, cost = m.Cost;
						g.SilentChanges = true;
						IPlayable now;
						try { now = Generic.ChangeEntityBlock(c, m, dragon, true); }
						finally { g.SilentChanges = false; }
						Generic.AddEnchantmentBlock(g, Cards.FromId("SW_078e"), (IPlayable)s, now, atk, hp, 0);
						Generic.AddEnchantmentBlock(g, Cards.FromId("SW_078e2"), (IPlayable)s, now, cost, 0, 0);
					}
				})
			}));
			cards.Add("SW_078e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.ATK, EffectOperator.SET, 0), new Effect(GameTag.HEALTH, EffectOperator.SET, 0)) { UseScriptTag = true }
			}));
			cards.Add("SW_078e2", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) { UseScriptTag = true } }));
			// [SW_079] Flightmaster Dungar (3) — Battlecry: Choose a flightpath and go Dormant. Awaken with a bonus when you complete it!
			// (Westfall: 1 turn, a 2/2 Adventurer; Ironforge: 3 turns, restore 10 Health; Eastern Plaguelands: 5 turns, 12 damage
			// split among enemies.) Live (live-20261006-dungar-student: Ironforge, and Shudderwock repeating it into Eastern
			// Plaguelands): TAG_SCRIPT_DATA_NUM_1 = 1 on the card, the three paths offered; the pick sets FX_DATANUM_1 = its place
			// (1-3), the card goes Dormant under its Flight counter (SW_079e4-6), then a new copy of the path is cast (its POWER
			// block puts the path's Flight enchantment on the player, which stays) and the picked option goes to the graveyard. At
			// the end of the count the counter's block wakes the card, then the path's bonus plays (source: the card).
			string[] paths = { "SW_079t", "SW_079t2", "SW_079t3" };
			cards.Add("SW_079", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int flier = s.Id;
					s[GameTag.TAG_SCRIPT_DATA_NUM_1] = 1;
					Generic.CreateChoiceEntities(c, s, OfferCards(c, s, paths), ChoiceAction.STACK, new CustomTask((g2, c2, s2, path, st2) =>
					{
						if (path == null || !(g2.IdEntityDic.TryGetValue(flier, out IPlayable me) && me is Minion m && m.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY)) return;
						int which = System.Array.IndexOf(paths, path.Card.Id);
						if (which < 0) return;
						m[GameTag.FX_DATANUM_1] = which + 1;
						Generic.MakeDormant(m, new[] { V("SW_079t", "turns", 1), V("SW_079t2", "turns", 3), V("SW_079t3", "turns", 5) }[which],
							new[] { "SW_079e4", "SW_079e5", "SW_079e6" }[which], m);
						CastQuietly(g2, c2, m, path.Card);
						if (g2.History)
							g2.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.HISTORY_TARGET, Data = 0, Info = new List<int> { path.Id } });
						Generic.NoteAffected(path, m.Id);
						if (path.Zone?.Type == SabberStoneCore.Enums.Zone.SETASIDE) c2.GraveyardZone.Add(path.Zone.Remove(path));
					}));
				}),
				// its own awaken block is empty (live: EffectIndex 0, EFFECT_TIMING only); the bonus is the counter's
				AwakenTask = new CustomTask((g, c, s, t, stack) => { }),
				OwnFx = true // the recorded heal effect is Ironforge's bonus, placed by the script (not the battlecry's)
			}));
			foreach ((string path, string flight) in new[] { ("SW_079t", "SW_079te"), ("SW_079t2", "SW_079t2e"), ("SW_079t3", "SW_079t3e") })
				cards.Add(path, new CardDef(new Power { PowerTask = new AddEnchantmentTask(flight, EntityType.CONTROLLER) }));
			// the counters: the bonus when the flight lands (source: the woken card, target: the counter)
			cards.Add("SW_079e4", Flight("SW_079t", (g, c, flier) => Summon(g, c, RandomAdventurer(g), flier)));
			cards.Add("SW_079e5", Flight("SW_079t2", (g, c, flier) =>
				Generic.NamedFx(flier, "Heal", new[] { c.Hero.Id }, () => c.Hero.TakeHeal(flier, V("SW_079t2", "heal", 10)))));
			cards.Add("SW_079e6", Flight("SW_079t3", (g, c, flier) =>
				new EnqueueTask(V("SW_079t3", "damage", 12), ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, 1)).Process(g, c, flier, null)));
			// [SW_323] The Rat King (5) — Rush. Deathrattle: Go Dormant. Revive after 5 friendly minions die.
			// (WONDERS: the shared "Go Dormant. Revive after N friendly minions die" — Generic.GoDormantOnDeath / AwakenAfterDeaths, as
			// Malorne)
			cards.Add("SW_323", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => Generic.GoDormantOnDeath(c, s, "SW_323e")),
				Trigger = Generic.AwakenAfterDeaths()
			}));
			// [SW_400] Entrapped Sorceress (3) — Battlecry: If you control a Quest, Discover a spell.
			cards.Add("SW_400", new CardDef(new Power { PowerTask = ComplexTask.If((g, c, s) => c.SecretZone.Quest != null, new DiscoverTask(CardType.SPELL)) }));

			// ---------------------------------------------------------------- Demon Hunter
			// [DED_506] Need for Greed (5) — Tradeable. Draw 3 cards. If drawn this turn, this costs (3).
			cards.Add("DED_506", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("DED_506", "draw", 3); i++) Generic.Draw(c); }),
				Aura = new AdaptiveCostEffect(p => p[HsTag.ADDED_TO_HAND_TURN] == p.Game.Turn && p.Id < p.Game.FirstGeneratedEntityId
					? System.Math.Max(0, p.Card.Cost - V("DED_506", "cost", 3)) : 0)
			}));
			// [DED_508] Proving Grounds (6) — Summon two minions from your deck. They fight! (User 2026-10-06: they attack each other.)
			cards.Add("DED_508", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var fighters = new List<Minion>();
					for (int i = 0; i < 2; i++)
					{
						var deck = c.DeckZone.OfType<Minion>().Where(m => Generic.IsMinionCard(m)).ToList();
						if (deck.Count == 0 || c.BoardZone.IsFull) break;
						Minion m = deck[g.Random.Next(deck.Count)];
						c.DeckZone.Remove(m);
						Generic.SummonBlock.Invoke(g, m, -1, s);
						if (m.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY) fighters.Add(m);
					}
					if (fighters.Count == 2) EffectAttack(g, fighters[0], fighters[1]);
				})
			}));
			// [SW_044] Jace Darkweaver (8) — Battlecry: Cast all Fel spells you've played this game (targets enemies if possible).
			cards.Add("SW_044", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Card fel in SpellsCast(c).Where(h => h.SourceCard[SPELL_SCHOOL] == FEL).Select(h => h.SourceCard).ToList())
						new CastRandomSpellTask(enemiesIfPossible: true).Process(g, c, s, Entity.FromCard(c, fel));
				})
			}));
			// [SW_454] Lion's Frenzy (3) — Has Attack equal to the number of cards you've drawn this turn.
			cards.Add("SW_454", new CardDef(new Power { Aura = new AdaptiveEffect(GameTag.ATK, EffectOperator.SET, p => p.Controller.NumCardsDrawnThisTurn) }));

			// ---------------------------------------------------------------- Druid
			// [DED_002] Moonlit Guidance (2) — Discover a copy of a card in your deck. If you play it this turn, draw the original.
			cards.Add("DED_002", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.DiscoverFromCards(c, s, Generic.PickFromDeck(c, p => true), (g2, c2, s2, original) =>
					{
						if (c2.HandZone.IsFull) return;
						IPlayable copy = Entity.FromCard(c2, original.Card);
						Generic.AddHandPhase.Invoke(c2, copy);
						if (copy.Zone?.Type == SabberStoneCore.Enums.Zone.HAND)
							Generic.AddEnchantmentBlock(g2, Cards.FromId("DED_002e"), (IPlayable)s2, copy, 0, 0, original.Id);
						copy[GameTag.TAG_SCRIPT_DATA_ENT_1] = original.Id;
					}))
			}));
			cards.Add("DED_002e", new CardDef(new Power
			{
				Enchant = new Enchant { IsOneTurnEffect = true },
				Trigger = new Trigger(TriggerType.PLAY_CARD)
				{
					TriggerActivation = TriggerActivation.HAND_OR_PLAY,
					TriggerSource = TriggerSource.ENCHANTMENT_TARGET,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Enchantment e && g.IdEntityDic.TryGetValue(e.Target[GameTag.TAG_SCRIPT_DATA_ENT_1], out IPlayable original) &&
						    original.Zone?.Type == SabberStoneCore.Enums.Zone.DECK)
							Generic.Draw(c, original);
					})
				}
			}));
			// [DED_003] Jerry Rig Carpenter (2) — Battlecry: Draw a Choose One spell and split it.
			cards.Add("DED_003", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(DrawOne(g, c, p => p is Spell && p.Card.ChooseOne) is IPlayable spell)) return;
					Card[] options = Cards.ChooseOneOptions(spell.Card);
					if (options == null || options.Length == 0) return;
					c.HandZone.Remove(spell);
					c.SetasideZone.Add(spell);
					foreach (Card option in options) AddToHand(c, option);
				})
			}));
			// [SW_436] Wickerclaw (2) — After your hero gains Attack, this minion gains +2 Attack.
			cards.Add("SW_436", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.HERO_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = new AddEnchantmentTask("SW_436e", EntityType.SOURCE) }
			}));
			cards.Add("SW_436e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("SW_436", "attack", 2))) }));
			// [SW_447] Sheldras Moontree (8) — Battlecry: The next 3 spells you draw are Cast When Drawn.
			cards.Add("SW_447", new CardDef(new Power { PowerTask = new AddEnchantmentTask("SW_447e", EntityType.CONTROLLER) }));
			cards.Add("SW_447e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DRAW)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Spell),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || !((t ?? g.CurrentEventData?.EventSource as IPlayable) is Spell drawn) || drawn.Zone?.Type != SabberStoneCore.Enums.Zone.HAND) return;
						Generic.CastFromHand(c, s, drawn);
						e[GameTag.TAG_SCRIPT_DATA_NUM_1] = e[GameTag.TAG_SCRIPT_DATA_NUM_1] + 1;
						if (e[GameTag.TAG_SCRIPT_DATA_NUM_1] >= V("SW_447", "spells", 3)) RemoveEnchantmentTask.Task.Process(g, c, s, null);
					})
				}
			}));

			// ---------------------------------------------------------------- Hunter
			// [SW_457] Leatherworking Kit (1) — After three friendly Beasts die, draw a Beast and give it +1/+1. Lose 1 Durability.
			cards.Add("SW_457", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerSource = TriggerSource.MINIONS,
					Condition = new SelfCondition(p => IsRaceCard(p, Race.BEAST)),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						s[GameTag.TAG_SCRIPT_DATA_NUM_1] = s[GameTag.TAG_SCRIPT_DATA_NUM_1] + 1;
						if (s[GameTag.TAG_SCRIPT_DATA_NUM_1] < V("SW_457", "beasts", 3)) return;
						s[GameTag.TAG_SCRIPT_DATA_NUM_1] = 0;
						if (DrawOne(g, c, p => IsRaceCard(p, Race.BEAST)) is IPlayable beast) Give(g, s, beast, "SW_457e");
						LoseDurability(s);
					})
				}
			}));
			cards.Add("SW_457e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("SW_457", "stats", 1))) }));
			// [SW_460] Devouring Swarm (0) — Choose an enemy minion. Your minions attack it, then return any that die to your hand.
			cards.Add("SW_460", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion prey)) return;
					var swarm = c.BoardZone.Where(m => !m.Untouchable).ToList();
					var died = new List<Card>();
					foreach (Minion m in swarm)
					{
						if (prey.Zone?.Type != SabberStoneCore.Enums.Zone.PLAY || prey.ToBeDestroyed) break;
						EffectAttack(g, m, prey);
						if (m.ToBeDestroyed || m.Health <= 0 || m.Zone?.Type == SabberStoneCore.Enums.Zone.GRAVEYARD) died.Add(m.Card);
					}
					g.DeathProcessingAndAuraUpdate();
					foreach (Card card in died) AddToHand(c, card);
				})
			}));

			// ---------------------------------------------------------------- Mage
			// [SW_001] Celestial Ink Set (2) — After you spend 5 Mana on spells, reduce the Cost of a spell in your hand by (5). Lose 1 Durability.
			cards.Add("SW_001", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable cast = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (cast == null) return;
						s[GameTag.TAG_SCRIPT_DATA_NUM_1] = s[GameTag.TAG_SCRIPT_DATA_NUM_1] + PlayedCost(cast);
						if (s[GameTag.TAG_SCRIPT_DATA_NUM_1] < V("SW_001", "mana", 5)) return;
						s[GameTag.TAG_SCRIPT_DATA_NUM_1] -= V("SW_001", "mana", 5);
						BuffRandomInHand(p => p is Spell, "SW_001e2").Process(g, c, s, null);
						LoseDurability(s);
					})
				}
			}));
			cards.Add("SW_001e2", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("SW_001", "cost", 5))) }));
			// [SW_109] Clumsy Courier (7) — Battlecry: Cast the highest Cost spell from your hand.
			cards.Add("SW_109", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var spells = c.HandZone.OfType<Spell>().ToList();
					if (spells.Count == 0) return;
					int top = spells.Max(x => x.Cost);
					var highest = spells.Where(x => x.Cost == top).ToList();
					Generic.CastFromHand(c, s, highest[g.Random.Next(highest.Count)]);
				})
			}));
			// [SW_110] Ignite (2) — Deal $@ damage. Shuffle an Ignite into your deck that deals one more damage. (its damage: its
			// TAG_SCRIPT_DATA_NUM_1, shown by the client)
			cards.Add("SW_110", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int damage = s[GameTag.TAG_SCRIPT_DATA_NUM_1] > 0 ? s[GameTag.TAG_SCRIPT_DATA_NUM_1] : s.Card[GameTag.TAG_SCRIPT_DATA_NUM_1];
					if (t is ICharacter target) Generic.DamageCharFunc((IPlayable)s, target, damage, true);
					IPlayable next = Entity.FromCard(c, s.Card, new EntityData { { GameTag.TAG_SCRIPT_DATA_NUM_1, damage + 1 } });
					Generic.ShuffleIntoDeck.Invoke(c, s, next);
				})
			}));
			// [SW_113] Grand Magus Antonidas (8) — Battlecry: If you've cast a Fire spell on each of your last three turns, cast 3
			// Fireballs at random enemies. (Your last three turns: this one and the two before it.)
			cards.Add("SW_113", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					bool FireOn(int turn) => SpellsCast(c).Any(h => h.Turn == turn && h.SourceCard[SPELL_SCHOOL] == FIRE);
					if (!(FireOn(g.Turn) && FireOn(g.Turn - 2) && FireOn(g.Turn - 4))) return;
					for (int i = 0; i < V("SW_113", "fireballs", 3); i++)
						new CastRandomSpellTask(enemiesIfPossible: true).Process(g, c, s, Entity.FromCard(c, Cards.FromId("CS2_029")));
				})
			}));

			// ---------------------------------------------------------------- Paladin
			// [SW_314] Lightbringer's Hammer (3) — Lifesteal. Can't attack heroes. (Engine: the loader's CANNOT_ATTACK_HEROES from
			// the text; a hero holding such a weapon can't attack heroes — Hero.CantAttackHeroes.)
			cards.Add("SW_314", EngineOnly);

			// ---------------------------------------------------------------- Priest
			// [DED_512] Amulet of Undying (3) — Tradeable. Resurrect @ friendly Deathrattle minions. (Upgrades when Traded!)
			cards.Add("DED_512", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = s[GameTag.TAG_SCRIPT_DATA_NUM_1] > 0 ? s[GameTag.TAG_SCRIPT_DATA_NUM_1] : s.Card[GameTag.TAG_SCRIPT_DATA_NUM_1];
					var dead = c.DiedThisGameCards.Where(p => p is Minion && p.Card[GameTag.DEATHRATTLE] > 0).ToList();
					for (int i = 0; i < n && dead.Count > 0 && !c.BoardZone.IsFull; i++)
					{
						IPlayable pick = dead[g.Random.Next(dead.Count)];
						dead.Remove(pick);
						Summon(g, c, pick.Card, s);
					}
				}),
				TradeTask = new CustomTask((g, c, s, t, stack) =>
				{
					int now = s[GameTag.TAG_SCRIPT_DATA_NUM_1] > 0 ? s[GameTag.TAG_SCRIPT_DATA_NUM_1] : s.Card[GameTag.TAG_SCRIPT_DATA_NUM_1];
					s[GameTag.TAG_SCRIPT_DATA_NUM_1] = now + V("DED_512", "upgrade", 1);
				})
			}));
			// [DED_514] Copycat (3) — Battlecry: Add a copy of the next card your opponent plays to your hand.
			cards.Add("DED_514", new CardDef(new Power { PowerTask = new AddEnchantmentTask("DED_514e", EntityType.CONTROLLER) }));
			cards.Add("DED_514e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_CARD)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable played = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (played != null) AddToHand(c, played.Card);
						RemoveEnchantmentTask.Task.Process(g, c, s, null);
					})
				}
			}));
			// [SW_440] Call of the Grave (1) — Discover a Deathrattle minion. If you have enough Mana to play it, trigger its Deathrattle.
			cards.Add("SW_440", new CardDef(new Power
			{
				PowerTask = new DiscoverTask(CardType.MINION, CardClass.INVALID, (GameTag.DEATHRATTLE, RelaSign.EQ, 1), ChoiceAction.HAND, new CustomTask((g, c, s, t, stack) =>
				{
					if (t != null && c.RemainingMana >= t.Cost) TriggerDeathrattleOf(g, c, t);
				}), 1, null)
			}));
			// [SW_444] Twilight Deceptor (2) — Battlecry: If any hero took damage this turn, draw a Shadow spell.
			cards.Add("SW_444", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c[HsTag.HERO_DAMAGE_THIS_TURN] > 0 || c.Opponent[HsTag.HERO_DAMAGE_THIS_TURN] > 0,
					new CustomTask((g, c, s, t, stack) => DrawOne(g, c, p => p is Spell && p[SPELL_SCHOOL] == SHADOW)))
			}));
			// [SW_446] Voidtouched Attendant (2) — Both heroes take one extra damage from all sources.
			cards.Add("SW_446", new CardDef(new Power { Aura = new Aura(AuraType.CONTROLLERS, new Effect(HsTag.HERO_EXTRA_DAMAGE, EffectOperator.ADD, 1)) }));

			// ---------------------------------------------------------------- Rogue
			// [DED_005] Parrrley (1) — Swap this for a card in your opponent's deck.
			cards.Add("DED_005", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.Opponent.DeckZone.Count == 0) return;
					IPlayable taken = c.Opponent.DeckZone[g.Random.Next(c.Opponent.DeckZone.Count)];
					c.Opponent.DeckZone.Remove(taken);
					taken.Controller = c;
					taken[GameTag.CONTROLLER] = c.PlayerId;
					if (c.HandZone.IsFull) c.GraveyardZone.Add(taken); else Generic.AddHandPhase.Invoke(c, taken);
					Generic.ShuffleIntoDeck.Invoke(c.Opponent, s, Entity.FromCard(c.Opponent, s.Card));
				})
			}));
			// [DED_510] Edwin, Defias Kingpin (4) — Battlecry: Draw a card. If you play it this turn, gain +2/+2 and repeat this effect.
			cards.Add("DED_510", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (Generic.Draw(c) is IPlayable drawn && drawn.Zone?.Type == SabberStoneCore.Enums.Zone.HAND) Give(g, s, drawn, "DED_510e");
				})
			}));
			cards.Add("DED_510e", new CardDef(new Power
			{
				Enchant = new Enchant { IsOneTurnEffect = true },
				Trigger = new Trigger(TriggerType.PLAY_CARD)
				{
					TriggerActivation = TriggerActivation.HAND_OR_PLAY,
					TriggerSource = TriggerSource.ENCHANTMENT_TARGET,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || !(e.Creator is Minion edwin) || edwin.Zone?.Type != SabberStoneCore.Enums.Zone.PLAY) return;
						Give(g, edwin, edwin, "DED_510e2");
						if (Generic.Draw(c) is IPlayable next && next.Zone?.Type == SabberStoneCore.Enums.Zone.HAND) Give(g, edwin, next, "DED_510e");
					})
				}
			}));
			cards.Add("DED_510e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DED_510", "stats", 2))) }));
			// [SW_310] Counterfeit Blade (4) — Battlecry: Get a copy of a random friendly Deathrattle minion that died this game.
			cards.Add("SW_310", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var dead = c.DiedThisGameCards.Where(p => p is Minion && p.Card[GameTag.DEATHRATTLE] > 0).ToList();
					if (dead.Count > 0) AddToHand(c, dead[g.Random.Next(dead.Count)].Card);
				})
			}));
			// [SW_311] Garrote (2) — Deal $2 damage to the enemy hero. Shuffle 3 Bleeds into your deck that deal $2 more when drawn.
			cards.Add("SW_311", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("SW_311", "damage", 2), EntityType.OP_HERO, true), new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("SW_311", "bleeds", 3); i++) Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("SW_311t")));
				}))
			}));
			// [SW_311t] Bleed — Casts When Drawn. Deal $2 damage to the enemy hero.
			cards.Add("SW_311t", new CardDef(new Power { TopdeckTask = new DamageTask(V("SW_311t", "damage", 2), EntityType.OP_HERO, true) }));
			// [SW_405] Sketchy Information (4) — Draw a Deathrattle card that costs (4) or less. Trigger its Deathrattle.
			cards.Add("SW_405", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (DrawOne(g, c, p => p.Card[GameTag.DEATHRATTLE] > 0 && p.Cost <= V("SW_405", "cost", 4)) is IPlayable card)
						card.Card.Power?.DeathrattleTask?.Process(g, c, card, null);
				})
			}));
			// [SW_411] SI:7 Informant (4) — Battlecry: Gain +1/+1 for each other SI:7 card you've played this game.
			cards.Add("SW_411", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = c.PlayHistory.Count(h => h.SourceId != s.Id && h.SourceCard != null && IsSI7(h.SourceCard));
					if (n > 0) Generic.AddEnchantmentBlock(g, Cards.FromId("SW_411e"), (IPlayable)s, (IPlayable)s, n, n, 0);
				})
			}));
			cards.Add("SW_411e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(0)) { UseScriptTag = true } }));
			// [SW_417] SI:7 Assassin (7) — Costs (1) less for each SI:7 card you've played this game. Combo: Destroy an enemy minion.
			cards.Add("SW_417", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_FOR_COMBO, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.PlayHistory.Count(h => h.SourceCard != null && IsSI7(h.SourceCard))),
				ComboTask = new DestroyTask(EntityType.TARGET)
			}));

			// ---------------------------------------------------------------- Shaman
			// [SW_035] Charged Call (3) — Discover a @-Cost minion and summon it. (Upgraded for each Overload card you played this game!)
			cards.Add("SW_035", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int cost = s.Card[GameTag.TAG_SCRIPT_DATA_NUM_1] + c.PlayHistory.Count(h => h.SourceCard != null && HasOverloadCard(h.SourceCard));
					new DiscoverTask(CardType.MINION, CardClass.INVALID, (GameTag.COST, RelaSign.EQ, cost), ChoiceAction.SUMMON, null, 1, null).Process(g, c, s, null);
				})
			}));

			// ---------------------------------------------------------------- Warlock
			// [DED_503] Shadowblade Slinger (1) — Battlecry: If you've taken damage this turn, deal that much to an enemy minion.
			cards.Add("DED_503", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m && c[HsTag.HERO_DAMAGE_THIS_TURN] > 0) Generic.DamageCharFunc((IPlayable)s, m, c[HsTag.HERO_DAMAGE_THIS_TURN], false);
				})
			}));
			// [SW_003] Runed Mithril Rod (5) — After you draw 4 cards, reduce the Cost of cards in your hand by (1). Lose 1 Durability.
			cards.Add("SW_003", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DRAW)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						s[GameTag.TAG_SCRIPT_DATA_NUM_1] = s[GameTag.TAG_SCRIPT_DATA_NUM_1] + 1;
						if (s[GameTag.TAG_SCRIPT_DATA_NUM_1] < V("SW_003", "cards", 4)) return;
						s[GameTag.TAG_SCRIPT_DATA_NUM_1] = 0;
						GiveAll(g, s, c.HandZone.ToList(), "SW_003e");
						LoseDurability(s);
					})
				}
			}));
			cards.Add("SW_003e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("SW_003", "cost", 1))) }));
			// [SW_087] Dreaded Mount (3) — Give a minion +1/+1. When it dies, summon an endless Dreadsteed.
			cards.Add("SW_087", MountSpell("SW_087e"));
			cards.Add("SW_087e", MountEnchant("SW_087t", Effects.AttackHealth_N(V("SW_087", "stats", 1))));
			// [SW_087t] Tamsin's Dreadsteed — Deathrattle: At the end of the turn, summon Tamsin's Dreadsteed.
			cards.Add("SW_087t", new CardDef(new Power { DeathrattleTask = new AddEnchantmentTask("SW_087e2", EntityType.CONTROLLER) }));
			cards.Add("SW_087e2", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					EitherTurn = true,
					SingleTask = ComplexTask.Create(new SummonTask("SW_087t"), RemoveEnchantmentTask.Task)
				}
			}));

			// ---------------------------------------------------------------- Warrior
			// [SW_023] Provoke (0) — Tradeable. Choose a friendly minion. Enemy minions attack it.
			cards.Add("SW_023", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion bait)) return;
					foreach (Minion m in c.Opponent.BoardZone.Where(x => !x.Untouchable && x.AttackDamage > 0).ToList())
					{
						if (bait.Zone?.Type != SabberStoneCore.Enums.Zone.PLAY || bait.ToBeDestroyed) break;
						if (m.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY && !m.ToBeDestroyed) EffectAttack(g, m, bait);
					}
				})
			}));

			// ---------------------------------------------------------------- Rogue: Maestra
			// [SW_050] Maestra of the Masquerade (2) — You start the game as a different class until you play a Rogue card. (User
			// 2026-10-06: a random other class, its hero and Hero Power. Start of Game from the deck: the hero becomes that class's
			// hero, SW_050e "Disguise" keeps the real hero and Hero Power; playing a Rogue card brings them back.)
			// (2026-10-08, live vfx-decks 85210 / 85750: the hero is BUILT disguised — CREATE_GAME shows the other class's hero — and the reveal
			// makes a new real hero: Actions/Disguise.cs. SW_050e on the player is the revealer.)
			cards.Add("SW_050", new CardDef(new Power { StartsDisguisedWith = "SW_050e" }));
			cards.Add("SW_050e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.Card.HasClass(CardClass.ROGUE)),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Enchantment e) Generic.RevealDisguise(g, c, e, "SW_050");
					})
				}
			}));
		}
	}
}
