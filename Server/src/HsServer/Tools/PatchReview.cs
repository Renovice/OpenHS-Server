using System.Text.RegularExpressions;

namespace HsServer.Tools;

/// <summary>
/// `HsServer patchreview [snapshot dir]` (also run by `update`): after a Hearthstone patch, the reworded cards whose rules may have
/// changed — their text differs once markup, the Assemble '&amp;' marker and spacing are left out — each with the script lines that name
/// it (the card, its CORE_ / VAN_ / WON_ copies and its tokens: "ID", "IDe", "IDt…"). A script carries its numbers as V(...)
/// defaults and its rules in code, written from the text of the day: these lines are what to check. Written to script-review.md in
/// the snapshot (2026-11-20: patch 37.0.0.256589 had 10 such cards — FINDINGS.md).
/// </summary>
internal static class PatchReview
{
    public static int Run(string[] args)
    {
        string root = Program.ProjectRoot();
        string dataDir = Path.Combine(root, "Server", "data");
        string snapshot = args.Length > 0 && !args[0].StartsWith("--") ? args[0]
            : Directory.GetDirectories(dataDir, "dbf-*").OrderBy(d => d, StringComparer.Ordinal).Last();
        if (!Directory.Exists(snapshot)) snapshot = Path.Combine(dataDir, snapshot);
        string changes = Path.Combine(snapshot, "changes.txt");
        if (!File.Exists(changes)) { Console.WriteLine($"no changes.txt in {snapshot}"); return 1; }
        Write(root, snapshot, File.ReadAllLines(changes));
        return 0;
    }

    /// <summary>The review for one snapshot's changes.txt lines (UpdateClient.ReportChanges calls it right after writing them).</summary>
    internal static void Write(string root, string snapshot, IReadOnlyList<string> changeLines)
    {
        // a "reworded" entry may run over several lines (the texts keep their line breaks)
        var entries = new List<string>();
        foreach (string line in changeLines)
        {
            if (Regex.IsMatch(line, @"^(new|reworded|tags|gone) ")) entries.Add(line);
            else if (entries.Count > 0) entries[^1] += "\n" + line;
        }
        var rx = new Regex(@"^reworded (\S+)\s+(.*?): ""(.*)"" -> ""(.*)""$", RegexOptions.Singleline);
        var cards = new List<(string Id, string Name, string Before, string After)>();
        foreach (string e in entries.Where(e => e.StartsWith("reworded ")))
        {
            Match m = rx.Match(e);
            if (!m.Success) continue;
            string id = m.Groups[1].Value;
            if (id.StartsWith("BG", StringComparison.Ordinal) || id.StartsWith("TB_", StringComparison.Ordinal)) continue; // (not our modes)
            if (Plain(m.Groups[3].Value) == Plain(m.Groups[4].Value)) continue; // (only markup, '&' or spacing)
            cards.Add((id, m.Groups[2].Value, Text(m.Groups[3].Value), Text(m.Groups[4].Value)));
        }

        string scripts = Path.Combine(root, "Server", "src", "HsServer.Rules", "src");
        var files = Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories).Select(f => (Path: f, Lines: File.ReadAllLines(f))).ToList();
        var md = new List<string>
        {
            $"# Script review — {Path.GetFileName(snapshot)}",
            "",
            $"{cards.Count} reworded cards whose rules may have changed (markup, '&' and spacing left out; Battlegrounds left out).",
            "Check each script line against the new text: V(...) defaults, targets, conditions. Then `golden`, the suites, and note it in FINDINGS.md.",
            ""
        };
        foreach (var (id, name, before, after) in cards.OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            string bare = Regex.Replace(id, "^(CORE_|VAN_|WON_)", "");
            var named = new Regex("\"(CORE_|VAN_|WON_)?" + Regex.Escape(bare) + @"(e\d*|t\d*\w*)?""");
            md.Add($"## {id} {name}");
            md.Add($"- before: {before}");
            md.Add($"- now: {after}");
            var hits = files.SelectMany(f => f.Lines.Select((l, i) => (f.Path, Line: i + 1, Text: l)).Where(x => named.IsMatch(x.Text)))
                .Where(x => !x.Text.TrimStart().StartsWith("cards.Add(\"" + id + "\", new CardDef(new[]")) // (adventure deck lists)
                .Take(12).ToList();
            foreach (var h in hits)
                md.Add($"  - {Path.GetRelativePath(root, h.Path).Replace('\\', '/')}:{h.Line}  `{h.Text.Trim().Replace("`", "'")[..Math.Min(140, h.Text.Trim().Length)]}`");
            if (hits.Count == 0) md.Add("  - (no script names it: card data only)");
            md.Add("");
        }
        string output = Path.Combine(snapshot, "script-review.md");
        File.WriteAllLines(output, md);
        Console.WriteLine($"Script review: {cards.Count} reworded cards to check -> {output}");
    }

    private static string Text(string s) => Regex.Replace(Regex.Replace(s, "<[^>]*>|\\[x\\]", ""), @"\s+", " ").Trim();

    private static string Plain(string s) => Regex.Replace(Text(s).Replace("&", ""), @"[\s_]+", "").ToLowerInvariant();
}
