namespace HsServer.Tests;

/// <summary>
/// The engine's GameTag numbers go to the client as they are. A tag the client also names must have the client's number:
/// the upstream engine had guessed ones (NUM_SPELLS_PLAYED_THIS_GAME 1001 instead of 1780, PROTECTED/PROTECTING swapped),
/// so the client got the counter under a number it doesn't know (wirecheck, 2026-10-01).
/// </summary>
public class TagNumberTests
{
    [Fact]
    public void Engine_tags_the_client_names_have_the_client_numbers()
    {
        var client = Enum.GetNames(typeof(HsServer.Engine.GAME_TAG))
            .ToDictionary(n => n, n => (int)Enum.Parse(typeof(HsServer.Engine.GAME_TAG), n));
        var wrong = new List<string>();
        foreach (string name in Enum.GetNames(typeof(SabberStoneCore.Enums.GameTag)))
        {
            int engine = (int)Enum.Parse(typeof(SabberStoneCore.Enums.GameTag), name);
            if (client.TryGetValue(name, out int number) && number != engine)
                wrong.Add($"{name}: engine {engine}, client {number}");
        }
        Assert.Empty(wrong);
    }
}
