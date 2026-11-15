using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Documents;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Makes captured plugin metadata searchable by projecting the stored cache records of an entry into
/// generic key/value fields. The projection is schema driven, so a new plugin's fields become searchable
/// without any change to the search UI and without reconnecting the media.
/// </summary>
public sealed class MediaCacheSearchFieldProvider : ICatalogSearchFieldProvider
{
    private readonly IMediaCacheStore store;

    public MediaCacheSearchFieldProvider(IMediaCacheStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// Reads every record once and indexes it by entry, so searching a large catalog costs one store pass
    /// instead of one query per entry. Records hold metadata only; artifact bytes stay in storage.
    /// </summary>
    public CatalogSearchFieldLookup OpenLookup()
    {
        var recordsByEntry = store.GetAllRecords()
            .Where(record => !string.IsNullOrWhiteSpace(record.EntryId))
            .GroupBy(record => record.EntryId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<MediaCacheRecord>)group.ToList(), StringComparer.Ordinal);

        return document =>
        {
            if (string.IsNullOrWhiteSpace(document.Id) ||
                !recordsByEntry.TryGetValue(document.Id, out var records))
            {
                return [];
            }

            return MediaEntryMetadataProjection.Project(document, records)
                .Select(field => new CatalogSearchField(
                    MediaMetadataValueFormatter.ResolveLabel(field.Key),
                    MediaMetadataValueFormatter.Format(field.Value, field.Type, field.Unit)))
                .Where(field => !string.IsNullOrWhiteSpace(field.Value));
        };
    }
}
