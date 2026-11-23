using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;

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
    private const float PageAspect = 1.4142f;
    private const string FontFamily = "sans-serif";

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

        foreach (var token in Tokenize(text))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (token == "\n")
            {
                x = left;
                baseline += lineHeight;
                if (baseline > height - margin)
                {
                    break;
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
                    break;
                }
            }

            if (!IsWhitespace(token))
            {
                canvas.DrawText(token, x, baseline, font, paint);
                drawn += token.Length;
                if (drawn >= maxCharacters)
                {
                    break;
                }
            }

            x += tokenWidth;
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
        foreach (var ch in token)
        {
            if (!char.IsWhiteSpace(ch))
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
            if (text[index] == '\n')
            {
                tokens.Add("\n");
                index++;
                continue;
            }

            var start = index;
            var whitespace = char.IsWhiteSpace(text[index]);
            while (index < text.Length && text[index] != '\n' &&
                   char.IsWhiteSpace(text[index]) == whitespace)
            {
                index++;
            }

            tokens.Add(text[start..index]);
        }

        return tokens;
    }
}
