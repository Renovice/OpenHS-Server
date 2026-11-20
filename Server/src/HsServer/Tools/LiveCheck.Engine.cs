using PegasusGame;
using SabberStoneCore.Config;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;
using SGame = SabberStoneCore.Model.Game;
using Zone = SabberStoneCore.Enums.Zone;

namespace HsServer.Tools;

/// <summary>`livecheck`, part 2: the live position rebuilt in our engine, the live action done there, and what our game looks like.</summary>
internal static partial class LiveCheck
{
    /// <summary>A card for a hidden card (an opponent's hand or deck card the recording never showed): a plain minion.</summary>
    private const string Placeholder = "CS2_182";

    private sealed class Rebuilt
    {
        public SGame G;
        public Controller Actor;
        public readonly Dictionary<int, int> LiveToOurs = new(), OursToLive = new();
        public readonly HashSet<int> Placeholders = new(); // our ids made from a placeholder card
        public int MaxIdBefore;
        public readonly List<string> Notes = new();
        /// <summary>Live enchantments an aura makes (left to our aura: its own enchantment stands for them).</summary>
        public readonly HashSet<int> AuraMade = new();
        /// <summary>Numbers the rebuild had to force (live showed a modifier the rebuilt game didn't make): (live id, attribute).</summary>
        public readonly HashSet<(int, string)> Fitted = new();
        /// <summary>Our ids of untouchable permanents live shows without Health (kept alive with 1: their Health isn't compared).</summary>
        public readonly HashSet<int> NoHealth = new();
        /// <summary>The played card as the play showed it (a hidden or no longer seen hand card), or null.</summary>
        public LiveEntity SourceView;
    }

    /// <summary>Not rebuilt: why (the position or the action can't be reproduced in our engine).</summary>
    private sealed class NotRebuilt : Exception
    {
        public NotRebuilt(string why) : base(why) { }
    }

    // tags never copied from live (identity, zone, references to other entities, stats set apart)
    private static readonly HashSet<int> NotCopied = new[]
    {
        GameTag.ENTITY_ID, GameTag.CONTROLLER, GameTag.ZONE, GameTag.ZONE_POSITION, GameTag.CARDTYPE, GameTag.CREATOR, GameTag.CREATOR_DBID,
        GameTag.DISPLAYED_CREATOR, GameTag.LINKED_ENTITY, GameTag.ATTACHED, GameTag.PARENT_CARD, GameTag.TAG_SCRIPT_DATA_ENT_1,
        GameTag.TAG_SCRIPT_DATA_ENT_2, GameTag.LAST_AFFECTED_BY, GameTag.ATK, GameTag.HEALTH, GameTag.DAMAGE, GameTag.COST, GameTag.DURABILITY,
        GameTag.HERO_POWER, GameTag.SILENCED, GameTag.PLAYER_ID, GameTag.TEAM_ID, GameTag.HERO_ENTITY, GameTag.CURRENT_PLAYER,
        GameTag.FIRST_PLAYER, GameTag.PLAYSTATE, GameTag.MULLIGAN_STATE, GameTag.TIMEOUT, GameTag.STARTHANDSIZE, GameTag.LAST_CARD_PLAYED,
        GameTag.CARD_SET, GameTag.CLASS, GameTag.RARITY, GameTag.FACTION, GameTag.PREMIUM, GameTag.TAG_LAST_KNOWN_COST_IN_HAND,
        GameTag.CONTROLLER_CHANGED_THIS_TURN, GameTag.JUST_PLAYED, GameTag.CARDRACE,
        GameTag.HEALTH_MINIMUM, GameTag.DORMANT,
        GameTag.ATTACKABLE_BY_RUSH, // (live leaves it on after the minion's first turn: set below only while it means something)
    }.Select(t => (int)t).ToHashSet();

    /// <summary>Rules keywords and states compared (and enforced on a rebuilt visible entity).</summary>
    internal static readonly GameTag[] Keywords =
    {
        GameTag.TAUNT, GameTag.DIVINE_SHIELD, GameTag.STEALTH, GameTag.WINDFURY, GameTag.LIFESTEAL, GameTag.POISONOUS, GameTag.RUSH,
        GameTag.CHARGE, GameTag.REBORN, GameTag.FROZEN, GameTag.IMMUNE, GameTag.SILENCED, GameTag.DORMANT, GameTag.CANT_ATTACK,
    };

    private static readonly GameTag[] StateTags =
    {
        GameTag.EXHAUSTED, GameTag.NUM_ATTACKS_THIS_TURN, GameTag.FROZEN, GameTag.NUM_TURNS_IN_PLAY,
    };

    private static readonly Dictionary<int, Card> byDbf = new();

    /// <summary>The client's card data (for cards the engine has no card of: a cosmetic Coin is The Coin).</summary>
    internal static Engine.CardDb ClientDb;

    private static Card CardFor(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        Card c = Cards.TryFromId(id);
        if (c == null && ClientDb != null && ClientDb.TryGet(id, out Engine.CardDef def) && def.Tags.GetValueOrDefault(2088) > 0)
            c = Cards.TryFromId("GAME_005");
        return c == null ? null : SabberStoneCore.Loader.ReprintCards.Current(c) ?? c;
    }

    /// <summary>The card id the engine uses for a live card id (renamed reprints), for comparing.</summary>
    internal static string Canon(string id) => CardFor(id)?.Id ?? id ?? "";

    private static Card FromDbf(int dbf)
    {
        lock (byDbf)
        {
            if (byDbf.Count == 0)
                foreach (Card c in Cards.All) byDbf[c.AssetId] = c;
            return byDbf.GetValueOrDefault(dbf);
        }
    }

    /// <summary>The live position just before <paramref name="a"/>, built in a new engine game.</summary>
    private static Rebuilt Rebuild(LiveAction a, long seed, bool leftDeckAtBottom = false)
    {
        var live = a.Before;
        LiveEntity sourceView = null;
        // a card played from a hand the viewer didn't see — or saw once, then lost sight of (an opponent's card returned to hand: its
        // tags are the old ones) — as the play showed it (its buffs and Cost in hand), still in the hand
        if (a.SourceShown != null && live.TryGetValue(a.Source, out LiveEntity hiddenSource) && (!hiddenSource.Visible || hiddenSource[GameTag.ZONE] == (int)Zone.HAND))
        {
            live = new Dictionary<int, LiveEntity>(live);
            LiveEntity shown = hiddenSource.Copy();
            foreach (var (tag, value) in a.SourceShown)
                if (tag is not ((int)GameTag.ZONE or (int)GameTag.ZONE_POSITION or (int)GameTag.JUST_PLAYED or (int)GameTag.EXHAUSTED or (int)GameTag.CONTROLLER))
                    shown.Tags[tag] = value;
            live[a.Source] = shown;
            sourceView = shown;
        }
        var players = live.Values.Where(e => e[GameTag.CARDTYPE] == (int)CardType.PLAYER).ToDictionary(e => e[GameTag.PLAYER_ID]);
        if (!players.ContainsKey(1) || !players.ContainsKey(2)) throw new NotRebuilt("no two players");
        LiveEntity actorEntity = players.Values.FirstOrDefault(p => p[GameTag.CURRENT_PLAYER] == 1) ?? throw new NotRebuilt("no current player");
        int actorId = actorEntity[GameTag.PLAYER_ID];
        int PlayerOf(LiveEntity e) => e[GameTag.CONTROLLER];

        var heroes = new Card[3];
        var heroPowers = new Card[3];
        var heroEntity = new int[3];
        var powerEntity = new int[3];
        foreach (int pid in new[] { 1, 2 })
        {
            heroEntity[pid] = players[pid][GameTag.HERO_ENTITY];
            string heroCard = a.CardOf(heroEntity[pid]);
            heroes[pid] = CardFor(heroCard) ?? throw new NotRebuilt($"hero card unknown: '{heroCard}'");
            if (heroes[pid].Type != CardType.HERO) throw new NotRebuilt($"hero {heroCard} isn't a hero card in our data");
            var powers = live.Values.Where(e => e[GameTag.CARDTYPE] == (int)CardType.HERO_POWER && e[GameTag.ZONE] == (int)Zone.PLAY && PlayerOf(e) == pid)
                .OrderBy(e => e.Id).ToList();
            int wantDbf = live.TryGetValue(heroEntity[pid], out var he) ? he[GameTag.HERO_POWER] : 0;
            LiveEntity main = powers.FirstOrDefault(p => CardFor(a.CardOf(p.Id)) is Card c && c.AssetId == wantDbf) ?? powers.FirstOrDefault();
            if (main != null)
            {
                powerEntity[pid] = main.Id;
                heroPowers[pid] = CardFor(a.CardOf(main.Id)) ?? throw new NotRebuilt($"hero power unknown: '{a.CardOf(main.Id)}'");
            }
            else heroPowers[pid] = FromDbf(wantDbf);
        }

        Card placeholder = Cards.FromId(Placeholder);
        CardClass ClassOf(int pid)
        {
            var c = live.TryGetValue(heroEntity[pid], out var h) ? (CardClass)h[GameTag.CLASS] : heroes[pid].Class;
            return c is CardClass.INVALID or CardClass.NEUTRAL or CardClass.DREAM or CardClass.WHIZBANG ? CardClass.MAGE : c;
        }
        var g = new SGame(new GameConfig
        {
            StartPlayer = actorId, Player1HeroClass = ClassOf(1), Player2HeroClass = ClassOf(2),
            Player1HeroCard = heroes[1], Player2HeroCard = heroes[2],
            Player1HeroPowerCard = heroPowers[1], Player2HeroPowerCard = heroPowers[2],
            Player1Deck = Enumerable.Repeat(placeholder, 6).ToList(), Player2Deck = Enumerable.Repeat(placeholder, 6).ToList(),
            FillDecks = false, Shuffle = false, History = true, Logging = false, SkipMulligan = true, RandomSeed = seed,
            FormatType = FormatType.FT_WILD, // (as our server plays every game: SabberEngineGame — the random pools are Wild's)
        });
        g.StartGame();
        var r = new Rebuilt { G = g, SourceView = sourceView };
        Controller Ours(int pid) => pid == 1 ? g.Player1 : g.Player2;
        r.Actor = Ours(actorId);
        if (g.CurrentPlayer != r.Actor) throw new NotRebuilt("our game didn't start on the acting player");

        // empty the start's hands, decks and graveyards
        foreach (Controller c in new[] { g.Player1, g.Player2 })
        {
            foreach (IPlayable p in c.HandZone.ToList()) c.HandZone.Remove(p);
            foreach (IPlayable p in c.DeckZone.ToList()) c.DeckZone.Remove(p);
            foreach (IPlayable p in c.GraveyardZone.ToList()) c.GraveyardZone.Remove(p);
            foreach (Minion m in c.BoardZone.ToList()) c.BoardZone.Remove(m);
            c.DiedThisGame.Clear();
        }
        void Map(int liveId, IEntity ours)
        {
            r.LiveToOurs[liveId] = ours.Id;
            r.OursToLive[ours.Id] = liveId;
        }
        Map(a.GameEntity, g);
        foreach (int pid in new[] { 1, 2 })
        {
            Map(players[pid].Id, Ours(pid));
            Map(heroEntity[pid], Ours(pid).Hero);
            if (powerEntity[pid] != 0 && Ours(pid).Hero.HeroPower != null) Map(powerEntity[pid], Ours(pid).Hero.HeroPower);
        }
        if (live.TryGetValue(a.GameEntity, out var gameLive)) g.Turn = gameLive[GameTag.TURN];
        // the players' counters (mana, Overload, cards played this turn, Corpses, ...)
        foreach (int pid in new[] { 1, 2 }) CopyTags(players[pid], Ours(pid), enforce: false);
        // (the engine's own count of a Hero Power's turns — "Unlocks on Turn 3": the player's turns so far)
        foreach (int pid in new[] { 1, 2 })
            if (Ours(pid).Hero.HeroPower is HeroPower ownPower) ownPower[HsTag.OWN_TURNS_STARTED] = players[pid][GameTag.TURN];

        // the cards: weapons first (equipping raises an event: nothing else is there yet), then hands, decks, graveyards, boards, secrets
        int Rank(LiveEntity e) => e[GameTag.ZONE] switch
        {
            (int)Zone.PLAY when e[GameTag.CARDTYPE] == (int)CardType.WEAPON => 0,
            (int)Zone.HAND => 1, (int)Zone.DECK => 2, (int)Zone.GRAVEYARD => 3, (int)Zone.PLAY => 4, (int)Zone.SECRET => 5, _ => 9,
        };
        var cards = live.Values.Where(e => e.Id != a.GameEntity && !players.Values.Contains(e) && e.Id != heroEntity[1] && e.Id != heroEntity[2] &&
                                           e.Id != powerEntity[1] && e.Id != powerEntity[2] && Rank(e) < 9 &&
                                           e[GameTag.CARDTYPE] != (int)CardType.ENCHANTMENT && PlayerOf(e) is 1 or 2)
            .OrderBy(Rank).ThenBy(e => e[GameTag.ZONE_POSITION]).ThenBy(e => e.Id).ToList();
        // which card each one is (a hidden hand / deck card the viewer never saw: a placeholder); what stays out
        var made = new List<(LiveEntity E, Card Card, bool Placeholder)>();
        foreach (LiveEntity e in cards)
        {
            int zone = e[GameTag.ZONE];
            string id = a.CardOf(e.Id);
            Card card = CardFor(id);
            bool placeholderCard = false;
            if (card?.Type == CardType.ENCHANTMENT) continue; // (a hidden one: placed with the enchantments)
            if (card == null || card.Type is CardType.INVALID)
            {
                if (zone is (int)Zone.HAND or (int)Zone.DECK) { card = placeholder; placeholderCard = true; }
                else if (zone == (int)Zone.GRAVEYARD) continue;
                else if (zone == (int)Zone.SECRET) { r.Notes.Add("a hidden secret left out"); continue; }
                else if (!e.Visible && id.Length == 0) { r.Notes.Add("a hidden entity in play left out"); continue; }
                else throw new NotRebuilt($"unknown card in play: '{id}' ({ZoneName(zone)})");
            }
            if (zone == (int)Zone.PLAY && card.Type is not (CardType.MINION or CardType.LOCATION or CardType.WEAPON))
            {
                r.Notes.Add($"{card.Id} ({card.Type}) in play left out");
                continue;
            }
            if (zone == (int)Zone.SECRET && card.Type != CardType.SPELL) { r.Notes.Add($"{card.Id} in the secret zone left out"); continue; }
            made.Add((e, card, placeholderCard));
        }
        // ... and the cards the game's history names that are nowhere now (played earlier, then gone): made, not placed
        var inHistory = a.History.Take(a.HistoryCount).Where(h => h.Kind == HistoryKind.Played).Select(h => h.Entity).ToHashSet();
        foreach (int id in inHistory)
            if (!made.Any(m => m.E.Id == id) && live.TryGetValue(id, out LiveEntity gone) && PlayerOf(gone) is 1 or 2 && CardFor(a.CardOf(id)) is Card goneCard &&
                goneCard.Type is not (CardType.ENCHANTMENT or CardType.INVALID or CardType.HERO_POWER) && gone[GameTag.ZONE] is not ((int)Zone.HAND or (int)Zone.DECK or (int)Zone.PLAY or (int)Zone.SECRET))
                made.Add((gone, goneCard, false));

        // created in two steps, so "didn't start in your deck" (an id from Game.FirstGeneratedEntityId on) reads as live: first what
        // started in a deck (live ids below its first generated one), then the rest
        var entityOf = new Dictionary<int, IPlayable>();
        foreach (var (e, card, ph) in made.Where(m => m.E.Id < a.FirstGenerated).OrderBy(m => m.E.Id))
            entityOf[e.Id] = SabberStoneCore.Model.Entities.Entity.FromCard(Ours(PlayerOf(e)), card);
        SetFirstGenerated(g, g.IdEntityDic.Max(kv => kv.Key) + 1);
        StartingDecks(a, g, live, placeholder);
        foreach (var (e, card, ph) in made.Where(m => m.E.Id >= a.FirstGenerated).OrderBy(m => m.E.Id))
            entityOf[e.Id] = SabberStoneCore.Model.Entities.Entity.FromCard(Ours(PlayerOf(e)), card);
        // (2026-11-16) an Assembled minion: live's half tags name the card whose half it took (Generic.AssembledCard)
        foreach (var (e, card, ph) in made)
            if (e[SabberStoneCore.Actions.Generic.ASSEMBLED_MINION] > 0 && entityOf.TryGetValue(e.Id, out IPlayable asm))
            {
                Card first = Cards.FromAssetId(e[SabberStoneCore.Actions.Generic.CRAFTABLE_FIRST_HALF_DBID]);
                Card second = Cards.FromAssetId(e[SabberStoneCore.Actions.Generic.CRAFTABLE_SECOND_HALF_DBID]);
                if (first != null || second != null)
                    asm.Card = SabberStoneCore.Actions.Generic.AssembledCard(card, first ?? card, second ?? card);
            }

        var deck = new Dictionary<int, List<(LiveEntity, IPlayable)>> { [1] = new(), [2] = new() };
        // (a graveyard in the order things went there — the last played / died last: "the last minions that died this turn" read it
        // from the end)
        var wentAt = new Dictionary<int, int>();
        for (int i = 0; i < a.HistoryCount; i++)
            if (a.History[i].Kind is HistoryKind.Played or HistoryKind.Died) wentAt[a.History[i].Entity] = i;
        int GraveOrder(LiveEntity e) => e[GameTag.ZONE] == (int)Zone.GRAVEYARD ? wentAt.GetValueOrDefault(e.Id, -1) : 0;
        foreach (var (e, card, placeholderCard) in made.OrderBy(m => Rank(m.E)).ThenBy(m => m.E[GameTag.ZONE_POSITION]).ThenBy(m => GraveOrder(m.E)).ThenBy(m => m.E.Id))
        {
            int zone = e[GameTag.ZONE];
            Controller c = Ours(PlayerOf(e));
            IPlayable p = entityOf[e.Id];
            Map(e.Id, p);
            if (placeholderCard) r.Placeholders.Add(p.Id);
            switch (zone)
            {
                case (int)Zone.HAND:
                    if (c.HandZone.IsFull) throw new NotRebuilt("hand over its limit");
                    c.HandZone.Add(p);
                    // (2026-11-16) an Echo copy (GHOSTLY): the engine discards it at the end of the turn (Game.GhostlyCards)
                    if (e[GameTag.GHOSTLY] > 0) { p[GameTag.GHOSTLY] = 1; g.GhostlyCards.Add(p.Id); }
                    break;
                case (int)Zone.DECK:
                    deck[PlayerOf(e)].Add((e, p));
                    break;
                case (int)Zone.GRAVEYARD:
                    c.GraveyardZone.Add(p);
                    break;
                case (int)Zone.PLAY when p is Weapon w:
                    // (its aura and trigger on, as PlayCard.PlayWeapon does before equipping)
                    w.Card.Power?.Aura?.Activate(w);
                    w.Card.Power?.Trigger?.Activate(w);
                    c.Hero.AddWeapon(w);
                    break;
                case (int)Zone.PLAY when p is Minion m:
                    if (c.BoardZone.IsFull) throw new NotRebuilt("board over its limit");
                    bool sleepsByCard = m.Card.DormantTurns > 0 || m.Card.StartsDormant;
                    if (e[GameTag.DORMANT] == 0 || !sleepsByCard) m[HsTag.AWAKENED_COPY] = 1; // (awake: no Dormant start)
                    c.BoardZone.Add(m);
                    // (put to sleep by an effect: Dormant until its condition)
                    if (e[GameTag.DORMANT] > 0 && !sleepsByCard) SabberStoneCore.Actions.Generic.MakeDormant(m);
                    break;
                // (two Quests up only as the halves of one split card: Reach Equilibrium, Generic.SplitQuest)
                case (int)Zone.SECRET when p is Spell q && q.Card.IsQuest && c.SecretZone.Quest != null &&
                                           !c.SecretZone.Quests.All(up => SabberStoneCore.Model.Zones.SecretZone.HalvesOfOneQuest(up.Card, q.Card)):
                    throw new NotRebuilt($"two Quests up at once ({c.SecretZone.Quest.Card.Id}, {q.Card.Id}): ours holds one (engine-rules.md One Quest)");
                case (int)Zone.SECRET when p is Spell s:
                    s.Power?.Trigger?.Activate(s);
                    if (s.Card.IsDurationSpell) s.Power?.Aura?.Activate(s);
                    c.SecretZone.Add(s);
                    break;
                default:
                    p[GameTag.ZONE] = zone; // (gone: only remembered by the history)
                    break;
            }
        }
        // the decks: what live took from the deck during the action on top, in that order (DeckZone's top is its last card)
        // (or, the bottom variant: at the bottom, in the order they left — an effect taking from the bottom)
        foreach (int pid in new[] { 1, 2 })
        {
            if (leftDeckAtBottom)
                foreach (int leftId in a.LeftDeck)
                    if (deck[pid].FirstOrDefault(x => x.Item1.Id == leftId) is { Item2: { } bp }) Ours(pid).DeckZone.Add(bp);
            foreach (var (_, p) in deck[pid].Where(x => !a.LeftDeck.Contains(x.Item1.Id)))
                Ours(pid).DeckZone.Add(p);
            if (!leftDeckAtBottom)
                foreach (int leftId in Enumerable.Reverse(a.LeftDeck))
                    if (deck[pid].FirstOrDefault(x => x.Item1.Id == leftId) is { Item2: { } p }) Ours(pid).DeckZone.Add(p);
        }

        // what the engine remembers of the game so far: cards played (and this turn's), minions died / summoned, cards added to hand
        int turnNow = g.Turn;
        foreach (LiveHistory h in a.History.Take(a.HistoryCount))
        {
            if (h.Player is not (1 or 2)) continue;
            Controller c = Ours(h.Player);
            r.LiveToOurs.TryGetValue(h.Entity, out int ourId);
            IPlayable p = ourId != 0 && g.IdEntityDic.TryGetValue(ourId, out IPlayable found) ? found : null;
            Card card = p?.Card ?? CardFor(a.CardOf(h.Entity));
            switch (h.Kind)
            {
                case HistoryKind.Played when p != null:
                    IPlayable target = r.LiveToOurs.TryGetValue(h.Target, out int tid) && g.IdEntityDic.TryGetValue(tid, out IPlayable tp) ? tp : null;
                    g.Turn = h.Turn;
                    object entry = new PlayHistoryEntry(p, target);
                    SetField(entry, "Cost", h.Cost);
                    SetField(entry, "SourceController", h.Player);
                    // (2026-11-16) the card as it was played, not as it is now (a Hunting Mastiff Hexed later is not a "played Hexfrog":
                    // Joymancer Jepetto, "minions you've played this game")
                    if (CardFor(h.Card) is Card playedCard) entry.GetType().GetField("SourceCard").SetValue(entry, playedCard);
                    c.PlayHistory.Add((PlayHistoryEntry)entry);
                    if (h.Turn == turnNow) c.CardsPlayedThisTurn.Add(p.Card);
                    break;
                case HistoryKind.Died when p != null:
                    c.DiedThisGame.Add(p.Id);
                    break;
                case HistoryKind.Summoned when card != null:
                    c.SummonedThisGame.Add(card);
                    break;
                case HistoryKind.ToHand when card != null && !r.Placeholders.Contains(ourId):
                    c.AddedToHandThisGame.Add(card);
                    break;
            }
        }
        g.Turn = turnNow;
        FillHandCounters(a, r, live);
        FillHeroDamage(a, r);
        // (2026-11-16) "If you've Forged a card this game" (Ignis): each player's forged cards — live tags them FORGED (3011), in any zone
        foreach (int pid in new[] { 1, 2 })
            Ours(pid)[HsTag.FORGES_THIS_GAME] = live.Values.Count(e => e[(GameTag)3011] > 0 && e[GameTag.CONTROLLER] == pid);
        // (2026-11-20) Agamaggan's pending "next card costs your opponent's Health (up to 10)": live marks it AGAMAGGAN_CURSE on the
        // opponent's hero; ours keeps the cap on the player (HsTag.NEXT_CARD_COSTS_OPPONENT_HEALTH)
        foreach (int pid in new[] { 1, 2 })
            if (Ours(pid == 1 ? 2 : 1).Hero is Hero cursed && live.Values.Any(e => e[SabberStoneCore.Actions.Generic.AGAMAGGAN_CURSE] > 0 && e[GameTag.CONTROLLER] != pid &&
                                                                                    e[GameTag.CARDTYPE] == (int)CardType.HERO && e[GameTag.ZONE] == (int)Zone.PLAY))
            {
                Ours(pid)[HsTag.NEXT_CARD_COSTS_OPPONENT_HEALTH] = SabberStoneCore.Loader.CardValues.Get("EDR_489", "up to", 10);
                cursed[SabberStoneCore.Actions.Generic.AGAMAGGAN_CURSE] = 1;
            }
        // (2026-11-20) Husk's hero Deathrattle "Spend up to N Corpses to resurrect": live points the hero's PLAYER_DEATHRATTLE at the
        // TIME_618e enchantment (session-20261106-001808 167771); ours keeps the cap on the player (HsTag.HERO_RESURRECT_CORPSES)
        foreach (LiveEntity h in live.Values.Where(e => e[GameTag.CARDTYPE] == (int)CardType.HERO && e[GameTag.ZONE] == (int)Zone.PLAY && e[(GameTag)4398] > 0)) // (client PLAYER_DEATHRATTLE)
            if (a.CardOf(h[(GameTag)4398]).StartsWith("TIME_618", StringComparison.Ordinal) && PlayerOf(h) is 1 or 2)
                Ours(PlayerOf(h))[HsTag.HERO_RESURRECT_CORPSES] = SabberStoneCore.Loader.CardValues.Get("TIME_618", "corpses", 20);
        FillElementals(a, r, players);
        // (a minion that died stays marked in the graveyard — the engine's "died this game / this turn": Resurrect's requirement, "a
        // friendly X died this turn" walk the graveyard for it; a discarded one isn't)
        foreach (LiveHistory h in a.History.Take(a.HistoryCount))
            if (h.Kind == HistoryKind.Died && r.LiveToOurs.TryGetValue(h.Entity, out int deadId) &&
                g.IdEntityDic.TryGetValue(deadId, out IPlayable dead) && dead.Zone?.Type == Zone.GRAVEYARD)
                ToBeDestroyedField?.SetValue(dead, true);

        // silenced minions lose their text (before the tags and enchantments live still shows)
        foreach (LiveEntity e in cards)
            if (e[GameTag.SILENCED] > 0 && r.LiveToOurs.TryGetValue(e.Id, out int oid) && g.IdEntityDic[oid] is Minion sm && sm.Zone?.Type == Zone.PLAY)
                sm.Silence();

        // tags (keywords, counters, exhausted ...)
        foreach (var (liveId, ourId) in r.LiveToOurs.ToList())
        {
            if (liveId == a.GameEntity || players.Values.Any(p => p.Id == liveId)) continue;
            if (!live.TryGetValue(liveId, out LiveEntity e) || !g.IdEntityDic.TryGetValue(ourId, out IPlayable p)) continue;
            CopyTags(e, p, enforce: e.Visible && !r.Placeholders.Contains(ourId));
        }

        // enchantments live shows on them (not those of auras: our auras make their own)
        // (one on a hidden card — an opponent's hand card made cheaper — is hidden too until the action shows it: taken as the action
        // showed it, in the zone it had before)
        IEnumerable<LiveEntity> Enchantments()
        {
            foreach (LiveEntity x in live.Values.OrderBy(x => x.Id))
            {
                if (x[GameTag.CARDTYPE] == (int)CardType.ENCHANTMENT) yield return x;
                else if (!x.Visible && x[GameTag.ZONE] is (int)Zone.PLAY or (int)Zone.SETASIDE && a.After.TryGetValue(x.Id, out LiveEntity shown) &&
                         shown[GameTag.CARDTYPE] == (int)CardType.ENCHANTMENT)
                {
                    LiveEntity view = shown.Copy();
                    view.Tags[(int)GameTag.ZONE] = x[GameTag.ZONE];
                    yield return view;
                }
            }
        }
        foreach (LiveEntity en in Enchantments())
        {
            if (!r.LiveToOurs.TryGetValue(en[GameTag.ATTACHED], out int targetId)) continue;
            if (en[GameTag.ZONE] is not ((int)Zone.PLAY or (int)Zone.SETASIDE)) continue;
            Card card = CardFor(a.CardOf(en.Id));
            if (card == null || card.Type != CardType.ENCHANTMENT) { r.Notes.Add($"enchantment {a.CardOf(en.Id)} unknown here"); continue; }
            IEntity target = targetId == g.Id ? g : (IEntity)(g.IdEntityDic.TryGetValue(targetId, out IPlayable tp) ? tp : null) ??
                             (targetId == g.Player1.Id ? g.Player1 : targetId == g.Player2.Id ? g.Player2 : null);
            if (target == null) continue;
            int creatorLive = en[GameTag.CREATOR];
            IPlayable creator = r.LiveToOurs.TryGetValue(creatorLive, out int cid) && g.IdEntityDic.TryGetValue(cid, out IPlayable cp) ? cp : null;
            if (live.TryGetValue(creatorLive, out LiveEntity creatorEntity) && creatorEntity[GameTag.ZONE] is (int)Zone.PLAY or (int)Zone.HAND or (int)Zone.SECRET &&
                CardFor(a.CardOf(creatorLive))?.Power?.Aura != null)
            {
                r.AuraMade.Add(en.Id);
                continue; // an aura's: ours has it from the aura
            }
            creator ??= target as IPlayable ?? Ours(PlayerOf(en) is 1 or 2 ? PlayerOf(en) : actorId).Hero;
            // (a card captured in it — Unearthed Raptor's copied Deathrattle, a Trapped Soul: live's TAG_SCRIPT_DATA_NUM_1 is the captured
            // entity, not a number)
            Card captured = CapturesACard(card) && en[GameTag.TAG_SCRIPT_DATA_NUM_1] > 0 ? CardFor(a.CardOf(en[GameTag.TAG_SCRIPT_DATA_NUM_1])) : null;
            try
            {
                int mark = g.IdEntityDic.Max(kv => kv.Key);
                // (script data naming another entity the same card made — Exarch Hataaru's GDB_136e names the spell it Discovered: our
                // entity's id, not live's)
                int ScriptData(GameTag tag)
                {
                    int v = en[tag];
                    if (v <= 3) return v > 0 ? v : -1;
                    if (en[GameTag.CREATOR] != 0 && v != en.Id && live.TryGetValue(v, out LiveEntity named) && named[GameTag.CREATOR] == en[GameTag.CREATOR])
                    {
                        if (r.LiveToOurs.TryGetValue(v, out int ourNamed)) return ourNamed;
                        // (one set aside — Ascension's snapshot of the minion it was, CATA_567e — made there for it)
                        if (named[GameTag.ZONE] == (int)Zone.SETASIDE && CardFor(a.CardOf(v)) is Card namedCard && named[GameTag.CONTROLLER] is 1 or 2)
                        {
                            Controller owner = Ours(named[GameTag.CONTROLLER]);
                            IPlayable aside = SabberStoneCore.Model.Entities.Entity.FromCard(owner, namedCard, null, owner.SetasideZone);
                            Map(v, aside);
                            return aside.Id;
                        }
                    }
                    return v;
                }
                // (2026-11-16) its controller is live's (Wild Magic: each player's DALA_856e is that player's, though one card made both)
                SabberStoneCore.Actions.Generic.AddEnchantmentBlock(g, card, creator, target,
                    captured == null ? ScriptData(GameTag.TAG_SCRIPT_DATA_NUM_1) : -1, ScriptData(GameTag.TAG_SCRIPT_DATA_NUM_2),
                    owner: en[GameTag.CONTROLLER] is 1 or 2 ? Ours(en[GameTag.CONTROLLER]) : null);
                // (the new enchantment is live's: its triggers act as live's did)
                if (target.AppliedEnchantments?.LastOrDefault(x => x.Card.Id == card.Id && x.Id > mark) is { } added)
                {
                    Map(en.Id, added);
                    if (captured != null) added.CapturedCard = captured;
                    else if (CapturesACard(card)) r.Notes.Add($"enchantment {card.Id}: its captured card unknown");
                }
                else r.Notes.Add($"enchantment {card.Id} not attached here");
            }
            catch (Exception ex) { r.Notes.Add($"enchantment {card.Id}: {ex.GetType().Name}"); }
        }
        LiveOrderOfPlay(a, r);
        g.AuraUpdate();
        // (again: an enchantment re-added can bring back a keyword live no longer shows: a Divine Shield already lost)
        foreach (var (liveId, ourId) in r.LiveToOurs.ToList())
        {
            if (liveId == a.GameEntity || players.Values.Any(p => p.Id == liveId)) continue;
            if (!live.TryGetValue(liveId, out LiveEntity e) || !g.IdEntityDic.TryGetValue(ourId, out IPlayable p) || p is SabberStoneCore.Model.Entities.Enchantment) continue;
            CopyTags(e, p, enforce: e.Visible && !r.Placeholders.Contains(ourId));
        }

        // which numbers don't come out as live's by themselves (a modifier the rebuilt game doesn't have: forced, and noted)
        foreach (var (liveId, ourId) in r.LiveToOurs)
        {
            if (!live.TryGetValue(liveId, out LiveEntity e) || !e.Visible || r.Placeholders.Contains(ourId)) continue;
            if (!g.IdEntityDic.TryGetValue(ourId, out IPlayable p) || p is SabberStoneCore.Model.Entities.Enchantment) continue;
            ESnap o = OurSnap(p), l = LiveSnap(a, e, false);
            if (o.Zone != l.Zone) continue;
            if (l.Zone is (int)Zone.PLAY or (int)Zone.HAND && (p is Character || p is Weapon))
            {
                if (o.Atk != l.Atk && e.Tags.ContainsKey((int)GameTag.ATK)) r.Fitted.Add((liveId, "ATK"));
                // (a hero's Health is the game's setting — a Heist boss's 50, a hero card's own — not a modifier the rebuild missed)
                if (p is Weapon ? o.Dur != l.Dur : o.Health != l.Health && e.Tags.ContainsKey((int)GameTag.HEALTH) && p is not Hero)
                {
                    r.Fitted.Add((liveId, p is Weapon ? "DURABILITY" : "HEALTH"));
                    r.Fitted.Add((liveId, "DAMAGE"));
                }
            }
            if (o.Cost != l.Cost && (l.Zone == (int)Zone.HAND || p is HeroPower)) r.Fitted.Add((liveId, "COST"));
        }

        // the numbers live shows: Attack, Health, Damage, Durability, Cost (whatever buffs and auras give). Weapons first (a hero's Attack
        // counts its weapon's), Damage before Attack (Enrage: "+3 Attack while damaged"); each fitted step by step (an aura can take
        // Attack below 0 and the shown Attack stops at 0); a permanent without Health keeps its own.
        var fitting = r.LiveToOurs.Select(kv => (E: live.GetValueOrDefault(kv.Key), P: g.IdEntityDic.TryGetValue(kv.Value, out IPlayable fp) ? fp : null,
                                                 Visible: !r.Placeholders.Contains(kv.Value)))
            .Where(x => x.E != null && x.P != null && x.E.Visible && x.Visible).ToList();
        // (2026-11-20) a Spellburst live shows spent — the keyword on the card, SPELLBURST 0 on the entity — is gone: its one-shot
        // trigger off (Spellburst triggers once, Trigger.RemoveAfterTriggered); else Shooting Star's 3 Asteroids came again
        foreach (var (e, p, _) in fitting)
            if (p.Zone?.Type == Zone.PLAY && p.Card[HsTag.SPELLBURST] > 0 && e[HsTag.SPELLBURST] == 0 &&
                p.ActivatedTrigger is { RemoveAfterTriggered: true, Type: TriggerType.AFTER_CAST } burst)
            {
                burst.Remove();
                p[HsTag.SPELLBURST] = 0;
            }
        // (2026-11-20) a whole Shatter card already in hand has split and recombined, or a full hand kept it whole (it splits as it
        // enters the hand; wiki Shatter): it doesn't split again. Hidden ones too (a recombined card the viewer never saw).
        foreach (int ours in r.LiveToOurs.Values)
            if (g.IdEntityDic.TryGetValue(ours, out IPlayable sp) && sp.Zone?.Type == Zone.HAND && sp[SabberStoneCore.Actions.Generic.SHATTER] > 0)
                sp[SabberStoneCore.Actions.Generic.SHATTER] = 0;
        foreach (var (e, p, _) in fitting)
            if (p is Weapon w)
            {
                if (e.Tags.ContainsKey((int)GameTag.ATK)) Fit(() => w[GameTag.ATK], v => w[GameTag.ATK] = v, e[GameTag.ATK]);
                Fit(() => w[GameTag.DURABILITY], v => w[GameTag.DURABILITY] = v, LiveDurability(e));
                w[GameTag.DAMAGE] = e[GameTag.DAMAGE];
            }
        bool Fits(LiveEntity e, IPlayable p) => p is Character && (p is Hero || p.Zone?.Type == Zone.PLAY || p.Zone?.Type == Zone.HAND && e[GameTag.HEALTH] > 0);
        foreach (var (e, p, _) in fitting)
            if (Fits(e, p) && p is Character ch)
            {
                bool inHand = p.Zone?.Type == Zone.HAND;
                if (!inHand) ch.Damage = 0;
                if (e[GameTag.HEALTH] > 0) Fit(() => ch.BaseHealth, v => ch.BaseHealth = v, e[GameTag.HEALTH]);
                if (!inHand) ch.Damage = e[GameTag.DAMAGE];
            }
        g.AuraUpdate(); // (Enrage-like effects switch on with the damage)
        for (int round = 0; round < 3; round++)
        {
            bool off = false;
            foreach (var (e, p, _) in fitting)
                if (Fits(e, p) && p is Character ch && e.Tags.ContainsKey((int)GameTag.ATK) && ch.AttackDamage != e[GameTag.ATK])
                {
                    Fit(() => ch.AttackDamage, v => ch.AttackDamage = v, e[GameTag.ATK]);
                    off = true;
                }
            if (!off) break;
            g.AuraUpdate();
        }
        // (2026-11-16) fitting sets Damage before Health: on the way a minion can read as lethal and get marked to die (the attack then
        // ends in preparation — ours "nothing happened"). A character alive on live, above 0 Health in ours, isn't dying.
        foreach (var (e, p, _) in fitting)
            if (p is Character ch && ch.Zone?.Type == Zone.PLAY && ch.Health > 0 && e[GameTag.TO_BE_DESTROYED] == 0 && ch.ToBeDestroyed)
            {
                ToBeDestroyedField?.SetValue(ch, false);
                MortallyWoundedField?.SetValue(ch, false);
                if (ch is Minion dm) g.DeadMinions.Remove(dm);
            }
        // (a live permanent — UNTOUCHABLE, or no Health at all: a Tombs of Terror tomb — that our card makes a plain minion: it would die)
        foreach (var (e, p, _) in fitting)
            if (p is Minion m && !(p is Location) && p.Zone?.Type == Zone.PLAY)
            {
                if (!m.Untouchable && e[GameTag.UNTOUCHABLE] > 0)
                    throw new NotRebuilt($"{m.Card.Id} is an untouchable permanent live, a plain minion in our card data");
                if (e[GameTag.HEALTH] <= 0 && m.Untouchable && e[GameTag.UNTOUCHABLE] > 0)
                {
                    // (untouchable in ours too — Eternal Tomb ULDA_804t: every board walk skips it, nothing can hit it; only the death
                    // check would take it for 0 Health: given 1, and its stats, which live hides, left out of the comparison)
                    if (m.Health <= 0)
                    {
                        m.BaseHealth = 1 + m.Damage;
                        r.Notes.Add($"{m.Card.Id}: untouchable with no Health, kept with 1");
                    }
                    r.NoHealth.Add(m.Id);
                }
                else if (e[GameTag.HEALTH] <= 0 && m.Health <= 0)
                    throw new NotRebuilt($"{m.Card.Id} is a permanent with no Health live; ours has 0 Health and dies at the first death check (an engine difference to check)");
            }
        // (a Cost the rebuilt auras / enchantments already give is left alone: forcing the base would keep a discount that ends
        // during the action — Mug's Magic's "first minion each turn" — after it ended)
        foreach (var (e, p, _) in fitting)
            if (p.Zone?.Type is Zone.HAND || p is HeroPower)
            {
                Fit(() => FreshCost(p), v => { p.Cost = v; QueueCostUpdate(p); }, e[GameTag.COST]);
            }
        g.AuraUpdate();
        r.MaxIdBefore = g.IdEntityDic.Max(kv => kv.Key);
        return r;
    }

    /// <summary>
    /// Order of play as live's (rulebook: triggers of the same event resolve in order of play): the rebuild puts the board in
    /// position order and re-adds enchantments by id, so their order-of-play numbers are given again in the order live had them come
    /// into play (minions, weapons, secrets, locations, enchantments: when they last entered the play / secret zone), the
    /// same numbers shuffled, so what's new in the action still comes after.
    /// </summary>
    private static void LiveOrderOfPlay(LiveAction a, Rebuilt r)
    {
        var placed = new List<(IEntity E, long Key)>();
        foreach (var (liveId, ourId) in r.LiveToOurs)
        {
            if (!r.G.IdEntityDic.TryGetValue(ourId, out IPlayable p)) continue;
            bool enchantment = p is SabberStoneCore.Model.Entities.Enchantment;
            if (!enchantment && (p is Hero || p is HeroPower || p.Zone?.Type is not (Zone.PLAY or Zone.SECRET))) continue;
            // (in play since before the recording started: by id, before everything that came later)
            long key = a.EnteredPlay.TryGetValue(liveId, out int at) ? 1_000_000L + at : liveId;
            placed.Add((p, key));
        }
        if (placed.Count < 2) return;
        static int Get(IEntity e) => e is SabberStoneCore.Model.Entities.Enchantment en ? en.OrderOfPlay : ((SabberStoneCore.Model.Entities.Entity)e).OrderOfPlay;
        int[] numbers = placed.Select(x => Get(x.E)).OrderBy(n => n).ToArray();
        int i = 0;
        foreach (var (e, _) in placed.OrderBy(x => x.Key))
        {
            if (e is SabberStoneCore.Model.Entities.Enchantment en) en.OrderOfPlay = numbers[i++];
            else ((SabberStoneCore.Model.Entities.Entity)e).OrderOfPlay = numbers[i++];
        }
    }

    /// <summary>The Elementals each player played this turn and on their last turn (the engine's NUM_ELEMENTAL_PLAYED_THIS_TURN /
    /// _LAST_TURN: "If you played an Elemental last turn"), from live's plays, when live's player entity doesn't show them.</summary>
    private static void FillElementals(LiveAction a, Rebuilt r, Dictionary<int, LiveEntity> players)
    {
        int turn = r.G.Turn;
        foreach (int pid in new[] { 1, 2 })
        {
            Controller c = pid == 1 ? r.G.Player1 : r.G.Player2;
            int lastOwnTurn = c == r.Actor ? turn - 2 : turn - 1;
            int Count(int t) => a.History.Take(a.HistoryCount).Count(h => h.Kind == HistoryKind.Played && h.Player == pid && h.Turn == t &&
                                                                         CardFor(h.Card.Length > 0 ? h.Card : a.CardOf(h.Entity))?.IsRace(Race.ELEMENTAL) == true);
            if (!players[pid].Tags.ContainsKey((int)GameTag.NUM_ELEMENTAL_PLAYED_THIS_TURN)) c.NumElementalsPlayedThisTurn = c == r.Actor ? Count(turn) : 0;
            if (!players[pid].Tags.ContainsKey((int)GameTag.NUM_ELEMENTAL_PLAYED_LAST_TURN)) c.NumElementalsPlayedLastTurn = Count(lastOwnTurn);
        }
    }

    /// <summary>
    /// The damage each hero took "this turn" as the engine counts it (Hero.DamageTakenThisTurn, zeroed when its own turn ends: Rigged
    /// Faire Game reads it at the enemy turn's end; HsTag.HERO_DAMAGE_THIS_TURN, zeroed at every turn change), from live's history.
    /// </summary>
    private static void FillHeroDamage(LiveAction a, Rebuilt r)
    {
        int lastTurnEnd = -1;
        var lastOwnEnd = new Dictionary<int, int> { [1] = -1, [2] = -1 };
        for (int i = 0; i < a.HistoryCount; i++)
            if (a.History[i].Kind == HistoryKind.TurnEnded)
            {
                lastTurnEnd = i;
                lastOwnEnd[a.History[i].Player] = i;
            }
        foreach (int pid in new[] { 1, 2 })
        {
            Controller c = pid == 1 ? r.G.Player1 : r.G.Player2;
            int sinceOwnEnd = 0, sinceAnyEnd = 0;
            for (int i = 0; i < a.HistoryCount; i++)
            {
                LiveHistory h = a.History[i];
                if (h.Kind != HistoryKind.HeroDamaged || h.Player != pid) continue;
                if (i > lastOwnEnd[pid]) sinceOwnEnd += h.Cost;
                if (i > lastTurnEnd) sinceAnyEnd += h.Cost;
            }
            c.Hero.DamageTakenThisTurn = sinceOwnEnd;
            c[HsTag.HERO_DAMAGE_THIS_TURN] = sinceAnyEnd;
        }
    }

    /// <summary>
    /// The engine's own counters on hand cards, from live's history (LiveCheck.Counters): the turn each card entered the hand
    /// (HsTag.ADDED_TO_HAND_TURN, Quickdraw; the starting hand 0), the plays before it (HsTag.HELD_SINCE_PLAY) and a "... while holding
    /// this" count (TAG_SCRIPT_DATA_NUM_1 of a card whose trigger counts in hand) = live's triggers of the card in hand since then,
    /// when live doesn't show the count itself.
    /// </summary>
    private static void FillHandCounters(LiveAction a, Rebuilt r, Dictionary<int, LiveEntity> live)
    {
        var lastToHand = new Dictionary<int, int>();
        for (int i = 0; i < a.HistoryCount; i++)
            if (a.History[i].Kind == HistoryKind.ToHand) lastToHand[a.History[i].Entity] = i;
        foreach (var (liveId, ourId) in r.LiveToOurs)
        {
            if (!r.G.IdEntityDic.TryGetValue(ourId, out IPlayable p) || p.Zone?.Type != Zone.HAND) continue;
            if (!lastToHand.TryGetValue(liveId, out int at)) continue; // (in hand since before the recording: left at 0)
            LiveHistory entered = a.History[at];
            p[HsTag.ADDED_TO_HAND_TURN] = entered.Turn;
            p[HsTag.HELD_SINCE_PLAY] = a.History.Take(at).Count(h => h.Kind == HistoryKind.Played && h.Player == entered.Player);
            if (p.Card.Power?.Trigger?.CountsWhileHeld == true && !(live.GetValueOrDefault(liveId)?.Tags.ContainsKey((int)GameTag.TAG_SCRIPT_DATA_NUM_1) ?? false))
            {
                int counted = 0;
                for (int i = at + 1; i < a.HistoryCount; i++)
                    if (a.History[i].Kind == HistoryKind.HandTrigger && a.History[i].Entity == liveId) counted++;
                if (counted > 0) p[GameTag.TAG_SCRIPT_DATA_NUM_1] = counted;
            }
        }
    }

    private static readonly System.Reflection.FieldInfo ToBeDestroyedField =
        typeof(SabberStoneCore.Model.Entities.Entity).GetField("_toBeDestroyed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    private static readonly System.Reflection.FieldInfo MortallyWoundedField =
        typeof(Playable).GetField("MortallyWounded", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    private static readonly System.Reflection.FieldInfo CostManagerField =
        typeof(Playable).GetField("_costManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    /// <summary>A playable's cost manager keeps its last result until a modifier changes: setting the base cost alone doesn't
    /// recompute it (the shown Cost would stay the old one).</summary>
    private static void QueueCostUpdate(IPlayable p)
    {
        object manager = p is Playable pl ? CostManagerField?.GetValue(pl) : null;
        manager?.GetType().GetMethod("QueueUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(manager, null);
    }

    /// <summary>The Cost shown now (recomputed, not the cost manager's last result).</summary>
    private static int FreshCost(IPlayable p)
    {
        QueueCostUpdate(p);
        return p.Cost;
    }

    private static readonly Dictionary<string, bool> capturing = new();

    /// <summary>An enchantment that holds a card (its script reads Enchantment.CapturedCard: GetCapturedCardTask,
    /// ActivateCapturedDeathrattleTask): found by walking its Power's tasks.</summary>
    private static bool CapturesACard(Card enchantment)
    {
        lock (capturing)
        {
            if (capturing.TryGetValue(enchantment.Id, out bool known)) return known;
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            bool Walk(object o, int depth)
            {
                if (o == null || depth > 12 || o is string || o.GetType().IsPrimitive || o.GetType().IsEnum || !seen.Add(o)) return false;
                if (o.GetType().Name.Contains("Captured", StringComparison.Ordinal)) return true;
                if (o is System.Collections.IEnumerable list)
                {
                    foreach (object x in list)
                        if (Walk(x, depth + 1)) return true;
                    return false;
                }
                if (o is not (SabberStoneCore.Tasks.ISimpleTask or SabberStoneCore.Enchants.Power or SabberStoneCore.Triggers.Trigger)) return false;
                foreach (var f in o.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                    if (Walk(f.GetValue(o), depth + 1)) return true;
                foreach (var pr in o.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                    if (pr.GetIndexParameters().Length == 0 && (typeof(SabberStoneCore.Tasks.ISimpleTask).IsAssignableFrom(pr.PropertyType) || pr.PropertyType == typeof(SabberStoneCore.Triggers.Trigger)))
                        try { if (Walk(pr.GetValue(o), depth + 1)) return true; } catch (Exception) { }
                return false;
            }
            bool result = Walk(enchantment.Power, 0);
            capturing[enchantment.Id] = result;
            return result;
        }
    }

    private static readonly System.Reflection.PropertyInfo DeckCardsProperty = typeof(Controller).GetProperty(nameof(Controller.DeckCards));

    /// <summary>
    /// The players' starting decks (Controller.DeckCards: "if your starting deck ...", Kazakusan-like counts): every card live made
    /// before its first generated entity, as the viewer knows it (a later reveal) or a placeholder. ("Didn't start in your deck" is
    /// Game.FirstGeneratedEntityId for every card since 2026-10-10 — SelfCondition.IsNotStartInDeck no longer guesses from the deck
    /// sizes, so the lists aren't evened to it.)
    /// </summary>
    private static void StartingDecks(LiveAction a, SGame g, Dictionary<int, LiveEntity> live, Card placeholder)
    {
        if (DeckCardsProperty?.SetMethod == null) return;
        var decks = new Dictionary<int, List<Card>> { [1] = new(), [2] = new() };
        foreach (LiveEntity e in live.Values.Where(x => x.Id < a.FirstGenerated && x.Id > 3).OrderBy(x => x.Id))
        {
            if (e[GameTag.CONTROLLER] is not (1 or 2)) continue;
            Card c = CardFor(a.CardOf(e.Id));
            if (c != null && c.Type is CardType.HERO or CardType.HERO_POWER or CardType.ENCHANTMENT or CardType.GAME or CardType.PLAYER) continue;
            if (c == null && e[GameTag.ZONE] is not ((int)Zone.DECK or (int)Zone.HAND or (int)Zone.GRAVEYARD or (int)Zone.SETASIDE or (int)Zone.REMOVEDFROMGAME)) continue;
            decks[e[GameTag.CONTROLLER]].Add(c ?? placeholder);
        }
        DeckCardsProperty.SetValue(g.Player1, decks[1]);
        DeckCardsProperty.SetValue(g.Player2, decks[2]);
    }

    private static void SetFirstGenerated(SGame g, int id) =>
        typeof(SGame).GetProperty(nameof(SGame.FirstGeneratedEntityId)).GetSetMethod(true).Invoke(g, new object[] { id });

    private static void SetField(object boxed, string field, int value) =>
        boxed.GetType().GetField(field).SetValue(boxed, value);

    /// <summary>A live weapon's Durability: live sends it as HEALTH (the engine keeps DURABILITY).</summary>
    internal static int LiveDurability(LiveEntity e) => e[GameTag.DURABILITY] > 0 ? e[GameTag.DURABILITY] : e[GameTag.HEALTH];

    /// <summary>Sets a value whose shown number has modifiers on top: base adjusted until it shows the live number.</summary>
    private static void Fit(Func<int> get, Action<int> set, int want)
    {
        if (get() == want) return; // (already shown: what the rebuilt modifiers give is kept, not folded into the base)
        int b = want;
        for (int i = 0; i < 5; i++)
        {
            set(b);
            int now = get();
            if (now == want) return;
            b += want - now;
        }
    }

    /// <summary>Live's tags onto our entity; with <paramref name="enforce"/>, also the keywords live doesn't show switched off.</summary>
    private static void CopyTags(LiveEntity e, IEntity ours, bool enforce)
    {
        // (RESOURCES_USED last: the engine moves it with OVERLOAD_LOCKED, live's already counts the locked crystals)
        foreach (var (tag, value) in e.Tags.OrderBy(t => t.Key == (int)GameTag.RESOURCES_USED ? 1 : 0))
        {
            if (NotCopied.Contains(tag) || tag >= 990000) continue;
            var t = (GameTag)tag;
            try
            {
                // (2026-11-16) Armor: the hero's own (Character.Armor), not the indexer's — that falls back to a hero card's printed
                // ARMOR (Shadowcrafter Scabbs 5), so "already 5" left ours at 0 and damage skipped the Armor
                if (t == GameTag.ARMOR && ours is Character armored) { if (armored.Armor != value) armored.Armor = value; continue; }
                if (ours[t] != value) ours[t] = value;
            }
            catch (Exception) { /* a tag the engine can't take on this kind of entity */ }
        }
        // states live shows by their absence (0): set them on every entity in play
        if (e[GameTag.ZONE] == (int)Zone.PLAY)
            foreach (GameTag k in StateTags)
                if (!e.Tags.ContainsKey((int)k))
                    try { if (ours[k] != 0) ours[k] = 0; } catch (Exception) { }
        // (a hero card's own Armor — Lord Jaraxxus' 5 — that live shows spent by its absence)
        if (ours is Hero && e[GameTag.ZONE] == (int)Zone.PLAY && !e.Tags.ContainsKey((int)GameTag.ARMOR))
            try { if (ours[GameTag.ARMOR] != 0) ours[GameTag.ARMOR] = 0; } catch (Exception) { }
        // (a cached state: the tag alone doesn't change it)
        if (ours is Playable pl && (e[GameTag.ZONE] == (int)Zone.PLAY || e.Tags.ContainsKey((int)GameTag.EXHAUSTED)))
        {
            pl.IsExhausted = e[GameTag.EXHAUSTED] > 0;
            // (a Rush minion in its first turn: live keeps it EXHAUSTED and marks it ATTACKABLE_BY_RUSH — and leaves the mark on
            // afterwards; ours keeps it awake with AttackableByRush, cleared at the turn's end: Minion.IsRush)
            if (pl is Minion rush && e[GameTag.ZONE] == (int)Zone.PLAY)
            {
                bool rushOnly = e[GameTag.ATTACKABLE_BY_RUSH] > 0 && e[GameTag.NUM_TURNS_IN_PLAY] == 0;
                if (rush.AttackableByRush != rushOnly) rush.AttackableByRush = rushOnly;
                if (rushOnly && pl.IsExhausted && e[GameTag.NUM_ATTACKS_THIS_TURN] < (e[GameTag.WINDFURY] > 0 ? 2 : 1) && e[GameTag.FROZEN] == 0)
                {
                    rush.IsExhausted = false;
                    if (!rush.Game.RushMinions.Contains(rush.Id)) rush.Game.RushMinions.Add(rush.Id);
                }
                // (Charge given with a steal — Shadow Madness: live keeps the stolen minion EXHAUSTED with NON_KEYWORD_CHARGE (3968); ours keeps a
                // Charge minion awake)
                else if ((e[GameTag.CHARGE] > 0 || e[(GameTag)3968] > 0) && pl.IsExhausted &&
                         e[GameTag.NUM_ATTACKS_THIS_TURN] < (e[GameTag.WINDFURY] > 0 ? 2 : 1) && e[GameTag.FROZEN] == 0)
                    rush.IsExhausted = false;
            }
        }
        if (!enforce) return;
        foreach (GameTag k in Keywords)
        {
            if (k is GameTag.SILENCED or GameTag.DORMANT) continue;
            // (2026-11-16) a card in hand keeps what is printed on it: live tags a hand card's CANT_ATTACK only when it is played
            // (Ancient Watcher, Ragnaros: 15 DIFFERS "kw live =CANT_ATTACK, ours unchanged" were the rebuild clearing it)
            if (ours.Zone?.Type == Zone.HAND && ours.Card[k] > 0) continue;
            if (!e.Tags.ContainsKey((int)k))
                try { if (ours[k] != 0) ours[k] = 0; } catch (Exception) { }
        }
    }

    /// <summary>The task for the live action in our game, or why there's none.</summary>
    private static PlayerTask TaskFor(LiveAction a, Rebuilt r, int chooseOne, out string why)
    {
        why = null;
        SGame g = r.G;
        if (a.EndTurn) return EndTurnTask.Any(r.Actor);
        if (!r.LiveToOurs.TryGetValue(a.Source, out int srcId))
        {
            // a Titan ability / an option entity: not rebuilt as such
            why = $"source {a.CardOf(a.Source)} not in our position ({ZoneName(a.Before.GetValueOrDefault(a.Source)?[GameTag.ZONE] ?? 0)})";
            return null;
        }
        IPlayable src = g.IdEntityDic[srcId];
        IPlayable target = null;
        if (a.Target != 0)
        {
            if (!r.LiveToOurs.TryGetValue(a.Target, out int tid)) { why = $"target {a.CardOf(a.Target)} not in our position"; return null; }
            target = tid == g.Player1.Hero.Id || tid == g.Player2.Hero.Id ? (tid == g.Player1.Hero.Id ? g.Player1.Hero : g.Player2.Hero) : g.IdEntityDic[tid];
        }
        Controller me = r.Actor;
        if (a.Block == HistoryBlock.Type.ATTACK)
        {
            if (target is not ICharacter defender) { why = "attack without a character target"; return null; }
            if (src is Hero) return HeroAttackTask.Any(me, defender);
            if (src is Minion m) return MinionAttackTask.Any(me, m, defender);
            why = $"attacker {src.Card.Id} isn't a character";
            return null;
        }
        if (a.Block == HistoryBlock.Type.DECK_ACTION)
        {
            // (a hidden card known only by a later reveal that can't do a deck action: it wasn't that card yet)
            if (src.Card[SabberStoneCore.Actions.Generic.PREPARE] == 0 && src.Card[SabberStoneCore.Actions.Generic.FORGE] == 0 && src.Card[SabberStoneCore.Actions.Generic.TRADEABLE] == 0 &&
                !(a.Before.GetValueOrDefault(a.Source)?.Visible ?? false))
            {
                why = $"a deck action by a hidden card later shown as {src.Card.Id}, which has none";
                return null;
            }
            // a deck action: Prepare, Forge or Trade (live sets the option's tag on the card)
            // (an opponent's: live shows only PREPARE_REVEALED / FORGE_REVEALED on the hidden card)
            if (a.SourceTagsSet.Contains((int)SabberStoneCore.Actions.Generic.IS_USING_PREPARE_OPTION) ||
                a.SourceTagsSet.Contains((int)SabberStoneCore.Actions.Generic.PREPARE_REVEALED)) return PrepareTask.Any(me, src);
            if (a.SourceTagsSet.Contains((int)SabberStoneCore.Actions.Generic.IS_USING_FORGE_OPTION) ||
                a.SourceTagsSet.Contains((int)SabberStoneCore.Actions.Generic.FORGE_REVEALED)) return ForgeTask.Any(me, src);
            return TradeTask.Any(me, src);
        }
        if (src is HeroPower hp)
            return HeroPowerTask.Any(me, target as ICharacter, chooseOne, power: hp == me.Hero.HeroPower ? null : hp);
        if (src is Location loc && src.Zone?.Type == Zone.PLAY) return LocationTask.Any(me, loc, target);
        if (src is Minion titan && src.Zone?.Type == Zone.PLAY && SabberStoneCore.Actions.Generic.IsTitan(titan))
        {
            // a Titan's ability: the picked sub-option (the client's) or the block's
            int index = a.SubOptionEntity != 0 ? SabberStoneCore.Actions.Generic.TitanAbilityIndex(titan, CardFor(a.CardOf(a.SubOptionEntity))) : a.BlockSubOption;
            if (index < 0) { why = "Titan ability unknown"; return null; }
            return TitanAbilityTask.Any(me, titan, index, target);
        }
        if (src.Zone?.Type == Zone.HAND)
        {
            int pos = -1;
            if (src is Minion && a.Position > 0) pos = Math.Min(a.Position - 1, me.BoardZone.Count);
            return PlayCardTask.Any(me, src, target, pos, chooseOne);
        }
        why = $"source {src.Card.Id} in {src.Zone?.Type}: no way to use it";
        return null;
    }

    /// <summary>Which Choose One option the live play took (1-based), 0 none, -1 unknown (try each).</summary>
    private static int ChooseOneOf(LiveAction a, Rebuilt r)
    {
        if (!r.LiveToOurs.TryGetValue(a.Source, out int srcId) || r.G.IdEntityDic[srcId] is not IPlayable src || !src.ChooseOne) return 0;
        string picked = a.SubOptionEntity != 0 ? Canon(a.CardOf(a.SubOptionEntity)) : null;
        Card[] options = Cards.ChooseOneOptions(src.Card);
        if (picked != null && options != null)
        {
            for (int i = 0; i < options.Length; i++)
                if (Canon(options[i].Id) == picked) return i + 1;
            // (a combined option: Fandral's both)
            return options.Length + 1;
        }
        if (a.BlockSubOption >= 0) return a.BlockSubOption + 1;
        return -1;
    }

    /// <summary>The live action played in our rebuilt game; its picks follow live's. Returns why it couldn't be done, or null.</summary>
    private static string Perform(LiveAction a, Rebuilt r, int chooseOne, int choiceVariant, List<string> randomPicks)
    {
        PlayerTask task = TaskFor(a, r, chooseOne, out string why);
        if (task == null) return "NR:" + why;
        SGame g = r.G;
        bool ok;
        int mark = g.PowerHistory.Full.Count;
        try { ok = g.Process(task); }
        catch (Exception ex) { return "ERROR:" + ErrorText(ex); }
        // (an attack cut short — the attacker destroyed by a Secret on the way, Vaporize — reports false although it was made: its
        // block is in the history)
        if (!ok && !ActionStarted(g, mark, task)) return "REFUSED:" + WhyRefused(task, r);
        int liveChoice = 0;
        for (int n = 0; n < 8 && g.State == State.RUNNING; n++)
        {
            Controller c = g.Player1.Choice != null ? g.Player1 : g.Player2.Choice != null ? g.Player2 : null;
            if (c == null) break;
            Choice choice = c.Choice;
            int pick = choice.Choices[0];
            LiveChoice lc = liveChoice < a.Choices.Count ? a.Choices[liveChoice++] : null;
            string want = null;
            int livePicked = 0;
            if (lc != null)
            {
                int picked = lc.Picked?.FirstOrDefault() ?? 0;
                if (picked == 0)
                    // the opponent's pick: the offered entity that went somewhere (not to the graveyard)
                    picked = lc.Entities.FirstOrDefault(id => a.After.TryGetValue(id, out var x) && x[GameTag.ZONE] is (int)Zone.HAND or (int)Zone.PLAY or (int)Zone.DECK or (int)Zone.SECRET);
                if (picked != 0) want = Canon(a.CardOf(picked, after: true));
                livePicked = picked;
            }
            // (2026-11-20) a pick of something already in the game (a TARGET choice: Flame Shock's second enemy) is that very entity — two
            // copies of a card on the board are two choices (20261021-194927 203917: BE_550 #163, not the other BE_550)
            int match = livePicked != 0 && r.LiveToOurs.TryGetValue(livePicked, out int same) && choice.Choices.Contains(same) ? same : 0;
            if (match == 0 && want != null) match = choice.Choices.FirstOrDefault(id => g.IdEntityDic.TryGetValue(id, out IPlayable o) && o.Card.Id == want);
            if (match != 0) pick = match;
            else
            {
                // not offered here (a random offer): the variant's option, noted as random
                pick = choice.Choices[Math.Min(choiceVariant, choice.Choices.Count - 1)];
                randomPicks.Add(want ?? "?");
            }
            try
            {
                if (!g.Process(ChooseTask.Pick(c, pick))) return "REFUSED-CHOICE";
            }
            catch (Exception ex) { return "ERROR:" + ErrorText(ex); }
        }
        return null;
    }

    /// <summary>An exception as one line: its message, the innermost cause and where it came from.</summary>
    private static string ErrorText(Exception ex)
    {
        Exception inner = ex;
        while (inner.InnerException != null) inner = inner.InnerException;
        static string FirstLine(string m) => m.Split((char)10)[0].Trim();
        string text = $"{ex.GetType().Name}: {FirstLine(ex.Message)}";
        if (inner != ex) text += $" <- {inner.GetType().Name}: {FirstLine(inner.Message)}";
        return text + " at " + Where(inner);
    }

    /// <summary>Our game started the action (its PLAY / ATTACK / DECK_ACTION block from the task's source is in the history).</summary>
    private static bool ActionStarted(SGame g, int mark, PlayerTask task)
    {
        int source = task.Source?.Id ?? (task.PlayerTaskType == PlayerTaskType.HERO_POWER ? task.Controller.Hero.HeroPower.Id : -1);
        foreach (var e in g.PowerHistory.Full.Skip(mark))
            if (e is SabberStoneCore.Kettle.PowerHistoryBlockStart b && b.Source == source &&
                (b.BlockType is SabberStoneCore.Enums.BlockType.ATTACK or SabberStoneCore.Enums.BlockType.PLAY || (int)b.BlockType == (int)HistoryBlock.Type.DECK_ACTION))
                return true;
        return false;
    }

    /// <summary>Why our engine refused a task live allowed (what its checks see).</summary>
    private static string WhyRefused(PlayerTask task, Rebuilt r)
    {
        Controller me = r.Actor;
        IPlayable src = task.Source ?? (task.PlayerTaskType == PlayerTaskType.HERO_POWER ? me.Hero.HeroPower : null);
        var parts = new List<string>();
        if (src is Character ch && task.PlayerTaskType is PlayerTaskType.MINION_ATTACK or PlayerTaskType.HERO_ATTACK)
        {
            parts.Add($"canAttack={ch.CanAttack} exhausted={ch.IsExhausted} frozen={ch.IsFrozen} atk={ch.AttackDamage} attacks={ch.NumAttacksThisTurn} cantAttack={ch.CantAttack}");
            if (ch is Minion rm && (rm.AttackableByRush || rm.IsRush)) parts.Add($"rush={rm.IsRush} rushOnly={rm.AttackableByRush} turnsInPlay={rm[GameTag.NUM_TURNS_IN_PLAY]}");
            if (ch.CantAttackHeroes) parts.Add("cantAttackHeroes");
            if (task.Target is ICharacter t)
            {
                parts.Add($"validTarget={ch.IsValidAttackTarget(t)}");
                if (t.HasStealth || t.IsImmune) parts.Add($"target stealth={t.HasStealth} immune={t.IsImmune}");
                if (ch.Controller.Opponent.BoardZone.Any(x => x.HasTaunt && !x.HasStealth && !x.IsImmune) && !t.HasTaunt)
                    parts.Add("taunt: " + string.Join(",", ch.Controller.Opponent.BoardZone.Where(x => x.HasTaunt).Select(x => $"{x.Card.Id}#{r.OursToLive.GetValueOrDefault(x.Id)}")));
            }
        }
        else if (src != null)
        {
            parts.Add($"cost={src.Cost} mana={me.RemainingMana} playable={src.IsPlayable} byPlayer={src.IsPlayableByPlayer} byCardReq={src.IsPlayableByCardReq}");
            if (task.Target != null)
            {
                parts.Add($"validTarget={src.IsValidPlayTarget(task.Target)}");
                if (task.Target is ICharacter tc)
                    parts.Add($"target {tc.Card.Id} zone={tc.Zone?.Type} stealth={tc.HasStealth} immune={tc.IsImmune} elusive={tc.CantBeTargetedBySpells}/{tc.CantBeTargetedByHeroPowers}" +
                              $" untouchable={(tc is Minion um && um.Untouchable)}");
            }
        }
        bool offered = me.Options().Any(o => o.PlayerTaskType == task.PlayerTaskType && (o.Source == task.Source || o.Source == null && task.Source == null));
        parts.Add(offered ? "offered with other targets" : "not offered");
        return string.Join(" ", parts);
    }

    private static string ZoneName(int zone) => zone switch
    {
        1 => "PLAY", 2 => "DECK", 3 => "HAND", 4 => "GRAVEYARD", 5 => "REMOVEDFROMGAME", 6 => "SETASIDE", 7 => "SECRET", 0 => "-", _ => zone.ToString(),
    };
}
