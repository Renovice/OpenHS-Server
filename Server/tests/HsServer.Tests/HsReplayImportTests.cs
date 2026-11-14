using System.Xml.Linq;
using HsServer.Protocol;
using HsServer.Sabber;
using PegasusGame;
using Xunit.Abstractions;

namespace HsServer.Tests;

/// <summary>`HsServer hsreplay`: a live recording written as an HSReplay replay (python-hsreplay's element and attribute
/// names) imports back into the same power history, sub-spells and metadata included.</summary>
public class HsReplayImportTests(ITestOutputHelper output)
{
    [Fact]
    public void A_live_game_round_trips_through_the_hsreplay_format()
    {
        string session = Directory.GetFiles(Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures", "live-20261003-dk-classxp"), "session-*.jsonl")
            .OrderBy(f => f).Last();
        int historyId = PegasusPackets.IdOf<PowerHistory>();
        var original = Recording.Read(session).Where(p => p.Channel == "game" && p.IsIn && p.Type == historyId && p.Body != null)
            .SelectMany(p => PegasusPackets.Deserialize<PowerHistory>(p.Body).List).ToList();
        // the first game only (an HSReplay file holds one <Game> per game)
        int second = original.FindIndex(1, d => d.CreateGame != null);
        if (second > 0) original = original.Take(second).ToList();

        string dir = Path.Combine(Path.GetTempPath(), "hsreplay-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string xml = Path.Combine(dir, "sample.xml");
            ToHsReplay(original).Save(xml);
            Assert.Equal(0, HsServer.Tools.HsReplayImport.Run(new[] { xml, dir }));
            string imported = Directory.GetFiles(Path.Combine(dir, "hsreplay-sample"), "session-*.jsonl").Single();
            var back = Recording.Read(imported).Where(p => p.Type == historyId && p.Body != null)
                .SelectMany(p => PegasusPackets.Deserialize<PowerHistory>(p.Body).List).ToList();

            // block flags (ShowInHistory, IsDeferrable, IsBatchable, IsDeferBlocker, ForceShowBigCard) aren't in the HSReplay
            // format (nor printed in Power.log): compared without them
            string Text(List<PowerHistoryData> h) => System.Text.RegularExpressions.Regex.Replace(
                HistoryText.Format(h, id => id.ToString(), withTags: true), @" (ShowInHistory|IsDeferrable|IsBatchable|IsDeferBlocker|ForceShowBigCard)", "");
            string[] a = Text(original).Split('\n'), b = Text(back).Split('\n');
            int diff = Enumerable.Range(0, Math.Min(a.Length, b.Length)).FirstOrDefault(i => a[i] != b[i], -1);
            if (diff >= 0) output.WriteLine($"first difference at line {diff}:\n  live:     {a[diff]}\n  imported: {b[diff]}");
            Assert.Equal(-1, diff);
            Assert.Equal(a.Length, b.Length);
            Assert.Contains(back, d => d.SubSpellStart != null);
            Assert.Contains(back, d => d.MetaData?.Type == HistoryMeta.Type.DAMAGE);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    /// <summary>The power history as python-hsreplay writes it (numbers for tags and enums).</summary>
    private static XDocument ToHsReplay(List<PowerHistoryData> history)
    {
        var game = new XElement("Game", new XAttribute("ts", "2026-10-03T09:00:00Z"), new XAttribute("type", 7), new XAttribute("format", 2));
        var stack = new Stack<XElement>();
        stack.Push(game);
        XElement Tags(XElement e, IEnumerable<Tag> tags)
        {
            foreach (Tag t in tags) e.Add(new XElement("Tag", new XAttribute("tag", t.Name), new XAttribute("value", t.Value)));
            return e;
        }
        foreach (PowerHistoryData d in history)
        {
            XElement top = stack.Peek();
            if (d.CreateGame is { } c)
            {
                top.Add(Tags(new XElement("GameEntity", new XAttribute("id", c.GameEntity.Id)), c.GameEntity.Tags));
                foreach (var p in c.Players)
                    top.Add(Tags(new XElement("Player", new XAttribute("id", p.Entity.Id), new XAttribute("playerID", p.Id),
                        new XAttribute("accountHi", p.GameAccountId?.Hi ?? 0), new XAttribute("accountLo", p.GameAccountId?.Lo ?? 0),
                        new XAttribute("cardback", p.CardBack)), p.Entity.Tags));
            }
            else if (d.FullEntity is { } f) top.Add(Tags(new XElement("FullEntity", new XAttribute("id", f.Entity), new XAttribute("cardID", f.Name ?? "")), f.Tags));
            else if (d.ShowEntity is { } s) top.Add(Tags(new XElement("ShowEntity", new XAttribute("entity", s.Entity), new XAttribute("cardID", s.Name ?? "")), s.Tags));
            else if (d.ChangeEntity is { } ch) top.Add(Tags(new XElement("ChangeEntity", new XAttribute("entity", ch.Entity), new XAttribute("cardID", ch.Name ?? "")), ch.Tags));
            else if (d.HideEntity is { } h) top.Add(new XElement("HideEntity", new XAttribute("entity", h.Entity), new XAttribute("zone", h.Zone)));
            else if (d.TagChange is { } t) top.Add(new XElement("TagChange", new XAttribute("entity", t.Entity), new XAttribute("tag", t.Tag), new XAttribute("value", t.Value)));
            else if (d.ShuffleDeck is { } sd) top.Add(new XElement("ShuffleDeck", new XAttribute("player_id", sd.PlayerId)));
            else if (d.MetaData is { } m)
                top.Add(new XElement("MetaData", new XAttribute("meta", (int)m.Type), new XAttribute("data", m.Data), new XAttribute("infoCount", m.Info.Count),
                    m.Info.Select((id, i) => new XElement("Info", new XAttribute("index", i), new XAttribute("entity", id)))));
            else if (d.PowerStart is { } b)
            {
                var block = new XElement("Block", new XAttribute("entity", b.Source), new XAttribute("type", (int)b.Type),
                    new XAttribute("effectIndex", b.EffectIndex), new XAttribute("target", b.Target), new XAttribute("subOption", b.SubOption),
                    new XAttribute("triggerKeyword", b.TriggerKeyword), new XAttribute("effectCardId", b.EffectCardId ?? ""));
                top.Add(block);
                stack.Push(block);
            }
            else if (d.PowerEnd != null && stack.Count > 1) stack.Pop();
            else if (d.SubSpellStart is { } ss)
            {
                var sub = new XElement("SubSpell", new XAttribute("spellPrefabGuid", ss.SpellPrefabGuid), new XAttribute("source", ss.SourceEntityId),
                    new XAttribute("targetCount", ss.TargetEntityIds.Count),
                    ss.TargetEntityIds.Select((id, i) => new XElement("SubSpellTarget", new XAttribute("index", i), new XAttribute("entity", id))));
                top.Add(sub);
                stack.Push(sub);
            }
            else if (d.SubSpellEnd != null && stack.Count > 1) stack.Pop();
        }
        return new XDocument(new XElement("HSReplay", new XAttribute("version", "1.7"), new XAttribute("build", 0), game));
    }
}
