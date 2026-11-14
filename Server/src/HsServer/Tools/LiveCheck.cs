using System.Diagnostics;
using System.Text;
using HsServer.Protocol;
using PegasusGame;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace HsServer.Tools;

/// <summary>
/// `HsServer livecheck [--card ID] [--set NAME] [--kind play|attack|end-turn] [--limit N] [--session FILE] [--line N] [--captures DIR]
/// [--verbose] [--detail] [--out DIR] [--no-report]` (2026-10-09, user: "functionality has to be right first on everything"): a rules regression
/// check of our engine against LIVE recordings. Every player action of every live game — a card played, a Hero Power, a Location, a
/// trade / Prepare / Forge, a Titan ability, an attack, and each turn's end (MAIN_END up to the next MAIN_ACTION: end- and start-of-turn
/// triggers, the draw, choices made there) — becomes a test:
///   1. the live game is followed tag by tag (LiveCheck.Recording.cs); just before the action the position is taken: every entity's
///      tags, the game's history so far (cards played, died, summoned, added to hand), the choices made inside the action;
///   2. the position is rebuilt in a new engine game (LiveCheck.Engine.cs): heroes and Hero Powers by the config, every card by
///      Entity.FromCard into its zone (the cards that started in a deck before Game.FirstGeneratedEntityId, the rest after — "didn't
///      start in your deck" reads as live), live's tags, live's enchantments (not auras': ours make their own), the engine's histories,
///      then Attack / Health / Damage / Durability / Cost fitted to what live showed. A card the viewer never saw is its later reveal,
///      else a plain placeholder; what live took from a deck during the action is put on top, in order;
///   3. the same action is done (same target, position, Choose One option / Titan ability, Discover picks) and the outcomes are
///      compared per entity (LiveCheck.Compare.cs): zones, owners, transforms, Attack / Health / Damage / Armor / Durability changes,
///      keywords, hand costs, the new cards (where and which; a cosmetic version counts as the card), Mana / Overload / Corpses.
/// Each action is MATCH, MATCH-RANDOM (one of a few seeds / pick variants gives live's outcome, or the same shape when ours is random),
/// DIFFERS (ours deterministic and not live's: the first difference), DIFFERS-RANDOM, UNSURE (only numbers the rebuild had to force
/// differ; or a card that acted reads an engine counter the rebuild can't fill, or a hand / deck the viewer never saw), REFUSED / ERROR
/// (ours refused the action / threw), UNSCRIPTED (a card that acted has no script in ours) or NOT-REBUILT (why: a hidden card, an entity
/// live had that ours can't, two Quests up ...). The tool pass of 2026-10-10 (FINDINGS.md): the rebuild keeps the discounts it makes
/// again (Mug's Magic), Rush / Charge / cut-short attacks, untouchable permanents, captured cards and script-data entities, hidden
/// enchantments, live's order of play, the engine's hand / hero-damage / Elemental counters, starting decks, Rewind resets. Writes RESEARCH/livecheck/last-run.md (counts per kind and expansion, then each differing card with its first
/// difference, session and line in `HsServer power &lt;session&gt;`) and RESEARCH/livecheck/results.tsv (every action). `--line N` (with
/// --session) prints one action's outcomes and our engine's history. Sessions: the live install's HsRecorder sessions,
/// RESEARCH/captures/**/session-*.jsonl (not local-test-*: ours; hsreplay-* imports count) and --captures DIRs.
/// </summary>
internal static partial class LiveCheck
{
    private const string LiveRecorder = @"C:\Program Files (x86)\Hearthstone\BepInEx\HsRecorder";
    private const int Seeds = 4;

    /// <summary>Client zone 9: cosmetic entities (a player's pet: PET_*, CARDTYPE 45) — they react to the game, never change it.</summary>
    private const int CosmeticZone = 9;

    internal sealed record Result(LiveAction Action, string Kind, string Card, string Set, string Status, string Detail, string Notes, string Acting = "")
    {
        /// <summary>The board order after the action where a minion or Location came into play (BoardOrder; "" none): not part of
        /// the status — rules compare what is on the board, this compares where (2026-10-10, summon placement pass).</summary>
        public string Board { get; init; } = "";
    }

    internal sealed class Options
    {
        public string Card, Set, Kind = "all";
        public int Limit = int.MaxValue;
        public readonly List<string> Sessions = new(), Roots = new();
        public bool Verbose, NoReport, Detail;
        public int Line;
        public string Out;
    }

    public static int Run(string[] args)
    {
        string root = Program.ProjectRoot();
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--card" when i + 1 < args.Length: o.Card = args[++i]; break;
                case "--set" when i + 1 < args.Length: o.Set = args[++i]; break;
                case "--limit" when i + 1 < args.Length: o.Limit = int.Parse(args[++i]); break;
                case "--session" when i + 1 < args.Length: o.Sessions.Add(args[++i]); break;
                case "--captures" when i + 1 < args.Length: o.Roots.Add(args[++i]); break;
                case "--kind" when i + 1 < args.Length: o.Kind = args[++i]; break;
                case "--out" when i + 1 < args.Length: o.Out = args[++i]; break;
                case "--verbose": o.Verbose = true; break;
                case "--line" when i + 1 < args.Length: o.Line = int.Parse(args[++i]); break;
                case "--no-report": o.NoReport = true; break;
                case "--detail": o.Detail = true; break;
                default:
                    Console.Error.WriteLine("usage: HsServer livecheck [--card ID] [--set NAME] [--kind play|attack|end-turn] [--limit N] [--session FILE] [--line N] [--captures DIR] [--verbose] [--detail] [--out DIR] [--no-report]");
                    return 1;
            }
        }
        Sabber.SabberEngineGame.EnsureCards(Engine.CardDb.FindDefaultDir(root));
        ClientDb ??= Engine.CardDb.Load(Engine.CardDb.FindDefaultDir(root));
        _ = SabberStoneCore.Loader.ReprintCards.CanonicalOf;
        _ = Cards.All.Count();

        List<string> sessions = o.Sessions.Count > 0
            ? o.Sessions.Select(s => File.Exists(s) ? s : FindSessions(root, o.Roots).FirstOrDefault(f => Path.GetFileName(f).Contains(s, StringComparison.OrdinalIgnoreCase)))
                .Where(s => s != null).ToList()
            : FindSessions(root, o.Roots);
        Console.Error.WriteLine($"livecheck: {sessions.Count} live sessions");

        var watch = Stopwatch.StartNew();
        var results = new List<Result>();
        int seen = 0;
        foreach (string session in sessions)
        {
            if (seen >= o.Limit) break;
            try
            {
                ReadActions(session, a =>
                {
                    if (seen >= o.Limit) return;
                    Result res = Check(a, o);
                    if (res == null) return;
                    seen++;
                    results.Add(res);
                    if (o.Verbose)
                        Console.WriteLine($"{res.Status}\t{res.Card}\t{res.Kind}\t{a.Session}:{a.Line}\t{res.Detail}{(res.Notes.Length > 0 ? "\t[" + res.Notes + "]" : "")}");
                });
            }
            catch (Exception ex) { Console.Error.WriteLine($"{Path.GetFileName(session)}: {ex.GetType().Name}: {ex.Message}"); }
        }
        Console.Error.WriteLine($"{results.Count} actions in {watch.Elapsed.TotalSeconds:0}s: " +
                                string.Join(", ", results.GroupBy(r => r.Status).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}")));
        if (!o.NoReport) Report(results, o.Out ?? Path.Combine(root, "RESEARCH", "livecheck"), o);
        return 0;
    }

    /// <summary>Live sessions: the live install's recorder folder, RESEARCH/captures and extra roots (never our own server's).</summary>
    private static List<string> FindSessions(string root, List<string> extra) =>
        Recording.FindSessions(new[] { LiveRecorder, Path.Combine(root, "RESEARCH", "captures") }.Concat(extra).ToArray())
            .Where(f => !f.Replace('\\', '/').Contains("/local-test", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string KindOf(LiveAction a)
    {
        if (a.EndTurn) return "end-turn";
        if (a.Block == HistoryBlock.Type.ATTACK) return "attack";
        if (a.Block == HistoryBlock.Type.DECK_ACTION) return "trade";
        LiveEntity e = a.Before.GetValueOrDefault(a.Source);
        int zone = e?[GameTag.ZONE] ?? 0, type = e?[GameTag.CARDTYPE] ?? 0;
        if (type == (int)CardType.HERO_POWER) return "hero-power";
        if (zone == (int)SabberStoneCore.Enums.Zone.PLAY && type == (int)CardType.LOCATION) return "location";
        if (zone == (int)SabberStoneCore.Enums.Zone.HAND) return "play";
        return "other";
    }

    internal static string SetName(Card c) =>
        c == null ? "?" : Enum.IsDefined(typeof(Engine.TAG_CARD_SET), (int)c.Set) ? ((Engine.TAG_CARD_SET)(int)c.Set).ToString() : ((int)c.Set).ToString();

    /// <summary>One live action checked (null: filtered out).</summary>
    internal static Result Check(LiveAction a, Options o = null)
    {
        var detail = new StringWriter();
        Result res = CheckOne(a, o, detail);
        // a difference where a card that acted has no script in ours: a card to script, not a rule to fix (kept apart)
        if (res != null && res.Status is "DIFFERS" or "DIFFERS-RANDOM" or "UNSURE" or "REFUSED" or "ERROR" && UnscriptedActing(a) is { Count: > 0 } unscripted)
            res = res with { Status = "UNSCRIPTED", Detail = $"no script in ours: {string.Join(", ", unscripted)} — {res.Status}: {res.Detail}" };
        // a deterministic difference where a card that acted reads a hand or deck the viewer never saw (rebuilt with placeholder minions)
        else if (res != null && res.Status == "DIFFERS" && HiddenCardsRead(a) is string hidden)
            res = res with { Status = "UNSURE", Detail = $"reads {hidden} — {res.Detail}" };
        // a deterministic difference where a card that acted reads an engine counter the rebuild couldn't fill (LiveCheck.Counters)
        else if (res != null && res.Status == "DIFFERS" && UnfilledCountersActing(a) is { Count: > 0 } counters)
            res = res with { Status = "UNSURE", Detail = $"reads engine counters the rebuild can't fill: {string.Join(", ", counters.Take(4))} — {res.Detail}" };
        // (--line: always; --detail: every action that isn't a match)
        if (res != null && (o?.Line > 0 || o?.Detail == true && !res.Status.StartsWith("MATCH", StringComparison.Ordinal)))
        {
            if (o.Line == 0) Console.WriteLine($"##### {res.Status} {res.Card} {res.Kind} {a.Session}:{a.Line}: {res.Detail}");
            Console.Write(detail.ToString());
        }
        return res;
    }

    /// <summary>The cards that acted in the action (its source, every block's source, an aura in play) that have text but no script in
    /// ours.</summary>
    private static List<string> UnscriptedActing(LiveAction a)
    {
        var ids = new List<int>();
        if (!a.EndTurn) ids.Add(a.Source);
        ids.AddRange(a.BlockSources);
        var list = new List<string>();
        foreach (int id in ids.Distinct())
        {
            LiveEntity e = a.Before.GetValueOrDefault(id) ?? a.After.GetValueOrDefault(id);
            if (e?[GameTag.CARDTYPE] is (int)CardType.GAME or (int)CardType.PLAYER) continue;
            if (CardFor(a.CardOf(id)) is Card c && Unscripted(c) && !list.Contains(c.Id)) list.Add(c.Id);
        }
        // (and an aura in play with no script: it acts without a block — Red Herring's Stealth)
        foreach (LiveEntity e in a.Before.Values)
            if (e[GameTag.ZONE] == (int)SabberStoneCore.Enums.Zone.PLAY && CardFor(a.CardOf(e.Id)) is Card c && c[GameTag.AURA] > 0 && Unscripted(c) && !list.Contains(c.Id))
                list.Add(c.Id);
        return list;
    }

    private static readonly System.Text.RegularExpressions.Regex DrawsAKind =
        new(@"\b(draw|draws|discover|discovers)\s+(a|an|one|two|three|four|five|\d+)\s+(?!cards?\b)\w+", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>What hidden cards a card that acted reads ("holding a Dragon", "draw a spell", "your deck"), when the hand / deck it
    /// reads has cards the viewer never saw (the rebuild's placeholders: plain minions); null when none.</summary>
    private static string HiddenCardsRead(LiveAction a)
    {
        bool Hidden(LiveEntity e) => !e.Visible && a.CardOf(e.Id).Length == 0;
        bool HiddenIn(int zone, int player) =>
            a.Before.Values.Any(e => e[GameTag.ZONE] == zone && e[GameTag.CONTROLLER] == player && Hidden(e));
        var ids = new List<int>();
        if (!a.EndTurn) ids.Add(a.Source);
        ids.AddRange(a.BlockSources);
        foreach (int id in ids.Distinct())
        {
            LiveEntity e = a.Before.GetValueOrDefault(id) ?? a.After.GetValueOrDefault(id);
            if (e == null || CardFor(a.CardOf(id)) is not Card c) continue;
            int own = e[GameTag.CONTROLLER], opp = own == 1 ? 2 : 1;
            // ("didn't start in your deck" is where a card came from — the rebuild keeps it — not what the deck holds)
            string text = System.Text.RegularExpressions.Regex.Replace(c.Text ?? "", "<[^>]*>", "").ToLowerInvariant()
                .Replace("start in your deck", "").Replace("started in your deck", "");
            bool theirs = text.Contains("opponent's") || text.Contains("their hand") || text.Contains("their deck") || text.Contains("enemy hand") || text.Contains("enemy deck");
            bool reads = text.Contains("holding") || text.Contains("in your hand") || text.Contains("from your hand") || text.Contains("cards in hand") ||
                         text.Contains("opponent's hand") || text.Contains("their hand") || text.Contains("enemy hand");
            if (reads && (HiddenIn((int)SabberStoneCore.Enums.Zone.HAND, own) || theirs && HiddenIn((int)SabberStoneCore.Enums.Zone.HAND, opp)))
                return $"a hand with hidden cards ({c.Id})";
            // (what a deck holds, not what goes into it: "draw a spell", "summon a minion from your deck", "if your deck has no ...", Dredge)
            bool readsDeck = text.Contains("from your deck") || text.Contains("in your deck") || text.Contains("your deck has") || text.Contains("deck has no") ||
                             text.Contains("from their deck") || text.Contains("in their deck") || text.Contains("from your opponent's deck") ||
                             text.Contains("dredge") || DrawsAKind.IsMatch(text);
            if (readsDeck &&
                (HiddenIn((int)SabberStoneCore.Enums.Zone.DECK, own) || theirs && HiddenIn((int)SabberStoneCore.Enums.Zone.DECK, opp)))
                return $"a deck with hidden cards ({c.Id})";
        }
        return null;
    }

    /// <summary>A card with rules text that our engine has no script for (CardCoverage's test).</summary>
    internal static bool Unscripted(Card c) =>
        c != null && !CardCoverage.IsImplemented(c) && !string.IsNullOrWhiteSpace(System.Text.RegularExpressions.Regex.Replace(c.Text ?? "", @"<[^>]*>|\[x\]", ""));

    private static Result CheckOne(LiveAction a, Options o, TextWriter log)
    {
        string kind = KindOf(a);
        string card = a.EndTurn ? "END_TURN" : a.CardOf(a.Source);
        // the cards that acted in it (a turn's end: its triggers)
        string acting = string.Join(",", a.BlockSources.Where(id => a.Before.GetValueOrDefault(id)?[GameTag.CARDTYPE] is not ((int)CardType.GAME or (int)CardType.PLAYER))
            .Select(id => a.CardOf(id)).Where(c => c.Length > 0 && c != card).Distinct().Take(8));
        Card c = a.EndTurn ? null : CardFor(card);
        string set = a.EndTurn ? "turn" : SetName(c);
        if (o != null)
        {
            if (o.Kind != "all" && o.Kind != kind && !(o.Kind == "play" && kind is "hero-power" or "location" or "trade" or "other")) return null;
            if (o.Line > 0 && a.Line != o.Line) return null;
            if (o.Card != null && !string.Equals(card, o.Card, StringComparison.OrdinalIgnoreCase) && !string.Equals(Canon(card), Canon(o.Card), StringComparison.OrdinalIgnoreCase) &&
                !acting.Split(',').Any(x => string.Equals(x, o.Card, StringComparison.OrdinalIgnoreCase))) return null;
            if (o.Set != null && !string.Equals(set, o.Set, StringComparison.OrdinalIgnoreCase)) return null;
        }
        string board = "";
        Result R(string status, string detail, string notes = "") => new(a, kind, card.Length > 0 ? card : "hidden", set, status, detail, notes, acting) { Board = board };
        if (card.Length == 0) return R("NOT-REBUILT", "hidden card (never shown to the viewer)");
        if (c == null && !a.EndTurn) return R("NOT-REBUILT", $"card unknown to our card data: {card}");

        LiveEntity actorPlayer = a.Before.Values.FirstOrDefault(e => e[GameTag.CARDTYPE] == (int)CardType.PLAYER && e[GameTag.CURRENT_PLAYER] == 1);
        if (actorPlayer == null) return R("NOT-REBUILT", "no current player");
        Outcome live = LiveOutcome(a, actorPlayer[GameTag.PLAYER_ID]);

        string firstDiff = null, firstSig = null, notes = "";
        List<string> firstShape = null, liveShape = null;
        bool random = false, variantsTried = false, firstForced = false;
        var signatures = new HashSet<string>();
        int chooseOne = 0;
        var variants = new List<(long Seed, int ChooseOne, int Pick)> { (1, int.MinValue, 0) };
        for (int v = 0; v < variants.Count; v++)
        {
            var (seed, co, pickVariant) = variants[v];
            Rebuilt r;
            try { r = Rebuild(a, seed); }
            catch (NotRebuilt nr) { return R("NOT-REBUILT", nr.Message); }
            catch (Exception ex) { return R("NOT-REBUILT", $"rebuild error {ex.GetType().Name}: {ex.Message}", Where(ex)); }
            Dictionary<int, ESnap> before = OurBefore(r);
            if (v == 0)
            {
                var minor = new List<string>();
                List<string> mismatch = RebuildMismatches(a, r, before, minor);
                if (mismatch.Count > 0) return R("NOT-REBUILT", "rebuild: " + mismatch[0], string.Join("; ", mismatch.Skip(1).Take(3)));
                notes = string.Join("; ", r.Notes.Concat(minor.Take(3)).Distinct());
                // live had an effect of something our position hasn't (a hidden Heist treasure, an unknown secret ...); a block that only
                // changed its own entity (a Shattered half's counter, an enchantment removing itself) is no effect
                foreach (int src in a.EffectiveSources)
                    if (src > 0 && src <= a.MaxIdBefore && !r.LiveToOurs.ContainsKey(src) && !r.AuraMade.Contains(src) && a.Before.TryGetValue(src, out LiveEntity missing) &&
                        missing[GameTag.CARDTYPE] is not ((int)CardType.GAME or (int)CardType.PLAYER) && missing[GameTag.ZONE] != CosmeticZone)
                        return R("NOT-REBUILT", $"live: {(a.CardOf(src) is { Length: > 0 } mc ? mc : "a hidden entity")} ({ZoneName(missing[GameTag.ZONE])}) acted, not in our position", notes);
                chooseOne = ChooseOneOf(a, r);
                if (chooseOne < 0)
                {
                    Card[] options = Cards.ChooseOneOptions(CardFor(card));
                    int n = options?.Length ?? 0;
                    variants[0] = (1, 1, 0);
                    for (int k = 2; k <= n + 1; k++) variants.Add((1, k, 0));
                    co = 1;
                }
                else co = chooseOne;
                variants[0] = (seed, co, pickVariant);
            }
            var counters = OurCounters(r);
            var randomPicks = new List<string>();
            int historyMark = r.G.PowerHistory.Full.Count;
            string failed = Perform(a, r, co, pickVariant, randomPicks);
            if (failed != null)
            {
                if (v > 0) continue;
                if (o?.Line > 0 || o?.Detail == true)
                {
                    log.WriteLine($"== variant 0: {failed}");
                    if (r.LiveToOurs.TryGetValue(a.Source, out int sid) && r.G.IdEntityDic.TryGetValue(sid, out IPlayable sp))
                        log.WriteLine($"  source #{a.Source} -> ours #{sid} {sp.Card.Id}: before {before.GetValueOrDefault(a.Source)} after {OurSnap(sp)}");
                    foreach (var e in r.G.PowerHistory.Full.Skip(historyMark))
                        log.WriteLine("    " + e.Print().TrimEnd().Replace("\n", "\n    "));
                }
                if (failed.StartsWith("NR:", StringComparison.Ordinal)) return R("NOT-REBUILT", failed.Substring(3), notes);
                if (failed.StartsWith("ERROR", StringComparison.Ordinal)) return R("ERROR", "ours threw: " + failed.Substring(6), notes);
                return R("REFUSED", failed.StartsWith("REFUSED:", StringComparison.Ordinal) ? "ours refused the action live did: " + failed.Substring(8) : "ours refused live's pick", notes);
            }
            Outcome ours = OurOutcome(r, before);
            AddCounters(ours, r, counters);
            if (v == 0) board = BoardOrder(a, r);
            List<string> diffs = Differences(a, live, ours, r, out bool forcedOnly);
            if (o?.Line > 0 || o?.Detail == true)
            {
                log.WriteLine($"== variant {v} (seed {seed}, choose one {co}, pick {pickVariant}); random picks: {string.Join(",", randomPicks)}; notes: {string.Join("; ", r.Notes)}");
                log.WriteLine("fitted: " + string.Join(",", r.Fitted.Select(f => f.Item1 + ":" + f.Item2)));
                if (v == 0)
                    foreach (var (side, bc) in new[] { ("own", r.Actor), ("opp", r.Actor.Opponent) })
                        log.WriteLine($"board {side} (before, live ids by position): ours [{string.Join(" ", before.Where(kv => kv.Value.Zone == (int)SabberStoneCore.Enums.Zone.PLAY && kv.Value.Ctrl == bc.PlayerId && kv.Value.Type is (int)CardType.MINION or (int)CardType.LOCATION).Select(kv => kv.Key).OrderBy(id => r.G.IdEntityDic[r.LiveToOurs[id]].ZonePosition))}]" +
                                      $" live [{string.Join(" ", a.Before.Values.Where(e => e[GameTag.ZONE] == (int)SabberStoneCore.Enums.Zone.PLAY && e[GameTag.CONTROLLER] == bc.PlayerId && e[GameTag.CARDTYPE] is (int)CardType.MINION or (int)CardType.LOCATION).OrderBy(e => e[GameTag.ZONE_POSITION]).Select(e => e.Id))}]");
                log.WriteLine("live: " + live.Signature());
                log.WriteLine("ours: " + ours.Signature());
                foreach (string d in diffs) log.WriteLine("  " + d);
                foreach (int id in live.Changes.Keys.Union(ours.Changes.Keys))
                    if (r.LiveToOurs.TryGetValue(id, out int oid) && r.G.IdEntityDic.TryGetValue(oid, out IPlayable op))
                        log.WriteLine($"  #{id} -> ours #{oid} {op.Card.Id}: before {before.GetValueOrDefault(id)} after {OurSnap(op)}" +
                                      (op.AppliedEnchantments?.Count > 0 ? " enchantments " + string.Join(",", op.AppliedEnchantments.Select(x => x.Card.Id)) : ""));
                if (v == 0)
                    foreach (var e in r.G.PowerHistory.Full.Skip(historyMark))
                        log.WriteLine("    " + e.Print().TrimEnd().Replace("\n", "\n    "));
            }
            if (diffs.Count == 0)
                return R(v == 0 && randomPicks.Count == 0 ? "MATCH" : "MATCH-RANDOM", v == 0 ? "" : $"variant {v}", notes);
            if (randomPicks.Count > 0) random = true;
            signatures.Add(ours.Signature());
            if (v == 0)
            {
                firstForced = forcedOnly;
                firstDiff = diffs[0] + (diffs.Count > 1 ? $" (+{diffs.Count - 1} more)" : "");
                firstSig = ours.Signature();
                firstShape = Shape(ours, a, r);
                liveShape = Shape(live, a, r);
            }
            if (!variantsTried)
            {
                variantsTried = true;
                for (long s = 2; s <= Seeds; s++) variants.Add((s, co, 0));
                if (randomPicks.Count > 0)
                    for (int p = 1; p < 3; p++) variants.Add((1, co, p));
            }
        }
        if (signatures.Count > 1) random = true;
        if (random)
        {
            if (firstShape.SequenceEqual(liveShape)) return R("MATCH-RANDOM", "random: same shape (" + firstDiff + ")", notes);
            return R("DIFFERS-RANDOM", firstDiff, notes);
        }
        if (firstForced) return R("UNSURE", "only numbers the rebuild had to force: " + firstDiff, notes);
        return R("DIFFERS", firstDiff, notes);
    }

    /// <summary>Where the minions and Locations stand after the action, live and ours (our first variant), on each side where one came
    /// into play: "same", "order" (the same slots for the new ones, but not the same new card in each: which came first, or a random
    /// card), "placement" (a new one in another slot) or "content" (not the same minions: a rules difference), with both boards. A minion on the board before is its live id (#n), a new
    /// one its card (+ID). "" when nothing came into play.</summary>
    private static string BoardOrder(LiveAction a, Rebuilt r)
    {
        var parts = new List<string>();
        foreach (var (side, bc) in new[] { ("own", r.Actor), ("opp", r.Actor.Opponent) })
        {
            bool WasThere(int liveId) => a.Before.TryGetValue(liveId, out LiveEntity b) && b[GameTag.ZONE] == (int)SabberStoneCore.Enums.Zone.PLAY &&
                                         b[GameTag.CONTROLLER] == bc.PlayerId;
            List<string> live = a.After.Values
                .Where(e => e[GameTag.ZONE] == (int)SabberStoneCore.Enums.Zone.PLAY && e[GameTag.CONTROLLER] == bc.PlayerId &&
                            e[GameTag.CARDTYPE] is (int)CardType.MINION or (int)CardType.LOCATION && e[GameTag.ZONE_POSITION] > 0)
                .OrderBy(e => e[GameTag.ZONE_POSITION]).Select(e => WasThere(e.Id) ? "#" + e.Id : "+" + Canon(a.CardOf(e.Id, true))).ToList();
            List<string> ours = bc.BoardZone.GetAll()
                .Select(m => r.OursToLive.TryGetValue(m.Id, out int id) && WasThere(id) ? "#" + id : "+" + Canon(m.Card.Id)).ToList();
            if (!live.Any(x => x[0] == '+') && !ours.Any(x => x[0] == '+')) continue;
            if (live.SequenceEqual(ours)) { parts.Add(side + " same"); continue; }
            static string Mask(string x) => x[0] == '+' ? "+" : x;
            // (not the same minions on the board — one missing or extra: a rules difference, the status shows it; not a placement)
            string kind = live.Select(Mask).SequenceEqual(ours.Select(Mask)) ? "order"
                : live.Select(Mask).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(ours.Select(Mask).OrderBy(x => x, StringComparer.Ordinal)) ? "placement"
                : "content";
            parts.Add($"{side} {kind}: live [{string.Join(" ", live)}] ours [{string.Join(" ", ours)}]");
        }
        return string.Join("; ", parts);
    }

    private static readonly string[] StatusOrder ={ "MATCH", "MATCH-RANDOM", "DIFFERS", "DIFFERS-RANDOM", "UNSURE", "REFUSED", "ERROR", "UNSCRIPTED", "NOT-REBUILT" };

    private static void Report(List<Result> results, string dir, Options o)
    {
        Directory.CreateDirectory(dir);
        static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
        var tsv = new List<string> { "session\tline\tseq\ttime\tgame\tkind\tcard\tname\tset\tstatus\tdetail\tnotes\tacting\tboard" };
        foreach (Result r in results)
            tsv.Add($"{r.Action.Session}\t{r.Action.Line}\t{r.Action.Seq}\t{r.Action.Time:HH:mm:ss.fff}\t{r.Action.GameIndex}\t{r.Kind}\t{r.Card}\t" +
                    $"{Clean(CardFor(r.Card)?.Name)}\t{r.Set}\t{r.Status}\t{Clean(r.Detail)}\t{Clean(r.Notes)}\t{r.Acting}\t{Clean(r.Board)}");
        File.WriteAllLines(Path.Combine(dir, "results.tsv"), tsv, new UTF8Encoding(false));

        var md = new StringBuilder();
        md.AppendLine("# Live check (generated by `HsServer livecheck`; do not edit)");
        md.AppendLine();
        md.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm}: {results.Count} live actions from {results.Select(r => r.Action.Session).Distinct().Count()} sessions" +
                      (o.Card != null ? $", card {o.Card}" : "") + (o.Set != null ? $", set {o.Set}" : "") + (o.Kind != "all" ? $", {o.Kind}" : "") + ".");
        md.AppendLine();
        md.AppendLine("Every action of every live game (a card played, a Hero Power, a Location, a trade, an attack, a turn's end with its end- and");
        md.AppendLine("start-of-turn effects) is rebuilt in our engine from the live position and done the same way (same target, position,");
        md.AppendLine("option, picks); the outcomes are compared: zones, owners, transforms, Attack / Health / Damage / Armor / Durability,");
        md.AppendLine("keywords, hand costs, new cards, Mana / Overload / Corpses. Rules, not visuals. Lines are those of `HsServer power <session>`.");
        md.AppendLine();
        md.AppendLine("- MATCH: the same outcome. MATCH-RANDOM: random in ours — one of our seeds / picks gave live's outcome, or the same shape.");
        md.AppendLine("- DIFFERS: ours is deterministic here and does something else (the first difference is shown). DIFFERS-RANDOM: random in");
        md.AppendLine("  ours and not even live's shape. UNSURE: a difference the rebuild can't vouch for — the only differences are numbers it had");
        md.AppendLine("  to force (a modifier it couldn't reproduce), a card that acted reads an engine counter it can't fill (LiveCheck.Counters,");
        md.AppendLine("  named), or reads a hand / deck with cards the viewer never saw (rebuilt as placeholder minions). REFUSED / ERROR: ours");
        md.AppendLine("  refused the action live did / threw. UNSCRIPTED: a difference where a card that acted (or an aura in play) has text but no");
        md.AppendLine("  script in ours — a card to script, listed per card below. NOT-REBUILT: why in the next section.");
        md.AppendLine("- `HsServer livecheck --session S --line N` prints one action (our board, live's and our outcome, our history); `--detail`");
        md.AppendLine("  prints that for every action that isn't a match.");
        md.AppendLine();
        string Counts(IEnumerable<Result> rs)
        {
            var g = rs.GroupBy(r => r.Status).ToDictionary(x => x.Key, x => x.Count());
            return string.Join(" | ", StatusOrder.Select(s => g.GetValueOrDefault(s).ToString()));
        }
        int rebuilt = results.Count(r => r.Status != "NOT-REBUILT");
        md.AppendLine($"Rebuilt: {rebuilt} of {results.Count} ({(results.Count == 0 ? 0 : 100.0 * rebuilt / results.Count):0.0}%).");
        md.AppendLine();
        md.AppendLine("| kind | " + string.Join(" | ", StatusOrder) + " |");
        md.AppendLine("|---|" + string.Concat(StatusOrder.Select(_ => "---:|")));
        foreach (var g in results.GroupBy(r => r.Kind).OrderBy(g => g.Key, StringComparer.Ordinal))
            md.AppendLine($"| {g.Key} | {Counts(g)} |");
        md.AppendLine($"| **all** | {Counts(results)} |");
        md.AppendLine();
        md.AppendLine("## Not rebuilt: why");
        md.AppendLine();
        foreach (var g in results.Where(r => r.Status == "NOT-REBUILT").GroupBy(r => Reason(r.Detail)).OrderByDescending(g => g.Count()).Take(30))
            md.AppendLine($"- {g.Count()} × {g.Key} (e.g. {g.First().Card}, `{g.First().Action.Session}:{g.First().Action.Line}`: {Clean(g.First().Detail)})");
        md.AppendLine();
        md.AppendLine("## Per expansion (the card played: plays, Hero Powers, Locations, trades)");
        md.AppendLine();
        md.AppendLine("| set | " + string.Join(" | ", StatusOrder) + " |");
        md.AppendLine("|---|" + string.Concat(StatusOrder.Select(_ => "---:|")));
        var plays = results.Where(r => r.Kind is not ("attack" or "end-turn")).ToList();
        foreach (var g in plays.GroupBy(r => r.Set).OrderBy(g => g.Key, StringComparer.Ordinal))
            md.AppendLine($"| {g.Key} | {Counts(g)} |");
        md.AppendLine();

        // differences: per expansion, then attacks (by attacker), then turn ends (by the cards that acted)
        string Section(Result r) => r.Kind == "attack" ? "~attacks" : r.Kind == "end-turn" ? "~turn ends" : r.Set;
        string Key(Result r) => r.Kind == "end-turn" ? (r.Acting.Length > 0 ? r.Acting.Split(',')[0] : "(no card acted)") : r.Card;
        foreach (var g in results.Where(r => r.Status is "DIFFERS" or "REFUSED" or "ERROR").GroupBy(Section).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            md.AppendLine($"## {g.Key.TrimStart('~')}: differences (DIFFERS, REFUSED, ERROR)");
            md.AppendLine();
            foreach (var byCard in g.GroupBy(Key).OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                Result first = byCard.OrderBy(r => r.Status == "DIFFERS" ? 0 : 1).First();
                int total = results.Count(r => Section(r) == g.Key && Key(r) == byCard.Key);
                md.AppendLine($"- **{byCard.Key}** {Clean(CardFor(byCard.Key)?.Name)}: {string.Join(", ", byCard.GroupBy(r => r.Status).Select(s => $"{s.Count()} {s.Key}"))} of {total} — " +
                              $"{Clean(first.Detail)} — `{first.Action.Session}:{first.Action.Line}`");
            }
            md.AppendLine();
        }
        md.AppendLine("## Differences where a card that acted has no script in ours (UNSCRIPTED: cards to script, per card)");
        md.AppendLine();
        foreach (var byCard in results.Where(r => r.Status == "UNSCRIPTED").GroupBy(r => r.Detail.Substring(19).Split(' ', ',')[0])
                     .OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.Ordinal))
        {
            Result first = byCard.First();
            md.AppendLine($"- **{byCard.Key}** {Clean(CardFor(byCard.Key)?.Name)}: {byCard.Count()} — {Clean(first.Detail)} — `{first.Action.Session}:{first.Action.Line}`");
        }
        md.AppendLine();
        // where the new minions stand (not part of the status: rules compare what is on the board, this where)
        var boards = results.Where(r => r.Board.Length > 0).ToList();
        int Of(string k) => boards.Count(r => r.Board.Split("; ").Any(p => p.Split(' ', ':')[1] == k));
        md.AppendLine("## Board order after summons (where the new minions stand, live vs ours; not part of the status)");
        md.AppendLine();
        md.AppendLine($"{boards.Count} actions put a minion or Location into play: {boards.Count(r => r.Board.Split("; ").All(p => p.EndsWith(" same")))} the same board, " +
                      $"{Of("placement")} with a new one in another slot (placement), {Of("order")} with the same slots but another new card in one (order: " +
                      $"which came first, or a random card), {Of("content")} with other minions on the board (a rules difference: not compared here).");
        md.AppendLine();
        foreach (var byCard in boards.Where(r => r.Board.Contains("placement:")).GroupBy(Key).OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.Ordinal).Take(200))
        {
            Result first = byCard.First();
            int total = boards.Count(r => Key(r) == byCard.Key);
            md.AppendLine($"- **{byCard.Key}** {Clean(CardFor(byCard.Key)?.Name)} ({first.Kind}): {byCard.Count()} of {total} — {Clean(first.Board)} — `{first.Action.Session}:{first.Action.Line}`");
        }
        md.AppendLine();
        md.AppendLine("## Random or unsure differences (per card: count, an example; the 200 most frequent — all in results.tsv)");
        md.AppendLine();
        foreach (var byCard in results.Where(r => r.Status is "DIFFERS-RANDOM" or "UNSURE").GroupBy(Key).OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.Ordinal).Take(200))
        {
            Result first = byCard.First();
            md.AppendLine($"- **{byCard.Key}** ({first.Kind}): {string.Join(", ", byCard.GroupBy(r => r.Status).Select(s => $"{s.Count()} {s.Key}"))} — " +
                          $"{Clean(first.Detail)} — `{first.Action.Session}:{first.Action.Line}`");
        }
        md.AppendLine();
        File.WriteAllText(Path.Combine(dir, "last-run.md"), md.ToString(), new UTF8Encoding(false));
        Console.Error.WriteLine($"wrote {Path.Combine(dir, "last-run.md")} and results.tsv");
    }

    /// <summary>Where an exception came from: the first frames of its stack.</summary>
    private static string Where(Exception ex) =>
        string.Join(" < ", (ex.StackTrace ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("at ")).Take(3).Select(l => l.Split(" in ")[0].Substring(3)));

    /// <summary>A not-rebuilt reason without its card / entity details, for grouping.</summary>
    private static string Reason(string detail)
    {
        string d = System.Text.RegularExpressions.Regex.Replace(detail ?? "", @"'[^']*'|\b[A-Za-z0-9]+_[A-Za-z0-9_]+#?\d*|#\d+|\d+", "…");
        return d.Length > 90 ? d.Substring(0, 90) : d;
    }
}
