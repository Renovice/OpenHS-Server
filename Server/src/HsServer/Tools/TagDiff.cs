using HsServer.Engine;
using HsServer.Protocol;
using HsServer.Sabber;
using PegasusGame;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace HsServer.Tools;

/// <summary>
/// `HsServer tagdiff [session.jsonl ...] [top=N] [tag=NAME|ID] [all]`: compares the tags live shows for each card (its first
/// FULL_ENTITY / SHOW_ENTITY in the recordings) with the tags our server sends for the same card, and reports per tag how
/// many cards miss it or carry another value. With no files it reads every RESEARCH/captures/*/session-*.jsonl. A tag
/// the client needs but we never send (TRADEABLE was one) shows up here at once.
/// </summary>
internal static class TagDiff
{
    // Where the card is and what happened to it this game — not the card's own data.
    private static readonly HashSet<int> StateTags = new[]
    {
        GAME_TAG.ZONE, GAME_TAG.ZONE_POSITION, GAME_TAG.CONTROLLER, GAME_TAG.ENTITY_ID, GAME_TAG.CREATOR,
        GAME_TAG.CREATOR_DBID, GAME_TAG.DISPLAYED_CREATOR, GAME_TAG.NUM_TURNS_IN_HAND, GAME_TAG.TAG_LAST_KNOWN_COST_IN_HAND,
        GAME_TAG.DAMAGE, GAME_TAG.EXHAUSTED, GAME_TAG.JUST_PLAYED, GAME_TAG.PREMIUM, GAME_TAG.CARD_TARGET, GAME_TAG.ATTACHED,
        GAME_TAG.NUM_ATTACKS_THIS_TURN, GAME_TAG.REVEALED, GAME_TAG.OWNER,
    }.Select(t => (int)t).ToHashSet();

    public static int Run(string[] args)
    {
        string root = Program.ProjectRoot();
        int top = int.TryParse(Arg(args, "top"), out int n) ? n : 40;
        string only = Arg(args, "tag");
        var files = args.Where(a => !a.Contains('=') && a != "all").ToList();
        if (files.Count == 0)
            files = Directory.GetDirectories(Path.Combine(root, "RESEARCH", "captures"))
                .Where(d => !Path.GetFileName(d).StartsWith("local-test")) // our own server's games, not live
                .SelectMany(d => Directory.GetFiles(d, "session-*.jsonl")).ToList();

        // Live: each card's tags the first time an entity of it is shown.
        var live = new Dictionary<string, Dictionary<int, int>>();
        int historyId = PegasusPackets.IdOf<PowerHistory>();
        foreach (string file in files)
        {
            var seen = new HashSet<int>();
            foreach (RecordedPacket p in Recording.Read(file))
            {
                if (p.Channel != "game" || p.Body == null || !p.IsIn || p.Type != historyId) continue;
                foreach (PowerHistoryData d in PegasusPackets.Deserialize<PowerHistory>(p.Body).List)
                {
                    PowerHistoryEntity e = d.FullEntity ?? d.ShowEntity;
                    if (e == null || string.IsNullOrEmpty(e.Name) || !seen.Add(e.Entity) || live.ContainsKey(e.Name)) continue;
                    live[e.Name] = e.Tags.GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.Last().Value);
                }
            }
        }

        // Ours: the same card created in a game, as the server sends it.
        SabberEngineGame.EnsureCards(CardDb.FindDefaultDir(root));
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false,
        });
        game.StartGame();
        var stats = new Dictionary<int, (int Missing, int Extra, int Differs, List<string> Examples)>();
        int compared = 0;
        var notLoaded = new List<string>();
        var failed = new List<string>();
        foreach ((string cardId, Dictionary<int, int> liveTags) in live.OrderBy(k => k.Key))
        {
            Card card = Cards.TryFromId(cardId);
            if (card == null) { notLoaded.Add(cardId); continue; }
            if (card.Type == SabberStoneCore.Enums.CardType.ENCHANTMENT) continue;
            Dictionary<int, int> ours;
            try
            {
                IPlayable entity = SabberStoneCore.Model.Entities.Entity.FromCard(game.Player1, card, zone: game.Player1.SetasideZone);
                ours = SabberStoneCore.Kettle.PowerHistoryBuilder.FullEntity(entity).Entity.Tags
                    .ToDictionary(t => (int)t.Key, t => t.Value);
            }
            catch (Exception ex) { failed.Add($"{cardId}: {ex.GetType().Name}"); continue; }
            compared++;
            foreach (int tag in liveTags.Keys.Union(ours.Keys))
            {
                if (StateTags.Contains(tag)) continue;
                int l = liveTags.TryGetValue(tag, out int lv) ? lv : 0, o = ours.TryGetValue(tag, out int ov) ? ov : 0;
                if (l == o) continue;
                var s = stats.TryGetValue(tag, out var x) ? x : (Missing: 0, Extra: 0, Differs: 0, Examples: new List<string>());
                if (o == 0) s.Missing++; else if (l == 0) s.Extra++; else s.Differs++;
                if (s.Examples.Count < 4) s.Examples.Add($"{cardId} live={l} ours={o}");
                stats[tag] = s;
            }
        }

        Console.WriteLine($"{live.Count} cards seen live in {files.Count} recording(s); {compared} compared; " +
                          $"{notLoaded.Count} not loaded by the engine; {failed.Count} could not be created.");
        Console.WriteLine("   tag  name                                   missing  extra differs  examples");
        // The client can only read tags its GAME_TAG enum names; the unnamed ones live sends are server bookkeeping.
        bool all = args.Contains("all");
        int hidden = stats.Keys.Count(t => !Enum.IsDefined(typeof(GAME_TAG), t));
        if (!all && only == null && hidden > 0) Console.WriteLine($"({hidden} tags the client has no name for are hidden; `all` shows them)");
        foreach (var (tag, s) in stats.Where(k => only == null ? all || Enum.IsDefined(typeof(GAME_TAG), k.Key)
                     : Name(k.Key).Equals(only, StringComparison.OrdinalIgnoreCase) || k.Key.ToString() == only)
                     .OrderByDescending(k => k.Value.Missing + k.Value.Differs).ThenByDescending(k => k.Value.Extra).Take(top))
            Console.WriteLine($"{tag,6}  {Name(tag),-38} {s.Missing,7} {s.Extra,6} {s.Differs,7}  {string.Join("; ", s.Examples)}");
        if (notLoaded.Count > 0) Console.WriteLine($"not loaded: {string.Join(", ", notLoaded.Take(30))}{(notLoaded.Count > 30 ? " ..." : "")}");
        if (failed.Count > 0) Console.WriteLine($"could not create: {string.Join(", ", failed.Take(30))}");
        return 0;
    }

    private static string Name(int tag) => Enum.IsDefined(typeof(GAME_TAG), tag) ? ((GAME_TAG)tag).ToString() : $"tag {tag}";

    private static string Arg(string[] args, string name) =>
        args.FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))?.Substring(name.Length + 1);
}
