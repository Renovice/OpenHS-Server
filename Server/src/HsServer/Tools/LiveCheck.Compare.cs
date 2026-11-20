using PegasusGame;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using Zone = SabberStoneCore.Enums.Zone;

namespace HsServer.Tools;

/// <summary>`livecheck`, part 3: what an action changed (live and ours), and the differences.</summary>
internal static partial class LiveCheck
{
    /// <summary>The rules state of one entity: its card, owner, zone and the numbers / keywords that matter.</summary>
    internal sealed record ESnap(string Card, int Ctrl, int Zone, int Type, int Atk, int Health, int Damage, int Armor, int Dur, int Cost, int Kw, bool Visible);

    /// <summary>What an action did: per existing entity (live id) its changed attributes; the new cards; the players' counters.</summary>
    internal sealed class Outcome
    {
        public readonly SortedDictionary<int, SortedDictionary<string, string>> Changes = new();
        public sealed record NewCard(string Side, string Zone, string Card, string Type, string Stats);
        public readonly List<NewCard> New = new();
        public readonly SortedDictionary<string, string> Players = new(StringComparer.Ordinal);

        public string Signature() =>
            string.Join(";", Changes.Select(c => c.Key + ":" + string.Join(",", c.Value.Select(v => v.Key + "=" + v.Value)))) + "|" +
            string.Join(";", New.Select(n => $"{n.Side}/{n.Zone}/{n.Card}/{n.Stats}").OrderBy(s => s, StringComparer.Ordinal)) + "|" +
            string.Join(";", Players.Select(p => p.Key + "=" + p.Value));
    }

    private static readonly GameTag[] PlayerCounters =
    {
        GameTag.RESOURCES, GameTag.OVERLOAD_OWED, GameTag.OVERLOAD_LOCKED, GameTag.CORPSES,
    };

    /// <summary>The players' numbers compared: Mana Crystals, the Mana left this turn (live and ours keep it in different tags: The Coin
    /// lowers RESOURCES_USED live, raises TEMP_RESOURCES here), Overload, Corpses.</summary>
    private static Dictionary<string, int> Counters(Func<GameTag, int> get)
    {
        var d = new Dictionary<string, int>();
        foreach (GameTag t in PlayerCounters) d[t.ToString()] = get(t);
        d["MANA_LEFT"] = get(GameTag.RESOURCES) + get(GameTag.TEMP_RESOURCES) - get(GameTag.RESOURCES_USED); // (USED counts the locked ones)
        return d;
    }

    /// <summary>The same card for the rules: the same id, or a cosmetic version of it (a hero skin's Silver Hand Recruit CS2_101t2…t8,
    /// a Coin skin) — same name, type, cost, stats and text.</summary>
    internal static bool Equivalent(string x, string y)
    {
        if (x == y) return true;
        Card a = CardFor(x), b = CardFor(y);
        if (a == null || b == null) return false;
        if (a.Id == b.Id) return true;
        return a.Name == b.Name && a.Type == b.Type && a.Cost == b.Cost && a.ATK == b.ATK && a.Health == b.Health && a.Text == b.Text;
    }

    private static int NormZone(int z) => z == (int)Zone.REMOVEDFROMGAME ? (int)Zone.SETASIDE : z;

    private static int KwMask(Func<GameTag, int> get)
    {
        int m = 0;
        for (int i = 0; i < Keywords.Length; i++)
            if (get(Keywords[i]) > 0) m |= 1 << i;
        return m;
    }

    private static string KwText(int mask, string sign) =>
        string.Join(" ", Enumerable.Range(0, Keywords.Length).Where(i => (mask & (1 << i)) != 0).Select(i => sign + Keywords[i]));

    internal static ESnap LiveSnap(LiveAction a, LiveEntity e, bool after) => new(
        Canon(a.CardOf(e.Id, after)), e[GameTag.CONTROLLER], NormZone(e[GameTag.ZONE]), e[GameTag.CARDTYPE], e[GameTag.ATK], e[GameTag.HEALTH],
        e[GameTag.DAMAGE], e[GameTag.ARMOR], LiveDurability(e), e[GameTag.COST], KwMask(t => e[t]), e.Visible);

    internal static ESnap OurSnap(IPlayable p)
    {
        int zone = p.Zone != null ? (int)p.Zone.Type : p[GameTag.ZONE];
        if (p is Hero or HeroPower && p.Zone == null && zone == 0) zone = (int)Zone.PLAY;
        // (a dead hero: live moves it to the graveyard when the game ends)
        if (p is Hero hero && hero == hero.Controller?.Hero && (hero.Health <= 0 || hero.ToBeDestroyed)) zone = (int)Zone.GRAVEYARD;
        int Safe(GameTag t) { try { return p[t]; } catch (Exception) { return 0; } }
        return new ESnap(p.Card.Id, p.Controller?.PlayerId ?? 0, NormZone(zone), (int)p.Card.Type, Safe(GameTag.ATK), Safe(GameTag.HEALTH),
            Safe(GameTag.DAMAGE), p is Character armored ? armored.Armor : Safe(GameTag.ARMOR), Safe(GameTag.DURABILITY), p.Cost, KwMask(Safe), true); // (Armor: the hero's own, not a hero card's printed one)
    }

    /// <summary>The attributes that changed between two snapshots of one entity.</summary>
    private static SortedDictionary<string, string> Changed(ESnap b, ESnap f)
    {
        var c = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (b == null || f == null) return c;
        if (f.Zone != b.Zone) c["zone"] = ZoneName(f.Zone);
        if (f.Ctrl != b.Ctrl) c["owner"] = "changed";
        if (f.Card.Length > 0 && b.Card.Length > 0 && f.Card != b.Card) c["card"] = f.Card;
        if (f.Zone == (int)Zone.PLAY && f.Visible)
        {
            bool was = b.Zone == (int)Zone.PLAY && b.Visible;
            void Stat(string name, int before, int now, bool shows)
            {
                if (!shows) return;
                if (!was) c[name] = "=" + now;
                else if (now != before) c[name] = (now > before ? "+" : "") + (now - before);
            }
            bool character = f.Type is (int)CardType.MINION or (int)CardType.HERO or (int)CardType.LOCATION;
            Stat("ATK", b.Atk, f.Atk, character || f.Type == (int)CardType.WEAPON);
            Stat("HEALTH", b.Health, f.Health, character);
            Stat("DAMAGE", b.Damage, f.Damage, character || f.Type == (int)CardType.WEAPON);
            Stat("ARMOR", b.Armor, f.Armor, f.Type == (int)CardType.HERO);
            Stat("DURABILITY", b.Dur, f.Dur, f.Type == (int)CardType.WEAPON);
            if (character)
            {
                if (!was) { if (f.Kw != 0) c["kw"] = "=" + KwText(f.Kw, ""); }
                else if (f.Kw != b.Kw) c["kw"] = (KwText(f.Kw & ~b.Kw, "+") + " " + KwText(b.Kw & ~f.Kw, "-")).Trim();
            }
        }
        else if (f.Zone == (int)Zone.HAND && b.Zone == (int)Zone.HAND && f.Visible && b.Visible && f.Cost != b.Cost)
            c["COST"] = (f.Cost > b.Cost ? "+" : "") + (f.Cost - b.Cost);
        return c;
    }

    private static string StatsOf(ESnap s) =>
        s.Zone == (int)Zone.PLAY && s.Type is (int)CardType.MINION or (int)CardType.LOCATION && s.Visible ? $"{s.Atk}/{s.Health - s.Damage}" : "";

    private static bool NewKind(ESnap s) =>
        s.Type != (int)CardType.ENCHANTMENT && s.Zone is (int)Zone.PLAY or (int)Zone.HAND or (int)Zone.DECK or (int)Zone.SECRET;

    /// <summary>Live's outcome of the action.</summary>
    private static Outcome LiveOutcome(LiveAction a, int actor)
    {
        var o = new Outcome();
        string Side(int ctrl) => ctrl == actor ? "own" : "opp";
        foreach (var (id, before) in a.Before)
        {
            if (!a.After.TryGetValue(id, out LiveEntity after)) continue;
            int type = after[GameTag.CARDTYPE] != 0 ? after[GameTag.CARDTYPE] : before[GameTag.CARDTYPE];
            if (type is (int)CardType.ENCHANTMENT or (int)CardType.GAME or (int)CardType.PLAYER) continue;
            var c = Changed(LiveSnap(a, before, false), LiveSnap(a, after, true));
            if (c.Count > 0) o.Changes[id] = c;
        }
        foreach (var (id, e) in a.After)
        {
            if (id <= a.MaxIdBefore) continue;
            ESnap s = LiveSnap(a, e, true);
            if (!NewKind(s)) continue;
            if (!s.Visible && s.Zone == (int)Zone.PLAY) continue; // (a hidden rule entity: never shown)
            if (CardFor(s.Card)?.Type == CardType.ENCHANTMENT) continue;
            if (s.Type is (int)CardType.GAME or (int)CardType.PLAYER) continue;
            o.New.Add(new Outcome.NewCard(Side(s.Ctrl), ZoneName(s.Zone), s.Card, s.Type == 0 ? "" : ((CardType)s.Type).ToString(), StatsOf(s)));
        }
        foreach (LiveEntity p in a.Before.Values.Where(e => e[GameTag.CARDTYPE] == (int)CardType.PLAYER))
        {
            if (!a.After.TryGetValue(p.Id, out LiveEntity pa)) continue;
            var was = Counters(t => p[t]);
            foreach (var (k, v) in Counters(t => pa[t]))
                if (v != was[k]) o.Players[Side(p[GameTag.PLAYER_ID]) + " " + k] = (v > was[k] ? "+" : "") + (v - was[k]);
        }
        return o;
    }

    /// <summary>Our game's snapshot of every entity mapped to live (by live id), taken before the action.</summary>
    private static Dictionary<int, ESnap> OurBefore(Rebuilt r)
    {
        var s = new Dictionary<int, ESnap>();
        foreach (var (liveId, ourId) in r.LiveToOurs)
            if (r.G.IdEntityDic.TryGetValue(ourId, out IPlayable p) && p is not SabberStoneCore.Model.Entities.Enchantment)
                // (an untouchable permanent's stats are hidden live — 0 — and given 1 Health here only so it survives the death check)
                s[liveId] = r.NoHealth.Contains(ourId) ? OurSnap(p) with { Atk = 0, Health = 0, Damage = 0 } : OurSnap(p);
        return s;
    }

    private static Outcome OurOutcome(Rebuilt r, Dictionary<int, ESnap> before)
    {
        var o = new Outcome();
        int actor = r.Actor.PlayerId;
        string Side(int ctrl) => ctrl == actor ? "own" : "opp";
        foreach (var (liveId, b) in before)
        {
            if (!r.G.IdEntityDic.TryGetValue(r.LiveToOurs[liveId], out IPlayable p)) continue;
            var c = Changed(b, OurSnap(p));
            if (r.Placeholders.Contains(p.Id)) c.Remove("card");
            if (r.NoHealth.Contains(p.Id) && p is Minion still && still.Untouchable) // (2026-11-20: until it becomes a real minion — a Violet Prison)
                foreach (string stat in new[] { "ATK", "HEALTH", "DAMAGE" }) c.Remove(stat);
            if (c.Count > 0) o.Changes[liveId] = c;
        }
        foreach (var (id, p) in r.G.IdEntityDic)
        {
            if (id <= r.MaxIdBefore || p == null) continue;
            ESnap s = OurSnap(p);
            if (!NewKind(s)) continue;
            o.New.Add(new Outcome.NewCard(Side(s.Ctrl), ZoneName(s.Zone), s.Card, ((CardType)s.Type).ToString(), StatsOf(s)));
        }
        return o;
    }

    /// <summary>The players' counters before (ours), to diff after the action.</summary>
    private static Dictionary<int, Dictionary<string, int>> OurCounters(Rebuilt r) =>
        new[] { r.G.Player1, r.G.Player2 }.ToDictionary(c => c.PlayerId, c => Counters(t => c[t]));

    private static void AddCounters(Outcome o, Rebuilt r, Dictionary<int, Dictionary<string, int>> before)
    {
        foreach (Controller c in new[] { r.G.Player1, r.G.Player2 })
            foreach (var (k, now) in Counters(t => c[t]))
            {
                int was = before[c.PlayerId][k];
                if (now != was) o.Players[(c == r.Actor ? "own" : "opp") + " " + k] = (now > was ? "+" : "") + (now - was);
            }
    }

    /// <summary>Live vs ours: every difference as text (empty: the same outcome).</summary>
    private static List<string> Differences(LiveAction a, Outcome live, Outcome ours, Rebuilt r) => Differences(a, live, ours, r, out _);

    /// <param name="forcedOnly">every difference is on a number the rebuild had to force (Rebuilt.Fitted): likely the rebuild's</param>
    private static List<string> Differences(LiveAction a, Outcome live, Outcome ours, Rebuilt r, out bool forcedOnly)
    {
        var diffs = new List<string>();
        int forced = 0;
        string Who(int id)
        {
            LiveEntity e = a.Before.GetValueOrDefault(id) ?? a.After.GetValueOrDefault(id);
            string card = a.CardOf(id);
            int ctrl = e?[GameTag.CONTROLLER] ?? 0;
            string side = ctrl == r.Actor.PlayerId ? "own" : "opp";
            int type = e?[GameTag.CARDTYPE] ?? 0;
            return $"{(card.Length > 0 ? card : "hidden")}#{id} ({side} {(type != 0 ? ((CardType)type).ToString().ToLowerInvariant() : "card")} in {ZoneName(e?[GameTag.ZONE] ?? 0)})";
        }
        bool hiddenLive(int id) => !(a.After.GetValueOrDefault(id)?.Visible ?? false);
        // the source first, then the rest by id
        var ids = live.Changes.Keys.Union(ours.Changes.Keys).OrderBy(id => id == a.Source ? 0 : 1).ThenBy(id => id);
        foreach (int id in ids)
        {
            // (a hidden entity our position left out — a hidden enchantment or rule entity: its moves are live's bookkeeping, nothing
            // of ours to compare; one that acted made the action NOT-REBUILT)
            if (!r.LiveToOurs.ContainsKey(id) && !(a.Before.GetValueOrDefault(id)?.Visible ?? false) && !(a.After.GetValueOrDefault(id)?.Visible ?? false)) continue;
            var l = live.Changes.GetValueOrDefault(id) ?? new SortedDictionary<string, string>();
            var o = ours.Changes.GetValueOrDefault(id) ?? new SortedDictionary<string, string>();
            foreach (string attr in l.Keys.Union(o.Keys).OrderBy(k => k == "zone" ? 0 : k == "card" ? 1 : 2).ThenBy(k => k, StringComparer.Ordinal))
            {
                string lv = l.GetValueOrDefault(attr), ov = o.GetValueOrDefault(attr);
                if (lv == ov || attr == "card" && lv != null && ov != null && Equivalent(lv, ov)) continue;
                // a card id live never showed (an opponent's hidden card): nothing to compare
                if (attr == "card" && (lv == null && hiddenLive(id) || r.LiveToOurs.TryGetValue(id, out int oid) && r.Placeholders.Contains(oid))) continue;
                // a hand card's Cost the viewer never saw change (an opponent's hidden hand): nothing to compare
                if (attr == "COST" && lv == null && !(a.Before.GetValueOrDefault(id)?.Visible == true && a.After.GetValueOrDefault(id)?.Visible == true)) continue;
                diffs.Add($"{Who(id)}: {attr} live {lv ?? "unchanged"}, ours {ov ?? "unchanged"}");
                if (r.Fitted.Contains((id, attr))) forced++;
            }
        }
        // new cards: the same card (or a cosmetic version of it: Equivalent) in the same place first, then a live card the viewer
        // didn't see matches any of ours in the same place
        string Show(Outcome.NewCard n) => $"{n.Side} {n.Zone} {(n.Card.Length > 0 ? n.Card : "(hidden)")}{(n.Stats.Length > 0 ? " " + n.Stats : "")}";
        var oursLeft = ours.New.ToList();
        var liveLeft = new List<Outcome.NewCard>();
        foreach (var n in live.New.OrderBy(n => n.Card.Length == 0 ? 1 : 0))
        {
            // (2026-11-16: ours copying a hidden card the rebuild filled with its placeholder — Psychic Conjurer copying the opponent's
            // deck — can't know the card: any live card in that place)
            int i = oursLeft.FindIndex(x => x.Side == n.Side && x.Zone == n.Zone &&
                                            (n.Card.Length == 0 || x.Card == Placeholder || x.Stats == n.Stats && Equivalent(x.Card, n.Card)));
            if (i >= 0) oursLeft.RemoveAt(i); else liveLeft.Add(n);
        }
        if (liveLeft.Count > 0 || oursLeft.Count > 0)
            diffs.Add($"new cards: live [{string.Join(", ", liveLeft.Select(Show))}], ours [{string.Join(", ", oursLeft.Select(Show))}]");
        foreach (string key in live.Players.Keys.Union(ours.Players.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            string lv = live.Players.GetValueOrDefault(key), ov = ours.Players.GetValueOrDefault(key);
            if (lv != ov) diffs.Add($"player {key}: live {lv ?? "unchanged"}, ours {ov ?? "unchanged"}");
        }
        forcedOnly = diffs.Count > 0 && forced == diffs.Count;
        return diffs;
    }

    /// <summary>The outcome without who exactly: per side and kind of entity (a random target / card can be another).</summary>
    private static List<string> Shape(Outcome o, LiveAction a, Rebuilt r)
    {
        var s = new List<string>();
        foreach (var (id, c) in o.Changes)
        {
            LiveEntity e = a.Before.GetValueOrDefault(id);
            string side = e?[GameTag.CONTROLLER] == r.Actor.PlayerId ? "own" : "opp";
            int type = e?[GameTag.CARDTYPE] ?? 0;
            string kind = type is (int)CardType.MINION or (int)CardType.HERO ? "char" : type == 0 ? "card" : ((CardType)type).ToString();
            foreach (var (k, v) in c) s.Add($"{side} {kind} {k}{(k is "card" ? "" : "=" + v)}");
        }
        foreach (var n in o.New) s.Add($"new {n.Side} {n.Zone}");
        foreach (var (k, v) in o.Players) s.Add($"{k}={v}");
        s.Sort(StringComparer.Ordinal);
        return s;
    }

    private static readonly System.Reflection.FieldInfo AppliedIds =
        typeof(SabberStoneCore.Auras.Aura).GetField("AppliedEntityIdCollection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    /// <summary>What modifies our entity: its enchantments and the auras on it (" (ours: ...)"), for a rebuild mismatch's reason.</summary>
    private static string Modifiers(Rebuilt r, int liveId)
    {
        if (!r.LiveToOurs.TryGetValue(liveId, out int id) || !r.G.IdEntityDic.TryGetValue(id, out IPlayable p)) return "";
        var parts = new List<string>();
        if (p.AppliedEnchantments?.Count > 0) parts.Add("enchantments " + string.Join(",", p.AppliedEnchantments.Select(x => x.Card.Id)));
        var auras = new List<string>();
        foreach (var aura in r.G.Auras.OfType<SabberStoneCore.Auras.Aura>())
            try
            {
                object ids = AppliedIds?.GetValue(aura);
                bool on = ids?.GetType().GetMethod("Contains", new[] { typeof(int) })?.Invoke(ids, new object[] { id }) is true;
                if (on) auras.Add(aura.Owner.Card.Id);
            }
            catch (Exception) { }
        if (auras.Count > 0) parts.Add("auras of " + string.Join(",", auras.Distinct()));
        return parts.Count > 0 ? $" (ours: {string.Join("; ", parts)})" : "";
    }

    /// <summary>Where the rebuilt position doesn't show what live showed (before the action).</summary>
    private static List<string> RebuildMismatches(LiveAction a, Rebuilt r, Dictionary<int, ESnap> ours, List<string> minor)
    {
        var m = new List<string>();
        foreach (var (liveId, o) in ours)
        {
            if (!a.Before.TryGetValue(liveId, out LiveEntity e) || e[GameTag.CARDTYPE] is (int)CardType.PLAYER or (int)CardType.GAME) continue;
            if (liveId == a.Source && r.SourceView != null) e = r.SourceView;
            ESnap l = LiveSnap(a, e, false);
            string who = $"{(l.Card.Length > 0 ? l.Card : "hidden")}#{liveId}";
            if (l.Zone != o.Zone) { m.Add($"{who} zone live {ZoneName(l.Zone)} ours {ZoneName(o.Zone)}"); continue; }
            if (!l.Visible || r.Placeholders.Contains(r.LiveToOurs[liveId])) continue;
            if (l.Zone == (int)Zone.PLAY)
            {
                bool character = l.Type is (int)CardType.MINION or (int)CardType.HERO or (int)CardType.LOCATION;
                if (r.NoHealth.Contains(r.LiveToOurs[liveId]) && r.G.IdEntityDic.TryGetValue(r.LiveToOurs[liveId], out IPlayable hidden) && hidden is Minion { Untouchable: true }) continue; // (an untouchable permanent's hidden stats)
                if ((character || l.Type == (int)CardType.WEAPON) && l.Atk != o.Atk) m.Add($"{who} ATK live {l.Atk} ours {o.Atk}{Modifiers(r, liveId)}");
                if (character && l.Health - l.Damage != o.Health - o.Damage) m.Add($"{who} health live {l.Health - l.Damage} ours {o.Health - o.Damage}");
                if (l.Type == (int)CardType.HERO && l.Armor != o.Armor) m.Add($"{who} ARMOR live {l.Armor} ours {o.Armor}");
                if (l.Type == (int)CardType.WEAPON && l.Dur - l.Damage != o.Dur - o.Damage) m.Add($"{who} durability live {l.Dur - l.Damage} ours {o.Dur - o.Damage}");
                // (a hero's keyword our aura gives it — the engine reads a Hero Power damage aura as Taunt on a hero: a difference of the
                // engine's to note, not one that stops the check)
                if (character && l.Kw != o.Kw) (l.Type == (int)CardType.HERO ? minor : m).Add($"{who} keywords live [{KwText(l.Kw, "")}] ours [{KwText(o.Kw, "")}]{Modifiers(r, liveId)}");
            }
            // (a hand card's Cost from what the engine counts its own way — attacks this game, spell schools cast: only a note, unless
            // it's the card played)
            if (l.Zone == (int)Zone.HAND && l.Cost != o.Cost) (liveId == a.Source ? m : minor).Add($"{who} cost live {l.Cost} ours {o.Cost}");
        }
        return m;
    }
}
