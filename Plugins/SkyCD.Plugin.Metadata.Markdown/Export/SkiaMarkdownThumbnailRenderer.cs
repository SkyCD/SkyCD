using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using SkyCD.Plugin.Metadata.Markdown.Models;

namespace SkyCD.Plugin.Metadata.Markdown.Export;

/// <summary>
/// Lays out the leading blocks as an A-series page and encodes it to PNG with SkiaSharp.
/// </summary>
/// <remarks>
/// Headings are drawn larger and bold so the page reads as a document rather than as a wall of text. Every
/// rasterizer failure degrades to "no page image": a missing native library, an unavailable font or a surface
/// that cannot be allocated all return null, and the metadata result is unaffected.
/// </remarks>
public sealed class SkiaMarkdownThumbnailRenderer : IMarkdownThumbnailRenderer
{
    private const float PageAspect = 1.4142f;
    private const string FontFamily = "sans-serif";
    private const float HeadingScale = 1.3f;

    public bool IsAvailable => true;

    public Task<byte[]?> RenderAsync(
        IReadOnlyList<MarkdownPreviewLine> lines,
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
        IReadOnlyList<MarkdownPreviewLine> lines,
        int maxDimension,
        CancellationToken cancellationToken)
    {
        var width = maxDimension;
        var height = (int)Math.Round(width * PageAspect);
        if (height <= 0)
        {
            return null;
        }

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        if (surface is null)
        {
            return null;
        }

        using var bodyFace = SKTypeface.FromFamilyName(FontFamily, SKFontStyle.Normal);
        using var headingFace = SKTypeface.FromFamilyName(FontFamily, SKFontStyle.Bold);
        if (bodyFace is null || headingFace is null)
        {
            return null;
        }

        var bodySize = Math.Max(8f, width / 40f);
        var bodyLineHeight = bodySize * 1.35f;
        var headingSize = bodySize * HeadingScale;
        var headingLineHeight = headingSize * 1.35f;
        var margin = Math.Max(8f, width / 12f);

        using var bodyFont = new SKFont(bodyFace, bodySize);
        using var headingFont = new SKFont(headingFace, headingSize);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        var left = margin;
        var right = width - margin;
        var baseline = margin;
        var drawn = 0;

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var font = line.IsHeading ? headingFont : bodyFont;
            var lineHeight = line.IsHeading ? headingLineHeight : bodyLineHeight;
            baseline += lineHeight;
            if (baseline > height - margin)
            {
                break;
            }

            var x = left;
            foreach (var token in Tokenize(line.Text))
            {
                if (token.Contains('\n'))
                {
                    x = left;
                    baseline += lineHeight;
                    if (baseline > height - margin)
                    {
                        return Encode(surface, drawn);
                    }

                    continue;
                }

                var tokenWidth = font.MeasureText(token);
                if (x > left && x + tokenWidth > right)
                {
                    x = left;
                    baseline += lineHeight;
                    if (baseline > height - margin)
                    {
                        return Encode(surface, drawn);
                    }
                }

                if (!IsWhitespace(token))
                {
                    canvas.DrawText(token, x, baseline, font, paint);
                    drawn += token.Length;
                }

                x += tokenWidth;
            }
        }

        return Encode(surface, drawn);
    }

    private static byte[]? Encode(SKSurface surface, int drawn)
    {
        if (drawn == 0)
        {
            return null;
        }

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded?.ToArray();
    }

    private static bool IsWhitespace(string token)
    {
        foreach (var ch in token)
        {
            if (!char.IsWhiteSpace(ch))
            {
                return false;
            }
        }

        return token.Length > 0;
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
