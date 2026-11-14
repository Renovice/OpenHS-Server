namespace HsServer.Engine;

/// <summary>Starter decks built only from cards the engine fully supports (vanilla/keyword minions + scripted cards).</summary>
internal static class Decks
{
    private static readonly string[] Basic =
    {
        "CS2_168", "CS2_168", // Murloc Raider 2/1
        "CS2_172", "CS2_172", // Bloodfen Raptor 3/2
        "CS2_120", "CS2_120", // River Crocolisk 2/3
        "CS2_189", "CS2_189", // Elven Archer (battlecry: 1 damage)
        "EX1_015", "EX1_015", // Novice Engineer (battlecry: draw)
        "CS2_124", "CS2_124", // Wolfrider 3/1 Charge
        "CS2_118", "CS2_118", // Magma Rager 5/1
        "CS2_179", "CS2_179", // Sen'jin Shieldmasta 3/5 Taunt
        "CS2_182", "CS2_182", // Chillwind Yeti 4/5
        "CS2_119", "CS2_119", // Oasis Snapjaw 2/7
        "CS2_131", "CS2_131", // Stormwind Knight 2/5 Charge
        "CS2_200", "CS2_200", // Boulderfist Ogre 6/7
        "CS2_141", "CS2_141", // Ironforge Rifleman (battlecry: 1 damage)
        "CS2_029", "CS2_024", // Fireball, Frostbolt
        "CS2_023", "CS2_008", // Arcane Intellect, Moonfire
    };

    public static List<string> BasicDeck(CardDb db) => Basic.Where(id => db.TryGet(id, out _)).ToList();
}
