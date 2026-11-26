using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Office.Exceptions;
using SkyCD.Plugin.Metadata.Office.Models;
using Xunit;
using SkyCD.TestSupport;

namespace SkyCD.Plugin.Metadata.Office.Tests;

public class OfficePreviewPluginTests
{
    [Fact]
    public void Descriptor_ClaimsTheSameSourcesAndAnIdentityOnlySchema()
    {
        var descriptor = new OfficePreviewPlugin().Descriptor;
        var metadata = new OfficeMetadataPlugin().Descriptor;

        Assert.Equal(metadata.Extensions, descriptor.Extensions);
        Assert.Equal(metadata.ContentTypes, descriptor.ContentTypes);
        Assert.Equal("skycd.office.preview", descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
        Assert.Empty(descriptor.Schema.Fields);
    }

    [Fact]
    public async Task EmbeddedJpeg_IsPublishedAsTheImageArtifact()
    {
        var preview = await PreviewAsync(OfficeFixtures.DocxWithThumbnail());

        var artifact = Assert.Single(preview.Artifacts);
        Assert.Equal("image/jpeg", artifact.ContentType);
        Assert.Equal(0xFF, artifact.Data.Span[0]);
        Assert.Equal(0xD8, artifact.Data.Span[1]);
    }

    [Fact]
    public async Task DocumentWithoutAThumbnail_PublishesNoArtifacts()
    {
        var preview = await PreviewAsync(OfficeFixtures.Docx(OfficeFixtures.FullCore(), OfficeFixtures.FullApp()));

        Assert.Empty(preview.Artifacts);
    }

    [Fact]
    public async Task ThumbnailAboveThePluginByteBudget_IsNotPublished()
    {
        var thumbnail = OfficeFixtures.Jpeg(96, 64);
        var package = OfficeFixtures.DocxWithThumbnail(thumbnail);

        var preview = await PreviewAsync(package, new OfficePluginOptions(maxPreviewBytes: thumbnail.Length - 1));

        Assert.Empty(preview.Artifacts);
    }

    [Fact]
    public async Task ThumbnailAboveThePixelBound_IsNotPublished()
    {
        var preview = await PreviewAsync(OfficeFixtures.DocxWithThumbnail(), new OfficePluginOptions(maxThumbnailDimension: 32));

        Assert.Empty(preview.Artifacts);
    }

    [Fact]
    public async Task ThumbnailThatIsNotAJpeg_IsNotPublished()
    {
        var package = OfficeFixtures.WithBinaryEntry(
            OfficeFixtures.Docx(OfficeFixtures.FullCore()),
            "docProps/thumbnail.jpeg",
            [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00]);

        var preview = await PreviewAsync(package);

        Assert.Empty(preview.Artifacts);
    }

    [Fact]
    public async Task EncryptedPackage_PublishesNoArtifacts()
    {
        var preview = await PreviewAsync(OfficeFixtures.EncryptedPackage());

        Assert.Empty(preview.Artifacts);
    }

    [Fact]
    public async Task LegacyBitmapThumbnail_IsPublishedWrappedInABitmapContainer()
    {
        var dib = OfficeFixtures.Dib(width: 8, height: 6);
        var document = OfficeFixtures.LegacyWord(
            [.. OfficeFixtures.FullSummary(), OfficeFixtures.ClipboardThumbnail(8, dib)]);

        var preview = await PreviewAsync(document, item: LegacyItem(document));

        var artifact = Assert.Single(preview.Artifacts);
        Assert.Equal("image/bmp", artifact.ContentType);
        Assert.Equal((byte)'B', artifact.Data.Span[0]);
        Assert.Equal((byte)'M', artifact.Data.Span[1]);
        Assert.Equal(14 + dib.Length, BinaryPrimitives.ReadInt32LittleEndian(artifact.Data.Span[2..6]));
        Assert.Equal(54, BinaryPrimitives.ReadInt32LittleEndian(artifact.Data.Span[10..14]));
    }

    [Fact]
    public async Task LegacyMetafileThumbnail_IsNotRendered()
    {
        var document = OfficeFixtures.LegacyWord(
            [.. OfficeFixtures.FullSummary(), OfficeFixtures.ClipboardThumbnail(3, [0x01, 0x02, 0x03, 0x04])]);

        var preview = await PreviewAsync(document, item: LegacyItem(document));

        Assert.Empty(preview.Artifacts);
    }

    [Fact]
    public async Task LegacyDocumentWithoutAThumbnail_PublishesNoArtifacts()
    {
        var document = OfficeFixtures.LegacyWord(OfficeFixtures.FullSummary());

        var preview = await PreviewAsync(document, item: LegacyItem(document));

        Assert.Empty(preview.Artifacts);
    }

    [Fact]
    public async Task LegacyThumbnailAboveThePixelBound_IsNotPublished()
    {
        var dib = OfficeFixtures.Dib(width: 64, height: 64);
        var document = OfficeFixtures.LegacyWord(
            [.. OfficeFixtures.FullSummary(), OfficeFixtures.ClipboardThumbnail(8, dib)]);

        var preview = await PreviewAsync(document, new OfficePluginOptions(maxThumbnailDimension: 32),
            LegacyItem(document));

        Assert.Empty(preview.Artifacts);
    }

    [Fact]
    public async Task MalformedPackage_IsReportedAsADecodeFailure()
    {
        var document = OfficeFixtures.Truncate(OfficeFixtures.DocxWithThumbnail(), 100);

        await Assert.ThrowsAsync<OfficeDecodeException>(() => PreviewAsync(document));
    }

    [Fact]
    public async Task SourceThatIsNotAnOfficeDocument_IsReportedAsADecodeFailure()
    {
        var document = "This is not an Office document."u8.ToArray();

        await Assert.ThrowsAsync<OfficeDecodeException>(() => PreviewAsync(document));
    }

    [Fact]
    public async Task OversizedSource_IsRejectedBeforeAnyByteIsRead()
    {
        var document = OfficeFixtures.DocxWithThumbnail();
        var source = new ByteArrayMediaSource(document);
        var plugin = new OfficePreviewPlugin(new OfficePluginOptions(maxSourceBytes: document.Length - 1));

        await Assert.ThrowsAsync<OfficeSourceTooLargeException>(() => plugin.GeneratePreviewAsync(
            Item(document), source, null, CancellationToken.None));

        Assert.Equal(0, source.ChargedBytes);
        Assert.Equal(0, source.ReadCount);
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeReadingTheSource()
    {
        var document = OfficeFixtures.DocxWithThumbnail();
        var source = new ByteArrayMediaSource(document);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new OfficePreviewPlugin().GeneratePreviewAsync(
            Item(document), source, null, cancelled.Token));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task ReadingChargesTheSourceOnceAndPublishesBoundedProgress()
    {
        var document = OfficeFixtures.DocxWithThumbnail();
        var source = new ByteArrayMediaSource(document);
        var reports = new CollectingProgress();

        await new OfficePreviewPlugin().GeneratePreviewAsync(
            Item(document), source, reports, CancellationToken.None);

        Assert.Equal(document.Length, source.ChargedBytes);
        Assert.Equal(1, source.ReadCount);
        Assert.NotEmpty(reports.Values);
        Assert.Equal(1, reports.Values[^1]);
        Assert.All(reports.Values, value => Assert.InRange(value, 0, 1));
    }

    private static Task<MediaPreviewResult> PreviewAsync(byte[] document, OfficePluginOptions? options = null,
        MediaItem? item = null) =>
        new OfficePreviewPlugin(options ?? OfficePluginOptions.Default).GeneratePreviewAsync(
            item ?? Item(document),
            new ByteArrayMediaSource(document),
            progress: null,
            CancellationToken.None);

    private static MediaItem Item(byte[] document) =>
        new(".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", document.Length);

    private static MediaItem LegacyItem(byte[] document) => new(".doc", "application/msword", document.Length);

    private sealed class CollectingProgress : IProgress<MediaProgress>
    {
        public System.Collections.Generic.List<double> Values { get; } = [];

        public void Report(MediaProgress value) => Values.Add(value.Fraction);
    }
}
