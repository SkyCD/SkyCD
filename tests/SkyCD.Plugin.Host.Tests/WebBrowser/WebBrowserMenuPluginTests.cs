using System;
using System.Collections.Generic;
using System.Linq;
using DryIoc;
using SkyCD.Core.DependencyInjection.Registrators;
using SkyCD.SDK.Capabilities.Menu;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Feature.WebBrowser;
using Xunit;

namespace SkyCD.Plugin.Host.Tests.WebBrowser;

public sealed class WebBrowserMenuPluginTests
{
    [Fact]
    public void PublishedAssembly_PreservesPluginIdentityAndIcon()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(WebBrowserMenuPlugin).Assembly);

        Assert.Equal("SkyCD.Plugin.WebBrowser", plugin.Id);
        Assert.Equal("SkyCD.Plugin.Feature.WebBrowser.dll", plugin.FileName);
        Assert.Equal(3, plugin.Version.Major);
        Assert.Equal(new Version(3, 0, 0), plugin.MinHostVersion);
        Assert.Equal(3, plugin.MaxHostVersion?.Major);
        Assert.NotEmpty(plugin.IconFrames);
        Assert.IsType<WebBrowserMenuPlugin>(Assert.Single(plugin.Capabilities));
    }

    [Fact]
    public void PluginCapabilityServiceRegistrator_RegistersMenuCapability()
    {
        using var container = new DryIoc.Container();
        new CommonRuntimeServiceRegistrator().RegisterServices(container);

        var plugin = new DiscoveredPlugin
        {
            Id = "SkyCD.Plugin.WebBrowser",
            Name = "Web Browser Plugin",
            Version = new Version(3, 0, 0),
            MinHostVersion = new Version(3, 0, 0),
            FileName = "SkyCD.Plugin.Feature.WebBrowser.dll",
            Capabilities = [new WebBrowserMenuPlugin()]
        };

        plugin.RegisterPluginServices(container);

        var menuCapabilities = container.Resolve<IEnumerable<IMenuPluginCapability>>().ToList();
        Assert.Contains(menuCapabilities, capability => capability is WebBrowserMenuPlugin);
    }

    [Fact]
    public void MenuExtensionManager_ResolvesToolsContribution()
    {
        using var container = new DryIoc.Container();
        new CommonRuntimeServiceRegistrator().RegisterServices(container);

        var plugin = new DiscoveredPlugin
        {
            Id = "SkyCD.Plugin.WebBrowser",
            Name = "Web Browser Plugin",
            Version = new Version(3, 0, 0),
            MinHostVersion = new Version(3, 0, 0),
            FileName = "SkyCD.Plugin.Feature.WebBrowser.dll",
            Capabilities = [new WebBrowserMenuPlugin()]
        };

        plugin.RegisterPluginServices(container);
        container.Register<MenuExtensionManager>(Reuse.Singleton);
        var manager = container.Resolve<MenuExtensionManager>();

        var contribution = Assert.Single(manager.GetMenuContributions("Tools"));
        Assert.Equal("webbrowser.open", contribution.CommandId);
        Assert.Equal("Web Browser", contribution.Title);
        Assert.Equal("Tools", contribution.Location);
    }
}
