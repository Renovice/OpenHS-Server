using HsServer.Engine;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;
using SGame = SabberStoneCore.Model.Game;

namespace HsServer.Tests;

/// <summary>The token pass (2026-10-09): the non-collectible cards scripted cards create (CardSets/HsServer/TokenPassCardsGenHs.cs,
/// Actions/TokenPass.cs, the data rules in ReprintCards / CardContainer, `HsServer cards --tokens`).</summary>
public class TokenPassTests
{
    private static readonly Lazy<CardDb> Db = new(() =>
    {
        string dir = CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
        SabberEngineGame.EnsureCards(dir);
        return CardDb.Load(dir);
    });

    private static readonly string[] Filler = { "CS2_118", "CS2_118", "CS2_124", "CS2_124", "CS2_201", "CS2_201", "CS2_186", "CS2_186", "CS2_168", "CS2_168" };

    /// <summary>The cards the pass found without a script: each one works now (a script, a data rule, or marked as shown only).</summary>
    private static readonly string[] FormerlyMissing =
    {
        "AT_132_SHAMANa", "AV_257t", "AV_282t", "AV_282t3", "AV_282t5", "BAR_888t", "BT_850t", "CFM_621t", "CFM_621t14", "CFM_621t15",
        "DALA_724d", "DALA_728d", "DALA_740ts4", "DALA_744d", "DALA_746d", "DALA_830", "DRG_232t", "DRG_255t2", "DRG_401d", "EDR_490t",
        "FIR_951t2", "FIR_951t3", "FIR_951t4", "GAME_002", "GAME_006", "GDB_100t2", "GDB_100t4", "GDB_100t5", "GDB_100t6", "GDB_100t7",
        "GDB_100t8", "GDB_100t9", "GVG_111t", "ICC_828t", "KARA_13_20", "KARA_13_22", "LOEA16_3", "LOEA16_4", "LOEA16_5", "LOOTA_806",
        "LOOTA_811", "LOOTA_812", "LOOTA_816", "LOOTA_819", "LOOTA_820", "LOOTA_821", "LOOTA_826", "LOOTA_827", "LOOTA_834", "LOOTA_835",
        "LOOTA_836", "LOOTA_837", "LOOTA_838", "LOOTA_839", "LOOTA_841", "LOOTA_842", "LOOTA_842b", "LOOTA_843", "LOOTA_847", "LOOT_500d",
        "Mekka1", "Mekka2", "Mekka3", "Mekka4", "Mekka4t", "NX2_037t", "NX2_037t2", "RLK_008t", "RLK_061t", "RLK_085t", "RLK_226t",
        "RLK_506t", "SCH_199t16", "SCH_199t29", "SCH_259t", "SC_999t", "SW_458t", "TB_OG_027", "TID_712t", "TID_712t2", "TIME_036t",
        "TIME_618t", "TSC_032t", "TSC_032t2", "TTN_719t", "TTN_719t1", "TTN_719t2", "TTN_719t3", "TTN_719t4", "TTN_719t5", "TTN_719t6",
        "TTN_719t7", "ULD_209t", "ULD_326t", "VAC_410t", "VAC_420t", "VAC_523t", "VAC_525t1", "VAN_EX1_tk33", "WORK_027t1", "WORK_027t2",
        "WORK_027t3", "WW_400t1", "WW_400t2", "WW_400t3", "WW_400t4", "WW_400t5", "WW_400t6", "YOD_029t", "YOP_024t",
        "DMF_064t", "DMF_248t", "REV_957t",
    };

    private static SGame Game(IEnumerable<string>? deck1 = null, IEnumerable<string>? deck2 = null)
    {
        _ = Db.Value;
        var game = new SGame(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.MAGE, Player2HeroClass = CardClass.WARRIOR,
            Player1Deck = (deck1 ?? Filler).Select(Cards.FromId).ToList(),
            Player2Deck = (deck2 ?? Filler).Select(Cards.FromId).ToList(),
            FillDecks = false, History = true, Shuffle = false, SkipMulligan = true,
        });
        game.StartGame();
        game.Player1.BaseMana = 10;
        game.Player2.BaseMana = 10;
        return game;
    }

    private static IPlayable Give(Controller c, string id) => Generic.DrawCard(c, Cards.FromId(id));

    private static Minion OnBoard(SGame game, Controller c, string id)
    {
        var m = (Minion)SabberStoneCore.Model.Entities.Entity.FromCard(c, Cards.FromId(id));
        Generic.SummonBlock.Invoke(game, m, -1, null);
        m[GameTag.NUM_TURNS_IN_PLAY] = 1;
        m.IsExhausted = false;
        return m;
    }

    private static void Play(SGame game, Controller me, string id, ICharacter? target = null)
    {
        me.UsedMana = 0;
        var card = Give(me, id);
        var task = card switch
        {
            Minion => target == null ? PlayCardTask.Minion(me, card) : PlayCardTask.MinionTarget(me, card, target),
            Weapon => PlayCardTask.Any(me, card),
            _ => target == null ? PlayCardTask.Spell(me, card) : PlayCardTask.SpellTarget(me, card, target),
        };
        Assert.True(game.Process(task), id);
    }

    private static void EndTurns(SGame game, int n)
    {
        for (int i = 0; i < n; i++)
        {
            game.CurrentPlayer.UsedMana = 0;
            Assert.True(game.Process(EndTurnTask.Any(game.CurrentPlayer)));
        }
    }

    private static void PickFirst(SGame game, Controller c)
    {
        Assert.NotNull(c.Choice);
        Assert.True(game.Process(ChooseTask.Pick(c, c.Choice!.Choices[0])));
    }

    [Fact]
    public void Every_token_the_pass_found_works()
    {
        _ = Db.Value;
        var broken = FormerlyMissing.Where(id => Cards.TryFromId(id) is not Card c || !c.Implemented).ToList();
        Assert.Empty(broken);
        // still open (reasons in the pass report): C'Thun's Old God abilities, the Dark Gift spells, Whizbang's Experimental Decks
        Assert.False(Cards.FromId("BE_013t1").Implemented);
    }

    // ------------------------------------------------------------------------------------------------ rules from tags

    [Fact]
    public void Snowman_freezes_what_it_damages_and_the_risen_leave_no_corpse()
    {
        var game = Game();
        var me = game.Player1;
        var snowman = OnBoard(game, me, "AV_282t");
        var yeti = OnBoard(game, game.Player2, "CS2_182");
        Assert.True(game.Process(MinionAttackTask.Any(me, snowman, yeti)));
        Assert.True(yeti.IsFrozen);

        int corpses = me.Corpses;
        var ghoul = OnBoard(game, me, "RLK_008t");
        Generic.Destroy(ghoul, null);
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(corpses, me.Corpses);
    }

    [Fact]
    public void Mirage_blade_makes_its_hero_immune_while_attacking()
    {
        var game = Game();
        var me = game.Player1;
        Assert.True(Cards.FromId("ULD_326t")[GameTag.IMMUNE_WHILE_ATTACKING] > 0);
        Play(game, me, "ULD_326t");
        var yeti = OnBoard(game, game.Player2, "CS2_182");
        int health = me.Hero.Health;
        Assert.True(game.Process(HeroAttackTask.Any(me, yeti)));
        Assert.Equal(health, me.Hero.Health);
        Assert.True(yeti.Damage > 0);
    }

    [Fact]
    public void A_card_printed_again_shares_its_twin_script()
    {
        _ = Db.Value;
        Assert.Same(Cards.FromId("CS2_097").Power, Cards.FromId("DRG_232t").Power);       // Truesilver Champion
        Assert.Same(Cards.FromId("NEW1_009").Power, Cards.FromId("AT_132_SHAMANa").Power); // Healing Totem
        Assert.Same(Cards.FromId("EX1_029").Power, Cards.FromId("DRG_255t2").Power);      // Leper Gnome
        Assert.Same(Cards.FromId("OG_027").Power, Cards.FromId("TB_OG_027").Power);       // Evolve
        Assert.NotNull(Cards.FromId("VAN_EX1_tk33").Power?.PowerTask);                     // INFERNO! (the "Hero Power" header)

        var game = Game();
        var me = game.Player1;
        var totem = OnBoard(game, me, "AT_132_SHAMANa");
        var yeti = OnBoard(game, me, "CS2_182");
        yeti.Damage = 2;
        EndTurns(game, 1);
        Assert.Equal(1, yeti.Damage);
        _ = totem;
    }

    // ------------------------------------------------------------------------------------------------ Kazakus

    [Fact]
    public void Kazakus_crafts_a_potion_that_casts_both_ingredients()
    {
        string[] noDuplicates = { "CS2_118", "CS2_124", "CS2_201", "CS2_186", "CS2_168", "CS2_182", "CS2_120", "CS2_119" };
        var game = Game(noDuplicates);
        var me = game.Player1;
        Play(game, me, "CFM_621");
        // the size (Lesser / Greater / Superior), then two ingredients of that size, the second offer without the first pick
        Assert.Equal(new[] { "CFM_621t11", "CFM_621t12", "CFM_621t13" }, me.Choice!.Choices.Select(id => game.IdEntityDic[id].Card.Id));
        PickFirst(game, me);
        Assert.All(me.Choice!.Choices, id => Assert.Equal(1, game.IdEntityDic[id].Card.Cost));
        string first = game.IdEntityDic[me.Choice.Choices[0]].Card.Id;
        PickFirst(game, me);
        Assert.DoesNotContain(me.Choice!.Choices, id => game.IdEntityDic[id].Card.Id == first);
        PickFirst(game, me);
        var potion = me.HandZone.Last();
        Assert.Equal("CFM_621t", potion.Card.Id);
        Assert.Equal(1, potion.Cost);
        Assert.NotNull(potion.Card.Power?.PowerTask);
        Assert.True(potion[GameTag.MODULAR_ENTITY_PART_1] > 0 && potion[GameTag.MODULAR_ENTITY_PART_2] > 0);
        // the parts in the client's ingredient order (TAG_SCRIPT_DATA_NUM_1)
        Assert.True(Cards.FromAssetId(potion[GameTag.MODULAR_ENTITY_PART_1])[GameTag.TAG_SCRIPT_DATA_NUM_1] <=
                    Cards.FromAssetId(potion[GameTag.MODULAR_ENTITY_PART_2])[GameTag.TAG_SCRIPT_DATA_NUM_1]);
    }

    // ------------------------------------------------------------------------------------------------ Gelbin's inventions

    [Fact]
    public void Gelbins_inventions_do_their_text()
    {
        var game = Game();
        var me = game.Player1;
        var repair = OnBoard(game, me, "Mekka2");
        me.Hero.Damage = 10;
        var embolden = OnBoard(game, me, "Mekka3");
        EndTurns(game, 1); // Repair Bot heals the only damaged character, Emboldener buffs a random minion
        Assert.Equal(4, me.Hero.Damage);
        Assert.Contains(me.BoardZone.Concat(game.Player2.BoardZone), m => m.AppliedEnchantments?.Any(e => e.Card.Id == "Mekka3e") == true);

        var chicken = OnBoard(game, me, "Mekka1");
        foreach (var card in me.HandZone.ToList()) me.HandZone.Remove(card);
        EndTurns(game, 1); // player 1's turn starts: Homing Chicken dies and draws 3 (plus the turn's draw)
        Assert.True(chicken.Zone?.Type != Zone.PLAY);
        Assert.Equal(4, me.HandZone.Count);
        _ = repair; _ = embolden;
    }

    [Fact]
    public void Poultryizer_turns_a_random_minion_into_a_chicken()
    {
        var game = Game();
        var me = game.Player1;
        OnBoard(game, game.Player2, "Mekka4");
        EndTurns(game, 1);
        Assert.Contains(game.Player2.BoardZone, m => m.Card.Id == "Mekka4t");
    }

    // ------------------------------------------------------------------------------------------------ Dungeon Run treasures

    [Fact]
    public void Dungeon_run_treasures_do_their_text()
    {
        var game = Game();
        var me = game.Player1;
        var op = game.Player2;

        foreach (var card in me.HandZone.ToList()) me.HandZone.Remove(card);
        Play(game, me, "LOOTA_836"); // Bag of Coins
        Assert.True(me.HandZone.IsFull);
        Assert.All(me.HandZone, p => Assert.Equal("GAME_005", p.Card.Id));
        foreach (var coin in me.HandZone.ToList()) me.HandZone.Remove(coin);

        Play(game, me, "LOOTA_838"); // Dr. Boom's Boombox
        Assert.Equal(7, me.BoardZone.Count(m => m.Card.Id == "GVG_110t"));

        Play(game, me, "LOOTA_806"); // Wand of Disintegration: the enemy board only
        var enemy = OnBoard(game, op, "CS2_182");
        Play(game, me, "LOOTA_806");
        game.DeathProcessingAndAuraUpdate();
        Assert.True(enemy.Zone?.Type != Zone.PLAY);
        Assert.Equal(7, me.BoardZone.Count);
    }

    [Fact]
    public void Boots_of_haste_orb_gloves_and_the_candle()
    {
        var game = Game();
        var me = game.Player1;
        var op = game.Player2;
        var golem = Give(me, "CS2_186");
        Play(game, me, "LOOTA_812"); // Boots of Haste
        Assert.Equal(0, golem.Cost);
        EndTurns(game, 2);
        Assert.Equal(7, golem.Cost);

        foreach (var card in op.HandZone.ToList()) op.HandZone.Remove(card);
        foreach (var card in me.HandZone.ToList()) me.HandZone.Remove(card);
        for (int i = 0; i < 4; i++) Give(op, "CS2_182");
        int opMana = op.BaseMana;
        Play(game, me, "LOOTA_811"); // Orb of Destruction
        Assert.Equal(opMana - 2, op.BaseMana);
        Assert.Equal(2, op.HandZone.Count);
        Play(game, me, "LOOTA_834"); // Gloves of Mugging: 2 left to steal
        Assert.Equal(0, op.HandZone.Count);
        Assert.Equal(2, me.HandZone.Count);

        int deck = me.DeckZone.Count;
        Play(game, me, "LOOTA_843"); // THE CANDLE: back into the deck as a new card
        Assert.Equal(deck + 1, me.DeckZone.Count);
        Assert.Contains(me.DeckZone, p => p.Card.Id == "LOOTA_843");
    }

    [Fact]
    public void Aleatoric_cube_and_scroll_of_confusion_keep_every_card()
    {
        var game = Game(Filler, Filler.Take(4));
        var me = game.Player1;
        var op = game.Player2;
        int total = me.DeckZone.Count + op.DeckZone.Count;
        Play(game, me, "LOOTA_820");
        Assert.Equal(total, me.DeckZone.Count + op.DeckZone.Count);
        Assert.True(Math.Abs(me.DeckZone.Count - op.DeckZone.Count) <= 1);
        Assert.All(me.DeckZone, p => Assert.Equal(me, p.Controller));
        Assert.All(op.DeckZone, p => Assert.Equal(op, p.Controller));
        Assert.Contains(me.DeckZone, p => p.AppliedEnchantments?.Any(e => e.Card.Id == "LOOTA_820e") == true);

        for (int i = 0; i < 3; i++) { OnBoard(game, me, "CS2_182"); OnBoard(game, op, "CS2_182"); }
        Play(game, me, "LOOTA_839");
        Assert.Equal(6, me.BoardZone.Count + op.BoardZone.Count);
    }

    [Fact]
    public void Greedy_pickaxe_and_quel_delar()
    {
        var game = Game(new[] { "LOOTA_842a", "LOOTA_842b" }.Concat(Filler));
        var me = game.Player1;
        // Start of Game: Blade + Hilt in the deck become Quel'Delar in the hand (wiki Hilt of Quel'Delar)
        Assert.DoesNotContain(me.DeckZone.Concat(me.HandZone), p => p.Card.Id is "LOOTA_842a" or "LOOTA_842b");
        Assert.Contains(me.HandZone, p => p.Card.Id == "LOOTA_842");

        Play(game, me, "LOOTA_835");
        var yeti = OnBoard(game, game.Player2, "CS2_182");
        me.BaseMana = 5;
        Assert.True(game.Process(HeroAttackTask.Any(me, yeti)));
        Assert.Equal(6, me.BaseMana);
    }

    // ------------------------------------------------------------------------------------------------ others

    [Fact]
    public void Party_elemental_brings_a_party_and_the_mime_is_silenced()
    {
        var game = Game();
        var me = game.Player1;
        OnBoard(game, me, "KARA_13_20");
        Assert.Equal(2, me.BoardZone.Count(m => m.Card.Id == "TB_KaraPortals_003"));
        var mime = OnBoard(game, me, "KARA_13_22");
        Assert.True(mime.IsSilenced);
    }

    [Fact]
    public void Rafaams_artifacts()
    {
        var game = Game();
        var me = game.Player1;
        var yeti = OnBoard(game, me, "CS2_182");
        Play(game, me, "LOEA16_3", yeti);
        Assert.Equal(14, yeti.AttackDamage);
        Play(game, me, "LOEA16_5");
        Assert.True(me.BoardZone.IsFull);
        Assert.Equal(6, me.BoardZone.Count(m => m.Card.Id == "LOEA16_5t"));
        int health = game.Player2.Hero.Health;
        Play(game, me, "LOEA16_4");
        Assert.Equal(health - 10, game.Player2.Hero.Health); // no enemy minions: all ten at the hero
    }

    [Fact]
    public void Snake_eyes_dice_discover_a_card_of_their_cost()
    {
        var game = Game();
        var me = game.Player1;
        Play(game, me, "WW_400t3");
        Assert.NotNull(me.Choice);
        Assert.All(me.Choice!.Choices, id => Assert.Equal(3, game.IdEntityDic[id].Card.Cost));
    }
}
