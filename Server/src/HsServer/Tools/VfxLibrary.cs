using System.Text.Json;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;

namespace HsServer.Tools;

/// <summary>
/// `HsServer vfxlib [--out dir]` (2026-10-08): the visual-effect library of every collectible card, as one status each — where its
/// effects come from and whether they are confirmed:
///   verified        — live recordings of its server-sent effects, and vfxcheck saw ours send every one it could reach;
///   recorded        — recorded, but some effect's action never happened in the test game (a condition; check by hand or a capture);
///   choice          — recorded; the unsent ones depend on which option is picked (vfxcheck picks the first);
///   missed          — recorded, our block did the action but the effect wasn't sent: a bug;
///   client          — no recording; the client's own card data lists its effects (it plays them when our blocks are right);
///   generic         — a minion / weapon / hero with text but no client data or recording: the shared mechanics' visuals only;
///   needs-capture   — a spell / location with no client effect data and no recording (RESEARCH/vfx/to-capture.md's kind of card);
///   plain           — keywords / stats only: nothing beyond the generic summon / attack / death visuals.
/// Reads RESEARCH/vfx/carddefs.json (the client's CardDefs), the recorded effects (RecordedSubSpellAnchors) and a vfxcheck run.
/// Writes RESEARCH/vfx/library.tsv (per card) and RESEARCH/vfx/library.md (per set and status).
/// </summary>
internal static class VfxLibrary
{
    // vfxcheck rows that depend on the pick (Discover / Choose One options: the check takes the first)
    private static readonly HashSet<string> ChoiceDependent = new() { "BG31_BOB", "JAIL_EVENT_100" };

    public static int Run(string[] args)
    {
        string root = Program.ProjectRoot();
        string outDir = Path.Combine(root, "RESEARCH", "vfx");
        for (int i = 0; i < args.Length; i++)
            if (args[i] == "--out" && i + 1 < args.Length) outDir = args[++i];

        // 1. our check of every recorded card
        string checkFile = Path.Combine(Path.GetTempPath(), "hsserver-vfxcheck-" + Environment.ProcessId + ".tsv");
        VfxCheck.Run(new[] { "--out", checkFile });
        var checks = new Dictionary<string, List<string>>();
        foreach (string line in File.ReadLines(checkFile).Skip(1))
        {
            string[] p = line.Split('\t');
            if (p.Length < 6) continue;
            if (!checks.TryGetValue(p[0], out var list)) checks[p[0]] = list = new();
            list.Add(p[5]);
        }
        File.Delete(checkFile);

        // 2. the client's CardDef effects per card (not voice lines, not the generic Play / Attack / Death / stingers)
        var client = new Dictionary<string, int>();
        string defs = Path.Combine(root, "RESEARCH", "vfx", "carddefs.json");
        if (File.Exists(defs))
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(defs));
            foreach (JsonProperty card in doc.RootElement.EnumerateObject())
            {
                int n = 0;
                foreach (JsonProperty slot in card.Value.EnumerateObject())
                    if (slot.Value.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement prefab in slot.Value.EnumerateArray())
                        {
                            string name = (prefab.GetString() ?? "").Split('.')[0];
                            if (name.Length == 0 || name.StartsWith("VO_", StringComparison.Ordinal) || name is "Play" or "Attack" or "Death" or "Trigger" ||
                                name.Contains("Stinger", StringComparison.OrdinalIgnoreCase) || name.Contains("Underlay", StringComparison.OrdinalIgnoreCase))
                                continue;
                            n++;
                        }
                client[card.Name] = n;
            }
        }

        // 3. every collectible card, one status
        var rows = new List<(string Set, string Id, string Name, string Type, string Status, string Detail)>();
        foreach (Card card in Cards.All.Where(c => c.Collectible && c.Type is CardType.MINION or CardType.SPELL or CardType.WEAPON or CardType.HERO or CardType.LOCATION)
                     .OrderBy(c => c.Set.ToString()).ThenBy(c => c.Id))
        {
            string id = card.Id;
            string recordedAs = checks.ContainsKey(id) ? id : RecordedId(id, checks);
            string status, detail;
            if (recordedAs != null)
            {
                var s = checks[recordedAs];
                int sent = s.Count(x => x == "sent"), unseen = s.Count(x => x == "action-not-seen"), missed = s.Count(x => x == "missed");
                status = missed > 0 && ChoiceDependent.Contains(recordedAs) ? "choice"
                    : missed > 0 ? "missed" : unseen > 0 ? "recorded" : "verified";
                detail = $"{sent}/{s.Count} sent" + (unseen > 0 ? $", {unseen} not reached" : "") + (missed > 0 ? $", {missed} missed" : "") +
                         (recordedAs != id ? $" (as {recordedAs})" : "");
            }
            else if (client.TryGetValue(id, out int fx) && fx > 0 || client.TryGetValue(StripPrefix(id), out fx) && fx > 0)
            {
                status = "client";
                detail = $"{fx} client effect(s)";
            }
            else if (card.Text == null || SabberStoneCore.Loader.ReprintCards.IsKeywordOnly(card.Text))
            {
                status = "plain";
                detail = "";
            }
            else if (card.Type is CardType.SPELL or CardType.LOCATION)
            {
                // (a spell / location has no body: without data or a recording it shows as a plain card play)
                status = "needs-capture";
                detail = "no client effect data, no recording";
            }
            else
            {
                // (a minion / weapon / hero: its summon, attack, death and what its effect does — buffs, damage, summons, Deathrattle —
                // are the client's shared visuals; a server-picked extra effect is possible but unknown)
                status = "generic";
                detail = "shared mechanic visuals; no recording";
            }
            rows.Add((SetName((int)card.Set), id, card.Name, card.Type.ToString(), status, detail));
        }

        Directory.CreateDirectory(outDir);
        File.WriteAllLines(Path.Combine(outDir, "library.tsv"),
            new[] { "set\tcard\tname\ttype\tstatus\tdetail" }.Concat(rows.Select(r => $"{r.Set}\t{r.Id}\t{r.Name}\t{r.Type}\t{r.Status}\t{r.Detail}")));

        string[] order = { "verified", "choice", "recorded", "missed", "client", "generic", "needs-capture", "plain" };
        var md = new List<string>
        {
            "# Visual-effect library (generated by `HsServer vfxlib`; do not edit)",
            "",
            "One status per collectible card (see Server/src/HsServer/Tools/VfxLibrary.cs): **verified** recorded and confirmed sent ·",
            "**choice** recorded, depends on the pick · **recorded** recorded, some effect not reached by the test game · **missed** a bug ·",
            "**client** the client's own card data has its effects · **generic** a minion / weapon / hero with the shared visuals only (no",
            "recording: a server-picked extra is possible) · **needs-capture** a spell / location with no data and no recording ·",
            "**plain** keywords / stats only. Per card: library.tsv.",
            "",
            "| status | cards |",
            "|---|---|",
        };
        foreach (string s in order) md.Add($"| {s} | {rows.Count(r => r.Status == s)} |");
        md.Add($"| **total** | {rows.Count} |");
        md.Add("");
        md.Add("| set | " + string.Join(" | ", order) + " |");
        md.Add("|---|" + string.Join("|", order.Select(_ => "---")) + "|");
        foreach (var set in rows.GroupBy(r => r.Set).OrderByDescending(g => g.Count(r => r.Status == "needs-capture")).ThenBy(g => g.Key))
            md.Add($"| {set.Key} | " + string.Join(" | ", order.Select(s => set.Count(r => r.Status == s).ToString())) + " |");
        md.Add("");
        md.Add("## Missed (bugs)");
        foreach (var r in rows.Where(r => r.Status == "missed")) md.Add($"- {r.Name} ({r.Id}): {r.Detail}");
        File.WriteAllLines(Path.Combine(outDir, "library.md"), md);

        Console.Error.WriteLine($"{rows.Count} collectible cards: " + string.Join(", ", order.Select(s => $"{s} {rows.Count(r => r.Status == s)}")) +
                                $" -> {Path.Combine(outDir, "library.md")}");
        return 0;
    }

    /// <summary>A set id by the client's own name (TAG_CARD_SET: 1637 CORE, 1810 CORE_HIDDEN); the number when it has none.</summary>
    private static string SetName(int set) =>
        set == 0 ? "NONE" : Enum.IsDefined(typeof(HsServer.Engine.TAG_CARD_SET), set) ? ((HsServer.Engine.TAG_CARD_SET)set).ToString() : set.ToString();

    private static string StripPrefix(string id) =>
        id.StartsWith("CORE_", StringComparison.Ordinal) ? id[5..] : id.StartsWith("VAN_", StringComparison.Ordinal) ? id[4..] : id;

    /// <summary>A reprint recorded under its original (or the other way): the check's row for it.</summary>
    private static string RecordedId(string id, Dictionary<string, List<string>> checks)
    {
        foreach (string v in new[] { StripPrefix(id), "CORE_" + id, "VAN_" + id })
            if (checks.ContainsKey(v)) return v;
        return null;
    }
}
