using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using Xunit;
using SkyCD.Plugin.Metadata.IWork.Models;

namespace SkyCD.Plugin.Metadata.IWork.Tests;

public class IWorkPreviewPluginTests
{
    [Fact]
    public void Descriptor_ClaimsTheSameSourcesAndAnIdentityOnlySchema()
    {
        var descriptor = new IWorkPreviewPlugin().Descriptor;

        Assert.Equal([".pages", ".numbers", ".key"], descriptor.Extensions);
        Assert.Equal("skycd.iwork.preview", descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
        Assert.Empty(descriptor.Schema.Fields);
    }

    [Fact]
    public async Task EmbeddedPng_IsPublishedAsTheImageArtifact()
    {
        var preview = await PreviewAsync(IWorkFixtures.Build());

        var artifact = Assert.Single(preview.Artifacts);
        Assert.Equal("image/png", artifact.ContentType);
        Assert.Equal(IWorkFixtures.PreviewWidth, ReadPngWidth(artifact.Data.Span));
        Assert.Equal(IWorkFixtures.PreviewHeight, ReadPngHeight(artifact.Data.Span));
    }

    [Fact]
    public async Task EmbeddedJpeg_IsPublishedWithItsOwnContentType()
    {
        var preview = await PreviewAsync(IWorkFixtures.Build(new PackageOptions { JpegPreview = true }));

        var artifact = Assert.Single(preview.Artifacts);
        Assert.Equal("image/jpeg", artifact.ContentType);
        Assert.Equal(0xFF, artifact.Data.Span[0]);
        Assert.Equal(0xD8, artifact.Data.Span[1]);
    }

    [Fact]
    public async Task PackageWithoutAPreview_PublishesNoArtifacts()
    {
        var preview = await PreviewAsync(IWorkFixtures.Build(new PackageOptions { IncludePreview = false }));

        Assert.Empty(preview.Artifacts);
    }

    [Fact]
    public async Task ProtectedPackage_PublishesNoArtifacts()
    {
        var preview = await PreviewAsync(IWorkFixtures.Build(new PackageOptions { PasswordProtected = true }));

        Assert.Empty(preview.Artifacts);
    }

    [Fact]
    public async Task ThumbnailAboveThePluginBudget_IsNotPublished()
    {
        var preview = await PreviewAsync(
            IWorkFixtures.Build(),
            new IWorkPluginOptions(maxPreviewBytes: 64));

        Assert.Empty(preview.Artifacts);
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeReadingTheSource()
    {
        var package = IWorkFixtures.Build();
        var source = new ByteArrayMediaSource(package);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new IWorkPreviewPlugin().GeneratePreviewAsync(
            new MediaItem(".pages", "application/x-iwork-pages-sffpages", package.Length),
            source,
            null,
            cancelled.Token));

        Assert.Equal(0, source.ChargedBytes);
    }

    private static Task<MediaPreviewResult> PreviewAsync(byte[] package, IWorkPluginOptions? options = null) =>
        new IWorkPreviewPlugin(options ?? IWorkPluginOptions.Default).GeneratePreviewAsync(
            new MediaItem(".pages", "application/x-iwork-pages-sffpages", package.Length),
            new ByteArrayMediaSource(package),
            progress: null,
            CancellationToken.None);

    private static int ReadPngWidth(ReadOnlySpan<byte> png) => BinaryPrimitives.ReadInt32BigEndian(png[16..20]);

    private static int ReadPngHeight(ReadOnlySpan<byte> png) => BinaryPrimitives.ReadInt32BigEndian(png[20..24]);
}
