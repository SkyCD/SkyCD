using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using IconifyBundle;
using SkyCD.App.Icons;

namespace SkyCD.App.Converters;

/// <summary>
/// Paints the Add To List source selector icons as colored Twemoji artwork through the same solid-fill
/// pipeline the toolbar icons and language flags use (issues #1022, #1099).
/// </summary>
public sealed class SourceIconImageConverter : IValueConverter
{
    // IconifyBundle materialises an asset for a strongly-typed member access it can see at compile time, so
    // every entry names a Twemoji member. The recorded name is the asset that member writes out, which lets a
    // test read the shipped SVG without the icon registry the application populates at startup.
    private static readonly IReadOnlyDictionary<string, (string Name, Func<Icon> Icon)> Sources =
        new Dictionary<string, (string Name, Func<Icon> Icon)>(StringComparer.Ordinal)
        {
            ["Media"] = ("optical-disk", () => Twemoji.OpticalDisk),
            ["Folder"] = ("file-folder", () => Twemoji.FileFolder),
            ["Internet"] = ("globe-showing-europe-africa", () => Twemoji.GlobeShowingEuropeAfrica),
            ["TitleOnly"] = ("letter-t", () => Twemoji.LetterT)
        };

    private static readonly ConcurrentDictionary<string, DrawingImage> Images = new(StringComparer.Ordinal);

    /// <summary>Every source selector button that paints colored artwork.</summary>
    internal static IEnumerable<string> Keys => Sources.Keys;

    /// <summary>The Twemoji asset a source selector key paints.</summary>
    internal static string IconNameFor(string source) => Sources[source].Name;

    /// <summary>The colored artwork for a source selector button; the argument is its source name.</summary>
    public static DrawingImage Resolve(object? source)
    {
        var key = source as string ?? string.Empty;
        if (!Sources.TryGetValue(key, out var entry))
        {
            throw new NotSupportedException($"Unknown Add To List source '{source}'.");
        }

        if (Images.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var image = TwemojiArtwork.Create(entry.Icon());
        Images[key] = image;
        return image;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Resolve(parameter);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
