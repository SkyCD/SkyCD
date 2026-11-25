using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Configuration;
using Xunit;
using SkyCD.Plugin.Metadata.EBook.Configuration;
using SkyCD.Plugin.Metadata.EBook.Models;
using SkyCD.Plugin.Metadata.EBook.Parsing;

namespace SkyCD.Plugin.Metadata.EBook.Tests;

/// <summary>
/// Persisted settings contract: written values round-trip, unreadable values fall back to the documented
/// defaults, and the configuration surface normalizes what it returns.
/// </summary>
public sealed class EBookSettingsTests
{
    [Fact]
    public void Settings_RoundTripThroughThePersistedDictionary()
    {
        var original = new EBookSettings(
            collectionEnabled: true,
            nameSource: EBookNameSource.Author,
            tagAuthors: true,
            extractCovers: true,
            coverSize: 512,
            extensions: [".epub", ".fb2"]);

        var restored = EBookSettings.From(original.ToDictionary());

        Assert.True(restored.CollectionEnabled);
        Assert.Equal(EBookNameSource.Author, restored.NameSource);
        Assert.True(restored.TagAuthors);
        Assert.True(restored.ExtractCovers);
        Assert.Equal(512, restored.CoverSize);
        Assert.Equal([".epub", ".fb2"], restored.Extensions);
    }

    [Fact]
    public void Defaults_EnableCollectionAndLeaveCoversAndTagsOff()
    {
        var settings = EBookSettings.Default;

        Assert.True(settings.CollectionEnabled);
        Assert.Equal(EBookNameSource.FileName, settings.NameSource);
        Assert.False(settings.TagAuthors);
        Assert.False(settings.ExtractCovers);
        Assert.Equal(250, settings.CoverSize);
        Assert.Equal([".epub", ".ibook", ".mobi", ".prc", ".azw", ".azw3", ".fb2"], settings.Extensions);
    }

    [Fact]
    public void UnreadableValues_FallBackToDefaults()
    {
        var settings = EBookSettings.From(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [EBookSettings.CollectionEnabledKey] = "not a boolean",
            [EBookSettings.NameSourceKey] = "bogus",
            [EBookSettings.CoverSizeKey] = "huge",
            [EBookSettings.ExtensionsKey] = "*.zip; sub/dir"
        });

        Assert.True(settings.CollectionEnabled);
        Assert.Equal(EBookNameSource.FileName, settings.NameSource);
        Assert.Equal(250, settings.CoverSize);
        Assert.Equal(EBookSchema.DefaultExtensions, settings.Extensions);
    }

    [Theory]
    [InlineData("0", 64)]
    [InlineData("4096", 2048)]
    [InlineData("300", 300)]
    public void CoverSize_IsClampedToTheSupportedRange(string persisted, int expected)
    {
        var settings = EBookSettings.From(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [EBookSettings.CoverSizeKey] = persisted
        });

        Assert.Equal(expected, settings.CoverSize);
    }

    [Fact]
    public void Extensions_AreNormalizedAndDeduplicated()
    {
        var parsed = EBookSettings.ParseExtensions(" EPUB; .Mobi ,fb2;.epub");

        Assert.Equal([".epub", ".mobi", ".fb2"], parsed);
    }

    [Fact]
    public void EmptyExtensionList_FallsBackToTheDocumentedTypes()
    {
        Assert.Equal(EBookSchema.DefaultExtensions, EBookSettings.ParseExtensions("  "));
        Assert.Equal(EBookSchema.DefaultExtensions, EBookSettings.ParseExtensions(null));
    }

    [Fact]
    public void Editor_ReturnsNormalizedSettings()
    {
        var editor = new EBookConfigurationEditor(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [EBookSettings.NameSourceKey] = "title",
            [EBookSettings.CoverSizeKey] = "300"
        })
        {
            CoverSize = 4096,
            Extensions = "EPUB; fb2; bad/entry"
        };

        var settings = EBookSettings.From(editor.BuildSettings());

        Assert.Equal(EBookNameSource.Title, settings.NameSource);
        Assert.Equal(2048, settings.CoverSize);
        Assert.Equal([".epub", ".fb2"], settings.Extensions);
    }

    [Fact]
    public void Editor_DescribesEveryNameSource()
    {
        Assert.Equal(3, EBookConfigurationEditor.NameSources.Count);
        Assert.All(EBookConfigurationEditor.NameSources, source =>
            Assert.False(string.IsNullOrWhiteSpace(EBookConfigurationEditor.DescribeNameSource(source))));
    }

    [Fact]
    public async Task ConfigurationPlugin_ReturnsSettingsForItsOwnSurface()
    {
        var plugin = new EBookConfigurationPlugin();
        using var page = await plugin.CreatePageAsync(new PluginConfigurationRequest
        {
            ConfigurationId = "ebook.settings",
            CurrentSettings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                [EBookSettings.NameSourceKey] = "author",
                [EBookSettings.CoverSizeKey] = "480"
            }
        });

        var result = await page.AcceptAsync();
        Assert.True(result.Success);
        Assert.False(result.Canceled);
        Assert.Equal("author", result.Settings[EBookSettings.NameSourceKey]);
        Assert.Equal("480", result.Settings[EBookSettings.CoverSizeKey]);
    }

    [Fact]
    public async Task ConfigurationPlugin_RejectsAForeignConfigurationId()
    {
        var plugin = new EBookConfigurationPlugin();
        using var page = await plugin.CreatePageAsync(new PluginConfigurationRequest
        {
            ConfigurationId = "someone.else"
        });

        var result = await page.AcceptAsync();
        Assert.False(result.Success);
        Assert.Contains("someone.else", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationPlugin_DescribesItsSurface()
    {
        var descriptor = new EBookConfigurationPlugin().Configuration;

        Assert.Equal("ebook.settings", descriptor.ConfigurationId);
        Assert.Equal("eBook", descriptor.Title);
        Assert.Equal("Metadata", descriptor.Group);
    }
}
