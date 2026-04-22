using Moq;
using SkyCD.Plugin.Abstractions.Capabilities.Menu;

namespace SkyCD.Plugin.WebBrowser.Tests;

public class WebBrowserPluginTests
{
    [Fact]
    public void GetMenuContributions_ReturnsWebBrowserCommand()
    {
        var plugin = new WebBrowserPlugin();

        var contribution = Assert.Single(plugin.GetMenuContributions());
        Assert.Equal("webbrowser.open", contribution.CommandId);
        Assert.Equal("Web Browser", contribution.Title);
        Assert.Equal("Tools", contribution.Location);
        Assert.Equal(100, contribution.Order);
    }

    [Fact]
    public async Task ExecuteMenuCommandAsync_UsesContextUrl_WhenProvided()
    {
        var browserLauncher = new Mock<IWebBrowserLauncher>();
        var plugin = new WebBrowserPlugin(browserLauncher.Object);
        var hostApi = new Mock<IHostCommandApi>();
        var context = new MenuCommandContext
        {
            Properties = new Dictionary<string, string> { ["url"] = "https://example.com" },
            HostApi = hostApi.Object
        };

        await plugin.ExecuteMenuCommandAsync("webbrowser.open", context);

        browserLauncher.Verify(
            launcher => launcher.OpenAsync("https://example.com", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteMenuCommandAsync_UsesDefaultUrl_WhenExplicitUrlMissing()
    {
        var browserLauncher = new Mock<IWebBrowserLauncher>();
        var plugin = new WebBrowserPlugin(browserLauncher.Object);
        var hostApi = new Mock<IHostCommandApi>();
        var context = new MenuCommandContext
        {
            Properties = new Dictionary<string, string> { ["defaultUrl"] = "https://docs.example.com" },
            HostApi = hostApi.Object
        };

        await plugin.ExecuteMenuCommandAsync("webbrowser.open", context);

        browserLauncher.Verify(
            launcher => launcher.OpenAsync("https://docs.example.com", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteMenuCommandAsync_FallsBackToAboutBlank_WhenNoUrlProperties()
    {
        var browserLauncher = new Mock<IWebBrowserLauncher>();
        var plugin = new WebBrowserPlugin(browserLauncher.Object);
        var hostApi = new Mock<IHostCommandApi>();
        var context = new MenuCommandContext
        {
            Properties = new Dictionary<string, string>(),
            HostApi = hostApi.Object
        };

        await plugin.ExecuteMenuCommandAsync("webbrowser.open", context);

        browserLauncher.Verify(
            launcher => launcher.OpenAsync("about:blank", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteMenuCommandAsync_DoesNothing_ForUnknownCommand()
    {
        var browserLauncher = new Mock<IWebBrowserLauncher>();
        var plugin = new WebBrowserPlugin(browserLauncher.Object);
        var hostApi = new Mock<IHostCommandApi>();
        var context = new MenuCommandContext
        {
            Properties = new Dictionary<string, string> { ["url"] = "https://example.com" },
            HostApi = hostApi.Object
        };

        await plugin.ExecuteMenuCommandAsync("other.command", context);

        browserLauncher.Verify(
            launcher => launcher.OpenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
