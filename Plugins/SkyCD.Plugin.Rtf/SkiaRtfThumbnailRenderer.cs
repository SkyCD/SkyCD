using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using SkyCD.Formatting.Rtf;

namespace SkyCD.Plugin.Rtf;

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
    private const float PageAspect = 1.4142f;
    private const string FontFamily = "sans-serif";

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

        var faces = new List<SKTypeface>();
        try
        {
            var regularFace = SKTypeface.FromFamilyName(FontFamily, SKFontStyle.Normal);
            if (regularFace is null)
            {
                return null;
            }

            faces.Add(regularFace);
            var boldFace = CreateFace(faces, SKFontStyle.Bold) ?? regularFace;
            var italicFace = CreateFace(faces, SKFontStyle.Italic) ?? regularFace;
            var boldItalicFace = CreateFace(faces, SKFontStyle.BoldItalic) ?? regularFace;

            var fontSize = Math.Max(8f, width / 40f);
            var lineHeight = fontSize * 1.35f;
            var margin = Math.Max(8f, width / 12f);

            using var regularFont = new SKFont(regularFace, fontSize);
            using var boldFont = new SKFont(boldFace, fontSize);
            using var italicFont = new SKFont(italicFace, fontSize);
            using var boldItalicFont = new SKFont(boldItalicFace, fontSize);
            using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
            using var linePaint = new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1f, fontSize / 18f),
                IsAntialias = true
            };

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

            bool DrawToken(string token, SKFont font, RichTextSpan? style)
            {
                if (finished)
                {
                    return false;
                }

                var tokenWidth = font.MeasureText(token);
                if (x > left && x + tokenWidth > right)
                {
                    NewLine();
                }

                if (finished)
                {
                    return false;
                }

                if (!IsWhitespace(token))
                {
                    canvas.DrawText(token, x, baseline, font, paint);

                    if (style?.Underline == true)
                    {
                        canvas.DrawLine(x, baseline + fontSize * 0.15f, x + tokenWidth,
                            baseline + fontSize * 0.15f, linePaint);
                    }

                    if (style?.Strikeout == true)
                    {
                        canvas.DrawLine(x, baseline - fontSize * 0.3f, x + tokenWidth,
                            baseline - fontSize * 0.3f, linePaint);
                    }
                }

                x += tokenWidth;
                drawn += token.Length;
                return drawn < maxCharacters;
            }

            SKFont SelectFont(RichTextSpan span) => span switch
            {
                { Bold: true, Italic: true } => boldItalicFont,
                { Bold: true } => boldFont,
                { Italic: true } => italicFont,
                _ => regularFont
            };

            foreach (var paragraph in document.Paragraphs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (finished)
                {
                    break;
                }

                if (paragraph.IsBullet && !DrawToken("• ", regularFont, null))
                {
                    finished = true;
                    break;
                }

                foreach (var span in paragraph.Spans)
                {
                    var font = SelectFont(span);
                    var spanText = span.Text.Replace("\t", "    ", StringComparison.Ordinal);

                    foreach (var token in Tokenize(spanText))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (token == "\n")
                        {
                            NewLine();
                            if (finished)
                            {
                                break;
                            }

                            continue;
                        }

                        if (!DrawToken(token, font, span))
                        {
                            finished = true;
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
        finally
        {
            foreach (var face in faces)
            {
                face.Dispose();
            }
        }
    }

    private static SKTypeface? CreateFace(List<SKTypeface> faces, SKFontStyle style)
    {
        var face = SKTypeface.FromFamilyName(FontFamily, style);
        if (face is not null)
        {
            faces.Add(face);
        }

        return face;
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
