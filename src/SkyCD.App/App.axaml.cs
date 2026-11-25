using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DryIoc;
using Microsoft.Extensions.Logging;
using SkyCD.App.Views;
using SkyCD.App.Marketplace;
using SkyCD.App.Mcp;
using SkyCD.App.PluginHost;
using SkyCD.App.Plugins;
using SkyCD.Couchbase;
using SkyCD.Documents;
using SkyCD.Documents.Repository;
using SkyCD.Core.BackgroundServices;
using SkyCD.Core.DependencyInjection;
using SkyCD.Plugin.Runtime.Installation;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.Runtime.Marketplace.Services;
using SkyCD.SDK;
using SkyCD.SDK.Capabilities.Catalog;
using SkyCD.SDK.Capabilities.Storage;
using SkyCD.SDK.Capabilities.Ui;
using SkyCD.Core.DependencyInjection.Registrators;
using SkyCD.Core.Versioning;
using SkyCD.Presentation.Catalog;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.ViewModels;
using SkyCD.Plugin.Host.Catalog;
using SkyCD.Plugin.Host.Indexing;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.Host.Volume;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.SDK.Localization;
using SkyCD.UI.Localization;

namespace SkyCD.App;

public partial class App : Avalonia.Application
{
    private IContainer? appServiceProvider;
    private McpServerHost? mcpServerHost;
    private AppBackgroundServiceHost? backgroundServiceHost;
    private IClassicDesktopStyleApplicationLifetime? desktopLifetime;
    private Task backgroundServicesStarted = Task.CompletedTask;
    private int shutdownDrainState;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string? lastOpenedCatalogPath = null;
            try
            {
                appServiceProvider = CreateAppServiceProvider();
                var optionsRepository = (AppOptionsDocumentRepository)appServiceProvider.Resolve<RepositoryManager>().For<AppOptionsDocument>();
                var options = optionsRepository.GetOrCreateAppOptions();
                ApplyPluginRepositorySources(appServiceProvider.Resolve<PluginCatalog>(),
                    options.PluginRepositorySources);
                lastOpenedCatalogPath = options.LastOpenedCatalogPath;
                LocalizationService.Current.SetLanguage(options.Language);
                InitializePlugins(appServiceProvider.Resolve<RepositoryManager>());
                InitializeDefaultStatuses(appServiceProvider.Resolve<RepositoryManager>());
                InitializeDefaultScanExclusionRules(appServiceProvider.Resolve<RepositoryManager>());
                InitializeDefaultAiScanExclusions(appServiceProvider.Resolve<RepositoryManager>());
                mcpServerHost = appServiceProvider.Resolve<McpServerHost>();
                mcpServerHost.UpdateConfiguration(options.IsMcpServerEnabled, options.McpPort, options.ExposedMcpTools);
                backgroundServiceHost = appServiceProvider.Resolve<AppBackgroundServiceHost>();
                var mainWindowViewModel = appServiceProvider.Resolve<MainWindowViewModel>();
                mainWindowViewModel.AttachOpenCatalogState(appServiceProvider.Resolve<OpenCatalogState>());
                var descriptionManager = ServiceProvider.Resolve<DescriptionExtensionManager>();
                descriptionManager.SelectImplementation(options.AiDescriptionImplementation);
                mainWindowViewModel.RefreshPluginMenuServices(ServiceProvider.Resolve<MenuExtensionManager>());
                mainWindowViewModel.RefreshMediaServices(ServiceProvider.Resolve<MediaExtensionManager>(),
                    ServiceProvider.Resolve<VolumeExtensionManager>(),
                    ServiceProvider.Resolve<IndexWorkItemSequence>(),
                    ServiceProvider.Resolve<IAudioCdMetadataProvider>());
                mainWindowViewModel.RefreshDescriptionServices(descriptionManager);
                mainWindowViewModel.RefreshCatalogFeatureServices(ServiceProvider.Resolve<CatalogFeatureExtensionManager>());
                var mainWindow = appServiceProvider.Resolve<MainWindow>();
                mainWindow.DataContext = mainWindowViewModel;
                desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;

                desktopLifetime = desktop;
                desktop.ShutdownRequested += OnShutdownRequested;
                desktop.Exit += (_, _) =>
                {
                    // Last-resort cleanup for shutdown paths that never raised ShutdownRequested.
                    mcpServerHost?.Dispose();
                    appServiceProvider.Dispose();
                };
                desktop.MainWindow = mainWindow;
                backgroundServicesStarted = StartBackgroundServicesAsync(backgroundServiceHost,
                    appServiceProvider.Resolve<ILogger<AppBackgroundServiceHost>>());
            }
            catch (Exception exception) when (DatabaseRecoveryWindow.IsDatabaseFailure(exception))
            {
                try { appServiceProvider?.Resolve<DatabaseManager>().GetDatabase().Dispose(); }
                catch (Exception) { /* The database may never have opened. */ }
                desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
                desktop.MainWindow = new DatabaseRecoveryWindow(exception, lastOpenedCatalogPath);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task StartBackgroundServicesAsync(
        AppBackgroundServiceHost host, ILogger<AppBackgroundServiceHost> logger)
    {
        try
        {
            await host.StartAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to start background services.");
        }
    }

    private async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (Volatile.Read(ref shutdownDrainState) == 2)
        {
            return;
        }

        e.Cancel = true;
        if (Interlocked.CompareExchange(ref shutdownDrainState, 1, 0) != 0)
        {
            return;
        }

        if (backgroundServiceHost is { } host)
        {
            try
            {
                await host.StopAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                // The host already logs per-service failures; never block exit on drain errors.
                appServiceProvider?.Resolve<ILoggerFactory>().CreateLogger(typeof(App).FullName!)
                    .LogError(exception, "Failed to drain background services.");
            }
        }

        Volatile.Write(ref shutdownDrainState, 2);
        desktopLifetime?.Shutdown();
    }

    private static void ApplyPluginRepositorySources(PluginCatalog catalog, IReadOnlyList<string>? sources)
    {
        catalog.SetRepositorySources((sources ?? [])
            .Select(static url => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null)
            .OfType<Uri>());
    }

    private void InitializePlugins(RepositoryManager repositoryManager)
    {
        var appOptionsRepository = (AppOptionsDocumentRepository)repositoryManager.For<AppOptionsDocument>();
        var options = appOptionsRepository.GetOrCreateAppOptions();
        var pluginPath = options.PluginPath;
        var pluginManager = ServiceProvider.Resolve<PluginManager>();
        var translations = appServiceProvider!.Resolve<PluginTranslationRegistry>();
        pluginManager.PluginsChanged += (_, _) => translations.Refresh(pluginManager.Plugins);
        var hostVersionProvider = ServiceProvider.Resolve<HostVersionProvider>();

        // Staged catalog installs and updates become visible to assembly discovery only here, before the scan.
        PluginServiceRegistrator.ApplyPendingOperationsBeforeDiscoveryAsync(
                ServiceProvider.Resolve<IPluginPackageInstaller>())
            .GetAwaiter()
            .GetResult();

        if (!string.IsNullOrWhiteSpace(pluginPath) && Directory.Exists(pluginPath))
        {
            pluginManager.Discover(pluginPath, hostVersionProvider.Current);
        }

        ServiceProvider.ReregisterPluginsService();
        InitializePluginConsumers(pluginManager);
    }

    // Hands each plugin capability the host-owned APIs before its services are used. Failures are isolated per
    // capability so one bad plugin cannot stop the others from receiving their APIs. Re-discovery creates new
    // capability instances, so hosts must call this again after every plugin runtime sync.
    internal void InitializePluginConsumers(PluginManager pluginManager)
    {
        var catalogApi = appServiceProvider!.Resolve<CatalogApiAdapter>();
        var locationApi = appServiceProvider.Resolve<LocationApiAdapter>();
        var catalogProvider = (ICatalogRepositoryProvider)appServiceProvider.Resolve<OpenCatalogState>();
        var storageFactory = appServiceProvider.Resolve<PluginStorageApiFactory>();
        var dialogApi = new AvaloniaHostDialogApi(() =>
            (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow);

        foreach (var plugin in pluginManager.Plugins)
        {
            foreach (var consumer in plugin.Capabilities.OfType<ICatalogApiConsumer>())
            {
                try { consumer.InitializeCatalogApi(catalogApi); }
                catch (Exception exception) { PluginFailure.Publish(consumer.GetType(), "catalog", exception); }
            }

            foreach (var consumer in plugin.Capabilities.OfType<ICatalogRepositoryConsumer>())
            {
                try { consumer.InitializeCatalogRepository(catalogProvider); }
                catch (Exception exception) { PluginFailure.Publish(consumer.GetType(), "catalog", exception); }
            }

            foreach (var consumer in plugin.Capabilities.OfType<ILocationApiConsumer>())
            {
                try { consumer.InitializeLocationApi(locationApi); }
                catch (Exception exception) { PluginFailure.Publish(consumer.GetType(), "catalog", exception); }
            }

            foreach (var consumer in plugin.Capabilities.OfType<IPluginStorageConsumer>())
            {
                try { consumer.InitializeStorage(storageFactory.ForPlugin(plugin.Id)); }
                catch (Exception exception) { PluginFailure.Publish(consumer.GetType(), "catalog", exception); }
            }

            foreach (var consumer in plugin.Capabilities.OfType<IHostDialogConsumer>())
            {
                try { consumer.InitializeDialogApi(dialogApi); }
                catch (Exception exception) { PluginFailure.Publish(consumer.GetType(), "modal", exception); }
            }
        }
    }

    private static void InitializeDefaultStatuses(RepositoryManager repositoryManager)
    {
        var statusRepository = (StatusVariantDocumentRepository)repositoryManager.For<StatusVariantDocument>();
        statusRepository.EnsureDefaultStatuses();
    }

    private static void InitializeDefaultScanExclusionRules(RepositoryManager repositoryManager)
    {
        var scanRuleRepository =
            (ScanExclusionRuleDocumentRepository)repositoryManager.For<ScanExclusionRuleDocument>();
        scanRuleRepository.EnsureDefaultRules();
    }

    private static void InitializeDefaultAiScanExclusions(RepositoryManager repositoryManager)
    {
        var aiScanExclusionRepository =
            (AiScanExclusionDocumentRepository)repositoryManager.For<AiScanExclusionDocument>();
        aiScanExclusionRepository.EnsureDefaultEntries();
    }

    private static IContainer CreateAppServiceProvider()
    {
        ServiceProvider.RegisterInstance<ILocalizationService>(LocalizationService.Current);
        return ServiceProvider.RegisterChildContainer(static registrator =>
        {
            registrator.Register<IMediaDriveProvider, SystemMediaDriveProvider>(Reuse.Singleton);
            registrator.Register<McpServerHost>(Reuse.Singleton);
            registrator.RegisterDelegate<AppBackgroundServiceHost>(static resolver =>
                new AppBackgroundServiceHost(
                    [resolver.Resolve<McpServerHost>()],
                    resolver.Resolve<ILogger<AppBackgroundServiceHost>>()), Reuse.Singleton);
            registrator.Register<PluginTranslationRegistry>(Reuse.Singleton);
            registrator.Register<PluginMarketplaceService>(Reuse.Singleton);
            registrator.Register<OpenCatalogState>(Reuse.Singleton);
            registrator.Register<CatalogApiAdapter>(Reuse.Singleton);
            registrator.Register<LocationApiAdapter>(Reuse.Singleton);
            registrator.Register<PluginStorageApiFactory>(Reuse.Singleton);
            registrator.Register<PluginRuntimeSynchronizer>(Reuse.Singleton);
            registrator.Register<MainWindowViewModel>(Reuse.Singleton);
            registrator.Register<MainWindow>(Reuse.Singleton);
        });
    }

    /// <summary>
    /// Completes when the initial background-service start pass has finished.
    /// </summary>
    public Task BackgroundServicesStarted => backgroundServicesStarted;

    public async Task ApplyMcpSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (appServiceProvider is null || mcpServerHost is null)
        {
            return;
        }

        var repositoryManager = appServiceProvider.Resolve<RepositoryManager>();
        var appOptionsRepository = (AppOptionsDocumentRepository)repositoryManager.For<AppOptionsDocument>();
        var options = appOptionsRepository.GetOrCreateAppOptions();
        mcpServerHost.UpdateConfiguration(options.IsMcpServerEnabled, options.McpPort, options.ExposedMcpTools);
        await mcpServerHost.ApplyConfigurationAsync(cancellationToken);
    }

    public (bool IsRunning, string? BaseUrl) GetMcpStatus()
    {
        if (mcpServerHost is null)
        {
            return (false, null);
        }

        return (mcpServerHost.IsRunning, mcpServerHost.BaseUrl);
    }

    /// <summary>Discovers the MCP tools the Options page lists; returns none before the host is resolved.</summary>
    public Task<IReadOnlyList<SkyCD.Cli.Mcp.CliMcpToolDescriptor>> GetMcpToolsAsync(
        CancellationToken cancellationToken = default)
    {
        return mcpServerHost is null
            ? Task.FromResult<IReadOnlyList<SkyCD.Cli.Mcp.CliMcpToolDescriptor>>([])
            : mcpServerHost.ListToolsAsync(cancellationToken);
    }
}
