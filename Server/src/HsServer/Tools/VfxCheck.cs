using SabberStoneCore.Config;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;
using SGame = SabberStoneCore.Model.Game;

namespace HsServer.Tools;

/// <summary>
/// `HsServer vfxcheck [file] [--out file] [--card ID] [--dump]`: does our server send the effects (SUB_SPELL) live sent for each card?
/// Reads the live recordings' card + effect pairs (default RESEARCH/vfx/recorded_subspells.tsv: card, block, prefab, guid,
/// plays), plays each card in a small test game (10 mana, two minions a side, cards in hand; every way to play it — targets
/// and Choose One — then its choices, a turn each, its attacks, its Location / Hero Power, and its death for a
/// Deathrattle) and lists every effect the engine sent. Per pair: sent / not-sent / not-played (no way to play it here) /
/// error. A recorded effect that only shows in a special situation (a Secret's trigger, a condition) can read "not-sent"
/// although the card has it: check those by hand.
/// </summary>
internal static class VfxCheck
{
    /// <summary>--dump: print the history of each test game (with --card: one card's).</summary>
    private static bool dump;

    /// <summary>(2026-10-09, `golden`) the test games' random seed (null: unseeded) and a look at each finished play: the card, the
    /// way, the game and where the play's history starts.</summary>
    internal static long? Seed;
    internal static Action<Card, int, SGame, IPlayable, int>? Played;

    public static int Run(string[] args)
    {
        string root = Program.ProjectRoot();
        Sabber.SabberEngineGame.EnsureCards(Engine.CardDb.FindDefaultDir(root));
        _ = SabberStoneCore.Loader.ReprintCards.CanonicalOf;
        _ = Cards.All.Count();
        string? outFile = null, only = null;
        var files = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--out" && i + 1 < args.Length) outFile = args[++i];
            else if (args[i] == "--card" && i + 1 < args.Length) only = args[++i];
            else if (args[i] == "--dump") dump = true;
            else files.Add(args[i]);
        }
        string file = files.FirstOrDefault() ?? Path.Combine(root, "RESEARCH", "vfx", "recorded_subspells.tsv");

        // card -> recorded effect names (and the block types live played them in)
        var recorded = new Dictionary<string, Dictionary<string, HashSet<string>>>();
        foreach (string line in File.ReadLines(file))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            string[] p = line.Split('\t');
            if (p.Length < 3) continue;
            if (only != null && p[0] != only) continue;
            if (!recorded.TryGetValue(p[0], out var byName)) recorded[p[0]] = byName = new();
            if (!byName.TryGetValue(p[2], out var blocks)) byName[p[2]] = blocks = new();
            blocks.Add(p[1]);
        }

        var rows = new List<string> { "card\tname\ttype\tprefab\tlive_blocks\tstatus\tsent_by_us" };
        var counts = new Dictionary<string, int>();
        foreach (var (cardId, byName) in recorded.OrderBy(k => k.Key))
        {
            Card? card = Cards.TryFromId(cardId);
            HashSet<string> sent = new();
            string status;
            if (card == null) status = "unknown-card";
            else
            {
                status = PlayAll(card, sent);
            }
            foreach (var (prefab, blocks) in byName)
            {
                string s = status != "played" ? status : sent.Contains(prefab) ? "sent" : Missed(cardId, prefab, sent);
                counts[s] = counts.GetValueOrDefault(s) + 1;
                rows.Add($"{cardId}\t{card?.Name}\t{card?.Type}\t{prefab}\t{string.Join(",", blocks)}\t{s}\t{string.Join(",", sent.Where(x => x[0] != '@').OrderBy(x => x))}");
            }
        }
        if (outFile != null) File.WriteAllLines(outFile, rows);
        else foreach (string r in rows) Console.WriteLine(r);
        Console.Error.WriteLine($"{recorded.Count} cards, {recorded.Sum(r => r.Value.Count)} recorded effects: " +
                                string.Join(", ", counts.OrderByDescending(c => c.Value).Select(c => $"{c.Key} {c.Value}")));
        return 0;
    }

    /// <summary>A recorded effect we didn't send: "missed" when the card's block in our game did what live wrapped in it (the
    /// recorded anchor: a summon, a buff… or, for an empty one, the block itself ran), else "action-not-seen" (our test game never
    /// made the card do it: a condition, a trigger it didn't meet).</summary>
    private static string Missed(string cardId, string prefab, HashSet<string> seen)
    {
        // (a Titan: an effect of one of its abilities counts only when that ability was used — "@ability:<key>", Collect)
        if (Cards.TryFromId(cardId) is Card titan && titan.IsTitan)
        {
            var keys = Cards.TitanAbilities(titan).Select(SabberStoneCore.Actions.Generic.AbilityKey).Where(k => k != null).ToList();
            var its = keys.Where(k => SabberStoneCore.Actions.Generic.EffectOfAbility(prefab, k)).ToList();
            if (its.Count > 0 && !its.Any(k => seen.Contains("@ability:" + k))) return "action-not-seen";
        }
        foreach (var a in SabberStoneCore.Loader.SubSpells.Anchors(cardId).Where(a => a.Name == prefab))
            if (seen.Contains("@" + a.Block + ":" + (a.Kind == "marker" ? "block" : a.Kind)))
                return "missed";
        return "action-not-seen";
    }

    /// <summary>Plays the card every way it can be played (one game each); the effects sent in any of them.</summary>
    internal static string PlayAll(Card card, HashSet<string> sent)
    {
        string result = "not-played";
        int ways;
        try { ways = Ways(card); }
        catch (Exception e) { return "error " + e.GetType().Name; }
        for (int way = 0; way < Math.Max(1, ways) && way < 12; way++)
        {
            try
            {
                if (PlayOnce(card, way, sent)) result = "played";
            }
            catch (Exception e) { if (result != "played") result = "error " + e.GetType().Name; }
        }
        return result;
    }

    // (2026-10-08) every play twice: as it is, and after a card was already played this turn (a Coin: Combo, "played a card this turn");
    // an enchantment: attached four ways (a friendly minion, its hero, its player, an enemy minion) — then the events script (Stir)
    private const int EnchantmentWays = 4;

    private static int Ways(Card card)
    {
        if (card.Type == CardType.ENCHANTMENT) return EnchantmentWays;
        var (g, me, p) = Setup(card);
        return p == null ? 0 : Plays(me, p).Count * 2;
    }

    private static (SGame g, Controller me, IPlayable? p) Setup(Card card)
    {
        CardClass cls = card.Class is CardClass.INVALID or CardClass.NEUTRAL or CardClass.DREAM or CardClass.WHIZBANG ? CardClass.MAGE : card.Class;
        var g = new SGame(new GameConfig
        {
            StartPlayer = 1, Player1HeroClass = cls, Player2HeroClass = CardClass.MAGE,
            FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false, RandomSeed = Seed,
        });
        g.StartGame();
        // (a Ranked spell stays the rank it is: Mana just below its next rank — at 10 Rank 2 was drawn as Rank 3)
        var (nextRank, _) = SabberStoneCore.Actions.Generic.RankOf(card);
        g.Player1.BaseMana = nextRank > 0 ? Math.Max(card.Cost, nextRank - 1) : 10;
        g.Player2.BaseMana = 10;
        Controller me = g.Player1, them = g.Player2;
        // (2026-10-08) a board with what conditions ask for: a Beast, a Mech, a Demon, a Murloc, a Divine Shield minion, a damaged one;
        // a Dragon in hand ("if you're holding a Dragon"); enemies of several kinds (one damaged)
        foreach (string id in new[] { "CS2_182", "CS2_172", "EX1_556", "EX1_008" }) // Yeti, Raptor, Harvest Golem (a Mech), Argent Squire (Divine Shield)
            Entity.FromCard(me, Cards.FromId(id), zone: me.BoardZone);
        foreach (string id in new[] { "CS2_182", "CS2_172", "CS2_168", "EX1_284" }) // Yeti, Raptor, Murloc Raider, Azure Drake
            Entity.FromCard(them, Cards.FromId(id), zone: them.BoardZone);
        if (me.BoardZone.Count > 1) me.BoardZone[1].Damage = 1; // (a damaged friendly minion)
        if (them.BoardZone.Count > 0) them.BoardZone[0].Damage = 2;
        foreach (Minion m in me.BoardZone.Concat(them.BoardZone)) { m[GameTag.NUM_TURNS_IN_PLAY] = 1; m.IsExhausted = false; }
        foreach (string id in new[] { "CS2_182", "CS2_172", "CS2_029", "EX1_284" }) // a minion, a Beast, a spell (Fireball), a Dragon
            SabberStoneCore.Actions.Generic.DrawCard(me, Cards.FromId(id));
        foreach (string id in new[] { "CS2_182", "CS2_029" }) // the opponent's turn: a minion and a spell
            SabberStoneCore.Actions.Generic.DrawCard(them, Cards.FromId(id));
        me.GainCorpses(6); // (a Corpse spender can spend)
        me.Hero.Damage = 10; // (a heal has something to heal)
        IPlayable? p = null;
        switch (card.Type)
        {
            case CardType.HERO_POWER:
                // (2026-10-08: installed the game's way — a Hero Power assigned by hand wasn't in play: all 22 were "not-played")
                new SabberStoneCore.Tasks.SimpleTasks.ReplaceHeroPower(card).Process(g, me, me.Hero, null);
                p = me.Hero.HeroPower;
                break;
            case CardType.ENCHANTMENT:
                return (g, me, null);
            default:
                p = SabberStoneCore.Actions.Generic.DrawCard(me, card);
                break;
        }
        return (g, me, p);
    }

    /// <summary>The ways to use the card now: its plays (each target / Choose One) or its Hero Power.</summary>
    private static List<PlayerTask> Plays(Controller me, IPlayable p)
    {
        // (a Hero Power use names no source unless it's a second Hero Power: the player's current one is p)
        var all = me.Options().Where(t => (t.Source == p || t.PlayerTaskType == PlayerTaskType.HERO_POWER && t.Source == null && p == me.Hero.HeroPower)
                                          && t.PlayerTaskType is PlayerTaskType.PLAY_CARD or PlayerTaskType.HERO_POWER).ToList();
        // one per Choose One option and per kind of target (an enemy minion first, then the enemy hero, a friendly one, none)
        int Rank(PlayerTask t) => t.Target == null ? 3 : t.Target.Controller != me ? (t.Target is Minion ? 0 : 1) : 2;
        return all.GroupBy(t => (t.ChooseOne, Rank(t))).OrderBy(gr => gr.Key.Item2).Select(gr => gr.First())
            .GroupBy(t => t.ChooseOne).SelectMany(gr => gr.Take(2)).ToList();
    }

    private static bool PlayOnce(Card card, int way, HashSet<string> sent)
    {
        var (g, me, p) = Setup(card);
        int start;
        if (card.Type == CardType.ENCHANTMENT)
        {
            // attached to a friendly minion / its hero / its player / an enemy minion, then the events
            IEntity? on = way switch
            {
                0 => me.BoardZone.FirstOrDefault(),
                1 => me.Hero,
                2 => me,
                _ => me.Opponent.BoardZone.FirstOrDefault(),
            };
            if (on == null) return false;
            start = g.PowerHistory.Full.Count;
            SabberStoneCore.Actions.Generic.AddEnchantmentBlock(g, card, me.Hero, on, 0, 0, 0);
            p = on.AppliedEnchantments?.LastOrDefault(e => e.Card == card);
            if (p == null) return false;
            Stir(g, me, p);
            Played?.Invoke(card, way, g, p, start);
            Collect(g, card, p, start, sent);
            return true;
        }
        if (p == null) return false;
        if (way % 2 == 1)
        {
            // a card already played this turn (Combo; "if you played a card this turn")
            IPlayable coin = SabberStoneCore.Actions.Generic.DrawCard(me, Cards.FromId("GAME_005"));
            if (coin == null || !g.Process(PlayCardTask.Any(me, coin))) return false;
            Resolve(g);
        }
        var plays = Plays(me, p);
        if (way / 2 >= plays.Count) return false;
        start = g.PowerHistory.Full.Count;
        if (!g.Process(plays[way / 2])) return false;
        Resolve(g);
        // a Location / its attacks / the Hero Power again, then a turn each (turn triggers; the opponent attacks once: Secrets)
        foreach (PlayerTask t in me.Options().Where(t => t.Source == p && t.PlayerTaskType is PlayerTaskType.LOCATION or PlayerTaskType.MINION_ATTACK or PlayerTaskType.HERO_ATTACK).Take(1).ToList())
        { g.Process(t); Resolve(g); }
        if (card.Type == CardType.WEAPON)
            foreach (PlayerTask t in me.Options().Where(t => t.PlayerTaskType == PlayerTaskType.HERO_ATTACK).Take(1).ToList())
            { g.Process(t); Resolve(g); }
        // a Titan: one ability a turn, three turns (before the events: it must still be there)
        for (int round = 0; round < 3 && g.State == State.RUNNING && p is Minion titan && titan.Card.IsTitan && titan.Zone?.Type == Zone.PLAY; round++)
        {
            PlayerTask? use = me.Options().FirstOrDefault(t => t.Source == p && t.PlayerTaskType == PlayerTaskType.TITAN_ABILITY);
            if (use == null) break;
            g.Process(use); Resolve(g);
            if (g.State == State.RUNNING) { g.Process(EndTurnTask.Any(me)); Resolve(g); }
            if (g.State == State.RUNNING && g.CurrentPlayer == me.Opponent) { g.Process(EndTurnTask.Any(me.Opponent)); Resolve(g); }
        }
        Stir(g, me, p);
        if (g.State == State.RUNNING && p is Minion m && m.Zone?.Type == Zone.PLAY)
        {
            foreach (PlayerTask t in me.Options().Where(t => t.Source == p && t.PlayerTaskType == PlayerTaskType.MINION_ATTACK).Take(1).ToList())
            { g.Process(t); Resolve(g); }
            if (m.Zone?.Type == Zone.PLAY) { m.Destroy(); g.DeathProcessingAndAuraUpdate(); Resolve(g); }
        }
        if (dump)
        {
            Console.Error.WriteLine($"==== {card.Id} way {way}");
            foreach (IPowerHistoryEntry e in g.PowerHistory.Full.Skip(start)) Console.Error.WriteLine(e.Print().TrimEnd());
        }
        Played?.Invoke(card, way, g, p, start);
        Collect(g, card, p, start, sent);
        return true;
    }

    /// <summary>
    /// (2026-10-08) The events a card's triggers wait for, after it was played: its owner plays a minion, casts a spell at an enemy minion,
    /// uses the Hero Power, attacks; a friendly minion takes damage and is healed; a card is drawn; the turn ends; the opponent plays a
    /// minion, casts a spell at the hero and attacks (Secrets); then one more turn each (start / end of turn).
    /// </summary>
    private static void Stir(SGame g, Controller me, IPlayable p)
    {
        void Do(Controller c, Func<PlayerTask, bool> pick)
        {
            if (g.State != State.RUNNING || g.CurrentPlayer != c) return;
            PlayerTask? t = c.Options().FirstOrDefault(pick);
            if (t == null) return;
            try { g.Process(t); } catch { }
            Resolve(g);
        }
        bool IsPlay(PlayerTask t, string id) => t is PlayCardTask pc && pc.Source?.Card.Id == id && pc.Source.Id != p.Id;
        Controller them = me.Opponent;
        Do(me, t => t.Source == p && t.PlayerTaskType is PlayerTaskType.LOCATION);
        if (me.BoardZone.Count <= 4) Do(me, t => IsPlay(t, "CS2_182")); // (room left for what the card summons)
        Do(me, t => IsPlay(t, "CS2_029") && t.Target is Minion m && m.Controller != me);
        Do(me, t => t.PlayerTaskType == PlayerTaskType.HERO_POWER && t.Source != p);
        Do(me, t => t.PlayerTaskType == PlayerTaskType.MINION_ATTACK && t.Target is Minion);
        Do(me, t => t.PlayerTaskType == PlayerTaskType.HERO_ATTACK);
        if (g.State == State.RUNNING)
        {
            Minion? hurt = p is Minion pm && pm.Zone?.Type == Zone.PLAY ? pm : me.BoardZone.FirstOrDefault(m => !m.IsDead);
            if (hurt != null)
            {
                SabberStoneCore.Actions.Generic.DamageCharFunc(them.Hero, hurt, 1, false);
                g.DeathProcessingAndAuraUpdate(); Resolve(g);
                if (!hurt.IsDead) { hurt.TakeHeal(me.Hero, 2); g.DeathProcessingAndAuraUpdate(); Resolve(g); }
            }
            SabberStoneCore.Actions.Generic.Draw(me); g.DeathProcessingAndAuraUpdate(); Resolve(g);
        }
        for (int round = 0; round < 2 && g.State == State.RUNNING; round++)
        {
            if (g.CurrentPlayer == me) { g.Process(EndTurnTask.Any(me)); Resolve(g); }
            if (g.State != State.RUNNING || g.CurrentPlayer != them) break;
            Do(them, t => IsPlay(t, "CS2_182"));
            Do(them, t => IsPlay(t, "CS2_029") && t.Target == me.Hero);
            Do(them, t => t.PlayerTaskType == PlayerTaskType.MINION_ATTACK && t.Target is Minion);
            Do(them, t => t.PlayerTaskType == PlayerTaskType.MINION_ATTACK);
            if (g.State == State.RUNNING) { g.Process(EndTurnTask.Any(them)); Resolve(g); }
            if (round == 0)
            {
                Do(me, t => t.Source == p && t.PlayerTaskType is PlayerTaskType.LOCATION or PlayerTaskType.HERO_POWER);
                Do(me, t => t.Source == p && t.PlayerTaskType == PlayerTaskType.MINION_ATTACK);
            }
        }
    }

    private static void Collect(SGame g, Card card, IPlayable p, int start, HashSet<string> sent)
    {
        // what the card's own blocks did ("@POWER:spawn"…: Missed), as RESEARCH/vfx/tools/engine_subspells.py reads live's
        var blocks = new Stack<string?>();
        var zone = new Dictionary<int, int>();
        var history = g.PowerHistory.Full;
        for (int i = 0; i < history.Count; i++)
        {
            IPowerHistoryEntry e = history[i];
            bool mine = i >= start; // (the setup's history only keeps zones)
            string? kind = null;
            switch (e)
            {
                case PowerHistoryBlockStart b:
                    bool effect = b.BlockType is BlockType.POWER or BlockType.TRIGGER;
                    string? name = effect && b.Source == p.Id
                        ? b.BlockType == BlockType.POWER ? "POWER" : b.TriggerKeyword == (int)GameTag.DEATHRATTLE ? "DEATHRATTLE" : "TRIGGER"
                        : effect ? null : blocks.Count > 0 ? blocks.Peek() : null;
                    blocks.Push(name);
                    if (mine && name != null && b.Source == p.Id) sent.Add("@" + name + ":block");
                    continue;
                case PowerHistoryBlockEnd:
                    if (blocks.Count > 0) blocks.Pop();
                    continue;
                case PowerHistorySubSpellStart s:
                    if (mine && s.Prefab != null) sent.Add(s.Prefab.Split(':')[0]);
                    continue;
                case PowerHistoryFullEntity { Entity: { } f }:
                    int fz = f.Tags.TryGetValue(GameTag.ZONE, out int z0) ? z0 : 0;
                    zone[f.Id] = fz;
                    if (f.Tags.ContainsKey(GameTag.ATTACHED)) kind = "buff";
                    else if (fz == (int)Zone.PLAY && g.IdEntityDic.TryGetValue(f.Id, out IPlayable made) && made is Minion) kind = "spawn";
                    else if (fz == (int)Zone.HAND) kind = "tohand";
                    else if (fz == (int)Zone.DECK) kind = "todeck";
                    break;
                case PowerHistoryChangeEntity:
                    kind = "transform";
                    break;
                case PowerHistoryTagChange { Tag: (GameTag)1020, Value: 1 } used when mine && card.IsTitan &&
                                                                                           g.IdEntityDic.TryGetValue(used.EntityId, out IPlayable ability):
                    if (SabberStoneCore.Actions.Generic.AbilityKey(ability.Card) is string key) sent.Add("@ability:" + key);
                    break;
                case PowerHistoryMetaData { Type: MetaDataType.DAMAGE }:
                    kind = "damage";
                    break;
                case PowerHistoryMetaData { Type: MetaDataType.HEALING }:
                    kind = "heal";
                    break;
                case PowerHistoryTagChange t:
                    switch (t.Tag)
                    {
                        case GameTag.ZONE:
                            int old = zone.TryGetValue(t.EntityId, out int o) ? o : 0;
                            zone[t.EntityId] = t.Value;
                            if (t.Value == (int)Zone.PLAY && old != (int)Zone.PLAY && g.IdEntityDic.TryGetValue(t.EntityId, out IPlayable m2) && m2 is Minion) kind = "spawn";
                            else if (t.Value == (int)Zone.HAND && old == (int)Zone.DECK) kind = "draw";
                            else if (t.Value == (int)Zone.HAND && old != (int)Zone.HAND) kind = "tohand";
                            else if (t.Value == (int)Zone.DECK && old != (int)Zone.DECK) kind = "todeck";
                            break;
                        case GameTag.TO_BE_DESTROYED when t.Value == 1:
                            kind = "destroy";
                            break;
                        case GameTag.ATK or GameTag.HEALTH or GameTag.ARMOR or GameTag.COST or GameTag.TAUNT or GameTag.DIVINE_SHIELD or GameTag.LIFESTEAL
                            or GameTag.WINDFURY or GameTag.RUSH or GameTag.CHARGE or GameTag.POISONOUS or GameTag.STEALTH or GameTag.REBORN
                            or GameTag.SPELLPOWER or GameTag.FROZEN or GameTag.IMMUNE or GameTag.SILENCED:
                            kind = "buff";
                            break;
                    }
                    break;
            }
            if (mine && kind != null && blocks.Count > 0 && blocks.Peek() is { } inBlock) sent.Add("@" + inBlock + ":" + kind);
        }
    }

    /// <summary>Picks the first option of every open choice (Discover, Choose…), a few deep.</summary>
    private static void Resolve(SGame g)
    {
        for (int i = 0; i < 6 && g.State == State.RUNNING; i++)
        {
            Controller? c = g.Player1.Choice != null ? g.Player1 : g.Player2.Choice != null ? g.Player2 : null;
            if (c == null) return;
            var choice = c.Choice;
            if (choice.ChoiceType == ChoiceType.MULLIGAN) g.Process(ChooseTask.Mulligan(c, new List<int>()));
            else g.Process(ChooseTask.Pick(c, choice.Choices[0]));
        }
    }
}
