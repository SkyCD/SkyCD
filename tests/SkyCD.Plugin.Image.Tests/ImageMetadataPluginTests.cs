using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Image.Exceptions;
using Xunit;

namespace SkyCD.Plugin.Image.Tests;

public class ImageMetadataPluginTests
{
    [Fact]
    public void Descriptor_ClaimsTheDocumentedImageTypesAndAVersionedSchema()
    {
        var descriptor = new ImageMetadataPlugin().Descriptor;

        Assert.Equal(ImageSchema.Extensions, descriptor.Extensions);
        Assert.Equal(ImageSchema.ContentTypes, descriptor.ContentTypes);
        Assert.Equal("skycd.image.metadata", descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
        Assert.Contains(descriptor.Extensions, extension => extension == ".jpg");
        Assert.Contains(descriptor.Extensions, extension => extension == ".png");
        Assert.Contains(descriptor.Extensions, extension => extension == ".webp");
        Assert.Contains(descriptor.Extensions, extension => extension == ".gif");
    }

    [Fact]
    public async Task TransparentPng_ReportsDimensionsFormatAndAlpha()
    {
        var result = await ExtractAsync(ImageFixtures.TransparentPng(30, 12), ".png", "image/png");

        Assert.Equal(30L, Integer(result, ImageSchema.Width));
        Assert.Equal(12L, Integer(result, ImageSchema.Height));
        Assert.Equal("png", Text(result, ImageSchema.Format));
        Assert.True(Boolean(result, ImageSchema.HasAlpha));
        Assert.False(Boolean(result, ImageSchema.IsAnimated));
        Assert.Equal(1L, Integer(result, ImageSchema.FrameCount));
    }

    [Fact]
    public async Task OpaqueJpeg_ReportsNoAlphaAndUprightOrientation()
    {
        var result = await ExtractAsync(ImageFixtures.Jpeg(24, 16), ".jpg", "image/jpeg");

        Assert.Equal(24L, Integer(result, ImageSchema.Width));
        Assert.Equal(16L, Integer(result, ImageSchema.Height));
        Assert.Equal("jpeg", Text(result, ImageSchema.Format));
        Assert.False(Boolean(result, ImageSchema.HasAlpha));
        Assert.Equal(1L, Integer(result, ImageSchema.Orientation));
        Assert.Equal(0L, Integer(result, ImageSchema.RotationDegrees));
    }

    [Fact]
    public async Task Webp_ReportsItsContainerFormat()
    {
        var result = await ExtractAsync(ImageFixtures.Webp(16, 16), ".webp", "image/webp");

        Assert.Equal("webp", Text(result, ImageSchema.Format));
        Assert.Equal(16L, Integer(result, ImageSchema.Width));
    }

    [Fact]
    public async Task RotatedJpeg_ReportsDisplayDimensionsRatherThanStoredOnes()
    {
        var result = await ExtractAsync(ImageFixtures.JpegWithExif(40, 20, orientation: 6), ".jpg", "image/jpeg");

        Assert.Equal(6L, Integer(result, ImageSchema.Orientation));
        Assert.Equal(90L, Integer(result, ImageSchema.RotationDegrees));
        Assert.Equal(20L, Integer(result, ImageSchema.Width));
        Assert.Equal(40L, Integer(result, ImageSchema.Height));
        Assert.Equal(40L, Integer(result, ImageSchema.Height));
    }

    [Theory]
    [InlineData((ushort)1, 0)]
    [InlineData((ushort)3, 180)]
    [InlineData((ushort)6, 90)]
    [InlineData((ushort)8, 270)]
    public async Task ExifOrientation_IsPublishedAsBothCodeAndRotation(ushort orientation, int expectedDegrees)
    {
        var result = await ExtractAsync(ImageFixtures.JpegWithExif(20, 20, orientation), ".jpg", "image/jpeg");

        Assert.Equal(orientation, Integer(result, ImageSchema.Orientation));
        Assert.Equal(expectedDegrees, Integer(result, ImageSchema.RotationDegrees));
    }

    [Fact]
    public async Task ExifCaptureDate_IsReportedAsATypedTimestamp()
    {
        var capturedAt = new DateTime(2023, 7, 4, 12, 34, 56, DateTimeKind.Unspecified);
        var result = await ExtractAsync(ImageFixtures.JpegWithExif(16, 16, orientation: 1, capturedAt), ".jpg", "image/jpeg");

        var value = result.Values.Single(field => field.Key == ImageSchema.CapturedAt);
        Assert.Equal(MediaMetadataType.Timestamp, value.Type);
        Assert.NotNull(value.Timestamp);
        Assert.Equal(new DateTimeOffset(2023, 7, 4, 12, 34, 56, TimeSpan.Zero), value.Timestamp);
    }

    [Fact]
    public async Task SourceWithoutExif_OmitsTheCaptureDateInsteadOfGuessing()
    {
        var result = await ExtractAsync(ImageFixtures.Jpeg(16, 16), ".jpg", "image/jpeg");

        Assert.DoesNotContain(result.Values, field => field.Key == ImageSchema.CapturedAt);
    }

    [Fact]
    public async Task AnimatedGif_ReportsFrameCountAndTheAnimationFlag()
    {
        var result = await ExtractAsync(ImageFixtures.AnimatedGif(4, 4, frameCount: 3), ".gif", "image/gif");

        Assert.Equal("gif", Text(result, ImageSchema.Format));
        Assert.Equal(3L, Integer(result, ImageSchema.FrameCount));
        Assert.True(Boolean(result, ImageSchema.IsAnimated));
    }

    [Fact]
    public async Task StillImage_ReportsASingleFrame()
    {
        var result = await ExtractAsync(ImageFixtures.OpaquePng(8, 8), ".png", "image/png");

        Assert.Equal(1L, Integer(result, ImageSchema.FrameCount));
        Assert.False(Boolean(result, ImageSchema.IsAnimated));
    }

    [Fact]
    public async Task EveryReturnedValue_ConformsToTheDeclaredSchema()
    {
        var schema = ImageSchema.Metadata;
        var result = await ExtractAsync(
            ImageFixtures.JpegWithExif(12, 9, orientation: 6, new DateTime(2021, 1, 2, 3, 4, 5)),
            ".jpg",
            "image/jpeg");

        Assert.NotEmpty(result.Values);
        foreach (var value in result.Values)
        {
            var field = schema.Fields.SingleOrDefault(candidate => candidate.Key == value.Key);
            Assert.NotNull(field);
            Assert.Equal(field!.Type, value.Type);
        }

        Assert.Equal(result.Values.Count, result.Values.Select(value => value.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task CorruptSource_FailsWithADecodeError()
    {
        await Assert.ThrowsAsync<ImageDecodeException>(
            () => ExtractAsync(ImageFixtures.NotAnImage(), ".jpg", "image/jpeg"));
    }

    [Fact]
    public async Task TruncatedImage_FailsWithADecodeError()
    {
        var truncated = ImageFixtures.Truncated(ImageFixtures.Jpeg(60, 40));

        await Assert.ThrowsAsync<ImageDecodeException>(() => ExtractAsync(truncated, ".jpg", "image/jpeg"));
    }

    [Fact]
    public async Task EmptySource_FailsWithADecodeError()
    {
        await Assert.ThrowsAsync<ImageDecodeException>(() => ExtractAsync([], ".png", "image/png"));
    }

    [Fact]
    public async Task OversizedSource_IsRejectedBeforeAnyByteIsRead()
    {
        var image = ImageFixtures.Jpeg(200, 200);
        var source = new ByteArrayMediaSource(image);
        var plugin = new ImageMetadataPlugin(new ImagePluginOptions(maxSourceBytes: image.Length - 1));

        await Assert.ThrowsAsync<ImageSourceTooLargeException>(() => plugin.ExtractAsync(
            new MediaItem(".jpg", "image/jpeg", image.Length), source, null, CancellationToken.None));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task Extraction_LeavesTheSourceBytesUnchanged()
    {
        var image = ImageFixtures.JpegWithExif(32, 24, orientation: 3, new DateTime(2020, 5, 6, 7, 8, 9));
        var before = (byte[])image.Clone();

        await ExtractAsync(image, ".jpg", "image/jpeg");

        Assert.Equal(before, image);
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeReadingTheSource()
    {
        var image = ImageFixtures.Jpeg(16, 16);
        var source = new ByteArrayMediaSource(image);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ImageMetadataPlugin().ExtractAsync(
            new MediaItem(".jpg", "image/jpeg", image.Length), source, null, cancelled.Token));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task MetadataReads_StayInsideTheHostBudget()
    {
        var image = ImageFixtures.JpegWithExif(1600, 1200, orientation: 1, new DateTime(2019, 3, 2, 1, 0, 0));
        var source = new ByteArrayMediaSource(image);

        var result = await new ImageMetadataPlugin().ExtractAsync(
            new MediaItem(".jpg", "image/jpeg", image.Length), source, null, CancellationToken.None);

        Assert.Equal(1600L, Integer(result, ImageSchema.Width));
        Assert.True(source.ChargedBytes <= image.Length,
            $"Metadata charged {source.ChargedBytes} bytes for a {image.Length} byte source.");
        Assert.True(source.ReadCount < 200, $"Metadata needed {source.ReadCount} host reads; the adapter should batch.");
    }

    private static Task<MediaExtractionResult> ExtractAsync(byte[] image, string extension, string contentType) =>
        new ImageMetadataPlugin().ExtractAsync(
            new MediaItem(extension, contentType, image.Length),
            new ByteArrayMediaSource(image),
            progress: null,
            CancellationToken.None);

    private static long Integer(MediaExtractionResult result, string key) =>
        result.Values.Single(value => value.Key == key).Integer
        ?? throw new InvalidOperationException($"Field '{key}' has no integer value.");

    private static string Text(MediaExtractionResult result, string key) =>
        result.Values.Single(value => value.Key == key).Text
        ?? throw new InvalidOperationException($"Field '{key}' has no text value.");

    private static bool Boolean(MediaExtractionResult result, string key) =>
        result.Values.Single(value => value.Key == key).Boolean
        ?? throw new InvalidOperationException($"Field '{key}' has no boolean value.");
}
