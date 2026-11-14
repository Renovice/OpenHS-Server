using System.Text.Json;
using PegasusLuckyDraw;
using PegasusShared;

namespace HsServer.Lobby;

/// <summary>
/// D2 Lucky Draw ("Darkmoon Faire Treasure") boxes from the client data: LuckyDrawBox (event, pull licenses in order,
/// rewards), LuckyDrawRewards (reward list, weight, style), AccountLicense (a pull license's license id).
/// </summary>
internal sealed class LuckyDrawRules
{
    public sealed record Reward(int Id, int RewardList, int Weight, string Style);
    /// <summary>A box: its event (DBF event id), pull licenses as AccountLicense ids in pull order, rewards in DBF order.</summary>
    public sealed record Box(int Id, string Name, string Event, IReadOnlyList<int> PullLicenses, IReadOnlyList<Reward> Rewards);

    private readonly Dictionary<int, Box> boxes = new();
    private readonly Dictionary<int, long> licenseOf = new();

    public LuckyDrawRules(string dbfDir)
    {
        if (dbfDir == null || !File.Exists(Path.Combine(dbfDir, "LuckyDrawBox.json"))) return;
        var rewards = Records(dbfDir, "LuckyDrawRewards.json").ToDictionary(r => Int(r, "ID"),
            r => new Reward(Int(r, "ID"), Int(r, "RewardListId"), Int(r, "Weight"), Str(r, "Style")));
        var licenses = Records(dbfDir, "LuckyDrawPullLicenses.json").ToDictionary(r => Int(r, "ID"), r => Int(r, "LuckyDrawPullLicenseId"));
        foreach (JsonElement r in Records(dbfDir, "LuckyDrawBox.json"))
        {
            int id = Int(r, "ID");
            boxes[id] = new Box(id, Str(r, "Name"), Str(r, "Event"),
                Ids(r, "LuckyDrawPullActionLicense").Where(licenses.ContainsKey).Select(l => licenses[l]).ToList(),
                Ids(r, "LuckyDrawRewards").Where(rewards.ContainsKey).Select(x => rewards[x]).ToList());
        }
        foreach (JsonElement r in Records(dbfDir, "AccountLicense.json"))
            licenseOf[Int(r, "ID")] = Int(r, "LicenseId");
    }

    public IReadOnlyCollection<Box> All => boxes.Values;
    public Box Get(int id) => boxes.GetValueOrDefault(id);
    /// <summary>The license an AccountLicense row stands for (what the shop grants and the client checks).</summary>
    public long? LicenseOf(int accountLicense) => licenseOf.TryGetValue(accountLicense, out long l) ? l : null;

    private static IEnumerable<JsonElement> Records(string dir, string file)
    {
        string path = Path.Combine(dir, file);
        if (!File.Exists(path)) return Enumerable.Empty<JsonElement>();
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        return doc.RootElement.GetProperty("records").EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static int Int(JsonElement r, string name) =>
        r.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? int.Parse(v.GetString()!) : v.GetInt32() : 0;

    private static string Str(JsonElement r, string name) => r.TryGetProperty(name, out var v) ? v.ToString() : "";

    private static IEnumerable<int> Ids(JsonElement r, string name) =>
        r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetInt32()) : Enumerable.Empty<int>();
}

/// <summary>A player's Lucky Draw boxes (saved with the collection): rewards won and seen, by box.</summary>
internal sealed class LuckyDrawBook
{
    public Dictionary<int, LuckyDrawBoxState> Boxes { get; set; } = new();
    /// <summary>Live's state from the recorded login was taken over (once).</summary>
    public bool Adopted { get; set; }

    public LuckyDrawBoxState Box(int id) => Boxes.TryGetValue(id, out var s) ? s : Boxes[id] = new LuckyDrawBoxState();
}

internal sealed class LuckyDrawBoxState
{
    /// <summary>Rewards won, in the order they were pulled.</summary>
    public List<int> Awarded { get; set; } = new();
    public List<int> Acknowledged { get; set; } = new();
}

/// <summary>
/// D2 Lucky Draw (FINDINGS 2026-10-02; wiki Darkmoon Faire Treasure). A pull is bought in the shop (a LUCKY_DRAW product
/// = the box's next pull license); the client then asks for the pull (10204) and the server picks the reward: "After an
/// item is pulled, its weight becomes 0", chance = weight / remaining weight, so all 10 come in 10 pulls. A license bought
/// and never used is pulled at login and counted as seen (live, location-20260930). Boxes are listed while their event is
/// on.
/// </summary>
internal sealed class LuckyDraws(LuckyDrawRules rules, EventCalendar calendar, RewardTracks rewards, Random random = null)
{
    private readonly Random random = random ?? Random.Shared;

    public sealed record Result<T>(T Response, List<IProtoBuf> Changes, string Log);

    /// <summary>10200 → 10201: every open box with the player's rewards and pulls (a live recorded state is taken over
    /// first, once); with ReconcileRewards (the first request after login) unused pulls are pulled now.</summary>
    public Result<LuckyDrawBoxStateResponse> State(PlayerAccount player, LuckyDrawBoxStateRequest request, LuckyDrawBoxStateResponse recorded)
    {
        CollectionStore collection = player.Collection;
        var changes = new List<IProtoBuf>();
        var log = new List<string>();
        var response = new LuckyDrawBoxStateResponse { ErrorCode = ErrorCode.ERROR_OK };
        collection.LuckyDrawBook(book =>
        {
            if (!book.Adopted)
            {
                book.Adopted = true;
                foreach (LuckyDrawState live in recorded?.ActiveLuckyDraws ?? new List<LuckyDrawState>())
                    if (rules.Get(live.LuckyDrawBoxId) is { } box) Adopt(collection, book, box, live, changes);
            }
            return 0;
        });
        IEnumerable<LuckyDrawRules.Box> open = request.LuckyDrawBoxId != 0 && !request.CheckEventActive
            ? new[] { rules.Get(request.LuckyDrawBoxId) }.Where(b => b != null)!
            : rules.All.Where(b => calendar?.IsActive(b.Event) == true).OrderBy(b => b.Id);
        foreach (LuckyDrawRules.Box box in open)
        {
            var reconciled = new List<int>();
            if (request.ReconcileRewards)
                while (Pulls(collection, box) > collection.LuckyDrawBook(b => b.Box(box.Id).Awarded.Count) && Pull(player, box, changes) is { } won)
                {
                    collection.LuckyDrawBook(b => { b.Box(box.Id).Acknowledged.Add(won.Id); return 0; });
                    reconciled.Add(won.Id);
                    log.Add($"box {box.Id} unused pull → reward {won.Id}");
                }
            response.ActiveLuckyDraws.Add(StateOf(collection, box, reconciled));
        }
        log.Insert(0, $"{response.ActiveLuckyDraws.Count} boxes [{string.Join(", ", response.ActiveLuckyDraws.Select(s => s.LuckyDrawBoxId))}]");
        return new Result<LuckyDrawBoxStateResponse>(response, changes, string.Join("; ", log));
    }

    /// <summary>10204 → 10205: the player owns an unused pull for the box → a reward picked by weight, granted.</summary>
    public Result<LuckyDrawUsePullResponse> UsePull(PlayerAccount player, int boxId)
    {
        var changes = new List<IProtoBuf>();
        var response = new LuckyDrawUsePullResponse { ErrorCode = ErrorCode.ERROR_GLOBAL_INVALID_INPUT, LuckyDrawBoxId = boxId };
        LuckyDrawRules.Box box = rules.Get(boxId);
        if (box == null) return new(response, changes, $"box {boxId} unknown");
        CollectionStore collection = player.Collection;
        if (Pulls(collection, box) <= collection.LuckyDrawBook(b => b.Box(box.Id).Awarded.Count))
            return new(response, changes, $"box {boxId}: no unused pull");
        var outputs = new List<int[]>();
        if (Pull(player, box, changes, outputs) is not { } won) return new(response, changes, $"box {boxId}: nothing left to win");
        response.ErrorCode = ErrorCode.ERROR_OK;
        response.LuckyDrawRewardId = won.Id;
        response.RewardOutput.AddRange(outputs.Select(Output));
        return new(response, changes, $"box {boxId} pull → reward {won.Id} ({won.Style})");
    }

    /// <summary>10206 → 10207: the player saw the reward.</summary>
    public LuckyDrawAcknowledgeRewardResponse Acknowledge(PlayerAccount player, int boxId, int rewardId)
    {
        bool ok = player.Collection.LuckyDrawBook(book =>
        {
            LuckyDrawBoxState s = book.Box(boxId);
            if (!s.Awarded.Contains(rewardId)) return false;
            if (!s.Acknowledged.Contains(rewardId)) s.Acknowledged.Add(rewardId);
            return true;
        });
        return new LuckyDrawAcknowledgeRewardResponse
        {
            LuckyDrawBoxId = boxId, LuckyDrawRewardId = rewardId, ErrorCode = ok ? ErrorCode.ERROR_OK : ErrorCode.ERROR_GLOBAL_INVALID_INPUT,
        };
    }

    /// <summary>Admin reset: the player's boxes and pull licenses go; the next login takes live's recorded state over
    /// again. Rewards already granted stay in the collection.</summary>
    public void StartOver(CollectionStore collection)
    {
        collection.RevokeLicenses(rules.All.SelectMany(b => b.PullLicenses).Select(rules.LicenseOf).OfType<long>());
        collection.LuckyDrawBook(book => { book.Boxes.Clear(); book.Adopted = false; return 0; });
    }

    // How many of the box's pull licenses the player owns (bought in order: the shop sells the next one).
    private int Pulls(CollectionStore collection, LuckyDrawRules.Box box) =>
        box.PullLicenses.Count(l => rules.LicenseOf(l) is { } license && collection.OwnsLicense(license));

    // One pull: a reward not won yet, by weight; granted (its reward list), recorded.
    private LuckyDrawRules.Reward Pull(PlayerAccount player, LuckyDrawRules.Box box, List<IProtoBuf> changes, List<int[]> outputs = null)
    {
        LuckyDrawRules.Reward won = player.Collection.LuckyDrawBook(book =>
        {
            LuckyDrawBoxState s = book.Box(box.Id);
            var left = box.Rewards.Where(r => r.Weight > 0 && !s.Awarded.Contains(r.Id)).ToList();
            if (left.Count == 0) return null;
            int roll = random.Next(left.Sum(r => r.Weight));
            LuckyDrawRules.Reward pick = left.First(r => (roll -= r.Weight) < 0);
            s.Awarded.Add(pick.Id);
            return pick;
        });
        if (won == null) return null;
        if (rewards?.GrantList(player, won.RewardList) is var (granted, outs, _))
        {
            changes.AddRange(granted);
            outputs?.AddRange(outs);
        }
        return won;
    }

    private LuckyDrawState StateOf(CollectionStore collection, LuckyDrawRules.Box box, List<int> reconciled)
    {
        var (awarded, acknowledged) = collection.LuckyDrawBook(b => (b.Box(box.Id).Awarded.ToList(), b.Box(box.Id).Acknowledged.ToList()));
        int pulls = Pulls(collection, box);
        var state = new LuckyDrawState { LuckyDrawBoxId = box.Id };
        // live: every reward in DBF order with explicit flags; one pull per owned license (PURCHASED, even the free one)
        foreach (LuckyDrawRules.Reward r in box.Rewards)
            state.Rewards.Add(new LuckyDrawReward { LuckyDrawRewardId = r.Id, IsAwarded = awarded.Contains(r.Id), IsAcknowledged = acknowledged.Contains(r.Id) });
        for (int i = 0; i < pulls; i++)
            state.Pulls.Add(new LuckyDrawPull { GrantMethod_ = LuckyDrawPull.GrantMethod.GRANT_METHOD_PURCHASED, IsUsed = i < awarded.Count });
        state.ReconciledRewards.AddRange(reconciled);
        if (pulls < box.PullLicenses.Count) state.NextPullLicenseAsset = box.PullLicenses[pulls];
        return state;
    }

    // Live's box as recorded at login: its rewards won / seen and the licenses for its pulls.
    private void Adopt(CollectionStore collection, LuckyDrawBook book, LuckyDrawRules.Box box, LuckyDrawState live, List<IProtoBuf> changes)
    {
        LuckyDrawBoxState s = book.Box(box.Id);
        foreach (LuckyDrawReward r in live.Rewards)
        {
            if (r.IsAwarded && !s.Awarded.Contains(r.LuckyDrawRewardId)) s.Awarded.Add(r.LuckyDrawRewardId);
            if (r.IsAcknowledged && !s.Acknowledged.Contains(r.LuckyDrawRewardId)) s.Acknowledged.Add(r.LuckyDrawRewardId);
        }
        foreach (int accountLicense in box.PullLicenses.Take(live.Pulls.Count))
            if (rules.LicenseOf(accountLicense) is { } license && collection.GrantLicense(license) is { } notice) changes.Add(notice);
    }

    private static PegasusUtil.RewardItemOutput Output(int[] o) => new()
    {
        RewardItemId = o[0],
        OutputData = new PegasusUtil.RewardItemOutputData { CardId = o[1], Premium = o[2], Amount = o[3], MercenaryId = 0, ArtVariationId = 0 },
    };
}
