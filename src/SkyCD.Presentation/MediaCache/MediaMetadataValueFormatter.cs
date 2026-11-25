using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Localization;
using SkyCD.SDK.Capabilities.Media;
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
    /// Localizes a schema field key. A plugin-supplied key first consults that plugin's own
    /// <c>metadata.&lt;key&gt;</c> translation, then the core <c>core.metadata.</c> catalog; a key with no
    /// translation is humanized so raw identifiers never reach the UI.
    /// </summary>
    public static string ResolveLabel(string key, string? pluginId = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(pluginId))
        {
            var pluginKey = "plugins." + pluginId + ".metadata." + key;
            var pluginLabel = LocalizationService.Current.Get(pluginKey);
            if (!string.Equals(pluginLabel, pluginKey, StringComparison.Ordinal))
            {
                return pluginLabel;
            }
        }

        var translationKey = PreviewKeys.FieldPrefix + key;
        var translated = LocalizationService.Current.Get(translationKey);
        return string.Equals(translated, translationKey, StringComparison.Ordinal) ? HumanizeKey(key) : translated;
    }

    /// <summary>
    /// Turns an untranslated identifier into a label: <c>FullName</c> becomes "Full name" and
    /// <c>LastWriteTime</c> becomes "Last write time". Acronyms such as <c>SHA256</c> keep their casing.
    /// </summary>
    public static string HumanizeKey(string key)
    {
        var words = SplitWords(key);
        if (words.Count == 0)
        {
            return key;
        }

        var label = new StringBuilder(key.Length + words.Count);
        for (var index = 0; index < words.Count; index++)
        {
            if (index > 0)
            {
                label.Append(' ');
            }

            var word = words[index];
            if (IsAcronym(word))
            {
                label.Append(word);
            }
            else if (index == 0)
            {
                label.Append(char.ToUpperInvariant(word[0]));
                label.Append(word.AsSpan(1).ToString().ToLowerInvariant());
            }
            else
            {
                label.Append(word.ToLowerInvariant());
            }
        }

        return label.ToString();
    }

    private static List<string> SplitWords(string key)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        for (var index = 0; index < key.Length; index++)
        {
            var character = key[index];
            if (character is '_' or '-' or ' ')
            {
                Flush();
                continue;
            }

            if (char.IsUpper(character) && current.Length > 0)
            {
                var previous = current[^1];
                var nextIsLower = index + 1 < key.Length && char.IsLower(key[index + 1]);
                if (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower))
                {
                    Flush();
                }
            }

            current.Append(character);
        }

        Flush();
        return words;

        void Flush()
        {
            if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }
    }

    private static bool IsAcronym(string word)
    {
        var hasLetter = false;
        foreach (var character in word)
        {
            if (char.IsLetter(character))
            {
                if (!char.IsUpper(character))
                {
                    return false;
                }

                hasLetter = true;
            }
            else if (!char.IsDigit(character))
            {
                return false;
            }
        }

        return hasLetter && word.Length > 1;
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
