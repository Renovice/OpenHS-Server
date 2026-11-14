using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using BepInEx.Logging;
using Blizzard.Commerce;
using com.blizzard.commerce.Model;
using HarmonyLib;
using UnityEngine;

namespace HsLocalServer;

/// <summary>
/// The in-game shop on the local server (roadmap C3). The client's Commerce SDK (native, Blizzard's) would sign in with
/// Blizzard and fail; instead the SDK is marked ready without its native start, and its requests — shop pages, product
/// catalog, balances — go to the local server's shop port. The answers are handed to the SDK's own observers on the next
/// SDK update, exactly where the native SDK would deliver them (EventListenerObserverImpl → HearthstoneCheckout).
/// Purchases (gold / runestones) are placed on the server; their result waits for the purchase window (see
/// PurchaseWindowReady).
/// </summary>
internal static class CommerceBridge
{
    private static ManualLogSource log;
    private static string host;
    private static int port;
    private static string player;
    private static EventListenerObserver listener;
    private static readonly ConcurrentQueue<Action> pending = new ConcurrentQueue<Action>();
    private static readonly object netLock = new object();
    private static TcpClient client;
    private static StreamReader reader;
    private static StreamWriter writer;

    public static void Install(Harmony harmony, string serverHost, ManualLogSource logger)
    {
        log = logger;
        host = serverHost;
        port = int.TryParse(System.Environment.GetEnvironmentVariable("HS_LOCAL_SHOP_PORT"), out int p) ? p : 1122;
        player = System.Environment.GetEnvironmentVariable("HS_LOCAL_SERVER_ACCOUNT");
        if (string.IsNullOrEmpty(player)) player = "default";
        Type wrapper = typeof(CommerceWrapper);
        harmony.Patch(AccessTools.Method(wrapper, "InitListener"), prefix: Prefix(nameof(InitPrefix)));
        harmony.Patch(AccessTools.Method(wrapper, "RegisterBlzHttp"), prefix: Prefix(nameof(TruePrefix)));
        harmony.Patch(AccessTools.Method(wrapper, "Update"), prefix: Prefix(nameof(UpdatePrefix)));
        harmony.Patch(AccessTools.Method(wrapper, "LoadPersonalizedShop"), prefix: Prefix(nameof(PagesPrefix)));
        harmony.Patch(AccessTools.Method(wrapper, "LoadProducts"), prefix: Prefix(nameof(ProductsPrefix)));
        harmony.Patch(AccessTools.Method(wrapper, "GetVCBalance"), prefix: Prefix(nameof(BalancePrefix)));
        harmony.Patch(AccessTools.Method(wrapper, "PurchaseWithVC"), prefix: Prefix(nameof(PurchaseWithVCPrefix)));
        // Diagnostics: the shop's start-up stages (the client logs them at a level its log config hides)
        Type shopInit = null;
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            if (a.GetName().Name == "Assembly-CSharp")
                foreach (Type t in AccessTools.GetTypesFromAssembly(a))
                    if (t.Name == "ShopInitialization") shopInit = t;
        if (shopInit != null)
        {
            harmony.Patch(AccessTools.Method(shopInit, "SetAndLogStatus"), postfix: new HarmonyMethod(typeof(CommerceBridge), nameof(ShopStatusPostfix)));
            harmony.Patch(AccessTools.Method(shopInit, "SetAndLogFailure"), postfix: new HarmonyMethod(typeof(CommerceBridge), nameof(ShopFailurePostfix)));
        }
        else log.LogWarning("CommerceBridge: ShopInitialization not found (no shop start-up diagnostics).");
        // Gold / runestones come from the shop's balance, which the client re-reads only when marked stale (live: after a
        // purchase). Every currency update from the server (333 → NetCache.OnCurrencyState → RefreshWallet) marks them
        // stale here, so an admin change shows at once like dust (CurrencyManager.Update then asks the shop again).
        Type currencyManager = AccessTools.TypeByName("CurrencyManager");
        if (currencyManager != null && AccessTools.Method(currencyManager, "RefreshWallet") is { } refresh)
            harmony.Patch(refresh, postfix: new HarmonyMethod(typeof(CommerceBridge), nameof(RefreshWalletPostfix)));
        log.LogInfo($"CommerceBridge: shop requests go to {host}:{port} as player '{player}'.");
    }

    private static void ShopStatusPostfix(string message) => log.LogInfo("[shop] " + message);

    private static void RefreshWalletPostfix(object __instance)
    {
        MethodInfo mark = AccessTools.Method(__instance.GetType(), "MarkCurrencyDirty");
        if (mark == null) return;
        Type currencyType = mark.GetParameters()[0].ParameterType;
        foreach (string name in new[] { "GOLD", "ROW_RUNESTONES" })
            mark.Invoke(__instance, new[] { Enum.Parse(currencyType, name) });
    }
    private static void ShopFailurePostfix(string message) => log.LogError("[shop] FAILED: " + message);

    private static HarmonyMethod Prefix(string name) => new HarmonyMethod(typeof(CommerceBridge), name);

    // The SDK is "ready" without its native start (which would ask Blizzard for OAuth credentials).
    private static bool InitPrefix(CommerceWrapper __instance, EventListenerObserver listener, RequestedModules modules, object logger, ref bool __result)
    {
        CommerceBridge.listener = listener;
        Traverse t = Traverse.Create(__instance);
        t.Field("_initModules").SetValue(modules);
        t.Field("_logger").SetValue(logger);
        FieldInfo state = AccessTools.Field(typeof(CommerceWrapper), "_currentState");
        state.SetValue(__instance, Enum.Parse(state.FieldType, "Idle"));
        log.LogInfo($"CommerceBridge: SDK ready (local shop, modules {modules}).");
        __result = true;
        return false;
    }

    private static bool TruePrefix(ref bool __result)
    {
        __result = true;
        return false;
    }

    // Each SDK update delivers the answers that came back since the last one (on the main thread, like native events).
    // Only those queued before this update: an answer that isn't due yet queues itself again for the next one.
    private static bool UpdatePrefix(ref bool __result)
    {
        for (int n = pending.Count; n > 0 && pending.TryDequeue(out Action deliver); n--)
        {
            try { deliver(); }
            catch (Exception e) { log.LogError("CommerceBridge: delivering an answer failed: " + e); }
        }
        __result = true;
        return false;
    }

    private static bool PagesPrefix(CommerceWrapper __instance, GetCurrentPagesRequest request, ref bool __result)
    {
        Ask("pages", JsonUtility.ToJson(request), json =>
        {
            var response = JsonUtility.FromJson<GetCurrentPagesResponse>(json);
            Traverse.Create(__instance).Field("_hasLoadedPersonalizedShop").SetValue(true);
            foreach (ICatalogEventObserver o in Observers<ICatalogEventObserver>("_catalogObservers"))
                o?.PersonalizedShopReceived(response.placements, response.error);
            log.LogInfo($"CommerceBridge: shop pages delivered ({response.placements?.Count ?? 0} placements).");
        });
        __result = true;
        return false;
    }

    private static bool ProductsPrefix(CommerceWrapper __instance, GetProductsByStoreIdRequest request, ref bool __result)
    {
        Ask("products", JsonUtility.ToJson(request), json =>
        {
            var response = JsonUtility.FromJson<GetProductsByStoreIdResponse>(json);
            Traverse.Create(__instance).Field("_hasLoadedCatalog").SetValue(true);
            foreach (ICatalogEventObserver o in Observers<ICatalogEventObserver>("_catalogObservers"))
                o?.ProductsLoaded(response.products, 0f);
            log.LogInfo($"CommerceBridge: {response.products?.Count ?? 0} products delivered.");
        });
        __result = true;
        return false;
    }

    private static bool BalancePrefix(GetBalanceDetailRequest request, ref bool __result)
    {
        Ask("balance", JsonUtility.ToJson(request), json =>
        {
            var response = JsonUtility.FromJson<GetBalanceDetailResponse>(json);
            // the SDK's own conversion (LedgerBalanceDetail → CurrencyBalance)
            object[] args = { default(CurrencyBalance), response.balance, null, false };
            AccessTools.Method(listener.GetType(), "UpdateCurrencyBalanceFromLedgerBalanceDetail").Invoke(listener, args);
            var balance = (CurrencyBalance)args[0];
            foreach (IVirtualCurrencyEventObserver o in Observers<IVirtualCurrencyEventObserver>("_vcObservers"))
                o?.OnGetBalance((bool)args[3], (string)args[2], balance);
            log.LogInfo($"CommerceBridge: balance {request.currencyCode} delivered.");
        });
        __result = true;
        return false;
    }

    // Buying with gold / runestones: the server charges and grants; the SDK's observers get live's IN_PROGRESS, then
    // SUCCESS (or the error), as EventListenerObserverImpl reports a PlaceOrderWithVCResponse.
    private static bool PurchaseWithVCPrefix(PlaceOrderWithVCRequest request, ref bool __result)
    {
        float asked = Time.realtimeSinceStartup;
        Ask("order", JsonUtility.ToJson(request), json => DeliverOrder(request, json, asked));
        __result = true;
        return false;
    }

    private static void DeliverOrder(PlaceOrderWithVCRequest request, string json, float asked)
    {
        // live's answer takes network time; ours is back before the shop's purchase window has loaded, and a success
        // that arrives first is lost (StoreManager.HandlePurchaseSuccess returns while no shop window is loaded; the
        // window then shows "Working on it..." for good). Seen with free products, which skip the confirm popup.
        if (!PurchaseWindowReady() && Time.realtimeSinceStartup - asked < 3f)
        {
            pending.Enqueue(() => DeliverOrder(request, json, asked));
            return;
        }
        var response = JsonUtility.FromJson<PlaceOrderWithVCResponse>(json);
        bool ok = response.status == "SUCCESS";
        string code = ok ? null : response.error?.code ?? "FAILED";
        foreach (IVirtualCurrencyEventObserver o in Observers<IVirtualCurrencyEventObserver>("_vcObservers"))
            o?.OnPurchaseEvent(false, State.InProgress, null);
        foreach (IVirtualCurrencyEventObserver o in Observers<IVirtualCurrencyEventObserver>("_vcObservers"))
            o?.OnPurchaseEvent(!ok, State.Finished, code);
        log.LogInfo($"CommerceBridge: order {request.productId} x{request.quantity} {request.currencyCode} -> {response.status} {code}" +
                    $" ({(Time.realtimeSinceStartup - asked) * 1000:0} ms)");
    }

    private static FieldInfo shopWindows;

    // The purchase window (PurchaseAuthWindow, asked for by StoreManager.StartStoreBuy before the order) has loaded and
    // shows the transaction; with no purchase window asked for, any loaded shop window will do.
    private static bool PurchaseWindowReady()
    {
        shopWindows ??= AccessTools.Field(AccessTools.TypeByName("ShopWindowController"), "s_activeWindows");
        if (shopWindows?.GetValue(null) is not System.Collections.IDictionary windows) return true;
        bool anyLoaded = false;
        foreach (System.Collections.DictionaryEntry w in windows)
        {
            bool loaded = Traverse.Create(w.Value).Property("IsLoaded").GetValue<bool>();
            if (((Type)w.Key).Name == "PurchaseAuthWindow") return loaded && Traverse.Create(w.Value).Property("IsShown").GetValue<bool>();
            anyLoaded |= loaded;
        }
        return anyLoaded;
    }

    private static IEnumerable<T> Observers<T>(string field)
    {
        if (listener == null) yield break;
        if (Traverse.Create(listener).Field(field).GetValue() is List<T> list)
            foreach (T o in list.ToArray()) yield return o;
    }

    // The server call runs off the main thread; the answer is delivered on the next SDK update.
    private static void Ask(string op, string requestJson, Action<string> deliver)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            string response = Call(op, requestJson);
            if (response != null) pending.Enqueue(() => deliver(response));
        });
    }

    private static string Call(string op, string requestJson)
    {
        string line = "{\"op\":" + Quote(op) + ",\"player\":" + Quote(player) + ",\"request\":" + Quote(requestJson) + "}";
        lock (netLock)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    if (client == null || !client.Connected)
                    {
                        client?.Close();
                        client = new TcpClient(host, port) { NoDelay = true };
                        var stream = client.GetStream();
                        reader = new StreamReader(stream, new UTF8Encoding(false));
                        writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                    }
                    writer.WriteLine(line);
                    string reply = reader.ReadLine();
                    if (reply != null) return reply;
                }
                catch (Exception e) when (e is IOException || e is SocketException || e is ObjectDisposedException)
                {
                    log.LogWarning($"CommerceBridge: shop server {host}:{port} not reachable ({e.Message}).");
                }
                client?.Close();
                client = null;
            }
        }
        return null;
    }

    private static string Quote(string s)
    {
        if (s == null) return "null";
        var sb = new StringBuilder(s.Length + 16).Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
