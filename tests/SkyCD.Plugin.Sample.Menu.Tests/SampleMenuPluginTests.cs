using System;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Menu;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.Sample.Menu;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public class SampleMenuPluginTests
{
    [Fact]
    public void GetMenuContributions_ReturnsExpectedContribution()
    {
        var service = new MenuExtensionManager([new SampleMenuPlugin()]);

        var contributions = service.GetMenuContributions("Tools");
        var contribution = Assert.Single(contributions);

        Assert.Equal("sample.menu.example", contribution.CommandId);
        Assert.Equal("Example", contribution.Title);
        Assert.Equal("Tools", contribution.Location);
    }

    [Fact]
    public async Task ExecuteAsync_UnregisteredCommandId_ReturnsFailure()
    {
        var service = new MenuExtensionManager([new SampleMenuPlugin()]);
        var context = new MenuCommandContext();

        var result = await service.ExecuteAsync(
            "unknown.command",
            context,
            timeout: TimeSpan.FromSeconds(1));

        Assert.False(result.Success);
    }
}
