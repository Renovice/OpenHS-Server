using HsServer.Engine;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Model;

namespace HsServer.Tests;

/// <summary>Assemble's pool (client tag 4533): which pool minions our scripts can split into a first half (when) and a second (what).
/// The ones that can't are listed in RESEARCH/assemble-unsplit.tsv — each needs Power.AssembleWhen / AssembleWhat in its script.</summary>
public class AssemblePoolAuditTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void Most_of_the_assemble_pool_splits_into_halves()
    {
        SabberEngineGame.EnsureCards(CardDb.FindDefaultDir(TestEnvironment.ProjectRoot));
        var pool = Cards.All.Where(c => c.Type == SabberStoneCore.Enums.CardType.MINION && c[Generic.ASSEMBLE_POOL] > 0 && c.Collectible).ToList();
        var unsplit = pool.Where(c => !Generic.Assemblable(c)).ToList();
        File.WriteAllLines(Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "assemble-unsplit.tsv"),
            unsplit.Select(c => $"{c.Id}\t{c.Name}\t{(c.Power == null ? "no script" : $"trigger={c.Power.Trigger?.GetType().Name} dr={c.Power.DeathrattleTask != null} bc={c.Power.PowerTask != null} aura={c.Power.Aura != null}")}\t{c.Text?.Replace("\n", " ")}"));
        output.WriteLine($"pool {pool.Count}, splittable {pool.Count - unsplit.Count}, unsplit {unsplit.Count}");
        Assert.True(pool.Count > 0);
    }
}
