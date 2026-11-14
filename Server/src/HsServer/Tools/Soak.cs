using HsServer.Sabber;

namespace HsServer.Tools;

/// <summary>
/// `HsServer soak [games=100] [seed=1]`: plays games between random Basic+Classic decks on the rules engine through the
/// full client adapter (see <see cref="SoakRunner"/>) and reports every problem found, grouped by kind.
/// HS_SOAK_RESUME=<n>: the client also reconnects every n steps (A4: the resume snapshot must match what it knew).
/// Every packet goes through the client-view check (<see cref="Sabber.ClientView"/> + SoakRunner.Compare); a problem is printed
/// with the packet that broke it.
/// </summary>
internal static class Soak
{
    private static int ResumeEvery() => int.TryParse(Environment.GetEnvironmentVariable("HS_SOAK_RESUME"), out int n) ? n : 0;

    public static int Run(string[] args)
    {
        int games = args.Length > 0 ? int.Parse(args[0]) : 100;
        int seed = args.Length > 1 ? int.Parse(args[1]) : 1;
        string root = Program.ProjectRoot();
        var cards = Engine.CardDb.Load(Engine.CardDb.FindDefaultDir(root));
        SabberEngineGame.EnsureCards(Engine.CardDb.FindDefaultDir(root));

        int clean = 0, crashed = 0;
        long turns = 0, packets = 0, actions = 0, over = 0;
        var kinds = new Dictionary<string, (int Count, string Example, string Packet)>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            var rng = new Random(seed + i);
            List<string> problems;
            var offending = new Dictionary<string, string>();
            try
            {
                // A game that never ends (an effect looping forever) is reported by seed and stops the soak.
                var runner = new SoakRunner(rng) { ClientCards = cards, ResumeEvery = ResumeEvery() };
                var game = SoakRunner.RandomGame(rng, cards);
                var play = System.Threading.Tasks.Task.Run(() => runner.Play(game));
                if (!play.Wait(TimeSpan.FromSeconds(30)))
                {
                    Console.WriteLine($"HANG: game seed {seed + i} did not finish in 30 s (last move: {game.LastMove}); stopping. Trace it with `HsServer trace {seed + i}`.");
                    Console.WriteLine($"  last task started: {SabberStoneCore.Model.Game.LastTask} (source {SabberStoneCore.Model.Game.LastTaskSource})");
                    SabberStoneCore.Model.Game.AbortRequested = true; // the stuck engine throws at its next task; the adapter logs where
                    play.Wait(TimeSpan.FromSeconds(5));
                    return 2;
                }
                var result = play.Result;
                problems = result.Problems;
                offending = runner.Offending;
                turns += result.Turns; packets += result.Packets; actions += result.HumanActions; over += result.Over ? 1 : 0;
            }
            catch (Exception e)
            {
                crashed++;
                // (the game runs in a task: report the real exception and where it was thrown, a few frames deep)
                Exception inner = e is AggregateException ae ? ae.Flatten().InnerException ?? e : e;
                string at = string.Join(" <- ", (inner.StackTrace ?? "").Split('\n').Where(l => l.Contains(" in ")).Take(4)
                    .Select(l => System.Text.RegularExpressions.Regex.Replace(l.Trim(), @"^at |\(.*?\) in .*[\\/]", "")));
                problems = new List<string> { $"crash: {inner.GetType().Name}: {inner.Message} @ {at}" };
            }
            if (problems.Count == 0) { clean++; continue; }
            foreach (string p in problems.Take(3))
            {
                // Group by the message without numbers (entity ids, steps) so the same bug counts once.
                string kind = System.Text.RegularExpressions.Regex.Replace(p, @"\d+", "#");
                kinds[kind] = kinds.TryGetValue(kind, out var k) ? (k.Count + 1, k.Example, k.Packet)
                    : (1, $"game seed {seed + i}: {p}", offending.GetValueOrDefault(p));
            }
        }
        Console.WriteLine($"{games} games in {sw.Elapsed.TotalSeconds:0.0}s: {clean} clean, {games - clean} with problems ({crashed} crashed)");
        Console.WriteLine($"  per game: {(double)turns / games:0.0} turns, {(double)actions / games:0.0} player-1 actions, {(double)packets / games:0.0} packets; finished {over}/{games}");
        int shown = 0;
        foreach (var (kind, (count, example, packet)) in kinds.OrderByDescending(k => k.Value.Count).Take(40))
        {
            Console.WriteLine($"  {count,4}x {kind}\n        e.g. {example}");
            // the packet the client got (the client-view check's offending packet, or the last one before an engine mismatch)
            if (packet != null && shown++ < 8)
            {
                // HS_SOAK_LINES=<n>: how much of it (default 60)
                int max = int.TryParse(Environment.GetEnvironmentVariable("HS_SOAK_LINES"), out int n) ? n : 60;
                string[] lines = packet.TrimEnd().Split('\n');
                foreach (string line in lines.Take(max)) Console.WriteLine($"          | {line.TrimEnd()}");
                if (lines.Length > max) Console.WriteLine($"          | ({lines.Length - max} more lines: HS_SOAK_LINES=<n>)");
            }
        }
        return clean == games ? 0 : 1;
    }
}
