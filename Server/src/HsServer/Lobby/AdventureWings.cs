using System.Text.Json;

namespace HsServer.Lobby;

/// <summary>The client's Wing table (Wing.json): an adventure's wings.</summary>
internal static class AdventureWings
{
    /// <summary>(2026-11-14, user: every solo adventure open, like an owned one with no progress) every wing of every adventure.</summary>
    public static IReadOnlyList<int> All(string dbfDir)
    {
        string file = Path.Combine(dbfDir, "Wing.json");
        if (!File.Exists(file)) return Array.Empty<int>();
        using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
        return doc.RootElement.GetProperty("records").EnumerateArray()
            .Where(r => r.TryGetProperty("AdventureId", out JsonElement a) && a.ValueKind == JsonValueKind.Number && a.GetInt32() > 0)
            .Select(r => r.GetProperty("ID").GetInt32()).OrderBy(w => w).ToList();
    }

    public static IReadOnlyList<int> OfAdventure(string dbfDir, int adventureId)
    {
        string file = Path.Combine(dbfDir, "Wing.json");
        if (!File.Exists(file)) return Array.Empty<int>();
        using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
        return doc.RootElement.GetProperty("records").EnumerateArray()
            .Where(r => r.TryGetProperty("AdventureId", out JsonElement a) && a.ValueKind == JsonValueKind.Number && a.GetInt32() == adventureId)
            .Select(r => r.GetProperty("ID").GetInt32()).OrderBy(w => w).ToList();
    }
}
