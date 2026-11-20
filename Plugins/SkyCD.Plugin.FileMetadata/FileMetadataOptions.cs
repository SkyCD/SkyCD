using System;

namespace SkyCD.Plugin.FileMetadata;

/// <summary>
/// Extraction limits. Only a bounded prefix is read, because the tags this plugin publishes sit in header
/// segments and early chunks; a 1 MiB cap covers large XMP packets and multi-page caption sets while staying
/// well inside the host's own source budget.
/// </summary>
public sealed class FileMetadataOptions
{
    public const int DefaultMaxScanBytes = 1024 * 1024;

    public FileMetadataOptions(int maxScanBytes = DefaultMaxScanBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxScanBytes, 1024);
        MaxScanBytes = maxScanBytes;
    }

    public static FileMetadataOptions Default { get; } = new();

    public int MaxScanBytes { get; }
}
