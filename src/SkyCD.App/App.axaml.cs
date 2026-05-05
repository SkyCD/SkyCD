using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SkyCD.App.Services;
using SkyCD.App.Views;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Plugin.Runtime.DependencyInjection;
using SkyCD.Plugin.Runtime.DependencyInjection.Registrators;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Presentation.ViewModels;
using PluginServiceProvider = SkyCD.Plugin.Runtime.DependencyInjection.ServiceProvider;

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
            var pluginServices = appServiceProvider.GetRequiredService<PluginUiServices>();
            var mainWindowViewModel = appServiceProvider.GetRequiredService<MainWindowViewModel>();
            var mainWindow = appServiceProvider.GetRequiredService<MainWindow>();
            mainWindow.DataContext = mainWindowViewModel;

            desktop.Exit += (_, _) =>
            {
                (appServiceProvider as IDisposable)?.Dispose();
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
        var pluginPath = string.IsNullOrWhiteSpace(options.PluginPath)
            ? ResolveDefaultPluginPath()
            : options.PluginPath;
        IServiceCollection mergedServices = new ServiceCollection()
            .AddSingleton(repositoryManager)
            .AddRegistrator<CommonRuntimeServiceRegistrator>();

        var runtimeProvider = PluginServiceProvider.Instance;
        runtimeProvider.Register(mergedServices);
        var pluginManager = runtimeProvider.GetRequiredService<PluginManager>();

        if (!string.IsNullOrWhiteSpace(pluginPath) && Directory.Exists(pluginPath))
        {
            pluginManager.Discover(pluginPath, new Version(3, 0, 0));

            discoveredPlugins = pluginManager.Plugins;
        }

        var pluginList = discoveredPlugins.ToList();
        var pluginById = pluginList.ToDictionary(static plugin => plugin.Id, StringComparer.OrdinalIgnoreCase);

        mergedServices.AddSingleton<IReadOnlyList<DiscoveredPlugin>>(pluginList);
        mergedServices.AddSingleton<IReadOnlyCollection<DiscoveredPlugin>>(pluginList);
        mergedServices.AddSingleton<IReadOnlyDictionary<string, DiscoveredPlugin>>(pluginById);
        mergedServices.AddPluginRegistrator(discoveredPlugins);

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
        services
            .AddSingleton<IBrowserDataStore, CouchbaseLiteBrowserDataStore>()
            .AddSingleton<MainWindowViewModel>()
            .AddSingleton(static provider =>
            {
                var databaseManager = provider.GetRequiredService<DatabaseManager>();
                var repositoryManager = provider.GetRequiredService<RepositoryManager>();
                return CreatePluginServices(databaseManager, repositoryManager);
            })
            .AddSingleton(static provider => provider.GetRequiredService<PluginUiServices>().PluginManager)
            .AddSingleton(static provider => provider.GetRequiredService<PluginUiServices>().FileFormatManager)
            .AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
