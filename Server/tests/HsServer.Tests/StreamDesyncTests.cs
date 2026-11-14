using HsServer.Protocol;
using HsServer.Sabber;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using Xunit;

namespace HsServer.Tests;

/// <summary>(2026-10-09, the user's Heist game vs Carousel Gryphon) what made the client lose track of the board.</summary>
public class StreamDesyncTests
{
    private static SabberStoneCore.Model.Game Game()
    {
        SabberEngineGame.EnsureCards(HsServer.Engine.CardDb.FindDefaultDir(TestEnvironment.ProjectRoot));
        var g = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.MAGE, Player2HeroClass = CardClass.HUNTER,
            FillDecks = true, History = true, Shuffle = false, SkipMulligan = true,
        });
        g.StartGame();
        return g;
    }

    [Fact]
    public void A_secret_put_into_play_from_the_deck_shows_its_class_and_cost()
    {
        // Mad Scientist's Deathrattle put Freezing Trap from the deck into play: we sent CLASS=INVALID and no SECRET / COST — the client
        // drew a broken "?" (the user saw a Mage Secret) and its reveal played nothing
        var g = Game();
        foreach (var card in g.Player2.DeckZone.GetAll().ToList()) g.Player2.DeckZone.Remove(card); // (only the trap to pick)
        var trap = SabberStoneCore.Model.Entities.Entity.FromCard(g.Player2, SabberStoneCore.Model.Cards.FromId("EX1_611"), zone: g.Player2.DeckZone);
        int mark = g.PowerHistory.Full.Count;
        var scientist = SabberStoneCore.Model.Entities.Entity.FromCard(g.Player2, SabberStoneCore.Model.Cards.FromId("FP1_004"), zone: g.Player2.BoardZone);
        ((SabberStoneCore.Model.Entities.Minion)scientist).Destroy(); // (Mad Scientist's Deathrattle: a Secret from the deck into play)
        g.DeathProcessingAndAuraUpdate();
        Assert.Equal(Zone.SECRET, trap.Zone.Type);
        var stream = new ClientStream(1, _ => (0, 0, 0));
        stream.Convert(g.PowerHistory.Full.Take(mark));
        var sent = stream.Convert(g.PowerHistory.Full.Skip(mark));
        var tags = sent.Where(d => d.TagChange is { } t && t.Entity == trap.Id).Select(d => d.TagChange).ToList();
        Assert.Contains(tags, t => t.Tag == (int)HsServer.Engine.GAME_TAG.CLASS && t.Value == (int)CardClass.HUNTER);
        Assert.Contains(tags, t => t.Tag == (int)HsServer.Engine.GAME_TAG.SECRET && t.Value == 1);
        Assert.Contains(tags, t => t.Tag == (int)HsServer.Engine.GAME_TAG.COST && t.Value == 2);
    }

    [Fact]
    public void A_minion_bounced_from_the_board_is_only_moved()
    {
        // Freezing Trap returned the attacking minion: we re-sent it as a SHOW_ENTITY still "in play at position 0" before the move; live
        // only moves it — the client lost track of the board (the other minions stopped lighting up)
        var g = Game();
        var minion = SabberStoneCore.Model.Entities.Entity.FromCard(g.Player1, SabberStoneCore.Model.Cards.FromId("CS2_182"), zone: g.Player1.BoardZone);
        int mark = g.PowerHistory.Full.Count;
        new SabberStoneCore.Tasks.SimpleTasks.ReturnHandTask(SabberStoneCore.Tasks.SimpleTasks.EntityType.TARGET).Process(g, g.Player2, g.Player2.Hero, minion);
        Assert.Equal(Zone.HAND, minion.Zone.Type);
        Assert.DoesNotContain(g.PowerHistory.Full.Skip(mark), e => e is PowerHistoryShowEntity s && s.Entity.Id == minion.Id);
    }

    // ---- The client-view guard (2026-10-09, user: "make sure we don't send fake data to the client"): the stream's packets are
    // replayed into a model of what the client believes (ClientView: the rules live keeps, derived from 402 live games) and that is
    // compared with the engine as the viewer may see it (SoakRunner.Compare). Soak runs the same guard on every packet.

    private static readonly Lazy<HsServer.Engine.CardDb> ClientCards =
        new(() => HsServer.Engine.CardDb.Load(HsServer.Engine.CardDb.FindDefaultDir(TestEnvironment.ProjectRoot)));

    /// <summary>The whole game so far as player <paramref name="viewer"/>'s client gets it: every client-view rule it breaks.</summary>
    private static List<string> ClientViewProblems(SabberStoneCore.Model.Game g, int viewer = 1)
    {
        var view = new ClientView { Cards = ClientCards.Value };
        var problems = new List<string>();
        view.Apply(new ClientStream(viewer, _ => (0, 0, 0)).Convert(g.PowerHistory.Full), problems.Add);
        SoakRunner.Compare(g, view, viewer, problems, "end");
        return problems;
    }

    private static PegasusGame.PowerHistoryData Full(int id, string card, params (HsServer.Engine.GAME_TAG Tag, int Value)[] tags) => new()
    {
        FullEntity = new PegasusGame.PowerHistoryEntity
        {
            Entity = id, Name = card, Tags = tags.Select(t => new PegasusGame.Tag { Name = (int)t.Tag, Value = t.Value }).ToList(),
            DefTags = new List<PegasusGame.Tag>(), TagLists = new List<PegasusGame.TagList>(), DefTagLists = new List<PegasusGame.TagList>(),
        },
    };

    private static PegasusGame.PowerHistoryData Change(int id, HsServer.Engine.GAME_TAG tag, int value) =>
        new() { TagChange = new PegasusGame.PowerHistoryTagChange { Entity = id, Tag = (int)tag, Value = value } };

    [Fact]
    public void The_client_view_flags_a_hidden_secret_without_its_class()
    {
        // the old wire of bug 1: the opponent's Secret entered the Secret zone with no CLASS / SECRET — the client drew a broken "?"
        var view = new ClientView();
        var problems = new List<string>();
        view.Apply(new List<PegasusGame.PowerHistoryData>
        {
            Full(70, "", (HsServer.Engine.GAME_TAG.ZONE, (int)HsServer.Engine.TAG_ZONE.DECK), (HsServer.Engine.GAME_TAG.CONTROLLER, 2)),
            Change(70, HsServer.Engine.GAME_TAG.ZONE, (int)HsServer.Engine.TAG_ZONE.SECRET),
        }, problems.Add);
        Assert.Contains(problems, p => p.Contains("Secret zone"));

        // live's shape (heist-run-20260929 #2088): kind, class and cost first
        view = new ClientView();
        problems.Clear();
        view.Apply(new List<PegasusGame.PowerHistoryData>
        {
            Full(70, "", (HsServer.Engine.GAME_TAG.ZONE, (int)HsServer.Engine.TAG_ZONE.DECK), (HsServer.Engine.GAME_TAG.CONTROLLER, 2)),
            Change(70, HsServer.Engine.GAME_TAG.SECRET, 1), Change(70, HsServer.Engine.GAME_TAG.CLASS, (int)CardClass.HUNTER),
            Change(70, HsServer.Engine.GAME_TAG.COST, 2), Change(70, HsServer.Engine.GAME_TAG.ZONE, (int)HsServer.Engine.TAG_ZONE.SECRET),
        }, problems.Add);
        Assert.Empty(problems);
    }

    [Fact]
    public void The_client_view_flags_a_public_card_revealed_again()
    {
        // the old wire of bug 2: a bounced minion re-sent as a SHOW_ENTITY "in play at position 0", then moved
        var view = new ClientView();
        var problems = new List<string>();
        view.Apply(new List<PegasusGame.PowerHistoryData>
        {
            Full(70, "CS2_182", (HsServer.Engine.GAME_TAG.ZONE, (int)HsServer.Engine.TAG_ZONE.PLAY), (HsServer.Engine.GAME_TAG.CONTROLLER, 1),
                (HsServer.Engine.GAME_TAG.CARDTYPE, (int)HsServer.Engine.TAG_CARDTYPE.MINION), (HsServer.Engine.GAME_TAG.ZONE_POSITION, 1)),
        }, problems.Add);
        Assert.Empty(problems);
        var show = Full(70, "CS2_182", (HsServer.Engine.GAME_TAG.ZONE, (int)HsServer.Engine.TAG_ZONE.PLAY), (HsServer.Engine.GAME_TAG.ZONE_POSITION, 0));
        view.Apply(new List<PegasusGame.PowerHistoryData>
        {
            new() { ShowEntity = show.FullEntity },
            Change(70, HsServer.Engine.GAME_TAG.ZONE, (int)HsServer.Engine.TAG_ZONE.HAND), Change(70, HsServer.Engine.GAME_TAG.ZONE_POSITION, 1),
        }, problems.Add);
        Assert.Contains(problems, p => p.Contains("already known as CS2_182"));
    }

    [Fact]
    public void Freezing_trap_from_mad_scientist_reaches_the_client_as_the_engine_has_it()
    {
        // the user's game (2026-10-09): Mad Scientist puts the boss's Freezing Trap into play from the deck; the player's minion attacks,
        // the trap returns it to the hand. Every packet must keep the client-view rules, and the client must end where the engine is:
        // a Hunter Secret "?" (not class-less), the trap revealed with its type, the minion only moved to the hand.
        var g = Game();
        foreach (var card in g.Player2.DeckZone.GetAll().ToList()) g.Player2.DeckZone.Remove(card);
        var trap = SabberStoneCore.Model.Entities.Entity.FromCard(g.Player2, SabberStoneCore.Model.Cards.FromId("EX1_611"), zone: g.Player2.DeckZone);
        var scientist = SabberStoneCore.Model.Entities.Entity.FromCard(g.Player2, SabberStoneCore.Model.Cards.FromId("FP1_004"), zone: g.Player2.BoardZone);
        ((SabberStoneCore.Model.Entities.Minion)scientist).Destroy();
        g.DeathProcessingAndAuraUpdate();
        Assert.Equal(Zone.SECRET, trap.Zone.Type);
        Assert.Empty(ClientViewProblems(g));

        var minion = SabberStoneCore.Actions.Generic.DrawCard(g.Player1, SabberStoneCore.Model.Cards.FromId("CS2_124")); // Wolfrider: Charge
        g.Player1.BaseMana = 10;
        Assert.True(g.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(g.Player1, minion)));
        // (the trap returns the attacker before it attacks: the attack itself reports not done)
        g.Process(SabberStoneCore.Tasks.PlayerTasks.MinionAttackTask.Any(g.Player1, minion, g.Player2.Hero));
        Assert.Equal(Zone.HAND, minion.Zone.Type);
        Assert.Equal(Zone.GRAVEYARD, trap.Zone.Type);
        Assert.Empty(ClientViewProblems(g));
    }

    [Fact]
    public void A_joust_shows_new_entities_not_the_deck_cards()
    {
        // live (session-20261007-113244, Raven Familiar): a Joust's cards are NEW entities shown from the deck and set aside; ours re-sent
        // the deck cards' own FULL_ENTITY (an entity the client already had)
        var g = Game();
        var jouster = SabberStoneCore.Actions.Generic.DrawCard(g.Player1, SabberStoneCore.Model.Cards.FromId("AT_133"));
        int deckBefore = g.Player1.DeckZone.Count, theirsBefore = g.Player2.DeckZone.Count;
        Assert.True(g.Process(SabberStoneCore.Tasks.PlayerTasks.PlayCardTask.Minion(g.Player1, jouster)));
        Assert.Equal(deckBefore, g.Player1.DeckZone.Count);
        Assert.Equal(theirsBefore, g.Player2.DeckZone.Count);
        var joust = g.PowerHistory.Full.OfType<PowerHistoryMetaData>().Single(m => m.Type == MetaDataType.JOUST);
        Assert.Equal(2, joust.Info.Count);
        Assert.All(joust.Info, id => Assert.Equal(Zone.SETASIDE, g.IdEntityDic[id].Zone.Type));
        Assert.Empty(ClientViewProblems(g));
    }

    [Fact]
    public void A_hidden_hand_card_gets_no_hand_counters()
    {
        // live: NUM_TURNS_IN_HAND / JUST_PLAYED only ever go to cards the viewer knows (0 of 42,667 to a hidden card in 402 live games)
        var g = Game();
        Assert.True(g.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(g.Player1)));
        Assert.True(g.Process(SabberStoneCore.Tasks.PlayerTasks.EndTurnTask.Any(g.Player2)));
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(g.PowerHistory.Full);
        var theirs = g.Player2.HandZone.Select(c => c.Id).ToHashSet();
        Assert.DoesNotContain(sent, d => d.TagChange is { } t && theirs.Contains(t.Entity)
            && t.Tag is (int)HsServer.Engine.GAME_TAG.NUM_TURNS_IN_HAND or (int)HsServer.Engine.GAME_TAG.JUST_PLAYED);
        Assert.Empty(ClientViewProblems(g));
    }

    [Fact]
    public void A_deck_card_changed_silently_is_revealed_as_it_is_now()
    {
        // a ranked spell ranks up silently in the deck (live: nothing sent); revealed later (here: milled) the client must get the new
        // card — ours showed the old one (soak client view: Cera'thine Fleetrunner's deck minions, BAR_042 shown for CORE_YOP_034)
        var g = Game();
        var them = g.Player2;
        them.BaseMana = 4;
        var spell = SabberStoneCore.Model.Entities.Entity.FromCard(them, SabberStoneCore.Model.Cards.FromId("BAR_319"), zone: them.DeckZone);
        them.BaseMana = 5;
        Assert.Equal("BAR_319t", spell.Card.Id);
        Assert.DoesNotContain(new ClientStream(1, _ => (0, 0, 0)).Convert(g.PowerHistory.Full), d => d.ChangeEntity?.Entity == spell.Id);
        them.DeckZone.Remove(spell);
        them.GraveyardZone.Add(spell);
        Assert.Empty(ClientViewProblems(g));
    }
}
