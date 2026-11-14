// HsServer addition (not upstream SabberStone): The Dalaran Heist — chunk 4: the treasures and passives (the complete pool: the wiki's
// The_Dalaran_Heist page lists 16 new + 13 returning passives and 29 treasures by dbf id; Server/data/heist/treasures.json), the player
// heroes' Hero Powers (DALA_<Class>_HP1/HP2) and the chapter twists (DALA_829-833). Shared pieces: Actions/DalaranHeistGroup4.cs —
// passives start set aside and act at the start of the game (Generic.PlayDungeonPassives), twists are played as the game starts
// (GameConfig.Player1PlotTwist → Generic.PlayPlotTwist), dummy effect cards, the Stench's swap, "cards cost half", Fatigue immunity,
// remembered cards. Every number goes through CardValues (V). Live block shapes: the decoded Heist games (scratchpad boss-games).
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
		/// <summary>The Dalaran Heist's treasure pool (wiki The_Dalaran_Heist, Treasures: 29 dbf ids — "selected from one of three random
		/// cards from the pool of Treasure cards" after rounds 3 and 7): what "a random treasure" picks (The Box, Untold Splendor).</summary>
		internal static readonly string[] HeistTreasurePool =
		{
			"DALA_700", "DALA_701", "DALA_702", "DALA_703", "DALA_704", "DALA_705", "DALA_706", "DALA_707", "DALA_708", "DALA_709",
			"DALA_711", "DALA_712", "DALA_713", "DALA_714", "DALA_715", "DALA_716", "DALA_717", "DALA_718", "DALA_719", "DALA_720",
			"DALA_721", "DALA_722", "DALA_723", "DALA_724", "DALA_725", "DALA_726", "DALA_727", "DALA_729", "DALA_730",
		};

		/// <summary>A random Heist treasure, never the card generating it.</summary>
		private static Card HeistRandomTreasure(Game g, IEntity source)
		{
			string self = (source as IPlayable)?.Card.Id ?? (source as Enchantment)?.Creator?.Card.Id;
			var pool = HeistTreasurePool.Where(id => id != self).Select(Cards.TryFromId).Where(x => x != null).ToList();
			return pool.Count > 0 ? pool[g.Random.Next(pool.Count)] : null;
		}


		/// <summary>A player enchantment holding an aura (the passives' "… Player Enchantment": LOOTA_828e2, DALA_733e, GILA_813e…).</summary>
		private static CardDef HeistPlayerAura(IAura aura) => new CardDef(new Power { Aura = aura });

		static partial void HeistTreasures(IDictionary<string, CardDef> cards)
		{
			HeistHeroPowers(cards);
			HeistPassives(cards);
			HeistTreasureCards(cards);
			HeistTwists(cards);
		}

		// =============================================================================================================== Hero Powers

		private static void HeistHeroPowers(IDictionary<string, CardDef> cards)
		{
			var friendlyMinion = new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 } };

			// [DALA_Druid_HP1] Lifebloom (2) — Restore a minion to full Health.
			cards.Add("DALA_Druid_HP1", new CardDef(TargetMinion, new Power { PowerTask = new HealFullTask(EntityType.TARGET) }));
			// [DALA_Druid_HP2] Touch of Bark (2) — Give a minion +1/+1. (DALA_Druid_HP2e "Barky")
			cards.Add("DALA_Druid_HP2", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("DALA_Druid_HP2e", EntityType.TARGET) }));
			cards.Add("DALA_Druid_HP2e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DALA_Druid_HP2", "stats", 1))) }));

			// [DALA_Hunter_HP1] Opportunist (2) — Give a minion +2 Attack this turn. (DALA_Hunter_HP1e: TAG_ONE_TURN_EFFECT)
			cards.Add("DALA_Hunter_HP1", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("DALA_Hunter_HP1e", EntityType.TARGET) }));
			cards.Add("DALA_Hunter_HP1e", OneTurn(Effects.Attack_N(V("DALA_Hunter_HP1", "attack", 2))));
			// [DALA_Hunter_HP2] Pet Training (2) — Add a 1/1 Shifting Chameleon to your hand.
			// [DALA_Hunter_HP2t] Shifting Chameleon — Each turn this is in your hand, transform into a random 1-cost minion. The shared
			// in-hand shift (Generic.ShiftInHand, like Shifter Zerus OG_123: Advanced_rulebook.txt "each turn this is in your hand"), its
			// shifting enchantment DALA_Hunter_HP2e "Shifting: Transforming into a random 1-cost minion".
			cards.Add("DALA_Hunter_HP2", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_HAND_NOT_FULL, 0 } },
				new Power { PowerTask = new AddCardTo("DALA_Hunter_HP2t", EntityType.HAND) }));
			ISimpleTask chameleon = new CustomTask((g, c, s, t, stack) => Generic.ShiftInHand(s,
				me => RandomCard(g, x => x.Type == CardType.MINION && x.Cost == V("DALA_Hunter_HP2t", "cost", 1)), Cards.FromId("DALA_Hunter_HP2e")));
			cards.Add("DALA_Hunter_HP2t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START) { TriggerActivation = TriggerActivation.HAND, SingleTask = chameleon }
			}));
			cards.Add("DALA_Hunter_HP2e", new CardDef(new Power
			{
				Enchant = new Enchant(GameTag.SHIFTING, EffectOperator.SET, 1) { RemoveWhenPlayed = true },
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = chameleon }
			}));

			// [DALA_Mage_HP1] Burning Wit (1) — Reduce the Cost of a random card in your hand by (2). (DALA_Mage_HP1e "Witty"; live 34
			// uses, e.g. live-session-20260929-122856)
			cards.Add("DALA_Mage_HP1", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.HandZone.Count == 0) return;
					Give(g, s, c.HandZone[g.Random.Next(c.HandZone.Count)], "DALA_Mage_HP1e");
				})
			}));
			cards.Add("DALA_Mage_HP1e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("DALA_Mage_HP1", "discount", 2))) }));
			// [DALA_Mage_HP2] Frostburn (2) — Freeze a character. If it's already Frozen, deal 2 damage.
			cards.Add("DALA_Mage_HP2", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is ICharacter target)) return;
					if (target.IsFrozen)
						Generic.DamageCharFunc((IPlayable)s, target, V("DALA_Mage_HP2", "damage", 2), false);
					else
						ComplexTask.Freeze(EntityType.TARGET).Process(g, c, s, t);
				})
			}));

			// [DALA_Paladin_HP1] Backup (2) — Add three 1/1 Silver Hand Recruits to your hand.
			cards.Add("DALA_Paladin_HP1", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_HAND_NOT_FULL, 0 } },
				new Power { PowerTask = new AddCardTo("CS2_101t", EntityType.HAND, V("DALA_Paladin_HP1", "recruits", 3)) }));
			// [DALA_Paladin_HP2] Boon of Light (2) — Give a friendly minion Divine Shield.
			cards.Add("DALA_Paladin_HP2", new CardDef(friendlyMinion, new Power { PowerTask = new SetGameTagTask(GameTag.DIVINE_SHIELD, 1, EntityType.TARGET) }));

			// [DALA_Priest_HP1] Distort (2) — Swap a minion's Attack and Health. (DALA_Priest_HP1e "Distorted")
			cards.Add("DALA_Priest_HP1", new CardDef(TargetMinion, new Power { PowerTask = new SwapAttackHealthTask(EntityType.TARGET, "DALA_Priest_HP1e") }));
			// [DALA_Priest_HP2] Soothe (2) — Give a minion -2 Attack until your next turn. (DALA_Priest_HP2e "Soothed": its owner's next
			// turn start removes it, like EX1_196e)
			cards.Add("DALA_Priest_HP2", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("DALA_Priest_HP2e", EntityType.TARGET) }));
			cards.Add("DALA_Priest_HP2e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(-V("DALA_Priest_HP2", "attack", 2))),
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = RemoveEnchantmentTask.Task }
			}));

			// [DALA_Rogue_HP1] Yoink! (2) — Add a random card to your hand (from another class).
			cards.Add("DALA_Rogue_HP1", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_HAND_NOT_FULL, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var pool = OtherClassCards(g, c).ToList();
					if (pool.Count > 0) AddToHand(c, pool[g.Random.Next(pool.Count)]);
				})
			}));
			// [DALA_Rogue_HP2] Cut-less (2) — Deal 2 damage to an undamaged minion.
			cards.Add("DALA_Rogue_HP2", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_UNDAMAGED_TARGET, 0 } },
				new Power { PowerTask = new DamageTask(V("DALA_Rogue_HP2", "damage", 2), EntityType.TARGET) }));

			// [DALA_Shaman_HP1] Evolution (2) — Transform a friendly minion into one that costs (1) more (the Evolve rule: a random
			// minion of its Cost + 1; none of that Cost: nothing).
			cards.Add("DALA_Shaman_HP1", new CardDef(friendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m) || m.Zone?.Type != Zone.PLAY) return;
					Card into = RandomMinionOfCost(g, m.Cost + V("DALA_Shaman_HP1", "more", 1));
					if (into != null) new TransformTask(into, EntityType.TARGET).Process(g, c, s, t);
				})
			}));
			// [DALA_Shaman_HP2] Refresh (2) — Draw a card. Overload: (1). (Overload from the client text: the engine's)
			cards.Add("DALA_Shaman_HP2", new CardDef(new Power { PowerTask = new DrawTask() }));

			// [DALA_Warlock_HP1] Pain Split (2) — Take 2 damage. Deal 2 damage.
			cards.Add("DALA_Warlock_HP1", new CardDef(TargetAny, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("DALA_Warlock_HP1", "self", 2), EntityType.HERO),
					new DamageTask(V("DALA_Warlock_HP1", "damage", 2), EntityType.TARGET))
			}));
			// [DALA_Warlock_HP2] The Pact (2) — Take 2 damage. Summon a 2/2 Imp. (DALA_Warlock_HP2t "Devious Imp")
			cards.Add("DALA_Warlock_HP2", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("DALA_Warlock_HP2", "self", 2), EntityType.HERO), new SummonTask("DALA_Warlock_HP2t"))
			}));

			// [DALA_Warrior_HP1] Invigorating Brew (2) — Deal 1 damage to a minion and give it +2 Attack. (DALA_Warrior_HP1e "Invigorated")
			cards.Add("DALA_Warrior_HP1", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("DALA_Warrior_HP1", "damage", 1), EntityType.TARGET),
					new AddEnchantmentTask("DALA_Warrior_HP1e", EntityType.TARGET))
			}));
			cards.Add("DALA_Warrior_HP1e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("DALA_Warrior_HP1", "attack", 2))) }));
			// [DALA_Warrior_HP2] Undermine (2) — Shuffle two Explosives into your opponent's deck.
			// [DALA_Warrior_HP2t] Improvised Explosive — Casts When Drawn. Take 3 damage. (its drawer's hero)
			cards.Add("DALA_Warrior_HP2", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("DALA_Warrior_HP2", "explosives", 2) && !c.Opponent.DeckZone.IsFull; i++)
						Generic.ShuffleIntoDeck.Invoke(c.Opponent, s, Entity.FromCard(c.Opponent, Cards.FromId("DALA_Warrior_HP2t"),
							new EntityData { [GameTag.CREATOR] = s.Id }));
				})
			}));
			ISimpleTask explode = new DamageTask(V("DALA_Warrior_HP2t", "damage", 3), EntityType.HERO);
			cards.Add("DALA_Warrior_HP2t", new CardDef(new Power { PowerTask = explode, TopdeckTask = explode }));
		}

		// =============================================================================================================== passives

		private static void HeistPassives(IDictionary<string, CardDef> cards)
		{
			// Passives act from the start of the game (Generic.PlayDungeonPassives): a copy is cast and its effect is usually a player
			// enchantment (live: "… Player Enchantment" ATTACHED to the player); the set-aside original only marks the deck list.

			// --------------------------------------------------------------------------------------- returning (Dungeon Run / Monster Hunt)

			// [LOOTA_800] Potion of Vitality — Double your starting Health. Live (heist-session-20260929-122856 ~23940): LOOTA_800e "+@
			// Health" on the hero with TAG_SCRIPT_DATA_NUM_1 = its Health then (40 → 80).
			cards.Add("LOOTA_800", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Give(g, s, c.Hero, "LOOTA_800e", c.Hero.Health))
			}));
			cards.Add("LOOTA_800e", new CardDef(new Power { Enchant = Enchants.Enchants.AddHealthScriptTag }));
			// [LOOTA_801] Crystal Gem — Start with an extra Mana Crystal. Live (heist-session-20260929-114419): RESOURCES 1 on the player.
			cards.Add("LOOTA_801", new CardDef(new Power { PowerTask = new ManaCrystalFullTask(V("LOOTA_801", "crystals", 1)) }));
			// [LOOTA_803] Scepter of Summoning — Your minions that cost (5) or more cost (5). (LOOTA_803e on the player)
			cards.Add("LOOTA_803", new CardDef(new Power { PowerTask = new AddEnchantmentTask("LOOTA_803e", EntityType.CONTROLLER) }));
			cards.Add("LOOTA_803e", HeistPlayerAura(new Aura(AuraType.HAND, Effects.SetCost(V("LOOTA_803", "cost", 5)))
			{
				Condition = new SelfCondition(p => p is Minion && p.Card.Cost >= V("LOOTA_803", "cost", 5))
			}));
			// [LOOTA_804] Small Backpacks — At the start of the game, draw 2 cards.
			cards.Add("LOOTA_804", new CardDef(new Power { PowerTask = new DrawTask(V("LOOTA_804", "cards", 2)) }));
			// [LOOTA_825] Robe of the Magi — Spell Damage +3. (LOOTA_825e "+3 Spell Damage" on the player)
			cards.Add("LOOTA_825", new CardDef(new Power { PowerTask = new AddEnchantmentTask("LOOTA_825e", EntityType.CONTROLLER) }));
			cards.Add("LOOTA_825e", HeistPlayerAura(new Aura(AuraType.CONTROLLER, new Effect(GameTag.SPELLPOWER, EffectOperator.ADD, V("LOOTA_825", "spell_damage", 3)))));
			// [LOOTA_828] Captured Flag — Your minions have +1/+1. Live (live-session-20261001-162435 6227): LOOTA_828e2 (AURA) on the
			// player puts LOOTA_828e "+1/+1" on each of the player's minions (hand, deck, board; ours: hand and board — a deck card's
			// stats aren't seen, it gets the buff as it is drawn).
			cards.Add("LOOTA_828", new CardDef(new Power { PowerTask = new AddEnchantmentTask("LOOTA_828e2", EntityType.CONTROLLER) }));
			cards.Add("LOOTA_828e2", HeistPlayerAura(new Aura(AuraType.HAND_AND_BOARD, "LOOTA_828e") { Condition = SelfCondition.IsMinion }));
			cards.Add("LOOTA_828e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("LOOTA_828", "stats", 1))) }));
			// [LOOTA_831] Glyph of Warding — Enemy minions cost (1) more. (LOOTA_831e on the player — live heist-session-20260929-122856)
			cards.Add("LOOTA_831", new CardDef(new Power { PowerTask = new AddEnchantmentTask("LOOTA_831e", EntityType.CONTROLLER) }));
			cards.Add("LOOTA_831e", HeistPlayerAura(new Aura(AuraType.OP_HAND, Effects.AddCost(V("LOOTA_831", "more", 1))) { Condition = SelfCondition.IsMinion }));
			// [LOOTA_832] Cloak of Invisibility — Your minions have permanent Stealth. (LOOTA_832e2 on the player gives each friendly minion
			// entering the board LOOTA_832e "Invisible: Has Stealth" — Stealth that attacking doesn't break: HsTag.PERMANENT_STEALTH, the
			// shared rule of Akama Prime BT_713t)
			ISimpleTask cloak = new CustomTask((g, c, s, t, stack) =>
			{
				if (t is Minion m && m.Zone?.Type == Zone.PLAY && !(m.AppliedEnchantments?.Any(e => e.Card.Id == "LOOTA_832e") ?? false))
					Give(g, s, m, "LOOTA_832e");
			});
			cards.Add("LOOTA_832", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Generic.AddEnchantmentBlock(g, Cards.FromId("LOOTA_832e2"), (IPlayable)s, c, 0, 0, 0);
					foreach (Minion m in c.BoardZone.GetAll().ToList()) cloak.Process(g, c, s, m);
				})
			}));
			cards.Add("LOOTA_832e2", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.SUMMON) { TriggerSource = TriggerSource.MINIONS, EitherTurn = true, SingleTask = cloak }
			}));
			cards.Add("LOOTA_832e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.STEALTH, EffectOperator.SET, 1), new Effect(HsTag.PERMANENT_STEALTH, EffectOperator.SET, 1))
			}));
			// [LOOTA_833] Mysterious Tome — At the start of the game, play 3 random Secrets. (any class: the text names none; put into play
			// — Generic.PutSecretIntoPlay; a full Secret zone or a duplicate: skipped)
			cards.Add("LOOTA_833", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var pool = Cards.GenerationPool(g, c).Where(x => x.IsSecret && !x.IsSigil).ToList();
					foreach (Card secret in DiscoverFromTask.PickDistinct(g, pool, V("LOOTA_833", "secrets", 3)))
						if (Generic.CanHoldSecret(c, secret))
							Generic.PutSecretIntoPlay(c, (Spell)Entity.FromCard(c, secret, new EntityData { [GameTag.CREATOR] = s.Id }));
				})
			}));
			// [LOOTA_845] Totem of the Dead — Your Deathrattles trigger twice. [LOOTA_846] Battle Totem — Your Battlecries trigger twice.
			// Live (Battle Totem: live-session-20261004-224614; heist-session RecordedBlockMeta 9/9): no enchantment — the player's
			// EXTRA_BATTLECRIES_BASE becomes 1 in the passive's POWER block. Totem of the Dead the same with EXTRA_DEATHRATTLES_BASE
			// (unrecorded). The player's rule for the rest of the game (Controller.BattlecryRuns / DeathrattleTimes read these).
			cards.Add("LOOTA_845", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => c.ControllerAuraEffects[GameTag.EXTRA_DEATHRATTLES_BASE] += 1)
			}));
			cards.Add("LOOTA_846", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => c.ControllerAuraEffects[GameTag.EXTRA_BATTLECRIES_BASE] += 1)
			}));
			// [GILA_506] First Aid Kit — At the end of your turn, restore 2 Health to all friendly characters. (GILA_506e on the player;
			// live live-session-20261008-012951: TRIGGER on GILA_506e, TARGET the hero and the minions)
			cards.Add("GILA_506", new CardDef(new Power { PowerTask = new AddEnchantmentTask("GILA_506e", EntityType.CONTROLLER) }));
			cards.Add("GILA_506e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new HealTask(V("GILA_506", "heal", 2), EntityType.FRIENDS) }
			}));
			// [GILA_813] Expedite — Your minions cost (1) less. (GILA_813e on the player)
			cards.Add("GILA_813", new CardDef(new Power { PowerTask = new AddEnchantmentTask("GILA_813e", EntityType.CONTROLLER) }));
			cards.Add("GILA_813e", HeistPlayerAura(new Aura(AuraType.HAND, Effects.ReduceCost(V("GILA_813", "discount", 1))) { Condition = SelfCondition.IsMinion }));

			// --------------------------------------------------------------------------------------- new in the Heist

			// [DALA_728] Recycling — After a friendly minion dies, gain 2 armor. Live (live-session-20261003-184102 5529): TRIGGER on
			// DALA_728e (TRIGGER_VISUAL), the Armor, then the dummy DALA_728d played at the hero (Generic.PlayDummyEffect).
			cards.Add("DALA_728", new CardDef(new Power { PowerTask = new AddEnchantmentTask("DALA_728e", EntityType.CONTROLLER) }));
			cards.Add("DALA_728e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerSource = TriggerSource.MINIONS, EitherTurn = true, KeywordOverride = GameTag.TRIGGER_VISUAL, // (live: TriggerKeyword TRIGGER_VISUAL)
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						new ArmorTask(V("DALA_728", "armor", 2)).Process(g, c, s, t);
						Generic.PlayDummyEffect(c, s, "DALA_728d", c.Hero);
					})
				}
			}));
			// [DALA_731] Rocket Backpacks — Your minions have Rush. (DALA_731e on the player; DALA_731e2 "Has Rush" on the minions)
			cards.Add("DALA_731", new CardDef(new Power { PowerTask = new AddEnchantmentTask("DALA_731e", EntityType.CONTROLLER) }));
			cards.Add("DALA_731e", HeistPlayerAura(new Aura(AuraType.BOARD, "DALA_731e2")));
			cards.Add("DALA_731e2", new CardDef(new Power { Enchant = new Enchant(Effects.Rush) }));
			// [DALA_733] Emerald Goggles — The left-most card in your hand costs (2) less. Live (heist-session-20260929-122856; 222
			// DALA_733e2 sends): DALA_733e (AURA) on the player, DALA_733e2 "Cost reduced by (2)" on the left-most card, moving with it.
			cards.Add("DALA_733", new CardDef(new Power { PowerTask = new AddEnchantmentTask("DALA_733e", EntityType.CONTROLLER) }));
			cards.Add("DALA_733e", HeistPlayerAura(new Aura(AuraType.HAND, "DALA_733e2")
			{
				Condition = new SelfCondition(p => p.Zone?.Type == Zone.HAND && p.ZonePosition == 0), Restless = true
			}));
			cards.Add("DALA_733e2", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("DALA_733", "discount", 2))) }));
			// [DALA_735] Robes of Gaudiness — Your cards cost half, but you can only play two cards each turn. Live
			// (live-session-20261001-162435 3379): DALA_735e (AURA, TAG_SCRIPT_DATA_NUM_1 = cards played this turn) on the player,
			// DALA_735e2 "Costs half" on each hand card; once two were played, DALA_735e3 "Too Gaudy: Can't play cards this turn" on each
			// hand card. Ours: the half is the player's cost rule (Generic.CARDS_COST_HALF: rounded down, after every other change); "can't
			// play" is HsTag.ONLY_PLAY_CARDTYPE = no type (Playable.IsPlayRestricted; LITERALLY_UNPLAYABLE on the hand cards, every card drawn
			// later that turn too) until the turn ends.
			cards.Add("DALA_735", new CardDef(new Power { PowerTask = new AddEnchantmentTask("DALA_735e", EntityType.CONTROLLER) }));
			cards.Add("DALA_735e", new CardDef(new Power
			{
				Aura = new MultiAura(
					new Aura(AuraType.CONTROLLER, new Effect(Generic.CARDS_COST_HALF, EffectOperator.ADD, 1)),
					new Aura(AuraType.HAND, new IEffect[0]) { ShowAs = "DALA_735e2" }),
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.AFTER_PLAY_CARD)
					{
						TriggerSource = TriggerSource.FRIENDLY,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (s is Enchantment e) e[GameTag.TAG_SCRIPT_DATA_NUM_1] = c.NumCardsPlayedThisTurn;
							if (c.NumCardsPlayedThisTurn < V("DALA_735", "cards", 2) || c[HsTag.ONLY_PLAY_CARDTYPE] == Generic.NoCardType) return;
							c[HsTag.ONLY_PLAY_CARDTYPE] = Generic.NoCardType;
							foreach (IPlayable card in c.HandZone.ToList())
								Give(g, s, card, "DALA_735e3");
						})
					},
					new Trigger(TriggerType.TURN_END)
					{
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (c[HsTag.ONLY_PLAY_CARDTYPE] == Generic.NoCardType) c[HsTag.ONLY_PLAY_CARDTYPE] = 0;
							if (s is Enchantment e && e[GameTag.TAG_SCRIPT_DATA_NUM_1] != 0) e[GameTag.TAG_SCRIPT_DATA_NUM_1] = 0;
						})
					})
			}));
			cards.Add("DALA_735e3", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { EitherTurn = true, SingleTask = RemoveEnchantmentTask.Task }
			}));
			// [DALA_736] Stargazing — You can use your Hero Power twice each turn. It costs (1) less. Live (heist-session-20260929-122856):
			// DALA_736e (AURA) on the player, the Hero Power's COST lowered.
			cards.Add("DALA_736", new CardDef(new Power { PowerTask = new AddEnchantmentTask("DALA_736e", EntityType.CONTROLLER) }));
			cards.Add("DALA_736e", HeistPlayerAura(new MultiAura(
				new Aura(AuraType.CONTROLLER, new Effect(GameTag.HEROPOWER_ADDITIONAL_ACTIVATIONS, EffectOperator.ADD, V("DALA_736", "extra_uses", 1))),
				new Aura(AuraType.HEROPOWER, Effects.ReduceCost(V("DALA_736", "discount", 1))))));
			// [DALA_737] Resourcefulness — At the start of the game, equip a random weapon. Give it +1/+1. (DALA_737e "E.V.I.L. Upgrade:
			// +1 Attack and +1 Durability"; any collectible weapon — the text names no class)
			cards.Add("DALA_737", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card weapon = RandomCard(g, x => x.Type == CardType.WEAPON);
					if (weapon == null) return;
					new WeaponTask(weapon.Id).Process(g, c, s, null);
					if (c.Hero.Weapon != null) Give(g, s, c.Hero.Weapon, "DALA_737e");
				})
			}));
			cards.Add("DALA_737e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.ATK, EffectOperator.ADD, V("DALA_737", "stats", 1)),
					new Effect(GameTag.DURABILITY, EffectOperator.ADD, V("DALA_737", "stats", 1)))
			}));
			// [DALA_738] A Prince's Ring — Replace your starting Hero Power with a random Death Knight one. (the nine Death Knight Hero
			// Powers of Knights of the Frozen Throne — upstream's entourage)
			string[] dkPowers = { "ICC_831p", "ICC_828p", "ICC_833h", "ICC_832p", "ICC_834h", "ICC_830p", "ICC_481p", "ICC_829p", "ICC_827p" };
			cards.Add("DALA_738", new CardDef(dkPowers, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var pool = dkPowers.Select(Cards.TryFromId).Where(x => x != null).ToList();
					if (pool.Count > 0) new ReplaceHeroPower(pool[g.Random.Next(pool.Count)]).Process(g, c, s, null);
				})
			}));
			// [DALA_739] Book of Wonders — At the start of the game, shuffle 10 'Scrolls of Wonder' into your deck. Live
			// (heist-session-20260929-114419): two POWER blocks (index 0 and 1) of 5 Scrolls each (LOOT_106t, the client's related card).
			ISimpleTask scrolls = new CustomTask((g, c, s, t, stack) =>
			{
				for (int i = 0; i < V("DALA_739", "scrolls", 10) / 2 && !c.DeckZone.IsFull; i++)
					Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("LOOT_106t"), new EntityData { [GameTag.CREATOR] = s.Id }));
			});
			cards.Add("DALA_739", new CardDef(new Power { PowerTask = scrolls, SecondPowerTask = scrolls }));
			// [DALA_740] Wondrous Wisdomball — Occasionally gives helpful advice. Live (live-session-20261005-140614): DALA_740e on the
			// player; its triggers: after you play a minion — DALA_740ts4 "Transform a minion into a random Legendary minion" cast at it
			// (TRIGGER index 15, 52810); when you play a spell — DALA_740e3 "Extra Spellcast" on the player, the spell casts twice
			// (TRIGGER index 18, MISSION_EVENT 523, 50216). The wiki lists ~21 kinds of advice (wiki Wondrous_Wisdomball, "it can trigger
			// almost every turn" late in a run); only these two are recorded — the rest and the chances are unknown (doubtful).
			cards.Add("DALA_740", new CardDef(new Power { PowerTask = new AddEnchantmentTask("DALA_740e", EntityType.CONTROLLER) }));
			bool WisdomChance(Game g) => g.Random.Next(100) < V("DALA_740", "chance_percent", 15);
			cards.Add("DALA_740e", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.AFTER_PLAY_MINION)
					{
						TriggerSource = TriggerSource.FRIENDLY, EffectIndex = 15,
						Condition = new SelfCondition(p => p is Minion m && m.Zone?.Type == Zone.PLAY && WisdomChance(p.Game)),
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (!(t is Minion m) || m.Zone?.Type != Zone.PLAY) return;
							Card legend = RandomCard(g, x => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY && !x.NeverRandomlyOnBoard);
							if (legend == null) return;
							Generic.PlayDummyEffect(c, s, "DALA_740ts4", m, () => Generic.ChangeEntityBlock(c, m, legend, true));
						})
					},
					new Trigger(TriggerType.CAST_SPELL)
					{
						TriggerSource = TriggerSource.FRIENDLY, EffectIndex = 18,
						Condition = new SelfCondition(p => WisdomChance(p.Game)),
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							Generic.AddEnchantmentBlock(g, Cards.FromId("DALA_740e3"), (IPlayable)s, c, 0, 0, 0);
							c[HsTag.PLAYED_SPELLS_CAST_TWICE] = c[HsTag.PLAYED_SPELLS_CAST_TWICE] + 1;
						})
					})
			}));
			cards.Add("DALA_740e3", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = RemoveEnchantmentTask.Task }
			}));
			// [DALA_741] Togwaggle's Dice — At the end of your turn, randomize the cost of all cards in your hand. (DALA_741e on the player,
			// DALA_741e2 "Roll the Dice: Cost manipulated" on each card.) Out of Games (outof.games/news/131): "Togwaggle's Dice does not
			// randomize between 0 and 10, it increases or decreases the cost of cards at random" — by how much isn't known: ours ±2
			// (doubtful), never below 0.
			cards.Add("DALA_741", new CardDef(new Power { PowerTask = new AddEnchantmentTask("DALA_741e", EntityType.CONTROLLER) }));
			cards.Add("DALA_741e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						int range = V("DALA_741", "range", 2);
						foreach (IPlayable card in c.HandZone.ToList())
						{
							int change = g.Random.Next(-range, range + 1);
							if (change == 0) continue;
							// (never below 0: a decrease is at most the card's cost now)
							int down = change < 0 ? System.Math.Min(-change, card.Cost) : 0;
							if (change < 0 && down == 0) continue;
							Generic.AddEnchantmentBlock(g, Cards.FromId("DALA_741e2"), (IPlayable)s, card, change > 0 ? change : 0, down, 0);
						}
					})
				}
			}));
			cards.Add("DALA_741e2", new CardDef(new Power
			{
				// TAG_SCRIPT_DATA_NUM_1 = how much more, NUM_2 = how much less (rolled when the dice roll)
				Enchant = new Enchant(new Effect(GameTag.COST, EffectOperator.ADD, 0), new Effect(GameTag.COST, EffectOperator.SUB, 0)) { UseScriptTag = true },
			}));
			// [DALA_743] Dr. Boom's Remote — At the start of the game, summon three 1/1 Boom Bots (GVG_110t).
			cards.Add("DALA_743", new CardDef(new Power { PowerTask = new SummonTask("GVG_110t", V("DALA_743", "bots", 3), SummonSide.SPELL) }));
			// [DALA_744] Hagatha's Embrace — At the start of your turn, give a random minion in your hand +1/+1. (DALA_744e on the
			// player, DALA_744e2 "+1/+1", DALA_744d its dummy effect)
			cards.Add("DALA_744", new CardDef(new Power { PowerTask = new AddEnchantmentTask("DALA_744e", EntityType.CONTROLLER) }));
			cards.Add("DALA_744e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var minions = c.HandZone.Where(p => Generic.IsMinionCard(p)).ToList();
						if (minions.Count == 0) return;
						IPlayable pick = minions[g.Random.Next(minions.Count)];
						Generic.PlayDummyEffect(c, s, "DALA_744d", pick, () => Give(g, s, pick, "DALA_744e2"));
					})
				}
			}));
			cards.Add("DALA_744e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DALA_744", "stats", 1))) }));
			// [DALA_745] The Hand of Rafaam — At the start of the game, give your opponent two 'Cursed!' cards (LOE_007t).
			cards.Add("DALA_745", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("DALA_745", "curses", 2); i++) AddToHand(c.Opponent, Cards.FromId("LOE_007t"));
				})
			}));
			// [DALA_746] Elixir of Vigor — After you play a minion, shuffle two copies of it into your deck. They cost (1). (DALA_746e on
			// the player, TRIGGER_VISUAL; DALA_746d its dummy effect)
			cards.Add("DALA_746", new CardDef(new Power { PowerTask = new AddEnchantmentTask("DALA_746e", EntityType.CONTROLLER) }));
			cards.Add("DALA_746e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t == null) return;
						Generic.PlayDummyEffect(c, s, "DALA_746d", t, () =>
						{
							// (the client's generic "Costs (1)" GBL_001e: the card data has no enchantment of its own for it)
							for (int i = 0; i < V("DALA_746", "copies", 2) && !c.DeckZone.IsFull; i++)
							{
								IPlayable copy = Entity.FromCard(c, t.Card, new EntityData { [GameTag.CREATOR] = s.Id });
								if (Generic.ShuffleIntoDeck.Invoke(c, s, copy)) Give(g, s, copy, "GBL_001e");
							}
						});
					})
				}
			}));
			// [DALA_747] Elixir of Vim — You draw an additional two cards each turn. You are Immune to Fatigue. Live: DALA_747e "Draw 2
			// additional cards each turn" on the player (heist-session RecordedBlockMeta 4/4); the immunity is Generic.FATIGUE_IMMUNE.
			cards.Add("DALA_747", new CardDef(new Power { PowerTask = new AddEnchantmentTask("DALA_747e", EntityType.CONTROLLER) }));
			cards.Add("DALA_747e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER, new Effect(Generic.FATIGUE_IMMUNE, EffectOperator.ADD, 1)),
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = new DrawTask(V("DALA_747", "cards", 2)) }
			}));
			// [DALA_748] Elixir of Vile — Your spells cost Health instead of Mana. Live (heist-session-20260929-122856 16700): DALA_748e on
			// the player; the spells in hand get CARD_ALTERNATE_COST 1 (the engine's SPELLS_COST_HEALTH rule).
			cards.Add("DALA_748", new CardDef(new Power { PowerTask = new AddEnchantmentTask("DALA_748e", EntityType.CONTROLLER) }));
			cards.Add("DALA_748e", HeistPlayerAura(new Aura(AuraType.CONTROLLER, new Effect(GameTag.SPELLS_COST_HEALTH, EffectOperator.SET, 1))));
		}

		// =============================================================================================================== treasures

		private static void HeistTreasureCards(IDictionary<string, CardDef> cards)
		{
			// [DALA_700] Continuum Collider (4) — Take an extra turn. Your opponent takes an extra turn after. (Generic.GrantExtraTurn; the
			// opponent's TemporusFlag: two turns when yours end)
			cards.Add("DALA_700", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Generic.GrantExtraTurn(c, s);
					c.Opponent.TemporusFlag = true;
				})
			}));
			// [DALA_701] The Box (0) — Each turn this is in your hand, transform it into a random treasure. (Can't be played: its own
			// play requirement; the shared in-hand shift, DALA_701e "Sifting")
			ISimpleTask box = new CustomTask((g, c, s, t, stack) => Generic.ShiftInHand(s, me => HeistRandomTreasure(g, me), Cards.FromId("DALA_701e")));
			cards.Add("DALA_701", new CardDef(HeistTreasurePool, new Dictionary<PlayReq, int> { { PlayReq.REQ_CANNOT_PLAY_THIS, 0 } }, new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START) { TriggerActivation = TriggerActivation.HAND, SingleTask = box }
			}));
			cards.Add("DALA_701e", new CardDef(new Power
			{
				Enchant = new Enchant(GameTag.SHIFTING, EffectOperator.SET, 1) { RemoveWhenPlayed = true },
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = box }
			}));
			// [DALA_702] Gnomish Army Knife (5) — Give a minion Charge, Windfury, Divine Shield, Lifesteal, Poisonous, Taunt, and Stealth.
			// (DALA_702e "Tooled Up!"; live 9/9 NOTARGET)
			cards.Add("DALA_702", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("DALA_702e", EntityType.TARGET) }));
			cards.Add("DALA_702e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Charge, Effects.Windfury, new Effect(GameTag.DIVINE_SHIELD, EffectOperator.SET, 1), Effects.Lifesteal,
					new Effect(GameTag.POISONOUS, EffectOperator.SET, 1), Effects.TauntEff, Effects.StealthEff)
			}));
			// [DALA_703] The Muscle (4) — Battlecry: Add three random cards to your hand. They cost (0). (the client's generic "Costs (0)"
			// GBL_009e)
			cards.Add("DALA_703", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("DALA_703", "cards", 3) && !c.HandZone.IsFull; i++)
						if (RandomCard(g, x => true) is Card card)
						{
							IPlayable got = Entity.FromCard(c, card, new EntityData { [GameTag.CREATOR] = s.Id });
							if (Generic.AddHandPhase.Invoke(c, got)) Give(g, s, got, "GBL_009e");
						}
				})
			}));
			// [DALA_704] Super Simian Sphere (5) — Summon King Mukla with Immune and Elusive. (EX1_014; no client enchantment: the keywords
			// on the new minion)
			cards.Add("DALA_704", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.BoardZone.IsFull) return;
					var mukla = (Minion)Entity.FromCard(c, Cards.FromId("EX1_014"), new EntityData
					{
						[GameTag.IMMUNE] = 1, [GameTag.CANT_BE_TARGETED_BY_SPELLS] = 1, [GameTag.CANT_BE_TARGETED_BY_HERO_POWERS] = 1,
						[HsTag.ELUSIVE] = 1,
					});
					Generic.SummonBlock.Invoke(g, mukla, -1, s);
				})
			}));
			// [DALA_705] Grimmer Patron (3) — At the end of your turn, summon a copy of this minion.
			cards.Add("DALA_705", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new SummonCopyTask(EntityType.SOURCE) }
			}));
			// [DALA_706] EVIL Propaganda (10) — Take control of all enemy minions. (a full board: the rest die — rules C2 Z1, ControlTask)
			cards.Add("DALA_706", new CardDef(new Power { PowerTask = new ControlTask(EntityType.OP_MINIONS) }));
			// [DALA_707] Loyal Henchman (1) — Start of Game: Draw this. Taunt. Each turn this is in your hand, gain +1/+1. (DALA_707e
			// "Scheming"; live DALA_707 TRIGGER TARGET@start = itself)
			cards.Add("DALA_707", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.GAME_START)
					{
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (s is IPlayable me && me.Zone?.Type == Zone.DECK) Generic.Draw(me.Controller, me);
						})
					},
					new Trigger(TriggerType.TURN_START)
					{
						TriggerActivation = TriggerActivation.HAND,
						SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is IPlayable me) Give(g, s, me, "DALA_707e"); })
					})
			}));
			cards.Add("DALA_707e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DALA_707", "stats", 1))) }));
			// [DALA_708] Untold Splendor (1) — Shuffle 5 random treasures into your deck.
			cards.Add("DALA_708", new CardDef(HeistTreasurePool, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("DALA_708", "treasures", 5) && !c.DeckZone.IsFull; i++)
						if (HeistRandomTreasure(g, s) is Card treasure)
							Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, treasure, new EntityData { [GameTag.CREATOR] = s.Id }));
				})
			}));
			// [DALA_709] Golden Candle (4) — Taunt. Battlecry: Replace ALL other cards with random Legendary minions. Live
			// (live-session-20261004-224614 11931): POWER 0 changes the opponent's cards, SLUSH_TIME 3000, POWER 1 the player's own (hands,
			// decks, boards: CHANGE_ENTITY each, CREATOR = the Candle, PREMIUM 1 — they turn golden).
			ISimpleTask CandleFor(bool opponent) => new CustomTask((g, c, s, t, stack) =>
			{
				Controller who = opponent ? c.Opponent : c;
				var all = who.HandZone.GetAll().Concat(who.DeckZone.GetAll()).Concat(who.BoardZone.GetAll())
					.Where(p => p.Id != s.Id && !p.ToBeDestroyed).ToList();
				foreach (IPlayable p in all)
				{
					Card legend = RandomCard(g, x => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY && (p.Zone?.Type != Zone.PLAY || !x.NeverRandomlyOnBoard));
					if (legend == null) continue;
					IPlayable now = Generic.ChangeEntityBlock(who, p, legend, true);
					if (now != null && g.History) now[GameTag.PREMIUM] = 1;
				}
			});
			cards.Add("DALA_709", new CardDef(new Power { PowerTask = CandleFor(true), SecondPowerTask = CandleFor(false) }));
			// [DALA_711] Dreamgrove Ring (9) — Discover a Legendary minion. Summon five copies of it. (the pick is the first)
			cards.Add("DALA_711", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY && !x.NeverRandomlyOnBoard,
					afterPick: new CustomTask((g, c, s, picked, stack) =>
					{
						if (picked == null) return;
						for (int i = 1; i < V("DALA_711", "copies", 5); i++) Summon(g, c, picked.Card, s);
					}), action: ChoiceAction.SUMMON)
			}));
			// [DALA_712] Orb of the Untold (4) — Deal 20 damage split between all enemies. (random 1-damage hits like Avenging Wrath, Spell
			// Damage adds hits; live live-session-20261005-140614 31540: one TARGET + damage per hit)
			cards.Add("DALA_712", new CardDef(new Power
			{
				PowerTask = new EnqueueTask(V("DALA_712", "damage", 20), ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, 1), true)
			}));
			// [DALA_713] Elder Taggawag (3) — Battlecry: Deal 4 damage to all enemy minions. Deathrattle: Shuffle this into your deck.
			cards.Add("DALA_713", new CardDef(new Power
			{
				PowerTask = new DamageTask(V("DALA_713", "damage", 4), EntityType.OP_MINIONS),
				DeathrattleTask = new MoveToDeck(EntityType.SOURCE)
			}));
			// [DALA_714] THE... Candles? (3) — Twinspell ×3. Deal 3 damage to all enemy minions. (the copies DALA_714a/b/c come from the
			// client's ReplacementsWhenPlayed: Cards.OnPlayReplacement)
			ISimpleTask candles = new DamageTask(V("DALA_714", "damage", 3), EntityType.OP_MINIONS, true);
			foreach (string id in new[] { "DALA_714", "DALA_714a", "DALA_714b", "DALA_714c" })
				cards.Add(id, new CardDef(new Power { PowerTask = candles }));
			// [DALA_715] Overpowered (2) — After you play a card this turn, replay it (targets chosen randomly). Live
			// (live-session-20261008-012951 2048-2225): DALA_715e (TAG_ONE_TURN_EFFECT) on the player; after a minion is played, a copy is
			// summoned in a TRIGGER on DALA_715e (CREATOR = it, no Battlecry); after a spell, a copy is cast (random target): Generic.PlayByEffect.
			cards.Add("DALA_715", new CardDef(new Power { PowerTask = new AddEnchantmentTask("DALA_715e", EntityType.CONTROLLER) }));
			cards.Add("DALA_715e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t == null || t is HeroPower) return;
						Generic.PlayByEffect(g, c, s, Entity.FromCard(c, t.Card, new EntityData { [GameTag.CREATOR] = s.Id }));
					})
				}
			}));
			// [DALA_716] Fly-By (5) — Shuffle a Kadoom Bot into your opponent's deck. When drawn, it explodes for 50 damage.
			// [DALA_716t] Kadoom Bot — Casts When Drawn. You take 50 damage.
			cards.Add("DALA_716", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!c.Opponent.DeckZone.IsFull)
						Generic.ShuffleIntoDeck.Invoke(c.Opponent, s, Entity.FromCard(c.Opponent, Cards.FromId("DALA_716t"), new EntityData { [GameTag.CREATOR] = s.Id }));
				})
			}));
			ISimpleTask kadoom = new DamageTask(V("DALA_716t", "damage", 50), EntityType.HERO);
			cards.Add("DALA_716t", new CardDef(new Power { PowerTask = kadoom, TopdeckTask = kadoom }));
			// [DALA_717] Case Study (1) — Discover a Quest Reward. (the nine Journey to Un'Goro rewards — upstream's entourage)
			string[] rewards = { "UNG_940t8", "UNG_116t", "UNG_067t1", "UNG_954t1", "UNG_942t", "UNG_829t1", "UNG_920t1", "UNG_934t1", "UNG_028t" };
			cards.Add("DALA_717", new CardDef(rewards, new Power
			{
				PowerTask = new DiscoverFromTask((g, c, s) => rewards.Select(Cards.TryFromId).Where(x => x != null))
			}));
			// [DALA_718] Elistra the Immortal (3) — Battlecry: Recast all spells you've cast on this minion this run. Ours: the spells
			// cast on this card this game (remembered on it: Generic.Remember, at most 30 — web: "Elistra the Immortal has a cap at 30
			// spell recasts", icy-veins Dalaran Heist guides), recast at random targets, preferring her. "This run" needs the run's memory
			// across games (not done: the server would have to keep it).
			cards.Add("DALA_718", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Card spell in Generic.Remembered(s).Take(V("DALA_718", "cap", 30)))
					{
						if (c.Hero.IsDead || c.Opponent.Hero.IsDead) break;
						new CastRandomSpellTask(preferTarget: true) { Spell = spell }.Process(g, c, s, s as IPlayable);
					}
				}),
				Trigger = new Trigger(TriggerType.CAST_SPELL)
				{
					TriggerSource = TriggerSource.FRIENDLY_SPELL_CASTED_ON_THE_OWNER,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t != null) Generic.Remember(s, t.Card, V("DALA_718", "cap", 30));
					})
				}
			}));
			// [DALA_719] Soulreaper's Scythe (5) — Deathrattle: Summon all minions killed by this weapon this run. Ours: the minions its
			// hero's attacks killed this game (remembered on the weapon); "this run" needs the run's memory (not done).
			cards.Add("DALA_719", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					Condition = new SelfCondition(p => p.Game.CurrentEventData?.EventTarget is Minion m && (m.IsDead || m.ToBeDestroyed || m.Health <= 0)),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventTarget is Minion dead && s is Weapon scythe && c.Hero.Weapon == scythe) Generic.Remember(scythe, dead.Card);
					})
				},
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Card minion in Generic.Remembered(s))
					{
						if (c.BoardZone.IsFull) break;
						Summon(g, c, minion, s);
					}
				})
			}));
			// [DALA_720] Swampqueen's Call (2) — Transform your minions into random Legendary minions. Repeatable this turn.
			// ("Repeatable this turn": client NON_KEYWORD_ECHO — the engine's Echo copy)
			cards.Add("DALA_720", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in c.BoardZone.GetAll().ToList())
					{
						Card legend = RandomCard(g, x => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY && !x.NeverRandomlyOnBoard);
						if (legend != null) Generic.ChangeEntityBlock(c, m, legend, true);
					}
				})
			}));
			// [DALA_721] Duplatransmogrifier (4) — Choose a friendly minion. Replace all minions in your Adventure deck with copies of it.
			// Live (live-session-20260929-183021 75509): in the game only DISPLAYED_CREATOR = the card on the minions of the deck; the run's
			// deck changes after the game. Ours: the choice is remembered on the player (Generic.Remember: the server reads it to change
			// the run's DECK_CARD_LIST — not wired yet) and the deck's minions are marked as live.
			cards.Add("DALA_721", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } },
				new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(t is Minion chosen)) return;
						c[Generic.REMEMBERED_COUNT] = 0;
						Generic.Remember(c, chosen.Card, 1);
						foreach (IPlayable p in c.DeckZone.GetAll().Where(p => Generic.IsMinionCard(p)))
							p[GameTag.DISPLAYED_CREATOR] = s.Id;
					})
				}));
			// [DALA_722] Annoy-o Horn (7) — Fill your board with annoying minions. (Annoy-o-Tron, Annoy-o-Module, Psych-o-Tron: upstream's
			// entourage; one at random each)
			string[] annoying = { "BOT_911", "GVG_085", "OG_145" };
			cards.Add("DALA_722", new CardDef(annoying, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = c.BoardZone.FreeSpace;
					Generic.BeginSummonGroup(g, s, n);
					for (int i = 0; i < n && !c.BoardZone.IsFull; i++)
						Summon(g, c, Cards.FromId(annoying[g.Random.Next(annoying.Length)]), s);
				})
			}));
			// [DALA_723] Hyperblaster (3) — Poisonous. Your hero is Immune while attacking. (DALA_723e "Immune while attacking" on the hero,
			// like Gladiator's Longbow DS1_188e; live TRIGGER −1 TARGETS 22/22)
			cards.Add("DALA_723", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TARGET)
				{
					TriggerSource = TriggerSource.HERO, EffectIndex = -1, // (live: effect index −1, 22 of 22)
					SingleTask = new AddEnchantmentTask("DALA_723e", EntityType.HERO)
				}
			}));
			cards.Add("DALA_723e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Immune),
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.HERO, SingleTask = RemoveEnchantmentTask.Task }
			}));
			// [DALA_724] Big Boomba (2) — Destroy ALL minions. Do it again at the start of your next turn. Live (live-session-20261008-012951
			// 111464 / 112052): DALA_724e on the player; at the start of the next turn a TRIGGER on it plays DALA_724d (the dummy) and
			// destroys every minion; then it is gone.
			cards.Add("DALA_724", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new DestroyTask(EntityType.ALLMINIONS), new AddEnchantmentTask("DALA_724e", EntityType.CONTROLLER))
			}));
			cards.Add("DALA_724e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						Generic.PlayDummyEffect(c, s, "DALA_724d", c.Hero);
						new DestroyTask(EntityType.ALLMINIONS).Process(g, c, s, null);
						RemoveEnchantmentTask.Task.Process(g, c, s, null);
					})
				}
			}));
			// [DALA_725] Banana Split (2) — Give a friendly minion +2/+2. Summon two copies of it. (DALA_725e "Glowing Green"; the copies
			// keep the buff: SummonCopyTask copies its enchantments)
			cards.Add("DALA_725", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 } },
				new Power
				{
					PowerTask = ComplexTask.Create(new AddEnchantmentTask("DALA_725e", EntityType.TARGET),
						new SummonCopyTask(EntityType.TARGET), new SummonCopyTask(EntityType.TARGET))
				}));
			cards.Add("DALA_725e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DALA_725", "stats", 2))) }));
			// [DALA_726] Master Scheme (3) — Deal @ damage. Draw @ card(s). Gain @ Armor. Summon @ Boom Bot(s). (Upgrades each turn!)
			// The number is the card's TAG_SCRIPT_DATA_NUM_1 (1 in the card data), +1 at each of its owner's turns in hand (live
			// live-session-20261008-012951 78600: TRIGGER on it, TARGET itself, NUM_1 2). Live (live-session-20261003-185040 96829): three
			// POWER blocks — damage + draw (0), Armor (1), Boom Bots (2); ours: damage + draw, then Armor + Boom Bots in the second
			// (index 1) block (doubtful: no third block).
			int Scheme(IEntity s) => System.Math.Max(1, s[GameTag.TAG_SCRIPT_DATA_NUM_1]);
			cards.Add("DALA_726", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter target) Generic.DamageCharFunc((IPlayable)s, target, Scheme(s), false);
					for (int i = 0; i < Scheme(s); i++) Generic.Draw(c);
				}),
				SecondPowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					new ArmorTask(Scheme(s)).Process(g, c, s, null);
					int n = System.Math.Min(Scheme(s), c.BoardZone.FreeSpace);
					Generic.BeginSummonGroup(g, s, n);
					for (int i = 0; i < n; i++) Summon(g, c, Cards.FromId("GVG_110t"), s);
				}),
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					TriggerActivation = TriggerActivation.HAND,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						s[GameTag.TAG_SCRIPT_DATA_NUM_1] = Scheme(s) + V("DALA_726", "upgrade", 1);
					})
				}
			}));
			// [DALA_727] Sow the Seeds (5) — Give all minions in your deck +3/+3. (DALA_727e "Seeds Sown")
			cards.Add("DALA_727", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in c.DeckZone.GetAll().Where(p => Generic.IsMinionCard(p)).ToList()) Give(g, s, p, "DALA_727e");
				})
			}));
			cards.Add("DALA_727e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DALA_727", "stats", 3))) }));
			// [DALA_729] Dagwik Stickytoe (3) — Stealth. After this minion attacks a hero, steal a card from your opponent's hand.
			cards.Add("DALA_729", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					Condition = new SelfCondition(p => p.Game.CurrentEventData?.EventTarget is Hero),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (c.Opponent.HandZone.Count == 0 || c.HandZone.IsFull) return;
						Generic.GiveTo(c.Opponent.HandZone[g.Random.Next(c.Opponent.HandZone.Count)], c, Zone.HAND);
					})
				}
			}));
			// [DALA_730] Murgatha (3) — Whenever you play a card, Overload one of your opponent's Mana Crystals. (OVERLOAD_OWED +1: locked on
			// their next turn)
			cards.Add("DALA_730", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => !(p is HeroPower)),
					SingleTask = new CustomTask((g, c, s, t, stack) => c.Opponent.OverloadOwed += V("DALA_730", "overload", 1))
				}
			}));
		}

		// =============================================================================================================== twists

		private static void HeistTwists(IDictionary<string, CardDef> cards)
		{
			// [DALA_829] Twist - The Coffers — Your opponent starts the game with a 'Cache of Cash.' Live (live-session-20260929-183021 417):
			// DALA_829t summoned for the opponent in the twist's POWER block (CREATOR = the twist).
			// [DALA_829t] Cache of Cash (0/3) — Deathrattle: Give each player two Coins. (wiki The_Dalaran_Heist: "When destroyed, two Coins
			// will be added to both players' hands.")
			cards.Add("DALA_829", new CardDef(new Power { PowerTask = new SummonOpTask("DALA_829t") }));
			cards.Add("DALA_829t", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Controller player in new[] { c, c.Opponent })
						for (int i = 0; i < V("DALA_829t", "coins", 2); i++) AddToHand(player, Cards.FromId("GAME_005"));
				})
			}));
			// [DALA_830] Twist - The Finale — This run has four additional powerful bosses. (the run's: Chapter V has 12 bosses, HeistRuns;
			// nothing in the game)
			cards.Add("DALA_830", EngineOnly);
			// [DALA_831] Twist - The Stench — All minions' Attack and Health are swapped. Live (heist-session-20260929-114419 9628-9757):
			// DALA_831e2 "Stinky" on every minion card of both players, DALA_831e on the player — it swaps every minion made later as it is
			// made (Generic.StenchSwap; ours: as it enters a hand or the board).
			cards.Add("DALA_831", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in Generic.AllMinionCards(g)) Generic.StenchSwap(g, c, s, p);
					Generic.AddEnchantmentBlock(g, Cards.FromId("DALA_831e"), (IPlayable)s, c, 0, 0, 0);
				})
			}));
			cards.Add("DALA_831e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ZONE)
				{
					TriggerSource = TriggerSource.ALL_MINIONS, FastExecution = true, EitherTurn = true,
					Condition = new SelfCondition(p => p is Minion && !(p is Location) && p[Generic.HEIST_STENCH_SWAPPED] == 0),
					SingleTask = new CustomTask((g, c, s, t, stack) => Generic.StenchSwap(g, c, s, t))
				}
			}));
			// [DALA_832] Twist - The Prisons — Both players start the game with a 'Violet Prison.' wiki The_Dalaran_Heist (Chapter 2): "The
			// prisons are dormant, and they each contain a random minion. The prison will be replaced with the aforementioned minion after
			// a number of turns equal to the minion's mana cost. The minion cannot attack on the turn it is summoned unless it has Rush or
			// Charge." [DALA_832t] Violet Prison: TAG_SCRIPT_DATA_NUM_1 = the minion inside (dbf; the client's mouse-over card), NUM_2 =
			// the turns left (card data: 3). Ours: at each of its owner's turn starts −1; at 0 it leaves (Generic.RemoveFromGame) and the
			// minion is summoned in its place. (Unrecorded: the counting moment — live-checks.)
			cards.Add("DALA_832", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Controller player in new[] { c, c.Opponent })
					{
						if (player.BoardZone.IsFull) continue;
						Card inside = RandomBoardMinion(g, x => x.Cost >= 0);
						if (inside == null) continue;
						var prison = (Minion)Entity.FromCard(player, Cards.FromId("DALA_832t"), new EntityData
						{
							[GameTag.TAG_SCRIPT_DATA_NUM_1] = inside.AssetId, [GameTag.TAG_SCRIPT_DATA_NUM_2] = System.Math.Max(1, inside.Cost),
						});
						Generic.SummonBlock.Invoke(g, prison, -1, s);
					}
				})
			}));
			cards.Add("DALA_832t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion prison) || prison.Zone?.Type != Zone.PLAY) return;
						int left = prison[GameTag.TAG_SCRIPT_DATA_NUM_2] - 1;
						prison[GameTag.TAG_SCRIPT_DATA_NUM_2] = System.Math.Max(0, left);
						if (left > 0) return;
						Card inside = Cards.FromAssetId(prison[GameTag.TAG_SCRIPT_DATA_NUM_1]);
						int slot = prison.ZonePosition;
						Generic.RemoveFromGame(prison, prison);
						if (inside != null && !c.BoardZone.IsFull)
							Generic.SummonBlock.Invoke(g, (Minion)Entity.FromCard(c, inside, new EntityData { [GameTag.CREATOR] = prison.Id }), slot, null);
					})
				}
			}));
			// [DALA_833] Twist - The Carts — Both players can only have four minions. wiki The_Dalaran_Heist (Chapter 3): "Each player
			// always starts with three random Carts in play, they can either be a Dessert Cart, a Fish Cart, a Meat Cart, or a Fruit Cart.
			// They are dormant and cannot be removed." (DALA_833t / t2 / t3 / t4: UNTOUCHABLE permanents in the card data)
			string[] carts = { "DALA_833t", "DALA_833t2", "DALA_833t3", "DALA_833t4" };
			cards.Add("DALA_833", new CardDef(carts, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Controller player in new[] { c, c.Opponent })
						for (int i = 0; i < V("DALA_833", "carts", 3) && !player.BoardZone.IsFull; i++)
							Generic.SummonBlock.Invoke(g, (Minion)Entity.FromCard(player, Cards.FromId(carts[g.Random.Next(carts.Length)])), -1, s);
				})
			}));
		}
	}
}
