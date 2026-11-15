using HsServer.Lobby;
using PegasusShared;
using PegasusUtil;

namespace HsServer.Tests;

/// <summary>Small lobby requests that used to fall back to a GenericResponse: SetOptions 239, AckNotice 213,
/// DeckSetTemplateSource 332.</summary>
public class ClientSettingsTests
{
    [Fact]
    public void Saved_options_come_back_at_login_over_the_recorded_ones()
    {
        var store = new CollectionStore(null);
        Assert.Null(store.ApplyTo((ClientOptions)null));                                     // nothing saved: as recorded
        store.SetOptions(new[] { new ClientOption { Index = 7, AsBool = true }, new ClientOption { Index = 9, AsBool = true } });
        store.SetOptions(new[] { new ClientOption { Index = 9, AsBool = false } });          // merged by index
        var recorded = new ClientOptions();
        recorded.Options.Add(new ClientOption { Index = 7, AsBool = false });
        recorded.Options.Add(new ClientOption { Index = 3, AsBool = true });
        ClientOptions login = store.ApplyTo(recorded);
        Assert.Equal(new[] { 3, 7, 9 }, login.Options.Select(o => o.Index).OrderBy(i => i));
        Assert.True(login.Options.Single(o => o.Index == 7).AsBool);
        Assert.False(login.Options.Single(o => o.Index == 9).AsBool);
    }

    [Fact]
    public void A_dismissed_notice_is_not_sent_again()
    {
        var store = new CollectionStore(null);
        var notices = new ProfileNotices();
        notices.List.Add(new ProfileNotice { Entry = 11 });
        notices.List.Add(new ProfileNotice { Entry = 12 });
        store.AckNotice(11);
        store.ApplyTo(notices);
        Assert.Equal(new long[] { 12 }, notices.List.Select(n => n.Entry));
    }

    [Fact]
    public void A_game_adds_to_the_hero_record_and_the_type_total_like_live()
    {
        // live-20261002-progression: after each Tavern Brawl game the (16, hero) record and the (16, 0) total gain the
        // win or loss, and the whole list is sent again (270)
        var store = new CollectionStore(null);
        var recorded = new PlayerRecords();
        recorded.Records.Add(new PlayerRecord { Type = GameType.GT_VS_AI, Data = 0, Wins = 10, Losses = 4 });
        recorded.Records.Add(new PlayerRecord { Type = GameType.GT_VS_AI, Data = 7, Wins = 3, Losses = 1 });
        Assert.Equal(2, store.RecordsLogin(recorded).Records.Count);
        PlayerRecords after = store.AddResult((int)GameType.GT_VS_AI, 7, won: true);
        Assert.Equal((11, 4), (after.Records.Single(r => r.Data == 0).Wins, after.Records.Single(r => r.Data == 0).Losses));
        Assert.Equal(4, after.Records.Single(r => r.Data == 7).Wins);
        after = store.AddResult((int)GameType.GT_VS_AI, 31, won: false);                     // a new hero
        Assert.Equal(1, after.Records.Single(r => r.Data == 31).Losses);
        Assert.Equal(3, store.RecordsLogin(new PlayerRecords()).Records.Count);              // not adopted again
    }

    [Fact]
    public void The_login_points_at_the_players_unfinished_game()
    {
        // A4: the recorded login's DisconnectedGame is live's; ours names the player's unfinished game on this server (or none)
        string reference = RecordedAccount.FindReferenceSession(
            Path.Combine(TestEnvironment.ProjectRoot, "Hearthstone main game", "BepInEx", "HsRecorder"),
            Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures"));
        var notifications = RecordedAccount.Load(reference).Util.Values.SelectMany(l => l).SelectMany(e => e.Notifications).ToList();
        GameConnectionInfo Login(GameConnectionInfo unfinished)
        {
            foreach (byte[] n in notifications)
            {
                byte[] r = LobbySession.RewriteLoginState(n, new DeckStore(null), null, out _, unfinished: unfinished);
                if (r == null) continue;
                var c = HsServer.Protocol.Bgs.Parse<Blizzard.GameService.Protocol.Notification.V2.Client.NotificationReceivedNotification>(r);
                var m = c.Notifications.FirstOrDefault(x => x.Attribute.Any(a => a.Name == "message_type" && a.Value.IntValue == 714));
                if (m == null) continue;
                byte[] body = m.Attribute.Where(a => a.Name.StartsWith("fragment_")).OrderBy(a => a.Name, StringComparer.Ordinal)
                    .SelectMany(a => a.Value.BlobValue.ToByteArray()).ToArray();
                return HsServer.Protocol.PegasusPackets.Deserialize<SecondaryClientStateResponse>(body).SecondaryState.DisconnectedGame;
            }
            throw new InvalidOperationException("no 714 in the reference login");
        }
        Assert.Null(Login(null));
        var mine = new GameConnectionInfo { Address = "127.0.0.1", Port = 3724, GameHandle = 1001, ClientHandle = 5, AuroraPassword = "AB", Scenario = 260, GameType = GameType.GT_VS_AI };
        GameConnectionInfo sent = Login(mine);
        Assert.Equal((1001, "AB", 260), (sent.GameHandle, sent.AuroraPassword, sent.Scenario));
    }

    [Fact]
    public void A_deck_from_a_template_is_marked_so()
    {
        var decks = new DeckStore(null);
        var state = new SecondaryClientState();
        state.Decks.Add(new DeckInfo { Id = 5, Name = "x", SourceType = DeckSourceType.DECK_SOURCE_TYPE_NORMAL });
        var contents = new DeckContents { DeckId = 5 }; // (Seed keeps decks with cards)
        contents.Cards.Add(new DeckCardData { Def = new PegasusShared.CardDef { Asset = 1 }, Qty = 1 });
        state.DeckContents.Add(contents);
        decks.Seed(state);
        Assert.True(decks.SetTemplateSource(5));
        Assert.Equal(DeckSourceType.DECK_SOURCE_TYPE_TEMPLATE, decks.Get(5).SourceType);
        Assert.False(decks.SetTemplateSource(99));
    }
}
