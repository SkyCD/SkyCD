using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SkyCD.Plugin.GCstar;

/// <summary>
/// Maps a parsed GCstar collection onto catalog rows consumed by the normal
/// catalog loader (root media node plus one leaf row per kept item).
/// Values are typed generically: numbers stay numeric (no leading-zero loss),
/// flag fields become "true"/"false", date-like fields keep raw text plus an
/// ISO normalized twin, and multi-value fields become string lists. Unknown
/// data parsed into extension metadata is surfaced under its namespaced key.
/// </summary>
public static class GCstarHierarchyBuilder
{
    private static readonly Regex CanonicalNumber = new(@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?$", RegexOptions.Compiled);

    private static readonly HashSet<string> NumericFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "rating", "ratingpress", "players", "installationsize"
    };

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyy/MM/dd", "yyyy.MM.dd", "yyyyMMdd",
        "dd-MM-yyyy", "dd/MM/yyyy", "dd.MM.yyyy",
        "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "O", "o"
    ];

    private static readonly string[] ImagePreferenceOrder =
    [
        "cover", "boxpic", "image", "picture", "frontpic", "backpic",
        "screenshot1", "screenshot2", "coverurl"
    ];

    public static GCstarReportDocument Compose(GCstarParseResult parseResult)
    {
        var rows = new List<Dictionary<string, object?>>();
        var usedNodeIds = new HashSet<string>(StringComparer.Ordinal);
        const string rootNodeId = "gcstar:collection";

        var rootProperties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(parseResult.CollectionType))
        {
            rootProperties["CollectionType"] = parseResult.CollectionType;
            rootProperties["DatabaseType"] = parseResult.CollectionType;
        }

        if (!string.IsNullOrWhiteSpace(parseResult.Version))
        {
            rootProperties["FormatVersion"] = parseResult.Version;
        }

        if (parseResult.Information.Count > 0)
        {
            foreach (var (key, value) in parseResult.Information)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    rootProperties[key] = value;
                }
            }
        }

        if (parseResult.UnsupportedFields.Count > 0)
        {
            rootProperties["UnsupportedFields"] = string.Join("; ", parseResult.UnsupportedFields);
        }

        if (parseResult.SkippedItemsCount > 0)
        {
            rootProperties["SkippedItems"] = parseResult.SkippedItemsCount.ToString(CultureInfo.InvariantCulture);
            if (parseResult.SkippedReasons.Count > 0)
            {
                rootProperties["SkippedReasons"] = string.Join("; ", parseResult.SkippedReasons);
            }
        }

        var keptItems = 0;
        foreach (var item in parseResult.Items)
        {
            if (item is null)
            {
                continue;
            }

            keptItems++;
        }

        rootProperties["ItemCount"] = keptItems.ToString(CultureInfo.InvariantCulture);

        var rootRow = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["nodeId"] = rootNodeId,
            ["parentId"] = null,
            ["kind"] = "media",
            ["name"] = string.IsNullOrWhiteSpace(parseResult.CollectionName)
                ? "GCstar Collection"
                : parseResult.CollectionName,
            ["sizeBytes"] = ComputeTotalInstallSize(parseResult),
            ["childrenCount"] = (long)keptItems,
            ["properties"] = rootProperties
        };
        rows.Add(rootRow);

        var fileCount = 0;
        foreach (var item in parseResult.Items)
        {
            if (item is null)
            {
                continue;
            }

            fileCount++;
            rows.Add(BuildItemRow(item, rootNodeId, usedNodeIds));
        }

        var summary = new GCstarImportSummary
        {
            Volumes = 1,
            Files = fileCount,
            SkippedRows = parseResult.SkippedItemsCount,
            CollectionType = parseResult.CollectionType,
            Version = parseResult.Version,
            CollectionName = parseResult.CollectionName
        };

        if (parseResult.UnsupportedFields.Count > 0)
        {
            summary.Notes.Add("Unsupported fields: " + string.Join("; ", parseResult.UnsupportedFields));
        }

        foreach (var reason in parseResult.SkippedReasons)
        {
            summary.Notes.Add(reason);
        }

        return new GCstarReportDocument(rows, summary);
    }

    private static Dictionary<string, object?> BuildItemRow(
        GCstarItem item,
        string parentNodeId,
        HashSet<string> usedNodeIds)
    {
        var id = TryGetField(item.Fields, "id", out var rawId) && !string.IsNullOrWhiteSpace(rawId)
            ? rawId.Trim()
            : $"item-{item.Index}";
        var name = TryGetField(item.Fields, "__name", out var resolved) && !string.IsNullOrWhiteSpace(resolved)
            ? resolved.Trim()
            : $"Unnamed {id}";

        var nodeId = MakeUniqueNodeId(id, item.Index, usedNodeIds);
        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["GCstarId"] = id
        };

        foreach (var (field, value) in item.Fields)
        {
            if (field is "__name" or "id")
            {
                continue;
            }

            AddTypedProperty(properties, field, value);
        }

        foreach (var (key, value) in item.ExtraFields)
        {
            properties.TryAdd(key, value);
        }

        if (!properties.ContainsKey("Image"))
        {
            var primary = FindPrimaryImage(item.Fields);
            if (primary is not null)
            {
                properties["Image"] = primary;
            }
        }

        var sizeBytes = ComputeItemInstallSizeBytes(item.Fields);

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["nodeId"] = nodeId,
            ["parentId"] = parentNodeId,
            ["kind"] = "file",
            ["name"] = name,
            ["sizeBytes"] = sizeBytes,
            ["properties"] = properties
        };
    }

    private static void AddTypedProperty(Dictionary<string, object?> properties, string field, string value)
    {
        var key = ToPropertyKey(field);
        if (GCstarElementNames.IsFlagField(field))
        {
            properties[key] = ParseFlag(value) ? "true" : "false";
            return;
        }

        if (GCstarElementNames.IsFreeTextField(field))
        {
            properties[key] = value;
            return;
        }

        if (field.Equals("tags", StringComparison.OrdinalIgnoreCase))
        {
            properties[key] = SplitList(value, ',');
            return;
        }

        if (value.Contains(" / ", StringComparison.Ordinal))
        {
            properties[key] = SplitList(value, '/');
            return;
        }

        if (GCstarElementNames.IsImageField(field))
        {
            properties[key] = value;
            return;
        }

        if (GCstarElementNames.IsDateField(field))
        {
            properties[key] = value;
            var normalized = NormalizeDate(value);
            if (normalized is not null)
            {
                properties[key + "Normalized"] = normalized;
            }

            return;
        }

        if (NumericFields.Contains(field) && TryParseCanonicalNumber(value, out var number))
        {
            properties[key] = number;
            return;
        }

        properties[key] = value;
    }

    private static object SplitList(string value, char separator)
    {
        var parts = separator == ','
            ? value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : value.Split([" / "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 1 ? new List<string>(parts) : (object)value.Trim();
    }

    private static bool ParseFlag(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "1" or "true" or "yes" or "y" or "on";
    }

    private static string? NormalizeDate(string value)
    {
        var trimmed = value.Trim();
        foreach (var format in DateFormats)
        {
            if (DateTime.TryParseExact(trimmed, format, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
            {
                return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private static bool TryParseCanonicalNumber(string value, out object number)
    {
        number = value;
        if (!CanonicalNumber.IsMatch(value.Trim()))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Contains('.', StringComparison.Ordinal))
        {
            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDouble))
            {
                number = parsedDouble;
                return true;
            }

            return false;
        }

        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLong))
        {
            number = parsedLong;
            return true;
        }

        return false;
    }

    private static string? FindPrimaryImage(Dictionary<string, string> fields)
    {
        foreach (var candidate in ImagePreferenceOrder)
        {
            foreach (var (field, value) in fields)
            {
                if (field.Equals(candidate, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }

        return null;
    }

    private static long ComputeItemInstallSizeBytes(Dictionary<string, string> fields)
    {
        if (!TryGetField(fields, "installationsize", out var sizeText)
            || !double.TryParse(sizeText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var size)
            || size < 0)
        {
            return 0L;
        }

        var multiplier = 1L;
        if (TryGetField(fields, "installationsizeunit", out var unit))
        {
            multiplier = UnitMultiplier(unit);
        }

        var bytes = size * multiplier;
        return bytes > long.MaxValue ? long.MaxValue : (long)bytes;
    }

    private static long ComputeTotalInstallSize(GCstarParseResult parseResult)
    {
        long total = 0;
        foreach (var item in parseResult.Items)
        {
            if (item is null)
            {
                continue;
            }

            total += ComputeItemInstallSizeBytes(item.Fields);
            if (total < 0)
            {
                return long.MaxValue;
            }
        }

        return total;
    }

    private static long UnitMultiplier(string unit)
    {
        var normalized = unit.Trim().ToLowerInvariant();
        return normalized switch
        {
            "b" or "byte" or "bytes" => 1L,
            "kb" or "k" or "kib" => 1024L,
            "mb" or "m" or "mib" => 1024L * 1024,
            "gb" or "g" or "gib" => 1024L * 1024 * 1024,
            "tb" or "t" or "tib" => 1024L * 1024 * 1024 * 1024,
            _ => 1L
        };
    }

    private static bool TryGetField(Dictionary<string, string> fields, string name, out string value)
    {
        foreach (var (field, candidate) in fields)
        {
            if (field.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static string MakeUniqueNodeId(string id, int index, HashSet<string> usedNodeIds)
    {
        var sanitized = new System.Text.StringBuilder("gcstar:item:");
        foreach (var ch in id.Trim())
        {
            sanitized.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.'
                ? ch
                : '-');
        }

        if (sanitized.Length == "gcstar:item:".Length)
        {
            sanitized.Append($"item-{index}");
        }

        var candidate = sanitized.ToString();
        var attempt = candidate;
        var suffix = 2;
        while (!usedNodeIds.Add(attempt))
        {
            attempt = $"{candidate}-{suffix}";
            suffix++;
        }

        return attempt;
    }

    private static string ToPropertyKey(string field)
    {
        return field switch
        {
            "id" => "GCstarId",
            "name" => "Title",
            "title" => "Title",
            "description" => "Description",
            "comments" => "Comments",
            "secrets" => "Secrets",
            "borrower" => "Borrower",
            "lenddate" => "LendDate",
            "borrowings" => "Borrowings",
            "favourite" => "Favourite",
            "tags" => "Tags",
            "platform" => "Platform",
            "released" => "Released",
            "added" => "DateAdded",
            "cover" => "Cover",
            "coverurl" => "CoverUrl",
            "boxpic" => "BoxPic",
            "backpic" => "BackPic",
            "image" => "ImageFile",
            "picture" => "Picture",
            "frontpic" => "FrontPic",
            "screenshot1" => "Screenshot1",
            "screenshot2" => "Screenshot2",
            "installationsize" => "InstallationSize",
            "installationsizeunit" => "InstallationSizeUnit",
            "serialnumber" => "SerialNumber",
            _ => char.ToUpperInvariant(field[0]) + field.Substring(1)
        };
    }
}
