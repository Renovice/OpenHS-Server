using System.Runtime.CompilerServices;
using HsServer.Protocol;

namespace HsServer.Tests;

internal static class TestEnvironment
{
    public static readonly string ProjectRoot = FindProjectRoot();

    [ModuleInitializer]
    internal static void Init() => GameAssemblies.Initialize();

    /// <summary>Every recorded session: live recorder folder plus backups under RESEARCH/captures.</summary>
    public static IReadOnlyList<string> Sessions => Recording.FindSessions(
        Path.Combine(ProjectRoot, "Hearthstone main game", "BepInEx", "HsRecorder"),
        Path.Combine(ProjectRoot, "RESEARCH", "captures"));

    private static string FindProjectRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "Hearthstone main game"))) return dir.FullName;
        throw new DirectoryNotFoundException("project root not found");
    }
}
