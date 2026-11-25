using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using SkyCD.Core.DependencyInjection;
using SkyCD.Core.DependencyInjection.Registrators;
using SkyCD.Core.Versioning;
using SkyCD.Plugin.Runtime.Configuration;
using SkyCD.Plugin.Runtime.Installation;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Plugins;

/// <summary>
/// Applies staged installs, re-discovers the plugin folder and returns the file-format facade of the
/// resulting plugin runtime, so Open and Save As gates follow the plugins that are enabled right now.
/// </summary>
/// <remarks>
/// Capability managers live in the plugin runtime container, which is rebuilt on every synchronisation.
/// Hosts must use the facade returned here instead of a constructor-injected instance, because the host
/// container keeps an empty one that never sees plugin capabilities.
/// </remarks>
public sealed class PluginRuntimeSynchronizer(
    PluginManager pluginManager,
    IPluginPackageInstaller installer,
    HostVersionProvider hostVersionProvider)
{
    /// <summary>
    /// Activates marketplace installs staged during this session before the next discovery, because a
    /// staged package is invisible to assembly discovery until it is applied.
    /// </summary>
    public Task ApplyPendingInstallsAsync(CancellationToken cancellationToken = default)
    {
        return PluginServiceRegistrator.ApplyPendingOperationsBeforeDiscoveryAsync(installer, cancellationToken);
    }

    /// <summary>Re-discovers plugins from the configured folder and rebuilds the plugin runtime services.</summary>
    public FileFormatManager Synchronize(string? configuredPluginPath)
    {
        pluginManager.Discover(ResolveDiscoveryPath(configuredPluginPath), hostVersionProvider.Current);

        ReinjectHostApis();
        return ServiceProvider.Resolve<FileFormatManager>();
    }

    /// <summary>
    /// Re-registers the plugin runtime and hands the fresh capability instances their host APIs again.
    /// Re-discovery builds new instances, so every path that calls <c>Discover</c> — synchronisation and
    /// the Options plugin list alike — must follow it with this before the capabilities are used.
    /// </summary>
    public void ReinjectHostApis()
    {
        ServiceProvider.ReregisterPluginsService();
        // Without re-injection the fresh instances would resolve from the new container without the host
        // APIs (catalog repository, dialogs, storage) they depend on.
        (Application.Current as App)?.InitializePluginConsumers(pluginManager);
        ServiceProvider.Resolve<PluginConfigurationManager>().RefreshConsumers();
    }

    /// <summary>Pushes readable and writable format availability of <paramref name="manager"/> to the menus.</summary>
    public static void PushAvailability(MainWindowViewModel? viewModel, FileFormatManager manager)
    {
        viewModel?.SetFileFormatAvailability(
            manager.GetOpenFormats().Count > 0,
            manager.GetSaveFormats().Count > 0);
    }

    /// <summary>Configured plugin folder when it exists, otherwise the first host default that does.</summary>
    public static string ResolveDiscoveryPath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && Directory.Exists(configuredPath))
        {
            return configuredPath;
        }

        var baseDirPlugins = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Plugins"));
        if (Directory.Exists(baseDirPlugins))
        {
            return baseDirPlugins;
        }

        var current = Directory.GetCurrentDirectory();
        var repoPlugins = Path.GetFullPath(Path.Combine(current, "Plugins"));
        if (Directory.Exists(repoPlugins))
        {
            return repoPlugins;
        }

        return configuredPath ?? string.Empty;
    }
}
