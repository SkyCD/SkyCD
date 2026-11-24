using SkyCD.Plugin.Ai.GenDesc.Models;
using SkyCD.Plugin.Ai.GenDesc.Sources;
using Xunit;

namespace SkyCD.Plugin.Ai.GenDesc.Tests;

public class ImporterTests
{
    [Fact]
    public void WikipediaActionApiExtractsTitlePlotAndImage()
    {
        var facts = new DescriptionFacts();
        new WikipediaImporter().Parse(Fixtures.Read("wikipedia-action.json"), new ImportRequest("Example Film", null, "en"), facts);
        Assert.Equal("Example Film", facts.Get("Title"));
        Assert.Contains("directed by Jane Doe", facts.Get("Plot"));
        Assert.Contains("example.png", facts.Get("Image"));
        Assert.False(facts.Has("Genre"));
    }

    [Fact]
    public void WikipediaSummaryExtractsDescriptionAsGenre()
    {
        var facts = new DescriptionFacts();
        new WikipediaImporter().Parse(Fixtures.Read("wikipedia-summary.json"), new ImportRequest("Example Film", null, "en"), facts);
        Assert.Equal("Example Film", facts.Get("Title"));
        Assert.Equal("1999 film", facts.Get("Genre"));
        Assert.Contains("feature directed by Jane Doe", facts.Get("Plot"));
    }

    [Fact]
    public void ImdbReadsJsonLdFacts()
    {
        var facts = new DescriptionFacts();
        new ImdbImporter().Parse(Fixtures.Read("imdb-title.html"), new ImportRequest("The Matrix", null, "en"), facts);
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
    public void AniDbReadsInfoboxDefinitionList()
    {
        var facts = new DescriptionFacts();
        new AniDbImporter().Parse(Fixtures.Read("anidb-anime.html"), new ImportRequest("Evangelion", null, "en"), facts);
        Assert.Equal("Neon Genesis Evangelion", facts.Get("Title"));
        Assert.Equal("TV", facts.Get("Type"));
        Assert.Equal("26", facts.Get("Episodes"));
        Assert.Equal("24 min per ep", facts.Get("Runtime"));
        Assert.Equal("Anno Hideaki", facts.Get("Director"));
        Assert.Equal("Gainax", facts.Get("Studio"));
        Assert.Contains("giant cyborgs", facts.Get("Plot"));
    }

    [Fact]
    public void AnnReadsProfileTable()
    {
        var facts = new DescriptionFacts();
        new AnimeNewsNetworkImporter().Parse(Fixtures.Read("ann-encyclopedia.html"), new ImportRequest("Cowboy Bebop", null, "en"), facts);
        Assert.Equal("Cowboy Bebop", facts.Get("Title"));
        Assert.Equal("TV", facts.Get("Type"));
        Assert.Equal("26", facts.Get("Episodes"));
        Assert.Equal("Sunrise", facts.Get("Studio"));
        Assert.Equal("Watanabe Shinichiro", facts.Get("Director"));
        Assert.Contains("bounty hunters", facts.Get("Plot"));
    }

    [Fact]
    public void TvComReadsMetaAndJsonLd()
    {
        var facts = new DescriptionFacts();
        new TvComImporter().Parse(Fixtures.Read("tvcom-show.html"), new ImportRequest("Breaking Bad", null, "en"), facts);
        Assert.Equal("Breaking Bad", facts.Get("Title"));
        Assert.Equal("Crime|Drama|Thriller", facts.Get("Genre"));
        Assert.Equal("2008", facts.Get("Year"));
        Assert.Equal("62", facts.Get("Episodes"));
        Assert.Equal("Vince Gilligan", facts.Get("Director"));
    }

    [Fact]
    public void ParseNeverOverwritesAnExistingFact()
    {
        var facts = new DescriptionFacts();
        facts.Force("Title", "Existing");
        new ImdbImporter().Parse(Fixtures.Read("imdb-title.html"), new ImportRequest("x", null, "en"), facts);
        Assert.Equal("Existing", facts.Get("Title"));
    }

    [Fact]
    public void GarbageBodyProducesNoFacts()
    {
        var facts = new DescriptionFacts();
        new ImdbImporter().Parse("<<<not html>>>", new ImportRequest("x", null, "en"), facts);
        Assert.Empty(facts.Keys);
    }

    [Fact]
    public void WikipediaResolvesSummaryFromArticleUrl()
    {
        var url = new WikipediaImporter().ResolveUrl(new ImportRequest("", "https://en.wikipedia.org/wiki/Example_Film", "en"));
        Assert.NotNull(url);
        Assert.Contains("/api/rest_v1/page/summary/Example%20Film", url);
    }

    [Fact]
    public void ImdbUsesExplicitTitleUrl()
    {
        var importer = new ImdbImporter();
        Assert.Equal("https://www.imdb.com/title/tt0133093/", importer.ResolveUrl(new ImportRequest("The Matrix", "https://www.imdb.com/title/tt0133093/", "en")));
    }

    [Fact]
    public void ImdbIgnoresForeignUrlWithoutQuery()
    {
        Assert.Null(new ImdbImporter().ResolveUrl(new ImportRequest("", "https://en.wikipedia.org/wiki/Example", "en")));
    }

    [Fact]
    public void EmptyRequestResolvesNothing()
    {
        Assert.Null(new WikipediaImporter().ResolveUrl(new ImportRequest("", null, "en")));
        Assert.Null(new TvComImporter().ResolveUrl(new ImportRequest("", null, "en")));
    }
}
