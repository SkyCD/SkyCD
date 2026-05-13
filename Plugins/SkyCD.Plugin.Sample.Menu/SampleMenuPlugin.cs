using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Menu;

namespace SkyCD.Plugin.Sample.Menu;

public sealed class SampleMenuPlugin : IMenuPluginCapability
{
    public IReadOnlyCollection<MenuContribution> GetMenuContributions() =>
    [
        new("sample.menu.example", "Example", "Tools", Order: 100)
    ];

    public async Task ExecuteMenuCommandAsync(string commandId, MenuCommandContext context,
        CancellationToken cancellationToken = default)
    {
        if (!commandId.Equals("sample.menu.example", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (context.HostApi is null)
        {
            throw new InvalidOperationException("Host API is required.");
        }

        await context.HostApi.NotifyAsync(
            "did you thought that this sample plugin can do something useful? \U0001F609",
            cancellationToken);
    }
}