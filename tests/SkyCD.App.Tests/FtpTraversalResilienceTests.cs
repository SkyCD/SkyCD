using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SkyCD.Presentation.Operations;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Traversal resilience of <see cref="FtpCatalogScanner"/> (issue #433): directory cycles are
/// skipped, transient disconnects are retried with a reconnect, per-item failures never abort
/// the scan, and the discovered / skipped / failed counters stay accurate.
/// </summary>
public sealed class FtpTraversalResilienceTests
{
    // ------------------------------------------------------------------ helpers

    private static FtpScanOptions Options(ScanTraversalOptions? traversal = null) =>
        new(
            "ftp://localhost/",
            "user",
            "pass",
            anonymous: false,
            mediaName: "TestRoot",
            targetParentId: null,
            includeExtendedInfo: false,
            traversal: traversal);

    private static ScanTraversalOptions FastFail(int maxRetries = 1) =>
        new() { MaxRetries = maxRetries, RetryDelay = TimeSpan.Zero };

    private static FtpRemoteEntry Dir(string name, string fullName, string parent) =>
        new(name, fullName, parent, IsDirectory: true, Size: 0, Permission: null, ModifiedUtc: null);

    private static FtpRemoteEntry File(string name, string fullName, string parent, long size = 10) =>
        new(name, fullName, parent, IsDirectory: false, Size: size, Permission: null, ModifiedUtc: null);

    // ------------------------------------------------------------------ fakes

    /// <summary>Serves a fixed tree of remote entries; each path may throw on demand.</summary>
    private sealed class FakeFtpSession : IFtpRemoteSession
    {
        private readonly Dictionary<string, Func<IReadOnlyList<FtpRemoteEntry>>> listings;

        public FakeFtpSession(Dictionary<string, Func<IReadOnlyList<FtpRemoteEntry>>> listings) =>
            this.listings = listings;

        public Dictionary<string, int> ListDirectoryCalls { get; } = new(StringComparer.Ordinal);

        public int TotalListDirectoryCalls => ListDirectoryCalls.Values.Sum();

        public IReadOnlyList<FtpRemoteEntry> ListDirectory(string path)
        {
            ListDirectoryCalls[path] = ListDirectoryCalls.GetValueOrDefault(path) + 1;

            return listings.TryGetValue(path, out var factory) ? factory() : [];
        }

        public void Dispose()
        {
            // The fixture session is reused across reconnects.
        }
    }

    private sealed class FakeFtpConnector : IFtpCatalogConnector
    {
        private readonly Func<IFtpRemoteSession> factory;

        public FakeFtpConnector(Func<IFtpRemoteSession> factory) => this.factory = factory;

        public IFtpRemoteSession Connect(FtpLocation location, string username, string password, CancellationToken cancellationToken) =>
            factory();
    }

    private sealed class CapturingProgress : IOperationProgress
    {
        public List<string> Skipped { get; } = [];

        public List<string> Failed { get; } = [];

        public List<string> Warnings { get; } = [];

        public List<string> Reports { get; } = [];

        public void Report(OperationProgressReport value) => Reports.Add(value.OperationTitle ?? string.Empty);

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

    // ------------------------------------------------------------------ cycle detection

    [Fact]
    public void Scan_DirectoryCycle_IsSkippedAndTheScanStillCompletes()
    {
        var session = new FakeFtpSession(new Dictionary<string, Func<IReadOnlyList<FtpRemoteEntry>>>
        {
            ["/"] = () =>
            [
                Dir("loop", "/loop", "/"),
                Dir("deep", "/deep", "/"),
                File("root.txt", "/root.txt", "/"),
            ],
            ["/loop"] = () => [Dir("/", "/", "/loop")],
            ["/deep"] = () => [File("deep.txt", "/deep/deep.txt", "/deep")],
        });

        var progress = new CapturingProgress();
        var result = new FtpCatalogScanner(new FakeFtpConnector(() => session)).ScanDetailed(Options(), progress);

        Assert.Contains(result.Documents, document => document.Name == "root.txt");
        Assert.Contains(result.Documents, document => document.Name == "deep.txt");
        Assert.Contains(progress.Skipped, path => path == "/");
        Assert.Equal(1, result.Statistics.Skipped);
        Assert.Equal(0, result.Statistics.Failed);
    }

    [Fact]
    public void Scan_SelfReferencingDirectory_IsSkippedWithoutLooping()
    {
        var session = new FakeFtpSession(new Dictionary<string, Func<IReadOnlyList<FtpRemoteEntry>>>
        {
            ["/"] = () => [Dir("self", "/self", "/"), File("root.txt", "/root.txt", "/")],
            ["/self"] = () => [Dir("self", "/self", "/self")],
        });

        var progress = new CapturingProgress();
        var result = new FtpCatalogScanner(new FakeFtpConnector(() => session)).ScanDetailed(Options(), progress);

        Assert.Contains(result.Documents, document => document.Name == "root.txt");
        Assert.Contains(progress.Skipped, path => path == "/self");
        Assert.Equal(1, result.Statistics.Skipped);
    }

    // ------------------------------------------------------------------ disconnect and reconnect

    [Fact]
    public void Scan_TransientDisconnectThenReconnect_RetriesAndSucceeds()
    {
        var attempts = 0;
        var session = new FakeFtpSession(new Dictionary<string, Func<IReadOnlyList<FtpRemoteEntry>>>
        {
            ["/"] = () => [Dir("data", "/data", "/"), File("readme.txt", "/readme.txt", "/")],
            ["/data"] = () =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new FtpConnectionException("Connection reset by peer");
                }

                return [File("report.csv", "/data/report.csv", "/data", size: 20)];
            },
        });

        var result = new FtpCatalogScanner(new FakeFtpConnector(() => session))
            .ScanDetailed(Options(new ScanTraversalOptions { MaxRetries = 2, RetryDelay = TimeSpan.Zero }));

        Assert.Equal(2, attempts);
        Assert.Contains(result.Documents, document => document.Name == "readme.txt");
        Assert.Contains(result.Documents, document => document.Name == "report.csv");
        Assert.Equal(0, result.Statistics.Failed);
        Assert.Equal(0, result.Statistics.Skipped);
    }

    [Fact]
    public void Scan_ChildRetryExhausted_FailsTheItemAndContinues()
    {
        var session = new FakeFtpSession(new Dictionary<string, Func<IReadOnlyList<FtpRemoteEntry>>>
        {
            ["/"] = () => [Dir("flaky", "/flaky", "/"), File("ok.txt", "/ok.txt", "/")],
            ["/flaky"] = () => throw new FtpConnectionException("Server closed the connection"),
        });

        var progress = new CapturingProgress();
        var result = new FtpCatalogScanner(new FakeFtpConnector(() => session))
            .ScanDetailed(Options(FastFail()), progress);

        Assert.Contains(result.Documents, document => document.Name == "ok.txt");
        Assert.DoesNotContain(result.Documents, document => document.Name == "flaky");
        Assert.Contains(progress.Failed, path => path == "/flaky");
        Assert.Equal(1, result.Statistics.Failed);
        Assert.Equal(0, result.Statistics.Skipped);
    }

    [Fact]
    public void Scan_ChildDirectoryVanished_FailsTheItemAndContinues()
    {
        var session = new FakeFtpSession(new Dictionary<string, Func<IReadOnlyList<FtpRemoteEntry>>>
        {
            ["/"] = () => [Dir("gone", "/gone", "/"), File("ok.txt", "/ok.txt", "/")],
            ["/gone"] = () => throw new FtpDirectoryNotFoundException("Directory not found"),
        });

        var progress = new CapturingProgress();
        var result = new FtpCatalogScanner(new FakeFtpConnector(() => session)).ScanDetailed(Options(), progress);

        Assert.Contains(result.Documents, document => document.Name == "ok.txt");
        Assert.Contains(progress.Failed, path => path == "/gone");
        Assert.Equal(1, result.Statistics.Failed);
    }

    [Fact]
    public void Scan_RootListingRetryExhausted_IsFatal()
    {
        var session = new FakeFtpSession(new Dictionary<string, Func<IReadOnlyList<FtpRemoteEntry>>>
        {
            ["/"] = () => throw new FtpConnectionException("Server unreachable"),
        });

        var exception = Assert.Throws<FtpConnectionException>(() =>
            new FtpCatalogScanner(new FakeFtpConnector(() => session))
                .ScanDetailed(Options(FastFail())));

        Assert.Contains("localhost", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pass", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_ConnectRetryExhausted_IsFatalAndHidesCredentials()
    {
        var connector = new FakeFtpConnector(() => throw new FtpConnectionException("Connection refused"));

        var exception = Assert.Throws<FtpConnectionException>(() =>
            new FtpCatalogScanner(connector).ScanDetailed(Options(FastFail())));

        Assert.Contains("localhost", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pass", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Scan_AuthenticationFailure_IsFatalAndNotRetried()
    {
        var attempts = 0;
        var connector = new FakeFtpConnector(() =>
        {
            attempts++;
            throw new FtpAuthenticationException("Authentication failed");
        });

        Assert.Throws<FtpAuthenticationException>(() =>
            new FtpCatalogScanner(connector).ScanDetailed(Options(FastFail())));

        Assert.Equal(1, attempts);
    }

    // ------------------------------------------------------------------ cancellation

    [Fact]
    public void Scan_CancelledDuringRetryWait_ThrowsOperationCanceledException()
    {
        var session = new FakeFtpSession(new Dictionary<string, Func<IReadOnlyList<FtpRemoteEntry>>>
        {
            ["/"] = () => throw new FtpConnectionException("Temporary disconnect"),
        });

        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(30));

        Assert.ThrowsAny<OperationCanceledException>(() =>
            new FtpCatalogScanner(new FakeFtpConnector(() => session)).ScanDetailed(
                Options(new ScanTraversalOptions
                {
                    MaxRetries = 100,
                    RetryDelay = TimeSpan.FromMilliseconds(50)
                }),
                cancellationToken: cancellation.Token));
    }

    [Fact]
    public void Scan_CancelledBeforeTraversal_ThrowsOperationCanceledException()
    {
        var session = new FakeFtpSession(new Dictionary<string, Func<IReadOnlyList<FtpRemoteEntry>>>
        {
            ["/"] = () => [File("never.txt", "/never.txt", "/")],
        });

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            new FtpCatalogScanner(new FakeFtpConnector(() => session))
                .ScanDetailed(Options(), cancellationToken: cancellation.Token));
    }

    // ------------------------------------------------------------------ policies and counters

    [Fact]
    public void Scan_ExclusionPatterns_SkipMatchingEntriesAndKeepCounts()
    {
        var session = new FakeFtpSession(new Dictionary<string, Func<IReadOnlyList<FtpRemoteEntry>>>
        {
            ["/"] = () =>
            [
                File("keep.txt", "/keep.txt", "/"),
                File("drop.tmp", "/drop.tmp", "/"),
                Dir("drop-dir", "/drop-dir", "/"),
            ],
            ["/drop-dir"] = () => [File("nested.txt", "/drop-dir/nested.txt", "/drop-dir")],
        });

        var progress = new CapturingProgress();
        var result = new FtpCatalogScanner(new FakeFtpConnector(() => session)).ScanDetailed(
            Options(new ScanTraversalOptions { ExclusionPatterns = ["*.tmp", "drop-dir"] }),
            progress);

        Assert.Contains(result.Documents, document => document.Name == "keep.txt");
        Assert.DoesNotContain(result.Documents, document => document.Name == "drop.tmp");
        Assert.DoesNotContain(result.Documents, document => document.Name == "drop-dir");
        Assert.Equal(2, result.Statistics.Skipped);
        Assert.Equal(0, result.Statistics.Failed);
    }

    [Fact]
    public void Scan_LargeDirectoryTree_CompletesWithAccurateCounts()
    {
        const int folderCount = 25;
        const int filesPerFolder = 5;

        var listings = new Dictionary<string, Func<IReadOnlyList<FtpRemoteEntry>>>
        {
            ["/"] = () =>
            [
                .. Enumerable.Range(0, folderCount)
                    .Select(index => Dir($"dir-{index:D3}", $"/dir-{index:D3}", "/"))
            ]
        };

        for (var index = 0; index < folderCount; index++)
        {
            var folder = $"/dir-{index:D3}";
            listings[folder] = () =>
            [
                .. Enumerable.Range(0, filesPerFolder)
                    .Select(file => File($"file-{file:D2}.dat", $"{folder}/file-{file:D2}.dat", folder, file + 1))
            ];
        }

        var session = new FakeFtpSession(listings);
        var result = new FtpCatalogScanner(new FakeFtpConnector(() => session)).ScanDetailed(Options());

        var expected = 1 + folderCount + (folderCount * filesPerFolder);
        Assert.Equal(expected, result.Documents.Count);
        Assert.Equal(expected, result.Statistics.Discovered);
        Assert.Equal(0, result.Statistics.Failed);
        Assert.Equal(0, result.Statistics.Skipped);
    }

    [Fact]
    public void Scan_FailedItem_IsReportedWithoutCredentials()
    {
        var session = new FakeFtpSession(new Dictionary<string, Func<IReadOnlyList<FtpRemoteEntry>>>
        {
            ["/"] = () => [Dir("private", "/private", "/")],
            ["/private"] = () => throw new FtpConnectionException("ftp://user:p@ssw0rd@host/private failed"),
        });

        var progress = new AddingProgressDialogViewModel();
        new FtpCatalogScanner(new FakeFtpConnector(() => session)).ScanDetailed(
            Options(new ScanTraversalOptions { MaxRetries = 0, RetryDelay = TimeSpan.Zero }),
            progress);

        var logText = progress.GetFullLogText();
        Assert.DoesNotContain("p@ssw0rd", logText);
        Assert.Contains("user:***@", logText);
        Assert.True(progress.FailedCount >= 1);
    }

    [Fact]
    public void Scan_Completed_ReportsDiscoveredSkippedAndFailed()
    {
        var session = new FakeFtpSession(new Dictionary<string, Func<IReadOnlyList<FtpRemoteEntry>>>
        {
            ["/"] = () => [File("a.txt", "/a.txt", "/")],
        });

        var progress = new CapturingProgress();
        var result = new FtpCatalogScanner(new FakeFtpConnector(() => session)).ScanDetailed(Options(), progress);

        Assert.Equal(2, result.Statistics.Discovered);
        Assert.Equal(0, result.Statistics.Skipped);
        Assert.Equal(0, result.Statistics.Failed);
    }
}
