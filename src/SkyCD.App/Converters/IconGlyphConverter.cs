using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkyCD.App.Icons;

namespace SkyCD.App.Converters;

/// <summary>
/// Converts an icon glyph key (e.g. "folder", "ext:pdf", base64/file path) to a Bitmap image.
/// Supports captured custom icon metadata, platform shell icon extraction, and portable fallbacks.
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
        ["toolbar-new"] = "avares://SkyCD.App/Assets/new.png",
        ["toolbar-open"] = "avares://SkyCD.App/Assets/open.png",
        ["toolbar-save"] = "avares://SkyCD.App/Assets/save.png",

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

    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static IIconResolver? CustomResolver { get; set; }

    public static void ClearCache() => Cache.Clear();

    public static Bitmap? Resolve(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return GetDefaultFileIcon();
        }

        var normalizedKey = key.Trim();
        if (Cache.TryGetValue(normalizedKey, out var cached))
        {
            return cached;
        }

        var bitmap = ResolveUncached(normalizedKey);
        Cache[normalizedKey] = bitmap;
        return bitmap;
    }

    private static Bitmap? ResolveUncached(string key)
    {
        if (CustomResolver is not null)
        {
            try
            {
                var custom = CustomResolver.ResolveIcon(key);
                if (custom is not null)
                {
                    return custom;
                }
            }
            catch
            {
                // Fall back gracefully
            }
        }

        if (IconMap.TryGetValue(key, out var mapUri))
        {
            try
            {
                return LoadBitmap(mapUri);
            }
            catch
            {
                return GetDefaultFileIcon();
            }
        }

        if (key.StartsWith("ext:", StringComparison.OrdinalIgnoreCase))
        {
            var ext = key[4..].TrimStart('.');
            return ResolveExtensionIcon(ext);
        }

        if (key.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("base64:", StringComparison.OrdinalIgnoreCase))
        {
            return TryLoadFromBase64(key) ?? GetDefaultFileIcon();
        }

        if (key.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return LoadBitmap(key);
            }
            catch
            {
                return GetDefaultFileIcon();
            }
        }

        if (File.Exists(key))
        {
            try
            {
                if (OperatingSystem.IsWindows() &&
                    (key.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                     key.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                {
                    var shellIcon = WindowsShellIconProvider.Instance.ResolvePathIcon(key);
                    if (shellIcon is not null)
                    {
                        return shellIcon;
                    }
                }

                return new Bitmap(key);
            }
            catch
            {
                return ResolveExtensionIcon(Path.GetExtension(key));
            }
        }

        if (LooksLikeBase64(key))
        {
            var base64Bitmap = TryLoadFromBase64(key);
            if (base64Bitmap is not null)
            {
                return base64Bitmap;
            }
        }

        if (key.Contains('.') || key.Contains('\\') || key.Contains('/'))
        {
            var ext = Path.GetExtension(key);
            if (!string.IsNullOrWhiteSpace(ext))
            {
                return ResolveExtensionIcon(ext);
            }
        }

        return GetDefaultFileIcon();
    }

    public static Bitmap? ResolveExtensionIcon(string? extension)
    {
        var ext = (extension ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(ext))
        {
            return GetDefaultFileIcon();
        }

        if (CustomResolver is not null)
        {
            try
            {
                var custom = CustomResolver.ResolveIcon("ext:" + ext);
                if (custom is not null)
                {
                    return custom;
                }
            }
            catch
            {
                // Fall back gracefully
            }
        }

        if (LinuxFreeDesktopIconProvider.Instance.ForceEnable || OperatingSystem.IsLinux())
        {
            try
            {
                var linuxIcon = LinuxFreeDesktopIconProvider.Instance.ResolveExtensionIcon(ext);
                if (linuxIcon is not null)
                {
                    return linuxIcon;
                }
            }
            catch
            {
                // Fall back gracefully
            }
        }
        else if (OperatingSystem.IsWindows())
        {
            try
            {
                var shellIcon = WindowsShellIconProvider.Instance.ResolveExtensionIcon(ext);
                if (shellIcon is not null)
                {
                    return shellIcon;
                }
            }
            catch
            {
                // Fall back gracefully
            }
        }

        return PortableExtensionIconProvider.Instance.ResolveExtensionIcon(ext) ?? GetDefaultFileIcon();
    }

    private static Bitmap? TryLoadFromBase64(string data)
    {
        try
        {
            var raw = data;
            var commaIndex = raw.IndexOf(',');
            if (commaIndex >= 0 && raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                raw = raw[(commaIndex + 1)..];
            }
            else if (raw.StartsWith("base64:", StringComparison.OrdinalIgnoreCase))
            {
                raw = raw[7..];
            }

            var bytes = System.Convert.FromBase64String(raw.Trim());
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikeBase64(string s)
    {
        if (s.Length < 32 || s.Length % 4 != 0)
        {
            return false;
        }

        foreach (var c in s)
        {
            if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '+' || c == '/' || c == '='))
            {
                return false;
            }
        }

        return true;
    }

    private static Bitmap? GetDefaultFileIcon()
    {
        if (Cache.TryGetValue("file", out var cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var bitmap = LoadBitmap("avares://SkyCD.App/Assets/add-from-media.png");
            Cache["file"] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap LoadBitmap(string uri)
    {
        var uriObj = new Uri(uri);
        using var stream = AssetLoader.Open(uriObj);
        return new Bitmap(stream);
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Resolve(value?.ToString());
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
