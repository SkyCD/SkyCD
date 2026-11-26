using System;

namespace SkyCD.Plugin.Metadata.VideoProjects.Models;

/// <summary>
/// Fixed bounds the plugin applies on top of the host's budgets. The host still owns the authoritative
/// source-read and artifact limits; these values stop the plugin from decompressing, reading or walking
/// more of an untrusted project than its facts need.
/// </summary>
public sealed record VideoProjectPluginOptions
{
    public VideoProjectPluginOptions(
        int maxProjectXmlBytes = 64 * 1024 * 1024,
        int maxArchiveEntryBytes = 32 * 1024 * 1024,
        int maxArchiveTotalBytes = 64 * 1024 * 1024,
        int maxArchiveEntries = 10_000,
        int maxSeqContainers = 1024,
        int maxChunks = 100_000,
        int maxTimelineNames = 32,
        int maxNameCharacters = 256)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxProjectXmlBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxArchiveEntryBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxArchiveTotalBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxArchiveEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSeqContainers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChunks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTimelineNames);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxNameCharacters);

        MaxProjectXmlBytes = maxProjectXmlBytes;
        MaxArchiveEntryBytes = maxArchiveEntryBytes;
        MaxArchiveTotalBytes = maxArchiveTotalBytes;
        MaxArchiveEntries = maxArchiveEntries;
        MaxSeqContainers = maxSeqContainers;
        MaxChunks = maxChunks;
        MaxTimelineNames = maxTimelineNames;
        MaxNameCharacters = maxNameCharacters;
    }

    /// <summary>
    /// Decompressed byte bound for Premiere XML. A gzip project can expand far beyond its on-disk size, so the
    /// limit applies after decompression; plain XML uses the same bound directly.
    /// </summary>
    public int MaxProjectXmlBytes { get; }

    /// <summary>Largest archive entry the Resolve reader will inflate, from its declared uncompressed size.</summary>
    public int MaxArchiveEntryBytes { get; }

    /// <summary>Total inflated archive bytes, so many small entries cannot add up to an unbounded read.</summary>
    public int MaxArchiveTotalBytes { get; }

    /// <summary>Entry visit cap, so a pathological ZIP cannot spin the scan.</summary>
    public int MaxArchiveEntries { get; }

    /// <summary>Resolve keeps one <c>SeqContainer</c> document per timeline; only the first few are walked.</summary>
    public int MaxSeqContainers { get; }

    /// <summary>Chunk visit cap for the After Effects big-endian chunk walk.</summary>
    public int MaxChunks { get; }

    /// <summary>How many timeline names are published; the count field still reports the full total.</summary>
    public int MaxTimelineNames { get; }

    /// <summary>Longest timeline or composition name kept after trimming.</summary>
    public int MaxNameCharacters { get; }

    public static VideoProjectPluginOptions Default { get; } = new();
}
