using HsServer.Engine;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;
using SGame = SabberStoneCore.Model.Game;

namespace HsServer.Tests;

/// <summary>Engine rules `HsServer livecheck` found different from live recordings (pass-lc-engine, 2026-10-10).</summary>
public class LiveCheckEngineFixTests
{
    private static readonly Lazy<CardDb> Db = new(() =>
    {
        string dir = CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
        SabberEngineGame.EnsureCards(dir);
        return CardDb.Load(dir);
    });

    private static readonly string[] Filler = { "CS2_118", "CS2_118", "CS2_124", "CS2_124", "CS2_201", "CS2_201", "CS2_186", "CS2_186", "CS2_168", "CS2_168" };

    private static SGame Game(CardClass p1 = CardClass.MAGE, CardClass p2 = CardClass.WARRIOR, FormatType format = FormatType.FT_WILD)
    {
        _ = Db.Value;
        var game = new SGame(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = p1, Player2HeroClass = p2, FormatType = format,
            Player1Deck = Filler.Select(Cards.FromId).ToList(), Player2Deck = Filler.Select(Cards.FromId).ToList(),
            FillDecks = false, History = true, Shuffle = false, SkipMulligan = true,
        });
        game.StartGame();
        game.Player1.BaseMana = 10;
        game.Player1.UsedMana = 0;
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

    private static int[] OfferedCosts(SGame game, Controller c) =>
        c.Choice.Choices.Select(id => game.IdEntityDic[id].Card.Cost).ToArray();

    [Fact]
    public void Discover_a_cost_uses_its_own_cost_not_an_earlier_discovers()
    {
        // DiscoverTask's pool cache ignored the criteria's value: Jeweled Scarab's 3-Cost pool came back for Dark Peddler's 1-Cost
        // (and JAIL_451's 5-Cost minion pool for Belfry Bully's... any later "Discover an N-Cost" card)
        var game = Game();
        var me = game.Player1;
        IPlayable scarab = Generic.DrawCard(me, Cards.FromId("LOE_029")); // Discover a 3-Cost card
        Assert.True(game.Process(PlayCardTask.Minion(me, scarab)));
        Assert.All(OfferedCosts(game, me), cost => Assert.Equal(3, cost));
        Assert.True(game.Process(ChooseTask.Pick(me, me.Choice.Choices[0])));
        IPlayable peddler = Generic.DrawCard(me, Cards.FromId("LOE_023")); // Discover a 1-Cost card
        Assert.True(game.Process(PlayCardTask.Minion(me, peddler)));
        Assert.All(OfferedCosts(game, me), cost => Assert.Equal(1, cost));
    }

    [Fact]
    public void Lord_Marrowgars_cards_follow_their_text()
    {
        // live session-20261007-190857 (Lord Marrowgar): the ICCA cards were unscripted and did nothing
        var game = Game();
        var me = game.Player1;
        me.Hero.Damage = 11;
        Cards.FromId("ICCA06_002p").Power.PowerTask.Process(game, me, me.Hero, null); // Restore your hero to full Health
        Assert.Equal(0, me.Hero.Damage);

        var theirs = OnBoard(game, me.Opponent, "CS2_186"); // War Golem 7/7
        int hand = me.HandZone.Count;
        IPlayable storm = Generic.DrawCard(me, Cards.FromId("ICCA06_004")); // Deal 1 damage to all enemies. Draw a card.
        Assert.True(game.Process(PlayCardTask.Spell(me, storm)));
        Assert.Equal(1, theirs.Damage);
        Assert.Equal(1, me.Opponent.Hero.Damage);
        Assert.Equal(hand + 1, me.HandZone.Count); // (the storm left, a card came)

        var knight = OnBoard(game, me, "ICCA11_001"); // Deathrattle: Add a Knights of the Frozen Throne card to your opponent's hand
        int theirHand = me.Opponent.HandZone.Count;
        IPlayable fireball = Generic.DrawCard(me, Cards.FromId("CS2_029"));
        Assert.True(game.Process(PlayCardTask.SpellTarget(me, fireball, knight)));
        Assert.Equal(theirHand + 1, me.Opponent.HandZone.Count);
        Assert.Equal(CardSet.ICECROWN, me.Opponent.HandZone[^1].Card.Set);
    }

    [Fact]
    public void A_copied_deathrattle_with_nothing_captured_does_nothing()
    {
        // ActivateCapturedDeathrattleTask threw when its enchantment held no card (livecheck: Unearthed Raptor's LOE_019e rebuilt)
        var game = Game();
        var me = game.Player1;
        var raptor = OnBoard(game, me, "LOE_019");
        Generic.AddEnchantmentBlock(game, Cards.FromId("LOE_019e"), raptor, raptor);
        IPlayable fireball = Generic.DrawCard(me, Cards.FromId("CS2_029"));
        Assert.True(game.Process(PlayCardTask.SpellTarget(me, fireball, raptor)));
        Assert.Equal(Zone.GRAVEYARD, raptor.Zone.Type);
    }

    [Fact]
    public void Tiny_Pal_offers_its_ammunition_when_played()
    {
        // live session-20261001-130515 82129: Tiny Pal's Battlecry offers the 4 ammunitions; the pick transforms the played weapon.
        // Ours offered nothing: the Battlecry looked at the hero's weapon, which it isn't before the Equipping Phase (rule book).
        var game = Game(CardClass.SHAMAN);
        var me = game.Player1;
        IPlayable pal = Generic.DrawCard(me, Cards.FromId("JAIL_458"));
        Assert.True(game.Process(PlayCardTask.Any(me, pal)));
        Assert.NotNull(me.Choice);
        Assert.Equal(4, me.Choice.Choices.Count);
        int pick = me.Choice.Choices.First(id => game.IdEntityDic[id].Card.Id == "JAIL_458t1");
        Assert.True(game.Process(ChooseTask.Pick(me, pick)));
        Assert.Equal("JAIL_458t1", me.Hero.Weapon?.Card.Id);
        Assert.Equal(pal.Id, me.Hero.Weapon.Id);
    }

    [Theory]
    [InlineData("MEND_046t")] // Bashana's Treant (live session-20261005-140614 35272: played with its spell's target)
    [InlineData("TSC_052t")]  // Nagaling (wiki School Teacher: "Nagaling can target if the spell can target")
    [InlineData("TIME_704t")] // Pupil
    [InlineData("TOY_504t")]  // Hagatha's Slime
    [InlineData("DAL_431t")]  // Drustvar Horror (wiki: "you choose the target when you play the minion")
    public void A_carried_spell_is_cast_at_the_target_the_carrier_was_played_with(string carrierId)
    {
        var game = Game();
        var me = game.Player1;
        var golem = OnBoard(game, me.Opponent, "CS2_186"); // War Golem 7/7
        OnBoard(game, me.Opponent, "CS2_186");
        IPlayable carrier = Generic.DrawCard(me, Cards.FromId(carrierId));
        IPlayable fireball = SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("CS2_029"), null, me.SetasideZone); // Deal 6 damage
        Generic.CarrySpell(carrier, fireball);
        Assert.Equal(Cards.FromId("CS2_029").AssetId, carrier[GameTag.ADDITIONAL_PLAY_REQS_1]);
        // the options aim it like the spell; it can't be played without a target while one is there (a targeted battlecry)
        Assert.Contains(me.Options(), o => o is PlayCardTask pt && pt.Source == carrier && pt.Target == golem);
        Assert.False(((Playable)carrier).IsValidPlayTarget(null));
        Assert.True(game.Process(PlayCardTask.MinionTarget(me, carrier, golem)));
        Assert.Equal(6, golem.Damage);
    }

    [Fact]
    public void A_carrier_with_no_target_available_is_still_played()
    {
        var game = Game();
        var me = game.Player1;
        IPlayable carrier = Generic.DrawCard(me, Cards.FromId("TSC_052t"));
        // "Deal 2 damage to a minion" with no minion anywhere: the Battlecry takes a target only when one is available
        IPlayable spell = SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("CS2_057"), null, me.SetasideZone); // Shadow Bolt
        Generic.CarrySpell(carrier, spell);
        Assert.True(carrier.IsPlayable);
        Assert.True(game.Process(PlayCardTask.Minion(me, carrier)));
        Assert.NotEqual(Zone.HAND, carrier.Zone.Type); // (played; its spell then picks a random target — the Nagaling itself here)
    }

    [Fact]
    public void Red_Herrings_aura_gives_Stealth_that_attacking_does_not_end()
    {
        // REV_014 "Your non-Red Herring minions have Stealth" (live session-20261007-190857: the played minion gets Stealth, keeps it
        // after attacking, loses it when the Herring dies); ours never gave it (HasStealth read only the minion's own Stealth)
        var game = Game();
        var me = game.Player1;
        var herring = OnBoard(game, me, "REV_014");
        IPlayable yeti = Generic.DrawCard(me, Cards.FromId("CS2_182"));
        Assert.True(game.Process(PlayCardTask.Minion(me, yeti)));
        var minion = (Minion)yeti;
        Assert.True(minion.HasStealth);
        Assert.False(herring.HasStealth);
        minion.IsExhausted = false;
        minion[GameTag.NUM_TURNS_IN_PLAY] = 1;
        Assert.True(game.Process(MinionAttackTask.Any(me, minion, me.Opponent.Hero)));
        Assert.True(minion.HasStealth); // the aura's Stealth stays
        IPlayable fireball = Generic.DrawCard(me, Cards.FromId("CS2_029"));
        herring.Damage = herring.Health - 1;
        Assert.True(game.Process(PlayCardTask.SpellTarget(me, fireball, herring)));
        Assert.False(minion.HasStealth);
    }

    [Fact]
    public void The_Heist_Gallywix_does_not_buy_Gallywixs_Coin()
    {
        // DALA_BOSS_57p "Buy a copy of the first card your opponent plays each turn for a Coin"; GVG_028t "(Won't trigger Gallywix.)":
        // live session-20261005-140614 74794-75011 — two Coins played uncopied, the third card is the one bought
        var game = Game();
        var me = game.Player1;
        new SabberStoneCore.Tasks.SimpleTasks.ReplaceHeroPower(Cards.FromId("DALA_BOSS_57p")).Process(game, me.Opponent, me.Opponent.Hero, null);
        int theirHand = me.Opponent.HandZone.Count;
        IPlayable coin = Generic.DrawCard(me, Cards.FromId("GVG_028t"));
        Assert.True(game.Process(PlayCardTask.Spell(me, coin)));
        Assert.Equal(theirHand, me.Opponent.HandZone.Count);
        IPlayable yeti = Generic.DrawCard(me, Cards.FromId("CS2_182"));
        Assert.True(game.Process(PlayCardTask.Minion(me, yeti)));
        Assert.Equal(theirHand + 1, me.Opponent.HandZone.Count); // the Yeti is the first card bought
        Assert.Equal("CS2_182", me.Opponent.HandZone[^1].Card.Id);
    }

    [Theory]
    [InlineData("BRM_022", "BRM_022t")] // Dragon Egg: "summon a 2/1 Whelp" (live 140614 2833: Black Whelp 2/1; ours Nefarian's 1/1 Whelp)
    [InlineData("DMF_703", "DMF_706t")] // Pit Master: "Summon a 3/2 Duelist" (live 20260930-212533 1265: Pavilion Duelist)
    [InlineData("UNG_208", "UNG_208t")] // Stone Sentinel: "summon two 2/3 Elementals with Taunt" (ours: two 1/2 Flame Elementals)
    public void Summoned_tokens_are_the_ones_the_text_names(string cardId, string tokenId)
    {
        var game = Game();
        var me = game.Player1;
        me.NumElementalsPlayedLastTurn = 1;
        if (cardId == "BRM_022")
        {
            OnBoard(game, me, cardId); // (0/2: the opponent's Arcane Explosion hits it for 1)
            Assert.True(game.Process(EndTurnTask.Any(me)));
            me.Opponent.BaseMana = 10; me.Opponent.UsedMana = 0;
            IPlayable theirs = Generic.DrawCard(me.Opponent, Cards.FromId("CS2_025"));
            Assert.True(game.Process(PlayCardTask.Spell(me.Opponent, theirs)));
        }
        else
        {
            IPlayable card = Generic.DrawCard(me, Cards.FromId(cardId));
            Assert.True(game.Process(PlayCardTask.Minion(me, card)));
        }
        Assert.Contains(me.BoardZone, m => m.Card.Id == tokenId);
        Assert.DoesNotContain(me.BoardZone, m => m.Card.Id is "BRM_004t" or "DMF_703t2" or "UNG_809t1");
    }

    [Fact]
    public void A_Permanent_or_Dormant_minion_is_not_in_your_minions_auras_until_it_awakens()
    {
        // Advanced rulebook BE2: "Permanents and Dormant minions are not considered minions and cannot be affected by any effects from
        // either player unless otherwise specified" (live session-20261007-190857: Red Herring's Stealth never on The Twisting Nether)
        var game = Game();
        var me = game.Player1;
        OnBoard(game, me, "REV_014");
        var nether = OnBoard(game, me, "TTN_960t"); // a Permanent
        game.DeathProcessingAndAuraUpdate();
        Assert.False(nether.HasStealth);
        var yeti = OnBoard(game, me, "CS2_182");
        game.DeathProcessingAndAuraUpdate();
        Assert.True(yeti.HasStealth);
        Generic.MakeDormant(yeti);
        game.DeathProcessingAndAuraUpdate();
        Assert.False(yeti.HasStealth); // asleep: out of the aura
        Generic.Awaken(yeti);
        game.DeathProcessingAndAuraUpdate();
        Assert.True(yeti.HasStealth); // awake: back in it
    }

    [Fact]
    public void Ysera_Awakens_spares_every_Ysera()
    {
        // "except Ysera": every card the client tags a Ysera (4392), not only the one named "Ysera" (live session-20260930-125547 13769:
        // Ysera the Dreamer CS3_033 undamaged)
        var game = Game();
        var me = game.Player1;
        var dreamer = OnBoard(game, me, "CS3_033");
        var golem = OnBoard(game, me.Opponent, "CS2_186"); // War Golem 7/7
        IPlayable awakens = Generic.DrawCard(me, Cards.FromId("DREAM_02"));
        Assert.True(game.Process(PlayCardTask.Spell(me, awakens)));
        Assert.Equal(0, dreamer.Damage);
        Assert.Equal(5, golem.Damage);
    }

    [Fact]
    public void A_hero_aura_slot_is_not_a_minion_keyword()
    {
        // Fallen Hero's "Your Hero Power deals 1 extra damage" (HEROPOWER_DAMAGE, the hero's slot 6) read as Taunt on the hero
        // (live session-20261002-145651 5025: no Taunt)
        var game = Game();
        var me = game.Player1;
        IPlayable hero = Generic.DrawCard(me, Cards.FromId("CORE_AT_003"));
        Assert.True(game.Process(PlayCardTask.Minion(me, hero)));
        Assert.False(me.Hero.HasTaunt);
        Assert.Equal(30, me.Hero.BaseHealth);
    }

    [Fact]
    public void Brukans_invocation_swaps_at_the_end_of_his_owners_turn()
    {
        // live session-20260930-071519 23841: the Hero Power's TRIGGER block at MAIN_END swaps to one of the other three
        var game = Game(CardClass.SHAMAN);
        var me = game.Player1;
        new SabberStoneCore.Tasks.SimpleTasks.ReplaceHeroPower(Cards.FromId("AV_258pt")).Process(game, me, me.Hero, null);
        Assert.True(game.Process(EndTurnTask.Any(me)));
        string after = me.Hero.HeroPower.Card.Id;
        Assert.NotEqual("AV_258pt", after);
        Assert.Contains(after, new[] { "AV_258p2", "AV_258pt3", "AV_258pt4" });
        Assert.True(game.Process(EndTurnTask.Any(me.Opponent)));
        Assert.Equal(after, me.Hero.HeroPower.Card.Id); // not at the start of his turn
    }

    [Theory]
    [InlineData("CFM_603")] // Potion of Madness (live session-20261003-184102 2538: NON_KEYWORD_CHARGE 1, no CHARGE)
    [InlineData("EX1_334")] // Shadow Madness
    public void A_minion_stolen_until_end_of_turn_can_attack_without_the_Charge_keyword(string cardId)
    {
        var game = Game(CardClass.PRIEST);
        var me = game.Player1;
        var theirs = OnBoard(game, me.Opponent, "CS2_120"); // River Crocolisk 2/3
        IPlayable potion = Generic.DrawCard(me, Cards.FromId(cardId));
        Assert.True(game.Process(PlayCardTask.SpellTarget(me, potion, theirs)));
        Assert.Equal(me, theirs.Controller);
        Assert.Equal(0, theirs[GameTag.CHARGE]); // no keyword
        Assert.Equal(1, theirs[Generic.NON_KEYWORD_CHARGE]);
        Assert.True(game.Process(MinionAttackTask.Any(me, theirs, me.Opponent.Hero)));
        Assert.True(game.Process(EndTurnTask.Any(me)));
        Assert.Equal(me.Opponent, theirs.Controller);
        Assert.Equal(0, theirs[Generic.NON_KEYWORD_CHARGE]);
    }

    // ---------------------------------------------------------------- pass-lc-fix2 (2026-10-10)

    private static IPlayable KillWithFireballs(SGame game, Controller me, ICharacter victim)
    {
        for (int i = 0; i < 3 && victim.Zone?.Type == Zone.PLAY; i++)
        {
            me.UsedMana = 0;
            IPlayable fireball = Generic.DrawCard(me, Cards.FromId("CS2_029"));
            Assert.True(game.Process(PlayCardTask.SpellTarget(me, fireball, victim)));
        }
        return victim;
    }

    [Theory]
    [InlineData("LEG_RLK_082", Zone.HAND)] // Deathbringer Saurfang: Deathrattle: Return this to your hand (live 20261004-163220 4036)
    [InlineData("RLK_082", Zone.HAND)]
    [InlineData("DALA_713", Zone.DECK)]    // Elder Taggawag: Deathrattle: Shuffle this into your deck (live 20261002-204752 94487)
    [InlineData("CFM_095", Zone.DECK)]     // Weasel Tunneler: into the opponent's deck (live 20261007-144826 176113)
    public void A_dead_card_brought_back_as_itself_is_set_aside_and_a_new_card_comes_back(string id, Zone to)
    {
        // user decision 2026-10-10 ("follow live"): the dead card leaves the Graveyard for SETASIDE; the card that comes back is new
        var game = Game();
        var me = game.Player1;
        var dead = OnBoard(game, me, id);
        KillWithFireballs(game, me, dead);
        Assert.Equal(Zone.SETASIDE, dead.Zone?.Type);
        Assert.Contains(dead.Id, me.DiedThisGame); // it still died this game
        Controller owner = id == "CFM_095" ? me.Opponent : me;
        IPlayable back = (to == Zone.HAND ? owner.HandZone.GetAll() : owner.DeckZone.GetAll()).Single(p => p.Card.Id == id);
        Assert.NotEqual(dead.Id, back.Id);
    }

    [Theory]
    [InlineData("TRL_306")] // Immortal Prelate: Shuffle this into your deck. It keeps any enchantments. (live 20261002-204752 54093)
    [InlineData("VAC_958")] // Adaptive Amalgam (live 20261007-144826 154418)
    public void A_minion_shuffled_back_with_its_enchantments_ends_in_the_Graveyard_like_live(string id)
    {
        // live: DECK -> SETASIDE -> GRAVEYARD in its Deathrattle block (2 of 2); the new copy in the deck
        var game = Game();
        var me = game.Player1;
        var dead = OnBoard(game, me, id);
        KillWithFireballs(game, me, dead);
        Assert.Equal(Zone.GRAVEYARD, dead.Zone?.Type);
        Assert.Contains(me.DeckZone.GetAll(), p => p.Card.Id == id && p.Id != dead.Id);
    }

    [Fact]
    public void Kingsbane_sets_the_dead_weapon_aside_and_shuffles_a_new_one_with_its_enchantments()
    {
        // live session-20261003-185040 18204: Kingsbane id 33 GRAVEYARD -> DEATHRATTLE -> SETASIDE, a new entity shuffled in
        var game = Game(CardClass.ROGUE);
        var me = game.Player1;
        IPlayable bane = Generic.DrawCard(me, Cards.FromId("LOOT_542"));
        Assert.True(game.Process(PlayCardTask.Any(me, bane)));
        IPlayable poison = Generic.DrawCard(me, Cards.FromId("CS2_074")); // Deadly Poison: +2 Attack
        Assert.True(game.Process(PlayCardTask.Any(me, poison)));
        var weapon = me.Hero.Weapon;
        Generic.Destroy(weapon, me.Opponent.Hero);
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(Zone.SETASIDE, weapon.Zone?.Type);
        IPlayable back = me.DeckZone.GetAll().Single(p => p.Card.Id == "LOOT_542");
        Assert.NotEqual(weapon.Id, back.Id);
        Assert.Equal(3, back[GameTag.ATK]); // it keeps its enchantments (1 + Deadly Poison's 2)
    }

    [Fact]
    public void A_given_return_this_deathrattle_and_Lucentbark_set_the_dead_aside_but_a_resurrect_leaves_it()
    {
        var game = Game(CardClass.DRUID);
        var me = game.Player1;
        // Elixir of Hope LOOT_278t4: +2/+2 and "Deathrattle: Return this minion to your hand" (live 20261004-224614 79240)
        var rager = OnBoard(game, me, "CS2_118");
        IPlayable elixir = Generic.DrawCard(me, Cards.FromId("LOOT_278t4"));
        Assert.True(game.Process(PlayCardTask.SpellTarget(me, elixir, rager)));
        KillWithFireballs(game, me, rager);
        Assert.Equal(Zone.SETASIDE, rager.Zone?.Type);
        Assert.Contains(me.HandZone.GetAll(), p => p.Card.Id == "CS2_118" && p.Id != rager.Id);

        // Lucentbark DAL_357: Deathrattle: go Dormant (live 20261005-140614 49729-49763: the spirit, then SETASIDE)
        var bark = OnBoard(game, me, "DAL_357");
        KillWithFireballs(game, me, bark);
        Assert.Equal(Zone.SETASIDE, bark.Zone?.Type);
        Assert.Contains(game.IdEntityDic.Values, p => p.Card.Id == "DAL_357t" && p.Zone?.Type == Zone.PLAY && p.Controller == me); // (Dormant: not in GetAll)

        // a resurrect ("summon a minion that died": Reborn) leaves the dead one in the Graveyard
        var mummy = OnBoard(game, me, "ULD_217"); // Micro Mummy: Reborn
        KillWithFireballs(game, me, mummy);
        Assert.Equal(Zone.GRAVEYARD, mummy.Zone?.Type);
    }

    private static void Cast(SGame game, Controller me, string id)
    {
        me.UsedMana = 0;
        IPlayable spell = Generic.DrawCard(me, Cards.FromId(id));
        Assert.True(game.Process(PlayCardTask.Spell(me, spell)));
    }

    [Fact]
    public void Reach_Equilibrium_puts_both_of_its_Quests_up_and_each_counts_its_own_spells()
    {
        // live session-20261001-130515 13259: TLC_817 set aside, TLC_817t + TLC_817t2 up as two Quests (the One Quest exception)
        var game = Game(CardClass.PRIEST);
        var me = game.Player1;
        IPlayable card = Generic.DrawCard(me, Cards.FromId("TLC_817"));
        Assert.True(game.Process(PlayCardTask.Spell(me, card)));
        Assert.Equal(Zone.SETASIDE, card.Zone?.Type);
        Assert.Equal(new[] { "TLC_817t", "TLC_817t2" }, me.SecretZone.Quests.Select(q => q.Card.Id).ToArray());
        Assert.All(me.SecretZone.Quests, q => Assert.Equal(card.Id, q[GameTag.CREATOR]));
        Spell holy = me.SecretZone.Quests[0], shadow = me.SecretZone.Quests[1];

        Cast(game, me, "DS1_233"); // Mind Blast: a Shadow spell
        Assert.Equal(0, holy.QuestProgress);
        Assert.Equal(1, shadow.QuestProgress);
        for (int i = 0; i < 6; i++) Cast(game, me, "CS1_112"); // Holy Nova x6 (2026-11-20 patch 37.0.0.256589: "Cast 6 Holy spells", was 4)
        Assert.False(me.SecretZone.IsActiveQuest(holy)); // done: its reward
        Assert.True(me.SecretZone.IsActiveQuest(shadow));
        Assert.Equal(1, shadow.QuestProgress);
        Assert.Contains(me.HandZone.GetAll(), p => p.Card.Id == "TLC_817t3"); // Sol'etos, Life's Breath
        Assert.DoesNotContain(me.HandZone.GetAll(), p => p.Card.Id == "TLC_817t4");

        // another Quest still can't be played while one is up
        IPlayable other = Generic.DrawCard(me, Cards.FromId("UNG_028"));
        me.UsedMana = 0;
        Assert.False(game.Process(PlayCardTask.Spell(me, other)));
    }

    [Fact]
    public void Void_Terror_next_to_one_minion_destroys_it_and_gains_its_current_stats()
    {
        // live session-20261006-145846 5344: one neighbour — its stats gained; ours stopped the Battlecry at the missing second one
        // (GetGameTagTask: an entity index past a non-empty list now reads 0). Live 8234: a damaged neighbour gives its CURRENT Health
        // (rulebook H: "the value of its current Health")
        var game = Game(CardClass.WARLOCK);
        var me = game.Player1;
        var golem = OnBoard(game, me, "CS2_186"); // War Golem 7/7
        golem.Damage = 4;
        IPlayable terror = Generic.DrawCard(me, Cards.FromId("EX1_304"));
        Assert.True(game.Process(PlayCardTask.Minion(me, terror))); // (right of the Golem: one neighbour)
        var vt = (Minion)terror;
        Assert.True(golem.IsDead || golem.Zone?.Type == Zone.GRAVEYARD);
        Assert.Equal(vt.Card.ATK + 7, vt.AttackDamage);
        Assert.Equal(vt.Card.Health + 3, vt.Health);
    }

    [Fact]
    public void Mugs_Magic_lasts_until_a_minion_is_played_and_a_Location_is_not_one()
    {
        // live session-20260929-171955: JAIL_800hp1e kept through a turn with no minion and the opponent's turn (1761), one at a time;
        // live session-20260930-071519 9819: a Location played doesn't use it (wiki Location: "locations are not classified as minions")
        var game = Game();
        var me = game.Player1;
        new SabberStoneCore.Tasks.SimpleTasks.AddEnchantmentTask("JAIL_800hp1e", SabberStoneCore.Tasks.SimpleTasks.EntityType.CONTROLLER).Process(game, me, me.Hero, null);
        IPlayable rager = Generic.DrawCard(me, Cards.FromId("CS2_118")); // 3 Mana
        IPlayable location = Generic.DrawCard(me, Cards.FromId("CORE_REV_290")); // Cathedral of Atonement, a Location
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(1, rager.Cost);
        Assert.Equal(location.Card.Cost, location.Cost); // not discounted: not a minion
        Assert.True(game.Process(EndTurnTask.Any(me)));
        Assert.Equal(1, rager.Cost); // kept through the turn end
        Assert.True(game.Process(EndTurnTask.Any(me.Opponent)));
        Assert.Equal(1, rager.Cost);
        me.UsedMana = 0;
        Assert.True(game.Process(me.Options().OfType<PlayCardTask>().First(o => o.Source == location)));
        Assert.Equal(1, rager.Cost); // a Location isn't "your first minion"
        Assert.True(game.Process(PlayCardTask.Minion(me, rager)));
        IPlayable next = Generic.DrawCard(me, Cards.FromId("CS2_118"));
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(3, next.Cost); // used by the minion
        Assert.False(SabberStoneCore.Conditions.SelfCondition.IsMinion.Eval(location));
    }

    [Fact]
    public void A_Health_cost_is_Health_lost_with_Armor_untouched()
    {
        // live session-20260929-122856 26858 (and 74 more): DAMAGE +3, ARMOR 6 kept; wiki Change_Cost_to_Health (patch 26.0): "always
        // removes health only and ignores armor"; Immune doesn't prevent it
        var game = Game();
        var me = game.Player1;
        me.Hero.Armor = 6;
        IPlayable rager = Generic.DrawCard(me, Cards.FromId("CS2_118"));
        rager[GameTag.CARD_COSTS_HEALTH] = 1;
        Assert.True(game.Process(PlayCardTask.Minion(me, rager)));
        Assert.Equal(6, me.Hero.Armor);
        Assert.Equal(3, me.Hero.Damage);
        Assert.Equal(0, me.UsedMana);
        me.Hero.IsImmune = true;
        IPlayable second = Generic.DrawCard(me, Cards.FromId("CS2_118"));
        second[GameTag.CARD_COSTS_HEALTH] = 1;
        Assert.True(game.Process(PlayCardTask.Minion(me, second)));
        Assert.Equal(6, me.Hero.Damage);
    }

    [Fact]
    public void Died_this_turn_reads_the_engines_died_record_not_the_graveyard()
    {
        // ReqFriendlyMinionOfRaceDiedThisTurn walked the Graveyard back by the died-this-turn count and ran past its start; DiedThisTurn
        // took the last N entries of the died record, a destroyed weapon included
        var game = Game(CardClass.HUNTER);
        var me = game.Player1;
        var raptor = OnBoard(game, me, "CS2_172"); // Bloodfen Raptor, a Beast
        KillWithFireballs(game, me, raptor);
        IPlayable axe = Generic.DrawCard(me, Cards.FromId("CS2_106")); // Fiery War Axe
        Assert.True(game.Process(PlayCardTask.Any(me, axe)));
        Generic.Destroy(me.Hero.Weapon, me.Opponent.Hero);
        game.DeathProcessingAndAuraUpdate();
        Assert.Equal(new[] { raptor.Id }, Generic.DiedThisTurn(me).Select(p => p.Id).ToArray());
        var beastDied = SabberStoneCore.Loader.TargetingPredicates.ReqFriendlyMinionOfRaceDiedThisTurn(Race.BEAST);
        Assert.True(beastDied(me, Cards.FromId("CS2_172")));
        me.NumFriendlyMinionsThatDiedThisTurn = 5; // more than the Graveyard's marked minions (a rebuilt game, a set-aside dead card)
        Assert.True(beastDied(me, Cards.FromId("CS2_172")));
        Assert.False(SabberStoneCore.Loader.TargetingPredicates.ReqFriendlyMinionOfRaceDiedThisTurn(Race.DRAGON)(me, Cards.FromId("CS2_172")));
    }

    [Fact]
    public void Didnt_start_in_your_deck_is_the_engines_first_generated_id_rule()
    {
        // Open the Waygate / Leyline Manipulator read SelfCondition.IsNotStartInDeck, which guessed from the deck sizes (id > both decks + 7)
        var game = Game(CardClass.MAGE);
        var me = game.Player1;
        IPlayable fromDeck = me.DeckZone[0];
        IPlayable made = Generic.DrawCard(me, Cards.FromId("CS2_029"));
        typeof(Controller).GetProperty(nameof(Controller.DeckCards))!.SetValue(me, new List<Card>()); // the guess's input changes...
        Assert.False(SabberStoneCore.Conditions.SelfCondition.IsNotStartInDeck.Eval(fromDeck)); // ...the rule doesn't
        Assert.True(SabberStoneCore.Conditions.SelfCondition.IsNotStartInDeck.Eval(made));
        Assert.Equal(Generic.DidntStartInDeck(fromDeck), SabberStoneCore.Conditions.SelfCondition.IsNotStartInDeck.Eval(fromDeck));
    }
}
