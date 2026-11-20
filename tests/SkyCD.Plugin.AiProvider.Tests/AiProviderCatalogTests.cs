using Xunit;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.AiProvider.Tests;

public sealed class AiProviderCatalogTests
{
    [Fact]
    public void BuildEndpoint_UsesProtocolAddressPortAndVersionPath()
    {
        var settings = new AiProviderSettings
        {
            Protocol = "http",
            Address = "127.0.0.1",
            Port = 11434
        };

        var endpoint = AiProviderCatalog.BuildEndpoint(settings);

        Assert.Equal("http://127.0.0.1:11434/v1", endpoint);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("localhost", true)]
    [InlineData("LOCALHOST", true)]
    [InlineData("::1", true)]
    [InlineData("api.openai.com", false)]
    public void IsLoopback_DetectsLocalHosts(string address, bool expected)
    {
        var settings = new AiProviderSettings { Address = address };

        Assert.Equal(expected, AiProviderCatalog.IsLoopback(settings));
    }

    [Fact]
    public void ServerCommand_IsAllowlistedPerProvider()
    {
        Assert.Equal(("ollama", "serve"), AiProviderCatalog.ServerCommand("ollama"));
        Assert.Equal(("lms", "server start"), AiProviderCatalog.ServerCommand("lmstudio"));
        Assert.Equal(("coli", "serve"), AiProviderCatalog.ServerCommand("colibri"));
        Assert.Null(AiProviderCatalog.ServerCommand("llamacpp"));
        Assert.Null(AiProviderCatalog.ServerCommand("custom"));
    }

    [Fact]
    public void Providers_AreLocalServicesOnly()
    {
        Assert.All(AiProviderCatalog.Providers, provider =>
        {
            Assert.Equal("http", provider.Protocol);
            Assert.True(AiProviderCatalog.IsLoopback(new AiProviderSettings { Address = provider.Address }),
                provider.Id);
        });
        Assert.Contains(AiProviderCatalog.Providers, provider => provider.Id == "colibri");
        Assert.Contains(AiProviderCatalog.Providers, provider => provider.Id == "llamacpp");
        Assert.Contains(AiProviderCatalog.Providers, provider => provider.Id == "ollama");
        Assert.Contains(AiProviderCatalog.Providers, provider => provider.Id == "lmstudio");
        Assert.DoesNotContain(AiProviderCatalog.Providers, provider => provider.Id == "openai");
    }

    [Fact]
    public void SwitchingProviders_OffersSuggestedModels()
    {
        Assert.NotEmpty(AiProviderCatalog.Find("ollama")!.SuggestedModels!);
        Assert.Equal("llama3.2", AiProviderCatalog.Find("ollama")!.DefaultModel);
        Assert.NotEmpty(AiProviderCatalog.Find("colibri")!.SuggestedModels!);
    }

    [Fact]
    public void Find_IsCaseInsensitive()
    {
        Assert.Equal("Ollama", AiProviderCatalog.Find("OLLAMA")?.Name);
        Assert.Null(AiProviderCatalog.Find("missing"));
    }
}
