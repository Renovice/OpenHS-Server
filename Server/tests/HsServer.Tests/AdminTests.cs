using System.Net;
using System.Net.Sockets;
using System.Text;
using HsServer.Admin;
using HsServer.Lobby;
using PegasusUtil;

namespace HsServer.Tests;

/// <summary>The admin console (roadmap E1).</summary>
public class AdminTests : IDisposable
{
    private static readonly string DbfDir = HsServer.Engine.CardDb.FindDefaultDir(TestEnvironment.ProjectRoot);
    private static readonly Lazy<HsServer.Engine.CardDb> Cards = new(() => HsServer.Engine.CardDb.Load(DbfDir));
    private static readonly DateTime Today = new(2026, 10, 1, 15, 0, 0, DateTimeKind.Utc);

    private readonly string dir = Path.Combine(Path.GetTempPath(), "hsserver-admin-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    // A server state in a temp folder: players start with 100 dust and no cards; the calendar has one event.
    private ServerData Data()
    {
        var calendar = new EventCalendar(Path.Combine(DbfDir, "EventMap.json"),
            new[] { new EventCalendar.Entry("always", 203, null, null) }, Path.Combine(dir, "events.json")) { Clock = () => Today };
        var accounts = new AccountStore(Path.Combine(dir, "accounts"), new RecordedAccount(), null, null)
        {
            SeedCollection = c => c.Seed(new Collection { Stacks = new List<PegasusShared.CardStack>(), CollectionVersion = 5 },
                new GameCurrencyStates { ArcaneDustBalance = 100, CurrencyVersion = 5 }),
        };
        accounts.Get("default");
        var crafting = new CraftingRules(DbfDir, Cards.Value, calendar);
        return new ServerData
        {
            Root = dir, Reference = "", Account = new RecordedAccount(), DbfDir = DbfDir, Cards = Cards.Value,
            GrantedCards = Array.Empty<(int, int)>(), Calendar = calendar, Crafting = crafting,
            Accounts = accounts, Boosters = new BoosterRules(DbfDir, Cards.Value, crafting, calendar),
        };
    }

    private static async Task<string> Run(AdminCommands admin, params string[] args)
    {
        var result = await admin.RunAsync(args);
        Assert.True(result.Ok, result.Text);
        return result.Text;
    }

    [Fact]
    public async Task Quest_commands_give_progress_complete_and_grant()
    {
        var data = Data();
        data = new ServerData
        {
            Root = data.Root, Reference = data.Reference, Account = data.Account, DbfDir = data.DbfDir, Cards = data.Cards,
            GrantedCards = data.GrantedCards, Calendar = data.Calendar, Crafting = data.Crafting, Accounts = data.Accounts,
            Boosters = data.Boosters, Quests = new Quests(new QuestRules(DbfDir, data.Calendar)),
        };
        var admin = new AdminCommands(data);
        Assert.Contains("1035", await Run(admin, "quest", "list", "Spend $q mana"));
        await Run(admin, "quest", "give", "default", "1035");
        Assert.Contains("200/500", await Run(admin, "quest", "progress", "default", "1035", "+200"));
        Assert.Contains("completed", await Run(admin, "quest", "complete", "default", "1035"));
        Assert.Contains("reward granted", await Run(admin, "quests", "default"));
        Assert.Contains("new quest", await Run(admin, "quest", "new", "default"));
        await Run(admin, "quest", "reset", "default");
        Assert.Contains("no quests", await Run(admin, "quests", "default"));
        Assert.False((await admin.RunAsync(new[] { "quest", "give", "default", "999999" })).Ok);
    }

    [Fact]
    public async Task Give_and_take_cards_by_id_name_or_number()
    {
        var data = Data();
        var admin = new AdminCommands(data);
        var leeroy = Cards.Value["EX1_116"];
        Assert.Contains("0 -> 2", await Run(admin, "give", "default", "EX1_116", "2"));
        Assert.Contains("0 -> 1", await Run(admin, "give", "default", leeroy.DbfId.ToString(), "golden"));
        Assert.Contains("2 -> 1", await Run(admin, "take", "default", "ex1_116"));
        var player = data.Accounts.Get("default");
        Assert.Equal((1, 1), (player.Collection.Count(leeroy.DbfId, 0), player.Collection.Count(leeroy.DbfId, 1)));
        Assert.Contains("offline", await Run(admin, "give", "default", "EX1_116"));

        // a name only one collectible card has, typed as loose words in any case
        var unique = Cards.Value.All.Where(c => c.Tag(HsServer.Engine.GAME_TAG.COLLECTIBLE) == 1 && c.Name?.Split(' ').Length == 2)
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase).First(g => g.Count() == 1 &&
                Cards.Value.All.Count(c => string.Equals(c.Name, g.Key, StringComparison.OrdinalIgnoreCase)) == 1).Single();
        Assert.Contains("0 -> 3", await Run(admin, new[] { "give", "default" }.Concat(unique.Name.ToUpperInvariant().Split(' ')).Append("3").ToArray()));
        Assert.Equal(3, player.Collection.Count(unique.DbfId, 0));

        foreach (string vague in new[] { "Leeroy Jenkins", "leeroy" })                         // versions / partial name: listed
        {
            var result = await admin.RunAsync(new[] { "give", "default", vague });
            Assert.False(result.Ok);
            Assert.Contains("say which", result.Text);
        }
        Assert.False((await admin.RunAsync(new[] { "give", "nobody", "EX1_116" })).Ok);        // no such player
        Assert.False(data.Accounts.Exists("nobody"));                                           // and none made
    }

    [Fact]
    public async Task Dust_is_set_added_or_taken()
    {
        var data = Data();
        var admin = new AdminCommands(data);
        Assert.Contains("100 -> 5000", await Run(admin, "dust", "default", "5000"));
        Assert.Contains("5000 -> 5400", await Run(admin, "dust", "default", "+400"));
        Assert.Contains("5400 -> 0", await Run(admin, "dust", "default", "-9999"));             // never below 0
        Assert.Contains("0 -> 25", await Run(admin, "currency", "default", "tickets", "+25"));
        Assert.Contains("0 -> 1000", await Run(admin, "currency", "default", "gold", "1000"));
        Assert.Contains("bgtokens: 0 -> 7", await Run(admin, "currency", "default", "BgTokens", "7"));
        Assert.False((await admin.RunAsync(new[] { "currency", "default", "rubies", "5" })).Ok);
    }


    [Fact]
    public async Task Packs_are_given_by_name_or_id_and_listed()
    {
        var data = Data();
        var admin = new AdminCommands(data);
        Assert.Contains("no unopened packs", await Run(admin, "packs", "default"));
        Assert.Contains("Standard (713) packs: 0 -> 10", await Run(admin, "pack", "default", "Standard", "10"));
        Assert.Contains("0 -> 2", await Run(admin, "pack", "default", "Golden", "Standard", "+2"));     // a name with spaces
        Assert.Contains("10 -> 7", await Run(admin, "pack", "default", "713", "-3"));
        string list = await Run(admin, "packs", "default");
        Assert.Contains("7 x Standard (713)", list);
        Assert.Contains("2 x Golden Standard (716)", list);
        Assert.Contains("can't be opened here yet", await Run(admin, "pack", "default", "Epic", "Pack", "1"));
        Assert.Contains("Golden Standard", await Run(admin, "packtypes", "standard"));
        Assert.False((await admin.RunAsync(new[] { "pack", "default", "Nonsense", "1" })).Ok);
    }

    [Fact]
    public async Task Events_turn_on_and_off_and_are_kept()
    {
        var data = Data();
        var admin = new AdminCommands(data);
        await Run(admin, "event", "on", "dust_refund_36_4_2", "14");
        Assert.True(data.Calendar.IsActive(CollectionTests.EventNumber("dust_refund_36_4_2")));                                        // the DBF number of that event
        Assert.Contains("dust_refund_36_4_2", await Run(admin, "event", "list"));               // the active ones
        await Run(admin, "event", "off", "dust_refund_36_4_2");
        Assert.False(data.Calendar.IsActive("dust_refund_36_4_2"));
        await Run(admin, "event", "set", "dust_refund_36_4_2", "2026-09-01", "-");
        Assert.True(Data().Calendar.IsActive("dust_refund_36_4_2"));                            // events.json survives a restart
        Assert.Contains("nothing in the game uses it", await Run(admin, "event", "on", "made_up_event"));
        Assert.False((await admin.RunAsync(new[] { "event", "set", "x", "tomorrow", "-" })).Ok);
    }

    [Fact]
    public async Task Reset_starts_the_collection_over_with_newer_versions()
    {
        var data = Data();
        var admin = new AdminCommands(data);
        await Run(admin, "give", "default", "EX1_116");
        var collection = data.Accounts.Get("default").Collection;
        long version = collection.CollectionVersion;
        await Run(admin, "reset", "default", "collection");
        Assert.Equal((0, 0), collection.Totals());
        Assert.Equal(100, collection.Dust);
        Assert.True(collection.CollectionVersion > version);                                     // the client takes it
        Assert.False((await admin.RunAsync(new[] { "reset", "default", "everything" })).Ok);
    }

    [Theory]
    [InlineData("give default EX1_116 2", new[] { "give", "default", "EX1_116", "2" })]
    [InlineData("  give   default \"Leeroy Jenkins\"  golden ", new[] { "give", "default", "Leeroy Jenkins", "golden" })]
    [InlineData("event set x \"\" -", new[] { "event", "set", "x", "", "-" })]
    [InlineData("   ", new string[0])]
    public void Typed_lines_split_into_words(string line, string[] words) =>
        Assert.Equal(words, AdminConsole.Split(line));

    [Fact]
    public async Task The_admin_port_runs_commands_in_the_server()
    {
        var data = Data();
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var cts = new CancellationTokenSource();
        Task server = new AdminServer(new AdminCommands(data), port).RunAsync(cts.Token);
        try
        {
            string reply = null!;
            for (int attempt = 0; attempt < 50 && reply == null; attempt++)
            {
                try
                {
                    using var client = new TcpClient();
                    await client.ConnectAsync(IPAddress.Loopback, port);
                    var stream = client.GetStream();
                    await stream.WriteAsync(Encoding.UTF8.GetBytes("[\"dust\",\"default\",\"+1\"]\n"));
                    client.Client.Shutdown(SocketShutdown.Send);
                    reply = await new StreamReader(stream).ReadToEndAsync();
                }
                catch (SocketException) { await Task.Delay(20); }
            }
            Assert.StartsWith("ok\n", reply);
            Assert.Contains("100 -> 101", reply);
            Assert.Equal(101, data.Accounts.Get("default").Collection.Dust);
        }
        finally
        {
            cts.Cancel();
            await server;
        }
    }
}
