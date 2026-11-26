using System;
using System.IO;
using SkyCD.Presentation.Scanning;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class SftpHostKeyStoreTests : IDisposable
{
    private readonly TestWorkspace workspace = new();

    private string storePath => workspace.GetPath("sftp-known-hosts.json");

    [Fact]
    public void Find_EmptyStore_ReturnsNull()
    {
        var store = new SftpHostKeyStore(storePath);

        Assert.Null(store.Find("example.com", 22));
    }

    [Fact]
    public void Trust_PersistsTheRecordAndMatchesHostCaseInsensitively()
    {
        var store = new SftpHostKeyStore(storePath);
        store.Trust("Example.COM", 22, new SftpHostKeyInfo("SHA256:abc", "ssh-ed25519"));

        var reloaded = new SftpHostKeyStore(storePath);
        var record = reloaded.Find("example.com", 22);

        Assert.NotNull(record);
        Assert.Equal("SHA256:abc", record.Fingerprint);
        Assert.Equal("ssh-ed25519", record.KeyName);
        Assert.Equal(22, record.Port);
    }

    [Fact]
    public void Trust_SameEndpointTwice_ReplacesInsteadOfDuplicating()
    {
        var store = new SftpHostKeyStore(storePath);
        store.Trust("example.com", 22, new SftpHostKeyInfo("SHA256:old", "ssh-rsa"));
        store.Trust("example.com", 22, new SftpHostKeyInfo("SHA256:new", "ssh-ed25519"));

        var record = store.Find("example.com", 22);

        Assert.NotNull(record);
        Assert.Equal("SHA256:new", record.Fingerprint);
        Assert.True(store.Remove("example.com", 22));
        Assert.Null(store.Find("example.com", 22));
    }

    [Fact]
    public void Find_DoesNotMatchOtherPortsOrHosts()
    {
        var store = new SftpHostKeyStore(storePath);
        store.Trust("example.com", 22, new SftpHostKeyInfo("SHA256:abc", "ssh-rsa"));

        Assert.Null(store.Find("example.com", 2222));
        Assert.Null(store.Find("other.com", 22));
    }

    [Fact]
    public void Remove_UnknownEntry_ReturnsFalse()
    {
        var store = new SftpHostKeyStore(storePath);

        Assert.False(store.Remove("example.com", 22));
    }

    [Fact]
    public void Evaluate_UnknownTrustedAndChangedFollowTheStore()
    {
        var service = new SftpHostKeyService(new SftpHostKeyStore(storePath));

        Assert.Equal(SftpHostKeyState.Unknown, service.Evaluate("example.com", 22, "SHA256:abc"));

        service.Trust("example.com", 22, new SftpHostKeyInfo("SHA256:abc", "ssh-rsa"));
        Assert.Equal(SftpHostKeyState.Trusted, service.Evaluate("example.com", 22, "SHA256:abc"));
        Assert.Equal(SftpHostKeyState.Changed, service.Evaluate("example.com", 22, "SHA256:other"));
    }

    [Fact]
    public void FetchHostKey_ReturnsThePresentedKeyWithoutPersistingTrust()
    {
        var presented = new SftpHostKeyInfo("SHA256:probe", "ssh-ed25519");
        var service = new SftpHostKeyService(
            new SftpHostKeyStore(storePath),
            (_, _, _) => presented);

        var fetched = service.FetchHostKey("example.com", 22, default);

        Assert.Same(presented, fetched);
        Assert.Equal(SftpHostKeyState.Unknown, service.Evaluate("example.com", 22, "SHA256:probe"));
    }

    public void Dispose()
    {
        workspace.Dispose();
    }
}
