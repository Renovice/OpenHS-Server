using HsServer.Engine;
using PegasusGame;

namespace HsServer.Sabber;

/// <summary>
/// The client's picture of a game, rebuilt from nothing but the power history packets it receives, applied the way the real
/// client applies them (Assembly-CSharp Entity): FULL_ENTITY makes a new entity with the listed tags (every other tag 0);
/// SHOW_ENTITY / CHANGE_ENTITY set the card id and the listed tags and keep the rest (HandleEntityChange → SetTags);
/// HIDE_ENTITY sets the zone, puts ATK / HEALTH / COST back to the card's, DAMAGE to 0 and forgets the card id (OnHideEntity).
/// <para>Besides the picture, <see cref="Apply"/> checks the rules every packet must keep, rules that need no engine and so hold
/// for live recordings too (`HsServer viewcheck` runs them over the live sessions; 2026-10-09, after the Freezing Trap desync):</para>
/// <list type="bullet">
/// <item>every entity a change names exists; a SHOW_ENTITY never re-reveals a card the client already knows (a bounced minion
///   re-sent "in play at position 0" made the client lose the board); a HIDE_ENTITY hides a known card;</item>
/// <item>a card the client knows carries its card's CARDTYPE; a hidden Secret shows its class and kind (a class-less "?" played
///   no reveal);</item>
/// <item>a hidden card gets only the tags live sends for a hidden card in that zone (<see cref="HiddenTags"/>);</item>
/// <item>at the end of a packet with no block open, each player's hand and minions in play sit at positions 1..n.</item>
/// </list>
/// </summary>
internal sealed class ClientView
{
    public sealed class Ent
    {
        public string CardId = "";
        public readonly Dictionary<int, int> Tags = new();
        public int this[GAME_TAG tag] => Tags.TryGetValue((int)tag, out int v) ? v : 0;
        public int Zone => this[GAME_TAG.ZONE];
        public int Controller => this[GAME_TAG.CONTROLLER];
        public bool Known => CardId.Length > 0;
    }

    public readonly Dictionary<int, Ent> Entities = new();

    /// <summary>The client card data (HIDE_ENTITY's reset to the card's stats); optional.</summary>
    public CardDb Cards { get; init; }

    /// <summary>Optional: every tag the stream gives a hidden entity (zone, tag) — `viewcheck` derives <see cref="HiddenTags"/> from live with it.</summary>
    public Action<int, int> HiddenTagSent { get; init; }

    /// <summary>Check what a hidden entity may be told (off for the live calibration itself).</summary>
    public bool CheckHiddenTags { get; init; } = true;

    private int depth; // blocks open across packets

    public bool BlockOpen => depth > 0;

    public Ent Get(int id) => Entities.TryGetValue(id, out Ent e) ? e : null;

    // what live tells a hidden card about to enter the Secret zone (ClientStream.SecretShownTags; live heist-run-20260929 #2088)
    private static readonly HashSet<int> SecretEntryTags = new()
    {
        (int)GAME_TAG.ZONE_POSITION, (int)GAME_TAG.EXHAUSTED, (int)GAME_TAG.SECRET, (int)GAME_TAG.SIDE_QUEST, (int)GAME_TAG.CLASS,
        (int)GAME_TAG.MULTIPLE_CLASSES, (int)GAME_TAG.COST, (int)GAME_TAG.TAG_LAST_KNOWN_COST_IN_HAND, (int)GAME_TAG.SECRET_LOCKED,
    };

    /// <summary>
    /// Tags live gives an entity whose card the viewer doesn't know, per zone (2026-10-09, from every hidden entity of the live
    /// sessions — `HsServer viewcheck --hidden`). Anything else on a hidden card either leaks what it is or is a tag the client
    /// reads from a card it can't know. 0 = in any zone.
    /// </summary>
    internal static readonly Dictionary<int, HashSet<int>> HiddenTags = new()
    {
        // where it is, whose it is, and the "Created by" (live: Pilfer, Psychic Conjurer — a TAG_CHANGE on the hidden card)
        [0] = new() { (int)GAME_TAG.ZONE, (int)GAME_TAG.CONTROLLER, (int)GAME_TAG.ENTITY_ID, (int)GAME_TAG.ZONE_POSITION, (int)GAME_TAG.DISPLAYED_CREATOR },
        // public states of a card nobody sees: shown to both (REVEALED: a traded card), unplayable, Immolate / Shatter / Forge /
        // Prepare marks, the "evil" glow
        [(int)TAG_ZONE.DECK] = new()
        {
            (int)GAME_TAG.REVEALED, (int)GAME_TAG.CANT_PLAY, (int)GAME_TAG.IMMOLATING, (int)GAME_TAG.IMMOLATESTAGE,
            (int)GAME_TAG.SUPPRESS_IMMOLATE_VISUAL_FOR_OPPONENT,
        },
        [(int)TAG_ZONE.HAND] = new()
        {
            (int)GAME_TAG.REVEALED, (int)GAME_TAG.CANT_PLAY, (int)GAME_TAG.PREPARE_REVEALED, (int)GAME_TAG.PREPARE, (int)GAME_TAG.EVIL_GLOW,
            (int)GAME_TAG.FORGE_REVEALED, (int)GAME_TAG.SHATTERED,
        },
        [(int)TAG_ZONE.SETASIDE] = new() { (int)GAME_TAG.REVEALED, (int)GAME_TAG.SHATTERED, (int)GAME_TAG.EVIL_GLOW },
        // a Secret: what kind, its class and cost, whether it is ready (the coloured "?"), in the zone or as it enters it
        [(int)TAG_ZONE.SECRET] = SecretEntryTags,
    };

    /// <summary>
    /// Applies one PowerHistory packet; every rule broken is reported through <paramref name="problem"/> with the packet item's
    /// index and what it was.
    /// </summary>
    public void Apply(IList<PowerHistoryData> list, Action<string> problem)
    {
        // live (heist-session-20260929-122856, the Secret of the boss): a hidden card about to enter the Secret zone is first told
        // what kind of Secret it is, its class and cost — in the zone it leaves; those tags count as the Secret zone's
        var entering = new HashSet<int>();
        for (int i = 0; i < list.Count; i++)
            if (list[i].TagChange is { Tag: (int)GAME_TAG.ZONE, Value: (int)TAG_ZONE.SECRET } z) entering.Add(z.Entity);
            else if (list[i].ShowEntity is { } shown && shown.Tags.Any(t => t.Name == (int)GAME_TAG.ZONE && t.Value == (int)TAG_ZONE.SECRET)) entering.Add(shown.Entity);
        for (int i = 0; i < list.Count; i++)
        {
            PowerHistoryData d = list[i];
            string at = $"item {i}";
            // an engine-internal tag (>= 900000: script bookkeeping, HsServer's rule tags) never reaches the client (live: none)
            foreach (int tag in TagsOf(d))
                if (tag >= InternalTagsFrom) { problem($"{at}: engine-internal tag {tag} sent"); break; }
            if (d.CreateGame != null)
            {
                Entities.Clear();
                depth = 0;
                Put(d.CreateGame.GameEntity.Id, "", d.CreateGame.GameEntity.Tags);
                foreach (var p in d.CreateGame.Players) Put(p.Entity.Id, "", p.Entity.Tags);
            }
            else if (d.ResetGame != null)
            {
                // a Rewind's GAME_RESET: every entity is sent again (FULL_ENTITY), and the client drops every entity above the
                // highest one re-sent when the reset block ends (the discarded timeline's: RewindGameSpellController)
                resetDepth = depth;
                resetMax = 0;
            }
            else if (d.FullEntity is { } f)
            {
                if (resetDepth > 0) resetMax = Math.Max(resetMax, f.Entity);
                else if (Entities.ContainsKey(f.Entity))
                    problem($"{at}: FULL_ENTITY of entity {f.Entity}, which the client already has ({Describe(f.Entity)})");
                Ent e = Put(f.Entity, f.Name, f.Tags);
                // (not in a reset: it sends the client back what it had — live rewind session-20261001-130515 59738 re-sends a hidden
                // hand card's ATK / HEALTH / COST)
                if (!e.Known && !IsGameOrPlayer(f.Entity) && resetDepth == 0)
                    foreach (var t in f.Tags) Hidden(f.Entity, e, t.Name, t.Value, entering, at, problem);
            }
            else if (d.ShowEntity is { } s)
            {
                if (Get(s.Entity) is not { } e) { problem($"{at}: SHOW_ENTITY of unknown entity {s.Entity}"); continue; }
                if (e.Known) problem($"{at}: SHOW_ENTITY of entity {s.Entity}, already known as {e.CardId} in {(TAG_ZONE)e.Zone}: a public card is moved, never re-revealed");
                if (string.IsNullOrEmpty(s.Name)) problem($"{at}: SHOW_ENTITY of entity {s.Entity} without a card id");
                e.CardId = s.Name ?? "";
                foreach (var t in s.Tags) e.Tags[t.Name] = t.Value;
            }
            else if (d.ChangeEntity is { } c)
            {
                if (Get(c.Entity) is not { } e) { problem($"{at}: CHANGE_ENTITY of unknown entity {c.Entity}"); continue; }
                if (!e.Known) problem($"{at}: CHANGE_ENTITY of hidden entity {c.Entity} (reveals it as {c.Name})");
                if (!string.IsNullOrEmpty(c.Name)) e.CardId = c.Name;
                foreach (var t in c.Tags) e.Tags[t.Name] = t.Value;
            }
            else if (d.HideEntity is { } h)
            {
                if (Get(h.Entity) is not { } e) { problem($"{at}: HIDE_ENTITY of unknown entity {h.Entity}"); continue; }
                if (!e.Known) problem($"{at}: HIDE_ENTITY of entity {h.Entity}, which the client doesn't know");
                e.Tags[(int)GAME_TAG.ZONE] = h.Zone;
                // the client: ATK / HEALTH / COST back to the card's, DAMAGE 0, no card id
                if (Cards != null && Cards.TryGet(e.CardId, out CardDef card))
                {
                    e.Tags[(int)GAME_TAG.ATK] = card.Atk;
                    e.Tags[(int)GAME_TAG.HEALTH] = card.Health;
                    e.Tags[(int)GAME_TAG.COST] = card.Cost;
                }
                e.Tags[(int)GAME_TAG.DAMAGE] = 0;
                e.CardId = "";
            }
            else if (d.TagChange is { } tc)
            {
                if (Get(tc.Entity) is not { } e) { problem($"{at}: TAG_CHANGE of unknown entity {tc.Entity}"); continue; }
                if (!e.Known && !IsGameOrPlayer(tc.Entity)) Hidden(tc.Entity, e, tc.Tag, tc.Value, entering, at, problem);
                e.Tags[tc.Tag] = tc.Value;
            }
            else if (d.PowerStart != null) depth++;
            else if (d.PowerEnd != null)
            {
                depth = Math.Max(0, depth - 1);
                if (resetDepth > 0 && depth < resetDepth)
                {
                    foreach (int id in Entities.Keys.Where(id => id > resetMax).ToList()) Entities.Remove(id);
                    resetDepth = 0;
                }
            }
        }
        CheckState(problem);
    }

    private int resetDepth, resetMax;

    private const int InternalTagsFrom = 900000; // SabberStoneCore.Kettle.PowerHistoryBuilder.InternalTagsFrom

    private static IEnumerable<int> TagsOf(PowerHistoryData d)
    {
        if (d.TagChange != null) return new[] { d.TagChange.Tag };
        if (d.CreateGame != null)
            return d.CreateGame.GameEntity.Tags.Select(t => t.Name).Concat(d.CreateGame.Players.SelectMany(p => p.Entity.Tags.Select(t => t.Name)));
        return (d.FullEntity?.Tags ?? d.ShowEntity?.Tags ?? d.ChangeEntity?.Tags ?? new List<Tag>()).Select(t => t.Name);
    }

    private bool IsGameOrPlayer(int id) => Get(id) is { } e && e[GAME_TAG.CARDTYPE] is (int)TAG_CARDTYPE.GAME or (int)TAG_CARDTYPE.PLAYER;

    private void Hidden(int id, Ent e, int tag, int value, HashSet<int> entering, string at, Action<string> problem)
    {
        int zone = entering.Contains(id) && SecretEntryTags.Contains(tag) ? (int)TAG_ZONE.SECRET : e.Zone;
        HiddenTagSent?.Invoke(zone, tag);
        if (!CheckHiddenTags || HiddenTags[0].Contains(tag) || HiddenTags.TryGetValue(zone, out var allowed) && allowed.Contains(tag)) return;
        problem($"{at}: hidden entity {id} in {(TAG_ZONE)zone} of player {e.Controller} is told {Name(tag)}={value} (live never tells a hidden card that)");
    }

    /// <summary>What must hold whenever the client has applied a packet.</summary>
    private void CheckState(Action<string> problem)
    {
        foreach (var (id, e) in Entities)
        {
            if (IsGameOrPlayer(id)) continue;
            // a card the client knows is drawn from its tags: no type = the client can't place it (an empty card); a Secret's kind
            // and class build the coloured "?" (live: the eight Secret tags of every hidden card entering the Secret zone)
            // (live: a known card's CARDTYPE is its card's, always — 1.6 million entity states in 385 games, `viewcheck --identity`)
            if (e.Known && e[GAME_TAG.CARDTYPE] == 0)
                problem($"entity {id} {e.CardId} in {(TAG_ZONE)e.Zone} is known without a CARDTYPE");
            else if (e.Known && Cards != null && Cards.TryGet(e.CardId, out CardDef def) && def.Tag(GAME_TAG.CARDTYPE) != e[GAME_TAG.CARDTYPE])
                problem($"entity {id} {e.CardId} in {(TAG_ZONE)e.Zone} is known with CARDTYPE {e[GAME_TAG.CARDTYPE]}, its card's is {def.Tag(GAME_TAG.CARDTYPE)}");
            if (!e.Known && e.Zone == (int)TAG_ZONE.SECRET && (e[GAME_TAG.CLASS] == 0 || e[GAME_TAG.SECRET] == 0 && e[GAME_TAG.SIDE_QUEST] == 0 && e[GAME_TAG.QUEST] == 0))
                problem($"hidden entity {id} in the Secret zone of player {e.Controller} shows CLASS={e[GAME_TAG.CLASS]} SECRET={e[GAME_TAG.SECRET]}: the client draws a broken \"?\"");
        }
        if (depth > 0) return;
        // positions: each player's hand and minions / locations in play are 1..n (the client lays them out by ZONE_POSITION)
        foreach (var group in Entities.Where(x => !IsGameOrPlayer(x.Key) && Positioned(x.Value)).GroupBy(x => (x.Value.Controller, x.Value.Zone)))
        {
            var positions = group.Select(x => x.Value[GAME_TAG.ZONE_POSITION]).OrderBy(p => p).ToList();
            if (!positions.SequenceEqual(Enumerable.Range(1, positions.Count)))
                problem($"player {group.Key.Controller} {(TAG_ZONE)group.Key.Zone} positions are [{string.Join(",", group.OrderBy(x => x.Value[GAME_TAG.ZONE_POSITION]).Select(x => $"{x.Key}@{x.Value[GAME_TAG.ZONE_POSITION]}"))}], not 1..{positions.Count}");
        }
    }

    private static bool Positioned(Ent e) =>
        e.Zone == (int)TAG_ZONE.HAND
        || e.Zone == (int)TAG_ZONE.PLAY && e[GAME_TAG.CARDTYPE] is (int)TAG_CARDTYPE.MINION or (int)TAG_CARDTYPE.LOCATION;

    public string Describe(int id) => Get(id) is { } e ? $"{(e.Known ? e.CardId : "hidden")} in {(TAG_ZONE)e.Zone} of player {e.Controller}" : "unknown";

    internal static string Name(int tag) => Enum.IsDefined(typeof(GAME_TAG), tag) ? ((GAME_TAG)tag).ToString() : tag.ToString();

    private Ent Put(int id, string cardId, List<Tag> tags)
    {
        var e = new Ent { CardId = cardId ?? "" };
        foreach (var t in tags) e.Tags[t.Name] = t.Value;
        Entities[id] = e;
        return e;
    }
}
