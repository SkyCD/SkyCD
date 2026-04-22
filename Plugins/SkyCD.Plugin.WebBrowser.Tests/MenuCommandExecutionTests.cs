using Moq;
using SkyCD.Plugin.Abstractions.Capabilities.Menu;

namespace SkyCD.Plugin.WebBrowser.Tests;

public class MenuCommandExecutionTests
{
    [Fact]
    public async Task ExecuteMenuCommandAsync_NotifiesOnInvalidUrlScheme()
    {
        var plugin = new WebBrowserPlugin(new Mock<IWebBrowserLauncher>().Object);
        var hostApi = new Mock<IHostCommandApi>();
        var context = new MenuCommandContext
        {
            Properties = new Dictionary<string, string> { ["url"] = "ftp://invalid.example.com" },
            HostApi = hostApi.Object
        };

        await plugin.ExecuteMenuCommandAsync("webbrowser.open", context);

        hostApi.Verify(
            api => api.NotifyAsync(
                "Invalid URL scheme. Allowed: http, https, about",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteMenuCommandAsync_Throws_WhenHostApiMissing()
    {
        var plugin = new WebBrowserPlugin(new Mock<IWebBrowserLauncher>().Object);
        var context = new MenuCommandContext
        {
            Properties = new Dictionary<string, string> { ["url"] = "https://example.com" },
            HostApi = null
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => plugin.ExecuteMenuCommandAsync("webbrowser.open", context));
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com")]
    [InlineData("about:blank")]
    [InlineData("HTTP://example.com")]
    public async Task ExecuteMenuCommandAsync_AcceptsAllowedSchemes(string url)
    {
        var browserLauncher = new Mock<IWebBrowserLauncher>();
        var plugin = new WebBrowserPlugin(browserLauncher.Object);
        var hostApi = new Mock<IHostCommandApi>();
        var context = new MenuCommandContext
        {
            Properties = new Dictionary<string, string> { ["url"] = url },
            HostApi = hostApi.Object
        };

        await plugin.ExecuteMenuCommandAsync("webbrowser.open", context);

        browserLauncher.Verify(
            launcher => launcher.OpenAsync(url, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteMenuCommandAsync_Notifies_WhenLaunchFails()
    {
        var browserLauncher = new Mock<IWebBrowserLauncher>();
        browserLauncher.Setup(launcher => launcher.OpenAsync("https://example.com", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("launch failed"));
        var plugin = new WebBrowserPlugin(browserLauncher.Object);
        var hostApi = new Mock<IHostCommandApi>();

        var context = new MenuCommandContext
        {
            Properties = new Dictionary<string, string> { ["url"] = "https://example.com" },
            HostApi = hostApi.Object
        };

        await plugin.ExecuteMenuCommandAsync("webbrowser.open", context);

        hostApi.Verify(
            api => api.NotifyAsync(It.Is<string>(message => message.Contains("Failed to open URL")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
