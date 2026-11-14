using HsServer.Sabber;
using HsServer.Tools;
using PegasusGame;

namespace HsServer.Tests;

/// <summary>HearthSim's tag names (`HsServer hsjson`, Server/data/hearthsim-tag-names.tsv): only a note in decoded text.</summary>
public class HsJsonCheckTests
{
    [Fact]
    public void A_tag_the_client_does_not_name_keeps_its_number_and_gets_HearthSims_name_as_a_note()
    {
        // 1086: unnamed in the client's GAME_TAG enum, HearthDb's UPGRADED_HERO_POWER. WireCheck relies on "a number in tag= = the
        // client doesn't read it", so the name must never replace the number.
        var history = new List<PowerHistoryData>
        {
            new() { TagChange = new PowerHistoryTagChange { Entity = 5, Tag = 1086, Value = 7 } },
            new() { TagChange = new PowerHistoryTagChange { Entity = 5, Tag = (int)HsServer.Engine.GAME_TAG.ZONE, Value = 1 } },
        };
        string[] lines = HistoryText.Format(history, id => id.ToString()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("TAG_CHANGE Entity=5 tag=1086 value=7", lines[0]);
        Assert.EndsWith("# hs:UPGRADED_HERO_POWER", lines[0].TrimEnd());
        Assert.DoesNotContain("# hs:", lines[1]); // a client-named tag needs no note
    }

    [Fact]
    public void Numbers_HearthstoneJSON_uses_for_unnamed_tags_are_not_names()
    {
        Assert.True(TagNames.IsName("UPGRADED_HERO_POWER"));
        Assert.True(TagNames.IsName("ImmuneToSpellpower"));
        Assert.False(TagNames.IsName("10"));
        Assert.False(TagNames.IsName("2242"));
        Assert.False(TagNames.IsName(""));
    }
}
