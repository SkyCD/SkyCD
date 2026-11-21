using System;

namespace SkyCD.Plugin.Arc;

/// <summary>One header and the location of its payload inside an ARC archive.</summary>
internal sealed record ArcEntry(
    string Name,
    ArcMethod Method,
    byte RawMethod,
    long CompressedSize,
    long UncompressedSize,
    long DataOffset,
    bool IsDirectory,
    int CrcBits,
    long Crc,
    bool HasExtendedDate,
    DateTimeOffset? ModifiedUtc);
