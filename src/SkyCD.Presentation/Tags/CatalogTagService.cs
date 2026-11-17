using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Repository;

namespace SkyCD.Presentation.Tags;

/// <summary>
/// Assigns reusable <see cref="TagDocument"/> tags to catalog entries. Assignments are
/// stored as sorted tag-id lists in each entry's <c>Tags</c> property, so they travel
/// with the native catalog document through repository recreation and restart without
/// changing the source hierarchy. Tag definitions live in <see cref="TagDocumentRepository"/>
/// and are referenced by stable ids, so renaming never orphans assignments.
/// </summary>
public sealed class CatalogTagService(
    CatalogDocumentRepository catalogRepository,
    TagDocumentRepository tagRepository)
{
    public const string TagsPropertyKey = "Tags";

    private readonly CatalogDocumentRepository catalogRepository =
        catalogRepository ?? throw new ArgumentNullException(nameof(catalogRepository));
    private readonly TagDocumentRepository tagRepository =
        tagRepository ?? throw new ArgumentNullException(nameof(tagRepository));

    public IReadOnlyList<TagDocument> GetTags() => tagRepository.GetOrdered();

    public TagDocument CreateTag(string name) => tagRepository.CreateTag(name);

    public void RenameTag(string tagId, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagId);
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new TagNameRequiredException();
        }

        var tag = tagRepository.Get(tagId) ?? throw new TagNotFoundException(tagId);
        var trimmed = newName.Trim();
        var duplicate = tagRepository.FindByName(trimmed);
        if (duplicate is not null && !string.Equals(duplicate.Id, tagId, StringComparison.Ordinal))
        {
            throw new TagDuplicateNameException(trimmed);
        }

        if (string.Equals(tag.Name, trimmed, StringComparison.Ordinal))
        {
            return;
        }

        tag.Name = trimmed;
        tagRepository.Save(tag.Id, tag);
    }

    /// <summary>
    /// Merges the source tag into the target tag: every entry assigned the source is
    /// assigned the target instead, then the source tag is deleted. No orphan references remain.
    /// </summary>
    public void MergeTags(string sourceTagId, string targetTagId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceTagId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetTagId);
        if (string.Equals(sourceTagId, targetTagId, StringComparison.Ordinal))
        {
            return;
        }

        _ = tagRepository.Get(sourceTagId) ?? throw new TagNotFoundException(sourceTagId);
        _ = tagRepository.Get(targetTagId) ?? throw new TagNotFoundException(targetTagId);

        foreach (var entry in catalogRepository.GetAll())
        {
            var assigned = ReadTagIds(entry);
            if (!assigned.Contains(sourceTagId, StringComparer.Ordinal))
            {
                continue;
            }

            var merged = assigned
                .Where(id => !string.Equals(id, sourceTagId, StringComparison.Ordinal))
                .Append(targetTagId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            WriteEntryTags(entry.Id, merged);
        }

        using var document = tagRepository.Collection.GetDocument(sourceTagId);
        if (document is not null)
        {
            tagRepository.Collection.Delete(document);
        }
    }

    /// <summary>
    /// Deletes a tag and removes its assignments from every entry, so no orphan references remain.
    /// </summary>
    public void DeleteTag(string tagId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagId);
        _ = tagRepository.Get(tagId) ?? throw new TagNotFoundException(tagId);

        foreach (var entry in catalogRepository.GetAll())
        {
            var assigned = ReadTagIds(entry);
            if (!assigned.Contains(tagId, StringComparer.Ordinal))
            {
                continue;
            }

            WriteEntryTags(entry.Id, assigned
                .Where(id => !string.Equals(id, tagId, StringComparison.Ordinal))
                .ToArray());
        }

        using var document = tagRepository.Collection.GetDocument(tagId);
        if (document is not null)
        {
            tagRepository.Collection.Delete(document);
        }
    }

    public IReadOnlyList<string> GetEntryTagIds(string entryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        var entry = catalogRepository.Get(entryId);
        return entry is null ? [] : ReadTagIds(entry);
    }

    public IReadOnlyList<TagDocument> GetEntryTags(string entryId)
    {
        var byId = tagRepository.GetAll()
            .Where(static tag => !string.IsNullOrWhiteSpace(tag.Id))
            .ToDictionary(static tag => tag.Id, StringComparer.Ordinal);
        return GetEntryTagIds(entryId)
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .ToArray();
    }

    public void SetEntryTags(string entryId, IEnumerable<string> tagIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentNullException.ThrowIfNull(tagIds);
        _ = catalogRepository.Get(entryId)
            ?? throw new InvalidOperationException($"Catalog entry '{entryId}' does not exist.");
        WriteEntryTags(entryId, NormalizeTagIds(tagIds));
    }

    public void AssignTags(IEnumerable<string> entryIds, IEnumerable<string> tagIds)
    {
        ArgumentNullException.ThrowIfNull(entryIds);
        var normalized = NormalizeTagIds(tagIds);
        if (normalized.Count == 0)
        {
            return;
        }

        foreach (var entryId in entryIds.Distinct(StringComparer.Ordinal))
        {
            var entry = catalogRepository.Get(entryId);
            if (entry is null)
            {
                continue;
            }

            var merged = ReadTagIds(entry)
                .Concat(normalized)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            WriteEntryTags(entry.Id, merged);
        }
    }

    public void UnassignTags(IEnumerable<string> entryIds, IEnumerable<string> tagIds)
    {
        ArgumentNullException.ThrowIfNull(entryIds);
        ArgumentNullException.ThrowIfNull(tagIds);
        var removed = new HashSet<string>(tagIds, StringComparer.Ordinal);
        if (removed.Count == 0)
        {
            return;
        }

        foreach (var entryId in entryIds.Distinct(StringComparer.Ordinal))
        {
            var entry = catalogRepository.Get(entryId);
            if (entry is null)
            {
                continue;
            }

            var remaining = ReadTagIds(entry)
                .Where(id => !removed.Contains(id))
                .ToArray();
            WriteEntryTags(entry.Id, remaining);
        }
    }

    public IReadOnlyList<CatalogDocument> GetEntriesWithTag(string tagId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagId);
        return catalogRepository.GetAll()
            .Where(entry => ReadTagIds(entry).Contains(tagId, StringComparer.Ordinal))
            .OrderBy(static entry => entry.Id, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Strips assignments that reference deleted tags. Returns the number of stripped references.
    /// </summary>
    public int RemoveMissingTagReferences()
    {
        var known = new HashSet<string>(
            tagRepository.GetAll().Select(static tag => tag.Id),
            StringComparer.Ordinal);
        var stripped = 0;
        foreach (var entry in catalogRepository.GetAll())
        {
            var assigned = ReadTagIds(entry);
            var kept = assigned.Where(known.Contains).ToArray();
            if (kept.Length == assigned.Count)
            {
                continue;
            }

            stripped += assigned.Count - kept.Length;
            WriteEntryTags(entry.Id, kept);
        }

        return stripped;
    }

    /// <summary>
    /// Migrates existing catalogs to tag storage without changing the source hierarchy:
    /// entries missing the <c>Tags</c> property gain an empty sorted assignment list.
    /// Returns the number of entries normalized.
    /// </summary>
    public int NormalizeAllEntries()
    {
        var normalized = 0;
        foreach (var entry in catalogRepository.GetAll())
        {
            // Missing Tags properties already read as empty and are left untouched,
            // so migration never dirties an otherwise clean catalog.
            if (!entry.Properties.TryGetValue(TagsPropertyKey, out var raw) || raw is null)
            {
                continue;
            }

            var current = ReadTagIds(entry);
            if (raw is not List<string> exact || !exact.SequenceEqual(current, StringComparer.Ordinal))
            {
                WriteEntryTags(entry.Id, current.ToArray());
                normalized++;
            }
        }

        return normalized;
    }

    private IReadOnlyList<string> NormalizeTagIds(IEnumerable<string> tagIds)
    {
        ArgumentNullException.ThrowIfNull(tagIds);
        var known = new HashSet<string>(
            tagRepository.GetAll().Select(static tag => tag.Id),
            StringComparer.Ordinal);
        var normalized = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var tagId in tagIds)
        {
            if (string.IsNullOrWhiteSpace(tagId))
            {
                continue;
            }

            if (!known.Contains(tagId))
            {
                throw new TagNotFoundException(tagId);
            }

            normalized.Add(tagId);
        }

        return normalized.ToArray();
    }

    private static IReadOnlyList<string> ReadTagIds(CatalogDocument entry)
    {
        if (!entry.Properties.TryGetValue(TagsPropertyKey, out var raw) || raw is null)
        {
            return [];
        }

        if (raw is IEnumerable<string> ids)
        {
            return NormalizeIds(ids);
        }

        if (raw is System.Collections.IEnumerable values)
        {
            return NormalizeIds(values.Cast<object?>()
                .Select(value => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)));
        }

        return [];
    }

    private static IReadOnlyList<string> NormalizeIds(IEnumerable<string?> ids)
    {
        return ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    private void WriteEntryTags(string entryId, IReadOnlyList<string> tagIds)
    {
        var entry = catalogRepository.Get(entryId);
        if (entry is null)
        {
            return;
        }

        var properties = new PropertiesCollection(entry.Properties)
        {
            [TagsPropertyKey] = tagIds.ToList()
        };
        catalogRepository.Save(entry.Id, new CatalogDocument
        {
            Id = entry.Id,
            Name = entry.Name,
            ParentId = entry.ParentId,
            Type = entry.Type,
            Size = entry.Size,
            ChildrenCount = entry.ChildrenCount,
            ContentsState = entry.ContentsState,
            Status = entry.Status,
            Properties = properties
        });
    }
}
