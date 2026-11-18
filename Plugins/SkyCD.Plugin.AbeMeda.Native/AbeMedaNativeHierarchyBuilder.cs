using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SkyCD.Plugin.AbeMeda.Native;

/// <summary>
/// Builds the import rows for the verified container layer: one media node for
/// the catalog, plus an explicitly unassociated folder for embedded artifact
/// records. Item hierarchy and item-level metadata stay undecoded because their
/// layout has not been verified against genuine fixtures.
/// </summary>
public static class AbeMedaNativeHierarchyBuilder
{
    public const string ArtifactFolderName = "Embedded artifacts (unassociated)";
    private const int MaxHandlerBuckets = 256;
    private const int MaxHandlerPropertyLength = 2_000;
    private const int MaxWarningPropertyLength = 2_000;

    public static AbeMedaNativeReportDocument Build(AbeMedaNativeCatalog catalog, string? fileStem)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var mediaName = string.IsNullOrWhiteSpace(fileStem) ? "abeMeda Catalog" : fileStem.Trim();
        var summary = CreateSummary(catalog);
        var handlerCounts = CountHandlers(catalog.Entries, summary);
        var rows = new List<Dictionary<string, object?>>();

        var mediaId = $"media:{mediaName}";
        rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = mediaId,
            ["name"] = mediaName,
            ["parentId"] = null,
            ["type"] = "media",
            ["size"] = 0L,
            ["childrenCount"] = catalog.Entries.Count > 0 ? 1L : 0L,
            ["properties"] = CreateMediaProperties(catalog, summary, handlerCounts)
        });

        if (catalog.Entries.Count == 0)
        {
            return new AbeMedaNativeReportDocument(rows, summary);
        }

        var artifactFolderId = $"folder:{mediaName}/{ArtifactFolderName}";
        rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = artifactFolderId,
            ["name"] = ArtifactFolderName,
            ["parentId"] = mediaId,
            ["type"] = "folder",
            ["size"] = 0L,
            ["childrenCount"] = (long)catalog.Entries.Count,
            ["properties"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["AbeMedaNativeUnassociated"] = "true",
                ["AbeMedaNativeArtifactCount"] = catalog.Entries.Count.ToString(CultureInfo.InvariantCulture)
            }
        });

        foreach (var entry in catalog.Entries)
        {
            var ordinal = entry.Ordinal.ToString(CultureInfo.InvariantCulture);
            rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = $"file:{mediaName}/{ArtifactFolderName}/{ordinal}",
                ["name"] = $"Artifact {ordinal}",
                ["parentId"] = artifactFolderId,
                ["type"] = "file",
                ["size"] = 0L,
                ["childrenCount"] = 0L,
                ["properties"] = CreateArtifactProperties(entry)
            });
        }

        return new AbeMedaNativeReportDocument(rows, summary);
    }

    private static AbeMedaNativeImportSummary CreateSummary(AbeMedaNativeCatalog catalog)
    {
        var summary = new AbeMedaNativeImportSummary
        {
            Generation = catalog.Generation.ToString(),
            Version = catalog.Header.Version,
            CompatibilityVersion = catalog.Header.CompatibilityVersion,
            FileLength = catalog.FileLength,
            HeaderSize = catalog.Header.HeaderSize,
            SectionOffset = catalog.Header.SectionOffset,
            SectionLength = catalog.Header.SectionLength,
            BlockSize = catalog.Header.BlockSize,
            DeclaredEntryCount = catalog.Header.EntryCount,
            ImportedEntryCount = catalog.Entries.Count,
            SkippedEntryCount = catalog.SkippedEntryCount
        };

        summary.Warnings.AddRange(catalog.Warnings);
        summary.Notes.Add(
            "Item hierarchy, names, annotations, and extracted media metadata are not decoded in this version.");
        summary.Notes.Add(
            "Embedded artifact records are imported into an unassociated folder; item association is not yet known.");
        summary.Notes.Add(
            "The integrity field is preserved verbatim and is not verified as a checksum.");

        if (catalog.SkippedEntryCount > 0)
        {
            summary.Notes.Add($"{catalog.SkippedEntryCount} malformed record(s) were skipped.");
        }

        return summary;
    }

    private static Dictionary<string, int> CountHandlers(
        IReadOnlyList<AbeMedaNativeEntry> entries,
        AbeMedaNativeImportSummary summary)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var other = 0;

        foreach (var entry in entries)
        {
            var key = entry.Handler.Length == 0 ? "(none)" : entry.Handler;
            if (counts.TryGetValue(key, out var count))
            {
                counts[key] = count + 1;
            }
            else if (counts.Count < MaxHandlerBuckets)
            {
                counts[key] = 1;
            }
            else
            {
                other++;
            }
        }

        if (other > 0)
        {
            counts["(other)"] = other;
        }

        foreach (var (key, value) in counts)
        {
            summary.HandlerCounts[key] = value;
        }

        return counts;
    }

    private static Dictionary<string, object?> CreateMediaProperties(
        AbeMedaNativeCatalog catalog,
        AbeMedaNativeImportSummary summary,
        Dictionary<string, int> handlerCounts)
    {
        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["AbeMedaNativeSource"] = "native",
            ["AbeMedaNativeGeneration"] = catalog.Generation.ToString(),
            ["AbeMedaNativeVersion"] = catalog.Header.Version.ToString(CultureInfo.InvariantCulture),
            ["AbeMedaNativeCompatibilityVersion"] =
                catalog.Header.CompatibilityVersion.ToString(CultureInfo.InvariantCulture),
            ["AbeMedaNativeIntegrity"] = $"0x{catalog.Header.Integrity:X8}",
            ["AbeMedaNativeHeaderSize"] = catalog.Header.HeaderSize.ToString(CultureInfo.InvariantCulture),
            ["AbeMedaNativeEntryStride"] = catalog.Header.EntryStride.ToString(CultureInfo.InvariantCulture),
            ["AbeMedaNativeSectionOffset"] = $"0x{catalog.Header.SectionOffset:X8}",
            ["AbeMedaNativeSectionLength"] = catalog.Header.SectionLength.ToString(CultureInfo.InvariantCulture),
            ["AbeMedaNativeBlockSize"] = catalog.Header.BlockSize.ToString(CultureInfo.InvariantCulture),
            ["AbeMedaNativeDeclaredEntryCount"] =
                catalog.Header.EntryCount.ToString(CultureInfo.InvariantCulture),
            ["AbeMedaNativeImportedEntryCount"] =
                catalog.Entries.Count.ToString(CultureInfo.InvariantCulture),
            ["AbeMedaNativeSkippedEntryCount"] =
                catalog.SkippedEntryCount.ToString(CultureInfo.InvariantCulture),
            ["AbeMedaNativeWarningCount"] = summary.WarningCount.ToString(CultureInfo.InvariantCulture),
            ["AbeMedaNativeArtifactHandlers"] = FormatHandlerCounts(handlerCounts),
            ["AbeMedaNativeUnknown.0x2C"] = $"0x{catalog.Header.Unknown2C:X8}",
            ["AbeMedaNativeUnknown.0x30"] = $"0x{catalog.Header.Unknown30:X8}",
            ["AbeMedaNativeUnknown.0x34"] = $"0x{catalog.Header.Unknown34:X8}",
            ["AbeMedaNativeUnknown.0x38"] = $"0x{catalog.Header.Unknown38:X8}",
            ["AbeMedaNativeLimitation"] =
                "Item hierarchy, names, and metadata are not decoded; only the container and embedded artifact records are imported."
        };

        if (summary.Warnings.Count > 0)
        {
            properties["AbeMedaNativeWarnings"] = Truncate(string.Join(" | ", summary.Warnings),
                MaxWarningPropertyLength);
        }

        return properties;
    }

    private static Dictionary<string, object?> CreateArtifactProperties(AbeMedaNativeEntry entry)
    {
        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["AbeMedaNativeArtifact"] = "true",
            ["AbeMedaNativeUnassociated"] = "true",
            ["AbeMedaNativeOrdinal"] = entry.Ordinal.ToString(CultureInfo.InvariantCulture),
            ["AbeMedaNativeBlobValue"] = entry.BlobValue.ToString(CultureInfo.InvariantCulture),
            ["AbeMedaNativeFlags"] = $"0x{entry.Flags:X8}",
            ["AbeMedaNativeHandler"] = entry.Handler,
            ["AbeMedaNativeHandlerIsPrintable"] = entry.HandlerIsPrintable ? "true" : "false",
            ["AbeMedaNativeRangeA"] = $"0x{entry.RangeA:X8}",
            ["AbeMedaNativeRangeB"] = $"0x{entry.RangeB:X8}",
            ["AbeMedaNativeRangesWithinFile"] = entry.RangesWithinFile ? "true" : "false",
            ["AbeMedaNativeRecordOffset"] = $"0x{entry.RecordOffset:X}"
        };

        var mimeType = DetectMimeType(entry.Handler);
        if (mimeType is not null)
        {
            properties["AbeMedaNativeMimeType"] = mimeType;
        }

        return properties;
    }

    private static string? DetectMimeType(string handler)
    {
        if (handler.StartsWith("JPEG", StringComparison.OrdinalIgnoreCase))
        {
            return "image/jpeg";
        }

        if (handler.StartsWith("PNG", StringComparison.OrdinalIgnoreCase))
        {
            return "image/png";
        }

        if (handler.StartsWith("GIF", StringComparison.OrdinalIgnoreCase))
        {
            return "image/gif";
        }

        return null;
    }

    private static string FormatHandlerCounts(Dictionary<string, int> counts)
    {
        if (counts.Count == 0)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        foreach (var (handler, count) in counts
                     .OrderByDescending(pair => pair.Value)
                     .ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (text.Length > 0)
            {
                text.Append("; ");
            }

            text.Append(handler).Append('=').Append(count.ToString(CultureInfo.InvariantCulture));
        }

        return Truncate(text.ToString(), MaxHandlerPropertyLength);
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
