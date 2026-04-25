using System.Reflection;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Menu;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Managers;

namespace SkyCD.Plugin.Runtime.Tests;

public class PluginManagerTests
{
    [Fact]
    public void BuildFromAssembly_ReturnsPluginAndCapabilities()
    {
        var factory = new DiscoveredPluginFactory();

        var target = factory.BuildFromAssembly(Assembly.GetExecutingAssembly());
        Assert.Contains(target.Capabilities, capability => capability is IMenuPluginCapability);
        Assert.Contains(target.Capabilities, capability => capability is IFileFormatPluginCapability);
        Assert.Contains(target.Capabilities, capability => capability is StandaloneFileFormatCapability);

        Assert.Equal("SkyCD.Plugin.Runtime.Tests", target.Name);
        Assert.Equal(new Version(3, 0, 0), target.MinHostVersion);
    }

    [Fact]
    public void Discover_SkipsIncompatiblePlugin()
    {
        var root = Path.Combine(Path.GetTempPath(), $"skycd-plugin-compat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            File.Copy(assemblyPath, Path.Combine(root, Path.GetFileName(assemblyPath)), overwrite: true);

            var discovery = new PluginManager();
            discovery.Discover(root, new Version(2, 9, 0));

            Assert.Empty(discovery.Plugins);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void BuildFromAssembly_UsesAssemblyMetadata()
    {
        var factory = new DiscoveredPluginFactory();

        var target = factory.BuildFromAssembly(Assembly.GetExecutingAssembly());
        Assert.Equal("tests.runtime.assembly-plugin", target.Id);
        Assert.Equal("SkyCD.Plugin.Runtime.Tests", target.Name);
        Assert.Equal(Assembly.GetExecutingAssembly().GetName().Version, target.Version);
    }
}

public sealed class PluginDiscoveryCapabilityPlugin : IMenuPluginCapability, IFileFormatPluginCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new("test", "Test", [".test"], true, false);

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
    public FileFormatDescriptor SupportedFormat =>
        new("standalone", "Standalone", [".stand"], CanRead: true, CanWrite: false);

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new FileFormatReadResult { Success = true, Payload = "standalone" });

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new FileFormatWriteResult { Success = false, Error = "Read-only." });
}
