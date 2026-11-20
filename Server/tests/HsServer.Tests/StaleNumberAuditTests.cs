using System.Reflection;
using System.Text.RegularExpressions;
using HsServer.Engine;
using HsServer.Sabber;
using SabberStoneCore.Model;
using SabberStoneCore.Tasks;
using SabberStoneCore.Tasks.SimpleTasks;

namespace HsServer.Tests;

/// <summary>Guard (2026-11-16, from livecheck: Razorpetal dealt 1, live 2): scripts whose damage number differs from the card's current text (Blizzard changed the card after the 2021 script).</summary>
public class StaleNumberAuditTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static void Walk(object task, List<DamageTask> found, HashSet<object> seen, int depth = 0)
    {
        if (task == null || depth > 8 || !seen.Add(task)) return;
        if (task is DamageTask d) found.Add(d);
        foreach (var f in task.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            object v = f.GetValue(task);
            if (v is ISimpleTask t) Walk(t, found, seen, depth + 1);
            else if (v is IEnumerable<ISimpleTask> list) foreach (var x in list) Walk(x, found, seen, depth + 1);
        }
        foreach (var p in task.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.GetIndexParameters().Length == 0))
        {
            object v; try { v = p.GetValue(task); } catch { continue; }
            if (v is ISimpleTask t) Walk(t, found, seen, depth + 1);
            else if (v is IEnumerable<ISimpleTask> list) foreach (var x in list) Walk(x, found, seen, depth + 1);
        }
    }

    [Fact]
    public void Damage_numbers_match_the_card_text()
    {
        string dir = CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
        SabberEngineGame.EnsureCards(dir);
        // the current text, from the client's own card table (the engine's Text can be 2021's)
        var text = new Dictionary<string, string>();
        using (var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir, "Card.json"))))
            foreach (var r in doc.RootElement.GetProperty("records").EnumerateArray())
                if (r.TryGetProperty("NoteMiniGuid", out var id) && r.TryGetProperty("TextInHand", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.String)
                    text[id.GetString()] = t.GetString();
        var stale = new List<string>();
        foreach (Card c in Cards.All)
        {
            if (!text.TryGetValue(c.Id, out string now)) continue;
            if (Regex.IsMatch(now, @"(?i)split|randomly|times|missile")) continue; // repeated 1-damage effects: the script's 1 is right
            var nums = Regex.Matches(now, @"\$(\d+) damage").Select(m => int.Parse(m.Groups[1].Value)).Distinct().ToList();
            if (nums.Count != 1 || c.Power == null) continue;
            var found = new List<DamageTask>();
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            Walk(c.Power.PowerTask, found, seen);
            Walk(c.Power.DeathrattleTask, found, seen);
            Walk(c.Power.Trigger?.SingleTask, found, seen);
            var amounts = found.Where(d => d.RandAmount == 0).Select(d => d.Amount).Distinct().ToList();
            if (amounts.Count == 1 && amounts[0] != nums[0] && amounts[0] > 0)
                stale.Add($"{c.Id}\t{c.Name}\tscript {amounts[0]}\ttext {nums[0]}\t{c.Text.Replace("\n", " ")}");
        }
        File.WriteAllLines(Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "livecheck", "stale-damage-numbers.tsv"), stale);
        output.WriteLine($"{stale.Count} cards");
        // (known: the Classic-format copies VAN_… and puzzle copies run the modern card's script — Classic mode's own numbers are
        // still to do; they are kept out of every non-Classic pool)
        var unexplained = stale.Where(l => !l.StartsWith("VAN_") && !l.Split('\t')[0].EndsWith("_Puzzle")).ToList();
        Assert.True(unexplained.Count == 0, "scripts with a damage number the card no longer has:\n" + string.Join("\n", unexplained));
    }
}
