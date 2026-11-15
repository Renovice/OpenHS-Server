using HsServer.Lobby;
using HsServer.Protocol;
using PegasusShared;
using PegasusUtil;
using Xunit.Abstractions;

namespace HsServer.Tests;

/// <summary>Deck builder flow against the server deck store, plus the login deck list rewrite (714).</summary>
public class DeckStoreTests(ITestOutputHelper output)
{
    // the newest recorded login whose login state has deck contents (a newer one may list decks the client had cached, with no
    // cards: Seed keeps only decks with cards)
    private static string Reference() => Recording.FindSessions(
            Path.Combine(TestEnvironment.ProjectRoot, "Hearthstone main game", "BepInEx", "HsRecorder"),
            Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures"))
        .Reverse().First(s => Recording.Read(s).LastOrDefault(p => p.Channel == "util" && p.IsIn && p.Type == 714 && p.Body != null) is { } login &&
                              PegasusPackets.Deserialize<SecondaryClientStateResponse>(login.Body).SecondaryState?.DeckContents?.Any(c => c.Cards.Count > 0) == true);

    private static SecondaryClientState RecordedState(string reference) =>
        PegasusPackets.Deserialize<SecondaryClientStateResponse>(
            Recording.Read(reference).Last(p => p.Channel == "util" && p.IsIn && p.Type == 714 && p.Body != null).Body).SecondaryState;

    [Fact]
    public void Create_edit_rename_delete_and_reload()
    {
        string file = Path.Combine(Path.GetTempPath(), $"hsserver-decks-{Guid.NewGuid():N}.bin");
        try
        {
            var store = new DeckStore(file);
            store.Seed(RecordedState(Reference()));
            int seeded = store.Count;
            Assert.True(seeded > 0);

            DeckCreated created = store.Create(new CreateDeck { Name = "Custom Mage", Hero = 637, DeckType = DeckType.NORMAL_DECK, SortOrder = -1, RequestId = 7, FormatType = FormatType.FT_STANDARD });
            Assert.Equal(7, created.RequestId);
            long id = created.Info.Id;
            // The client decodes what it gets: the created header must survive a wire round trip.
            var wire = PegasusPackets.Deserialize<DeckCreated>(PegasusPackets.Serialize(created));
            Assert.Equal("Custom Mage", wire.Info.Name);
            Assert.Equal(637, wire.Info.Hero);

            var set = new DeckSetData { Deck = id, Cards = new List<DeckCardData>(), SideboardCards = new List<SideBoardCardData>() };
            set.Cards.Add(new DeckCardData { Def = new CardDef { Asset = 179 }, Qty = 2 }); // Fireball
            set.Cards.Add(new DeckCardData { Def = new CardDef { Asset = 1063 }, Qty = 1 });
            DBAction action = store.SetData(set);
            Assert.Equal(DatabaseAction.DB_A_SET_DECK, action.Action);
            Assert.Equal(DatabaseResult.DB_E_SUCCESS, action.Result);
            Assert.Equal(id, action.MetaData);
            Assert.Equal(new[] { 179, 179, 1063 }, store.CardIds(id));

            Assert.Equal(DatabaseResult.DB_E_UNKNOWN, store.SetData(new DeckSetData { Deck = 42, Cards = new(), SideboardCards = new() }).Result);

            DeckRenamed renamed = store.Rename(new RenameDeck { Deck = id, Name = "Fire Mage" });
            Assert.Equal("Fire Mage", renamed.Name);

            var request = new GetDeckContents { DeckId = new List<long> { id, 42 } };
            GetDeckContentsResponse contents = store.GetContents(request);
            Assert.True(contents.Decks[0].Success);
            Assert.Equal(2, contents.Decks[0].Cards.Count);
            Assert.False(contents.Decks[1].Success);

            // Persisted: a fresh store sees the renamed deck with its cards.
            var reloaded = new DeckStore(file);
            Assert.True(reloaded.Load());
            Assert.Equal(seeded + 1, reloaded.Count);
            Assert.Equal("Fire Mage", reloaded.Get(id).Name);
            Assert.Equal(3, reloaded.CardIds(id).Count);

            Assert.Equal(id, store.Delete(new DeleteDeck { Deck = id }).Deck);
            Assert.Null(store.Get(id));
            Assert.Equal(seeded, store.Count);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Login_state_notification_carries_the_store_decks()
    {
        string reference = Reference();
        RecordedAccount account = RecordedAccount.Load(reference);
        byte[] raw = account.Util.Values.SelectMany(l => l).SelectMany(e => e.Notifications)
            .First(n => LobbySession.RewriteLoginState(n, new DeckStore(null), out _) is var r && !ReferenceEquals(r, n));

        var store = new DeckStore(null);
        store.Seed(RecordedState(reference));
        long id = store.Create(new CreateDeck { Name = "New", Hero = 637, DeckType = DeckType.NORMAL_DECK, SortOrder = 0 }).Info.Id;

        byte[] rewritten = LobbySession.RewriteLoginState(raw, store, out int count);
        Assert.Equal(store.Count, count);

        // Decode exactly like the client: concatenate fragments, check message_size, parse 714.
        var container = Bgs.Parse<Blizzard.GameService.Protocol.Notification.V2.Client.NotificationReceivedNotification>(rewritten);
        var n = container.Notifications.Single(x => x.Attribute.Any(a => a.Name == "message_type" && a.Value.IntValue == 714));
        byte[] payload = n.Attribute.Where(a => a.Name.StartsWith("fragment_")).OrderBy(a => a.Name, StringComparer.Ordinal)
            .SelectMany(a => a.Value.BlobValue.ToByteArray()).ToArray();
        Assert.Equal(payload.Length, n.Attribute.Single(a => a.Name == "message_size").Value.IntValue);
        Assert.DoesNotContain(n.Attribute, a => a.Name == "z");
        SecondaryClientState state = PegasusPackets.Deserialize<SecondaryClientStateResponse>(payload).SecondaryState;
        Assert.Contains(state.Decks, d => d.Id == id && d.Name == "New");
        Assert.Contains(state.DeckContents, c => c.DeckId == id);
        // Everything else in the login state is untouched (e.g. the recorded save data).
        Assert.Equal(RecordedState(reference).GameSaveData.Count, state.GameSaveData.Count);
        output.WriteLine($"714: {state.Decks.Count} decks, {state.DeckContents.Count} contents, {payload.Length} bytes");
    }

    /// <summary>DeckSetData lists only changed cards (new count per premium, 0 = removed): unchanged cards stay.</summary>
    [Fact]
    public void Deck_edits_apply_the_changes_and_keep_unchanged_cards()
    {
        var store = new DeckStore(null);
        long id = store.Create(new CreateDeck { Name = "D", Hero = 7, DeckType = DeckType.NORMAL_DECK, SortOrder = 0 }).Info.Id;
        DeckCardData Card(int asset, int premium, int qty) => new() { Def = new CardDef { Asset = asset, Premium = premium }, Qty = qty };
        // First save: the whole deck (every card is a change against the empty deck).
        store.SetData(new DeckSetData { Deck = id, Cards = new() { Card(1, 0, 2), Card(2, 0, 2), Card(3, 0, 1) }, SideboardCards = new() });
        // Edit: card 2 removed (all premiums 0), card 3 now 1 normal + 1 golden, card 4 added; card 1 not sent.
        store.SetData(new DeckSetData
        {
            Deck = id, SideboardCards = new(),
            Cards = new() { Card(2, 0, 0), Card(2, 1, 0), Card(3, 0, 1), Card(3, 1, 1), Card(4, 0, 2) },
        });
        Assert.Equal(new[] { 1, 1, 3, 3, 4, 4 }, store.CardIds(id).OrderBy(x => x).ToArray());
    }

    /// <summary>The login collection (713) gets the granted cards as normal copies; recorded stacks stay.</summary>
    [Fact]
    public void Login_collection_owns_the_granted_cards()
    {
        const int cathedral = 118074; // CORE_REV_290 Cathedral of Atonement (a Location the recorded account doesn't own)
        var grants = new List<(int, int)> { (cathedral, 2) };
        RecordedAccount account = RecordedAccount.Load(Reference());
        byte[] raw = account.Util.Values.SelectMany(l => l).SelectMany(e => e.Notifications)
            .First(n => LobbySession.RewriteLoginState(n, new DeckStore(null), grants, out _) is var r && !ReferenceEquals(r, n)
                        && Bgs.Parse<Blizzard.GameService.Protocol.Notification.V2.Client.NotificationReceivedNotification>(n)
                            .Notifications.Any(x => x.Attribute.Any(a => a.Name == "message_type" && a.Value.IntValue == 713)));

        byte[] rewritten = LobbySession.RewriteLoginState(raw, new DeckStore(null), grants, out _);
        var container = Bgs.Parse<Blizzard.GameService.Protocol.Notification.V2.Client.NotificationReceivedNotification>(rewritten);
        var n = container.Notifications.Single(x => x.Attribute.Any(a => a.Name == "message_type" && a.Value.IntValue == 713));
        byte[] payload = n.Attribute.Where(a => a.Name.StartsWith("fragment_")).OrderBy(a => a.Name, StringComparer.Ordinal)
            .SelectMany(a => a.Value.BlobValue.ToByteArray()).ToArray();
        Assert.Equal(payload.Length, n.Attribute.Single(a => a.Name == "message_size").Value.IntValue);
        Collection collection = PegasusPackets.Deserialize<CollectionClientStateResponse>(payload).Collection;
        Assert.Contains(collection.Stacks, s => s.CardDef.Asset == cathedral && s.CardDef.Premium == 0 && s.Count == 2);
        output.WriteLine($"713: {collection.Stacks.Count} stacks, {payload.Length} bytes");
    }

    /// <summary>B2: with a server-owned collection, 713 sends the player's own stacks and 714 their dust.</summary>
    [Fact]
    public void Login_state_carries_the_players_own_collection_and_dust()
    {
        var mine = new CollectionStore(null);
        mine.Seed(new Collection
        {
            CollectionVersion = 7,
            Stacks = new List<PegasusShared.CardStack> { new() { CardDef = new PegasusShared.CardDef { Asset = 118074, Premium = 1 }, Count = 1 } },
        }, new GameCurrencyStates { ArcaneDustBalance = 1234, CurrencyVersion = 9999 });
        RecordedAccount account = RecordedAccount.Load(Reference());
        byte[] Payload(byte[] raw, int type)
        {
            var c = Bgs.Parse<Blizzard.GameService.Protocol.Notification.V2.Client.NotificationReceivedNotification>(raw);
            var n = c.Notifications.FirstOrDefault(x => x.Attribute.Any(a => a.Name == "message_type" && a.Value.IntValue == type));
            return n?.Attribute.Where(a => a.Name.StartsWith("fragment_")).OrderBy(a => a.Name, StringComparer.Ordinal)
                .SelectMany(a => a.Value.BlobValue.ToByteArray()).ToArray();
        }
        var notifications = account.Util.Values.SelectMany(l => l).SelectMany(e => e.Notifications).ToList();
        byte[] r713 = notifications.Select(n => LobbySession.RewriteLoginState(n, new DeckStore(null), null, out _, mine)).First(n => n != null && Payload(n, 713) != null);
        Collection collection = PegasusPackets.Deserialize<CollectionClientStateResponse>(Payload(r713, 713)).Collection;
        var stack = Assert.Single(collection.Stacks);
        Assert.Equal((118074, 1, 1), (stack.CardDef.Asset, stack.CardDef.Premium, stack.Count));
        Assert.Equal(7, collection.CollectionVersion);

        byte[] r714 = notifications.Select(n => LobbySession.RewriteLoginState(n, new DeckStore(null), null, out _, mine)).First(n => n != null && Payload(n, 714) != null);
        var currency = PegasusPackets.Deserialize<SecondaryClientStateResponse>(Payload(r714, 714)).SecondaryState.GameCurrencyStates;
        Assert.Equal(1234, currency.ArcaneDustBalance);
        Assert.Equal(9999, currency.CurrencyVersion);
    }

    [Fact]
    public void Login_state_never_replays_the_recorded_license_or_battlegrounds_lists()
    {
        // The live login pushes ACCOUNT_LICENSES (325) and the Battlegrounds lists as notifications after the 327 batch;
        // replayed after the server's own answers, live's list replaced the player's purchased licenses (sequence
        // bundles went back to rank 1 after a restart). With a player collection they are never sent; without one they stay.
        string live = Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures", "live-20261002-shop-purchase", "session-20261002-111909.jsonl");
        var notifications = RecordedAccount.Load(live).Util.Values.SelectMany(l => l).SelectMany(e => e.Notifications).ToList();
        static IEnumerable<long> Types(byte[] raw) => raw == null ? Enumerable.Empty<long>()
            : Bgs.Parse<Blizzard.GameService.Protocol.Notification.V2.Client.NotificationReceivedNotification>(raw).Notifications
                .Select(n => n.Attribute.FirstOrDefault(a => a.Name == "message_type")?.Value?.IntValue ?? 0);
        long[] owned = { 325, 621, 626, 637, 642, 649 };
        Assert.Contains(325L, notifications.SelectMany(Types)); // the recording does push it

        var sent = notifications.Select(n => LobbySession.RewriteLoginState(n, new DeckStore(null), null, out _, new CollectionStore(null))).SelectMany(Types).ToList();
        Assert.DoesNotContain(sent, owned.Contains);
        Assert.Contains(714L, sent); // the rest still goes

        var withoutPlayer = notifications.Select(n => LobbySession.RewriteLoginState(n, new DeckStore(null), out _)).SelectMany(Types);
        Assert.Contains(325L, withoutPlayer);
    }

    [Fact]
    public void Login_state_never_sends_the_recorded_live_game_to_rejoin()
    {
        // The reference live login (2026-10-01 16:48) had an unfinished live game (DisconnectedGame: a Blizzard game
        // server address + password); replayed, the client was stuck on "Reconnecting..." trying to reach it.
        // pinned: this live login (2026-10-01 16:48) had an unfinished live game
        string unfinished = Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures", "live-20261001-startofgame", "session-20261001-164832.jsonl");
        RecordedAccount account = RecordedAccount.Load(unfinished);
        int checkedStates = 0;
        foreach (byte[] n in account.Util.Values.SelectMany(l => l).SelectMany(e => e.Notifications))
        {
            byte[] rewritten = LobbySession.RewriteLoginState(n, new DeckStore(null), null, out _, new CollectionStore(null));
            if (rewritten == null) continue;
            foreach (var note in Bgs.Parse<Blizzard.GameService.Protocol.Notification.V2.Client.NotificationReceivedNotification>(rewritten).Notifications)
            {
                long type = note.Attribute.FirstOrDefault(a => a.Name == "message_type")?.Value?.IntValue ?? 0;
                if (type is not (713 or 714)) continue;
                byte[] payload = note.Attribute.Where(a => a.Name.StartsWith("fragment_")).OrderBy(a => a.Name, StringComparer.Ordinal)
                    .SelectMany(a => a.Value.BlobValue.ToByteArray()).ToArray();
                if (note.Attribute.Any(a => a.Name == "z" && a.Value.IntValue == 1))
                {
                    using var inflated = new MemoryStream();
                    using (var zlib = new System.IO.Compression.ZLibStream(new MemoryStream(payload), System.IO.Compression.CompressionMode.Decompress))
                        zlib.CopyTo(inflated);
                    payload = inflated.ToArray();
                }
                SecondaryClientState state = type == 714
                    ? PegasusPackets.Deserialize<SecondaryClientStateResponse>(payload).SecondaryState
                    : PegasusPackets.Deserialize<CollectionClientStateResponse>(payload).SecondaryState;
                if (state == null) continue;
                Assert.Null(state.DisconnectedGame);
                checkedStates++;
            }
        }
        Assert.True(checkedStates > 0);
        Assert.NotNull(RecordedState(unfinished).DisconnectedGame); // the recording itself has it (the test means something)
    }

    [Fact]
    public void Login_state_announces_the_event_calendar_as_of_now()
    {
        string reference = Reference();
        DateTime later = new(2026, 12, 25, 0, 0, 0, DateTimeKind.Utc);
        var calendar = new EventCalendar(null, EventCalendar.FromRecording(reference)) { Clock = () => later };
        RecordedAccount account = RecordedAccount.Load(reference);
        byte[] r714 = account.Util.Values.SelectMany(l => l).SelectMany(e => e.Notifications)
            .Select(n => LobbySession.RewriteLoginState(n, new DeckStore(null), null, out _, null, calendar))
            .Select(n => Bgs.Parse<Blizzard.GameService.Protocol.Notification.V2.Client.NotificationReceivedNotification>(n).Notifications
                .FirstOrDefault(x => x.Attribute.Any(a => a.Name == "message_type" && a.Value.IntValue == 714)))
            .Where(n => n != null)
            .Select(n => n.Attribute.Where(a => a.Name.StartsWith("fragment_")).OrderBy(a => a.Name, StringComparer.Ordinal)
                .SelectMany(a => a.Value.BlobValue.ToByteArray()).ToArray())
            .First();
        var timings = PegasusPackets.Deserialize<SecondaryClientStateResponse>(r714).SecondaryState.SpecialEventTiming;
        Assert.Equal(calendar.Count, timings.Count);
        // live 2026-08-28 .. 2026-12-03: by Christmas it has ended (the recorded seconds would still say "weeks left")
        var refund = timings.Single(t => t.EventName == "dust_refund_36_4_2 notification");
        Assert.True(refund.SecondsToEnd < 0);
        Assert.Equal((long)Math.Floor((calendar.Get(refund.EventName).End!.Value - later).TotalSeconds), refund.SecondsToEnd);
    }
}
