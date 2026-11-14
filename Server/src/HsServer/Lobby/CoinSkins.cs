using System.Text.Json;

namespace HsServer.Lobby;

/// <summary>
/// Coin skins (client table CosmeticCoin: ID → the coin's card). Live (2026-10-08 audit, darkmoon / vfx-decks captures): the player's
/// coin skin is the starting Coin and every Coin an effect gives them (DMF_COIN1, the account's favorite). The deck's own coin wins,
/// else a random favorite (several favorites: the client's "random favorite" choice), else The Coin.
/// </summary>
internal static class CoinSkins
{
    private static Dictionary<int, int> cardOfCoin;
    private static readonly object gate = new();

    /// <summary>The coin skin's card id for a deck coin / the favorites (null: The Coin).</summary>
    public static string CardOf(string dbfDir, Engine.CardDb cards, int deckCoin, IReadOnlyList<int> favorites, Random rng)
    {
        int coin = deckCoin > 0 ? deckCoin : favorites is { Count: > 0 } ? favorites[rng.Next(favorites.Count)] : 0;
        if (coin <= 0 || dbfDir == null) return null;
        lock (gate) cardOfCoin ??= Load(dbfDir);
        return cardOfCoin.TryGetValue(coin, out int dbf) ? cards.ByDbf(dbf)?.CardId : null;
    }

    private static Dictionary<int, int> Load(string dbfDir)
    {
        var map = new Dictionary<int, int>();
        string path = Path.Combine(dbfDir, "CosmeticCoin.json");
        if (!File.Exists(path)) return map;
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (JsonElement r in doc.RootElement.GetProperty("records").EnumerateArray())
            if (r.TryGetProperty("ID", out var id) && r.TryGetProperty("CardId", out var card) && id.ValueKind == JsonValueKind.Number && card.ValueKind == JsonValueKind.Number)
                map[id.GetInt32()] = card.GetInt32();
        return map;
    }
}
