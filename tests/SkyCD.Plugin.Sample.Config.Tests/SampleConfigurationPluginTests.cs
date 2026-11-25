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
using SkyCD.SDK;
using SkyCD.SDK.Capabilities.Configuration;
using SkyCD.Plugin.Runtime.Configuration;
using SkyCD.Plugin.Runtime.Documents;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.Sample.Config;
using Xunit;
using SkyCD.Plugin.Sample.Config.Configuration;

namespace SkyCD.Plugin.Sample.Config.Tests;

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
    }

    [Fact]
    public void PluginManager_SkipsAssembly_WhenConfigurationCapabilityCannotBeRead()
    {
        CopyPluginAssembly(typeof(SampleConfigurationPluginTests).Assembly);
        var testPluginId = typeof(SampleConfigurationPluginTests).Assembly.GetName().Name!;
        TestStubConfigurationCapability.ThrowOnConfigurationRead = true;

        try
        {
            var exception = Record.Exception(() => pluginManager.Discover(pluginsDirectory, new Version(3, 0, 0)));

            Assert.Null(exception);
            Assert.DoesNotContain(pluginManager.AvailablePlugins, plugin => plugin.Id == testPluginId);
            Assert.DoesNotContain(configurationManager.GetRegistrations(includeDisabled: true),
                registration => registration.PluginId == testPluginId);
        }
        finally
        {
            TestStubConfigurationCapability.ThrowOnConfigurationRead = false;
        }
    }

    [Fact]
    public void GetRegistrations_IsolatesDescriptorFailure_WithoutLosingOtherPlugins()
    {
        CopyPluginAssembly(typeof(SampleConfigurationPlugin).Assembly);
        CopyPluginAssembly(typeof(SampleConfigurationPluginTests).Assembly);
        pluginManager.Discover(pluginsDirectory, new Version(3, 0, 0));

        var testPluginId = typeof(SampleConfigurationPluginTests).Assembly.GetName().Name!;
        Assert.Contains(configurationManager.GetRegistrations(includeDisabled: true),
            registration => registration.PluginId == testPluginId);

        PluginFailure? reportedFailure = null;
        Action<PluginFailure> handler = failure => reportedFailure = failure;
        PluginFailure.Reported += handler;
        TestStubConfigurationCapability.ThrowOnConfigurationRead = true;

        try
        {
            var registrations = configurationManager.GetRegistrations(includeDisabled: true);

            Assert.DoesNotContain(registrations, registration => registration.PluginId == testPluginId);
            Assert.Contains(registrations, registration => registration.PluginId == "SkyCD.Plugin.Sample.Configuration");
            Assert.NotNull(reportedFailure);
            Assert.Equal("configure", reportedFailure.Operation);
        }
        finally
        {
            TestStubConfigurationCapability.ThrowOnConfigurationRead = false;
            PluginFailure.Reported -= handler;
        }
    }

    [Fact]
    public async Task PageSession_SavesSettings_WhenSuccessful()
    {
        CopyPluginAssembly(typeof(SampleConfigurationPlugin).Assembly);

        pluginManager.Discover(pluginsDirectory, new Version(3, 0, 0));

        var outcome = await configurationManager.CreatePageAsync("SkyCD.Plugin.Sample.Configuration", TimeSpan.FromSeconds(5));
        using var page = outcome.Page!;
        outcome = await configurationManager.AcceptPageAsync("SkyCD.Plugin.Sample.Configuration", page, TimeSpan.FromSeconds(5));

        Assert.Equal(PluginConfigurationStatus.Succeeded, outcome.Status);
        Assert.NotNull(outcome.Settings);
        Assert.Equal(SampleConfigurationEditor.DefaultGreeting, outcome.Settings[SampleConfigurationEditor.GreetingKey]);
        Assert.Equal(SampleConfigurationEditor.DefaultSignature, outcome.Settings[SampleConfigurationEditor.SignatureKey]);

        Assert.Empty(configurationStore.GetSettings("SkyCD.Plugin.Sample.Configuration"));
        configurationManager.SaveSettings(new Dictionary<string, IReadOnlyDictionary<string, string?>>
        {
            ["SkyCD.Plugin.Sample.Configuration"] = outcome.Settings
        });
        // Verify only explicit Save persists the draft.
        var persisted = configurationStore.GetSettings("SkyCD.Plugin.Sample.Configuration");
        Assert.Equal(SampleConfigurationEditor.DefaultGreeting, persisted[SampleConfigurationEditor.GreetingKey]);
        Assert.Equal(SampleConfigurationEditor.DefaultSignature, persisted[SampleConfigurationEditor.SignatureKey]);
    }

    [Fact]
    public async Task PageSession_PassesExistingSettingsToPlugin()
    {
        CopyPluginAssembly(typeof(SampleConfigurationPlugin).Assembly);

        // Pre-seed configuration
        configurationStore.SaveSettings("SkyCD.Plugin.Sample.Configuration", new Dictionary<string, string?>
        {
            [SampleConfigurationEditor.GreetingKey] = "Custom Greeting",
            [SampleConfigurationEditor.SignatureKey] = "Custom Signature"
        });

        pluginManager.Discover(pluginsDirectory, new Version(3, 0, 0));

        var outcome = await configurationManager.CreatePageAsync("SkyCD.Plugin.Sample.Configuration", TimeSpan.FromSeconds(5));
        using var page = outcome.Page!;
        outcome = await configurationManager.AcceptPageAsync("SkyCD.Plugin.Sample.Configuration", page, TimeSpan.FromSeconds(5));

        Assert.Equal(PluginConfigurationStatus.Succeeded, outcome.Status);
        Assert.NotNull(outcome.Settings);
        Assert.Equal("Custom Greeting", outcome.Settings[SampleConfigurationEditor.GreetingKey]);
        Assert.Equal("Custom Signature", outcome.Settings[SampleConfigurationEditor.SignatureKey]);
    }

    [Fact]
    public async Task PageSession_DoesNotPersistSettings_WhenCanceled()
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

        var outcome = await configurationManager.CreatePageAsync(pluginId, TimeSpan.FromSeconds(5));
        using var page = outcome.Page!;
        outcome = await configurationManager.AcceptPageAsync(pluginId, page, TimeSpan.FromSeconds(5));

        Assert.Equal(PluginConfigurationStatus.Canceled, outcome.Status);

        // Settings should remain unchanged
        var persisted = configurationStore.GetSettings(pluginId);
        Assert.Equal("Original Greeting", persisted["greeting"]);
    }

    [Fact]
    public async Task PageSession_ReturnsNotSupported_ForUnsupportedPlugin()
    {
        CopyPluginAssembly(typeof(SampleConfigurationPlugin).Assembly);

        pluginManager.Discover(pluginsDirectory, new Version(3, 0, 0));

        var outcome = await configurationManager.CreatePageAsync("SkyCD.Plugin.Unsupported", TimeSpan.FromSeconds(5));

        Assert.Equal(PluginConfigurationStatus.NotSupported, outcome.Status);
        Assert.Contains("does not provide a configuration surface", outcome.Error);
    }

    [Fact]
    public async Task PageSession_SurfacesPluginFailures_WithoutCrashingHost()
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

            var outcome = await configurationManager.CreatePageAsync(pluginId, TimeSpan.FromSeconds(5));

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
    public async Task PageSession_SurfacesPluginTimeout_WithoutCrashingHost()
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

            var outcome = await configurationManager.CreatePageAsync(pluginId, TimeSpan.FromMilliseconds(50));

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

    [Fact]
    public async Task PageSession_CallerCancellationPropagatesWithoutSaving()
    {
        CopyPluginAssembly(Assembly.GetExecutingAssembly());
        pluginManager.Discover(pluginsDirectory, new Version(3, 0, 0));
        TestStubConfigurationCapability.Mode = TestStubMode.Delay;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => configurationManager.CreatePageAsync(
            Assembly.GetExecutingAssembly().GetName().Name!, TimeSpan.FromSeconds(5), cancellation.Token));
    }

    [Fact]
    public async Task PageSession_TimeoutDisposesLatePageFromCancellationIgnoringPlugin()
    {
        CopyPluginAssembly(Assembly.GetExecutingAssembly());
        pluginManager.Discover(pluginsDirectory, new Version(3, 0, 0));
        TestStubConfigurationCapability.Mode = TestStubMode.Deferred;
        var deferred = new TaskCompletionSource<PluginConfigurationPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        TestStubConfigurationCapability.DeferredPage = deferred.Task;
        var outcome = await configurationManager.CreatePageAsync(Assembly.GetExecutingAssembly().GetName().Name!,
            TimeSpan.FromMilliseconds(50));
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        deferred.SetResult(new PluginConfigurationPage(() => new object(),
            _ => Task.FromResult(new PluginConfigurationResult { Success = true }), () => disposed.SetResult()));
        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PluginConfigurationStatus.TimedOut, outcome.Status);
        Assert.Empty(configurationStore.GetSettings(Assembly.GetExecutingAssembly().GetName().Name!));
    }

    [Fact]
    public void Store_BatchFailureRollsBackEarlierWrites()
    {
        configurationStore.SaveSettings("first", new Dictionary<string, string?> { ["value"] = "original" });
        Assert.Throws<ArgumentException>(() => configurationStore.SaveBatch(
            new Dictionary<string, IReadOnlyDictionary<string, string?>>
            {
                ["first"] = new Dictionary<string, string?> { ["value"] = "changed" },
                [""] = new Dictionary<string, string?> { ["value"] = "invalid plugin id" }
            }));
        Assert.Equal("original", configurationStore.GetSettings("first")["value"]);
    }

    [Fact]
    public async Task Page_DisposeWithoutContentOrAccept_IsHeadlessAndDoesNotSave()
    {
        var plugin = new SampleConfigurationPlugin();
        using var page = await plugin.CreatePageAsync(new PluginConfigurationRequest
        {
            ConfigurationId = plugin.Configuration.ConfigurationId,
            CurrentSettings = new Dictionary<string, string?> { ["greeting"] = "draft" }
        });
        page.Dispose();
        Assert.Throws<ObjectDisposedException>(() => { _ = page.AcceptAsync(); });
        Assert.Empty(configurationStore.GetSettings("SkyCD.Plugin.Sample.Configuration"));
    }

    public void Dispose()
    {
        databaseManager.GetDatabase().Dispose();
        databaseManager.Dispose();
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
    Delay,
    Deferred
}

public sealed class TestStubConfigurationCapability : IPluginConfigurationCapability
{
    public static TestStubMode Mode { get; set; } = TestStubMode.Success;
    public static bool ThrowOnConfigurationRead { get; set; }
    public static Task<PluginConfigurationPage>? DeferredPage { get; set; }

    public PluginConfigurationDescriptor Configuration => ThrowOnConfigurationRead
        ? throw new MissingMethodException(
            "Method not found: 'Void SkyCD.SDK.Capabilities.Configuration.PluginConfigurationDescriptor..ctor(System.String, System.String)'.")
        : new PluginConfigurationDescriptor("tests.sample.config", "Test Stub Configuration");

    public Task<PluginConfigurationPage> CreatePageAsync(PluginConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (Mode == TestStubMode.Deferred) return DeferredPage!;
        if (Mode == TestStubMode.Throw) throw new InvalidOperationException("Test plugin failure during configuration");
        if (Mode == TestStubMode.Delay) return DelayedPageAsync(cancellationToken);
        return Task.FromResult(new PluginConfigurationPage(() => new object(), token => AcceptAsync(request, token)));
    }

    private static async Task<PluginConfigurationPage> DelayedPageAsync(CancellationToken token)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), token);
        return new PluginConfigurationPage(() => new object(), _ => Task.FromResult(new PluginConfigurationResult { Success = true }));
    }

    private static async Task<PluginConfigurationResult> AcceptAsync(
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
