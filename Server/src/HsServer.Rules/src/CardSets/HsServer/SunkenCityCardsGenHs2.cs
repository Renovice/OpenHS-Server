// HsServer addition (not upstream SabberStoneCore): Voyage to the Sunken City, the rest of batch 1 (see SunkenCityCardsGenHs.cs).
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
		/// <summary>The enemy hero and the enemy minions alive (HsServer, TITANS review 2026-10-07: BoardZone.GetAll — no
		/// untouchables: a Dormant minion is "Untouchable for both players", a permanent such as Sargeras' Twisting Nether "cannot be
		/// affected or targeted by any means", a Location isn't a minion; Tempest Hammer's "lowest Health enemy" hit the 0/1
		/// portal).</summary>
		private static List<ICharacter> Enemies(Controller c)
		{
			var enemies = new List<ICharacter> { c.Opponent.Hero };
			enemies.AddRange(c.Opponent.BoardZone.GetAll(m => !m.IsDead && !m.ToBeDestroyed));
			return enemies;
		}

		private static void DamageAll(IEntity source, IEnumerable<ICharacter> targets, int amount, bool spell)
		{
			foreach (ICharacter ch in targets.ToList())
				if (!ch.IsDead) Generic.DamageCharFunc((IPlayable)source, ch, amount, spell);
		}

		/// <summary>A minion of a Colossal on your board (its limbs by the client's COLOSSAL_LIMB, named after it).</summary>
		private static IEnumerable<Minion> LimbsOf(Controller c, string body) =>
			c.BoardZone.Where(m => !m.IsDead && Generic.ColossalLimbs(Cards.FromId(body)).Any(l => l.Id == m.Card.Id));

		private static void SunkenCityBatch1b(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Neutral
			// [TID_710] Snapdragon (3) — Battlecry: Give all Battlecry minions in your deck +1/+1.
			cards.Add("TID_710", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.DeckZone.Where(p => Generic.IsMinionCard(p) && p.Card[GameTag.BATTLECRY] > 0), "TID_710e"))
			}));
			cards.Add("TID_710e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TID_710", "stats", 1))) }));
			// [TID_711] Ozumat (8) — Colossal +6. Deathrattle: For each of Ozumat's Tentacles, destroy a random enemy minion.
			cards.Add("TID_711", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = LimbsOf(c, "TID_711").Count();
					foreach (Minion m in PickMinions(g, c.Opponent.BoardZone.Where(x => !x.IsDead && !x.ToBeDestroyed), n)) Generic.Destroy(m, s);
				})
			}));
			// [TID_713] Bubbler (1) — After this minion takes exactly one damage, destroy it. (Pop!)
			cards.Add("TID_713", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.SELF,
					Condition = new SelfCondition(p => p.Game.CurrentEventData?.EventNumber == 1),
					SingleTask = new DestroyTask(EntityType.SOURCE)
				}
			}));
			// [TSC_001] Naval Mine (2) — Deathrattle: Deal 4 damage to the enemy hero.
			cards.Add("TSC_001", new CardDef(new Power { DeathrattleTask = new DamageTask(V("TSC_001", "damage", 4), EntityType.OP_HERO) }));
			// [TSC_002] Pufferfist (3, weapon) — After your hero attacks, deal 1 damage to all enemies.
			cards.Add("TSC_002", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) => DamageAll(s, Enemies(c), V("TSC_002", "damage", 1), false))
				}
			}));
			// [TSC_007] Gangplank Diver (5) — Dormant for 1 turn. Rush. Immune while attacking. (Card data + Dormant table.)
			cards.Add("TSC_007", EngineOnly);
			// [TSC_020] Barbaric Sorceress (6) — Taunt. Battlecry: Swap the Cost of a random spell in each player's hand.
			cards.Add("TSC_020", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var mine = c.HandZone.Where(p => p is Spell).ToList();
					var theirs = c.Opponent.HandZone.Where(p => p is Spell).ToList();
					if (mine.Count == 0 || theirs.Count == 0) return;
					IPlayable a = mine[g.Random.Next(mine.Count)], b = theirs[g.Random.Next(theirs.Count)];
					int ca = a.Cost, cb = b.Cost;
					Generic.AddEnchantmentBlock(g, Cards.FromId("TSC_020e2"), (IPlayable)s, a, cb, 0, 0);
					Generic.AddEnchantmentBlock(g, Cards.FromId("TSC_020e2"), (IPlayable)s, b, ca, 0, 0);
				})
			}));
			cards.Add("TSC_020e2", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) { UseScriptTag = true } }));
			// [TSC_032] Blademaster Okani — batch 2.
			// [TSC_034] Gorloc Ravager (5) — Battlecry: Draw 3 Murlocs.
			cards.Add("TSC_034", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, IsMurloc, V("TSC_034", "murlocs", 3))) }));
			// [TSC_065] Helmet Hermit (1) — Can't attack. (CANT_ATTACK from its text: ReprintCards.)
			cards.Add("TSC_065", EngineOnly);
			// [TSC_067] Ambassador Faelin (4) — Battlecry: Put 3 Colossal minions on the bottom of your deck.
			cards.Add("TSC_067", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Card colossal in Pick(g, Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION && x[Generic.Colossal] > 0), V("TSC_067", "colossals", 3)))
						Generic.PutOnBottom(c, s, colossal);
				})
			}));
			// [TSC_069] Amalgam of the Deep (2) — Battlecry: Choose a friendly minion. Discover a minion of the same minion type.
			cards.Add("TSC_069", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_RACE, (int)Race.ALL } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion chosen)) return;
					Card typed = chosen.Card;
					new DiscoverFromTask((g2, c2, s2) => Cards.FormatTypeCards(g2.FormatType).Where(x => x.Type == CardType.MINION && x.Collectible &&
						(x.HasClass(c2.HeroClass) || x.HasClass(CardClass.NEUTRAL)) && SharesType(x, typed))).Process(g, c, s, null);
				})
			}));
			// [TSC_632] Click-Clocker (1) — Divine Shield. Battlecry: Give a random Mech in your hand +1/+1.
			cards.Add("TSC_632", new CardDef(new Power { PowerTask = BuffRandomInHand(IsMech, "TSC_632e") }));
			cards.Add("TSC_632e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TSC_632", "stats", 1))) }));
			// [TSC_638] Piranha Swarmer (1) — Rush. After you summon a Piranha Swarmer, gain +1 Attack. (Its tokens t..t4 too.)
			foreach (string id in new[] { "TSC_638", "TSC_638t", "TSC_638t2", "TSC_638t3", "TSC_638t4" })
				cards.Add(id, new CardDef(new Power
				{
					Trigger = new Trigger(TriggerType.AFTER_SUMMON)
					{
						TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
						Condition = new SelfCondition(p => p.Card.Name == "Piranha Swarmer"),
						SingleTask = new AddEnchantmentTask("TSC_638e", EntityType.SOURCE)
					}
				}));
			cards.Add("TSC_638e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TSC_638", "attack", 1))) }));
			// [TSC_640] Reefwalker (3) — Battlecry and Deathrattle: Summon a 1/1 Piranha Swarmer.
			cards.Add("TSC_640", new CardDef(new Power
			{
				PowerTask = new SummonTask("TSC_638t", SummonSide.RIGHT),
				DeathrattleTask = new SummonTask("TSC_638t", SummonSide.DEATHRATTLE)
			}));
			// [TSC_645] Stormcoil Mothership (6) — Rush. Deathrattle: Summon two random Mechs that cost (3) or less.
			cards.Add("TSC_645", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TSC_645", "mechs", 2); i++)
						Summon(g, c, RandomBoardMinion(g, x => x.IsRace(Race.MECHANICAL) && x.Cost <= V("TSC_645", "cost", 3)), s,
							s is Minion dead ? dead.LastBoardPosition : -1);
				})
			}));
			// [TSC_646] Seascout Operator (3) — Battlecry: If you control a Mech, summon two 2/1 Mechafish.
			cards.Add("TSC_646", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!c.BoardZone.Any(m => m.Id != s.Id && IsMech(m))) return;
					for (int i = 0; i < V("TSC_646", "fish", 2); i++) Summon(g, c, Cards.FromId("TSC_646t"), s);
				})
			}));
			// [TSC_649] Ini Stormcoil (5) — Battlecry: Choose a friendly Mech. Summon a copy of it with Rush, Windfury, and Divine
			// Shield.
			cards.Add("TSC_649", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_RACE, (int)Race.MECHANICAL } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion mech) || c.BoardZone.IsFull) return;
					var stackCopy = new TaskStack();
					new SummonCopyTask(EntityType.TARGET, addToStack: true).Process(g, c, s, mech, stackCopy);
					foreach (IPlayable copy in stackCopy.Playables ?? new List<IPlayable>()) Give(g, s, copy, "TSC_649e2");
				})
			}));
			cards.Add("TSC_649e2", new CardDef(new Power { Enchant = new Enchant(Effects.Rush, Effects.Windfury, new Effect(GameTag.DIVINE_SHIELD, EffectOperator.SET, 1)) }));
			// [TSC_823] Murkwater Scribe (2) — Battlecry: The next spell you play costs (1) less.
			cards.Add("TSC_823", new CardDef(new Power { PowerTask = new AddEnchantmentTask("TSC_823e", EntityType.CONTROLLER) }));
			cards.Add("TSC_823e", NextCardCost(Effects.ReduceCost(V("TSC_823", "discount", 1)), SelfCondition.IsSpell, "GBL_003e"));
			// [TSC_829] Naga Giant (20) — Costs (1) less for each Mana you've spent on spells this game.
			cards.Add("TSC_829", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.PlayHistory.Where(h => h.SourceCard?.Type == CardType.SPELL).Sum(h => h.Cost))
			}));
			// [TSC_926] Smothering Starfish (3) — Battlecry: Silence ALL other minions.
			cards.Add("TSC_926", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.Concat(c.Opponent.BoardZone).Where(m => m.Id != s.Id && !m.IsDead).ToList())
						new SilenceTask(EntityType.TARGET).Process(g, c, s, m);
				})
			}));
			// [TSC_928] Security Automaton (2) — After you summon a Mech, gain +1/+1.
			cards.Add("TSC_928", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					Condition = new SelfCondition(IsMech),
					SingleTask = new AddEnchantmentTask("TSC_928e", EntityType.SOURCE)
				}
			}));
			cards.Add("TSC_928e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TSC_928", "stats", 1))) }));
			// [TSC_935] Selfish Shellfish (4) — Deathrattle: Your opponent draws 2 cards.
			cards.Add("TSC_935", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("TSC_935", "cards", 2); i++) Generic.Draw(c.Opponent); })
			}));
			// [TSC_938] Treasure Guard (3) — Taunt. Deathrattle: Draw a card.
			cards.Add("TSC_938", new CardDef(new Power { DeathrattleTask = new DrawTask() }));
			// [TSC_960] Twin-fin Fin Twin (3) — Rush. Battlecry: Summon a copy of this.
			cards.Add("TSC_960", new CardDef(new Power { PowerTask = new SummonCopyTask(EntityType.SOURCE) }));

			// ---------------------------------------------------------------- Colossal appendages and bodies
			// [TSC_026] Colaque (7) — Colossal +1. Immune while you control Colaque's Shell.
			cards.Add("TSC_026", new CardDef(new Power
			{
				Aura = new Aura(AuraType.SELF, Effects.Immune) { Condition = new SelfCondition(p => LimbsOf(p.Controller, "TSC_026").Any()), Restless = true }
			}));
			// [TSC_026t] Colaque's Shell — Taunt. Deathrattle: Gain 8 Armor.
			cards.Add("TSC_026t", new CardDef(new Power { DeathrattleTask = new ArmorTask(V("TSC_026t", "armor", 8)) }));
			// [TSC_029] Gaia, the Techtonic (8) — Colossal +2. After a friendly Mech attacks, deal 1 damage to all enemies.
			cards.Add("TSC_029", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.MINIONS,
					Condition = new SelfCondition(IsMech),
					SingleTask = new CustomTask((g, c, s, t, stack) => DamageAll(s, Enemies(c), V("TSC_029", "damage", 1), false))
				}
			}));
			// [TSC_030] The Leviathan (7) — Colossal +1. Rush, Divine Shield. After this attacks, Dredge.
			cards.Add("TSC_030", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.SELF, SingleTask = new CustomTask((g, c, s, t, stack) => Generic.Dredge(c, c, s)) }
			}));
			// [TSC_030t2] The Leviathan's Claw — Rush, Divine Shield. After this attacks, draw a card.
			cards.Add("TSC_030t2", new CardDef(new Power { Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.SELF, SingleTask = new DrawTask() } }));
			// [TSC_216] Blackwater Behemoth (7) — Colossal +1. Lifesteal. (Its Lure lands on its left: COLOSSAL_LIMB_ON_LEFT.)
			cards.Add("TSC_216", EngineOnly);
			// [TSC_216t] Behemoth's Lure — At the end of your turn, force a random enemy minion to attack the Blackwater Behemoth.
			cards.Add("TSC_216t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var behemoth = c.BoardZone.FirstOrDefault(m => m.Card.Id == "TSC_216" && !m.IsDead);
						var enemies = c.Opponent.BoardZone.Where(m => !m.IsDead && !m.Untouchable && m.AttackDamage > 0).ToList();
						if (behemoth == null || enemies.Count == 0) return;
						EffectAttack(g, enemies[g.Random.Next(enemies.Count)], behemoth);
					})
				}
			}));
			// [TSC_639] Glugg the Gulper (7) — Colossal +3. After a friendly minion dies, gain its original stats.
			cards.Add("TSC_639", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventSource is Minion dead && s is Minion me)
							Generic.AddEnchantmentBlock(g, Cards.FromId("TSC_639e"), me, me, dead.Card.ATK, dead.Card.Health, 0);
					})
				}
			}));
			cards.Add("TSC_639e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0), Effects.Health_N(0)) { UseScriptTag = true } }));
			// [TSC_950] Hydralodon (7) — Colossal +2. Battlecry: Give your Hydralodon Heads Rush.
			cards.Add("TSC_950", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { foreach (Minion head in LimbsOf(c, "TSC_950").ToList()) head.IsRush = true; })
			}));
			// [TSC_950t] Hydralodon Head — Deathrattle: If you control Hydralodon, summon 2 Hydralodon Heads.
			foreach (string head in new[] { "TSC_950t", "TSC_950t2" })
				cards.Add(head, new CardDef(new Power
				{
					DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!c.BoardZone.Any(m => m.Card.Id == "TSC_950" && !m.IsDead)) return;
						for (int i = 0; i < V("TSC_950t", "heads", 2); i++)
							Summon(g, c, Cards.FromId("TSC_950t"), s, s is Minion dead ? dead.LastBoardPosition : -1);
					})
				}));
			// [TSC_937] Crabatoa (6) — Colossal +2. Your Crabatoa Claws have +2 Attack.
			cards.Add("TSC_937", new CardDef(new Power
			{
				Aura = new Aura(AuraType.WEAPON, Effects.Attack_N(V("TSC_937", "attack", 2))) { Condition = new SelfCondition(p => p.Card.Id == "TSC_937t2"), ShowAs = "TSC_937e" }
			}));
			// [TSC_937t] Crabatoa's Claw — Rush. Deathrattle: Equip a 2/1 Claw.
			foreach (string claw in new[] { "TSC_937t", "TSC_937t3" })
				cards.Add(claw, new CardDef(new Power { DeathrattleTask = new WeaponTask("TSC_937t2") }));

			// ---------------------------------------------------------------- Demon Hunter
			// [TID_704] Fossil Fanatic (2) — After your hero attacks, draw a Fel spell.
			cards.Add("TID_704", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => IsSchoolSpell(p, FEL))) }
			}));
			// [TSC_608] Abyssal Depths (3) — Draw your two lowest Cost minions.
			cards.Add("TSC_608", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TSC_608", "minions", 2); i++)
					{
						var minions = c.DeckZone.Where(p => Generic.IsMinionCard(p)).ToList();
						if (minions.Count == 0) return;
						int low = minions.Min(p => p.Cost);
						DrawWhere(g, c, p => p is Minion && p.Cost == low);
					}
				})
			}));
			// [TSC_610] Glaiveshark (4) — Battlecry: If your hero attacked this turn, deal 2 damage to all enemies.
			cards.Add("TSC_610", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (c.Hero.NumAttacksThisTurn > 0) DamageAll(s, Enemies(c), V("TSC_610", "damage", 2), false); })
			}));

			// ---------------------------------------------------------------- Druid
			// [TID_000] Spirit of the Tides (2) — If you have any unspent Mana at the end of your turn, gain +1/+2.
			cards.Add("TID_000", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (c.RemainingMana > 0) Give(g, s, (IPlayable)s, "TID_000e"); })
				}
			}));
			cards.Add("TID_000e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TID_000", "attack", 1)), Effects.Health_N(V("TID_000", "health", 2))) }));
			// [TID_001] Moonbeam (1) — Deal $1 damage to an enemy, twice.
			cards.Add("TID_001", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("TID_001", "damage", 1), EntityType.TARGET, true), new DamageTask(V("TID_001", "damage", 1), EntityType.TARGET, true))
			}));
			// [TSC_652] Green-Thumb Gardener (6) — Battlecry: Refresh empty Mana Crystals equal to the Cost of the highest Cost spell
			// in your hand.
			cards.Add("TSC_652", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var spells = c.HandZone.Where(p => p is Spell).ToList();
					if (spells.Count > 0) RefreshMana(c, spells.Max(p => p.Cost));
				})
			}));
			// [TSC_656] Miracle Growth (7) — Draw 3 cards. Summon a Plant with Taunt and stats equal to your hand size.
			cards.Add("TSC_656", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TSC_656", "cards", 3); i++) Generic.Draw(c);
					if (c.BoardZone.IsFull) return;
					// (no client enchantment: the Plant is created with those stats)
					int size = c.HandZone.Count;
					var plant = (Minion)Entity.FromCard(c, Cards.FromId("TSC_656t"), new EntityData { { GameTag.ATK, size }, { GameTag.HEALTH, size } });
					if (size > 0) Generic.SummonBlock.Invoke(g, plant, -1, s);
				})
			}));
			// [TSC_657] Dozing Kelpkeeper (1) — Rush. Starts Dormant. After you've cast 5 Mana worth of spells, awaken.
			cards.Add("TSC_657", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					WhileDormant = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me && g.CurrentEventData?.EventSource is IPlayable spell)
							Generic.AwakenProgress(me, PlayedCost(spell), V("TSC_657", "mana", 5));
					})
				}
			}));

			// ---------------------------------------------------------------- Hunter
			// [TID_075] Shellshot (3) — Deal $3 damage to a random enemy minion. Repeat this with 1 less damage.
			cards.Add("TID_075", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int damage = V("TID_075", "damage", 3); damage > 0; damage--)
					{
						var enemies = c.Opponent.BoardZone.Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
						if (enemies.Count == 0) return;
						Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], damage, true);
					}
				})
			}));
			// [TSC_072] Conch's Call (3) — Draw a Naga and a spell.
			cards.Add("TSC_072", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { DrawWhere(g, c, p => p is Minion && IsNaga(p)); DrawWhere(g, c, p => p is Spell); })
			}));
			// [TSC_073] Raj Naz'jan (2) — After you cast a spell, deal damage equal to its Cost to the enemy Hero.
			cards.Add("TSC_073", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventSource is IPlayable spell && PlayedCost(spell) > 0)
							Generic.DamageCharFunc((IPlayable)s, c.Opponent.Hero, PlayedCost(spell), false);
					})
				}
			}));
			// [TSC_929] Emergency Maneuvers (2) — Secret: When a friendly minion dies, summon a copy of it. It's Dormant for 1 turn.
			cards.Add("TSC_929", new CardDef(new Power { Trigger = Maneuvers(V("TSC_929", "copies", 1)) }));
			// [TSC_929t] Improved Emergency Maneuvers (2) — … summon two copies of it. They're Dormant for 1 turn.
			cards.Add("TSC_929t", new CardDef(new Power { Trigger = Maneuvers(V("TSC_929t", "copies", 2)) }));

			// ---------------------------------------------------------------- Mage
			// [TID_707] Submerged Spacerock (2) — Deathrattle: Add two Arcane Mage spells to your hand. They are Temporary.
			cards.Add("TID_707", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TID_707", "spells", 2) && !c.HandZone.IsFull; i++)
					{
						if (!(RandomCard(g, x => x.Type == CardType.SPELL && x.HasClass(CardClass.MAGE) && x[SPELL_SCHOOL] == ARCANE) is Card spell)) return;
						IPlayable p = Entity.FromCard(c, spell);
						if (Generic.AddHandPhase.Invoke(c, p)) Generic.MakeTemporary(p, s);
					}
				})
			}));
			// [TID_708] Polymorph: Jellyfish (3) — Transform a minion into a 4/1 Jellyfish with Spell Damage +2.
			cards.Add("TID_708", new CardDef(TargetMinion, new Power { PowerTask = new TransformTask("TID_708t", EntityType.TARGET) }));
			// [TSC_054] Mecha-Shark (3) — After you summon a Mech, deal 3 damage randomly split among all enemies.
			cards.Add("TSC_054", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					Condition = new SelfCondition(IsMech),
					SingleTask = new EnqueueTask(V("TSC_054", "damage", 3), ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, 1))
				}
			}));
			// [TSC_055] Seafloor Gateway (3) — Draw a Mech. Reduce the Cost of Mechs in your hand by (1).
			cards.Add("TSC_055", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					DrawWhere(g, c, IsMech);
					GiveAll(g, s, c.HandZone.Where(IsMech), "TSC_055e");
				})
			}));
			cards.Add("TSC_055e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("TSC_055", "discount", 1))) }));
			// [TSC_056] Volcanomancy (2) — Choose a minion. When it dies, deal 3 damage to all other minions.
			cards.Add("TSC_056", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("TSC_056e", EntityType.TARGET) }));
			cards.Add("TSC_056e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.DEATHRATTLE, EffectOperator.SET, 1)),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					// an enchantment's Deathrattle: source = the dead minion, target = the enchantment
					if (!(t is Enchantment) || !(s is Minion dead)) return;
					DamageAll(s, c.BoardZone.Concat(c.Opponent.BoardZone).Where(m => m.Id != dead.Id), V("TSC_056", "damage", 3), false);
				})
			}));

			// ---------------------------------------------------------------- Paladin
			// [TID_077] Lightray (9) — Taunt. Costs (1) less for each Paladin card you've played this game.
			cards.Add("TID_077", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.PlayHistory.Count(h => h.SourceCard != null && h.SourceCard.HasClass(CardClass.PALADIN)))
			}));
			// [TID_098] Myrmidon (3) — After you cast a spell on this minion, draw a card.
			cards.Add("TID_098", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST) { TriggerSource = TriggerSource.FRIENDLY_SPELL_CASTED_ON_THE_OWNER, SingleTask = new DrawTask() }
			}));
			// [TSC_059] Bubblebot (4) — Battlecry: Give your other Mechs Divine Shield and Taunt.
			cards.Add("TSC_059", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.Where(m => m.Id != s.Id && !m.IsDead && IsMech(m)).ToList()) { m.HasDivineShield = true; m.HasTaunt = true; }
				})
			}));
			// [TSC_060] Shimmering Sunfish (3) — Battlecry: If you're holding a Holy spell, gain Taunt and Divine Shield.
			cards.Add("TSC_060", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => stack.Flag = c.HandZone.Any(p => IsSchoolSpell(p, HOLY))),
					new FlagTask(true, new CustomTask((g, c, s, t, stack) => { if (s is Minion me) { me.HasTaunt = true; me.HasDivineShield = true; } })))
			}));
			// [TSC_061] The Garden's Grace (10) — Give a minion +4/+4 and Divine Shield. Costs (1) less for each Mana you've spent on
			// Holy spells this game.
			cards.Add("TSC_061", new CardDef(TargetMinion, new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.PlayHistory.Where(h => h.SourceCard?.Type == CardType.SPELL && h.SourceCard[SPELL_SCHOOL] == HOLY).Sum(h => h.Cost)),
				PowerTask = new AddEnchantmentTask("TSC_061e", EntityType.TARGET)
			}));
			cards.Add("TSC_061e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TSC_061", "stats", 4)).Append(new Effect(GameTag.DIVINE_SHIELD, EffectOperator.SET, 1)).ToArray()) }));
			// [TSC_079] Radar Detector (2) — Scan the bottom 5 cards of your deck. Draw any Mechs found this way, then shuffle your deck.
			cards.Add("TSC_079", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable mech in c.DeckZone.Take(V("TSC_079", "cards", 5)).Where(IsMech).ToList()) Generic.Draw(c, mech);
					c.DeckZone.Shuffle();
					if (g.History) g.PowerHistory.Add(Kettle.PowerHistoryBuilder.ShuffleDeck(c.PlayerId));
				})
			}));
			// [TSC_952] Holy Maki Roll (1) — Restore #2 Health. Repeatable this turn. (Repeatable: Card.Repeatable.)
			cards.Add("TSC_952", new CardDef(TargetAny, new Power { PowerTask = new HealTask(V("TSC_952", "heal", 2), EntityType.TARGET) }));

			// ---------------------------------------------------------------- Priest
			// [TSC_211] Whispers of the Deep (1) — Silence a friendly minion, then deal damage equal to its Attack randomly split among
			// all enemy minions.
			cards.Add("TSC_211", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					new SilenceTask(EntityType.TARGET).Process(g, c, s, m);
					int damage = m.AttackDamage;
					if (damage > 0) new EnqueueTask(damage, ComplexTask.DamageRandomTargets(1, EntityType.OP_MINIONS, 1), true).Process(g, c, s, null);
				})
			}));
			// [TSC_213] Queensguard (2) — Battlecry: Gain +1/+1 for each spell you've cast this turn.
			cards.Add("TSC_213", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = SpellsCast(c).Count(h => h.Turn == g.Turn);
					if (n > 0 && s is Minion me) Generic.AddEnchantmentBlock(g, Cards.FromId("TSC_213e"), me, me, n, 0, 0);
				})
			}));
			cards.Add("TSC_213e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));
			// [TSC_702] Switcheroo (5) — Draw 2 minions. Swap their Health.
			cards.Add("TSC_702", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					IPlayable a = DrawOne(g, c, p => p is Minion), b = DrawOne(g, c, p => p is Minion);
					if (!(a is Minion ma) || !(b is Minion mb)) return;
					int ha = ma.Health, hb = mb.Health;
					Generic.AddEnchantmentBlock(g, Cards.FromId("TSC_702e"), (IPlayable)s, ma, hb, 0, 0);
					Generic.AddEnchantmentBlock(g, Cards.FromId("TSC_702e"), (IPlayable)s, mb, ha, 0, 0);
				})
			}));
			cards.Add("TSC_702e", new CardDef(new Power { Enchant = Enchants.Enchants.SetHealthScriptTag }));
			// [TSC_828] Priestess Valishj (1) — Battlecry: Refresh an empty Mana Crystal for each spell you've cast this turn.
			cards.Add("TSC_828", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => RefreshMana(c, SpellsCast(c).Count(h => h.Turn == g.Turn))) }));

			// ---------------------------------------------------------------- Rogue
			// [TID_078] Shattershambler (1) — Battlecry: Your next Deathrattle minion costs (1) less, but immediately dies when played.
			cards.Add("TID_078", new CardDef(new Power { PowerTask = new AddEnchantmentTask("TID_078e", EntityType.CONTROLLER) }));
			cards.Add("TID_078e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("TID_078", "discount", 1)))
				{
					Condition = new SelfCondition(p => p is Minion && p.Card[GameTag.DEATHRATTLE] > 0), ShowAs = "TID_078e2"
				},
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.Card[GameTag.DEATHRATTLE] > 0),
					SingleTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventSource is Minion played && played.Zone?.Type == Zone.PLAY) Generic.Destroy(played, s);
					}), RemoveEnchantmentTask.Task)
				}
			}));
			// [TID_080] Inkveil Ambusher (2) — Stealth. Has +3 Attack and Immune while attacking.
			cards.Add("TID_080", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.ATTACK) { TriggerSource = TriggerSource.SELF, SingleTask = new AddEnchantmentTask("TID_080e2", EntityType.SOURCE) },
					new Trigger(TriggerType.AFTER_ATTACK)
					{
						TriggerSource = TriggerSource.SELF,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (s is Minion me) foreach (Enchantment e in me.AppliedEnchantments?.Where(e => e.Card.Id == "TID_080e2").ToList() ?? new List<Enchantment>()) e.Remove();
						})
					})
			}));
			cards.Add("TID_080e2", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TID_080", "attack", 3)), Effects.Immune) }));
			// [TSC_085] Cutlass Courier (3) — After your hero attacks, draw a Pirate.
			cards.Add("TSC_085", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, IsPirate)) }
			}));
			// [TSC_932] Blood in the Water (6) — Deal $3 damage to an enemy. Summon a 5/5 Shark with Rush.
			cards.Add("TSC_932", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("TSC_932", "damage", 3), EntityType.TARGET, true), new SummonTask("TSC_932t", SummonSide.SPELL))
			}));
			// [TSC_933] Bootstrap Sunkeneer (5) — Combo: Put an enemy minion on the bottom of your opponent's deck.
			cards.Add("TSC_933", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_FOR_COMBO, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				ComboTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m) PutMinionOnBottom(m.Controller, m, s); })
			}));
			// [TSC_936] Swiftscale Trickster (6) — Battlecry: Your next spell this turn costs (0).
			cards.Add("TSC_936", new CardDef(new Power { PowerTask = new AddEnchantmentTask("TSC_936e", EntityType.CONTROLLER) }));
			cards.Add("TSC_936e", new CardDef(new Power
			{
				Enchant = new Enchant { IsOneTurnEffect = true },
				Aura = new Aura(AuraType.HAND, Effects.SetCost(0)) { Condition = SelfCondition.IsSpell, ShowAs = "GBL_009e" },
				Trigger = new Trigger(TriggerType.PLAY_CARD) { TriggerSource = TriggerSource.FRIENDLY, Condition = SelfCondition.IsSpell, SingleTask = RemoveEnchantmentTask.Task }
			}));
			// [TSC_963] Filletfighter (1) — Battlecry: Deal 1 damage.
			cards.Add("TSC_963", new CardDef(BattlecryAny, new Power { PowerTask = new DamageTask(V("TSC_963", "damage", 1), EntityType.TARGET) }));

			// ---------------------------------------------------------------- Shaman
			// [TID_004] Clownfish (3) — Battlecry: Your next two Murlocs cost (2) less.
			cards.Add("TID_004", new CardDef(new Power { PowerTask = new AddEnchantmentTask("TID_004e", EntityType.CONTROLLER) }));
			cards.Add("TID_004e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("TID_004", "discount", 2))) { Condition = new SelfCondition(p => p is Minion && IsMurloc(p)), ShowAs = "TID_004e2" },
				Trigger = new Trigger(TriggerType.PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Minion && IsMurloc(p)),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e)) return;
						e[GameTag.TAG_SCRIPT_DATA_NUM_1] = e[GameTag.TAG_SCRIPT_DATA_NUM_1] + 1;
						if (e[GameTag.TAG_SCRIPT_DATA_NUM_1] >= V("TID_004", "murlocs", 2)) e.Remove();
					})
				}
			}));
			// [TID_005] Command of Neptulon (5) — Summon two 5/4 Elementals with Rush. Overload: (1)
			cards.Add("TID_005", new CardDef(new Power { PowerTask = ComplexTask.Repeat(new SummonTask("TID_005t", SummonSide.SPELL), V("TID_005", "elementals", 2)) }));
			// [TSC_631] Schooling (1) — Add three 1/1 Piranha Swarmers to your hand.
			cards.Add("TSC_631", new CardDef(new Power { PowerTask = new AddCardTo("TSC_638t", EntityType.HAND, V("TSC_631", "swarmers", 3)) }));
			// [TSC_633] Piranha Poacher (3) — At the end of your turn, add a 1/1 Piranha Swarmer to your hand.
			cards.Add("TSC_633", new CardDef(new Power { Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new AddCardTo("TSC_638t", EntityType.HAND) } }));
			// [TSC_635] Radiance of Azshara (3) — Fire Spell Damage +2 (its tag). Your Nature spells cost (1) less. After you cast a
			// Frost spell, gain 3 Armor.
			cards.Add("TSC_635", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("TSC_635", "discount", 1))) { Condition = new SelfCondition(p => IsSchoolSpell(p, NATURE)), ShowAs = "TSC_635e" },
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => IsSchoolSpell(p, FROST)),
					SingleTask = new ArmorTask(V("TSC_635", "armor", 3))
				}
			}));
			// [TSC_648] Coral Keeper (5) — Battlecry: Summon a 3/3 Elemental for each spell school you've cast this game.
			cards.Add("TSC_648", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int schools = Generic.SpellSchoolsCast(c).Count;
					for (int i = 0; i < schools && !c.BoardZone.IsFull; i++) Summon(g, c, Cards.FromId("TSC_648t"), s);
				})
			}));
			// [TSC_922] Anchored Totem (2) — After you summon a 1-Cost minion, give it +2/+1.
			cards.Add("TSC_922", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					Condition = new SelfCondition(p => p.Card.Cost == V("TSC_922", "cost", 1)),
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (g.CurrentEventData?.EventSource is Minion m && m.Zone?.Type == Zone.PLAY) Give(g, s, m, "TSC_922e"); })
				}
			}));
			cards.Add("TSC_922e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TSC_922", "attack", 2)), Effects.Health_N(V("TSC_922", "health", 1))) }));
			// [TSC_923] Bioluminescence (4) — Give your minions Spell Damage +1.
			cards.Add("TSC_923", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.Where(m => !m.IsDead), "TSC_923e")) }));
			cards.Add("TSC_923e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.SPELLPOWER, EffectOperator.ADD, V("TSC_923", "spell damage", 1))) }));

			// ---------------------------------------------------------------- Warlock
			// [TSC_614] Voidgill (2) — Deathrattle: Give all Murlocs in your hand +1/+1.
			cards.Add("TSC_614", new CardDef(new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.Where(p => Generic.IsMinionCard(p) && IsMurloc(p)), "TSC_614e")) }));
			cards.Add("TSC_614e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TSC_614", "stats", 1))) }));
			// [TSC_957] Chum Bucket (2) — Give all Murlocs in your hand +1/+1. Repeat for each Murloc you control.
			cards.Add("TSC_957", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int times = 1 + c.BoardZone.Count(m => !m.IsDead && IsMurloc(m));
					for (int i = 0; i < times; i++) GiveAll(g, s, c.HandZone.Where(p => Generic.IsMinionCard(p) && IsMurloc(p)), "TSC_957e");
				})
			}));
			cards.Add("TSC_957e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TSC_957", "stats", 1))) }));

			// ---------------------------------------------------------------- Warrior
			// [TID_715] Clash of the Colossals (3) — Add a random Colossal minion to both players' hands. Yours costs (2) less.
			cards.Add("TID_715", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var pool = Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION && x[Generic.Colossal] > 0 && x.Collectible).ToList();
					if (pool.Count == 0) return;
					IPlayable mine = Entity.FromCard(c, pool[g.Random.Next(pool.Count)]);
					if (Generic.AddHandPhase.Invoke(c, mine)) Give(g, s, mine, "TID_715e");
					AddToHand(c.Opponent, pool[g.Random.Next(pool.Count)]);
				})
			}));
			cards.Add("TID_715e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("TID_715", "discount", 2))) }));
			// [TID_716] Tidal Revenant (8) — Battlecry: Deal 5 damage. Gain 8 Armor.
			cards.Add("TID_716", new CardDef(BattlecryAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("TID_716", "damage", 5), EntityType.TARGET), new ArmorTask(V("TID_716", "armor", 8)))
			}));
			// [TSC_659] Trenchstalker (9) — Battlecry: Attack three different random enemies.
			cards.Add("TSC_659", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var hit = new HashSet<int>();
					for (int i = 0; i < V("TSC_659", "attacks", 3); i++)
					{
						if (!(s is Minion me) || me.IsDead || me.Zone?.Type != Zone.PLAY) return;
						var enemies = Enemies(c).Where(e => !hit.Contains(e.Id) && !(e is Minion m && m.Untouchable)).ToList();
						if (enemies.Count == 0) return;
						ICharacter target = enemies[g.Random.Next(enemies.Count)];
						hit.Add(target.Id);
						EffectAttack(g, me, target);
					}
				})
			}));
			// [TSC_917] Blackscale Brute (7) — Taunt. Battlecry: If you have a weapon equipped, summon a 5/6 Naga with Rush.
			cards.Add("TSC_917", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) => stack.Flag = c.Hero.Weapon != null),
					new FlagTask(true, new SummonTask("TSC_917t", SummonSide.RIGHT)))
			}));
			// [TSC_939] Forged in Flame (2) — Destroy your weapon, then draw cards equal to its Attack.
			cards.Add("TSC_939", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(c.Hero.Weapon is Weapon w)) return;
					int n = w.AttackDamage;
					Generic.Destroy(w, s);
					g.DeathProcessingAndAuraUpdate();
					for (int i = 0; i < n; i++) Generic.Draw(c);
				})
			}));
			// [TSC_941] Guard the City (2) — Gain 3 Armor. Summon a 2/3 Naga with Taunt.
			cards.Add("TSC_941", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new ArmorTask(V("TSC_941", "armor", 3)), new SummonTask("TSC_941t", SummonSide.SPELL))
			}));
			// [TSC_943] Lady Ashvane (5) — Battlecry: Give all weapons in your hand, deck, and battlefield +1/+1.
			cards.Add("TSC_943", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var weapons = c.HandZone.Concat(c.DeckZone).Where(p => p is Weapon).ToList();
					if (c.Hero.Weapon != null) weapons.Add(c.Hero.Weapon);
					GiveAll(g, s, weapons, "TSC_943e");
				})
			}));
			cards.Add("TSC_943e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TSC_943", "stats", 1)), new Effect(GameTag.DURABILITY, EffectOperator.ADD, V("TSC_943", "stats", 1))) }));
			// [TSC_944] The Fires of Zin-Azshari (2) — Replace your deck with minions that cost (5) or more. They cost (5).
			cards.Add("TSC_944", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = c.DeckZone.Count;
					foreach (IPlayable old in c.DeckZone.ToList()) c.SetasideZone.Add(c.DeckZone.Remove(old));
					var pool = Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION && x.Cost >= V("TSC_944", "cost", 5)).ToList();
					for (int i = 0; i < n && pool.Count > 0; i++)
					{
						IPlayable p = Entity.FromCard(c, pool[g.Random.Next(pool.Count)]);
						if (Generic.ShuffleIntoDeck.Invoke(c, s, p)) Give(g, s, p, "TSC_944e");
					}
				})
			}));
			cards.Add("TSC_944e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(V("TSC_944", "cost", 5))) }));

			SunkenCityBatch2(cards);
		}

		/// <summary>Do two cards share a minion type (any of the body's types, Amalgam of the Deep)?</summary>
		private static bool SharesType(Card x, Card typed)
		{
			foreach (Race r in new[] { Race.MURLOC, Race.DEMON, Race.MECHANICAL, Race.ELEMENTAL, Race.BEAST, Race.TOTEM, Race.PIRATE, Race.DRAGON, Race.UNDEAD, NAGA, (Race)43 })
				if (typed.IsRace(r) && x.IsRace(r)) return true;
			return false;
		}

		/// <summary>"Refresh N empty Mana Crystals" (up to the ones used this turn).</summary>
		private static void RefreshMana(Controller c, int n)
		{
			if (n <= 0) return;
			c.UsedMana = System.Math.Max(0, c.UsedMana - n);
		}

		/// <summary>"Put a minion on the bottom of [a] deck": it leaves play into that deck's bottom (the same entity; a zone move).</summary>
		private static void PutMinionOnBottom(Controller deckOwner, Minion m, IEntity source)
		{
			if (m.Zone?.Type != Zone.PLAY || deckOwner.DeckZone.IsFull) return;
			Generic.MoveToDeck(deckOwner, source, m);
			if (m.Zone?.Type == Zone.DECK)
			{
				deckOwner.DeckZone.Remove(m);
				deckOwner.DeckZone.Add(m, 0);
			}
		}

		/// <summary>Emergency Maneuvers: "When a friendly minion dies, summon N copies of it. They're Dormant for 1 turn."</summary>
		private static Trigger Maneuvers(int copies) => new Trigger(TriggerType.DEATH)
		{
			TriggerSource = TriggerSource.MINIONS,
			SingleTask = ComplexTask.Secret(new CustomTask((g, c, s, t, stack) =>
			{
				if (!(g.CurrentEventData?.EventSource is Minion dead)) return;
				for (int i = 0; i < copies && !c.BoardZone.IsFull; i++)
				{
					var copy = (Minion)Entity.FromCard(c, dead.Card);
					Generic.SummonBlock.Invoke(g, copy, dead.LastBoardPosition, s);
					if (copy.Zone?.Type == Zone.PLAY) Generic.MakeDormant(copy, 1, "TSC_647e", (IPlayable)s);
				}
			}))
		};
	}
}
