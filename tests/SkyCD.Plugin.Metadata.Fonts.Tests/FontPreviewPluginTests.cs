using System;
using System.Threading.Tasks;
using SkiaSharp;
using SkyCD.SDK.Capabilities.Media;
using Xunit;
using SkyCD.Plugin.Metadata.Fonts.Export;
using SkyCD.Plugin.Metadata.Fonts.Models;
using SkyCD.TestSupport;

namespace SkyCD.Plugin.Metadata.Fonts.Tests;

public sealed class FontPreviewPluginTests
{
    [Fact]
    public async Task Preview_IsDisabledByDefaultAndProducesNothing()
    {
        var plugin = new FontPreviewPlugin();
        plugin.ApplySettings(FontFixtures.Settings(previewsEnabled: false));

        Assert.Empty(plugin.Descriptor.Extensions);
        var result = await plugin.GeneratePreviewAsync(
            new MediaItem(".ttf", null, 100),
            new ByteArrayMediaSource(FontFixtures.Sfnt("Ignored")),
            progress: null,
            default);

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task EnabledPreview_ClaimsTheConfiguredExtensionsAndProducesAPng()
    {
        var result = await FontFixtures.GeneratePreviewAsync(FontFixtures.Sfnt("Rendering Face"));

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal(FontPreviewRenderer.ContentType, artifact.ContentType);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, artifact.Data.Span[..4].ToArray());
        using var image = SKBitmap.Decode(artifact.Data.Span.ToArray());
        Assert.NotNull(image);
        Assert.InRange(image.Width, 64, 1600);
        Assert.InRange(image.Height, 48, 1200);
    }

    [Fact]
    public async Task Preview_HonoursThePerFolderLimit()
    {
        var font = FontFixtures.Sfnt("Limited Face");
        var settings = FontFixtures.Settings(previewsEnabled: true, previewLimitPerFolder: "1");
        var plugin = new FontPreviewPlugin(FontPluginOptions.Default, _ => SKTypeface.FromFamilyName(null));
        plugin.ApplySettings(settings);

        async Task<MediaPreviewResult> GenerateAsync(string groupKey) => await plugin.GeneratePreviewAsync(
            new MediaItem(".ttf", null, font.Length, groupKey),
            new ByteArrayMediaSource(font, budget: Math.Max(font.Length * 4, 1_000_000)),
            progress: null,
            default);

        var first = await GenerateAsync("folder-a");
        var second = await GenerateAsync("folder-a");
        var other = await GenerateAsync("folder-b");

        Assert.Single(first.Artifacts);
        Assert.Empty(second.Artifacts);
        Assert.Single(other.Artifacts);
    }

    [Fact]
    public async Task UnlimitedPreview_KeepsProducingImages()
    {
        var font = FontFixtures.Sfnt("Unbounded Face");
        var settings = FontFixtures.Settings(previewsEnabled: true, previewLimitPerFolder: "0");

        var first = await FontFixtures.GeneratePreviewAsync(font, settings, groupKey: "folder");
        var second = await FontFixtures.GeneratePreviewAsync(font, settings, groupKey: "folder");

        Assert.Single(first.Artifacts);
        Assert.Single(second.Artifacts);
    }

    [Fact]
    public async Task Preview_ReturnsNothingWhenTheGlyphsCannotBeDecoded()
    {
        var plugin = new FontPreviewPlugin(FontPluginOptions.Default, _ => null);
        plugin.ApplySettings(FontFixtures.Settings(previewsEnabled: true));

        var result = await plugin.GeneratePreviewAsync(
            new MediaItem(".ttf", null, 100),
            new ByteArrayMediaSource(FontFixtures.Sfnt("Undecodable")),
            progress: null,
            default);

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task Preview_IsSkippedWhenTheFontExceedsTheReadBound()
    {
        var font = FontFixtures.Sfnt("Huge Face");
        var options = new FontPluginOptions(maxFontBytes: font.Length - 1);
        var plugin = new FontPreviewPlugin(options, _ => SKTypeface.FromFamilyName(null));
        plugin.ApplySettings(FontFixtures.Settings(previewsEnabled: true));

        var result = await plugin.GeneratePreviewAsync(
            new MediaItem(".ttf", null, font.Length),
            new ByteArrayMediaSource(font),
            progress: null,
            default);

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public void Renderer_KeepsTheCanvasWithinTheConfiguredPixelLimit()
    {
        var font = FontFixtures.Sfnt("Any Face");
        var options = new FontPluginOptions(maxPreviewPixels: 100);

        Assert.Throws<SkyCD.Plugin.Metadata.Fonts.Exceptions.FontPreviewException>(() =>
            FontPreviewRenderer.Render(font, "sample", 60, options, default,
                _ => SKTypeface.FromFamilyName(null)));
    }
}
