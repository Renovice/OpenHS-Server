// HsServer addition (not upstream SabberStone): the Legacy set's cards that had no script (2026-10-04, Legacy part 2). The
// Legacy set is client set 1635 + Expert1 (475 cards, FINDINGS 2026-10-04); its LEG_ reprints share these scripts through
// Loader/CardDefs.cs (same client text), so each card is scripted once, under its original id. Numbers go through V.
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
		/// <summary>
		/// A Paladin Aura ("Lasts @ turns", client PALADIN_AURA): it sits in the Secret zone (CastSpell) with its effect (its
		/// trigger and/or aura), and at the end of each of its owner's turns ("Lasts @ enemy turns": each enemy turn) counts one
		/// (QUEST_PROGRESS) until its duration (QUEST_PROGRESS_TOTAL, client data) is over; then its effect stops and it leaves
		/// for the graveyard. Live: Chronological Aura TIME_700 (live-20261003-dk-classxp): the turn it is played counts, it
		/// leaves at the end of the third.
		/// </summary>
		/// <param name="atStart">"Unlike other Auras, this card's timer decrements at the start of the player's turn" (wiki Acceleration
		/// Aura; live dk-classxp 17411: its effect, then the count).</param>
		/// (WHIZBANG: <paramref name="countdownEffect"/> — an "At the end of your turn" Aura's effect runs inside the countdown's block, before
		/// the count: live vfx 192161 VAC_922, one OBJECTIVE block)
		/// <param name="countdownIndex">the countdown's client effect index: live sends it as its own block with no keyword and no
		/// history tile (live-20261008-issues 14185 / 14741: EffectIndex 1; Iceblood Garrison / Desecrated Graveyard: 0, their
		/// effect 1 — audit 2026-10-08), so the client plays nothing for it.</param>
		/// <param name="countdownKeyword">a separate countdown's TriggerKeyword (live Resistance Aura: TRIGGER_VISUAL; most: none).</param>
		/// <param name="countdownInHistory">a separate countdown shows in the history (live Sandfury Aura: index 3, shown).</param>
		/// <param name="expiryInOwnBlock">the expiry is its own index-0 TRIGGER block after the tick (live Alterac Objectives).</param>
		/// HsServer (2026-10-08, live audit: 14 cards): an Aura whose effect is "at the end / start of your turn" ticks INSIDE that effect's
		/// block — one OBJECTIVE block a turn: the effect, QUEST_PROGRESS, the turns-elapsed counter, and on the last tick the expiry
		/// (Generic.ObjectiveTick / ObjectiveExpires); an Aura whose effect isn't turn-based has a separate countdown block.
		private static Power PaladinAura(Power effect, bool enemyTurns = false, bool atStart = false, ISimpleTask countdownEffect = null, int countdownIndex = 1,
			GameTag countdownKeyword = (GameTag)0, bool countdownInHistory = false, bool expiryInOwnBlock = false)
		{
			// the effect's own turn trigger becomes the tick's block (its index, the card's keyword)
			bool folded = countdownEffect != null;
			if (!folded && effect.Trigger is Trigger own && !(own is MultiTrigger) && own.Condition == null &&
			    own.Type == (atStart ? TriggerType.TURN_START : TriggerType.TURN_END))
			{
				countdownEffect = own.SingleTask;
				countdownIndex = own.EffectIndex;
				effect.Trigger = null;
				folded = true;
			}
			else if (folded)
				countdownIndex = 0; // (an "at the end of your turn" effect given apart: the effect's block, index 0 — pattern of TIME_700)
			var countdown = new Trigger(atStart ? TriggerType.TURN_START : TriggerType.TURN_END)
			{
				EitherTurn = enemyTurns,
				EffectIndex = countdownIndex,
				// (with the effect inside it, the block is that effect's: the card's keyword, OBJECTIVE; a bare countdown: its own)
				KeywordOverride = folded ? (GameTag?)null : countdownKeyword,
				InHistory = !folded && countdownInHistory,
				NotInHistory = !folded && !countdownInHistory,
				SingleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Spell aura) || aura.Zone?.Type != Zone.SECRET || enemyTurns && g.CurrentPlayer == c) return;
					if (countdownEffect != null) { countdownEffect.Process(g, c, s, t, stack); g.ProcessTasks(); }
					if (aura.Zone?.Type != Zone.SECRET) return;
					if (!Generic.ObjectiveTick(aura, aura.QuestTotalProgress)) return;
					Generic.ObjectiveExpires(c, aura, expiryInOwnBlock);
				})
			};
			effect.Trigger = effect.Trigger == null ? countdown : new MultiTrigger(effect.Trigger, countdown);
			return effect;
		}

		/// <summary>A choice of option cards; the pick is cast (Champions of Azeroth, Bob the Bartender).</summary>
		private static CustomTask ChooseAndCast(params string[] options) => new CustomTask((g, c, s, t, stack) =>
		{
			Card[] offer = options.Select(Cards.TryFromId).Where(x => x != null).ToArray();
			if (offer.Length > 0)
				Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.CAST, offer, null);
		});

		/// <summary>The card that offered a chosen action (its CREATOR: Bob the Bartender), else the action itself.</summary>
		private static IPlayable Bob(Game g, IEntity action) =>
			g.IdEntityDic.TryGetValue(action[GameTag.CREATOR], out IPlayable creator) ? creator : (IPlayable)action;

		/// <summary>Draws a random card of the deck matching <paramref name="where"/>; the drawn card if it reached the hand
		/// (an overdrawn card burns: null), null when none.</summary>
		private static IPlayable DrawOne(Game g, Controller c, Func<IPlayable, bool> where)
		{
			var matching = c.DeckZone.Where(where).ToList();
			if (matching.Count == 0) return null;
			return Generic.Draw(c, matching[g.Random.Next(matching.Count)]) is IPlayable drawn && drawn.Zone?.Type == SabberStoneCore.Enums.Zone.HAND ? drawn : null;
		}

		private static void Legacy(IDictionary<string, CardDef> cards)
		{
			// ---------------------------------------------------------------- Death Knight (Path of Arthas, LEG_RLK_)

			// [RLK_034] Heart Strike (1) — Deal $3 damage to a minion. If it dies, gain a Corpse.
			cards.Add("RLK_034", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("RLK_034", "damage", 3), EntityType.TARGET, true),
					new CustomTask((g, c, s, t, stack) =>
					{
						if (t is Minion m && (m.IsDead || m.ToBeDestroyed)) Generic.GainCorpses(c, s, V("RLK_034", "corpses", 1));
					}))
			}));

			// [RLK_039] Plagued Grain (1) — Gain 4 Corpses. Shuffle four Crates into your deck that summon a 2/2 Undead when drawn.
			cards.Add("RLK_039", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Generic.GainCorpses(c, s, V("RLK_039", "corpses", 4));
					for (int i = 0; i < V("RLK_039", "crates", 4); i++)
						Generic.ShuffleIntoDeck.Invoke(c, s, Entity.FromCard(c, Cards.FromId("RLK_039t")));
				})
			}));
			// [RLK_039t] Grain Crate — Casts When Drawn: Summon a 2/2 Undead Peasant.
			cards.Add("RLK_039t", new CardDef(new Power { TopdeckTask = new SummonTask("RLK_070t") }));

			// [RLK_071] Patchwerk (7) — Battlecry: Destroy a random minion in your opponent's hand, deck, and battlefield.
			// (Destroy works in every zone: Generic.Destroy; a destroyed hand card is a discard.)
			cards.Add("RLK_071", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					Controller op = c.Opponent;
					foreach (IEnumerable<IPlayable> zone in new IEnumerable<IPlayable>[] { op.HandZone, op.DeckZone, op.BoardZone })
					{
						var minions = zone.Where(p => p is Minion && !p.ToBeDestroyed && !(p is Minion m && m.IsDead)).ToList();
						if (minions.Count == 0) continue;
						IPlayable victim = minions[g.Random.Next(minions.Count)];
						Generic.MissileFx((IPlayable)s, victim, () => Generic.Destroy(victim, s)); // its cleaver at each
					}
				})
			}));

			// [RLK_077] Possessifier (5) — Battlecry: Summon two 1/1 Fighters with Rush and Reborn.
			cards.Add("RLK_077", new CardDef(new Power { PowerTask = new SummonTask("RLK_077t", V("RLK_077", "fighters", 2)) }));

			// [RLK_079] Noxious Cadaver (1) — Battlecry: Deal 2 damage to an enemy and your hero.
			cards.Add("RLK_079", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_IF_AVAILABLE, 0 }, { PlayReq.REQ_ENEMY_TARGET, 0 } }, new Power
			{
				PowerTask = ComplexTask.Create(
					new DamageTask(V("RLK_079", "damage", 2), EntityType.TARGET),
					new DamageTask(V("RLK_079", "damage", 2), EntityType.HERO))
			}));

			// [RLK_082] Deathbringer Saurfang (5) — Taunt. Deathrattle: Return this to your hand. It costs Health instead of Mana.
			// (A dead minion returned to hand is a fresh card: Generic.ReturnToHand; "costs Health" is the card's
			// CARD_COSTS_HEALTH, which the engine pays like Death Metal Knight's and reports as TAG_CARD_ALTERNATE_COST 1.)
			cards.Add("RLK_082", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (s is Minion saurfang && Generic.ReturnToHand(c, saurfang) is IPlayable back)
						new Effect(GameTag.CARD_COSTS_HEALTH, EffectOperator.SET, 1).ApplyTo(back);
				})
			}));

			// [RLK_115] Repulsive Gargantuan (4) — Enemy characters can't be healed.
			cards.Add("RLK_115", new CardDef(new Power
			{
				Aura = new Aura(AuraType.OPPONENT, new Effect(HsTag.CHARACTERS_CANT_BE_HEALED, EffectOperator.SET, 1))
			}));

			// [RLK_125] Obliterate (2) — Destroy a minion. Deal 3 damage to your hero.
			cards.Add("RLK_125", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(
					new DestroyTask(EntityType.TARGET),
					new DamageTask(V("RLK_125", "damage", 3), EntityType.HERO))
			}));

			// [RLK_224] Overseer Frigidara (6) — Battlecry: Draw 2 spells. If they're both Frost spells, deal 2 damage to all enemies.
			cards.Add("RLK_224", new CardDef(new Power
			{
				OwnFx = true, // its frost blast (RLKFX_Xyrella_AE) only with the damage
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var drawn = new List<IPlayable>();
					for (int i = 0; i < V("RLK_224", "spells", 2); i++)
						if (DrawOne(g, c, p => p is Spell) is IPlayable p) drawn.Add(p);
					if (drawn.Count == V("RLK_224", "spells", 2) && drawn.All(p => p.Card[SPELL_SCHOOL] == FROST))
						Generic.WholeFx((IPlayable)s, () =>
						{
							foreach (ICharacter enemy in c.Opponent.BoardZone.Cast<ICharacter>().Append(c.Opponent.Hero).ToList())
								Generic.DamageCharFunc((IPlayable)s, enemy, V("RLK_224", "damage", 2), false);
						});
				})
			}));

			// [RLK_705] Graveyard Shift (3) — Summon two 1/1 Zombies with Reborn.
			cards.Add("RLK_705", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_NUM_MINION_SLOTS, 1 } },
				new Power { PowerTask = new SummonTask("RLK_705t", V("RLK_705", "zombies", 2)) }));

			// [RLK_710] Rimefang Sword (3, weapon) — After your hero attacks, reduce the Cost of a spell in your hand by (1).
			// (A trigger, so random: user rule 2026-10-04, automatic effects stay random.)
			cards.Add("RLK_710", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.HERO,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						var spells = c.HandZone.Where(p => p is Spell).ToList();
						if (spells.Count > 0) Generic.AddEnchantmentBlock(g, Cards.FromId("RLK_710e"), (IPlayable)s, spells[g.Random.Next(spells.Count)], 0, 0, 0);
					})
				}
			}));
			cards.Add("RLK_710e", new CardDef(new Power { Enchant = new Enchant(Effects.ReduceCost(V("RLK_710e", "discount", 1))) }));

			// [RLK_744] Stitched Giant (9) — Costs (1) less for each Corpse you've spent this game.
			cards.Add("RLK_744", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller[HsTag.CORPSES_SPENT_THIS_GAME] * V("RLK_744", "discount", 1))
			}));

			// [RLK_752] Rime Sculptor (5) — Battlecry: Summon two 2/1 Rime Elementals with "Deathrattle: Deal 2 damage to a random
			// enemy." (The client links its token: COLLECTION_RELATED_CARD_DATABASE_ID → RLK_907t.)
			cards.Add("RLK_752", new CardDef(new Power { PowerTask = new SummonTask("RLK_907t", V("RLK_752", "elementals", 2)) }));
			cards.Add("RLK_907t", new CardDef(new Power
			{
				DeathrattleTask = ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, V("RLK_907t", "damage", 2))
			}));

			// [EX1_062] Old Murk-Eye (4) — Charge. Has +1 Attack for each other Murloc on the battlefield. (Upstream counted only
			// its own side and itself.)
			cards.Add("EX1_062", new CardDef(new Power
			{
				Aura = new AdaptiveEffect(GameTag.ATK, EffectOperator.ADD, p =>
					p.Controller.BoardZone.Concat(p.Controller.Opponent.BoardZone).Count(m => m != p && m.IsRace(Race.MURLOC)))
			}));

			// ---------------------------------------------------------------- Rotated Core 2022 (LEG_CS3_)

			// [CS3_001] Aegwynn, the Guardian (5) — Spell Damage +2. Deathrattle: The next minion you draw inherits these powers.
			// Client: CS3_001e2 on the player ("The next minion you draw inherits the Guardian's Legacy"), CS3_001e on the
			// minion (Spell Damage +2 and "Deathrattle: Pass on the Guardian's Legacy.").
			cards.Add("CS3_001", new CardDef(new Power { DeathrattleTask = new AddEnchantmentTask("CS3_001e2", EntityType.CONTROLLER) }));
			cards.Add("CS3_001e2", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.DRAW)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						IPlayable drawn = t ?? g.CurrentEventData?.EventSource as IPlayable;
						if (!(drawn is Minion) || !(s is Enchantment legacy)) return;
						Generic.AddEnchantmentBlock(g, Cards.FromId("CS3_001e"), drawn, drawn, 0, 0, 0);
						RemoveEnchantmentTask.Task.Process(g, c, legacy, null);
					})
				}
			}));
			cards.Add("CS3_001e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.SPELLPOWER, EffectOperator.ADD, V("CS3_001e", "spell damage", 2))),
				DeathrattleTask = new AddEnchantmentTask("CS3_001e2", EntityType.CONTROLLER)
			}));

			// [CS3_013] Shadowed Spirit (3) — Deathrattle: Deal 3 damage to the enemy hero.
			cards.Add("CS3_013", new CardDef(new Power { DeathrattleTask = new DamageTask(V("CS3_013", "damage", 3), EntityType.OP_HERO) }));

			// [CS3_015] Selective Breeder (2) — Battlecry: Discover a copy of a Beast in your deck.
			cards.Add("CS3_015", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.DiscoverFromDeck(c, s, Generic.PickFromDeck(c, p => p is Minion && p.Card.IsRace(Race.BEAST)),
						(g2, c2, s2, beast) => AddToHand(c2, beast.Card)))
			}));

			// [CS3_017] Gan'arg Glaivesmith (3) — Outcast: Give your hero +3 Attack this turn.
			cards.Add("CS3_017", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new ConditionTask(EntityType.SOURCE, PlayedAsOutcast),
					new FlagTask(true, new AddEnchantmentTask("CS3_017e", EntityType.HERO)))
			}));
			cards.Add("CS3_017e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("CS3_017e", "attack", 3))) { IsOneTurnEffect = true } }));

			// [CS3_028] Thrive in the Shadows (2) — Discover a spell from your deck.
			cards.Add("CS3_028", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.DiscoverFromDeck(c, s, Generic.PickFromDeck(c, p => p is Spell)))
			}));

			// [CS3_031] Alexstrasza the Life-Binder (8) — Battlecry: Choose a character. If it's friendly, restore 8 Health. If
			// it's an enemy, deal 8 damage.
			cards.Add("CS3_031", new CardDef(BattlecryAny, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is ICharacter target)) return;
					if (target.Controller == c) target.TakeHeal((IPlayable)s, V("CS3_031", "heal", 8));
					else Generic.DamageCharFunc((IPlayable)s, target, V("CS3_031", "damage", 8), false);
				})
			}));

			// [CS3_032] Onyxia the Broodmother (8) — At the end of each turn, fill your board with 1/1 Whelps. (The client links
			// its token: ds1_whelptoken.)
			cards.Add("CS3_032", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.TURN_END)
				{
					EitherTurn = true,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						Generic.BeginSummonGroup(g, s, Game.MAX_MINIONS_ON_BOARD - c.BoardZone.Count);
						while (!c.BoardZone.IsFull)
						{
							int before = c.BoardZone.Count;
							Generic.SummonBlock.Invoke(g, (Minion)Entity.FromCard(c, Cards.FromId("ds1_whelptoken")), -1, s);
							if (c.BoardZone.Count == before) break;
						}
					})
				}
			}));

			// [CS3_033] Ysera the Dreamer (9) — Battlecry: Add one of each Dream card to your hand.
			cards.Add("CS3_033", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (string dream in new[] { "DREAM_01", "DREAM_02", "DREAM_03", "DREAM_04", "DREAM_05" })
						AddToHand(c, Cards.FromId(dream));
				})
			}));

			// [CS3_034] Malygos the Spellweaver (9) — Battlecry: Draw spells until your hand is full.
			cards.Add("CS3_034", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					while (!c.HandZone.IsFull && DrawOne(g, c, p => p is Spell) != null) { }
				})
			}));

			// [CS3_036] Deathwing the Destroyer (10) — Battlecry: Destroy all other minions. Discard a card for each destroyed.
			cards.Add("CS3_036", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var others = c.BoardZone.Concat(c.Opponent.BoardZone).Where(m => m != s && !m.IsDead && !m.ToBeDestroyed).ToList();
					foreach (Minion m in others) Generic.Destroy(m, s, true);
					for (int i = 0; i < others.Count && c.HandZone.Count > 0; i++)
						Generic.DiscardBlock.Invoke(c, c.HandZone[g.Random.Next(c.HandZone.Count)]);
				})
			}));

			// ---------------------------------------------------------------- Paladin Aura (LEG_TTN_908)

			// [TTN_908] Crusader Aura (3) — Whenever a friendly minion attacks, give it +2/+1. Lasts 3 turns.
			cards.Add("TTN_908", new CardDef(PaladinAura(new Power
			{
				Trigger = new Trigger(TriggerType.ATTACK)
				{
					TriggerSource = TriggerSource.MINIONS,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (g.CurrentEventData?.EventSource is Minion attacker)
							Generic.AddEnchantmentBlock(g, Cards.FromId("TTN_908e2"), (IPlayable)s, attacker, 0, 0, 0);
					})
				}
			})));
			cards.Add("TTN_908e2", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TTN_908e2", "attack", 2)), Effects.Health_N(V("TTN_908e2", "health", 1))) }));

			// ---------------------------------------------------------------- Event cards

			// [WON_113] Champions of Azeroth (5) — Choose Alliance or Horde. Get 2 of their Legendary Champions and reduce their
			// Costs by (2). The champions are the client's related cards of WON_113, five per side in that order (Alliance:
			// Varian, Tirion, Antonidas, Brann, Velen; Horde: Grommash, Sylvanas, Cairne, Lor'themar, Vol'jin).
			cards.Add("WON_113", new CardDef(new Power { PowerTask = ChooseAndCast("WON_113a", "WON_113b") }));
			foreach (var (option, half) in new[] { ("WON_113a", 0), ("WON_113b", 1) })
				cards.Add(option, new CardDef(new Power
				{
					PowerTask = new CustomTask((g, c, s, t, stack) =>
					{
						var champions = (Loader.ReprintCards.RelatedCardIds("WON_113") ?? new List<string>())
							.Skip(half * 5).Take(5).Select(Cards.TryFromId).Where(x => x != null).ToList();
						foreach (Card champion in Pick(g, champions, V("WON_113", "champions", 2)))
						{
							IPlayable card = Entity.FromCard(c, champion);
							Effects.ReduceCost(V("WON_113", "discount", 2)).ApplyTo(card);
							Generic.AddHandPhase.Invoke(c, card);
						}
					})
				}));

			// [BG31_BOB] Bob the Bartender (5) — Battlecry: Choose an action from the Battlegrounds! (The client's related cards:
			// its four actions BG31_BOBt..t4.)
			// Its recorded effects belong to its actions (they place them: Power.OwnFx), not to its Battlecry.
			cards.Add("BG31_BOB", new CardDef(new Power { PowerTask = ChooseAndCast("BG31_BOBt", "BG31_BOBt2", "BG31_BOBt3", "BG31_BOBt4"), OwnFx = true }));
			// Bob's actions (live-20261005, all four): every effect plays from Bob, each its own recorded one (they are all
			// recorded on BG31_BOB: Generic.NamedFx picks the action's by name).
			// [BG31_BOBt] Freeze the Shop — Freeze all enemy minions. (live: Frost Nova shockwave from Bob around the freezes)
			cards.Add("BG31_BOBt", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					Generic.NamedFx(Bob(g, s), "FrostNova", null, () => ComplexTask.Freeze(EntityType.OP_MINIONS).Process(g, c, s, t, stack)))
			}));
			// [BG31_BOBt2] Recruit a Minion — Put a copy of an enemy minion into your hand. Your opponent gets 3 Coins.
			// Not random: the player aims at the enemy minion (live: a TARGET choice from Bob; user rule "not random unless it
			// says so"); a Sap smoke missile at it, the copy added, then the opponent's Coins with the gold-coins effect.
			cards.Add("BG31_BOBt2", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					IPlayable bob = Bob(g, s);
					if (!c.Opponent.BoardZone.Any(m => !m.IsDead && !m.ToBeDestroyed)) return;
					int me = c.PlayerId; // (values only: the pick may resolve on an AI's copy — src is Bob in that game)
					Generic.PickTargets(c, bob, 1, x => x is Minion m && m.Controller.PlayerId != me && !m.IsDead && !m.ToBeDestroyed,
						(g2, c2, src, picked) =>
						{
							var bob2 = (IPlayable)src;
							Generic.NamedFx(bob2, "Sap", new[] { picked.Id }, () => { });
							Generic.NamedFx(bob2, "SpawnToHand_NoFX", null, () => AddToHand(c2, picked.Card));
							Generic.NamedFx(bob2, "GoldCoins", null, () =>
							{
								for (int i = 0; i < V("BG31_BOBt2", "coins", 3); i++) AddToHand(c2.Opponent, Cards.FromId("GAME_005"));
							});
						});
				})
			}));
			// [BG31_BOBt3] Refresh the Tavern — Discover a 3-Cost minion. Refresh 3 Mana Crystals.
			cards.Add("BG31_BOBt3", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new DiscoverTask(CardType.MINION, CardClass.INVALID, (GameTag.COST, RelaSign.EQ, V("BG31_BOBt3", "cost", 3))),
					new CustomTask((g, c, s, t, stack) => c.UsedMana = Math.Max(0, c.UsedMana - V("BG31_BOBt3", "mana", 3))))
			}));
			// [BG31_BOBt4] Find a Triple — Draw a minion. Get 2 copies of it. (live: the copies with the goldenize effect)
			cards.Add("BG31_BOBt4", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(DrawOne(g, c, p => p is Minion) is IPlayable drawn)) return;
					Generic.NamedFx(Bob(g, s), "Goldenize", null, () =>
					{
						for (int i = 0; i < V("BG31_BOBt4", "copies", 2); i++) AddToHand(c, drawn.Card);
					});
				})
			}));
		}
	}
}
