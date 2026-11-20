using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.MediaCache;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Applies the catalog effects a media plugin asked for through its schema roles: a preferred display name
/// for the browser and writer tags for newly published files. Both are derived from captured cache records,
/// so they keep working offline and never rewrite the catalogued file name.
/// </summary>
public partial class MainWindowViewModel
{
    private Dictionary<string, IReadOnlyList<MediaCacheRecord>>? recordsByEntryId;

    /// <summary>Drops captured records held for the browser projection; called when the cache changes.</summary>
    private void InvalidateMediaCatalogEffects() => recordsByEntryId = null;

    private Dictionary<string, IReadOnlyList<MediaCacheRecord>> GetRecordsByEntryId()
    {
        if (recordsByEntryId is not null)
        {
            return recordsByEntryId;
        }

        var map = new Dictionary<string, IReadOnlyList<MediaCacheRecord>>(StringComparer.Ordinal);
        if (!isDisposed)
        {
            foreach (var group in mediaCacheStore.GetAllRecords().GroupBy(record => record.EntryId, StringComparer.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(group.Key))
                {
                    map[group.Key] = group.ToArray();
                }
            }
        }

        return recordsByEntryId = map;
    }

    /// <summary>
    /// Projects preferred names captured for a previous source state onto the visible entries. An entry whose
    /// name was renamed since capture keeps the rename, because the captured title no longer describes it.
    /// </summary>
    private void ApplyPreferredNames(IReadOnlyList<CatalogDocument> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        var records = GetRecordsByEntryId();
        foreach (var item in items)
        {
            item.SetDisplayName(records.TryGetValue(item.Id, out var entryRecords)
                ? MediaMetadataCatalogEffects.ResolveDisplayName(item, entryRecords)
                : null);
        }
    }

    /// <summary>
    /// Creates and assigns tags published by a plugin for the files just added. An entry that already carries
    /// any tag is left alone, so a user's own tagging decisions are never revisited by a later scan.
    /// </summary>
    private bool ApplyPluginTags(IReadOnlyList<MediaCacheRecord> records, IReadOnlyList<CatalogDocument> published)
    {
        if (tagService is null || records.Count == 0)
        {
            return false;
        }

        var files = published
            .Where(entry => entry.Type == CatalogDocumentType.File)
            .ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        var tagIdsByName = tagService.GetTags()
            .Where(tag => !string.IsNullOrWhiteSpace(tag.Name) && !string.IsNullOrWhiteSpace(tag.Id))
            .GroupBy(tag => tag.Name!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);

        var applied = false;
        foreach (var group in records.GroupBy(record => record.EntryId, StringComparer.Ordinal))
        {
            if (!files.TryGetValue(group.Key, out var entry) || HasAnyTag(entry))
            {
                continue;
            }

            var names = MediaMetadataCatalogEffects.ResolveTagNames(group.ToArray());
            var tagIds = new List<string>(names.Count);
            foreach (var name in names)
            {
                if (!tagIdsByName.TryGetValue(name, out var tagId))
                {
                    try
                    {
                        tagId = tagService.CreateTag(name).Id;
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    tagIdsByName[name] = tagId;
                }

                tagIds.Add(tagId);
            }

            if (tagIds.Count == 0)
            {
                continue;
            }

            tagService.AssignTags([group.Key], tagIds);
            applied = true;
        }

        if (applied)
        {
            RefreshTagsMenuItems();
        }

        return applied;
    }

    private static bool HasAnyTag(CatalogDocument entry) =>
        entry.Properties.TryGetValue(SkyCD.Presentation.Tags.CatalogTagService.TagsPropertyKey, out var raw) &&
        raw is System.Collections.IEnumerable values &&
        values.Cast<object?>().Any(value => !string.IsNullOrWhiteSpace(value?.ToString()));
}
