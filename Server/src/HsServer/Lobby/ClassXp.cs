using HsServer.Game;

namespace HsServer.Lobby;

/// <summary>
/// Class levels (wiki Level: "Experience is gained by playing games, especially winning games, in that class"; cap 60;
/// cosmetic since patch 28.4). Live never states the amounts and the client data has no table (HeroXP 283 carries MaxXp).
/// Community (HearthPwn): "minions destroyed, cards played, how many turns and the biggest xp buff is winning the game",
/// real players give "way more xp" than the AI. Fitted to the four recorded gains (UNCLEAR, live-20261003-dk-classxp):
/// activity = cards / Hero Powers / Locations used + attacks + enemy minions that died; Practice (AI) losses: 41 for 41,
/// 7 for 8; Casual against players: lost 54 for 24 (x2.25), won 96 for 40 (x2.4). So activity x 1 in Practice and
/// adventures, x 2.25 elsewhere, x 1.07 for a win. A game that doesn't count (conceded early, GameTally.CountsAsPlayed:
/// two live early concedes at 30 Health gave nothing) gives none. The XP a level needs: 1730 at levels 45-56 (every
/// recorded 283); below that UNCLEAR (a 2020 player reached Demon Hunter 42 in ~330 games: far less than 1730 a level),
/// 1730 too. Level 60 shows 9999 / 9999 (live).
/// </summary>
internal static class ClassXpRules
{
    public const int MaxLevel = 60, MaxLevelXp = 9999, PerLevel = 1730;
    public const double Solo = 1, Versus = 2.25, Win = 1.07;

    public static int XpForLevel(int level) => PerLevel;

    /// <param name="solo">Practice or an adventure (a scenario with an Adventure)</param>
    public static int GameXp(GameTally tally, bool solo)
    {
        if (!tally.CountsAsPlayed) return 0;
        int activity = tally.Events.Where(e => e.Kind is GameTally.CardPlayed or GameTally.HeroPowerUsed or GameTally.LocationActivated
                                               or GameTally.Attacked or GameTally.EnemyMinionDied).Sum(e => e.Amount);
        return (int)Math.Round(activity * (solo ? Solo : Versus) * (tally.Won ? Win : 1));
    }
}
