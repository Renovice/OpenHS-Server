using System.Text.Json;
using System.Text.Json.Nodes;
using HsServer.Protocol;

namespace HsServer.Shop;

/// <summary>
/// The shop as the client's Commerce SDK sees it (roadmap C3), from the reference live login's recorded Commerce JSON
/// (HsRecorder "json" channel): the personalized shop pages (GetCurrentPagesResponse) and the product catalog
/// (GetProductsByStoreIdResponse). Served as of now: server times are "now" and page schedules move with them.
/// Balances (GetBalanceDetailResponse) are the player's own: gold = XSH, runestones = XSR.
/// </summary>
internal sealed class ShopCatalog
{
    private readonly string pagesJson;
    private readonly long pagesServerTimeMs;
    private readonly Dictionary<long, JsonNode> products = new();
    /// <summary>The recorded balances (currency code → purchased + granted), the starting point of each player's own.</summary>
    public IReadOnlyDictionary<string, long> RecordedBalances { get; }

    public int ProductCount => products.Count;

    /// <summary>Products tagged "free" (storeSetting Tags): the client prices them 0 gold (ProductFactory) and buys them
    /// as a gold order of 0, e.g. the free ranks of sequence bundles. They have no prices of their own.</summary>
    public HashSet<long> FreeProducts() => products
        .Where(p => (p.Value["storeSetting"]?["attributes"] as JsonArray)?.Any(a => a?["key"]?.GetValue<string>() == "Tags"
            && (a["value"]?.GetValue<string>() ?? "").Split(',').Contains("free")) == true)
        .Select(p => p.Key).ToHashSet();
    public bool HasPages => pagesJson != null;

    private ShopCatalog(string pagesJson, long pagesServerTimeMs, IEnumerable<JsonNode> products, Dictionary<string, long> balances)
    {
        this.pagesJson = pagesJson;
        this.pagesServerTimeMs = pagesServerTimeMs;
        foreach (JsonNode p in products)
            if (p?["productId"]?.GetValue<long>() is { } id) this.products[id] = p;
        RecordedBalances = balances;
    }

    /// <summary>The newest pages, every recorded product, and the last balances of a recorded session.</summary>
    public static ShopCatalog FromRecording(string session)
    {
        string pages = null;
        long pagesTime = 0;
        var allProducts = new List<JsonNode>();
        var balances = new Dictionary<string, long>();
        string pendingCurrency = null;
        if (session != null && File.Exists(session))
            foreach (string line in File.ReadLines(session))
            {
                if (!line.Contains("\"ch\":\"json\"", StringComparison.Ordinal) || !line.Contains("com.blizzard.commerce", StringComparison.Ordinal)) continue;
                using var doc = JsonDocument.Parse(line);
                JsonElement r = doc.RootElement;
                string msg = r.GetProperty("msg").GetString(), json = r.GetProperty("json").GetString();
                switch (msg)
                {
                    case "com.blizzard.commerce.Model.GetCurrentPagesResponse":
                        pages = json;
                        pagesTime = JsonNode.Parse(json)?["metaData"]?["serverTimeMs"]?.GetValue<long>() ?? 0;
                        break;
                    case "com.blizzard.commerce.Model.GetProductsByStoreIdResponse":
                        if (JsonNode.Parse(json)?["products"] is JsonArray list) allProducts.AddRange(list.Select(p => p?.DeepClone()));
                        break;
                    case "com.blizzard.commerce.Model.GetBalanceDetailRequest":
                        pendingCurrency = JsonNode.Parse(json)?["currencyCode"]?.GetValue<string>();
                        break;
                    case "com.blizzard.commerce.Model.GetBalanceDetailResponse":
                        JsonNode balance = JsonNode.Parse(json)?["balance"];
                        string code = balance?["ledgerId"]?["currencyCode"]?.GetValue<string>() ?? pendingCurrency;
                        if (code != null && balance != null)
                            balances[code] = Amount(balance["purchasedBalance"]) + Amount(balance["grantedBalance"]);
                        break;
                }
            }
        return new ShopCatalog(pages, pagesTime, allProducts, balances);
    }

    private static long Amount(JsonNode n) => n == null ? 0 : long.TryParse(n.ToString(), out long v) ? v : 0;

    /// <summary>GetCurrentPagesResponse as of now (schedules shifted by the time since the recording).</summary>
    public string Pages(DateTime now)
    {
        long nowMs = new DateTimeOffset(now).ToUnixTimeMilliseconds();
        if (pagesJson == null) return JsonSerializer.Serialize(new { error = (object)null, metaData = new { serverTimeMs = nowMs }, placements = Array.Empty<object>() });
        JsonNode root = JsonNode.Parse(pagesJson)!;
        long shift = pagesServerTimeMs > 0 ? nowMs - pagesServerTimeMs : 0;
        if (root["metaData"] is JsonObject meta) meta["serverTimeMs"] = nowMs;
        if (root["placements"] is JsonArray placements)
            foreach (JsonNode placement in placements)
                if (placement?["nextScheduledPageDisplayTimeMs"] is JsonValue next && next.TryGetValue(out long at) && at > 0)
                    placement["nextScheduledPageDisplayTimeMs"] = at + shift;
        return root.ToJsonString();
    }

    /// <summary>GetProductsByStoreIdResponse for the requested product ids (all of them in one page).</summary>
    public string Products(string requestJson, DateTime now)
    {
        var ids = (JsonNode.Parse(requestJson ?? "{}")?["productIds"] as JsonArray)?.Select(n => n?.GetValue<long>() ?? 0).ToHashSet();
        var list = new JsonArray();
        foreach (var (id, product) in products)
            if (ids == null || ids.Count == 0 || ids.Contains(id)) list.Add(product.DeepClone());
        var response = new JsonObject
        {
            ["paginationToken"] = "",
            ["metaData"] = new JsonObject
            {
                ["serverTimeMs"] = new DateTimeOffset(now).ToUnixTimeMilliseconds(),
                ["cacheControl"] = new JsonObject { ["maxAgeDurationS"] = 3600 },
            },
            ["childProducts"] = new JsonArray(),
            ["products"] = list,
        };
        return response.ToJsonString();
    }

    /// <summary>GetBalanceDetailResponse for one currency (the shape live sends; the ledger key echoes the request).</summary>
    public static string Balance(string requestJson, long amount, ulong accountId)
    {
        JsonNode request = JsonNode.Parse(requestJson ?? "{}");
        string code = request?["currencyCode"]?.GetValue<string>() ?? "";
        string segment = request?["ledgerKey"]?["titleSegment"]?.GetValue<string>();
        var response = new JsonObject
        {
            ["error"] = null,
            ["balance"] = new JsonObject
            {
                ["ledgerId"] = new JsonObject
                {
                    ["accountId"] = accountId,
                    ["ledgerKey"] = string.IsNullOrEmpty(segment) ? null : new JsonObject { ["gameServiceRegionId"] = null, ["titleSegment"] = segment },
                    ["currencyCode"] = code,
                },
                ["purchasedBalance"] = "0",
                ["grantedBalance"] = amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["ecosystem"] = null,
                ["ledgerBalanceDetailBreakdown"] = new JsonArray(),
            },
            ["softCapped"] = false,
        };
        return response.ToJsonString();
    }
}
