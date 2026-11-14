using System.Diagnostics;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;
using SGame = SabberStoneCore.Model.Game;

namespace HsServer.Sabber.Ai;

/// <summary>
/// Turn-planning AI (roadmap A3). At the start of its turn (and whenever reality drifts from the plan) it searches whole
/// move sequences on cheap clones (no history): a beam search that keeps the <see cref="BeamWidth"/> best partial turns at
/// each depth, skips boards it has already reached in another order, and stops at <see cref="BudgetMs"/>. It then plays
/// the plan move by move; after each move the real board is compared with the simulated one, and a difference (a random
/// effect, a Discover) triggers a new search. Lethal falls out of the scoring (a won game outranks everything).
/// </summary>
internal sealed class PlannerAi : IGameAi
{
    public string Label { get; init; } = "planner";
    public string Name => Label;
    public int BeamWidth { get; init; } = 12;
    /// <summary>How many of the best plans get the opponent's answer simulated (attacks only: what the AI can see).</summary>
    public int Replies { get; init; } = 8;
    public int MaxDepth { get; init; } = 14;
    public double BudgetMs { get; init; } = 150;
    public BoardScore Weights { get; init; } = new();
    /// <summary>Personality: the chance to play the second-best plan instead of the best (a softer opponent).</summary>
    public double Mistakes { get; init; }
    /// <summary>Mulligan: "cheap" (keep cost ≤ 3, the old rule) or "curve" (keep a curve: 1-2s, a 3 with a 2, 4s going
    /// second).</summary>
    public string Mulligan { get; init; } = "cheap";
    /// <summary>Try every board slot for a minion or Location (Magnetic, adjacency); false = always the default far-right
    /// slot (arena comparison).</summary>
    public bool Slots { get; init; } = true;
    private readonly Random rng = new();

    // The plan for the current turn: moves (as entity ids, rebuilt on the real game) and the board fingerprint expected
    // before each of them.
    private readonly List<(Move Move, long Before)> plan = new();
    private int planTurn = -1;

    // TargetEntity: the move's target as the client names it (a card in hand for a Location or a hand-targeting play).
    // Position: the board slot a card is played to (-1 = the default, far right).
    private readonly record struct Move(PlayerTaskType Type, int Source, int Target, int ChooseOne, int TargetEntity, int Position);

    public PlayerTask Step(SGame game, Controller me)
    {
        // (2026-10-09) auto-cast: a card or Hero Power the client data marks AI_MUST_PLAY (tag 367: the adventure bosses' "Auto-cast"
        // Hero Powers, 204 cards) is used as soon as it can be — live Kizi Copperclip's Makeover: the first action of each turn with a
        // friendly minion, else right after one is played (heist-session-20260929-122856, 7 of 7)
        if (AutoCast(me) is { } must)
        {
            plan.Clear();
            return must;
        }
        if (planTurn == game.Turn && plan.Count > 0 && plan[0].Before == Fingerprint(game))
        {
            Move next = plan[0].Move;
            plan.RemoveAt(0);
            if (Build(next, game, me) is { } task) return task;
        }
        plan.Clear();
        planTurn = game.Turn;
        Search(game, me);
        if (plan.Count == 0) return EndTurnTask.Any(me);
        Move first = plan[0].Move;
        plan.RemoveAt(0);
        return Build(first, game, me) ?? EndTurnTask.Any(me);
    }

    private const GameTag AiMustPlay = (GameTag)367;
    private readonly Random autoCastRng = new();

    /// <summary>An option of a card / Hero Power marked AI_MUST_PLAY, if any (a random one of its targets).</summary>
    private PlayerTask? AutoCast(Controller me)
    {
        List<PlayerTask> options;
        try { options = me.Options(); }
        catch { return null; }
        var must = options.Where(t => t.PlayerTaskType is PlayerTaskType.HERO_POWER or PlayerTaskType.PLAY_CARD &&
                                      (t.PlayerTaskType == PlayerTaskType.HERO_POWER && t.Source == null ? me.Hero?.HeroPower : t.Source) is { } src &&
                                      src.Card[AiMustPlay] == 1).ToList();
        return must.Count == 0 ? null : must[autoCastRng.Next(must.Count)];
    }

    public PlayerTask Choose(SGame game, Controller me, Choice choice)
    {
        if (choice.ChoiceType == ChoiceType.MULLIGAN)
            return Mulligan == "curve" ? CurveMulligan(game, me, choice) : SabberAi.Choose(game, me, choice);
        // a Discover-style pick: try each option and keep the best board
        int best = choice.Choices[0];
        double bestScore = double.MinValue;
        foreach (int option in choice.Choices)
        {
            try
            {
                SGame clone = Visible(game, me.Id);
                Controller cm = clone.ControllerById(me.Id);
                if (!clone.Process(ChooseTask.Pick(cm, option))) continue;
                double score = Weights.Score(clone, me.PlayerId) + HandValue(clone, me.PlayerId, option);
                if (score > bestScore) { bestScore = score; best = option; }
            }
            catch (Exception) { }
        }
        return ChooseTask.Pick(me, best);
    }

    // Keep a curve: every 1-2 drop; a 3 when there's also a 1-2 (or going second); a 4 only going second with a cheaper
    // card; nothing above 4.
    private static PlayerTask CurveMulligan(SGame game, Controller me, Choice choice)
    {
        var cards = choice.Choices.Select(id => game.IdEntityDic[id]).ToList();
        bool second = me != game.FirstPlayer;
        bool early = cards.Any(p => p.Cost <= 2);
        var keep = cards.Where(p => p.Cost <= 2 || p.Cost == 3 && (early || second) || p.Cost == 4 && second && early)
            .Select(p => p.Id).ToList();
        return ChooseTask.Mulligan(me, keep);
    }

    // A picked card that went to the hand is worth its cost (a crude "how good is this card" until cards get values).
    private static double HandValue(SGame g, int playerId, int option) =>
        g.IdEntityDic.TryGetValue(option, out IPlayable p) && p.Zone?.Type == Zone.HAND ? p.Cost * 0.15 : 0;

    private sealed class Node
    {
        public SGame Game = null!;
        public List<Move> Moves = new();
        public List<long> Befores = new();
        public double Score;
    }

    private void Search(SGame game, Controller me)
    {
        var clock = Stopwatch.StartNew();
        int playerId = me.PlayerId;
        var root = new Node { Game = Visible(game, me.Id), Score = Weights.Score(game, playerId) };
        Node best = root;
        var frontier = new List<Node> { root };
        var finished = new List<Node> { root };
        // boards already reached; minion order left out, so a slot that changes nothing is tried once (the default
        // comes first) and a slot that does change the board (Magnetic, an adjacency buff) stays a plan of its own
        var seen = new HashSet<long> { Fingerprint(game, ordered: false) };
        for (int depth = 0; depth < MaxDepth && frontier.Count > 0 && clock.Elapsed.TotalMilliseconds < BudgetMs; depth++)
        {
            var children = new List<Node>();
            foreach (Node node in frontier)
            {
                Controller nm = node.Game.ControllerById(me.Id);
                if (node.Game.State == State.COMPLETE || node.Game.CurrentPlayer != nm) continue;
                long before = Fingerprint(node.Game);
                foreach (PlayerTask option in Candidates(nm))
                {
                    if (clock.Elapsed.TotalMilliseconds >= BudgetMs) break;
                    Node child;
                    try
                    {
                        SGame clone = node.Game.Clone();
                        PlayerTask copy = SabberAi.Rebuild(option, clone);
                        if (!Slots && copy is PlayCardTask play) play.ZonePosition = -1;
                        if (copy == null || !clone.Process(copy)) continue;
                        Controller cm = clone.ControllerById(me.Id);
                        if (cm.Choice is { } pending && pending.ChoiceType != ChoiceType.MULLIGAN)
                            clone.Process(SabberAi.Choose(clone, cm, pending)); // cheap pick inside the search
                        long key = Fingerprint(clone, ordered: false);
                        if (!seen.Add(key)) continue; // the same board in another order
                        child = new Node
                        {
                            Game = clone, Moves = new List<Move>(node.Moves) { ToMove(copy) },
                            Befores = new List<long>(node.Befores) { before }, Score = Weights.Score(clone, playerId),
                        };
                    }
                    catch (Exception) { continue; } // a move the engine can't simulate: skip it
                    children.Add(child);
                    finished.Add(child);
                    if (child.Score > best.Score) best = child;
                }
            }
            frontier = children.OrderByDescending(c => c.Score).Take(BeamWidth).ToList();
        }
        // foresight: the best few plans get the opponent's visible answer (their attacks), and the plan that holds up best
        // wins — unless a plan simply wins the game
        var ranked = new List<(Node Node, double Score)>();
        if (Replies > 0 && best.Score < 1e6)
        {
            foreach (Node candidate in finished.OrderByDescending(n => n.Score).Take(Replies))
            {
                if (clock.Elapsed.TotalMilliseconds >= BudgetMs * 1.5) break;
                ranked.Add((candidate, AfterTheirAttacks(candidate.Game, me.Id, playerId)));
            }
            ranked.Sort((x, y) => y.Score.CompareTo(x.Score));
            if (ranked.Count > 0) best = ranked[0].Node;
        }
        // personality: now and then the second-best plan (never when the best one wins the game)
        if (ranked.Count == 0)
            ranked = finished.OrderByDescending(n => n.Score).Take(2).Select(n => (n, n.Score)).ToList();
        if (Mistakes > 0 && ranked.Count > 1 && best.Score < 1e6 && rng.NextDouble() < Mistakes)
            best = ranked[1].Node;
        if (CheapestAttackFirst(game, me.Id, best.Moves) is { } aroundSecrets)
        {
            plan.AddRange(aroundSecrets);
            return;
        }
        for (int i = 0; i < best.Moves.Count; i++) plan.Add((best.Moves[i], best.Befores[i]));
    }

    /// <summary>
    /// Playing around a Secret like a player: with one of the opponent's Secrets up (the AI sees that one is, not which),
    /// each run of attacks in the plan starts with its least valuable attacker, so a Freezing / Explosive Trap or
    /// Misdirection hits that one. Only when the reordered plan reaches the same board (replayed on a copy); else the plan
    /// stays as found. Returns the moves with the board expected before each, or null.
    /// </summary>
    private static List<(Move Move, long Before)>? CheapestAttackFirst(SGame game, int meId, List<Move> moves)
    {
        Controller me = game.ControllerById(meId);
        if (!me.Opponent.SecretZone.OfType<Spell>().Any(s => s.IsSecret)) return null;
        double Value(Move m) => game.IdEntityDic.TryGetValue(m.Source, out IPlayable p) && p is Minion minion
            ? minion.AttackDamage * 1.6 + minion.Health + minion.Card.Cost * 0.5 : double.MaxValue;
        var reordered = new List<Move>(moves);
        for (int i = 0; i < reordered.Count;)
        {
            if (reordered[i].Type != PlayerTaskType.MINION_ATTACK) { i++; continue; }
            int j = i;
            while (j < reordered.Count && reordered[j].Type == PlayerTaskType.MINION_ATTACK) j++;
            var run = reordered.GetRange(i, j - i).OrderBy(Value).ToList();
            for (int k = 0; k < run.Count; k++) reordered[i + k] = run[k];
            i = j;
        }
        if (reordered.SequenceEqual(moves)) return null;
        var original = Replay(game, meId, moves);
        var cheap = Replay(game, meId, reordered);
        if (original == null || cheap == null || original.Value.End != cheap.Value.End) return null;
        return reordered.Select((m, k) => (m, cheap.Value.Befores[k])).ToList();
    }

    // the plan played on a copy without the opponent's Secrets: the board before each move and at the end (null: a move failed)
    private static (List<long> Befores, long End)? Replay(SGame game, int meId, List<Move> moves)
    {
        try
        {
            SGame copy = Visible(game, meId);
            Controller me = copy.ControllerById(meId);
            var befores = new List<long>();
            foreach (Move m in moves)
            {
                befores.Add(Fingerprint(copy));
                if (Build(m, copy, me) is not { } task || !copy.Process(task)) return null;
                if (me.Choice is { } pending && pending.ChoiceType != ChoiceType.MULLIGAN)
                    copy.Process(SabberAi.Choose(copy, me, pending));
            }
            return (befores, Fingerprint(copy, ordered: false));
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// The game as the AI may know it: a copy without the opponent's Secrets. A player sees that a Secret is up, not which
    /// one; planning on the real game let the AI read them — it never attacked into Freezing Trap (stalled the game) and
    /// went around Explosive Trap exactly (user 2026-10-05). A Secret that fires in the real game changes the board, and the
    /// plan is made again (Step's fingerprint check).
    /// </summary>
    private static SGame Visible(SGame game, int meId)
    {
        SGame copy = game.Clone();
        Controller them = copy.ControllerById(meId).Opponent;
        foreach (Spell secret in them.SecretZone.OfType<Spell>().Where(s => s.IsSecret).ToList())
            SabberStoneCore.Actions.Generic.DestroySecret(secret, reveal: false);
        return copy;
    }

    // The board after this turn ends and the opponent attacks greedily with what's in play (minions, hero/weapon) — the
    // information the AI can see; their hidden hand isn't used.
    private double AfterTheirAttacks(SGame planned, int meId, int playerId)
    {
        try
        {
            SGame g = planned.Clone();
            Controller me = g.ControllerById(meId);
            if (!g.Process(EndTurnTask.Any(me)) || g.State == State.COMPLETE) return Weights.Score(g, playerId);
            Controller them = g.CurrentPlayer;
            if (them.Choice is { } ch) g.Process(SabberAi.Choose(g, them, ch));
            for (int i = 0; i < 12 && g.State != State.COMPLETE && g.CurrentPlayer == them; i++)
            {
                PlayerTask? bestAttack = null;
                double bestScore = Weights.Score(g, them.PlayerId);
                foreach (PlayerTask t in them.Options())
                {
                    if (t.PlayerTaskType is not (PlayerTaskType.MINION_ATTACK or PlayerTaskType.HERO_ATTACK)) continue;
                    SGame c = g.Clone();
                    PlayerTask copy = SabberAi.Rebuild(t, c);
                    if (copy == null || !c.Process(copy)) continue;
                    double sc = Weights.Score(c, them.PlayerId);
                    if (sc > bestScore) { bestScore = sc; bestAttack = t; }
                }
                if (bestAttack == null || !g.Process(bestAttack)) break;
            }
            return Weights.Score(g, playerId);
        }
        catch (Exception) { return double.MinValue / 2; }
    }

    // The moves worth trying (END_TURN is the "stop here" every node already is).
    // A card's board slots come far right first (the default: a slot that changes nothing collapses into it).
    private IEnumerable<PlayerTask> Candidates(Controller me)
    {
        var seenPlays = new HashSet<(string, int, int, int, int)>();
        List<PlayerTask> options = me.Options();
        var firstOf = new Dictionary<(PlayerTaskType, int, int, int), int>();
        var ordered = options.Select((t, i) =>
            {
                var group = (t.PlayerTaskType, t.Source?.Id ?? 0, t.TargetEntity?.Id ?? 0, t.ChooseOne);
                if (!firstOf.ContainsKey(group)) firstOf[group] = i;
                return (Task: t, First: firstOf[group]);
            })
            .ToList()
            .OrderBy(x => x.First).ThenByDescending(x => Slot(x.Task));
        foreach (var (t, _) in ordered)
        {
            if (t.PlayerTaskType == PlayerTaskType.END_TURN) continue;
            // two copies of the same card in hand do the same thing: try one (per slot)
            if (t.PlayerTaskType == PlayerTaskType.PLAY_CARD && t.Source != null &&
                !seenPlays.Add((t.Source.Card.Id, t.Source.Cost, t.TargetEntity?.Id ?? 0, t.ChooseOne, Slots ? Slot(t) : -1)))
                continue;
            yield return t;
        }
    }

    private static int Slot(PlayerTask t) => (t as PlayCardTask)?.ZonePosition ?? -1;

    private static Move ToMove(PlayerTask t) => new(t.PlayerTaskType, t.Source?.Id ?? 0, t.Target?.Id ?? 0, t.ChooseOne,
        t.TargetEntity?.Id ?? 0, Slot(t));

    private static PlayerTask? Build(Move m, SGame game, Controller me)
    {
        IPlayable? source = m.Source != 0 && game.IdEntityDic.TryGetValue(m.Source, out IPlayable s) ? s : null;
        ICharacter? target = m.Target != 0 && game.IdEntityDic.TryGetValue(m.Target, out IPlayable t) ? t as ICharacter : null;
        IPlayable? entity = m.TargetEntity != 0 && game.IdEntityDic.TryGetValue(m.TargetEntity, out IPlayable e) ? e : null;
        if (m.Source != 0 && source == null || m.Target != 0 && target == null || m.TargetEntity != 0 && entity == null) return null;
        return m.Type switch
        {
            PlayerTaskType.PLAY_CARD => PlayCardTask.Any(me, source, entity, m.Position, m.ChooseOne),
            PlayerTaskType.MINION_ATTACK => MinionAttackTask.Any(me, source, target),
            PlayerTaskType.HERO_ATTACK => HeroAttackTask.Any(me, target),
            PlayerTaskType.HERO_POWER => HeroPowerTask.Any(me, target, m.ChooseOne, power: source as HeroPower),
            PlayerTaskType.TRADE => TradeTask.Any(me, source),
            PlayerTaskType.FORGE => ForgeTask.Any(me, source),
            PlayerTaskType.PREPARE => PrepareTask.Any(me, source),
            // (a move planned on another copy of the game: its ids may name something else here — no such move then)
            PlayerTaskType.LOCATION => source is Location location ? LocationTask.Any(me, location, entity) : null,
            PlayerTaskType.TITAN_ABILITY => source is Minion titan ? TitanAbilityTask.Any(me, titan, m.ChooseOne - 1, entity) : null,
            PlayerTaskType.STARSHIP_LAUNCH => source is Minion ship ? StarshipLaunchTask.Any(me, ship) : null,
            PlayerTaskType.INTERACT => source is Minion obj && entity != null ? InteractTask.Any(me, obj, entity) : null,
            _ => null,
        };
    }

    /// <summary>A cheap fingerprint of what matters for planning: heroes, boards, hands, mana. Unordered: the same minions
    /// in another order count as the same board.</summary>
    internal static long Fingerprint(SGame g, bool ordered = true)
    {
        var h = new HashCode();
        foreach (Controller c in new[] { g.Player1, g.Player2 })
        {
            h.Add(c.Hero.Health); h.Add(c.Hero.Armor); h.Add(c.Hero.AttackDamage); h.Add(c.Hero.IsExhausted);
            h.Add(c.Hero.Weapon?.Id ?? 0); h.Add(c.RemainingMana); h.Add(c.HandZone.Count); h.Add(c.DeckZone.Count);
            h.Add(c.Hero.HeroPower?.IsExhausted ?? false);
            long board = 0;
            foreach (Minion m in c.BoardZone.GetAll())
            {
                int minion = HashCode.Combine(m.Id, m.AttackDamage, m.Health, m.IsExhausted, m.HasDivineShield, m.HasTaunt, m.IsFrozen);
                if (ordered) h.Add(minion);
                else board += minion;
            }
            h.Add(board);
            foreach (IPlayable p in c.HandZone.GetAll()) { h.Add(p.Id); h.Add(p.Card.AssetId); h.Add(p.Cost); }
        }
        return h.ToHashCode();
    }
}

/// <summary>
/// How good a board is for one player (end-of-turn view). Weights are plain numbers so personalities (Innkeeper, bosses)
/// are just different instances; `HsServer arena` tunes them.
/// </summary>
internal sealed class BoardScore
{
    public double Health { get; init; } = 1.0;
    public double EnemyHealth { get; init; } = 1.2;
    public double Attack { get; init; } = 1.6;
    public double MinionHealth { get; init; } = 1.0;
    public double Taunt { get; init; } = 1.5;
    public double DivineShield { get; init; } = 2.0;
    public double Keyword { get; init; } = 1.0; // Poisonous, Lifesteal, Windfury, Reborn, Deathrattle
    public double Card { get; init; } = 1.2;
    public double Weapon { get; init; } = 0.8;
    public double Danger { get; init; } = 25;
    /// <summary>Card quality: each card in your hand is worth Card + CardCost × its cost (the opponent's hand is hidden:
    /// count only).</summary>
    public double CardCost { get; init; }
    /// <summary>Mana left unspent at the end of the turn (tempo).</summary>
    public double UnspentMana { get; init; }

    public double Score(SGame g, int playerId)
    {
        Controller me = g.Player1.PlayerId == playerId ? g.Player1 : g.Player2;
        Controller them = me.Opponent;
        if (them.PlayState is PlayState.LOST or PlayState.CONCEDED || them.Hero.Health <= 0) return 1e6;
        if (me.PlayState == PlayState.LOST || me.Hero.Health <= 0) return -1e6;

        double score = Life(me.Hero.Health + me.Hero.Armor) * Health - Life(them.Hero.Health + them.Hero.Armor) * EnemyHealth;
        score += Side(me) - Side(them) * 1.1;
        score += me.HandZone.GetAll().Sum(p => Card + CardCost * Math.Min(p.Cost, 10)) - them.HandZone.Count * Card;
        if (g.CurrentPlayer == me) score -= Math.Max(0, me.RemainingMana) * UnspentMana;
        if (me.Hero.Weapon is { } w) score += w.AttackDamage * w.Durability * Weapon;
        if (them.Hero.Weapon is { } tw) score -= tw.AttackDamage * tw.Durability * Weapon;

        // danger: what the opponent could hit my face with next turn (their minions that can attack, their weapon)
        int theirDamage = them.BoardZone.Where(m => !(m is Location) && !m.IsFrozen && !m.CantAttack).Sum(m => m.AttackDamage * (m.HasWindfury ? 2 : 1))
                          + (them.Hero.Weapon?.AttackDamage ?? 0);
        int myTaunts = me.BoardZone.Count(m => m.HasTaunt && !(m is Location));
        if (myTaunts == 0 && theirDamage >= me.Hero.Health + me.Hero.Armor) score -= Danger;
        return score;
    }

    // Life is worth more when it's low (the last 10 Health decide games).
    private static double Life(int hp) => hp <= 0 ? -50 : hp + Math.Max(0, 10 - hp) * 0.8;

    private double Side(Controller c)
    {
        double total = 0;
        foreach (Minion m in c.BoardZone.GetAll())
        {
            if (m is Location) continue;
            double v = m.AttackDamage * Attack + m.Health * MinionHealth;
            if (m.HasTaunt) v += Taunt;
            if (m.HasDivineShield) v += DivineShield + m.AttackDamage * 0.5;
            if (m.Poisonous) v += Keyword * 2;
            if (m.HasLifeSteal) v += Keyword + m.AttackDamage * 0.3;
            if (m.HasWindfury) v += m.AttackDamage * 0.8;
            if (m[GameTag.REBORN] > 0) v += Keyword;
            if (m.HasDeathrattle) v += Keyword;
            if (m.IsFrozen) v -= m.AttackDamage * 0.5;
            v += m.SpellPower * 1.5;
            total += v;
        }
        return total;
    }
}
