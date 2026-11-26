using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.OnlineMetadata.Models;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;
using SkyCD.TestSupport;

namespace SkyCD.Plugin.Metadata.AnimeNewsNetwork.Tests;

public class AnimeNewsNetworkMetadataPluginTests
{
    private static AnimeNewsNetworkMetadataPlugin Plugin(string body = "") => new(new StubFetcher(body));

    [Fact]
    public void ReadsProfileTable()
    {
        var facts = new DescriptionFacts();
        Plugin().Parse(OnlineMetadataFixtures.Read("ann-encyclopedia.html"), facts);

        Assert.Equal("Cowboy Bebop", facts.Get("Title"));
        Assert.Equal("TV", facts.Get("Type"));
        Assert.Equal("26", facts.Get("Episodes"));
        Assert.Equal("Sunrise", facts.Get("Studio"));
        Assert.Equal("Watanabe Shinichiro", facts.Get("Director"));
        Assert.Contains("bounty hunters", facts.Get("Plot"));
    }

    [Fact]
    public void UsesAnExplicitEncyclopediaUrl()
    {
        Assert.Equal("https://www.animenewsnetwork.com/encyclopedia/anime.php?id=1",
            Plugin().ResolveUrl(new OnlineMetadataRequest("Cowboy Bebop",
                "https://www.animenewsnetwork.com/encyclopedia/anime.php?id=1", "en")));
    }

    [Fact]
    public void QueryFallsBackToTheSearchPage()
    {
        Assert.Equal("https://www.animenewsnetwork.com/encyclopedia/search.php?query=Cowboy%20Bebop",
            Plugin().ResolveUrl(new OnlineMetadataRequest("Cowboy Bebop", null, "en")));
    }

    [Fact]
    public void EmptyRequestResolvesNothing()
    {
        Assert.Null(Plugin().ResolveUrl(new OnlineMetadataRequest("", null, "en")));
    }

    [Fact]
    public async Task FetchReturnsFactsAndTheFetchedUrl()
    {
        var fetcher = new StubFetcher(OnlineMetadataFixtures.Read("ann-encyclopedia.html"));
        var plugin = new AnimeNewsNetworkMetadataPlugin(fetcher);
        var request = new OnlineMetadataRequest("", "https://www.animenewsnetwork.com/encyclopedia/anime.php?id=1", "en");

        var result = await plugin.FetchAsync(request, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("ann", result.SourceId);
        Assert.Equal(request.Url, result.SourceUrl);
        Assert.Equal(request.Url, fetcher.Requested.Single());
        Assert.Equal("Cowboy Bebop", result.Facts["Title"]);
    }

    [Fact]
    public async Task FetchConvertsFetcherFailureIntoAResult()
    {
        var result = await new AnimeNewsNetworkMetadataPlugin(new StubFetcher(_ => null!))
            .FetchAsync(new OnlineMetadataRequest("Cowboy Bebop", null, "en"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("HttpRequestException", result.Error);
    }

    [Fact]
    public void AssemblyIsDiscoveredAsAnOnlineMetadataSource()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(AnimeNewsNetworkMetadataPlugin).Assembly);

        Assert.Equal("skycd.metadata-ann", plugin.Id);
        var capability = Assert.IsType<AnimeNewsNetworkMetadataPlugin>(
            Assert.Single(plugin.Capabilities.OfType<IOnlineMetadataSourceCapability>()));
        Assert.Equal("ann", capability.SourceId);
        Assert.Equal("Anime News Network", capability.DisplayName);
    }
}
