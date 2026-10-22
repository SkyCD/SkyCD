using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DryIoc;
using SkyCD.App.Views;
using SkyCD.App.Mcp;
using SkyCD.Couchbase;
using SkyCD.Documents;
using SkyCD.Documents.Repository;
using SkyCD.Core.DependencyInjection;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Core.Versioning;
using SkyCD.Presentation.ViewModels;
using SkyCD.Plugin.Host.Menu;

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
            appServiceProvider = CreateAppServiceProvider();
            InitializePlugins(appServiceProvider.Resolve<RepositoryManager>());
            InitializeDefaultStatuses(appServiceProvider.Resolve<RepositoryManager>());
            mcpServerHost = appServiceProvider.Resolve<McpServerHost>();
            ApplyMcpSettings();
            var mainWindowViewModel = appServiceProvider.Resolve<MainWindowViewModel>();
            mainWindowViewModel.RefreshPluginMenuServices(ServiceProvider.Resolve<MenuExtensionManager>());
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

        base.OnFrameworkInitializationCompleted();
    }

    private static void InitializePlugins(RepositoryManager repositoryManager)
    {
        var appOptionsRepository = (AppOptionsDocumentRepository)repositoryManager.For<AppOptionsDocument>();
        var options = appOptionsRepository.GetOrCreateAppOptions();
        var pluginPath = options.PluginPath;
        var pluginManager = ServiceProvider.Resolve<PluginManager>();
        var hostVersionProvider = ServiceProvider.Resolve<HostVersionProvider>();

        if (!string.IsNullOrWhiteSpace(pluginPath) && Directory.Exists(pluginPath))
        {
            pluginManager.Discover(pluginPath, hostVersionProvider.Current);
        }

        ServiceProvider.ReregisterPluginsService();
    }

    private static void InitializeDefaultStatuses(RepositoryManager repositoryManager)
    {
        var statusRepository = (StatusVariantDocumentRepository)repositoryManager.For<StatusVariantDocument>();
        statusRepository.EnsureDefaultStatuses();
    }

    private static IContainer CreateAppServiceProvider()
    {
        return ServiceProvider.RegisterChildContainer(static registrator =>
        {
            registrator.Register<McpServerHost>(Reuse.Singleton);
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
