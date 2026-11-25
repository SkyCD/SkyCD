using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Image.Exceptions;
using Xunit;
using SkyCD.Plugin.Metadata.Image.Models;

namespace SkyCD.Plugin.Metadata.Image.Tests;

public class ImagePreviewPluginTests
{
    [Fact]
    public void Descriptor_ClaimsTheDocumentedImageTypesWithAPreviewSchema()
    {
        var descriptor = new ImagePreviewPlugin().Descriptor;

        Assert.Equal(ImageSchema.Extensions, descriptor.Extensions);
        Assert.Equal(ImageSchema.ContentTypes, descriptor.ContentTypes);
        Assert.Equal("skycd.image.preview", descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
    }

    [Fact]
    public async Task LargeImage_IsScaledDownToTheLongestEdgeWithItsAspectRatio()
    {
        var thumbnail = await RenderAsync(ImageFixtures.Jpeg(1200, 800), ".jpg", "image/jpeg", maxDimension: 256);

        var (width, height) = ImageFixtures.Dimensions(thumbnail.Data);
        Assert.Equal(256, width);
        Assert.Equal(171, height);
        Assert.True(Math.Abs(width / (double)height - 1200 / 800.0) < 0.02);
    }

    [Fact]
    public async Task SmallImage_IsNeverUpscaled()
    {
        var thumbnail = await RenderAsync(ImageFixtures.OpaquePng(24, 16), ".png", "image/png", maxDimension: 256);

        Assert.Equal((24, 16), ImageFixtures.Dimensions(thumbnail.Data));
    }

    [Fact]
    public async Task VeryWideImage_KeepsAtLeastOnePixelOnTheShortEdge()
    {
        var thumbnail = await RenderAsync(ImageFixtures.OpaquePng(400, 3), ".png", "image/png", maxDimension: 64);

        var (width, height) = ImageFixtures.Dimensions(thumbnail.Data);
        Assert.Equal(64, width);
        Assert.Equal(1, height);
    }

    [Fact]
    public async Task RotatedJpeg90_ProducesAnUprightPortraitThumbnail()
    {
        // The fixture stores 40x20 with its dark quadrant top-left; upright that quadrant belongs top-right.
        var thumbnail = await RenderAsync(
            ImageFixtures.JpegWithExif(40, 20, orientation: 6), ".jpg", "image/jpeg", maxDimension: 256);

        var (width, height) = ImageFixtures.Dimensions(thumbnail.Data);
        Assert.Equal(20, width);
        Assert.Equal(40, height);
        Assert.True(IsDark(Pixel(thumbnail.Data, width - 3, height / 4)), "The dark quadrant should have moved to the top right.");
        Assert.True(IsLight(Pixel(thumbnail.Data, 2, height / 4)), "The top left should now be light.");
        Assert.True(IsLight(Pixel(thumbnail.Data, width - 3, (height * 3) / 4)), "The bottom right should now be light.");
    }

    [Fact]
    public async Task RotatedJpeg180_MovesTheDarkQuadrantToTheBottomRight()
    {
        var thumbnail = await RenderAsync(
            ImageFixtures.JpegWithExif(40, 20, orientation: 3), ".jpg", "image/jpeg", maxDimension: 256);

        Assert.Equal((40, 20), ImageFixtures.Dimensions(thumbnail.Data));
        Assert.True(IsDark(Pixel(thumbnail.Data, 35, 15)));
        Assert.True(IsLight(Pixel(thumbnail.Data, 5, 5)));
    }

    [Fact]
    public async Task MirroredJpeg_FlipsHorizontally()
    {
        var thumbnail = await RenderAsync(
            ImageFixtures.JpegWithExif(40, 20, orientation: 2), ".jpg", "image/jpeg", maxDimension: 256);

        Assert.Equal((40, 20), ImageFixtures.Dimensions(thumbnail.Data));
        Assert.True(IsDark(Pixel(thumbnail.Data, 35, 5)), "The dark quadrant should have mirrored to the top right.");
        Assert.True(IsLight(Pixel(thumbnail.Data, 5, 5)));
    }

    [Fact]
    public async Task UprightJpeg_KeepsTheDarkQuadrantTopLeft()
    {
        var thumbnail = await RenderAsync(ImageFixtures.Jpeg(40, 20), ".jpg", "image/jpeg", maxDimension: 256);

        Assert.Equal((40, 20), ImageFixtures.Dimensions(thumbnail.Data));
        Assert.True(IsDark(Pixel(thumbnail.Data, 5, 5)));
        Assert.True(IsLight(Pixel(thumbnail.Data, 35, 15)));
    }

    [Fact]
    public async Task TransparentPng_IsEncodedAsPngAndKeepsItsAlphaChannel()
    {
        var thumbnail = await RenderAsync(ImageFixtures.TransparentPng(30, 12), ".png", "image/png", maxDimension: 256);

        Assert.Equal("image/png", thumbnail.ContentType);
        Assert.Equal((30, 12), ImageFixtures.Dimensions(thumbnail.Data));
        Assert.Equal(0, Pixel(thumbnail.Data, 2, 6).Alpha);
        Assert.True(Pixel(thumbnail.Data, 26, 6).Alpha > 0);
    }

    [Fact]
    public async Task OpaqueJpeg_IsEncodedAsJpegAndStaysWithinTheHostArtifactBudget()
    {
        var thumbnail = await RenderAsync(ImageFixtures.Jpeg(900, 900), ".jpg", "image/jpeg", maxDimension: 512);

        Assert.Equal("image/jpeg", thumbnail.ContentType);
        Assert.Equal((512, 512), ImageFixtures.Dimensions(thumbnail.Data));
        Assert.True(thumbnail.Data.Length <= 4 * 1024 * 1024,
            $"Thumbnail was {thumbnail.Data.Length} bytes, above the host artifact budget.");
    }

    [Fact]
    public async Task AnimatedGif_IsPreviewedByItsFirstFrameOnly()
    {
        var thumbnail = await RenderAsync(ImageFixtures.AnimatedGif(6, 4, frameCount: 4), ".gif", "image/gif", maxDimension: 256);

        Assert.Equal((6, 4), ImageFixtures.Dimensions(thumbnail.Data));
        Assert.True(thumbnail.Data.Length > 0);
    }

    [Fact]
    public async Task CorruptSource_FailsWithADecodeError()
    {
        await Assert.ThrowsAsync<ImageDecodeException>(
            () => RenderAsync(ImageFixtures.NotAnImage(), ".jpg", "image/jpeg"));
    }

    [Fact]
    public async Task TruncatedImage_FailsWithADecodeError()
    {
        var truncated = ImageFixtures.Truncated(ImageFixtures.Jpeg(80, 60));

        await Assert.ThrowsAsync<ImageDecodeException>(
            () => RenderAsync(truncated, ".jpg", "image/jpeg"));
    }

    [Fact]
    public async Task OversizedSource_IsRejectedBeforeAnyByteIsRead()
    {
        var image = ImageFixtures.Jpeg(120, 120);
        var source = new ByteArrayMediaSource(image);
        var plugin = new ImagePreviewPlugin(new ImagePluginOptions(maxSourceBytes: image.Length - 1));

        await Assert.ThrowsAsync<ImageSourceTooLargeException>(() => plugin.GeneratePreviewAsync(
            new MediaItem(".jpg", "image/jpeg", image.Length), source, null, CancellationToken.None));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeReadingTheSource()
    {
        var image = ImageFixtures.Jpeg(32, 32);
        var source = new ByteArrayMediaSource(image);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ImagePreviewPlugin().GeneratePreviewAsync(
            new MediaItem(".jpg", "image/jpeg", image.Length), source, null, cancelled.Token));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task Rendering_LeavesTheSourceBytesUnchanged()
    {
        var image = ImageFixtures.JpegWithExif(48, 32, orientation: 6, new DateTime(2022, 11, 12, 13, 14, 15));
        var before = (byte[])image.Clone();

        await RenderAsync(image, ".jpg", "image/jpeg", maxDimension: 32);

        Assert.Equal(before, image);
    }

    [Fact]
    public async Task Progress_IsReportedFromStartToFinish()
    {
        var fractions = new System.Collections.Generic.List<double>();
        var progress = new InlineProgress(value => fractions.Add(value.Fraction));
        var image = ImageFixtures.Jpeg(200, 100);

        await new ImagePreviewPlugin().GeneratePreviewAsync(
            new MediaItem(".jpg", "image/jpeg", image.Length),
            new ByteArrayMediaSource(image),
            progress,
            CancellationToken.None);

        Assert.Contains(fractions, fraction => fraction >= 0.99);
        Assert.All(fractions, fraction => Assert.InRange(fraction, 0, 1));
    }

    [Fact]
    public void Options_RejectValuesThatCannotBeHonoured()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImagePluginOptions(maxThumbnailDimension: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImagePluginOptions(jpegQuality: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImagePluginOptions(jpegQuality: 101));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImagePluginOptions(maxSourceBytes: 0));
    }

    private static async Task<MediaPreviewArtifact> RenderAsync(
        byte[] image,
        string extension,
        string contentType,
        int maxDimension = 512)
    {
        var plugin = new ImagePreviewPlugin(new ImagePluginOptions(maxThumbnailDimension: maxDimension));
        var source = new ByteArrayMediaSource(image);

        var result = await plugin.GeneratePreviewAsync(
            new MediaItem(extension, contentType, image.Length), source, null, CancellationToken.None);

        return Assert.Single(result.Artifacts);
    }

    private static SKColor Pixel(ReadOnlyMemory<byte> image, int x, int y)
    {
        using var bitmap = SKBitmap.Decode(image.Span)
                          ?? throw new InvalidOperationException("The thumbnail could not be decoded by the test.");
        return bitmap.GetPixel(x, y);
    }

    private static bool IsDark(SKColor color) => Luminance(color) < 96;

    private static bool IsLight(SKColor color) => Luminance(color) > 160;

    private static int Luminance(SKColor color) => (color.Red + color.Green + color.Blue) / 3;

    private sealed class InlineProgress(Action<MediaProgress> report) : IProgress<MediaProgress>
    {
        public void Report(MediaProgress value) => report(value);
    }
}
