using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace HsLocalServer;

/// <summary>
/// Connects the client to the local C# server (ws://127.0.0.1:1119, synthetic token) — the BepInEx version of the
/// research LocalBootstrap, so HsMod and HsRecorder also run in local mode.
/// Safety: does nothing unless the process was started with HS_LOCAL_SERVER=1 (run-local.ps1). A Battle.net launch
/// never has that variable, so the live client is untouched.
/// </summary>
[BepInPlugin("local.hs.localserver", "HsLocalServer", "1.0.0")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string DefaultToken = "LOCAL-DIAGNOSTIC-NO-ACCOUNT";
    // B1: HS_LOCAL_SERVER_ACCOUNT=<name> logs in as that player of the local server ("LOCAL:<name>"); unset = the default
    // player (the old token).
    private static string Token => string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HS_LOCAL_SERVER_ACCOUNT"))
        ? DefaultToken : "LOCAL:" + Environment.GetEnvironmentVariable("HS_LOCAL_SERVER_ACCOUNT");
    private static ManualLogSource log;
    private static string host = "127.0.0.1";
    private static uint port = 1119;

    private void Awake()
    {
        log = Logger;
        if (Environment.GetEnvironmentVariable("HS_LOCAL_SERVER") != "1")
        {
            log.LogInfo("HsLocalServer inactive (live mode).");
            return;
        }
        host = Environment.GetEnvironmentVariable("HS_LOCAL_SERVER_HOST") ?? host;
        if (uint.TryParse(Environment.GetEnvironmentVariable("HS_LOCAL_SERVER_PORT"), out uint p)) port = p;

        var harmony = new Harmony("local.hs.localserver");
        Type fetcher = AccessTools.TypeByName("Hearthstone.Login.DesktopLoginTokenFetcher") ?? throw new MissingMemberException("DesktopLoginTokenFetcher");
        foreach (string method in new[] { "GetTokenIfAvailable", "GetCachedAuthTokenIfAvailable" })
            harmony.Patch(AccessTools.Method(fetcher, method), prefix: new HarmonyMethod(typeof(Plugin), nameof(TokenPrefix)));
        harmony.Patch(AccessTools.Method(AccessTools.TypeByName("Network"), "CreateConnectionOptions"),
            prefix: new HarmonyMethod(typeof(Plugin), nameof(ConnectionPrefix)));

        // The splash screen subscribes to OnLoginCompleted after a fast local login may already have fired it.
        Type queueIterator = null;
        foreach (Type nested in AccessTools.TypeByName("SplashScreen").GetNestedTypes(BindingFlags.NonPublic))
            if (nested.Name.StartsWith("<Job_ShowLoginQueue>"))
                queueIterator = queueIterator == null ? nested : throw new InvalidOperationException("ambiguous splash queue iterator");
        if (queueIterator == null) throw new MissingMemberException("splash login queue iterator");
        harmony.Patch(AccessTools.Method(queueIterator, "MoveNext"), transpiler: new HarmonyMethod(typeof(Plugin), nameof(QueueRegistration)));

        // Diagnostics (2026-09-30): the deck builder drops Core cards the server grants at login. Log what the client adds to
        // and reports from its collection for those cards.
        Type collectionManager = AccessTools.TypeByName("CollectionManager");
        foreach (MethodInfo m in AccessTools.GetDeclaredMethods(collectionManager))
        {
            if (m.Name == "AddCounts" && m.GetParameters().Length == 6)
                harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(AddCountsPrefix)));
            if (m.Name == "GetOwnedCount" && m.GetParameters().Length == 2)
                harmony.Patch(m, postfix: new HarmonyMethod(typeof(Plugin), nameof(OwnedCountPostfix)));
        }

        // C3: the in-game shop answers from the local server (CommerceBridge)
        CommerceBridge.Install(harmony, host, log);

        // D1: the Black Market's buy / haggle spinner. ShowPurchaseSpinner queues a job that loads the PurchaseAuthWindow and
        // shows it a frame or more later; HidePurchaseSpinner only hides an already-loaded window. A reply faster than
        // that load (the local server answers in milliseconds) left "Working on it..." up forever. The spinner's delayed
        // Show is skipped when its hide already came.
        Type popup = AccessTools.TypeByName("BlackMarketItemPopup");
        Type authWindow = AccessTools.TypeByName("PurchaseAuthWindow");
        if (popup != null && authWindow != null)
        {
            harmony.Patch(AccessTools.Method(popup, "ShowPurchaseSpinner"), prefix: new HarmonyMethod(typeof(Plugin), nameof(SpinnerShowPrefix)));
            harmony.Patch(AccessTools.Method(popup, "HidePurchaseSpinner"), prefix: new HarmonyMethod(typeof(Plugin), nameof(SpinnerHidePrefix)));
            harmony.Patch(AccessTools.Method(authWindow, "Show"), prefix: new HarmonyMethod(typeof(Plugin), nameof(AuthWindowShowPrefix)));
        }
        else log.LogWarning("Black Market spinner patch not installed (BlackMarketItemPopup / PurchaseAuthWindow not found)");

        log.LogWarning($"HsLocalServer ACTIVE: client redirected to ws://{host}:{port} with the local token.");
    }

    private static readonly HashSet<string> Watched = new HashSet<string>
        { "CORE_REV_290", "CORE_ICC_025", "CORE_ICC_092", "CORE_ICC_213", "CORE_ICC_854", "CORE_ICC_912", "CORE_REV_990" };
    private static readonly HashSet<string> Logged = new HashSet<string>();

    private static void AddCountsPrefix(string cardID, object premium, int count, int numSeen)
    {
        if (cardID != null && Watched.Contains(cardID))
            log.LogInfo($"[diag] collection AddCounts {cardID} {premium} count={count} seen={numSeen}");
    }

    private static void OwnedCountPostfix(string cardId, object premium, int __result)
    {
        if (cardId != null && Watched.Contains(cardId) && Logged.Add($"{cardId}/{premium}/{__result}"))
            log.LogInfo($"[diag] GetOwnedCount {cardId} {premium} = {__result}");
    }

    // The Black Market spinner's Show is on its way (queued) / still wanted (no hide yet). Unity's main thread only.
    private static bool spinnerPending, spinnerWanted;

    private static void SpinnerShowPrefix()
    {
        spinnerPending = true;
        spinnerWanted = true;
    }

    private static void SpinnerHidePrefix() => spinnerWanted = false;

    // The first PurchaseAuthWindow.Show after ShowPurchaseSpinner is the spinner's: skipped if it was hidden meanwhile.
    private static bool AuthWindowShowPrefix()
    {
        if (!spinnerPending) return true;
        spinnerPending = false;
        if (spinnerWanted) return true;
        log.LogInfo("Black Market spinner: the reply came before the window loaded; not showing it");
        return false;
    }

    private static bool TokenPrefix(ref string __result)
    {
        __result = Token;
        return false;
    }

    private static bool ConnectionPrefix(MethodBase __originalMethod, ref object __result)
    {
        Type type = ((MethodInfo)__originalMethod).ReturnType;
        object value = Activator.CreateInstance(type);
        Set(value, "Host", host);
        Set(value, "Port", port);
        Set(value, "UseSSL", false);
        Set(value, "AuthToken", Token);
        __result = value;
        return false;
    }

    private static IEnumerable<CodeInstruction> QueueRegistration(IEnumerable<CodeInstruction> instructions)
    {
        int matches = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.operand is MethodInfo called && called.Name == "add_OnLoginCompleted" && called.DeclaringType?.Name == "LoginManager")
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(Plugin), nameof(RegisterQueueLoginCallback));
                matches++;
            }
            yield return instruction;
        }
        if (matches != 1) throw new InvalidOperationException("expected one splash login registration, found " + matches);
    }

    public static void RegisterQueueLoginCallback(object manager, Action callback)
    {
        manager.GetType().GetEvent("OnLoginCompleted").AddEventHandler(manager, callback);
        object dependency = manager.GetType().GetField("LoggedInDependency").GetValue(manager);
        if ((bool)dependency.GetType().GetMethod("IsReady").Invoke(dependency, null)) callback();
    }

    private static void Set(object target, string name, object value)
    {
        PropertyInfo property = target.GetType().GetProperty(name);
        if (property != null) property.SetValue(target, value, null);
        else target.GetType().GetField(name).SetValue(target, value);
    }
}
