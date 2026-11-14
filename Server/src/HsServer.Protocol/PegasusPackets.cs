using System.Buffers.Binary;
using System.Reflection;

namespace HsServer.Protocol;

/// <summary>
/// Pegasus (util + game) packet catalogue built from the client's own classes: every message class carries a
/// nested <c>enum PacketID { ID = n }</c>. Serialization uses the client's generated IProtoBuf code, so bytes are
/// identical to what the client itself produces.
/// </summary>
public static class PegasusPackets
{
    private static readonly Lazy<Dictionary<int, Type[]>> byId = new(Index);
    private static readonly Lazy<Dictionary<Type, int>> idByType = new(() =>
        byId.Value.SelectMany(kv => kv.Value.Select(t => (t, kv.Key))).ToDictionary(x => x.t, x => x.Key));

    /// <summary>All classes declaring this packet id (some ids are reused across Pegasus namespaces).</summary>
    public static IReadOnlyList<Type> TypesFor(int packetId) => byId.Value.TryGetValue(packetId, out var t) ? t : Array.Empty<Type>();

    /// <summary>The class for a packet id; for ids shared by several namespaces, pass the expected namespace.</summary>
    public static Type TypeFor(int packetId, string preferredNamespace = null)
    {
        var types = TypesFor(packetId);
        if (types.Count == 0) return null;
        return types.FirstOrDefault(t => t.Namespace == preferredNamespace) ?? types[0];
    }

    public static int IdOf(Type messageType) =>
        idByType.Value.TryGetValue(messageType, out int id) ? id : throw new ArgumentException(messageType + " has no PacketID");

    public static int IdOf<T>() where T : IProtoBuf => IdOf(typeof(T));

    /// <summary>(2026-11-14, the no-recording start-up) <paramref name="message"/> with every unset required sub-message created, recursively —
    /// the client's serializer throws "Required by proto specification" for a required one left null (BattlegroundsRatingInfoResponse
    /// PlayerInfo, RewardProgress SeasonEnd). Lists stay empty, numbers 0, strings "".</summary>
    public static T Fill<T>(T message, int depth = 0) where T : class
    {
        if (message == null || depth > 6) return message;
        foreach (var p in message.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (!p.CanRead || !p.CanWrite || p.GetIndexParameters().Length > 0) continue;
            // an optional field has its Has… flag (ProfileProgress HasLastForge): left unset — only required ones are created
            if (message.GetType().GetProperty("Has" + p.Name) != null) continue;
            Type t = p.PropertyType;
            object value = p.GetValue(message);
            if (t == typeof(string)) { if (value == null) p.SetValue(message, ""); continue; }
            if (t == typeof(byte[])) { if (value == null) p.SetValue(message, Array.Empty<byte>()); continue; }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) { if (value == null) p.SetValue(message, Activator.CreateInstance(t)); continue; }
            if (!typeof(IProtoBuf).IsAssignableFrom(t) || t.IsAbstract || t.GetConstructor(Type.EmptyTypes) == null) continue;
            if (value == null)
            {
                value = Activator.CreateInstance(t);
                p.SetValue(message, value);
                // a required date: now (a year-0 date is rejected — RewardProgress.SeasonEnd left the menu waiting)
                if (t.FullName == "PegasusShared.Date") { SetNow(value); continue; }
            }
            Fill(value, depth + 1);
        }
        return message;
    }

    private static void SetNow(object date)
    {
        DateTime now = DateTime.UtcNow;
        foreach (var (name, v) in new[] { ("Year", now.Year), ("Month", now.Month), ("Day", now.Day), ("Hours", now.Hour), ("Min", now.Minute), ("Sec", now.Second) })
            if (date.GetType().GetProperty(name) is { CanWrite: true } prop) prop.SetValue(date, Convert.ChangeType(v, prop.PropertyType));
    }

    public static byte[] Serialize(IProtoBuf message)
    {
        using var stream = new MemoryStream();
        message.Serialize(stream);
        return stream.ToArray();
    }

    public static IProtoBuf Deserialize(Type messageType, byte[] body)
    {
        var message = (IProtoBuf)Activator.CreateInstance(messageType);
        message.Deserialize(new MemoryStream(body ?? Array.Empty<byte>()));
        return message;
    }

    public static T Deserialize<T>(byte[] body) where T : IProtoBuf, new()
    {
        var message = new T();
        message.Deserialize(new MemoryStream(body ?? Array.Empty<byte>()));
        return message;
    }

    /// <summary>Game-server TCP framing: [int32 LE packet type][int32 LE body length][body].</summary>
    public static byte[] FrameGame(int packetId, IProtoBuf message)
    {
        byte[] body = Serialize(message);
        var frame = new byte[8 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, packetId);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), body.Length);
        body.CopyTo(frame, 8);
        return frame;
    }

    private static Dictionary<int, Type[]> Index()
    {
        Assembly asm = typeof(PegasusUtil.RpcMessage).Assembly;
        Type[] types;
        try { types = asm.GetTypes(); }
        catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
        return types
            .Where(t => t.Namespace?.StartsWith("Pegasus", StringComparison.Ordinal) == true && typeof(IProtoBuf).IsAssignableFrom(t))
            .Select(t => (Type: t, Id: t.GetNestedType("PacketID")?.GetField("ID")?.GetRawConstantValue()))
            .Where(x => x.Id != null)
            .GroupBy(x => Convert.ToInt32(x.Id))
            .ToDictionary(g => g.Key, g => g.Select(x => x.Type).OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray());
    }
}
