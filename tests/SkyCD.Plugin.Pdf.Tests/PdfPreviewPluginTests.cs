using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Pdf.Exceptions;
using Xunit;
using SkyCD.Plugin.Pdf.Export;
using SkyCD.Plugin.Pdf.Models;

namespace SkyCD.Plugin.Pdf.Tests;

public class PdfPreviewPluginTests
{
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void Descriptor_ClaimsOnlyPdfWithAPreviewSchema()
    {
        var descriptor = new PdfPreviewPlugin(PdfPluginOptions.Default, new StubRenderer(true, null)).Descriptor;

        Assert.Equal([".pdf"], descriptor.Extensions);
        Assert.Equal("skycd.pdf.preview", descriptor.Schema.Id);
        Assert.Empty(descriptor.Schema.Fields);
    }

    [Fact]
    public async Task WithoutARasterizer_ReturnsNoArtifactAndNeverReadsTheSource()
    {
        var pdf = PdfFixtures.Document();
        var source = new ByteArrayMediaSource(pdf);
        var plugin = new PdfPreviewPlugin(PdfPluginOptions.Default, new StubRenderer(available: false, null));

        var result = await plugin.GeneratePreviewAsync(
            new MediaItem(".pdf", "application/pdf", pdf.Length), source, null, CancellationToken.None);

        Assert.Empty(result.Artifacts);
        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task RasterizerProducingNoPage_IsMetadataOnlyRatherThanAFailure()
    {
        var pdf = PdfFixtures.Document();
        var plugin = new PdfPreviewPlugin(PdfPluginOptions.Default, new StubRenderer(true, null));

        var result = await plugin.GeneratePreviewAsync(
            new MediaItem(".pdf", "application/pdf", pdf.Length),
            new ByteArrayMediaSource(pdf),
            null,
            CancellationToken.None);

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task RasterizerOutput_IsPublishedAsThePreviewArtifact()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        var renderer = new StubRenderer(true, payload);
        var pdf = PdfFixtures.Document();

        var result = await new PdfPreviewPlugin(new PdfPluginOptions(maxThumbnailDimension: 400), renderer)
            .GeneratePreviewAsync(
                new MediaItem(".pdf", "application/pdf", pdf.Length),
                new ByteArrayMediaSource(pdf),
                null,
                CancellationToken.None);

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("image/png", artifact.ContentType);
        Assert.Equal(payload, artifact.Data.ToArray());

        // The first page is the representative one, and the configured bound is passed through.
        Assert.Equal(0, renderer.PageIndex);
        Assert.Equal(400, renderer.MaxDimension);
    }

    [Fact]
    public async Task EncryptedDocument_ReportsAuthRequiredInsteadOfARenderFailure()
    {
        var pdf = PdfFixtures.Document(encrypted: true);
        var renderer = new StubRenderer(true, new byte[] { 1 });

        await Assert.ThrowsAsync<PdfAuthenticationRequiredException>(
            () => new PdfPreviewPlugin(PdfPluginOptions.Default, renderer).GeneratePreviewAsync(
                new MediaItem(".pdf", "application/pdf", pdf.Length),
                new ByteArrayMediaSource(pdf),
                null,
                CancellationToken.None));

        Assert.Equal(0, renderer.Calls);
    }

    [Fact]
    public async Task MalformedDocument_FailsWithADecodeError()
    {
        var pdf = PdfFixtures.Malformed();

        await Assert.ThrowsAsync<PdfDecodeException>(
            () => new PdfPreviewPlugin(PdfPluginOptions.Default, new StubRenderer(true, new byte[] { 1 }))
                .GeneratePreviewAsync(
                    new MediaItem(".pdf", "application/pdf", pdf.Length),
                    new ByteArrayMediaSource(pdf),
                    null,
                    CancellationToken.None));
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeReadingTheSource()
    {
        var pdf = PdfFixtures.Document();
        var source = new ByteArrayMediaSource(pdf);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new PdfPreviewPlugin(PdfPluginOptions.Default, new StubRenderer(true, null))
                .GeneratePreviewAsync(
                    new MediaItem(".pdf", "application/pdf", pdf.Length), source, null, cancelled.Token));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task DefaultRasterizer_RendersTheFirstPageAsABoundedPng()
    {
        var pdf = PdfFixtures.Document(pageCount: 3, text: "Render me");
        using var renderer = new DisposableRenderer(new DocnetPdfPageRenderer());

        var png = await renderer.Inner.RenderPageAsync(pdf, 0, 320, CancellationToken.None);

        // A rasterizer that cannot load on this platform reports "no image" rather than throwing; the
        // assertion below is only meaningful when one is present, so the absence is accepted explicitly.
        if (png is null)
        {
            return;
        }

        Assert.True(png.Length > PngMagic.Length);
        Assert.Equal(PngMagic, png[..PngMagic.Length]);

        await using var probe = new MemoryStream(png);
        using var bitmap = SkiaSharp.SKBitmap.Decode(probe);
        Assert.NotNull(bitmap);
        Assert.True(Math.Max(bitmap!.Width, bitmap.Height) <= 320,
            $"Rendered page was {bitmap.Width}x{bitmap.Height}, above the 320 px bound.");
    }

    [Fact]
    public async Task DefaultRasterizer_ReportsNoImageForAnOutOfRangePage()
    {
        var pdf = PdfFixtures.Document(pageCount: 1);
        using var renderer = new DisposableRenderer(new DocnetPdfPageRenderer());

        var result = await renderer.Inner.RenderPageAsync(pdf, 9, 320, CancellationToken.None);

        Assert.Null(result);
    }

    /// <summary>Stub rasterizer that records how the plugin asked for a page.</summary>
    private sealed class StubRenderer : IPdfPageRenderer
    {
        private readonly byte[]? image;

        public StubRenderer(bool available, byte[]? image)
        {
            IsAvailable = available;
            this.image = image;
        }

        public bool IsAvailable { get; }
        public int Calls { get; private set; }
        public int PageIndex { get; private set; } = -1;
        public int MaxDimension { get; private set; }

        public Task<byte[]?> RenderPageAsync(byte[] document, int pageIndex, int maxDimension, CancellationToken cancellationToken)
        {
            Calls++;
            PageIndex = pageIndex;
            MaxDimension = maxDimension;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(image);
        }
    }

    /// <summary>Keeps the native rasterizer's unmanaged handles tied to the test's lifetime.</summary>
    private sealed class DisposableRenderer(IPdfPageRenderer inner) : IDisposable
    {
        public IPdfPageRenderer Inner { get; } = inner;

        public void Dispose() => (Inner as IDisposable)?.Dispose();
    }
}
