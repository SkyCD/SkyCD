using System;
using System.IO;
using System.Threading;
using FxSsh;
using SkyCD.Presentation.Scanning;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Integration coverage for the SSH.NET-based SFTP transport (#624). Every test drives the real
/// <c>SftpCatalogConnector</c> against <see cref="ScriptedSftpServer"/>, so the SSH handshake, the
/// SFTP listings and the exception mapping are exercised end to end over a real loopback socket.
/// </summary>
public sealed class SftpCatalogTransportTests
{
    private static readonly DateTime Stamp = new(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);
    private const string Username = "alice";
    private const string Password = "secret-pass";

    // ------------------------------------------------------------------ helpers

    private static RemoteLocation Location(int port, string path = "/nested") =>
        RemoteLocation.Parse($"sftp://127.0.0.1:{port}{path}");

    private static RemoteCredentials Credentials(string? password = Password) =>
        new(Username, password, anonymous: false);

    private static void AssertModified(DateTime expected, DateTime? actual)
    {
        Assert.True(actual.HasValue, "modified timestamp is missing");
        var delta = (expected - actual!.Value).Duration();
        Assert.True(delta <= TimeSpan.FromSeconds(5), $"expected modified {expected:O} but got {actual:O}");
    }

    private sealed class TempHostKeys : IDisposable
    {
        private readonly TestWorkspace workspace = new();

        public TempHostKeys()
        {
            Store = new SftpHostKeyStore(workspace.GetPath("sftp-known-hosts.json"));
            Service = new SftpHostKeyService(Store);
        }

        public SftpHostKeyStore Store { get; }

        public SftpHostKeyService Service { get; }

        public SftpHostKeyInfo Trust(ScriptedSftpServer server)
        {
            var presented = Service.FetchHostKey("127.0.0.1", server.Port, CancellationToken.None);
            Service.Trust("127.0.0.1", server.Port, presented);
            return presented;
        }

        public void Dispose()
        {
            workspace.Dispose();
        }
    }

    // ------------------------------------------------------------------ nested listings

    [Fact]
    public void Connect_PasswordAuth_NestedListingsCarrySizeAndTimestamps()
    {
        using var server = new ScriptedSftpServer();
        server.SeedFile("/nested/child/file.txt", 42, Stamp);
        server.SeedFile("/nested/top.txt", 7, Stamp);
        using var hostKeys = new TempHostKeys();
        hostKeys.Trust(server);

        var connector = new SftpCatalogConnector(hostKeys.Service);
        using var session = connector.Connect(Location(server.Port), Credentials(), CancellationToken.None);

        var nested = session.ListDirectory("/nested");
        Assert.Equal(2, nested.Count);

        var top = Assert.Single(nested, entry => entry.Name == "top.txt");
        Assert.False(top.IsDirectory);
        Assert.Equal(7, top.Size);
        Assert.Equal("rw-r--r--", top.Permission);
        AssertModified(Stamp, top.ModifiedUtc);
        Assert.Equal("/nested/top.txt", top.VisitedIdentity);

        var child = Assert.Single(nested, entry => entry.Name == "child");
        Assert.True(child.IsDirectory);
        Assert.Equal(0, child.Size);
        Assert.Equal("/nested/child", child.VisitedIdentity);

        var listing = session.ListDirectory("/nested/child");
        var file = Assert.Single(listing, entry => entry.Name == "file.txt");
        Assert.False(file.IsDirectory);
        Assert.Equal(42, file.Size);
        Assert.Equal("rw-r--r--", file.Permission);
        AssertModified(Stamp, file.ModifiedUtc);
        Assert.False(string.IsNullOrEmpty(file.VisitedIdentity));
        Assert.Equal("/nested/child/file.txt", file.VisitedIdentity);
    }

    // ------------------------------------------------------------------ authentication

    [Fact]
    public void Connect_PrivateKeyAuth_Connects()
    {
        using var server = new ScriptedSftpServer();
        server.SeedFile("/nested/top.txt", 7, Stamp);
        server.AcceptPublicKey = true;
        using var hostKeys = new TempHostKeys();
        hostKeys.Trust(server);

        using var workspace = new TestWorkspace();
        var keyPath = workspace.GetPath("id_rsa.pem");
        File.WriteAllText(keyPath, KeyGenerator.GenerateRsaKeyPem(2048));

        var connector = new SftpCatalogConnector(hostKeys.Service);
        using var session = connector.Connect(
            Location(server.Port),
            new RemoteCredentials(Username, null, anonymous: false, RemoteAuthMethod.PrivateKey, keyPath, null),
            CancellationToken.None);

        var nested = session.ListDirectory("/nested");
        Assert.Contains(nested, entry => entry.Name == "top.txt");
        Assert.True(server.SawPublicKeyAuth);
    }

    [Fact]
    public void Connect_RejectedCredentials_FailsWithTypedAuthenticationError()
    {
        using var server = new ScriptedSftpServer();
        server.AcceptAuthentication = false;
        using var hostKeys = new TempHostKeys();
        hostKeys.Trust(server);

        var connector = new SftpCatalogConnector(hostKeys.Service);
        const string wrongPassword = "wrong-secret-9234";
        var exception = Assert.Throws<RemoteAuthenticationException>(() =>
            connector.Connect(Location(server.Port), Credentials(wrongPassword), CancellationToken.None));

        Assert.True(server.UserAuthAttempts > 0);
        Assert.DoesNotContain(wrongPassword, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(server.Password, exception.ToString(), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ host keys

    [Fact]
    public void Connect_UnknownHostKey_FailsWithTypedErrorAndNoImplicitTrust()
    {
        using var server = new ScriptedSftpServer();
        using var hostKeys = new TempHostKeys();

        var connector = new SftpCatalogConnector(hostKeys.Service);
        var exception = Assert.Throws<RemoteHostKeyException>(() =>
            connector.Connect(Location(server.Port), Credentials(), CancellationToken.None));

        Assert.False(exception.Changed);
        Assert.Equal($"127.0.0.1:{server.Port}", exception.Target);
        Assert.StartsWith("SHA256:", exception.Fingerprint, StringComparison.Ordinal);
        Assert.Equal(server.ExpectedHostKeyFingerprint, exception.Fingerprint);

        // The failed attempt must not have persisted anything.
        Assert.Null(hostKeys.Service.Find("127.0.0.1", server.Port));
        Assert.Equal(SftpHostKeyState.Unknown, hostKeys.Service.Evaluate("127.0.0.1", server.Port, exception.Fingerprint));
    }

    [Fact]
    public void Connect_ChangedHostKey_FailsWithTypedErrorUntilReplaced()
    {
        using var server = new ScriptedSftpServer();
        server.SeedFile("/nested/top.txt", 7, Stamp);
        using var hostKeys = new TempHostKeys();
        var wrong = new SftpHostKeyInfo("SHA256:" + Convert.ToBase64String(new byte[32]).TrimEnd('='), "rsa-sha2-256");
        hostKeys.Service.Trust("127.0.0.1", server.Port, wrong);

        var connector = new SftpCatalogConnector(hostKeys.Service);
        var exception = Assert.Throws<RemoteHostKeyException>(() =>
            connector.Connect(Location(server.Port), Credentials(), CancellationToken.None));

        Assert.True(exception.Changed);
        Assert.Equal(server.ExpectedHostKeyFingerprint, exception.Fingerprint);

        // Explicit replacement of the stored key unblocks the connection.
        hostKeys.Trust(server);
        using var session = connector.Connect(Location(server.Port), Credentials(), CancellationToken.None);
        Assert.Contains(session.ListDirectory("/nested"), entry => entry.Name == "top.txt");
    }

    // ------------------------------------------------------------------ cancellation and drops

    [Fact]
    public void Connect_CancelledToken_ThrowsOperationCanceled()
    {
        using var server = new ScriptedSftpServer();
        using var hostKeys = new TempHostKeys();
        hostKeys.Trust(server);

        var connector = new SftpCatalogConnector(hostKeys.Service);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            connector.Connect(Location(server.Port), Credentials(), cancelled.Token));
    }

    [Fact]
    public void ListDirectory_AfterServerStops_FailsWithTypedConnectionError()
    {
        using var server = new ScriptedSftpServer();
        server.SeedFile("/nested/child/file.txt", 42, Stamp);
        using var hostKeys = new TempHostKeys();
        hostKeys.Trust(server);

        var connector = new SftpCatalogConnector(hostKeys.Service);
        using var session = connector.Connect(Location(server.Port), Credentials(), CancellationToken.None);
        Assert.Contains(session.ListDirectory("/nested"), entry => entry.Name == "child");

        server.StopAbruptly();

        var exception = Assert.Throws<RemoteConnectionException>(() => session.ListDirectory("/nested/child"));
        Assert.DoesNotContain(Password, exception.ToString(), StringComparison.Ordinal);
    }
}
