using System;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using SkyCD.Formatting.Rtf;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Rtf.Exceptions;
using Xunit;
using SkyCD.Plugin.Rtf.Export;
using SkyCD.Plugin.Rtf.Models;

namespace SkyCD.Plugin.Rtf.Tests;

public class RtfPreviewPluginTests
{
    [Fact]
    public void Descriptor_ClaimsOnlyRtfAndAVersionedPreviewSchema()
    {
        var descriptor = new RtfPreviewPlugin().Descriptor;

        Assert.Equal([".rtf"], descriptor.Extensions);
        Assert.Equal(["application/rtf", "text/rtf"], descriptor.ContentTypes);
        Assert.Equal("skycd.rtf.preview", descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
        Assert.Empty(descriptor.Schema.Fields);
    }

    [Fact]
    public async Task RenderedDocument_ProducesABoundedPng()
    {
        var rtf = RtfFixtures.Document(body: @"{\b Bold} plain text\par Second line");
        var plugin = new RtfPreviewPlugin(new RtfPluginOptions(maxThumbnailDimension: 320));

        var result = await plugin.GeneratePreviewAsync(
            new MediaItem(".rtf", "application/rtf", rtf.Length),
            new ByteArrayMediaSource(rtf),
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
    public async Task TextlessDocument_ProducesNoArtifact()
    {
        var rtf = RtfFixtures.EmptyBody();

        var result = await new RtfPreviewPlugin().GeneratePreviewAsync(
            new MediaItem(".rtf", "application/rtf", rtf.Length),
            new ByteArrayMediaSource(rtf),
            progress: null,
            CancellationToken.None);

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task MissingRenderer_LeavesTheSourceUnread()
    {
        var rtf = RtfFixtures.Document();
        var source = new ByteArrayMediaSource(rtf);
        var plugin = new RtfPreviewPlugin(RtfPluginOptions.Default, new StubRenderer(available: false, image: null));

        var result = await plugin.GeneratePreviewAsync(
            new MediaItem(".rtf", "application/rtf", rtf.Length), source, null, CancellationToken.None);

        Assert.Empty(result.Artifacts);
        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task RendererWithoutAnImage_ProducesNoArtifact()
    {
        var rtf = RtfFixtures.Document();
        var plugin = new RtfPreviewPlugin(RtfPluginOptions.Default, new StubRenderer(available: true, image: null));

        var result = await plugin.GeneratePreviewAsync(
            new MediaItem(".rtf", "application/rtf", rtf.Length),
            new ByteArrayMediaSource(rtf),
            progress: null,
            CancellationToken.None);

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task RendererOutput_IsPublishedAsAPngArtifact()
    {
        var rtf = RtfFixtures.Document();
        var image = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var plugin = new RtfPreviewPlugin(RtfPluginOptions.Default, new StubRenderer(available: true, image: image));

        var result = await plugin.GeneratePreviewAsync(
            new MediaItem(".rtf", "application/rtf", rtf.Length),
            new ByteArrayMediaSource(rtf),
            progress: null,
            CancellationToken.None);

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("image/png", artifact.ContentType);
        Assert.Equal(image, artifact.Data.ToArray());
    }

    [Fact]
    public async Task NonRtfSource_FailsWithADecodeError()
    {
        await Assert.ThrowsAsync<RtfDecodeException>(() => new RtfPreviewPlugin().GeneratePreviewAsync(
            new MediaItem(".rtf", "application/rtf", 4),
            new ByteArrayMediaSource("nope"u8.ToArray()),
            progress: null,
            CancellationToken.None));
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeReadingTheSource()
    {
        var rtf = RtfFixtures.Document();
        var source = new ByteArrayMediaSource(rtf);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RtfPreviewPlugin().GeneratePreviewAsync(
            new MediaItem(".rtf", "application/rtf", rtf.Length), source, null, cancelled.Token));

        Assert.Equal(0, source.ChargedBytes);
    }

    private sealed class StubRenderer(bool available, byte[]? image) : IRtfThumbnailRenderer
    {
        public bool IsAvailable => available;

        public Task<byte[]?> RenderAsync(
            RichTextDocument document,
            int maxDimension,
            int maxCharacters,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(image);
        }
    }
}
