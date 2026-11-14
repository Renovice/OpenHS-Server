using System.Text.Json;
using HsServer.Protocol;

namespace HsServer.Lobby;

/// <summary>
/// The reward track (roadmap C4) as the client data describes it: RewardTrack (type, season, event, the Tavern Pass and
/// premium licenses), RewardTrackLevel (XP needed per level, free / paid / paid-premium reward lists), RewardList and
/// RewardItem. Which track is current follows the event calendar. The XP rule itself isn't in the client (its
/// XpPerTime tables carry only a track id): see <see cref="GameXp"/>.
/// </summary>
internal sealed class RewardTrackRules
{
    // Global.RewardTrackType and RewardTrackPaidType as the client numbers them
    public const int Global = 1, Battlegrounds = 2, Event = 7, Apprentice = 8;
    public const int Free = 0, Paid = 1, PaidPremium = 2;

    public sealed record Level(int Number, int XpNeeded, int[] Lists);
    public sealed record Track(int Id, int Type, int Season, string Event, long? PaidLicense, long? PremiumLicense, SortedDictionary<int, Level> Levels)
    {
        public int MaxLevel => Levels.Count == 0 ? 1 : Levels.Keys.Max();
    }
    public sealed record Item(int Id, string Type, int Quantity, int Premium, int Card, int Booster, int CardBack, int Coin,
        int RandomSet, int BgHeroSkin, int BgGuideSkin, int BgBoard, int BgFinisher, int BgEmote);
    public sealed record List(int Id, bool ChooseOne, bool Random, int Fallback, IReadOnlyList<Item> Items);

    private readonly Dictionary<int, Track> tracks = new();
    private readonly Dictionary<int, List> lists = new();
    private readonly EventCalendar calendar;

    public RewardTrackRules(string dbfDir, EventCalendar calendar)
    {
        this.calendar = calendar;
        var licenseOf = Records(dbfDir, "AccountLicense.json").ToDictionary(r => Int(r, "ID"), r => (long)Int(r, "LicenseId"));
        var premiumTier = Records(dbfDir, "PaidPremiumTier.json").ToDictionary(r => Int(r, "ID"), r => Int(r, "AccountLicenseRecord"));
        var levels = Records(dbfDir, "RewardTrackLevel.json").GroupBy(r => Int(r, "RewardTrackId"))
            .ToDictionary(g => g.Key, g => new SortedDictionary<int, Level>(g.ToDictionary(r => Int(r, "Level"),
                r => new Level(Int(r, "Level"), Int(r, "XpNeeded"), new[] { Int(r, "FreeRewardList"), Int(r, "PaidRewardList"), Int(r, "PaidPremiumRewardList") }))));
        foreach (JsonElement r in Records(dbfDir, "RewardTrack.json"))
        {
            int id = Int(r, "ID");
            int type = Str(r, "RewardTrackType") switch { "GLOBAL" => Global, "BATTLEGROUNDS" => Battlegrounds, "EVENT" => Event, "APPRENTICE" => Apprentice, _ => 0 };
            long? paid = licenseOf.TryGetValue(Int(r, "AccountLicenseRecord"), out long p) ? p : null;
            long? premium = premiumTier.TryGetValue(Int(r, "PaidPremiumTier"), out int tierLicense) && licenseOf.TryGetValue(tierLicense, out long pp) ? pp : null;
            tracks[id] = new Track(id, type, Int(r, "Season"), Str(r, "Event"), paid, premium, levels.GetValueOrDefault(id) ?? new SortedDictionary<int, Level>());
        }
        var items = Records(dbfDir, "RewardItem.json").GroupBy(r => Int(r, "RewardListId")).ToDictionary(g => g.Key, g => g.Select(r => new Item(
            Int(r, "ID"), Str(r, "RewardType"), Int(r, "Quantity"), PremiumOf(Str(r, "CardPremiumLevel")), Int(r, "Card"), Int(r, "Booster"),
            Int(r, "CardBack"), Int(r, "CustomCoin"), Int(r, "RandomCardBoosterCardSet"), Int(r, "BattlegroundsHeroSkinId"),
            Int(r, "BattlegroundsGuideSkinId"), Int(r, "BattlegroundsBoardSkinId"), Int(r, "BattlegroundsFinisherId"), Int(r, "BattlegroundsEmoteId"))).ToList());
        foreach (JsonElement r in Records(dbfDir, "RewardList.json"))
            lists[Int(r, "ID")] = new List(Int(r, "ID"), Bool(r, "ChooseOne"), Bool(r, "Random"), Int(r, "FallbackRewardListRecord"),
                items.GetValueOrDefault(Int(r, "ID")) ?? new List<Item>());
    }

    private static int PremiumOf(string level) => level switch
    {
        "GOLDEN" => CollectionStore.Golden, "DIAMOND" => CollectionStore.Diamond, "SIGNATURE" => CollectionStore.Signature, _ => CollectionStore.Normal,
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
    private static bool Bool(JsonElement r, string name) => r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public Track Get(int id) => tracks.GetValueOrDefault(id);
    public List RewardList(int id) => lists.GetValueOrDefault(id);

    /// <summary>The current track of a type: its event is on, newest season first.</summary>
    public Track Current(int type) => tracks.Values
        .Where(t => t.Type == type && t.Levels.Count > 0 && t.Event != null && calendar.IsActive(t.Event))
        .OrderByDescending(t => t.Season).ThenByDescending(t => t.Id).FirstOrDefault();

    /// <summary>Does the player own this side of the track (free always; paid / paid premium by their license)?</summary>
    public static bool Owns(Track track, int paidType, Func<long, bool> ownsLicense) => paidType switch
    {
        Free => true,
        Paid => track.PaidLicense is { } l && ownsLicense(l),
        PaidPremium => track.PremiumLicense is { } l && ownsLicense(l),
        _ => false,
    };

    /// <summary>The Tavern Pass XP boost, as the client computes it (RewardTrack.CalcRewardTrackXpMult): the best
    /// REWARD_TRACK_XP_BOOST on an owned paid side at or below the player's level, 1 + Quantity/100; 1 without a pass.</summary>
    public double XpMultiplier(Track track, int level, Func<long, bool> ownsLicense)
    {
        double best = 1;
        foreach (int paid in new[] { Paid, PaidPremium })
        {
            if (!Owns(track, paid, ownsLicense)) continue;
            foreach (Level l in track.Levels.Values.Where(l => l.Number <= level))
                foreach (Item item in RewardList(l.Lists[paid])?.Items ?? Array.Empty<Item>())
                    if (item.Type == "REWARD_TRACK_XP_BOOST") best = Math.Max(best, 1 + item.Quantity / 100.0);
        }
        return best;
    }

    /// <summary>
    /// XP for one game (before the Tavern Pass boost). The rule is Blizzard's server's; the wiki (Rewards Track): about
    /// 400 XP per hour in Ranked, 240 in Solo Adventures, 300 in other modes, more for a win, less for a loss, counted up
    /// to 30 minutes. Calibrated on live (live-20261002-progression, Tavern Brawl, no pass): 7.8 min won → 41, 5.0 min
    /// conceded → 18, 3.8 min won → 19, i.e. ×1.05 for a win and ×0.7 for a loss (FINDINGS 2026-10-02); 14 live games
    /// (2026-10-04) agree: Ranked won 7.0-7.4 XP / min, Casual / Brawl won 5.0-5.4, lost 3.4-3.8. Practice gives none (two
    /// live Practice games: no RewardTrackXpNotification at all); a game that doesn't count (conceded early,
    /// GameTally.CountsAsPlayed) gives none either (two live early concedes at 30 Health: none) — the caller checks that.
    /// </summary>
    public static int GameXp(long bnetGameType, TimeSpan duration, bool won, bool practice = false)
    {
        if (practice) return 0;
        // the matchmaking "type" is the client's BnetGameType (GameMgr: BnetAttribute "type" = bnetGameType)
        double perHour = bnetGameType switch
        {
            2 or 30 or 45 or 58 or 63 => 400, // BGT_RANKED_STANDARD / WILD / STANDARD_NEW_PLAYER / CLASSIC / TWIST
            4 or 5 => 240,                    // BGT_VS_AI (practice, solo adventures), BGT_TUTORIAL
            _ => 300,
        };
        double minutes = Math.Min(duration.TotalMinutes, 30);
        return (int)Math.Round(minutes * perHour / 60 * (won ? 1.05 : 0.7));
    }
}

/// <summary>
/// A player's reward tracks (C4), live's way (live-20261002-progression): XP after games and quests →
/// PlayerRewardTrackStateUpdate 614 (level, XP) + RewardTrackXpNotification 617 (the end-of-game XP bar); claiming a level
/// (615) → NOP, a 614 with that level's full state at GRANTED (the client shows the reward popup from its own data), then
/// the grants (gold: VirtualCurrencyTransactionComplete 735); the client acks it after the popup (616) → ACKED.
/// </summary>
internal sealed class RewardTracks(RewardTrackRules rules, Shop.ShopOrders shop, Func<long> recordedGold)
{
    public const int Unclaimed = 0, Granted = 1, Acked = 2;
    // RewardSourceType: why XP was given (the client's end-of-game screen tells games from quests)
    public const int FromQuest = 1, FromGame = 5;

    public RewardTrackRules Rules => rules;

    /// <summary>Give XP on the current track of a type; the messages to push (614, 617), none if there's no track.</summary>
    public List<IProtoBuf> AddXp(CollectionStore collection, int amount, int sourceType, int sourceId = 0, int trackType = RewardTrackRules.Global) =>
        AddXp(collection, new[] { (amount, sourceType, sourceId) }, trackType);

    /// <summary>Several XP grants at once (a game and the quests it completed): one 614 and one 617 with an XpChange each,
    /// as live (live-20261002-progression #22870: the game's and quest 61's in the same 617).</summary>
    public List<IProtoBuf> AddXp(CollectionStore collection, IEnumerable<(int Amount, int SourceType, int SourceId)> grants, int trackType = RewardTrackRules.Global)
    {
        RewardTrackRules.Track track = rules.Current(trackType);
        var given = grants.Where(g => g.Amount > 0).ToList();
        if (track == null || given.Count == 0) return new List<IProtoBuf>();
        return collection.RewardTrack(track.Id, state =>
        {
            var notification = new PegasusUtil.RewardTrackXpNotification();
            foreach (var (amount, sourceType, sourceId) in given)
                notification.XpChange.Add(Gain(track, state, collection, amount, sourceType, sourceId));
            var update = new PegasusUtil.PlayerRewardTrackStateUpdate { RewardTrackId = track.Id };
            update.State.Add(state.ToState(track.Id, withLevels: false));
            return new List<IProtoBuf> { update, notification };
        });
    }

    private PegasusUtil.RewardTrackXpChange Gain(RewardTrackRules.Track track, RewardTrackState state, CollectionStore collection, int amount, int sourceType, int sourceId)
    {
        {
            int prevLevel = Math.Max(1, state.Level), prevXp = state.Xp;
            int gained = (int)Math.Round(amount * rules.XpMultiplier(track, prevLevel, collection.OwnsLicense));
            state.Level = prevLevel;
            state.Xp += gained;
            state.Active = true;
            // level up while there's XP for the next level (the last level keeps its XP at the cap)
            while (state.Level < track.MaxLevel && track.Levels.TryGetValue(state.Level, out var level) && level.XpNeeded > 0 && state.Xp >= level.XpNeeded)
            {
                state.Xp -= level.XpNeeded;
                state.Level++;
            }
            if (state.Level >= track.MaxLevel && track.Levels.TryGetValue(state.Level, out var last))
                state.Xp = Math.Min(state.Xp, Math.Max(0, last.XpNeeded - 1));
            return new PegasusUtil.RewardTrackXpChange
            {
                RewardSourceType = sourceType, RewardSourceId = sourceId, PrevXp = prevXp, CurrXp = state.Xp,
                PrevLevel = prevLevel, CurrLevel = state.Level, RewardTrackType = track.Type,
            };
        }
    }

    public sealed record Claimed(List<IProtoBuf> Changes, string Log);

    /// <summary>Claim one level's reward; null (with the reason in <paramref name="refused"/>) when it can't be claimed.</summary>
    public Claimed Claim(PlayerAccount player, int trackId, int level, int paidType, int chooseOneItem, out string refused)
    {
        refused = null;
        CollectionStore collection = player.Collection;
        RewardTrackRules.Track track = rules.Get(trackId);
        if (track == null || !track.Levels.TryGetValue(level, out var levelRecord)) { refused = $"track {trackId} has no level {level}"; return null; }
        if (paidType is < 0 or > 2 || !RewardTrackRules.Owns(track, paidType, collection.OwnsLicense)) { refused = $"paid type {paidType} not owned"; return null; }
        RewardTrackRules.List list = rules.RewardList(levelRecord.Lists[paidType]);
        if (list == null || list.Items.Count == 0) { refused = "no reward at that level"; return null; }
        var (current, status) = collection.RewardTrack(trackId, s => (s.Level, s.Status(level, paidType)));
        if (level > current) { refused = $"level {level} not reached ({current})"; return null; }
        if (status is Granted or Acked) { refused = "already claimed"; return null; }

        var granted = GrantList(player, list.Id, chooseOneItem);
        if (granted == null) { refused = $"choose-one item {chooseOneItem} is not in list {list.Id}"; return null; }
        var (changes, outputs, log) = granted.Value;
        PegasusUtil.PlayerRewardTrackLevelState levelState = collection.RewardTrack(trackId, s =>
        {
            s.SetStatus(level, paidType, Granted);
            s.Outputs[$"{level}:{paidType}"] = outputs;
            return s.LevelState(level);
        });
        // the level's full state first (the client's reward popup), then what it gave. Only the track id and the level:
        // a set IsActiveRewardTrack / Level / Xp would make the client re-read the track (an "inactive" one = season roll)
        var update = new PegasusUtil.PlayerRewardTrackStateUpdate();
        var trackState = new PegasusUtil.PlayerRewardTrackState { RewardTrackId = trackId };
        trackState.TrackLevel.Add(levelState);
        update.State.Add(trackState);
        changes.Insert(0, update);
        return new Claimed(changes, $"track {trackId} level {level} {(paidType == 0 ? "free" : paidType == 1 ? "paid" : "premium")}: {string.Join(", ", log)}");
    }

    /// <summary>
    /// Grant a reward list (a reward-track level's, a quest's): its items (one picked for a choose-one list, one at random
    /// for a random list; the fallback list when every cosmetic is owned). Returns the messages to push, the outputs for
    /// the client's reward popup ([item id, card, premium, amount]) and a log; null when a choose-one pick isn't in the list.
    /// </summary>
    public (List<IProtoBuf> Changes, List<int[]> Outputs, List<string> Log)? GrantList(PlayerAccount player, int listId, int chooseOneItem = 0)
    {
        CollectionStore collection = player.Collection;
        RewardTrackRules.List list = rules.RewardList(listId);
        var changes = new List<IProtoBuf>();
        var outputs = new List<int[]>();
        var log = new List<string>();
        if (list == null || list.Items.Count == 0) return (changes, outputs, log);
        IReadOnlyList<RewardTrackRules.Item> items = list.Items;
        if (list.ChooseOne) items = list.Items.Where(i => i.Id == chooseOneItem).Take(1).ToList();
        else if (list.Random) items = new[] { list.Items[Random.Shared.Next(list.Items.Count)] };
        if (items.Count == 0) return null;
        // a list whose cosmetics are all owned gives its fallback list instead
        if (list.Fallback > 0 && items.All(i => OwnedAlready(collection, i)) && rules.RewardList(list.Fallback) is { Items.Count: > 0 } fallback)
            items = fallback.Items;
        foreach (RewardTrackRules.Item item in items)
            Grant(collection, player, item, changes, outputs, log);
        return (changes, outputs, log);
    }

    /// <summary>The list a grant would give instead of <paramref name="listId"/>: its fallback when the player owns every
    /// cosmetic in it (GrantList does the same); 0 = the list itself.</summary>
    public int Resolved(CollectionStore collection, int listId) =>
        rules.RewardList(listId) is { Fallback: > 0 } list && list.Items.Count > 0 && list.Items.All(i => OwnedAlready(collection, i))
            && rules.RewardList(list.Fallback) is { Items.Count: > 0 } ? list.Fallback : 0;

    /// <summary>The client saw the reward (616): GRANTED → ACKED.</summary>
    public bool Ack(CollectionStore collection, int trackId, int level, int paidType) =>
        paidType is >= 0 and <= 2 && collection.RewardTrack(trackId, s =>
        {
            if (s.Status(level, paidType) != Granted) return false;
            s.SetStatus(level, paidType, Acked);
            s.Outputs.Remove($"{level}:{paidType}");
            return true;
        });

    private bool OwnedAlready(CollectionStore collection, RewardTrackRules.Item item) => item.Type switch
    {
        "CARD_BACK" => collection.Owns(CollectionStore.Cosmetic.CardBack, item.CardBack),
        "CUSTOM_COIN" => collection.Owns(CollectionStore.Cosmetic.Coin, item.Coin),
        "HERO_SKIN" => collection.Count(item.Card, CollectionStore.Normal) > 0,
        "BATTLEGROUNDS_HERO_SKIN" => collection.Owns(CollectionStore.Cosmetic.BgHeroSkin, item.BgHeroSkin),
        "BATTLEGROUNDS_GUIDE_SKIN" => collection.Owns(CollectionStore.Cosmetic.BgGuideSkin, item.BgGuideSkin),
        "BATTLEGROUNDS_BOARD_SKIN" => collection.Owns(CollectionStore.Cosmetic.BgBoardSkin, item.BgBoard),
        "BATTLEGROUNDS_FINISHER" => collection.Owns(CollectionStore.Cosmetic.BgFinisher, item.BgFinisher),
        "BATTLEGROUNDS_EMOTE" => collection.Owns(CollectionStore.Cosmetic.BgEmote, item.BgEmote),
        _ => false,
    };

    // One reward item → the player's state, the messages to push, and its RewardItemOutput [item, card, premium, amount].
    private void Grant(CollectionStore collection, PlayerAccount player, RewardTrackRules.Item item, List<IProtoBuf> changes, List<int[]> outputs, List<string> log)
    {
        int quantity = Math.Max(1, item.Quantity);
        void Add(IProtoBuf change) { if (change != null) changes.Add(change); }
        switch (item.Type)
        {
            case "GOLD": // live: VirtualCurrencyTransactionComplete (735) XSH; the client re-reads its shop balance
            {
                long gold = collection.ShopBalance("XSH", recordedGold());
                collection.SetBalance(CollectionStore.Currency.Gold, gold + quantity);
                changes.Add(new PegasusUtil.VirtualCurrencyTransactionCompleteResponse
                    { Success = true, ReferenceId = Guid.NewGuid().ToString(), CurrencyCode = "XSH", Amount = quantity });
                outputs.Add(new[] { item.Id, 0, 0, quantity });
                log.Add($"{quantity} gold");
                break;
            }
            case "BOOSTER":
                Add(collection.SetPacks(item.Booster, collection.PackCount(item.Booster) + quantity));
                outputs.Add(new[] { item.Id, 0, 0, quantity });
                log.Add($"pack {item.Booster} x{quantity}");
                break;
            case "CARD":
                Add(collection.Give(item.Card, item.Premium, quantity));
                outputs.Add(new[] { item.Id, item.Card, item.Premium, quantity });
                log.Add($"card {item.Card}/{item.Premium} x{quantity}");
                break;
            case "HERO_SKIN": // a constructed hero skin is the hero card in the collection (as the shop's)
                if (collection.Count(item.Card, CollectionStore.Normal) == 0) Add(collection.Give(item.Card, CollectionStore.Normal, 1));
                outputs.Add(new[] { item.Id, item.Card, 0, 1 });
                log.Add($"hero skin {item.Card}");
                break;
            case "RANDOM_CARD":
            {
                var picked = shop?.Random?.Pick(item.RandomSet, quantity, dbf => Enumerable.Range(0, 4).Sum(p => collection.Count(dbf, p)), Random.Shared) ?? new List<int>();
                foreach (int dbf in picked)
                {
                    Add(collection.Give(dbf, item.Premium, 1));
                    outputs.Add(new[] { item.Id, dbf, item.Premium, 1 });
                }
                log.Add($"random cards {item.RandomSet}: {string.Join(", ", picked)}");
                break;
            }
            case "CARD_BACK":
                Add(collection.GrantCosmetic(CollectionStore.Cosmetic.CardBack, item.CardBack));
                outputs.Add(new[] { item.Id, 0, 0, 1 });
                log.Add($"card back {item.CardBack}");
                break;
            case "CUSTOM_COIN":
                Add(collection.GrantCosmetic(CollectionStore.Cosmetic.Coin, item.Coin));
                outputs.Add(new[] { item.Id, 0, 0, 1 });
                log.Add($"coin {item.Coin}");
                break;
            case "TAVERN_TICKET":
                Add(collection.SetBalance(CollectionStore.Currency.ArenaTickets, collection.Balance(CollectionStore.Currency.ArenaTickets) + quantity));
                outputs.Add(new[] { item.Id, 0, 0, quantity });
                log.Add($"Arena tickets +{quantity}");
                break;
            case "BATTLEGROUNDS_TOKEN":
                Add(collection.SetBalance(CollectionStore.Currency.BgTokens, collection.Balance(CollectionStore.Currency.BgTokens) + quantity));
                outputs.Add(new[] { item.Id, 0, 0, quantity });
                log.Add($"Battlegrounds tokens +{quantity}");
                break;
            case "BATTLEGROUNDS_HERO_SKIN" or "BATTLEGROUNDS_GUIDE_SKIN" or "BATTLEGROUNDS_BOARD_SKIN" or "BATTLEGROUNDS_FINISHER" or "BATTLEGROUNDS_EMOTE":
            {
                var (kind, id) = item.Type switch
                {
                    "BATTLEGROUNDS_HERO_SKIN" => (CollectionStore.Cosmetic.BgHeroSkin, item.BgHeroSkin),
                    "BATTLEGROUNDS_GUIDE_SKIN" => (CollectionStore.Cosmetic.BgGuideSkin, item.BgGuideSkin),
                    "BATTLEGROUNDS_BOARD_SKIN" => (CollectionStore.Cosmetic.BgBoardSkin, item.BgBoard),
                    "BATTLEGROUNDS_FINISHER" => (CollectionStore.Cosmetic.BgFinisher, item.BgFinisher),
                    _ => (CollectionStore.Cosmetic.BgEmote, item.BgEmote),
                };
                Add(collection.GrantCosmetic(kind, id));
                outputs.Add(new[] { item.Id, 0, 0, 1 });
                log.Add($"{item.Type.ToLowerInvariant()} {id}");
                break;
            }
            case "REWARD_TRACK_XP_BOOST": // nothing to give: owning the pass and the level is the boost
                outputs.Add(new[] { item.Id, 0, 0, item.Quantity });
                log.Add($"XP boost {item.Quantity}%");
                break;
            default: // DECK, MERCENARY, HERO_CLASS, GAME_MODE, LOANER_DECKS, BATTLEGROUNDS_SEASON_BONUS: not granted yet
                outputs.Add(new[] { item.Id, 0, 0, quantity });
                log.Add($"{item.Type} (not granted yet)");
                break;
        }
    }
}
