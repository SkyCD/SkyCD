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
        new MenuContribution("sample.menu.notify", "Notification", "Tools", Order: 100)
    ];

    public async Task ExecuteMenuCommandAsync(string commandId, MenuCommandContext context, CancellationToken cancellationToken = default)
    {
        if (!commandId.Equals("sample.menu.notify", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (context.HostApi is null)
        {
            throw new InvalidOperationException("Host API is required.");
        }

        await context.HostApi.NotifyAsync("menu command executed.", cancellationToken);
    }
}
