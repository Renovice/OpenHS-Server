using HsServer.Engine;
using PegasusGame;
using PegasusShared;
using K = SabberStoneCore.Kettle;
using SabberStoneCore.Enums;

namespace HsServer.Sabber;

/// <summary>
/// Converts the rules engine's history (SabberStone "Kettle" entries, omniscient) into the client's power history as
/// one player (the viewer) may see it. A shadow copy of every entity, replayed in history order, decides visibility:
/// cards in the viewer's hand, and anything in play / graveyard / removed, are shown; the opponent's hand and both decks
/// stay hidden (FULL_ENTITY with zone/controller/id only), and a hidden card is revealed with SHOW_ENTITY — carrying its
/// tags as of that moment — when it moves somewhere the viewer can see.
/// </summary>
internal sealed class ClientStream(int viewer, Func<int, (ulong Hi, ulong Lo, int CardBack)> accountOf)
{
    private sealed class Shadow
    {
        public string CardId = "";
        public readonly Dictionary<int, int> Tags = new();
        public bool Known;
        public int this[GAME_TAG tag] => Tags.TryGetValue((int)tag, out int v) ? v : 0;
    }

    private readonly Dictionary<int, Shadow> shadow = new();
    // a block's recorded metas that go right after its first SUB_SPELL_START (Loader.BlockMeta)
    private readonly Dictionary<K.PowerHistoryBlockStart, List<SabberStoneCore.Loader.BlockMeta.Meta>> pendingSubMeta = new();
    // blocks whose TARGET waits for their own opening EFFECT_TIMING (live order: EFFECT_TIMING, then TARGET)
    private readonly Dictionary<K.PowerHistoryBlockStart, List<int>> pendingTarget = new();

    private static PowerHistoryData Meta(MetaDataType type, int source) => new()
    {
        MetaData = new PowerHistoryMetaData { Type = (HistoryMeta.Type)(int)type, Data = 0, Info = new List<int> { source }, AdditionalData = new List<int>() },
    };

    // Tags the client may see on an entity whose identity it doesn't know. (2026-10-09: not NUM_TURNS_IN_HAND / JUST_PLAYED — live
    // sends them only to cards the viewer knows: 0 of 42,667 in 402 live games went to a hidden card; `HsServer viewcheck --hidden`.
    // A hidden card's counters come with its reveal, in its SHOW_ENTITY.)
    private static readonly HashSet<int> PublicTags = new()
    {
        (int)GAME_TAG.ZONE, (int)GAME_TAG.ZONE_POSITION, (int)GAME_TAG.CONTROLLER, (int)GAME_TAG.ENTITY_ID,
        // live: the "Created by" of a card the opponent got (Pilfer, Psychic Conjurer): a TAG_CHANGE on the hidden card
        (int)GAME_TAG.DISPLAYED_CREATOR,
    };

    // the blocks open in what was converted so far (a block can span several conversions: a held pick)
    private readonly List<K.PowerHistoryBlockStart> openBlocks = new();

    public List<PowerHistoryData> Convert(IEnumerable<K.IPowerHistoryEntry> entries)
    {
        var output = new List<PowerHistoryData>();
        var list = entries as IList<K.IPowerHistoryEntry> ?? entries.ToList();
        HashSet<K.IPowerHistoryEntry>? folded = null;
        foreach (K.IPowerHistoryEntry entry in list)
        {
            if (folded != null && folded.Contains(entry)) continue;
            switch (entry)
            {
                case K.PowerHistoryCreateGame c:
                    // (2026-10-09) live: each player's HERO_ENTITY is one of its CREATE_GAME tags, not a later TAG_CHANGE — the
                    // Dalaran Heist's client code reads the boss's hero from it (GenericDungeonMissionEntity.GetOpposingHeroCardID:
                    // without it the client threw and the game never loaded; live-20261001-1624 Power.log)
                    folded = FoldPlayerHeroes(c, list);
                    output.Add(CreateGame(c));
                    break;
                case K.PowerHistoryFullEntity f:
                {
                    Shadow s = Replace(f.Entity);
                    s.Known = Visible(s);
                    output.Add(new PowerHistoryData { FullEntity = Entity(f.Entity.Id, s, s.Known, created: true) });
                    // Live (Bear Trap, Venomstrike Trap, Cat Trick, Netherwind Portal): a block recorded to name what it acts on
                    // (TARGETS) that acts on nothing else names the first minion it creates, right after it.
                    if (openBlocks.Count > 0 && openBlocks[^1] is var made && made.Created.Count > 0 && made.Created[0] == f.Entity.Id &&
                        made.Touched.Count == 0 && !made.TouchesSelf && BlockTargets(made).Count == 0 &&
                        SabberStoneCore.Loader.BlockMeta.TargetModeOf(Get(made.Source).CardId, made.BlockType, made.Index) ==
                        SabberStoneCore.Loader.BlockMeta.TargetMode.Touched)
                        output.Add(new PowerHistoryData
                        {
                            MetaData = new PowerHistoryMetaData
                            {
                                Type = HistoryMeta.Type.TARGET, Data = 0, Info = new List<int> { f.Entity.Id }, AdditionalData = new List<int>(),
                            },
                        });
                    break;
                }
                case K.PowerHistoryShowEntity show:
                {
                    // (2026-10-09) A card the viewer already knows is never revealed again: live sends no SHOW_ENTITY for a known
                    // card (0 in 402 live games, `HsServer viewcheck`) — a minion bounced from the board was re-sent "in play at
                    // position 0" and the client lost the board (Freezing Trap); a Discover pick was re-sent in SETASIDE before
                    // it moved to the hand (live session-20261007-113244 5504: the pick just moves). Only what changed is told.
                    if (Get(show.Entity.Id) is { Known: true } known && (string.IsNullOrEmpty(show.Entity.Name) || show.Entity.Name == known.CardId))
                    {
                        foreach (var (tag, value) in show.Entity.Tags)
                            if (known[(GAME_TAG)tag] != value)
                            {
                                known.Tags[(int)tag] = value;
                                if ((int)tag < K.PowerHistoryBuilder.InternalTagsFrom && !(OwnerOnlyTags.Contains((int)tag) && known[GAME_TAG.CONTROLLER] != viewer))
                                    output.Add(TagChange(show.Entity.Id, (int)tag, value));
                            }
                        break;
                    }
                    // What the client had before, for the public tags (a hidden card only shows its move).
                    var before = PublicTags.ToDictionary(tag => tag, tag => Get(show.Entity.Id)[(GAME_TAG)tag]);
                    Shadow s = Merge(show.Entity);
                    if (Visible(s))
                    {
                        s.Known = true;
                        output.Add(new PowerHistoryData { ShowEntity = Entity(show.Entity.Id, s, full: true) });
                    }
                    else
                    {
                        // Revealed to its owner only (e.g. the opponent drew it): the viewer just sees it move. (Its hand counters
                        // aren't public: a counter on the hidden deck card made the client's draw effect — Chillfallen Baron — read a
                        // card without a type and resync with a reconnect, local-test-20261003-dk-reconnect.)
                        foreach (int tag in PublicTags)
                            if (s[(GAME_TAG)tag] != before[tag])
                                output.Add(TagChange(show.Entity.Id, tag, s[(GAME_TAG)tag]));
                    }
                    break;
                }
                case K.PowerHistoryShuffleDeck shuffle:
                    output.Add(new PowerHistoryData { ShuffleDeck = new PowerHistoryShuffleDeck { PlayerId = shuffle.PlayerId } });
                    break;
                case K.PowerHistoryHideEntity hide:
                {
                    Shadow s = Get(hide.EntityID);
                    int zoneBefore = s[GAME_TAG.ZONE];
                    s.Tags[(int)GAME_TAG.ZONE] = (int)hide.Zone;
                    if (s.Known && !Visible(s))
                    {
                        s.Known = false;
                        output.Add(new PowerHistoryData { HideEntity = new PowerHistoryHide { Entity = hide.EntityID, Zone = (int)hide.Zone } });
                    }
                    else if (zoneBefore != (int)hide.Zone) // already hidden and already there: nothing to tell
                        output.Add(TagChange(hide.EntityID, (int)GAME_TAG.ZONE, (int)hide.Zone));
                    break;
                }
                case K.PowerHistoryTagChange t:
                    output.AddRange(TagChange(t));
                    break;
                case K.PowerHistoryBlockStart b:
                    openBlocks.Add(b);
                    // Live (enemy Snipe, live-20261004-night): a Secret the viewer can't see is revealed (SHOW_ENTITY) right
                    // BEFORE its TRIGGER block; else the client gets the trigger of an unknown card and plays none of its
                    // effects (Explosive Trap showed nothing, Freezing Trap's attack played through: local-test-20261005).
                    if (b.BlockType == SabberStoneCore.Enums.BlockType.TRIGGER && b.TriggerKeyword == (int)GAME_TAG.SECRET
                        && Get(b.Source) is { Known: false } secret && !string.IsNullOrEmpty(secret.CardId))
                    {
                        secret.Known = true;
                        output.Add(new PowerHistoryData { ShowEntity = Entity(b.Source, secret, full: true) });
                    }
                    output.Add(new PowerHistoryData
                    {
                        PowerStart = new PowerHistoryStart
                        {
                            Type = (HistoryBlock.Type)(int)b.BlockType, Source = b.Source, Target = b.Target,
                            EffectIndex = b.Index, EffectCardId = b.EffectCardId ?? "", SubOption = HidesChoice(b) ? -1 : b.SubOption,
                            TriggerKeyword = b.TriggerKeyword, ShowInHistory = b.ShowInHistory, IsDeferrable = b.IsDeferrable,
                        },
                    });
                    // Live: the card's own EFFECT_TIMING / OVERRIDE_HISTORY (recorded per card: Loader.BlockMeta; the
                    // effect kind doesn't predict them) come first in its block, or right after its first SUB_SPELL.
                    // A recorded TARGET names the card itself (live: Chillfallen Baron, Arcane Intellect: their FX read the
                    // target's card type, and without it the client takes the first entity whose tags change, the hidden
                    // drawn card: a dev fatal); not again when the engine's TARGET already names it.
                    // A recorded TARGETHERO names the source's controller's hero instead (live Defrost).
                    int ownHero = HeroOf(Get(b.Source)[GAME_TAG.CONTROLLER]);
                    var metas = SabberStoneCore.Loader.BlockMeta.Find(Get(b.Source).CardId, b.BlockType, b.Index)
                        .Where(m => m.Type != SabberStoneCore.Enums.MetaDataType.TARGET || b.EffectTargets?.Contains(m.OnHero ? ownHero : b.Source) != true).ToList();
                    // (2026-10-08: a HISTORY_TRIGGER_SOURCE of an enchantment's block names the card it came from — live JAIL_509e → JAIL_509)
                    int historySource = Get(b.Source)[GAME_TAG.CARDTYPE] == (int)TAG_CARDTYPE.ENCHANTMENT && Get(b.Source)[GAME_TAG.CREATOR] > 0
                        ? Get(b.Source)[GAME_TAG.CREATOR] : b.Source;
                    foreach (var meta in metas.Where(m => !m.AfterFirstSubSpell))
                        output.Add(Meta(meta.Type, meta.OnHero ? ownHero : meta.Type == SabberStoneCore.Enums.MetaDataType.HISTORY_TRIGGER_SOURCE ? historySource : b.Source));
                    // A card whose client effect is a SelectSpell plays the table entry its block's EFFECT_SELECTION names, and
                    // nothing without it (client SelectSpell.AttachPowerTaskList: Arcanosaur's explosion never showed, user
                    // 2026-10-09). Live: first in the block, Info = the card (The Countess, Mecha'thun, Omega Assembly).
                    if (b.BlockType is SabberStoneCore.Enums.BlockType.POWER or SabberStoneCore.Enums.BlockType.TRIGGER
                        && SabberStoneCore.Loader.SelectFx.SelectionOf(Get(b.Source).CardId, b) is int selection)
                        output.Add(new PowerHistoryData
                        {
                            MetaData = new PowerHistoryMetaData
                            {
                                Type = HistoryMeta.Type.EFFECT_SELECTION, Data = selection, Info = new List<int> { b.Source }, AdditionalData = new List<int>(),
                            },
                        });
                    // Unrecorded: a block that draws, whose client effect is a BuffImpact, names the card itself (rule from
                    // the recorded ones: Chillfallen Baron, Corpse Taker; Loader.BlockMeta.HasImpactFx) when nothing else
                    // is its TARGET — else the client's effect waits on the drawn card forever (Defrost).
                    if (b.Draws && b.Target == 0 && BlockTargets(b).Count == 0
                        && !SabberStoneCore.Loader.BlockMeta.IsRecorded(Get(b.Source).CardId, b.BlockType, b.Index)
                        && SabberStoneCore.Loader.BlockMeta.HasImpactFx(Get(b.Source).CardId, b.BlockType, b.Index))
                        output.Add(Meta(SabberStoneCore.Enums.MetaDataType.TARGET, b.Source));
                    // Unrecorded: a block whose client effect is an "…ImpactOnly" one (only the impact, on a target) and that names
                    // nothing itself (no chosen target, nothing damaged / healed / frozen) names what it acted on — a minion
                    // buffing itself (live TRL_071 with Lone Champion's very effect: TARGET = itself; user 2026-10-05: Lone
                    // Champion's Taunt + Divine Shield impact was missing). BuffImpact effects without a draw get none (live
                    // Demon Hunter Hero Power 8/8).
                    else if (!b.Draws && b.Target == 0 && (b.Touched.Count > 0 || b.TouchesSelf) && BlockTargets(b).Count == 0
                        && !SabberStoneCore.Loader.BlockMeta.IsRecorded(Get(b.Source).CardId, b.BlockType, b.Index)
                        && SabberStoneCore.Loader.BlockMeta.HasImpactOnlyFx(Get(b.Source).CardId, b.BlockType, b.Index))
                        output.Add(new PowerHistoryData
                        {
                            MetaData = new PowerHistoryMetaData
                            {
                                Type = HistoryMeta.Type.TARGET, Data = 0,
                                Info = (b.Touched.Count > 0 ? b.Touched.Distinct() : new[] { b.Source }).ToList(), AdditionalData = new List<int>(),
                            },
                        });
                    if (metas.Any(m => m.AfterFirstSubSpell))
                        pendingSubMeta[b] = metas.Where(m => m.AfterFirstSubSpell).ToList();
                    // Live: an effect block opens with one META_DATA TARGET naming what it lands on (the engine
                    // collects them); the client's effect FX land the damage on impact from it. With a SUB_SPELL it goes
                    // inside the first one instead (see below).
                    if (BlockTargets(b) is { Count: > 0 } waiting && !b.HasSubSpell && b.OpensWithEffectTiming)
                        pendingTarget[b] = waiting.ToList();
                    else if (BlockTargets(b) is { Count: > 0 } targets && !b.HasSubSpell)
                        output.Add(new PowerHistoryData
                        {
                            MetaData = new PowerHistoryMetaData
                            {
                                Type = HistoryMeta.Type.TARGET, Data = 0, Info = targets.ToList(), AdditionalData = new List<int>(),
                            },
                        });
                    break;
                case K.PowerHistoryBlockEnd:
                    output.Add(new PowerHistoryData { PowerEnd = new PowerHistoryEnd() });
                    if (openBlocks.Count > 0) openBlocks.RemoveAt(openBlocks.Count - 1);
                    break;
                case K.PowerHistorySubSpellStart sub:
                {
                    // Live: a block's first SUB_SPELL carries the block's TARGET metadata right after it (Fire Breath).
                    // (EFFECT_TIMING is the card's own, from Loader.BlockMeta — a guessed "before the first SUB_SPELL of an
                    // untargeted block" was wrong in ~85% of live blocks: engine audit 2026-10-02, D1.)
                    List<int> blockTargets = sub.Block == null ? new List<int>() : BlockTargets(sub.Block);
                    var start = new PowerHistorySubSpellStart { SpellPrefabGuid = sub.Prefab, SourceEntityId = sub.Source };
                    start.TargetEntityIds.AddRange(sub.Targets);
                    output.Add(new PowerHistoryData { SubSpellStart = start });
                    if (sub.First && sub.Block != null && pendingSubMeta.Remove(sub.Block, out var afterSub))
                        foreach (var meta in afterSub)
                            output.Add(Meta(meta.Type, sub.Block.Source));
                    if (sub.First && blockTargets is { Count: > 0 } inner)
                        output.Add(new PowerHistoryData
                        {
                            MetaData = new PowerHistoryMetaData
                            {
                                Type = HistoryMeta.Type.TARGET, Data = 0, Info = inner.ToList(), AdditionalData = new List<int>(),
                            },
                        });
                    break;
                }
                case K.PowerHistorySubSpellEnd:
                    output.Add(new PowerHistoryData { SubSpellEnd = new PowerHistorySubSpellEnd() });
                    break;
                case K.PowerHistoryTargetRound round:
                    // Live: one TARGET per repeat of a repeated effect (Mad Bomber's bombs, Arcane Missiles), right before
                    // it, even for the same target again: the client flies one projectile per TARGET. None when it hit nothing.
                    if (InPlay(round.Info) is { Count: > 0 } roundTargets)
                        output.Add(new PowerHistoryData
                        {
                            MetaData = new PowerHistoryMetaData
                            {
                                Type = HistoryMeta.Type.TARGET, Data = 0, Info = roundTargets.ToList(), AdditionalData = new List<int>(),
                            },
                        });
                    break;
                case K.PowerHistoryMetaData m:
                    // A card shown big (a spell an effect casts) is shown to both players, as live. Not a card that stays in a hidden
                    // zone (an opponent's hand or deck card whose Deathrattle is triggered: Call of the Grave; its REVEALED copy shows
                    // the effect): the opponent's hand isn't revealed (soak 300999: the AI's discovered Lotus Bookie leaked).
                    if (m.Type == MetaDataType.SHOW_BIG_CARD)
                        foreach (int id in m.Info ?? new List<int>())
                            // (not Get: live's SHOW_BIG_CARD Info also carries a number that is no entity — [card, 1000], live AT_016 /
                            // ETC_506 — and a shadow made for it went out as an entity in the reconnect snapshot: soak 7000089)
                            if (shadow.TryGetValue(id, out Shadow hidden) && !hidden.Known && hidden.CardId.Length > 0
                                && hidden[GAME_TAG.ZONE] != (int)Zone.HAND && hidden[GAME_TAG.ZONE] != (int)Zone.DECK)
                            {
                                hidden.Known = true;
                                output.Add(new PowerHistoryData { ShowEntity = Entity(id, hidden, full: true) });
                            }
                    output.Add(new PowerHistoryData
                    {
                        MetaData = new PowerHistoryMetaData
                        {
                            Type = (HistoryMeta.Type)(int)m.Type, Data = m.Data,
                            Info = m.Info?.ToList() ?? new List<int>(), AdditionalData = new List<int>(),
                        },
                    });
                    if (m.Type == MetaDataType.EFFECT_TIMING && openBlocks.Count > 0 && pendingTarget.Remove(openBlocks[^1], out var named))
                        output.Add(new PowerHistoryData
                        {
                            MetaData = new PowerHistoryMetaData { Type = HistoryMeta.Type.TARGET, Data = 0, Info = named, AdditionalData = new List<int>() },
                        });
                    break;
                case K.PowerHistoryChangeEntity ch:
                {
                    Shadow s = Merge(ch.Entity);
                    s.CardId = ch.CardId ?? ch.Entity.Name ?? s.CardId;
                    // live (13 in-hand transforms, a Forge): a CHANGE_ENTITY never repeats where the card is (ZONE,
                    // ZONE_POSITION, CONTROLLER, ENTITY_ID); sending them re-placed a forged card at once (no flight back
                    // from the deck)
                    if (s.Known) output.Add(new PowerHistoryData { ChangeEntity = Entity(ch.Entity.Id, s, full: true, placement: false) });
                    break;
                }
            }
        }
        return output;
    }

    /// <summary>The viewer's view of an entity's card id ("" when hidden).</summary>
    public string KnownCardId(int entityId) => shadow.TryGetValue(entityId, out Shadow s) && s.Known ? s.CardId : "";

    private IEnumerable<PowerHistoryData> TagChange(K.PowerHistoryTagChange t)
    {
        Shadow s = Get(t.EntityId);
        int tag = (int)t.Tag;
        if (tag >= K.PowerHistoryBuilder.InternalTagsFrom) { s.Tags[tag] = t.Value; yield break; } // (never sent: Sendable)
        int was = s[(GAME_TAG)tag];
        s.Tags[tag] = t.Value;
        // Where a card is, whether it is REVEALED, and whose it is (a hand card given to the other player) decide whether
        // the viewer sees it.
        bool visibility = tag == (int)GAME_TAG.ZONE || tag == (int)GAME_TAG.REVEALED || tag == (int)GAME_TAG.CONTROLLER;
        if (visibility && !s.Known && Visible(s))
        {
            s.Known = true;
            // Live (an opponent's enchantment: Cold Blood's CS2_073e2, Frostwolf Warlord's CS2_226e): an entity revealed as
            // it leaves SETASIDE is shown where it was, then moved (SHOW_ENTITY in SETASIDE, then TAG_CHANGE ZONE).
            if (tag == (int)GAME_TAG.ZONE && was == (int)TAG_ZONE.SETASIDE)
            {
                s.Tags[tag] = was;
                yield return new PowerHistoryData { ShowEntity = Entity(t.EntityId, s, full: true) };
                s.Tags[tag] = t.Value;
                yield return TagChange(t.EntityId, tag, t.Value);
                yield break;
            }
            yield return new PowerHistoryData { ShowEntity = Entity(t.EntityId, s, full: true) };
            yield break;
        }
        if (visibility && s.Known && !Visible(s))
        {
            s.Known = false;
            // a card given to the other player: its new owner is public (it moves to their hand), then it's hidden
            if (tag == (int)GAME_TAG.CONTROLLER) yield return TagChange(t.EntityId, tag, t.Value);
            yield return new PowerHistoryData { HideEntity = new PowerHistoryHide { Entity = t.EntityId, Zone = s[GAME_TAG.ZONE] } };
            // Live: a revealed card that goes hidden again (a traded card in the deck) is HIDE_ENTITY, then REVEALED=0.
            if (tag == (int)GAME_TAG.REVEALED) yield return TagChange(t.EntityId, tag, t.Value);
            yield break;
        }
        if (OwnerOnlyTags.Contains(tag) && s[GAME_TAG.CONTROLLER] != viewer) yield break; // the owner's secret (HIDDEN_CHOICE)
        // Live (heist-run-20260929 #2088, the boss's Secret): a hidden card going into the Secret zone first shows what kind
        // of Secret it is (SECRET / SIDE_QUEST...), its class and its cost — the client builds the coloured "?" from them;
        // without them it never finished placing it and stopped processing the game (2026-10-05, the AI's Secret).
        // (2026-10-08: live sends all eight, zeros too, in this order — 121 of 121 hidden cards entering the Secret zone, dk-classxp
        // 110225 10397; a Sidequest's SIDE_QUEST follows SECRET when set)
        if (!s.Known && tag == (int)GAME_TAG.ZONE && t.Value == (int)TAG_ZONE.SECRET)
            foreach (int shown in SecretShownTags)
                if (shown != (int)GAME_TAG.SIDE_QUEST || s[(GAME_TAG)shown] != 0)
                    yield return TagChange(t.EntityId, shown, shown is (int)GAME_TAG.ZONE_POSITION or (int)GAME_TAG.EXHAUSTED or (int)GAME_TAG.SECRET_LOCKED ? 0 : s[(GAME_TAG)shown]);
        if (s.Known || PublicTags.Contains(tag))
        {
            PowerHistoryData change = TagChange(t.EntityId, tag, t.Value);
            if (t.ChangeDef) change.TagChange.ChangeDef = true; // (live "DEF CHANGE")
            yield return change;
        }
    }

    // What the viewer learns of a hidden card played into the Secret zone (live order): its slot and readiness reset, its kind, class
    // and cost, and that it isn't locked. (2026-10-09: a Secret put into play from the deck — Mad Scientist — had only the engine's
    // creation tags: we sent CLASS=INVALID and no SECRET / COST, the client drew a broken "?" and its reveal played nothing. The
    // shadow now starts from the card's tags: Replace.)
    // what a hidden card in the Secret zone shows in its FULL_ENTITY (live: the non-zero ones of these)
    private static readonly HashSet<int> HiddenSecretTags = new()
    {
        (int)GAME_TAG.SECRET, (int)GAME_TAG.SIDE_QUEST, (int)GAME_TAG.CLASS, (int)GAME_TAG.MULTIPLE_CLASSES, (int)GAME_TAG.COST,
        (int)GAME_TAG.TAG_LAST_KNOWN_COST_IN_HAND,
    };

    private static readonly int[] SecretShownTags =
    {
        (int)GAME_TAG.ZONE_POSITION, (int)GAME_TAG.EXHAUSTED, (int)GAME_TAG.SECRET, (int)GAME_TAG.SIDE_QUEST, (int)GAME_TAG.CLASS,
        (int)GAME_TAG.MULTIPLE_CLASSES, (int)GAME_TAG.COST, (int)GAME_TAG.TAG_LAST_KNOWN_COST_IN_HAND, (int)GAME_TAG.SECRET_LOCKED,
    };


    // Tags only the entity's owner gets, even on a card both players see. HIDDEN_CHOICE: what the owner chose "(Secretly)" /
    // what only they may see (client HiddenChoiceCardTextBuilder picks the card text by it — Rhythm and Roots' ETC_387t "In 2
    // turns, summon three 5/5 Ancients"; its Objective banner reads 2 or 4 turns from it; HiddenEntityCardTextBuilder: "{0} is
    // inside! (Only you can see this.)"). The opponent's client keeps 0: the generic "Choose One (Secretly)" text.
    private static readonly HashSet<int> OwnerOnlyTags = new() { (int)GAME_TAG.HIDDEN_CHOICE };

    // A "(Secretly)" choice (client card tag 818: Rhythm and Roots, Fatespinner) keeps the chosen option from the opponent:
    // their client gets no SubOption on its blocks (and so plays no option's effect).
    private bool HidesChoice(K.PowerHistoryBlockStart b) =>
        b.SubOption >= 0 && Get(b.Source) is { } source && source[GAME_TAG.CONTROLLER] != viewer &&
        SabberStoneCore.Model.Cards.TryFromId(source.CardId) is { } card && card[SabberStoneCore.Enums.HsTag.SECRET_CHOICE] > 0;


    /// <summary>Live's TARGET metadata names characters on the board (or summoned during the block), never a card in hand
    /// or deck an effect targets (Zuramat's Prison) nor one that already left play (a Prison of Yogg-Saron target). The
    /// engine records only what an effect affects while it is in play (PowerHistory.InPlay), at the moment it does: a
    /// minion a Deathrattle pulls from the deck mid-block and then hit is named.</summary>
    private static List<int> InPlay(List<int> ids) => ids ?? new List<int>();

    // The block's TARGET: what the engine says it lands on (its chosen target, what it damages / heals / freezes), unless
    // the card's recording says otherwise (Loader.BlockMeta.TargetModeOf: live sends none for it — Rockbiter Weapon, Cold
    // Blood, Ice Lance — or names what it acts on — Deadly Shot, Counterspell, Defender of Argus).
    private List<int> BlockTargets(K.PowerHistoryBlockStart b)
    {
        var mode = SabberStoneCore.Loader.BlockMeta.TargetModeOf(Get(b.Source).CardId, b.BlockType, b.Index);
        if (mode == SabberStoneCore.Loader.BlockMeta.TargetMode.None || b.NamesItsTargets) return new List<int>();
        // Live (Explosive Shot, Howling Blast, Obliterate: 3/3): a SPLIT effect with a chosen target (the target treated
        // differently from the rest) names only it; the card's client effect does the rest, or a splash names its own in its
        // area effect (SplashFx). An equal effect (Cone of Cold: 1 to each) is one effect over all: all named (user 2026-10-05).
        if (b.BlockType == SabberStoneCore.Enums.BlockType.POWER && b.SplitTarget && b.EffectTargets?.Contains(b.Target) == true)
            return new List<int> { b.Target };
        List<int> targets = InPlay(b.EffectTargets);
        // (2026-10-09) not the hidden deck cards it acted on: live The Upper Hand names only its picked copy, not the deck cards
        // its offered copies stand for (a copy "acts on its original" — Mirror Entity's in play)
        if (targets.Count == 0 && mode == SabberStoneCore.Loader.BlockMeta.TargetMode.Touched)
            targets = b.Touched.Where(id => Get(id)[GAME_TAG.ZONE] != (int)TAG_ZONE.DECK).ToList();
        return targets;
    }

    private static PowerHistoryData TagChange(int entity, int tag, int value) =>
        new() { TagChange = new PowerHistoryTagChange { Entity = entity, Tag = tag, Value = value } };

    /// <summary>
    /// What may leave the server: never an engine-internal tag (&gt;= 900000: script bookkeeping and HsServer's rule tags). 2026-10-09:
    /// a Rewind's reset re-sent the players with their whole engine data (FULL_ENTITY tags 990211, 990084...; soak client view) — the
    /// stream is the last gate for every packet.
    /// </summary>
    private static IEnumerable<KeyValuePair<int, int>> Sendable(IEnumerable<KeyValuePair<int, int>> tags) =>
        tags.Where(t => t.Key < K.PowerHistoryBuilder.InternalTagsFrom);

    // the game entity and the players (player id, entity id) from CREATE_GAME, and the game's uuid: the reconnect
    // snapshot repeats them
    private int gameEntity;
    private readonly List<(int PlayerId, int EntityId)> playerEntities = new();
    private string gameUuid;

    /// <summary>The players' HERO_ENTITY changes in the same batch as CREATE_GAME, moved into their CREATE_GAME tags; the changes it took.</summary>
    private static HashSet<K.IPowerHistoryEntry> FoldPlayerHeroes(K.PowerHistoryCreateGame c, IList<K.IPowerHistoryEntry> entries)
    {
        var taken = new HashSet<K.IPowerHistoryEntry>();
        var players = c.Players.ToDictionary(p => p.PowerEntity.Id);
        foreach (K.IPowerHistoryEntry e in entries)
            if (e is K.PowerHistoryTagChange { Tag: SabberStoneCore.Enums.GameTag.HERO_ENTITY } t && players.TryGetValue(t.EntityId, out K.PowerPlayer? player) &&
                taken.All(x => ((K.PowerHistoryTagChange)x).EntityId != t.EntityId)) // (the first: the hero the game starts with)
            {
                player.PowerEntity.Tags[SabberStoneCore.Enums.GameTag.HERO_ENTITY] = t.Value;
                taken.Add(e);
            }
        return taken;
    }

    private PowerHistoryData CreateGame(K.PowerHistoryCreateGame c)
    {
        Shadow game = Get(c.Game.Id);
        given.Add(c.Game.Id);
        foreach (var (tag, value) in c.Game.Tags) game.Tags[(int)tag] = value;
        game.Known = true;
        gameEntity = c.Game.Id;
        gameUuid = Guid.NewGuid().ToString();
        playerEntities.Clear();
        foreach (K.PowerPlayer p in c.Players)
        {
            Shadow s = Get(p.PowerEntity.Id);
            foreach (var (tag, value) in p.PowerEntity.Tags) s.Tags[(int)tag] = value;
            s.Known = true;
            playerEntities.Add((p.PlayerId, p.PowerEntity.Id));
        }
        return CreateGameNow();
    }

    /// <summary>CREATE_GAME with the game and player entities as the shadows have them now.</summary>
    private PowerHistoryData CreateGameNow()
    {
        var players = new List<PegasusGame.Player>();
        foreach (var (playerId, entityId) in playerEntities)
        {
            var (hi, lo, cardBack) = accountOf(playerId);
            players.Add(new PegasusGame.Player
            {
                Id = playerId,
                GameAccountId = new BnetId { Hi = hi, Lo = lo },
                CardBack = cardBack,
                Entity = new PegasusGame.Entity { Id = entityId, Tags = History.Tags(Sendable(Get(entityId).Tags)), TagLists = new List<TagList>() },
            });
        }
        return new PowerHistoryData
        {
            CreateGame = new PowerHistoryCreateGame
            {
                GameEntity = new PegasusGame.Entity { Id = gameEntity, Tags = History.Tags(Sendable(Get(gameEntity).Tags)), TagLists = new List<TagList>() },
                Players = players, PlayerInfos = new List<SharedPlayerInfo>(), ActionInfos = new List<ActionInfo>(),
                GameUuid = gameUuid,
            },
        };
    }

    /// <summary>
    /// The game as the viewer may see it now, for a client that reconnects (A4). Live (live-20261001-startofgame, a
    /// reconnect from login): one PowerHistory of CREATE_GAME, then a FULL_ENTITY of every other entity in id order with
    /// its current tags — hidden ones (both decks, the opponent's hand) with only ZONE / CONTROLLER / ENTITY_ID and a hand
    /// card's ZONE_POSITION — and no blocks or tag changes.
    /// </summary>
    public List<PowerHistoryData> Snapshot()
    {
        var output = new List<PowerHistoryData> { CreateGameNow() };
        var skip = playerEntities.Select(p => p.EntityId).Append(gameEntity).ToHashSet();
        foreach (var (id, s) in shadow.OrderBy(e => e.Key))
            if (!skip.Contains(id) && given.Contains(id)) // (only what the client was given: a shadow is also made for any id a block names)
            {
                output.Add(new PowerHistoryData { FullEntity = Entity(id, s, s.Known) });
                // (2026-10-09) a hidden card outside the hand reaches the client with no position (live's snapshot): the shadow
                // follows, else its return to the hand sent no ZONE_POSITION — the engine's stale set-aside position matched the
                // shadow's (soak HS_SOAK_RESUME client view: TIME_706's set-aside hand came back at position 0)
                if (!s.Known && s[GAME_TAG.ZONE] != (int)TAG_ZONE.HAND) s.Tags.Remove((int)GAME_TAG.ZONE_POSITION);
            }
        return output;
    }

    private PowerHistoryEntity Entity(int id, Shadow s, bool full, bool placement = true, bool created = false)
    {
        IEnumerable<KeyValuePair<int, int>> tags = full
            ? s.Tags.Where(t => placement || t.Key is not ((int)GAME_TAG.ZONE or (int)GAME_TAG.ZONE_POSITION or (int)GAME_TAG.CONTROLLER or (int)GAME_TAG.ENTITY_ID))
            : s.Tags.Where(t => t.Key is (int)GAME_TAG.ZONE or (int)GAME_TAG.CONTROLLER or (int)GAME_TAG.ENTITY_ID
                || t.Key == (int)GAME_TAG.ZONE_POSITION && s[GAME_TAG.ZONE] == (int)TAG_ZONE.HAND
                // (2026-10-09) a hidden Secret keeps its coloured "?" in a FULL_ENTITY too (a reconnect's snapshot): live sends its kind,
                // class and cost (32 hidden FULL_ENTITY in the Secret zone, session-20260930-071519 51309: SECRET, CLASS, COST,
                // TAG_LAST_KNOWN_COST_IN_HAND); ours gave a class-less "?" after a reconnect (soak HS_SOAK_RESUME client view)
                || s[GAME_TAG.ZONE] == (int)TAG_ZONE.SECRET && HiddenSecretTags.Contains(t.Key) && t.Value != 0);
        // live: a new entity's FULL_ENTITY lists no 0 tags (the client starts every tag at 0); ours listed ~20 (BATTLECRY 0,
        // CHARGE 0... on every summoned token)
        if (created) tags = tags.Where(t => t.Value != 0);
        if (s[GAME_TAG.CONTROLLER] != viewer) tags = tags.Where(t => !OwnerOnlyTags.Contains(t.Key));
        var lists = new List<TagList>();
        // live: a card with an on-play replacement names the card it leaves in its hand slot (client tag list 3677,
        // EntityBase.GetReplacementsWhenPlayed: Twinspell's pink TWINSPELL_SUMMON_IN, a Drink spell's next drink...)
        if (full && SabberStoneCore.Model.Cards.OnPlayReplacement(SabberStoneCore.Model.Cards.TryFromId(s.CardId)) is { } copy)
            lists.Add(new TagList { Name = (int)GAME_TAG.CLIENT_LIST_REPLACEMENTS_WHEN_PLAYED, Values = new List<int> { copy.AssetId } });
        return new PowerHistoryEntity
        {
            Entity = id, Name = full ? s.CardId : "", Tags = History.Tags(Sendable(tags)),
            DefTags = new List<Tag>(), TagLists = lists, DefTagLists = new List<TagList>(),
        };
    }

    // In play, graveyard or removed: public. Hand, Secrets and set-aside (choice options): owner only. Deck: nobody. The rest of
    // the Secret zone is public: Quests, Sidequests, Paladin Auras, Objectives (live: the opponent's TLC_817 Quest, TIME_700 /
    // JAIL_327 Paladin Auras shown with SHOW_ENTITY as they are played; wiki Sidequest: "Sidequests can be seen by the opponent
    // when they are hovered over"; user 2026-10-07: Rhythm and Roots sits in "the same slots as secrets and/or auras ... like
    // Wildpaw Cavern"). Only a Secret (SECRET) stays a "?".
    private bool Visible(Shadow s)
    {
        // (2026-10-09) nothing to show without a card: the engine leaves an enchantment on a hidden card unnamed (Enchantment.GetInstance:
        // no reveal for a deck card's) and it stays hidden in PLAY — live too (64 hidden FULL_ENTITY in PLAY, heist-session-20260929-122856:
        // the run deck's enchantments); ours revealed it with an empty card id when it entered PLAY (soak client view)
        if (s.CardId.Length == 0) return false;
        var zone = (TAG_ZONE)s[GAME_TAG.ZONE];
        bool mine = s[GAME_TAG.CONTROLLER] == viewer;
        if (s[GAME_TAG.REVEALED] > 0 && zone != TAG_ZONE.INVALID) return true; // shown to both players (a traded card)
        return zone switch
        {
            TAG_ZONE.PLAY or TAG_ZONE.GRAVEYARD or TAG_ZONE.REMOVEDFROMGAME => true,
            TAG_ZONE.SECRET => mine || !IsSecret(s),
            TAG_ZONE.HAND or TAG_ZONE.SETASIDE => mine,
            TAG_ZONE.DECK => false,
            _ => mine,
        };
    }

    // A Secret by its tags, or by its card: a deck card's FULL_ENTITY carries only its zone tags (Entity.FromCard), so a Secret
    // put into play straight from the deck (Mysterious Challenger) may not have SECRET in its shadow yet.
    private static bool IsSecret(Shadow s) =>
        s[GAME_TAG.SECRET] > 0 || SabberStoneCore.Model.Cards.TryFromId(s.CardId) is { } card && card[SabberStoneCore.Enums.GameTag.SECRET] > 0;

    /// <summary>The hero entity of the player with <paramref name="playerId"/> (its player entity's HERO_ENTITY).</summary>
    private int HeroOf(int playerId) =>
        shadow.Values.FirstOrDefault(s => s[GAME_TAG.PLAYER_ID] == playerId && s[GAME_TAG.HERO_ENTITY] > 0)?[GAME_TAG.HERO_ENTITY] ?? 0;

    private Shadow Get(int id)
    {
        if (!shadow.TryGetValue(id, out Shadow s)) shadow[id] = s = new Shadow();
        return s;
    }

    /// <summary>
    /// A new entity: its card's own tags, then the entity's (2026-10-09). The engine's FULL_ENTITY of a deck card (Entity.FromCard)
    /// and of an enchantment lists only the entity's own tags, so a card later revealed from the deck reached the client without
    /// its CARDTYPE / CLASS / RARITY / COST (Freezing Trap put into play by Mad Scientist: a class-less "?", then a reveal with no
    /// type), an enchantment without its RARITY. Live's FULL_ENTITY / SHOW_ENTITY carry the card's tags (an enchantment's too:
    /// CS2_122e with CARDTYPE, CLASS, 323, 324...); a hidden entity still only shows ZONE / CONTROLLER / ENTITY_ID (<see cref="Entity"/>).
    /// </summary>
    // the entities given to the client (FULL_ENTITY / CREATE_GAME)
    private readonly HashSet<int> given = new();

    private Shadow Replace(K.PowerHistoryEntity e)
    {
        given.Add(e.Id);
        var s = new Shadow { CardId = e.Name ?? "" };
        if (SabberStoneCore.Model.Cards.TryFromId(s.CardId) is { } card)
            foreach (var (tag, value) in card.Tags)
                if ((int)tag < K.PowerHistoryBuilder.InternalTagsFrom && value != 0) s.Tags[(int)tag] = value;
        foreach (var (tag, value) in e.Tags) s.Tags[(int)tag] = value;
        s.Tags[(int)GAME_TAG.ENTITY_ID] = e.Id;
        shadow[e.Id] = s;
        return s;
    }

    private Shadow Merge(K.PowerHistoryEntity e)
    {
        Shadow s = Get(e.Id);
        // a card made hidden and named only now (an enchantment: FULL_ENTITY in SETASIDE, then SHOW_ENTITY): its card's tags are its
        // base from here, as for a new entity (Replace; live: an enchantment's RARITY / CLASS are its card's, 110,937 of 110,937)
        if (s.CardId.Length == 0 && !string.IsNullOrEmpty(e.Name) && SabberStoneCore.Model.Cards.TryFromId(e.Name) is { } card)
            foreach (var (tag, value) in card.Tags)
                if ((int)tag < K.PowerHistoryBuilder.InternalTagsFrom && value != 0) s.Tags.TryAdd((int)tag, value);
        if (!string.IsNullOrEmpty(e.Name)) s.CardId = e.Name;
        foreach (var (tag, value) in e.Tags) s.Tags[(int)tag] = value;
        s.Tags[(int)GAME_TAG.ENTITY_ID] = e.Id;
        return s;
    }
}
