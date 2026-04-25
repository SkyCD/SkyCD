using System.Reflection;
using Microsoft.Extensions.Logging;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Managers;

namespace SkyCD.Plugin.Runtime.Tests;

public sealed class PluginManagerLoadingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"skycd-plugins-{Guid.NewGuid():N}");

    public PluginManagerLoadingTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void LoadFromDirectories_LoadsPluginFromAssemblyWithoutManifest()
    {
        var pluginDir = Path.Combine(_root, "good");
        Directory.CreateDirectory(pluginDir);

        var assemblyName = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
        File.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(pluginDir, assemblyName), overwrite: true);

        var logger = new TestLogger<PluginManager>();
        var assembliesLogger = new TestLogger<AssembliesListFactory>();
        var pluginManager = new PluginManager(logger, new AssembliesListFactory(assembliesLogger), new DiscoveredPluginFactory());
        pluginManager.Discover(_root, new Version(3, 0, 0));

        Assert.Contains(pluginManager.Plugins, plugin => plugin.Id == "tests.runtime.assembly-plugin");
        Assert.DoesNotContain(assembliesLogger.Entries, entry => entry.LogLevel >= LogLevel.Error);
    }

    [Fact]
    public void LoadFromDirectories_ReportsDiagnosticWhenAssemblyLoadFails()
    {
        var pluginDir = Path.Combine(_root, "invalid");
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "broken.dll"), "this is not a valid .NET assembly");

        var logger = new TestLogger<PluginManager>();
        var assembliesLogger = new TestLogger<AssembliesListFactory>();
        var pluginManager = new PluginManager(logger, new AssembliesListFactory(assembliesLogger), new DiscoveredPluginFactory());
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
        var pluginManager = new PluginManager(logger, new AssembliesListFactory(assembliesLogger), new DiscoveredPluginFactory());

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

        var logger = new TestLogger<PluginManager>();
        var assembliesLogger = new TestLogger<AssembliesListFactory>();
        var pluginManager = new PluginManager(logger, new AssembliesListFactory(assembliesLogger), new DiscoveredPluginFactory());
        var combinedPaths = string.Join(Path.PathSeparator, pluginA, pluginB);

        pluginManager.Discover(combinedPaths, new Version(3, 0, 0));

        Assert.Contains(pluginManager.Plugins, plugin => plugin.Id == "tests.runtime.assembly-plugin");
        Assert.DoesNotContain(
            assembliesLogger.Entries,
            entry => entry.LogLevel == LogLevel.Warning &&
                     entry.Message.Contains("already loaded", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
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
