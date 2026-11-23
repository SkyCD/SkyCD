using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.AbiWord.Exceptions;
using Xunit;
using SkyCD.Plugin.Metadata.AbiWord.Export;
using SkyCD.Plugin.Metadata.AbiWord.Models;

namespace SkyCD.Plugin.Metadata.AbiWord.Tests;

public class AbiWordPreviewPluginTests
{
    [Fact]
    public void Descriptor_ClaimsAbiWordDocumentsAndAVersionedPreviewSchema()
    {
        var descriptor = new AbiWordPreviewPlugin().Descriptor;
        var metadata = new AbiWordMetadataPlugin().Descriptor;

        Assert.Equal([".abw", ".zabw"], descriptor.Extensions);
        Assert.Equal(["application/x-abiword"], descriptor.ContentTypes);
        Assert.Equal(metadata.Extensions, descriptor.Extensions);
        Assert.Equal(metadata.ContentTypes, descriptor.ContentTypes);
        Assert.Equal(AbiWordSchema.PreviewSchemaId, descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
        Assert.Empty(descriptor.Schema.Fields);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenderedBody_ProducesABoundedPng(bool compressed)
    {
        var document = compressed
            ? AbiWordFixtures.Compressed(
                AbiWordFixtures.Paragraph("Quarterly operations summary for the regional offices.") +
                AbiWordFixtures.Paragraph("Second paragraph with enough words to wrap onto another line."))
            : AbiWordFixtures.Plain(
                AbiWordFixtures.Paragraph("Quarterly operations summary for the regional offices.") +
                AbiWordFixtures.Paragraph("Second paragraph with enough words to wrap onto another line."));
        var plugin = new AbiWordPreviewPlugin(new AbiWordPluginOptions(maxThumbnailDimension: 320));

        var result = await plugin.GeneratePreviewAsync(
            new MediaItem(compressed ? ".zabw" : ".abw", "application/x-abiword", document.Length),
            new ByteArrayMediaSource(document),
            progress: null,
            CancellationToken.None);

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("image/png", artifact.ContentType);

        var bytes = artifact.Data.ToArray();
        Assert.True(bytes.Length > 8);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, bytes[..8]);

        using var bitmap = SKBitmap.Decode(bytes);
        Assert.NotNull(bitmap);
        Assert.Equal(320, bitmap.Width);
        Assert.InRange(bitmap.Height, 321, 480);
    }

    [Fact]
    public async Task RecoveredText_IsBoundedByTheConfiguredCharacterCount()
    {
        var document = AbiWordFixtures.Plain(
            AbiWordFixtures.Paragraph("Hello") + AbiWordFixtures.Paragraph("World"));
        var renderer = new StubRenderer(available: true, image: null);
        var plugin = new AbiWordPreviewPlugin(
            new AbiWordPluginOptions(maxThumbnailDimension: 128, maxRenderedCharacters: 8),
            renderer);

        await plugin.GeneratePreviewAsync(
            new MediaItem(".abw", null, document.Length),
            new ByteArrayMediaSource(document),
            progress: null,
            CancellationToken.None);

        Assert.Equal("Hello\nWo", renderer.Text);
        Assert.Equal(128, renderer.MaxDimension);
        Assert.Equal(8, renderer.MaxCharacters);
    }

    [Fact]
    public async Task TextlessDocument_ProducesNoArtifact()
    {
        var document = AbiWordFixtures.Plain(AbiWordFixtures.EmptyParagraph);
        var renderer = new StubRenderer(available: true, image: [1, 2, 3]);

        var result = await new AbiWordPreviewPlugin(AbiWordPluginOptions.Default, renderer)
            .GeneratePreviewAsync(
                new MediaItem(".abw", null, document.Length),
                new ByteArrayMediaSource(document),
                progress: null,
                CancellationToken.None);

        Assert.Empty(result.Artifacts);
        Assert.Null(renderer.Text);
    }

    [Fact]
    public async Task DocumentWithoutSection_ProducesNoArtifact()
    {
        var document = AbiWordFixtures.Bytes("<abiword><metadata><m key='dc.title'>Title</m></metadata></abiword>");

        var result = await new AbiWordPreviewPlugin().GeneratePreviewAsync(
            new MediaItem(".abw", null, document.Length),
            new ByteArrayMediaSource(document),
            progress: null,
            CancellationToken.None);

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task MalformedXml_ProducesAnEmptyPreviewWithoutLosingMetadata()
    {
        var malformed = AbiWordFixtures.Bytes("<abiword><section><p>unfinished");

        var preview = await new AbiWordPreviewPlugin().GeneratePreviewAsync(
            new MediaItem(".abw", null, malformed.Length),
            new ByteArrayMediaSource(malformed),
            progress: null,
            CancellationToken.None);
        Assert.Empty(preview.Artifacts);

        var headerOnly = AbiWordFixtures.Plain(
            AbiWordFixtures.EmptyParagraph,
            "<metadata><m key='dc.title'>Kept</m></metadata>");
        var metadata = await new AbiWordMetadataPlugin().ExtractAsync(
            new MediaItem(".abw", null, headerOnly.Length),
            new ByteArrayMediaSource(headerOnly),
            progress: null,
            CancellationToken.None);
        Assert.Equal("Kept", metadata.Values.Single(value => value.Key == AbiWordSchema.Title).Text);
    }

    [Fact]
    public async Task NonAbiWordSource_ProducesNoArtifact()
    {
        var document = AbiWordFixtures.Bytes("<document><section><p>not AbiWord</p></section></document>");

        var result = await new AbiWordPreviewPlugin().GeneratePreviewAsync(
            new MediaItem(".abw", null, document.Length),
            new ByteArrayMediaSource(document),
            progress: null,
            CancellationToken.None);

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task DamagedGzip_FailsWithADecodeError()
    {
        var document = AbiWordFixtures.Compressed(AbiWordFixtures.Paragraph("Hello"));
        document[^4] ^= 0xff;

        await Assert.ThrowsAsync<AbiWordDecodeException>(() => new AbiWordPreviewPlugin()
            .GeneratePreviewAsync(
                new MediaItem(".zabw", null, document.Length),
                new ByteArrayMediaSource(document),
                progress: null,
                CancellationToken.None));
    }

    [Fact]
    public async Task MissingRenderer_LeavesTheSourceUnread()
    {
        var document = AbiWordFixtures.Plain(AbiWordFixtures.Paragraph("Hello"));
        var source = new ByteArrayMediaSource(document);
        var plugin = new AbiWordPreviewPlugin(
            AbiWordPluginOptions.Default, new StubRenderer(available: false, image: null));

        var result = await plugin.GeneratePreviewAsync(
            new MediaItem(".abw", null, document.Length), source, null, CancellationToken.None);

        Assert.Empty(result.Artifacts);
        Assert.Equal(0, source.ChargedBytes);
        Assert.Equal(0, source.ReadCount);
    }

    [Fact]
    public async Task RendererWithoutAnImage_ProducesNoArtifact()
    {
        var document = AbiWordFixtures.Plain(AbiWordFixtures.Paragraph("Hello"));

        var result = await new AbiWordPreviewPlugin(
                AbiWordPluginOptions.Default, new StubRenderer(available: true, image: null))
            .GeneratePreviewAsync(
                new MediaItem(".abw", null, document.Length),
                new ByteArrayMediaSource(document),
                progress: null,
                CancellationToken.None);

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task RendererOutput_IsPublishedAsAPngArtifact()
    {
        var document = AbiWordFixtures.Plain(AbiWordFixtures.Paragraph("Hello"));
        var image = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        var result = await new AbiWordPreviewPlugin(
                AbiWordPluginOptions.Default, new StubRenderer(available: true, image: image))
            .GeneratePreviewAsync(
                new MediaItem(".abw", null, document.Length),
                new ByteArrayMediaSource(document),
                progress: null,
                CancellationToken.None);

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("image/png", artifact.ContentType);
        Assert.Equal(image, artifact.Data.ToArray());
    }

    [Fact]
    public async Task RunsAndEmptyParagraphs_AreJoinedIntoLeadText()
    {
        var document = AbiWordFixtures.Plain(
            AbiWordFixtures.EmptyParagraph +
            AbiWordFixtures.RunParagraph("Hello ", "world") +
            AbiWordFixtures.EmptyParagraph +
            AbiWordFixtures.Paragraph("Second"));
        var renderer = new StubRenderer(available: true, image: [7]);

        await new AbiWordPreviewPlugin(AbiWordPluginOptions.Default, renderer).GeneratePreviewAsync(
            new MediaItem(".abw", null, document.Length),
            new ByteArrayMediaSource(document),
            progress: null,
            CancellationToken.None);

        Assert.Equal("Hello world\nSecond", renderer.Text);
    }

    [Fact]
    public async Task OversizedSource_IsRejectedBeforeAnyRead()
    {
        var document = AbiWordFixtures.Plain(AbiWordFixtures.Paragraph("Hello"));
        var source = new ByteArrayMediaSource(document);

        await Assert.ThrowsAsync<AbiWordLimitExceededException>(() =>
            new AbiWordPreviewPlugin(new AbiWordPluginOptions(maxSourceBytes: 100))
                .GeneratePreviewAsync(
                    new MediaItem(".abw", null, document.Length), source, null, CancellationToken.None));

        Assert.Equal(0, source.ChargedBytes);
        Assert.Equal(0, source.ReadCount);
    }

    [Fact]
    public async Task OversizedDecodedSource_IsRejected()
    {
        var document = AbiWordFixtures.Compressed(
            AbiWordFixtures.Paragraph(new string('a', 4096)));

        await Assert.ThrowsAsync<AbiWordLimitExceededException>(() =>
            new AbiWordPreviewPlugin(new AbiWordPluginOptions(maxDecompressedBytes: 1024))
                .GeneratePreviewAsync(
                    new MediaItem(".zabw", null, document.Length),
                    new ByteArrayMediaSource(document),
                    progress: null,
                    CancellationToken.None));
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeReadingTheSource()
    {
        var document = AbiWordFixtures.Plain(AbiWordFixtures.Paragraph("Hello"));
        var source = new ByteArrayMediaSource(document);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AbiWordPreviewPlugin()
            .GeneratePreviewAsync(
                new MediaItem(".abw", null, document.Length), source, null, cancelled.Token));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task Source_IsReadOnceAndChargedExactlyOnce()
    {
        var document = AbiWordFixtures.Plain(AbiWordFixtures.Paragraph("Hello"));
        var source = new ByteArrayMediaSource(document);

        await new AbiWordPreviewPlugin(AbiWordPluginOptions.Default,
                new StubRenderer(available: true, image: null))
            .GeneratePreviewAsync(
                new MediaItem(".abw", null, document.Length), source, null, CancellationToken.None);

        Assert.Equal(document.Length, source.ChargedBytes);
        Assert.Equal(1, source.ReadCount);
    }

    [Fact]
    public async Task Progress_ReportsTheRenderingStepsAndCompletion()
    {
        var progress = new CollectingProgress();
        var document = AbiWordFixtures.Plain(AbiWordFixtures.Paragraph("Hello"));

        await new AbiWordPreviewPlugin(AbiWordPluginOptions.Default,
                new StubRenderer(available: true, image: [1]))
            .GeneratePreviewAsync(
                new MediaItem(".abw", null, document.Length),
                new ByteArrayMediaSource(document),
                progress,
                CancellationToken.None);

        Assert.Equal([0.5, 1], progress.Reports);
    }

    [Fact]
    public async Task SkiaRenderer_FailsClosedWhenItCannotDraw()
    {
        var renderer = new SkiaAbiWordThumbnailRenderer();

        Assert.True(renderer.IsAvailable);
        Assert.Null(await renderer.RenderAsync(" \n ", 128, 100, CancellationToken.None));

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            renderer.RenderAsync("Some text to draw", 128, 100, cancelled.Token));
    }

    private sealed class StubRenderer(bool available, byte[]? image) : IAbiWordThumbnailRenderer
    {
        public string? Text { get; private set; }

        public int? MaxDimension { get; private set; }

        public int? MaxCharacters { get; private set; }

        public bool IsAvailable => available;

        public Task<byte[]?> RenderAsync(
            string text,
            int maxDimension,
            int maxCharacters,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Text = text;
            MaxDimension = maxDimension;
            MaxCharacters = maxCharacters;
            return Task.FromResult(image);
        }
    }

    private sealed class CollectingProgress : IProgress<MediaProgress>
    {
        public List<double> Reports { get; } = [];

        public void Report(MediaProgress value) => Reports.Add(value.Fraction);
    }
}
