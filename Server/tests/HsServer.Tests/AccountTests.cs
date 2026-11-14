using HsServer.Lobby;

namespace HsServer.Tests;

/// <summary>Player accounts and persistence (roadmap B1).</summary>
public class AccountTests
{
    private static RecordedAccount Reference() => RecordedAccount.Load(RecordedAccount.FindReferenceSession(
        Path.Combine(TestEnvironment.ProjectRoot, "Hearthstone main game", "BepInEx", "HsRecorder"),
        Path.Combine(TestEnvironment.ProjectRoot, "RESEARCH", "captures")));

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "hsserver-accounts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Theory]
    [InlineData(LobbySession.LocalAuthToken, "default")]
    [InlineData("LOCAL:Bob", "Bob")]
    [InlineData("LOCAL:bad name", null)]
    [InlineData("LOCAL:", null)]
    [InlineData("something-else", null)]
    [InlineData(null, null)]
    public void Login_tokens_name_a_player(string? token, string? expected) =>
        Assert.Equal(expected, AccountStore.NameFromToken(token!));

    [Fact]
    public void Players_have_their_own_ids_and_persisted_save_data()
    {
        string dir = TempDir();
        try
        {
            RecordedAccount reference = Reference();
            var store = new AccountStore(dir, reference, s => s.SetInts(1, 1, new long[] { 7 }), null);
            PlayerAccount main = store.Get(AccountStore.DefaultName);
            PlayerAccount bob = store.Get("Bob");
            Assert.Equal(reference.AccountId, main.AccountId);           // the default player keeps the recorded identity
            Assert.Equal(reference.GameAccountId, main.GameAccountId);
            Assert.NotEqual(main.GameAccountId, bob.GameAccountId);
            Assert.StartsWith("Bob#", bob.BattleTag);
            Assert.Equal(new long[] { 7 }, bob.SaveData.Values(1, 1));   // seeded once

            bob.SaveData.SetInts(55, 14, new long[] { 1, 2, 3 });        // e.g. a Heist deck list
            Assert.Empty(main.SaveData.Values(55, 14));                  // players don't share save data

            // a restart: a new store reads the same files back
            var again = new AccountStore(dir, reference, s => s.SetInts(1, 1, new long[] { 99 }), null);
            PlayerAccount bob2 = again.Get("bob");
            Assert.Equal(bob.GameAccountId, bob2.GameAccountId);
            Assert.Equal(bob.BattleTag, bob2.BattleTag);
            Assert.Equal(new long[] { 1, 2, 3 }, bob2.SaveData.Values(55, 14));
            Assert.Equal(new long[] { 7 }, bob2.SaveData.Values(1, 1)); // not re-seeded
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void The_default_player_gets_a_copy_of_the_old_deck_file()
    {
        string dir = TempDir();
        try
        {
            string legacy = Path.Combine(dir, "decks.bin");
            var old = new DeckStore(legacy);
            old.Seed(new PegasusUtil.SecondaryClientState());
            File.WriteAllBytes(legacy, new byte[] { 0x0A, 0x00 }); // any content: copied byte for byte
            var store = new AccountStore(Path.Combine(dir, "accounts"), Reference(), null, null, legacy);
            store.Get(AccountStore.DefaultName);
            Assert.True(File.Exists(legacy)); // copied, not moved
            Assert.Equal(File.ReadAllBytes(legacy), File.ReadAllBytes(Path.Combine(dir, "accounts", "default", "decks.bin")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
