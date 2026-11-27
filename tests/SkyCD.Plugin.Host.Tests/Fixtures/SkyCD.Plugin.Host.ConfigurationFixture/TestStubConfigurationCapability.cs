using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Configuration;

namespace SkyCD.Plugin.Host.Tests.Samples;

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

    public IReadOnlyList<PluginConfigurationDescriptor> Configurations => ThrowOnConfigurationRead
        ? throw new MissingMethodException(
            "Method not found: 'Void SkyCD.SDK.Capabilities.Configuration.PluginConfigurationDescriptor..ctor(System.String, System.String)'.")
        : [new PluginConfigurationDescriptor("tests.sample.config", "Test Stub Configuration")];

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
