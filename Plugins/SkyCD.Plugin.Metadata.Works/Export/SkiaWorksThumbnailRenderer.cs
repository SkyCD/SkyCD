using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using SkyCD.Plugin.Preview.Export;

namespace SkyCD.Plugin.Metadata.Works.Export;

/// <summary>
/// Lays out the recovered text sample as a page and encodes it to PNG with SkiaSharp.
/// </summary>
/// <remarks>
/// Every rasterizer failure degrades to "no preview": a missing native library, an unavailable font or a
/// surface that cannot be allocated all return null, because metadata is still worth cataloguing when the
/// thumbnail cannot be drawn.
/// </remarks>
public sealed class SkiaWorksThumbnailRenderer : IWorksThumbnailRenderer
{
    public bool IsAvailable => true;

    public Task<byte[]?> RenderAsync(
        IReadOnlyList<string> paragraphs,
        int maxDimension,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDimension);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCharacters);

        try
        {
            return Task.FromResult(RenderCore(paragraphs, maxDimension, maxCharacters, cancellationToken));
        }
        catch (Exception exception) when (exception is DllNotFoundException
            or TypeInitializationException
            or EntryPointNotFoundException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException)
        {
            return Task.FromResult<byte[]?>(null);
        }
    }

    private static byte[]? RenderCore(
        IReadOnlyList<string> paragraphs,
        int maxDimension,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        using var page = SkiaPageRenderer.TryCreate(maxDimension);
        if (page is null)
        {
            return null;
        }

        var font = page.GetFont(SKFontStyle.Normal);
        var finished = false;

        foreach (var paragraph in paragraphs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (finished)
            {
                break;
            }

            // The sample is a run of single-line paragraphs, so a paragraph break is a hard line break.
            foreach (var token in SkiaPageText.TokenizePreservingNewlines(paragraph))
            {
                if (token == "\n")
                {
                    page.AdvanceLine();
                    finished = page.IsFinished;
                }
                else
                {
                    finished = !page.TryDrawToken(font, token, maxCharacters: maxCharacters);
                }

                if (finished)
                {
                    break;
                }
            }

            if (!finished)
            {
                page.AdvanceLine();
                finished = page.IsFinished;
            }
        }

        return page.EncodePng();
    }
}
