// HsServer addition (not upstream SabberStone): Into the Emerald Dream keywords (Dark Gift, Kindred) and the Core cards using
// them. Every number goes through CardValues (V).
using System;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Actions;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.src.Loader;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks;
using SabberStoneCore.Tasks.SimpleTasks;

namespace SabberStoneCore.CardSets.HsServer
{
	public static partial class CurrentCoreCardsGen
	{
		/// <summary>A Dark Gift: its enchantment and which minions may get it (keyword gifts skip minions that already have one
		/// of their keywords; Patch 32.2.0).</summary>
		private sealed class DarkGiftDef
		{
			public string Enchantment;
			public Func<Card, bool> Allowed;
		}

		private static bool Has(Card card, GameTag tag) => card[tag] > 0;

		/// <summary>The constructed Dark Gift pool (Into the Emerald Dream; hearthstone.wiki.gg/wiki/Dark_Gift).</summary>
		private static readonly DarkGiftDef[] DarkGifts =
		{
			new DarkGiftDef { Enchantment = "EDR_100te", Allowed = x => !Has(x, GameTag.LIFESTEAL) },                         // Waking Terror
			new DarkGiftDef { Enchantment = "EDR_100t1e", Allowed = x => !Has(x, GameTag.CANT_BE_TARGETED_BY_SPELLS) },       // Well Rested
			new DarkGiftDef { Enchantment = "EDR_100t2e", Allowed = x => x.ATK >= 3 },                                          // Short Claws
			new DarkGiftDef { Enchantment = "EDR_100t3e", Allowed = x => !Has(x, GameTag.TAUNT) },                             // Bundled Up
			new DarkGiftDef { Enchantment = "EDR_100t5e", Allowed = x => true },                                                // Living Nightmare
			new DarkGiftDef { Enchantment = "EDR_100t6e", Allowed = x => x.ATK >= 1 && !Has(x, GameTag.CHARGE) },             // Sleepwalker
			new DarkGiftDef { Enchantment = "EDR_100t7e", Allowed = x => Has(x, GameTag.BATTLECRY) },                          // Rude Awakening
			new DarkGiftDef { Enchantment = "EDR_100t8e1", Allowed = x => true },                                               // Sweet Dreams (live vfx-decks 57571: EDR_100t8e1)
			new DarkGiftDef { Enchantment = "EDR_100t9e", Allowed = x => !Has(x, GameTag.REBORN) },                            // Persisting Horror
			new DarkGiftDef { Enchantment = "EDR_100t13e", Allowed = x => !Has(x, GameTag.DIVINE_SHIELD) && !Has(x, GameTag.WINDFURY) }, // Harpy's Talons
		};

		private const string SWEET_DREAMS = "EDR_100t8e1";

		/// <summary>Client HAS_DARK_GIFT (3880): live sets it on the card that gets a gift (c3dec 185040 64592).</summary>
		private const GameTag HAS_DARK_GIFT = (GameTag)3880;

		/// <summary>Does the card carry a Dark Gift (live HAS_DARK_GIFT: while its gift enchantment is on it)?</summary>
		private static bool HasDarkGift(IPlayable p) => p?.AppliedEnchantments?.Any(e => DarkGifts.Any(d => d.Enchantment == e.Card.Id)) == true;

		/// <summary>Gives <paramref name="card"/> the Dark Gift <paramref name="gift"/> (its enchantment and HAS_DARK_GIFT).</summary>
		private static void GiveDarkGift(Game g, IEntity source, IPlayable card, string gift)
		{
			Generic.AddEnchantmentBlock(g, Cards.FromId(gift), (IPlayable)source, card, 0, 0, 0);
			card[HAS_DARK_GIFT] = 1;
		}

		/// <summary>
		/// A Dark Gift was given to <paramref name="receiver"/> (the pick): every card of its owner's hand and deck that "gains a copy
		/// of every Dark Gift given to your minions" (Power.CopiesDarkGifts: Wallow) gets the same gift. Wiki Wallow: "Wallow also
		/// gains a copy of Dark Gifts given to itself"; Sweet Dreams "gets taken out of the hand and placed on top of the deck, or
		/// moved from whatever its position is in the deck to the top of the deck"; it keeps its own gifts when it moves back (a
		/// plain zone move here); "Wallow can gain the same Dark Gift multiple times".
		/// </summary>
		private static void DarkGiftGiven(Game g, IPlayable receiver, string gift)
		{
			if (!(receiver is Minion) || gift == null || receiver.Controller == null) return;
			Controller c = receiver.Controller;
			foreach (IPlayable wallow in c.HandZone.GetAll().Concat(c.DeckZone.GetAll()).Where(x => x.Card.Power?.CopiesDarkGifts == true).ToList())
			{
				GiveDarkGift(g, wallow, wallow, gift);
				if (gift != SWEET_DREAMS || c.DeckZone.IsFull && wallow.Zone?.Type != Zone.DECK) continue;
				wallow.Zone?.Remove(wallow);
				c.DeckZone.Add(wallow); // on top of the deck
			}
		}

		/// <summary>A random allowed Dark Gift for <paramref name="card"/> not in <paramref name="used"/> (null when none).</summary>
		private static string RandomDarkGift(Game g, Card card, HashSet<string> used)
		{
			var gifts = DarkGifts.Where(d => !used.Contains(d.Enchantment) && d.Allowed(card)).ToList();
			if (gifts.Count == 0) return null;
			string gift = gifts[g.Random.Next(gifts.Count)].Enchantment;
			used.Add(gift);
			return gift;
		}

		/// <summary>
		/// "Discover a minion with a Dark Gift": three different minions from <paramref name="pool"/>, each built set aside with a
		/// different random allowed Dark Gift (so the client shows the gifted cards). The pick goes to the hand, or on top of the
		/// deck with Sweet Dreams; <paramref name="afterPick"/> then runs on it.
		/// </summary>
		/// <param name="fromDeck">"Discover a minion from your deck. Give it a Dark Gift." (Xavius): the options are copies of these
		/// deck cards; the pick's real card is drawn (a real draw — live dk-classxp 22046), then gets the pick's gift.</param>
		/// <param name="shuffleOthers">"Shuffle the other two into your deck" (Jumpscare): the unpicked ones go into the deck with
		/// their gifts (live vfx-decks 280441).</param>
		/// <param name="options">The options themselves, one per slot (Smoke Bomb: a Combo, a Battlecry and a Stealth minion).</param>
		private static void DiscoverWithDarkGift(Game g, Controller c, IEntity source, IEnumerable<Card> pool, Action<IPlayable> afterPick = null,
			IList<IPlayable> fromDeck = null, bool shuffleOthers = false, IList<Card> options = null)
		{
			var deckCards = fromDeck?.GroupBy(p => p.Card.Id).Select(x => x.First()).OrderBy(_ => g.Random.Next()).Take(3).ToList();
			List<Card> cards = options != null ? options.ToList() : deckCards != null ? deckCards.Select(p => p.Card).ToList() : Pick(g, pool, 3);
			if (cards.Count == 0) return;
			var used = new HashSet<string>();
			var offered = new List<IPlayable>();
			var deckIds = new Dictionary<int, int>(); // offered copy id -> its deck card id (values only: a pick may resolve on a copy)
			for (int i = 0; i < cards.Count; i++)
			{
				Card card = cards[i];
				IPlayable p = Entity.FromCard(c, card, new EntityData
				{
					{ GameTag.CREATOR, source.Id },
					{ GameTag.DISPLAYED_CREATOR, source.Id }
				}, c.SetasideZone);
				if (deckCards != null) deckIds[p.Id] = deckCards[i].Id;
				if (RandomDarkGift(g, card, used) is string gift)
					GiveDarkGift(g, source, p, gift);
				offered.Add(p);
			}
			var offeredIds = offered.Select(p => p.Id).ToList();
			Generic.CreateChoiceEntities(c, source, offered, ChoiceAction.STACK, new CustomTask((g2, c2, s2, chosen, stack2) =>
			{
				if (chosen == null) return;
				if (shuffleOthers)
					foreach (int other in offeredIds.Where(id => id != chosen.Id))
						if (g2.IdEntityDic.TryGetValue(other, out IPlayable left) && left.Zone?.Type == Zone.SETASIDE && !c2.DeckZone.IsFull)
						{
							left.Zone.Remove(left);
							Generic.ShuffleIntoDeck.Invoke(c2, s2, left);
						}
				if (deckIds.TryGetValue(chosen.Id, out int deckId))
				{
					// the real card from the deck: drawn, then the gift the pick showed
					string giftOf = chosen.AppliedEnchantments?.Select(e => e.Card.Id).FirstOrDefault(id => DarkGifts.Any(d => d.Enchantment == id));
					chosen.Zone?.Remove(chosen);
					if (!g2.IdEntityDic.TryGetValue(deckId, out IPlayable real) || real.Zone?.Type != Zone.DECK) return;
					IPlayable drawn = Generic.Draw(c2, real);
					if (giftOf != null && drawn != null && drawn.Zone?.Type == Zone.HAND)
						GiveDarkGift(g2, s2, drawn, giftOf);
					if (giftOf != null && drawn != null) DarkGiftGiven(g2, drawn, giftOf);
					if (drawn != null) afterPick?.Invoke(drawn);
					return;
				}
				chosen.Zone?.Remove(chosen);
				if (chosen.AppliedEnchantments?.Any(e => e.Card.Id == SWEET_DREAMS) == true)
				{
					if (!c2.DeckZone.IsFull) c2.DeckZone.Add(chosen); // on top of the deck
				}
				else if (!c2.HandZone.IsFull)
				{
					Generic.AddHandPhase.Invoke(c2, chosen);
					// ("after you Discover": Generic.NoteDiscovered at the pick)
				}
				else
					c2.GraveyardZone.Add(chosen);
				DarkGiftGiven(g2, chosen, chosen.AppliedEnchantments?.Select(e => e.Card.Id).FirstOrDefault(id => DarkGifts.Any(d => d.Enchantment == id)));
				afterPick?.Invoke(chosen);
			}));
		}

		/// <summary>
		/// Kindred: you played a card last turn that shares a minion type or spell school with <paramref name="self"/>.
		/// </summary>
		/// (the shared rule: Generic.Kindred — dual types count, LOST_CITY pass)
		private static bool Kindred(Controller c, IEntity self) => Generic.Kindred(c, self);

		private static void EmeraldDream(IDictionary<string, CardDef> cards)
		{
			// [EDR_811] Rite of Atrocity (1) — Discover an Undead. Spend 2 Corpses to give it a Dark Gift. (live: the Corpses
			// spent first, then the Dark Gift Discover; without them a plain one)
			cards.Add("EDR_811", new CardDef(new Power
			{
				CorpseCost = V("EDR_811", "corpses", 2), // spent on play (glows with enough Corpses)
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					var undead = Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION && x.IsRace(Race.UNDEAD)
						&& (x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL))).ToList();
					if (Generic.SpendCorpses(c, s))
						DiscoverWithDarkGift(g, c, s, undead);
					else if (Pick(g, undead, 3) is { Count: > 0 } offer && !c.HandZone.IsFull)
						Generic.CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.HAND, offer.ToArray(), null);
				})
			}));

			// ---------------------------------------------------------------- Dark Gift enchantments
			cards.Add("EDR_100te", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("EDR_100t", "attack", 3)), new Effect(GameTag.LIFESTEAL, EffectOperator.SET, 1))
			}));
			cards.Add("EDR_100t1e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("EDR_100t1", "attack", 2)), Effects.Health_N(V("EDR_100t1", "health", 2)),
					new Effect(GameTag.CANT_BE_TARGETED_BY_SPELLS, EffectOperator.SET, 1),
					new Effect(GameTag.CANT_BE_TARGETED_BY_HERO_POWERS, EffectOperator.SET, 1))
			}));
			cards.Add("EDR_100t2e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.ReduceCost(V("EDR_100t2", "discount", 2)), new Effect(GameTag.ATK, EffectOperator.SUB, V("EDR_100t2", "attack", 2)))
			}));
			cards.Add("EDR_100t3e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Health_N(V("EDR_100t3", "health", 4)), new Effect(GameTag.TAUNT, EffectOperator.SET, 1))
			}));
			cards.Add("EDR_100t5e", new CardDef(new Power { Enchant = new Enchant(new Effect(Game.LIVING_NIGHTMARE, EffectOperator.SET, 1)) }));
			cards.Add("EDR_100t6e", new CardDef(new Power { Enchant = new Enchant(new Effect(GameTag.CHARGE, EffectOperator.SET, 1)) }));
			cards.Add("EDR_100t7e", new CardDef(new Power { Enchant = new Enchant(new Effect(Game.DOUBLE_BATTLECRY, EffectOperator.SET, 1)) }));
			cards.Add("EDR_100t8e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("EDR_100t8", "attack", 4)), Effects.Health_N(V("EDR_100t8", "health", 5)))
			}));
			cards.Add("EDR_100t8e1", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("EDR_100t8", "attack", 4)), Effects.Health_N(V("EDR_100t8", "health", 5)))
			}));
			// Persisting Horror: Reborn. "Is Reborn with full Health and enchantments" (VIOLET_HOLD: the shared rule, client tag 2872 —
			// Game.RebornTask; live dk-classxp 34940: this enchantment goes when the minion comes back, its tag 4137)
			cards.Add("EDR_100t9e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.REBORN, EffectOperator.SET, 1), new Effect(Generic.FULL_HEALTH_REBORN, EffectOperator.SET, 1))
			}));
			cards.Add("EDR_100t13e", new CardDef(new Power
			{
				Enchant = new Enchant(new Effect(GameTag.DIVINE_SHIELD, EffectOperator.SET, 1), new Effect(GameTag.WINDFURY, EffectOperator.SET, 1))
			}));

			// ---------------------------------------------------------------- Cards

			// [EDR_004] Raptor Herald (3) — Rewind. Battlecry: Discover a Beast with a Dark Gift. Kindred: It costs (1) less.
			// (Rewind: RewindPower — Keep / Rewind, Generic.OfferRewind)
			var raptorHerald = new CardDef(new Power
			{
				PowerTask = RewindPower((g, c, s, t) =>
				{
					bool kindred = Generic.KindredPlayed(c, s);
					var beasts = Cards.GenerationPool(g, c).Where(x => x.Type == CardType.MINION && x.Collectible && x.IsRace(Race.BEAST)
						&& (x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL)));
					DiscoverWithDarkGift(g, c, s, beasts, picked => { if (kindred) Give(g, s, picked, "GBL_003e"); });
				})
			});
			// [EDR_001] Hopeful Dryad (3) — Battlecry: Get a random Dream card. (Not CORE_EDR_001, a different card.)
			cards.Add("EDR_001", new CardDef(new Power
			{
				PowerTask = new CustomTask((g, c, s, t, stack) =>
				{
					string[] dreams = Cards.FromId("EX1_572").Entourage;
					if (dreams?.Length > 0) AddToHand(c, Cards.FromId(dreams[g.Random.Next(dreams.Length)]));
				})
			}));
			// Originals of Core reprints with current text the engine had no script for (the Core copy shares it).
			// [BT_142] Shadowhoof Slayer (1) — Battlecry: Give your hero +1 Attack this turn.
			var shadowhoof = new CardDef(new Power { PowerTask = new AddEnchantmentTask("BT_142e", EntityType.HERO) });
			cards.Add("BT_142", shadowhoof); // CORE_BT_142 is loaded as this card
			cards.Add("BT_142e", new CardDef(new Power { Enchant = new Enchant(Effects.Attack_N(V("BT_142", "attack", 1))) { IsOneTurnEffect = true } }));
			// [WON_350] I Know a Guy (1) — Discover a Taunt minion. Give it +1/+2.
			var knowAGuy = new CardDef(new Power
			{
				PowerTask = DiscoverFromTask.ClassOrNeutral((c, x) => x.Type == CardType.MINION && x[GameTag.TAUNT] > 0, "CFM_940e")
			});
			cards.Add("WON_350", knowAGuy);
			cards.Add("CORE_WON_350", knowAGuy);
			cards.Add("CFM_940e", new CardDef(new Power
			{
				Enchant = new Enchant(Effects.Attack_N(V("WON_350", "attack", 1)), Effects.Health_N(V("WON_350", "health", 2)))
			}));
			cards.Add("CORE_EDR_004", raptorHerald);
			cards.Add("CORE_EDR_004_2026", raptorHerald);
			// The client has no Raptor Herald enchantment: the generic "Cost - 1" (GBL_003e) shows the discount.

			EmeraldChunk1(cards); // EmeraldDreamCardsGenHs2.cs
			EmeraldChunk2(cards); // EmeraldDreamCardsGenHs3.cs
			EmeraldChunk3(cards); // EmeraldDreamCardsGenHs4.cs
			EmeraldChunk4(cards); // EmeraldDreamCardsGenHs5.cs
		}
	}
}
