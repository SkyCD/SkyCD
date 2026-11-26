using System.Linq;
using SkyCD.Plugin.Feature.GenDesc.Models;
using SkyCD.Plugin.Feature.GenDesc.Parsing;
using SkyCD.Plugin.OnlineMetadata.Models;
using Xunit;

namespace SkyCD.Plugin.Feature.GenDesc.Tests;

public class BuiltInTemplatesTests
{
    private static readonly string[] Categories =
    [
        "movie", "tv", "music", "audiobook", "game", "software", "ebook", "comic", "course", "image", "font",
        "rom", "other"
    ];

    [Fact]
    public void EveryCategoryHasAnEnglishTemplateOfBothKinds()
    {
        var templates = BuiltInTemplates.All();

        foreach (var category in Categories)
        {
            Assert.Single(templates, template =>
                template.Category == category && template.Kind == TemplateInfo.NfoKind && template.Language == "en");
            Assert.Single(templates, template =>
                template.Category == category && template.Kind == TemplateInfo.DescriptionKind &&
                template.Language == "en");
        }
    }

    [Fact]
    public void MovieAndTvAlsoShipLithuanianTemplates()
    {
        var templates = BuiltInTemplates.All();

        foreach (var category in new[] { "movie", "tv" })
        {
            foreach (var kind in new[] { TemplateInfo.NfoKind, TemplateInfo.DescriptionKind })
            {
                Assert.Single(templates, template =>
                    template.Category == category && template.Kind == kind && template.Language == "lt");
            }
        }
    }

    [Fact]
    public void IdentifiersAreUniqueAndSeedingCompatible()
    {
        var templates = BuiltInTemplates.All();

        Assert.Equal(templates.Count, templates.Select(template => template.Id).Distinct().Count());
        Assert.All(templates, template => Assert.StartsWith("default-", template.Id));
        Assert.All(templates, template => Assert.Equal(TemplateInfo.DefaultSite, template.Site));
    }

    [Fact]
    public void DescriptionTemplatesUseBbCodeAndNfoTemplatesDoNot()
    {
        foreach (var template in BuiltInTemplates.All())
        {
            if (template.IsNfo)
            {
                Assert.DoesNotContain("[b]", template.Body);
                Assert.DoesNotContain("[i]", template.Body);
                Assert.Contains(new string('-', 60), template.Body);
            }
            else
            {
                Assert.Contains("[b]", template.Body);
                Assert.Contains("[i]", template.Body);
            }
        }
    }

    [Fact]
    public void NfoTemplateRendersAGuardedMultilineLayout()
    {
        var template = BuiltInTemplates.All().Single(candidate => candidate.Id == "default-nfo-movie");
        var facts = SampleMovieFacts();

        var lines = TemplateEngine.Render(template.Body, facts).Split('\n');

        Assert.Equal("Example Movie (2024)", lines[0]);
        Assert.Equal(new string('-', 60), lines[1]);
        Assert.Equal(string.Empty, lines[2]);
        Assert.Contains("Year: 2024", lines);
        Assert.Contains("Genre: Action, Drama", lines);
        Assert.Contains("Plot: A short plot.", lines);
        Assert.Contains(string.Empty, lines);
        Assert.Contains("File: Example.Movie.2024.1080p.mkv", lines);
        Assert.Contains("Size: 8.5 GB", lines);
        Assert.Contains("Type: video/x-matroska", lines);
        Assert.Contains("Category: Movie", lines);
    }

    [Fact]
    public void MissingFactsLeaveNoEmptyLinesBehind()
    {
        var template = BuiltInTemplates.All().Single(candidate => candidate.Id == "default-nfo-movie");
        var facts = new DescriptionFacts();
        facts.Force("Title", "Bare Movie");
        facts.Force("FileName", "Bare.Movie.mkv");

        var rendered = TemplateEngine.Render(template.Body, facts);

        Assert.DoesNotContain("Year:", rendered);
        Assert.DoesNotContain("Genre:", rendered);
        Assert.Contains("File: Bare.Movie.mkv", rendered);
        Assert.DoesNotContain("\n\n\n", rendered);
    }

    [Fact]
    public void DescriptionTemplateRendersBbCodeLines()
    {
        var template = BuiltInTemplates.All().Single(candidate => candidate.Id == "default-description-movie");

        var rendered = TemplateEngine.Render(template.Body, SampleMovieFacts());

        Assert.StartsWith("[b]Example Movie (2024)[/b]", rendered);
        Assert.Contains("[i]Year:[/i] 2024", rendered);
        Assert.Contains("[i]Cast:[/i] Actor One, Actor Two", rendered);
        Assert.DoesNotContain("File:", rendered);
    }

    private static DescriptionFacts SampleMovieFacts()
    {
        var facts = new DescriptionFacts();
        facts.Force("Name", "Example.Movie.2024.1080p");
        facts.Force("Title", "Example Movie");
        facts.Force("Year", "2024");
        facts.Force("Genre", "Action|Drama");
        facts.Force("Cast", "Actor One|Actor Two");
        facts.Force("Plot", "A short plot.");
        facts.Force("FileName", "Example.Movie.2024.1080p.mkv");
        facts.Force("Size", "8.5 GB");
        facts.Force("MimeType", "video/x-matroska");
        facts.Force("CategoryTitle", "Movie");
        return facts;
    }
}
