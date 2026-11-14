using System.Buffers.Binary;
using Blizzard.GameService.Protocol;
using Google.Protobuf;

namespace HsServer.Protocol;

/// <summary>
/// One Battle.net RPC message as carried in a single binary WebSocket message (subprotocol v1.rpc.battle.net):
/// [u16 big-endian header length][Header][body]. Header.Size may be absent; the WebSocket message bounds the body.
/// </summary>
public readonly record struct BgsFrame(Header Header, byte[] Body)
{
    public const string SubProtocol = "v1.rpc.battle.net";
    public const uint ResponseServiceId = 254;
    public const int MaxBodyBytes = 4 * 1024 * 1024;

    public static BgsFrame Parse(ReadOnlySpan<byte> message)
    {
        if (message.Length < 2) throw new InvalidDataException("BGS frame shorter than its length prefix");
        int headerLength = BinaryPrimitives.ReadUInt16BigEndian(message);
        if (headerLength == 0 || message.Length < 2 + headerLength) throw new InvalidDataException("Truncated BGS header");
        Header header = Bgs.Parse<Header>(message.Slice(2, headerLength).ToArray());
        byte[] body = message.Slice(2 + headerLength).ToArray();
        if (body.Length > MaxBodyBytes) throw new InvalidDataException("Oversized BGS body");
        if (header.HasSize && header.Size != body.Length) throw new InvalidDataException("BGS header size does not match body");
        return new BgsFrame(header, body);
    }

    public static byte[] Encode(Header header, IMessage body) =>
        EncodeRaw(header, body == null ? Array.Empty<byte>() : Bgs.Serialize(body));

    /// <summary>Frame an already-serialized body (used to replay recorded bytes unchanged).</summary>
    public static byte[] EncodeRaw(Header header, byte[] payload)
    {
        payload ??= Array.Empty<byte>();
        header.Size = (uint)payload.Length;
        byte[] raw = Bgs.Serialize(header);
        if (raw.Length > ushort.MaxValue) throw new InvalidDataException("BGS header too large");
        var frame = new byte[2 + raw.Length + payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(frame, (ushort)raw.Length);
        raw.CopyTo(frame, 2);
        payload.CopyTo(frame, 2 + raw.Length);
        return frame;
    }

    public T Decode<T>() where T : IMessage, new() => Bgs.Parse<T>(Body);
}

/// <summary>
/// Google.Protobuf helpers. The client's Google.Protobuf was compiled against Unity's mscorlib (which defines
/// Span types itself), so the Span/ReadOnlySequence overloads of MessageParser.ParseFrom cannot be resolved from
/// net9.0. Everything here goes through the single-overload IMessage.MergeFrom(CodedInputStream) instead.
/// </summary>
public static class Bgs
{
    public static T Parse<T>(byte[] data) where T : IMessage, new()
    {
        var message = new T();
        message.MergeFrom(new CodedInputStream(data ?? Array.Empty<byte>()));
        return message;
    }

    public static IMessage Parse(Type messageType, byte[] data)
    {
        var message = (IMessage)Activator.CreateInstance(messageType);
        message.MergeFrom(new CodedInputStream(data ?? Array.Empty<byte>()));
        return message;
    }

    public static byte[] Serialize(IMessage message)
    {
        using var stream = new MemoryStream();
        var output = new CodedOutputStream(stream);
        message.WriteTo(output);
        output.Flush();
        return stream.ToArray();
    }
}
