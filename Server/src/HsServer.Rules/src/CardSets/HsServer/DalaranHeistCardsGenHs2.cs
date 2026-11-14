// HsServer addition (not upstream SabberStone): The Dalaran Heist — bosses DALA_BOSS_26..50: their Normal ("p") and Heroic ("px") Hero
// Powers, the tokens and enchantments those make, and their boss-only cards (PARTY PORTAL!, Void Shift). Shared pieces:
// Actions/DalaranHeistGroup2.cs (a starting Hero Power on from the start, "after a minion attacks you", a minion taking over a dead
// hero, one healing step with different amounts, a deck card moved to the top, the Void) and the existing Generic actions (Silas'
// rotation, Build-A-Beast, Animal Companions, spend all your Mana, reveal from the deck, remove from the game). Every number goes
// through CardValues (V).
// Evidence: the client's card data (dbf-36.6.0.0-20261004), the boss pages on hearthstone.wiki.gg (wiki/<Boss>?action=raw), and the live
// boss games decoded in the scratchpad boss-games/ folder (cited per card: <session>:<line>).
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
		/// <summary>"Deal 2 damage to a friendly Elemental" (Mama Diggs' Excavate): a friendly Elemental minion is the target.</summary>
		private static readonly Dictionary<PlayReq, int> HeistTargetFriendlyElemental = new Dictionary<PlayReq, int>
		{
			{ PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 },
			{ PlayReq.REQ_TARGET_WITH_RACE, (int)Race.ELEMENTAL }
		};
		/// <summary>A Hero Power that summons needs a free board slot.</summary>
		private static readonly Dictionary<PlayReq, int> HeistMinionSlot = new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } };
		/// <summary>A Hero Power that adds a card to the hand needs room in it (Build-A-Beast's requirement).</summary>
		private static readonly Dictionary<PlayReq, int> HeistHandNotFull = new Dictionary<PlayReq, int> { { PlayReq.REQ_HAND_NOT_FULL, 0 } };

		/// <summary>The "old Treasures" (the Dungeon Run's: Kobolds &amp; Catacombs' adventure) — wiki Ol' Toomba, Hero Power, "Generated cards"
		/// (the 22 dbf ids of its card query), also Mama Diggs' "Dungeon Run treasures". Cards the loader lacks are left out (none expected).</summary>
		private static readonly string[] HeistOldTreasures =
		{
			"LOOTA_819", "LOOTA_839", "LOOTA_836", "LOOTA_842a", "LOOTA_812", "LOOTA_834", "LOOTA_842b", "LOOTA_847", "LOOTA_816", "LOOTA_841",
			"LOOTA_826", "LOOTA_843", "LOOTA_820", "LOOTA_835", "LOOTA_837", "LOOTA_821", "LOOTA_827", "LOOT_998k", "LOOTA_811", "LOOTA_806",
			"LOOT_998l", "LOOTA_838"
		};

		/// <summary>Portal Party's portals — wiki Portal Party (Normal), "Generated cards" (dbf 39712, 1929, 39716, 39747, 55862, 39714, 39715); live
		/// (live-20261007-212735 81884-85700: Silvermoon, Maelstrom and Firelands Portals from it).</summary>
		private static readonly string[] HeistPortals = { "KAR_073", "GVG_003", "KAR_077", "KAR_091", "DALA_BOSS_38t", "KAR_075", "KAR_076" };

		/// <summary>PARTY PORTAL!'s Partygoers — Blizzard's Entourage of the card (SabberStone's 2019 CardDefs: KARA_13_20, GIL_805, KARA_13_22,
		/// DAL_721, BRM_019, CFM_659); wiki PARTY PORTAL! (Dalaran Heist), "Summoned minions", lists the same five without Party Elemental.</summary>
		private static readonly string[] HeistPartygoers = { "KARA_13_20", "GIL_805", "KARA_13_22", "DAL_721", "BRM_019", "CFM_659" };
		/// <summary>The Animal Companions (the entourage of Summon Companion, as Animal Companion's).</summary>
		private static readonly string[] HeistCompanions = { "NEW1_034", "NEW1_032", "NEW1_033" };

		/// <summary>Kizi Copperclip's haircuts (Makeover): DALA_BOSS_32e1..e4 — live picks each of the four (heist-session-20260929-122856
		/// and four other boss games: e1 ×8, e2 ×3, e3 ×5, e4 ×7).</summary>
		private static readonly string[] HeistHaircuts = { "DALA_BOSS_32e1", "DALA_BOSS_32e2", "DALA_BOSS_32e3", "DALA_BOSS_32e4" };

		private static Card[] HeistCards(IEnumerable<string> ids) => ids.Select(Cards.TryFromId).Where(card => card != null).ToArray();

		/// <summary>Cold Water / Immolation Aura: "After a minion attacks you, &lt;effect&gt; it" — a TRIGGER block of the Hero Power after the
		/// ATTACK block, on the attacker (live Cold Water: live-session-20261004-222041 41139, EffectIndex 0, ShowInHistory, no keyword).</summary>
		private static Trigger HeistAfterMinionAttacksYou(ISimpleTask effect, SelfCondition also = null) =>
			new Trigger(TriggerType.AFTER_ATTACK)
			{
				TriggerSource = TriggerSource.OP_MINIONS,
				Condition = new SelfCondition(p => Generic.AttackedEnemyHeroAndLives(p) && (also == null || also.Eval(p))),
				InHistory = true,
				SingleTask = effect
			};

		static partial void HeistBossPowers2(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- 26 Dalaran Fountain Golem
			// [DALA_BOSS_26p] Cold Water — Passive. Freeze the first minion that attacks you each turn. Live (live-session-20261004-222041
			// 40608, 41139; 34 turn-start blocks in 3 games): a TRIGGER block at every turn start (EffectIndex 1: its NUM_1 back to 0), and after the
			// first attack on the hero a TRIGGER block (EffectIndex 0, ShowInHistory): FROZEN 1, TARGET the attacker, the Hero Power's NUM_1 = 1.
			cards.Add("DALA_BOSS_26p", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					HeistAfterMinionAttacksYou(ComplexTask.Create(
							ComplexTask.Freeze(EntityType.TARGET),
							new CustomTask((g, c, s, t, stack) => { if (s is IPlayable hp) hp[GameTag.TAG_SCRIPT_DATA_NUM_1] = 1; })),
						new SelfCondition(p => p.Controller.Opponent.Hero?.HeroPower?[GameTag.TAG_SCRIPT_DATA_NUM_1] == 0)),
					new Trigger(TriggerType.TURN_START)
					{
						EitherTurn = true,
						EffectIndex = 1,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (s is IPlayable hp && hp[GameTag.TAG_SCRIPT_DATA_NUM_1] != 0) hp[GameTag.TAG_SCRIPT_DATA_NUM_1] = 0;
						})
					})
			}));
			// [DALA_BOSS_26px] Cold Water (Heroic) — Passive. After a minion attacks you, Freeze it. (live: 7 blocks, EffectIndex 0, ShowInHistory)
			cards.Add("DALA_BOSS_26px", new CardDef(new Power { Trigger = HeistAfterMinionAttacksYou(ComplexTask.Freeze(EntityType.TARGET)) }));

			// ---------------------------------------------------------------- 27 Rasil Fireborne
			// [DALA_BOSS_27p] Masterpiece! — Spend all your Mana. Summon a random minion that costs that much. (Commissary Crook's way: 0 Mana left
			// = a 0-Cost minion; wiki Masterpiece! tags: "Spend mana", "Summon", "Cost-related")
			cards.Add("DALA_BOSS_27p", new CardDef(HeistMinionSlot, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int spent = Generic.SpendAllMana(c);
					if (Cards.BoardCostMinionCards(g.FormatType).TryGetValue(spent, out List<Card> pool) && pool.Count > 0)
						Summon(g, c, pool[g.Random.Next(pool.Count)], s);
				})
			}));

			// ---------------------------------------------------------------- 28 Xur'ios
			// [DALA_BOSS_28p] Scroll Savvy — Passive. Spells cost (0) the turn they are drawn. Each friendly spell drawn gets DALA_BOSS_28e
			// "Savvy: Costs (0) this turn" (client TAG_ONE_TURN_EFFECT): a one-turn ADD of (0 - Cost), as Darkmoon's FreeThisTurn.
			cards.Add("DALA_BOSS_28p", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DRAW)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = SelfCondition.IsSpell,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable drawn = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (drawn is Spell && drawn.Zone?.Type == Zone.HAND) FreeThisTurn(g, s, drawn, "DALA_BOSS_28e");
					})
				}
			}));
			cards.Add("DALA_BOSS_28e", new CardDef(new Power { Enchant = new Enchant(Effects.AddCost(0)) { UseScriptTag = true, IsOneTurnEffect = true } }));

			// ---------------------------------------------------------------- 29 Mama Diggs
			// [DALA_BOSS_29p] Excavate — Deal 2 damage to a friendly Elemental. Find out what's inside. Wiki Mama Diggs, "Possible excavations":
			// "Nothing", "A Small Rock" (GILA_500p2t), "Drawing 1-2 cards", "Random shaman cards", "Copy of the damaged minion", "Dungeon Run
			// treasures" — one of them, at random (their odds aren't known: equal). Live (heist-session-20260929-114419 22085, 22987, 23762): the
			// damage, then a hidden card to her hand (a copy of the damaged Tar Creeper, played later as id 167), or two draws — also when the
			// Elemental died of it.
			cards.Add("DALA_BOSS_29p", new CardDef(HeistTargetFriendlyElemental, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("DALA_BOSS_29p", "damage", 2), EntityType.TARGET),
					new CustomTask((g, c, s, t, stack) =>
					{
						switch (g.Random.Next(6))
						{
							case 0: // nothing
								break;
							case 1:
								if (Cards.TryFromId("GILA_500p2t") is Card rock) AddToHand(c, rock);
								break;
							case 2:
								for (int i = 1 + g.Random.Next(V("DALA_BOSS_29p", "draws", 2)); i > 0; i--) Generic.Draw(c);
								break;
							case 3:
								if (RandomCard(g, card => card.HasClass(CardClass.SHAMAN)) is Card shaman) AddToHand(c, shaman);
								break;
							case 4:
								if (t != null) AddToHand(c, t.Card);
								break;
							default:
								Card[] treasures = HeistCards(HeistOldTreasures);
								if (treasures.Length > 0) AddToHand(c, treasures[g.Random.Next(treasures.Length)]);
								break;
						}
					}))
			}));

			// [GILA_500p2t] A Small Rock (0, Monster Hunt) — Deal $1 damage. One of Excavate's finds (no other script has it).
			cards.Add("GILA_500p2t", new CardDef(TargetAny, new Power { PowerTask = new DamageTask(V("GILA_500p2t", "damage", 1), EntityType.TARGET, true) }));

			// ---------------------------------------------------------------- 30 The Rat King
			// [DALA_BOSS_30p] A Tale of Kings — Summon an Underbelly Rat.
			cards.Add("DALA_BOSS_30p", new CardDef(HeistMinionSlot, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("DALA_BOSS_30t"), s))
			}));
			// [DALA_BOSS_30t] Underbelly Rat (3, 10/1) — When your hero dies, the eldest Underbelly Rat replaces your hero. (The shared rule:
			// Generic.MinionTakesOverDeadHero, marked by Generic.TAKES_OVER_DEAD_HERO while it has its text.)
			cards.Add("DALA_BOSS_30t", new CardDef(new Power
			{
				Aura = new Aura(AuraType.SELF, new Effect(Generic.TAKES_OVER_DEAD_HERO, EffectOperator.SET, 1))
			}));

			// ---------------------------------------------------------------- 31 Timothy Jones
			// [DALA_BOSS_31p] Bling it On! — Make a minion Golden. If it's already Golden, give it +4/+4. (Golden = PREMIUM, as Your Eyes Betray
			// You / Fool's Gold.) [DALA_BOSS_31px] Heroic: +8/+8 (DALA_BOSS_31ex).
			cards.Add("DALA_BOSS_31p", new CardDef(TargetMinion, new Power { PowerTask = HeistBling("DALA_BOSS_31e") }));
			cards.Add("DALA_BOSS_31px", new CardDef(TargetMinion, new Power { PowerTask = HeistBling("DALA_BOSS_31ex") }));
			cards.Add("DALA_BOSS_31e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DALA_BOSS_31p", "stats", 4))) }));
			cards.Add("DALA_BOSS_31ex", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DALA_BOSS_31px", "stats", 8))) }));

			// ---------------------------------------------------------------- 32 Kizi Copperclip
			// [DALA_BOSS_32p] Makeover — Give a friendly minion a random haircut. Live (heist-session-20260929-122856 2691, 3515): one of the four
			// haircut enchantments on the targeted minion, TARGET it. (Wiki: Auto-cast.)
			cards.Add("DALA_BOSS_32p", new CardDef(TargetFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Card[] cuts = HeistCards(HeistHaircuts);
					if (cuts.Length > 0) Give(g, s, m, cuts[g.Random.Next(cuts.Length)].Id);
				})
			}));
			// [DALA_BOSS_32e1] Nondescript Fade — Has Stealth. Live (heist-session-20260929-122856 4881; live-session-20261007-113244 144409;
			// 4 of 4): the enchantment goes to the graveyard once its minion attacked (its Stealth is gone) — its own TRIGGER block.
			cards.Add("DALA_BOSS_32e1", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.StealthEff),
				Trigger = new Trigger(TriggerType.AFTER_ATTACK) { TriggerSource = TriggerSource.ENCHANTMENT_TARGET, SingleTask = RemoveEnchantmentTask.Task }
			}));
			// [DALA_BOSS_32e2] Intrusive Pompadour — Has Taunt. [DALA_BOSS_32e3] Incredible Quiff — +2 Health. [DALA_BOSS_32e4] Aggressive Mohawk — +2 Attack.
			cards.Add("DALA_BOSS_32e2", new CardDef(new Power { Enchant = new Enchant(Effects.TauntEff) }));
			cards.Add("DALA_BOSS_32e3", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("DALA_BOSS_32e3", "health", 2))) }));
			cards.Add("DALA_BOSS_32e4", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("DALA_BOSS_32e4", "attack", 2))) }));

			// ---------------------------------------------------------------- 33 Gold Elemental
			// [DALA_BOSS_33p] Made of Coins — Passive. Whenever you take damage, add a Coin to your hand. Live (live-session-20260929-183021
			// 40403, 41892; 36 blocks): a TRIGGER block per damage taken (EffectIndex 0, ShowInHistory), a Coin to the hand.
			cards.Add("DALA_BOSS_33p", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.HERO,
					InHistory = true,
					SingleTask = new CustomTask((g, c, s, t, stack) => AddToHand(c, Cards.FromId("GAME_005")))
				}
			}));

			// ---------------------------------------------------------------- 34 Carousel Gryphon
			// [DALA_BOSS_34p] Merry Go Round — Passive. At the end of your turn, merrily rotate all minions around the board. Silas Darkmoon's
			// rotation (wiki Carousel Gryphon (boss) trivia: "Its Hero Power … became the basis of the Battlecry of Silas Darkmoon") one way:
			// hearthstonetopdecks Heroic guide — "Your right-most minion becomes his, while his left-most minion becomes yours." Live
			// (live-session-20260929-183021 15875, 16749): TRIGGER block (TRIGGER_VISUAL, no ShowInHistory): his left-most to the player's left
			// end, the player's right-most to his right end, then BEGIN_ARTIFICIAL_HISTORY_TRIGGER_TILE + OVERRIDE_HISTORY naming the Hero Power.
			cards.Add("DALA_BOSS_34p", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					NotInHistory = true,
					// (2026-10-09, user: "it triggers with no minion on the board — that doesn't happen"; live: 4 of 4 triggers moved a minion)
					Condition = new SelfCondition(p => p.Controller.BoardZone.Count > 0 || p.Controller.Opponent.BoardZone.Count > 0),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						RotateMinions(c, true);
						Generic.SelfTriggerTile((IPlayable)s, new int[0]);
					})
				}
			}));

			// ---------------------------------------------------------------- 35 Soothsayer Zoie
			// [DALA_BOSS_35p] Healing Hands — Passive. At the start of your turn, restore #2 Health to all friendly minions. Live
			// (live-session-20261004-224614 78852, 79723; 22 blocks): TRIGGER (TRIGGER_VISUAL, ShowInHistory) at each of her turn starts, TARGET
			// every friendly minion (damaged or not: Overheal fires), an empty block with none.
			cards.Add("DALA_BOSS_35p", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = new HealTask(V("DALA_BOSS_35p", "heal", 2), EntityType.MINIONS) }
			}));
			// [DALA_BOSS_35px] Healing Hands (Heroic) — … restore all friendly minions to full Health. Live (11 blocks): PREHEALING = each
			// minion's maximum Health (UNG_034 3, EX1_335 4) — Character.TakeFullHeal (rules E2).
			cards.Add("DALA_BOSS_35px", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
						Generic.HealEach(g, (IPlayable)s, c.BoardZone.GetAll().Where(m => !(m is Location)).Cast<ICharacter>().ToList(),
							ch => ch.TakeFullHeal((IPlayable)s)))
				}
			}));

			// ---------------------------------------------------------------- 36 Draemus
			// [DALA_BOSS_36p] / [DALA_BOSS_36px] Import "Pet" — Craft a custom Exotic Pet. Wiki Draemus, Notes: "Import "Pet" is functionally
			// identical to Build-A-Beast" (SpecificTask.BuildABeast). Live (live-session-20261002-204752: 17 uses): two hidden 3-card picks, then
			// a hidden card to her hand. (DALA_BOSS_36t "Exotic Pet" is the client's 1/1 Beast that card is made from: the result was never shown.)
			cards.Add("DALA_BOSS_36p", new CardDef(HeistHandNotFull, new Power { PowerTask = SpecificTask.BuildABeast }));
			cards.Add("DALA_BOSS_36px", new CardDef(HeistHandNotFull, new Power { PowerTask = SpecificTask.BuildABeast }));

			// ---------------------------------------------------------------- 37 Ol' Toomba
			// [DALA_BOSS_37p] Tales of Fortune — Shuffle a random old Treasure into your deck. [DALA_BOSS_37px] Heroic: three. Live
			// (live-session-20261004-224614: LOOTA_838, LOOTA_835 …): the new card into the deck, shown (REVEALED) and hidden again, SHUFFLE_DECK.
			cards.Add("DALA_BOSS_37p", new CardDef(new Power { PowerTask = HeistShuffleTreasures("DALA_BOSS_37p", 1) }));
			cards.Add("DALA_BOSS_37px", new CardDef(new Power { PowerTask = HeistShuffleTreasures("DALA_BOSS_37px", 3) }));

			// ---------------------------------------------------------------- 38 Tipsi Wobblerune
			// [DALA_BOSS_38p] / [DALA_BOSS_38px] Portal Party — Add a random Portal to your hand. Live (live-session-20261007-212735 81884): a
			// hidden card to her hand, DISPLAYED_CREATOR the Hero Power.
			cards.Add("DALA_BOSS_38p", new CardDef(HeistPortals, HeistHandNotFull, new Power { PowerTask = HeistRandomToHand(HeistPortals) }));
			cards.Add("DALA_BOSS_38px", new CardDef(HeistPortals, HeistHandNotFull, new Power { PowerTask = HeistRandomToHand(HeistPortals) }));
			// [DALA_BOSS_38t] PARTY PORTAL! (4) — Summon a random Partygoer.
			cards.Add("DALA_BOSS_38t", new CardDef(HeistPartygoers, HeistMinionSlot, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Card[] goers = HeistCards(HeistPartygoers);
					if (goers.Length > 0) Summon(g, c, goers[g.Random.Next(goers.Length)], s);
				})
			}));

			// ---------------------------------------------------------------- 39 Aki the Brilliant
			// [DALA_BOSS_39p] For The Light! — Give all minions in your hand +1/+1. Live (live-session-20261002-204752: 28 uses): one
			// DALA_BOSS_39e per hand minion, each followed by its TARGET.
			cards.Add("DALA_BOSS_39p", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in c.HandZone.GetAll().Where(p => Generic.IsMinionCard(p)).ToList()) Give(g, s, p, "DALA_BOSS_39e");
				})
			}));
			cards.Add("DALA_BOSS_39e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DALA_BOSS_39p", "stats", 1))) }));

			// ---------------------------------------------------------------- 40 Oxana Demonslay
			// [DALA_BOSS_40p] Immolation Aura — Passive. After a minion attacks you, deal 2 damage to it. [DALA_BOSS_40px] Heroic: 3. (Cold
			// Water's heroic shape: unrecorded.)
			cards.Add("DALA_BOSS_40p", new CardDef(new Power { Trigger = HeistAfterMinionAttacksYou(new DamageTask(V("DALA_BOSS_40p", "damage", 2), EntityType.TARGET)) }));
			cards.Add("DALA_BOSS_40px", new CardDef(new Power { Trigger = HeistAfterMinionAttacksYou(new DamageTask(V("DALA_BOSS_40px", "damage", 3), EntityType.TARGET)) }));

			// ---------------------------------------------------------------- 41 Ungan Oddkind
			// [DALA_BOSS_41p] Summon Companion — Summon a random Animal Companion. [DALA_BOSS_41px] Summon Companions — two (different ones).
			// Wiki: Huffer, Leokk, Misha (Generic.SummonCompanions).
			cards.Add("DALA_BOSS_41p", new CardDef(HeistCompanions, HeistMinionSlot, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.SummonCompanions(c, s, V("DALA_BOSS_41p", "companions", 1)))
			}));
			cards.Add("DALA_BOSS_41px", new CardDef(HeistCompanions, HeistMinionSlot, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.SummonCompanions(c, s, V("DALA_BOSS_41px", "companions", 2)))
			}));

			// ---------------------------------------------------------------- 42 P.O.G.O.
			// [DALA_BOSS_42p] Pogoshuffle — Choose a minion. Shuffle 3 copies of it into your deck. [DALA_BOSS_42px] Heroic: … Draw a card. Live
			// (live-session-20261008-012951 etc., 5 heroic uses, enemy minions too): three new cards into the deck, one SHUFFLE_DECK, then the draw.
			cards.Add("DALA_BOSS_42p", new CardDef(TargetMinion, new Power { PowerTask = HeistPogoshuffle("DALA_BOSS_42p", false) }));
			cards.Add("DALA_BOSS_42px", new CardDef(TargetMinion, new Power { PowerTask = HeistPogoshuffle("DALA_BOSS_42px", true) }));

			// ---------------------------------------------------------------- 43 Magistrix Norroa
			// [DALA_BOSS_43p] Kaironomaly — Passive. Your opponent has 25 seconds to take their turn. A turn-time limit on the opponent only
			// (Nozdormu's Controller.TurnLimitSeconds, shown on its TIMEOUT by Generic.SyncTurnLimit). Its number is what live sends: TIMEOUT 30
			// on the player alone, from the game's start (live-session-20261004-222041 607, live-session-20261002-204752 83540) — like Nozdormu's
			// "15 seconds" sent as TIMEOUT 20. (Wiki note: Nozdormu in play lifted it; ours averages the two limits — doubt.)
			cards.Add("DALA_BOSS_43p", new CardDef(new Power
			{
				Aura = new Aura(AuraType.OPPONENT, new Effect(HsTag.TURN_LIMITS, EffectOperator.ADD, 1),
					new Effect(HsTag.TURN_LIMIT_SECONDS, EffectOperator.ADD, V("DALA_BOSS_43p", "seconds", 30)))
			}));

			// ---------------------------------------------------------------- 44 Erekem
			// [DALA_BOSS_44p] Dark Tidings — Reveal a minion from your deck. Give it +2/+2 and put it on top. [DALA_BOSS_44px] Heroic: +4/+4.
			// (Generic.RevealFromDeck — the reveal; the buff on the deck card; Generic.MoveToTopOfDeck.)
			cards.Add("DALA_BOSS_44p", new CardDef(new Power { PowerTask = HeistDarkTidings("DALA_BOSS_44e") }));
			cards.Add("DALA_BOSS_44px", new CardDef(new Power { PowerTask = HeistDarkTidings("DALA_BOSS_44ex") }));
			cards.Add("DALA_BOSS_44e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DALA_BOSS_44p", "stats", 2))) }));
			cards.Add("DALA_BOSS_44ex", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DALA_BOSS_44px", "stats", 4))) }));

			// ---------------------------------------------------------------- 45 Moragg
			// [DALA_BOSS_45p] / [DALA_BOSS_45px] Ray of Suffering — Deal @ damage to the enemy hero. (increases each use) The @ is the Hero
			// Power's TAG_SCRIPT_DATA_NUM_1 (client CardTextBuilderType SCRIPT_DATA_NUM_1; card data 1), raised after each use.
			cards.Add("DALA_BOSS_45p", new CardDef(new Power { PowerTask = HeistRayOfSuffering("DALA_BOSS_45p") }));
			cards.Add("DALA_BOSS_45px", new CardDef(new Power { PowerTask = HeistRayOfSuffering("DALA_BOSS_45px") }));

			// ---------------------------------------------------------------- 46 Ichoron
			// [DALA_BOSS_46p] Protective Bubble — Give a friendly minion Divine Shield. [DALA_BOSS_46px] Heroic: all your minions (wiki: Auto-cast).
			cards.Add("DALA_BOSS_46p", new CardDef(TargetFriendlyMinion, new Power { PowerTask = ComplexTask.DivineShield(EntityType.TARGET) }));
			cards.Add("DALA_BOSS_46px", new CardDef(new Power { PowerTask = ComplexTask.DivineShield(EntityType.MINIONS) }));

			// ---------------------------------------------------------------- 47 Lavanthor
			// [DALA_BOSS_47p] / [DALA_BOSS_47px] Lava Belch — Summon a 0/3 Molten Rock.
			cards.Add("DALA_BOSS_47p", new CardDef(HeistMinionSlot, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("DALA_BOSS_47t"), s))
			}));
			cards.Add("DALA_BOSS_47px", new CardDef(HeistMinionSlot, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Summon(g, c, Cards.FromId("DALA_BOSS_47t"), s))
			}));
			// [DALA_BOSS_47t] Molten Rock (1, 0/3) — At the end of your turn, deal 1 damage to all characters. (hearthstonetopdecks: "deals 1
			// damage to the whole board at the end of the turn")
			cards.Add("DALA_BOSS_47t", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new DamageTask(V("DALA_BOSS_47t", "damage", 1), EntityType.ALL) }
			}));

			// ---------------------------------------------------------------- 48 Zuramat the Obliterator
			// [DALA_BOSS_48p] To The Void — Banish a minion to the Void. (Generic.BanishToVoid: wiki Zuramat, Notes — "removes it from play and
			// does not trigger its Deathrattle"; a list of banished minions.)
			cards.Add("DALA_BOSS_48p", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m) Generic.BanishToVoid(m, s); })
			}));
			// [DALA_BOSS_48t] Void Shift (3) — Destroy all minions. Summon all minions from the Void. (Generic.SummonFromVoid: each on the side it
			// was banished from, out of the Void after; wiki: "After using Void Shift, all minions in the list of banished are removed".) The
			// destroyed minions are pending destroy until the spell ends (rule book Rule 4a), so they still hold their board slots.
			cards.Add("DALA_BOSS_48t", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new DestroyTask(EntityType.ALLMINIONS),
					new CustomTask((g, c, s, t, stack) => Generic.SummonFromVoid(g, s)))
			}));

			// ---------------------------------------------------------------- 49 Cyanigosa
			// [DALA_BOSS_49p] Uncontrollable Energy — Passive. After you cast a spell, deal $1 damage to all enemies. [DALA_BOSS_49px] Heroic: $2.
			cards.Add("DALA_BOSS_49p", new CardDef(new Power { Trigger = HeistAfterYouCast(new DamageTask(V("DALA_BOSS_49p", "damage", 1), EntityType.ENEMIES)) }));
			cards.Add("DALA_BOSS_49px", new CardDef(new Power { Trigger = HeistAfterYouCast(new DamageTask(V("DALA_BOSS_49px", "damage", 2), EntityType.ENEMIES)) }));

			// ---------------------------------------------------------------- 50 Nozari
			// [DALA_BOSS_50p] Sand Breath — Deal $2 damage to a random enemy. [DALA_BOSS_50px] Heroic: $4. (Wiki: Auto-cast.)
			cards.Add("DALA_BOSS_50p", new CardDef(new Power { PowerTask = ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, V("DALA_BOSS_50p", "damage", 2)) }));
			cards.Add("DALA_BOSS_50px", new CardDef(new Power { PowerTask = ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, V("DALA_BOSS_50px", "damage", 4)) }));
		}

		/// <summary>Bling it On!: the targeted minion becomes Golden (PREMIUM 1); one that already is gets <paramref name="buff"/>.</summary>
		private static ISimpleTask HeistBling(string buff) => new CustomTask((g, c, s, t, stack) =>
		{
			if (!(t is Minion m)) return;
			if (m[GameTag.PREMIUM] > 0) Give(g, s, m, buff);
			else m[GameTag.PREMIUM] = 1;
		});

		/// <summary>Tales of Fortune: <paramref name="n"/> different random old Treasures shuffled into the deck (one shuffle).</summary>
		private static ISimpleTask HeistShuffleTreasures(string id, int n) => new CustomTask((g, c, s, t, stack) =>
		{
			foreach (Card treasure in Pick(g, HeistCards(HeistOldTreasures), V(id, "treasures", n)))
				Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, treasure));
		});

		/// <summary>A random card of <paramref name="ids"/> to the hand.</summary>
		private static ISimpleTask HeistRandomToHand(string[] ids) => new CustomTask((g, c, s, t, stack) =>
		{
			Card[] pool = HeistCards(ids);
			if (pool.Length > 0) AddToHand(c, pool[g.Random.Next(pool.Length)]);
		});

		/// <summary>Pogoshuffle: 3 copies (new cards of its card) of the chosen minion shuffled into the deck; the Heroic one then draws.</summary>
		private static ISimpleTask HeistPogoshuffle(string id, bool draw) => new CustomTask((g, c, s, t, stack) =>
		{
			if (t is Minion m)
				for (int i = 0; i < V(id, "copies", 3); i++)
					Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, m.Card));
			if (draw) Generic.Draw(c);
		});

		/// <summary>Dark Tidings: a random minion of the deck revealed, given <paramref name="buff"/> and put on top.</summary>
		private static ISimpleTask HeistDarkTidings(string buff) => new CustomTask((g, c, s, t, stack) =>
		{
			if (!(Generic.RevealFromDeck(c, (IPlayable)s, p => p is Minion) is IPlayable minion)) return;
			Give(g, s, minion, buff);
			Generic.MoveToTopOfDeck(minion);
		});

		/// <summary>Ray of Suffering: its NUM_1 damage to the enemy hero, then NUM_1 one more.</summary>
		private static ISimpleTask HeistRayOfSuffering(string id) => new CustomTask((g, c, s, t, stack) =>
		{
			if (!(s is IPlayable hp)) return;
			int damage = System.Math.Max(1, hp[GameTag.TAG_SCRIPT_DATA_NUM_1]);
			Generic.DamageCharFunc(hp, c.Opponent.Hero, damage, false);
			hp[GameTag.TAG_SCRIPT_DATA_NUM_1] = damage + V(id, "increase", 1);
		});

		/// <summary>"After you cast a spell, &lt;effect&gt;" on a Passive Hero Power (Uncontrollable Energy).</summary>
		private static Trigger HeistAfterYouCast(ISimpleTask effect) =>
			new Trigger(TriggerType.AFTER_CAST) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = effect };
	}
}
