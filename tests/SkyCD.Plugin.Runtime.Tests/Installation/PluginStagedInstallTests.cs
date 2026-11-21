using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using SkyCD.Core.Plugins.Installation;
using SkyCD.Couchbase;
using SkyCD.Couchbase.Repository;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Installation;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests.Installation;

/// <summary>
/// End-to-end staged install, update, rollback and crash-recovery tests. Packages come from an in-memory NuGet
/// feed, activation is applied by a fresh service instance (a simulated restart) and discovery is the real
/// assembly scan. The installer never loads or executes plugin code.
/// </summary>
public sealed class PluginStagedInstallTests : IDisposable
{
    // Declared by PluginIdAttribute in the Slice fixture assembly.
    private const string PluginId = "skycd.plugin.slip-slice";

    // The packaged assembly carries the same plugin id as the package, so the requested package id is the
    // plugin id itself.
    private const string PackageId = PluginId;

    private const string ForeignPackageId = "skycd.plugin.foreign-slice";

    private const string SliceAssemblyFile = "Slice.dll";

    private const string ForeignAssemblyFile = "ForeignSlice.dll";

    private static readonly Version SliceAssemblyVersion = new(2, 5, 0, 0);

    private static readonly Version HostVersion = new(3, 5, 0);

    private static readonly string ReceiptFileName = PluginInstallLayout.ReceiptFileName;

    private static readonly string ContentFolder = Path.Combine("lib", PluginPackageFixtures.DefaultTargetFramework);

    private readonly string root = Path.Combine(Path.GetTempPath(), $"skycd-staging-{Guid.NewGuid():N}");

    private readonly PluginPackageFixtures fixtures = new();

    public PluginStagedInstallTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // A locked leftover does not change assertion results.
        }
        catch (UnauthorizedAccessException)
        {
            // Assemblies loaded by discovery stay locked until the test process exits.
        }
    }

    [Fact]
    public async Task FreshInstall_StagesOutsideDiscovery_AndActivatesOnlyOnRestart()
    {
        var feed = new FakeNuGetFeed();
        feed.AddPackage(PackageId, "1.0.0", Package(PluginId, "1.0.0"));
        var installer = CreateInstaller(feed);

        var staged = await installer.StageInstallAsync(Request("1.0.0"));

        Assert.True(staged.IsSuccess, staged.Message);
        Assert.Equal(PluginInstallOperationStatus.PendingRestart, staged.Status);
        Assert.Equal(PluginId, staged.State.PluginId);
        Assert.Equal("1.0.0", staged.State.PendingVersion);
        Assert.Null(staged.State.ActiveVersion);
        Assert.True(staged.State.IsFreshInstall);
        Assert.True(staged.State.RestartRequired);
        Assert.Equal(1, feed.PackageRequests);
        Assert.DoesNotContain(PluginId, DiscoverPluginIds());
        Assert.False(Directory.Exists(installedFolder(installer)));

        var pendingPath = installer.Layout.ResolvePendingPath(PluginId);
        Assert.True(File.Exists(pendingPath));
        Assert.StartsWith(Path.Combine(installer.Layout.StagingRoot, "pending"), pendingPath);
        Assert.Single(OperationStagingDirectories(installer));

        // A restarted host applies pending operations before discovery.
        var restarted = CreateInstaller(new FakeNuGetFeed());
        var applied = Assert.Single(await restarted.ApplyPendingAsync());

        Assert.True(applied.IsSuccess, applied.Message);
        Assert.Equal(PluginInstallOperationStatus.Installed, applied.Status);
        Assert.Equal("1.0.0", applied.State.ActiveVersion);
        Assert.Equal(SliceAssemblyVersion, DiscoveredVersion(restarted));
        Assert.Equal("1.0.0", InstalledMarker(restarted));
        Assert.False(File.Exists(pendingPath));

        var receipt = ReadReceipt(restarted);
        Assert.Equal(PackageId, receipt.PackageId);
        Assert.Equal(FakeNuGetFeed.ServiceIndexUrl, receipt.SourceUrl);
        Assert.Equal("1.0.0", receipt.Version);
        Assert.Equal("SkyCD.Plugin.SlipSlice", receipt.EntryAssemblyName);
        Assert.Contains("2.5.0.0", receipt.AssemblyVersion);
        Assert.Equal(Path.Combine(ContentFolder, "Slice.dll").Replace('\\', '/'),
            receipt.EntryAssemblyPath.Replace('\\', '/'));
        Assert.True(File.Exists(Path.Combine(installedFolder(restarted), ContentFolder,
            "SkyCD.Plugin.SlipSlice.Private.dll")), "Bundled private dependencies are installed next to the entry assembly.");

        Assert.Empty(OperationStagingDirectories(restarted));
        Assert.Empty(await restarted.ApplyPendingAsync());
    }

    [Fact]
    public async Task Update_KeepsOldVersionActiveUntilRestart_AndRetainsPreviousVersionBackup()
    {
        var feed = new FakeNuGetFeed();
        var installer = CreateInstaller(feed);
        await InstallAndActivateAsync(feed, installer, "1.0.0");

        feed.AddPackage(PackageId, "2.0.0", Package(PluginId, "2.0.0"));
        var staged = await installer.StageInstallAsync(Request("2.0.0"));

        Assert.True(staged.IsSuccess, staged.Message);
        Assert.Equal(PluginInstallOperationStatus.PendingRestart, staged.Status);
        Assert.Equal("1.0.0", staged.State.ActiveVersion);
        Assert.Equal("2.0.0", staged.State.PendingVersion);
        Assert.False(staged.State.IsFreshInstall);
        Assert.Equal("1.0.0", ReadReceipt(installer).Version);
        Assert.Equal("1.0.0", InstalledMarker(installer));
        Assert.Equal(SliceAssemblyVersion, DiscoveredVersion(installer));

        var applied = Assert.Single(await CreateInstaller(feed).ApplyPendingAsync());

        Assert.True(applied.IsSuccess, applied.Message);
        Assert.Equal("2.0.0", applied.State.ActiveVersion);
        Assert.Equal("2.0.0", ReadReceipt(installer).Version);
        Assert.Equal("1.0.0", ReadReceipt(installer).PreviousVersion);
        Assert.Equal("2.0.0", InstalledMarker(installer));
        Assert.Equal(SliceAssemblyVersion, DiscoveredVersion(installer));
        var backup = Assert.Single(Directory.GetDirectories(installer.Layout.PluginRoot, $".backup-{PluginId}-*"));
        Assert.True(File.Exists(Path.Combine(backup, ReceiptFileName)), "The previous version stays recoverable.");
        Assert.Empty(OperationStagingDirectories(installer));
    }

    [Fact]
    public async Task FailedReplacement_OnRestart_KeepsPreviousVersionActiveAndReportsFailure()
    {
        var feed = new FakeNuGetFeed();
        var installer = CreateInstaller(feed);
        await InstallAndActivateAsync(feed, installer, "1.0.0");

        feed.AddPackage(PackageId, "2.0.0", Package(PluginId, "2.0.0"));
        Assert.True((await installer.StageInstallAsync(Request("2.0.0"))).IsSuccess);

        // Lock the active entry assembly: replacement must not proceed and the old version must stay active.
        var activeAssembly = Path.Combine(installedFolder(installer), ContentFolder, "Slice.dll");
        Assert.True(File.Exists(activeAssembly));
        using (new FileStream(activeAssembly, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var applied = Assert.Single(await CreateInstaller(feed).ApplyPendingAsync());

            Assert.False(applied.IsSuccess);
            Assert.Equal(PluginInstallFailureReason.DestinationUnavailable, applied.FailureReason);
        }

        Assert.Equal("1.0.0", ReadReceipt(installer).Version);
        Assert.Equal("1.0.0", InstalledMarker(installer));
        Assert.Equal(PluginInstallOperationStatus.Failed, installer.GetInstallState(PluginId).Status);
        Assert.Equal("2.0.0", installer.GetInstallState(PluginId).PendingVersion);
        Assert.Empty(OperationStagingDirectories(installer));
    }

    [Fact]
    public async Task FailedMoveAfterBackup_RestoresPreviousVersionAndReportsRollback()
    {
        var feed = new FakeNuGetFeed();
        var installer = CreateInstaller(feed);
        await InstallAndActivateAsync(feed, installer, "1.0.0");

        feed.AddPackage(PackageId, "2.0.0", Package(PluginId, "2.0.0"));
        Assert.True((await installer.StageInstallAsync(Request("2.0.0"))).IsSuccess);

        // Lock a file in the staged content: the backup move succeeds but the replacement cannot be placed.
        var pending = ReadPending(installer);
        var stagedDependency = Path.Combine(pending.StagedPath!, "prepared", ContentFolder,
            "SkyCD.Plugin.SlipSlice.Private.dll");
        Assert.True(File.Exists(stagedDependency));
        using (new FileStream(stagedDependency, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var applied = Assert.Single(await CreateInstaller(feed).ApplyPendingAsync());

            Assert.False(applied.IsSuccess, applied.Message);
            Assert.Equal(PluginInstallFailureReason.DestinationUnavailable, applied.FailureReason);
        }

        Assert.Equal("1.0.0", ReadReceipt(installer).Version);
        Assert.Equal("1.0.0", InstalledMarker(installer));
        Assert.Equal(SliceAssemblyVersion, DiscoveredVersion(installer));
        Assert.Equal(PluginInstallOperationStatus.RolledBack, installer.GetInstallState(PluginId).Status);
        Assert.Empty(BackupDirectories(installer));

        // The locked staging copy could not be removed during the failed activation; the next startup cleans it.
        Assert.True(await installer.ApplyPendingAsync() is not null);
        Assert.Empty(OperationStagingDirectories(installer));
    }

    [Fact]
    public async Task InvalidContentAfterReplacement_RestoresPreviousVersionAndReportsFailure()
    {
        var feed = new FakeNuGetFeed();
        var installer = CreateInstaller(feed);
        await InstallAndActivateAsync(feed, installer, "1.0.0");
        var filesBefore = Directory.GetFiles(installedFolder(installer), "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(installedFolder(installer), path))
            .Order(StringComparer.Ordinal)
            .ToList();

        // The staged package is altered between restarts: its entry assembly no longer matches the marker.
        feed.AddPackage(PackageId, "2.0.0", Package(PluginId, "2.0.0"));
        Assert.True((await installer.StageInstallAsync(Request("2.0.0"))).IsSuccess);
        var pending = ReadPending(installer);
        File.WriteAllText(Path.Combine(pending.StagedPath!, "prepared", ContentFolder, "Slice.dll"), "tampered");

        var applied = Assert.Single(await CreateInstaller(feed).ApplyPendingAsync());

        Assert.False(applied.IsSuccess);
        Assert.Equal(PluginInstallFailureReason.InvalidPluginAssembly, applied.FailureReason);
        Assert.Equal("1.0.0", ReadReceipt(installer).Version);
        Assert.Equal("1.0.0", InstalledMarker(installer));
        Assert.Equal(filesBefore, Directory.GetFiles(installedFolder(installer), "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(installedFolder(installer), path))
            .Order(StringComparer.Ordinal));
        Assert.Equal(PluginInstallOperationStatus.Installed, installer.GetInstallState(PluginId).Status);
        Assert.Empty(BackupDirectories(installer));
        Assert.Empty(OperationStagingDirectories(installer));
    }

    [Theory]
    [InlineData("download-http-500", PluginInstallFailureReason.DownloadFailed)]
    [InlineData("download-connection-error", PluginInstallFailureReason.DownloadFailed)]
    [InlineData("download-interrupted", PluginInstallFailureReason.DownloadFailed)]
    [InlineData("corrupt-archive", PluginInstallFailureReason.ArchiveCorrupt)]
    [InlineData("gzip-not-zip", PluginInstallFailureReason.ArchiveCorrupt)]
    [InlineData("traversal", PluginInstallFailureReason.UnsafeArchivePath)]
    [InlineData("backslash-traversal", PluginInstallFailureReason.UnsafeArchivePath)]
    [InlineData("absolute-path", PluginInstallFailureReason.UnsafeArchivePath)]
    [InlineData("duplicate-entry", PluginInstallFailureReason.UnsafeArchivePath)]
    [InlineData("nuspec-id", PluginInstallFailureReason.PackageIdentityMismatch)]
    [InlineData("nuspec-version", PluginInstallFailureReason.PackageIdentityMismatch)]
    [InlineData("incompatible-host", PluginInstallFailureReason.IncompatibleHostVersion)]
    [InlineData("identity-conflict-manual", PluginInstallFailureReason.IdentityConflict)]
    [InlineData("identity-conflict-foreign", PluginInstallFailureReason.IdentityConflict)]
    public async Task InvalidPackage_PreservesExistingState_ReportsReason_AndCleansStaging(string scenario,
        PluginInstallFailureReason expectedReason)
    {
        var feed = new FakeNuGetFeed();
        var installer = CreateInstaller(feed);
        string? protectedFile = null;
        switch (scenario)
        {
            case "identity-conflict-manual":
                // A manually installed plugin in the catalog folder has no receipt and must never be replaced.
                var manualFolder = installedFolder(installer);
                Directory.CreateDirectory(manualFolder);
                protectedFile = Path.Combine(manualFolder, "manual-plugin.dll");
                File.WriteAllText(protectedFile, "manually installed plugin");
                break;
            case "identity-conflict-foreign":
                await InstallForeignCatalogPluginAsync(feed, installer);
                protectedFile = Path.Combine(installer.Layout.ResolveInstallFolder(ForeignPackageId),
                    ReceiptFileName);
                break;
            default:
                await InstallAndActivateAsync(feed, installer, "1.0.0");
                break;
        }

        ArrangeScenario(feed, scenario);
        var staged = await installer.StageInstallAsync(Request("2.0.0"));

        Assert.False(staged.IsSuccess);
        Assert.Equal(expectedReason, staged.FailureReason);
        Assert.Equal(PluginInstallOperationStatus.Failed, staged.Status);
        Assert.False(string.IsNullOrWhiteSpace(staged.Message));

        if (scenario == "identity-conflict-foreign")
        {
            Assert.Equal("1.0.0", ReadReceipt(installer, ForeignPackageId).Version);
        }
        else if (scenario != "identity-conflict-manual")
        {
            Assert.Equal("1.0.0", ReadReceipt(installer).Version);
            Assert.Equal("1.0.0", InstalledMarker(installer));
        }

        Assert.Null(installer.GetInstallState(PluginId).PendingVersion);
        Assert.False(File.Exists(installer.Layout.ResolvePendingPath(PluginId)));
        Assert.Empty(OperationStagingDirectories(installer));
        Assert.Empty(BackupDirectories(installer));
        Assert.False(File.Exists(Path.Combine(root, "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(root, "skycd-evil.dll")));
        if (protectedFile is not null)
        {
            Assert.True(File.Exists(protectedFile), "Existing plugin content must never be overwritten.");
        }
    }

    [Fact]
    public async Task RetryingTheSameOperation_IsIdempotent_AndDoesNotDownloadAgain()
    {
        var feed = new FakeNuGetFeed();
        feed.AddPackage(PackageId, "1.0.0", Package(PluginId, "1.0.0"));
        var installer = CreateInstaller(feed);

        var first = await installer.StageInstallAsync(Request("1.0.0"));
        var requestsAfterFirst = feed.PackageRequests;
        var second = await installer.StageInstallAsync(Request("1.0.0"));

        Assert.True(first.IsSuccess, first.Message);
        Assert.True(second.IsSuccess, second.Message);
        Assert.True(second.IsIdempotentRetry);
        Assert.Equal(requestsAfterFirst, feed.PackageRequests);
        Assert.Equal(first.State.PendingVersion, second.State.PendingVersion);
        Assert.Single(OperationStagingDirectories(installer));

        Assert.True(Assert.Single(await CreateInstaller(feed).ApplyPendingAsync()).IsSuccess);
        Assert.Equal("1.0.0", ReadReceipt(installer).Version);

        // Repeating an already installed request stages an update; a canceled retry does not duplicate state.
        Assert.True((await installer.CancelPendingAsync(PluginId)).IsSuccess);
    }

    [Fact]
    public async Task ConcurrentOperations_ForTheSamePlugin_AreSerialized()
    {
        var feed = new FakeNuGetFeed();
        feed.AddPackage(PackageId, "1.0.0", Package(PluginId, "1.0.0"));
        var installer = CreateInstaller(feed);

        var results = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => installer.StageInstallAsync(Request("1.0.0")))));

        Assert.All(results, result => Assert.True(result.IsSuccess, result.Message));
        Assert.Single(results, result => !result.IsIdempotentRetry);
        Assert.Equal(3, results.Count(result => result.IsIdempotentRetry));
        Assert.All(results, result => Assert.Equal(PluginInstallOperationStatus.PendingRestart, result.Status));
        Assert.Single(OperationStagingDirectories(installer));
    }

    [Fact]
    public async Task ConcurrentOperations_ForDifferentPlugins_StageIndependently()
    {
        var feed = new FakeNuGetFeed();
        feed.AddPackage(PackageId, "1.0.0", Package(PluginId, "1.0.0"));
        feed.AddPackage(ForeignPackageId, "1.0.0", ForeignPackage("1.0.0"));
        var installer = CreateInstaller(feed);

        var results = await Task.WhenAll(
            installer.StageInstallAsync(Request("1.0.0")),
            installer.StageInstallAsync(new PluginPackageRequest(FakeNuGetFeed.ServiceIndexUrl,
                ForeignPackageId, "1.0.0")));

        Assert.All(results, result => Assert.True(result.IsSuccess, result.Message));
        Assert.Equal(PluginId, results[0].State.PluginId);
        Assert.Equal(ForeignPackageId, results[1].State.PluginId);
    }

    [Fact]
    public async Task Package_CannotWriteOutsideStagingOrInstallFolder()
    {
        var feed = new FakeNuGetFeed();
        var installer = CreateInstaller(feed);
        feed.AddPackage(PackageId, "2.0.0", fixtures.Build(fixtures.CreateFreshPlugin(PluginId, "2.0.0"),
            extraEntries: new Dictionary<string, byte[]>
            {
                ["lib/net10.0/../../../../escaped.txt"] = [1, 2, 3]
            }));

        var staged = await installer.StageInstallAsync(Request("2.0.0"));

        Assert.False(staged.IsSuccess);
        Assert.Equal(PluginInstallFailureReason.UnsafeArchivePath, staged.FailureReason);
        Assert.False(File.Exists(Path.Combine(root, "escaped.txt")));
        Assert.False(Directory.Exists(installedFolder(installer)));
        Assert.Empty(OperationStagingDirectories(installer));
    }

    [Fact]
    public async Task InterruptedDownload_IsCleaned_AndLeftoverStagingIsRemovedByNextOperation()
    {
        var feed = new FakeNuGetFeed();
        feed.AddPackage(PackageId, "1.0.0", Package(PluginId, "1.0.0"));
        var installer = CreateInstaller(feed);

        feed.DownloadStatus = HttpStatusCode.InternalServerError;
        var failed = await installer.StageInstallAsync(Request("1.0.0"));
        Assert.False(failed.IsSuccess);
        Assert.Equal(PluginInstallFailureReason.DownloadFailed, failed.FailureReason);
        Assert.Empty(OperationStagingDirectories(installer));

        // Leftover from a crashed process: removed by the next operation instead of accumulating.
        var orphan = Path.Combine(installer.Layout.StagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(orphan);
        File.WriteAllText(Path.Combine(orphan, "package.nupkg"), "partial");

        feed.DownloadStatus = HttpStatusCode.OK;
        var staged = await installer.StageInstallAsync(Request("1.0.0"));

        Assert.True(staged.IsSuccess, staged.Message);
        Assert.False(Directory.Exists(orphan));
    }

    [Fact]
    public async Task CancelPending_DiscardsStagedOperation_WithoutTouchingActiveVersion()
    {
        var feed = new FakeNuGetFeed();
        var installer = CreateInstaller(feed);
        await InstallAndActivateAsync(feed, installer, "1.0.0");
        feed.AddPackage(PackageId, "2.0.0", Package(PluginId, "2.0.0"));
        Assert.True((await installer.StageInstallAsync(Request("2.0.0"))).IsSuccess);

        var cancelled = await installer.CancelPendingAsync(PluginId);

        Assert.True(cancelled.IsSuccess);
        Assert.Equal(PluginInstallOperationStatus.Cancelled, cancelled.Status);
        Assert.False(File.Exists(installer.Layout.ResolvePendingPath(PluginId)));
        Assert.Empty(OperationStagingDirectories(installer));
        Assert.Equal("1.0.0", ReadReceipt(installer).Version);
        Assert.Empty(await installer.ApplyPendingAsync());
    }

    [Fact]
    public async Task CrashAfterContentMovedIntoPlace_CompletesInstallOnNextStartup()
    {
        var feed = new FakeNuGetFeed();
        feed.AddPackage(PackageId, "1.0.0", Package(PluginId, "1.0.0"));
        var installer = CreateInstaller(feed);
        Assert.True((await installer.StageInstallAsync(Request("1.0.0"))).IsSuccess);

        // Simulate a crash between the folder move and the receipt write.
        var pending = ReadPending(installer);
        var installFolder = installedFolder(installer);
        Directory.CreateDirectory(Path.GetDirectoryName(installFolder)!);
        Directory.Move(Path.Combine(pending.StagedPath!, "prepared"), installFolder);

        var applied = Assert.Single(await CreateInstaller(feed).ApplyPendingAsync());

        Assert.True(applied.IsSuccess, applied.Message);
        Assert.Equal("1.0.0", ReadReceipt(installer).Version);
        Assert.Equal("1.0.0", InstalledMarker(installer));
        Assert.Equal(SliceAssemblyVersion, DiscoveredVersion(installer));
        Assert.False(File.Exists(installer.Layout.ResolvePendingPath(PluginId)));
        Assert.Empty(OperationStagingDirectories(installer));
    }

    [Fact]
    public async Task CrashBetweenFolderMoves_CompletesInstallOnNextStartup()
    {
        var feed = new FakeNuGetFeed();
        feed.AddPackage(PackageId, "1.0.0", Package(PluginId, "1.0.0"));
        var installer = CreateInstaller(feed);
        Assert.True((await installer.StageInstallAsync(Request("1.0.0"))).IsSuccess);

        // Simulate a crash after the staged content was renamed into the plugin root but before it was placed.
        var pending = ReadPending(installer);
        var installFolder = installedFolder(installer);
        Directory.CreateDirectory(installer.Layout.PluginRoot);
        var transientFolder = Path.Combine(installer.Layout.PluginRoot, $".replace-{PluginId}-{Guid.NewGuid():N}");
        Directory.Move(Path.Combine(pending.StagedPath!, "prepared"), transientFolder);

        var applied = Assert.Single(await CreateInstaller(feed).ApplyPendingAsync());

        Assert.True(applied.IsSuccess, applied.Message);
        Assert.Equal("1.0.0", ReadReceipt(installer).Version);
        Assert.Equal("1.0.0", InstalledMarker(installer));
        Assert.True(Directory.Exists(installFolder));
        Assert.False(Directory.Exists(transientFolder));
    }

    [Fact]
    public async Task StartupWithHostAbovePendingMaximum_KeepsExistingVersionAndReportsIncompatibility()
    {
        var feed = new FakeNuGetFeed();
        var installer = CreateInstaller(feed);
        await InstallAndActivateAsync(feed, installer, "1.0.0");
        feed.AddPackage(PackageId, "2.0.0", Package(PluginId, "2.0.0"));
        Assert.True((await installer.StageInstallAsync(Request("2.0.0"))).IsSuccess);

        var newerHost = new PluginInstallService(installer.Layout, new Version(10, 0, 0),
            new TestLogger<PluginInstallService>(), feed);
        var applied = Assert.Single(await newerHost.ApplyPendingAsync());

        Assert.False(applied.IsSuccess);
        Assert.Equal(PluginInstallFailureReason.IncompatibleHostVersion, applied.FailureReason);
        Assert.Equal("1.0.0", ReadReceipt(installer).Version);
        Assert.Equal("1.0.0", InstalledMarker(installer));
        Assert.Equal(SliceAssemblyVersion, DiscoveredVersion(installer));
        Assert.Empty(OperationStagingDirectories(installer));
    }

    [Fact]
    public async Task InsecureServiceIndex_FailsWithFeedReason_AndNeverDownloads()
    {
        var feed = new FakeNuGetFeed();
        var installer = CreateInstaller(feed);

        var insecure = await installer.StageInstallAsync(
            new PluginPackageRequest("http://feed.test/v3/index.json", PackageId, "1.0.0"));

        Assert.False(insecure.IsSuccess);
        Assert.Equal(PluginInstallFailureReason.FeedUnavailable, insecure.FailureReason);
        Assert.Equal(0, feed.PackageRequests);
    }

    [Fact]
    public async Task StagingRoot_NestedInPluginRoot_IsRejectedByTheInstaller()
    {
        var feed = new FakeNuGetFeed();
        var service = new PluginInstallService(
            new PluginInstallLayout(Path.Combine(root, "plugins"), Path.Combine(root, "plugins", "staging")),
            HostVersion, new TestLogger<PluginInstallService>(), feed);

        await Assert.ThrowsAsync<ArgumentException>(() => service.StageInstallAsync(Request("1.0.0")));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ApplyPendingAsync());
        Assert.Equal(0, feed.PackageRequests);
    }

    [Fact]
    public void PackageIdentity_OrdersVersionsWithNuGetSemantics()
    {
        Assert.True(PackageIdentity.CompareVersions("1.0.0", "1.0.0.0") == 0);
        Assert.True(PackageIdentity.CompareVersions("1.0.0-beta", "1.0.0") < 0);
        Assert.True(PackageIdentity.CompareVersions("1.0.0-alpha.2", "1.0.0-alpha.10") < 0);
        Assert.True(PackageIdentity.CompareVersions("1.2.0", "1.10.0") < 0);
        Assert.True(PackageIdentity.IsExactVersion("2.0.0-rc.1"));
        Assert.False(PackageIdentity.IsExactVersion("1.*"));
        Assert.False(PackageIdentity.IsExactVersion("[1.0.0,2.0.0)"));
        Assert.Equal("1.0.0", PackageIdentity.Normalize(" 1.0.0+build.5 "));
    }

    private byte[] Package(string pluginId, string version, string assemblyFile = SliceAssemblyFile) =>
        fixtures.Build(fixtures.CreateFreshPlugin(pluginId, version, assemblyFile));

    private byte[] ForeignPackage(string version) => fixtures.Build(
        new PluginFixture(ForeignPackageId, version, ForeignAssemblyFile, "SkyCD.Plugin.ForeignSlice.dll"));

    private void ArrangeScenario(FakeNuGetFeed feed, string scenario)
    {
        var fixture = fixtures.CreateFreshPlugin(PluginId, "2.0.0");
        feed.AddPackage(PackageId, "2.0.0", fixtures.Build(fixture));
        switch (scenario)
        {
            case "download-http-500":
                feed.DownloadStatus = HttpStatusCode.InternalServerError;
                break;
            case "download-connection-error":
                feed.DownloadThrowsConnectionError = true;
                break;
            case "download-interrupted":
                feed.DownloadOverride = fixtures.Build(fixture);
                feed.TruncatedLength = feed.DownloadOverride.Length / 2;
                break;
            case "corrupt-archive":
                feed.DownloadOverride = [0x50, 0x4B, 0x03, 0x04, 0x00, 0x01, 0x02, 0x03];
                break;
            case "gzip-not-zip":
                feed.DownloadOverride = fixtures.BuildTarGz(PluginId, "2.0.0");
                break;
            case "traversal":
                feed.DownloadOverride = fixtures.Build(fixture, extraEntries: new Dictionary<string, byte[]>
                {
                    ["lib/net10.0/../../../../escaped.txt"] = [0x4D, 0x5A]
                });
                break;
            case "backslash-traversal":
                feed.DownloadOverride = fixtures.Build(fixture, mutate: archive =>
                    PluginPackageFixtures.WriteEntry(archive, @"lib\net10.0\..\..\skycd-evil.dll", [0x4D, 0x5A]));
                break;
            case "absolute-path":
                feed.DownloadOverride = fixtures.Build(fixture, mutate: archive =>
                    PluginPackageFixtures.WriteEntry(archive, "/skycd-evil.dll", [0x4D, 0x5A]));
                break;
            case "duplicate-entry":
                feed.DownloadOverride = fixtures.Build(fixture, mutate: archive =>
                    PluginPackageFixtures.WriteEntry(archive, "lib/net10.0/SkyCD.Plugin.SlipSlice.Private.dll",
                        [0x4D, 0x5A]));
                break;
            case "nuspec-id":
                feed.DownloadOverride = fixtures.Build(fixture, nuspecId: "skycd.plugin.other");
                break;
            case "nuspec-version":
                feed.DownloadOverride = fixtures.Build(fixture, nuspecVersion: "3.0.0");
                break;
            case "incompatible-host":
                // The test assembly declares a maximum host version of 3.0.0, below the simulated 3.5.0 host.
                feed.DownloadOverride = fixtures.Build(
                    new PluginFixture(PackageId, "2.0.0", "SkyCD.Plugin.Runtime.Tests.dll",
                        "SkyCD.Plugin.Runtime.Tests.dll"));
                break;
            case "identity-conflict-manual":
                feed.DownloadOverride = fixtures.Build(fixture);
                break;
            case "identity-conflict-foreign":
                // A different catalog plugin exists under the foreign package id: a slice package whose entry
                // declares that identity may not take it over, and the other plugin's install stays untouched.
                feed.DownloadOverride = fixtures.Build(
                    new PluginFixture(PackageId, "2.0.0", ForeignAssemblyFile, "SkyCD.Plugin.ForeignSlice.dll"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown scenario.");
        }
    }

    private PluginInstallService CreateInstaller(FakeNuGetFeed feed) => new(
        new PluginInstallLayout(Path.Combine(root, "plugins"), Path.Combine(root, "staging", HostVersion.ToString())),
        HostVersion,
        new TestLogger<PluginInstallService>(),
        feed);

    private static PluginPackageRequest Request(string version) =>
        new(FakeNuGetFeed.ServiceIndexUrl, PackageId, version);

    private async Task InstallAndActivateAsync(FakeNuGetFeed feed, PluginInstallService installer, string version)
    {
        feed.AddPackage(PackageId, version, Package(PluginId, version));
        var staged = await installer.StageInstallAsync(Request(version));
        Assert.True(staged.IsSuccess, staged.Message);
        var applied = Assert.Single(await installer.ApplyPendingAsync());
        Assert.True(applied.IsSuccess, applied.Message);
    }

    private async Task InstallForeignCatalogPluginAsync(FakeNuGetFeed feed, PluginInstallService installer)
    {
        feed.AddPackage(ForeignPackageId, "1.0.0", ForeignPackage("1.0.0"));
        var staged = await installer.StageInstallAsync(new PluginPackageRequest(FakeNuGetFeed.ServiceIndexUrl,
            ForeignPackageId, "1.0.0"));
        Assert.True(staged.IsSuccess, staged.Message);
        Assert.Equal(ForeignPackageId, staged.State.PluginId);
        Assert.True(Assert.Single(await installer.ApplyPendingAsync()).IsSuccess);
    }

    private IReadOnlyCollection<string> DiscoverPluginIds() => DiscoverPluginIds(Path.Combine(root, "plugins"));

    private Version? DiscoveredVersion(PluginInstallService installer) =>
        Discover(installer.Layout.PluginRoot).FirstOrDefault(plugin => plugin.Id == PluginId)?.Version;

    private IReadOnlyCollection<string> DiscoverPluginIds(string pluginRoot) =>
        Discover(pluginRoot).Select(plugin => plugin.Id).ToList();

    private static IReadOnlyList<SkyCD.Plugin.Runtime.Discovery.DiscoveredPlugin> Discover(string pluginRoot)
    {
        var databaseManager = new DatabaseManager();
        var directory = Path.Combine(Path.GetTempPath(), "SkyCD", "PluginStagedInstallTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        databaseManager.Connect("default", directory);
        var repositoryManager = new RepositoryManager(databaseManager);
        var pluginManager = new PluginManager(new TestLogger<PluginManager>(),
            new AssembliesListFactory(new TestLogger<AssembliesListFactory>()),
            new DiscoveredPluginFactory(),
            (IRepository<SkyCD.Plugin.Runtime.Documents.PluginDocument>)repositoryManager
                .For<SkyCD.Plugin.Runtime.Documents.PluginDocument>());
        pluginManager.Discover(pluginRoot, HostVersion);
        return pluginManager.Plugins.ToList();
    }

    private static PluginInstallReceipt ReadReceipt(PluginInstallService installer, string? pluginId = null)
    {
        var folder = installer.Layout.ResolveInstallFolder(pluginId ?? PluginId);
        return JsonSerializer.Deserialize<PluginInstallReceipt>(
            File.ReadAllText(Path.Combine(folder, ReceiptFileName)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static PluginInstallOperation ReadPending(PluginInstallService installer) =>
        JsonSerializer.Deserialize<PluginInstallOperation>(
            File.ReadAllText(installer.Layout.ResolvePendingPath(PluginId)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static string InstalledMarker(PluginInstallService installer) =>
        File.ReadAllText(Path.Combine(installedFolder(installer), ContentFolder, "package-version.txt"));

    private static string installedFolder(PluginInstallService installer) =>
        installer.Layout.ResolveInstallFolder(PluginId);

    private static List<string> OperationStagingDirectories(PluginInstallService installer)
    {
        if (!Directory.Exists(installer.Layout.StagingRoot))
        {
            return [];
        }

        return Directory.GetDirectories(installer.Layout.StagingRoot)
            .Where(directory => !directory.EndsWith("pending", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static List<string> BackupDirectories(PluginInstallService installer)
    {
        if (!Directory.Exists(installer.Layout.PluginRoot))
        {
            return [];
        }

        return Directory.GetDirectories(installer.Layout.PluginRoot, ".backup-*").ToList();
    }
}
