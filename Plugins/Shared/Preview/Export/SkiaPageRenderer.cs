using System;
using System.Collections.Generic;
using SkiaSharp;

namespace SkyCD.Plugin.Preview.Export;

/// <summary>
/// Draws flowing text onto one white A-series page shared by every text preview plugin.
/// </summary>
/// <remarks>
/// The page owns its surface, typefaces and fonts, so a renderer only feeds it tokens and asks for the encoded
/// bytes. Metrics are derived from the page width alone: the margin, font size and line height are all fractions
/// of <c>maxDimension</c>, which keeps a thumbnail readable at every configured size. <see cref="TryCreate"/>
/// returns <c>null</c> when the page cannot be allocated instead of throwing, because a document is still worth
/// cataloguing when its thumbnail cannot be drawn.
/// </remarks>
internal sealed class SkiaPageRenderer : IDisposable
{
    private const float PageAspect = 1.4142f;
    private const string FontFamily = "sans-serif";

    private readonly SKSurface _surface;
    private readonly SKTypeface _regularFace;
    private readonly List<SKTypeface> _ownedFaces = new();
    private readonly SKPaint _paint = new() { Color = SKColors.Black, IsAntialias = true };
    private readonly Dictionary<(SKFontStyle Style, float Size), SKFont> _fonts = new();
    private readonly Dictionary<SKFontStyle, SKTypeface> _faces = new();
    private SKPaint? _linePaint;
    private float _x;
    private float _baseline;
    private bool _isFinished;

    private SkiaPageRenderer(SKSurface surface, SKTypeface regularFace, int width, int height, bool beginAtMargin)
    {
        _surface = surface;
        _regularFace = regularFace;
        Width = width;
        Height = height;
        Margin = Math.Max(8f, width / 12f);
        FontSize = Math.Max(8f, width / 40f);
        LineHeight = FontSize * 1.35f;
        _x = Margin;
        _baseline = beginAtMargin ? Margin : Margin + FontSize;
        _surface.Canvas.Clear(SKColors.White);
    }

    /// <summary>Allocates a blank page, or returns <c>null</c> when its size or font is unavailable.</summary>
    internal static SkiaPageRenderer? TryCreate(int maxDimension, bool beginAtMargin = false)
    {
        var width = maxDimension;
        var height = (int)Math.Round(width * PageAspect);
        if (height <= 0)
        {
            return null;
        }

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var surface = SKSurface.Create(info);
        if (surface is null)
        {
            return null;
        }

        var regularFace = SKTypeface.FromFamilyName(FontFamily, SKFontStyle.Normal);
        if (regularFace is null)
        {
            surface.Dispose();
            return null;
        }

        return new SkiaPageRenderer(surface, regularFace, width, height, beginAtMargin);
    }

    /// <summary>Page width in pixels; equals the requested maximum dimension.</summary>
    internal int Width { get; }

    /// <summary>Page height in pixels; the width scaled to an A4 aspect ratio.</summary>
    internal int Height { get; }

    /// <summary>Left and right gutter in pixels.</summary>
    internal float Margin { get; }

    /// <summary>Body font size in pixels.</summary>
    internal float FontSize { get; }

    /// <summary>Body line height in pixels.</summary>
    internal float LineHeight { get; }

    /// <summary>The canvas the tokens are drawn on.</summary>
    internal SKCanvas Canvas => _surface.Canvas;

    /// <summary>Number of non-whitespace characters drawn so far.</summary>
    internal int DrawnCharacters { get; private set; }

    /// <summary><c>true</c> once a line was advanced past the bottom margin.</summary>
    internal bool IsFinished => _isFinished;

    /// <summary>
    /// Returns the cached font for a style at <see cref="FontSize"/>; a missing face falls back to the regular one.
    /// </summary>
    internal SKFont GetFont(SKFontStyle style) => GetFont(style, FontSize);

    /// <summary>Returns the cached font for a style and size; a missing face falls back to the regular one.</summary>
    internal SKFont GetFont(SKFontStyle style, float size)
    {
        if (_fonts.TryGetValue((style, size), out var font))
        {
            return font;
        }

        font = new SKFont(GetFace(style), size);
        _fonts.Add((style, size), font);
        return font;
    }

    private SKTypeface GetFace(SKFontStyle style)
    {
        if (style == SKFontStyle.Normal)
        {
            return _regularFace;
        }

        if (_faces.TryGetValue(style, out var cached))
        {
            return cached;
        }

        var face = SKTypeface.FromFamilyName(FontFamily, style) ?? _regularFace;
        if (face != _regularFace)
        {
            _ownedFaces.Add(face);
        }

        _faces.Add(style, face);
        return face;
    }

    /// <summary>Moves to the start of the next line using <see cref="LineHeight"/>.</summary>
    internal void AdvanceLine() => AdvanceLine(LineHeight);

    /// <summary>Moves to the start of the next line; finishes the page when the baseline passes the bottom margin.</summary>
    internal void AdvanceLine(float lineHeight)
    {
        _x = Margin;
        _baseline += lineHeight;
        if (_baseline > Height - Margin)
        {
            _isFinished = true;
        }
    }

    /// <summary>
    /// Draws one token at the cursor, wrapping first when it would overflow the right margin.
    /// </summary>
    /// <returns><c>false</c> once the page is finished or <paramref name="maxCharacters"/> is reached.</returns>
    internal bool TryDrawToken(
        SKFont font,
        string token,
        float? wrapLineHeight = null,
        int maxCharacters = int.MaxValue,
        bool underline = false,
        bool strikeout = false,
        bool countWhitespace = false)
    {
        if (_isFinished)
        {
            return false;
        }

        var tokenWidth = font.MeasureText(token);
        if (_x > Margin && _x + tokenWidth > Width - Margin)
        {
            AdvanceLine(wrapLineHeight ?? LineHeight);
            if (_isFinished)
            {
                return false;
            }
        }

        var whitespace = SkiaPageText.IsWhitespace(token);
        if (!whitespace)
        {
            Canvas.DrawText(token, _x, _baseline, font, _paint);

            if (underline)
            {
                var y = _baseline + FontSize * 0.15f;
                Canvas.DrawLine(_x, y, _x + tokenWidth, y, GetLinePaint());
            }

            if (strikeout)
            {
                var y = _baseline - FontSize * 0.3f;
                Canvas.DrawLine(_x, y, _x + tokenWidth, y, GetLinePaint());
            }
        }

        if (!whitespace || countWhitespace)
        {
            DrawnCharacters += token.Length;
        }

        _x += tokenWidth;
        return DrawnCharacters < maxCharacters;
    }

    /// <summary>Encodes the page as PNG, or returns <c>null</c> when nothing was drawn.</summary>
    internal byte[]? EncodePng()
    {
        if (DrawnCharacters == 0)
        {
            return null;
        }

        using var image = _surface.Snapshot();
        return PreviewArtifactEncoder.EncodePng(image);
    }

    public void Dispose()
    {
        foreach (var font in _fonts.Values)
        {
            font.Dispose();
        }

        _fonts.Clear();
        _faces.Clear();
        foreach (var face in _ownedFaces)
        {
            face.Dispose();
        }

        _ownedFaces.Clear();
        _linePaint?.Dispose();
        _paint.Dispose();
        _regularFace.Dispose();
        _surface.Dispose();
    }

    private SKPaint GetLinePaint() => _linePaint ??= new SKPaint
    {
        Color = SKColors.Black,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = Math.Max(1f, FontSize / 18f),
        IsAntialias = true
    };
}
