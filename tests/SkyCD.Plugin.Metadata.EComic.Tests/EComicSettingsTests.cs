using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Configuration;
using Xunit;
using SkyCD.Plugin.Metadata.EComic.Configuration;
using SkyCD.Plugin.Metadata.EComic.Models;

namespace SkyCD.Plugin.Metadata.EComic.Tests;

/// <summary>
/// Persisted settings contract: written values round-trip, unreadable values fall back to the documented
/// defaults, and the configuration surface normalizes what it returns.
/// </summary>
public sealed class EComicSettingsTests
{
    [Fact]
    public void Settings_RoundTripThroughThePersistedDictionary()
    {
        var original = new EComicSettings(
            collectionEnabled: true,
            nameSource: EComicNameSource.Series,
            tagWriters: true,
            extractCovers: true,
            coverSize: 512,
            extensions: [".cbz", ".cbr"]);

        var restored = EComicSettings.From(original.ToDictionary());

        Assert.True(restored.CollectionEnabled);
        Assert.Equal(EComicNameSource.Series, restored.NameSource);
        Assert.True(restored.TagWriters);
        Assert.True(restored.ExtractCovers);
        Assert.Equal(512, restored.CoverSize);
        Assert.Equal([".cbz", ".cbr"], restored.Extensions);
    }

    [Fact]
    public void Defaults_EnableCollectionAndLeaveCoversAndTagsOff()
    {
        var settings = EComicSettings.Default;

        Assert.True(settings.CollectionEnabled);
        Assert.Equal(EComicNameSource.FileName, settings.NameSource);
        Assert.False(settings.TagWriters);
        Assert.False(settings.ExtractCovers);
        Assert.Equal(720, settings.CoverSize);
        Assert.Equal([".cbz", ".cbr", ".cb7"], settings.Extensions);
    }

    [Fact]
    public void UnreadableValues_FallBackToDefaults()
    {
        var settings = EComicSettings.From(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [EComicSettings.CollectionEnabledKey] = "not a boolean",
            [EComicSettings.NameSourceKey] = "bogus",
            [EComicSettings.CoverSizeKey] = "huge",
            [EComicSettings.ExtensionsKey] = "*.zip; sub/dir"
        });

        Assert.True(settings.CollectionEnabled);
        Assert.Equal(EComicNameSource.FileName, settings.NameSource);
        Assert.Equal(720, settings.CoverSize);
        Assert.Equal([".cbz", ".cbr", ".cb7"], settings.Extensions);
    }

    [Theory]
    [InlineData("0", 64)]
    [InlineData("4096", 2048)]
    [InlineData("512", 512)]
    public void CoverSize_IsClampedToTheSupportedRange(string persisted, int expected)
    {
        var settings = EComicSettings.From(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [EComicSettings.CoverSizeKey] = persisted
        });

        Assert.Equal(expected, settings.CoverSize);
    }

    [Fact]
    public void Extensions_AreNormalizedAndDeduplicated()
    {
        var parsed = EComicSettings.ParseExtensions(" JPEG; .Png ,cbz;.jpeg");

        Assert.Equal([".jpeg", ".png", ".cbz"], parsed);
    }

    [Fact]
    public void EmptyExtensionList_FallsBackToTheDocumentedTypes()
    {
        Assert.Equal([".cbz", ".cbr", ".cb7"], EComicSettings.ParseExtensions("  "));
        Assert.Equal([".cbz", ".cbr", ".cb7"], EComicSettings.ParseExtensions(null));
    }

    [Fact]
    public void Editor_ReturnsNormalizedSettings()
    {
        var editor = new EComicConfigurationEditor(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [EComicSettings.NameSourceKey] = "title",
            [EComicSettings.CoverSizeKey] = "720"
        })
        {
            CoverSize = 4096,
            Extensions = "CBZ; cb7; bad/entry"
        };

        var settings = EComicSettings.From(editor.BuildSettings());

        Assert.Equal(EComicNameSource.Title, settings.NameSource);
        Assert.Equal(2048, settings.CoverSize);
        Assert.Equal([".cbz", ".cb7"], settings.Extensions);
    }

    [Fact]
    public void Editor_DescribesEveryNameSource()
    {
        Assert.Equal(3, EComicConfigurationEditor.NameSources.Count);
        Assert.All(EComicConfigurationEditor.NameSources, source =>
            Assert.False(string.IsNullOrWhiteSpace(EComicConfigurationEditor.DescribeNameSource(source))));
    }

    [Fact]
    public async Task ConfigurationPlugin_ReturnsSettingsForItsOwnSurface()
    {
        var plugin = new EComicConfigurationPlugin();
        using var page = await plugin.CreatePageAsync(new PluginConfigurationRequest
        {
            ConfigurationId = "ecomic.settings",
            CurrentSettings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                [EComicSettings.NameSourceKey] = "series",
                [EComicSettings.CoverSizeKey] = "480"
            }
        });

        var result = await page.AcceptAsync();
        Assert.True(result.Success);
        Assert.False(result.Canceled);
        Assert.Equal("series", result.Settings[EComicSettings.NameSourceKey]);
        Assert.Equal("480", result.Settings[EComicSettings.CoverSizeKey]);
    }

    [Fact]
    public async Task ConfigurationPlugin_RejectsAForeignConfigurationId()
    {
        var plugin = new EComicConfigurationPlugin();
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
        var descriptor = new EComicConfigurationPlugin().Configuration;

        Assert.Equal("ecomic.settings", descriptor.ConfigurationId);
        Assert.Equal("eComic", descriptor.Title);
        Assert.Equal("Metadata", descriptor.Group);
    }
}
