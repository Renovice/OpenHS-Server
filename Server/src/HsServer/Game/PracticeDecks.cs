using System.Text.Json;
using HsServer.Lobby;

namespace HsServer.Game;

/// <summary>
/// The Innkeeper's deck in Practice (user 2026-10-04, for watching every card's effects): when set
/// (`admin practice decks &lt;name start&gt;`, saved in Server/state/practice.json, read at each game), the AI plays one of the
/// player's own decks whose name starts with that text and whose hero is of the Innkeeper's class — preferably not the deck
/// the player is using ("Legacy Mage A" against "Legacy Mage B"). Otherwise (no setting, no such deck) the usual random
/// Basic + Classic deck.
/// </summary>
internal static class PracticeDecks
{
    private static string FilePath => Path.Combine(Program.ProjectRoot(), "Server", "state", "practice.json");

    private sealed record Settings(string DeckPrefix);

    /// <summary>The deck-name start the Innkeeper's decks are taken from, or null (off).</summary>
    public static string Prefix
    {
        get
        {
            try
            {
                return File.Exists(FilePath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath))?.DeckPrefix : null;
            }
            catch (Exception e) when (e is IOException or JsonException)
            {
                return null;
            }
        }
        set
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Settings(string.IsNullOrWhiteSpace(value) ? null : value.Trim())));
        }
    }

    /// <summary>
    /// The card ids of the player's deck the Innkeeper plays: a deck named <see cref="Prefix"/>… of the class
    /// <paramref name="heroClass"/>, another than <paramref name="playerDeck"/> when there is one; null when off or none.
    /// </summary>
    public static List<string> Pick(DeckStore decks, long playerDeck, Func<int, string> heroClassOfDbf, string heroClass,
        Func<int, string> cardIdOfDbf, Random rng) =>
        Pick(Prefix, decks, playerDeck, heroClassOfDbf, heroClass, cardIdOfDbf, rng);

    internal static List<string> Pick(string prefix, DeckStore decks, long playerDeck, Func<int, string> heroClassOfDbf, string heroClass,
        Func<int, string> cardIdOfDbf, Random rng) =>
        PickDeck(prefix, decks, playerDeck, heroClassOfDbf, heroClass, rng) is long chosen
            ? decks.CardIds(chosen).Select(cardIdOfDbf).Where(id => id != null).ToList()
            : null;

    /// <summary>The id of the player's deck the Innkeeper plays (see <see cref="Pick(DeckStore, long, Func{int, string}, string, Func{int, string}, Random)"/>):
    /// its cards and its sideboards come from it; null when off or none.</summary>
    internal static long? PickDeck(string prefix, DeckStore decks, long playerDeck, Func<int, string> heroClassOfDbf, string heroClass, Random rng)
    {
        if (prefix == null || decks == null) return null;
        var mine = decks.All()
            .Where(d => d.Name != null && d.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        && heroClassOfDbf(d.Hero) == heroClass && decks.CardIds(d.Id).Count > 0)
            .ToList();
        if (mine.Count == 0) return null;
        var others = mine.Where(d => d.Id != playerDeck).ToList();
        var pool = others.Count > 0 ? others : mine;
        var chosen = pool[rng.Next(pool.Count)];
        Log.Info("practice", $"the Innkeeper plays \"{chosen.Name}\"");
        return chosen.Id;
    }
}
