using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SkyCD.Core.Plugins.Installation;
using SkyCD.Plugin.Runtime.Installation;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests.Installation;

/// <summary>
/// Uninstall refuses anything that is not a catalog install folder directly below the plugin root, never deletes
/// outside that root, and leaves the install intact when the files cannot be removed. Every test uses a temporary
/// plugin root; the real plugin directory is never touched.
/// </summary>
public sealed class PluginUninstallTests : IDisposable
{
    private const string PluginId = "skycd.plugin.demo";

    private const string ForeignPluginId = "skycd.plugin.foreign";

    private static readonly Version HostVersion = new(3, 5, 0);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string root = Path.Combine(Path.GetTempPath(), $"skycd-uninstall-{Guid.NewGuid():N}");

    public PluginUninstallTests()
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
            // A locked leftover does not change assertion results.
        }
    }

    private string PluginRoot => Path.Combine(root, "plugins");

    private string InstallFolder => Path.Combine(PluginRoot, PluginId);

    private string StagingRoot => Path.Combine(root, "staging", HostVersion.ToString());

    private PluginInstallService CreateService() =>
        new(new PluginInstallLayout(PluginRoot, StagingRoot), HostVersion, new TestLogger<PluginInstallService>());

    [Fact]
    public async Task Uninstall_RemovesCatalogFolderReceiptAndInstallState()
    {
        CreateCatalogInstall();

        var result = await CreateService().UninstallAsync(PluginId);

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(PluginInstallOperationStatus.NotInstalled, result.Status);
        Assert.Equal(PluginId, result.State.PluginId);
        Assert.False(Directory.Exists(InstallFolder));
        Assert.Empty(CreateService().GetInstallStates());
        Assert.Empty(Directory.GetFileSystemEntries(PluginRoot));
    }

    [Fact]
    public async Task Uninstall_AlsoDiscardsPendingOperationAndBackups()
    {
        CreateCatalogInstall();
        var pendingPath = WritePendingOperation("2.0.0");
        var backup = Path.Combine(PluginRoot, $".backup-{PluginId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(backup, "Demo.dll"), "old");

        var result = await CreateService().UninstallAsync(PluginId);

        Assert.True(result.IsSuccess, result.Message);
        Assert.False(File.Exists(pendingPath));
        Assert.False(Directory.Exists(backup));
        Assert.False(Directory.Exists(InstallFolder));
    }

    [Fact]
    public async Task Uninstall_RefusesFolderWithoutReceipt_AndKeepsFiles()
    {
        Directory.CreateDirectory(InstallFolder);
        File.WriteAllText(Path.Combine(InstallFolder, "Demo.dll"), "manual");

        var result = await CreateService().UninstallAsync(PluginId);

        Assert.False(result.IsSuccess);
        Assert.Equal(PluginInstallFailureReason.NotCatalogOwned, result.FailureReason);
        Assert.True(File.Exists(Path.Combine(InstallFolder, "Demo.dll")));
    }

    [Fact]
    public async Task Uninstall_RefusesUnreadableReceipt_AndKeepsFiles()
    {
        Directory.CreateDirectory(InstallFolder);
        File.WriteAllText(Path.Combine(InstallFolder, "Demo.dll"), "plugin");
        File.WriteAllText(Path.Combine(InstallFolder, PluginInstallLayout.ReceiptFileName), "{ not json");

        var result = await CreateService().UninstallAsync(PluginId);

        Assert.False(result.IsSuccess);
        Assert.Equal(PluginInstallFailureReason.NotCatalogOwned, result.FailureReason);
        Assert.True(File.Exists(Path.Combine(InstallFolder, "Demo.dll")));
    }

    [Fact]
    public async Task Uninstall_RefusesReceiptOfAnotherPlugin_AndKeepsFiles()
    {
        CreateCatalogInstall(receiptPluginId: ForeignPluginId);

        var result = await CreateService().UninstallAsync(PluginId);

        Assert.False(result.IsSuccess);
        Assert.Equal(PluginInstallFailureReason.NotCatalogOwned, result.FailureReason);
        Assert.True(Directory.Exists(InstallFolder));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("..")]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("nested/folder")]
    [InlineData("nested\\folder")]
    [InlineData(".hidden")]
    [InlineData("plugin:stream")]
    public async Task Uninstall_RefusesUnsafeIds_WithoutTouchingTheFileSystem(string pluginId)
    {
        CreateCatalogInstall();
        var marker = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
        var markerFile = Path.Combine(marker, "keep.txt");
        File.WriteAllText(markerFile, "keep");

        var result = await CreateService().UninstallAsync(pluginId);

        Assert.False(result.IsSuccess);
        Assert.Equal(PluginInstallFailureReason.InvalidPluginId, result.FailureReason);
        Assert.True(Directory.Exists(InstallFolder));
        Assert.True(File.Exists(markerFile));
    }

    [Fact]
    public void IsDirectChildOfPluginRoot_RejectsTheRootItselfAndEscapingPaths()
    {
        var layout = new PluginInstallLayout(PluginRoot, StagingRoot);

        Assert.True(layout.IsDirectChildOfPluginRoot(Path.Combine(PluginRoot, PluginId)));
        Assert.True(layout.IsDirectChildOfPluginRoot(Path.Combine(PluginRoot, PluginId) + Path.DirectorySeparatorChar));
        Assert.False(layout.IsDirectChildOfPluginRoot(PluginRoot));
        Assert.False(layout.IsDirectChildOfPluginRoot(Path.Combine(PluginRoot, "nested", PluginId)));
        Assert.False(layout.IsDirectChildOfPluginRoot(Path.Combine(root, "outside")));
        Assert.False(layout.IsDirectChildOfPluginRoot(Path.Combine(PluginRoot, "..", "outside")));
        Assert.False(layout.IsDirectChildOfPluginRoot(" "));
    }

    [Fact]
    public async Task Uninstall_ReportsLockedFiles_AndRestoresTheInstall()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Unix allows deleting an open file, so the lock cannot be observed there.
            return;
        }

        CreateCatalogInstall();
        var assemblyPath = Path.Combine(InstallFolder, "Demo.dll");
        await using var lockStream = new FileStream(assemblyPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = await CreateService().UninstallAsync(PluginId);

        Assert.False(result.IsSuccess);
        Assert.Equal(PluginInstallFailureReason.DestinationUnavailable, result.FailureReason);
        Assert.True(Directory.Exists(InstallFolder));
        Assert.True(File.Exists(Path.Combine(InstallFolder, PluginInstallLayout.ReceiptFileName)));
        Assert.Equal(PluginId, Assert.Single(CreateService().GetInstallStates()).Key);
    }

    [Fact]
    public async Task Uninstall_DeletesOnlyTheRequestedPlugin()
    {
        CreateCatalogInstall();
        var other = Path.Combine(PluginRoot, ForeignPluginId);
        Directory.CreateDirectory(other);

        var result = await CreateService().UninstallAsync(PluginId);

        Assert.True(result.IsSuccess, result.Message);
        Assert.True(Directory.Exists(other));
        Assert.False(Directory.Exists(InstallFolder));
    }

    private void CreateCatalogInstall(string receiptPluginId = PluginId)
    {
        Directory.CreateDirectory(Path.Combine(InstallFolder, "lib", "net10.0"));
        File.WriteAllText(Path.Combine(InstallFolder, "Demo.dll"), "plugin");
        WriteReceipt(InstallFolder, receiptPluginId);
    }

    private void WriteReceipt(string installFolder, string receiptPluginId)
    {
        var receipt = new PluginInstallReceipt
        {
            PluginId = receiptPluginId,
            SourceUrl = "https://feed.test/v3/index.json",
            PackageId = PluginId,
            Version = "1.0.0",
            EntryAssemblyPath = "Demo.dll",
            EntryAssemblyName = "Demo",
            AssemblyVersion = "1.0.0.0",
            InstalledAt = DateTimeOffset.UnixEpoch
        };
        File.WriteAllText(Path.Combine(installFolder, PluginInstallLayout.ReceiptFileName),
            JsonSerializer.Serialize(receipt, JsonOptions));
    }

    private string WritePendingOperation(string version)
    {
        var stagedPath = Path.Combine(StagingRoot, Guid.NewGuid().ToString("N"), "prepared");
        Directory.CreateDirectory(stagedPath);
        var operation = new PluginInstallOperation
        {
            PluginId = PluginId,
            Status = PluginInstallOperationStatus.PendingRestart,
            SourceUrl = "https://feed.test/v3/index.json",
            PackageId = PluginId,
            Version = version,
            PreviousVersion = "1.0.0",
            StagedAt = DateTimeOffset.UnixEpoch,
            StagedPath = stagedPath,
            EntryAssemblyPath = "Demo.dll",
            ClaimedPluginIds = [PluginId]
        };

        var pendingPath = Path.Combine(StagingRoot, "pending", PluginId + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(pendingPath)!);
        File.WriteAllText(pendingPath, JsonSerializer.Serialize(operation, JsonOptions));
        return pendingPath;
    }
}
