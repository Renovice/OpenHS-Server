// HsServer addition (not upstream SabberStoneCore): event cards (sets 1941 and 1994: BE_EVENT_, CATA_EVENT_, JAIL_EVENT_, TIME_EVENT_, TLC_EVENT_,
// EDR_950, BE_036, BE_259, BE_703, BE_013). Spec with sources: session scratchpad audit/events-spec.md (wiki, forum, live captures).
// Shared pieces: Generic.Ripple ("continues to the left and right with 1 less"), HsTag.ONLY_PLAY_CARDTYPE + Generic.SyncTurnPlayRestriction
// ("You can only play X for the rest of the turn"), RefreshMana, SpendAllMana, Reopen, RefreshHeroPower, TransformCostShifted.
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
		private const GameTag OLD_GOD = (GameTag)4754, OLD_GOD_ABILITY_OPTION = (GameTag)4786, COLOSSAL_TAG = (GameTag)2247;

		/// <summary>"You can only play &lt;type&gt; for the rest of the turn": a one-turn player enchantment.</summary>
		private static CardDef OnlyThisTurn(CardType type) => new CardDef(new Power
		{
			Enchant = new Enchant(new Effect(HsTag.ONLY_PLAY_CARDTYPE, EffectOperator.SET, (int)type)) { IsOneTurnEffect = true }
		});

		private static void Events(IDictionary<string, CardDef> cards)
		{
			// [BE_EVENT_100] Arrival of the Old Gods (5) — Draw 2 minions. Refresh 5 Mana Crystals. You can only play minions for the rest of the turn.
			cards.Add("BE_EVENT_100", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					DrawWhere(g, c, p => p is Minion, V("BE_EVENT_100", "draw", 2));
					RefreshMana(c, V("BE_EVENT_100", "refresh", 5));
					new AddEnchantmentTask("BE_EVENT_100e", EntityType.CONTROLLER).Process(g, c, s, t, stack);
				})
			}));
			cards.Add("BE_EVENT_100e", OnlyThisTurn(CardType.MINION));
			// [BE_EVENT_101] Arrival of the Titans (6) — Draw 2 spells. Refresh 6 Mana Crystals. You can only play spells for the rest of the turn.
			// (live-20261007-vfx-decks 15338: the draws, the refresh, the enchantment on the player, the off-type hand cards locked)
			cards.Add("BE_EVENT_101", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					DrawWhere(g, c, p => p is Spell, V("BE_EVENT_101", "draw", 2));
					RefreshMana(c, V("BE_EVENT_101", "refresh", 6));
					new AddEnchantmentTask("BE_EVENT_101e", EntityType.CONTROLLER).Process(g, c, s, t, stack);
				})
			}));
			cards.Add("BE_EVENT_101e", OnlyThisTurn(CardType.SPELL));
			// [BE_EVENT_102] Dark Terraforming (5) — Deal $5 damage to a minion. This continues to the left and right with 1 less damage. (Spell
			// Damage once on the start: the card is ImmuneToSpellpower 349; then the deaths.)
			cards.Add("BE_EVENT_102", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m) || !(m.Zone is SabberStoneCore.Model.Zones.BoardZone board)) return;
					var row = board.GetAll().Cast<Minion>().ToList();
					Generic.Ripple(row, row.IndexOf(m), V("BE_EVENT_102", "damage", 5) + c.CurrentSpellPower,
						(x, n) => { if (!x.IsDead) Generic.DamageCharFunc((IPlayable)s, x, n, false); });
				})
			}));
			// [CATA_EVENT_000] Primordial Lord (6) — Battlecry: Get a random Colossal minion from the past. (Colossals can be generated to the
			// hand: wiki Colossal; the client's CATA_EVENT_000e "Costs (0)" isn't in the text: not given — live-checks)
			cards.Add("CATA_EVENT_000", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var pool = Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION && x[COLOSSAL_TAG] > 0 && Cards.FromThePast(x)).ToList();
					if (pool.Count > 0) AddToHand(c, pool[g.Random.Next(pool.Count)]);
				})
			}));
			// [CATA_EVENT_002] Baleful Blazer (3) — Battlecry: If you've played a Fire spell this turn, destroy a minion.
			cards.Add("CATA_EVENT_002", new CardDef(BattlecryMinion, new Power
			{
				TargetIf = (PlayReq.REQ_TARGET_IF_AVAILABLE, (src, target) =>
					src.Controller.PlayHistory.Any(h => h.Turn == src.Game.Turn && h.SourceCard?.Type == CardType.SPELL && h.SourceCard[SPELL_SCHOOL] == FIRE_SCHOOL)),
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m) Generic.Destroy(m, s); })
			}));
			// [CATA_EVENT_400] Commissary Crook (3) — Prepare. Battlecry: Spend all your Mana. Summon a random minion of that Cost. (0 Mana: a
			// 0-Cost one: wiki Spend mana)
			cards.Add("CATA_EVENT_400", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int spent = Generic.SpendAllMana(c);
					if (Cards.BoardCostMinionCards(g.FormatType).TryGetValue(spent, out List<Card> pool) && pool.Count > 0)
						Summon(g, c, pool[g.Random.Next(pool.Count)], s);
				})
			}));
			// [CATA_EVENT_401] Tunneling Geomancer (3) — Prepare. Spell Damage +1. (both from its tags: no script — ReprintCards.IsKeywordOnly)
			// [CATA_EVENT_402] Deadly Bribe (3) — Destroy a minion and give your opponent a Coin. Combo: You get one too.
			cards.Add("CATA_EVENT_402", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m) Generic.Destroy(m, s);
					AddToHand(c.Opponent, Cards.FromId("GAME_005"));
				}),
				ComboTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion m) Generic.Destroy(m, s);
					AddToHand(c.Opponent, Cards.FromId("GAME_005"));
					AddToHand(c, Cards.FromId("GAME_005"));
				})
			}));
			// [JAIL_EVENT_101] Soul Immolation (2) — Your Hero Power becomes 'Collapsing Star'. If it already is, increase its damage by 1.
			cards.Add("JAIL_EVENT_101", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.Hero.HeroPower?.Card.Id == "JAIL_EVENT_101hp")
						c.Hero.HeroPower[GameTag.TAG_SCRIPT_DATA_NUM_1] += V("JAIL_EVENT_101", "more", 1);
					else
						new ReplaceHeroPower(Cards.FromId("JAIL_EVENT_101hp")).Process(g, c, s, t, stack);
				})
			}));
			// [JAIL_EVENT_101hp] Collapsing Star (1) — Deal @ damage to a random enemy (@ = its TAG_SCRIPT_DATA_NUM_1). Whenever you summon a
			// Demon, refresh this (live darkmoon 15294: at the summon, before the Demon's Battlecry).
			cards.Add("JAIL_EVENT_101hp", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var enemies = c.Opponent.BoardZone.GetAll().Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).Cast<ICharacter>().Append(c.Opponent.Hero).ToList();
					Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], ((IPlayable)s)[GameTag.TAG_SCRIPT_DATA_NUM_1], false);
				}),
				Trigger = new Trigger(TriggerType.SUMMON)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = SelfCondition.IsRace(Race.DEMON),
					SingleTask = new CustomTask((g, c, s, t, stack) => Generic.RefreshHeroPower(c))
				}
			}));
			// [JAIL_EVENT_102] Desperate Bribe (3) — Summon two 2-Cost minions for each player. Transform your minions into ones that cost (1) more.
			// (live 1417 Power.log 16447: yours, theirs, yours, theirs; then ALL your minions, old and new, transform)
			cards.Add("JAIL_EVENT_102", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!Cards.BoardCostMinionCards(g.FormatType).TryGetValue(V("JAIL_EVENT_102", "cost", 2), out List<Card> pool) || pool.Count == 0) return;
					for (int i = 0; i < V("JAIL_EVENT_102", "minions", 2); i++)
						foreach (Controller side in new[] { c, c.Opponent })
							if (!side.BoardZone.IsFull) Summon(g, side, pool[g.Random.Next(pool.Count)], s);
					g.DeathProcessingAndAuraUpdate();
					TransformCostShifted(g, c.BoardZone.GetAll().Where(m => !m.IsDead && !m.Untouchable).ToList(), V("JAIL_EVENT_102", "more", 1));
				})
			}));
			// [TIME_EVENT_300] Dark Iron Harbinger (4) — Deathrattle: Summon a 0/7 Doomsayer that destroys ALL minions at the start of your turn.
			// (client COLLECTION_RELATED: CORE_NEW1_021)
			cards.Add("TIME_EVENT_300", new CardDef(new Power { DeathrattleTask = new SummonTask("CORE_NEW1_021", SummonSide.DEATHRATTLE) }));
			// [TIME_EVENT_301] Disciple of Demise (8) — Battlecry: Randomly destroy another minion. Repeat for each Dragon you're holding. (the
			// Dragons counted once, at the Battlecry; a different, living minion each time)
			cards.Add("TIME_EVENT_301", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int times = 1 + c.HandZone.Count(p => p.Card.IsRace(Race.DRAGON));
					for (int i = 0; i < times; i++)
					{
						var others = c.BoardZone.Concat(c.Opponent.BoardZone).Where(m => m != s && !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
						if (others.Count == 0) break;
						Generic.Destroy(others[g.Random.Next(others.Count)], s);
					}
				})
			}));
			// [TIME_EVENT_997] Welcome Home! (3) — Reopen a location. Give it "Deathrattle: Summon a random 3-Cost minion." (live yogg-cthun 24880:
			// the location picked; reopened; TIME_EVENT_997e on it)
			cards.Add("TIME_EVENT_997", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayErrors.REQ_LOCATION_TARGET, 0 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Location place)) return;
					Generic.Reopen(place);
					Give(g, s, place, "TIME_EVENT_997e");
				})
			}));
			cards.Add("TIME_EVENT_997e", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (Cards.BoardCostMinionCards(g.FormatType).TryGetValue(V("TIME_EVENT_997", "cost", 3), out List<Card> pool) && pool.Count > 0)
						Summon(g, c, pool[g.Random.Next(pool.Count)], s);
				})
			}));
			// [TLC_EVENT_402] Staff of the Endbringer (3, weapon) — Deathrattle: Destroy all minions.
			cards.Add("TLC_EVENT_402", new CardDef(new Power { DeathrattleTask = new DestroyTask(EntityType.ALLMINIONS) }));
			// [BE_036] M.O.T.H.E.R. (9) — Battlecry: Reduce the Cost of a card in your hand by (5). This continues to the left and right with (1) less
			// reduction. (The player picks: CAN_TARGET_CARDS_IN_HAND. Live startofgame 14437: one BE_036e2 per card, its NUM_1 the reduction.)
			cards.Add("BE_036", new CardDef(new Power
			{
				HandTarget = SelfCondition.AnyCard,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t == null || t.Zone?.Type != Zone.HAND || t.Controller != c) return;
					var hand = c.HandZone.GetAll().ToList();
					Generic.Ripple(hand, hand.IndexOf(t), V("BE_036", "reduction", 5), (card, n) => Give(g, s, card, "BE_036e2", n));
				})
			}));
			cards.Add("BE_036e2", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(0)) { UseScriptTag = true } }));
			// [BE_703] Drest'agath (4) — Deathrattle: Draw an Old God. If you can't, get a random Old God. (Old God: client OLD_GOD and its
			// ability option tag; the wiki: it can generate them.)
			cards.Add("BE_703", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					bool IsOldGod(Card x) => x.Type == CardType.MINION && x[OLD_GOD] > 0 && x[OLD_GOD_ABILITY_OPTION] > 0;
					var inDeck = c.DeckZone.Where(p => IsOldGod(p.Card)).ToList();
					if (inDeck.Count > 0) { Generic.Draw(c, inDeck[g.Random.Next(inDeck.Count)]); return; }
					var pool = Cards.All.Where(x => x.Collectible && IsOldGod(x)).ToList();
					if (pool.Count > 0) AddToHand(c, pool[g.Random.Next(pool.Count)]);
				})
			}));
			// [JAIL_EVENT_100] Watfin (2) — Battlecry: Discover a minion. Pick the suspicious one to gain +1/+1. (wiki Watfin: one option has "one
			// of the following adjustments: Costs 1 less mana, Has +1 Attack, Has +1 Health, Has an added minion type, Has the name of another
			// random minion"; "The adjusted card will keep its adjustment when played". The option shows the wrong numbers; picking it gives
			// that adjusted card and Watfin JAIL_EVENT_100e. DOUBT: the added-type and other-name variants aren't built — cost/Attack/Health only.)
			cards.Add("JAIL_EVENT_100", new CardDef(new Power
			{
				OwnFx = true, // its buff effect goes with the found card's +1/+1 (below), not around the Discover
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var offer = Pick(g, Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION), 3);
					if (offer.Count == 0) return;
					Card suspicious = offer[g.Random.Next(offer.Count)];
					IPlayable disguised = null;
					Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.HAND, offer.ToArray(),
						new CustomTask((g2, c2, s2, chosen, stack2) =>
						{
							if (chosen == null || chosen != disguised || !(s is Minion watfin) || watfin.Zone?.Type != Zone.PLAY) return;
							// live (live-20261008-120539 1226): Watfin's +1/+1 inside its JAILEVENTFX_Watfin_BuffImpact_Super (Source = Targets =
							// Watfin). (The 4472 / 4997 live sent on the found card are every hand minion's last known Attack / Health: Game.cs.)
							bool fx = SabberStoneCore.Loader.SubSpells.Named("JAIL_EVENT_100", "BuffImpact") is string buff && g2.OpenSubSpell(watfin, buff, new[] { watfin.Id });
							Give(g2, watfin, watfin, "JAIL_EVENT_100e");
							if (fx) g2.CloseSubSpells(watfin.Id);
						}));
					disguised = c.SetasideZone.LastOrDefault(x => x.Card == suspicious && x[GameTag.CREATOR] == s.Id);
					if (disguised == null) return;
					switch (disguised.Card.Cost > 0 ? g.Random.Next(3) : 1 + g.Random.Next(2))
					{
						case 0: disguised[GameTag.COST] = disguised.Card.Cost - 1; break;
						case 1: disguised[GameTag.ATK] = disguised.Card.ATK + 1; break;
						default: disguised[GameTag.HEALTH] = disguised.Card.Health + 1; break;
					}
				})
			}));
			cards.Add("JAIL_EVENT_100e", FromText("JAIL_EVENT_100e"));
			EventsComplex(cards);
		}

		private const GameTag ASPECT_ESSENCE = (GameTag)4761; // client tag on Dragon Soul's six Essences

		/// <summary>
		/// An Aspect Essence: its effect, then the adjoining Essences in hand are cast. Live (live-20261008-120539 5032, 9466, 19719): the run is
		/// taken once, at the play (Generic.AdjoiningRunInHand: the right side outward, then the left), and each Essence is cast from INSIDE
		/// the previous one's POWER block — an empty CATAFX_DragonsSoul_CardsInHand_Impact_Conceal_Super (Source = Targets = the Essence about
		/// to go), then the cast — so a chain-cast Essence casts the next one; none starts a run of its own (wiki 36.2.0 fix "Sinestra could
		/// cast an Essence twice").
		/// </summary>
		private static CardDef Essence(Dictionary<PlayReq, int> reqs, ISimpleTask effect) => new CardDef(reqs, new Power
		{
			PowerTask = ComplexTask.Create(effect, new CustomTask((g, c, s, t, stack) =>
			{
				bool played = c[HsTag.CASTING_ADJOINING_RUN] == 0;
				if (played)
				{
					var run = Generic.AdjoiningRunInHand(c, p => p is Spell && p.Card[ASPECT_ESSENCE] > 0);
					if (run.Count == 0) return;
					EssenceRun = new Queue<int>(run.Select(p => p.Id));
					c[HsTag.CASTING_ADJOINING_RUN] = 1;
				}
				// the next one still in hand, cast from inside this one
				while (EssenceRun != null && EssenceRun.Count > 0 && !c.Hero.IsDead && !c.Opponent.Hero.IsDead)
				{
					if (!(g.IdEntityDic.TryGetValue(EssenceRun.Dequeue(), out IPlayable next) && next is Spell spell && spell.Zone?.Type == Zone.HAND && spell.Controller == c))
						continue;
					if (SabberStoneCore.Loader.SubSpells.Named("CATA_EVENT_110", "CardsInHand_Impact_Conceal") is string fx && g.OpenSubSpell(spell, fx, new[] { spell.Id }))
						g.CloseSubSpells(spell.Id);
					Generic.CastFromHand(c, s, spell);
					break;
				}
				if (played)
				{
					EssenceRun = null;
					c[HsTag.CASTING_ADJOINING_RUN] = 0;
				}
			}))
		});

		/// <summary>The Essences still to cast in the running chain (one chain at a time; set by the played Essence).</summary>
		[System.ThreadStatic] private static Queue<int> EssenceRun;

		private static void EventsComplex(IDictionary<string, CardDef> cards)
		{
			// [CATA_EVENT_110] Dragon Soul, Shattered (6) — Start of Game: Break into 6 Essences. Adjoining Essences are cast together. (As C'Thun,
			// the Shattered: the six Essences go into the deck, the card is set aside.)
			cards.Add("CATA_EVENT_110", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.GAME_START)
				{
					TriggerActivation = TriggerActivation.DECK,
					SingleTask = new CustomTask((g, c, s, t, stack) => BreakIntoPieces(g, c, s as IPlayable,
						new[] { "CATA_EVENT_110t2", "CATA_EVENT_110t3", "CATA_EVENT_110t4", "CATA_EVENT_110t5", "CATA_EVENT_110t6", "CATA_EVENT_110t7" }, "StartOfGame")),
					RemoveAfterTriggered = true
				}
			}));
			// the Essences (a chain-cast Red picks its enemy at random: CastFromHand)
			cards.Add("CATA_EVENT_110t2", Essence(TargetEnemy, new DamageTask(V("CATA_EVENT_110t2", "damage", 8), EntityType.TARGET, true)));
			cards.Add("CATA_EVENT_110t3", Essence(null, new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p is Spell, V("CATA_EVENT_110t3", "spells", 3)))));
			cards.Add("CATA_EVENT_110t4", Essence(null, new CustomTask((g, c, s, t, stack) => RefreshMana(c, V("CATA_EVENT_110t4", "refresh", 8)))));
			cards.Add("CATA_EVENT_110t5", Essence(null, new ArmorTask(V("CATA_EVENT_110t5", "armor", 12))));
			cards.Add("CATA_EVENT_110t6", Essence(null, new SummonTask("CATA_EVENT_110t6t", SummonSide.SPELL)));
			cards.Add("CATA_EVENT_110t7", Essence(null, new DamageTask(V("CATA_EVENT_110t7", "damage", 4), EntityType.OP_MINIONS, true)));

			// [CATA_EVENT_001] Destructive Phoenix (4) — Battlecry: Choose a card in your hand to set on fire. In 3 turns, discard it and summon a
			// copy of this. (The player picks; counted at the start of your turns — wiki hidden tag "Triggered effect – at the start of your
			// turn", confirmed by the user 2026-10-08. The copy is a plain Phoenix: forum t/160060.)
			cards.Add("CATA_EVENT_001", new CardDef(new Power
			{
				HandTarget = SelfCondition.AnyCard,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t == null || t.Zone?.Type != Zone.HAND || t.Controller != c) return;
					Give(g, s, t, "CATA_EVENT_001e");
				})
			}));
			cards.Add("CATA_EVENT_001e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || !(e.Target is IPlayable burning)) return;
						if (burning.Zone?.Type != Zone.HAND) { e.Remove(); return; }
						// turns left: the card's own TAG_SCRIPT_DATA_NUM_2 (3) minus the starts counted so far; shown on the enchantment
						e[GameTag.TAG_SCRIPT_DATA_NUM_1] = e[GameTag.TAG_SCRIPT_DATA_NUM_1] + 1;
						int left = e.Card[GameTag.TAG_SCRIPT_DATA_NUM_2] - e[GameTag.TAG_SCRIPT_DATA_NUM_1];
						e[GameTag.TAG_SCRIPT_DATA_NUM_2] = System.Math.Max(0, left);
						if (left > 0) return;
						Controller owner = burning.Controller;
						Generic.DiscardBlock.Invoke(owner, burning);
						if (!owner.BoardZone.IsFull) Summon(g, owner, Cards.FromId("CATA_EVENT_001"), e);
					})
				}
			}));

			// [TIME_EVENT_998] Runi, Temporal Guardian (5) — Battlecry: Send all minions in your hand 2 turns into the future. They return with +5/+5.
			// (As Anachronos: set aside, back at the start of your next turn — to the hand, with TIME_EVENT_998e2; no room: lost — wiki.)
			cards.Add("TIME_EVENT_998", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var sent = c.HandZone.Where(p => Generic.IsMinionCard(p)).ToList();
					if (sent.Count == 0) return;
					foreach (IPlayable m in sent)
					{
						c.HandZone.Remove(m);
						c.SetasideZone.Add(m);
						m[HS_SCRIPT_B] = s.Id;
					}
					Generic.AddEnchantmentBlock(g, Cards.FromId("TIME_EVENT_998e1"), (IPlayable)s, c, s.Id, 0, 0);
				})
			}));
			cards.Add("TIME_EVENT_998e1", new CardDef(new Power
			{
				Enchant = new Enchant(GameTag.TAG_SCRIPT_DATA_NUM_1, EffectOperator.SET),
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = ComplexTask.Create(
						new CustomTask((g, c, s, t, stack) =>
						{
							if (!(s is Enchantment e)) return;
							int sender = e[GameTag.TAG_SCRIPT_DATA_NUM_1];
							foreach (IPlayable x in c.SetasideZone.Where(x => x is Minion && x[HS_SCRIPT_B] == sender).ToList())
							{
								x[HS_SCRIPT_B] = 0;
								if (c.HandZone.IsFull) continue; // lost (wiki: "provided there is enough hand space")
								c.SetasideZone.Remove(x);
								Generic.AddHandPhase(c, x);
								Give(g, e, x, "TIME_EVENT_998e2");
							}
						}),
						RemoveEnchantmentTask.Task)
				}
			}));
			cards.Add("TIME_EVENT_998e2", FromText("TIME_EVENT_998e2"));

			// [TIME_EVENT_999] Sands of Time (1) — Rewind. Discover a spell from ANY class. (Or just your class after you Rewind!) (live darkmoon
			// 38031: the replay after a Rewind offered the player's class only — USED_REWIND; with Morchie the second outcome is that one.)
			cards.Add("TIME_EVENT_999", new CardDef(new Power
			{
				PowerTask = RewindPower((g, c, s, t) =>
				{
					bool rewound = s[Generic.USED_REWIND] > 0 || g.RewindSecondOutcome;
					new DiscoverFromTask((g2, c2, s2) => Cards.GenerationPool(g2, c2).Where(x => x.Type == CardType.SPELL && (!rewound || x.Class == c2.HeroClass)))
						.Process(g, c, s, t, null);
				})
			}));
			// [TIME_EVENT_999t] Sands of Time (1) — Discover a spell from YOUR class.
			cards.Add("TIME_EVENT_999t", new CardDef(new Power
			{
				PowerTask = new DiscoverFromTask((g, c, s) => Cards.GenerationPool(g, c).Where(x => x.Type == CardType.SPELL && x.Class == c.HeroClass))
			}));

			// [EDR_950] Sharp-Eyed Lookout (3) — Battlecry: Draw a card. It costs (1) less this turn. (In hand it names the top card of your
			// deck — "(It's {5}!)": Power.ShowsTopOfDeck, Generic.SyncTopOfDeckText.)
			cards.Add("EDR_950", new CardDef(new Power
			{
				ShowsTopOfDeck = true,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					IPlayable drawn = Generic.Draw(c);
					if (drawn?.Zone?.Type == Zone.HAND) Give(g, s, drawn, "EDR_950e");
				})
			}));
			cards.Add("EDR_950e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("EDR_950", "less", 1))) { IsOneTurnEffect = true } }));

			// [TLC_EVENT_400] Storm the Gates (1) — Sidequest: Play 3 Beasts or Undead. Reward: Craft a custom Zombeast. It costs (3) less. (A
			// Beast-and-Undead minion counts once; played from hand only.)
			cards.Add("TLC_EVENT_400", Sidequest(TriggerType.PLAY_MINION, TriggerSource.FRIENDLY,
				(g, c, t) => t is Minion m && (m.IsRace(Race.BEAST) || m.IsRace(Race.UNDEAD)) ? 1 : 0,
				(g, c, side) =>
				{
					c[HsTag.CRAFTED_ZOMBEAST_DISCOUNT] = V("TLC_EVENT_400", "less", 3);
					SpecificTask.BuildABeast.Process(g, c, side, null);
				}));
		}
	}
}
