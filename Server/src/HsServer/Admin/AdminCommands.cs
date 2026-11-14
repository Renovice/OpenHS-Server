using System.Globalization;
using System.Text;
using HsServer.Lobby;

namespace HsServer.Admin;

/// <summary>
/// The admin console (roadmap E1): change a player's cards and dust, the event calendar, or start a part of their state
/// over. Runs inside the server (`HsServer admin` sends the command to it, so a logged-in client sees card and dust
/// changes at once, pushed like a craft) or, when the server is stopped, on the state files directly.
/// </summary>
internal sealed class AdminCommands(ServerData data)
{
    public sealed record Result(bool Ok, string Text);

    public const string Help = """
        HsServer admin <command>   (admin.bat <command>, or type <command> in the server window; there "stop" stops the server)
          players                                  every player, their dust and cards, who is online
          show <player>                            one player
          give <player> <card> [count] [premium]   add copies; card = id (EX1_116), dbf number or name (when several
                                                   cards match, e.g. reprints, it lists them with their ids);
                                                   premium = normal (default), golden, signature, diamond
          take <player> <card> [count] [premium]   remove copies
          currency <player> <kind> <amount|+N|-N>  set / add / take: dust, gold, runestones, renown, bgtokens,
                                                   bmec / bloodstones (event currency), tickets (Arena)
          dust <player> <amount|+N|-N>             = currency <player> dust ...
          packs <player>                           the player's unopened packs
          pack <player> <pack> <amount|+N|-N>      set / add / take packs; pack = name ("Standard", "Golden Standard",
                                                   "The Lost City") or id
          packtypes [text]                         pack types (id, name, openable here) whose name has <text>
          reset <player> collection|savedata|decks|luckydraw|blackmarket
                                                   start that part over from the recordings
          reset <player> heist [normal|heroic]     end the player's Dalaran Heist run (both modes when none given);
                                                   unlocks and win counts are kept
          quests <player>                          the player's quests (status, progress) and quest pools
          quest list [text]                        quests whose text has <text> (ids for the commands below)
          quest give <player> <quest id>           give a quest (new, progress 0)
          quest progress <player> <id> <n|+N>      set / add progress; reaching the goal completes it (XP)
          quest complete <player> <quest id>       complete it now (its reward-track XP)
          quest new <player>                       the quest pools' timers run out now: new dailies / weeklies
          quest reset <player>                     quests start over from the recorded login
          ranked <player>                          the player's ranks this season (stars, bonus, rating, Legend #)
          ladder [standard|wild|classic|twist]     this season's leaderboard of the server (default standard)
          seasonend <player>                       end the Ranked season for the player now: the season-end chest
                                                   (log in again to see it) and a new season with its Star Bonus
          event list [all|text]                    events grouped by what they are (none: the active ones; all; or a search)
          event on <name> [days]                   start now, for <days> (none: no end)
          event off <name>                         end now
          event set <name> <start|-> <end|->       dates as yyyy-MM-dd or yyyy-MM-ddTHH:mm (UTC), now, or - (open)
          practice                                 which decks the Practice Innkeeper plays
          practice decks <name start>              the Innkeeper plays one of the player's decks whose name starts
                                                   with this (its class; not the deck you play): "practice decks Legacy"
          practice decks off                       back to a random Basic + Classic deck
        Card and currency changes reach a logged-in client at once (gold: when the shop next asks); event and
        reset changes at its next login.
        """;

    private static readonly string[] Premiums = { "normal", "golden", "diamond", "signature" }; // index = premium

    public async Task<Result> RunAsync(string[] args)
    {
        try
        {
            return args.Length == 0 ? new Result(true, Help) : args[0].ToLowerInvariant() switch
            {
                "help" or "-h" or "--help" or "?" => new Result(true, Help),
                "players" => Players(),
                "show" when args.Length == 2 => Show(args[1]),
                "give" when args.Length >= 3 => await GiveAsync(args[1], args[2..], +1),
                "take" when args.Length >= 3 => await GiveAsync(args[1], args[2..], -1),
                "dust" when args.Length == 3 => await CurrencyAsync(args[1], "dust", args[2]),
                "currency" when args.Length == 4 => await CurrencyAsync(args[1], args[2], args[3]),
                "packs" when args.Length == 2 => Packs(args[1]),
                "pack" when args.Length >= 4 => await PackAsync(args[1], string.Join(' ', args[2..^1]), args[^1]),
                "packtypes" => PackTypes(string.Join(' ', args[1..])),
                "reset" when args.Length == 3 => Reset(args[1], args[2].ToLowerInvariant()),
                "reset" when args.Length == 4 && args[2].Equals("heist", StringComparison.OrdinalIgnoreCase) => Reset(args[1], "heist", args[3].ToLowerInvariant()),
                "event" when args.Length >= 2 => Event(args[1].ToLowerInvariant(), args[2..]),
                "quests" when args.Length == 2 => QuestList(args[1]),
                "ranked" when args.Length == 2 => RankedList(args[1]),
                "seasonend" when args.Length == 2 => await SeasonEndAsync(args[1]),
                "ladder" when args.Length <= 2 => RankedLadderList(args.Length == 2 ? args[1] : "standard"),
                "quest" when args.Length >= 2 => await QuestAsync(args[1].ToLowerInvariant(), args[2..]),
                "practice" => Practice(args[1..]),
                _ => Error($"unknown or incomplete command: {string.Join(' ', args)}\n\n{Help}"),
            };
        }
        catch (Exception e) when (e is FormatException or ArgumentException or IOException)
        {
            return Error(e.Message);
        }
    }

    private static Result Error(string text) => new(false, "error: " + text);

    // ---------- practice ----------

    /// <summary>`practice` / `practice decks &lt;name start&gt;|off`: the Practice Innkeeper's decks (Game.PracticeDecks).</summary>
    private static Result Practice(string[] rest)
    {
        if (rest.Length >= 2 && rest[0].Equals("decks", StringComparison.OrdinalIgnoreCase))
        {
            string prefix = string.Join(' ', rest[1..]);
            Game.PracticeDecks.Prefix = prefix.Equals("off", StringComparison.OrdinalIgnoreCase) ? null : prefix;
        }
        else if (rest.Length > 0)
            return Error("usage: practice | practice decks <name start> | practice decks off");
        string now = Game.PracticeDecks.Prefix;
        return new Result(true, now == null
            ? "Practice Innkeeper: a random Basic + Classic deck of its class"
            : $"Practice Innkeeper: one of the player's decks named \"{now}...\" of its class (another than yours when there is one); " +
              "the next game uses it");
    }

    // ---------- players ----------

    private Result Players()
    {
        var sb = new StringBuilder();
        foreach (string name in data.Accounts.Names())
        {
            PlayerAccount p = data.Accounts.Get(name);
            sb.AppendLine($"{p.Name,-20} {Summary(p)}");
        }
        return new Result(true, sb.Length == 0 ? "no players yet" : sb.ToString().TrimEnd());
    }

    private Result Show(string name)
    {
        if (Player(name, out PlayerAccount p) is { } error) return error;
        long B(CollectionStore.Currency c) => p.Collection.Balance(c);
        return new Result(true, $"{p.Name}: {Summary(p)}\n  renown {B(CollectionStore.Currency.Renown)}, Battlegrounds tokens " +
                                $"{B(CollectionStore.Currency.BgTokens)}, event currency {B(CollectionStore.Currency.Bmec)}, Arena tickets " +
                                $"{B(CollectionStore.Currency.ArenaTickets)}\n  decks {p.Decks.Count}, save data keys {p.SaveData.KeyCount}");
    }

    private static string Summary(PlayerAccount p)
    {
        var (stacks, copies) = p.Collection?.Totals() ?? (0, 0);
        long B(CollectionStore.Currency c) => p.Collection?.Balance(c) ?? 0;
        return $"dust {B(CollectionStore.Currency.Dust),7}  gold {B(CollectionStore.Currency.Gold),6}  cards {stacks} ({copies} copies)  " +
               $"{(p.IsOnline ? "online" : "offline")}";
    }

    private Result Player(string name, out PlayerAccount player)
    {
        player = null;
        if (!data.Accounts.Exists(name)) return Error($"no player '{name}' (players: {string.Join(", ", data.Accounts.Names())})");
        player = data.Accounts.Get(name);
        return player.Collection == null ? Error($"player '{name}' has no collection (server without card data)") : null;
    }

    // ---------- cards and dust ----------

    // give <player> <card words...> [count] [premium]: count and premium are taken from the end, the rest is the card.
    private async Task<Result> GiveAsync(string name, string[] rest, int sign)
    {
        if (Player(name, out PlayerAccount p) is { } error) return error;
        var words = rest.ToList();
        int premium = 0, count = 1;
        if (words.Count > 1 && Array.IndexOf(Premiums, words[^1].ToLowerInvariant()) is >= 0 and int pr) { premium = pr; words.RemoveAt(words.Count - 1); }
        if (words.Count > 1 && int.TryParse(words[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) { count = n; words.RemoveAt(words.Count - 1); }
        if (count <= 0) return Error("count must be 1 or more");
        string query = string.Join(' ', words);
        var found = FindCards(query);
        if (found.Count != 1)
            return Error(found.Count == 0 ? $"no card '{query}'"
                : $"'{query}' matches {found.Count} cards, say which:\n" + string.Join('\n', found.Take(12).Select(c => $"  {c.CardId,-16} {c.DbfId,7}  {c.Name}")));
        Engine.CardDef card = found[0];

        int before = p.Collection.Count(card.DbfId, premium);
        var change = p.Collection.Give(card.DbfId, premium, sign * count);
        int after = p.Collection.Count(card.DbfId, premium);
        string what = $"{card.Name} ({card.CardId}, {Premiums[premium]}): {before} -> {after}";
        return new Result(true, what + await PushAsync(p, change));
    }

    private static readonly Dictionary<string, CollectionStore.Currency> CurrencyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dust"] = CollectionStore.Currency.Dust, ["gold"] = CollectionStore.Currency.Gold,
        ["renown"] = CollectionStore.Currency.Renown, ["bgtokens"] = CollectionStore.Currency.BgTokens,
        ["bgtoken"] = CollectionStore.Currency.BgTokens, ["bmec"] = CollectionStore.Currency.Bmec,
        ["event"] = CollectionStore.Currency.Bmec, ["bloodstones"] = CollectionStore.Currency.Bmec, ["tickets"] = CollectionStore.Currency.ArenaTickets,
        ["arena"] = CollectionStore.Currency.ArenaTickets, ["runestones"] = CollectionStore.Currency.Runestones,
    };

    private async Task<Result> CurrencyAsync(string name, string kind, string amount)
    {
        if (Player(name, out PlayerAccount p) is { } error) return error;
        if (!CurrencyNames.TryGetValue(kind, out var currency))
            return Error($"no currency '{kind}' (dust, gold, runestones, renown, bgtokens, bmec / bloodstones, tickets)");
        if (!long.TryParse(amount, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value))
            return Error($"not an amount: {amount}");
        long before = p.Collection.Balance(currency);
        long target = amount.StartsWith('+') || amount.StartsWith('-') ? before + value : value;
        var change = p.Collection.SetBalance(currency, target);
        string push = await PushAsync(p, change);
        return new Result(true, $"{p.Name} {kind.ToLowerInvariant()}: {before} -> {p.Collection.Balance(currency)}" + push);
    }

    // ---------- packs ----------

    private Result Packs(string name)
    {
        if (Player(name, out PlayerAccount p) is { } error) return error;
        var owned = p.Collection.PackList();
        if (owned.Count == 0) return new Result(true, $"{p.Name} has no unopened packs");
        return new Result(true, string.Join('\n', owned.Select(o => $"{o.Count,4} x {data.Boosters.NameOf(o.Type) ?? "?"} ({o.Type})")));
    }

    private async Task<Result> PackAsync(string name, string packText, string amount)
    {
        if (Player(name, out PlayerAccount p) is { } error) return error;
        if (data.Boosters.Find(packText) is not { } type) return Error($"no pack type '{packText}' (packtypes lists them)");
        if (!int.TryParse(amount, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
            return Error($"not an amount: {amount}");
        int before = p.Collection.PackCount(type);
        int target = amount.StartsWith('+') || amount.StartsWith('-') ? before + value : value;
        var change = p.Collection.SetPacks(type, target);
        var pack = data.Boosters.Get(type);
        string note = data.Boosters.CanOpen(pack) ? "" : "\n  note: this pack type can't be opened here yet (no pool, or its open event is off)";
        return new Result(true, $"{p.Name} {pack.Name} ({type}) packs: {before} -> {p.Collection.PackCount(type)}" + await PushAsync(p, change) + note);
    }

    private Result PackTypes(string text)
    {
        var list = data.Boosters.Types.OrderBy(t => t)
            .Select(t => (Type: t, Pack: data.Boosters.Get(t)))
            .Where(x => text.Length == 0 || (x.Pack.Name ?? "").Contains(text, StringComparison.OrdinalIgnoreCase))
            .Select(x => $"{x.Type,5}  {x.Pack.Name,-40} {(data.Boosters.CanOpen(x.Pack) ? "opens" : "-")}").ToList();
        return new Result(true, list.Count == 0 ? "no such pack types" : string.Join('\n', list));
    }

    private static async Task<string> PushAsync(PlayerAccount p, PegasusUtil.ClientStateNotification change)
    {
        if (change == null) return " (no change)";
        int clients = await p.NotifyAsync(change);
        return clients > 0 ? " (sent to the client)" : " (offline: at the next login)";
    }

    /// <summary>A card by dbf number, card id, exact name (collectible first), else the collectible cards whose name
    /// contains the text.</summary>
    internal List<Engine.CardDef> FindCards(string query)
    {
        query = query.Trim();
        if (int.TryParse(query, NumberStyles.None, CultureInfo.InvariantCulture, out int dbf))
            return data.Cards.ByDbf(dbf) is { } byDbf ? new() { byDbf } : new();
        if (data.Cards.TryGet(query, out Engine.CardDef exactId)) return new() { exactId };
        var all = data.Cards.All.ToList();
        var byId = all.Where(c => string.Equals(c.CardId, query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byId.Count > 0) return byId;
        bool Collectible(Engine.CardDef c) => c.Tag(Engine.GAME_TAG.COLLECTIBLE) == 1;
        var byName = all.Where(c => string.Equals(c.Name, query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byName.Count(Collectible) == 1) return byName.Where(Collectible).ToList();
        if (byName.Count == 1) return byName;
        if (byName.Count > 1) return byName.OrderByDescending(Collectible).ToList();
        return all.Where(c => Collectible(c) && c.Name != null && c.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ---------- reset ----------

    private Result Reset(string name, string part, string mode = null)
    {
        if (!data.Accounts.Exists(name)) return Error($"no player '{name}' (players: {string.Join(", ", data.Accounts.Names())})");
        PlayerAccount p = data.Accounts.Get(name);
        if (part == "heist")
        {
            if (data.Heist == null) return Error("the Dalaran Heist isn't handled by this server");
            if (mode is not (null or "normal" or "heroic")) return Error($"heist mode '{mode}': normal or heroic");
            data.Heist.Clean(p.SaveData, mode == null ? null : mode == "heroic");
            return new Result(true, $"{p.Name}: no Dalaran Heist run in progress ({mode ?? "normal and heroic"}); unlocks kept" +
                                    (p.IsOnline ? " (log out and in to see it)" : ""));
        }
        if (part == "luckydraw" && data.LuckyDraw != null && p.Collection != null) data.LuckyDraw.StartOver(p.Collection);
        else if (part == "blackmarket" && p.Collection != null) BlackMarkets.StartOver(p.Collection);
        else if (!data.Accounts.Reset(p, part)) return Error($"can't reset '{part}' (collection, savedata, decks, luckydraw or blackmarket)");
        return new Result(true, $"{p.Name}: {part} started over from the recordings" + (p.IsOnline ? " (log out and in to see it)" : ""));
    }

    // ---------- quests (C4) ----------

    // ---------- Ranked ----------

    private Result RankedList(string name)
    {
        if (data.Ranked == null) return Error("Ranked isn't handled by this server");
        if (Player(name, out PlayerAccount p) is { } error) return error;
        return new Result(true, $"{p.Name}:{Environment.NewLine}" + string.Join(Environment.NewLine, data.Ranked.Describe(p).Select(l => "  " + l)));
    }

    private async Task<Result> SeasonEndAsync(string name)
    {
        if (data.Ranked == null) return Error("Ranked isn't handled by this server");
        if (Player(name, out PlayerAccount p) is { } error) return error;
        var changes = data.Ranked.EndSeasonNow(p, RankedLadder.Recorded(data.Account), data.Orders?.Random);
        if (changes.Count == 0) return new Result(true, $"{p.Name}: season ended (no Ranked games this season: no chest)");
        await PushAllAsync(p, changes);
        return new Result(true, $"{p.Name}: season ended, chest granted ({changes.Count - 1} change(s))" +
            (p.IsOnline ? " (log out and in to see it)" : " (shown at the next login)") + Environment.NewLine +
            string.Join(Environment.NewLine, data.Ranked.Describe(p).Select(l => "  " + l)));
    }

    private Result RankedLadderList(string format)
    {
        if (data.Ranked == null) return Error("Ranked isn't handled by this server");
        PegasusShared.FormatType? f = format.ToLowerInvariant() switch
        {
            "standard" => PegasusShared.FormatType.FT_STANDARD, "wild" => PegasusShared.FormatType.FT_WILD,
            "classic" => PegasusShared.FormatType.FT_CLASSIC, "twist" => PegasusShared.FormatType.FT_TWIST, _ => null,
        };
        if (f == null) return Error($"no format '{format}' (standard, wild, classic, twist)");
        var lines = data.Ranked.Ladder(f.Value);
        return new Result(true, lines.Count == 0 ? "nobody has played this season yet" : string.Join(Environment.NewLine, lines));
    }

    private Result QuestList(string name)
    {
        if (data.Quests == null) return Error("quests aren't handled by this server");
        if (Player(name, out PlayerAccount p) is { } error) return error;
        return new Result(true, $"{p.Name}:{Environment.NewLine}{data.Quests.Describe(p.Collection)}");
    }

    private async Task<Result> QuestAsync(string verb, string[] rest)
    {
        if (data.Quests == null) return Error("quests aren't handled by this server");
        if (verb == "list")
        {
            string text = string.Join(' ', rest);
            var found = data.Quests.Find(text).Take(40).ToList();
            return new Result(true, found.Count == 0 ? $"no quest with '{text}'"
                : string.Join(Environment.NewLine, found.Select(q => $"  {q.Id,5}  pool {q.Pool,3}  {q.Quota,4}  {System.Text.RegularExpressions.Regex.Replace(q.Description ?? "", @"\s+", " ")}")));
        }
        if (rest.Length == 0) return Error($"quest {verb}: which player?");
        if (Player(rest[0], out PlayerAccount p) is { } error) return error;
        int Id() => rest.Length >= 2 && int.TryParse(rest[1], NumberStyles.None, CultureInfo.InvariantCulture, out int id) && data.Quests.Rules.Get(id) != null
            ? id : throw new ArgumentException($"no quest '{(rest.Length >= 2 ? rest[1] : "")}' (quest list <text> finds ids)");
        switch (verb)
        {
            case "give":
            {
                int id = Id();
                return new Result(true, $"{p.Name}: quest {id} given" + await PushAllAsync(p, new IProtoBuf[] { data.Quests.Give(p.Collection, id) }));
            }
            case "progress" when rest.Length == 3:
            case "complete" when rest.Length == 2:
            {
                int id = Id();
                bool add = verb == "progress" && rest[2].StartsWith('+');
                int amount = verb == "complete" ? int.MaxValue / 2
                    : int.TryParse(rest[2].TrimStart('+'), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : throw new FormatException($"not a number: {rest[2]}");
                var (update, completed) = data.Quests.SetProgress(p.Collection, id, amount, add);
                if (update == null) return Error($"{p.Name} doesn't hold quest {id} as new or active (quest give {p.Name} {id})");
                var changes = data.RewardTracks?.AddXp(p.Collection, Quests.XpGrants(completed)) ?? new List<IProtoBuf>();
                changes.Add(update);
                changes.AddRange(data.Quests.Rewards(p, completed, data.RewardTracks)); // a reward list (event chains)
                var state = update.Quest.Single();
                return new Result(true, $"{p.Name}: quest {id} {state.Progress}/{data.Quests.Rules.Get(id).Quota}" +
                                        (completed.Count > 0 ? $", completed (+{data.Quests.Rules.Get(id).Xp} XP)" : "") + await PushAllAsync(p, changes));
            }
            case "new":
            {
                var changes = data.Quests.GrantNow(p.Collection);
                int granted = changes.OfType<PegasusUtil.PlayerQuestStateUpdate>().Sum(u => u.Quest.Count);
                return new Result(true, $"{p.Name}: {granted} new quest(s)" + await PushAllAsync(p, changes));
            }
            case "reset":
                data.Quests.StartOver(p.Collection);
                return new Result(true, $"{p.Name}: quests start over from the recorded login" + (p.IsOnline ? " (log out and in to see it)" : ""));
        }
        return Error($"unknown or incomplete quest command: quest {verb} {string.Join(' ', rest)}");
    }

    private static async Task<string> PushAllAsync(PlayerAccount p, IEnumerable<IProtoBuf> changes)
    {
        int clients = 0;
        foreach (IProtoBuf change in changes.Where(c => c != null)) clients = Math.Max(clients, await p.NotifyAsync(change));
        return clients > 0 ? " (sent to the client)" : p.IsOnline ? "" : " (offline: at the next login)";
    }

    // ---------- events ----------

    private EventNames eventNames;

    private Result Event(string verb, string[] rest)
    {
        EventCalendar calendar = data.Calendar;
        DateTime now = calendar.Clock();
        switch (verb)
        {
            case "list":
            {
                // (2026-11-14) grouped by what each event is (EventNames: the client's tables); no text = the active ones,
                // "all" = every event, else a search in the raw names and the labels
                eventNames ??= new EventNames(data.DbfDir);
                string text = string.Join(' ', rest);
                bool all = text.Equals("all", StringComparison.OrdinalIgnoreCase);
                var list = calendar.All()
                    .Select(e => (Entry: e, What: eventNames.Of(e.Name)))
                    .Where(x => text.Length == 0 ? x.Entry.IsActive(now)
                        : all || x.Entry.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || x.What.Label.Contains(text, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (list.Count == 0) return new Result(true, "no such events");
                var lines = new List<string>();
                foreach (var group in list.GroupBy(x => x.What.Kind).OrderBy(g => g.Key))
                {
                    lines.Add($"== {EventNames.Titles[group.Key]} ({group.Count()})");
                    foreach (var x in group.OrderBy(x => x.What.Label, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Entry.Name, StringComparer.Ordinal))
                        lines.Add("  " + Line(x.Entry, now) + (x.What.Label.Length > 0 ? "  " + x.What.Label : ""));
                }
                lines.Add($"{list.Count} events" + (text.Length == 0 ? " on now (event list all: every event; event list <text>: search)" : ""));
                return new Result(true, string.Join('\n', lines));
            }
            case "on" when rest.Length is 1 or 2:
            {
                double? days = rest.Length == 2 ? double.Parse(rest[1], CultureInfo.InvariantCulture) : null;
                return SetEvent(rest[0], now, days is { } d ? now.AddDays(d) : null);
            }
            case "off" when rest.Length == 1:
                return SetEvent(rest[0], calendar.Get(rest[0])?.Start is { } s && s < now ? s : now.AddSeconds(-1), now);
            case "set" when rest.Length == 3:
                return SetEvent(rest[0], Date(rest[1], now), Date(rest[2], now));
            default:
                return Error("event list [all|text] | event on <name> [days] | event off <name> | event set <name> <start|-> <end|->");
        }
    }

    private Result SetEvent(string name, DateTime? start, DateTime? end)
    {
        EventCalendar calendar = data.Calendar;
        bool known = calendar.Knows(name);
        calendar.Set(name, start, end);
        string warning = known ? "" : "\n  note: the client's data never names this event, so nothing in the game uses it";
        return new Result(true, Line(calendar.Get(name), calendar.Clock()) +
                                "\n  the server uses it now; clients see it at their next login" + warning);
    }

    private static string Line(EventCalendar.Entry e, DateTime now) =>
        $"{e.Name,-44} {(e.IsActive(now) ? "ACTIVE" : "off"),-6}  {Show(e.Start)} .. {Show(e.End)}";

    private static string Show(DateTime? d) => d?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "-";

    private static DateTime? Date(string text, DateTime now)
    {
        if (text == "-") return null;
        if (text.Equals("now", StringComparison.OrdinalIgnoreCase)) return now;
        string[] formats = { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss" };
        if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime d))
            return DateTime.SpecifyKind(d, DateTimeKind.Utc);
        throw new FormatException($"not a date: {text} (yyyy-MM-dd, yyyy-MM-ddTHH:mm, now or -)");
    }
}
