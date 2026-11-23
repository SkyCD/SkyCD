using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;

namespace SkyCD.Presentation.Staging;

public sealed class CatalogEntryMerger
{
    /// <summary>
    /// Merges staged scanned documents into the existing catalog document set.
    /// Preserves stable existing document IDs and user-managed metadata (Comments, Tags, Rating, Status, Custom Properties).
    /// </summary>
    public IReadOnlyList<CatalogDocument> Merge(
        IReadOnlyList<CatalogDocument> stagedDocuments,
        IReadOnlyList<CatalogDocument> existingDocuments,
        bool preserveUserMetadata = true,
        bool isIncomplete = false,
        string? errorSummary = null)
    {
        ArgumentNullException.ThrowIfNull(stagedDocuments);
        ArgumentNullException.ThrowIfNull(existingDocuments);

        if (stagedDocuments.Count == 0)
        {
            return [];
        }

        if (existingDocuments.Count == 0 && !isIncomplete)
        {
            return stagedDocuments;
        }

        // Map existing documents by (ParentId, Name) and Id
        var existingByParentAndName = new Dictionary<(string? ParentId, string Name), CatalogDocument>();
        var existingById = existingDocuments.ToDictionary(d => d.Id, StringComparer.Ordinal);

        foreach (var doc in existingDocuments)
        {
            var key = (doc.ParentId, doc.Name);
            existingByParentAndName.TryAdd(key, doc);
        }

        // Map old staged ID -> resolved (existing or preserved) ID
        var idRemap = new Dictionary<string, string>(StringComparer.Ordinal);
        var mergedResult = new List<CatalogDocument>(stagedDocuments.Count);

        foreach (var staged in stagedDocuments)
        {
            // Resolve ParentId if parent was remapped
            var effectiveParentId = staged.ParentId;
            if (effectiveParentId is not null && idRemap.TryGetValue(effectiveParentId, out var remappedParentId))
            {
                effectiveParentId = remappedParentId;
            }

            var effectiveState = isIncomplete
                ? CatalogContentsState.Incomplete
                : staged.ContentsState;

            var matchKey = (effectiveParentId, staged.Name);
            if (existingByParentAndName.TryGetValue(matchKey, out var existing))
            {
                idRemap[staged.Id] = existing.Id;

                var props = MergeProperties(staged.Properties, existing.Properties, preserveUserMetadata);
                if (isIncomplete)
                {
                    props["ContentsState"] = CatalogContentsState.Incomplete.ToString();
                    if (!string.IsNullOrEmpty(errorSummary))
                    {
                        props["ScanErrorSummary"] = errorSummary;
                    }
                }

                var merged = new CatalogDocument
                {
                    Id = existing.Id,
                    Name = staged.Name,
                    ParentId = effectiveParentId,
                    Type = staged.Type,
                    Size = staged.Size,
                    ChildrenCount = staged.ChildrenCount,
                    ContentsState = effectiveState != CatalogContentsState.Known ? effectiveState : existing.ContentsState,
                    Status = preserveUserMetadata ? (existing.Status ?? staged.Status) : staged.Status,
                    Properties = props
                };

                mergedResult.Add(merged);
            }
            else
            {
                idRemap[staged.Id] = staged.Id;

                var props = new PropertiesCollection(staged.Properties);
                if (isIncomplete)
                {
                    props["ContentsState"] = CatalogContentsState.Incomplete.ToString();
                    if (!string.IsNullOrEmpty(errorSummary))
                    {
                        props["ScanErrorSummary"] = errorSummary;
                    }
                }

                var copy = new CatalogDocument
                {
                    Id = staged.Id,
                    Name = staged.Name,
                    ParentId = effectiveParentId,
                    Type = staged.Type,
                    Size = staged.Size,
                    ChildrenCount = staged.ChildrenCount,
                    ContentsState = effectiveState,
                    Status = staged.Status,
                    Properties = props
                };

                mergedResult.Add(copy);
            }
        }

        return mergedResult;
    }

    private static PropertiesCollection MergeProperties(
        PropertiesCollection stagedProps,
        PropertiesCollection existingProps,
        bool preserveUserMetadata)
    {
        var result = new PropertiesCollection(stagedProps);

        if (preserveUserMetadata)
        {
            foreach (var (key, value) in existingProps)
            {
                // SizeKnown describes the measured size, not user data: a rescan that produced a size must
                // clear an earlier unknown marker instead of inheriting it.
                if (string.Equals(key, CatalogDocument.SizeKnownPropertyKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // If existing has a custom user property that scanner didn't populate, preserve it
                if (!result.ContainsKey(key))
                {
                    result[key] = value;
                }
            }
        }

        return result;
    }
}
