using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.App.Plugins;
using SkyCD.Core.DependencyInjection;
using SkyCD.Core.Versioning;
using SkyCD.Plugin.Runtime.Installation;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Enabling or disabling a file-format plugin in Options must move the Open and Save As gates in the same
/// session, including a marketplace install staged while the dialog is open.
/// </summary>
public sealed class PluginFileFormatAvailabilityTests : IDisposable
{
    private const string PluginAssemblyFileName = "SkyCD.Plugin.FileFormat.Json.dll";

    private readonly string pluginFolder =
        Path.Combine(Path.GetTempPath(), "SkyCD-file-format-availability-" + Guid.NewGuid().ToString("N"));
    private readonly PluginManager pluginManager = ServiceProvider.Resolve<PluginManager>();

    public PluginFileFormatAvailabilityTests()
    {
        Directory.CreateDirectory(pluginFolder);
    }

    public void Dispose()
    {
        // The plugin manager is process-wide; leave it without enabled plugins for the other tests.
        pluginManager.SavePluginEnabledStates(pluginManager.AvailablePlugins
            .Select(static plugin => (plugin.Id, IsEnabled: false))
            .ToArray());
        // Discover an empty directory so the collectible context that mapped the copied plugin
        // is unloaded. Windows otherwise denies deletion of that dll.
        var empty = Path.Combine(Path.GetTempPath(), "SkyCD-file-format-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        pluginManager.Discover(empty, ServiceProvider.Resolve<HostVersionProvider>().Current);
        DeleteDirectory(pluginFolder);
        DeleteDirectory(empty);
    }

    [Fact]
    public async Task OptionsAccepted_SavesStatesRediscoversAndPushesAvailability()
    {
        CopyPluginAssembly(pluginFolder);
        var synchronizer = CreateSynchronizer();
        using var viewModel = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());

        var pluginId = DiscoverPluginId(synchronizer);

        // Options unchecked the plugins: the dialog saves the states, re-discovers and pushes.
        pluginManager.SavePluginEnabledStates([(pluginId, false)]);
        var manager = synchronizer.Synchronize(pluginFolder);
        PluginRuntimeSynchronizer.PushAvailability(viewModel, manager);

        Assert.False(viewModel.OpenCatalogCommand.CanExecute(null));
        Assert.False(viewModel.SaveCatalogAsCommand.CanExecute(null));
        Assert.False(FileItem(viewModel, "file_open").IsEnabled);
        Assert.False(FileItem(viewModel, "file_save_as").IsEnabled);

        // Options checked them again: the same path must re-enable both gates without a restart.
        await synchronizer.ApplyPendingInstallsAsync();
        pluginManager.SavePluginEnabledStates([(pluginId, true)]);
        manager = synchronizer.Synchronize(pluginFolder);
        PluginRuntimeSynchronizer.PushAvailability(viewModel, manager);

        Assert.True(viewModel.OpenCatalogCommand.CanExecute(null));
        Assert.True(viewModel.SaveCatalogAsCommand.CanExecute(null));
        Assert.True(FileItem(viewModel, "file_open").IsEnabled);
        Assert.True(FileItem(viewModel, "file_save_as").IsEnabled);
    }

    [Fact]
    public async Task OptionsAccepted_ActivatesMarketplaceInstallStagedInTheSameSession()
    {
        var installer = new StagedPluginInstaller(pluginFolder, Path.Combine(AppContext.BaseDirectory, PluginAssemblyFileName));
        var synchronizer = CreateSynchronizer(installer);
        using var viewModel = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());

        // Nothing readable or writable is installed yet.
        var manager = synchronizer.Synchronize(pluginFolder);
        PluginRuntimeSynchronizer.PushAvailability(viewModel, manager);
        Assert.False(viewModel.OpenCatalogCommand.CanExecute(null));
        Assert.False(viewModel.SaveCatalogAsCommand.CanExecute(null));

        // The marketplace staged a package while the dialog was open; accepting Options applies it.
        installer.Stage();
        await synchronizer.ApplyPendingInstallsAsync();
        manager = synchronizer.Synchronize(pluginFolder);
        PluginRuntimeSynchronizer.PushAvailability(viewModel, manager);

        Assert.True(viewModel.OpenCatalogCommand.CanExecute(null));
        Assert.True(viewModel.SaveCatalogAsCommand.CanExecute(null));
        Assert.True(FileItem(viewModel, "file_open").IsEnabled);
        Assert.True(FileItem(viewModel, "file_save_as").IsEnabled);
    }

    private PluginRuntimeSynchronizer CreateSynchronizer(IPluginPackageInstaller? installer = null)
    {
        return new PluginRuntimeSynchronizer(
            pluginManager,
            installer ?? new StagedPluginInstaller(pluginFolder, Path.Combine(AppContext.BaseDirectory, PluginAssemblyFileName)),
            ServiceProvider.Resolve<HostVersionProvider>());
    }

    private string DiscoverPluginId(PluginRuntimeSynchronizer synchronizer)
    {
        synchronizer.Synchronize(pluginFolder);
        return Assert.Single(pluginManager.Plugins).Id;
    }

    private static MainMenuItemViewModel FileItem(MainWindowViewModel viewModel, string key)
    {
        return Assert.Single(viewModel.FileMenuItems, item => item.Key == key);
    }

    private static void DeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 8 && Directory.Exists(path); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(50);
            }
        }
    }

    private static void CopyPluginAssembly(string targetDirectory)
    {
        var source = Path.Combine(AppContext.BaseDirectory, PluginAssemblyFileName);
        File.Copy(source, Path.Combine(targetDirectory, PluginAssemblyFileName), overwrite: true);
    }

    /// <summary>Copies a package into the plugin root only when pending operations are applied.</summary>
    private sealed class StagedPluginInstaller(string pluginFolder, string stagedAssemblyPath) : IPluginPackageInstaller
    {
        private bool isStaged;

        public void Stage() => isStaged = true;

        public Task<PluginInstallResult> StageInstallAsync(PluginPackageRequest request,
            CancellationToken cancellationToken = default)
        {
            isStaged = true;
            return Task.FromResult(PluginInstallResult.Success(new PluginInstallState
            {
                PluginId = request.NormalizedPackageId,
                Status = PluginInstallOperationStatus.PendingRestart,
                PendingVersion = request.NormalizedVersion
            }));
        }

        public Task<IReadOnlyList<PluginInstallResult>> ApplyPendingAsync(CancellationToken cancellationToken = default)
        {
            if (!isStaged)
            {
                return Task.FromResult<IReadOnlyList<PluginInstallResult>>([]);
            }

            isStaged = false;
            File.Copy(stagedAssemblyPath, Path.Combine(pluginFolder, Path.GetFileName(stagedAssemblyPath)),
                overwrite: true);
            return Task.FromResult<IReadOnlyList<PluginInstallResult>>(
                [PluginInstallResult.Success(new PluginInstallState
                {
                    PluginId = "SkyCD.Plugin.Json",
                    Status = PluginInstallOperationStatus.Installed
                })]);
        }

        public Task<PluginInstallResult> CancelPendingAsync(string pluginId,
            CancellationToken cancellationToken = default)
        {
            isStaged = false;
            return Task.FromResult(PluginInstallResult.Success(PluginInstallState.NotInstalled(pluginId)));
        }
    }
}
