using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SkyCD.Couchbase;
using SkyCD.Couchbase.Repository;
using SkyCD.Plugin.Abstractions;
using SkyCD.Plugin.Abstractions.Capabilities.Configuration;
using SkyCD.Plugin.Runtime.Configuration;
using SkyCD.Plugin.Runtime.Documents;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.Sample.Configuration;
using Xunit;

namespace SkyCD.Plugin.Sample.Configuration.Tests;

public sealed class SampleConfigurationPluginTests : IDisposable
{
    private readonly string testDirectory;
    private readonly string pluginsDirectory;
    private readonly DatabaseManager databaseManager;
    private readonly RepositoryManager repositoryManager;
    private readonly PluginConfigurationStore configurationStore;
    private readonly PluginManager pluginManager;
    private readonly PluginConfigurationManager configurationManager;

    public SampleConfigurationPluginTests()
    {
        testDirectory = Path.Combine(Path.GetTempPath(), "SkyCD", "SampleConfigTests", Guid.NewGuid().ToString("N"));
        pluginsDirectory = Path.Combine(testDirectory, "plugins");
        Directory.CreateDirectory(pluginsDirectory);

        databaseManager = new DatabaseManager();
        databaseManager.Connect("default", Path.Combine(testDirectory, "db"));
        repositoryManager = new RepositoryManager(databaseManager);

        var configRepo = (IRepository<PluginConfigurationDocument>)repositoryManager.For<PluginConfigurationDocument>();
        configurationStore = new PluginConfigurationStore(configRepo);

        var pluginRepo = (IRepository<PluginDocument>)repositoryManager.For<PluginDocument>();
        var assembliesFactory = new AssembliesListFactory(NullLogger<AssembliesListFactory>.Instance);
        var pluginFactory = new DiscoveredPluginFactory();

        pluginManager = new PluginManager(
            NullLogger<PluginManager>.Instance,
            assembliesFactory,
            pluginFactory,
            pluginRepo);

        configurationManager = new PluginConfigurationManager(pluginManager, configurationStore);
    }

    [Fact]
    public void SampleConfigurationPlugin_HasExpectedDescriptor()
    {
        var plugin = new SampleConfigurationPlugin();

        Assert.Equal("sample.configuration.general", plugin.Configuration.ConfigurationId);
        Assert.Equal("Sample Configuration", plugin.Configuration.Title);
        Assert.Equal(420, plugin.Configuration.Width);
        Assert.Equal(240, plugin.Configuration.Height);
    }

    [Fact]
    public void PluginManager_DiscoversAndRegistersSampleConfigurationPlugin()
    {
        CopyPluginAssembly(typeof(SampleConfigurationPlugin).Assembly);

        pluginManager.Discover(pluginsDirectory, new Version(3, 0, 0));

        var registrations = configurationManager.GetRegistrations();

        Assert.Contains(registrations, r => r.PluginId == "SkyCD.Plugin.Sample.Configuration");
        var registration = registrations.Single(r => r.PluginId == "SkyCD.Plugin.Sample.Configuration");
        Assert.Equal("Sample Configuration", registration.Descriptor.Title);
        Assert.Equal("sample.configuration.general", registration.Descriptor.ConfigurationId);
        Assert.True(configurationManager.SupportsConfiguration("SkyCD.Plugin.Sample.Configuration"));
        Assert.False(configurationManager.SupportsConfiguration("SkyCD.Plugin.NonExistent"));
        Assert.False(configurationManager.SupportsConfiguration(null));
    }

    [Fact]
    public async Task ConfigureAsync_SavesSettings_WhenSuccessful()
    {
        CopyPluginAssembly(typeof(SampleConfigurationPlugin).Assembly);

        pluginManager.Discover(pluginsDirectory, new Version(3, 0, 0));

        var outcome = await configurationManager.ConfigureAsync("SkyCD.Plugin.Sample.Configuration", TimeSpan.FromSeconds(5));

        Assert.Equal(PluginConfigurationStatus.Succeeded, outcome.Status);
        Assert.NotNull(outcome.Settings);
        Assert.Equal(SampleConfigurationEditor.DefaultGreeting, outcome.Settings[SampleConfigurationEditor.GreetingKey]);
        Assert.Equal(SampleConfigurationEditor.DefaultSignature, outcome.Settings[SampleConfigurationEditor.SignatureKey]);

        // Verify settings were persisted into the store
        var persisted = configurationStore.GetSettings("SkyCD.Plugin.Sample.Configuration");
        Assert.Equal(SampleConfigurationEditor.DefaultGreeting, persisted[SampleConfigurationEditor.GreetingKey]);
        Assert.Equal(SampleConfigurationEditor.DefaultSignature, persisted[SampleConfigurationEditor.SignatureKey]);
    }

    [Fact]
    public async Task ConfigureAsync_PassesExistingSettingsToPlugin()
    {
        CopyPluginAssembly(typeof(SampleConfigurationPlugin).Assembly);

        // Pre-seed configuration
        configurationStore.SaveSettings("SkyCD.Plugin.Sample.Configuration", new Dictionary<string, string?>
        {
            [SampleConfigurationEditor.GreetingKey] = "Custom Greeting",
            [SampleConfigurationEditor.SignatureKey] = "Custom Signature"
        });

        pluginManager.Discover(pluginsDirectory, new Version(3, 0, 0));

        var outcome = await configurationManager.ConfigureAsync("SkyCD.Plugin.Sample.Configuration", TimeSpan.FromSeconds(5));

        Assert.Equal(PluginConfigurationStatus.Succeeded, outcome.Status);
        Assert.NotNull(outcome.Settings);
        Assert.Equal("Custom Greeting", outcome.Settings[SampleConfigurationEditor.GreetingKey]);
        Assert.Equal("Custom Signature", outcome.Settings[SampleConfigurationEditor.SignatureKey]);
    }

    [Fact]
    public async Task ConfigureAsync_DoesNotPersistSettings_WhenCanceled()
    {
        CopyPluginAssembly(Assembly.GetExecutingAssembly());

        var pluginId = Assembly.GetExecutingAssembly().GetName().Name!;
        // Pre-seed settings
        configurationStore.SaveSettings(pluginId, new Dictionary<string, string?>
        {
            ["greeting"] = "Original Greeting"
        });

        pluginManager.Discover(pluginsDirectory, new Version(3, 0, 0));

        // Use test plugin with canceling capability
        TestStubConfigurationCapability.Mode = TestStubMode.Cancel;

        var outcome = await configurationManager.ConfigureAsync(pluginId, TimeSpan.FromSeconds(5));

        Assert.Equal(PluginConfigurationStatus.Canceled, outcome.Status);

        // Settings should remain unchanged
        var persisted = configurationStore.GetSettings(pluginId);
        Assert.Equal("Original Greeting", persisted["greeting"]);
    }

    [Fact]
    public async Task ConfigureAsync_ReturnsNotSupported_ForUnsupportedPlugin()
    {
        CopyPluginAssembly(typeof(SampleConfigurationPlugin).Assembly);

        pluginManager.Discover(pluginsDirectory, new Version(3, 0, 0));

        var outcome = await configurationManager.ConfigureAsync("SkyCD.Plugin.Unsupported", TimeSpan.FromSeconds(5));

        Assert.Equal(PluginConfigurationStatus.NotSupported, outcome.Status);
        Assert.Contains("does not provide a configuration surface", outcome.Error);
    }

    [Fact]
    public async Task ConfigureAsync_SurfacesPluginFailures_WithoutCrashingHost()
    {
        CopyPluginAssembly(Assembly.GetExecutingAssembly());

        var pluginId = Assembly.GetExecutingAssembly().GetName().Name!;
        TestStubConfigurationCapability.Mode = TestStubMode.Throw;

        PluginFailure? reportedFailure = null;
        Action<PluginFailure> handler = failure => reportedFailure = failure;
        PluginFailure.Reported += handler;

        try
        {
            pluginManager.Discover(pluginsDirectory, new Version(3, 0, 0));

            var outcome = await configurationManager.ConfigureAsync(pluginId, TimeSpan.FromSeconds(5));

            Assert.Equal(PluginConfigurationStatus.Failed, outcome.Status);
            Assert.NotNull(reportedFailure);
            Assert.Equal("configure", reportedFailure.Operation);
        }
        finally
        {
            PluginFailure.Reported -= handler;
        }
    }

    [Fact]
    public async Task ConfigureAsync_SurfacesPluginTimeout_WithoutCrashingHost()
    {
        CopyPluginAssembly(Assembly.GetExecutingAssembly());

        var pluginId = Assembly.GetExecutingAssembly().GetName().Name!;
        TestStubConfigurationCapability.Mode = TestStubMode.Delay;

        PluginFailure? reportedFailure = null;
        Action<PluginFailure> handler = failure => reportedFailure = failure;
        PluginFailure.Reported += handler;

        try
        {
            pluginManager.Discover(pluginsDirectory, new Version(3, 0, 0));

            var outcome = await configurationManager.ConfigureAsync(pluginId, TimeSpan.FromMilliseconds(50));

            Assert.Equal(PluginConfigurationStatus.TimedOut, outcome.Status);
            Assert.NotNull(reportedFailure);
            Assert.Equal("configure", reportedFailure.Operation);
        }
        finally
        {
            PluginFailure.Reported -= handler;
        }
    }

    [Fact]
    public void DiscoveredPluginFactory_DiscoversSampleConfigurationCapabilityFromAssembly()
    {
        var factory = new DiscoveredPluginFactory();
        var discovered = factory.BuildFromAssembly(typeof(SampleConfigurationPlugin).Assembly);

        Assert.NotNull(discovered);
        var configCapability = Assert.Single(discovered.Capabilities.OfType<IPluginConfigurationCapability>());
        Assert.Equal("sample.configuration.general", configCapability.Configuration.ConfigurationId);
    }

    [Fact]
    public void PluginConfigurationStore_ClearSettings_RemovesPersistedSettings()
    {
        configurationStore.SaveSettings("test.plugin", new Dictionary<string, string?>
        {
            ["key1"] = "val1",
            ["key2"] = "val2"
        });

        var stored = configurationStore.GetSettings("test.plugin");
        Assert.Equal(2, stored.Count);

        configurationStore.ClearSettings("test.plugin");

        var empty = configurationStore.GetSettings("test.plugin");
        Assert.Empty(empty);
    }

    public void Dispose()
    {
        if (Directory.Exists(testDirectory))
        {
            try
            {
                Directory.Delete(testDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private void CopyPluginAssembly(Assembly assembly)
    {
        var location = assembly.Location;
        var destination = Path.Combine(pluginsDirectory, Path.GetFileName(location));
        File.Copy(location, destination, overwrite: true);
    }
}

public enum TestStubMode
{
    Success,
    Cancel,
    Throw,
    Delay
}

public sealed class TestStubConfigurationCapability : IPluginConfigurationCapability
{
    public static TestStubMode Mode { get; set; } = TestStubMode.Success;

    public PluginConfigurationDescriptor Configuration => new("tests.sample.config", "Test Stub Configuration");

    public async Task<PluginConfigurationResult> ConfigureAsync(
        PluginConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        switch (Mode)
        {
            case TestStubMode.Cancel:
                return new PluginConfigurationResult
                {
                    Success = true,
                    Canceled = true,
                    Settings = new Dictionary<string, string?> { ["greeting"] = "New Greeting" }
                };
            case TestStubMode.Throw:
                throw new InvalidOperationException("Test plugin failure during configuration");
            case TestStubMode.Delay:
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                return new PluginConfigurationResult { Success = true };
            default:
                return new PluginConfigurationResult
                {
                    Success = true,
                    Settings = new Dictionary<string, string?> { ["greeting"] = "Test" }
                };
        }
    }
}
