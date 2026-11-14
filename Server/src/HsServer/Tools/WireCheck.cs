using System.Text.RegularExpressions;
using HsServer.Engine;
using HsServer.Sabber;

namespace HsServer.Tools;

/// <summary>
/// `HsServer wirecheck [games=300] [seed=1]`: which tags live sends that our server never does. Live side: every
/// GameState TAG_CHANGE in RESEARCH/captures/*/Power.log (not local-test-*: those are ours). Our side: what the client
/// receives in simulated games through the full client adapter (the soak). Grouped by entity kind (game, player, hero,
/// hand, play, other); only tags the client names. Tags we decided not to send are listed in
/// RESEARCH/engine-audit/wire-tag-ignore.txt ("TAG reason") and reported apart. Writes
/// RESEARCH/engine-audit/wire-tag-gaps-yyyyMMdd.txt.
/// </summary>
internal static class WireCheck
{
    private static readonly Regex TagChange = new(@"TAG_CHANGE Entity=(.*?) tag=(\S+) value=(\S+)", RegexOptions.Compiled);

    public static int Run(string[] args)
    {
        int games = args.Length > 0 ? int.Parse(args[0]) : 300;
        int seed = args.Length > 1 ? int.Parse(args[1]) : 1;
        string root = Program.ProjectRoot();
        string captures = Path.Combine(root, "RESEARCH", "captures");
        string audit = Path.Combine(root, "RESEARCH", "engine-audit");

        // live: (kind, tag name) -> (count, example capture)
        var live = new Dictionary<(string Kind, string Tag), (int Count, string Example)>();
        var liveCards = new Dictionary<string, HashSet<string>>(); // tag -> the cards it was seen on (named entities)
        foreach (string log in Directory.EnumerateFiles(captures, "Power.log", SearchOption.AllDirectories))
        {
            string capture = Path.GetRelativePath(captures, log).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (capture.StartsWith("local-test", StringComparison.Ordinal)) continue;
            foreach (string line in File.ReadLines(log))
            {
                if (!line.Contains("GameState.DebugPrintPower()", StringComparison.Ordinal)) continue;
                Match m = TagChange.Match(line);
                if (!m.Success || char.IsDigit(m.Groups[2].Value[0])) continue; // unnamed: the client doesn't read it
                var key = (LiveKind(m.Groups[1].Value), m.Groups[2].Value);
                live[key] = live.TryGetValue(key, out var v) ? (v.Count + 1, v.Example) : (1, capture);
                Match card = Regex.Match(m.Groups[1].Value, @"cardId=(\w+)");
                if (card.Success)
                    (liveCards.TryGetValue(m.Groups[2].Value, out var set) ? set : liveCards[m.Groups[2].Value] = new HashSet<string>()).Add(card.Groups[1].Value);
            }
        }

        // ours: the client view of simulated games
        var ours = new HashSet<(string Kind, string Tag)>();
        // ... and what the real client logged from our server (local-test captures: cards the simulated decks may miss)
        foreach (string log in Directory.EnumerateFiles(captures, "Power.log", SearchOption.AllDirectories))
        {
            string capture = Path.GetRelativePath(captures, log).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (!capture.StartsWith("local-test", StringComparison.Ordinal)) continue;
            foreach (string line in File.ReadLines(log))
            {
                if (!line.Contains("GameState.DebugPrintPower()", StringComparison.Ordinal)) continue;
                Match m = TagChange.Match(line);
                if (m.Success && !char.IsDigit(m.Groups[2].Value[0])) ours.Add((LiveKind(m.Groups[1].Value), m.Groups[2].Value));
            }
        }
        var cards = CardDb.Load(CardDb.FindDefaultDir(root));
        SabberEngineGame.EnsureCards(CardDb.FindDefaultDir(root));
        for (int i = 0; i < games; i++)
        {
            var rng = new Random(seed + i);
            var runner = new SoakRunner(rng)
            {
                ClientCards = cards,
                TagSent = (kind, tag) => ours.Add((kind, Enum.IsDefined(typeof(GAME_TAG), tag) ? ((GAME_TAG)tag).ToString() : tag.ToString())),
            };
            try { runner.Play(SoakRunner.RandomGame(rng, cards)); }
            catch (Exception) { /* a crashing game still counted what it sent */ }
        }

        var ignore = new Dictionary<string, string>();
        string ignoreFile = Path.Combine(audit, "wire-tag-ignore.txt");
        if (File.Exists(ignoreFile))
            foreach (string raw in File.ReadLines(ignoreFile))
            {
                string t = raw.Trim();
                if (t.Length == 0 || t.StartsWith('#')) continue;
                int sp = t.IndexOf(' ');
                ignore[sp < 0 ? t : t[..sp]] = sp < 0 ? "" : t[(sp + 1)..].Trim();
            }

        var oursTags = new HashSet<string>(ours.Select(o => o.Tag));
        var gaps = live.Where(kv => !ours.Contains(kv.Key)).OrderBy(kv => kv.Key.Kind).ThenByDescending(kv => kv.Value.Count).ToList();
        var report = new List<string>
        {
            $"# wirecheck {DateTime.Now:yyyy-MM-dd HH:mm}: {live.Count} live (kind, tag) pairs, {ours.Count} ours ({games} simulated games, seed {seed}).",
            "# kind tag live-count example-capture [note]. Fix a gap in the engine, or add the tag to wire-tag-ignore.txt with the reason.",
        };
        // 1. tags we never send on any entity (the real gaps); 2. tags we send, but not on this kind of entity (the live
        // log sometimes names an entity with a stale zone or by bare id, so these are often noise)
        int open = 0;
        var neverSent = gaps.Where(g => !oursTags.Contains(g.Key.Tag)).GroupBy(g => g.Key.Tag)
            .Select(g => (Tag: g.Key, Count: g.Sum(x => x.Value.Count), Kinds: string.Join(",", g.Select(x => x.Key.Kind)), Example: g.First().Value.Example))
            .OrderByDescending(g => g.Count).ToList();
        report.Add("## Never sent by us (on any entity)");
        foreach (var g in neverSent)
        {
            bool ignored = ignore.TryGetValue(g.Tag, out string why);
            if (!ignored) open++;
            string note = ignored ? "ignored: " + why : UnscriptedOnly(liveCards, g.Tag);
            report.Add($"{g.Tag,-45} {g.Count,5}  {g.Kinds,-20} {g.Example}  {note}");
        }
        report.Add("## Sent by us, but live also sends it on another kind of entity (often log noise)");
        foreach (var ((kind, tag), (count, example)) in gaps.Where(g => oursTags.Contains(g.Key.Tag)))
            report.Add($"{kind,-7} {tag,-45} {count,5}  {example}");
        Directory.CreateDirectory(audit);
        string outFile = Path.Combine(audit, $"wire-tag-gaps-{DateTime.Now:yyyyMMdd}.txt");
        File.WriteAllLines(outFile, report);
        Console.WriteLine(report[0]);
        Console.WriteLine($"{neverSent.Count} tags live sends that we never do ({open} open, {neverSent.Count - open} ignored); " +
                          $"{gaps.Count(g => oursTags.Contains(g.Key.Tag))} kind differences -> {Path.GetRelativePath(root, outFile)}");
        foreach (var g in neverSent.Where(g => !ignore.ContainsKey(g.Tag)).Take(60))
            Console.WriteLine($"  {g.Tag,-45} {g.Count,5}  {g.Kinds,-20} {g.Example}  {UnscriptedOnly(liveCards, g.Tag)}");
        return open == 0 ? 0 : 1;
    }

    // "needs card scripts: ..." when every card live sent the tag on has no script in our engine (the tag comes with them)
    private static string UnscriptedOnly(Dictionary<string, HashSet<string>> liveCards, string tag)
    {
        if (!liveCards.TryGetValue(tag, out var ids) || ids.Count == 0) return "";
        var unscripted = ids.Where(id => SabberStoneCore.Model.Cards.TryFromId(id) is not { Power: not null }).ToList();
        return unscripted.Count == ids.Count ? "needs card scripts: " + string.Join(", ", unscripted.Take(6)) : "";
    }

    // The live log names the entity as the client knew it: GameEntity, a player name, or [entityName=... zone=... cardId=...].
    private static string LiveKind(string entity)
    {
        if (entity == "GameEntity") return "game";
        if (!entity.StartsWith('[')) return char.IsDigit(entity[0]) ? "other" : "player";
        bool inPlay = entity.Contains("zone=PLAY", StringComparison.Ordinal);
        Match card = Regex.Match(entity, @"cardId=(\S*)");
        string id = card.Success ? card.Groups[1].Value : "";
        if (inPlay && id.StartsWith("HERO_", StringComparison.Ordinal) && !id.Contains("bp", StringComparison.Ordinal)) return "hero";
        if (entity.Contains("zone=HAND", StringComparison.Ordinal)) return "hand";
        return inPlay ? "play" : "other";
    }
}
