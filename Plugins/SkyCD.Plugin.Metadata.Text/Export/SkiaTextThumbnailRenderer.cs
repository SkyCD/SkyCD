using System;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using SkyCD.Plugin.Preview.Export;

namespace SkyCD.Plugin.Metadata.Text.Export;

/// <summary>
/// Lays out the excerpt as a page and encodes it to PNG with SkiaSharp.
/// </summary>
/// <remarks>
/// Every rasterizer failure degrades to "no page image": a missing native library, an unavailable font or a
/// surface that cannot be allocated all return null, because the readable text artifact is still worth
/// publishing when the thumbnail cannot be drawn.
/// </remarks>
public sealed class SkiaTextThumbnailRenderer : ITextThumbnailRenderer
{
    public bool IsAvailable => true;

    public Task<byte[]?> RenderAsync(string text, int maxDimension, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDimension);

        try
        {
            return Task.FromResult(RenderCore(text, maxDimension, cancellationToken));
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

    private static byte[]? RenderCore(string text, int maxDimension, CancellationToken cancellationToken)
    {
        using var page = SkiaPageRenderer.TryCreate(maxDimension);
        if (page is null)
        {
            return null;
        }

        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
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

            if (!page.TryDrawToken(font, token))
            {
                break;
            }
        }

        return page.EncodePng();
    }
}
