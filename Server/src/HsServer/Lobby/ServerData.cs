using HsServer.Protocol;

namespace HsServer.Lobby;

/// <summary>
/// The server's state and what it is built from: the reference recording (identity, seeds, event calendar), the client
/// card data, player accounts, crafting rules. One setup for the running server and for `HsServer admin` when the server
/// is stopped (then the admin edits the same files).
/// </summary>
internal sealed class ServerData
{
    public required string Root { get; init; }
    public required string Reference { get; init; }
    public required RecordedAccount Account { get; init; }
    public required string DbfDir { get; init; }
    public required Engine.CardDb Cards { get; init; }
    /// <summary>Cards every player owns: the current Core set (2 copies, legendaries 1).</summary>
    public required IReadOnlyList<(int Dbf, int Count)> GrantedCards { get; init; }
    public required EventCalendar Calendar { get; init; }
    public required CraftingRules Crafting { get; init; }
    public required BoosterRules Boosters { get; init; }
    /// <summary>C3: the shop (recorded pages and products of the reference login).</summary>
    public Shop.ShopCatalog Shop { get; init; }
    /// <summary>C3: prices and contents of what the shop sells (the reference login's BattlePay config).</summary>
    public Shop.ShopOrders Orders { get; init; }
    /// <summary>C4: reward tracks (levels, rewards, XP).</summary>
    public RewardTracks RewardTracks { get; init; }
    /// <summary>Zilliax Deluxe 3000's saved versions.</summary>
    public ZilliaxBuilds Zilliax { get; init; }
    /// <summary>What a deck sideboard may hold (the owner's limits and its recorded subset).</summary>
    public SideboardRules Sideboards { get; init; }
    /// <summary>C4: daily / weekly quests.</summary>
    public Quests Quests { get; init; }
    public LuckyDraws LuckyDraw { get; init; }
    public BlackMarkets BlackMarket { get; init; }
    public RankedLadder Ranked { get; init; }
    /// <summary>The Dalaran Heist run loop (save data driven).</summary>
    public HeistRuns Heist { get; init; }
    public required AccountStore Accounts { get; init; }

    /// <summary>(2026-11-14, user: a new account starts with every mode open) the client's mode unlocks are save-data flags —
    /// PLAYER_FLAGS (671) subkeys HAS_UNLOCKED_TAVERN_BRAWL 1017, ARENA 1018, SOLO_ADVENTURES 1019, BATTLEGROUNDS 1020, MERCENARIES 1021,
    /// DUELS 1022, TAVERN_GUIDE 1035, UNDERGROUND_ARENA 1204 = 1 (GameModeUtils.HasUnlockedMode); FTUE (695) 801 = the Mercenaries
    /// button's first-time animation seen.</summary>
    internal static void UnlockAllModes(SaveDataStore store)
    {
        foreach (long subkey in new long[] { 1017, 1018, 1019, 1020, 1021, 1022, 1035, 1204 })
            store.SetInts(671, subkey, new long[] { 1 });
        store.SetInts(695, 801, new long[] { 1 });
    }

    /// <summary>Null when there is no recorded login to start from. <paramref name="noRecording"/> (2026-11-14, the public server's
    /// start-up): no recording at all — a generic account, its own accounts folder (Server/state/accounts-norecording, the
    /// real accounts untouched); every request the server can't answer itself is logged ("has no recording").</summary>
    public static ServerData Load(string root, string reference = null, bool noRecording = false)
    {
        string recorder = Path.Combine(root, "Hearthstone main game", "BepInEx", "HsRecorder"), captures = Path.Combine(root, "RESEARCH", "captures");
        if (noRecording)
        {
            reference = null;
            recorder = captures = Path.Combine(root, "Server", "state", "no-recordings"); // (never created: nothing found)
        }
        else
        {
            reference ??= RecordedAccount.FindReferenceSession(recorder, captures);
            // (2026-11-15, the public server) no recorded login at all: start without one
            if (reference == null)
            {
                Log.Info("server", "no recorded login found: starting with fresh accounts (as --no-recording)");
                return Load(root, null, noRecording: true);
            }
        }
        RecordedAccount account = noRecording ? new RecordedAccount() : RecordedAccount.Load(reference);

        // A new player's save data starts from the recordings: every recorded GameSaveDataResponse in chronological order
        // (a short session may only touch one key), so the newest value of each key wins. Then it's theirs (persisted).
        HeistRuns heist = null;
        void SeedSaveData(SaveDataStore store)
        {
            foreach (string session in Recording.FindSessions(recorder, captures))
                store.Seed(Recording.Read(session).Where(p => p.Channel == "util" && p.IsIn && p.Type == 358 && p.Body != null).Select(p => p.Body));
            heist?.Clean(store); // (2026-10-09) the recordings' last Heist run isn't theirs: no run in progress, progress kept
            UnlockAllModes(store);
        }
        // ... and their decks from the reference session's login deck list (714).
        void SeedDecks(DeckStore store)
        {
            byte[] recorded = Recording.Read(reference).LastOrDefault(p => p.Channel == "util" && p.IsIn && p.Type == 714 && p.Body != null)?.Body;
            if (recorded != null) store.Seed(PegasusPackets.Deserialize<PegasusUtil.SecondaryClientStateResponse>(recorded).SecondaryState);
        }
        // B2: the special-event calendar (crafting, nerf refunds, the Core set; later quests and the shop): the reference
        // login's list pinned to its recording clock, plus the admin's changes (Server/state/events.json).
        string dbfDir = Engine.CardDb.FindDefaultDir(root);
        var calendar = new EventCalendar(Path.Combine(dbfDir, "EventMap.json"), EventCalendar.FromRecording(reference),
            Path.Combine(root, "Server", "state", "events.json"));
        // Card data (client DBF): crafting values, the free Core set every player owns (2 copies, legendaries 1): the
        // cards in Core right now (CardSetTiming on the calendar: it rotates with its events, whatever the card id).
        var cards = Engine.CardDb.Load(dbfDir);
        heist = new HeistRuns(dbfDir, Path.Combine(root, "Server", "data", "heist"), cards);
        var sets = Engine.ClientCardSets.Load(dbfDir, calendar.IsActive);
        IReadOnlyList<(int Dbf, int Count)> grantedCards = cards.All
            .Where(c => sets.IsCurrentCore(c.DbfId) && c.Tag(Engine.GAME_TAG.COLLECTIBLE) == 1 && c.Type != Engine.TAG_CARDTYPE.HERO)
            .Select(c => (c.DbfId, c.Tag(Engine.GAME_TAG.RARITY) == (int)Engine.TAG_RARITY.LEGENDARY ? 1 : 2)).ToList();
        var crafting = new CraftingRules(dbfDir, cards, calendar);
        // B2: a new player's collection, dust and gold start as the reference session's (713 collection, 714 currencies).
        void SeedCollection(CollectionStore store)
        {
            var packets = Recording.Read(reference).Where(p => p.Channel == "util" && p.IsIn && p.Body != null).ToList();
            byte[] c713 = packets.LastOrDefault(p => p.Type == 713)?.Body, c714 = packets.LastOrDefault(p => p.Type == 714)?.Body;
            store.Seed(c713 == null ? null : PegasusPackets.Deserialize<PegasusUtil.CollectionClientStateResponse>(c713).Collection,
                c714 == null ? null : PegasusPackets.Deserialize<PegasusUtil.SecondaryClientStateResponse>(c714).SecondaryState?.GameCurrencyStates);
        }
        // Player accounts (B1): Server/state/accounts/<name>/ — the old Server/state/decks.bin is copied into "default".
        var accounts = new AccountStore(Path.Combine(root, "Server", "state", noRecording ? "accounts-norecording" : "accounts"), account, SeedSaveData, SeedDecks,
            legacyDecks: noRecording ? null : Path.Combine(root, "Server", "state", "decks.bin")) { SeedCollection = SeedCollection, Grants = grantedCards };

        var shop = HsServer.Shop.ShopCatalog.FromRecording(reference);
        var boosters = new BoosterRules(dbfDir, cards, crafting, calendar);
        // "from the past" in the rules engine (False Disciple, Toki's cards): the sets out of Standard on this calendar
        SabberStoneCore.Loader.ReprintCards.IsStandardSetNow = boosters.IsStandardSet;
        var orders = HsServer.Shop.ShopOrders.FromRecording(reference, dbfDir, new HsServer.Shop.RandomCards(dbfDir, cards, crafting, boosters.IsStandardSet),
            shop.FreeProducts());
        var rankedRules = new RankedRules(dbfDir);
        var rewardTracks = new RewardTracks(new RewardTrackRules(dbfDir, calendar), orders, () => shop.RecordedBalances.GetValueOrDefault("XSH"));
        return new ServerData
        {
            Root = root, Reference = reference, Account = account, DbfDir = dbfDir, Cards = cards, GrantedCards = grantedCards,
            Calendar = calendar, Crafting = crafting, Accounts = accounts, Boosters = boosters,
            Shop = shop, Orders = orders, Zilliax = new ZilliaxBuilds(cards, Recording.FindSessions(recorder, captures)),
            Sideboards = new SideboardRules(cards, reference == null ? null : new[] { reference }),
            RewardTracks = rewardTracks,
            Heist = heist,
            LuckyDraw = new LuckyDraws(new LuckyDrawRules(dbfDir), calendar, rewardTracks),
            Ranked = new RankedLadder(rankedRules, null, new RankedLeaderboard(Path.Combine(root, "Server", "state", "ranked-leaderboard.json")),
                new RankedSeasonRewards(dbfDir, rankedRules.LeagueId)),
            BlackMarket = new BlackMarkets(new BlackMarketRules(dbfDir, Path.Combine(root, "Server", "data", "black-market-start-prices.tsv")), calendar, rewardTracks, startingItems: BlackMarkets.LatestRecordedItems(
                Recording.FindSessions(captures).Where(s => !s.Contains("local-test", StringComparison.OrdinalIgnoreCase)))),
            Quests = new Quests(new QuestRules(dbfDir, calendar), new QuestConditions(Path.Combine(root, "Server", "data", "quest-conditions.tsv"), cards, dbfDir)),
        };
    }
}
