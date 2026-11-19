using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using FluentFTP;
using SkyCD.Presentation.Operations;
using SkyCD.Presentation.Scanning;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Integration coverage for the FluentFTP-based FTPS transport (#623). Every test drives the real
/// <c>FtpCatalogConnector</c> against a TLS-enabled <see cref="LocalFtpServer"/>, so the implicit
/// and explicit handshakes, the protected data channel and the exception mapping are exercised
/// end to end over real loopback sockets.
/// </summary>
public sealed class FtpsCatalogTransportTests
{
    private const string Username = "catalog-user";
    private const string Password = "s3cret-pass";

    // ------------------------------------------------------------------ helpers

    private static RemoteLocation Location(int port, string path = "/nested") =>
        RemoteLocation.Parse($"ftps://127.0.0.1:{port}{path}");

    private static RemoteLocation ExplicitTlsLocation(int port, string path = "/nested") =>
        RemoteLocation.Parse($"ftp://127.0.0.1:{port}{path}?tls=explicit");

    private static RemoteCredentials Credentials(string? password = Password) =>
        new(Username, password, anonymous: false);

    /// <summary>
    /// Trust policy for the fixture's self-signed certificate: the exact certificate presented by
    /// <paramref name="server"/> is accepted, anything else must fail validation.
    /// </summary>
    private static FtpSslValidation TrustCertificate(X509Certificate2 trusted) =>
        (control, args) =>
        {
            var presented = args.Certificate?.GetRawCertData();
            if (presented is not null && presented.AsSpan().SequenceEqual(trusted.RawData))
            {
                args.Accept = true;
            }
        };

    private static FtpCatalogConnector TrustingConnector(LocalFtpServer server) =>
        new("ftps", 990, RemoteTlsMode.Implicit, TrustCertificate(server.Certificate!));

    private static void AssertTarget(RemoteTlsException exception, int port) =>
        Assert.Equal($"127.0.0.1:{port}", exception.Target);

    private sealed class TempTree : IDisposable
    {
        public TempTree()
        {
            Root = Path.Combine(Path.GetTempPath(), $"skycd-ftps-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(Root, "nested", "child"));
            Write("nested/top.txt", 7);
            Write("nested/child/file.txt", 42);
        }

        public string Root { get; }

        public void Write(string relativePath, int size)
        {
            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[size]);
            File.SetLastWriteTimeUtc(path, new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc));
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Collects the progress events so retry behaviour can be asserted.</summary>
    private sealed class RecordingProgress : IOperationProgress
    {
        public List<string> Messages { get; } = [];

        public void Report(OperationProgressReport value)
        {
        }

        public void ReportStage(string stageName, double? stagePercentage = null, double? overallPercentage = null, long? stageCurrent = null, long? stageTotal = null, long? overallCurrent = null, long? overallTotal = null) =>
            Messages.Add(stageName);

        public void ReportItem(string currentItem, double? stagePercentage = null, double? overallPercentage = null, long? stageCurrent = null, long? stageTotal = null, long? overallCurrent = null, long? overallTotal = null) =>
            Messages.Add(currentItem);

        public void ReportOverall(double? overallPercentage, long? overallCurrent = null, long? overallTotal = null, string? operationTitle = null)
        {
            if (!string.IsNullOrWhiteSpace(operationTitle))
            {
                Messages.Add(operationTitle);
            }
        }

        public void ReportLog(OperationLogLevel level, string message, string? stage = null, string? itemPath = null, string? details = null) =>
            Messages.Add(string.Join(" ", new[] { message, stage, itemPath, details }.Where(static value => !string.IsNullOrWhiteSpace(value))));

        public void ReportInfo(string message, string? itemPath = null, string? stage = null) =>
            ReportLog(OperationLogLevel.Information, message, stage, itemPath);

        public void ReportWarning(string message, string? itemPath = null, string? stage = null) =>
            ReportLog(OperationLogLevel.Warning, message, stage, itemPath);

        public void ReportError(string message, Exception? exception = null, string? itemPath = null, string? stage = null) =>
            ReportLog(OperationLogLevel.Error, message, stage, itemPath, exception?.Message);

        public void ReportSkipped(string itemPath, string reason, string? stage = null) =>
            Messages.Add(itemPath + " " + reason);

        public void ReportFailed(string itemPath, string reason, string? stage = null, Exception? exception = null) =>
            Messages.Add(itemPath + " " + reason + " " + exception?.Message);
    }

    // ------------------------------------------------------------------ implicit and explicit TLS

    [Fact]
    public void Connect_ImplicitTls_ListsNestedEntriesOverProtectedChannels()
    {
        using var tree = new TempTree();
        using var server = new LocalFtpServer(tree.Root, Username, Password, tlsMode: RemoteTlsMode.Implicit);
        var connector = TrustingConnector(server);

        using var session = connector.Connect(Location(server.Port), Credentials(), CancellationToken.None);
        var nested = session.ListDirectory("/nested");

        var top = Assert.Single(nested, entry => entry.Name == "top.txt");
        Assert.False(top.IsDirectory);
        Assert.Equal(7, top.Size);
        Assert.Equal("/nested/top.txt", top.FullName);

        var child = Assert.Single(nested, entry => entry.Name == "child");
        Assert.True(child.IsDirectory);

        var listing = session.ListDirectory("/nested/child");
        var file = Assert.Single(listing, entry => entry.Name == "file.txt");
        Assert.Equal(42, file.Size);
        Assert.Equal(
            new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc),
            file.ModifiedUtc);
    }

    [Fact]
    public void Connect_ExplicitTls_ListsNestedEntriesOverProtectedChannels()
    {
        using var tree = new TempTree();
        using var server = new LocalFtpServer(tree.Root, Username, Password, tlsMode: RemoteTlsMode.Explicit);
        var connector = TrustingConnector(server);

        using var session = connector.Connect(ExplicitTlsLocation(server.Port), Credentials(), CancellationToken.None);
        var nested = session.ListDirectory("/nested");

        Assert.Contains(nested, entry => entry.Name == "top.txt");
        Assert.Contains(nested, entry => entry.Name == "child");
        Assert.Equal(42, Assert.Single(session.ListDirectory("/nested/child"), entry => entry.Name == "file.txt").Size);
    }

    // ------------------------------------------------------------------ certificate validation

    [Fact]
    public void Connect_UntrustedCertificate_FailsWithTypedLocalizedCertificateError()
    {
        using var tree = new TempTree();
        using var server = new LocalFtpServer(tree.Root, Username, Password, tlsMode: RemoteTlsMode.Implicit);

        // The production connector validates against the system trust store: the fixture's
        // self-signed certificate must be rejected, never accepted silently.
        var connector = new FtpCatalogConnector();
        var exception = Assert.Throws<RemoteCertificateValidationException>(() =>
            connector.Connect(Location(server.Port), Credentials(), CancellationToken.None));

        AssertTarget(exception, server.Port);
        Assert.Equal(
            LocalizationService.Current.Format(TextKeys.RemoteCertificateValidationFailed, $"127.0.0.1:{server.Port}"),
            exception.Message);
        Assert.DoesNotContain(Password, exception.ToString(), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ rejected TLS upgrades

    [Fact]
    public void Connect_AuthTlsRejected_FailsWithTypedUpgradeError()
    {
        using var tree = new TempTree();
        using var server = new LocalFtpServer(
            tree.Root, Username, Password, tlsMode: RemoteTlsMode.Explicit, rejectAuthTls: true);
        var connector = TrustingConnector(server);

        var exception = Assert.Throws<RemoteTlsUpgradeException>(() =>
            connector.Connect(ExplicitTlsLocation(server.Port), Credentials(), CancellationToken.None));

        AssertTarget(exception, server.Port);
        Assert.Equal(
            LocalizationService.Current.Format(TextKeys.RemoteTlsUpgradeRejected, $"127.0.0.1:{server.Port}"),
            exception.Message);
    }

    [Fact]
    public void Connect_ProtPRejected_FailsWithTypedDataProtectionError()
    {
        using var tree = new TempTree();
        using var server = new LocalFtpServer(
            tree.Root, Username, Password, tlsMode: RemoteTlsMode.Implicit, rejectProtP: true);
        var connector = TrustingConnector(server);

        var exception = Assert.Throws<RemoteTlsDataProtectionException>(() =>
            connector.Connect(Location(server.Port), Credentials(), CancellationToken.None));

        AssertTarget(exception, server.Port);
        Assert.Equal(
            LocalizationService.Current.Format(TextKeys.RemoteTlsDataProtectionRejected, $"127.0.0.1:{server.Port}"),
            exception.Message);
        Assert.DoesNotContain(Password, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Connect_TlsExpected_ServerSpeaksPlaintext_FailsWithTypedHandshakeError()
    {
        using var tree = new TempTree();
        using var server = new LocalFtpServer(tree.Root, Username, Password);
        var connector = TrustingConnector(server);

        var exception = Assert.Throws<RemoteTlsHandshakeException>(() =>
            connector.Connect(Location(server.Port), Credentials(), CancellationToken.None));

        AssertTarget(exception, server.Port);
        Assert.Equal(
            LocalizationService.Current.Format(TextKeys.RemoteTlsHandshakeFailed, $"127.0.0.1:{server.Port}"),
            exception.Message);
    }

    // ------------------------------------------------------------------ authentication, cancellation, drops

    [Fact]
    public void Connect_RejectedCredentials_OverTls_FailsWithTypedAuthenticationError()
    {
        using var tree = new TempTree();
        using var server = new LocalFtpServer(tree.Root, Username, Password, tlsMode: RemoteTlsMode.Implicit);
        var connector = TrustingConnector(server);
        const string wrongPassword = "wrong-secret-9234";

        var exception = Assert.Throws<RemoteAuthenticationException>(() =>
            connector.Connect(Location(server.Port), Credentials(wrongPassword), CancellationToken.None));

        Assert.DoesNotContain(wrongPassword, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Password, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Connect_CancelledToken_ThrowsOperationCanceled()
    {
        using var tree = new TempTree();
        using var server = new LocalFtpServer(tree.Root, Username, Password, tlsMode: RemoteTlsMode.Implicit);
        var connector = TrustingConnector(server);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            connector.Connect(Location(server.Port), Credentials(), cancelled.Token));
    }

    [Fact]
    public void ListDirectory_AfterControlConnectionDrops_FailsWithTypedConnectionError()
    {
        using var tree = new TempTree();

        // The fixture drops the control connection right after the second listing, so the first
        // directory is catalogued and the follow-up listing meets an interrupted connection.
        using var server = new LocalFtpServer(
            tree.Root, Username, Password, dropConnectionAfterListings: 2, tlsMode: RemoteTlsMode.Implicit);
        var connector = TrustingConnector(server);
        using var session = connector.Connect(Location(server.Port), Credentials(), CancellationToken.None);
        Assert.Contains(session.ListDirectory("/nested"), entry => entry.Name == "top.txt");

        var exception = Assert.Throws<RemoteConnectionException>(() => session.ListDirectory("/nested/child"));
        Assert.DoesNotContain(Password, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_ControlConnectionDroppedOverTls_ReconnectsAndCompletesTheTraversal()
    {
        using var tree = new TempTree();

        // The fixture drops the control connection right after the first listing, so the scan
        // has to detect the disconnect, reconnect over TLS and finish the traversal (#433).
        using var server = new LocalFtpServer(
            tree.Root, Username, Password, dropConnectionAfterListings: 1, tlsMode: RemoteTlsMode.Implicit);
        var progress = new RecordingProgress();

        var scanned = new RemoteCatalogScanner(TrustingConnector(server)).Scan(new RemoteScanOptions(
            $"ftps://127.0.0.1:{server.Port}/",
            Username,
            Password,
            anonymous: false,
            mediaName: "Reconnected",
            targetParentId: null,
            includeExtendedInfo: true), progress);

        Assert.Equal(5, scanned.Count);
        Assert.Contains(scanned, document => document.Name == "top.txt");
        Assert.Contains(scanned, document => document.Name == "child");
        Assert.Contains(scanned, document => document.Name == "file.txt");
        Assert.Contains(progress.Messages, message => message.Contains("Transient failure", StringComparison.Ordinal));
    }
}
