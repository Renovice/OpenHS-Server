using System.Net.WebSockets;
using Blizzard.GameService.Protocol;
using Blizzard.GameService.Protocol.Account.V2;
using Blizzard.GameService.Protocol.Account.V2.Client;
using Blizzard.GameService.Protocol.Authentication.V2.Client;
using Blizzard.GameService.Protocol.Connection.V1;
using Blizzard.GameService.Protocol.GameUtilities.V2.Client;
using Blizzard.GameService.Protocol.Session.V2.Client;
using Google.Protobuf;
using HsServer.Protocol;

namespace HsServer.Lobby;

/// <summary>
/// One client connection to the lobby (Battle.net RPC over WebSocket). Login/session/social calls are answered
/// with minimal synthetic data; Pegasus util calls are served from <see cref="RecordedAccount"/> replay or from
/// server state (save data). Matchmaking is delegated to <see cref="IMatchmaker"/>.
/// </summary>
internal sealed class LobbySession
{
    public const string LocalAuthToken = "LOCAL-DIAGNOSTIC-NO-ACCOUNT";

    private readonly WebSocket socket;
    private readonly LobbyContext context;
    private readonly string id;
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly Dictionary<int, int> utilOccurrences = new();
    private readonly HashSet<UtilExchange> notificationsSent = new();
    private uint notificationToken;
    private bool authenticated;
    private PlayerAccount player;

    /// <summary>The logged-in player (null before logon, or when the server runs without accounts).</summary>
    public PlayerAccount Player => player;
    private ulong AccountId => player?.AccountId ?? context.Account.AccountId;
    private ulong GameAccountId => player?.GameAccountId ?? context.Account.GameAccountId;
    private string BattleTag => player?.BattleTag ?? context.Account.BattleTag;
    private SaveDataStore SaveData => player?.SaveData ?? context.SaveData;
    private DeckStore Decks => player?.Decks ?? context.Decks;

    public LobbySession(WebSocket socket, LobbyContext context, string id)
    {
        this.socket = socket;
        this.context = context;
        this.id = id;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
                message.Write(buffer, 0, result.Count);
                if (message.Length > BgsFrame.MaxBodyBytes) throw new InvalidDataException("oversized WebSocket message");
                if (!result.EndOfMessage) continue;
                BgsFrame frame = BgsFrame.Parse(message.ToArray());
                message.SetLength(0);
                try { await HandleAsync(frame, ct); }
                catch (Exception e) { Log.Error(id, $"handling svc={frame.Header.ServiceHash} m={frame.Header.MethodId}: {e}"); }
            }
        }
        finally { player?.Detach(this); } // admin pushes stop reaching a closed connection
    }

    private async Task HandleAsync(BgsFrame frame, CancellationToken ct)
    {
        Header h = frame.Header;
        if (h.ServiceId == BgsFrame.ResponseServiceId) return; // client acknowledging one of our notifications
        Log.Debug(id, $"rpc svc={h.ServiceHash} m={h.MethodId} tok={h.Token} bytes={frame.Body.Length}");

        switch (h.ServiceHash, h.MethodId)
        {
            case (BgsRoutes.Connection, 1):
                await ReplyAsync(h, new ConnectResponse
                {
                    ServerId = new ProcessId { Label = 1, Epoch = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
                    ClientId = new ProcessId { Label = 2, Epoch = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
                    UseBindlessRpc = true,
                    ConnectedRegion = context.Account.Region,
                    Ciid = "hsserver-" + id,
                    ServerTime = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000),
                });
                return;
            case (BgsRoutes.Connection, _):
                return; // KeepAlive and friends are fire-and-forget

            case (BgsRoutes.Authentication, 1):
                await LogonAsync(h, frame.Decode<LogonRequest>());
                return;
        }

        if (!authenticated)
        {
            Log.Warn(id, $"call before logon: svc={h.ServiceHash} m={h.MethodId}");
            await ReplyAsync(h, new NoData(), status: 3);
            return;
        }

        switch (h.ServiceHash, h.MethodId)
        {
            case (BgsRoutes.Authentication, 3):
                await ReplyAsync(h, new GenerateAuthTokenResponse { AuthToken = ByteString.CopyFromUtf8(LocalAuthToken) });
                break;
            case (BgsRoutes.Account, 101):
                // Deliberately minimal: never replay the recorded account info (it holds personal data).
                await ReplyAsync(h, new GetAccountInfoResponse
                {
                    Info = new AccountInfo
                    {
                        AccountId = AccountId, HomeRegion = context.Account.Region,
                        PreferredRegion = context.Account.Region, Country = "NOR", BattleTag = BattleTag,
                    },
                });
                break;
            case (BgsRoutes.Session, 1):
                await ReplyAsync(h, new CreateSessionResponse
                {
                    SessionId = SessionId(),
                    Variables = new SessionVariables { RecommendedMarkAliveIntervalS = 60, MaxMarkAliveIntervalS = 120 },
                });
                await NotifyAsync(BgsRoutes.SessionListener, 1, new SessionCreatedNotification
                {
                    Status = 0,
                    SessionId = SessionId(),
                    SessionKey = new SessionKey { Key = ByteString.CopyFromUtf8("hsserver-session-key"), ExpirationS = (ulong)DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds() },
                });
                break;
            case (BgsRoutes.Session, _):
            case (BgsRoutes.Presence, _):
            case (BgsRoutes.Friends, 1):
                await ReplyAsync(h, new NoData());
                break;
            case (BgsRoutes.Whisper, 1):
                await ReplyAsync(h, new Blizzard.GameService.Protocol.Whisper.V2.Client.SubscribeResponse());
                break;
            case (BgsRoutes.ChannelMembership, 1):
                await ReplyAsync(h, new Blizzard.GameService.Protocol.ChannelMembership.V2.Client.SubscribeResponse());
                break;
            // Friends lists are other people's data: always empty.
            case (BgsRoutes.Friends, 3):
                await ReplyAsync(h, new Blizzard.GameService.Protocol.Friends.V2.Client.GetSentInvitationsResponse());
                break;
            case (BgsRoutes.Friends, 4):
                await ReplyAsync(h, new Blizzard.GameService.Protocol.Friends.V2.Client.GetReceivedInvitationsResponse());
                break;
            case (BgsRoutes.Friends, 5):
                await ReplyAsync(h, new Blizzard.GameService.Protocol.Friends.V2.Client.GetFriendsResponse());
                break;
            case (BgsRoutes.GameUtilities, 1):
                await UtilAsync(h, frame.Decode<ProcessTaskRequest>());
                break;
            case (BgsRoutes.Matchmaking, _):
                await context.Matchmaker.HandleAsync(this, h, frame, ct);
                break;
            default:
                Log.Warn(id, $"unhandled rpc svc={h.ServiceHash} m={h.MethodId} ({frame.Body.Length} bytes) — left pending");
                break;
        }
    }

    private async Task LogonAsync(Header h, LogonRequest request)
    {
        string token = request.LogonOptions?.AuthToken?.ToStringUtf8();
        string name = context.Accounts != null ? AccountStore.NameFromToken(token) : token == LocalAuthToken ? AccountStore.DefaultName : null;
        if (name == null)
        {
            Log.Warn(id, "logon with an unexpected token rejected (the local bootstrap token, or LOCAL:<name>)");
            await ReplyAsync(h, new NoData(), status: 3);
            return;
        }
        player?.Detach(this);
        player = context.Accounts?.Get(name);
        // a season ended since the last login: its chest (granted before the collection is sent, shown by the notice)
        if (player != null) context.Ranked?.SeasonEnd(player, RankedLadder.Recorded(context.Account), context.Orders?.Random);
        player?.Attach(this);
        authenticated = true;
        await ReplyAsync(h, new NoData());
        var record = new LogonRecord
        {
            AccountId = AccountId, BattleTag = BattleTag, GeoipCountry = "NO",
            SessionKey = ByteString.CopyFromUtf8("hsserver-session-key"),
        };
        record.GameAccount.Add(GameAccount());
        await NotifyAsync(BgsRoutes.AuthenticationListener, 1, new LogonCompleteNotification { ErrorCode = 0, Record = record });
        Log.Info(id, $"logon ok as {BattleTag}");
    }

    // ---------- Pegasus util ----------

    private async Task UtilAsync(Header h, ProcessTaskRequest request)
    {
        var blob = request.Attribute.FirstOrDefault(a => a.Name == "p")?.Value?.BlobValue;
        string system = request.Attribute.FirstOrDefault(a => a.Name == "system")?.Value?.StringValue;
        if (blob == null) { Log.Warn(id, "ProcessTask without payload"); return; }
        var rpc = PegasusPackets.Deserialize<PegasusUtil.RpcMessage>(blob.ToByteArray());
        int type = (int)rpc.RpcHeader.Type;
        byte[] body = rpc.MessageBody ?? Array.Empty<byte>();

        UtilExchange ex = PickRecorded(type, body);
        switch (type)
        {
            // 305 GetAdventureProgress → 306: the recorded wings plus the ones every player is granted (owned: flag 1, not started)
            case 305 when ex?.ResponseType == 306 && ex.ResponseBody != null && context.GrantedWings.Count > 0:
            {
                var progress = PegasusPackets.Deserialize<PegasusUtil.AdventureProgressResponse>(ex.ResponseBody);
                foreach (int wing in context.GrantedWings)
                    if (!progress.List.Any(p => p.WingId == wing))
                        progress.List.Add(new PegasusShared.AdventureProgress { WingId = wing, Progress = 0, Ack = 0, Flags_ = 1 });
                await UtilReplyAsync(h, progress);
                Log.Debug(id, $"util 305 -> 306 (recorded + {context.GrantedWings.Count} granted wings)");
                break;
            }
            // Subscribe (314) → SubscribeResponse (315), built (2026-11-14: the no-recording start-up stopped here — the client's fatal
            // "Playful sprites" error on a GenericResponse). Live's values (live-20261005 seq 57/66): KeepAliveSecs 300,
            // MaxResubscribeAttempts 5, PendingResponseTimeout 40, PendingSubscribeTimeout 15, RequestMaxWaitSecs 120; Route a
            // server-chosen id per subscription.
            case 314:
                await UtilReplyAsync(h, new PegasusUtil.SubscribeResponse
                {
                    Route = (ulong)Interlocked.Increment(ref subscribeRoutes), KeepAliveSecs = 300, MaxResubscribeAttempts = 5,
                    PendingResponseTimeout = 40, PendingSubscribeTimeout = 15, RequestMaxWaitSecs = 120,
                });
                Log.Debug(id, $"util 314 -> 315 (subscribe, system {system})");
                break;
            // No recording (the public server's start-up, 2026-11-14): the login state the recorded login pushed after 711 / 712 —
            // the collection (713) and the account state (714) — is built from the player's own state: an empty message run
            // through the same rewrite as a recorded one (RewriteLoginState fills collection, decks, dust, packs, options,
            // calendar, ranks). Live: 711 / 712 → GenericResponse 326, then the notification.
            case 711 or 712 when ex == null && Decks != null:
            {
                await UtilReplyAsync(h, InProcess(type));
                IProtoBuf state = type == 711
                    ? new PegasusUtil.CollectionClientStateResponse { Collection = new PegasusUtil.Collection() }
                    : new PegasusUtil.SecondaryClientStateResponse { SecondaryState = NewSecondaryState() };
                byte[] raw = Google.Protobuf.MessageExtensions.ToByteArray(UtilNotification(state));
                byte[] payload = RewriteLoginState(raw, Decks, context.GrantedCards, out int deckCount, player?.Collection, context.Calendar, context.Quests,
                    context.Matchmaker.Unfinished(player),
                    player?.Collection != null && context.Ranked != null ? () => context.Ranked.Medal(player, RankedLadder.Recorded(context.Account)) : null, SaveData);
                if (payload != null) await SendRawAsync(NotificationHeader(BgsRoutes.NotificationListener, 1), payload);
                Log.Info(id, $"util {type} -> 326 + {(type == 711 ? 713 : 714)} built from the player's state (no recording; {deckCount} decks)");
                break;
            }
            // UpdateLogin (205) with no recording: GenericResponse 326, then UpdateLoginComplete (307) — live (session-20261101-162527
            // seq 130/134); without the 307 the client stops at "Playful sprites".
            case 205 when ex == null:
                await UtilReplyAsync(h, InProcess(type));
                await NotifyUtilAsync(new PegasusUtil.UpdateLoginComplete());
                Log.Info(id, "util 205 -> 326 + 307 UpdateLoginComplete (no recording)");
                break;
            // no recording: the granted wings alone (owned: flag 1, not started)
            case 305 when ex == null:
            {
                var progress = new PegasusUtil.AdventureProgressResponse();
                foreach (int wing in context.GrantedWings)
                    progress.List.Add(new PegasusShared.AdventureProgress { WingId = wing, Progress = 0, Ack = 0, Flags_ = 1 });
                await UtilReplyAsync(h, progress);
                Log.Debug(id, $"util 305 -> 306 ({context.GrantedWings.Count} granted wings, no recording)");
                break;
            }
            case 357: // GameSaveDataRequest
                var saveRequest = PegasusPackets.Deserialize<PegasusUtil.GameSaveDataRequest>(body);
                await UtilReplyAsync(h, SaveData.Get(saveRequest));
                Log.Debug(id, $"util 357 -> 358 (save data store: keys {string.Join(",", saveRequest.KeyIds)})");
                break;
            case 359: // SetGameSaveData
            {
                var write = PegasusPackets.Deserialize<PegasusUtil.SetGameSaveData>(body);
                await UtilReplyAsync(h, SaveData.Set(write));
                // the Dalaran Heist: a loot / treasure pick or a retire is applied at once (the next read shows it, as live)
                context.Heist?.ClientWrote(SaveData, write.Data.Where(d => d.Tuple.Count >= 2).Select(d => (d.Tuple[0].Id, d.Tuple[1].Id)));
                Log.Debug(id, "util 359 -> 360 (save data store)");
                break;
            }
            case 209 or 210 or 211 or 214 or 222 when Decks != null:
                await DeckAsync(h, type, body);
                break;
            // B2: CraftingTransaction → BoughtSoldCard (258), then the change itself as a ClientStateNotification (333)
            case 392 when player?.Collection != null && context.Crafting != null:
            {
                var (reply, change) = player.Collection.Craft(PegasusPackets.Deserialize<PegasusUtil.CraftingTransaction>(body), context.Crafting);
                await UtilReplyAsync(h, reply);
                if (change != null) await NotifyUtilAsync(change);
                Log.Info(id, $"util 392 craft card {reply.Def?.Asset} -> 258 {reply.Result_} x{reply.Amount}, dust {player.Collection.Dust}");
                break;
            }
            // GetCustomizedCardHistoryRequest (707): Zilliax's saved versions → 708, the player's own
            case 707 when player?.Collection != null && context.Zilliax != null:
            {
                var history = player.Collection.ZilliaxHistory(context.Zilliax.Recorded);
                uint maximum = PegasusPackets.Deserialize<PegasusUtil.GetCustomizedCardHistoryRequest>(body).MaximumCount;
                await UtilReplyAsync(h, ZilliaxBuilds.Response(history, maximum));
                Log.Info(id, $"util 707 Zilliax saved versions -> 708 ({Math.Min(history.Count, maximum == 0 ? int.MaxValue : (int)maximum)} of {history.Count})");
                break;
            }
            // D1 Black Market: state (726 → 727), items (731 → 732), buy (740 → 741), haggle (742 → 743); live's recorded
            // state and items are taken over once
            case 726 when player?.Collection != null && context.BlackMarket != null:
            {
                var state = context.BlackMarket.PlayerState(player, LastRecorded<PegasusUtil.BlackMarketPlayerStateResponse>(727),
                    LastRecorded<PegasusUtil.BlackMarketItemListResponse>(732));
                await UtilReplyAsync(h, state);
                Log.Info(id, $"util 726 black market -> 727 {state.Status_} event {state.EventId} day {state.CurrentEventDay}");
                break;
            }
            case 731 when player?.Collection != null && context.BlackMarket != null:
            {
                var items = context.BlackMarket.ItemList(player, LastRecorded<PegasusUtil.BlackMarketItemListResponse>(732));
                await UtilReplyAsync(h, items);
                Log.Info(id, $"util 731 black market items -> 732 {items.Status_}: {string.Join(", ", items.Items.Select(i => $"{i.ItemAssetId}={i.Price}"))}");
                break;
            }
            case 740 when player?.Collection != null && context.BlackMarket != null:
            {
                var buy = context.BlackMarket.Buy(player, PegasusPackets.Deserialize<PegasusUtil.BlackMarketBuyItemRequest>(body));
                foreach (IProtoBuf change in buy.Changes) await NotifyUtilAsync(change);
                await UtilReplyAsync(h, buy.Response);
                Log.Info(id, $"util 740 black market -> 741 {buy.Response.Status_}: {buy.Log}");
                break;
            }
            case 742 when player?.Collection != null && context.BlackMarket != null:
            {
                var haggle = context.BlackMarket.Haggle(player, PegasusPackets.Deserialize<PegasusUtil.BlackMarketHaggleItemRequest>(body));
                foreach (IProtoBuf change in haggle.Changes) await NotifyUtilAsync(change);
                await UtilReplyAsync(h, haggle.Response);
                Log.Info(id, $"util 742 black market -> 743 {haggle.Response.Status_}: {haggle.Log}");
                break;
            }
            case 729: // SetBlackMarketWarModeRequest: no reply exists; war mode is off live (guardian var)
                Log.Info(id, "util 729 black market war mode (off) -> no reply");
                break;
            // D2 Lucky Draw: box state (10200 → 10201), a pull (10204 → 10205), a reward seen (10206 → 10207)
            case 10200 when player?.Collection != null && context.LuckyDraw != null:
            {
                var recorded = LastRecorded<PegasusLuckyDraw.LuckyDrawBoxStateResponse>(10201);
                var state = context.LuckyDraw.State(player, PegasusPackets.Deserialize<PegasusLuckyDraw.LuckyDrawBoxStateRequest>(body), recorded);
                foreach (IProtoBuf change in state.Changes) await NotifyUtilAsync(change);
                await UtilReplyAsync(h, state.Response);
                Log.Info(id, $"util 10200 lucky draw -> 10201: {state.Log}");
                break;
            }
            case 10204 when player?.Collection != null && context.LuckyDraw != null:
            {
                var pull = context.LuckyDraw.UsePull(player, PegasusPackets.Deserialize<PegasusLuckyDraw.LuckyDrawUsePullRequest>(body).LuckyDrawBoxId);
                foreach (IProtoBuf change in pull.Changes) await NotifyUtilAsync(change);
                await UtilReplyAsync(h, pull.Response);
                Log.Info(id, $"util 10204 lucky draw -> 10205: {pull.Log}");
                break;
            }
            case 10206 when player?.Collection != null && context.LuckyDraw != null:
            {
                var ack = PegasusPackets.Deserialize<PegasusLuckyDraw.LuckyDrawAcknowledgeRewardRequest>(body);
                var reply = context.LuckyDraw.Acknowledge(player, ack.LuckyDrawBoxId, ack.LuckyDrawRewardId);
                await UtilReplyAsync(h, reply);
                Log.Info(id, $"util 10206 lucky draw seen box {ack.LuckyDrawBoxId} reward {ack.LuckyDrawRewardId} -> 10207 {reply.ErrorCode}");
                break;
            }
            // ValidateAchieve (284): the client asks the server to confirm an old-style achievement it thinks is done (e.g.
            // after cards were gained). AchieveManager stays "not ready" until every one is answered (285), and the end-of-
            // game screen waits for it (no victory screen, no XP bar). Not validated: the old achievements aren't ours yet.
            case 284:
            {
                var validate = PegasusPackets.Deserialize<PegasusUtil.ValidateAchieve>(body);
                await UtilReplyAsync(h, new PegasusUtil.ValidateAchieveResponse { Achieve = validate.Achieve, Success = false });
                Log.Info(id, $"util 284 validate achieve {validate.Achieve} -> 285 (not validated)");
                break;
            }
            // C4: ClaimRewardTrackReward (615) → NOP, then the level at GRANTED (the client's reward popup) and the grants
            case 615 when player?.Collection != null && context.RewardTracks != null:
            {
                var claim = PegasusPackets.Deserialize<PegasusUtil.ClaimRewardTrackReward>(body);
                int paid = claim.HasPaidType ? (int)claim.PaidType - 1 : claim.ForPaidTrack ? 1 : 0;
                await UtilReplyAsync(h, new PegasusUtil.NOP());
                var claimed = context.RewardTracks.Claim(player, claim.RewardTrackId, claim.Level, paid, claim.ChooseOneRewardItemId, out string refused);
                if (claimed == null) { Log.Warn(id, $"util 615 claim track {claim.RewardTrackId} level {claim.Level} refused: {refused}"); break; }
                foreach (IProtoBuf change in claimed.Changes) await NotifyUtilAsync(change);
                Log.Info(id, $"util 615 claim {claimed.Log}");
                break;
            }
            // C4: AckRewardTrackReward (616): the player saw the reward → ACKED; NOP
            case 616 when player?.Collection != null && context.RewardTracks != null:
            {
                var ack = PegasusPackets.Deserialize<PegasusUtil.AckRewardTrackReward>(body);
                int paid = ack.HasPaidType ? (int)ack.PaidType - 1 : ack.ForPaidTrack ? 1 : 0;
                bool acked = context.RewardTracks.Ack(player.Collection, ack.RewardTrackId, ack.Level, paid);
                await UtilReplyAsync(h, new PegasusUtil.NOP());
                Log.Info(id, $"util 616 ack track {ack.RewardTrackId} level {ack.Level} paid type {paid}{(acked ? "" : " (nothing granted there)")}");
                break;
            }
            // SetOptions (239): the client saves its settings on the server (sent back in the login 714); live answers NOP
            case 239 when player?.Collection != null:
                player.Collection.SetOptions(PegasusPackets.Deserialize<PegasusUtil.SetOptions>(body).Options);
                await UtilReplyAsync(h, new PegasusUtil.NOP());
                Log.Debug(id, "util 239 client options saved");
                break;
            // AckNotice (213): a notice the player dismissed isn't sent again
            case 213 when player?.Collection != null:
            {
                long entry = PegasusPackets.Deserialize<PegasusUtil.AckNotice>(body).Entry;
                player.Collection.AckNotice(entry);
                await UtilReplyAsync(h, new PegasusUtil.NOP());
                Log.Info(id, $"util 213 notice {entry} dismissed");
                break;
            }
            // DeckSetTemplateSource (332): the deck came from a premade template
            case 332 when Decks != null:
            {
                var source = PegasusPackets.Deserialize<PegasusUtil.DeckSetTemplateSource>(body);
                bool known = Decks.SetTemplateSource(source.Deck);
                await UtilReplyAsync(h, new PegasusUtil.NOP());
                Log.Info(id, $"util 332 deck {source.Deck} from template {source.TemplateId}{(known ? "" : " (unknown deck)")}");
                break;
            }
            // Unsubscribe (329): the client is shutting down (one per system); nothing to keep
            case 329:
                await UtilReplyAsync(h, new PegasusUtil.NOP());
                Log.Debug(id, $"util 329 unsubscribe ({system})");
                break;
            // C4 quests (wire-conventions "Lobby: quests"). AckQuest (604): REWARD_ACKED → NOP (live); a banked quest may now fit
            case 604 when player?.Collection != null && context.Quests != null:
            {
                int questId = PegasusPackets.Deserialize<PegasusUtil.AckQuest>(body).QuestId;
                await UtilReplyAsync(h, new PegasusUtil.NOP());
                foreach (IProtoBuf change in context.Quests.Ack(player.Collection, questId)) await NotifyUtilAsync(change);
                Log.Info(id, $"util 604 ack quest {questId}");
                break;
            }
            // CheckForNewQuests (605): the client's timer reached a pool's next grant → new quests (602 + 601), NOP
            case 605 when player?.Collection != null && context.Quests != null:
            {
                await UtilReplyAsync(h, new PegasusUtil.NOP());
                var changes = context.Quests.Refresh(player.Collection);
                foreach (IProtoBuf change in changes) await NotifyUtilAsync(change);
                if (changes.Count > 0) Log.Info(id, $"util 605 new quests: {string.Join(", ", changes.OfType<PegasusUtil.PlayerQuestStateUpdate>().SelectMany(u => u.Quest).Select(q => q.QuestId))}");
                break;
            }
            // RerollQuest (606) → RerollQuestResponse (607), then the pool (602) and both quests (601), as live
            case 606 when player?.Collection != null && context.Quests != null:
            {
                int questId = PegasusPackets.Deserialize<PegasusUtil.RerollQuest>(body).QuestId;
                var changes = context.Quests.Reroll(player.Collection, questId);
                await UtilReplyAsync(h, changes[0]);
                foreach (IProtoBuf change in changes.Skip(1)) await NotifyUtilAsync(change);
                var r = (PegasusUtil.RerollQuestResponse)changes[0];
                Log.Info(id, $"util 606 reroll quest {questId} -> 607 {(r.Success ? $"quest {r.GrantedQuestId}" : "refused")}");
                break;
            }
            // CheckForExpiredQuests (654): quests of ended events expire, chains start (Quests.Refresh) → NOP + changes
            case 654 when player?.Collection != null && context.Quests != null:
            {
                await UtilReplyAsync(h, new PegasusUtil.NOP());
                foreach (IProtoBuf change in context.Quests.Refresh(player.Collection)) await NotifyUtilAsync(change);
                break;
            }
            // AbandonQuest (620): an event chain quest the player drops → ABANDONED (601), NOP
            case 620 when player?.Collection != null && context.Quests != null:
            {
                int questId = PegasusPackets.Deserialize<PegasusUtil.AbandonQuest>(body).QuestId;
                await UtilReplyAsync(h, new PegasusUtil.NOP());
                foreach (IProtoBuf change in context.Quests.Abandon(player.Collection, questId)) await NotifyUtilAsync(change);
                Log.Info(id, $"util 620 abandon quest {questId}");
                break;
            }
            // C3: ReportBlizzardCheckoutStatus (366): the client reports a finished purchase; live answers NOP (254)
            case 366:
            {
                var report = PegasusPackets.Deserialize<PegasusUtil.ReportBlizzardCheckoutStatus>(body);
                await UtilReplyAsync(h, new PegasusUtil.NOP());
                Log.Info(id, $"util 366 checkout status {report.Status} product {report.ProductId} x{report.Quantity} {report.Currency} -> 254");
                break;
            }
            // C3: UpdateAccountLicenses (276): "in process" (326), then UpdateAccountLicensesResponse (331) on its own, as live
            // (login #156 → #164 326 … #271 331). The shop waits for "fixed licenses updated" (AccountLicenseMgr) to open.
            case 276:
                await UtilReplyAsync(h, new PegasusUtil.GenericResponse { ResultCode = PegasusUtil.GenericResponse.Result.RESULT_REQUEST_IN_PROCESS, RequestId = type });
                await NotifyUtilAsync(new PegasusUtil.UpdateAccountLicensesResponse { FixedLicenseSuccess = true, ConsumableLicenseSuccess = true });
                Log.Debug(id, "util 276 -> 326 + 331 (licenses updated)");
                break;
            // C2: OpenBooster (225) → BoosterContent (226), then the change as a 333 (cards + BoosterModifications)
            case 225 when player?.Collection != null && context.Boosters != null:
            {
                var open = PegasusPackets.Deserialize<PegasusUtil.OpenBooster>(body);
                var (reply, change, error) = player.Collection.OpenPacks(open.BoosterType, Math.Max(1, open.NumPacks), context.Boosters, Random.Shared);
                if (reply == null)
                {
                    Log.Warn(id, $"util 225 open {open.NumPacks} x pack {open.BoosterType}: {error}");
                    reply = new PegasusUtil.BoosterContent { CollectionVersion = player.Collection.CollectionVersion, PacksOpened = 0, MassPackOpeningEnabled = true };
                }
                await UtilReplyAsync(h, reply);
                if (change != null) await NotifyUtilAsync(change);
                Log.Info(id, $"util 225 open {reply.PacksOpened} x pack {open.BoosterType} -> 226 {reply.List.Count} cards" +
                             (reply.List.Count > 0 ? ": " + string.Join(", ", reply.List.Select(c => c.CardDef.Asset + (c.CardDef.Premium > 0 ? "*" : ""))) : ""));
                break;
            }
            // B2: favorites and "seen" — the player's own (persisted)
            case 291 when player?.Collection != null:
                await UtilReplyAsync(h, player.Collection.SetFavoriteCardBack(PegasusPackets.Deserialize<PegasusUtil.SetFavoriteCardBack>(body)));
                break;
            case 319 when player?.Collection != null:
                await UtilReplyAsync(h, player.Collection.SetFavoriteHero(PegasusPackets.Deserialize<PegasusUtil.SetFavoriteHero>(body)));
                break;
            case 609 when player?.Collection != null:
                await UtilReplyAsync(h, player.Collection.SetFavoriteCoin(PegasusPackets.Deserialize<PegasusUtil.SetFavoriteCosmeticCoin>(body)));
                break;
            case 223 when player?.Collection != null:
                player.Collection.Seen(PegasusPackets.Deserialize<PegasusUtil.AckCardSeen>(body));
                await UtilReplyAsync(h, InProcess(type));
                break;
            // B2: MassDisenchantRequest → MassDisenchantResponse (269) + 333
            case 268 when player?.Collection != null && context.Crafting != null:
            {
                var (reply, change) = player.Collection.MassDisenchant(context.Crafting);
                await UtilReplyAsync(h, reply);
                if (change != null) await NotifyUtilAsync(change);
                Log.Info(id, $"util 268 mass disenchant -> 269 +{reply.Amount} dust");
                break;
            }
            // GetAccountInfo: e.g. MEDAL_INFO after a ranked game (the end screen waits for it). Answered with the
            // recorded packet of that kind when the recording never asked the same.
            case 201 when ex == null && AccountInfo(body) is { } info:
                await UtilReplyAsync(h, info.Reply);
                Log.Info(id, $"util 201 {info.Request} -> {PegasusPackets.IdOf(info.Reply.GetType())} (recorded account data)");
                break;
            // GenericRequestList: a batch of GetAccountInfo requests (the end screen's MEDAL_INFO + HERO_XP after every
            // game). Live answers GenericResponse, then pushes each requested object as a util notification (MedalInfo
            // 232, HeroXP 283). Without the data the end screen waited 5 s and NetCache gave a fatal timeout after 2 min.
            case 327:
                if (ex != null) await SendRawAsync(ResponseHeader(h), ex.RawBgsResponse);
                else await UtilReplyAsync(h, InProcess(type));
                foreach (PegasusUtil.GenericRequest r in PegasusPackets.Deserialize<PegasusUtil.GenericRequestList>(body).Requests)
                {
                    if (r.RequestId == 201 && AccountInfo((PegasusUtil.GetAccountInfo.Request)r.RequestSubId) is { } data)
                    {
                        await NotifyUtilAsync(data.Reply);
                        Log.Debug(id, $"util 327 {data.Request} -> {PegasusPackets.IdOf(data.Reply.GetType())} (recorded account data)");
                    }
                    else if (r.RequestId == 340 && context.Account.SourceSession == null) // GetClientStaticAssets (no recording)
                        await NotifyUtilAsync(new PegasusUtil.ClientStaticAssetsResponse());
                    else
                        Log.Warn(id, $"util 327 request {r.RequestId}:{r.RequestSubId} has no account data to send");
                }
                break;
            default:
                if (ex == null && EmptyAnswer(type) is { } empty)
                {
                    await UtilReplyAsync(h, PegasusPackets.Fill(empty));
                    Log.Info(id, $"util {type} (system {system}) has no recording; answering an empty {empty.GetType().Name}");
                    return;
                }
                if (ex == null)
                {
                    Log.Warn(id, $"util {type} (system {system}) has no recording; answering GenericResponse");
                    await UtilReplyAsync(h, InProcess(type));
                    return;
                }
                await SendRawAsync(ResponseHeader(h), ex.RawBgsResponse);
                Log.Debug(id, $"util {type} -> {ex.ResponseType} (replay #{ex.ResponseSeq})");
                break;
        }
        // Recorded follow-up notifications are attributed by arrival time, so they may belong to requests the server
        // answers from its own state (e.g. the account-data burst after SetGameSaveData): push them regardless.
        if (ex != null && notificationsSent.Add(ex))
            foreach (byte[] notification in ex.Notifications)
            {
                int deckCount = 0;
                byte[] payload = Decks == null ? notification : RewriteLoginState(notification, Decks, context.GrantedCards, out deckCount, player?.Collection, context.Calendar, context.Quests,
                    context.Matchmaker.Unfinished(player),
                    player?.Collection != null && context.Ranked != null ? () => context.Ranked.Medal(player, RankedLadder.Recorded(context.Account)) : null, SaveData);
                if (payload == null) continue; // only lists the server sent itself
                if (!ReferenceEquals(payload, notification)) Log.Info(id, $"714 login state: {deckCount} decks from the deck store");
                await SendRawAsync(NotificationHeader(BgsRoutes.NotificationListener, 1), payload);
            }
    }

    // The response kind of each GetAccountInfo request (as the client's own FakeUtilHandler maps them).
    private (PegasusUtil.GetAccountInfo.Request Request, IProtoBuf Reply)? AccountInfo(byte[] body) =>
        AccountInfo(PegasusPackets.Deserialize<PegasusUtil.GetAccountInfo>(body).Request_);

    private (PegasusUtil.GetAccountInfo.Request Request, IProtoBuf Reply)? AccountInfo(PegasusUtil.GetAccountInfo.Request request)
    {
        // (2026-11-14: with no recorded login at all — the public start-up — an empty one; the client waits for each)
        IProtoBuf Recorded<T>() where T : class, IProtoBuf, new() =>
            context.Account.LastUtil.TryGetValue(PegasusPackets.IdOf<T>(), out byte[] b) ? PegasusPackets.Deserialize<T>(b)
            : context.Account.SourceSession == null ? PegasusPackets.Fill(new T()) : null;
        IProtoBuf reply = request switch
        {
            // the player's ranks (Ranked.cs) — the end screen animates a game by comparing it with the previous one
            PegasusUtil.GetAccountInfo.Request.MEDAL_INFO when player?.Collection != null && context.Ranked != null =>
                context.Ranked.Medal(player, RankedLadder.Recorded(context.Account)),
            // no ranked data recorded: an empty one (no medals) — the end screen waits for it after every game
            PegasusUtil.GetAccountInfo.Request.MEDAL_INFO => Recorded<PegasusUtil.MedalInfo>() ?? new PegasusUtil.MedalInfo(),
            // C4: the player's own win / loss records and class levels (live's adopted once): the end screen asks for them
            // after every game and animates the class bar by comparing the levels with the last HeroXP
            PegasusUtil.GetAccountInfo.Request.PLAYER_RECORD when player?.Collection != null =>
                player.Collection.RecordsLogin(Recorded<PegasusUtil.PlayerRecords>() as PegasusUtil.PlayerRecords),
            PegasusUtil.GetAccountInfo.Request.HERO_XP when player?.Collection != null =>
                player.Collection.ClassXpLogin(Recorded<PegasusUtil.HeroXP>() as PegasusUtil.HeroXP),
            PegasusUtil.GetAccountInfo.Request.PLAYER_RECORD => Recorded<PegasusUtil.PlayerRecords>(),
            // no recording: this month's season (live: SeasonEnd the month's last day, SeasonNumber 157 = November 2026) — the
            // menu waits for it (NetCacheRewardProgress)
            PegasusUtil.GetAccountInfo.Request.REWARD_PROGRESS when context.Account.SourceSession == null => SeasonNow(),
            PegasusUtil.GetAccountInfo.Request.REWARD_PROGRESS => Recorded<PegasusUtil.RewardProgress>(),
            PegasusUtil.GetAccountInfo.Request.HERO_XP => Recorded<PegasusUtil.HeroXP>(),
            PegasusUtil.GetAccountInfo.Request.CARD_BACKS => Recorded<PegasusUtil.CardBacks>(),
            PegasusUtil.GetAccountInfo.Request.FAVORITE_HEROES => Recorded<PegasusUtil.FavoriteHeroesResponse>(),
            PegasusUtil.GetAccountInfo.Request.COINS => Recorded<PegasusUtil.CosmeticCoins>(),
            // C3: the player's account licenses (shop ownership, sequence bundles); never recorded
            PegasusUtil.GetAccountInfo.Request.ACCOUNT_LICENSES when player?.Collection != null =>
                new PegasusUtil.AccountLicensesInfoResponse { List = player.Collection.LicenseInfos() },
            // C3: the player's Battlegrounds cosmetics (never recorded; the defaults are owned without being listed)
            PegasusUtil.GetAccountInfo.Request.BATTLEGROUNDS_SKINS when player?.Collection != null => new PegasusUtil.BattlegroundsHeroSkinsResponse
            {
                OwnedSkins = player.Collection.Owned(CollectionStore.Cosmetic.BgHeroSkin).Select(id => new PegasusUtil.BattlegroundsHeroSkinInfo
                    { HeroSkinId = id, HasSeen = true, BaseHeroCardId = context.Orders?.BgHeroBase(id) ?? 0 }).ToList(),
            },
            PegasusUtil.GetAccountInfo.Request.BATTLEGROUNDS_GUIDE_SKINS when player?.Collection != null => new PegasusUtil.BattlegroundsGuideSkinsResponse
            {
                OwnedSkins = player.Collection.Owned(CollectionStore.Cosmetic.BgGuideSkin).Select(id => new PegasusUtil.BattlegroundsGuideSkinInfo { GuideSkinId = id, HasSeen = true }).ToList(),
            },
            PegasusUtil.GetAccountInfo.Request.BATTLEGROUNDS_BOARD_SKINS when player?.Collection != null => new PegasusUtil.BattlegroundsBoardSkinsResponse
            {
                OwnedSkins = player.Collection.Owned(CollectionStore.Cosmetic.BgBoardSkin).Select(id => new PegasusUtil.BattlegroundsBoardSkinInfo { BoardSkinId = id, HasSeen = true }).ToList(),
            },
            PegasusUtil.GetAccountInfo.Request.BATTLEGROUNDS_FINISHERS when player?.Collection != null => new PegasusUtil.BattlegroundsFinishersResponse
            {
                OwnedSkins = player.Collection.Owned(CollectionStore.Cosmetic.BgFinisher).Select(id => new PegasusUtil.BattlegroundsFinisherInfo { FinisherId = id, HasSeen = true }).ToList(),
            },
            // emotes need a loadout: the 6 default emotes (BattlegroundsEmote IsDefault: ids 1..6)
            PegasusUtil.GetAccountInfo.Request.BATTLEGROUNDS_EMOTES when player?.Collection != null => new PegasusUtil.BattlegroundsEmotesResponse
            {
                OwnedEmotes = player.Collection.Owned(CollectionStore.Cosmetic.BgEmote).Select(id => new PegasusUtil.BattlegroundsEmoteInfo { EmoteId = id, HasSeen = true }).ToList(),
                Loadout = new PegasusUtil.BattlegroundsEmoteLoadout { EmoteSlot0 = 1, EmoteSlot1 = 2, EmoteSlot2 = 3, EmoteSlot3 = 4, EmoteSlot4 = 5, EmoteSlot5 = 6 },
            },
            // (no recording: pets and the SPJ story state — the client's login cache waits for both; a recorded login
            // delivers them in its follow-up notifications)
            PegasusUtil.GetAccountInfo.Request.PETS when context.Account.SourceSession == null => Recorded<PegasusUtil.PetsResponse>(),
            PegasusUtil.GetAccountInfo.Request.SPJ when context.Account.SourceSession == null => Recorded<PegasusUtil.SpjStoryStateResponse>(),
            // the server's feature flags (no recording: Server/data/feature-flags.b64, live's defaults — an empty set turns
            // ShowUserUI and every timer off and the client gives up)
            PegasusUtil.GetAccountInfo.Request.FEATURES when context.Account.SourceSession == null && FeatureFlags(Program.ProjectRoot()) is { } flags => flags,
            PegasusUtil.GetAccountInfo.Request.FEATURES => Recorded<PegasusUtil.GuardianVars>(),
            // a new player with no recorded login: the tutorial done (TutorialProgress 6, as a finished live account) — the
            // client's login cache waits for it, and the tutorial missions aren't served
            PegasusUtil.GetAccountInfo.Request.CAMPAIGN_INFO when context.Account.SourceSession == null =>
                new PegasusUtil.ProfileProgress { Progress = 6, TutorialComplete = true },
            PegasusUtil.GetAccountInfo.Request.CAMPAIGN_INFO => Recorded<PegasusUtil.ProfileProgress>(),
            _ => null,
        };
        if (player?.Collection != null)
            switch (reply) // B2: the player's own favorites
            {
                case PegasusUtil.CardBacks b: player.Collection.ApplyTo(b); break;
                case PegasusUtil.FavoriteHeroesResponse f: player.Collection.ApplyTo(f); break;
                case PegasusUtil.CosmeticCoins c: player.Collection.ApplyTo(c); break;
            }
        return reply == null ? null : (request, reply);
    }

    private static PegasusUtil.RewardProgress SeasonNow()
    {
        DateTime now = DateTime.UtcNow;
        DateTime last = new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month), 23, 59, 59, DateTimeKind.Utc);
        PegasusShared.Date Peg(DateTime d) => new() { Year = d.Year, Month = d.Month, Day = d.Day, Hours = d.Hour, Min = d.Minute, Sec = d.Second };
        return new PegasusUtil.RewardProgress
        {
            SeasonEnd = Peg(last), SeasonNumber = now.Year * 12 + now.Month - 24166, NextQuestCancel = Peg(now),
        };
    }

    private static PegasusUtil.GuardianVars FeatureFlags(string root)
    {
        string file = Path.Combine(root ?? "", "Server", "data", "feature-flags.b64");
        if (!File.Exists(file)) return null;
        string b64 = File.ReadLines(file).FirstOrDefault(l => l.Length > 0 && l[0] != '#');
        return b64 == null ? null : PegasusPackets.Deserialize<PegasusUtil.GuardianVars>(Convert.FromBase64String(b64.Trim()));
    }

    private async Task DeckAsync(Header h, int type, byte[] body)
    {
        DeckStore decks = Decks;
        IProtoBuf response = type switch
        {
            209 => decks.Create(PegasusPackets.Deserialize<PegasusUtil.CreateDeck>(body)),
            210 => decks.Delete(PegasusPackets.Deserialize<PegasusUtil.DeleteDeck>(body)),
            211 => decks.Rename(PegasusPackets.Deserialize<PegasusUtil.RenameDeck>(body)),
            214 => decks.GetContents(PegasusPackets.Deserialize<PegasusUtil.GetDeckContents>(body)),
            222 => decks.SetData(PegasusPackets.Deserialize<PegasusUtil.DeckSetData>(body)),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
        await UtilReplyAsync(h, response);
        // a saved deck's Zilliax becomes the newest of the player's saved versions (707 / 708)
        if (type == 222 && player?.Collection != null && context.Zilliax != null && decks != null
            && PegasusPackets.Deserialize<PegasusUtil.DeckSetData>(body) is { SideboardCards.Count: > 0 } changed)
            foreach (int[] build in context.Zilliax.FromSideboards(decks.SideboardCards(changed.Deck)))
            {
                player.Collection.RememberZilliax(build, context.Zilliax.Recorded);
                Log.Info(id, $"Zilliax build saved: modules {build[0]}, {build[1]}, portrait {build[2]}");
            }
        string detail = response switch
        {
            PegasusUtil.DeckCreated c => $"deck {c.Info.Id} \"{c.Info.Name}\" hero {c.Info.Hero}",
            PegasusUtil.DBAction a => $"deck {a.MetaData}: {a.Result}, {decks.CardIds(a.MetaData).Count} cards",
            PegasusUtil.DeckRenamed r => $"deck {r.Deck} \"{r.Name}\"",
            PegasusUtil.DeckDeleted d => $"deck {d.Deck}",
            PegasusUtil.GetDeckContentsResponse g => $"{g.Decks.Count} deck(s)",
            _ => "",
        };
        Log.Info(id, $"util {type} -> {PegasusPackets.IdOf(response.GetType())} (deck store) {detail}");
    }

    // Recorded notifications carrying account state the server owns are rebuilt from that state:
    // SecondaryClientStateResponse (714) delivers the deck list and deck contents at login.
    internal static byte[] RewriteLoginState(byte[] raw, DeckStore decks, out int deckCount) =>
        RewriteLoginState(raw, decks, null, out deckCount);

    /// <summary>
    /// Also CollectionClientStateResponse (713), the login collection: the <paramref name="grants"/> (card dbf id, copies)
    /// are owned as normal copies at least that many times (the client drops unowned cards from decks it opens).
    /// With a <paramref name="collection"/>, the recorded account lists the server answers itself (licenses 325,
    /// Battlegrounds skins / guides / boards / finishers / emotes 621 626 637 642 649) are dropped: replayed after the
    /// server's own, they replaced the player's purchases with live's (FINDINGS 2026-10-02). Null = nothing left to send.
    /// </summary>
    internal static byte[] RewriteLoginState(byte[] raw, DeckStore decks, IReadOnlyList<(int Dbf, int Count)> grants, out int deckCount,
        CollectionStore collection = null, EventCalendar calendar = null, Quests quests = null, PegasusShared.GameConnectionInfo unfinished = null,
        Func<PegasusUtil.MedalInfo> medal = null, SaveDataStore saveData = null)
    {
        deckCount = 0;
        var container = Protocol.Bgs.Parse<Blizzard.GameService.Protocol.Notification.V2.Client.NotificationReceivedNotification>(raw);
        bool changed = false;
        var dropped = new List<Blizzard.GameService.Protocol.Notification.V2.Client.Notification>();
        foreach (var n in container.Notifications)
        {
            if (n.Type != "WTCG.UtilNotificationMessage") continue;
            var attrs = n.Attribute;
            long messageType = attrs.FirstOrDefault(a => a.Name == "message_type")?.Value?.IntValue ?? 0;
            if (collection != null && ServerOwnedLists.Contains(messageType))
            {
                dropped.Add(n);
                continue;
            }
            if (messageType is not (713 or 714) && !(messageType is 333 or 236 or 318 or 608 or 614 or 270 && collection != null) &&
                !(messageType is 601 or 602 && collection != null && quests != null)) continue;
            var fragments = attrs.Where(a => a.Name.StartsWith("fragment_", StringComparison.Ordinal)).OrderBy(a => a.Name, StringComparer.Ordinal).ToList();
            byte[] payload = fragments.SelectMany(a => a.Value.BlobValue.ToByteArray()).ToArray();
            // "z"=1: the fragments hold a zlib stream (client ZipCompressor). The rewrite is sent uncompressed.
            // The "z" flag goes only with a rewrite (a skipped notification keeps its compressed fragments and flag).
            var z = attrs.FirstOrDefault(a => a.Name == "z");
            if (z != null)
            {
                if (z.Value.IntValue == 1)
                {
                    using var inflated = new MemoryStream();
                    using (var zlib = new System.IO.Compression.ZLibStream(new MemoryStream(payload), System.IO.Compression.CompressionMode.Decompress))
                        zlib.CopyTo(inflated);
                    payload = inflated.ToArray();
                }
            }
            if (messageType == 270) // C4: the player's own win / loss records (live's adopted once)
            {
                payload = PegasusPackets.Serialize(collection.RecordsLogin(PegasusPackets.Deserialize<PegasusUtil.PlayerRecords>(payload)));
            }
            else if (messageType == 602) // C4: the player's own quest pools (live's adopted once, timed from the recording)
            {
                DateTime recordedAt = n.CreationTimeMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds((long)n.CreationTimeMs).UtcDateTime : DateTime.UtcNow;
                payload = PegasusPackets.Serialize(quests.PoolsLogin(collection, PegasusPackets.Deserialize<PegasusUtil.PlayerQuestPoolStateUpdate>(payload), recordedAt));
            }
            else if (messageType == 601) // C4: the player's own quests (live's adopted once)
            {
                payload = PegasusPackets.Serialize(quests.QuestsLogin(collection, PegasusPackets.Deserialize<PegasusUtil.PlayerQuestStateUpdate>(payload)));
            }
            else if (messageType == 614) // C4: the player's own reward tracks (the recorded login's adopted once)
            {
                payload = PegasusPackets.Serialize(collection.RewardTrackLogin(PegasusPackets.Deserialize<PegasusUtil.PlayerRewardTrackStateUpdate>(payload)));
            }
            else if (messageType is 236 or 318 or 608) // B2: the player's own favorites
            {
                IProtoBuf msg = messageType switch
                {
                    236 => PegasusPackets.Deserialize<PegasusUtil.CardBacks>(payload),
                    318 => PegasusPackets.Deserialize<PegasusUtil.FavoriteHeroesResponse>(payload),
                    _ => PegasusPackets.Deserialize<PegasusUtil.CosmeticCoins>(payload),
                };
                switch (msg)
                {
                    case PegasusUtil.CardBacks b: collection.ApplyTo(b); break;
                    case PegasusUtil.FavoriteHeroesResponse f: collection.ApplyTo(f); break;
                    case PegasusUtil.CosmeticCoins c: collection.ApplyTo(c); break;
                }
                payload = PegasusPackets.Serialize(msg);
            }
            else if (messageType == 333)
            {
                // B2: a recorded change notice must not change the player's own collection or dust (the recording's
                // pack openings, crafts...): those parts go; achievements, XP, records stay.
                var notice = PegasusPackets.Deserialize<PegasusUtil.ClientStateNotification>(payload);
                if (notice.CollectionModifications == null && notice.CurrencyState == null && notice.PlayerDraftTickets == null &&
                    notice.BoosterModifications == null) continue;
                notice.CollectionModifications = null;
                notice.CurrencyState = null;
                notice.BoosterModifications = null; // C2: the player's own packs
                notice.PlayerDraftTickets = null; // C1: the player's own tickets
                payload = PegasusPackets.Serialize(notice);
            }
            else if (messageType == 713)
            {
                var collectionState = PegasusPackets.Deserialize<PegasusUtil.CollectionClientStateResponse>(payload);
                bool ownCollection = collectionState.Collection != null && (grants != null || collection != null);
                bool leftGame = collectionState.SecondaryState?.DisconnectedGame != null;
                if (!ownCollection && !leftGame) continue;
                // B2: the player's own collection (the recorded stacks only lend their date)
                if (ownCollection && collection != null)
                    collectionState.Collection = collection.ToCollection(collectionState.Collection.Stacks.FirstOrDefault()?.LatestInsertDate);
                else if (ownCollection)
                    Grant(collectionState.Collection, grants);
                if (collectionState.SecondaryState != null)
                {
                    if (ownCollection) decks.ApplyTo(collectionState.SecondaryState);
                    if (collection != null && collectionState.SecondaryState.Boosters != null)
                        collection.ApplyTo(collectionState.SecondaryState.Boosters); // C2
                    collectionState.SecondaryState.DisconnectedGame = null; // see the 714
                    if (medal != null && collectionState.SecondaryState.MedalInfo != null) collectionState.SecondaryState.MedalInfo = medal();
                }
                payload = PegasusPackets.Serialize(collectionState);
            }
            else
            {
                var state = PegasusPackets.Deserialize<PegasusUtil.SecondaryClientStateResponse>(payload);
                if (state.SecondaryState == null) continue;
                decks.ApplyTo(state.SecondaryState);
                // The recorded login's unfinished game is a live one on Blizzard's servers (address, game handle,
                // password): replayed, the client keeps trying to rejoin it ("Reconnecting...") and reaches out to live.
                // A4: the player's own unfinished game on this server goes here instead; the client rejoins it.
                state.SecondaryState.DisconnectedGame = unfinished;
                if (collection != null && state.SecondaryState.GameCurrencyStates != null)
                    collection.ApplyTo(state.SecondaryState.GameCurrencyStates); // B2: the player's dust
                if (collection != null && state.SecondaryState.Boosters != null)
                    collection.ApplyTo(state.SecondaryState.Boosters); // C2: the player's unopened packs
                if (collection != null && state.SecondaryState.PlayerDraftTickets != null)
                    collection.ApplyTo(state.SecondaryState.PlayerDraftTickets); // C1: the player's Arena tickets
                if (collection != null) // the player's own settings and the notices they haven't dismissed
                {
                    state.SecondaryState.ClientOptions = collection.ApplyTo(state.SecondaryState.ClientOptions);
                    collection.ApplyTo(state.SecondaryState.Notices);
                }
                if (calendar != null) // B2: the event calendar as of now (the recorded seconds would restart every login)
                    state.SecondaryState.SpecialEventTiming = calendar.Timings();
                if (medal != null) state.SecondaryState.MedalInfo = medal(); // the player's own ranks (Ranked.cs)
                // (2026-11-14) the player's own save data — the client takes most keys from here (mode unlocks PLAYER_FLAGS…)
                if (saveData != null) state.SecondaryState.GameSaveData = saveData.AllUpdates();
                deckCount = state.SecondaryState.Decks.Count;
                payload = PegasusPackets.Serialize(state);
            }

            if (z != null) attrs.Remove(z);
            foreach (var f in fragments) attrs.Remove(f);
            attrs.First(a => a.Name == "message_size").Value = new Blizzard.GameService.Protocol.V2.Variant { IntValue = payload.Length };
            attrs.Add(new Blizzard.GameService.Protocol.V2.Attribute { Name = "fragment_000", Value = new Blizzard.GameService.Protocol.V2.Variant { BlobValue = Blob(payload) } });
            changed = true;
        }
        if (dropped.Count > 0)
        {
            foreach (var n in dropped) container.Notifications.Remove(n);
            if (container.Notifications.Count == 0) return null;
            changed = true;
        }
        return changed ? Protocol.Bgs.Serialize(container) : raw;
    }

    // GetAccountInfo answers the server builds from the player's state (AccountInfo): their recorded copies never replay
    // C4: live's XP bars (617) and gold grants (735) after its own games / claims: never replayed
    // C4: the player's class levels (HeroXP 283) and ranks (MedalInfo 232) are the server's: live's recorded copies, replayed
    // as a recorded exchange's follow-ups, overwrote ours right after (the class bar snapped back to live's level 46 / 35 XP)
    private static readonly HashSet<long> ServerOwnedLists = new() { 325, 621, 626, 637, 642, 649, 617, 735, 283, 232 };

    // Raise each granted card's normal (non-golden) stack to at least its copies; new stacks borrow a recorded date.
    private static void Grant(PegasusUtil.Collection collection, IReadOnlyList<(int Dbf, int Count)> grants)
    {
        var normal = collection.Stacks.Where(s => s.CardDef.Premium == 0).GroupBy(s => s.CardDef.Asset).ToDictionary(g => g.Key, g => g.First());
        DateTime now = DateTime.UtcNow; // (a CardStack needs its date: now when there is no recorded one)
        var date = collection.Stacks.FirstOrDefault()?.LatestInsertDate ??
                   new PegasusShared.Date { Year = now.Year, Month = now.Month, Day = now.Day, Hours = now.Hour, Min = now.Minute, Sec = now.Second };
        foreach (var (dbf, count) in grants)
        {
            if (normal.TryGetValue(dbf, out var stack))
            {
                if (stack.Count < count) stack.Count = count;
                continue;
            }
            var added = new PegasusShared.CardStack { CardDef = new PegasusShared.CardDef { Asset = dbf, Premium = 0 }, Count = count, NumSeen = count };
            if (date != null) added.LatestInsertDate = date;
            collection.Stacks.Add(added);
        }
    }

    // Requests the client waits on for one specific reply, answered empty when the reference login has no recording of
    // them. Mercenaries (ignored, user decision): the main shop opens only once the Mercenaries collection is loaded
    // (StoreManager.Load GENERAL_STORE → CollectionManager.IsLettuceLoaded: player info, collection and team list); a
    // GenericResponse left the shop loading forever (local-test-20261002-luckydraw-shop).
    private static IProtoBuf EmptyAnswer(int request) => request switch
    {
        10101 => new PegasusLettuce.MercenariesPlayerInfoResponse(),
        10103 => new PegasusLettuce.MercenariesCollectionResponse { ErrorCode = PegasusShared.ErrorCode.ERROR_OK, MercenaryList = new PegasusLettuce.MercenaryDetailedList() },
        10110 => new PegasusLettuce.MercenariesTeamListResponse { ErrorCode = PegasusShared.ErrorCode.ERROR_OK, TeamList = new PegasusLettuce.LettuceTeamList() },
        10181 => new PegasusLettuce.MercenariesMythicTreasureScalarsResponse(),
        // (2026-11-14, no recording: the replies live sends for these, empty — server time, Battlegrounds / Arena ratings)
        364 => new PegasusUtil.GetServerTimeResponse { ServerUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
        372 => new PegasusUtil.BattlegroundsRatingInfoResponse(),
        718 => new PegasusUtil.ArenaRatingInfoResponse(),
        321 => new PegasusUtil.GetAssetResponse(),
        305 => new PegasusUtil.AdventureProgressResponse(),
        // live answers these with NOP (254): options, Lucky Draw state, currency flush, smart deck, card / quest / reward acks
        239 or 10200 or 736 or 369 or 223 or 604 or 615 or 616 => new PegasusUtil.NOP(),
        _ => null,
    };

    // The reference login's last recorded message of a type (live's state to take over once); null if none. Several
    // answers (10201, 727, 732) come as their own util message after the request's reply, so the recorded exchange's
    // own reply body is not where they are.
    private T LastRecorded<T>(int messageType) where T : class, IProtoBuf, new() =>
        context.Account.LastUtil.TryGetValue(messageType, out byte[] body) ? PegasusPackets.Deserialize<T>(body) : null;

    // Exact request match first (same parameters), then the n-th recorded occurrence, then the last one.
    private static long subscribeRoutes = 1000;

    // Live's GenericResponse (326) to a request whose answer follows as a notification (711, 712, 205, 327…): RESULT_REQUEST_IN_PROCESS
    // (session-20261101-162527). RESULT_OK is the client's fatal "Unhandled resultCode" for such a request (Network.OnGenericResponse).
    private static PegasusUtil.GenericResponse InProcess(int request) =>
        new() { ResultCode = PegasusUtil.GenericResponse.Result.RESULT_REQUEST_IN_PROCESS, RequestId = request };

    private UtilExchange PickRecorded(int type, byte[] body)
    {
        if (!context.Account.Util.TryGetValue(type, out var list) || list.Count == 0) return null;
        utilOccurrences.TryGetValue(type, out int n);
        utilOccurrences[type] = n + 1;
        return list.FirstOrDefault(e => e.RequestBody.AsSpan().SequenceEqual(body) && !notificationsSent.Contains(e))
            ?? list.FirstOrDefault(e => e.RequestBody.AsSpan().SequenceEqual(body))
            ?? list[Math.Min(n, list.Count - 1)];
    }

    private Task UtilReplyAsync(Header h, IProtoBuf pegasus)
    {
        pegasus = NoRecordingFill(pegasus);
        int packetId = PegasusPackets.IdOf(pegasus.GetType());
        var response = new ProcessTaskResponse();
        response.Result.Add(new Blizzard.GameService.Protocol.V2.Attribute { Name = "id", Value = new Blizzard.GameService.Protocol.V2.Variant { IntValue = packetId } });
        response.Result.Add(new Blizzard.GameService.Protocol.V2.Attribute
        {
            Name = "proto",
            Value = new Blizzard.GameService.Protocol.V2.Variant { BlobValue = Blob(PegasusPackets.Serialize(pegasus)) },
        });
        return ReplyAsync(h, response);
    }

    /// <summary>Push a Pegasus util notification (e.g. game-to-connect, save data changes).</summary>
    // An empty account state for a player with no recorded login (the lists the rewrite fills in place).
    private static PegasusUtil.SecondaryClientState NewSecondaryState() => new()
    {
        GameCurrencyStates = new PegasusUtil.GameCurrencyStates(),
        Boosters = new PegasusUtil.Boosters(),
        PlayerDraftTickets = new PegasusUtil.PlayerDraftTickets(),
        Notices = new PegasusUtil.ProfileNotices(),
        ClientOptions = new PegasusUtil.ClientOptions(), // (the client's login cache waits for the options: NetCacheClientOptions)
        GuardianVars = FeatureFlags(Program.ProjectRoot()), // the server's feature flags (Server/data/feature-flags.b64)
        PlayerProfileProgress = new PegasusUtil.ProfileProgress { Progress = 6, TutorialComplete = true }, // (see CAMPAIGN_INFO)
    };

    public Task NotifyUtilAsync(IProtoBuf pegasus) => NotifyAsync(BgsRoutes.NotificationListener, 1, UtilNotification(NoRecordingFill(pegasus)));

    // With no recorded login (the public start-up) every message the server builds gets its required sub-messages (Fill):
    // a default the code expected the recording to supply is created empty instead of failing the send.
    private T NoRecordingFill<T>(T pegasus) where T : class => context.Account.SourceSession == null ? PegasusPackets.Fill(pegasus) : pegasus;

    private static Blizzard.GameService.Protocol.Notification.V2.Client.NotificationReceivedNotification UtilNotification(IProtoBuf pegasus)
    {
        byte[] payload = PegasusPackets.Serialize(pegasus);
        var n = new Blizzard.GameService.Protocol.Notification.V2.Client.Notification { Type = "WTCG.UtilNotificationMessage" };
        n.Attribute.Add(new Blizzard.GameService.Protocol.V2.Attribute { Name = "message_type", Value = new Blizzard.GameService.Protocol.V2.Variant { IntValue = PegasusPackets.IdOf(pegasus.GetType()) } });
        n.Attribute.Add(new Blizzard.GameService.Protocol.V2.Attribute { Name = "message_size", Value = new Blizzard.GameService.Protocol.V2.Variant { IntValue = payload.Length } });
        n.Attribute.Add(new Blizzard.GameService.Protocol.V2.Attribute { Name = "fragment_000", Value = new Blizzard.GameService.Protocol.V2.Variant { BlobValue = Blob(payload) } });
        var container = new Blizzard.GameService.Protocol.Notification.V2.Client.NotificationReceivedNotification();
        container.Notifications.Add(n);
        return container;
    }

    // ByteString.CopyFrom(byte[]) cannot be bound from net9 (Span overload ambiguity, see Bgs); parse a length-
    // delimited copy instead via a wrapper Variant.
    private static ByteString Blob(byte[] data) => ByteString.CopyFrom(data, 0, data.Length);

    // ---------- identity ----------

    private GameAccountHandle GameAccount() => new()
    {
        Id = (uint)GameAccountId, TitleId = BgsRoutes.TitleId, Region = context.Account.Region,
    };

    private SessionId SessionId() => new()
    {
        AccountId = AccountId,
        GameAccount = GameAccount(),
        InstanceId = "hsserver-" + id,
        Region = context.Account.Region,
    };

    // ---------- sending ----------

    public Task ReplyAsync(Header request, IMessage body, uint status = 0) =>
        SendRawAsync(ResponseHeader(request, status), Protocol.Bgs.Serialize(body));

    public Task NotifyAsync(uint serviceHash, uint methodId, IMessage body) =>
        SendRawAsync(NotificationHeader(serviceHash, methodId), Protocol.Bgs.Serialize(body));

    private static Header ResponseHeader(Header request, uint status = 0)
    {
        var header = new Header { ServiceId = BgsFrame.ResponseServiceId, Token = request.Token, Status = status };
        if (request.HasClientId) header.ClientId = request.ClientId;
        return header;
    }

    private Header NotificationHeader(uint serviceHash, uint methodId) =>
        new() { ServiceId = 0, ServiceHash = serviceHash, MethodId = methodId, Token = Interlocked.Increment(ref notificationToken) };

    private async Task SendRawAsync(Header header, byte[] body)
    {
        byte[] frame = BgsFrame.EncodeRaw(header, body);
        await sendLock.WaitAsync();
        try { await socket.SendAsync(frame, WebSocketMessageType.Binary, true, CancellationToken.None); }
        finally { sendLock.Release(); }
    }
}
