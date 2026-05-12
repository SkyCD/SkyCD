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
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Runtime.DependencyInjection;
using SkyCD.Plugin.Runtime.DependencyInjection.Registrators;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Presentation.ViewModels;

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
            appServiceProvider = BuildAppServiceProvider();
            var pluginServices = appServiceProvider.Resolve<PluginUiServices>();
            var mainWindowViewModel = appServiceProvider.Resolve<MainWindowViewModel>();
            var mainWindow = appServiceProvider.Resolve<MainWindow>();
            mainWindow.DataContext = mainWindowViewModel;

            desktop.Exit += (_, _) =>
            {
                appServiceProvider.Dispose();
                pluginServices.ServiceProvider.Dispose();
            };
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static PluginUiServices CreatePluginServices(AppOptionsDocumentRepository appOptionsRepository)
    {
        IReadOnlyCollection<DiscoveredPlugin> discoveredPlugins = [];
        var options = appOptionsRepository.GetOrCreateAppOptions();
        var pluginPath = options.PluginPath;
        var pluginManager = ServiceProvider.Resolve<PluginManager>();

        if (!string.IsNullOrWhiteSpace(pluginPath) && Directory.Exists(pluginPath))
        {
            pluginManager.Discover(pluginPath, new Version(3, 0, 0));

            discoveredPlugins = pluginManager.Plugins;
        }

        var pluginList = discoveredPlugins.ToList();
        var pluginServiceProvider = PluginServiceRegistrator.CreatePluginSubcontainer(pluginList);
        var fileFormatManager = pluginServiceProvider.Resolve<FileFormatManager>();
        return new PluginUiServices(fileFormatManager, pluginManager, pluginServiceProvider);
    }

    private sealed record PluginUiServices(
        FileFormatManager FileFormatManager,
        PluginManager PluginManager,
        DryIoc.IContainer ServiceProvider);

    private static IContainer BuildAppServiceProvider()
    {
        var container = new DryIoc.Container();
        new CouchbaseServiceRegistrator().RegisterServices(container);
        container.RegisterDelegate<AppOptionsDocumentRepository>(static resolver =>
        {
            var repositoryManager = resolver.Resolve<RepositoryManager>();
            return (AppOptionsDocumentRepository)repositoryManager.For<AppOptionsDocument>();
        }, Reuse.Singleton);
        container.RegisterDelegate(static resolver =>
        {
            var repositoryManager = resolver.Resolve<RepositoryManager>();
            return repositoryManager.For<CatalogDocument>() as CatalogDocumentRepository
                   ?? throw new CatalogRepositoryTypeMismatchException();
        }, Reuse.Singleton);
        container.RegisterDelegate(static resolver =>
        {
            var catalogRepository = resolver.Resolve<CatalogDocumentRepository>();
            return new MainWindowViewModel(catalogRepository);
        }, Reuse.Singleton);
        container.RegisterDelegate(static resolver =>
        {
            var appOptionsRepository = resolver.Resolve<AppOptionsDocumentRepository>();
            return CreatePluginServices(appOptionsRepository);
        }, Reuse.Singleton);
        container.RegisterDelegate(static resolver => resolver.Resolve<PluginUiServices>().ServiceProvider,
            Reuse.Singleton);
        container.RegisterDelegate(static resolver => resolver.Resolve<PluginUiServices>().PluginManager,
            Reuse.Singleton);
        container.RegisterDelegate(static resolver => resolver.Resolve<PluginUiServices>().FileFormatManager,
            Reuse.Singleton);
        container.RegisterDelegate(static resolver =>
                new MainWindow(
                    resolver.Resolve<AppOptionsDocumentRepository>(),
                    resolver.Resolve<PluginManager>(),
                    resolver.Resolve<DryIoc.IContainer>(),
                    resolver.Resolve<FileFormatManager>()),
            Reuse.Singleton);

        return container;
    }
}
