using Blizzard.GameService.Protocol;
using Blizzard.GameService.Protocol.GameUtilities.V2.Client;
using Blizzard.GameService.Protocol.Notification.V2.Client;
using HsServer.Protocol;

namespace HsServer.Tools;

/// <summary>`HsServer analyze &lt;session.jsonl&gt; [maxLines]`: prints the BGS RPC flow of a recorded session.</summary>
internal static class Analyze
{
    /// <summary>`HsServer dump &lt;session.jsonl&gt; &lt;seq&gt;...`: prints recorded BGS messages / util payloads as JSON-ish text.</summary>
    public static int Dump(string[] args)
    {
        var wanted = args.Skip(1).Select(long.Parse).ToHashSet();
        var bgsAssembly = typeof(Header).Assembly;
        foreach (RecordedPacket p in Recording.Read(args[0]))
        {
            if (!wanted.Contains(p.Seq)) continue;
            Console.WriteLine($"===== #{p.Seq} {p.Channel}-{p.Direction} {p.Message ?? p.Type.ToString()}");
            if (p.Channel == "bgs")
            {
                if (p.Header != null) Console.WriteLine("header: " + Bgs.Parse<Header>(p.Header));
                if (!p.Redacted && p.Body != null && bgsAssembly.GetType(p.Message) is { } t)
                    Console.WriteLine(Bgs.Parse(t, p.Body).ToString());
            }
            // (2026-11-14) a util (Pegasus) message: its fields by name (the defaults of the no-recording start-up come from these)
            else if (p.Channel == "util" && p.Body != null && PegasusPackets.TypeFor(p.Type, "PegasusUtil") is { } u)
                PrintFields(PegasusPackets.Deserialize(u, p.Body), "  ", 0);
        }
        return 0;
    }

    private static void PrintFields(object o, string indent, int depth)
    {
        if (o == null || depth > 5) return;
        foreach (var prop in o.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length > 0 || !prop.CanWrite) continue;
            object v = prop.GetValue(o);
            var has = o.GetType().GetProperty("Has" + prop.Name);
            if (has != null && has.GetValue(o) is false) continue;
            if (v is System.Collections.IList list && v is not byte[])
            {
                Console.WriteLine($"{indent}{prop.Name}: [{list.Count}]");
                foreach (object item in list.Cast<object>().Take(5))
                    if (item is IProtoBuf) { Console.WriteLine($"{indent}  -"); PrintFields(item, indent + "    ", depth + 1); }
                    else Console.WriteLine($"{indent}  - {item}");
            }
            else if (v is IProtoBuf) { Console.WriteLine($"{indent}{prop.Name}:"); PrintFields(v, indent + "  ", depth + 1); }
            else if (v != null) Console.WriteLine($"{indent}{prop.Name} = {v}");
        }
    }

    public static int Run(string[] args)
    {
        string session = args[0];
        int max = args.Length > 1 ? int.Parse(args[1]) : 400;
        int shown = 0;
        foreach (RecordedPacket p in Recording.Read(session))
        {
            if (p.Channel != "bgs" || p.Header == null) continue;
            Header h = Bgs.Parse<Header>(p.Header);
            string detail = "";
            if (!p.Redacted && p.Body != null)
            {
                if (p.Message.EndsWith(".ProcessTaskRequest"))
                {
                    var req = Bgs.Parse<ProcessTaskRequest>(p.Body);
                    var blob = req.Attribute.FirstOrDefault(a => a.Name == "p")?.Value?.BlobValue;
                    string system = req.Attribute.FirstOrDefault(a => a.Name == "system")?.Value?.StringValue;
                    if (blob != null)
                    {
                        var rpc = PegasusPackets.Deserialize<PegasusUtil.RpcMessage>(blob.ToByteArray());
                        detail = $"util-req {rpc.RpcHeader.Type} sys={system}";
                    }
                }
                else if (p.Message.EndsWith(".ProcessTaskResponse"))
                {
                    var resp = Bgs.Parse<ProcessTaskResponse>(p.Body);
                    long id = resp.Result.FirstOrDefault(a => a.Name == "id")?.Value?.IntValue ?? -1;
                    detail = $"util-resp {id}";
                }
                else if (p.Message.EndsWith(".NotificationReceivedNotification"))
                {
                    var n = Bgs.Parse<NotificationReceivedNotification>(p.Body);
                    detail = string.Join(" ", n.Notifications.Select(x =>
                        x.Type + (x.Attribute.FirstOrDefault(a => a.Name == "message_type") is { } mt ? ":" + mt.Value.IntValue : "")));
                }
            }
            string msg = p.Message?.Replace("Blizzard.GameService.Protocol.", "") ?? "";
            if (p.Kind == "response" && string.IsNullOrEmpty(msg)) msg = "(empty) for " + p.RequestMessage?.Replace("Blizzard.GameService.Protocol.", "");
            Console.WriteLine($"{p.Seq,6} {p.Direction,-3} {p.Kind,-8} svc={h.ServiceId,3} hash={h.ServiceHash,10} m={h.MethodId,3} tok={h.Token,5} {msg} {detail}{(p.Redacted ? " [redacted]" : "")}");
            if (++shown >= max) break;
        }
        return 0;
    }
}
