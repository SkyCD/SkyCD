using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SkyCD.App.Converters;

/// <summary>
/// Converts an icon glyph key (e.g. "folder", "cd") to a legacy Bitmap image.
/// This replaces the previous emoji-based icon approach with proper legacy-style icons.
/// </summary>
public sealed class IconGlyphConverter : IValueConverter
{
    private static readonly Dictionary<string, string> IconMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Tree node icons
        ["library"] = "avares://SkyCD.App/Assets/legacy/icon-cd.png",
        ["movies"] = "avares://SkyCD.App/Assets/legacy/icon-folder.png",
        ["music"] = "avares://SkyCD.App/Assets/legacy/icon-folder.png",
        ["projects"] = "avares://SkyCD.App/Assets/legacy/icon-folder.png",

        // Browser item icons
        ["file"] = "avares://SkyCD.App/Assets/add-from-media.png",
        ["folder"] = "avares://SkyCD.App/Assets/legacy/icon-folder.png",
        ["network-folder"] = "avares://SkyCD.App/Assets/network-folder.ico",
        ["video"] = "avares://SkyCD.App/Assets/legacy/icon-cd.png",
        ["audio"] = "avares://SkyCD.App/Assets/legacy/icon-cd.png",
        ["cd"] = "avares://SkyCD.App/Assets/legacy/icon-cd.png",
        ["network"] = "avares://SkyCD.App/Assets/add-from-internet.png",

        // Toolbar icons
        ["toolbar-new"] = "avares://SkyCD.App/Assets/add-from-media.png",
        ["toolbar-open"] = "avares://SkyCD.App/Assets/add-from-folder.png",
        ["toolbar-save"] = "avares://SkyCD.App/Assets/add-from-internet.png",

        // Status icons
        ["check"] = "avares://SkyCD.App/Assets/legacy/icon-cd.png",
        ["check-check"] = "avares://SkyCD.App/Assets/legacy/icon-cd.png",
        ["clock"] = "avares://SkyCD.App/Assets/legacy/icon-cd.png",
        ["star"] = "avares://SkyCD.App/Assets/legacy/icon-cd.png",
        ["list-todo"] = "avares://SkyCD.App/Assets/legacy/icon-folder.png",
        ["pause"] = "avares://SkyCD.App/Assets/legacy/icon-folder.png",
        ["x-circle"] = "avares://SkyCD.App/Assets/legacy/icon-folder.png",
        ["calendar-plus"] = "avares://SkyCD.App/Assets/legacy/icon-cd.png",
        ["rotate-ccw"] = "avares://SkyCD.App/Assets/legacy/icon-cd.png",
        ["package-check"] = "avares://SkyCD.App/Assets/legacy/icon-folder.png",
        ["handshake"] = "avares://SkyCD.App/Assets/legacy/icon-folder.png",
        ["search"] = "avares://SkyCD.App/Assets/legacy/icon-folder.png",
        ["warning"] = "avares://SkyCD.App/Assets/legacy/icon-folder.png",
        ["archive"] = "avares://SkyCD.App/Assets/legacy/icon-folder.png",
        ["circle"] = "avares://SkyCD.App/Assets/legacy/icon-folder.png",
    };

    private static readonly Dictionary<string, Bitmap?> Cache = [];

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrWhiteSpace(key))
            return null;

        if (Cache.TryGetValue(key, out var cached))
            return cached;

        var uri = IconMap.GetValueOrDefault(key);
        if (uri is null)
        {
            if (File.Exists(key))
            {
                var bitmap = new Bitmap(key);
                Cache[key] = bitmap;
                return bitmap;
            }

            // Try as direct URI
            if (key.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
                uri = key;
            else
                return null;
        }

        try
        {
            var bitmap = LoadBitmap(uri);
            Cache[key] = bitmap;
            return bitmap;
        }
        catch
        {
            Cache[key] = null;
            return null;
        }
    }

    private static Bitmap LoadBitmap(string uri)
    {
        var uriObj = new Uri(uri);
        using var stream = AssetLoader.Open(uriObj);
        return new Bitmap(stream);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
