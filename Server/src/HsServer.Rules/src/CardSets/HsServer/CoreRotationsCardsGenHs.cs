// HsServer addition (not upstream SabberStone): Core-set cards from earlier Core rotations (2022-2025) that exist only in
// the client DB (loaded by ReprintCards as client-only cards). They are genuine cards of the Wild pool; this gives them their
// effect. Every number goes through CardValues (V).
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
		/// <summary>Engine-internal script data (ids >= 1000: never reported to the client).</summary>
		private const GameTag HS_SCRIPT_A = (GameTag)990001, HS_SCRIPT_B = (GameTag)990002;

		/// <summary>The playable version of a card by name: the current Core copy if there is one, else the original. Hero
		/// cards count (Galakrond); hero skins (HERO_04a "Lady Liadrin") don't.</summary>
		private static Card CardByName(string name) =>
			Cards.All.Where(c => c.Name == name && c.Collectible && !c.Id.StartsWith("HERO_"))
				.OrderBy(c => c.Id.StartsWith("CORE_") ? 0 : c.Id.StartsWith("VAN_") || c.Id.StartsWith("TUTR_") ? 2 : 1)
				.FirstOrDefault();

		/// <summary>The Core Gifts: Discover one of three iconic spells; it is Temporary (GBL_999e: discarded at the end of your turn).</summary>
		private static CardDef Gift(params string[] names) => new CardDef(new Power
		{
			PowerTask = new DiscoverFromTask((g, c, s) => names.Select(CardByName).Where(x => x != null), "GBL_999e")
		});

		/// <summary>Harth Stonebrew's 13 iconic hands (wiki Harth_Stonebrew), by name: class, the 8 cards.</summary>
		private static readonly Dictionary<string, (CardClass Class, string Key, string[] Cards)> HarthHands = new (CardClass, string, string[])[]
		{
			(CardClass.DEATHKNIGHT, "Blood", new[] { "Blood Boil", "Soulstealer", "Gnome Muncher", "Alexandros Mograine", "Vampiric Blood", "Obliterate", "Asphyxiate", "Patchwerk" }),
			(CardClass.DEATHKNIGHT, "Frost", new[] { "Glacial Advance", "Lady Deathwhisper", "School Teacher", "Frostwyrm's Fury", "Howling Blast", "Frost Strike", "Thassarian", "Horn of Winter" }),
			(CardClass.DEATHKNIGHT, "Unholy", new[] { "Amalgam of the Deep", "Grave Strength", "Nerubian Swarmguard", "Hawkstrider Rancher", "Skeletal Sidekick", "Blightfang", "Plague Strike", "Battlefield Necromancer" }),
			(CardClass.DEMONHUNTER, "Demon Hunter", new[] { "Twin Slice", "Skull of Gul'dan", "Warglaives of Azzinoth", "Altruis the Outcast", "Metamorphosis", "Imprisoned Antaen", "Priestess of Fury", "Eye Beam" }),
			(CardClass.DRUID, "Druid", new[] { "King Togwaggle", "Twig of the World Tree", "Malfurion the Pestilent", "Naturalize", "Azalina Soulthief", "Lesser Jasper Spellstone", "Spreading Plague", "Branching Paths" }),
			(CardClass.HUNTER, "Hunter", new[] { "Lesser Emerald Spellstone", "Wandering Monster", "Misdirection", "Explosive Trap", "Cat Trick", "Deathstalker Rexxar", "Cloaked Huntress", "Freezing Trap" }),
			(CardClass.MAGE, "Mage", new[] { "Alexstrasza", "Ice Block", "Frost Nova", "Doomsayer", "Ice Lance", "Fireball", "Frostbolt", "Archmage Antonidas" }),
			(CardClass.PALADIN, "Paladin", new[] { "Lady Liadrin", "Aldor Truthseeker", "Libram of Hope", "Pen Flinger", "Libram of Judgment", "Libram of Justice", "Libram of Wisdom", "Aldor Attendant" }),
			(CardClass.PRIEST, "Priest", new[] { "Galakrond, the Unspeakable", "Soul Mirror", "Kronx Dragonhoof", "Devoted Maniac", "Shield of Galakrond", "Disciple of Galakrond", "Fate Weaver", "Time Rip" }),
			(CardClass.ROGUE, "Rogue", new[] { "Youthful Brewmaster", "Gang Up", "Preparation", "Coldlight Oracle", "Brann Bronzebeard", "Antique Healbot", "Shadowstep", "Vanish" }),
			(CardClass.SHAMAN, "Shaman", new[] { "Healing Rain", "Saronite Chain Gang", "Grumble, Worldshaker", "Glacial Shard", "Zola the Gorgon", "Lifedrinker", "Shudderwock", "Volcano" }),
			(CardClass.WARLOCK, "Warlock", new[] { "Skull of the Man'ari", "Carnivorous Cube", "Bloodreaver Gul'dan", "Faceless Manipulator", "Doomguard", "Voidlord", "Defile", "Dark Pact" }),
			(CardClass.WARRIOR, "Warrior", new[] { "Whirlwind", "Grim Patron", "Inner Rage", "Execute", "Frothing Berserker", "Death's Bite", "Emperor Thaurissan", "Warsong Commander" }),
		}.ToDictionary(h => h.Item2, h => (h.Item1, h.Item2, h.Item3));

		private static bool IsRecruit(IPlayable p) => SelfCondition.IsSilverHandRecruitCard(p.Card);   // client tag (audit A2)

		private static void CoreRotations(IDictionary<string, CardDef> cards)
		{
			// [GBL_999e] Temporary — Discarded at the end of your turn. The client's Temporary enchantment (invisible): it marks the
			// card Temporary (TEMPORARY + VALEERASHADOW: the outline the client shows; live-20261003-dk-classxp). At the end of
			// your turn the card is discarded if still in hand (wiki Temporary: since patch 30.0 a discard, so discard triggers
			// fire); played, the enchantment leaves and the card is an ordinary one again. Generic.MakeTemporary.
			cards.Add("GBL_999e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(HsTag.TEMPORARY, EffectOperator.SET, 1), new Effect(GameTag.VALEERASHADOW, EffectOperator.SET, 1))
				{
					RemoveWhenPlayed = true
				},
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || !(e.Target is IPlayable card) || card.Zone?.Type != SabberStoneCore.Enums.Zone.HAND) return;
						Generic.DiscardBlock.Invoke(card.Controller, card);
					})
				}
			}));
			cards.Add("CORE_GIFT_02", Gift("Frostbolt", "Arcane Intellect", "Fireball"));
			cards.Add("CORE_GIFT_03", Gift("Quick Shot", "Deadly Shot", "Explosive Shot"));
			cards.Add("CORE_GIFT_04", Gift("Dark Transformation", "Howling Blast", "Death Strike"));
			cards.Add("CORE_GIFT_05", Gift("Equality", "Consecration", "Blessing of Kings"));
			cards.Add("CORE_GIFT_06", Gift("Lightning Storm", "Hex", "Bloodlust"));
			cards.Add("CORE_GIFT_07", Gift("Execute", "Shield Block", "Brawl"));
			cards.Add("CORE_GIFT_08", Gift("Fel Barrage", "Chaos Strike", "Chaos Nova"));
			cards.Add("CORE_GIFT_09", Gift("Backstab", "Deadly Poison", "Fan of Knives"));
			cards.Add("CORE_GIFT_10", Gift("Feral Rage", "Wild Growth", "Swipe"));
			cards.Add("CORE_GIFT_11", Gift("Mortal Coil", "Siphon Soul", "Twisting Nether"));
			cards.Add("CORE_GIFT_12", Gift("Power Word: Shield", "Shadow Word: Pain", "Mind Control"));

			// [GIFT_01] Harth Stonebrew (6) — Battlecry: Replace your hand with an iconic one from Hearthstone's past. (Once per
			// game.) Wiki (Harth_Stonebrew): 1 of 11 classes at random, whatever your class (a Death Knight roll then picks 1 of
			// 3 rune hands); the hand is always those 8 cards, whatever its size; a player-bound once-per-game rule. Your cards
			// become the new ones (a card more than 8 leaves the hand).
			cards.Add("CORE_GIFT_01", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c[HS_SCRIPT_A] > 0) return;
					c[HS_SCRIPT_A] = 1;
					string[][] classes = HarthHands.Values.GroupBy(h => h.Class).Select(grp => grp.Select(h => h.Key).ToArray()).ToArray();
					string[] options = classes[g.Random.Next(classes.Length)];
					var hand = HarthHands[options[g.Random.Next(options.Length)]].Cards.Select(CardByName).Where(x => x != null).ToList();
					// The old hand leaves inside the server-named RemoveHand effect (its cards flying off: the client's
					// m_SubSpellEffectDefs), then the 8 cards arrive as NEW cards (user rule: generated cards are new entities; the
					// client's own CardsInHand_OverrideSpawn plays their arrival). Transforming the old cards kept their own looks
					// (a golden Legendary in hand became a golden "Legendary" Ice Block).
					var old = c.HandZone.ToList();
					Generic.NamedFx((IPlayable)s, "RemoveHand", old.Select(p => p.Id), () =>
					{
						foreach (IPlayable p in old) Generic.RemoveFromHand(c, p);
					});
					foreach (Card card in hand) AddToHand(c, card);
				}),
				OwnFx = true // it places its RemoveHand effect itself
			}));

			// Gift spells the engine lacks.
			// [RLK_057] Dark Transformation (2) — Transform an Undead into a 4/5 Undead Monstrosity with Rush.
			cards.Add("RLK_057", new CardDef(new Dictionary<PlayReq, int>
				{
					{ PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_TARGET_WITH_RACE, (int)Race.UNDEAD }
				},
				new Power { PowerTask = new TransformTask("RLK_057t", EntityType.TARGET) }));
			// [RLK_015] Howling Blast (4) — Deal 3 damage to an enemy and Freeze it. Deal 1 damage to all other enemies.
			cards.Add("RLK_015", new CardDef(TargetEnemy, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("RLK_015", "damage", 3), EntityType.TARGET, true),
					ComplexTask.Freeze(EntityType.TARGET),
					new CustomTask((g, c, s, t, stack) => Generic.SplashFx((IPlayable)s, () =>
						new DamageTask(V("RLK_015", "others", 1), EntityType.ENEMIES_NOTARGET, true).Process(g, c, s, t, stack)))),
				OwnFx = true // its recorded shockwave is the splash's (Generic.SplashFx), not the main hit's
			}));
			// [SW_040] Fel Barrage (2) — Deal 2 damage to the lowest Health enemy, twice.
			cards.Add("SW_040", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < 2; i++)
					{
						var enemies = c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed).Cast<ICharacter>().ToList();
						enemies.Add(c.Opponent.Hero);
						int low = enemies.Min(e => e.Health);
						var lowest = enemies.Where(e => e.Health == low).ToList();
						Generic.DamageCharFunc((IPlayable)s, lowest[g.Random.Next(lowest.Count)], V("SW_040", "damage", 2), true);
					}
				})
			}));

			// ---------------------------------------------------------------- Neutral

			// [SCH_160] Wandmaker (2) — Battlecry: Add a 1-Cost spell from your class to your hand.
			cards.Add("CORE_SCH_160", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (RandomCard(g, x => x.Type == CardType.SPELL && x.HasClass(c.HeroClass) && x.Cost == 1) is Card card) AddToHand(c, card);
				})
			}));
			// [SCH_312] Tour Guide (1) — Battlecry: Your next Hero Power costs (0).
			cards.Add("CORE_SCH_312", new CardDef(new Power { PowerTask = new AddEnchantmentTask("SCH_312e", EntityType.CONTROLLER) }));
			cards.Add("SCH_312e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HEROPOWER, Effects.SetCost(0)),
				Trigger = new Trigger(TriggerType.INSPIRE) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = RemoveEnchantmentTask.Task }
			}));
			// [SW_080] Cornelius Roame (6) — At the start and end of each player's turn, draw a card.
			cards.Add("CORE_SW_080", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.TURN_START) { EitherTurn = true, SingleTask = new DrawTask() },
					new Trigger(TriggerType.TURN_END) { EitherTurn = true, SingleTask = new DrawTask() })
			}));
			// [TOY_100] Gnomelia, S.A.F.E. Pilot (6) — Rush. Also damages minions next to whomever this attacks. Deathrattle: Deal 2
			// damage to all enemies.
			cards.Add("CORE_TOY_100", new CardDef(new Power
			{
				Trigger = Cleave(),
				DeathrattleTask = new DamageTask(V("TOY_100", "damage", 2), EntityType.ENEMIES),
				DeathrattleEffectIndex = 1 // client trigger effects: 0 = TOYFX_..._Trigger_AdjacentBurst, 1 = TOYFX_..._AE (the Deathrattle)
			}));
			// [TOY_101] Night Elf Huntress (5) — Battlecry: Deal 3 damage to three different enemies. (You pick the targets!)
			// (Generic.PickTargets: aim, shoot, three times; the battlecry can't aim at a Stealthed minion.)
			cards.Add("CORE_TOY_101", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.PickTargets(c, s, V("TOY_101", "enemies", 3),
					ch => ch.Controller != c && !(ch is Minion m && m.HasStealth),
					(g2, c2, s2, enemy) => Generic.DamageCharFunc((IPlayable)s2, enemy, V("TOY_101", "damage", 3), false),
					counter: GameTag.TAG_SCRIPT_DATA_NUM_1))
			}));

			// [TOY_102] Footman (4) — Taunt. Adjacent minions are Immune while attacking.
			cards.Add("CORE_TOY_102", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.ATTACK)
					{
						TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (s is Minion me && t is Minion attacker && attacker.Zone == me.Zone && System.Math.Abs(attacker.ZonePosition - me.ZonePosition) == 1)
							{
								attacker.IsImmune = true;
								attacker[HS_SCRIPT_A] = 1; // Footman made it Immune
							}
						})
					},
					new Trigger(TriggerType.AFTER_ATTACK)
					{
						TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (t is Minion attacker && attacker[HS_SCRIPT_A] == 1)
							{
								attacker.IsImmune = false;
								attacker[HS_SCRIPT_A] = 0;
							}
						})
					})
			}));
			// [TOY_103] Warsong Grunt (5) — Rush. After this attacks and kills a minion, it may attack again.
			cards.Add("CORE_TOY_103", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me && g.CurrentEventData?.EventTarget is Minion victim && (victim.IsDead || victim.ToBeDestroyed) && !me.IsDead)
							me.IsExhausted = false;
					})
				}
			}));
			// [TSC_827] Vicious Slitherspear (1) — After you cast a spell, gain +1 Attack until your next turn.
			cards.Add("CORE_TSC_827", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = new AddEnchantmentTask("TSC_827e", EntityType.SOURCE) }
			}));
			cards.Add("TSC_827e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("TSC_827", "attack", 1))),
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = RemoveEnchantmentTask.Task }
			}));
			// [VAN_EX1_572] Ysera (9) — At the end of your turn, get two random Dream cards.
			cards.Add("CORE_VAN_EX1_572", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						Card[] dreams = Cards.FromId("EX1_572").Entourage.Select(Cards.FromId).ToArray();
						for (int i = 0; i < V("VAN_EX1_572", "dreams", 2) && dreams.Length > 0; i++) AddToHand(c, dreams[g.Random.Next(dreams.Length)]);
					})
				}
			}));
			// [WON_142] Menagerie Jug (5) — Battlecry: Give 3 random friendly minions of different minion types +2/+2.
			cards.Add("CORE_WON_142", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var typed = c.BoardZone.GetAll().Where(m => m.Id != s.Id && m[GameTag.CARDRACE] != 0).ToList();
					var used = new HashSet<int>();
					for (int i = 0; i < V("WON_142", "minions", 3); i++)
					{
						var options = typed.Where(m => !used.Contains(m[GameTag.CARDRACE]) || m[GameTag.CARDRACE] == (int)Race.ALL).ToList();
						if (options.Count == 0) break;
						Minion pick = options[g.Random.Next(options.Count)];
						used.Add(pick[GameTag.CARDRACE]);
						typed.Remove(pick);
						Give(g, s, pick, "WON_142e");
					}
				})
			}));
			cards.Add("WON_142e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("WON_142", "stats", 2))) }));
			// [YOD_006] Escaped Manasaber (4) — Stealth. Whenever this attacks, gain 1 Mana Crystal this turn only.
			cards.Add("CORE_YOD_006", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) => c.TemporaryMana += V("YOD_006", "mana", 1))
				}
			}));
			// [YOP_032] Armor Vendor (1) — Battlecry: Give 4 Armor to each hero.
			cards.Add("CORE_YOP_032", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new ArmorTask(V("YOP_032", "armor", 4)), new ArmorTask(V("YOP_032", "armor", 4), true))
			}));

			// ---------------------------------------------------------------- Demon Hunter

			// [BAR_330] Tuskpiercer (1) — Deathrattle: Draw a Deathrattle minion.
			cards.Add("CORE_BAR_330", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p is Minion && p.Card.Deathrattle))
			}));
			// [BT_036] Coordinated Strike (3) — Summon three 1/1 Illidari with Rush.
			cards.Add("CORE_BT_036", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } },
				new Power { PowerTask = new SummonTask("BT_036t", V("BT_036", "illidari", 3), SummonSide.SPELL) }));
			// [BT_235] Chaos Nova (5) — Deal 4 damage to all minions.
			cards.Add("CORE_BT_235", new CardDef(new Power { PowerTask = new DamageTask(V("BT_235", "damage", 4), EntityType.ALLMINIONS, true) }));
			// [BT_271] Flamereaper (7) — Also damages the minions next to whomever your hero attacks. (The shared Cleave, After
			// Attack Event.)
			cards.Add("CORE_BT_271", new CardDef(new Power { Trigger = Cleave(TriggerSource.HERO) }));
			// [BT_323] Sightless Watcher (2) — Battlecry: Look at 3 cards in your deck. Choose one to put on top.
			cards.Add("CORE_BT_323", new CardDef(new Power
			{
				// live (live-20261005 112456): the options are linked copies of the deck cards (Generic.DiscoverFromCards: the
				// picked copy then leaves; ours offered unlinked new cards and the picked one stayed on screen, user 2026-10-05)
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.DiscoverFromCards(c, s, Generic.PickFromDeck(c, _ => true, V("BT_323", "cards", 3)), (g2, c2, s2, card) =>
					{
						c2.DeckZone.Remove(card);
						c2.DeckZone.Add(card); // on top
					}))
			}));
			// [BT_355] Wrathscale Naga (3) — After a friendly minion dies, deal 3 damage to a random enemy.
			cards.Add("CORE_BT_355", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF,
					SingleTask = ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, V("BT_355", "damage", 3))
				}
			}));
			// [BT_427] Feast of Souls (1) — Draw a card for each friendly minion that died this turn.
			cards.Add("CORE_BT_427", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < c.NumFriendlyMinionsThatDiedThisTurn; i++) Generic.Draw(c);
				})
			}));
			// [BT_429] Metamorphosis (4) — Swap your Hero Power to "Deal 5 damage." After 2 uses, swap it back.
			cards.Add("CORE_BT_429", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c[HS_SCRIPT_B] == 0) c[HS_SCRIPT_B] = c.Hero.HeroPower.Card.AssetId;
					new ReplaceHeroPower(Cards.FromId("BT_429p")).Process(g, c, s, null);
				})
			}));
			cards.Add("BT_429p", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("BT_429p", "damage", 5), EntityType.TARGET),
					new CustomTask((g, c, s, t, stack) =>
					{
						new ReplaceHeroPower(Cards.FromId("BT_429p2")).Process(g, c, s, null);
						c.Hero.HeroPower.IsExhausted = true;
					}))
			}));
			cards.Add("BT_429p2", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("BT_429p", "damage", 5), EntityType.TARGET),
					new CustomTask((g, c, s, t, stack) =>
					{
						int original = c[HS_SCRIPT_B];
						c[HS_SCRIPT_B] = 0;
						if (original > 0) new ReplaceHeroPower(Cards.FromAssetId(original)).Process(g, c, s, null);
						c.Hero.HeroPower.IsExhausted = true;
					}))
			}));
			// [BT_430] Warglaives of Azzinoth (5) — After attacking a minion, your hero may attack again.
			cards.Add("CORE_BT_430", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventTarget is Minion) c.Hero.IsExhausted = false;
					})
				}
			}));
			// [BT_514] Immolation Aura (2) — Deal 1 damage to all minions twice.
			cards.Add("CORE_BT_514", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("BT_514", "damage", 1), EntityType.ALLMINIONS, true),
					new DamageTask(V("BT_514", "damage", 1), EntityType.ALLMINIONS, true))
			}));
			// [BT_922] Umberwing (2) — Battlecry: Summon two 1/1 Felwings.
			cards.Add("CORE_BT_922", new CardDef(new Power { PowerTask = new SummonTask("BT_922t", V("BT_922", "felwings", 2), SummonSide.DEFAULT) })); // a weapon: no board position
			// [CS3_019] Kor'vas Bloodthorn (2) — Charge, Lifesteal. After you play a card with Outcast, return this to your hand.
			cards.Add("CORE_CS3_019", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = IsOutcastCard,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me && me.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY && !me.IsDead && !c.HandZone.IsFull)
							new ReturnHandTask(EntityType.SOURCE).Process(g, c, s, null);
					})
				}
			}));
			// [DMF_224] Expendable Performers (7) — Summon seven 1/1 Illidari with Rush. If they all die this turn, summon seven more.
			cards.Add("CORE_DMF_224", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int summoned = 0;
					for (int i = 0; i < V("DMF_224", "illidari", 7) && !c.BoardZone.IsFull; i++, summoned++)
						Generic.SummonBlock.Invoke(g, (Minion)Entity.FromCard(c, Cards.FromId("BT_036t")), -1, s);
					if (summoned == 0) return;
					Generic.AddEnchantmentBlock(g, Cards.FromId("DMF_224e"), (IPlayable)s, c, summoned, 0, 0);
				})
			}));
			cards.Add("DMF_224e", new CardDef(new Power
			{
				Enchant = new Enchant(GameTag.TAG_SCRIPT_DATA_NUM_1, EffectOperator.SET),
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.DEATH)
					{
						TriggerSource = TriggerSource.FRIENDLY,
						Condition = new SelfCondition(p => p.Card.Id == "BT_036t"),
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (!(s is Enchantment e)) return;
							int left = e[GameTag.TAG_SCRIPT_DATA_NUM_1] - 1;
							e[GameTag.TAG_SCRIPT_DATA_NUM_1] = left;
							if (left > 0) return;
							for (int i = 0; i < V("DMF_224", "illidari", 7) && !c.BoardZone.IsFull; i++)
								Generic.SummonBlock.Invoke(g, (Minion)Entity.FromCard(c, Cards.FromId("BT_036t")), -1, e);
							RemoveEnchantmentTask.Task.Process(e.Game, e.Controller, e, null); // reverts its effects too
						})
					},
					new Trigger(TriggerType.TURN_END) { SingleTask = RemoveEnchantmentTask.Task })
			}));
			// [DMF_231] Zai, the Incredible (5) — Battlecry: Copy the left- and right-most cards in your hand.
			cards.Add("CORE_DMF_231", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var hand = c.HandZone.ToList();
					if (hand.Count == 0) return;
					IPlayable left = hand[0], right = hand[hand.Count - 1];
					AddToHand(c, left.Card);
					if (right.Id != left.Id) AddToHand(c, right.Card);
				})
			}));
			// [TSC_217] Wayward Sage (2) — Outcast: Reduce the Cost of the left and right-most cards in your hand by (1).
			cards.Add("CORE_TSC_217", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new ConditionTask(EntityType.SOURCE, PlayedAsOutcast),
					new FlagTask(true, new CustomTask((g, c, s, t, stack) =>
					{
						var hand = c.HandZone.ToList();
						if (hand.Count == 0) return;
						Give(g, s, hand[0], "TSC_217e");
						if (hand.Count > 1) Give(g, s, hand[hand.Count - 1], "TSC_217e");
					})))
			}));
			cards.Add("TSC_217e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("TSC_217", "discount", 1))) }));

			// ---------------------------------------------------------------- Death Knight

			// [RLK_012] Soulbreaker (3) — After your hero attacks and kills a minion, gain 2 Corpses.
			cards.Add("CORE_RLK_012", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventTarget is Minion m && (m.IsDead || m.ToBeDestroyed)) Generic.GainCorpses(c, s, V("RLK_012", "corpses", 2));
					})
				}
			}));
			// [RLK_035] Corpse Explosion (5) — Detonate a Corpse to deal 1 damage to all minions. If any are still alive, repeat.
			cards.Add("CORE_RLK_035", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					while (Generic.SpendCorpses(c, s, 1))
					{
						foreach (Minion m in c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => !m.IsDead && !m.ToBeDestroyed).ToList())
							Generic.DamageCharFunc((IPlayable)s, m, V("RLK_035", "damage", 1), true);
						if (!c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Any(m => !m.IsDead && !m.ToBeDestroyed)) break;
					}
				})
			}));
			// [RLK_042] Horn of Winter (0) — Refresh 2 Mana Crystals.
			cards.Add("CORE_RLK_042", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => c.UsedMana = System.Math.Max(0, c.UsedMana - V("RLK_042", "mana", 2)))
			}));
			// [RLK_051] Vampiric Blood (2) — Give your hero +5 Health. Spend 3 Corpses to gain 5 more and draw a card.
			cards.Add("CORE_RLK_051", new CardDef(new Power
			{
				CorpseCost = V("RLK_051", "corpses", 3), // spent on play (glows with enough Corpses)
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					// in the order of the text: +5, the spend, then 5 more and a draw
					int health = V("RLK_051", "health", 5);
					Give(g, s, c.Hero, "RLK_051e", health);
					if (Generic.SpendCorpses(c, s))
					{
						Give(g, s, c.Hero, "RLK_051e", health);
						Generic.Draw(c);
					}
				})
			}));
			cards.Add("RLK_051e", new CardDef(new Power { Enchant = Enchants.Enchants.AddHealthScriptTag })); // a raw HEALTH tag never reached the hero's Health
			// [RLK_122] The Scourge (9) — Fill your board with random Undead.
			cards.Add("CORE_RLK_122", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var undead = Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION && x.IsRace(Race.UNDEAD) && !x.NeverRandomlyOnBoard).ToList();
					for (int i = 0; i < Game.MAX_MINIONS_ON_BOARD && !c.BoardZone.IsFull && undead.Count > 0; i++)
						Summon(g, c, undead[g.Random.Next(undead.Count)], s);
				})
			}));
			// [RLK_504] Corpse Bride (5) — Battlecry: Spend up to 10 Corpses to summon a Risen Groom with Taunt and that much
			// Attack and Health.
			cards.Add("CORE_RLK_504", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.BoardZone.IsFull) return;
					int n = Generic.SpendCorpsesUpTo(c, s, V("RLK_504", "corpses", 10));
					if (n == 0) return;
					var groom = (Minion)Entity.FromCard(c, Cards.FromId("RLK_506t"));
					groom.AttackDamage = n;
					groom.BaseHealth = n;
					Generic.SummonBlock.Invoke(g, groom, ((Minion)s).ZonePosition + 1, s);
				})
			}));
			// [RLK_740] Might of Menethil (4) — Battlecry: Spend up to 3 Corpses. Freeze that many enemy minions.
			cards.Add("CORE_RLK_740", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = c.Opponent.BoardZone.GetAll().Where(m => !m.IsFrozen).ToList();
					int n = Generic.SpendCorpsesUpTo(c, s, System.Math.Min(V("RLK_740", "corpses", 3), enemies.Count));
					for (int i = 0; i < n; i++)
					{
						Minion m = enemies[g.Random.Next(enemies.Count)];
						enemies.Remove(m);
						m.IsFrozen = true;
					}
				})
			}));
			// [RLK_741] Soulstealer (8) — Battlecry: Destroy all other minions. Gain 1 Corpse for each enemy destroyed.
			cards.Add("CORE_RLK_741", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int enemies = c.Opponent.BoardZone.GetAll().Length;
					foreach (Minion m in c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => m.Id != s.Id).ToList()) m.Destroy();
					Generic.GainCorpses(c, s, enemies);
				})
			}));

			// ---------------------------------------------------------------- Druid

			// [BAR_535] Thickhide Kodo (4) — Taunt. Deathrattle: Gain 5 Armor.
			cards.Add("CORE_BAR_535", new CardDef(new Power { DeathrattleTask = new ArmorTask(V("BAR_535", "armor", 5)) }));
			// [BT_134] Bogbeam (3) — Deal 3 damage to a minion. Costs (0) if you have at least 7 Mana Crystals.
			cards.Add("CORE_BT_134", new CardDef(TargetMinion, new Power
			{
				PowerTask = new DamageTask(V("BT_134", "damage", 3), EntityType.TARGET, true),
				Aura = new AdaptiveCostEffect(p => p.Controller.BaseMana >= V("BT_134", "mana crystals", 7) ? p.Card.Cost : 0)
			}));
			// [CS3_012] Nordrassil Druid (4) — Battlecry: The next spell you cast this turn costs (3) less.
			cards.Add("CORE_CS3_012", new CardDef(new Power { PowerTask = new AddEnchantmentTask("CS3_012e", EntityType.CONTROLLER) }));
			cards.Add("CS3_012e", NextCardEffect(Effects.ReduceCost(V("CS3_012", "discount", 3)), SelfCondition.IsSpell, thisTurnOnly: true));
			// [DMF_057] Lunar Eclipse (2) — Deal 3 damage to a minion. Your next spell this turn costs (2) less.
			cards.Add("CORE_DMF_057", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("DMF_057", "damage", 3), EntityType.TARGET, true), new AddEnchantmentTask("DMF_057o", EntityType.CONTROLLER))
			}));
			cards.Add("DMF_057o", NextCardEffect(Effects.ReduceCost(V("DMF_057", "discount", 2)), SelfCondition.IsSpell, thisTurnOnly: true));
			// [DMF_058] Solar Eclipse (2) — The next spell you cast this turn casts twice.
			cards.Add("CORE_DMF_058", new CardDef(new Power { PowerTask = new AddEnchantmentTask("DMF_058o", EntityType.CONTROLLER) }));
			cards.Add("DMF_058o", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.AFTER_CAST)
					{
						TriggerSource = TriggerSource.FRIENDLY,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (!(s is Enchantment e) || !(t is Spell spell)) return;
							RemoveEnchantmentTask.Task.Process(e.Game, e.Controller, e, null); // reverts its effects too
							var copy = (Spell)Entity.FromCard(c, spell.Card);
							ICharacter target = spell.CardTarget > 0 && g.IdEntityDic.TryGetValue(spell.CardTarget, out IPlayable p) && p is ICharacter ch
							                    && ch.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY ? ch : null;
							if (target == null && copy.Card.MustHaveTargetToPlay) target = copy.GetRandomValidTarget();
							if (copy.Card.MustHaveTargetToPlay && target == null) return;
							Generic.CastSpell.Invoke(c, g, copy, target, 0);
						})
					},
					new Trigger(TriggerType.TURN_END) { SingleTask = RemoveEnchantmentTask.Task })
			}));
			// [DMF_060] Umbral Owl (7) — Rush. Costs (1) less for each spell you've cast this game.
			cards.Add("CORE_DMF_060", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.PlayHistory.Count(e => e.SourceController == p.Controller.PlayerId && e.SourceCard.Type == CardType.SPELL))
			}));
			// [DMF_733] Kiri, Chosen of Elune (3) — Battlecry: Add a Solar Eclipse and Lunar Eclipse to your hand.
			cards.Add("CORE_DMF_733", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					AddToHand(c, Cards.FromId("CORE_DMF_058"));
					AddToHand(c, Cards.FromId("CORE_DMF_057"));
				})
			}));
			// [DMF_734] Greybough (5) — Taunt. Deathrattle: Give a random friendly minion "Deathrattle: Summon Greybough."
			ISimpleTask greybud = ComplexTask.Create(
				new IncludeTask(EntityType.MINIONS),
				new FilterStackTask(SelfCondition.IsNotDead),
				new RandomTask(1, EntityType.STACK),
				new AddEnchantmentTask("DMF_734e", EntityType.STACK));
			cards.Add("CORE_DMF_734", new CardDef(new Power { DeathrattleTask = greybud }));
			cards.Add("DMF_734e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("CORE_DMF_734"), s))
			}));

			// ---------------------------------------------------------------- Hunter

			// [AV_226] Ice Trap (2) — Secret: When your opponent casts a spell, return it to their hand instead. It costs (1) more.
			cards.Add("CORE_AV_226", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.CAST_SPELL)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = ComplexTask.Secret(
						new CounterTask(EntityType.TARGET),
						new CustomTask((g, c, s, t, stack) =>
						{
							if (t == null || c.Opponent.HandZone.IsFull) return;
							IPlayable back = Entity.FromCard(c.Opponent, t.Card);
							Generic.AddHandPhase.Invoke(c.Opponent, back);
							Give(g, s, back, "AV_226e");
						}))
				}
			}));
			cards.Add("AV_226e", new CardDef(new Power { Enchant = new Enchant(Effects.AddCost(V("AV_226", "cost", 1))) }));
			// [BAR_551] Barak Kodobane (5) — Battlecry: Draw a 1, 2, and 3-Cost spell.
			cards.Add("CORE_BAR_551", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int cost = 1; cost <= 3; cost++) { int wanted = cost; DrawWhere(g, c, p => p is Spell && p.Cost == wanted); }
				})
			}));
			// [DED_009] Doggie Biscuit (2) — Tradeable. Give a minion +2/+3. After you Trade this, give a friendly minion Rush.
			cards.Add("CORE_DED_009", new CardDef(TargetMinion, new Power
			{
				PowerTask = new AddEnchantmentTask("DED_009e", EntityType.TARGET),
				TradeTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.BoardZone.GetAll().Where(m => !m.IsRush).ToList();
					if (minions.Count > 0) minions[g.Random.Next(minions.Count)].IsRush = true;
				})
			}));
			cards.Add("DED_009e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("DED_009", "attack", 2)), Effects.Health_N(V("DED_009", "health", 3))) }));

			// ---------------------------------------------------------------- Paladin

			// [BT_018] Underlight Angling Rod (3) — After your Hero attacks, add a random Murloc to your hand.
			cards.Add("CORE_BT_018", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.MURLOC)) is Card card) AddToHand(c, card);
					})
				}
			}));
			// [BT_334] Lady Liadrin (7) — Battlecry: Add a copy of each spell you cast on friendly characters this game to your hand.
			cards.Add("CORE_BT_334", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (PlayHistoryEntry e in c.PlayHistory.Where(e => e.SourceController == c.PlayerId && e.TargetController == c.PlayerId
					                                                    && e.SourceCard.Type == CardType.SPELL).ToList())
					{
						if (c.HandZone.IsFull) break;
						AddToHand(c, e.SourceCard);
					}
				})
			}));
			// [CS3_016] Reckoning (1) — Secret: After an enemy minion deals 3 or more damage, destroy it.
			cards.Add("CORE_CS3_016", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEAL_DAMAGE)
				{
					TriggerSource = TriggerSource.OP_MINIONS,
					Condition = new SelfCondition(p => p.Game.CurrentEventData?.EventNumber >= 3),
					SingleTask = ComplexTask.Secret(new DestroyTask(EntityType.EVENT_SOURCE))
				}
			}));
			// [CS3_029] Pursuit of Justice (2) — Give +1 Attack to Silver Hand Recruits you summon this game.
			cards.Add("CORE_CS3_029", new CardDef(new Power { PowerTask = new AddEnchantmentTask("CS3_029e", EntityType.CONTROLLER) }));
			cards.Add("CS3_029e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.BOARD, "CS3_029e2") { Condition = new SelfCondition(IsRecruit) }
			}));
			cards.Add("CS3_029e2", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("CS3_029", "attack", 1))) }));
			// [DMF_238] Hammer of the Naaru (6) — Battlecry: Summon a 6/6 Holy Elemental with Taunt.
			cards.Add("CORE_DMF_238", new CardDef(new Power { PowerTask = new SummonTask("DMF_238t", SummonSide.DEFAULT) }));
			// [DMF_240] Lothraxion the Redeemed (5) — Battlecry: For the rest of the game, after you summon a Silver Hand Recruit,
			// give it Divine Shield.
			cards.Add("CORE_DMF_240", new CardDef(new Power { PowerTask = new AddEnchantmentTask("DMF_240e", EntityType.CONTROLLER) }));
			cards.Add("DMF_240e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(IsRecruit),
					SingleTask = new SetGameTagTask(GameTag.DIVINE_SHIELD, 1, EntityType.TARGET)
				}
			}));
			// [RLK_919] Anachronos (7) — Battlecry: Send all other minions 2 turns into the future. (They return at the start of
			// your next turn.)
			cards.Add("CORE_RLK_919", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => m.Id != s.Id).ToList())
					{
						m.Zone.Remove(m);
						m.Controller.SetasideZone.Add(m);
						m[HS_SCRIPT_B] = s.Id; // sent by this Anachronos
					}
					Generic.AddEnchantmentBlock(g, Cards.FromId("RLK_919e"), (IPlayable)s, c, s.Id, 0, 0);
				})
			}));
			cards.Add("RLK_919e", new CardDef(new Power
			{
				Enchant = new Enchant(GameTag.TAG_SCRIPT_DATA_NUM_1, EffectOperator.SET),
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = ComplexTask.Create(
						new CustomTask((g, c, s, t, stack) =>
						{
							if (!(s is Enchantment e)) return;
							int sender = e[GameTag.TAG_SCRIPT_DATA_NUM_1];
							foreach (Controller p in new[] { c, c.Opponent })
								foreach (IPlayable x in p.SetasideZone.Where(x => x is Minion && x[HS_SCRIPT_B] == sender).ToList())
								{
									x[HS_SCRIPT_B] = 0;
									p.SetasideZone.Remove(x);
									if (!p.BoardZone.IsFull) Generic.SummonBlock.Invoke(g, (Minion)x, -1, e);
								}
						}),
						RemoveEnchantmentTask.Task)
				}
			}));

			// ---------------------------------------------------------------- Priest

			// [AV_328] Spirit Guide (5) — Taunt. Deathrattle: Draw a Holy spell and a Shadow spell.
			cards.Add("CORE_AV_328", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					DrawWhere(g, c, p => p is Spell && p.Card[SPELL_SCHOOL] == HOLY);
					DrawWhere(g, c, p => p is Spell && p.Card[SPELL_SCHOOL] == SHADOW);
				})
			}));
			// [CS3_014] Crimson Clergy (1) — Overheal: Draw a card.
			cards.Add("CORE_CS3_014", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.OVERHEAL) { TriggerSource = TriggerSource.SELF, SingleTask = new DrawTask() }
			}));
			// [CS3_027] Focused Will (1) — Silence a minion, then give it +3 Health.
			cards.Add("CORE_CS3_027", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new SilenceTask(EntityType.TARGET), new AddEnchantmentTask("CS3_027e", EntityType.TARGET))
			}));
			cards.Add("CS3_027e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("CS3_027", "health", 3))) }));
			// [EX1_194] Power Infusion (4) — Give a minion +2/+6.
			cards.Add("CORE_EX1_194", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("EX1_194e", EntityType.TARGET) }));
			cards.Add("EX1_194e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("EX1_194", "attack", 2)), Effects.Health_N(V("EX1_194", "health", 6))) }));
			// [EX1_195] Kul Tiran Chaplain (2) — Battlecry: Give a friendly minion +2 Health.
			cards.Add("CORE_EX1_195", new CardDef(BattlecryFriendlyMinion, new Power { PowerTask = new AddEnchantmentTask("EX1_195e", EntityType.TARGET) }));
			cards.Add("EX1_195e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("EX1_195", "health", 2))) }));
			// [SW_441] Shard of the Naaru (1) — Tradeable. Silence all enemy minions.
			cards.Add("CORE_SW_441", new CardDef(new Power { PowerTask = new SilenceTask(EntityType.OP_MINIONS) }));
			// [SW_448] Darkbishop Benedictus (5) — Start of Game: If the spells in your deck are all Shadow, enter Shadowform.
			cards.Add("CORE_SW_448", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.GAME_START)
				{
					TriggerActivation = TriggerActivation.DECK,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						// HsServer (2026-10-08): the deck it started with — Start_of_Game.txt: "triggers before the mulligan … nothing the player does during the mulligan can affect whether or not any Start of Game effects will trigger" (our trigger resolves after the opening draw)
						if (c.DeckCards.Where(x => x.Type == CardType.SPELL).All(x => x[SPELL_SCHOOL] == SHADOW))
							new ReplaceHeroPower(Cards.FromId("EX1_625t")).Process(g, c, s, null);
					}),
					RemoveAfterTriggered = true
				}
			}));
			// [WON_065] Ship's Chirurgeon (1) — After you summon a minion, give it +1 Health.
			cards.Add("CORE_WON_065", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON) { TriggerSource = TriggerSource.MINIONS_EXCEPT_SELF, SingleTask = new AddEnchantmentTask("WON_065e", EntityType.TARGET) }
			}));
			cards.Add("WON_065e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("WON_065", "health", 1))) }));

			// ---------------------------------------------------------------- Rogue

			// [BT_188] Shadowjeweler Hanar (2) — After you play a Secret, Discover a Secret from a different class.
			cards.Add("CORE_BT_188", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = SelfCondition.IsSecret,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var offer = Pick(g, Cards.GenerationPool(g, c).Where(x => x.IsSecret && x.Class != (t?.Card.Class ?? c.HeroClass)), 3);
						if (offer.Count > 0 && !c.HandZone.IsFull)
							Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.HAND, offer.ToArray(), null);
					})
				}
			}));
			// [BT_707] Ambush (2) — Secret: After your opponent plays a minion, summon a 2/3 Ambusher with Poisonous.
			cards.Add("CORE_BT_707", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = ComplexTask.Secret(new SummonTask("BT_707t", SummonSide.SPELL))
				}
			}));
			// [CS3_005] Vanessa VanCleef (2) — Combo: Add a copy of the last card your opponent played to your hand.
			cards.Add("CORE_CS3_005", new CardDef(new Power
			{
				ComboTask = new CustomTask((g, c, s, t, stack) =>
				{
					var last = c.Opponent.PlayHistory.LastOrDefault(e => e.SourceController == c.Opponent.PlayerId);
					if (last.SourceCard != null) AddToHand(c, last.SourceCard);
				})
			}));
			// [SCH_706] Plagiarize (2) — Secret: At the end of your opponent's turn, add copies of the cards they played to your hand.
			cards.Add("CORE_SCH_706", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					EitherTurn = true,
					Condition = new SelfCondition(p => p.Game.CurrentPlayer == p.Controller.Opponent && p.Controller.Opponent.CardsPlayedThisTurn.Count > 0),
					SingleTask = ComplexTask.Secret(new CustomTask((g, c, s, t, stack) =>
					{
						foreach (Card card in c.Opponent.CardsPlayedThisTurn.ToList()) AddToHand(c, card);
					}))
				}
			}));

			// ---------------------------------------------------------------- Shaman

			// [BT_115] Marshspawn (3) — Battlecry: If you cast a spell last turn, Discover a spell.
			cards.Add("CORE_BT_115", new CardDef(new Power
			{
				PowerTask = ComplexTask.Conditional(SelfCondition.CastSpellLastTurn, new DiscoverTask(CardType.SPELL))
			}));
			// [DMF_709] Grand Totem Eys'or (3) — At the end of your turn, give +1/+1 to all other Totems in your hand, deck and
			// battlefield.
			cards.Add("CORE_DMF_709", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
						GiveAll(g, s, c.BoardZone.GetAll().Cast<IPlayable>().Concat(c.HandZone).Concat(c.DeckZone)
							.Where(p => p.Id != s.Id && p is Minion m && m.IsRace(Race.TOTEM)), "DMF_709e"))
				}
			}));
			cards.Add("DMF_709e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DMF_709", "stats", 1))) }));
			// [SW_114] Overdraft (1) — Tradeable. Unlock your Overloaded Mana Crystals to deal that much damage.
			cards.Add("CORE_SW_114", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					// (Generic.UnlockOverload — rule book: unlocking sets OVERLOAD_OWED and OVERLOAD_LOCKED to 0; "that much" = both,
					// as live Thorim draws for both)
					int locked = Generic.UnlockOverload(c);
					if (locked > 0 && t is ICharacter target) Generic.DamageCharFunc((IPlayable)s, target, locked, true);
				})
			}));

			// ---------------------------------------------------------------- Warlock

			// [BT_304] Enhanced Dreadlord (8) — Taunt. Deathrattle: Summon a 5/5 Dreadlord with Lifesteal.
			cards.Add("CORE_BT_304", new CardDef(new Power { DeathrattleTask = new SummonTask("BT_304t", SummonSide.DEATHRATTLE) }));
			// [CS3_002] Ritual of Doom (0) — Destroy a friendly minion. If you had 5 or more, summon a 5/5 Demon.
			cards.Add("CORE_CS3_002", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } },
				new Power
				{
					PowerTask = ComplexTask.If((g, c, s) => c.BoardZone.GetAll().Length >= V("CS3_002", "minions", 5),
						new CustomTask((g, c, s, t, stack) =>
						{
							if (t is Minion m) Generic.Destroy(m, s);
							g.DeathProcessingAndAuraUpdate();
							Summon(g, c, Cards.FromId("CS3_002t"), s);
						}),
						new CustomTask((g, c, s, t, stack) => { if (t is Minion m) Generic.Destroy(m, s); }))
				}));
			// [CS3_003] Felsoul Jailer (5) — Battlecry: Your opponent discards a minion. Deathrattle: Return it.
			cards.Add("CORE_CS3_003", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.Opponent.HandZone.Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.Count == 0) return;
					IPlayable discarded = minions[g.Random.Next(minions.Count)];
					s[GameTag.TAG_SCRIPT_DATA_NUM_1] = discarded.Card.AssetId;
					// its fel fire at the discarded card (unrecorded placement)
					Generic.NamedFx((IPlayable)s, "FelFire", new[] { discarded.Id }, () => Generic.DiscardBlock.Invoke(c.Opponent, discarded));
				}),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					int assetId = s[GameTag.TAG_SCRIPT_DATA_NUM_1];
					if (assetId > 0) AddToHand(c.Opponent, Cards.FromAssetId(assetId));
				})
			}));
			// [FIX_SW_084] Bloodbound Imp (2) — Whenever this attacks, deal 2 damage to your hero.
			cards.Add("CORE_FIX_SW_084", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK) { TriggerSource = TriggerSource.SELF, SingleTask = new DamageTask(V("FIX_SW_084", "damage", 2), EntityType.HERO) }
			}));
			// [SCH_158] Demonic Studies (1) — Discover a Demon. Your next one costs (1) less.
			cards.Add("CORE_SCH_158", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new DiscoverTask(DiscoverType.DEMON), new AddEnchantmentTask("SCH_158e", EntityType.CONTROLLER))
			}));
			cards.Add("SCH_158e", NextCardEffect(Effects.ReduceCost(V("SCH_158", "discount", 1)), SelfCondition.IsRace(Race.DEMON), thisTurnOnly: false));
			// [SW_085] Dark Alley Pact (4) — Summon a Fiend with Taunt and stats equal to your hand size.
			cards.Add("CORE_SW_085", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = c.HandZone.Count;
					if (n == 0 || c.BoardZone.IsFull) return;
					var fiend = (Minion)Entity.FromCard(c, Cards.FromId("SW_085t"));
					fiend.AttackDamage = n;
					fiend.BaseHealth = n;
					Generic.SummonBlock.Invoke(g, fiend, -1, s);
				})
			}));

			// ---------------------------------------------------------------- Warrior

			// [BT_117] Bladestorm (2) — Deal 1 damage to all minions. Repeat until one dies.
			cards.Add("CORE_BT_117", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int round = 0; round < 40; round++)
					{
						var minions = c.BoardZone.GetAll().Concat(c.Opponent.BoardZone.GetAll()).Where(m => !m.IsDead && !m.ToBeDestroyed).ToList();
						if (minions.Count == 0) break;
						foreach (Minion m in minions) Generic.DamageCharFunc((IPlayable)s, m, V("BT_117", "damage", 1), true);
						if (minions.Any(m => m.IsDead || m.ToBeDestroyed)) break;
					}
				})
			}));
			// [CS3_008] Bloodsail Deckhand (1) — Battlecry: The next weapon you play costs (1) less.
			cards.Add("CORE_CS3_008", new CardDef(new Power { PowerTask = new AddEnchantmentTask("CS3_008e", EntityType.CONTROLLER) }));
			cards.Add("CS3_008e", NextCardEffect(Effects.ReduceCost(V("CS3_008", "discount", 1)), SelfCondition.IsWeapon, thisTurnOnly: false));
			// [CS3_009] War Cache (3) — Add a random Warrior minion, spell, and weapon to your hand.
			cards.Add("CORE_CS3_009", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (CardType type in new[] { CardType.MINION, CardType.SPELL, CardType.WEAPON })
						if (RandomCard(g, x => x.HasClass(CardClass.WARRIOR) && x.Type == type) is Card card) AddToHand(c, card);
				})
			}));
			// [DMF_521] Sword Eater (4) — Taunt. Battlecry: Equip a 3/2 Sword.
			cards.Add("CORE_DMF_521", new CardDef(new Power { PowerTask = new WeaponTask("DMF_521t") }));
			// [DMF_522] Minefield (2) — Deal 5 damage randomly split among all minions.
			cards.Add("CORE_DMF_522", new CardDef(new Power
			{
				PowerTask = new EnqueueTask(V("DMF_522", "damage", 5), ComplexTask.DamageRandomTargets(1, EntityType.ALLMINIONS, 1), true)
			}));
			// [ONY_024] Onyxian Drake (4) — Taunt. Battlecry: Deal damage equal to your Armor to an enemy minion.
			cards.Add("CORE_ONY_024", new CardDef(BattlecryEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter target && c.Hero.Armor > 0) Generic.DamageCharFunc((IPlayable)s, target, c.Hero.Armor, false);
				})
			}));
			// [SW_030] Cargo Guard (3) — At the end of your turn, gain 3 Armor.
			cards.Add("CORE_SW_030", new CardDef(new Power { Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new ArmorTask(V("SW_030", "armor", 3)) } }));
			// [SW_094] Heavy Plate (3) — Tradeable. Gain 8 Armor.
			cards.Add("CORE_SW_094", new CardDef(new Power { PowerTask = new ArmorTask(V("SW_094", "armor", 8)) }));
			// [SW_107] Fire Sale (4) — Tradeable. Deal 3 damage to all minions.
			cards.Add("CORE_SW_107", new CardDef(new Power { PowerTask = new DamageTask(V("SW_107", "damage", 3), EntityType.ALLMINIONS, true) }));
			// [CS3_021] Enslaved Fel Lord (7) — Taunt. Also damages the minions next to whomever this attacks.
			cards.Add("CORE_CS3_021", new CardDef(new Power { Trigger = Cleave() }));
			// [SW_431] Park Panther (4) — Rush. Whenever this attacks, give your hero +3 Attack this turn.
			cards.Add("CORE_SW_431", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK) { TriggerSource = TriggerSource.SELF, SingleTask = new AddEnchantmentTask("SW_431e", EntityType.HERO) }
			}));
			cards.Add("SW_431e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("SW_431", "attack", 3))) { IsOneTurnEffect = true } }));
			// [DMF_194] Redscale Dragontamer (2) — Deathrattle: Draw a Dragon.
			cards.Add("CORE_DMF_194", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p is Minion m && m.IsRace(Race.DRAGON)))
			}));
		}
	}
}
