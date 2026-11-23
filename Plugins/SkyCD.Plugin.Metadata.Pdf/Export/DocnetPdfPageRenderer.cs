using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Docnet.Core;
using Docnet.Core.Models;
using SkiaSharp;
using SkyCD.Plugin.Preview.Export;

namespace SkyCD.Plugin.Metadata.Pdf.Export;

/// <summary>
/// Renders a page through PDFium (via Docnet) and encodes the raster to PNG.
/// </summary>
/// <remarks>
/// PDFium returns raw pixels, not an encoded image, so the raster is encoded here. Every failure mode of the
/// native rasterizer — a missing library, an unloadable dependency, a page it refuses to draw — is reported as
/// "no image" rather than an exception, because a document's metadata is still worth cataloguing when its
/// first page cannot be drawn.
/// </remarks>
public sealed class DocnetPdfPageRenderer : IPdfPageRenderer
{
    public bool IsAvailable => true;

    public Task<byte[]?> RenderPageAsync(byte[] document, int pageIndex, int maxDimension, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDimension);

        try
        {
            return Task.FromResult(RenderCore(document, pageIndex, maxDimension, cancellationToken));
        }
        catch (Exception exception) when (exception is DllNotFoundException
            or TypeInitializationException
            or EntryPointNotFoundException
            or AccessViolationException
            or Docnet.Core.Exceptions.DocnetException
            or InvalidOperationException)
        {
            // An unavailable or unwilling rasterizer is a missing preview, not a failed extraction.
            return Task.FromResult<byte[]?>(null);
        }
    }

    private static byte[]? RenderCore(byte[] document, int pageIndex, int maxDimension, CancellationToken cancellationToken)
    {
        using var reader = DocLib.Instance.GetDocReader(document, new PageDimensions(maxDimension, maxDimension));
        if (pageIndex >= reader.GetPageCount())
        {
            return null;
        }

        using var page = reader.GetPageReader(pageIndex);
        var width = page.GetPageWidth();
        var height = page.GetPageHeight();
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var pixels = page.GetImage();
        if (pixels is null || pixels.Length < (long)width * height * 4)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return EncodePng(pixels, width, height);
    }

    /// <summary>Copies PDFium's BGRA raster into a bitmap and encodes it losslessly.</summary>
    private static byte[]? EncodePng(byte[] pixels, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);

        var target = bitmap.GetPixels();
        if (target == IntPtr.Zero)
        {
            return null;
        }

        Marshal.Copy(pixels, 0, target, Math.Min(pixels.Length, width * height * 4));

        using var image = SKImage.FromBitmap(bitmap);
        return PreviewArtifactEncoder.EncodePng(image);
    }
}
