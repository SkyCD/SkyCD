using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DryIoc;
using SkyCD.Core.DependencyInjection.Registrators;
using SkyCD.Plugin.Abstractions.Capabilities.Menu;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.WebBrowser;
using Xunit;

namespace SkyCD.Plugin.WebBrowser.Tests;

public sealed class WebBrowserMenuPluginTests
{
    [Fact]
    public void PluginCapabilityServiceRegistrator_RegistersMenuCapability()
    {
        using var container = new DryIoc.Container();
        new CommonRuntimeServiceRegistrator().RegisterServices(container);

        var plugin = new DiscoveredPlugin
        {
            Id = "SkyCD.Plugin.WebBrowser",
            Name = "Web Browser Plugin",
            Version = new Version(1, 0, 0),
            MinHostVersion = new Version(3, 0, 0),
            FileName = "SkyCD.Plugin.WebBrowser.dll",
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
            Version = new Version(1, 0, 0),
            MinHostVersion = new Version(3, 0, 0),
            FileName = "SkyCD.Plugin.WebBrowser.dll",
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

    [Fact]
    public async Task ExecuteMenuCommandAsync_NoContextUrl_UsesAboutBlank()
    {
        Uri? launchedUrl = null;
        var plugin = new WebBrowserMenuPlugin((uri, _) =>
        {
            launchedUrl = uri;
            return Task.FromResult(true);
        });

        await plugin.ExecuteMenuCommandAsync("webbrowser.open", new MenuCommandContext());

        Assert.Equal(new Uri("about:blank"), launchedUrl);
    }

    [Fact]
    public async Task ExecuteMenuCommandAsync_InvalidUrl_NotifiesHostAndSkipsLaunch()
    {
        var hostApi = new RecordingHostCommandApi();
        var launchCalled = false;
        var plugin = new WebBrowserMenuPlugin((_, _) =>
        {
            launchCalled = true;
            return Task.FromResult(true);
        });

        await plugin.ExecuteMenuCommandAsync("webbrowser.open", new MenuCommandContext
        {
            Properties = new Dictionary<string, string> { ["url"] = "javascript:alert(1)" },
            HostApi = hostApi
        });

        Assert.False(launchCalled);
        Assert.Single(hostApi.Notifications);
        Assert.Contains("Unsupported URL scheme", hostApi.Notifications[0]);
    }

    [Fact]
    public async Task ExecuteMenuCommandAsync_ValidUrl_LaunchesResolvedUrl()
    {
        Uri? launchedUrl = null;
        var hostApi = new RecordingHostCommandApi();
        var plugin = new WebBrowserMenuPlugin((uri, _) =>
        {
            launchedUrl = uri;
            return Task.FromResult(true);
        });

        await plugin.ExecuteMenuCommandAsync("webbrowser.open", new MenuCommandContext
        {
            Properties = new Dictionary<string, string> { ["url"] = "https://skycd.example/" },
            HostApi = hostApi
        });

        Assert.Equal(new Uri("https://skycd.example/"), launchedUrl);
        Assert.Empty(hostApi.Notifications);
    }

    [Fact]
    public async Task ExecuteMenuCommandAsync_LaunchFailure_NotifiesHost()
    {
        var hostApi = new RecordingHostCommandApi();
        var plugin = new WebBrowserMenuPlugin((_, _) => Task.FromResult(false));

        await plugin.ExecuteMenuCommandAsync("webbrowser.open", new MenuCommandContext
        {
            Properties = new Dictionary<string, string> { ["url"] = "https://skycd.example/" },
            HostApi = hostApi
        });

        Assert.Single(hostApi.Notifications);
        Assert.Contains("Failed to open URL", hostApi.Notifications[0]);
    }

    private sealed class RecordingHostCommandApi : IHostCommandApi
    {
        public List<string> Notifications { get; } = [];

        public Task NavigateToNodeAsync(long nodeId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task NotifyAsync(string message, CancellationToken cancellationToken = default)
        {
            Notifications.Add(message);
            return Task.CompletedTask;
        }
    }
}
