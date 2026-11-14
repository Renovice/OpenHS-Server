using HsServer.Engine;
using HsServer.Protocol;
using HsServer.Sabber;
using PegasusGame;

namespace HsServer.Tools;

/// <summary>
/// `HsServer power &lt;session.jsonl&gt; [from=HH:mm:ss] [to=HH:mm:ss]`: a recorded session's game packets as Power.log-style
/// text — power history (with entity tags), the options the server offered (with play errors per target) and the options
/// the client sent. Times are local, like Power.log. The recorder has no size limit, so this covers what the client's own
/// Power.log loses (it stops writing at 10 MB and only 6 sessions are kept).
/// </summary>
internal static class PowerDump
{
    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: HsServer power <session.jsonl> [from=HH:mm:ss] [to=HH:mm:ss] [ui]");
            return 1;
        }
        TimeSpan? from = Arg(args, "from"), to = Arg(args, "to");
        int historyId = PegasusPackets.IdOf<PowerHistory>(), optionsId = PegasusPackets.IdOf<AllOptions>();
        int chooseOptionId = PegasusPackets.IdOf<ChooseOption>();
        int choicesId = PegasusPackets.IdOf<EntityChoices>(), chooseEntitiesId = PegasusPackets.IdOf<ChooseEntities>();
        int userUiId = PegasusPackets.IdOf<UserUI>();
        bool ui = args.Contains("ui"); // the cursor / held card / arrow / emote packets (UserUI), off by default (many)
        var cardIds = new Dictionary<int, string>(); // entity id → card id, learned from the history
        string Name(int id) => cardIds.TryGetValue(id, out string c) && c.Length > 0 ? $"[{c} id={id}]" : id.ToString();

        foreach (RecordedPacket p in Recording.Read(args[0]))
        {
            if (p.Channel != "game" || p.Body == null) continue;
            PowerHistory history = p.IsIn && p.Type == historyId ? PegasusPackets.Deserialize<PowerHistory>(p.Body) : null;
            if (history != null) // learn names from the whole session (also before the window) so every line shows them
                foreach (PowerHistoryData d in history.List)
                {
                    if (d.FullEntity is { } f) cardIds[f.Entity] = f.Name ?? "";
                    else if (d.ShowEntity is { } s) cardIds[s.Entity] = s.Name ?? "";
                    else if (d.ChangeEntity is { } c) cardIds[c.Entity] = c.Name ?? "";
                }
            TimeSpan time = p.Time.ToLocalTime().TimeOfDay;
            if (from != null && time < from || to != null && time > to) continue;
            string stamp = p.Time.ToLocalTime().ToString("HH:mm:ss.fff");

            if (history != null)
            {
                Console.WriteLine($"# {stamp} PowerHistory (seq {p.Seq})");
                Console.Write(HistoryText.Format(history.List, Name, withTags: true));
            }
            else if (p.IsIn && p.Type == optionsId)
            {
                var options = PegasusPackets.Deserialize<AllOptions>(p.Body);
                Console.WriteLine($"# {stamp} AllOptions id={options.Id}");
                for (int i = 0; i < options.Options.Count; i++)
                {
                    Option o = options.Options[i];
                    SubOption main = o.MainOption;
                    Console.WriteLine($"  option {i} type={o.Type_} mainEntity={(main == null ? "-" : Name(main.Id))} error={Error(main?.PlayError ?? 0)}{(main?.PowerKeyword > 0 ? $" keyword={main.PowerKeyword}" : "")}");
                    foreach (TargetOption t in main?.Targets ?? new List<TargetOption>())
                        Console.WriteLine($"    target entity={Name(t.Id)} error={Error(t.PlayError)}");
                    foreach (SubOption s in o.SubOptions ?? new List<SubOption>())
                        Console.WriteLine($"    subOption entity={Name(s.Id)} error={Error(s.PlayError)}");
                }
            }
            else if (!p.IsIn && p.Type == chooseOptionId)
            {
                var c = PegasusPackets.Deserialize<ChooseOption>(p.Body);
                Console.WriteLine($"# {stamp} SendOption id={c.Id} selectedOption={c.Index} selectedSubOption={c.SubOption} " +
                                  $"selectedTarget={(c.Target == 0 ? "0" : Name(c.Target))} selectedPosition={c.Position}");
            }
            else if (p.IsIn && p.Type == choicesId)
            {
                var c = PegasusPackets.Deserialize<EntityChoices>(p.Body);
                Console.WriteLine($"# {stamp} EntityChoices id={c.Id} type={c.ChoiceType} source={Name(c.Source)} " +
                                  $"entities=[{string.Join(", ", c.Entities.Select(Name))}]");
            }
            else if (!p.IsIn && p.Type == chooseEntitiesId)
            {
                var c = PegasusPackets.Deserialize<ChooseEntities>(p.Body);
                Console.WriteLine($"# {stamp} ChooseEntities id={c.Id} entities=[{string.Join(", ", c.Entities.Select(Name))}]");
            }
            else if (ui && p.Type == userUiId)
            {
                var u = PegasusPackets.Deserialize<UserUI>(p.Body);
                var parts = new List<string> { p.IsIn ? "in" : "out" };
                if (u.HasPlayerId) parts.Add($"player={u.PlayerId}");
                if (u.HasMouseInfo && u.MouseInfo is { } m)
                    parts.Add($"over={(m.OverCard == 0 ? "-" : Name(m.OverCard))} held={(m.HeldCard == 0 ? "-" : Name(m.HeldCard))} " +
                              $"arrow={(m.ArrowOrigin == 0 ? "-" : Name(m.ArrowOrigin))} x={m.X} y={m.Y}");
                if (u.HasEmote) parts.Add($"emote={u.Emote}");
                if (u.HasSelectedEntityId) parts.Add($"selected={Name(u.SelectedEntityId)}");
                Console.WriteLine($"# {stamp} UserUI {string.Join(" ", parts)}");
            }
        }
        return 0;
    }

    private static string Error(int error) =>
        Enum.IsDefined(typeof(PlayErrorType), error) ? ((PlayErrorType)error).ToString() : error.ToString();

    private static TimeSpan? Arg(string[] args, string key)
    {
        string value = args.FirstOrDefault(a => a.StartsWith(key + "=", StringComparison.Ordinal))?.Substring(key.Length + 1);
        return value == null ? null : TimeSpan.Parse(value);
    }
}
