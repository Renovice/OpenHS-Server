using HsServer.Engine;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;

namespace HsServer.Tests;

/// <summary>Therazane the Earthmother: "Your next spell of each school casts twice" — one charge per school (a BE_337e2 on the player,
/// its school in TAG_SCRIPT_DATA_NUM_1), spent by the first spell of that school (live session-20261023-152211).</summary>
public class SchoolCastsTwiceTests
{
    [Fact]
    public void Each_school_has_one_charge_and_the_first_spell_of_it_spends_it()
    {
        SabberEngineGame.EnsureCards(CardDb.FindDefaultDir(TestEnvironment.ProjectRoot));
        var game = new SabberStoneCore.Model.Game(new SabberStoneCore.Config.GameConfig { StartPlayer = 1, FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false, SkipMulligan = true });
        game.StartGame();
        var me = game.CurrentPlayer;
        var therazane = (Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("BE_337"));
        me.BoardZone.Add(therazane);
        Generic.GiveSchoolsCastTwice(me, therazane, Cards.FromId("BE_337e2"), Cards.TryFromId("BE_337e"));
        Assert.Equal(Generic.SpellSchools, me.AppliedEnchantments.Count(e => e.Card.Id == "BE_337e2"));

        var bolt = (Spell)SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("CORE_EX1_238")); // Lightning Bolt (Nature)
        int school = bolt[(GameTag)1635];
        Assert.True(school > 0);
        Assert.True(Generic.SchoolSpellCastsTwice(me, bolt));
        Assert.False(Generic.SchoolSpellCastsTwice(me, bolt)); // spent
        Assert.Equal(Generic.SpellSchools - 1, me.AppliedEnchantments.Count(e => e.Card.Id == "BE_337e2"));
    }
}
