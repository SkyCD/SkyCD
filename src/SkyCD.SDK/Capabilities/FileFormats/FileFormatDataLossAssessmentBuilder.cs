using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyCD.SDK.Capabilities.FileFormats;

/// <summary>
/// Builds loss items describing which snapshot entries carry data a format cannot represent.
/// </summary>
public static class FileFormatDataLossAssessmentBuilder
{
    private const int SampleSize = 3;

    /// <summary>
    /// Reports every entry field outside <paramref name="represented"/> that is present in the
    /// snapshot, with the number of affected entries and a short sample.
    /// </summary>
    public static IReadOnlyList<FileFormatDataLossItem> AssessEntries(
        FileFormatDataLossRequest request,
        FileFormatEntryFieldSupport represented)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Entries);

        var items = new List<FileFormatDataLossItem>();
        var missing = FileFormatEntryFieldSupport.All & ~represented;
        if (missing == FileFormatEntryFieldSupport.None || request.Entries.Count == 0)
        {
            return items;
        }

        if (missing.HasFlag(FileFormatEntryFieldSupport.Id))
        {
            AddPresenceItem(items, request.Entries, FileFormatDataLossFields.Id, static entry => true);
        }

        if (missing.HasFlag(FileFormatEntryFieldSupport.ParentId))
        {
            AddPresenceItem(items, request.Entries, FileFormatDataLossFields.ParentId,
                static entry => !string.IsNullOrEmpty(entry.ParentId));
        }

        if (missing.HasFlag(FileFormatEntryFieldSupport.Kind))
        {
            AddPresenceItem(items, request.Entries, FileFormatDataLossFields.Kind, static entry => true);
        }

        if (missing.HasFlag(FileFormatEntryFieldSupport.Size))
        {
            AddPresenceItem(items, request.Entries, FileFormatDataLossFields.Size, static entry => entry.Size > 0);
        }

        if (missing.HasFlag(FileFormatEntryFieldSupport.Status))
        {
            AddPresenceItem(items, request.Entries, FileFormatDataLossFields.Status,
                static entry => !string.IsNullOrWhiteSpace(entry.Status));
        }

        if (missing.HasFlag(FileFormatEntryFieldSupport.Properties))
        {
            var keys = request.Entries
                .SelectMany(static entry => entry.Properties.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
            {
                var affected = request.Entries
                    .Where(entry => entry.Properties.ContainsKey(key))
                    .ToList();
                items.Add(new FileFormatDataLossItem(
                    FileFormatDataLossCategories.Entries,
                    FileFormatDataLossFields.PropertyKeyPrefix + key,
                    affected.Count,
                    Sample(affected)));
            }
        }

        return items;
    }

    private static void AddPresenceItem(
        List<FileFormatDataLossItem> items,
        IReadOnlyList<FileFormatDataLossEntry> entries,
        string name,
        Func<FileFormatDataLossEntry, bool> predicate)
    {
        var affected = entries.Where(predicate).ToList();
        if (affected.Count == 0)
        {
            return;
        }

        items.Add(new FileFormatDataLossItem(FileFormatDataLossCategories.Entries, name, affected.Count,
            Sample(affected)));
    }

    private static string? Sample(IReadOnlyList<FileFormatDataLossEntry> entries)
    {
        var names = entries
            .Select(static entry => entry.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Take(SampleSize)
            .ToList();
        return names.Count == 0 ? null : string.Join(", ", names);
    }
}
