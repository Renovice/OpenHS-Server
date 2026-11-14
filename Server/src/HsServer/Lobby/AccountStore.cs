using System.Text.Json;
using HsServer.Protocol;

namespace HsServer.Lobby;

/// <summary>One player of the local server: identity plus the state the server keeps for them.</summary>
internal sealed class PlayerAccount
{
    public required string Name { get; init; }
    public required ulong AccountId { get; init; }
    public required ulong GameAccountId { get; init; }
    public required string BattleTag { get; init; }
    /// <summary>Game save data (Heist runs, FTUE flags, options) — persisted on every change.</summary>
    public required SaveDataStore SaveData { get; init; }
    public required DeckStore Decks { get; init; }
    /// <summary>Cards, dust, gold (B2) — persisted on every change; null when the server runs without card data.</summary>
    public CollectionStore Collection { get; init; }

    // The lobby sessions this player is logged in with (admin changes are pushed to them).
    private readonly List<LobbySession> sessions = new();
    internal void Attach(LobbySession session) { lock (sessions) sessions.Add(session); }
    internal void Detach(LobbySession session) { lock (sessions) sessions.Remove(session); }
    public bool IsOnline { get { lock (sessions) return sessions.Count > 0; } }

    /// <summary>Push a util notification (e.g. a 333 collection change) to every client logged in as this player;
    /// returns how many got it.</summary>
    public async Task<int> NotifyAsync(IProtoBuf message)
    {
        LobbySession[] now;
        lock (sessions) now = sessions.ToArray();
        foreach (LobbySession s in now) await s.NotifyUtilAsync(message);
        return now.Length;
    }
}

/// <summary>
/// Player accounts (roadmap B1). The client plugin logs in with a token: the old bootstrap token is the "default" player
/// (the recorded account's ids, so nothing changes for it), "LOCAL:&lt;name&gt;" is player &lt;name&gt; (created on first
/// login). Each player has a folder Server/state/accounts/&lt;name&gt;/ with account.json, savedata.bin, decks.bin, all
/// written at once on every change (atomic replace, retried when OneDrive briefly locks a file).
/// </summary>
internal sealed class AccountStore
{
    public const string DefaultName = "default";
    public const string TokenPrefix = "LOCAL:";

    private readonly string root;
    private readonly RecordedAccount reference;
    private readonly Action<SaveDataStore> seedSaveData;
    private readonly Action<DeckStore> seedDecks;
    private readonly string legacyDecks;
    /// <summary>B2: a new player's collection (the recorded one) and the cards everyone owns (free Core set, topped up at
    /// every load).</summary>
    public Action<CollectionStore> SeedCollection { get; init; }
    public IReadOnlyList<(int Dbf, int Count)> Grants { get; init; }
    private readonly object gate = new();
    private readonly Dictionary<string, PlayerAccount> loaded = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="seedSaveData">fills a new player's save data (from the recordings)</param>
    /// <param name="seedDecks">fills a new player's decks (from the reference session's deck list)</param>
    /// <param name="legacyDecks">the old single decks.bin: copied (not moved) into the default player on first use</param>
    public AccountStore(string root, RecordedAccount reference, Action<SaveDataStore> seedSaveData, Action<DeckStore> seedDecks, string legacyDecks = null)
    {
        this.root = root;
        this.reference = reference;
        this.seedSaveData = seedSaveData;
        this.seedDecks = seedDecks;
        this.legacyDecks = legacyDecks;
    }

    /// <summary>The player a login token names: the bootstrap token = default, "LOCAL:name" = name; null = not a token we
    /// accept (names: letters, digits, '-' and '_', up to 24).</summary>
    public static string NameFromToken(string token)
    {
        if (token == LobbySession.LocalAuthToken) return DefaultName;
        if (token == null || !token.StartsWith(TokenPrefix, StringComparison.Ordinal)) return null;
        string name = token[TokenPrefix.Length..];
        return name.Length is > 0 and <= 24 && name.All(c => char.IsLetterOrDigit(c) || c is '-' or '_') ? name : null;
    }

    /// <summary>Every player with a folder (loaded or not).</summary>
    public IReadOnlyList<string> Names()
    {
        lock (gate)
            return Directory.Exists(root)
                ? Directory.GetDirectories(root).Select(Path.GetFileName).Concat(loaded.Keys.Select(k => k.ToLowerInvariant()))
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
                : loaded.Keys.ToList();
    }

    /// <summary>Does this player exist (folder or loaded)? Get() creates one.</summary>
    public bool Exists(string name)
    {
        lock (gate) return loaded.ContainsKey(name) || Directory.Exists(Path.Combine(root, name.ToLowerInvariant()));
    }

    /// <summary>Admin: start a part of a player's state over from the recordings: "collection" (cards, dust, favorites),
    /// "savedata" (Heist runs, FTUE flags, options), "decks". Returns false for an unknown part.</summary>
    public bool Reset(PlayerAccount account, string part)
    {
        switch (part)
        {
            case "collection" when account.Collection != null:
                account.Collection.Reseed(SeedCollection, Grants);
                return true;
            case "savedata":
                account.SaveData.Clear();
                seedSaveData?.Invoke(account.SaveData);
                account.SaveData.Save();
                return true;
            case "decks":
                seedDecks?.Invoke(account.Decks);
                return true;
            default:
                return false;
        }
    }

    public PlayerAccount Get(string name)
    {
        lock (gate)
        {
            if (loaded.TryGetValue(name, out PlayerAccount known)) return known;
            string dir = Path.Combine(root, name.ToLowerInvariant());
            Directory.CreateDirectory(dir);
            Identity id = LoadIdentity(dir, name);

            var saveData = new SaveDataStore { FilePath = Path.Combine(dir, "savedata.bin") };
            if (!saveData.Load())
            {
                seedSaveData?.Invoke(saveData);
                saveData.Save();
            }

            string decksPath = Path.Combine(dir, "decks.bin");
            if (!File.Exists(decksPath) && name.Equals(DefaultName, StringComparison.OrdinalIgnoreCase) && legacyDecks != null && File.Exists(legacyDecks))
                File.Copy(legacyDecks, decksPath); // the default player keeps the decks made before accounts existed
            var decks = new DeckStore(decksPath);
            if (!decks.Load()) seedDecks?.Invoke(decks);

            CollectionStore collection = null;
            if (SeedCollection != null || Grants != null)
            {
                collection = new CollectionStore(Path.Combine(dir, "collection.json"));
                if (!collection.Load()) SeedCollection?.Invoke(collection);
                collection.Grant(Grants);
            }

            var account = new PlayerAccount
            {
                Name = id.Name, AccountId = id.AccountId, GameAccountId = id.GameAccountId, BattleTag = id.BattleTag,
                SaveData = saveData, Decks = decks, Collection = collection,
            };
            loaded[name] = account;
            Log.Info("accounts", $"player {account.Name} ({account.BattleTag}) loaded from {Path.GetRelativePath(root, dir)}");
            return account;
        }
    }

    private sealed record Identity(string Name, ulong AccountId, ulong GameAccountId, string BattleTag, DateTime Created);

    private Identity LoadIdentity(string dir, string name)
    {
        string file = Path.Combine(dir, "account.json");
        if (File.Exists(file))
            return JsonSerializer.Deserialize<Identity>(File.ReadAllText(file));
        Identity id;
        if (name.Equals(DefaultName, StringComparison.OrdinalIgnoreCase))
            id = new Identity(DefaultName, reference.AccountId, reference.GameAccountId, reference.BattleTag, DateTime.UtcNow);
        else
        {
            uint hash = Fnv(name.ToLowerInvariant());
            id = new Identity(name, reference.AccountId + 1 + (hash % 0x7FFFFF), reference.GameAccountId + 1 + (hash % 0x7FFFFF),
                $"{name}#{1000 + hash % 9000}", DateTime.UtcNow);
        }
        StateFiles.WriteAtomic(file, JsonSerializer.SerializeToUtf8Bytes(id, new JsonSerializerOptions { WriteIndented = true }));
        return id;
    }

    private static uint Fnv(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) { h ^= c; h *= 16777619; }
        return h;
    }
}

/// <summary>Writing server state files safely.</summary>
internal static class StateFiles
{
    /// <summary>Write to a temp file, then replace the target in one step; OneDrive can lock a file for a moment, so a
    /// failed replace is retried a few times.</summary>
    public static void WriteAtomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.WriteAllBytes(temp, bytes);
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 6)
            {
                Thread.Sleep(50 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < 6)
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }
}
