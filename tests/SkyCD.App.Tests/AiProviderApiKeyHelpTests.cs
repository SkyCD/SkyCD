using System;
using SkyCD.Plugin.Ai.Provider.Models;
using SkyCD.Presentation.Plugins;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #1209: the Options AI provider page offers a documentation link under the API key
/// field. Provider data is third-party, so only absolute http(s) URLs may surface or launch.
/// </summary>
public sealed class AiProviderApiKeyHelpTests
{
    [Fact]
    public void Catalog_HelpUrlsAreAbsoluteHttpOrHttps()
    {
        Assert.All(AiProviderCatalog.Providers, provider =>
        {
            if (provider.ApiKeyHelpUrl is null)
            {
                return;
            }

            Assert.True(Uri.TryCreate(provider.ApiKeyHelpUrl, UriKind.Absolute, out var uri));
            Assert.True(uri!.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps,
                $"Unexpected scheme for {provider.Id}: {uri.Scheme}");
            Assert.True(ExternalLinkLauncher.IsLaunchable(provider.ApiKeyHelpUrl));
        });
    }

    [Theory]
    [InlineData("lmstudio")]
    [InlineData("llamacpp")]
    [InlineData("koboldcpp")]
    [InlineData("localai")]
    [InlineData("vllm")]
    public void Catalog_DocumentsApiKeyAcquisition(string providerId)
    {
        var provider = AiProviderCatalog.Find(providerId);

        Assert.NotNull(provider);
        Assert.NotNull(provider!.ApiKeyHelpUrl);
    }

    [Theory]
    [InlineData("ollama")]
    [InlineData("colibri")]
    [InlineData("jan")]
    [InlineData("custom")]
    public void Catalog_OmitsHelpUrlForProvidersWithoutKeyConcept(string providerId)
    {
        var provider = AiProviderCatalog.Find(providerId);

        Assert.NotNull(provider);
        Assert.Null(provider!.ApiKeyHelpUrl);
    }
}
