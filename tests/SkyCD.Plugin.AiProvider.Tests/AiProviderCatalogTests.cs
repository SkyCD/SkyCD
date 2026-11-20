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
        Assert.Null(AiProviderCatalog.ServerCommand("openai"));
        Assert.Null(AiProviderCatalog.ServerCommand("custom"));
    }

    [Fact]
    public void Find_IsCaseInsensitive()
    {
        Assert.Equal("Ollama", AiProviderCatalog.Find("OLLAMA")?.Name);
        Assert.Null(AiProviderCatalog.Find("missing"));
    }
}
