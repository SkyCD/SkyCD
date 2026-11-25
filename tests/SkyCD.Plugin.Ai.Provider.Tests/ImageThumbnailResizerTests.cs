using SkiaSharp;
using Xunit;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Ai.Provider.Exceptions;
using SkyCD.Plugin.Ai.Provider.Export;

namespace SkyCD.Plugin.Ai.Provider.Tests;

public sealed class ImageThumbnailResizerTests
{
    [Fact]
    public void ResizeIfNeeded_WhenImageExceedsBounds_DownscalesPreservingAspectRatio()
    {
        var original = TestImages.CreatePng(800, 400);

        var (contentType, data) = ImageThumbnailResizer.ResizeIfNeeded(original, "image/png");

        Assert.Equal("image/png", contentType);
        using var resized = SKBitmap.Decode(data);
        Assert.NotNull(resized);
        Assert.Equal(ImageThumbnailResizer.MaximumDimension, resized.Width);
        Assert.Equal(ImageThumbnailResizer.MaximumDimension / 2, resized.Height);
    }

    [Fact]
    public void ResizeIfNeeded_WhenImageFits_ReturnsOriginalBytes()
    {
        var original = TestImages.CreatePng(448, 300);

        var (contentType, data) = ImageThumbnailResizer.ResizeIfNeeded(original, "image/png");

        Assert.Equal("image/png", contentType);
        Assert.Same(original, data);
    }

    [Fact]
    public void ResizeIfNeeded_WhenNotAnImage_ReturnsOriginalBytes()
    {
        var original = new byte[] { 1, 2, 3 };

        var (contentType, data) = ImageThumbnailResizer.ResizeIfNeeded(original, "audio/mpeg");

        Assert.Equal("audio/mpeg", contentType);
        Assert.Same(original, data);
    }

    [Fact]
    public void ResizeIfNeeded_WhenBytesUndecodable_Throws()
    {
        var exception = Assert.Throws<AiProviderException>(() =>
            ImageThumbnailResizer.ResizeIfNeeded(new byte[] { 0xFF, 0xD8, 0xFF }, "image/jpeg"));

        Assert.Contains("could not be decoded", exception.Message);
    }
}
