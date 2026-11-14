// HsServer addition (not upstream SabberStone): deck sideboards — the cards a player picks while building a deck for a card
// with a sideboard (client SIDEBOARD_TYPE 3427: 1 E.T.C., Band Manager's band, 2 Zilliax Deluxe 3000's modules, 3 Commander
// Beatrix's 2-Cost minion, 4 King of the Underbelly's contraband Beasts). The lobby keeps them with the deck
// (DeckContents.SideboardCards); the game gets them in GameConfig.Player1Sideboard / Player2Sideboard and each player keeps
// them in Controller.Sideboards. Wiki E.T.C.: "In-game, cards in the sideboard are not considered to be in the player's deck."
using System;
using System.Collections.Generic;
using System.Linq;
using SabberStoneCore.Auras;
using SabberStoneCore.Enchants;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks;
using SabberStoneCore.Tasks.SimpleTasks;
using SabberStoneCore.Triggers;

namespace SabberStoneCore.Model
{
	/// <summary>HsServer: one deck sideboard — the card that has it and the cards picked for it, in the deck builder's order.
	/// Never changed in place (Generic.TakeFromSideboard makes a new one).</summary>
	public sealed class Sideboard
	{
		public Card Owner { get; }
		public IReadOnlyList<Card> Cards { get; }

		public Sideboard(Card owner, IReadOnlyList<Card> cards)
		{
			Owner = owner;
			Cards = cards;
		}

		public override string ToString() => $"{Owner?.Id}: [{string.Join(", ", Cards.Select(c => c.Id))}]";
	}
}

namespace SabberStoneCore.Actions
{
	public static partial class Generic
	{
		/// <summary>Client card tags of the deck sideboards (GAME_TAG).</summary>
		public const GameTag SIDEBOARD_TYPE = (GameTag)3427, MAX_SIDEBOARD_CARDS = (GameTag)2931,
			ZILLIAX_CUSTOMIZABLE_COSMETICMODULE = (GameTag)3376, ZILLIAX_CUSTOMIZABLE_FUNCTIONALMODULE = (GameTag)3377,
			ZILLIAX_CUSTOMIZABLE_LINKED_COSMETICMOUDLE = (GameTag)3450, DONT_PICK_FROM_SUBSETS = (GameTag)676,
			HIDE_HEALTH_NUMBER = (GameTag)3471, HIDE_ATTACK_NUMBER = (GameTag)3472;

		/// <summary>A player's sideboards from (owner card, sideboard card) pairs: one per owner card, in the given order.</summary>
		public static IReadOnlyList<Sideboard> SideboardsOf(IEnumerable<(Card Owner, Card Card)> entries)
		{
			if (entries == null) return Array.Empty<Sideboard>();
			var list = new List<Sideboard>();
			foreach (IGrouping<int, (Card Owner, Card Card)> owner in entries.Where(e => e.Owner != null && e.Card != null).GroupBy(e => e.Owner.AssetId))
				list.Add(new Sideboard(owner.First().Owner, owner.Select(e => e.Card).ToArray()));
			return list;
		}

		/// <summary>The cards in <paramref name="c"/>'s sideboard for <paramref name="owner"/> (empty when none). The sideboard
		/// belongs to the player, not the card: a copy, a generated or a stolen one uses its player's own (wiki E.T.C., Band
		/// Manager: "E.T.C. is tied to the player, not the card itself. Copying or stealing and replaying an opponent's E.T.C.
		/// would pull from your own sideboard, which may be non-existent if you are not playing E.T.C. yourself") — the one
		/// built for that card, else for a card of the same SIDEBOARD_TYPE (another version of it).</summary>
		public static IReadOnlyList<Card> SideboardCards(Controller c, Card owner) => FindSideboard(c, owner)?.Cards ?? Array.Empty<Card>();

		private static Sideboard FindSideboard(Controller c, Card owner)
		{
			if (c == null || owner == null) return null;
			foreach (Sideboard s in c.Sideboards)
				if (s.Owner.AssetId == owner.AssetId) return s;
			int type = owner[SIDEBOARD_TYPE];
			if (type == 0) return null;
			foreach (Sideboard s in c.Sideboards)
				if (s.Owner[SIDEBOARD_TYPE] == type) return s;
			return null;
		}

		/// <summary>Takes one <paramref name="card"/> out of <paramref name="c"/>'s sideboard for <paramref name="owner"/> for
		/// the rest of the game (wiki E.T.C.: "Once a card is picked to be discovered from E.T.C.'s sideboard, it is removed
		/// from there for the rest of the game"). False when it isn't there.</summary>
		public static bool TakeFromSideboard(Controller c, Card owner, Card card)
		{
			Sideboard found = FindSideboard(c, owner);
			if (found == null || card == null) return false;
			int at = -1;
			for (int i = 0; i < found.Cards.Count && at < 0; i++)
				if (found.Cards[i].Id == card.Id) at = i;
			for (int i = 0; i < found.Cards.Count && at < 0; i++)
				if (found.Cards[i].AssetId == card.AssetId) at = i;
			if (at < 0) return false;
			var left = new Sideboard(found.Owner, found.Cards.Where((x, i) => i != at).ToArray());
			c.Sideboards = c.Sideboards.Select(s => s == found ? left : s).ToArray();
			return true;
		}

		/// <summary>
		/// "Battlecry: Discover one!" of the player's sideboard for the source card (E.T.C.'s band, King of the Underbelly's
		/// Beasts): its cards are offered (no duplicate options: wiki Discover), the pick goes to the hand and leaves the
		/// sideboard for the rest of the game, then gets <paramref name="enchantment"/> (a cost change) when given. An empty
		/// sideboard offers nothing (hearthpwn, E.T.C.: "If you Discover/generate/steal ETC, you're offered nothing unless you
		/// run ETC yourself, in which case you're offered your ETC sideboard"); a full hand Discovers nothing, as every
		/// Discover here (DiscoverFromTask).
		/// </summary>
		public static ISimpleTask DiscoverFromSideboard(string enchantment = null, int times = 1) =>
			new CustomTask((g, c, s, t, stack) =>
			{
				Card owner = Cards.SelfOf(s);
				// live (live-20261007-zilliax-etc-remix 8950ff, E.T.C. with a band): the band's set-aside entities name themselves
				// (LINKED_ENTITY), then the options are new copies of them, last band card first (CREATOR = the source,
				// LINKED_ENTITY / COPIED_FROM_ENTITY_ID = the band card, 3669 = 1, WAS_DISCOVER_OPTION); the pick goes to the hand
				List<IPlayable> band = BandEntities(c, owner);
				if (band.Count > 0)
				{
					if (c.HandZone.IsFull) return;
					var offered = new List<IPlayable>();
					foreach (IPlayable member in band) member[GameTag.LINKED_ENTITY] = member.Id;
					foreach (IPlayable member in Enumerable.Reverse(band).GroupBy(x => x.Card.Id).Select(x => x.First()).Take(3))
					{
						IPlayable copy = Entity.FromCard(c, member.Card, new EntityData { [GameTag.CREATOR] = s.Id }, c.SetasideZone);
						copy[GameTag.LINKED_ENTITY] = member.Id;
						copy[HsTag.COPIED_FROM_ENTITY_ID] = member.Id;
						copy[(GameTag)3669] = 1;
						copy[HsTag.WAS_DISCOVER_OPTION] = 1;
						offered.Add(copy);
					}
					CreateChoiceEntities(c, s, offered, ChoiceAction.HAND, new CustomTask((g2, c2, s2, picked, st) =>
					{
						if (picked == null) return;
						TakeFromSideboard(c2, Cards.SelfOf(s2), picked.Card);
						if (g2.IdEntityDic.TryGetValue(picked[GameTag.LINKED_ENTITY], out IPlayable member)) member[HsTag.SIDEBOARD_OF] = 0;
						if (enchantment != null)
							for (int i = 0; i < times; i++)
								AddEnchantmentBlock(g2, Cards.FromId(enchantment), (IPlayable)s2, picked, 0, 0, 0);
					}));
					return;
				}
				Card[] offer = SideboardCards(c, owner).GroupBy(x => x.Id).Select(x => x.First()).Take(3).ToArray();
				if (offer.Length == 0 || c.HandZone.IsFull) return;
				CreateChoiceCards.Invoke(c, s, null, ChoiceType.GENERAL, ChoiceAction.HAND, offer, new CustomTask((g2, c2, s2, picked, st) =>
				{
					if (picked == null) return;
					TakeFromSideboard(c2, Cards.SelfOf(s2), picked.Card);
					if (enchantment != null)
						for (int i = 0; i < times; i++)
							AddEnchantmentBlock(g2, Cards.FromId(enchantment), (IPlayable)s2, picked, 0, 0, 0);
				}));
			});

		/// <summary>
		/// HsServer: a sideboard its owner Discovers from (the owner card has DISCOVER: E.T.C.'s band, King of the Underbelly's
		/// Beasts) exists in the game as set-aside entities its player sees. Live (live-20261007-zilliax-etc-remix 135-175): FULL_ENTITY
		/// in SETASIDE right after the player's deck entities, in the band's order. Game's constructor calls it per player.
		/// </summary>
		public static void CreateBandEntities(Controller c)
		{
			foreach (Sideboard s in c.Sideboards)
			{
				if (s.Owner[GameTag.DISCOVER] == 0) continue;
				foreach (Card card in s.Cards)
				{
					IPlayable member = Entity.FromCard(c, card, null, c.SetasideZone);
					member[HsTag.SIDEBOARD_OF] = s.Owner.AssetId;
				}
			}
		}

		/// <summary>The set-aside band entities of <paramref name="c"/>'s sideboard for <paramref name="owner"/> not picked yet
		/// (the same owner or one of the same SIDEBOARD_TYPE), in order.</summary>
		private static List<IPlayable> BandEntities(Controller c, Card owner)
		{
			Sideboard s = FindSideboard(c, owner);
			if (s == null) return new List<IPlayable>();
			return c.SetasideZone.Where(p => p[HsTag.SIDEBOARD_OF] == s.Owner.AssetId).OrderBy(p => p.Id).ToList();
		}

		/// <summary>
		/// The sideboard cards' deck-building effects (Power.SideboardTask: Commander Beatrix's "Ten copies join your deck!"),
		/// player 1's deck first: each card in a deck with a non-empty sideboard. Live (live-20261003-dk-classxp, Beatrix):
		/// right after the players start PLAYING and before the first player and the opening hands — so the copies can be
		/// drawn into the opening hand — one TRIGGER block by the card (effect index 2, no keyword) with the card revealed
		/// in it (SHOW_ENTITY … HIDE_ENTITY, REVEALED 0).
		/// </summary>
		public static void SideboardEffects(Game g)
		{
			foreach (Controller c in new[] { g.Player1, g.Player2 })
				foreach (IPlayable card in c.DeckZone.ToArray())
				{
					ISimpleTask task = card.Card.Power?.SideboardTask;
					// (VIOLET_HOLD: a card without a sideboard runs it too — Azalina's "plus 20 copied from your enemy")
					if (task == null || card.Card[SIDEBOARD_TYPE] > 0 && SideboardCards(c, card.Card).Count == 0) continue;
					g.TaskQueue.Execute(new CustomTask((g2, c2, s2, t2, st) =>
					{
						var me = (IPlayable)s2;
						bool inDeck = me.Zone?.Type == Zone.DECK;
						if (inDeck) me[GameTag.REVEALED] = 1;
						task.Process(g2, c2, s2, null);
						if (inDeck && me.Zone?.Type == Zone.DECK) me[GameTag.REVEALED] = 0;
					}), c, card, null, keyword: 0, effectIndex: 2);
				}
		}

		/// <summary>"… join your deck": a new <paramref name="card"/> put into <paramref name="c"/>'s deck at a random place,
		/// shown as created by <paramref name="source"/> (live: DISPLAYED_CREATOR as a TAG_CHANGE right after its FULL_ENTITY).
		/// Not a shuffle (no SHUFFLE_DECK, no "shuffled into" triggers), and a generated card: the "your deck has only odd /
		/// even-Cost cards" checks don't see it (wiki Commander Beatrix: "The 10 copies of the chosen minion are considered to
		/// be generated cards, not ones that started in the player's deck"). Null when the deck is full.</summary>
		public static IPlayable JoinDeck(Controller c, IEntity source, Card card)
		{
			if (card == null || c.DeckZone.IsFull) return null;
			bool noEven = c.DeckZone.NoEvenCostCards, noOdd = c.DeckZone.NoOddCostCards;
			int at = c.DeckZone.Count == 0 ? -1 : c.Game.Random.Next(c.DeckZone.Count + 1);
			IPlayable joined = Entity.FromCard(c, card, zone: c.DeckZone, zonePos: at);
			c.DeckZone.NoEvenCostCards = noEven;
			c.DeckZone.NoOddCostCards = noOdd;
			if (source != null) joined[GameTag.DISPLAYED_CREATOR] = source.Id;
			return joined;
		}

		/// <summary>A deck card as its sideboard built it: a card whose sideboard holds two functional modules (client
		/// ZILLIAX_CUSTOMIZABLE_FUNCTIONALMODULE) is its cosmetic module card (ZILLIAX_CUSTOMIZABLE_COSMETICMODULE, the look)
		/// with both modules combined (<see cref="ModularCard"/>); every other card is itself.</summary>
		public static Card AsBuilt(Controller c, Card card)
		{
			if (card == null || card[SIDEBOARD_TYPE] == 0) return card;
			IReadOnlyList<Card> sideboard = SideboardCards(c, card);
			List<Card> modules = sideboard.Where(x => x[ZILLIAX_CUSTOMIZABLE_FUNCTIONALMODULE] > 0).Take(2).ToList();
			if (modules.Count < 2) return card;
			Card look = sideboard.FirstOrDefault(x => x[ZILLIAX_CUSTOMIZABLE_COSMETICMODULE] > 0) ?? card;
			return ModularCard(look, modules[0], modules[1]);
		}

		// What a module part is rather than what it gives: identity, stats (summed instead) and its module bookkeeping.
		private static readonly HashSet<GameTag> ModulePartOwnTags = new HashSet<GameTag>
		{
			GameTag.CARDTYPE, GameTag.CLASS, GameTag.COLLECTIBLE, GameTag.RARITY, GameTag.ELITE, GameTag.CARD_SET, GameTag.FACTION,
			GameTag.CARDRACE, GameTag.COST, GameTag.ATK, GameTag.HEALTH, DONT_PICK_FROM_SUBSETS, ZILLIAX_CUSTOMIZABLE_FUNCTIONALMODULE,
			ZILLIAX_CUSTOMIZABLE_LINKED_COSMETICMOUDLE,
		};

		/// <summary>
		/// A card made of two module parts, with <paramref name="look"/>'s id and picture (Zilliax Deluxe 3000: wiki "The two
		/// Modules will have their attack, health, cost, and all card text combined into one card"; the art piece "is purely a
		/// cosmetic pick"). As the client builds it (ZilliaxSideboardDeck.UpdateZilliaxPreview): COST / ATK / HEALTH the parts'
		/// sums, HIDE_STATS / HIDE_COST / HIDE_ATTACK_NUMBER / HIDE_HEALTH_NUMBER off, MODULAR_ENTITY_PART_1 / _2 the parts' dbf
		/// ids (the client's ZILLIAX_DELUXE_3000 text builder writes the card text from them). The parts' keywords and effect
		/// tags (Taunt, Deathrattle, Elusive...) are the card's; its effects are both parts' (<see cref="CombinedPower"/>).
		/// </summary>
		public static Card ModularCard(Card look, Card part1, Card part2)
		{
			var tags = new Dictionary<GameTag, int>(look.Tags);
			foreach (Card part in new[] { part1, part2 })
				foreach (KeyValuePair<GameTag, int> t in part.Tags)
					if (!ModulePartOwnTags.Contains(t.Key))
						tags[t.Key] = Math.Max(tags.TryGetValue(t.Key, out int had) ? had : 0, t.Value);
			tags[GameTag.COST] = part1[GameTag.COST] + part2[GameTag.COST];
			tags[GameTag.ATK] = part1[GameTag.ATK] + part2[GameTag.ATK];
			tags[GameTag.HEALTH] = part1[GameTag.HEALTH] + part2[GameTag.HEALTH];
			// the look's hidden numbers are shown: the tags go (the client reads an entity's tags only from the server, and the
			// engine takes any HIDE_STATS, even 0, for "can't be played")
			foreach (GameTag hidden in new[] { GameTag.HIDE_STATS, GameTag.HIDE_COST, HIDE_ATTACK_NUMBER, HIDE_HEALTH_NUMBER })
				tags.Remove(hidden);
			// live (live-20261007-zilliax-etc-remix, TOY_330t5 drawn): the built card isn't a cosmetic module itself
			tags[ZILLIAX_CUSTOMIZABLE_COSMETICMODULE] = 0;
			tags[GameTag.MODULAR_ENTITY_PART_1] = part1.AssetId;
			tags[GameTag.MODULAR_ENTITY_PART_2] = part2.AssetId;
			var tagList = tags.Select(t => new Tag(t.Key, t.Value)).ToList();
			tagList.Add(new Tag(GameTag.CARDNAME, look.Name ?? ""));
			tagList.Add(new Tag(GameTag.CARDTEXT, string.Join("\n", new[] { part1.Text, part2.Text }.Where(x => !string.IsNullOrEmpty(x)))));
			var built = new Card(look.Id, look.AssetId, tagList.ToArray(), new Dictionary<PlayReq, int>(), look.Entourage,
				look.RefTags.Select(r => new Tag(r.Key, r.Value)).ToArray());
			var requirements = new Dictionary<PlayReq, int>();
			foreach (Card x in new[] { look, part1, part2 })
				if (x.PlayRequirements != null)
					foreach (KeyValuePair<PlayReq, int> r in x.PlayRequirements)
						requirements[r.Key] = r.Value;
			if (requirements.Count > 0) built.SetPlayRequirements(requirements);
			built.Power = CombinedPower(part1.Power, part2.Power);
			built.Implemented = part1.Implemented && part2.Implemented;
			return built;
		}

		/// <summary>Two parts' effects as one card's: each slot from whichever part has it; both when both do — tasks one
		/// after the other (part 1 first), triggers and auras together (MultiTrigger / MultiAura: a hand cost aura and a
		/// board aura each work where they belong, MultiAura.CostEffect).</summary>
		public static Power CombinedPower(Power a, Power b)
		{
			if (a == null) return b;
			if (b == null) return a;
			ISimpleTask Both(ISimpleTask x, ISimpleTask y) => x == null ? y : y == null ? x : ComplexTask.Create(x, y);
			return new Power
			{
				PowerTask = Both(a.PowerTask, b.PowerTask),
				DeathrattleTask = Both(a.DeathrattleTask, b.DeathrattleTask),
				ComboTask = Both(a.ComboTask, b.ComboTask),
				TopdeckTask = Both(a.TopdeckTask, b.TopdeckTask),
				OverkillTask = Both(a.OverkillTask, b.OverkillTask),
				HonorableKillTask = Both(a.HonorableKillTask, b.HonorableKillTask),
				Trigger = a.Trigger == null ? b.Trigger : b.Trigger == null ? a.Trigger : new MultiTrigger(a.Trigger, b.Trigger),
				Aura = a.Aura == null ? b.Aura : b.Aura == null ? a.Aura : new MultiAura(a.Aura, b.Aura),
				Enchant = a.Enchant ?? b.Enchant,
				NoScript = a.NoScript && b.NoScript,
			};
		}
	}
}
