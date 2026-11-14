using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;
using SGame = SabberStoneCore.Model.Game;

namespace HsServer.Sabber.Ai;

/// <summary>
/// An AI player for the rules engine (roadmap A3). <see cref="Step"/> picks the next move on the AI's turn,
/// <see cref="Choose"/> answers an open choice (mulligan, Discover...). Implementations must be fast: the server calls them
/// between animations, and `HsServer arena` measures both strength and time per decision.
/// </summary>
internal interface IGameAi
{
    string Name { get; }
    PlayerTask Step(SGame game, Controller me);
    PlayerTask Choose(SGame game, Controller me, Choice choice);
}

/// <summary>The AIs `HsServer arena` and the server know by name.</summary>
internal static class GameAis
{
    // Any other setting names a BoardScore weight (case-insensitive): planner:card=1.5,cardcost=0.2,unspentmana=0.3
    private static BoardScore Weights(Dictionary<string, double> settings)
    {
        var score = new BoardScore();
        foreach (var prop in typeof(BoardScore).GetProperties())
            if (settings.TryGetValue(prop.Name.ToLowerInvariant(), out double v)) prop.SetValue(score, v);
        return score;
    }

    /// <summary>The opponent for a scenario mode (client Scenario.ModeId): Normal (1) — a small search that misplays half the
    /// time, about the old one-move AI's strength (arena: 50.9% vs greedy); Expert (2) and anything else — the full planner
    /// (73.5% vs greedy). Heist bosses will get their own presets.</summary>
    public static IGameAi ForMode(int mode) => mode == 1
        ? ByName("planner:beam=2,replies=0,mistakes=0.5")
        : ByName("planner");

    /// <summary>"greedy", "planner", or "planner:beam=10,depth=14,budget=150" (settings for tuning in the arena; slots=0 =
    /// every minion at the default far-right slot, the old behaviour).</summary>
    public static IGameAi ByName(string name)
    {
        string[] parts = name.ToLowerInvariant().Split(':', 2);
        var settings = parts.Length > 1
            ? parts[1].Split(',').Select(kv => kv.Split('=')).ToDictionary(kv => kv[0], kv => double.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture))
            : new Dictionary<string, double>();
        double Get(string key, double fallback) => settings.TryGetValue(key, out double v) ? v : fallback;
        return parts[0] switch
        {
            "greedy" => new GreedyAi(),
            "planner" => new PlannerAi
            {
                Label = name, BeamWidth = (int)Get("beam", 12), MaxDepth = (int)Get("depth", 14), BudgetMs = Get("budget", 150), Replies = (int)Get("replies", 8),
                Mistakes = Get("mistakes", 0), Mulligan = Get("curve", 0) > 0 ? "curve" : "cheap", Weights = Weights(settings),
                Slots = Get("slots", 1) > 0,
            },
            _ => throw new ArgumentException($"unknown AI '{name}' (known: greedy, planner[:beam=,depth=,budget=])"),
        };
    }
}


/// <summary>The first AI: one-move look-ahead (<see cref="SabberAi"/>), kept as the baseline the arena measures against.</summary>
internal sealed class GreedyAi : IGameAi
{
    public string Name => "greedy";
    public PlayerTask Step(SGame game, Controller me) => SabberAi.Step(game, me);
    public PlayerTask Choose(SGame game, Controller me, Choice choice) => SabberAi.Choose(game, me, choice);
}
