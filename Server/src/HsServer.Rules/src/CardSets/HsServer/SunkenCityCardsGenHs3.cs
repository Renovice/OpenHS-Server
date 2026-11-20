// HsServer addition (not upstream SabberStone): Voyage to the Sunken City, batch 2 (the cards with their own mechanics).
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
		/// <summary>The spells this player played while <paramref name="held"/> was in their hand (HsTag.HELD_SINCE_PLAY), in order.</summary>
		private static List<PlayHistoryEntry> SpellsWhileHeld(Controller c, IPlayable held) =>
			c.PlayHistory.Skip(held[HsTag.HELD_SINCE_PLAY]).Where(h => h.SourceCard?.Type == CardType.SPELL && h.SourceId != held.Id).ToList();

		/// <summary>Lady Naz'jar: after a Fire / Frost / Arcane spell, the version of that school (in hand).</summary>
		private static Trigger NazjarShift => new Trigger(TriggerType.AFTER_CAST)
		{
			TriggerActivation = TriggerActivation.HAND,
			TriggerSource = TriggerSource.FRIENDLY,
			Condition = new SelfCondition(p => p is Spell && (p[SPELL_SCHOOL] == FIRE || p[SPELL_SCHOOL] == FROST || p[SPELL_SCHOOL] == ARCANE)),
			SingleTask = new CustomTask((g, c, s, t, stack) =>
			{
				if (!(s is IPlayable me) || me.Zone?.Type != Zone.HAND || !(g.CurrentEventData?.EventSource is IPlayable spell)) return;
				string version = spell[SPELL_SCHOOL] == ARCANE ? "TID_709t" : spell[SPELL_SCHOOL] == FIRE ? "TID_709t2" : "TID_709t3";
				if (me.Card.Id != version) Generic.ChangeEntityBlock(c, me, Cards.FromId(version), false);
			})
		};

		/// <summary>Ring of Tides' shifting: in hand, after you cast a spell, it becomes a copy of it that costs (1).</summary>
		private static ISimpleTask RingShift => new CustomTask((g, c, s, t, stack) =>
		{
			IPlayable ring = s is Enchantment e ? e.Target as IPlayable : s as IPlayable;
			if (ring?.Zone?.Type != Zone.HAND || !(g.CurrentEventData?.EventSource is IPlayable spell) || spell.Id == ring.Id) return;
			IPlayable now = Generic.ChangeEntityBlock(c, ring, spell.Card, false);
			if (now == null) return;
			if (now.AppliedEnchantments?.Any(x => x.Card.Id == "TSC_641tae") != true) Give(g, s, now, "TSC_641tae");
			if (now.AppliedEnchantments?.Any(x => x.Card.Id == "TSC_641tde") != true) Give(g, s, now, "TSC_641tde");
		});

		private static void SunkenCityBatch2(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Neutral
			// [TID_712] Neptulon the Tidehunter (10) — Colossal +2, Rush, Windfury. Whenever Neptulon attacks, if you control any
			// Hands, they attack instead.
			cards.Add("TID_712", new CardDef(new Power
			{
				AttackInstead = (g, attacker, defender) =>
				{
					var hands = LimbsOf(attacker.Controller, "TID_712").ToList();
					if (hands.Count == 0) return null;
					return () =>
					{
						foreach (Minion hand in hands)
							if (!defender.IsDead && defender[GameTag.ZONE] == (int)Zone.PLAY && !hand.IsDead && hand.Zone?.Type == Zone.PLAY) EffectAttack(g, hand, defender);
					};
				}
			}));
			// [TID_744] Coilfang Constrictor (4) — Battlecry: Look at 3 cards in your opponent's hand and choose one. It can't be
			// played next turn.
			cards.Add("TID_744", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.DiscoverFromCards(c, s, Generic.PickFrom(c.Opponent.HandZone, g, null), (g2, c2, s2, card) => Give(g2, s2, card, "TID_744e")))
			}));
			cards.Add("TID_744e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.CANT_PLAY, EffectOperator.SET, 1)),
				// lasts through its owner's next turn
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					EitherTurn = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Enchantment e && e.Target is IPlayable card && g.CurrentPlayer == card.Controller) e.Remove();
					})
				}
			}));
			// [TSC_032] Blademaster Okani (4) — Battlecry: Secretly choose to Counter the next minion or spell your opponent plays
			// while this is alive.
			cards.Add("TSC_032", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int okani = s.Id;
					Generic.CreateChoiceEntities(c, s, OfferCards(c, s, new[] { "TSC_032t", "TSC_032t2" }), ChoiceAction.STACK, new CustomTask((g2, c2, s2, pick, st) =>
					{
						if (pick == null || !(g2.IdEntityDic.TryGetValue(okani, out IPlayable me) && me.Zone?.Type == Zone.PLAY)) return;
						Generic.AddEnchantmentBlock(g2, Cards.FromId("TSC_032e3"), me, me, pick.Card.Id == "TSC_032t" ? 1 : 2, 0, 0);
						Generic.NoteAffected(pick, okani);
						if (pick.Zone?.Type == Zone.SETASIDE) c2.GraveyardZone.Add(pick.Zone.Remove(pick));
					}));
				})
			}));
			cards.Add("TSC_032e3", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_CARD)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || !(g.CurrentEventData?.EventSource is IPlayable played)) return;
						bool minions = e[GameTag.TAG_SCRIPT_DATA_NUM_1] == 1;
						if (minions && played is Minion) played[GameTag.CANT_PLAY] = 1;
						else if (!minions && played is Spell spell) spell.IsCountered = true;
						else return;
						e.Remove();
					})
				}
			}));
			// [TSC_052] School Teacher (4) — Battlecry: Add a 1/1 Nagaling to your hand. Discover a spell that costs (3) or less to
			// teach it. (The Nagaling's "Cast {0}." names the entity in its TAG_SCRIPT_DATA_ENT_1: the client's
			// DRUSTVAR_HORROR text builder.)
			cards.Add("TSC_052", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					IPlayable nagaling = Entity.FromCard(c, Cards.FromId("TSC_052t"));
					if (!Generic.AddHandPhase.Invoke(c, nagaling)) return;
					var pool = Cards.FormatTypeCards(g.FormatType).Where(x => x.Type == CardType.SPELL && x.Collectible && x.Cost <= V("TSC_052", "cost", 3) &&
						(x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL)));
					Generic.CreateChoiceEntities(c, s, OfferCards(c, s, Pick(g, pool, 3).Select(x => x.Id)), ChoiceAction.STACK, new CustomTask((g2, c2, s2, lesson, st) =>
					{
						if (lesson == null) return;
						Generic.CarrySpell(nagaling, lesson); // the taught spell stays set aside; the Nagaling targets like it
					}));
				})
			}));
			cards.Add("TSC_052t", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Generic.CastCarriedSpells(c, (IPlayable)s, t)) }));
			// [TSC_209] Whirlpool (8) — Destroy all minions and all copies of them (wherever they are).
			cards.Add("TSC_209", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.BoardZone.Concat(c.Opponent.BoardZone).Where(m => !m.IsDead).ToList();
					var ids = minions.Select(m => m.Card.Id).ToHashSet();
					foreach (Minion m in minions) Generic.Destroy(m, s);
					foreach (Controller p in new[] { c, c.Opponent })
					{
						foreach (IPlayable copy in p.HandZone.Where(x => ids.Contains(x.Card.Id)).ToList())
						{
							Generic.NoteAffected(copy, s.Id);
							p.GraveyardZone.Add(p.HandZone.Remove(copy));
						}
						foreach (IPlayable copy in p.DeckZone.Where(x => ids.Contains(x.Card.Id)).ToList())
							Generic.DeckCardToGraveyard(copy, s.Id, suppressAnimation: false);
					}
				})
			}));
			// [TSC_641] Queen Azshara (5) — Battlecry: If you've cast three spells while holding this, choose an Ancient Relic.
			cards.Add("TSC_641", new CardDef(new Power
			{
				Trigger = WhileHolding(TriggerType.CAST_SPELL, p => p is Spell),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!Held(s)) return;
					string[] relics = { "TSC_641ta", "TSC_641tb", "TSC_641tc", "TSC_641td" };
					Generic.CreateChoiceEntities(c, s, OfferCards(c, s, relics), ChoiceAction.HAND, null);
				})
			}));
			// the Ancient Relics
			cards.Add("TSC_641ta", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST) { TriggerActivation = TriggerActivation.HAND, TriggerSource = TriggerSource.FRIENDLY, SingleTask = RingShift }
			}));
			cards.Add("TSC_641tae", new CardDef(new Power { Trigger = new Trigger(TriggerType.AFTER_CAST) { TriggerSource = TriggerSource.FRIENDLY, SingleTask = RingShift } }));
			cards.Add("TSC_641tde", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(V("TSC_641", "relic cost", 1))) }));
			cards.Add("TSC_641tb", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var pool = Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION && x[Generic.Colossal] > 0 && x.Collectible).ToList();
					if (pool.Count == 0 || c.HandZone.IsFull) return;
					IPlayable p = Entity.FromCard(c, pool[g.Random.Next(pool.Count)]);
					if (Generic.AddHandPhase.Invoke(c, p)) Give(g, s, p, "TSC_641tde");
				})
			}));
			cards.Add("TSC_641tc", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						Generic.DamageCharFunc((IPlayable)s, c.Opponent.Hero, V("TSC_641tc", "damage", 2), false);
						LoseDurability(s);
					})
				}
			}));
			cards.Add("TSC_641td", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TSC_641td", "spells", 5); i++)
					{
						if (!(RandomCard(g, x => x.Type == CardType.SPELL) is Card spell)) break;
						IPlayable p = Entity.FromCard(c, spell);
						if (Generic.ShuffleIntoDeck.Invoke(c, s, p)) Give(g, s, p, "TSC_641tde");
					}
					for (int i = 0; i < V("TSC_641td", "draws", 2); i++) Generic.Draw(c);
				})
			}));
			// [TSC_908] Sir Finley, Sea Guide (1) — Battlecry: Swap your hand with the bottom of your deck.
			cards.Add("TSC_908", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var hand = c.HandZone.ToList();
					var bottom = c.DeckZone.Take(hand.Count).ToList();
					foreach (IPlayable card in hand)
					{
						c.HandZone.Remove(card);
						c.DeckZone.Add(card, 0);
						if (g.History) g.PowerHistory.Add(PowerHistoryBuilder.HideEntity(card));
					}
					foreach (IPlayable card in bottom) Generic.Draw(c, card);
				})
			}));
			// [TSC_934] Pirate Admiral Hooktusk (7) — Battlecry: If you've summoned 7 other Pirates this game, plunder the enemy!
			cards.Add("TSC_934", new CardDef(new Power
			{
				ThresholdCount = p => Generic.OthersSummonedThisGame(p, x => x.IsRace(Race.PIRATE)), // ("other": the shared count)
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (Generic.ThresholdMet((IPlayable)s)) ChooseAndCast("TSC_934t", "TSC_934t2", "TSC_934t3").Process(g, c, s, null);
				})
			}));
			cards.Add("TSC_934t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TSC_934t", "cards", 5) && c.Opponent.DeckZone.Count > 0 && !c.HandZone.IsFull; i++)
					{
						IPlayable taken = c.Opponent.DeckZone[g.Random.Next(c.Opponent.DeckZone.Count)];
						c.Opponent.DeckZone.Remove(taken);
						Generic.AddHandPhase.Invoke(c, Generic.GiveTo(taken, c, Zone.HAND)); // (CONTROLLER_AND_ZONE_CHANGE, live)
					}
				})
			}));
			cards.Add("TSC_934t2", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TSC_934t2", "cards", 2) && c.Opponent.HandZone.Count > 0 && !c.HandZone.IsFull; i++)
					{
						IPlayable taken = c.Opponent.HandZone[g.Random.Next(c.Opponent.HandZone.Count)];
						c.Opponent.HandZone.Remove(taken);
						Generic.AddHandPhase.Invoke(c, Generic.GiveTo(taken, c, Zone.HAND)); // (CONTROLLER_AND_ZONE_CHANGE, live)
					}
				})
			}));
			cards.Add("TSC_934t3", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = c.Opponent.BoardZone.Where(m => !m.IsDead && !m.Untouchable).ToList();
					if (enemies.Count == 0 || c.BoardZone.IsFull) return;
					int top = enemies.Max(m => m.AttackDamage);
					var highest = enemies.Where(m => m.AttackDamage == top).ToList();
					new ControlTask(EntityType.TARGET).Process(g, c, s, highest[g.Random.Next(highest.Count)]);
				})
			}));
			// [TSC_962] Gigafin (8) — Colossal +1. Battlecry: Devour all enemy minions. Deathrattle: Spit them back out.
			// Live (vfx-decks 110421-110470 / 110959): the devoured minions are DESTROYED (2747 + TO_BE_DESTROYED in its OverrideDeath effect;
			// they really die — their Deathrattles and Reborn run), Gigafin gets ONE TSC_962e2 holding them; its Deathrattle summons new
			// copies at base stats for the opponent (OverrideSpawn, CREATOR = Gigafin); the Maw's Deathrattle makes that hold empty.
			cards.Add("TSC_962", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					var prey = c.Opponent.BoardZone.Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
					foreach (Minion m in prey)
						Generic.Destroy(m, me, many: true);
					Generic.AddEnchantmentBlock(g, Cards.FromId("TSC_962e2"), me, me, 0, 0, 0);
					if (me.AppliedEnchantments?.LastOrDefault(e => e.Card.Id == "TSC_962e2") is Enchantment hold)
					{
						// (the shared "eaten" hold: EATEN_COUNT / EATEN_BASE + i = the entity)
						int n = System.Math.Min(prey.Count, 16);
						hold[HsTag.EATEN_COUNT] = n;
						for (int i = 0; i < n; i++) hold[HsTag.EATEN_BASE + i] = prey[i].Id;
					}
				})
			}));
			cards.Add("TSC_962e2", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					// an enchantment's Deathrattle: source = the dead Gigafin, target = the enchantment
					if (!(t is Enchantment e) || e[HsTag.EATEN_GONE] > 0) return;
					for (int i = 0; i < e[HsTag.EATEN_COUNT]; i++)
						if (g.IdEntityDic.TryGetValue(e[HsTag.EATEN_BASE + i], out IPlayable eaten))
							Summon(g, c.Opponent, eaten.Card, s); // (a new minion of that card, base stats)
				})
			}));
			// [TSC_962t] Gigafin's Maw — Taunt. Deathrattle: Permanently destroy all minions inside Gigafin.
			cards.Add("TSC_962t", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion gigafin in c.BoardZone.Where(m => m.Card.Id == "TSC_962").ToList())
						foreach (Enchantment e in gigafin.AppliedEnchantments?.Where(x => x.Card.Id == "TSC_962e2").ToList() ?? new List<Enchantment>())
							e[HsTag.EATEN_GONE] = 1; // (live: the Maw's Deathrattle block is empty — the hold is engine state)
				})
			}));
			// [TSC_218] Lady S'theno (3) — Immune while attacking. After you cast a spell, attack the lowest Health enemy.
			cards.Add("TSC_218", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me) || me.IsDead) return;
						var enemies = Enemies(c).Where(e => !(e is Minion m && m.Untouchable)).ToList();
						if (enemies.Count == 0) return;
						int low = enemies.Min(e => e.Health);
						var lowest = enemies.Where(e => e.Health == low).ToList();
						EffectAttack(g, me, lowest[g.Random.Next(lowest.Count)]);
					})
				}
			}));
			// [TSC_219] Xhilag of the Abyss (7) — Colossal +4. At the start of your turn, increase the damage of Xhilag's Stalks by 1.
			cards.Add("TSC_219", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						foreach (Minion stalk in LimbsOf(c, "TSC_219").ToList())
						{
							stalk[GameTag.TAG_SCRIPT_DATA_NUM_1] = stalk[GameTag.TAG_SCRIPT_DATA_NUM_1] + V("TSC_219", "increase", 1);
							Give(g, s, stalk, "TSC_219e");
						}
					})
				}
			}));
			cards.Add("TSC_219e", new CardDef(new Power()));
			// [TSC_219t] Xhilag's Stalk — At the end of your turn, deal @ damage to a random enemy (@: its TAG_SCRIPT_DATA_NUM_1).
			foreach (string stalk in new[] { "TSC_219t", "TSC_219t2", "TSC_219t3", "TSC_219t4" })
				cards.Add(stalk, new CardDef(new Power
				{
					Trigger = new Trigger(TriggerType.TURN_END)
					{
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							var enemies = Enemies(c).Where(e => !(e is Minion m && m.Untouchable)).ToList();
							if (enemies.Count > 0) Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], s[GameTag.TAG_SCRIPT_DATA_NUM_1], false);
						})
					}
				}));
			// [TSC_620] Spitelash Siren (4) — After you play a Naga, refresh two Mana Crystals (then switch to spell!) / after you
			// cast a spell, refresh two Mana Crystals (then switch to Naga!). (The client shows the text USE_ALTERNATE_CARD_TEXT
			// picks: 0 Naga, 1 spell.)
			cards.Add("TSC_620", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.AFTER_PLAY_MINION)
					{
						TriggerSource = TriggerSource.FRIENDLY,
						Condition = new SelfCondition(IsNaga),
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (s[GameTag.USE_ALTERNATE_CARD_TEXT] != 0) return;
							RefreshMana(c, s.Card[GameTag.TAG_SCRIPT_DATA_NUM_1]);
							s[GameTag.USE_ALTERNATE_CARD_TEXT] = 1;
						})
					},
					new Trigger(TriggerType.AFTER_CAST)
					{
						TriggerSource = TriggerSource.FRIENDLY,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (s[GameTag.USE_ALTERNATE_CARD_TEXT] != 1) return;
							RefreshMana(c, s.Card[GameTag.TAG_SCRIPT_DATA_NUM_1]);
							s[GameTag.USE_ALTERNATE_CARD_TEXT] = 0;
						})
					})
			}));
			// [TSC_660] Nellie, the Great Thresher (7) — Colossal +1. Battlecry: Discover 3 Pirates to crew Nellie's Ship! (Each
			// crew member: a TSC_660e2 on the Ship capturing it.)
			cards.Add("TSC_660", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => CrewShip(g, c, s, V("TSC_660", "pirates", 3)))
			}));
			cards.Add("TSC_660e2", new CardDef(new Power()));
			// [TSC_660t] Nellie's Pirate Ship — Taunt. Deathrattle: Add Nellie's Pirate crew to your hand. They cost (1).
			cards.Add("TSC_660t", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion ship)) return;
					foreach (Enchantment e in ship.AppliedEnchantments?.Where(x => x.Card.Id == "TSC_660e2").ToList() ?? new List<Enchantment>())
					{
						if (e.CapturedCard == null || c.HandZone.IsFull) continue;
						IPlayable crew = Entity.FromCard(c, e.CapturedCard);
						if (Generic.AddHandPhase.Invoke(c, crew)) Give(g, s, crew, "TSC_660e");
					}
				})
			}));
			cards.Add("TSC_660e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(V("TSC_660t", "cost", 1))) }));

			// ---------------------------------------------------------------- Class cards
			// [TID_709] Lady Naz'jar (5) — While in your hand, this transforms after you cast a Fire, Frost, or Arcane spell.
			// (Arcane: TID_709t, Fire: TID_709t2, Frost: TID_709t3; user 2026-10-07: each form transforms again after a spell of
			// another of those schools — the 2026-10-06 "stays in her first form" was corrected.)
			cards.Add("TID_709", new CardDef(new Power { Trigger = NazjarShift }));
			cards.Add("TID_709t", new CardDef(new Power
			{
				Trigger = NazjarShift,
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.Where(p => p is Spell), "TID_709e"))
			}));
			cards.Add("TID_709e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("TID_709t", "discount", 1))) }));
			cards.Add("TID_709t2", new CardDef(new Dictionary<PlayReq, int>
				{ { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				Trigger = NazjarShift,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					var neighbors = m.Zone?.Type == Zone.PLAY ? Neighbors(m).ToList() : new List<Minion>();
					Generic.DamageCharFunc((IPlayable)s, m, V("TID_709t2", "damage", 5), false);
					foreach (Minion n in neighbors) Generic.DamageCharFunc((IPlayable)s, n, V("TID_709t2", "adjacent", 2), false);
				})
			}));
			cards.Add("TID_709t3", new CardDef(new Power { Trigger = NazjarShift, PowerTask = new ArmorTask(V("TID_709t3", "armor", 8)) }));
			// [TID_718] Immolate (4) — Light every card in the opponent's hand on fire. In 3 turns, any still in hand are destroyed!
			// (User 2026-10-06: it counts at the end of each of your turns, the cast turn first; at the third the cards burn.
			// TID_718e on the caster counts; TID_718e2 marks the cards.)
			cards.Add("TID_718", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					GiveAll(g, s, c.Opponent.HandZone, "TID_718e2");
					Generic.AddEnchantmentBlock(g, Cards.FromId("TID_718e"), (IPlayable)s, c, 0, 0, 0);
				})
			}));
			cards.Add("TID_718e2", new CardDef(new Power()));
			cards.Add("TID_718e", FireCounter("TID_718e2", "TID_718", opponentsHand: true)); // shared with Burndown (IslandCardsGenHs3.cs)
			// [TID_719] Commander Ulthok (5) — Battlecry: Your opponent's cards cost Health instead of Mana next turn.
			cards.Add("TID_719", new CardDef(new Power { PowerTask = new AddEnchantmentTask("TID_719e", EntityType.OP_CONTROLLER) }));
			cards.Add("TID_719e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.OP_HAND, new Effect(GameTag.CARD_COSTS_HEALTH, EffectOperator.SET, 1))
				{
					Condition = new SelfCondition(p => p.Game.CurrentPlayer == p.Controller), Restless = true
				},
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					EitherTurn = true,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Enchantment e && g.CurrentPlayer == c) e.Remove(); }) // (c: the player it is on — Trigger.Side)
				}
			}));
			// [TID_920] Drown (4) — Put an enemy minion on the bottom of your deck.
			cards.Add("TID_920", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_MINION_TARGET, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m) PutMinionOnBottom(c, m, s); })
			}));
			// [TID_949] Front Lines (9) — Summon a minion from each player's deck. Repeat until either side of the battlefield is full.
			cards.Add("TID_949", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int guard = 0; guard < 14 && !c.BoardZone.IsFull && !c.Opponent.BoardZone.IsFull; guard++)
					{
						Minion mine = SummonFromDeck(g, c, s, p => true);
						Minion theirs = SummonFromDeck(g, c.Opponent, s, p => true);
						if (mine == null && theirs == null) return;
					}
				})
			}));
			// [TSC_006] Multi-Strike (2) — Give your hero +2 Attack this turn. They may attack an additional enemy minion.
			cards.Add("TSC_006", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Give(g, s, c.Hero, "TSC_006e");
					Give(g, s, c.Hero, "TSC_006e2");
					c.Hero.ExtraAttacksThisTurn = c.Hero.ExtraAttacksThisTurn + V("TSC_006", "attacks", 1);
				})
			}));
			cards.Add("TSC_006e", OneTurn(Effects.Attack_N(V("TSC_006", "attack", 2))));
			cards.Add("TSC_006e2", OneTurn(new Effect(HsTag.EXTRA_ATTACK_MINIONS_ONLY, EffectOperator.SET, 1)));
			// [TSC_074] Kotori Lightblade (2) — After you cast a Holy spell on this, cast it again on another friendly minion.
			cards.Add("TSC_074", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY_SPELL_CASTED_ON_THE_OWNER,
					Condition = new SelfCondition(p => IsSchoolSpell(p, HOLY)),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(g.CurrentEventData?.EventSource is Spell spell)) return;
						var others = c.BoardZone.Where(m => m.Id != s.Id && !m.IsDead && !m.Untouchable).ToList();
						if (others.Count == 0) return;
						new CastRandomSpellTask(preferTarget: true) { Spell = spell.Card }.Process(g, c, s, others[g.Random.Next(others.Count)]);
					})
				}
			}));
			// [TSC_087] Commander Sivara (4) — Battlecry: If you've cast three spells while holding this, add those spells back to
			// your hand. (User 2026-10-06: it remembers the first three spells, then says Ready and adds no more; back in hand
			// after being played, it counts again.)
			cards.Add("TSC_087", new CardDef(new Power
			{
				Trigger = WhileHolding(TriggerType.CAST_SPELL, p => p is Spell),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!Held(s)) return;
					foreach (PlayHistoryEntry h in SpellsWhileHeld(c, (IPlayable)s).Take(V("TSC_087", "spells", 3))) AddToHand(c, h.SourceCard);
				})
			}));
			// [TSC_630] Wrathspine Enchanter (7) — Battlecry: Cast a copy of a Fire, Frost, and Nature spell in your hand (targets
			// chosen randomly).
			cards.Add("TSC_630", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (int school in new[] { FIRE, FROST, NATURE })
					{
						var spells = c.HandZone.Where(p => IsSchoolSpell(p, school)).ToList();
						if (spells.Count == 0) continue;
						if (c.Hero.IsDead || c.Opponent.Hero.IsDead) return;
						new CastRandomSpellTask { Spell = spells[g.Random.Next(spells.Count)].Card }.Process(g, c, s, null);
					}
				})
			}));
			// [TSC_658] Hedra the Heretic (7) — Battlecry: For each spell you've cast while holding this, summon a minion of that
			// spell's Cost.
			cards.Add("TSC_658", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (PlayHistoryEntry h in SpellsWhileHeld(c, (IPlayable)s))
					{
						if (c.BoardZone.IsFull) return;
						Summon(g, c, RandomMinionOfCost(g, h.Cost), s);
					}
				})
			}));
			// [TSC_946] Urchin Spines (1) — Your spells this turn are Poisonous. (HsTag.SPELLS_POISONOUS, like Professor Slate.)
			cards.Add("TSC_946", new CardDef(new Power { PowerTask = new AddEnchantmentTask("TSC_946e", EntityType.CONTROLLER) }));
			cards.Add("TSC_946e", new CardDef(new Power
			{
				Enchant = new Enchant { IsOneTurnEffect = true },
				Aura = new Aura(AuraType.CONTROLLER, new Effect(HsTag.SPELLS_POISONOUS, EffectOperator.ADD, 1))
			}));
		}

		/// <summary>Nellie: Discover a Pirate <paramref name="left"/> times; each pick is captured on Nellie's Ship (TSC_660e2).</summary>
		private static void CrewShip(Game g, Controller c, IEntity nellie, int left)
		{
			if (left <= 0) return;
			var pool = Cards.FormatTypeCards(g.FormatType).Where(x => x.Type == CardType.MINION && x.Collectible && x.IsRace(Race.PIRATE) &&
				(x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL)));
			Generic.CreateChoiceEntities(c, nellie, OfferCards(c, nellie, Pick(g, pool, 3).Select(x => x.Id)), ChoiceAction.STACK, new CustomTask((g2, c2, s2, pick, st) =>
			{
				if (pick == null) return;
				Minion ship = LimbsOf(c2, "TSC_660").FirstOrDefault();
				if (ship != null) Generic.AddEnchantmentBlock(g2, Cards.FromId("TSC_660e2"), (IPlayable)s2, ship, 0, 0, pick.Id);
				Generic.NoteAffected(pick, s2.Id);
				if (pick.Zone?.Type == Zone.SETASIDE) c2.GraveyardZone.Add(pick.Zone.Remove(pick));
				CrewShip(g2, c2, s2, left - 1);
			}));
		}
	}
}
