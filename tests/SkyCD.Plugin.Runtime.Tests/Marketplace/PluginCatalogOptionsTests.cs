using System;
using System.Linq;
using SkyCD.Plugin.Runtime.Marketplace.Models;
using SkyCD.Plugin.Runtime.Marketplace.Services;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests.Marketplace;

/// <summary>The shipped catalog address and the recognition rule that keeps it out of the repository list.</summary>
public sealed class PluginCatalogOptionsTests
{
    [Fact]
    public void DefaultCatalogEndpoint_IsTheGitHubPagesCatalog()
    {
        Assert.Equal("https://skycd.github.io/awesome-skycd/plugins.json",
            PluginCatalogOptions.DefaultCatalogEndpoint.AbsoluteUri);
        Assert.DoesNotContain("raw.githubusercontent.com", PluginCatalogOptions.DefaultCatalogEndpoint.AbsoluteUri,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PluginCatalogOptions.DefaultCatalogEndpoint,
            Assert.Single(new PluginCatalogOptions().CatalogEndpoints));
    }

    [Fact]
    public void RetiredCatalogEndpoint_IsRecognizedAsBuiltIn()
    {
        Assert.True(PluginCatalogOptions.IsBuiltInCatalogEndpoint(PluginCatalogOptions.DefaultCatalogEndpoint));
        Assert.True(PluginCatalogOptions.IsBuiltInCatalogEndpoint(PluginCatalogOptions.RetiredCatalogEndpoint));
    }

    [Theory]
    [InlineData("HTTPS://SkyCD.github.io/awesome-skycd/plugins.json")]
    [InlineData("https://raw.githubusercontent.com/skycd/awesome-skycd/main/plugins.json")]
    public void IsBuiltInCatalogEndpoint_IgnoresCase(string url)
    {
        Assert.True(PluginCatalogOptions.IsBuiltInCatalogEndpoint(new Uri(url)));
    }

    [Theory]
    [InlineData("https://repo.example/plugins.json")]
    [InlineData("https://skycd.github.io/awesome-skycd/other.json")]
    [InlineData("https://raw.githubusercontent.com/SkyCD/awesome-skycd/main/manifest.json")]
    public void IsBuiltInCatalogEndpoint_AcceptsPublisherManifests(string url)
    {
        Assert.False(PluginCatalogOptions.IsBuiltInCatalogEndpoint(new Uri(url)));
    }

    [Fact]
    public void RepositoryStore_DropsBuiltInCatalogAddressesAndKeepsUserEntries()
    {
        var store = new PluginRepositorySourceStore();

        Assert.True(store.SetSources([
            PluginCatalogOptions.RetiredCatalogEndpoint,
            PluginCatalogOptions.DefaultCatalogEndpoint,
            new Uri("https://repo.example/plugins.json")
        ]));

        Assert.Equal(["https://repo.example/plugins.json"],
            store.Sources.Select(static source => source.AbsoluteUri));
    }
}
