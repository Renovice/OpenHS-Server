using CardDb = HsServer.Engine.CardDb;
using HsServer.Sabber;
using SabberStoneCore.Actions;
using SabberStoneCore.Enums;
using SabberStoneCore.Kettle;
using SabberStoneCore.Model;
using SabberStoneCore.Model.Entities;
using SabberStoneCore.Tasks.PlayerTasks;
using SGame = SabberStoneCore.Model.Game;

namespace HsServer.Tests;

/// <summary>
/// A card an entity's effect adds to the hand flies in from it (FINDINGS 2026-11-14). The client flies a new hand card from its
/// CREATOR only inside a spawn-override effect (Card.ActivateHandSpawnSpell); live's server sends a SpawnToHand SUB_SPELL for a
/// creator whose CardDef has only the generic Play / Trigger (87 of 89 live creators), none for one whose own client effect does
/// it (68 of 77: Fire Fly, Babbling Book, Zola). Also: summons of a crafted Location count by its parts' text (Elise's).
/// </summary>
public class ToHandFlightTests
{
    private static readonly Lazy<CardDb> Db = new(() =>
    {
        string dir = CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
        SabberEngineGame.EnsureCards(dir);
        return CardDb.Load(dir);
    });

    private static SGame NewGame()
    {
        _ = Db.Value;
        var game = new SGame(new SabberStoneCore.Config.GameConfig
        {
            StartPlayer = 1, Player1HeroClass = CardClass.MAGE, Player2HeroClass = CardClass.MAGE,
            FillDecks = true, FillDecksPredictably = true, History = true, Shuffle = false,
        });
        game.StartGame();
        game.Player1.BaseMana = 10;
        game.Player2.BaseMana = 10;
        return game;
    }

    /// <summary>Plays minion <paramref name="id"/>; the prefab of the SUB_SPELL each new hand card arrives in (null: none).</summary>
    private static List<string?> PlayAndWatchHand(SGame game, Controller me, string id)
    {
        me.UsedMana = 0;
        var card = Generic.DrawCard(me, Cards.FromId(id));
        int from = game.PowerHistory.Full.Count;
        Assert.True(game.Process(PlayCardTask.Minion(me, card)), id);
        var open = new Stack<string>();
        var result = new List<string?>();
        foreach (var e in game.PowerHistory.Full.Skip(from))
            switch (e)
            {
                case PowerHistorySubSpellStart s: open.Push(s.Prefab); break;
                case PowerHistorySubSpellEnd: if (open.Count > 0) open.Pop(); break;
                case PowerHistoryFullEntity f when f.Entity.Tags.TryGetValue(GameTag.ZONE, out int z) && z == (int)Zone.HAND &&
                                                   f.Entity.Tags.TryGetValue(GameTag.CREATOR, out int by) && by == card.Id:
                    result.Add(open.Count > 0 ? open.Peek() : null);
                    break;
            }
        return result;
    }

    [Fact]
    public void A_creator_without_its_own_client_effect_flies_the_card_in_like_live()
    {
        var game = NewGame();
        var me = game.CurrentPlayer;
        // Wandmaker (CardDef: only the generic Play): the plain to-hand flight from it, around its card
        var wand = PlayAndWatchHand(game, me, "SCH_160");
        Assert.Single(wand);
        Assert.Equal(SGame.GenericSpawnToHand, wand[0]);
        Assert.Equal(SGame.GenericSpawnToHand, PlayAndWatchHand(game, me, "CORE_SCH_160").Single()); // its Core reprint too
        // Fire Fly (CardDef: SpawnToHand_FireSparks_FX, live sends no SUB_SPELL) and Babbling Book: none from us either
        Assert.Null(PlayAndWatchHand(game, me, "UNG_809").Single());
        Assert.Null(PlayAndWatchHand(game, me, "CORE_KAR_009").Single());
        Assert.True(SabberStoneCore.Loader.BlockMeta.HasOwnFx("UNG_809", BlockType.POWER));
        Assert.False(SabberStoneCore.Loader.BlockMeta.HasOwnFx("SCH_160", BlockType.POWER));
    }

    [Fact]
    public void A_crafted_location_counts_its_summons_by_its_parts()
    {
        _ = Db.Value;
        var look = Cards.FromId("TLC_100t2");
        var two = Generic.CraftedCard(look, Cards.FromId("TLC_100t25"), Cards.FromId("TLC_100t26")); // two Raptors + Armor
        var one = Generic.CraftedCard(look, Cards.FromId("TLC_100t15"), Cards.FromId("TLC_100t16")); // one Raptor + Armor
        Assert.Equal(Generic.SummonCount.Even, Generic.SummonCountOf(two));
        Assert.Equal(Generic.SummonCount.Unknown, Generic.SummonCountOf(one)); // (not the first crafted card's count: same id)

        // live (Hearthstone_2026_11_11_03_08_16, Terror Run at 1): its two Raptors land right, then left of it
        var game = NewGame();
        var me = game.CurrentPlayer;
        var location = (Location)SabberStoneCore.Model.Entities.Entity.FromCard(me, two);
        Generic.SummonBlock.Invoke(game, location, -1, null);
        location.IsExhausted = false;
        location[GameTag.NUM_TURNS_IN_PLAY] = 1;
        var minion = (Minion)SabberStoneCore.Model.Entities.Entity.FromCard(me, Cards.FromId("CS2_182"));
        Generic.SummonBlock.Invoke(game, minion, -1, null);
        var before = me.BoardZone.Select(m => m.Id).ToHashSet();
        Assert.True(game.Process(LocationTask.Any(me, location)));
        // (a Location takes a board slot but isn't enumerated with the minions: order everything by slot)
        var made = me.BoardZone.Where(m => !before.Contains(m.Id)).Select(m => m.Id).OrderBy(x => x).ToList();
        string layout = string.Concat(me.BoardZone.Append(location).OrderBy(m => m.ZonePosition)
            .Select(m => m == location ? "S" : made.IndexOf(m.Id) is var i and >= 0 ? (i + 1).ToString() : "."));
        Assert.Equal("2S1.", layout);
    }
}
