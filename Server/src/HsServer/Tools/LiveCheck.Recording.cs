using HsServer.Protocol;
using PegasusGame;
using GameTag = SabberStoneCore.Enums.GameTag;

namespace HsServer.Tools;

/// <summary>`livecheck`, part 1: a live recording read as a list of player actions, each with the game just before and just after it.</summary>
internal static partial class LiveCheck
{
    /// <summary>One entity of a live game as the recording's viewer knew it: its card (kept once seen) and its tags.</summary>
    internal sealed class LiveEntity
    {
        public int Id;
        public string Card = "";
        public Dictionary<int, int> Tags = new();
        public int this[GameTag t] => Tags.TryGetValue((int)t, out int v) ? v : 0;
        /// <summary>Shown to the viewer (a hidden entity has only ZONE / CONTROLLER / ENTITY_ID).</summary>
        public bool Visible => Tags.ContainsKey((int)GameTag.CARDTYPE);
        public LiveEntity Copy() => new() { Id = Id, Card = Card, Tags = new Dictionary<int, int>(Tags) };
    }

    /// <summary>A choice (Discover, "choose one of these") offered inside an action, and what was picked.</summary>
    internal sealed class LiveChoice
    {
        public int Id, PlayerId, Source, Type;
        public List<int> Entities = new();
        public List<int> Picked; // null: the viewer didn't see the pick (the opponent's)
    }

    /// <summary>One player action of a live game (a card played, a Hero Power, a Location, a trade, an attack).</summary>
    internal sealed class LiveAction
    {
        public string Session = "";
        public int GameIndex;
        public long Seq;
        public DateTime Time;
        public int Line;             // line of its BLOCK_START in `HsServer power <session>`
        public HistoryBlock.Type Block;
        public int Source, Target;
        public int BlockSubOption = -1;
        public int SubOptionEntity;   // the client's pick of a sub-option (Choose One), as an entity
        public int Position;          // where a minion went (1-based), 0 unknown
        public bool ByViewer;         // the recording's player sent it (SendOption seen)
        /// <summary>A turn's end: from MAIN_END to the next player's MAIN_ACTION (end- and start-of-turn triggers, the draw, choices).</summary>
        public bool EndTurn;
        public Dictionary<int, LiveEntity> Before, After;
        public List<LiveChoice> Choices = new();
        public List<int> LeftDeck = new();
        public int MaxIdBefore;
        public int GameEntity = 1;
        /// <summary>The first entity id live made after the decks (the ids below started in a deck or as a hero).</summary>
        public int FirstGenerated = int.MaxValue;
        /// <summary>The game's history before this action (shared list; the first <see cref="HistoryCount"/> items are before it).</summary>
        public List<LiveHistory> History = new();
        public int HistoryCount;
        /// <summary>The source's tags as the action first showed them (a card played from a hidden hand: its Attack / Health / Cost there,
        /// buffs in hand included).</summary>
        public Dictionary<int, int> SourceShown;
        /// <summary>Tags set (non-zero) on the source during the action (IS_USING_PREPARE_OPTION, IS_USING_FORGE_OPTION: which deck action).</summary>
        public HashSet<int> SourceTagsSet = new();
        /// <summary>Every entity that had a block of its own inside the action (what acted).</summary>
        public HashSet<int> BlockSources = new();
        /// <summary>The block sources whose blocks changed or made another entity than themselves and the game (not only their own
        /// bookkeeping: a Shattered half's script counter, an enchantment removing itself).</summary>
        public HashSet<int> EffectiveSources = new();
        /// <summary>When each entity last came into play / the secret zone (an event number of the recording): live's order of play.</summary>
        public Dictionary<int, int> EnteredPlay = new();
        /// <summary>Card ids the viewer learns later (a card hidden now, shown when played): entity → card.</summary>
        public Func<int, string> LaterCard = _ => "";

        public string CardOf(int id, bool after = false)
        {
            var s = after ? After : Before;
            if (s.TryGetValue(id, out LiveEntity e) && e.Card.Length > 0) return e.Card;
            // (a card the viewer didn't know yet: as first shown from the action on — the card played, not what it became: Druid of the
            // Claw shown as CORE_EX1_165, then transformed)
            if (!after && LaterCard(id) is { Length: > 0 } shown) return shown;
            if (!after && After.TryGetValue(id, out LiveEntity a) && a.Card.Length > 0) return a.Card;
            return LaterCard(id);
        }
    }

    internal enum HistoryKind { Played, Died, Summoned, ToHand, HandTrigger, HeroDamaged, TurnEnded }

    /// <summary>Something the engine remembers for the rest of the game: a card played, a minion died / summoned, a card added to hand.</summary>
    internal sealed record LiveHistory(HistoryKind Kind, int Player, int Entity, int Turn, int Target, int Cost, string Card = "");

    private enum EvKind { Power, Options, SendOption, Choices, Chosen }

    private readonly record struct Ev(EvKind Kind, object Data, long Seq, DateTime Time, int Line);

    /// <summary>The session's game events with the line each one has in `HsServer power` (PowerDump's layout).</summary>
    private static List<Ev> ReadEvents(string session)
    {
        int historyId = PegasusPackets.IdOf<PowerHistory>(), optionsId = PegasusPackets.IdOf<AllOptions>();
        int chooseOptionId = PegasusPackets.IdOf<ChooseOption>();
        int choicesId = PegasusPackets.IdOf<EntityChoices>(), chooseEntitiesId = PegasusPackets.IdOf<ChooseEntities>();
        var list = new List<Ev>();
        int line = 1;
        foreach (RecordedPacket p in Recording.Read(session))
        {
            if (p.Channel != "game" || p.Body == null) continue;
            DateTime time = p.Time.ToLocalTime();
            if (p.IsIn && p.Type == historyId)
            {
                var history = PegasusPackets.Deserialize<PowerHistory>(p.Body);
                line++; // "# time PowerHistory (seq n)"
                foreach (PowerHistoryData d in history.List)
                {
                    list.Add(new Ev(EvKind.Power, d, p.Seq, time, line));
                    line += Lines(d);
                }
            }
            else if (p.IsIn && p.Type == optionsId)
            {
                var o = PegasusPackets.Deserialize<AllOptions>(p.Body);
                list.Add(new Ev(EvKind.Options, o, p.Seq, time, line));
                line++;
                foreach (Option op in o.Options)
                    line += 1 + (op.MainOption?.Targets?.Count ?? 0) + (op.SubOptions?.Count ?? 0);
            }
            else if (!p.IsIn && p.Type == chooseOptionId)
            {
                list.Add(new Ev(EvKind.SendOption, PegasusPackets.Deserialize<ChooseOption>(p.Body), p.Seq, time, line));
                line++;
            }
            else if (p.IsIn && p.Type == choicesId)
            {
                list.Add(new Ev(EvKind.Choices, PegasusPackets.Deserialize<EntityChoices>(p.Body), p.Seq, time, line));
                line++;
            }
            else if (!p.IsIn && p.Type == chooseEntitiesId)
            {
                list.Add(new Ev(EvKind.Chosen, PegasusPackets.Deserialize<ChooseEntities>(p.Body), p.Seq, time, line));
                line++;
            }
        }
        return list;
    }

    /// <summary>The lines HistoryText.Format (with tags) writes for one item.</summary>
    private static int Lines(PowerHistoryData d)
    {
        if (d.FullEntity is { } f) return 1 + f.Tags.Count + (f.TagLists?.Count ?? 0) + (f.DefTagLists?.Count ?? 0);
        if (d.ShowEntity is { } s) return 1 + s.Tags.Count + (s.TagLists?.Count ?? 0) + (s.DefTagLists?.Count ?? 0);
        if (d.ChangeEntity is { } c) return 1 + c.Tags.Count;
        if (d.PowerStart != null || d.PowerEnd != null || d.TagChange != null || d.MetaData != null || d.TagListChange != null ||
            d.HideEntity != null || d.SubSpellStart != null || d.SubSpellEnd != null || d.CreateGame != null || d.ShuffleDeck != null)
            return 1;
        return 0;
    }

    private static bool IsAction(HistoryBlock.Type t) => t is HistoryBlock.Type.PLAY or HistoryBlock.Type.ATTACK or HistoryBlock.Type.DECK_ACTION;

    /// <summary>Reads a session and hands every finished action to <paramref name="onAction"/>.</summary>
    internal static void ReadActions(string session, Action<LiveAction> onAction)
    {
        List<Ev> events = ReadEvents(session);
        string name = Path.GetFileName(session);

        // later reveals, per game (entity ids restart with every CREATE_GAME): entity → (event index, card) of each FULL / SHOW
        var gameStarts = new List<int>();
        for (int i = 0; i < events.Count; i++)
            if (events[i].Data is PowerHistoryData { CreateGame: not null }) gameStarts.Add(i);
        var reveals = new Dictionary<int, List<(int At, string Card)>>();
        int revealGame = -1;
        void LoadReveals(int game)
        {
            reveals.Clear();
            revealGame = game;
            int from = gameStarts[game], to = game + 1 < gameStarts.Count ? gameStarts[game + 1] : events.Count;
            for (int i = from; i < to; i++)
            {
                if (events[i].Data is not PowerHistoryData d) continue;
                (int id, string card) = d.FullEntity is { } f ? (f.Entity, f.Name) : d.ShowEntity is { } s ? (s.Entity, s.Name) : (0, null);
                if (id == 0 || string.IsNullOrEmpty(card)) continue;
                if (!reveals.TryGetValue(id, out var l)) reveals[id] = l = new();
                l.Add((i, card));
            }
        }

        var state = new Dictionary<int, LiveEntity>();
        int depth = 0, game = -1, gameEntity = 1;
        AllOptions options = null;
        ChooseOption send = null;
        LiveAction cur = null;
        int curEvent = 0;
        var history = new List<LiveHistory>();
        long createSeq = -1;
        int firstGenerated = int.MaxValue, initialMax = 0;
        int Turn() => state.TryGetValue(gameEntity, out var ge) ? ge[GameTag.TURN] : 0;
        bool firstTurnStarted = false;
        var blockStack = new List<int>();
        // a Rewind's GAME_RESET block: every entity of the rewound game is sent again; the client drops the ones above the highest re-sent
        // (the discarded timeline's) when it ends (ClientView)
        int resetDepth = -1, resetMax = 0;
        void Touched(int id)
        {
            if (cur == null || id == gameEntity) return;
            foreach (int src in blockStack)
                if (src != id) cur.EffectiveSources.Add(src);
        }

        LiveEntity Get(int id)
        {
            if (!state.TryGetValue(id, out LiveEntity e)) state[id] = e = new LiveEntity { Id = id };
            return e;
        }
        Dictionary<int, LiveEntity> Copy() => state.ToDictionary(k => k.Key, k => k.Value.Copy());
        void Finish()
        {
            if (cur == null) return;
            cur.After = Copy();
            int start = curEvent;
            var r = reveals.ToDictionary(k => k.Key, k => k.Value);
            cur.LaterCard = id => r.TryGetValue(id, out var l) ? l.FirstOrDefault(x => x.At >= start).Card ?? l[^1].Card : "";
            LiveAction done = cur;
            cur = null;
            // (the card played as the play showed it: a hidden hand card's id comes with the play; a later transform doesn't change it)
            if (done.HistoryCount < history.Count && history[done.HistoryCount] is { Kind: HistoryKind.Played, Card: "" } played && played.Entity == done.Source)
                history[done.HistoryCount] = played with { Card = done.CardOf(done.Source, after: true) is { Length: > 0 } shown ? shown : done.CardOf(done.Source) };
            onAction(done);
        }
        var enteredPlay = new Dictionary<int, int>();
        int eventNo = 0;
        void ZoneMoved(int id, int oldZone, int newZone)
        {
            if (newZone == oldZone) return;
            if (newZone is (int)SabberStoneCore.Enums.Zone.PLAY or (int)SabberStoneCore.Enums.Zone.SECRET) enteredPlay[id] = eventNo;
            if (cur != null && oldZone == (int)SabberStoneCore.Enums.Zone.DECK && !cur.LeftDeck.Contains(id))
                cur.LeftDeck.Add(id);
            LiveEntity e = Get(id);
            int type = e[GameTag.CARDTYPE], ctrl = e[GameTag.CONTROLLER];
            if (oldZone == (int)SabberStoneCore.Enums.Zone.PLAY && newZone == (int)SabberStoneCore.Enums.Zone.GRAVEYARD && type is 4 or 7)
                history.Add(new LiveHistory(HistoryKind.Died, ctrl, id, Turn(), 0, 0));
            else if (newZone == (int)SabberStoneCore.Enums.Zone.PLAY && type == 4)
                history.Add(new LiveHistory(HistoryKind.Summoned, ctrl, id, Turn(), 0, 0));
            else if (newZone == (int)SabberStoneCore.Enums.Zone.HAND)
                // (the starting hand: before the first turn's MAIN_READY, turn 0 for the engine's Quickdraw)
                history.Add(new LiveHistory(HistoryKind.ToHand, ctrl, id, firstTurnStarted ? Turn() : 0, 0, 0));
        }
        void SourcePosition(LiveEntity e)
        {
            if (cur != null && e.Id == cur.Source && cur.Position == 0 && e[GameTag.ZONE] == (int)SabberStoneCore.Enums.Zone.PLAY && e[GameTag.ZONE_POSITION] > 0)
                cur.Position = e[GameTag.ZONE_POSITION];
        }

        for (int i = 0; i < events.Count; i++)
        {
            Ev ev = events[i];
            eventNo = i;
            switch (ev.Data)
            {
                case AllOptions o: options = o; continue;
                case ChooseOption c: send = c; continue;
                case EntityChoices ch:
                    if (cur != null && ch.ChoiceType != 1)
                        cur.Choices.Add(new LiveChoice { Id = ch.Id, PlayerId = ch.PlayerId, Source = ch.Source, Type = ch.ChoiceType, Entities = ch.Entities.ToList() });
                    continue;
                case ChooseEntities chosen:
                    if (cur?.Choices.LastOrDefault(x => x.Id == chosen.Id) is { } open) open.Picked = chosen.Entities.ToList();
                    continue;
            }
            var d = (PowerHistoryData)ev.Data;
            if (d.CreateGame is { } cg)
            {
                Finish();
                state.Clear();
                enteredPlay.Clear();
                history = new List<LiveHistory>();
                firstTurnStarted = false;
                createSeq = ev.Seq;
                firstGenerated = int.MaxValue;
                initialMax = 0;
                depth = 0;
                game++;
                if (revealGame != game) LoadReveals(game);
                gameEntity = cg.GameEntity.Id;
                var ge = Get(gameEntity);
                foreach (Tag t in cg.GameEntity.Tags) ge.Tags[t.Name] = t.Value;
                foreach (PegasusGame.Player p in cg.Players)
                {
                    var pe = Get(p.Entity.Id);
                    foreach (Tag t in p.Entity.Tags) pe.Tags[t.Name] = t.Value;
                }
                continue;
            }
            if (game < 0) continue;
            if (ev.Seq != createSeq && firstGenerated == int.MaxValue && createSeq >= 0) firstGenerated = initialMax + 1;
            if (d.PowerStart is { } ps)
            {
                if (ps.Type == HistoryBlock.Type.GAME_RESET)
                {
                    if (depth == 0) Finish(); // (the rewound action: its outcome is the discarded timeline's)
                    resetDepth = depth;
                    resetMax = 0;
                }
                if (cur != null) cur.BlockSources.Add(ps.Source);
                if (depth == 0) blockStack.Clear();
                // (a card's trigger in hand: "... while holding this" counting, the engine keeps the count on the card)
                if (ps.Type == HistoryBlock.Type.TRIGGER && state.GetValueOrDefault(ps.Source)?[GameTag.ZONE] == (int)SabberStoneCore.Enums.Zone.HAND)
                    history.Add(new LiveHistory(HistoryKind.HandTrigger, state[ps.Source][GameTag.CONTROLLER], ps.Source, Turn(), 0, 0));
                if (depth == 0 && IsAction(ps.Type))
                {
                    Finish();
                    // a card played from the hand: remembered for the rest of the game (after this action's own check)
                    LiveEntity played = state.GetValueOrDefault(ps.Source);
                    bool fromHand = ps.Type == HistoryBlock.Type.PLAY && played?[GameTag.ZONE] == (int)SabberStoneCore.Enums.Zone.HAND;
                    {
                        cur = new LiveAction
                        {
                            Session = name, GameIndex = game, Seq = ev.Seq, Time = ev.Time, Line = ev.Line, Block = ps.Type,
                            Source = ps.Source, Target = ps.Target, BlockSubOption = ps.SubOption, Before = Copy(), GameEntity = gameEntity,
                            MaxIdBefore = state.Count == 0 ? 0 : state.Keys.Max(),
                            FirstGenerated = firstGenerated, History = history, HistoryCount = history.Count,
                            EnteredPlay = new Dictionary<int, int>(enteredPlay),
                        };
                        if (fromHand)
                            history.Add(new LiveHistory(HistoryKind.Played, played[GameTag.CONTROLLER], ps.Source, Turn(), ps.Target, played[GameTag.COST], played.Card));
                        curEvent = i;
                        // the viewer's own option (sent since the last action) for this source
                        if (send != null && options != null && options.Id == send.Id && send.Index >= 0 && send.Index < options.Options.Count)
                        {
                            Option op = options.Options[send.Index];
                            if (op.MainOption?.Id == ps.Source)
                            {
                                cur.ByViewer = true;
                                if (send.Position > 0) cur.Position = send.Position;
                                if (send.SubOption >= 0 && op.SubOptions != null && send.SubOption < op.SubOptions.Count)
                                    cur.SubOptionEntity = op.SubOptions[send.SubOption].Id;
                                if (cur.Target == 0 && send.Target != 0) cur.Target = send.Target;
                            }
                        }
                        send = null;
                    }
                }
                depth++;
                blockStack.Add(ps.Source);
                continue;
            }
            if (d.PowerEnd != null)
            {
                depth = Math.Max(0, depth - 1);
                if (resetDepth >= 0 && depth == resetDepth)
                {
                    foreach (int gone in state.Keys.Where(id => id > resetMax).ToList()) state.Remove(gone);
                    resetDepth = -1;
                }
                if (blockStack.Count > 0) blockStack.RemoveAt(blockStack.Count - 1);
                continue;
            }
            if (d.TagChange is { } tc)
            {
                // (2026-11-19) an unseen TARGET pick (the opponent's): live notes it on the choice's source as tag 4470 = the target
                // (20261109-205554 53578: Fetid Devourer's second destroy)
                if (tc.Tag == 4470 && cur?.Choices.LastOrDefault(x => x.Source == tc.Entity && x.Picked == null && x.Type == 3) is { } aimed &&
                    aimed.Entities.Contains(tc.Value))
                    aimed.Picked = new List<int> { tc.Value };
                if (tc.Entity == gameEntity && tc.Tag == (int)GameTag.STEP && tc.Value == (int)SabberStoneCore.Enums.Step.MAIN_READY) firstTurnStarted = true;
                if (depth == 0 && tc.Entity == gameEntity && tc.Tag == (int)GameTag.STEP)
                {
                    if (tc.Value == (int)SabberStoneCore.Enums.Step.MAIN_END)
                    {
                        // a turn ends: one action up to the next player's MAIN_ACTION
                        Finish();
                        cur = new LiveAction
                        {
                            Session = name, GameIndex = game, Seq = ev.Seq, Time = ev.Time, Line = ev.Line, EndTurn = true, Before = Copy(),
                            GameEntity = gameEntity, MaxIdBefore = state.Count == 0 ? 0 : state.Keys.Max(),
                            FirstGenerated = firstGenerated, History = history, HistoryCount = history.Count,
                            EnteredPlay = new Dictionary<int, int>(enteredPlay),
                        };
                        curEvent = i;
                        send = null;
                        // (the turn ends: after this action's own position)
                        LiveEntity ending = state.Values.FirstOrDefault(x => x[GameTag.CARDTYPE] == (int)SabberStoneCore.Enums.CardType.PLAYER && x[GameTag.CURRENT_PLAYER] == 1);
                        if (ending != null) history.Add(new LiveHistory(HistoryKind.TurnEnded, ending[GameTag.PLAYER_ID], ending.Id, Turn(), 0, 0));
                    }
                    else if (cur?.EndTurn != true || tc.Value == (int)SabberStoneCore.Enums.Step.MAIN_ACTION) Finish();
                }
                var e = Get(tc.Entity);
                int old = e[GameTag.ZONE];
                if (e[(GameTag)tc.Tag] != tc.Value) Touched(tc.Entity);
                // (a hero's damage taken: the engine counts it per turn — Rigged Faire Game, "this turn" counters; Armor lost counts)
                if (e[GameTag.CARDTYPE] == (int)SabberStoneCore.Enums.CardType.HERO && e[GameTag.ZONE] == (int)SabberStoneCore.Enums.Zone.PLAY &&
                    (tc.Tag == (int)GameTag.DAMAGE && tc.Value > e[GameTag.DAMAGE] || tc.Tag == (int)GameTag.ARMOR && tc.Value < e[GameTag.ARMOR]))
                    history.Add(new LiveHistory(HistoryKind.HeroDamaged, e[GameTag.CONTROLLER], tc.Entity, Turn(), 0, Math.Abs(tc.Value - e[(GameTag)tc.Tag])));
                e.Tags[tc.Tag] = tc.Value;
                if (cur != null && tc.Entity == cur.Source && tc.Value != 0) cur.SourceTagsSet.Add(tc.Tag);
                if (tc.Tag == (int)GameTag.ZONE) ZoneMoved(e.Id, old, tc.Value);
                if (tc.Tag is (int)GameTag.ZONE or (int)GameTag.ZONE_POSITION) SourcePosition(e);
                continue;
            }
            if (d.FullEntity is { } fe)
            {
                bool knownBefore = state.ContainsKey(fe.Entity);
                var e = new LiveEntity { Id = fe.Entity, Card = fe.Name ?? "" };
                foreach (Tag t in fe.Tags) e.Tags[t.Name] = t.Value;
                if (resetDepth >= 0)
                {
                    resetMax = Math.Max(resetMax, fe.Entity);
                    // (the rewound game's entity as it was: its card stays known when the reset sends it hidden)
                    if (e.Card.Length == 0 && state.TryGetValue(fe.Entity, out LiveEntity was) && was.Card.Length > 0 && was.Visible) e.Card = was.Card;
                }
                Touched(fe.Entity);
                state[e.Id] = e;
                if (ev.Seq == createSeq)
                {
                    initialMax = Math.Max(initialMax, e.Id);
                    // (a game joined in progress — a reconnect's dump: a minion already in the graveyard died earlier this game)
                    if (e[GameTag.ZONE] == (int)SabberStoneCore.Enums.Zone.GRAVEYARD && e[GameTag.CARDTYPE] == (int)SabberStoneCore.Enums.CardType.MINION)
                        history.Add(new LiveHistory(HistoryKind.Died, e[GameTag.CONTROLLER], e.Id, 0, 0, 0));
                }
                // HsServer (user, 2026-11-20): a mid-game FullEntity resend of an entity we already know (a reconnect's resync dump)
                // isn't a new summon — only a genuinely new entity's first FULL counts as one, same as ZoneMoved's own dedup via oldZone.
                else if (resetDepth < 0 && !knownBefore && e[GameTag.ZONE] is int z && z != 0) ZoneMoved(e.Id, 0, z);
                continue;
            }
            if (d.ShowEntity is { } se)
            {
                var e = Get(se.Entity);
                int old = e[GameTag.ZONE];
                Touched(se.Entity);
                if (cur != null && se.Entity == cur.Source && cur.SourceShown == null)
                    cur.SourceShown = se.Tags.GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.Last().Value);
                if (!string.IsNullOrEmpty(se.Name)) e.Card = se.Name;
                foreach (Tag t in se.Tags) e.Tags[t.Name] = t.Value;
                ZoneMoved(e.Id, old, e[GameTag.ZONE]);
                SourcePosition(e);
                continue;
            }
            if (d.ChangeEntity is { } ce)
            {
                var e = Get(ce.Entity);
                int old = e[GameTag.ZONE];
                Touched(ce.Entity);
                if (!string.IsNullOrEmpty(ce.Name)) e.Card = ce.Name;
                foreach (Tag t in ce.Tags) e.Tags[t.Name] = t.Value;
                ZoneMoved(e.Id, old, e[GameTag.ZONE]);
                continue;
            }
            if (d.HideEntity is { } he)
            {
                var e = Get(he.Entity);
                int old = e[GameTag.ZONE];
                // (2026-11-16) hidden again: the viewer keeps the card id it saw, but no tags beyond a hidden entity's (live: Mug'Zee
                // revealed at Start of Game, hidden back in the deck, drawn — its Cost changes are never sent to the viewer)
                // (not an enchantment: one on a hidden card is still the game's — it acts later, on the card shown again)
                if (e[GameTag.CARDTYPE] != (int)SabberStoneCore.Enums.CardType.ENCHANTMENT)
                foreach (int k in e.Tags.Keys.ToList())
                    if (k is not ((int)GameTag.ZONE or (int)GameTag.CONTROLLER or (int)GameTag.ENTITY_ID or (int)GameTag.ZONE_POSITION)) e.Tags.Remove(k);
                e.Tags[(int)GameTag.ZONE] = he.Zone;
                ZoneMoved(e.Id, old, he.Zone);
            }
        }
        Finish();
    }
}
