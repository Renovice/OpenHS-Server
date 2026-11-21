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

    /// <summary>Close every connection logged in as this player (account logout / relog / login); returns how many.</summary>
    public async Task<int> DisconnectAsync()
    {
        LobbySession[] now;
        lock (sessions) now = sessions.ToArray();
        foreach (LobbySession s in now) await s.CloseAsync();
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

    // ---------- account management (2026-11-21, user: the "account" admin commands) ----------

    /// <summary>How long a deleted account can be restored (user 2026-11-21: 24 hours); after that it is gone for good.</summary>
    public static readonly TimeSpan RestoreWindow = TimeSpan.FromHours(24);

    /// <summary>The players' folder (Server/state/accounts or accounts-norecording).</summary>
    public string Root => root;

    private string ActiveFile => Path.Combine(root, "active.txt");
    private string DeletedRoot => root + "-deleted";

    /// <summary>A name the server accepts: letters, digits, '-' and '_', up to 24 (the same rule as a LOCAL: login).</summary>
    public static bool ValidName(string name) =>
        !string.IsNullOrEmpty(name) && name.Length <= 24 && name.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');

    /// <summary>The account a login without a name uses (the plain run-local.bat): set by "account login", default first.</summary>
    public string ActiveName
    {
        get
        {
            try
            {
                string name = File.Exists(ActiveFile) ? File.ReadAllText(ActiveFile).Trim() : null;
                return ValidName(name) ? name : DefaultName;
            }
            catch (IOException) { return DefaultName; }
        }
    }

    public void SetActive(string name)
    {
        if (!ValidName(name)) throw new ArgumentException($"not a valid account name: {name} (letters, digits, - and _, up to 24)");
        StateFiles.WriteAtomic(ActiveFile, System.Text.Encoding.UTF8.GetBytes(name));
    }

    /// <summary>Is the player logged in right now?</summary>
    public bool IsOnline(string name)
    {
        lock (gate) return loaded.TryGetValue(name, out PlayerAccount p) && p.IsOnline;
    }

    /// <summary>When the player last changed anything (their save files' newest write), or null.</summary>
    public DateTime? LastPlayed(string name)
    {
        string dir = Path.Combine(root, name.ToLowerInvariant());
        if (!Directory.Exists(dir)) return null;
        var files = Directory.GetFiles(dir);
        return files.Length == 0 ? null : files.Max(File.GetLastWriteTimeUtc);
    }

    /// <summary>A new account: empty (every mode open, like a first login), or a copy of <paramref name="from"/>'s cards,
    /// decks and save data with its own identity.</summary>
    public PlayerAccount Create(string name, string from = null)
    {
        if (!ValidName(name)) throw new ArgumentException($"not a valid account name: {name} (letters, digits, - and _, up to 24)");
        if (Exists(name)) throw new ArgumentException($"account {name} already exists");
        if (from != null)
        {
            if (!Exists(from)) throw new ArgumentException($"no account named {from}");
            Get(from); // (loaded: its files are current — every change is written at once)
            string source = Path.Combine(root, from.ToLowerInvariant()), target = Path.Combine(root, name.ToLowerInvariant());
            Directory.CreateDirectory(target);
            foreach (string file in new[] { "savedata.bin", "decks.bin", "collection.json" })
                if (File.Exists(Path.Combine(source, file))) File.Copy(Path.Combine(source, file), Path.Combine(target, file));
        }
        return Get(name);
    }

    /// <summary>Rename an account (not default, not while it is logged in). It keeps its ids, cards and progress.</summary>
    public void Rename(string oldName, string newName)
    {
        if (!ValidName(newName)) throw new ArgumentException($"not a valid account name: {newName} (letters, digits, - and _, up to 24)");
        if (oldName.Equals(DefaultName, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("the default account can't be renamed");
        if (!Exists(oldName)) throw new ArgumentException($"no account named {oldName}");
        if (Exists(newName)) throw new ArgumentException($"account {newName} already exists");
        if (IsOnline(oldName)) throw new ArgumentException($"{oldName} is logged in: log out first (account logout)");
        lock (gate)
        {
            loaded.Remove(oldName);
            string from = Path.Combine(root, oldName.ToLowerInvariant()), to = Path.Combine(root, newName.ToLowerInvariant());
            Directory.Move(from, to);
            string file = Path.Combine(to, "account.json");
            if (File.Exists(file))
            {
                Identity id = JsonSerializer.Deserialize<Identity>(File.ReadAllText(file));
                string number = id.BattleTag.Contains('#') ? id.BattleTag[id.BattleTag.IndexOf('#')..] : "#1000";
                StateFiles.WriteAtomic(file, JsonSerializer.SerializeToUtf8Bytes(id with { Name = newName, BattleTag = newName + number },
                    new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        if (ActiveName.Equals(oldName, StringComparison.OrdinalIgnoreCase)) SetActive(newName);
    }

    /// <summary>Delete an account (not default, not while logged in): it waits <see cref="RestoreWindow"/> in the deleted
    /// folder (<see cref="Restore"/>), then <see cref="PurgeExpired"/> removes it for good; <paramref name="now"/> removes it at once.</summary>
    public void Delete(string name, bool now)
    {
        if (name.Equals(DefaultName, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("the default account can't be deleted");
        if (!Exists(name)) throw new ArgumentException($"no account named {name}");
        if (IsOnline(name)) throw new ArgumentException($"{name} is logged in: log out first (account logout)");
        lock (gate)
        {
            loaded.Remove(name);
            string dir = Path.Combine(root, name.ToLowerInvariant());
            if (now) Directory.Delete(dir, recursive: true);
            else
            {
                Directory.CreateDirectory(DeletedRoot);
                Directory.Move(dir, Path.Combine(DeletedRoot, $"{name.ToLowerInvariant()}__{DateTime.UtcNow:yyyyMMddTHHmmssZ}"));
            }
        }
        if (ActiveName.Equals(name, StringComparison.OrdinalIgnoreCase)) SetActive(DefaultName);
    }

    /// <summary>The deleted accounts still in their restore window: name, when deleted (UTC), when they are gone.</summary>
    public IReadOnlyList<(string Name, DateTime Deleted, DateTime GoneAt)> Deleted()
    {
        var list = new List<(string, DateTime, DateTime)>();
        if (!Directory.Exists(DeletedRoot)) return list;
        foreach (string dir in Directory.GetDirectories(DeletedRoot))
            if (ParseDeleted(dir) is (string name, DateTime when) && when + RestoreWindow > DateTime.UtcNow)
                list.Add((name, when, when + RestoreWindow));
        return list.OrderBy(d => d.Item1, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Bring back a deleted account while its restore window is open (the newest delete of that name).</summary>
    public void Restore(string name)
    {
        PurgeExpired();
        if (Exists(name)) throw new ArgumentException($"an account named {name} exists again: rename it first");
        string dir = Directory.Exists(DeletedRoot)
            ? Directory.GetDirectories(DeletedRoot).Where(d => ParseDeleted(d) is (string n, _) && n.Equals(name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d, StringComparer.Ordinal).FirstOrDefault()
            : null;
        if (dir == null) throw new ArgumentException($"no deleted account named {name} to restore (they are kept {RestoreWindow.TotalHours:0} hours)");
        lock (gate) Directory.Move(dir, Path.Combine(root, name.ToLowerInvariant()));
    }

    /// <summary>Remove for good the deleted accounts whose restore window is over (at the server start and with every account
    /// command). Returns how many.</summary>
    public int PurgeExpired()
    {
        if (!Directory.Exists(DeletedRoot)) return 0;
        int n = 0;
        foreach (string dir in Directory.GetDirectories(DeletedRoot))
            if (ParseDeleted(dir) is (_, DateTime when) && when + RestoreWindow <= DateTime.UtcNow)
            {
                Directory.Delete(dir, recursive: true);
                n++;
            }
        return n;
    }

    private static (string Name, DateTime When)? ParseDeleted(string dir)
    {
        string leaf = Path.GetFileName(dir);
        int cut = leaf.LastIndexOf("__", StringComparison.Ordinal);
        if (cut <= 0) return null;
        return DateTime.TryParseExact(leaf[(cut + 2)..], "yyyyMMddTHHmmssZ", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime when)
            ? (leaf[..cut], when)
            : null;
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
