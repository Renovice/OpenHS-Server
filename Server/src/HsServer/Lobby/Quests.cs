using System.Text.Json;
using HsServer.Protocol;

namespace HsServer.Lobby;

/// <summary>
/// C4 quests: the client's quest data (Quest.json, QuestPool.json). A quest has a pool, a quota, reward-track XP or a reward
/// list, an event that gates it and a chain; what counts as progress is not in the client data (server-side on live).
/// </summary>
internal sealed class QuestRules
{
    public sealed record Quest(int Id, int Pool, int Quota, int Xp, int RewardList, string Event, int NextInChain, bool CanAbandon,
        string Description, int TrackType);
    public sealed record Pool(int Id, string Type, int Granted, int MaxActive, int TrackType);

    private readonly Dictionary<int, Quest> quests = new();
    private readonly Dictionary<int, Pool> pools = new();
    private readonly EventCalendar calendar;

    public QuestRules(string dbfDir, EventCalendar calendar)
    {
        this.calendar = calendar;
        foreach (JsonElement r in Records(dbfDir, "QuestPool.json"))
            pools[Int(r, "ID")] = new Pool(Int(r, "ID"), Str(r, "QuestPoolType"), Int(r, "NumQuestsGranted"), Int(r, "MaxQuestsActive"), TrackType(r));
        foreach (JsonElement r in Records(dbfDir, "Quest.json"))
            quests[Int(r, "ID")] = new Quest(Int(r, "ID"), Int(r, "QuestPool"), Int(r, "Quota"), Int(r, "RewardTrackXp"), Int(r, "RewardList"),
                Str(r, "Event"), Int(r, "NextInChain"), r.TryGetProperty("CanAbandon", out var a) && a.ValueKind == JsonValueKind.True,
                Str(r, "Description"), TrackType(r));
    }

    public Quest Get(int id) => quests.GetValueOrDefault(id);
    public IEnumerable<Quest> All => quests.Values;

    /// <summary>Is the quest's event on (no event: always)?</summary>
    public bool IsOn(Quest quest) => quest.Event == null || calendar.IsActive(quest.Event);

    /// <summary>Does the quest belong to an event ("UNKNOWN" is the DBF's unset value: none)?</summary>
    public static bool HasEvent(Quest quest) => quest.Event is not (null or "UNKNOWN");

    private List<Quest> chainHeads;
    /// <summary>Event quest chains' first quests: no pool, an event, not another quest's NextInChain, not Battlegrounds /
    /// event-pass track quests (Break the Chains, legendary_quest_36_6).</summary>
    public IReadOnlyList<Quest> ChainHeads => chainHeads ??= quests.Values
        .Where(q => q.Pool == 0 && HasEvent(q) && q.TrackType is not (RewardTrackRules.Battlegrounds or RewardTrackRules.Event)
                    && !quests.Values.Any(o => o.NextInChain == q.Id))
        .ToList();
    public Pool GetPool(int id) => pools.GetValueOrDefault(id);

    /// <summary>The pools every player gets quests from: the Global daily and weekly pools (Battlegrounds / event pools
    /// only when the player already has them from live).</summary>
    public IEnumerable<Pool> StandardPools => pools.Values.Where(p => p.TrackType == RewardTrackRules.Global && p.Granted > 0 && p.Type is "DAILY" or "WEEKLY");

    /// <summary>The quests a pool can grant now: its quests whose event (if any) is on.</summary>
    public IEnumerable<Quest> Grantable(int pool) => quests.Values.Where(q => q.Pool == pool && (q.Event == null || calendar.IsActive(q.Event)));

    /// <summary>A pool's grant period: a day for dailies, a week for weeklies (none for the others).</summary>
    public static TimeSpan Period(Pool pool) => pool?.Type switch { "DAILY" => TimeSpan.FromDays(1), "WEEKLY" => TimeSpan.FromDays(7), _ => TimeSpan.Zero };

    private static int TrackType(JsonElement r) => Str(r, "RewardTrackType") switch
    {
        "GLOBAL" => RewardTrackRules.Global, "BATTLEGROUNDS" => RewardTrackRules.Battlegrounds, "EVENT" => RewardTrackRules.Event, _ => 0,
    };

    private static List<JsonElement> Records(string dbfDir, string file)
    {
        string path = Path.Combine(dbfDir, file);
        if (!File.Exists(path)) return new List<JsonElement>();
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        return doc.RootElement.GetProperty("records").EnumerateArray().Select(r => r.Clone()).ToList();
    }

    private static int Int(JsonElement r, string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
    private static string Str(JsonElement r, string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>A player's quests (C4, collection.json): each quest's status and progress, and each pool's next grant time,
/// rerolls left and banked quests. Null until the recorded login's are adopted.</summary>
internal sealed class QuestBook
{
    public Dictionary<int, PlayerQuest> Quests { get; set; } = new();
    public Dictionary<int, PlayerQuestPool> Pools { get; set; } = new();
    // live's login lists are adopted once each
    public bool QuestsAdopted { get; set; }
    public bool PoolsAdopted { get; set; }
}

internal sealed class PlayerQuest
{
    public int Status { get; set; }
    public int Progress { get; set; }
    /// <summary>What a reward list gave (REWARD_GRANTED, for the client's popup): [item id, card, premium, amount].</summary>
    public List<int[]> Outputs { get; set; }
}

internal sealed class PlayerQuestPool
{
    public DateTime NextGrant { get; set; }
    public int Rerolls { get; set; }
    public int Banked { get; set; }
}

/// <summary>
/// C4 quests (wire: RESEARCH/wire-conventions.md "Lobby: quests", live-20261002-progression): the login lists (601 quests,
/// 602 pools), new quests when a pool's time comes (the client asks with CheckForNewQuests 605; wiki Quests: a daily each
/// day, up to three weeklies each Monday, at most 3 of each held, a grant into a full pool is lost), reroll (606 → 607 + 602
/// + 601) and ack (604 → NOP).
/// </summary>
internal sealed class Quests(QuestRules rules, QuestConditions conditions = null, Func<DateTime> clock = null)
{
    // QuestManager.QuestStatus
    public const int New = 1, Active = 2, Completed = 3, RewardGranted = 4, RewardAcked = 5, Rerolled = 6, Reset = 7, Abandoned = 8, Expired = 9;

    private readonly Random random = new();
    private DateTime Now => (clock ?? (() => DateTime.UtcNow))();

    public QuestRules Rules => rules;
    public QuestConditions Conditions => conditions;

    /// <summary>
    /// After a game: each held quest (new or active) gains what the game counts for it (<see cref="QuestConditions"/>),
    /// capped at its quota; one that reaches it is REWARD_GRANTED (live goes straight there). Returns the 601 of the
    /// changed quests (null when none) and the completed quests (their XP / rewards are the caller's).
    /// </summary>
    public (PegasusUtil.PlayerQuestStateUpdate Update, List<QuestRules.Quest> Completed) Progress(CollectionStore collection, Game.GameTally tally) =>
        collection.QuestBook(book =>
        {
            var changed = new List<int>();
            var completed = new List<QuestRules.Quest>();
            if (conditions == null || tally == null) return ((PegasusUtil.PlayerQuestStateUpdate)null, completed);
            foreach (var (id, held) in book.Quests)
            {
                if (held.Status is not (New or Active) || rules.Get(id) is not { } quest) continue;
                int gained = conditions.Count(conditions.For(quest), tally);
                if (gained <= 0) continue;
                held.Progress = Math.Min(quest.Quota, held.Progress + gained);
                held.Status = held.Progress >= quest.Quota ? RewardGranted : Active;
                if (held.Status == RewardGranted) completed.Add(quest);
                changed.Add(id);
            }
            return (changed.Count > 0 ? QuestUpdate(book, changed) : null, completed);
        });

    /// <summary>The login's PlayerQuestPoolStateUpdate (602): live's pools adopted the first time (their next grant
    /// counted from the recording's time), then the player's own.</summary>
    public PegasusUtil.PlayerQuestPoolStateUpdate PoolsLogin(CollectionStore collection, PegasusUtil.PlayerQuestPoolStateUpdate recorded, DateTime recordedAt) =>
        collection.QuestBook(book =>
        {
            if (!book.PoolsAdopted && recorded != null)
                foreach (PegasusUtil.PlayerQuestPoolState p in recorded.QuestPool)
                    book.Pools[p.QuestPoolId] = new PlayerQuestPool
                        { NextGrant = recordedAt.AddSeconds(p.SecondsUntilNextGrant), Rerolls = p.RerollAvailableCount, Banked = p.BankedQuestCount };
            book.PoolsAdopted = true;
            EnsureStandardPools(book);
            return PoolUpdate(book, book.Pools.Keys);
        });

    /// <summary>The login's PlayerQuestStateUpdate (601): live's quests adopted the first time, then the player's own
    /// (the ones still to show: new, active, completed, reward granted).</summary>
    public PegasusUtil.PlayerQuestStateUpdate QuestsLogin(CollectionStore collection, PegasusUtil.PlayerQuestStateUpdate recorded) =>
        collection.QuestBook(book =>
        {
            if (!book.QuestsAdopted && recorded != null)
                foreach (PegasusUtil.PlayerQuestState q in recorded.Quest)
                    book.Quests[q.QuestId] = new PlayerQuest { Status = q.Status, Progress = q.Progress };
            book.QuestsAdopted = true;
            return QuestUpdate(book, book.Quests.Where(q => q.Value.Status is New or Active or Completed or RewardGranted).Select(q => q.Key));
        });

    /// <summary>CheckForNewQuests (605): grant the quests of every pool whose time has come (and banked ones that fit);
    /// the changes to push (602 + 601), none when nothing changed.</summary>
    public List<IProtoBuf> Refresh(CollectionStore collection) => collection.QuestBook(book =>
    {
        EnsureStandardPools(book);
        var changedPools = new HashSet<int>();
        var changedQuests = new List<int>();
        DateTime now = Now;
        foreach (var (poolId, pool) in book.Pools)
        {
            QuestRules.Pool rule = rules.GetPool(poolId);
            TimeSpan period = QuestRules.Period(rule);
            if (rule == null || period == TimeSpan.Zero) continue;
            while (pool.NextGrant <= now)
            {
                // wiki Quests: "If a player already has 3 quests when the reset time is reached, the potential new quest
                // will simply be lost." (live's Battlegrounds pool showed banked quests: kept as adopted, never added here)
                for (int i = 0; i < rule.Granted; i++)
                    if (ActiveIn(book, poolId) < rule.MaxActive && Grant(book, poolId, exclude: 0) is int granted) changedQuests.Add(granted);
                // wiki: "The player can replace one Daily quest and one Weekly quest each day": every pool's reroll comes
                // back at the daily reset
                if (rule.Type == "DAILY")
                    foreach (var (otherId, other) in book.Pools)
                        if (other.Rerolls < 1) { other.Rerolls = 1; changedPools.Add(otherId); }
                pool.NextGrant += period;
                changedPools.Add(poolId);
            }
            changedQuests.AddRange(FromBank(book, poolId, pool, rule, changedPools));
        }
        // event quest chains: a chain's first quest while its event is on (Break the Chains → Worship Them)
        foreach (QuestRules.Quest head in rules.ChainHeads)
            if (!book.Quests.ContainsKey(head.Id) && rules.IsOn(head))
            {
                book.Quests[head.Id] = new PlayerQuest { Status = New };
                changedQuests.Add(head.Id);
            }
        // a held quest whose event ended expires (CheckForExpiredQuests)
        foreach (var (id, held) in book.Quests)
            if (held.Status is New or Active && rules.Get(id) is { } q && QuestRules.HasEvent(q) && !rules.IsOn(q))
            {
                held.Status = Expired;
                changedQuests.Add(id);
            }
        return Changes(book, changedPools, changedQuests);
    });

    /// <summary>
    /// The reward lists of completed quests (event chains: Break the Chains' reward list 3615): granted like a reward-track
    /// level (RewardTracks.GrantList), stored on the quest for the client's popup (601 REWARD_GRANTED with
    /// RewardItemOutput). Returns the messages to push: the grants, then the quests' 601. Quests with only XP give none.
    /// </summary>
    public List<IProtoBuf> Rewards(PlayerAccount player, IEnumerable<QuestRules.Quest> completed, RewardTracks tracks)
    {
        var changes = new List<IProtoBuf>();
        var rewarded = new List<int>();
        foreach (QuestRules.Quest quest in completed.Where(q => q.RewardList > 0))
        {
            var granted = tracks?.GrantList(player, quest.RewardList);
            if (granted == null) continue;
            changes.AddRange(granted.Value.Changes);
            player.Collection.QuestBook(book =>
            {
                if (book.Quests.TryGetValue(quest.Id, out PlayerQuest held)) held.Outputs = granted.Value.Outputs;
                return 0;
            });
            rewarded.Add(quest.Id);
        }
        if (rewarded.Count > 0) changes.Add(player.Collection.QuestBook(book => QuestUpdate(book, rewarded)));
        return changes;
    }

    /// <summary>AbandonQuest (620): a quest the player may drop (CanAbandon: event chains) → ABANDONED; its 601.</summary>
    public List<IProtoBuf> Abandon(CollectionStore collection, int questId) => collection.QuestBook(book =>
    {
        if (rules.Get(questId) is not { CanAbandon: true } || !book.Quests.TryGetValue(questId, out PlayerQuest held) || held.Status is not (New or Active))
            return new List<IProtoBuf>();
        held.Status = Abandoned;
        return Changes(book, Array.Empty<int>(), new[] { questId });
    });

    /// <summary>RerollQuest (606): the quest is replaced by another of its pool (one daily and one weekly reroll a day:
    /// wiki Quests) → 607, 602, 601.</summary>
    public List<IProtoBuf> Reroll(CollectionStore collection, int questId) => collection.QuestBook(book =>
    {
        var response = new PegasusUtil.RerollQuestResponse { RerolledQuestId = questId, Success = false };
        QuestRules.Quest quest = rules.Get(questId);
        if (quest == null || !book.Quests.TryGetValue(questId, out PlayerQuest held) || held.Status is not (New or Active) ||
            !book.Pools.TryGetValue(quest.Pool, out PlayerQuestPool pool) || pool.Rerolls <= 0)
            return new List<IProtoBuf> { response };
        int? granted = Grant(book, quest.Pool, exclude: questId);
        if (granted == null) return new List<IProtoBuf> { response };
        held.Status = Rerolled;
        held.Progress = 0;
        pool.Rerolls--;
        response.GrantedQuestId = granted.Value;
        response.Success = true;
        var changes = new List<IProtoBuf> { response };
        changes.AddRange(Changes(book, new[] { quest.Pool }, new[] { questId, granted.Value }));
        return changes;
    });

    /// <summary>AckQuest (604): the player saw the reward → REWARD_ACKED; a banked quest may now fit (changes to push).</summary>
    public List<IProtoBuf> Ack(CollectionStore collection, int questId) => collection.QuestBook(book =>
    {
        if (!book.Quests.TryGetValue(questId, out PlayerQuest held) || held.Status != RewardGranted) return new List<IProtoBuf>();
        held.Status = RewardAcked;
        held.Outputs = null;
        var changedPools = new HashSet<int>();
        QuestRules.Quest quest = rules.Get(questId);
        int pool = quest?.Pool ?? 0;
        var granted = book.Pools.TryGetValue(pool, out PlayerQuestPool p) ? FromBank(book, pool, p, rules.GetPool(pool), changedPools) : new List<int>();
        // an event chain goes on with its next quest
        if (quest?.NextInChain is int next and > 0 && rules.Get(next) is { } nextQuest && !book.Quests.ContainsKey(next) && rules.IsOn(nextQuest))
        {
            book.Quests[next] = new PlayerQuest { Status = New };
            granted.Add(next);
        }
        return Changes(book, changedPools, granted);
    });

    // ---------- admin (Server/ADMIN.md "Quests") ----------

    /// <summary>The reward-track XP grants of completed quests (source 1 = quest, live), Global quests only.</summary>
    public static IEnumerable<(int Amount, int SourceType, int SourceId)> XpGrants(IEnumerable<QuestRules.Quest> completed) =>
        completed.Where(q => q.Xp > 0 && q.TrackType == RewardTrackRules.Global).Select(q => (q.Xp, RewardTracks.FromQuest, q.Id));

    /// <summary>Give the player a quest (NEW, progress 0); the 601 to push, or null for an unknown quest.</summary>
    public PegasusUtil.PlayerQuestStateUpdate Give(CollectionStore collection, int questId) =>
        rules.Get(questId) == null ? null : collection.QuestBook(book =>
        {
            book.Quests[questId] = new PlayerQuest { Status = New, Progress = 0 };
            return QuestUpdate(book, new[] { questId });
        });

    /// <summary>Set a held quest's progress ("5" or "+2"), capped at the quota; reaching it completes the quest
    /// (REWARD_GRANTED). Null update: the player doesn't hold that quest.</summary>
    public (PegasusUtil.PlayerQuestStateUpdate Update, List<QuestRules.Quest> Completed) SetProgress(CollectionStore collection, int questId, int amount, bool add) =>
        collection.QuestBook(book =>
        {
            var completed = new List<QuestRules.Quest>();
            if (rules.Get(questId) is not { } quest || !book.Quests.TryGetValue(questId, out PlayerQuest held) || held.Status is not (New or Active))
                return ((PegasusUtil.PlayerQuestStateUpdate)null, completed);
            held.Progress = Math.Clamp(add ? held.Progress + amount : amount, 0, quest.Quota);
            held.Status = held.Progress >= quest.Quota ? RewardGranted : Active;
            if (held.Status == RewardGranted) completed.Add(quest);
            return (QuestUpdate(book, new[] { questId }), completed);
        });

    /// <summary>Every pool's next grant becomes now, then the grants run (as CheckForNewQuests would).</summary>
    public List<IProtoBuf> GrantNow(CollectionStore collection)
    {
        collection.QuestBook(book =>
        {
            EnsureStandardPools(book);
            foreach (PlayerQuestPool pool in book.Pools.Values)
                if (pool.NextGrant > Now) pool.NextGrant = Now;
            return 0;
        });
        return Refresh(collection);
    }

    /// <summary>Start the quests over: the next login adopts the recorded ones again.</summary>
    public void StartOver(CollectionStore collection) => collection.QuestBook(book =>
    {
        book.Quests.Clear();
        book.Pools.Clear();
        book.QuestsAdopted = false;
        book.PoolsAdopted = false;
        return 0;
    });

    /// <summary>The player's quests (held ones) and pools, as text.</summary>
    public string Describe(CollectionStore collection) => collection.QuestBook(book =>
    {
        var sb = new System.Text.StringBuilder();
        string[] names = { "?", "new", "active", "completed", "reward granted", "reward acked", "rerolled", "reset", "abandoned", "expired" };
        foreach (var (id, q) in book.Quests.Where(q => q.Value.Status is New or Active or Completed or RewardGranted).OrderBy(q => q.Key))
        {
            QuestRules.Quest quest = rules.Get(id);
            sb.AppendLine($"  quest {id,5}  pool {quest?.Pool,3}  {names[Math.Clamp(q.Status, 0, 9)],-14} {q.Progress}/{quest?.Quota}  " +
                          $"{System.Text.RegularExpressions.Regex.Replace(quest?.Description?.Replace("$q", quest.Quota.ToString()) ?? "", @"\s+", " ")}" +
                          (conditions != null && quest != null && conditions.For(quest) == null ? "  (not counted yet)" : ""));
        }
        foreach (var (id, p) in book.Pools.OrderBy(p => p.Key))
        {
            QuestRules.Pool pool = rules.GetPool(id);
            sb.AppendLine($"  pool {id,3} {pool?.Type,-7} next quest {p.NextGrant:yyyy-MM-dd HH:mm} UTC, rerolls {p.Rerolls}, banked {p.Banked}");
        }
        return sb.Length == 0 ? "  no quests" : sb.ToString().TrimEnd();
    });

    /// <summary>Quests whose text contains <paramref name="text"/> (daily / weekly pools first).</summary>
    public IEnumerable<QuestRules.Quest> Find(string text) => rules.All
        .Where(q => q.Description != null && q.Description.Contains(text, StringComparison.OrdinalIgnoreCase))
        .OrderBy(q => q.Pool is 1 or 2 ? 0 : 1).ThenBy(q => q.Id);

    private void EnsureStandardPools(QuestBook book)
    {
        foreach (QuestRules.Pool pool in rules.StandardPools)
            if (!book.Pools.ContainsKey(pool.Id))
                book.Pools[pool.Id] = new PlayerQuestPool { NextGrant = Now, Rerolls = 1 };
    }

    // Active as the client counts it (QuestManager.IsQuestActive): new or active
    private int ActiveIn(QuestBook book, int pool) =>
        book.Quests.Count(q => q.Value.Status is New or Active && rules.Get(q.Key)?.Pool == pool);

    // A random grantable quest of the pool the player doesn't hold now (new or active), as NEW.
    private int? Grant(QuestBook book, int pool, int exclude)
    {
        var candidates = rules.Grantable(pool)
            .Where(q => q.Id != exclude && !(book.Quests.TryGetValue(q.Id, out PlayerQuest h) && h.Status is New or Active)).ToList();
        if (candidates.Count == 0) return null;
        QuestRules.Quest pick = candidates[random.Next(candidates.Count)];
        book.Quests[pick.Id] = new PlayerQuest { Status = New, Progress = 0 };
        return pick.Id;
    }

    private List<int> FromBank(QuestBook book, int poolId, PlayerQuestPool pool, QuestRules.Pool rule, HashSet<int> changedPools)
    {
        var granted = new List<int>();
        while (rule != null && pool.Banked > 0 && ActiveIn(book, poolId) < rule.MaxActive && Grant(book, poolId, exclude: 0) is int id)
        {
            pool.Banked--;
            granted.Add(id);
            changedPools.Add(poolId);
        }
        return granted;
    }

    private List<IProtoBuf> Changes(QuestBook book, IEnumerable<int> pools, IEnumerable<int> quests)
    {
        var changes = new List<IProtoBuf>();
        var poolIds = pools.Distinct().ToList();
        var questIds = quests.Distinct().ToList();
        if (poolIds.Count > 0) changes.Add(PoolUpdate(book, poolIds));
        if (questIds.Count > 0) changes.Add(QuestUpdate(book, questIds));
        return changes;
    }

    private PegasusUtil.PlayerQuestPoolStateUpdate PoolUpdate(QuestBook book, IEnumerable<int> pools)
    {
        var update = new PegasusUtil.PlayerQuestPoolStateUpdate();
        DateTime now = Now;
        foreach (int id in pools.OrderBy(p => p))
        {
            PlayerQuestPool p = book.Pools[id];
            update.QuestPool.Add(new PegasusUtil.PlayerQuestPoolState
            {
                QuestPoolId = id, SecondsUntilNextGrant = (int)Math.Max(0, (p.NextGrant - now).TotalSeconds),
                RerollAvailableCount = p.Rerolls, BankedQuestCount = p.Banked,
            });
        }
        return update;
    }

    private static PegasusUtil.PlayerQuestStateUpdate QuestUpdate(QuestBook book, IEnumerable<int> quests)
    {
        var update = new PegasusUtil.PlayerQuestStateUpdate();
        foreach (int id in quests)
            if (book.Quests.TryGetValue(id, out PlayerQuest q))
            {
                var state = new PegasusUtil.PlayerQuestState { QuestId = id, Status = q.Status, Progress = q.Progress };
                if (q.Status == RewardGranted && q.Outputs != null)
                    foreach (int[] o in q.Outputs)
                        state.RewardItemOutput.Add(new PegasusUtil.RewardItemOutput
                        {
                            RewardItemId = o[0],
                            OutputData = new PegasusUtil.RewardItemOutputData { CardId = o[1], Premium = o[2], Amount = o[3], MercenaryId = 0, ArtVariationId = 0 },
                        });
                update.Quest.Add(state);
            }
        return update;
    }
}
