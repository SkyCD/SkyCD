using System;

namespace SkyCD.Plugin.CdCat.Hcf.Exceptions;

/// <summary>
/// Thrown when decompressing a CdCat file exceeds the maximum allowed decompressed size limit.
/// </summary>
public sealed class CdCatDecompressionLimitException(long limitBytes)
    : InvalidOperationException($"Decompressed CdCat stream exceeded maximum allowed limit of {limitBytes} bytes.");
