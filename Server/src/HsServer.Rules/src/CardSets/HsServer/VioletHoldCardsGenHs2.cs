// HsServer addition (not upstream SabberStone): Escape from Violet Hold (VIOLET_HOLD, set 1988) — chunk 2: Mage, Druid, Hunter (spec:
// research spec_chunk2). Shared pieces: Prepare (Actions/Prepare.cs), Generic.RevealFromDeck ("Reveal a X in your deck"), a transformed
// attacker leaves the fight (Attack.cs), HsTag.SPELL_CASTS_TWICE (CastSpell: "casts twice when played"), RandomMinionOfCost with a filter.
// Every number goes through CardValues (V).
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
		/// <summary>Client card tag CHEF_NETHREK_REQUIREMENT (4835): the deck rule's Cost (the deck builder: "deck has a card costing ≥ N").</summary>
		private const GameTag CHEF_NETHREK_REQUIREMENT = (GameTag)4835;

		/// <summary>"If you have 10 or more Mana" (Spiteful Chef, Lethal Recipe): Mana Crystals (a 3-Cost card can't leave 10 after paying;
		/// Manathirst's wording, wiki: "only permanent mana crystals").</summary>
		private static bool TenMana(Controller c, string id) => c.BaseMana >= V(id, "mana", 10);

		/// <summary>"If you've cast a spell this turn" (wiki Cast spell: only the player's own plays count).</summary>
		private static bool CastSpellThisTurn(Game g, Controller c, IEntity self) =>
			c.PlayHistory.Any(h => h.Turn == g.Turn && h.SourceCard?.Type == CardType.SPELL && h.SourceId != self?.Id);

		/// <summary>"Give your hero +N Attack this turn. Gain N Armor. Add X to your hand." (Widow's Bite and its upgrades)</summary>
		private static CardDef WidowsBite(string id, string enchantment, string next, int armor) => new CardDef(new Power
		{
			PowerTask = ComplexTask.Create(new AddEnchantmentTask(enchantment, EntityType.HERO), new ArmorTask(V(id, "armor", armor)),
				next != null ? new AddCardTo(next, EntityType.HAND) : Nothing)
		});

		/// <summary>"Deal N damage split among all enemies" (Arcane Tripwire and its Tripped copy: one at a time, random; Spell Damage adds hits
		/// once — wiki patch 2026-07-06).</summary>
		private static ISimpleTask ArcaneTrip(string id) => new EnqueueTask(V(id, "damage", 4), ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, 1), true);

		/// <summary>"Shuffle 2 spells into your deck that do it again when drawn" (the Tripwires).</summary>
		private static ISimpleTask ShuffleTripped(string id, string tripped) => new CustomTask((g, c, s, t, stack) =>
		{
			for (int i = 0; i < V(id, "spells", 2); i++)
				Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId(tripped), new EntityData { { GameTag.CREATOR, s.Id } }));
		});

		/// <summary>The Skeleton Key's offer: 3 spells (class or neutral) and "Transform the Key!", again after each refresh (live L4: up to the
		/// player; 20% to take 5 damage with no source — wiki: "the damage source is external to the spell").</summary>
		private static void SkeletonKeyOffer(Controller c, IEntity key)
		{
			Game g = c.Game;
			Card self = Cards.FromId("JAIL_319");
			var spells = DiscoverFromTask.PickDistinct(g, Cards.FormatTypeCards(g.FormatType).Where(x => x.Type == CardType.SPELL && x.Id != self.Id &&
				(x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL))), V("JAIL_319", "spells", 3));
			Card[] offer = spells.Append(Cards.FromId("JAIL_319t")).ToArray();
			Generic.CreateChoiceCards.Invoke(c, key, null, ChoiceType.GENERAL, ChoiceAction.STACK, offer, new CustomTask((g2, c2, s2, pick, st) =>
			{
				if (pick == null) return;
				pick.Zone?.Remove(pick);
				if (pick.Card.Id != "JAIL_319t")
				{
					Generic.AddHandPhase.Invoke(c2, pick);
					return;
				}
				if (g2.Random.Next(100) < V("JAIL_319", "chance", 20))
					Generic.NamedFx((IPlayable)s2, "BuffImpact_Hero", new[] { c2.Hero.Id }, () =>
					{
						g2.RulesDamage = true;
						try { Generic.DamageCharFunc(c2.Hero, c2.Hero, V("JAIL_319", "damage", 5), false); }
						finally { g2.RulesDamage = false; }
					});
				c2.GraveyardZone.Add(pick);
				if (!c2.Hero.IsDead && c2.Hero.Health > 0) SkeletonKeyOffer(c2, s2);
			}));
		}

		private static void VioletHoldChunk2(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Mage
			// [JAIL_122] Jailhouse Manastorm (5) — Battlecry: After you cast a spell this game, summon a random minion of the same Cost.
			// (each one stacks; the Cost it was played for; spells an effect casts don't count — Cast_spell.txt; after the spell: patch
			// 2026-07-06 "summons minions after a spell clears space on a full board")
			cards.Add("JAIL_122", new CardDef(new Power { PowerTask = new AddEnchantmentTask("JAIL_122e5", EntityType.CONTROLLER) }));
			cards.Add("JAIL_122e5", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(t is Spell spell)) return;
						PlayHistoryEntry played = c.PlayHistory.LastOrDefault(h => h.SourceId == spell.Id && h.Turn == g.Turn);
						if (played.SourceCard == null) return;
						Summon(g, c, RandomMinionOfCost(g, played.Cost), s);
					})
				}
			}));
			// [JAIL_123] Breakout Architect (5) — Battlecry: Discover a spell that costs (5) or more. It casts twice when played.
			cards.Add("JAIL_123", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL && x.Cost >= V("JAIL_123", "cost", 5), "JAIL_123e")
			}));
			cards.Add("JAIL_123e", new CardDef(new Power { Enchant = new Enchant(new Effect(HsTag.SPELL_CASTS_TWICE, EffectOperator.SET, 1)) }));
			// [JAIL_125] Cold Snap (1) — Freeze an enemy. Get a random Frost spell. (any class: live gave a Mage one)
			cards.Add("JAIL_125", new CardDef(TargetEnemy, new Power
			{
				PowerTask = ComplexTask.Create(ComplexTask.Freeze(EntityType.TARGET),
					new CustomTask((g, c, s, t, stack) => GetCard(c, RandomCard(g, IsFrostCard), s)))
			}));
			// [JAIL_312] Contraband Wands (2) — Get 3 Arcane Missiles.
			cards.Add("JAIL_312", new CardDef(new Power { PowerTask = new AddCardTo("EX1_277", EntityType.HAND, V("JAIL_312", "missiles", 3)) }));
			// [JAIL_313] Bootleg Alchemist (5) — Battlecry: Choose a card in your hand. Transform it into a spell that costs (5) more (keeps its
			// original Cost). (its Cost as it is in hand — doubt; none of that Cost: nothing)
			cards.Add("JAIL_313", new CardDef(new Power
			{
				HandTarget = SelfCondition.AnyCard,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t?.Zone?.Type != Zone.HAND || t.Controller != c) return;
					int cost = t.Cost;
					if (!(RandomCard(g, x => x.Type == CardType.SPELL && x.Cost == cost + V("JAIL_313", "more", 5)) is Card spell)) return;
					IPlayable changed = Generic.ChangeEntityBlock(c, t, spell, true);
					Give(g, s, changed, "JAIL_313e", cost);
				})
			}));
			cards.Add("JAIL_313e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) { UseScriptTag = true } }));
			// [JAIL_315] Mystic Misdirection (3) — Secret: When an enemy minion attacks, transform it into a 1/1 Sheep. (the attack ends: Attack.cs;
			// live L1 73868: the Secret's trigger has effect index 1)
			cards.Add("JAIL_315", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.OP_MINIONS,
					EffectIndex = 1,
					SingleTask = ComplexTask.Secret(new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventSource is Minion attacker && attacker.Zone?.Type == Zone.PLAY)
							Generic.TransformBlock.Invoke(attacker.Controller, Cards.FromId("CS2_tk1"), attacker);
					}))
				}
			}));
			// [JAIL_319] The Skeleton Key (1) — Discover a spell, or refresh your options (20% chance to take 5 damage each refresh!)
			cards.Add("JAIL_319", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => SkeletonKeyOffer(c, s)) }));
			cards.Add("JAIL_319t", new CardDef(new Power { PowerTask = Nothing }));
			// [JAIL_321] Tricksy Improviser (5) — Prepare. Battlecry: If you've cast a spell this turn, cast two random Mage Secrets.
			cards.Add("JAIL_321", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => CastSpellThisTurn(g, c, s),
					new CustomTask((g, c, s, t, stack) => Generic.CastRandomSecrets(c, s, x => x.HasClass(CardClass.MAGE), V("JAIL_321", "secrets", 2))))
			}));
			// [JAIL_379] Spire Security (3) — Battlecry: Reveal a spell in your deck. If it costs (5) or more, deal 5 damage split among enemy minions.
			cards.Add("JAIL_379", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) =>
						stack.Flag = Generic.RevealFromDeck(c, (IPlayable)s, p => p is Spell) is IPlayable shown && shown.Cost >= V("JAIL_379", "cost", 5)),
					new FlagTask(true, new EnqueueTask(V("JAIL_379", "damage", 5), ComplexTask.DamageRandomTargets(1, EntityType.OP_MINIONS, 1))))
			}));
			// [JAIL_735] Code Violet (7) — Prepare. Summon an 8-Cost minion. If you've cast 3 other spells this turn, do it again. (random: the
			// wiki's pool of all 8-Cost minions)
			cards.Add("JAIL_735", new CardDef(new Power
			{
				ThresholdCount = p => p.Controller.PlayHistory.Count(h => h.Turn == p.Game.Turn && h.SourceCard?.Type == CardType.SPELL && h.SourceId != p.Id),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int times = Generic.ThresholdMet((IPlayable)s) ? 2 : 1;
					for (int i = 0; i < times; i++) Summon(g, c, RandomMinionOfCost(g, V("JAIL_735", "cost", 8)), s);
				})
			}));

			// ---------------------------------------------------------------- Druid
			// [JAIL_200] Infest the Scullery (5) — Summon two random @-Cost minions. (Improved by your hero attacks this game.)
			cards.Add("JAIL_200", new CardDef(new Power
			{
				HandCounter = p => p.Card[GameTag.TAG_SCRIPT_DATA_NUM_1] + p.Controller[HsTag.HERO_ATTACKS_THIS_GAME],
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int cost = s.Card[GameTag.TAG_SCRIPT_DATA_NUM_1] + c[HsTag.HERO_ATTACKS_THIS_GAME];
					for (int i = 0; i < V("JAIL_200", "minions", 2); i++) Summon(g, c, RandomMinionOfCost(g, cost), s);
				})
			}));
			// [JAIL_201] Secret Ingredient (0) — Choose One - Give your hero +2 Attack this turn; or get a random Druid card.
			cards.Add("JAIL_201a", new CardDef(new Power { PowerTask = new AddEnchantmentTask("JAIL_201ae1", EntityType.HERO) }));
			cards.Add("JAIL_201ae1", OneTurn(Effects.Attack_N(V("JAIL_201", "attack", 2))));
			cards.Add("JAIL_201b", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => GetCard(c, RandomCard(g, x => x.HasClass(CardClass.DRUID)), s)) }));
			// [JAIL_202] Spiderling (1) — Your hero has +1 Attack on your turn. (live L1: JAIL_202e on the hero at the owner's turn start)
			cards.Add("JAIL_202", new CardDef(new Power { Aura = new Aura(AuraType.HERO, "JAIL_202e") { Condition = SelfCondition.IsCurrentPlayer, Restless = true } }));
			cards.Add("JAIL_202e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("JAIL_202", "attack", 1))) }));
			// [JAIL_436] Widow's Bite (2) / [JAIL_436t] Widow's Feast (2) / [JAIL_436t2] Widow's Banquet (2) — Give your hero +N Attack this turn.
			// Gain N Armor. Add the next one to your hand.
			cards.Add("JAIL_436", WidowsBite("JAIL_436", "JAIL_436e", "JAIL_436t", 1));
			cards.Add("JAIL_436t", WidowsBite("JAIL_436t", "JAIL_436e2", "JAIL_436t2", 2));
			cards.Add("JAIL_436t2", WidowsBite("JAIL_436t2", "JAIL_436e3", null, 4));
			cards.Add("JAIL_436e", OneTurn(Effects.Attack_N(V("JAIL_436", "attack", 1))));
			cards.Add("JAIL_436e2", OneTurn(Effects.Attack_N(V("JAIL_436t", "attack", 2))));
			cards.Add("JAIL_436e3", OneTurn(Effects.Attack_N(V("JAIL_436t2", "attack", 4))));
			// [JAIL_507] Spiteful Chef (3) — Battlecry: Summon a 2-Cost Taunt minion. If you have 10 or more Mana, summon a 6-Cost instead.
			// (a generated minion: random, the wiki's pools)
			cards.Add("JAIL_507", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => TenMana(c, "JAIL_507"),
					new CustomTask((g, c, s, t, stack) => Summon(g, c, RandomMinionOfCost(g, V("JAIL_507", "big", 6), x => x[GameTag.TAUNT] > 0), s)),
					new CustomTask((g, c, s, t, stack) => Summon(g, c, RandomMinionOfCost(g, V("JAIL_507", "small", 2), x => x[GameTag.TAUNT] > 0), s)))
			}));
			// [JAIL_860] Chef Neth'rek (3) — Start of Game: If your deck only has cards that cost (3) or less, set your Mana to 10 after five turns!
			// (the rule's Cost is client data: CHEF_NETHREK_REQUIREMENT; wiki: "sets the player's mana to 10 after they end their 5th turn")
			cards.Add("JAIL_860", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.GAME_START)
				{
					TriggerActivation = TriggerActivation.DECK,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						int limit = s.Card[CHEF_NETHREK_REQUIREMENT] > 0 ? s.Card[CHEF_NETHREK_REQUIREMENT] : V("JAIL_860", "cost", 4);
						// HsServer (2026-10-08): the deck it started with — Start_of_Game.txt: "triggers before the mulligan … nothing the player does during the mulligan can affect whether or not any Start of Game effects will trigger" (our trigger resolves after the opening draw)
						if (c.DeckCards.All(x => x.Cost < limit))
							new AddEnchantmentTask("JAIL_860e", EntityType.CONTROLLER).Process(g, c, s, null);
					}),
					RemoveAfterTriggered = true
				}
			}));
			cards.Add("JAIL_860e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || g.CurrentPlayer != c) return;
						e[HsTag.CHEF_TURNS_ENDED] = e[HsTag.CHEF_TURNS_ENDED] + 1;
						if (e[HsTag.CHEF_TURNS_ENDED] < V("JAIL_860", "turns", 5)) return;
						c.BaseMana = V("JAIL_860", "mana", 10);
						RemoveEnchantmentTask.Task.Process(g, c, e, null);
					})
				}
			}));
			// [JAIL_861] Noxious Bribe (2) — Discover a Choose One card. It has both effects combined. Give your opponent a plain copy.
			cards.Add("JAIL_861", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.ChooseOne && x[GameTag.CHOOSE_BOTH] == 0, "JAIL_861e",
					new CustomTask((g, c, s, t, stack) => { if (t != null) GetCard(c.Opponent, t.Card, s); }))
			}));
			cards.Add("JAIL_861e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.CHOOSE_BOTH, EffectOperator.SET, 1)) }));
			// [JAIL_866] Lethal Recipe (3) — Draw 2 minions. If you have 10 or more Mana, give them +3/+3.
			cards.Add("JAIL_866", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => TenMana(c, "JAIL_866"), LethalRecipe(true), LethalRecipe(false))
			}));
			cards.Add("JAIL_866e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("JAIL_866", "stats", 3))) }));
			// [JAIL_872] Spider Rider (2) — After your hero attacks, draw a card.
			cards.Add("JAIL_872", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = new DrawTask() }
			}));
			// [JAIL_875] Staff of Trickery (4) — After your hero attacks, Discover a Druid card. Reduce its Cost by your hero's Attack. (Druid cards
			// only: the text names the class)
			cards.Add("JAIL_875", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = DiscoverFromTask.Where((c, x) => x.HasClass(CardClass.DRUID), null,
						new CustomTask((g, c, s, t, stack) => { if (t != null && c.Hero.AttackDamage > 0) Give(g, s, t, "JAIL_875e", c.Hero.AttackDamage); }))
				}
			}));
			cards.Add("JAIL_875e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(0)) { UseScriptTag = true } }));

			// ---------------------------------------------------------------- Hunter
			// [JAIL_380] Smuggled Shovel (1, weapon) — Deathrattle: Draw a spell that didn't start in your deck. (live L7: Deathrattle index 1)
			cards.Add("JAIL_380", new CardDef(new Power
			{
				DeathrattleEffectIndex = 1,
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => DrawOne(g, c, p => p is Spell && Generic.DidntStartInDeck(p)))
			}));
			// [JAIL_395] Sewer Swimmer (4) — Prepare. Battlecry: Trigger a friendly minion's Deathrattle.
			cards.Add("JAIL_395", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_DEATHRATTLE, 0 } },
				new Power { PowerTask = new ActivateDeathrattleTask(EntityType.TARGET) }));
			// [JAIL_876] Dig for Freedom (4) — Give a friendly minion "Deathrattle: Summon two random 4-Cost minions."
			cards.Add("JAIL_876", new CardDef(TargetFriendlyMinion, new Power { PowerTask = new AddEnchantmentTask("JAIL_876e", EntityType.TARGET) }));
			cards.Add("JAIL_876e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("JAIL_876", "minions", 2); i++) Summon(g, c, RandomMinionOfCost(g, V("JAIL_876", "cost", 4)), s);
				})
			}));
			// [JAIL_877] Underbelly Network (2, Location) — Summon a 2/1 Rat with "Deathrattle: Draw a card." (live L8: its recorded burrowing
			// effect around the rat, Source = the location)
			cards.Add("JAIL_877", new CardDef(new Power
			{
				OwnFx = true,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.BoardZone.IsFull) return;
					var rat = (Minion)Entity.FromCard(c, Cards.FromId("JAIL_877t"));
					Generic.NamedFx((IPlayable)s, "SpawnToHand_Burrowing", null, () => Generic.SummonBlock.Invoke(g, rat, -1, s));
				})
			}));
			cards.Add("JAIL_877t", new CardDef(new Power { DeathrattleTask = new DrawTask() }));
			// [JAIL_878] Guard Dog (2) — Deathrattle: Summon a random 1-Cost Deathrattle minion.
			cards.Add("JAIL_878", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
					Summon(g, c, RandomMinionOfCost(g, V("JAIL_878", "cost", 1), x => x[GameTag.DEATHRATTLE] > 0), s))
			}));
			// [JAIL_879] Beast Tripwire (4) — Summon a random 5-Cost Beast. Shuffle 2 spells into your deck that do it again when drawn.
			cards.Add("JAIL_879", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => Summon(g, c, RandomMinionOfCost(g, V("JAIL_879", "cost", 5), x => x.IsRace(Race.BEAST)), s)),
					ShuffleTripped("JAIL_879", "JAIL_879t"))
			}));
			cards.Add("JAIL_879t", new CardDef(new Power
			{
				TopdeckTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, RandomMinionOfCost(g, V("JAIL_879", "cost", 5), x => x.IsRace(Race.BEAST)), s))
			}));
			// [JAIL_880] Black Market Overseer (1) — Whenever you play a Deathrattle minion, give it Rush.
			cards.Add("JAIL_880", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = SelfCondition.IsDeathrattleMinion,
					SingleTask = new AddEnchantmentTask("JAIL_880e", EntityType.TARGET)
				}
			}));
			cards.Add("JAIL_880e", new CardDef(new Power { Enchant = new Enchant(Effects.Rush) }));
			// [JAIL_881] Arcane Tripwire (3) — Deal $4 damage split among all enemies. Shuffle 2 spells into your deck that do it again when drawn.
			cards.Add("JAIL_881", new CardDef(new Power { PowerTask = ComplexTask.Create(ArcaneTrip("JAIL_881"), ShuffleTripped("JAIL_881", "JAIL_881t")) }));
			cards.Add("JAIL_881t", new CardDef(new Power { TopdeckTask = ArcaneTrip("JAIL_881t") }));
			// [JAIL_882] R4T-C4TCH3R (3) — Battlecry: Copy all spells in your deck. Deathrattle: Draw one. (copies shuffled in, the deck limit
			// stops them — wiki patch 36.4.0; "one" = a spell, any)
			cards.Add("JAIL_882", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable spell in c.DeckZone.Where(p => p is Spell).ToList())
					{
						if (c.DeckZone.IsFull) break;
						Generic.Copy(c, s, spell, Zone.DECK);
					}
				}),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => DrawOne(g, c, p => p is Spell))
			}));
		}

		/// <summary>Lethal Recipe: draw 2 minions; buffed with JAIL_866e when <paramref name="buff"/>.</summary>
		private static ISimpleTask LethalRecipe(bool buff) => new CustomTask((g, c, s, t, stack) =>
		{
			for (int i = 0; i < V("JAIL_866", "minions", 2); i++)
				if (DrawOne(g, c, p => p is Minion) is IPlayable drawn && buff) Give(g, s, drawn, "JAIL_866e");
		});
	}
}
