using System;
using System.Globalization;
using System.Text;
using Avalonia.Data.Converters;
using IconPacks.Avalonia.Lucide;
using SkyCD.Presentation.Plugins;

namespace SkyCD.App.Converters;

/// <summary>
/// Resolves a <c>lucide:&lt;Glyph&gt;</c> plugin icon key to the icon-pack glyph. Invalid or non-glyph keys
/// resolve to the neutral puzzle glyph so a plugin icon can never break the plugin list.
/// </summary>
public sealed class PluginGlyphConverter : IValueConverter
{
    public static bool TryResolve(string? key, out PackIconLucideKind kind)
    {
        kind = PackIconLucideKind.Puzzle;
        if (!PluginIconKey.IsGlyph(key))
        {
            return false;
        }

        var name = key![PluginIconKey.GlyphPrefix.Length..];
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var builder = new StringBuilder(name.Length);
        foreach (var character in name)
        {
            if (character is '-' or '_' or ' ')
            {
                continue;
            }

            builder.Append(character);
        }

        return builder.Length > 0 &&
               !char.IsDigit(builder[0]) &&
               Enum.TryParse(builder.ToString(), ignoreCase: true, out kind) &&
               Enum.IsDefined(kind);
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string key && TryResolve(key, out var kind) ? kind : PackIconLucideKind.Puzzle;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
