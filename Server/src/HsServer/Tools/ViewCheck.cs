using HsServer.Engine;
using HsServer.Protocol;
using HsServer.Sabber;
using PegasusGame;

namespace HsServer.Tools;

/// <summary>
/// `HsServer viewcheck [session.jsonl|folder ...] [--ours] [--hidden]`: replays recorded games packet by packet into the client-view model
/// (<see cref="ClientView"/>) and lists every rule a packet broke. With no files: every live session (the live install's
/// HsRecorder folder and RESEARCH/captures, sessions against our own server left out) — the rules must hold there, since they
/// are live's own behaviour. Files or folders can be named instead; --ours reads only the sessions against our own server (what
/// the real client got from us). --hidden: the tags live gives hidden entities, per zone (where <see cref="ClientView.HiddenTags"/> comes from).
/// </summary>
internal static class ViewCheck
{
    public static int Run(string[] args)
    {
        bool hidden = args.Contains("--hidden");
        string root = Program.ProjectRoot();
        var named = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        // --ours: only the sessions against our own server (what the real client got from us); else only live's
        bool ours = args.Contains("--ours");
        if (named.Count == 0)
            named = new List<string> { @"C:\Program Files (x86)\Hearthstone\BepInEx\HsRecorder", Path.Combine(root, "RESEARCH", "captures") };
        List<string> sessions = named
            .SelectMany(a => Directory.Exists(a) ? Directory.EnumerateFiles(a, "session-*.jsonl", SearchOption.AllDirectories) : File.Exists(a) ? new[] { a } : Array.Empty<string>())
            .GroupBy(Path.GetFileName).Select(g => g.OrderByDescending(f => new FileInfo(f).Length).First()) // (live folder and backups overlap)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .Where(f => Recording.IsLocalServerSession(f) == ours).ToList();
        var cards = CardDb.Load(CardDb.FindDefaultDir(root));
        int historyId = PegasusPackets.IdOf<PowerHistory>();
        var kinds = new Dictionary<string, (int Count, string Example)>();
        var hiddenTags = new Dictionary<(int Zone, int Tag), (int Count, string Example)>();
        string where = "";
        bool identity = args.Contains("--identity");
        var identityStats = new Dictionary<(string Key, bool Same), (int Count, string Example)>();
        int games = 0, packets = 0, bad = 0;
        foreach (string session in sessions)
        {
            ClientView view = null;
            foreach (RecordedPacket p in Recording.Read(session))
            {
                if (p.Channel != "game" || !p.IsIn || p.Type != historyId || p.Body == null) continue;
                PowerHistory h = PegasusPackets.Deserialize<PowerHistory>(p.Body);
                where = $"{Path.GetFileName(session)} seq {p.Seq}";
                // a game is followed from its CREATE_GAME (a session recorded from mid-game is skipped up to the next one; a
                // reconnect's snapshot starts the picture again)
                if (h.List.Any(d => d.CreateGame != null))
                {
                    games++;
                    view = new ClientView
                    {
                        Cards = cards, CheckHiddenTags = !hidden,
                        HiddenTagSent = (zone, tag) => hiddenTags[(zone, tag)] = hiddenTags.TryGetValue((zone, tag), out var v) ? (v.Count + 1, v.Example) : (1, where),
                    };
                }
                if (view == null) continue;
                packets++;
                var found = new List<string>();
                view.Apply(h.List, found.Add);
                if (identity)
                    foreach (var (id, e) in view.Entities)
                        if (e.Known && cards.TryGet(e.CardId, out CardDef card) && e[GAME_TAG.CARDTYPE] is not (int)TAG_CARDTYPE.GAME and not (int)TAG_CARDTYPE.PLAYER)
                            foreach (GAME_TAG tag in new[] { GAME_TAG.CARDTYPE, GAME_TAG.CLASS, GAME_TAG.MULTIPLE_CLASSES, GAME_TAG.RARITY, GAME_TAG.SECRET, GAME_TAG.QUEST, GAME_TAG.SIDE_QUEST, GAME_TAG.COST, GAME_TAG.ATK, GAME_TAG.HEALTH })
                            {
                                var key = ($"{tag} {(TAG_CARDTYPE)card.Tag(GAME_TAG.CARDTYPE)} {(TAG_ZONE)e.Zone}", e[tag] == card.Tag(tag));
                                identityStats[key] = identityStats.TryGetValue(key, out var v) ? (v.Count + 1, v.Example) : (1, $"{where} #{id} {e.CardId} client={e[tag]} card={card.Tag(tag)}");
                            }
                if (found.Count > 0) bad++;
                foreach (string problem in found)
                {
                    string kind = System.Text.RegularExpressions.Regex.Replace(problem, @"\d+", "#");
                    kinds[kind] = kinds.TryGetValue(kind, out var k) ? (k.Count + 1, k.Example) : (1, $"{Path.GetFileName(session)} seq {p.Seq}: {problem}");
                }
            }
        }
        Console.WriteLine($"{sessions.Count} sessions, {games} games, {packets} power packets: {bad} broke a rule");
        foreach (var (kind, (count, example)) in kinds.OrderByDescending(k => k.Value.Count).Take(60))
            Console.WriteLine($"  {count,6}x {kind}\n          e.g. {example}");
        if (hidden)
        {
            Console.WriteLine("tags given to hidden entities (zone, tag: count):");
            foreach (var ((zone, tag), (count, example)) in hiddenTags.OrderBy(x => x.Key.Zone).ThenByDescending(x => x.Value.Count))
                Console.WriteLine($"  {(TAG_ZONE)zone,-16} {ClientView.Name(tag),-36} {count,6}  e.g. {example}");
        }
        if (identity)
            foreach (var group in identityStats.GroupBy(x => x.Key.Key).OrderBy(g => g.Key))
            {
                int same = group.Where(x => x.Key.Same).Sum(x => x.Value.Count), differ = group.Where(x => !x.Key.Same).Sum(x => x.Value.Count);
                Console.WriteLine($"  {group.Key,-40} same {same,8}  differ {differ,8}  {group.FirstOrDefault(x => !x.Key.Same).Value.Example}");
            }
        return bad == 0 ? 0 : 1;
    }
}
