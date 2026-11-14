using PegasusGame;
using PegasusShared;

namespace HsServer.Engine;

/// <summary>Deck, hero and identity of one side at game start.</summary>
internal sealed class PlayerSetup
{
    public string HeroCardId = "HERO_08";
    public List<string> Deck = new();
    /// <summary>The deck's sideboards: (owner card id, sideboard card id) in the deck builder's order (rules engine only).</summary>
    public List<(string Owner, string Card)> Sideboard = new();
    public bool IsAi;
    public ulong AccountHi;
    public ulong AccountLo;
    public int CardBack;
    /// <summary>The player's coin skin card id (the deck's coin, else a favorite; null: The Coin) — rules engine.</summary>
    public string CoinCardId;
    /// <summary>(2026-10-09, Dalaran Heist) the Hero Power when not the hero's own, and the hero's Health for this game (rules
    /// engine; null: the hero card's).</summary>
    public string HeroPowerCardId;
    public int? StartHealth;
    /// <summary>(2026-10-09, Dalaran Heist) the deck's adventure-run slots and enchantments (rules engine; null: not a run deck).</summary>
    public RunDeckSetup RunDeck;
    /// <summary>(2026-10-09, Dalaran Heist) the chapter's twist played for this player as the game starts (client WingDbf.PlotTwistCardId;
    /// rules engine: GameConfig.Player1PlotTwist → Generic.PlayPlotTwist); null: none.</summary>
    public string PlotTwistCardId;
    /// <summary>(2026-10-09, Dalaran Heist Anomaly Mode) the run's anomaly (DUNGEON_CRAWL_CURRENT_ANOMALY_MODE_CARD 243 when the run is in
    /// anomaly mode) played for this player once the mulligan is done (rules engine: GameConfig.Player1Anomaly → Generic.PlayAnomaly);
    /// null: none.</summary>
    public string AnomalyCardId;
}

internal sealed class PlayerState
{
    public int Index; // PLAYER_ID: 1 or 2
    public bool IsAi;
    public Entity Player;
    public Entity Hero;
    public Entity HeroPower;
    public PlayerState Opponent;
    public readonly List<Entity> Deck = new();
    public readonly List<Entity> Hand = new();
    public readonly List<Entity> Board = new();
    public int MulliganChoiceId;
    public List<int> MulliganKeep;

    public int AvailableMana =>
        Player[GAME_TAG.RESOURCES] + Player[GAME_TAG.TEMP_RESOURCES] - Player[GAME_TAG.RESOURCES_USED];
}

/// <summary>
/// The rules engine. Every state change is reported through <see cref="Set"/>/<see cref="History"/> in the order and
/// shape Blizzard's server uses (recorded official Power.log is the reference): STEP/NEXT_STEP state machine,
/// TRIGGER blocks per step, draw bookkeeping tags, mulligan states, PREDAMAGE + META DAMAGE, DEATHS blocks.
/// </summary>
internal sealed class GameState
{
    public const int MaxHand = 10;
    public const int MaxBoard = 7;
    private const int NextCardToDraw = 2166; // player tag set before each draw (unnamed in the client enum)
    private const int CardsLeftToDraw = 467;

    private readonly CardDb db;
    private readonly Random rng;
    private readonly Dictionary<int, Entity> entities = new();
    private int nextEntityId = 1;
    private int nextChoiceId = 1;
    private int optionsId;
    private List<Option> currentOptions;

    public readonly History History = new();
    public Entity Game { get; private set; }
    public PlayerState P1 { get; private set; }
    public PlayerState P2 { get; private set; }
    public PlayerState Current { get; private set; }
    public bool Over { get; private set; }
    public PlayerState Winner { get; private set; }
    public int Turn => Game[GAME_TAG.TURN];
    /// <summary>The player whose view the client renders (hidden information is filtered for this side).</summary>
    public PlayerState Viewer => P1.IsAi ? P2 : P1;

    public GameState(CardDb db, int seed)
    {
        this.db = db;
        rng = new Random(seed);
    }

    public CardDb Cards => db;
    public Random Random => rng;

    public Entity Get(int id) => entities.TryGetValue(id, out var e) ? e : null;

    public void RandomEnemyDamage(PlayerState p, int amount, Entity source)
    {
        var targets = p.Opponent.Board.Where(m => m.Health > 0).Append(p.Opponent.Hero).ToList();
        Damage(targets[rng.Next(targets.Count)], amount, source);
    }
    public PlayerState PlayerOf(Entity e) => e.Controller == 1 ? P1 : P2;

    // ---------- tag writes ----------

    public void Set(Entity e, GAME_TAG tag, int value)
    {
        if (e[tag] == value) return;
        e.Tags[(int)tag] = value;
        History.TagChange(e.Id, tag, value);
    }

    private void SetRaw(Entity e, int tag, int value)
    {
        e.Tags[tag] = value;
        History.TagChange(e.Id, (GAME_TAG)tag, value);
    }

    private void Add(Entity e, GAME_TAG tag, int delta) => Set(e, tag, e[tag] + delta);

    // ---------- setup ----------

    public void Start(PlayerSetup first, PlayerSetup second, int scenario)
    {
        Game = NewEntity(null);
        Game.Tags[(int)GAME_TAG.CARDTYPE] = (int)TAG_CARDTYPE.GAME;
        Game.Tags[(int)GAME_TAG.ZONE] = (int)TAG_ZONE.PLAY;
        Game.Tags[(int)GAME_TAG.ENTITY_ID] = Game.Id;
        if (scenario != 0) Game.Tags[937] = scenario;
        Game.Tags[(int)GAME_TAG.SPAWN_TIME_COUNT] = 1;
        Game.Tags[(int)GAME_TAG.GAME_SEED] = rng.Next(1, int.MaxValue);

        P1 = NewPlayer(1, first);
        P2 = NewPlayer(2, second);
        P1.Opponent = P2;
        P2.Opponent = P1;

        // Deck entities: player 1's cards, then player 2's (ids 4..), shuffled, created hidden.
        foreach (var (p, setup) in new[] { (P1, first), (P2, second) })
            foreach (string cardId in setup.Deck.OrderBy(_ => rng.Next()))
            {
                Entity card = NewCard(db[cardId], p, TAG_ZONE.DECK);
                p.Deck.Add(card);
            }
        foreach (var (p, setup) in new[] { (P1, first), (P2, second) })
        {
            p.Hero = NewCard(db[setup.HeroCardId], p, TAG_ZONE.PLAY);
            p.Player.Tags[(int)GAME_TAG.HERO_ENTITY] = p.Hero.Id;
            CardDef power = db.ByDbf(p.Hero.Card.Tag(GAME_TAG.HERO_POWER)) ?? db["HERO_08bp"];
            p.HeroPower = NewCard(power, p, TAG_ZONE.PLAY);
            p.HeroPower.Tags[(int)GAME_TAG.CREATOR] = p.Hero.Id;
        }

        History.Add(new PowerHistoryData
        {
            CreateGame = new PowerHistoryCreateGame
            {
                GameEntity = new PegasusGame.Entity { Id = Game.Id, Tags = History.Tags(Game.Tags) },
                Players = new List<PegasusGame.Player> { PlayerInfo(P1, first), PlayerInfo(P2, second) },
                PlayerInfos = new List<SharedPlayerInfo>(),
                ActionInfos = new List<ActionInfo>(),
                GameUuid = Guid.NewGuid().ToString(),
            },
        });
        foreach (PlayerState p in new[] { P1, P2 })
            foreach (Entity card in p.Deck) FullEntity(card, reveal: false);
        foreach (PlayerState p in new[] { P1, P2 })
        {
            FullEntity(p.Hero, reveal: true);
            FullEntity(p.HeroPower, reveal: true);
        }

        Set(Game, GAME_TAG.STATE, (int)TAG_STATE.RUNNING);
        Set(P1.Player, GAME_TAG.PLAYSTATE, (int)TAG_PLAYSTATE.PLAYING);
        Set(P2.Player, GAME_TAG.PLAYSTATE, (int)TAG_PLAYSTATE.PLAYING);

        History.BlockStart(HistoryBlock.Type.TRIGGER, Game.Id);
        Current = P1;
        Set(P1.Player, GAME_TAG.CURRENT_PLAYER, 1);
        Set(P1.Player, GAME_TAG.FIRST_PLAYER, 1);
        Set(Game, GAME_TAG.TURN, 1);
        DrawCards(P1, 3);
        Set(P1.Player, GAME_TAG.NUM_TURNS_LEFT, 1);
        DrawCards(P2, 4);
        Set(P2.Player, GAME_TAG.NUM_TURNS_LEFT, 1);
        Set(Game, GAME_TAG.NEXT_STEP, (int)TAG_STEP.BEGIN_MULLIGAN);
        History.BlockEnd();

        Set(Game, GAME_TAG.STEP, (int)TAG_STEP.BEGIN_MULLIGAN);
        History.BlockStart(HistoryBlock.Type.TRIGGER, Game.Id);
        foreach (PlayerState p in new[] { P1, P2 })
        {
            Set(p.Player, GAME_TAG.MULLIGAN_STATE, (int)TAG_MULLIGAN.INPUT);
            p.MulliganChoiceId = nextChoiceId++;
            History.Send(new EntityChoices
            {
                Id = p.MulliganChoiceId, ChoiceType = (int)CHOICE_TYPE.MULLIGAN, CountMin = 0, CountMax = p.Hand.Count,
                Entities = p.Hand.Select(c => c.Id).ToList(), Source = Game.Id, PlayerId = p.Index,
                UnchoosableEntities = new List<int>(),
            });
        }
        History.BlockEnd();
    }

    private PlayerState NewPlayer(int index, PlayerSetup setup)
    {
        var p = new PlayerState { Index = index, IsAi = setup.IsAi, Player = NewEntity(null) };
        var t = p.Player.Tags;
        t[(int)GAME_TAG.CONTROLLER] = index;
        t[(int)GAME_TAG.CARDTYPE] = (int)TAG_CARDTYPE.PLAYER;
        t[(int)GAME_TAG.PLAYER_ID] = index;
        t[(int)GAME_TAG.MAXHANDSIZE] = MaxHand;
        t[(int)GAME_TAG.STARTHANDSIZE] = 4;
        t[(int)GAME_TAG.TEAM_ID] = index;
        t[(int)GAME_TAG.ZONE] = (int)TAG_ZONE.PLAY;
        t[(int)GAME_TAG.ENTITY_ID] = p.Player.Id;
        t[(int)GAME_TAG.MAXRESOURCES] = 10;
        t[(int)GAME_TAG.SPAWN_TIME_COUNT] = 1;
        return p;
    }

    private static PegasusGame.Player PlayerInfo(PlayerState p, PlayerSetup setup) => new()
    {
        Id = p.Index,
        GameAccountId = new BnetId { Hi = setup.AccountHi, Lo = setup.AccountLo },
        CardBack = setup.CardBack,
        Entity = new PegasusGame.Entity { Id = p.Player.Id, Tags = History.Tags(p.Player.Tags), TagLists = new List<TagList>() },
    };

    private Entity NewEntity(CardDef card)
    {
        var e = new Entity(nextEntityId++, card);
        entities[e.Id] = e;
        return e;
    }

    private Entity NewCard(CardDef card, PlayerState owner, TAG_ZONE zone)
    {
        Entity e = NewEntity(card);
        foreach (var (tag, value) in card.Tags) e.Tags[tag] = value;
        e.Tags[(int)GAME_TAG.CONTROLLER] = owner.Index;
        e.Tags[(int)GAME_TAG.ZONE] = (int)zone;
        e.Tags[(int)GAME_TAG.ENTITY_ID] = e.Id;
        e.Tags[(int)GAME_TAG.SPAWN_TIME_COUNT] = 1;
        return e;
    }

    /// <summary>FULL_ENTITY: hidden entities carry only zone/controller/id; revealed ones all tags.</summary>
    private void FullEntity(Entity e, bool reveal)
    {
        var tags = reveal ? e.Tags.ToList() : new List<KeyValuePair<int, int>>
        {
            new((int)GAME_TAG.ZONE, e[GAME_TAG.ZONE]),
            new((int)GAME_TAG.CONTROLLER, e[GAME_TAG.CONTROLLER]),
            new((int)GAME_TAG.ENTITY_ID, e.Id),
        };
        e.RevealedToClient = reveal;
        History.Add(new PowerHistoryData
        {
            FullEntity = new PowerHistoryEntity
            {
                Entity = e.Id, Name = reveal ? e.CardId : "", Tags = History.Tags(tags),
                DefTags = new List<Tag>(), TagLists = new List<TagList>(), DefTagLists = new List<TagList>(),
            },
        });
    }

    private void ShowEntity(Entity e)
    {
        e.RevealedToClient = true;
        History.Add(new PowerHistoryData
        {
            ShowEntity = new PowerHistoryEntity
            {
                Entity = e.Id, Name = e.CardId, Tags = History.Tags(e.Tags),
                DefTags = new List<Tag>(), TagLists = new List<TagList>(), DefTagLists = new List<TagList>(),
            },
        });
    }

    /// <summary>
    /// Move an entity to a zone and report it: a SHOW_ENTITY (full tags incl. the new zone) when the client must
    /// learn the card's identity now, otherwise a ZONE tag change. Never pre-write the tag before calling
    /// <see cref="Set"/> — Set only reports actual changes.
    /// </summary>
    private void MoveTo(Entity e, TAG_ZONE zone, bool reveal)
    {
        if (reveal && !e.RevealedToClient)
        {
            e.Tags[(int)GAME_TAG.ZONE] = (int)zone;
            ShowEntity(e);
        }
        else Set(e, GAME_TAG.ZONE, (int)zone);
    }

    private void HideEntity(Entity e, TAG_ZONE zone)
    {
        e.RevealedToClient = false;
        History.Add(new PowerHistoryData { HideEntity = new PowerHistoryHide { Entity = e.Id, Zone = (int)zone } });
    }

    /// <summary>Summon a new minion straight onto the board (tokens, hero power summons).</summary>
    public Entity Summon(PlayerState p, CardDef card)
    {
        if (p.Board.Count >= MaxBoard) return null;
        Entity e = NewCard(card, p, TAG_ZONE.PLAY);
        e.Tags[(int)GAME_TAG.ZONE_POSITION] = p.Board.Count + 1;
        e.Tags[(int)GAME_TAG.EXHAUSTED] = 1;
        p.Board.Add(e);
        FullEntity(e, reveal: true);
        return e;
    }

    // ---------- drawing ----------

    private void DrawCards(PlayerState p, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (p.Deck.Count == 0) { Fatigue(p); continue; }
            Entity card = p.Deck[0];
            SetRaw(p.Player, NextCardToDraw, card.Id);
            SetRaw(p.Player, CardsLeftToDraw, count - i);
            p.Deck.RemoveAt(0);
            if (p.Hand.Count >= MaxHand) { Burn(card); continue; }
            p.Hand.Add(card);
            MoveTo(card, TAG_ZONE.HAND, reveal: p == Viewer);
            Set(card, GAME_TAG.NUM_TURNS_IN_HAND, 1);
            // Blizzard reports hand insertion as pos -> 0 -> pos.
            int pos = p.Hand.Count;
            SetRaw(card, (int)GAME_TAG.ZONE_POSITION, pos);
            SetRaw(card, (int)GAME_TAG.ZONE_POSITION, 0);
            SetRaw(card, (int)GAME_TAG.ZONE_POSITION, pos);
        }
        if (count > 0) SetRaw(p.Player, CardsLeftToDraw, 0);
    }

    private void Burn(Entity card) => MoveTo(card, TAG_ZONE.GRAVEYARD, reveal: true);

    private void Fatigue(PlayerState p)
    {
        History.BlockStart(HistoryBlock.Type.FATIGUE, p.Player.Id);
        Add(p.Player, GAME_TAG.FATIGUE, 1);
        Damage(p.Hero, p.Player[GAME_TAG.FATIGUE], p.Player);
        History.BlockEnd();
    }

    // ---------- mulligan ----------

    /// <summary>A player's mulligan answer: the entities to keep. Returns true once both players are done.</summary>
    public bool Mulligan(PlayerState p, IReadOnlyCollection<int> keep)
    {
        if (p.MulliganKeep != null) return false;
        p.MulliganKeep = keep.Where(id => p.Hand.Any(c => c.Id == id)).ToList();
        History.Send(new EntitiesChosen
        {
            ChooseEntities = new ChooseEntities { Id = p.MulliganChoiceId, Entities = p.MulliganKeep.ToList() },
            PlayerId = p.Index, ChoiceType = (int)CHOICE_TYPE.MULLIGAN,
        });
        if (P1.MulliganKeep == null || P2.MulliganKeep == null) return false;

        foreach (PlayerState player in new[] { P2, P1 }) DealMulligan(player);
        History.BlockStart(HistoryBlock.Type.TRIGGER, P1.Player.Id);
        Set(P1.Player, GAME_TAG.MULLIGAN_STATE, (int)TAG_MULLIGAN.DONE);
        GiveCoin(P2);
        foreach (PlayerState player in new[] { P1, P2 })
            History.Add(new PowerHistoryData { ShuffleDeck = new PowerHistoryShuffleDeck { PlayerId = player.Index } });
        Set(P1.Player, GAME_TAG.TURN, 1);
        Set(Game, GAME_TAG.NEXT_STEP, (int)TAG_STEP.MAIN_READY);
        History.BlockEnd();
        StartTurn(P1);
        return true;
    }

    private void DealMulligan(PlayerState p)
    {
        Set(p.Player, GAME_TAG.MULLIGAN_STATE, (int)TAG_MULLIGAN.DEALING);
        History.BlockStart(HistoryBlock.Type.TRIGGER, p.Player.Id);
        foreach (Entity old in p.Hand.Where(c => !p.MulliganKeep.Contains(c.Id)).ToList())
        {
            if (p.Deck.Count == 0) break;
            int index = p.Hand.IndexOf(old);
            Entity fresh = p.Deck[0];
            p.Deck.RemoveAt(0);
            p.Hand[index] = fresh;
            MoveTo(fresh, TAG_ZONE.HAND, reveal: p == Viewer);
            Set(fresh, GAME_TAG.NUM_TURNS_IN_HAND, 1);
            Set(fresh, GAME_TAG.ZONE_POSITION, index + 1);
            // The replaced card goes back into the deck at a random place.
            Set(old, GAME_TAG.ZONE_POSITION, 0);
            if (old.RevealedToClient) HideEntity(old, TAG_ZONE.DECK);
            Set(old, GAME_TAG.ZONE, (int)TAG_ZONE.DECK);
            p.Deck.Insert(rng.Next(p.Deck.Count + 1), old);
        }
        Set(p.Player, GAME_TAG.MULLIGAN_STATE, (int)TAG_MULLIGAN.WAITING);
        History.BlockEnd();
        if (p == P2)
        {
            History.BlockStart(HistoryBlock.Type.TRIGGER, p.Player.Id);
            Set(p.Player, GAME_TAG.MULLIGAN_STATE, (int)TAG_MULLIGAN.DONE);
            History.BlockEnd();
        }
    }

    private void GiveCoin(PlayerState p)
    {
        if (!db.TryGet("GAME_005", out CardDef coin) || p.Hand.Count >= MaxHand) return;
        Entity e = NewCard(coin, p, TAG_ZONE.HAND);
        p.Hand.Add(e);
        e.Tags[(int)GAME_TAG.ZONE_POSITION] = p.Hand.Count;
        FullEntity(e, reveal: p == Viewer);
    }

    // ---------- turn structure ----------

    private void Step(TAG_STEP step, PlayerState owner, TAG_STEP next, Action inside = null)
    {
        Set(Game, GAME_TAG.STEP, (int)step);
        History.BlockStart(HistoryBlock.Type.TRIGGER, owner?.Player.Id ?? Game.Id);
        inside?.Invoke();
        Set(Game, GAME_TAG.NEXT_STEP, (int)next);
        History.BlockEnd();
    }

    private void StartTurn(PlayerState p)
    {
        Current = p;
        Step(TAG_STEP.MAIN_READY, p, TAG_STEP.MAIN_START_TRIGGERS, () =>
        {
            Set(p.Player, GAME_TAG.RESOURCES, Math.Min(10, p.Player[GAME_TAG.RESOURCES] + 1));
            Set(p.Player, GAME_TAG.RESOURCES_USED, 0);
            Set(p.Player, GAME_TAG.TEMP_RESOURCES, 0);
            Set(p.Player, GAME_TAG.NUM_CARDS_PLAYED_THIS_TURN, 0);
            Set(p.Opponent.Player, GAME_TAG.NUM_CARDS_DRAWN_THIS_TURN, 0);
            foreach (Entity m in p.Board)
            {
                Set(m, GAME_TAG.EXHAUSTED, 0);
                Set(m, GAME_TAG.NUM_ATTACKS_THIS_TURN, 0);
                Set(m, GAME_TAG.JUST_PLAYED, 0);
                Add(m, GAME_TAG.NUM_TURNS_IN_PLAY, 1);
            }
            Set(p.Hero, GAME_TAG.NUM_ATTACKS_THIS_TURN, 0);
            Set(p.HeroPower, GAME_TAG.EXHAUSTED, 0);
            Set(p.Player, GAME_TAG.HEROPOWER_ACTIVATIONS_THIS_TURN, 0);
            Set(p.HeroPower, GAME_TAG.HEROPOWER_ACTIVATIONS_THIS_TURN, 0);
        });
        Step(TAG_STEP.MAIN_START_TRIGGERS, p, TAG_STEP.MAIN_START);
        Step(TAG_STEP.MAIN_START, p, TAG_STEP.MAIN_ACTION, () =>
        {
            Add(p.Player, GAME_TAG.NUM_CARDS_DRAWN_THIS_TURN, 1);
            DrawCards(p, 1);
        });
        if (Over) return;
        Step(TAG_STEP.MAIN_ACTION, p, TAG_STEP.MAIN_END);
        if (!p.IsAi) SendOptions();
    }

    public void EndTurn()
    {
        PlayerState p = Current;
        Step(TAG_STEP.MAIN_END, p, TAG_STEP.MAIN_CLEANUP);
        // Live: frozen characters thaw in their controller's MAIN_CLEANUP block if they missed their attack this turn.
        Step(TAG_STEP.MAIN_CLEANUP, p, TAG_STEP.MAIN_NEXT, () =>
        {
            foreach (Entity c in p.Board.Append(p.Hero))
                if (c.Has(GAME_TAG.FROZEN) && c[GAME_TAG.NUM_ATTACKS_THIS_TURN] == 0) Set(c, GAME_TAG.FROZEN, 0);
        });
        PlayerState q = p.Opponent;
        Step(TAG_STEP.MAIN_NEXT, null, TAG_STEP.MAIN_READY, () =>
        {
            Set(p.Player, GAME_TAG.CURRENT_PLAYER, 0);
            Set(q.Player, GAME_TAG.CURRENT_PLAYER, 1);
            Add(Game, GAME_TAG.TURN, 1);
            Add(q.Player, GAME_TAG.TURN, 1);
        });
        StartTurn(q);
    }

    // ---------- options ----------

    public void SendOptions()
    {
        if (Over) return;
        PlayerState p = Current;
        var options = new List<Option>
        {
            new() { Type_ = Option.Type.END_TURN, SubOptions = new List<SubOption>() },
        };
        foreach (Entity card in p.Hand) options.Add(PowerOption(card, PlayableError(p, card), CardTargets(p, card)));
        options.Add(PowerOption(p.HeroPower, HeroPowerError(p), HeroPowerTargets(p)));
        foreach (Entity attacker in p.Board.Append(p.Hero).Where(e => e.Atk > 0 || e.Type == TAG_CARDTYPE.MINION))
        {
            var targets = AttackTargets(p, attacker);
            options.Add(PowerOption(attacker, AttackError(attacker, targets), targets));
        }
        currentOptions = options;
        History.Send(new AllOptions { Id = ++optionsId, Options = options });
    }

    private static Option PowerOption(Entity e, PlayErrorType error, List<Entity> targets) => new()
    {
        Type_ = Option.Type.POWER,
        MainOption = new SubOption
        {
            Id = e.Id, PlayError = (int)error,
            Targets = (targets ?? new List<Entity>()).Select(t => new TargetOption { Id = t.Id, PlayError = (int)PlayErrorType.NONE }).ToList(),
        },
        SubOptions = new List<SubOption>(),
    };

    private PlayErrorType PlayableError(PlayerState p, Entity card)
    {
        if (card.Cost > p.AvailableMana) return PlayErrorType.REQ_ENOUGH_MANA;
        if (card.Type == TAG_CARDTYPE.MINION && p.Board.Count >= MaxBoard) return PlayErrorType.REQ_NUM_MINION_SLOTS;
        if (card.Type != TAG_CARDTYPE.MINION && !CardScripts.CanPlaySpell(card)) return PlayErrorType.INVALID;
        return PlayErrorType.NONE;
    }

    private List<Entity> CardTargets(PlayerState p, Entity card) =>
        CardScripts.NeedsTarget(card) ? AllCharacters().Where(t => t.Zone == TAG_ZONE.PLAY).ToList() : null;

    private PlayErrorType HeroPowerError(PlayerState p)
    {
        if (p.HeroPower.Has(GAME_TAG.EXHAUSTED)) return PlayErrorType.REQ_NOT_EXHAUSTED_ACTIVATE;
        if (p.HeroPower.Cost > p.AvailableMana) return PlayErrorType.REQ_ENOUGH_MANA;
        if (!HeroPowers.IsSupported(this, p)) return PlayErrorType.INVALID;
        return PlayErrorType.NONE;
    }

    private List<Entity> HeroPowerTargets(PlayerState p) =>
        HeroPowers.NeedsTarget(p) ? AllCharacters().ToList() : null;

    private IEnumerable<Entity> AllCharacters() =>
        new[] { P1.Hero, P2.Hero }.Concat(P1.Board).Concat(P2.Board).Where(e => e.Zone == TAG_ZONE.PLAY);

    public List<Entity> AttackTargets(PlayerState p, Entity attacker)
    {
        var enemies = p.Opponent.Board.Where(m => !m.Has(GAME_TAG.STEALTH)).ToList();
        var taunts = enemies.Where(m => m.Has(GAME_TAG.TAUNT)).ToList();
        var targets = taunts.Count > 0 ? taunts : enemies.Append(p.Opponent.Hero).ToList();
        // Rush minions can't hit the hero on the turn they're played.
        if (attacker.Has(GAME_TAG.RUSH) && attacker.Has(GAME_TAG.JUST_PLAYED) && !attacker.Has(GAME_TAG.CHARGE))
            targets = targets.Where(t => t.Type == TAG_CARDTYPE.MINION).ToList();
        return targets;
    }

    private PlayErrorType AttackError(Entity attacker, List<Entity> targets)
    {
        if (attacker.Atk <= 0 || attacker.Has(GAME_TAG.FROZEN)) return PlayErrorType.REQ_ATTACKER_NOT_FROZEN;
        if (!CanAttackNow(attacker)) return PlayErrorType.REQ_ATTACKER_CAN_ATTACK;
        if (targets.Count == 0) return PlayErrorType.REQ_TARGET_TO_PLAY;
        return PlayErrorType.NONE;
    }

    public bool CanAttackNow(Entity attacker)
    {
        int allowed = attacker.Has(GAME_TAG.WINDFURY) ? 2 : 1;
        if (attacker[GAME_TAG.NUM_ATTACKS_THIS_TURN] >= allowed) return false;
        if (attacker.Type == TAG_CARDTYPE.MINION && attacker.Has(GAME_TAG.EXHAUSTED)
            && !attacker.Has(GAME_TAG.CHARGE) && !attacker.Has(GAME_TAG.RUSH)) return false;
        return attacker.Atk > 0 && !attacker.Has(GAME_TAG.FROZEN);
    }

    /// <summary>The client's ChooseOption. Returns false when it doesn't match the current options.</summary>
    public bool ChooseOption(int id, int index, int target, int position)
    {
        if (Over || id != optionsId || currentOptions == null || index < 0 || index >= currentOptions.Count) return false;
        Option option = currentOptions[index];
        currentOptions = null;
        if (option.Type_ == Option.Type.END_TURN)
        {
            EndTurn();
            return true;
        }
        Entity source = Get(option.MainOption.Id);
        Log.Info("engine", $"player {Current.Index} option {index}: {source} -> {(target == 0 ? "-" : Get(target)?.ToString())} pos {position}");
        bool ok = PerformAction(Current, source, target == 0 ? null : Get(target), position);
        if (!ok) Log.Warn("engine", $"rejected option {index} ({source}) target {target}");
        if (!Over && !Current.IsAi) SendOptions();
        return ok;
    }

    /// <summary>Play a card, use the hero power or attack — shared by the human option path and the AI.</summary>
    public bool PerformAction(PlayerState p, Entity source, Entity target, int position = 0)
    {
        if (source == null || Over) return false;
        if (p.IsAi) Log.Info("engine", $"AI: {source} -> {target?.ToString() ?? "-"}");
        if (p.Hand.Contains(source))
        {
            if (PlayableError(p, source) != PlayErrorType.NONE) return false;
            PlayCard(p, source, target, position);
        }
        else if (source == p.HeroPower)
        {
            if (HeroPowerError(p) != PlayErrorType.NONE) return false;
            UseHeroPower(p, target);
        }
        else if (source.Zone == TAG_ZONE.PLAY && source.Controller == p.Index)
        {
            var targets = AttackTargets(p, source);
            if (target == null || !targets.Contains(target) || !CanAttackNow(source)) return false;
            Attack(source, target);
        }
        else return false;
        ProcessDeaths();
        return true;
    }

    // ---------- actions ----------

    private void PlayCard(PlayerState p, Entity card, Entity target, int position)
    {
        History.BlockStart(HistoryBlock.Type.PLAY, card.Id, target?.Id ?? 0, effectIndex: 0);
        Add(p.Player, GAME_TAG.RESOURCES_USED, card.Cost);
        Add(p.Player, GAME_TAG.NUM_RESOURCES_SPENT_THIS_GAME, card.Cost);
        Add(p.Player, GAME_TAG.NUM_CARDS_PLAYED_THIS_TURN, 1);
        if (target != null) Set(card, GAME_TAG.CARD_TARGET, target.Id); // live: before JUST_PLAYED
        Set(card, GAME_TAG.JUST_PLAYED, 1);

        int handIndex = p.Hand.IndexOf(card);
        p.Hand.RemoveAt(handIndex);
        for (int i = handIndex; i < p.Hand.Count; i++) Set(p.Hand[i], GAME_TAG.ZONE_POSITION, i + 1);
        Set(card, GAME_TAG.ZONE_POSITION, 0);

        if (card.Type == TAG_CARDTYPE.MINION)
        {
            int slot = position <= 0 || position > p.Board.Count + 1 ? p.Board.Count + 1 : position;
            p.Board.Insert(slot - 1, card);
            MoveTo(card, TAG_ZONE.PLAY, reveal: true);
            for (int i = 0; i < p.Board.Count; i++) Set(p.Board[i], GAME_TAG.ZONE_POSITION, i + 1);
            Set(card, GAME_TAG.EXHAUSTED, 1);
            History.Meta(HistoryMeta.Type.SLUSH_TIME, SlushMinionPlay, card.Id);
            Effect(HistoryBlock.Type.POWER, card, target, () => CardScripts.Battlecry(this, p, card, target));
        }
        else
        {
            MoveTo(card, TAG_ZONE.PLAY, reveal: true);
            History.Meta(HistoryMeta.Type.SLUSH_TIME, SlushSpellPlay, card.Id);
            Effect(HistoryBlock.Type.POWER, card, target, () => CardScripts.CastSpell(this, p, card, target));
            Set(card, GAME_TAG.ZONE, (int)TAG_ZONE.GRAVEYARD);
        }
        History.BlockEnd();
    }

    private void UseHeroPower(PlayerState p, Entity target)
    {
        // Order as on live (Innkeeper's HERO_08bp): CARD_TARGET and SLUSH_TIME 2000 before the effect, the
        // activation counters and EXHAUSTED only after it. With EXHAUSTED first and no CARD_TARGET, the client only
        // showed the damage once the whole hero-power effect had finished (~500 ms after the missile hit).
        Entity power = p.HeroPower;
        History.BlockStart(HistoryBlock.Type.PLAY, power.Id, target?.Id ?? 0, effectIndex: 0);
        Add(p.Player, GAME_TAG.RESOURCES_USED, power.Cost);
        Add(p.Player, GAME_TAG.NUM_RESOURCES_SPENT_THIS_GAME, power.Cost);
        if (target != null) Set(power, GAME_TAG.CARD_TARGET, target.Id);
        History.Meta(HistoryMeta.Type.SLUSH_TIME, SlushHeroPower, power.Id);
        Effect(HistoryBlock.Type.POWER, power, target, () => HeroPowers.Use(this, p, target));
        Add(p.Player, GAME_TAG.HEROPOWER_ACTIVATIONS_THIS_TURN, 1);
        Add(p.Player, GAME_TAG.NUM_TIMES_HERO_POWER_USED_THIS_GAME, 1);
        Add(power, GAME_TAG.HEROPOWER_ACTIVATIONS_THIS_TURN, 1);
        Set(power, GAME_TAG.EXHAUSTED, 1);
        History.BlockEnd();
    }

    /// <summary>Freeze a character (live: FROZEN=1 at the end of the effect block, after its damage).</summary>
    public void Freeze(Entity target)
    {
        if (target.Zone == TAG_ZONE.PLAY && !target.Has(GAME_TAG.CANT_BE_FROZEN)) Set(target, GAME_TAG.FROZEN, 1);
    }

    // SLUSH_TIME per action as live sends it. The client does not wait on it (it only feeds its own timing stats);
    // kept for fidelity.
    private const int SlushMinionPlay = 3000, SlushSpellPlay = 3000, SlushHeroPower = 2000, SlushAttack = 1200;

    /// <summary>
    /// Runs one effect (spell, battlecry, hero power, trigger...) as a history block the way live shapes it: the block
    /// opens with a single META_DATA TARGET (Data 0) listing every character the effect acts on — its chosen target
    /// plus everything it damages, heals or freezes — before any of those changes. The client's effect FX map their
    /// targets to that entry (SuperSpell.AddTargetFromMetaData) and resolve what follows it at the moment of impact;
    /// without it the damage only shows once the whole effect has finished. Scripts never emit it themselves.
    /// </summary>
    private void Effect(HistoryBlock.Type type, Entity source, Entity target, Action body)
    {
        History.BlockStart(type, source.Id, target?.Id ?? 0, effectIndex: 0);
        var mark = History.Mark();
        body();
        var affected = new List<int>();
        if (target != null) affected.Add(target.Id);
        affected.AddRange(History.AffectedSince(mark).Where(id => !affected.Contains(id)));
        if (affected.Count > 0) History.InsertMeta(mark, HistoryMeta.Type.TARGET, 0, affected);
        History.BlockEnd();
    }

    // Unnamed in the client's GAME_TAG enum; roles inferred from official Power.log attack/damage sequences.
    private const int AttackLink = 1715;          // set on attacker and defender = attacker id
    private const int AttackerCounter = 4163;     // attacker, incremented per attack
    private const int DamageSource = 2187;        // defender side of a pre-damage pulse = damage source id
    private const int DamageTargetSelf = 1173;    // = target id during the pre-damage pulse
    private const int TimesDamaged = 4741;        // per character: how many times it has taken damage (live: 1st hit 1, 2nd hit 2...)
    private const int CombatDamageToggle = 1710;  // game entity, pulsed once per combat damage

    /// <summary>Mirrors Blizzard's ATTACK block tag-for-tag (see official practice log 2026-09-29).</summary>
    private void Attack(Entity attacker, Entity defender)
    {
        PlayerState owner = PlayerOf(attacker);
        History.BlockStart(HistoryBlock.Type.ATTACK, attacker.Id, defender.Id, effectIndex: 0);
        SetRaw(attacker, AttackLink, attacker.Id);
        SetRaw(defender, AttackLink, attacker.Id);
        if (attacker.Type == TAG_CARDTYPE.MINION) Add(owner.Player, GAME_TAG.NUM_FRIENDLY_MINIONS_THAT_ATTACKED_THIS_TURN, 1);
        Set(Game, GAME_TAG.PROPOSED_ATTACKER, attacker.Id);
        Set(Game, GAME_TAG.PROPOSED_DEFENDER, defender.Id);
        Set(attacker, GAME_TAG.ATTACKING, 1);
        History.Meta(HistoryMeta.Type.SLUSH_TIME, SlushAttack, attacker.Id);
        Set(defender, GAME_TAG.DEFENDING, 1);
        Add(attacker, GAME_TAG.NUM_ATTACKS_THIS_TURN, 1);
        SetRaw(attacker, AttackerCounter, attacker.Tags.GetValueOrDefault(AttackerCounter) + 1);
        int allowed = attacker.Has(GAME_TAG.WINDFURY) ? 2 : 1;
        if (attacker[GAME_TAG.NUM_ATTACKS_THIS_TURN] >= allowed) Set(attacker, GAME_TAG.EXHAUSTED, 1);
        if (attacker.Has(GAME_TAG.STEALTH)) Set(attacker, GAME_TAG.STEALTH, 0);

        int toDefender = attacker.Atk, toAttacker = defender.Atk;
        int pulses = 0;
        if (Damage(defender, toDefender, attacker)) pulses++;
        if (toAttacker > 0 && Damage(attacker, toAttacker, defender)) pulses++;
        for (int i = 0; i < pulses; i++)
        {
            SetRaw(Game, CombatDamageToggle, 1);
            SetRaw(Game, CombatDamageToggle, 0);
        }

        Set(Game, GAME_TAG.PROPOSED_ATTACKER, 0);
        Set(Game, GAME_TAG.PROPOSED_DEFENDER, 0);
        Set(attacker, GAME_TAG.ATTACKING, 0);
        Set(defender, GAME_TAG.DEFENDING, 0);
        History.BlockEnd();
    }

    // ---------- damage, healing, death ----------

    /// <summary>
    /// Deals damage with Blizzard's two-phase report: a pre-damage pulse carrying source/target, a second PREDAMAGE
    /// pulse, then META DAMAGE and the amount/DAMAGE tags. The client times the damage number off these.
    /// Returns false when nothing was dealt (divine shield, no damage).
    /// </summary>
    public bool Damage(Entity target, int amount, Entity source)
    {
        if (amount <= 0 || target.Zone != TAG_ZONE.PLAY) return false;
        if (target.Has(GAME_TAG.DIVINE_SHIELD))
        {
            Set(target, GAME_TAG.DIVINE_SHIELD, 0);
            return false;
        }
        SetRaw(target, DamageSource, source?.Id ?? 0);
        SetRaw(target, (int)GAME_TAG.PREDAMAGE, amount);
        SetRaw(target, DamageTargetSelf, target.Id);
        SetRaw(target, DamageSource, 0);
        SetRaw(target, (int)GAME_TAG.PREDAMAGE, 0);
        SetRaw(target, DamageTargetSelf, 0);
        SetRaw(target, (int)GAME_TAG.PREDAMAGE, amount);
        SetRaw(target, (int)GAME_TAG.PREDAMAGE, 0);
        History.Meta(HistoryMeta.Type.DAMAGE, amount, target.Id);
        SetRaw(target, TimesDamaged, target.Tags.GetValueOrDefault(TimesDamaged) + 1);
        int armor = target[GAME_TAG.ARMOR];
        int absorbed = Math.Min(armor, amount);
        if (absorbed > 0) Set(target, GAME_TAG.ARMOR, armor - absorbed);
        Set(target, GAME_TAG.LAST_AFFECTED_BY, source?.Id ?? 0);
        if (amount - absorbed > 0) Add(target, GAME_TAG.DAMAGE, amount - absorbed);
        if (source != null && source.Has(GAME_TAG.POISONOUS) && target.Type == TAG_CARDTYPE.MINION)
            Set(target, GAME_TAG.TO_BE_DESTROYED, 1);
        return true;
    }

    public void Heal(Entity target, int amount, Entity source)
    {
        int healed = Math.Min(amount, target[GAME_TAG.DAMAGE]);
        if (healed <= 0) return;
        History.Meta(HistoryMeta.Type.HEALING, healed, target.Id);
        Set(target, GAME_TAG.DAMAGE, target[GAME_TAG.DAMAGE] - healed);
    }

    public void GainArmor(Entity hero, int amount) => Add(hero, GAME_TAG.ARMOR, amount);

    public void DrawFor(PlayerState p, int count)
    {
        Add(p.Player, GAME_TAG.NUM_CARDS_DRAWN_THIS_TURN, count);
        DrawCards(p, count);
    }

    private void ProcessDeaths()
    {
        var dead = P1.Board.Concat(P2.Board)
            .Where(m => m.Health <= 0 || m.Has(GAME_TAG.TO_BE_DESTROYED)).ToList();
        if (dead.Count > 0)
        {
            History.BlockStart(HistoryBlock.Type.DEATHS, Game.Id);
            foreach (Entity m in dead)
            {
                PlayerState owner = PlayerOf(m);
                owner.Board.Remove(m);
                Set(m, GAME_TAG.ZONE_POSITION, 0);
                Set(m, GAME_TAG.ZONE, (int)TAG_ZONE.GRAVEYARD);
            }
            foreach (PlayerState p in new[] { P1, P2 })
                for (int i = 0; i < p.Board.Count; i++) Set(p.Board[i], GAME_TAG.ZONE_POSITION, i + 1);
            History.BlockEnd();
        }
        bool p1Dead = P1.Hero.Health <= 0, p2Dead = P2.Hero.Health <= 0;
        if (p1Dead || p2Dead) EndGame(p1Dead && p2Dead ? null : p1Dead ? P2 : P1);
    }

    /// <summary>
    /// A player concedes (client Concede, game packet 11; also sent by the in-game Restart button, which re-queues
    /// once the game is over). Sequence as on live: CONCEDED then LOST for the conceder, WON for the opponent,
    /// FINAL_WRAPUP, then FINAL_GAMEOVER and STATE COMPLETE — top level, outside any block.
    /// </summary>
    public void Concede(PlayerState p)
    {
        if (Over) return;
        Over = true;
        Winner = p.Opponent;
        Set(p.Player, GAME_TAG.PLAYSTATE, (int)TAG_PLAYSTATE.CONCEDED);
        Set(p.Player, GAME_TAG.PLAYSTATE, (int)TAG_PLAYSTATE.LOST);
        Set(p.Opponent.Player, GAME_TAG.PLAYSTATE, (int)TAG_PLAYSTATE.WON);
        Set(Game, GAME_TAG.NEXT_STEP, (int)TAG_STEP.FINAL_WRAPUP);
        Set(Game, GAME_TAG.STEP, (int)TAG_STEP.FINAL_WRAPUP);
        History.BlockStart(HistoryBlock.Type.TRIGGER, Game.Id);
        Set(Game, GAME_TAG.NEXT_STEP, (int)TAG_STEP.FINAL_GAMEOVER);
        History.BlockEnd();
        Set(Game, GAME_TAG.STEP, (int)TAG_STEP.FINAL_GAMEOVER);
        Set(Game, GAME_TAG.STATE, (int)TAG_STATE.COMPLETE);
    }

    private void EndGame(PlayerState winner)
    {
        if (Over) return;
        Over = true;
        Winner = winner;
        History.BlockStart(HistoryBlock.Type.TRIGGER, Game.Id);
        foreach (PlayerState p in new[] { P1, P2 })
        {
            var state = winner == null ? TAG_PLAYSTATE.TIED : p == winner ? TAG_PLAYSTATE.WON : TAG_PLAYSTATE.LOST;
            Set(p.Player, GAME_TAG.PLAYSTATE, (int)(winner == null ? TAG_PLAYSTATE.TIED : p == winner ? TAG_PLAYSTATE.WINNING : TAG_PLAYSTATE.LOSING));
            Set(p.Player, GAME_TAG.PLAYSTATE, (int)state);
        }
        Set(Game, GAME_TAG.STEP, (int)TAG_STEP.FINAL_GAMEOVER);
        Set(Game, GAME_TAG.STATE, (int)TAG_STATE.COMPLETE);
        History.BlockEnd();
    }
}
