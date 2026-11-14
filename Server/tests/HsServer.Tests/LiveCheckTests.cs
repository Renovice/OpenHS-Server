using System.Text;
using HsServer.Engine;
using HsServer.Protocol;
using HsServer.Sabber;
using HsServer.Tools;
using PegasusGame;
using GameTag = SabberStoneCore.Enums.GameTag;

namespace HsServer.Tests;

/// <summary>`HsServer livecheck` (2026-10-09): a recorded live action is read with the position before and after it, rebuilt in our
/// engine, done again, and the outcomes compared. A small recording made here: Jaina casts Fireball at the opponent's Chillwind Yeti.</summary>
public class LiveCheckTests
{
    private static readonly Lazy<bool> Cards = new(() =>
    {
        SabberEngineGame.EnsureCards(CardDb.FindDefaultDir(TestEnvironment.ProjectRoot));
        return true;
    });

    private const int Game = 1, Me = 2, Them = 3, MyHero = 4, MyPower = 5, TheirHero = 6, TheirPower = 7, Fireball = 8, Yeti = 9;

    private static Tag T(GameTag tag, int value) => new() { Name = (int)tag, Value = value };

    private static PowerHistoryData Full(int id, string card, params Tag[] tags) => new()
    {
        FullEntity = new PowerHistoryEntity
        {
            Entity = id, Name = card, Tags = tags.Append(T(GameTag.ENTITY_ID, id)).ToList(),
            DefTags = new List<Tag>(), TagLists = new List<TagList>(), DefTagLists = new List<TagList>(),
        },
    };

    private static PowerHistoryData Change(int entity, GameTag tag, int value) =>
        new() { TagChange = new PowerHistoryTagChange { Entity = entity, Tag = (int)tag, Value = value } };

    private static PowerHistoryData Start(HistoryBlock.Type type, int source, int target) =>
        new() { PowerStart = new PowerHistoryStart { Type = type, Source = source, Target = target, EffectCardId = "", SubOption = -1 } };

    private static PowerHistoryData End() => new() { PowerEnd = new PowerHistoryEnd() };

    /// <summary>The recording: the position (turn 3, 5 Mana each), then the Fireball and the deaths; <paramref name="yetiDamage"/> is what
    /// the recording says the Yeti took (6 kills it: what the rules say).</summary>
    private static string Recording(int yetiDamage)
    {
        int zone(SabberStoneCore.Enums.Zone z) => (int)z;
        var position = Position(
            Full(Fireball, "CS2_029", T(GameTag.CONTROLLER, 1), T(GameTag.CARDTYPE, 5), T(GameTag.ZONE, zone(SabberStoneCore.Enums.Zone.HAND)),
                T(GameTag.ZONE_POSITION, 1), T(GameTag.COST, 4)),
            Full(Yeti, "CS2_182", T(GameTag.CONTROLLER, 2), T(GameTag.CARDTYPE, 4), T(GameTag.ZONE, zone(SabberStoneCore.Enums.Zone.PLAY)),
                T(GameTag.ZONE_POSITION, 1), T(GameTag.ATK, 4), T(GameTag.HEALTH, 5), T(GameTag.COST, 4), T(GameTag.NUM_TURNS_IN_PLAY, 1)));
        var action = new List<PowerHistoryData>
        {
            Start(HistoryBlock.Type.PLAY, Fireball, Yeti),
            Change(Me, GameTag.RESOURCES_USED, 4),
            Change(Fireball, GameTag.ZONE, zone(SabberStoneCore.Enums.Zone.PLAY)),
            Start(HistoryBlock.Type.POWER, Fireball, Yeti),
            Change(Yeti, GameTag.DAMAGE, yetiDamage),
            End(),
            Change(Fireball, GameTag.ZONE, zone(SabberStoneCore.Enums.Zone.GRAVEYARD)),
            End(),
        };
        if (yetiDamage >= 5)
            action.AddRange(new[]
            {
                Start(HistoryBlock.Type.DEATHS, Game, 0), Change(Yeti, GameTag.ZONE, zone(SabberStoneCore.Enums.Zone.GRAVEYARD)),
                Change(Them, GameTag.CORPSES, 1), End(), // (live counts a Corpse for every player's dead minion)
            });
        action.Add(Change(Game, GameTag.STEP, 11)); // (the turn ends: the action is over)
        return Write(position, action);
    }

    /// <summary>A position: the game (turn 3, 5 Mana each, the first player's turn), the heroes and Hero Powers, a hidden deck card each,
    /// then <paramref name="cards"/>.</summary>
    private static List<PowerHistoryData> Position(params PowerHistoryData[] cards)
    {
        int zone(SabberStoneCore.Enums.Zone z) => (int)z;
        var create = new PowerHistoryCreateGame
        {
            GameEntity = new PegasusGame.Entity
            {
                Id = Game, Tags = new List<Tag> { T(GameTag.CARDTYPE, 1), T(GameTag.TURN, 3), T(GameTag.STEP, 10), T(GameTag.ENTITY_ID, Game) },
                TagLists = new List<TagList>(),
            },
            Players = new List<PegasusGame.Player>(), PlayerInfos = new List<SharedPlayerInfo>(), ActionInfos = new List<ActionInfo>(),
        };
        foreach (var (entity, player, hero, current) in new[] { (Me, 1, MyHero, 1), (Them, 2, TheirHero, 0) })
            create.Players.Add(new PegasusGame.Player
            {
                Id = player, GameAccountId = new PegasusShared.BnetId(),
                Entity = new PegasusGame.Entity
                {
                    Id = entity, TagLists = new List<TagList>(),
                    Tags = new List<Tag>
                    {
                        T(GameTag.CARDTYPE, 2), T(GameTag.PLAYER_ID, player), T(GameTag.CONTROLLER, player), T(GameTag.HERO_ENTITY, hero),
                        T(GameTag.CURRENT_PLAYER, current), T(GameTag.RESOURCES, 5), T(GameTag.MAXHANDSIZE, 10), T(GameTag.ENTITY_ID, entity),
                    },
                },
            });
        var position = new List<PowerHistoryData>
        {
            new() { CreateGame = create },
            Full(MyHero, "HERO_08", T(GameTag.CONTROLLER, 1), T(GameTag.CARDTYPE, 3), T(GameTag.ZONE, zone(SabberStoneCore.Enums.Zone.PLAY)), T(GameTag.HEALTH, 30)),
            Full(MyPower, "HERO_08bp", T(GameTag.CONTROLLER, 1), T(GameTag.CARDTYPE, 10), T(GameTag.ZONE, zone(SabberStoneCore.Enums.Zone.PLAY)), T(GameTag.COST, 2)),
            Full(TheirHero, "HERO_01", T(GameTag.CONTROLLER, 2), T(GameTag.CARDTYPE, 3), T(GameTag.ZONE, zone(SabberStoneCore.Enums.Zone.PLAY)), T(GameTag.HEALTH, 30)),
            Full(TheirPower, "HERO_01bp", T(GameTag.CONTROLLER, 2), T(GameTag.CARDTYPE, 10), T(GameTag.ZONE, zone(SabberStoneCore.Enums.Zone.PLAY)), T(GameTag.COST, 2)),
            Full(10, "", T(GameTag.CONTROLLER, 1), T(GameTag.ZONE, zone(SabberStoneCore.Enums.Zone.DECK))),
            Full(11, "", T(GameTag.CONTROLLER, 2), T(GameTag.ZONE, zone(SabberStoneCore.Enums.Zone.DECK))),
        };
        position.AddRange(cards);
        return position;
    }

    /// <summary>A recording file: each list one PowerHistory packet.</summary>
    private static string Write(params List<PowerHistoryData>[] packets)
    {
        int historyId = PegasusPackets.IdOf<PowerHistory>();
        var sb = new StringBuilder();
        int seq = 1;
        foreach (var list in packets)
        {
            byte[] body = PegasusPackets.Serialize(new PowerHistory { List = list });
            sb.AppendLine($"{{\"seq\":{seq++},\"t\":\"2026-10-09T12:00:{seq:00}Z\",\"ch\":\"game\",\"dir\":\"in\",\"type\":{historyId},\"b64\":\"{Convert.ToBase64String(body)}\"}}");
        }
        string file = Path.Combine(Path.GetTempPath(), $"session-livecheck-test-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(file, sb.ToString());
        return file;
    }

    private static List<LiveCheck.LiveAction> Actions(string file)
    {
        var actions = new List<LiveCheck.LiveAction>();
        LiveCheck.ReadActions(file, actions.Add);
        return actions;
    }

    [Fact]
    public void A_recorded_action_is_read_with_the_position_before_and_after_it()
    {
        string file = Recording(yetiDamage: 6);
        try
        {
            LiveCheck.LiveAction a = Assert.Single(Actions(file));
            Assert.Equal(Fireball, a.Source);
            Assert.Equal(Yeti, a.Target);
            Assert.Equal("CS2_029", a.CardOf(a.Source));
            Assert.Equal((int)SabberStoneCore.Enums.Zone.HAND, a.Before[Fireball][GameTag.ZONE]);
            Assert.Equal((int)SabberStoneCore.Enums.Zone.GRAVEYARD, a.After[Yeti][GameTag.ZONE]); // (the deaths belong to the action)
            Assert.Equal(6, a.After[Yeti][GameTag.DAMAGE]);
            Assert.True(a.FirstGenerated > Yeti); // (all made with the game: they started in it)

            // the line is the BLOCK_START's in `HsServer power <session>`
            var text = new StringWriter();
            TextWriter stdout = Console.Out;
            Console.SetOut(text);
            try { PowerDump.Run(new[] { file }); }
            finally { Console.SetOut(stdout); }
            string[] lines = text.ToString().Split('\n');
            Assert.StartsWith("BLOCK_START BlockType=PLAY", lines[a.Line - 1]);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void An_action_our_engine_does_as_live_did_matches()
    {
        _ = Cards.Value;
        string file = Recording(yetiDamage: 6);
        try
        {
            LiveCheck.Result r = LiveCheck.Check(Assert.Single(Actions(file)));
            Assert.True(r.Status == "MATCH", $"{r.Status}: {r.Detail} {r.Notes}");
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void An_action_with_another_outcome_live_differs_with_the_first_difference()
    {
        _ = Cards.Value;
        string file = Recording(yetiDamage: 3); // (a recording where Fireball dealt 3: our engine deals 6 and the Yeti dies)
        try
        {
            LiveCheck.Result r = LiveCheck.Check(Assert.Single(Actions(file)));
            Assert.Equal("DIFFERS", r.Status);
            Assert.Contains("CS2_182#9", r.Detail);
            Assert.Contains("zone live unchanged, ours GRAVEYARD", r.Detail);
        }
        finally { File.Delete(file); }
    }

    // ---- the tool pass (2026-10-10): false differences the rebuild made, and real ones it must keep

    private const int Golem = 12, Wisp = 13, MugsMagic = 14, Discount = 15, Raptor = 16;

    private static int Z(SabberStoneCore.Enums.Zone z) => (int)z;

    [Fact]
    public void A_discount_the_rebuild_makes_again_ends_as_live_s_did()
    {
        // Mug's Magic (JAIL_800hp1e on the player: "your first minion each turn costs (2) less"): the War Golem shows 5; playing the
        // Wisp ends the discount and the Golem is back to 7 — the rebuild must not fold the 5 into the Golem's own cost
        _ = Cards.Value;
        string file = Write(Position(
                Full(MugsMagic, "JAIL_800hp1", T(GameTag.CONTROLLER, 1), T(GameTag.CARDTYPE, 10), T(GameTag.ZONE, Z(SabberStoneCore.Enums.Zone.SETASIDE))),
                Full(Discount, "JAIL_800hp1e", T(GameTag.CONTROLLER, 1), T(GameTag.CARDTYPE, 6), T(GameTag.ZONE, Z(SabberStoneCore.Enums.Zone.PLAY)),
                    T(GameTag.ATTACHED, Me), T(GameTag.CREATOR, MugsMagic)),
                Full(Golem, "CS2_186", T(GameTag.CONTROLLER, 1), T(GameTag.CARDTYPE, 4), T(GameTag.ZONE, Z(SabberStoneCore.Enums.Zone.HAND)),
                    T(GameTag.ZONE_POSITION, 1), T(GameTag.ATK, 7), T(GameTag.HEALTH, 7), T(GameTag.COST, 5)),
                Full(Wisp, "CS2_231", T(GameTag.CONTROLLER, 1), T(GameTag.CARDTYPE, 4), T(GameTag.ZONE, Z(SabberStoneCore.Enums.Zone.HAND)),
                    T(GameTag.ZONE_POSITION, 2), T(GameTag.ATK, 1), T(GameTag.HEALTH, 1), T(GameTag.COST, 0))),
            new List<PowerHistoryData>
            {
                Start(HistoryBlock.Type.PLAY, Wisp, 0),
                Change(Wisp, GameTag.ZONE, Z(SabberStoneCore.Enums.Zone.PLAY)),
                Change(Wisp, GameTag.ZONE_POSITION, 1),
                Start(HistoryBlock.Type.TRIGGER, Discount, 0),
                Change(Discount, GameTag.ZONE, Z(SabberStoneCore.Enums.Zone.GRAVEYARD)),
                End(),
                Change(Golem, GameTag.COST, 7),
                End(),
                Change(Game, GameTag.STEP, 11),
            });
        try
        {
            LiveCheck.Result r = LiveCheck.Check(Assert.Single(Actions(file)));
            Assert.True(r.Status == "MATCH", $"{r.Status}: {r.Detail} {r.Notes}");
        }
        finally { File.Delete(file); }
    }

    /// <summary>The Raptor (Rush) attacks the Yeti; live marks it EXHAUSTED and ATTACKABLE_BY_RUSH (and keeps the mark later on).</summary>
    private static string RushAttack(int turnsInPlay, int target)
    {
        var position = Position(
            Full(Raptor, "CS2_120", T(GameTag.CONTROLLER, 1), T(GameTag.CARDTYPE, 4), T(GameTag.ZONE, Z(SabberStoneCore.Enums.Zone.PLAY)),
                T(GameTag.ZONE_POSITION, 1), T(GameTag.ATK, 2), T(GameTag.HEALTH, 3), T(GameTag.RUSH, 1), T(GameTag.ATTACKABLE_BY_RUSH, 1),
                T(GameTag.EXHAUSTED, turnsInPlay == 0 ? 1 : 0), T(GameTag.NUM_TURNS_IN_PLAY, turnsInPlay)),
            Full(Yeti, "CS2_182", T(GameTag.CONTROLLER, 2), T(GameTag.CARDTYPE, 4), T(GameTag.ZONE, Z(SabberStoneCore.Enums.Zone.PLAY)),
                T(GameTag.ZONE_POSITION, 1), T(GameTag.ATK, 4), T(GameTag.HEALTH, 5), T(GameTag.NUM_TURNS_IN_PLAY, 1)));
        var attack = new List<PowerHistoryData> { Start(HistoryBlock.Type.ATTACK, Raptor, target) };
        if (target == Yeti)
            attack.AddRange(new[] { Change(Yeti, GameTag.DAMAGE, 2), Change(Raptor, GameTag.DAMAGE, 3) });
        else
            attack.Add(Change(TheirHero, GameTag.DAMAGE, 2));
        attack.Add(Change(Raptor, GameTag.NUM_ATTACKS_THIS_TURN, 1));
        attack.Add(Change(Raptor, GameTag.EXHAUSTED, 1));
        attack.Add(End());
        if (target == Yeti)
            attack.AddRange(new[]
            {
                Start(HistoryBlock.Type.DEATHS, Game, 0), Change(Raptor, GameTag.ZONE, Z(SabberStoneCore.Enums.Zone.GRAVEYARD)),
                Change(Me, GameTag.CORPSES, 1), End(),
            });
        attack.Add(Change(Game, GameTag.STEP, 11));
        return Write(position, attack);
    }

    [Fact]
    public void A_rush_minion_played_this_turn_attacks_a_minion_as_live()
    {
        _ = Cards.Value;
        string file = RushAttack(turnsInPlay: 0, target: Yeti);
        try
        {
            LiveCheck.Result r = LiveCheck.Check(Assert.Single(Actions(file)));
            Assert.True(r.Status == "MATCH", $"{r.Status}: {r.Detail} {r.Notes}");
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void An_old_rush_mark_does_not_stop_a_later_attack_on_the_hero()
    {
        _ = Cards.Value;
        string file = RushAttack(turnsInPlay: 2, target: TheirHero);
        try
        {
            LiveCheck.Result r = LiveCheck.Check(Assert.Single(Actions(file)));
            Assert.True(r.Status == "MATCH", $"{r.Status}: {r.Detail} {r.Notes}");
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void A_rewind_drops_the_discarded_timeline_s_entities()
    {
        // a GAME_RESET block sends every entity of the rewound game again; one above the highest re-sent (made in the discarded timeline)
        // is gone from the next action's position
        _ = Cards.Value;
        var position = Position(
            Full(Fireball, "CS2_029", T(GameTag.CONTROLLER, 1), T(GameTag.CARDTYPE, 5), T(GameTag.ZONE, Z(SabberStoneCore.Enums.Zone.HAND)),
                T(GameTag.ZONE_POSITION, 1), T(GameTag.COST, 4)));
        var played = new List<PowerHistoryData>
        {
            Start(HistoryBlock.Type.PLAY, Fireball, TheirHero),
            Full(20, "CS2_231", T(GameTag.CONTROLLER, 1), T(GameTag.CARDTYPE, 4), T(GameTag.ZONE, Z(SabberStoneCore.Enums.Zone.PLAY))),
            End(),
            Start(HistoryBlock.Type.GAME_RESET, Fireball, 0),
            Full(MyHero, "HERO_08", T(GameTag.CONTROLLER, 1), T(GameTag.CARDTYPE, 3), T(GameTag.ZONE, Z(SabberStoneCore.Enums.Zone.PLAY)), T(GameTag.HEALTH, 30)),
            Full(Fireball, "CS2_029", T(GameTag.CONTROLLER, 1), T(GameTag.CARDTYPE, 5), T(GameTag.ZONE, Z(SabberStoneCore.Enums.Zone.HAND)),
                T(GameTag.ZONE_POSITION, 1), T(GameTag.COST, 4)),
            Full(11, "", T(GameTag.CONTROLLER, 2), T(GameTag.ZONE, Z(SabberStoneCore.Enums.Zone.DECK))),
            End(),
            Start(HistoryBlock.Type.PLAY, Fireball, TheirHero),
            Change(TheirHero, GameTag.DAMAGE, 6),
            End(),
            Change(Game, GameTag.STEP, 11),
        };
        string file = Write(position, played);
        try
        {
            List<LiveCheck.LiveAction> actions = Actions(file);
            Assert.Equal(2, actions.Count);
            Assert.True(actions[0].After.ContainsKey(20)); // (the rewound play's outcome: before the reset)
            Assert.False(actions[1].Before.ContainsKey(20)); // (the replay's position: the reset dropped it)
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Cards_with_no_script_and_engine_counters_are_told_apart()
    {
        _ = Cards.Value;
        // an adventure Hero Power with text and no script; a scripted card
        Assert.True(LiveCheck.Unscripted(SabberStoneCore.Model.Cards.FromId("ULDA_BOSS_29p"))); // (ICCA06_002p was this example until it got its script)
        Assert.False(LiveCheck.Unscripted(SabberStoneCore.Model.Cards.FromId("CS2_029")));
        // a card reading an engine counter live never sends (the map's picks: TLC_464e), and one whose counter the rebuild fills
        // (Quickdraw: HsTag.ADDED_TO_HAND_TURN from live's history)
        Assert.NotEmpty(LiveCheck.UnfilledCountersRead(SabberStoneCore.Model.Cards.FromId("TLC_464e")));
        Assert.Empty(LiveCheck.UnfilledCountersRead(SabberStoneCore.Model.Cards.FromId("WW_823")));
    }
}
