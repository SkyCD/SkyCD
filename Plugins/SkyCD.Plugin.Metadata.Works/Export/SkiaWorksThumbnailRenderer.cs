using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

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
    private const float PageAspect = 1.4142f;
    private const string FontFamily = "sans-serif";

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

        using var face = SKTypeface.FromFamilyName(FontFamily, SKFontStyle.Normal);
        if (face is null)
        {
            return null;
        }

        var fontSize = Math.Max(8f, width / 40f);
        var lineHeight = fontSize * 1.35f;
        var margin = Math.Max(8f, width / 12f);

        using var font = new SKFont(face, fontSize);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        var left = margin;
        var right = width - margin;
        var x = left;
        var baseline = margin + fontSize;
        var drawn = 0;
        var finished = false;

        void NewLine()
        {
            x = left;
            baseline += lineHeight;
            if (baseline > height - margin)
            {
                finished = true;
            }
        }

        void DrawToken(string token)
        {
            if (finished)
            {
                return;
            }

            var tokenWidth = font.MeasureText(token);
            if (x > left && x + tokenWidth > right)
            {
                NewLine();
            }

            if (finished)
            {
                return;
            }

            if (!IsWhitespace(token))
            {
                canvas.DrawText(token, x, baseline, font, paint);
            }

            x += tokenWidth;
            drawn += token.Length;
            if (drawn >= maxCharacters)
            {
                finished = true;
            }
        }

        foreach (var paragraph in paragraphs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (finished)
            {
                break;
            }

            foreach (var token in Tokenize(paragraph))
            {
                DrawToken(token);
                if (finished)
                {
                    break;
                }
            }

            if (!finished)
            {
                NewLine();
            }
        }

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
        foreach (var value in token)
        {
            if (!char.IsWhiteSpace(value))
            {
                return false;
            }
        }

        return token.Length > 0;
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var index = 0;
        while (index < text.Length)
        {
            var start = index;
            var whitespace = char.IsWhiteSpace(text[index]);
            while (index < text.Length && char.IsWhiteSpace(text[index]) == whitespace)
            {
                index++;
            }

            tokens.Add(text[start..index]);
        }

        return tokens;
    }
}
