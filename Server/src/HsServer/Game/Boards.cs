using HsServer.Protocol;

namespace HsServer.Game;

/// <summary>
/// The game board each game is played on. Live picks a random expansion board per game (the user's recordings: 35
/// different boards over 220 games — Stormwind, Orgrimmar, Pandaria, Naxxramas, Dalaran, Revendreth, Cataclysm ...);
/// the board is the GameSetup's Board (the client's Board.json id), and Transfer Student becomes that board's version
/// (Power.BoardVersion). Adventure boards (Dalaran Heist's own, Non Combat Encounter) come with those modes later.
/// </summary>
public static class Boards
{
    /// <summary>The expansion boards (Board.json ids) a normal game picks from: every board that has a Transfer Student
    /// version, Stormwind to Cataclysm.</summary>
    public static readonly int[] Expansion =
    {
        1, 2, 3, 4, 5, 6, 7, 8, 10, 11, 12, 13, 14, 26, 54, 61, 64, 65, 68, 70, 73, 75, 78, 84, 86, 89, 91, 106, 118, 120,
        126, 129, 130, 131, 133, 134, 140, 141, 142, 143,
    };

    /// <summary>The recorded GameSetup with a board picked at random (the rest — Secret / Quest limits, keep-alive — kept).</summary>
    public static byte[] WithRandomBoard(byte[] gameSetup, Random rng)
    {
        var setup = PegasusPackets.Deserialize<PegasusGame.GameSetup>(gameSetup);
        setup.Board = Expansion[rng.Next(Expansion.Length)];
        return PegasusPackets.Serialize(setup);
    }
}
