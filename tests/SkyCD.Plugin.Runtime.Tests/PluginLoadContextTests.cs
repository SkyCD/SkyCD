using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging.Abstractions;
using SkyCD.Couchbase;
using SkyCD.Couchbase.Repository;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Documents;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Loading;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests;

/// <summary>
/// Issue #1229: plugins load in per-folder collectible contexts instead of the default context.
/// </summary>
public sealed class PluginLoadContextTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"skycd-alc-{Guid.NewGuid():N}");

    public PluginLoadContextTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void SideBySideConflictingVersions_LoadInSeparateContexts()
    {
        var firstDir = Path.Combine(root, "first");
        var secondDir = Path.Combine(root, "second");
        Directory.CreateDirectory(firstDir);
        Directory.CreateDirectory(secondDir);

        var packageDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages", "system.reflection.metadataloadcontext", "10.0.15", "lib");
        var firstSource = Path.Combine(packageDir, "netstandard2.0", "System.Reflection.MetadataLoadContext.dll");
        var secondSource = Path.Combine(packageDir, "net10.0", "System.Reflection.MetadataLoadContext.dll");
        Assert.True(File.Exists(firstSource), "Old-target fixture assembly must exist in the NuGet cache.");
        Assert.True(File.Exists(secondSource), "New-target fixture assembly must exist in the NuGet cache.");

        var firstPath = Path.Combine(firstDir, "Conflict.dll");
        var secondPath = Path.Combine(secondDir, "Conflict.dll");
        File.Copy(firstSource, firstPath);
        File.Copy(secondSource, secondPath);

        PluginLoadContext firstContext = new(firstDir);
        PluginLoadContext secondContext = new(secondDir);
        try
        {
            var first = firstContext.LoadEntry(firstPath);
            var second = secondContext.LoadEntry(secondPath);

            Assert.NotSame(first, second);
            Assert.Equal(first.GetName().Name, second.GetName().Name);
            Assert.NotEqual(first.ManifestModule.ModuleVersionId, second.ManifestModule.ModuleVersionId);
            Assert.Same(firstContext, AssemblyLoadContext.GetLoadContext(first));
            Assert.Same(secondContext, AssemblyLoadContext.GetLoadContext(second));
            Assert.Equal(firstPath, first.Location);
            Assert.Equal(secondPath, second.Location);
        }
        finally
        {
            firstContext.Unload();
            secondContext.Unload();
        }
    }

    [Fact]
    public void UnloadedContext_RejectsFurtherLoads()
    {
        var pluginDir = Path.Combine(root, "unload");
        Directory.CreateDirectory(pluginDir);
        var entryPath = Path.Combine(pluginDir, "Entry.dll");
        File.Copy(Assembly.GetExecutingAssembly().Location, entryPath, overwrite: true);

        var context = new PluginLoadContext(pluginDir);
        try
        {
            Assert.NotNull(context.LoadEntry(entryPath));
        }
        finally
        {
            context.Unload();
        }

        Assert.Throws<InvalidOperationException>(() => context.LoadEntry(entryPath));
    }

    [Fact]
    public void NestedStrayDll_IsNeverLoaded()
    {
        var pluginDir = Path.Combine(root, "stray");
        var nested = Path.Combine(pluginDir, "sub", "deep");
        Directory.CreateDirectory(nested);

        var entryPath = Path.Combine(pluginDir, "Entry.dll");
        File.Copy(Assembly.GetExecutingAssembly().Location, entryPath, overwrite: true);
        var strayPath = Path.Combine(nested, "Stray.dll");
        File.Copy(typeof(AssembliesListFactory).Assembly.Location, strayPath, overwrite: true);

        var factory = new AssembliesListFactory(NullLogger<AssembliesListFactory>.Instance);
        var assemblies = factory.BuildFromPaths([pluginDir]);
        try
        {
            Assert.Contains(assemblies, assembly =>
                string.Equals(assembly.Location, entryPath, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(assemblies, assembly =>
                string.Equals(assembly.Location, strayPath, StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), assembly =>
                string.Equals(assembly.Location, strayPath, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            foreach (var context in factory.LoadedContexts)
            {
                context.Unload();
            }
        }
    }

    [Fact]
    public void Discovery_DoesNotPolluteDefaultContext()
    {
        var pluginDir = Path.Combine(root, "clean");
        Directory.CreateDirectory(pluginDir);
        var entryPath = Path.Combine(pluginDir, Path.GetFileName(Assembly.GetExecutingAssembly().Location));
        File.Copy(Assembly.GetExecutingAssembly().Location, entryPath, overwrite: true);

        var manager = new PluginManager(
            NullLogger<PluginManager>.Instance,
            new AssembliesListFactory(NullLogger<AssembliesListFactory>.Instance),
            new DiscoveredPluginFactory(),
            CreatePluginRepository());

        manager.Discover(pluginDir, new Version(3, 0, 0));

        Assert.Contains(manager.Plugins, plugin => plugin.Id == "tests.runtime.assembly-plugin");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        // AppDomain.GetAssemblies includes every load context. Pollution is a second copy in the
        // default context, whose location is the file copied under the test root.
        Assert.DoesNotContain(AssemblyLoadContext.Default.Assemblies, assembly =>
            !string.IsNullOrWhiteSpace(assembly.Location) &&
            assembly.Location.StartsWith(root, StringComparison.OrdinalIgnoreCase));
        Assert.Single(AppDomain.CurrentDomain.GetAssemblies(), assembly =>
            string.Equals(assembly.GetName().Name, "SkyCD.SDK", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SharedSdk_NeverLoadsSecondCopy()
    {
        var pluginDir = Path.Combine(root, "shared");
        Directory.CreateDirectory(pluginDir);
        File.Copy(Assembly.GetExecutingAssembly().Location,
            Path.Combine(pluginDir, Path.GetFileName(Assembly.GetExecutingAssembly().Location)), overwrite: true);

        var factory = new AssembliesListFactory(NullLogger<AssembliesListFactory>.Instance);
        var assemblies = factory.BuildFromPaths([pluginDir]);
        try
        {
            Assert.NotEmpty(assemblies);
            Assert.DoesNotContain(assemblies, assembly =>
                string.Equals(assembly.GetName().Name, "SkyCD.SDK", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            foreach (var context in factory.LoadedContexts)
            {
                context.Unload();
            }
        }
    }

    private static IRepository<PluginDocument> CreatePluginRepository()
    {
        var databaseManager = new DatabaseManager();
        var directory = Path.Combine(Path.GetTempPath(), "SkyCD", "PluginLoadContextTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        databaseManager.Connect("default", directory);
        var repositoryManager = new RepositoryManager(databaseManager);
        return (IRepository<PluginDocument>)repositoryManager.For<PluginDocument>();
    }
}
