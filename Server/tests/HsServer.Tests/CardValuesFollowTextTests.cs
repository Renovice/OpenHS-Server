using SabberStoneCore.Loader;

namespace HsServer.Tests;

/// <summary>Card numbers follow the card's current text (Loader.CardValues, user 2026-11-20: "the DB should be auto variable"): a
/// reworded text of the same shape gives the value at the same place; another shape keeps the scripted value and asks for a review.</summary>
[Collection("CardValues")]
public class CardValuesFollowTextTests
{
    [Fact]
    public void A_patched_number_follows_and_a_new_shape_asks_for_a_review()
    {
        string file = Path.Combine(Path.GetTempPath(), "cv-baseline-" + Guid.NewGuid() + ".tsv");
        File.WriteAllLines(file, new[]
        {
            "card\tname\tvalue\tindex\ttext",
            "ZZ_1\tdraws\t1\t0\tBattlecry: Draw 1 card.",
            "ZZ_2\tdamage\t3\t0\tDeal 3 damage.",
            "ZZ_3\tbuff\t2\t-1\tGive +2/+2.",
        });
        string oldFile = CardValues.BaselineFile;
        var oldText = CardValues.CurrentText;
        try
        {
            CardValues.BaselineFile = file;
            CardValues.CurrentText = id => id switch
            {
                "ZZ_1" => "<b>Battlecry:</b> Draw 3 card.",          // same shape, new number: follows
                "ZZ_2" => "Deal 3 damage to all enemies.",            // another shape: the scripted value, reviewed
                "ZZ_3" => "Give +3/+3.",                              // never placed (twice): reviewed
                _ => null,
            };
            CardValues.ReloadBaseline();
            Assert.Equal(3, CardValues.Get("ZZ_1", "draws", 1));
            Assert.Equal(3, CardValues.Get("ZZ_2", "damage", 3));
            Assert.Equal(2, CardValues.Get("ZZ_3", "buff", 2));
            Assert.Equal((1, 3), CardValues.Followed[("ZZ_1", "draws")]);
            Assert.Contains(("ZZ_2", "damage"), CardValues.NeedsReview);
            Assert.Contains(("ZZ_3", "buff"), CardValues.NeedsReview);
        }
        finally
        {
            CardValues.BaselineFile = oldFile;
            CardValues.CurrentText = oldText;
            CardValues.ReloadBaseline();
            File.Delete(file);
        }
    }
}
