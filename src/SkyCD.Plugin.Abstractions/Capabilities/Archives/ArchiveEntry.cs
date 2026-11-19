using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Abstractions.Capabilities.Archives;

/// <summary>
/// Identifies whether an archive entry represents a file or a directory.
/// </summary>
public enum ArchiveEntryKind
{
    /// <summary>A file entry.</summary>
    File,

    /// <summary>A directory entry.</summary>
    Directory
}

/// <summary>
/// Explains why an archive entry could not be extracted. <see cref="None"/> means the entry is supported.
/// </summary>
public enum ArchiveEntrySkipReason
{
    /// <summary>The entry is supported.</summary>
    None,

    /// <summary>The entry uses a compression or feature the plugin does not support.</summary>
    Unsupported,

    /// <summary>The entry is encrypted and cannot be read without a password.</summary>
    Encrypted,

    /// <summary>The entry data is damaged or could not be read.</summary>
    Damaged,

    /// <summary>The entry path would escape the extraction directory.</summary>
    UnsafePath,

    /// <summary>The entry exceeds the archive limits.</summary>
    LimitExceeded
}

/// <summary>
/// A single archive entry discovered by inspection. Paths use forward slashes and are relative to the archive root.
/// </summary>
public sealed record ArchiveEntry(
    string Path,
    ArchiveEntryKind Kind,
    long SizeBytes,
    DateTimeOffset? ModifiedUtc,
    ArchiveEntrySkipReason SkipReason = ArchiveEntrySkipReason.None,
    string? Detail = null);

/// <summary>
/// Result payload for archive inspection.
/// </summary>
public sealed class ArchiveInspectionResult
{
    /// <summary>Gets whether the archive could be inspected.</summary>
    public required bool Success { get; init; }

    /// <summary>Gets the discovered entries when inspection succeeds.</summary>
    public IReadOnlyList<ArchiveEntry> Entries { get; init; } = [];

    /// <summary>Gets an optional failure message when inspection fails.</summary>
    public string? Error { get; init; }
}
