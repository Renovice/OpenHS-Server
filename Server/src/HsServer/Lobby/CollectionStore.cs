using System.Text.Json;
using HsServer.Engine;
using HsServer.Protocol;
using PegasusShared;
using PegasusUtil;

namespace HsServer.Lobby;

/// <summary>
/// Crafting values and rules from the client's own data (roadmap B2), so the server agrees with what the crafting UI
/// shows. As CraftingManager.GetCardValue / CraftingUI.IsCraftingEventForCardActive:
/// - value: InitCardValue (rarity, premium) → Buy / Sell / Upgrade dust, replaced by the first CardValue override (no
///   region) whose OverrideEvent is on (nerf refunds: Sell 0 + RECENTLY_NERFED_USE_BUY_VALUE = sells for Buy);
/// - craftable / disenchantable now: the premium's crafting event (Card.CraftingEvent / Golden… / Signature… /
///   Diamond…, else the card set's ContentLaunchEvent) is on in the <see cref="EventCalendar"/> ("never": free Core cards);
///   hero skins (a hero with a CardHero record) never, playable hero cards like any card;
/// - a Classic card uses its Legacy counterpart's value, events and overrides.
/// A card's deck limit (what counts as "extra") is 1 for Legendaries, else 2.
/// </summary>
internal sealed class CraftingRules
{
    private const int ClassicSet = 1646, LegacySet = 1635; // TAG_CARD_SET.VANILLA (format Classic) / LEGACY

    private readonly Dictionary<(int Rarity, int Premium), (int Buy, int Sell, int Upgrade)> values = new();
    private readonly Dictionary<int, string[]> events = new(); // dbf → crafting event per premium (normal, golden, diamond, signature)
    private readonly Dictionary<int, string> setLaunch = new(); // card set → ContentLaunchEvent
    // CardValue overrides with no region (the CN ones are China's), in table order: the first one whose event is on wins
    private readonly Dictionary<(int Dbf, int Premium), List<(string Event, int Buy, int Sell, string SellState)>> overrides = new();
    private readonly Dictionary<int, List<int>> counterparts = new(); // card dbf → its counterparts (CounterpartCards)
    private readonly Dictionary<int, List<(int Set, string Event)>> setTiming = new(); // card dbf → set while an event is on
    private readonly HashSet<int> heroSkins = new(); // hero cards with a CardHero record (EntityDef.IsHeroSkin)
    private readonly CardDb cards;
    private readonly EventCalendar calendar;

    public CraftingRules(string dbfDir, CardDb cards, EventCalendar calendar)
    {
        this.cards = cards;
        this.calendar = calendar;
        foreach (JsonElement r in Records(dbfDir, "InitCardValue.json"))
            values[(Int(r, "Rarity"), Int(r, "Premium"))] = (Int(r, "Buy"), Int(r, "Sell"), Int(r, "Upgrade"));
        foreach (JsonElement r in Records(dbfDir, "CardValue.json"))
            if (Str(r, "OverrideRegion") is null or "NONE")
            {
                var key = (Int(r, "AssetCardId"), Int(r, "Premium"));
                if (!overrides.TryGetValue(key, out var list)) overrides[key] = list = new();
                list.Add((Str(r, "OverrideEvent"), Int(r, "Buy"), Int(r, "Sell"), Str(r, "SellState")));
            }
        foreach (JsonElement r in Records(dbfDir, "CardSet.json"))
            setLaunch[Int(r, "ID")] = Str(r, "ContentLaunchEvent");
        foreach (JsonElement r in Records(dbfDir, "CardHero.json"))
            heroSkins.Add(Int(r, "CardId"));
        foreach (JsonElement r in Records(dbfDir, "CounterpartCards.json"))
        {
            if (!counterparts.TryGetValue(Int(r, "PrimaryCardId"), out var list)) counterparts[Int(r, "PrimaryCardId")] = list = new();
            list.Add(Int(r, "DeckEquivalentCardId"));
        }
        foreach (JsonElement r in Records(dbfDir, "CardSetTiming.json"))
        {
            if (!setTiming.TryGetValue(Int(r, "CardId"), out var list)) setTiming[Int(r, "CardId")] = list = new();
            list.Add((Int(r, "CardSetId"), Str(r, "EventTimingEvent")));
        }
        foreach (JsonElement r in Records(dbfDir, "Card.json"))
            events[Int(r, "ID")] = new[] { Str(r, "CraftingEvent"), Str(r, "GoldenCraftingEvent"), Str(r, "DiamondCraftingEvent"), Str(r, "SignatureCraftingEvent") };
    }

    private static List<JsonElement> Records(string dbfDir, string file)
    {
        string path = Path.Combine(dbfDir, file);
        if (!File.Exists(path)) return new List<JsonElement>();
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        return doc.RootElement.GetProperty("records").EnumerateArray().Select(r => r.Clone()).ToList();
    }

    private static int Int(JsonElement r, string name) => r.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
    private static string Str(JsonElement r, string name) => r.TryGetProperty(name, out JsonElement v) ? v.GetString() : null;

    /// <summary>The card's set now: CardSetTiming (card → set while an event is on; the client DB has no CARD_SET tag).</summary>
    public int SetOf(int dbf) =>
        setTiming.TryGetValue(dbf, out var list) ? list.FirstOrDefault(t => calendar.IsActive(t.Event)).Set : 0;

    /// <summary>(Buy, Sell, Upgrade) for a card in a premium, or null when it can't be crafted / disenchanted now.</summary>
    public (int Buy, int Sell, int Upgrade)? Value(int dbf, int premium) => Value(dbf, premium, out _);

    /// <param name="refusal">why not: SOULBOUND (no value, hero skin, "never") or EVENT_NOT_ACTIVE (its event is off now)</param>
    public (int Buy, int Sell, int Upgrade)? Value(int dbf, int premium, out BoughtSoldCard.Result refusal)
    {
        refusal = BoughtSoldCard.Result.SOULBOUND;
        Engine.CardDef card = cards.ByDbf(dbf);
        if (card == null || card.Type == TAG_CARDTYPE.HERO && heroSkins.Contains(dbf)) return null; // hero skins (hero cards are cards)
        if (SetOf(dbf) == ClassicSet && counterparts.TryGetValue(dbf, out var twins) && twins.FirstOrDefault(t => SetOf(t) == LegacySet) is > 0 and int legacy &&
            cards.ByDbf(legacy) is { } counterpart)
            (card, dbf) = (counterpart, legacy);
        if (!values.TryGetValue((card.Tag(GAME_TAG.RARITY), premium), out var v)) return null;

        string craftingEvent = events.TryGetValue(dbf, out string[] e) ? e[premium] : null;
        if (calendar.NameOf(craftingEvent) == null) craftingEvent = setLaunch.GetValueOrDefault(SetOf(dbf));
        if (!calendar.IsActive(craftingEvent))
        {
            if (calendar.NameOf(craftingEvent) is not (null or "never")) refusal = BoughtSoldCard.Result.EVENT_NOT_ACTIVE;
            return null;
        }
        // 0 in an override = the base value (a nerf refund sells for Buy)
        if (overrides.TryGetValue((dbf, premium), out var list))
            foreach (var o in list)
                if (calendar.IsActive(o.Event))
                {
                    v = (o.Buy != 0 ? o.Buy : v.Buy, o.Sell != 0 ? o.Sell : o.SellState == "RECENTLY_NERFED_USE_BUY_VALUE" ? v.Buy : v.Sell, v.Upgrade);
                    break;
                }
        return v;
    }

    public int DeckLimit(int dbf) => cards.ByDbf(dbf)?.Tag(GAME_TAG.RARITY) == (int)TAG_RARITY.LEGENDARY ? 1 : 2;
}

/// <summary>
/// A player's card collection and currencies (roadmap B2): copies per (card, premium), arcane dust, gold, and the
/// CollectionVersion / CurrencyVersion the client tracks (both only ever grow; the client ignores an older one).
/// Server/state/accounts/&lt;name&gt;/collection.json, written on every change. Login sends it in 713 / 714; crafting answers
/// 258 / 269 and pushes the change as a 333 ClientStateNotification (what the client applies).
/// </summary>
internal sealed class CollectionStore
{
    public const int Normal = 0, Golden = 1, Diamond = 2, Signature = 3;

    private sealed class State
    {
        public long CollectionVersion { get; set; } = 1;
        public long CurrencyVersion { get; set; } = 1;
        public long Dust { get; set; }
        public long Gold { get; set; }
        // C1: the other login-state currencies (null until the first login: then the recorded ones are adopted)
        public long? Renown { get; set; }
        public long? BgTokens { get; set; }
        public long? Bmec { get; set; }
        public long? ArenaTickets { get; set; }
        // C3: the shop's currencies (XSH gold, XSR runestones); live's balances are adopted the first time the shop asks
        public bool GoldAdopted { get; set; }
        // C3: owned cosmetics. Card backs and coins: the recorded login's lists adopted once (null until then).
        // Battlegrounds (hero skins, guide skins, boards, finishers, emotes by their dbf ids): only what is granted
        // here; the defaults are owned by every player without being listed.
        public List<int> OwnedCardBacks { get; set; }
        public List<int> OwnedCoins { get; set; }
        public List<int> BgHeroSkins { get; set; } = new();
        public List<int> BgGuideSkins { get; set; } = new();
        public List<int> BgBoardSkins { get; set; } = new();
        public List<int> BgFinishers { get; set; } = new();
        public List<int> BgEmotes { get; set; } = new();
        // C3: owned account licenses (license id → its CasId; flags OWNED). Shop ownership, sequence bundles and
        // bonuses hang on them. Notices that announce them need increasing ids.
        public Dictionary<long, long> Licenses { get; set; } = new();
        public long NextNoticeId { get; set; } = 1_000_000_000;
        public long? Runestones { get; set; }
        // C2: unopened packs per pack type (the recorded login's are adopted once), their pity state, and the copies of
        // each card ever received (duplicate protection counts crafted and disenchanted cards too; null until first used)
        public Dictionary<int, PackState> Packs { get; set; } = new();
        public bool PacksAdopted { get; set; }
        public Dictionary<int, int> Collected { get; set; }
        public List<Stack> Cards { get; set; } = new();
        // Favorites (null until the first login: then the recorded ones are adopted)
        public List<int> FavoriteCardBacks { get; set; }
        public List<int[]> FavoriteHeroes { get; set; } // [class id, hero dbf, premium]
        public List<int> FavoriteCoins { get; set; }
        // C4: reward tracks by track id (null until the recorded login's are adopted), and the login update's own
        // track id (live sends the Battlegrounds track there)
        public Dictionary<int, RewardTrackState> RewardTracks { get; set; }
        public int RewardTrackLoginId { get; set; }
        // Zilliax Deluxe 3000's saved versions [module 1, module 2, portrait], newest first (null until live's are adopted)
        public List<int[]> ZilliaxBuilds { get; set; }
        // C4: quests and quest pools (Lobby/Quests.cs)
        public QuestBook Quests { get; set; }
        // D2: Lucky Draw boxes (Lobby/LuckyDraw.cs)
        public LuckyDrawBook LuckyDraws { get; set; }
        // D1: Black Market prices, purchases, haggles (Lobby/BlackMarket.cs)
        public BlackMarketBook BlackMarket { get; set; }
        // Ranked ladder per format (Lobby/Ranked.cs)
        public RankedBook Ranked { get; set; }
        // The client's settings it saves on the server (SetOptions 239), as a serialized ClientOptions (null: none yet)
        public string ClientOptions { get; set; }
        // Profile notices the player dismissed (AckNotice 213): not sent again
        public List<long> AckedNotices { get; set; }
        // Notices the server made that stay in the login 714 until the player dismisses them (serialized ProfileNotice,
        // base64): the Ranked season-end chest (GAINED_MEDAL)
        public List<string> PendingNotices { get; set; }
        // Win / loss records [game type, hero dbf (0: the type's total), wins, losses, ties] (PlayerRecords 270; live's
        // adopted once)
        public List<int[]> Records { get; set; }
        // Class levels [class id, level, XP into the level] (HeroXP 283; live's adopted once)
        public List<int[]> ClassXp { get; set; }
    }

    private sealed class Stack
    {
        public int Dbf { get; set; }
        public int Premium { get; set; }
        public int Count { get; set; }
        public int Seen { get; set; }
    }

    private readonly object gate = new();
    private State state = new();
    private readonly string path;

    public CollectionStore(string path) => this.path = path;

    public long Dust { get { lock (gate) return state.Dust; } }
    public long CurrencyVersion { get { lock (gate) return state.CurrencyVersion; } }

    /// <summary>(distinct cards, copies) owned, all premiums.</summary>
    public (int Stacks, int Copies) Totals()
    {
        lock (gate) return (state.Cards.Count(s => s.Count > 0), state.Cards.Sum(s => s.Count));
    }
    public long CollectionVersion { get { lock (gate) return state.CollectionVersion; } }

    public int Count(int dbf, int premium)
    {
        lock (gate) return Find(dbf, premium)?.Count ?? 0;
    }

    /// <summary>Deck cards (dbf per copy) the player doesn't own enough copies of, any premium counting: (dbf, copies
    /// missing). Live saves such a deck (the client greys the missing cards) but won't queue it.</summary>
    public List<(int Dbf, int Missing)> Missing(IEnumerable<int> deck)
    {
        lock (gate)
            return deck.GroupBy(d => d)
                .Select(g => (Dbf: g.Key, Missing: g.Count() - new[] { Normal, Golden, Diamond, Signature }.Sum(p => Find(g.Key, p)?.Count ?? 0)))
                .Where(m => m.Missing > 0).ToList();
    }

    public bool Load()
    {
        if (path == null || !File.Exists(path)) return false;
        lock (gate) state = JsonSerializer.Deserialize<State>(File.ReadAllText(path)) ?? new State();
        return true;
    }

    private void Save()
    {
        if (path == null) return;
        StateFiles.WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(state));
    }

    /// <summary>A new player's collection: the recorded one (the reference session's 713/714), versions included.</summary>
    public void Seed(Collection recorded, PegasusUtil.GameCurrencyStates currency)
    {
        lock (gate)
        {
            state = new State();
            if (recorded != null)
            {
                state.CollectionVersion = Math.Max(1, recorded.CollectionVersion);
                foreach (CardStack s in recorded.Stacks)
                    if (s.CardDef != null && s.Count > 0)
                        Add(s.CardDef.Asset, s.CardDef.Premium, s.Count, s.NumSeen);
            }
            if (currency != null)
            {
                state.Dust = currency.ArcaneDustBalance;
                state.Gold = currency.CappedGoldBalance + currency.BonusGoldBalance;
                if (currency.HasRenownBalance) state.Renown = currency.RenownBalance;
                if (currency.HasBgTokenBalance) state.BgTokens = currency.BgTokenBalance;
                if (currency.HasBmecBalance) state.Bmec = currency.BmecBalance;
                state.CurrencyVersion = Math.Max(1, currency.CurrencyVersion);
            }
            Save();
        }
    }

    /// <summary>Cards everyone owns (the free Core set): raise the normal copies to at least <c>Count</c>.</summary>
    public void Grant(IReadOnlyList<(int Dbf, int Count)> grants)
    {
        if (grants == null) return;
        lock (gate)
        {
            bool changed = false;
            foreach (var (dbf, count) in grants)
            {
                Stack s = Find(dbf, Normal);
                if (s == null) { Add(dbf, Normal, count, count); changed = true; }
                else if (s.Count < count) { s.Count = count; s.Seen = Math.Max(s.Seen, count); changed = true; }
            }
            if (changed) { state.CollectionVersion++; Save(); }
        }
    }

    /// <summary>The login collection (713): every owned stack and the current version.</summary>
    public Collection ToCollection(Date date)
    {
        lock (gate)
        {
            var c = new Collection { Stacks = new List<CardStack>(), CollectionVersion = state.CollectionVersion, CollectionVersionLastModified = state.CollectionVersion };
            // (2026-11-14: a CardStack needs its LatestInsertDate — with no recorded one to lend, now)
            DateTime now = DateTime.UtcNow;
            date ??= new Date { Year = now.Year, Month = now.Month, Day = now.Day, Hours = now.Hour, Min = now.Minute, Sec = now.Second };
            foreach (Stack s in state.Cards.Where(s => s.Count > 0))
            {
                var stack = new CardStack { CardDef = new PegasusShared.CardDef { Asset = s.Dbf, Premium = s.Premium }, Count = s.Count, NumSeen = s.Seen };
                if (date != null) stack.LatestInsertDate = date;
                c.Stacks.Add(stack);
            }
            return c;
        }
    }

    /// <summary>The player's currencies in the login state (714) and in every change (333): dust, renown,
    /// Battlegrounds tokens, event currency (BMEC). A balance the player doesn't have yet is adopted from
    /// <paramref name="currency"/> (the recorded login) the first time. Gold isn't here: the client takes it from the
    /// shop's Commerce service.</summary>
    public void ApplyTo(PegasusUtil.GameCurrencyStates currency)
    {
        lock (gate)
        {
            bool adopted = false;
            if (state.Renown == null && currency.HasRenownBalance) { state.Renown = currency.RenownBalance; adopted = true; }
            if (state.BgTokens == null && currency.HasBgTokenBalance) { state.BgTokens = currency.BgTokenBalance; adopted = true; }
            if (state.Bmec == null && currency.HasBmecBalance) { state.Bmec = currency.BmecBalance; adopted = true; }
            if (adopted) Save();
            currency.ArcaneDustBalance = state.Dust;
            if (state.Renown is { } renown) currency.RenownBalance = renown;
            if (state.BgTokens is { } tokens) currency.BgTokenBalance = tokens;
            if (state.Bmec is { } bmec) currency.BmecBalance = bmec;
            currency.CurrencyVersion = state.CurrencyVersion;
        }
    }

    /// <summary>The player's Arena tickets in the login state (714); adopted from the recorded login the first time.</summary>
    public void ApplyTo(PegasusUtil.PlayerDraftTickets tickets)
    {
        lock (gate)
        {
            if (state.ArenaTickets == null) { state.ArenaTickets = tickets.UnusedTicketBalance; Save(); }
            tickets.UnusedTicketBalance = (int)state.ArenaTickets.Value;
        }
    }

    /// <summary>The currencies the server keeps (C1). Gold and runestones live in the shop's Commerce service.</summary>
    public enum Currency { Dust, Renown, BgTokens, Bmec, ArenaTickets, Gold, Runestones }

    /// <summary>The shop's balance of a Commerce currency (XSH gold, XSR runestones); <paramref name="recorded"/> (live's)
    /// is adopted the first time.</summary>
    public long ShopBalance(string currencyCode, long recorded)
    {
        lock (gate)
        {
            switch (currencyCode)
            {
                case "XSH":
                    if (!state.GoldAdopted) { state.Gold = recorded; state.GoldAdopted = true; Save(); }
                    return state.Gold;
                case "XSR":
                    if (state.Runestones == null) { state.Runestones = recorded; Save(); }
                    return state.Runestones.Value;
                default:
                    return 0;
            }
        }
    }

    public long Balance(Currency currency)
    {
        lock (gate)
            return currency switch
            {
                Currency.Dust => state.Dust,
                Currency.Renown => state.Renown ?? 0,
                Currency.BgTokens => state.BgTokens ?? 0,
                Currency.Bmec => state.Bmec ?? 0,
                Currency.ArenaTickets => state.ArenaTickets ?? 0,
                Currency.Gold => state.Gold,
                Currency.Runestones => state.Runestones ?? 0,
                _ => 0,
            };
    }

    /// <summary>Set a balance (not below 0). The 333 to push: the login-state currencies as a CurrencyState, Arena
    /// tickets as PlayerDraftTickets. Gold / runestones live in the shop's Commerce balance, but a CurrencyState is pushed
    /// for them too: the client plugin then marks them stale and the client asks the shop again.</summary>
    public ClientStateNotification SetBalance(Currency currency, long amount)
    {
        amount = Math.Max(0, amount);
        lock (gate)
        {
            switch (currency)
            {
                case Currency.Dust: state.Dust = amount; break;
                case Currency.Renown: state.Renown = amount; break;
                case Currency.BgTokens: state.BgTokens = amount; break;
                case Currency.Bmec: state.Bmec = amount; break;
                case Currency.ArenaTickets: state.ArenaTickets = amount; break;
                case Currency.Gold: state.Gold = amount; state.GoldAdopted = true; break;
                case Currency.Runestones: state.Runestones = amount; break;
            }
            if (currency is Currency.ArenaTickets)
            {
                Save();
                return new ClientStateNotification { PlayerDraftTickets = new PegasusUtil.PlayerDraftTickets { UnusedTicketBalance = (int)amount } };
            }
            state.CurrencyVersion++;
            Save();
            var states = new PegasusUtil.GameCurrencyStates();
            ApplyTo(states);
            return new ClientStateNotification { CurrencyState = states };
        }
    }

    /// <summary>
    /// 392 CraftingTransaction: create / disenchant / upgrade copies of one card. Checked like live: the client's
    /// expected counts must match what the player owns (COUNT_MISMATCH), its expected dust change must match the value
    /// table (WRONG_BUY_PRICE / WRONG_SELL_PRICE), the card must be craftable in that premium (SOULBOUND) and the dust
    /// must be there (GENERIC_FAILURE). Returns the 258 answer and, when something changed, the 333 to push.
    /// </summary>
    public (BoughtSoldCard Reply, ClientStateNotification Change) Craft(CraftingTransaction t, CraftingRules rules)
    {
        int dbf = t.Def?.Asset ?? 0;
        BoughtSoldCard Fail(BoughtSoldCard.Result result) =>
            new() { Def = t.Def, Amount = 0, Result_ = result, CollectionVersion = CollectionVersion };
        lock (gate)
        {
            if (t.ExpectedNormalOwnedCount != Count(dbf, Normal) || t.ExpectedGoldenOwnedCount != Count(dbf, Golden) ||
                t.ExpectedSignatureOwnedCount != Count(dbf, Signature) || t.ExpectedDiamondOwnedCount != Count(dbf, Diamond))
                return (Fail(BoughtSoldCard.Result.COUNT_MISMATCH), null);
            int[] counts = { t.NormalCreateCount, t.NormalDisenchantCount, t.GoldenCreateCount, t.GoldenDisenchantCount,
                t.GoldenUpgradeFromNormalCount, t.GoldenUpgradeFromNothingCount, t.SignatureDisenchantCount, t.DiamondDisenchantCount };
            if (counts.Any(c => c < 0) || counts.All(c => c == 0)) return (Fail(BoughtSoldCard.Result.GENERIC_FAILURE), null);

            var normal = rules.Value(dbf, Normal, out var whyNormal);
            var golden = rules.Value(dbf, Golden, out var whyGolden);
            var signature = rules.Value(dbf, Signature, out var whySignature);
            var diamond = rules.Value(dbf, Diamond, out var whyDiamond);
            bool creates = t.NormalCreateCount + t.GoldenCreateCount + t.GoldenUpgradeFromNormalCount + t.GoldenUpgradeFromNothingCount > 0;
            if (t.NormalCreateCount + t.NormalDisenchantCount + t.GoldenUpgradeFromNormalCount + t.GoldenUpgradeFromNothingCount > 0 && normal == null)
                return (Fail(whyNormal), null);
            if (t.GoldenCreateCount + t.GoldenDisenchantCount + t.GoldenUpgradeFromNormalCount + t.GoldenUpgradeFromNothingCount > 0 && golden == null)
                return (Fail(whyGolden), null);
            if (t.SignatureDisenchantCount > 0 && signature == null) return (Fail(whySignature), null);
            if (t.DiamondDisenchantCount > 0 && diamond == null) return (Fail(whyDiamond), null);

            // ExpectedDustDelta is the transaction's COST (positive = dust spent), as the client's
            // CraftingPendingTransaction.GetExpectedTransactionCost; a golden from nothing = normal Buy + normal Upgrade.
            long cost = 0;
            if (normal is { } n) cost += n.Buy * (long)t.NormalCreateCount - n.Sell * (long)t.NormalDisenchantCount +
                                         n.Upgrade * (long)t.GoldenUpgradeFromNormalCount + (n.Buy + (long)n.Upgrade) * t.GoldenUpgradeFromNothingCount;
            if (golden is { } g) cost += g.Buy * (long)t.GoldenCreateCount - g.Sell * (long)t.GoldenDisenchantCount;
            if (signature is { } s) cost -= s.Sell * (long)t.SignatureDisenchantCount;
            if (diamond is { } d) cost -= d.Sell * (long)t.DiamondDisenchantCount;
            if (cost != t.ExpectedDustDelta)
                return (Fail(creates ? BoughtSoldCard.Result.WRONG_BUY_PRICE : BoughtSoldCard.Result.WRONG_SELL_PRICE), null);
            if (state.Dust < cost) return (Fail(BoughtSoldCard.Result.GENERIC_FAILURE), null);
            if (t.NormalDisenchantCount + t.GoldenUpgradeFromNormalCount > Count(dbf, Normal) || t.GoldenDisenchantCount > Count(dbf, Golden) ||
                t.SignatureDisenchantCount > Count(dbf, Signature) || t.DiamondDisenchantCount > Count(dbf, Diamond))
                return (Fail(BoughtSoldCard.Result.COUNT_MISMATCH), null);

            var mods = new List<CardModification>();
            void Change(int premium, int quantity)
            {
                if (quantity == 0) return;
                Add(dbf, premium, quantity, quantity > 0 ? quantity : 0);
                mods.Add(new CardModification { AssetCardId = dbf, Premium = premium, Quantity = quantity, AmountSeen = quantity > 0 ? quantity : 0 });
            }
            Change(Normal, t.NormalCreateCount - t.NormalDisenchantCount - t.GoldenUpgradeFromNormalCount);
            Change(Golden, t.GoldenCreateCount + t.GoldenUpgradeFromNormalCount + t.GoldenUpgradeFromNothingCount - t.GoldenDisenchantCount);
            Change(Signature, -t.SignatureDisenchantCount);
            Change(Diamond, -t.DiamondDisenchantCount);
            state.Dust -= cost;
            ClientStateNotification change = Committed(mods);

            var result = t.GoldenUpgradeFromNormalCount > 0 ? BoughtSoldCard.Result.UPGRADE
                : creates ? BoughtSoldCard.Result.BOUGHT : BoughtSoldCard.Result.SOLD;
            int amount = counts.Sum();
            return (new BoughtSoldCard { Def = t.Def, Amount = amount, Result_ = result, CollectionVersion = state.CollectionVersion }, change);
        }
    }

    /// <summary>
    /// 268 MassDisenchantRequest: every copy beyond a card's deck limit (all premiums together, the fanciest copies
    /// kept: diamond, signature, golden, then normal) is disenchanted, for craftable cards only. Returns the 269 answer
    /// (Amount = dust gained; 0 = nothing) and the 333 to push.
    /// </summary>
    public (MassDisenchantResponse Reply, ClientStateNotification Change) MassDisenchant(CraftingRules rules)
    {
        lock (gate)
        {
            var mods = new List<CardModification>();
            long gained = 0;
            int[] keepOrder = { Diamond, Signature, Golden, Normal };
            foreach (var group in state.Cards.Where(s => s.Count > 0).GroupBy(s => s.Dbf).ToList())
            {
                int keep = rules.DeckLimit(group.Key);
                foreach (int premium in keepOrder)
                {
                    Stack s = group.FirstOrDefault(x => x.Premium == premium);
                    if (s == null || s.Count == 0) continue;
                    int kept = Math.Min(keep, s.Count);
                    keep -= kept;
                    int extra = s.Count - kept;
                    if (extra == 0 || rules.Value(group.Key, premium) is not { } v) continue;
                    s.Count -= extra;
                    gained += v.Sell * (long)extra;
                    mods.Add(new CardModification { AssetCardId = group.Key, Premium = premium, Quantity = -extra });
                }
            }
            if (mods.Count == 0)
                return (new MassDisenchantResponse { Amount = 0, CollectionVersion = state.CollectionVersion }, null);
            state.Dust += gained;
            ClientStateNotification change = Committed(mods);
            return (new MassDisenchantResponse { Amount = (int)gained, CollectionVersion = state.CollectionVersion }, change);
        }
    }

    // ---------- favorites (card back, hero per class, coin) and "seen" ----------

    /// <summary>The login card backs (236): the player's favorites (the recorded ones the first time).</summary>
    public void ApplyTo(CardBacks backs)
    {
        lock (gate)
        {
            if (state.FavoriteCardBacks == null) { state.FavoriteCardBacks = backs.FavoriteCardBacks.ToList(); Save(); }
            backs.FavoriteCardBacks = state.FavoriteCardBacks.ToList();
            if (state.OwnedCardBacks == null) { state.OwnedCardBacks = backs.CardBacks_.ToList(); Save(); }
            backs.CardBacks_ = state.OwnedCardBacks.ToList();
        }
    }

    public void ApplyTo(FavoriteHeroesResponse heroes)
    {
        lock (gate)
        {
            if (state.FavoriteHeroes == null)
            {
                state.FavoriteHeroes = heroes.FavoriteHeroes.Select(f => new[] { f.ClassId, f.Hero?.Asset ?? 0, f.Hero?.Premium ?? 0 }).ToList();
                Save();
            }
            heroes.FavoriteHeroes = state.FavoriteHeroes.Select(f => new PegasusShared.FavoriteHero
            {
                ClassId = f[0], Hero = new PegasusShared.CardDef { Asset = f[1], Premium = f[2] },
            }).ToList();
        }
    }

    public void ApplyTo(CosmeticCoins coins)
    {
        lock (gate)
        {
            if (state.FavoriteCoins == null) { state.FavoriteCoins = coins.FavoriteCoins.ToList(); Save(); }
            coins.FavoriteCoins = state.FavoriteCoins.ToList();
            if (state.OwnedCoins == null) { state.OwnedCoins = coins.Coins.ToList(); Save(); }
            coins.Coins = state.OwnedCoins.ToList();
        }
    }

    /// <summary>291 SetFavoriteCardBack → 292.</summary>
    public SetFavoriteCardBackResponse SetFavoriteCardBack(SetFavoriteCardBack request)
    {
        lock (gate)
        {
            state.FavoriteCardBacks ??= new List<int>();
            state.FavoriteCardBacks.Remove(request.CardBack);
            if (request.IsFavorite) state.FavoriteCardBacks.Add(request.CardBack);
            Save();
        }
        return new SetFavoriteCardBackResponse { Success = true, CardBack = request.CardBack, IsFavorite = request.IsFavorite };
    }

    /// <summary>319 SetFavoriteHero → 320.</summary>
    public SetFavoriteHeroResponse SetFavoriteHero(SetFavoriteHero request)
    {
        PegasusShared.FavoriteHero hero = request.FavoriteHero;
        if (hero?.Hero == null) return new SetFavoriteHeroResponse { Success = false, FavoriteHero = hero, IsFavorite = request.IsFavorite };
        lock (gate)
        {
            state.FavoriteHeroes ??= new List<int[]>();
            state.FavoriteHeroes.RemoveAll(f => f[0] == hero.ClassId && f[1] == hero.Hero.Asset && f[2] == hero.Hero.Premium);
            if (request.IsFavorite) state.FavoriteHeroes.Add(new[] { hero.ClassId, hero.Hero.Asset, hero.Hero.Premium });
            Save();
        }
        return new SetFavoriteHeroResponse { Success = true, FavoriteHero = hero, IsFavorite = request.IsFavorite };
    }

    /// <summary>The player's favorite coins (COSMETIC_COIN ids; empty: none chosen yet).</summary>
    public List<int> FavoriteCoinIds()
    {
        lock (gate) return state.FavoriteCoins?.ToList() ?? new List<int>();
    }

    /// <summary>609 SetFavoriteCosmeticCoin → 610.</summary>
    public SetFavoriteCosmeticCoinResponse SetFavoriteCoin(SetFavoriteCosmeticCoin request)
    {
        lock (gate)
        {
            state.FavoriteCoins ??= new List<int>();
            state.FavoriteCoins.Remove(request.Coin);
            if (request.IsFavorite) state.FavoriteCoins.Add(request.Coin);
            Save();
        }
        return new SetFavoriteCosmeticCoinResponse { Success = true, CoinId = request.Coin, IsFavorite = request.IsFavorite };
    }

    /// <summary>223 AckCardSeen: these cards are no longer "new" (the collection's glow).</summary>
    public void Seen(AckCardSeen ack)
    {
        lock (gate)
        {
            bool changed = false;
            foreach (PegasusShared.CardDef def in ack.CardDefs)
                if (Find(def.Asset, def.Premium) is { } s && s.Seen < s.Count) { s.Seen = s.Count; changed = true; }
            if (changed) Save();
        }
    }

    // ---------- admin (E1) ----------

    /// <summary>Add copies (a negative count removes, down to 0). The 333 to push; null when nothing changed. Given
    /// cards are unseen (they glow as new in the collection).</summary>
    public ClientStateNotification Give(int dbf, int premium, int count)
    {
        lock (gate)
        {
            int before = Count(dbf, premium);
            Add(dbf, premium, count, 0);
            int delta = Count(dbf, premium) - before;
            if (delta == 0) return null;
            return Committed(new List<CardModification> { new() { AssetCardId = dbf, Premium = premium, Quantity = delta, AmountSeen = 0 } });
        }
    }

    /// <summary>Set the dust balance (not below 0). The 333 to push (the currency part only).</summary>
    public ClientStateNotification SetDust(long dust) => SetBalance(Currency.Dust, dust);

    /// <summary>Start over from the seed, keeping the versions growing (the client ignores an older one).</summary>
    public void Reseed(Action<CollectionStore> seed, IReadOnlyList<(int Dbf, int Count)> grants)
    {
        lock (gate)
        {
            long collectionVersion = state.CollectionVersion, currencyVersion = state.CurrencyVersion;
            seed?.Invoke(this);
            Grant(grants);
            state.CollectionVersion = Math.Max(state.CollectionVersion, collectionVersion + 1);
            state.CurrencyVersion = Math.Max(state.CurrencyVersion, currencyVersion + 1);
            Save();
        }
    }

    // ---------- cosmetics (C3) ----------

    public enum Cosmetic { CardBack, Coin, BgHeroSkin, BgGuideSkin, BgBoardSkin, BgFinisher, BgEmote }

    private List<int> ListOf(Cosmetic kind) => kind switch
    {
        Cosmetic.CardBack => state.OwnedCardBacks ??= new List<int>(),
        Cosmetic.Coin => state.OwnedCoins ??= new List<int>(),
        Cosmetic.BgHeroSkin => state.BgHeroSkins ??= new List<int>(),
        Cosmetic.BgGuideSkin => state.BgGuideSkins ??= new List<int>(),
        Cosmetic.BgBoardSkin => state.BgBoardSkins ??= new List<int>(),
        Cosmetic.BgFinisher => state.BgFinishers ??= new List<int>(),
        _ => state.BgEmotes ??= new List<int>(),
    };

    public bool Owns(Cosmetic kind, int id)
    {
        lock (gate) return ListOf(kind).Contains(id);
    }

    public IReadOnlyList<int> Owned(Cosmetic kind)
    {
        lock (gate) return ListOf(kind).ToList();
    }

    /// <summary>Grant a cosmetic. The message to push: a 333 with the matching modification, or CoinUpdate (611) for coins
    /// (they have no 333 field); null when already owned.</summary>
    public IProtoBuf GrantCosmetic(Cosmetic kind, int id)
    {
        lock (gate)
        {
            List<int> owned = ListOf(kind);
            if (owned.Contains(id)) return null;
            owned.Add(id);
            Save();
        }
        return kind switch
        {
            Cosmetic.CardBack => new ClientStateNotification
            {
                CardBackModifications = new CardBackModifications { CardBackModifications_ = { new CardBackModification { AssetCardBackId = id, AutoSetAsFavorite = false } } },
            },
            Cosmetic.Coin => new CoinUpdate { AddCoinId = { id } },
            Cosmetic.BgHeroSkin => new ClientStateNotification
            {
                BattlegroundsHeroSkinModifications = new BattlegroundsHeroSkinModifications
                    { BattlegroundsHeroSkinModifications_ = { new BattlegroundsHeroSkinModification { BattlegroundsHeroSkinId = id, AddBattlegroundsHeroSkin = true } } },
            },
            Cosmetic.BgGuideSkin => new ClientStateNotification
            {
                BattlegroundsGuideSkinModifications = new BattlegroundsGuideSkinModifications
                    { BattlegroundsGuideSkinModifications_ = { new BattlegroundsGuideSkinModification { BattlegroundsGuideSkinId = id, AddBattlegroundsGuideSkin = true } } },
            },
            Cosmetic.BgBoardSkin => new ClientStateNotification
            {
                BattlegroundsBoardSkinModifications = new BattlegroundsBoardSkinModifications
                    { BattlegroundsBoardSkinModifications_ = { new BattlegroundsBoardSkinModification { BattlegroundsBoardSkinId = id, AddBattlegroundsBoardSkin = true } } },
            },
            Cosmetic.BgFinisher => new ClientStateNotification
            {
                BattlegroundsFinisherModifications = new BattlegroundsFinisherModifications
                    { BattlegroundsFinisherModifications_ = { new BattlegroundsFinisherModification { BattlegroundsFinisherId = id, AddBattlegroundsFinisher = true } } },
            },
            // the client's add branch tests HasRemoveBattlegroundsEmote: "remove = false" must be sent explicitly
            _ => new ClientStateNotification
            {
                BattlegroundsEmoteModifications = new BattlegroundsEmoteModifications
                    { BattlegroundsEmoteModifications_ = { new BattlegroundsEmoteModification { BattlegroundsEmoteId = id, AddBattlegroundsEmote = true, RemoveBattlegroundsEmote = false } } },
            },
        };
    }

    // ---------- Zilliax saved versions ----------

    private const int MaxZilliaxBuilds = 50;

    /// <summary>The player's Zilliax builds, newest first; <paramref name="recorded"/> (live's) adopted the first time.</summary>
    public List<int[]> ZilliaxHistory(IReadOnlyList<int[]> recorded)
    {
        lock (gate)
        {
            if (state.ZilliaxBuilds == null) { state.ZilliaxBuilds = (recorded ?? new List<int[]>()).Select(b => b.ToArray()).ToList(); Save(); }
            return state.ZilliaxBuilds.Select(b => b.ToArray()).ToList();
        }
    }

    /// <summary>A build was saved in a deck: it moves to the front (distinct builds; module order counts, as live's).</summary>
    public void RememberZilliax(int[] build, IReadOnlyList<int[]> recorded)
    {
        lock (gate)
        {
            state.ZilliaxBuilds ??= (recorded ?? new List<int[]>()).Select(b => b.ToArray()).ToList();
            state.ZilliaxBuilds.RemoveAll(b => b.SequenceEqual(build));
            state.ZilliaxBuilds.Insert(0, build.ToArray());
            if (state.ZilliaxBuilds.Count > MaxZilliaxBuilds) state.ZilliaxBuilds.RemoveRange(MaxZilliaxBuilds, state.ZilliaxBuilds.Count - MaxZilliaxBuilds);
            Save();
        }
    }

    // ---------- reward tracks (C4) ----------

    /// <summary>The login's PlayerRewardTrackStateUpdate (614): the recorded one is adopted the first time, then the
    /// player's own tracks are sent.</summary>
    public PegasusUtil.PlayerRewardTrackStateUpdate RewardTrackLogin(PegasusUtil.PlayerRewardTrackStateUpdate recorded)
    {
        lock (gate)
        {
            if (state.RewardTracks == null)
            {
                state.RewardTracks = new();
                state.RewardTrackLoginId = recorded?.RewardTrackId ?? 0;
                foreach (PegasusUtil.PlayerRewardTrackState t in recorded?.State ?? new List<PegasusUtil.PlayerRewardTrackState>())
                {
                    if (t.RewardTrackId == 0) continue;
                    var track = state.RewardTracks.TryGetValue(t.RewardTrackId, out var known) ? known : state.RewardTracks[t.RewardTrackId] = new RewardTrackState();
                    // a track can be listed twice (live: 183 with and without its level); the fullest one wins
                    if (t.HasLevel && t.Level >= track.Level) { track.Level = t.Level; track.Xp = t.Xp; }
                    if (t.HasIsActiveRewardTrack) track.Active = t.IsActiveRewardTrack;
                    foreach (PegasusUtil.PlayerRewardTrackLevelState l in t.TrackLevel)
                        track.Levels[l.Level] = new[] { l.FreeRewardStatus, l.PaidRewardStatus, l.PaidPremiumRewardStatus };
                }
                Save();
            }
            var update = new PegasusUtil.PlayerRewardTrackStateUpdate { RewardTrackId = state.RewardTrackLoginId };
            foreach (var (id, track) in state.RewardTracks.OrderBy(t => t.Key))
                update.State.Add(track.ToState(id, withLevels: true));
            return update;
        }
    }

    /// <summary>Read or change one reward track (created at level 1 when new), saved afterwards.</summary>
    public T RewardTrack<T>(int trackId, Func<RewardTrackState, T> use)
    {
        lock (gate)
        {
            state.RewardTracks ??= new();
            if (!state.RewardTracks.TryGetValue(trackId, out var track))
                state.RewardTracks[trackId] = track = new RewardTrackState { Level = 1, Active = true };
            T result = use(track);
            Save();
            return result;
        }
    }

    // ---------- win / loss records ----------

    /// <summary>The login's PlayerRecords (270): live's adopted the first time, then the player's own.</summary>
    public PegasusUtil.PlayerRecords RecordsLogin(PegasusUtil.PlayerRecords recorded)
    {
        lock (gate)
        {
            if (state.Records == null)
            {
                state.Records = (recorded?.Records ?? new List<PegasusUtil.PlayerRecord>())
                    .Select(r => new[] { (int)r.Type, r.Data, r.Wins, r.Losses, r.Ties }).ToList();
                Save();
            }
            return RecordsMessage();
        }
    }

    /// <summary>A finished game: a win or loss on the hero's record and on the game type's total (live: both, then the
    /// whole list again, 270); returns that list.</summary>
    public PegasusUtil.PlayerRecords AddResult(int gameType, int heroDbf, bool won)
    {
        lock (gate)
        {
            state.Records ??= new();
            foreach (int data in new[] { heroDbf, 0 })
            {
                int[] record = state.Records.FirstOrDefault(r => r[0] == gameType && r[1] == data);
                if (record == null) state.Records.Add(record = new[] { gameType, data, 0, 0, 0 });
                record[won ? 2 : 3]++;
            }
            Save();
            return RecordsMessage();
        }
    }

    private PegasusUtil.PlayerRecords RecordsMessage()
    {
        var message = new PegasusUtil.PlayerRecords();
        foreach (int[] r in state.Records)
            message.Records.Add(new PegasusUtil.PlayerRecord { Type = (PegasusShared.GameType)r[0], Data = r[1], Wins = r[2], Losses = r[3], Ties = r[4] });
        return message;
    }

    // ---------- class levels ----------

    /// <summary>The player's class levels (HeroXP 283): live's adopted the first time, then the player's own.</summary>
    public PegasusUtil.HeroXP ClassXpLogin(PegasusUtil.HeroXP recorded)
    {
        lock (gate)
        {
            if (state.ClassXp == null)
            {
                // (2026-11-14) a new player with no recorded levels: every class at level 1 — the client counts a class as
                // unlocked at level > 0, and Arena opens with 3 (GameModeUtils.GetTotalDefaultHeroesUnlocked)
                state.ClassXp = recorded?.XpInfos is { Count: > 0 } infos
                    ? infos.Select(x => new[] { (int)x.ClassId, (int)x.Level, (int)x.CurrXp }).ToList()
                    : new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 14 }.Select(c => new[] { c, 1, 0 }).ToList();
                Save();
            }
            return ClassXpMessage();
        }
    }

    /// <summary>A finished game's class XP (ClassXpRules): levels up past each level's XP, stops at the cap; returns the
    /// levels before and after (the end screen's bar animates by comparing the HeroXP it asks for with the last one).</summary>
    public (int Level, int Xp, int NewLevel, int NewXp) AddClassXp(int classId, int xp)
    {
        lock (gate)
        {
            state.ClassXp ??= new();
            int[] entry = state.ClassXp.FirstOrDefault(x => x[0] == classId);
            if (entry == null) state.ClassXp.Add(entry = new[] { classId, 1, 0 });
            (int level, int had) = (entry[1], entry[2]);
            if (entry[1] < ClassXpRules.MaxLevel)
            {
                entry[2] += Math.Max(0, xp);
                while (entry[1] < ClassXpRules.MaxLevel && entry[2] >= ClassXpRules.XpForLevel(entry[1]))
                {
                    entry[2] -= ClassXpRules.XpForLevel(entry[1]);
                    entry[1]++;
                }
                if (entry[1] >= ClassXpRules.MaxLevel) entry[2] = ClassXpRules.MaxLevelXp;
            }
            Save();
            return (level, had, entry[1], entry[2]);
        }
    }

    private PegasusUtil.HeroXP ClassXpMessage()
    {
        var message = new PegasusUtil.HeroXP();
        foreach (int[] x in state.ClassXp)
            message.XpInfos.Add(new PegasusUtil.HeroXPInfo
            {
                ClassId = x[0], Level = x[1], CurrXp = x[2],
                MaxXp = x[1] >= ClassXpRules.MaxLevel ? ClassXpRules.MaxLevelXp : ClassXpRules.XpForLevel(x[1]),
            });
        return message;
    }

    // ---------- client settings and notices ----------

    /// <summary>SetOptions (239): the client's settings, merged by option index (live answers NOP).</summary>
    public void SetOptions(IEnumerable<PegasusUtil.ClientOption> options)
    {
        lock (gate)
        {
            PegasusUtil.ClientOptions stored = StoredOptions() ?? new PegasusUtil.ClientOptions();
            foreach (PegasusUtil.ClientOption o in options)
            {
                stored.Options.RemoveAll(x => x.Index == o.Index);
                stored.Options.Add(o);
            }
            state.ClientOptions = Convert.ToBase64String(PegasusPackets.Serialize(stored));
            Save();
        }
    }

    /// <summary>The login's ClientOptions (in the 714): the recorded ones with the player's own over them.</summary>
    public PegasusUtil.ClientOptions ApplyTo(PegasusUtil.ClientOptions recorded)
    {
        lock (gate)
        {
            PegasusUtil.ClientOptions stored = StoredOptions();
            if (stored == null) return recorded;
            var result = recorded ?? new PegasusUtil.ClientOptions();
            foreach (PegasusUtil.ClientOption o in stored.Options)
            {
                result.Options.RemoveAll(x => x.Index == o.Index);
                result.Options.Add(o);
            }
            return result;
        }
    }

    private PegasusUtil.ClientOptions StoredOptions() =>
        state.ClientOptions == null ? null : PegasusPackets.Deserialize<PegasusUtil.ClientOptions>(Convert.FromBase64String(state.ClientOptions));

    /// <summary>AckNotice (213): the player dismissed a profile notice; it isn't sent again.</summary>
    public void AckNotice(long entry)
    {
        lock (gate)
        {
            state.AckedNotices ??= new();
            if (!state.AckedNotices.Contains(entry)) { state.AckedNotices.Add(entry); Save(); }
            state.PendingNotices?.RemoveAll(n => Notice(n).Entry == entry);
        }
    }

    /// <summary>The login's notices (in the 714) without the ones the player dismissed, plus the server's own pending ones.</summary>
    public void ApplyTo(PegasusUtil.ProfileNotices notices)
    {
        lock (gate)
        {
            if (state.AckedNotices is { Count: > 0 } acked && notices?.List != null)
                notices.List.RemoveAll(n => acked.Contains(n.Entry));
            // (2026-11-01, user: "seasonal rank rewards each login") the recorded login's season chests are the LIVE account's (a new
            // live season replayed with every reference login); our own season-end chest is made by RankedLadder.SeasonEnd (pending)
            notices?.List?.RemoveAll(n => n.Medal != null); // (only recorded ones here: ours are added below)
            if (notices?.List != null && state.PendingNotices is { Count: > 0 } pending)
                foreach (string n in pending)
                    if (Notice(n) is var notice && notices.List.All(x => x.Entry != notice.Entry)) notices.List.Add(notice);
        }
    }

    /// <summary>A notice the server made: a new id, kept for later logins until dismissed; the 333 to push it now.</summary>
    public ClientStateNotification AddPendingNotice(ProfileNotice notice)
    {
        lock (gate)
        {
            notice.Entry = ++state.NextNoticeId;
            DateTime now = DateTime.UtcNow;
            notice.When ??= new Date { Year = now.Year, Month = now.Month, Day = now.Day, Hours = now.Hour, Min = now.Minute, Sec = now.Second };
            (state.PendingNotices ??= new()).Add(Convert.ToBase64String(HsServer.Protocol.PegasusPackets.Serialize(notice)));
            Save();
            return new ClientStateNotification
            {
                NoticeNotifications = new NoticeNotifications { NoticeNotifications_ = { new NoticeNotification { Notice = notice } } },
            };
        }
    }

    private static ProfileNotice Notice(string base64) =>
        HsServer.Protocol.PegasusPackets.Deserialize<ProfileNotice>(Convert.FromBase64String(base64));

    /// <summary>Read or change the player's quests (C4), saved afterwards.</summary>
    /// <summary>D2: read or change the player's Lucky Draw boxes (saved afterwards).</summary>
    public T LuckyDrawBook<T>(Func<LuckyDrawBook, T> use)
    {
        lock (gate)
        {
            state.LuckyDraws ??= new LuckyDrawBook();
            T result = use(state.LuckyDraws);
            Save();
            return result;
        }
    }

    public T QuestBook<T>(Func<QuestBook, T> use)
    {
        lock (gate)
        {
            state.Quests ??= new QuestBook();
            T result = use(state.Quests);
            Save();
            return result;
        }
    }

    // ---------- account licenses (C3) ----------

    /// <summary>The login list (AccountLicensesInfoResponse 325): every owned license, flags OWNED.</summary>
    public List<PegasusShared.AccountLicenseInfo> LicenseInfos()
    {
        lock (gate)
            return (state.Licenses ??= new()).Select(l => new PegasusShared.AccountLicenseInfo { License = l.Key, Flags_ = 1, CasId = l.Value }).ToList();
    }

    /// <summary>No longer own these licenses (admin resets); how many were owned.</summary>
    public int RevokeLicenses(IEnumerable<long> licenses)
    {
        lock (gate)
        {
            state.Licenses ??= new();
            int removed = licenses.Count(l => state.Licenses.Remove(l));
            Save();
            return removed;
        }
    }

    public bool OwnsLicense(long license)
    {
        lock (gate) return (state.Licenses ??= new()).ContainsKey(license);
    }

    /// <summary>Own a license. The 333 to push: a profile notice ACCOUNT_LICENSE (origin ACCOUNT_LICENSE_FLAGS 21,
    /// OriginData = flags 1 = owned, a new CasId), as AccountLicenseMgr.OnNewNotices_Internal applies it; null if owned.</summary>
    public ClientStateNotification GrantLicense(long license)
    {
        lock (gate)
        {
            state.Licenses ??= new();
            if (state.Licenses.ContainsKey(license)) return null;
            long notice = ++state.NextNoticeId;
            state.Licenses[license] = notice; // the CasId only has to grow
            Save();
            DateTime now = DateTime.UtcNow;
            return new ClientStateNotification
            {
                NoticeNotifications = new NoticeNotifications
                {
                    NoticeNotifications_ =
                    {
                        new NoticeNotification
                        {
                            Notice = new ProfileNotice
                            {
                                Entry = notice, Origin = 21, OriginData = 1,
                                When = new Date { Year = now.Year, Month = now.Month, Day = now.Day, Hours = now.Hour, Min = now.Minute, Sec = now.Second },
                                AccountLicense = new ProfileNoticeAccountLicense { License = license, CasId = notice },
                            },
                        },
                    },
                },
            };
        }
    }

    /// <summary>A "you received this card" profile notice (REWARD_CARD, origin FROM_PURCHASE 22): the client shows
    /// purchased card rewards one by one (RewardUtils.GetViewableRewards → PopupDisplayManager). The card itself comes
    /// with the CollectionModifications.</summary>
    public ClientStateNotification CardRewardNotice(int dbf, int premium, int quantity)
    {
        long id;
        lock (gate) { id = ++state.NextNoticeId; Save(); }
        DateTime now = DateTime.UtcNow;
        return new ClientStateNotification
        {
            NoticeNotifications = new NoticeNotifications
            {
                NoticeNotifications_ =
                {
                    new NoticeNotification
                    {
                        Notice = new ProfileNotice
                        {
                            Entry = id, Origin = 22,
                            When = new Date { Year = now.Year, Month = now.Month, Day = now.Day, Hours = now.Hour, Min = now.Minute, Sec = now.Second },
                            RewardCard = new ProfileNoticeRewardCard { Card = new PegasusShared.CardDef { Asset = dbf, Premium = premium }, Quantity = quantity },
                        },
                    },
                },
            },
        };
    }

    /// <summary>A REWARD_LIST profile notice (the client's reward popup for a list: ProfileNoticeRewardList) with the
    /// given origin (35 = NOTICE_ORIGIN_BLACK_MARKET_PURCHASE).</summary>
    public (long Id, ClientStateNotification Notice) RewardListNotice(int origin, int originalList, int selectedList, IEnumerable<int[]> outputs)
    {
        long id;
        lock (gate) { id = ++state.NextNoticeId; Save(); }
        DateTime now = DateTime.UtcNow;
        var list = new ProfileNoticeRewardList { OriginalRewardListId = originalList, SelectedRewardListId = selectedList };
        foreach (int[] o in outputs)
            list.RewardItemOutput.Add(new PegasusUtil.RewardItemOutput
            {
                RewardItemId = o[0],
                OutputData = new PegasusUtil.RewardItemOutputData { CardId = o[1], Premium = o[2], Amount = o[3], MercenaryId = 0, ArtVariationId = 0 },
            });
        return (id, new ClientStateNotification
        {
            NoticeNotifications = new NoticeNotifications
            {
                NoticeNotifications_ =
                {
                    new NoticeNotification
                    {
                        Notice = new ProfileNotice
                        {
                            Entry = id, Origin = origin,
                            When = new Date { Year = now.Year, Month = now.Month, Day = now.Day, Hours = now.Hour, Min = now.Minute, Sec = now.Second },
                            RewardList = list,
                        },
                    },
                },
            },
        });
    }

    /// <summary>Read or change the player's Ranked state (saved afterwards).</summary>
    public T RankedBook<T>(Func<RankedBook, T> use)
    {
        lock (gate)
        {
            state.Ranked ??= new RankedBook();
            T result = use(state.Ranked);
            Save();
            return result;
        }
    }

    /// <summary>D1: read or change the player's Black Market state (saved afterwards).</summary>
    public T BlackMarketBook<T>(Func<BlackMarketBook, T> use)
    {
        lock (gate)
        {
            state.BlackMarket ??= new BlackMarketBook();
            T result = use(state.BlackMarket);
            Save();
            return result;
        }
    }

    // ---------- packs (C2) ----------

    public sealed class PackState
    {
        public int Count { get; set; }
        public int EverGranted { get; set; }
        public BoosterRules.PityState Pity { get; set; } = new();
    }

    /// <summary>The login pack list (714 Boosters): the player's own; the recorded list is adopted the first time
    /// (its packs already opened on live count as opened, so the "first 10 packs" rule doesn't restart).</summary>
    public void ApplyTo(PegasusUtil.Boosters boosters)
    {
        lock (gate)
        {
            if (!state.PacksAdopted)
            {
                foreach (PegasusShared.BoosterInfo b in boosters.List)
                    if (!state.Packs.ContainsKey(b.Type))
                        state.Packs[b.Type] = new PackState
                        {
                            Count = b.Count, EverGranted = b.EverGrantedCount,
                            Pity = new BoosterRules.PityState
                            {
                                Opened = Math.Max(0, b.EverGrantedCount - b.Count),
                                Got = b.EverGrantedCount - b.Count >= 10 ? new HashSet<string> { BoosterRules.Slot.Legendary.ToString(), BoosterRules.Slot.GoldenLegendary.ToString() } : new(),
                            },
                        };
                state.PacksAdopted = true;
                Save();
            }
            boosters.List = state.Packs.OrderBy(p => p.Key)
                .Select(p => new PegasusShared.BoosterInfo { Type = p.Key, Count = p.Value.Count, EverGrantedCount = p.Value.EverGranted }).ToList();
        }
    }

    public int PackCount(int type)
    {
        lock (gate) return state.Packs.TryGetValue(type, out PackState p) ? p.Count : 0;
    }

    public List<(int Type, int Count)> PackList()
    {
        lock (gate) return state.Packs.Where(p => p.Value.Count > 0).OrderBy(p => p.Key).Select(p => (p.Key, p.Value.Count)).ToList();
    }

    /// <summary>Admin: set how many packs of a type the player has. The 333 (BoosterModifications: the change).</summary>
    public ClientStateNotification SetPacks(int type, int count)
    {
        count = Math.Max(0, count);
        lock (gate)
        {
            if (!state.Packs.TryGetValue(type, out PackState p)) state.Packs[type] = p = new PackState();
            int delta = count - p.Count;
            if (delta == 0) return null;
            p.Count = count;
            if (delta > 0) p.EverGranted += delta;
            Save();
            return new ClientStateNotification
            {
                BoosterModifications = new PegasusUtil.BoosterModifications
                {
                    Modifications = { new PegasusShared.BoosterInfo { Type = type, Count = delta, EverGrantedCount = Math.Max(0, delta) } },
                },
            };
        }
    }

    /// <summary>
    /// 225 OpenBooster: open <paramref name="packs"/> packs of a type (up to 40, golden 20, like live's mass opening).
    /// Live answers BoosterContent (226: the cards, new CollectionVersion, PacksOpened) then a 333 with the collection
    /// change and BoosterModifications {type, -n}. Null reply + the reason when it can't be opened.
    /// </summary>
    public (PegasusUtil.BoosterContent Reply, ClientStateNotification Change, string Error) OpenPacks(int type, int packs, BoosterRules rules, Random rng)
    {
        BoosterRules.Pack pack = rules.Get(type);
        if (pack == null) return (null, null, $"unknown pack type {type}");
        if (!rules.CanOpen(pack)) return (null, null, $"pack type {type} ({pack.Name}) can't be opened here yet");
        int limit = pack.Golden || pack.Catchup ? 20 : 40;
        if (packs < 1 || packs > limit) return (null, null, $"{packs} packs at once (1..{limit})");
        List<Engine.CardDef> pool = rules.Pool(pack);
        if (pool.Count == 0) return (null, null, $"pack type {type} ({pack.Name}) has no cards to give");
        lock (gate)
        {
            if (!state.Packs.TryGetValue(type, out PackState owned) || owned.Count < packs)
                return (null, null, $"only {owned?.Count ?? 0} packs of type {type}");
            Dictionary<int, int> collected = Collected();
            // Catch-Up: a set's collected share in copies (2 per card, 1 per Legendary; disenchanted ones count)
            int Limit(Engine.CardDef c) => c.Tag(Engine.GAME_TAG.RARITY) == (int)Engine.TAG_RARITY.LEGENDARY ? 1 : 2;
            (long Have, long All) SetCopies(int set)
            {
                var cardsOfSet = pool.Where(c => rules.SetOf(c.DbfId) == set).ToList();
                return (cardsOfSet.Sum(c => (long)Math.Min(Limit(c), collected.GetValueOrDefault(c.DbfId))), cardsOfSet.Sum(c => (long)Limit(c)));
            }
            var cards = new List<(int Dbf, int Premium)>();
            for (int i = 0; i < packs; i++)
            {
                var opened = pack.Catchup
                    ? rules.RollCatchup(pack, pool, owned.Pity, dbf => collected.GetValueOrDefault(dbf),
                        set => SetCopies(set) is { All: > 0 } c ? c.Have / (double)c.All : 0, rng)
                    : rules.Roll(pack, pool, owned.Pity, dbf => collected.GetValueOrDefault(dbf), rng);
                foreach (var (dbf, premium) in opened) Add(dbf, premium, 1, 0); // also counts as collected
                cards.AddRange(opened);
            }
            owned.Count -= packs;
            var mods = cards.GroupBy(c => c).Select(g => new CardModification
            {
                AssetCardId = g.Key.Dbf, Premium = g.Key.Premium, Quantity = g.Count(), AmountSeen = 0,
            }).ToList();
            ClientStateNotification change = Committed(mods);
            change.BoosterModifications = new PegasusUtil.BoosterModifications
            {
                Modifications = { new PegasusShared.BoosterInfo { Type = type, Count = -packs, EverGrantedCount = 0 } },
            };
            DateTime now = DateTime.UtcNow;
            var date = new Date { Year = now.Year, Month = now.Month, Day = now.Day, Hours = now.Hour, Min = now.Minute, Sec = now.Second };
            var reply = new PegasusUtil.BoosterContent
            {
                List = cards.Select(c => new PegasusUtil.BoosterCard { CardDef = new PegasusShared.CardDef { Asset = c.Dbf, Premium = c.Premium }, InsertDate = date }).ToList(),
                CollectionVersion = state.CollectionVersion, IsMpo = packs > 1, IsCatchupPack = pack.Catchup, PacksOpened = packs,
                MassPackOpeningEnabled = true,
            };
            if (pack.Catchup) // the summary screen: "<set>: x% collected"
                reply.ExpansionCollectionStats = pack.CatchupSets.Select(set => SetCopies(set)).Zip(pack.CatchupSets, (c, set) =>
                    new PegasusUtil.ExpansionCollectionStats { CardSetId = (ulong)set, CardsInCollection = (uint)c.Have, CardsInExpansion = (uint)c.All }).ToList();
            return (reply, change, null);
        }
    }

    // New versions, saved, and the 333 the client applies (collection changes + the new dust balance).
    private ClientStateNotification Committed(List<CardModification> mods)
    {
        state.CollectionVersion++;
        state.CurrencyVersion++;
        Save();
        var currency = new PegasusUtil.GameCurrencyStates();
        ApplyTo(currency);
        return new ClientStateNotification
        {
            CollectionModifications = new CollectionModifications { CardModifications = mods, CollectionVersion = state.CollectionVersion },
            CurrencyState = currency,
        };
    }

    private Stack Find(int dbf, int premium) => state.Cards.FirstOrDefault(s => s.Dbf == dbf && s.Premium == premium);

    private void Add(int dbf, int premium, int count, int seen)
    {
        Stack s = Find(dbf, premium);
        if (s == null) state.Cards.Add(s = new Stack { Dbf = dbf, Premium = premium });
        s.Count = Math.Max(0, s.Count + count);
        s.Seen = Math.Min(s.Count, s.Seen + seen);
        if (count > 0 && state.Collected != null) state.Collected[dbf] = state.Collected.GetValueOrDefault(dbf) + count;
    }

    // Copies ever received per card (all premiums): started from what is owned now, then every gain is added.
    private Dictionary<int, int> Collected()
    {
        if (state.Collected == null)
        {
            state.Collected = new Dictionary<int, int>();
            foreach (Stack s in state.Cards.Where(s => s.Count > 0))
                state.Collected[s.Dbf] = state.Collected.GetValueOrDefault(s.Dbf) + s.Count;
        }
        return state.Collected;
    }
}

/// <summary>A player's progress on one reward track (C4): level, XP into that level, and each level's reward statuses
/// [free, paid, paid premium] (0 unknown/unclaimed, 1 GRANTED, 2 ACKED, 3 RESET) with what a granted reward gave
/// (RewardItemOutput: item id, card, premium, amount) for the client's reward popup.</summary>
internal sealed class RewardTrackState
{
    public int Level { get; set; }
    public int Xp { get; set; }
    public bool Active { get; set; }
    public Dictionary<int, int[]> Levels { get; set; } = new();
    public Dictionary<string, List<int[]>> Outputs { get; set; } = new();

    public int Status(int level, int paidType) => Levels.TryGetValue(level, out var s) ? s[paidType] : 0;

    public void SetStatus(int level, int paidType, int status)
    {
        if (!Levels.TryGetValue(level, out var s)) Levels[level] = s = new int[3];
        s[paidType] = status;
    }

    /// <summary>One level's full state (the client replaces a level's state with what it receives).</summary>
    public PegasusUtil.PlayerRewardTrackLevelState LevelState(int level)
    {
        var s = Levels.TryGetValue(level, out var known) ? known : new int[3];
        var state = new PegasusUtil.PlayerRewardTrackLevelState
            { Level = level, FreeRewardStatus = s[0], PaidRewardStatus = s[1], PaidPremiumRewardStatus = s[2] };
        for (int paid = 0; paid < 3; paid++)
            if (s[paid] == 1 && Outputs.TryGetValue($"{level}:{paid}", out var outputs))
                foreach (int[] o in outputs)
                    state.RewardItemOutput.Add(new PegasusUtil.RewardItemOutput
                    {
                        RewardItemId = o[0],
                        OutputData = new PegasusUtil.RewardItemOutputData { CardId = o[1], Premium = o[2], Amount = o[3], MercenaryId = 0, ArtVariationId = 0 },
                    });
        return state;
    }

    public PegasusUtil.PlayerRewardTrackState ToState(int trackId, bool withLevels)
    {
        var state = new PegasusUtil.PlayerRewardTrackState { RewardTrackId = trackId, Level = Level, Xp = Xp, IsActiveRewardTrack = Active };
        if (withLevels)
            foreach (int level in Levels.Keys.OrderBy(l => l)) state.TrackLevel.Add(LevelState(level));
        return state;
    }
}
