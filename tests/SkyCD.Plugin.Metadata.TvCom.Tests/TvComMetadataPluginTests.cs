using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.OnlineMetadata.Models;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;
using SkyCD.TestSupport;

namespace SkyCD.Plugin.Metadata.TvCom.Tests;

public class TvComMetadataPluginTests
{
    private static TvComMetadataPlugin Plugin(string body = "") => new(new StubFetcher(body));

    [Fact]
    public void ReadsMetaAndJsonLd()
    {
        var facts = new DescriptionFacts();
        Plugin().Parse(OnlineMetadataFixtures.Read("tvcom-show.html"), facts);

        Assert.Equal("Breaking Bad", facts.Get("Title"));
        Assert.Equal("Crime|Drama|Thriller", facts.Get("Genre"));
        Assert.Equal("2008", facts.Get("Year"));
        Assert.Equal("62", facts.Get("Episodes"));
        Assert.Equal("Vince Gilligan", facts.Get("Director"));
    }

    [Fact]
    public void UsesAnExplicitShowUrl()
    {
        Assert.Equal("https://www.tv.com/shows/breaking-bad/",
            Plugin().ResolveUrl(new OnlineMetadataRequest("Breaking Bad", "https://www.tv.com/shows/breaking-bad/", "en")));
    }

    [Fact]
    public void AcceptsTheCbsShowHost()
    {
        Assert.Equal("https://www.cbs.com/shows/breaking-bad/",
            Plugin().ResolveUrl(new OnlineMetadataRequest("", "https://www.cbs.com/shows/breaking-bad/", "en")));
    }

    [Fact]
    public void QueryFallsBackToTheSearchPage()
    {
        Assert.Equal("https://www.tv.com/search/?q=Breaking%20Bad",
            Plugin().ResolveUrl(new OnlineMetadataRequest("Breaking Bad", null, "en")));
    }

    [Fact]
    public void EmptyRequestResolvesNothing()
    {
        Assert.Null(Plugin().ResolveUrl(new OnlineMetadataRequest("", null, "en")));
    }

    [Fact]
    public async Task FetchReturnsFactsAndTheFetchedUrl()
    {
        var fetcher = new StubFetcher(OnlineMetadataFixtures.Read("tvcom-show.html"));
        var plugin = new TvComMetadataPlugin(fetcher);
        var request = new OnlineMetadataRequest("", "https://www.tv.com/shows/breaking-bad/", "en");

        var result = await plugin.FetchAsync(request, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("tvcom", result.SourceId);
        Assert.Equal(request.Url, result.SourceUrl);
        Assert.Equal(request.Url, fetcher.Requested.Single());
        Assert.Equal("Breaking Bad", result.Facts["Title"]);
    }

    [Fact]
    public async Task FetchConvertsFetcherFailureIntoAResult()
    {
        var result = await new TvComMetadataPlugin(new StubFetcher(_ => null!))
            .FetchAsync(new OnlineMetadataRequest("Breaking Bad", null, "en"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("HttpRequestException", result.Error);
    }

    [Fact]
    public void AssemblyIsDiscoveredAsAnOnlineMetadataSource()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(TvComMetadataPlugin).Assembly);

        Assert.Equal("skycd.metadata-tvcom", plugin.Id);
        var capability = Assert.IsType<TvComMetadataPlugin>(
            Assert.Single(plugin.Capabilities.OfType<IOnlineMetadataSourceCapability>()));
        Assert.Equal("tvcom", capability.SourceId);
        Assert.Contains("cbs.com", capability.KnownHosts);
    }
}
