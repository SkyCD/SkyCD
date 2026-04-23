using SkyCD.Plugin.Abstractions.Capabilities.Menu;
using SkyCD.Plugin.Abstractions.Lifecycle;

namespace SkyCD.Plugin.Sample.Menu;

public sealed class SampleMenuPlugin : IPlugin, IMenuPluginCapability
{
    public string Id => "skycd.plugin.sample.menu";
    public string Name => "Sample Menu Plugin";
    public Version Version => new(1, 0, 0);
    public Version MinHostVersion => new(3, 0, 0);
    public string Description => "Example menu contribution plugin.";

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
