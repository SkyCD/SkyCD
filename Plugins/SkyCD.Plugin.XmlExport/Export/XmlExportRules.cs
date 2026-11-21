using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SkyCD.Plugin.XmlExport.Exceptions;

namespace SkyCD.Plugin.XmlExport.Export;

/// <summary>
/// Shared validation, formatting, and naming rules for the XML exporters.
/// </summary>
internal static class XmlExportRules
{
    internal const string DefaultDatabaseName = "Catalog";

    private static readonly string[] SizeKeys =
        ["size", "sizebytes"];

    private static readonly string[] DateKeys =
        ["date", "modified", "modifiedutc", "lastmodified", "lastmodifiedutc"];

    private static readonly HashSet<string> StructuralCatalogKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "nodeid", "parentid", "kind", "name",
        "size", "sizebytes",
        "date", "modified", "modifiedutc", "lastmodified", "lastmodifiedutc"
    };

    private static readonly HashSet<string> StructuralListKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "nodeid", "parentid", "kind"
    };

    internal static string? GetValue(Dictionary<string, object?> row, string key)
    {
        if (row.TryGetValue(key, out var direct))
        {
            return ToDisplayText(direct);
        }

        foreach (var (candidate, value) in row)
        {
            if (candidate.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return ToDisplayText(value);
            }
        }

        return null;
    }

    internal static string? ToDisplayText(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? bool.TrueString : bool.FalseString,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    internal static void ValidateText(string item, string field, string? value)
    {
        if (value is null)
        {
            return;
        }

        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];
            if (char.IsHighSurrogate(current)
                && i + 1 < value.Length
                && char.IsLowSurrogate(value[i + 1]))
            {
                // Astral-plane characters (#x10000-#x10FFFF) are valid in XML 1.0.
                i++;
                continue;
            }

            if (!IsValidXmlChar(current))
            {
                throw new XmlExportInvalidCharacterException(item, field);
            }
        }
    }

    private static bool IsValidXmlChar(char value) =>
        value is '\t' or '\n' or '\r'
            || (value >= 0x20 && value <= 0xD7FF)
            || (value >= 0xE000 && value <= 0xFFFD);

    internal static bool IsStructuralCatalogKey(string key) => StructuralCatalogKeys.Contains(key);

    internal static bool IsStructuralListKey(string key) => StructuralListKeys.Contains(key);

    internal static bool IsSizeKey(string key) =>
        Array.Exists(SizeKeys, candidate => candidate.Equals(key, StringComparison.OrdinalIgnoreCase));

    internal static bool IsDateKey(string key) =>
        Array.Exists(DateKeys, candidate => candidate.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Formats a size as unsigned decimal bytes. Missing values default to zero;
    /// negative or non-integer values fail with item/field diagnostics.
    /// </summary>
    internal static ulong FormatSize(string item, string field, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 0;
        }

        var text = raw.Trim();
        if (ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new XmlExportInvalidValueException(item, field,
            $"Value '{text}' is not an unsigned decimal byte size.");
    }

    /// <summary>
    /// Formats a timestamp as ISO-8601 in UTC. Missing values return null so the
    /// caller omits the attribute or element; unparseable values fail with
    /// item/field diagnostics.
    /// </summary>
    internal static string? FormatTimestamp(string item, string field, object? value)
    {
        var raw = value is string text ? text : ToDisplayText(value);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        DateTimeOffset parsed;
        var trimmed = raw.Trim();
        string[] formats =
        [
            "o", "yyyy-MM-ddTHH:mm:ss.FFFFFFFK", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd",
            "yyyy/MM/dd HH:mm:ss", "yyyy/MM/dd", "MM/dd/yyyy HH:mm:ss", "MM/dd/yyyy",
            "dd.MM.yyyy HH:mm:ss", "dd.MM.yyyy"
        ];
        if (!DateTimeOffset.TryParseExact(trimmed, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed)
            && !DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
        {
            throw new XmlExportInvalidValueException(item, field,
                $"Value '{trimmed}' is not a recognizable timestamp.");
        }

        var utc = parsed.UtcDateTime;
        return utc.TimeOfDay.Ticks % TimeSpan.TicksPerSecond == 0
            ? utc.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture)
            : utc.ToString("o", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Normalizes a display label to a valid XML element name without a namespace
    /// prefix. Reports whether normalization changed the label.
    /// </summary>
    internal static string NormalizeElementName(string label, out bool changed)
    {
        if (string.IsNullOrEmpty(label))
        {
            changed = true;
            return "Field";
        }

        var builder = new StringBuilder(label.Length);
        foreach (var current in label)
        {
            builder.Append(IsElementNameChar(current) ? current : '_');
        }

        var candidate = builder.ToString();
        if (candidate.Length == 0 || !IsElementNameStart(candidate[0]))
        {
            candidate = "_" + candidate;
        }

        if (candidate.Length == 0)
        {
            candidate = "Field";
        }

        changed = !candidate.Equals(label, StringComparison.Ordinal);
        return candidate;
    }

    private static bool IsElementNameStart(char value) =>
        value == '_' || char.IsLetter(value);

    private static bool IsElementNameChar(char value) =>
        value == '_' || value == '.' || value == '-' || char.IsLetterOrDigit(value);

    /// <summary>
    /// Maps an entry kind to its hierarchical element name.
    /// </summary>
    internal static string MapKindElement(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return "Item";
        }

        var normalized = kind.Trim().ToLowerInvariant();
        return normalized switch
        {
            "disk" => "Disk",
            "folder" or "directory" => "Folder",
            "file" => "File",
            _ => NormalizeElementName(kind.Trim(), out _)
        };
    }

    /// <summary>
    /// Maps an entry kind to its flat-list type label.
    /// </summary>
    internal static string MapKindType(string item, string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return "Item";
        }

        var trimmed = kind.Trim();
        var mapped = trimmed.ToLowerInvariant() switch
        {
            "disk" => "Disk",
            "folder" or "directory" => "Folder",
            "file" => "File",
            _ => trimmed
        };
        ValidateText(item, "Kind", mapped);
        return mapped;
    }

    internal static string ResolveDatabaseName(string? requested, string? fileName, string item)
    {
        var candidate = !string.IsNullOrWhiteSpace(requested)
            ? requested.Trim()
            : !string.IsNullOrWhiteSpace(fileName)
                ? Path.GetFileNameWithoutExtension(fileName.Trim())
                : DefaultDatabaseName;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            candidate = DefaultDatabaseName;
        }

        ValidateText(item, "DatabaseName", candidate);
        return candidate;
    }

    /// <summary>
    /// Validates an optional stylesheet reference. Only a bare relative
    /// <c>.xsl</c> file name is accepted: no directories, no absolute paths,
    /// no traversal, and no characters that could break out of the processing
    /// instruction or file-name rules on any platform.
    /// </summary>
    internal static string? ValidateStylesheet(string item, string? stylesheet)
    {
        if (string.IsNullOrWhiteSpace(stylesheet))
        {
            return null;
        }

        var name = stylesheet.Trim();
        if (!name.EndsWith(".xsl", StringComparison.OrdinalIgnoreCase))
        {
            throw new XmlExportInvalidValueException(item, "Stylesheet",
                $"Value '{name}' must reference a relative .xsl file name.");
        }

        if (name.Contains('/') || name.Contains('\\'))
        {
            throw new XmlExportInvalidValueException(item, "Stylesheet",
                $"Value '{name}' must be a bare file name without directories.");
        }

        if (Path.IsPathRooted(name) || name.Contains(':'))
        {
            throw new XmlExportInvalidValueException(item, "Stylesheet",
                $"Value '{name}' must be a relative file name, never an absolute path.");
        }

        if (name is "." or ".." || name.Contains("..", StringComparison.Ordinal))
        {
            throw new XmlExportInvalidValueException(item, "Stylesheet",
                $"Value '{name}' must not contain path traversal.");
        }

        foreach (var current in name)
        {
            if (current < 0x20 || current is '"' or '\'' or '<' or '>' or '&' or '?' or '*' or '|' or '`')
            {
                throw new XmlExportInvalidValueException(item, "Stylesheet",
                    $"Value '{name}' contains a character that is not allowed in a stylesheet reference.");
            }
        }

        ValidateText(item, "Stylesheet", name);
        return name;
    }
}
