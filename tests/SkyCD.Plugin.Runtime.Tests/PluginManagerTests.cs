using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SkyCD.Couchbase;
using SkyCD.Couchbase.Repository;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.SDK.Capabilities.Menu;
using SkyCD.Plugin.Runtime.Documents;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests;

public class PluginManagerTests : IDisposable
{
    private readonly TestWorkspace workspace = new();

    public void Dispose()
    {
        workspace.Dispose();
    }

    [Fact]
    public void BuildFromAssembly_ReturnsPluginAndCapabilities()
    {
        var factory = new DiscoveredPluginFactory();

        var target = factory.BuildFromAssembly(Assembly.GetExecutingAssembly());
        Assert.Contains(target.Capabilities, capability => capability is IMenuPluginCapability);
        Assert.Contains(target.Capabilities, capability => capability is IFileFormatPluginCapability);
        Assert.Contains(target.Capabilities, capability => capability is StandaloneFileFormatCapability);

        Assert.Equal("Runtime Test Plugin", target.Name);
        Assert.Equal(new Version(3, 0, 0), target.MinHostVersion);
    }

    [Fact]
    public void Discover_SkipsIncompatiblePlugin()
    {
        var root = workspace.CreateDirectory($"plugin-compat-{Guid.NewGuid():N}");

        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        File.Copy(assemblyPath, Path.Combine(root, Path.GetFileName(assemblyPath)), overwrite: true);

        var discovery = new PluginManager(
            NullLogger<PluginManager>.Instance,
            new AssembliesListFactory(NullLogger<AssembliesListFactory>.Instance),
            new DiscoveredPluginFactory(),
            CreatePluginRepository());
        discovery.Discover(root, new Version(2, 9, 0));

        Assert.Empty(discovery.Plugins);
    }

    [Fact]
    public void BuildFromAssembly_UsesAssemblyMetadata()
    {
        var factory = new DiscoveredPluginFactory();

        var target = factory.BuildFromAssembly(Assembly.GetExecutingAssembly());
        Assert.Equal("tests.runtime.assembly-plugin", target.Id);
        Assert.Equal("Runtime Test Plugin", target.Name);
        Assert.Equal(Assembly.GetExecutingAssembly().GetName().Version, target.Version);
    }

    [Fact]
    public void Discover_PersistsPrimaryExtensionInDescriptor()
    {
        var root = workspace.CreateDirectory($"plugin-icon-{Guid.NewGuid():N}");

        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        File.Copy(assemblyPath, Path.Combine(root, Path.GetFileName(assemblyPath)), overwrite: true);

        var manager = new PluginManager(
            NullLogger<PluginManager>.Instance,
            new AssembliesListFactory(NullLogger<AssembliesListFactory>.Instance),
            new DiscoveredPluginFactory(),
            CreatePluginRepository());
        manager.Discover(root, new Version(3, 0, 0));

        var descriptor = Assert.Single(manager.GetPluginDescriptors());
        Assert.Equal("test", descriptor.PrimaryExtension);
    }

    private IRepository<PluginDocument> CreatePluginRepository()
    {
        var databaseManager = new DatabaseManager();
        var directory = workspace.CreateDirectory(Path.Combine("plugin-runtime-db", Guid.NewGuid().ToString("N")));
        databaseManager.Connect("default", directory);
        var repositoryManager = new RepositoryManager(databaseManager);
        return (IRepository<PluginDocument>)repositoryManager.For<PluginDocument>();
    }
}

public sealed class PluginDiscoveryCapabilityPlugin : IMenuPluginCapability, IFileFormatPluginCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new("test", "Test", [".test"], ["application/x-test"], true, false);

    public IReadOnlyCollection<MenuContribution> GetMenuContributions() =>
    [
        new("tests.command", "Tests", "Tools")
    ];

    public Task ExecuteMenuCommandAsync(string commandId, MenuCommandContext context,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new FileFormatReadResult { Success = true, Payload = new object() });

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new FileFormatWriteResult { Success = false, Error = "Read-only test format." });
}

public sealed class StandaloneFileFormatCapability : IFileFormatPluginCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new("standalone", "Standalone", [".stand"], ["application/x-standalone"], CanRead: true, CanWrite: false);

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new FileFormatReadResult { Success = true, Payload = "standalone" });

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new FileFormatWriteResult { Success = false, Error = "Read-only." });
}