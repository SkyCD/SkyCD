using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SkyCD.Plugin.Tellico.Models;

namespace SkyCD.Plugin.Tellico.Import;

/// <summary>
/// Maps a parsed Tellico collection onto catalog rows consumed by the normal
/// catalog loader: one media node per collection plus one leaf row per entry.
/// Entries use kind <c>entry</c> (never <c>file</c>): Tellico items are
/// collection objects, not filesystem files, and the loader maps the entry
/// kind explicitly. Node ids embed the collection index and the Tellico
/// entry id, so re-imports map stably.
/// </summary>
public static class TellicoHierarchyBuilder
{
    public static TellicoReportDocument Compose(TellicoParseResult parseResult)
    {
        var rows = new List<Dictionary<string, object?>>();
        var usedNodeIds = new HashSet<string>(StringComparer.Ordinal);
        var totalEntries = 0;
        var totalSkipped = 0;
        var notes = new List<string>();

        for (var collectionIndex = 0; collectionIndex < parseResult.Collections.Count; collectionIndex++)
        {
            var collection = parseResult.Collections[collectionIndex];
            var rootNodeId = MakeUniqueNodeId($"tellico:collection:{collectionIndex}", usedNodeIds);

            var rootProperties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["TellicoCollectionTitle"] = collection.Title,
                ["TellicoCollectionType"] = collection.Type,
            };
            if (!string.IsNullOrWhiteSpace(collection.EntryTitle))
            {
                rootProperties["TellicoEntryTitle"] = collection.EntryTitle;
            }

            if (!string.IsNullOrWhiteSpace(parseResult.SyntaxVersion))
            {
                rootProperties["TellicoSyntaxVersion"] = parseResult.SyntaxVersion;
            }

            var keptIds = collection.Entries.Where(entry => entry is not null)
                .Select(entry => entry!.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unmatchedLoans = collection.Loans.Where(loan => !keptIds.Contains(loan.EntryRef)).ToArray();
            if (unmatchedLoans.Length > 0)
            {
                rootProperties["TellicoUnmappedLoans"] = unmatchedLoans.Select(loan => loan.Uid ?? loan.EntryRef).ToArray();
            }

            var loansByEntry = collection.Loans.ToLookup(loan => loan.EntryRef, StringComparer.OrdinalIgnoreCase);
            rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["nodeId"] = rootNodeId,
                ["parentId"] = null,
                ["kind"] = "media",
                ["name"] = string.IsNullOrWhiteSpace(collection.Title) ? "Tellico Collection" : collection.Title,
                ["sizeBytes"] = 0L,
                ["childrenCount"] = (long)collection.Entries.Count(item => item is not null),
                ["properties"] = rootProperties
            });

            foreach (var item in collection.Entries)
            {
                if (item is null)
                {
                    continue;
                }

                totalEntries++;
                var nodeId = MakeUniqueNodeId(
                    $"tellico:{collectionIndex}:{Sanitize(item.Id)}", usedNodeIds);
                var name = ResolveName(item);
                var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["TellicoId"] = item.Id,
                    ["TellicoCollection"] = collection.Title,
                    ["TellicoCollectionType"] = collection.Type
                };

                foreach (var (field, value) in item.Fields)
                {
                    properties[ToPropertyKey(field)] = value;
                }

                var additionalLoans = loansByEntry[item.Id].Skip(1).ToArray();
                if (additionalLoans.Length > 0)
                {
                    properties["TellicoUnmappedLoans"] = additionalLoans.Select(loan => loan.Uid ?? loan.EntryRef).ToArray();
                }

                foreach (var (key, value) in item.ExtraFields)
                {
                    properties.TryAdd(key, value);
                }

                rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["nodeId"] = nodeId,
                    ["parentId"] = rootNodeId,
                    ["kind"] = "entry",
                    ["name"] = name,
                    ["sizeBytes"] = 0L,
                    ["properties"] = properties
                });
            }

            totalSkipped += collection.SkippedEntriesCount;
            notes.AddRange(collection.UnsupportedFields);
            notes.AddRange(collection.SkippedReasons);
        }

        var summary = new TellicoImportSummary
        {
            Collections = parseResult.Collections.Count,
            Entries = totalEntries,
            SkippedEntries = totalSkipped,
            SyntaxVersion = parseResult.SyntaxVersion
        };
        summary.Notes.AddRange(notes);

        return new TellicoReportDocument(rows, summary);
    }

    private static string ResolveName(TellicoEntry item)
    {
        foreach (var candidate in new[] { "title", "name" })
        {
            if (item.Fields.TryGetValue(candidate, out var value) && value is string text
                && !string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        foreach (var (field, value) in item.Fields)
        {
            if (value is string text && !string.IsNullOrWhiteSpace(text)
                && !field.EndsWith("Normalized", StringComparison.Ordinal)
                && !field.EndsWith("Format", StringComparison.Ordinal)
                && !field.EndsWith("Dimensions", StringComparison.Ordinal))
            {
                return text.Trim();
            }
        }

        return $"Untitled {item.Id}";
    }

    private static string ToPropertyKey(string field)
    {
        if (field.Length == 0)
        {
            return field;
        }

        return char.ToUpperInvariant(field[0]) + field.Substring(1);
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder();
        foreach (var ch in value.Trim())
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.'
                ? ch
                : '-');
        }

        return builder.Length == 0 ? "unnamed" : builder.ToString();
    }

    private static string MakeUniqueNodeId(string candidate, HashSet<string> usedNodeIds)
    {
        var attempt = candidate;
        var suffix = 2;
        while (!usedNodeIds.Add(attempt))
        {
            attempt = $"{candidate}-{suffix}";
            suffix++;
        }

        return attempt;
    }
}
