namespace HsServer.Protocol;

/// <summary>
/// Where the project and its game copy are (2026-11-15, user: the server can live inside a copied Hearthstone folder). The project
/// root is the folder holding Server\ (the repo). The game copy is the first that has Hearthstone.exe:
/// <list type="number">
/// <item>root\Hearthstone main game — the original layout (setup.bat made a copy next to Server\);</item>
/// <item>root\.. — the repo cloned into a copied game folder (PrivateHS\OpenHS-Server\Server);</item>
/// <item>root — the repo's contents unpacked straight into the game folder (PrivateHS\Server).</item>
/// </list>
/// Server\Directory.Build.props and run-local.bat / setup.bat look in the same order.
/// </summary>
public static class GameFolder
{
    public const string SideBySide = "Hearthstone main game";

    /// <summary>The folder holding Server\src, walking up from <paramref name="start"/> (default: this binary).</summary>
    public static string ProjectRoot(string start = null)
    {
        for (var dir = new DirectoryInfo(start ?? AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "Server", "src")) || Directory.Exists(Path.Combine(dir.FullName, SideBySide)))
                return dir.FullName;
        return Directory.GetCurrentDirectory();
    }

    /// <summary>The game copy for <paramref name="root"/>, or null when there is none yet (before setup).</summary>
    public static string Find(string root)
    {
        if (string.IsNullOrEmpty(root)) return null;
        foreach (string candidate in new[] { Path.Combine(root, SideBySide), Path.GetDirectoryName(Path.GetFullPath(root).TrimEnd('\\', '/')), root })
            if (!string.IsNullOrEmpty(candidate) && File.Exists(Path.Combine(candidate, "Hearthstone.exe")) &&
                Directory.Exists(Path.Combine(candidate, "Hearthstone_Data")))
                return Path.GetFullPath(candidate);
        return null;
    }

    /// <summary>The game copy, or where the original layout puts it (root\Hearthstone main game) when there is none yet.</summary>
    public static string GameDir(string root) => Find(root) ?? Path.Combine(root, SideBySide);
}
