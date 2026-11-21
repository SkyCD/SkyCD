using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using SkyCD.Plugin.Fonts.Configuration;
using SkyCD.Plugin.Fonts.Models;

namespace SkyCD.Plugin.Fonts.Tests;

public sealed class FontSettingsTests
{
    [Fact]
    public void Defaults_ClaimTheFourFontExtensionsAndDisablePreviews()
    {
        var settings = FontSettings.From(new Dictionary<string, string?>());

        Assert.True(settings.CollectionEnabled);
        Assert.False(settings.PreviewsEnabled);
        Assert.Equal(60, settings.PreviewSize);
        Assert.Equal(0, settings.PreviewLimitPerFolder);
        Assert.Equal([".ttf", ".ttc", ".otf", ".otc"], settings.Extensions);
    }

    [Fact]
    public void UnreadableValues_FallBackToDocumentedDefaults()
    {
        var settings = FontSettings.From(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [FontSettings.CollectionEnabledKey] = "nonsense",
            [FontSettings.PreviewsEnabledKey] = "true",
            [FontSettings.PreviewSizeKey] = "10000",
            [FontSettings.PreviewLimitPerFolderKey] = "-4"
        });

        Assert.True(settings.CollectionEnabled);
        Assert.True(settings.PreviewsEnabled);
        Assert.Equal(FontSettings.MaxPreviewSize, settings.PreviewSize);
        Assert.Equal(0, settings.PreviewLimitPerFolder);
    }

    [Fact]
    public void ExtensionParsing_NormalizesAndDeduplicates()
    {
        var extensions = FontSettings.ParseExtensions(" ttf; .TTF,otf WOFF2 ");

        Assert.Equal([".ttf", ".otf", ".woff2"], extensions);
    }

    [Fact]
    public void BlankPreviewText_FallsBackToTheDefaultSample()
    {
        var settings = new FontSettings(true, true, "   ", 60, 0, FontSchema.DefaultExtensions);

        Assert.Equal(FontSettings.DefaultPreviewText, settings.PreviewText);
    }

    [Fact]
    public void PreviewText_IsCappedAndLineEndingsAreNormalized()
    {
        var settings = new FontSettings(true, true, "a\r\nb\r" + new string('c', 400), 60, 0,
            FontSchema.DefaultExtensions);

        Assert.DoesNotContain('\r', settings.PreviewText);
        Assert.True(settings.PreviewText.Length <= FontSettings.MaxPreviewTextLength);
    }

    [Fact]
    public void RoundTrip_KeepsEveryDocumentedOption()
    {
        var original = new FontSettings(true, true, "Sample", 72, 5, [".ttf", ".otf"]);

        var roundTripped = FontSettings.From(original.ToDictionary());

        Assert.Equal("Sample", roundTripped.PreviewText);
        Assert.Equal(72, roundTripped.PreviewSize);
        Assert.Equal(5, roundTripped.PreviewLimitPerFolder);
        Assert.Equal(original.Extensions, roundTripped.Extensions);
    }

    [Fact]
    public void Editor_ProducesNormalizedSettings()
    {
        var editor = new FontConfigurationEditor(new Dictionary<string, string?>())
        {
            PreviewsEnabled = true,
            PreviewText = "Hello",
            PreviewSize = 500,
            Extensions = "ttf; otf"
        };

        var settings = FontSettings.From(editor.BuildSettings());

        Assert.True(settings.PreviewsEnabled);
        Assert.Equal(FontSettings.MaxPreviewSize, settings.PreviewSize);
        Assert.Equal([".ttf", ".otf"], settings.Extensions);
    }
}
