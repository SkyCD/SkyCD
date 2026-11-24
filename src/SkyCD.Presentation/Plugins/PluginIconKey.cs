using System;
using SkyCD.Plugin.Runtime.Discovery;

namespace SkyCD.Presentation.Plugins;

/// <summary>
/// Turns a declared plugin icon and its capability kind into the icon key the Options plug-ins page renders.
/// Glyph keys use the <c>lucide:</c> prefix, extension icons use <c>ext:</c>, and everything else resolves as
/// a bitmap or file path.
/// </summary>
public static class PluginIconKey
{
    public const string GlyphPrefix = "lucide:";
    public const string ExtensionPrefix = "ext:";

    /// <summary>Icon used when a marketplace catalog entry declares no icon.</summary>
    public const string MarketplaceFallback = GlyphPrefix + "Puzzle";

    private const string FileBitmapKey = "file";

    public static string ForKind(PluginIconKind kind) => kind switch
    {
        PluginIconKind.Metadata => GlyphPrefix + "Tag",
        PluginIconKind.Preview => GlyphPrefix + "Eye",
        PluginIconKind.Accounts => GlyphPrefix + "User",
        PluginIconKind.Ai => GlyphPrefix + "Sparkles",
        PluginIconKind.FileFormat => FileBitmapKey,
        _ => MarketplaceFallback
    };

    /// <summary>
    /// A declared icon wins; otherwise file-format plugins keep their extension icon and every other plugin
    /// uses the icon of its capability kind.
    /// </summary>
    public static string Resolve(string? declaredIcon, PluginIconKind kind, string? primaryExtension)
    {
        if (!string.IsNullOrWhiteSpace(declaredIcon))
        {
            return declaredIcon.Trim();
        }

        if (kind == PluginIconKind.FileFormat && !string.IsNullOrWhiteSpace(primaryExtension))
        {
            return ExtensionPrefix + primaryExtension.Trim().TrimStart('.').ToLowerInvariant();
        }

        return ForKind(kind);
    }

    /// <summary>
    /// Marketplace manifests are remote data, so only local glyph and extension keys are accepted; anything
    /// else falls back to the neutral marketplace icon instead of being treated as a path.
    /// </summary>
    public static string ForMarketplace(string? declaredIcon)
    {
        var value = declaredIcon?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return MarketplaceFallback;
        }

        if (value.StartsWith(GlyphPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return value.Length > GlyphPrefix.Length ? value : MarketplaceFallback;
        }

        if (value.StartsWith(ExtensionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return value.Length > ExtensionPrefix.Length ? value : MarketplaceFallback;
        }

        return MarketplaceFallback;
    }

    public static bool IsGlyph(string? key) =>
        key is not null && key.StartsWith(GlyphPrefix, StringComparison.OrdinalIgnoreCase);

    public static PluginIconKind ParseKind(string? value) =>
        Enum.TryParse<PluginIconKind>(value, ignoreCase: true, out var kind)
            ? kind
            : PluginIconKind.Feature;
}
