// HsServer addition (not upstream SabberStone): reprints from the current client's card database.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;

namespace SabberStoneCore.Loader
{
	/// <summary>
	/// Adds the reprints the current client uses (Core set CORE_*, Legacy/Vanilla VAN_*, tutorial TUTR_* / TU5_*) on top of
	/// SabberStone's 2021 card data. A reprint takes its tags (cost, stats, keywords, set...) from the client's DBF export
	/// and borrows the original card's play requirements, entourage and script — but only when its card text matches the
	/// original's, so a reworded reprint never silently plays the old effect.
	/// Set <see cref="DbfDirectory"/> (Card.json + CardTag.json) before the first use of <see cref="Cards"/>.
	/// </summary>
	public static class ReprintCards
	{
		public static string DbfDirectory { get; set; }

		public static readonly string[] Prefixes = { "CORE_", "Core_", "VAN_", "TUTR_", "TU5_" };

		/// <summary>
		/// More card ids to add from the client DB when the engine has no 2021 original (set by the server: current Core
		/// cards under their own ids, e.g. CS3_, RLK_). CORE_ ids are always added.
		/// </summary>
		public static ISet<string> ExtraCardIds { get; set; } = new HashSet<string>();


		/// <summary>Reprint card id → original card id, for every reprint that was added.</summary>
		public static IReadOnlyDictionary<string, string> CanonicalOf => _canonicalOf;
		private static readonly Dictionary<string, string> _canonicalOf = new Dictionary<string, string>();

		/// <summary>Reprints whose text differs from the original (not added; they need their own script).</summary>
		public static IReadOnlyList<string> Reworded => _reworded;

		/// <summary>Current Core cards the engine has only from the client DB (no 2021 original): see Build.</summary>
		public static IReadOnlyCollection<string> ClientOnly => _clientOnly;

		/// <summary>Current Core cards of a type the engine has no entity for (Locations): not added.</summary>
		public static IReadOnlyCollection<string> Unsupported => _unsupported;
		private static readonly HashSet<string> _unsupported = new HashSet<string>();

		/// <summary>Card types the engine can create entities for.</summary>
		public static bool Playable(int cardType) =>
			cardType == (int)CardType.MINION || cardType == (int)CardType.SPELL || cardType == (int)CardType.WEAPON ||
			cardType == (int)CardType.HERO || cardType == (int)CardType.HERO_POWER || cardType == (int)CardType.LOCATION;
		private static readonly HashSet<string> _clientOnly = new HashSet<string>();

		/// <summary>
		/// Text that is only keywords the engine applies from tags ("Taunt. Divine Shield"): such a card needs no script.
		/// </summary>
		public static bool IsKeywordOnly(string text)
		{
			string rest = Regex.Replace(Normalize(text), @"taunt|rush|charge|divineshield|stealth|mega-?windfury|windfury|poisonous|lifesteal|reborn|tradeable|elusive|spelldamage\+\d+|overload:?\(\d+\)|dormantfor\d+turns?|can'tbetargetedbyspellsorheropowers|destroyanyminiondamagedbythisminion|prepare|"
				+ KeywordRulesFromTags + @"|[.,]", ""); // Prepare: its tag (VIOLET_HOLD) // Dormant: BoardZone/Card.DormantTurns; Vanilla Poisonous: Card.DestroysWhatItDamages
			return rest.Length == 0;
		}

		/// <summary>
		/// HsServer (token pass 2026-10-09): more card text that is a rule the engine applies from the card's tags (normalized text, see
		/// IsKeywordOnly), found on tokens the coverage tool never checked: "Freeze any character damaged by this minion" (client FREEZE:
		/// Attack.cs freezes what the attacker damages — Snowman AV_282t, Ice Shard YOD_029t, Sindragosa's Wing NX2_037t), "Immune while
		/// attacking" as a minion's own text (client IMMUNE_WHILE_ATTACKING: Attack.ImmuneWhileAttacking — Tavish's Ram SW_458t,
		/// Neptulon's Hand TID_712t; a weapon's "Your hero is Immune while attacking" is its IMMUNE_WHILE_ATTACKING too: ClientTags),
		/// "Can't attack." (CANT_ATTACK from the text: ClientTags — Night Terror EDR_490t, Portable Ice Wall LOOTA_826), "Doesn't
		/// leave a Corpse." (client tag 2217: Game.cs — the Risen RLK_008t…), Magnetic (MODULAR: PlayCard / Generic.Magnetize — the
		/// Sparkbots TTN_719t*), Echo (ECHO: Playable) and Mega-Windfury written with its hyphen (MEGA_WINDFURY: Attack.AttacksPerTurn —
		/// V-07-TR-0N GVG_111t, Vorpal Dagger LOOTA_821; the old pattern only matched "megawindfury"), and the state labels "Corrupted" / "Infused" a
		/// card's upgraded version shows (Carousel Gryphon's DMF_064t "Corrupted Divine Shield, Taunt", Murlocula's REV_957t "Infused
		/// Lifesteal": the version is the card the Corrupt / Infuse rule turns it into; the label is no effect).
		/// </summary>
		private const string KeywordRulesFromTags =
			@"freezeanycharacterdamagedbythisminion|(yourherois)?immunewhileattacking|can['’]tattack|doesn['’]tleaveacorpse|magnetic|echo|corrupted|infused";

		/// <summary>
		/// Cards (and reprints) whose current client text differs from the text SabberStone's script was written for —
		/// Blizzard changed them since (e.g. Fire Plume Phoenix now deals 3, the script deals 2). Their scripts need updating.
		/// </summary>
		public static IReadOnlyCollection<string> Stale => _stale.Where(id => !Reviewed.ContainsKey(Canonical(id))).ToList();

		/// <summary>
		/// Cards whose current version was checked against the script (HsServer, 2026-09-29): script updated (see the
		/// "HsServer: current text" comments), current stats/keywords taken from the client, or the new text is only
		/// reworded. Keyed by SabberStone id; their reprints count as reviewed too.
		/// </summary>
		public static readonly IReadOnlyDictionary<string, string> Reviewed = new Dictionary<string, string>
		{
			["CS1_112"] = "script: enemy minions only", ["CS1_130"] = "script: 3 to a minion", ["CS2_009"] = "script: +2/+3",
			["CS2_032"] = "script: 5", ["CS2_042"] = "script: 4", ["CS2_089"] = "script: 8 to your hero",
			["CS2_097"] = "script: 3", ["CS2_103"] = "script: +2 Attack and Charge", ["DS1_184"] = "script: Discover from deck",
			["EX1_084"] = "script: Charge on summon", ["EX1_134"] = "script: 3", ["EX1_259"] = "script: 3 (overload from tag)",
			["EX1_320"] = "script: 3", ["EX1_365"] = "script: a minion", ["EX1_572"] = "script: two Dream cards",
			["EX1_609"] = "script: 6", ["EX1_614"] = "script: after you play", ["EX1_625"] = "script: no upgrade",
			["NEW1_003"] = "script: friendly Demon", ["NEW1_008"] = "script: 2 cards / 7 Health",
			["CS2_088"] = "tags: Taunt", ["EX1_089"] = "tags: Charge", ["EX1_110"] = "tags: Taunt, Baine 5/5",
			["EX1_165"] = "tags: forms 7/6 Rush, 4/9 Taunt", ["EX1_248"] = "tags: Overload 1", ["EX1_250"] = "tags: Overload 2",
			["EX1_577"] = "token renamed (same id)", ["EX1_302"] = "reworded", ["EX1_085"] = "script: the player picks (no random)", ["NEW1_023"] = "Elusive = same",
			["EX1_312"] = "script: minions and locations", ["EX1_145"] = "script: (2) less", ["EX1_182"] = "script: another class", ["EX1_608"] = "script: not less than 1",
			["EX1_258"] = "script: after you play",
			// Core reprints from other sets (2026-09-30)
			["AT_011"] = "script: Overheal", ["AT_021"] = "script: +2/+1", ["AT_075"] = "script: +2 Attack and Taunt",
			["CFM_604"] = "script: + draw", ["CFM_606"] = "script: Overheal", ["CFM_781"] = "script: another class",
			["DAL_721"] = "script: Undead only", ["DRG_403"] = "script: next Hero Power +2 (was unscripted)",
			["GIL_653"] = "script: any friendly minion", ["GIL_663"] = "script: two", ["GIL_667"] = "script: 6",
			["GIL_836"] = "script: costs 1 less", ["ICC_055"] = "script: 3", ["KAR_036"] = "script: after you cast",
			["KAR_069"] = "script: another class", ["KAR_076"] = "script: 6 / 6-Cost", ["UNG_034"] = "script: not less than 1",
			["UNG_084"] = "script: 3", ["UNG_817"] = "script: Lifesteal 5", ["UNG_833"] = "script: two lowest-Cost",
			["AT_053"] = "tags: Overload 1", ["TRL_900"] = "tags: Rush", ["AT_061"] = "reworded", ["AT_062"] = "reworded (already Webspinners)",
			["BOT_568"] = "Temporary = discarded at end of turn", ["CFM_605"] = "reworded (copies already)", ["DRG_079"] = "Elusive = same",
			["EX1_620"] = "reworded (missing Health)", ["EX1_002"] = "Tradeable = client tag", ["EX1_005"] = "Tradeable = client tag",
			["EX1_323"] = "Hero card now (script: equip Blood Fury)", ["FP1_011"] = "reworded", ["GVG_021"] = "reworded", ["GVG_061"] = "reworded",
			["ICC_314"] = "same card pool, renamed", ["ICC_419"] = "Elusive = same", ["ICC_854"] = "same card pool, renamed",
			["KAR_061"] = "reworded", ["LOE_012"] = "reworded", ["LOOT_309"] = "reworded (Recruit)", ["OG_273"] = "reworded",
			["OG_330"] = "reworded", ["TRL_339"] = "reworded (fixed 2026-09-29)", ["UNG_020"] = "reworded",
			["LOOT_193"] = "approximate: untargetable by the opponent (current: Elusive on the opponent's turn)",
			// Caverns of Time originals checked against their 27.2 text (WONDERS 2026-10-07; the WON_ copies play them: CountsAsCopyOf)
			["LOE_105"] = "reworded (Get)", ["OG_090"] = "reworded (Get)", ["GVG_007"] = "script: except Mechs (Rush = tag)",
			["AT_006"] = "tags: Spell Damage +1", ["OG_188"] = "tags: Taunt (ritual check = ideal C'Thun)", ["OG_321"] = "tags: Taunt, cost 4",
			["OG_334"] = "tags: Taunt", ["CFM_816"] = "enchantment from the client text (+3/+3)", ["AT_028"] = "enchantment from the client text (+4)",
			// HsServer (WONDERS, 2026-10-07): originals rewritten to their current text for their Caverns of Time copies
			// (WondersCardsGenHs2-5.cs; the WON_ copy plays them: CountsAsCopyOf).
			["AT_001"] = "script: current text (WONDERS)", ["AT_007"] = "script: current text (WONDERS)", ["AT_007e"] = "script: current text (WONDERS)",
			["AT_012"] = "script: current text (WONDERS)", ["AT_015"] = "script: current text (WONDERS)", ["AT_018"] = "script: current text (WONDERS)",
			["AT_024"] = "script: current text (WONDERS)", ["AT_024e"] = "script: current text (WONDERS)", ["AT_033"] = "script: current text (WONDERS)",
			["AT_036"] = "script: current text (WONDERS)", ["AT_036t"] = "script: current text (WONDERS)", ["AT_041"] = "script: current text (WONDERS)",
			["AT_048"] = "script: current text (WONDERS)", ["AT_049"] = "script: current text (WONDERS)", ["AT_049e"] = "script: current text (WONDERS)",
			["AT_063"] = "script: current text (WONDERS)", ["AT_063t"] = "script: current text (WONDERS)", ["AT_132_SHAMANe"] = "script: current text (WONDERS)",
			["BRM_034"] = "script: current text (WONDERS)", ["CFM_643"] = "script: current text (WONDERS)", ["CFM_690"] = "script: current text (WONDERS)",
			["CFM_707"] = "script: current text (WONDERS)", ["CFM_752"] = "script: current text (WONDERS)", ["CFM_752e"] = "script: current text (WONDERS)",
			["CS2_058"] = "script: current text (WONDERS)", ["CS2_058e"] = "script: current text (WONDERS)", ["GVG_004"] = "script: current text (WONDERS)",
			["GVG_011"] = "script: current text (WONDERS)", ["GVG_011a"] = "script: current text (WONDERS)", ["GVG_015"] = "script: current text (WONDERS)",
			["GVG_035"] = "script: current text (WONDERS)", ["GVG_046"] = "script: current text (WONDERS)", ["GVG_056"] = "script: current text (WONDERS)",
			["GVG_123"] = "script: current text (WONDERS)", ["KAR_204"] = "script: current text (WONDERS)", ["LOE_006"] = "script: current text (WONDERS)",
			["LOE_105e"] = "script: current text (WONDERS)", ["OG_087"] = "script: current text (WONDERS)", ["OG_116"] = "script: current text (WONDERS)",
			["OG_121"] = "script: current text (WONDERS)", ["OG_121e2"] = "script: current text (WONDERS)", ["OG_209"] = "script: current text (WONDERS)",
			["OG_283"] = "script: current text (WONDERS)", ["OG_284"] = "script: current text (WONDERS)", ["OG_293"] = "script: current text (WONDERS)",
			["OG_302"] = "script: current text (WONDERS)", ["OG_311"] = "script: current text (WONDERS)", ["OG_311e"] = "script: current text (WONDERS)",
			["OG_313"] = "script: current text (WONDERS)",
		};
		private static readonly HashSet<string> _stale = new HashSet<string>();

		/// <summary>The client's card text (null if the client has no such card).</summary>
		public static string ClientText(string cardId) => _clientText.TryGetValue(cardId, out string t) ? t : null;

		/// <summary>Both cards exist in the client DB with the same text (markup and value placeholders aside).</summary>
		public static bool SameClientText(string a, string b) =>
			ClientText(a) is string ta && ClientText(b) is string tb && Normalize(ta) == Normalize(tb);
		private static readonly Dictionary<string, string> _clientText = new Dictionary<string, string>();
		private static readonly List<string> _reworded = new List<string>();

		/// <summary>
		/// Cards the client renamed rather than reprinted under a prefix: current id → SabberStone's id. The basic hero
		/// powers are the only Basic/Classic cards missing from the current client under their old ids.
		/// </summary>
		public static readonly IReadOnlyDictionary<string, string> Renamed = new Dictionary<string, string>
		{
			["HERO_01bp"] = "CS2_102",  // Armor Up!
			["HERO_02bp"] = "CS2_049",  // Totemic Call
			["HERO_03bp"] = "CS2_083b", // Dagger Mastery
			["HERO_04bp"] = "CS2_101",  // Reinforce
			["HERO_05bp"] = "DS1h_292", // Steady Shot
			["HERO_06bp"] = "CS2_017",  // Shapeshift
			["HERO_07bp"] = "CS2_056",  // Life Tap
			["HERO_08bp"] = "CS2_034",  // Fireblast
			["HERO_09bp"] = "CS1h_001", // Lesser Heal
		};

		/// <summary>
		/// Newer ids for old cards whose wording changed a little (checked by hand): current id → the 2021 card whose script
		/// they play with.
		/// </summary>
		public static readonly IReadOnlyDictionary<string, string> Aliases = new Dictionary<string, string>
		{
			["CORE_WON_351"] = "CFM_325", // Small-Time Buccaneer
			["CORE_WON_096"] = "LOE_023", // Dark Peddler
			["CORE_WON_350"] = "CFM_940", // I Know a Guy
		};

		/// <summary>Token cards HsServer scripts use that the engine lacks (added from the client DB).</summary>
		public static readonly ISet<string> Tokens = new HashSet<string>
		{
			"BAR_035t", // Swift Hyena (Wound Prey)
			"HERO_10bpe", "HERO_10pe2", // Demon Claws, Demon's Bite (Demon Hunter hero power)
			"HERO_11bpt", "HERO_11bp2t", // Frail Ghoul (Death Knight hero power)
			"RLK_008t", "RLK_061t", // Risen Ghoul (Army of the Dead), Risen Footman (Boneguard Commander)
			"GBL_999e", // Temporary: discarded at the end of your turn (Generic.MakeTemporary)
			"RLK_506t", // Risen Groom (Corpse Bride)
			// The Dark Gift enchantments (Into the Emerald Dream) and "Cost - 1" (Raptor Herald's Kindred discount)
			"EDR_100te", "EDR_100t1e", "EDR_100t2e", "EDR_100t3e", "EDR_100t5e", "EDR_100t6e", "EDR_100t7e", "EDR_100t8e",
			"EDR_100t9e", "EDR_100t13e", // the generic Cost enchantments (CurrentCoreCardsGen.GenericCostEnchantments):
			"GBL_001e", "GBL_002e", "GBL_003e", "GBL_004e", "GBL_005e", "GBL_006e", "GBL_007e", "GBL_008e", "GBL_009e", "GBL_010e",
			"GBL_011e", "GBL_012e", "GBL_013e",
			"CFM_940e", // Knows Another Guy (+1/+2 from I Know a Guy)
		};

		/// <summary>Collectible cards other cards create by name that the engine lacks (the Core Gifts' Temporary spells).</summary>
		public static readonly ISet<string> ReferencedCards = new HashSet<string> { "RLK_057", "RLK_015", "SW_040",
			"GDB_905" }; // HsServer (SPACE): the Starship launch card (client GameUtils.STARSHIP_LAUNCH_CARD_ID; no card relates to it)

		/// <summary>SabberStone card id → the current client's id for it (renamed cards that were added).</summary>
		public static IReadOnlyDictionary<string, string> CurrentOf => _currentOf;
		private static readonly Dictionary<string, string> _currentOf = new Dictionary<string, string>();

		/// <summary>The card the client knows: <paramref name="card"/>, or its current renamed version.</summary>
		public static Card Current(Card card) =>
			card != null && _currentOf.TryGetValue(card.Id, out string current) ? Cards.TryFromId(current) ?? card : card;

		/// <summary>The asset id of the SabberStone card a reprint / variant plays as (-1 if none): rules keyed on asset ids use it.</summary>
		public static int OriginalAssetId(string cardId) =>
			_canonicalOf.TryGetValue(cardId, out string c) && _baseAssetIds.TryGetValue(c, out int a) ? a : -1;
		private static readonly Dictionary<string, int> _baseAssetIds = new Dictionary<string, int>();

		/// <summary>
		/// Choose One options of a newer card whose options aren't named &lt;id&gt;a / &lt;id&gt;b: its two related non-collectible
		/// spell cards, in id order (Boomkin: ONY_018t, ONY_018t2; Flipper Friends: TSC_650a, TSC_650d). Null if not exactly two.
		/// </summary>
		public static string ChooseOneOptionId(string cardId, char option) => ChooseOneOptionId(cardId, option, 0);

		private static string ChooseOneOptionId(string cardId, char option, int depth)
		{
			if (!LoadClient() || depth > 2) return null;
			string b = Canonical(cardId);
			var byCardId = _dbfByCardId ??= dbf.ToDictionary(d => d.Value.CardId, d => d.Key);
			// HsServer: a reprint that counts as a copy of another card (client tag 858, Mire Keeper WON_302 = OG_202) uses
			// that card's options.
			if (byCardId.TryGetValue(cardId, out int dbfId) && Tag(tags, dbfId, (GameTag)858) is int copyOf && copyOf > 0
			    && dbf.TryGetValue(copyOf, out var original) && original.CardId != cardId)
			{
				string viaOriginal = byCardId.ContainsKey(original.CardId + option) ? original.CardId + option
					: ChooseOneOptionId(original.CardId, option, depth + 1);
				if (viaOriginal != null) return viaOriginal;
			}
			var options = dbf.Where(d => d.Value.CardId.StartsWith(b, StringComparison.Ordinal) && d.Value.CardId.Length > b.Length
										 && Tag(tags, d.Key, GameTag.COLLECTIBLE) != 1 && Tag(tags, d.Key, GameTag.CARDTYPE) == (int)CardType.SPELL)
				.Select(d => d.Value.CardId).OrderBy(id => id, StringComparer.Ordinal).ToList();
			if (options.Count == 2) return options[option == 'a' ? 0 : 1];
			// a hero power "<card>p" whose options are named after its hero card (Wildheart Guff's Nurture AV_205p: AV_205a / AV_205pb)
			if (options.Count < 2 && b.Length > 1 && b.EndsWith("p", StringComparison.Ordinal) && byCardId.TryGetValue(cardId, out int hp) &&
			    Tag(tags, hp, GameTag.CARDTYPE) == (int)CardType.HERO_POWER)
			{
				string hero = b[..^1];
				var heroOptions = dbf.Where(d => d.Value.CardId.StartsWith(hero, StringComparison.Ordinal) && d.Value.CardId.Length > hero.Length
				                                  && Tag(tags, d.Key, GameTag.COLLECTIBLE) != 1 && Tag(tags, d.Key, GameTag.CARDTYPE) == (int)CardType.SPELL)
					.Select(d => d.Value.CardId).OrderBy(id => id, StringComparer.Ordinal).ToList();
				if (heroOptions.Count == 2) return heroOptions[option == 'a' ? 0 : 1];
			}
			// Newer cards name their two options with one letter after the id (Rhythm and Roots: ETC_387b / ETC_387c).
			var lettered = options.Where(id => id.Length == b.Length + 1 && char.IsLower(id[b.Length])).ToList();
			return lettered.Count == 2 ? lettered[option == 'a' ? 0 : 1] : null;
		}
		private static Dictionary<string, int> _dbfByCardId;

		/// <summary>
		/// HsServer (WONDERS, 2026-10-07): the card the client says <paramref name="cardId"/> counts as a copy of (client tag 858,
		/// DECK_RULE_COUNT_AS_COPY_OF_CARD_ID in HearthDb: the Caverns of Time reprints WON_018 = EX1_609, WON_076t = AT_036t; Story,
		/// Uldum and Dalaran Heist adventure copies) when both have the same current client text — Blizzard updated the Wild
		/// originals with the reprints (27.2 patch notes: "If you already have the cards in your collection from their original
		/// release, they will be automatically updated"). Null otherwise (a reworded copy, VAN_EX1_609's "deal 4", needs its own
		/// script).
		/// </summary>
		public static string CountsAsCopyOf(string cardId)
		{
			if (cardId == null || !LoadClient() || !dbfByCardId.TryGetValue(cardId, out int dbfId)) return null;
			int copyOf = Tag(tags, dbfId, (GameTag)858);
			if (copyOf <= 0 || copyOf == dbfId || !dbf.TryGetValue(copyOf, out var original)) return null;
			return Normalize(dbf[dbfId].Text) == Normalize(original.Text) ? original.CardId : null;
		}

		/// <summary>HsServer (WONDERS): the engine card's script was written for an older text than the client's (Stale, not
		/// Reviewed): a "counts as a copy" reprint doesn't play it (CardContainer) until it is updated.</summary>
		public static bool IsStaleScript(string cardId) => cardId != null && _stale.Contains(cardId) && !Reviewed.ContainsKey(Canonical(cardId));

		/// <summary>HsServer (WONDERS): reprints made by the "counts as a copy" rule (tag 858), by id.</summary>
		public static bool IsCopyReprint(string cardId) => cardId != null && _copyReprints.Contains(cardId);
		private static readonly HashSet<string> _copyReprints = new HashSet<string>();

		/// <summary>
		/// HsServer (WONDERS): every id that plays the same card as <paramref name="cardId"/> — itself, the card it reprints
		/// (CanonicalOf) and the other reprints of that card — for recorded per-card data (SubSpells: a recording of WON_033
		/// serves GVG_123 and the other way round).
		/// </summary>
		public static IEnumerable<string> Variants(string cardId)
		{
			if (cardId == null) yield break;
			yield return cardId;
			string root = _canonicalOf.TryGetValue(cardId, out string o) ? o : cardId;
			if (root != cardId) yield return root;
			if (_variantsCount != _canonicalOf.Count)
			{
				var map = new Dictionary<string, List<string>>();
				foreach (var kv in _canonicalOf)
					(map.TryGetValue(kv.Value, out var l) ? l : map[kv.Value] = new List<string>()).Add(kv.Key);
				_variants = map;
				_variantsCount = _canonicalOf.Count;
			}
			if (_variants.TryGetValue(root, out var others))
				foreach (string other in others)
					if (other != cardId) yield return other;
		}
		private static Dictionary<string, List<string>> _variants = new Dictionary<string, List<string>>();
		private static int _variantsCount = -1;

		/// <summary>
		/// HsServer (WONDERS): the enchantment a reprint gives in place of its original's — "&lt;reprint id&gt;&lt;suffix&gt;" for the
		/// original's "&lt;original id&gt;&lt;suffix&gt;" when the client has that twin (same name and text: WON_009e = OG_313e
		/// "Addled", WON_344e = AT_007e "Spellslung"; loaded as a reprint of it in Build). Null when there is none (live: WON_023
		/// has no twin and attaches AT_061e).
		/// </summary>
		public static string TwinEnchantment(string creatorCardId, string enchantmentId)
		{
			if (creatorCardId == null || enchantmentId == null || !_canonicalOf.TryGetValue(creatorCardId, out string original)
			    || !enchantmentId.StartsWith(original, StringComparison.Ordinal) || enchantmentId.Length == original.Length)
				return null;
			string twin = creatorCardId + enchantmentId.Substring(original.Length);
			return _canonicalOf.TryGetValue(twin, out string twinOf) && twinOf == enchantmentId ? twin : null;
		}

		public static string Canonical(string cardId)
		{
			if (Renamed.TryGetValue(cardId, out string original)) return original;
			foreach (string p in Prefixes)
				if (cardId.StartsWith(p, StringComparison.Ordinal)) return cardId.Substring(p.Length);
			return cardId;
		}

		// The client's card database: dbf id → (card id, name, text); card id → dbf id; dbf id → its own tags.
		private static Dictionary<int, (string CardId, string Name, string Text)> dbf;
		private static Dictionary<string, int> dbfByCardId;
		private static Dictionary<int, List<(int Tag, int Value)>> tags;

		/// <summary>HsServer: the tags Blizzard's card data marks as abilities (IsPowerKeywordTag: Taunt, Poisonous,
		/// Deathrattle, the trigger icon, Rush…) — a card's "text". Silence removes all of them.</summary>
		public static IReadOnlyCollection<int> AbilityTags { get { LoadClient(); return _abilityTags; } }
		private static readonly HashSet<int> _abilityTags = new HashSet<int>();

		/// <summary>
		/// HsServer: the client's RelatedCards table (card → related cards, in table order): for a Choose One card whose
		/// options aren't named a/b, its options (Wardruid Loti: TRL_343at1, ct1, dt1, bt1). Null if none.
		/// </summary>
		public static IReadOnlyList<string> RelatedCardIds(string cardId)
		{
			if (!LoadClient() || !dbfByCardId.TryGetValue(cardId, out int dbfId)) return null;
			LoadRelated();
			return _related.TryGetValue(dbfId, out var rel)
				? rel.OrderBy(x => x.Order).Select(x => dbf.TryGetValue(x.Related, out var d) ? d.CardId : null).Where(id => id != null).ToList()
				: null;
		}

		/// <summary>HsServer: the dbf ids the client's RelatedCards table relates to any of <paramref name="cards"/>.</summary>
		private static HashSet<int> RelatedDbfIds(IEnumerable<int> cards)
		{
			LoadRelated();
			var result = new HashSet<int>();
			foreach (int card in cards)
				if (_related.TryGetValue(card, out var rel))
					foreach (var (_, related) in rel) result.Add(related);
			return result;
		}

		private static void LoadRelated()
		{
			if (_related == null)
			{
				_related = new Dictionary<int, List<(int Order, int Related)>>();
				string file = Path.Combine(DbfDirectory, "RelatedCards.json");
				if (File.Exists(file))
					using (JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(file)))
						foreach (JsonElement r in doc.RootElement.GetProperty("records").EnumerateArray())
						{
							if (!r.TryGetProperty("CardId", out JsonElement c) || !c.TryGetInt32(out int card)) continue;
							if (!r.TryGetProperty("RelatedCardDatabaseId", out JsonElement rc) || !rc.TryGetInt32(out int related)) continue;
							int order = r.TryGetProperty("ID", out JsonElement o) && o.TryGetInt32(out int oi) ? oi : 0;
							(_related.TryGetValue(card, out var list) ? list : _related[card] = new List<(int, int)>()).Add((order, related));
						}
			}
		}
		private static Dictionary<int, List<(int Order, int Related)>> _related;

		private static bool LoadClient()
		{
			if (dbf != null) return true;
			if (DbfDirectory == null || !File.Exists(Path.Combine(DbfDirectory, "Card.json"))) return false;
			dbf = new Dictionary<int, (string, string, string)>();
			dbfByCardId = new Dictionary<string, int>();
			using (JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DbfDirectory, "Card.json"))))
				foreach (JsonElement r in doc.RootElement.GetProperty("records").EnumerateArray())
				{
					if (!r.TryGetProperty("NoteMiniGuid", out JsonElement id)) continue;
					int dbfId = r.GetProperty("ID").GetInt32();
					string text = r.TryGetProperty("TextInHand", out JsonElement t) ? t.GetString() : "";
					string name = r.TryGetProperty("Name", out JsonElement n) ? n.GetString() : "";
					dbf[dbfId] = (id.GetString(), name, text);
					dbfByCardId[id.GetString()] = dbfId;
				}

			tags = new Dictionary<int, List<(int Tag, int Value)>>();
			var ownReferences = new List<(int Card, int Tag, int Value)>();
			var keywordReferences = new List<(int Card, int Tag, int Value)>();
			using (JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(DbfDirectory, "CardTag.json"))))
				foreach (JsonElement r in doc.RootElement.GetProperty("records").EnumerateArray())
				{
					// A reference tag usually only means the text mentions the keyword (for its tooltip): Glacial Shard has
					// FREEZE as a reference tag, Water Elemental as its own. But Blizzard also files some cards' own
					// keyword abilities as references (Zuramat's Prison, Skyvateer: DEATHRATTLE; live sends DEATHRATTLE=1 on
					// the entity) — the text tells them apart: an own ability is written "<b>Deathrattle:</b>", outside quotes.
					int card = r.GetProperty("CardId").GetInt32();
					int tagId = r.GetProperty("TagId").GetInt32();
					if (r.TryGetProperty("IsPowerKeywordTag", out JsonElement keyword) && keyword.GetBoolean())
						_abilityTags.Add(tagId);
					int value = r.TryGetProperty("TagValue", out JsonElement v) ? v.GetInt32() : 0;
					if (r.TryGetProperty("IsReferenceTag", out JsonElement reference) && reference.GetBoolean())
					{
						// HsServer (2026-10-06): a row that is both a reference and an ability (IsPowerKeywordTag) is the
						// card's own keyword that its text also mentions (Il'gynoth "Lifesteal. Your Lifesteal…", Halazzi,
						// Lotus Assassin, Carousel Gryphon; 661 such rows, all own keywords but 3 boss Hero Powers). A
						// reference alone is only a mention. Was: every reference row dropped (Il'gynoth had no Lifesteal).
						if (keyword.ValueKind == JsonValueKind.True) keywordReferences.Add((card, tagId, value));
						else if (OwnAbility(card, tagId)) ownReferences.Add((card, tagId, value));
						continue;
					}
					if (!tags.TryGetValue(card, out var list)) tags[card] = list = new List<(int, int)>();
					list.Add((tagId, value));
				}
			foreach (var (card, tagId, value) in keywordReferences)
			{
				if (!tags.TryGetValue(card, out var list)) tags[card] = list = new List<(int, int)>();
				bool spell = list.Any(t => t.Tag == (int)GameTag.CARDTYPE && t.Value == (int)CardType.SPELL);
				if (!list.Any(t => t.Tag == tagId) && !(spell && tagId == (int)GameTag.DEATHRATTLE))
					list.Add((tagId, value == 0 ? 1 : value));
			}
			// A spell's "Deathrattle:" text grants it (Adapt's Living Spores): only other cards own the ability.
			foreach (var (card, tagId, value) in ownReferences)
				if (tags.TryGetValue(card, out var list) && !list.Any(t => t.Tag == (int)GameTag.CARDTYPE && t.Value == (int)CardType.SPELL))
					list.Add((tagId, value == 0 ? 1 : value));
			return true;
		}

		/// <summary>Keyword abilities written "<b>Name:</b>" in a card's text, by tag.</summary>
		private static readonly Dictionary<int, string> AbilityKeywords = new Dictionary<int, string>
		{
			[(int)GameTag.DEATHRATTLE] = "Deathrattle", [(int)GameTag.BATTLECRY] = "Battlecry", [(int)GameTag.COMBO] = "Combo",
			[1637] = "Frenzy", [1333] = "Outcast", [1920] = "Honorable Kill", [1427] = "Spellburst", [(int)GameTag.INSPIRE] = "Inspire",
			[923] = "Overkill", [2820] = "Finale", [2905] = "Quickdraw", [2129] = "Avenge",
		};

		/// <summary>Whether the card's own text (quoted text grants the ability to something else) has this keyword ability.</summary>
		private static bool OwnAbility(int dbfId, int tag)
		{
			if (!AbilityKeywords.TryGetValue(tag, out string keyword) || !dbf.TryGetValue(dbfId, out var entry) || entry.Text == null)
				return false;
			string own = Regex.Replace(entry.Text, "\"[^\"]*\"", ""); // quoted text is granted to something else
			return Regex.IsMatch(own, "<b>" + keyword + @"( \(\d+\))?:</b>");
		}

		/// <summary>
		/// A reprint card: the client's own tags (current stats and keywords) on top of the original's other tags, the
		/// original's play requirements and entourage; its script is the original's (CanonicalOf).
		/// </summary>
		private static Card Reprint(int dbfId, (string CardId, string Name, string Text) entry, Card original, bool ownSet = false)
		{
			var cardTags = new List<Tag>();
			var seen = new HashSet<int>();
			foreach (var (tag, value) in ClientTags(dbfId))
				if (seen.Add(tag)) cardTags.Add(new Tag((GameTag)tag, value));
			// HsServer (WONDERS): a "counts as a copy" reprint is a card of its own set (Caverns of Time WON_x: set 1898, not the
			// original's GVG / TGT), from CardSetTiming. (2026-10-09) So is every reprint under its own id (CORE_TRL_240 is Core, not
			// Rastakhan's Rumble): the original's set only when it has none; the same card id keeps its 2021 set (below).
			if ((ownSet || entry.CardId != original.Id) && !seen.Contains((int)GameTag.CARD_SET) && SetOf(dbfId) is int ownCardSet && seen.Add((int)GameTag.CARD_SET))
				cardTags.Add(new Tag(GameTag.CARD_SET, ownCardSet));
			// (what a card is — type, class, collectible… — is the export's, 0 when it lists none, as in the merge: Holy Smite's
			// Puzzle Lab copy CS1_130_Puzzle has the 2021 card's text but is not collectible)
			foreach (KeyValuePair<GameTag, int> t in original.Tags)
				if (!CurrentTags.Contains(t.Key) && !IdentityTags.Contains(t.Key) && seen.Add((int)t.Key)) cardTags.Add(new Tag(t.Key, t.Value));
			cardTags.AddRange(ElusiveTags(cardTags).Where(t => seen.Add((int)t.GameTag)));
			// HsServer (2026-10-06): a card with no set (the export has no CARD_SET; 2021 data predates it) takes its
			// CardSetTiming always-on row (YOD_ 1403, BT_ 1414, SCH_ 1443, TSC_ 1658...). Cards the 2021 data knows keep its
			// set (Basic / Classic would otherwise move to Legacy). Was: every newer card's set INVALID.
			if (!seen.Contains((int)GameTag.CARD_SET) && SetOf(dbfId) is int set && seen.Add((int)GameTag.CARD_SET))
				cardTags.Add(new Tag(GameTag.CARD_SET, set));
			cardTags.Add(new Tag(GameTag.CARDNAME, entry.Name));
			cardTags.Add(new Tag(GameTag.CARDTEXT, entry.Text ?? ""));
			_canonicalOf[entry.CardId] = original.Id;
			return new Card(entry.CardId, dbfId, cardTags.ToArray(),
				original.PlayRequirements == null ? null : new Dictionary<PlayReq, int>(original.PlayRequirements),
				original.Entourage, Array.Empty<Tag>());
		}

		/// <summary>
		/// HsServer (WONDERS): <paramref name="entry"/> as a reprint of the card it counts as a copy of (CountsAsCopyOf): of the
		/// engine card (its play requirements, entourage and script), or — an original only the client has — the client's own
		/// tags with CanonicalOf naming it (CardContainer gives it that card's script and play requirements). Null if it is no
		/// such copy.
		/// </summary>
		private static Card CopyOf(int dbfId, (string CardId, string Name, string Text) entry, Dictionary<string, Card> byId)
		{
			string original = CountsAsCopyOf(entry.CardId);
			if (original == null) return null;
			// the original as the engine knows it (a renamed / prefixed reprint: its script's id)
			string engineId = byId.ContainsKey(original) ? original : _canonicalOf.TryGetValue(original, out string c) ? c : original;
			_copyReprints.Add(entry.CardId);
			if (byId.TryGetValue(engineId, out Card engineCard))
				return Reprint(dbfId, entry, engineCard, ownSet: true);
			_canonicalOf[entry.CardId] = engineId;
			return ClientOnlyCard(dbfId, entry);
		}

		/// <summary>The enchantment <paramref name="entry"/> is the twin of ("&lt;reprint&gt;&lt;suffix&gt;" = "&lt;original&gt;&lt;suffix&gt;",
		/// same name and text), or null.</summary>
		private static string TwinOf((string CardId, string Name, string Text) entry)
		{
			string id = entry.CardId;
			for (int len = id.Length - 1; len >= 4; len--)
			{
				if (!_canonicalOf.TryGetValue(id.Substring(0, len), out string original)) continue;
				string twin = original + id.Substring(len);
				if (twin != id && dbfByCardId.TryGetValue(twin, out int twinDbf) && dbf[twinDbf].Name == entry.Name
				    && Normalize(dbf[twinDbf].Text) == Normalize(entry.Text) && Tag(tags, twinDbf, GameTag.CARDTYPE) == (int)CardType.ENCHANTMENT)
					return twin;
				return null;
			}
			return null;
		}

		/// <summary>A card from the client's own tags only (no engine original).</summary>
		private static Card ClientOnlyCard(int dbfId, (string CardId, string Name, string Text) entry)
		{
			var own = ClientTags(dbfId).Select(t => new Tag((GameTag)t.Tag, t.Value)).ToList();
			own.AddRange(ElusiveTags(own));
			if (own.All(t => t.GameTag != GameTag.CARD_SET) && SetOf(dbfId) is int set) own.Add(new Tag(GameTag.CARD_SET, set));
			own.Add(new Tag(GameTag.CARDNAME, entry.Name));
			own.Add(new Tag(GameTag.CARDTEXT, entry.Text ?? ""));
			return new Card(entry.CardId, dbfId, own.ToArray(), new Dictionary<PlayReq, int>(), null, Array.Empty<Tag>());
		}

		/// <summary>
		/// HsServer (2026-10-07): a non-collectible client card a script defines (CardDefs by its id) that Build left out because
		/// it is named after no card and related to none — Cho'gall's Fortifications buff TTN_02t30e1 "Fortified" (the client's
		/// id lacks a 0 of TTN_002t30). A script naming the id is reason enough to load it (CardContainer.Load); before, such
		/// ids had to be listed by hand in <see cref="Tokens"/>. Null when the client lacks it, or it is collectible, a hero
		/// (skin) or a hero power (those have their own loading above) or a type the engine can't create.
		/// </summary>
		internal static Card ScriptedClientCard(string cardId)
		{
			if (!LoadClient() || !dbfByCardId.TryGetValue(cardId, out int dbfId) || !dbf.TryGetValue(dbfId, out var entry)) return null;
			if (Tag(tags, dbfId, GameTag.COLLECTIBLE) == 1) return null;
			int type = Tag(tags, dbfId, GameTag.CARDTYPE);
			if (!Playable(type) && type != (int)CardType.ENCHANTMENT) return null;
			if (type == (int)CardType.HERO_POWER || type == (int)CardType.HERO) return null;
			var own = ClientTags(dbfId).Select(t => new Tag((GameTag)t.Tag, t.Value)).ToList();
			own.AddRange(ElusiveTags(own));
			if (own.All(t => t.GameTag != GameTag.CARD_SET) && SetOf(dbfId) is int set) own.Add(new Tag(GameTag.CARD_SET, set));
			own.Add(new Tag(GameTag.CARDNAME, entry.Name));
			own.Add(new Tag(GameTag.CARDTEXT, entry.Text ?? ""));
			return new Card(entry.CardId, dbfId, own.ToArray(), new Dictionary<PlayReq, int>(), null, Array.Empty<Tag>());
		}

		/// <summary>The client's ELUSIVE keyword (1211) is the engine's two "can't be targeted" tags.</summary>
		private static IEnumerable<Tag> ElusiveTags(IEnumerable<Tag> tags) =>
			tags.Any(t => (int)t.GameTag == 1211 && t.TagValue > 0)
				? new[] { new Tag(GameTag.CANT_BE_TARGETED_BY_SPELLS, 1), new Tag(GameTag.CANT_BE_TARGETED_BY_HERO_POWERS, 1) }
				: Array.Empty<Tag>();

		private static IEnumerable<(int Tag, int Value)> ClientTags(int dbfId)
		{
			if (!tags.TryGetValue(dbfId, out var list)) return Enumerable.Empty<(int, int)>();
			var distinct = list.GroupBy(t => t.Tag).Select(g => g.First()).ToList();
			// Newer client weapons carry their durability as HEALTH only; the engine's weapons read DURABILITY.
			bool weapon = distinct.Any(t => t.Tag == (int)GameTag.CARDTYPE && t.Value == (int)CardType.WEAPON);
			if (weapon && distinct.All(t => t.Tag != (int)GameTag.DURABILITY))
				foreach (var (tag, value) in distinct.Where(t => t.Tag == (int)GameTag.HEALTH).ToList())
					distinct.Add(((int)GameTag.DURABILITY, value));
			// HsServer (2026-10-04): the client's SPELLPOWER is the keyword (always 1: Malygos, Aegwynn, Spellweaver...); the
			// amount is the card's own "Spell Damage +N" (26 collectible cards say more than +1). The entity carries the
			// amount (live GDB_310: SPELLPOWER 2). Text granted to something else (in quotes) doesn't count.
			int sp = distinct.FindIndex(t => t.Tag == (int)GameTag.SPELLPOWER && t.Value > 0);
			if (sp >= 0 && dbf.TryGetValue(dbfId, out var entry) && entry.Text != null &&
			    Regex.Match(Regex.Replace(entry.Text, "\"[^\"]*\"", ""), @"Spell Damage \+(\d+)") is { Success: true } m)
				distinct[sp] = ((int)GameTag.SPELLPOWER, int.Parse(m.Groups[1].Value));
			// HsServer (2026-10-06): the same for Overload — the client's OVERLOAD is the keyword (always 1: Lightning Bloom,
			// Earth Elemental, Runic Carvings); the amount is the card's own "Overload: (N)" ("Overload that player for (2)":
			// Disguised Detective). Live: Lightning Bloom / Earth Elemental owe 2.
			int ol = distinct.FindIndex(t => t.Tag == (int)GameTag.OVERLOAD && t.Value > 0);
			if (ol >= 0 && dbf.TryGetValue(dbfId, out var withOverload) && withOverload.Text != null &&
			    Regex.Match(withOverload.Text, @"Overload(?:</b>)?[^()]{0,30}\((\d+)\)") is { Success: true } o)
				distinct[ol] = ((int)GameTag.OVERLOAD, int.Parse(o.Groups[1].Value));
			// HsServer (2026-10-06): "Can't attack heroes." as a card's own text (Icehowl, Supreme Abyssal, Fool's Bane, Bladed
			// Gauntlet, Lightbringer's Hammer…): the client has no tag for it, the server sets CANNOT_ATTACK_HEROES. Not "It can't
			// attack heroes this turn" (an effect) and not quoted text (granted to something else).
			if (distinct.All(t => t.Tag != (int)GameTag.CANNOT_ATTACK_HEROES) && dbf.TryGetValue(dbfId, out var withRule) && withRule.Text != null &&
			    Regex.IsMatch(Regex.Replace(Regex.Replace(withRule.Text, "<[^>]+>", ""), "\"[^\"]*\"", ""), @"(^|[\s.\]_])Can['’]t attack heroes\."))
				distinct.Add(((int)GameTag.CANNOT_ATTACK_HEROES, 1));
			// HsServer (2026-10-06): "Can't attack." as a card's own text (Helmet Hermit, Imposing Anubisath, the Watch Posts,
			// Ragnaros…): the current client data has no CANT_ATTACK on any of them (only the 2021 data had it for the old ones).
			if (distinct.All(t => t.Tag != (int)GameTag.CANT_ATTACK) && dbf.TryGetValue(dbfId, out var withCant) && withCant.Text != null &&
			    Regex.IsMatch(Regex.Replace(Regex.Replace(withCant.Text, "<[^>]+>", ""), "\"[^\"]*\"", ""), @"(^|[\s.\]_])Can['’]t attack\.", RegexOptions.IgnoreCase))
				distinct.Add(((int)GameTag.CANT_ATTACK, 1)); // (any case: Portable Ice Wall LOOTA_826 says "Can't Attack.")
			// HsServer (token pass 2026-10-09): "Your hero is Immune while attacking." as a weapon's own text (Mirage Blade ULD_326t: the
			// client gives it no tag) is the weapon's IMMUNE_WHILE_ATTACKING, which Attack.ImmuneWhileAttacking reads for its hero (rule
			// book Advanced_rulebook.txt:509: "If you have a weapon that grants Immunity while attacking ... at the start of Combat
			// Sequence, you are Immune in the whole Sequence").
			if (weapon && distinct.All(t => t.Tag != (int)GameTag.IMMUNE_WHILE_ATTACKING) && dbf.TryGetValue(dbfId, out var withImmune) && withImmune.Text != null &&
			    Regex.IsMatch(Regex.Replace(Regex.Replace(withImmune.Text, "<[^>]+>", ""), "\"[^\"]*\"", ""), @"(^|[\s.\]_])Your hero is Immune while attacking\."))
				distinct.Add(((int)GameTag.IMMUNE_WHILE_ATTACKING, 1));
			// HsServer (2026-10-06): SPELLPOWER_<school> (1945-1952) is the client's keyword (always 1: Radiance of Azshara says
			// Fire +2, Bru'kan Nature +3); the amount is the card's own "<School> Spell Damage +N". A card that only names it
			// (Prestor's Pyromancer: "Your next Fire spell has Spell Damage +2") gives none by being in play.
			for (int i = 0; i < distinct.Count; i++)
				if (distinct[i].Tag >= 1945 && distinct[i].Tag <= 1952 && distinct[i].Value > 0 && dbf.TryGetValue(dbfId, out var withSchool))
				{
					// (SPACE: only the card's OWN keyword, the bold one its text opens with — Saruun's "Give all Elementals in your deck
					// <b>Fire Spell Damage +1</b>" gives Saruun none: live S0930 16038 FULL_ENTITY has no SPELLPOWER_FIRE)
					Match sd = Regex.Match(withSchool.Text ?? "", @"^\s*(?:\[x\])?\s*(?:<b>[^<]+</b>[,.\s]*)*?<b>(Arcane|Fire|Frost|Nature|Holy|Shadow|Fel) Spell Damage \+(\d+)</b>");
					distinct[i] = (distinct[i].Tag, sd.Success ? int.Parse(sd.Groups[2].Value) : 0);
				}
			return distinct;
		}

		/// <summary>
		/// HsServer (TITANS review 2026-10-07): is the card set <paramref name="set"/> in Standard now — "from the past" is "any card
		/// set that has become exclusive to Wild format" (wiki Toki, Time-Tinker), so it moves with each rotation. The server sets
		/// <see cref="IsStandardSetNow"/> from its event calendar (CardSet.StandardEvent active: BoosterRules.IsStandardSet); without
		/// it (soak, tests) the client data's rotation: the Core set and the expansions of the two newest Standard years
		/// (CardSet.json: the expansions sharing a StandardEvent are one year, newest by ReleaseOrder — wiki Standard format: the
		/// Core set plus the expansions of the current and the previous year).
		/// </summary>
		public static bool InStandardNow(int set) => IsStandardSetNow?.Invoke(set) ?? StandardByRotation().Contains(set);

		/// <summary>HsServer (ISLAND_VACATION): "the newest expansion" (Resort Valet) — of the expansions in Standard now, the one
		/// with the highest ReleaseOrder (CardSet.json); 0 without client data.</summary>
		public static int NewestExpansion()
		{
			if (_newestExpansion >= 0) return _newestExpansion;
			int best = 0, order = int.MinValue;
			string file = DbfDirectory == null ? null : Path.Combine(DbfDirectory, "CardSet.json");
			if (file != null && File.Exists(file))
				using (var doc = JsonDocument.Parse(File.ReadAllBytes(file)))
					foreach (JsonElement r in doc.RootElement.GetProperty("records").EnumerateArray())
					{
						int id = r.GetProperty("ID").GetInt32();
						int o = r.TryGetProperty("ReleaseOrder", out JsonElement ro) ? ro.GetInt32() : 0;
						bool expansion = r.TryGetProperty("IsExpansionCardSet", out JsonElement ex) && ex.ValueKind == JsonValueKind.True;
						if (expansion && InStandardNow(id) && o > order) { best = id; order = o; }
					}
			return _newestExpansion = best;
		}
		private static int _newestExpansion = -1;

		/// <summary>The server's "is this set in Standard now" (its event calendar); null: the data's rotation.</summary>
		public static Func<int, bool> IsStandardSetNow { get; set; }

		private static HashSet<int> _standardByRotation;

		private static HashSet<int> StandardByRotation()
		{
			if (_standardByRotation != null) return _standardByRotation;
			var result = new HashSet<int>();
			string file = DbfDirectory == null ? null : Path.Combine(DbfDirectory, "CardSet.json");
			if (file != null && File.Exists(file))
			{
				var sets = new List<(int Id, string Standard, int Order, bool Expansion, bool Collectible)>();
				using (var doc = JsonDocument.Parse(File.ReadAllBytes(file)))
					foreach (JsonElement r in doc.RootElement.GetProperty("records").EnumerateArray())
						sets.Add((r.GetProperty("ID").GetInt32(),
							r.TryGetProperty("StandardEvent", out JsonElement se) ? se.GetString() : null,
							r.TryGetProperty("ReleaseOrder", out JsonElement ro) ? ro.GetInt32() : 0,
							r.TryGetProperty("IsExpansionCardSet", out JsonElement ex) && ex.ValueKind == JsonValueKind.True,
							r.TryGetProperty("IsCollectible", out JsonElement co) && co.ValueKind == JsonValueKind.True));
				static bool Dated(string e) => e != null && !e.StartsWith("SPECIAL_EVENT", StringComparison.Ordinal);
				// the two newest Standard years: the expansions' StandardEvents, newest first by their latest ReleaseOrder
				var years = sets.Where(s => s.Expansion && Dated(s.Standard)).GroupBy(s => s.Standard)
					.OrderByDescending(g => g.Max(s => s.Order)).Take(2).Select(g => g.Key).ToHashSet();
				foreach (var s in sets.Where(s => years.Contains(s.Standard))) result.Add(s.Id);
				// the Core set: the collectible base set (ReleaseOrder below the expansions') whose Standard period no other set shares
				foreach (var s in sets.Where(s => !s.Expansion && s.Collectible && s.Order < 1000 && Dated(s.Standard) &&
				                                   sets.Count(o => o.Standard == s.Standard) == 1))
					result.Add(s.Id);
			}
			else
				foreach (CardSet set in Cards.StandardSets) result.Add((int)set); // no client data: the 2019 constant
			return _standardByRotation = result;
		}

		private static Dictionary<int, List<(int Set, string Event)>> _setTiming;
		private static Dictionary<string, string> _eventNames;

		/// <summary>HsServer (2026-10-09): the server's "is this DBF event on now" (its event calendar: ClientCardSets.IsActive),
		/// set before the cards load; null: the data's own answer (a post_… rotation row is in effect, a pre_… one is over).</summary>
		public static Func<string, bool> IsEventActiveNow { get; set; }

		/// <summary>
		/// HsServer: the card's set now, from CardSetTiming: its SPECIAL_EVENT_ALWAYS row, else (2026-10-09) the dated row whose event
		/// is on now — ~1,200 cards (Core reprints, cards moved to Legacy) list only a pre / post pair around a yearly rotation
		/// (Chaos Strike: pre_set_rotation_2021 → Basic 2, post_set_rotation_2021 → Legacy 1635). Was: always rows only — those
		/// cards had no set (out of Standard pools, "from the past" for Timeways). Null when it has none.
		/// </summary>
		public static int? SetOf(int dbfId)
		{
			if (_setTiming == null)
			{
				var map = new Dictionary<int, List<(int, string)>>();
				string file = DbfDirectory == null ? null : Path.Combine(DbfDirectory, "CardSetTiming.json");
				if (file != null && File.Exists(file))
				{
					using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(file));
					foreach (var r in doc.RootElement.GetProperty("records").EnumerateArray())
						if (r.TryGetProperty("EventTimingEvent", out var ev) && r.TryGetProperty("CardId", out var card) &&
						    r.TryGetProperty("CardSetId", out var cs))
						{
							if (!map.TryGetValue(card.GetInt32(), out var rows)) map[card.GetInt32()] = rows = new List<(int, string)>();
							rows.Add((cs.GetInt32(), ev.GetString()));
						}
				}
				_setTiming = map;
			}
			if (!_setTiming.TryGetValue(dbfId, out var list)) return null;
			foreach (var (set, ev) in list)
				if (ev == "SPECIAL_EVENT_ALWAYS") return set;
			foreach (var (set, ev) in list)
				if (IsEventActiveNow?.Invoke(ev) ?? EventName(ev)?.StartsWith("post_", StringComparison.Ordinal) == true) return set;
			return null;
		}

		/// <summary>A DBF event's name (EventMap.json: "10000294" → post_set_rotation_2021), or null.</summary>
		private static string EventName(string dbfEvent)
		{
			if (_eventNames == null)
			{
				var names = new Dictionary<string, string>();
				string file = DbfDirectory == null ? null : Path.Combine(DbfDirectory, "EventMap.json");
				if (file != null && File.Exists(file))
				{
					using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(file));
					foreach (var r in doc.RootElement.GetProperty("records").EnumerateArray())
						if (r.TryGetProperty("ID", out var id) && r.TryGetProperty("Name", out var name))
							names[id.GetRawText()] = name.GetString();
				}
				_eventNames = names;
			}
			return dbfEvent != null && _eventNames.TryGetValue(dbfEvent, out string n) ? n : null;
		}

		private static Dictionary<int, HashSet<int>> _allSets;

		/// <summary>HsServer: every set CardSetTiming puts the card in, at any time (Huffer: Basic 2, then Legacy 1635); null
		/// without the export.</summary>
		public static HashSet<int> SetsOf(int dbfId)
		{
			if (_allSets == null)
			{
				var map = new Dictionary<int, HashSet<int>>();
				string file = DbfDirectory == null ? null : Path.Combine(DbfDirectory, "CardSetTiming.json");
				if (file == null || !File.Exists(file)) return null;
				using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(file));
				foreach (var r in doc.RootElement.GetProperty("records").EnumerateArray())
					if (r.TryGetProperty("CardId", out var card) && r.TryGetProperty("CardSetId", out var cs))
					{
						if (!map.TryGetValue(card.GetInt32(), out var sets)) map[card.GetInt32()] = sets = new HashSet<int>();
						sets.Add(cs.GetInt32());
					}
				_allSets = map;
			}
			return _allSets.TryGetValue(dbfId, out var all) ? all : new HashSet<int>();
		}

		/// <summary>What a card is: the export's value, or 0 when it lists none (it leaves 0 out).</summary>
		private static readonly HashSet<GameTag> IdentityTags = new HashSet<GameTag>
		{
			GameTag.CARDTYPE, GameTag.CLASS, GameTag.COLLECTIBLE, GameTag.RARITY, GameTag.ELITE, // (the export has no CARD_SET tag: CardSetTiming)
		};

		/// <summary>Current values a Basic/Classic card takes from the client: stats, overload, spell damage, tribe, keywords.</summary>
		private static readonly HashSet<GameTag> CurrentTags = new HashSet<GameTag>
		{
			GameTag.COST, GameTag.ATK, GameTag.HEALTH, GameTag.DURABILITY, GameTag.OVERLOAD, GameTag.SPELLPOWER, GameTag.CARDRACE,
			GameTag.TAUNT, GameTag.CHARGE, GameTag.RUSH, GameTag.DIVINE_SHIELD, GameTag.STEALTH, GameTag.WINDFURY,
			GameTag.POISONOUS, GameTag.LIFESTEAL, GameTag.FREEZE,
			Actions.Generic.TRADEABLE, Actions.Generic.DECK_ACTION_COST,
			(GameTag)1635, // SPELL_SCHOOL (Holy, Shadow...: current cards and scripts key on it)
		};

		/// <summary>
		/// Brings SabberStone's cards (every set, and their tokens) to the current client version: stats and keywords
		/// Blizzard changed since 2021 (Cairne has Taunt, Baine is 5/5, Feral Spirit overloads 1...), and every client tag
		/// the 2021 data lacks (minion types and spell schools added since, hero emote tags...), which the client reads
		/// from the entity. Effects written into scripts are not changed here — see <see cref="Stale"/>.
		/// </summary>
		internal static Card[] UpdateBaseCards(Card[] baseCards)
		{
			if (!LoadClient()) return baseCards;
			var updated = new Card[baseCards.Length];
			for (int i = 0; i < baseCards.Length; i++)
			{
				Card c = baseCards[i];
				updated[i] = c;
				if (!dbfByCardId.TryGetValue(c.Id, out int dbfId))
					continue;
				// The export (the server's card database: Server/data/dbf-*) is the base for every card it has: each of its
				// tags wins — type, class, set, collectible too (user 2026-10-03; Lord Jaraxxus is a Hero card now, the 2021
				// data had a minion). Stats and keywords (CurrentTags) follow it both ways: one it doesn't list is gone
				// (Druid of the Claw's bear lost Charge for Rush). The export leaves 0 out: what a card is (IdentityTags) is 0
				// when it lists nothing (a card no longer collectible has no COLLECTIBLE). Other 2021 tags it has nothing for stay.
				var merged = c.Tags.Where(t => !CurrentTags.Contains(t.Key) && !IdentityTags.Contains(t.Key)).ToDictionary(t => t.Key, t => t.Value);
				foreach (var (tag, value) in ClientTags(dbfId))
					merged[(GameTag)tag] = value;
				bool changed = merged.Count != c.Tags.Count || merged.Any(t => !c.Tags.TryGetValue(t.Key, out int old) || old != t.Value);
				if (!changed) continue;
				var tagList = merged.Select(t => new Tag(t.Key, t.Value)).ToList();
				tagList.Add(new Tag(GameTag.CARDNAME, c.Name));
				tagList.Add(new Tag(GameTag.CARDTEXT, c.Text ?? "")); // the script's text: Stale compares it with the client's
				updated[i] = new Card(c.Id, c.AssetId, tagList.ToArray(), c.PlayRequirements ?? new Dictionary<PlayReq, int>(),
					c.Entourage, c.RefTags.Select(t => new Tag(t.Key, t.Value)).ToArray());
			}
			return updated;
		}

		internal static IEnumerable<Card> Build(Card[] baseCards)
		{
			if (!LoadClient()) yield break;
			var byId = baseCards.ToDictionary(c => c.Id);
			foreach (Card c in baseCards) _baseAssetIds[c.Id] = c.AssetId;
			// HsServer: the same card under a new id — the client's dbf id is the card's identity. An engine card whose id the
			// client doesn't have, but whose dbf id it does, is known to the client by that card's id (AT_132_SHAMAN is dbf 2742
			// = HERO_02bp2, CS2_049 = HERO_02bp): entities are created as the current one (Current). The hand-kept Renamed list
			// only ever covered some (engine audit 2026-10-02).
			foreach (Card c in baseCards)
				if (!dbfByCardId.ContainsKey(c.Id) && dbf.TryGetValue(c.AssetId, out var renamed) && renamed.CardId != c.Id && !_currentOf.ContainsKey(c.Id))
					_currentOf[c.Id] = renamed.CardId;

			// Cards Blizzard changed after SabberStone's data (2021): the client's text differs from the script's.
			foreach (var (dbfId, entry) in dbf)
			{
				_clientText[entry.CardId] = entry.Text;
				if (byId.TryGetValue(entry.CardId, out Card baseCard) && Normalize(entry.Text) != Normalize(baseCard.Text))
					_stale.Add(entry.CardId);
			}

			foreach (var (dbfId, entry) in dbf)
			{
				string canonicalId = Canonical(entry.CardId);
				if (canonicalId == entry.CardId || byId.ContainsKey(entry.CardId) || !byId.TryGetValue(canonicalId, out Card original))
					continue;
				if (!Renamed.ContainsKey(entry.CardId) && Normalize(entry.Text) != Normalize(original.Text))
					_stale.Add(entry.CardId);
				string originalText = dbfByCardId.TryGetValue(canonicalId, out int originalDbf) ? dbf[originalDbf].Text : original.Text;
				// The renamed list is checked by hand: its new texts only add a "Hero Power" header and value
				// placeholders ($d2, {0}), so the text gate would wrongly reject them.
				if (!Renamed.ContainsKey(entry.CardId) && Normalize(entry.Text) != Normalize(originalText))
				{
					_reworded.Add(entry.CardId);
					continue;
				}

				// The reprint's own tags from the client (current stats and keywords; reference tags were dropped), on top of
				// the original's (SabberStone tags the client doesn't carry).
				if (Renamed.ContainsKey(entry.CardId) && !dbfByCardId.ContainsKey(canonicalId))
					_currentOf[canonicalId] = entry.CardId; // the old id is gone from the client: always use the new one
				yield return Reprint(dbfId, entry, original);
			}

			var byName = baseCards.Where(c => c.Name != null).GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.ToList());
			// Core cards with no 2021 original (newer expansions): added from the client's own tags, so they exist and play as
			// their stats and keywords; effects in text need a script (CardSets/HsServer). Collectible only for now.
			// HsServer: also the original of each Core reprint (REV_290 for CORE_REV_290): players build decks with either, and
			// the original shares the Core card's script when their texts match (CardDefs).
			var coreOriginals = dbf.Values.Where(e => e.CardId.StartsWith("CORE_", StringComparison.OrdinalIgnoreCase))
				.Select(e => e.CardId.Substring(5)).ToHashSet();
			foreach (var (dbfId, entry) in dbf)
			{
				// HsServer (2026-09-30, user decision): every collectible card of the client is loaded, scripted or not, so decks
				// never lose cards; unscripted ones play as their stats and keywords until their set is scripted.
				if (byId.ContainsKey(entry.CardId) || _canonicalOf.ContainsKey(entry.CardId)
				    || HeroId.IsMatch(entry.CardId) && Tag(tags, dbfId, GameTag.CARDTYPE) == (int)CardType.HERO) // hero skins: below
					continue;
				if (Tag(tags, dbfId, GameTag.COLLECTIBLE) != 1 && !ReferencedCards.Contains(entry.CardId))
					continue;
				// A hero card needs its (client) hero power, which the loader adds with the other client hero powers.
				if (Tag(tags, dbfId, GameTag.CARDTYPE) == (int)CardType.HERO && !dbf.ContainsKey(Tag(tags, dbfId, GameTag.HERO_POWER)))
					continue;
				if (!Playable(Tag(tags, dbfId, GameTag.CARDTYPE)))
				{
					_unsupported.Add(entry.CardId); // e.g. Locations: a card type the engine can't create
					continue;
				}
				if (Aliases.TryGetValue(entry.CardId, out string alias) && byId.TryGetValue(alias, out Card aliased))
				{
					yield return Reprint(dbfId, entry, aliased);
					continue;
				}
				// HsServer (WONDERS, 2026-10-07): a card that counts as a copy of another (client tag 858, same current text: the
				// Caverns of Time reprints, WON_018 Snipe = EX1_609) plays with that card's script. The 2021 text the name match
				// below compares no longer matches once Blizzard updated the original (27.2), so 54 WONDERS cards were placeholders.
				if (CopyOf(dbfId, entry, byId) is Card copied)
				{
					yield return copied;
					continue;
				}
				// A reprint under an unrelated id (WON_351 Small-Time Buccaneer = CFM_325): same name, type and text as a 2021
				// card, so it plays with that card's script.
				int type = Tag(tags, dbfId, GameTag.CARDTYPE);
				Card same = byName.TryGetValue(entry.Name ?? "", out var named)
					? named.FirstOrDefault(c => (int)c.Type == type && Normalize(c.Text) == Normalize(entry.Text))
					: null;
				if (same != null)
				{
					yield return Reprint(dbfId, entry, same);
					continue;
				}
				var own = ClientTags(dbfId).Select(t => new Tag((GameTag)t.Tag, t.Value)).ToList();
				own.AddRange(ElusiveTags(own));
				if (own.All(t => t.GameTag != GameTag.CARD_SET) && SetOf(dbfId) is int set) own.Add(new Tag(GameTag.CARD_SET, set)); // its set (CardSetTiming)
				own.Add(new Tag(GameTag.CARDNAME, entry.Name));
				own.Add(new Tag(GameTag.CARDTEXT, entry.Text ?? ""));
				_clientOnly.Add(entry.CardId);
				yield return new Card(entry.CardId, dbfId, own.ToArray(), new Dictionary<PlayReq, int>(), null, Array.Empty<Tag>());
			}

			// Their tokens and enchantments (the client DB names them after the card: SW_439t Acorn, ULD_191e ...), when the
			// engine lacks them.
			// HsServer: every collectible card is a base now (also engine cards that lack their newer tokens: YOD_012ts).
			// HsServer: Excavate treasures (client tag 2823) are cards in their own right: DEEP_999t1… are named after no
			// collectible card, so they (and their enchantments, DEEP_999t1e) never loaded and Excavate never gave them.
			// HsServer: so are the cards a Forge card turns into (FORGES_INTO): Tyr's Tears TTN_855t forges into TTN_855, which
			// isn't named after it.
			var forged = dbf.Where(d => Tag(tags, d.Key, GameTag.COLLECTIBLE) == 1).Select(d => Tag(tags, d.Key, (GameTag)3074))
				.Where(f => f > 0).ToHashSet();
			// HsServer (2026-10-04): and the card a collectible card names as its own (COLLECTION_RELATED_CARD_DATABASE_ID, 1452:
			// the token the collection shows beside it): Rime Sculptor RLK_752 summons Rime Elemental RLK_907t, not named after it.
			var linked = dbf.Where(d => Tag(tags, d.Key, GameTag.COLLECTIBLE) == 1).Select(d => Tag(tags, d.Key, (GameTag)1452))
				.Where(f => f > 0).ToHashSet();
			// HsServer (2026-10-07): and the cards the client's RelatedCards table relates to a collectible card: Symphony of Sins
			// ETC_085 relates Movement of Sloth's Bored Doomlord ETC_t8t, which isn't named after any card.
			linked.UnionWith(RelatedDbfIds(dbf.Keys.Where(k => Tag(tags, k, GameTag.COLLECTIBLE) == 1)));
			var bases = dbf.Where(d => Tag(tags, d.Key, GameTag.COLLECTIBLE) == 1 || Tag(tags, d.Key, (GameTag)2823) == 1 || forged.Contains(d.Key) || linked.Contains(d.Key))
				.Select(d => Canonical(d.Value.CardId))
				.Concat(_clientOnly.Select(Canonical)).ToHashSet(StringComparer.OrdinalIgnoreCase); // HsServer: the client has
				// some ids in lower case (Kazakus' Superior Wildvine is "bar_079t10c"): a token named after its card in any case
			// HsServer: a collectible card with a token-style id (Ivus AV_142t, Lokholar AV_141t) names its enchantments after
			// its stem (AV_142e Crystalskin): the stem is a base too
			foreach (string b in bases.ToList())
				if (System.Text.RegularExpressions.Regex.Match(b, @"^(.+_\d+)t\d*$") is { Success: true } m) bases.Add(m.Groups[1].Value);
			bool NamedAfterBase(string tokenId)
			{
				for (int len = tokenId.Length - 1; len >= 4; len--)
					if (bases.Contains(tokenId.Substring(0, len))) return true;
				return false;
			}
			var added = new HashSet<string>(_clientOnly);
			foreach (var (dbfId, entry) in dbf)
			{
				if (added.Contains(entry.CardId) || byId.ContainsKey(entry.CardId) || _canonicalOf.ContainsKey(entry.CardId)) continue;
				if (Tag(tags, dbfId, GameTag.COLLECTIBLE) == 1) continue;
				string id = Canonical(entry.CardId);
				if (!Tokens.Contains(entry.CardId) && !NamedAfterBase(id) && Tag(tags, dbfId, (GameTag)2823) != 1 && !forged.Contains(dbfId) && !linked.Contains(dbfId)) continue;
				if (HeroId.IsMatch(entry.CardId) && Tag(tags, dbfId, GameTag.CARDTYPE) == (int)CardType.HERO) continue; // hero skins: below
				int type = Tag(tags, dbfId, GameTag.CARDTYPE);
				if (!Playable(type) && type != (int)CardType.ENCHANTMENT) continue;
				if (type == (int)CardType.HERO_POWER) continue; // added below with the other client-only hero powers
				// HsServer (WONDERS): a token that counts as a copy of another (tag 858: Anub'arak's Nerubian WON_076t = AT_036t;
				// live AT_036 summons WON_076t) — as the collectible copies above
				if (CopyOf(dbfId, entry, byId) is Card copiedToken)
				{
					added.Add(entry.CardId);
					yield return copiedToken;
					continue;
				}
				// HsServer (WONDERS): a reprint's twin enchantment (WON_009e = OG_313e: "<reprint id><suffix>", the same name and
				// text as "<original id><suffix>") plays as the original's (TwinEnchantment gives it in its place)
				if (type == (int)CardType.ENCHANTMENT && TwinOf(entry) is string twinId)
				{
					added.Add(entry.CardId);
					if (byId.TryGetValue(twinId, out Card twinCard))
						yield return Reprint(dbfId, entry, twinCard, ownSet: true);
					else
					{
						_canonicalOf[entry.CardId] = twinId;
						yield return ClientOnlyCard(dbfId, entry);
					}
					continue;
				}
				// HsServer (2026-10-06): a token that is the same card as a 2021 one under another id (Kazakusan's Treasures are
				// Dalaran Heist / Uldum / Naxxramas cards: ONY_005ta1 Necrotic Poison = NAX3_03) plays with that card's script, like
				// the collectible reprints above (same name, type and text). A script of its own (CardDefs by id) still wins.
				if (byName.TryGetValue(entry.Name ?? "", out var twins)
				    && twins.FirstOrDefault(c => (int)c.Type == type && Normalize(c.Text) == Normalize(entry.Text)) is Card twin)
				{
					added.Add(entry.CardId);
					yield return Reprint(dbfId, entry, twin);
					continue;
				}
				var own = ClientTags(dbfId).Select(t => new Tag((GameTag)t.Tag, t.Value)).ToList();
				own.AddRange(ElusiveTags(own));
				if (own.All(t => t.GameTag != GameTag.CARD_SET) && SetOf(dbfId) is int set) own.Add(new Tag(GameTag.CARD_SET, set)); // its set (CardSetTiming)
				own.Add(new Tag(GameTag.CARDNAME, entry.Name));
				own.Add(new Tag(GameTag.CARDTEXT, entry.Text ?? ""));
				added.Add(entry.CardId);
				yield return new Card(entry.CardId, dbfId, own.ToArray(), new Dictionary<PlayReq, int>(), null, Array.Empty<Tag>());
			}

			// Hero powers only the client has (newer hero skins' powers, e.g. HERO_02bhp): added as cards with the client's
			// tags; their script is the matching scripted power's (MapHeroPowerVariants, once scripts are loaded).
			foreach (var (dbfId, entry) in dbf)
				if (Tag(tags, dbfId, GameTag.CARDTYPE) == (int)CardType.HERO_POWER && !byId.ContainsKey(entry.CardId) && !_canonicalOf.ContainsKey(entry.CardId))
				{
					var powerTags = tags[dbfId].GroupBy(t => t.Tag).Select(g => new Tag((GameTag)g.Key, g.First().Value)).ToList();
					powerTags.Add(new Tag(GameTag.CARDNAME, entry.Name));
					powerTags.Add(new Tag(GameTag.CARDTEXT, entry.Text ?? ""));
					yield return new Card(entry.CardId, dbfId, powerTags.ToArray(), new Dictionary<PlayReq, int>(), null, Array.Empty<Tag>());
				}

			// Class heroes and skins only the client has (Illidan HERO_10 and his skins: Demon Hunter is newer than this
			// engine's data), with the client's tags; HERO_POWER points at their (client) hero power by DBF id.
			foreach (var (dbfId, entry) in dbf)
				if (Tag(tags, dbfId, GameTag.CARDTYPE) == (int)CardType.HERO && HeroId.IsMatch(entry.CardId) && !byId.ContainsKey(entry.CardId) && !_canonicalOf.ContainsKey(entry.CardId))
				{
					var heroTags = ClientTags(dbfId).Select(t => new Tag((GameTag)t.Tag, t.Value)).ToList();
					heroTags.Add(new Tag(GameTag.CARDNAME, entry.Name));
					heroTags.Add(new Tag(GameTag.CARDTEXT, entry.Text ?? ""));
					yield return new Card(entry.CardId, dbfId, heroTags.ToArray(), new Dictionary<PlayReq, int>(), null, Array.Empty<Tag>());
				}

			_heroPowers = dbf.Where(d => Tag(tags, d.Key, GameTag.CARDTYPE) == (int)CardType.HERO_POWER)
				.Select(d => (d.Value.CardId, (Tag(tags, d.Key, GameTag.CLASS), Tag(tags, d.Key, GameTag.COST), HeroPowerText(d.Value.Text))))
				.ToList();
			// HsServer: the engine's own hero powers the client no longer has (AT_132_WARRIOR, Justicar's Tank Up!): scripted, so
			// the client's same power under another id (HERO_01bp2, the skins' CS2_102_H1_AT_132) plays with their script
			_engineHeroPowers = baseCards.Where(c => c.Type == CardType.HERO_POWER && !dbfByCardId.ContainsKey(c.Id))
				.Select(c => (c.Id, ((int)c.Class, c.Cost, HeroPowerText(c.Text)))).ToList();
		}

		/// <summary>
		/// Hero-skin hero powers (e.g. CS2_034_H2, Khadgar's Fireblast) are separate cards without a script of their own.
		/// Each one plays like the scripted hero power with the same class, cost and card text (compared on the client's
		/// texts), so it borrows that one's definition — targets included.
		/// </summary>
		private static readonly Regex HeroId = new Regex(@"^HERO_\d+[a-z]*$");

		private static List<(string CardId, (int Class, int Cost, string Text) Key)> _heroPowers = new List<(string, (int, int, string))>();
		private static List<(string CardId, (int Class, int Cost, string Text) Key)> _engineHeroPowers = new List<(string, (int, int, string))>();

		private static int Tag(Dictionary<int, List<(int Tag, int Value)>> tags, int dbfId, GameTag tag) =>
			tags.TryGetValue(dbfId, out var list) ? list.FirstOrDefault(t => t.Tag == (int)tag).Value : 0;

		/// <summary>
		/// HsServer: the upgrade of a hero power (Justicar Trueheart) as the client names it: its own UPGRADED_HERO_POWER
		/// (1086), else that of the client hero power with the same class, cost and text (the engine's 2021 basic powers —
		/// CS2_049 — have none; their current equivalent HERO_02bp names HERO_02bp2). 0 when the client data has none.
		/// </summary>
		public static int UpgradedHeroPower(Card heroPower, Card hero = null)
		{
			if (heroPower == null) return 0;
			// only the client's data: the engine's 2021 tag names the old AT_132_* cards the client no longer has
			if (!LoadClient()) return heroPower[HsTag.UPGRADED_HERO_POWER];
			if (dbfByCardId.TryGetValue(heroPower.Id, out int ownDbf) && Tag(tags, ownDbf, HsTag.UPGRADED_HERO_POWER) is int own && own > 0)
				return own;
			// the hero's own current power in the client (HERO_02 → HERO_02bp → HERO_02bp2): the 2021 basic powers' texts differ
			// from today's ("a random Totem" / "a random basic Totem"), but the hero is the same card
			if (hero != null && dbfByCardId.TryGetValue(hero.Id, out int heroDbf) && Tag(tags, heroDbf, GameTag.HERO_POWER) is int power && power > 0
			    && Tag(tags, power, HsTag.UPGRADED_HERO_POWER) is int upgraded && upgraded > 0)
				return upgraded;
			var key = ((int)heroPower.Class, heroPower.Cost, HeroPowerText(_clientText.TryGetValue(heroPower.Id, out string t) ? t : heroPower.Text));
			foreach (var (id, k) in _heroPowers)
				if (k == key && dbfByCardId.TryGetValue(id, out int dbf) && Tag(tags, dbf, HsTag.UPGRADED_HERO_POWER) is int up && up > 0)
					return up;
			return 0;
		}

		/// <summary>Called once card scripts are available (CardContainer.Load): see the remarks above.</summary>
		internal static void MapHeroPowerVariants(Func<string, bool> hasScript)
		{
			// any card type: a client card that is a scripted engine card under a new id (same dbf: HERO_03bp2 = AT_132_ROGUE,
			// HERO_04bp2 = AT_132_PALADIN) plays with that card's script — the engine creates it under the client's id (Current)
			foreach (var (engineId, clientId) in _currentOf.ToList())
				if (hasScript(engineId) && !hasScript(clientId) && !_canonicalOf.ContainsKey(clientId))
					_canonicalOf[clientId] = engineId;
			var engineOf = _currentOf.Where(kv => hasScript(kv.Key)).GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.First().Key);
			bool Scripted(string id) => hasScript(id) || _canonicalOf.TryGetValue(id, out string c) && hasScript(c) || engineOf.ContainsKey(id);
			string ScriptOf(string id) => _canonicalOf.TryGetValue(id, out string c) && hasScript(c) ? c : engineOf.TryGetValue(id, out string e) && !hasScript(id) ? e : id;
			var scripted = new Dictionary<(int, int, string), string>();
			foreach (var (id, key) in _heroPowers)
				if (Scripted(id) && !scripted.ContainsKey(key))
					scripted[key] = ScriptOf(id);
			foreach (var (id, key) in _engineHeroPowers) // after the client's own: those win
				if (hasScript(id) && !scripted.ContainsKey(key))
					scripted[key] = id;
			foreach (var (id, key) in _heroPowers)
				if (!Scripted(id) && scripted.TryGetValue(key, out string original))
					_canonicalOf[id] = original;
		}

		// Card text compared without markup, the [x] layout marker, placeholders and whitespace
		// ("Deal $3 damage to a_character" == "Deal 3 damage to a character").
		// Everything from '@' on is SabberStone's "@spelldmg" marker or the client's alternate text.
		/// <summary>HsServer (token pass 2026-10-09): a hero power's text for matching hero powers (MapHeroPowerVariants), without the
		/// "<b>Hero Power</b>" header newer prints open with — the Legacy INFERNO! VAN_EX1_tk33 "Hero Power Summon a 6/6 Infernal." is
		/// EX1_tk33 "Summon a 6/6 Infernal." (same class and cost); the Renamed list above was kept by hand for the same reason.</summary>
		private static string HeroPowerText(string text)
		{
			string n = Normalize(text);
			return n.StartsWith("heropower", StringComparison.Ordinal) ? n.Substring("heropower".Length) : n;
		}

		private static string Normalize(string text) =>
			Regex.Replace((text ?? "").Split('@')[0].ToLowerInvariant(), @"\[x\]|<[^>]+>|\(\*\)|\$[a-z](?=\d)|[\$#_\s]", ""); // "$d4 Armor" == "4 Armor"
	}
}
