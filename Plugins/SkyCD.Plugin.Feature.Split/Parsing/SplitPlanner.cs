using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Feature.Split.Exceptions;
using SkyCD.Plugin.Feature.Split.Models;
using SkyCD.SDK.Capabilities.Catalog;
using SkyCD.SDK.Capabilities.FileFormats;

namespace SkyCD.Plugin.Feature.Split.Parsing;

/// <summary>
/// Cuts the entries of the open catalog into part catalogs at entry boundaries and writes each part through
/// the host save surface, so every part reopens as a valid catalog in the chosen format. A cancelled or failed
/// run removes the parts it wrote, and an existing target stops the run before anything is written.
/// </summary>
public sealed class SplitPlanner(ICatalogSaveHostApi saveHost)
{
    /// <summary>
    /// Assigns entries and part file names to parts, honouring both limits at entry boundaries. Parts wider
    /// than the size cap are subdivided until they fit or hold a single entry, so a single large entry alone
    /// may still exceed the cap. The measurer serializes one candidate part and reports its size.
    /// </summary>
    public async Task<IReadOnlyList<SplitPartPlan>> PlanAsync(
        IReadOnlyList<CatalogEntry> entries,
        SplitRequest request,
        Func<string, IReadOnlyList<CatalogEntry>, CancellationToken, Task<long>>? measureBytesAsync = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(request);
        if (request.MaxEntriesPerPart <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request),
                "Split planning needs a positive maximum entry count.");
        }

        var pattern = SplitPattern.Normalize(request.FileNamePattern);
        var measure = measureBytesAsync ?? ((name, slice, token) =>
            MeasureAsync(request.FormatId, name, slice, [], token));
        var chunks = ChunkByCount(entries.Count, request.MaxEntriesPerPart)
            .Select(chunk => (chunk.FirstEntry, chunk.EntryCount, Bytes: 0L)).ToList();
        if (request.MaxPartSizeBytes > 0)
        {
            chunks = await EnforceSizeCapAsync(entries, chunks, pattern, request, measure, cancellationToken)
                .ConfigureAwait(false);
        }

        var plans = new List<SplitPartPlan>(chunks.Count);
        for (var index = 0; index < chunks.Count; index++)
        {
            var (first, count, bytes) = chunks[index];
            plans.Add(new SplitPartPlan(index + 1,
                SplitPattern.Build(pattern, index + 1, chunks.Count, request.SourceName),
                first, count, bytes));
        }

        return plans;
    }

    /// <summary>
    /// Writes every planned part, publishing each one only after it is complete. The data-loss warning for
    /// the chosen format is confirmed once for the whole run, not once per part. Declining it, cancelling,
    /// or a failure removes the parts written by this run.
    /// </summary>
    public async Task<SplitResult> SplitAsync(
        IReadOnlyList<CatalogEntry> entries,
        SplitRequest request,
        Func<SplitDataLossPreview, Task<bool>> confirmLoss,
        IProgress<SplitProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(confirmLoss);
        if (entries.Count == 0)
        {
            throw new ArgumentException("Splitting needs at least one catalog entry.", nameof(entries));
        }

        var outputFolder = Path.GetFullPath(request.OutputFolder);
        Directory.CreateDirectory(outputFolder);
        var catalogScopeItems = saveHost.CollectCatalogScopeItems();
        var plans = await PlanAsync(entries, request,
                (name, slice, token) => MeasureAsync(request.FormatId, name, slice, catalogScopeItems, token),
                cancellationToken)
            .ConfigureAwait(false);

        var loss = saveHost.AssessDataLoss(request.FormatId, plans[0].FileName, entries, catalogScopeItems);
        if (loss.Count > 0)
        {
            var displayName = saveHost.GetRewritableFormats()
                .FirstOrDefault(format =>
                    format.FormatId.Equals(request.FormatId, StringComparison.OrdinalIgnoreCase))
                ?.DisplayName ?? request.FormatId;
            if (!await confirmLoss(new SplitDataLossPreview(displayName, loss)).ConfigureAwait(false))
            {
                throw new OperationCanceledException("The split was declined at the data-loss confirmation.");
            }
        }

        var targets = new List<string>(plans.Count);
        foreach (var plan in plans)
        {
            var target = Path.GetFullPath(Path.Combine(outputFolder, plan.FileName));
            if (!IsInside(target, outputFolder))
            {
                throw new SplitPatternException(request.FileNamePattern,
                    "it addresses a location outside the output folder");
            }

            if (File.Exists(target))
            {
                throw new SplitOutputConflictException(target);
            }

            targets.Add(target);
        }

        var written = new List<string>();
        try
        {
            var parts = new List<SplitPart>(plans.Count);
            var entriesWritten = 0;
            for (var index = 0; index < plans.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var plan = plans[index];
                var slice = entries.Skip(plan.FirstEntry).Take(plan.EntryCount).ToArray();
                await saveHost.WriteFileAsync(request.FormatId, targets[index], slice, catalogScopeItems,
                    cancellationToken).ConfigureAwait(false);
                written.Add(targets[index]);
                entriesWritten += plan.EntryCount;
                parts.Add(new SplitPart(targets[index], plan.EntryCount,
                    new FileInfo(targets[index]).Length));
                progress?.Report(new SplitProgress(parts.Count, plans.Count, entriesWritten, entries.Count));
            }

            return new SplitResult(parts, entriesWritten);
        }
        catch (Exception)
        {
            Rollback(written);
            throw;
        }
    }

    private static List<(int FirstEntry, int EntryCount)> ChunkByCount(int totalEntries, int maxEntries)
    {
        var chunks = new List<(int, int)>();
        for (var first = 0; first < totalEntries; first += maxEntries)
        {
            chunks.Add((first, Math.Min(maxEntries, totalEntries - first)));
        }

        return chunks;
    }

    private async Task<List<(int FirstEntry, int EntryCount, long Bytes)>> EnforceSizeCapAsync(
        IReadOnlyList<CatalogEntry> entries,
        List<(int FirstEntry, int EntryCount, long Bytes)> chunks,
        string pattern,
        SplitRequest request,
        Func<string, IReadOnlyList<CatalogEntry>, CancellationToken, Task<long>> measure,
        CancellationToken cancellationToken)
    {
        var current = chunks.Select(chunk => (chunk.FirstEntry, chunk.EntryCount, 0L)).ToList();
        while (true)
        {
            var names = Rename(current, pattern, request.SourceName);
            var subdivided = false;
            var next = new List<(int, int, long)>(current.Count);
            for (var index = 0; index < current.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (first, count, _) = current[index];
                if (count > 1)
                {
                    var slice = entries.Skip(first).Take(count).ToArray();
                    var bytes = await measure(names[index], slice, cancellationToken).ConfigureAwait(false);
                    if (bytes > request.MaxPartSizeBytes)
                    {
                        var half = count / 2;
                        next.Add((first, half, 0L));
                        next.Add((first + half, count - half, 0L));
                        subdivided = true;
                        continue;
                    }

                    next.Add((first, count, bytes));
                    continue;
                }

                next.Add((first, count, 0L));
            }

            current = next;
            if (!subdivided)
            {
                return current;
            }
        }
    }

    private static List<string> Rename(
        IReadOnlyList<(int FirstEntry, int EntryCount, long Bytes)> chunks, string pattern, string sourceName)
    {
        var names = new List<string>(chunks.Count);
        for (var index = 0; index < chunks.Count; index++)
        {
            names.Add(SplitPattern.Build(pattern, index + 1, chunks.Count, sourceName));
        }

        return names;
    }

    private async Task<long> MeasureAsync(
        string formatId,
        string fileName,
        IReadOnlyList<CatalogEntry> slice,
        IReadOnlyList<FileFormatDataLossItem> catalogScopeItems,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await saveHost.WriteAsync(formatId, fileName, buffer, slice, catalogScopeItems, cancellationToken)
            .ConfigureAwait(false);
        return buffer.Length;
    }

    private static bool IsInside(string path, string folder)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var root = folder.EndsWith(Path.DirectorySeparatorChar) ? folder : folder + Path.DirectorySeparatorChar;
        return path.StartsWith(root, comparison);
    }

    private static void Rollback(List<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Cleanup must not replace the cancellation or failure that triggered it.
            }
        }
    }
}
