using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using SkyCD.Plugin.Metadata.Html.Models;
using SkyCD.Plugin.Preview.Export;

namespace SkyCD.Plugin.Metadata.Html.Export;

/// <summary>
/// Lays out the leading blocks as an A-series page and encodes it to PNG with SkiaSharp.
/// </summary>
/// <remarks>
/// Headings are drawn larger and bold so the page reads as a document rather than as a wall of text. Every
/// rasterizer failure degrades to "no page image": a missing native library, an unavailable font or a surface
/// that cannot be allocated all return null, and the metadata result is unaffected.
/// </remarks>
public sealed class SkiaHtmlThumbnailRenderer : IHtmlThumbnailRenderer
{
    private const float HeadingScale = 1.3f;
    private const float LineHeightScale = 1.35f;

    public bool IsAvailable => true;

    public Task<byte[]?> RenderAsync(
        IReadOnlyList<HtmlPreviewLine> lines,
        int maxDimension,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDimension);

        try
        {
            return Task.FromResult(RenderCore(lines, maxDimension, cancellationToken));
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
        IReadOnlyList<HtmlPreviewLine> lines,
        int maxDimension,
        CancellationToken cancellationToken)
    {
        using var page = SkiaPageRenderer.TryCreate(maxDimension, beginAtMargin: true);
        if (page is null)
        {
            return null;
        }

        var bodyFont = page.GetFont(SKFontStyle.Normal);
        var headingSize = page.FontSize * HeadingScale;
        var headingFont = page.GetFont(SKFontStyle.Bold, headingSize);
        var bodyLineHeight = page.FontSize * LineHeightScale;
        var headingLineHeight = headingSize * LineHeightScale;

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var font = line.IsHeading ? headingFont : bodyFont;
            var lineHeight = line.IsHeading ? headingLineHeight : bodyLineHeight;
            page.AdvanceLine(lineHeight);
            if (page.IsFinished)
            {
                break;
            }

            foreach (var token in Tokenize(line.Text))
            {
                if (token.Contains('\n'))
                {
                    page.AdvanceLine(lineHeight);
                    if (page.IsFinished)
                    {
                        return page.EncodePng();
                    }

                    continue;
                }

                if (!page.TryDrawToken(font, token, wrapLineHeight: lineHeight))
                {
                    return page.EncodePng();
                }
            }
        }

        return page.EncodePng();
    }

    /// <summary>Splits on spaces only; a preview line is already single-line condensed text.</summary>
    private static IEnumerable<string> Tokenize(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            var start = index;
            var whitespace = char.IsWhiteSpace(text[index]);
            while (index < text.Length && char.IsWhiteSpace(text[index]) == whitespace)
            {
                index++;
            }

            yield return text[start..index];
        }
    }
}
