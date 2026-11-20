using HsServer.Engine;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsServer.Tests;

/// <summary>Assemble (Generic.Assemble): live session-20261021-013733 28457-28620 — a base from the pool, two sides offered twice (one
/// per half: 4573 / 4572), the pick swaps one half; the result keeps the base's name, Cost and stats and carries live's tags.</summary>
public class AssembleTests
{
    private static SabberStoneCore.Model.Game NewGame()
    {
        SabberEngineGame.EnsureCards(CardDb.FindDefaultDir(TestEnvironment.ProjectRoot));
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig { StartPlayer = 1, FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false, SkipMulligan = true, FormatType = FormatType.FT_WILD }); // (as the server plays: Wild)
        game.StartGame();
        return game;
    }

    [Fact]
    public void An_assembled_minion_is_a_unique_card_apart_from_its_base()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        Card doom = Cards.FromId("CORE_NEW1_021");
        var plain = SabberStoneCore.Model.Entities.Entity.FromCard(me, doom);
        var asm1 = SabberStoneCore.Model.Entities.Entity.FromCard(me, Generic.AssembledCard(doom, doom, Cards.FromId("TIME_100")));
        var asm2 = SabberStoneCore.Model.Entities.Entity.FromCard(me, Generic.AssembledCard(doom, doom, Cards.FromId("TIME_100")));
        asm1[Generic.ASSEMBLED_MINION] = 1; asm2[Generic.ASSEMBLED_MINION] = 1;
        Assert.Equal(3, new[] { plain, asm1, asm2 }.GroupBy(Generic.UniqueCardKey).Count());
        var plain2 = SabberStoneCore.Model.Entities.Entity.FromCard(me, doom);
        Assert.Equal(Generic.UniqueCardKey(plain), Generic.UniqueCardKey(plain2));
    }

    [Fact]
    public void Another_class_player_assembles_from_their_own_class()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        Assert.NotEqual(CardClass.SHAMAN, me.HeroClass); // (Shadow Sculpt is a Shaman card: this player got it from elsewhere)
        var sculpt = SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("BE_114"));
        bool ownClassSeen = false;
        for (int i = 0; i < 60; i++)
        {
            me.Choice = null;
            Generic.Assemble(me, sculpt, (c, x) => x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL));
            Assert.NotNull(me.Choice);
            foreach (int id in me.Choice.Choices)
            {
                Card side = game.IdEntityDic[id].Card;
                Assert.True(side.HasClass(me.HeroClass) || side.HasClass(CardClass.NEUTRAL), side.Id);
                if (side.HasClass(me.HeroClass) && !side.HasClass(CardClass.NEUTRAL)) ownClassSeen = true;
            }
        }
        Assert.True(ownClassSeen);
    }

    [Fact]
    public void Middle_and_sides_never_share_a_half_and_every_swap_fits()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        var fetcher = SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("BE_044"));
        for (int i = 0; i < 100; i++)
        {
            me.Choice = null;
            Generic.Assemble(me, fetcher, (c, x) => (x.HasClass(c.HeroClass) || x.HasClass(CardClass.NEUTRAL)) && x.Cost == 4);
            if (me.Choice == null) continue;
            var opts = me.Choice.Choices.Select(id => game.IdEntityDic[id].Card).Distinct().ToList();
            Card middle = game.IdEntityDic[me.Choice.Choices[0] - 1].Card; // (made right before the options)
            var all = opts.Append(middle).ToList();
            Assert.Equal(all.Count, all.Select(x => Generic.AssembleHalfText(x, true)).Distinct().Count());
            Assert.Equal(all.Count, all.Select(x => Generic.AssembleHalfText(x, false)).Distinct().Count());
            Assert.All(opts, x => Assert.True(Generic.AssembleFits(middle, x), x.Id + " with " + middle.Id));
        }
    }

    [Fact]
    public void A_deathrattle_cannot_take_an_effect_on_this_minion()
    {
        NewGame();
        // Crystalline Golem's "Deathrattle:" can't take Mantid Precursor-like "gain +1/+1"; an end-of-turn can't Discover
        Card dr = Cards.All.First(x => x.Collectible && x[Generic.ASSEMBLE_POOL] > 0 && Generic.AssembleHalfText(x, true) == "deathrattle");
        Card selfBuff = Cards.All.First(x => x.Collectible && x[Generic.ASSEMBLE_POOL] > 0 && Generic.AssembleHalfText(x, false).ToLowerInvariant().Contains("gain +"));
        Assert.False(Generic.AssembleCompatible(dr, selfBuff, dr));
    }

    [Fact]
    public void A_class_base_is_twice_as_likely_as_a_neutral_one()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        Card classCard = Cards.All.First(x => x.Collectible && x.Type == CardType.MINION && x.HasClass(me.HeroClass) && !x.HasClass(CardClass.NEUTRAL));
        Card neutral = Cards.All.First(x => x.Collectible && x.Type == CardType.MINION && x.HasClass(CardClass.NEUTRAL));
        var bases = new List<Card> { classCard, neutral };
        int cls = 0, n = 6000;
        for (int i = 0; i < n; i++) if (Generic.PickAssembleBase(me, bases) == classCard) cls++;
        double ratio = cls / (double)(n - cls);
        Assert.InRange(ratio, 1.8, 2.2);
    }

    [Fact]
    public void Live_example_doomsayer_with_the_second_half_of_time_100()
    {
        NewGame();
        Card doom = Cards.FromId("CORE_NEW1_021"), polish = Cards.FromId("TIME_100");
        Assert.True(Generic.Assemblable(doom) && Generic.Assemblable(polish));
        Card made = Generic.AssembledCard(doom, doom, polish);
        Assert.Equal(TriggerType.TURN_START, made.Power.Trigger.Type);
        Assert.Same(polish.Power.Trigger.SingleTask, made.Power.Trigger.SingleTask);
        Assert.Equal(doom.Name, made.Name);
        Assert.Equal(doom.Cost, made.Cost);
    }

    [Fact]
    public void A_first_half_that_aims_brings_its_target_live_stone_trogg()
    {
        NewGame();
        // live session-20261022-020357: Stone Trogg with Ohn'ahra's "At the end of your turn," played without a target
        Card trogg = Cards.FromId("BE_994"), ohnahra = Cards.FromId("EDR_031"), doom = Cards.FromId("CORE_NEW1_021");
        Card endOfTurn = Generic.AssembledCard(trogg, ohnahra, trogg);
        Assert.Equal(TriggerType.TURN_END, endOfTurn.Power.Trigger.Type);
        Assert.Empty(endOfTurn.PlayRequirements);
        // its own first half aims, whatever second half it takes
        Card aimed = Generic.AssembledCard(doom, trogg, doom);
        Assert.NotNull(aimed.Power.PowerTask);
        Assert.Equal(trogg.PlayRequirements.Count, aimed.PlayRequirements.Count);
    }

    [Fact]
    public void Rajh_turns_the_assembled_minion_into_a_passive_hero_power()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        me.BaseMana = 10; me.UsedMana = 0;
        var rajh = SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("BE_004"));
        me.HandZone.Add(rajh);
        int oldPower = me.Hero.HeroPower.Id;
        Assert.True(game.Process(PlayCardTask.Minion(me, rajh)));
        Assert.NotNull(me.Choice);
        game.Process(ChooseTask.Pick(me, me.Choice.Choices[0]));
        Assert.Equal("BE_004p", me.Hero.HeroPower.Card.Id);
        Assert.NotEqual(oldPower, me.Hero.HeroPower.Id);
        Assert.NotNull(me.Hero.HeroPower.Card.Power?.Trigger); // the assembled trigger, a Passive
        Assert.NotNull(me.Hero.HeroPower.ActivatedTrigger);
    }

    [Fact]
    public void Fetcher_assembles_a_4_cost_minion_into_the_hand()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        me.BaseMana = 10; me.UsedMana = 0;
        var fetcher = SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("BE_044"));
        me.HandZone.Add(fetcher);
        int before = me.HandZone.Count;
        Assert.True(game.Process(PlayCardTask.Minion(me, fetcher)));
        Assert.NotNull(me.Choice);
        Assert.Equal(4, me.Choice.Choices.Count);
        game.Process(ChooseTask.Pick(me, me.Choice.Choices[1])); // a 4572 option: its second half
        var made = me.HandZone.GetAll().Last();
        Assert.Equal(before, me.HandZone.Count); // (F3TCH3R left, the assembled minion came)
        Assert.Equal(1, made[Generic.ASSEMBLED_MINION]);
        Assert.Equal(1, made[Generic.CRAFT]);
        Assert.True(made[Generic.CRAFTABLE_SECOND_HALF_DBID] > 0);
        Assert.Equal(4, made.Card.Cost);
        Assert.Contains(made.AppliedEnchantments, e => e.Card.Id == "BE_044e");
    }
}
