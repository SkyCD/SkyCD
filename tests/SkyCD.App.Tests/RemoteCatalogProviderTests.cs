using System;
using System.Collections.Generic;
using System.Threading;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class RemoteCatalogProviderTests
{
    [Theory]
    [InlineData("example.com", 21, "/")]
    [InlineData("ftp://example.com/a//b/../c/", 21, "/a/c")]
    [InlineData("ftp://example.com:2121/a%20b/", 2121, "/a b")]
    public void Parse_FtpDefaultsAndNormalizesPath(string address, int port, string path)
    {
        var location = RemoteLocation.Parse(address);
        Assert.Equal("ftp", location.Scheme);
        Assert.Equal(port, location.Port);
        Assert.Equal(path, location.Path);
    }

    [Fact]
    public void Credentials_DoNotExposePasswordInStringRepresentation()
    {
        var credentials = new RemoteCredentials("alice", "secret", false);
        Assert.DoesNotContain("secret", credentials.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_SelectsProviderAndUsesItsPathsAndCredentials()
    {
        var provider = new TestProvider();
        var registry = new RemoteCatalogProviderRegistry([new FtpCatalogConnector(), provider]);
        var location = RemoteLocation.Parse("TEST://alice:secret@example.com/root/", registry);
        Assert.Equal("test", location.Scheme);
        Assert.Equal(4321, location.Port);
        Assert.Equal("/ROOT", location.Path);
        Assert.DoesNotContain("alice", location.PublicUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", location.PublicUrl, StringComparison.Ordinal);
        Assert.Equal(9876, RemoteLocation.Parse("test://example.com:9876/root", registry).Port);
        Assert.Same(provider, registry.Get("TEST"));

        var login = LoginDialogViewModel.FromAddress("test://alice:secret@example.com/root/", registry);
        Assert.Equal("alice", login.Username);
        Assert.Equal("secret", login.Password);
        Assert.True(login.CanConfirm);
        login.UseAnonymous = true;
        Assert.False(login.CanConfirm);

        var result = new RemoteCatalogScanner(registry).ScanDetailed(new RemoteScanOptions(
            "test://example.com/root/", "alice", "secret", false, null, null, true));
        Assert.Equal("alice", provider.Username);
        Assert.Equal("secret", provider.Password);
        Assert.Equal(["/ROOT", "/ROOT/CHILD"], provider.Listed);
        Assert.Contains(result.Documents, document => document.Name == "file.txt" && document.Size == 42);
        Assert.Equal(3, result.Statistics.Discovered);
    }

    [Fact]
    public void Scan_SecondProviderReconnectsAfterTransientDisconnect()
    {
        var provider = new TestProvider { DropFirstListing = true };
        var registry = new RemoteCatalogProviderRegistry([provider]);
        var result = new RemoteCatalogScanner(registry).ScanDetailed(new RemoteScanOptions(
            "test://example.com/root", "alice", "secret", false, null, null, false,
            new ScanTraversalOptions { MaxRetries = 1, RetryDelay = TimeSpan.Zero }));

        Assert.Equal(2, provider.Connections);
        Assert.Contains(result.Documents, document => document.Name == "file.txt");
        Assert.Equal(0, result.Statistics.Failed);
    }

    [Fact]
    public void UnsupportedScheme_IsLocalizedTypedAndNeverConnects()
    {
        var provider = new TestProvider();
        var registry = new RemoteCatalogProviderRegistry([provider]);
        var exception = Assert.Throws<RemoteSchemeNotSupportedException>(() =>
            new RemoteCatalogScanner(registry).Scan(new RemoteScanOptions(
                "smb://alice:secret@example.com/root", null, null, false, null, null, false)));
        Assert.Equal("smb", exception.Scheme);
        Assert.Contains("smb", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.Ordinal);
        Assert.Empty(provider.Listed);
        Assert.Throws<RemoteSchemeNotSupportedException>(() => LoginDialogViewModel.FromAddress("smb://example.com", registry));
    }

    private sealed class TestProvider : IRemoteCatalogConnector, IRemoteCatalogSession
    {
        public string Scheme => "test";
        public int DefaultPort => 4321;
        public string? Username { get; private set; }
        public string? Password { get; private set; }
        public bool DropFirstListing { get; init; }
        public int Connections { get; private set; }
        public List<string> Listed { get; } = [];

        public string NormalizePath(string? path) =>
            string.IsNullOrWhiteSpace(path) || path == "/" ? "/" : path.TrimEnd('/').ToUpperInvariant();

        public bool CanConfirmCredentials(RemoteCredentials credentials) =>
            !credentials.Anonymous && !string.IsNullOrWhiteSpace(credentials.Username) && !string.IsNullOrWhiteSpace(credentials.Password);

        public (string Username, string Password) ResolveCredentials(RemoteCredentials credentials) =>
            (credentials.Username!, credentials.Password!);

        public RemoteConnectionException ConnectionFailed(RemoteLocation location, Exception? cause = null, string? detail = null) =>
            new("test", location.Host, cause, detail);

        public IRemoteCatalogSession Connect(RemoteLocation location, RemoteCredentials credentials, CancellationToken cancellationToken)
        {
            Connections++;
            Username = credentials.Username;
            Password = credentials.Password;
            return this;
        }

        public IReadOnlyList<RemoteEntry> ListDirectory(string path)
        {
            Listed.Add(path);
            if (DropFirstListing && Listed.Count == 1)
            {
                throw new RemoteConnectionException("test", "Connection lost");
            }

            return path == "/ROOT"
                ? [new RemoteEntry("child", "/root/child/", "/ROOT", true, 0, null, null)]
                : [new RemoteEntry("file.txt", "/ROOT/CHILD/file.txt", "/ROOT/CHILD", false, 42, null, null)];
        }

        public void Dispose()
        {
        }
    }
}
