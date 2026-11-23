using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.Text.Exceptions;
using SkyCD.Plugin.Metadata.Text.Export;
using Xunit;
using SkyCD.Plugin.Metadata.Text.Models;

namespace SkyCD.Plugin.Metadata.Text.Tests;

public class TextPreviewPluginTests
{
    [Fact]
    public void Descriptor_ShareTheAllowListWithAPreviewSchema()
    {
        var descriptor = new TextPreviewPlugin().Descriptor;

        Assert.Equal(TextSchema.Extensions, descriptor.Extensions);
        Assert.Equal(TextSchema.ContentTypes, descriptor.ContentTypes);
        Assert.Equal("skycd.text.preview", descriptor.Schema.Id);
        Assert.Empty(descriptor.Schema.Fields);
    }

    [Fact]
    public async Task TextSource_ProducesABoundedPlainTextArtifact()
    {
        var result = await RenderAsync(TextFixtures.Utf8(), ".txt");

        var artifact = Assert.Single(result.Artifacts, candidate => candidate.ContentType == "text/plain");

        // text/plain is what makes the browser render it inertly rather than as markup.
        Assert.Equal("text/plain", artifact.ContentType);
        Assert.Equal(TextFixtures.Sample, Decode(artifact.Data.ToArray()));
    }

    [Fact]
    public async Task ArtifactIsAlwaysUtf8WithoutABom_RegardlessOfSourceEncoding()
    {
        // No emoji here: the ISO-8859-1 fixture cannot represent one, and this test is about the artifact's
        // encoding rather than the source's repertoire.
        const string shared = "Café résumé";

        foreach (var source in new[]
                 {
                     TextFixtures.Utf8(shared),
                     TextFixtures.Utf8WithBom(shared),
                     TextFixtures.Utf16Le(shared),
                     TextFixtures.Utf16Be(shared),
                     TextFixtures.Latin1(shared)
                 })
        {
            var artifact = Assert.Single(
                (await RenderAsync(source, ".txt")).Artifacts,
                candidate => candidate.ContentType == "text/plain");
            var bytes = artifact.Data.ToArray();

            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "The artifact must not carry a byte order mark.");
            Assert.Equal(shared, Decode(bytes));
        }
    }

    [Fact]
    public async Task HtmlSource_IsEmittedAsInertTextWithItsMarkupIntact()
    {
        var artifact = Assert.Single(
            (await RenderAsync(TextFixtures.Html(), ".html")).Artifacts,
            candidate => candidate.ContentType == "text/plain");
        var text = Decode(artifact.Data.ToArray());

        // Nothing is parsed, stripped, executed or resolved; the consumer receives characters to display.
        Assert.Equal("text/plain", artifact.ContentType);
        Assert.Contains("<script>alert('x')</script>", text, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://example.com\">", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExcerptIsBoundedByTheConfiguredCharacterLimit()
    {
        var options = new TextPluginOptions(maxExcerptCharacters: 120);
        var text = TextFixtures.HugeSingleLine(50_000);

        var artifact = Assert.Single(
            (await new TextPreviewPlugin(options).GeneratePreviewAsync(
                new MediaItem(".log", "text/plain", text.Length),
                new ByteArrayMediaSource(text),
                null,
                CancellationToken.None)).Artifacts,
            candidate => candidate.ContentType == "text/plain");

        Assert.Equal(120, Decode(artifact.Data.ToArray()).Length);
    }

    [Fact]
    public async Task RenderedExcerpt_IsPublishedAfterTheTextArtifact()
    {
        var text = TextFixtures.Utf8();
        var image = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var renderer = new StubRenderer(available: true, image: image);

        var result = await new TextPreviewPlugin(TextPluginOptions.Default, renderer).GeneratePreviewAsync(
            new MediaItem(".txt", "text/plain", text.Length),
            new ByteArrayMediaSource(text),
            progress: null,
            CancellationToken.None);

        Assert.Equal(2, result.Artifacts.Count);
        Assert.Equal("text/plain", result.Artifacts[0].ContentType);
        Assert.Equal("image/png", result.Artifacts[1].ContentType);
        Assert.Equal(image, result.Artifacts[1].Data.ToArray());

        // The renderer draws exactly the published excerpt at the configured page bound.
        Assert.Equal(TextFixtures.Sample, renderer.Text);
        Assert.Equal(TextPluginOptions.Default.MaxThumbnailDimension, renderer.MaxDimension);
    }

    [Fact]
    public async Task MissingRenderer_StillPublishesTheTextArtifact()
    {
        var text = TextFixtures.Utf8();
        var source = new ByteArrayMediaSource(text);
        var renderer = new StubRenderer(available: false, image: null);

        var result = await new TextPreviewPlugin(TextPluginOptions.Default, renderer).GeneratePreviewAsync(
            new MediaItem(".txt", "text/plain", text.Length), source, null, CancellationToken.None);

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("text/plain", artifact.ContentType);

        // The readable excerpt does not depend on any rasterizer being usable.
        Assert.True(source.ChargedBytes > 0);
        Assert.Equal(0, renderer.CallCount);
    }

    [Fact]
    public async Task RendererWithoutAnImage_PublishesOnlyTheTextArtifact()
    {
        var text = TextFixtures.Utf8();
        var renderer = new StubRenderer(available: true, image: null);

        var result = await new TextPreviewPlugin(TextPluginOptions.Default, renderer).GeneratePreviewAsync(
            new MediaItem(".txt", "text/plain", text.Length),
            new ByteArrayMediaSource(text),
            progress: null,
            CancellationToken.None);

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("text/plain", artifact.ContentType);
        Assert.Equal(1, renderer.CallCount);
    }

    [Fact]
    public async Task EmptySource_ProducesNoArtifactRatherThanAnEmptyOne()
    {
        var renderer = new StubRenderer(available: true, image: [0x89, 0x50, 0x4E, 0x47]);
        var text = TextFixtures.Empty();

        var result = await new TextPreviewPlugin(TextPluginOptions.Default, renderer).GeneratePreviewAsync(
            new MediaItem(".txt", "text/plain", text.Length),
            new ByteArrayMediaSource(text),
            progress: null,
            CancellationToken.None);

        Assert.Empty(result.Artifacts);
        Assert.Equal(0, renderer.CallCount);
    }

    [Fact]
    public async Task RenderedExcerpt_ProducesABoundedPng()
    {
        var text = TextFixtures.Utf8(new string('w', 400) + "\nsecond line");
        var plugin = new TextPreviewPlugin(new TextPluginOptions(maxThumbnailDimension: 320));

        var result = await plugin.GeneratePreviewAsync(
            new MediaItem(".txt", "text/plain", text.Length),
            new ByteArrayMediaSource(text),
            progress: null,
            CancellationToken.None);

        Assert.Equal(2, result.Artifacts.Count);
        var artifact = result.Artifacts[1];
        Assert.Equal("image/png", artifact.ContentType);

        var bytes = artifact.Data.ToArray();
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, bytes[..8]);

        using var bitmap = SKBitmap.Decode(bytes);
        Assert.NotNull(bitmap);
        Assert.Equal(320, bitmap.Width);
        Assert.InRange(bitmap.Height, 321, 480);
    }

    [Fact]
    public async Task BinaryContent_IsRejectedInsteadOfRenderedAsArbitraryBytes()
    {
        await Assert.ThrowsAsync<BinaryContentRejectedException>(
            () => RenderAsync(TextFixtures.BinaryMasqueradingAsText(), ".txt"));
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeReadingTheSource()
    {
        var text = TextFixtures.Utf8();
        var source = new ByteArrayMediaSource(text);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TextPreviewPlugin().GeneratePreviewAsync(
            new MediaItem(".txt", "text/plain", text.Length), source, null, cancelled.Token));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task Rendering_LeavesTheSourceBytesUnchanged()
    {
        var text = TextFixtures.Markdown();
        var before = (byte[])text.Clone();

        await RenderAsync(text, ".md");

        Assert.Equal(before, text);
    }

    private static async Task<MediaPreviewResult> RenderAsync(byte[] text, string extension) =>
        await new TextPreviewPlugin().GeneratePreviewAsync(
            new MediaItem(extension, "text/plain", text.Length),
            new ByteArrayMediaSource(text),
            progress: null,
            CancellationToken.None);

    private static string Decode(byte[] bytes) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);

    private sealed class StubRenderer(bool available, byte[]? image) : ITextThumbnailRenderer
    {
        public bool IsAvailable => available;

        public int CallCount { get; private set; }

        public string? Text { get; private set; }

        public int MaxDimension { get; private set; }

        public Task<byte[]?> RenderAsync(string text, int maxDimension, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Text = text;
            MaxDimension = maxDimension;
            return Task.FromResult(image);
        }
    }
}
