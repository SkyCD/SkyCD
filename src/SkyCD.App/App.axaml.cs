using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DryIoc;
using SkyCD.App.Views;
using SkyCD.App.Marketplace;
using SkyCD.App.Mcp;
using SkyCD.App.PluginHost;
using SkyCD.Couchbase;
using SkyCD.Documents;
using SkyCD.Documents.Repository;
using SkyCD.Core.DependencyInjection;
using SkyCD.Plugin.Runtime.Installation;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.Abstractions;
using SkyCD.Plugin.Abstractions.Capabilities.Catalog;
using SkyCD.Plugin.Abstractions.Capabilities.Storage;
using SkyCD.Plugin.Abstractions.Capabilities.Ui;
using SkyCD.Core.DependencyInjection.Registrators;
using SkyCD.Core.Versioning;
using SkyCD.Presentation.Catalog;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.ViewModels;
using SkyCD.Plugin.Host.Indexing;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.Host.Volume;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Abstractions.Localization;
using SkyCD.UI.Localization;

namespace SkyCD.App;

public partial class App : Avalonia.Application
{
    private IContainer? appServiceProvider;
    private McpServerHost? mcpServerHost;

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
                lastOpenedCatalogPath = options.LastOpenedCatalogPath;
                LocalizationService.Current.SetLanguage(options.Language);
                InitializePlugins(appServiceProvider.Resolve<RepositoryManager>());
                InitializeDefaultStatuses(appServiceProvider.Resolve<RepositoryManager>());
                InitializeDefaultScanExclusionRules(appServiceProvider.Resolve<RepositoryManager>());
                InitializeDefaultAiScanExclusions(appServiceProvider.Resolve<RepositoryManager>());
                mcpServerHost = appServiceProvider.Resolve<McpServerHost>();
                ApplyMcpSettings();
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
                var mainWindow = appServiceProvider.Resolve<MainWindow>();
                mainWindow.DataContext = mainWindowViewModel;
                desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;

                desktop.Exit += (_, _) =>
                {
                    mcpServerHost?.Dispose();
                    appServiceProvider.Dispose();
                };
                desktop.MainWindow = mainWindow;
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
    // capability so one bad plugin cannot stop the others from receiving their APIs.
    private void InitializePluginConsumers(PluginManager pluginManager)
    {
        var catalogApi = appServiceProvider!.Resolve<CatalogApiAdapter>();
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
            registrator.Register<PluginTranslationRegistry>(Reuse.Singleton);
            registrator.Register<PluginMarketplaceService>(Reuse.Singleton);
            registrator.Register<OpenCatalogState>(Reuse.Singleton);
            registrator.Register<CatalogApiAdapter>(Reuse.Singleton);
            registrator.Register<PluginStorageApiFactory>(Reuse.Singleton);
            registrator.Register<MainWindowViewModel>(Reuse.Singleton);
            registrator.Register<MainWindow>(Reuse.Singleton);
        });
    }

    public void ApplyMcpSettings()
    {
        if (appServiceProvider is null || mcpServerHost is null)
        {
            return;
        }

        var repositoryManager = appServiceProvider.Resolve<RepositoryManager>();
        var appOptionsRepository = (AppOptionsDocumentRepository)repositoryManager.For<AppOptionsDocument>();
        var options = appOptionsRepository.GetOrCreateAppOptions();
        mcpServerHost.Configure(options.IsMcpServerEnabled, options.McpPort);
    }

    public (bool IsRunning, string? BaseUrl) GetMcpStatus()
    {
        if (mcpServerHost is null)
        {
            return (false, null);
        }

        return (mcpServerHost.IsRunning, mcpServerHost.BaseUrl);
    }
}
