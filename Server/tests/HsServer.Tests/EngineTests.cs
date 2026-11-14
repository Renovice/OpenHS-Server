using HsServer.Engine;
using HsServer.Protocol;
using PegasusGame;
using Xunit.Abstractions;

namespace HsServer.Tests;

public class EngineTests(ITestOutputHelper output)
{
    private static readonly Lazy<CardDb> Db = new(() => CardDb.Load(CardDb.FindDefaultDir(TestEnvironment.ProjectRoot)));

    private static (GameState Game, List<IProtoBuf> Packets) NewGame(int seed, bool humanFirst)
    {
        var g = new GameState(Db.Value, seed);
        var human = new PlayerSetup { HeroCardId = "HERO_08", Deck = Decks.BasicDeck(Db.Value), IsAi = !humanFirst, AccountHi = 1, AccountLo = 2 };
        var ai = new PlayerSetup { HeroCardId = "HERO_05", Deck = Decks.BasicDeck(Db.Value), IsAi = true };
        g.Start(human, ai, scenario: 260);
        return (g, g.History.Drain());
    }

    [Fact]
    public void Card_database_has_current_client_cards()
    {
        Assert.True(Db.Value.Count > 30000);
        CardDef yeti = Db.Value["CS2_182"];
        Assert.Equal((4, 4, 5), (yeti.Cost, yeti.Atk, yeti.Health));
        Assert.Equal(30, Decks.BasicDeck(Db.Value).Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Ai_vs_ai_games_finish_and_serialize(int seed)
    {
        var g = new GameState(Db.Value, seed);
        g.Start(new PlayerSetup { HeroCardId = "HERO_08", Deck = Decks.BasicDeck(Db.Value), IsAi = true },
                new PlayerSetup { HeroCardId = "HERO_05", Deck = Decks.BasicDeck(Db.Value), IsAi = true }, 260);
        var packets = new List<IProtoBuf>(g.History.Drain());
        g.Mulligan(g.P1, g.P1.Hand.Select(c => c.Id).ToList());
        g.Mulligan(g.P2, g.P2.Hand.Select(c => c.Id).ToList());
        int actions = 0;
        while (!g.Over && g.Turn < 90)
        {
            if (SimpleAi.Step(g)) actions++;
            else g.EndTurn();
            packets.AddRange(g.History.Drain());
        }
        output.WriteLine($"seed {seed}: over={g.Over} turn={g.Turn} actions={actions} winner={g.Winner?.Index} packets={packets.Count}");
        Assert.True(g.Over, "game should end (fatigue guarantees it)");
        AssertSerializable(packets);
    }

    // Live: every targeted effect block opens with META_DATA TARGET (Info = target) before its damage, which is what
    // lets the client land the damage on the spell's impact.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Targeted_effect_blocks_open_with_target_metadata(int seed)
    {
        var g = new GameState(Db.Value, seed);
        g.Start(new PlayerSetup { HeroCardId = "HERO_08", Deck = Decks.BasicDeck(Db.Value), IsAi = true },
                new PlayerSetup { HeroCardId = "HERO_05", Deck = Decks.BasicDeck(Db.Value), IsAi = true }, 260);
        var items = new List<PowerHistoryData>();
        g.Mulligan(g.P1, g.P1.Hand.Select(c => c.Id).ToList());
        g.Mulligan(g.P2, g.P2.Hand.Select(c => c.Id).ToList());
        while (!g.Over && g.Turn < 90)
        {
            if (!SimpleAi.Step(g)) g.EndTurn();
            items.AddRange(g.History.Drain().OfType<PowerHistory>().SelectMany(h => h.List));
        }
        // Universal rule (live): an effect block that damages, heals or freezes characters opens with one TARGET entry
        // listing all of them (and its chosen target), before any of those changes.
        int checkedBlocks = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].PowerStart is not { Type: HistoryBlock.Type.POWER } start) continue;
            var touched = new HashSet<int>();
            int depth = 0;
            for (int j = i + 1; j < items.Count; j++)
            {
                if (items[j].PowerStart != null) depth++;
                if (items[j].PowerEnd != null && depth-- == 0) break;
                if (depth > 0) continue;
                if (items[j].MetaData is { Type: HistoryMeta.Type.DAMAGE or HistoryMeta.Type.HEALING } m) touched.UnionWith(m.Info);
                if (items[j].TagChange is { Tag: (int)GAME_TAG.FROZEN, Value: 1 } t) touched.Add(t.Entity);
            }
            if (start.Target > 0) touched.Add(start.Target);
            if (touched.Count == 0) continue;
            var meta = items[i + 1].MetaData;
            // live: a block with a chosen target names only it (Explosive Shot, Howling Blast, Obliterate)
            if (start.Type == HistoryBlock.Type.POWER && start.Target > 0 && meta is { Type: HistoryMeta.Type.TARGET } only
                && only.Info.Count == 1 && only.Info[0] == start.Target) { checkedBlocks++; continue; }
            Assert.True(meta is { Type: HistoryMeta.Type.TARGET, Data: 0 } && touched.IsSubsetOf(meta.Info),
                $"POWER block of {start.Source} → {start.Target} does not open with a TARGET list covering {string.Join(",", touched)}");
            checkedBlocks++;
        }
        output.WriteLine($"seed {seed}: {checkedBlocks} targeted effect blocks checked");
        Assert.True(checkedBlocks > 0);
    }

    // Freeze: a frozen character can't attack; it thaws at the end of its controller's turn (MAIN_CLEANUP) if it did
    // not attack that turn — and not at the end of the freezer's turn.
    [Fact]
    public void Frozen_minion_skips_its_attack_then_thaws_at_its_own_turn_end()
    {
        var (g, _) = NewGame(seed: 3, humanFirst: true);
        g.Mulligan(g.P1, g.P1.Hand.Select(c => c.Id).ToList());
        g.Mulligan(g.P2, g.P2.Hand.Select(c => c.Id).ToList());
        PlayerState me = g.Current, them = me.Opponent;
        HsServer.Engine.Entity yeti = g.Summon(them, Db.Value["CS2_182"]);
        g.Freeze(yeti);
        Assert.True(yeti.Has(GAME_TAG.FROZEN));

        g.EndTurn(); // freezer's turn ends: still frozen
        Assert.Same(them, g.Current);
        Assert.True(yeti.Has(GAME_TAG.FROZEN));
        Assert.False(g.CanAttackNow(yeti));

        g.EndTurn(); // the frozen minion's own turn ends without an attack: thaws
        Assert.False(yeti.Has(GAME_TAG.FROZEN));
        g.EndTurn();
        Assert.True(g.CanAttackNow(yeti));
    }

    [Fact]
    public void Human_flow_via_options_until_game_over()
    {
        var (g, packets) = NewGame(seed: 7, humanFirst: true);
        Assert.Contains(packets, p => p is EntityChoices ec && ec.PlayerId == 1);
        g.Mulligan(g.P2, g.P2.Hand.Select(c => c.Id).ToList());
        g.Mulligan(g.P1, g.P1.Hand.Take(1).Select(c => c.Id).ToList()); // replace all but one
        packets.AddRange(g.History.Drain());
        Assert.Contains(packets, p => p is AllOptions);

        int humanActions = 0, rounds = 0;
        while (!g.Over && rounds++ < 400)
        {
            if (g.Current == g.P1)
            {
                var options = (AllOptions)packets.Last(p => p is AllOptions);
                // Play the first legal option that isn't END_TURN; otherwise end the turn.
                int index = options.Options.FindIndex(o => o.Type_ == Option.Type.POWER && o.MainOption.PlayError == 0);
                int target = 0;
                if (index > 0)
                {
                    var main = options.Options[index].MainOption;
                    if (main.Targets.Count > 0) target = main.Targets[0].Id;
                    if (g.Get(main.Id).Zone == HsServer.Engine.TAG_ZONE.PLAY && main.Targets.Count > 0) target = main.Targets.Last().Id;
                }
                Assert.True(g.ChooseOption(options.Id, Math.Max(index, 0), target, 0), $"option {index} rejected");
                if (index > 0) humanActions++;
            }
            else if (!SimpleAi.Step(g)) g.EndTurn();
            packets.AddRange(g.History.Drain());
        }
        output.WriteLine($"human actions {humanActions}, turn {g.Turn}, over {g.Over}, winner {g.Winner?.Index}, packets {packets.Count}");
        Assert.True(g.Over);
        AssertSerializable(packets);
    }

    /// <summary>
    /// Rebuilds what the client knows purely from the packets and checks it against the server after every action:
    /// any state change the engine forgets to report (e.g. a minion's move to PLAY) makes the client act on stale
    /// data — the "attacking minion drags like a hand card" bug.
    /// </summary>
    [Theory]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    public void Client_view_matches_server_state_after_every_action(int seed)
    {
        var (g, packets) = NewGame(seed, humanFirst: true);
        var mirror = new Dictionary<int, Dictionary<int, int>>();
        void Apply(IEnumerable<IProtoBuf> batch)
        {
            foreach (var h in batch.OfType<PowerHistory>())
                foreach (var d in h.List)
                {
                    if (d.CreateGame != null)
                    {
                        mirror[d.CreateGame.GameEntity.Id] = d.CreateGame.GameEntity.Tags.ToDictionary(t => t.Name, t => t.Value);
                        foreach (var pl in d.CreateGame.Players) mirror[pl.Entity.Id] = pl.Entity.Tags.ToDictionary(t => t.Name, t => t.Value);
                    }
                    if (d.FullEntity != null) mirror[d.FullEntity.Entity] = d.FullEntity.Tags.ToDictionary(t => t.Name, t => t.Value);
                    if (d.ShowEntity != null)
                    {
                        var m = mirror.GetValueOrDefault(d.ShowEntity.Entity) ?? new Dictionary<int, int>();
                        foreach (var t in d.ShowEntity.Tags) m[t.Name] = t.Value;
                        mirror[d.ShowEntity.Entity] = m;
                    }
                    if (d.HideEntity != null) mirror[d.HideEntity.Entity][(int)GAME_TAG.ZONE] = d.HideEntity.Zone;
                    if (d.TagChange != null) mirror[d.TagChange.Entity][d.TagChange.Tag] = d.TagChange.Value;
                }
        }
        void Compare(string when)
        {
            foreach (var (id, tags) in mirror)
            {
                var e = g.Get(id);
                foreach (var tag in new[] { GAME_TAG.ZONE, GAME_TAG.CONTROLLER, GAME_TAG.DAMAGE, GAME_TAG.EXHAUSTED, GAME_TAG.ARMOR })
                    Assert.True(tags.GetValueOrDefault((int)tag) == e[tag], $"{when}: {e} {tag} client={tags.GetValueOrDefault((int)tag)} server={e[tag]}");
                if (e.Zone is HsServer.Engine.TAG_ZONE.PLAY or HsServer.Engine.TAG_ZONE.HAND && e.Type == HsServer.Engine.TAG_CARDTYPE.MINION)
                    Assert.True(tags.GetValueOrDefault((int)GAME_TAG.ZONE_POSITION) == e[GAME_TAG.ZONE_POSITION], $"{when}: {e} position client={tags.GetValueOrDefault((int)GAME_TAG.ZONE_POSITION)} server={e[GAME_TAG.ZONE_POSITION]}");
            }
        }

        Apply(packets);
        g.Mulligan(g.P2, g.P2.Hand.Select(c => c.Id).ToList());
        g.Mulligan(g.P1, g.P1.Hand.Select(c => c.Id).ToList());
        Apply(g.History.Drain());
        Compare("after mulligan");
        var rng = new Random(seed);
        for (int step = 0; step < 300 && !g.Over; step++)
        {
            if (g.Current == g.P1)
            {
                // Random legal human action, so attacks, plays and hero powers all get exercised.
                var candidates = new List<(HsServer.Engine.Entity Source, HsServer.Engine.Entity Target)>();
                foreach (var c in g.P1.Hand) candidates.Add((c, null));
                foreach (var m in g.P1.Board.Where(g.CanAttackNow))
                    foreach (var t in g.AttackTargets(g.P1, m)) candidates.Add((m, t));
                var pick = candidates.OrderBy(_ => rng.Next()).FirstOrDefault(x => g.PerformAction(g.P1, x.Source,
                    x.Target ?? (HsServer.Engine.CardScripts.NeedsTarget(x.Source) ? g.P2.Hero : null)));
                if (pick.Source == null) g.EndTurn();
            }
            else if (!SimpleAi.Step(g)) g.EndTurn();
            Apply(g.History.Drain());
            Compare($"step {step} turn {g.Turn}");
        }
        output.WriteLine($"seed {seed}: consistent through turn {g.Turn}, over {g.Over}");
    }

    private static void AssertSerializable(List<IProtoBuf> packets)
    {
        foreach (IProtoBuf packet in packets)
        {
            byte[] bytes = PegasusPackets.Serialize(packet);
            IProtoBuf back = PegasusPackets.Deserialize(packet.GetType(), bytes);
            Assert.NotNull(back);
        }
    }
}
