using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Ai.Provider.Exceptions;

namespace SkyCD.Plugin.Ai.Provider.Tests;

public sealed class AiProviderPluginTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(),
        "skycd-ai-provider-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private AiProviderPlugin CreatePlugin() => new(Path.Combine(directory, "provider.json"));

    [Fact]
    public void Plugin_ImplementsSelectableProviderImplementation()
    {
        var plugin = CreatePlugin();

        Assert.Equal(AiDescriptionImplementationIds.Provider, plugin.ImplementationId);
        Assert.Equal("AI provider", plugin.ImplementationName);
        Assert.Contains(plugin.Providers, provider => provider.Id == "lmstudio");
        Assert.Contains(plugin.Providers, provider => provider.Id == "colibri");
    }

    [Fact]
    public void GetPlan_WhenNotConfigured_ReportsDisabled()
    {
        var plugin = CreatePlugin();

        var plan = plugin.GetPlan();

        Assert.False(plan.IsEnabled);
        Assert.NotNull(plan.UnavailableReason);
        Assert.Contains("disabled", plan.UnavailableReason);
    }

    [Fact]
    public void GetPlan_WhenLocalProviderEnabled_ReportsLocalInference()
    {
        var plugin = CreatePlugin();
        plugin.SaveSettings(new AiProviderSettings { Enabled = true });

        var plan = plugin.GetPlan();

        Assert.True(plan.IsEnabled);
        Assert.Equal("LM Studio", plan.ProviderName);
        Assert.Equal("local-inference", plan.ProcessingDestination);
        Assert.Equal("google/gemma-4-e2b", plan.Model);
        Assert.Null(plan.UnavailableReason);
        Assert.True(plan.ContextOptions.Filename);
        Assert.True(plan.ContextOptions.Images);
    }

    [Fact]
    public void GetPlan_WhenCustomRemoteAddress_ReportsCloudDestination()
    {
        var plugin = CreatePlugin();
        plugin.SaveSettings(new AiProviderSettings
        {
            Enabled = true,
            ProviderId = "custom",
            Protocol = "http",
            Address = "192.168.1.50",
            Port = 8000,
            Model = "local-model"
        });

        var plan = plugin.GetPlan();

        Assert.True(plan.IsEnabled);
        Assert.Equal("cloud", plan.ProcessingDestination);
    }

    [Fact]
    public void GetPlan_WhenConfigurationInvalid_ReportsUnavailable()
    {
        var plugin = CreatePlugin();
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "provider.json"), "{ broken");

        var plan = plugin.GetPlan();

        Assert.False(plan.IsEnabled);
        Assert.NotNull(plan.UnavailableReason);
    }

    [Fact]
    public void SaveSettings_WhenModelMissing_Throws()
    {
        var plugin = CreatePlugin();

        Assert.Throws<AiProviderException>(() =>
            plugin.SaveSettings(new AiProviderSettings { Model = string.Empty }));
    }

    [Fact]
    public void BuildEndpointUrl_IncludesVersionPath()
    {
        var plugin = CreatePlugin();

        var url = plugin.BuildEndpointUrl(new AiProviderSettings());

        Assert.Equal("http://127.0.0.1:1234/v1", url);
    }

    [Fact]
    public async Task StartServerAsync_WhenProviderDoesNotSupportIt_ReturnsMessage()
    {
        var plugin = CreatePlugin();

        var error = await plugin.StartServerAsync(new AiProviderSettings { ProviderId = "llamacpp" },
            CancellationToken.None);

        Assert.NotNull(error);
        Assert.Contains("does not support", error);
    }

    [Fact]
    public async Task StartServerAsync_WhenProviderUnknown_ReturnsMessage()
    {
        var plugin = CreatePlugin();

        var error = await plugin.StartServerAsync(new AiProviderSettings { ProviderId = "nope" },
            CancellationToken.None);

        Assert.Equal("Select a supported provider.", error);
    }
}
