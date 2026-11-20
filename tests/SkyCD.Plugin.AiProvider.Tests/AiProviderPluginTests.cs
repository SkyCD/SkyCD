using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.AiProvider.Tests;

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
        Assert.Contains(plugin.Providers, provider => provider.Id == "openai");
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
    public void GetPlan_WhenCloudProviderEnabled_ReportsCloudDestination()
    {
        var plugin = CreatePlugin();
        plugin.SaveSettings(new AiProviderSettings { Enabled = true, ApiKey = "key" });

        var plan = plugin.GetPlan();

        Assert.True(plan.IsEnabled);
        Assert.Equal("OpenAI", plan.ProviderName);
        Assert.Equal("cloud", plan.ProcessingDestination);
        Assert.Equal("gpt-4o-mini", plan.Model);
        Assert.Null(plan.UnavailableReason);
        Assert.True(plan.ContextOptions.Filename);
        Assert.True(plan.ContextOptions.Images);
    }

    [Fact]
    public void GetPlan_WhenLoopbackProviderEnabled_ReportsLocalInference()
    {
        var plugin = CreatePlugin();
        plugin.SaveSettings(new AiProviderSettings
        {
            Enabled = true,
            ProviderId = "ollama",
            Protocol = "http",
            Address = "127.0.0.1",
            Port = 11434,
            Model = "llama3.2"
        });

        var plan = plugin.GetPlan();

        Assert.True(plan.IsEnabled);
        Assert.Equal("local-inference", plan.ProcessingDestination);
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

        Assert.Equal("https://api.openai.com:443/v1", url);
    }

    [Fact]
    public async Task StartServerAsync_WhenProviderDoesNotSupportIt_ReturnsMessage()
    {
        var plugin = CreatePlugin();

        var error = await plugin.StartServerAsync(new AiProviderSettings(), CancellationToken.None);

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
