using System;

namespace SkyCD.Plugin.Zoo.Models;

/// <summary>One directory entry parsed from a ZOO archive.</summary>
internal sealed record ZooArchiveEntry(
    string Path,
    int PackingMethod,
    long Offset,
    long CompressedBytes,
    long OriginalBytes,
    ushort Crc16,
    DateTimeOffset? ModifiedUtc);
