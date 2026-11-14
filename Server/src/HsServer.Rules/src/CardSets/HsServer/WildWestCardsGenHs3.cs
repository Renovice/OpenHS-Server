// HsServer addition (not upstream SabberStone): Showdown in the Badlands / Delve into Deepholm — chunk 2: Druid, Hunter and Mage
// cards (spec: research spec_chunk2; shared pieces: WildWestCardsGenHs.cs, Actions/WildWest.cs). Every number goes through
// CardValues (V).
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
		/// <summary>Camouflage Mount's Bonus Effects in its enchantments' order (WW_810t1e1 … WW_810t8e1: Divine Shield, Taunt, Rush,
		/// Windfury, Elusive, Poisonous, Lifesteal, Reborn — the client texts).</summary>
		private static readonly GameTag[] WildWestC2MountKeywords =
		{
			GameTag.DIVINE_SHIELD, GameTag.TAUNT, GameTag.RUSH, GameTag.WINDFURY, HsTag.ELUSIVE, GameTag.POISONOUS, GameTag.LIFESTEAL, GameTag.REBORN,
		};

		private static IEffect WildWestC2KeywordEffect(GameTag keyword) => keyword == HsTag.ELUSIVE
			? Effects.CantBeTargetedBySpellsAndHeroPowers
			: new Effect(keyword, EffectOperator.SET, 1);

		/// <summary>"Summon a copy of this" N times (Overflow Surger, Dragon Golem): copies of the minion as it is (play → play).</summary>
		private static void WildWestC2CopiesOfSelf(Controller c, IEntity s, int n)
		{
			for (int i = 0; i < n && s is Minion me && me.Zone?.Type == Zone.PLAY && !c.BoardZone.IsFull; i++)
				Generic.Copy(c, s, me, Zone.PLAY);
		}

		/// <summary>A random Beast for the battlefield with exactly this Cost (Spurfang), or null.</summary>
		private static Card WildWestC2BeastOfCost(Game g, int cost) =>
			RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.BEAST) && x.Cost == cost && !x.NeverRandomlyOnBoard);

		private static void WildWestChunk2(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Mage
			// [DEEP_000] Summoning Ward (3) — Secret: When your turn starts, summon a copy of your highest Cost minion. (A Secret may
			// trigger at its owner's turn start; not with a full board — wiki patch 29.0.0.195635. Live vfx-decks 13924.)
			cards.Add("DEEP_000", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					Condition = new SelfCondition(p => p.Controller.BoardZone.GetAll().Any(m => !m.IsDead) && !p.Controller.BoardZone.IsFull),
					SingleTask = ComplexTask.Secret(new CustomTask((g, c, s, t, stack) =>
					{
						var mine = c.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed).ToList();
						if (mine.Count == 0) return;
						int max = mine.Max(m => m.Cost);
						var top = mine.Where(m => m.Cost == max).ToList();
						Generic.Copy(c, s, top[g.Random.Next(top.Count)], Zone.PLAY);
					}))
				}
			}));
			// [WW_377] Heat Wave (2) — Deal $2 damage to an enemy minion and its neighbors. Quickdraw: To all enemies instead. (Its
			// data's tag 2433: no target while Quickdraw is on — Generic.DropsTarget; live ×3.)
			cards.Add("WW_377", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int damage = V("WW_377", "damage", 2);
					if (SelfCondition.IsQuickdraw.Eval((IPlayable)s)) { DamageAll(s, Enemies(c), damage, true); return; }
					if (!(t is Minion m)) return;
					var hit = new List<ICharacter> { m };
					hit.AddRange(Neighbors(m));
					DamageAll(s, hit, damage, true);
				})
			}));
			// [WW_422] Azerite Vein (3) — Secret: When the enemy plays a card on the turn that it entered their hand, get a copy that
			// costs (0). (The Quickdraw rule: HsTag.QUICKDRAW_PLAY. Live: before the card's own POWER block; GBL_009e "Cost = 0".)
			cards.Add("WW_422", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_CARD)
				{
					TriggerSource = TriggerSource.ENEMY,
					Condition = new SelfCondition(p => p[HsTag.QUICKDRAW_PLAY] == 1),
					SingleTask = ComplexTask.Secret(new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable played = g.CurrentEventData?.EventSource as IPlayable ?? t;
						if (played == null || c.HandZone.IsFull) return;
						Generic.Copy(c, s, played, Zone.HAND, prepare: copy => Give(g, s, copy, "GBL_009e"));
					}))
				}
			}));
			// [WW_424] Overflow Surger (4) — Battlecry: Summon a copy of this for each turn in a row you've played an Elemental.
			cards.Add("WW_424", new CardDef(new Power
			{
				HandCounter = WildWestStreakCounter,
				PowerTask = new CustomTask((g, c, s, t, stack) => WildWestC2CopiesOfSelf(c, s, Generic.ElementalStreak(c)))
			}));
			// [WW_425] Stargazing (2) — Draw a different Arcane spell. If you play it this turn, it casts twice. (Generic.MarkCastsTwice:
			// WW_425e + the live highlight tags; live p-20261002-204752 65551.)
			cards.Add("WW_425", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					string self = ((IPlayable)s).Card.Id;
					if (DrawOne(g, c, p => p is Spell && p[SPELL_SCHOOL] == ARCANE && p.Card.Id != self) is IPlayable drawn)
						Generic.MarkCastsTwice(drawn, s, "WW_425e");
				})
			}));
			cards.Add("WW_425e", new CardDef(new Power { Enchant = new Enchant(new IEffect[0]) }));
			// [WW_427] Sunset Volley (9) — Deal $10 damage randomly split among all enemies. Summon a random 10-Cost minion. (Spell
			// Damage adds shots.)
			cards.Add("WW_427", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					WildWestC1Shots(g, c, s, Generic.SpellDamageAmount((IPlayable)s, V("WW_427", "damage", 10)), 1);
					Summon(g, c, RandomMinionOfCost(g, V("WW_427", "cost", 10)), s);
				})
			}));
			// [WW_429] Mes'Adune the Fractured (5) — Battlecry: Draw an Elemental. Split it into two halves. (Generic.DrawAndSplit;
			// the halves' enchantments WW_422e (Cost) and WW_422e2 (Attack and Health), by their texts.)
			cards.Add("WW_429", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.DrawAndSplit(c, (IPlayable)s, p => p is Minion && IsRaceCard(p, Race.ELEMENTAL), Cards.FromId("WW_422e"), Cards.FromId("WW_422e2")))
			}));
			cards.Add("WW_422e", new CardDef(new Power { Enchant = Generic.SetCostScriptTag }));
			cards.Add("WW_422e2", new CardDef(new Power { Enchant = Enchants.Enchants.SetAttackHealthScriptTag }));
			// [WW_430] Tae'thelan Bloodwatcher (4) — Cards that didn't start in your deck cost (4) less (but not less than 1).
			cards.Add("WW_430", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCostNotBelow1(V("WW_430", "discount", 4)))
				{
					Condition = new SelfCondition(p => Generic.DidntStartInDeck(p)),
					ShowAs = "WW_430e"
				}
			}));

			// ---------------------------------------------------------------- Mage / Hunter (Deepholm)
			// [DEEP_002] Elemental Companion (3) — Summon a random Elemental Companion. (Its related cards: Hiffar, Luekk, Me'sho.)
			cards.Add("DEEP_002", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var companions = ReprintCards.RelatedCardIds(Cards.SelfOf(s)?.Id ?? "DEEP_002")?.Select(Cards.TryFromId)
						.Where(x => x != null && x.Type == CardType.MINION).ToList() ?? new List<Card>();
					if (companions.Count > 0) Summon(g, c, companions[g.Random.Next(companions.Count)], s);
				})
			}));
			// [DEEP_002t] Hiffar — Your spells cost (1) less. (DEEP_002te shown on them: live p-20261002-204752 17357.)
			cards.Add("DEEP_002t", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("DEEP_002t", "discount", 1))) { Condition = SelfCondition.IsSpell, ShowAs = "DEEP_002te" }
			}));
			// [DEEP_003] Shimmer Shot (1) — Deal $1 damage. Summon a random minion of that Cost. (The Cost = the damage the text
			// shows, Spell Damage included.)
			cards.Add("DEEP_003", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int amount = Generic.SpellDamageAmount((IPlayable)s, V("DEEP_003", "damage", 1));
					if (t is ICharacter target) Generic.DamageCharFunc((IPlayable)s, target, V("DEEP_003", "damage", 1), true);
					Summon(g, c, RandomMinionOfCost(g, amount), s);
				})
			}));
			// [DEEP_004] Mantle Shaper (5) — Costs (1) less for each spell you've cast while holding this.
			cards.Add("DEEP_004", new CardDef(new Power
			{
				Trigger = WhileHolding(TriggerType.CAST_SPELL, p => p is Spell),
				Aura = new AdaptiveCostEffect(p => p[GameTag.TAG_SCRIPT_DATA_NUM_1] * V("DEEP_004", "discount", 1))
			}));

			// ---------------------------------------------------------------- Hunter
			// [DEEP_001] Mismatched Fossils (3) — Discover a Beast and an Undead. Swap their stats. (Live vfx-decks 218680: both to the
			// hand, then DEEP_001e on each with the other's stats.)
			cards.Add("DEEP_001", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.IsRace(Race.BEAST), null,
					new CustomTask((g, c, s, beast, stack) =>
					{
						if (beast == null) return;
						DiscoverFromTask.ClassOrNeutral((c2, x) => x.Type == CardType.MINION && x.IsRace(Race.UNDEAD), null,
							new CustomTask((g2, c2, s2, undead, st) =>
							{
								if (!(undead is Minion u) || !(beast is Minion b) || u.Zone?.Type != Zone.HAND || b.Zone?.Type != Zone.HAND) return;
								int ba = b.AttackDamage, bh = b.Health, ua = u.AttackDamage, uh = u.Health;
								Generic.AddEnchantmentBlock(g2, Cards.FromId("DEEP_001e"), (IPlayable)s2, b, ua, uh, 0);
								Generic.AddEnchantmentBlock(g2, Cards.FromId("DEEP_001e"), (IPlayable)s2, u, ba, bh, 0);
							})).Process(g, c, s, null, stack);
					}))
			}));
			cards.Add("DEEP_001e", new CardDef(new Power { Enchant = Enchants.Enchants.SetAttackHealthScriptTag }));
			// [DEEP_005] Obsidian Revenant (6) — Taunt. Deathrattle: Summon two random Deathrattle minions that cost (3) or less.
			cards.Add("DEEP_005", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("DEEP_005", "minions", 2); i++)
						Summon(g, c, RandomCard(g, x => x.Type == CardType.MINION && x.Deathrattle && x.Cost <= V("DEEP_005", "cost", 3) && !x.NeverRandomlyOnBoard), s);
				})
			}));
			// [WW_806] Sneaky Snakes (1) — Summon two 1/1 Snakes with Stealth.
			cards.Add("WW_806", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = ComplexTask.Repeat(new SummonTask("WW_806t", SummonSide.SPELL), V("WW_806", "snakes", 2))
			}));
			// [WW_807] Messenger Buzzard (2) — Deathrattle: Draw a Beast. Give minions in your hand +1/+1.
			cards.Add("WW_807", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					DrawWhere(g, c, p => p is Minion && IsRaceCard(p, Race.BEAST));
					GiveAll(g, s, c.HandZone.Where(p => Generic.IsMinionCard(p)), "WW_807e");
				})
			}));
			cards.Add("WW_807e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("WW_807", "stats", 1))) }));
			// [WW_808] Silver Serpent (3) — Rush, Poisonous. Quickdraw: Gain Immune this turn.
			cards.Add("WW_808", new CardDef(new Power { PowerTask = WildWestIfQuickdraw(new AddEnchantmentTask("WW_808e", EntityType.SOURCE)) }));
			cards.Add("WW_808e", OneTurn(Effects.Immune));
			// [WW_809] Bovine Skeleton (3) — Deathrattle: If this has 4 or more Attack, summon a Bovine Skeleton.
			cards.Add("WW_809", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (s is Minion me && me.AttackDamage >= V("WW_809", "attack", 4)) Summon(g, c, Cards.FromId("WW_809"), s);
				})
			}));
			// [WW_810] Camouflage Mount (4) — Give a minion +3/+3 and a random Bonus Effect. When it dies, summon a Chameleon. (The
			// Bonus Effect picks its enchantment / Chameleon: WW_810t<n>e1 / WW_810t<n>; Generic.RandomBonusEffect's Rush rule.)
			cards.Add("WW_810", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m) || !(Generic.RandomBonusEffect(g, m) is GameTag keyword)) return;
					int n = System.Array.IndexOf(WildWestC2MountKeywords, keyword) + 1;
					if (n > 0) Give(g, s, m, $"WW_810t{n}e1");
				})
			}));
			for (int i = 0; i < WildWestC2MountKeywords.Length; i++)
				cards.Add($"WW_810t{i + 1}e1", MountEnchant($"WW_810t{i + 1}", Effects.Attack_N(V("WW_810", "attack", 3)), Effects.Health_N(V("WW_810", "health", 3)),
					WildWestC2KeywordEffect(WildWestC2MountKeywords[i])));
			// [WW_811] Ten Gallon Hat (2) — Draw a minion. Give it +1/+1 and "Deathrattle: Get a Ten Gallon Hat."
			cards.Add("WW_811", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (DrawOne(g, c, p => p is Minion) is IPlayable drawn) Give(g, s, drawn, "WW_811e"); })
			}));
			cards.Add("WW_811e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.AttackHealth_N(V("WW_811", "stats", 1))),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => AddToHand(c, Cards.FromId("WW_811")))
			}));
			// [WW_812] Saddle Up! (3) — Give your minions "Deathrattle: Summon a random Beast that costs (3) or less."
			cards.Add("WW_812", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.GetAll().Where(m => !m.IsDead), "WW_812e"))
			}));
			cards.Add("WW_812e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
					Summon(g, c, RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.BEAST) && x.Cost <= V("WW_812", "cost", 3) && !x.NeverRandomlyOnBoard), s))
			}));
			// [WW_813] Starshooter (4, weapon) — After your hero attacks, get an Arcane Shot.
			cards.Add("WW_813", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = new AddCardTo("CORE_DS1_185", EntityType.HAND) }
			}));
			// [WW_814] Spurfang (5) — Battlecry and Deathrattle: Summon a random Beast with Cost equal to this minion's Attack. (Cost
			// stops at 10: wiki patch 28.6.0.193541.)
			ISimpleTask spurfang = new CustomTask((g, c, s, t, stack) =>
			{
				int cost = System.Math.Min(V("WW_814", "max cost", 10), System.Math.Max(0, s is Minion me ? me.AttackDamage : 0));
				Summon(g, c, WildWestC2BeastOfCost(g, cost), s);
			});
			cards.Add("WW_814", new CardDef(new Power { PowerTask = spurfang, DeathrattleTask = spurfang }));
			// [WW_815] Theldurin the Lost (4) — Battlecry: If your deck started with no duplicates, gain Immune this turn and attack all
			// enemies. (Generic.AttackAllEnemies: minions left to right, then the hero.)
			cards.Add("WW_815", new CardDef(new Power
			{
				PowerTask = WildWestIfNoDuplicates(new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					Give(g, s, me, "WW_815e");
					Generic.AttackAllEnemies(c, me);
				}))
			}));
			cards.Add("WW_815e", OneTurn(Effects.Immune));

			// ---------------------------------------------------------------- Druid
			// [DEEP_028] Crystal Cluster (7) — Gain three empty Mana Crystals. Any that can't fit summon a 3/7 Elemental with Taunt.
			cards.Add("DEEP_028", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int max = c[GameTag.MAXRESOURCES] > 0 ? c[GameTag.MAXRESOURCES] : Controller.MaxResources;
					int want = V("DEEP_028", "crystals", 3);
					int fit = System.Math.Max(0, System.Math.Min(want, max - c.BaseMana));
					if (fit > 0) Generic.ChangeManaCrystal.Invoke(c, fit, false);
					for (int i = fit; i < want; i++) Summon(g, c, Cards.FromId("DEEP_028t"), s);
				})
			}));
			// [DEEP_029] Trogg Gemtosser (3) — Finale: Deal 1 damage to a random enemy for each of your Mana Crystals.
			cards.Add("DEEP_029", new CardDef(new Power
			{
				PowerTask = Finale(new CustomTask((g, c, s, t, stack) => WildWestC1Shots(g, c, s, c.BaseMana, V("DEEP_029", "damage", 1))))
			}));
			// [WW_816] Take to the Skies (3) — Draw two Dragons. Give them +1/+1.
			cards.Add("WW_816", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("WW_816", "draw", 2); i++)
						if (DrawOne(g, c, p => p is Minion && IsRaceCard(p, Race.DRAGON)) is IPlayable dragon) Give(g, s, dragon, "WW_816e");
				})
			}));
			cards.Add("WW_816e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("WW_816", "stats", 1))) }));
			// [WW_818] Cactus Construct (1) — Discover a 2-Cost minion. Summon a 1/2 copy of it. (Live heist-run: the pick to the
			// hand, a new copy with WW_818e summoned.)
			cards.Add("WW_818", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.Cost == V("WW_818", "cost", 2), null,
					new CustomTask((g, c, s, t, stack) =>
					{
						if (t is Minion picked && !c.BoardZone.IsFull) Generic.Copy(c, s, picked, Zone.PLAY, prepare: copy => Give(g, s, copy, "WW_818e"));
					}))
			}));
			cards.Add("WW_818e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.SetAttack(V("WW_818", "attack", 1)), Effects.SetMaxHealth(V("WW_818", "health", 2)))
			}));
			// [WW_819] Splish-Splash Whelp (2) — Battlecry: If you're holding a Dragon, gain an empty Mana Crystal.
			cards.Add("WW_819", new CardDef(new Power { PowerTask = IfHoldingDragon(new ManaCrystalEmptyTask(V("WW_819", "crystals", 1))) }));
			// [WW_820] Spinetail Drake (4) — Battlecry: If you're holding a Dragon, deal 5 damage to an enemy minion.
			cards.Add("WW_820", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_IF_AVAILABLE_AND_DRAGON_IN_HAND, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				PowerTask = IfHoldingDragon(new DamageTask(V("WW_820", "damage", 5), EntityType.TARGET))
			}));
			// [WW_821] Dragon Tales (2) — Choose One - Get two random Dragons that cost (5) or less; or Get two that cost more than (5).
			int tale = V("WW_821", "cost", 5);
			cards.Add("WW_821t1", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < 2; i++) if (RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.DRAGON) && x.Cost <= tale) is Card d) AddToHand(c, d);
				})
			}));
			cards.Add("WW_821t2", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < 2; i++) if (RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.DRAGON) && x.Cost > tale) is Card d) AddToHand(c, d);
				})
			}));
			// [WW_822] Dragon Golem (7) — Taunt. Battlecry: Summon a copy of this for each Dragon in your hand.
			cards.Add("WW_822", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => WildWestC2CopiesOfSelf(c, s, c.HandZone.Count(p => Generic.IsMinionCard(p) && IsRaceCard(p, Race.DRAGON))))
			}));
			// [WW_823] Rehydrate (2) — Restore 7 Health. Quickdraw: Refresh 2 Mana Crystals. (Live ×10: the Quickdraw in a second POWER
			// block without ShowInHistory.)
			cards.Add("WW_823", new CardDef(TargetAny, new Power
			{
				PowerTask = new HealTask(V("WW_823", "heal", 7), EntityType.TARGET),
				SecondPowerTask = WildWestIfQuickdraw(new CustomTask((g, c, s, t, stack) => RefreshMana(c, V("WW_823", "refresh", 2)))),
				SecondPowerInHistory = false
			}));
			// [WW_824] Rheastrasza (8) — Battlecry: If your deck started with no duplicates, summon the Purified Dragon Nest.
			cards.Add("WW_824", new CardDef(new Power
			{
				PowerTask = WildWestIfNoDuplicates(new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("WW_824t"), s)))
			}));
			// [WW_824t] Purified Dragon Nest — At the start of your turn, Discover a Dragon. It costs (4) less. (Wiki Rheastrasza: "The
			// Purified Dragon Nest cannot generate Rheastrasza" — never the card that made it.)
			cards.Add("WW_824t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new DiscoverFromTask((g, c, s) =>
					{
						string maker = g.IdEntityDic.TryGetValue(s[GameTag.CREATOR], out IPlayable creator) ? creator.Card.Id : null;
						return Cards.FormatTypeCards(g.FormatType).Where(x => (x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL))
							&& x.Type == CardType.MINION && x.IsRace(Race.DRAGON) && x.Id != maker);
					}, "WW_824e")
				}
			}));
			cards.Add("WW_824e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("WW_824t", "discount", 4))) }));
			// [WW_825] Fye, the Setting Sun (9) — Rush, Lifesteal, Taunt. Costs (1) less for each Dragon you've summoned this game.
			cards.Add("WW_825", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.SummonedThisGame.Count(x => x.IsRace(Race.DRAGON)) * V("WW_825", "discount", 1))
			}));
			// [WW_826] Desert Nestmatron (4) — Taunt. Battlecry: If you're holding a Dragon, refresh 4 Mana Crystals.
			cards.Add("WW_826", new CardDef(new Power
			{
				PowerTask = IfHoldingDragon(new CustomTask((g, c, s, t, stack) => RefreshMana(c, V("WW_826", "refresh", 4))))
			}));
		}
	}
}
