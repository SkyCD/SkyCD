using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.Operations;
using SkyCD.Presentation.Scanning;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class ScanConcurrencyTests : IDisposable
{
    private readonly TestWorkspace workspace = new();

    private readonly string root;

    public ScanConcurrencyTests()
    {
        // Scans run against an in-memory file system, so nothing is created here; the workspace only
        // supplies a unique absolute base path that exists on disk (and is cleaned up on disposal).
        root = workspace.GetPath(Path.Combine("skycd-concurrency", Guid.NewGuid().ToString("N")));
    }

    public void Dispose()
    {
        workspace.Dispose();
    }

    [Theory]
    [InlineData(DriveType.Removable, "FAT12", 1_440_000L, null, ScanSourceKind.SlowRemovable)]
    [InlineData(DriveType.Removable, "FAT16", 100_000_000L, null, ScanSourceKind.SlowRemovable)]
    [InlineData(DriveType.Removable, "FAT32", 64L * 1024 * 1024 * 1024, null, ScanSourceKind.SolidState)]
    [InlineData(DriveType.Removable, "exFAT", 0L, null, ScanSourceKind.Unknown)]
    [InlineData(DriveType.CDRom, "UDF", 700_000_000L, null, ScanSourceKind.Optical)]
    [InlineData(DriveType.Fixed, "NTFS", 1_000_000_000_000L, true, ScanSourceKind.HardDisk)]
    [InlineData(DriveType.Fixed, "NTFS", 1_000_000_000_000L, false, ScanSourceKind.SolidState)]
    [InlineData(DriveType.Fixed, "NTFS", 1_000_000_000_000L, null, ScanSourceKind.HardDisk)]
    [InlineData(DriveType.Network, null, 0L, null, ScanSourceKind.Network)]
    public void Classify_MapsDriveFactsToSourceProfile(
        DriveType driveType,
        string? format,
        long totalSize,
        bool? incursSeekPenalty,
        ScanSourceKind expected)
    {
        Assert.Equal(expected, ScanSourceClassifier.Classify(driveType, format, totalSize, incursSeekPenalty));
    }

    [Fact]
    public void Resolver_PicksSourceAppropriateWorkerCounts()
    {
        var floppy = ScanConcurrencyResolver.ResolvePlan(
            new ScanSourceProfile(ScanSourceKind.SlowRemovable), processorCount: 16, cpuSharePercent: 80);
        Assert.Equal(1, floppy.InitialWorkers);
        Assert.Equal(1, floppy.CeilingWorkers);

        var optical = ScanConcurrencyResolver.ResolvePlan(
            new ScanSourceProfile(ScanSourceKind.Optical), processorCount: 16, cpuSharePercent: 80);
        Assert.Equal(1, optical.InitialWorkers);
        Assert.Equal(2, optical.CeilingWorkers);

        var hardDisk = ScanConcurrencyResolver.ResolvePlan(
            new ScanSourceProfile(ScanSourceKind.HardDisk), processorCount: 16, cpuSharePercent: 80);
        Assert.InRange(hardDisk.InitialWorkers, 2, 4);
        Assert.Equal(4, hardDisk.CeilingWorkers);

        var solidState = ScanConcurrencyResolver.ResolvePlan(
            new ScanSourceProfile(ScanSourceKind.SolidState), processorCount: 16, cpuSharePercent: 80);
        Assert.Equal(12, solidState.CeilingWorkers); // floor(16 * 0.8)
        Assert.Equal(2, solidState.InitialWorkers);

        var network = ScanConcurrencyResolver.ResolvePlan(
            new ScanSourceProfile(ScanSourceKind.Network), processorCount: 32, cpuSharePercent: 80);
        Assert.Equal(ScanConcurrencyResolver.NetworkInitialWorkers, network.InitialWorkers);
        Assert.Equal(ScanConcurrencyResolver.NetworkMaxWorkers, network.CeilingWorkers);

        var unknown = ScanConcurrencyResolver.ResolvePlan(
            ScanSourceProfile.Unknown, processorCount: 16, cpuSharePercent: 80);
        Assert.Equal(1, unknown.InitialWorkers);
        Assert.Equal(2, unknown.CeilingWorkers);

        var shared = ScanConcurrencyResolver.ResolvePlan(
            new ScanSourceProfile(ScanSourceKind.SolidState), processorCount: 8, cpuSharePercent: 50);
        Assert.Equal(4, shared.CeilingWorkers);
    }

    [Fact]
    public void Resolver_ManualModeUsesExplicitWorkerCount()
    {
        var options = new ScanTraversalOptions
        {
            ConcurrencyMode = ScanConcurrencyMode.Manual,
            MaxConcurrency = 6,
            SourceProfile = new ScanSourceProfile(ScanSourceKind.SlowRemovable)
        };

        var plan = ScanConcurrencyResolver.ResolvePlan(options, processorCount: 4);
        Assert.Equal(6, plan.InitialWorkers);
        Assert.Equal(6, plan.CeilingWorkers);
    }

    [Fact]
    public void Resolver_MetadataWorkersRegressToOneOnSlowMediaAndUnderLoad()
    {
        var slow = new ScanTraversalOptions { SourceProfile = new ScanSourceProfile(ScanSourceKind.SlowRemovable) };
        Assert.Equal(1, ScanConcurrencyResolver.ResolveMetadataWorkers(slow, 16, systemLoadFraction: 0));

        var solid = new ScanTraversalOptions { SourceProfile = new ScanSourceProfile(ScanSourceKind.SolidState) };
        Assert.Equal(1, ScanConcurrencyResolver.ResolveMetadataWorkers(solid, 16, systemLoadFraction: 0.95));
        // Idle machine: metadata work may use the same source budget as traversal (80% of 16 cores).
        Assert.Equal(12, ScanConcurrencyResolver.ResolveMetadataWorkers(solid, 16, systemLoadFraction: 0));
    }

    [Fact]
    public void Controller_ShrinksUnderLoad_AndRecoversGraduallyWithHysteresis()
    {
        var now = DateTimeOffset.UtcNow;
        var options = new ScanTraversalOptions
        {
            LoadShrinkThresholdPercent = 70,
            LoadSuspendThresholdPercent = 85
        };
        var controller = new ScanConcurrencyController(
            new ScanConcurrencyPlan(4, 4),
            options,
            () => now,
            smoothingFactor: 1,
            coolDown: TimeSpan.FromSeconds(2));

        Assert.Equal(4, controller.Current);

        now += TimeSpan.FromSeconds(3);
        Assert.True(controller.SampleLoad(0.75));
        Assert.Equal(2, controller.Current);

        now += TimeSpan.FromSeconds(3);
        Assert.True(controller.SampleLoad(0.9));
        Assert.Equal(1, controller.Current); // emergency drop ignores cool-down

        now += TimeSpan.FromSeconds(3);
        Assert.True(controller.SampleLoad(0.5));
        Assert.Equal(2, controller.Current); // gradual restore, one step

        now += TimeSpan.FromSeconds(3);
        Assert.True(controller.SampleLoad(0.5));
        Assert.Equal(3, controller.Current);

        now += TimeSpan.FromSeconds(3);
        Assert.True(controller.SampleLoad(0.5));
        Assert.Equal(4, controller.Current);

        now += TimeSpan.FromSeconds(3);
        Assert.False(controller.SampleLoad(0.5)); // already at the ceiling
    }

    [Fact]
    public void Controller_RecoversOnlyBelowHysteresisBand()
    {
        var now = DateTimeOffset.UtcNow;
        var options = new ScanTraversalOptions
        {
            LoadShrinkThresholdPercent = 70,
            LoadSuspendThresholdPercent = 85
        };
        var controller = new ScanConcurrencyController(
            new ScanConcurrencyPlan(4, 4),
            options,
            () => now,
            smoothingFactor: 1,
            coolDown: TimeSpan.FromSeconds(1));

        controller.SampleLoad(0.9);
        Assert.Equal(1, controller.Current);

        now += TimeSpan.FromSeconds(2);
        Assert.False(controller.SampleLoad(0.65)); // inside 60..70 hysteresis band
        Assert.Equal(1, controller.Current);
    }

    [Fact]
    public void Controller_GrowsAdditivelyOnHealthyCompletionsAndBacksOffOnFailure()
    {
        var now = DateTimeOffset.UtcNow;
        var controller = new ScanConcurrencyController(
            new ScanConcurrencyPlan(2, 4),
            new ScanTraversalOptions(),
            () => now,
            coolDown: TimeSpan.FromSeconds(1));

        controller.RecordCompleted(ScanConcurrencyController.DefaultCompletionsPerStep);
        Assert.Equal(3, controller.Current);

        now += TimeSpan.FromSeconds(2);
        controller.RecordBackoff();
        Assert.Equal(1, controller.Current); // 3 / 2 = 1

        controller.RecordBackoff(); // cool-down suppresses a second immediate drop
        Assert.Equal(1, controller.Current);
    }

    [Fact]
    public void FolderScan_ParallelTraversalMatchesSequentialDocumentSet()
    {
        var fs = BuildTree();
        var scanner = new FolderCatalogScanner(fs);

        var sequential = scanner.ScanDetailed(new FolderScanOptions(root,
            Traversal: new ScanTraversalOptions
            {
                ConcurrencyMode = ScanConcurrencyMode.Manual,
                MaxConcurrency = 1,
                MaxRetries = 0,
                RetryDelay = TimeSpan.Zero
            }));
        var parallel = scanner.ScanDetailed(new FolderScanOptions(root,
            Traversal: new ScanTraversalOptions
            {
                ConcurrencyMode = ScanConcurrencyMode.Manual,
                MaxConcurrency = 4,
                MaxRetries = 0,
                RetryDelay = TimeSpan.Zero
            }));

        Assert.Equal(DescribeDocuments(sequential.Documents), DescribeDocuments(parallel.Documents));
        Assert.Equal(sequential.Statistics.Discovered, parallel.Statistics.Discovered);
        Assert.Equal(sequential.Statistics.Skipped, parallel.Statistics.Skipped);
        Assert.Equal(sequential.Statistics.Failed, parallel.Statistics.Failed);

        var duplicateNames = parallel.Documents
            .GroupBy(document => document.ParentId)
            .Select(group => group.Select(document => document.Name).Count())
            .Sum();
        Assert.Equal(parallel.Documents.Count, duplicateNames);
    }

    [Fact]
    public void FolderScan_ParallelTraversalIsOrderDeterministic()
    {
        var fs = BuildTree();
        var scanner = new FolderCatalogScanner(fs);
        var options = new FolderScanOptions(root,
            Traversal: new ScanTraversalOptions
            {
                ConcurrencyMode = ScanConcurrencyMode.Manual,
                MaxConcurrency = 4,
                MaxRetries = 0,
                RetryDelay = TimeSpan.Zero
            });

        var first = scanner.ScanDetailed(options).Documents.Select(document => document.Name).ToArray();
        var second = scanner.ScanDetailed(options).Documents.Select(document => document.Name).ToArray();

        Assert.Equal(first, second);
    }

    [Fact]
    public void SettingsApplicator_MapsPersistedConcurrencyOptions()
    {
        var appOptions = new AppOptionsDocument
        {
            ScanConcurrencyMode = "Manual",
            ScanConcurrencyManualWorkers = 5,
            ScanConcurrencyCpuSharePercent = 60,
            ScanConcurrencyLoadShrinkPercent = 65,
            ScanConcurrencyLoadSuspendPercent = 90
        };

        var traversal = ScanSettingsApplicator.BuildTraversalOptions(
            appOptions,
            rules: null,
            sourceProfile: new ScanSourceProfile(ScanSourceKind.SolidState));

        Assert.Equal(ScanConcurrencyMode.Manual, traversal.ConcurrencyMode);
        Assert.Equal(5, traversal.MaxConcurrency);
        Assert.Equal(60, traversal.CpuSharePercent);
        Assert.Equal(65, traversal.LoadShrinkThresholdPercent);
        Assert.Equal(90, traversal.LoadSuspendThresholdPercent);
        Assert.Equal(ScanSourceKind.SolidState, traversal.SourceProfile.Kind);
    }

    [Fact]
    public async Task FolderScan_CancellationDuringTraversalStopsPromptly()
    {
        var fs = new FakeScanFileSystem();
        fs.AddDirectory(root);
        for (var i = 0; i < 60; i++)
        {
            var dir = Path.Combine(root, $"dir-{i:D3}");
            fs.AddDirectory(dir);
            for (var j = 0; j < 20; j++)
            {
                fs.AddFile(Path.Combine(dir, $"file-{j:D2}.bin"), length: 10);
            }
        }

        using var cancellation = new CancellationTokenSource();
        var progress = new CancelingProgress(cancellation, cancelAfterItems: 25);
        var scanner = new FolderCatalogScanner(fs);

        var scan = Task.Run(() => scanner.ScanDetailed(
            new FolderScanOptions(root,
                Traversal: new ScanTraversalOptions
                {
                    ConcurrencyMode = ScanConcurrencyMode.Manual,
                    MaxConcurrency = 4,
                    MaxRetries = 0,
                    RetryDelay = TimeSpan.Zero
                }),
            progress,
            cancellation.Token));

        var completed = await Task.WhenAny(scan, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(scan, completed);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan);
        Assert.True(progress.ItemsReported >= 25);
    }

    private FakeScanFileSystem BuildTree()
    {
        var fs = new FakeScanFileSystem();
        fs.AddDirectory(root);
        for (var i = 0; i < 25; i++)
        {
            var dir = Path.Combine(root, $"dir-{i:D2}");
            fs.AddDirectory(dir);
            for (var j = 0; j < 4; j++)
            {
                fs.AddFile(Path.Combine(dir, $"file-{j}.dat"), length: i + j + 1);
            }

            if (i % 5 == 0)
            {
                var nested = Path.Combine(dir, "nested");
                fs.AddDirectory(nested);
                fs.AddFile(Path.Combine(nested, "deep.bin"), length: 100 + i);
            }
        }

        fs.AddFile(Path.Combine(root, "root-file.txt"), length: 7);
        return fs;
    }

    private static List<string> DescribeDocuments(IReadOnlyList<CatalogDocument> documents)
    {
        var byId = documents.ToDictionary(document => document.Id, StringComparer.Ordinal);

        string PathOf(CatalogDocument document)
        {
            var parts = new List<string> { document.Name };
            var parentId = document.ParentId;
            while (parentId is not null && byId.TryGetValue(parentId, out var parent))
            {
                parts.Add(parent.Name);
                parentId = parent.ParentId;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        return documents
            .Select(document => $"{PathOf(document)}|{document.Type}|{document.Size}|{document.ChildrenCount}")
            .OrderBy(description => description, StringComparer.Ordinal)
            .ToList();
    }

    private sealed class CancelingProgress : IOperationProgress
    {
        private readonly CancellationTokenSource cancellation;
        private readonly int cancelAfterItems;
        private int itemsReported;

        public CancelingProgress(CancellationTokenSource cancellation, int cancelAfterItems)
        {
            this.cancellation = cancellation;
            this.cancelAfterItems = cancelAfterItems;
        }

        public int ItemsReported => Volatile.Read(ref itemsReported);

        public void Report(OperationProgressReport value)
        {
        }

        public void ReportStage(
            string stageName,
            double? stagePercentage = null,
            double? overallPercentage = null,
            long? stageCurrent = null,
            long? stageTotal = null,
            long? overallCurrent = null,
            long? overallTotal = null)
        {
        }

        public void ReportItem(
            string currentItem,
            double? stagePercentage = null,
            double? overallPercentage = null,
            long? stageCurrent = null,
            long? stageTotal = null,
            long? overallCurrent = null,
            long? overallTotal = null)
        {
            if (Interlocked.Increment(ref itemsReported) >= cancelAfterItems)
            {
                cancellation.Cancel();
            }
        }

        public void ReportOverall(
            double? overallPercentage,
            long? overallCurrent = null,
            long? overallTotal = null,
            string? operationTitle = null)
        {
        }

        public void ReportLog(
            OperationLogLevel level,
            string message,
            string? stage = null,
            string? itemPath = null,
            string? details = null)
        {
        }

        public void ReportInfo(string message, string? itemPath = null, string? stage = null)
        {
        }

        public void ReportWarning(string message, string? itemPath = null, string? stage = null)
        {
        }

        public void ReportError(string message, Exception? exception = null, string? itemPath = null, string? stage = null)
        {
        }

        public void ReportSkipped(string itemPath, string reason, string? stage = null)
        {
        }

        public void ReportFailed(string itemPath, string reason, string? stage = null, Exception? exception = null)
        {
        }
    }
}
