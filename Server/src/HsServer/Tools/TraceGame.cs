using HsServer.Sabber;

namespace HsServer.Tools;

/// <summary>`HsServer trace [seed=1] [maxLines=400]`: one random rules-engine game as the client receives it, Power.log style.</summary>
internal static class TraceGame
{
    public static int Run(string[] args)
    {
        int seed = args.Length > 0 ? int.Parse(args[0]) : 1;
        int maxLines = args.Length > 1 ? int.Parse(args[1]) : 400;
        string dir = Engine.CardDb.FindDefaultDir(Program.ProjectRoot());
        SabberEngineGame.EnsureCards(dir);
        var cards = Engine.CardDb.Load(dir);
        int lines = 0;
        var rng = new Random(seed);
        var runner = new SoakRunner(rng)
        {
            Trace = (label, text) =>
            {
                if (lines >= maxLines) return;
                Console.WriteLine($"===== {label}");
                foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    if (lines++ < maxLines) Console.WriteLine(line.TrimEnd('\r'));
            },
        };
        var result = runner.Play(SoakRunner.RandomGame(rng, cards));
        Console.WriteLine($"===== game over: {result.Over}, turns {result.Turns}, problems {result.Problems.Count}");
        return 0;
    }
}