using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Documents;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Combines cached plugin results with the entry's own properties into one ordered view. Captured values
/// come first in provider precedence order; user annotations always win over a captured value with the
/// same key, and the captured value stays available next to the edit.
/// </summary>
public static class MediaEntryMetadataProjection
{
    /// <summary>Projects the effective metadata of one catalog entry.</summary>
    /// <param name="entry">The catalog entry, or null to project cached values only.</param>
    /// <param name="records">Cache records of the entry, normally from <c>GetRecordsForEntry</c>.</param>
    /// <param name="includeStaleRecords">Keep values from superseded records, which is what makes previews and metadata usable offline.</param>
    public static IReadOnlyList<MediaMetadataFieldView> Project(
        CatalogDocument? entry,
        IReadOnlyList<MediaCacheRecord> records,
        bool includeStaleRecords = true)
    {
        ArgumentNullException.ThrowIfNull(records);

        var views = new List<MediaMetadataFieldView>();
        var indexByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in OrderRecords(records, includeStaleRecords))
        {
            var units = record.SchemaFields
                .GroupBy(field => field.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Unit, StringComparer.Ordinal);

            foreach (var value in record.Metadata)
            {
                if (string.IsNullOrWhiteSpace(value.Key) || indexByKey.ContainsKey(value.Key))
                {
                    continue;
                }

                units.TryGetValue(value.Key, out var unit);
                indexByKey[value.Key] = views.Count;
                views.Add(new MediaMetadataFieldView
                {
                    Key = value.Key,
                    Origin = MediaMetadataOrigin.Captured,
                    Value = value.GetPayload(),
                    Type = value.Type,
                    Unit = unit,
                    PluginId = record.PluginId,
                    PluginVersion = record.PluginVersion,
                    SchemaId = record.SchemaId,
                    SchemaVersion = record.SchemaVersion,
                    RecordState = record.State
                });
            }
        }

        // A failed capture publishes no values, but the fields it would have produced are known from its
        // schema. Showing them as unavailable keeps a failed hash visible instead of looking never computed.
        foreach (var record in records
                     .Where(record => record.State == MediaCacheRecordState.Failed)
                     .OrderByDescending(record => record.Priority)
                     .ThenBy(record => record.PluginId, StringComparer.Ordinal))
        {
            foreach (var field in record.SchemaFields)
            {
                if (string.IsNullOrWhiteSpace(field.Key) || indexByKey.ContainsKey(field.Key))
                {
                    continue;
                }

                indexByKey[field.Key] = views.Count;
                views.Add(new MediaMetadataFieldView
                {
                    Key = field.Key,
                    Origin = MediaMetadataOrigin.Captured,
                    Value = null,
                    Type = field.Type,
                    Unit = field.Unit,
                    PluginId = record.PluginId,
                    PluginVersion = record.PluginVersion,
                    SchemaId = record.SchemaId,
                    SchemaVersion = record.SchemaVersion,
                    RecordState = record.State
                });
            }
        }

        if (entry is not null)
        {
            foreach (var (key, value) in entry.Properties)
            {
                if (value is null || !MediaAnnotationPolicy.IsUserAnnotation(key))
                {
                    continue;
                }

                ApplyUserAnnotation(views, indexByKey, key, value);
            }

            if (!string.IsNullOrWhiteSpace(entry.Status))
            {
                ApplyUserAnnotation(views, indexByKey, nameof(CatalogDocument.Status), entry.Status);
            }
        }

        return views;
    }

    private static void ApplyUserAnnotation(
        List<MediaMetadataFieldView> views,
        Dictionary<string, int> indexByKey,
        string key,
        object value)
    {
        if (indexByKey.TryGetValue(key, out var index))
        {
            var captured = views[index];
            views[index] = new MediaMetadataFieldView
            {
                Key = captured.Key,
                Origin = MediaMetadataOrigin.UserEdited,
                Value = value,
                Type = captured.Type,
                Unit = captured.Unit,
                PluginId = captured.PluginId,
                PluginVersion = captured.PluginVersion,
                SchemaId = captured.SchemaId,
                SchemaVersion = captured.SchemaVersion,
                RecordState = captured.RecordState,
                CapturedValue = captured.Value
            };
            return;
        }

        indexByKey[key] = views.Count;
        views.Add(new MediaMetadataFieldView
        {
            Key = key,
            Origin = MediaMetadataOrigin.UserEdited,
            Value = value
        });
    }

    private static IEnumerable<MediaCacheRecord> OrderRecords(
        IReadOnlyList<MediaCacheRecord> records,
        bool includeStaleRecords) => records
        .Where(record => record.State switch
        {
            MediaCacheRecordState.Captured => true,
            MediaCacheRecordState.Missing => true,
            MediaCacheRecordState.Stale => includeStaleRecords,
            _ => false
        })
        .OrderBy(record => StateRank(record.State))
        .ThenByDescending(record => record.Priority)
        .ThenBy(record => record.PluginId, StringComparer.Ordinal)
        .ThenBy(record => record.Capability, StringComparer.Ordinal);

    private static int StateRank(MediaCacheRecordState state) => state switch
    {
        MediaCacheRecordState.Captured => 0,
        MediaCacheRecordState.Missing => 1,
        _ => 2
    };
}
