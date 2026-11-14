using PegasusGame;

namespace HsServer.Engine;

/// <summary>
/// Accumulates power history entries and interleaves them with other game packets (choices, options) in the exact
/// order the client must receive them. Pending history is flushed into one PowerHistory packet whenever a
/// non-history packet is queued.
/// </summary>
internal sealed class History
{
    private readonly List<PowerHistoryData> pending = new();
    private readonly List<IProtoBuf> outbox = new();

    public int PendingCount => pending.Count;

    public void Add(PowerHistoryData data) => pending.Add(data);

    public void TagChange(int entity, GAME_TAG tag, int value) =>
        pending.Add(new PowerHistoryData { TagChange = new PowerHistoryTagChange { Entity = entity, Tag = (int)tag, Value = value } });

    public void BlockStart(HistoryBlock.Type type, int source, int target = 0, int effectIndex = -1, string effectCardId = "", int subOption = -1) =>
        pending.Add(new PowerHistoryData
        {
            PowerStart = new PowerHistoryStart
            {
                Type = type, Source = source, Target = target, SubOption = subOption,
                EffectIndex = effectIndex, EffectCardId = effectCardId,
            },
        });

    public void BlockEnd() => pending.Add(new PowerHistoryData { PowerEnd = new PowerHistoryEnd() });

    private int slushMs;

    /// <summary>
    /// Milliseconds of SLUSH_TIME reported since the last call: Blizzard's own estimate of how long the client needs
    /// to animate what was just sent. The server paces AI actions with it.
    /// </summary>
    public int TakeSlushMs()
    {
        int ms = slushMs;
        slushMs = 0;
        return ms;
    }

    public void Meta(HistoryMeta.Type type, int data, params int[] info)
    {
        if (type == HistoryMeta.Type.SLUSH_TIME) slushMs += data;
        var meta = new PowerHistoryMetaData { Type = type, Data = data, Info = new List<int>(info), AdditionalData = new List<int>() };
        pending.Add(new PowerHistoryData { MetaData = meta });
    }

    /// <summary>Position in the pending history, for <see cref="AffectedSince"/> / <see cref="InsertMeta"/>.</summary>
    public (int Flushes, int Index) Mark() => (flushes, pending.Count);

    /// <summary>
    /// Characters an effect acted on since <paramref name="mark"/>, in first-touched order: DAMAGE/HEALING metadata
    /// targets and characters that became FROZEN. Empty if the history was flushed in between.
    /// </summary>
    public List<int> AffectedSince((int Flushes, int Index) mark)
    {
        var affected = new List<int>();
        if (mark.Flushes != flushes) return affected;
        for (int i = mark.Index; i < pending.Count; i++)
        {
            PowerHistoryData d = pending[i];
            if (d.MetaData is { Type: HistoryMeta.Type.DAMAGE or HistoryMeta.Type.HEALING } m)
                affected.AddRange(m.Info);
            else if (d.TagChange is { Tag: (int)GAME_TAG.FROZEN, Value: 1 } t)
                affected.Add(t.Entity);
        }
        return affected.Distinct().ToList();
    }

    /// <summary>Insert a metadata entry at a marked position (e.g. the TARGET list at the top of an effect block).</summary>
    public void InsertMeta((int Flushes, int Index) mark, HistoryMeta.Type type, int data, IEnumerable<int> info)
    {
        var meta = new PowerHistoryMetaData { Type = type, Data = data, Info = info.ToList(), AdditionalData = new List<int>() };
        if (mark.Flushes == flushes && mark.Index <= pending.Count) pending.Insert(mark.Index, new PowerHistoryData { MetaData = meta });
        else pending.Add(new PowerHistoryData { MetaData = meta });
    }

    private int flushes;

    /// <summary>Queue a non-history packet (choices, options...) after everything reported so far.</summary>
    public void Send(IProtoBuf packet)
    {
        Flush();
        outbox.Add(packet);
    }

    public void Flush()
    {
        if (pending.Count == 0) return;
        outbox.Add(new PowerHistory { List = new List<PowerHistoryData>(pending) });
        pending.Clear();
        flushes++;
    }

    /// <summary>Everything queued since the last call, in order (history flushed first).</summary>
    public List<IProtoBuf> Drain()
    {
        Flush();
        var packets = new List<IProtoBuf>(outbox);
        outbox.Clear();
        return packets;
    }

    public static List<Tag> Tags(IEnumerable<KeyValuePair<int, int>> tags) =>
        tags.Select(t => new Tag { Name = t.Key, Value = t.Value }).ToList();
}
