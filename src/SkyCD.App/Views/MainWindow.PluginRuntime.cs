using System.Linq;
using DryIoc;
using SkyCD.App.Plugins;
using SkyCD.Core.DependencyInjection;
using SkyCD.Plugin.Host.Catalog;
using SkyCD.Plugin.Host.Indexing;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Presentation.ViewModels;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.App.Views;

public partial class MainWindow
{
    private void SyncPluginRuntimeState()
    {
        var options = appOptionsRepository.GetOrCreateAppOptions();
        var resolvedPluginPath = PluginRuntimeSynchronizer.ResolveDiscoveryPath(options.PluginPath);
        options.PluginPath = resolvedPluginPath;
        SaveAppOptions(options);

        fileFormatManager = pluginRuntimeSynchronizer.Synchronize(resolvedPluginPath);
        var descriptionManager = ServiceProvider.Resolve<DescriptionExtensionManager>();
        descriptionManager.SelectImplementation(options.AiDescriptionImplementation);

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.RefreshPluginMenuServices(ServiceProvider.Resolve<MenuExtensionManager>());
            viewModel.RefreshCatalogFeatureServices(ServiceProvider.Resolve<CatalogFeatureExtensionManager>());
            viewModel.RefreshMediaServices(ServiceProvider.Resolve<MediaExtensionManager>(),
                ServiceProvider.Resolve<IMediaTargetMetadataProvider>(),
                ServiceProvider.Resolve<IndexWorkItemSequence>());
            viewModel.RefreshDescriptionServices(descriptionManager,
                ServiceProvider.Resolve<OnlineMetadataExtensionManager>());
        }

        PushFileFormatAvailability();
    }

    private void EnsureFileFormatProvidersLoaded()
    {
        if (fileFormatManager.GetReadableFormats().Count == 0)
        {
            SyncPluginRuntimeState();
            if (fileFormatManager.GetReadableFormats().Count == 0)
            {
                var availableDescriptors = pluginManager.GetAvailablePluginIds<IFileFormatPluginCapability>()
                    .Select(static id => (id, IsEnabled: true))
                    .ToArray();
                if (availableDescriptors.Length > 0)
                {
                    pluginManager.SavePluginEnabledStates(availableDescriptors);
                    SyncPluginRuntimeState();
                }
            }
        }

        // Pushed after the auto-enable fallback so installed-but-disabled plugins are reflected correctly.
        PushFileFormatAvailability();
    }

    private void PushFileFormatAvailability()
    {
        PluginRuntimeSynchronizer.PushAvailability(DataContext as MainWindowViewModel, fileFormatManager);
    }
}
