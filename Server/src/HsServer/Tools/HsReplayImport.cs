using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using HsServer.Engine;
using HsServer.Protocol;
using PegasusGame;
using PegasusShared;

namespace HsServer.Tools;

/// <summary>
/// `HsServer hsreplay &lt;replay.xml | .xml.gz | folder&gt; [captures folder]`: HSReplay replays (the XML hsreplay.net keeps
/// for uploaded games, made from the uploader's Power.log by HearthSim's python-hslog) back into the server messages they
/// were: one recorder session file per game (RESEARCH/captures/hsreplay-&lt;name&gt;/session-hsreplay-&lt;name&gt;.jsonl), so every
/// tool that reads live recordings — `power`, the effect extractors (RESEARCH/vfx/tools), the audit, `wirecheck` — reads
/// them too. Kept: game setup, entities and reveals, tag changes, blocks, metadata (TARGET, DAMAGE, EFFECT_TIMING...),
/// SUB_SPELLs (the server-picked effects), deck shuffles. Not in a replay: lobby packets; the opponent's hidden cards; the
/// client's own animation. Player names are not copied (privacy).
/// </summary>
internal static class HsReplayImport
{
    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: HsServer hsreplay <replay.xml | replay.xml.gz | folder> [captures folder]");
            return 1;
        }
        string root = Program.ProjectRoot();
        string captures = args.Length > 1 ? args[1] : Path.Combine(root, "RESEARCH", "captures");
        var files = Directory.Exists(args[0])
            ? Directory.EnumerateFiles(args[0]).Where(f => f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                                                            f.EndsWith(".xml.gz", StringComparison.OrdinalIgnoreCase) ||
                                                            f.EndsWith(".hsreplay", StringComparison.OrdinalIgnoreCase)).ToList()
            : new List<string> { args[0] };
        int games = 0, failed = 0;
        foreach (string file in files)
        {
            try
            {
                XDocument doc = Load(file);
                string name = Path.GetFileName(file).Split('.')[0];
                int index = 0;
                foreach (XElement game in doc.Descendants("Game"))
                {
                    string id = $"hsreplay-{Safe(name)}{(index++ > 0 ? "-" + index : "")}";
                    string folder = Path.Combine(captures, id);
                    Directory.CreateDirectory(folder);
                    var stats = new Stats();
                    string session = Path.Combine(folder, $"session-{id}.jsonl");
                    File.WriteAllText(session, Convert(game, stats), new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(folder, "replay-info.txt"), Info(file, doc, game, stats));
                    Console.WriteLine($"{Path.GetFileName(file)} -> {session}: {stats}");
                    games++;
                }
            }
            catch (Exception e)
            {
                failed++;
                Console.Error.WriteLine($"{Path.GetFileName(file)}: {e.Message}");
            }
        }
        Console.WriteLine($"{games} game(s) imported, {failed} file(s) failed");
        return failed > 0 && games == 0 ? 1 : 0;
    }

    private sealed class Stats
    {
        public int Packets, Blocks, SubSpells, Meta, Skipped;
        public override string ToString() => $"{Packets} power packets, {Blocks} blocks, {SubSpells} sub-spells, {Meta} metadata, {Skipped} other nodes skipped";
    }

    private static XDocument Load(string file)
    {
        using Stream raw = File.OpenRead(file);
        using Stream input = file.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? new GZipStream(raw, CompressionMode.Decompress) : raw;
        return XDocument.Load(input);
    }

    private static string Safe(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());

    /// <summary>One power-history packet per top-level node of the game (a block with everything inside it, a tag change),
    /// as recorder lines (channel game, in, type PowerHistory), time from the node's ts.</summary>
    private static string Convert(XElement game, Stats stats)
    {
        var sb = new StringBuilder();
        int seq = 1;
        DateTime start = Time(game) ?? DateTime.UtcNow;
        sb.AppendLine($"{{\"seq\":{seq++},\"t\":\"{start:O}\",\"ms\":0,\"f\":0,\"meta\":\"session_start\",\"game_version\":\"hsreplay\",\"recorder\":\"hsreplay-import\"}}");
        int historyId = PegasusPackets.IdOf<PowerHistory>();
        DateTime last = start;
        PowerHistoryCreateGame create = null;

        void Emit(List<PowerHistoryData> list, DateTime t)
        {
            if (list.Count == 0) return;
            byte[] body = PegasusPackets.Serialize(new PowerHistory { List = list });
            sb.AppendLine($"{{\"seq\":{seq++},\"t\":\"{t:O}\",\"ms\":{(t - start).TotalMilliseconds:0.##},\"f\":0,\"ch\":\"game\",\"dir\":\"in\",\"type\":{historyId},\"len\":{body.Length},\"b64\":\"{System.Convert.ToBase64String(body)}\"}}");
            stats.Packets++;
        }

        foreach (XElement node in game.Elements())
        {
            DateTime t = Time(node) ?? last;
            last = t;
            switch (node.Name.LocalName)
            {
                case "GameEntity":
                    create = new PowerHistoryCreateGame
                    {
                        GameEntity = new PegasusGame.Entity { Id = Int(node, "id"), Tags = Tags(node), TagLists = new List<TagList>() },
                        Players = new List<PegasusGame.Player>(), PlayerInfos = new List<SharedPlayerInfo>(), ActionInfos = new List<ActionInfo>(),
                    };
                    continue;
                case "Player" when create != null:
                    create.Players.Add(new PegasusGame.Player
                    {
                        Id = Int(node, "playerID"),
                        GameAccountId = new BnetId { Hi = Long(node, "accountHi"), Lo = Long(node, "accountLo") },
                        CardBack = Int(node, "cardback"),
                        Entity = new PegasusGame.Entity { Id = Int(node, "id"), Tags = Tags(node), TagLists = new List<TagList>() },
                    });
                    continue;
            }
            if (create != null)
            {
                Emit(new List<PowerHistoryData> { new PowerHistoryData { CreateGame = create } }, t);
                create = null;
            }
            var list = new List<PowerHistoryData>();
            Walk(node, list, stats);
            Emit(list, t);
        }
        if (create != null) Emit(new List<PowerHistoryData> { new PowerHistoryData { CreateGame = create } }, last);
        return sb.ToString();
    }

    private static void Walk(XElement e, List<PowerHistoryData> list, Stats stats)
    {
        switch (e.Name.LocalName)
        {
            case "FullEntity":
                list.Add(new PowerHistoryData { FullEntity = Entity(Int(e, "id"), e) });
                break;
            case "ShowEntity":
                list.Add(new PowerHistoryData { ShowEntity = Entity(Int(e, "entity"), e) });
                break;
            case "ChangeEntity":
                list.Add(new PowerHistoryData { ChangeEntity = Entity(Int(e, "entity"), e) });
                break;
            case "HideEntity":
                list.Add(new PowerHistoryData { HideEntity = new PowerHistoryHide { Entity = Int(e, "entity"), Zone = Enum(e, "zone", typeof(TAG_ZONE)) } });
                break;
            case "TagChange":
                list.Add(new PowerHistoryData
                {
                    TagChange = new PowerHistoryTagChange { Entity = Int(e, "entity"), Tag = Enum(e, "tag", typeof(GAME_TAG)), Value = Value(e) },
                });
                break;
            case "Block":
                stats.Blocks++;
                list.Add(new PowerHistoryData
                {
                    PowerStart = new PowerHistoryStart
                    {
                        Type = (HistoryBlock.Type)Enum(e, "type", typeof(HistoryBlock.Type)), Source = Int(e, "entity"), Target = Int(e, "target"),
                        EffectIndex = Int(e, "effectIndex", Int(e, "index")), EffectCardId = (string)e.Attribute("effectCardId") ?? "",
                        SubOption = Int(e, "subOption", -1), TriggerKeyword = Enum(e, "triggerKeyword", typeof(GAME_TAG)),
                    },
                });
                foreach (XElement child in e.Elements()) Walk(child, list, stats);
                list.Add(new PowerHistoryData { PowerEnd = new PowerHistoryEnd() });
                break;
            case "MetaData":
                stats.Meta++;
                list.Add(new PowerHistoryData
                {
                    MetaData = new PowerHistoryMetaData
                    {
                        Type = (HistoryMeta.Type)Enum(e, "meta", typeof(HistoryMeta.Type)), Data = Int(e, "data"),
                        Info = e.Elements("Info").Select(i => Int(i, "entity")).ToList(), AdditionalData = new List<int>(),
                    },
                });
                break;
            case "SubSpell":
                stats.SubSpells++;
                var start = new PowerHistorySubSpellStart
                {
                    SpellPrefabGuid = (string)e.Attribute("spellPrefabGuid") ?? "", SourceEntityId = Int(e, "source"),
                };
                start.TargetEntityIds.AddRange(e.Elements("SubSpellTarget").Select(x => Int(x, "entity")));
                list.Add(new PowerHistoryData { SubSpellStart = start });
                foreach (XElement child in e.Elements().Where(c => c.Name.LocalName != "SubSpellTarget")) Walk(child, list, stats);
                list.Add(new PowerHistoryData { SubSpellEnd = new PowerHistorySubSpellEnd() });
                break;
            case "ShuffleDeck":
                list.Add(new PowerHistoryData { ShuffleDeck = new PowerHistoryShuffleDeck { PlayerId = Int(e, "player_id", Int(e, "playerID")) } });
                break;
            default: // Choices, Options, SendOption, VOSpell...: not power history
                stats.Skipped++;
                break;
        }
    }

    private static PowerHistoryEntity Entity(int id, XElement e) => new()
    {
        Entity = id, Name = (string)e.Attribute("cardID") ?? "", Tags = Tags(e),
        DefTags = new List<Tag>(), TagLists = new List<TagList>(), DefTagLists = new List<TagList>(),
    };

    private static List<Tag> Tags(XElement e) =>
        e.Elements("Tag").Select(t => new Tag { Name = Enum(t, "tag", typeof(GAME_TAG)), Value = Value(t) }).ToList();

    /// <summary>A tag's value: a number, or (some exports) an enum name of the tag's type (ZONE=PLAY).</summary>
    private static int Value(XElement e)
    {
        string v = (string)e.Attribute("value") ?? "0";
        if (int.TryParse(v, out int n)) return n;
        foreach (Type t in new[] { typeof(TAG_ZONE), typeof(TAG_STEP), typeof(TAG_PLAYSTATE), typeof(TAG_CARDTYPE), typeof(TAG_CLASS) })
            if (System.Enum.TryParse(t, v, true, out object parsed)) return (int)parsed;
        return 0;
    }

    private static int Enum(XElement e, string attribute, Type type)
    {
        string v = (string)e.Attribute(attribute);
        if (string.IsNullOrEmpty(v)) return 0;
        if (int.TryParse(v, out int n)) return n;
        return System.Enum.TryParse(type, v, true, out object parsed) ? (int)parsed : 0;
    }

    private static int Int(XElement e, string attribute, int fallback = 0) =>
        int.TryParse((string)e.Attribute(attribute), out int n) ? n : fallback;

    private static ulong Long(XElement e, string attribute) =>
        ulong.TryParse((string)e.Attribute(attribute), out ulong n) ? n : 0;

    private static DateTime? Time(XElement e)
    {
        string ts = (string)e.Attribute("ts");
        if (ts == null) return null;
        if (DateTime.TryParse(ts, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime t)) return t.ToUniversalTime();
        // python-hslog writes times of day only (HH:mm:ss.ffffff): today's date
        return TimeSpan.TryParse(ts, out TimeSpan time) ? DateTime.UtcNow.Date + time : null;
    }

    private static string Info(string file, XDocument doc, XElement game, Stats stats)
    {
        XElement root = doc.Root;
        var sb = new StringBuilder();
        sb.AppendLine($"HSReplay import of {Path.GetFileName(file)} ({DateTime.Now:yyyy-MM-dd HH:mm})");
        sb.AppendLine($"replay version {(string)root?.Attribute("version")}, build {(string)root?.Attribute("build")}");
        sb.AppendLine($"game type {(string)game.Attribute("type")}, format {(string)game.Attribute("format")}, scenario {(string)game.Attribute("scenarioID")}");
        sb.AppendLine($"players: {game.Elements("Player").Count()} (names not copied)");
        sb.AppendLine(stats.ToString());
        return sb.ToString();
    }
}
