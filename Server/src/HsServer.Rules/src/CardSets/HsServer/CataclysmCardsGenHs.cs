// HsServer addition (not upstream SabberStone): CATACLYSM (set 1980) — chunk 1: neutrals, Death Knight, Demon Hunter (spec: research
// spec_chunk1). Shared pieces: Generic.Herald (Actions/Herald.cs), Shatter (Actions/Shatter.cs), Power.WhenSummonedTask (limbs and
// Soldiers), Colossal limbs t/t1 (Actions/Colossal.cs), AuraType.OP_BOARD, Power.ExtraDamageTaken / HealthLossToMaxHealth /
// AdjacentInHand / HandTransform, HsTag.KILLED_BY, Twisted Monstrosity's keyword swap (Actions/Cataclysm.cs). Every number goes through
// CardValues (V); a limb's or Soldier's number is its own TAG_SCRIPT_DATA_NUM_1 (Herald-upgraded).
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
		private const int FEL_SCHOOL = 7; // client TAG_SPELL_SCHOOL

		/// <summary>A Herald card's Herald (Generic.Herald) as a task.</summary>
		private static ISimpleTask HeraldTask => new CustomTask((g, c, s, t, stack) => Generic.Herald(c, (IPlayable)s));

		/// <summary>A new card in the hand; the entity, or null when it burned / none.</summary>
		private static IPlayable GetCard(Controller c, Card card, IEntity source)
		{
			if (card == null) return null;
			IPlayable p = Entity.FromCard(c, card, new EntityData { { GameTag.CREATOR, source.Id }, { GameTag.DISPLAYED_CREATOR, source.Id } });
			return Generic.AddHandPhase.Invoke(c, p) ? p : null;
		}

		/// <summary>"When summoned, give your hero +{0} Attack this turn." (Azshara's Tentacles, Soldier of Azshara: CATA_151te)</summary>
		private static ISimpleTask TentacleSummoned => new CustomTask((g, c, s, t, stack) =>
			Generic.AddEnchantmentBlock(g, Cards.FromId("CATA_151te"), (IPlayable)s, c.Hero, s[GameTag.TAG_SCRIPT_DATA_NUM_1], 0, 0));

		/// <summary>"When summoned, get a random {0}-Cost minion. It costs Health this turn." (Onyxia's Wings, Soldier of Onyxia)</summary>
		private static ISimpleTask WingSummoned => new CustomTask((g, c, s, t, stack) =>
		{
			int cost = s[GameTag.TAG_SCRIPT_DATA_NUM_1];
			if (GetCard(c, RandomCard(g, x => x.Type == CardType.MINION && x.Cost == cost), s) is IPlayable got) Give(g, s, got, "CATA_155e2");
		});

		private static void Cataclysm(IDictionary<string, CardDef> cards)
		{
			CataclysmChunk1(cards);
			CataclysmChunk2(cards); // CataclysmCardsGenHs2.cs
			CataclysmChunk3(cards); // CataclysmCardsGenHs3.cs
			CataclysmChunk4(cards); // CataclysmCardsGenHs4.cs
		}

		private static void CataclysmChunk1(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Neutral
			// [CATA_111] Darkscale Broodmother (3) — Battlecry: If you're holding a Dragon, refresh 2 Mana Crystals.
			cards.Add("CATA_111", new CardDef(new Power
			{
				GlowWhile = p => HoldingDragon(p.Controller, p),
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (HoldingDragon(c, s)) RefreshMana(c, V("CATA_111", "refresh", 2)); })
			}));
			// [CATA_180] War'loc (2) — Battlecry: Your next Murloc that costs (3) or less costs Health instead of Mana.
			cards.Add("CATA_180", new CardDef(new Power { PowerTask = new AddEnchantmentTask("CATA_180e", EntityType.CONTROLLER) }));
			cards.Add("CATA_180e", NextCardCost(new Effect(GameTag.CARD_COSTS_HEALTH, EffectOperator.SET, 1),
				new SelfCondition(p => p is Minion && p.Card.IsRace(Race.MURLOC) && p.Cost <= V("CATA_180", "cost", 3)), "CATA_180e"));
			// [CATA_185] Faceless Replicator (3) — Elusive. Deathrattle: Transform the minion that killed this into a Faceless Replicator.
			// (HsTag.KILLED_BY: the minion whose damage or destroy effect killed it; nothing if it is gone too)
			cards.Add("CATA_185", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (g.IdEntityDic.TryGetValue(s[HsTag.KILLED_BY], out IPlayable p) && p is Minion killer && killer.Zone?.Type == Zone.PLAY &&
					    !killer.IsDead && !killer.ToBeDestroyed)
						new TransformTask(((IPlayable)s).Card, EntityType.TARGET).Process(g, c, s, killer);
				})
			}));
			// [CATA_186] Stickybomb Saboteur (4) — Battlecry: Give your opponent a 2-Cost Sabotage. Cards next to it cost (1) more.
			cards.Add("CATA_186", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => GetCard(c.Opponent, Cards.FromId("CATA_186t"), s)) }));
			cards.Add("CATA_186t", new CardDef(new Power { AdjacentInHand = "CATA_186te" }));
			cards.Add("CATA_186te", new CardDef(new Power { Enchant = new Enchant(Effects.AddCost(V("CATA_186t", "more", 1))) }));
			// [CATA_206] Twisted Monstrosity (5) — {0}, {1}. Each turn this is in your hand, swap between two random Bonus Effects.
			// (Generic.SwapDynamicKeywords, at the end of its owner's turns)
			cards.Add("CATA_206", new CardDef(new Power { NoScript = true }));
			// [CATA_208] Selfless Protector (2) — Taunt. Takes one extra damage from all sources.
			cards.Add("CATA_208", new CardDef(new Power { ExtraDamageTaken = V("CATA_208", "extra", 1) }));
			// [CATA_209] Battlefield Blaster (3) — Battlecry: Choose a spell in your hand to give Spell Damage +1.
			cards.Add("CATA_209", new CardDef(new Power
			{
				HandTarget = SelfCondition.IsSpell,
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is Spell spell && spell.Zone?.Type == Zone.HAND) Give(g, s, spell, "CATA_209e"); })
			}));
			cards.Add("CATA_209e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.SPELLPOWER, EffectOperator.ADD, V("CATA_209", "spell damage", 1))) }));
			// [CATA_210] Twilight Egg (1) — Deathrattle: Summon a {0}/{1} Whelp. (Gains +1/+1 at the start of your turns!) (wiki: the Egg's
			// Deathrattle grows, the Whelp doesn't)
			cards.Add("CATA_210", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						s[GameTag.TAG_SCRIPT_DATA_NUM_1] = s[GameTag.TAG_SCRIPT_DATA_NUM_1] + V("CATA_210", "growth", 1);
						s[GameTag.TAG_SCRIPT_DATA_NUM_2] = s[GameTag.TAG_SCRIPT_DATA_NUM_2] + V("CATA_210", "growth", 1);
					})
				},
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
					SummonWithStats(g, c, Cards.FromId("CATA_210t"), s[GameTag.TAG_SCRIPT_DATA_NUM_1], s[GameTag.TAG_SCRIPT_DATA_NUM_2], s))
			}));
			// [CATA_213] Vyranoth (6) — Battlecry: If the total Cost of your starting minions was 100, split 100 stats among minions in your
			// deck. (at least 100 — the deck builder's bar; each point a random deck minion's +1 Attack or Health — doubt)
			Func<Controller, int> startingMinionCost = c => c.DeckCards.Where(x => x.Type == CardType.MINION).Sum(x => x.Cost);
			cards.Add("CATA_213", new CardDef(new Power
			{
				GlowWhile = p => startingMinionCost(p.Controller) >= V("CATA_213", "total", 100),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (startingMinionCost(c) < V("CATA_213", "total", 100)) return;
					var minions = c.DeckZone.Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.Count == 0) return;
					var atk = new Dictionary<int, int>();
					var health = new Dictionary<int, int>();
					for (int i = 0; i < V("CATA_213", "stats", 100); i++)
					{
						IPlayable m = minions[g.Random.Next(minions.Count)];
						var bag = g.Random.Next(2) == 0 ? atk : health;
						bag[m.Id] = (bag.TryGetValue(m.Id, out int n) ? n : 0) + 1;
					}
					foreach (IPlayable m in minions)
					{
						if (atk.TryGetValue(m.Id, out int a)) Give(g, s, m, "CATA_213e", a);
						if (health.TryGetValue(m.Id, out int h)) Give(g, s, m, "CATA_213e2", h);
					}
				})
			}));
			cards.Add("CATA_213e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0)) { UseScriptTag = true } }));
			cards.Add("CATA_213e2", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(0)) { UseScriptTag = true } }));
			// [CATA_476] Bronze Keeper (8) — At the end of your turn, summon a 6/6 Elemental Dragon with Divine Shield.
			cards.Add("CATA_476", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("CATA_476t"), s)) }
			}));
			// [CATA_497] Ultraxion (6) — Battlecry: Herald {0}. Reduce Deathwing's Cost by ({1}). (Herald to improve!) ({1} = 1 + your Heralds;
			// "Deathwing" = cards whose name starts with it — wiki: "any card where the character is Deathwing")
			cards.Add("CATA_497", new CardDef(new Power
			{
				HandCounter = p => V("CATA_497", "base", 1) + p.Controller[Generic.HERALD_COLOSSAL_AMOUNT],
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = V("CATA_497", "base", 1) + c[Generic.HERALD_COLOSSAL_AMOUNT];
					Generic.Herald(c, (IPlayable)s);
					// (one player enchantment per point: each a hand aura of (1) less on the Deathwing cards — live: one CATA_497e with
					// TAG_SCRIPT_DATA_NUM_1 = n; doubt)
					for (int i = 0; i < n; i++) Generic.AddEnchantmentBlock(g, Cards.FromId("CATA_497e"), (IPlayable)s, c, n, 0, 0);
				})
			}));
			cards.Add("CATA_497e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(1))
				{
					Condition = new SelfCondition(p => (p is Minion || p is Hero) && p.Card.Name?.StartsWith("Deathwing", StringComparison.Ordinal) == true)
				}
			}));
			// [CATA_556] Carrier Whelp (1) — Battlecry: Get a random Dragon that costs (3) or less.
			cards.Add("CATA_556", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GetCard(c, RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.DRAGON) && x.Cost <= V("CATA_556", "cost", 3)), s))
			}));
			// [CATA_612] Frostbitten Imp (2) — Battlecry: Freeze this.
			cards.Add("CATA_612", new CardDef(new Power { PowerTask = ComplexTask.Freeze(EntityType.SOURCE) }));
			// [CATA_613] Survivalist (9) — Has Immune while you control no other minions.
			cards.Add("CATA_613", new CardDef(new Power
			{
				Aura = new Aura(AuraType.SELF, new Effect(GameTag.IMMUNE, EffectOperator.SET, 1))
				{
					Condition = new SelfCondition(p => Generic.Soloist(p.Controller, p)), Restless = true
				}
			}));
			// [CATA_614] Shadowed Informant (2) — Battlecry: Discover a spell from your class / a {0} spell. (Swaps class each turn!) (the class
			// in TAG_SCRIPT_DATA_NUM_1, re-rolled at the start of your turns while in hand; class cards only)
			CardClass[] classes = { CardClass.DEATHKNIGHT, CardClass.DEMONHUNTER, CardClass.DRUID, CardClass.HUNTER, CardClass.MAGE, CardClass.PALADIN,
				CardClass.PRIEST, CardClass.ROGUE, CardClass.SHAMAN, CardClass.WARLOCK, CardClass.WARRIOR };
			cards.Add("CATA_614", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					TriggerActivation = TriggerActivation.HAND,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is IPlayable me && me.Zone?.Type == Zone.HAND) me[GameTag.TAG_SCRIPT_DATA_NUM_1] = (int)classes[g.Random.Next(classes.Length)];
					})
				},
				PowerTask = new DiscoverFromTask((g, c, s) =>
				{
					CardClass cls = s[GameTag.TAG_SCRIPT_DATA_NUM_1] > 0 ? (CardClass)s[GameTag.TAG_SCRIPT_DATA_NUM_1] : c.HeroClass;
					return Cards.GenerationPool(g, c).Where(x => x.Type == CardType.SPELL && x.HasClass(cls) && !x.HasClass(CardClass.NEUTRAL));
				})
			}));
			// [CATA_615] Genn, Cursed King (4) — While holding this, if the rest of your hand is all even or all odd, transform into the 6/5
			// Worgen King. (an empty rest counts — doubt)
			cards.Add("CATA_615", new CardDef(new Power
			{
				HandTransform = ((Func<IPlayable, bool>)(p =>
				{
					var rest = p.Controller.HandZone.Where(x => x.Id != p.Id).ToList();
					return rest.All(x => x.Cost % 2 == 0) || rest.All(x => x.Cost % 2 == 1);
				}), "CATA_615t")
			}));
			// [CATA_615t] Genn, Worgen King (4) — Battlecry: Upgrade your starting Hero Power. It costs (1).
			cards.Add("CATA_615t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card starting = Cards.FromAssetId(c.Hero.Card[GameTag.HERO_POWER]) ?? c.Hero.HeroPower?.Card;
					if (starting != null && Cards.FromAssetId(ReprintCards.UpgradedHeroPower(starting, c.Hero.Card)) is Card upgraded)
						new ReplaceHeroPower(upgraded).Process(g, c, s, null);
					Generic.AddEnchantmentBlock(g, Cards.FromId("CATA_615e"), (IPlayable)s, c, 0, 0, 0);
				})
			}));
			cards.Add("CATA_615e", new CardDef(new Power { Aura = new Aura(AuraType.HEROPOWER, Effects.SetCost(V("CATA_615t", "cost", 1))) }));
			// [CATA_616] Gronn Giant (9) — This minion's Cost is reduced by the Cost of the last card you played. (the Cost it was played for)
			cards.Add("CATA_616", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.PlayHistory.Count == 0 ? 0 : Math.Max(0, p.Controller.PlayHistory[p.Controller.PlayHistory.Count - 1].Cost))
			}));
			// [CATA_720] Warmaster Blackhorn (7) — Battlecry: Destroy all cards that cost (2) or less in both players' decks.
			cards.Add("CATA_720", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Controller p in new[] { c, c.Opponent })
						foreach (IPlayable card in p.DeckZone.Where(x => x.Cost <= V("CATA_720", "cost", 2)).ToList())
							Generic.DeckCardToGraveyard(card, s.Id, suppressAnimation: true);
				})
			}));
			// [CATA_721] Sheltered Survivor (2) — Battlecry: Choose a card in your hand to shuffle into your deck. Draw a card.
			cards.Add("CATA_721", new CardDef(new Power
			{
				HandTarget = SelfCondition.AnyCard,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t?.Zone?.Type == Zone.HAND && t.Controller == c) Generic.MoveToDeck(c, s, t);
					Generic.Draw(c);
				})
			}));
			// [CATA_722] Envoy of the End (5) — Taunt. Battlecry: Herald {0}.
			cards.Add("CATA_722", new CardDef(new Power { PowerTask = HeraldTask }));
			// [CATA_723] Drakeadon Mongrel (7) — Deathrattle: Summon two random 4-Cost minions.
			cards.Add("CATA_723", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("CATA_723", "minions", 2); i++) Summon(g, c, RandomMinionOfCost(g, V("CATA_723", "cost", 4)), s);
				})
			}));
			// [CATA_897] Gemstone Hoarder (3) — Battlecry: Choose a card in your hand to discard. Deathrattle: Get it back. It costs (1) less.
			// (a new card of it — the discard took its enchantments)
			cards.Add("CATA_897", new CardDef(new Power
			{
				HandTarget = SelfCondition.AnyCard,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t?.Zone?.Type != Zone.HAND || t.Controller != c) return;
					s[HsTag.REMEMBERED_CARD] = t.Card.AssetId;
					Generic.DiscardBlock.Invoke(c, t);
				}),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (s[HsTag.REMEMBERED_CARD] > 0 && GetCard(c, Cards.FromAssetId(s[HsTag.REMEMBERED_CARD]), s) is IPlayable back)
						GiveCostLess(g, s, back, V("CATA_897", "less", 1));
				})
			}));
			// [CATA_898] Scaled Lancer (4) — All enemy minions have Taunt.
			cards.Add("CATA_898", new CardDef(new Power { Aura = new Aura(AuraType.OP_BOARD, "CATA_898e") }));
			cards.Add("CATA_898e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.TAUNT, EffectOperator.SET, 1)) }));
			// [CATA_999] Earthen Drake (5) — At the end of your turn, deal 4 damage to the enemy hero.
			cards.Add("CATA_999", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) => Generic.DamageCharFunc((IPlayable)s, c.Opponent.Hero, V("CATA_999", "damage", 4), false))
				}
			}));
			// [MEND_100] Cultivating Sprite (3) — Battlecry: Get a 3-Cost Bulb that casts three random 1-Cost spells. It upgrades each turn.
			cards.Add("MEND_100", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => GetCard(c, Cards.FromId("MEND_100t"), s)) }));
			// [MEND_100t] Blooming Bulb (3) — Cast three random @-Cost spells. (Upgrades each turn!) (+1 at the start of your turns in hand)
			cards.Add("MEND_100t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					TriggerActivation = TriggerActivation.HAND,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is IPlayable me && me.Zone?.Type == Zone.HAND && me[GameTag.TAG_SCRIPT_DATA_NUM_1] < V("MEND_100t", "max", 10))
							me[GameTag.TAG_SCRIPT_DATA_NUM_1] = me[GameTag.TAG_SCRIPT_DATA_NUM_1] + 1;
					})
				},
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int cost = s[GameTag.TAG_SCRIPT_DATA_NUM_1];
					for (int i = 0; i < V("MEND_100t", "spells", 3); i++)
					{
						if (c.Hero.IsDead || c.Opponent.Hero.IsDead) break;
						if (RandomCard(g, x => x.Type == CardType.SPELL && x.Cost == cost) is Card spell)
							new CastRandomSpellTask().Process(g, c, s, Entity.FromCard(c, spell, new EntityData { [GameTag.CREATOR] = s.Id }));
					}
				})
			}));

			// ---------------------------------------------------------------- Death Knight
			// [CATA_155] Arisen Onyxia (9) — Colossal +2. When your hero would lose Health on your turn, gain that much max Health instead.
			cards.Add("CATA_155", new CardDef(new Power { HealthLossToMaxHealth = "CATA_155e" }));
			cards.Add("CATA_155e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(0)) { UseScriptTag = true } }));
			// [CATA_155t / t1] Onyxia's Wing, [CATA_780t] Soldier of Onyxia — When summoned, get a random {0}-Cost minion. It costs Health
			// this turn. (CATA_155e2: a one-turn "costs Health")
			foreach (string id in new[] { "CATA_155t", "CATA_155t1", "CATA_780t" })
				cards.Add(id, new CardDef(new Power { WhenSummonedTask = WingSummoned }));
			cards.Add("CATA_155e2", OneTurn(new Effect(GameTag.CARD_COSTS_HEALTH, EffectOperator.ADD, 1)));
			// [CATA_156] Experimental Animation (6) — Herald {0}. Deal $4 damage to all enemy minions. (live 1417: the Herald first)
			cards.Add("CATA_156", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(HeraldTask, new CustomTask((g, c, s, t, stack) =>
					DamageAll(s, c.Opponent.BoardZone.GetAll().Where(m => !(m is Location) && !m.Untouchable).Cast<ICharacter>(), V("CATA_156", "damage", 4), true)))
			}));
			// [CATA_161] Gruesome Nightmare (3) — Battlecry: Give a minion in your hand or battlefield Attack equal to this minion's Attack.
			// (wiki: the player's choice, hand or board)
			cards.Add("CATA_161", new CardDef(BattlecryFriendlyMinion, new Power
			{
				HandTarget = SelfCondition.IsMinion,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m && m.Controller == c && s is Minion me && me.AttackDamage > 0) Give(g, s, m, "CATA_161e", me.AttackDamage);
				})
			}));
			cards.Add("CATA_161e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0)) { UseScriptTag = true } }));
			// [CATA_464] Blackwing Experiment (2) — Deathrattle: Get a 2-Cost spell that deals this minion's Attack damage. (its last Attack
			// in play: live vfx 199064 — a buffed 5-Attack one gave 5)
			cards.Add("CATA_464", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (GetCard(c, Cards.FromId("CATA_464t"), s) is IPlayable breath)
						breath[GameTag.TAG_SCRIPT_DATA_NUM_1] = s[HsTag.LAST_ATTACK_IN_PLAY] > 0 ? s[HsTag.LAST_ATTACK_IN_PLAY] : ((IPlayable)s).Card.ATK;
				})
			}));
			// [CATA_464t] Dragon Breath (2) — Deal $@ damage.
			cards.Add("CATA_464t", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is ICharacter ch) Generic.DamageCharFunc((IPlayable)s, ch, s[GameTag.TAG_SCRIPT_DATA_NUM_1], true); })
			}));
			// [CATA_467] Command Claw (2, weapon) — After your hero attacks, give a random friendly minion +2 Attack.
			cards.Add("CATA_467", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (RandomFriendly(g, c) is Minion m) Give(g, s, m, "CATA_467e"); })
				}
			}));
			cards.Add("CATA_467e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("CATA_467", "attack", 2))) }));
			// [CATA_469] Chromatic Broodmother (4) — Rush. Whenever this attacks, refresh Mana Crystals equal to this minion's Attack.
			cards.Add("CATA_469", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me) RefreshMana(c, me.AttackDamage); })
				}
			}));
			// [CATA_470] Victor Nefarius (4) — Battlecry: Craft a custom Undead Dragon. If you're holding a Dragon, reduce the Creation's Cost
			// by (3). (wiki: 3 Dragons then 3 Undead; Costs, stats and text combined, at most 10 Mana; the curated pool is unknown — ours:
			// Dragons / Undead of any class from the pool — doubt; the Creation looks like Nefarian's Creation)
			cards.Add("CATA_470", new CardDef(new Power
			{
				GlowWhile = p => HoldingDragon(p.Controller, p),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var dragons = Pick(g, Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION && x.IsRace(Race.DRAGON) && !x.IsRace(Race.UNDEAD) &&
						x.Cost <= V("CATA_470", "max cost", 10) - 1), 3);
					if (dragons.Count == 0) return;
					Generic.CreateChoiceEntities(c, s, OfferCards(c, s, dragons.Select(x => x.Id)), ChoiceAction.STACK, new CustomTask((g2, c2, s2, dragon, st2) =>
					{
						if (dragon == null) return;
						int dragonId = dragon.Card.AssetId;
						int room = V("CATA_470", "max cost", 10) - dragon.Card.Cost;
						var undead = Pick(g2, Cards.GenerationPool(g2, c2).Where(x => x.Type == CardType.MINION && x.IsRace(Race.UNDEAD) && !x.IsRace(Race.DRAGON) &&
							x.Cost <= room), 3);
						if (undead.Count == 0) return;
						Generic.CreateChoiceEntities(c2, s2, OfferCards(c2, s2, undead.Select(x => x.Id)), ChoiceAction.STACK, new CustomTask((g3, c3, s3, dead, st3) =>
						{
							if (dead == null || !(Cards.FromAssetId(dragonId) is Card d)) return;
							Card creation = Generic.ModularCard(Cards.FromId("CATA_470t1"), d, dead.Card);
							if (GetCard(c3, creation, s3) is IPlayable made && HoldingDragon(c3, made))
								Give(g3, s3, made, GenericCostLess(V("CATA_470", "less", 3)));
						}));
					}));
				})
			}));
			// [CATA_471] Talanji's Last Stand (5) — Give your minions "Deathrattle: Summon a random 4-Cost minion."
			cards.Add("CATA_471", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.GetAll().Where(m => !(m is Location) && !m.Untouchable), "CATA_471e"))
			}));
			cards.Add("CATA_471e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, RandomMinionOfCost(g, V("CATA_471", "cost", 4)), s))
			}));
			// [CATA_780] Obsessive Technician (4) — Lifesteal. Battlecry: Herald {0}.
			cards.Add("CATA_780", new CardDef(new Power { PowerTask = HeraldTask }));

			// ---------------------------------------------------------------- Demon Hunter
			// [CATA_151] Azshara, Ocean Lord (8) — Colossal +2. Your hero has Windfury.
			cards.Add("CATA_151", new CardDef(new Power { Aura = new Aura(AuraType.HERO, "CATA_151e") }));
			cards.Add("CATA_151e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.WINDFURY, EffectOperator.SET, 1)) }));
			// [CATA_151t / t1] Azshara's Tentacle, [CATA_525t] Soldier of Azshara — When summoned, give your hero +{0} Attack this turn.
			foreach (string id in new[] { "CATA_151t", "CATA_151t1", "CATA_525t" })
				cards.Add(id, new CardDef(new Power { WhenSummonedTask = TentacleSummoned }));
			cards.Add("CATA_151te", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0)) { UseScriptTag = true, IsOneTurnEffect = true } }));
			// [CATA_525] Armored Bloodletter (3) — Rush. Battlecry: Herald {0}.
			cards.Add("CATA_525", new CardDef(new Power { PowerTask = HeraldTask }));
			// [CATA_526] Broxigar's Last Stand (2) — Deal $1 damage to all minions. Draw a card for each that died. (live darkmoon 11121: the
			// deaths and their Deathrattles first, then the draws)
			cards.Add("CATA_526", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var hit = AllOtherMinions(c, null);
					DamageAll(s, hit.Cast<ICharacter>(), V("CATA_526", "damage", 1), true);
					g.DeathProcessingAndAuraUpdate();
					int died = hit.Count(m => m.IsDead || m.Zone?.Type != Zone.PLAY);
					for (int i = 0; i < died; i++) Generic.Draw(c);
				})
			}));
			// [CATA_527] Nespirah, Enthralled (3, Location) — Deal 1 damage. After you cast a Fel spell, reopen. Deathrattle: Summon Nespirah,
			// Unshackled. (any character — doubt)
			cards.Add("CATA_527", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is ICharacter ch) Generic.DamageCharFunc((IPlayable)s, ch, V("CATA_527", "damage", 1), false); }),
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p[SPELL_SCHOOL] == FEL_SCHOOL),
					SingleTask = new CustomTask((g, c, s, t, stack) => Generic.Reopen(s))
				},
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("CATA_527t2"), s))
			}));
			// [CATA_527t2] Nespirah, Unshackled (6) — After you cast a Fel spell, get a random non-Colossal Naga. It costs (1).
			cards.Add("CATA_527t2", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p[SPELL_SCHOOL] == FEL_SCHOOL),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (GetCard(c, RandomCard(g, x => x.Type == CardType.MINION && x.IsRace((Race)92) && x[Generic.Colossal] == 0), s) is IPlayable naga)
							Give(g, s, naga, "GBL_001e");
					})
				}
			}));
			// [CATA_528] Sigil of the Seas (1) — At the start of your next turn, summon a 3/3 Naga with Taunt.
			cards.Add("CATA_528", Sigil("CATA_528", new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("CATA_528t"), s))));
			// [CATA_529] Ravenous Felfisher (6) — Costs (1) less for each Fel spell you've cast this game.
			cards.Add("CATA_529", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => SpellsCast(p.Controller).Count(h => h.SourceCard[SPELL_SCHOOL] == FEL_SCHOOL) * V("CATA_529", "less", 1))
			}));
			// [CATA_530] Fel Infusion (2) — Herald {0}. Your hero has Lifesteal this turn.
			cards.Add("CATA_530", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(HeraldTask, new AddEnchantmentTask("CATA_530e", EntityType.HERO))
			}));
			cards.Add("CATA_530e", OneTurn(new Effect(GameTag.LIFESTEAL, EffectOperator.ADD, 1)));
			// [CATA_533] Flash Flood (5) — Deal $5 damage to your opponent's left and right-most minions. Outcast: Do it again. (one minion:
			// hit once a round; the second round re-picks among those not mortally wounded)
			ISimpleTask flood = new CustomTask((g, c, s, t, stack) =>
			{
				for (int round = 0; round < (c.OutcastPlayId == s.Id ? 2 : 1); round++)
				{
					var enemies = c.Opponent.BoardZone.GetAll().Where(m => !(m is Location) && !m.Untouchable && !m.IsDead && !m.ToBeDestroyed && m.Health > 0).ToList();
					if (enemies.Count == 0) break;
					foreach (Minion m in new[] { enemies[0], enemies[enemies.Count - 1] }.Distinct().ToList())
						Generic.DamageCharFunc((IPlayable)s, m, V("CATA_533", "damage", 5), true);
				}
			});
			cards.Add("CATA_533", new CardDef(new Power { PowerTask = flood }));
			// [CATA_697] Malevolent Mutant (3) — Battlecry: Choose a Fel spell in your hand. Get a copy of it.
			cards.Add("CATA_697", new CardDef(new Power
			{
				HandTarget = new SelfCondition(p => p is Spell && p[SPELL_SCHOOL] == FEL_SCHOOL),
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t?.Zone?.Type == Zone.HAND && t.Controller == c) Generic.Copy(c, s, t, Zone.HAND); })
			}));
			// [CATA_699] Dread Leviathan (9) — Taunt. Battlecry: Choose an enemy minion to steal 3 Health from, three times. (wiki: three
			// separate picks, the same one allowed; a damaged one gives at most its current Health — patch 35.0/35.4)
			cards.Add("CATA_699", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.PickTargets(c, s, V("CATA_699", "picks", 3), ch => ch is Minion m && m.Controller != c && !m.Untouchable, (g2, c2, s2, victim) =>
					{
						if (!(victim is Minion v) || !(s2 is Minion me) || me.Zone?.Type != Zone.PLAY) return;
						int x = Math.Min(V("CATA_699", "health", 3), Math.Max(0, v.Health));
						if (x <= 0) return;
						Generic.AddEnchantmentBlock(g2, Cards.FromId("CATA_699e"), me, v, x, 0, 0);
						Generic.AddEnchantmentBlock(g2, Cards.FromId("CATA_699e2"), me, me, x, 0, 0);
					}, different: false))
			}));
			cards.Add("CATA_699e", new CardDef(new Power { Enchant = new Enchant(Health.Effect(EffectOperator.SUB, 0)) { UseScriptTag = true } }));
			cards.Add("CATA_699e2", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(0)) { UseScriptTag = true } }));
		}
	}
}
