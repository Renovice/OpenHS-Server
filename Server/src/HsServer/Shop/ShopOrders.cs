using System.Text.Json;
using System.Text.Json.Nodes;
using HsServer.Lobby;
using HsServer.Protocol;
using PegasusUtil;

namespace HsServer.Shop;

/// <summary>
/// Buying in the shop (roadmap C3). Live (live-20261002-shop-purchase): the client sends PlaceOrderWithVCRequest through
/// the Commerce SDK {productId, quantity, currencyCode XSH gold / XSR runestones, externalTransactionId}; the answer is
/// status IN_PROGRESS then SUCCESS; the game server then grants the contents as a 333 (BoosterModifications {type, +n})
/// and a 331, and the client re-reads its balances. Prices and contents are the reference login's BattlePayConfigResponse
/// (238): a Bundle per PmtProductId with GoldCost (gold), VirtualCurrencyCost (runestones) and Items (product type, data,
/// quantity). A product holding something the server can't grant yet is refused, so nothing is paid for nothing.
/// </summary>
internal sealed class ShopOrders
{
    private readonly Dictionary<long, Bundle> bundles = new();
    // Mini-sets and class sets (client MiniSet → its DeckRecord → DeckCard, one row per copy): set id → (card dbf, copies)
    private readonly Dictionary<int, List<(int Dbf, int Copies)>> miniSets = new();
    // Pre-built decks: SellableDeck → DeckTemplate (class, format) → Deck (name) → DeckCard
    public sealed record PrebuiltDeck(string Name, int ClassId, PegasusShared.FormatType Format, List<(int Dbf, int Copies)> Cards);
    private readonly Dictionary<int, PrebuiltDeck> sellableDecks = new();
    private readonly Dictionary<int, int> basicHero = new(); // class → its basic hero card (CardHero HeroType VANILLA)
    // Battlegrounds skins sold as PRODUCT_TYPE_HERO (Data = the skin's card): skin card → skin id (and the hero it dresses)
    private readonly Dictionary<int, (int Id, int BaseCard)> bgHeroSkins = new();
    private readonly Dictionary<int, int> bgHeroBase = new();
    private readonly Dictionary<int, int> bgGuideSkins = new();
    // Licenses: HIDDEN_LICENSE item data → HiddenLicense.AccountLicenseId → AccountLicense.LicenseId; bonus items' data
    // is an AccountLicense record (StoreManager.GetStaticProductItemOwnershipStatus)
    private readonly Dictionary<int, int> hiddenLicenseAccount = new();
    private readonly Dictionary<int, long> accountLicense = new();

    public int Count => bundles.Count;

    /// <summary>Random-card pools (needs the card data); null = random cards aren't sold.</summary>
    public RandomCards Random { get; init; }

    /// <summary>Products the catalog tags "free": sold for 0 gold (ShopCatalog.FreeProducts).</summary>
    public IReadOnlySet<long> Free { get; init; } = new HashSet<long>();

    public ShopOrders(IEnumerable<Bundle> recorded, string dbfDir = null)
    {
        foreach (Bundle b in recorded ?? Enumerable.Empty<Bundle>())
            if (b.PmtProductId != 0) bundles[b.PmtProductId] = b;
        if (dbfDir == null || !File.Exists(Path.Combine(dbfDir, "MiniSet.json")) || !File.Exists(Path.Combine(dbfDir, "DeckCard.json"))) return;
        var deckOf = new Dictionary<int, int>();
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dbfDir, "MiniSet.json"))))
            foreach (JsonElement r in doc.RootElement.GetProperty("records").EnumerateArray())
                if (r.TryGetProperty("DeckRecord", out var deck)) deckOf[deck.GetInt32()] = r.GetProperty("ID").GetInt32();
        var copies = new Dictionary<int, Dictionary<int, int>>();
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dbfDir, "DeckCard.json"))))
            foreach (JsonElement r in doc.RootElement.GetProperty("records").EnumerateArray())
                if (r.TryGetProperty("DeckId", out var d) && deckOf.TryGetValue(d.GetInt32(), out int set) && r.TryGetProperty("CardId", out var c))
                {
                    if (!copies.TryGetValue(set, out var cards)) copies[set] = cards = new Dictionary<int, int>();
                    cards[c.GetInt32()] = cards.GetValueOrDefault(c.GetInt32()) + 1;
                }
        foreach (var (set, cards) in copies) miniSets[set] = cards.Select(kv => (kv.Key, kv.Value)).ToList();
        LoadSellableDecks(dbfDir);
        foreach (JsonElement r in Records(dbfDir, "BattlegroundsHeroSkin.json"))
        {
            bgHeroSkins[Int(r, "SkinCardId")] = (Int(r, "ID"), Int(r, "BaseCardId"));
            bgHeroBase[Int(r, "ID")] = Int(r, "BaseCardId");
        }
        foreach (JsonElement r in Records(dbfDir, "BattlegroundsGuideSkin.json"))
            bgGuideSkins[Int(r, "SkinCardId")] = Int(r, "ID");
        foreach (JsonElement r in Records(dbfDir, "HiddenLicense.json"))
            hiddenLicenseAccount[Int(r, "ID")] = Int(r, "AccountLicenseId");
        foreach (JsonElement r in Records(dbfDir, "AccountLicense.json"))
            accountLicense[Int(r, "ID")] = Int(r, "LicenseId");
    }

    /// <summary>The license an item stands for (hidden license, fixed license, bonus); null for none.</summary>
    public long? LicenseOf(BundleItem item) => item.ProductType switch
    {
        ProductType.PRODUCT_TYPE_HIDDEN_LICENSE when hiddenLicenseAccount.TryGetValue(item.Data, out int account) && accountLicense.TryGetValue(account, out long id) => id,
        ProductType.PRODUCT_TYPE_FIXED_LICENSE => item.Data,
        ProductType.PRODUCT_TYPE_PROGRESSION_BONUS or ProductType.PRODUCT_TYPE_BATTLEGROUNDS_BONUS
            when accountLicense.TryGetValue(item.Data, out long bonus) => bonus,
        // D2: a Lucky Draw pull (data = the box's next pull license, an AccountLicense row; LuckyDrawManager)
        ProductType.PRODUCT_TYPE_LUCKY_DRAW when accountLicense.TryGetValue(item.Data, out long pull) => pull,
        _ => null,
    };

    /// <summary>The hero a Battlegrounds hero skin dresses (BattlegroundsHeroSkinInfo.BaseHeroCardId).</summary>
    public int BgHeroBase(int skinId) => bgHeroBase.GetValueOrDefault(skinId);

    private static List<JsonElement> Records(string dbfDir, string file)
    {
        string path = Path.Combine(dbfDir, file);
        if (!File.Exists(path)) return new List<JsonElement>();
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        return doc.RootElement.GetProperty("records").EnumerateArray().Select(r => r.Clone()).ToList();
    }

    private static int Int(JsonElement r, string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private void LoadSellableDecks(string dbfDir)
    {
        var templates = Records(dbfDir, "DeckTemplate.json").ToDictionary(r => Int(r, "ID"));
        var names = Records(dbfDir, "Deck.json").ToDictionary(r => Int(r, "ID"),
            r => r.TryGetProperty("Name", out var n) ? (n.ValueKind == JsonValueKind.String ? n.GetString() : n.TryGetProperty("enUS", out var en) ? en.GetString() : null) : null);
        var deckCards = new Dictionary<int, Dictionary<int, int>>();
        foreach (JsonElement r in Records(dbfDir, "DeckCard.json"))
        {
            int deck = Int(r, "DeckId");
            if (!deckCards.TryGetValue(deck, out var cards)) deckCards[deck] = cards = new Dictionary<int, int>();
            cards[Int(r, "CardId")] = cards.GetValueOrDefault(Int(r, "CardId")) + 1;
        }
        foreach (JsonElement r in Records(dbfDir, "SellableDeck.json"))
        {
            if (!templates.TryGetValue(Int(r, "DeckTemplateId"), out JsonElement t)) continue;
            int deck = Int(t, "DeckId");
            var format = t.TryGetProperty("FormatType", out var f) && Enum.TryParse(f.GetString(), out PegasusShared.FormatType ft) ? ft : PegasusShared.FormatType.FT_STANDARD;
            sellableDecks[Int(r, "ID")] = new PrebuiltDeck(names.GetValueOrDefault(deck) ?? "Deck", Int(t, "ClassId"), format,
                deckCards.GetValueOrDefault(deck)?.Select(kv => (kv.Key, kv.Value)).ToList() ?? new List<(int, int)>());
        }
        // a class's basic hero: CardHero records of HeroType VANILLA, by the hero card's CLASS tag (CardTag 199)
        var classOf = Records(dbfDir, "CardTag.json").Where(r => Int(r, "TagId") == 199).GroupBy(r => Int(r, "CardId")).ToDictionary(g => g.Key, g => Int(g.First(), "TagValue"));
        foreach (JsonElement h in Records(dbfDir, "CardHero.json"))
            if (h.TryGetProperty("HeroType", out var type) && type.GetString() == "VANILLA" && classOf.TryGetValue(Int(h, "CardId"), out int cls))
                basicHero.TryAdd(cls, Int(h, "CardId"));
    }

    public PrebuiltDeck Sellable(int id) => sellableDecks.GetValueOrDefault(id);

    /// <summary>The last BattlePayConfigResponse of a recorded session (mini-set contents from the client data).</summary>
    public static ShopOrders FromRecording(string session, string dbfDir = null, RandomCards random = null, IReadOnlySet<long> free = null)
    {
        byte[] body = session == null || !File.Exists(session) ? null
            : Recording.Read(session).LastOrDefault(p => p.Channel == "util" && p.IsIn && p.Type == 238 && p.Body != null)?.Body;
        return new ShopOrders(body == null ? null : PegasusPackets.Deserialize<BattlePayConfigResponse>(body).Bundles, dbfDir)
            { Random = random, Free = free ?? new HashSet<long>() };
    }

    public IReadOnlyList<(int Dbf, int Copies)> MiniSet(int id) => miniSets.GetValueOrDefault(id);

    public Bundle Get(long productId) => bundles.GetValueOrDefault(productId);

    /// <summary>Item kinds this server grants (the rest refuse the purchase).</summary>
    public bool Grants(BundleItem item) => item.ProductType switch
    {
        ProductType.PRODUCT_TYPE_HERO or ProductType.PRODUCT_TYPE_CARD_BACK or ProductType.PRODUCT_TYPE_COSMETIC_COIN
            or ProductType.PRODUCT_TYPE_BATTLEGROUNDS_BOARD_SKIN or ProductType.PRODUCT_TYPE_BATTLEGROUNDS_FINISHER
            or ProductType.PRODUCT_TYPE_BATTLEGROUNDS_EMOTE => true,
        ProductType.PRODUCT_TYPE_HIDDEN_LICENSE or ProductType.PRODUCT_TYPE_FIXED_LICENSE or ProductType.PRODUCT_TYPE_PROGRESSION_BONUS
            or ProductType.PRODUCT_TYPE_BATTLEGROUNDS_BONUS or ProductType.PRODUCT_TYPE_LUCKY_DRAW
            => item.ProductType == ProductType.PRODUCT_TYPE_HIDDEN_LICENSE || LicenseOf(item) != null,
        ProductType.PRODUCT_TYPE_MINI_SET => miniSets.ContainsKey(item.Data),
        ProductType.PRODUCT_TYPE_RANDOM_CARD => Random != null && Random.Pool(item.Data).Count > 0,
        ProductType.PRODUCT_TYPE_DRAFT => true, // Arena tickets
        ProductType.PRODUCT_TYPE_SELLABLE_DECK => sellableDecks.TryGetValue(item.Data, out var d) && d.Cards.Count > 0,
        _ => CanGrant(item),
    };

    /// <summary>Item kinds any server grants without client data (the rest refuse the purchase).</summary>
    public static bool CanGrant(BundleItem item) => item.ProductType switch
    {
        ProductType.PRODUCT_TYPE_BOOSTER or ProductType.PRODUCT_TYPE_SPECIFIC_CARD or ProductType.PRODUCT_TYPE_DIAMOND_CARD
            or ProductType.PRODUCT_TYPE_HIDDEN_LICENSE => true,
        ProductType.PRODUCT_TYPE_CURRENCY => CurrencyOf(item.Data) != null,
        _ => false,
    };

    // ProductType CURRENCY data = the client's CurrencyType (1 GOLD, 2 DUST, 6 ROW_RUNESTONES; others aren't ours)
    private static CollectionStore.Currency? CurrencyOf(int data) => data switch
    {
        1 => CollectionStore.Currency.Gold,
        2 => CollectionStore.Currency.Dust,
        6 => CollectionStore.Currency.Runestones,
        _ => null,
    };

    /// <summary>The price of one in a currency (XSH = GoldCost, XSR = the runestone VirtualCurrencyCost); null = not sold for it.</summary>
    public static long? Price(Bundle bundle, string currencyCode) => currencyCode switch
    {
        "XSH" when bundle.GoldCost != null && bundle.GoldCost.CurrentCost > 0 => (long)bundle.GoldCost.CurrentCost,
        _ when bundle.VirtualCurrencyCost?.Cost != null && bundle.VirtualCurrencyCost.CurrencyCode == currencyCode
            => (long)bundle.VirtualCurrencyCost.Cost.CurrentCost,
        _ => null,
    };

    public sealed record Result(string Json, List<IProtoBuf> Changes, string Log);

    /// <summary>One PlaceOrderWithVCRequest: charge, grant, and the PlaceOrderWithVCResponse JSON (status SUCCESS or the
    /// error), plus the 333s to push.</summary>
    public Result Place(PlayerAccount player, string requestJson)
    {
        JsonNode request = JsonNode.Parse(requestJson ?? "{}");
        long productId = request?["productId"]?.GetValue<long>() ?? 0;
        int quantity = Math.Max(1, request?["quantity"]?.GetValue<int>() ?? 1);
        string currency = request?["currencyCode"]?.GetValue<string>() ?? "";
        string transaction = request?["externalTransactionId"]?.GetValue<string>() ?? "";
        Result Fail(string code, string message) => new(new JsonObject
        {
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
            ["externalTransactionId"] = transaction,
            ["status"] = "FAILED",
        }.ToJsonString(), new List<IProtoBuf>(), $"order {productId} x{quantity} {currency} refused: {code} ({message})");

        CollectionStore collection = player.Collection;
        if (collection == null) return Fail("NO_COLLECTION", "the server runs without card data");
        if (Get(productId) is not { } bundle) return Fail("UNKNOWN_PRODUCT", $"product {productId} isn't in the shop data");
        if (bundle.Items.FirstOrDefault(i => !Grants(i)) is { } unsupported)
            return Fail("NOT_SUPPORTED", $"{unsupported.ProductType} can't be granted by this server yet");
        // the client's ALREADY_OWNED: an owned blocking item (hidden license, bonus) can't be bought again
        // (StoreManager.GetNetworkBundleProductAvailability, GetProductItemPurchaseRule BLOCKING)
        if (bundle.Items.FirstOrDefault(i => i.ProductType is ProductType.PRODUCT_TYPE_HIDDEN_LICENSE or ProductType.PRODUCT_TYPE_PROGRESSION_BONUS
                or ProductType.PRODUCT_TYPE_BATTLEGROUNDS_BONUS or ProductType.PRODUCT_TYPE_LUCKY_DRAW && LicenseOf(i) is { } l && collection.OwnsLicense(l)) is { } ownedItem)
            return Fail("ALREADY_OWNED", $"license {LicenseOf(ownedItem)} is owned");
        long? price = Free.Contains(productId) && currency == "XSH" ? 0 : Price(bundle, currency);
        if (price == null) return Fail("NOT_FOR_SALE", $"not sold for {currency}");
        CollectionStore.Currency wallet = currency == "XSH" ? CollectionStore.Currency.Gold : CollectionStore.Currency.Runestones;
        long total = price.Value * quantity, balance = collection.Balance(wallet);
        if (balance < total) return Fail("INSUFFICIENT_FUNDS", $"{total} needed, {balance} owned");

        collection.SetBalance(wallet, balance - total);
        var changes = new List<IProtoBuf>();
        var granted = new List<string>();
        foreach (BundleItem item in bundle.Items)
        {
            int n = item.Quantity * quantity;
            switch (item.ProductType)
            {
                case ProductType.PRODUCT_TYPE_BOOSTER:
                    if (collection.SetPacks(item.Data, collection.PackCount(item.Data) + n) is { } packs) changes.Add(packs);
                    granted.Add($"pack {item.Data} x{n}");
                    break;
                case ProductType.PRODUCT_TYPE_SPECIFIC_CARD:
                case ProductType.PRODUCT_TYPE_DIAMOND_CARD:
                    int premium = item.ProductType == ProductType.PRODUCT_TYPE_DIAMOND_CARD ? CollectionStore.Diamond
                        : int.TryParse(item.Attributes.FirstOrDefault(a => a.Name == "premium")?.Value, out int p) ? p : CollectionStore.Normal;
                    if (collection.Give(item.Data, premium, n) is { } card) changes.Add(card);
                    granted.Add($"card {item.Data}/{premium} x{n}");
                    break;
                case ProductType.PRODUCT_TYPE_MINI_SET: // mini-sets and class sets: exactly their cards (golden: premium 1)
                    int setPremium = int.TryParse(item.Attributes.FirstOrDefault(a => a.Name == "premium")?.Value, out int sp) ? sp : CollectionStore.Normal;
                    foreach (var (dbf, copies) in MiniSet(item.Data))
                        if (collection.Give(dbf, setPremium, copies * n) is { } setCard) changes.Add(setCard);
                    granted.Add($"mini-set {item.Data}/{setPremium} ({MiniSet(item.Data).Sum(c => c.Copies)} cards) x{n}");
                    break;
                // Hero skins: a Battlegrounds hero / guide skin card → its skin id in the Battlegrounds list; else a
                // constructed hero skin = the hero card in the collection (StoreManager.GetStaticProductItemOwnershipStatus)
                case ProductType.PRODUCT_TYPE_HERO:
                {
                    IProtoBuf skin;
                    if (bgHeroSkins.TryGetValue(item.Data, out var bg)) skin = collection.GrantCosmetic(CollectionStore.Cosmetic.BgHeroSkin, bg.Id);
                    else if (bgGuideSkins.TryGetValue(item.Data, out int guide)) skin = collection.GrantCosmetic(CollectionStore.Cosmetic.BgGuideSkin, guide);
                    else skin = collection.Count(item.Data, CollectionStore.Normal) > 0 ? null : collection.Give(item.Data, CollectionStore.Normal, 1);
                    if (skin != null) changes.Add(skin);
                    granted.Add($"hero skin {item.Data}");
                    break;
                }
                case ProductType.PRODUCT_TYPE_CARD_BACK:
                    if (collection.GrantCosmetic(CollectionStore.Cosmetic.CardBack, item.Data) is { } back) changes.Add(back);
                    granted.Add($"card back {item.Data}");
                    break;
                case ProductType.PRODUCT_TYPE_COSMETIC_COIN:
                    if (collection.GrantCosmetic(CollectionStore.Cosmetic.Coin, item.Data) is { } coin) changes.Add(coin);
                    granted.Add($"coin {item.Data}");
                    break;
                case ProductType.PRODUCT_TYPE_BATTLEGROUNDS_BOARD_SKIN:
                    if (collection.GrantCosmetic(CollectionStore.Cosmetic.BgBoardSkin, item.Data) is { } board) changes.Add(board);
                    granted.Add($"BG board {item.Data}");
                    break;
                case ProductType.PRODUCT_TYPE_BATTLEGROUNDS_FINISHER:
                    if (collection.GrantCosmetic(CollectionStore.Cosmetic.BgFinisher, item.Data) is { } finisher) changes.Add(finisher);
                    granted.Add($"BG finisher {item.Data}");
                    break;
                case ProductType.PRODUCT_TYPE_BATTLEGROUNDS_EMOTE:
                    if (collection.GrantCosmetic(CollectionStore.Cosmetic.BgEmote, item.Data) is { } emote) changes.Add(emote);
                    granted.Add($"BG emote {item.Data}");
                    break;
                case ProductType.PRODUCT_TYPE_DRAFT: // Arena tickets (PlayerDraftTickets in the 333)
                    if (collection.SetBalance(CollectionStore.Currency.ArenaTickets, collection.Balance(CollectionStore.Currency.ArenaTickets) + n) is { } tickets)
                        changes.Add(tickets);
                    granted.Add($"Arena tickets +{n}");
                    break;
                case ProductType.PRODUCT_TYPE_RANDOM_CARD: // n cards from the pool, each with a "you received" notice
                {
                    int randomPremium = int.TryParse(item.Attributes.FirstOrDefault(a => a.Name == "premium")?.Value, out int rp) ? rp : CollectionStore.Normal;
                    var picked = Random.Pick(item.Data, n, dbf => Enumerable.Range(0, 4).Sum(p => collection.Count(dbf, p)), System.Random.Shared);
                    foreach (int dbf in picked)
                    {
                        if (collection.Give(dbf, randomPremium, 1) is { } randomCard) changes.Add(randomCard);
                        changes.Add(collection.CardRewardNotice(dbf, randomPremium, 1));
                    }
                    granted.Add($"random cards {item.Data}{(Random.Approximate(item.Data) ? " (approximate pool)" : "")}: {string.Join(", ", picked)}");
                    break;
                }
                case ProductType.PRODUCT_TYPE_SELLABLE_DECK: // its cards, and the deck in the player's deck list
                {
                    PrebuiltDeck deck = Sellable(item.Data);
                    foreach (var (dbf, copies) in deck.Cards)
                        if (collection.Give(dbf, CollectionStore.Normal, copies * n) is { } deckCard) changes.Add(deckCard);
                    if (player.Decks != null)
                    {
                        var created = player.Decks.Create(new CreateDeck
                        {
                            Name = deck.Name, Hero = basicHero.GetValueOrDefault(deck.ClassId), DeckType = PegasusShared.DeckType.NORMAL_DECK,
                            SortOrder = 0, FormatType = deck.Format,
                        });
                        var contents = new DeckSetData { Deck = created.Info.Id };
                        contents.Cards.AddRange(deck.Cards.Select(c => new PegasusShared.DeckCardData
                            { Def = new PegasusShared.CardDef { Asset = c.Dbf, Premium = 0 }, Qty = c.Copies }));
                        player.Decks.SetData(contents);
                    }
                    granted.Add($"deck \"{deck.Name}\" ({deck.Cards.Sum(c => c.Copies)} cards)");
                    break;
                }
                case ProductType.PRODUCT_TYPE_CURRENCY:
                    var kind = CurrencyOf(item.Data)!.Value;
                    if (collection.SetBalance(kind, collection.Balance(kind) + n) is { } money) changes.Add(money);
                    granted.Add($"{kind} +{n}");
                    break;
                // Licenses: owning the product (sequence bundles unlock their next page), bonuses
                case ProductType.PRODUCT_TYPE_HIDDEN_LICENSE:
                case ProductType.PRODUCT_TYPE_FIXED_LICENSE:
                case ProductType.PRODUCT_TYPE_PROGRESSION_BONUS:
                case ProductType.PRODUCT_TYPE_BATTLEGROUNDS_BONUS:
                case ProductType.PRODUCT_TYPE_LUCKY_DRAW: // the pull itself is asked for next (10204, LuckyDraws)
                    if (LicenseOf(item) is { } license)
                    {
                        if (collection.GrantLicense(license) is { } owned) changes.Add(owned);
                        granted.Add($"license {license}");
                    }
                    break;
            }
        }
        string json = new JsonObject { ["error"] = null, ["externalTransactionId"] = transaction, ["status"] = "SUCCESS" }.ToJsonString();
        return new Result(json, changes, $"order {productId} x{quantity} for {total} {currency}: {string.Join(", ", granted)}");
    }
}
