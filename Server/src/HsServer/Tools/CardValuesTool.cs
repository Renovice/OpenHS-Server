using SabberStoneCore.Loader;

namespace HsServer.Tools;

/// <summary>
/// `HsServer cardvalues [baseline]` (2026-11-20): the card numbers that follow the current client texts (Loader.CardValues).
/// `cardvalues`: which values now follow a reworded text (scripted → now) and which cards changed in a way the baseline can't map
/// (their scripts need a look). `cardvalues baseline`: writes Server/data/card-values-baseline.tsv from the current texts — after
/// a patch's script review is done, so the reviewed texts become the new reference.
/// </summary>
internal static class CardValuesTool
{
    public static int Run(string[] args)
    {
        string root = Program.ProjectRoot();
        string dbf = Engine.CardDb.FindDefaultDir(root);
        string file = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbf))!, "card-values-baseline.tsv");
        bool write = args.Contains("baseline");
        if (write) CardValues.BaselineFile = ""; // (the scripts' own values while they load: the baseline is made from them)
        Sabber.SabberEngineGame.EnsureCards(dbf);
        if (write)
        {
            var rows = CardValues.BaselineRows().ToList();
            File.WriteAllLines(file, rows);
            int placed = rows.Skip(1).Count(r => r.Split('\t')[3] != "-1");
            Console.WriteLine($"{rows.Count - 1} card values, {placed} placed in their card's text (follow it after a patch), " +
                              $"{rows.Count - 1 - placed} not (the number appears twice or not at all) -> {file}");
            return 0;
        }
        Console.WriteLine($"baseline: {file}{(File.Exists(file) ? "" : " (missing: run `HsServer cardvalues baseline`)")}");
        Console.WriteLine($"values that follow a reworded text: {CardValues.Followed.Count}");
        foreach (var ((card, name), (scripted, now)) in CardValues.Followed.OrderBy(k => k.Key.Card, StringComparer.Ordinal))
            Console.WriteLine($"    {card,-16} {name,-14} {scripted} -> {now}");
        Console.WriteLine($"values whose card changed shape (review the script): {CardValues.NeedsReview.Count}");
        foreach (var (card, name) in CardValues.NeedsReview.OrderBy(k => k.Card, StringComparer.Ordinal))
            Console.WriteLine($"    {card,-16} {name}");
        return 0;
    }
}
