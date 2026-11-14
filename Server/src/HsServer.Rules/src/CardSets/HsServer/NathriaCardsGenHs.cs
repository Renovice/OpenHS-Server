// HsServer addition (not upstream SabberStone): Murder at Castle Nathria and Maw and Disorder (2022), as the client-only
// CORE_REV / CORE_MAW copies of the former Core set (the engine lacks the originals). Infuse itself is generic
// (Game.InfuseStep): an Infuse card turns into its infused version (a separate card) or, where the infused effect is scripted
// in place, just gets INFUSED. Every number goes through CardValues (V).
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
		/// <summary>Internal mark: this card is a copy of one of the opponent's cards (Theft Accusation, Harvester of Envy...).</summary>
		private const GameTag COPIED_FROM_OPPONENT = HsTag.COPIED_FROM_OPPONENT; // (VIOLET_HOLD: shared, Generic.Copy marks it)

		/// <summary>Internal: the player who made an Accusation (Arson / Murder / Theft).</summary>
		private const GameTag ACCUSER = (GameTag)990006;

		/// <summary>Internal per-player counter: Relics played ("Improve your future Relics").</summary>
		private const GameTag RELICS_PLAYED = (GameTag)990007;

		/// <summary>Internal mark: a copy made by Invitation Courier (so it isn't copied again).</summary>
		private const GameTag COPIED_BY_COURIER = (GameTag)990008;

		private static bool IsInfused(IEntity e) => e[Game.INFUSED] > 0;

		/// <summary>A copy of one of the opponent's cards added to this player's hand (marked as copied).</summary>
		private static void AddOpponentCopy(Controller c, Card card)
		{
			if (c.HandZone.IsFull) return;
			IPlayable copy = Entity.FromCard(c, card);
			copy[COPIED_FROM_OPPONENT] = 1;
			Generic.AddHandPhase.Invoke(c, copy);
		}

		/// <summary>A guess between the real card and two others of the same kind; guessing right adds a copy of it.</summary>
		private static void Guess(Game g, Controller guesser, IEntity source, Card real, bool fromOpponent)
		{
			var fakes = Pick(g, Cards.GenerationPool(g, guesser).Where(x => x.Id != real.Id && x.Type == real.Type && x.Class == real.Class), 2);
			fakes.Insert(g.Random.Next(fakes.Count + 1), real);
			Generic.CreateChoiceCards.Invoke(guesser, source, null, ChoiceType.GENERAL, ChoiceAction.STACK, fakes.ToArray(),
				new CustomTask((g2, c2, s2, chosen, stack2) =>
				{
					if (chosen?.Card.Id != real.Id) return;
					if (fromOpponent) AddOpponentCopy(c2, real);
					else AddToHand(c2, real);
				}));
		}

		/// <summary>Suspicious cards: Discover; the opponent may guess the pick and get a copy (approximated: a 1 in 3 chance).</summary>
		private static CardDef Suspicious(string id, Func<Card, bool> pool) => new CardDef(new Power
		{
			PowerTask = new CustomTask((g, c, s, t, stack) =>
			{
				var offer = Pick(g, Cards.GenerationPool(g, c).Where(pool), 3);
				if (offer.Count == 0 || c.HandZone.IsFull) return;
				Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.HAND, offer.ToArray(),
					new CustomTask((g2, c2, s2, chosen, stack2) =>
					{
						if (chosen != null && g2.Random.Next(3) == 0) AddOpponentCopy(c2.Opponent, chosen.Card);
					}));
			})
		});

		/// <summary>An Accusation: the chosen minion is destroyed when <paramref name="happens"/> (checked on each event).</summary>
		private static CardDef Accusation(string enchantment) => new CardDef(TargetMinion, new Power
		{
			PowerTask = new CustomTask((g, c, s, t, stack) =>
			{
				if (!(t is Minion m)) return;
				Generic.AddEnchantmentBlock(g, Cards.FromId(enchantment), (IPlayable)s, m, 0, 0, 0);
				m[ACCUSER] = c.PlayerId;
			})
		});

		private static CardDef AccusedBy(TriggerType type, Func<Game, Enchantment, IPlayable, bool> happens) => new CardDef(new Power
		{
			Trigger = new Trigger(type)
			{
				TriggerSource = TriggerSource.ALL,
				SingleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Enchantment e) || !(e.Target is Minion accused) || accused.IsDead || accused.ToBeDestroyed) return;
					if (happens(g, e, t)) accused.Destroy();
				})
			}
		});

		private static int Relics(Controller c) => c[RELICS_PLAYED];

		private static void RelicPlayed(Controller c) => c[RELICS_PLAYED] = c[RELICS_PLAYED] + 1;

		private static void Nathria(IDictionary<string, CardDef> cards)
		{

			// ---------------------------------------------------------------- Maw and Disorder

			// [MAW_000] Imp-oster (2) — Battlecry: Choose a friendly Imp. Transform into a copy of it.
			cards.Add("CORE_MAW_000", new CardDef(BattlecryFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (t is Minion imp && imp.Id != s.Id && IsImp(imp) && s is Minion me) Generic.TransformBlock.Invoke(c, imp.Card, me);
				})
			}));
			// [MAW_001] Arson Accusation (0) — Choose a minion. Destroy it after your hero takes damage.
			cards.Add("CORE_MAW_001", Accusation("MAW_001e2"));
			cards.Add("MAW_001e2", AccusedBy(TriggerType.TAKE_DAMAGE, (g, e, t) => t is Hero h && h.Controller.PlayerId == e.Target[ACCUSER]));
			// [MAW_019] Murder Accusation (1) — Choose a minion. Destroy it after another enemy minion dies.
			cards.Add("CORE_MAW_019", Accusation("MAW_019e2"));
			cards.Add("MAW_019e2", AccusedBy(TriggerType.DEATH, (g, e, t) =>
				t is Minion dead && dead.Id != e.Target.Id && dead.Controller.PlayerId != e.Target[ACCUSER]));
			// [MAW_023] Theft Accusation (1) — Choose a minion. Destroy it after you play a card copied from the opponent.
			cards.Add("CORE_MAW_023", Accusation("MAW_023e2"));
			cards.Add("MAW_023e2", AccusedBy(TriggerType.PLAY_CARD, (g, e, t) =>
				t != null && t[COPIED_FROM_OPPONENT] > 0 && t.Controller.PlayerId == e.Target[ACCUSER]));

			// [MAW_002] Habeas Corpses (1) — Discover a friendly minion to resurrect and give it Rush. It dies at the end of turn.
			cards.Add("CORE_MAW_002", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var died = c.DiedThisGameCards.Where(p => p is Minion).Select(p => p.Card).GroupBy(x => x.Id).Select(x => x.First());
					var offer = Pick(g, died, 3);
					if (offer.Count == 0) return;
					Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.STACK, offer.ToArray(),
						new CustomTask((g2, c2, s2, chosen, stack2) =>
						{
							if (chosen == null || c2.BoardZone.IsFull) return;
							var m = (Minion)Entity.FromCard(c2, chosen.Card);
							Generic.SummonBlock.Invoke(g2, m, -1, s2);
							m.IsRush = true;
							Give(g2, s2, m, "MAW_002e");
						}));
				})
			}));
			cards.Add("MAW_002e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					EitherTurn = true,
					SingleTask = new CustomTask((g, c, s, t, stack) => { if (s is Enchantment e && e.Target is Minion m) m.Destroy(); })
				}
			}));

			// [MAW_003] Totemic Evidence (1) — Choose a basic Totem and summon it. Infuse (3 Totems): Summon all 4 instead.
			string[] basicTotems = { "NEW1_009", "CS2_050", "CS2_051", "CS2_052" };
			cards.Add("CORE_MAW_003", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.SUMMON, basicTotems.Select(Cards.FromId).ToArray(), null))
			}));
			cards.Add("MAW_003t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => { foreach (string totem in basicTotems) Summon(g, c, Cards.FromId(totem), s); })
			}));

			// [MAW_004] Soul Seeker (3) — Battlecry: Swap this with a random minion from your opponent's deck.
			cards.Add("CORE_MAW_004", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var theirs = c.Opponent.DeckZone.Where(p => Generic.IsMinionCard(p)).ToList();
					if (theirs.Count == 0 || !(s is Minion me)) return;
					// the two minions themselves trade places (moves, not copies: the taken deck card used to stay behind as a
					// ghost): this one into the opponent's deck (MoveToDeck: reset, new owner), theirs into its slot
					var swapped = (Minion)theirs[g.Random.Next(theirs.Count)];
					int position = me.ZonePosition;
					new MoveToDeck(EntityType.SOURCE, opponent: true).Process(g, c, s, null);
					Generic.SummonBlock(g, (Minion)Generic.GiveTo(swapped, c), position, s);
				})
			}));
			// [MAW_005] Framester (3) — Battlecry: Shuffle 3 'Framed' cards into the opponent's deck. When drawn, they Overload for (2).
			cards.Add("CORE_MAW_005", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("MAW_005", "framed", 3); i++)
						Generic.ShuffleIntoDeck.Invoke(c.Opponent, s, Entity.FromCard(c.Opponent, Cards.FromId("MAW_005t")));
				})
			}));
			cards.Add("MAW_005t", new CardDef(new Power
			{
				TopdeckTask = new CustomTask((g, c, s, t, stack) => Generic.AddOverload(c, V("MAW_005t", "overload", 2))) // (WONDERS: the shared Overload rule)
			}));
			// [MAW_006] Objection! (1) — Secret: When your opponent plays a minion, Counter it.
			cards.Add("CORE_MAW_006", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.PLAY_MINION)
				{
					TriggerSource = TriggerSource.ENEMY,
					SingleTask = ComplexTask.Secret(new CounterTask(EntityType.TARGET))
				}
			}));
			// [MAW_008] Sightless Magistrate (3) — Battlecry: Both players draw until they have 5 cards.
			cards.Add("CORE_MAW_008", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Controller p in new[] { c, c.Opponent })
						for (int guard = 0; p.HandZone.Count < V("MAW_008", "hand", 5) && guard < 10; guard++)
							if (Generic.Draw(p) == null) break;
				})
			}));
			// [MAW_009] Shadehound (5) — Whenever this attacks, give your other Beasts +2/+2. Infuse (3 Beasts): Gain Rush.
			var shadehound = new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
						GiveAll(g, s, c.BoardZone.GetAll().Where(m => m.Id != s.Id && m.IsRace(Race.BEAST)), "MAW_009e"))
				}
			});
			cards.Add("CORE_MAW_009", shadehound);
			cards.Add("MAW_009t", shadehound);
			cards.Add("MAW_009e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("MAW_009", "stats", 2))) }));
			// [MAW_010] Motion Denied (1) — Secret: After your opponent plays three cards in a turn, deal 6 damage to the enemy hero.
			cards.Add("CORE_MAW_010", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.ENEMY,
					Condition = new SelfCondition(p => p.Controller.NumCardsPlayedThisTurn == 3),
					SingleTask = ComplexTask.Secret(new DamageTask(V("MAW_010", "damage", 6), EntityType.OP_HERO, true))
				}
			}));
			// [MAW_011] Defense Attorney Nathanos (5) — Battlecry: Discover a friendly Deathrattle minion that died this game. Gain
			// its Deathrattle and then trigger it.
			cards.Add("CORE_MAW_011", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var died = c.DiedThisGameCards.Where(p => p is Minion && p.Card.Deathrattle).Select(p => p.Card)
						.GroupBy(x => x.Id).Select(x => x.First());
					var offer = Pick(g, died, 3);
					if (offer.Count == 0) return;
					IEntity nathanos = s;
					Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.STACK, offer.ToArray(),
						new CustomTask((g2, c2, s2, chosen, stack2) =>
						{
							if (chosen == null) return;
							nathanos[GameTag.TAG_SCRIPT_DATA_NUM_2] = chosen.Card.AssetId;
							Give(g2, nathanos, (IPlayable)nathanos, "DRG_031e"); // "copied Deathrattle" (Necrium Apothecary's)
							chosen.Card.Power?.DeathrattleTask?.Process(g2, c2, nathanos, null);
						}));
				})
			}));
			// [MAW_012] All Fel Breaks Loose (2) — Summon a friendly Demon that died this game. Infuse (3 Demons): Summon three.
			ISimpleTask FelBreaksLoose(int n) => new CustomTask((g, c, s, t, stack) =>
			{
				var demons = c.DiedThisGameCards.Where(p => p is Minion m && m.IsRace(Race.DEMON)).Select(p => p.Card).ToList();
				foreach (Card demon in Pick(g, demons, n)) Summon(g, c, demon, s);
			});
			cards.Add("CORE_MAW_012", new CardDef(new Power { PowerTask = FelBreaksLoose(1) }));
			cards.Add("MAW_012t", new CardDef(new Power { PowerTask = FelBreaksLoose(V("MAW_012t", "demons", 3)) }));
			// [MAW_013] Life Sentence (6) — Remove a minion from the game.
			cards.Add("CORE_MAW_013", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.RemoveFromGame(t, s)) // (Actions/Titans.cs: no death, no Deathrattle)
			}));
			// [MAW_014] Prosecutor Mel'tranix (5) — Battlecry: Your opponent can only play their left- and right-most cards on
			// their next turn. (MAW_014e2 on them sets HsTag.ONLY_OUTER_CARDS_PLAYABLE.)
			cards.Add("CORE_MAW_014", new CardDef(new Power { PowerTask = new AddEnchantmentTask("MAW_014e2", EntityType.OP_CONTROLLER) }));
			cards.Add("MAW_014e2", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(HsTag.ONLY_OUTER_CARDS_PLAYABLE, EffectOperator.SET, 1)),
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					EitherTurn = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						// Lasts through the opponent's next turn: removed at the end of that player's turn.
						if (s is Enchantment e && g.CurrentPlayer == e.Target as Controller) RemoveEnchantmentTask.Task.Process(e.Game, e.Controller, e, null); // reverts its effects too
					})
				}
			}));
			// [MAW_015] Jury Duty (3) — Summon two Silver Hand Recruits. Give your Silver Hand Recruits +1/+1.
			cards.Add("CORE_MAW_015", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } }, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < 2; i++) Summon(g, c, Cards.FromId("CS2_101t"), s);
					GiveAll(g, s, c.BoardZone.GetAll().Where(IsRecruit), "MAW_015e");
				})
			}));
			cards.Add("MAW_015e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("MAW_015", "stats", 1))) }));
			// [MAW_016] Order in the Court (1) — Reorder your deck from highest Cost to lowest Cost. Draw a card.
			cards.Add("CORE_MAW_016", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var deck = c.DeckZone.ToList();
					foreach (IPlayable p in deck) c.DeckZone.Remove(p);
					foreach (IPlayable p in deck.OrderBy(p => p.Cost)) c.DeckZone.Add(p); // the top (drawn first) is the highest Cost
					Generic.Draw(c);
				})
			}));
			// [MAW_017] Class Action Lawyer (2) — Battlecry: If your deck has no Neutral cards, set a minion's stats to 1/1.
			cards.Add("CORE_MAW_017", new CardDef(BattlecryMinion, new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => !c.DeckZone.Any(p => p.Card.HasClass(CardClass.NEUTRAL)),
					new CustomTask((g, c, s, t, stack) => { if (t is Minion m) Give(g, s, m, "MAW_017e"); }))
			}));
			cards.Add("MAW_017e", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(1)) }));
			// [MAW_018] Perjury (1) — Secret: When your turn starts, Discover and cast a Secret from another class.
			cards.Add("CORE_MAW_018", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					SingleTask = ComplexTask.Secret(new CustomTask((g, c, s, t, stack) =>
					{
						var offer = Pick(g, Cards.GenerationPool(g, c).Where(x => x.IsSecret && !x.HasClass(c.HeroClass)), 3);
						if (offer.Count > 0)
							Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.CAST, offer.ToArray(), null);
					}))
				}
			}));
			// [MAW_020] Scribbling Stenographer (4) — Rush. Costs (1) less for each card you've played this turn.
			cards.Add("CORE_MAW_020", new CardDef(new Power { Aura = new AdaptiveCostEffect(p => p.Controller.NumCardsPlayedThisTurn) }));
			// [MAW_021] Clear Conscience (2) — Give a friendly minion +2/+3 and "Elusive on your opponent's turn."
			cards.Add("CORE_MAW_021", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 }, { PlayReq.REQ_MINION_TARGET, 0 } },
				new Power { PowerTask = new AddEnchantmentTask("MAW_021e", EntityType.TARGET) }));
			cards.Add("MAW_021e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("MAW_021", "attack", 2)), Effects.Health_N(V("MAW_021", "health", 3))),
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.TURN_START)
					{
						EitherTurn = true,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (!(s is Enchantment e) || !(e.Target is Minion m)) return;
							bool elusive = g.CurrentPlayer != m.Controller;
							m[GameTag.CANT_BE_TARGETED_BY_SPELLS] = elusive ? 1 : 0;
							m[GameTag.CANT_BE_TARGETED_BY_HERO_POWERS] = elusive ? 1 : 0;
						})
					})
			}));
			// [MAW_022] Incriminating Psychic (4) — Taunt. Deathrattle: Copy two random cards from your opponent's hand.
			cards.Add("CORE_MAW_022", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Card card in Pick(g, c.Opponent.HandZone.Select(p => p.Card), V("MAW_022", "cards", 2))) AddOpponentCopy(c, card);
				})
			}));
			// [MAW_024] Dew Process (0) — For the rest of the game, players draw an extra card at the start of their turn.
			cards.Add("CORE_MAW_024", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(new AddEnchantmentTask("MAW_024e3", EntityType.CONTROLLER), new AddEnchantmentTask("MAW_024e3", EntityType.OP_CONTROLLER))
			}));
			cards.Add("MAW_024e3", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_START)
				{
					EitherTurn = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Enchantment e && e.Target is Controller p && g.CurrentPlayer == p) Generic.Draw(p);
					})
				}
			}));
			// [MAW_025] Attorney-at-Maw (5) — Choose One - Silence a minion; or Give a minion Immune this turn.
			cards.Add("CORE_MAW_025", new CardDef(new Power()));
			cards.Add("MAW_025a", new CardDef(TargetMinion, new Power { PowerTask = new SilenceTask(EntityType.TARGET) }));
			cards.Add("MAW_025b", new CardDef(TargetMinion, new Power { PowerTask = new AddEnchantmentTask("MAW_025e", EntityType.TARGET) }));
			cards.Add("MAW_025e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.IMMUNE, EffectOperator.SET, 1)) { IsOneTurnEffect = true } }));
			// [MAW_026] Incarceration (3) — Choose a minion. It goes Dormant for 3 turns.
			cards.Add("CORE_MAW_026", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m) || m.Untouchable) return;
					Generic.MakeDormant(m, V("MAW_026", "turns", 3), "MAW_026e", (IPlayable)s); // its counter "Doing Time"
				})
			}));
			// [MAW_027] Call to the Stand (1) — Your opponent summons a random minion from their hand.
			cards.Add("CORE_MAW_027", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.Opponent.HandZone.Where(p => Generic.IsMinionCard(p)).ToList();
					if (minions.Count == 0 || c.Opponent.BoardZone.IsFull) return;
					var m = (Minion)minions[g.Random.Next(minions.Count)];
					c.Opponent.HandZone.Remove(m);
					Generic.SummonBlock.Invoke(g, m, -1, s);
				})
			}));
			// [MAW_029] Weapons Expert (2) — Battlecry: If you have a weapon equipped, give it +1/+1. Otherwise, draw a weapon.
			cards.Add("CORE_MAW_029", new CardDef(new Power
			{
				PowerTask = ComplexTask.If((g, c, s) => c.Hero.Weapon != null,
					new CustomTask((g, c, s, t, stack) =>
					{
						if (!(c.Hero.Weapon is Weapon w)) return;
						w.AttackDamage += V("MAW_029", "attack", 1);
						w.Durability += V("MAW_029", "durability", 1);
					}),
					new CustomTask((g, c, s, t, stack) => DrawWhere(g, c, p => p is Weapon)))
			}));
			// [MAW_030] Torghast Custodian (2) — Battlecry: For each enemy minion, randomly gain Rush, Divine Shield, or Windfury.
			cards.Add("CORE_MAW_030", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					GameTag[] gifts = { GameTag.RUSH, GameTag.DIVINE_SHIELD, GameTag.WINDFURY };
					for (int i = 0; i < c.Opponent.BoardZone.CountExceptUntouchables; i++)
					{
						GameTag gift = gifts[g.Random.Next(gifts.Length)];
						if (gift == GameTag.RUSH) ((Minion)s).IsRush = true; else s[gift] = 1;
					}
				})
			}));
			// [MAW_031] Afterlife Attendant (3) — Your Infuse cards also Infuse while in your deck. (Its aura sets HsTag.INFUSE_IN_DECK.)
			cards.Add("CORE_MAW_031", new CardDef(new Power { Aura = new Aura(AuraType.CONTROLLER, new Effect(HsTag.INFUSE_IN_DECK, EffectOperator.ADD, 1)) }));
			// [MAW_032] Tight-Lipped Witness (3) — Secrets can't be revealed. (Its aura sets HsTag.SECRETS_CANT_TRIGGER on both players.)
			cards.Add("CORE_MAW_032", new CardDef(new Power { Aura = new Aura(AuraType.CONTROLLERS, new Effect(HsTag.SECRETS_CANT_TRIGGER, EffectOperator.ADD, 1)) }));
			// [MAW_033] Sylvanas, the Accused (6) — Battlecry: Destroy an enemy minion. Infuse (7): Take control of it instead.
			cards.Add("CORE_MAW_033", new CardDef(BattlecryEnemyMinion, new Power { PowerTask = new DestroyTask(EntityType.TARGET) }));
			cards.Add("MAW_033t", new CardDef(BattlecryEnemyMinion, new Power { PowerTask = new ControlTask(EntityType.TARGET) }));
			// [MAW_034] The Jailer (10) — Battlecry: Destroy your deck. This minion gains Immune.
			cards.Add("CORE_MAW_034", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (IPlayable p in c.DeckZone.ToList()) Generic.Destroy(p, s, many: true);
					((Minion)s).IsImmune = true;
				})
			}));
			// [MAW_101] Contract Conjurer (8) — Costs (3) less for each Secret you control.
			cards.Add("CORE_MAW_101", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => V("MAW_101", "discount per secret", 3) * p.Controller.SecretZone.Count(x => x is Spell sp && sp.IsSecret))
			}));

			// ---------------------------------------------------------------- Nathria: Neutral

			cards.Add("CORE_REV_000", Suspicious("REV_000", x => x.Type == CardType.SPELL));                          // Suspicious Alchemist
			cards.Add("CORE_REV_002", Suspicious("REV_002", x => x.Type == CardType.MINION && x.Rarity == Rarity.LEGENDARY)); // Suspicious Usher
			cards.Add("CORE_REV_006", Suspicious("REV_006", x => x.Type == CardType.WEAPON));                           // Suspicious Pirate
			// [REV_011] The Harvester of Envy (8) — After you play a card copied from the opponent, steal the original. (Approx.:
			// the opponent's first card with the same id, in hand or deck.)
			cards.Add("CORE_REV_011", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_PLAY_CARD)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => p[COPIED_FROM_OPPONENT] > 0),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable original = c.Opponent.HandZone.Concat(c.Opponent.DeckZone).FirstOrDefault(p => p.Card.Id == t?.Card.Id);
						if (original == null) return;
						original.Zone.Remove(original);
						c.Opponent.SetasideZone.Add(original);
						AddToHand(c, original.Card);
					})
				}
			}));
			// [REV_012] Bog Beast (3) — Taunt. Deathrattle: Summon a 2/4 Muckmare with Taunt.
			cards.Add("CORE_REV_012", new CardDef(new Power { DeathrattleTask = new SummonTask("REV_012t", SummonSide.DEATHRATTLE) }));
			// [REV_013] Stoneborn Accuser: its infused version has "Battlecry: Deal 5 damage."
			cards.Add("CORE_REV_013", new CardDef(new Power { PowerTask = Nothing }));
			cards.Add("REV_013t", new CardDef(BattlecryAny, new Power { PowerTask = new DamageTask(V("REV_013t", "damage", 5), EntityType.TARGET) }));
			// [REV_014] Red Herring (2) — Taunt. Your non-Red Herring minions have Stealth.
			cards.Add("CORE_REV_014", new CardDef(new Power
			{
				Aura = new Aura(AuraType.BOARD, new Effect(GameTag.STEALTH, EffectOperator.SET, 1))
				{
					Condition = new SelfCondition(p => p.Card.Name != "Red Herring")
				}
			}));
			// [REV_015] Masked Reveler (2) — Rush. Deathrattle: Summon a 2/2 copy of another minion in your deck.
			cards.Add("CORE_REV_015", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.DeckZone.Where(p => Generic.IsMinionCard(p) && p.Card.Id != s.Card.Id).ToList();
					if (minions.Count == 0 || c.BoardZone.IsFull) return;
					var copy = (Minion)Entity.FromCard(c, minions[g.Random.Next(minions.Count)].Card);
					Generic.SummonBlock.Invoke(g, copy, -1, s);
					Give(g, s, copy, "REV_015t");
				})
			}));
			cards.Add("REV_015t", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttackHealth(2)) }));
			// [REV_016] Crooked Cook (2) — At the end of your turn, if you dealt 3 or more damage to the enemy hero, draw a card.
			cards.Add("CORE_REV_016", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = ComplexTask.Create(
						new CustomTask((g, c, s, t, stack) => stack.Flag = c.Opponent.Hero.DamageTakenThisTurn >= V("REV_016", "damage", 3)),
						new FlagTask(true, new DrawTask()))
				}
			}));
			// [REV_017] Insatiable Devourer (6) — Battlecry: Devour an enemy minion and gain its stats. Infuse: And its neighbors.
			ISimpleTask Devour(bool neighbors) => new CustomTask((g, c, s, t, stack) =>
			{
				if (!(t is Minion victim) || !(s is Minion me)) return;
				var eaten = new List<Minion> { victim };
				if (neighbors) eaten.AddRange(victim.GetAdjacentMinions());
				int atk = eaten.Sum(m => m.AttackDamage), hp = eaten.Sum(m => m.Health);
				foreach (Minion m in eaten) m.Destroy();
				me.AttackDamage += atk;
				me.BaseHealth += hp;
			});
			cards.Add("CORE_REV_017", new CardDef(BattlecryEnemyMinion, new Power { PowerTask = Devour(false) }));
			cards.Add("REV_017t", new CardDef(BattlecryEnemyMinion, new Power { PowerTask = Devour(true) }));
			// [REV_019] Famished Fool (5) — Battlecry: Draw a card. Infuse (3): Draw 3 instead.
			cards.Add("CORE_REV_019", new CardDef(new Power { PowerTask = new DrawTask() }));
			cards.Add("REV_019t", new CardDef(new Power { PowerTask = new EnqueueTask(V("REV_019t", "cards", 3), new DrawTask()) }));
			// [REV_020] Dinner Performer (3) — Battlecry: Summon a random minion from your deck that you can afford to play.
			cards.Add("CORE_REV_020", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var affordable = c.DeckZone.Where(p => Generic.IsMinionCard(p) && p.Cost <= c.RemainingMana).ToList();
					if (affordable.Count == 0 || c.BoardZone.IsFull) return;
					var m = (Minion)affordable[g.Random.Next(affordable.Count)];
					c.DeckZone.Remove(m);
					Generic.SummonBlock.Invoke(g, m, -1, s);
				})
			}));
			// [REV_021] Kael'thas Sinstrider (7) — Every third minion you play each turn costs (0).
			cards.Add("CORE_REV_021", new CardDef(new Power
			{
				Aura = new Aura(AuraType.HAND, Effects.SetCost(0)) { Condition = new SelfCondition(p => p is Minion && p.Controller.NumMinionsPlayedThisTurn % 3 == 2), ShowAs = "REV_021e" }
			}));
			// [REV_022] Murloc Holmes (2) — Battlecry: Solve 3 Clues about your opponent's cards to get copies of them.
			cards.Add("CORE_REV_022", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var theirs = c.Opponent.HandZone.Concat(c.Opponent.DeckZone).Select(p => p.Card).ToList();
					foreach (Card real in Pick(g, theirs, V("REV_022", "clues", 3))) Guess(g, c, s, real, fromOpponent: true);
				})
			}));
			// [REV_238] Theotar, the Mad Duke (6) — Battlecry: Discover a card in each player's hand and swap them. Two Discovers
			// over the existing cards (Generic.DiscoverFromCards: yours, then theirs); the same two cards change hands (a swap is
			// a move: Generic.GiveTo), never copies.
			cards.Add("CORE_REV_238", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (c.HandZone.Count == 0 || c.Opponent.HandZone.Count == 0) return;
					Generic.DiscoverFromCards(c, s, Generic.PickFrom(c.HandZone, g, null), (g2, c2, s2, mine) =>
						Generic.DiscoverFromCards(c2, s2, Generic.PickFrom(c2.Opponent.HandZone, g2, null), (g3, c3, s3, theirs) =>
						{
							if (mine.Zone != c3.HandZone || theirs.Zone != c3.Opponent.HandZone) return;
							c3.Opponent.HandZone.Add(Generic.GiveTo(mine, c3.Opponent));
							c3.HandZone.Add(Generic.GiveTo(theirs, c3));
						}));
				})
			}));
			// [REV_246] Mysterious Visitor (2) — Battlecry: Reduce the Cost of cards copied from your opponent by (3).
			cards.Add("CORE_REV_246", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.HandZone.Concat(c.DeckZone).Where(p => p[COPIED_FROM_OPPONENT] > 0), "REV_246e2"))
			}));
			cards.Add("REV_246e2", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("REV_246", "discount", 3))) }));
			// [REV_247] Partner in Crime (3) — Battlecry: Summon a copy of this minion at the end of your turn.
			cards.Add("CORE_REV_247", new CardDef(new Power { PowerTask = new AddEnchantmentTask("REV_247e", EntityType.SOURCE) }));
			cards.Add("REV_247e", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (!(s is Enchantment e) || !(e.Target is Minion me)) return;
						Summon(g, c, me.Card, e, me.ZonePosition + 1);
						RemoveEnchantmentTask.Task.Process(e.Game, e.Controller, e, null); // reverts its effects too
					})
				}
			}));
			// [REV_251] Sinrunner (4) — Deathrattle: Destroy a random enemy minion.
			cards.Add("CORE_REV_251", new CardDef(new Power { DeathrattleTask = ComplexTask.DestroyRandomTargets(1, EntityType.OP_MINIONS) }));
			// [REV_332] Anima Extractor (1) — Whenever a friendly minion takes damage, give a random minion in your hand +1/+1.
			cards.Add("CORE_REV_332", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TAKE_DAMAGE)
				{
					TriggerSource = TriggerSource.MINIONS,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var minions = c.HandZone.Where(p => Generic.IsMinionCard(p)).ToList();
						if (minions.Count > 0) Give(g, s, minions[g.Random.Next(minions.Count)], "REV_332e");
					})
				}
			}));
			cards.Add("REV_332e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("REV_332", "stats", 1))) }));
			// [REV_338] Dredger Staff (1) — Battlecry: Give minions in your hand +1 Health.
			cards.Add("CORE_REV_338", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.Where(p => Generic.IsMinionCard(p)), "REV_338e"))
			}));
			cards.Add("REV_338e", new CardDef(new Power { Enchant = new Enchant(Effects.Health_N(V("REV_338", "health", 1))) }));
			// [REV_375] Stoneborn General (8) — Rush. Deathrattle: Summon an 8/8 Gravewing with Rush.
			cards.Add("CORE_REV_375", new CardDef(new Power { DeathrattleTask = new SummonTask("REV_375t", SummonSide.DEATHRATTLE) }));
			// [REV_377] Invitation Courier (2) — After a card is added to your hand from another class, copy it.
			cards.Add("CORE_REV_377", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.ADD_TO_HAND)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					Condition = new SelfCondition(p => !p.Card.HasClass(p.Controller.HeroClass) && !p.Card.HasClass(CardClass.NEUTRAL)
					                                   && !p.Card.HasClass(CardClass.INVALID) && p[COPIED_BY_COURIER] == 0),
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (t == null || c.HandZone.IsFull) return;
						IPlayable copy = Entity.FromCard(c, t.Card);
						copy[COPIED_BY_COURIER] = 1; // don't copy the copy
						Generic.AddHandPhase.Invoke(c, copy);
					})
				}
			}));
			// [REV_378] Forensic Duster (4) — Battlecry: Your opponent's minions cost (1) more next turn.
			cards.Add("CORE_REV_378", new CardDef(new Power { PowerTask = new AddEnchantmentTask("REV_378e", EntityType.OP_CONTROLLER) }));
			cards.Add("REV_378e", new CardDef(new Power
			{
				Aura = new Aura(AuraType.OP_HAND, Effects.AddCost(V("REV_378", "cost", 1)))
				{
					Condition = SelfCondition.IsMinion,
					RemoveTrigger = (TriggerType.TURN_END, SelfCondition.IsOpTurn)
				}
			}));
			// [REV_837] Muck Plumber (1) — ALL minions cost (2) more.
			cards.Add("CORE_REV_837", new CardDef(new Power
			{
				Aura = new MultiAura(
					new Aura(AuraType.HAND, Effects.AddCost(V("REV_837", "cost", 2))) { Condition = SelfCondition.IsMinion, ShowAs = "REV_837e" },
					new Aura(AuraType.OP_HAND, Effects.AddCost(V("REV_837", "cost", 2))) { Condition = SelfCondition.IsMinion, ShowAs = "REV_837e" })
			}));
			// [REV_843] Sinfueled Golem (4) — Infuse (3): Gain stats equal to the Attack of the minions that Infused this.
			cards.Add("CORE_REV_843", new CardDef(new Power { PowerTask = Nothing }));
			cards.Add("REV_843t", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int bonus = s[Game.INFUSE_ATTACK_SUM];
					if (bonus > 0 && s is Minion me) { me.AttackDamage += bonus; me.BaseHealth += bonus; }
				})
			}));
			// [REV_845] Volatile Skeleton (1) — Deathrattle: Deal 2 damage to a random enemy.
			cards.Add("CORE_REV_845", new CardDef(new Power { DeathrattleTask = ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, V("REV_845", "damage", 2)) }));
			// [REV_901] Dispossessed Soul (3) — Battlecry: If you control a location, Discover a copy of a card in your deck. (The
			// engine has no locations yet, so the condition is never met.)
			cards.Add("CORE_REV_901", new CardDef(new Power { PowerTask = Nothing }));
			// [REV_916] Creepy Painting (4) — After another minion dies, become a copy of it.
			cards.Add("CORE_REV_916", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DEATH)
				{
					TriggerSource = TriggerSource.ALL_MINIONS_EXCEPT_SELF,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Minion me && !me.IsDead && !me.ToBeDestroyed && me.Zone?.Type == SabberStoneCore.Enums.Zone.PLAY && t is Minion dead)
							Generic.TransformBlock.Invoke(c, dead.Card, me);
					})
				}
			}));
			// [REV_930] Crazed Wretch (3) — Has +2 Attack and Charge while damaged.
			cards.Add("CORE_REV_930", new CardDef(new Power { Aura = new EnrageEffect(AuraType.SELF, "REV_930e") }));
			cards.Add("REV_930e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("REV_930", "attack", 2)), new Effect(GameTag.CHARGE, EffectOperator.SET, 1))
			}));
			// [REV_936] Crud Caretaker (5) — Battlecry: Summon a 3/5 Elemental with Taunt.
			cards.Add("CORE_REV_936", new CardDef(new Power { PowerTask = new SummonTask("REV_936t", SummonSide.RIGHT) }));
			// [REV_956] Priest of the Deceased: infused version is stats only (REV_956t).
			cards.Add("CORE_REV_956", new CardDef(new Power { PowerTask = Nothing }));
			// [REV_957] Murlocula: infused version costs (0) (REV_957t's own tags).
			cards.Add("CORE_REV_957", new CardDef(new Power { PowerTask = Nothing }));
			// [REV_960] Ashen Elemental (4) — Battlecry: Whenever your opponent draws a card next turn, they take 2 damage.
			cards.Add("CORE_REV_960", new CardDef(new Power { PowerTask = new AddEnchantmentTask("REV_960e", EntityType.OP_CONTROLLER) }));
			cards.Add("REV_960e", new CardDef(new Power
			{
				Trigger = new MultiTrigger(
					new Trigger(TriggerType.DRAW)
					{
						TriggerSource = TriggerSource.ALL,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (s is Enchantment e && e.Target is Controller victim && t?.Controller == victim && g.CurrentPlayer == victim)
								Generic.DamageCharFunc(e, victim.Hero, V("REV_960", "damage", 2), false);
						})
					},
					new Trigger(TriggerType.TURN_END)
					{
						EitherTurn = true,
						SingleTask = new CustomTask((g, c, s, t, stack) =>
						{
							if (s is Enchantment e && g.CurrentPlayer == e.Target as Controller) RemoveEnchantmentTask.Task.Process(e.Game, e.Controller, e, null); // reverts its effects too
						})
					})
			}));
		}
	}
}
