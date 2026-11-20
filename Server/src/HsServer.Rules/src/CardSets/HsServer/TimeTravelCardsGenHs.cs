// HsServer addition (not upstream SabberStone): Across the Timeways (TIME_TRAVEL, set 1957) + the Echoes of the Infinite mini-set —
// chunk 1 (spec: research spec_chunk1). Shared pieces: Generic.Imbue (Actions/Imbue.cs), the Hero Power twice (HeroPower.cs: client
// 3428), the hero resurrect (Generic.HeroResurrect), no start-of-turn draw (client 4316), DeckZone.Reverse, PaladinAura(atStart),
// Fabled bundles (RandomDecks). Rewind: the effect runs once — the keep/rewind prompt (a game-state rewind) is not built yet;
// Morchie's "keep BOTH outcomes" repeats the effect (RewindPower). Every number goes through CardValues (V).
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
		/// <summary>Player (controller aura): "Your Rewinds keep BOTH potential outcomes" (Morchie).</summary>
		private const GameTag REWINDS_KEEP_BOTH = (GameTag)990653;
		/// <summary>A choice option: the right guess (Futuristic Forefather: the card from the opponent's hand).</summary>
		private const GameTag GUESS_RIGHT = (GameTag)990654;

		/// <summary>How many Rewinds a card has (its client text's "&lt;b&gt;Rewind&lt;/b&gt;" count — Mister Clocksworth 3).</summary>
		private static int RewindsOf(Card card) =>
			card?.Text == null ? 0 : (card.Text.Length - card.Text.Replace("<b>Rewind</b>", "").Length) / "<b>Rewind</b>".Length;

		/// <summary>A Rewind card's effect (wiki Rewind). With Morchie (wiki Morchie: "it instead triggers all of its effects immediately";
		/// "checks for deaths between the triggers of the two outcomes"; no prompt) it runs once more per Rewind.</summary>
		private static ISimpleTask RewindPower(Action<Game, Controller, IEntity, IPlayable> effect) => new CustomTask((g, c, s, t, stack) =>
		{
			effect(g, c, s, t);
			// no Morchie: Keep / Rewind (Generic.OfferRewind; the adapter rewinds and replays)
			if (c.ControllerAuraEffects[REWINDS_KEEP_BOTH] <= 0) { Generic.OfferRewind(g, c, s as IPlayable); return; }
			g.RewindSecondOutcome = true;
			for (int i = 0; i < RewindsOf(((IPlayable)s).Card); i++)
			{
				g.DeathProcessingAndAuraUpdate();
				if (c.Hero.IsDead || c.Opponent.Hero.IsDead) break;
				effect(g, c, s, t);
			}
			g.RewindSecondOutcome = false;
		});

		/// <summary>Keep Timeline / Rewind Timeline, the two options after a Rewind card (Generic.OfferRewind): Keep does nothing; Rewind marks
		/// the card that asked (its CREATOR) for the adapter to rewind (Game.PendingRewind).</summary>
		private static void RewindOptions(IDictionary<string, CardDef> cards)
		{
			cards.Add("TIME_000ta", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => { }) }));
			cards.Add("TIME_000tb", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => g.PendingRewind = s[GameTag.CREATOR]) }));
		}

		private static void TimeTravel(IDictionary<string, CardDef> cards)
		{
			RewindOptions(cards);
			TimeTravelChunk1(cards);
			TimeTravelChunk2(cards); // TimeTravelCardsGenHs2.cs
			TimeTravelChunk3(cards); // TimeTravelCardsGenHs3.cs
			TimeTravelChunk4(cards); // TimeTravelCardsGenHs4.cs
		}

		private static void TimeTravelChunk1(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Neutral
			// [END_033] Prescient Slitherdrake (7) — Elusive. Costs (3) less if you're holding another Dragon.
			cards.Add("END_033", new CardDef(new Power { Aura = new AdaptiveCostEffect(p => HoldingDragon(p.Controller, p) ? V("END_033", "less", 3) : 0) }));
			// [END_034] Crumblecrusher (8) — Battlecry: Destroy a random enemy minion, location, and weapon.
			cards.Add("END_034", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.Opponent.BoardZone.GetAll().Where(m => !(m is Location) && !m.IsDead && !m.ToBeDestroyed).ToList();
					if (minions.Count > 0) Generic.Destroy(minions[g.Random.Next(minions.Count)], (IPlayable)s);
					var locations = c.Opponent.BoardZone.GetAll().OfType<Location>().ToList();
					if (locations.Count > 0) Generic.Destroy(locations[g.Random.Next(locations.Count)], (IPlayable)s);
					if (c.Opponent.Hero.Weapon is Weapon w) Generic.Destroy(w, (IPlayable)s);
				})
			}));
			// [END_035] Omen of the End (5) — Battlecry: If your deck is empty, destroy the top 5 cards of the enemy deck.
			cards.Add("END_035", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.DeckZone.Count == 0, new CustomTask((g, c, s, t, stack) =>
				{
					int n = V("END_035", "cards", 5);
					for (int i = 0; i < n; i++)
						if (c.Opponent.DeckZone.TopCard is IPlayable top) Generic.DeckCardToGraveyard(top, s.Id, suppressAnimation: n > 1);
				}))
			}));
			// [END_036] Morchie (4) — Your Rewinds keep BOTH potential outcomes. Battlecry: Discover a Rewind card from any class.
			cards.Add("END_036", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER, new Effect(REWINDS_KEEP_BOTH, EffectOperator.ADD, 1)),
				PowerTask = DiscoverFromTask.Where((c, x) => x[(GameTag)3934] > 0)
			}));
			// [END_037] Endtime Murozond (9) — Battlecry: Fill your board with random Dragons. Fully heal your hero. Skip your next turn.
			// (wiki: "the effect is 'Your opponent takes two turns, then you take one turn'" — the opponent's TemporusFlag)
			cards.Add("END_037", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					while (!c.BoardZone.IsFull && SummonGet(g, c, RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.DRAGON) && !x.NeverRandomlyOnBoard), s) != null) { }
					if (c.Hero.Damage > 0) c.Hero.TakeHeal((IPlayable)s, c.Hero.Damage);
					c.Opponent.TemporusFlag = true;
				})
			}));
			// [TIME_002] Aeon Wizard (5) — Rewind. Battlecry: Get 2 random spells from your class.
			cards.Add("TIME_002", new CardDef(new Power
			{
				PowerTask = RewindPower((g, c, s, t) =>
				{
					for (int i = 0; i < V("TIME_002", "spells", 2); i++)
						if (Pick(g, ClassCards(c, CardType.SPELL), 1).FirstOrDefault() is Card spell) AddToHand(c, spell);
				})
			}));
			// [TIME_003] Portal Vanguard (3) — Rewind. Battlecry: Draw a random minion. Give it +2/+2.
			cards.Add("TIME_003", new CardDef(new Power
			{
				PowerTask = RewindPower((g, c, s, t) => { if (DrawOne(g, c, p => p is Minion) is IPlayable drawn) Give(g, s, drawn, "TIME_003e"); })
			}));
			cards.Add("TIME_003e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TIME_003", "stats", 2))) }));
			// [TIME_004] Conflux Crasher (7) — Rewind. Battlecry: Deal 7 damage to a random enemy.
			cards.Add("TIME_004", new CardDef(new Power
			{
				PowerTask = RewindPower((g, c, s, t) =>
				{
					var enemies = Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed).ToList();
					if (enemies.Count > 0) Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], V("TIME_004", "damage", 7), false);
				})
			}));
			// [TIME_024] Murozond, Unbounded (9) — Battlecry: At the start of your next turn, set this minion's Attack to INFINITY!
			cards.Add("TIME_024", new CardDef(new Power { PowerTask = new AddEnchantmentTask("TIME_024e2", EntityType.SOURCE) }));
			cards.Add("TIME_024e2", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || !(e.Target is Minion m)) return;
						e.Remove();
						if (m.Zone?.Type == Zone.PLAY) Give(g, m, m, "TIME_024e");
					})
				}
			}));
			cards.Add("TIME_024e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttack(Generic.Infinity)) }));
			// [TIME_035] Time Machine (6) — Taunt. Deathrattle: Get a random Rewind card.
			cards.Add("TIME_035", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => { if (RandomCard(g, x => x[(GameTag)3934] > 0 && RewindsOf(x) > 0) is Card r) AddToHand(c, r); })
			}));
			// [TIME_038] Mister Clocksworth (8) — Rewind, Rewind, Rewind. Battlecry: Summon 2 random Legendary minions. (+ its versions t1..t3)
			ISimpleTask clocksworth = RewindPower((g, c, s, t) =>
			{
				for (int i = 0; i < V("TIME_038", "legendaries", 2); i++)
					SummonGet(g, c, RandomCard(g, x => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY && !x.NeverRandomlyOnBoard), s);
			});
			foreach (string id in new[] { "TIME_038", "TIME_038t1", "TIME_038t2", "TIME_038t3" }) cards.Add(id, new CardDef(new Power { PowerTask = clocksworth }));
			// [TIME_040] Fading Memory (4) — Deathrattle: Get a random 5-Cost minion from the past.
			cards.Add("TIME_040", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (Pick(g, Cards.GenerationPool(g, c, FormatType.FT_WILD).Where(x => x.Type == CardType.MINION && x.Cost == V("TIME_040", "cost", 5) && Cards.FromThePast(x)), 1).FirstOrDefault() is Card m)
						AddToHand(c, m);
				})
			}));
			// [TIME_041] Futuristic Forefather (4) — Taunt. Battlecry: Look at 3 cards. Guess which one is in your opponent's hand to gain +4
			// Health. (wiki: "one card from the opponent's hand and two cards from their deck"; fillers from the pool)
			cards.Add("TIME_041", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var hand = c.Opponent.HandZone.GetAll().ToList();
					var deck = c.Opponent.DeckZone.GetAll().OrderBy(_ => g.Random.Next()).Take(2).Select(p => p.Card).ToList();
					while (deck.Count < 2 && RandomCard(g, x => true) is Card filler) deck.Add(filler);
					Card right = hand.Count > 0 ? hand[g.Random.Next(hand.Count)].Card : RandomCard(g, x => true);
					var options = new List<IPlayable>();
					foreach (Card card in deck.Append(right).OrderBy(_ => g.Random.Next()))
					{
						IPlayable o = Entity.FromCard(c, card, new EntityData { [GameTag.CREATOR] = s.Id }, c.SetasideZone);
						if (card == right && options.All(x => x[GUESS_RIGHT] == 0)) o[GUESS_RIGHT] = 1;
						options.Add(o);
					}
					Generic.CreateChoiceEntities(c, s, options, ChoiceAction.STACK, new CustomTask((g2, c2, s2, pick, st) =>
					{
						if (pick == null) return;
						if (pick[GUESS_RIGHT] > 0 && s2 is Minion me && me.Zone?.Type == Zone.PLAY) Give(g2, s2, me, "TIME_041e2");
						if (pick.Zone?.Type == Zone.SETASIDE) c2.GraveyardZone.Add(pick.Zone.Remove(pick));
					}));
				})
			}));
			cards.Add("TIME_041e2", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("TIME_041", "health", 4))) }));
			// [TIME_047] Devious Coyote (5) — Stealth. Costs (1) less for each time the enemy hero took damage this turn. (client 4453)
			cards.Add("TIME_047", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.Opponent[Generic.HERO_DAMAGED_TIMES_THIS_TURN] * V("TIME_047", "less", 1))
			}));
			// [TIME_048] Clockwork Rager (5) — Battlecry: Gain +1 Health for each turn you've taken this game. (this one included)
			// (the player's turns this game, this one included: every other game turn from its first)
			Func<IPlayable, int> turnsTaken = p => (p.Game.Turn + (p.Controller == p.Game.FirstPlayer ? 1 : 0)) / 2;
			cards.Add("TIME_048", new CardDef(new Power
			{
				HandCounter = turnsTaken,
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int n = turnsTaken((IPlayable)s);
					if (s is Minion me && n > 0) Give(g, s, me, "TIME_048e", n * V("TIME_048", "health", 1));
				})
			}));
			cards.Add("TIME_048e", new CardDef(new Power { Enchant = Enchants.Enchants.AddHealthScriptTag }));
			// [TIME_049] Dangerous Variant (2) — At the start of your turn, transform into a random 5-Cost minion.
			cards.Add("TIME_049", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me && me.Zone?.Type == Zone.PLAY && RandomMinionOfCost(g, V("TIME_049", "cost", 5)) is Card into)
							Generic.ChangeEntityBlock(c, me, into, true);
					})
				}
			}));
			// [TIME_050] Sentient Hourglass (6) — Rush. After this minion survives damage, swap its stats. (live darkmoon 71696)
			cards.Add("TIME_050", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.SELF,
					Condition = new SelfCondition(p => p is Minion m && m.Health > 0 && !m.ToBeDestroyed && m.Zone?.Type == Zone.PLAY),
					SingleTask = new SwapAttackHealthTask(EntityType.SOURCE, "TIME_050e")
				}
			}));
			// [TIME_051] Soldier of the Infinite (5) — Rush. Battlecry: Double this minion's Attack.
			cards.Add("TIME_051", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me) Give(g, s, me, "TIME_051e", me.AttackDamage); }) }));
			cards.Add("TIME_051e", new CardDef(new Power { Enchant = Enchants.Enchants.AddAttackScriptTag }));
			// [TIME_052] Amber Warden (8) — Taunt. Deathrattle: Summon a random minion from the past.
			cards.Add("TIME_052", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
					SummonGet(g, c, Pick(g, Cards.GenerationPool(g, c, FormatType.FT_WILD).Where(x => x.Type == CardType.MINION && Cards.FromThePast(x) && !x.NeverRandomlyOnBoard), 1).FirstOrDefault(), s))
			}));
			// [TIME_054] Time Skipper (4) — At the end of each player's turn, give them a Coin.
			cards.Add("TIME_054", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					EitherTurn = true,
					SingleTask = new CustomTask((g, c, s, t, stack) => AddToHand(g.CurrentPlayer, Cards.FromId("GAME_005")))
				}
			}));
			// [TIME_055] Unknown Voyager (5) — After this survives damage, transform into a random 7-Cost minion.
			cards.Add("TIME_055", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.SELF,
					Condition = new SelfCondition(p => p is Minion m && m.Health > 0 && !m.ToBeDestroyed && m.Zone?.Type == Zone.PLAY),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me && RandomMinionOfCost(g, V("TIME_055", "cost", 7)) is Card into) Generic.ChangeEntityBlock(c, me, into, true);
					})
				}
			}));
			// [TIME_057] Wizened Truthseeker (4) — Battlecry: Set the Cost of every card in both player's hands back to their original Costs.
			// (an enchantment setting each card's data Cost — cost auras still apply after: doubt)
			cards.Add("TIME_057", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable card in c.HandZone.GetAll().Concat(c.Opponent.HandZone.GetAll()).ToList())
						Give(g, s, card, "TIME_057e", card.Card.Cost);
				})
			}));
			cards.Add("TIME_057e", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) { UseScriptTag = true } }));
			// [TIME_058] Paltry Flutterwing (1) — Deathrattle: Summon a random 2-Cost minion that is Dormant for 2 turns.
			cards.Add("TIME_058", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (SummonGet(g, c, RandomMinionOfCost(g, V("TIME_058", "cost", 2)), s) is Minion m)
						Generic.MakeDormant(m, V("TIME_058", "turns", 2), "TIME_058e", (IPlayable)s);
				})
			}));
			// [TIME_059] Living Paradox (3) — Elusive. Battlecry: Summon two 2/1 Living Paradoxes with Elusive.
			cards.Add("TIME_059", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("TIME_059", "paradoxes", 2); i++) SummonGet(g, c, ((IPlayable)s).Card, s); })
			}));
			// [TIME_060] Quantum Destabilizer (3) — This minion takes double damage from all sources.
			cards.Add("TIME_060", new CardDef(new Power
			{
				// (live zilliax 55425: a TRIGGER block at index 0 with META CRITICAL_HIT on itself)
				Trigger = new Trigger(TriggerType.PREDAMAGE) { TriggerSource = TriggerSource.SELF, FastExecution = true, SingleTask = Generic.PreDamageMultiply(2, critical: true) }
			}));
			// [TIME_061] Timeless Causality (2) — Battlecry: Reverse the order of your deck.
			cards.Add("TIME_061", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => c.DeckZone.Reverse()) }));
			// [TIME_062] Chronicle Keeper (4) — Battlecry: If you're holding a Dragon, gain Taunt and Divine Shield.
			cards.Add("TIME_062", new CardDef(new Power
			{
				PowerTask = IfHoldingDragon(new CustomTask((g, c, s, t, stack) => { if (s is Minion me) { me[GameTag.TAUNT] = 1; me.HasDivineShield = true; } }))
			}));
			// [TIME_063] Timelord Nozdormu (3) — Dormant for 5 turns. Rush. After you play a card from the newest expansion, awaken 1 turn
			// sooner. (works while Dormant; not a Coin)
			cards.Add("TIME_063", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					WhileDormant = true,
					Condition = new SelfCondition(p => (int)p.Card.Set == Generic.NewestExpansion() && p.Card[(GameTag)2088] == 0 /* COIN_CARD */),
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me && me[GameTag.DORMANT] > 0) Generic.AwakenSooner(me, 1); })
				}
			}));
			// [TIME_064] Chrono-Lord Deios (7) — Your Battlecries, Deathrattles, Hero Power, and end of turn effects trigger twice. (live
			// dk-classxp 29029: TIME_064e on the player; EXTRA_BATTLECRIES_BASE, 755, EXTRA_DEATHRATTLES_BASE, 3428)
			cards.Add("TIME_064", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER,
					new Effect(GameTag.EXTRA_BATTLECRIES_BASE, EffectOperator.ADD, 1), new Effect(GameTag.EXTRA_DEATHRATTLES_BASE, EffectOperator.ADD, 1),
					new Effect(GameTag.EXTRA_END_TURN_EFFECT, EffectOperator.ADD, 1), new Effect(Generic.HERO_POWER_TWICE, EffectOperator.ADD, 1)) { ShowAs = "TIME_064e" }
			}));
			// [TIME_100] Hourglass Attendant (4) — Divine Shield. At the end of your turn, give all minions in your hand +1/+1.
			cards.Add("TIME_100", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END) { SingleTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.GetAll().Where(p => Generic.IsMinionCard(p)).ToList(), "TIME_100e")) }
			}));
			cards.Add("TIME_100e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TIME_100", "stats", 1))) }));
			// [TIME_102] Circadiamancer (3) — Battlecry: Add a random 8-Cost minion to your hand. At the start of your turns, reduce its Cost by (1).
			cards.Add("TIME_102", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.HandZone.IsFull || !(RandomCard(g, x => x.Type == CardType.MINION && x.Cost == V("TIME_102", "cost", 8)) is Card m)) return;
					IPlayable got = Entity.FromCard(c, m, new EntityData { [GameTag.CREATOR] = s.Id });
					if (Generic.AddHandPhase.Invoke(c, got)) Give(g, s, got, "TIME_102e");
				})
			}));
			cards.Add("TIME_102e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.ReduceCost(0)) { UseScriptTag = true },
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || e.Target?.Zone?.Type != Zone.HAND) return;
						Give(g, e.Creator ?? (IPlayable)e, (IPlayable)e.Target, "GBL_003e");
					})
				}
			}));
			// [TIME_103] Chromie (6) — Deathrattle: Draw another copy of cards you've played this game. (one per different card — doubt)
			Func<Controller, List<string>> playedIds = c => c.PlayHistory.Where(h => h.SourceController == c.PlayerId && h.SourceCard != null).Select(h => h.SourceCard.Id).Distinct().ToList();
			cards.Add("TIME_103", new CardDef(new Power
			{
				HandCounter = p => playedIds(p.Controller).Count(id => p.Controller.DeckZone.Any(x => x.Card.Id == id)),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (string id in playedIds(c)) DrawOne(g, c, p => p.Card.Id == id);
				})
			}));
			// [TIME_428] Yesterloc (2) — At the end of your turn, give your other minions +1 Health.
			cards.Add("TIME_428", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.BoardZone.GetAll().Where(m => m.Id != s.Id && !(m is Location)).ToList(), "TIME_428e"))
				}
			}));
			cards.Add("TIME_428e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("TIME_428", "health", 1))) }));
			// [TIME_434] Temporal Traveler (3) — Deathrattle: Summon a 4/1 Shadow that attacks a random enemy minion.
			cards.Add("TIME_434", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(SummonGet(g, c, Cards.FromId("TIME_434t"), s) is Minion shadow)) return;
					var enemies = BoardMinions(c.Opponent).Cast<Minion>().Where(m => !m.IsDead && !m.ToBeDestroyed && !m.Untouchable).ToList();
					if (enemies.Count > 0) EffectAttack(g, shadow, enemies[g.Random.Next(enemies.Count)]);
				})
			}));
			// [TIME_720] Soldier of the Bronze (5) — Taunt. Battlecry: Double this minion's Health.
			cards.Add("TIME_720", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => { if (s is Minion me) Give(g, s, me, "TIME_720e", me.Health); }) }));
			cards.Add("TIME_720e", new CardDef(new Power { Enchant = Enchants.Enchants.AddHealthScriptTag }));

			// ---------------------------------------------------------------- Demon Hunter
			// [END_006] Chronikar (5) — Battlecry: Give your hero +3 Attack this turn, next turn, and the turn after. (one enchantment through
			// three of your turns — doubt)
			cards.Add("END_006", new CardDef(new Power { PowerTask = new AddEnchantmentTask("END_006e", EntityType.HERO) }));
			cards.Add("END_006e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("END_006", "attack", 3))),
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e)) return;
						e[GameTag.TAG_SCRIPT_DATA_NUM_1] = e[GameTag.TAG_SCRIPT_DATA_NUM_1] + 1;
						if (e[GameTag.TAG_SCRIPT_DATA_NUM_1] >= V("END_006", "turns", 3)) e.Remove();
					})
				}
			}));
			// [TIME_020] Broxigar (2) — Fabled, Charge. Start of Game: Disappear. Kill all 4 Demons from Argus to reappear in hand. (live
			// 112456 567: set aside, the deck's First Portal carries its id; the Portals summon a Demon for the opponent whose Deathrattle
			// shuffles the next Portal into the Portal owner's deck; the last returns Broxigar)
			cards.Add("TIME_020", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.GAME_START)
				{
					TriggerActivation = TriggerActivation.DECK,
					EffectIndex = 1,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is IPlayable brox) || !Generic.StartOfGameHolds(brox)) return;
						brox[GameTag.TAG_SCRIPT_DATA_NUM_1] = 1;
						brox[GameTag.TAG_SCRIPT_DATA_NUM_2] = 1;
						foreach (IPlayable portal in c.DeckZone.GetAll().Where(p => p.Card.Id == "TIME_020t2" && p[GameTag.TAG_SCRIPT_DATA_NUM_1] == 0).ToList())
							portal[GameTag.TAG_SCRIPT_DATA_NUM_1] = brox.Id;
						brox.Zone.Remove(brox); // (its deck, or its opening hand)
						c.SetasideZone.Add(brox);
					})
				}
			}));
			string[] portals = { "TIME_020t2", "TIME_020t3", "TIME_020t4", "TIME_020t5" };
			for (int i = 0; i < portals.Length; i++)
			{
				string portal = portals[i], demon = portals[i] + "t", next = i + 1 < portals.Length ? portals[i + 1] : null;
				cards.Add(portal, new CardDef(new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (SummonGet(g, c.Opponent, Cards.FromId(demon), s) is Minion d) d[GameTag.TAG_SCRIPT_DATA_NUM_1] = s[GameTag.TAG_SCRIPT_DATA_NUM_1];
					})
				}));
				cards.Add(demon, new CardDef(new Power
				{
					DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
					{
						Controller owner = c.Opponent;
						int brox = s[GameTag.TAG_SCRIPT_DATA_NUM_1];
						if (next != null)
						{
							Generic.Draw(owner);
							if (!owner.DeckZone.IsFull)
								Generic.ShuffleIntoDeck.Invoke(owner, s, Entity.FromCard(owner, Cards.FromId(next), new EntityData
								{
									[GameTag.CREATOR] = s.Id, [GameTag.DISPLAYED_CREATOR] = s.Id, [GameTag.TAG_SCRIPT_DATA_NUM_1] = brox,
								}));
						}
						else if (g.IdEntityDic.TryGetValue(brox, out IPlayable b) && b.Zone?.Type == Zone.SETASIDE && !owner.HandZone.IsFull)
						{
							b.Zone.Remove(b);
							b[GameTag.TAG_SCRIPT_DATA_NUM_2] = 0;
							Generic.AddHandPhase.Invoke(owner, b);
						}
					})
				}));
			}
			// [TIME_020t1] Axe of Cenarius (3) — Lifesteal. After your hero attacks and kills a minion, draw a Portal to Argus.
			cards.Add("TIME_020t1", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					EffectIndex = 1,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (KilledItsDefender(g)) DrawOne(g, c, p => portals.Contains(p.Card.Id)); })
				}
			}));
			// [TIME_021] Doomsday Prepper (5) — Outcast: Your hero is Immune until your next turn. (live: Time Out!'s TRL_302e)
			cards.Add("TIME_021", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (c.OutcastPlayId == s.Id) Give(g, s, c.Hero, "TRL_302e"); })
			}));
			// [TIME_022] Perennial Serpent (8) — Rush. Costs (4) less if a minion is Dormant.
			cards.Add("TIME_022", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.BoardZone.GetSpan().ToArray().Concat(p.Controller.Opponent.BoardZone.GetSpan().ToArray())
					.Any(m => m != null && m[GameTag.DORMANT] > 0) ? V("TIME_022", "less", 4) : 0)
			}));
			// [TIME_441] Aeon Rend (4) — Rewind. Deal $4 damage to two random enemies.
			cards.Add("TIME_441", new CardDef(new Power
			{
				PowerTask = RewindPower((g, c, s, t) =>
				{
					foreach (ICharacter e in Enemies(c).Where(x => !x.IsDead && !x.ToBeDestroyed).OrderBy(_ => g.Random.Next()).Take(V("TIME_441", "enemies", 2)).ToList())
						Generic.DamageCharFunc((IPlayable)s, e, V("TIME_441", "damage", 4), true);
				})
			}));
			// [TIME_442] Timeway Warden (4) — Battlecry: Imprison an enemy minion. It goes Dormant for 10,000 turns. Deathrattle: Awaken it.
			cards.Add("TIME_442", new CardDef(BattlecryEnemyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m) || m.Zone?.Type != Zone.PLAY) return;
					Generic.MakeDormant(m, V("TIME_442", "turns", 10000), "TIME_442e", (IPlayable)s);
					s[HsTag.IMPRISONED] = m.Id;
				}),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (g.IdEntityDic.TryGetValue(s[HsTag.IMPRISONED], out IPlayable p) && p is Minion m && m.Zone?.Type == Zone.PLAY && m[GameTag.DORMANT] > 0) Generic.Awaken(m);
				})
			}));
			// [TIME_443] Hounds of Fury (4) — Summon two 3/2 Demons. If your deck has no minions, they attack the lowest Health enemy.
			cards.Add("TIME_443", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				GlowWhile = p => NoMinionsInDeck(p.Controller),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var hounds = new List<Minion>();
					foreach (string id in new[] { "TIME_443t2", "TIME_443t" })
						if (SummonGet(g, c, Cards.FromId(id), s) is Minion m) hounds.Add(m);
					if (!NoMinionsInDeck(c)) return;
					foreach (Minion hound in hounds)
					{
						if (hound.IsDead || hound.Zone?.Type != Zone.PLAY || !(LowestHealthEnemy(g, c) is ICharacter target)) continue;
						EffectAttack(g, hound, target);
						g.DeathProcessingAndAuraUpdate();
					}
				})
			}));
			// [TIME_444] Time-Lost Glaive (1) — Deathrattle: Get a random Demon from the past.
			cards.Add("TIME_444", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (Pick(g, Cards.GenerationPool(g, c, FormatType.FT_WILD).Where(x => x.Type == CardType.MINION && x.IsRace(Race.DEMON) && Cards.FromThePast(x)), 1).FirstOrDefault() is Card d)
						AddToHand(c, d);
				})
			}));
			// [TIME_446] The Eternal Hold (6) — Location: Discover any Demon that costs (5) or more. If your deck has no minions, your next one costs (1).
			cards.Add("TIME_446", new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.Where((c, x) => x.Type == CardType.MINION && x.IsRace(Race.DEMON) && x.Cost >= V("TIME_446", "cost", 5),
					afterPick: new CustomTask((g, c, s, t, stack) => { if (NoMinionsInDeck(c)) Generic.AddEnchantmentBlock(g, Cards.FromId("TIME_446e"), (IPlayable)s, c, 0, 0, 0); }))
			}));
			cards.Add("TIME_446e", NextCardCost(Effects.SetCost(V("TIME_446", "next cost", 1)), SelfCondition.IsMinion, null));
			// [TIME_448] Solitude (3) — Discover 2 minions. If your deck has no minions, reduce the Cost of any in your hand by (2).
			ISimpleTask solitudeDiscount = new CustomTask((g, c, s, t, stack) =>
			{
				if (NoMinionsInDeck(c)) GiveAll(g, s, c.HandZone.GetAll().Where(p => Generic.IsMinionCard(p)).ToList(), GenericCostLess(V("TIME_448", "less", 2)));
			});
			cards.Add("TIME_448", new CardDef(new Power
			{
				GlowWhile = p => NoMinionsInDeck(p.Controller),
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION, afterPick:
					DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION, afterPick: solitudeDiscount))
			}));
			// [TIME_449] Lasting Legacy (3) — Give your hero +4 Attack this turn. If your deck has no minions, give any in hand +4 Attack.
			cards.Add("TIME_449", new CardDef(new Power
			{
				GlowWhile = p => NoMinionsInDeck(p.Controller),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Give(g, s, c.Hero, "TIME_449e1");
					if (NoMinionsInDeck(c)) GiveAll(g, s, c.HandZone.GetAll().Where(p => Generic.IsMinionCard(p)).ToList(), "TIME_449e2");
				})
			}));
			cards.Add("TIME_449e1", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TIME_449", "attack", 4))) { IsOneTurnEffect = true } }));
			cards.Add("TIME_449e2", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TIME_449", "attack", 4))) }));

			// ---------------------------------------------------------------- Death Knight
			// [END_003] Finality (3) — Draw an Undead. Imbue your Hero Power twice.
			cards.Add("END_003", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					DrawWhere(g, c, IsUndead);
					for (int i = 0; i < V("END_003", "imbues", 2); i++) Generic.Imbue(c, s);
				})
			}));
			// [TIME_610] Shadows of Yesterday (6) — Rewind. Summon four 3/2 Shades. They each gain two random Bonus Effects.
			cards.Add("TIME_610", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = RewindPower((g, c, s, t) =>
				{
					var shades = new List<Minion>();
					for (int i = 0; i < V("TIME_610", "shades", 4); i++)
						if (SummonGet(g, c, Cards.FromId("TIME_610t2"), s) is Minion m) shades.Add(m);
					foreach (Minion m in shades) Generic.GainBonusEffects(g, m, V("TIME_610", "effects", 2));
				})
			}));
			// [TIME_611] Timestop (2) — Deal $3 damage. Freeze two random enemy minions.
			cards.Add("TIME_611", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter ch) Generic.DamageCharFunc((IPlayable)s, ch, V("TIME_611", "damage", 3), true);
					foreach (Minion m in c.Opponent.BoardZone.GetAll().Where(m => !(m is Location) && !m.IsDead && !m.ToBeDestroyed).OrderBy(_ => g.Random.Next()).Take(V("TIME_611", "freeze", 2)).ToList())
						m.IsFrozen = true;
				})
			}));
			// [TIME_612] Blood Draw (3) — Discover a spell. This costs Health instead of Mana. (its data's alternate cost: Playable.CostsHealth)
			cards.Add("TIME_612", new CardDef(new Power { PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.SPELL) }));
			// [TIME_613] Cryofrozen Champion (1) — Deathrattle: Get a random Legendary minion. Reduce its Cost by (1).
			cards.Add("TIME_613", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.HandZone.IsFull || !(RandomCard(g, x => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY) is Card m)) return;
					IPlayable got = Entity.FromCard(c, m, new EntityData { [GameTag.CREATOR] = s.Id });
					if (Generic.AddHandPhase.Invoke(c, got)) GiveCostLess(g, s, got, V("TIME_613", "less", 1));
				})
			}));
			// [TIME_614] Liferender (3) — Battlecry: If your hero's Health changed this turn, deal 6 damage to an enemy minion.
			bool HealthChanged(Controller c) => c[HsTag.HERO_HEALTH_CHANGED_TURN] == c.Game.Turn;
			cards.Add("TIME_614", new CardDef(BattlecryEnemyMinion, new Power
			{
				TargetIf = (PlayReq.REQ_TARGET_IF_AVAILABLE, (src, target) => HealthChanged(src.Controller)),
				GlowWhile = p => HealthChanged(p.Controller),
				PowerTask = new CustomTask((g, c, s, t, stack) => { if (t is Minion m && HealthChanged(c)) Generic.DamageCharFunc((IPlayable)s, m, V("TIME_614", "damage", 6), false); })
			}));
			// [TIME_615] Forgotten Millennium (8) — Fill your hand with random Undead. They cost Health instead of Mana this turn.
			cards.Add("TIME_615", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int guard = 0; guard < 10 && !c.HandZone.IsFull; guard++)
					{
						if (!(RandomCard(g, x => x.Type == CardType.MINION && x.IsRace(Race.UNDEAD)) is Card u)) break;
						IPlayable got = Entity.FromCard(c, u, new EntityData { [GameTag.CREATOR] = s.Id });
						if (Generic.AddHandPhase.Invoke(c, got)) Give(g, s, got, "TIME_615e");
					}
				})
			}));
			cards.Add("TIME_615e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.CARD_COSTS_HEALTH, EffectOperator.ADD, 1)) { IsOneTurnEffect = true } }));
			// [TIME_616] Memoriam Manifest (4) — Summon the highest Cost friendly Undead that died this game.
			cards.Add("TIME_616", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var undead = c.DiedThisGameCards.Where(p => p is Minion && p.Card.IsRace(Race.UNDEAD)).ToList();
					if (undead.Count == 0 || c.BoardZone.IsFull) return;
					int top = undead.Max(p => p.Card.Cost);
					var best = undead.Where(p => p.Card.Cost == top).ToList();
					Generic.SummonBlock.Invoke(g, (Minion)Generic.NewFromGraveyard(c, best[g.Random.Next(best.Count)]), -1, s);
				})
			}));
			// [TIME_617] Chronochiller (4) — You no longer draw a card at the start of your turn. (client player tag 4316; its empty turn-start block)
			cards.Add("TIME_617", new CardDef(new Power
			{
				Aura = new Aura(AuraType.CONTROLLER, new Effect((GameTag)4316, EffectOperator.ADD, 1)),
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = new CustomTask((g, c, s, t, stack) => { }) }
			}));
			// [TIME_618] Husk, Eternal Reaper (4) — Battlecry: Give your hero "Deathrattle: Spend up to 20 Corpses to resurrect with that much
			// Health." (one at a time — wiki; HsTag.HERO_RESURRECT_CORPSES, Game.CheckGameOver)
			cards.Add("TIME_618", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c[HsTag.HERO_RESURRECT_CORPSES] > 0) return;
					c[HsTag.HERO_RESURRECT_CORPSES] = V("TIME_618", "corpses", 20);
					Generic.AddEnchantmentBlock(g, Cards.FromId("TIME_618e"), (IPlayable)s, c, 0, 0, 0);
				})
			}));
			cards.Add("TIME_618e", new CardDef(new Power { NoScript = true }));
			cards.Add("TIME_618e2", new CardDef(new Power { NoScript = true }));
			// [TIME_619] Talanji of the Graves (4) — Fabled. Battlecry: Draw Bwonsamdi (or resurrect him if he has died). Choose a Boon to give him.
			ISimpleTask chooseBoon = new CustomTask((g, c, s, t, stack) =>
			{
				Card[] boons = new[] { "TIME_619t3", "TIME_619t4", "TIME_619t5" }.Select((id, i) => (id, i)).Where(x => (c[HsTag.BWONSAMDI_BOONS] & (1 << x.i)) == 0)
					.Select(x => Cards.FromId(x.id)).ToArray();
				if (boons.Length > 0) Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.CAST, boons, null);
			});
			cards.Add("TIME_619", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) =>
				{
					var dead = c.DiedThisGameCards.Where(p => p.Card.Id == "TIME_619t").ToList();
					if (dead.Count > 0) { if (!c.BoardZone.IsFull) Generic.SummonBlock.Invoke(g, (Minion)Generic.NewFromGraveyard(c, dead.Last()), -1, s); }
					else DrawOne(g, c, p => p.Card.Id == "TIME_619t");
				}), chooseBoon)
			}));
			cards.Add("TIME_619t2", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new CustomTask((g, c, s, t, stack) => DamageAll(s, Enemies(c), V("TIME_619t2", "damage", 2), true)), chooseBoon)
			}));
			string[] boonIds = { "TIME_619t3", "TIME_619t4", "TIME_619t5" };
			for (int i = 0; i < boonIds.Length; i++)
			{
				int bit = 1 << i;
				cards.Add(boonIds[i], new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => c[HsTag.BWONSAMDI_BOONS] = c[HsTag.BWONSAMDI_BOONS] | bit) }));
			}
			// [TIME_619t] Bwonsamdi (6) — Deathrattle: Summon a random @-Cost minion. (Any Boons given to Bwonsamdi carry over.) (@ = 4 + 2 per Boon;
			// each Boon its keyword — the player's Boons, an aura on him: wiki "cannot be removed with Silence")
			Func<IPlayable, int> boonCount = p => Enumerable.Range(0, 3).Count(i => (p.Controller[HsTag.BWONSAMDI_BOONS] & (1 << i)) != 0);
			cards.Add("TIME_619t", new CardDef(new Power
			{
				HandCounter = p => V("TIME_619t", "cost", 4) + V("TIME_619t", "boon cost", 2) * boonCount(p),
				Trigger = new Trigger(TriggerType.AFTER_SUMMON)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Minion me)) return;
						int boons = c[HsTag.BWONSAMDI_BOONS];
						if ((boons & 1) != 0) me[GameTag.TAUNT] = 1;
						if ((boons & 2) != 0) me[GameTag.LIFESTEAL] = 1;
						if ((boons & 4) != 0) me.IsRush = true;
					})
				},
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
					SummonGet(g, c, RandomMinionOfCost(g, V("TIME_619t", "cost", 4) + V("TIME_619t", "boon cost", 2) * boonCount((IPlayable)s)), s))
			}));
			// [END_001] Jagged Edge of Time (3) — Battlecry: Imbue your Hero Power.
			cards.Add("END_001", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => Generic.Imbue(c, s)) }));
			// [END_002] Wicked Blightspawn (4) — Reborn. Deathrattle: Equip a 1/2 Dagger. If you already have a weapon equipped, give it +2 Attack instead.
			cards.Add("END_002", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.Hero.Weapon is Weapon w && !w.ToBeDestroyed) Give(g, s, w, "END_002e");
					else new WeaponTask("CS2_082").Process(g, c, s, null);
				})
			}));
			cards.Add("END_002e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.ATK, EffectOperator.ADD, V("END_002", "attack", 2))) }));
			// [END_004] Remnant of Rage (7) — Costs (1) less for each minion that died this turn. Battlecry: Draw 2 cards.
			cards.Add("END_004", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => (p.Controller.NumFriendlyMinionsThatDiedThisTurn + p.Controller.Opponent.NumFriendlyMinionsThatDiedThisTurn) * V("END_004", "less", 1)),
				PowerTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("END_004", "draws", 2); i++) Generic.Draw(c); })
			}));

			// ---------------------------------------------------------------- the mini-set's two-class cards
			// [END_007] Press the Advantage (2) — Deal $1 damage. Give your hero +1 Attack this turn. Draw 1 card. Gain 1 Armor.
			cards.Add("END_007", new CardDef(TargetAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is ICharacter ch) Generic.DamageCharFunc((IPlayable)s, ch, V("END_007", "damage", 1), true);
					Give(g, s, c.Hero, "END_007e1");
					Generic.Draw(c);
					new ArmorTask(V("END_007", "armor", 1)).Process(g, c, s, null);
				})
			}));
			cards.Add("END_007e1", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("END_007", "attack", 1))) { IsOneTurnEffect = true } }));
			// [END_008] Enduring Roach (3) — After you use your Hero Power, refresh 2 Mana Crystals.
			cards.Add("END_008", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.INSPIRE) { SingleTask = new CustomTask((g, c, s, t, stack) => RefreshMana(c, V("END_008", "mana", 2))) }
			}));
			// [END_010] Twilight Timereaver (6) — Choose One - Set the Attack of all other minions to 1; or Health to 1.
			cards.Add("END_010", new CardDef(new Power()));
			cards.Add("END_010a", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, BoardMinions(c).Concat(BoardMinions(c.Opponent)).Where(m => m.Id != s.Id).Cast<IPlayable>().ToList(), "END_010ae"))
			}));
			cards.Add("END_010b", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, BoardMinions(c).Concat(BoardMinions(c.Opponent)).Where(m => m.Id != s.Id).Cast<IPlayable>().ToList(), "END_010be"))
			}));
			cards.Add("END_010ae", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttack(V("END_010", "attack", 1))) }));
			cards.Add("END_010be", new CardDef(new Power { Enchant = new Enchant(Effects.SetMaxHealth(V("END_010", "health", 1))) }));
			// [END_011] Acceleration Aura (2) — At the start of your turn, gain a temporary Mana Crystal. Lasts @ turns. (counts at the start)
			cards.Add("END_011", new CardDef(PaladinAura(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START) { SingleTask = new CustomTask((g, c, s, t, stack) => c.TemporaryMana += V("END_011", "mana", 1)) }
			}, atStart: true)));
			// [END_013] Brutish Endmaw (3) — Battlecry: Discover a 1-Cost minion with a Dark Gift.
			cards.Add("END_013", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => DiscoverWithDarkGift(g, c, s, ClassOrNeutralMinions(g, c, x => x.Cost == V("END_013", "cost", 1))))
			}));
			// [END_014] Synchronized Spark (4) — Deal $3 damage to an enemy. If it dies, give a random friendly minion +3/+3.
			cards.Add("END_014", new CardDef(TargetEnemy, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is ICharacter ch)) return;
					Generic.DamageCharFunc((IPlayable)s, ch, V("END_014", "damage", 3), true);
					if ((ch.IsDead || ch.ToBeDestroyed || ch.Health <= 0) && RandomFriendly(g, c) is Minion m) Give(g, s, m, "END_014e");
				})
			}));
			cards.Add("END_014e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("END_014", "stats", 3))) }));
			// [END_016] Chronoclaws (4) — After your hero attacks, discard your highest Cost card.
			cards.Add("END_016", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var hand = c.HandZone.GetAll().ToList();
						if (hand.Count == 0) return;
						int top = hand.Max(p => p.Cost);
						var highest = hand.Where(p => p.Cost == top).ToList();
						Generic.DiscardBlock.Invoke(c, highest[g.Random.Next(highest.Count)]);
					})
				}
			}));
			// [END_017] Battle at the End Time (1) — Quest: Fill your hand, then empty it. Reward: Tick and Tock. (checked as the game
			// settles: full, then empty — Power.StateCheck)
			cards.Add("END_017", new CardDef(new Power
			{
				StateCheck = p => p is Spell q && p.Controller.SecretZone.IsActiveQuest(q) && (q.QuestProgress == 0 && p.Controller.HandZone.IsFull || q.QuestProgress == 1 && p.Controller.HandZone.Count == 0),
				StateTask = new CustomTask((g, c, s, t, stack) => QuestStep(g, c, s as Spell))
			}));
			// [END_017t] Tick and Tock (5) — Battlecry: Draw until your hand is full. Deathrattle: Empty the opponent's hand. (a discard — doubt)
			cards.Add("END_017t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { while (!c.HandZone.IsFull && c.DeckZone.Count > 0 && Generic.Draw(c) != null) { } }),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable card in c.Opponent.HandZone.GetAll().ToList()) Generic.DiscardBlock.Invoke(c.Opponent, card);
				})
			}));
			// [END_019] Endtime Survivor (5) — Taunt. Battlecry: If your hero took damage this turn, gain +3/+3.
			cards.Add("END_019", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.Hero.DamageTakenThisTurn > 0, new CustomTask((g, c, s, t, stack) => { if (s is Minion me) Give(g, s, me, "END_019e"); }))
			}));
			cards.Add("END_019e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("END_019", "stats", 3))) }));
			// [END_020] Eternal Toil (1) — Deal $1 damage to a minion. If it survives, draw a card. If it dies, summon a random 1-Cost minion.
			cards.Add("END_020", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Generic.DamageCharFunc((IPlayable)s, m, V("END_020", "damage", 1), true);
					if (m.IsDead || m.ToBeDestroyed || m.Health <= 0) SummonGet(g, c, RandomMinionOfCost(g, V("END_020", "cost", 1)), s);
					else Generic.Draw(c);
				})
			}));
			// [END_022] Time-Twisted Seer (1) — Has Spell Damage +2 while damaged.
			cards.Add("END_022", new CardDef(new Power { Aura = new EnrageEffect(AuraType.SELF, "END_022e") }));
			cards.Add("END_022e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.SPELLPOWER, EffectOperator.ADD, V("END_022", "spell damage", 2))) }));
			// [END_023] Bitter End (5) — Freeze a minion and its neighbors. Destroy any that are damaged.
			cards.Add("END_023", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					var hit = new List<Minion> { m };
					if (m.Zone?.Type == Zone.PLAY) hit.AddRange(Neighbors(m));
					foreach (Minion x in hit) x.IsFrozen = true;
					foreach (Minion x in hit.Where(x => x.Damage > 0)) Generic.Destroy(x, (IPlayable)s);
				})
			}));
			// [END_025] Eternal Firebolt (3) — Lifesteal. Deal $3 damage to a minion. If it dies, return this to your hand at the end of your turn.
			// (live dredge 1557: END_025e on the player gives a NEW one at the end of the turn, leaves at the next turn's start)
			cards.Add("END_025", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Generic.DamageCharFunc((IPlayable)s, m, V("END_025", "damage", 3), true);
					if (m.IsDead || m.ToBeDestroyed || m.Health <= 0) Generic.AddEnchantmentBlock(g, Cards.FromId("END_025e"), (IPlayable)s, c, 0, 0, 0);
				})
			}));
			cards.Add("END_025e", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.TURN_END)
					{
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (!(s is Enchantment e) || e[GameTag.TAG_SCRIPT_DATA_NUM_1] > 0) return;
							e[GameTag.TAG_SCRIPT_DATA_NUM_1] = 1;
							if (!c.HandZone.IsFull)
								Generic.AddHandPhase.Invoke(c, Entity.FromCard(c, e.Creator?.Card ?? Cards.FromId("END_025"), new EntityData { [GameTag.CREATOR] = e.Id, [GameTag.DISPLAYED_CREATOR] = e.Id }));
						})
					},
					new Trigger(TriggerType.TURN_START)
					{
						EitherTurn = true,
						EffectIndex = 1,
						SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Enchantment e && e[GameTag.TAG_SCRIPT_DATA_NUM_1] > 0) e.Remove(); })
					})
			}));
			// [END_026] Fragment of Nothing (4) — After you cast a spell on a minion, draw a card.
			cards.Add("END_026", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable spell = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (spell != null && g.IdEntityDic.TryGetValue(spell[GameTag.CARD_TARGET], out IPlayable target) && target is Minion) Generic.Draw(c);
					})
				}
			}));
			// [END_028] For All Time (4) — Destroy all minions with 4 or less Attack. Overload: (2)
			cards.Add("END_028", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in BoardMinions(c).Concat(BoardMinions(c.Opponent)).Cast<Minion>().Where(m => m.AttackDamage <= V("END_028", "attack", 4)).ToList())
						Generic.Destroy(m, (IPlayable)s);
				})
			}));
			// [END_029] Voodoo Totem (2) — At the end of your turn, get a random Shadow spell. (any class: live)
			cards.Add("END_029", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (RandomCard(g, x => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == 6) is Card spell) AddToHand(c, spell); })
				}
			}));
			// [END_032] Winged Aberration (4) — Rush. Combo: Overload for (2) to gain Immune this turn and Windfury.
			cards.Add("END_032", new CardDef(new Power
			{
				ComboTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					Give(g, s, me, "END_032e");
					me.HasWindfury = true;
					c.OverloadOwed += V("END_032", "overload", 2);
					c.OverloadThisGame += V("END_032", "overload", 2);
				})
			}));
			cards.Add("END_032e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.IMMUNE, EffectOperator.SET, 1)) { IsOneTurnEffect = true } }));
		}
	}
}
