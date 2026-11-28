using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using SkyCD.Plugin.Metadata.Audio.AudioCD;
using SkyCD.Plugin.Metadata.Audio.AudioCD.Sources;
using Xunit;
using SkyCD.Plugin.Metadata.Audio.AudioCD.Export;

namespace SkyCD.Plugin.Metadata.Audio.Tests.AudioCD;

public sealed class CoverArtTests
{
    [Theory]
    [InlineData(250, "front-250")]
    [InlineData(32, "front-250")]
    [InlineData(400, "front-500")]
    [InlineData(500, "front-500")]
    [InlineData(900, "front-1200")]
    public void SizeSelectsTheSmallestSufficientThumbnail(int size, string expected) =>
        Assert.Equal(expected, CoverArtArchiveClient.SelectVariant(size));

    [Fact]
    public async Task AvailableArtwork_ReturnsBytesAndContentType()
    {
        var png = AudioCdFixtures.CoverPng(10);
        var handler = new DelegateHttpMessageHandler(_ =>
            DelegateHttpMessageHandler.Bytes(png, "image/png"));

        var image = await CreateClient(handler).FetchAsync("release", 250, CancellationToken.None);

        Assert.NotNull(image);
        Assert.Equal(png, image.Data);
        Assert.Equal("image/png", image.ContentType);
        Assert.Contains("/release/front-250", handler.Requests[0].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task MissingArtwork_ReturnsNull()
    {
        var handler = new DelegateHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Null(await CreateClient(handler).FetchAsync("release", 250, CancellationToken.None));
    }

    [Fact]
    public void Renderer_DoesNotUpscale_SmallImages()
    {
        var png = AudioCdFixtures.CoverPng(120);

        var artifact = new AudioCdCoverRenderer().Render(png, "image/png", 250);

        Assert.NotNull(artifact);
        Assert.Equal(png, artifact.Data.ToArray());
        Assert.Equal("image/png", artifact.ContentType);
    }

    [Fact]
    public void Renderer_FitsLargeImages_ToTheLongestEdge()
    {
        var png = AudioCdFixtures.CoverPng(300);

        var artifact = new AudioCdCoverRenderer().Render(png, "image/jpeg", 100);

        Assert.NotNull(artifact);
        Assert.Equal("image/png", artifact.ContentType);
        using var decoded = SKBitmap.Decode(artifact.Data.ToArray());
        Assert.NotNull(decoded);
        Assert.Equal(100, Math.Max(decoded.Width, decoded.Height));
    }

    [Fact]
    public void Renderer_RejectsUndecodableData()
    {
        Assert.Null(new AudioCdCoverRenderer().Render([1, 2, 3, 4], "image/png", 250));
    }

    private static CoverArtArchiveClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler), NoThrottle.Instance, TimeSpan.FromSeconds(5));
}
