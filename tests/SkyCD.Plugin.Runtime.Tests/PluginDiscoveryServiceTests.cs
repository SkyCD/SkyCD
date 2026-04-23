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

        var target = Assert.Single(plugins, plugin => plugin.Plugin.Descriptor.Id == "tests.runtime.assembly-plugin");
        Assert.Contains(target.Capabilities, capability => capability is IMenuPluginCapability);
        Assert.Contains(target.Capabilities, capability => capability is IFileFormatPluginCapability);
        Assert.Contains(target.Capabilities, capability => capability is StandaloneFileFormatCapability);
    }

    [Fact]
    public void DiscoverFromAssembly_SkipsIncompatiblePlugin()
    {
        var discovery = new PluginDiscoveryService();

        var plugins = discovery.DiscoverFromAssembly(Assembly.GetExecutingAssembly(), new Version(2, 9, 0));

        Assert.Empty(plugins);
    }

    [Fact]
    public void DiscoverFromPlugins_ReturnsCapabilitiesFromExistingInstance()
    {
        var discovery = new PluginDiscoveryService();
        var plugin = new PluginDiscoveryCapabilityPlugin();

        var plugins = discovery.DiscoverFromPlugins([plugin]);

        var target = Assert.Single(plugins);
        Assert.Equal("tests.runtime.assembly-plugin", target.Plugin.Descriptor.Id);
        Assert.Contains(target.Capabilities, capability => capability is IMenuPluginCapability);
        Assert.Contains(target.Capabilities, capability => capability is IFileFormatPluginCapability);
    }
}

public sealed class PluginDiscoveryCapabilityPlugin : IPlugin, IMenuPluginCapability, IFileFormatPluginCapability
{
    public PluginDescriptor Descriptor => new(
        "tests.plugin",
        "Test Plugin",
        new Version(1, 0, 0),
        new Version(3, 0, 0),
        "Runtime discovery test plugin");

    public IReadOnlyCollection<FileFormatDescriptor> SupportedFormats =>
    [
        new FileFormatDescriptor("test", "Test", [".test"], true, false)
    ];

    public ValueTask OnLoadAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask OnInitializeAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask OnActivateAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public IReadOnlyCollection<MenuContribution> GetMenuContributions() =>
    [
        new MenuContribution("tests.command", "Tests", "Tools")
    ];

    public Task ExecuteMenuCommandAsync(string commandId, MenuCommandContext context, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new FileFormatReadResult { Success = true, Payload = new object() });

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new FileFormatWriteResult { Success = false, Error = "Read-only test format." });
}

public sealed class StandaloneFileFormatCapability : IFileFormatPluginCapability
{
    public IReadOnlyCollection<FileFormatDescriptor> SupportedFormats =>
    [
        new("standalone", "Standalone", [".stand"], CanRead: true, CanWrite: false)
    ];

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new FileFormatReadResult { Success = true, Payload = "standalone" });

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new FileFormatWriteResult { Success = false, Error = "Read-only." });
}
