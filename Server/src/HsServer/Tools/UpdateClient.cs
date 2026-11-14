using System.Diagnostics;
using System.Text.Json;

namespace HsServer.Tools;

/// <summary>
/// `HsServer update [--yes] [--live &lt;folder&gt;] [--no-client-sync]` (= Server\update.bat): after a Hearthstone patch, bring the
/// private client and the server's card database up to date. Launching the server never does this (user, 2026-10-03).
/// 1. The private client (Hearthstone main game) gets the real install's new / changed game files; our own setup is never
///    touched (BepInEx with our plugins, the doorstop loader, Logs, Battle.net's .product.db) and nothing is deleted.
/// 2. The private client's recorder (HsRecorder, from RESEARCH\tools) exports the card tables and the event map once the
///    client has loaded them (it redoes the export when the client's card data file changed: hotfixes keep the version).
/// 3. That export becomes a new snapshot Server\data\dbf-&lt;version&gt;-&lt;date&gt; (the server uses the newest), unless it is
///    the same as the newest one; then what changed since the previous snapshot is listed (changes.txt in it).
/// </summary>
internal static class UpdateClient
{
    private const string DefaultLive = @"C:\Program Files (x86)\Hearthstone";

    // our setup in the private client: never overwritten by the real install's files
    private static readonly string[] KeepDirs = { "BepInEx", "Logs" };
    private static readonly string[] KeepFiles = { ".product.db", "Launch Local Hearthstone.bat", "doorstop_config.ini", "winhttp.dll" };

    public static int Run(string[] args)
    {
        string root = Program.ProjectRoot();
        string game = Path.Combine(root, "Hearthstone main game");
        int liveArg = Array.IndexOf(args, "--live");
        string live = liveArg >= 0 && liveArg + 1 < args.Length ? args[liveArg + 1] : DefaultLive;
        bool yes = args.Contains("--yes");

        if (Process.GetProcessesByName("Hearthstone").Length > 0)
        {
            Console.Error.WriteLine("Hearthstone is running. Close every Hearthstone window (and the local server) first.");
            return 1;
        }

        // 1. the private client
        if (!args.Contains("--no-client-sync"))
        {
            if (!File.Exists(Path.Combine(live, "Hearthstone.exe")))
            {
                Console.Error.WriteLine($"The real install wasn't found at {live} (use --live <folder>).");
                return 1;
            }
            var plan = ClientChanges(live, game);
            long bytes = plan.Sum(p => new FileInfo(p.From).Length);
            Console.WriteLine($"Private client: {plan.Count} new or changed game files ({bytes / 1048576.0:0.0} MB) from {live}.");
            foreach (var group in plan.GroupBy(p => TopFolder(Path.GetRelativePath(live, p.From))).OrderByDescending(g => g.Count()))
                Console.WriteLine($"    {group.Count(),5}  {group.Key}");
            Console.WriteLine("    (kept as they are: BepInEx and our plugins, the doorstop loader, Logs, .product.db; nothing is deleted)");
            if (plan.Count > 0)
            {
                if (!yes && !Confirm("Copy them into the private client?"))
                {
                    Console.WriteLine("Private client left as it was.");
                }
                else
                {
                    foreach (var (from, to) in plan)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                        File.Copy(from, to, overwrite: true);
                        File.SetLastWriteTimeUtc(to, File.GetLastWriteTimeUtc(from));
                    }
                    Console.WriteLine($"Copied {plan.Count} files.");
                }
            }
        }

        // 2. the recorder export
        string recorderDir = Path.Combine(game, "BepInEx", "HsRecorder");
        if (!UpdateRecorder(root, game)) return 1;
        string stamp = DataStamp(game);
        string export = WaitForExport(game, recorderDir, stamp);
        if (export == null) return 1;

        // 3. the server's snapshot
        string data = Path.Combine(root, "Server", "data");
        string newest = Directory.EnumerateDirectories(data, "dbf-*").OrderByDescending(d => d, StringComparer.Ordinal).FirstOrDefault();
        if (newest != null && SameData(newest, export))
        {
            Console.WriteLine($"The server's card database is already current ({Path.GetFileName(newest)}). Nothing to do.");
            return 0;
        }
        string version = Path.GetFileName(export)["dbf-".Length..];
        string target = Path.Combine(data, $"dbf-{version}-{DateTime.Now:yyyyMMdd}");
        for (int n = 2; Directory.Exists(target); n++) target = Path.Combine(data, $"dbf-{version}-{DateTime.Now:yyyyMMdd}-{n}");
        CopyDir(export, target);
        Console.WriteLine($"New card database: Server\\data\\{Path.GetFileName(target)} (the server uses it from its next start).");
        if (newest != null) ReportChanges(newest, target);
        // user policy (2026-11-01): every new snapshot is cross-checked against HearthSim's extraction of the same build
        // (`HsServer hsjson`; our export stays the card database — a difference is an export bug to fix or theirs to note)
        int build = HsJsonCheck.ClientBuild(game, target); // (the private client keeps its own .product.db: the real install's then)
        if (build == 0) build = HsJsonCheck.ClientBuild(live, target);
        if (build == 0 || HsJsonCheck.Check(root, target, build) != 0)
            Console.WriteLine("Cross-check not done (no build number or no HearthstoneJSON data yet): run `HsServer hsjson --build N` later.");
        return 0;
    }

    private static bool Confirm(string question)
    {
        Console.Write(question + " [y/N] ");
        return Console.ReadLine()?.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string TopFolder(string relative) =>
        relative.Contains(Path.DirectorySeparatorChar) ? relative[..relative.IndexOf(Path.DirectorySeparatorChar)] : "(top folder)";

    /// <summary>The real install's game files that are new or changed (size, or a newer time) in the private client.</summary>
    private static List<(string From, string To)> ClientChanges(string live, string game)
    {
        var plan = new List<(string, string)>();
        foreach (string from in Directory.EnumerateFiles(live, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(live, from);
            string name = Path.GetFileName(rel);
            if (KeepDirs.Any(d => rel.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) continue;
            if (!rel.Contains(Path.DirectorySeparatorChar) &&
                (KeepFiles.Contains(name, StringComparer.OrdinalIgnoreCase) || name.StartsWith("doorstop_", StringComparison.OrdinalIgnoreCase)))
                continue;
            string to = Path.Combine(game, rel);
            var a = new FileInfo(from);
            var b = new FileInfo(to);
            if (!b.Exists || a.Length != b.Length || a.LastWriteTimeUtc > b.LastWriteTimeUtc.AddSeconds(2))
                plan.Add((from, to));
        }
        return plan;
    }

    /// <summary>The newest recorder from RESEARCH\tools into the private client (the old one kept in RESEARCH\backups), and its
    /// card export switched on.</summary>
    private static bool UpdateRecorder(string root, string game)
    {
        string built = Path.Combine(root, "RESEARCH", "tools", "HsRecorder", "bin", "Release", "net48", "HsRecorder.dll");
        string plugin = Path.Combine(game, "BepInEx", "plugins", "HsRecorder.dll");
        if (File.Exists(built) && (!File.Exists(plugin) || !File.ReadAllBytes(built).AsSpan().SequenceEqual(File.ReadAllBytes(plugin))))
        {
            if (File.Exists(plugin))
            {
                string backup = Path.Combine(root, "RESEARCH", "backups", $"private-client-recorder-{DateTime.Now:yyyyMMdd-HHmmss}");
                Directory.CreateDirectory(backup);
                File.Copy(plugin, Path.Combine(backup, "HsRecorder.dll"));
            }
            File.Copy(built, plugin, overwrite: true);
            Console.WriteLine("Private client: recorder updated (it exports the card tables and the event map).");
        }
        string cfg = Path.Combine(game, "BepInEx", "config", "local.hs.recorder.cfg");
        if (File.Exists(cfg))
        {
            string text = File.ReadAllText(cfg);
            string on = System.Text.RegularExpressions.Regex.Replace(text, @"(?m)^Dbf\s*=\s*false", "Dbf = true");
            if (on != text) File.WriteAllText(cfg, on);
        }
        return true;
    }

    /// <summary>Which card data the client has: Data\Win\dbf.unity3d's size and time (as HsRecorder's manifest "data").</summary>
    private static string DataStamp(string game)
    {
        var f = new FileInfo(Path.Combine(game, "Data", "Win", "dbf.unity3d"));
        return f.Exists ? $"{f.Length}-{f.LastWriteTimeUtc:yyyyMMddHHmmss}" : "none";
    }

    /// <summary>Start the private client until its recorder has exported this card data (a few minutes at most); the export
    /// folder, or null.</summary>
    private static string WaitForExport(string game, string recorderDir, string stamp)
    {
        string Ready() => Directory.Exists(recorderDir)
            ? Directory.EnumerateDirectories(recorderDir, "dbf-*").FirstOrDefault(d =>
                File.Exists(Path.Combine(d, "manifest.json")) && File.ReadAllText(Path.Combine(d, "manifest.json")).Contains($"\"data\":\"{stamp}\""))
            : null;
        if (Ready() is { } done) return done;

        Console.WriteLine("Starting the private client once to export its card data (it closes by itself)...");
        string profile = Path.Combine(Program.ProjectRoot(), "Server", "run", "profile");
        var info = new ProcessStartInfo(Path.Combine(game, "Hearthstone.exe")) { WorkingDirectory = game, UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("-launch");
        info.Environment["HS_LOCAL_SERVER"] = "1"; // as run-local.bat starts it (the card tables load before any login)
        info.Environment["APPDATA"] = Path.Combine(profile, "Roaming");
        info.Environment["LOCALAPPDATA"] = Path.Combine(profile, "Local");
        // (2026-11-15, a first setup: no server yet) with nothing on the lobby port the client fails its login within seconds and
        // quits mid-export; a listener that accepts and holds the connection keeps it at "connecting" until the export is done
        using var hold = HoldLobbyPort();
        using Process client = Process.Start(info)!;
        var clock = Stopwatch.StartNew();
        string export = null;
        while (clock.Elapsed < TimeSpan.FromMinutes(5) && !client.HasExited && (export = Ready()) == null)
            Thread.Sleep(2000);
        if (!client.HasExited)
        {
            client.CloseMainWindow();
            if (!client.WaitForExit(10000)) client.Kill();
        }
        if (export == null)
            Console.Error.WriteLine("The client didn't export its card data (see BepInEx\\LogOutput.log in the private client).");
        return export;
    }

    /// <summary>Accepts and holds connections on the local lobby port (1119) while it is free; null when a server already has it.</summary>
    private static IDisposable HoldLobbyPort()
    {
        System.Net.Sockets.TcpListener listener;
        try
        {
            listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 1119);
            listener.Start();
        }
        catch (System.Net.Sockets.SocketException) { return null; }
        var held = new List<System.Net.Sockets.TcpClient>();
        _ = AcceptLoop(listener, held);
        return new Releaser(() =>
        {
            listener.Stop();
            lock (held) foreach (var c in held) c?.Dispose();
        });
    }

    private static async Task AcceptLoop(System.Net.Sockets.TcpListener listener, List<System.Net.Sockets.TcpClient> held)
    {
        try
        {
            while (true)
            {
                var c = await listener.AcceptTcpClientAsync();
                lock (held) held.Add(c);
            }
        }
        catch { } // stopped
    }

    private sealed class Releaser(Action release) : IDisposable
    {
        public void Dispose() => release();
    }

    /// <summary>The same card data: the card tables and the event map are byte-identical.</summary>
    private static bool SameData(string a, string b) =>
        new[] { "Card.json", "CardTag.json", "EventMap.json" }.All(f =>
            File.Exists(Path.Combine(a, f)) && File.Exists(Path.Combine(b, f)) &&
            File.ReadAllBytes(Path.Combine(a, f)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(b, f))));

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string f in Directory.EnumerateFiles(from))
            File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
    }

    /// <summary>Cards new, gone, renamed / reworded or with other tags since the previous snapshot: on screen (first ones)
    /// and in changes.txt. A reworded card's script needs checking (`HsServer cards --stale`).</summary>
    private static void ReportChanges(string before, string after)
    {
        var a = Load(before);
        var b = Load(after);
        var lines = new List<string>();
        foreach (var (id, card) in b.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            if (!a.TryGetValue(id, out var old)) { lines.Add($"new      {id}  {card.Name}"); continue; }
            if (old.Text != card.Text || old.Name != card.Name) lines.Add($"reworded {id}  {card.Name}: \"{old.Text}\" -> \"{card.Text}\"");
            else if (old.Tags != card.Tags) lines.Add($"tags     {id}  {card.Name}");
        }
        lines.AddRange(a.Keys.Where(id => !b.ContainsKey(id)).OrderBy(id => id, StringComparer.Ordinal).Select(id => $"gone     {id}  {a[id].Name}"));
        File.WriteAllLines(Path.Combine(after, "changes.txt"), lines);
        Console.WriteLine($"Changes since {Path.GetFileName(before)}: {lines.Count(l => l.StartsWith("new"))} new, " +
                          $"{lines.Count(l => l.StartsWith("reworded"))} reworded, {lines.Count(l => l.StartsWith("tags"))} with other tags, " +
                          $"{lines.Count(l => l.StartsWith("gone"))} gone (all in changes.txt).");
        foreach (string l in lines.Where(l => l.StartsWith("reworded")).Take(25)) Console.WriteLine("    " + l);
    }

    private sealed record CardInfo(string Name, string Text, string Tags);

    private static Dictionary<string, CardInfo> Load(string dir)
    {
        var tags = new Dictionary<int, List<string>>();
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir, "CardTag.json"))))
            foreach (JsonElement t in doc.RootElement.GetProperty("records").EnumerateArray())
            {
                int card = t.GetProperty("CardId").GetInt32();
                int value = t.TryGetProperty("TagValue", out JsonElement v) ? v.GetInt32() : 0;
                (tags.TryGetValue(card, out var l) ? l : tags[card] = new List<string>()).Add($"{t.GetProperty("TagId").GetInt32()}={value}");
            }
        var cards = new Dictionary<string, CardInfo>();
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir, "Card.json"))))
            foreach (JsonElement c in doc.RootElement.GetProperty("records").EnumerateArray())
            {
                if (!c.TryGetProperty("NoteMiniGuid", out JsonElement idEl) || idEl.GetString() is not { Length: > 0 } id) continue;
                int dbf = c.GetProperty("ID").GetInt32();
                cards[id] = new CardInfo(Text(c, "Name"), Text(c, "TextInHand"),
                    string.Join(",", (tags.TryGetValue(dbf, out var l) ? l : new List<string>()).OrderBy(x => x, StringComparer.Ordinal)));
            }
        return cards;
    }

    private static string Text(JsonElement card, string field) =>
        !card.TryGetProperty(field, out JsonElement v) ? "" :
        v.ValueKind == JsonValueKind.Object ? (v.TryGetProperty("enUS", out JsonElement en) ? en.GetString() ?? "" : "") : v.GetString() ?? "";
}
