using System;
using System.Globalization;
using Microsoft.Extensions.Localization;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Renders a stored typed value and its schema field name for display. Formatting is driven only by the
/// declared <see cref="MediaMetadataType"/> and unit, so the browser shows whatever a plugin captured
/// without knowing anything about the media format that produced it.
/// </summary>
public static class MediaMetadataValueFormatter
{
    /// <summary>
    /// Localizes a schema field key through the optional <c>core.metadata.</c> catalog and falls back to the
    /// key itself, which lets a plugin ship its own translations without any host-side field list.
    /// </summary>
    public static string ResolveLabel(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var translationKey = PreviewKeys.FieldPrefix + key;
        var translated = LocalizationService.Current.Get(translationKey);
        return string.Equals(translated, translationKey, StringComparison.Ordinal) ? key : translated;
    }

    /// <summary>Formats one value with its unit, using the supplied localizer for boolean and empty states.</summary>
    public static string Format(
        object? value,
        MediaMetadataType? type,
        string? unit,
        IStringLocalizer? localizer = null)
    {
        if (value is null)
        {
            return localizer?["Unknown"] ?? "Unknown";
        }

        var culture = LocalizationService.Current.CurrentCulture;
        var text = type switch
        {
            MediaMetadataType.Boolean => FormatBoolean(value, localizer),
            MediaMetadataType.Timestamp => FormatTimestamp(value, culture),
            MediaMetadataType.Integer => FormatInteger(value, culture),
            MediaMetadataType.Number => FormatNumber(value, culture),
            _ => value.ToString() ?? string.Empty
        };

        return AppendUnit(text, unit);
    }

    private static string FormatBoolean(object value, IStringLocalizer? localizer)
    {
        if (!TryGetBoolean(value, out var flag))
        {
            return value.ToString() ?? string.Empty;
        }

        return localizer?[flag ? "Yes" : "No"] ?? (flag ? "Yes" : "No");
    }

    private static bool TryGetBoolean(object value, out bool flag)
    {
        switch (value)
        {
            case bool boolean:
                flag = boolean;
                return true;
            case string text when bool.TryParse(text, out var parsed):
                flag = parsed;
                return true;
            default:
                flag = false;
                return false;
        }
    }

    private static string FormatTimestamp(object value, CultureInfo culture) => value switch
    {
        DateTimeOffset offset => offset.LocalDateTime.ToString("g", culture),
        DateTime dateTime => dateTime.ToString("g", culture),
        _ => value.ToString() ?? string.Empty
    };

    private static string FormatInteger(object value, CultureInfo culture) => value switch
    {
        long integer => integer.ToString("N0", culture),
        int integer => integer.ToString("N0", culture),
        _ => value.ToString() ?? string.Empty
    };

    private static string FormatNumber(object value, CultureInfo culture) => value switch
    {
        double number when double.IsFinite(number) => number.ToString("0.###", culture),
        float number when float.IsFinite(number) => number.ToString("0.###", culture),
        _ => value.ToString() ?? string.Empty
    };

    private static string AppendUnit(string text, string? unit) =>
        string.IsNullOrWhiteSpace(unit) || string.IsNullOrEmpty(text) ? text : text + ' ' + unit;
}
