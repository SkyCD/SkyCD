using System;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using SkyCD.Formatting.Rtf;
using SkyCD.Plugin.Preview.Export;

namespace SkyCD.Plugin.Metadata.Rtf.Export;

/// <summary>
/// Lays out the parsed document as a page and encodes it to PNG with SkiaSharp.
/// </summary>
/// <remarks>
/// Every rasterizer failure degrades to "no preview": a missing native library, an unavailable font or a
/// surface that cannot be allocated all return null, because a document's metadata is still worth
/// cataloguing when its thumbnail cannot be drawn.
/// </remarks>
public sealed class SkiaRtfThumbnailRenderer : IRtfThumbnailRenderer
{
    public bool IsAvailable => true;

    public Task<byte[]?> RenderAsync(
        RichTextDocument document,
        int maxDimension,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDimension);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCharacters);

        try
        {
            return Task.FromResult(RenderCore(document, maxDimension, maxCharacters, cancellationToken));
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
        RichTextDocument document,
        int maxDimension,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        using var page = SkiaPageRenderer.TryCreate(maxDimension);
        if (page is null)
        {
            return null;
        }

        var regularFont = page.GetFont(SKFontStyle.Normal);
        var boldFont = page.GetFont(SKFontStyle.Bold);
        var italicFont = page.GetFont(SKFontStyle.Italic);
        var boldItalicFont = page.GetFont(SKFontStyle.BoldItalic);

        var finished = false;

        SKFont SelectFont(RichTextSpan span) => span switch
        {
            { Bold: true, Italic: true } => boldItalicFont,
            { Bold: true } => boldFont,
            { Italic: true } => italicFont,
            _ => regularFont
        };

        bool DrawToken(string token, SKFont font, RichTextSpan? style)
        {
            var drawn = page.TryDrawToken(
                font,
                token,
                maxCharacters: maxCharacters,
                underline: style?.Underline == true,
                strikeout: style?.Strikeout == true,
                countWhitespace: true);
            finished = !drawn;
            return drawn;
        }
        foreach (var paragraph in document.Paragraphs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (finished)
            {
                break;
            }

            if (paragraph.IsBullet && !DrawToken("• ", regularFont, null))
            {
                break;
            }

            foreach (var span in paragraph.Spans)
            {
                var font = SelectFont(span);
                var spanText = span.Text.Replace("\t", "    ", StringComparison.Ordinal);

                foreach (var token in SkiaPageText.TokenizePreservingNewlines(spanText))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (token == "\n")
                    {
                        page.AdvanceLine();
                        finished = page.IsFinished;
                        if (finished)
                        {
                            break;
                        }

                        continue;
                    }

                    if (!DrawToken(token, font, span))
                    {
                        break;
                    }
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
