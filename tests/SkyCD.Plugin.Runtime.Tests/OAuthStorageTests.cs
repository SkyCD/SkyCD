using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DryIoc;
using SkyCD.Core.DependencyInjection.Registrators;
using SkyCD.Core.OAuth;
using SkyCD.Plugin.Abstractions.OAuth;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests;

public sealed class OAuthStorageTests
{
    [Fact]
    public void PlatformSelection_PreservesDpapiAndFailsClosedOnUnsupportedPlatforms()
    {
        Assert.IsType<WindowsOAuthDataProtector>(OAuthDataProtectorFactory.Create(OSPlatform.Windows));
        Assert.IsType<KeyringOAuthDataProtector>(OAuthDataProtectorFactory.Create(OSPlatform.OSX));
        Assert.IsType<KeyringOAuthDataProtector>(OAuthDataProtectorFactory.Create(OSPlatform.Linux));
        AssertUnavailable(() => OAuthDataProtectorFactory.Create(OSPlatform.FreeBSD));
        using var container = new Container();
        new CommonRuntimeServiceRegistrator().RegisterServices(container);
        Assert.Equal(OAuthDataProtectorFactory.Create().GetType(), container.Resolve<IOAuthDataProtector>().GetType());
    }

    [Fact]
    public async Task EncryptedPersistence_Reloads_IsolatesAccountsAndNamespaces_AndDeletesSessions()
    {
        var keys = new KeyStore();
        await CheckPersistenceAsync(() => new KeyringOAuthDataProtector(keys));
        Assert.Equal(2, keys.Keys.Count);
        Assert.All(keys.ReturnedBuffers, buffer => Assert.All(buffer, value => Assert.Equal(0, value)));
    }

    [Fact]
    public void Encryption_AuthenticatesPurposeVersionNonceCiphertextAndTag()
    {
        var keys = new KeyStore();
        var protector = new KeyringOAuthDataProtector(keys);
        byte[] purpose = [1];
        var encrypted = protector.Protect([11, 22, 33], purpose);
        Assert.NotEqual(encrypted, protector.Protect([11, 22, 33], purpose));
        foreach (var offset in new[] { 0, 1, 13, 29 })
        {
            var changed = encrypted.ToArray();
            changed[offset] ^= 1;
            AssertUnavailable(() => protector.Unprotect(changed, purpose));
        }
        // Even the same key cannot decrypt data moved to a different namespace.
        keys.Keys[Convert.ToHexString(SHA256.HashData(new byte[] { 2 }))] = keys.Keys.Values.Single().ToArray();
        AssertUnavailable(() => protector.Unprotect(encrypted, [2]));
        AssertUnavailable(() => protector.Unprotect([], purpose));
    }

    [Theory]
    [InlineData("read")]
    [InlineData("create")]
    [InlineData("not-persisted")]
    [InlineData("invalid-key")]
    public async Task KeyringFailure_NeverPublishesCredentialsOrLeaksNativeErrors(string failure)
    {
        var folder = TemporaryFolder();
        try
        {
            var keys = new KeyStore { Failure = failure };
            var store = new ProtectedFileOAuthTokenStore(folder, new KeyringOAuthDataProtector(keys));
            var error = await Assert.ThrowsAsync<OAuthException>(() => store.WriteAsync("plugin", Session("personal"), default));
            Assert.Equal(OAuthFailure.SecureStorageUnavailable, error.Failure);
            Assert.DoesNotContain("sensitive", error.ToString());
            Assert.Null(error.InnerException);
            Assert.False(Directory.Exists(folder));
        }
        finally { Cleanup(folder); }
    }

    [Fact]
    public async Task LockedOrLostKey_PreservesExistingCiphertext_AndDeletionStillWorks()
    {
        var keys = new KeyStore();
        var folder = TemporaryFolder();
        try
        {
            var store = new ProtectedFileOAuthTokenStore(folder, new KeyringOAuthDataProtector(keys));
            await store.WriteAsync("plugin", Session("personal"), default);
            var path = Directory.GetFiles(folder, "*.token", SearchOption.AllDirectories).Single();
            var original = await File.ReadAllBytesAsync(path);
            keys.Failure = "read";
            Assert.Equal(OAuthFailure.SecureStorageUnavailable,
                (await Assert.ThrowsAsync<OAuthException>(() => store.ReadAsync("plugin", "personal", default))).Failure);
            await Assert.ThrowsAsync<OAuthException>(() => store.GetAccountsAsync("plugin", default));
            await Assert.ThrowsAsync<OAuthException>(() => store.WriteAsync("plugin", Session("personal"), default));
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
            keys.Failure = null;
            keys.Keys.Clear();
            await Assert.ThrowsAsync<OAuthException>(() => store.ReadAsync("plugin", "personal", default));
            Assert.Empty(keys.Keys); // Decryption never creates a replacement key.
            await store.DeleteAsync("plugin", "personal", default);
            await store.DeleteAsync("plugin", "personal", default);
            Assert.Null(await store.ReadAsync("plugin", "personal", default));
        }
        finally { Cleanup(folder); }
    }

    [Fact]
    public async Task Cancellation_DoesNotWriteSession()
    {
        var folder = TemporaryFolder();
        try
        {
            var keys = new KeyStore();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var store = new ProtectedFileOAuthTokenStore(folder, new KeyringOAuthDataProtector(keys));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.WriteAsync("plugin", Session("personal"), cts.Token));
            Assert.Empty(keys.Keys);
            Assert.False(Directory.Exists(folder));
        }
        finally { Cleanup(folder); }
    }

    [NativeKeyringFact]
    public async Task NativeKeyring_PersistenceAndTamperDetection()
    {
        var namespaces = new[] { "skycd-native-test-" + Guid.NewGuid().ToString("N"), "skycd-native-test-" + Guid.NewGuid().ToString("N") };
        try { await CheckPersistenceAsync(OAuthDataProtectorFactory.Create, namespaces); }
        finally
        {
            foreach (var ns in namespaces)
            {
                var keyId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ns)));
                if (OperatingSystem.IsMacOS()) new MacOSOAuthKeyStore().Delete(keyId);
                if (OperatingSystem.IsLinux()) new LinuxOAuthKeyStore().Delete(keyId);
            }
        }
    }

    [UnavailableKeyringFact]
    public void NativeKeyring_Unavailable_FailsClosed()
    {
        // CI supplies either a locked macOS test keychain or an unreachable Linux session bus.
        var protector = OAuthDataProtectorFactory.Create();
        AssertUnavailable(() => protector.Protect([1], Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())));
    }

    private static async Task CheckPersistenceAsync(Func<IOAuthDataProtector> create, string[]? namespaces = null)
    {
        namespaces ??= ["plugin-a", "plugin-b"];
        var folder = TemporaryFolder();
        try
        {
            var store = new ProtectedFileOAuthTokenStore(folder, create());
            await Task.WhenAll(new[] { "personal", "work" }.Select(account => store.WriteAsync(namespaces[0], Session(account), default)));
            await store.WriteAsync(namespaces[1], Session("personal", "other-token"), default);
            foreach (var file in Directory.GetFiles(folder, "*.token", SearchOption.AllDirectories))
            {
                var data = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file));
                Assert.DoesNotContain("secret-access", data);
                Assert.DoesNotContain("secret-refresh", data);
                Assert.DoesNotContain("personal", data);
            }
            var restarted = new ProtectedFileOAuthTokenStore(folder, create());
            var restored = await restarted.ReadAsync(namespaces[0], "personal", default);
            Assert.NotNull(restored);
            Assert.Equal("personal", restored.AccountId);
            Assert.Equal("secret-access", restored.AccessToken);
            Assert.Equal("secret-refresh", restored.RefreshToken);
            Assert.Equal(Session("personal").ExpiresAt, restored.ExpiresAt);
            Assert.Equal(new[] { "personal", "work" }, await restarted.GetAccountsAsync(namespaces[0], default));
            Assert.Equal("other-token", (await restarted.ReadAsync(namespaces[1], "personal", default))!.AccessToken);
            var accountFiles = Directory.GetFiles(folder, "*.token", SearchOption.AllDirectories);
            var workName = Convert.ToHexString(SHA256.HashData("work"u8)) + ".token";
            var personalName = Convert.ToHexString(SHA256.HashData("personal"u8)) + ".token";
            var workFile = accountFiles.Single(file => Path.GetFileName(file) == workName);
            var personalFile = Path.Combine(Path.GetDirectoryName(workFile)!, personalName);
            var personalBytes = await File.ReadAllBytesAsync(personalFile);
            await File.WriteAllBytesAsync(personalFile, await File.ReadAllBytesAsync(workFile));
            await Assert.ThrowsAsync<OAuthException>(() => restarted.ReadAsync(namespaces[0], "personal", default));
            await Assert.ThrowsAsync<OAuthException>(() => restarted.GetAccountsAsync(namespaces[0], default));
            await File.WriteAllBytesAsync(personalFile, personalBytes);
            await restarted.DeleteAsync(namespaces[0], "personal", default);
            Assert.Equal(new[] { "work" }, await restarted.GetAccountsAsync(namespaces[0], default));
            Assert.Single(await restarted.GetAccountsAsync(namespaces[1], default));
            var remaining = Directory.GetFiles(folder, "*.token", SearchOption.AllDirectories);
            var work = remaining.Single(file => Path.GetFileName(file) == Convert.ToHexString(SHA256.HashData("work"u8)) + ".token");
            var other = remaining.Single(file => file != work);
            // Swapping whole valid files between accounts and between namespaces must fail closed.
            var saved = await File.ReadAllBytesAsync(work);
            await File.WriteAllBytesAsync(work, await File.ReadAllBytesAsync(other));
            await Assert.ThrowsAsync<OAuthException>(() => restarted.ReadAsync(namespaces[0], "work", default));
            saved[^1] ^= 1;
            await File.WriteAllBytesAsync(work, saved);
            await Assert.ThrowsAsync<OAuthException>(() => restarted.ReadAsync(namespaces[0], "work", default));
            await restarted.DeleteAsync(namespaces[0], "work", default);
            Assert.Empty(await restarted.GetAccountsAsync(namespaces[0], default));
        }
        finally { Cleanup(folder); }
    }

    private static OAuthSession Session(string account, string accessToken = "secret-access") => new()
    {
        AccountId = account,
        AccessToken = accessToken,
        RefreshToken = "secret-refresh",
        ExpiresAt = DateTimeOffset.UnixEpoch.AddHours(1)
    };
    private static string TemporaryFolder() => Path.Combine(Path.GetTempPath(), "skycd-oauth-tests", Guid.NewGuid().ToString("N"));
    private static void Cleanup(string folder) { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    private static void AssertUnavailable(Action action) => Assert.Equal(OAuthFailure.SecureStorageUnavailable, Assert.Throws<OAuthException>(action).Failure);

    private sealed class KeyStore : IOAuthKeyStore
    {
        public Dictionary<string, byte[]> Keys { get; } = new();
        public List<byte[]> ReturnedBuffers { get; } = [];
        public string? Failure { get; set; }
        public byte[]? Read(string keyId)
        {
            if (Failure == "read") throw new IOException("sensitive native error");
            if (!Keys.TryGetValue(keyId, out var value)) return null;
            var result = Failure == "invalid-key" ? new byte[1] : value.ToArray();
            ReturnedBuffers.Add(result);
            return result;
        }
        public void Create(string keyId, byte[] key)
        {
            if (Failure == "create") throw new IOException("sensitive native error");
            if (Failure != "not-persisted") Keys.Add(keyId, key.ToArray());
        }
    }
}

internal sealed class NativeKeyringFactAttribute : FactAttribute
{
    public NativeKeyringFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SKYCD_TEST_NATIVE_OAUTH") != "1")
            Skip = "Requires an isolated, unlocked OS keyring; enabled in native OAuth CI.";
    }
}

internal sealed class UnavailableKeyringFactAttribute : FactAttribute
{
    public UnavailableKeyringFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SKYCD_TEST_UNAVAILABLE_OAUTH") != "1")
            Skip = "Requires a deliberately unavailable/locked OS keyring; enabled in native OAuth CI.";
    }
}
