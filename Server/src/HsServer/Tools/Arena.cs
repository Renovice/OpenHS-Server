using System.Diagnostics;
using HsServer.Engine;
using HsServer.Sabber;
using HsServer.Sabber.Ai;
using SabberStoneCore.Config;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;
using SGame = SabberStoneCore.Model.Game;

namespace HsServer.Tools;

/// <summary>
/// `HsServer arena [games=200] [seed=1] [aiA=greedy] [aiB=greedy]`: two AIs play each other on random decks (the soak's
/// mix: Basic+Classic, current Core, Wild) and the report says who's stronger AND how fast each one decides — win rate,
/// time per decision (average / 95th percentile / max) and per whole turn. Engine only (no client adapter, no history),
/// sides swapped every game, game i seeded seed+i (replayable).
/// </summary>
internal static class Arena
{
    private sealed class Stats
    {
        public int Wins, Losses, Draws;
        public readonly List<double> Decisions = new(), Turns = new();
    }

    public static int Run(string[] args)
    {
        int games = args.Length > 0 ? int.Parse(args[0]) : 200;
        int seed = args.Length > 1 ? int.Parse(args[1]) : 1;
        IGameAi a = GameAis.ByName(args.Length > 2 ? args[2] : "greedy"), b = GameAis.ByName(args.Length > 3 ? args[3] : "greedy");
        var cards = CardDb.Load(CardDb.FindDefaultDir(Program.ProjectRoot()));
        SabberEngineGame.EnsureCards(CardDb.FindDefaultDir(Program.ProjectRoot()));
        var stats = new Dictionary<IGameAi, Stats> { [a] = new(), [b] = new() };
        int errors = 0, turns = 0;
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < games; i++)
        {
            var rng = new Random(seed + i);
            (IGameAi p1, IGameAi p2) = i % 2 == 0 ? (a, b) : (b, a);
            try
            {
                SGame game = NewGame(rng);
                PlayState result = Play(game, p1, p2, stats, ref turns);
                Stats s1 = stats[p1], s2 = stats[p2];
                if (result == PlayState.WON) { s1.Wins++; s2.Losses++; }
                else if (result == PlayState.LOST) { s1.Losses++; s2.Wins++; }
                else { s1.Draws++; s2.Draws++; }
            }
            catch (Exception e)
            {
                errors++;
                if (errors <= 5) Console.Error.WriteLine($"game {seed + i}: {e}");
            }
        }

        Console.WriteLine($"arena: {games} games in {clock.Elapsed.TotalSeconds:F1}s ({turns / (double)Math.Max(1, games):F1} turns/game), {errors} errors");
        foreach ((IGameAi ai, Stats s) in stats)
        {
            int played = s.Wins + s.Losses + s.Draws;
            string label = a.Name == b.Name ? $"{ai.Name} {(ai == a ? "A" : "B")}" : ai.Name;
            Console.WriteLine($"  {label,-10} won {s.Wins,5} lost {s.Losses,5} drew {s.Draws,3}  win rate {100.0 * s.Wins / Math.Max(1, played),5:F1}%" +
                              $"  decision ms avg {Avg(s.Decisions):F2} p95 {Pct(s.Decisions, 95):F2} max {Max(s.Decisions):F1}" +
                              $"  turn ms avg {Avg(s.Turns):F1} p95 {Pct(s.Turns, 95):F1} max {Max(s.Turns):F0}");
        }
        return errors == 0 ? 0 : 1;
    }

    private static SGame NewGame(Random rng)
    {
        CardClass c1 = RandomDecks.Classes[rng.Next(RandomDecks.Classes.Length)], c2 = RandomDecks.Classes[rng.Next(RandomDecks.Classes.Length)];
        int kind = rng.Next(3);
        List<Card> Deck(CardClass c) => (kind == 0 ? RandomDecks.Build(c, rng, CardSet.CORE, CardSet.EXPERT1)
                : kind == 1 ? RandomDecks.BuildCurrentCore(c, rng) : RandomDecks.BuildWild(c, rng))
            .Select(Cards.TryFromId).Where(x => x != null).ToList();
        Card Hero(CardClass c) => Cards.TryFromId(RandomDecks.AnyHero(c, rng)) is { Type: CardType.HERO } h ? h : Cards.FromId("HERO_08");
        Card h1 = Hero(c1), h2 = Hero(c2);
        var game = new SGame(new GameConfig
        {
            StartPlayer = 1, Player1HeroCard = h1, Player1HeroClass = h1.Class, Player2HeroCard = h2, Player2HeroClass = h2.Class,
            Player1Deck = Deck(c1), Player2Deck = Deck(c2), FormatType = FormatType.FT_WILD,
            FillDecks = false, Shuffle = true, SkipMulligan = false, History = false, Logging = false, RandomSeed = rng.Next(),
        });
        game.StartGame();
        return game;
    }

    // Plays the game out; returns player 1's result.
    private static PlayState Play(SGame game, IGameAi p1, IGameAi p2, Dictionary<IGameAi, Stats> stats, ref int turns)
    {
        IGameAi AiOf(Controller c) => c == game.Player1 ? p1 : p2;
        int lastTurn = -1, movesThisTurn = 0;
        double turnMs = 0;
        var watch = new Stopwatch();
        while (game.State != State.COMPLETE)
        {
            Controller chooser = game.Player1.Choice != null ? game.Player1 : game.Player2.Choice != null ? game.Player2 : null;
            if (chooser != null)
            {
                game.Process(AiOf(chooser).Choose(game, chooser, chooser.Choice));
                continue;
            }
            Controller me = game.CurrentPlayer;
            if (game.Turn != lastTurn)
            {
                if (lastTurn >= 0) stats[AiOf(me.Opponent)].Turns.Add(turnMs);
                lastTurn = game.Turn; movesThisTurn = 0; turnMs = 0; turns++;
            }
            IGameAi ai = AiOf(me);
            watch.Restart();
            PlayerTask move = ai.Step(game, me);
            watch.Stop();
            stats[ai].Decisions.Add(watch.Elapsed.TotalMilliseconds);
            turnMs += watch.Elapsed.TotalMilliseconds;
            // a move that does nothing would repeat forever: end the turn instead (as the server does)
            if (!game.Process(move) || ++movesThisTurn > 40)
                game.Process(EndTurnTask.Any(me));
        }
        return game.Player1.PlayState;
    }

    private static double Avg(List<double> v) => v.Count == 0 ? 0 : v.Average();
    private static double Max(List<double> v) => v.Count == 0 ? 0 : v.Max();
    private static double Pct(List<double> v, int p)
    {
        if (v.Count == 0) return 0;
        var s = v.OrderBy(x => x).ToList();
        return s[Math.Min(s.Count - 1, (int)(s.Count * p / 100.0))];
    }
}
