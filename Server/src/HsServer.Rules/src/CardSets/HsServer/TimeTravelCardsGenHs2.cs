// HsServer addition (not upstream SabberStone): Across the Timeways — chunk 2: Druid, Hunter and Mage (spec: research spec_chunk2).
// Shared pieces: Generic.DamageWithExcess, "Advance to the present!" locations (LocationActions), Restless CONTROLLER / HEROPOWER
// auras (Aura.RenewAll), HsTag.SPELL_CASTS_TWICE (CastSpell), HsTag.PLAYED_HAND_COUNT (PlayCard). Rewind: see TimeTravelCardsGenHs.cs.
// Every number goes through CardValues (V).
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
		/// <summary>"From the past" cards (Cards.FromThePast: a set not in Standard now — user ruling: not Wild cards reprinted in
		/// Standard), whatever the game's format, under the generation rules.</summary>
		private static IEnumerable<Card> PastCards(Game g, Controller c, Func<Card, bool> where) =>
			Cards.GenerationPool(g, c, FormatType.FT_WILD).Where(x => Cards.FromThePast(x) && where(x));

		/// <summary>"Draw until you have N cards" (an empty deck stops it: no Fatigue — doubt).</summary>
		private static void DrawUntil(Controller c, int n)
		{
			for (int guard = 0; c.HandZone.Count < n && guard < 10 && c.DeckZone.Count > 0; guard++)
				if (Generic.Draw(c) == null) break;
		}

		/// <summary>The minions summoned by Zin-Azshari / spells of the Well: the Lady Azshara family (TIME_211's related locations).</summary>
		private static void AzsharaEmpower(Game g, Controller c, IEntity s, string empower, string destroy)
		{
			foreach (IPlayable p in c.DeckZone.GetAll().Concat(c.HandZone.GetAll()).Concat(c.BoardZone.GetAll()).ToList())
			{
				if (p.Card.Id == empower && Cards.TryFromId(empower + "t") is Card empowered) Generic.ChangeEntityBlock(c, p, empowered, false);
				else if (p.Card.Id == destroy || p.Card.Id == destroy + "t") Generic.Destroy(p, (IPlayable)s);
			}
		}

		private static void TimeTravelChunk2(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Druid
			// [END_009] Splintered Reality (4) — Summon two 2/2 Treants. They gain +1/+1 for each friendly Treant that died this game.
			Func<IPlayable, int> treantsDied = p => p.Controller.DiedThisGameCards.Count(x => x is Minion && SelfCondition.IsTreantCard(x.Card));
			cards.Add("END_009", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				HandCounter = treantsDied,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = treantsDied((IPlayable)s);
					for (int i = 0; i < V("END_009", "treants", 2); i++)
						if (SummonGet(g, c, Cards.FromId("END_009t"), s) is Minion m && n > 0) Generic.AddEnchantmentBlock(g, Cards.FromId("END_009e"), (IPlayable)s, m, n, n, 0);
				})
			}));
			cards.Add("END_009e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0), Effects.Health_N(0)) { UseScriptTag = true } }));
			// [TIME_023] Contingency (3) — Draw the bottom two cards from your deck.
			cards.Add("TIME_023", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TIME_023", "draws", 2); i++)
					{
						if (c.DeckZone.Count > 0) Generic.Draw(c, c.DeckZone[0]); // slot 0 is the bottom
						else Generic.Draw(c);
					}
				})
			}));
			// [TIME_033] Druid of Regrowth (6) — Rewind. Battlecry: Cast 2 random Nature spells.
			cards.Add("TIME_033", new CardDef(new Power
			{
				PowerTask = RewindPower((g, c, s, t) =>
				{
					for (int i = 0; i < V("TIME_033", "spells", 2); i++)
						if (RandomCard(g, x => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == NATURE) is Card spell)
							new CastRandomSpellTask().Process(g, c, s, Entity.FromCard(c, spell, new EntityData { [GameTag.CREATOR] = s.Id }));
				})
			}));
			// [TIME_211] Lady Azshara (5) — Fabled. Choose One - Empower Zin-Azshari; or The Well of Eternity. (The other gets destroyed!)
			// (wiki: wherever they are; an empowered one keeps its state; both options destroy both)
			cards.Add("TIME_211", new CardDef(new Power()));
			cards.Add("TIME_211a", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => AzsharaEmpower(g, c, s, "TIME_211t2", "TIME_211t1")) }));
			cards.Add("TIME_211b", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => AzsharaEmpower(g, c, s, "TIME_211t1", "TIME_211t2")) }));
			// [TIME_211t1] The Well of Eternity — Location: Fill your hand with random Temporary spells. (t1t: they cast twice)
			void Well(Game g, Controller c, IEntity s, bool twice)
			{
				for (int guard = 0; guard < 10 && !c.HandZone.IsFull; guard++)
				{
					if (!(RandomCard(g, x => x.Type == CardType.SPELL) is Card spell)) break;
					IPlayable got = Entity.FromCard(c, spell, new EntityData { [GameTag.CREATOR] = s.Id });
					if (!Generic.AddHandPhase.Invoke(c, got)) break;
					Generic.MakeTemporary(got, s);
					if (twice) Give(g, s, got, "TIME_211t1te");
				}
			}
			cards.Add("TIME_211t1", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Well(g, c, s, false)) }));
			cards.Add("TIME_211t1t", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Well(g, c, s, true)) }));
			cards.Add("TIME_211t1te", new CardDef(new Power { Enchant = new Enchant(new Effect(HsTag.SPELL_CASTS_TWICE, EffectOperator.SET, 1)) }));
			// [TIME_211t2] Zin-Azshari — Location: Summon a copy of a friendly minion. (t2t: with its stats doubled)
			void Zin(Game g, Controller c, IEntity s, IPlayable t, bool doubled)
			{
				if (!(t is Minion m) || c.BoardZone.IsFull) return;
				Generic.Copy(c, s, m, Zone.PLAY, position: m.ZonePosition + 1, prepare: copy =>
				{
					if (doubled && copy is Minion cm) Generic.AddEnchantmentBlock(g, Cards.FromId("TIME_211t2te"), (IPlayable)s, cm, m.AttackDamage, m.BaseHealth, 0);
				});
			}
			cards.Add("TIME_211t2", new CardDef(LocationFriendlyMinion, new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Zin(g, c, s, t, false)) }));
			cards.Add("TIME_211t2t", new CardDef(LocationFriendlyMinion, new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Zin(g, c, s, t, true)) }));
			cards.Add("TIME_211t2te", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(0), Effects.Health_N(0)) { UseScriptTag = true } }));
			// [TIME_701] Waveshaping (1) — Discover a card from your deck. The others get put on the bottom.
			cards.Add("TIME_701", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var offers = Generic.PickFromDeck(c, p => true);
					var ids = offers.Select(p => p.Id).ToList();
					Generic.DiscoverFromDeck(c, s, offers, (g2, c2, s2, card) =>
					{
						Generic.Draw(c2, card);
						foreach (int id in ids.Where(id => id != card.Id))
							if (g2.IdEntityDic.TryGetValue(id, out IPlayable other) && other.Zone?.Type == Zone.DECK && other.Controller == c2)
							{
								c2.DeckZone.Remove(other);
								c2.DeckZone.Add(other, 0);
							}
					});
				})
			}));
			// [TIME_702] Ebb and Flow (2) — Deal $3 damage. If you played a minion while holding this, gain 5 Armor.
			cards.Add("TIME_702", new CardDef(TargetAny, new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_MINION)
				{
					TriggerActivation = TriggerActivation.HAND,
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) => s[GameTag.TAG_SCRIPT_DATA_NUM_2] = 1)
				},
				GlowWhile = p => p[GameTag.TAG_SCRIPT_DATA_NUM_2] > 0,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter ch) Generic.DamageCharFunc((IPlayable)s, ch, V("TIME_702", "damage", 3), true);
					if (s[GameTag.TAG_SCRIPT_DATA_NUM_2] > 0) new ArmorTask(V("TIME_702", "armor", 5)).Process(g, c, s, null);
				})
			}));
			// [TIME_703] Endangered Dodo (5) — Taunt. Battlecry: If you have 10 or less Health, gain +5/+5 and summon a copy of this.
			cards.Add("TIME_703", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.Hero.Health <= V("TIME_703", "health", 10), new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					Give(g, s, me, "TIME_703e");
					if (!c.BoardZone.IsFull && me.Zone?.Type == Zone.PLAY) Generic.Copy(c, s, me, Zone.PLAY);
				}))
			}));
			cards.Add("TIME_703e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TIME_703", "stats", 5))) }));
			// [TIME_704] Highborne Mentor (7) — Battlecry: Get a 2/2 Pupil. Discover a spell that costs (7) or more from the past to teach it.
			// (the School Teacher pattern: the Pupil's TAG_SCRIPT_DATA_ENT_1 = the taught spell, set aside)
			cards.Add("TIME_704", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					IPlayable pupil = Entity.FromCard(c, Cards.FromId("TIME_704t"), new EntityData { [GameTag.CREATOR] = s.Id });
					if (!Generic.AddHandPhase.Invoke(c, pupil)) return;
					int pupilId = pupil.Id;
					var pool = PastCards(g, c, x => x.Type == CardType.SPELL && x.Cost >= V("TIME_704", "cost", 7) && (x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL)));
					Generic.CreateChoiceEntities(c, s, OfferCards(c, s, Pick(g, pool, 3).Select(x => x.Id)), ChoiceAction.STACK, new CustomTask((g2, c2, s2, lesson, st) =>
					{
						if (lesson != null && g2.IdEntityDic.TryGetValue(pupilId, out IPlayable p)) Generic.CarrySpell(p, lesson);
					}));
				})
			}));
			cards.Add("TIME_704t", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Generic.CastCarriedSpells(c, (IPlayable)s, t)) }));
			// [TIME_705] Krona, Keeper of Eons (6) — Taunt. Battlecry: Set the Costs of the bottom 5 cards of your deck to (1).
			cards.Add("TIME_705", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable card in c.DeckZone.GetAll().Take(V("TIME_705", "cards", 5)).ToList()) Give(g, s, card, "GBL_001e");
				})
			}));
			// [TIME_707] Alternate Reality (2) — Replace your hand and deck with random Choose One cards from the past. They cost (1) less.
			cards.Add("TIME_707", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var pool = PastCards(g, c, x => x[(GameTag)443] > 0 || x.ChooseOne).ToList();
					if (pool.Count == 0) return;
					foreach (IPlayable card in c.HandZone.GetAll().Where(p => p.Id != s.Id).ToList())
						if (Generic.ChangeEntityBlock(c, card, pool[g.Random.Next(pool.Count)], true) is IPlayable now) Give(g, s, now, "GBL_003e");
					foreach (IPlayable card in c.DeckZone.GetAll().ToList())
					{
						IPlayable fresh = Entity.FromCard(c, pool[g.Random.Next(pool.Count)], new EntityData { [GameTag.CREATOR] = s.Id });
						int at = card.ZonePosition;
						c.DeckZone.Remove(card);
						c.SetasideZone.Add(card);
						c.DeckZone.Add(fresh, Math.Min(at, c.DeckZone.Count));
						Give(g, s, fresh, "GBL_003e");
					}
				})
			}));
			// [TIME_730] Kaldorei Cultivator (2) — Battlecry: Discover 2 Beasts. Put them on the bottom of your deck with +5/+5.
			ISimpleTask cultivate = new CustomTask((g, c, s, t, stack) =>
			{
				if (t == null) return;
				t.Zone?.Remove(t);
				if (c.DeckZone.IsFull) return;
				c.DeckZone.Add(t, 0);
				Give(g, s, t, "TIME_730e");
			});
			cards.Add("TIME_730", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.IsRace(Race.BEAST), action: ChoiceAction.STACK,
					afterPick: ComplexTask.Create(cultivate, DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x.IsRace(Race.BEAST), action: ChoiceAction.STACK, afterPick: cultivate)))
			}));
			cards.Add("TIME_730e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TIME_730", "stats", 5))) }));

			// ---------------------------------------------------------------- Hunter
			// [END_015] Triennium Rex (5) — Kindred and Deathrattle: Get a random Deathrattle minion. It costs (2) less.
			Action<Game, Controller, IEntity, IPlayable> rex = (g, c, s, t) =>
			{
				if (c.HandZone.IsFull || !(RandomCard(g, x => x.Type == CardType.MINION && x[GameTag.DEATHRATTLE] > 0) is Card m)) return;
				IPlayable got = Entity.FromCard(c, m, new EntityData { [GameTag.CREATOR] = s.Id });
				if (Generic.AddHandPhase.Invoke(c, got)) Give(g, s, got, GenericCostLess(V("END_015", "less", 2)));
			};
			cards.Add("END_015", new CardDef(KindredPower(rex, power: new Power { DeathrattleTask = new CustomTask((g, c, s, t, stack) => rex(g, c, s, t)) })));
			// [TIME_042] King Maluk (4) — Battlecry: Discard your hand. Get an Infinite Banana.
			cards.Add("TIME_042", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in c.HandZone.GetAll().ToList()) Generic.DiscardBlock.Invoke(c, p);
					AddToHand(c, Cards.FromId("TIME_042t"));
				})
			}));
			// [TIME_042t] Infinite Banana (1) — Give a minion +1/+1. (This stays in your hand.) (a new one each cast — wiki)
			cards.Add("TIME_042t", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m) Give(g, s, m, "TIME_042te");
					AddToHand(c, ((IPlayable)s).Card);
				})
			}));
			cards.Add("TIME_042te", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TIME_042t", "stats", 1))) }));
			// [TIME_600] Precise Shot (2) — Deal $3 damage. If this is EXACTLY in the center of your hand, deal $5 instead.
			bool Centered(int slot, int count) => count % 2 == 1 && slot == count / 2;
			cards.Add("TIME_600", new CardDef(TargetAny, new Power
			{
				GlowWhile = p => p.Zone?.Type == Zone.HAND && Centered(p.ZonePosition, p.Controller.HandZone.Count),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int amount = Centered(c.PlayedFromSlot, c[HsTag.PLAYED_HAND_COUNT]) ? V("TIME_600", "more", 5) : V("TIME_600", "damage", 3);
					if (t is ICharacter ch) Generic.DamageCharFunc((IPlayable)s, ch, amount, true);
				})
			}));
			// [TIME_601] Arrow Retriever (2) — Battlecry: Draw until you have 3 cards.
			cards.Add("TIME_601", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => DrawUntil(c, V("TIME_601", "cards", 3))) }));
			// [TIME_602] Wormhole (3) — Rewind. Summon a random 3-Cost Beast. It attacks a random enemy.
			cards.Add("TIME_602", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = RewindPower((g, c, s, t) =>
				{
					if (!(SummonGet(g, c, RandomBoardMinion(g, V("TIME_602", "cost", 3), x => x.IsRace(Race.BEAST)), s) is Minion beast)) return;
					var enemies = AttackableEnemies(c);
					if (enemies.Count > 0) EffectAttack(g, beast, enemies[g.Random.Next(enemies.Count)]);
				})
			}));
			// [TIME_603] Ticking Timebomb (2) — Deathrattle: Destroy a random enemy minion.
			cards.Add("TIME_603", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = BoardMinions(c.Opponent).Cast<Minion>().Where(m => !m.IsDead && !m.ToBeDestroyed).ToList();
					if (enemies.Count > 0) Generic.Destroy(enemies[g.Random.Next(enemies.Count)], (IPlayable)s);
				})
			}));
			// [TIME_605] Epoch Stalker (6) — Rush, Elusive. Battlecry: Summon a copy of this.
			cards.Add("TIME_605", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me && me.Zone?.Type == Zone.PLAY && !c.BoardZone.IsFull) Generic.Copy(c, s, me, Zone.PLAY); })
			}));
			// [TIME_606] Quel'dorei Fletcher (1) — Your Hero Power costs (0) while your hand has 3 or less cards. (live: TIME_606e on the
			// player while it lives; the Hero Power's cost follows the hand — a Restless HEROPOWER aura)
			cards.Add("TIME_606", new CardDef(new Power
			{
				Aura = new MultiAura(
					new Aura(AuraType.CONTROLLER) { ShowAs = "TIME_606e" },
					new Aura(AuraType.HEROPOWER, Effects.SetCost(0)) { Condition = new SelfCondition(p => p.Controller.HandZone.Count <= V("TIME_606", "hand", 3)), Restless = true })
			}));
			// [TIME_609] Ranger General Sylvanas (3) — Fabled. Battlecry: Deal 2 damage to all enemies. If you've played Alleria or Vereesa,
			// repeat for each. (the three sisters: TIME_609 and its related bundle cards)
			Func<IPlayable, int> sisters = p =>
			{
				var family = new HashSet<string>((ReprintCards.RelatedCardIds("TIME_609") ?? new List<string>()).Append("TIME_609"));
				return p.Controller.PlayHistory.Where(h => h.SourceController == p.Controller.PlayerId && h.SourceCard != null && family.Contains(h.SourceCard.Id) && h.SourceCard.Id != p.Card.Id)
					.Select(h => h.SourceCard.Id).Distinct().Count();
			};
			cards.Add("TIME_609", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i <= sisters((IPlayable)s); i++)
					{
						DamageAll(s, Enemies(c), V("TIME_609", "damage", 2), false);
						g.DeathProcessingAndAuraUpdate();
					}
				})
			}));
			cards.Add("TIME_609t1", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i <= sisters((IPlayable)s); i++) DiscoverFromTask.ClassOrNeutral((c2, x) => x.Type == CardType.SPELL).Process(g, c, s, null);
				})
			}));
			cards.Add("TIME_609t2", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i <= sisters((IPlayable)s); i++) GiveAll(g, s, c.DeckZone.GetAll().Where(p => Generic.IsMinionCard(p)).ToList(), "TIME_609t2e");
				})
			}));
			cards.Add("TIME_609t2e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TIME_609t2", "stats", 1))) }));
			// [TIME_620] Untimely Death (2) — Secret: When a friendly minion dies the turn after being played, resummon it.
			cards.Add("TIME_620", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p is Minion && p.Controller.PlayHistory.Any(h => h.SourceId == p.Id && h.Turn == p.Game.Turn - 1)),
					SingleTask = ComplexTask.Secret(new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable dead = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (dead != null && !c.BoardZone.IsFull) Generic.SummonBlock.Invoke(g, (Minion)Generic.NewFromGraveyard(c, dead), -1, s);
					}))
				}
			}));
			// [TIME_810] Past / Present / Future Silvermoon — Location: 5 damage to a random (Future: the lowest Health) enemy minion
			// (Present / Future: excess hits the enemy hero). Advance… (LocationActions: the location changes after its use)
			void Silvermoon(Game g, Controller c, IEntity s, bool lowest, bool excess)
			{
				var enemies = BoardMinions(c.Opponent).Cast<Minion>().Where(m => !m.IsDead && !m.ToBeDestroyed && m.Health > 0).ToList();
				if (enemies.Count == 0) return;
				Minion target;
				if (lowest) { int low = enemies.Min(m => m.Health); var l = enemies.Where(m => m.Health == low).ToList(); target = l[g.Random.Next(l.Count)]; }
				else target = enemies[g.Random.Next(enemies.Count)];
				int left = Generic.DamageWithExcess((IPlayable)s, target, V("TIME_810", "damage", 5), false);
				if (excess && left > 0) Generic.DamageCharFunc((IPlayable)s, c.Opponent.Hero, left, false);
			}
			cards.Add("TIME_810", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Silvermoon(g, c, s, false, false)) }));
			cards.Add("TIME_810t1", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Silvermoon(g, c, s, false, true)) }));
			cards.Add("TIME_810t2", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Silvermoon(g, c, s, true, true)) }));

			// ---------------------------------------------------------------- Mage
			// [END_024] Flames of Infinity (3) — Secret: When your enemy's turn ends, deal INFINITE damage to their highest Health minion.
			cards.Add("END_024", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					EitherTurn = true,
					Condition = new SelfCondition(p => p.Game.CurrentPlayer == p.Controller.Opponent && BoardMinions(p.Controller.Opponent).Any(m => !m.IsDead)),
					SingleTask = ComplexTask.Secret(new CustomTask((g, c, s, t, stack) =>
					{
						var enemies = BoardMinions(c.Opponent).Cast<Minion>().Where(m => !m.IsDead && !m.ToBeDestroyed).ToList();
						if (enemies.Count == 0) return;
						int top = enemies.Max(m => m.Health);
						var high = enemies.Where(m => m.Health == top).ToList();
						Generic.DamageCharFunc((IPlayable)s, high[g.Random.Next(high.Count)], Generic.Infinity, false);
					}))
				}
			}));
			// [TIME_000] Semi-Stable Portal (2) — Rewind. Add a random minion to your hand. It costs (3) less.
			cards.Add("TIME_000", new CardDef(new Power
			{
				PowerTask = RewindPower((g, c, s, t) =>
				{
					if (c.HandZone.IsFull || !(RandomCard(g, x => x.Type == CardType.MINION) is Card m)) return;
					IPlayable got = Entity.FromCard(c, m, new EntityData { [GameTag.CREATOR] = s.Id });
					if (Generic.AddHandPhase.Invoke(c, got)) Give(g, s, got, GenericCostLess(V("TIME_000", "less", 3)));
				})
			}));
			// [TIME_006] Mirror Dimension (1) — Summon a 0/4 minion with Taunt. If you are holding a Dragon, summon another.
			cards.Add("TIME_006", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) => SummonGet(g, c, Cards.FromId("TIME_006t1"), s)),
					IfHoldingDragon(new CustomTask((g, c, s, t, stack) => SummonGet(g, c, Cards.FromId("TIME_006t1"), s))))
			}));
			// [TIME_852] Azure Queen Sindragosa (5) — Fabled. If you control another Dragon, your Arcane spells cost (2) less.
			bool AnotherDragon(IPlayable p) => p.Controller.BoardZone.GetAll().Any(m => m.Id != p.Id && m.IsRace(Race.DRAGON) && !m.IsDead);
			cards.Add("TIME_852", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.ReduceCost(V("TIME_852", "less", 2)))
				{
					Condition = new SelfCondition(p => p is Spell && p.Card[SPELL_SCHOOL] == 1 && p.Controller.BoardZone.GetAll().Count(m => m.IsRace(Race.DRAGON) && !m.IsDead) >= 2),
					Restless = true
				}
			}));
			// [TIME_852t1] Azure King Malygos (5) — If you control another Dragon, your Arcane spells cast twice. (a Restless CONTROLLER aura)
			cards.Add("TIME_852t1", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER, new Effect(HsTag.ARCANE_SPELLS_CAST_TWICE, EffectOperator.ADD, 1)) { Condition = new SelfCondition(AnotherDragon), Restless = true }
			}));
			// [TIME_852t3] Azure Oathstone (8) — Summon your Dragons that died this game.
			cards.Add("TIME_852t3", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable dead in c.DiedThisGameCards.Where(p => p is Minion && p.Card.IsRace(Race.DRAGON)).ToList())
					{
						if (c.BoardZone.IsFull) break;
						Generic.SummonBlock.Invoke(g, (Minion)Generic.NewFromGraveyard(c, dead), -1, s);
					}
				})
			}));
			// [TIME_855] Arcane Barrage (3) — Deal $3 damage to an enemy and $2 damage to two other random ones.
			cards.Add("TIME_855", new CardDef(TargetEnemy, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is ICharacter first)) return;
					Generic.DamageCharFunc((IPlayable)s, first, V("TIME_855", "damage", 3), true);
					foreach (ICharacter e in Enemies(c).Where(x => x.Id != first.Id && !x.IsDead && !x.ToBeDestroyed).OrderBy(_ => g.Random.Next()).Take(V("TIME_855", "others", 2)).ToList())
						Generic.DamageCharFunc((IPlayable)s, e, V("TIME_855", "small", 2), true);
				})
			}));
			// [TIME_857] Alter Time (4) — Discover two Arcane spells from the past. They cost (2) less.
			ISimpleTask alterTime(ISimpleTask then) => new CustomTask((g, c, s, t, stack) =>
			{
				Card[] offer = DiscoverFromTask.PickDistinct(g, PastCards(g, c, x => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == 1 &&
					(x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL))), 3).ToArray();
				if (offer.Length == 0) return;
				Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.STACK, offer, new CustomTask((g2, c2, s2, pick, st) =>
				{
					if (pick != null && !c2.HandZone.IsFull)
					{
						pick.Zone?.Remove(pick);
						if (Generic.AddHandPhase.Invoke(c2, pick)) Give(g2, s2, pick, GenericCostLess(V("TIME_857", "less", 2)));
					}
					then?.Process(g2, c2, s2, null);
				}));
			});
			cards.Add("TIME_857", new CardDef(new Power { PowerTask = alterTime(alterTime(null)) }));
			// [TIME_858] Temporal Construct (7) — Battlecry: Deal 5 damage to an enemy minion. Draw cards equal to the excess damage.
			cards.Add("TIME_858", new CardDef(BattlecryEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					int excess = Generic.DamageWithExcess((IPlayable)s, m, V("TIME_858", "damage", 5), false);
					for (int i = 0; i < excess; i++) Generic.Draw(c);
				})
			}));
			// [TIME_859] Anomalize (7) — Summon a random 10 and 1-Cost minion. Scramble their stats. (wiki: the 4 values shuffled between them,
			// avoiding 0 Health and their own stats when possible; live: TIME_859e NUM_1 = Health, NUM_2 = Attack)
			cards.Add("TIME_859", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var made = new List<Minion>();
					foreach (int cost in new[] { 10, 1 })
						if (SummonGet(g, c, RandomMinionOfCost(g, cost), s) is Minion m) made.Add(m);
					if (made.Count < 2) return;
					int[] values = { made[0].AttackDamage, made[0].Health, made[1].AttackDamage, made[1].Health };
					int[] best = null;
					for (int attempt = 0; attempt < 24; attempt++)
					{
						int[] p = values.OrderBy(_ => g.Random.Next()).ToArray();
						bool zero = p[1] == 0 || p[3] == 0;
						bool same = p[0] == values[0] && p[1] == values[1] || p[2] == values[2] && p[3] == values[3];
						best ??= p;
						if (!zero && !same) { best = p; break; }
					}
					Generic.AddEnchantmentBlock(g, Cards.FromId("TIME_859e"), (IPlayable)s, made[0], best[1], best[0], 0);
					Generic.AddEnchantmentBlock(g, Cards.FromId("TIME_859e"), (IPlayable)s, made[1], best[3], best[2], 0);
				})
			}));
			cards.Add("TIME_859e", new CardDef(new Power { Enchant = new Enchant(Effects.SetMaxHealth(0), Effects.SetAttack(0)) { UseScriptTag = true } }));
			// [TIME_860] Faceless Enigma (2) — Battlecry: Look at 2 random Secrets. Pick one to cast for yourself. The other casts for your
			// opponent. (any class — wiki)
			cards.Add("TIME_860", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var secrets = DiscoverFromTask.PickDistinct(g, Cards.GenerationPool(g, c).Where(x => x.Type == CardType.SPELL && x[GameTag.SECRET] > 0), 2);
					if (secrets.Count == 0) return;
					var offered = secrets.Select(x => Entity.FromCard(c, x, new EntityData { [GameTag.CREATOR] = s.Id }, c.SetasideZone)).ToList();
					var ids = offered.Select(p => p.Id).ToList();
					Generic.CreateChoiceEntities(c, s, offered, ChoiceAction.STACK, new CustomTask((g2, c2, s2, pick, st) =>
					{
						if (pick == null) return;
						foreach (int id in ids)
						{
							if (!g2.IdEntityDic.TryGetValue(id, out IPlayable p) || p.Zone?.Type != Zone.SETASIDE) continue;
							Controller to = id == pick.Id ? c2 : c2.Opponent;
							p.Zone.Remove(p);
							IPlayable cast = id == pick.Id ? p : Entity.FromCard(to, p.Card, new EntityData { [GameTag.CREATOR] = s2.Id });
							Generic.CastSpell.Invoke(to, g2, (Spell)cast, null, 0);
						}
					}));
				})
			}));
			// [TIME_861] Timelooper Toki (3) — Battlecry: Get 3 random spells from the past. When you play ALL 3, get another Timelooper Toki.
			// (live 175606 16332: TIME_861e1 on the player, each spell marked TIME_861e2 and highlighted; ours counts the marked plays)
			cards.Add("TIME_861", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var spells = new List<IPlayable>();
					for (int i = 0; i < V("TIME_861", "spells", 3) && !c.HandZone.IsFull; i++)
					{
						if (!(Pick(g, PastCards(g, c, x => x.Type == CardType.SPELL), 1).FirstOrDefault() is Card spell)) break;
						IPlayable got = Entity.FromCard(c, spell, new EntityData { [GameTag.CREATOR] = s.Id, [GameTag.DISPLAYED_CREATOR] = s.Id });
						if (Generic.AddHandPhase.Invoke(c, got)) spells.Add(got);
					}
					if (spells.Count < V("TIME_861", "spells", 3)) return; // wiki: without all 3, no more Tokis
					Generic.AddEnchantmentBlock(g, Cards.FromId("TIME_861e1"), (IPlayable)s, c, s.Id, 0, 0);
					foreach (IPlayable sp in spells)
					{
						Give(g, s, sp, "TIME_861e2");
						sp[(GameTag)1054] = 1;
						sp[(GameTag)4663] = 1;
					}
				})
			}));
			cards.Add("TIME_861e2", new CardDef(new Power { NoScript = true }));
			cards.Add("TIME_861e1", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p.AppliedEnchantments?.Any(e => e.Card.Id == "TIME_861e2") == true || p[(GameTag)4663] > 0 && p[GameTag.CREATOR] > 0),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable played = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (!(s is Enchantment e) || played == null || played[GameTag.CREATOR] != e[GameTag.TAG_SCRIPT_DATA_NUM_1]) return;
						if (played[(GameTag)1054] != 0) played[(GameTag)1054] = 0;
						if (played[(GameTag)4663] != 0) played[(GameTag)4663] = 0;
						e[GameTag.TAG_SCRIPT_DATA_NUM_2] = e[GameTag.TAG_SCRIPT_DATA_NUM_2] + 1;
						if (e[GameTag.TAG_SCRIPT_DATA_NUM_2] < V("TIME_861", "spells", 3)) return;
						AddToHand(c, Cards.FromId("TIME_861"));
						e.Remove();
					})
				}
			}));
		}
	}
}
