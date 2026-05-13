using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DryIoc;
using SkyCD.Core.DependencyInjection.Registrators;
using SkyCD.Plugin.Abstractions.Capabilities.Menu;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Sample.Menu;
using Xunit;

namespace SkyCD.Plugin.Sample.Menu.Tests;

public sealed class SampleMenuPluginTests
{
    [Fact]
    public void PluginCapabilityServiceRegistrator_RegistersMenuCapability()
    {
        using var container = new DryIoc.Container();
        new CommonRuntimeServiceRegistrator().RegisterServices(container);

        var plugin = new DiscoveredPlugin
        {
            Id = "SkyCD.Plugin.Sample.Menu",
            Name = "Sample Menu Plugin",
            Version = new Version(1, 0, 0),
            MinHostVersion = new Version(3, 0, 0),
            FileName = "SkyCD.Plugin.Sample.Menu.dll",
            Capabilities = [new SampleMenuPlugin()]
        };

        plugin.RegisterPluginServices(container);

        var menuCapabilities = container.Resolve<IEnumerable<IMenuPluginCapability>>().ToList();

        Assert.Contains(menuCapabilities, c => c is SampleMenuPlugin);
    }

    [Fact]
    public void MenuExtensionManager_ResolvesContributionsFromContainer()
    {
        using var container = new DryIoc.Container();
        new CommonRuntimeServiceRegistrator().RegisterServices(container);

        var plugin = new DiscoveredPlugin
        {
            Id = "SkyCD.Plugin.Sample.Menu",
            Name = "Sample Menu Plugin",
            Version = new Version(1, 0, 0),
            MinHostVersion = new Version(3, 0, 0),
            FileName = "SkyCD.Plugin.Sample.Menu.dll",
            Capabilities = [new SampleMenuPlugin()]
        };

        plugin.RegisterPluginServices(container);

        container.Register<MenuExtensionManager>(Reuse.Singleton);
        var manager = container.Resolve<MenuExtensionManager>();

        var toolsContributions = manager.GetMenuContributions("Tools");
        var contribution = Assert.Single(toolsContributions);

        Assert.Equal("sample.menu.example", contribution.CommandId);
        Assert.Equal("Example", contribution.Title);
        Assert.Equal("Tools", contribution.Location);
    }

    [Fact]
    public async Task MenuExtensionManager_ExecuteAsync_UnknownCommand_ReturnsFailure()
    {
        using var container = new DryIoc.Container();
        new CommonRuntimeServiceRegistrator().RegisterServices(container);

        var plugin = new DiscoveredPlugin
        {
            Id = "SkyCD.Plugin.Sample.Menu",
            Name = "Sample Menu Plugin",
            Version = new Version(1, 0, 0),
            MinHostVersion = new Version(3, 0, 0),
            FileName = "SkyCD.Plugin.Sample.Menu.dll",
            Capabilities = [new SampleMenuPlugin()]
        };

        plugin.RegisterPluginServices(container);

        container.Register<MenuExtensionManager>(Reuse.Singleton);
        var manager = container.Resolve<MenuExtensionManager>();

        var result = await manager.ExecuteAsync(
            "unknown.command",
            new MenuCommandContext(),
            TimeSpan.FromSeconds(1));

        Assert.False(result.Success);
    }
}
