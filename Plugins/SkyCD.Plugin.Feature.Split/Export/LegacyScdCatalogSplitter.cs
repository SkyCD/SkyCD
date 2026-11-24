using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Feature.Split.Exceptions;
using SkyCD.Plugin.Feature.Split.Models;
using SkyCD.Plugin.Feature.Split.Parsing;
using SkyCD.Plugin.FileFormat.LegacySkyCD;
using SkyCD.Plugin.FileFormat.LegacySkyCD.Models;

namespace SkyCD.Plugin.Feature.Split.Export;

/// <summary>
/// Cuts a legacy text catalog into part catalogs at entry boundaries and writes them through the
/// <c>legacy-scd</c> format, so every part reopens as a valid catalog.
/// </summary>
public static class LegacyScdCatalogSplitter
{
    private const int Utf8PreambleBytes = 3;
    private const string PendingSuffix = ".skycd-split-pending";

    /// <summary>Assigns entries and part file names to parts, honouring both limits at entry boundaries.</summary>
    public static IReadOnlyList<SplitPartPlan> Plan(LegacyTextCatalog catalog, SplitRequest request)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(request);
        var pattern = SplitPattern.Normalize(request.FileNamePattern);
        var maxEntries = request.MaxEntriesPerPart;
        var maxBytes = request.MaxPartSizeBytes;
        if (maxEntries < 0 || maxBytes < 0 || (maxEntries == 0 && maxBytes == 0))
        {
            throw new SplitLimitException();
        }

        var groups = new List<SplitPartPlan>();
        var first = 0;
        var contentBytes = 0L;
        for (var index = 0; index < catalog.Entries.Count; index++)
        {
            var lineBytes = catalog.LineBytes[index];
            var exceedsEntries = maxEntries > 0 && index - first + 1 > maxEntries;
            var exceedsBytes = maxBytes > 0 && index > first
                && Utf8PreambleBytes + contentBytes + lineBytes > maxBytes;
            if (index > first && (exceedsEntries || exceedsBytes))
            {
                groups.Add(new SplitPartPlan(groups.Count + 1, string.Empty, first, index - first,
                    Utf8PreambleBytes + contentBytes));
                first = index;
                contentBytes = 0;
            }

            contentBytes += lineBytes;
        }

        if (catalog.Entries.Count > first)
        {
            groups.Add(new SplitPartPlan(groups.Count + 1, string.Empty, first, catalog.Entries.Count - first,
                Utf8PreambleBytes + contentBytes));
        }

        var sourceName = Path.GetFileNameWithoutExtension(request.SourcePath);
        var plans = new List<SplitPartPlan>(groups.Count);
        foreach (var group in groups)
        {
            plans.Add(group with
            {
                FileName = SplitPattern.Build(pattern, group.PartNumber, groups.Count, sourceName)
            });
        }

        return plans;
    }

    /// <summary>
    /// Writes every planned part, publishing each one only after it is complete. A cancellation or a failure
    /// removes the parts written by this run, so no part is left behind that looks like a finished split.
    /// </summary>
    public static async Task<SplitResult> SplitAsync(LegacyTextCatalog catalog, SplitRequest request,
        IProgress<SplitProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputFolder);
        var plans = Plan(catalog, request);
        var outputFolder = Path.GetFullPath(request.OutputFolder);
        Directory.CreateDirectory(outputFolder);
        var plugin = new LegacyScdPlugin();
        var formatId = plugin.SupportedFormat.FormatId;
        var written = new List<string>();
        try
        {
            var parts = new List<SplitPart>(plans.Count);
            var entriesWritten = 0;
            foreach (var plan in plans)
            {
                cancellationToken.ThrowIfCancellationRequested();
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

                var pending = target + PendingSuffix;
                try
                {
                    await WritePartAsync(plugin, formatId, Slice(catalog, plan), pending, plan.FileName,
                        cancellationToken).ConfigureAwait(false);
                    File.Move(pending, target);
                    written.Add(target);
                }
                finally
                {
                    File.Delete(pending);
                }

                entriesWritten += plan.EntryCount;
                parts.Add(new SplitPart(target, plan.EntryCount, plan.Bytes));
                progress?.Report(new SplitProgress(parts.Count, plans.Count, entriesWritten, catalog.EntryCount));
            }

            return new SplitResult(parts, entriesWritten);
        }
        catch (Exception)
        {
            Rollback(written);
            throw;
        }
    }

    private static LegacyScdCatalog Slice(LegacyTextCatalog catalog, SplitPartPlan plan)
    {
        var part = new LegacyScdCatalog();
        for (var index = plan.FirstEntry; index < plan.FirstEntry + plan.EntryCount; index++)
        {
            part.Entries.Add(catalog.Entries[index]);
        }

        return part;
    }

    private static async Task WritePartAsync(LegacyScdPlugin plugin, string formatId, LegacyScdCatalog part,
        string pendingPath, string fileName, CancellationToken cancellationToken)
    {
        File.Delete(pendingPath);
        await using var target = new FileStream(pendingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var result = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            Target = target,
            FormatId = formatId,
            FileName = fileName,
            Payload = part
        }, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            throw new LegacyCatalogWriteException(fileName, result.Error ?? "the writer reported no reason");
        }

        await target.FlushAsync(cancellationToken).ConfigureAwait(false);
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
