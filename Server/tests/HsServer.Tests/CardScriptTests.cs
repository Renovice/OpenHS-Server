using HsServer.Engine;

namespace HsServer.Tests;

public class CardScriptTests
{
    [Theory]
    [InlineData("CORE_CS2_024", "CS2_024")] // Core Frostbolt
    [InlineData("VAN_CS2_029", "CS2_029")]  // Legacy Fireball
    [InlineData("TUTR_CS2_023", "CS2_023")] // tutorial Arcane Intellect
    [InlineData("CS2_008", "CS2_008")]
    public void Reprints_use_the_original_script(string cardId, string expected) =>
        Assert.Equal(expected, CardScripts.Canon(cardId));

    [Fact]
    public void Core_reprint_spells_are_playable()
    {
        CardDb db = CardDb.Load(CardDb.FindDefaultDir(TestEnvironment.ProjectRoot));
        foreach (string id in new[] { "CORE_CS2_024", "CORE_CS2_023", "CORE_CS2_029" })
            Assert.True(CardScripts.IsSupported(db[id]), id);
    }
}
