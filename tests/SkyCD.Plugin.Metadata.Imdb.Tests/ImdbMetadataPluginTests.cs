using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.OnlineMetadata.Models;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;
using SkyCD.TestSupport;

namespace SkyCD.Plugin.Metadata.Imdb.Tests;

public class ImdbMetadataPluginTests
{
    private static ImdbMetadataPlugin Plugin(string body = "") => new(new StubFetcher(body));

    [Fact]
    public void ReadsJsonLdFacts()
    {
        var facts = new DescriptionFacts();
        Plugin().Parse(OnlineMetadataFixtures.Read("imdb-title.html"), facts);

        Assert.Equal("The Matrix", facts.Get("Title"));
        Assert.Equal("Action|Sci-Fi", facts.Get("Genre"));
        Assert.Equal("1999", facts.Get("Year"));
        Assert.Equal("2 h 11 min", facts.Get("Runtime"));
        Assert.Equal("Lana Wachowski|Lilly Wachowski", facts.Get("Director"));
        Assert.Contains("Keanu Reeves", facts.Get("Cast"));
        Assert.Equal("8.7/10", facts.Get("Rating"));
        Assert.Equal("Matrix", facts.Get("OriginalTitle"));
    }

    [Fact]
    public void ParseNeverOverwritesAnExistingFact()
    {
        var facts = new DescriptionFacts();
        facts.Force("Title", "Existing");

        Plugin().Parse(OnlineMetadataFixtures.Read("imdb-title.html"), facts);

        Assert.Equal("Existing", facts.Get("Title"));
    }

    [Fact]
    public void GarbageBodyProducesNoFacts()
    {
        var facts = new DescriptionFacts();
        Plugin().Parse("<<<not html>>>", facts);

        Assert.Empty(facts.Keys);
    }

    [Fact]
    public void UsesAnExplicitTitleUrl()
    {
        Assert.Equal("https://www.imdb.com/title/tt0133093/",
            Plugin().ResolveUrl(new OnlineMetadataRequest("The Matrix", "https://www.imdb.com/title/tt0133093/", "en")));
    }

    [Fact]
    public void QueryFallsBackToTheSearchPage()
    {
        var url = Plugin().ResolveUrl(new OnlineMetadataRequest("The Matrix", null, "en"));

        Assert.Equal("https://www.imdb.com/find/?q=The%20Matrix&s=tt", url);
    }

    [Fact]
    public void ForeignUrlWithoutAQueryResolvesNothing()
    {
        Assert.Null(Plugin().ResolveUrl(new OnlineMetadataRequest("", "https://en.wikipedia.org/wiki/Example", "en")));
    }

    [Fact]
    public async Task FetchReturnsFactsAndTheFetchedUrl()
    {
        var fetcher = new StubFetcher(OnlineMetadataFixtures.Read("imdb-title.html"));
        var plugin = new ImdbMetadataPlugin(fetcher);
        var request = new OnlineMetadataRequest("", "https://www.imdb.com/title/tt0133093/", "en");

        var result = await plugin.FetchAsync(request, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("imdb", result.SourceId);
        Assert.Equal(request.Url, result.SourceUrl);
        Assert.Equal(request.Url, fetcher.Requested.Single());
        Assert.Equal("The Matrix", result.Facts["Title"]);
    }

    [Fact]
    public async Task FetchConvertsFetcherFailureIntoAResult()
    {
        var result = await new ImdbMetadataPlugin(new StubFetcher(_ => null!))
            .FetchAsync(new OnlineMetadataRequest("The Matrix", null, "en"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("HttpRequestException", result.Error);
    }

    [Fact]
    public void AssemblyIsDiscoveredAsAnOnlineMetadataSource()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(ImdbMetadataPlugin).Assembly);

        Assert.Equal("skycd.metadata-imdb", plugin.Id);
        var capability = Assert.IsType<ImdbMetadataPlugin>(
            Assert.Single(plugin.Capabilities.OfType<IOnlineMetadataSourceCapability>()));
        Assert.Equal("imdb", capability.SourceId);
        Assert.Contains("imdb.com", capability.KnownHosts);
    }
}
