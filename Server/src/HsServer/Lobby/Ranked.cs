using System.Text.Json;
using PegasusShared;
using PegasusUtil;

namespace HsServer.Lobby;

/// <summary>
/// The Ranked ladder from the client data: the current normal league (League: NORMAL, highest version; its
/// SeasonCardBackMinWins), its ranks (LeagueRank: 3 stars each, CanLoseStars / CanLoseLevel, WinStreakThreshold;
/// StarLevel 51 = Legend) and the season card backs (CardBack Source SEASON, Data1 = season id).
/// </summary>
internal sealed class RankedRules
{
    public sealed record Rank(int StarLevel, int Stars, bool CanLoseStars, bool CanLoseLevel, int WinStreakThreshold, string Name);

    public int LeagueId { get; }
    public int SeasonCardBackMinWins { get; } = 5;
    public int LegendLevel { get; }
    private readonly Dictionary<int, Rank> ranks = new();
    private readonly Dictionary<int, int> seasonCardBacks = new();

    public RankedRules(string dbfDir)
    {
        if (dbfDir == null || !File.Exists(Path.Combine(dbfDir, "League.json"))) return;
        JsonElement league = Records(dbfDir, "League.json").Where(r => Str(r, "LeagueType") == "NORMAL").OrderBy(r => Int(r, "LeagueVersion")).Last();
        LeagueId = Int(league, "ID");
        if (Int(league, "SeasonCardBackMinWins") > 0) SeasonCardBackMinWins = Int(league, "SeasonCardBackMinWins");
        foreach (JsonElement r in Records(dbfDir, "LeagueRank.json").Where(r => Int(r, "LeagueId") == LeagueId))
            ranks[Int(r, "StarLevel")] = new Rank(Int(r, "StarLevel"), Int(r, "Stars"), Bool(r, "CanLoseStars"), Bool(r, "CanLoseLevel"),
                Int(r, "WinStreakThreshold"), Str(r, "RankName"));
        LegendLevel = ranks.Count == 0 ? 0 : ranks.Keys.Max();
        foreach (JsonElement r in Records(dbfDir, "CardBack.json").Where(r => Str(r, "Source") == "SEASON"))
            seasonCardBacks[Int(r, "Data1")] = Int(r, "ID");
    }

    public Rank Get(int starLevel) => ranks.GetValueOrDefault(starLevel);
    public int? SeasonCardBack(int season) => seasonCardBacks.TryGetValue(season, out int id) ? id : null;

    /// <summary>Rank floors (wiki Ranked: "Rank floors occur at every 5 ranks in the ladder"): Bronze 5, Silver 10 …
    /// Diamond 5. (CanLoseLevel can't tell: it is off for all of Bronze; live's Star Bonus dropped at Bronze 5 too.)</summary>
    public bool IsFloor(int starLevel) => starLevel > 1 && starLevel < LegendLevel && (starLevel - 1) % 5 == 0;

    // ---------- seasons ----------

    // Live: season 155 ended 2026-09-30 22:00 UTC and 156 ends 2026-10-31 23:00 UTC — calendar months ending at midnight
    // Central European time (the EU region's reset). Season 156 = October 2026.
    private const int AnchorSeason = 156, AnchorYear = 2026, AnchorMonth = 10;
    private static readonly TimeZoneInfo Region = FindRegion();

    public static (int Season, DateTime EndUtc) SeasonAt(DateTime utc)
    {
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(utc, Region);
        int season = AnchorSeason + (local.Year - AnchorYear) * 12 + (local.Month - AnchorMonth);
        var nextMonth = new DateTime(local.Year, local.Month, 1).AddMonths(1);
        return (season, TimeZoneInfo.ConvertTimeToUtc(nextMonth, Region));
    }

    private static TimeZoneInfo FindRegion()
    {
        foreach (string id in new[] { "Central European Standard Time", "Europe/Berlin" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (Exception) { }
        return TimeZoneInfo.Utc;
    }

    /// <summary>
    /// The Star Bonus a season starts with, from last season's best rank (wiki Ranked, minimum Star Bonus: Bronze 10 2x,
    /// Silver 5 3x, Gold 10 4x, Gold 5 5x, Platinum 10 6x, Platinum 5 7x, Diamond 10 8x, Diamond 5 9x, Legend 10x).
    /// Live can give more (MMR); that part isn't known.
    /// </summary>
    public static int SeasonStartBonus(int bestLastSeason) => bestLastSeason switch
    {
        >= 51 => 10, >= 46 => 9, >= 41 => 8, >= 36 => 7, >= 31 => 6, >= 26 => 5, >= 21 => 4, >= 16 => 3, _ => 2,
    };

    private static List<JsonElement> Records(string dir, string file)
    {
        string path = Path.Combine(dir, file);
        if (!File.Exists(path)) return new List<JsonElement>();
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        return doc.RootElement.GetProperty("records").EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static int Int(JsonElement r, string name) =>
        r.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? int.TryParse(v.GetString(), out int i) ? i : 0 : v.GetInt32() : 0;
    private static bool Bool(JsonElement r, string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    private static string Str(JsonElement r, string name) => r.TryGetProperty(name, out var v) ? v.ToString() : "";
}

/// <summary>A player's Ranked state per format (saved with the collection).</summary>
internal sealed class RankedBook
{
    public Dictionary<int, FormatRank> Formats { get; set; } = new();
    /// <summary>Live's recorded medal was taken over (once).</summary>
    public bool Adopted { get; set; }
    /// <summary>Seasons whose card back was granted.</summary>
    public List<int> CardBackSeasons { get; set; } = new();
    /// <summary>Seasons whose end-of-season chest was given.</summary>
    public List<int> ChestSeasons { get; set; } = new();
}

internal sealed class FormatRank
{
    public int Season { get; set; }
    public int StarLevel { get; set; } = 1;
    public int Stars { get; set; }
    public int Streak { get; set; }
    public int SeasonWins { get; set; }
    public int SeasonGames { get; set; }
    public int StarsPerWin { get; set; } = 1;
    public int BestStarLevel { get; set; } = 1;
    public int BestStars { get; set; }
    public int LegendRank { get; set; }
    /// <summary>Hidden matchmaking rating (Glicko: rating and its deviation), carried over between seasons.</summary>
    public double Rating { get; set; } = Glicko.InitialRating;
    public double Deviation { get; set; } = Glicko.InitialDeviation;
    public DateTime? RatedUtc { get; set; }
}

/// <summary>
/// The hidden rating behind matchmaking, Legend numbers and the MMR part of the Star Bonus. Live's formula isn't public
/// (and the client never gets it: live's MedalInfo never carries Rating / Variance); ours is Glicko-1 (Glickman 1995):
/// a rating with a deviation that shrinks with games and grows back while a player is away.
/// </summary>
internal static class Glicko
{
    public const double InitialRating = 1500, InitialDeviation = 350, MinDeviation = 30;
    // deviation growth per day away: back from 50 to 350 in about 100 days
    private const double GrowthPerDay = 34.6;
    private static readonly double Q = Math.Log(10) / 400;

    /// <summary>One game: score 1 win, 0 loss, 0.5 draw, against an opponent's (rating, deviation).</summary>
    public static (double Rating, double Deviation) Update(double rating, double deviation, DateTime? lastGame, DateTime now,
        double opponentRating, double opponentDeviation, double score)
    {
        if (lastGame is { } last && now > last)
            deviation = Math.Min(InitialDeviation, Math.Sqrt(deviation * deviation + GrowthPerDay * GrowthPerDay * (now - last).TotalDays));
        double g = G(opponentDeviation);
        double expected = 1 / (1 + Math.Pow(10, -g * (rating - opponentRating) / 400));
        double d2 = 1 / (Q * Q * g * g * expected * (1 - expected));
        double denominator = 1 / (deviation * deviation) + 1 / d2;
        return (rating + Q / denominator * g * (score - expected), Math.Max(MinDeviation, Math.Sqrt(1 / denominator)));
    }

    private static double G(double deviation) => 1 / Math.Sqrt(1 + 3 * Q * Q * deviation * deviation / (Math.PI * Math.PI));
}

/// <summary>
/// The server's leaderboard: every player's rank and rating per season and format (Server/state/ranked-leaderboard.json),
/// so Legend numbers, High Legend and the MMR Star Bonus compare players with each other once a server has several.
/// </summary>
internal sealed class RankedLeaderboard(string path = null)
{
    public sealed class Entry
    {
        public string Player { get; set; }
        public int Season { get; set; }
        public int Format { get; set; }
        public int StarLevel { get; set; }
        public int Stars { get; set; }
        public int BestStarLevel { get; set; }
        public double Rating { get; set; }
    }

    private readonly object gate = new();
    private List<Entry> entries;

    public void Update(string player, int season, FormatType format, FormatRank r)
    {
        lock (gate)
        {
            Load();
            Entry e = entries.FirstOrDefault(x => x.Player == player && x.Season == season && x.Format == (int)format);
            if (e == null) entries.Add(e = new Entry { Player = player, Season = season, Format = (int)format });
            (e.StarLevel, e.Stars, e.BestStarLevel, e.Rating) = (r.StarLevel, r.Stars, r.BestStarLevel, r.Rating);
            entries.RemoveAll(x => x.Season < season - 2); // the last seasons are enough
            Save();
        }
    }

    public List<Entry> Entries(int season, FormatType format)
    {
        lock (gate)
        {
            Load();
            return entries.Where(e => e.Season == season && e.Format == (int)format).ToList();
        }
    }

    private void Load()
    {
        if (entries != null) return;
        entries = path != null && File.Exists(path) ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path)) ?? new() : new();
    }

    private void Save()
    {
        if (path == null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>
/// The Ranked ladder (wiki Ranked; client MedalInfoTranslator; FINDINGS 2026-10-03). The client only shows what MedalInfo
/// (232; the login's SecondaryClientState.MedalInfo) says, and animates a game by comparing it with the previous one: the
/// server must count SeasonGames. A win gives StarsPerWin stars ("Star Bonus"), doubled "if the player also won their
/// previous two non-drawn ranked matches" below Diamond 5 (the rank's WinStreakThreshold); each rank has three stars and
/// a win past the third ranks up (live: Silver 8 2★ + 8 → Silver 5 1★); "Upon climbing and hitting each ranked floor, a
/// player's Star Bonus will decrease by 1" (live: x10 → x9 at Bronze 5 … x6 at Gold 10). A loss takes a star where the
/// rank CanLoseStars (not in Bronze), and at 0 stars drops a rank unless it is a floor. Each season starts at Bronze 10
/// with the wiki's minimum Star Bonus for last season's best rank; the season card back comes at 5 wins.
/// Ratings (Glicko, server-side only) and the server leaderboard: a Legend number is the player's place among this
/// server's Legend players by rating ("the 275th best Ranked player in their region"); the top 10 % of last season's
/// Legend start at 11x ("High Legend"); and the Star Bonus is "based on both the player's MMR and numbered rank in the
/// previous season": ours takes the better of the best rank's bonus and the rank players with the same rating percentile
/// reached on this server. The AI counts as a fixed 1500 rating.
/// UNCLEAR: where a dropped rank lands (ours: 2 stars), live's MMR formula, the season-end chest and GAINED_MEDAL notice.
/// </summary>
internal sealed class RankedLadder(RankedRules rules, Func<DateTime> clock = null, RankedLeaderboard leaderboard = null,
    RankedSeasonRewards seasonRewards = null)
{
    /// <summary>
    /// A season ended since the player last played (run at login, before the collection is sent): the end-of-season chest for
    /// the best rank of last season in Standard or Wild (wiki Ranked: "whichever is higher"), every milestone's rewards up to
    /// it added up (client MedalInfoTranslator.GetRankedRewardsEarned), granted now and announced with one GAINED_MEDAL
    /// notice (origin SEASON, OriginData = the season) that stays in the login until dismissed: the client shows one
    /// season-end dialog (DialogManager.MaybeShowSeasonEndDialog). Nothing for a season without Ranked games. Returns what
    /// changed (to push when the player is online).
    /// </summary>
    /// <summary>Admin: ends this season for the player now, as if the month were over: this season's ranks become last
    /// season's (the chest for them, a new season from Bronze 10 with its Star Bonus at the next medal).</summary>
    public List<IProtoBuf> EndSeasonNow(PlayerAccount player, MedalInfo recorded, HsServer.Shop.RandomCards random, Random rng = null)
    {
        if (player.Collection is not { } collection) return new List<IProtoBuf>();
        var (season, _) = RankedRules.SeasonAt(Now);
        List<IProtoBuf> changes = SeasonEnd(player, recorded, random, rng, season + 1);
        // a fresh season with the same number (the real end of the month still gives a chest for what is played after)
        collection.RankedBook(book =>
        {
            foreach (FormatType f in book.Formats.Keys.Select(k => (FormatType)k).ToList())
            {
                book.Formats[(int)f].Season = season - 1;
                Current(player.Name, book, f, season);
            }
            book.ChestSeasons.Remove(season);
            return 0;
        });
        return changes;
    }

    public List<IProtoBuf> SeasonEnd(PlayerAccount player, MedalInfo recorded, HsServer.Shop.RandomCards random, Random rng = null,
        int? currentSeason = null)
    {
        var changes = new List<IProtoBuf>();
        if (seasonRewards == null || player.Collection is not { } collection) return changes;
        int season = currentSeason ?? RankedRules.SeasonAt(Now).Season;
        (FormatType Format, FormatRank Rank)? best = null;
        collection.RankedBook(book =>
        {
            Adopt(book, recorded);
            foreach (FormatType f in new[] { FormatType.FT_STANDARD, FormatType.FT_WILD })
                if (book.Formats.TryGetValue((int)f, out FormatRank r) && r.Season < season && r.SeasonGames > 0 && !book.ChestSeasons.Contains(r.Season) &&
                    (best == null || r.BestStarLevel > best.Value.Rank.BestStarLevel))
                    best = (f, r);
            if (best is { } b) book.ChestSeasons.Add(b.Rank.Season);
            return 0;
        });
        if (best is not { } chosen) return changes;
        var chest = new RewardChest();
        foreach (var (kind, data, count) in seasonRewards.Chest(chosen.Rank.BestStarLevel, chosen.Rank.Season))
        {
            if (kind == "PACK")
            {
                if (collection.SetPacks(data, collection.PackCount(data) + count) is { } packs) changes.Add(packs);
                chest.Bag.Add(new RewardBag { RewardBooster = new ProfileNoticeRewardBooster { BoosterType = data, BoosterCount = count } });
            }
            else if (random != null)
                foreach (int dbf in random.Pick(data, count, d => Enumerable.Range(0, 4).Sum(p => collection.Count(d, p)), rng ?? Random.Shared))
                {
                    if (collection.Give(dbf, CollectionStore.Normal, 1) is { } card) changes.Add(card);
                    chest.Bag.Add(new RewardBag { RewardCard = new ProfileNoticeRewardCard { Card = new CardDef { Asset = dbf, Premium = 0 }, Quantity = 1 } });
                }
        }
        var medal = new ProfileNoticeMedal
        {
            StarLevel = chosen.Rank.StarLevel, BestStarLevel = chosen.Rank.BestStarLevel, LeagueId = rules.LeagueId, Chest = chest,
            MedalType_ = chosen.Format == FormatType.FT_WILD ? ProfileNoticeMedal.MedalType.WILD_MEDAL : ProfileNoticeMedal.MedalType.STANDARD_MEDAL,
        };
        if (chosen.Rank.StarLevel == rules.LegendLevel) medal.LegendRank = Math.Max(1, chosen.Rank.LegendRank);
        changes.Add(collection.AddPendingNotice(new ProfileNotice { Origin = 1, OriginData = chosen.Rank.Season, Medal = medal })); // SEASON
        return changes;
    }

    /// <summary>The AI opponent's rating (not measured against people yet).</summary>
    public static readonly (double Rating, double Deviation) AiOpponent = (Glicko.InitialRating, 50);

    private DateTime Now => clock?.Invoke() ?? DateTime.UtcNow;

    // Ranked BnetGameTypes → FormatType (LeagueGameType: STANDARD 2, WILD 30, CLASSIC 58, TWIST 63)
    public static FormatType? FormatOf(long bnetGameType) => bnetGameType switch
    {
        2 => FormatType.FT_STANDARD, 30 => FormatType.FT_WILD, 58 => FormatType.FT_CLASSIC, 63 => FormatType.FT_TWIST, _ => null,
    };

    /// <summary>Live's medal in a recorded login: the last MedalInfo (232), else the login's SecondaryClientState.</summary>
    public static MedalInfo Recorded(RecordedAccount account)
    {
        if (account == null) return null;
        if (account.LastUtil.TryGetValue(232, out byte[] medal)) return HsServer.Protocol.PegasusPackets.Deserialize<MedalInfo>(medal);
        return account.LastUtil.TryGetValue(714, out byte[] login)
            ? HsServer.Protocol.PegasusPackets.Deserialize<SecondaryClientStateResponse>(login).SecondaryState?.MedalInfo : null;
    }

    private static readonly FormatType[] Formats = { FormatType.FT_WILD, FormatType.FT_STANDARD, FormatType.FT_CLASSIC, FormatType.FT_TWIST };

    /// <summary>The player's MedalInfo: every format, this season (live's recorded medal taken over once).</summary>
    public MedalInfo Medal(PlayerAccount player, MedalInfo recorded)
    {
        var (season, end) = RankedRules.SeasonAt(Now);
        var medal = new MedalInfo();
        var current = new List<(FormatType Format, FormatRank Rank)>();
        player.Collection.RankedBook(book =>
        {
            Adopt(book, recorded);
            foreach (FormatType format in Formats) current.Add((format, Current(player.Name, book, format, season)));
            return 0;
        });
        foreach (var (format, r) in current)
        {
            leaderboard?.Update(player.Name, season, format, r);
            medal.MedalData.Add(Data(player.Name, r, format, season, end));
        }
        return medal;
    }

    /// <summary>A Ranked game ended: stars, streak, season wins / games; the season card back at the 5th win.</summary>
    public (List<IProtoBuf> Changes, string Log) GameEnded(PlayerAccount player, long bnetGameType, bool won, MedalInfo recorded,
        (double Rating, double Deviation)? opponent = null)
    {
        var (opponentRating, opponentDeviation) = opponent ?? AiOpponent;
        var changes = new List<IProtoBuf>();
        if (FormatOf(bnetGameType) is not { } format) return (changes, null);
        var (season, _) = RankedRules.SeasonAt(Now);
        string log = null;
        bool cardBack = false;
        player.Collection.RankedBook(book =>
        {
            Adopt(book, recorded);
            FormatRank r = Current(player.Name, book, format, season);
            string before = Describe(r);
            r.SeasonGames++;
            (r.Rating, r.Deviation) = Glicko.Update(r.Rating, r.Deviation, r.RatedUtc, Now, opponentRating, opponentDeviation, won ? 1 : 0);
            r.RatedUtc = Now;
            if (won) Win(r); else Lose(r);
            if (r.StarLevel > r.BestStarLevel || r.StarLevel == r.BestStarLevel && r.Stars > r.BestStars)
                (r.BestStarLevel, r.BestStars) = (r.StarLevel, r.Stars);
            cardBack = won && r.SeasonWins >= rules.SeasonCardBackMinWins && !book.CardBackSeasons.Contains(season);
            if (cardBack) book.CardBackSeasons.Add(season);
            log = $"{format} {(won ? "win" : "loss")}: {before} -> {Describe(r)}, rating {r.Rating:0}";
            leaderboard?.Update(player.Name, season, format, r);
            return 0;
        });
        if (cardBack && rules.SeasonCardBack(season) is { } back && player.Collection.GrantCosmetic(CollectionStore.Cosmetic.CardBack, back) is { } notice)
        {
            changes.Add(notice);
            log += $"; season card back {back}";
        }
        return (changes, log);
    }

    private void Win(FormatRank r)
    {
        r.SeasonWins++;
        RankedRules.Rank rank = rules.Get(r.StarLevel);
        bool streak = rank is { WinStreakThreshold: > 0 } && r.Streak + 1 >= rank.WinStreakThreshold;
        r.Streak++;
        int stars = Math.Max(1, r.StarsPerWin) * (streak ? 2 : 1);
        for (int i = 0; i < stars && r.StarLevel < rules.LegendLevel; i++)
        {
            if (r.Stars < (rules.Get(r.StarLevel)?.Stars ?? 3)) { r.Stars++; continue; }
            r.StarLevel++;
            r.Stars = r.StarLevel == rules.LegendLevel ? 0 : 1;
            // a floor reached for the first time this season lowers the Star Bonus
            if (r.StarLevel > r.BestStarLevel && rules.IsFloor(r.StarLevel)) r.StarsPerWin = Math.Max(1, r.StarsPerWin - 1);
            if (r.StarLevel > r.BestStarLevel) (r.BestStarLevel, r.BestStars) = (r.StarLevel, r.Stars);
        }
        if (r.StarLevel == rules.LegendLevel && r.LegendRank == 0) r.LegendRank = 1;
    }

    private void Lose(FormatRank r)
    {
        r.Streak = 0;
        RankedRules.Rank rank = rules.Get(r.StarLevel);
        if (rank is not { CanLoseStars: true }) return; // Bronze, Legend
        if (r.Stars > 0) { r.Stars--; return; }
        if (rank.CanLoseLevel) { r.StarLevel--; r.Stars = 2; }
    }

    // The format's state this season: a new season starts at Bronze 10 with its Star Bonus; the rating carries over.
    private FormatRank Current(string player, RankedBook book, FormatType format, int season)
    {
        if (!book.Formats.TryGetValue((int)format, out FormatRank r))
            book.Formats[(int)format] = r = new FormatRank { Season = season, StarsPerWin = RankedRules.SeasonStartBonus(1) };
        if (r.Season != season)
            book.Formats[(int)format] = r = new FormatRank
            {
                Season = season, StarsPerWin = r.Season < season ? SeasonStartBonus(player, format, r) : RankedRules.SeasonStartBonus(1),
                Rating = r.Rating, Deviation = r.Deviation, RatedUtc = r.RatedUtc,
            };
        return r;
    }

    /// <summary>The Star Bonus a new season starts with: the minimum for last season's best rank; 11x for the top 10 % of
    /// last season's Legend players on this server; and the bonus for the rank that players with the same rating
    /// percentile reached last season (the MMR part) — whichever is highest.</summary>
    private int SeasonStartBonus(string player, FormatType format, FormatRank last)
    {
        int bonus = RankedRules.SeasonStartBonus(last.BestStarLevel);
        if (leaderboard == null) return bonus;
        List<RankedLeaderboard.Entry> season = leaderboard.Entries(last.Season, format);
        if (last.BestStarLevel >= rules.LegendLevel)
        {
            var legends = season.Where(e => e.BestStarLevel >= rules.LegendLevel).OrderByDescending(e => e.Rating).ToList();
            int place = legends.FindIndex(e => e.Player == player) + 1;
            if (place > 0 && place <= Math.Max(1, (int)Math.Ceiling(legends.Count * 0.1))) bonus = Math.Max(bonus, 11);
        }
        if (season.Count >= 2 && season.Any(e => e.Player == player))
        {
            var byRating = season.OrderBy(e => e.Rating).ToList();
            double percentile = byRating.FindIndex(e => e.Player == player) / (double)(byRating.Count - 1);
            var levels = season.Select(e => e.BestStarLevel).OrderBy(l => l).ToList();
            bonus = Math.Max(bonus, RankedRules.SeasonStartBonus(levels[(int)Math.Round(percentile * (levels.Count - 1))]));
        }
        return bonus;
    }

    /// <summary>The player's Legend number: their place among this season's Legend players of the format by rating.</summary>
    private int LegendNumber(string player, FormatRank r, FormatType format, int season)
    {
        if (leaderboard == null) return Math.Max(1, r.LegendRank);
        var legends = leaderboard.Entries(season, format).Where(e => e.StarLevel >= rules.LegendLevel)
            .OrderByDescending(e => e.Rating).ThenBy(e => e.Player, StringComparer.Ordinal).ToList();
        int place = legends.FindIndex(e => e.Player == player) + 1;
        return place > 0 ? place : legends.Count + 1;
    }

    /// <summary>The player's hidden rating for a format: what matchmaking pairs by (live matches on MMR, not on the visible
    /// rank; Legend numbers show it as a place).</summary>
    public (double Rating, double Deviation) Rating(PlayerAccount player, FormatType format)
    {
        var (season, _) = RankedRules.SeasonAt(Now);
        FormatRank r = player.Collection.RankedBook(book => Current(player.Name, book, format, season));
        return (r.Rating, r.Deviation);
    }

    /// <summary>
    /// Matchmaking by rating (for player-vs-player queues): among the waiting players, the one closest in rating, within a
    /// window that widens the longer either side has waited (100 rating points, +50 per 10 s). Live's exact algorithm
    /// isn't public; this is ours. Null = nobody close enough yet.
    /// </summary>
    public static T ClosestByRating<T>(double rating, TimeSpan waited, IEnumerable<(T Player, double Rating, TimeSpan Waited)> waiting)
        where T : class
    {
        static double Window(TimeSpan w) => 100 + 50 * Math.Floor(w.TotalSeconds / 10);
        return waiting
            .Where(o => Math.Abs(o.Rating - rating) <= Math.Max(Window(waited), Window(o.Waited)))
            .OrderBy(o => Math.Abs(o.Rating - rating))
            .Select(o => o.Player).FirstOrDefault();
    }

    /// <summary>Admin view: the player's formats this season (rank, stars, bonus, rating, Legend number).</summary>
    public List<string> Describe(PlayerAccount player)
    {
        var (season, _) = RankedRules.SeasonAt(Now);
        var current = new List<(FormatType, FormatRank)>();
        player.Collection.RankedBook(book => { foreach (FormatType f in Formats) current.Add((f, Current(player.Name, book, f, season))); return 0; });
        return current.Select(c => $"{c.Item1,-12} {Describe(c.Item2)}  games {c.Item2.SeasonGames} wins {c.Item2.SeasonWins}  " +
                                   $"rating {c.Item2.Rating:0} ±{c.Item2.Deviation:0}" +
                                   (c.Item2.StarLevel == rules.LegendLevel ? $"  Legend #{LegendNumber(player.Name, c.Item2, c.Item1, season)}" : "")).ToList();
    }

    /// <summary>Admin view: this season's leaderboard of a format, best first.</summary>
    public List<string> Ladder(FormatType format)
    {
        var (season, _) = RankedRules.SeasonAt(Now);
        return (leaderboard?.Entries(season, format) ?? new()).OrderByDescending(e => e.StarLevel).ThenByDescending(e => e.Stars)
            .ThenByDescending(e => e.Rating)
            .Select((e, i) => $"{i + 1,3}. {e.Player,-16} {rules.Get(e.StarLevel)?.Name ?? e.StarLevel.ToString()} {e.Stars}*  rating {e.Rating:0}").ToList();
    }

    private void Adopt(RankedBook book, MedalInfo recorded)
    {
        if (book.Adopted) return;
        book.Adopted = true;
        foreach (MedalInfoData d in recorded?.MedalData ?? new List<MedalInfoData>())
            if (d.LeagueId == rules.LeagueId && d.HasFormatType)
                book.Formats[(int)d.FormatType] = new FormatRank
                {
                    Season = d.SeasonId, StarLevel = Math.Max(1, d.StarLevel), Stars = d.Stars, Streak = d.Streak, SeasonWins = d.SeasonWins,
                    SeasonGames = d.SeasonGames, StarsPerWin = Math.Max(1, d.StarsPerWin), BestStarLevel = Math.Max(1, d.BestStarLevel),
                    BestStars = d.BestStars, LegendRank = d.LegendRank,
                };
    }

    private MedalInfoData Data(string player, FormatRank r, FormatType format, int season, DateTime end)
    {
        var d = new MedalInfoData
        {
            LeagueId = rules.LeagueId, StarLevel = r.StarLevel, Stars = r.Stars, Streak = r.Streak, SeasonWins = r.SeasonWins,
            SeasonGames = r.SeasonGames, StarsPerWin = r.StarsPerWin, BestStarLevel = r.BestStarLevel, BestStars = r.BestStars,
            SeasonId = season, FormatType = format, SecondsUntilSeasonEnd = Math.Max(0, (int)(end - Now).TotalSeconds),
        };
        if (r.StarLevel == rules.LegendLevel) d.LegendRank = LegendNumber(player, r, format, season);
        return d;
    }

    private string Describe(FormatRank r) => $"{rules.Get(r.StarLevel)?.Name ?? r.StarLevel.ToString()} {r.Stars}* streak {r.Streak} x{r.StarsPerWin}";
}

/// <summary>
/// The end-of-season rewards from the client data: each rank milestone's bag (LeagueRank.RewardBagId) and what bags hold
/// (RewardBag: RANDOM_CARD of a BoosterCardSet, the season's RANKED_SEASON_REWARD_PACK, REWARD_CHEST_CONTENTS of more
/// bags), and the season's pack (Booster.RankedRewardInitialSeason: the newest at or before the season,
/// RankMgr.GetRankedRewardBoosterIdForSeasonId).
/// </summary>
internal sealed class RankedSeasonRewards
{
    private readonly Dictionary<int, int> bagOfLevel = new();
    private readonly Dictionary<int, List<(string Reward, int Base, int Data)>> bags = new();
    private readonly Dictionary<int, List<int>> contents = new();
    private readonly List<(int Booster, int Season)> seasonPacks = new();

    public RankedSeasonRewards(string dbfDir, int leagueId)
    {
        if (dbfDir == null || !File.Exists(Path.Combine(dbfDir, "RewardBag.json"))) return;
        foreach (JsonElement r in Records(dbfDir, "LeagueRank.json"))
            if (Int(r, "LeagueId") == leagueId && Int(r, "RewardBagId") > 0) bagOfLevel[Int(r, "StarLevel")] = Int(r, "RewardBagId");
        foreach (JsonElement r in Records(dbfDir, "RewardBag.json"))
        {
            int bag = Int(r, "BagId");
            if (!bags.TryGetValue(bag, out var list)) bags[bag] = list = new();
            list.Add((r.TryGetProperty("Reward", out var reward) ? reward.ToString() : "", Int(r, "Base"), Int(r, "RewardData")));
        }
        foreach (JsonElement r in Records(dbfDir, "RewardChestContents.json"))
            contents[Int(r, "ID")] = new[] { "Bag1", "Bag2", "Bag3", "Bag4", "Bag5" }.Select(b => Int(r, b)).Where(b => b > 0).ToList();
        foreach (JsonElement r in Records(dbfDir, "Booster.json"))
            if (Int(r, "RankedRewardInitialSeason") > 0) seasonPacks.Add((Int(r, "ID"), Int(r, "RankedRewardInitialSeason")));
    }

    /// <summary>The chest for a best star level: ("CARD", BoosterCardSet, n) and ("PACK", booster, n), every milestone added
    /// up from Bronze 10.</summary>
    public List<(string Kind, int Data, int Count)> Chest(int bestStarLevel, int season)
    {
        var items = new List<(string, int, int)>();
        foreach (var (level, bag) in bagOfLevel.OrderBy(x => x.Key))
            if (level <= bestStarLevel) AddBag(bag, season, items, 0);
        return items.GroupBy(i => (i.Item1, i.Item2)).Select(g => (g.Key.Item1, g.Key.Item2, g.Sum(x => x.Item3))).ToList();
    }

    private void AddBag(int bag, int season, List<(string, int, int)> items, int depth)
    {
        if (depth > 4 || !bags.TryGetValue(bag, out var rows)) return;
        foreach (var (reward, count, data) in rows)
            switch (reward)
            {
                case "RANDOM_CARD": items.Add(("CARD", data, Math.Max(1, count))); break;
                case "RANKED_SEASON_REWARD_PACK":
                    int booster = seasonPacks.Where(p => p.Season <= season).OrderBy(p => p.Season).LastOrDefault().Booster;
                    if (booster > 0) items.Add(("PACK", booster, Math.Max(1, count)));
                    break;
                case "REWARD_CHEST_CONTENTS":
                    foreach (int inner in contents.GetValueOrDefault(data) ?? new List<int>()) AddBag(inner, season, items, depth + 1);
                    break;
            }
    }

    private static List<JsonElement> Records(string dir, string file)
    {
        string path = Path.Combine(dir, file);
        if (!File.Exists(path)) return new List<JsonElement>();
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        return doc.RootElement.GetProperty("records").EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static int Int(JsonElement r, string name) =>
        r.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? int.TryParse(v.GetString(), out int i) ? i : 0 : v.GetInt32() : 0;
}
