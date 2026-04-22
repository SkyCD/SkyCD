using SkyCD.Plugin.Abstractions.Capabilities.Menu;
using SkyCD.Plugin.Abstractions.Lifecycle;

namespace SkyCD.Plugin.Sample.Menu;

public sealed class SampleMenuPlugin : IPlugin, IMenuPluginCapability
{
    public IReadOnlyCollection<MenuContribution> GetMenuContributions()
    {
        return
        [
            new MenuContribution("sample.menu.notify", "Notification", "Tools", 100)
        ];
    }

    public async Task ExecuteMenuCommandAsync(string commandId, MenuCommandContext context,
        CancellationToken cancellationToken = default)
    {
        if (!commandId.Equals("sample.menu.notify", StringComparison.OrdinalIgnoreCase)) return;

        if (context.HostApi is null) throw new InvalidOperationException("Host API is required.");

        await context.HostApi.NotifyAsync("menu command executed.", cancellationToken);
    }

    public PluginDescriptor Descriptor => new(
        "skycd.plugin.sample.menu",
        "Sample Menu Plugin",
        new Version(1, 0, 0),
        new Version(3, 0, 0),
        "Example menu contribution plugin.");

    public ValueTask OnLoadAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask OnInitializeAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask OnActivateAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}