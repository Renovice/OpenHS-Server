using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace HsServer.Tools;

/// <summary>
/// `HsServer local [--replay] [--no-client] [--autoplay N] [--rules basic|sabber] [--player name]` — what run-local.bat starts: the server in this console
/// (log also in Server\logs\server-*.log) plus the RE-project Hearthstone client pointed at it.
/// `HsServer local --stop` — stops the server/client a previous `local` run started, and nothing else: each process is
/// checked by executable path and start time recorded in Server\run\local.json.
/// </summary>
internal static class LocalRun
{
    private sealed record State(int ServerPid, DateTime ServerStart, int? ClientPid, DateTime? ClientStart);

    public static int Run(string[] args)
    {
        string root = Program.ProjectRoot();
        string server = Path.Combine(root, "Server");
        string stateFile = Path.Combine(server, "run", "local.json");
        if (args.Contains("--stop"))
        {
            Stop(root, stateFile);
            return 0;
        }
        Stop(root, stateFile); // a previous run would hold the port and the plugin DLL

        if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e => e.Port == 1119))
        {
            Console.Error.WriteLine("Port 1119 is already in use — is another server running? Close it and try again.");
            return 1;
        }

        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Log.ToFile(Path.Combine(server, "logs", $"server-{stamp}.log"));
        Log.Info("local", $"log: Server\\logs\\server-{stamp}.log");

        Process client = null;
        int playerArg = Array.IndexOf(args, "--player");
        string player = playerArg >= 0 && playerArg + 1 < args.Length ? args[playerArg + 1] : null;
        if (!args.Contains("--no-client")) client = StartClient(root, Path.Combine(server, "logs", $"client-{stamp}"), player);

        using Process self = Process.GetCurrentProcess();
        var state = new State(self.Id, self.StartTime, client?.Id, client?.StartTime);
        File.WriteAllText(stateFile, JsonSerializer.Serialize(state));

        var serveArgs = new List<string> { "--verbose" };
        if (!args.Contains("--replay")) serveArgs.Add("--engine");
        if (args.Contains("--no-recording")) serveArgs.Add("--no-recording");
        int rules = Array.IndexOf(args, "--rules");
        if (rules >= 0 && rules + 1 < args.Length) serveArgs.AddRange(new[] { "--rules", args[rules + 1] });
        int autoplay = Array.IndexOf(args, "--autoplay");
        if (autoplay >= 0 && autoplay + 1 < args.Length) serveArgs.AddRange(new[] { "--autoplay", args[autoplay + 1] });
        return Program.Serve(serveArgs.ToArray());
    }

    // Persistent local profile (caches, seen-cinematic flags...) kept apart from the real one, with a copy of the real
    // options.txt (graphics, frame rate; without it the new-player cinematic replays) and log.config (Power.log).
    private static Process StartClient(string root, string clientLogDir, string player = null)
    {
        string game = Path.Combine(root, "Hearthstone main game");
        string profile = Path.Combine(root, "Server", "run", "profile");
        string appData = Path.Combine(profile, "Roaming"), localAppData = Path.Combine(profile, "Local");
        string hsLocal = Path.Combine(localAppData, "Blizzard", "Hearthstone");
        Directory.CreateDirectory(appData);
        Directory.CreateDirectory(hsLocal);
        Directory.CreateDirectory(clientLogDir);
        foreach (string name in new[] { "options.txt", "log.config" })
        {
            string live = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Blizzard", "Hearthstone", name);
            if (File.Exists(live)) File.Copy(live, Path.Combine(hsLocal, name), overwrite: true);
        }

        // CreateNoWindow: the client doesn't attach to this console. Attached, a Ctrl+C here (stopping the server, or copying
        // a log line) also reached Hearthstone, and the game closed together with the server after a concede (2026-10-01).
        var info = new ProcessStartInfo(Path.Combine(game, "Hearthstone.exe")) { WorkingDirectory = game, UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("-launch");
        info.ArgumentList.Add("-logFile");
        info.ArgumentList.Add(Path.Combine(clientLogDir, "unity.log"));
        info.Environment["HS_LOCAL_SERVER"] = "1"; // activates the HsLocalServer plugin (inert in Battle.net launches)
        info.Environment["APPDATA"] = appData;
        info.Environment["LOCALAPPDATA"] = localAppData;
        // B1: log in as a named player (Server/state/accounts/<name>/); none = the default player
        if (!string.IsNullOrEmpty(player)) info.Environment["HS_LOCAL_SERVER_ACCOUNT"] = player;
        Process client = Process.Start(info)!;
        Log.Info("local", $"client started (pid {client.Id}), logs: {clientLogDir}");
        return client;
    }

    private static void Stop(string root, string stateFile)
    {
        if (!File.Exists(stateFile)) return;
        State state;
        try { state = JsonSerializer.Deserialize<State>(File.ReadAllText(stateFile)); }
        catch (JsonException) { File.Delete(stateFile); return; }
        string serverExe = Path.Combine(root, "Server", "run", "HsServer.exe");
        string clientExe = Path.Combine(root, "Hearthstone main game", "Hearthstone.exe");
        if (state.ClientPid is int clientPid && Ours(clientPid, state.ClientStart, clientExe) is { } c)
        {
            c.CloseMainWindow();
            if (!c.WaitForExit(10000)) c.Kill();
            Console.WriteLine($"stopped Hearthstone ({clientPid})");
        }
        if (state.ServerPid != Environment.ProcessId && Ours(state.ServerPid, state.ServerStart, serverExe) is { } s)
        {
            s.Kill();
            s.WaitForExit(5000);
            Console.WriteLine($"stopped HsServer ({state.ServerPid})");
        }
        File.Delete(stateFile);
    }

    // The process only if it is still the one we started: same executable and same start time (pids get reused).
    private static Process Ours(int pid, DateTime? started, string exe)
    {
        try
        {
            Process p = Process.GetProcessById(pid);
            bool same = string.Equals(p.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase)
                        && started is DateTime t && Math.Abs((p.StartTime - t).TotalSeconds) < 1;
            return same ? p : null;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
