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
using PluginServiceProvider = SkyCD.Plugin.Runtime.DependencyInjection.ServiceProvider;

namespace SkyCD.App;

public partial class App : Avalonia.Application
{
    private PluginServiceProvider? appServiceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            appServiceProvider = BuildAppServiceProvider();
            var pluginServices = appServiceProvider.GetRequiredService<PluginUiServices>();
            var mainWindowViewModel = appServiceProvider.GetRequiredService<MainWindowViewModel>();
            var mainWindow = appServiceProvider.GetRequiredService<MainWindow>();
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

    private static PluginUiServices CreatePluginServices(DatabaseManager databaseManager, RepositoryManager repositoryManager)
    {
        IReadOnlyCollection<DiscoveredPlugin> discoveredPlugins = [];
        var options = repositoryManager.For<AppOptionsDocument>()
            .GetOrCreate<AppOptionsDocument>(AppOptionsDocument.DocumentId);
        var pluginPath = ResolvePluginPathOrDefault(options.PluginPath);
        Action<IContainer> registrations = registrator => registrator.AddRegistrator<CommonRuntimeServiceRegistrator>();

        var runtimeProvider = PluginServiceProvider.Instance;
        runtimeProvider.Register(registrations);
        var pluginManager = runtimeProvider.GetRequiredService<PluginManager>();

        if (!string.IsNullOrWhiteSpace(pluginPath) && Directory.Exists(pluginPath))
        {
            pluginManager.Discover(pluginPath, new Version(3, 0, 0));

            discoveredPlugins = pluginManager.Plugins;
        }

        var pluginList = discoveredPlugins.ToList();
        var pluginById = pluginList.ToDictionary(static plugin => plugin.Id, StringComparer.OrdinalIgnoreCase);

        runtimeProvider.Register(registrator =>
        {
            registrator.RegisterInstance<IReadOnlyList<DiscoveredPlugin>>(pluginList);
            registrator.RegisterInstance<IReadOnlyCollection<DiscoveredPlugin>>(pluginList);
            registrator.RegisterInstance<IReadOnlyDictionary<string, DiscoveredPlugin>>(pluginById);
            registrator.AddPluginRegistrator(discoveredPlugins);
        });
        var fileFormatManager = runtimeProvider.GetRequiredService<FileFormatManager>();
        return new PluginUiServices(fileFormatManager, pluginManager, runtimeProvider);
    }

    private static string ResolveDefaultPluginPath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "Plugins"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Plugins")),
            Path.Combine(Environment.CurrentDirectory, "Plugins", "samples"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Plugins", "samples"))
        };

        return candidates.FirstOrDefault(Directory.Exists) ?? string.Empty;
    }

    private static string ResolvePluginPathOrDefault(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && Directory.Exists(configuredPath))
        {
            return configuredPath;
        }

        return ResolveDefaultPluginPath();
    }

    private sealed record PluginUiServices(
        FileFormatManager FileFormatManager,
        PluginManager PluginManager,
        PluginServiceProvider ServiceProvider);

    private static PluginServiceProvider BuildAppServiceProvider()
    {
        return new PluginServiceProvider(registrator =>
        {
            CouchbaseServiceRegistrator.RegisterServices(registrator);
            registrator.RegisterDelegate(static resolver =>
            {
                var repositoryManager = resolver.Resolve<RepositoryManager>();
                return repositoryManager.For<CatalogDocument>() as CatalogDocumentRepository
                       ?? throw new CatalogRepositoryTypeMismatchException();
            }, Reuse.Singleton);
            registrator.RegisterDelegate(static resolver =>
            {
                var catalogRepository = resolver.Resolve<CatalogDocumentRepository>();
                return new MainWindowViewModel(catalogRepository);
            }, Reuse.Singleton);
            registrator.RegisterDelegate(static resolver =>
            {
                var databaseManager = resolver.Resolve<DatabaseManager>();
                var repositoryManager = resolver.Resolve<RepositoryManager>();
                return CreatePluginServices(databaseManager, repositoryManager);
            }, Reuse.Singleton);
            registrator.RegisterDelegate(static resolver => resolver.Resolve<PluginUiServices>().ServiceProvider, Reuse.Singleton);
            registrator.RegisterDelegate(static resolver => resolver.Resolve<PluginUiServices>().PluginManager, Reuse.Singleton);
            registrator.RegisterDelegate(static resolver => resolver.Resolve<PluginUiServices>().FileFormatManager, Reuse.Singleton);
            registrator.Register<MainWindow>(Reuse.Singleton);
        });
    }
}
