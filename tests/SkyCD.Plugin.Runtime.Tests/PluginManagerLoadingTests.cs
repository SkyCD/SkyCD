using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;
using SkyCD.Couchbase;
using SkyCD.Couchbase.Repository;
using SkyCD.Plugin.Runtime.Documents;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.SDK.Capabilities.Accounts;
using SkyCD.SDK.Capabilities.Actions;
using SkyCD.SDK.Capabilities.Ownership;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests;

public sealed class PluginManagerLoadingTests : IDisposable
{
    private readonly TestWorkspace workspace = new();

    private string _root => workspace.Root;

    [Fact]
    public void LoadFromDirectories_LoadsPluginFromAssemblyWithoutManifest()
    {
        var pluginDir = Path.Combine(_root, "good");
        Directory.CreateDirectory(pluginDir);

        var assemblyName = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
        File.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(pluginDir, assemblyName), overwrite: true);

        var logger = new TestLogger<PluginManager>();
        var assembliesLogger = new TestLogger<AssembliesListFactory>();
        var pluginManager = new PluginManager(logger, new AssembliesListFactory(assembliesLogger),
            new DiscoveredPluginFactory(), CreatePluginRepository());
        pluginManager.Discover(_root, new Version(3, 0, 0));

        Assert.Contains(pluginManager.Plugins, plugin => plugin.Id == "tests.runtime.assembly-plugin");
        Assert.Single(pluginManager.GetCapabilities<IOwnershipProvider>());
        Assert.Single(pluginManager.GetCapabilities<IAccountConnector>());
        Assert.Single(pluginManager.GetCapabilities<IActionProvider>());
        Assert.NotEmpty(pluginManager.GetCapabilities<IFileFormatPluginCapability>());
        var metadataCapabilities = pluginManager.GetCapabilities<IMediaMetadataCapability>();
        Assert.Contains(metadataCapabilities, capability => capability is FakeMediaCapability);
        Assert.Contains(metadataCapabilities, capability => capability is ProbeAcceptingMetadataCapability);
        Assert.Contains(metadataCapabilities, capability => capability is ProbeRejectingMetadataCapability);
        Assert.Single(pluginManager.GetCapabilities<IMediaPreviewCapability>());
        Assert.DoesNotContain(assembliesLogger.Entries, entry => entry.LogLevel >= LogLevel.Error);

        pluginManager.SavePluginEnabledStates([("tests.runtime.assembly-plugin", false)]);
        pluginManager.Discover(_root, new Version(3, 0, 0));

        Assert.Empty(pluginManager.GetCapabilities<IOwnershipProvider>());
        Assert.Empty(pluginManager.GetCapabilities<IAccountConnector>());
        Assert.Empty(pluginManager.GetCapabilities<IActionProvider>());
        Assert.Empty(pluginManager.GetCapabilities<IFileFormatPluginCapability>());
        Assert.Empty(pluginManager.GetCapabilities<IMediaMetadataCapability>());
        Assert.Empty(pluginManager.GetCapabilities<IMediaPreviewCapability>());

        pluginManager.SavePluginEnabledStates([("tests.runtime.assembly-plugin", true)]);
        pluginManager.Discover(_root, new Version(4, 0, 0));
        Assert.Empty(pluginManager.GetCapabilities<IMediaMetadataCapability>());
        Assert.Empty(pluginManager.GetCapabilities<IMediaPreviewCapability>());
    }

    [Fact]
    public void LoadFromDirectories_ReportsDiagnosticWhenAssemblyLoadFails()
    {
        var pluginDir = Path.Combine(_root, "invalid");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "broken.dll"), "this is not a valid .NET assembly");

        var logger = new TestLogger<PluginManager>();
        var assembliesLogger = new TestLogger<AssembliesListFactory>();
        var pluginManager = new PluginManager(logger, new AssembliesListFactory(assembliesLogger),
            new DiscoveredPluginFactory(), CreatePluginRepository());
        pluginManager.Discover(_root, new Version(3, 0, 0));

        Assert.Empty(pluginManager.Plugins);
        Assert.Contains(assembliesLogger.Entries, entry =>
            entry.LogLevel == LogLevel.Warning &&
            entry.Message.Contains("Skipped", StringComparison.OrdinalIgnoreCase) &&
            entry.Message.Contains("broken.dll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadFromDirectories_ReportsDiagnosticWhenDirectoryDoesNotExist()
    {
        var missingDirectory = Path.Combine(_root, "missing");
        var logger = new TestLogger<PluginManager>();
        var assembliesLogger = new TestLogger<AssembliesListFactory>();
        var pluginManager = new PluginManager(logger, new AssembliesListFactory(assembliesLogger),
            new DiscoveredPluginFactory(), CreatePluginRepository());

        pluginManager.Discover(missingDirectory, new Version(3, 0, 0));

        Assert.Empty(pluginManager.Plugins);
        Assert.Contains(assembliesLogger.Entries, entry =>
            entry.LogLevel == LogLevel.Warning &&
            entry.Message.Contains("Plugin directory not found", StringComparison.OrdinalIgnoreCase) &&
            entry.Message.Contains(missingDirectory, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadFromDirectories_IgnoresObjAndReferenceOutputs_AndDeduplicatesAssemblyName()
    {
        var pluginA = Path.Combine(_root, "a");
        var pluginB = Path.Combine(_root, "b");
        var objOutput = Path.Combine(pluginA, "obj", "Release", "net10.0");
        var binOutput = Path.Combine(pluginB, "bin", "Release", "net10.0");
        Directory.CreateDirectory(objOutput);
        Directory.CreateDirectory(binOutput);

        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var assemblyName = Path.GetFileName(assemblyPath);
        File.Copy(assemblyPath, Path.Combine(objOutput, assemblyName), overwrite: true);
        File.Copy(assemblyPath, Path.Combine(binOutput, assemblyName), overwrite: true);
        File.WriteAllText(Path.Combine(pluginB, "b.csproj"), "<Project />");

        var logger = new TestLogger<PluginManager>();
        var assembliesLogger = new TestLogger<AssembliesListFactory>();
        var pluginManager = new PluginManager(logger, new AssembliesListFactory(assembliesLogger),
            new DiscoveredPluginFactory(), CreatePluginRepository());
        var combinedPaths = string.Join(Path.PathSeparator, pluginA, pluginB);

        pluginManager.Discover(combinedPaths, new Version(3, 0, 0));

        Assert.Contains(pluginManager.Plugins, plugin => plugin.Id == "tests.runtime.assembly-plugin");
        Assert.DoesNotContain(
            assembliesLogger.Entries,
            entry => entry.LogLevel == LogLevel.Warning &&
                     entry.Message.Contains("already loaded", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadFromDirectories_SkipsBuildOutputWhoseProjectIsGone()
    {
        var binOutput = Path.Combine(_root, "deleted", "bin", "Release", "net10.0");
        Directory.CreateDirectory(binOutput);
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        File.Copy(assemblyPath, Path.Combine(binOutput, Path.GetFileName(assemblyPath)), overwrite: true);

        var pluginManager = CreateManager();

        pluginManager.Discover(_root, new Version(3, 0, 0));

        Assert.Empty(pluginManager.Plugins);
    }

    [Fact]
    public void LoadFromDirectories_LoadsBuildOutputBesideItsProject()
    {
        var project = Path.Combine(_root, "project");
        var binOutput = Path.Combine(project, "bin", "Release", "net10.0");
        Directory.CreateDirectory(binOutput);
        File.WriteAllText(Path.Combine(project, "project.csproj"), "<Project />");
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        File.Copy(assemblyPath, Path.Combine(binOutput, Path.GetFileName(assemblyPath)), overwrite: true);

        var pluginManager = CreateManager();

        pluginManager.Discover(_root, new Version(3, 0, 0));

        Assert.Contains(pluginManager.Plugins, plugin => plugin.Id == "tests.runtime.assembly-plugin");
    }

    public void Dispose()
    {
        workspace.Dispose();
    }

    private PluginManager CreateManager() =>
        new(new TestLogger<PluginManager>(),
            new AssembliesListFactory(new TestLogger<AssembliesListFactory>()),
            new DiscoveredPluginFactory(), CreatePluginRepository());

    private IRepository<PluginDocument> CreatePluginRepository()
    {
        var databaseManager = new DatabaseManager();
        var directory = workspace.CreateDirectory(Path.Combine("plugin-runtime-loading-db",
            Guid.NewGuid().ToString("N")));
        databaseManager.Connect("default", directory);
        var repositoryManager = new RepositoryManager(databaseManager);
        return (IRepository<PluginDocument>)repositoryManager.For<PluginDocument>();
    }
}

internal sealed class TestLogger<T> : ILogger<T>
{
    public List<LogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }

    public sealed record LogEntry(LogLevel LogLevel, string Message);
}
