using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HsServer.Lobby;

namespace HsServer.Shop;

/// <summary>
/// The shop's local answer port (127.0.0.1 only), used by the client plugin's Commerce bridge in place of Blizzard's
/// Commerce service: one JSON request per line {"op", "player", "request"} → one line with the response JSON.
/// ops: pages (GetCurrentPagesRequest), products (GetProductsByStoreIdRequest), balance (GetBalanceDetailRequest).
/// </summary>
internal sealed class CommerceServer(ServerData data, ShopCatalog catalog, int port)
{
    public const int DefaultPort = 1122;

    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        try { listener.Start(); }
        catch (SocketException e)
        {
            Log.Warn("shop", $"shop port {port} unavailable ({e.Message}): the in-game shop won't load");
            return;
        }
        Log.Info("shop", $"shop on 127.0.0.1:{port}: {catalog.ProductCount} products, {data.Orders?.Count ?? 0} priced, pages {(catalog.HasPages ? "recorded" : "empty")}");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => ServeAsync(client, ct), ct);
            }
        }
        catch (OperationCanceledException) { }
        finally { listener.Stop(); }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, new UTF8Encoding(false), leaveOpen: true);
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                string line;
                while ((line = await reader.ReadLineAsync(ct)) != null) // the plugin keeps one connection open
                {
                    string reply = Handle(line);
                    await writer.WriteLineAsync(reply.AsMemory(), ct);
                }
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or JsonException)
            {
                Log.Debug("shop", "shop connection ended: " + e.Message);
            }
        }
    }

    /// <summary>One request → the response JSON (never throws: errors come back as {"error": ...}).</summary>
    internal string Handle(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            string op = doc.RootElement.GetProperty("op").GetString();
            string name = doc.RootElement.TryGetProperty("player", out var p) ? p.GetString() : null;
            string request = doc.RootElement.TryGetProperty("request", out var r) ? r.GetString() : null;
            DateTime now = data.Calendar.Clock();
            switch (op)
            {
                case "pages":
                    Log.Info("shop", "shop pages");
                    return catalog.Pages(now);
                case "products":
                    return catalog.Products(request, now);
                case "balance":
                {
                    PlayerAccount player = data.Accounts.Get(string.IsNullOrEmpty(name) ? AccountStore.DefaultName : name);
                    string code = JsonDocument.Parse(request ?? "{}").RootElement.TryGetProperty("currencyCode", out var c) ? c.GetString() : "";
                    long amount = player.Collection?.ShopBalance(code, catalog.RecordedBalances.GetValueOrDefault(code)) ?? 0;
                    Log.Info("shop", $"balance {code} for {player.Name}: {amount}");
                    return ShopCatalog.Balance(request, amount, player.AccountId);
                }
                case "order":
                {
                    PlayerAccount player = data.Accounts.Get(string.IsNullOrEmpty(name) ? AccountStore.DefaultName : name);
                    if (data.Orders == null) return JsonSerializer.Serialize(new { error = new { code = "NO_SHOP_DATA" }, status = "FAILED" });
                    ShopOrders.Result result = data.Orders.Place(player, request);
                    Log.Info("shop", $"{player.Name}: {result.Log}");
                    // the grants reach the client as live's: the 333 change notice(s), then a 331
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(300); // after the order's SUCCESS has been delivered
                        foreach (var change in result.Changes) await player.NotifyAsync(change);
                        if (result.Changes.Count > 0)
                            await player.NotifyAsync(new PegasusUtil.UpdateAccountLicensesResponse { FixedLicenseSuccess = true, ConsumableLicenseSuccess = true });
                    });
                    return result.Json;
                }
                default:
                    return JsonSerializer.Serialize(new { error = new { code = "UNKNOWN_OP", message = op } });
            }
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            Log.Warn("shop", "bad shop request: " + e.Message);
            return JsonSerializer.Serialize(new { error = new { code = "BAD_REQUEST", message = e.Message } });
        }
    }
}
