using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;

namespace SkyCD.App.Icons;

/// <summary>
/// Resolves a <c>ToolbarCommandDescriptor.IconPath</c> to the image a toolbar button or an Options list row
/// shows (issue #933). A key of the form <c>ToolbarIcon.*</c> is colored Twemoji artwork (issue #1022); a key
/// no longer covered falls back to the project-owned glyph dictionary
/// <c>avares://SkyCD.App/Styles/ToolbarIcons.axaml</c>, and any other value is an application asset path such
/// as <c>/Assets/new.png</c> and is loaded as a bitmap.
/// </summary>
/// <remarks>
/// An icon is a <see cref="DrawingImage"/>, so it rerenders at any scaling instead of being resampled. The
/// Twemoji artwork paints with the colors of the emoji itself; only a fallback glyph follows the theme
/// foreground brush, which keeps it legible on the dark and light themes.
/// </remarks>
public static class ToolbarIconResolver
{
    /// <summary>The resource path of the project-owned toolbar glyph dictionary.</summary>
    public const string GlyphDictionaryPath = "avares://SkyCD.App/Styles/ToolbarIcons.axaml";

    /// <summary>Every toolbar glyph key starts this way, which distinguishes it from an asset path.</summary>
    public const string GlyphKeyPrefix = "ToolbarIcon.";

    private const string ForegroundResource = "ThemeForegroundBrush";

    private static readonly Uri GlyphDictionaryUri = new(GlyphDictionaryPath);

    /// <summary>Glyph images by theme variant; a theme switch resolves the other variant on demand.</summary>
    private static readonly ConcurrentDictionary<ThemeVariant, Dictionary<string, IImage>> GlyphsByVariant = new();

    /// <summary>Bitmap assets by application asset path.</summary>
    private static readonly ConcurrentDictionary<string, IImage> Assets = new(StringComparer.Ordinal);

    /// <summary>
    /// Seam for tests: return the colored image for a key, or null to take the monochrome glyph branch.
    /// </summary>
    internal static Func<string, IImage?>? ArtworkLoaderOverride { get; set; }

    /// <summary>
    /// Seam for tests: return the fallback glyph for a key, or null when the reference is not a glyph at all
    /// (a plain asset path), so the test exercises the real asset branch.
    /// </summary>
    internal static Func<string, IImage?>? GlyphLoaderOverride { get; set; }

    /// <summary>Seam so tests exercise the real resolver without opening the packed bitmap assets.</summary>
    internal static Func<string, IImage?>? AssetLoaderOverride { get; set; }

    /// <summary>
    /// Returns the icon for a descriptor icon reference, or null when the reference is empty — a separator
    /// or a deliberately text-only button.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The reference names neither a glyph nor a packaged asset; a broken icon fails loudly instead of
    /// silently degrading the toolbar to text only.
    /// </exception>
    public static IImage? Resolve(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            return null;
        }

        var reference = iconPath.Trim();
        if (reference.StartsWith(GlyphKeyPrefix, StringComparison.Ordinal))
        {
            // Colored Twemoji artwork first; a key it does not cover keeps its monochrome glyph.
            return (ArtworkLoaderOverride is { } artworkLoader ? artworkLoader(reference) : TwemojiToolbarIcons.Load(reference))
                ?? ResolveFallbackGlyph(reference)
                ?? throw MissingGlyph(reference);
        }

        if (Assets.TryGetValue(reference, out var cached))
        {
            return cached;
        }

        if (!reference.StartsWith('/'))
        {
            throw MissingGlyph(reference);
        }

        var image = AssetLoaderOverride is { } assetLoader
            ? assetLoader(reference)
            : new Avalonia.Media.Imaging.Bitmap(AssetLoader.Open(new Uri($"avares://SkyCD.App{reference}")));
        if (image is null)
        {
            throw new InvalidOperationException($"Toolbar icon asset '{reference}' could not be loaded.");
        }

        Assets[reference] = image;
        return image;
    }

    private static IImage? ResolveFallbackGlyph(string reference) =>
        GlyphLoaderOverride is { } glyphLoader
            ? glyphLoader(reference)
            : ResolveGlyph(reference);

    private static IImage? ResolveGlyph(string reference)
    {
        if (Application.Current is not { } application)
        {
            return null;
        }

        var variant = application.ActualThemeVariant;
        if (!GlyphsByVariant.TryGetValue(variant, out var glyphs))
        {
            glyphs = LoadGlyphs(application, variant);
            GlyphsByVariant[variant] = glyphs;
        }

        return glyphs.TryGetValue(reference, out var glyph) ? glyph : null;
    }

    private static Dictionary<string, IImage> LoadGlyphs(Application application, ThemeVariant variant)
    {
        ResourceInclude include;
        try
        {
            include = new ResourceInclude(GlyphDictionaryUri) { Source = GlyphDictionaryUri };
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"The toolbar glyph dictionary {GlyphDictionaryPath} could not be loaded.", ex);
        }

        var dictionary = include.Loaded
            ?? throw new InvalidOperationException($"The toolbar glyph dictionary {GlyphDictionaryPath} produced no resources.");
        var brush = application.TryGetResource(ForegroundResource, variant, out var resource) && resource is IBrush themeBrush
            ? themeBrush
            : variant == ThemeVariant.Light ? Brushes.Black : Brushes.White;
        var glyphs = new Dictionary<string, IImage>(StringComparer.Ordinal);
        foreach (var keyObject in dictionary.Keys)
        {
            if (keyObject is not string key || string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            // The dictionary materializes each value on access, so read it through the indexer.
            var geometry = dictionary[key] switch
            {
                Geometry parsed => parsed,
                string data => StreamGeometry.Parse(data),
                null => null,
                var other => throw new InvalidOperationException(
                    $"Toolbar glyph '{key}' in {GlyphDictionaryPath} must be a {nameof(Geometry)} or path data, but is {other.GetType().Name}.")
            };
            if (geometry is null)
            {
                throw new InvalidOperationException($"Toolbar glyph '{key}' in {GlyphDictionaryPath} is empty.");
            }

            var drawing = new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing { Geometry = geometry, Brush = brush });
            glyphs[key] = new DrawingImage { Drawing = drawing };
        }

        return glyphs;
    }

    private static InvalidOperationException MissingGlyph(string reference) =>
        new($"Toolbar icon '{reference}' is neither colored Twemoji artwork, a glyph of {GlyphDictionaryPath} nor an application asset path.");
}
