using System.Reflection;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Menu;
using SkyCD.Plugin.Abstractions.Lifecycle;
using SkyCD.Plugin.Runtime.Discovery;

namespace SkyCD.Plugin.Runtime.Tests;

public class PluginDiscoveryServiceTests
{
    [Fact]
    public void DiscoverFromAssembly_ReturnsCompatiblePluginAndCapabilities()
    {
        var discovery = new PluginDiscoveryService();

        var plugins = discovery.DiscoverFromAssembly(Assembly.GetExecutingAssembly(), new Version(3, 0, 0));

        var target = Assert.Single(plugins, plugin => plugin.Plugin.Descriptor.Id == "tests.plugin");
        Assert.Contains(target.Capabilities, capability => capability is IMenuPluginCapability);
        Assert.Contains(target.Capabilities, capability => capability is IFileFormatPluginCapability);
    }

    [Fact]
    public void DiscoverFromAssembly_SkipsIncompatiblePlugin()
    {
        var discovery = new PluginDiscoveryService();

        var plugins = discovery.DiscoverFromAssembly(Assembly.GetExecutingAssembly(), new Version(2, 9, 0));

        Assert.Empty(plugins);
    }

    private sealed class TestCapabilityPlugin : IPlugin, IMenuPluginCapability, IFileFormatPluginCapability
    {
        public IReadOnlyCollection<FileFormatDescriptor> SupportedFormats =>
        [
            new("test", "Test", [".test"], true, false)
        ];

        public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new FileFormatReadResult { Success = true, Payload = new object() });
        }

        public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new FileFormatWriteResult { Success = false, Error = "Read-only test format." });
        }

        public IReadOnlyCollection<MenuContribution> GetMenuContributions()
        {
            return
            [
                new MenuContribution("tests.command", "Tests", "Tools")
            ];
        }

        public Task ExecuteMenuCommandAsync(string commandId, MenuCommandContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public PluginDescriptor Descriptor => new(
            "tests.plugin",
            "Test Plugin",
            new Version(1, 0, 0),
            new Version(3, 0, 0),
            "Runtime discovery test plugin");

        public ValueTask OnLoadAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask OnInitializeAsync(PluginLifecycleContext context,
            CancellationToken cancellationToken = default)
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
}