// HsServer addition (not upstream SabberStone): The Dalaran Heist — chunk 6: Anomaly Mode's 18 anomalies (DALA_850 … DALA_867, the
// wiki's list: hearthstone.wiki.gg/wiki/The_Dalaran_Heist#Anomaly_Mode — "Anomaly Mode adds an additional Twist to the selected
// chapter. The Twist is rolled before chapter selection"), their "… Player Enchantment"s and tokens. Shared pieces:
// Actions/DalaranHeistAnomalies.cs — the anomaly is played after the mulligan (Generic.PlayAnomaly), an aura anomaly gives ONE player
// enchantment whose aura covers both players (live: Arcane, Crying, Rattling), a trigger anomaly gives each player their own
// (Generic.GiveEachPlayer; live: Refreshing, Wild Magic), Wild Magic's copy (Generic.CastPlayedCopy). Existing shared mechanics reused:
// Brann / Rivendare's EXTRA_*_BASE, Mogor's Forgetful attack (ForgetfulAttack), Summoning Stone's "minion of the same Cost", Dragon
// Soul's 5/5 (LOOT_209t), the client's generic cost enchantments (GBL_002e / GBL_003e), the Heist treasure pool. Every number goes
// through CardValues (V). Live block shapes: the decoded anomaly games (scratchpad decoded/, 8 sessions; FINDINGS 2026-10-09).
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Actions;
using SabberStoneCore.Auras;
using SabberStoneCore.Conditions;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.src.Loader;
using SabberStoneCore.Tasks;
using SabberStoneCore.Tasks.SimpleTasks;
using SabberStoneCore.Triggers;

namespace SabberStoneCore.CardSets.HsServer
{
	public static partial class CurrentCoreCardsGen
	{
		/// <summary>The anomaly's one player enchantment, on its player (live: ATTACHED = player 1, an aura over both players). Its aura
		/// applies inside the anomaly's POWER block (live 185040 631-681: Arcane's GBL_002e on the spells in hand; 222041 1436-1440:
		/// Crying's EXTRA_BATTLECRIES_BASE on both players), not after it.</summary>
		private static CardDef AnomalyOnePlayer(string enchantment) => new CardDef(new Power
		{
			PowerTask = new CustomTask((g, c, s, t, stack) =>
			{
				Generic.AddEnchantmentBlock(g, Cards.FromId(enchantment), (IPlayable)s, c, 0, 0, 0);
				g.AuraUpdate();
			})
		});

		/// <summary>The anomaly's player enchantment on each player, its own player's first (Generic.GiveEachPlayer).</summary>
		private static CardDef AnomalyEachPlayer(string enchantment) => new CardDef(new Power
		{
			PowerTask = new CustomTask((g, c, s, t, stack) => Generic.GiveEachPlayer(g, (IPlayable)s, Cards.FromId(enchantment)))
		});

		/// <summary>What a played card cost (PlayCard keeps it as TAG_LAST_KNOWN_COST_IN_HAND: the Mana paid).</summary>
		private static int AnomalyPlayedCost(IPlayable p) => p == null ? 0 : p[GameTag.TAG_LAST_KNOWN_COST_IN_HAND];

		static partial void HeistAnomalies(IDictionary<string, CardDef> cards)
		{
			// [DALA_850] Anomaly - Arcane — All spells cost (2) less. Live (live-session-20261003-185040 626-681): DALA_850e on player 1;
			// GBL_002e (the client's generic "Cost - 2", ENCHANTMENT_INVISIBLE) on every spell in both hands (the opponent's hidden).
			cards.Add("DALA_850", AnomalyOnePlayer("DALA_850e"));
			cards.Add("DALA_850e", HeistPlayerAura(new Aura(AuraType.HANDS, Effects.ReduceCost(V("DALA_850", "discount", 2)))
			{
				Condition = SelfCondition.IsSpell, ShowAs = "GBL_002e"
			}));

			// [DALA_851] Anomaly - Summons — After a spell is played, a minion of the same cost is summoned. (Unrecorded. For the spell's
			// player — each player's DALA_851e; the Cost paid, as Summoning Stone's; a minion that may go straight onto the board.)
			cards.Add("DALA_851", AnomalyEachPlayer("DALA_851e"));
			cards.Add("DALA_851e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						int cost = AnomalyPlayedCost(t);
						Summon(g, c, RandomBoardMinion(g, x => x.Cost == cost), s);
					})
				}
			}));

			// [DALA_852] Anomaly - Crying — All Battlecries trigger twice. [DALA_853] Anomaly - Rattling — All Deathrattles trigger twice.
			// Live (live-session-20261004-222041 1436-1440; 20261004-224614 71424-71450): the player enchantment's POWER block sets
			// EXTRA_BATTLECRIES_BASE / EXTRA_DEATHRATTLES_BASE = 1 on both players (Brann's / Rivendare's rule: Controller.BattlecryRuns,
			// DeathrattleTimes — SET, so never three times with Brann: wiki Brann "multiple … do not stack").
			cards.Add("DALA_852", AnomalyOnePlayer("DALA_852e"));
			cards.Add("DALA_852e", HeistPlayerAura(new Aura(AuraType.CONTROLLERS, new Effect(GameTag.EXTRA_BATTLECRIES_BASE, EffectOperator.SET, 1))));
			cards.Add("DALA_853", AnomalyOnePlayer("DALA_853e"));
			cards.Add("DALA_853e", HeistPlayerAura(new Aura(AuraType.CONTROLLERS, new Effect(GameTag.EXTRA_DEATHRATTLES_BASE, EffectOperator.SET, 1))));

			// [DALA_854] Anomaly - Nesting — After a minion is played, it gains "Deathrattle: Summon a 1/1 copy of this minion." (Unrecorded.
			// DALA_854e2 "Nested" on the played minion; its Deathrattle summons a new copy of the card where it died, made 1/1 by
			// DALA_854e3 "Popped Out" — a summoned copy isn't played, so it isn't Nested again.)
			cards.Add("DALA_854", AnomalyEachPlayer("DALA_854e"));
			cards.Add("DALA_854e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_MINION)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t is Minion m && m.Zone?.Type == Zone.PLAY) Give(g, s, m, "DALA_854e2");
					})
				}
			}));
			cards.Add("DALA_854e2", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion dead) || c.BoardZone.IsFull) return;
					var copy = (Minion)Entity.FromCard(c, dead.Card, new EntityData { [GameTag.CREATOR] = (t as IPlayable ?? dead).Id });
					Generic.SummonBlock.Invoke(g, copy, dead.LastBoardPosition, s);
					if (copy.Zone?.Type == Zone.PLAY) Give(g, t as IPlayable ?? dead, copy, "DALA_854e3");
				})
			}));
			cards.Add("DALA_854e3", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.SetAttackHealth(V("DALA_854", "copy_stats", 1)))
			}));

			// [DALA_855] Anomaly - Refreshing — At the end of each player's turn, that player draws until they have 5 cards. Live
			// (live-session-20261004-224614 91392-91443, 91570-91612): DALA_855e on each player; its TRIGGER block at the end of its
			// player's turn (TriggerKeyword TRIGGER_VISUAL), the draws.
			cards.Add("DALA_855", AnomalyEachPlayer("DALA_855e"));
			cards.Add("DALA_855e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					KeywordOverride = GameTag.TRIGGER_VISUAL, // (live 91570: TriggerKeyword TRIGGER_VISUAL — the card's tag; as DALA_728e)
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						for (int guard = 0; c.HandZone.Count < V("DALA_855", "hand", 5) && guard < 10; guard++)
							if (Generic.Draw(c) == null) break;
					})
				}
			}));

			// [DALA_856] Anomaly - Wild Magic — All spells cast an additional time. (targets chosen randomly) Live (live-session-20260929-
			// 122856 11390-11420, 12943-12985): DALA_856e on each player; after its player plays a spell, a new copy is cast at random
			// targets (Generic.CastPlayedCopy).
			cards.Add("DALA_856", AnomalyEachPlayer("DALA_856e"));
			cards.Add("DALA_856e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) => Generic.CastPlayedCopy(c, s as IPlayable, t))
				}
			}));

			// [DALA_857] Anomaly - Glittering — After a player plays a card that costs (8) or more, that player adds a Treasure to their hand.
			// (Unrecorded. "This anomaly generates The Dalaran Heist treasures" — wiki Anomaly - Glittering notes: the Heist treasure pool;
			// the Cost paid.)
			cards.Add("DALA_857", AnomalyEachPlayer("DALA_857e"));
			cards.Add("DALA_857e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => AnomalyPlayedCost(p) >= V("DALA_857", "cost", 8)),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (HeistRandomTreasure(g, s) is Card treasure) AddToHand(c, treasure);
					})
				}
			}));

			// [DALA_858] Anomaly - Fishy — Murlocs occasionally join the battle. (Unrecorded. Wiki Anomaly - Fishy notes: "there is a small
			// chance that at the start of both players' turn one random murloc will be summoned on their board" — the chance isn't given:
			// DOUBT, V default 25%.)
			cards.Add("DALA_858", AnomalyEachPlayer("DALA_858e"));
			cards.Add("DALA_858e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (c.BoardZone.IsFull || g.Random.Next(100) >= V("DALA_858", "chance %", 25)) return;
						Summon(g, c, RandomBoardMinion(g, x => x.IsRace(Race.MURLOC)), s);
					})
				}
			}));

			// [DALA_859] Anomaly - Gorged — Both players start the game with two extra cards and Mana Crystals. Live (live-session-20260929-
			// 122856 5961-6006): no enchantment — in its POWER block both players' RESOURCES +2 (no RESOURCES_USED: before the first turn
			// a crystal is a crystal), then the player draws 2, then the opponent 2.
			cards.Add("DALA_859", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Controller p in new[] { c, c.Opponent }) Generic.ChangeManaCrystal.Invoke(p, V("DALA_859", "crystals", 2), true);
					foreach (Controller p in new[] { c, c.Opponent })
						for (int i = 0; i < V("DALA_859", "cards", 2); i++) Generic.Draw(p);
				})
			}));

			// [DALA_860] Anomaly - Dragon Soul — After a player casts 3 spells in a turn, that player summons a 5/5 Dragon. (Unrecorded. Dragon
			// Soul's own rule (LOOT_209: its count on the source, back to 0 at the dragon and at the turn's end) and its 5/5, LOOT_209t —
			// wiki Anomaly - Dragon Soul's related card.)
			cards.Add("DALA_860", AnomalyEachPlayer("DALA_860e"));
			cards.Add("DALA_860e", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.AFTER_CAST)
					{
						TriggerSource = TriggerSource.FRIENDLY,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							s[GameTag.TAG_SCRIPT_DATA_NUM_1] += 1;
							if (s[GameTag.TAG_SCRIPT_DATA_NUM_1] < V("DALA_860", "spells", 3)) return;
							s[GameTag.TAG_SCRIPT_DATA_NUM_1] = 0;
							Summon(g, c, Cards.FromId("LOOT_209t"), s);
						})
					},
					new Trigger(TriggerType.TURN_END)
					{
						EitherTurn = true, NotInHistory = true,
						SingleTask = new CustomTask((g, c, s, t, stack) => { if (s[GameTag.TAG_SCRIPT_DATA_NUM_1] != 0) s[GameTag.TAG_SCRIPT_DATA_NUM_1] = 0; })
					})
			}));

			// [DALA_861] Anomaly - Ogreish — All minions have a 50% chance to attack the wrong enemy. (Unrecorded. The client's DALA_861e2
			// "Ogreish: 50% chance to attack the wrong enemy" — FORGETFUL, TRIGGER_VISUAL — on every minion of both boards, from DALA_861e's
			// aura; each one's attack trigger is Forgetful's / Mogor's (ForgetfulAttack: any other enemy character, even chances).)
			cards.Add("DALA_861", AnomalyOnePlayer("DALA_861e"));
			cards.Add("DALA_861e", HeistPlayerAura(new Aura(AuraType.BOARDS_EXCEPT_SOURCE, new IEffect[0]) { ShowAs = "DALA_861e2" }));
			cards.Add("DALA_861e2", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.ENCHANTMENT_TARGET,
					KeywordOverride = GameTag.FORGETFUL, // (live Ogre-Gang Outlaw: TriggerKeyword FORGETFUL)
					ProposedAttack = true, // (rule book, Proposed Attack Event: Forgetful)
					SingleTask = new CustomTask((g, c, s, t, stack) =>
						ForgetfulAttack(g, (s as Enchantment)?.Target as ICharacter, V("DALA_861", "chance %", 50)))
				}
			}));

			// [DALA_862] Anomaly - Reductive — At the end of each player's turn, reduce the cost of cards in their hand by (1). (Unrecorded.
			// The client's generic "Cost - 1" GBL_003e on each card, as Arcane's GBL_002e.)
			cards.Add("DALA_862", AnomalyEachPlayer("DALA_862e"));
			cards.Add("DALA_862e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						foreach (IPlayable card in c.HandZone.GetAll().ToList())
							for (int i = 0; i < V("DALA_862", "reduce", 1); i++) Give(g, s, card, "GBL_003e");
					})
				}
			}));

			// [DALA_863] Anomaly - Hallucinating — Both players start the game with 10 'Hallucinations' in their deck. (Unrecorded; no
			// enchantment in the client data. The card: UNG_856 — named "Hallucination" when the Heist came out, "Spore Hallucination" now;
			// wiki Anomaly - Hallucinating's related card, dbfId 42011.)
			cards.Add("DALA_863", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Controller p in new[] { c, c.Opponent })
						for (int i = 0; i < V("DALA_863", "cards", 10) && !p.DeckZone.IsFull; i++)
							Generic.ShuffleIntoDeck.Invoke(p, s, Entity.FromCard(p, Cards.FromId("UNG_856"), new EntityData { [GameTag.CREATOR] = s.Id }));
				})
			}));

			// [DALA_864] Anomaly - Growing — Minions gain +1/+1 at the end of their owner's turn. (Unrecorded. DALA_864e2 "Growth Anomaly:
			// Increased stats." on each of the player's minions, one per turn.)
			cards.Add("DALA_864", AnomalyEachPlayer("DALA_864e"));
			cards.Add("DALA_864e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						foreach (Minion m in c.BoardZone.GetAll().Where(m => !m.Untouchable).ToList()) Give(g, s, m, "DALA_864e2");
					})
				}
			}));
			cards.Add("DALA_864e2", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("DALA_864", "stats", 1))) }));

			// [DALA_865] Anomaly - Explosive — After a minion is summoned, it gains "Deathrattle: Deal 1 damage to all minions." (Unrecorded.
			// DALA_865e2 "Explosive" on each minion its player summons — played ones too.)
			cards.Add("DALA_865", AnomalyEachPlayer("DALA_865e"));
			cards.Add("DALA_865e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.MINIONS, EitherTurn = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t is Minion m && m.Zone?.Type == Zone.PLAY) Give(g, s, m, "DALA_865e2");
					})
				}
			}));
			cards.Add("DALA_865e2", new CardDef(new Power { DeathrattleTask = new DamageTask(V("DALA_865", "damage", 1), EntityType.ALLMINIONS) }));

			// [DALA_866] Anomaly - Rejuvenating — At the start of each player's turn, that player restores 2 Health to their hero. (Unrecorded.)
			cards.Add("DALA_866", AnomalyEachPlayer("DALA_866e"));
			cards.Add("DALA_866e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = new HealTask(V("DALA_866", "heal", 2), EntityType.HERO) }
			}));

			// [DALA_867] Anomaly - Infused — After a minion is summoned, it gains Taunt, Divine Shield, Rush, or Windfury at random.
			// (Live session-20261013-233341 86588-87180: one DALA_867e, on player 1, its TRIGGER block after any minion's summon on either
			// side — the keyword itself, no enchantment for it.)
			cards.Add("DALA_867", AnomalyOnePlayer("DALA_867e"));
			cards.Add("DALA_867e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.ALL_MINIONS, EitherTurn = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(t is Minion m) || m.Zone?.Type != Zone.PLAY) return;
						GameTag[] gifts = { GameTag.TAUNT, GameTag.DIVINE_SHIELD, GameTag.RUSH, GameTag.WINDFURY };
						GameTag gift = gifts[g.Random.Next(gifts.Length)];
						if (gift == GameTag.RUSH) m.IsRush = true; else m[gift] = 1;
					})
				}
			}));
		}
	}
}
