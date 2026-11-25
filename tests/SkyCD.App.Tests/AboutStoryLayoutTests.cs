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
    public void OverviewTab_DoesNotRepeatTheRunningVersion()
    {
        var story = OverviewTab();

        Assert.Empty(story.Descendants(Avalonia + "Run"));
        Assert.DoesNotContain("AboutStoryVersion", story.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Running version", story.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewTab_ReferenceLinksOpenThroughTheSharedHandler()
    {
        var story = OverviewTab();
        var links = story.Descendants(Avalonia + "TextBlock")
            .Where(text => (string?)text.Attribute("PointerPressed") == "OnOpenRepositoryUrl")
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
        var path = Path.Combine(FindRepoRoot(), "src", "SkyCD.App", "Views", "AboutWindow.axaml");
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
