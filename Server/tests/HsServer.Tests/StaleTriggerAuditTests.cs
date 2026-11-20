using System.Text.RegularExpressions;
using HsServer.Engine;
using HsServer.Sabber;
using SabberStoneCore.Model;

namespace HsServer.Tests;

/// <summary>Guard (2026-11-16, from livecheck: Shimmering Tempest gave its spell on death, the card now says Battlecry): a script whose
/// Battlecry / Deathrattle doesn't match the card's current text (Blizzard reworked the card after the 2021 script).</summary>
public class StaleTriggerAuditTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void Battlecry_and_deathrattle_scripts_match_the_card_text()
    {
        string dir = CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
        SabberEngineGame.EnsureCards(dir);
        var text = new Dictionary<string, string>();
        using (var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir, "Card.json"))))
            foreach (var r in doc.RootElement.GetProperty("records").EnumerateArray())
                if (r.TryGetProperty("NoteMiniGuid", out var id) && r.TryGetProperty("TextInHand", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.String)
                    text[id.GetString()] = t.GetString();
        var stale = new List<string>();
        foreach (Card c in Cards.All)
        {
            if (c.Power == null || c.Type != SabberStoneCore.Enums.CardType.MINION || !text.TryGetValue(c.Id, out string now)) continue;
            string plain = Regex.Replace(now, "<[^>]*>", "");
            bool saysDeathrattle = Regex.IsMatch(plain, @"(?i)\bDeathrattle\b"), saysBattlecry = Regex.IsMatch(plain, @"(?i)\bBattlecry\b");
            bool hasDeathrattle = c.Power.DeathrattleTask != null, hasBattlecry = c.Power.PowerTask != null;
            if (hasDeathrattle && !saysDeathrattle && saysBattlecry && !hasBattlecry)
                stale.Add($"{c.Id}\t{c.Name}\tscript Deathrattle, text Battlecry\t{plain.Replace("\n", " ")}");
            else if (hasBattlecry && !saysBattlecry && saysDeathrattle && !hasDeathrattle)
                stale.Add($"{c.Id}\t{c.Name}\tscript Battlecry, text Deathrattle\t{plain.Replace("\n", " ")}");
        }
        File.WriteAllLines(Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "livecheck", "stale-triggers.tsv"), stale);
        output.WriteLine($"{stale.Count} cards");
        // (Vengeful Spirit's Outcast only names "Deathrattle minions")
        var unexplained = stale.Where(l => !l.StartsWith("VAN_") && !l.StartsWith("BAR_328	")).ToList();
        Assert.True(unexplained.Count == 0, "scripts with a Battlecry / Deathrattle the card no longer has:" + Environment.NewLine + string.Join(Environment.NewLine, unexplained));
    }
}
