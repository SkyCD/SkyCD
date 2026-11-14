using System;

namespace SkyCD.Plugin.CdCat.Hcf.Exceptions;

/// <summary>
/// Thrown when a CdCat catalog file is corrupted, unreadable, or not a valid gzip/XML structure.
/// </summary>
public sealed class CdCatCorruptFileException(string? reason = null)
    : InvalidOperationException(
        string.IsNullOrWhiteSpace(reason)
            ? "CdCat catalog file is corrupt or unreadable."
            : $"CdCat catalog file is corrupt or unreadable: {reason}");
