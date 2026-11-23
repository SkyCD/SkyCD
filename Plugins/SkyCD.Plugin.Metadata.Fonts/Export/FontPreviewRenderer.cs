using System;
using System.Collections.Generic;
using System.Threading;
using SkiaSharp;
using SkyCD.Plugin.Metadata.Fonts.Exceptions;
using SkyCD.Plugin.Metadata.Fonts.Models;
using SkyCD.Plugin.Preview.Export;

namespace SkyCD.Plugin.Metadata.Fonts.Export;

/// <summary>
/// Renders the configured sample text with the font's own glyphs into a bounded PNG. The canvas is sized to
/// the measured text and clamped in both dimensions, so a long line or a huge configured size cannot request
/// an excessive bitmap.
/// </summary>
internal static class FontPreviewRenderer
{
    public const string ContentType = "image/png";

    private const int Padding = 12;
    private const float LineSpacing = 1.35f;
    private const int MaxWidth = 1600;
    private const int MaxHeight = 1200;

    public static byte[] Render(byte[] fontData, string text, int size, FontPluginOptions options,
        CancellationToken cancellationToken, Func<byte[], SKTypeface?>? createTypeface = null)
    {
        ArgumentNullException.ThrowIfNull(fontData);
        ArgumentNullException.ThrowIfNull(options);

        using var typeface = (createTypeface ?? DecodeTypeface)(fontData);
        if (typeface is null)
        {
            throw new FontPreviewException("the glyphs could not be decoded");
        }

        IReadOnlyList<string> lines = SplitLines(text, options.MaxPreviewLines);
        using var font = new SKFont(typeface, Math.Max(1, size));
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };

        var textWidth = 0f;
        foreach (var line in lines)
        {
            textWidth = Math.Max(textWidth, font.MeasureText(line));
        }

        var lineHeight = font.Size * LineSpacing;
        var width = Math.Clamp((int)MathF.Ceiling(textWidth) + Padding * 2, 64, MaxWidth);
        var height = Math.Clamp((int)MathF.Ceiling(lineHeight * lines.Count) + Padding * 2, 48, MaxHeight);
        if ((long)width * height > options.MaxPreviewPixels)
        {
            throw new FontPreviewException("the preview exceeds the pixel limit");
        }

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        if (surface is null)
        {
            throw new FontPreviewException("the drawing surface could not be created");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        using var border = new SKPaint { Color = new SKColor(0xC0, 0xC0, 0xC0), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        canvas.DrawRect(0.5f, 0.5f, width - 1, height - 1, border);

        var baseline = Padding + font.Size;
        foreach (var line in lines)
        {
            canvas.DrawText(line, Padding, baseline, font, paint);
            baseline += lineHeight;
        }

        using var image = surface.Snapshot();
        var encoded = PreviewArtifactEncoder.EncodePng(image);
        if (encoded is null || encoded.Length == 0)
        {
            throw new FontPreviewException("the image could not be encoded");
        }

        return encoded;
    }

    private static SKTypeface? DecodeTypeface(byte[] fontData) =>
        SKTypeface.FromData(SKData.CreateCopy(fontData));

    private static IReadOnlyList<string> SplitLines(string? text, int maxLines)
    {
        var source = string.IsNullOrWhiteSpace(text) ? FontSettings.DefaultPreviewText : text;
        var lines = new List<string>(maxLines);
        foreach (var line in source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            if (lines.Count == maxLines)
            {
                break;
            }

            lines.Add(line);
        }

        return lines.Count > 0 ? lines : [FontSettings.DefaultPreviewText];
    }
}
