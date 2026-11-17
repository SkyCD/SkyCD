using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.GCstar;

/// <summary>
/// Name vocabulary for GCstar collection exports. Attribute matching is exact
/// first (matching GCstar's lowercase field names) with a case-insensitive fallback.
/// </summary>
public static class GCstarElementNames
{
    public const string RootElement = "collection";
    public const string InformationElement = "information";
    public const string ItemElement = "item";
    public const string TypeAttribute = "type";
    public const string VersionAttribute = "version";
    public const string ItemsAttribute = "items";

    public const string DemoMarker = "*** DEMO ***";

    /// <summary>
    /// Field names with defined import semantics. Matching is case-insensitive.
    /// Everything outside this set is retained verbatim as namespaced extension
    /// metadata and reported as an unsupported field.
    /// </summary>
    private static readonly HashSet<string> KnownFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "name", "title", "description", "comments", "secrets",
        "borrower", "lenddate", "borrowings", "favourite", "tags",
        "ean", "platform", "players", "editor", "developer", "released", "genre",
        "rating", "ratingpress", "case", "manual", "exclusive", "added",
        "completion", "resolutions", "location", "installationsize",
        "installationsizeunit", "executable", "code", "unlockable",
        "region", "serialnumber",
        "cover", "coverurl", "boxpic", "backpic", "image", "picture", "frontpic",
        "screenshot1", "screenshot2"
    };

    private static readonly HashSet<string> FlagFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "case", "manual", "exclusive", "favourite"
    };

    private static readonly HashSet<string> FreeTextFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "title", "description", "comments", "secrets", "label"
    };

    /// <summary>
    /// Attribute names (lowercase) that always hold image references regardless of model.
    /// </summary>
    private static readonly HashSet<string> ImageFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "cover", "boxpic", "backpic", "image", "picture", "frontpic",
        "screenshot1", "screenshot2", "coverurl"
    };

    private static readonly HashSet<string> CollectionMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "borrower", "lendDate", "borrowings", "favourite", "tags",
        "ean", "boxpic", "platform", "serialnumber", "cover"
    };

    public static bool IsCollectionMarker(string localName) =>
        CollectionMarkers.Contains(localName);

    public static bool IsKnownField(string name) => KnownFields.Contains(name);

    public static bool IsFlagField(string name) => FlagFields.Contains(name);

    public static bool IsFreeTextField(string name) => FreeTextFields.Contains(name);

    /// <summary>
    /// Date-like fields keep the raw text and gain a normalized ISO date when parsing succeeds.
    /// Year-only values intentionally gain no twin: inventing a month and day would corrupt data.
    /// </summary>
    public static bool IsDateField(string name) =>
        name.Contains("date", StringComparison.OrdinalIgnoreCase)
        || name.Equals("added", StringComparison.OrdinalIgnoreCase)
        || name.Equals("released", StringComparison.OrdinalIgnoreCase);

    public static bool IsImageField(string name)
    {
        if (ImageFields.Contains(name))
        {
            return true;
        }

        var lower = name.ToLowerInvariant();
        return lower.EndsWith("pic", StringComparison.Ordinal)
            || lower.EndsWith("image", StringComparison.Ordinal)
            || lower.EndsWith("cover", StringComparison.Ordinal)
            || lower.EndsWith("screenshot", StringComparison.Ordinal);
    }

    public static bool IsDemoValue(string? value) =>
        !string.IsNullOrEmpty(value) && value.Contains(DemoMarker, StringComparison.Ordinal);
}
