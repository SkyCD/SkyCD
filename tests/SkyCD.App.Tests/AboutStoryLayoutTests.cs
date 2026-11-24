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
    public void StoryTab_ExistsWithTranslatedStoryParagraphs()
    {
        var story = StoryTab();

        Assert.Contains("UiKeys.AboutStory", (string?)story.Attribute("Header"), StringComparison.Ordinal);
        AssertTranslatedText(story, "UiKeys.AboutStoryWhat");
        AssertTranslatedText(story, "UiKeys.AboutStoryOrigin");
        AssertTranslatedText(story, "UiKeys.AboutStoryRewrite");
        AssertTranslatedText(story, "UiKeys.AboutStoryReferences");
    }

    [Fact]
    public void StoryTab_ShowsTheRunningVersionInsteadOfAHardCodedOne()
    {
        var story = StoryTab();
        var versionLine = story.Descendants(Avalonia + "TextBlock")
            .Single(text => text.Descendants(Avalonia + "Run").Any());

        Assert.Contains(versionLine.Descendants(Avalonia + "Run"), run =>
            ((string?)run.Attribute("Text"))?.Contains("UiKeys.AboutStoryVersion", StringComparison.Ordinal) == true);
        Assert.Contains(versionLine.Descendants(Avalonia + "Run"), run =>
            string.Equals((string?)run.Attribute("Text"), "{Binding Version}", StringComparison.Ordinal));
        Assert.DoesNotContain("Version 30", story.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void StoryTab_ReferenceLinksOpenThroughTheSharedHandler()
    {
        var story = StoryTab();
        var links = story.Descendants(Avalonia + "TextBlock")
            .Where(text => (string?)text.Attribute("PointerPressed") == "OnOpenRepositoryUrl")
            .ToArray();

        Assert.Equal(5, links.Length);
        Assert.Contains(links, link => (string?)link.Attribute("Text") == "https://github.com/SkyCD/SkyCD");
        Assert.Contains(links, link => (string?)link.Attribute("Text") == "https://sourceforge.net/projects/skycd/");
        Assert.Contains(links, link => (string?)link.Attribute("Text") == "http://skycd.sf.net");
        Assert.Contains(links, link => (string?)link.Attribute("Text") == "http://www.skyftp.skynet.lt");
        Assert.Contains(links, link =>
            (string?)link.Attribute("Tag") == "https://github.com/SkyCD/SkyCD/blob/main/LICENSE");
    }

    private static void AssertTranslatedText(XElement story, string key)
    {
        Assert.Contains(story.Descendants(Avalonia + "TextBlock"), text =>
            ((string?)text.Attribute("Text"))?.Contains(key, StringComparison.Ordinal) == true);
    }

    private static XElement StoryTab()
    {
        var document = XDocument.Parse(ReadAboutWindow());
        return document.Descendants(Avalonia + "TabItem")
            .Single(tab => ((string?)tab.Attribute("Header"))?.Contains("UiKeys.AboutStory", StringComparison.Ordinal) == true);
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
