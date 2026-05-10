using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using DryIoc;
using Microsoft.Extensions.Logging;
using SkyCD.Couchbase;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.DependencyInjection;
using SkyCD.Plugin.Runtime.DependencyInjection.Registrators;
using SkyCD.Plugin.Runtime.Discovery;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests;

public sealed class ServiceRegistratorTests
{
    [Fact]
    public void PluginCapabilityServiceRegistrator_RegistersPluginCapabilities()
    {
        var plugin = new DiscoveredPlugin
        {
            Id = "tests.runtime.di",
            Name = "Runtime Di",
            Version = new Version(1, 2, 3),
            MinHostVersion = new Version(3, 0, 0),
            FileName = "tests.runtime.di.dll",
            Capabilities =
            [
                new StandaloneFileFormatCapability()
            ]
        };

        using var provider = new DryIoc.Container();
        new CommonRuntimeServiceRegistrator().RegisterServices(provider);
        var databasePath = Path.Combine(Path.GetTempPath(), "skycd-runtime-registrator-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(databasePath);
        provider.RegisterDelegate<DatabaseManager>(_ =>
        {
            var manager = new DatabaseManager();
            manager.Connect("default", databasePath);
            return manager;
        }, Reuse.Singleton);
        provider.Register<RepositoryManager>(Reuse.Singleton);

        var pluginById = new Dictionary<string, DiscoveredPlugin>(StringComparer.OrdinalIgnoreCase)
        {
            [plugin.Id] = plugin
        };

        provider.RegisterInstance<IReadOnlyList<DiscoveredPlugin>>([plugin]);
        provider.RegisterInstance<IReadOnlyCollection<DiscoveredPlugin>>([plugin]);
        provider.RegisterInstance<IReadOnlyDictionary<string, DiscoveredPlugin>>(pluginById);
        PluginServiceRegistrator.RegisterServices(provider, plugin);

        var discovered = provider.Resolve<IReadOnlyList<DiscoveredPlugin>>();
        var byId = provider.Resolve<IReadOnlyDictionary<string, DiscoveredPlugin>>();
        var formatCapabilities = provider.ResolveMany<IFileFormatPluginCapability>().ToList();
        var keyedFormatCapability =
            provider.Resolve<IFileFormatPluginCapability>(serviceKey: typeof(IFileFormatPluginCapability));

        Assert.Single(discovered);
        Assert.Same(plugin, discovered[0]);
        Assert.Same(plugin, byId["tests.runtime.di"]);
        Assert.Contains(formatCapabilities, capability => capability is StandaloneFileFormatCapability);
        Assert.IsType<StandaloneFileFormatCapability>(keyedFormatCapability);
    }

    [Fact]
    public void CommonRuntimeServiceRegistrator_RegistersLoggerFactory()
    {
        using var provider = new DryIoc.Container();
        new CommonRuntimeServiceRegistrator().RegisterServices(provider);
        Assert.NotNull(provider.Resolve<ILoggerFactory>());
    }
}