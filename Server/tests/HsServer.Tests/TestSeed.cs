// HsServer (2026-11-20): every game a test makes without a seed uses the same one, so random decks and random effects play the same
// way every run (no flakes: a failure is real and reproducible). The real server leaves GameConfig.DefaultRandomSeed null.
using System.Runtime.CompilerServices;

internal static class TestSeed
{
    [ModuleInitializer]
    internal static void Init()
    {
        SabberStoneCore.Config.GameConfig.DefaultRandomSeed = 20261120;
        SabberStoneCore.Config.GameConfig.PlainPredictableFill = true; // (plain filler: nothing in it acts)
    }
}

/// <summary>(2026-11-21) Tests that capture the console (Console.SetOut is process-wide) run alone: another test's console output
/// would land in their capture (LiveCheckTests' power-dump line check failed now and then in a full run).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleCapture
{
    public const string Name = "ConsoleCapture";
}
