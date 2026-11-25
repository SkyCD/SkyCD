using System;

namespace SkyCD.Presentation.Plugins;

/// <summary>
/// Icon keys rendered by the Options plug-ins page. Installed plugins show a frame from their embedded ICO
/// assembly, while marketplace entries are remote data and may only declare a Lucide glyph or an extension key.
/// </summary>
public static class PluginIconKey
{
    public const string GlyphPrefix = "lucide:";
    public const string ExtensionPrefix = "ext:";

    /// <summary>Neutral glyph used for marketplace entries without an icon.</summary>
    public const string Fallback = GlyphPrefix + "Puzzle";

    /// <summary>
    /// Decodes the base64 icon persisted with a plugin descriptor, or <see langword="null"/> when the value is
    /// missing or unreadable.
    /// </summary>
    public static byte[]? TryDecodePersisted(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return null;
        }

        try
        {
            var icon = Convert.FromBase64String(base64);
            return icon.Length > 0 ? icon : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Marketplace manifests are remote data, so only local glyph and extension keys are accepted; anything
    /// else falls back to the neutral icon instead of being treated as a path or a download.
    /// </summary>
    public static string ForMarketplace(string? declaredIcon)
    {
        var value = declaredIcon?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return Fallback;
        }

        if (value.StartsWith(GlyphPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return value.Length > GlyphPrefix.Length ? value : Fallback;
        }

        if (value.StartsWith(ExtensionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return value.Length > ExtensionPrefix.Length ? value : Fallback;
        }

        return Fallback;
    }

    public static bool IsGlyph(string? key) =>
        key is not null && key.StartsWith(GlyphPrefix, StringComparison.OrdinalIgnoreCase);
}
