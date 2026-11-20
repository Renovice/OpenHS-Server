// HsServer addition (not upstream SabberStoneCore): Descent of Dragons (2019) — upstream left most of the set (Galakrond
// included) as TODO stubs. These definitions replace the stubs. Every number goes through CardValues (V).
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
		// ------------------------------------------------------------------ Galakrond and Invoke

		private static readonly string[] GalakrondIds = { "DRG_600", "DRG_610", "DRG_620", "DRG_650", "DRG_660" };

		/// <summary>
		/// The player's Galakrond: their hero if they already became Galakrond, else their main Galakrond card (the first one
		/// found in hand, then deck; remembered in MAIN_GALAKROND and followed through its upgrades).
		/// </summary>
		private static IPlayable FindGalakrond(Game g, Controller c)
		{
			if (c.Hero.Card[GameTag.GALAKROND_HERO_CARD] > 0) return c.Hero;
			int id = c[GameTag.MAIN_GALAKROND];
			if (id > 0 && g.IdEntityDic.TryGetValue(id, out IPlayable main) && main.Controller == c &&
			    (main.Zone?.Type == SabberStoneCore.Enums.Zone.HAND || main.Zone?.Type == SabberStoneCore.Enums.Zone.DECK))
				return main;
			IPlayable found = c.HandZone.Concat(c.DeckZone).FirstOrDefault(p => p.Card[GameTag.GALAKROND_HERO_CARD] > 0);
			if (found != null) c[GameTag.MAIN_GALAKROND] = found.Id;
			return found;
		}

		/// <summary>
		/// "Invoke Galakrond": with a Galakrond, trigger its Hero Power's effect, count the Invoke (INVOKE_COUNTER), and
		/// upgrade a Galakrond card after every 2 Invokes (base, Apocalypse, Azeroth's End).
		/// </summary>
		private static void Invoke(Game g, Controller c, int times = 1)
		{
			for (int i = 0; i < times; i++)
			{
				IPlayable gala = FindGalakrond(g, c);
				if (gala == null) return;
				c[GameTag.INVOKE_COUNTER] = c[GameTag.INVOKE_COUNTER] + 1;
				Card power = Cards.FromAssetId(gala.Card[GameTag.HERO_POWER]);
				power?.Power?.PowerTask?.Process(g, c, gala, null);
				if (gala == c.Hero) continue; // already Galakrond: no upgrade (a Galakrond card in hand is a Hero entity too)
				int progress = gala[GameTag.TAG_SCRIPT_DATA_NUM_1] + 1;
				string baseId = GalakrondIds.FirstOrDefault(b => gala.Card.Id.StartsWith(b));
				string next = gala.Card.Id == baseId ? baseId + "t2" : gala.Card.Id == baseId + "t2" ? baseId + "t3" : null;
				if (progress >= V("Galakrond", "invokes per upgrade", 2) && next != null)
				{
					IPlayable upgraded = Generic.ChangeEntityBlock(c, gala, Cards.FromId(next), false);
					upgraded[GameTag.TAG_SCRIPT_DATA_NUM_1] = 0;
					c[GameTag.MAIN_GALAKROND] = upgraded.Id;
				}
				else
					gala[GameTag.TAG_SCRIPT_DATA_NUM_1] = progress;
			}
		}

		private static bool InvokedTwice(Controller c) => c[GameTag.INVOKE_COUNTER] >= 2;

		private static ISimpleTask InvokeTask(int times = 1) => new CustomTask((g, c, s, t, stack) => Invoke(g, c, times));

		private static bool HoldingDragon(Controller c, IEntity except = null) =>
			c.HandZone.Any(p => p.Id != except?.Id && p is Minion m && m.IsRace(Race.DRAGON));

		private static ISimpleTask IfHoldingDragon(ISimpleTask then) => ComplexTask.If((g, c, s) => HoldingDragon(c, s), then);

		private static bool Overloaded(Controller c) => c.OverloadLocked > 0 || c.OverloadOwed > 0;

		// Dragon's Pack: two Spirit Wolves; +3/+3 when Invoked twice (the branch ComplexTask.If picks).
		private static ISimpleTask DragonsPack(bool powered) => new CustomTask((g, c, s, t, stack) =>
		{
			for (int i = 0; i < V("DRG_217", "wolves", 2) && !c.BoardZone.IsFull; i++)
			{
				var wolf = (Minion)Entity.FromCard(c, Cards.FromId("DRG_217t"));
				Generic.SummonBlock.Invoke(g, wolf, -1, s);
				if (powered) Give(g, s, wolf, "DRG_217e");
			}
		});

		// Dragon's Fury-style "deal 4 to a minion; holding a Dragon: also its neighbors".
		private static ISimpleTask DragonsFury(bool powered) => new CustomTask((g, c, s, t, stack) =>
		{
			if (!(t is Minion m)) return;
			var hit = new List<Minion> { m };
			if (powered) hit.AddRange(m.GetAdjacentMinions());
			foreach (Minion x in hit) Generic.DamageCharFunc((IPlayable)s, x, V("DRG_219", "damage", 4), true);
		});

		/// <summary>One step (or <paramref name="amount"/>) of a Sidequest; when complete, its reward runs and it leaves play.</summary>
		private static void SidequestStep(Game g, Controller c, IEntity source, int amount, Action<Game, Controller, IPlayable> reward)
		{
			if (!(source is Spell side) || side.Zone?.Type != SabberStoneCore.Enums.Zone.SECRET || amount <= 0) return;
			side.QuestProgress += amount;
			if (side.QuestProgress < side.QuestTotalProgress) return;
			side[GameTag.REVEALED] = 1;
			side.ActivatedTrigger?.Remove();
			c.SecretZone.Remove(side);
			c.GraveyardZone.Add(side);
			reward(g, c, side);
		}

		private static CardDef Sidequest(TriggerType type, TriggerSource source, Func<Game, Controller, IPlayable, int> steps,
			Action<Game, Controller, IPlayable> reward) => new CardDef(new Power
		{
			Trigger = new Trigger(type)
			{
				TriggerSource = source,
				SingleTask = new CustomTask((g, c, s, t, stack) => SidequestStep(g, c, s, steps(g, c, t), reward))
			}
		});

		private static void DrawWhere(Game g, Controller c, Func<IPlayable, bool> where, int n = 1)
		{
			for (int i = 0; i < n; i++)
			{
				var matching = c.DeckZone.Where(where).ToList();
				if (matching.Count == 0) return;
				Generic.Draw(c, matching[g.Random.Next(matching.Count)]);
			}
		}

		private static bool IsTreant(IPlayable p) => p is Minion && SelfCondition.IsTreantCard(p.Card);

		private static void Dragons(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Galakrond heroes and powers

			// [DRG_600] Galakrond, the Wretched (Warlock) — Battlecry: Summon 1/2/4 random Demons (t3: equip a 5/2 Claw).
			void Galakrond(string id, Action<Game, Controller, IEntity, int> battlecry)
			{
				int[] amounts = { V(id, "stage 1", 1), V(id, "stage 2", 2), V(id, "stage 3", 4) };
				string[] stages = { id, id + "t2", id + "t3" };
				for (int i = 0; i < 3; i++)
				{
					int stage = i, amount = amounts[i];
					cards.Add(stages[i], new CardDef(new Power
					{
						PowerTask = new CustomTask((g, c, s, t, stack) =>
						{
							battlecry(g, c, s, amount);
							if (stage == 2) new WeaponTask("DRG_238ht").Process(g, c, s, t);
						})
					}));
				}
			}
			Galakrond("DRG_600", (g, c, s, n) =>
			{
				for (int i = 0; i < n; i++)
					Summon(g, c, RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.DEMON) && !x.NeverRandomlyOnBoard), s);
			});
			// [DRG_610] Galakrond, the Nightmare (Rogue) — Battlecry: Draw 1/2/4 cards. They cost (0).
			Galakrond("DRG_610", (g, c, s, n) =>
			{
				for (int i = 0; i < n; i++)
					if (Generic.Draw(c) is IPlayable drawn && drawn.Zone?.Type == SabberStoneCore.Enums.Zone.HAND) Give(g, s, drawn, "DRG_610e");
			});
			cards.Add("DRG_610e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) }));
			// [DRG_620] Galakrond, the Tempest (Shaman) — Battlecry: Summon two 2/2 / 4/4 / 8/8 Storms with Rush.
			Galakrond("DRG_620", (g, c, s, n) =>
			{
				string storm = n == 1 ? "DRG_620t4" : n == 2 ? "DRG_620t5" : "DRG_620t6";
				for (int i = 0; i < 2; i++) Summon(g, c, Cards.FromId(storm), s);
			});
			// [DRG_650] Galakrond, the Unbreakable (Warrior) — Battlecry: Draw 1/2/4 minions. Give them +4/+4.
			Galakrond("DRG_650", (g, c, s, n) =>
			{
				for (int i = 0; i < n; i++)
				{
					var minions = c.DeckZone.Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.Count == 0) break;
					if (Generic.Draw(c, minions[g.Random.Next(minions.Count)]) is IPlayable drawn && drawn.Zone?.Type == SabberStoneCore.Enums.Zone.HAND)
						Give(g, s, drawn, "DRG_650e");
				}
			});
			cards.Add("DRG_650e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DRG_650", "stats", 4))) }));
			// [DRG_660] Galakrond, the Unspeakable (Priest) — Battlecry: Destroy 1/2/4 random enemy minions.
			Galakrond("DRG_660", (g, c, s, n) =>
			{
				var enemies = c.Opponent.BoardZone.GetAll().ToList();
				for (int i = 0; i < n && enemies.Count > 0; i++)
				{
					Minion victim = enemies[g.Random.Next(enemies.Count)];
					enemies.Remove(victim);
					victim.Destroy();
				}
			});

			// Galakrond Hero Powers (also each Galakrond's Invoke effect).
			cards.Add("DRG_238p", new CardDef(new Power { PowerTask = new AddEnchantmentTask("DRG_238t10e", EntityType.HERO) }));      // Might: +3 Attack this turn
			cards.Add("DRG_238t10e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("DRG_238p", "attack", 3))) { IsOneTurnEffect = true } }));
			cards.Add("DRG_238p2", new CardDef(new Power { PowerTask = new AddLackeyTask(1) }));                                       // Guile: a Lackey
			cards.Add("DRG_238p3", new CardDef(new Power { PowerTask = new SummonTask("DRG_238t12t2", V("DRG_238p3", "imps", 2), SummonSide.DEFAULT) })); // Malice
			cards.Add("DRG_238p4", new CardDef(new Power { PowerTask = new SummonTask("DRG_238t14t3", SummonSide.DEFAULT) }));        // Fury
			cards.Add("DRG_238p5", new CardDef(new Power                                                                                  // Wit
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (RandomCard(g, x => x.Type == CardType.MINION && x.HasClass(CardClass.PRIEST)) is Card card) AddToHand(c, card);
				})
			}));

			// [DRG_099] Kronx Dragonhoof (6) — Battlecry: Draw Galakrond. If you're already Galakrond, unleash a Devastation.
			cards.Add("DRG_099", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.Hero.Card[GameTag.GALAKROND_HERO_CARD] > 0,
					new CustomTask((g, c, s, t, stack) =>
					{
						Card[] devastations = { Cards.FromId("DRG_099t1"), Cards.FromId("DRG_099t2"), Cards.FromId("DRG_099t3"), Cards.FromId("DRG_099t4") };
						Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.CAST, devastations, null);
					}),
					new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p.Card[GameTag.GALAKROND_HERO_CARD] > 0)))
			}));
			cards.Add("DRG_099t1", new CardDef(new Power { PowerTask = ComplexTask.Create(new DamageTask(V("DRG_099t1", "damage", 5), EntityType.OP_HERO), new HealTask(V("DRG_099t1", "heal", 5), EntityType.HERO)) }));
			cards.Add("DRG_099t2", new CardDef(new Power { PowerTask = new SummonTask("DRG_099t2t", SummonSide.SPELL) }));
			cards.Add("DRG_099t3", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.GetAll().Where(m => m.Card.Id != "DRG_099"), "DRG_099t3e"))
			}));
			cards.Add("DRG_099t3e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DRG_099t3", "stats", 2))) }));
			cards.Add("DRG_099t4", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => m.Card.Id != "DRG_099").ToList())
						Generic.DamageCharFunc((IPlayable)s, m, V("DRG_099t4", "damage", 5), false);
				})
			}));

			// Invoke cards.
			cards.Add("DRG_021", new CardDef(new Power { PowerTask = InvokeTask() }));                       // Ritual Chopper
			cards.Add("DRG_050", new CardDef(new Power { PowerTask = InvokeTask() }));                       // Devoted Maniac
			cards.Add("DRG_242", new CardDef(new Power { PowerTask = InvokeTask() }));                       // Shield of Galakrond
			cards.Add("DRG_303", new CardDef(new Power { PowerTask = InvokeTask() }));                       // Disciple of Galakrond
			cards.Add("DRG_218", new CardDef(new Power { PowerTask = InvokeTask(2) }));                      // Corrupt Elementalist
			cards.Add("DRG_030", new CardDef(TargetMinion, new Power                                         // Praise Galakrond!
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("DRG_030e", EntityType.TARGET), InvokeTask())
			}));
			cards.Add("DRG_030e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("DRG_030", "attack", 1))) }));
			cards.Add("DRG_246", new CardDef(TargetMinion, new Power { PowerTask = ComplexTask.Create(new DestroyTask(EntityType.TARGET), InvokeTask()) })); // Time Rip
			cards.Add("DRG_247", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_UNDAMAGED_TARGET, 0 } },
				new Power { PowerTask = ComplexTask.Create(new DamageTask(V("DRG_247", "damage", 3), EntityType.TARGET, true), InvokeTask()) })); // Seal Fate
			cards.Add("DRG_248", new CardDef(TargetEnemy, new Power { PowerTask = ComplexTask.Create(ComplexTask.Freeze(EntityType.TARGET), InvokeTask()) })); // Invocation of Frost
			cards.Add("DRG_249", new CardDef(new Power { PowerTask = ComplexTask.Create(InvokeTask(), new DamageTask(V("DRG_249", "damage", 1), EntityType.ALLMINIONS, true)) })); // Awaken!
			cards.Add("DRG_250", new CardDef(new Power { PowerTask = ComplexTask.Create(InvokeTask(), new AddEnchantmentTask("DRG_250e", EntityType.MINIONS)) })); // Fiendish Rites
			cards.Add("DRG_250e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("DRG_250", "attack", 1))) }));
			// [DRG_202] Dragonblight Cultist (3) — Battlecry: Invoke Galakrond. Gain +1 Attack for each other friendly minion.
			cards.Add("DRG_202", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(InvokeTask(), new CustomTask((g, c, s, t, stack) =>
				{
					int n = c.BoardZone.GetAll().Count(m => m.Id != s.Id);
					if (n > 0) Give(g, s, (IPlayable)s, "DRG_202e", n * V("DRG_202", "attack per minion", 1));
				}))
			}));
			cards.Add("DRG_202e", new CardDef(new Power { Enchant = new Enchant(GameTag.ATK, EffectOperator.ADD) }));

			// "If you've Invoked twice" cards.
			cards.Add("DRG_019", new CardDef(new Power                                                       // Scion of Ruin
			{
				PowerTask = ComplexTask.If((g, c, s) => InvokedTwice(c), new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("DRG_019", "copies", 2); i++) Summon(g, c, s.Card, s);
				}))
			}));
			cards.Add("DRG_027", new CardDef(new Power                                                       // Umbral Skulker
			{
				PowerTask = ComplexTask.If((g, c, s) => InvokedTwice(c), new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("DRG_027", "coins", 3); i++) AddToHand(c, Cards.FromId("GAME_005"));
				}))
			}));
			cards.Add("DRG_203", new CardDef(new Power                                                       // Veiled Worshipper
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => stack.Flag = InvokedTwice(c)),
					new FlagTask(true, new EnqueueTask(V("DRG_203", "cards", 3), new DrawTask())))
			}));
			cards.Add("DRG_217", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power // Dragon's Pack
			{
				PowerTask = ComplexTask.If((g, c, s) => InvokedTwice(c), DragonsPack(true), DragonsPack(false))
			}));
			cards.Add("DRG_217e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DRG_217", "stats", 3))) }));
			cards.Add("DRG_300", new CardDef(new Power                                                       // Fate Weaver
			{
				PowerTask = ComplexTask.If((g, c, s) => InvokedTwice(c), new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone, "DRG_300e")))
			}));
			cards.Add("DRG_300e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("DRG_300", "discount", 1))) }));

			// ---------------------------------------------------------------- Sidequests

			// [DRG_008] Righteous Cause — Sidequest: Summon 5 minions. Reward: Give your minions +1/+1.
			cards.Add("DRG_008", Sidequest(TriggerType.AFTER_SUMMON, TriggerSource.MINIONS, (g, c, t) => 1,
				(g, c, s) => GiveAll(g, s, c.BoardZone.GetAll(), "DRG_008e")));
			cards.Add("DRG_008e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DRG_008", "stats", 1))) }));
			// [DRG_051] Strength in Numbers — Sidequest: Spend 10 Mana on minions. Reward: Summon a minion from your deck.
			cards.Add("DRG_051", Sidequest(TriggerType.PLAY_MINION, TriggerSource.FRIENDLY, (g, c, t) => t?[GameTag.TAG_LAST_KNOWN_COST_IN_HAND] ?? 0,
				(g, c, s) =>
				{
					var minions = c.DeckZone.Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.Count == 0 || c.BoardZone.IsFull) return;
					var m = (Minion)minions[g.Random.Next(minions.Count)];
					c.DeckZone.Remove(m);
					Generic.SummonBlock.Invoke(g, m, -1, s);
				}));
			// [DRG_251] Clear the Way — Sidequest: Summon 3 Rush minions. Reward: Summon a 4/4 Gryphon with Rush.
			cards.Add("DRG_251", Sidequest(TriggerType.AFTER_SUMMON, TriggerSource.MINIONS, (g, c, t) => t is Minion m && m.IsRush ? 1 : 0,
				(g, c, s) => Summon(g, c, Cards.FromId("DRG_251t"), s)));
			// [DRG_255] Toxic Reinforcements — Sidequest: Use your Hero Power three times. Reward: Summon three 1/1 Leper Gnomes.
			cards.Add("DRG_255", Sidequest(TriggerType.INSPIRE, TriggerSource.FRIENDLY, (g, c, t) => 1,
				(g, c, s) => { for (int i = 0; i < V("DRG_255", "gnomes", 3); i++) Summon(g, c, Cards.FromId("DRG_255t2"), s); }));
			// [DRG_258] Sanctuary — Sidequest: Take no damage for a turn. Reward: Summon a 3/6 minion with Taunt.
			cards.Add("DRG_258", Sidequest(TriggerType.TURN_START, TriggerSource.ALL, (g, c, t) => g.CurrentPlayer == c && c.Hero.DamageTakenThisTurn == 0 ? 1 : 0,
				(g, c, s) => Summon(g, c, Cards.FromId("DRG_258t"), s)));
			// [DRG_317] Secure the Deck — Sidequest: Attack twice with your hero. Reward: Add 3 'Claw' spells to your hand.
			cards.Add("DRG_317", Sidequest(TriggerType.ATTACK, TriggerSource.HERO, (g, c, t) => 1,
				(g, c, s) => { for (int i = 0; i < V("DRG_317", "claws", 3); i++) AddToHand(c, Cards.FromId("CS2_005")); }));
			// [DRG_323] Learn Draconic — Sidequest: Spend 8 Mana on spells. Reward: Summon a 6/6 Dragon.
			cards.Add("DRG_323", Sidequest(TriggerType.CAST_SPELL, TriggerSource.FRIENDLY, (g, c, t) => t?[GameTag.TAG_LAST_KNOWN_COST_IN_HAND] ?? 0,
				(g, c, s) => Summon(g, c, Cards.FromId("DRG_323t"), s)));
			// [DRG_324] Elemental Allies — Sidequest: Play an Elemental 2 turns in a row. Reward: Draw 3 spells from your deck.
			cards.Add("DRG_324", Sidequest(TriggerType.PLAY_MINION, TriggerSource.FRIENDLY,
				(g, c, t) => t is Minion m && m.IsRace(Race.ELEMENTAL) && c.NumElementalsPlayedLastTurn > 0 ? 2 : 0,
				(g, c, s) => DrawWhere(g, c, p => p is Spell, V("DRG_324", "spells", 3))));

			// ---------------------------------------------------------------- Neutral

			// Evasive minions: their "can't be targeted" is in their tags (see IsKeywordOnly).
			// [DRG_049] Tasty Flyfish (2) — Deathrattle: Give a Dragon in your hand +2/+2.
			cards.Add("DRG_049", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var dragons = c.HandZone.Where(p => p is Minion m && m.IsRace(Race.DRAGON)).ToList();
					if (dragons.Count > 0) Give(g, s, dragons[g.Random.Next(dragons.Count)], "DRG_049e");
				})
			}));
			cards.Add("DRG_049e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DRG_049", "stats", 2))) }));

			cards.Add("DRG_054", new CardDef(new Power { PowerTask = new DrawTask() }));                     // Big Ol' Whelp

			// [DRG_055] Hoard Pillager (4) — Battlecry: Equip one of your destroyed weapons.
			cards.Add("DRG_055", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var weapons = c.DiedThisGameCards.Where(p => p is Weapon).Select(p => p.Card).ToList();
					if (weapons.Count == 0) return;
					var weapon = (Weapon)Entity.FromCard(c, weapons[g.Random.Next(weapons.Count)]);
					weapon.Card.Power?.Aura?.Activate(weapon);
					weapon.Card.Power?.Trigger?.Activate(weapon);
					c.Hero.AddWeapon(weapon);
				})
			}));

			// [DRG_056] Parachute Brigand (2) — After you play a Pirate, summon this minion from your hand.
			cards.Add("DRG_056", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerActivation = TriggerActivation.HAND,
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = SelfCondition.IsRace(Race.PIRATE),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me) Generic.SummonFromHand(c, me, s);
					})
				}
			}));

			// [DRG_057] Hot Air Balloon (1) — At the start of your turn, gain +1 Health.
			cards.Add("DRG_057", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = new AddEnchantmentTask("DRG_057e", EntityType.SOURCE) }
			}));
			cards.Add("DRG_057e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("DRG_057", "health", 1))) }));

			// [DRG_058] Wing Commander (4) — Has +2 Attack for each Dragon in your hand.
			cards.Add("DRG_058", new CardDef(new Power
			{
				Aura = new AdaptiveEffect(GameTag.ATK, EffectOperator.ADD,
					p => V("DRG_058", "attack per dragon", 2) * p.Controller.HandZone.Count(x => x is Minion m && m.IsRace(Race.DRAGON)))
			}));

			// [DRG_059] Goboglide Tech (3) — Battlecry: If you control a Mech, gain +1/+1 and Rush.
			cards.Add("DRG_059", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.BoardZone.GetAll().Any(m => m.Id != s.Id && m.IsRace(Race.MECHANICAL)), new CustomTask((g, c, s, t, stack) =>
				{
					Give(g, s, (IPlayable)s, "DRG_059e");
					((Minion)s).IsRush = true;
				}))
			}));
			cards.Add("DRG_059e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DRG_059", "stats", 1))) }));

			// [DRG_060] Fire Hawk (3) — Battlecry: Gain +1 Attack for each card in your opponent's hand.
			cards.Add("DRG_060", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = c.Opponent.HandZone.Count;
					if (n > 0) Give(g, s, (IPlayable)s, "DRG_060e", n * V("DRG_060", "attack per card", 1));
				})
			}));
			cards.Add("DRG_060e", new CardDef(new Power { Enchant = new Enchant(GameTag.ATK, EffectOperator.ADD) }));

			// [DRG_062] Wyrmrest Purifier (2) — Battlecry: Transform all Neutral cards in your deck into random cards from your class.
			cards.Add("DRG_062", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var classCards = Cards.GenerationPool(g, c).Where(x => x.HasClass(c.HeroClass)).ToList();
					if (classCards.Count == 0) return;
					foreach (IPlayable p in c.DeckZone.Where(p => p.Card.HasClass(CardClass.NEUTRAL)).ToList())
						Generic.ChangeEntityBlock(c, p, classCards[g.Random.Next(classCards.Count)], true);
				})
			}));

			// [DRG_063] Dragonmaw Poacher (4) — Battlecry: If your opponent controls a Dragon, gain +4/+4 and Rush.
			cards.Add("DRG_063", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.Opponent.BoardZone.GetAll().Any(m => m.IsRace(Race.DRAGON)), new CustomTask((g, c, s, t, stack) =>
				{
					Give(g, s, (IPlayable)s, "DRG_063e");
					((Minion)s).IsRush = true;
				}))
			}));
			cards.Add("DRG_063e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DRG_063", "stats", 4))) }));

			// [DRG_064] Zul'Drak Ritualist (4) — Taunt. Battlecry: Summon three random 1-Cost minions for your opponent.
			cards.Add("DRG_064", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("DRG_064", "minions", 3); i++) Summon(g, c.Opponent, RandomMinionOfCost(g, 1), s);
				})
			}));

			// [DRG_067] Troll Batrider (4) — Battlecry: Deal 3 damage to a random enemy minion.
			cards.Add("DRG_067", new CardDef(new Power { PowerTask = ComplexTask.DamageRandomTargets(1, EntityType.OP_MINIONS, V("DRG_067", "damage", 3)) }));

			// [DRG_068] Living Dragonbreath (3) — Your minions can't be Frozen. (Character.CantBeFrozen checks it.)
			cards.Add("DRG_068", new CardDef(new Power
			{
				Aura = new Aura(AuraType.BOARD, new Effect(GameTag.CANT_BE_FROZEN, EffectOperator.SET, 1)), // "Your minions can't be Frozen"
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().Where(m => m.IsFrozen)) m.IsFrozen = false;
				})
			}));

			// [DRG_069] Platebreaker (5) — Battlecry: Destroy your opponent's Armor.
			cards.Add("DRG_069", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => c.Opponent.Hero.Armor = 0) }));

			// [DRG_070] Dragon Breeder (2) — Battlecry: Choose a friendly Dragon. Add a copy of it to your hand.
			cards.Add("DRG_070", new CardDef(new Dictionary<PlayReq, int>
				{
					{ PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_RACE, (int)Race.DRAGON }
				},
				new Power { PowerTask = new CustomTask((g, c, s, t, stack) => { if (t != null) AddToHand(c, t.Card); }) }));

			// [DRG_071] Bad Luck Albatross (3) — Deathrattle: Shuffle two 1/1 Albatross into your opponent's deck.
			cards.Add("DRG_071", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("DRG_071", "albatross", 2); i++)
						Generic.ShuffleIntoDeck.Invoke(c.Opponent, s, Entity.FromCard(c.Opponent, Cards.FromId("DRG_071t")));
				})
			}));

			// [DRG_072] Skyfin (5) — Battlecry: If you're holding a Dragon, summon 2 random Murlocs.
			cards.Add("DRG_072", new CardDef(new Power
			{
				PowerTask = IfHoldingDragon(new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("DRG_072", "murlocs", 2); i++)
						Summon(g, c, RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.MURLOC) && !x.NeverRandomlyOnBoard), s);
				}))
			}));

			// [DRG_074] Camouflaged Dirigible (6) — Battlecry: Give your other Mechs Stealth until your next turn.
			cards.Add("DRG_074", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.BoardZone.GetAll().Where(m => m.Id != s.Id && m.IsRace(Race.MECHANICAL)), "DRG_074e"))
			}));
			cards.Add("DRG_074e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.STEALTH, EffectOperator.SET, 1)),
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = RemoveEnchantmentTask.Task }
			}));

			// [DRG_075] Cobalt Spellkin (5) — Battlecry: Add two 1-Cost spells from your class to your hand.
			cards.Add("DRG_075", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("DRG_075", "spells", 2); i++)
						if (RandomCard(g, x => x.Type == CardType.SPELL && x.HasClass(c.HeroClass) && x.Cost == 1) is Card card) AddToHand(c, card);
				})
			}));

			// [DRG_076] Faceless Corruptor (5) — Rush. Battlecry: Transform one of your minions into a copy of this.
			cards.Add("DRG_076", new CardDef(BattlecryFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m && m.Id != s.Id) Generic.TransformBlock.Invoke(c, s.Card, m); })
			}));

			// [DRG_077] Utgarde Grapplesniper (6) — Battlecry: Both players draw a card. If it's a Dragon, summon it.
			cards.Add("DRG_077", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Controller p in new[] { c, c.Opponent })
						if (Generic.Draw(p) is Minion m && m.IsRace(Race.DRAGON))
							Generic.SummonFromHand(p, m, s);
				})
			}));

			// [DRG_078] Depth Charge (1) — At the start of your turn, deal 5 damage to ALL minions.
			cards.Add("DRG_078", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = new DamageTask(V("DRG_078", "damage", 5), EntityType.ALLMINIONS) }
			}));

			// [DRG_081] Scalerider (3) — Battlecry: If you're holding a Dragon, deal 2 damage.
			cards.Add("DRG_081", new CardDef(BattlecryAny, new Power { PowerTask = IfHoldingDragon(new DamageTask(V("DRG_081", "damage", 2), EntityType.TARGET)) }));

			// [DRG_082] Kobold Stickyfinger (5) — Battlecry: Steal your opponent's weapon.
			cards.Add("DRG_082", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(c.Opponent.Hero.Weapon is Weapon theirs)) return;
					int used = theirs.Damage;
					theirs.Destroy();
					var mine = (Weapon)Entity.FromCard(c, theirs.Card);
					mine.Card.Power?.Aura?.Activate(mine);
					mine.Card.Power?.Trigger?.Activate(mine);
					c.Hero.AddWeapon(mine);
					mine.Damage = used;
				})
			}));

			// [DRG_084] Tentacled Menace (5) — Battlecry: Each player draws a card. Swap their Costs.
			cards.Add("DRG_084", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					IPlayable mine = Generic.Draw(c), theirs = Generic.Draw(c.Opponent);
					if (mine == null || theirs == null) return;
					int a = mine.Cost, b = theirs.Cost;
					mine.Cost = b;
					theirs.Cost = a;
				})
			}));

			// [DRG_086] Chromatic Egg (5) — Battlecry: Secretly Discover a Dragon to hatch into. Deathrattle: Hatch!
			cards.Add("DRG_086", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var offer = Pick(g, Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION && x.IsRace(Race.DRAGON)), 3);
					if (offer.Count == 0) return;
					IEntity egg = s;
					Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.STACK, offer.ToArray(),
						new CustomTask((g2, c2, s2, chosen, stack2) => { if (chosen != null) egg[GameTag.TAG_SCRIPT_DATA_NUM_1] = chosen.Card.AssetId; }));
				}),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					int assetId = s[GameTag.TAG_SCRIPT_DATA_NUM_1];
					if (assetId > 0) Summon(g, c, Cards.FromAssetId(assetId), s);
				})
			}));

			// [DRG_088] Dread Raven (3) — Has +3 Attack for each other Dread Raven you control.
			cards.Add("DRG_088", new CardDef(new Power
			{
				Aura = new AdaptiveEffect(GameTag.ATK, EffectOperator.ADD,
					p => V("DRG_088", "attack per raven", 3) * p.Controller.BoardZone.GetAll().Count(m => m.Id != p.Id && m.Card.Id == "DRG_088"))
			}));

			// [DRG_089] Dragonqueen Alexstrasza (9) — Battlecry: If your deck has no duplicates, add 2 random Dragons to your hand.
			// They cost (0).
			cards.Add("DRG_089", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => NoDuplicatesInDeck(c), new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("DRG_089", "dragons", 2) && !c.HandZone.IsFull; i++)
					{
						Card dragon = RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.DRAGON) && x.Id != "DRG_089");
						if (dragon == null) break;
						IPlayable added = Entity.FromCard(c, dragon);
						Generic.AddHandPhase.Invoke(c, added);
						Give(g, s, added, "DRG_089e");
					}
				}))
			}));
			cards.Add("DRG_089e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) }));

			// [DRG_091] Shu'ma (7) — At the end of your turn, fill your board with 1/1 Tentacles.
			cards.Add("DRG_091", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < Game.MAX_MINIONS_ON_BOARD && !c.BoardZone.IsFull; i++) Summon(g, c, Cards.FromId("DRG_091t"), s); })
				}
			}));

			// [DRG_092] Transmogrifier (2) — Whenever you draw a card, transform it into a random Legendary minion.
			cards.Add("DRG_092", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DRAW)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t == null || t.Zone?.Type != SabberStoneCore.Enums.Zone.HAND) return;
						if (RandomCard(g, x => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY) is Card legend)
							Generic.ChangeEntityBlock(c, t, legend, true);
					})
				}
			}));

			// [DRG_213] Twin Tyrant (8) — Battlecry: Deal 4 damage to two random enemy minions.
			cards.Add("DRG_213", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = c.Opponent.BoardZone.GetAll().ToList();
					for (int i = 0; i < V("DRG_213", "targets", 2) && enemies.Count > 0; i++)
					{
						Minion m = enemies[g.Random.Next(enemies.Count)];
						enemies.Remove(m);
						Generic.DamageCharFunc((IPlayable)s, m, V("DRG_213", "damage", 4), false);
					}
				})
			}));

			// [DRG_257] Frizz Kindleroost (4) — Battlecry: Reduce the Cost of Dragons in your deck by (2).
			cards.Add("DRG_257", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.DeckZone.Where(p => p is Minion m && m.IsRace(Race.DRAGON)), "DRG_257e3"))
			}));
			cards.Add("DRG_257e3", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("DRG_257", "discount", 2))) }));

			// [DRG_401] Grizzled Wizard (2) — Battlecry: Swap Hero Powers with your opponent until your next turn.
			cards.Add("DRG_401", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card mine = c.Hero.HeroPower.Card, theirs = c.Opponent.Hero.HeroPower.Card;
					new ReplaceHeroPower(theirs).Process(g, c, s, null);
					new ReplaceHeroPower(mine).Process(g, c.Opponent, s, null);
					Generic.AddEnchantmentBlock(g, Cards.FromId("DRG_401e"), (IPlayable)s, c, mine.AssetId, theirs.AssetId, 0);
				})
			}));
			cards.Add("DRG_401e", new CardDef(new Power
			{
				Enchant = new Enchant(GameTag.TAG_SCRIPT_DATA_NUM_1, EffectOperator.SET),
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = ComplexTask.Create(
						new CustomTask((g, c, s, t, stack) =>
						{
							if (!(s is Enchantment e)) return;
							int mine = e[GameTag.TAG_SCRIPT_DATA_NUM_1], theirs = e[GameTag.TAG_SCRIPT_DATA_NUM_2];
							if (mine > 0) new ReplaceHeroPower(Cards.FromAssetId(mine)).Process(g, c, s, null);
							if (theirs > 0) new ReplaceHeroPower(Cards.FromAssetId(theirs)).Process(g, c.Opponent, s, null);
						}),
						RemoveEnchantmentTask.Task)
				}
			}));

			// [DRG_402] Sathrovarr (9) — Battlecry: Choose a friendly minion. Add a copy of it to your hand, deck, and battlefield.
			cards.Add("DRG_402", new CardDef(BattlecryFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m) || m.Id == s.Id) return;
					AddToHand(c, m.Card);
					Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, m.Card));
					Summon(g, c, m.Card, s);
				})
			}));

			// ---------------------------------------------------------------- Druid

			var treenforce = new CardDef(new Power());
			cards.Add("DRG_311", treenforce);                                                                  // Treenforcements (Choose One)
			cards.Add("DRG_311a", new CardDef(new Power { PowerTask = new SummonTask("DRG_311t", SummonSide.SPELL) }));
			cards.Add("DRG_311b", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("DRG_311e", EntityType.TARGET) }));
			cards.Add("DRG_311e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Health_N(V("DRG_311b", "health", 2)), new Effect(GameTag.TAUNT, EffectOperator.SET, 1))
			}));
			cards.Add("DRG_312", new CardDef(new Power { PowerTask = new SummonTask("DRG_311t", SummonSide.RIGHT) }));       // Shrubadier
			cards.Add("DRG_313", new CardDef(new Power { PowerTask = new DiscoverTask(DiscoverType.DRAGON) }));           // Emerald Explorer

			// [DRG_314] Aeroponics (5) — Draw 2 cards. Costs (2) less for each Treant you control.
			cards.Add("DRG_314", new CardDef(new Power
			{
				PowerTask = new EnqueueTask(V("DRG_314", "cards", 2), new DrawTask()),
				Aura = new AdaptiveCostEffect(p => V("DRG_314", "discount per treant", 2) * p.Controller.BoardZone.GetAll().Count(IsTreant))
			}));

			// [DRG_315] Embiggen (0) — Give all minions in your deck +2/+2. They cost (1) more (up to 10).
			cards.Add("DRG_315", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in c.DeckZone.Where(p => Generic.IsMinionCard(p)).ToList())
					{
						Give(g, s, p, "DRG_315e");
						if (p.Cost < 10) Give(g, s, p, "DRG_315e2");
					}
				})
			}));
			cards.Add("DRG_315e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DRG_315", "stats", 2))) }));
			cards.Add("DRG_315e2", new CardDef(new Power { Enchant = new Enchant(Effects.AddCost(1)) }));

			// [DRG_318] Breath of Dreams (2) — Draw a card. If you're holding a Dragon, gain an empty Mana Crystal.
			cards.Add("DRG_318", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new DrawTask(), IfHoldingDragon(new CustomTask((g, c, s, t, stack) =>
				{
					if (c.BaseMana < 10) c.BaseMana++;
				})))
			}));

			// [DRG_319] Goru the Mightree (7) — Taunt. Battlecry: For the rest of the game, your Treants have +1/+1.
			cards.Add("DRG_319", new CardDef(new Power { PowerTask = new AddEnchantmentTask("DRG_319e4", EntityType.CONTROLLER) }));
			cards.Add("DRG_319e4", new CardDef(new Power
			{
				Aura = new MultiAura(
					new Aura(AuraType.BOARD, "DRG_319e5") { Condition = new SelfCondition(IsTreant) },
					new Aura(AuraType.HAND, "DRG_319e5") { Condition = new SelfCondition(IsTreant) })
			}));
			cards.Add("DRG_319e5", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DRG_319", "stats", 1))) }));

			// [DRG_320] Ysera, Unleashed (9) — Battlecry: Shuffle 7 Dream Portals into your deck. When drawn, summon a random Dragon.
			cards.Add("DRG_320", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("DRG_320", "portals", 7); i++)
						Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("DRG_320t")));
				})
			}));
			cards.Add("DRG_320t", new CardDef(new Power
			{
				TopdeckTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.DRAGON) && !x.NeverRandomlyOnBoard), s))
			}));

			// ---------------------------------------------------------------- Hunter

			// [DRG_006] Corrosive Breath (2) — Deal 3 damage to a minion. If you're holding a Dragon, it also hits the enemy hero.
			cards.Add("DRG_006", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("DRG_006", "damage", 3), EntityType.TARGET, true),
					IfHoldingDragon(new DamageTask(V("DRG_006", "damage", 3), EntityType.OP_HERO, true)))
			}));

			// [DRG_007] Stormhammer (3) — Doesn't lose Durability while you control a Dragon. (Attack checks Weapon.KeepsDurability.)
			cards.Add("DRG_007", new CardDef(new Power // "Doesn't lose Durability while you control a Dragon"
			{
				Aura = new AdaptiveEffect(new SelfCondition(w => w.Controller.BoardZone.GetAll().Any(m => m.IsRace(Race.DRAGON))), HsTag.KEEPS_DURABILITY)
			}));

			// [DRG_010] Diving Gryphon (3) — Rush. Battlecry: Draw a Rush minion from your deck.
			cards.Add("DRG_010", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p is Minion m && m.IsRush))
			}));

			// [DRG_095] Veranus (6) — Battlecry: Change the Health of all enemy minions to 1.
			var veranus = new CardDef(new Power { PowerTask = new AddEnchantmentTask("DRG_095e", EntityType.OP_MINIONS) });
			cards.Add("DRG_095", veranus);
			cards.Add("CORE_DRG_095", veranus);
			cards.Add("DRG_095e", new CardDef(new Power { Enchant = new Enchant(Effects.SetMaxHealth(1)) }));

			// [DRG_252] Phase Stalker (2) — After you use your Hero Power, cast a Secret from your deck.
			cards.Add("DRG_252", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.INSPIRE)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var secrets = c.DeckZone.Where(p => p is Spell sp && sp.IsSecret && !c.SecretZone.Any(z => z.Card.Id == p.Card.Id)).ToList();
						if (secrets.Count == 0 || c.SecretZone.IsFull) return;
						var secret = (Spell)secrets[g.Random.Next(secrets.Count)];
						c.DeckZone.Remove(secret);
						PutSecretIntoPlay(c, secret);
					})
				}
			}));

			// [DRG_253] Dwarven Sharpshooter (1) — Your Hero Power can target minions.
			cards.Add("DRG_253", new CardDef(new Power { Aura = new Aura(AuraType.CONTROLLER, new Effect(GameTag.STEADY_SHOT_CAN_TARGET, EffectOperator.ADD, 1)) }));
			cards.Add("DRG_254", new CardDef(new Power { PowerTask = new DiscoverTask(DiscoverType.DRAGON) })); // Primordial Explorer

			// ---------------------------------------------------------------- Mage

			cards.Add("DRG_102", new CardDef(new Power { PowerTask = new DiscoverTask(DiscoverType.DRAGON) })); // Azure Explorer

			// [DRG_104] Chenvaala (3) — After you cast three spells in a turn, summon a 5/5 Elemental.
			cards.Add("DRG_104", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (c.CardsPlayedThisTurn.Count(x => x.Type == CardType.SPELL) == V("DRG_104", "spells", 3))
							Summon(g, c, Cards.FromId("DRG_104t2"), s);
					})
				}
			}));

			// [DRG_106] Arcane Breath (1) — Deal 2 damage to a minion. If you're holding a Dragon, Discover a spell.
			cards.Add("DRG_106", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("DRG_106", "damage", 2), EntityType.TARGET, true),
					IfHoldingDragon(new DiscoverTask(CardType.SPELL)))
			}));

			// [DRG_109] Mana Giant (8) — Costs (1) less for each card you've played this game that didn't start in your deck.
			cards.Add("DRG_109", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => Generic.PlayedNotFromDeck(p.Controller)) // (shared with Techysaurus)
			}));

			// [DRG_270] Malygos, Aspect of Magic (5) — Battlecry: If you're holding a Dragon, Discover an upgraded Mage spell.
			string[] upgraded = { "DRG_270t1", "DRG_270t2", "DRG_270t4", "DRG_270t5", "DRG_270t6", "DRG_270t7", "DRG_270t8", "DRG_270t9", "DRG_270t11" };
			cards.Add("DRG_270", new CardDef(new Power
			{
				PowerTask = IfHoldingDragon(new DiscoverFromTask((g, c, s) => upgraded.Select(Cards.FromId)))
			}));
			cards.Add("DRG_270t1", new CardDef(new Power { PowerTask = new EnqueueTask(V("DRG_270t1", "cards", 4), new DrawTask()) }));
			cards.Add("DRG_270t2", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("DRG_270t2", "spells", 3); i++)
						if (RandomCard(g, x => x.Type == CardType.SPELL && x.HasClass(CardClass.MAGE)) is Card card) AddToHand(c, card);
				})
			}));
			cards.Add("DRG_270t4", new CardDef(new Power { PowerTask = new DamageTask(V("DRG_270t4", "damage", 2), EntityType.OP_MINIONS, true) }));
			cards.Add("DRG_270t5", new CardDef(new Power { PowerTask = ComplexTask.Freeze(EntityType.OP_MINIONS) }));
			cards.Add("DRG_270t6", new CardDef(TargetMinion, new Power { PowerTask = new TransformTask("DRG_270t6t", EntityType.TARGET) }));
			cards.Add("DRG_270t7", new CardDef(new Power { PowerTask = new DamageTask(V("DRG_270t7", "damage", 8), EntityType.OP_MINIONS, true) }));
			cards.Add("DRG_270t8", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("DRG_270t8", "damage", 3), EntityType.TARGET, true), ComplexTask.Freeze(EntityType.TARGET))
			}));
			cards.Add("DRG_270t9", new CardDef(TargetAny, new Power { PowerTask = new DamageTask(V("DRG_270t9", "damage", 8), EntityType.TARGET, true) }));
			cards.Add("DRG_270t11", new CardDef(new Power
			{
				PowerTask = new EnqueueTask(V("DRG_270t11", "damage", 6), ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, 1), true)
			}));

			// [DRG_321] Rolling Fireball (5) — Deal 8 damage to a minion. Any excess damage continues to the left or right.
			cards.Add("DRG_321", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion first)) return;
					int left = V("DRG_321", "damage", 8) + c.CurrentSpellPower;
					if (s is Spell rolling) rolling[HsTag.SPELL_DAMAGE_COUNTED] = 1; // counted once, in the total that rolls on
					int direction = g.Random.Next(2) == 0 ? -1 : 1;
					Minion m = first;
					while (m != null && left > 0)
					{
						left = Generic.DamageWithExcess((IPlayable)s, m, left, false); // (WHIZBANG: the shared excess hit, capped at its Health)
						if (left <= 0 || !(m.IsDead || m.ToBeDestroyed)) break;
						var row = m.Controller.BoardZone;
						int next = m.ZonePosition + direction;
						m = next >= 0 && next < row.Count ? row[next] : null;
					}
				})
			}));

			// [DRG_322] Dragoncaster (6) — Battlecry: If you're holding a Dragon, your next spell this turn costs (0).
			cards.Add("DRG_322", new CardDef(new Power { PowerTask = IfHoldingDragon(new AddEnchantmentTask("DRG_322e", EntityType.CONTROLLER)) }));
			cards.Add("DRG_322e", NextCardEffect(Effects.SetCost(0), SelfCondition.IsSpell, thisTurnOnly: true));

			// ---------------------------------------------------------------- Paladin

			// [DRG_225] Sky Claw (3) — Your other Mechs have +1 Attack. Battlecry: Summon two 1/1 Microcopters.
			cards.Add("DRG_225", new CardDef(new Power
			{
				Aura = new Aura(AuraType.BOARD_EXCEPT_SOURCE, "DRG_225e") { Condition = SelfCondition.IsRace(Race.MECHANICAL) },
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("DRG_225", "microcopters", 2); i++) Summon(g, c, Cards.FromId("DRG_225t"), s, ((Minion)s).ZonePosition + 1);
				})
			}));
			cards.Add("DRG_225e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("DRG_225", "attack", 1))) }));

			// [DRG_226] Amber Watcher (5) — Battlecry: Restore 8 Health.
			var amber = new CardDef(BattlecryAny, new Power { PowerTask = new HealTask(V("DRG_226", "heal", 8), EntityType.TARGET) });
			cards.Add("DRG_226", amber);
			cards.Add("CORE_DRG_226", amber);

			// [DRG_229] Bronze Explorer (3) — Lifesteal. Battlecry: Discover a Dragon.
			var bronze = new CardDef(new Power { PowerTask = new DiscoverTask(DiscoverType.DRAGON) });
			cards.Add("DRG_229", bronze);
			cards.Add("CORE_DRG_229", bronze);

			// [DRG_231] Lightforged Crusader (7) — Battlecry: If your deck has no Neutral cards, add 5 random Paladin cards to your hand.
			cards.Add("DRG_231", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => !c.DeckZone.Any(p => p.Card.HasClass(CardClass.NEUTRAL)), new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("DRG_231", "cards", 5); i++)
						if (RandomCard(g, x => x.HasClass(CardClass.PALADIN)) is Card card) AddToHand(c, card);
				}))
			}));

			// [DRG_232] Lightforged Zealot (4) — Battlecry: If your deck has no Neutral cards, equip a 4/2 Truesilver Champion.
			cards.Add("DRG_232", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => !c.DeckZone.Any(p => p.Card.HasClass(CardClass.NEUTRAL)), new WeaponTask("DRG_232t"))
			}));

			// [DRG_233] Sand Breath (1) — Give a minion +1/+2. Give it Divine Shield if you're holding a Dragon.
			cards.Add("DRG_233", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(
					new AddEnchantmentTask("DRG_233e", EntityType.TARGET),
					IfHoldingDragon(new SetGameTagTask(GameTag.DIVINE_SHIELD, 1, EntityType.TARGET)))
			}));
			cards.Add("DRG_233e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("DRG_233", "attack", 1)), Effects.Health_N(V("DRG_233", "health", 2)))
			}));

			// [DRG_235] Dragonrider Talritha (3) — Deathrattle: Give a Dragon in your hand +3/+3 and this Deathrattle.
			ISimpleTask talritha = new CustomTask((g, c, s, t, stack) =>
			{
				var dragons = c.HandZone.Where(p => p is Minion m && m.IsRace(Race.DRAGON)).ToList();
				if (dragons.Count > 0) Give(g, s, dragons[g.Random.Next(dragons.Count)], "DRG_235e");
			});
			cards.Add("DRG_235", new CardDef(new Power { DeathrattleTask = talritha }));
			cards.Add("DRG_235e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.AttackHealth_N(V("DRG_235", "stats", 3))),
				DeathrattleTask = talritha
			}));

			// [DRG_309] Nozdormu the Timeless (4) — Battlecry: Set each player to 10 Mana Crystals.
			cards.Add("DRG_309", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { c.BaseMana = 10; c.Opponent.BaseMana = 10; })
			}));

			// ---------------------------------------------------------------- Priest

			// [DRG_090] Murozond the Infinite (8) — Battlecry: Play all cards your opponent played last turn.
			var murozond = new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (PlayHistoryEntry played in c.Opponent.PlayHistory.Where(e => e.Turn == g.Turn - 1).ToList())
						PlayFromPack(g, c, s, played.SourceCard);
				})
			});
			cards.Add("DRG_090", murozond);
			cards.Add("CORE_DRG_090", murozond);

			// [DRG_301] Whispers of EVIL (0) — Add a Lackey to your hand.
			cards.Add("DRG_301", new CardDef(new Power { PowerTask = new AddLackeyTask(1) }));

			// [DRG_302] Grave Rune (4) — Give a minion "Deathrattle: Summon 2 copies of this."
			cards.Add("DRG_302", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("DRG_302e", EntityType.TARGET) }));
			cards.Add("DRG_302e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("DRG_302", "copies", 2); i++) Summon(g, c, s.Card, s);
				})
			}));

			// [DRG_304] Chronobreaker (5) — Deathrattle: If you're holding a Dragon, deal 3 damage to all enemy minions.
			cards.Add("DRG_304", new CardDef(new Power { DeathrattleTask = IfHoldingDragon(new DamageTask(V("DRG_304", "damage", 3), EntityType.OP_MINIONS)) }));

			// [DRG_306] Envoy of Lazul (2) — Battlecry: Look at 3 cards. Guess which one is in your opponent's hand to get a copy.
			cards.Add("DRG_306", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var theirs = c.Opponent.HandZone.ToList();
					if (theirs.Count == 0 || c.HandZone.IsFull) return;
					Card real = theirs[g.Random.Next(theirs.Count)].Card;
					var offer = Pick(g, Cards.GenerationPool(g, c).Where(x => x.Id != real.Id && x.Class == real.Class), 2);
					offer.Insert(g.Random.Next(offer.Count + 1), real);
					Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.STACK, offer.ToArray(),
						new CustomTask((g2, c2, s2, chosen, stack2) => { if (chosen?.Card.Id == real.Id) AddToHand(c2, real); }));
				})
			}));

			// [DRG_307] Breath of the Infinite (3) — Deal 2 damage to all minions. If you're holding a Dragon, only damage enemies.
			cards.Add("DRG_307", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => stack.Flag = HoldingDragon(c)),
					new FlagTask(true, new DamageTask(V("DRG_307", "damage", 2), EntityType.OP_MINIONS, true)),
					new FlagTask(false, new DamageTask(V("DRG_307", "damage", 2), EntityType.ALLMINIONS, true)))
			}));

			// [DRG_308] Mindflayer Kaahrj (3) — Battlecry: Choose an enemy minion. Deathrattle: Summon a new copy of it.
			cards.Add("DRG_308", new CardDef(BattlecryEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t != null) s[GameTag.TAG_SCRIPT_DATA_NUM_1] = t.Card.AssetId; }),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					int assetId = s[GameTag.TAG_SCRIPT_DATA_NUM_1];
					if (assetId > 0) Summon(g, c, Cards.FromAssetId(assetId), s);
				})
			}));

			// ---------------------------------------------------------------- Rogue

			// [DRG_028] Dragon's Hoard (1) — Discover a Legendary minion from another class.
			cards.Add("DRG_028", new CardDef(new Power
			{
				PowerTask = new DiscoverFromTask((g, c, s) => OtherClassCards(g, c).Where(x => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY))
			}));

			// [DRG_031] Necrium Apothecary (4) — Combo: Draw a Deathrattle minion from your deck and gain its Deathrattle.
			cards.Add("DRG_031", new CardDef(new Power
			{
				ComboTask = new CustomTask((g, c, s, t, stack) =>
				{
					var withDeathrattle = c.DeckZone.Where(p => Generic.IsMinionCard(p) && p.Card.Deathrattle).ToList();
					if (withDeathrattle.Count == 0) return;
					IPlayable drawn = withDeathrattle[g.Random.Next(withDeathrattle.Count)];
					Generic.Draw(c, drawn);
					s[GameTag.TAG_SCRIPT_DATA_NUM_2] = drawn.Card.AssetId;
					Give(g, s, (IPlayable)s, "DRG_031e");
				})
			}));
			cards.Add("DRG_031e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					int assetId = s[GameTag.TAG_SCRIPT_DATA_NUM_2];
					if (assetId > 0) Cards.FromAssetId(assetId).Power?.DeathrattleTask?.Process(g, c, s, null);
				})
			}));

			// [DRG_033] Candle Breath (6) — Draw 3 cards. Costs (3) less while you're holding a Dragon.
			cards.Add("DRG_033", new CardDef(new Power
			{
				PowerTask = new EnqueueTask(V("DRG_033", "cards", 3), new DrawTask()),
				Aura = new AdaptiveCostEffect(p => HoldingDragon(p.Controller, p) ? V("DRG_033", "discount", 3) : 0)
			}));

			// [DRG_034] Stowaway (5) — Battlecry: If there are cards in your deck that didn't start there, draw 2 of them.
			cards.Add("DRG_034", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.DeckZone.Any(p => p.Id >= g.FirstGeneratedEntityId),
					new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p.Id >= g.FirstGeneratedEntityId, V("DRG_034", "cards", 2))))
			}));

			// [DRG_035] Bloodsail Flybooter (1) — Battlecry: Add two 1/1 Pirates to your hand.
			cards.Add("DRG_035", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("DRG_035", "pirates", 2); i++) AddToHand(c, Cards.FromId("DRG_035t"));
				})
			}));

			// [DRG_036] Waxadred (5) — Deathrattle: Shuffle a Candle into your deck that resummons Waxadred when drawn.
			cards.Add("DRG_036", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
					Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("DRG_036t"))))
			}));
			cards.Add("DRG_036t", new CardDef(new Power
			{
				TopdeckTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("DRG_036"), s))
			}));

			// [DRG_037] Flik Skyshiv (6) — Battlecry: Destroy a minion and all copies of it (wherever they are).
			var flik = new CardDef(BattlecryMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion target)) return;
					string id = target.Card.Id;
					// each zone's destroy rule (Generic.Destroy): in play pending destroy, hand = discard, deck = graveyard
					var copies = new[] { c, c.Opponent }.SelectMany(p => p.BoardZone.GetAll().Cast<IPlayable>()
						.Concat(p.HandZone).Concat(p.DeckZone)).Where(x => x.Card.Id == id).ToList();
					foreach (IPlayable x in copies) Generic.Destroy(x, s, copies.Count > 1);
				})
			});
			cards.Add("DRG_037", flik);
			cards.Add("CORE_DRG_037", flik);

			// ---------------------------------------------------------------- Shaman

			// [DRG_096] Bandersmosh (5) — Each turn this is in your hand, transform it into a 5/5 copy of a random Legendary minion.
			// (WONDERS: the shared in-hand shift, Generic.ShiftInHand — once a turn, TRANSFORMED_FROM_CARD = Bandersmosh as live NIGHT 13708)
			ISimpleTask smosh = new CustomTask((g, c, s, t, stack) =>
				Generic.ShiftInHand(s, me => RandomCard(g, x => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY),
					Cards.FromId("DRG_096e"), Cards.FromId("DRG_096e2")));
			cards.Add("DRG_096", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START) { TriggerActivation = TriggerActivation.HAND, SingleTask = smosh }
			}));
			cards.Add("DRG_096e", new CardDef(new Power
			{
				Enchant = new Enchant(GameTag.SHIFTING_MINION, EffectOperator.SET, 1) { RemoveWhenPlayed = true },
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = smosh }
			}));
			cards.Add("DRG_096e2", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.SetAttack(5), Effects.SetMaxHealth(5), Effects.SetCost(V("DRG_096", "cost", 5)))
			}));

			// [DRG_215] Storm's Wrath (1) — Give your minions +1/+1. Overload: (1)
			cards.Add("DRG_215", new CardDef(new Power { PowerTask = new AddEnchantmentTask("DRG_215e", EntityType.MINIONS) }));
			cards.Add("DRG_215e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DRG_215", "stats", 1))) }));

			// [DRG_216] Surging Tempest (1) — Has +1 Attack while you have Overloaded Mana Crystals.
			cards.Add("DRG_216", new CardDef(new Power
			{
				Aura = new AdaptiveEffect(GameTag.ATK, EffectOperator.ADD, p => Overloaded(p.Controller) ? V("DRG_216", "attack", 1) : 0)
			}));

			// [DRG_219] Lightning Breath (3) — Deal 4 damage to a minion. If you're holding a Dragon, also damage its neighbors.
			cards.Add("DRG_219", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => HoldingDragon(c), DragonsFury(true), DragonsFury(false))
			}));

			// [DRG_223] Cumulo-Maximus (5) — Battlecry: If you have Overloaded Mana Crystals, deal 5 damage.
			cards.Add("DRG_223", new CardDef(BattlecryAny, new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => stack.Flag = Overloaded(c)),
					new FlagTask(true, new DamageTask(V("DRG_223", "damage", 5), EntityType.TARGET)))
			}));

			// [DRG_224] Nithogg (6) — Battlecry: Summon two 0/3 Eggs. Next turn they hatch into 4/4 Drakes with Rush.
			cards.Add("DRG_224", new CardDef(new Power { PowerTask = new SummonTask("DRG_224t", V("DRG_224", "eggs", 2), SummonSide.RIGHT) }));
			cards.Add("DRG_224t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = new TransformTask("DRG_224t2", EntityType.SOURCE) }
			}));

			// ---------------------------------------------------------------- Warlock

			// [DRG_201] Crazed Netherwing (5) — Battlecry: If you're holding a Dragon, deal 3 damage to all other characters.
			cards.Add("DRG_201", new CardDef(new Power { PowerTask = IfHoldingDragon(new DamageTask(V("DRG_201", "damage", 3), EntityType.ALL_NOSOURCE)) }));

			// [DRG_204] Dark Skies (3) — Deal 1 damage to a random minion. Repeat for each card in your hand.
			cards.Add("DRG_204", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int times = 1 + c.HandZone.Count;
					for (int i = 0; i < times; i++)
					{
						var targets = c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => !m.IsDead && !m.ToBeDestroyed).ToList();
						if (targets.Count == 0) break;
						Generic.DamageCharFunc((IPlayable)s, targets[g.Random.Next(targets.Count)], V("DRG_204", "damage", 1), true);
					}
				})
			}));

			// [DRG_205] Nether Breath (2) — Deal 2 damage. If you're holding a Dragon, deal 4 damage with Lifesteal instead.
			cards.Add("DRG_205", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => HoldingDragon(c),
					new CustomTask((g, c, s, t, stack) =>
					{
						if (!(t is ICharacter target)) return;
						int before = target.Health;
						Generic.DamageCharFunc((IPlayable)s, target, V("DRG_205", "dragon damage", 4), true);
						int dealt = System.Math.Max(0, before - target.Health);
						if (dealt > 0) c.Hero.TakeHeal((IPlayable)s, dealt);
					}),
					new CustomTask((g, c, s, t, stack) =>
					{
						if (t is ICharacter target) Generic.DamageCharFunc((IPlayable)s, target, V("DRG_205", "damage", 2), true);
					}))
			}));

			// [DRG_206] Rain of Fire (1) — Deal 1 damage to all characters.
			cards.Add("DRG_206", new CardDef(new Power { PowerTask = new DamageTask(V("DRG_206", "damage", 1), EntityType.ALL, true) }));

			// [DRG_207] Abyssal Summoner (6) — Battlecry: Summon a Demon with Taunt and stats equal to your hand size.
			cards.Add("DRG_207", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = c.HandZone.Count;
					if (n == 0 || c.BoardZone.IsFull) return;
					var demon = (Minion)Entity.FromCard(c, Cards.FromId("DRG_207t"));
					demon.AttackDamage = n;
					demon.BaseHealth = n;
					Generic.SummonBlock.Invoke(g, demon, ((Minion)s).ZonePosition + 1, s);
				})
			}));

			// [DRG_208] Valdris Felgorge (7) — Battlecry: Increase your maximum hand size to 12. Draw 4 cards.
			cards.Add("DRG_208", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new CustomTask((g, c, s, t, stack) => c[GameTag.MAXHANDSIZE] = System.Math.Max(c[GameTag.MAXHANDSIZE], V("DRG_208", "hand size", 12))),
					new EnqueueTask(V("DRG_208", "cards", 4), new DrawTask()))
			}));

			// [DRG_209] Zzeraku the Warped (8) — Whenever your hero takes damage, summon a 6/6 Nether Drake.
			cards.Add("DRG_209", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE) { TriggerSource = TriggerSource.HERO, SingleTask = new SummonTask("DRG_209t", SummonSide.RIGHT) }
			}));

			// ---------------------------------------------------------------- Warrior

			// [DRG_020] EVIL Quartermaster (3) — Battlecry: Add a Lackey to your hand. Gain 3 Armor.
			cards.Add("DRG_020", new CardDef(new Power { PowerTask = ComplexTask.Create(new AddLackeyTask(1), new ArmorTask(V("DRG_020", "armor", 3))) }));

			// [DRG_022] Ramming Speed (3) — Force a minion to attack one of its neighbors.
			cards.Add("DRG_022", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					var neighbors = m.GetAdjacentMinions();
					if (neighbors.Length == 0) return;
					EventMetaData temp = g.CurrentEventData;
					Minion victim = neighbors[g.Random.Next(neighbors.Length)];
					// its client effect: RammingSpeed_AttackRight / _AttackLeft (Loader.SelectFx), by where the neighbor is
					Generic.SelectEffect(s, Generic.SideSelection((IPlayable)s, 0, m, victim));
					Generic.AttackBlock.Invoke(m.Controller, m, victim, true, true);
					g.DeathProcessingAndAuraUpdate();
					g.CurrentEventData = temp;
				})
			}));

			// [DRG_023] Skybarge (3) — After you summon a Pirate, deal 2 damage to a random enemy.
			cards.Add("DRG_023", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					Condition = SelfCondition.IsRace(Race.PIRATE),
					SingleTask = ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, V("DRG_023", "damage", 2))
				}
			}));

			// [DRG_025] Ancharrr (3) — After your hero attacks, draw a Pirate from your deck.
			cards.Add("DRG_025", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p is Minion m && m.IsRace(Race.PIRATE)))
				}
			}));

			// [DRG_026] Deathwing, Mad Aspect (8) — Battlecry: Attack ALL other minions.
			var deathwing = new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					var others = c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => m.Id != me.Id).ToList();
					EventMetaData temp = g.CurrentEventData;
					while (others.Count > 0)
					{
						if (me.IsDead || me.ToBeDestroyed || me.Zone?.Type != SabberStoneCore.Enums.Zone.PLAY) break;
						Minion victim = others[g.Random.Next(others.Count)];
						others.Remove(victim);
						if (victim.IsDead || victim.ToBeDestroyed) continue;
						Generic.AttackBlock.Invoke(c, me, victim, true, true);
						g.DeathProcessingAndAuraUpdate();
					}
					g.CurrentEventData = temp;
				})
			});
			cards.Add("DRG_026", deathwing);
			cards.Add("CORE_DRG_026", deathwing);

			// [DRG_500] Molten Breath (4) — Deal 5 damage to a minion. If you're holding a Dragon, gain 5 Armor.
			cards.Add("DRG_500", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("DRG_500", "damage", 5), EntityType.TARGET, true),
					IfHoldingDragon(new ArmorTask(V("DRG_500", "armor", 5))))
			}));
		}
	}
}
