using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Markdown.Export;
using SkyCD.Plugin.Metadata.Markdown.Models;
using Xunit;
using SkyCD.TestSupport;

namespace SkyCD.Plugin.Metadata.Markdown.Tests;

public sealed class MarkdownPreviewPluginTests
{
    private const string Document = "# Release Notes\n\nSome *emphasised* body text.\n\n## Section\n\n- first item\n- second item\n";

    [Fact]
    public void Descriptor_ClaimsTheSameSourcesAsMetadata_WithItsOwnPreviewSchema()
    {
        var descriptor = new MarkdownPreviewPlugin().Descriptor;

        Assert.Equal(MarkdownSchema.Extensions, descriptor.Extensions);
        Assert.Equal(MarkdownSchema.ContentTypes, descriptor.ContentTypes);
        Assert.Equal("skycd.markdown.preview", descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
        Assert.Empty(descriptor.Schema.Fields);

        // The priority is what makes this page the list thumbnail instead of the text plugin's.
        Assert.True(descriptor.Priority > 0);
    }

    [Fact]
    public async Task HeadingsAndBody_ProduceASingleBoundedPng()
    {
        var source = MarkdownFixtures.Text(Document);
        var plugin = new MarkdownPreviewPlugin(new MarkdownPluginOptions(maxThumbnailDimension: 320));

        var result = await plugin.GeneratePreviewAsync(
            new MediaItem(".md", "text/markdown", source.Length),
            new ByteArrayMediaSource(source),
            progress: null,
            CancellationToken.None);

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("image/png", artifact.ContentType);

        var bytes = artifact.Data.ToArray();
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, bytes[..8]);

        using var bitmap = SKBitmap.Decode(bytes);
        Assert.NotNull(bitmap);
        Assert.Equal(320, bitmap.Width);
        Assert.InRange(bitmap.Height, 321, 480);
    }

    [Fact]
    public async Task ZeroLengthSource_ProducesNoArtifact()
    {
        var source = new ByteArrayMediaSource([]);
        var renderer = new StubRenderer(available: true, image: [0x89, 0x50, 0x4E, 0x47]);

        var result = await new MarkdownPreviewPlugin(MarkdownPluginOptions.Default, renderer).GeneratePreviewAsync(
            new MediaItem(".md", "text/markdown", 0), source, null, CancellationToken.None);

        Assert.Empty(result.Artifacts);
        Assert.Equal(0, source.ChargedBytes);
        Assert.Equal(0, renderer.CallCount);
    }

    [Fact]
    public async Task DocumentWithNoDrawableBlocks_ProducesNoArtifactRatherThanAnEmptyOne()
    {
        var source = MarkdownFixtures.Text("<div>markup only</div>\n\n---\n");
        var renderer = new StubRenderer(available: true, image: [0x89, 0x50, 0x4E, 0x47]);

        var result = await new MarkdownPreviewPlugin(MarkdownPluginOptions.Default, renderer).GeneratePreviewAsync(
            new MediaItem(".md", "text/markdown", source.Length),
            new ByteArrayMediaSource(source),
            progress: null,
            CancellationToken.None);

        Assert.Empty(result.Artifacts);
        Assert.Equal(0, renderer.CallCount);
    }

    [Fact]
    public async Task WhitespaceOnlyDocument_ProducesNoArtifactRatherThanAnEmptyOne()
    {
        var source = MarkdownFixtures.Text("   \n\n\t\n");
        var renderer = new StubRenderer(available: true, image: [0x89, 0x50, 0x4E, 0x47]);

        var result = await new MarkdownPreviewPlugin(MarkdownPluginOptions.Default, renderer).GeneratePreviewAsync(
            new MediaItem(".md", "text/markdown", source.Length),
            new ByteArrayMediaSource(source),
            progress: null,
            CancellationToken.None);

        Assert.Empty(result.Artifacts);
        Assert.Equal(0, renderer.CallCount);
    }

    [Fact]
    public async Task FrontMatterOnlyDocument_DrawsNoPage()
    {
        var source = MarkdownFixtures.WithFrontMatter("title: Only metadata", string.Empty);
        var renderer = new StubRenderer(available: true, image: [0x89, 0x50, 0x4E, 0x47]);

        var result = await new MarkdownPreviewPlugin(MarkdownPluginOptions.Default, renderer).GeneratePreviewAsync(
            new MediaItem(".md", "text/markdown", source.Length),
            new ByteArrayMediaSource(source),
            progress: null,
            CancellationToken.None);

        Assert.Empty(result.Artifacts);
        Assert.Equal(0, renderer.CallCount);
    }

    [Fact]
    public async Task RendererReceivesTheParsedLinesAtTheConfiguredDimension()
    {
        var source = MarkdownFixtures.WithFrontMatter("title: Ignored by the page", Document);
        var renderer = new StubRenderer(available: true, image: [0x89, 0x50, 0x4E, 0x47]);
        var options = new MarkdownPluginOptions(maxThumbnailDimension: 256);

        var result = await new MarkdownPreviewPlugin(options, renderer).GeneratePreviewAsync(
            new MediaItem(".md", "text/markdown", source.Length),
            new ByteArrayMediaSource(source),
            progress: null,
            CancellationToken.None);

        Assert.Single(result.Artifacts);
        Assert.Equal(1, renderer.CallCount);
        Assert.Equal(256, renderer.MaxDimension);

        var lines = Assert.IsAssignableFrom<IReadOnlyList<MarkdownPreviewLine>>(renderer.Lines);
        Assert.Collection(lines,
            line =>
            {
                Assert.True(line.IsHeading);
                Assert.Equal("Release Notes", line.Text);
            },
            line =>
            {
                Assert.False(line.IsHeading);
                Assert.Equal("Some emphasised body text.", line.Text);
            },
            line =>
            {
                Assert.True(line.IsHeading);
                Assert.Equal("Section", line.Text);
            },
            line =>
            {
                Assert.False(line.IsHeading);
                Assert.Contains("first item", line.Text, StringComparison.Ordinal);
                Assert.Contains("second item", line.Text, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task UnavailableRenderer_ProducesNoArtifact()
    {
        var source = MarkdownFixtures.Text(Document);
        var mediaSource = new ByteArrayMediaSource(source);
        var renderer = new StubRenderer(available: false, image: [0x89, 0x50, 0x4E, 0x47]);

        var result = await new MarkdownPreviewPlugin(MarkdownPluginOptions.Default, renderer).GeneratePreviewAsync(
            new MediaItem(".md", "text/markdown", source.Length),
            mediaSource,
            progress: null,
            CancellationToken.None);

        Assert.Empty(result.Artifacts);
        Assert.Equal(0, renderer.CallCount);
        Assert.True(mediaSource.ChargedBytes > 0);
    }

    [Fact]
    public async Task RendererWithoutAnImage_ProducesNoArtifact()
    {
        var source = MarkdownFixtures.Text(Document);
        var renderer = new StubRenderer(available: true, image: null);

        var result = await new MarkdownPreviewPlugin(MarkdownPluginOptions.Default, renderer).GeneratePreviewAsync(
            new MediaItem(".md", "text/markdown", source.Length),
            new ByteArrayMediaSource(source),
            progress: null,
            CancellationToken.None);

        Assert.Empty(result.Artifacts);
        Assert.Equal(1, renderer.CallCount);
    }

    [Fact]
    public async Task OversizedSource_ProducesNoPreviewAndChargesNothing()
    {
        var source = new ByteArrayMediaSource(MarkdownFixtures.Text(Document));
        var renderer = new StubRenderer(available: true, image: [0x89, 0x50, 0x4E, 0x47]);
        var options = new MarkdownPluginOptions(maxSourceBytes: 16);

        var result = await new MarkdownPreviewPlugin(options, renderer).GeneratePreviewAsync(
            new MediaItem(".md", "text/markdown", 1024),
            source,
            progress: null,
            CancellationToken.None);

        Assert.Empty(result.Artifacts);
        Assert.Equal(0, source.ChargedBytes);
        Assert.Equal(0, renderer.CallCount);
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeReadingTheSource()
    {
        var source = new ByteArrayMediaSource(MarkdownFixtures.Text(Document));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new MarkdownPreviewPlugin().GeneratePreviewAsync(
            new MediaItem(".md", "text/markdown", source.Length), source, null, cancelled.Token));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public void NullRenderer_IsRejectedAtConstruction()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new MarkdownPreviewPlugin(MarkdownPluginOptions.Default, null!));
    }

    private sealed class StubRenderer(bool available, byte[]? image) : IMarkdownThumbnailRenderer
    {
        public bool IsAvailable => available;

        public int CallCount { get; private set; }

        public IReadOnlyList<MarkdownPreviewLine>? Lines { get; private set; }

        public int MaxDimension { get; private set; }

        public Task<byte[]?> RenderAsync(
            IReadOnlyList<MarkdownPreviewLine> lines,
            int maxDimension,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Lines = lines;
            MaxDimension = maxDimension;
            return Task.FromResult(image);
        }
    }
}
