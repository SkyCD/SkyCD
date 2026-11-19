using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class SftpCatalogProviderTests : IDisposable
{
    private readonly string storePath = Path.Combine(
        Path.GetTempPath(),
        "skycd-sftp-provider-tests",
        Guid.NewGuid().ToString("N"),
        "sftp-known-hosts.json");

    [Fact]
    public void Defaults_AreRegisteredForTheSftpScheme()
    {
        var connector = RemoteCatalogProviderRegistry.Default.Get("sftp");

        Assert.IsType<SftpCatalogConnector>(connector);
        Assert.Equal("sftp", connector.Scheme);
        Assert.Equal(22, connector.DefaultPort);
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("a//b/../c/", "/a/c")]
    [InlineData("\\a\\b", "/a/b")]
    public void NormalizePath_IsStableDirectoryIdentity(string? path, string expected)
    {
        var connector = new SftpCatalogConnector(new SftpHostKeyService(new SftpHostKeyStore(storePath)));

        Assert.Equal(expected, connector.NormalizePath(path));
    }

    [Fact]
    public void CanConfirmCredentials_GatesPasswordAndKeyAuthentication()
    {
        var connector = new SftpCatalogConnector(new SftpHostKeyService(new SftpHostKeyStore(storePath)));

        Assert.False(connector.CanConfirmCredentials(new RemoteCredentials("alice", null, false)));
        Assert.False(connector.CanConfirmCredentials(new RemoteCredentials(null, "secret", false)));
        Assert.True(connector.CanConfirmCredentials(new RemoteCredentials("alice", "secret", false)));
        Assert.True(connector.CanConfirmCredentials(new RemoteCredentials("alice", "secret", true)));

        var keyAuth = new RemoteCredentials("alice", null, false, RemoteAuthMethod.PrivateKey);
        Assert.False(connector.CanConfirmCredentials(keyAuth));
        Assert.False(connector.CanConfirmCredentials(new RemoteCredentials("alice", null, false, RemoteAuthMethod.PrivateKey, "   ")));
        Assert.True(connector.CanConfirmCredentials(
            new RemoteCredentials("alice", null, false, RemoteAuthMethod.PrivateKey, @"C:\keys\id_ed25519")));
    }

    [Fact]
    public void ResolveCredentials_ValidatesBeforeAnyConnection()
    {
        var connector = new SftpCatalogConnector(new SftpHostKeyService(new SftpHostKeyStore(storePath)));

        var (username, password) = connector.ResolveCredentials(new RemoteCredentials(" alice ", "secret", false));
        Assert.Equal("alice", username);
        Assert.Equal("secret", password);

        var (keyUser, keyPassword) = connector.ResolveCredentials(
            new RemoteCredentials("alice", null, false, RemoteAuthMethod.PrivateKey, @"C:\keys\id_ed25519", "passphrase"));
        Assert.Equal("alice", keyUser);
        Assert.Equal(string.Empty, keyPassword);

        Assert.Throws<RemoteAuthenticationException>(() =>
            connector.ResolveCredentials(new RemoteCredentials("alice", "", false)));
        Assert.Throws<RemoteAuthenticationException>(() =>
            connector.ResolveCredentials(new RemoteCredentials("", "secret", false)));
        Assert.Throws<RemoteAuthenticationException>(() =>
            connector.ResolveCredentials(new RemoteCredentials("alice", null, false, RemoteAuthMethod.PrivateKey)));
    }

    [Fact]
    public void ConnectionFailed_IsLocalizedTypedAndSecretSafe()
    {
        var connector = new SftpCatalogConnector(new SftpHostKeyService(new SftpHostKeyStore(storePath)));
        var location = RemoteLocation.Parse("sftp://example.com:2222/pub");

        var exception = connector.ConnectionFailed(location, detail: "boom");

        Assert.Contains("SFTP", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("example.com:2222", exception.Message, StringComparison.Ordinal);
        Assert.Contains("(boom)", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_WithFakeSession_BuildsDocumentsFromNestedListings()
    {
        var session = new FakeSftpSession();
        session.Directories["/"] =
        [
            new RemoteEntry("child", "/child", "/", true, 0, "rwxr-xr-x", null),
            new RemoteEntry("top.txt", "/top.txt", "/", false, 7, "rw-r--r--", null),
        ];
        session.Directories["/child"] =
        [
            new RemoteEntry("file.txt", "/child/file.txt", "/child", false, 42, "rw-r--r--", null),
        ];
        var connector = new FakeSftpConnector(session);

        var result = new RemoteCatalogScanner(connector).ScanDetailed(
            new RemoteScanOptions("sftp://example.com/", "alice", "secret", false, null, null, true));

        Assert.Equal(0, result.Statistics.Failed);
        Assert.Contains(result.Documents, document => document.Name == "file.txt" && document.Size == 42);
        Assert.Contains(result.Documents, document => document.Name == "top.txt" && document.Size == 7);
        Assert.Equal(4, result.Statistics.Discovered);
    }

    [Fact]
    public void Scan_SymlinkDirectoryCycle_IsSkippedThroughVisitedIdentity()
    {
        var session = new FakeSftpSession();
        session.Directories["/"] =
        [
            new RemoteEntry("a", "/a", "/", true, 0, null, null),
        ];
        session.Directories["/a"] =
        [
            new RemoteEntry("file.txt", "/a/file.txt", "/a", false, 42, null, null),

            // A symlinked directory that resolves back to the already visited root.
            new RemoteEntry("loop", "/a/loop", "/a", true, 0, null, null, VisitedIdentity: "/"),
        ];
        var connector = new FakeSftpConnector(session);

        var result = new RemoteCatalogScanner(connector).ScanDetailed(new RemoteScanOptions(
            "sftp://example.com/", "alice", "secret", false, null, null, false,
            new ScanTraversalOptions { MaxRetries = 0, RetryDelay = TimeSpan.Zero }));

        Assert.Equal(0, result.Statistics.Failed);
        Assert.Equal(1, result.Statistics.Skipped);
        Assert.DoesNotContain("/a/loop", session.Listed);
        Assert.Contains(result.Documents, document => document.Name == "file.txt");
    }

    [Fact]
    public void Scan_UntrustedHostKey_SurfacesTheTypedException()
    {
        var connector = new FakeSftpConnector(new FakeSftpSession())
        {
            ConnectFailure = () => new RemoteHostKeyException("example.com:22", "SHA256:abc", changed: false),
        };

        var exception = Assert.Throws<RemoteHostKeyException>(() =>
            new RemoteCatalogScanner(connector).Scan(new RemoteScanOptions(
                "sftp://example.com/", "alice", "secret", false, null, null, false)));

        Assert.Equal("example.com:22", exception.Target);
        Assert.Equal("SHA256:abc", exception.Fingerprint);
        Assert.False(exception.Changed);
    }

    [Fact]
    public void LoginDialog_SftpScheme_ExposesKeyAndHostKeyFlow()
    {
        var presented = new SftpHostKeyInfo("SHA256:probe", "ssh-ed25519");
        var service = new SftpHostKeyService(new SftpHostKeyStore(storePath), (_, _, _) => presented);
        var viewModel = LoginDialogViewModel.FromAddress("sftp://example.com:2222/pub", hostKeys: service);

        Assert.True(viewModel.IsSftp);
        Assert.False(viewModel.IsAnonymousAvailable);
        Assert.Equal(2222, viewModel.Port);
        Assert.False(viewModel.ConfirmCommand.CanExecute(null));

        viewModel.Username = "alice";
        viewModel.Password = "secret";
        Assert.True(viewModel.ConfirmCommand.CanExecute(null));

        viewModel.AuthMethodIndex = 1;
        Assert.True(viewModel.IsKeyAuthentication);
        Assert.False(viewModel.ConfirmCommand.CanExecute(null));
        viewModel.KeyFilePath = Path.Combine("keys", "id_ed25519");
        Assert.True(viewModel.ConfirmCommand.CanExecute(null));

        viewModel.Port = 3333;
        Assert.Equal("sftp://example.com:3333/pub", viewModel.EffectiveAddress);
    }

    [Fact]
    public async System.Threading.Tasks.Task LoginDialog_HostKeyConfirmation_IsExplicitAndPersistent()
    {
        var service = new SftpHostKeyService(new SftpHostKeyStore(storePath), (_, _, _) =>
            new SftpHostKeyInfo("SHA256:probe", "ssh-ed25519"));
        var viewModel = LoginDialogViewModel.FromAddress("sftp://example.com/pub", hostKeys: service);

        Assert.False(viewModel.TrustHostKeyCommand.CanExecute(null));

        await viewModel.CheckHostKeyCommand.ExecuteAsync(null);
        Assert.Equal("SHA256:probe", viewModel.HostKeyFingerprint);
        Assert.False(viewModel.HostKeyIsTrusted);
        Assert.True(viewModel.TrustHostKeyCommand.CanExecute(null));

        viewModel.TrustHostKeyCommand.Execute(null);
        Assert.True(viewModel.HostKeyIsTrusted);
        Assert.False(viewModel.TrustHostKeyCommand.CanExecute(null));
        Assert.Equal(SftpHostKeyState.Trusted, service.Evaluate("example.com", 22, "SHA256:probe"));

        var reopened = LoginDialogViewModel.FromAddress("sftp://example.com/pub", hostKeys: service);
        Assert.True(reopened.HostKeyIsTrusted);
        Assert.Equal("SHA256:probe", reopened.HostKeyFingerprint);
    }

    [Fact]
    public async System.Threading.Tasks.Task LoginDialog_ChangedHostKey_OffersReplacement()
    {
        var service = new SftpHostKeyService(new SftpHostKeyStore(storePath), (_, _, _) =>
            new SftpHostKeyInfo("SHA256:new", "ssh-ed25519"));
        service.Trust("example.com", 22, new SftpHostKeyInfo("SHA256:old", "ssh-rsa"));

        var viewModel = LoginDialogViewModel.FromAddress("sftp://example.com/pub", hostKeys: service);
        await viewModel.CheckHostKeyCommand.ExecuteAsync(null);

        Assert.False(viewModel.HostKeyIsTrusted);
        Assert.Equal("SHA256:new", viewModel.HostKeyFingerprint);

        viewModel.TrustHostKeyCommand.Execute(null);
        Assert.Equal(SftpHostKeyState.Trusted, service.Evaluate("example.com", 22, "SHA256:new"));
    }

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(storePath);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeSftpConnector : IRemoteCatalogConnector
    {
        private readonly IRemoteCatalogSession session;

        public FakeSftpConnector(IRemoteCatalogSession session) => this.session = session;

        public Func<Exception>? ConnectFailure { get; init; }

        public string Scheme => "sftp";

        public int DefaultPort => 22;

        public string NormalizePath(string? path) => FtpPathNormalize(path);

        public bool CanConfirmCredentials(RemoteCredentials credentials) => true;

        public (string Username, string Password) ResolveCredentials(RemoteCredentials credentials) =>
            (credentials.Username ?? string.Empty, credentials.Password ?? string.Empty);

        public RemoteConnectionException ConnectionFailed(RemoteLocation location, Exception? cause = null, string? detail = null) =>
            new("sftp", location.Host, cause, detail);

        public IRemoteCatalogSession Connect(RemoteLocation location, RemoteCredentials credentials, CancellationToken cancellationToken)
        {
            if (ConnectFailure is not null)
            {
                throw ConnectFailure();
            }

            return session;
        }

        private static string FtpPathNormalize(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "/";
            }

            var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var stack = new List<string>(parts.Length);
            foreach (var part in parts)
            {
                if (part == ".")
                {
                    continue;
                }

                if (part == "..")
                {
                    if (stack.Count > 0)
                    {
                        stack.RemoveAt(stack.Count - 1);
                    }

                    continue;
                }

                stack.Add(part);
            }

            return stack.Count == 0 ? "/" : "/" + string.Join("/", stack);
        }
    }

    private sealed class FakeSftpSession : IRemoteCatalogSession
    {
        public Dictionary<string, List<RemoteEntry>> Directories { get; } = new(StringComparer.Ordinal);

        public List<string> Listed { get; } = [];

        public IReadOnlyList<RemoteEntry> ListDirectory(string path)
        {
            Listed.Add(path);
            return Directories.TryGetValue(path, out var entries)
                ? entries
                : throw new RemoteDirectoryNotFoundException(path);
        }

        public void Dispose()
        {
        }
    }
}
