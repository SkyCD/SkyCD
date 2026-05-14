using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DryIoc;
using SkyCD.App.Exceptions;
using SkyCD.App.Views;
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

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            appServiceProvider = CreateAppServiceProvider();
            InitializePlugins(appServiceProvider.Resolve<AppOptionsDocumentRepository>());
            var mainWindowViewModel = appServiceProvider.Resolve<MainWindowViewModel>();
            var mainWindow = appServiceProvider.Resolve<MainWindow>();
            mainWindow.DataContext = mainWindowViewModel;

            desktop.Exit += (_, _) => appServiceProvider.Dispose();
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void InitializePlugins(AppOptionsDocumentRepository appOptionsRepository)
    {
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

    private static IContainer CreateAppServiceProvider()
    {
        return ServiceProvider.RegisterChildContainer(static registrator =>
        {
            registrator.RegisterDelegate<AppOptionsDocumentRepository>(static resolver =>
            {
                var repositoryManager = resolver.Resolve<RepositoryManager>();
                return (AppOptionsDocumentRepository)repositoryManager.For<AppOptionsDocument>();
            }, Reuse.Singleton);
            registrator.RegisterDelegate(static resolver =>
            {
                var repositoryManager = resolver.Resolve<RepositoryManager>();
                return repositoryManager.For<CatalogDocument>() as CatalogDocumentRepository
                       ?? throw new CatalogRepositoryTypeMismatchException();
            }, Reuse.Singleton);
            registrator.RegisterDelegate(static resolver =>
            {
                var catalogRepository = resolver.Resolve<CatalogDocumentRepository>();
                var menuExtensionManager = ServiceProvider.ResolvePlugin<MenuExtensionManager>();
                return new MainWindowViewModel(catalogRepository, new PropertyValueLocalizer(), menuExtensionManager);
            }, Reuse.Singleton);
            registrator.RegisterDelegate(static _ => ServiceProvider.ResolvePlugin<FileFormatManager>(), Reuse.Singleton);
            registrator.Register<MainWindow>(Reuse.Singleton);
        });
    }
}

