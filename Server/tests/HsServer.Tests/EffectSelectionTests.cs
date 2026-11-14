using HsServer.Engine;
using HsServer.Sabber;
using PegasusGame;
using SabberStoneCore.Enums;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;
using SGame = SabberStoneCore.Model.Game;
using Xunit;

namespace HsServer.Tests;

/// <summary>
/// A card whose client effect is a SelectSpell plays the table entry the block's META_DATA EFFECT_SELECTION names, and nothing
/// without it (client SelectSpell.AttachPowerTaskList; user 2026-10-09: Arcanosaur's explosion never showed, client Power.log
/// "FAILED to attach task list to spell TRLFX_ArcaneExplosion_SelectSpell"). Loader.SelectFx, ClientStream.
/// </summary>
public class EffectSelectionTests
{
    private static readonly Lazy<CardDb> Db = new(() =>
    {
        string dir = CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
        SabberEngineGame.EnsureCards(dir);
        return CardDb.Load(dir);
    });

    private static SGame TenManaGame()
    {
        _ = Db.Value;
        var game = new SGame(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.DRUID, Player2HeroClass = CardClass.MAGE,
            FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false,
        });
        game.StartGame();
        game.Player1.BaseMana = 10;
        game.Player2.BaseMana = 10;
        return game;
    }

    private static IPlayable Give(Controller c, string id) =>
        SabberStoneCore.Actions.Generic.DrawCard(c, SabberStoneCore.Model.Cards.FromId(id));

    private static Minion OnBoard(SGame game, Controller c, string id)
    {
        var m = (Minion)SabberStoneCore.Model.Entities.Entity.FromCard(c, SabberStoneCore.Model.Cards.FromId(id));
        SabberStoneCore.Actions.Generic.SummonBlock.Invoke(game, m, -1, null);
        m[GameTag.NUM_TURNS_IN_PLAY] = 1;
        m.IsExhausted = false;
        return m;
    }

    /// <summary>What the viewer gets from <paramref name="from"/> on, and the EFFECT_SELECTIONs inside <paramref name="source"/>'s
    /// effect blocks (null: none in a block).</summary>
    private static (List<PowerHistoryData> Sent, List<PowerHistoryMetaData?> Selections) Sent(SGame game, int from, int source,
        HistoryBlock.Type type)
    {
        // the whole game (the viewer's view of the cards comes from their earlier entries), only blocks after from
        var stream = new ClientStream(1, _ => (0, 0, 0));
        stream.Convert(game.PowerHistory.Full.Take(from).ToList());
        var sent = stream.Convert(game.PowerHistory.Full.Skip(from).ToList());
        var selections = new List<PowerHistoryMetaData?>();
        for (int i = 0; i < sent.Count; i++)
        {
            if (sent[i].PowerStart is not { } p || p.Source != source || p.Type != type) continue;
            PowerHistoryMetaData? found = null;
            for (int j = i + 1, depth = 1; j < sent.Count; j++)
            {
                if (sent[j].PowerStart != null) depth++;
                if (sent[j].PowerEnd != null && --depth == 0) break;
                if (depth == 1 && sent[j].MetaData is { Type: HistoryMeta.Type.EFFECT_SELECTION } m) found ??= m;
            }
            selections.Add(found);
        }
        return (sent, selections);
    }

    [Theory]
    [InlineData(1, 1)] // an Elemental last turn: its Arcane Explosion (the table's 1)
    [InlineData(0, 0)] // none: the empty entry (live The Countess sends 0 in an empty block)
    public void Arcanosaur_tells_the_client_whether_its_explosion_plays(int elementals, int expected)
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var enemy = OnBoard(game, me.Opponent, "CS2_182"); // 4/5
        me[GameTag.NUM_ELEMENTAL_PLAYED_LAST_TURN] = elementals;
        var saur = Give(me, "TRL_311");
        int from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(PlayCardTask.Minion(me, saur)));
        Assert.Equal(elementals > 0 ? 3 : 0, enemy.Damage);
        var (sent, selections) = Sent(game, from, saur.Id, HistoryBlock.Type.POWER);
        var selection = Assert.Single(selections);
        Assert.NotNull(selection);
        Assert.Equal(expected, selection!.Data);
        Assert.Equal(new[] { saur.Id }, selection.Info);
        // live: first in the block (before its TARGET)
        int start = sent.FindIndex(d => d.PowerStart is { Type: HistoryBlock.Type.POWER } p && p.Source == saur.Id);
        Assert.Equal(HistoryMeta.Type.EFFECT_SELECTION, sent[start + 1].MetaData?.Type);
    }

    [Fact]
    public void Mechathun_without_its_condition_sends_the_empty_entry_like_live()
    {
        // live (session-20261006-235106 80905, 20261008-120539 18916): Mecha'thun's DEATHRATTLE block, EFFECT_SELECTION 1, nothing
        // destroyed — its client table is 0 = MechaThun_Deathrattle_FX, 1 = none.
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var thun = OnBoard(game, me, "BOT_424");
        int from = game.PowerHistory.Full.Count;
        SabberStoneCore.Actions.Generic.Destroy(thun, null);
        game.DeathProcessingAndAuraUpdate();
        Assert.True(me.Opponent.Hero.Health > 0);
        var (_, selections) = Sent(game, from, thun.Id, HistoryBlock.Type.TRIGGER);
        Assert.Contains(selections, s => s is { Data: 1 });
    }

    [Theory]
    [InlineData(true, 0)]  // a Lifesteal minion in hand gets the buff: the axe effect (table 0)
    [InlineData(false, 1)] // nothing to buff: the empty entry (1)
    public void Deathaxe_punisher_follows_what_its_battlecry_did(bool lifestealInHand, int expected)
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        while (me.HandZone.Count > 0) me.HandZone.Remove(me.HandZone[0]);
        if (lifestealInHand) // any Lifesteal minion
            SabberStoneCore.Actions.Generic.DrawCard(me, SabberStoneCore.Model.Cards.All.First(c =>
                c.Type == CardType.MINION && c.Collectible && c[GameTag.LIFESTEAL] == 1));
        var axe = Give(me, "ICC_810");
        int from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(PlayCardTask.Minion(me, axe)));
        var (_, selections) = Sent(game, from, axe.Id, HistoryBlock.Type.POWER);
        Assert.Equal(expected, Assert.Single(selections)!.Data);
    }

    [Fact]
    public void A_card_without_a_select_effect_sends_none()
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var yeti = Give(me, "CS2_182");
        int from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(PlayCardTask.Minion(me, yeti)));
        var sent = new ClientStream(1, _ => (0, 0, 0)).Convert(game.PowerHistory.Full);
        Assert.DoesNotContain(sent, d => d.MetaData is { Type: HistoryMeta.Type.EFFECT_SELECTION });
    }

    [Theory]
    [InlineData(true, 1)]  // no minion in hand: keep all 3, its portal effect (1)
    [InlineData(false, 0)] // minions in hand: a plain Discover (0)
    public void Crystalsong_portal_selects_by_its_text_condition(bool emptyOfMinions, int expected)
    {
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        while (me.HandZone.Count > 0) me.HandZone.Remove(me.HandZone[0]);
        if (!emptyOfMinions) Give(me, "CS2_182");
        var portal = Give(me, "DAL_352");
        int from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(PlayCardTask.Spell(me, portal)));
        var (_, selections) = Sent(game, from, portal.Id, HistoryBlock.Type.POWER);
        Assert.Equal(expected, Assert.Single(selections)!.Data);
    }

    [Fact]
    public void Augmented_elekk_names_the_deck_its_copy_goes_to()
    {
        // its client table: 0 = KARAFX_SpawnToDeck_CardOverDeck_Blue_FX_FastTriggers, 1 = …_Opponent (live: 1 into the
        // opponent's deck, session-20261007-113244); Lab Recruiter shuffles into its owner's deck: 0
        var game = TenManaGame();
        var me = game.CurrentPlayer;
        var elekk = OnBoard(game, me, "BOT_559");
        var recruiter = Give(me, "BOT_288");
        int from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(PlayCardTask.MinionTarget(me, recruiter, elekk)));
        var (_, selections) = Sent(game, from, elekk.Id, HistoryBlock.Type.TRIGGER);
        Assert.NotEmpty(selections);
        Assert.All(selections, s => Assert.Equal(0, s!.Data));
    }

    [Fact]
    public void Every_client_select_effect_is_read()
    {
        // ClientSelectFx.tsv (RESEARCH/vfx/tools/select_spells.py): 17 SelectSpell effects on 36 card blocks
        Assert.True(SabberStoneCore.Loader.SelectFx.Has("TRL_311", BlockType.POWER, 0));
        Assert.True(SabberStoneCore.Loader.SelectFx.Has("BOT_424", BlockType.TRIGGER, 0));
        Assert.True(SabberStoneCore.Loader.SelectFx.Has("CORE_REV_951", BlockType.POWER, 0));
        Assert.False(SabberStoneCore.Loader.SelectFx.Has("CS2_182", BlockType.POWER, 0));
        // Ramming Speed: the "…AttackRight" entry is 0, "…AttackLeft" 1; Supercollider: "_Right" 1, the other spell 0
        Assert.Equal(0, SabberStoneCore.Loader.SelectFx.IndexNamed("DRG_022", BlockType.POWER, 0, "Right", otherwiseSpell: true));
        Assert.Equal(1, SabberStoneCore.Loader.SelectFx.IndexNamed("DRG_022", BlockType.POWER, 0, "Left", otherwiseSpell: true));
        Assert.Equal(1, SabberStoneCore.Loader.SelectFx.IndexNamed("BOT_406", BlockType.TRIGGER, 0, "Right", otherwiseSpell: true));
        Assert.Equal(0, SabberStoneCore.Loader.SelectFx.IndexNamed("BOT_406", BlockType.TRIGGER, 0, "Left", otherwiseSpell: true));
    }
}
