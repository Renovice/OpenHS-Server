using System.Globalization;
using System.Text.Json;

namespace HsServer.Tools;

/// <summary>
/// `HsServer timeline &lt;session.jsonl&gt;...`: gameplay-feel metrics from HsRecorder 1.5+ sessions (packets + "fx"
/// timeline on one clock), one column per session, so a live recording can be compared with one against our server.
///
/// Per player action (ChooseOption), grouped by what it started (PLAY / ATTACK / POWER / END_TURN):
///   server    send → first PowerHistory from the server
///   anim      send → client starts animating the action's block
///   input     send → client back in main option mode (can act again)
///   splat     send → first damage number (actions that deal damage)
///   block     how long the action's own block animated
///   settle    send → last server block of the action finished animating
/// Opponent turns: end turn → first opponent action, gaps between opponent actions (start to start) and the idle time
/// between one action finishing and the next arriving (the AI "thinking" as the player sees it).
/// </summary>
internal static class TimelineReport
{
    private sealed record Ev(double Ms, string Kind, JsonElement Data)
    {
        public string Str(string name) => Data.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        public double? Num(string name) => Data.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
        public bool Flag(string name) => Data.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    }

    private static readonly string[] ActionBlocks = { "PLAY", "ATTACK", "POWER" };
    private static Engine.CardDb cards;

    // A PLAY block is a card or a hero power: name it by the source card's type (entity "<id>:<cardId>").
    private static string PlayKind(string src)
    {
        string cardId = src?.Split(':', 2).ElementAtOrDefault(1);
        if (string.IsNullOrEmpty(cardId) || !cards.TryGet(cardId, out var card)) return "PLAY";
        return card.Type switch
        {
            Engine.TAG_CARDTYPE.MINION => "PLAY minion",
            Engine.TAG_CARDTYPE.SPELL => "PLAY spell",
            Engine.TAG_CARDTYPE.HERO_POWER => "HERO POWER",
            Engine.TAG_CARDTYPE.WEAPON => "PLAY weapon",
            _ => "PLAY " + card.Type.ToString().ToLowerInvariant(),
        };
    }

    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: HsServer timeline <session.jsonl> [<session.jsonl>...]");
            return 2;
        }
        bool detail = args.Contains("--detail");
        string[] files = args.Where(a => a != "--detail").ToArray();
        cards = Engine.CardDb.Load(Engine.CardDb.FindDefaultDir(Program.ProjectRoot()));
        var reports = files.Select(Analyze).ToList();
        Print(files.Select(Path.GetFileNameWithoutExtension).ToList(), reports);
        if (detail)
            for (int k = 0; k < files.Length; k++)
            {
                Console.WriteLine();
                Console.WriteLine($"== actions in {Path.GetFileName(files[k])}  (ms after ChooseOption sent)");
                Console.WriteLine($"{"t(s)",7} {"kind",-12} {"card",-22} {"target",-18} {"server",7} {"anim",7} {"splat",7} {"settle",7}");
                foreach (string line in reports[k].Details) Console.WriteLine(line);
            }
        return 0;
    }

    // ---------- reading ----------

    private static List<Ev> Read(string file)
    {
        var events = new List<Ev>();
        using var reader = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        for (string line; (line = reader.ReadLine()) != null;)
        {
            if (!line.Contains("\"ms\":")) continue;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); } catch (JsonException) { continue; }
            JsonElement r = doc.RootElement.Clone();
            doc.Dispose();
            if (!r.TryGetProperty("ms", out var ms) || !r.TryGetProperty("ch", out var ch)) continue;
            string kind = ch.GetString() switch
            {
                "fx" => r.GetProperty("ev").GetString(),
                "game" => $"{r.GetProperty("dir").GetString()}{r.GetProperty("type").GetInt32()}", // e.g. in19, out2
                _ => null,
            };
            if (kind != null) events.Add(new Ev(ms.GetDouble(), kind, r));
        }
        events.Sort((a, b) => a.Ms.CompareTo(b.Ms));
        return events;
    }

    // ---------- analysis ----------

    private sealed class Report
    {
        public readonly Dictionary<string, Dictionary<string, List<double>>> Actions = new(); // kind → metric → values
        public readonly Dictionary<string, List<double>> Opponent = new();
        public readonly Dictionary<string, List<double>> Blocks = new(); // block type → animation time (all blocks)
        public readonly Dictionary<string, List<double>> Turn = new();
        public readonly Dictionary<string, List<double>> Frames = new();
        public int Games;
        public readonly List<string> Details = new();

        public void Add(Dictionary<string, List<double>> into, string metric, double? value)
        {
            if (value is not double v) return;
            if (!into.TryGetValue(metric, out var list)) into[metric] = list = new List<double>();
            list.Add(v);
        }

        public void AddAction(string kind, string metric, double? value)
        {
            if (!Actions.TryGetValue(kind, out var m)) Actions[kind] = m = new Dictionary<string, List<double>>();
            Add(m, metric, value);
        }
    }

    private static Report Analyze(string file)
    {
        List<Ev> ev = Read(file);
        var r = new Report();
        int First(int from, Func<Ev, bool> match, double untilMs = double.MaxValue)
        {
            for (int i = from; i < ev.Count && ev[i].Ms <= untilMs; i++) if (match(ev[i])) return i;
            return -1;
        }
        double? After(int from, double t0, Func<Ev, bool> match, double untilMs) =>
            First(from, match, untilMs) is int i and >= 0 ? ev[i].Ms - t0 : null;

        // All block animation times.
        foreach (Ev e in ev.Where(e => e.Kind == "tl_end" && e.Num("took") is not null && e.Str("block") is { } b && b != "INVALID"))
            r.Add(r.Blocks, e.Str("block") + (e.Str("parent") is null && e.Num("parent") is null ? "" : " (sub)"), e.Num("took"));

        // Player actions: each ChooseOption up to the next decision point.
        var decisions = Enumerable.Range(0, ev.Count).Where(i => ev[i].Kind is "send_option" or "send_choices" or "end_turn_click").ToList();
        foreach (int i in decisions.Where(i => ev[i].Kind == "send_option"))
        {
            double t0 = ev[i].Ms;
            int next = decisions.FirstOrDefault(j => j > i && ev[j].Ms > t0 + 1);
            double until = next > i ? ev[next].Ms : t0 + 15000;
            bool endTurn = decisions.Any(j => j < i && ev[j].Kind == "end_turn_click" && t0 - ev[j].Ms < 300);

            int main = First(i, e => e.Kind == "tl_start" && ActionBlocks.Contains(e.Str("block")) && e.Flag("blockStart"), until);
            // What the player acted with: the entity the client answered for (net_response, same frame as the send).
            int clicked = First(i, e => e.Kind == "net_response", t0 + 50);
            string actor = main >= 0 && ev[main].Str("src") is { } s && s.Contains(':') && !s.EndsWith(':') ? s : clicked >= 0 ? ev[clicked].Str("ent") : null;
            string kind = endTurn ? "END_TURN" : main < 0 ? "OTHER" : ev[main].Str("block") == "PLAY" ? PlayKind(actor) : ev[main].Str("block");
            r.AddAction(kind, "server", After(i, t0, e => e.Kind == "in19", until));
            r.AddAction(kind, "anim", main >= 0 ? ev[main].Ms - t0 : null);
            if (!endTurn) r.AddAction(kind, "input", After(i, t0, e => e.Kind == "mode" && e.Str("mode") == "main", until));
            r.AddAction(kind, "splat", After(i, t0, e => e.Kind == "splat_anim", until));
            if (main >= 0)
            {
                double? id = ev[main].Num("id");
                int end = First(main, e => e.Kind == "tl_end" && e.Num("id") == id, until);
                r.AddAction(kind, "block", end >= 0 ? ev[end].Num("took") : null);
            }
            if (!endTurn)
            {
                int last = -1;
                for (int j = i; j < ev.Count && ev[j].Ms < until; j++) if (ev[j].Kind == "tl_end") last = j;
                r.AddAction(kind, "settle", last >= 0 ? ev[last].Ms - t0 : null);
            }
            r.AddAction(kind, "count", 1);

            string Ms(double? v) => v is double d ? d.ToString("0", CultureInfo.InvariantCulture) : "-";
            int lastEnd = -1;
            for (int j = i; j < ev.Count && ev[j].Ms < until; j++) if (ev[j].Kind == "tl_end") lastEnd = j;
            double? target = ev[i].Num("target");
            r.Details.Add($"{(t0 / 1000).ToString("0.0", CultureInfo.InvariantCulture),7} {Trim(kind, 12),-12} {Trim(actor ?? "?", 22),-22} {Trim(target is > 0 ? EntityName(ev, (int)target) : "-", 18),-18} " +
                          $"{Ms(After(i, t0, e => e.Kind == "in19", until)),7} {Ms(main >= 0 ? ev[main].Ms - t0 : null),7} " +
                          $"{Ms(After(i, t0, e => e.Kind == "splat_anim", until)),7} {Ms(!endTurn && lastEnd >= 0 ? ev[lastEnd].Ms - t0 : null),7}");
        }

        // Server → animation: every batch of history, from arrival to its first block starting.
        foreach (int i in Enumerable.Range(0, ev.Count).Where(i => ev[i].Kind == "in19"))
        {
            int start = First(i, e => e.Kind == "tl_start", ev[i].Ms + 5000);
            r.Add(r.Frames, "history arrival → block start", start >= 0 ? ev[start].Ms - ev[i].Ms : null);
        }

        // Opponent turns: between our END_TURN and our next turn start.
        foreach (int i in decisions.Where(i => ev[i].Kind == "end_turn_click"))
        {
            double t0 = ev[i].Ms;
            int ourTurn = First(i + 1, e => e.Kind == "turn_events");
            double until = ourTurn >= 0 ? ev[ourTurn].Ms : t0 + 120000;
            var starts = new List<int>();
            for (int j = i; j < ev.Count && ev[j].Ms < until; j++)
                if (ev[j].Kind == "tl_start" && ActionBlocks.Contains(ev[j].Str("block")) && ev[j].Flag("blockStart") && ev[j].Num("parent") is null)
                    starts.Add(j);
            if (ourTurn < 0) continue;
            r.Add(r.Opponent, "turn length", until - t0);
            r.Add(r.Opponent, "actions per turn", starts.Count);
            if (starts.Count == 0) continue;
            r.Add(r.Opponent, "end turn → first action", ev[starts[0]].Ms - t0);
            for (int k = 1; k < starts.Count; k++)
            {
                r.Add(r.Opponent, "action start → next start", ev[starts[k]].Ms - ev[starts[k - 1]].Ms);
                // Idle: the previous action's last block finished → the next action's data arrived.
                int lastEnd = -1;
                for (int j = starts[k - 1]; j < starts[k]; j++) if (ev[j].Kind == "tl_end") lastEnd = j;
                int arrive = -1;
                for (int j = starts[k]; j > starts[k - 1]; j--) if (ev[j].Kind == "hist") { arrive = j; break; }
                if (lastEnd >= 0 && arrive >= 0) r.Add(r.Opponent, "next action data vs prev anim end", ev[arrive].Ms - ev[lastEnd].Ms);
            }
            r.Add(r.Opponent, "last action → our turn", until - ev[starts[^1]].Ms);
        }

        // Our turn start: turn events (draw etc.) until the end-turn button says it is our turn.
        foreach (int i in Enumerable.Range(0, ev.Count).Where(i => ev[i].Kind == "turn_events"))
        {
            int done = First(i, e => e.Kind == "turn_events_done", ev[i].Ms + 30000);
            r.Add(r.Turn, "turn start sequence", done >= 0 ? ev[done].Ms - ev[i].Ms : null);
            int ready = First(i, e => e.Kind == "mode" && e.Str("mode") == "main", ev[i].Ms + 30000);
            r.Add(r.Turn, "turn start → can act", ready >= 0 ? ev[ready].Ms - ev[i].Ms : null);
        }

        // Frames during the game (first to last server batch).
        double gameStart = ev.FirstOrDefault(e => e.Kind == "hist")?.Ms ?? 0, gameEnd = ev.LastOrDefault(e => e.Kind == "hist")?.Ms ?? 0;
        foreach (Ev e in ev.Where(e => e.Ms >= gameStart && e.Ms <= gameEnd))
        {
            if (e.Kind == "frames") { r.Add(r.Frames, "frame avg (5 s windows)", e.Num("avg")); r.Add(r.Frames, "frame max (5 s windows)", e.Num("max")); }
            if (e.Kind == "hitch") r.Add(r.Frames, "hitches > 50 ms", e.Num("dt"));
        }
        r.Games = ev.Count(e => e.Kind == "in16");
        return r;
    }

    // ---------- output ----------

    private static void Print(List<string> names, List<Report> reports)
    {
        const int label = 34, col = 24;
        string Header() => "".PadRight(label) + string.Concat(names.Select(n => Trim(n, col - 2).PadLeft(col)));
        void Section(string title) { Console.WriteLine(); Console.WriteLine($"== {title}"); Console.WriteLine(Header()); }
        void Row(string name, Func<Report, List<double>> values, bool countOnly = false)
        {
            var cells = reports.Select(r => values(r) is { Count: > 0 } v ? (countOnly ? v.Count.ToString() : Stat(v)) : "-");
            Console.WriteLine(Trim(name, label - 1).PadRight(label) + string.Concat(cells.Select(c => c.PadLeft(col))));
        }

        Console.WriteLine("values: median [min-max] ms, n = samples");
        Section("player actions (from ChooseOption sent)");
        var kinds = reports.SelectMany(r => r.Actions.Keys).Distinct().OrderBy(k => k.StartsWith("PLAY") ? 0 : k == "HERO POWER" ? 1 : k == "ATTACK" ? 2 : k == "END_TURN" ? 4 : 3).ThenBy(k => k).ToList();
        string[] metrics = { "server", "anim", "input", "splat", "block", "settle" };
        foreach (string kind in kinds)
        {
            Row($"{kind}: count", r => r.Actions.GetValueOrDefault(kind)?.GetValueOrDefault("count"), countOnly: true);
            foreach (string m in metrics)
                if (reports.Any(r => r.Actions.GetValueOrDefault(kind)?.ContainsKey(m) == true))
                    Row($"  {m}", r => r.Actions.GetValueOrDefault(kind)?.GetValueOrDefault(m));
        }
        Section("opponent turns");
        foreach (string m in reports.SelectMany(r => r.Opponent.Keys).Distinct()) Row(m, r => r.Opponent.GetValueOrDefault(m));
        Section("our turn start");
        foreach (string m in reports.SelectMany(r => r.Turn.Keys).Distinct()) Row(m, r => r.Turn.GetValueOrDefault(m));
        Section("block animation time (all blocks)");
        foreach (string m in reports.SelectMany(r => r.Blocks.Keys).Distinct().OrderBy(k => k)) Row(m, r => r.Blocks.GetValueOrDefault(m));
        Section("frames");
        foreach (string m in reports.SelectMany(r => r.Frames.Keys).Distinct()) Row(m, r => r.Frames.GetValueOrDefault(m));
    }

    // "<id>:<cardId>" for an entity id, from any timeline event that named it.
    private static string EntityName(List<Ev> ev, int id)
    {
        string prefix = id + ":";
        foreach (Ev e in ev)
            foreach (string field in new[] { "src", "ent", "card" })
                if (e.Str(field) is { } s && s.StartsWith(prefix, StringComparison.Ordinal) && s.Length > prefix.Length) return s;
        return id.ToString(CultureInfo.InvariantCulture);
    }

    private static string Stat(List<double> v)
    {
        var s = v.OrderBy(x => x).ToList();
        string F(double x) => x.ToString(x >= 100 ? "0" : "0.#", CultureInfo.InvariantCulture);
        return $"{F(s[s.Count / 2])} [{F(s[0])}-{F(s[^1])}] n{s.Count}";
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
