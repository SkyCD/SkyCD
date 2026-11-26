using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.OnlineMetadata.Models;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;
using SkyCD.TestSupport;

namespace SkyCD.Plugin.Metadata.AniDb.Tests;

public class AniDbMetadataPluginTests
{
    private static AniDbMetadataPlugin Plugin(string body = "") => new(new StubFetcher(body));

    [Fact]
    public void ReadsInfoboxDefinitionList()
    {
        var facts = new DescriptionFacts();
        Plugin().Parse(OnlineMetadataFixtures.Read("anidb-anime.html"), facts);

        Assert.Equal("Neon Genesis Evangelion", facts.Get("Title"));
        Assert.Equal("TV", facts.Get("Type"));
        Assert.Equal("26", facts.Get("Episodes"));
        Assert.Equal("24 min per ep", facts.Get("Runtime"));
        Assert.Equal("Anno Hideaki", facts.Get("Director"));
        Assert.Equal("Gainax", facts.Get("Studio"));
        Assert.Contains("giant cyborgs", facts.Get("Plot"));
    }

    [Fact]
    public void UsesAnExplicitAnimeUrl()
    {
        Assert.Equal("https://anidb.net/anime/1",
            Plugin().ResolveUrl(new OnlineMetadataRequest("Evangelion", "https://anidb.net/anime/1", "en")));
    }

    [Fact]
    public void AcceptsTheMirrorHost()
    {
        Assert.Equal("https://anidb.info/anime/1",
            Plugin().ResolveUrl(new OnlineMetadataRequest("", "https://anidb.info/anime/1", "en")));
    }

    [Fact]
    public void QueryFallsBackToTheSearchPage()
    {
        Assert.Equal("https://anidb.net/search/?q=Neon%20Genesis%20Evangelion",
            Plugin().ResolveUrl(new OnlineMetadataRequest("Neon Genesis Evangelion", null, "en")));
    }

    [Fact]
    public void EmptyRequestResolvesNothing()
    {
        Assert.Null(Plugin().ResolveUrl(new OnlineMetadataRequest("", null, "en")));
    }

    [Fact]
    public async Task FetchReturnsFactsAndTheFetchedUrl()
    {
        var fetcher = new StubFetcher(OnlineMetadataFixtures.Read("anidb-anime.html"));
        var plugin = new AniDbMetadataPlugin(fetcher);
        var request = new OnlineMetadataRequest("", "https://anidb.net/anime/1", "en");

        var result = await plugin.FetchAsync(request, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("anidb", result.SourceId);
        Assert.Equal(request.Url, result.SourceUrl);
        Assert.Equal(request.Url, fetcher.Requested.Single());
        Assert.Equal("Neon Genesis Evangelion", result.Facts["Title"]);
    }

    [Fact]
    public async Task FetchConvertsFetcherFailureIntoAResult()
    {
        var result = await new AniDbMetadataPlugin(new StubFetcher(_ => null!))
            .FetchAsync(new OnlineMetadataRequest("Evangelion", null, "en"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("HttpRequestException", result.Error);
    }

    [Fact]
    public void AssemblyIsDiscoveredAsAnOnlineMetadataSource()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(AniDbMetadataPlugin).Assembly);

        Assert.Equal("skycd.metadata-anidb", plugin.Id);
        var capability = Assert.IsType<AniDbMetadataPlugin>(
            Assert.Single(plugin.Capabilities.OfType<IOnlineMetadataSourceCapability>()));
        Assert.Equal("anidb", capability.SourceId);
        Assert.Contains("anidb.net", capability.KnownHosts);
    }
}
