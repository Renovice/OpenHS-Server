using PegasusShared;
using PegasusUtil;
using HsServer.Protocol;

namespace HsServer.Lobby;

/// <summary>
/// Game save data (GameSaveKeyId → subkey → values): adventure/Heist run state, FTUE flags, client options...
/// Seeded from recorded GameSaveDataResponse (358) packets, updated by the client's SetGameSaveData (359).
/// Always emits freshly built messages so encodings stay minimal like Blizzard's.
/// </summary>
internal sealed class SaveDataStore
{
    private readonly object gate = new();
    private readonly Dictionary<long, SortedDictionary<long, GameSaveDataValue>> keys = new();

    public int KeyCount { get { lock (gate) return keys.Count; } }

    /// <summary>Forget every key (admin reset; seed again after).</summary>
    public void Clear() { lock (gate) keys.Clear(); }

    /// <summary>Where this player's save data lives (Server/state/accounts/&lt;name&gt;/savedata.bin); null = memory only.
    /// Written after every change, in the same GameSaveDataResponse form the server sends.</summary>
    public string FilePath { get; init; }

    /// <summary>Loads <see cref="FilePath"/>; false if there's nothing yet.</summary>
    public bool Load()
    {
        if (FilePath == null || !File.Exists(FilePath)) return false;
        lock (gate) keys.Clear();
        Seed(new[] { File.ReadAllBytes(FilePath) });
        return true;
    }

    public void Save()
    {
        if (FilePath == null) return;
        byte[] bytes;
        lock (gate)
        {
            var all = new GameSaveDataResponse { ErrorCode = 0, Data = new List<GameSaveDataUpdate>() };
            foreach (long key in keys.Keys.OrderBy(k => k)) all.Data.Add(KeyUpdate(key));
            bytes = PegasusPackets.Serialize(all);
        }
        StateFiles.WriteAtomic(FilePath, bytes);
    }

    public void Seed(IEnumerable<byte[]> recordedResponses)
    {
        foreach (byte[] body in recordedResponses)
        {
            var resp = PegasusPackets.Deserialize<GameSaveDataResponse>(body);
            foreach (GameSaveDataUpdate upd in resp.Data) Apply(upd);
        }
    }

    /// <summary>358 for a 357 request: one update per requested key, whole subkey map (empty keys included).</summary>
    public GameSaveDataResponse Get(GameSaveDataRequest request)
    {
        var response = new GameSaveDataResponse { ErrorCode = 0, ClientToken = request.ClientToken, Data = new List<GameSaveDataUpdate>() };
        lock (gate)
            foreach (long key in request.KeyIds)
                response.Data.Add(KeyUpdate(key));
        return response;
    }

    /// <summary>
    /// 360 for a 359 request: store every update and acknowledge with ErrorCode + ClientToken only. Live answers are
    /// always that bare 4-byte ack; echoing the key maps back made the client re-save every second.
    /// </summary>
    public SetGameSaveDataResponse Set(SetGameSaveData request)
    {
        lock (gate)
            foreach (GameSaveDataUpdate upd in request.Data) Apply(upd);
        Save();
        return new SetGameSaveDataResponse { ErrorCode = 0, ClientToken = request.ClientToken, Data = new List<GameSaveDataUpdate>() };
    }

    /// <summary>Every key the player has (the login state's GameSaveData: the client reads most of its save data from there —
    /// GameSaveDataManager.ApplyGameSaveDataFromInitialClientState — not from a 357 request).</summary>
    public List<GameSaveDataUpdate> AllUpdates()
    {
        lock (gate) return keys.Keys.Select(KeyUpdate).ToList();
    }

    public IReadOnlyList<long> Values(long key, long subkey)
    {
        lock (gate)
            return keys.TryGetValue(key, out var map) && map.TryGetValue(subkey, out var v) ? v.IntValue.ToList() : Array.Empty<long>();
    }

    public void SetInts(long key, long subkey, IEnumerable<long> values)
    {
        var v = NewValue();
        v.IntValue.AddRange(values);
        lock (gate) Map(key)[subkey] = v;
        Save();
    }

    // A recorded/received update is either [key] + map value (server form) or [key, subkey] + value (client form).
    private void Apply(GameSaveDataUpdate upd)
    {
        if (upd.Tuple.Count == 0) return;
        long key = upd.Tuple[0].Id;
        var map = Map(key);
        if (upd.Tuple.Count >= 2)
        {
            if (upd.Value != null) map[upd.Tuple[1].Id] = Clone(upd.Value);
            else map.Remove(upd.Tuple[1].Id);
            return;
        }
        if (upd.Value == null) return;
        for (int i = 0; i < upd.Value.MapKeys.Count && i < upd.Value.MapValues.Count; i++)
            map[upd.Value.MapKeys[i]] = Clone(upd.Value.MapValues[i]);
    }

    private SortedDictionary<long, GameSaveDataValue> Map(long key)
    {
        if (!keys.TryGetValue(key, out var map)) keys[key] = map = new SortedDictionary<long, GameSaveDataValue>();
        return map;
    }

    private GameSaveDataUpdate KeyUpdate(long key)
    {
        var update = new GameSaveDataUpdate { Tuple = new List<GameSaveKey>() };
        update.Tuple.Add(new GameSaveKey { Id = key });
        if (keys.TryGetValue(key, out var map) && map.Count > 0)
        {
            var value = NewValue();
            foreach (var (subkey, v) in map)
            {
                value.MapKeys.Add(subkey);
                value.MapValues.Add(Clone(v));
            }
            update.Value = value;
        }
        return update;
    }

    private static GameSaveDataValue NewValue() => new GameSaveDataValue
    {
        IntValue = new List<long>(), FloatValue = new List<double>(), StringValue = new List<string>(),
        MapKeys = new List<long>(), MapValues = new List<GameSaveDataValue>(),
    };

    // Fresh copy with only the list payloads: avoids re-emitting default fields flagged by deserialization.
    private static GameSaveDataValue Clone(GameSaveDataValue src)
    {
        var v = NewValue();
        v.IntValue.AddRange(src.IntValue);
        v.FloatValue.AddRange(src.FloatValue);
        v.StringValue.AddRange(src.StringValue);
        v.MapKeys.AddRange(src.MapKeys);
        foreach (var inner in src.MapValues) v.MapValues.Add(Clone(inner));
        return v;
    }
}
