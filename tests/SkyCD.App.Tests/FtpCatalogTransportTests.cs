using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SkyCD.Presentation.Operations;
using SkyCD.Presentation.Scanning;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Integration coverage for the FluentFTP-based FTP transport (#580). Every test drives the real
/// <see cref="FtpCatalogConnector"/> and <see cref="FtpCatalogScanner"/> against
/// <see cref="ScriptedFtpServer"/>, so the FluentFTP session, listing and exception mapping are
/// exercised end to end over a real loopback socket.
/// </summary>
public sealed class FtpCatalogTransportTests
{
    private static readonly DateTime Stamp = new(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);

    // ------------------------------------------------------------------ helpers

    private static FtpScanOptions Options(int port, string path = "/", ScanTraversalOptions? traversal = null) =>
        new(
            $"ftp://127.0.0.1:{port}{path}",
            "alice",
            "secret-pass",
            anonymous: false,
            mediaName: "Remote",
            targetParentId: null,
            includeExtendedInfo: true,
            traversal: traversal);

    private static FtpScanOptions AnonymousOptions(int port) =>
        new(
            $"ftp://127.0.0.1:{port}/",
            null,
            null,
            anonymous: true,
            mediaName: "Remote",
            targetParentId: null,
            includeExtendedInfo: false);

    private static ScanTraversalOptions FastFail(int maxRetries = 1) =>
        new() { MaxRetries = maxRetries, RetryDelay = TimeSpan.Zero };

    private static ScriptedDirectory Tree()
    {
        var nested = new ScriptedDirectory("nested", Stamp);
        nested.Files.Add(new ScriptedFile("deep.txt", 7, Stamp));

        var docs = new ScriptedDirectory("docs", Stamp);
        docs.Files.Add(new ScriptedFile("readme.txt", 11, Stamp));
        docs.Children.Add(nested);

        var root = new ScriptedDirectory("/", Stamp);
        root.Files.Add(new ScriptedFile("top.txt", 3, Stamp));
        root.Children.Add(docs);
        return root;
    }

    private static ScriptedFtpServer Server()
    {
        var server = new ScriptedFtpServer();
        var tree = Tree();
        server.Directories["/"] = tree;
        server.Directories["/docs"] = tree.Children[0];
        server.Directories["/docs/nested"] = tree.Children[0].Children[0];
        return server;
    }

    // ------------------------------------------------------------------ nested listings

    [Fact]
    public void Scan_NestedDirectories_TraversesAndMapsTheCatalog()
    {
        using var server = Server();

        var result = new FtpCatalogScanner(new FtpCatalogConnector())
            .ScanDetailed(Options(server.Port));

        var names = result.Documents.Select(document => document.Name).ToList();
        Assert.Contains("Remote", names);
        Assert.Contains("top.txt", names);
        Assert.Contains("docs", names);
        Assert.Contains("readme.txt", names);
        Assert.Contains("nested", names);
        Assert.Contains("deep.txt", names);
        Assert.Equal(1 + 3 + 2, result.Documents.Count);
        Assert.Equal(0, result.Statistics.Failed);
        Assert.Equal(0, result.Statistics.Skipped);

        var root = result.Documents[0];
        Assert.Equal("Remote", root.Name);
        Assert.Equal(21, root.Size);
        Assert.Equal(2, root.ChildrenCount);

        var readme = Assert.Single(result.Documents, document => document.Name == "readme.txt");
        Assert.Equal(11, readme.Size);
        Assert.Equal("/docs/readme.txt", readme.Properties["FullName"]);
        Assert.Equal("/docs", readme.Properties["RealPath"]);
        Assert.Equal("rw-r--r--", readme.Properties["Permission"]);
        Assert.Equal(Stamp.ToString("O"), readme.Properties["FileDateTime"]);

        Assert.Contains(server.Commands, command => command.StartsWith("LIST", StringComparison.OrdinalIgnoreCase));
        Assert.False(server.SawPasswordInCommand);
    }

    [Fact]
    public void Scan_ModernMlsdServer_StillUsesTheListDialect()
    {
        // FluentFTP answers a failed MLSD with an empty listing, which would turn a missing or
        // refused directory into a silently empty one, so the transport asks for LIST (#580).
        using var server = Server();

        var result = new FtpCatalogScanner(new FtpCatalogConnector())
            .ScanDetailed(Options(server.Port));

        Assert.Contains(result.Documents, document => document.Name == "readme.txt");
        Assert.Equal(0, result.Statistics.Failed);
        Assert.Contains(server.Commands, command => command.StartsWith("LIST", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Scan_ServerWithoutFeatureNegotiation_StillLists()
    {
        using var server = Server();
        server.SupportsFeat = false;
        server.SupportsMlsd = false;

        var result = new FtpCatalogScanner(new FtpCatalogConnector())
            .ScanDetailed(Options(server.Port));

        Assert.Contains(result.Documents, document => document.Name == "top.txt");
        Assert.Equal(0, result.Statistics.Failed);
    }

    [Fact]
    public void Scan_SubdirectoryAddress_ScansOnlyThatDirectory()
    {
        using var server = Server();

        var result = new FtpCatalogScanner(new FtpCatalogConnector())
            .ScanDetailed(Options(server.Port, "/docs"));

        Assert.Contains(result.Documents, document => document.Name == "readme.txt");
        Assert.Contains(result.Documents, document => document.Name == "deep.txt");
        Assert.DoesNotContain(result.Documents, document => document.Name == "top.txt");
        Assert.Equal("/docs/readme.txt", result.Documents.Single(document => document.Name == "readme.txt").Properties["FullName"]);
    }

    // ------------------------------------------------------------------ authentication

    [Fact]
    public void Scan_RejectedCredentials_FailsFatalWithoutRetry()
    {
        using var server = Server();
        var options = new FtpScanOptions(
            $"ftp://127.0.0.1:{server.Port}/",
            "alice",
            "wrong-password",
            anonymous: false,
            mediaName: null,
            targetParentId: null,
            includeExtendedInfo: false,
            traversal: FastFail(maxRetries: 3));

        var exception = Assert.Throws<FtpAuthenticationException>(() =>
            new FtpCatalogScanner(new FtpCatalogConnector()).ScanDetailed(options));

        Assert.DoesNotContain("wrong-password", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, server.ConnectionCount);
    }

    [Fact]
    public void Scan_AnonymousLogin_UsesTheAnonymousIdentity()
    {
        using var server = new ScriptedFtpServer("anonymous", "anonymous@");
        var tree = Tree();
        server.Directories["/"] = tree;

        var result = new FtpCatalogScanner(new FtpCatalogConnector())
            .ScanDetailed(AnonymousOptions(server.Port));

        Assert.Contains(result.Documents, document => document.Name == "top.txt");
        Assert.Contains(server.Commands, command => command.StartsWith("USER anonymous", StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------ missing directories

    [Fact]
    public void Scan_MissingRootDirectory_IsFatal()
    {
        using var server = Server();
        server.MissingDirectories.Add("/gone");

        var exception = Assert.Throws<FtpDirectoryNotFoundException>(() =>
            new FtpCatalogScanner(new FtpCatalogConnector())
                .ScanDetailed(Options(server.Port, "/gone")));

        Assert.Contains("/gone", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, server.ListingCount);
    }

    [Fact]
    public void Scan_ChildDirectoryVanishes_FailsTheItemAndKeepsTheRest()
    {
        using var server = Server();
        server.MissingDirectories.Add("/docs/nested");

        var progress = new RecordingProgress();
        var result = new FtpCatalogScanner(new FtpCatalogConnector())
            .ScanDetailed(Options(server.Port), progress);

        Assert.Contains(result.Documents, document => document.Name == "readme.txt");
        Assert.DoesNotContain(result.Documents, document => document.Name == "deep.txt");
        Assert.Equal(1, result.Statistics.Failed);
        Assert.Contains(progress.Failed, path => path == "/docs/nested");
    }

    // ------------------------------------------------------------------ interrupted connections

    [Fact]
    public void Scan_InterruptedListing_ReconnectsAndCompletes()
    {
        using var server = Server();
        server.DropListings.Add("/docs");

        var progress = new RecordingProgress();
        var result = new FtpCatalogScanner(new FtpCatalogConnector())
            .ScanDetailed(Options(server.Port, traversal: new ScanTraversalOptions { MaxRetries = 2, RetryDelay = TimeSpan.Zero }), progress);

        Assert.Contains(result.Documents, document => document.Name == "readme.txt");
        Assert.Contains(result.Documents, document => document.Name == "deep.txt");
        Assert.Equal(0, result.Statistics.Failed);
        Assert.Contains(progress.Warnings, warning => warning.Contains("/docs", StringComparison.Ordinal));
        Assert.True(server.ConnectionCount >= 2, "The scan must re-establish the control session after a drop.");
    }

    [Fact]
    public void Scan_InterruptedRootListing_IsFatal()
    {
        using var server = Server();
        server.DropListings.Add("/");

        var exception = Assert.Throws<FtpConnectionException>(() =>
            new FtpCatalogScanner(new FtpCatalogConnector())
                .ScanDetailed(Options(server.Port, traversal: FastFail())));

        Assert.Contains("127.0.0.1", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-pass", exception.Message, StringComparison.Ordinal);
        Assert.True(server.ConnectionCount >= 2, "The retry must reconnect before giving up.");
    }

    [Fact]
    public void Scan_UnreachableServer_FailsWithASanitizedConnectionError()
    {
        // Port 1 on the loopback interface refuses connections.
        var options = new FtpScanOptions(
            "ftp://127.0.0.1:1/",
            "alice",
            "secret-pass",
            anonymous: false,
            mediaName: null,
            targetParentId: null,
            includeExtendedInfo: false,
            traversal: new ScanTraversalOptions { MaxRetries = 0, RetryDelay = TimeSpan.Zero, OperationTimeout = TimeSpan.FromSeconds(20) });

        var exception = Assert.Throws<FtpConnectionException>(() =>
            new FtpCatalogScanner(new FtpCatalogConnector()).ScanDetailed(options));

        Assert.Contains("127.0.0.1:1", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-pass", exception.Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ cancellation

    [Fact]
    public void Scan_CancelledBeforeConnecting_ThrowsOperationCanceledException()
    {
        using var server = Server();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            new FtpCatalogScanner(new FtpCatalogConnector())
                .ScanDetailed(Options(server.Port), cancellationToken: cancellation.Token));

        Assert.Empty(server.Commands);
    }

    [Fact]
    public void Scan_CancelledDuringTraversal_ThrowsOperationCanceledException()
    {
        using var server = Server();
        using var cancellation = new CancellationTokenSource();

        var scanner = new FtpCatalogScanner(new FtpCatalogConnector());
        var options = Options(server.Port, traversal: new ScanTraversalOptions { MaxRetries = 50, RetryDelay = TimeSpan.FromMilliseconds(20) });
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(5));

        Assert.ThrowsAny<OperationCanceledException>(() =>
            scanner.ScanDetailed(options, cancellationToken: cancellation.Token));
    }

    // ------------------------------------------------------------------ session semantics

    [Fact]
    public void Connect_LineBreakInCredentials_IsRejectedBeforeAnyConnection()
    {
        using var server = Server();
        var location = FtpLocation.Parse($"ftp://127.0.0.1:{server.Port}/");

        Assert.Throws<FtpAuthenticationException>(() =>
            new FtpCatalogConnector().Connect(location, "alice\r\nUSER root", "secret-pass", CancellationToken.None));

        Assert.Empty(server.Commands);
    }

    [Fact]
    public void Connect_TransportConfigNeverRetriesAndNormalizesTimesToUtc()
    {
        var config = FtpCatalogConnector.CreateConfig();

        Assert.Equal(0, config.RetryAttempts);
        Assert.Equal(FluentFTP.FtpDate.UTC, config.TimeConversion);
        Assert.Equal(FluentFTP.FtpEncryptionMode.None, config.EncryptionMode);
        Assert.Equal(FluentFTP.FtpDataConnectionType.AutoPassive, config.DataConnectionType);
        Assert.Equal(FluentFTP.FtpParser.Auto, config.ListingParser);
        Assert.Equal(FluentFTP.FtpListOption.ForceList, FtpCatalogConnector.ListingOptions);
        Assert.Equal(20_000, config.ReadTimeout);
        Assert.Equal(15_000, config.ConnectTimeout);
    }

    /// <summary>Collects the progress events the scan reports.</summary>
    private sealed class RecordingProgress : IOperationProgress
    {
        public List<string> Warnings { get; } = [];

        public List<string> Failed { get; } = [];

        public List<string> Skipped { get; } = [];

        public void Report(OperationProgressReport value)
        {
        }

        public void ReportStage(string stageName, double? stagePercentage = null, double? overallPercentage = null, long? stageCurrent = null, long? stageTotal = null, long? overallCurrent = null, long? overallTotal = null)
        {
        }

        public void ReportItem(string currentItem, double? stagePercentage = null, double? overallPercentage = null, long? stageCurrent = null, long? stageTotal = null, long? overallCurrent = null, long? overallTotal = null)
        {
        }

        public void ReportOverall(double? overallPercentage, long? overallCurrent = null, long? overallTotal = null, string? operationTitle = null)
        {
        }

        public void ReportLog(OperationLogLevel level, string message, string? stage = null, string? itemPath = null, string? details = null)
        {
        }

        public void ReportInfo(string message, string? itemPath = null, string? stage = null)
        {
        }

        public void ReportWarning(string message, string? itemPath = null, string? stage = null) => Warnings.Add(message);

        public void ReportError(string message, Exception? exception = null, string? itemPath = null, string? stage = null)
        {
        }

        public void ReportSkipped(string itemPath, string reason, string? stage = null) => Skipped.Add(itemPath);

        public void ReportFailed(string itemPath, string reason, string? stage = null, Exception? exception = null) => Failed.Add(itemPath);
    }
}
