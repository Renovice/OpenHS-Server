using HsServer.Engine;
using HsServer.Game;
using HsServer.Protocol;
using PegasusGame;
using SabberStoneCore.Enums;
using SabberStoneCore.Model.Entities;

namespace HsServer.Sabber;

/// <summary>
/// Plays whole games on the rules engine through <see cref="IEngineGame"/> the way the network does, with a scripted
/// "client" as player 1 (random legal options, mulligan/Discover answers), and checks after every action that:
/// every packet serializes; every option the client was offered is accepted; every packet keeps the client-view rules
/// (<see cref="ClientView"/>: no re-reveal, no unknown entity, a known card has its type, a hidden card gets only what live
/// tells a hidden card, positions 1..n); the client's view rebuilt purely from the packets matches the engine as the viewer
/// may see it (<see cref="Compare"/>: card ids, zone, controller, position, the card's identity tags, stats, attack state, a
/// hidden Secret's class and kind); the opponent's hand and both decks never reveal a card id; the game ends.
/// </summary>
internal sealed class SoakRunner(Random rng)
{
    public sealed record Result(int Turns, int Packets, int HumanActions, bool Over, List<string> Problems);

    /// <summary>A4: every this many steps the client reconnects — its view is thrown away and rebuilt from the resume
    /// snapshot alone, which must show what it knew before (and agree with the engine). 0 = never.</summary>
    public int ResumeEvery { get; init; }

    /// <summary>Optional: receives (action label, history as Power.log-style text) for every history packet.</summary>
    public Action<string, string> Trace { get; init; }

    /// <summary>Every TAG_CHANGE the client receives: (entity kind as the client knew it then, tag) — for `wirecheck`.
    /// Kinds: game, player, hero, hand, play, other (the same buckets as the live-log side).</summary>
    public Action<string, int> TagSent { get; init; }

    internal static string KindOf(IReadOnlyDictionary<int, int> tags, int id)
    {
        tags.TryGetValue((int)GAME_TAG.CARDTYPE, out int type);
        tags.TryGetValue((int)GAME_TAG.ZONE, out int zone);
        if (id == 1 || type == 1) return "game";
        if (type == 2) return "player";
        if (type == 3 && zone == (int)TAG_ZONE.PLAY) return "hero";
        if (zone == (int)TAG_ZONE.HAND) return "hand";
        if (zone == (int)TAG_ZONE.PLAY) return "play";
        return "other";
    }

    /// <summary>Optional: the client's card database; every card id sent must exist in it (the client can't load others).</summary>
    public CardDb ClientCards { get; init; }
    private string label = "start";

    /// <summary>The packet a problem was first seen in, as Power.log-style text (the soak prints it under its example).</summary>
    public Dictionary<string, string> Offending { get; } = new();

    public Result Play(SabberEngineGame engine, int maxSteps = 600)
    {
        var problems = new List<string>();
        var view = new ClientView { Cards = ClientCards };
        int packets = 0, humanActions = 0;
        EntityChoices openChoice = null;
        PowerHistory lastPacket = null;
        AllOptions openOptions = null;

        void Take()
        {
            foreach (IProtoBuf packet in engine.Drain())
            {
                packets++;
                try { PegasusPackets.Deserialize(packet.GetType(), PegasusPackets.Serialize(packet)); }
                catch (Exception e) { problems.Add($"packet {packet.GetType().Name} does not serialize: {e.Message}"); }
                switch (packet)
                {
                    case PowerHistory h:
                        int had = problems.Count;
                        if (TagSent != null)
                            foreach (PowerHistoryData d in h.List)
                            {
                                if (d.TagChange is { } tc)
                                    TagSent(KindOf(view.Get(tc.Entity)?.Tags ?? new Dictionary<int, int>(), tc.Entity), tc.Tag);
                                // a tag the client gets in a FULL_ENTITY / SHOW_ENTITY counts as sent too
                                if ((d.FullEntity?.Tags ?? d.ShowEntity?.Tags) is { } entityTags)
                                {
                                    var now = entityTags.ToDictionary(t => t.Name, t => t.Value);
                                    int entityId = d.FullEntity?.Entity ?? d.ShowEntity.Entity;
                                    foreach (var t in entityTags) TagSent(KindOf(now, entityId), t.Name);
                                }
                            }
                        // the names as the client knew them before this packet (for the offending packet's text)
                        var named = view.Entities.ToDictionary(x => x.Key, x => x.Value.CardId);
                        view.Apply(h.List, problem => problems.Add($"packet {packets} after {label}: {problem}"));
                        // after Apply: a card revealed in this packet (the opponent's spell) is known by its id
                        CheckEffectTargets(h.List, problems, id => view.Get(id)?.CardId ?? "");
                        foreach (string p in problems.Skip(had))
                            Offending.TryAdd(p, HistoryText.Format(h.List, id => named.TryGetValue(id, out string c) && c != "" ? $"[{c}#{id}]" : $"#{id}", withTags: true));
                        if (problems.Count > had && Environment.GetEnvironmentVariable("HS_SOAK_DUMP") is { Length: > 0 } dumpTo && !File.Exists(dumpTo))
                            File.WriteAllLines(dumpTo, engine.Rules.PowerHistory.Full.Skip(Environment.GetEnvironmentVariable("HS_SOAK_DUMP_FULL") == "1" ? 0 : Math.Max(0, engine.Rules.PowerHistory.Full.Count - 150)).Select((e, i) => (Environment.GetEnvironmentVariable("HS_SOAK_DUMP_FULL") == "1" ? $"{i}: " : "") + e.Print().TrimEnd()).Prepend(DescribeUnknown(engine, problems[^1])).Prepend(problems[^1]));
                        if (ClientCards != null) CheckCardIds(h.List, ClientCards, problems);
                        Trace?.Invoke(label, HistoryText.Format(h.List, id => view.Get(id) is { Known: true } e ? $"[{e.CardId}#{id}]" : $"#{id}"));
                        lastPacket = h;
                        break;
                    case EntityChoices c when c.PlayerId == 1: openChoice = c; break;
                    case AllOptions o: openOptions = o; break;
                }
            }
        }

        engine.Start();
        Take();
        int step = 0;
        for (; step < maxSteps && !engine.Over && problems.Count == 0; step++)
        {
            if (ResumeEvery > 0 && step > 0 && step % ResumeEvery == 0)
            {
                // (the snapshot's CREATE_GAME starts the client's picture again; the old entities are kept here)
                var before = new Dictionary<int, ClientView.Ent>(view.Entities);
                openChoice = null;
                openOptions = null;
                label = $"reconnect at step {step}";
                if (!engine.Resume()) problems.Add("the engine can't resume");
                Take();
                CompareResume(engine, before, view.Entities, problems, $"step {step} turn {engine.Rules.Turn} reconnect");
            }
            if (openChoice != null)
            {
                var c = openChoice;
                openChoice = null;
                var pick = c.ChoiceType == (int)ChoiceType.MULLIGAN
                    ? c.Entities.Where(_ => rng.Next(2) == 0).ToList()
                    : new List<int> { c.Entities[rng.Next(c.Entities.Count)] };
                label = $"P1 answers choice {c.Id} ({(ChoiceType)c.ChoiceType}) with [{string.Join(",", pick)}]";
                if (!engine.ChooseEntities(c.Id, pick))
                {
                    problems.Add($"choice {c.Id} rejected ({engine.LastRejection})");
                    // (HS_SOAK_DUMP: the engine's recent history at a rejected choice too)
                    if (Environment.GetEnvironmentVariable("HS_SOAK_DUMP") is { Length: > 0 } dumpChoice && !File.Exists(dumpChoice))
                        File.WriteAllLines(dumpChoice, engine.Rules.PowerHistory.Full.Skip(Math.Max(0, engine.Rules.PowerHistory.Full.Count - 300))
                            .Select(e => e.Print().TrimEnd()).Prepend($"choice {c.Id} ({(ChoiceType)c.ChoiceType}) entities [{string.Join(",", c.Entities)}]").Prepend(problems[^1]));
                }
            }
            else if (engine.AiToAct)
            {
                engine.AiStep();
                label = engine.LastMove ?? "AI step";
            }
            else if (openOptions != null)
            {
                var o = openOptions;
                openOptions = null;
                // Mostly act (end turn 1 time in 6) so plays, attacks and hero powers all get exercised.
                // Like the client: options and targets that carry a PlayError are shown, not usable. Now and then try one
                // anyway — the server must refuse it (the error and the engine's moves have to agree).
                var errored = o.Options.Select((opt, i) => (opt, i)).Where(x => x.opt.MainOption?.PlayError > 0).ToList();
                if (errored.Count > 0 && rng.Next(8) == 0)
                {
                    var (bad, badIndex) = errored[rng.Next(errored.Count)];
                    if (engine.ChooseOption(o.Id, badIndex, 0, 1, -1))
                        { problems.Add($"option with PlayError {bad.MainOption.PlayError} was accepted: #{badIndex} entity {bad.MainOption.Id}"); continue; }
                }
                var actions = o.Options.Select((opt, i) => (opt, i))
                    .Where(x => x.opt.Type_ != Option.Type.END_TURN && (x.opt.MainOption?.PlayError ?? 0) == 0).ToList();
                var (option, index) = actions.Count > 0 && rng.Next(6) != 0 ? actions[rng.Next(actions.Count)] : (o.Options[0], 0);
                int sub = -1, target = 0;
                SubOption main = option.MainOption;
                if (option.SubOptions.Count > 0)
                {
                    // like the client: a sub-option with a PlayError (a Titan's used ability) is shown, not usable; now and then
                    // try one anyway — the server must refuse it
                    var usableSubs = Enumerable.Range(0, option.SubOptions.Count).Where(i => option.SubOptions[i].PlayError == 0).ToList();
                    var erroredSubs = Enumerable.Range(0, option.SubOptions.Count).Where(i => option.SubOptions[i].PlayError > 0).ToList();
                    if (erroredSubs.Count > 0 && rng.Next(8) == 0)
                    {
                        int badSub = erroredSubs[rng.Next(erroredSubs.Count)];
                        if (engine.ChooseOption(o.Id, index, 0, 1, badSub))
                            { problems.Add($"sub-option with PlayError {option.SubOptions[badSub].PlayError} was accepted: #{index} entity {option.MainOption?.Id} sub {badSub}"); continue; }
                    }
                    sub = usableSubs.Count > 0 ? usableSubs[rng.Next(usableSubs.Count)] : rng.Next(option.SubOptions.Count);
                    main = option.SubOptions[sub];
                }
                var targets = main?.Targets.Where(t => t.PlayError == 0).ToList();
                if (targets?.Count > 0) target = targets[rng.Next(targets.Count)].Id;
                var badTarget = main?.Targets.FirstOrDefault(t => t.PlayError > 0);
                if (badTarget != null && rng.Next(8) == 0 && engine.ChooseOption(o.Id, index, badTarget.Id, 1, sub))
                {
                    problems.Add($"target with PlayError {badTarget.PlayError} was accepted: #{index} entity {option.MainOption?.Id} target {badTarget.Id}");
                    continue;
                }
                int position = rng.Next(1, 8);
                label = option.Type_ == Option.Type.END_TURN ? "P1 END_TURN" : $"P1 option #{index} {(engine.Rules.IdEntityDic.TryGetValue(option.MainOption?.Id ?? 0, out IPlayable actor) ? actor.Card.Id : "?")} entity {option.MainOption?.Id} target {target} sub {sub}";
                if (!engine.ChooseOption(o.Id, index, target, position, sub))
                    problems.Add($"offered option rejected: #{index} {option.Type_} entity {option.MainOption?.Id} target {target} sub {sub}: {engine.LastRejection}");
                else if (option.Type_ != Option.Type.END_TURN) humanActions++;
            }
            else
            {
                var g = engine.Rules;
                problems.Add($"stuck: no options, no choice, AI not to act (step {g.Step}, state {g.State}, current P{g.CurrentPlayer?.PlayerId}, " +
                             $"P1 choice {g.Player1.Choice?.ChoiceType}, P2 choice {g.Player2.Choice?.ChoiceType}, P1 mulligan {g.Player1.MulliganState}, P2 mulligan {g.Player2.MulliganState})");
                break;
            }
            Take();
            // While a Discover is open the client sees the game as it was when the choice was offered (live keeps the
            // play's blocks open until the pick), so it is compared once the pick is shown.
            if (!engine.Rules.HoldingForChoice)
            {
                int before = problems.Count;
                Compare(engine.Rules, view, 1, problems, $"step {step} turn {engine.Rules.Turn} after {label}");
                if (lastPacket != null)
                    foreach (string p in problems.Skip(before))
                        Offending.TryAdd(p, "(the last packet before the check)\n" + HistoryText.Format(lastPacket.List,
                            id => view.Get(id) is { Known: true } e ? $"[{e.CardId}#{id}]" : $"#{id}", withTags: true));
                // Debugging aid: HS_SOAK_DUMP=<file> writes the engine's recent history at the first mismatch.
                if (problems.Count > before && Environment.GetEnvironmentVariable("HS_SOAK_DUMP") is { Length: > 0 } dump && !File.Exists(dump))
                    File.WriteAllLines(dump, engine.Rules.PowerHistory.Full.Skip(Math.Max(0, engine.Rules.PowerHistory.Full.Count - 250))
                        .Select(e => e.Print().TrimEnd()).Prepend(problems[^1]));
            }
        }
        if (!engine.Over && problems.Count == 0) problems.Add($"game not over after {maxSteps} steps");
        if (engine.EngineErrors > 0) problems.Add($"{engine.EngineErrors} card-script error(s) caught (see log)");
        return new Result(engine.Rules.Turn, packets, humanActions, engine.Over, problems);
    }

    /// <summary>Debugging aid for HS_SOAK_DUMP: what the engine knows of an "unknown entity N" (card, type, what it is on, creator).</summary>
    private static string DescribeUnknown(SabberEngineGame engine, string problem)
    {
        var m = System.Text.RegularExpressions.Regex.Match(problem, @"unknown entity (\d+)");
        if (!m.Success || !engine.Rules.IdEntityDic.TryGetValue(int.Parse(m.Groups[1].Value), out var e)) return "";
        string on = e is SabberStoneCore.Model.Entities.Enchantment en
            ? $" on #{en.Target?.Id} {(en.Target as SabberStoneCore.Model.Entities.IPlayable)?.Card.Id} creator #{en.Creator?.Id} {en.Creator?.Card.Id}" : "";
        return $"engine: #{e.Id} {e.Card.Id} {e.Card.Type} zone {e.Zone?.Type}{on}";
    }

    /// <summary>
    /// Live rule: an effect block (POWER / TRIGGER) that damages, heals or freezes characters itself (not in nested
    /// blocks) opens with a META_DATA TARGET naming each of them — the client's FX land the effect on impact from it.
    /// A repeated effect (Mad Bomber, Arcane Missiles) names each repeat's targets in its own TARGET before it instead.
    /// </summary>
    private static void CheckEffectTargets(List<PowerHistoryData> list, List<string> problems, Func<int, string> cardOf)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].PowerStart is not { Type: HistoryBlock.Type.POWER or HistoryBlock.Type.TRIGGER } start) continue;
            // a card live never opens with a TARGET (recorded: Loader.BlockMeta NOTARGET — Ice Lance, Rockbiter Weapon)
            if (SabberStoneCore.Loader.BlockMeta.TargetModeOf(cardOf(start.Source), (SabberStoneCore.Enums.BlockType)(int)start.Type, start.EffectIndex)
                == SabberStoneCore.Loader.BlockMeta.TargetMode.None) continue;
            // (with a SUB_SPELL the TARGET comes right after the first SUB_SPELL_START, as live; the card's own recorded
            // EFFECT_TIMING / OVERRIDE_HISTORY may come first: live Fan of Knives = EFFECT_TIMING, then TARGET)
            int at = i + 1;
            while (at < list.Count && list[at].MetaData is { Type: HistoryMeta.Type.EFFECT_TIMING or HistoryMeta.Type.OVERRIDE_HISTORY }) at++;
            if (at < list.Count && list[at].SubSpellStart != null) at++;
            while (at < list.Count && list[at].MetaData is { Type: HistoryMeta.Type.EFFECT_TIMING or HistoryMeta.Type.OVERRIDE_HISTORY }) at++;
            var named = at < list.Count && list[at].MetaData is { Type: HistoryMeta.Type.TARGET } m ? m.Info : new List<int>();
            // (an effect that first makes something — an enchantment on its target before its SUB_SPELL, Judge Unworthy — has
            // its opening TARGET further in: the block's first TARGET before anything it hits is that one)
            if (named.Count == 0)
                for (int j = i + 1, d0 = 0; j < list.Count; j++)
                {
                    PowerHistoryData d = list[j];
                    if (d.PowerStart != null) { d0++; continue; }
                    if (d.PowerEnd != null) { if (d0-- == 0) break; continue; }
                    if (d0 > 0) continue;
                    if (d.MetaData is { Type: HistoryMeta.Type.TARGET } first) { named = first.Info; at = j; break; }
                    if (d.MetaData is { Type: HistoryMeta.Type.DAMAGE or HistoryMeta.Type.HEALING } || d.TagChange is { Tag: (int)GAME_TAG.FROZEN, Value: 1 })
                        break;
                }
            // live (Explosive Shot, Howling Blast, Obliterate): a block with a chosen target names only it; what else it hits
            // is the card's client effect's (ClientStream.BlockTargets)
            if (start.Type == HistoryBlock.Type.POWER && start.Target > 0 && named.Count == 1 && named[0] == start.Target) continue;
            int depth = 0;
            for (int j = i + 1; j < list.Count; j++)
            {
                PowerHistoryData d = list[j];
                if (d.PowerStart != null) { depth++; continue; }
                if (d.PowerEnd != null) { if (depth-- == 0) break; continue; }
                if (depth > 0) continue;
                if (j > at && d.MetaData is { Type: HistoryMeta.Type.TARGET } round) { named = round.Info; continue; }
                var affected = d.MetaData is { Type: HistoryMeta.Type.DAMAGE or HistoryMeta.Type.HEALING } dm ? dm.Info
                    : d.TagChange is { Tag: (int)GAME_TAG.FROZEN, Value: 1 } t ? new List<int> { t.Entity } : null;
                foreach (int id in affected ?? new List<int>())
                    if (!named.Contains(id))
                        problems.Add($"{start.Type} block of entity {start.Source} affects {id} without naming it in its TARGET");
            }
        }
    }

    private static void CheckCardIds(List<PowerHistoryData> list, CardDb cards, List<string> problems)
    {
        foreach (PowerHistoryData d in list)
            foreach (string id in new[] { d.FullEntity?.Name, d.ShowEntity?.Name, d.ChangeEntity?.Name })
                if (!string.IsNullOrEmpty(id) && !cards.TryGet(id, out _))
                    problems.Add($"card id {id} is not in the client's card database");
    }

    /// <summary>
    /// The client's picture against the engine, as the viewer may see it (2026-10-09, after the Freezing Trap desync: the old check
    /// looked at zone / controller / stats only, so a Secret drawn as a class-less "?" and a bounced minion re-sent "in play at
    /// position 0" both passed): every card the client knows is the engine's card with the engine's identity tags; what is in play
    /// (not an enchantment) is known, what is in a hidden zone isn't; the hand and the board are at the engine's positions; a
    /// hidden Secret shows its class and kind; in play the stats and every tag that decides whether and how a character attacks.
    /// </summary>
    internal static void Compare(SabberStoneCore.Model.Game game, ClientView view, int viewer, List<string> problems, string when)
    {
        foreach (IPlayable e in game.IdEntityDic.Values)
        {
            if (e == null || e[GameTag.ZONE] == 0) continue;
            int id = e.Id;
            var zone = (Zone)e[GameTag.ZONE]; // (an enchantment's Zone object stays after it is removed: the tag is its zone)
            int controller = e[GameTag.CONTROLLER];
            ClientView.Ent s = view.Get(id);
            if (s == null)
            {
                // (an entity the history never named can't be somewhere public)
                if (zone is Zone.PLAY or Zone.HAND or Zone.SECRET)
                    problems.Add($"{when}: entity {id} {e.Card.Id} ({zone} of P{controller}) is not in the client's view");
                continue;
            }
            void Check(GameTag tag, int client, int server)
            {
                if (client != server) problems.Add($"{when}: entity {id} {e.Card.Id} ({zone}) {tag} client={client} server={server}");
            }
            Check(GameTag.ZONE, s[GAME_TAG.ZONE], (int)zone);
            Check(GameTag.CONTROLLER, s[GAME_TAG.CONTROLLER], controller);
            bool mine = controller == viewer;
            // (a REVEALED card is shown to both players — ClientStream.Visible; live: Void Scripture's pick, REVEALED + SHOW_BIG_CARD)
            bool hiddenZone = (zone == Zone.DECK || zone == Zone.HAND && !mine) && e[GameTag.REVEALED] == 0;
            bool secret = zone == Zone.SECRET && e[GameTag.SECRET] > 0;
            if (hiddenZone && s.Known) problems.Add($"{when}: entity {id} leaks card id {s.CardId} in {zone} of player {controller}");
            if (secret && !mine && s.Known) problems.Add($"{when}: entity {id} leaks the Secret {s.CardId} of player {controller}");
            // what is public must be known (live: everything in play, the viewer's own hand and Secrets)
            bool mustKnow = zone == Zone.PLAY && e is not SabberStoneCore.Model.Entities.Enchantment || mine && zone is Zone.HAND or Zone.SECRET;
            if (mustKnow && !s.Known) problems.Add($"{when}: entity {id} {e.Card.Id} in {zone} of player {controller} is hidden from the client");
            if (s.Known && s.CardId != e.Card.Id) problems.Add($"{when}: entity {id} is {e.Card.Id} but the client knows it as {s.CardId}");
            // a known card's identity: what the client draws it as (type, class, rarity, Secret / Quest kind)
            if (s.Known && s.CardId == e.Card.Id)
                foreach (GameTag tag in IdentityTags) Check(tag, s.Tags.GetValueOrDefault((int)tag), Own(e, tag));
            // a hidden Secret: the coloured "?" (live: SECRET / SIDE_QUEST, CLASS, COST of every hidden card entering the Secret zone)
            if (secret && !s.Known)
                foreach (GameTag tag in new[] { GameTag.SECRET, GameTag.CLASS }) Check(tag, s.Tags.GetValueOrDefault((int)tag), Own(e, tag));
            // where it sits: each hand and each board in the engine's order (the client lays them out by ZONE_POSITION)
            if (e.Zone?.Type == zone && (zone == Zone.HAND || zone == Zone.PLAY && e is Minion or Location))
                Check(GameTag.ZONE_POSITION, s[GAME_TAG.ZONE_POSITION], e.ZonePosition + 1);
            if (zone == Zone.PLAY && e is ICharacter c)
            {
                Check(GameTag.DAMAGE, s[GAME_TAG.DAMAGE], c.Damage);
                Check(GameTag.ATK, s[GAME_TAG.ATK], c.AttackDamage);
                Check(GameTag.HEALTH, s[GAME_TAG.HEALTH], c.BaseHealth);
                foreach (GameTag tag in KeywordTags) Check(tag, s.Tags.GetValueOrDefault((int)tag), e[tag]);
            }
            if (zone == Zone.PLAY && e is Weapon w)
            {
                Check(GameTag.ATK, s[GAME_TAG.ATK], w.AttackDamage);
                Check(GameTag.DURABILITY, s.Tags.GetValueOrDefault((int)GameTag.DURABILITY), w[GameTag.DURABILITY]);
                Check(GameTag.DAMAGE, s[GAME_TAG.DAMAGE], w.Damage);
            }
            if (zone == Zone.HAND && mine) Check(GameTag.COST, s[GAME_TAG.COST], e.Cost);
            if (zone == Zone.PLAY) Check(GameTag.EXHAUSTED, s[GAME_TAG.EXHAUSTED], e[GameTag.EXHAUSTED]);
            if (problems.Count > 20) return;
        }
        // Every entity a zone holds must know it is there (a mismatch breaks later moves: ZoneException).
        foreach (Controller p in new[] { game.Player1, game.Player2 })
            foreach (var (zone, items) in new (SabberStoneCore.Model.Zones.IZone, IEnumerable<IPlayable>)[]
                     { (p.DeckZone, p.DeckZone), (p.HandZone, p.HandZone), (p.BoardZone, p.BoardZone), (p.GraveyardZone, p.GraveyardZone),
                       (p.SecretZone, p.SecretZone), (p.SetasideZone, p.SetasideZone) })
                foreach (IPlayable e in items)
                    if (e.Zone != zone || e.Controller != p)
                        problems.Add($"{when}: {e.Card.Id} #{e.Id} is in P{p.PlayerId} {zone.Type} but its zone is {e.Zone?.Type}{(e.Zone == zone ? "" : " (another)")}, controller P{e.Controller?.PlayerId}");
        // Hero powers must be scripted (a hero skin's power without a script is offered with no targets and does nothing).
        // Starting heroes and skins only: a played hero card from a set not scripted yet is a placeholder like its set.
        foreach (Controller p in new[] { game.Player1, game.Player2 })
            if (p.Hero?.HeroPower is { } hp && hp.Card.Power == null && p.Hero.Card.Id.StartsWith("HERO_", StringComparison.Ordinal))
                problems.Add($"{when}: hero power {hp.Card.Id} of {p.Hero.Card.Id} has no script");
        // Players: the client's mana display.
        foreach (Controller p in new[] { game.Player1, game.Player2 })
            if (view.Get(p.Id) is { } ps)
                foreach (GameTag tag in new[] { GameTag.RESOURCES, GameTag.RESOURCES_USED, GameTag.TEMP_RESOURCES, GameTag.OVERLOAD_LOCKED })
                    if (ps.Tags.GetValueOrDefault((int)tag) != p[tag])
                        problems.Add($"{when}: player {p.PlayerId} {tag} client={ps.Tags.GetValueOrDefault((int)tag)} server={p[tag]}");
    }

    // The snapshot (FULL_ENTITY of everything) against what the client knew before the reconnect: the same card ids, every
    // tag of a known entity, and where a hidden one is (live's hidden FULL_ENTITY: ZONE, CONTROLLER, a hand card's
    // ZONE_POSITION).
    private static void CompareResume(SabberEngineGame engine, Dictionary<int, ClientView.Ent> before, Dictionary<int, ClientView.Ent> now, List<string> problems, string when)
    {
        foreach (var (id, was) in before)
        {
            // (a removed entity of a timeline a Rewind discarded: the client keeps it, removed and unseen, the engine has it no more and
            // the snapshot, made from the engine, doesn't send it; nothing the player can see)
            if (!now.ContainsKey(id) && was.Zone == (int)TAG_ZONE.REMOVEDFROMGAME && !engine.Rules.IdEntityDic.ContainsKey(id)) continue;
            if (!now.TryGetValue(id, out ClientView.Ent s)) { problems.Add($"{when}: entity {id} ({was.CardId}) in {(TAG_ZONE)was.Zone} missing from the snapshot (engine: {(engine.Rules.IdEntityDic.TryGetValue(id, out var gone) ? gone.Zone?.Type.ToString() ?? "no zone" : "none")})"); continue; }
            if (s.CardId != was.CardId) problems.Add($"{when}: entity {id} card id {s.CardId} (was {was.CardId})");
            bool known = was.CardId != "" || !engine.Rules.IdEntityDic.ContainsKey(id); // game and player entities: known
            IEnumerable<int> tags = known
                ? was.Tags.Keys.Union(s.Tags.Keys)
                : new[] { (int)GAME_TAG.ZONE, (int)GAME_TAG.CONTROLLER }.Concat(was[GAME_TAG.ZONE] == (int)TAG_ZONE.HAND ? new[] { (int)GAME_TAG.ZONE_POSITION } : Array.Empty<int>());
            foreach (int tag in tags)
                if (s.Tags.GetValueOrDefault(tag) != was.Tags.GetValueOrDefault(tag))
                    problems.Add($"{when}: entity {id} {(was.CardId == "" ? "" : was.CardId + " ")}tag {(GAME_TAG)tag} snapshot={s.Tags.GetValueOrDefault(tag)} before={was.Tags.GetValueOrDefault(tag)}");
            if (problems.Count > 20) return;
        }
        foreach (int id in now.Keys.Except(before.Keys)) problems.Add($"{when}: snapshot has entity {id} the client never saw");
    }

    // the entity's own value of a tag (its changes, else its card's; an enchantment's indexer reads only its changes)
    private static int Own(IPlayable e, GameTag tag) =>
        e.NativeTags.TryGetValue(tag, out int v) ? v : e.Card.Tags.TryGetValue(tag, out int c) ? c : 0;

    private static readonly GameTag[] KeywordTags =
    {
        GameTag.FROZEN, GameTag.TAUNT, GameTag.DIVINE_SHIELD, GameTag.STEALTH, GameTag.WINDFURY, GameTag.CHARGE,
        GameTag.SILENCED, GameTag.POISONOUS, GameTag.LIFESTEAL, GameTag.IMMUNE, GameTag.SPELLPOWER, GameTag.ARMOR,
        GameTag.NUM_ATTACKS_THIS_TURN, GameTag.RUSH, GameTag.CANT_ATTACK, GameTag.JUST_PLAYED, GameTag.EXTRA_ATTACKS_THIS_TURN,
        GameTag.DORMANT,
    };

    // what the client draws a known card as: its type, class, rarity and kind of Secret / Quest (CLASS=INVALID drew Freezing Trap
    // as a Mage Secret; no CARDTYPE = an empty card)
    private static readonly GameTag[] IdentityTags =
    {
        GameTag.CARDTYPE, GameTag.CLASS, GameTag.MULTIPLE_CLASSES, GameTag.RARITY, GameTag.SECRET, GameTag.QUEST, (GameTag)(int)GAME_TAG.SIDE_QUEST,
    };

    private static CardSet[] SoakSets() =>
        (Environment.GetEnvironmentVariable("HS_SOAK_SETS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(n => Enum.TryParse(n, true, out CardSet set) ? set : throw new ArgumentException($"HS_SOAK_SETS: unknown set '{n}'"))
        .ToArray();

    // What a deck sideboard may hold (the recorded subsets of the newest login), for the random decks' sideboards; null
    // without a recording (the random sideboards then come from every collectible card).
    private static readonly Lazy<Lobby.SideboardRules> SideboardRules = new(() =>
    {
        try
        {
            string root = Program.ProjectRoot();
            string login = Lobby.RecordedAccount.FindReferenceSession(Path.Combine(root, "Hearthstone main game", "BepInEx", "HsRecorder"),
                Path.Combine(root, "RESEARCH", "captures"));
            return new Lobby.SideboardRules(CardDb.Load(CardDb.FindDefaultDir(root)), login == null ? Array.Empty<string>() : new[] { login });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    });

    /// <summary>A random full sideboard for each sideboard card in a random deck (E.T.C.'s band, Zilliax's modules,
    /// Beatrix's minion, King's Beasts), as the deck builder would have them: (owner, card) ids. No random draw without one.</summary>
    internal static List<(string Owner, string Card)> RandomSideboard(CardDb cards, List<string> deck, CardClass cardClass, Random rng)
    {
        var dbfs = deck.Select(id => cards.TryGet(id, out var c) ? c.DbfId : 0).Where(d => d != 0).ToList();
        if (!dbfs.Any(d => cards.ByDbf(d).Tag(GAME_TAG.SIDEBOARD_TYPE) != 0)) return new List<(string Owner, string Card)>();
        var rules = SideboardRules.Value ?? new Lobby.SideboardRules(cards, Array.Empty<string>());
        return rules.RandomFill(dbfs, (int)cardClass, rng).Select(s => (Owner: cards.ByDbf(s.Owner).CardId, Card: cards.ByDbf(s.Card).CardId)).ToList();
    }

    /// <summary>A game between two random Basic+Classic decks (random classes), human = player 1.</summary>
    public static SabberEngineGame RandomGame(Random rng, CardDb cards)
    {
        var c1 = RandomDecks.Classes[rng.Next(RandomDecks.Classes.Length)];
        var c2 = RandomDecks.Classes[rng.Next(RandomDecks.Classes.Length)];
        int kind = rng.Next(3); // a third each: Basic + Classic, current Core, Wild (every set)
        // HS_SOAK_SETS=GVG[,BRM...]: every deck from those sets (+ their class's neutrals of the same sets), for a set pass
        CardSet[] only = SoakSets();
        PlayerSetup WithSideboard(PlayerSetup player, CardClass c) { player.Sideboard = RandomSideboard(cards, player.Deck, c, rng); return player; }
        List<string> Deck(CardClass c) => only.Length > 0 ? RandomDecks.Build(c, rng, only)
            : kind == 0 ? RandomDecks.Build(c, rng, CardSet.CORE, CardSet.EXPERT1)
            : kind == 1 ? RandomDecks.BuildCurrentCore(c, rng) : RandomDecks.BuildWild(c, rng);
        return new SabberEngineGame(new EngineSetup
        {
            Cards = cards, Scenario = 260, Seed = rng.Next(), Rules = RulesEngine.Sabber,
            // a random expansion board, as live (Transfer Student's versions get played too)
            GameSetupBody = PegasusPackets.Serialize(new GameSetup { Board = Boards.Expansion[rng.Next(Boards.Expansion.Length)], MaxFriendlyMinionsPerPlayer = 7 }),
            Human = WithSideboard(new PlayerSetup { HeroCardId = RandomDecks.AnyHero(c1, rng), Deck = Deck(c1), AccountHi = 1, AccountLo = 2 }, c1),
            Opponent = WithSideboard(new PlayerSetup { HeroCardId = RandomDecks.AnyHero(c2, rng), Deck = Deck(c2), IsAi = true }, c2),
        });
    }
}
