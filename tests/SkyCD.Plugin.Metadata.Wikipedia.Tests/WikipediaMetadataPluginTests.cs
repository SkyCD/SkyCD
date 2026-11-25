using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.OnlineMetadata.Models;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;

namespace SkyCD.Plugin.Metadata.Wikipedia.Tests;

public class WikipediaMetadataPluginTests
{
    private static WikipediaMetadataPlugin Plugin(string body = "") =>
        new(new StubFetcher(body));

    [Fact]
    public void ActionApiExtractsTitlePlotAndImage()
    {
        var facts = new DescriptionFacts();
        Plugin().Parse(Fixtures.Read("wikipedia-action.json"), facts);

        Assert.Equal("Example Film", facts.Get("Title"));
        Assert.Contains("directed by Jane Doe", facts.Get("Plot"));
        Assert.Contains("example.png", facts.Get("Image"));
        Assert.False(facts.Has("Genre"));
    }

    [Fact]
    public void SummaryExtractsDescriptionAsGenre()
    {
        var facts = new DescriptionFacts();
        Plugin().Parse(Fixtures.Read("wikipedia-summary.json"), facts);

        Assert.Equal("Example Film", facts.Get("Title"));
        Assert.Equal("1999 film", facts.Get("Genre"));
        Assert.Contains("feature directed by Jane Doe", facts.Get("Plot"));
    }

    [Fact]
    public void ResolvesSummaryFromArticleUrl()
    {
        var url = Plugin().ResolveUrl(new OnlineMetadataRequest("", "https://en.wikipedia.org/wiki/Example_Film", "en"));

        Assert.NotNull(url);
        Assert.Contains("/api/rest_v1/page/summary/Example%20Film", url);
    }

    [Fact]
    public void QueryUsesConfiguredLanguage()
    {
        var url = Plugin().ResolveUrl(new OnlineMetadataRequest("Example Film", null, "LT"));

        Assert.NotNull(url);
        Assert.StartsWith("https://lt.wikipedia.org/w/api.php", url);
        Assert.Contains("titles=Example%20Film", url);
    }

    [Fact]
    public void EmptyRequestResolvesNothing()
    {
        Assert.Null(Plugin().ResolveUrl(new OnlineMetadataRequest("", null, "en")));
    }

    [Fact]
    public async Task FetchReturnsFactsAndTheFetchedUrl()
    {
        var fetcher = new StubFetcher(Fixtures.Read("wikipedia-summary.json"));
        var plugin = new WikipediaMetadataPlugin(fetcher);
        var request = new OnlineMetadataRequest("", "https://en.wikipedia.org/wiki/Example_Film", "en");

        var result = await plugin.FetchAsync(request, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("wikipedia", result.SourceId);
        Assert.Equal(fetcher.Requested.Single(), result.SourceUrl);
        Assert.Contains("/api/rest_v1/page/summary/", result.SourceUrl);
        Assert.Equal("Example Film", result.Facts["Title"]);
    }

    [Fact]
    public async Task FetchWithoutALookupFailsWithoutFetching()
    {
        var fetcher = new StubFetcher("{}");
        var result = await new WikipediaMetadataPlugin(fetcher)
            .FetchAsync(new OnlineMetadataRequest(null, null, "en"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.SourceUrl);
        Assert.Empty(fetcher.Requested);
    }

    [Fact]
    public async Task FetchConvertsFetcherFailureIntoAResult()
    {
        var result = await new WikipediaMetadataPlugin(new StubFetcher(_ => null!))
            .FetchAsync(new OnlineMetadataRequest("Example Film", null, "en"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("HttpRequestException", result.Error);
    }

    [Fact]
    public void AssemblyIsDiscoveredAsAnOnlineMetadataSource()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(WikipediaMetadataPlugin).Assembly);

        Assert.Equal("skycd.metadata-wikipedia", plugin.Id);
        var capability = Assert.IsType<WikipediaMetadataPlugin>(
            Assert.Single(plugin.Capabilities.OfType<IOnlineMetadataSourceCapability>()));
        Assert.Equal("wikipedia", capability.SourceId);
        Assert.Contains("wikipedia.org", capability.KnownHosts);
    }
}
