// HsServer addition (not upstream SabberStone): Scholomance Academy (2020). Upstream had no script for the set; these
// definitions give its cards (and their tokens / enchantments) their effects. Every number goes through CardValues (V).
// Shared rules this set added: Spellburst (one-time "after you cast a spell", live shape), Soul Fragments, Poisonous
// spells (HsTag.SPELLS_POISONOUS), hero Health changes (HsTag.HERO_HEALTH_CHANGED_*), a hand set aside until the end of
// the turn (Secret Passage, Mindrender Illucia), what a spell destroyed (HsTag.DIED_BEFORE_SPELL_*).
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
		// ------------------------------------------------------------------ shared pieces of the set

		/// <summary>
		/// Spellburst (wiki: "a minion, weapon, or location ... do[es] something the first time you cast a spell with it in
		/// play, and only the first time"; "it loses the Spellburst graphic and effect"; "must survive"). A one-time "after you
		/// cast a spell" trigger; live: its TRIGGER block (TriggerKeyword SPELLBURST) runs the effect, then SPELLBURST = 0 on the
		/// card as its last change (questline GDB_310 6831, vfx-decks GDB_104 86444 / GDB_134 17743, night YOP_018 40766 — a
		/// Discover's choice comes after the block). The effect's target is the spell.
		/// </summary>
		private static Trigger Spellburst(ISimpleTask effect) => new Trigger(TriggerType.AFTER_CAST)
		{
			TriggerSource = TriggerSource.FRIENDLY,
			RemoveAfterTriggered = true,
			SingleTask = new CustomTask((g, c, s, t, stack) =>
			{
				effect.Process(g, c, s, t, stack);
				if (s is IPlayable me && me[HsTag.SPELLBURST] > 0) me[HsTag.SPELLBURST] = 0;
			})
		};

		private const string SoulFragment = "SCH_307t";

		/// <summary>"Shuffle N Soul Fragments into your deck."</summary>
		private static ISimpleTask ShuffleSoulFragments(int n) => new CustomTask((g, c, s, t, stack) =>
		{
			for (int i = 0; i < n; i++) Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId(SoulFragment)));
		});

		private static bool HasSoulFragment(Controller c) => c.DeckZone.Any(p => p.Card.Id == SoulFragment);

		/// <summary>"Destroy a Soul Fragment in your deck to …": with one in the deck, it's destroyed (deck → graveyard) and
		/// the effect happens; the condition drives the card's glow (ComplexTask.If).</summary>
		private static ISimpleTask DestroySoulFragmentTo(ISimpleTask effect) => ComplexTask.If((g, c, s) => HasSoulFragment(c), ComplexTask.Create(
			new CustomTask((g, c, s, t, stack) =>
			{
				var fragments = c.DeckZone.Where(p => p.Card.Id == SoulFragment).ToList();
				if (fragments.Count > 0) Generic.DeckCardToGraveyard(fragments[g.Random.Next(fragments.Count)], s?.Id ?? 0, false);
			}), effect));

		/// <summary>The "… Studies" spells: Discover a card; your next one costs (1) less (a player enchantment).</summary>
		private static CardDef Studies(System.Func<Controller, Card, bool> pool, string playerEnchantment) => new CardDef(new Power
		{
			PowerTask = ComplexTask.Create(DiscoverFromTask.ClassOrNeutral(pool), new AddEnchantmentTask(playerEnchantment, EntityType.CONTROLLER))
		});

		/// <summary>
		/// A player's hand set aside until the end of this turn (Secret Passage, Mindrender Illucia; wiki Secret Passage: the
		/// original hand is "temporarily removed", "swapped back at turn's end through a triggered effect"; cards gained
		/// meanwhile stay, then the original hand comes back after them). <paramref name="marker"/> is the player
		/// enchantment whose end-of-turn trigger brings it back (SetAsideHandReturns).
		/// </summary>
		private static void SetAsideHand(Game g, Controller c, IEntity source, string marker)
		{
			Generic.AddEnchantmentBlock(g, Cards.FromId(marker), (IPlayable)source, c, 0, 0, 0);
			int mark = c.AppliedEnchantments?.LastOrDefault(e => e.Card.Id == marker)?.Id ?? 1;
			foreach (IPlayable p in c.HandZone.ToList())
			{
				p[HsTag.SET_ASIDE_HAND] = mark;
				Generic.RemoveFromHand(c, p);
			}
		}

		/// <summary>The end-of-turn half of <see cref="SetAsideHand"/>: the cards that only lived in the hand until now leave
		/// (Secret Passage's deck cards go back to the deck; Illucia's copies vanish), then the set-aside hand returns.</summary>
		private static CardDef SetAsideHandReturns(bool tempCardsToDeck) => new CardDef(new Power
		{
			Trigger = new Trigger(TriggerType.TURN_END)
			{
				SingleTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) =>
				{
					int mark = s.Id;
					foreach (IPlayable p in c.HandZone.Where(p => p[HsTag.HAND_UNTIL_TURN_END] == mark).ToList())
					{
						p[HsTag.HAND_UNTIL_TURN_END] = 0;
						if (tempCardsToDeck) Generic.MoveToDeck(c, s, p);
						else Generic.RemoveFromHand(c, p);
					}
					foreach (IPlayable p in c.SetasideZone.Where(p => p[HsTag.SET_ASIDE_HAND] == mark).ToList())
					{
						p[HsTag.SET_ASIDE_HAND] = 0;
						if (c.HandZone.IsFull) break;
						c.SetasideZone.Remove(p);
						Generic.AddHandPhase.Invoke(c, p);
					}
				}), RemoveEnchantmentTask.Task)
			}
		});

		private static void Scholomance(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Soul Fragments, Studies, Spellburst neutrals

			// [SCH_307t] Soul Fragment (0) — Casts When Drawn. Restore 2 Health to your hero.
			var fragment = new HealTask(V("SCH_307t", "heal", 2), EntityType.HERO);
			cards.Add(SoulFragment, new CardDef(new Power { PowerTask = fragment, TopdeckTask = fragment }));

			// The Studies: Discover; the next one costs (1) less (their player enchantments: "Your next … costs (1) less").
			cards.Add("SCH_233", Studies((c, x) => x.Type == CardType.MINION && x.IsRace(Race.DRAGON), "SCH_233e"));       // Draconic Studies
			cards.Add("SCH_233e", NextCardEffect(Effects.ReduceCost(V("SCH_233", "discount", 1)), SelfCondition.IsRace(Race.DRAGON), thisTurnOnly: false));
			cards.Add("SCH_237", Studies((c, x) => x.Type == CardType.MINION && x[GameTag.RUSH] > 0, "SCH_237e"));          // Athletic Studies
			cards.Add("SCH_237e", NextCardEffect(Effects.ReduceCost(V("SCH_237", "discount", 1)), new SelfCondition(p => p is Minion && p.Card[GameTag.RUSH] > 0), thisTurnOnly: false));
			cards.Add("SCH_270", Studies((c, x) => x.Type == CardType.MINION && x[GameTag.SPELLPOWER] > 0, "SCH_270e"));    // Primordial Studies
			cards.Add("SCH_270e", NextCardEffect(Effects.ReduceCost(V("SCH_270", "discount", 1)), new SelfCondition(p => p is Minion && p.Card[GameTag.SPELLPOWER] > 0), thisTurnOnly: false));
			cards.Add("SCH_300", Studies((c, x) => x.Type == CardType.MINION && x[GameTag.DEATHRATTLE] > 0, "SCH_300e"));   // Carrion Studies
			cards.Add("SCH_300e", NextCardEffect(Effects.ReduceCost(V("SCH_300", "discount", 1)), new SelfCondition(p => p is Minion && p.Card[GameTag.DEATHRATTLE] > 0), thisTurnOnly: false));
			cards.Add("SCH_333", Studies((c, x) => x.Type == CardType.SPELL, "SCH_333e"));                                  // Nature Studies
			cards.Add("SCH_333e", NextCardEffect(Effects.ReduceCost(V("SCH_333", "discount", 1)), SelfCondition.IsSpell, thisTurnOnly: false));

			// [SCH_230] Onyx Magescribe (6) — Spellburst: Add 2 random spells from your class to your hand.
			cards.Add("SCH_230", new CardDef(new Power
			{
				Trigger = Spellburst(new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("SCH_230", "spells", 2); i++)
						if (RandomCard(g, x => x.Type == CardType.SPELL && x.HasClass(c.HeroClass)) is Card card) AddToHand(c, card);
				}))
			}));
			// [SCH_231] Intrepid Initiate (1) — Spellburst: Gain +2 Attack.
			cards.Add("SCH_231", new CardDef(new Power { Trigger = Spellburst(new AddEnchantmentTask("SCH_231e", EntityType.SOURCE)) }));
			cards.Add("SCH_231e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("SCH_231", "attack", 2))) }));
			// [SCH_232] Crimson Hothead (4) — Spellburst: Gain +1 Attack and Taunt.
			cards.Add("SCH_232", new CardDef(new Power { Trigger = Spellburst(new AddEnchantmentTask("SCH_232e", EntityType.SOURCE)) }));
			cards.Add("SCH_232e", FromText("SCH_232e"));
			// [SCH_157] Enchanted Cauldron (3) — Spellburst: Cast a random spell of the same Cost.
			cards.Add("SCH_157", new CardDef(new Power
			{
				Trigger = Spellburst(new CustomTask((g, c, s, t, stack) =>
				{
					int cost = t?[GameTag.TAG_LAST_KNOWN_COST_IN_HAND] ?? 0;
					new CastRandomSpellTask(x => x.Cost == cost).Process(g, c, s, null);
				}))
			}));
			// [SCH_182] Speaker Gidra (3) — Rush, Windfury. Spellburst: Gain Attack and Health equal to the spell's Cost.
			cards.Add("SCH_182", new CardDef(new Power
			{
				Trigger = Spellburst(new CustomTask((g, c, s, t, stack) =>
				{
					int cost = t?[GameTag.TAG_LAST_KNOWN_COST_IN_HAND] ?? 0;
					if (cost > 0) Generic.AddEnchantmentBlock(g, Cards.FromId("SCH_182e"), (IPlayable)s, (IPlayable)s, cost, cost, 0);
				}))
			}));
			cards.Add("SCH_182e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackHealthScriptTag }));
			// [SCH_224] Headmaster Kel'Thuzad (5) — Spellburst: If the spell destroys any minions, summon them.
			cards.Add("SCH_224", new CardDef(new Power
			{
				Trigger = Spellburst(new CustomTask((g, c, s, t, stack) =>
				{
					if (t == null) return;
					var died = c.DiedThisGame.Skip(t[HsTag.DIED_BEFORE_SPELL_MINE]).Concat(c.Opponent.DiedThisGame.Skip(t[HsTag.DIED_BEFORE_SPELL_THEIRS]))
						.Select(id => g.IdEntityDic.TryGetValue(id, out IPlayable p) ? p : null).Where(p => p is Minion).ToList();
					foreach (IPlayable dead in died)
						Summon(g, c, dead.Card, s);
				}))
			}));
			// [SCH_234] Shifty Sophomore (4) — Stealth. Spellburst: Add a Combo card to your hand.
			cards.Add("SCH_234", new CardDef(new Power
			{
				Trigger = Spellburst(new CustomTask((g, c, s, t, stack) =>
				{
					if (RandomCard(g, x => x[GameTag.COMBO] > 0) is Card card) AddToHand(c, card);
				}))
			}));
			// [SCH_236] Diligent Notetaker (2) — Spellburst: Return the spell to your hand. (a new copy of it: "get / add" cards are new)
			cards.Add("SCH_236", new CardDef(new Power
			{
				Trigger = Spellburst(new CustomTask((g, c, s, t, stack) => { if (t != null) AddToHand(c, t.Card); }))
			}));
			// [SCH_313] Wretched Tutor (4) — Spellburst: Deal 2 damage to all other minions.
			cards.Add("SCH_313", new CardDef(new Power { Trigger = Spellburst(new DamageTask(V("SCH_313", "damage", 2), EntityType.ALLMINIONS_NOSOURCE)) }));
			// [SCH_248] Pen Flinger (1) — Battlecry: Deal 1 damage to a minion. Spellburst: Return this to your hand.
			cards.Add("SCH_248", new CardDef(BattlecryMinion, new Power
			{
				PowerTask = new DamageTask(V("SCH_248", "damage", 1), EntityType.TARGET),
				Trigger = Spellburst(new CustomTask((g, c, s, t, stack) => { if (s is Minion me) Generic.ReturnToHand(c, me); }))
			}));

			// ---------------------------------------------------------------- Transfer Student

			// [SCH_199] Transfer Student (2) — "This has different effects based on which game board you're on." Wiki: "At the
			// start of game, Transfer Student's effect is automatically replaced by one of the following ones, depending on the
			// board"; "On special battlefields or Tavern Brawls with forced battlefields, Transfer Student uses its Stormwind
			// version". Before the Year of the Hydra each board has a unique version; from then on the version Discovers a card
			// from that expansion / year (user 2026-10-06). Board ids: the client's Board.json; versions: the wiki's table
			// (Transfer Student (The Museum) = SCH_199t9 ...).
			var transferStudent = new Dictionary<int, string>
			{
				[1] = "SCH_199t", [2] = "SCH_199t2", [3] = "SCH_199t3", [4] = "SCH_199t4", [5] = "SCH_199t5", [6] = "SCH_199t6",
				[7] = "SCH_199t7", [8] = "SCH_199t8", [10] = "SCH_199t9", [11] = "SCH_199t24", [12] = "SCH_199t10", [13] = "SCH_199t11",
				[14] = "SCH_199t12", [26] = "SCH_199t13", [54] = "SCH_199t14", [61] = "SCH_199t15", [64] = "SCH_199t16",
				[65] = "SCH_199t17", [68] = "SCH_199t18", [70] = "SCH_199t19", [73] = "SCH_199t20", [75] = "SCH_199t25",
				[78] = "SCH_199t21", [84] = "SCH_199t22", [86] = "SCH_199t23", [89] = "SCH_199t26", [91] = "SCH_199t27",
				[106] = "SCH_199t28", [118] = "SCH_199t29", [120] = "SCH_199t30", [126] = "SCH_199t31", [129] = "SCH_199t32",
				[130] = "SCH_199t33", [131] = "SCH_199t34", [133] = "SCH_199t35", [134] = "SCH_199t36",
				[140] = "SCH_199t37", [141] = "SCH_199t37", [142] = "SCH_199t37", [143] = "SCH_199t38",
			};
			cards.Add("SCH_199", new CardDef(new Power { BoardVersion = board => transferStudent.TryGetValue(board, out string v) ? v : "SCH_199t" }));
			// the versions (keyword-only ones need no script: SCH_199t, t4, t16, t20)
			cards.Add("SCH_199t2", new CardDef(BattlecryAny, new Power { PowerTask = new DamageTask(V("SCH_199t2", "damage", 2), EntityType.TARGET) }));
			cards.Add("SCH_199t3", new CardDef(BattlecryFriendlyMinion, new Power { PowerTask = new AddEnchantmentTask("SCH_199t3e", EntityType.TARGET) }));
			cards.Add("SCH_199t3e", FromText("SCH_199t3e"));
			cards.Add("SCH_199t5", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (RandomCard(g, x => x.Type == CardType.MINION && x[GameTag.DEATHRATTLE] > 0) is Card card) AddToHand(c, card);
				})
			}));
			ISimpleTask spareParts = new CustomTask((g, c, s, t, stack) =>
			{
				// (WONDERS: the shared Spare Parts list, Generic.RandomSparePart)
				if (Generic.RandomSparePart(g) is Card part) AddToHand(c, part);
			});
			cards.Add("SCH_199t6", new CardDef(new Power { PowerTask = spareParts, DeathrattleTask = spareParts }));
			cards.Add("SCH_199t7", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (c.HandZone.Count > 0) Give(g, s, c.HandZone[g.Random.Next(c.HandZone.Count)], "SCH_199t7e");
					})
				}
			}));
			cards.Add("SCH_199t7e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("SCH_199t7", "discount", 2))) }));
			cards.Add("SCH_199t8", new CardDef(new Power { Trigger = new Trigger(TriggerType.INSPIRE) { SingleTask = new DrawTask() } }));
			cards.Add("SCH_199t9", new CardDef(new Power { PowerTask = new DiscoverTask(DiscoverType.BASIC_HEROPOWERS) }));
			cards.Add("SCH_199t10", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int mana = c.RemainingMana;
					c.UsedMana += mana;
					Summon(g, c, RandomMinionOfCost(g, mana), s);
				})
			}));
			cards.Add("SCH_199t11", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					string[] portals = { "KAR_073", "KAR_075", "KAR_076", "KAR_077", "KAR_091" };
					AddToHand(c, Cards.FromId(portals[g.Random.Next(portals.Length)]));
				})
			}));
			cards.Add("SCH_199t12", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.HandZone.Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.Count > 0) Give(g, s, minions[g.Random.Next(minions.Count)], "SCH_199t12e");
				})
			}));
			cards.Add("SCH_199t12e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("SCH_199t12", "stats", 2))) }));
			cards.Add("SCH_199t13", new CardDef(new Power { PowerTask = new AdaptTask(EntityType.SOURCE) }));
			cards.Add("SCH_199t14", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (RandomCard(g, x => x.Set == CardSet.ICECROWN) is Card card) AddToHand(c, card);
				})
			}));
			cards.Add("SCH_199t15", new CardDef(new Power { PowerTask = new RecruitTask(1, SelfCondition.IsCost(V("SCH_199t15", "cost", 2), RelaSign.LEQ)) }));
			cards.Add("SCH_199t17", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.BaseMana >= V("SCH_199t17", "mana crystals", 10), new AddEnchantmentTask("SCH_199t17e", EntityType.SOURCE))
			}));
			cards.Add("SCH_199t17e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("SCH_199t17", "stats", 5))) }));
			cards.Add("SCH_199t18", new CardDef(new Power { OverkillTask = new DrawTask() }));
			cards.Add("SCH_199t19", new CardDef(new Power { PowerTask = new AddLackeyTask(1) }));
			cards.Add("SCH_199t21", new CardDef(new Power { PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.IsRace(Race.DRAGON)) }));
			cards.Add("SCH_199t22", new CardDef(new Power
			{
				AwakenTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
					for (int i = 0; i < V("SCH_199t22", "minions", 2) && enemies.Count > 0; i++)
					{
						Minion m = enemies[g.Random.Next(enemies.Count)];
						enemies.Remove(m);
						Generic.DamageCharFunc((IPlayable)s, m, V("SCH_199t22", "damage", 3), false);
					}
				})
			}));
			ISimpleTask AddRandom(System.Func<Card, bool> which) => new CustomTask((g, c, s, t, stack) => { if (RandomCard(g, which) is Card card) AddToHand(c, card); });
			cards.Add("SCH_199t23", new CardDef(new Power { PowerTask = AddRandom(x => x[GameTag.MULTIPLE_CLASSES] > 0) }));
			cards.Add("SCH_199t24", new CardDef(new Power { PowerTask = AddRandom(x => x.Type == CardType.WEAPON) }));
			cards.Add("SCH_199t25", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					string[] plagues = { "ULD_172", "ULD_707", "ULD_715", "ULD_717", "ULD_718" };
					AddToHand(c, Cards.FromId(plagues[g.Random.Next(plagues.Length)]));
				})
			}));
			cards.Add("SCH_199t27", new CardDef(new Power { PowerTask = AddRandom(x => x.Type == CardType.SPELL && x.Name != null && x.Name.EndsWith("(Rank 1)")) }));
			cards.Add("SCH_199t28", new CardDef(BattlecryFriendlyMinion, new Power { PowerTask = new SetGameTagTask(GameTag.DIVINE_SHIELD, 1, EntityType.TARGET) }));
			// the Discover versions: a card from that expansion / year (its set ids; minisets share them)
			void DiscoverFrom(string id, params int[] sets) =>
				cards.Add(id, new CardDef(new Power { PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => sets.Contains((int)x.Set)) }));
			DiscoverFrom("SCH_199t30", 1658);              // Voyage to the Sunken City
			DiscoverFrom("SCH_199t31", 1691);              // Murder at Castle Nathria
			DiscoverFrom("SCH_199t32", 1776);              // March of the Lich King
			DiscoverFrom("SCH_199t33", 1809);              // Festival of Legends
			DiscoverFrom("SCH_199t34", 1858);              // TITANS
			DiscoverFrom("SCH_199t35", 1892);              // Showdown in the Badlands
			DiscoverFrom("SCH_199t36", 1897);              // Whizbang's Workshop
			DiscoverFrom("SCH_199t37", 1946, 1952, 1957);  // Year of the Raptor (Emerald Dream, Lost City, Timeways)
			DiscoverFrom("SCH_199t38", 1980);              // Year of the Scarab (Cataclysm)
			// SCH_199t26 (Darkmoon Faire: Corrupt) and SCH_199t29 (Alterac: Honorable Kill) come with those sets' mechanics.

			// ---------------------------------------------------------------- Neutral

			// [SCH_142] Voracious Reader (3) — At the end of your turn, draw until you have 3 cards.
			cards.Add("SCH_142", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						int want = V("SCH_142", "hand", 3);
						while (c.HandZone.Count < want && c.DeckZone.Count > 0 && Generic.Draw(c) != null) { }
					})
				}
			}));
			// [SCH_146] Robes of Protection (3) — Your minions have Elusive.
			cards.Add("SCH_146", new CardDef(new Power { Aura = new Aura(AuraType.BOARD, "SCH_146e") }));
			cards.Add("SCH_146e", new CardDef(new Power { Enchant = new Enchant(Effects.CantBeTargetedBySpellsAndHeroPowers) }));
			// [SCH_162] Vectus (5) — Battlecry: Summon two 1/1 Whelps. Each gains a Deathrattle from your minions that died this game.
			cards.Add("SCH_162", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var rattles = c.DiedThisGameCards.Where(p => p is Minion && p.Card.Power?.DeathrattleTask != null).ToList();
					for (int i = 0; i < V("SCH_162", "whelps", 2) && !c.BoardZone.IsFull; i++)
					{
						var whelp = (Minion)Entity.FromCard(c, Cards.FromId("SCH_162t"));
						Generic.SummonBlock.Invoke(g, whelp, -1, s);
						if (rattles.Count > 0) // Unearthed Raptor's way: the enchantment captures the minion's Deathrattle
							Generic.AddEnchantmentBlock(g, Cards.FromId("SCH_162e"), (IPlayable)s, whelp, 0, 0, rattles[g.Random.Next(rattles.Count)].Id);
					}
				})
			}));
			cards.Add("SCH_162e", new CardDef(new Power { DeathrattleTask = ActivateCapturedDeathrattleTask.Task }));
			// [SCH_235] Devolving Missiles (1) — Shoot three missiles at random enemy minions that transform them into ones that cost (1) less.
			cards.Add("SCH_235", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("SCH_235", "missiles", 3); i++)
					{
						var enemies = c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
						if (enemies.Count == 0) break;
						TransformCostShifted(g, new[] { enemies[g.Random.Next(enemies.Count)] }, -V("SCH_235", "cost", 1));
					}
				})
			}));
			// [SCH_245] Steward of Scrolls (5) — Spell Damage +1. Battlecry: Discover a spell.
			cards.Add("SCH_245", new CardDef(new Power { PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL) }));
			// [SCH_259] Sphere of Sapience (1) — At the start of your turn, look at your top card. You can put it on the bottom
			// and lose 1 Durability. Live (live-20261002 session-20261002-204752, two turns): OVERRIDE_HISTORY, a copy of the top card
			// (Generic.DeckCardCopies), EFFECT_TIMING, "A New Fate"; after the pick both options go to the graveyard
			// (Generic.OptionsToGraveyard) — nothing to the hand; the top card is then drawn as usual.
			cards.Add("SCH_259", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable top = c.DeckZone.TopCard;
						if (top == null) return;
						if (g.History)
							g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.OVERRIDE_HISTORY, Data = 0, Info = new List<int> { s.Id } });
						List<IPlayable> options = Generic.DeckCardCopies(c, s, new[] { top });
						if (g.History)
							g.PowerHistory.Add(new PowerHistoryMetaData { Type = MetaDataType.EFFECT_TIMING, Data = 0, Info = new List<int> { s.Id } });
						options.Add(Entity.FromCard(c, Cards.FromId("SCH_259t"), new EntityData { { GameTag.CREATOR, s.Id } }, c.SetasideZone));
						List<int> ids = options.Select(p => p.Id).ToList();
						Generic.CreateChoiceEntities(c, s, options, ChoiceAction.STACK, new CustomTask((g2, c2, s2, picked, st2) =>
						{
							Generic.OptionsToGraveyard(c2, s2, ids, picked);
							// the top card's copy: it stays on top; "A New Fate" (the option that isn't a deck card): to the bottom
							if (picked == null || picked[GameTag.LINKED_ENTITY] != 0) return;
							if (!g2.IdEntityDic.TryGetValue(top.Id, out IPlayable card) || card.Zone?.Type != Zone.DECK || card.Controller != c2) return;
							c2.DeckZone.Remove(card);
							c2.DeckZone.Add(card, 0); // the bottom of the deck (a deck reorder, as Lorekeeper Polkelt's)
							if (s2 is Weapon w && c2.Hero.Weapon == w) w.Damage += 1;
						}));
					})
				}
			}));
			cards.Add("SCH_259t", EngineOnly); // a choice the Sphere resolves (never cast)
			// [SCH_273] Ras Frostwhisper (5) — At the end of your turn, deal 1 damage to all enemies (improved by Spell Damage).
			cards.Add("SCH_273", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
						new DamageTask(V("SCH_273", "damage", 1) + c.CurrentSpellPower, EntityType.ENEMIES).Process(g, c, s, t))
				}
			}));
			// [SCH_283] Manafeeder Panthara (2) — Battlecry: If you've used your Hero Power this turn, draw a card.
			cards.Add("SCH_283", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.HeroPowerActivationsThisTurn > 0, new DrawTask())
			}));
			// [SCH_311] Animated Broomstick (1) — Rush. Battlecry: Give your other minions Rush.
			cards.Add("SCH_311", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().Where(m => m.Id != s.Id)) m[GameTag.RUSH] = 1;
				})
			}));
			// [SCH_350] Wand Thief (1) — Combo: Discover a Mage spell.
			cards.Add("SCH_350", new CardDef(new Power { ComboTask = DiscoverFromTask.Where((c, x) => x.Type == CardType.SPELL && x.HasClass(CardClass.MAGE)) }));
			// [SCH_351] Jandice Barov (5) — Battlecry: Summon two random 5-Cost minions. Secretly pick one that dies when it
			// takes damage. (both show "might be an illusion"; the pick among the two, as a Discover of existing cards)
			cards.Add("SCH_351", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var summoned = new List<IPlayable>();
					for (int i = 0; i < V("SCH_351", "minions", 2) && !c.BoardZone.IsFull; i++)
					{
						if (!(RandomMinionOfCost(g, V("SCH_351", "cost", 5)) is Card card)) break;
						var m = (Minion)Entity.FromCard(c, card);
						Generic.SummonBlock.Invoke(g, m, -1, s);
						Give(g, s, m, "SCH_351e2");
						summoned.Add(m);
					}
					if (summoned.Count == 0) return;
					Generic.DiscoverFromCards(c, s, summoned, (g2, c2, s2, picked) => Give(g2, s2, picked, "SCH_351e"));
				})
			}));
			cards.Add("SCH_351e2", new CardDef(new Power()));
			cards.Add("SCH_351e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.ENCHANTMENT_TARGET,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m) m.Destroy(); })
				}
			}));
			// [SCH_352] Potion of Illusion (4) — Add 1/1 copies of your minions to your hand. They cost (1).
			cards.Add("SCH_352", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().ToList())
					{
						if (c.HandZone.IsFull) break;
						IPlayable copy = Entity.FromCard(c, m.Card);
						if (!Generic.AddHandPhase.Invoke(c, copy)) break;
						Give(g, s, copy, "SCH_352e");
						Give(g, s, copy, "SCH_352e2");
					}
				})
			}));
			cards.Add("SCH_352e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("SCH_352", "stats", 1))) }));
			cards.Add("SCH_352e2", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(V("SCH_352", "cost", 1))) }));
			// [SCH_425] Doctor Krastinov (5) — Rush. Whenever this attacks, give your weapon +1/+1.
			cards.Add("SCH_425", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (c.Hero.Weapon is Weapon w) Give(g, s, w, "SCH_425e"); })
				}
			}));
			cards.Add("SCH_425e", new CardDef(new Power { Enchant = new Enchant(WeaponStats(V("SCH_425", "stats", 1))) }));
			// [SCH_428] Lorekeeper Polkelt (4) — Battlecry: Reorder your deck from the highest Cost card to the lowest Cost card.
			cards.Add("SCH_428", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					// the deck's top is its last slot: lowest cost first in slot 0, highest on top (ties random)
					var ordered = c.DeckZone.OrderBy(p => p.Cost).ThenBy(_ => g.Random.Next()).ToList();
					foreach (IPlayable p in ordered) c.DeckZone.Remove(p);
					foreach (IPlayable p in ordered) c.DeckZone.Add(p); // each on top: the highest ends on top
				})
			}));
			// [SCH_530] Sorcerous Substitute (6) — Battlecry: If you have Spell Damage, summon a copy of this.
			cards.Add("SCH_530", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.CurrentSpellPower > 0, new SummonCopyTask(EntityType.SOURCE))
			}));
			// [SCH_537] Trick Totem (2) — At the end of your turn, cast a random spell that costs (3) or less.
			cards.Add("SCH_537", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new CastRandomSpellTask(x => x.Cost <= V("SCH_537", "cost", 3)) }
			}));
			// [SCH_707] Fishy Flyer / [SCH_708] Sneaky Delinquent / [SCH_709] Smug Senior — Deathrattle: Add a Ghost to your hand.
			cards.Add("SCH_707", new CardDef(new Power { DeathrattleTask = new AddCardTo("SCH_707t", EntityType.HAND) }));
			cards.Add("SCH_708", new CardDef(new Power { DeathrattleTask = new AddCardTo("SCH_708t", EntityType.HAND) }));
			cards.Add("SCH_709", new CardDef(new Power { DeathrattleTask = new AddCardTo("SCH_709t", EntityType.HAND) }));
			// [SCH_710] Ogremancer (5) — Whenever your opponent casts a spell, summon a 2/2 Skeleton with Taunt.
			cards.Add("SCH_710", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.CAST_SPELL) { TriggerSource = TriggerSource.ENEMY, SingleTask = new SummonTask("SCH_710t", SummonSide.RIGHT) }
			}));
			// [SCH_711] Plagued Protodrake (8) — Deathrattle: Summon a random 7-Cost minion.
			cards.Add("SCH_711", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, RandomMinionOfCost(g, V("SCH_711", "cost", 7)), s))
			}));
			// [SCH_714] Educated Elekk (3) — Whenever a spell is played, this minion remembers it. Deathrattle: Shuffle the spells
			// into your deck. (each remembered spell is an enchantment holding its card; its Deathrattle shuffles a new one)
			cards.Add("SCH_714", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_CARD)
				{
					TriggerSource = TriggerSource.ALL,
					Condition = SelfCondition.IsSpell,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t != null) Generic.AddEnchantmentBlock(g, Cards.FromId("SCH_714e"), (IPlayable)s, (IPlayable)s, t.Card.AssetId, 0, 0);
					})
				}
			}));
			cards.Add("SCH_714e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t != null && Cards.FromAssetId(t[GameTag.TAG_SCRIPT_DATA_NUM_1]) is Card spell)
						Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, spell));
				})
			}));

			// ---------------------------------------------------------------- Demon Hunter

			// [SCH_252] Marrowslicer (4) — Battlecry: Shuffle 2 Soul Fragments into your deck.
			cards.Add("SCH_252", new CardDef(new Power { PowerTask = ShuffleSoulFragments(V("SCH_252", "fragments", 2)) }));
			// [SCH_253] Cycle of Hatred (7) — Deal 3 damage to all minions. Summon a 3/3 Spirit for every minion killed.
			cards.Add("SCH_253", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var all = c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).ToList();
					new DamageTask(V("SCH_253", "damage", 3), EntityType.ALLMINIONS, true).Process(g, c, s, t);
					int killed = all.Count(m => m.IsDead || m.ToBeDestroyed || m.Health <= 0);
					g.DeathProcessingAndAuraUpdate();
					for (int i = 0; i < killed && !c.BoardZone.IsFull; i++) Summon(g, c, Cards.FromId("SCH_253t"), s);
				})
			}));
			// [SCH_354] Ancient Void Hound (9) — At the end of your turn, steal 1 Attack and Health from all enemy minions.
			// (a minion with 0 Attack gives only Health: the client's "Reduced Health" / "Increased Health" halves)
			cards.Add("SCH_354", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me)) return;
						foreach (Minion m in c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed).ToList())
						{
							bool attack = m.AttackDamage > 0;
							Give(g, s, m, attack ? "SCH_354e" : "SCH_354eb");
							Give(g, s, me, attack ? "SCH_354e2" : "SCH_354e2b");
						}
					})
				}
			}));
			cards.Add("SCH_354e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(-V("SCH_354", "steal", 1))) }));
			cards.Add("SCH_354eb", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(-V("SCH_354", "steal", 1))) }));
			cards.Add("SCH_354e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("SCH_354", "steal", 1))) }));
			cards.Add("SCH_354e2b", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("SCH_354", "steal", 1))) }));
			// [SCH_355] Shardshatter Mystic (3) — Battlecry: Destroy a Soul Fragment in your deck to deal 3 damage to all other minions.
			cards.Add("SCH_355", new CardDef(new Power
			{
				PowerTask = DestroySoulFragmentTo(new DamageTask(V("SCH_355", "damage", 3), EntityType.ALLMINIONS_NOSOURCE))
			}));
			// [SCH_356] Glide (4) — Shuffle your hand into your deck. Draw 4 cards. Outcast: Your opponent does the same.
			ISimpleTask GlideFor(bool opponent) => new CustomTask((g, c, s, t, stack) =>
			{
				Controller who = opponent ? c.Opponent : c;
				foreach (IPlayable p in who.HandZone.ToList()) Generic.MoveToDeck(who, s, p);
				for (int i = 0; i < V("SCH_356", "cards", 4); i++) Generic.Draw(who);
			});
			cards.Add("SCH_356", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(GlideFor(false),
					new ConditionTask(EntityType.SOURCE, PlayedAsOutcast), new FlagTask(true, GlideFor(true)))
			}));
			// [SCH_357] Fel Guardians (7) — Summon three 1/2 Demons with Taunt. Costs (1) less whenever a friendly minion dies.
			cards.Add("SCH_357", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new SummonTask("SCH_357t", V("SCH_357", "demons", 3), SummonSide.SPELL),
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerActivation = TriggerActivation.HAND,
					TriggerSource = TriggerSource.MINIONS,
					SingleTask = new AddEnchantmentTask("SCH_357e", EntityType.SOURCE)
				}
			}));
			cards.Add("SCH_357e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("SCH_357", "discount", 1))) }));
			// [SCH_422] Double Jump (1) — Draw an Outcast card from your deck.
			cards.Add("SCH_422", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p.Card[OUTCAST] > 0)) }));
			// [SCH_538] Ace Hunter Kreen (3) — Your other characters are Immune while attacking. (Gladiator's Longbow's way:
			// an attacker gets an enchantment that goes after its attack)
			cards.Add("SCH_538", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TARGET)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t is ICharacter attacker && attacker.Id != s.Id && attacker.Controller == c) Give(g, s, (IPlayable)attacker, "SCH_538e");
					})
				}
			}));
			cards.Add("SCH_538e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Immune),
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.ENCHANTMENT_TARGET, SingleTask = RemoveEnchantmentTask.Task }
			}));
			// [SCH_600] Demon Companion (1) — Summon a random Demon Companion.
			cards.Add("SCH_600", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					string[] companions = { "SCH_600t1", "SCH_600t2", "SCH_600t3" };
					Summon(g, c, Cards.FromId(companions[g.Random.Next(companions.Length)]), s);
				})
			}));
			cards.Add("SCH_600t3", new CardDef(new Power { Aura = new Aura(AuraType.BOARD_EXCEPT_SOURCE, "SCH_600t3e") }));
			cards.Add("SCH_600t3e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("SCH_600t3", "attack", 1))) }));
			// [SCH_603] Star Student Stelina (4) — Outcast: Look at 3 cards in your opponent's hand. Shuffle one of them into their deck.
			cards.Add("SCH_603", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new ConditionTask(EntityType.SOURCE, PlayedAsOutcast), new FlagTask(true, new CustomTask((g, c, s, t, stack) =>
					Generic.DiscoverFromCards(c, s, Generic.PickFrom(c.Opponent.HandZone, g, _ => true),
						(g2, c2, s2, picked) => { if (picked.Zone?.Type == SabberStoneCore.Enums.Zone.HAND) Generic.MoveToDeck(picked.Controller, s2, picked); }))))
			}));
			// [SCH_618] Blood Herald (5) — Whenever a friendly minion dies while this is in your hand, gain +1/+1.
			cards.Add("SCH_618", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerActivation = TriggerActivation.HAND,
					TriggerSource = TriggerSource.MINIONS,
					SingleTask = new AddEnchantmentTask("SCH_618e", EntityType.SOURCE)
				}
			}));
			cards.Add("SCH_618e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("SCH_618", "stats", 1))) }));
			// [SCH_704] Soulshard Lapidary (5) — Battlecry: Destroy a Soul Fragment in your deck to give your hero +5 Attack this turn.
			cards.Add("SCH_704", new CardDef(new Power { PowerTask = DestroySoulFragmentTo(new AddEnchantmentTask("SCH_704e", EntityType.HERO)) }));
			cards.Add("SCH_704e", OneTurn(Effects.Attack_N(V("SCH_704", "attack", 5))));
			// [SCH_705] Vilefiend Trainer (4) — Outcast: Summon two 1/1 Demons.
			cards.Add("SCH_705", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new ConditionTask(EntityType.SOURCE, PlayedAsOutcast),
					new FlagTask(true, new SummonTask("SCH_705t", V("SCH_705", "demons", 2))))
			}));
			// [SCH_279] Trueaim Crescent (1) — After your Hero attacks a minion, your minions attack it too.
			cards.Add("SCH_279", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(g.CurrentEventData?.EventTarget is Minion defender)) return;
						foreach (Minion m in c.BoardZone.GetAll().ToList())
						{
							if (defender.IsDead || defender.ToBeDestroyed || defender.Zone?.Type != SabberStoneCore.Enums.Zone.PLAY) break;
							if (!m.IsDead && !m.ToBeDestroyed) EffectAttack(g, m, defender);
						}
					})
				}
			}));

			// ---------------------------------------------------------------- Druid

			// [SCH_242] Gibberling (2) — Spellburst: Summon a Gibberling.
			cards.Add("SCH_242", new CardDef(new Power { Trigger = Spellburst(new SummonTask("SCH_242", SummonSide.RIGHT)) }));
			// [SCH_427] Lightning Bloom (0) — Refresh 2 Mana Crystals. Overload: (2)
			cards.Add("SCH_427", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => c.UsedMana = System.Math.Max(0, c.UsedMana - V("SCH_427", "mana", 2)))
			}));
			// [SCH_606] Partner Assignment (1) — Add a random 2-Cost and 3-Cost Beast to your hand.
			cards.Add("SCH_606", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (int cost in new[] { V("SCH_606", "first cost", 2), V("SCH_606", "second cost", 3) })
						if (RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.BEAST) && x.Cost == cost) is Card card) AddToHand(c, card);
				})
			}));
			// [SCH_609] Survival of the Fittest (10) — Give +4/+4 to all minions in your hand, deck, and battlefield.
			cards.Add("SCH_609", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.HandZone.Concat(c.DeckZone).Concat(c.BoardZone).Where(p => Generic.IsMinionCard(p)).ToList(), "SCH_609e"))
			}));
			cards.Add("SCH_609e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("SCH_609", "stats", 4))) }));
			// [SCH_612] Runic Carvings (6) — Choose One - Summon four 2/2 Treant Totems; or Overload: (2) to summon them with Rush.
			// (its options are the client's related cards; the "both" version: the Rush one)
			ISimpleTask Treants(bool rush) => new CustomTask((g, c, s, t, stack) =>
			{
				for (int i = 0; i < V("SCH_612", "treants", 4) && !c.BoardZone.IsFull; i++)
				{
					var treant = (Minion)Entity.FromCard(c, Cards.FromId("SCH_612t"));
					Generic.SummonBlock.Invoke(g, treant, -1, s);
					if (rush) treant[GameTag.RUSH] = 1;
				}
			});
			cards.Add("SCH_612", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power { PowerTask = Treants(true) }));
			cards.Add("SCH_612a", new CardDef(new Power { PowerTask = Treants(false) }));
			cards.Add("SCH_612b", new CardDef(new Power { PowerTask = Treants(true) }));
			// [SCH_613] Groundskeeper (4) — Taunt. Battlecry: If you're holding a spell that costs (5) or more, restore 5 Health.
			// (targeted: its arrow text "Restore #5 Health", wiki)
			// (HsServer EMERALD_DREAM pass: a target only while holding such a spell — Power.TargetIf, the shared conditional target)
			cards.Add("SCH_613", new CardDef(BattlecryAny, new Power
			{
				TargetIf = (PlayReq.REQ_TARGET_IF_AVAILABLE, (src, target) => src.Controller.HandZone.Any(p => p is Spell && p.Id != src.Id && p.Cost >= V("SCH_613", "spell cost", 5))),
				PowerTask = ComplexTask.If((g, c, s) => c.HandZone.Any(p => p is Spell && p.Cost >= V("SCH_613", "spell cost", 5)),
					new HealTask(V("SCH_613", "heal", 5), EntityType.TARGET))
			}));
			// [SCH_614] Forest Warden Omu (6) — Spellburst: Refresh your Mana Crystals.
			cards.Add("SCH_614", new CardDef(new Power { Trigger = Spellburst(new CustomTask((g, c, s, t, stack) => c.UsedMana = 0)) }));
			// [SCH_616] Twilight Runner (5) — Stealth. Whenever this attacks, draw 2 cards.
			cards.Add("SCH_616", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK) { TriggerSource = TriggerSource.SELF, SingleTask = new EnqueueTask(V("SCH_616", "cards", 2), new DrawTask()) }
			}));

			// ---------------------------------------------------------------- Hunter

			// [SCH_133] Wolpertinger (1) — Battlecry: Summon a copy of this.
			cards.Add("SCH_133", new CardDef(new Power { PowerTask = new SummonCopyTask(EntityType.SOURCE) }));
			// [SCH_239] Krolusk Barkstripper (4) — Spellburst: Destroy a random enemy minion.
			cards.Add("SCH_239", new CardDef(new Power
			{
				Trigger = Spellburst(ComplexTask.Create(new RandomTask(1, EntityType.OP_MINIONS, harmful: true), new DestroyTask(EntityType.STACK)))
			}));
			// [SCH_244] Teacher's Pet (5) — Taunt. Deathrattle: Summon a random 3-Cost Beast.
			cards.Add("SCH_244", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
					Summon(g, c, RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.BEAST) && x.Cost == V("SCH_244", "cost", 3) && !x.NeverRandomlyOnBoard), s))
			}));
			// [SCH_340] Bloated Python (3) — Deathrattle: Summon a 4/4 Hapless Handler.
			cards.Add("SCH_340", new CardDef(new Power { DeathrattleTask = new SummonTask("SCH_340t") }));
			// [SCH_539] Professor Slate (3) — Your spells are Poisonous. (Character.TakeDamage: HsTag.SPELLS_POISONOUS)
			cards.Add("SCH_539", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER, new Effect(HsTag.SPELLS_POISONOUS, EffectOperator.ADD, 1))
			}));
			// [SCH_604] Overwhelm (1) — Deal 2 damage to a minion. Deal one more damage for each Beast you control.
			cards.Add("SCH_604", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m)
						Generic.DamageCharFunc((IPlayable)s, m, V("SCH_604", "damage", 2) + c.BoardZone.Count(b => b.IsRace(Race.BEAST)), false);
				})
			}));
			// [SCH_607] Shan'do Wildclaw (3) — Choose One - Give Beasts in your deck +1/+1; or Transform into a copy of a friendly Beast.
			cards.Add("SCH_607a", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_RACE, (int)Race.BEAST } },
				new Power { PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion beast && s is Minion me) new TransformCopyTask().Process(g, c, me, beast); }) }));
			cards.Add("SCH_607b", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.DeckZone.Where(p => p is Minion m && m.IsRace(Race.BEAST)).ToList(), "SCH_607e"))
			}));
			cards.Add("SCH_607e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("SCH_607", "stats", 1))) }));
			cards.Add("SCH_607", new CardDef(new Power
			{
				PowerTask = cards["SCH_607b"].Power.PowerTask // "choose both" (Fandral): the deck buff (no target)
			}));
			// [SCH_610] Guardian Animals (7) — Summon two Beasts that cost (5) or less from your deck. Give them Rush.
			cards.Add("SCH_610", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new RecruitTask(V("SCH_610", "beasts", 2), true, SelfCondition.IsRace(Race.BEAST), SelfCondition.IsCost(V("SCH_610", "cost", 5), RelaSign.LEQ)),
					new SetGameTagTask(GameTag.RUSH, 1, EntityType.STACK))
			}));
			// [SCH_617] Adorable Infestation (1) — Give a minion +1/+1. Summon a 1/1 Cub. Add a Cub to your hand.
			cards.Add("SCH_617", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("SCH_617e", EntityType.TARGET),
					new SummonTask("SCH_617t", SummonSide.SPELL), new AddCardTo("SCH_617t", EntityType.HAND))
			}));
			cards.Add("SCH_617e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("SCH_617", "stats", 1))) }));

			// ---------------------------------------------------------------- Mage

			// [SCH_241] Firebrand (3) — Spellburst: Deal 4 damage randomly split among all enemy minions.
			cards.Add("SCH_241", new CardDef(new Power
			{
				Trigger = Spellburst(new EnqueueTask(V("SCH_241", "damage", 4), ComplexTask.DamageRandomTargets(1, EntityType.OP_MINIONS, 1)))
			}));
			// [SCH_243] Wyrm Weaver (4) — Spellburst: Summon two 1/3 Mana Wyrms.
			cards.Add("SCH_243", new CardDef(new Power { Trigger = Spellburst(new SummonTask("NEW1_012", V("SCH_243", "wyrms", 2))) }));
			// [SCH_348] Combustion (3) — Deal 4 damage to a minion. Any excess damages both neighbors.
			cards.Add("SCH_348", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					var neighbors = m.GetAdjacentMinions().ToList();
					int before = m.Health;
					int dealt = Generic.DamageCharFunc((IPlayable)s, m, V("SCH_348", "damage", 4), false);
					int excess = System.Math.Max(0, dealt - before);
					if (excess > 0)
						foreach (Minion n in neighbors) n.TakeDamage((IPlayable)s, excess);
				})
			}));
			// [SCH_353] Cram Session (2) — Draw 1 card (improved by Spell Damage).
			cards.Add("SCH_353", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("SCH_353", "cards", 1) + c.CurrentSpellPower; i++) Generic.Draw(c);
				})
			}));
			// [SCH_400] Mozaki, Master Duelist (5) — After you cast a spell, gain Spell Damage +1.
			cards.Add("SCH_400", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = new AddEnchantmentTask("SCH_400e2", EntityType.SOURCE) }
			}));
			cards.Add("SCH_400e2", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.SPELLPOWER, EffectOperator.ADD, V("SCH_400", "spell damage", 1))) }));

			// ---------------------------------------------------------------- Paladin

			// [SCH_135] Turalyon, the Tenured (8) — Rush. Whenever this attacks a minion, set the defender's Attack and Health to 3.
			cards.Add("SCH_135", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					Condition = SelfCondition.IsEventTargetIs(CardType.MINION),
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (g.CurrentEventData?.EventTarget is Minion d) Give(g, s, d, "SCH_135e"); })
				}
			}));
			cards.Add("SCH_135e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("SCH_135", "stats", 3))) }));
			// [SCH_138] Blessing of Authority (5) — Give a minion +8/+8. It can't attack heroes this turn.
			cards.Add("SCH_138", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("SCH_138e", EntityType.TARGET), new AddEnchantmentTask("SCH_138e2", EntityType.TARGET))
			}));
			cards.Add("SCH_138e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("SCH_138", "stats", 8))) }));
			cards.Add("SCH_138e2", OneTurn(new Effect(GameTag.CANNOT_ATTACK_HEROES, EffectOperator.SET, 1)));
			// [SCH_139] Devout Pupil (6) — Divine Shield, Taunt. Costs (1) less for each spell you've cast on friendly characters this game.
			cards.Add("SCH_139", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.PlayHistory.Count(e => e.SourceCard.Type == CardType.SPELL && e.TargetCard != null &&
					e.TargetController == p.Controller.PlayerId))
			}));
			// [SCH_141] High Abbess Alura (4) — Spellburst: Cast a spell from your deck (targets this if possible).
			cards.Add("SCH_141", new CardDef(new Power
			{
				Trigger = Spellburst(new CustomTask((g, c, s, t, stack) =>
				{
					var spells = c.DeckZone.Where(p => p is Spell).ToList();
					if (spells.Count == 0) return;
					var spell = (Spell)spells[g.Random.Next(spells.Count)];
					Generic.CastFromDeck(c, s, spell, targetsSourceIfPossible: true);
				}))
			}));
			// [SCH_149] Argent Braggart (2) — Battlecry: Set this minion's Attack and Health to the highest in the battlefield.
			cards.Add("SCH_149", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var others = c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => m.Id != s.Id).ToList();
					if (others.Count == 0) return;
					int atk = others.Max(m => m.AttackDamage), hp = others.Max(m => m.Health);
					Generic.AddEnchantmentBlock(g, Cards.FromId("SCH_149e"), (IPlayable)s, (IPlayable)s, atk, hp, 0);
				})
			}));
			cards.Add("SCH_149e", new CardDef(new Power { Enchant = Enchants.Enchants.SetAttackHealthScriptTag }));
			// [SCH_247] First Day of School (1) — Add 2 random 1-Cost minions to your hand.
			cards.Add("SCH_247", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("SCH_247", "minions", 2); i++)
						if (RandomCard(g, x => x.Type == CardType.MINION && x.Cost == V("SCH_247", "cost", 1)) is Card card) AddToHand(c, card);
				})
			}));
			// [SCH_250] Wave of Apathy (1) — Set the Attack of all enemy minions to 1 until your next turn.
			// (a SET can't be undone when the enchantment goes: the change to 1 is an ADD of (1 - Attack), which can)
			cards.Add("SCH_250", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.Opponent.BoardZone.GetAll().ToList())
						Generic.AddEnchantmentBlock(g, Cards.FromId("SCH_250e"), (IPlayable)s, m, V("SCH_250", "attack", 1) - m.AttackDamage, 0, 0);
				})
			}));
			cards.Add("SCH_250e", new CardDef(new Power
			{
				Enchant = Enchants.Enchants.AddAttackScriptTag,
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = RemoveEnchantmentTask.Task }
			}));
			// [SCH_302] Gift of Luminance (3) — Give a minion Divine Shield, then summon a 1/1 copy of it.
			cards.Add("SCH_302", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new SetGameTagTask(GameTag.DIVINE_SHIELD, 1, EntityType.TARGET),
					new SummonCopyTask(EntityType.TARGET, addToStack: true), new AddEnchantmentTask("SCH_302e", EntityType.STACK))
			}));
			cards.Add("SCH_302e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(V("SCH_302", "stats", 1))) }));
			// [SCH_523] Ceremonial Maul (3) — Spellburst: Summon a Student with Taunt and stats equal to the spell's Cost.
			cards.Add("SCH_523", new CardDef(new Power
			{
				Trigger = Spellburst(new CustomTask((g, c, s, t, stack) =>
				{
					int cost = t?[GameTag.TAG_LAST_KNOWN_COST_IN_HAND] ?? 0;
					if (cost <= 0 || c.BoardZone.IsFull) return;
					var student = (Minion)Entity.FromCard(c, Cards.FromId("SCH_523t"));
					student.AttackDamage = cost;
					student.BaseHealth = cost;
					Generic.SummonBlock.Invoke(g, student, -1, s);
				}))
			}));
			// [SCH_524] Shield of Honor (1) — Give a damaged minion +3 Attack and Divine Shield.
			cards.Add("SCH_524", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_DAMAGED_TARGET, 0 } },
				new Power { PowerTask = new AddEnchantmentTask("SCH_524e", EntityType.TARGET) }));
			cards.Add("SCH_524e", FromText("SCH_524e"));
			// [SCH_526] Lord Barov (3) — Battlecry: Set the Health of all other minions to 1. Deathrattle: Deal 1 damage to all minions.
			cards.Add("SCH_526", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => m.Id != s.Id).ToList(), "SCH_526e")),
				DeathrattleTask = new DamageTask(V("SCH_526", "damage", 1), EntityType.ALLMINIONS)
			}));
			cards.Add("SCH_526e", new CardDef(new Power { Enchant = new Enchant(Effects.SetMaxHealth(V("SCH_526", "health", 1))) }));
			// [SCH_532] Goody Two-Shields (3) — Divine Shield. Spellburst: Gain Divine Shield.
			cards.Add("SCH_532", new CardDef(new Power { Trigger = Spellburst(new SetGameTagTask(GameTag.DIVINE_SHIELD, 1, EntityType.SOURCE)) }));
			// [SCH_533] Commencement (7) — Summon a minion from your deck. Give it Taunt and Divine Shield.
			cards.Add("SCH_533", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new RecruitTask(1, true), new AddEnchantmentTask("SCH_533e", EntityType.STACK))
			}));
			cards.Add("SCH_533e", FromText("SCH_533e"));

			// ---------------------------------------------------------------- Priest

			// [SCH_120] Cabal Acolyte (4) — Taunt. Spellburst: Gain control of a random enemy minion with 2 or less Attack.
			cards.Add("SCH_120", new CardDef(new Power
			{
				Trigger = Spellburst(new CustomTask((g, c, s, t, stack) =>
				{
					var weak = c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed && m.AttackDamage <= V("SCH_120", "attack", 2)).ToList();
					if (weak.Count > 0 && !c.BoardZone.IsFull) new ControlTask(EntityType.TARGET).Process(g, c, s, weak[g.Random.Next(weak.Count)]);
				}))
			}));
			// [SCH_126] Disciplinarian Gandling (4) — After you play a minion, destroy it and summon a 4/4 Failed Student.
			cards.Add("SCH_126", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(t is Minion played) || played.Zone?.Type != SabberStoneCore.Enums.Zone.PLAY) return;
						played.Destroy();
						Summon(g, c, Cards.FromId("SCH_126t"), played);
					})
				}
			}));
			// [SCH_136] Power Word: Feast (2) — Give a minion +2/+2. Restore it to full Health at the end of this turn.
			cards.Add("SCH_136", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("SCH_136e", EntityType.TARGET), new AddEnchantmentTask("SCH_136e2", EntityType.TARGET))
			}));
			cards.Add("SCH_136e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("SCH_136", "stats", 2))) }));
			cards.Add("SCH_136e2", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					EitherTurn = true,
					SingleTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) =>
					{
						if (t is ICharacter ch && ch.Damage > 0) ch.TakeHeal(s is Enchantment e ? e.Creator ?? (IPlayable)s : (IPlayable)s, ch.Damage);
					}), RemoveEnchantmentTask.Task)
				}
			}));
			// [SCH_140] Flesh Giant (10) — Costs (1) less for each time your hero's Health changed during your turns.
			cards.Add("SCH_140", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller[HsTag.HERO_HEALTH_CHANGES_OWN_TURNS] * V("SCH_140", "discount", 1))
			}));
			// [SCH_159] Mindrender Illucia (3) — Battlecry: Replace your hand with a copy of your opponent's until end of turn.
			cards.Add("SCH_159", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var theirs = c.Opponent.HandZone.ToList();
					SetAsideHand(g, c, s, "SCH_159e2");
					Enchantment marker = c.AppliedEnchantments?.LastOrDefault(e => e.Card.Id == "SCH_159e2");
					foreach (IPlayable p in theirs)
					{
						if (c.HandZone.IsFull) break;
						IPlayable copy = Generic.Copy(c, s, p, SabberStoneCore.Enums.Zone.HAND);
						if (copy != null && marker != null) copy[HsTag.HAND_UNTIL_TURN_END] = marker.Id;
					}
				})
			}));
			cards.Add("SCH_159e2", SetAsideHandReturns(tempCardsToDeck: false));
			// [SCH_513] Brittlebone Destroyer (4) — Battlecry: If your hero's Health changed this turn, destroy a minion.
			cards.Add("SCH_513", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } }, new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c[HsTag.HERO_HEALTH_CHANGED_TURN] == g.Turn, new DestroyTask(EntityType.TARGET))
			}));
			// [SCH_514] Raise Dead (0) — Deal 3 damage to your hero. Return two friendly minions that died this game to your hand.
			cards.Add("SCH_514", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("SCH_514", "damage", 3), EntityType.HERO, true), new CustomTask((g, c, s, t, stack) =>
				{
					var dead = c.DiedThisGameCards.Where(p => p is Minion).Select(p => p.Card).ToList();
					for (int i = 0; i < V("SCH_514", "minions", 2) && dead.Count > 0; i++)
					{
						Card card = dead[g.Random.Next(dead.Count)];
						dead.Remove(card);
						AddToHand(c, card);
					}
				}))
			}));

			// ---------------------------------------------------------------- Rogue

			// [SCH_305] Secret Passage (2) — Replace your hand with 4 cards from your deck. Swap back next turn. (wiki: the hand
			// is set aside; the 4 aren't drawn; at the end of your turn those still in hand go back and the hand returns)
			cards.Add("SCH_305", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					SetAsideHand(g, c, s, "SCH_305e3");
					Enchantment marker = c.AppliedEnchantments?.LastOrDefault(e => e.Card.Id == "SCH_305e3");
					for (int i = 0; i < V("SCH_305", "cards", 4) && c.DeckZone.Count > 0 && !c.HandZone.IsFull; i++)
					{
						IPlayable card = c.DeckZone.TopCard;
						c.DeckZone.Remove(card);
						Generic.AddHandPhase.Invoke(c, card);
						if (marker != null) card[HsTag.HAND_UNTIL_TURN_END] = marker.Id;
					}
				})
			}));
			cards.Add("SCH_305e3", SetAsideHandReturns(tempCardsToDeck: true));
			// [SCH_509] Brain Freeze (1) — Freeze a minion. Combo: Also deal 3 damage to it.
			cards.Add("SCH_509", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Freeze(EntityType.TARGET),
				ComboTask = ComplexTask.Create(ComplexTask.Freeze(EntityType.TARGET), new DamageTask(V("SCH_509", "damage", 3), EntityType.TARGET, true))
			}));
			// [SCH_519] Vulpera Toxinblade (3) — Your weapon has +2 Attack.
			cards.Add("SCH_519", new CardDef(new Power { Aura = new Aura(AuraType.WEAPON, "SCH_519e") }));
			cards.Add("SCH_519e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.ATK, EffectOperator.ADD, V("SCH_519", "attack", 2))) })); // a weapon: Effect(ATK), as Spiteful Smith
			// [SCH_521] Coerce (3) — Destroy a damaged minion. Combo: Destroy any minion.
			cards.Add("SCH_521", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_DAMAGED_TARGET_UNLESS_COMBO, 0 } },
				new Power { PowerTask = new DestroyTask(EntityType.TARGET), ComboTask = new DestroyTask(EntityType.TARGET) }));
			// [SCH_426] Infiltrator Lilian (4) — Stealth. Deathrattle: Summon a 4/2 Forsaken Lilian that attacks a random enemy.
			cards.Add("SCH_426", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.BoardZone.IsFull) return;
					var lilian = (Minion)Entity.FromCard(c, Cards.FromId("SCH_426t"));
					Generic.SummonBlock.Invoke(g, lilian, -1, s);
					var enemies = AttackableEnemies(c);
					if (enemies.Count > 0) EffectAttack(g, lilian, enemies[g.Random.Next(enemies.Count)]);
				})
			}));
			// [SCH_622] Self-Sharpening Sword (3) — After your hero attacks, gain +1 Attack.
			cards.Add("SCH_622", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = new AddEnchantmentTask("SCH_622e", EntityType.WEAPON) }
			}));
			cards.Add("SCH_622e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.ATK, EffectOperator.ADD, V("SCH_622", "attack", 1))) })); // a weapon
			// [SCH_623] Cutting Class (5) — Draw 2 cards. Costs (1) less per Attack of your weapon.
			cards.Add("SCH_623", new CardDef(new Power
			{
				PowerTask = new EnqueueTask(V("SCH_623", "cards", 2), new DrawTask()),
				Aura = new AdaptiveCostEffect(p => p.Controller.Hero.Weapon?.AttackDamage ?? 0)
			}));

			// ---------------------------------------------------------------- Shaman

			// [SCH_271] Molten Blast (3) — Deal 2 damage. Summon that many 1/1 Elementals. (that many: the damage with Spell Damage)
			cards.Add("SCH_271", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int damage = V("SCH_271", "damage", 2) + c.CurrentSpellPower;
					if (t is ICharacter ch) Generic.DamageCharFunc((IPlayable)s, ch, V("SCH_271", "damage", 2), false);
					for (int i = 0; i < damage && !c.BoardZone.IsFull; i++) Summon(g, c, Cards.FromId("SCH_271t"), s);
				})
			}));
			// [SCH_301] Rune Dagger (2) — After your hero attacks, gain Spell Damage +1 this turn.
			cards.Add("SCH_301", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = new AddEnchantmentTask("SCH_301e", EntityType.CONTROLLER) }
			}));
			cards.Add("SCH_301e", OneTurn(new Effect(GameTag.SPELLPOWER, EffectOperator.ADD, V("SCH_301", "spell damage", 1))));
			// [SCH_507] Instructor Fireheart (3) — Battlecry: Discover a spell that costs (1) or more. If you play it this turn,
			// repeat this effect. (the pick carries "Hot Streak!": playing it this turn Discovers again, with the same mark)
			ISimpleTask fireheart = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL && x.Cost >= V("SCH_507", "cost", 1), "SCH_507e");
			cards.Add("SCH_507", new CardDef(new Power { PowerTask = fireheart }));
			cards.Add("SCH_507e", new CardDef(new Power
			{
				Enchant = new Enchant { IsOneTurnEffect = true },
				Trigger = new Trigger(TriggerType.PLAY_CARD)
				{
					TriggerActivation = TriggerActivation.HAND_OR_PLAY,
					TriggerSource = TriggerSource.ENCHANTMENT_TARGET,
					SingleTask = fireheart
				}
			}));
			// [SCH_535] Tidal Wave (8) — Lifesteal. Deal 3 damage to all minions.
			cards.Add("SCH_535", new CardDef(new Power { PowerTask = new DamageTask(V("SCH_535", "damage", 3), EntityType.ALLMINIONS, true) }));
			// [SCH_615] Totem Goliath (5) — Deathrattle: Summon all four basic Totems. Overload: (1)
			cards.Add("SCH_615", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (string totem in new[] { "CS2_050", "CS2_051", "CS2_052", "NEW1_009" }) Summon(g, c, Cards.FromId(totem), s);
				})
			}));

			// ---------------------------------------------------------------- Warlock

			// [SCH_147] Boneweb Egg (2) — Deathrattle: Summon two 2/1 Spiders. If you discard this, trigger its Deathrattle.
			var spiders = new SummonTask("SCH_147t", V("SCH_147", "spiders", 2));
			cards.Add("SCH_147", new CardDef(new Power
			{
				DeathrattleTask = spiders,
				Trigger = new Trigger(TriggerType.DISCARD) { TriggerActivation = TriggerActivation.HAND, TriggerSource = TriggerSource.SELF, SingleTask = spiders }
			}));
			// [SCH_307] School Spirits (3) — Deal 2 damage to all minions. Shuffle 2 Soul Fragments into your deck.
			cards.Add("SCH_307", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("SCH_307", "damage", 2), EntityType.ALLMINIONS, true), ShuffleSoulFragments(V("SCH_307", "fragments", 2)))
			}));
			// [SCH_343] Void Drinker (5) — Taunt. Battlecry: Destroy a Soul Fragment in your deck to gain +3/+3.
			cards.Add("SCH_343", new CardDef(new Power { PowerTask = DestroySoulFragmentTo(new AddEnchantmentTask("SCH_343e", EntityType.SOURCE)) }));
			cards.Add("SCH_343e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("SCH_343", "stats", 3))) }));
			// [SCH_517] Shadowlight Scholar (3) — Battlecry: Destroy a Soul Fragment in your deck to deal 3 damage.
			cards.Add("SCH_517", new CardDef(BattlecryAny, new Power
			{
				PowerTask = DestroySoulFragmentTo(new DamageTask(V("SCH_517", "damage", 3), EntityType.TARGET))
			}));
			// [SCH_700] Spirit Jailer (1) — Battlecry: Shuffle 2 Soul Fragments into your deck.
			cards.Add("SCH_700", new CardDef(new Power { PowerTask = ShuffleSoulFragments(V("SCH_700", "fragments", 2)) }));
			// [SCH_701] Soul Shear (2) — Deal 3 damage to a minion. Shuffle 2 Soul Fragments into your deck.
			cards.Add("SCH_701", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("SCH_701", "damage", 3), EntityType.TARGET, true), ShuffleSoulFragments(V("SCH_701", "fragments", 2)))
			}));
			// [SCH_702] Felosophy (1) — Copy the lowest Cost Demon in your hand. Outcast: Give both +1/+1.
			cards.Add("SCH_702", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var demons = c.HandZone.Where(p => p is Minion m && m.IsRace(Race.DEMON)).ToList();
					if (demons.Count == 0 || c.HandZone.IsFull) return;
					int low = demons.Min(p => p.Cost);
					var pick = demons.Where(p => p.Cost == low).ToList();
					IPlayable original = pick[g.Random.Next(pick.Count)];
					IPlayable copy = Generic.Copy(c, s, original, SabberStoneCore.Enums.Zone.HAND);
					if (PlayedAsOutcast.Eval((IPlayable)s))
					{
						Give(g, s, original, "SCH_702e");
						if (copy != null) Give(g, s, copy, "SCH_702e");
					}
				})
			}));
			cards.Add("SCH_702e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("SCH_702", "stats", 1))) }));
			// [SCH_703] Soulciologist Malicia (7) — Battlecry: For each Soul Fragment in your deck, summon a 3/3 Soul with Rush.
			cards.Add("SCH_703", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = c.DeckZone.Count(p => p.Card.Id == SoulFragment);
					for (int i = 0; i < n && !c.BoardZone.IsFull; i++) Summon(g, c, Cards.FromId("SCH_703t"), s);
				})
			}));

			// ---------------------------------------------------------------- Warrior

			// [SCH_238] Reaper's Scythe (4) — Spellburst: Also damages adjacent minions this turn.
			cards.Add("SCH_238", new CardDef(new Power { Trigger = Spellburst(new AddEnchantmentTask("SCH_238e", EntityType.HERO)) }));
			cards.Add("SCH_238e", new CardDef(new Power
			{
				Enchant = new Enchant { IsOneTurnEffect = true },
				Trigger = Cleave(TriggerSource.HERO) // the shared Cleave (After Attack Event)
			}));
			// [SCH_317] Playmaker (3) — After you play a Rush minion, summon a copy with 1 Health remaining.
			cards.Add("SCH_317", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Minion m && m.IsRush),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(t is Minion played) || c.BoardZone.IsFull) return;
						var copies = new TaskStack();
						new SummonCopyTask(EntityType.TARGET, addToStack: true).Process(g, c, s, played, copies);
						foreach (Minion copy in (copies.Playables ?? new List<IPlayable>()).OfType<Minion>())
							copy.Damage = System.Math.Max(0, copy.Health - 1 + copy.Damage);
					})
				}
			}));
			// [SCH_337] Troublemaker (8) — At the end of your turn, summon two 3/3 Ruffians that attack random enemies.
			cards.Add("SCH_337", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var ruffians = new List<Minion>();
						for (int i = 0; i < V("SCH_337", "ruffians", 2) && !c.BoardZone.IsFull; i++)
						{
							var r = (Minion)Entity.FromCard(c, Cards.FromId("SCH_337t"));
							Generic.SummonBlock.Invoke(g, r, -1, s);
							ruffians.Add(r);
						}
						foreach (Minion r in ruffians) // all summoned, then each attacks (user ruling, Nagrand Slam)
						{
							var enemies = AttackableEnemies(c);
							if (enemies.Count > 0 && !r.IsDead && !r.ToBeDestroyed) EffectAttack(g, r, enemies[g.Random.Next(enemies.Count)]);
						}
					})
				}
			}));
			// [SCH_525] In Formation! (2) — Add 2 random Taunt minions to your hand.
			cards.Add("SCH_525", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("SCH_525", "minions", 2); i++)
						if (RandomCard(g, x => x.Type == CardType.MINION && x[GameTag.TAUNT] > 0) is Card card) AddToHand(c, card);
				})
			}));
			// [SCH_621] Rattlegore (9) — Deathrattle: Resummon this with -1/-1. (stops when it would be 0 Health)
			cards.Add("SCH_621", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me) || c.BoardZone.IsFull) return;
					int atk = System.Math.Max(0, me.AttackDamage - 1), hp = me.BaseHealth - 1;
					if (hp <= 0) return;
					var back = (Minion)Entity.FromCard(c, me.Card);
					back.AttackDamage = atk;
					back.BaseHealth = hp;
					Generic.SummonBlock.Invoke(g, back, -1, s);
				})
			}));

			// ---------------------------------------------------------------- Warrior / Rogue / Paladin dual cards

			// [SCH_522] Steeldancer (4) — Battlecry: Summon a random minion with Cost equal to your weapon's Attack.
			cards.Add("SCH_522", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Summon(g, c, RandomMinionOfCost(g, c.Hero.Weapon?.AttackDamage ?? 0), s))
			}));
			// [SCH_276] Magehunter (3) — Rush. Whenever this attacks a minion, Silence it.
			cards.Add("SCH_276", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					Condition = SelfCondition.IsEventTargetIs(CardType.MINION),
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (g.CurrentEventData?.EventTarget is Minion d) new SilenceTask(EntityType.TARGET).Process(g, c, s, d); })
				}
			}));
		}
	}
}
