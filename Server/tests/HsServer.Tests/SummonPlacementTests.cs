using CardDb = HsServer.Engine.CardDb;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;
using SGame = SabberStoneCore.Model.Game;

namespace HsServer.Tests;

/// <summary>
/// Where several minions summoned by one effect land (Generic.SummonPosition / LayoutOf; engine-rules.md "Board placement of
/// summons"). The rule as every live recording shows it (2026-10-10 survey) and as the user put it (2026-10-10): a living minion
/// whose text summons an even number or "as many as" (fill, until full, all, each, up to N) puts them both sides of itself, each
/// directly beside it, the first to its right; an odd number in its text: each directly to its right; a resurrect: to its right
/// in order; spells (and every other non-minion source): the far right in order; a Deathrattle: in the dead minion's slot.
/// A layout string reads the board left to right: S = the summoner, 1..n = its summons in the order they were made, . = others.
/// </summary>
public class SummonPlacementTests
{
    private static readonly Lazy<CardDb> Db = new(() =>
    {
        string dir = CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
        SabberEngineGame.EnsureCards(dir);
        return CardDb.Load(dir);
    });

    private static SGame NewGame()
    {
        _ = Db.Value;
        var game = new SGame(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.DEATHKNIGHT, Player2HeroClass = CardClass.MAGE,
            FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false,
        });
        game.StartGame();
        game.Player1.BaseMana = 10;
        game.Player2.BaseMana = 10;
        return game;
    }

    private static Minion OnBoard(SGame game, Controller c, string id)
    {
        var m = (Minion)SabberStoneCore.Model.Entities.Entity.FromCard(c, Cards.FromId(id));
        Generic.SummonBlock.Invoke(game, m, -1, null);
        m[GameTag.NUM_TURNS_IN_PLAY] = 1;
        m.IsExhausted = false;
        return m;
    }

    private static IPlayable Give(Controller c, string id) => Generic.DrawCard(c, Cards.FromId(id));

    /// <summary>Plays <paramref name="id"/> (a minion lands on the far right) and returns it and the board layout after it.</summary>
    private static (IPlayable Card, string Layout) Play(SGame game, Controller me, string id, int chooseOne = 0)
    {
        me.UsedMana = 0;
        var before = me.BoardZone.Select(m => m.Id).ToHashSet();
        var card = Give(me, id);
        var task = card is Minion ? PlayCardTask.Minion(me, card, chooseOne) : PlayCardTask.Spell(me, card, chooseOne);
        Assert.True(game.Process(task), id);
        return (card, Layout(me, card as Minion, before));
    }

    /// <summary>The board: S = <paramref name="source"/>, 1..n = the minions new since <paramref name="before"/>, in the order made.</summary>
    private static string Layout(Controller me, Minion? source, HashSet<int> before)
    {
        var made = me.BoardZone.Where(m => m != source && !before.Contains(m.Id)).Select(m => m.Id).OrderBy(x => x).ToList();
        return string.Concat(me.BoardZone.Select(m => m == source ? "S" : made.IndexOf(m.Id) is var i and >= 0 ? (i + 1).ToString() : "."));
    }

    private static void Clear(SGame game, Controller c)
    {
        foreach (var m in c.BoardZone.ToList()) c.BoardZone.Remove(m);
        game.DeathProcessingAndAuraUpdate();
    }

    [Fact]
    public void The_card_text_says_how_many()
    {
        _ = Db.Value;
        Generic.SummonCount Of(string id) => Generic.SummonCountOf(Cards.FromId(id));
        // odd numbers
        Assert.Equal(Generic.SummonCount.Odd, Of("EDR_492"));   // Mother Duck: three Ducklings
        Assert.Equal(Generic.SummonCount.Odd, Of("NX2_007"));   // Frostfin Chomper: three Murlocs
        Assert.Equal(Generic.SummonCount.Odd, Of("TRL_126"));   // Captain Hooktusk: 3 Pirates from your deck
        Assert.Equal(Generic.SummonCount.Odd, Of("LOOT_521"));  // Master Oakheart: a 1, 2, and 3-Attack minion
        // even numbers
        Assert.Equal(Generic.SummonCount.Even, Of("ETC_029"));  // Keyboard Soloist: two Amps (the user's Festival of Legends Mage card)
        Assert.Equal(Generic.SummonCount.Even, Of("GVG_110"));  // Dr. Boom: two Boom Bots
        Assert.Equal(Generic.SummonCount.Even, Of("ONY_004"));  // Raid Boss Onyxia: six Whelps
        Assert.Equal(Generic.SummonCount.Even, Of("CFM_668"));  // Doppelgangster: 2 copies of this
        Assert.Equal(Generic.SummonCount.Even, Of("SCH_181"));  // Archwitch Willow: a Demon from your hand and deck
        Assert.Equal(Generic.SummonCount.Even, Of("WW_392"));   // Elise, Badlands Savior: 5/5 copies of 4 minions (not "5")
        Assert.Equal(Generic.SummonCount.Even, Of("REV_244"));  // Mischievous Imp: Infuse: two copies instead
        // as many as there are / as fit
        Assert.Equal(Generic.SummonCount.Any, Of("EX1_562"));   // Onyxia: until your side is full
        Assert.Equal(Generic.SummonCount.Any, Of("CS3_032"));   // Onyxia the Broodmother: fill your board
        Assert.Equal(Generic.SummonCount.Any, Of("CORE_RLK_506")); // Boneguard Commander: raise up to 6 Corpses (the user's DK card)
        Assert.Equal(Generic.SummonCount.Any, Of("RLK_085"));   // Lord Marrowgar: raise ALL your Corpses
        Assert.Equal(Generic.SummonCount.Any, Of("LOOT_535"));  // Dragoncaller Alanna: a Dragon for each spell
        Assert.Equal(Generic.SummonCount.Any, Of("TSC_658"));   // Hedra: for each spell, summon a minion
        Assert.Equal(Generic.SummonCount.Any, Of("TRL_259"));   // Princess Talanji: all minions from your hand
        Assert.Equal(Generic.SummonCount.Any, Of("OG_316"));    // Herald Volazj: a copy of each of your other minions
        Assert.Equal(Generic.SummonCount.Any, Of("TOY_880"));   // Wind-Up Enforcer: @ copies
        // a single summon: nothing to place but "directly right"
        Assert.Equal(Generic.SummonCount.Unknown, Of("CS2_196")); // Razorfen Hunter: a Boar
    }

    [Fact]
    public void An_even_count_goes_both_sides_the_first_to_the_right()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        // Keyboard Soloist (the user's Festival of Legends Mage card): its Amps one on each side — was both on its right
        Assert.Equal("2S1", Play(game, me, "ETC_029").Layout);
        Clear(game, me);
        // Dr. Boom with two minions on its left (live: Dr. Boom 2S1; wiki: "Dr. Boom's right Boom Bot spawns before the left")
        OnBoard(game, me, "CS2_182"); OnBoard(game, me, "CS2_182");
        Assert.Equal("..2S1", Play(game, me, "GVG_110").Layout);
        Clear(game, me);
        // Raid Boss Onyxia: six Whelps, alternating, each directly beside her (live ..24S31 with 2 others: 4 fit)
        OnBoard(game, me, "CS2_182"); OnBoard(game, me, "CS2_182");
        Assert.Equal("..24S31", Play(game, me, "ONY_004").Layout);
        Clear(game, me);
        // copies go both sides too (live Nerubian Swarmguard 2S1 x4; its script was two copies "to its right")
        Assert.Equal("2S1", Play(game, me, "CORE_RLK_062").Layout);
    }

    [Fact]
    public void A_fill_effect_alternates_each_beside_the_summoner()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        // Onyxia: six Whelps — live Onyxia the Broodmother / N'Zoth: W2 W4 W6 S W5 W3 W1
        Assert.Equal("246S531", Play(game, me, "EX1_562").Layout);
        Clear(game, me);
        // the board fills up: with four others only two fit, one each side
        for (int i = 0; i < 4; i++) OnBoard(game, me, "CS2_182");
        Assert.Equal("....2S1", Play(game, me, "EX1_562").Layout);
        Clear(game, me);
        // Boneguard Commander (the user's 8-mana Death Knight card): 3 Corpses → 3 Risen Footmen, still both sides (live:
        // "for each" / "all" counts alternate whatever the number: Dragoncaller Alanna 2S31, Lord Marrowgar .24S531)
        me.Corpses = 3;
        Assert.Equal("2S31", Play(game, me, "CORE_RLK_506").Layout);
        Clear(game, me);
        me.Corpses = 5;
        OnBoard(game, me, "CS2_182");
        Assert.Equal(".24S531", Play(game, me, "RLK_085").Layout); // Lord Marrowgar, as live
        Clear(game, me);
        // Onyxia the Broodmother's end of turn fill
        var broodmother = OnBoard(game, me, "CS3_032");
        var before = me.BoardZone.Select(m => m.Id).ToHashSet();
        Assert.True(game.Process(EndTurnTask.Any(me)));
        Assert.Equal("246S531", Layout(me, broodmother, before));
    }

    [Fact]
    public void An_odd_count_goes_each_directly_to_the_right()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        // Mother Duck (live x6: S D3 D2 D1 — each lands directly right of her)
        OnBoard(game, me, "CS2_182"); OnBoard(game, me, "CS2_182");
        Assert.Equal("..S321", Play(game, me, "EDR_492").Layout);
        Clear(game, me);
        // Captain Hooktusk: 3 Pirates from the deck, all on his right (from the deck doesn't change it)
        foreach (var c in me.DeckZone.ToList()) me.DeckZone.Remove(c);
        for (int i = 0; i < 3; i++) me.DeckZone.Add(SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("CS2_146")));
        OnBoard(game, me, "CS2_182");
        var (hooktusk, layout) = Play(game, me, "TRL_126");
        Assert.Equal(5, me.BoardZone.Count);
        Assert.Equal(1, me.BoardZone.ToList().IndexOf((Minion)hooktusk));
        Assert.Equal(".S", layout.Substring(0, 2));
    }

    [Fact]
    public void A_resurrect_lines_up_to_the_right_n_zoth_goes_both_sides_like_live()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        // Kel'Thuzad (user rule: resurrects go to the right; live session-20261007-144826: S R1 R2)
        var kt = OnBoard(game, me, "FP1_013");
        var a = OnBoard(game, me, "CS2_182"); var b = OnBoard(game, me, "CS2_120");
        Generic.Destroy(a, null); Generic.Destroy(b, null);
        game.DeathProcessingAndAuraUpdate();
        var before = me.BoardZone.Select(m => m.Id).ToHashSet();
        Assert.True(game.Process(EndTurnTask.Any(me)));
        Assert.Equal("S12", Layout(me, kt, before));
        game.Process(EndTurnTask.Any(me.Opponent));
        Clear(game, me);
        // N'Zoth, the Corruptor: live (session-20261002-133808) puts his Deathrattle minions both sides (patch 30.0: "equally to
        // the left and right sides, instead of all on the right side")
        var hoarder = OnBoard(game, me, "EX1_096"); var golem = OnBoard(game, me, "EX1_556");
        Generic.Destroy(hoarder, null); Generic.Destroy(golem, null);
        game.DeathProcessingAndAuraUpdate();
        Clear(game, me);
        // (the Harvest Golem's Damaged Golem went with the board; its Deathrattle minions come back one on each side)
        Assert.Equal("2S1", Play(game, me, "OG_133").Layout);
    }

    [Fact]
    public void Spells_summon_on_the_far_right_in_order_and_deathrattles_in_the_slot()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        OnBoard(game, me, "CS2_182"); OnBoard(game, me, "CS2_182");
        Assert.Equal("..12", Play(game, me, "CS2_027").Layout); // Mirror Image
        Clear(game, me);
        OnBoard(game, me, "CS2_182");
        Assert.Equal(".123456", Play(game, me, "WORK_012").Layout); // Huddle Up: "fill your board" from a spell (live the same)
        Clear(game, me);
        // Haunted Creeper dies between two minions: its Spiders in its slot, left to right
        OnBoard(game, me, "CS2_182"); var creeper = OnBoard(game, me, "FP1_002"); OnBoard(game, me, "CS2_182");
        var before = me.BoardZone.Select(m => m.Id).ToHashSet();
        Generic.Destroy(creeper, null);
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(".12.", Layout(me, null, before));
        Clear(game, me);
        // the opponent's side: far right (Terrorguard Escapee's Huntresses)
        var op = me.Opponent;
        OnBoard(game, op, "CS2_182");
        var opBefore = op.BoardZone.Select(m => m.Id).ToHashSet();
        Play(game, me, "BT_159");
        Assert.Equal(".123", Layout(op, null, opBefore));
    }

    [Fact]
    public void A_trigger_in_the_middle_doesnt_break_the_group_and_the_group_arrives_together()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        // Knife Juggler throws after each Boom Bot: the second still goes to Dr. Boom's left (one effect: Game.EffectSerial)
        OnBoard(game, me, "NEW1_019");
        Assert.Equal(".2S1", Play(game, me, "GVG_110").Layout);
        Clear(game, me);
        // live Mother Duck: one block, one spawn SUB_SPELL with the three FULL_ENTITYs inside, nothing between them
        int from = game.PowerHistory.Full.Count;
        Play(game, me, "EDR_492");
        var entries = game.PowerHistory.Full.Skip(from).ToList();
        var ducks = entries.Select((e, i) => (e, i)).Where(x => x.e is PowerHistoryFullEntity fe && fe.Entity.Name == "EDR_492t").Select(x => x.i).ToList();
        Assert.Equal(3, ducks.Count);
        Assert.DoesNotContain(entries.Skip(ducks[0]).Take(ducks[2] - ducks[0]), e => e is PowerHistoryBlockStart || e is PowerHistorySubSpellEnd);
    }
}
