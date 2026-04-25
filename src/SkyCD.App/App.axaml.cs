using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SkyCD.App.Services;
using SkyCD.Presentation.ViewModels;
using SkyCD.Plugin.Host.Managers;
using SkyCD.Plugin.Runtime.DependencyInjection;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.App.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SkyCD.App;

public partial class App : Avalonia.Application
{
    private readonly SqliteBrowserDataStore browserDataStore = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var appOptionsStore = new AppOptionsStore();
            var pluginDiscoveryService = new RuntimePluginDiscoveryService();
            var pluginServices = CreatePluginServices(appOptionsStore);

            desktop.Exit += (_, _) =>
            {
                browserDataStore.Dispose();
                GlobalPluginServiceProvider.Reset();
            };
            desktop.MainWindow = new MainWindow(
                appOptionsStore,
                pluginDiscoveryService,
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

        if (!string.IsNullOrWhiteSpace(pluginPath) && Directory.Exists(pluginPath))
        {
            var pluginManager = new PluginManager();
            pluginManager.Discover(pluginPath, new Version(3, 0, 0));

            discoveredPlugins = pluginManager.Plugins;
        }

        var serviceProvider = new PluginServiceProviderFactory().Build(
            discoveredPlugins,
            services => services.AddSingleton<FileFormatManager>());
        GlobalPluginServiceProvider.Set(serviceProvider);
        var fileFormatManager = serviceProvider.GetRequiredService<FileFormatManager>();
        return new PluginUiServices(fileFormatManager);
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

    private sealed record PluginUiServices(FileFormatManager FileFormatManager);
}
