// HsServer addition (not upstream SabberStone): Across the Timeways — chunk 4: Warlock, Shaman and Warrior (spec: research spec_chunk4).
// Shared pieces (Actions/TimeTravel.cs): Generic.IsRafaam (client 3928), Generic.FabledFamily (Blood Fighters, the High King's Hammer),
// Generic.InsteadOfSpellDamage (Flux Revenant, Stormrook), Generic.SplitInHalves (Divergence), Generic.DrawFromDeckOf (Chronogor);
// CopyWithPermanentStats for weapons (High King's Hammer); INFINITY = Generic.Infinity. Rewind: see TimeTravelCardsGenHs.cs. Every
// number goes through CardValues (V).
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
		/// <summary>"Shuffle 2 Shreds of Time into your deck" (Twilight Timehopper, Entropic Continuity, Tachyon Barrage).</summary>
		private static void ShredsOfTime(Controller c, IEntity source, int n)
		{
			for (int i = 0; i < n; i++)
				Generic.ShuffleIntoDeck.Invoke(c, source, Entity.FromCard(c, Cards.FromId("TIME_025t"), new EntityData { { GameTag.CREATOR, source.Id } }));
		}

		/// <summary>"Cast a Shred of Time from your deck to …" (wiki Fatebreaker: "Casting Shred of Time from the deck does not draw an
		/// additional card"): a random Shred of your deck cast as an effect cast; false when there is none ("X to Y": Y only after X).</summary>
		private static bool CastShredFromDeck(Game g, Controller c, IEntity source)
		{
			Card shred = Cards.FromId("TIME_025t");
			var shreds = c.DeckZone.Where(p => p.Card == shred && p is Spell).ToList();
			if (shreds.Count == 0) return false;
			Generic.CastFromDeck(c, source, (Spell)shreds[g.Random.Next(shreds.Count)]);
			return true;
		}

		/// <summary>"N Mana worth of random minions" (Instant Multiverse; Rune of the Archmage's way): random minions that still fit the
		/// Mana left (Cost 1 or more) until it is spent or the board is full.</summary>
		private static void SummonManaWorth(Game g, Controller c, IEntity source, int mana)
		{
			int left = mana;
			for (int guard = 0; left > 0 && guard < 20 && !c.BoardZone.IsFull; guard++)
			{
				int fits = left;
				Card card = RandomCard(g, x => x.Type == CardType.MINION && !x.NeverRandomlyOnBoard && x.Cost >= 1 && x.Cost <= fits);
				if (card == null) return;
				left -= card.Cost;
				Summon(g, c, card, source);
			}
		}

		/// <summary>A Blood Fighter's Deathrattle (Lo'Gosh, Broll, Valeera: the Fabled family of TIME_850): a random one in your hand is
		/// summoned and gets +5/+5 (TIME_850e), then the card's own extra.</summary>
		private static ISimpleTask BloodFighter(Action<Game, Controller, Minion> extra) => new CustomTask((g, c, s, t, stack) =>
		{
			List<Card> family = Generic.FabledFamily(((IPlayable)s).Card);
			var fighters = c.HandZone.OfType<Minion>().Where(m => Generic.IsMinionCard(m)).Where(p => family.Contains(p.Card)).ToList();
			if (fighters.Count == 0 || c.BoardZone.IsFull) return;
			Minion m = fighters[g.Random.Next(fighters.Count)];
			if (!Generic.SummonFromHand(c, m, s)) return;
			Give(g, s, m, "TIME_850e");
			extra(g, c, m);
		});

		private static void TimeTravelChunk4(IDictionary<string, CardDef> cards)
		{
			// [TIME_101] Misplaced Pyromancer (3) — Whenever you Shatter a card, deal 2 damage to all enemy minions. (Power.AfterShatterTask)
			cards.Add("TIME_101", new CardDef(new Power
			{
				AfterShatterTask = new CustomTask((g, c, s, t, stack) =>
					DamageAll(s, c.Opponent.BoardZone.GetAll().Where(m => !(m is Location) && !m.Untouchable).Cast<ICharacter>(), V("TIME_101", "damage", 2), false))
			}));
			// ---------------------------------------------------------------- Warlock
			// [END_018] Acolyte of Infinity (3) — Battlecry: Set the Cost of a random card in your hand to INFINITY! Deathrattle: Change it
			// back. (the card is remembered on the Acolyte; a card that lost the enchantment — shuffled, played — stays as it is)
			cards.Add("END_018", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var hand = c.HandZone.GetAll().ToList();
					if (hand.Count == 0 || !(s is Minion me)) return;
					IPlayable card = hand[g.Random.Next(hand.Count)];
					Give(g, s, card, "END_018e", Math.Max(0, Generic.Infinity - card.Cost));
					me[HsTag.INFINITY_COST_CARD] = card.Id;
				}),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!g.IdEntityDic.TryGetValue(s[HsTag.INFINITY_COST_CARD], out IPlayable card)) return;
					var infinite = card.AppliedEnchantments?.Where(e => e.Creator?.Id == s.Id && e.Card == Cards.FromId("END_018e")).ToList();
					if (infinite == null || infinite.Count == 0) return;
					foreach (Enchantment e in infinite) RemoveEnchantmentTask.Task.Process(g, c, e, null); // its effect undone too
					Give(g, s, card, "END_018e2");
				})
			}));
			// (INFINITY as an ADD up to it — a SET Cost can't be undone, and "Change it back" needs that; the client shows ∞ from 1,000,000)
			cards.Add("END_018e", new CardDef(new Power { Enchant = new Enchant(Effects.AddCost(0)) { UseScriptTag = true } }));
			cards.Add("END_018e2", new CardDef(new Power { Enchant = new Enchant() }));
			// [TIME_005] Timethief Rafaam (10) — Fabled+. Your deck size is 40, but has 10 Rafaams! Battlecry: If you played the rest, destroy
			// the enemy hero. ("the rest" = the other Rafaams, each a different card — doubt; the count is the client's "({0} left!)")
			cards.Add("TIME_005", new CardDef(new Power
			{
				ThresholdCount = p => p.Controller.PlayHistory.Where(h => h.SourceCard != null && h.SourceCard[Generic.RAFAAM_FAMILY] > 0 && h.SourceCard != p.Card)
					.Select(h => h.SourceCard.Id).Distinct().Count(),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (Generic.ThresholdMet((IPlayable)s)) new DestroyTask(EntityType.OP_HERO).Process(g, c, s, null);
				})
			}));
			// [TIME_005t1] Tiny Rafaam (1) — Battlecry and Deathrattle: Draw a Rafaam.
			cards.Add("TIME_005t1", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => DrawOne(g, c, Generic.IsRafaam)),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) => DrawOne(g, c, Generic.IsRafaam))
			}));
			// [TIME_005t2] Green Rafaam (2) — Battlecry: Give other Rafaams in your hand and battlefield +2/+2.
			cards.Add("TIME_005t2", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					GiveAll(g, s, c.HandZone.GetAll().Concat(c.BoardZone.GetAll()).Where(p => p.Id != s.Id && Generic.IsRafaam(p)), "TIME_005t2e"))
			}));
			cards.Add("TIME_005t2e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TIME_005t2", "stats", 2))) }));
			// [TIME_005t3] Explorer Rafaam (4) — Battlecry: Discover a Rafaam from your deck.
			cards.Add("TIME_005t3", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => Generic.DiscoverFromDeck(c, s, Generic.PickFromDeck(c, Generic.IsRafaam)))
			}));
			// [TIME_005t4] Warchief Rafaam (5) — Battlecry: Gain 5 Armor. If you're holding another Rafaam, gain 5 more.
			cards.Add("TIME_005t4", new CardDef(new Power
			{
				GlowWhile = p => p.Controller.HandZone.Any(x => x.Id != p.Id && Generic.IsRafaam(x)),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					int armor = V("TIME_005t4", "armor", 5);
					if (c.HandZone.Any(p => p.Id != s.Id && Generic.IsRafaam(p))) armor += V("TIME_005t4", "more", 5);
					new ArmorTask(armor).Process(g, c, s, null);
				})
			}));
			// [TIME_005t5] Mindflayer R'faam (7) — Taunt. Battlecry: If you're holding another Rafaam, summon a copy of this.
			cards.Add("TIME_005t5", new CardDef(new Power
			{
				GlowWhile = p => p.Controller.HandZone.Any(x => x.Id != p.Id && Generic.IsRafaam(x)),
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (s is Minion me && me.Zone?.Type == Zone.PLAY && !c.BoardZone.IsFull && c.HandZone.Any(p => Generic.IsRafaam(p)))
						Generic.Copy(c, s, me, Zone.PLAY);
				})
			}));
			// [TIME_005t6] Calamitous Rafaam (6) — Battlecry: Deal 6 damage to all minions that aren't Rafaam.
			cards.Add("TIME_005t6", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
					DamageAll(s, AllOtherMinions(c, s).Where(m => !Generic.IsRafaam(m)).Cast<ICharacter>(), V("TIME_005t6", "damage", 6), false))
			}));
			// [TIME_005t7] Giant Rafaam (8) — Rush. Costs (1) less for each Rafaam you've played this game.
			cards.Add("TIME_005t7", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.PlayHistory.Count(h => h.SourceCard != null && h.SourceCard[Generic.RAFAAM_FAMILY] > 0) * V("TIME_005t7", "less", 1))
			}));
			// [TIME_005t8] Murloc Rafaam (3) — Battlecry: The next Rafaam you play costs (3) less.
			cards.Add("TIME_005t8", new CardDef(new Power { PowerTask = new AddEnchantmentTask("TIME_005t8e", EntityType.CONTROLLER) }));
			cards.Add("TIME_005t8e", NextCardCost(Effects.ReduceCost(V("TIME_005t8", "less", 3)), new SelfCondition(p => Generic.IsRafaam(p)),
				GenericCostLess(V("TIME_005t8", "less", 3)) ?? "GBL_003e"));
			// [TIME_005t9] Archmage Rafaam (9) — Battlecry: Transform all minions that aren't Rafaam into 1/1 Sheep. (Baaaafam is a Rafaam)
			cards.Add("TIME_005t9", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					foreach (Minion m in AllOtherMinions(c, s).Where(m => !Generic.IsRafaam(m)))
						new TransformTask(Cards.FromId("TIME_005t9t"), EntityType.TARGET).Process(g, c, s, m);
				})
			}));
			// [TIME_008] Bygone Doomspeaker (3) — Rewind. Battlecry: Both players discard a random card. (yours first — doubt)
			cards.Add("TIME_008", new CardDef(new Power
			{
				PowerTask = RewindPower((g, c, s, t) =>
				{
					foreach (Controller p in new[] { c, c.Opponent })
					{
						var hand = p.HandZone.GetAll().ToList();
						if (hand.Count > 0) Generic.Destroy(hand[g.Random.Next(hand.Count)], s);
					}
				})
			}));
			// [TIME_025] Twilight Timehopper (2) — Battlecry: Shuffle 2 Shreds of Time into your deck.
			cards.Add("TIME_025", new CardDef(new Power { PowerTask = new CustomTask((g, c, s, t, stack) => ShredsOfTime(c, s, V("TIME_025", "shreds", 2))) }));
			// [TIME_025t] Shred of Time (0) — Casts When Drawn. Deal $3 damage to your hero.
			var shred = new DamageTask(V("TIME_025t", "damage", 3), EntityType.HERO, true);
			cards.Add("TIME_025t", new CardDef(new Power { PowerTask = shred, TopdeckTask = shred }));
			// [TIME_026] Entropic Continuity (1) — Give your minions +1/+1. Shuffle 2 Shreds of Time into your deck.
			cards.Add("TIME_026", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					GiveAll(g, s, c.BoardZone.GetAll().Where(m => !(m is Location) && !m.Untouchable), "TIME_026e");
					ShredsOfTime(c, s, V("TIME_026", "shreds", 2));
				})
			}));
			cards.Add("TIME_026e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TIME_026", "stats", 1))) }));
			// [TIME_027] Tachyon Barrage (2) — Deal $6 damage split among all enemies. Shuffle 2 Shreds of Time into your deck. (random
			// 1-damage missiles, live dk-classxp 10840)
			cards.Add("TIME_027", new CardDef(new Power
			{
				PowerTask = ComplexTask.Create(
					new EnqueueTask(V("TIME_027", "damage", 6), ComplexTask.DamageRandomTargets(1, EntityType.ENEMIES, 1), true),
					new CustomTask((g, c, s, t, stack) => ShredsOfTime(c, s, V("TIME_027", "shreds", 2))))
			}));
			// [TIME_028] Fatebreaker (4) — Lifesteal. Battlecry: Cast a Shred of Time from your deck to gain +3/+3.
			cards.Add("TIME_028", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (CastShredFromDeck(g, c, s) && s is Minion me && me.Zone?.Type == Zone.PLAY) Give(g, s, me, "TIME_028e");
				})
			}));
			cards.Add("TIME_028e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TIME_028", "stats", 3))) }));
			// [TIME_029] Ruinous Velocidrake (5) — Rush. Battlecry: Cast a Shred of Time from your deck to summon a copy of this. (only while
			// it is still in play — doubt)
			cards.Add("TIME_029", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (CastShredFromDeck(g, c, s) && s is Minion me && me.Zone?.Type == Zone.PLAY && !me.ToBeDestroyed && !c.BoardZone.IsFull)
						Generic.Copy(c, s, me, Zone.PLAY);
				})
			}));
			// [TIME_030] Divergence (5) — Split a random minion in your hand into two halves.
			cards.Add("TIME_030", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var minions = c.HandZone.OfType<Minion>().Where(m => Generic.IsMinionCard(m)).ToList();
					if (minions.Count > 0) Generic.SplitInHalves(c, s, minions[g.Random.Next(minions.Count)], Cards.FromId("TIME_030e1"), Cards.FromId("TIME_030e2"));
				})
			}));
			cards.Add("TIME_030e1", new CardDef(new Power { Enchant = new Enchant(Effects.SetCost(0)) { UseScriptTag = true, RemoveWhenPlayed = true } }));
			cards.Add("TIME_030e2", new CardDef(new Power { Enchant = new Enchant(Effects.SetAttack(0), Effects.SetMaxHealth(0)) { UseScriptTag = true } }));
			// [TIME_031] RAFAAM LADDER!! (4) — Draw 3 cards of different Costs.
			cards.Add("TIME_031", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var costs = new HashSet<int>();
					for (int i = 0; i < V("TIME_031", "draws", 3); i++)
					{
						var fits = c.DeckZone.Where(p => !costs.Contains(p.Cost)).ToList();
						if (fits.Count == 0) break;
						IPlayable pick = fits[g.Random.Next(fits.Count)];
						costs.Add(pick.Cost);
						Generic.Draw(c, pick);
					}
				})
			}));
			// [TIME_032] Chronogor (6) — Battlecry: You draw your 2 highest Cost cards. Your opponent draws your 2 lowest Cost cards.
			// (ties random; the opponent's are a draw for them — Casts When Drawn cards cast for them: doubt)
			cards.Add("TIME_032", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int i = 0; i < V("TIME_032", "yours", 2); i++) DrawByCost(g, c, lowest: false);
					for (int i = 0; i < V("TIME_032", "theirs", 2) && c.DeckZone.Count > 0; i++)
					{
						int cost = c.DeckZone.Min(p => p.Cost);
						var lowest = c.DeckZone.Where(p => p.Cost == cost).ToList();
						Generic.DrawFromDeckOf(c.Opponent, lowest[g.Random.Next(lowest.Count)]);
					}
				})
			}));

			// ---------------------------------------------------------------- Shaman
			// [END_030] Haywire Hornswog (6) — Elusive, Taunt. Costs (1) less for each Mana Crystal you've Overloaded this game.
			cards.Add("END_030", new CardDef(new Power { Aura = new AdaptiveCostEffect(p => p.Controller.OverloadThisGame * V("END_030", "less", 1)) }));
			// [TIME_013] Farseer Wo (4) — Elusive. After you cast a spell, Discover a Nature spell from the past.
			cards.Add("TIME_013", new CardDef(new Power
			{
				Trigger = new Trigger(TriggerType.AFTER_CAST)
				{
					TriggerSource = TriggerSource.FRIENDLY,
					SingleTask = new DiscoverFromTask((g, c, s) => PastCards(g, c, x => x.Type == CardType.SPELL && x[SPELL_SCHOOL] == NATURE &&
						(x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL))))
				}
			}));
			// [TIME_014] Instant Multiverse (6) — Rewind. Summon 12 Mana worth of random minions. Overload: (3)
			cards.Add("TIME_014", new CardDef(new Power { PowerTask = RewindPower((g, c, s, t) => SummonManaWorth(g, c, s, V("TIME_014", "mana", 12))) }));
			// [TIME_209] Muradin, High King (5) — Fabled, Rush. Battlecry: Bring the High King's Hammer to ME! Deathrattle: Add it to your hand.
			// (wiki: the hammer from the deck, else the hand, else the battlefield — removed, not destroyed; Muradin gets its Attack and
			// Durability and Windfury; Silence loses it — doubt)
			cards.Add("TIME_209", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me) || me.Zone?.Type != Zone.PLAY || !(Generic.FabledRelative(me.Card, CardType.WEAPON) is Card hammerCard)) return;
					IPlayable hammer = c.DeckZone.FirstOrDefault(p => p.Card == hammerCard) ?? c.HandZone.FirstOrDefault(p => p.Card == hammerCard) ??
					                   (c.Hero.Weapon?.Card == hammerCard ? c.Hero.Weapon : null);
					if (!(hammer is Weapon w)) return;
					int atk = w.AttackDamage, durability = Math.Max(0, w.Durability - w.Damage);
					if (w.Zone?.Type == Zone.DECK) c.DeckZone.Remove(w);
					else if (w.Zone?.Type == Zone.HAND) c.HandZone.Remove(w);
					else if (c.Hero.Weapon == w) c.Hero.ClearWeapon();
					w.Zone?.Remove(w);
					c.SetasideZone.Add(w);
					me[HsTag.HELD_WEAPON] = w.Id;
					Generic.AddEnchantmentBlock(g, Cards.FromId("TIME_209e"), me, me, atk, durability, 0);
				}),
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!g.IdEntityDic.TryGetValue(s[HsTag.HELD_WEAPON], out IPlayable hammer) || hammer.Zone?.Type != Zone.SETASIDE) return;
					hammer.Zone.Remove(hammer);
					Generic.ResetForBackwardMove(hammer);
					if (hammer.Controller != c) Generic.GiveTo(hammer, c);
					Generic.AddHandPhase.Invoke(c, hammer);
				})
			}));
			cards.Add("TIME_209e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(0), Effects.Health_N(0), new Effect(GameTag.WINDFURY, EffectOperator.SET, 1)) { UseScriptTag = true }
			}));
			// [TIME_209t] High King's Hammer (6, weapon) — Windfury. Deathrattle: Shuffle this into your deck with +2 Attack permanently. (a
			// NEW card with raised base Attack, its earlier bonus kept: Generic.CopyWithPermanentStats)
			cards.Add("TIME_209t", new CardDef(new Power
			{
				DeathrattleTask = new CustomTask((g, c, s, t, stack) =>
				{
					Generic.SetAsideDead((IPlayable)s); // "shuffle THIS": the dead one is set aside (live, Generic.SetAsideDead)
					Generic.ShuffleIntoDeck.Invoke(c, s, Generic.CopyWithPermanentStats(c, s, (IPlayable)s, V("TIME_209t", "attack", 2), 0));
				})
			}));
			// [TIME_209t2] Avatar Form (3) — Give a friendly character +2 Attack and "After this attacks, deal 2 damage to all enemies" this turn.
			cards.Add("TIME_209t2", new CardDef(new Dictionary<PlayReq, int> { { PlayReq.REQ_TARGET_TO_PLAY, 0 }, { PlayReq.REQ_FRIENDLY_TARGET, 0 } }, new Power
			{
				PowerTask = new AddEnchantmentTask("TIME_209t2e", EntityType.TARGET)
			}));
			cards.Add("TIME_209t2e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("TIME_209t2", "attack", 2))) { IsOneTurnEffect = true },
				Trigger = new Trigger(TriggerType.AFTER_ATTACK)
				{
					TriggerSource = TriggerSource.ENCHANTMENT_TARGET,
					SingleTask = new CustomTask((g, c, s, t, stack) =>
					{
						if (s is Enchantment e && e.Target is ICharacter attacker)
							DamageAll(attacker, Enemies(attacker.Controller), V("TIME_209t2", "damage", 2), false);
					})
				}
			}));
			// [TIME_212] Lightning Rod (1) — Deal $2 damage to a friendly minion to deal $4 damage to a random enemy minion. (the bolt comes
			// even if the friendly minion takes none — Flux Revenant / Stormrook — doubt)
			cards.Add("TIME_212", new CardDef(TargetFriendlyMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Generic.DamageCharFunc((IPlayable)s, m, V("TIME_212", "damage", 2), true);
					var enemies = c.Opponent.BoardZone.GetAll().Where(x => !(x is Location) && !x.Untouchable && !x.IsDead && !x.ToBeDestroyed).ToList();
					if (enemies.Count > 0) Generic.DamageCharFunc((IPlayable)s, enemies[g.Random.Next(enemies.Count)], V("TIME_212", "bolt", 4), true);
				})
			}));
			// [TIME_213] Primordial Overseer (2) — Battlecry: If you've cast a Nature spell while holding this, gain +1/+1 and draw a card.
			// (live: Herald of Nature's TID_002e)
			cards.Add("TIME_213", new CardDef(HeldSpell(p => IsSchoolSpell(p, NATURE), new CustomTask((g, c, s, t, stack) =>
			{
				if (s is Minion me && me.Zone?.Type == Zone.PLAY) Give(g, s, me, "TID_002e");
				Generic.Draw(c);
			}))));
			// [TIME_214] Flux Revenant (2) — Taunt. Whenever you would damage this with a Nature spell, it gains +2/+1 instead.
			cards.Add("TIME_214", new CardDef(new Power
			{
				Trigger = Generic.InsteadOfSpellDamage(NATURE, new CustomTask((g, c, s, t, stack) => { if (s is Minion me) Give(g, s, me, "TIME_214e"); }))
			}));
			cards.Add("TIME_214e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("TIME_214", "attack", 2)), Effects.Health_N(V("TIME_214", "health", 1))) }));
			// [TIME_215] Thunderquake (2) — Deal $1 damage to all minions. Get a Static Shock.
			cards.Add("TIME_215", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					DamageAll(s, AllOtherMinions(c, null).Cast<ICharacter>(), V("TIME_215", "damage", 1), true);
					AddToHand(c, Cards.FromId("TIME_218"));
				})
			}));
			// [TIME_216] Nascent Bolt (3) — Deal $5 damage to a minion. If it survives, draw 2 cards.
			cards.Add("TIME_216", new CardDef(TargetMinion, new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(t is Minion m)) return;
					Generic.DamageCharFunc((IPlayable)s, m, V("TIME_216", "damage", 5), true);
					if (!m.IsDead && !m.ToBeDestroyed && m.Health > 0 && m.Zone?.Type == Zone.PLAY)
						for (int i = 0; i < V("TIME_216", "draws", 2); i++) Generic.Draw(c);
				})
			}));
			// [TIME_217] Stormrook (5) — Whenever you would damage this with a Nature spell, summon a random 5-Cost minion instead. (right of it)
			cards.Add("TIME_217", new CardDef(new Power
			{
				Trigger = Generic.InsteadOfSpellDamage(NATURE, new CustomTask((g, c, s, t, stack) =>
				{
					if (s is Minion me) Summon(g, c, RandomMinionOfCost(g, V("TIME_217", "cost", 5)), s, me.ZonePosition + 1);
				}))
			}));
			// [TIME_218] Static Shock (0) — Deal $1 damage to a minion. Give your hero +1 Attack this turn.
			cards.Add("TIME_218", new CardDef(TargetMinion, new Power
			{
				PowerTask = ComplexTask.Create(new DamageTask(V("TIME_218", "damage", 1), EntityType.TARGET, true), new AddEnchantmentTask("TIME_218e", EntityType.HERO))
			}));
			cards.Add("TIME_218e", OneTurn(Effects.Attack_N(V("TIME_218", "attack", 1))));

			// ---------------------------------------------------------------- Warrior
			// [END_021] Dimensional Weaponsmith (3) — Battlecry: Give all minions and weapons in your hand +2 Attack.
			cards.Add("END_021", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) => GiveAll(g, s, c.HandZone.Where(p => Generic.IsMinionCard(p) || p is Weapon), "END_021e"))
			}));
			cards.Add("END_021e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("END_021", "attack", 2))) }));
			// [TIME_034] Stadium Announcer (4) — Rewind. Battlecry: Both players equip a random weapon. Give yours +1/+1. (any class, one
			// pick each, yours first: live vfx-decks 248627)
			cards.Add("TIME_034", new CardDef(new Power
			{
				PowerTask = RewindPower((g, c, s, t) =>
				{
					foreach (Controller p in new[] { c, c.Opponent })
					{
						if (!(RandomCard(g, x => x.Type == CardType.WEAPON) is Card card)) continue;
						var weapon = (Weapon)Entity.FromCard(p, card, new EntityData { { GameTag.CREATOR, s.Id } });
						weapon.Card.Power?.Aura?.Activate(weapon);
						weapon.Card.Power?.Trigger?.Activate(weapon);
						p.Hero.AddWeapon(weapon);
						if (p == c) Give(g, s, weapon, "TIME_034e");
					}
				})
			}));
			cards.Add("TIME_034e", new CardDef(new Power { Enchant = new Enchant(WeaponStats(V("TIME_034", "stats", 1))) }));
			// [TIME_714] Chrono-Lord Epoch (6) — Battlecry: Destroy all minions that your opponent played last turn. (played, not summoned:
			// live 204312 55231; the same entities, still on their board)
			cards.Add("TIME_714", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var played = new HashSet<int>(c.Opponent.PlayHistory.Where(h => h.Turn == g.Turn - 1 && h.SourceCard?.Type == CardType.MINION).Select(h => h.SourceId));
					foreach (Minion m in c.Opponent.BoardZone.GetAll().Where(m => played.Contains(m.Id) && !m.IsDead && !m.ToBeDestroyed).ToList())
						Generic.Destroy(m, s, many: true);
				})
			}));
			// [TIME_715] For Glory! (5) — Draw 2 cards. Costs (1) less for each minion your opponent controls.
			cards.Add("TIME_715", new CardDef(new Power
			{
				Aura = new AdaptiveCostEffect(p => p.Controller.Opponent.BoardZone.Count(m => !(m is Location) && !m.Untouchable) * V("TIME_715", "less", 1)),
				PowerTask = new CustomTask((g, c, s, t, stack) => { for (int i = 0; i < V("TIME_715", "draws", 2); i++) Generic.Draw(c); })
			}));
			// [TIME_716] Slow Motion (2) — Your opponent's cards cost (1) more next turn. (Speaker Stomper's way)
			cards.Add("TIME_716", new CardDef(new Power { PowerTask = new AddEnchantmentTask("TIME_716e3", EntityType.OP_CONTROLLER) }));
			cards.Add("TIME_716e3", OpponentNextTurnCost(V("TIME_716", "more", 1), new SelfCondition(p => true), "TIME_716e"));
			// [TIME_750] Precursory Strike (2) — Deal $3 damage. If you're holding a minion that costs (5) or more, draw a minion.
			cards.Add("TIME_750", new CardDef(TargetAny, new Power
			{
				GlowWhile = p => p.Controller.HandZone.Any(x => Generic.IsMinionCard(x) && x.Cost >= V("TIME_750", "cost", 5)),
				PowerTask = ComplexTask.Create(new DamageTask(V("TIME_750", "damage", 3), EntityType.TARGET, true), new CustomTask((g, c, s, t, stack) =>
				{
					if (c.HandZone.Any(p => Generic.IsMinionCard(p) && p.Cost >= V("TIME_750", "cost", 5))) DrawOne(g, c, p => Generic.IsMinionCard(p));
				}))
			}));
			// [TIME_850] Lo'Gosh, Blood Fighter (7) — Fabled, Rush. Deathrattle: Summon a Blood Fighter from your hand. It gains +5/+5 and
			// attacks a random enemy.
			cards.Add("TIME_850", new CardDef(new Power
			{
				DeathrattleTask = BloodFighter((g, c, m) =>
				{
					var enemies = AttackableEnemies(c);
					if (enemies.Count > 0 && m.Zone?.Type == Zone.PLAY) EffectAttack(g, m, enemies[g.Random.Next(enemies.Count)]);
				})
			}));
			// [TIME_850t] Broll, Blood Fighter (7) — Taunt. Deathrattle: Summon a Blood Fighter from your hand. Give it +5/+5 and Taunt.
			cards.Add("TIME_850t", new CardDef(new Power { DeathrattleTask = BloodFighter((g, c, m) => m[GameTag.TAUNT] = 1) }));
			// [TIME_850t1] Valeera, Blood Fighter (7) — Elusive. Deathrattle: Summon a Blood Fighter from your hand. Give it +5/+5 and Elusive.
			cards.Add("TIME_850t1", new CardDef(new Power
			{
				DeathrattleTask = BloodFighter((g, c, m) =>
				{
					m[GameTag.CANT_BE_TARGETED_BY_SPELLS] = 1;
					m[GameTag.CANT_BE_TARGETED_BY_HERO_POWERS] = 1;
				})
			}));
			cards.Add("TIME_850e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TIME_850", "stats", 5))) }));
			// [TIME_870] Gladiatorial Combat (6) — Summon a random minion from your deck. Summon a 5/5 Tiger with Stealth for your opponent.
			cards.Add("TIME_870", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					SummonFromDeck(g, c, s, p => p is Minion);
					Summon(g, c.Opponent, Cards.FromId("TIME_870t"), s);
				})
			}));
			// [TIME_871] Heir of Hereafter (5) — Taunt. Battlecry: Gain +2/+2 for each damaged minion. (one TIME_871e each — doubt for N>1)
			cards.Add("TIME_871", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					if (!(s is Minion me)) return;
					int n = AllOtherMinions(c, null).Count(m => m.Damage > 0);
					for (int i = 0; i < n; i++) Give(g, s, me, "TIME_871e");
				})
			}));
			cards.Add("TIME_871e", new CardDef(new Power { Enchant = new Enchant(Effects.AttackHealth_N(V("TIME_871", "stats", 2))) }));
			// [TIME_872] Undefeated Champion (8) — Rush. Battlecry: Fill your opponent's board with random 1-Cost minions.
			cards.Add("TIME_872", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					for (int guard = 0; guard < 7 && !c.Opponent.BoardZone.IsFull; guard++)
					{
						int before = c.Opponent.BoardZone.Count;
						Summon(g, c.Opponent, RandomMinionOfCost(g, V("TIME_872", "cost", 1)), s);
						if (c.Opponent.BoardZone.Count == before) break;
					}
				})
			}));
			// [TIME_873] Unleash the Crocolisks (2) — Gain 10 Armor. Summon two 2/3 Beasts for your opponent.
			cards.Add("TIME_873", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					new ArmorTask(V("TIME_873", "armor", 10)).Process(g, c, s, null);
					for (int i = 0; i < V("TIME_873", "beasts", 2); i++) Summon(g, c.Opponent, Cards.FromId("TIME_873t"), s);
				})
			}));
		}
	}
}
