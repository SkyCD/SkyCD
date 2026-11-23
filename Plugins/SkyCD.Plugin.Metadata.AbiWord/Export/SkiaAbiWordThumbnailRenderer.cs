using System;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using SkyCD.Plugin.Preview.Export;

namespace SkyCD.Plugin.Metadata.AbiWord.Export;

/// <summary>
/// Lays out the recovered text as a page and encodes it to PNG with SkiaSharp.
/// </summary>
/// <remarks>
/// Every rasterizer failure degrades to "no preview": a missing native library, an unavailable font or a
/// surface that cannot be allocated all return null, so a document is still worth cataloguing when its
/// thumbnail cannot be drawn.
/// </remarks>
public sealed class SkiaAbiWordThumbnailRenderer : IAbiWordThumbnailRenderer
{
    public bool IsAvailable => true;

    public Task<byte[]?> RenderAsync(
        string text,
        int maxDimension,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDimension);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCharacters);

        try
        {
            return Task.FromResult(RenderCore(text, maxDimension, maxCharacters, cancellationToken));
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
        string text,
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
        foreach (var token in SkiaPageText.TokenizePreservingNewlines(text))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (token == "\n")
            {
                page.AdvanceLine();
                if (page.IsFinished)
                {
                    break;
                }

                continue;
            }

            if (!page.TryDrawToken(font, token, maxCharacters: maxCharacters))
            {
                break;
            }
        }

        return page.EncodePng();
    }
}
