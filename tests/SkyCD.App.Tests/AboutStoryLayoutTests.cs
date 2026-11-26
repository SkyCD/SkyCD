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
    public void OverviewTab_ShowsAuthorsAfterReferencesAndHoldsNoThanks()
    {
        // Issue #1204: the Thanks credits moved to their own tab, so the Overview tab ends
        // with the authors section in story -> references -> authors order.
        var story = OverviewTab();
        var markup = story.ToString();

        var references = markup.IndexOf("UiKeys.AboutStoryReferences", StringComparison.Ordinal);
        var authors = markup.IndexOf("UiKeys.AboutStoryAuthors}", StringComparison.Ordinal);
        Assert.True(0 <= references && references < authors,
            "the Overview tab must read story, references, authors.");
        Assert.DoesNotContain("UiKeys.AboutStoryThanks}", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("UiKeys.AboutStoryThanksFreewareBase", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("UiKeys.AboutStoryThanksSourceForge", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("UiKeys.AboutStoryThanksGbit", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("UiKeys.AboutStoryThanksSlivickas", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("UiKeys.AboutStoryThanksAndYou", markup, StringComparison.Ordinal);

        foreach (var key in new[]
                 {
                     "UiKeys.AboutStoryAuthorName",
                     "UiKeys.AboutStoryAuthorContact"
                 })
        {
            Assert.Contains(key, markup, StringComparison.Ordinal);
        }

        var email = Assert.Single(story.Descendants(Avalonia + "TextBlock"),
            text => ((string?)text.Attribute("Tag"))?.StartsWith("mailto:", StringComparison.Ordinal) == true);
        Assert.Equal("OnOpenRepositoryUrl", (string?)email.Attribute("PointerPressed"));
    }

    [Fact]
    public void ThanksTab_IsSecondAndHoldsTheFiveCreditLines()
    {
        // Issue #1204: the Thanks tab sits right after Overview and holds exactly the five credit lines.
        var tabs = AllTabs();
        Assert.True(tabs.Length >= 2, "the About window must render at least the Overview and Thanks tabs.");

        var thanks = tabs[1];
        Assert.Contains("UiKeys.AboutStoryThanks", (string?)thanks.Attribute("Header"), StringComparison.Ordinal);

        var thanksTexts = thanks.Descendants(Avalonia + "TextBlock").ToArray();
        Assert.Equal(5, thanksTexts.Length);
        foreach (var key in new[]
                 {
                     "UiKeys.AboutStoryThanksFreewareBase",
                     "UiKeys.AboutStoryThanksSourceForge",
                     "UiKeys.AboutStoryThanksGbit",
                     "UiKeys.AboutStoryThanksSlivickas",
                     "UiKeys.AboutStoryThanksAndYou"
                 })
        {
            var entry = Assert.Single(thanksTexts,
                text => ((string?)text.Attribute("Text"))?.Contains(key, StringComparison.Ordinal) == true);
            Assert.Equal("body", (string?)entry.Attribute("Classes"));
            Assert.Equal("Wrap", (string?)entry.Attribute("TextWrapping"));
        }

        Assert.DoesNotContain(thanksTexts,
            text => ((string?)text.Attribute("Text"))?.Contains("UiKeys.AboutStoryThanks}", StringComparison.Ordinal) == true);
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
    public void OverviewTab_ReferenceLinksOpenThroughTheSharedHandler()
    {
        var story = OverviewTab();
        var links = story.Descendants(Avalonia + "TextBlock")
            .Where(text => (string?)text.Attribute("PointerPressed") == "OnOpenRepositoryUrl")
            .ToArray();

        Assert.Equal(4, links.Length);
        Assert.Contains(links, link => (string?)link.Attribute("Text") == "https://github.com/SkyCD/SkyCD");
        Assert.Contains(links, link => (string?)link.Attribute("Text") == "https://sourceforge.net/projects/skycd/");
        Assert.Contains(links, link => (string?)link.Attribute("Text") == "http://skycd.sf.net");
        Assert.Contains(links, link => (string?)link.Attribute("Tag") == "mailto:skycd@mekdrop.name");
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
