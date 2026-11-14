using HsServer.Engine;
using HsServer.Protocol;

namespace HsServer.Lobby;

/// <summary>
/// Zilliax Deluxe 3000's saved versions: the deck builder asks for them (GetCustomizedCardHistoryRequest 707) and shows
/// them as quick picks. Live answers GetCustomizedCardHistoryResponse 708: distinct builds {ModuleCardId1, ModuleCardId2,
/// PortraitCardId}, newest first (live-20261001-startofgame #1917). A build is read from a saved deck's sideboard: the
/// cosmetic module (tag ZILLIAX_CUSTOMIZABLE_COSMETICMODULE 3376) is the portrait, the functional modules (3377) the two
/// modules in their sideboard order.
/// </summary>
internal sealed class ZilliaxBuilds
{
    private const int CosmeticModule = 3376, FunctionalModule = 3377;
    private readonly HashSet<int> cosmetic = new(), functional = new();

    /// <summary>The player's live history, from the newest live recording that has one (adopted once); may be empty.</summary>
    public IReadOnlyList<int[]> Recorded { get; }

    public ZilliaxBuilds(CardDb cards, IEnumerable<string> liveSessions)
    {
        foreach (CardDef card in cards.All)
        {
            if (card.Tag((GAME_TAG)CosmeticModule) != 0) cosmetic.Add(card.DbfId);
            if (card.Tag((GAME_TAG)FunctionalModule) != 0) functional.Add(card.DbfId);
        }
        foreach (string session in (liveSessions ?? Enumerable.Empty<string>()).Reverse())
        {
            byte[] body = Recording.Read(session).LastOrDefault(p => p.Channel == "util" && p.IsIn && p.Type == 708 && p.Body != null)?.Body;
            if (body == null) continue;
            Recorded = PegasusPackets.Deserialize<PegasusUtil.GetCustomizedCardHistoryResponse>(body).Cards
                .Select(c => new[] { c.ModuleCardId1, c.ModuleCardId2, c.PortraitCardId }).ToList();
            break;
        }
        Recorded ??= new List<int[]>();
    }

    /// <summary>The Zilliax builds in a saved deck's sideboards: [module 1, module 2, portrait], one per Zilliax with
    /// both modules chosen.</summary>
    public List<int[]> FromSideboards(IEnumerable<PegasusShared.SideBoardCardData> sideboard)
    {
        var builds = new List<int[]>();
        foreach (var zilliax in sideboard.Where(s => s.Def != null).GroupBy(s => s.LinkedCardDbId))
        {
            var modules = zilliax.Select(s => s.Def.Asset).Where(functional.Contains).ToList();
            if (modules.Count < 2) continue;
            int portrait = zilliax.Select(s => s.Def.Asset).FirstOrDefault(cosmetic.Contains);
            builds.Add(new[] { modules[0], modules[1], portrait });
        }
        return builds;
    }

    public static PegasusUtil.GetCustomizedCardHistoryResponse Response(IEnumerable<int[]> history, uint maximum)
    {
        var response = new PegasusUtil.GetCustomizedCardHistoryResponse();
        foreach (int[] b in history.Take(maximum == 0 ? int.MaxValue : (int)maximum))
            response.Cards.Add(new PegasusUtil.CustomizedCard { ModuleCardId1 = b[0], ModuleCardId2 = b[1], PortraitCardId = b[2] });
        return response;
    }
}
