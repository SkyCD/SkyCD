using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SkyCD.App.Services;
using SkyCD.Presentation.ViewModels;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.Runtime.DependencyInjection;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.App.Views;
using PluginServiceProvider = SkyCD.Plugin.Runtime.DependencyInjection.ServiceProvider;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SkyCD.App;

public partial class App : Avalonia.Application
{
    private readonly CouchbaseLocalStore localStore = new();
    private readonly CouchbaseLiteBrowserDataStore browserDataStore;

    public App()
    {
        browserDataStore = new CouchbaseLiteBrowserDataStore(localStore);
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var appOptionsStore = new AppOptionsStore(localStore);
            var pluginServices = CreatePluginServices(appOptionsStore);

            desktop.Exit += (_, _) =>
            {
                appOptionsStore.Dispose();
                localStore.Dispose();
                pluginServices.ServiceProvider.Dispose();
            };
            desktop.MainWindow = new MainWindow(
                appOptionsStore,
                pluginServices.PluginManager,
                pluginServices.FileFormatManager)
            {
                DataContext = new MainWindowViewModel(browserDataStore),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static PluginUiServices CreatePluginServices(AppOptionsStore appOptionsStore)
    {
        IReadOnlyCollection<DiscoveredPlugin> discoveredPlugins = [];
        var options = appOptionsStore.Load();
        var pluginPath = string.IsNullOrWhiteSpace(options.PluginPath)
            ? ResolveDefaultPluginPath()
            : options.PluginPath;
        var pluginManager = new PluginManager(
            NullLogger<PluginManager>.Instance,
            new AssembliesListFactory(NullLogger.Instance),
            new DiscoveredPluginFactory());

        if (!string.IsNullOrWhiteSpace(pluginPath) && Directory.Exists(pluginPath))
        {
            pluginManager.Discover(pluginPath, new Version(3, 0, 0));

            discoveredPlugins = pluginManager.Plugins;
        }

        var pluginList = discoveredPlugins.ToList();
        var pluginById = pluginList.ToDictionary(static plugin => plugin.Id, StringComparer.OrdinalIgnoreCase);
        var serviceCollectionFactory = new ServiceCollectionFactory();
        IServiceCollection mergedServices = serviceCollectionFactory.BuildCommonServiceCollection();

        mergedServices.AddSingleton<IReadOnlyList<DiscoveredPlugin>>(pluginList);
        mergedServices.AddSingleton<IReadOnlyCollection<DiscoveredPlugin>>(pluginList);
        mergedServices.AddSingleton<IReadOnlyDictionary<string, DiscoveredPlugin>>(pluginById);

        foreach (var plugin in pluginList)
        {
            var pluginDescriptors = serviceCollectionFactory.BuildPluginServiceCollection(plugin);
            foreach (var descriptor in pluginDescriptors)
            {
                mergedServices.Add(descriptor);
            }
        }

        PluginServiceProvider.Instance.Import(mergedServices);
        var fileFormatManager = PluginServiceProvider.Instance.GetRequiredService<FileFormatManager>();
        return new PluginUiServices(fileFormatManager, pluginManager, PluginServiceProvider.Instance);
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
}
