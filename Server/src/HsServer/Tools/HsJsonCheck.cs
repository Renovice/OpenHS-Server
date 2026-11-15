using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using HsServer.Engine;

namespace HsServer.Tools;

/// <summary>
/// `HsServer hsjson [--build N] [--dbf DIR] [--xml FILE] [--offline]` (user policy 2026-11-01): cross-checks our own card database
/// (the export snapshot Server/data/dbf-*, the server's card data — it stays authoritative) against HearthSim's independent
/// extraction of the same client build (HearthstoneJSON CardDefs.base.xml, the data HearthDb ships). Run after every `update`
/// (UpdateClient runs it): a tag our export dropped or read wrongly shows up as a difference. Writes RESEARCH/hsjson/check-&lt;build&gt;.md.
/// Also refreshes Server/data/hearthsim-tag-names.tsv: HearthSim's names for tags the client's enum doesn't name (the decoder
/// prints them as a note, never in the tag= field — a number there means the client doesn't read the tag).
/// The build comes from the private client's .product.db unless given; the XML is cached in Server/data/hsjson (not in git).
/// </summary>
internal static class HsJsonCheck
{
    private const string XmlUrl = "https://api.hearthstonejson.com/v1/{0}/CardDefs.base.xml";
    private const string EnumsUrl = "https://raw.githubusercontent.com/HearthSim/HearthDb/master/HearthDb/Enums/Enums.cs";

    public static int Run(string[] args)
    {
        string root = Program.ProjectRoot();
        string dbf = Option(args, "--dbf") ?? CardDb.FindDefaultDir(root);
        int build = int.TryParse(Option(args, "--build"), out int b) ? b : ClientBuild(HsServer.Protocol.GameFolder.GameDir(root), dbf);
        if (build == 0)
        {
            Console.Error.WriteLine("No build number: the private client's version doesn't match the card database. Pass --build N.");
            return 1;
        }
        return Check(root, dbf, build, Option(args, "--xml"), args.Contains("--offline"));
    }

    /// <summary>The cross-check of <paramref name="dbf"/> against HearthstoneJSON's data for <paramref name="build"/>; 0 = done.</summary>
    internal static int Check(string root, string dbf, int build, string xmlFile = null, bool offline = false)
    {
        string cache = Path.Combine(root, "Server", "data", "hsjson");
        Directory.CreateDirectory(cache);
        xmlFile ??= Path.Combine(cache, $"CardDefs-{build}.xml");
        if (!File.Exists(xmlFile))
        {
            if (offline || !Download(string.Format(XmlUrl, build), xmlFile))
            {
                Console.Error.WriteLine($"HearthstoneJSON data for build {build} not available ({xmlFile}); cross-check skipped.");
                return 1;
            }
        }

        var theirs = ReadXml(xmlFile, out var xmlNames, out int xmlBuild);
        var ours = ReadDbf(dbf);
        string report = Report(dbf, build, xmlBuild, ours, theirs);
        string outDir = Path.Combine(root, "RESEARCH", "hsjson");
        Directory.CreateDirectory(outDir);
        string outFile = Path.Combine(outDir, $"check-{build}.md");
        File.WriteAllText(outFile, report);

        string enums = Path.Combine(cache, "HearthDb-Enums.cs");
        if (!File.Exists(enums) && !offline) Download(EnumsUrl, enums);
        int named = RefreshTagNames(root, xmlNames, File.Exists(enums) ? File.ReadAllText(enums) : null);
        Console.WriteLine(report.Split('\n').Skip(2).TakeWhile(l => !l.StartsWith("## ")).Aggregate("", (a, l) => a + l + "\n").TrimEnd());
        Console.WriteLine($"-> {outFile}; HearthSim tag names for {named} tags the client doesn't name (Server\\data\\hearthsim-tag-names.tsv)");
        return 0;
    }

    /// <summary>The private client's build (.product.db holds "major.minor.patch.build") when its version matches the database's.</summary>
    internal static int ClientBuild(string game, string dbf)
    {
        string db = Path.Combine(game, ".product.db");
        if (!File.Exists(db)) return 0;
        Match m = Regex.Match(Encoding.ASCII.GetString(File.ReadAllBytes(db)), @"(\d+)\.(\d+)\.(\d+)\.(\d{5,7})");
        if (!m.Success) return 0;
        string version = Path.GetFileName(dbf)?["dbf-".Length..] ?? "";
        return version.StartsWith($"{m.Groups[1].Value}.{m.Groups[2].Value}.") ? int.Parse(m.Groups[4].Value) : 0;
    }

    /// <summary>Tags HearthstoneJSON lists that the client's CardTag table doesn't hold (the engine derives them elsewhere): not
    /// compared. CARD_SET: from CardSetTiming (Loader/ReprintCards.SetOf).</summary>
    private static readonly HashSet<int> NotInTagTable = new() { (int)GAME_TAG.CARD_SET };

    private sealed record CardData(string Id, Dictionary<int, int> Tags, HashSet<int> Referenced);

    /// <summary>Our export: Card.json (dbf id → card id) and CardTag.json (a missing value is 0; reference tags apart).</summary>
    private static Dictionary<int, CardData> ReadDbf(string dbf)
    {
        var cards = new Dictionary<int, CardData>();
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dbf, "Card.json"))))
            foreach (JsonElement r in doc.RootElement.GetProperty("records").EnumerateArray())
                if (r.TryGetProperty("ID", out var id) && r.TryGetProperty("NoteMiniGuid", out var cid))
                    cards[id.GetInt32()] = new CardData(cid.GetString(), new Dictionary<int, int>(), new HashSet<int>());
        using (var doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dbf, "CardTag.json"))))
            foreach (JsonElement r in doc.RootElement.GetProperty("records").EnumerateArray())
            {
                if (!r.TryGetProperty("CardId", out var c) || !cards.TryGetValue(c.GetInt32(), out CardData card)) continue;
                int tag = r.GetProperty("TagId").GetInt32();
                int value = r.TryGetProperty("TagValue", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
                // the engine's rule (ReprintCards): a reference alone is a mention; with IsPowerKeywordTag it is also the card's own keyword
                bool reference = r.TryGetProperty("IsReferenceTag", out var rt) && rt.ValueKind == JsonValueKind.True;
                bool power = r.TryGetProperty("IsPowerKeywordTag", out var pk) && pk.ValueKind == JsonValueKind.True;
                if (reference) card.Referenced.Add(tag);
                if ((!reference || power) && value != 0 && !NotInTagTable.Contains(tag)) card.Tags[tag] = value;
            }
        return cards;
    }

    /// <summary>HearthstoneJSON's CardDefs.base.xml: numeric tags (Int / Bool / Card; text tags aren't compared), referenced tags,
    /// and the tag names it uses.</summary>
    private static Dictionary<int, CardData> ReadXml(string file, out Dictionary<int, string> names, out int build)
    {
        var cards = new Dictionary<int, CardData>();
        names = new Dictionary<int, string>();
        build = 0;
        CardData current = null;
        using var reader = XmlReader.Create(file, new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true });
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element) continue;
            switch (reader.Name)
            {
                case "CardDefs":
                    int.TryParse(reader.GetAttribute("build"), out build);
                    break;
                case "Entity":
                    current = new CardData(reader.GetAttribute("CardID"), new Dictionary<int, int>(), new HashSet<int>());
                    if (int.TryParse(reader.GetAttribute("ID"), out int dbfId)) cards[dbfId] = current;
                    break;
                case "Tag" or "ReferencedTag" when current != null:
                    if (!int.TryParse(reader.GetAttribute("enumID"), out int tag)) break;
                    if (reader.GetAttribute("name") is { Length: > 0 } name) names.TryAdd(tag, name);
                    string type = reader.GetAttribute("type");
                    int.TryParse(reader.GetAttribute("value"), out int value);
                    if (reader.Name == "ReferencedTag") { if (value != 0) current.Referenced.Add(tag); }
                    else if (type is "Int" or "Bool" or "Card" && value != 0 && !NotInTagTable.Contains(tag)) current.Tags[tag] = value;
                    break;
            }
        }
        return cards;
    }

    private static string Report(string dbf, int build, int xmlBuild, Dictionary<int, CardData> ours, Dictionary<int, CardData> theirs)
    {
        var sb = new StringBuilder();
        string Tag(int t) => Enum.IsDefined(typeof(GAME_TAG), t) ? $"{(GAME_TAG)t} ({t})" : TagNames.Note(t) is { } n ? $"{t} (hs:{n})" : t.ToString();
        var onlyOurs = ours.Keys.Except(theirs.Keys).OrderBy(k => k).ToList();
        var onlyTheirs = theirs.Keys.Except(ours.Keys).OrderBy(k => k).ToList();
        var both = ours.Keys.Intersect(theirs.Keys).ToList();
        var idDiff = both.Where(k => ours[k].Id != theirs[k].Id).ToList();
        // per tag: (we lack it, they lack it, values differ) with examples
        var stats = new Dictionary<int, (int Missing, int Extra, int Value, List<string> Examples)>();
        var refStats = new Dictionary<int, (int Missing, int Extra, List<string> Examples)>();
        int cardsDiffering = 0;
        foreach (int k in both)
        {
            CardData o = ours[k], t = theirs[k];
            bool differs = false;
            foreach (int tag in o.Tags.Keys.Union(t.Tags.Keys))
            {
                o.Tags.TryGetValue(tag, out int ov);
                t.Tags.TryGetValue(tag, out int tv);
                if (ov == tv) continue;
                differs = true;
                if (!stats.TryGetValue(tag, out var s)) s = (0, 0, 0, new List<string>());
                if (ov == 0) s.Missing++; else if (tv == 0) s.Extra++; else s.Value++;
                if (s.Examples.Count < 4) s.Examples.Add($"{o.Id} ours {ov} / theirs {tv}");
                stats[tag] = s;
            }
            foreach (int tag in o.Referenced.Union(t.Referenced))
            {
                bool inO = o.Referenced.Contains(tag), inT = t.Referenced.Contains(tag);
                if (inO == inT) continue;
                if (!refStats.TryGetValue(tag, out var s)) s = (0, 0, new List<string>());
                if (!inO) s.Missing++; else s.Extra++;
                if (s.Examples.Count < 3) s.Examples.Add(o.Id);
                refStats[tag] = s;
            }
            if (differs) cardsDiffering++;
        }

        sb.AppendLine($"# Card database cross-check — build {build} (generated by `HsServer hsjson`; do not edit)");
        sb.AppendLine();
        sb.AppendLine($"Ours: {Path.GetFileName(dbf)} ({ours.Count} cards) · HearthstoneJSON CardDefs.base.xml build {xmlBuild} ({theirs.Count} cards)");
        sb.AppendLine($"Cards in both: {both.Count} · only ours: {onlyOurs.Count} · only theirs: {onlyTheirs.Count} · card id differs: {idDiff.Count}");
        sb.AppendLine($"Cards with a numeric tag difference: {cardsDiffering} · tags involved: {stats.Count} · referenced-tag differences: {refStats.Values.Sum(s => s.Missing + s.Extra)}");
        if (xmlBuild != build) sb.AppendLine($"WARNING: the XML says build {xmlBuild}, asked for {build}.");
        sb.AppendLine();
        sb.AppendLine("Our export is the server's card database (user rule); a difference here is either a tag our export lost or read");
        sb.AppendLine("wrongly (fix the export) or HearthSim's extraction differing (note it). Text tags are not compared, nor CARD_SET");
        sb.AppendLine("(not a CardTag row in the client's tables: the engine reads sets from CardSetTiming).");
        sb.AppendLine();
        sb.AppendLine("## Numeric tags that differ (most cards first)");
        sb.AppendLine("| tag | we lack | they lack | other value | examples |");
        sb.AppendLine("|---|---:|---:|---:|---|");
        foreach (var (tag, s) in stats.OrderByDescending(kv => kv.Value.Missing + kv.Value.Extra + kv.Value.Value))
            sb.AppendLine($"| {Tag(tag)} | {s.Missing} | {s.Extra} | {s.Value} | {string.Join("; ", s.Examples)} |");
        sb.AppendLine();
        sb.AppendLine("## Referenced tags (keywords a card's text mentions) that differ");
        sb.AppendLine("| tag | we lack | they lack | examples |");
        sb.AppendLine("|---|---:|---:|---|");
        foreach (var (tag, s) in refStats.OrderByDescending(kv => kv.Value.Missing + kv.Value.Extra))
            sb.AppendLine($"| {Tag(tag)} | {s.Missing} | {s.Extra} | {string.Join(", ", s.Examples)} |");
        void List(string title, IEnumerable<string> items)
        {
            var all = items.ToList();
            sb.AppendLine();
            sb.AppendLine($"## {title} ({all.Count})");
            sb.AppendLine(all.Count == 0 ? "(none)" : string.Join(", ", all.Take(200)) + (all.Count > 200 ? $", … {all.Count - 200} more" : ""));
        }
        List("Cards only in our export", onlyOurs.Select(k => $"{ours[k].Id} ({k})"));
        List("Cards only in HearthstoneJSON", onlyTheirs.Select(k => $"{theirs[k].Id} ({k})"));
        List("Same dbf id, other card id", idDiff.Select(k => $"{k}: ours {ours[k].Id} / theirs {theirs[k].Id}"));
        return sb.ToString();
    }

    /// <summary>Server/data/hearthsim-tag-names.tsv: names for tags the client's enum doesn't name, from HearthstoneJSON's tag
    /// names and HearthDb's GameTag enum (MIT, HearthSim). Existing rows are kept; returns the number of names.</summary>
    private static int RefreshTagNames(string root, Dictionary<int, string> xmlNames, string hearthDbEnums)
    {
        string file = Path.Combine(root, "Server", "data", "hearthsim-tag-names.tsv");
        var names = new SortedDictionary<int, (string Name, string Source)>();
        if (File.Exists(file))
            foreach (string line in File.ReadLines(file).Where(l => !l.StartsWith('#') && l.Contains('\t')))
            {
                string[] p = line.Split('\t');
                if (p.Length >= 2 && int.TryParse(p[0], out int id)) names[id] = (p[1], p.Length > 2 ? p[2] : "");
            }
        if (hearthDbEnums != null && Regex.Match(hearthDbEnums, @"enum GameTag\s*\{(.*?)\n\s*\}", RegexOptions.Singleline) is { Success: true } m)
            foreach (Match e in Regex.Matches(m.Groups[1].Value, @"(\w+)\s*=\s*(\d+)"))
                names.TryAdd(int.Parse(e.Groups[2].Value), (e.Groups[1].Value, "HearthDb"));
        foreach (var (id, name) in xmlNames) names.TryAdd(id, (name, "HearthstoneJSON"));
        foreach (int id in names.Keys.Where(id => Enum.IsDefined(typeof(GAME_TAG), id)).ToList()) names.Remove(id); // the client's own names win
        // (HearthstoneJSON writes a number as the "name" of a tag nobody named: not a name)
        foreach (int id in names.Where(kv => !TagNames.IsName(kv.Value.Name)).Select(kv => kv.Key).ToList()) names.Remove(id);
        var sb = new StringBuilder();
        sb.AppendLine("# HearthSim's names for game tags the client's GAME_TAG enum doesn't name (generated by `HsServer hsjson`).");
        sb.AppendLine("# Sources: HearthDb Enums.cs (MIT License, Copyright (c) 2016 HearthSim, https://github.com/HearthSim/HearthDb) and");
        sb.AppendLine("# HearthstoneJSON CardDefs.base.xml. Used only as a note in decoded text; a number in tag= still means the client doesn't read it.");
        sb.AppendLine("# id\tname\tsource");
        foreach (var (id, (name, source)) in names) sb.Append(id).Append('\t').Append(name).Append('\t').AppendLine(source);
        File.WriteAllText(file, sb.ToString());
        TagNames.Reset();
        return names.Count;
    }

    private static bool Download(string url, string file)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("HsServer-crosscheck/1.0");
            byte[] data = http.GetByteArrayAsync(url).GetAwaiter().GetResult();
            File.WriteAllBytes(file, data);
            Console.WriteLine($"Downloaded {url} ({data.Length / 1024} KB)");
            return true;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Download failed: {url}: {e.Message}");
            return false;
        }
    }

    private static string Option(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}

/// <summary>HearthSim's names for tags the client doesn't name (Server/data/hearthsim-tag-names.tsv, `HsServer hsjson`).</summary>
internal static class TagNames
{
    private static Dictionary<int, string> _names;

    /// <summary>The HearthSim name of a tag the client's enum doesn't name, or null.</summary>
    public static string Note(int tag)
    {
        _names ??= Load();
        return _names.TryGetValue(tag, out string n) ? n : null;
    }

    internal static void Reset() => _names = null;

    /// <summary>An identifier-like name (HearthstoneJSON fills unnamed tags with their number or another one).</summary>
    internal static bool IsName(string name) => name is { Length: > 0 } && (char.IsLetter(name[0]) || name[0] == '_') && name.All(c => char.IsLetterOrDigit(c) || c == '_');

    private static Dictionary<int, string> Load()
    {
        var map = new Dictionary<int, string>();
        try
        {
            string file = Path.Combine(Program.ProjectRoot(), "Server", "data", "hearthsim-tag-names.tsv");
            if (File.Exists(file))
                foreach (string line in File.ReadLines(file).Where(l => !l.StartsWith('#')))
                {
                    string[] p = line.Split('\t');
                    if (p.Length >= 2 && int.TryParse(p[0], out int id) && !Enum.IsDefined(typeof(GAME_TAG), id) && IsName(p[1])) map[id] = p[1];
                }
        }
        catch (IOException) { }
        return map;
    }
}
