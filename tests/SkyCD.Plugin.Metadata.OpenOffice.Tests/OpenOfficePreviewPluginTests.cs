using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.OpenOffice.Models;
using Xunit;

namespace SkyCD.Plugin.Metadata.OpenOffice.Tests;

public class OpenOfficePreviewPluginTests
{
    [Fact]
    public void Descriptor_ClaimsTheSameSourcesAndAnIdentityOnlySchema()
    {
        var descriptor = new OpenOfficePreviewPlugin().Descriptor;
        var metadata = new OpenOfficeMetadataPlugin().Descriptor;

        Assert.Equal(metadata.Extensions, descriptor.Extensions);
        Assert.Equal(metadata.ContentTypes, descriptor.ContentTypes);
        Assert.Equal(OpenOfficeSchema.PreviewSchemaId, descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
        Assert.Empty(descriptor.Schema.Fields);
        Assert.False(descriptor.IsOptIn);
        Assert.False(descriptor.IndexTimePreview);
    }

    [Fact]
    public async Task EmbeddedPreview_IsPublishedAsThePngArtifact()
    {
        var document = OpenOfficeFixtures.OdfDocumentWithThumbnail();

        var preview = await PreviewAsync(document);

        var artifact = Assert.Single(preview.Artifacts);
        Assert.Equal("image/png", artifact.ContentType);
        Assert.Equal(0x89, artifact.Data.Span[0]);
        Assert.Equal((byte)'P', artifact.Data.Span[1]);
        Assert.Equal(OpenOfficeFixtures.Png(96, 64).Length, artifact.Data.Length);
    }

    [Fact]
    public async Task LegacyPackage_WithTheSameEntry_PublishesTheSameArtifact()
    {
        var document = OpenOfficeFixtures.LegacyDocumentWithThumbnail();

        var preview = await PreviewAsync(document, item: new MediaItem(".sxw", null, document.Length));

        var artifact = Assert.Single(preview.Artifacts);
        Assert.Equal("image/png", artifact.ContentType);
    }

    [Fact]
    public async Task DifferentlyCasedMemberName_IsStillFound()
    {
        var preview = await PreviewAsync(OpenOfficeFixtures.OdfDocumentWithCasedThumbnail());

        Assert.Single(preview.Artifacts);
    }

    [Fact]
    public async Task PackageWithoutTheEntry_PublishesNoArtifacts()
    {
        var document = OpenOfficeFixtures.OdfDocument();

        var preview = await PreviewAsync(document);

        Assert.Empty(preview.Artifacts);
    }

    [Fact]
    public async Task LegacyPackageWithoutTheEntry_PublishesNoArtifacts()
    {
        var document = OpenOfficeFixtures.LegacyDocument();

        var preview = await PreviewAsync(document, item: new MediaItem(".sxw", null, document.Length));

        Assert.Empty(preview.Artifacts);
    }

    [Fact]
    public async Task ThumbnailThatIsNotAPng_PublishesNoArtifacts()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00];
        var document = OpenOfficeFixtures.OdfDocumentWithThumbnail(jpeg);

        var preview = await PreviewAsync(document);

        Assert.Empty(preview.Artifacts);
    }

    [Fact]
    public async Task TruncatedPng_PublishesNoArtifacts()
    {
        var thumbnail = OpenOfficeFixtures.Png(96, 64)[..16];
        var document = OpenOfficeFixtures.OdfDocumentWithThumbnail(thumbnail);

        var preview = await PreviewAsync(document);

        Assert.Empty(preview.Artifacts);
    }

    [Fact]
    public async Task PngWithoutAnIhdrChunk_PublishesNoArtifacts()
    {
        var thumbnail = OpenOfficeFixtures.Png(96, 64);
        "IDAT"u8.CopyTo(thumbnail.AsSpan(12));
        var document = OpenOfficeFixtures.OdfDocumentWithThumbnail(thumbnail);

        var preview = await PreviewAsync(document);

        Assert.Empty(preview.Artifacts);
    }

    [Fact]
    public async Task ThumbnailAboveTheByteBound_PublishesNoArtifacts()
    {
        var thumbnail = OpenOfficeFixtures.Png(96, 64);
        var document = OpenOfficeFixtures.OdfDocumentWithThumbnail(thumbnail);

        var preview = await PreviewAsync(document, new OpenOfficePluginOptions(maxPreviewBytes: thumbnail.Length - 1));

        Assert.Empty(preview.Artifacts);
    }

    [Fact]
    public async Task ThumbnailAboveThePixelBound_PublishesNoArtifacts()
    {
        var preview = await PreviewAsync(OpenOfficeFixtures.OdfDocumentWithThumbnail(),
            new OpenOfficePluginOptions(maxPreviewDimension: 32));

        Assert.Empty(preview.Artifacts);
    }

    [Fact]
    public async Task ThumbnailWithADegenerateDimension_PublishesNoArtifacts()
    {
        var document = OpenOfficeFixtures.OdfDocumentWithThumbnail(OpenOfficeFixtures.Png(0, 64));

        var preview = await PreviewAsync(document);

        Assert.Empty(preview.Artifacts);
    }

    [Fact]
    public async Task EncryptedThumbnailMember_PublishesNoArtifacts()
    {
        var preview = await PreviewAsync(OpenOfficeFixtures.OdfDocumentWithEncryptedThumbnail());

        Assert.Empty(preview.Artifacts);
    }

    [Fact]
    public async Task SourceThatIsNotAZip_PublishesNoArtifactsInsteadOfThrowing()
    {
        var preview = await PreviewAsync(OpenOfficeFixtures.NotAZip());

        Assert.Empty(preview.Artifacts);
    }

    [Fact]
    public async Task DamagedPackage_PublishesNoArtifactsInsteadOfThrowing()
    {
        var document = OpenOfficeFixtures.Truncated(OpenOfficeFixtures.OdfDocumentWithThumbnail());

        var preview = await PreviewAsync(document);

        Assert.Empty(preview.Artifacts);
    }

    [Fact]
    public async Task EmptySource_PublishesNoArtifacts()
    {
        var preview = await PreviewAsync([]);

        Assert.Empty(preview.Artifacts);
    }

    [Fact]
    public async Task OversizedSource_PublishesNoArtifactsBeforeAnyByteIsRead()
    {
        var document = OpenOfficeFixtures.OdfDocumentWithThumbnail();
        var source = new ByteArrayMediaSource(document);
        var plugin = new OpenOfficePreviewPlugin(new OpenOfficePluginOptions(maxSourceBytes: document.Length - 1));

        var preview = await plugin.GeneratePreviewAsync(
            new MediaItem(".odt", null, document.Length), source, null, CancellationToken.None);

        Assert.Empty(preview.Artifacts);
        Assert.Equal(0, source.ChargedBytes);
        Assert.Equal(0, source.ReadCount);
    }

    [Fact]
    public async Task CancelledToken_ThrowsInsteadOfReturningAnEmptyResult()
    {
        var document = OpenOfficeFixtures.OdfDocumentWithThumbnail();
        var source = new ByteArrayMediaSource(document);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new OpenOfficePreviewPlugin()
            .GeneratePreviewAsync(new MediaItem(".odt", null, document.Length), source, null, cancelled.Token));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task ReadingChargesTheSourceOnceAndPublishesBoundedProgress()
    {
        var document = OpenOfficeFixtures.OdfDocumentWithThumbnail();
        var source = new ByteArrayMediaSource(document);
        var reports = new CollectingProgress();

        await new OpenOfficePreviewPlugin().GeneratePreviewAsync(
            new MediaItem(".odt", null, document.Length), source, reports, CancellationToken.None);

        Assert.Equal(document.Length, source.ChargedBytes);
        Assert.Equal(1, source.ReadCount);
        Assert.Equal(2, reports.Values.Count);
        Assert.Equal(0.5, reports.Values[0]);
        Assert.Equal(1, reports.Values[1]);
    }

    private static Task<MediaPreviewResult> PreviewAsync(byte[] document, OpenOfficePluginOptions? options = null,
        MediaItem? item = null) =>
        new OpenOfficePreviewPlugin(options ?? OpenOfficePluginOptions.Default).GeneratePreviewAsync(
            item ?? new MediaItem(".odt", null, document.Length),
            new ByteArrayMediaSource(document),
            progress: null,
            CancellationToken.None);

    private sealed class CollectingProgress : IProgress<MediaProgress>
    {
        public System.Collections.Generic.List<double> Values { get; } = [];

        public void Report(MediaProgress value) => Values.Add(value.Fraction);
    }
}
