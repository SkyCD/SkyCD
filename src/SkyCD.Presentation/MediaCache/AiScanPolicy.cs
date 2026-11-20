using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Documents;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Decides whether a file may be sent to the AI description provider. Files whose MIME type is excluded
/// or whose size exceeds the configured limit are skipped.
/// </summary>
public sealed class AiScanPolicy
{
    /// <summary>Default maximum file size for AI scanning: 1 GiB.</summary>
    public const long DefaultMaxFileSizeBytes = 1024L * 1024L * 1024L;

    /// <summary>Policy without exclusions and without a size limit.</summary>
    public static AiScanPolicy Unrestricted { get; } = new([], long.MaxValue);

    private readonly HashSet<string> excludedMimeTypes;

    public AiScanPolicy(IEnumerable<string>? excludedMimeTypes, long maxFileSizeBytes)
    {
        this.excludedMimeTypes = new HashSet<string>(
            (excludedMimeTypes ?? [])
                .Where(static mimeType => !string.IsNullOrWhiteSpace(mimeType))
                .Select(static mimeType => mimeType.Trim()),
            StringComparer.OrdinalIgnoreCase);
        MaxFileSizeBytes = maxFileSizeBytes > 0 ? maxFileSizeBytes : DefaultMaxFileSizeBytes;
    }

    public long MaxFileSizeBytes { get; }

    public IReadOnlyCollection<string> ExcludedMimeTypes => excludedMimeTypes;

    /// <summary>Returns true when a file of the given size and MIME type may be scanned with AI.</summary>
    public bool ShouldScan(long size, string? mimeType)
    {
        if (size > MaxFileSizeBytes)
        {
            return false;
        }

        return mimeType is null || !excludedMimeTypes.Contains(mimeType.Trim());
    }

    /// <summary>Returns true when the catalog entry may be scanned with AI.</summary>
    public bool ShouldScan(CatalogDocument entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ShouldScan(entry.Size, FileMimeTypeResolver.Resolve(entry));
    }
}
