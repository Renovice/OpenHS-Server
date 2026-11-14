using System.Text.Json;
using PegasusUtil;

namespace HsServer.Lobby;

/// <summary>
/// D1 Black Market from the client data: the market (event, grace period, daily earn cap), its items (reward list,
/// stock) and its daily price-refresh events (BlackMarket, BlackMarketItem, BlackMarketPriceRefreshTiming).
/// </summary>
internal sealed class BlackMarketRules
{
    public sealed record Item(int Id, string Name, int RewardList, int Stock);
    public sealed record Market(int Id, string Event, string GraceEvent, int DailyEarnCap, IReadOnlyList<string> PriceRefreshEvents);

    public Market Current { get; }
    private readonly Dictionary<int, Item> items = new();

    /// <summary>Each item's price on the event's first day (Server/data/black-market-start-prices.tsv); none = unknown.</summary>
    public IReadOnlyDictionary<int, int> StartPrices => startPrices;
    private readonly Dictionary<int, int> startPrices = new();

    public BlackMarketRules(string dbfDir, string startPricesTsv = null)
    {
        if (startPricesTsv != null && File.Exists(startPricesTsv))
            foreach (string line in File.ReadAllLines(startPricesTsv))
                if (line.Length > 0 && line[0] != '#' && line.Split('	') is [var item, var price, ..] && int.TryParse(item, out int i) && int.TryParse(price, out int p))
                    startPrices[i] = p;
        if (dbfDir == null || !File.Exists(Path.Combine(dbfDir, "BlackMarket.json"))) return;
        JsonElement? market = Records(dbfDir, "BlackMarket.json").Cast<JsonElement?>().FirstOrDefault();
        if (market is not { } m) return;
        int id = Int(m, "ID");
        Current = new Market(id, Str(m, "EventTiming"), Str(m, "GracePeriodTiming"), Int(m, "DailyEarnCap"),
            Records(dbfDir, "BlackMarketPriceRefreshTiming.json").Where(r => Int(r, "BlackMarketId") == id).Select(r => Str(r, "EventTiming")).ToList());
        foreach (JsonElement r in Records(dbfDir, "BlackMarketItem.json"))
            items[Int(r, "ID")] = new Item(Int(r, "ID"), Str(r, "Name"), Int(r, "RewardListId"), Int(r, "ItemStock"));
    }

    public IReadOnlyCollection<Item> Items => items.Values;
    public Item Get(int id) => items.GetValueOrDefault(id);

    private static List<JsonElement> Records(string dir, string file)
    {
        string path = Path.Combine(dir, file);
        if (!File.Exists(path)) return new List<JsonElement>();
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        return doc.RootElement.GetProperty("records").EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static int Int(JsonElement r, string name) =>
        r.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? int.Parse(v.GetString()!) : v.GetInt32() : 0;

    private static string Str(JsonElement r, string name) => r.TryGetProperty(name, out var v) ? v.ToString() : "";
}

/// <summary>A player's Black Market (saved with the collection): their own prices (they change per player), what they
/// bought, haggles turned down.</summary>
internal sealed class BlackMarketBook
{
    public int Market { get; set; }
    public Dictionary<int, BlackMarketItemState> Items { get; set; } = new();
    public int TotalPurchases { get; set; }
    /// <summary>The start of the last daily price refresh applied to the prices.</summary>
    public DateTime? PricesAsOf { get; set; }
    /// <summary>Live's recorded state was taken over (once).</summary>
    public bool Adopted { get; set; }
    /// <summary>Bloodstones earned in the current earning day (it starts at 17:00 UTC), and that day's start.</summary>
    public int EarnedToday { get; set; }
    public DateTime? EarnDay { get; set; }
}

internal sealed class BlackMarketItemState
{
    public int Price { get; set; }
    /// <summary>The last price change in thousandths of a percent (the client shows it /1000 %).</summary>
    public int Adjustment { get; set; }
    public int Purchased { get; set; }
    /// <summary>A haggle on it was turned down (until the next price refresh).</summary>
    public bool Declined { get; set; }
}

/// <summary>
/// D1 Black Market (FINDINGS 2026-10-02; wiki Black_Market). Open while its event is on, and in its grace period.
/// Prices are the player's own: every daily refresh (07:00 UTC events) changes each by a whole percentage "from a 15%
/// discount to a 15% surcharge from its current price, including no change", rounded to an integer price. Stock is per
/// player (DBF stock − bought). An item the player owns already resolves to its fallback list (Golden Standard packs).
/// Bloodstones (BMEC) are earned only in Ranked (wiki), which this server doesn't have: the balance is the player's own.
/// Haggling only in the final week (patch 36.6 notes: "during the final week of the event", October 6 - 13 = the grace
/// period after the earning event). Starting prices: live's recorded item list, else the published first-day prices
/// (data file) with every refresh since the event start. UNCLEAR (no source): the haggle chance (ours: Bloodstones offered
/// / price; the notes: "If you're just a few Bloodstones short"). A refusal is cleared by a price refresh, and the daily
/// refreshes end with the earning event (the last on 10-06 07:00), so in the final week it is one haggle per item.
/// </summary>
internal sealed class BlackMarkets(BlackMarketRules rules, EventCalendar calendar, RewardTracks rewards, Random random = null,
    BlackMarketItemListResponse startingItems = null)
{
    /// <summary>The newest live item list (732) among the sessions (newest file first): the starting prices for a player
    /// whose own login recording has none. Our own server's sessions (local-test-*) must not be passed.</summary>
    public static BlackMarketItemListResponse LatestRecordedItems(IEnumerable<string> sessions)
    {
        foreach (string session in sessions.OrderByDescending(Path.GetFileName, StringComparer.Ordinal))
        {
            string line = File.ReadLines(session).LastOrDefault(l => l.Contains("\"type\":732,") && l.Contains("\"dir\":\"in\""));
            if (line == null) continue;
            using var doc = JsonDocument.Parse(line);
            return HsServer.Protocol.PegasusPackets.Deserialize<BlackMarketItemListResponse>(Convert.FromBase64String(doc.RootElement.GetProperty("b64").GetString()!));
        }
        return null;
    }

    private const int NoticeOriginBlackMarketPurchase = 35; // NetCache.ProfileNotice.NoticeOrigin
    private readonly Random random = random ?? Random.Shared;

    public sealed record Result<T>(T Response, List<IProtoBuf> Changes, string Log);

    private DateTime Now => calendar?.Clock() ?? DateTime.UtcNow;
    private bool Active => rules.Current != null && calendar?.IsActive(rules.Current.Event) == true;
    private bool InGrace => rules.Current != null && !Active && calendar?.IsActive(rules.Current.GraceEvent) == true;

    /// <summary>726 → 727: is the market open, today's numbers and the refresh countdowns.</summary>
    public BlackMarketPlayerStateResponse PlayerState(PlayerAccount player, BlackMarketPlayerStateResponse recordedState, BlackMarketItemListResponse recordedItems)
    {
        if (!Active && !InGrace) return new BlackMarketPlayerStateResponse { Status_ = BlackMarketPlayerStateResponse.Status.BMPS_NO_ACTIVE_EVENT };
        BlackMarketBook book = Book(player, recordedState, recordedItems ?? startingItems);
        var response = new BlackMarketPlayerStateResponse
        {
            Status_ = BlackMarketPlayerStateResponse.Status.BMPS_SUCCESS, EventId = rules.Current.Id, WarMode = false,
            DailyEarn = EarnedToday(player.Collection),
            TotalPurchases = book.TotalPurchases, ActiveLiveMessageId = 0,
        };
        if (calendar.StartOf(rules.Current.Event) is { } start) response.CurrentEventDay = (int)Math.Floor((Now - start).TotalDays);
        if (SecondsToNextRefresh() is { } refresh) response.SecondsUntilNextPriceRefresh = refresh;
        if (InGrace)
        {
            response.InGracePeriod = true;
            if (calendar.Get(calendar.NameOf(rules.Current.GraceEvent))?.End is { } end) response.SecondsUntilGraceEnd = (int)(end - Now).TotalSeconds;
        }
        return response;
    }

    /// <summary>731 → 732: the player's items with their prices, stock and what each would give.</summary>
    public BlackMarketItemListResponse ItemList(PlayerAccount player, BlackMarketItemListResponse recordedItems)
    {
        if (!Active && !InGrace) return new BlackMarketItemListResponse { Status_ = BlackMarketItemListResponse.Status.BMIL_NO_ACTIVE_EVENT };
        BlackMarketBook book = Book(player, null, recordedItems ?? startingItems);
        var response = new BlackMarketItemListResponse { Status_ = BlackMarketItemListResponse.Status.BMIL_SUCCESS, EventId = rules.Current.Id };
        foreach (var (id, s) in book.Items.OrderBy(i => i.Key))
            if (rules.Get(id) is { } item && s.Price > 0) response.Items.Add(Entry(player, item, s));
        if (SecondsToNextRefresh() is { } refresh) response.SecondsUntilNextPriceRefresh = refresh;
        return response;
    }

    /// <summary>740 → 741: pay the current price in Bloodstones, get the item's rewards (a REWARD_LIST notice first).</summary>
    public Result<BlackMarketBuyItemResponse> Buy(PlayerAccount player, BlackMarketBuyItemRequest request)
    {
        var response = new BlackMarketBuyItemResponse { ItemAssetId = request.ItemAssetId, EventId = rules.Current?.Id ?? 0 };
        var changes = new List<IProtoBuf>();
        Result<BlackMarketBuyItemResponse> Fail(BlackMarketBuyItemResponse.Status status, string why)
        {
            response.Status_ = status;
            return new(response, changes, $"item {request.ItemAssetId} refused: {why}");
        }
        if (!Active && !InGrace) return Fail(BlackMarketBuyItemResponse.Status.BMBI_NO_ACTIVE_EVENT, "no event");
        int quantity = Math.Max(1, request.Quantity);
        BlackMarketBook book = Book(player, null, startingItems);
        if (rules.Get(request.ItemAssetId) is not { } item || !book.Items.TryGetValue(item.Id, out var s) || s.Price <= 0)
            return Fail(BlackMarketBuyItemResponse.Status.BMBI_ITEM_NOT_AVAILABLE, "not offered");
        if (request.ExpectedPrice != s.Price) return Fail(BlackMarketBuyItemResponse.Status.BMBI_PRICE_MISMATCH, $"price {s.Price}, client expected {request.ExpectedPrice}");
        if (item.Stock - s.Purchased < quantity) return Fail(BlackMarketBuyItemResponse.Status.BMBI_OUT_OF_STOCK, "out of stock");
        long cost = (long)s.Price * quantity;
        CollectionStore collection = player.Collection;
        if (collection.Balance(CollectionStore.Currency.Bmec) < cost) return Fail(BlackMarketBuyItemResponse.Status.BMBI_INSUFFICIENT_FUNDS, "not enough Bloodstones");
        var (outputs, noticeId) = Deliver(player, item, quantity, cost, changes);
        response.Status_ = BlackMarketBuyItemResponse.Status.BMBI_SUCCESS;
        response.QuantityPurchased = quantity;
        response.BmecBalanceAfter = collection.Balance(CollectionStore.Currency.Bmec);
        response.RemainingStock = item.Stock - collection.BlackMarketBook(b => b.Items[item.Id].Purchased);
        response.TotalPurchasesAfter = collection.BlackMarketBook(b => b.TotalPurchases);
        response.GrantedRewards.AddRange(outputs.Select(Output));
        response.RewardNoticeId = noticeId;
        return new(response, changes, $"bought item {item.Id} ({item.Name}) x{quantity} for {cost} Bloodstones");
    }

    /// <summary>742 → 743: a player who can't afford an item offers all their Bloodstones for it; accepted → bought for
    /// that (the client closes the market for the session), turned down → no more haggling on it until the next refresh.</summary>
    public Result<BlackMarketHaggleItemResponse> Haggle(PlayerAccount player, BlackMarketHaggleItemRequest request)
    {
        var response = new BlackMarketHaggleItemResponse { ItemAssetId = request.ItemAssetId, EventId = rules.Current?.Id ?? 0 };
        var changes = new List<IProtoBuf>();
        Result<BlackMarketHaggleItemResponse> Fail(BlackMarketHaggleItemResponse.Status status, string why)
        {
            response.Status_ = status;
            return new(response, changes, $"haggle on item {request.ItemAssetId} refused: {why}");
        }
        if (!Active && !InGrace) return Fail(BlackMarketHaggleItemResponse.Status.BMHI_NO_ACTIVE_EVENT, "no event");
        BlackMarketBook book = Book(player, null, startingItems);
        if (rules.Get(request.ItemAssetId) is not { } item || !book.Items.TryGetValue(item.Id, out var s) || s.Price <= 0)
            return Fail(BlackMarketHaggleItemResponse.Status.BMHI_ITEM_NOT_AVAILABLE, "not offered");
        if (request.ExpectedPrice != s.Price) return Fail(BlackMarketHaggleItemResponse.Status.BMHI_PRICE_MISMATCH, "price changed");
        if (item.Stock - s.Purchased < 1) return Fail(BlackMarketHaggleItemResponse.Status.BMHI_OUT_OF_STOCK, "out of stock");
        if (!InGrace) return Fail(BlackMarketHaggleItemResponse.Status.BMHI_INVALID_REQUEST, "haggling opens in the final week");
        if (s.Declined) return Fail(BlackMarketHaggleItemResponse.Status.BMHI_ALREADY_DECLINED, "turned down already");
        CollectionStore collection = player.Collection;
        long offer = collection.Balance(CollectionStore.Currency.Bmec);
        if (offer >= s.Price) return Fail(BlackMarketHaggleItemResponse.Status.BMHI_HAS_SUFFICIENT_FUNDS, "can afford it");
        if (offer <= 0) return Fail(BlackMarketHaggleItemResponse.Status.BMHI_INVALID_REQUEST, "no Bloodstones to offer");
        int chance = (int)(offer * 1000 / s.Price);
        response.SuccessChanceMilli = chance;
        response.Quantity = 1;
        if (random.Next(1000) >= chance)
        {
            collection.BlackMarketBook(b => b.Items[item.Id].Declined = true);
            response.Status_ = BlackMarketHaggleItemResponse.Status.BMHI_DECLINED;
            response.BmecBalanceAfter = offer;
            response.RemainingStock = item.Stock - s.Purchased;
            response.TotalPurchasesAfter = book.TotalPurchases;
            return new(response, changes, $"haggle on item {item.Id}: {offer} of {s.Price} turned down ({chance / 10.0:0.#} %)");
        }
        var (outputs, noticeId) = Deliver(player, item, 1, offer, changes);
        response.Status_ = BlackMarketHaggleItemResponse.Status.BMHI_ACCEPTED;
        response.BmecBalanceAfter = collection.Balance(CollectionStore.Currency.Bmec);
        response.RemainingStock = item.Stock - collection.BlackMarketBook(b => b.Items[item.Id].Purchased);
        response.TotalPurchasesAfter = collection.BlackMarketBook(b => b.TotalPurchases);
        response.GrantedRewards.AddRange(outputs.Select(Output));
        response.RewardNoticeId = noticeId;
        response.ShopClosed = true;
        return new(response, changes, $"haggle on item {item.Id}: {offer} of {s.Price} accepted ({chance / 10.0:0.#} %)");
    }

    /// <summary>
    /// A Ranked game ended (wiki: "a win awards 200 Bloodstones; a draw or loss awards 100", up to the market's daily cap
    /// of 900; none in the final week). Live's 728 after a game: BaseAmount 100, WinBonusAmount 100 for a win, IsWinner,
    /// TotalEarned; at the cap all 0 and DailyEarnCapReached (live-20261001-1417). The earning day starts at 17:00 UTC
    /// (live DailyEarn resets then). Null when the market isn't earning.
    /// </summary>
    public Result<BlackMarketCurrencyNotification> RankedGameEnded(PlayerAccount player, bool won)
    {
        if (!Active || player.Collection is not { } collection) return null;
        var changes = new List<IProtoBuf>();
        int cap = rules.Current.DailyEarnCap > 0 ? rules.Current.DailyEarnCap : int.MaxValue;
        int before = EarnedToday(collection);
        int left = Math.Max(0, cap - before);
        int baseAmount = Math.Min(100, left), winBonus = won ? Math.Min(100, left - baseAmount) : 0;
        int earned = baseAmount + winBonus;
        DateTime day = EarnDayStart(Now);
        collection.BlackMarketBook(b => { b.EarnDay = day; b.EarnedToday = before + earned; return 0; });
        if (earned > 0 && collection.SetBalance(CollectionStore.Currency.Bmec, collection.Balance(CollectionStore.Currency.Bmec) + earned) is { } money)
            changes.Add(money);
        var notification = new BlackMarketCurrencyNotification
        {
            BaseAmount = baseAmount, WinBonusAmount = winBonus, IsWinner = won, TotalEarned = earned,
            DailyEarnCapReached = before + earned >= cap,
        };
        return new(notification, changes, $"{(won ? "won" : "lost")}: +{earned} Bloodstones (today {before + earned}/{cap})");
    }

    private int EarnedToday(CollectionStore collection) =>
        collection.BlackMarketBook(b => b.EarnDay == EarnDayStart(Now) ? b.EarnedToday : 0);

    // The earning day runs from 17:00 UTC to 17:00 UTC (live: DailyEarn resets at 17:00 UTC, the event's start time).
    private static DateTime EarnDayStart(DateTime now)
    {
        DateTime reset = now.Date.AddHours(17);
        return now >= reset ? reset : reset.AddDays(-1);
    }

    /// <summary>Admin reset: prices, purchases and haggles start over (from live's recording at the next request);
    /// Bloodstones and granted rewards stay.</summary>
    public static void StartOver(CollectionStore collection) =>
        collection.BlackMarketBook(book => { book.Market = 0; book.Items.Clear(); book.TotalPurchases = 0; book.PricesAsOf = null; book.Adopted = false; return 0; });

    // Charge, count, grant the item's (resolved) reward list per copy, and the REWARD_LIST notice the client's popup waits for.
    private (List<int[]> Outputs, long NoticeId) Deliver(PlayerAccount player, BlackMarketRules.Item item, int quantity, long cost, List<IProtoBuf> changes)
    {
        CollectionStore collection = player.Collection;
        int resolved = rewards?.Resolved(collection, item.RewardList) ?? 0;
        if (collection.SetBalance(CollectionStore.Currency.Bmec, collection.Balance(CollectionStore.Currency.Bmec) - cost) is { } money) changes.Add(money);
        collection.BlackMarketBook(b =>
        {
            b.Items[item.Id].Purchased += quantity;
            b.TotalPurchases += quantity;
            return 0;
        });
        var outputs = new List<int[]>();
        for (int i = 0; i < quantity; i++)
            if (rewards?.GrantList(player, item.RewardList) is var (granted, outs, _))
            {
                changes.AddRange(granted);
                outputs.AddRange(outs);
            }
        var (noticeId, notice) = collection.RewardListNotice(NoticeOriginBlackMarketPurchase, item.RewardList, resolved > 0 ? resolved : item.RewardList, outputs);
        changes.Add(notice);
        return (outputs, noticeId);
    }

    private BlackMarketItemEntry Entry(PlayerAccount player, BlackMarketRules.Item item, BlackMarketItemState s)
    {
        var entry = new BlackMarketItemEntry
        {
            ItemAssetId = item.Id, Price = s.Price, ItemStock = Math.Max(0, item.Stock - s.Purchased), PurchasedCount = s.Purchased,
            HaggleStatus_ = s.Declined ? BlackMarketItemEntry.HaggleStatus.HS_DECLINED : BlackMarketItemEntry.HaggleStatus.HS_NONE,
        };
        if (s.Adjustment != 0) entry.PriceAdjustment = s.Adjustment;
        if (rewards?.Resolved(player.Collection, item.RewardList) is > 0 and var resolved) entry.ResolvedRewardListId = resolved;
        return entry;
    }

    // The player's market: live's recorded prices / purchases taken over once, then every daily refresh since applied.
    private BlackMarketBook Book(PlayerAccount player, BlackMarketPlayerStateResponse recordedState, BlackMarketItemListResponse recordedItems)
    {
        List<DateTime> refreshes = Refreshes();
        return player.Collection.BlackMarketBook(book =>
        {
            if (book.Market != rules.Current.Id) { book.Market = rules.Current.Id; book.Items.Clear(); book.TotalPurchases = 0; book.PricesAsOf = null; book.Adopted = false; }
            if (!book.Adopted && recordedItems?.EventId == rules.Current.Id)
            {
                book.Adopted = true;
                foreach (BlackMarketItemEntry e in recordedItems.Items)
                    book.Items[e.ItemAssetId] = new BlackMarketItemState
                        { Price = e.Price, Adjustment = e.PriceAdjustment, Purchased = e.PurchasedCount, Declined = e.HaggleStatus_ == BlackMarketItemEntry.HaggleStatus.HS_DECLINED };
                book.PricesAsOf = refreshes.LastOrDefault(r => r <= Now) is var last && last != default ? last : Now;
            }
            if (!book.Adopted && rules.StartPrices.Count > 0)
            {
                book.Adopted = true; // nothing recorded: the first-day prices, then every refresh since the start
                foreach (var (item, price) in rules.StartPrices)
                    if (rules.Get(item) != null) book.Items[item] = new BlackMarketItemState { Price = price };
                book.PricesAsOf = calendar.StartOf(rules.Current.Event);
            }
            if (recordedState?.EventId == rules.Current.Id && book.TotalPurchases < recordedState.TotalPurchases)
                book.TotalPurchases = recordedState.TotalPurchases;
            foreach (DateTime refresh in refreshes.Where(r => r <= Now && (book.PricesAsOf == null || r > book.PricesAsOf)))
            {
                foreach (BlackMarketItemState s in book.Items.Values.Where(s => s.Price > 0))
                {
                    int percent = random.Next(-15, 16);
                    int price = (int)Math.Round(s.Price * (100 + percent) / 100.0, MidpointRounding.AwayFromZero);
                    s.Adjustment = (int)Math.Round((price / (double)s.Price - 1) * 100000);
                    s.Price = Math.Max(1, price);
                    s.Declined = false;
                }
                book.PricesAsOf = refresh;
            }
            return book;
        });
    }

    private List<DateTime> Refreshes() => rules.Current?.PriceRefreshEvents.Select(e => calendar?.StartOf(e)).OfType<DateTime>().OrderBy(d => d).ToList()
        ?? new List<DateTime>();

    private int? SecondsToNextRefresh() => Refreshes().FirstOrDefault(r => r > Now) is var next && next != default ? (int)(next - Now).TotalSeconds : null;

    private static RewardItemOutput Output(int[] o) => new()
    {
        RewardItemId = o[0],
        OutputData = new RewardItemOutputData { CardId = o[1], Premium = o[2], Amount = o[3], MercenaryId = 0, ArtVariationId = 0 },
    };
}
