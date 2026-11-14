using System.Reflection;
using System.Runtime.Loader;

namespace HsServer.Protocol;

/// <summary>
/// Resolves the client's protocol assemblies (bgssdk, bgsclient, Google.Protobuf and their dependencies) from the
/// local Hearthstone install. Must run before any Pegasus/BGS type is touched.
/// </summary>
public static class GameAssemblies
{
    private static string managedDir;

    public static string ManagedDir => managedDir;

    public static void Initialize(string managed = null)
    {
        if (managedDir != null) return;
        managedDir = Path.GetFullPath(managed ?? Environment.GetEnvironmentVariable("HS_MANAGED") ?? FindDefault());
        if (!File.Exists(Path.Combine(managedDir, "blizzard.bgsclient.dll")))
            throw new DirectoryNotFoundException($"Hearthstone Managed folder not found: {managedDir} (set HS_MANAGED)");
        AssemblyLoadContext.Default.Resolving += Resolve;
    }

    private static Assembly Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        string file = Path.Combine(managedDir, name.Name + ".dll");
        return File.Exists(file) ? context.LoadFromAssemblyPath(file) : null;
    }

    // Walk up from the binary until "Hearthstone main game" (project layout) is found.
    private static string FindDefault()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "Hearthstone main game", "Hearthstone_Data", "Managed");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("Could not locate 'Hearthstone main game' above " + AppContext.BaseDirectory + " (set HS_MANAGED)");
    }
}
