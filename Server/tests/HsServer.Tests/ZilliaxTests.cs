using HsServer.Lobby;
using HsServer.Protocol;
using PegasusShared;

namespace HsServer.Tests;

public class ZilliaxTests
{
    private static readonly Lazy<ZilliaxBuilds> Builds = new(() =>
    {
        string dir = HsServer.Engine.CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
        // pinned: this live login answered 707 with six builds (#1917)
        string live = Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures", "live-20261001-startofgame", "session-20261001-164832.jsonl");
        return new ZilliaxBuilds(HsServer.Engine.CardDb.Load(dir), new[] { live });
    });

    private static SideBoardCardData Side(int dbf, int zilliax = 102983) => new() { Def = new CardDef { Asset = dbf, Premium = 0 }, LinkedCardDbId = zilliax };

    [Fact]
    public void Live_s_history_is_adopted_once_then_saved_decks_lead_it()
    {
        ZilliaxBuilds builds = Builds.Value;
        Assert.Equal(6, builds.Recorded.Count);
        Assert.Equal(new[] { 104950, 104951, 110440 }, builds.Recorded[0]);

        var store = new CollectionStore(null);
        Assert.Equal(6, store.ZilliaxHistory(builds.Recorded).Count);

        // a deck saved with Zilliax: two functional modules + the cosmetic portrait (live's own ids)
        var saved = builds.FromSideboards(new[] { Side(104949), Side(110446), Side(104951) });
        Assert.Equal(new[] { 104949, 104951, 110446 }, Assert.Single(saved));
        store.RememberZilliax(saved[0], builds.Recorded);
        var history = store.ZilliaxHistory(builds.Recorded);
        Assert.Equal(6, history.Count);                                                   // it was already there: moved, not added
        Assert.Equal(new[] { 104949, 104951, 110446 }, history[0]);

        Assert.Empty(builds.FromSideboards(new[] { Side(104949) }));                       // one module: not a full build

        var response = ZilliaxBuilds.Response(history, 3);
        Assert.Equal(3, response.Cards.Count);
        Assert.Equal((104949, 104951, 110446), (response.Cards[0].ModuleCardId1, response.Cards[0].ModuleCardId2, response.Cards[0].PortraitCardId));
    }
}
