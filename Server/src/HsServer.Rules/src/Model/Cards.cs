#region copyright
// SabberStone, Hearthstone Simulator in C# .NET Core
// Copyright (C) 2017-2019 SabberStone Team, darkfriend77 & rnilva
//
// SabberStone is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as
// published by the Free Software Foundation, either version 3 of the
// License.
// SabberStone is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
#endregion
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using SabberStoneCore.Enums;
using SabberStoneCore.Loader;
using SabberStoneCore.Model.Entities;
using static SabberStoneCore.Model.Cards;

namespace SabberStoneCore.Model
{
	/// <summary>
	/// Provides helper methods for filtering the entire card set.
	/// </summary>
	public static class Cards
	{
		/// <summary>
		/// Object holding the information of all known cards.
		/// </summary>
		private static readonly CardContainer Data;

		/// <summary>
		/// Specifies which card sets combine into the STANDARD set.
		/// </summary>
		public static CardSet[] StandardSets { get; } = {

			CardSet.CORE,
			CardSet.EXPERT1,

			CardSet.GILNEAS, // 2018 The Witchwood
			CardSet.BOOMSDAY, // 2018 The Boomsday Project
			CardSet.TROLL, // 2018 Rastakhan’s Rumble

			CardSet.DALARAN, // 2019 Rise of Shadows
			CardSet.ULDUM, // 2019 Saviors of Uldum
			CardSet.DRAGONS // 2019 Descent of Dragons
		};

		/// <summary>
		/// Specifies which card sets combine into the WILD set.
		/// </summary>
		public static CardSet[] WildSets { get; } = {

			// standard
			CardSet.CORE,
			CardSet.EXPERT1,

			CardSet.GILNEAS,
			CardSet.BOOMSDAY,
			CardSet.TROLL,
			CardSet.DALARAN,
			CardSet.ULDUM,
			CardSet.DRAGONS,

			// wild
			CardSet.BRM,
			CardSet.GVG,
			CardSet.NAXX,
			CardSet.LOE,
			CardSet.TGT,
			CardSet.OG,
			CardSet.KARA,
			CardSet.GANGS,
			CardSet.UNGORO,
			CardSet.ICECROWN,
			CardSet.LOOTAPALOOZA,

			// hall of fame
			CardSet.HOF
		};

		/// <summary>
		/// Defines all card classes that can be used for deck creation.
		/// </summary>
		public static CardClass[] HeroClasses => new[]
		{
			CardClass.DRUID,
			CardClass.HUNTER,
			CardClass.PALADIN,
			CardClass.ROGUE,
			CardClass.SHAMAN,
			CardClass.WARLOCK,
			CardClass.MAGE,
			CardClass.PRIEST,
			CardClass.WARRIOR,
			CardClass.DEMONHUNTER, // HsServer
			CardClass.DEATHKNIGHT // HsServer (client class 1; Arthas comes from the client DB)
		};

		#region CARD_SETUP

		static Cards()
		{
			// Fetch all cards.
			var cardLoader = new CardLoader();
			Card[] cards = cardLoader.Load();

			//string json = File.ReadAllText(CardLoader.Path + @"SabberStone\HSProtoSim\Loader\Data\cardData.json");
			//string json = File.ReadAllText(Environment.CurrentDirectory + @"\cardData.json");
			//var cards = JsonConvert.DeserializeObject<IEnumerable<Card>>(Resources.cardDataJson);
			//File.WriteAllText(CardLoader.Path + @"SabberStone\HSProtoSim\Loader\Data\cardDataJson.txt", JsonConvert.SerializeObject(cards, Formatting.Indented));
			// Set as card definitions

			Data = new CardContainer();
			Data.Load(cards);

			//Log.Debug("Standard:");
			//Enum.GetValues(typeof(CardClass)).Cast<CardClass>().ToList().ForEach(heroClass =>
			for (int i = 0; i < HeroClasses.Length; i++)
			{
				CardClass heroClass = HeroClasses[i];
				Standard.Add(heroClass, All.Where(c =>
					c.Collectible &&
					InClassPool(c, heroClass) &&
					!IsHeroPortrait(c) && StandardSets.Contains(c.Set) && !c.NeverGenerated).ToList().AsReadOnly());
				//Log.Debug($"-> [{heroClass}] - {Standard[heroClass].Count} cards.");
				//});
			}

			//Log.Debug("AllStandard:");
			AllStandard = All.Where(c => c.Collectible && !IsHeroPortrait(c) && StandardSets.Contains(c.Set) && !c.NeverGenerated).ToList().AsReadOnly();

			//Log.Debug("Wild:");
			Enum.GetValues(typeof(CardClass)).Cast<CardClass>().ToList().ForEach(heroClass =>
			{
				Wild.Add(heroClass, All.Where(c =>
				c.Collectible &&
					InClassPool(c, heroClass) &&
					 !IsHeroPortrait(c) && !c.NeverGenerated).ToList().AsReadOnly());
				//Log.Debug($"-> [{heroClass}] - {Wild[heroClass].Count} cards.");
			});

			//Log.Debug("AllWild:");
			// HsServer: these are the random pools (Discover, random generation, random summons and transforms): cards that are
			// never generated (Card.NeverGenerated: Quests, Questlines, Titans, triple-rune cards) aren't in them.
			// (2026-11-14, user: a Heist Random deck held the old Classic-format Blizzard) the Classic format's copies (CARD_SET VANILLA
			// 1646, VAN_ ids) belong to that format only — never in the Standard / Wild pools (their real versions are there)
			AllWild = All.Where(c => c.Collectible && !IsHeroPortrait(c) && !c.NeverGenerated && !IsClassicFormatCopy(c)).ToList().AsReadOnly();

			StandardCostMinionCards = AllStandard.Where(c => c.Type == CardType.MINION).GroupBy(c => c.Cost).ToDictionary(g => g.Key, g => g.ToList());
			WildCostMinionCards = AllWild.Where(c => c.Type == CardType.MINION).GroupBy(c => c.Cost).ToDictionary(g => g.Key, g => g.ToList());
			StandardBoardCostMinionCards = StandardCostMinionCards.ToDictionary(x => x.Key, x => x.Value.Where(c => !c.NeverRandomlyOnBoard).ToList());
			WildBoardCostMinionCards = WildCostMinionCards.ToDictionary(x => x.Key, x => x.Value.Where(c => !c.NeverRandomlyOnBoard).ToList());

			// HsServer: Lotus Assassin's Stealth is an own keyword the card data also files as a reference. With the client data
			// (the server, always) the loader's shared rule reads it (ReprintCards.LoadClient); upstream's patch stays only for
			// the 2021-XML-only mode of the upstream rules tests, whose XML has the same gap.
			if (!Data.Cards["CFM_634"].Tags.ContainsKey(GameTag.STEALTH))
			{
				Data.Cards["CFM_634"].Stealth = true;
				Data.Cards["CFM_634"].Tags.Add(GameTag.STEALTH, 1);
			}

			// Basic Totems
			BasicTotems = new[]
			{
				FromId("NEW1_009"),	// Healing Totem
				FromId("CS2_050"),	// Searing Totem
				FromId("CS2_051"),	// Stoneclaw Totem
				FromId("CS2_052")	// Wraith of Air Totem
			};

			// filtered out cards ... cosmetic purpose
			Data.Cards.Remove("HERO_01c"); // HERO Deathwing
		}

		#endregion

		#region FILTERS

		/// <summary>
		/// Returns all known cards.
		/// </summary>
		public static IEnumerable<Card> All => Data.Cards.Values;

		/// <summary>
		/// Returns the count of all known cards.
		/// </summary>
		public static int Count => Data.Cards.Count;

		/// <summary>
		/// Retrieves all wild cards ordered by card class.
		/// </summary>
		public static Dictionary<CardClass, IReadOnlyList<Card>> Wild { get; } = new Dictionary<CardClass, IReadOnlyList<Card>>();

		/// <summary>
		/// Retrieves all standard cards ordered by card class.
		/// </summary>
		public static Dictionary<CardClass, IReadOnlyList<Card>> Standard { get; } = new Dictionary<CardClass, IReadOnlyList<Card>>();

		/// <summary>
		/// All cards belonging to the Standard set.
		/// </summary>
		public static ReadOnlyCollection<Card> AllStandard { get; }

		/// <summary>
		/// All cards belonging to the Wild set.
		/// </summary>
		public static ReadOnlyCollection<Card> AllWild { get; }

		/// <summary>
		/// A list of the four basic totems.
		/// [0]: Healing
		/// [1]: Searing
		/// [2]: Stoneclaw
		/// [3]: Wraith of Air
		/// </summary>
		public static IReadOnlyList<Card> BasicTotems { get; private set; }

		/// <summary>
		/// Retrieves the specified set of cards, sorted by <see cref="CardClass"/>.
		/// </summary>
		/// <param name="formatType"></param>
		/// <returns></returns>
		public static Dictionary<CardClass, IReadOnlyList<Card>> FormatTypeClassCards(FormatType formatType) => formatType == FormatType.FT_STANDARD ? Standard : Wild;

		/// <summary>
		/// Retrieves the specifified set of cards.
		/// </summary>
		/// <param name="formatType"></param>
		/// <returns></returns>
		public static IEnumerable<Card> FormatTypeCards(FormatType formatType) => formatType == FormatType.FT_STANDARD ? AllStandard : AllWild;

		/// <summary>
		/// Returns the default hero class card.
		/// ex; Returns Grommash for the WARRIOR class.
		/// </summary>
		/// <param name="cardClass"></param>
		/// <returns></returns>
		public static Card HeroCard(CardClass cardClass)
		{
			//return All.FirstOrDefault(c => c.Type == CardType.HERO && c.Id.StartsWith("HERO") && c.HasClass(cardClass));
			switch (cardClass)
			{
				case CardClass.DRUID:
					return FromId("HERO_06");
				case CardClass.HUNTER:
					return FromId("HERO_05");
				case CardClass.MAGE:
					return FromId("HERO_08");
				case CardClass.PALADIN:
					return FromId("HERO_04");
				case CardClass.PRIEST:
					return FromId("HERO_09");
				case CardClass.ROGUE:
					return FromId("HERO_03");
				case CardClass.SHAMAN:
					return FromId("HERO_02");
				case CardClass.WARLOCK:
					return FromId("HERO_07");
				case CardClass.WARRIOR:
					return FromId("HERO_01");
				case CardClass.DEMONHUNTER: // HsServer (Illidan comes from the client DB)
					return TryFromId("HERO_10"); // null without the client DB
				case CardClass.DEATHKNIGHT: // HsServer (Arthas, the Lich King)
					return TryFromId("HERO_11");
				default:
					throw new NotImplementedException();
			}
		}

		/// <summary>
		/// Returns a sequence of all known hero cards.
		/// </summary>
		/// <returns></returns>
		public static IEnumerable<Card> HeroCards()
		{
			return All.Where(c => c.Type == CardType.HERO && c.Id.StartsWith("HERO"));
		}

		/// <summary>
		/// Returns matching DK HeroPower Card of given Asset Id using direct mapping.
		/// </summary>
		public static Card GetHeroPower(int assetId)
		{
			switch (assetId)
			{
				case 42982:
					return FromId("ICC_481p");
				case 43188:
					return FromId("ICC_827p");
				case 43183:
					return FromId("ICC_828p");
				case 43013:
					return FromId("ICC_829p");
				case 45397:
					return FromId("ICC_830p");
				case 43181:
					return FromId("ICC_831p");
				case 43182:
					return FromId("ICC_832p");
				case 42944:
					return FromId("ICC_833h");
				case 45585:
					return FromId("ICC_834h");
				case 47631:
					return FromId("GIL_504h");
				case 48145:
					return FromId("BOT_238p");
				default:
					// HsServer: any other hero's power by its DBF id (Galakrond, client-only heroes)
					return FromAssetId(assetId) ?? throw new NotImplementedException($"no hero power with asset id {assetId}");
			}
		}

		/// <summary>
		/// Retrieves the specified set of cards, sorted by <see cref="GameTag.COST"/>.
		/// </summary>
		/// <param name="formatType"></param>
		/// <returns></returns>
		public static Dictionary<int, List<Card>> CostMinionCards(FormatType formatType) => formatType == FormatType.FT_STANDARD ? StandardCostMinionCards : WildCostMinionCards;

		private static readonly Dictionary<int, List<Card>> StandardCostMinionCards;
		private static readonly Dictionary<int, List<Card>> WildCostMinionCards;

		/// <summary>HsServer: the card a random pick must not be a copy of: the card whose effect it is (an enchantment's
		/// creator). Wiki Discover: "Cards that Discover random cards cannot Discover copies of themselves"; Generate: "Cards
		/// that 'generate a random card' can no longer generate themselves".</summary>
		public static Card SelfOf(IEntity source) =>
			source is Enchantment e && e.Creator != null ? e.Creator.Card : source?.Card;

		/// <summary>HsServer: the card whose effect is running now (the task queue's current source), or null. A card never
		/// generates itself (user rule 2026-10-05: "cards cannot actually generate themselves": a spell that Discovers spells
		/// can't make itself, a minion that makes random minions can't make itself); random picks leave it out.</summary>
		/// <summary>HsServer: whether <paramref name="card"/> can be Discovered by <paramref name="c"/>. A card of a deck-gated
		/// family (Galakrond / Invoke, Herald, Imbue: HsTag.DeckGatedFamilies) only when the player's deck list has a card of
		/// that family (user rule 2026-10-05: "cannot be discovered mid game unless u already have at least 1 of that type of
		/// card in your deck"; "u either put imbue card or its payoff in the deck or u cant discover any"; "it applies both
		/// random gen and discover"; "in deck just means ur starting deck").</summary>
		public static bool DeckAllows(Controller c, Card card) =>
			card.DeckGatedFamilies == 0 || c != null && (card.DeckGatedFamilies & c.DeckOpensFamilies) != 0;

		/// <summary>HsServer: the cards of the format that can be generated or Discovered for <paramref name="c"/> (the
		/// generating player): FormatTypeCards without the deck-gated families their starting deck doesn't open
		/// (DeckAllows). Every random / Discover pool in scripts starts here.</summary>
		public static IEnumerable<Card> GenerationPool(Game g, Controller c, FormatType? format = null) =>
			FormatTypeCards(format ?? g.FormatType).Where(card => DeckAllows(c, card));

		/// <summary>HsServer: the player whose effect is running now (the generating player for a helper that has no
		/// player of its own), or null.</summary>
		public static Controller GeneratingPlayer(Game game) => game?.TaskQueue.CurrentSource?.Controller;

		/// <summary>HsServer: the card a deck card starts the game as on <paramref name="board"/> (Power.BoardVersion), else itself.</summary>
		public static Card ForBoard(Card card, int board) =>
			card?.Power?.BoardVersion?.Invoke(board) is string id && TryFromId(id) is Card version ? version : card;

		public static Card GeneratingCard(Game game) => SelfOf(game?.TaskQueue.CurrentSource);

		/// <summary>HsServer: every collectible card of the format, the cards that are never randomly generated included
		/// (Quests, Titans...): for an effect that names them itself ("Get a random Titan").</summary>
		public static IEnumerable<Card> FormatTypeCollectible(FormatType formatType) =>
			All.Where(c => c.Collectible && !IsHeroPortrait(c) && !IsClassicFormatCopy(c) && (formatType != FormatType.FT_STANDARD || StandardSets.Contains(c.Set)));

		/// <summary>HsServer (SPACE pass 2026-10-07): a hero portrait (the class heroes and their skins: collectible HERO cards with no
		/// Cost) — not a card. Playable Hero cards (Jim Raynor, the Death Knights, Galakrond…) cost Mana and are cards like any other:
		/// upstream left every HERO-type card out of the pools, so 809 hero cards — 17 of them unscripted — were never generated
		/// and never listed by the coverage tool (rule: never exclude cards).</summary>
		public static bool IsHeroPortrait(Card c) => c.Type == CardType.HERO && c.Cost == 0;

		/// <summary>HsServer: a Classic-format copy of an original card (client CARD_SET VANILLA = 1646, ids VAN_…): played only in the
		/// Classic format; the Standard / Wild pools hold the real version.</summary>
		public static bool IsClassicFormatCopy(Card c) => (int)c.Set == 1646;

		/// <summary>HsServer: <see cref="CostMinionCards"/> for a random pick that goes onto the battlefield (summoned, or
		/// transformed into there): without <see cref="Card.NeverRandomlyOnBoard"/> (Colossals).</summary>
		public static Dictionary<int, List<Card>> BoardCostMinionCards(FormatType formatType) =>
			formatType == FormatType.FT_STANDARD ? StandardBoardCostMinionCards : WildBoardCostMinionCards;

		private static readonly Dictionary<int, List<Card>> StandardBoardCostMinionCards;
		private static readonly Dictionary<int, List<Card>> WildBoardCostMinionCards;

		/// <summary>HsServer: the pool a random pick for <paramref name="destination"/> comes from: the board leaves out
		/// <see cref="Card.NeverRandomlyOnBoard"/> (Colossals); hand and deck keep them (wiki Colossal).</summary>
		public static IReadOnlyList<Card> ForDestination(IReadOnlyList<Card> pool, IPlayable destination) =>
			destination?.Zone?.Type == Zone.PLAY ? pool.Where(c => !c.NeverRandomlyOnBoard).ToList() : pool;

		/// <summary>
		/// HsServer: the minion a cost-based random transform ("evolve" +N, "devolve" -N, "revolve" 0) picks, by the wiki's
		/// Transform rules: "Transforming based on cost will always re-roll the minion into a new one (may also roll into the
		/// same minion)"; "The bottom floor ... is 0 mana"; "minions that cost 10 or less mana cannot be evolved into minions
		/// that cost 11 or more mana" (patch 32.0); "minions that cost 11 or more mana can only transform into minions that
		/// cost the same amount as them"; "if a minion that costs 11 or more mana is devolved, it will keep devolving until a
		/// minion is found" (32.2). The cost is the card's (Evolve: "base mana cost"). The board pool when the minion is in
		/// play. No minion at the cost (a gap in the pool): ours keeps going down, as the wiki's devolve rule (UNCLEAR live
		/// for an evolve). Only cost-based random transforms: "Cards that transform minions into
		/// specific cards ... do not abide by these rules".
		/// </summary>
		public static Card EvolvePick(FormatType formatType, IPlayable minion, int costChange, Util.DeepCloneableRandom random,
			Controller generator = null)
		{
			return EvolvePickFromCost(formatType, minion.Card.Cost, costChange, random, generator ?? GeneratingPlayer(minion?.Game),
				minion?.Zone?.Type == Zone.PLAY);
		}

		/// <summary>The evolve rules from a given <paramref name="cost"/> (Plucky Podling: the cost of the minion it would have
		/// become); <paramref name="board"/>: the board pool.</summary>
		public static Card EvolvePickFromCost(FormatType formatType, int cost, int costChange, Util.DeepCloneableRandom random,
			Controller generator, bool board)
		{
			Dictionary<int, List<Card>> pool = board ? BoardCostMinionCards(formatType) : CostMinionCards(formatType);
			int wanted = cost >= 11 && costChange >= 0 ? cost
				: cost <= 10 && cost + costChange > 10 ? 10
				: Math.Max(0, cost + costChange);
			for (int c = wanted; c >= 0; c--)
				if (pool.TryGetValue(c, out List<Card> all) && all.Where(x => DeckAllows(generator, x)).ToList() is { Count: > 0 } minions)
					return minions.RandomElement(random);
			return null;
		}


		/// <summary>
		/// Return a specific card from the given ID.
		/// </summary>
		/// <param name="cardId"></param>
		/// <returns></returns>
		/// <summary>HsServer: a card in <paramref name="heroClass"/>'s pool: its class (Card.HasClass, dual / tri-class included)
		/// or neutral. Without the client data (the upstream rules tests' 2021 XML) a NEUTRAL Gadgetzan tri-class card has no
		/// MULTIPLE_CLASSES: its faction's three classes. (Upstream let every faction card — the class ones too — into all
		/// classes' pools.)</summary>
		internal static bool InClassPool(Card c, CardClass heroClass)
		{
			if (c.HasClass(CardClass.NEUTRAL) && c[GameTag.MULTIPLE_CLASSES] == 0 && c.MultiClassGroup != 0)
				return c.MultiClassGroup switch
				{
					1 => heroClass == CardClass.HUNTER || heroClass == CardClass.PALADIN || heroClass == CardClass.WARRIOR,
					2 => heroClass == CardClass.DRUID || heroClass == CardClass.ROGUE || heroClass == CardClass.SHAMAN,
					3 => heroClass == CardClass.MAGE || heroClass == CardClass.PRIEST || heroClass == CardClass.WARLOCK,
					_ => false
				};
			return c.HasClass(heroClass) || c.HasClass(CardClass.NEUTRAL);
		}

		public static Card FromId(string cardId)
		{
			return Data.Cards[cardId];
		}

		/// <summary>HsServer: the card, or null when the id is unknown.</summary>
		public static Card TryFromId(string cardId) => Data.Cards.TryGetValue(cardId, out Card card) ? card : null;

		/// <summary>
		/// HsServer: all Choose One options of a card: &lt;id&gt;a / &lt;id&gt;b (or the known equivalents), else the client's related
		/// cards for it (RelatedCards: Wardruid Loti's four forms). Null when unknown (the card plays as a plain card).
		/// </summary>
		public static Card[] ChooseOneOptions(Card card)
		{
			Card a = ChooseOneOption(card, 'a'), b = ChooseOneOption(card, 'b');
			if (a != null && b != null)
			{
				// HsServer: in the text's order — the client's related cards list them that way (Ancient of War: "+5 Attack" =
				// EX1_178b first; Power of the Wild, Starfall too); the a/b names don't (user 2026-10-04)
				var order = Loader.ReprintCards.RelatedCardIds(card.Id)?.ToList();
				int ia = order?.IndexOf(a.Id) ?? -1, ib = order?.IndexOf(b.Id) ?? -1;
				return ia >= 0 && ib >= 0 && ib < ia ? new[] { b, a } : new[] { a, b };
			}
			// a Twinspell copy (<id>ts, live: Rising Winds' YOD_001ts) has its original's options
			if (card.Id.EndsWith("ts", StringComparison.Ordinal) && TryFromId(card.Id[..^2]) is { } original && TwinspellCopy(original)?.Id == card.Id)
				return ChooseOneOptions(original);
			// HsServer (WHIZBANG): a Mini / Gigantic has its original's options (live vfx 154235: TOY_801t offers TOY_801b, TOY_801a)
			if ((card[(GameTag)3319] > 0 || card[(GameTag)3400] > 0) && ReplacementOriginal(card) is Card big && big.Id != card.Id)
				return ChooseOneOptions(big);
			Card[] related = Loader.ReprintCards.RelatedCardIds(card.Id)?.Select(TryFromId).Where(c => c != null).ToArray();
			return related != null && related.Length >= 2 ? related : null;
		}

		private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Card[]> _titanAbilities =
			new System.Collections.Concurrent.ConcurrentDictionary<string, Card[]>();

		/// <summary>
		/// HsServer (TITANS): a Titan's three abilities, in ability order: the SPELLs among the client's RelatedCards of a card
		/// with its own TITAN keyword (2772), in the RelatedCards order — not the id order: live Golganneth created its abilities
		/// as t2, t3, t and its 2nd (TTN_800t3) set TITAN_ABILITY_USED_2 (live-20261007-vfx-decks 67873-67903, 71840). Other
		/// related cards (Eonar's Ancient, Aggramar's weapon, Sargeras' portal) aren't abilities. Empty for any other card.
		/// </summary>
		public static Card[] TitanAbilities(Card card)
		{
			if (card == null || card[(GameTag)2772] <= 0 && card[(GameTag)4754] <= 0 || card.Type != CardType.MINION) return System.Array.Empty<Card>(); // (TITAN or OLD_GOD)
			return _titanAbilities.GetOrAdd(card.Id, _ =>
				Loader.ReprintCards.RelatedCardIds(card.Id)?.Select(TryFromId).Where(c => c != null && c.Type == CardType.SPELL).ToArray()
				?? System.Array.Empty<Card>());
		}

		/// <summary>HsServer (TITANS): a card "from the past" — from a set that is no longer in Standard now (False Disciple; Toki:
		/// "any card set that has become exclusive to Wild format"): Loader.ReprintCards.InStandardNow (the server's calendar, else
		/// the client data's rotation) — not the engine's 2019 StandardSets. Pools of such cards come from the Wild cards whatever
		/// the game's format (Cards.AllWild). Unpopular Has-Been, Tear Reality, False Disciple.</summary>
		/// HsServer (user 2026-10-07): "everything in Wild is technically considered in the past, excluding of course wild cards
		/// that got rotated in through either the core set or otherwise" — a Wild card with a collectible same-name reprint in a
		/// Standard set now (CORE_EX1_298 for EX1_298) is not from the past.
		public static bool FromThePast(Card card) => card != null && !Loader.ReprintCards.InStandardNow((int)card.Set)
			&& !StandardNowNames().Contains(card.Name ?? "");

		private static HashSet<string> _standardNowNames;

		/// <summary>The names of the collectible cards in a Standard set now (a reprint shares its original's name).</summary>
		private static HashSet<string> StandardNowNames() => _standardNowNames ??=
			All.Where(c => c.Collectible && Loader.ReprintCards.InStandardNow((int)c.Set)).Select(c => c.Name ?? "").ToHashSet();

		private static Dictionary<int, List<int>> _onPlayReplacements;
		/// <summary>HsServer: the client's ReplacementsWhenPlayed table (PrimaryCardId → SpawnWhenPlayedCardId, dbf ids):
		/// Twinspell, Drink spells, Miniaturize, Gigantify, Bunch of Bananas... Empty when the export lacks its fields.</summary>
		private static Dictionary<int, List<int>> OnPlayReplacements
		{
			get
			{
				if (_onPlayReplacements != null) return _onPlayReplacements;
				var map = new Dictionary<int, List<int>>(); // HsServer (WHIZBANG): every row (MIS_025: its Mini AND its Gigantic)
				string file = ReprintCards.DbfDirectory == null ? null : System.IO.Path.Combine(ReprintCards.DbfDirectory, "ReplacementsWhenPlayed.json");
				if (file != null && System.IO.File.Exists(file))
					using (var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllBytes(file)))
						foreach (System.Text.Json.JsonElement r in doc.RootElement.GetProperty("records").EnumerateArray())
							if (r.TryGetProperty("PrimaryCardId", out var from) && r.TryGetProperty("SpawnWhenPlayedCardId", out var to))
							{
								if (!map.TryGetValue(from.GetInt32(), out List<int> spawns)) map[from.GetInt32()] = spawns = new List<int>();
								spawns.Add(to.GetInt32());
							}
				return _onPlayReplacements = map;
			}
		}

		/// <summary>HsServer: the card a card leaves in its hand slot when played (an on-play replacement): the client's
		/// ReplacementsWhenPlayed table (a Drink spell's next drink, a Twinspell's copy, a Miniaturize card's mini...),
		/// else for a Twinspell its TWINSPELL_COPY / "&lt;id&gt;ts" (older client data). Null for other cards.</summary>
		public static Card OnPlayReplacement(Card card) => OnPlayReplacementsOf(card).FirstOrDefault();

		/// <summary>HsServer (WHIZBANG): every card a card leaves in its hand slot when played, in the table's order (The
		/// Replicator-inator: its Mini and its Gigantic).</summary>
		public static List<Card> OnPlayReplacementsOf(Card card)
		{
			if (card == null) return new List<Card>();
			if (OnPlayReplacements.TryGetValue(card.AssetId, out List<int> spawns))
				return spawns.Select(FromAssetId).Where(x => x != null).ToList();
			return TwinspellCopy(card) is Card copy ? new List<Card> { copy } : new List<Card>();
		}

		/// <summary>HsServer (WHIZBANG): the card whose on-play replacement this card is (a Mini / Gigantic's original), or null.</summary>
		public static Card ReplacementOriginal(Card card) =>
			card == null ? null : OnPlayReplacements.Where(kv => kv.Value.Contains(card.AssetId)).Select(kv => FromAssetId(kv.Key)).FirstOrDefault();

		/// <summary>HsServer (WHIZBANG): the raw table rows (dbf ids), for the loader (a Mini uses its original's script).</summary>
		internal static IEnumerable<(int from, int to)> OnPlayReplacementRows() =>
			OnPlayReplacements.SelectMany(kv => kv.Value.Select(to => (kv.Key, to)));

		/// <summary>HsServer: the card a Twinspell card leaves in its hand slot when played: TWINSPELL_COPY, else the client's
		/// "&lt;id&gt;ts" (the same spell without Twinspell). Null for other cards.</summary>
		public static Card TwinspellCopy(Card card) =>
			card == null || !card.TwinSpell ? null
			: card[GameTag.TWINSPELL_COPY] > 0 ? FromAssetId(card[GameTag.TWINSPELL_COPY]) : TryFromId(card.Id + "ts");

		/// <summary>
		/// HsServer: option card ('a' or 'b') of a Choose One card. Reprints reuse the original's options
		/// (the client has EX1_160a/b but no CORE_EX1_160a/b).
		/// </summary>
		public static Card ChooseOneOption(Card card, char option) =>
			// <id>a / <id>b only for cards named that way (Rising Winds' options are YOD_001b / YOD_001c: "YOD_001b" isn't its 'b')
			(TryFromId(card.Id + 'a') != null ? TryFromId(card.Id + option) : null)
			?? (TryFromId(Loader.ReprintCards.Canonical(card.Id) + 'a') != null ? TryFromId(Loader.ReprintCards.Canonical(card.Id) + option) : null)
			?? TryFromId(Loader.ReprintCards.ChooseOneOptionId(card.Id, option) ?? card.Id + option); // HsServer: null = no known options

		/// <summary>
		/// Returns the first collectible card that matches the provided name.
		/// 
		/// This string MUST be localized into the loaded language!
		/// </summary>
		/// <param name="cardName"></param>
		/// <returns></returns>
		public static Card FromName(string cardName)
		{
			return Data.Cards.FirstOrDefault(x => x.Value.Name == cardName && x.Value.Collectible).Value;
		}

		/// <summary>
		/// Returns the first card that matches the provided asset id.
		/// </summary>
		/// <param name="assetId"></param>
		/// <returns></returns>
		public static Card FromAssetId(int assetId)
		{
			return Data.Cards.Values.FirstOrDefault(x => x.AssetId == assetId);
		}

		#endregion

		/// <summary>
		/// Returns a string representation of the provided card set.
		/// </summary>
		/// <param name="cardSet"></param>
		/// <returns></returns>
		public static string CardSetToName(CardSet cardSet)
		{
			switch (cardSet)
			{
				case CardSet.CORE:
					return "Basic";
				case CardSet.EXPERT1:
					return "Classic";
				case CardSet.HOF:
					return "Hall of Fame";
				case CardSet.NAXX:
					return "Curse of Naxxramas";
				case CardSet.GVG:
					return "Goblins vs Gnomes";
				case CardSet.BRM:
					return "Blackrock Mountain";
				case CardSet.TGT:
					return "The Grand Tournament";
				case CardSet.LOE:
					return "The League of Explorers";
				case CardSet.OG:
					return "Whispers of the Old Gods";
				case CardSet.KARA:
					return "One Night in Karazhan";
				case CardSet.GANGS:
					return "Mean Streets of Gadgetzan";
				case CardSet.UNGORO:
					return "Journey to Un\'Goro";
				case CardSet.ICECROWN:
					return "Knights of the Frozen Throne";
				case CardSet.LOOTAPALOOZA:
					return "Kobolds and Catacombs";
				case CardSet.GILNEAS:
					return "The Witchwood";
				case CardSet.BOOMSDAY:
					return "The Boomsday Project";
				case CardSet.TROLL:
					return "Rastakhan\'s Rumble";
				case CardSet.DALARAN:
					return "Rise of Shadows";
				case CardSet.ULDUM:
					return "Saviors of Uldum";
				case CardSet.DRAGONS:
					return "Descent of Dragons";
				default:
					throw new ArgumentOutOfRangeException(nameof(cardSet), cardSet, null);
			}
		}

		/// <summary>
		/// Returns a string with the card implementation statistics.
		/// </summary>
		/// <returns></returns>
		public static string Statistics()
		{
			IEnumerable<Card> standard = All.Where(c => c.Collectible && StandardSets.Contains(c.Set));
			IEnumerable<Card> wild = All.Where(c => c.Collectible);
			var implemented = standard.Where(p => p.Implemented)
				.GroupBy(p => p.Set)
				.Select(t => new { Key = t.Key, Count = t.Count() });
			var implementedWild = wild.Where(p => p.Implemented)
				.GroupBy(p => p.Set)
				.Select(t => new { Key = t.Key, Count = t.Count() });
			var all = standard
				.GroupBy(p => p.Set)
				.Select(t => new { Key = t.Key, Count = t.Count() });
			var allWild = wild
				.GroupBy(p => p.Set)
				.Select(t => new { Key = t.Key, Count = t.Count() });

			var str = new StringBuilder();

			int totImpl = 0;
			int totCards = 0;
			foreach (CardSet set in StandardSets)
			{
				int impl = implemented.FirstOrDefault(p => p.Key == set)?.Count ?? 0;
				totImpl += impl;
				int tot = all.FirstOrDefault(p => p.Key == set).Count;
				str.AppendLine($"{CardSetToName(set)} => {impl * 100 / tot}% from {tot} Cards");
				totCards += tot;
			}

			str.AppendLine($"Total Standard => {totImpl * 100 / totCards}% from {totCards} Cards");
			str.AppendLine();

			totImpl = 0;
			totCards = 0;
			foreach (CardSet set in WildSets)
			{
				int impl = implementedWild.FirstOrDefault(p => p.Key == set)?.Count ?? 0;
				totImpl += impl;
				int tot = allWild.FirstOrDefault(p => p.Key == set).Count;
				str.AppendLine($"{CardSetToName(set)} => {impl * 100 / tot}% from {tot} Cards");
				totCards += tot;
			}

			str.AppendLine($"Total Wild => {totImpl * 100 / totCards}% from {totCards} Cards");

			IEnumerable<IGrouping<CardSet, Card>> notImplementedStandard = standard
				.Where(c => !c.Implemented)
				.GroupBy(c => c.Set);

			str.AppendLine("### Not yet implemented standard cards");
			foreach (IGrouping<CardSet, Card> group in notImplementedStandard)
			{
				str.AppendLine($"#### {CardSetToName(group.Key)}");
				foreach (Card c in group)
				{
					str.AppendLine($"- [{c.Id}] {c.Name}");
				}
			}

			str.AppendLine();

			IEnumerable<IGrouping<CardSet, Card>> notImplementedWild = wild
				.Where(c => !c.Implemented)
				.GroupBy(c => c.Set)
				.Where(c => WildSets.Contains(c.Key) && !StandardSets.Contains(c.Key));

			str.AppendLine("### Not yet implemented wild cards");
			foreach (IGrouping<CardSet, Card> group in notImplementedWild)
			{
				str.AppendLine($"#### {CardSetToName(group.Key)}");
				foreach (Card c in group)
				{
					str.AppendLine($"- [{c.Id}] {c.Name}");
				}
			}

			return str.ToString();
		}
	}
}
