using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #1024: the About window carries the v1 story over as a static, translatable tab with
/// clickable project references instead of the legacy animated scrolling text.
/// </summary>
public sealed class AboutStoryLayoutTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    [Fact]
    public void OverviewTab_IsFirstAndCarriesTranslatedStoryParagraphs()
    {
        var story = OverviewTab();

        Assert.Contains("UiKeys.AboutOverview", (string?)story.Attribute("Header"), StringComparison.Ordinal);
        Assert.Contains("UiKeys.AboutOverview", (string?)AllTabs().First().Attribute("Header"), StringComparison.Ordinal);
        AssertTranslatedText(story, "UiKeys.AboutStoryWhat");
        AssertTranslatedText(story, "UiKeys.AboutStoryOrigin");
        AssertTranslatedText(story, "UiKeys.AboutStoryRewrite");
        AssertTranslatedText(story, "UiKeys.AboutStoryReferences");
    }

    [Fact]
    public void OverviewTab_DropsTheRetiredVersionAndLicenseRows()
    {
        // Issue #1098: the version is already shown under the dialog title, and the original SkyFTP
        // site and license rows do not belong in the Overview story.
        var story = OverviewTab();
        var markup = story.ToString();

        Assert.DoesNotContain(story.Descendants(Avalonia + "Run"), _ => true);
        Assert.DoesNotContain("skyftp.skynet.lt", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("BSD-2-Clause", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("UiKeys.AboutStoryVersion", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("UiKeys.AboutStoryOriginalSite", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("UiKeys.AboutStoryLicense", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void RetiredAboutStoryKeys_LeaveNoOrphanKeysOrCatalogEntries()
    {
        foreach (var key in new[] { "AboutStoryVersion", "AboutStoryOriginalSite", "AboutStoryLicense" })
        {
            Assert.DoesNotContain(key, ReadRepoFile("src", "SkyCD.UI", "Localization", "UiKeys.cs"), StringComparison.Ordinal);
            Assert.DoesNotContain("ui." + key, ReadRepoFile("src", "SkyCD.UI", "Translations", "en-UI.yaml"), StringComparison.Ordinal);
            Assert.DoesNotContain("ui." + key, ReadRepoFile("src", "SkyCD.UI", "Translations", "lt-UI.yaml"), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OverviewTab_CarriesAuthorsAndThanksAfterReferences()
    {
        // Issue #1134: the legacy About.vb credits return after the project references.
        var story = OverviewTab();
        foreach (var key in new[]
                 {
                     "UiKeys.AboutStoryAuthors", "UiKeys.AboutStoryAuthorName",
                     "UiKeys.AboutStoryThanks", "UiKeys.AboutStoryThanksFreewareBase",
                     "UiKeys.AboutStoryThanksSourceForge", "UiKeys.AboutStoryThanksPersons",
                     "UiKeys.AboutStoryThanksGbit", "UiKeys.AboutStoryThanksSlivickas",
                     "UiKeys.AboutStoryThanksYou"
                 })
        {
            AssertTranslatedText(story, key);
        }

        var texts = story.Descendants(Avalonia + "TextBlock")
            .Select(text => (string?)text.Attribute("Text"))
            .Where(text => text is not null)
            .Select(text => text!)
            .ToArray();
        int IndexOf(string key) => Array.FindIndex(texts, text => text.Contains(key, StringComparison.Ordinal));

        var rewrite = IndexOf("UiKeys.AboutStoryRewrite");
        var references = IndexOf("UiKeys.AboutStoryReferences");
        var authors = IndexOf("UiKeys.AboutStoryAuthors");
        var thanks = IndexOf("UiKeys.AboutStoryThanks}");

        Assert.True(rewrite < references, "the story must precede the references");
        Assert.True(references < authors, "the references must precede the authors");
        Assert.True(authors < thanks, "the authors must precede the thanks");

        Assert.Contains(story.Descendants(Avalonia + "TextBlock"), text =>
            (string?)text.Attribute("Tag") == "mailto:skycd@mekdrop.name" &&
            (string?)text.Attribute("PointerPressed") == "OnOpenRepositoryUrl");
    }

    [Fact]
    public void OverviewTab_ReferenceLinksOpenThroughTheSharedHandler()
    {
        var story = OverviewTab();
        var links = story.Descendants(Avalonia + "TextBlock")
            .Where(text => (string?)text.Attribute("PointerPressed") == "OnOpenRepositoryUrl")
            .Where(text => ((string?)text.Attribute("Text"))?.StartsWith("http", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal(3, links.Length);
        Assert.Contains(links, link => (string?)link.Attribute("Text") == "https://github.com/SkyCD/SkyCD");
        Assert.Contains(links, link => (string?)link.Attribute("Text") == "https://sourceforge.net/projects/skycd/");
        Assert.Contains(links, link => (string?)link.Attribute("Text") == "http://skycd.sf.net");
    }

    private static void AssertTranslatedText(XElement story, string key)
    {
        Assert.Contains(story.Descendants(Avalonia + "TextBlock"), text =>
            ((string?)text.Attribute("Text"))?.Contains(key, StringComparison.Ordinal) == true);
    }

    private static XElement OverviewTab()
    {
        return AllTabs()
            .Single(tab => ((string?)tab.Attribute("Header"))?.Contains("UiKeys.AboutOverview", StringComparison.Ordinal) == true);
    }

    private static XElement[] AllTabs()
    {
        return XDocument.Parse(ReadAboutWindow()).Descendants(Avalonia + "TabItem").ToArray();
    }

    private static string ReadAboutWindow()
    {
        return ReadRepoFile("src", "SkyCD.App", "Views", "AboutWindow.axaml");
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var path = Path.Combine([FindRepoRoot(), .. parts]);
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SkyCD.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root with SkyCD.slnx was not found.");
    }
}
