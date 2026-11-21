using HsServer.Lobby;

namespace HsServer.Tests;

/// <summary>The "account" admin commands' store side (2026-11-21): the active account, new / copy, rename, delete with a 24-hour
/// restore window, restore and the purge. No recordings needed (a generic reference account).</summary>
public class AccountCommandTests
{
    private static AccountStore Store(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), "hs-accounts-" + Guid.NewGuid().ToString("N"));
        return new AccountStore(Path.Combine(dir, "accounts"), new RecordedAccount(), s => s.SetInts(1, 1, new long[] { 7 }), null);
    }

    [Fact]
    public void The_active_account_starts_as_default_and_follows_login()
    {
        var store = Store(out string dir);
        try
        {
            Assert.Equal(AccountStore.DefaultName, store.ActiveName);
            store.SetActive("Bob");
            Assert.Equal("Bob", store.ActiveName);
            Assert.Throws<ArgumentException>(() => store.SetActive("not ok!"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_new_account_can_copy_another_and_names_are_unique()
    {
        var store = Store(out string dir);
        try
        {
            PlayerAccount bob = store.Create("Bob");
            bob.SaveData.SetInts(5, 5, new long[] { 42 });
            bob.SaveData.Save();
            PlayerAccount twin = store.Create("Twin", from: "Bob");
            Assert.Equal(42, twin.SaveData.Values(5, 5).Single());
            Assert.NotEqual(bob.AccountId, twin.AccountId); // (its own identity)
            Assert.Throws<ArgumentException>(() => store.Create("bob"));
            Assert.Throws<ArgumentException>(() => store.Create("Nope", from: "Ghost"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Rename_keeps_the_progress_and_moves_the_active_mark()
    {
        var store = Store(out string dir);
        try
        {
            PlayerAccount bob = store.Create("Bob");
            ulong id = bob.AccountId;
            store.SetActive("Bob");
            store.Rename("Bob", "Robert");
            Assert.False(store.Exists("Bob"));
            PlayerAccount robert = store.Get("Robert");
            Assert.Equal(id, robert.AccountId);
            Assert.StartsWith("Robert#", robert.BattleTag);
            Assert.Equal("Robert", store.ActiveName);
            Assert.Throws<ArgumentException>(() => store.Rename(AccountStore.DefaultName, "x"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Delete_waits_24_hours_for_a_restore_then_is_gone_for_good()
    {
        var store = Store(out string dir);
        try
        {
            store.Create("Bob");
            store.SetActive("Bob");
            store.Delete("Bob", now: false);
            Assert.False(store.Exists("Bob"));
            Assert.Equal(AccountStore.DefaultName, store.ActiveName); // (the active one deleted: back to default)
            Assert.Contains(store.Deleted(), d => d.Name == "bob");
            store.Restore("Bob");
            Assert.True(store.Exists("Bob"));

            // past the window: the purge removes it, and it can't come back
            store.Delete("Bob", now: false);
            string deletedRoot = Path.Combine(dir, "accounts-deleted");
            string held = Directory.GetDirectories(deletedRoot).Single();
            string old = Path.Combine(deletedRoot, $"bob__{DateTime.UtcNow.AddHours(-25):yyyyMMddTHHmmssZ}");
            Directory.Move(held, old);
            Assert.Empty(store.Deleted());
            Assert.Equal(1, store.PurgeExpired());
            Assert.False(Directory.Exists(old));
            Assert.Throws<ArgumentException>(() => store.Restore("Bob"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Delete_now_is_permanent_and_default_is_protected()
    {
        var store = Store(out string dir);
        try
        {
            store.Create("Bob");
            store.Delete("Bob", now: true);
            Assert.False(store.Exists("Bob"));
            Assert.Empty(store.Deleted());
            Assert.Throws<ArgumentException>(() => store.Restore("Bob"));
            store.Get(AccountStore.DefaultName);
            Assert.Throws<ArgumentException>(() => store.Delete(AccountStore.DefaultName, now: false));
        }
        finally { Directory.Delete(dir, true); }
    }
}
