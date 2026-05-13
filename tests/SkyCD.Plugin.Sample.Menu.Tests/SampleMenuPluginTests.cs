using System;
using System.Collections.Generic;
using System.Threading;
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
    public async Task ExecuteAsync_InvokesHostNotification()
    {
        var service = new MenuExtensionManager([new SampleMenuPlugin()]);
        var hostApi = new RecordingHostCommandApi();
        var context = new MenuCommandContext
        {
            HostApi = hostApi
        };

        var result = await service.ExecuteAsync(
            "sample.menu.example",
            context,
            timeout: TimeSpan.FromSeconds(1));

        Assert.True(result.Success, result.Error);
        Assert.Single(hostApi.Notifications);
        Assert.Equal("did you thought that this sample plugin can do something useful? \U0001F609", hostApi.Notifications[0]);
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
