using System.Text.Json;

namespace HsServer.Protocol;

/// <summary>One line of an HsRecorder session-*.jsonl file (packet records only; meta lines are skipped).</summary>
public sealed record RecordedPacket(
    long Seq, DateTime Time, string Channel, string Direction, int Type, int Context,
    string System, string Kind, string Message, string RequestMessage, bool Redacted, byte[] Header, byte[] Body)
{
    public bool IsIn => Direction == "in";
}

public static class Recording
{
    public static IEnumerable<RecordedPacket> Read(string sessionFile)
    {
        // (2026-11-14: no session — the server's no-recording mode — reads as an empty recording)
        if (sessionFile == null || !File.Exists(sessionFile)) yield break;
        // The recorder may still be writing the file.
        using var reader = new StreamReader(new FileStream(sessionFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        for (string line; (line = reader.ReadLine()) != null;)
        {
            if (!line.Contains("\"ch\":")) continue;
            RecordedPacket packet;
            try { packet = ParseLine(line); }
            catch (JsonException) { continue; } // truncated last line of a live session
            if (packet != null) yield return packet;
        }
    }

    public static RecordedPacket ParseLine(string line)
    {
        using var doc = JsonDocument.Parse(line);
        JsonElement r = doc.RootElement;
        // Packet lines only; "fx" is the recorder's gameplay timeline (no direction/body).
        if (!r.TryGetProperty("ch", out var ch) || !r.TryGetProperty("dir", out _)) return null;
        return new RecordedPacket(
            Seq: r.GetProperty("seq").GetInt64(),
            Time: r.TryGetProperty("t", out var t) ? t.GetDateTime() : default,
            Channel: ch.GetString(),
            Direction: r.GetProperty("dir").GetString(),
            Type: r.TryGetProperty("type", out var ty) ? ty.GetInt32() : -1,
            Context: r.TryGetProperty("ctx", out var cx) ? cx.GetInt32() : 0,
            System: r.TryGetProperty("sys", out var sy) ? sy.GetString() : null,
            Kind: r.TryGetProperty("kind", out var k) ? k.GetString() : null,
            Message: r.TryGetProperty("msg", out var m) ? m.GetString() : null,
            RequestMessage: r.TryGetProperty("req", out var rq) ? rq.GetString() : null,
            Redacted: r.TryGetProperty("redacted", out var rd) && rd.GetBoolean(),
            Header: r.TryGetProperty("hdr", out var h) && h.GetString().Length > 0 ? Convert.FromBase64String(h.GetString()) : null,
            Body: r.TryGetProperty("b64", out var b) ? Convert.FromBase64String(b.GetString()) : null);
    }

    /// <summary>
    /// All live-server session files below the given roots, one per file name (live folder and backups overlap).
    /// Sessions recorded against our own local server are excluded: they would feed the server its own answers.
    /// </summary>
    public static IReadOnlyList<string> FindSessions(params string[] roots) =>
        roots.Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "session-*.jsonl", SearchOption.AllDirectories))
            .GroupBy(Path.GetFileName)
            .Select(g => g.OrderByDescending(f => new FileInfo(f).Length).First())
            .Where(f => !IsLocalServerSession(f))
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();

    public const string LocalServerCiidPrefix = "hsserver-";
    private static readonly Dictionary<string, bool> localCache = new();

    /// <summary>True when the session's Battle.net ConnectResponse came from our server (ciid "hsserver-...").</summary>
    public static bool IsLocalServerSession(string sessionFile)
    {
        lock (localCache)
            if (localCache.TryGetValue(sessionFile, out bool cached)) return cached;
        bool local = false;
        foreach (RecordedPacket p in Read(sessionFile))
        {
            if (p.Channel != "bgs" || !p.IsIn || p.Body == null || p.Message?.EndsWith("Connection.V1.ConnectResponse") != true) continue;
            var response = Bgs.Parse<Blizzard.GameService.Protocol.Connection.V1.ConnectResponse>(p.Body);
            local = response.Ciid?.StartsWith(LocalServerCiidPrefix, StringComparison.Ordinal) == true;
            break;
        }
        lock (localCache) localCache[sessionFile] = local;
        return local;
    }
}
