using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Documents;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Derives the catalog effects a plugin asked for through its schema roles: a display name that stands in
/// for the file name, and tag names to assign. Only values a plugin actually published are considered, so a
/// disabled option produces no effect even though the field stays declared in the cached schema.
/// </summary>
public static class MediaMetadataCatalogEffects
{
    /// <summary>Names longer than this are ignored rather than becoming unreadable rows or tags.</summary>
    public const int MaxNameLength = 200;

    /// <summary>Upper bound on tags taken from one entry's cached metadata.</summary>
    public const int MaxTagsPerEntry = 32;

    /// <summary>Titles and tag names are split on these characters, matching common tag-list conventions.</summary>
    private static readonly char[] TagSeparators = [';', ','];

    /// <summary>
    /// Returns the preferred display name for an entry, or null when no current captured record publishes one.
    /// Failed and unsupported records never contribute; superseded records do, keeping a name readable offline.
    /// </summary>
    public static string? ResolveDisplayName(CatalogDocument entry, IReadOnlyList<MediaCacheRecord> records)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(records);

        foreach (var record in Order(records))
        {
            var roleKeys = RoleKeys(record, MediaMetadataFieldRole.PreferredName);
            foreach (var value in record.Metadata)
            {
                if (!roleKeys.Contains(value.Key))
                {
                    continue;
                }

                var text = NormalizeName(value.Text);
                if (text is not null && !string.Equals(text, entry.Name, StringComparison.Ordinal))
                {
                    return text;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Returns distinct tag names published by <see cref="MediaMetadataFieldRole.Tags"/> fields, in record
    /// precedence order.
    /// </summary>
    public static IReadOnlyList<string> ResolveTagNames(IReadOnlyList<MediaCacheRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in Order(records))
        {
            var roleKeys = RoleKeys(record, MediaMetadataFieldRole.Tags);
            foreach (var value in record.Metadata)
            {
                if (!roleKeys.Contains(value.Key) || string.IsNullOrWhiteSpace(value.Text))
                {
                    continue;
                }

                foreach (var candidate in value.Text.Split(TagSeparators,
                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var text = NormalizeName(candidate);
                    if (text is not null && seen.Add(text))
                    {
                        names.Add(text);
                        if (names.Count == MaxTagsPerEntry)
                        {
                            return names;
                        }
                    }
                }
            }
        }

        return names;
    }

    /// <summary>True when any record declares a role the host applies, which lets callers skip the lookup.</summary>
    public static bool HasCatalogEffects(MediaCacheRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return record.SchemaFields.Any(field => field.Role != MediaMetadataFieldRole.None);
    }

    private static HashSet<string> RoleKeys(MediaCacheRecord record, MediaMetadataFieldRole role) => record
        .SchemaFields
        .Where(field => field.Role == role && !string.IsNullOrWhiteSpace(field.Key))
        .Select(field => field.Key)
        .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<MediaCacheRecord> Order(IReadOnlyList<MediaCacheRecord> records) => records
        .Where(record => record.State is MediaCacheRecordState.Captured or MediaCacheRecordState.Stale)
        .OrderBy(record => record.State == MediaCacheRecordState.Captured ? 0 : 1)
        .ThenByDescending(record => record.Priority)
        .ThenBy(record => record.PluginId, StringComparer.Ordinal)
        .ThenBy(record => record.Capability, StringComparer.Ordinal);

    private static string? NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length is > 0 and <= MaxNameLength ? trimmed : null;
    }
}
