using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SkyCD.App.Services;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Presentation.ViewModels;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.Runtime.DependencyInjection;
using SkyCD.Plugin.Runtime.DependencyInjection.Registrators;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.App.Views;
using SkyCD.Documents;
using PluginServiceProvider = SkyCD.Plugin.Runtime.DependencyInjection.ServiceProvider;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SkyCD.App;

public partial class App : Avalonia.Application
{
    private IServiceProvider? appServiceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            appServiceProvider = BuildAppServiceProvider();
            var localStore = appServiceProvider.GetRequiredService<CouchbaseLocalStore>();
            var mainWindowViewModel = appServiceProvider.GetRequiredService<MainWindowViewModel>();
            var repositoryManager = appServiceProvider.GetRequiredService<RepositoryManager>();
            var pluginServices = CreatePluginServices(localStore, repositoryManager);

            desktop.Exit += (_, _) =>
            {
                (appServiceProvider as IDisposable)?.Dispose();
                pluginServices.ServiceProvider.Dispose();
            };
            desktop.MainWindow = new MainWindow(
                localStore,
                pluginServices.PluginManager,
                pluginServices.FileFormatManager)
            {
                DataContext = mainWindowViewModel,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static PluginUiServices CreatePluginServices(CouchbaseLocalStore localStore, RepositoryManager repositoryManager)
    {
        IReadOnlyCollection<DiscoveredPlugin> discoveredPlugins = [];
        var options = localStore.GetRepository<AppOptionsDocument>()
            .GetOrCreate<AppOptionsDocument>(AppOptionsDocument.DocumentId);
        var pluginPath = string.IsNullOrWhiteSpace(options.PluginPath)
            ? ResolveDefaultPluginPath()
            : options.PluginPath;
        var pluginManager = new PluginManager(
            NullLogger<PluginManager>.Instance,
            new AssembliesListFactory(NullLogger<AssembliesListFactory>.Instance),
            new DiscoveredPluginFactory(),
            new PluginDocumentFactory(),
            repositoryManager);

        if (!string.IsNullOrWhiteSpace(pluginPath) && Directory.Exists(pluginPath))
        {
            pluginManager.Discover(pluginPath, new Version(3, 0, 0));

            discoveredPlugins = pluginManager.Plugins;
        }

        var pluginList = discoveredPlugins.ToList();
        var pluginById = pluginList.ToDictionary(static plugin => plugin.Id, StringComparer.OrdinalIgnoreCase);
        IServiceCollection mergedServices = new ServiceCollection()
            .AddRegistrator<CommonRuntimeServiceRegistrator>();

        mergedServices.AddSingleton<IReadOnlyList<DiscoveredPlugin>>(pluginList);
        mergedServices.AddSingleton<IReadOnlyCollection<DiscoveredPlugin>>(pluginList);
        mergedServices.AddSingleton<IReadOnlyDictionary<string, DiscoveredPlugin>>(pluginById);
        mergedServices.AddPluginRegistrator(discoveredPlugins);

        var runtimeProvider = PluginServiceProvider.Instance;
        runtimeProvider.Register(mergedServices);
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

    private sealed record PluginUiServices(
        FileFormatManager FileFormatManager,
        PluginManager PluginManager,
        PluginServiceProvider ServiceProvider);

    private static IServiceProvider BuildAppServiceProvider()
    {
        var services = new ServiceCollection();
        CouchbaseServiceRegistrator.RegisterServices(services);
        return services
            .AddSingleton<CouchbaseLocalStore>()
            .AddSingleton<IBrowserDataStore, CouchbaseLiteBrowserDataStore>()
            .AddSingleton<MainWindowViewModel>()
            .BuildServiceProvider();
    }
}
